#!/usr/bin/env python3
"""Integrity-manifest producer for the six-tile Sedan LiDAR freeze input.

Writes <cache>/freeze/lidar-integrity-manifest.json, the input that
sedan_lidar_freeze.py verifies read-only, plus a provenance sidecar
<cache>/freeze/lidar-integrity-producer-receipt.json that the consumer never reads.

Modes: default = READ-ONLY preflight (no writes, no child process, no network,
no tile hashing). --prepare-integrity = the explicit producing operation (single
attempt, no auto-retry). --recover-lock = remove a producer marker whose recorded
pid is no longer alive. Mode flags are mutually exclusive.

Guard order for --prepare-integrity: pin table -> rw mount -> cache root and freeze
ancestor (no symlinks) -> transport state (stage-2 state.json and the first tile)
-> on-disk sizes -> sha256 of all six tiles -> existing manifest (identical:
success with zero children; different or unbound: exit 4, no overwrite) -> venv
python -> producer marker -> header children (one per tile, sequential, header
fields only) -> manifest and receipt written atomically with no overwrite.

Exit codes: 0 success; 2 guard failure (missing mount, cache, venv or state file);
3 header worker process failure; 4 state, pin, integrity, drift or collision failure.
Python 3 standard library only. Header-only: no point data is ever decoded here.
"""

import argparse
import hashlib
import json
import math
import os
import selectors
import shutil
import stat
import subprocess
import sys
import time
from datetime import datetime

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


LOT = "NUALHD_1-0__LAZ_LAMB93_OD_2026-04-29"
FIRST_TILE_NAME = "LHD_FXX_0840_6958_PTS_LAMB93_IGN69.copc.laz"
FIRST_TILE_SIZE = 128_651_625
FIRST_TILE_SHA256 = "19465caf3085665f94fd648315896a84cbcf8fd2d9777eca85800c131979d212"
STAGE2_TILES = (
    ("LHD_FXX_0839_6958_PTS_LAMB93_IGN69.copc.laz", 119_931_651),
    ("LHD_FXX_0839_6959_PTS_LAMB93_IGN69.copc.laz", 146_099_989),
    ("LHD_FXX_0840_6959_PTS_LAMB93_IGN69.copc.laz", 130_615_694),
    ("LHD_FXX_0841_6958_PTS_LAMB93_IGN69.copc.laz", 153_352_444),
    ("LHD_FXX_0841_6959_PTS_LAMB93_IGN69.copc.laz", 151_880_730),
)

PINS = (
    {"name": FIRST_TILE_NAME, "size": FIRST_TILE_SIZE, "sha256": FIRST_TILE_SHA256,
     "rel": f"lidarhd/{LOT}/{FIRST_TILE_NAME}"},
) + tuple(
    {"name": name, "size": size, "sha256": None, "rel": f"lidarhd/stage2/{LOT}/{name}"}
    for name, size in STAGE2_TILES
)
STAGE2_NAMES = frozenset(name for name, _size in STAGE2_TILES)

DEFAULT_CACHE_ROOT = "/mnt/pit2/panopticon/geo-proof/sedan"
MOUNT_POINT = "/mnt/pit2"
PROC_MOUNTS_PATH = "/proc/mounts"
STATE_REL = "lidarhd/stage2/state/state.json"
STATE_VERSION = 1
VENV_PYTHON_REL = "tools/lidar-venv/bin/python"
WORKER_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "sedan_lidar_header_worker.py")

FREEZE_DIR = "freeze"
MANIFEST_NAME = "lidar-integrity-manifest.json"
RECEIPT_NAME = "lidar-integrity-producer-receipt.json"
MARKER_NAME = "PRODUCER-MARKER"
SCRATCH_PREFIX = "lidar-integrity-producer-"

READ_BOUND_BYTES = 64 << 20
CHUNK_SIZE = 1 << 20
CHILD_CAP_BYTES = 64 << 20
CHILD_DEADLINE_SECONDS = 300.0

EXPECTED_LAS_VERSION = "1.4"
EXPECTED_POINT_FORMAT = 6
EXPECTED_SCALE = 0.01
LAMBERT93_EPSG = 2154
PLAUSIBLE_X_RANGE = (839_000.0, 842_000.0)
PLAUSIBLE_Y_RANGE = (6_957_000.0, 6_959_000.0)


