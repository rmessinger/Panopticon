#!/usr/bin/env python3
"""Final six-tile LiDAR ground-truth freeze orchestrator (protocol v1.2, section 6.5).

Sequentially measures the SIX pinned LiDAR HD tiles through the isolated worker
(sedan_lidar_probe_worker.py, SUBPROCESS only, never imported), merges the per-tile
candidate measurements into one unique canonical catalog, then feeds the UNCHANGED
canonical API (sedan_lidar_select.select with stage_1_preliminary=False followed by
sedan_lidar_select.freeze) and records the full provenance hash chain. The final
freeze runs BEFORE any DEM access: this tool has NO MNT/RGE/BD ORTHO path at all,
never opens a DEM, and computes NO numeric residual and NO proof PASS. It is not a
provider-02 framework. Transport of the remaining five tiles belongs to the acquire
unit (E fix cycle); this tool never fetches, never touches the network, and never
downloads or installs anything. The stage-1 preliminary selection can never be
frozen through this path: the orchestrator always requests the final selection and
B's freeze() refuses stage_1_preliminary selections outright.

Modes: default = READ-ONLY preflight (writes nothing, spawns no worker, touches no
network; prints the plan, the six pinned tile identities, and the required input
integrity manifest contract, reporting which tiles lack manifest entries without
failing on pending transport). --freeze = the explicit final operation (guarded,
single attempt, no retry). --recover-lock = remove a stale freeze lock whose
recorded pid is no longer alive. Mode flags are mutually exclusive.

Six-input contract (all verified BEFORE any worker or point reader runs; any
missing/unverified/size-mismatch/SHA-mismatch/pin-mismatch/footprint-missing tile
is exit 4): each tile must exist as a regular non-symlink file with the pinned
size at its verified location, and a per-input local integrity manifest (JSON
object mapping tile name -> {"size", "sha256", "footprint_l93"}; exact format
printed by the preflight) must verify read-only: recomputed file sha256 equal to
the manifest sha256, manifest size equal to the pinned size, and an observed header
footprint available from the manifest sidecar or (first tile) B's probe report.
Coverage and containment always come from observed headers, never filenames.
Each manifest entry may carry an optional "rel_path" recording the tile's ACTUAL
verified location under the cache root (the first tile stands at
lidarhd/<LOT>/<name>; the five stage-2 tiles stand at lidarhd/stage2/<LOT>/<name>
per the acquire receipt); when present it must be a relative path ending with the
pinned tile name and must resolve strictly under the cache root without symlink
components, and when absent the pinned lidarhd/<LOT>/<name> path is used. The
worker script itself is a configurable pipeline input with provenance: the
integrity manifest may carry an OPTIONAL top-level "worker" object
{"rel_path", "sha256"} pointing at a replacement worker (B's minimal bounded
budget entrypoint for the three oversized tiles) that must resolve strictly under
this file's scripts/geo sibling base, end .py, hash to the manifest sha256, and
appear in APPROVED_WORKER_SHA256S; when absent the pinned sibling
sedan_lidar_probe_worker.py is used and must itself hash into the approved
registry. Two entrypoints are approved today, both under the same sole-registry
discipline (unknown hashes exit 4; no silent fallback): the original
5db92080... pinned default and the banded 52e935b5... bounded-budget entrypoint
(accepted 2026-11-03 E PASS, commit 26fb77f95). A future release adds its file
sha256 to APPROVED_WORKER_SHA256S by name with a comment, plus tests; nothing
else changes.
The base measurement math is pinned where it lives: EXPECTED_BASE_WORKER_SHA256
is the ACTUAL measurement math base worker (the module whose evaluate/fit
functions execute the section-6 statistics), bound to the on-disk base worker
file before any spawn and validated against the provenance block every worker
output carries; SELECT_MODULE_SHA256 pins the SELECTION/freeze module
(sedan_lidar_select.py: ladder, freeze tables, canonical digest), which is not
the OLS base. A manifest worker override (for example the banded entrypoint) is
a deliberate pipeline input with NO silent after-failure fallback to the
original worker: a failed worker STOPs the run as today. Its output MUST carry
the additive provenance fields (executed entrypoint + base measure module +
select module sha256s) consistent with the pins; the original base worker's
legacy outputs may omit them, but present-and-wrong is always exit 4. Idempotence source chain: the freeze receipt is
constructed FIRST from operator inputs and scientific-artifact hashes only, and
the run state is written after it exists and records the receipt's size and
sha256 (state -> receipt, one-way; the receipt never contains its own hash or
the state's), so the idempotent rerun verifies the receipt against the state
inventory.

Engineered budget: one worker at a time under the worker's own frozen 1 GiB memory
guard (the request never raises it); per-worker wall deadline 3600 s; parent stdout,
stderr and worker output file each hard-capped at 64 MiB with the cap checked during
capture; the parent holds at most one tile's parsed records at a time during the
merge and releases them between workers, so total live parent+child buffers stay
within 1 GiB. One invocation is one attempt: any failure STOPs, preserves the
scratch, state and diagnostics, and never auto-reruns. A memory-guard trip by the
worker (its exit code 3) is reported verbatim with the exact tile and the worker's
own arithmetic table: no budget raise, no fallback, no second pass, no method change.

Idempotence: a completed freeze (COMPLETED marker plus intact artifacts) is
REVERIFIED (tile size/sha256, artifact hashes, canonical digest) and its exact
artifact paths are returned without rewriting anything and without spawning any
worker. Two independent fresh runs reproduce byte-identical candidates.tsv and
selection.tsv and an identical canonical digest; the frozen manifest itself is NOT
byte-identical across runs (it embeds generated_at), which the canonical digest
strips by design.

Exit codes: 0 success; 2 guard failure (floor STOP, structural merge STOP, missing
tooling, free space); 3 worker/process failure or operator interrupt (scratch,
state and diagnostics preserved); 4 state, pin, integrity, collision, or drift
failure. Python 3 standard library only; no RNG.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import resource
import selectors
import stat
import subprocess
import sys
import time
from datetime import datetime

# Canonical stage-1/freeze selection module (protocol v1.2): stdlib-only sibling.
# select(), freeze(), canonical_digest() and the evaluate/classify constants are
# reused verbatim; the method is never re-implemented here. The worker
# (sedan_lidar_probe_worker.py) is spawned, never imported; sedan_acquire.py and
# sedan_lidar_probe.py are neither imported nor read (probe imports acquire at
# module load, and both are owned by other units).
try:
    import sedan_lidar_select as groundtruth
except ImportError:  # pragma: no cover - defensive path for unusual loaders
    _SELECT_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "sedan_lidar_select.py")
    import importlib.util
    _select_spec = importlib.util.spec_from_file_location("sedan_lidar_select", _SELECT_PATH)
    groundtruth = importlib.util.module_from_spec(_select_spec)
    sys.modules["sedan_lidar_select"] = groundtruth
    _select_spec.loader.exec_module(groundtruth)

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


# ---------------------------------------------------------------------------
# Pinned identities: six LiDAR HD tiles (mirrors sedan_lidar_probe.py lines
# 116-145; the probe file itself is never imported). Order is the pinned
# processing order: stage-1 first tile, then the five stage-2 tiles in the
# probe's recorded order.
# ---------------------------------------------------------------------------

LOT = "NUALHD_1-0__LAZ_LAMB93_OD_2026-04-29"
FIRST_TILE_NAME = "LHD_FXX_0840_6958_PTS_LAMB93_IGN69.copc.laz"
FIRST_TILE_SIZE = 128_651_625
OTHER_TILES = {
    "LHD_FXX_0839_6958_PTS_LAMB93_IGN69.copc.laz": 119_931_651,
    "LHD_FXX_0839_6959_PTS_LAMB93_IGN69.copc.laz": 146_099_989,
    "LHD_FXX_0840_6959_PTS_LAMB93_IGN69.copc.laz": 130_615_694,
    "LHD_FXX_0841_6958_PTS_LAMB93_IGN69.copc.laz": 153_352_444,
    "LHD_FXX_0841_6959_PTS_LAMB93_IGN69.copc.laz": 151_880_730,
}


def _pin(name, size):
    return {"name": name, "size": size, "rel": os.path.join("lidarhd", LOT, name)}


TILE_PINS = (
    _pin(FIRST_TILE_NAME, FIRST_TILE_SIZE),
    _pin("LHD_FXX_0839_6958_PTS_LAMB93_IGN69.copc.laz", 119_931_651),
    _pin("LHD_FXX_0839_6959_PTS_LAMB93_IGN69.copc.laz", 146_099_989),
    _pin("LHD_FXX_0840_6959_PTS_LAMB93_IGN69.copc.laz", 130_615_694),
    _pin("LHD_FXX_0841_6958_PTS_LAMB93_IGN69.copc.laz", 153_352_444),
    _pin("LHD_FXX_0841_6959_PTS_LAMB93_IGN69.copc.laz", 151_880_730),
)
FIRST_TILE_PIN = TILE_PINS[0]
TOTAL_PINNED_BYTES = sum(pin["size"] for pin in TILE_PINS)

DEFAULT_CACHE_ROOT = "/mnt/pit2/panopticon/geo-proof/sedan"
MOUNT_POINT = "/mnt/pit2"
PROC_MOUNTS_PATH = "/proc/mounts"
FREEZE_DIR_REL = "freeze"
INTEGRITY_MANIFEST_NAME = "lidar-integrity-manifest.json"
STAGE_PREFIX = "sedan-freeze-"
STATE_VERSION = 1
STATE_FILE_REL = os.path.join("state", "state.json")
LOCK_FILE_REL = os.path.join("state", "lock")
STARTED_NAME = "STARTED"
COMPLETED_NAME = "COMPLETED"
REQUESTS_DIR_REL = "requests"
RAW_DIR_REL = "raw"
DIAG_DIR_REL = "diagnostics"
FINAL_DIR_REL = "final"
RECEIPT_NAME = "freeze-receipt.json"
RESERVE_BYTES = 1 << 30
CHUNK_SIZE = 1 << 20
READ_BOUND_BYTES = 64 << 20

# Worker invocation constants (mirrors sedan_lidar_probe.py lines 216-219).
WORKER_TIMEOUT_SECONDS = 3600.0
WORKER_CHUNK_POINTS = 2_000_000
WORKER_MEMORY_BUDGET_BYTES = 1 << 30
WORKER_OUTPUT_CAP = 64 << 20          # per stream and per output file, checked during capture
PROBE_MARGIN_M = 50.0                 # filter window margin around the AOI bbox (probe convention)
COVERAGE_CELL_M = 10.0                # worker request cell_size_m (base 10 m coverage map)
TABLE_BOUND_BYTES = 64 << 20          # worst-case bound reserved per output table
SCRATCH_TOTAL_BYTES = 6 * WORKER_OUTPUT_CAP + 2 * TABLE_BOUND_BYTES

TOOLS_DIR_REL = os.path.join("tools", "lidar-venv")     # probe layout, tools dir
VENV_PYTHON_REL = os.path.join(TOOLS_DIR_REL, "bin", "python")
PROBE_STATE_DIR_REL = os.path.join("lidarhd", "state")
PROBE_REPORT_V12_NAME = "probe-report-v1_2.json"

# Operator-approved numeric tolerance decision memo (2026-11-03, BEFORE any DEM
# comparison; recorded in docs/roadmap/deep-dives/r2-sedan-ground-truth.md section 7).
OPERATOR_POLICY_PATH = "/home/rmessinger/log/track-r/sedan-numeric-tolerance-20261103T1855.md"
OPERATOR_POLICY_SHA256 = "d4e25a8f94da4f3228ed22377bb738881be50f9e20342af10bf595c675d57cbc"

# ACTUAL measurement math base worker: the module whose measure()/fit functions
# execute the section-6 statistics (streaming reduction, OLS plane, Huber
# passes, curvature bound). NOT the selection module: sedan_lidar_select.py
# (SELECT_MODULE_SHA256) owns the ladder, freeze tables and canonical digest.
EXPECTED_BASE_WORKER_SHA256 = "5db920802e4a2da484aee02b598774bb40eb8e5152ed6328f5ff711fe0d3a6da"
# Banded bounded-budget entrypoint (accepted 2026-11-03 E PASS, commit
# 26fb77f95; same REQUEST_JSON contract, unchanged base math): emits the
# additive output provenance fields this freeze validates after each capture.
BANDED_WORKER_SHA256 = "52e935b5360c0d10563a581e650df3ac1637512eb30bc2cfc953d59cf12d2ab0"

# Approved worker-script hash registry: the base worker above and the banded
# entrypoint above, both named by comment. Sole-registry discipline: an
# unlisted worker hash is a state failure (exit 4).
APPROVED_WORKER_SHA256S = (
    EXPECTED_BASE_WORKER_SHA256,
    BANDED_WORKER_SHA256,
)

# Pinned SELECTION/freeze module (ladder, freeze tables, canonical digest),
# reused verbatim by this orchestrator; it is NOT the measurement math base
# (that is EXPECTED_BASE_WORKER_SHA256, the base worker). Updates only with an
# accepted science change; drift is a state failure (exit 4) naming both hashes.
SELECT_MODULE_SHA256 = "4570c3507c6fb99442af0691f010a7406f64306622587abce140728873e913a1"

POLICY_REQUIRED_SNIPPETS = (
    "1.0 m",
    "ENGINEERING ACCEPTANCE ONLY",
    "No site pruning",
    "No failure-based alternate activation",
    "tolerances.verticalMeters = 1.0 explicitly",
    "verticalAccuracyMeters stays NULL/absent",
    "ESCALATE before any DEM comparison",
    "2024 auto-classification bias",
    "2012-2016 versus 2024 epoch",
    "0.0390625 game units",
    "restoredEngineMeters = Z0 + (E + C) * metersPerGameUnit",
    "Z0 is the ACTUAL verticalOriginMeters, not an assumed 0",
)

# Canonical lattice geometry, asserted against the pinned module at run time:
# SW anchor 639841.997 / 5506768.179 (EPSG:32631), 3.048 m lattice, 359 x 359
# canonical cells inside the one-cell margin, 361 x 361 full-extent cells, full-AOI
# 3 x 3 sector grid.
PINNED_LATTICE_ORIGIN_UTM = (639841.997, 5506768.179)
PINNED_AOI_CENTRE_UTM = (640391.997, 5507318.179)
PINNED_LATTICE = 3.048


def now_iso():
    return datetime.now().astimezone().isoformat(timespec="milliseconds")


def log(message):
    print(f"{now_iso()} {message}", flush=True)


def local_stamp():
    return datetime.now().astimezone().isoformat(timespec="milliseconds").replace(":", ".")


def pid_alive(pid):
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    except OSError:
        return True
    return True


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


# ---------------------------------------------------------------------------
# Pure table validation (first in every mode that plans or freezes).
# ---------------------------------------------------------------------------


def validate_tile_table():
    """Pure and first: the pinned six-tile table must be exactly the frozen set."""
    if len(TILE_PINS) != 6:
        fail_state(f"tile table holds {len(TILE_PINS)} entries, expected 6")
    seen = set()
    for pin in TILE_PINS:
        name = pin["name"]
        if not name.endswith(".copc.laz"):
            fail_state(f"tile {name!r} does not carry the pinned .copc.laz suffix")
        if name in seen:
            fail_state(f"tile {name!r} is pinned more than once")
        seen.add(name)
        if pin["size"] <= 0:
            fail_state(f"tile {name!r} pins non-positive size {pin['size']}")
        rel = pin["rel"]
        if os.path.isabs(rel) or "\\" in rel:
            fail_state(f"tile {name!r} relative path is not relative/posix: {rel!r}")
        components = rel.split(os.sep)
        if any(component in ("", ".", "..") for component in components):
            fail_state(f"tile {name!r} relative path has an empty, '.', or '..' component")
    expected_total = FIRST_TILE_SIZE + sum(OTHER_TILES[name] for name, _ in
                                           ((p["name"], p["size"]) for p in TILE_PINS[1:]))
    if TOTAL_PINNED_BYTES != expected_total:
        fail_state(f"tile table totals {TOTAL_PINNED_BYTES} bytes, expected {expected_total}")
    return TILE_PINS


# ---------------------------------------------------------------------------
# Integrity manifest (per-input local integrity manifest; produced by the future
# accepted acquire receipt, never written by this tool).
# ---------------------------------------------------------------------------


def freeze_root(cache):
    return os.path.join(cache, FREEZE_DIR_REL)


def integrity_manifest_path(cache):
    return os.path.join(freeze_root(cache), INTEGRITY_MANIFEST_NAME)


def probe_report_path(cache):
    return os.path.join(cache, PROBE_STATE_DIR_REL, PROBE_REPORT_V12_NAME)


def _finite_number(value):
    return (
        isinstance(value, (int, float))
        and not isinstance(value, bool)
        and math.isfinite(value)
    )


def validate_footprint(footprint, label):
    """(floats, problem): four finite edges forming a non-degenerate rectangle."""
    if not isinstance(footprint, dict):
        return None, f"{label} is not a JSON object"
    edges = {}
    for edge in ("x_min", "y_min", "x_max", "y_max"):
        value = footprint.get(edge)
        if not _finite_number(value):
            raw = {key: footprint.get(key) for key in ("x_min", "y_min", "x_max", "y_max")}
            return None, f"{label} edge {edge} is missing, non-numeric or non-finite in {raw!r}"
        edges[edge] = float(value)
    if edges["x_max"] <= edges["x_min"] or edges["y_max"] <= edges["y_min"]:
        return None, f"{label} is degenerate (non-positive extent)"
    return edges, None


def read_integrity_manifest(path):
    """Parse the integrity manifest read-only. (entries, worker_cfg, present,
    problem): entries maps tile name -> validated record; worker_cfg is the
    optional top-level worker override block or None; present is False when the
    file is absent (reported, not failed: the manifest is produced by the acquire
    unit)."""
    if not os.path.lexists(path):
        return {}, None, False, None
    if os.path.islink(path) or not os.path.isfile(path):
        return {}, None, True, f"integrity manifest at {path} is not a regular non-symlink file"
    try:
        with open(path, "r", encoding="utf-8") as handle:
            manifest = json.load(handle)
    except (OSError, ValueError) as error:
        return {}, None, True, f"integrity manifest at {path} is not readable JSON: {error}"
    if not isinstance(manifest, dict):
        return {}, None, True, f"integrity manifest at {path} is not a JSON object"
    pinned_names = {pin["name"] for pin in TILE_PINS}
    unknown = sorted(set(manifest) - pinned_names - {"worker"})
    if unknown:
        return {}, None, True, (
            f"integrity manifest carries entries outside the six pinned tiles and the "
            f"optional worker block: {unknown}")
    worker_block = manifest.get("worker")
    if worker_block is not None:
        if not isinstance(worker_block, dict):
            return {}, None, True, "integrity manifest worker block is not a JSON object"
        worker_rel = worker_block.get("rel_path")
        if not isinstance(worker_rel, str) or not worker_rel:
            return {}, None, True, (
                "integrity manifest worker block carries no non-empty rel_path string")
        worker_sha = worker_block.get("sha256")
        if not isinstance(worker_sha, str) or len(worker_sha) != 64 \
                or any(char not in "0123456789abcdef" for char in worker_sha):
            return {}, None, True, (
                "integrity manifest worker block carries no 64-hex sha256")
        worker_block = {"rel_path": worker_rel, "sha256": worker_sha}
    entries = {}
    for pin in TILE_PINS:
        name = pin["name"]
        if name not in manifest:
            continue
        entry = manifest[name]
        if not isinstance(entry, dict):
            return {}, None, True, f"integrity manifest entry for {name} is not a JSON object"
        size = entry.get("size")
        if not isinstance(size, int) or isinstance(size, bool) or size <= 0:
            return {}, None, True, f"integrity manifest entry for {name} carries no positive integer size"
        digest = entry.get("sha256")
        if not isinstance(digest, str) or len(digest) != 64 \
                or any(char not in "0123456789abcdef" for char in digest):
            return {}, None, True, f"integrity manifest entry for {name} carries no 64-hex sha256"
        footprint = entry.get("footprint_l93")
        if footprint is not None:
            _edges, problem = validate_footprint(footprint, f"manifest footprint for {name}")
            if problem is not None:
                return {}, None, True, problem
        rel_path = entry.get("rel_path")
        if rel_path is not None and (not isinstance(rel_path, str) or not rel_path):
            return {}, None, True, f"integrity manifest rel_path for {name} must be a non-empty string"
        entries[name] = {"size": size, "sha256": digest, "footprint_l93": footprint,
                         "rel_path": rel_path}
    return entries, worker_block, True, None


GLOB_CHARS = "*?[]"


def resolve_tile_rel(pin, entry):
    """Validated relative cache-root path for one tile: the manifest's optional
    rel_path override when present, else the pinned rel path. A rel_path that is
    absolute, carries a glob metacharacter or NUL, has an empty/'.'/'..'
    component, or does not end with the pinned tile name is a state failure
    (exit 4, pin mismatch)."""
    rel = entry.get("rel_path") if entry else None
    if rel is None:
        return pin["rel"]
    if os.path.isabs(rel) or "\\" in rel or "\x00" in rel:
        fail_state(f"integrity manifest rel_path for {pin['name']} is not a relative posix "
                   f"path: {rel!r}")
    if any(char in rel for char in GLOB_CHARS):
        fail_state(f"integrity manifest rel_path for {pin['name']} carries a glob "
                   f"metacharacter: {rel!r}")
    if any(component in ("", ".", "..") for component in rel.split("/")):
        fail_state(f"integrity manifest rel_path for {pin['name']} has an empty, '.', or '..' "
                   f"component: {rel!r}")
    if not rel.endswith(pin["name"]):
        fail_state(f"integrity manifest rel_path for {pin['name']} does not end with the pinned "
                   f"tile name {pin['name']!r}: {rel!r} (pin mismatch)")
    return rel


def resolve_under_cache(cache, rel, label):
    """Absolute cache-root path for a validated relative rel, refusing any
    symlink component (no-follow walk over every prefix) and refusing anything
    that resolves outside the cache root."""
    root = os.path.normpath(cache)
    path = os.path.normpath(os.path.join(cache, rel))
    if path != root and not path.startswith(root + os.sep):
        fail_state(f"{label} resolves outside the cache root: {rel!r}")
    probe = ""
    for component in (piece for piece in path.split(os.sep) if piece):
        probe += os.sep + component
        if os.path.islink(probe):
            fail_state(f"symlink component in the {label} path is not allowed: {probe}")
    return path


def worker_base_dir():
    """Sibling base for worker-script overrides: this file's scripts/geo
    directory. Tests patch this to a temp base; the real path stays first-class."""
    return os.path.dirname(os.path.abspath(__file__))


def select_module_path():
    """Real location of the pinned base-math module (this file's scripts/geo
    sibling). Deliberately NOT routed through the patchable worker override base:
    the science module is hash-pinned where it lives, never relocated."""
    return os.path.join(os.path.dirname(os.path.abspath(__file__)), "sedan_lidar_select.py")


def worker_script_path():
    return os.path.join(worker_base_dir(), "sedan_lidar_probe_worker.py")


def base_worker_file_path():
    """Real repo location of the base measurement worker. Directly anchored like
    select_module_path(): the math-base identity is hash-checked where the file
    lives and is never relocated by the patchable override base."""
    return os.path.join(os.path.dirname(os.path.abspath(__file__)),
                        "sedan_lidar_probe_worker.py")


def validate_worker_rel(rel):
    """Shape validation for the manifest worker rel_path override: relative
    posix, no NUL or glob metacharacters, no empty/'.'/'..' components, must end
    .py. Any defect is a state failure (exit 4)."""
    label = "integrity manifest worker rel_path"
    if not isinstance(rel, str) or not rel or os.path.isabs(rel) or "\\" in rel or "\x00" in rel:
        fail_state(f"{label} is not a relative posix path: {rel!r}")
    if any(char in rel for char in GLOB_CHARS):
        fail_state(f"{label} carries a glob metacharacter: {rel!r}")
    if any(component in ("", ".", "..") for component in rel.split("/")):
        fail_state(f"{label} has an empty, '.', or '..' component: {rel!r}")
    if not rel.endswith(".py"):
        fail_state(f"{label} must end .py: {rel!r}")
    return rel


def resolve_worker_script(worker_cfg):
    """Resolved worker record for the spawn pipeline: {"path", "sha256",
    "source"}. Without a manifest worker block this is the pinned sibling
    sedan_lidar_probe_worker.py; with one, rel_path resolves (no-follow walk)
    strictly under the scripts/geo sibling base. The file's sha256 must equal the
    manifest sha256 (override case) and must appear in APPROVED_WORKER_SHA256S in
    every case; drift or an unlisted hash is exit 4 naming the hashes."""
    if worker_cfg is None:
        path = worker_script_path()
        if os.path.islink(path) or not os.path.isfile(path):
            fail_guard(f"worker script missing at {path}; it is spawned, never imported")
        source = "pinned default"
    else:
        rel = validate_worker_rel(worker_cfg["rel_path"])
        path = resolve_under_cache(worker_base_dir(), rel, "worker script override")
        if os.path.islink(path) or not os.path.isfile(path):
            fail_state(f"worker script override missing at {path}")
        source = "manifest override"
    digest = sha256_file(path)
    if worker_cfg is not None and digest != worker_cfg["sha256"]:
        fail_state(f"worker script override sha256 mismatch: file {digest} vs manifest "
                   f"{worker_cfg['sha256']}")
    if digest not in APPROVED_WORKER_SHA256S:
        fail_state(f"worker script sha256 drift: {digest} at {path} is not in the approved "
                   f"registry {list(APPROVED_WORKER_SHA256S)}; add B's released worker hash "
                   f"to APPROVED_WORKER_SHA256S by name before spawning")
    return {"path": path, "sha256": digest, "source": source}


# ---------------------------------------------------------------------------
# B's probe state (read-only, bounded): the executed cs2cs record is the pinned
# source of the L93 lattice anchor and AOI/filter rectangles. B's probe state is
# never written by this tool.
# ---------------------------------------------------------------------------


def load_probe_report(path):
    """(report, problem): bounded, read-only JSON load of B's v1.2 probe report."""
    if not os.path.lexists(path):
        return None, f"probe state report missing at {path}"
    if os.path.islink(path) or not os.path.isfile(path):
        return None, f"probe state report at {path} is not a regular non-symlink file"
    if os.path.getsize(path) > READ_BOUND_BYTES:
        return None, f"probe state report at {path} exceeds the {READ_BOUND_BYTES}-byte read bound"
    try:
        with open(path, "r", encoding="utf-8") as handle:
            report = json.load(handle)
    except (OSError, ValueError) as error:
        return None, f"probe state report at {path} is not readable JSON: {error}"
    if not isinstance(report, dict):
        return None, f"probe state report at {path} is not a JSON object"
    return report, None


