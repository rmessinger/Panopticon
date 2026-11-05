#!/usr/bin/env python3
"""Stage-2 LiDAR acquisition controller: the five remaining LiDAR HD lot tiles.

Downloads, resumes and sha256-verifies exactly the five stage-2 tiles pinned in
sedan_lidar_probe.OTHER_TILES, after the stage-1 first tile (LHD_FXX_0840_6958,
128651625 bytes) passed the v1.2 protocol gates recorded in
probe-report-v1_2.json. Ownership is stage-2 TRANSPORT only: download, resume,
verification and multi-tile state. No extraction, no cloud reads, no scientific
selection (sedan_lidar_select is never invoked here), no budget raise, no
Core/schema/provider02 changes.

RELEASED UNCOMMITTED FOR INDEPENDENT E GUARD REVIEW; this file is not merged and
nothing in it has been run against real endpoints. A real fetch requires the E
review plus separate Architect authorization. The default invocation is a
READ-ONLY preflight that creates nothing and touches no network; any directory
creation or network access requires the explicit --fetch-remaining flag. The
no-follow checks, the tiles-directory inventory and the O_EXCL lock are in-process
checks, not an atomic OS-level race-proof pair; no race-proof claim is made.

Validated sources: the pin gate refuses to fetch unless the five accepted sources
(selection, probe controller, probe worker, and their test files) still hash to
the recorded f2466af9e pins in SOURCE_PINS.

One attempt per file per invocation: tiles are fetched strictly sequentially in
the committed pin order, each with one HEAD pre-check and at most one Range GET.
Any failure STOPS the run (no retry, no backoff, no fallback); the failed tile
keeps its verified partial and state, later tiles stay untouched, and the
operator re-invokes explicitly to resume.

Exit codes mirror sedan_acquire: 0 success; 2 guard or gate failure (including a
live or stale stage-2 lock); 3 download/network failure (resumable); 4 state or
drift failure (edition drift, corrupted state, operator collision, source-pin
drift).
"""

import argparse
import hashlib
import importlib.util
import json
import math
import os
import re
import sys
import urllib.error

try:
    import sedan_lidar_probe as probe
except ImportError:  # pragma: no cover - defensive path for unusual loaders
    _PROBE_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "sedan_lidar_probe.py")
    _probe_spec = importlib.util.spec_from_file_location("sedan_lidar_probe", _PROBE_PATH)
    probe = importlib.util.module_from_spec(_probe_spec)
    sys.modules["sedan_lidar_probe"] = probe
    _probe_spec.loader.exec_module(probe)

# Bind to the EXACT sedan_acquire instance the probe loaded, so Failure classes,
# exit codes and guard helpers are one shared set across probe and this module.
acquire = probe.acquire

Failure = acquire.Failure
EXIT_OK = acquire.EXIT_OK
EXIT_GUARD = acquire.EXIT_GUARD
EXIT_NETWORK = acquire.EXIT_NETWORK
EXIT_STATE = acquire.EXIT_STATE
NETWORK_ERRORS = acquire.NETWORK_ERRORS
CHUNK_SIZE = acquire.CHUNK_SIZE  # 1 MiB stream bound for every body read
CONTENT_RANGE_RE = acquire.CONTENT_RANGE_RE
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")


def log(message):
    acquire.log(message)


# The five stage-2 pins, verbatim from the probe: dict iteration order is the
# committed order and is the fetch order. Never mutated here.
PINS = probe.OTHER_TILES
LOT = probe.LOT

APPROVED_SIX_TOTAL = 830_532_133
FIRST_TILE_NAME = probe.FIRST_TILE.name
FIRST_TILE_SIZE = probe.FIRST_TILE.size
FIRST_TILE_SHA256 = "19465caf3085665f94fd648315896a84cbcf8fd2d9777eca85800c131979d212"
REMAINING_TOTAL = sum(PINS.values())
RESERVE = 2_147_483_648
OTHER_KNOWN_CONSUMER_BUDGET = 2_052_660_777 + 1_073_741_824
MIN_FREE_ABSOLUTE = 4_294_967_296

VALIDATED_COMMIT = "f2466af9e"
SOURCE_PINS = {
    "sedan_lidar_select.py": "4570c3507c6fb99442af0691f010a7406f64306622587abce140728873e913a1",
    "sedan_lidar_probe.py": "93c55be420124b00e8c5946a9cc09de57f0a9962d0faa77cd4859e16a88343da",
    "sedan_lidar_probe_worker.py": "5db920802e4a2da484aee02b598774bb40eb8e5152ed6328f5ff711fe0d3a6da",
    "tests/test_sedan_lidar_select.py": "fd0f1514cf751194b299081c7ea5dd178265239306a8115fa9d60caf5741e2e4",
    "tests/test_sedan_lidar_probe.py": "d9835a60a343db2f223319a7c3f822e360b04c8c34ebb7de50fab848f4a04ea3",
}
SOURCES_DIR = os.path.dirname(os.path.abspath(__file__))
REPORT_PATH = "/mnt/pit2/panopticon/geo-proof/sedan/lidarhd/state/probe-report-v1_2.json"
ARCHIVE_EVIDENCE = probe.DEFAULT_ARCHIVE_EVIDENCE
CACHE_ROOT = "/mnt/pit2/panopticon/geo-proof/sedan"

REPORT_MAX_BYTES = 4 * 1024 * 1024
STATE_VERSION = 1
STATE_MAX_BYTES = 64 * 1024
REQUIRED_TILE_KEYS = {"url", "expected_size", "bytes_on_disk", "status", "mtime_ns"}
GATING_GATES = ("a", "b", "c_v1_2", "d_v1_2", "e", "f")
# Tiles-dir inventory scan cap: pins + state-known names + hostile-input slack.
INVENTORY_SCAN_SLACK = 8