def now_iso():
    return datetime.now().astimezone().isoformat(timespec="milliseconds")


def log(message):
    print(f"{now_iso()} {message}", flush=True)


def is_hex64(value):
    return isinstance(value, str) and len(value) == 64 and all(c in "0123456789abcdef" for c in value)


def sha256_bytes(data):
    return hashlib.sha256(data).hexdigest()


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        while True:
            block = handle.read(CHUNK_SIZE)
            if not block:
                break
            digest.update(block)
    return digest.hexdigest()


def validate_pin_table(pins):
    if len(pins) != 6:
        fail_state(f"pin table holds {len(pins)} entries, expected 6")
    names = set()
    for index, pin in enumerate(pins):
        name = pin["name"]
        if not name.endswith(".copc.laz") or name in names:
            fail_state(f"pin {name!r} is malformed or duplicated")
        names.add(name)
        if not isinstance(pin["size"], int) or isinstance(pin["size"], bool) or pin["size"] <= 0:
            fail_state(f"pin {name!r} carries a non-positive size")
        rel = pin["rel"]
        components = rel.split("/")
        if rel.startswith("/") or "\\" in rel or any(c in ("", ".", "..") for c in components):
            fail_state(f"pin {name!r} relative path is not a clean posix path: {rel!r}")
        if components[-1] != name:
            fail_state(f"pin {name!r} relative path does not end with the tile name: {rel!r}")
        if index == 0:
            if not is_hex64(pin["sha256"]):
                fail_state(f"first tile pin {name!r} carries no 64-hex sha256")
        elif pin["sha256"] is not None:
            fail_state(f"stage-2 pin {name!r} must take its sha256 from the transport state")
    return pins


def check_mount_rw(cache, strict=True):
    """True when the exact rw mount entry for MOUNT_POINT exists. strict=False reports instead of refusing."""
    def refuse(message):
        if strict:
            fail_guard(message)
        log(f"mount: {message}")
        return False

    if not (cache == MOUNT_POINT or cache.startswith(MOUNT_POINT + os.sep)):
        return refuse(f"cache root {cache} is not under {MOUNT_POINT}")
    try:
        with open(PROC_MOUNTS_PATH, "r", encoding="utf-8") as handle:
            lines = handle.readlines()
    except OSError as error:
        return refuse(f"cannot read {PROC_MOUNTS_PATH}: {error}")
    for line in lines:
        fields = line.split()
        if len(fields) >= 4 and fields[1] == MOUNT_POINT and "rw" in fields[3].split(","):
            log(f"mount verified: {MOUNT_POINT} is an rw mount")
            return True
    return refuse(f"no exact rw mount entry for {MOUNT_POINT} in {PROC_MOUNTS_PATH}")


def check_cache_root(cache):
    if os.path.islink(cache) or not os.path.isdir(cache):
        fail_guard(f"cache root {cache} is not an existing real directory")


def no_follow_path(cache, rel):
    path = os.path.join(cache, *rel.split("/"))
    probe = cache
    for component in rel.split("/"):
        probe = os.path.join(probe, component)
        if os.path.islink(probe):
            fail_state(f"symlink component not allowed under the cache root: {probe}")
    return path


def check_freeze_ancestor(cache):
    """'exists' or 'absent' for <cache>/freeze; a symlink or non-directory is exit 4."""
    freeze = os.path.join(cache, FREEZE_DIR)
    if os.path.lexists(freeze):
        if os.path.islink(freeze) or not os.path.isdir(freeze):
            fail_state(f"operator collision: {freeze} exists and is not a real directory; left untouched")
        if not os.access(freeze, os.W_OK | os.X_OK):
            fail_guard(f"freeze directory {freeze} is not writable and searchable")
        log(f"freeze ancestor ok: {freeze} (existing real directory)")
        return "exists"
    if not os.access(cache, os.W_OK | os.X_OK):
        fail_guard(f"cache root {cache} is not writable: cannot create {FREEZE_DIR}/")
    log(f"freeze ancestor ok: {freeze} absent, creatable under the cache root")
    return "absent"