def probe_frozen_geometry(report, path):
    """(geometry, problem): the executed lattice anchor and AOI/filter rectangles.

    Required report fields (written by B's probe at execution time): cs2cs.
    output_corners_l93 (8 corners: AOI then filter), cs2cs.lattice_anchor_32631
    (must equal the pinned SW origin exactly), cs2cs.lattice_anchor_l93 (must
    equal lattice_v1_2.origin_l93), protocol_version "1.2", and the pinned first
    tile name. The executed conversion record is used verbatim; nothing is
    re-converted here and no cs2cs binary is invoked."""
    projection = report.get("projection_v1_2")
    cs2cs_info = report.get("cs2cs")
    lattice = report.get("lattice_v1_2")
    for label, block in (("projection_v1_2", projection), ("cs2cs", cs2cs_info),
                         ("lattice_v1_2", lattice)):
        if not isinstance(block, dict):
            return None, f"probe report at {path} carries no {label} object"
    if report.get("protocol_version") != groundtruth.PROTOCOL_VERSION:
        return None, (
            f"probe report at {path} records protocol_version "
            f"{report.get('protocol_version')!r}, expected {groundtruth.PROTOCOL_VERSION!r}"
        )
    if report.get("tile") != FIRST_TILE_NAME:
        return None, (
            f"probe report at {path} records tile {report.get('tile')!r}, expected "
            f"{FIRST_TILE_NAME!r}"
        )
    anchor_utm = projection.get("lattice_anchor_32631")
    if not isinstance(anchor_utm, list) or len(anchor_utm) != 2 \
            or any(not _finite_number(value) for value in anchor_utm) \
            or (float(anchor_utm[0]), float(anchor_utm[1])) != PINNED_LATTICE_ORIGIN_UTM:
        return None, (
            f"probe report lattice_anchor_32631 {anchor_utm!r} does not equal the pinned SW "
            f"origin {list(PINNED_LATTICE_ORIGIN_UTM)!r}"
        )
    anchor_l93 = projection.get("lattice_anchor_l93")
    origin_l93 = lattice.get("origin_l93")
    if not isinstance(anchor_l93, list) or len(anchor_l93) != 2 \
            or any(not _finite_number(value) for value in anchor_l93):
        return None, f"probe report lattice_anchor_l93 {anchor_l93!r} is not an [x, y] pair"
    if origin_l93 != anchor_l93:
        return None, (
            f"probe report lattice_v1_2.origin_l93 {origin_l93!r} disagrees with "
            f"lattice_anchor_l93 {anchor_l93!r}"
        )
    corners = cs2cs_info.get("output_corners_l93")
    if not isinstance(corners, list) or len(corners) != 8 \
            or any(not isinstance(point, list) or len(point) != 2
                   or any(not _finite_number(value) for value in point) for point in corners):
        return None, f"probe report cs2cs.output_corners_l93 is not eight [x, y] corners"
    aoi_bbox = bbox_from_corners(corners[:4])
    filter_bbox = bbox_from_corners(corners[4:8])
    for label, box in (("aoi_bbox_l93", aoi_bbox), ("filter_bbox_l93", filter_bbox)):
        _edges, problem = validate_footprint(box, f"probe report {label}")
        if problem is not None:
            return None, problem
    return {
        "lattice_anchor_32631": [float(anchor_utm[0]), float(anchor_utm[1])],
        "lattice_anchor_l93": [float(anchor_l93[0]), float(anchor_l93[1])],
        "aoi_bbox_l93": aoi_bbox,
        "filter_bbox_l93": filter_bbox,
        "report_path": path,
    }, None


