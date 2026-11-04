#!/usr/bin/env python3
"""Post-freeze Sedan runtime-input preparer (private unit, after the final freeze).

Runs ONLY after a VALID COMPLETE final six-tile freeze (5 primaries across at least
3 sectors + 2 alternates + the full provenance hash chain) and prepares the real
runtime inputs beside the cache root: sedan-ign-proof.json, preview-textures.json,
sources.txt, prepared EPSG:32631 GeoTIFFs (Float32 DEM + uint8 RGB), and the
atomic preparation/receipt.json + preparation/commands.jsonl chronology ledger.
This is NOT the provider-02 framework and NOT the fresh-cache bootstrap wrapper
(both stay separate future units); completing a preparation here does NOT complete
the WI-R2-01 card.

Chronology guarantee (code order, stated once): the ledger's first event is
freeze_validated_complete, appended AFTER the freeze chain validates and BEFORE
any subprocess is spawned, so code order guarantees freezeVerifiedAt <
firstDemRasterIoAt. NO hash certifies historic blindness; the ledger records the
executed order, nothing more. Metadata/header gdalinfo reads are DISTINCT events
from height reads: every logged invocation carries heightSemantic, and the FIRST
height-semantic command (the DEM gdalwarp) additionally logs dem_rasterio_start.
The final preparation_completed event binds the ledger sha256 into the receipt.

Freeze validation reuses the freeze orchestrator's OWN documented validators
(sedan_lidar_freeze.find_completed_freeze + reverify_completed: tile sizes and
sha256 re-hashes, artifact hashes, canonical digest) and fails closed on any
missing/corrupt/incomplete artifact BEFORE any height-semantic open. The extract
receipt (27 files, 205266077 bytes, per the executed extraction receipt and the
producer pin) is re-verified read-only (receipt shape,
member count, totals, file presence and sizes; per-member bytes were verified by
the extract unit's own receipt and are not re-hashed here). The six LAZ tiles ARE
fully re-hashed through reverify_completed in prepare mode.

Grid rule authority (docs/roadmap/deep-dives/r2-sedan-ground-truth.md section 6
and the parent deep dive section 4/5.1a): origin = exact SW corner E 639841.997 /
N 5506768.179 (EPSG:32631), grown OUTWARD to whole chunks north-east only, 3 x 3
chunks at the 3.048 m lattice (chunk = 128 sample intervals = 390.144 m, 385
lattice points per axis, one-sample read-only apron). Implemented as pure UTM
math on the frozen manifest's map_transform extent origin. Three bound kinds stay
distinct: the playable 1100 m AOI square (KML extent only), the 1170.432 m storage
extent (whole-chunk grid, not playable), and the source-corner bounds (storage
grown by 4.572 m = 1.524 m half-footprint + 3.048 m AreaWeighted kernel reach).
The DEM and RGB warp windows are the source-corner bounds outward-aligned to each
native GSD; NO arbitrary buffer is ever added. Every apron footprint (global
lattice indices -1..385) is verified against the planned windows and against the
ACTUAL post-warp GeoTransform before the manifest is generated. The KML extent is
a pure quadrilateral of the exact projected 1100 m AOI square corners
batch-converted to WGS84 via ONE batched cs2cs call.

Subprocess seam: run_tool() is the ONE runner (exact argv, wall timeout, bounded
stdout/stderr capture with the cap checked during capture); tests replace it with
fakes. Real runs use the installed gdalwarp/gdalinfo/gdal_translate/cs2cs
binaries and never import GDAL bindings. Source rasters enter only through
gdalinfo -json on their EMBEDDED headers (CRS and affines; never TAB-extrapolated
25000 ties). DEM output: Float32, bilinear, native 1 m source GSD preserved (any
GSD change is a STOP requiring recorded engineering justification). RGB output:
uint8, average, native 0.20 m GSD preserved. COMPRESS=NONE per the card. NoData
policy: nearest-valid with noDataSearchRadiusMeters EXACTLY 3.048 (one height
lattice) reported to the consumer with the edge-fail policy; the radius is a
pinned constant, never silently widened, never the 25.0 example placeholder. If a
primary control falls on post-fill DEM NoData the manifest generation REFUSES
with an explicit error (the workflow report must detect it; never quietly passes).

RGB control expected values: the controls sit at the quarter and three-quarter
centres of the generated 0.3048 m imagery grid (global pixel indices N//4 and 3N//4,
N = 3840 per axis). Each expected byte is the GDAL area average of the operator-input
prepared 0.2 m RGB over that 0.3048 m footprint, converted with Core rounding. The
expectation is two-stage-equivalent over the prepared raster, which is itself the raw
JP2 warp product (one filter plus Byte quantization); the raw JP2 mosaic and warp stay
cited and hash-bound, and a later raw-source comparison shot validates preparation
separately. Core bilinear weights are 1 at ideal centres; tiny PROJ/float error is
covered by the fixed 2-byte tolerance.

Modes: default = READ-ONLY preflight (validates the freeze chain and the extract
receipt, prints the plan, writes NOTHING and spawns NOTHING); --prepare = the
explicit guarded write operation (own unique stage dir + STARTED marker + O_EXCL
lock under <root>/preparation/, declared-space guard BEFORE any write, idempotent
verify-and-skip on a completed receipt, failures preserve the stage dir + ledger
and never overwrite operator files). Exit codes: 0 success; 2 guard failure
(space, mount, refusal); 3 subprocess failure or timeout; 4 state, collision, or
drift failure. Python 3 standard library only; no RNG; offline.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import re
import selectors
import stat
import subprocess
import sys
import time
from datetime import datetime
from types import SimpleNamespace

# Canonical stdlib-only siblings: the selection/freeze science module owns the
# canonical digest + protocol constants (reused verbatim, never re-implemented);
# the freeze orchestrator owns the completed-freeze validators (its OWN documented
# artifact shapes). Neither is modified by this module.
try:
    import sedan_lidar_select as groundtruth
except ImportError:  # pragma: no cover - defensive path for unusual loaders
    _SELECT_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "sedan_lidar_select.py")
    import importlib.util
    _select_spec = importlib.util.spec_from_file_location("sedan_lidar_select", _SELECT_PATH)
    groundtruth = importlib.util.module_from_spec(_select_spec)
    sys.modules["sedan_lidar_select"] = groundtruth
    _select_spec.loader.exec_module(groundtruth)
try:
    import sedan_lidar_freeze as freeze_tool
except ImportError:  # pragma: no cover - defensive path for unusual loaders
    _FREEZE_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "sedan_lidar_freeze.py")
    import importlib.util
    _freeze_spec = importlib.util.spec_from_file_location("sedan_lidar_freeze", _FREEZE_PATH)
    freeze_tool = importlib.util.module_from_spec(_freeze_spec)
    sys.modules["sedan_lidar_freeze"] = freeze_tool
    _freeze_spec.loader.exec_module(freeze_tool)

EXIT_OK = 0
EXIT_GUARD = 2
EXIT_WORKER = 3
EXIT_STATE = 4


class Failure(Exception):
    def __init__(self, code, message):
        super().__init__(message)
        self.code = code
        self.message = message


def fail_guard(message):
    raise Failure(EXIT_GUARD, message)


def fail_worker(message):
    raise Failure(EXIT_WORKER, message)


def fail_state(message):
    raise Failure(EXIT_STATE, message)


def through_freeze_validators(callable_, *args):
    """Run one of the freeze orchestrator's validators and translate its OWN
    Failure type into this tool's Failure (same exit code contract), so
    consumer-chain drift and ancestor-guard refusals are clean exit-4/2
    failures instead of uncaught cross-module exceptions."""
    try:
        return callable_(*args)
    except freeze_tool.Failure as error:
        raise Failure(getattr(error, "code", EXIT_STATE), str(error)) from None


# ---------------------------------------------------------------------------
# Pinned identities and policy (drift = refusal before anything runs).
# ---------------------------------------------------------------------------

DEFAULT_CACHE_ROOT = "/mnt/pit2/panopticon/geo-proof/sedan"
MOUNT_POINT = "/mnt/pit2"
PROC_MOUNTS_PATH = "/proc/mounts"

PREPARATION_DIR_REL = "preparation"
STAGE_PREFIX = "sedan-prepare-"
RECEIPT_NAME = "receipt.json"
LEDGER_NAME = "commands.jsonl"
STATE_VERSION = 1
STATE_FILE_REL = os.path.join("state", "state.json")
LOCK_FILE_REL = os.path.join("state", "lock")
STARTED_NAME = "STARTED"
COMPLETED_NAME = "COMPLETED"

MANIFEST_NAME = "sedan-ign-proof.json"
TEXTURE_MAP_NAME = "preview-textures.json"
SOURCES_NAME = "sources.txt"
PREPARED_DIR_REL = "prepared"
KML_NAME = "sedan-extent.kml"
DEM_NAME = "sedan-dem-32631.tif"
RGB_NAME = "sedan-ortho-32631.tif"

# Extract unit artifacts (sedan_extract.py layout).
EXTRACT_DIR_REL = "extracted"
EXTRACT_STAGE_PREFIX = "sedan-extract-"
EXTRACT_RECEIPT_NAME = "RECEIPT.txt"
EXTRACT_COMPLETED_NAME = "COMPLETED"
EXTRACT_FILES_DIR_REL = "files"
EXTRACT_MEMBERS_EXPECTED = 27
# Pinned from the executed extraction (27 members totalling exactly 205266077
# bytes: the producer pin sedan_extract.SELECTED_TOTAL and the executed receipt in
# ~/log/track-r/sedan-extraction-execution-20261103T1405/receipt.md agree; both
# re-verified read-only on every run).
EXTRACT_TOTAL_BYTES_EXPECTED = 205_266_077
EXTRACT_MEMBER_RE = re.compile(
    r"^member: (?P<archive>\S+) (?P<path>\S+) declared=(?P<declared>\d+) "
    r"got=(?P<got>\d+) sha256=(?P<sha256>[0-9a-f]{64})$")

# Chunk/lattice geometry (r2-sedan-ground-truth section 6; parent deep dive 5.1a).
CHUNK_SAMPLE_INTERVALS = 128            # chunk extent per axis, in lattice intervals
APRON_SAMPLES = 1                       # one read-only sample per side for normals
SOURCE_KERNEL_REACH_M = 3.048           # AreaWeighted source kernel reach past the sample footprint
IMAGERY_PIXELS_PER_SAMPLE = 10
IMAGERY_SAMPLE_SPACING_GAME_UNITS = 10  # Core spacing: 10 game units / 10 pixels = 0.3048 m
IMAGERY_TOLERANCE_BYTES = 2             # fixed per channel; raise only via review
NO_DATA_SEARCH_RADIUS_M = 3.048         # EXACTLY one height lattice; never widened
DEM_GSD_EXPECTED_M = 1.0                # RGE ALTI 1 m native GSD preserved
RGB_GSD_EXPECTED_M = 0.20               # BD ORTHO 20 cm native GSD preserved
GSD_EPSILON_M = 1e-6
DEM_NODATA = -9999.0

TARGET_CRS = {"type": "utm", "zone": 31, "hemisphere": "north"}
METERS_PER_GAME_UNIT = 0.3048
DEFAULT_MATERIAL = "grass"
VERTICAL_DATUM_LABEL = "NGF-IGN69"      # label only: NO EPSG:5720 geokey is written,
# no vertical conversion, no vertical shift, and verticalAccuracyMeters stays
# ABSENT (actual tile-specific source accuracy is unverified; a documented actual
# accuracy above 1.0 m escalates before any comparison, never silently absorbed).
VERTICAL_TOLERANCE_M = 1.0              # operator Budget A, engineering-only
BUDGET_B_GAME_UNITS = 0.0390625         # operator Budget B, independent
VERTICAL_EXAGGERATION = 1.0
IGN_ATTRIBUTION = "IGN, Licence Ouverte 2.0"
EXTENT_SOURCE_ID = "sedan-extent"
DEM_SOURCE_ID = "sedan-dem-32631"
RGB_SOURCE_ID = "sedan-ortho-32631"
EXTENT_FEATURE_ID = EXTENT_SOURCE_ID
PREVIEW_TEXTURES = {"grass": "GrassType1"}   # real catalog binding; no color inference

UNCERTAINTIES = (
    "RGE ALTI content editions 2012-2016 (index provenance per-member facts); recorded, "
    "not certification",
    "BD ORTHO content 2022; recorded, not certification",
    "LiDAR HD 2024 flight; recorded, not certification",
    "2024 LiDAR auto-classification bias unresolved (never silently absorbed into a tolerance)",
    "RGB control expected values are two-stage-equivalent over the prepared 0.2 m GeoTIFF "
    "(itself the raw-JP2 warp product: one filter plus Byte quantization); the raw JP2 mosaic "
    "and warp are cited and hash-bound; a later raw-source comparison shot validates "
    "preparation separately",
)

TOOL_TIMEOUT_SECONDS = 600.0
TOOL_OUTPUT_CAP_BYTES = 32 << 20
READ_BOUND_BYTES = 64 << 20
LEDGER_MAX_EVENTS = 4096
RESERVE_BYTES = 1 << 30
CHUNK_SIZE = 1 << 20
EXCERPT_BYTES = 500


def now_iso():
    return datetime.now().astimezone().isoformat(timespec="milliseconds")


def local_stamp():
    return datetime.now().astimezone().isoformat(timespec="milliseconds").replace(":", ".")


def log(message):
    print(f"{now_iso()} {message}", flush=True)


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        while True:
            block = handle.read(CHUNK_SIZE)
            if not block:
                break
            digest.update(block)
    return digest.hexdigest()


def statvfs(path):
    return os.statvfs(path)


def pid_alive(pid):
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except OSError:
        return True
    return True


def sha256_bytes(data):
    return hashlib.sha256(data).hexdigest()


def _finite_number(value):
    return (
        isinstance(value, (int, float))
        and not isinstance(value, bool)
        and math.isfinite(value)
    )


def write_bytes_atomic(path, data):
    """Unique tmp + fsync + replace + directory fsync: readers never see a torn
    file, and a stale tmp left by a crashed attempt can never collide."""
    tmp_path = f"{path}.{os.getpid()}.{time.monotonic_ns()}.tmp"
    descriptor = os.open(tmp_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o644)
    with os.fdopen(descriptor, "wb") as handle:
        handle.write(data)
        handle.flush()
        os.fsync(handle.fileno())
    os.replace(tmp_path, path)
    dir_fd = os.open(os.path.dirname(path), os.O_RDONLY)
    try:
        os.fsync(dir_fd)
    finally:
        os.close(dir_fd)


# ---------------------------------------------------------------------------
# The ONE subprocess seam: exact argv, wall timeout, bounded capture. Tests
# replace this function; nothing else in this module spawns a process.
# ---------------------------------------------------------------------------


class ToolRun:
    def __init__(self, returncode, stdout, stderr, timed_out, wall_seconds):
        self.returncode = returncode
        self.stdout = stdout
        self.stderr = stderr
        self.timed_out = timed_out
        self.wall_seconds = wall_seconds


def run_tool(argv, timeout_seconds=TOOL_TIMEOUT_SECONDS, input_bytes=None,
             cap_bytes=TOOL_OUTPUT_CAP_BYTES):
    """Run one external tool and return a bounded ToolRun. stdout/stderr are each
    hard-capped at cap_bytes with the cap checked during capture (never unbounded);
    a wall-clock timeout kills and reaps the child before returning."""
    started = time.monotonic()
    stdin_mode = subprocess.PIPE if input_bytes is not None else subprocess.DEVNULL
    try:
        proc = subprocess.Popen(argv, stdin=stdin_mode, stdout=subprocess.PIPE,
                                stderr=subprocess.PIPE)
    except OSError as error:
        fail_worker(f"tool failed to start ({argv[0]}): {error}")
    if input_bytes is not None:
        try:
            proc.stdin.write(input_bytes)
            proc.stdin.close()
        except OSError:
            pass
        proc.stdin = None
    selector = selectors.DefaultSelector()
    selector.register(proc.stdout, selectors.EVENT_READ, "out")
    selector.register(proc.stderr, selectors.EVENT_READ, "err")
    buffers = {"out": bytearray(), "err": bytearray()}
    open_fds = {proc.stdout.fileno(), proc.stderr.fileno()}
    deadline = started + timeout_seconds
    timed_out = False
    while open_fds:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            timed_out = True
            break
        events = selector.select(remaining)
        for key, _mask in events:
            chunk = os.read(key.fileobj.fileno(), CHUNK_SIZE)
            if chunk:
                target = buffers[key.data]
                room = max(cap_bytes - len(target), 0)
                target.extend(chunk[:room])
            else:
                selector.unregister(key.fileobj)
                open_fds.discard(key.fileobj.fileno())
                try:
                    key.fileobj.close()
                except OSError:
                    pass
    selector.close()
    if timed_out:
        try:
            proc.kill()
        except OSError:
            pass
    try:
        returncode = proc.wait(timeout=max(deadline - time.monotonic(), 5.0))
    except subprocess.TimeoutExpired:
        timed_out = True
        try:
            proc.kill()
        except OSError:
            pass
        returncode = proc.wait()
    for stream in (proc.stdout, proc.stderr):
        try:
            stream.close()
        except OSError:
            pass
    return ToolRun(
        returncode=returncode,
        stdout=bytes(buffers["out"]),
        stderr=bytes(buffers["err"]),
        timed_out=timed_out,
        wall_seconds=time.monotonic() - started,
    )


class RunJournal:
    """Append-only chronology ledger + invocation logger. The freeze event is
    appended FIRST and BEFORE any subprocess can run (run_tool is called only
    through journal.invoke), so code order guarantees freezeVerifiedAt <
    firstDemRasterIoAt. Every subprocess invocation logs the exact argv, timeout,
    returncode and the bounded-captured stdout/stderr byte counts + sha256s."""

    def __init__(self, ledger_path):
        self.ledger_path = ledger_path
        self.freeze_verified_at = None
        self.first_dem_raster_io_at = None
        self.invocations = 0

    def append(self, event):
        line = json.dumps(event, sort_keys=True, ensure_ascii=True) + "\n"
        descriptor = os.open(self.ledger_path, os.O_CREAT | os.O_APPEND | os.O_WRONLY, 0o644)
        with os.fdopen(descriptor, "a", encoding="utf-8") as handle:
            handle.write(line)
            handle.flush()
            os.fsync(handle.fileno())

    def freeze_validated_complete(self, freeze_record):
        if self.freeze_verified_at is not None:
            fail_state("freeze_validated_complete appended more than once in one run")
        self.freeze_verified_at = now_iso()
        self.append({
            "event": "freeze_validated_complete",
            "freezeVerifiedAt": self.freeze_verified_at,
            "freezeReceiptSha256": freeze_record["receipt_sha256"],
            "integrityManifestSha256": freeze_record["integrity_manifest_sha256"],
            "selectionTables": {
                "candidates_sha256": freeze_record["candidates_sha256"],
                "selection_sha256": freeze_record["selection_sha256"],
                "frozen_manifest_sha256": freeze_record["frozen_manifest_sha256"],
                "canonical_digest": freeze_record["canonical_digest"],
            },
        })

    def invoke(self, argv, height_semantic, timeout_seconds=TOOL_TIMEOUT_SECONDS,
               input_bytes=None, cap_bytes=TOOL_OUTPUT_CAP_BYTES, label=None):
        if self.freeze_verified_at is None:
            fail_state("subprocess invoked before the freeze_validated_complete event; "
                       "chronology code order violated")
        if height_semantic and self.first_dem_raster_io_at is None:
            self.first_dem_raster_io_at = now_iso()
            self.append({
                "event": "dem_rasterio_start",
                "firstDemRasterIoAt": self.first_dem_raster_io_at,
                "argv": [str(part) for part in argv],
            })
        run = run_tool(argv, timeout_seconds=timeout_seconds, input_bytes=input_bytes,
                       cap_bytes=cap_bytes)
        self.invocations += 1
        self.append({
            "event": "tool_invoked",
            "tool": os.path.basename(str(argv[0])),
            "argv": [str(part) for part in argv],
            "timeoutSeconds": timeout_seconds,
            "heightSemantic": bool(height_semantic),
            "returncode": run.returncode,
            "timedOut": run.timed_out,
            "stdoutBytes": len(run.stdout),
            "stdoutSha256": sha256_bytes(run.stdout),
            "stderrBytes": len(run.stderr),
            "stderrSha256": sha256_bytes(run.stderr),
        })
        if run.timed_out:
            fail_worker(f"{label or argv[0]} timed out after {timeout_seconds} s; "
                        f"child killed and reaped, stage preserved")
        if run.returncode != 0:
            excerpt = run.stderr.decode("utf-8", "replace").strip()[:EXCERPT_BYTES]
            fail_worker(f"{label or argv[0]} exited {run.returncode}; excerpt: {excerpt}")
        return run

    def preparation_completed(self, outputs):
        self.append({
            "event": "preparation_completed",
            "completedAt": now_iso(),
            "outputs": outputs,
        })


def read_ledger(path):
    """Bounded parse of the append-only ledger (count-capped)."""
    if not os.path.isfile(path) or os.path.islink(path):
        fail_state(f"chronology ledger missing at {path}")
    events = []
    with open(path, "r", encoding="utf-8") as handle:
        for line in handle:
            if not line.strip():
                continue
            try:
                events.append(json.loads(line))
            except ValueError as error:
                fail_state(f"chronology ledger at {path} carries an unparseable line: {error}")
            if len(events) > LEDGER_MAX_EVENTS:
                fail_state(f"chronology ledger at {path} exceeds the {LEDGER_MAX_EVENTS}-event bound")
    return events


def read_json_bound(path, label):
    if os.path.islink(path) or not os.path.isfile(path):
        fail_state(f"{label} missing at {path}")
    if os.path.getsize(path) > READ_BOUND_BYTES:
        fail_state(f"{label} at {path} exceeds the {READ_BOUND_BYTES}-byte read bound")
    try:
        with open(path, "r", encoding="utf-8") as handle:
            return json.load(handle)
    except (OSError, ValueError) as error:
        fail_state(f"{label} at {path} is not readable JSON: {error}")


# ---------------------------------------------------------------------------
# Freeze chain validation (read-only; reuses the freeze orchestrator's OWN
# validators so the accepted artifact shapes are checked where they live).
# ---------------------------------------------------------------------------


def validate_freeze_chain(cache):
    """Structural read-only validation of the completed final freeze: exactly one
    completed stage, state/receipt/manifest/tables hashes and canonical digest
    consistent, integrity manifest present with all six verified tile entries.
    No tile bytes are hashed here (preflight-safe); prepare mode additionally
    runs freeze_tool.reverify_completed (full re-hash) before any write."""
    stage, problem = freeze_tool.find_completed_freeze(cache)
    if problem is not None:
        fail_state(problem)
    if stage is None:
        fail_state(f"no completed final freeze under {freeze_tool.freeze_root(cache)}; the "
                   f"preparer runs only after a VALID COMPLETE final six-tile freeze")
    state = read_json_bound(os.path.join(stage, freeze_tool.STATE_FILE_REL),
                            "completed freeze state")
    if not isinstance(state, dict) or state.get("version") != freeze_tool.STATE_VERSION \
            or state.get("mode") != "freeze":
        fail_state(f"completed freeze state at {stage} has an unsupported shape")
    artifacts = state.get("artifacts")
    recorded_digest = state.get("canonical_digest")
    tiles = state.get("tiles")
    if not isinstance(artifacts, dict) or not isinstance(tiles, dict) \
            or not isinstance(recorded_digest, str):
        fail_state(f"completed freeze state at {stage} carries no tiles/artifacts/digest")
    for label in ("candidates_tsv", "selection_tsv", "frozen_manifest", "receipt"):
        artifact = artifacts.get(label)
        if not isinstance(artifact, dict) or not isinstance(artifact.get("path"), str) \
                or not isinstance(artifact.get("sha256"), str):
            fail_state(f"completed freeze state artifact {label} is malformed")
    receipt_path = artifacts["receipt"]["path"]
    if sha256_file(receipt_path) != artifacts["receipt"]["sha256"]:
        fail_state(f"freeze receipt sha256 drift at {receipt_path}")
    integrity_path = freeze_tool.integrity_manifest_path(cache)
    entries, _worker_cfg, present, problem = freeze_tool.read_integrity_manifest(integrity_path)
    if problem is not None:
        fail_state(problem)
    if not present:
        fail_state(f"integrity manifest absent at {integrity_path}")
    manifest = read_json_bound(artifacts["frozen_manifest"]["path"], "frozen manifest")
    digest = groundtruth.canonical_digest(manifest)
    if digest != recorded_digest:
        fail_state(f"freeze canonical digest drifted ({digest} vs {recorded_digest})")
    candidates_path = artifacts["candidates_tsv"]["path"]
    selection_path = artifacts["selection_tsv"]["path"]
    for label, path in (("candidates_tsv", candidates_path), ("selection_tsv", selection_path)):
        if sha256_file(path) != artifacts[label]["sha256"]:
            fail_state(f"freeze selection table {label} sha256 drifted at {path}")
    tables = manifest.get("tables", {})
    if tables.get("candidates_sha256") != artifacts["candidates_tsv"]["sha256"] \
            or tables.get("selection_sha256") != artifacts["selection_tsv"]["sha256"]:
        fail_state("frozen manifest tables block disagrees with the state artifact hashes")
    tile_shas = {}
    for pin in freeze_tool.TILE_PINS:
        name = pin["name"]
        state_record = tiles.get(name)
        entry = entries.get(name)
        if not isinstance(state_record, dict) or not isinstance(state_record.get("sha256"), str):
            fail_state(f"completed freeze state carries no sha256 for {name}")
        if entry is None:
            fail_state(f"integrity manifest has no entry for {name}: freeze incomplete")
        if entry["sha256"] != state_record["sha256"]:
            fail_state(f"integrity manifest sha256 for {name} disagrees with the freeze state")
        if entry["size"] != pin["size"] or state_record.get("size") not in (None, pin["size"]):
            fail_state(f"pinned size drift for {name}")
        tile_shas[name] = state_record["sha256"]
    return {
        "stage": stage,
        "canonical_digest": recorded_digest,
        "receipt_path": receipt_path,
        "receipt_sha256": artifacts["receipt"]["sha256"],
        "integrity_manifest_path": integrity_path,
        "integrity_manifest_sha256": sha256_file(integrity_path),
        "frozen_manifest_path": artifacts["frozen_manifest"]["path"],
        "frozen_manifest_sha256": artifacts["frozen_manifest"]["sha256"],
        "candidates_path": candidates_path,
        "candidates_sha256": artifacts["candidates_tsv"]["sha256"],
        "selection_path": selection_path,
        "selection_sha256": artifacts["selection_tsv"]["sha256"],
        "tiles": tile_shas,
    }


# ---------------------------------------------------------------------------
# Selection table parsing (5 primaries + 2 alternates from the frozen tables).
# ---------------------------------------------------------------------------

REQUIRED_SELECTION_COLUMNS = ("id", "role", "rank", "utm_x", "utm_y",
                              "wgs84_lat", "wgs84_lon", "truth_z")


def parse_selection_tsv(path):
    """Parse the frozen selection table; refuse anything but the frozen 5+2 shape.
    Truth values come verbatim from the frozen table (never recomputed)."""
    if os.path.islink(path) or not os.path.isfile(path):
        fail_state(f"selection table missing at {path}")
    if os.path.getsize(path) > READ_BOUND_BYTES:
        fail_state(f"selection table at {path} exceeds the {READ_BOUND_BYTES}-byte read bound")
    with open(path, "r", encoding="utf-8") as handle:
        lines = handle.read().splitlines()
    if not lines:
        fail_state(f"selection table at {path} is empty")
    header = lines[0].split("\t")
    missing = [column for column in REQUIRED_SELECTION_COLUMNS if column not in header]
    if missing:
        fail_state(f"selection table at {path} lacks the required columns {missing}")
    index = {column: header.index(column) for column in REQUIRED_SELECTION_COLUMNS}
    rows = {"primary": [], "alternate": []}
    for line_number, line in enumerate(lines[1:], start=2):
        if not line:
            continue
        fields = line.split("\t")
        if len(fields) != len(header):
            fail_state(f"selection table row {line_number} has {len(fields)} fields, "
                       f"expected {len(header)}")
        record = {column: fields[index[column]] for column in REQUIRED_SELECTION_COLUMNS}
        role = record["role"]
        if role not in rows:
            fail_state(f"selection table row {line_number} carries unknown role {role!r}")
        for column in ("utm_x", "utm_y", "wgs84_lat", "wgs84_lon", "truth_z"):
            try:
                value = float(record[column])
            except ValueError:
                fail_state(f"selection table row {line_number} column {column} is not numeric")
            if not math.isfinite(value):
                fail_state(f"selection table row {line_number} column {column} is not finite")
        for column in ("rank",):
            if not record[column].isdigit():
                fail_state(f"selection table row {line_number} column {column} is not an integer")
        rows[role].append(record)
    if len(rows["primary"]) != groundtruth.MIN_PRIMARY:
        fail_state(f"selection table carries {len(rows['primary'])} primaries, "
                   f"expected {groundtruth.MIN_PRIMARY}")
    if len(rows["alternate"]) != groundtruth.N_ALTERNATES:
        fail_state(f"selection table carries {len(rows['alternate'])} alternates, "
                   f"expected {groundtruth.N_ALTERNATES}")
    # The frozen table's rank column carries the candidate-pool rank (the
    # accepted freeze's primaries are ranks 1, 2, 3, 5, 172 and the alternates
    # 6, 7), so ranks are required to be DISTINCT POSITIVE integers per role,
    # never a dense 1..N sequence.
    for role, records in rows.items():
        ranks = [int(record["rank"]) for record in records]
        if any(rank <= 0 for rank in ranks) or len(set(ranks)) != len(ranks):
            fail_state(f"{role} ranks {ranks} are not distinct positive integers")
        names = [record["id"] for record in records]
        if len(set(names)) != len(names):
            fail_state(f"{role} ids are not unique: {names}")
    return rows


# ---------------------------------------------------------------------------
# Extract receipt validation (read-only re-verify: shape, counts, totals,
# member file presence and sizes; per-member bytes were hash-verified by the
# extract unit's own receipt and are not re-hashed here).
# ---------------------------------------------------------------------------


def find_completed_extract_stage(cache):
    root = os.path.join(cache, EXTRACT_DIR_REL)
    if not os.path.isdir(root) or os.path.islink(root):
        fail_state(f"no extracted directory at {root}")
    completed = []
    with os.scandir(root) as entries:
        for entry in entries:
            if not entry.name.startswith(EXTRACT_STAGE_PREFIX) \
                    or not entry.is_dir(follow_symlinks=False):
                continue
            if os.path.exists(os.path.join(entry.path, EXTRACT_COMPLETED_NAME)):
                completed.append(entry.path)
    if len(completed) > 1:
        fail_state(f"multiple completed extract stages under {root}; resolve which one stands "
                   f"manually: {sorted(completed)}")
    if not completed:
        fail_state(f"no completed extract stage under {root}")
    return completed[0]


def validate_extract_receipt(cache):
    stage = find_completed_extract_stage(cache)
    receipt_path = os.path.join(stage, EXTRACT_RECEIPT_NAME)
    if os.path.islink(receipt_path) or not os.path.isfile(receipt_path):
        fail_state(f"extract receipt missing at {receipt_path}")
    if os.path.getsize(receipt_path) > READ_BOUND_BYTES:
        fail_state(f"extract receipt at {receipt_path} exceeds the {READ_BOUND_BYTES}-byte bound")
    with open(receipt_path, "r", encoding="utf-8") as handle:
        text = handle.read()
    member_lines = []
    declared_members = None
    total_got = None
    editions = []
    for line in text.splitlines():
        if line.startswith("members: "):
            declared_members = int(line.split()[1])
        elif line.startswith("total got: "):
            total_got = int(line.split()[2])
        elif line.startswith("edition: "):
            editions.append(line[len("edition: "):])
        else:
            match = EXTRACT_MEMBER_RE.match(line)
            if match:
                member_lines.append(match.groupdict())
    if declared_members != EXTRACT_MEMBERS_EXPECTED:
        fail_state(f"extract receipt declares {declared_members} members, expected "
                   f"{EXTRACT_MEMBERS_EXPECTED}")
    if len(member_lines) != EXTRACT_MEMBERS_EXPECTED:
        fail_state(f"extract receipt lists {len(member_lines)} member lines, expected "
                   f"{EXTRACT_MEMBERS_EXPECTED}")
    if total_got != EXTRACT_TOTAL_BYTES_EXPECTED:
        fail_state(f"extract receipt total got {total_got}, expected "
                   f"{EXTRACT_TOTAL_BYTES_EXPECTED}")
    got_sum = sum(int(member["got"]) for member in member_lines)
    declared_sum = sum(int(member["declared"]) for member in member_lines)
    if got_sum != EXTRACT_TOTAL_BYTES_EXPECTED or declared_sum != EXTRACT_TOTAL_BYTES_EXPECTED:
        fail_state(f"extract receipt member sums ({declared_sum}/{got_sum}) disagree with the "
                   f"pinned {EXTRACT_TOTAL_BYTES_EXPECTED}")
    files_root = os.path.join(stage, EXTRACT_FILES_DIR_REL)
    file_count = 0
    file_bytes = 0
    for dirpath, _dirnames, filenames in os.walk(files_root):
        for filename in filenames:
            path = os.path.join(dirpath, filename)
            if os.path.islink(path) or not os.path.isfile(path):
                fail_state(f"extract stage member at {path} is not a regular non-symlink file")
            file_count += 1
            file_bytes += os.path.getsize(path)
    if file_count != EXTRACT_MEMBERS_EXPECTED:
        fail_state(f"extract stage holds {file_count} member files, expected "
                   f"{EXTRACT_MEMBERS_EXPECTED}")
    if file_bytes != EXTRACT_TOTAL_BYTES_EXPECTED:
        fail_state(f"extract stage member bytes {file_bytes} disagree with the pinned "
                   f"{EXTRACT_TOTAL_BYTES_EXPECTED}")
    elevation, imagery = classify_source_members(stage, member_lines)
    return {
        "stage": stage,
        "receipt_path": receipt_path,
        "receipt_sha256": sha256_file(receipt_path),
        "members": EXTRACT_MEMBERS_EXPECTED,
        "total_bytes": EXTRACT_TOTAL_BYTES_EXPECTED,
        "editions": editions,
        "elevation_members": elevation,
        "imagery_members": imagery,
    }


ELEVATION_MEMBER_MARK = "RGEALTI"
IMAGERY_MEMBER_MARK = "ORTHO"
RASTER_SUFFIXES = (".tif", ".jp2")


def classify_source_members(stage, member_lines):
    """Fail-closed source classification from the receipt's archive paths: the DEM
    warp needs RGEALTI rasters and the RGB warp needs ORTHO rasters. Only raster
    members are resolved, and each raster basename must match exactly one
    extracted file on disk; same-basename aux sidecars in different extracted
    subdirectories (e.g. dalles.shx in both the RGE and BD supplements) are
    skipped and never ambiguous."""
    disk = {}
    for dirpath, _dirnames, filenames in os.walk(os.path.join(stage, EXTRACT_FILES_DIR_REL)):
        for filename in filenames:
            disk.setdefault(filename, []).append(os.path.join(dirpath, filename))
    elevation = []
    imagery = []
    for member in member_lines:
        path = member["path"]
        basename = os.path.basename(path)
        if not basename.lower().endswith(RASTER_SUFFIXES):
            continue
        candidates = disk.get(basename, [])
        if len(candidates) != 1:
            fail_state(f"receipt raster member {basename} matches {len(candidates)} "
                       f"extracted files on disk; refusing an ambiguous source")
        disk_path = candidates[0]
        if ELEVATION_MEMBER_MARK in path:
            elevation.append(disk_path)
        elif IMAGERY_MEMBER_MARK in path:
            imagery.append(disk_path)
    if not elevation:
        fail_state(f"no {ELEVATION_MEMBER_MARK} raster members in the extract receipt; "
                   f"refusing to guess DEM sources")
    if not imagery:
        fail_state(f"no {IMAGERY_MEMBER_MARK} raster members in the extract receipt; "
                   f"refusing to guess imagery sources")
    return elevation, imagery


# ---------------------------------------------------------------------------
# Pure UTM grid geometry (documented rule; no Core import, no DEM access).
# ---------------------------------------------------------------------------


def compute_chunk_grid(origin_easting, origin_northing, aoi_size_m, lattice_m):
    """Canonical payload grid: the exact SW corner grown OUTWARD to whole chunks
    north-east only (r2-sedan-ground-truth section 6; parent deep dive section 4
    and 5.1a: chunk = 128 lattice intervals, one-sample apron). Pure UTM math on
    the frozen manifest's map_transform extent origin; nothing here opens a DEM."""
    if not (_finite_number(origin_easting) and _finite_number(origin_northing)):
        fail_state("grid origin is not finite")
    if not (_finite_number(aoi_size_m) and aoi_size_m > 0):
        fail_state("AOI size is not positive-finite")
    if lattice_m != groundtruth.LATTICE:
        fail_state(f"lattice {lattice_m} drifted from the frozen {groundtruth.LATTICE}")
    chunk_m = CHUNK_SAMPLE_INTERVALS * lattice_m
    n_chunks = int(math.ceil(aoi_size_m / chunk_m - 1e-9))
    if n_chunks <= 0:
        fail_state("chunk rounding produced a non-positive chunk count")
    grid_m = n_chunks * chunk_m
    grid_points = n_chunks * CHUNK_SAMPLE_INTERVALS + 1
    apron_m = APRON_SAMPLES * lattice_m
    margin_m = groundtruth.HALF_FOOTPRINT + SOURCE_KERNEL_REACH_M
    source_minimal = SimpleNamespace(
        x_min=origin_easting - margin_m,
        y_min=origin_northing - margin_m,
        x_max=origin_easting + grid_m + margin_m,
        y_max=origin_northing + grid_m + margin_m,
    )
    return {
        "origin": (origin_easting, origin_northing),
        "aoi_size_m": aoi_size_m,
        "lattice_m": lattice_m,
        "chunk_m": chunk_m,
        "n_chunks": n_chunks,
        "grid_m": grid_m,
        "grid_points": grid_points,
        "playable_bounds": SimpleNamespace(x_min=origin_easting, y_min=origin_northing,
                                           x_max=origin_easting + aoi_size_m,
                                           y_max=origin_northing + aoi_size_m),
        "grid_bounds": SimpleNamespace(x_min=origin_easting, y_min=origin_northing,
                                       x_max=origin_easting + grid_m,
                                       y_max=origin_northing + grid_m),
        "apron_m": apron_m,
        "source_minimal_bounds": source_minimal,
        "window": outward_aligned_window(source_minimal, DEM_GSD_EXPECTED_M),
        "rgb_window": outward_aligned_window(source_minimal, RGB_GSD_EXPECTED_M),
    }