def read_transport_state(cache, pins):
    """(tiles, raw_sha256): read-only structural verification of the stage-2 state."""
    path = os.path.join(cache, *STATE_REL.split("/"))
    if not os.path.lexists(path):
        fail_guard(f"transport state missing at {path}: the stage-2 acquisition has not produced it")
    if os.path.islink(path) or not os.path.isfile(path):
        fail_state(f"transport state at {path} is not a regular non-symlink file")
    if os.path.getsize(path) > READ_BOUND_BYTES:
        fail_state(f"transport state at {path} exceeds the {READ_BOUND_BYTES}-byte read bound")
    with open(path, "rb") as handle:
        raw = handle.read()
    try:
        state = json.loads(raw.decode("utf-8"))
    except (UnicodeDecodeError, ValueError) as error:
        fail_state(f"transport state at {path} is not readable JSON: {error}")
    if not isinstance(state, dict) or state.get("version") != STATE_VERSION:
        fail_state(f"transport state at {path} has no version {STATE_VERSION} object")
    tiles = state.get("tiles")
    if not isinstance(tiles, dict):
        fail_state(f"transport state at {path} carries no tiles object")
    if set(tiles) != STAGE2_NAMES:
        fail_state(f"transport state tiles {sorted(set(tiles))} are not exactly the five stage-2 names")
    sizes = {pin["name"]: pin["size"] for pin in pins}
    records = {}
    for name in sorted(STAGE2_NAMES):
        record = tiles[name]
        if not isinstance(record, dict):
            fail_state(f"transport state record for {name} is not a JSON object")
        if record.get("status") != "complete":
            fail_state(f"transport state for {name} is {record.get('status')!r}, not 'complete'")
        for key in ("expected_size", "bytes_on_disk"):
            value = record.get(key)
            if not isinstance(value, int) or isinstance(value, bool) or value != sizes[name]:
                fail_state(f"transport state {key} for {name} is {value!r}, pinned {sizes[name]}")
        if not is_hex64(record.get("sha256")):
            fail_state(f"transport state sha256 for {name} is not 64-hex")
        records[name] = {key: record.get(key) for key in
                         ("status", "expected_size", "bytes_on_disk", "mtime_ns", "sha256")}
    return records, sha256_bytes(raw)


def check_on_disk_size(cache, pin):
    path = no_follow_path(cache, pin["rel"])
    if not os.path.lexists(path):
        fail_state(f"tile {pin['name']} missing at {path}")
    info = os.lstat(path)
    if not stat.S_ISREG(info.st_mode):
        fail_state(f"tile {pin['name']} at {path} is not a regular non-symlink file")
    if info.st_size != pin["size"]:
        fail_state(f"tile {pin['name']} size on disk {info.st_size}, pinned {pin['size']}")
    return path


def hash_tiles(cache, pins, state):
    records = []
    for pin in pins:
        path = check_on_disk_size(cache, pin)
        digest = sha256_file(path)
        expected = pin["sha256"] if pin["sha256"] is not None else state[pin["name"]]["sha256"]
        if digest != expected:
            fail_state(f"tile {pin['name']} sha256 {digest} does not match the expected {expected}")
        records.append({"name": pin["name"], "rel_path": pin["rel"], "size": pin["size"],
                        "sha256": digest, "path": path})
        log(f"tile sha256 verified: {pin['name']} ({pin['size']} bytes)")
    return records


def bounded_read_json(path, label):
    if os.path.getsize(path) > READ_BOUND_BYTES:
        fail_state(f"{label} at {path} exceeds the {READ_BOUND_BYTES}-byte read bound")
    with open(path, "rb") as handle:
        raw = handle.read()
    try:
        return json.loads(raw.decode("utf-8"), parse_constant=_reject_constant), raw
    except (UnicodeDecodeError, ValueError) as error:
        fail_state(f"{label} at {path} is not readable finite JSON: {error}")


def _reject_constant(name):
    raise ValueError(f"non-finite constant {name} is not allowed")