def _fail_closed_constants():
    """Guard-time pin arithmetic: a mismatch means the committed pins moved and
    every derived budget number would be wrong, so import fails closed."""
    if REMAINING_TOTAL != APPROVED_SIX_TOTAL - FIRST_TILE_SIZE:
        raise Failure(
            EXIT_STATE,
            f"stage-2 pin arithmetic drifted: remaining {REMAINING_TOTAL} != approved six-tile "
            f"total {APPROVED_SIX_TOTAL} - first tile {FIRST_TILE_SIZE}; refusing to run",
        )
    if len(PINS) != 5 or FIRST_TILE_NAME in PINS:
        raise Failure(
            EXIT_STATE,
            f"stage-2 pin set drifted: expected exactly the five OTHER_TILES pins without "
            f"{FIRST_TILE_NAME}; refusing to run",
        )
    for name, size in PINS.items():
        if isinstance(size, bool) or not isinstance(size, int) or size <= 0:
            raise Failure(EXIT_STATE, f"stage-2 pin {name} has a non-positive size {size!r}")


_fail_closed_constants()


def layout(cache_root):
    lidarhd = os.path.join(cache_root, "lidarhd")
    stage2 = os.path.join(lidarhd, "stage2")
    state_dir = os.path.join(stage2, "state")
    tiles_dir = os.path.join(stage2, LOT)
    return {
        "cache": cache_root,
        "stage2": stage2,
        "tiles_dir": tiles_dir,
        "state_dir": state_dir,
        "state": os.path.join(state_dir, "state.json"),
        "lock": os.path.join(state_dir, "lock"),
        "first_tile": os.path.join(lidarhd, LOT, FIRST_TILE_NAME),
        "tiles": {name: os.path.join(tiles_dir, name) for name in PINS},
    }


def stage2_tiles():
    """The five pinned tiles as probe.Tile records; probe.head_precheck is generic
    over this namedtuple, so the probe HEAD guard runs unchanged on stage-2 pins."""
    return {
        name: probe.Tile(
            name=name,
            url=probe.BASE_URL + name,
            size=size,
            rel_path=os.path.join("lidarhd", "stage2", LOT, name),
        )
        for name, size in PINS.items()
    }


def open_tile_url(url, method="GET", headers=None):
    """Module-level transport seam: delegates to the probe opener (pinned UA,
    same-host redirects only) so tests can inject a fake here or at
    probe.open_tile_url; probe.head_precheck keeps using the probe seam."""
    return probe.open_tile_url(url, method=method, headers=headers)


def _reject_json_constant(token):
    raise ValueError(f"non-finite JSON constant {token} is not accepted")


def _walk_nonfinite(node, path):
    if isinstance(node, float) and not math.isfinite(node):
        raise ValueError(f"non-finite number at {path}")
    if isinstance(node, dict):
        for key, value in node.items():
            _walk_nonfinite(value, f"{path}.{key}")
    elif isinstance(node, list):
        for index, value in enumerate(node):
            _walk_nonfinite(value, f"{path}[{index}]")


def check_report_gate(report_path):
    """Gate 2: the recorded v1.2 stage-1 report, read from the ACTUAL file with
    bounded, strict parsing. Per-gate checks are mandatory even when the report's
    own aggregate gates_pass is true (a faked self-echo must not pass); the
    historical c_historical record is ignored and never trusted as gating;
    numeric_proof_pass must be present and False and is never used as evidence."""
    try:
        info = os.stat(report_path)
    except OSError as error:
        acquire.fail_guard(f"v1.2 probe report missing or unreadable at {report_path}: {error}")
    if info.st_size > REPORT_MAX_BYTES:
        acquire.fail_guard(
            f"v1.2 probe report at {report_path} is {info.st_size} bytes, beyond the "
            f"{REPORT_MAX_BYTES}-byte parse bound"
        )
    try:
        with open(report_path, "r", encoding="utf-8") as handle:
            raw = handle.read(REPORT_MAX_BYTES + 1)
    except OSError as error:
        acquire.fail_guard(f"v1.2 probe report unreadable at {report_path}: {error}")
    if len(raw) > REPORT_MAX_BYTES:
        acquire.fail_guard(
            f"v1.2 probe report at {report_path} grew beyond the "
            f"{REPORT_MAX_BYTES}-byte parse bound while being read"
        )
    try:
        report = json.loads(raw, parse_constant=_reject_json_constant)
    except ValueError as error:
        acquire.fail_guard(f"v1.2 probe report at {report_path} is not strictly valid JSON: {error}")
    if not isinstance(report, dict):
        acquire.fail_guard(f"v1.2 probe report at {report_path} is not a JSON object")
    report_version = report.get("report_version")
    if isinstance(report_version, bool) or report_version != 2:
        acquire.fail_guard(
            f"v1.2 probe report at {report_path} has report_version {report_version!r}, not 2 "
            f"(historic v1.1 reports are rejected)"
        )
    if report.get("protocol_version") != "1.2":
        acquire.fail_guard(
            f"v1.2 probe report at {report_path} has protocol_version "
            f"{report.get('protocol_version')!r}, not '1.2'"
        )
    gates = report.get("gates")
    if not isinstance(gates, dict):
        acquire.fail_guard(f"v1.2 probe report at {report_path} has no gates object")
    for name in GATING_GATES:
        gate = gates.get(name)
        if not isinstance(gate, dict):
            acquire.fail_guard(f"v1.2 report gate {name} is missing at {report_path}")
        if gate.get("pass") is not True:
            acquire.fail_guard(
                f"v1.2 report gate {name} is not a pass (pass={gate.get('pass')!r}) at "
                f"{report_path}; per-gate checks are mandatory even when gates_pass is true"
            )
    check_availability(gates["d_v1_2"])
    if report.get("stage_1_preliminary") is not True:
        acquire.fail_guard(f"v1.2 report stage_1_preliminary is not true at {report_path}")
    if report.get("numeric_proof_pass") is not False:
        acquire.fail_guard(
            f"v1.2 report numeric_proof_pass must be present and false at {report_path}; it is "
            f"never used as pass evidence"
        )
    if report.get("tile") != FIRST_TILE_NAME:
        acquire.fail_guard(
            f"v1.2 report tile {report.get('tile')!r} is not the pinned first tile "
            f"{FIRST_TILE_NAME!r}"
        )
    tile_bytes = report.get("tile_bytes")
    if isinstance(tile_bytes, bool) or not isinstance(tile_bytes, int) or tile_bytes != FIRST_TILE_SIZE:
        acquire.fail_guard(
            f"v1.2 report tile_bytes {tile_bytes!r} is not the pinned {FIRST_TILE_SIZE}"
        )
    if report.get("tile_sha256") != FIRST_TILE_SHA256:
        acquire.fail_guard(
            f"v1.2 report tile_sha256 {report.get('tile_sha256')!r} is not the pinned first-tile "
            f"digest"
        )
    worker = report.get("worker") if isinstance(report.get("worker"), dict) else {}
    worker_script = worker.get("script") or report.get("worker_script")
    venv_python = worker.get("venv_python") or report.get("venv_python")
    for label, value in (("worker script", worker_script), ("venv python", venv_python)):
        if not isinstance(value, str) or not value.strip():
            acquire.fail_guard(f"v1.2 report records no {label} provenance at {report_path}")
    try:
        _walk_nonfinite(report, "report")
    except ValueError as error:
        acquire.fail_guard(f"v1.2 probe report at {report_path} rejected: {error}")
    log(
        f"v1.2 report gate ok: {report_path} (report_version 2, protocol 1.2, gates "
        f"{'/'.join(GATING_GATES)} all pass, availability 5-primaries/3-sectors shape validated, "
        f"stage_1_preliminary true, numeric_proof_pass false, c_historical ignored as non-gating)"
    )
    return report