def outward_aligned_window(minimal, gsd_m):
    window = SimpleNamespace(
        x_min=math.floor(minimal.x_min / gsd_m) * gsd_m,
        y_min=math.floor(minimal.y_min / gsd_m) * gsd_m,
        x_max=math.ceil(minimal.x_max / gsd_m) * gsd_m,
        y_max=math.ceil(minimal.y_max / gsd_m) * gsd_m,
    )
    if window.x_max <= window.x_min or window.y_max <= window.y_min:
        fail_state(f"outward-aligned window at {gsd_m} m has a non-positive width")
    return window


def apron_index_range(grid):
    return range(-APRON_SAMPLES, grid["grid_points"] + APRON_SAMPLES)


def actual_extent(header, label):
    gt = header["geoTransform"]
    if gt[2] != 0.0 or gt[4] != 0.0 or gt[1] <= 0.0 or gt[5] >= 0.0:
        fail_state(f"{label}: raster is not north-up; its extent cannot be derived")
    return SimpleNamespace(x_min=gt[0], x_max=gt[0] + header["width"] * gt[1],
                           y_min=gt[3] + header["height"] * gt[5], y_max=gt[3])


def assert_extent_covers_apron(extent, grid, label):
    """Every apron footprint (global indices -1..385, both axes) lies inside the extent."""
    half = groundtruth.HALF_FOOTPRINT
    lattice = grid["lattice_m"]
    for axis, origin, low, high in (("x", grid["origin"][0], extent.x_min, extent.x_max),
                                    ("y", grid["origin"][1], extent.y_min, extent.y_max)):
        for index in apron_index_range(grid):
            centre = origin + index * lattice
            if not (low <= centre - half and centre + half <= high):
                fail_state(f"{label} does not cover the apron footprint of global index {index} "
                           f"on the {axis} axis: footprint [{centre - half}, {centre + half}] "
                           f"vs extent [{low}, {high}]")