def probe_first_tile_footprint(report, path):
    """(footprint, problem): the first tile's observed header footprint from B's
    probe gates (fallback source when the manifest sidecar omits it)."""
    gates = report.get("gates")
    if not isinstance(gates, dict):
        return None, f"probe report at {path} carries no gates object"
    gate_d = gates.get("d_v1_2")
    if not isinstance(gate_d, dict):
        return None, f"probe report at {path} carries no gates.d_v1_2 object"
    footprint, problem = validate_footprint(
        gate_d.get("header_footprint_l93"), f"probe report header footprint ({FIRST_TILE_NAME})")
    return footprint, problem


def bbox_from_corners(corners):
    xs = [point[0] for point in corners]
    ys = [point[1] for point in corners]
    return {"x_min": min(xs), "y_min": min(ys), "x_max": max(xs), "y_max": max(ys)}


# ---------------------------------------------------------------------------
# Mount realness, ancestor walk (no symlink components), free space.
# ---------------------------------------------------------------------------


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
    """True when the exact rw mount entry for MOUNT_POINT exists; preflight
    reports a False return, --freeze refuses on it."""
    if not (root == MOUNT_POINT or root.startswith(MOUNT_POINT + os.sep)):
        log(f"mount realness NOT verified: {root} is not under the default prefix {MOUNT_POINT}")
        return False
    for mount_point, options in read_mounts():
        if mount_point == MOUNT_POINT and "rw" in options:
            log(f"mount realness verified: {MOUNT_POINT} is a rw mount")
            return True
    fail_guard(
        f"requires an exact rw mount entry with mount point {MOUNT_POINT} in {PROC_MOUNTS_PATH}: "
        f"refusing to create anything on a possibly-unmounted path (no fallback, no faked mounts)"
    )


def check_freeze_ancestor(path, label):
    """Walk every path component refusing symlinks, then verify the nearest
    existing ancestor is a real rwx directory."""
    probe = ""
    for component in (piece for piece in path.split(os.sep) if piece):
        probe += os.sep + component
        if os.path.islink(probe):
            fail_guard(f"symlink component in the {label} path is not allowed: {probe}")
    ancestor = path
    while not os.path.exists(ancestor):
        parent = os.path.dirname(ancestor)
        if parent == ancestor:
            fail_guard(f"no existing ancestor found for the {label} path")
        ancestor = parent
    if os.path.islink(ancestor):
        fail_guard(f"nearest existing {label} ancestor is a symlink, not a real directory: {ancestor}")
    if not os.path.isdir(ancestor):
        if ancestor == path:
            fail_state(f"operator collision: {path} exists and is not a directory; left untouched")
        fail_guard(f"nearest existing {label} ancestor is not a real directory: {ancestor}")
    for mode, mode_label in ((os.R_OK, "readable"), (os.W_OK, "writable"), (os.X_OK, "executable")):
        # os.access always grants the superuser, so the writability probe is meaningless there.
        if os.geteuid() == 0 and mode == os.W_OK:
            continue
        if not os.access(ancestor, mode):
            fail_guard(f"nearest existing {label} ancestor {ancestor} is not {mode_label}")
    log(f"{label} ancestor ok: {ancestor}")
    return ancestor


def check_free_space(ancestor, required_bytes):
    usage = statvfs(ancestor)
    free = usage.f_bavail * usage.f_frsize
    log(f"free space: free={free} required={required_bytes} "
        f"(own scratch bound {SCRATCH_TOTAL_BYTES} + reserve {RESERVE_BYTES})")
    if free < required_bytes:
        fail_guard(f"insufficient free space on {ancestor}: free={free} required={required_bytes} "
                   f"remaining={required_bytes - free}")


# ---------------------------------------------------------------------------
# Protocol constants and operator policy (drift = exit 4).
# ---------------------------------------------------------------------------


def check_protocol_constants():
    """Assert the pinned module still carries the frozen protocol constants; any
    drift is a state failure before anything is created or spawned."""
    if groundtruth.PROTOCOL_VERSION != "1.2":
        fail_state(f"sedan_lidar_select PROTOCOL_VERSION is {groundtruth.PROTOCOL_VERSION!r}, "
                   f"expected '1.2'")
    centre = tuple(groundtruth.DEFAULT_AOI.centre_utm)
    origin = tuple(groundtruth.DEFAULT_AOI.origin_utm)
    if centre != PINNED_AOI_CENTRE_UTM:
        fail_state(f"AOI centre {centre!r} drifted from the pinned {PINNED_AOI_CENTRE_UTM!r}")
    if origin != PINNED_LATTICE_ORIGIN_UTM:
        fail_state(f"lattice SW origin {origin!r} drifted from the pinned "
                   f"{PINNED_LATTICE_ORIGIN_UTM!r}")
    if groundtruth.LATTICE != PINNED_LATTICE:
        fail_state(f"lattice spacing {groundtruth.LATTICE!r} drifted from {PINNED_LATTICE!r}")
    if groundtruth.WINDOW_R != 2.5 or groundtruth.E23_DIST != PINNED_LATTICE \
            or groundtruth.ONE_CELL_MARGIN != 1:
        fail_state("window/E2-E3 radii or the one-cell margin drifted from the frozen values")
    if len(groundtruth.enumerate_lattice_cells()) != 128_881 \
            or groundtruth.full_extent_cell_counts() != (361, 361):
        fail_state("canonical lattice enumeration drifted from 359 x 359 inside a 361 x 361 extent")
    if groundtruth.MIN_PRIMARY != 5 or groundtruth.MIN_SECTORS != 3 \
            or groundtruth.N_ALTERNATES != 2 or groundtruth.MIN_SPACING != 100.0:
        fail_state("selection floors (5 primaries, 3 sectors, 2 alternates, 100 m) drifted")
    log("protocol constants verified: v1.2, SW origin 639841.997/5506768.179, lattice 3.048 m, "
        "359 x 359 canonical cells, 3 x 3 sectors, 5+2 floors")


def check_operator_policy(path):
    """The operator-approved tolerance memo must exist, hash to the recorded
    sha256, and still encode Budget A/B and the actual-Z0 wording verbatim."""
    if not os.path.lexists(path):
        fail_state(f"operator policy memo missing at {path}")
    if os.path.islink(path) or not os.path.isfile(path):
        fail_state(f"operator policy memo at {path} is not a regular non-symlink file")
    if os.path.getsize(path) > READ_BOUND_BYTES:
        fail_state(f"operator policy memo at {path} exceeds the {READ_BOUND_BYTES}-byte read bound")
    digest = sha256_file(path)
    if digest != OPERATOR_POLICY_SHA256:
        fail_state(f"operator policy memo sha256 {digest} does not match the recorded "
                   f"{OPERATOR_POLICY_SHA256}")
    with open(path, "r", encoding="utf-8") as handle:
        text = handle.read()
    normalized = " ".join(text.split())
    missing = [snippet for snippet in POLICY_REQUIRED_SNIPPETS if snippet not in normalized]
    if missing:
        fail_state(f"operator policy memo no longer encodes: {missing!r}")
    log(f"operator policy verified: Budget A 1.0 m engineering-only + Budget B 0.0390625 game "
        f"units, actual-Z0 wording present (sha256 {digest})")