def footprint_from(header, label):
    edges = {}
    for key in ("x_min", "y_min", "x_max", "y_max"):
        value = header.get(key)
        if not isinstance(value, float) or not math.isfinite(value):
            fail_state(f"{label} {key} is non-finite or non-numeric: {value!r}")
        edges[key] = value
    if edges["x_max"] <= edges["x_min"] or edges["y_max"] <= edges["y_min"]:
        fail_state(f"{label} header extent is degenerate: {edges}")
    return edges


def validate_header_payload(payload, record, tile_path):
    if not isinstance(payload, dict) or payload.get("header_version") != 1:
        fail_state(f"header output for {record['name']} is not a version 1 object")
    if payload.get("tile_path") != tile_path or payload.get("tile_bytes") != record["size"]:
        fail_state(f"header output for {record['name']} does not echo the request tile and pinned size")
    header = payload.get("header")
    if not isinstance(header, dict):
        fail_state(f"header output for {record['name']} carries no header object")
    name = record["name"]
    if header.get("las_version") != EXPECTED_LAS_VERSION:
        fail_state(f"{name} las_version {header.get('las_version')!r}, expected {EXPECTED_LAS_VERSION}")
    if header.get("point_format_id") != EXPECTED_POINT_FORMAT:
        fail_state(f"{name} point_format_id {header.get('point_format_id')!r}, expected {EXPECTED_POINT_FORMAT}")
    count = header.get("point_count")
    if not isinstance(count, int) or isinstance(count, bool) or count <= 0:
        fail_state(f"{name} point_count {count!r} is not a positive integer")
    for axis in ("x", "y"):
        scale = header.get(f"{axis}_scale")
        if not isinstance(scale, float) or scale != EXPECTED_SCALE:
            fail_state(f"{name} {axis}_scale {scale!r}, expected exactly {EXPECTED_SCALE}")
    footprint = footprint_from(header, name)
    for axis, (low, high) in (("x", PLAUSIBLE_X_RANGE), ("y", PLAUSIBLE_Y_RANGE)):
        for edge in (f"{axis}_min", f"{axis}_max"):
            if not low <= footprint[edge] <= high:
                fail_state(f"{name} {edge} {footprint[edge]} is outside the L93 plausibility box "
                           f"[{low}, {high}] (reported, not clamped)")
    crs = header.get("crs")
    if crs is not None:
        if not isinstance(crs, dict):
            fail_state(f"{name} crs is not an object")
        if crs.get("epsg") not in (None, LAMBERT93_EPSG):
            fail_state(f"{name} crs epsg {crs.get('epsg')!r} is not Lambert-93 ({LAMBERT93_EPSG})")
    return {"name": name, "point_count": count, "las_version": header["las_version"],
            "point_format_id": header["point_format_id"], "crs": crs,
            "footprint_l93": footprint, "header": header}


def run_bounded(argv, output_path):
    """(returncode, stdout, stderr, problem): one attempt, killed and reaped on any problem."""
    try:
        proc = spawn_process(argv)
    except OSError as error:
        fail_guard(f"header worker failed to start ({argv[0]}): {error}")
    selector = selectors.DefaultSelector()
    selector.register(proc.stdout, selectors.EVENT_READ, "out")
    selector.register(proc.stderr, selectors.EVENT_READ, "err")
    buffers = {"out": bytearray(), "err": bytearray()}
    open_streams = 2
    problem = None
    deadline = time.monotonic() + CHILD_DEADLINE_SECONDS
    while open_streams and problem is None:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            problem = f"timeout after {CHILD_DEADLINE_SECONDS} s"
            break
        for key, _mask in selector.select(remaining):
            chunk = os.read(key.fileobj.fileno(), CHUNK_SIZE)
            if not chunk:
                selector.unregister(key.fileobj)
                open_streams -= 1
                continue
            target = buffers[key.data]
            target.extend(chunk)
            if len(target) > CHILD_CAP_BYTES:
                problem = f"{key.data} stream beyond the {CHILD_CAP_BYTES}-byte cap"
                break
        if problem is None and os.path.lexists(output_path):
            size = os.path.getsize(output_path)
            if size > CHILD_CAP_BYTES:
                problem = f"output file beyond the {CHILD_CAP_BYTES}-byte cap ({size} bytes)"
    if problem is not None:
        proc.kill()
    try:
        returncode = proc.wait(timeout=max(deadline - time.monotonic(), 1.0))
    except subprocess.TimeoutExpired:
        proc.kill()
        returncode = proc.wait()
    selector.close()
    proc.stdout.close()
    proc.stderr.close()
    return returncode, bytes(buffers["out"]), bytes(buffers["err"]), problem