def _check_availability_candidate(candidate, expected_role, label):
    """One availability candidate, schema-anchored to the recorded report's REAL
    field names: role, sector, stratum, rank, n2, sigma_fit, slope_deg, truth_z."""
    if not isinstance(candidate, dict):
        acquire.fail_guard(f"v1.2 report availability {label} is not an object")
    role = candidate.get("role")
    if role != expected_role:
        acquire.fail_guard(
            f"v1.2 report availability {label} has role {role!r}, expected {expected_role!r}"
        )
    sector = candidate.get("sector")
    if not isinstance(sector, str) or not sector.strip():
        acquire.fail_guard(f"v1.2 report availability {label} has no non-empty sector string")
    if not isinstance(candidate.get("stratum"), str):
        acquire.fail_guard(f"v1.2 report availability {label} has no stratum string")
    for key in ("rank", "n2"):
        value = candidate.get(key)
        if isinstance(value, bool) or not isinstance(value, int):
            acquire.fail_guard(f"v1.2 report availability {label} has non-integral {key} {value!r}")
    for key in ("sigma_fit", "slope_deg", "truth_z"):
        value = candidate.get(key)
        if isinstance(value, bool) or not isinstance(value, (int, float)):
            acquire.fail_guard(
                f"v1.2 report availability {label} is missing or has a non-numeric {key}"
            )
        if not math.isfinite(value):
            acquire.fail_guard(f"v1.2 report availability {label} has a non-finite {key}")
    return sector


def check_availability(gate_d):
    """Strict schema-anchored validation of the recorded d_v1_2 availability payload:
    at least 5 primaries across at least 3 distinct sectors, each carrying the
    recorded candidate shape; sectors_covered, when present, must equal the primaries'
    distinct sector set; recorded stage_1_preliminary exactly true and eligible_count
    an integer >= 5. Alternates are REPORTED ONLY, never final: the all-six final
    freeze is a separate future step this gate does not claim."""
    availability = gate_d.get("availability")
    if not isinstance(availability, dict):
        acquire.fail_guard(
            "v1.2 report gate d_v1_2 records no availability object; the recorded "
            "preliminary candidate availability is mandatory gating evidence"
        )
    primaries = availability.get("primaries")
    if not isinstance(primaries, list) or not primaries:
        acquire.fail_guard("v1.2 report availability primaries is absent or empty")
    if len(primaries) < 5:
        acquire.fail_guard(
            f"v1.2 report availability carries {len(primaries)} primaries, fewer than the "
            f"required 5"
        )
    sectors = []
    for index, candidate in enumerate(primaries):
        sector = _check_availability_candidate(candidate, "primary", f"primaries[{index}]")
        if sector not in sectors:
            sectors.append(sector)
    if len(sectors) < 3:
        acquire.fail_guard(
            f"v1.2 report availability primaries cover {len(sectors)} distinct sector(s), "
            f"fewer than the required 3"
        )
    sectors_covered = availability.get("sectors_covered")
    if sectors_covered is not None:
        strings_only = isinstance(sectors_covered, list) and all(
            isinstance(name, str) for name in sectors_covered
        )
        if not strings_only or sorted(sectors_covered) != sorted(sectors):
            acquire.fail_guard(
                f"v1.2 report availability sectors_covered {sectors_covered!r} disagrees with "
                f"the primaries' distinct sectors {sorted(sectors)}"
            )
    preliminary = availability.get("stage_1_preliminary")
    if preliminary is not True:
        acquire.fail_guard(
            f"v1.2 report availability stage_1_preliminary is not exactly true (got {preliminary!r})"
        )
    eligible_count = availability.get("eligible_count")
    if isinstance(eligible_count, bool) or not isinstance(eligible_count, int) or eligible_count < 5:
        acquire.fail_guard(
            f"v1.2 report availability eligible_count {eligible_count!r} is not an integer >= 5"
        )
    alternates = availability.get("alternates")
    if alternates is not None:
        if not isinstance(alternates, list):
            acquire.fail_guard("v1.2 report availability alternates is not a list")
        if len(alternates) > 2:
            acquire.fail_guard(
                f"v1.2 report availability carries {len(alternates)} alternates, beyond the "
                f"2-slot fallback ladder"
            )
        for index, candidate in enumerate(alternates):
            _check_availability_candidate(candidate, "alternate", f"alternates[{index}]")
    shortfall = availability.get("alternates_shortfall")
    if shortfall is not None and (isinstance(shortfall, bool) or not isinstance(shortfall, int)):
        acquire.fail_guard(
            f"v1.2 report availability alternates_shortfall {shortfall!r} is not an integer"
        )
    return availability