def venv_python_path(cache):
    return os.path.join(cache, VENV_PYTHON_REL)


def check_tooling(cache, worker_cfg):
    """Read-only presence checks for the spawn-time tooling plus the approved
    worker resolution and the pinned base-math module hash (guard failures: the
    worker is never spawned and nothing is installed from here)."""
    resolution = resolve_worker_script(worker_cfg)
    select_path = select_module_path()
    if os.path.islink(select_path) or not os.path.isfile(select_path):
        fail_guard(f"selection module missing at {select_path}")
    select_sha = sha256_file(select_path)
    if select_sha != SELECT_MODULE_SHA256:
        fail_state(f"selection module sha256 drift: {select_sha} at {select_path} does not "
                   f"equal the pinned {SELECT_MODULE_SHA256}")
    base_worker = base_worker_file_path()
    if os.path.islink(base_worker) or not os.path.isfile(base_worker):
        fail_guard(f"base measurement worker script missing at {base_worker}")
    base_sha = sha256_file(base_worker)
    if base_sha != EXPECTED_BASE_WORKER_SHA256:
        fail_state(f"base measurement worker sha256 drift: {base_sha} at {base_worker} does "
                   f"not equal the pinned {EXPECTED_BASE_WORKER_SHA256}")
    venv_python = venv_python_path(cache)
    if os.path.isdir(venv_python) or not os.path.isfile(venv_python):
        fail_guard(
            f"isolated lidar venv python missing at {venv_python} (probe TOOLS_DIR_REL layout); "
            "create it only through the probe's reviewed plan-tools recipe; never from here")
    if not os.access(venv_python, os.X_OK):
        fail_guard(f"isolated lidar venv python at {venv_python} is not executable")
    log(f"tooling ok: worker script {resolution['path']} (sha256 {resolution['sha256']}, "
        f"{resolution['source']}, approved); base measurement worker {base_worker} "
        f"(sha256 {base_sha}, math base); selection module {select_path} "
        f"(sha256 {select_sha}, selection/freeze); venv python {venv_python}")
    return {"worker_script": resolution["path"], "worker_sha256": resolution["sha256"],
            "worker_source": resolution["source"], "select_path": select_path,
            "select_sha256": select_sha, "base_worker_path": base_worker,
            "base_worker_sha256": base_sha, "venv_python": venv_python}


# ---------------------------------------------------------------------------
# Tile verification (read-only): regular non-symlink file, pinned size, manifest
# sha256, observed header footprint. Runs BEFORE any worker or point reader.
# ---------------------------------------------------------------------------


def verify_tiles(cache, manifest_entries):
    """Full six-tile verification: presence, regular file, pinned size, manifest
    sha256 recomputation, and an observed header footprint (manifest sidecar, or
    B's probe report for the first tile). Returns {name: record} in pinned order."""
    verified = {}
    report = None
    report_problem = None
    for pin in TILE_PINS:
        name = pin["name"]
        entry = manifest_entries.get(name)
        if entry is None:
            fail_state(f"integrity manifest has no entry for {name}: unverified input "
                       f"(exit 4 before any worker runs)")
        if entry["size"] != pin["size"]:
            fail_state(f"integrity manifest size for {name} is {entry['size']}, "
                       f"pinned {pin['size']}")
        rel = resolve_tile_rel(pin, entry)
        path = resolve_under_cache(cache, rel, f"tile {name}")
        if os.path.islink(path):
            fail_state(f"tile {name} is a symlink at {path}; refusing")
        if not os.path.isfile(path):
            fail_state(f"tile {name} missing at {path} (exit 4 before any worker runs)")
        info = os.lstat(path)
        if not stat.S_ISREG(info.st_mode):
            fail_state(f"tile {name} at {path} is not a regular file")
        if info.st_size != pin["size"]:
            fail_state(f"tile {name} size drift: {info.st_size} on disk vs pinned {pin['size']}")
        digest = sha256_file(path)
        if digest != entry["sha256"]:
            fail_state(f"tile {name} sha256 mismatch: file {digest} vs manifest {entry['sha256']}")
        footprint = None
        if entry["footprint_l93"] is not None:
            footprint, problem = validate_footprint(
                entry["footprint_l93"], f"manifest footprint for {name}")
            if problem is not None:
                fail_state(problem)
        else:
            if report is None and report_problem is None:
                report, report_problem = load_probe_report(probe_report_path(cache))
            if name == FIRST_TILE_NAME:
                if report is None:
                    fail_state(f"no footprint for {name}: manifest sidecar omits it and the "
                               f"probe report is unavailable ({report_problem})")
                footprint, problem = probe_first_tile_footprint(report, probe_report_path(cache))
                if problem is not None:
                    fail_state(problem)
            else:
                fail_state(f"no observed header footprint for {name}: the manifest entry must "
                           f"carry footprint_l93 (x_min/y_min/x_max/y_max)")
        verified[name] = {
            "name": name,
            "rel": rel,
            "path": path,
            "size": pin["size"],
            "sha256": digest,
            "footprint_l93": footprint,
        }
        log(f"tile verified: {name} at {path} ({pin['size']} bytes, sha256 {digest}, "
            f"footprint {footprint})")
    return verified


# ---------------------------------------------------------------------------
# Worker request construction and output verification (final mode: the request
# carries the full canonical lattice for FULL enumeration; containment is decided
# by the worker from the OBSERVED header footprint, never from this request).
# ---------------------------------------------------------------------------


def build_request(tile, geometry, tooling, request_path, output_path):
    cols, rows = groundtruth.full_extent_cell_counts()
    anchor = geometry["lattice_anchor_l93"]
    request = {
        "request_version": 1,
        "tile_path": tile["path"],
        "aoi_bbox_l93": dict(geometry["aoi_bbox_l93"]),
        "filter_bbox_l93": dict(geometry["filter_bbox_l93"]),
        "cell_size_m": COVERAGE_CELL_M,
        "chunk_size_points": WORKER_CHUNK_POINTS,
        "memory_budget_bytes": WORKER_MEMORY_BUDGET_BYTES,
        "candidates": {
            "lattice_origin_l93": [anchor[0], anchor[1]],
            "aoi_sw_l93": [anchor[0], anchor[1]],
            "lattice_cols": int(cols),
            "lattice_rows": int(rows),
            "spacing_m": groundtruth.LATTICE,
            "window_r": groundtruth.WINDOW_R,
            "e23_radius": groundtruth.E23_DIST,
            "footprint_l93": dict(tile["footprint_l93"]),
        },
        "venv_python": tooling["venv_python"],
        "worker_script": tooling["worker_script"],
        "worker_script_sha256": tooling["worker_sha256"],
        "base_worker_path": tooling["base_worker_path"],
        "base_worker_sha256": tooling["base_worker_sha256"],
        "select_module_sha256": tooling["select_sha256"],
        "request_path": request_path,
        "output_path": output_path,
    }
    return request


def verify_measurements(request, measurements, tile):
    """(ok, problem): the worker output must match the request's phase, source and
    frozen protocol fields before any record is converted."""
    if not isinstance(measurements, dict):
        return False, "worker output is not a JSON object"
    if measurements.get("measurement_version") != 1:
        return False, f"worker output measurement_version {measurements.get('measurement_version')!r}"
    if measurements.get("tile_path") != request["tile_path"]:
        return False, f"worker output tile_path {measurements.get('tile_path')!r} is not the request's"
    if measurements.get("tile_bytes") != tile["size"]:
        return False, (f"worker output tile_bytes {measurements.get('tile_bytes')!r} is not the "
                       f"pinned {tile['size']}")
    header = measurements.get("header")
    if not isinstance(header, dict):
        return False, "worker output carries no header object"
    _edges, problem = validate_footprint(header.get("footprint_l93"), "worker header footprint")
    if problem is not None:
        return False, problem
    block = measurements.get("candidates")
    if not isinstance(block, dict):
        return False, "worker output carries no candidates block (full enumeration requested)"
    anchor = request["candidates"]["lattice_origin_l93"]
    origin = block.get("lattice_origin_l93")
    if not isinstance(origin, list) or len(origin) != 2 \
            or max(abs(float(origin[i]) - float(anchor[i])) for i in (0, 1)) > 1e-9:
        return False, f"worker lattice origin {origin!r} disagrees with the request anchor {anchor!r}"
    for key, frozen in (("spacing_m", groundtruth.LATTICE), ("window_r", groundtruth.WINDOW_R),
                        ("e23_radius", groundtruth.E23_DIST)):
        value = block.get(key)
        if not _finite_number(value) or float(value) != frozen:
            return False, f"worker candidates {key} {value!r} is not the frozen {frozen}"
    for key in ("lattice_cols", "lattice_rows"):
        expected = request["candidates"][key]
        if block.get(key) != expected:
            return False, f"worker candidates {key} {block.get(key)!r} is not the request's {expected}"
    if block.get("cells_enumerated") != expected_cells(request):
        return False, (f"worker cells_enumerated {block.get('cells_enumerated')!r} is not "
                       f"{expected_cells(request)}")
    if block.get("footprint_request_l93") != request["candidates"]["footprint_l93"]:
        return False, "worker footprint_request_l93 does not echo the request footprint"
    _edges, problem = validate_footprint(
        block.get("footprint_observed_l93"), "worker footprint_observed_l93")
    if problem is not None:
        return False, problem
    columns = block.get("columns")
    arrays = block.get("arrays")
    if not isinstance(columns, list) or not columns or not isinstance(arrays, dict):
        return False, "worker candidates block carries no parallel arrays"
    length = None
    for key in columns:
        column = arrays.get(key)
        if not isinstance(column, list):
            return False, f"worker candidates array {key} missing or not a list"
        if length is None:
            length = len(column)
        elif len(column) != length:
            return False, f"worker candidates array {key} length disagrees ({len(column)} != {length})"
    if block.get("row_order") != "row-major (row ascending, then col ascending)":
        return False, f"worker row_order {block.get('row_order')!r} is not the frozen row-major order"
    return True, None


def expected_cells(request):
    return request["candidates"]["lattice_cols"] * request["candidates"]["lattice_rows"]


# Output provenance fields (additive, top-level): emitted by the banded
# entrypoint; optional-but-verified for the base worker's legacy outputs.
PROVENANCE_SHA_KEYS = (
    "executed_entrypoint_sha256",
    "base_measure_module_sha256",
    "select_module_sha256",
)


def verify_output_provenance(request, measurements):
    """Validated base measurement sha256 for one worker output. Additive
    provenance keys are REQUIRED whenever the spawned entrypoint is not the base
    worker (for example the banded entrypoint) and OPTIONAL-but-verified for the
    base worker's legacy outputs: a present key must be a sha256 string equal to
    its pinned value; missing-required, non-string, or contradictory values are
    state failures (exit 4) naming the field with expected vs actual."""
    executed_expected = request["worker_script_sha256"]
    required = executed_expected != EXPECTED_BASE_WORKER_SHA256
    expected = {
        "executed_entrypoint_sha256": executed_expected,
        "base_measure_module_sha256": EXPECTED_BASE_WORKER_SHA256,
        "select_module_sha256": SELECT_MODULE_SHA256,
    }
    for key in PROVENANCE_SHA_KEYS:
        actual = measurements.get(key)
        if actual is None:
            if required:
                fail_state(f"worker output provenance missing required key {key} for "
                           f"entrypoint {executed_expected!r}")
            continue
        if isinstance(actual, bool) or not isinstance(actual, str):
            fail_state(f"worker output provenance key {key} is not a sha256 string: "
                       f"{actual!r}")
        if actual != expected[key]:
            fail_state(f"worker output provenance key {key} contradicts the pinned value: "
                       f"expected {expected[key]}, actual {actual}")
    return expected["base_measure_module_sha256"]


# ---------------------------------------------------------------------------
# Candidate record conversion (controller-owned rules; mirrors the executed
# stage-1 conversion semantics: canonical cell filter, E6 from the recorded AOI
# bbox, sector indexing on the full-AOI 3 x 3 grid, frozen fired-rule evaluation,
# non-finite defense FIT_NONFINITE). One addition, fail-closed in the same C1
# spirit: a rank-deficient plane fit (worker fit_unavailable, sigma/slope absent)
# appends FIT_UNAVAILABLE and is NEVER eligible, because the unchanged select()
# ranks by sigma_fit and could not order such a record.
# ---------------------------------------------------------------------------