def assert_planned_windows_cover_apron(grid):
    assert_extent_covers_apron(grid["window"], grid, "planned DEM window")
    assert_extent_covers_apron(grid["rgb_window"], grid, "planned RGB window")


def named_half_footprint_bounds(selection_rows, half_footprint_m):
    """Every frozen named site's half-footprint square (the only named margins the
    warp window must cover; NO arbitrary buffer)."""
    if half_footprint_m != groundtruth.HALF_FOOTPRINT:
        fail_state(f"half footprint {half_footprint_m} drifted from the frozen "
                   f"{groundtruth.HALF_FOOTPRINT}")
    bounds = []
    for role in ("primary", "alternate"):
        for record in selection_rows[role]:
            x = float(record["utm_x"])
            y = float(record["utm_y"])
            bounds.append({"id": record["id"], "role": role,
                           "x_min": x - half_footprint_m, "y_min": y - half_footprint_m,
                           "x_max": x + half_footprint_m, "y_max": y + half_footprint_m})
    return bounds


def assert_window_covers_named_bounds(grid, named_bounds):
    window = grid["window"]
    for bound in named_bounds:
        inside = (bound["x_min"] >= window.x_min - 1e-9
                  and bound["y_min"] >= window.y_min - 1e-9
                  and bound["x_max"] <= window.x_max + 1e-9
                  and bound["y_max"] <= window.y_max + 1e-9)
        if not inside:
            fail_guard(f"DEM warp window does not cover the named half-footprint bound for "
                       f"{bound['id']}: {bound} vs window "
                       f"[{window.x_min}, {window.y_min}, {window.x_max}, {window.y_max}]")