def file_sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        while True:
            block = handle.read(CHUNK_SIZE)
            if not block:
                break
            digest.update(block)
    return digest.hexdigest()


def check_source_pins(sources_dir):
    """Gate 3: the five accepted sources must still hash to their f2466af9e pins;
    any drift exits 4 before anything is fetched (pin drift is a state failure of
    the provenance record, not a transient guard)."""
    for rel, expected in SOURCE_PINS.items():
        path = os.path.join(sources_dir, rel)
        if not os.path.isfile(path):
            raise Failure(
                EXIT_STATE,
                f"accepted source missing at {path} (validated commit {VALIDATED_COMMIT}); "
                f"nothing fetched",
            )
        actual = file_sha256(path)
        if actual != expected:
            raise Failure(
                EXIT_STATE,
                f"accepted source drifted at {path}: sha256 {actual} != pinned {expected} "
                f"(validated commit {VALIDATED_COMMIT}); nothing fetched",
            )
    log(f"source pins ok: {len(SOURCE_PINS)} accepted sources match commit {VALIDATED_COMMIT}")


def check_budget(ancestor, owned_completed_bytes):
    """Gate 4: free space must cover the remaining pins plus this tool's reserve
    plus the other known on-prefix consumer, with an absolute floor. The fetch
    path calls this before the state load, so owned bytes are conservatively 0
    there (any owned complete bytes would only lower the requirement)."""
    usage = acquire.statvfs(ancestor)
    free = usage.f_bavail * usage.f_frsize
    required = max(
        MIN_FREE_ABSOLUTE,
        REMAINING_TOTAL - owned_completed_bytes + RESERVE + OTHER_KNOWN_CONSUMER_BUDGET,
    )
    log(
        f"stage-2 free space: free={free} required={required} (remaining pins {REMAINING_TOTAL} "
        f"- owned complete {owned_completed_bytes} + reserve {RESERVE} + other known consumer "
        f"budget {OTHER_KNOWN_CONSUMER_BUDGET}, floor {MIN_FREE_ABSOLUTE})"
    )
    if free < required:
        acquire.fail_guard(
            f"insufficient free space on {ancestor}: free={free} required={required} "
            f"remaining={required - free}"
        )
    return free, required


def reject_symlinks(path, label):
    try:
        probe._reject_symlink_components(path, label)
    except probe.EvidenceProblem as error:
        acquire.fail_guard(str(error))


def load_stage2_state(state_path):
    """Stage-2 multi-tile state; bound at STATE_MAX_BYTES and fail-closed: any
    defect exits 4 and nothing is adopted."""
    if not os.path.exists(state_path):
        return {"version": STATE_VERSION, "tiles": {}}
    try:
        with open(state_path, "r", encoding="utf-8") as handle:
            raw = handle.read(STATE_MAX_BYTES + 1)
    except OSError as error:
        raise Failure(EXIT_STATE, f"unreadable stage-2 state at {state_path}: {error}; nothing adopted")
    if len(raw) > STATE_MAX_BYTES:
        raise Failure(
            EXIT_STATE,
            f"stage-2 state at {state_path} exceeds the {STATE_MAX_BYTES}-byte bound; nothing adopted",
        )
    try:
        state = json.loads(raw)
    except ValueError as error:
        raise Failure(EXIT_STATE, f"corrupted stage-2 state at {state_path}: {error}; nothing adopted")
    if not isinstance(state, dict) or state.get("version") != STATE_VERSION:
        raise Failure(
            EXIT_STATE,
            f"stage-2 state at {state_path} has an unsupported or missing version; nothing adopted",
        )
    tiles = state.get("tiles")
    if not isinstance(tiles, dict):
        raise Failure(EXIT_STATE, f"corrupted stage-2 state at {state_path}: tiles is not an object")
    known = stage2_tiles()
    for name, entry in tiles.items():
        if name not in known:
            raise Failure(EXIT_STATE, f"corrupted stage-2 state at {state_path}: unknown tile {name!r}")
        tile = known[name]
        if not isinstance(entry, dict) or not REQUIRED_TILE_KEYS.issubset(entry):
            raise Failure(EXIT_STATE, f"corrupted stage-2 state at {state_path}: malformed tile {name!r}")
        if entry["status"] not in ("partial", "complete"):
            raise Failure(EXIT_STATE, f"corrupted stage-2 state at {state_path}: bad status in {name!r}")
        for key in ("expected_size", "bytes_on_disk", "mtime_ns"):
            value = entry[key]
            if isinstance(value, bool) or not isinstance(value, int):
                raise Failure(EXIT_STATE, f"corrupted stage-2 state at {state_path}: bad {key} in {name!r}")
        if entry["url"] != tile.url or entry["expected_size"] != tile.size:
            raise Failure(
                EXIT_STATE,
                f"edition drift in stage-2 state for {name}: state records url={entry['url']} "
                f"expected={entry['expected_size']} but pinning says url={tile.url} "
                f"expected={tile.size}; nothing adopted",
            )
        if entry["bytes_on_disk"] > tile.size:
            raise Failure(
                EXIT_STATE,
                f"corrupted stage-2 state for {name}: bytes_on_disk {entry['bytes_on_disk']} "
                f"exceeds pinned {tile.size}; nothing adopted",
            )
        if entry["status"] == "complete":
            sha = entry.get("sha256")
            if not isinstance(sha, str) or not SHA256_RE.match(sha):
                raise Failure(
                    EXIT_STATE,
                    f"corrupted stage-2 state at {state_path}: completed tile {name!r} lacks a "
                    f"sha256 record",
                )
    return state