_CANDIDATE_VALUE_KEYS = (
    "n2", "a", "b", "c", "sigma_fit", "slope_deg", "curv_bound",
    "mix2", "mix3", "mix4", "mix5", "mix6", "mix9", "mix17",
    "e2_dist", "e3_dist",
)
_NONFINITE_EXCLUSION = "FIT_NONFINITE"
_FIT_UNAVAILABLE_EXCLUSION = "FIT_UNAVAILABLE"


def _candidate_value_defective(value):
    if value is None:
        return False
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return True
    return not math.isfinite(value)


def finalize_record(row, col, values, anchor_x, anchor_y, spacing, aoi_bbox_l93):
    """One canonical candidate record from worker cell values (pure)."""
    abs_x = anchor_x + (col + 0.5) * spacing
    abs_y = anchor_y + (row + 0.5) * spacing
    utm_x, utm_y = groundtruth.lattice_centre(row, col)
    e6 = groundtruth.seam_border_distance(abs_x, abs_y, aoi_bbox_l93)
    nonfinite = [key for key in _CANDIDATE_VALUE_KEYS if _candidate_value_defective(values[key])]
    clean = dict(values)
    for key in nonfinite:
        clean[key] = None
    n2 = 0 if clean["n2"] is None else int(clean["n2"])
    sigma = clean["sigma_fit"]
    slope = clean["slope_deg"]
    curv = clean["curv_bound"]
    truth_z = clean["a"]
    d9 = clean["e2_dist"]
    d17 = clean["e3_dist"]
    fit_unavailable = bool(values.get("fit_unavailable"))
    fired = groundtruth.fired_rules(n2, d9, d17, sigma, slope, e6, curv)
    if nonfinite:
        fired.append((_NONFINITE_EXCLUSION, ",".join(nonfinite)))
    if fit_unavailable:
        fired.append((_FIT_UNAVAILABLE_EXCLUSION, "plane fit rank-deficient"))
    sr, sc = groundtruth.sector_of(utm_x, utm_y)
    centroid_x, centroid_y = groundtruth.sector_centroid(sr, sc)
    stratum, pool = groundtruth.classify_stratum(slope, sigma)
    record = {
        "id": "r%dc%d" % (row, col),
        "row": row,
        "col": col,
        "utm_x": utm_x,
        "utm_y": utm_y,
        "l93_x": abs_x,
        "l93_y": abs_y,
        "n2": n2,
        "density": n2 / (math.pi * groundtruth.WINDOW_R * groundtruth.WINDOW_R),
        "truth_z": truth_z,
        "sigma_fit": sigma,
        "slope_deg": slope,
        "curv_bound": curv,
        "n3": 0 if clean["mix3"] is None else int(clean["mix3"]),
        "n4": 0 if clean["mix4"] is None else int(clean["mix4"]),
        "n5": 0 if clean["mix5"] is None else int(clean["mix5"]),
        "n6": 0 if clean["mix6"] is None else int(clean["mix6"]),
        "n9": 0 if clean["mix9"] is None else int(clean["mix9"]),
        "n17": 0 if clean["mix17"] is None else int(clean["mix17"]),
        "d9": d9,
        "d17": d17,
        "e6_dist": e6,
        "curv_unavailable": bool(values.get("curv_unavailable")),
        "fit_unavailable": fit_unavailable,
        "sector": groundtruth.sector_name(sr, sc),
        "sector_dist": math.hypot(utm_x - centroid_x, utm_y - centroid_y),
        "stratum": stratum,
        "pool": pool,
        "eligible": not fired,
        "fired": fired,
        "rank": None,
    }
    if nonfinite:
        record["nonfinite_fields"] = nonfinite
    return record


def records_from_candidates_block(block, anchor, aoi_bbox_l93, canonical_cells):
    """(records, meta, problem): map one tile's worker parallel arrays onto
    canonical candidate records. Only cells of the canonical one-cell-margin
    enumeration are kept; the worker's extra full-extent margin ring is counted
    and ignored. Containment is re-checked against the OBSERVED header footprint."""
    if not isinstance(block, dict):
        return None, {}, "candidates block is not a JSON object"
    arrays = block.get("arrays")
    columns = block.get("columns")
    if not isinstance(arrays, dict) or not isinstance(columns, list) or not columns:
        return None, {}, "candidates block carries no parallel arrays"
    length = None
    for key in columns:
        column = arrays.get(key)
        if not isinstance(column, list):
            return None, {}, f"candidates array {key} missing or not a list"
        if length is None:
            length = len(column)
        elif len(column) != length:
            return None, {}, f"candidates array {key} length disagrees ({len(column)} != {length})"
    origin = block.get("lattice_origin_l93")
    if not isinstance(origin, list) or len(origin) != 2 \
            or max(abs(float(origin[i]) - float(anchor[i])) for i in (0, 1)) > 1e-9:
        return None, {}, "candidates block lattice origin disagrees with the frozen anchor"
    spacing = float(block["spacing_m"])
    anchor_x = float(origin[0])
    anchor_y = float(origin[1])
    footprint, problem = validate_footprint(
        block.get("footprint_observed_l93"), "candidates block footprint_observed_l93")
    if problem is not None:
        return None, {}, problem
    records = []
    ignored_ring = 0
    containment_failures = 0
    for index in range(length):
        col = int(arrays["col"][index])
        row = int(arrays["row"][index])
        abs_x = anchor_x + (col + 0.5) * spacing
        abs_y = anchor_y + (row + 0.5) * spacing
        if not groundtruth.window_inside_footprint(abs_x, abs_y, groundtruth.WINDOW_R, footprint):
            containment_failures += 1
            continue
        if (row, col) not in canonical_cells:
            ignored_ring += 1
            continue
        values = {key: arrays[key][index] if key in arrays else None
                  for key in _CANDIDATE_VALUE_KEYS}
        values["curv_unavailable"] = arrays.get("curv_unavailable", [None] * length)[index]
        values["fit_unavailable"] = arrays.get("fit_unavailable", [None] * length)[index]
        records.append(finalize_record(row, col, values, anchor_x, anchor_y, spacing, aoi_bbox_l93))
    meta = {
        "cells_contained": int(block.get("cells_contained", length)),
        "cells_enumerated": int(block.get("cells_enumerated", 0)),
        "canonical_candidates": len(records),
        "ignored_margin_ring_cells": ignored_ring,
        "containment_failures_rereported": containment_failures,
        "footprint_observed_l93": dict(footprint),
    }
    return records, meta, None


# ---------------------------------------------------------------------------
# Bounded canonical merge (one tile's records in memory at a time).
# ---------------------------------------------------------------------------


def _record_bytes(record):
    return json.dumps(record, sort_keys=True, ensure_ascii=True, allow_nan=False)


def merge_tile_records(catalog, sources, tile_name, records):
    """Merge one tile's records into the global catalog keyed by cell id.
    A duplicate id with ANY eligible copy is a structural STOP naming the id and
    both tiles; a duplicate with only ineligible copies merges only when the
    records are field-identical (one copy kept), else STOP. Single-copy records
    pass through. Outside-footprint cells are absent by construction and holes
    (ineligible records) are retained like any other record."""
    for record in records:
        cid = record["id"]
        existing = catalog.get(cid)
        if existing is None:
            catalog[cid] = record
            sources[cid] = [tile_name]
            continue
        if record["eligible"] or existing["eligible"]:
            fail_guard(
                f"structural merge STOP: cell {cid} is eligible in more than one tile "
                f"({sources[cid][0]} and {tile_name}); overlapping-tile double counting of an "
                f"eligible ground-truth window is refused")
        try:
            differing = next(
                (key for key in sorted(set(existing) | set(record))
                 if _record_bytes({key: existing.get(key)})
                 != _record_bytes({key: record.get(key)})),
                None)
        except ValueError as error:
            fail_guard(
                f"structural merge STOP: ineligible duplicate cell {cid} between "
                f"{sources[cid][0]} and {tile_name} is not finitely comparable: {error}")
        if differing is not None:
            fail_guard(
                f"structural merge STOP: ineligible duplicate cell {cid} differs between "
                f"{sources[cid][0]} and {tile_name} (first differing field {differing!r}); "
                f"field-identical duplicates only are merged")
        sources[cid].append(tile_name)


# ---------------------------------------------------------------------------
# Final selection + freeze wrappers around B's UNCHANGED API.
# ---------------------------------------------------------------------------


def select_final(catalog):
    records = list(catalog.values()) if isinstance(catalog, dict) else list(catalog)
    try:
        selection = groundtruth.select(records, stage_1_preliminary=False)
    except groundtruth.ProtocolStop as stop:
        details = {key: value for key, value in stop.details.items()}
        fail_guard(f"final selection STOP: {stop.reason} {details}")
    if selection.stage_1_preliminary:
        fail_state("select() returned a stage-1 preliminary selection; the final freeze "
                   "refuses it (unreachable through this path)")
    return selection


def freeze_final(catalog, selection, out_dir, note):
    records = list(catalog.values()) if isinstance(catalog, dict) else list(catalog)
    try:
        manifest = groundtruth.freeze(records, selection, out_dir, note=note)
    except groundtruth.ProtocolStop as stop:
        fail_guard(f"final freeze refused: {stop.reason} {stop.details}")
    return manifest


# ---------------------------------------------------------------------------
# Bounded worker runner: selector-based, all-events batch consumption, EOF
# unregister, kill+reap on timeout/oversize/read-error; stdout, stderr and the
# worker's output file each hard-capped at 64 MiB with the cap checked during
# capture; wall time and child RSS captured via getrusage(RUSAGE_CHILDREN) deltas.
# ---------------------------------------------------------------------------


class WorkerStreams:
    """Bounded stdout/stderr reader around one worker child."""

    def __init__(self, proc, cap):
        self.proc = proc
        self.cap = cap
        self.selector = selectors.DefaultSelector()
        self.selector.register(proc.stdout, selectors.EVENT_READ, "out")
        self.selector.register(proc.stderr, selectors.EVENT_READ, "err")
        self.out = bytearray()
        self.err = bytearray()
        self.out_overflow = False
        self.err_overflow = False
        self.read_error = None

    def read_batch(self, timeout_seconds):
        """Every ready (kind, data) event of one select batch, in order. An EOF
        unregisters its fd: a closed pipe is level-triggered ready forever, so
        leaving it registered would starve the other stream's events."""
        batch = []
        for key, _mask in self.selector.select(timeout_seconds):
            try:
                chunk = os.read(key.fileobj.fileno(), CHUNK_SIZE)
            except OSError as error:
                self.read_error = str(error)
                self.selector.unregister(key.fileobj)
                batch.append((key.data + "-eof", b""))
                continue
            if chunk:
                target = self.out if key.data == "out" else self.err
                flag = "out" if key.data == "out" else "err"
                room = max(self.cap - len(target), 0)
                target.extend(chunk[:room])
                if len(chunk) > room:
                    if flag == "out":
                        self.out_overflow = True
                    else:
                        self.err_overflow = True
                batch.append((key.data, chunk))
            else:
                self.selector.unregister(key.fileobj)
                batch.append((key.data + "-eof", b""))
        return batch

    def kill(self):
        try:
            self.proc.kill()
        except OSError:
            pass

    def wait(self, timeout_seconds=None):
        try:
            return self.proc.wait(timeout_seconds)
        except subprocess.TimeoutExpired:
            self.proc.kill()
            return self.proc.wait()

    def close(self):
        self.selector.close()
        for stream in (self.proc.stdout, self.proc.stderr):
            try:
                stream.close()
            except OSError:
                pass


class WorkerRun:
    def __init__(self, pid, returncode, stdout, stderr, problem, wall_seconds,
                 rss_before_kb, rss_after_kb, output_bytes):
        self.pid = pid
        self.returncode = returncode
        self.stdout = stdout
        self.stderr = stderr
        self.problem = problem
        self.wall_seconds = wall_seconds
        self.rss_before_kb = rss_before_kb
        self.rss_after_kb = rss_after_kb
        self.output_bytes = output_bytes


def spawn_process(argv):
    """Process seam: one worker child; tests replace this function."""
    return subprocess.Popen(
        argv, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE)


def worker_command(venv_python, worker_script, request_path):
    """Command seam: [venv_python, worker_script, REQUEST_JSON] per the worker
    contract; tests patch this to exercise the orchestrator with a stand-in."""
    return [venv_python, worker_script, request_path]