def spawn_process(argv):
    """Process seam: one header child; tests replace this function to spy on spawns."""
    return subprocess.Popen(argv, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE)


def run_header_child(venv_python, worker_path, record, scratch):
    request_path = os.path.join(scratch, f"{record['name']}.request.json")
    output_path = os.path.join(scratch, f"{record['name']}.header.json")
    with open(request_path, "x", encoding="utf-8") as handle:
        json.dump({"tile_path": record["path"], "output_path": output_path}, handle, sort_keys=True)
    returncode, stdout, stderr, problem = run_bounded(
        [venv_python, worker_path, request_path], output_path)
    if problem is not None:
        fail_worker(f"header worker for {record['name']} stopped: {problem}")
    if returncode != 0:
        tail = stderr.decode("utf-8", "replace")[-400:]
        fail_worker(f"header worker for {record['name']} exited {returncode}: {tail}")
    if not os.path.isfile(output_path):
        fail_state(f"header worker for {record['name']} wrote no output file")
    payload, _raw = bounded_read_json(output_path, f"header output for {record['name']}")
    log(f"header read: {record['name']} ({stdout.decode('utf-8', 'replace').strip()})")
    return payload


def write_once(path, data):
    """Atomic create-only write: temp file, fsync, hard link (fails on existing target), fsync dir."""
    directory = os.path.dirname(path)
    temp = os.path.join(directory, f".{os.path.basename(path)}.tmp-{os.getpid()}")
    fd = os.open(temp, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o644)
    try:
        with os.fdopen(fd, "wb") as handle:
            handle.write(data)
            handle.flush()
            os.fsync(handle.fileno())
        try:
            os.link(temp, path)
        except FileExistsError:
            fail_state(f"refusing to overwrite existing {path}")
    finally:
        if os.path.lexists(temp):
            os.unlink(temp)
    dir_fd = os.open(directory, os.O_RDONLY)
    try:
        os.fsync(dir_fd)
    finally:
        os.close(dir_fd)


def serialize(obj):
    return (json.dumps(obj, sort_keys=True, indent=2, allow_nan=False) + "\n").encode("utf-8")


def check_existing(cache, records, state_sha):
    """None when no manifest exists; the verified manifest when it is bound to these inputs; exit 4 otherwise."""
    manifest_path = os.path.join(cache, FREEZE_DIR, MANIFEST_NAME)
    receipt_path = os.path.join(cache, FREEZE_DIR, RECEIPT_NAME)
    if not os.path.lexists(manifest_path):
        return None
    if os.path.islink(manifest_path) or not os.path.isfile(manifest_path):
        fail_state(f"existing manifest at {manifest_path} is not a regular non-symlink file")
    manifest, _raw = bounded_read_json(manifest_path, "existing manifest")
    if not isinstance(manifest, dict) or set(manifest) != {r["name"] for r in records}:
        fail_state("existing manifest does not carry exactly the six pinned tile names")
    for record in records:
        entry = manifest[record["name"]]
        if not isinstance(entry, dict) or entry.get("size") != record["size"] \
                or entry.get("sha256") != record["sha256"] or entry.get("rel_path") != record["rel_path"]:
            fail_state(f"existing manifest entry for {record['name']} differs from the current inputs; "
                       f"refusing to overwrite (exit 4)")
        footprint_from(entry.get("footprint_l93") or {}, f"existing manifest footprint {record['name']}")
    if not os.path.lexists(receipt_path) or os.path.islink(receipt_path):
        fail_state("existing manifest has no producer receipt: it is not bound to these inputs; "
                   "refusing to reuse or overwrite (exit 4)")
    receipt, _raw = bounded_read_json(receipt_path, "producer receipt")
    if receipt.get("transport_state", {}).get("sha256") != state_sha:
        fail_state("transport state changed since the manifest was produced (drift); refusing to reuse "
                   f"the manifest (receipt {receipt.get('transport_state', {}).get('sha256')} vs {state_sha})")
    if receipt.get("manifest", {}).get("sha256") != sha256_file(manifest_path):
        fail_state("manifest changed since the producer receipt was written; refusing to reuse (exit 4)")
    return manifest