def write_state(state_path, state):
    acquire.write_state(state_path, state)  # atomic tmp + fsync + replace
    size = os.stat(state_path).st_size
    if size > STATE_MAX_BYTES:
        raise Failure(
            EXIT_STATE,
            f"stage-2 state at {state_path} grew to {size} bytes beyond the "
            f"{STATE_MAX_BYTES}-byte bound; refusing to continue",
        )


def mark_partial(state, state_path, tile_file, name, tile, bytes_on_disk):
    entry = state["tiles"].setdefault(name, {})
    entry.update(
        {
            "url": tile.url,
            "expected_size": tile.size,
            "bytes_on_disk": bytes_on_disk,
            "status": "partial",
            "mtime_ns": os.lstat(tile_file).st_mtime_ns if os.path.exists(tile_file) else 0,
        }
    )
    entry.pop("sha256", None)
    write_state(state_path, state)


def mark_complete(state, state_path, tile_file, name, tile, digest):
    entry = state["tiles"].setdefault(name, {})
    entry.update(
        {
            "url": tile.url,
            "expected_size": tile.size,
            "bytes_on_disk": tile.size,
            "status": "complete",
            "mtime_ns": os.lstat(tile_file).st_mtime_ns,
            "sha256": digest,
        }
    )
    write_state(state_path, state)


def inventory_tiles_dir(paths, state):
    """Tiles-directory inventory: any entry that is not one of the five pins or an
    OWN state-known partial tile name fails closed (exit 4, untouched) before the
    lock and before any network. The scandir iterator is consumed lazily and the
    scan is capped at pins + state-known names + slack; an untrusted listing is
    never materialized as a whole and the first unexpected entry refuses."""
    tiles_dir = paths["tiles_dir"]
    if os.path.islink(tiles_dir):
        raise Failure(EXIT_STATE, f"stage-2 tiles dir is a symlink; refusing: {tiles_dir}")
    if not os.path.isdir(tiles_dir):
        if os.path.lexists(tiles_dir):
            raise Failure(EXIT_STATE, f"stage-2 tiles dir is not a directory; refusing: {tiles_dir}")
        log(f"tiles dir inventory ok: tiles dir absent, fresh start ({tiles_dir})")
        return
    allowed = set(PINS)
    for name, entry in state["tiles"].items():
        if isinstance(entry, dict) and entry.get("status") == "partial":
            allowed.add(name)
    scan_cap = len(PINS) + len(state["tiles"]) + INVENTORY_SCAN_SLACK
    scanned = 0
    with os.scandir(tiles_dir) as entries:
        for entry in entries:
            scanned += 1
            if scanned > scan_cap:
                raise Failure(
                    EXIT_STATE,
                    f"stage-2 tiles dir inventory exceeded the {scan_cap}-entry scan bound; "
                    f"refusing, untouched: {tiles_dir}",
                )
            if entry.name not in allowed:
                raise Failure(
                    EXIT_STATE,
                    f"operator collision: unexpected entry in the stage-2 tiles dir; refusing, "
                    f"nothing adopted, deleted or modified: {entry.name!r} in {tiles_dir}",
                )
            if entry.is_symlink() or not entry.is_file(follow_symlinks=False):
                raise Failure(
                    EXIT_STATE,
                    f"operator collision: stage-2 tiles dir entry is not a regular file; "
                    f"refusing, untouched: {entry.name!r} in {tiles_dir}",
                )
    log(f"tiles dir inventory ok: {scanned} scanned, all pinned names or state-known partials")


def scan_ownership(paths, tiles, state):
    """Read-only pass; returns {name: adoptable bytes on disk}. A tile file is
    resumable only with a matching owned state entry; an existing file without
    state, or beyond the pin, is an operator collision: exit 4, untouched."""
    adoption = {}
    tiles_state = state["tiles"]
    for name, tile in tiles.items():
        tile_file = paths["tiles"][name]
        entry = tiles_state.get(name)
        if not os.path.lexists(tile_file):
            if entry is not None and entry["status"] == "complete":
                raise Failure(
                    EXIT_STATE, f"stage-2 state marks {name} complete but the file is missing: {tile_file}"
                )
            adoption[name] = 0
            continue
        if entry is None:
            raise Failure(
                EXIT_STATE,
                f"operator collision: tile file exists without owned stage-2 state; left "
                f"untouched: {tile_file}",
            )
        if os.path.islink(tile_file) or not os.path.isfile(tile_file):
            raise Failure(EXIT_STATE, f"operator collision: {tile_file} is not a regular file; left untouched")
        info = os.lstat(tile_file)
        if info.st_size > tile.size:
            raise Failure(
                EXIT_STATE,
                f"operator collision: {tile_file} is {info.st_size} bytes, beyond the pinned "
                f"{tile.size}; left untouched",
            )
        if entry["status"] == "complete":
            if info.st_size != tile.size:
                raise Failure(
                    EXIT_STATE, f"stage-2 state marks {name} complete but {tile_file} is {info.st_size} bytes"
                )
            adoption[name] = info.st_size
            continue
        recorded = entry["bytes_on_disk"]
        if info.st_size < recorded:
            raise Failure(
                EXIT_STATE,
                f"operator collision: {tile_file} shrank below the recorded {recorded} bytes; "
                f"left untouched",
            )
        if info.st_size == recorded and entry["mtime_ns"] != info.st_mtime_ns:
            raise Failure(
                EXIT_STATE,
                f"operator collision: {tile_file} was modified after the recorded state (mtime "
                f"mismatch); left untouched",
            )
        adoption[name] = info.st_size
    return adoption


