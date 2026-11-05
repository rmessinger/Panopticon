#!/usr/bin/env python3
"""Unattended Sedan geo-proof preparation orchestrator (stdlib only).

Runs the accepted helpers as subprocesses in fixed stage order; never reimplements their
logic. Default mode is a read-only preflight (no writes, no network); its only children are
bounded read-only queries of the tools venv (stage TOOLS). --prepare is the explicit
operation: a single attempt per stage, no automatic rerun beyond the bounded transport retry
for the archive stage. Completed stages are verified and SKIPPED; partial or invalid state
FAILS clearly and is never overwritten, adopted, or forged into COMPLETED.

Stages: 1 ARCHIVES, 2 EXTRACT, 3 TOOLS, 4 FIRST TILE, 5 REMAINING 5, 6 INTEGRITY+FREEZE,
7 B POST-PREPARE. The final freeze (stage 6) precedes any DEM height access. Stage 7 runs
sedan_prepare_inputs.py --prepare and verifies its receipt and manifest; it prints the follow-on
Interactive proof command and invokes no dotnet command. No proof PASS is claimed.

Root resolution: positional > env PANOPTICON_GEO_PROOF_DIR > default.
Exit codes: 0 success or preflight without stops; 2 guard, gate, or stop; 3 child process or
network failure; 4 state, integrity, or drift failure.

Every probe invocation passes --cache-root <root> before its subcommand. The WFS lineage record
<root>/lidarhd/state/lineage-0840-6958.json is written by sedan_prepare_bootstrap.fetch_lineage
when absent. Stage TOOLS delegates venv creation and validation to bootstrap_tools.
"""

import argparse
import hashlib
import importlib.util
import json
import os
import re
import selectors
import stat
import subprocess
import sys
import tempfile
import time
from datetime import datetime
from typing import NamedTuple

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ACQUIRE_SCRIPT = os.path.join(SCRIPT_DIR, "sedan_acquire.py")
EXTRACT_SCRIPT = os.path.join(SCRIPT_DIR, "sedan_extract.py")
PROBE_SCRIPT = os.path.join(SCRIPT_DIR, "sedan_lidar_probe.py")
STAGE2_SCRIPT = os.path.join(SCRIPT_DIR, "sedan_lidar_acquire.py")
INTEGRITY_SCRIPT = os.path.join(SCRIPT_DIR, "sedan_lidar_integrity.py")
FREEZE_SCRIPT = os.path.join(SCRIPT_DIR, "sedan_lidar_freeze.py")
BANDED_WORKER_PATH = os.path.join(SCRIPT_DIR, "sedan_lidar_probe_worker_banded.py")
PROBE_WORKER_PATH = os.path.join(SCRIPT_DIR, "sedan_lidar_probe_worker.py")
BOOTSTRAP_PATH = os.path.join(SCRIPT_DIR, "sedan_prepare_bootstrap.py")