def output_size_within_cap(path, cap):
    try:
        size = os.path.getsize(path)
    except OSError:
        return True, None
    return size <= cap, size


def run_worker_bounded(argv, output_path, deadline_seconds, cap):
    """One bounded worker attempt (never retried by the caller). Any timeout,
    stream oversize, output-file oversize or read error kills and reaps the
    child before returning."""
    rss_before_kb = resource.getrusage(resource.RUSAGE_CHILDREN).ru_maxrss
    started = time.monotonic()
    try:
        proc = spawn_process(argv)
    except OSError as error:
        fail_guard(f"worker failed to start ({argv[0]}, {argv[1]}): {error}")
    streams = WorkerStreams(proc, cap)
    problem = None
    out_eof = False
    err_eof = False
    output_bytes = None
    deadline = started + deadline_seconds
    while problem is None and not (out_eof and err_eof):
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            problem = "timeout"
            break
        try:
            batch = streams.read_batch(remaining)
        except OSError as error:
            problem = f"read-error: {error}"
            break
        for kind, _data in batch:
            if kind == "out-eof":
                out_eof = True
            elif kind == "err-eof":
                err_eof = True
        if problem is None and streams.read_error is not None:
            problem = f"read-error: {streams.read_error}"
        if problem is None and streams.out_overflow:
            problem = f"stdout oversize beyond the {cap}-byte cap"
        if problem is None and streams.err_overflow:
            problem = f"stderr oversize beyond the {cap}-byte cap"
        if problem is not None:
            break
        within, size = output_size_within_cap(output_path, cap)
        if not within:
            output_bytes = size
            problem = f"worker output file beyond the {cap}-byte cap ({size} bytes)"
            break
        if time.monotonic() > deadline:
            problem = "timeout"
            break
    if problem is not None:
        streams.kill()
    elif deadline - time.monotonic() <= 0:
        problem = "timeout"
        streams.kill()
    code = streams.wait(max(deadline - time.monotonic(), 0))
    streams.close()
    rss_after_kb = resource.getrusage(resource.RUSAGE_CHILDREN).ru_maxrss
    _within, output_bytes = output_size_within_cap(output_path, cap)
    return WorkerRun(
        pid=proc.pid,
        returncode=code,
        stdout=bytes(streams.out),
        stderr=bytes(streams.err),
        problem=problem,
        wall_seconds=time.monotonic() - started,
        rss_before_kb=rss_before_kb,
        rss_after_kb=rss_after_kb,
        output_bytes=output_bytes,
    )


# ---------------------------------------------------------------------------
# Scratch, state, lock (O_EXCL; live pid refuses; stale suggests --recover-lock).
# ---------------------------------------------------------------------------


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
        fail_state(f"unparseable lock at {lock_path}; after confirming no freeze is running, "
                   f"resolve it manually")
    return int(fields[0]), fields[1]


def acquire_lock(lock_path):
    if os.path.exists(lock_path):
        pid, recorded = read_lock(lock_path)
        if pid_alive(pid):
            fail_state(f"freeze lock at {lock_path} is held by live pid {pid} (recorded {recorded})")
        fail_state(f"stale freeze lock at {lock_path} (dead pid {pid}, recorded {recorded}); "
                   f"rerun with --recover-lock")
    descriptor = os.open(lock_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o644)
    with os.fdopen(descriptor, "w", encoding="utf-8") as handle:
        handle.write(f"{os.getpid()} {now_iso()}\n")
    log(f"lock acquired: {lock_path} (pid {os.getpid()})")


def release_lock(lock_path):
    if os.path.exists(lock_path):
        os.unlink(lock_path)
        log(f"lock released: {lock_path}")


def recover_lock(lock_path):
    if not os.path.exists(lock_path):
        log(f"no lock present at {lock_path}; nothing to recover")
        return
    pid, recorded = read_lock(lock_path)
    if pid_alive(pid):
        fail_state(f"lock at {lock_path} is held by live pid {pid} (recorded {recorded}); "
                   f"refusing to remove")
    os.unlink(lock_path)
    log(f"stale lock removed: {lock_path} (dead pid {pid}, recorded {recorded})")


def find_completed_freeze(cache):
    """(stage, problem): the single completed freeze stage, or None."""
    root = freeze_root(cache)
    if not os.path.isdir(root) or os.path.islink(root):
        return None, None
    completed = []
    with os.scandir(root) as entries:
        for entry in entries:
            if not entry.name.startswith(STAGE_PREFIX) or not entry.is_dir(follow_symlinks=False):
                continue
            if os.path.exists(os.path.join(entry.path, COMPLETED_NAME)):
                completed.append(entry.path)
    if len(completed) > 1:
        return None, (f"multiple completed freezes under {root}; resolve which one stands "
                      f"manually: {sorted(completed)}")
    return (completed[0] if completed else None), None


def find_incomplete_freeze(cache):
    """(stage, problem): the single incomplete freeze stage with a STARTED marker."""
    root = freeze_root(cache)
    if not os.path.isdir(root) or os.path.islink(root):
        return None, None
    incomplete = []
    with os.scandir(root) as entries:
        for entry in entries:
            if not entry.name.startswith(STAGE_PREFIX) or not entry.is_dir(follow_symlinks=False):
                continue
            if os.path.exists(os.path.join(entry.path, COMPLETED_NAME)):
                continue
            if not os.path.exists(os.path.join(entry.path, STARTED_NAME)):
                log(f"stage directory without a STARTED marker left untouched: {entry.path}")
                continue
            incomplete.append(entry.path)
    if len(incomplete) > 1:
        return None, (f"multiple incomplete freezes under {root}; resolve them manually: "
                      f"{sorted(incomplete)}")
    return (incomplete[0] if incomplete else None), None


# ---------------------------------------------------------------------------
# Idempotent reverification of a completed freeze (read-only; no workers).
# ---------------------------------------------------------------------------


def reverify_completed(cache, stage):
    state_path = os.path.join(stage, STATE_FILE_REL)
    try:
        with open(state_path, "r", encoding="utf-8") as handle:
            state = json.load(handle)
    except (OSError, ValueError) as error:
        fail_state(f"completed freeze state at {state_path} cannot be parsed: {error}")
    if not isinstance(state, dict) or state.get("version") != STATE_VERSION \
            or state.get("mode") != "freeze":
        fail_state(f"completed freeze state at {state_path} has an unsupported shape")
    tiles = state.get("tiles")
    artifacts = state.get("artifacts")
    recorded_digest = state.get("canonical_digest")
    if not isinstance(tiles, dict) or not isinstance(artifacts, dict) \
            or not isinstance(recorded_digest, str):
        fail_state(f"completed freeze state at {state_path} carries no tiles/artifacts/digest")
    entries, _worker_cfg, _present, manifest_problem = read_integrity_manifest(
        integrity_manifest_path(cache))
    if manifest_problem is not None:
        fail_state(manifest_problem)
    for pin in TILE_PINS:
        record = tiles.get(pin["name"])
        if not isinstance(record, dict):
            fail_state(f"completed freeze state has no record for {pin['name']}")
        # Resolves tile paths via the manifest rel_path, as the initial freeze does (stage-2 layout).
        entry = entries.get(pin["name"])
        rel = resolve_tile_rel(pin, entry) if entry is not None else pin["rel"]
        path = resolve_under_cache(cache, rel, f"tile {pin['name']}")
        if os.path.islink(path) or not os.path.isfile(path):
            fail_state(f"completed freeze reverification: tile missing at {path}")
        if os.path.getsize(path) != pin["size"]:
            fail_state(f"completed freeze reverification: tile {pin['name']} size drifted")
        digest = sha256_file(path)
        if digest != record.get("sha256"):
            fail_state(f"completed freeze reverification: tile {pin['name']} sha256 drifted")
    for label in ("candidates_tsv", "selection_tsv", "frozen_manifest", "receipt"):
        artifact = artifacts.get(label)
        if not isinstance(artifact, dict) or not isinstance(artifact.get("path"), str) \
                or not isinstance(artifact.get("sha256"), str):
            fail_state(f"completed freeze state artifact {label} is malformed")
        path = artifact["path"]
        if os.path.islink(path) or not os.path.isfile(path):
            fail_state(f"completed freeze artifact {label} missing at {path}")
        if isinstance(artifact.get("size"), int) \
                and os.path.getsize(path) != artifact["size"]:
            fail_state(f"completed freeze artifact {label} size drifted "
                       f"({os.path.getsize(path)} vs recorded {artifact['size']})")
        digest = sha256_file(path)
        if digest != artifact["sha256"]:
            fail_state(f"completed freeze artifact {label} sha256 drifted ({digest} vs "
                       f"{artifact['sha256']})")
    try:
        with open(artifacts["frozen_manifest"]["path"], "r", encoding="utf-8") as handle:
            manifest = json.load(handle)
    except (OSError, ValueError) as error:
        fail_state(f"completed freeze manifest cannot be parsed: {error}")
    digest = groundtruth.canonical_digest(manifest)
    if digest != recorded_digest:
        fail_state(f"completed freeze canonical digest drifted ({digest} vs {recorded_digest})")
    worker_identity = state.get("worker")
    if not isinstance(worker_identity, dict):
        fail_state("completed freeze state carries no worker identity block")
    if worker_identity.get("base_worker_sha256") != EXPECTED_BASE_WORKER_SHA256:
        fail_state(f"completed freeze recorded base worker identity drifted: "
                   f"{worker_identity.get('base_worker_sha256')!r} vs pinned "
                   f"{EXPECTED_BASE_WORKER_SHA256}")
    if worker_identity.get("select_module_sha256") != SELECT_MODULE_SHA256:
        fail_state(f"completed freeze recorded select module identity drifted: "
                   f"{worker_identity.get('select_module_sha256')!r} vs pinned "
                   f"{SELECT_MODULE_SHA256}")
    if worker_identity.get("sha256") not in APPROVED_WORKER_SHA256S:
        fail_state(f"completed freeze recorded entrypoint {worker_identity.get('sha256')!r} "
                   f"is not in the approved registry")
    for label, key in (("candidates_tsv", "candidates_sha256"),
                       ("selection_tsv", "selection_sha256")):
        tables = manifest.get("tables", {})
        if tables.get(key) != artifacts[label]["sha256"]:
            fail_state(f"completed freeze manifest {key} disagrees with the artifact hash")
    log(f"completed freeze reverified: {stage} (tiles, artifacts, canonical digest all intact)")
    return {
        "stage": stage,
        "candidates_tsv": artifacts["candidates_tsv"]["path"],
        "selection_tsv": artifacts["selection_tsv"]["path"],
        "frozen_manifest": artifacts["frozen_manifest"]["path"],
        "canonical_digest": recorded_digest,
    }


# ---------------------------------------------------------------------------
# Preflight (default; read-only).
# ---------------------------------------------------------------------------