def verify_complete_tile(tile_file, name, tile, entry):
    """Local length + sha256 verification of a completed tile; no network, no
    redownload, repairs nothing."""
    if not os.path.isfile(tile_file) or os.path.islink(tile_file):
        raise Failure(EXIT_STATE, f"completed tile file is missing or not a regular file: {tile_file}")
    info = os.lstat(tile_file)
    if info.st_size != tile.size:
        raise Failure(
            EXIT_STATE,
            f"completed tile failed length verification: {tile_file} is {info.st_size} bytes, "
            f"pinned {tile.size}",
        )
    digest = file_sha256(tile_file)
    if digest != entry.get("sha256"):
        raise Failure(EXIT_STATE, f"completed tile failed sha256 verification; repairing nothing: {tile_file}")
    return digest


def download_tile(state, state_path, tile_file, name, tile, resume_from):
    """ONE Range-resume GET; exactly one attempt, no retries.

    206 must carry a parseable Content-Range whose start equals the resume offset
    and whose total equals the pin; a 200 on resume refuses to append a full body
    (exit 3); an overlong body is drift (exit 4) and the file stays at the cap; a
    short read or mid-stream network death is exit 3 with the partial and its
    state preserved. The running sha256 is seeded from a FRESH re-hash of the
    on-disk partial at resume time, never from the stored digest.
    """
    headers = {"Range": f"bytes={resume_from}-"} if resume_from else {}
    try:
        response = open_tile_url(tile.url, method="GET", headers=headers)
    except urllib.error.HTTPError as error:
        raise acquire.classify_http_error(tile.url, error)
    except NETWORK_ERRORS as error:
        raise Failure(EXIT_NETWORK, f"request failed for {tile.url}: {error}")
    cap = tile.size - resume_from
    received = 0
    failure = None
    digest = None
    try:
        with response:
            if response.status in (401, 403):
                failure = Failure(EXIT_NETWORK, acquire.no_account_message(tile.url, response.status))
            elif response.status == 206:
                match = CONTENT_RANGE_RE.match(response.headers.get("Content-Range") or "")
                if match is None:
                    failure = Failure(EXIT_NETWORK, f"206 without parseable Content-Range for {tile.url}")
                elif int(match.group(3)) != tile.size:
                    failure = Failure(
                        EXIT_STATE,
                        probe.drift_message(
                            tile, f"Content-Range total {match.group(3)} disagrees with the pinned size"
                        ),
                    )
                elif int(match.group(1)) != resume_from:
                    failure = Failure(
                        EXIT_NETWORK,
                        f"server resumed at byte {match.group(1)} instead of {resume_from} for {tile.url}",
                    )
            elif response.status == 200 and resume_from:
                failure = Failure(
                    EXIT_NETWORK,
                    f"server ignored Range for {tile.url}; refusing to append a full body "
                    f"(rerun to resume)",
                )
            elif response.status != 200:
                failure = Failure(EXIT_NETWORK, f"unexpected HTTP {response.status} for {tile.url}")
            if failure is None:
                hasher = acquire.seed_hasher(tile_file) if resume_from else hashlib.sha256()
                os.makedirs(os.path.dirname(tile_file), exist_ok=True)
                sink = open(tile_file, "ab")
                try:
                    while True:
                        want = min(CHUNK_SIZE, cap - received)
                        if want == 0:
                            if response.read(1):
                                failure = Failure(
                                    EXIT_STATE,
                                    f"overlong body beyond the pinned {tile.size} bytes (drift); "
                                    f"download stopped at the cap for {tile.url}",
                                )
                            break
                        chunk = response.read(want)
                        if not chunk:
                            break
                        sink.write(chunk)
                        hasher.update(chunk)
                        received += len(chunk)
                    if failure is None and received < cap:
                        failure = Failure(
                            EXIT_NETWORK,
                            f"short read for {tile.url}: got {received} of {cap} bytes; owned "
                            f"partial preserved; rerun to resume",
                        )
                    if failure is None:
                        sink.flush()
                        os.fsync(sink.fileno())
                        digest = hasher.hexdigest()
                except NETWORK_ERRORS as error:
                    failure = Failure(
                        EXIT_NETWORK,
                        f"network error during download of {tile.url}: {error}; owned partial "
                        f"preserved; rerun to resume",
                    )
                finally:
                    sink.close()
    except NETWORK_ERRORS as error:
        failure = Failure(
            EXIT_NETWORK,
            f"network error during download of {tile.url}: {error}; owned partial preserved; "
            f"rerun to resume",
        )
    finally:
        if failure is not None:
            mark_partial(state, state_path, tile_file, name, tile, resume_from + received)
        elif digest is not None:
            mark_complete(state, state_path, tile_file, name, tile, digest)
    if failure is not None:
        raise failure
    return digest