def load_script(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


PROBE_MODULE = load_script("sedan_lidar_probe", PROBE_SCRIPT)
BOOTSTRAP_MODULE = load_script("sedan_prepare_bootstrap", BOOTSTRAP_PATH)

ROOT_ENV = "PANOPTICON_GEO_PROOF_DIR"
DEFAULT_ROOT = "/mnt/pit2/panopticon/geo-proof/sedan"
MOUNT_POINT = "/mnt/pit2"
PROC_MOUNTS_PATH = "/proc/mounts"

LOT = "NUALHD_1-0__LAZ_LAMB93_OD_2026-04-29"
FIRST_TILE_NAME = "LHD_FXX_0840_6958_PTS_LAMB93_IGN69.copc.laz"
FIRST_TILE_BYTES = 128_651_625
STAGE2_TILES = {
    "LHD_FXX_0839_6958_PTS_LAMB93_IGN69.copc.laz": 119_931_651,
    "LHD_FXX_0839_6959_PTS_LAMB93_IGN69.copc.laz": 146_099_989,
    "LHD_FXX_0840_6959_PTS_LAMB93_IGN69.copc.laz": 130_615_694,
    "LHD_FXX_0841_6958_PTS_LAMB93_IGN69.copc.laz": 153_352_444,
    "LHD_FXX_0841_6959_PTS_LAMB93_IGN69.copc.laz": 151_880_730,
}
ARCHIVE_PART_COUNT = 8
ARCHIVE_TOTAL_BYTES = 29_835_514_326
FREEZE_SCRATCH_BYTES = 8 * (64 << 20)
RESERVE_BYTES = 1 << 30

BANDED_WORKER_REL = "sedan_lidar_probe_worker_banded.py"
BANDED_WORKER_SHA256 = "52e935b5360c0d10563a581e650df3ac1637512eb30bc2cfc953d59cf12d2ab0"
INTEGRITY_MANIFEST_NAME = "lidar-integrity-manifest.json"
PRODUCER_MARKER_NAME = "PRODUCER-MARKER"
COMPLETED_NAME = "COMPLETED"
FREEZE_RECEIPT_NAME = "freeze-receipt.json"
EXTRACT_STAGE_PREFIX = "sedan-extract-"
FREEZE_STAGE_PREFIX = "sedan-freeze-"
LOCK_REL = os.path.join("state", "lock")
STATE_REL = os.path.join("state", "state.json")
STAGE2_STATE_REL = os.path.join("lidarhd", "stage2", "state", "state.json")
STAGE2_LOCK_REL = os.path.join("lidarhd", "stage2", "state", "lock")
PROBE_LOCK_REL = os.path.join("lidarhd", "state", "lock")
PROBE_REPORT_REL = os.path.join("lidarhd", "state", "probe-report-v1_2.json")
VENV_DIR_REL = os.path.join("tools", "lidar-venv")
VENV_PYTHON_REL = os.path.join(VENV_DIR_REL, "bin", "python")
VENV_LOCK_RECEIPT_NAME = "lidar-tools-lock.txt"
VENV_LOCK_RECEIPT_LOG = os.path.expanduser(
    "~/log/track-r/lidar-firsttile-runtime-plan-20261103T1315-0500/lidar-tools-lock.txt")
VENV_CONFIG_NAME = "pyvenv.cfg"
EXPECTED_PACKAGES = (("laspy", "2.7.0"), ("lazrs", "0.8.2"), ("numpy", "2.5.4"))
IDENTITY_CODE = "import sys; print(sys.prefix); print(sys.base_prefix)"
PACKAGE_CODE = ("import importlib.metadata as metadata, laspy, lazrs, numpy\n"
                + "".join(f"print({name!r}, metadata.version({name!r}))\n"
                          for name, _version in EXPECTED_PACKAGES))
VENV_QUERY_SECONDS = 60
VENV_QUERY_CAP_BYTES = 1 << 20
PART_KEY_RE = re.compile(r"([^/]+)/(([^/]+)\.7z\.\d{3})")
PROOF_REL = "sedan-ign-proof.json"
PROOF_KEYS = frozenset((
    "sources", "targetCoordinateSystem", "metersPerGameUnit", "defaultMaterial",
    "extentFeatureId", "verticalPolicy", "rasterPolicy", "tolerances", "controlPoints",
))
BACKEND_OUTPUT_LABELS = frozenset({"manifest", "texture_map", "sources_txt", "extent_kml",
                                   "dem_tif", "rgb_tif"})
SHA256_RE = re.compile(r"[0-9a-f]{64}")
OUTPUT_CHUNK_BYTES = 1 << 20
BACKEND_SCRIPT = os.path.join(SCRIPT_DIR, "sedan_prepare_inputs.py")
BACKEND_RECEIPT_REL = os.path.join("preparation", "receipt.json")
BACKEND_LEDGER_REL = os.path.join("preparation", "commands.jsonl")
BACKEND_PHASE_COMPLETE = "preparation_completed"
RECEIPT_READ_CAP = 64 << 10
LEDGER_READ_CAP = 16 << 20
FIRST_TILE_REL = os.path.join("lidarhd", LOT, FIRST_TILE_NAME)
LINEAGE_REL = os.path.join("lidarhd", "state", "lineage-0840-6958.json")
FREEZE_DIR_REL = "freeze"
ARCHIVE_EVIDENCE_REL = os.path.join(FREEZE_DIR_REL, "archive-evidence.txt")
EXTRACTED_DIR_REL = "extracted"

CHILD_DEADLINE_SECONDS = 3600
CHILD_OUTPUT_CAP = 64 << 20
READ_CHUNK = 1 << 16
JSON_READ_CAP = 1 << 20
TRANSPORT_ATTEMPTS = 3
TRANSPORT_RE = re.compile(
    r"SSL|TLS|CERTIFICATE_VERIFY|timed out|timeout|Connection reset|Connection aborted"
    r"|RemoteDisconnected|Remote end closed"
)
AUTH_RE = re.compile(r"HTTP (401|403)|no IGN account")

EXIT_OK = 0
EXIT_GUARD = 2
EXIT_CHILD = 3
EXIT_STATE = 4

STATUS_SKIP = "SKIP"
STATUS_RUN = "RUN"
STATUS_STOP = "STOP"

DOTNET_COMMAND = (
    'PANOPTICON_GEO_PROOF_DIR={root} CNC_GENERALS_PATH="/home/rmessinger/dev/Command and '
    'Conquer Generals" dotnet test src --filter "FullyQualifiedName~SedanOperatorProof"'
)


class Stop(Exception):
    def __init__(self, code, message):
        super().__init__(message)
        self.code = code
        self.message = message


class ChildResult(NamedTuple):
    returncode: int
    stdout: str
    stderr: str


class Config(NamedTuple):
    root: str
    mount_point: str
    mounts_path: str


def now_iso():
    return datetime.now().astimezone().isoformat(timespec="milliseconds")


def log(message):
    print(f"{now_iso()} {message}", flush=True)


def resolve_root(positional, environ, default=DEFAULT_ROOT):
    if positional is not None:
        raw, source = positional, "positional"
        if not raw.strip():
            raise Stop(EXIT_GUARD, f"positional root is empty or whitespace-only (got {raw!r}); "
                                   "provide an absolute path or omit for env/default resolution")
    elif environ.get(ROOT_ENV):
        raw, source = environ[ROOT_ENV], f"env {ROOT_ENV}"
    else:
        raw, source = default, "default"
    if "\x00" in raw or not os.path.isabs(raw):
        raise Stop(EXIT_GUARD, f"root must be an absolute path (got {raw!r}; source {source})")
    normalized = os.path.normpath(raw)
    if normalized == os.sep:
        raise Stop(EXIT_GUARD, "refusing the filesystem root as the cache root")
    return normalized, source


def read_json_bounded(path, label):
    try:
        size = os.lstat(path).st_size
    except OSError as error:
        raise Stop(EXIT_STATE, f"unreadable {label} at {path}: {error}")
    if size > JSON_READ_CAP:
        raise Stop(EXIT_STATE, f"{label} at {path} exceeds the {JSON_READ_CAP}-byte bound")
    try:
        with open(path, "rb") as handle:
            raw = handle.read()
        return json.loads(raw), raw
    except (OSError, ValueError) as error:
        raise Stop(EXIT_STATE, f"corrupted {label} at {path}: {error}")


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for block in iter(lambda: handle.read(READ_CHUNK), b""):
            digest.update(block)
    return digest.hexdigest()


def stage_dirs(parent, prefix):
    if not os.path.isdir(parent) or os.path.islink(parent):
        return []
    found = []
    with os.scandir(parent) as entries:
        for entry in entries:
            if entry.name.startswith(prefix) and entry.is_dir(follow_symlinks=False):
                found.append(entry.path)
    return sorted(found)


def completed_stages(parent, prefix, label):
    completed = [path for path in stage_dirs(parent, prefix)
                 if os.path.isfile(os.path.join(path, COMPLETED_NAME))]
    if len(completed) > 1:
        raise Stop(EXIT_STATE, f"multiple completed {label} stages under {parent}: {completed}")
    return completed[0] if completed else None


def check_archives(cfg):
    path = os.path.join(cfg.root, STATE_REL)
    if not os.path.lexists(path):
        return False
    state, _raw = read_json_bounded(path, "acquisition state")
    products = state.get("products") if isinstance(state, dict) else None
    if not isinstance(products, dict):
        raise Stop(EXIT_STATE, f"acquisition state at {path} has no products object")
    complete = [entry for entry in products.values()
                if isinstance(entry, dict) and entry.get("status") == "complete"]
    return len(products) == ARCHIVE_PART_COUNT and len(complete) == ARCHIVE_PART_COUNT


def check_extract(cfg):
    return completed_stages(os.path.join(cfg.root, EXTRACTED_DIR_REL), EXTRACT_STAGE_PREFIX,
                            "extract") is not None


def venv_query(python, code):
    env = {key: value for key, value in os.environ.items() if key not in ("PYTHONPATH", "PYTHONHOME")}
    env["PYTHONDONTWRITEBYTECODE"] = "1"
    result = RUN_CHILD([python, "-c", code], deadline=VENV_QUERY_SECONDS,
                       cap=VENV_QUERY_CAP_BYTES, env=env)
    if result.returncode != 0:
        raise Stop(EXIT_STATE, f"venv python query exited {result.returncode}: "
                               f"{result.stderr.strip()[:300]}")
    return result.stdout.splitlines()


def check_identity(venv_dir, python):
    lines = venv_query(python, IDENTITY_CODE)
    if len(lines) != 2:
        raise Stop(EXIT_STATE, f"venv python identity query returned {len(lines)} lines; expected 2")
    prefix, base_prefix = lines
    if os.path.normpath(prefix) != os.path.normpath(venv_dir):
        raise Stop(EXIT_STATE, f"venv python reports sys.prefix {prefix}; expected {venv_dir}")
    if base_prefix == prefix:
        raise Stop(EXIT_STATE, f"venv python reports sys.base_prefix equal to sys.prefix: {prefix}")


def check_packages(python):
    found = {}
    for line in venv_query(python, PACKAGE_CODE):
        name, _separator, version = line.partition(" ")
        found[name] = version
    for name, version in EXPECTED_PACKAGES:
        if found.get(name) != version:
            raise Stop(EXIT_STATE, f"venv package {name} is {found.get(name)!r}; expected {version}")


def verify_venv(cfg):
    venv_dir = os.path.join(cfg.root, VENV_DIR_REL)
    python = os.path.join(cfg.root, VENV_PYTHON_REL)
    config_path = os.path.join(venv_dir, VENV_CONFIG_NAME)
    try:
        with open(config_path, "rb") as handle:
            handle.read(READ_CHUNK)
    except OSError as error:
        raise Stop(EXIT_STATE, f"venv {VENV_CONFIG_NAME} unreadable at {config_path}: {error}")
    if not os.path.isfile(python) or not os.access(python, os.X_OK):
        raise Stop(EXIT_STATE, f"venv python is not an executable file: {python}")
    check_identity(venv_dir, python)
    check_packages(python)
    receipt = next((path for path in (os.path.join(venv_dir, VENV_LOCK_RECEIPT_NAME), VENV_LOCK_RECEIPT_LOG)
                    if os.path.isfile(path)), None)
    if receipt:
        log(f"venv ownership receipt: {receipt}")
    else:
        log("verified existing venv (packages correct, no ownership receipt found; created before "
            "receipt policy); not adopted or modified")


def check_first_tile(cfg):
    """Return (state, detail): state is 'absent', 'pass', or 'fail'."""
    path = os.path.join(cfg.root, PROBE_REPORT_REL)
    if not os.path.lexists(path):
        return "absent", "v1.2 report absent"
    report, _raw = read_json_bounded(path, "v1.2 probe report")
    if not isinstance(report, dict) or report.get("report_version") != 2:
        raise Stop(EXIT_STATE, f"v1.2 probe report at {path} is not a report_version 2 object")
    if report.get("tile") != FIRST_TILE_NAME:
        raise Stop(EXIT_STATE, f"v1.2 probe report at {path} records tile {report.get('tile')!r}")
    gates_pass = report.get("gates_pass")
    if not isinstance(gates_pass, bool):
        raise Stop(EXIT_STATE, f"v1.2 probe report at {path} has no boolean gates_pass")
    return ("pass", "v1.2 gates PASS") if gates_pass else ("fail", "v1.2 gates FAIL")


def check_remaining(cfg):
    path = os.path.join(cfg.root, STAGE2_STATE_REL)
    if not os.path.lexists(path):
        return False
    state, _raw = read_json_bounded(path, "stage-2 state")
    tiles = state.get("tiles") if isinstance(state, dict) else None
    if not isinstance(tiles, dict):
        raise Stop(EXIT_STATE, f"stage-2 state at {path} has no tiles object")
    if set(tiles) - set(STAGE2_TILES):
        raise Stop(EXIT_STATE, f"stage-2 state at {path} carries unknown tiles")
    for name, size in STAGE2_TILES.items():
        entry = tiles.get(name)
        if not isinstance(entry, dict) or entry.get("status") != "complete" \
                or entry.get("expected_size") != size:
            return False
    return True


def check_freeze(cfg):
    stage = completed_stages(os.path.join(cfg.root, FREEZE_DIR_REL), FREEZE_STAGE_PREFIX, "freeze")
    return stage


def manifest_worker_state(cfg):
    path = os.path.join(cfg.root, FREEZE_DIR_REL, INTEGRITY_MANIFEST_NAME)
    if not os.path.lexists(path):
        return "absent"
    manifest, _raw = read_json_bounded(path, "integrity manifest")
    if not isinstance(manifest, dict):
        raise Stop(EXIT_STATE, f"integrity manifest at {path} is not a JSON object")
    worker = manifest.get("worker")
    if worker is None:
        return "unconfigured"
    if worker == {"rel_path": BANDED_WORKER_REL, "sha256": BANDED_WORKER_SHA256}:
        return "configured"
    raise Stop(EXIT_STATE, f"integrity manifest worker block differs from the banded entrypoint")


def read_regular_nofollow(path, cap, label):
    # O_NONBLOCK keeps a FIFO at the path from blocking the open before the regular-file check.
    try:
        descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    except OSError as error:
        raise Stop(EXIT_STATE, f"{label} at {path} is missing or not a readable non-symlink file: {error}")
    with os.fdopen(descriptor, "rb") as handle:
        if not stat.S_ISREG(os.fstat(handle.fileno()).st_mode):
            raise Stop(EXIT_STATE, f"{label} at {path} is not a regular file")
        data = handle.read(cap + 1)
    if len(data) > cap:
        raise Stop(EXIT_STATE, f"{label} at {path} exceeds the {cap}-byte bound")
    return data


def read_proof_json(path, cap, label):
    data = read_regular_nofollow(path, cap, label)
    try:
        return json.loads(data)
    except ValueError as error:
        raise Stop(EXIT_STATE, f"{label} at {path} is not valid JSON: {error}")


def output_fields(cfg, label, record):
    if not isinstance(record, dict):
        raise Stop(EXIT_STATE, f"B receipt output {label} is not an object")
    raw = record.get("path")
    if not isinstance(raw, str) or not raw or "\x00" in raw or ".." in raw.split(os.sep):
        raise Stop(EXIT_STATE, f"B receipt output {label} has an unusable path: {raw!r}")
    path = os.path.normpath(raw if os.path.isabs(raw) else os.path.join(cfg.root, raw))
    if not path.startswith(cfg.root + os.sep):
        raise Stop(EXIT_STATE, f"B receipt output {label} path {path} is outside the root {cfg.root}")
    sha = record.get("sha256")
    if not isinstance(sha, str) or not SHA256_RE.fullmatch(sha):
        raise Stop(EXIT_STATE, f"B receipt output {label} at {path} has a malformed sha256: {sha!r}")
    size = record.get("size")
    if not isinstance(size, int) or isinstance(size, bool) or size < 0:
        raise Stop(EXIT_STATE, f"B receipt output {label} at {path} has a malformed size: {size!r}")
    return path, sha, size


def check_output_ancestors(cfg, label, path):
    probe = cfg.root
    for piece in filter(None, os.path.dirname(path)[len(cfg.root):].split(os.sep)):
        probe = os.path.join(probe, piece)
        if os.path.islink(probe):
            raise Stop(EXIT_STATE, f"B receipt output {label} at {path} has a symlink ancestor: {probe}")


def verify_output_file(label, path, sha, size):
    try:
        descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    except OSError as error:
        raise Stop(EXIT_STATE, f"B output {label} at {path} is missing or not a readable "
                               f"non-symlink file: {error}")
    digest = hashlib.sha256()
    try:
        info = os.fstat(descriptor)
        if not stat.S_ISREG(info.st_mode):
            raise Stop(EXIT_STATE, f"B output {label} at {path} is not a regular file")
        if info.st_size != size:
            raise Stop(EXIT_STATE, f"B output {label} at {path} is {info.st_size} bytes; "
                                   f"receipt records {size}")
        for block in iter(lambda: os.read(descriptor, OUTPUT_CHUNK_BYTES), b""):
            digest.update(block)
    finally:
        os.close(descriptor)
    if digest.hexdigest() != sha:
        raise Stop(EXIT_STATE, f"B output {label} at {path} sha256 {digest.hexdigest()} differs "
                               f"from receipt {sha}")


def check_outputs(cfg, receipt, receipt_path):
    outputs = receipt.get("outputs")
    if not isinstance(outputs, dict):
        raise Stop(EXIT_STATE, f"B preparation receipt at {receipt_path} carries no outputs object")
    labels = set(outputs)
    if labels != BACKEND_OUTPUT_LABELS:
        raise Stop(EXIT_STATE, f"B preparation receipt at {receipt_path} outputs differ: "
                               f"missing {sorted(BACKEND_OUTPUT_LABELS - labels)}, "
                               f"unexpected {sorted(labels - BACKEND_OUTPUT_LABELS)}")
    manifest_path = os.path.join(cfg.root, PROOF_REL)
    for label in sorted(BACKEND_OUTPUT_LABELS):
        path, sha, size = output_fields(cfg, label, outputs[label])
        if label == "manifest" and path != manifest_path:
            raise Stop(EXIT_STATE, f"B receipt output manifest at {path} is not the proof "
                                   f"manifest {manifest_path}")
        check_output_ancestors(cfg, label, path)
        verify_output_file(label, path, sha, size)


def verify_proof(cfg):
    receipt_path = os.path.join(cfg.root, BACKEND_RECEIPT_REL)
    receipt = read_proof_json(receipt_path, RECEIPT_READ_CAP, "B preparation receipt")
    if not isinstance(receipt, dict) or not isinstance(receipt.get("commands_sha256"), str):
        raise Stop(EXIT_STATE, f"B preparation receipt at {receipt_path} carries no commands_sha256")
    ledger_path = os.path.join(cfg.root, BACKEND_LEDGER_REL)
    ledger = read_regular_nofollow(ledger_path, LEDGER_READ_CAP, "B chronology ledger")
    if hashlib.sha256(ledger).hexdigest() != receipt["commands_sha256"]:
        raise Stop(EXIT_STATE, f"B chronology ledger at {ledger_path} differs from the receipt commands_sha256")
    lines = [line for line in ledger.splitlines() if line.strip()]
    try:
        last_event = json.loads(lines[-1]) if lines else None
    except ValueError:
        last_event = None
    if not isinstance(last_event, dict) or last_event.get("event") != BACKEND_PHASE_COMPLETE:
        raise Stop(EXIT_STATE, f"B chronology ledger at {ledger_path} does not end with {BACKEND_PHASE_COMPLETE}")
    manifest_path = os.path.join(cfg.root, PROOF_REL)
    manifest = read_proof_json(manifest_path, JSON_READ_CAP, "sedan-ign proof manifest")
    if not isinstance(manifest, dict) or set(manifest) != PROOF_KEYS:
        raise Stop(EXIT_STATE, f"sedan-ign proof manifest at {manifest_path} does not carry exactly "
                               f"the {len(PROOF_KEYS)} bridge top-level keys")
    check_outputs(cfg, receipt, receipt_path)


def proof_defect(cfg):
    try:
        verify_proof(cfg)
    except Stop as stop:
        return stop.message
    return None


def run_child(argv, deadline=CHILD_DEADLINE_SECONDS, cap=CHILD_OUTPUT_CAP, env=None):
    """Bounded child: fair two-pipe read, EOF unregister, kill and reap on cap or deadline."""
    proc = subprocess.Popen(argv, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE, env=env)
    buffers = {"stdout": bytearray(), "stderr": bytearray()}
    selector = selectors.DefaultSelector()
    selector.register(proc.stdout, selectors.EVENT_READ, "stdout")
    selector.register(proc.stderr, selectors.EVENT_READ, "stderr")
    start = time.monotonic()
    try:
        while selector.get_map():
            remaining = deadline - (time.monotonic() - start)
            if remaining <= 0:
                raise Stop(EXIT_CHILD, f"child exceeded {deadline} s deadline: {argv[1:]}")
            for key, _mask in selector.select(timeout=min(remaining, 1.0)):
                chunk = os.read(key.fileobj.fileno(), READ_CHUNK)
                if not chunk:
                    selector.unregister(key.fileobj)
                    key.fileobj.close()
                    continue
                buffers[key.data].extend(chunk)
                if len(buffers[key.data]) > cap:
                    raise Stop(EXIT_CHILD, f"child {key.data} exceeded {cap} bytes: {argv[1:]}")
        returncode = proc.wait(timeout=max(1.0, deadline - (time.monotonic() - start)))
    except subprocess.TimeoutExpired:
        raise Stop(EXIT_CHILD, f"child did not exit after closing its pipes: {argv[1:]}")
    except Stop:
        proc.kill()
        proc.wait()
        raise
    finally:
        selector.close()
        if proc.poll() is None:
            proc.kill()
            proc.wait()
        proc.stdout.close()
        proc.stderr.close()
    return ChildResult(returncode,
                       buffers["stdout"].decode("utf-8", "replace"),
                       buffers["stderr"].decode("utf-8", "replace"))


RUN_CHILD = run_child


def invoke(label, argv):
    log(f"run: {label}: {' '.join(argv)}")
    result = RUN_CHILD(argv)
    if result.stdout:
        print(result.stdout, end="", flush=True)
    if result.stderr:
        print(result.stderr, end="", file=sys.stderr, flush=True)
    return result


def child_exit_code(returncode):
    return returncode if returncode in (EXIT_GUARD, EXIT_CHILD, EXIT_STATE) else EXIT_CHILD


def expect_success(label, result):
    if result.returncode != 0:
        raise Stop(child_exit_code(result.returncode),
                   f"{label} exited {result.returncode}; stopping (no retry)")


def acquire_archives(cfg):
    argv = [sys.executable, ACQUIRE_SCRIPT, "--download-only", cfg.root]
    for attempt in range(1, TRANSPORT_ATTEMPTS + 1):
        result = invoke(f"ARCHIVES attempt {attempt}/{TRANSPORT_ATTEMPTS}", argv)
        if result.returncode == 0:
            break
        combined = result.stdout + result.stderr
        retryable = (result.returncode == EXIT_CHILD and attempt < TRANSPORT_ATTEMPTS
                     and TRANSPORT_RE.search(combined) and not AUTH_RE.search(combined))
        if retryable:
            log("ARCHIVES transport failure (TLS, read timeout, or reset): retrying")
            continue
        expect_success("ARCHIVES", result)
    if not check_archives(cfg):
        raise Stop(EXIT_STATE, "ARCHIVES helper exited 0 but state is not 8/8 complete")


def part_path(root, key):
    match = PART_KEY_RE.fullmatch(key) if isinstance(key, str) else None
    if not match:
        raise Stop(EXIT_STATE, f"acquisition state key is not a part key: {key!r}")
    return os.path.join(root, "archives", match.group(1), match.group(3), match.group(2))


def archive_entries(cfg):
    path = os.path.join(cfg.root, STATE_REL)
    state, _raw = read_json_bounded(path, "acquisition state")
    products = state.get("products") if isinstance(state, dict) else None
    if not isinstance(products, dict) or len(products) != ARCHIVE_PART_COUNT:
        raise Stop(EXIT_STATE, f"acquisition state at {path} does not carry {ARCHIVE_PART_COUNT} products")
    entries = []
    for key in sorted(products):
        entry = products[key]
        sha = entry.get("sha256") if isinstance(entry, dict) else None
        size = entry.get("expected_size") if isinstance(entry, dict) else None
        if not isinstance(entry, dict) or entry.get("status") != "complete":
            raise Stop(EXIT_STATE, f"acquisition state part {key} is not complete")
        if not isinstance(sha, str) or not re.fullmatch(r"[0-9a-f]{64}", sha) \
                or not isinstance(size, int) or isinstance(size, bool):
            raise Stop(EXIT_STATE, f"acquisition state part {key} lacks a sha256 or expected_size")
        entries.append((part_path(cfg.root, key), sha, size))
    return entries


def archive_evidence_data(cfg):
    lines = []
    total = 0
    for path, sha, size in archive_entries(cfg):
        try:
            info = os.lstat(path)
        except OSError as error:
            raise Stop(EXIT_STATE, f"archive part missing on disk: {path}: {error}")
        if not stat.S_ISREG(info.st_mode):
            raise Stop(EXIT_STATE, f"archive part is not a regular file: {path}")
        if info.st_size != size:
            raise Stop(EXIT_STATE, f"archive part {path} is {info.st_size} bytes; state pins {size}")
        digest = sha256_file(path)
        if digest != sha:
            raise Stop(EXIT_STATE, f"archive part {path} sha256 {digest} differs from state {sha}")
        lines.append(f"{digest}  {size}  {os.path.basename(path)}\n")
        total += size
    if total != ARCHIVE_TOTAL_BYTES:
        raise Stop(EXIT_STATE, f"archive parts total {total} bytes; pinned {ARCHIVE_TOTAL_BYTES}")
    return "".join(lines).encode("utf-8")


def same_file_content(path, content):
    return os.lstat(path).st_size == len(content) and read_bytes(path) == content


def read_bytes(path):
    with open(path, "rb") as handle:
        return handle.read()


def validate_archive_evidence(path):
    try:
        PROBE_MODULE._parse_archive_manifest(path)
    except PROBE_MODULE.EvidenceProblem as problem:
        raise Stop(EXIT_STATE, f"archive evidence rejected by the acquisition parser: {problem}")


def archive_evidence_state(cfg):
    data = archive_evidence_data(cfg)
    path = os.path.join(cfg.root, ARCHIVE_EVIDENCE_REL)
    if not os.path.lexists(path):
        return "absent", data
    if os.path.islink(path) or not os.path.isfile(path):
        raise Stop(EXIT_STATE, f"archive evidence is not a regular file: {path}")
    header, separator, body = read_bytes(path).partition(b"\n")
    if not header.startswith(b"#") or not separator or body != data:
        raise Stop(EXIT_STATE, f"archive evidence at {path} differs from disk verification; not overwritten")
    validate_archive_evidence(path)
    return "match", data


def write_archive_evidence(cfg, content):
    path = os.path.join(cfg.root, ARCHIVE_EVIDENCE_REL)
    directory = os.path.dirname(path)
    os.makedirs(directory, exist_ok=True)
    fd, temp_path = tempfile.mkstemp(dir=directory, prefix=".archive-evidence-", suffix=".tmp")
    try:
        with os.fdopen(fd, "wb") as handle:
            handle.write(content)
            handle.flush()
            os.fsync(handle.fileno())
        os.chmod(temp_path, 0o644)
        validate_archive_evidence(temp_path)
        try:
            os.link(temp_path, path)
        except FileExistsError:
            if os.path.islink(path) or not same_file_content(path, content):
                raise Stop(EXIT_STATE, f"archive evidence appeared during write and differs: {path}")
        except OSError as error:
            raise Stop(EXIT_STATE, f"archive evidence not written at {path}: {error}")
    finally:
        os.unlink(temp_path)
    log(f"archive evidence written: {path}")


def stage_archives(cfg, act):
    if not check_archives(cfg):
        if not act:
            return STATUS_RUN, "acquisition needed (sedan_acquire.py --download-only); evidence follows"
        acquire_archives(cfg)
    state, data = archive_evidence_state(cfg)
    if state == "match":
        return STATUS_SKIP, "8/8 parts complete; archive-evidence.txt matches disk SHA-256 and sizes"
    if not act:
        return STATUS_RUN, "archive-evidence.txt to be written from disk SHA-256 and sizes"
    header = f"# Sedan archive completion evidence {now_iso()}\n".encode("utf-8")
    write_archive_evidence(cfg, header + data)
    return STATUS_RUN, "archive-evidence.txt written from disk SHA-256 and sizes"


def stage_extract(cfg, act):
    if check_extract(cfg):
        return STATUS_SKIP, "COMPLETED marker present (member SHAs not re-verified here)"
    if not act:
        return STATUS_RUN, "selective extraction needed (sedan_extract.py --extract)"
    expect_success("EXTRACT", invoke("EXTRACT", [sys.executable, EXTRACT_SCRIPT, cfg.root,
                                                 "--extract"]))
    if not check_extract(cfg):
        raise Stop(EXIT_STATE, "EXTRACT helper exited 0 without a COMPLETED marker")
    return STATUS_RUN, "extracted"


def call_bootstrap(label, function, *args):
    try:
        return function(*args)
    except BOOTSTRAP_MODULE.Stop as stop:
        raise Stop(stop.code, f"{label}: {stop.message}")


def stage_tools(cfg, act):
    existed = os.path.lexists(os.path.join(cfg.root, VENV_DIR_REL))
    if not act:
        if not existed:
            return STATUS_RUN, "tools bootstrap needed (pinned wheels into tools/lidar-venv)"
        verify_venv(cfg)
        return STATUS_SKIP, "tools/lidar-venv verified (sys.prefix, base prefix, package versions)"
    call_bootstrap("TOOLS", BOOTSTRAP_MODULE.bootstrap_tools, cfg.root)
    if existed:
        return STATUS_SKIP, "tools/lidar-venv verified by bootstrap_tools"
    return STATUS_RUN, "tools/lidar-venv created with pinned wheels"


def probe_command(root, args):
    return [sys.executable, PROBE_SCRIPT, "--cache-root", root, *args]


def check_lineage(cfg):
    path = os.path.join(cfg.root, LINEAGE_REL)
    lineage, _raw = read_json_bounded(path, "WFS lineage record")
    if not isinstance(lineage, dict) or not lineage:
        raise Stop(EXIT_STATE, f"WFS lineage record at {path} is not a non-empty JSON object")
    return path


def stage_first_tile(cfg, act):
    state, _detail = check_first_tile(cfg)
    if state == "pass":
        return STATUS_SKIP, "v1.2 report records the first tile with gates PASS"
    if state == "fail":
        raise Stop(EXIT_GUARD, "v1.2 gate FAIL: stopping (no retry, no threshold change)")
    lineage_present = os.path.lexists(os.path.join(cfg.root, LINEAGE_REL))
    if lineage_present:
        check_lineage(cfg)
    tile_present = os.path.lexists(os.path.join(cfg.root, FIRST_TILE_REL))
    if not act:
        plan = "probe run" if tile_present else "fetch, then probe run"
        lineage = "" if lineage_present else "; WFS lineage fetch first"
        return STATUS_RUN, f"first tile {plan} for the v1.2 report{lineage}"
    if not lineage_present:
        call_bootstrap("FIRST TILE lineage", BOOTSTRAP_MODULE.fetch_lineage, cfg.root)
    lineage_path = check_lineage(cfg)
    if not tile_present:
        expect_success("FIRST TILE", invoke("FIRST TILE", probe_command(cfg.root, [
            "fetch", FIRST_TILE_NAME, "--first-tile", "--execution-brief-accepted"])))
    expect_success("FIRST TILE probe", invoke("FIRST TILE probe", probe_command(cfg.root, [
        "probe", "--worker", os.path.join(cfg.root, VENV_PYTHON_REL), PROBE_WORKER_PATH,
        "--lineage-file", lineage_path])))
    state, _detail = check_first_tile(cfg)
    if state == "pass":
        return STATUS_RUN, "first tile probed; v1.2 gates PASS"
    if state == "fail":
        raise Stop(EXIT_GUARD, "v1.2 gate FAIL after probe: stopping (no retry)")
    raise Stop(EXIT_STATE, "first-tile probe exited 0 without a v1.2 report")


def stage_remaining(cfg, act):
    if check_remaining(cfg):
        return STATUS_SKIP, "five stage-2 tiles complete at pinned sizes"
    if not act:
        return STATUS_RUN, "stage-2 fetch needed (sedan_lidar_acquire.py --fetch-remaining)"
    expect_success("REMAINING", invoke("REMAINING", [
        sys.executable, STAGE2_SCRIPT, "--fetch-remaining", "--cache-root", cfg.root,
        "--report", os.path.join(cfg.root, PROBE_REPORT_REL)]))
    if not check_remaining(cfg):
        raise Stop(EXIT_STATE, "stage-2 helper exited 0 but five tiles are not complete")
    return STATUS_RUN, "five stage-2 tiles complete"


def configure_manifest(cfg):
    path = os.path.join(cfg.root, FREEZE_DIR_REL, INTEGRITY_MANIFEST_NAME)
    if sha256_file(BANDED_WORKER_PATH) != BANDED_WORKER_SHA256:
        raise Stop(EXIT_STATE, "banded worker drift: file sha256 differs from the pinned entrypoint")
    manifest, raw = read_json_bounded(path, "integrity manifest")
    expected = set(STAGE2_TILES) | {FIRST_TILE_NAME}
    if not isinstance(manifest, dict) or set(manifest) - {"worker"} != expected:
        raise Stop(EXIT_STATE, "integrity manifest does not carry exactly the six pinned tiles")
    before = hashlib.sha256(raw).hexdigest()
    manifest["worker"] = {"rel_path": BANDED_WORKER_REL, "sha256": BANDED_WORKER_SHA256}
    new_raw = (json.dumps(manifest, indent=2, sort_keys=True) + "\n").encode("utf-8")
    tmp_path = path + ".tmp"
    with open(tmp_path, "wb") as handle:
        handle.write(new_raw)
        handle.flush()
        os.fsync(handle.fileno())
    os.replace(tmp_path, path)
    after = hashlib.sha256(new_raw).hexdigest()
    log(f"worker block configured: manifest sha256 {before} -> {after}")


def stage_freeze(cfg, act):
    stage = check_freeze(cfg)
    if stage:
        state, _raw = read_json_bounded(os.path.join(stage, STATE_REL), "freeze state")
        digest = state.get("canonical_digest") if isinstance(state, dict) else None
        if not isinstance(digest, str):
            raise Stop(EXIT_STATE, f"completed freeze {stage} records no canonical_digest")
        if act:
            expect_success("FREEZE reverify", invoke("FREEZE reverify", [
                sys.executable, FREEZE_SCRIPT, cfg.root, "--freeze"]))
        return STATUS_SKIP, f"canonical_digest {digest}; artifacts {stage}"
    manifest_state = manifest_worker_state(cfg)
    if manifest_state == "unconfigured":
        raise Stop(EXIT_STATE, "integrity manifest present without the banded worker block; "
                               "not adopted (remove it manually after review)")
    if not act:
        return STATUS_RUN, "integrity producer, configure, freeze" if manifest_state == "absent" \
            else "freeze (manifest already configured)"
    if manifest_state == "absent":
        expect_success("INTEGRITY producer", invoke("INTEGRITY producer", [
            sys.executable, INTEGRITY_SCRIPT, "--prepare-integrity", "--cache", cfg.root]))
        configure_manifest(cfg)
    else:
        log("integrity manifest already configured with the banded worker: producer not invoked")
    expect_success("FREEZE", invoke("FREEZE", [sys.executable, FREEZE_SCRIPT, cfg.root,
                                               "--freeze"]))
    stage = check_freeze(cfg)
    if not stage:
        raise Stop(EXIT_STATE, "freeze helper exited 0 without a COMPLETED marker")
    return STATUS_RUN, f"frozen at {stage}"


def stage_proof(cfg, act):
    command = DOTNET_COMMAND.format(root=cfg.root)
    log(f"follow-on Interactive proof (not run by this orchestrator): {command}")
    defect = proof_defect(cfg)
    if defect is None:
        return STATUS_SKIP, f"{BACKEND_RECEIPT_REL} ({BACKEND_PHASE_COMPLETE}) and {PROOF_REL} verified"
    if not act:
        return STATUS_RUN, f"B prepare needed: {defect}"
    if not os.path.isfile(BACKEND_SCRIPT):
        raise Stop(EXIT_STATE, f"B backend script missing at {BACKEND_SCRIPT}")
    expect_success("B PREPARE", invoke("B PREPARE", ["python3", BACKEND_SCRIPT, cfg.root, "--prepare"]))
    defect = proof_defect(cfg)
    if defect is not None:
        raise Stop(EXIT_STATE, f"B prepare exited 0 but {defect}")
    return STATUS_RUN, f"B prepare completed; {BACKEND_RECEIPT_REL} and {PROOF_REL} verified"


STAGES = (
    ("ARCHIVES", stage_archives),
    ("EXTRACT", stage_extract),
    ("TOOLS", stage_tools),
    ("FIRST TILE", stage_first_tile),
    ("REMAINING 5", stage_remaining),
    ("INTEGRITY+FREEZE", stage_freeze),
    ("B POST-PREPARE", stage_proof),
)


def check_mount(cfg):
    if not (cfg.root == cfg.mount_point or cfg.root.startswith(cfg.mount_point + os.sep)):
        raise Stop(EXIT_GUARD, f"root {cfg.root} is not under {cfg.mount_point}")
    with open(cfg.mounts_path, "r", encoding="utf-8") as handle:
        for line in handle:
            fields = line.split()
            if len(fields) >= 4 and fields[1] == cfg.mount_point and "rw" in fields[3].split(","):
                return
    raise Stop(EXIT_GUARD, f"no exact rw mount entry for {cfg.mount_point} in {cfg.mounts_path}")


def check_ancestor(cfg):
    probe = os.sep
    for piece in (part for part in cfg.root.split(os.sep) if part):
        probe = os.path.join(probe, piece)
        if os.path.islink(probe):
            raise Stop(EXIT_GUARD, f"symlink component in root path: {probe}")
    ancestor = cfg.root
    while not os.path.exists(ancestor):
        parent = os.path.dirname(ancestor)
        if parent == ancestor:
            raise Stop(EXIT_GUARD, "no existing ancestor found")
        ancestor = parent
    if os.path.islink(ancestor) or not os.path.isdir(ancestor):
        raise Stop(EXIT_GUARD, f"nearest existing ancestor is not a real directory: {ancestor}")
    if not os.access(ancestor, os.W_OK | os.X_OK):
        raise Stop(EXIT_GUARD, f"nearest existing ancestor is not writable: {ancestor}")
    return ancestor


def lock_paths(cfg):
    paths = [os.path.join(cfg.root, LOCK_REL), os.path.join(cfg.root, STAGE2_LOCK_REL),
             os.path.join(cfg.root, PROBE_LOCK_REL),
             os.path.join(cfg.root, FREEZE_DIR_REL, PRODUCER_MARKER_NAME)]
    for stage in stage_dirs(os.path.join(cfg.root, EXTRACTED_DIR_REL), EXTRACT_STAGE_PREFIX):
        paths.append(os.path.join(stage, LOCK_REL))
    for stage in stage_dirs(os.path.join(cfg.root, FREEZE_DIR_REL), FREEZE_STAGE_PREFIX):
        paths.append(os.path.join(stage, LOCK_REL))
    return [path for path in paths if os.path.lexists(path)]


def requirements(cfg):
    needed = []
    if not check_archives(cfg):
        needed.append(ARCHIVE_TOTAL_BYTES)
    if not os.path.lexists(os.path.join(cfg.root, FIRST_TILE_REL)):
        needed.append(FIRST_TILE_BYTES)
    if not check_remaining(cfg):
        needed.append(sum(STAGE2_TILES.values()))
    if not check_freeze(cfg):
        needed.append(FREEZE_SCRATCH_BYTES)
    return needed


def check_guards(cfg):
    check_mount(cfg)
    ancestor = check_ancestor(cfg)
    locks = lock_paths(cfg)
    if locks:
        raise Stop(EXIT_GUARD, f"lock or producer marker present, overlapping job refused: {locks}")
    required = sum(requirements(cfg)) + RESERVE_BYTES
    usage = os.statvfs(ancestor)
    free = usage.f_bavail * usage.f_frsize
    log(f"free space: free={free} required={required} (stage requirements + 1 GiB)")
    if free < required:
        raise Stop(EXIT_GUARD, f"insufficient free space on {ancestor}: remaining={required - free}")


def run_preflight(cfg):
    try:
        check_guards(cfg)
        log("guards: would pass for --prepare")
    except Stop as stop:
        log(f"guards: would refuse for --prepare: {stop.message}")
    code = EXIT_OK
    for name, stage in STAGES:
        try:
            status, detail = stage(cfg, False)
        except Stop as stop:
            status, detail = STATUS_STOP, stop.message
            code = max(code, stop.code)
        log(f"stage {name}: {status} {detail}")
    return code


def run_prepare(cfg):
    check_guards(cfg)
    for name, stage in STAGES:
        status, detail = stage(cfg, True)
        log(f"stage {name}: {status} {detail}")
    return EXIT_OK


def run_recover(cfg):
    manual = lock_paths_manual(cfg)
    if manual:
        raise Stop(EXIT_GUARD, f"no recover flag exists for {manual}; remove manually after "
                               f"confirming no run is active")
    targets = []
    if os.path.lexists(os.path.join(cfg.root, LOCK_REL)):
        targets.append(("ARCHIVES", [sys.executable, ACQUIRE_SCRIPT, "--recover-lock", cfg.root]))
    for stage in stage_dirs(os.path.join(cfg.root, EXTRACTED_DIR_REL), EXTRACT_STAGE_PREFIX):
        if os.path.lexists(os.path.join(stage, LOCK_REL)):
            targets.append(("EXTRACT", [sys.executable, EXTRACT_SCRIPT, cfg.root,
                                        "--recover-lock"]))
            break
    if any(os.path.lexists(os.path.join(stage, LOCK_REL))
           for stage in stage_dirs(os.path.join(cfg.root, FREEZE_DIR_REL), FREEZE_STAGE_PREFIX)):
        targets.append(("FREEZE", [sys.executable, FREEZE_SCRIPT, cfg.root, "--recover-lock"]))
    if os.path.lexists(os.path.join(cfg.root, FREEZE_DIR_REL, PRODUCER_MARKER_NAME)):
        targets.append(("INTEGRITY", [sys.executable, INTEGRITY_SCRIPT, "--cache", cfg.root,
                                      "--recover-lock"]))
    if not targets:
        log("no recoverable lock present")
        return EXIT_OK
    for label, argv in targets:
        expect_success(label, invoke(f"RECOVER {label}", argv))
    return EXIT_OK


def lock_paths_manual(cfg):
    manual = [path for path in (os.path.join(cfg.root, STAGE2_LOCK_REL),
                                os.path.join(cfg.root, PROBE_LOCK_REL))
              if os.path.lexists(path)]
    return manual


def build_parser():
    parser = argparse.ArgumentParser(
        prog="sedan_prepare_orchestrator.py",
        description=(
            "Unattended Sedan geo-proof preparation. Default: read-only preflight. --prepare: the "
            "explicit operation. --recover-lock: remove stale locks via the owning helpers."
        ),
    )
    parser.add_argument("root", nargs="?", default=None,
                        help=f"cache root; resolution: positional > env {ROOT_ENV} > {DEFAULT_ROOT}")
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument("--prepare", action="store_true")
    modes.add_argument("--recover-lock", action="store_true")
    return parser


def main(argv=None):
    args = build_parser().parse_args(argv)
    mode = "prepare" if args.prepare else "recover-lock" if args.recover_lock else "preflight"
    code = EXIT_OK
    root = args.root
    try:
        root, source = resolve_root(args.root, os.environ)
        log(f"root: {root} (source: {source})")
        cfg = Config(root=root, mount_point=MOUNT_POINT, mounts_path=PROC_MOUNTS_PATH)
        if args.prepare:
            code = run_prepare(cfg)
        elif args.recover_lock:
            code = run_recover(cfg)
        else:
            code = run_preflight(cfg)
    except Stop as stop:
        log(f"FAIL({stop.code}): {stop.message}")
        code = stop.code
    log(f"final status: exit={code} mode={mode} root={root}")
    return code


if __name__ == "__main__":
    sys.exit(main())