def producer_marker_path(cache):
    return os.path.join(cache, FREEZE_DIR, MARKER_NAME)


def acquire_marker(cache):
    path = producer_marker_path(cache)
    body = serialize({"pid": os.getpid(), "started": now_iso()})
    try:
        fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o644)
    except FileExistsError:
        fail_state(f"producer marker present at {path}; a run may be active (use --recover-lock if stale)")
    with os.fdopen(fd, "wb") as handle:
        handle.write(body)


def release_marker(cache):
    path = producer_marker_path(cache)
    if os.path.lexists(path):
        os.unlink(path)


def recheck_transport_state(cache, pins, expected_sha):
    """Strict re-read of the transport state immediately before a publish step. Residual: the OS
    read-time vs rename race after this check is not closed; no platform locking is attempted."""
    try:
        _tiles, current_sha = read_transport_state(cache, pins)
    except Failure as failure:
        fail_state(f"transport state no longer verifies before promotion: {failure.message}")
    if current_sha != expected_sha:
        fail_state(f"transport state drift before promotion: raw sha256 {current_sha} vs verified "
                   f"{expected_sha}; nothing published")


def retain_promotion_drift(scratch, message):
    with open(os.path.join(scratch, "PROMOTION-DRIFT.txt"), "x", encoding="utf-8") as handle:
        handle.write(f"{now_iso()} {message}\n")
    log(f"producer diagnostics retained at {scratch}")


def prepare(cache, pins, venv_python, worker_path):
    validate_pin_table(pins)
    check_mount_rw(cache)
    check_cache_root(cache)
    check_freeze_ancestor(cache)
    state, state_sha = read_transport_state(cache, pins)
    for pin in pins:
        check_on_disk_size(cache, pin)
    records = hash_tiles(cache, pins, state)
    existing = check_existing(cache, records, state_sha)
    if existing is not None:
        log("integrity manifest already present and bound to these inputs: idempotent success, "
            "zero header children spawned")
        return {"status": "idempotent", "children": 0, "manifest": existing}
    if not os.path.isfile(venv_python) or not os.access(venv_python, os.X_OK):
        fail_guard(f"lidar venv python missing or not executable at {venv_python}; never installed from here")
    freeze = os.path.join(cache, FREEZE_DIR)
    if not os.path.lexists(freeze):
        os.mkdir(freeze)
    acquire_marker(cache)
    scratch = os.path.join(freeze, f"{SCRATCH_PREFIX}{os.getpid()}")
    keep_scratch = False
    try:
        os.mkdir(scratch)
        headers = []
        for record in records:
            payload = run_header_child(venv_python, worker_path, record, scratch)
            checked = validate_header_payload(payload, record, record["path"])
            headers.append(checked)
        manifest = {}
        for record, checked in zip(records, headers):
            manifest[record["name"]] = {"size": record["size"], "sha256": record["sha256"],
                                        "footprint_l93": checked["footprint_l93"],
                                        "rel_path": record["rel_path"]}
        manifest_bytes = serialize(manifest)
        manifest_path = os.path.join(freeze, MANIFEST_NAME)
        try:
            recheck_transport_state(cache, pins, state_sha)
        except Failure as failure:
            keep_scratch = True
            retain_promotion_drift(scratch, failure.message)
            raise
        write_once(manifest_path, manifest_bytes)
        receipt = {
            "receipt_version": 1,
            "generated_at": now_iso(),
            "producer": {"sha256": sha256_file(os.path.abspath(__file__)), "path": os.path.abspath(__file__)},
            "worker": {"sha256": sha256_file(worker_path), "path": worker_path},
            "venv_python": venv_python,
            "transport_state": {"path": os.path.join(cache, *STATE_REL.split("/")), "sha256": state_sha,
                                "tiles": state},
            "tiles": {record["name"]: {"rel_path": record["rel_path"], "size": record["size"],
                                       "sha256": record["sha256"], "footprint_l93": checked["footprint_l93"],
                                       "point_count": checked["point_count"],
                                       "las_version": checked["las_version"],
                                       "point_format_id": checked["point_format_id"],
                                       "crs": checked["crs"]}
                      for record, checked in zip(records, headers)},
            "manifest": {"path": manifest_path, "sha256": sha256_bytes(manifest_bytes),
                         "size": len(manifest_bytes)},
        }
        try:
            recheck_transport_state(cache, pins, state_sha)
        except Failure as failure:
            os.unlink(manifest_path)
            keep_scratch = True
            retain_promotion_drift(scratch, failure.message)
            raise
        try:
            write_once(os.path.join(freeze, RECEIPT_NAME), serialize(receipt))
        except BaseException:
            os.unlink(manifest_path)
            raise
        log(f"integrity manifest written: {manifest_path} (sha256 {receipt['manifest']['sha256']})")
        return {"status": "written", "children": len(records), "manifest": manifest}
    finally:
        if not keep_scratch:
            shutil.rmtree(scratch, ignore_errors=True)
        release_marker(cache)