def run_preflight(cache):
    pins = validate_tile_table()
    log(f"cache root: {cache}")
    log(f"plan: final six-tile freeze (protocol v1.2 section 6.5); {len(pins)} pinned tiles, "
        f"{TOTAL_PINNED_BYTES} bytes total")
    for pin in pins:
        log(f"pinned tile: {pin['name']} ({pin['size']} bytes) at {pin['rel']}")
    log("input integrity manifest contract (produced by the future accepted acquire receipt; "
        "this tool never writes it):")
    log(f"  path: {integrity_manifest_path(cache)}")
    log("  format: one JSON object mapping each of the six pinned tile names to "
        '{"size": <pinned byte count>, "sha256": "<64 lowercase hex>", "footprint_l93": '
        '{"x_min", "y_min", "x_max", "y_max"} (observed LAS header extent)}')
    log('  each entry may carry an optional "rel_path": the tile\'s actual verified location '
        "under the cache root, relative, ending with the pinned tile name, no symlink "
        "components (first tile lidarhd/<LOT>/<name>; stage-2 tiles "
        "lidarhd/stage2/<LOT>/<name> per the acquire receipt); when absent the pinned "
        "lidarhd/<LOT>/<name> path is used")
    log('  the manifest may carry an optional top-level "worker" object {"rel_path", '
        '"sha256"}: a replacement worker script under this file\'s scripts/geo sibling base, '
        "ending .py, hashing to the manifest sha256 AND the approved registry "
        "(APPROVED_WORKER_SHA256S); when absent the pinned sibling "
        "sedan_lidar_probe_worker.py is used and must itself hash into the registry")
    check_mount_realness(cache)
    check_freeze_ancestor(freeze_root(cache), "freeze")
    entries, worker_cfg, present, problem = read_integrity_manifest(integrity_manifest_path(cache))
    if problem is not None:
        fail_state(problem)
    if not present:
        log(f"integrity manifest absent at {integrity_manifest_path(cache)}: every tile "
            f"currently lacks its entry (reported, not failed; transport belongs to the "
            f"acquire unit)")
    lacking = [pin["name"] for pin in pins if pin["name"] not in entries]
    for name in lacking:
        log(f"tile lacks an integrity manifest entry (pending input): {name}")
    if not present:
        script = worker_script_path()
        if os.path.islink(script) or not os.path.isfile(script):
            log(f"worker resolution deferred: integrity manifest absent and the pinned default "
                f"worker script is absent at {script} (--freeze would refuse)")
        else:
            resolution = resolve_worker_script(None)
            log(f"worker resolution: {resolution['path']} "
                f"(sha256 {resolution['sha256']}, approved; manifest override deferred to "
                f"--freeze)")
    else:
        resolution = resolve_worker_script(worker_cfg)
        log(f"worker resolution ({resolution['source']}): {resolution['path']} "
            f"(sha256 {resolution['sha256']}, approved)")
    report, report_problem = load_probe_report(probe_report_path(cache))
    geometry = None
    if report_problem is not None:
        log(f"probe state: {report_problem} (the executed cs2cs lattice record is required "
            f"by --freeze)")
    else:
        geometry, problem = probe_frozen_geometry(report, probe_report_path(cache))
        if problem is not None:
            fail_state(problem)
        log(f"probe state: lattice anchor l93 {geometry['lattice_anchor_l93']}, "
            f"aoi bbox {geometry['aoi_bbox_l93']}")
    for pin in pins:
        entry = entries.get(pin["name"])
        rel = resolve_tile_rel(pin, entry) if entry is not None else pin["rel"]
        path = resolve_under_cache(cache, rel, f"tile {pin['name']}")
        if os.path.islink(path):
            fail_state(f"tile {pin['name']} is a symlink at {path}; refusing")
        if not os.path.isfile(path):
            log(f"tile pending transport (not present, preflight continues): {pin['name']}")
            continue
        info = os.lstat(path)
        if not stat.S_ISREG(info.st_mode):
            fail_state(f"tile {pin['name']} at {path} is not a regular file")
        if info.st_size != pin["size"]:
            fail_state(f"tile {pin['name']} size drift: {info.st_size} on disk vs pinned "
                       f"{pin['size']}")
        if entry is not None:
            if entry["size"] != pin["size"]:
                fail_state(f"integrity manifest size for {pin['name']} is {entry['size']}, "
                           f"pinned {pin['size']}")
            digest = sha256_file(path)
            if digest != entry["sha256"]:
                fail_state(f"tile {pin['name']} sha256 mismatch: file {digest} vs manifest "
                           f"{entry['sha256']}")
            log(f"tile verified: {pin['name']} at {path} ({pin['size']} bytes, sha256 ok)")
        else:
            log(f"tile present but unverified (no manifest entry): {pin['name']} "
                f"({info.st_size} bytes on disk)")
        if entry is None or entry["footprint_l93"] is None:
            if pin is FIRST_TILE_PIN and report is not None:
                footprint, problem = probe_first_tile_footprint(report, probe_report_path(cache))
                if problem is not None:
                    fail_state(problem)
                log(f"first-tile footprint available from the probe report: {footprint}")
            elif pin is not FIRST_TILE_PIN:
                log(f"tile has no observed header footprint source yet (manifest sidecar "
                    f"required by --freeze): {pin['name']}")
    check_protocol_constants()
    check_operator_policy(OPERATOR_POLICY_PATH)
    venv_python = venv_python_path(cache)
    if os.path.isdir(venv_python) or not os.path.isfile(venv_python):
        log(f"venv python absent (probe TOOLS_DIR_REL layout): --freeze would refuse; nothing "
            f"is installed from here")
    else:
        log(f"venv python present: {venv_python}")
    ancestor = check_freeze_ancestor(freeze_root(cache), "freeze")
    required = SCRATCH_TOTAL_BYTES + RESERVE_BYTES
    usage = statvfs(ancestor)
    log(f"free space: free={usage.f_bavail * usage.f_frsize} required-by---freeze={required} "
        f"(own scratch bound {SCRATCH_TOTAL_BYTES} + reserve {RESERVE_BYTES})")
    log("preflight complete; nothing was written, no directory was created, no worker was "
        "spawned, no network was touched")


# ---------------------------------------------------------------------------
# The final freeze.
# ---------------------------------------------------------------------------


def provenance_note(verified, policy_digest):
    tiles = " ".join(f"{pin['name']}:{verified[pin['name']]['sha256'][:16]}"
                     for pin in TILE_PINS)
    return ("sedan_lidar_freeze.py final all-tiles freeze; protocol v1.2; six pinned tiles "
            f"(sha256 prefixes) {tiles}; operator policy sha256 {policy_digest}; no DEM access "
            "and no numeric residual computed here; not a proof PASS")


def write_receipt(stage, verified, artifact_hashes, digest, geometry, policy_digest, run_notes,
                  tooling):
    receipt = {
        "receipt_version": 1,
        "generated_at": now_iso(),
        "tool": "sedan_lidar_freeze.py",
        "protocol_version": groundtruth.PROTOCOL_VERSION,
        "freeze_dir": stage,
        "lattice_anchor_32631": geometry["lattice_anchor_32631"],
        "lattice_anchor_l93": geometry["lattice_anchor_l93"],
        "worker": {
            "path": tooling["worker_script"],
            "sha256": tooling["worker_sha256"],
            "source": tooling["worker_source"],
            "approved_registry": list(APPROVED_WORKER_SHA256S),
            "base_worker_path": tooling["base_worker_path"],
            "base_worker_sha256": tooling["base_worker_sha256"],
            "select_module_sha256": tooling["select_sha256"],
        },
        "operator_policy": {
            "path": OPERATOR_POLICY_PATH,
            "sha256": policy_digest,
        },
        "budget_a": {
            "limit_m": 1.0,
            "scope": "engineering-only acceptance, never a certified source accuracy",
            "metric": "max abs(payloadMeters - frozen LiDAR refMeters) <= 1.0 m at every frozen "
                      "PRIMARY across at least 3 sectors",
            "residuals": "preserved verbatim; no pruning; no failure-based alternate activation",
            "future_manifest_encoding": {
                "tolerances.verticalMeters": 1.0,
                "source.verticalAccuracyMeters": "absent (actual tile-specific RGE accuracy "
                                                 "unknown)",
            },
            "escalation": "a documented actual accuracy above 1.0 m escalates before any DEM "
                          "comparison; the operator limit is never silently raised and no "
                          "accuracy value is fabricated",
        },
        "budget_b": {
            "limit_game_units": 0.0390625,
            "metric": "|E - (P - C)| <= 0.0390625 game units, independent of Budget A, no 1.0 m "
                      "term added",
            "restore": "restoredEngineMeters = Z0 + (E + C) * metersPerGameUnit / "
                       "verticalExaggeration",
            "z0": "the ACTUAL header verticalOriginMeters, never an assumed 0; recorded when the "
                  "payload is staged (extent-minimum convention); pending at freeze time "
                  "(this tool never opens a DEM)",
        },
        "carried_unresolved": [
            "RGE ALTI tile-specific accuracy UNKNOWN",
            "2024 auto-classification bias",
            "2012-2016 versus 2024 epoch mismatch",
        ],
        "provenance_hash_chain": {
            "worker_sha256": tooling["worker_sha256"],
            "base_worker_sha256": tooling["base_worker_sha256"],
            "select_module_sha256": tooling["select_sha256"],
            "tiles": {name: record["sha256"] for name, record in verified.items()},
            "raw_outputs": {name: artifact_hashes["raw"][name] for name in verified},
            "candidates_sha256": artifact_hashes["candidates"],
            "selection_sha256": artifact_hashes["selection"],
            "canonical_digest": digest,
        },
        "numeric_residual_computed_here": False,
        "numeric_proof_pass": False,
        "provider_02_framework": False,
        "stage_1_preliminary_can_never_be_frozen": True,
        "worker_runs": run_notes,
    }
    path = os.path.join(stage, RECEIPT_NAME)
    descriptor = os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o644)
    with os.fdopen(descriptor, "w", encoding="utf-8") as handle:
        json.dump(receipt, handle, indent=2, sort_keys=True)
        handle.write("\n")
        handle.flush()
        os.fsync(handle.fileno())
    receipt_digest = sha256_file(path)
    log(f"policy sidecar written: {path} (sha256 {receipt_digest})")
    return path, receipt_digest


def run_freeze(cache):
    pins = validate_tile_table()
    log(f"cache root: {cache}")
    check_mount_realness(cache)
    check_freeze_ancestor(freeze_root(cache), "freeze")
    entries, worker_cfg, present, problem = read_integrity_manifest(integrity_manifest_path(cache))
    if problem is not None:
        fail_state(problem)
    if not present:
        fail_state(f"integrity manifest missing at {integrity_manifest_path(cache)}: no tile can "
                   f"be verified (exit 4 before any worker runs)")
    verified = verify_tiles(cache, entries)
    check_protocol_constants()
    check_operator_policy(OPERATOR_POLICY_PATH)
    tooling = check_tooling(cache, worker_cfg)
    report, report_problem = load_probe_report(probe_report_path(cache))
    if report_problem is not None:
        fail_state(report_problem)
    geometry, problem = probe_frozen_geometry(report, probe_report_path(cache))
    if problem is not None:
        fail_state(problem)
    log(f"lattice anchor l93 {geometry['lattice_anchor_l93']} (executed cs2cs record)")
    ancestor = check_freeze_ancestor(freeze_root(cache), "freeze")
    check_free_space(ancestor, SCRATCH_TOTAL_BYTES + RESERVE_BYTES)
    root = freeze_root(cache)
    if not os.path.isdir(root):
        os.mkdir(root)
    stage = os.path.join(root, STAGE_PREFIX + local_stamp())
    try:
        os.mkdir(stage)
    except FileExistsError:
        fail_state(f"freeze stage stamp collision at {stage}: stages are never adopted or "
                   f"overwritten")
    log(f"freeze stage: {stage}")
    descriptor = os.open(os.path.join(stage, STARTED_NAME), os.O_CREAT | os.O_EXCL | os.O_WRONLY,
                         0o644)
    os.close(descriptor)
    os.mkdir(os.path.join(stage, "state"))
    os.mkdir(os.path.join(stage, REQUESTS_DIR_REL))
    os.mkdir(os.path.join(stage, RAW_DIR_REL))
    os.mkdir(os.path.join(stage, DIAG_DIR_REL))
    state_path = os.path.join(stage, STATE_FILE_REL)
    lock_path = os.path.join(stage, LOCK_FILE_REL)
    state = {
        "version": STATE_VERSION,
        "mode": "freeze",
        "started": now_iso(),
        "pid": os.getpid(),
        "cache_root": cache,
        "lattice_anchor_32631": geometry["lattice_anchor_32631"],
        "lattice_anchor_l93": geometry["lattice_anchor_l93"],
        "aoi_bbox_l93": geometry["aoi_bbox_l93"],
        "filter_bbox_l93": geometry["filter_bbox_l93"],
        "tiles": {pin["name"]: {"status": "verified", "size": pin["size"],
                                "sha256": verified[pin["name"]]["sha256"]}
                  for pin in pins},
        "worker": {
            "path": tooling["worker_script"],
            "sha256": tooling["worker_sha256"],
            "source": tooling["worker_source"],
            "base_worker_path": tooling["base_worker_path"],
            "base_worker_sha256": tooling["base_worker_sha256"],
            "select_module_sha256": tooling["select_sha256"],
        },
        "worker_runs": {},
        "artifacts": {},
    }
    write_state(state_path, state)
    acquire_lock(lock_path)
    try:
        catalog, raw_hashes, run_notes = measure_all_tiles(
            stage, state, state_path, verified, geometry, tooling)
        selection = select_final(catalog)
        state["selection"] = {
            "primary_ids": [c["id"] for c in selection.primaries],
            "alternate_ids": [c["id"] for c in selection.alternates],
            "sectors_covered": list(selection.sectors_covered),
            "ladder": [[pool, count] for pool, count in selection.ladder],
            "alternates_shortfall": selection.alternates_shortfall,
        }
        write_state(state_path, state)
        final_dir = os.path.join(stage, FINAL_DIR_REL)
        manifest = freeze_final(
            catalog, selection, final_dir, provenance_note(verified, OPERATOR_POLICY_SHA256))
        manifest_path = os.path.join(final_dir, "frozen-manifest.json")
        digest = groundtruth.canonical_digest(manifest)
        artifact_hashes = {
            "raw": raw_hashes,
            "candidates": sha256_file(os.path.join(final_dir, "candidates.tsv")),
            "selection": sha256_file(os.path.join(final_dir, "selection.tsv")),
        }
        state["canonical_digest"] = digest
        # Source chain, one-way (state -> receipt): the receipt is constructed
        # FIRST, from operator inputs and scientific-artifact hashes only (the six
        # verified tile sha256s, the six raw worker-output sha256s, the
        # candidates.tsv/selection.tsv sha256s, the resolved worker and
        # select-module sha256s, and the operator policy memo sha256). The receipt
        # never contains its own hash or any run-state content. The run state is
        # written AFTER the receipt exists and RECORDS the receipt's size and
        # sha256, so the idempotent rerun can verify the receipt against the
        # state inventory.
        receipt_path, receipt_digest = write_receipt(
            stage, verified, artifact_hashes, digest, geometry,
            OPERATOR_POLICY_SHA256, run_notes, tooling)
        state["artifacts"] = {
            "candidates_tsv": {"path": os.path.join(final_dir, "candidates.tsv"),
                               "sha256": artifact_hashes["candidates"]},
            "selection_tsv": {"path": os.path.join(final_dir, "selection.tsv"),
                              "sha256": artifact_hashes["selection"]},
            "frozen_manifest": {"path": manifest_path,
                                "sha256": sha256_file(manifest_path)},
            "receipt": {"path": receipt_path,
                        "size": os.path.getsize(receipt_path),
                        "sha256": receipt_digest},
        }
        write_state(state_path, state)
        completed = os.path.join(stage, COMPLETED_NAME)
        descriptor = os.open(completed, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o644)
        os.close(descriptor)
        log(f"freeze completed: canonical digest {digest}")
        log(f"artifacts: {state['artifacts']['candidates_tsv']['path']}")
        log(f"artifacts: {state['artifacts']['selection_tsv']['path']}")
        log(f"artifacts: {manifest_path}")
        return {
            "stage": stage,
            "candidates_tsv": state["artifacts"]["candidates_tsv"]["path"],
            "selection_tsv": state["artifacts"]["selection_tsv"]["path"],
            "frozen_manifest": manifest_path,
            "canonical_digest": digest,
        }
    finally:
        release_lock(lock_path)