def acquire_stage2_lock(lock_path):
    """O_EXCL lock with a live-pid check. There is NO automatic recovery and no
    --recover-lock: a stale lock exits 2 with a manual-removal message."""
    if os.path.exists(lock_path):
        pid, recorded = acquire.read_lock(lock_path)
        if acquire.pid_alive(pid):
            raise Failure(
                EXIT_GUARD,
                f"stage-2 lock at {lock_path} is held by live pid {pid} (recorded {recorded}); "
                f"another invocation may be running",
            )
        raise Failure(
            EXIT_GUARD,
            f"stale stage-2 lock at {lock_path} (dead pid {pid}, recorded {recorded}); this tool "
            f"never removes locks automatically; after confirming no stage-2 run is active, "
            f"remove it manually",
        )
    os.makedirs(os.path.dirname(lock_path), exist_ok=True)
    descriptor = os.open(lock_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o644)
    with os.fdopen(descriptor, "w", encoding="utf-8") as handle:
        handle.write(f"{os.getpid()} {acquire.now_iso()}\n")
    log(f"lock acquired: {lock_path} (pid {os.getpid()})")


def release_stage2_lock(lock_path):
    if os.path.exists(lock_path):
        os.unlink(lock_path)
        log(f"lock released: {lock_path}")


def print_hash_summary(paths):
    """Post-fetch metadata hash summary: per-tile local sha256 plus the six-tile
    denominator cross-check including the first tile at its known path (read-only
    length+hash recheck when present). Local completion hashes only; NO publisher
    hash is claimed."""
    state = load_stage2_state(paths["state"])
    tiles = stage2_tiles()
    total = 0
    log("stage-2 metadata hash summary (local completion hashes, not publisher-authenticated):")
    for name, tile in tiles.items():
        entry = state["tiles"].get(name)
        if entry is None or entry["status"] != "complete":
            raise Failure(EXIT_STATE, f"summary refused: {name} is not recorded complete")
        tile_file = paths["tiles"][name]
        digest = verify_complete_tile(tile_file, name, tile, entry)
        total += tile.size
        log(f"  {name}: {tile.size} bytes sha256 {digest}")
    first_tile = paths["first_tile"]
    if os.path.isfile(first_tile) and not os.path.islink(first_tile):
        info = os.lstat(first_tile)
        if info.st_size != FIRST_TILE_SIZE:
            raise Failure(
                EXIT_STATE,
                f"first tile drifted at {first_tile}: {info.st_size} bytes, pinned {FIRST_TILE_SIZE}",
            )
        first_digest = file_sha256(first_tile)
        if first_digest != FIRST_TILE_SHA256:
            raise Failure(
                EXIT_STATE, f"first tile failed sha256 verification; repairing nothing: {first_tile}"
            )
        log(
            f"  {FIRST_TILE_NAME}: {FIRST_TILE_SIZE} bytes sha256 {first_digest} (read-only recheck "
            f"of the completed stage-1 tile)"
        )
    else:
        log(
            f"  {FIRST_TILE_NAME}: not present locally at {first_tile}; its pinned "
            f"{FIRST_TILE_SIZE} bytes recorded in the v1.2 report are included unverified"
        )
    total += FIRST_TILE_SIZE
    if total != APPROVED_SIX_TOTAL:
        raise Failure(
            EXIT_STATE,
            f"six-tile denominator cross-check failed: {total} != pinned {APPROVED_SIX_TOTAL}",
        )
    log(f"  six-tile total: {total} bytes == pinned {APPROVED_SIX_TOTAL}; no publisher hash is claimed")


def run_fetch_remaining(args):
    """--fetch-remaining only. Fixed guard order, all hard, all before ANY mkdir
    or network: evidence -> report gate -> source pins -> mount realness -> safe
    ancestors/no-follow -> budget -> own state load -> collision scan -> lock."""
    if not getattr(args, "fetch_remaining", False):
        acquire.fail_guard(
            "--fetch-remaining is required before any directory creation or network access; "
            "nothing was created and no network was touched"
        )
    cache_root = acquire.check_path_safety(args.cache_root)
    paths = layout(cache_root)
    log(f"stage-2 fetch target: {len(PINS)} pinned tiles, {REMAINING_TOTAL} bytes, cache root {cache_root}")
    if FIRST_TILE_NAME in PINS:
        acquire.fail_guard(
            f"{FIRST_TILE_NAME} is the completed stage-1 first tile and is never fetched by stage 2"
        )
    probe.require_archive_evidence(args.archive_evidence, hard=True)
    check_report_gate(args.report)
    check_source_pins(args.sources_dir)
    probe.check_mount_realness_or_refuse(paths["tiles_dir"])
    reject_symlinks(paths["stage2"], "stage-2 root")
    reject_symlinks(paths["tiles_dir"], "stage-2 tiles dir")
    reject_symlinks(paths["state_dir"], "stage-2 state dir")
    reject_symlinks(paths["state"], "stage-2 state file")
    for tile_file in paths["tiles"].values():
        reject_symlinks(tile_file, "stage-2 tile file")
    ancestor, _device = acquire.check_ancestor(paths["tiles_dir"])
    check_budget(ancestor, 0)
    state = load_stage2_state(paths["state"])
    inventory_tiles_dir(paths, state)
    tiles = stage2_tiles()
    adoption = scan_ownership(paths, tiles, state)
    acquire_stage2_lock(paths["lock"])
    try:
        for name, tile in tiles.items():
            tile_file = paths["tiles"][name]
            entry = state["tiles"].get(name)
            if entry is not None and entry["status"] == "complete":
                digest = verify_complete_tile(tile_file, name, tile, entry)
                log(
                    f"completed owned tile verified; no network used and no redownload: {name} "
                    f"({tile.size} bytes, sha256 {digest})"
                )
                continue
            probe.head_precheck(tile)
            resume_from = adoption[name]
            log(f"downloading: {name} from byte {resume_from} of {tile.size} (single attempt)")
            download_tile(state, paths["state"], tile_file, name, tile, resume_from)
        print_hash_summary(paths)
    finally:
        release_stage2_lock(paths["lock"])