def preflight(cache, pins, venv_python):
    validate_pin_table(pins)
    log(f"plan: integrity manifest for {len(pins)} pinned tiles under {cache}/{FREEZE_DIR}/")
    for pin in pins:
        log(f"pin: {pin['name']} size {pin['size']} at {pin['rel']}")
    mount_ok = check_mount_rw(cache, strict=False)
    log(f"mount rw: {'verified' if mount_ok else 'NOT verified (--prepare-integrity would refuse)'}")
    try:
        check_cache_root(cache)
        log(f"freeze ancestor: {check_freeze_ancestor(cache)}")
    except Failure as failure:
        log(f"cache: {failure.message}")
    try:
        state, state_sha = read_transport_state(cache, pins)
        log(f"transport state: verified structure, five stage-2 tiles complete (raw sha256 {state_sha})")
    except Failure as failure:
        log(f"transport state: NOT verified: {failure.message}")
    manifest_path = os.path.join(cache, FREEZE_DIR, MANIFEST_NAME)
    manifest_state = "present (sha256 checked only by --prepare-integrity)" \
        if os.path.lexists(manifest_path) else "absent"
    log(f"integrity manifest: {manifest_state}")
    log(f"venv python: {'present' if os.path.isfile(venv_python) else 'absent (--prepare-integrity would refuse)'}")
    log(f"next step: python3 scripts/geo/sedan_lidar_integrity.py --prepare-integrity --cache {cache}")
    return EXIT_OK


def recover_lock(cache):
    path = producer_marker_path(cache)
    if not os.path.lexists(path):
        log("no producer marker present: nothing to recover")
        return EXIT_OK
    marker, _raw = bounded_read_json(path, "producer marker")
    pid = marker.get("pid") if isinstance(marker, dict) else None
    if not isinstance(pid, int) or pid <= 0:
        fail_state(f"producer marker at {path} records no valid pid")
    try:
        os.kill(pid, 0)
        alive = True
    except ProcessLookupError:
        alive = False
    except OSError:
        alive = True
    if alive:
        fail_state(f"producer marker pid {pid} is still alive; not removing it")
    os.unlink(path)
    log(f"stale producer marker removed (pid {pid} not alive)")
    return EXIT_OK


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--cache", default=DEFAULT_CACHE_ROOT)
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument("--prepare-integrity", action="store_true")
    modes.add_argument("--recover-lock", action="store_true")
    args = parser.parse_args(argv)
    cache = os.path.abspath(args.cache)
    venv_python = os.path.join(cache, *VENV_PYTHON_REL.split("/"))
    try:
        if args.prepare_integrity:
            result = prepare(cache, PINS, venv_python, WORKER_PATH)
            log(f"done: {result['status']}, header children spawned: {result['children']}")
            return EXIT_OK
        if args.recover_lock:
            return recover_lock(cache)
        return preflight(cache, PINS, venv_python)
    except Failure as failure:
        log(f"FAILED (exit {failure.code}): {failure.message}")
        return failure.code


if __name__ == "__main__":
    sys.exit(main())