def measure_all_tiles(stage, state, state_path, verified, geometry, tooling):
    """Sequential worker phase: one tile at a time in the pinned order, one
    attempt each, raw outputs preserved on disk with tile provenance. The parent
    drops each tile's parsed records before the next worker starts."""
    requests_dir = os.path.join(stage, REQUESTS_DIR_REL)
    raw_dir = os.path.join(stage, RAW_DIR_REL)
    diag_dir = os.path.join(stage, DIAG_DIR_REL)
    raw_hashes = {}
    run_notes = {}
    for pin in TILE_PINS:
        tile = verified[pin["name"]]
        name = pin["name"]
        request_path = os.path.join(requests_dir, f"{name}.request.json")
        output_path = os.path.join(raw_dir, f"{name}.measurements.json")
        request = build_request(tile, geometry, tooling, request_path, output_path)
        with open(request_path, "w", encoding="utf-8") as handle:
            json.dump(request, handle, indent=2, sort_keys=True)
            handle.write("\n")
        log(f"tile {name}: worker attempt 1 of 1 (single attempt, no auto rerun)")
        argv = worker_command(tooling["venv_python"], tooling["worker_script"], request_path)
        try:
            run = run_worker_bounded(argv, output_path, WORKER_TIMEOUT_SECONDS, WORKER_OUTPUT_CAP)
        except KeyboardInterrupt:
            log(f"tile {name}: interrupted; child killed and reaped, scratch and lock preserved")
            raise Failure(EXIT_WORKER, "interrupted by operator; scratch and lock preserved for "
                                       "--recover-lock")
        note = {
            "attempt": 1,
            "pid": run.pid,
            "returncode": run.returncode,
            "problem": run.problem,
            "wall_seconds": round(run.wall_seconds, 3),
            "rss_children_maxrss_before_kb": run.rss_before_kb,
            "rss_children_maxrss_after_kb": run.rss_after_kb,
            "stdout_bytes": len(run.stdout),
            "stderr_bytes": len(run.stderr),
            "output_bytes": run.output_bytes,
        }
        state["worker_runs"][name] = note
        if run.stdout:
            with open(os.path.join(diag_dir, f"{name}.worker-stdout.txt"), "wb") as handle:
                handle.write(run.stdout)
        if run.stderr:
            with open(os.path.join(diag_dir, f"{name}.worker-stderr.txt"), "wb") as handle:
                handle.write(run.stderr)
        if run.problem is not None:
            write_state(state_path, state)
            fail_worker(f"tile {name}: worker attempt failed ({run.problem}); returncode "
                        f"{run.returncode}; diagnostics preserved under {diag_dir}; one attempt "
                        f"only, no auto rerun")
        if run.returncode != 0:
            write_state(state_path, state)
            if run.returncode == 3:
                fail_worker(
                    f"MEMORY-GUARD TRIP on tile {name}: worker exited its EXIT_MEMORY refusal "
                    f"(exit 3); header point count and planned peak are in the preserved "
                    f"stderr arithmetic table at "
                    f"{os.path.join(diag_dir, name + '.worker-stderr.txt')}; excerpt: "
                    f"{run.stderr.decode('utf-8', 'replace').strip()[:800]}; no budget raise, "
                    f"no fallback, no second pass, no method change")
            excerpt = run.stderr.decode("utf-8", "replace").strip()[:500]
            fail_worker(f"tile {name}: worker exited {run.returncode}; refusing to freeze on "
                        f"partial measurements; excerpt: {excerpt}")
        if not os.path.isfile(output_path):
            write_state(state_path, state)
            fail_worker(f"tile {name}: worker wrote no output at {output_path}")
        if os.path.getsize(output_path) > WORKER_OUTPUT_CAP:
            write_state(state_path, state)
            fail_worker(f"tile {name}: worker output beyond the {WORKER_OUTPUT_CAP}-byte cap")
        try:
            with open(output_path, "r", encoding="utf-8") as handle:
                measurements = json.load(handle)
        except (OSError, ValueError) as error:
            write_state(state_path, state)
            fail_worker(f"tile {name}: worker output at {output_path} is not readable JSON: "
                        f"{error}")
        ok, problem = verify_measurements(request, measurements, tile)
        if not ok:
            write_state(state_path, state)
            fail_worker(f"tile {name}: worker output does not match the request: {problem}")
        verified_base = verify_output_provenance(request, measurements)
        raw_hashes[name] = sha256_file(output_path)
        state["tiles"][name].update({
            "status": "measured",
            "raw_output": output_path,
            "raw_output_sha256": raw_hashes[name],
        })
        note["base_measure_module_sha256"] = verified_base
        state["worker"]["base_measure_module_sha256"] = verified_base
        state["worker_runs"][name] = note
        write_state(state_path, state)
        log(f"tile {name}: measured and preserved (raw sha256 {raw_hashes[name]}, "
            f"wall {note['wall_seconds']} s, child rss max {note['rss_children_maxrss_after_kb']} kB)")
        run_notes[name] = note
        del measurements
    state["raw_outputs_sha256"] = dict(raw_hashes)
    write_state(state_path, state)
    catalog = merge_all_tiles(state, raw_dir)
    state["catalog_cells"] = len(catalog)
    write_state(state_path, state)
    return catalog, raw_hashes, run_notes


def merge_all_tiles(state, raw_dir):
    """Bounded canonical merge: one tile's raw file is loaded and converted at a
    time; the parsed block is released before the next tile. Every per-tile raw
    file stays on disk for traceability (outside-footprint cells are absent by
    construction; holes and ineligible records are retained)."""
    catalog = {}
    sources = {}
    canonical_cells = set(groundtruth.enumerate_lattice_cells())
    for pin in TILE_PINS:
        name = pin["name"]
        path = os.path.join(raw_dir, f"{name}.measurements.json")
        with open(path, "r", encoding="utf-8") as handle:
            measurements = json.load(handle)
        block = measurements.get("candidates")
        records, _meta, problem = records_from_candidates_block(
            block, state["lattice_anchor_l93"], state["aoi_bbox_l93"], canonical_cells)
        if problem is not None:
            fail_guard(f"tile {name}: candidate conversion failed: {problem}")
        merged_count = len(records)
        merge_tile_records(catalog, sources, name, records)
        del records, measurements, block
        log(f"tile {name}: merged (canonical records {merged_count}, "
            f"catalog now {len(catalog)} cells)")
    return catalog


# ---------------------------------------------------------------------------
# CLI.
# ---------------------------------------------------------------------------


def resolve_cache_root(args):
    if args.root:
        return args.root, "positional"
    env_value = os.environ.get("PANOPTICON_GEO_PROOF_DIR")
    if env_value:
        return env_value, "env PANOPTICON_GEO_PROOF_DIR"
    return DEFAULT_CACHE_ROOT, "default"


def build_parser():
    parser = argparse.ArgumentParser(
        prog="sedan_lidar_freeze.py",
        description=(
            "Final six-tile LiDAR ground-truth freeze orchestrator (protocol v1.2 section 6.5): "
            "measures the six pinned tiles through the isolated worker subprocess, merges the "
            "candidate catalog, and runs the UNCHANGED select()+freeze() API. The final freeze "
            "runs BEFORE any DEM access; no DEM is ever opened, no numeric residual is computed, "
            "and no proof PASS is claimed. This tool never fetches: transport of the remaining "
            "five tiles belongs to the acquire unit."
        ),
        epilog=(
            f"cache root resolution order: 1) positional argument, 2) env "
            f"PANOPTICON_GEO_PROOF_DIR, 3) default {DEFAULT_CACHE_ROOT}. Modes: default = "
            f"read-only preflight (plan, six pinned tile identities, input integrity manifest "
            f"contract; never writes, never spawns workers); --freeze = the explicit final "
            f"operation (single attempt per worker, scratch preserved on any failure); "
            f"--recover-lock = remove a stale freeze lock whose recorded pid is no longer "
            f"alive. Exit codes: 0 success; 2 guard failure (floor STOP, structural merge STOP, "
            f"missing tooling, free space); 3 worker/process failure or interrupt (stage "
            f"preserved); 4 state, pin, integrity, collision, or drift failure."
        ),
    )
    parser.add_argument(
        "root", nargs="?", default=None,
        help=(f"cache root; resolution order: positional > env PANOPTICON_GEO_PROOF_DIR > "
              f"default {DEFAULT_CACHE_ROOT}"))
    parser.add_argument(
        "--freeze", action="store_true",
        help="the explicit final freeze operation; requires every guard and all six verified "
             "inputs to pass")
    parser.add_argument(
        "--recover-lock", action="store_true",
        help="remove a stale freeze stage lock only when its recorded pid is no longer alive")
    return parser


def main(argv=None):
    parser = build_parser()
    args = parser.parse_args(argv)
    modes = [name for name, given in (("freeze", args.freeze),
                                      ("recover-lock", args.recover_lock)) if given]
    if len(modes) > 1:
        parser.error(f"{' and '.join(modes)} cannot be combined")
    cache, source = resolve_cache_root(args)
    mode = modes[0] if modes else "preflight"
    code = EXIT_OK
    try:
        if args.recover_lock:
            log(f"cache root: {cache} (source: {source})")
            check_mount_realness(cache)
            check_freeze_ancestor(freeze_root(cache), "freeze")
            stage, problem = find_incomplete_freeze(cache)
            if problem is not None:
                fail_state(problem)
            if stage is None:
                log("no incomplete freeze stage found; nothing to recover")
            else:
                recover_lock(os.path.join(stage, LOCK_FILE_REL))
        elif args.freeze:
            stage, problem = find_completed_freeze(cache)
            if problem is not None:
                fail_state(problem)
            if stage is not None:
                result = reverify_completed(cache, stage)
                log(f"idempotent: existing freeze returned without rewriting anything and "
                    f"without spawning any worker: {result['stage']}")
            else:
                run_freeze(cache)
        else:
            run_preflight(cache)
    except Failure as failure:
        log(f"FAIL({failure.code}): {failure.message}")
        code = failure.code
    log(f"final status: exit={code} mode={mode} root={cache}")
    return code


if __name__ == "__main__":
    sys.exit(main())