def assert_points_inside_playable_area(grid, points):
    """The playable area is the exact projected AOI square (the KML polygon's
    projected shape); quarter/three-quarter control centres must fall inside it."""
    bounds = grid["grid_bounds"]
    for label, x, y in points:
        inside = (bounds.x_min < x < bounds.x_max + (grid["aoi_size_m"] - grid["grid_m"])
                  and bounds.y_min < y < bounds.y_min + grid["aoi_size_m"])
        if not inside:
            fail_guard(f"RGB control {label} at ({x}, {y}) falls outside the extent polygon "
                       f"(playable area)")


# ---------------------------------------------------------------------------
# Coordinate conversion: ONE batched cs2cs per conversion set (KML corners;
# control centres), exact argv recorded by the journal.
# ---------------------------------------------------------------------------


def cs2cs_argv():
    # Without -f, PROJ prints DMS; EPSG:4326 authority order is latitude, then longitude.
    return ["cs2cs", "-f", "%.12f", "EPSG:32631", "+to", "EPSG:4326"]


def convert_utm_batch(journal, points_easting_northing):
    """Batched cs2cs EPSG:32631 -> EPSG:4326 for a list of (e, n) points; one
    subprocess invocation for the whole set (metadata-only: no DEM involvement)."""
    if not points_easting_northing:
        fail_state("empty cs2cs batch")
    stdin = "".join(f"{e!r} {n!r}\n" for e, n in points_easting_northing).encode("ascii")
    run = journal.invoke(cs2cs_argv(), height_semantic=False, input_bytes=stdin,
                         label="cs2cs batch")
    text = run.stdout.decode("utf-8", "replace")
    converted = []
    for line in text.splitlines():
        if not line.strip():
            continue
        if "*" in line:
            fail_guard(f"cs2cs failed to convert a point (error line): {line.strip()!r}")
        fields = line.split()
        if len(fields) < 2:
            fail_guard(f"cs2cs output line is not a lat/lon pair: {line.strip()!r}")
        try:
            lat, lon = float(fields[0]), float(fields[1])
        except ValueError:
            fail_guard(f"cs2cs output line is not numeric: {line.strip()!r}")
        if not (math.isfinite(lat) and math.isfinite(lon)):
            fail_guard(f"cs2cs returned a non-finite coordinate: {line.strip()!r}")
        if not (-90.0 <= lat <= 90.0 and -180.0 <= lon <= 180.0):
            fail_guard(f"cs2cs returned an out-of-range coordinate: {lat}, {lon}")
        converted.append((lat, lon))
    if len(converted) != len(points_easting_northing):
        fail_guard(f"cs2cs returned {len(converted)} points for {len(points_easting_northing)} "
                   f"inputs")
    return converted


def extent_kml_bytes(corners_wgs84):
    """Minimal KML: one Polygon quadrilateral over the exact projected AOI square
    corners converted to WGS84 (lon,lat order). Pure geometry, no IGN content."""
    if len(corners_wgs84) != 4:
        fail_state("extent KML needs exactly four corners")
    ring = list(corners_wgs84) + [corners_wgs84[0]]
    coordinates = " ".join(f"{lon:.9f},{lat:.9f},0" for lat, lon in ring)
    return (
        '<?xml version="1.0" encoding="UTF-8"?>\n'
        '<kml xmlns="http://www.opengis.net/kml/2.2">\n'
        '  <Document id="sedan-proof">\n'
        f'    <Placemark id="{EXTENT_FEATURE_ID}">\n'
        "      <name>Sedan proof extent</name>\n"
        "      <Polygon><outerBoundaryIs><LinearRing><coordinates>\n"
        f"        {coordinates}\n"
        "      </coordinates></LinearRing></outerBoundaryIs></Polygon>\n"
        "    </Placemark>\n"
        "  </Document>\n"
        "</kml>\n"
    ).encode("utf-8")


# ---------------------------------------------------------------------------
# GDAL/PROJ argv plans (pure builders; validated by tests; executed via the seam).
# ---------------------------------------------------------------------------


def gdalinfo_argv(path, stats=False):
    argv = ["gdalinfo", "-json"]
    if stats:
        argv.append("-stats")
    argv.append(path)
    return argv


def gdallocationinfo_argv(path, easting, northing):
    return ["gdallocationinfo", "-geoloc", "-valonly", path, f"{easting!r}", f"{northing!r}"]


def dem_warp_argv(window, gsd_m, sources, destination):
    """DEM warp: the outward-aligned source-corner window at the native source GSD;
    bilinear; Float32; COMPRESS=NONE per the card."""
    return ["gdalwarp", "-t_srs", "EPSG:32631",
            "-te", f"{window.x_min:.3f}", f"{window.y_min:.3f}",
            f"{window.x_max:.3f}", f"{window.y_max:.3f}",
            "-tr", f"{gsd_m!r}", f"{gsd_m!r}",
            "-r", "bilinear", "-ot", "Float32", "-of", "GTiff",
            "-co", "COMPRESS=NONE", "-dstnodata", f"{DEM_NODATA!r}",
            *sources, destination]


def rgb_warp_argv(window, gsd_m, sources, destination):
    """RGB warp: the outward-aligned source-corner window at the native imagery GSD;
    average resampling; uint8; COMPRESS=NONE per the card."""
    return ["gdalwarp", "-t_srs", "EPSG:32631",
            "-te", f"{window.x_min:.3f}", f"{window.y_min:.3f}",
            f"{window.x_max:.3f}", f"{window.y_max:.3f}",
            "-tr", f"{gsd_m!r}", f"{gsd_m!r}",
            "-r", "average", "-ot", "Byte", "-of", "GTiff",
            "-co", "COMPRESS=NONE",
            *sources, destination]


def control_reference_argv(prepared_source, band, footprint):
    """Expected-RGB chain: GDAL area average of the prepared RGB over one 0.3048 m
    footprint, Float32 so Core rounding is applied in Python, written through
    /vsistdout/ so the value lands in the bounded-captured ToolRun."""
    return ["gdalwarp", "-t_srs", "EPSG:32631",
            "-te", f"{footprint['x_min']!r}", f"{footprint['y_min']!r}",
            f"{footprint['x_max']!r}", f"{footprint['y_max']!r}",
            "-ts", "1", "1", "-r", "average", "-ot", "Float32",
            "-b", str(band), "-of", "AAIGrid", prepared_source, "/vsistdout/"]


def parse_gdalinfo_json(run, label):
    try:
        return json.loads(run.stdout.decode("utf-8", "replace"))
    except ValueError as error:
        fail_state(f"{label}: gdalinfo -json output is not readable JSON: {error}")


def raster_header_info(info, label, expect_epsg):
    """(record, problem): CRS/affine facts from an EMBEDDED header (never TAB
    extrapolation). Checks structure only; the caller pins product specifics."""
    if not isinstance(info, dict):
        fail_state(f"{label}: gdalinfo JSON is not an object")
    size = info.get("size")
    if not isinstance(size, list) or len(size) != 2 \
            or any(not isinstance(v, int) or isinstance(v, bool) or v <= 0 for v in size):
        fail_state(f"{label}: gdalinfo JSON carries no positive [width, height]")
    transform = info.get("geoTransform")
    if not isinstance(transform, list) or len(transform) != 6 \
            or any(not _finite_number(v) for v in transform):
        fail_state(f"{label}: gdalinfo JSON carries no six-term geoTransform")
    bands = info.get("bands")
    if not isinstance(bands, list) or not bands:
        fail_state(f"{label}: gdalinfo JSON carries no bands")
    band = bands[0]
    if not isinstance(band, dict) or not isinstance(band.get("type"), str):
        fail_state(f"{label}: gdalinfo JSON carries no band type")
    wkt = ""
    coordinate_system = info.get("coordinateSystem")
    if isinstance(coordinate_system, dict):
        wkt = str(coordinate_system.get("wkt", ""))
    if expect_epsg is not None and expect_epsg not in wkt:
        fail_state(f"{label}: embedded CRS does not name EPSG:{expect_epsg} (never TAB "
                   f"extrapolation); wkt head: {wkt[:120]!r}")
    return {
        "width": size[0],
        "height": size[1],
        "geoTransform": [float(v) for v in transform],
        "band_type": band["type"],
        "no_data_value": band.get("noDataValue"),
        "crs_wkt_head": wkt[:200],
    }


def check_native_gsd(record, expected_gsd, label):
    observed = abs(record["geoTransform"][1])
    if abs(observed - expected_gsd) > GSD_EPSILON_M:
        fail_guard(f"{label}: observed native GSD {observed} differs from the pinned "
                   f"{expected_gsd}; any GSD change needs explicit engineering justification "
                   f"recorded before it is made")
    return observed


def check_no_data_radius():
    """The NoData search radius is EXACTLY one height lattice and is never the
    25.0 example placeholder; widening is a STOP decision, never silent."""
    if NO_DATA_SEARCH_RADIUS_M != groundtruth.LATTICE:
        fail_state(f"noDataSearchRadiusMeters {NO_DATA_SEARCH_RADIUS_M} is not exactly one "
                   f"height lattice ({groundtruth.LATTICE}); widening is a STOP decision")
    if NO_DATA_SEARCH_RADIUS_M == 25.0:
        fail_state("noDataSearchRadiusMeters carries the example placeholder 25.0")
    return NO_DATA_SEARCH_RADIUS_M


def core_rgb_byte(value):
    """Core ToByte semantics (ImageryChunkGenerator): clamp to [0, 255], then round
    half away from zero. The PayloadImagerySampler Combine reads the same bytes
    (round then clamp), which is identical for every real value."""
    clamped = min(max(float(value), 0.0), 255.0)
    floor = math.floor(clamped)
    return int(floor) + (1 if clamped - floor >= 0.5 else 0)


def parse_aaigrid_value(run, label):
    """One-pixel Float32 AAIGrid value from the prepared RGB area average."""
    text = run.stdout.decode("utf-8", "replace")
    ncols = nrows = None
    data_values = []
    for line in text.splitlines():
        stripped = line.strip()
        if stripped.startswith("ncols"):
            ncols = int(stripped.split()[1])
        elif stripped.startswith("nrows"):
            nrows = int(stripped.split()[1])
        elif stripped and stripped[0].isdigit() or (stripped.startswith("-") and len(stripped) > 1):
            data_values.extend(float(part) for part in stripped.split())
    if ncols != 1 or nrows != 1 or len(data_values) != 1:
        fail_state(f"{label}: expected a 1 x 1 AAIGrid with one value; got ncols={ncols} "
                   f"nrows={nrows} values={len(data_values)}")
    value = data_values[0]
    if not math.isfinite(value):
        fail_state(f"{label}: AAIGrid value {value} is not finite")
    return core_rgb_byte(value)


# ---------------------------------------------------------------------------
# Prepared-raster space estimate and guard (declared BEFORE any write).
# ---------------------------------------------------------------------------


def axis_samples(extent_m, gsd_m):
    return int(math.ceil(extent_m / gsd_m - 1e-9)) + 1


def estimate_output_bytes(grid, dem_gsd_m, rgb_gsd_m):
    window = grid["window"]
    rgb_window = grid["rgb_window"]
    dem_samples = axis_samples(window.x_max - window.x_min, dem_gsd_m) \
        * axis_samples(window.y_max - window.y_min, dem_gsd_m)
    rgb_samples = axis_samples(rgb_window.x_max - rgb_window.x_min, rgb_gsd_m) \
        * axis_samples(rgb_window.y_max - rgb_window.y_min, rgb_gsd_m)
    return {
        "dem_bytes": dem_samples * 4,
        "rgb_bytes": rgb_samples * 3,
        "total_bytes": dem_samples * 4 + rgb_samples * 3,
    }