def run_preflight(args):
    """Default read-only mode: gate validation, budget arithmetic, plan print.
    Creates nothing and touches no network."""
    cache_root = acquire.check_path_safety(args.cache_root)
    paths = layout(cache_root)
    log(f"stage-2 preflight (read-only): tiles dir {paths['tiles_dir']}")
    probe.require_archive_evidence(args.archive_evidence, hard=True)
    check_report_gate(args.report)
    check_source_pins(args.sources_dir)
    verified = acquire.check_mount_realness(paths["tiles_dir"])
    if not verified:
        log(
            "mount realness NOT verified: preflight continues read-only, but fetch would refuse "
            "to write on a possibly-unmounted path"
        )
    ancestor, _device = acquire.check_ancestor(paths["tiles_dir"])
    check_budget(ancestor, 0)
    state = load_stage2_state(paths["state"])
    completed = [name for name, entry in state["tiles"].items() if entry["status"] == "complete"]
    owned = sum(PINS[name] for name in completed if name in PINS)
    log(
        f"state: {len(completed)} complete, {len(PINS) - len(completed)} outstanding, {owned} "
        f"owned bytes on disk (budget above conservatively assumed 0 owned)"
    )
    log(f"planned tiles dir: {paths['tiles_dir']}")
    log(f"planned state file: {paths['state']}")
    log(f"planned lock file: {paths['lock']}")
    for name, size in PINS.items():
        log(f"pinned stage-2 tile: {name} ({size} bytes) at {probe.BASE_URL + name}")
    log(f"pinned remaining total: {REMAINING_TOTAL} bytes; approved six-tile total {APPROVED_SIX_TOTAL}")
    log(
        f"refused explicitly: {FIRST_TILE_NAME} is the completed stage-1 first tile and is never "
        f"fetched by stage 2"
    )
    log(f"validated sources commit: {VALIDATED_COMMIT} ({len(SOURCE_PINS)} pinned files)")
    log("fetch requires the explicit --fetch-remaining flag; one attempt per file, stop at first failure")
    log("preflight complete; nothing was written and no directories were created")


def run_status(args):
    """Read-only state, lock and provenance report; writes nothing."""
    cache_root = acquire.check_path_safety(args.cache_root)
    paths = layout(cache_root)
    state = load_stage2_state(paths["state"])
    log(f"stage-2 state file: {paths['state']}")
    for name in PINS:
        entry = state["tiles"].get(name)
        if entry is None:
            log(f"  {name}: no owned entry")
        else:
            sha = entry.get("sha256", "none")
            log(
                f"  {name}: status={entry['status']} "
                f"bytes_on_disk={entry['bytes_on_disk']}/{entry['expected_size']} sha256={sha}"
            )
    if os.path.exists(paths["lock"]):
        pid, recorded = acquire.read_lock(paths["lock"])
        log(f"lock: present at {paths['lock']} (pid {pid}, recorded {recorded}, alive={acquire.pid_alive(pid)})")
    else:
        log(f"lock: absent (planned {paths['lock']})")
    log(f"report gate source: {args.report}")
    log(f"archive evidence: {args.archive_evidence}")
    log(f"source pins dir: {args.sources_dir} (validated commit {VALIDATED_COMMIT})")
    log("status complete; nothing was written and no directories were created")


def build_parser():
    parser = argparse.ArgumentParser(
        prog="sedan_lidar_acquire.py",
        description=(
            "Stage-2 LiDAR acquisition controller: fetches, resumes and sha256-verifies the five "
            "remaining pinned LiDAR HD lot tiles after the v1.2 stage-1 gates. Transport only: no "
            "extraction, no cloud reads, no scientific selection. One attempt per file per "
            "invocation, stop at first failure."
        ),
        epilog=(
            "Modes: default = read-only preflight (gate validation, budget arithmetic, plan "
            "print; never writes, never touches the network); --fetch-remaining = the explicit "
            "fetch flag required before any directory creation or network access; --status = "
            "read-only state and lock report. Exit codes: 0 success; 2 guard or gate failure "
            "(including a live or stale stage-2 lock); 3 download/network failure (resumable); "
            "4 state or drift failure."
        ),
    )
    parser.add_argument(
        "--fetch-remaining",
        action="store_true",
        help="explicit flag required before any mkdir or network; sequential one-attempt fetch",
    )
    parser.add_argument("--status", action="store_true", help="read-only state and lock report")
    parser.add_argument(
        "--archive-evidence",
        default=ARCHIVE_EVIDENCE,
        help=f"acquisition completion manifest (default {ARCHIVE_EVIDENCE})",
    )
    parser.add_argument(
        "--report",
        default=REPORT_PATH,
        help=f"v1.2 stage-1 probe report (default {REPORT_PATH})",
    )
    parser.add_argument(
        "--sources-dir",
        default=SOURCES_DIR,
        help=f"directory holding the five pinned accepted sources (default {SOURCES_DIR})",
    )
    parser.add_argument(
        "--cache-root",
        default=CACHE_ROOT,
        help=f"stage-2 cache root (default {CACHE_ROOT})",
    )
    return parser


def main(argv=None):
    parser = build_parser()
    args = parser.parse_args(argv)
    if args.fetch_remaining and args.status:
        parser.error("--fetch-remaining cannot be combined with --status")
    if args.fetch_remaining:
        mode = "fetch-remaining"
    elif args.status:
        mode = "status"
    else:
        mode = "preflight"
    code = EXIT_OK
    try:
        if args.fetch_remaining:
            run_fetch_remaining(args)
        elif args.status:
            run_status(args)
        else:
            run_preflight(args)
    except Failure as failure:
        log(f"FAIL({failure.code}): {failure.message}")
        code = failure.code
    log(f"final status: exit={code} mode={mode} cache_root={args.cache_root}")
    return code


if __name__ == "__main__":
    sys.exit(main())