def check_free_space(ancestor, required_bytes):
    usage = statvfs(ancestor)
    free = usage.f_bavail * usage.f_frsize
    log(f"free space: free={free} required={required_bytes} "
        f"(staged DEM+RGB estimate + reserve {RESERVE_BYTES})")
    if free < required_bytes:
        fail_guard(f"insufficient free space on {ancestor}: free={free} "
                   f"required={required_bytes} remaining={required_bytes - free}")


def read_mounts(path=None):
    mounts_path = path or PROC_MOUNTS_PATH
    entries = []
    try:
        handle = open(mounts_path, "r", encoding="utf-8")
    except OSError as error:
        fail_guard(f"cannot read {mounts_path}: {error}")
    with handle:
        for line in handle:
            fields = line.split()
            if len(fields) >= 4:
                entries.append((fields[1], fields[3].split(",")))
    return entries


def check_mount_realness(root):
    """True when the exact rw mount entry for MOUNT_POINT exists; the default
    preflight reports a False return, --prepare refuses on it (same semantics as
    the freeze orchestrator)."""
    if not (root == MOUNT_POINT or root.startswith(MOUNT_POINT + os.sep)):
        log(f"mount realness NOT verified: {root} is not under the default prefix {MOUNT_POINT}")
        return False
    for mount_point, options in read_mounts():
        if mount_point == MOUNT_POINT and "rw" in options:
            log(f"mount realness verified: {MOUNT_POINT} is a rw mount")
            return True
    fail_guard(
        f"requires an exact rw mount entry with mount point {MOUNT_POINT} in {PROC_MOUNTS_PATH}: "
        f"refusing to create anything on a possibly-unmounted path (no fallback, no faked mounts)")


# ---------------------------------------------------------------------------
# Manifest generation (schema per the committed example + C# validator rules,
# with REAL values; placeholders are never copied).
# ---------------------------------------------------------------------------


def validate_relative_source_path(rel):
    """ManifestPathGuard essentials: relative posix below the manifest directory,
    no parent segments, no absolute/URL/backslash/NUL forms."""
    if not isinstance(rel, str) or not rel or "\x00" in rel:
        fail_state(f"manifest source path is empty or contains NUL: {rel!r}")
    if "://" in rel:
        fail_state(f"manifest source path is a URL: {rel!r}")
    normalized = rel.replace("\\", "/")
    if os.path.isabs(rel) or os.path.isabs(normalized) or normalized.startswith("//"):
        fail_state(f"manifest source path is absolute: {rel!r}")
    segments = normalized.split("/")
    if any(segment in ("", ".", "..") for segment in segments):
        fail_state(f"manifest source path has an empty, '.', or '..' segment: {rel!r}")
    return normalized


def build_manifest(selection_rows, rgb_controls, dem_source_rel, rgb_source_rel,
                   kml_rel, dem_header, rgb_header, origin_meters):
    """The runtime manifest with REAL values: the five frozen primaries verbatim
    (id/lat/lon/truth), two RGB controls at the predetermined quarter and
    three-quarter output-pixel centres, and the operator policy pins. Alternates
    are NEVER activated: they appear nowhere in controlPoints."""
    for rel in (dem_source_rel, rgb_source_rel, kml_rel):
        validate_relative_source_path(rel)
    check_no_data_radius()
    dem_attribution = ("IGN RGE ALTI 1 m (prepared EPSG:32631 Float32, native 1 m GSD "
                       "preserved); " + IGN_ATTRIBUTION)
    rgb_attribution = ("IGN BD ORTHO 20 cm (prepared EPSG:32631 uint8, native 0.20 m GSD "
                       "preserved); " + IGN_ATTRIBUTION)
    sources = [
        {
            "id": EXTENT_SOURCE_ID,
            "kind": "kml",
            "path": kml_rel,
            "role": "extent",
            "label": ("generated extent KML: exact projected 1100 m AOI square corners "
                      "(EPSG:32631) batch-converted to WGS84; playable-area mask"),
            "attribution": "generated extent KML from the frozen selection geometry",
        },
        {
            "id": DEM_SOURCE_ID,
            "kind": "geotiff",
            "path": dem_source_rel,
            "role": "elevation",
            "verticalDatum": VERTICAL_DATUM_LABEL,
            "label": (f"IGN RGE ALTI 1 m prepared EPSG:32631 Float32 DEM ({dem_header['width']}x"
                      f"{dem_header['height']}); verticalAccuracyMeters intentionally omitted "
                      "(unverified source accuracy); tolerances.verticalMeters is the operator "
                      "engineering limit, never a stated source accuracy"),
            "attribution": dem_attribution,
        },
        {
            "id": RGB_SOURCE_ID,
            "kind": "geotiff",
            "path": rgb_source_rel,
            "role": "imagery",
            "label": (f"IGN BD ORTHO 20 cm prepared EPSG:32631 uint8 GeoTIFF "
                      f"({rgb_header['width']}x{rgb_header['height']}); RGB control expected "
                      "values computed independently from this source's area-footprint averages"),
            "attribution": rgb_attribution,
        },
    ]
    control_points = []
    for record in selection_rows["primary"]:
        control_points.append({
            "name": record["id"],
            "latitude": float(record["wgs84_lat"]),
            "longitude": float(record["wgs84_lon"]),
            "elevationMeters": float(record["truth_z"]),
        })
    for control in rgb_controls:
        control_points.append({
            "name": control["name"],
            "latitude": control["latitude"],
            "longitude": control["longitude"],
            "imageryRgb": control["rgb"],
            "imageryToleranceBytes": IMAGERY_TOLERANCE_BYTES,
        })
    return {
        "sources": sources,
        "targetCoordinateSystem": dict(TARGET_CRS),
        "metersPerGameUnit": METERS_PER_GAME_UNIT,
        "defaultMaterial": DEFAULT_MATERIAL,
        "extentFeatureId": EXTENT_FEATURE_ID,
        "verticalPolicy": {
            "originMeters": origin_meters,
            "exaggeration": VERTICAL_EXAGGERATION,
        },
        "rasterPolicy": {
            "noData": "nearest-valid",
            "noDataSearchRadiusMeters": NO_DATA_SEARCH_RADIUS_M,
            "edge": "fail",
            "imageryPixelsPerSample": IMAGERY_PIXELS_PER_SAMPLE,
        },
        "tolerances": {"verticalMeters": VERTICAL_TOLERANCE_M},
        "controlPoints": control_points,
    }


def validate_generated_manifest(manifest, expected_primaries, forbidden_names=()):
    """Structural self-check mirroring the C# ManifestValidator rules the example
    tests enforce; the workflow report must detect a bad manifest, never quietly
    pass one. forbidden_names carries the alternate ids: recorded only, never
    activated, so their appearance in controlPoints is a failure."""
    if not isinstance(manifest, dict):
        fail_state("generated manifest is not an object")
    expected_top = {"sources", "targetCoordinateSystem", "metersPerGameUnit", "defaultMaterial",
                    "extentFeatureId", "verticalPolicy", "rasterPolicy", "tolerances",
                    "controlPoints"}
    if set(manifest) != expected_top:
        fail_state(f"generated manifest top-level keys {sorted(manifest)} != {sorted(expected_top)}")
    sources = manifest["sources"]
    if not isinstance(sources, list) or len(sources) != 3:
        fail_state("generated manifest needs exactly three sources")
    by_role = {}
    for source in sources:
        if set(source) - {"id", "kind", "path", "role", "verticalDatum", "label", "attribution"}:
            fail_state(f"generated source carries unexpected keys: {sorted(source)}")
        if not source.get("id") or not source.get("label") or not source.get("attribution"):
            fail_state("generated source lacks id/label/attribution")
        validate_relative_source_path(source["path"])
        if source["role"] == "elevation":
            if source.get("verticalDatum") != VERTICAL_DATUM_LABEL:
                fail_state("elevation source verticalDatum drifted from NGF-IGN69")
        if source["kind"] == "kml":
            if "generated extent KML" not in source["attribution"]:
                fail_state("extent source attribution lost the generated-KML marker")
        else:
            if IGN_ATTRIBUTION not in source["attribution"]:
                fail_state("raster source attribution lost the IGN Licence Ouverte marker")
        by_role[source["role"]] = source
    if sorted(by_role) != ["elevation", "extent", "imagery"]:
        fail_state(f"generated source roles {sorted(by_role)} != extent/elevation/imagery")
    if manifest["targetCoordinateSystem"] != TARGET_CRS:
        fail_state("generated targetCoordinateSystem drifted")
    if manifest["metersPerGameUnit"] != METERS_PER_GAME_UNIT:
        fail_state("generated metersPerGameUnit drifted")
    if manifest["defaultMaterial"] != DEFAULT_MATERIAL:
        fail_state("generated defaultMaterial drifted")
    if manifest["extentFeatureId"] != EXTENT_FEATURE_ID:
        fail_state("generated extentFeatureId drifted")
    vertical = manifest["verticalPolicy"]
    if set(vertical) != {"originMeters", "exaggeration"}:
        fail_state("generated verticalPolicy keys drifted")
    if not _finite_number(vertical["originMeters"]) or vertical["originMeters"] == 0.0:
        fail_state("generated verticalPolicy.originMeters is not the computed non-zero extent "
                   "minimum (a consumer must never assume 0)")
    if vertical["exaggeration"] != VERTICAL_EXAGGERATION:
        fail_state("generated verticalPolicy.exaggeration drifted")
    raster = manifest["rasterPolicy"]
    if raster != {"noData": "nearest-valid", "noDataSearchRadiusMeters": NO_DATA_SEARCH_RADIUS_M,
                  "edge": "fail", "imageryPixelsPerSample": IMAGERY_PIXELS_PER_SAMPLE}:
        fail_state("generated rasterPolicy drifted from the pinned fill policy")
    if manifest["tolerances"] != {"verticalMeters": VERTICAL_TOLERANCE_M}:
        fail_state("generated tolerances drifted from the operator Budget A value")
    points = manifest["controlPoints"]
    elevation_points = [point for point in points if "elevationMeters" in point]
    rgb_points = [point for point in points if "imageryRgb" in point]
    if len(elevation_points) + len(rgb_points) != len(points):
        fail_state("mixed control point shapes")
    if len(elevation_points) != groundtruth.MIN_PRIMARY:
        fail_state(f"vertical control points {len(elevation_points)} != "
                   f"{groundtruth.MIN_PRIMARY}")
    if len(rgb_points) != 2:
        fail_state(f"RGB control points {len(rgb_points)} != 2")
    names = [point["name"] for point in points]
    if len(set(names)) != len(names):
        fail_state("generated control point names are not unique")
    vertical_ids = [point["name"] for point in elevation_points]
    if vertical_ids != expected_primaries:
        fail_state(f"vertical control names {vertical_ids} are not exactly the five frozen "
                   f"primary ids {expected_primaries}")
    activated = sorted(set(names) & set(forbidden_names))
    if activated:
        fail_state(f"alternate ids {activated} appear in controlPoints; alternates are "
                   f"recorded only and NEVER activated")
    for point in elevation_points:
        if set(point) != {"name", "latitude", "longitude", "elevationMeters"}:
            fail_state(f"vertical control point keys drifted: {sorted(point)}")
        if not _finite_number(point["elevationMeters"]) or point["elevationMeters"] == 0.0:
            fail_state(f"vertical control {point['name']} carries a zero/invalid elevation "
                       f"(placeholder values are never copied)")
        if not (-90.0 <= point["latitude"] <= 90.0 and -180.0 <= point["longitude"] <= 180.0):
            fail_state(f"vertical control {point['name']} carries an out-of-range coordinate")
    for point in rgb_points:
        if set(point) != {"name", "latitude", "longitude", "imageryRgb",
                          "imageryToleranceBytes"}:
            fail_state(f"RGB control point keys drifted: {sorted(point)}")
        rgb = point["imageryRgb"]
        if not isinstance(rgb, list) or len(rgb) != 3 \
                or any(not isinstance(v, int) or isinstance(v, bool) or not 0 <= v <= 255
                       for v in rgb):
            fail_state(f"RGB control {point['name']} carries an invalid imageryRgb")
        if point["imageryToleranceBytes"] != IMAGERY_TOLERANCE_BYTES:
            fail_state(f"RGB control {point['name']} tolerance drifted from the fixed "
                       f"{IMAGERY_TOLERANCE_BYTES} bytes per channel")
    # Accuracy stays ABSENT on EVERY source (key presence, not free text: the
    # elevation label honestly says the accuracy is intentionally omitted), and
    # no verticalDatum may carry the EPSG:5720 geokey.
    for source in manifest["sources"]:
        if "verticalAccuracyMeters" in source or "horizontalAccuracyMeters" in source:
            fail_state(f"generated source {source['id']} states an accuracy key; accuracy "
                       f"stays ABSENT (unverified)")
        if "5720" in str(source.get("verticalDatum", "")):
            fail_state(f"generated source {source['id']} verticalDatum carries the "
                       f"EPSG:5720 geokey")
    if manifest["rasterPolicy"]["noDataSearchRadiusMeters"] == 25.0:
        fail_state("generated rasterPolicy carries the example placeholder radius 25.0; the "
                   "radius is EXACTLY one height lattice or the manifest is refused")
    if "EXAMPLE ONLY" in json.dumps(manifest, sort_keys=True):
        fail_state("generated manifest carries the example placeholder marker")
    return manifest


def sources_txt_bytes(plan):
    """Factual text record of the prepared inputs (no fabricated tool versions)."""
    lines = [
        "# Sedan operator-proof runtime input sources",
        f"generated: {now_iso()}",
        f"cache root: {plan['cache_root']}",
        f"extent: {plan['kml_rel']} sha256={plan['kml_sha256']}",
        f"elevation: {plan['dem_rel']} sha256={plan['dem_sha256']} size={plan['dem_size']} "
        f"crs=EPSG:32631 band=Float32 verticalDatum={VERTICAL_DATUM_LABEL} "
        f"verticalAccuracyMeters=absent (unverified)",
        f"imagery: {plan['rgb_rel']} sha256={plan['rgb_sha256']} size={plan['rgb_size']} "
        f"crs=EPSG:32631 band=Byte",
        f"manifest: {MANIFEST_NAME} sha256={plan['manifest_sha256']}",
        f"texture map: {TEXTURE_MAP_NAME} sha256={plan['textures_sha256']}",
        f"no-data fill policy: nearest-valid within {NO_DATA_SEARCH_RADIUS_M} m (one height "
        f"lattice); raster edge fail; imageryPixelsPerSample {IMAGERY_PIXELS_PER_SAMPLE}",
        f"freeze stage: {plan['freeze']['stage']}",
        f"freeze canonical digest: {plan['freeze']['canonical_digest']}",
        f"freeze receipt sha256: {plan['freeze']['receipt_sha256']}",
        f"freeze integrity manifest sha256: {plan['freeze']['integrity_manifest_sha256']}",
        f"freeze candidates sha256: {plan['freeze']['candidates_sha256']}",
        f"freeze selection sha256: {plan['freeze']['selection_sha256']}",
        f"extract receipt: {plan['extract']['receipt_path']} sha256="
        f"{plan['extract']['receipt_sha256']} members={plan['extract']['members']} "
        f"total_bytes={plan['extract']['total_bytes']}",
    ]
    lines.extend(f"edition: {edition}" for edition in plan["extract"]["editions"])
    lines.extend(f"source tile: {name} sha256={sha}"
                 for name, sha in sorted(plan["freeze"]["tiles"].items()))
    lines.extend(f"elevation source member: {os.path.basename(path)}"
                 for path in plan["extract"]["elevation_members"])
    lines.extend(f"imagery source member: {os.path.basename(path)}"
                 for path in plan["extract"]["imagery_members"])
    lines.append(f"chronology: freezeVerifiedAt={plan['freeze_verified_at']} "
                 f"firstDemRasterIoAt={plan['first_dem_raster_io_at']}")
    lines.append(f"ledger: {plan['commands_note']}")
    lines.extend(f"uncertainty: {item}" for item in UNCERTAINTIES)
    lines.append("status: prepared runtime inputs; no numeric residual and no proof PASS "
                 "computed here; not a card completion")
    return ("\n".join(lines) + "\n").encode("utf-8")


# ---------------------------------------------------------------------------
# Stage, lock, idempotence (own unique stage dir + marker + O_EXCL lock).
# ---------------------------------------------------------------------------


def preparation_root(cache):
    return os.path.join(cache, PREPARATION_DIR_REL)


def receipt_path(cache):
    return os.path.join(preparation_root(cache), RECEIPT_NAME)


def ledger_path(cache):
    return os.path.join(preparation_root(cache), LEDGER_NAME)


def write_state(state_path, state):
    tmp_path = state_path + ".tmp"
    with open(tmp_path, "w", encoding="utf-8") as handle:
        json.dump(state, handle, indent=2, sort_keys=True)
        handle.write("\n")
        handle.flush()
        os.fsync(handle.fileno())
    os.replace(tmp_path, state_path)


def read_lock(lock_path):
    try:
        with open(lock_path, "r", encoding="utf-8") as handle:
            text = handle.read().strip()
    except OSError as error:
        fail_state(f"unreadable lock at {lock_path}: {error}")
    fields = text.split(None, 1)
    if len(fields) != 2 or not fields[0].isdigit():
        fail_state(f"unparseable lock at {lock_path}; resolve it manually after confirming no "
                   f"preparation is running")
    return int(fields[0]), fields[1]


def acquire_lock(lock_path):
    if os.path.exists(lock_path):
        pid, recorded = read_lock(lock_path)
        if pid_alive(pid):
            fail_state(f"preparation lock at {lock_path} is held by live pid {pid} "
                       f"(recorded {recorded})")
        fail_state(f"stale preparation lock at {lock_path} (dead pid {pid}, recorded "
                   f"{recorded}); resolve it manually")
    descriptor = os.open(lock_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o644)
    with os.fdopen(descriptor, "w", encoding="utf-8") as handle:
        handle.write(f"{os.getpid()} {now_iso()}\n")


def release_lock(lock_path):
    if os.path.exists(lock_path):
        os.unlink(lock_path)


def owned_output_paths(cache):
    prepared = os.path.join(cache, PREPARED_DIR_REL)
    return {
        "manifest": os.path.join(cache, MANIFEST_NAME),
        "texture_map": os.path.join(cache, TEXTURE_MAP_NAME),
        "sources_txt": os.path.join(cache, SOURCES_NAME),
        "extent_kml": os.path.join(prepared, KML_NAME),
        "dem_tif": os.path.join(prepared, DEM_NAME),
        "rgb_tif": os.path.join(prepared, RGB_NAME),
    }


def promote_output(stage_path, owned_path, label):
    """Materialize one prepared output at its owned path via os.link: the link
    refuses an existing target, so an operator file is never overwritten, the
    bytes at the owned path appear whole (never partial), and a failed attempt
    leaves only the stage + ledger behind (a retry starts clean)."""
    if os.path.lexists(owned_path):
        fail_state(f"operator collision: {label} target already exists at {owned_path}; "
                   f"left untouched")
    try:
        os.link(stage_path, owned_path)
    except FileExistsError:
        fail_state(f"operator collision: {label} target appeared at {owned_path} during "
                   f"promotion; left untouched")
    except OSError as error:
        fail_worker(f"cannot promote {label} from {stage_path} to {owned_path}: {error}")


def verify_completed_preparation(cache):
    """Idempotent rerun: a completed receipt plus intact ledger and outputs are
    re-verified against their recorded sha256s and SKIPPED (read-only, zero
    subprocesses). Any drift is a state failure naming the field."""
    path = receipt_path(cache)
    if os.path.lexists(path) and (os.path.islink(path) or not os.path.isfile(path)):
        fail_state(f"preparation receipt at {path} is not a regular non-symlink file")
    if not os.path.exists(path):
        return None
    receipt = read_json_bound(path, "preparation receipt")
    if not isinstance(receipt, dict) or receipt.get("receipt_version") != STATE_VERSION \
            or receipt.get("tool") != "sedan_prepare_inputs.py":
        fail_state(f"preparation receipt at {path} has an unsupported shape")
    commands_sha = receipt.get("commands_sha256")
    if not isinstance(commands_sha, str):
        fail_state("preparation receipt carries no commands_sha256")
    ledger = ledger_path(cache)
    if os.path.islink(ledger) or not os.path.isfile(ledger):
        fail_state(f"chronology ledger missing at {ledger}; the completed receipt at {path} "
                   f"cannot be verified and is never adopted; resolve manually")
    if sha256_file(ledger) != commands_sha:
        fail_state(f"chronology ledger sha256 drifted against the receipt at {path}")
    events = read_ledger(ledger)
    if not events or events[-1].get("event") != "preparation_completed":
        fail_state("chronology ledger does not end with preparation_completed")
    outputs = receipt.get("outputs")
    if not isinstance(outputs, dict):
        fail_state("preparation receipt carries no outputs block")
    for label, record in sorted(outputs.items()):
        if not isinstance(record, dict) or not isinstance(record.get("path"), str) \
                or not isinstance(record.get("sha256"), str):
            fail_state(f"preparation receipt output {label} is malformed")
        output_path = record["path"]
        if os.path.islink(output_path) or not os.path.isfile(output_path):
            fail_state(f"completed preparation output {label} missing at {output_path}")
        if sha256_file(output_path) != record["sha256"]:
            fail_state(f"completed preparation output {label} sha256 drifted at {output_path}")
        if isinstance(record.get("size"), int) \
                and os.path.getsize(output_path) != record["size"]:
            fail_state(f"completed preparation output {label} size drifted at {output_path}")
    log(f"idempotent: completed preparation verified and skipped: {path}")
    return receipt


def refuse_operator_overwrites(paths):
    for label, path in sorted(paths.items()):
        if os.path.lexists(path):
            fail_state(f"operator collision: {label} target already exists at {path}; left "
                       f"untouched (a completed preparation is never overwritten; resolve "
                       f"manually)")


# ---------------------------------------------------------------------------
# The preparation run.
# ---------------------------------------------------------------------------


def check_protocol_constants():
    """Assert the pinned science module still carries the frozen protocol values
    this preparer's math assumes; any drift is a state failure before anything
    is created or spawned."""
    if groundtruth.PROTOCOL_VERSION != "1.2":
        fail_state(f"sedan_lidar_select PROTOCOL_VERSION is {groundtruth.PROTOCOL_VERSION!r}, "
                   f"expected '1.2'")
    origin = tuple(groundtruth.DEFAULT_AOI.origin_utm)
    if origin != (639841.997, 5506768.179):
        fail_state(f"canonical SW origin {origin!r} drifted from the documented "
                   f"(639841.997, 5506768.179)")
    if groundtruth.LATTICE != 3.048 or groundtruth.HALF_FOOTPRINT != 1.524:
        fail_state("lattice or half-footprint drifted from the frozen values")
    if groundtruth.MIN_PRIMARY != 5 or groundtruth.N_ALTERNATES != 2 or groundtruth.MIN_SECTORS != 3:
        fail_state("selection floors (5 primaries, 2 alternates, 3 sectors) drifted")
    if METERS_PER_GAME_UNIT * 10 != groundtruth.LATTICE:
        fail_state("metersPerGameUnit x 10 no longer equals the terrain lattice")
    log("protocol constants verified: v1.2, SW origin 639841.997/5506768.179, lattice 3.048 m, "
        "half footprint 1.524 m, chunk 128 intervals, 5+2 floors")


def resolve_primary_dem_value(journal, dem_path, record, dem_header):
    """One primary control's prepared-DEM sample; a NoData hit REFUSES the manifest
    generation with an explicit error (the workflow report must detect it)."""
    easting = float(record["utm_x"])
    northing = float(record["utm_y"])
    run = journal.invoke(gdallocationinfo_argv(dem_path, easting, northing),
                         height_semantic=True, label="gdallocationinfo primary sample")
    text = run.stdout.decode("utf-8", "replace").strip()
    if not text:
        fail_guard(f"primary control {record['id']} has no prepared-DEM sample at "
                   f"({easting}, {northing}); refusing to generate the manifest")
    try:
        value = float(text.split()[-1])
    except ValueError:
        fail_guard(f"primary control {record['id']} prepared-DEM sample is not numeric: "
                   f"{text[:80]!r}")
    if not math.isfinite(value) or value == DEM_NODATA:
        fail_guard(f"primary control {record['id']} falls on prepared-DEM NoData (value "
                   f"{value}); refusing to generate the manifest with a NoData control")
    return value


def rgb_control_grid(grid):
    """Quarter and three-quarter centres of the generated imagery grid: global output
    pixel indices N//4 and 3N//4 with N = n_chunks * 128 * 10 per axis, centre +0.5,
    spacing 0.3048 m, from the canonical SW origin (never the prepared raster)."""
    pixels_per_axis = grid["n_chunks"] * CHUNK_SAMPLE_INTERVALS * IMAGERY_PIXELS_PER_SAMPLE
    spacing_m = ((IMAGERY_SAMPLE_SPACING_GAME_UNITS * METERS_PER_GAME_UNIT)
                 / IMAGERY_PIXELS_PER_SAMPLE)
    origin_easting, origin_northing = grid["origin"]
    controls = []
    for name, index in (("rgb-quarter-centre", pixels_per_axis // 4),
                        ("rgb-three-quarter-centre", (3 * pixels_per_axis) // 4)):
        controls.append({"name": name, "pixel": (index, index),
                         "easting": origin_easting + (index + 0.5) * spacing_m,
                         "northing": origin_northing + (index + 0.5) * spacing_m})
    return {"pixels_per_axis": pixels_per_axis, "spacing_m": spacing_m, "controls": controls}


def resolve_rgb_controls(journal, prepared_rgb, grid):
    """Two RGB controls at the generated-grid quarter and three-quarter centres. The
    expected bytes are the GDAL area average of the operator-input PREPARED 0.2 m RGB
    over each 0.3048 m footprint, converted with Core rounding. The prepared source is
    hash-bound in the returned record; the raw JP2 and the payload are never read."""
    source_sha256 = sha256_file(prepared_rgb)
    layout = rgb_control_grid(grid)
    centres = layout["controls"]
    assert_points_inside_playable_area(grid, [(c["name"], c["easting"], c["northing"])
                                              for c in centres])
    converted = convert_utm_batch(journal, [(c["easting"], c["northing"]) for c in centres])
    half_footprint = layout["spacing_m"] / 2.0
    controls = []
    for control, (latitude, longitude) in zip(centres, converted):
        footprint = {
            "x_min": control["easting"] - half_footprint,
            "y_min": control["northing"] - half_footprint,
            "x_max": control["easting"] + half_footprint,
            "y_max": control["northing"] + half_footprint,
        }
        rgb = []
        for band in (1, 2, 3):
            # Imagery pixel read, NOT a height read: heightSemantic stays False
            # (the DEM warp already opened the height chronology above).
            argv = control_reference_argv(prepared_rgb, band, footprint)
            run = journal.invoke(argv, height_semantic=False,
                                 label="prepared RGB 0.3048 m footprint average")
            rgb.append(parse_aaigrid_value(run, f"{control['name']} band {band}"))
        controls.append({"name": control["name"], "latitude": latitude,
                         "longitude": longitude, "rgb": rgb, "pixel": control["pixel"]})
    return {"controls": controls, "source_sha256": source_sha256,
            "source_size": os.path.getsize(prepared_rgb),
            "pixels_per_axis": layout["pixels_per_axis"], "spacing_m": layout["spacing_m"],
            "footprint_half_m": half_footprint}


def run_prepare(cache):
    """The explicit --prepare operation: validate everything read-only first, then
    guard space, then write. One attempt per stage; failures preserve the stage
    dir and the chronology ledger and never overwrite operator files."""
    check_protocol_constants()
    receipt = verify_completed_preparation(cache)
    if receipt is not None:
        return receipt
    owned = owned_output_paths(cache)
    refuse_operator_overwrites(owned)
    if os.path.exists(receipt_path(cache)):
        fail_state(f"preparation receipt exists at {receipt_path(cache)} but outputs are "
                   f"absent or unrecorded; resolve manually (never adopted)")
    # Read-only validation BEFORE any subprocess or write: freeze chain (structur-
    # al), extract receipt, then the freeze orchestrator's FULL re-verification
    # (six LAZ re-hashes + artifact hashes + canonical digest).
    freeze_record = validate_freeze_chain(cache)
    extract = validate_extract_receipt(cache)
    through_freeze_validators(freeze_tool.reverify_completed, cache, freeze_record["stage"])
    selection = parse_selection_tsv(freeze_record["selection_path"])
    primaries = [record["id"] for record in selection["primary"]]
    alternates = [record["id"] for record in selection["alternate"]]
    manifest = read_json_bound(freeze_record["frozen_manifest_path"], "frozen manifest")
    transform = manifest.get("map_transform")
    if not isinstance(transform, dict):
        fail_state("frozen manifest carries no map_transform block")
    origin_recorded = transform.get("extent_origin_utm")
    if not isinstance(origin_recorded, list) or len(origin_recorded) != 2 \
            or any(not _finite_number(v) for v in origin_recorded):
        fail_state("frozen manifest map_transform extent_origin_utm is malformed")
    if (float(origin_recorded[0]), float(origin_recorded[1])) \
            != tuple(groundtruth.DEFAULT_AOI.origin_utm):
        fail_state(f"frozen manifest extent origin {origin_recorded!r} disagrees with the "
                   f"canonical SW origin")
    lattice_recorded = transform.get("lattice_spacing_m")
    if not _finite_number(lattice_recorded) or float(lattice_recorded) != groundtruth.LATTICE:
        fail_state("frozen manifest lattice drifted from the frozen spacing")
    grid = compute_chunk_grid(float(origin_recorded[0]), float(origin_recorded[1]),
                              groundtruth.DEFAULT_AOI.size, float(lattice_recorded))
    named_bounds = named_half_footprint_bounds(selection, groundtruth.HALF_FOOTPRINT)
    assert_window_covers_named_bounds(grid, named_bounds)
    assert_planned_windows_cover_apron(grid)
    # Guard rails BEFORE any write: mount realness, ancestor, declared space.
    if not check_mount_realness(cache):
        fail_guard(f"--prepare requires verified mount realness for {cache}; refusing to "
                   f"write to an unverified or read-only mount")
    ancestor = through_freeze_validators(freeze_tool.check_freeze_ancestor,
                                         preparation_root(cache), "preparation")
    check_protocol_constants()
    dem_source_header_probe = None
    estimate = estimate_output_bytes(grid, DEM_GSD_EXPECTED_M, RGB_GSD_EXPECTED_M)
    check_free_space(ancestor, estimate["total_bytes"] + RESERVE_BYTES)
    # Stage, marker, lock, ledger.
    root = preparation_root(cache)
    if os.path.lexists(root) and (os.path.islink(root) or not os.path.isdir(root)):
        fail_state(f"preparation root at {root} exists and is not a real directory; left "
                   f"untouched")
    if not os.path.isdir(root):
        os.mkdir(root)
    stage = os.path.join(root, STAGE_PREFIX + local_stamp())
    try:
        os.mkdir(stage)
    except FileExistsError:
        fail_state(f"preparation stage stamp collision at {stage}: stages are never adopted "
                   f"or overwritten")
    log(f"preparation stage: {stage}")
    descriptor = os.open(os.path.join(stage, STARTED_NAME), os.O_CREAT | os.O_EXCL | os.O_WRONLY,
                         0o644)
    os.close(descriptor)
    os.mkdir(os.path.join(stage, "state"))
    state_path = os.path.join(stage, STATE_FILE_REL)
    lock_path = os.path.join(stage, LOCK_FILE_REL)
    state = {
        "version": STATE_VERSION,
        "mode": "prepare",
        "started": now_iso(),
        "pid": os.getpid(),
        "cache_root": cache,
        "stage": stage,
        "freeze_stage": freeze_record["stage"],
        "canonical_digest": freeze_record["canonical_digest"],
        "primaries": primaries,
        "alternates_recorded_never_activated": alternates,
        "space_estimate": estimate,
        "owned_outputs": owned,
    }
    write_state(state_path, state)
    acquire_lock(lock_path)
    journal = RunJournal(ledger_path(cache))
    try:
        # CHRONOLOGY ANCHOR: first ledger event, before any subprocess can run
        # (invoke refuses to run before this event exists).
        journal.freeze_validated_complete(freeze_record)
        # Source headers through EMBEDDED metadata only (gdalinfo -json; height-
        # semantic reads come strictly later, after dem_rasterio_start).
        dem_sources = extract["elevation_members"]
        rgb_sources = extract["imagery_members"]
        source_infos = {}
        for label, paths in (("elevation", dem_sources), ("imagery", rgb_sources)):
            for path in paths:
                run = journal.invoke(gdalinfo_argv(path), height_semantic=False,
                                     label=f"gdalinfo {label} source header")
                info = raster_header_info(parse_gdalinfo_json(run, f"{label} source header"),
                                          f"{label} source header", expect_epsg=None)
                source_infos[path] = info
        dem_gsd = check_native_gsd(source_infos[dem_sources[0]], DEM_GSD_EXPECTED_M,
                                   f"elevation source {os.path.basename(dem_sources[0])}")
        rgb_gsd = check_native_gsd(source_infos[rgb_sources[0]], RGB_GSD_EXPECTED_M,
                                   f"imagery source {os.path.basename(rgb_sources[0])}")
        # KML extent: pure quadrilateral, ONE batched cs2cs for the four corners.
        aoi = grid["playable_bounds"]
        corners_utm = [(aoi.x_min, aoi.y_min), (aoi.x_max, aoi.y_min),
                       (aoi.x_max, aoi.y_max), (aoi.x_min, aoi.y_max)]
        corners_wgs84 = convert_utm_batch(journal, corners_utm)
        kml_bytes = extent_kml_bytes(corners_wgs84)
        # Rasters and text outputs are built INSIDE the stage and promoted to the
        # owned paths only after every gate passes (see promote_output): a failed
        # attempt never leaves a partial file at a runtime path.
        stage_dem = os.path.join(stage, DEM_NAME)
        stage_rgb = os.path.join(stage, RGB_NAME)
        stage_kml = os.path.join(stage, KML_NAME)
        stage_manifest = os.path.join(stage, MANIFEST_NAME)
        stage_textures = os.path.join(stage, TEXTURE_MAP_NAME)
        stage_sources = os.path.join(stage, SOURCES_NAME)
        # DEM warp: the FIRST height-semantic command (dem_rasterio_start precedes
        # it in the ledger, after the freeze event by code order).
        dem_argv = dem_warp_argv(grid["window"], dem_gsd, dem_sources, stage_dem)
        journal.invoke(dem_argv, height_semantic=True, label="gdalwarp DEM")
        run = journal.invoke(gdalinfo_argv(stage_dem), height_semantic=False,
                             label="gdalinfo prepared DEM header")
        dem_header = raster_header_info(parse_gdalinfo_json(run, "prepared DEM header"),
                                        "prepared DEM header", expect_epsg="32631")
        if dem_header["band_type"] != "Float32":
            fail_state("prepared DEM band is not Float32")
        if dem_header["no_data_value"] != DEM_NODATA:
            fail_state(f"prepared DEM noDataValue {dem_header['no_data_value']!r} is not the "
                       f"pinned {DEM_NODATA!r}")
        assert_extent_covers_apron(actual_extent(dem_header, "prepared DEM"), grid,
                                   "prepared DEM actual extent")
        # RGB warp and header.
        rgb_argv = rgb_warp_argv(grid["rgb_window"], rgb_gsd, rgb_sources, stage_rgb)
        journal.invoke(rgb_argv, height_semantic=True, label="gdalwarp RGB")
        run = journal.invoke(gdalinfo_argv(stage_rgb), height_semantic=False,
                             label="gdalinfo prepared RGB header")
        rgb_header = raster_header_info(parse_gdalinfo_json(run, "prepared RGB header"),
                                        "prepared RGB header", expect_epsg="32631")
        if rgb_header["band_type"] != "Byte":
            fail_state("prepared RGB band is not uint8 (Byte)")
        rgb_gsd_observed = check_native_gsd(rgb_header, RGB_GSD_EXPECTED_M,
                                            "prepared RGB header")
        assert_extent_covers_apron(actual_extent(rgb_header, "prepared RGB"), grid,
                                   "prepared RGB actual extent")
        # Vertical origin: the extent minimum from the PREPARED DEM statistics
        # (never an assumed 0; distinct height-semantic event kind).
        run = journal.invoke(gdalinfo_argv(stage_dem, stats=True), height_semantic=True,
                             label="gdalinfo -stats prepared DEM")
        stats_info = parse_gdalinfo_json(run, "prepared DEM statistics")
        stats_band = (stats_info.get("bands") or [{}])[0]
        origin_meters = stats_band.get("minimum")
        if not _finite_number(origin_meters):
            fail_state("prepared DEM statistics carry no finite band minimum for the "
                       "verticalPolicy origin")
        origin_meters = float(origin_meters)
        # Primary controls must not fall on post-fill DEM NoData (explicit refusal).
        for record in selection["primary"]:
            resolve_primary_dem_value(journal, stage_dem, record, dem_header)
        # RGB controls: generated-grid centres, expected bytes from the prepared RGB.
        rgb_reference = resolve_rgb_controls(journal, stage_rgb, grid)
        rgb_controls = rgb_reference["controls"]
        # Manifest (real values), self-validated against the C# rules, then the
        # remaining operator files: writes happen only after every refusal gate.
        manifest_obj = build_manifest(selection, rgb_controls,
                                      os.path.join(PREPARED_DIR_REL, DEM_NAME),
                                      os.path.join(PREPARED_DIR_REL, RGB_NAME),
                                      os.path.join(PREPARED_DIR_REL, KML_NAME),
                                      dem_header, rgb_header, origin_meters)
        validate_generated_manifest(manifest_obj, primaries, forbidden_names=alternates)
        manifest_bytes = (json.dumps(manifest_obj, indent=2, sort_keys=True) + "\n").encode("utf-8")
        textures_bytes = (json.dumps(PREVIEW_TEXTURES, indent=2, sort_keys=True) + "\n") \
            .encode("utf-8")
        write_bytes_atomic(stage_kml, kml_bytes)
        write_bytes_atomic(stage_manifest, manifest_bytes)
        write_bytes_atomic(stage_textures, textures_bytes)
        plan = {
            "cache_root": cache,
            "freeze": freeze_record,
            "extract": extract,
            "kml_rel": os.path.join(PREPARED_DIR_REL, KML_NAME),
            "dem_rel": os.path.join(PREPARED_DIR_REL, DEM_NAME),
            "rgb_rel": os.path.join(PREPARED_DIR_REL, RGB_NAME),
            "kml_sha256": sha256_bytes(kml_bytes),
            "manifest_sha256": sha256_bytes(manifest_bytes),
            "textures_sha256": sha256_bytes(textures_bytes),
            "dem_sha256": sha256_file(stage_dem),
            "dem_size": os.path.getsize(stage_dem),
            "rgb_sha256": sha256_file(stage_rgb),
            "rgb_size": os.path.getsize(stage_rgb),
            "freeze_verified_at": journal.freeze_verified_at,
            "first_dem_raster_io_at": journal.first_dem_raster_io_at,
            "commands_note": "final chronology ledger sha256 is bound in "
                             "preparation/receipt.json (commands_sha256); sources.txt is "
                             "written before the preparation_completed event",
        }
        sources_bytes = sources_txt_bytes(plan)
        write_bytes_atomic(stage_sources, sources_bytes)
        outputs = {
            "manifest": {"path": owned["manifest"], "sha256": plan["manifest_sha256"],
                         "size": len(manifest_bytes)},
            "texture_map": {"path": owned["texture_map"], "sha256": plan["textures_sha256"],
                            "size": len(textures_bytes)},
            "sources_txt": {"path": owned["sources_txt"], "sha256": sha256_bytes(sources_bytes),
                            "size": len(sources_bytes)},
            "extent_kml": {"path": owned["extent_kml"], "sha256": plan["kml_sha256"],
                           "size": len(kml_bytes)},
            "dem_tif": {"path": owned["dem_tif"], "sha256": plan["dem_sha256"],
                        "size": plan["dem_size"]},
            "rgb_tif": {"path": owned["rgb_tif"], "sha256": plan["rgb_sha256"],
                        "size": plan["rgb_size"]},
        }
        # Promote: every gate has passed, so the outputs materialize at their
        # owned paths (os.link refuses existing targets; no operator overwrite,
        # no partial runtime files from failed attempts).
        prepared_dir = os.path.join(cache, PREPARED_DIR_REL)
        if os.path.lexists(prepared_dir) and (os.path.islink(prepared_dir)
                                              or not os.path.isdir(prepared_dir)):
            fail_state(f"prepared directory at {prepared_dir} exists and is not a real "
                       f"directory; left untouched")
        if not os.path.isdir(prepared_dir):
            os.mkdir(prepared_dir)
        promote_output(stage_kml, owned["extent_kml"], "extent_kml")
        promote_output(stage_dem, owned["dem_tif"], "dem_tif")
        promote_output(stage_rgb, owned["rgb_tif"], "rgb_tif")
        promote_output(stage_manifest, owned["manifest"], "manifest")
        promote_output(stage_textures, owned["texture_map"], "texture_map")
        promote_output(stage_sources, owned["sources_txt"], "sources_txt")
        journal.preparation_completed(
            {label: {"sha256": record["sha256"]} for label, record in outputs.items()})
        commands_sha = sha256_file(journal.ledger_path)
        # Chronology assertion of record: the code-order guarantee holds on the
        # executed ledger too (freeze never AFTER the first height read; the
        # strict precedence proof is the append-only event order itself, since
        # millisecond-resolution stamps can legitimately tie).
        if journal.first_dem_raster_io_at is None:
            fail_state("no dem_rasterio_start event was recorded; chronology broken")
        if _parse_iso(journal.freeze_verified_at) > _parse_iso(journal.first_dem_raster_io_at):
            fail_state("chronology order violated: freezeVerifiedAt is after "
                       "firstDemRasterIoAt")
        receipt_obj = {
            "receipt_version": STATE_VERSION,
            "tool": "sedan_prepare_inputs.py",
            "generated_at": now_iso(),
            "cache_root": cache,
            "stage": stage,
            "freeze": {
                "stage": freeze_record["stage"],
                "canonical_digest": freeze_record["canonical_digest"],
                "frozen_manifest_sha256": freeze_record["frozen_manifest_sha256"],
                "receipt_sha256": freeze_record["receipt_sha256"],
                "integrity_manifest_sha256": freeze_record["integrity_manifest_sha256"],
                "candidates_sha256": freeze_record["candidates_sha256"],
                "selection_sha256": freeze_record["selection_sha256"],
                "laz_tiles_sha256": dict(freeze_record["tiles"]),
            },
            "extract_receipt": {
                "path": extract["receipt_path"],
                "sha256": extract["receipt_sha256"],
                "members": extract["members"],
                "total_bytes": extract["total_bytes"],
            },
            "primaries": primaries,
            "alternates": alternates,
            "alternates_note": "recorded only; NEVER activated (no failure-based substitution)",
            "operator_policy": {
                "vertical_tolerance_meters": VERTICAL_TOLERANCE_M,
                "vertical_tolerance_scope": "engineering-only acceptance, never a certified "
                                            "source accuracy",
                "budget_b_game_units": BUDGET_B_GAME_UNITS,
                "vertical_datum_label": VERTICAL_DATUM_LABEL,
                "vertical_datum_note": "label only: NO EPSG:5720 geokey, no vertical "
                                       "conversion, no vertical shift",
                "vertical_accuracy_note": "verticalAccuracyMeters ABSENT: actual "
                                          "tile-specific source accuracy is unverified; a "
                                          "documented actual accuracy above 1.0 m escalates "
                                          "before any comparison",
                "no_data_fill_policy": f"nearest-valid within {NO_DATA_SEARCH_RADIUS_M} m "
                                       f"(one height lattice); edge fail; radius is pinned "
                                       f"and never silently widened",
            },
            "uncertainties": list(UNCERTAINTIES),
            "rgb_control_reference": {
                "source_role": "operator-input prepared 0.2 m RGB (not the raw JP2, "
                               "not the payload)",
                "source_path": os.path.join(PREPARED_DIR_REL, RGB_NAME),
                "source_sha256": rgb_reference["source_sha256"],
                "source_size": rgb_reference["source_size"],
                "pixels_per_axis": rgb_reference["pixels_per_axis"],
                "control_pixels": [control["pixel"][0] for control in rgb_controls],
                "output_spacing_m": rgb_reference["spacing_m"],
                "footprint_half_m": rgb_reference["footprint_half_m"],
                "rounding": "clamp to [0, 255], then round half away from zero (Core ToByte)",
                "tolerance_bytes": IMAGERY_TOLERANCE_BYTES,
            },
            "vertical_policy": {"originMeters": origin_meters,
                                "source": "prepared DEM extent minimum (gdalinfo -stats), "
                                          "never an assumed 0"},
            "chronology": {
                "freezeVerifiedAt": journal.freeze_verified_at,
                "firstDemRasterIoAt": journal.first_dem_raster_io_at,
                "note": "code order guarantees freezeVerifiedAt < firstDemRasterIoAt; no hash "
                        "certifies historic blindness",
            },
            "space_estimate": estimate,
            "outputs": outputs,
            "commands_sha256": commands_sha,
        }
        receipt_bytes = (json.dumps(receipt_obj, indent=2, sort_keys=True) + "\n").encode("utf-8")
        write_bytes_atomic(receipt_path(cache), receipt_bytes)
        completed = os.path.join(stage, COMPLETED_NAME)
        descriptor = os.open(completed, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o644)
        os.close(descriptor)
        state["completed"] = now_iso()
        state["receipt_sha256"] = sha256_bytes(receipt_bytes)
        state["commands_sha256"] = commands_sha
        write_state(state_path, state)
        log(f"preparation completed: manifest {plan['manifest_sha256']}, "
            f"ledger sha256 {commands_sha}, Z0 {origin_meters} m (extent minimum)")
        return receipt_obj
    finally:
        release_lock(lock_path)


def _parse_iso(value):
    return datetime.fromisoformat(value)


def run_preflight(cache):
    """READ-ONLY default: validate the freeze chain and the extract receipt, print
    the plan; writes NOTHING and spawns NOTHING."""
    check_protocol_constants()
    log(f"cache root: {cache}")
    check_no_data_radius()
    freeze_record = validate_freeze_chain(cache)
    log(f"freeze validated (structural, read-only): stage {freeze_record['stage']}, "
        f"canonical digest {freeze_record['canonical_digest']}")
    selection = parse_selection_tsv(freeze_record["selection_path"])
    primaries = [record["id"] for record in selection["primary"]]
    alternates = [record["id"] for record in selection["alternate"]]
    log(f"selection: primaries {primaries}; alternates recorded, never activated {alternates}")
    extract = validate_extract_receipt(cache)
    log(f"extract receipt verified: {extract['receipt_path']} "
        f"(members {extract['members']}, total {extract['total_bytes']} bytes)")
    manifest = read_json_bound(freeze_record["frozen_manifest_path"], "frozen manifest")
    transform = manifest.get("map_transform")
    if not isinstance(transform, dict):
        fail_state("frozen manifest carries no map_transform block")
    origin_recorded = transform.get("extent_origin_utm")
    if not isinstance(origin_recorded, list) or len(origin_recorded) != 2 \
            or any(not _finite_number(v) for v in origin_recorded):
        fail_state("frozen manifest map_transform extent_origin_utm is malformed")
    lattice_recorded = transform.get("lattice_spacing_m")
    if not _finite_number(lattice_recorded) or float(lattice_recorded) != groundtruth.LATTICE:
        fail_state("frozen manifest lattice drifted from the frozen spacing")
    if (float(origin_recorded[0]), float(origin_recorded[1])) \
            != tuple(groundtruth.DEFAULT_AOI.origin_utm):
        fail_state(f"frozen manifest extent origin {origin_recorded!r} disagrees with the "
                   f"canonical SW origin")
    grid = compute_chunk_grid(float(origin_recorded[0]), float(origin_recorded[1]),
                              groundtruth.DEFAULT_AOI.size, float(lattice_recorded))
    named_bounds = named_half_footprint_bounds(selection, groundtruth.HALF_FOOTPRINT)
    assert_window_covers_named_bounds(grid, named_bounds)
    assert_planned_windows_cover_apron(grid)
    window = grid["window"]
    rgb_window = grid["rgb_window"]
    minimal = grid["source_minimal_bounds"]
    playable = grid["playable_bounds"]
    log(f"grid: origin {grid['origin']}, {grid['n_chunks']} x {grid['n_chunks']} chunks of "
        f"{grid['chunk_m']} m, storage extent {grid['grid_m']} m, {grid['grid_points']} lattice "
        f"points per axis")
    log(f"playable AOI square (KML extent): [{playable.x_min}, {playable.y_min}] .. "
        f"[{playable.x_max}, {playable.y_max}]")
    minimal_margin = groundtruth.HALF_FOOTPRINT + SOURCE_KERNEL_REACH_M
    log(f"source-corner bounds (storage +/- {minimal_margin} m): [{minimal.x_min}, {minimal.y_min}] .. "
        f"[{minimal.x_max}, {minimal.y_max}]")
    log(f"DEM warp window (outward-aligned to {DEM_GSD_EXPECTED_M} m): [{window.x_min}, "
        f"{window.y_min}] .. [{window.x_max}, {window.y_max}]")
    log(f"RGB warp window (outward-aligned to {RGB_GSD_EXPECTED_M} m): [{rgb_window.x_min}, "
        f"{rgb_window.y_min}] .. [{rgb_window.x_max}, {rgb_window.y_max}]")
    log(f"apron footprints verified on both axes: global indices {apron_index_range(grid)[0]}.."
        f"{apron_index_range(grid)[-1]} (named bounds covered: {len(named_bounds)})")
    estimate = estimate_output_bytes(grid, DEM_GSD_EXPECTED_M, RGB_GSD_EXPECTED_M)
    log(f"declared space: DEM ~{estimate['dem_bytes']} B + RGB ~{estimate['rgb_bytes']} B = "
        f"{estimate['total_bytes']} B (+ reserve {RESERVE_BYTES} B in --prepare)")
    outputs = owned_output_paths(cache)
    for label, path in sorted(outputs.items()):
        state = "PRESENT (rerun would refuse without a completed receipt)" \
            if os.path.lexists(path) else "absent"
        log(f"planned output {label}: {path} ({state})")
    log(f"planned receipt: {receipt_path(cache)}; planned ledger: {ledger_path(cache)}")
    log("preflight complete; nothing was written, no directory was created, no subprocess was "
        "spawned, no network was touched, no height was read")


def resolve_cache_root(args):
    if args.root:
        return args.root, "positional"
    env_value = os.environ.get("PANOPTICON_GEO_PROOF_DIR")
    if env_value:
        return env_value, "env PANOPTICON_GEO_PROOF_DIR"
    return DEFAULT_CACHE_ROOT, "default"


def build_parser():
    parser = argparse.ArgumentParser(
        prog="sedan_prepare_inputs.py",
        description=(
            "Post-freeze Sedan runtime-input preparer: validates the completed final six-tile "
            "freeze and the extraction receipt, then prepares sedan-ign-proof.json, "
            "preview-textures.json, sources.txt, prepared EPSG:32631 GeoTIFFs (Float32 DEM + "
            "uint8 RGB), and the atomic preparation/receipt.json + commands.jsonl chronology "
            "ledger. NOT the provider-02 framework, NOT the fresh-cache bootstrap wrapper, and "
            "not a card completion; no numeric residual and no proof PASS is computed here."
        ),
        epilog=(
            f"cache root resolution order: 1) positional argument, 2) env "
            f"PANOPTICON_GEO_PROOF_DIR, 3) default {DEFAULT_CACHE_ROOT}. Modes: default = "
            f"read-only preflight (freeze chain + extract receipt + plan; never writes, never "
            f"spawns); --prepare = the explicit guarded write operation (own stage dir + "
            f"O_EXCL lock under <root>/preparation/, declared-space guard before writes, "
            f"idempotent verify-and-skip on a completed receipt, failures preserve the stage "
            f"dir + ledger and never overwrite operator files). Exit codes: 0 success; 2 guard "
            f"failure (space, mount, refusal); 3 subprocess failure or timeout; 4 state, "
            f"collision, or drift failure."
        ),
    )
    parser.add_argument(
        "root", nargs="?", default=None,
        help=(f"cache root; resolution order: positional > env PANOPTICON_GEO_PROOF_DIR > "
              f"default {DEFAULT_CACHE_ROOT}"))
    parser.add_argument(
        "--prepare", action="store_true",
        help="the explicit write operation; requires every guard and the complete verified "
             "freeze + extraction chain to pass first")
    return parser


def main(argv=None):
    parser = build_parser()
    args = parser.parse_args(argv)
    if sum(1 for given in (args.prepare,) if given) > 1:  # single explicit mode flag today
        parser.error("at most one mode flag is allowed")
    cache, source = resolve_cache_root(args)
    mode = "prepare" if args.prepare else "preflight"
    code = EXIT_OK
    try:
        if args.prepare:
            run_prepare(cache)
        else:
            run_preflight(cache)
    except Failure as failure:
        log(f"FAIL({failure.code}): {failure.message}")
        code = failure.code
    log(f"final status: exit={code} mode={mode} root={cache}")
    return code


if __name__ == "__main__":
    sys.exit(main())
