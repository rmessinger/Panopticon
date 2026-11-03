#!/usr/bin/env python3
"""Stage-1 LiDAR first-tile acquisition and data probe (protocol v1.2 stage-1 gates).

Stage 1 of the Sedan ground-truth protocol, sections 3, 4 and 5 of the plan record,
as amended by the user-approved protocol v1.2 package of 2026-11-03 (gate c population
aligned to the expected-intersection frame; gate d the preliminary ground-availability
floor under the complete unchanged section 6 method; canonical doc:
docs/roadmap/deep-dives/r2-sedan-ground-truth.md sections 4-6). The v1.1 gate
evaluators remain in this file as HISTORICAL evaluators and the recorded v1.1
execution result stands; the v1.2 report is written to probe-report-v1_2.json so the
recorded v1.1 probe-report.json stays IMMUTABLE.
RELEASED FOR REVIEW ONLY: fetching and tool installation require the reviewed
execution brief; nothing in this file downloads or installs on its own. The
default invocation (no subcommand) is a READ-ONLY preflight that writes nothing
and touches no network.

Scope is the single pinned first tile LHD_FXX_0840_6958_PTS_LAMB93_IGN69.copc.laz
(128651625 bytes). The other five lot tiles are NEVER downloadable by this tool:
a stage-2 attempt fails with a guard message because stage 2 needs its own
reviewed brief. One data-transfer attempt per explicit invocation, no retry
loops; failures STOP and report. Python 3 standard library only: this controller
never imports laspy, lazrs or numpy. The data probe (subcommand `probe`) invokes
the worker sedan_lidar_probe_worker.py as a SUBPROCESS under the isolated tool
venv python; it is never imported. `plan-tools` prints the isolated venv recipe
and executes nothing.

State and lock live under the probe's own lidarhd/state/ directory; the fetch
path never writes or mutates the acquire unit's state or archives, and its
archive-evidence guard only READS the acquire completion manifest and, when
present, the acquire owned state (read-only, bounded, symlink-refused).

Exit codes mirror sedan_acquire: 0 success; 2 guard or gate failure; 3
download/network failure (resumable); 4 state or drift failure.
"""

import argparse
import datetime
import hashlib
import http.client
import json
import math
import os
import re
import statistics
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
import importlib.util
from typing import NamedTuple

_ACQUIRE_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "sedan_acquire.py")
_acquire_spec = importlib.util.spec_from_file_location("sedan_acquire", _ACQUIRE_PATH)
acquire = importlib.util.module_from_spec(_acquire_spec)
_acquire_spec.loader.exec_module(acquire)

Failure = acquire.Failure
EXIT_OK = acquire.EXIT_OK
EXIT_GUARD = acquire.EXIT_GUARD
EXIT_NETWORK = acquire.EXIT_NETWORK
EXIT_STATE = acquire.EXIT_STATE
NETWORK_ERRORS = acquire.NETWORK_ERRORS

# Canonical stage-1 selection module (protocol v1.2): stdlib-only sibling; the
# controller applies E1-E7, strata, ranking, ladder, spacing and the 5-primary/
# 3-sector floor through it and never re-implements the method. The plain import
# keeps one shared module instance; the file-located fallback covers loads without
# the script directory on sys.path.
try:
    import sedan_lidar_select as groundtruth
except ImportError:  # pragma: no cover - defensive path for unusual loaders
    _SELECT_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "sedan_lidar_select.py")
    _select_spec = importlib.util.spec_from_file_location("sedan_lidar_select", _SELECT_PATH)
    groundtruth = importlib.util.module_from_spec(_select_spec)
    # Registered before exec: dataclass processing resolves the module through
    # sys.modules (Python 3.12+ behaviour).
    sys.modules["sedan_lidar_select"] = groundtruth
    _select_spec.loader.exec_module(groundtruth)

RECORD_PATH = (
    "/home/rmessinger/log/track-r/sedan-alternative-groundtruth-20261103T0900-0500/"
    "sedan-groundtruth-protocol-plan.md"
)
DEFAULT_ARCHIVE_EVIDENCE = (
    "/home/rmessinger/log/track-r/sedan-download-20261103T0905/hash-manifest.txt"
)

# Pinned identities of the completed RGE ALTI / BD ORTHO archive parts (protocol v1.1
# section 3 acquisition): the completion manifest must list exactly these part names
# with exactly these sizes and the sha256 digests recorded at download completion.
ARCHIVE_EVIDENCE_PARTS = (
    ("BDORTHO_1-0_RVB-0M20_JP2-E080_LAMB93_D008_2022-01-01.7z.001", 4_294_967_296),
    ("BDORTHO_1-0_RVB-0M20_JP2-E080_LAMB93_D008_2022-01-01.7z.002", 4_294_967_296),
    ("BDORTHO_1-0_RVB-0M20_JP2-E080_LAMB93_D008_2022-01-01.7z.003", 4_294_967_296),
    ("BDORTHO_1-0_RVB-0M20_JP2-E080_LAMB93_D008_2022-01-01.7z.004", 4_294_967_296),
    ("BDORTHO_1-0_RVB-0M20_JP2-E080_LAMB93_D008_2022-01-01.7z.005", 4_294_967_296),
    ("BDORTHO_1-0_RVB-0M20_JP2-E080_LAMB93_D008_2022-01-01.7z.006", 2_761_389_542),
    ("RGEALTI_2-0_1M_TIF_LAMB93-IGN69_D008_2019-10-14.7z.001", 4_294_967_296),
    ("RGEALTI_2-0_1M_TIF_LAMB93-IGN69_D008_2019-10-14.7z.002", 1_304_321_008),
)
ARCHIVE_EVIDENCE_MAX_BYTES = 8192
ARCHIVE_EVIDENCE_MAX_LINES = 32
ARCHIVE_EVIDENCE_MAX_LINE_CHARS = 512
ARCHIVE_EVIDENCE_STATE_MAX_BYTES = 1 << 20
ARCHIVE_EVIDENCE_PROVENANCE = (
    "manifest digests are the acquisition unit's completion record (sha256 recorded at "
    "download completion), not publisher-authenticated hashes; no archive bytes are "
    "rehashed or claimed here"
)

LOT = "NUALHD_1-0__LAZ_LAMB93_OD_2026-04-29"
RESOURCE = "LiDARHD-NUALID"
BASE_URL = f"https://data.geopf.fr/telechargement/download/{RESOURCE}/{LOT}/"

FIRST_TILE_NAME = "LHD_FXX_0840_6958_PTS_LAMB93_IGN69.copc.laz"
FIRST_TILE_SIZE = 128_651_625
Tile = NamedTuple("Tile", [("name", str), ("url", str), ("size", int), ("rel_path", str)])
FIRST_TILE = Tile(
    name=FIRST_TILE_NAME,
    url=BASE_URL + FIRST_TILE_NAME,
    size=FIRST_TILE_SIZE,
    rel_path=os.path.join("lidarhd", LOT, FIRST_TILE_NAME),
)
# Stage-2 tiles: pinned identity is recorded, but this tool must never fetch them.
OTHER_TILES = {
    "LHD_FXX_0839_6958_PTS_LAMB93_IGN69.copc.laz": 119_931_651,
    "LHD_FXX_0839_6959_PTS_LAMB93_IGN69.copc.laz": 146_099_989,
    "LHD_FXX_0840_6959_PTS_LAMB93_IGN69.copc.laz": 130_615_694,
    "LHD_FXX_0841_6958_PTS_LAMB93_IGN69.copc.laz": 153_352_444,
    "LHD_FXX_0841_6959_PTS_LAMB93_IGN69.copc.laz": 151_880_730,
}

USER_AGENT = "Panopticon-TrackR-Sedan-Proof/1.0 (LiDAR HD tile fetch)"

CACHE_ROOT = "/mnt/pit2/panopticon/geo-proof/sedan"
TOOLS_DIR_REL = os.path.join("tools", "lidar-venv")
PROBE_STATE_VERSION = 1
REQUIRED_TILE_KEYS = {"url", "expected_size", "bytes_on_disk", "status", "mtime_ns"}

WORKSPACE_RESERVE_BYTES = 2_147_483_648  # protocol v1.1 section 4 guard 2
CHUNK_SIZE = 1 << 20

# AOI in EPSG:32631 (protocol v1.1 section 6.1); the probe window adds the warp margin.
AOI_CENTRE_E_32631 = 640391.997
AOI_CENTRE_N_32631 = 5507318.179
AOI_HALF_M = 550.0
PROBE_MARGIN_M = 50.0

# First-tile gate constants (protocol v1.1 section 5).
CLASS_GROUND = 2
CLASS_WATER = 9
CLASS_BRIDGE = 17
Z_HARD_MIN_M = 100.0
Z_HARD_MAX_M = 250.0
DENSITY_MIN_PER_M2 = 4.0
COVERAGE_CELL_M = 10.0
LEVELLING_MIN_M = 149.371
LEVELLING_MAX_M = 156.940

# Gate e binds ONLY to WFS-verifiable provider fields: the raw feature properties of
# IGNF_LIDAR-HD_METADONNEE, taken verbatim from the --lineage-file (actual WFS keys).
# Documented normalizations live at their comparison sites: xsd:date trailing-Z
# stripping, JSON-encoded capteur list compared as an order-insensitive set, url_npl
# matched on its path tail. Licence, attribution and the spec document are documentary
# constants of the plan (data.gouv dataset licence lov2; DC_LiDAR_HD_1-0.pdf); they are
# recorded in every report and are never compared as WFS fields and never accepted from
# a self-asserted lineage file (the old LINEAGE_PINNED echo path is removed, review
# blocker B2).
LINEAGE_REQUIRED_WFS_KEYS = (
    "code_mission",
    "date_debut_acquisition",
    "date_fin_acquisition",
    "date_edition",
    "moa_acquisition",
    "moe_acquisition",
    "capteur",
    "systeme_altimetrique",
    "systeme_planimetrique",
    "coordonnees_nw",
    "url_npl",
)
LINEAGE_PINNED_WFS = {
    "code_mission": "24LHDOD",
    "date_debut_acquisition": "2024-11-30",
    "date_fin_acquisition": "2024-11-30",
    "date_edition": "2026-04-29",
    "moa_acquisition": "IGN",
    "moe_acquisition": "Eurosense",
    "systeme_altimetrique": "IGN69",
    "systeme_planimetrique": "LAMB93",
    "coordonnees_nw": "0840-6958",
}
# WFS capteur is a JSON-encoded list of sensor strings; compared ORDER-INSENSITIVELY
# (documented set equality) against this pinned sensor set.
LINEAGE_PINNED_CAPTEUR_SET = frozenset({"RIEGL VQ-780 II-S:H2227189"})
# url_npl is compared on its path tail (endswith) against the pinned first-tile filename.
LINEAGE_PINNED_URL_NPL_TAIL = FIRST_TILE_NAME
LINEAGE_DOCUMENTARY = {
    "licence": {
        "value": "Licence Ouverte 2.0 (lov2)",
        "source": "data.gouv dataset licence (lov2); documentary only, never a WFS comparison field",
    },
    "attribution": {
        "value": "IGN (LiDAR HD), Licence Ouverte 2.0",
        "source": "data.gouv dataset licence (lov2); documentary only, never a WFS comparison field",
    },
    "spec_document": {
        "value": "DC_LiDAR_HD_1-0.pdf",
        "source": ("LiDAR HD descriptive concept document (DC_LiDAR_HD_1-0.pdf); "
                   "documentary only, never a WFS comparison field"),
    },
}

CS2CS_PATH = "cs2cs"
CS2CS_TIMEOUT_SECONDS = 60.0
WORKER_TIMEOUT_SECONDS = 3600.0
WORKER_CHUNK_POINTS = 2_000_000
WORKER_MEMORY_BUDGET_BYTES = 1 << 30

# Protocol v1.2 stage-1 revision (user-approved 2026-11-03 package): gate c counts
# the expected-intersection population; gate d is the preliminary ground-availability
# floor under the complete unchanged section 6 method. The executed v1.1 result
# stands recorded forever in probe-report.json (kept immutable by the new filename).
PROTOCOL_VERSION_L1_2 = "1.2"
REPORT_VERSION = 2
LATTICE_ORIGIN_32631 = (AOI_CENTRE_E_32631 - AOI_HALF_M, AOI_CENTRE_N_32631 - AOI_HALF_M)
C_V1_2_THRESHOLD_PER_M2 = DENSITY_MIN_PER_M2
# Recorded v1.1 execution provenance (probe-report.json, 2026-11-03): the filter-window
# median and its population, retained verbatim; never recomputed as a new gate.
C_HISTORICAL_RECORDED = {"median_class2_points_per_m2": 11.61, "cells_counted": 9351}
C_V1_2_NOTE = (
    "v1.2 population: non-empty 10 m cells of the expected set (AOI grid intersect the "
    "observed first-tile header footprint, boundary partial cells included); excludes the "
    "575 out-of-grid filter-window keys recorded in the v1.1 reconciliation and the +50 m "
    "probe margin; the median-over-non-empty estimator and the 4.0 threshold are unchanged"
)
C_HISTORICAL_NOTE = (
    "v1.1 filter-window measurement retained from the recorded 2026-11-03 execution "
    "(median 11.61 pts/m2 over 9351 cells); it is the v1.1 measurement, not a new one, "
    "and is non-gating under v1.2; the v1.2 gate is c_v1_2"
)
D_V1_2_SCOPE_NOTE = (
    "v1.2 stage-1 ground availability: at least 5 preliminary eligible cells across at "
    "least 3 distinct full-AOI sectors of the canonical 3x3 grid under the complete "
    "unchanged section 6 method (E1-E7, strata, S1-hard, fallback ladder, 100 m spacing) "
    "on the canonical full-AOI 3.048 m lattice, evaluation windows fully inside the "
    "observed first-tile header footprint; fewer than 5 cells or fewer than 3 sectors is "
    "STOP and escalate, never waived; the 10 m coverage map and all holes remain recorded "
    "verbatim below; preliminary candidates are availability evidence only, never the "
    "final frozen selection (the final 5+2 selection requires the all-tiles scope, which "
    "this path never claims)"
)
STAGE_1_PRELIMINARY_NOTE = (
    "stage_1_preliminary is true by construction: these candidates are availability "
    "evidence only; the final-freeze API in sedan_lidar_select refuses all-tiles-freeze "
    "construction from stage-1 preliminary outputs"
)

DENSITY_OPERATIONALIZATION = (
    "median class-2 density over 10 m cells containing at least one class-2 point inside "
    "the AOI+50 m filter window; coverage holes are gate d's responsibility; recorded for "
    "reviewer confirmation (protocol v1.1 section 5 item c)"
)
GATE_D_SCOPE_NOTE = (
    "Stage 1 verifies physical coverage only of the AOI intersect the OBSERVED header "
    "footprint of the first tile; no whole-AOI coverage is claimed or implied; stage-2 "
    "tile authorization is a separate operator decision that this tool does not grant, "
    "and the currently pinned remaining-tile list must not be read as an AOI-coverage "
    "claim"
)
CLASS9_FALLBACK_NOTE = (
    "class 9 (eau) absent over the AOI portion; weaker fallback (density/roughness only) "
    "applies and is recorded explicitly (protocol v1.1 section 5 item f)"
)
PROOF_NOTE = (
    "The word PASS in this report applies to protocol section 5 gate items only and never "
    "asserts proof success; numeric_proof_pass is false by construction and the numeric "
    "acceptance decision belongs to the separate operator gate (protocol section 9, gate 3)."
)


def log(message):
    acquire.log(message)


def drift_message(tile, detail):
    return (
        f"{tile.url}: pinned editions only; drift fails, never upgrades ({detail}; "
        f"pinned={tile.size})"
    )


def layout(cache):
    lidarhd = os.path.join(cache, "lidarhd")
    state_dir = os.path.join(lidarhd, "state")
    tile_file = os.path.join(cache, FIRST_TILE.rel_path)
    return {
        "cache": cache,
        "tile_dir": os.path.dirname(tile_file),
        "tile": tile_file,
        "state_dir": state_dir,
        "state": os.path.join(state_dir, "state.json"),
        "lock": os.path.join(state_dir, "lock"),
        "tools": os.path.join(cache, TOOLS_DIR_REL),
        "request": os.path.join(state_dir, "worker-request.json"),
        "measurements": os.path.join(state_dir, "worker-measurements.json"),
        "report": os.path.join(state_dir, "probe-report.json"),
        # v1.2 report filename: the recorded v1.1 probe-report.json stays IMMUTABLE.
        "report_v1_2": os.path.join(state_dir, "probe-report-v1_2.json"),
    }


# Network seam: probe-own opener with the pinned UA. acquire.open_url setdefaults the
# acquire UA, so the probe must never use it for its own requests.
OPENER = urllib.request.build_opener(acquire.SameHostRedirectHandler())


def open_tile_url(url, method="GET", headers=None):
    sent_headers = dict(headers or {})
    sent_headers.setdefault("User-Agent", USER_AGENT)
    request = urllib.request.Request(url, headers=sent_headers, method=method)
    return OPENER.open(request, timeout=acquire.DEFAULT_TIMEOUT_SECONDS)


def check_probe_paths():
    cache = acquire.check_path_safety(CACHE_ROOT)
    tile_dir = acquire.check_path_safety(layout(cache)["tile_dir"])
    return cache, tile_dir


def require_free(ancestor, bytes_on_disk):
    usage = acquire.statvfs(ancestor)
    free = usage.f_bavail * usage.f_frsize
    owned = min(bytes_on_disk, FIRST_TILE.size)
    required = FIRST_TILE.size - owned + WORKSPACE_RESERVE_BYTES
    log(
        f"probe free space: free={free} required={required} "
        f"(first tile {FIRST_TILE.size} - on disk {owned} + reserve {WORKSPACE_RESERVE_BYTES})"
    )
    if free < required:
        acquire.fail_guard(
            f"insufficient free space on {ancestor}: free={free} required={required} "
            f"remaining={required - free}"
        )
    return free, required


def peek_owned_bytes(tile_file):
    try:
        info = os.lstat(tile_file)
    except OSError:
        return 0
    if not os.path.isfile(tile_file) or os.path.islink(tile_file):
        return 0
    return info.st_size


def check_mount_realness_or_refuse(tile_dir):
    verified = acquire.check_mount_realness(tile_dir)
    if not verified:
        acquire.fail_guard(
            f"mount realness NOT verified for {tile_dir}: refusing to write on a "
            f"possibly-unmounted path (no fallback, no faked mounts)"
        )
    return verified


class EvidenceProblem(Exception):
    """Any fail-closed defect of the archive completion evidence or its cross-check."""


def _reject_symlink_components(path, label):
    current = os.path.abspath(path)
    while True:
        if os.path.islink(current):
            raise EvidenceProblem(f"{label} path carries a symlinked component: {current}")
        parent = os.path.dirname(current)
        if parent == current:
            return
        current = parent


ARCHIVE_MANIFEST_DATA_RE = re.compile(r"^([0-9a-f]{64})\s+(\d+)\s+(\S+)$")


def _read_manifest_lines(path):
    """Bounded read: at most ARCHIVE_EVIDENCE_MAX_BYTES of file, at most
    ARCHIVE_EVIDENCE_MAX_LINES lines, at most ARCHIVE_EVIDENCE_MAX_LINE_CHARS per line;
    no unbounded growth on hostile input."""
    try:
        info = os.stat(path)
    except OSError as error:
        raise EvidenceProblem(f"archive completion evidence missing or unreadable at {path}: {error}")
    if info.st_size > ARCHIVE_EVIDENCE_MAX_BYTES:
        raise EvidenceProblem(
            f"archive completion evidence at {path} is {info.st_size} bytes, beyond the "
            f"{ARCHIVE_EVIDENCE_MAX_BYTES}-byte parse bound"
        )
    try:
        with open(path, "rb") as handle:
            raw = handle.read(ARCHIVE_EVIDENCE_MAX_BYTES + 1)
    except OSError as error:
        raise EvidenceProblem(f"archive completion evidence unreadable at {path}: {error}")
    if len(raw) > ARCHIVE_EVIDENCE_MAX_BYTES:
        raise EvidenceProblem(
            f"archive completion evidence at {path} grew beyond the "
            f"{ARCHIVE_EVIDENCE_MAX_BYTES}-byte parse bound while being read"
        )
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError as error:
        raise EvidenceProblem(f"archive completion evidence at {path} is not valid UTF-8: {error}")
    lines = text.splitlines()
    if len(lines) > ARCHIVE_EVIDENCE_MAX_LINES:
        raise EvidenceProblem(
            f"archive completion evidence at {path} has {len(lines)} lines, beyond the "
            f"{ARCHIVE_EVIDENCE_MAX_LINES}-line bound"
        )
    for line in lines:
        if len(line) > ARCHIVE_EVIDENCE_MAX_LINE_CHARS:
            raise EvidenceProblem(
                f"archive completion evidence at {path} has a line of {len(line)} chars, "
                f"beyond the {ARCHIVE_EVIDENCE_MAX_LINE_CHARS}-char line bound"
            )
    return lines


def _parse_archive_manifest(path):
    """Strict parser of the acquisition unit's 9-line hash manifest: line 1 is the
    '#...' comment header, then EXACTLY 8 lines '<64-lowercase-hex sha256> <size>
    <name>' matching the pinned part names and sizes. Returns {name: (sha256, size)}.
    The digests are the acquisition unit's completion record, never publisher-
    authenticated hashes."""
    lines = _read_manifest_lines(path)
    if not lines:
        raise EvidenceProblem(f"archive completion evidence at {path} is empty")
    if not lines[0].startswith("#"):
        raise EvidenceProblem(
            f"archive completion evidence at {path} is missing the '#...' comment header "
            f"(first line: {lines[0][:80]!r})"
        )
    entries = {}
    for number, line in enumerate(lines[1:], start=2):
        match = ARCHIVE_MANIFEST_DATA_RE.match(line)
        if match is None:
            raise EvidenceProblem(
                f"archive completion evidence at {path} has a malformed line {number}, "
                f"expected '<64-lowercase-hex sha256> <size> <name>': {line[:80]!r}"
            )
        digest, size_text, name = match.groups()
        if name in entries:
            raise EvidenceProblem(
                f"archive completion evidence at {path} lists part {name} more than once "
                f"(line {number})"
            )
        entries[name] = (digest, int(size_text))
    pinned = {name: size for name, size in ARCHIVE_EVIDENCE_PARTS}
    missing = sorted(set(pinned) - set(entries))
    if missing:
        raise EvidenceProblem(
            f"archive completion evidence at {path} is missing pinned part(s): {', '.join(missing)}"
        )
    unpinned = sorted(set(entries) - set(pinned))
    if unpinned:
        raise EvidenceProblem(
            f"archive completion evidence at {path} lists unpinned part name(s) "
            f"(wrong name or edition): {', '.join(unpinned)}"
        )
    wrong = [f"{name}: size {entries[name][1]} != pinned {size}"
             for name, size in sorted(pinned.items()) if entries[name][1] != size]
    if wrong:
        raise EvidenceProblem(
            f"archive completion evidence at {path} records wrong size(s): {'; '.join(wrong)}"
        )
    return entries


def archive_state_path():
    return os.path.join(CACHE_ROOT, "state", "state.json")


def _crosscheck_archive_state(entries):
    """Optional consistency cross-check against the acquire unit's completed owned
    state (state.json under the cache root). Absent state: proceed on the manifest
    alone and record the absence. Present state: every pinned part must have exactly
    one completed record whose sha256 and sizes equal the manifest record; otherwise
    the evidence is inconsistent and fails. No archive bytes are rehashed here."""
    state_path = archive_state_path()
    if not os.path.exists(state_path):
        return f"acquire owned-state absent at {state_path}; manifest-only verification"
    _reject_symlink_components(state_path, "archive owned-state")
    try:
        with open(state_path, "r", encoding="utf-8") as handle:
            raw = handle.read(ARCHIVE_EVIDENCE_STATE_MAX_BYTES + 1)
    except OSError as error:
        raise EvidenceProblem(f"archive owned-state unreadable at {state_path}: {error}")
    if len(raw) > ARCHIVE_EVIDENCE_STATE_MAX_BYTES:
        raise EvidenceProblem(
            f"archive owned-state at {state_path} exceeds the "
            f"{ARCHIVE_EVIDENCE_STATE_MAX_BYTES}-byte read bound"
        )
    try:
        state = json.loads(raw)
    except ValueError as error:
        raise EvidenceProblem(f"archive owned-state at {state_path} is not valid JSON: {error}")
    products = state.get("products") if isinstance(state, dict) else None
    if not isinstance(products, dict):
        raise EvidenceProblem(f"archive owned-state at {state_path} has no products object")
    disagreements = []
    for name, size in ARCHIVE_EVIDENCE_PARTS:
        matches = [record for key, record in products.items()
                   if isinstance(key, str) and key.rsplit("/", 1)[-1] == name]
        if len(matches) != 1 or not isinstance(matches[0], dict):
            disagreements.append(f"{name}: no unambiguous completed owned record")
            continue
        record = matches[0]
        if record.get("status") != "complete":
            disagreements.append(f"{name}: state status {record.get('status')!r} != 'complete'")
            continue
        if (
            record.get("sha256") != entries[name][0]
            or record.get("expected_size") != size
            or record.get("bytes_on_disk") != size
        ):
            disagreements.append(f"{name}: state sha256/size disagree with the manifest record")
    if disagreements:
        raise EvidenceProblem(
            f"archive owned-state at {state_path} disagrees with the completion manifest: "
            f"{'; '.join(disagreements)}"
        )
    return f"acquire owned-state cross-check passed ({state_path})"


def require_archive_evidence(path, hard):
    """L3 ordering guard: the RGE ALTI / BD ORTHO acquisition must have FINISHED before
    any stage-1 transfer is attempted (protocol v1.1 sections 3 and 4).

    The evidence must be the acquisition unit's 9-line hash manifest listing exactly
    the 8 pinned part names with the pinned sizes (review blocker B3 fix); parsing is
    bounded and every defect fails closed BEFORE any mkdir or network, whichever path
    was passed. Symlinked path components on the evidence or owned-state paths are
    refused. An arbitrary operator file is never adopted: the content must match the
    pinned identities exactly."""
    try:
        _reject_symlink_components(path, "archive completion evidence")
        entries = _parse_archive_manifest(path)
        state_note = _crosscheck_archive_state(entries)
        log(
            f"archive completion evidence verified: {path} lists exactly the 8 pinned "
            f"RGE ALTI / BD ORTHO parts "
            f"({sum(size for _name, size in ARCHIVE_EVIDENCE_PARTS)} bytes total); "
            f"{state_note}; {ARCHIVE_EVIDENCE_PROVENANCE}"
        )
        return True
    except EvidenceProblem as error:
        if hard:
            acquire.fail_guard(f"{error}; stage-1 fetch refuses to run without the completed "
                               f"archive acquisition")
        log(f"WARNING: {error}")
        return False


def load_probe_state(state_path):
    if not os.path.exists(state_path):
        return {"version": PROBE_STATE_VERSION, "tile": None}
    try:
        with open(state_path, "r", encoding="utf-8") as handle:
            state = json.load(handle)
    except (OSError, ValueError) as error:
        raise Failure(EXIT_STATE, f"corrupted probe state at {state_path}: {error}; nothing adopted")
    if not isinstance(state, dict) or state.get("version") != PROBE_STATE_VERSION:
        raise Failure(
            EXIT_STATE,
            f"probe state at {state_path} has an unsupported or missing version; nothing adopted",
        )
    tile = state.get("tile")
    if tile is not None:
        if not isinstance(tile, dict) or not REQUIRED_TILE_KEYS.issubset(tile):
            raise Failure(EXIT_STATE, f"corrupted probe state at {state_path}: malformed tile entry")
        if tile["status"] not in ("partial", "complete"):
            raise Failure(EXIT_STATE, f"corrupted probe state at {state_path}: bad tile status")
        if not isinstance(tile["expected_size"], int) or not isinstance(tile["bytes_on_disk"], int):
            raise Failure(EXIT_STATE, f"corrupted probe state at {state_path}: bad tile sizes")
    return state


def mark_partial(state, state_path, tile_file, tile, bytes_on_disk):
    entry = state.get("tile") or {}
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
    state["tile"] = entry
    acquire.write_state(state_path, state)


def mark_complete(state, state_path, tile_file, tile, digest):
    entry = state.get("tile") or {}
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
    state["tile"] = entry
    acquire.write_state(state_path, state)


def scan_tile_ownership(tile_file, tile, entry):
    """Single-tile ownership scan; mirrors acquire.scan_ownership semantics.

    Returns (mode, resume_from) with mode in {"fresh", "partial", "complete"}. An existing
    file not recorded owned in the probe state is an operator collision: exit 4, the file
    is left untouched, nothing is adopted, overwritten or deleted.
    """
    if not os.path.lexists(tile_file):
        if entry is not None and entry["status"] == "complete":
            raise Failure(EXIT_STATE, f"probe state marks the tile complete but the file is missing: {tile_file}")
        return "fresh", 0
    if entry is None:
        raise Failure(
            EXIT_STATE,
            f"operator collision: tile file exists without owned probe state; left untouched: {tile_file}",
        )
    if os.path.islink(tile_file) or not os.path.isfile(tile_file):
        raise Failure(EXIT_STATE, f"operator collision: {tile_file} is not a regular file; left untouched")
    if entry["url"] != tile.url or entry["expected_size"] != tile.size:
        raise Failure(
            EXIT_STATE,
            f"edition drift in probe state: state records url={entry['url']} "
            f"expected={entry['expected_size']} but pinning says url={tile.url} "
            f"expected={tile.size}; pinned editions only",
        )
    if entry["bytes_on_disk"] > tile.size:
        raise Failure(
            EXIT_STATE,
            f"corrupted probe state: bytes_on_disk {entry['bytes_on_disk']} exceeds pinned "
            f"{tile.size}; nothing adopted",
        )
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
                EXIT_STATE,
                f"probe state marks the tile complete but {tile_file} is {info.st_size} bytes",
            )
        return "complete", tile.size
    recorded = entry["bytes_on_disk"]
    if info.st_size < recorded:
        raise Failure(
            EXIT_STATE,
            f"operator collision: {tile_file} shrank below the recorded {recorded} bytes; left untouched",
        )
    if info.st_size == recorded and entry["mtime_ns"] != info.st_mtime_ns:
        raise Failure(
            EXIT_STATE,
            f"operator collision: {tile_file} was modified after the recorded state (mtime mismatch); "
            f"left untouched",
        )
    return "partial", info.st_size


def verify_complete_tile(state, tile_file, tile):
    entry = state.get("tile")
    if entry is None or entry["status"] != "complete":
        raise Failure(
            EXIT_STATE,
            f"probe state does not record a completed owned tile at {tile_file}; run fetch first",
        )
    if not os.path.isfile(tile_file) or os.path.islink(tile_file):
        raise Failure(EXIT_STATE, f"completed tile file is missing or not a regular file: {tile_file}")
    info = os.lstat(tile_file)
    if info.st_size != tile.size:
        raise Failure(
            EXIT_STATE,
            f"completed tile failed length verification: {tile_file} is {info.st_size} bytes, "
            f"pinned {tile.size}",
        )
    digest = hashlib.sha256()
    with open(tile_file, "rb") as handle:
        while True:
            block = handle.read(CHUNK_SIZE)
            if not block:
                break
            digest.update(block)
    if digest.hexdigest() != entry.get("sha256"):
        raise Failure(EXIT_STATE, f"completed tile failed sha256 verification; repairing nothing: {tile_file}")
    return digest.hexdigest()


def head_precheck(tile):
    url = tile.url
    try:
        response = open_tile_url(url, method="HEAD")
    except urllib.error.HTTPError as error:
        raise acquire.classify_http_error(url, error)
    except NETWORK_ERRORS as error:
        raise Failure(EXIT_NETWORK, f"HEAD pre-check failed for {url}: {error}")
    with response:
        if response.status in (401, 403):
            raise Failure(EXIT_NETWORK, acquire.no_account_message(url, response.status))
        if response.status != 200:
            raise Failure(EXIT_NETWORK, f"HEAD pre-check returned HTTP {response.status} for {url}")
        reported = response.headers.get("Content-Length")
    if reported is None:
        raise Failure(EXIT_STATE, drift_message(tile, "HEAD returned no Content-Length"))
    try:
        reported_size = int(reported)
    except ValueError:
        raise Failure(EXIT_STATE, drift_message(tile, f"HEAD Content-Length={reported!r} is not an integer"))
    if reported_size != tile.size:
        raise Failure(EXIT_STATE, drift_message(tile, f"HEAD Content-Length={reported_size}"))
    log(f"head pre-check ok: {url} = {reported_size} bytes")


def download_tile(state, state_path, tile_file, tile, resume_from):
    """Single Range-resume GET; exactly one attempt, no retries.

    206 must carry a parseable Content-Range whose start equals the resume offset and whose
    total equals the pinned size; a 200 on resume refuses to append a full body; an overlong
    body is drift (exit 4) and the file stays at the cap; a short read is a network failure
    (exit 3) with the partial state recorded for the next explicit invocation.
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
                match = acquire.CONTENT_RANGE_RE.match(response.headers.get("Content-Range") or "")
                if match is None:
                    failure = Failure(EXIT_NETWORK, f"206 without parseable Content-Range for {tile.url}")
                elif int(match.group(3)) != tile.size:
                    failure = Failure(
                        EXIT_STATE,
                        drift_message(tile, f"Content-Range total {match.group(3)} disagrees with the pinned size"),
                    )
                elif int(match.group(1)) != resume_from:
                    failure = Failure(
                        EXIT_NETWORK,
                        f"server resumed at byte {match.group(1)} instead of {resume_from} for {tile.url}",
                    )
            elif response.status == 200 and resume_from:
                failure = Failure(
                    EXIT_NETWORK,
                    f"server ignored Range for {tile.url}; refusing to append a full body (rerun to resume)",
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
                                    f"overlong body beyond the pinned {tile.size} bytes (drift); download "
                                    f"stopped at the cap for {tile.url}",
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
                            f"short read for {tile.url}: got {received} of {cap} bytes; owned partial "
                            f"preserved; rerun to resume",
                        )
                    if failure is None:
                        sink.flush()
                        os.fsync(sink.fileno())
                        digest = hasher.hexdigest()
                except NETWORK_ERRORS as error:
                    failure = Failure(
                        EXIT_NETWORK,
                        f"network error during download of {tile.url}: {error}; owned partial preserved; "
                        f"rerun to resume",
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
            mark_partial(state, state_path, tile_file, tile, resume_from + received)
        elif digest is not None:
            mark_complete(state, state_path, tile_file, tile, digest)
    if failure is not None:
        raise failure
    return digest


def run_fetch_guards(paths):
    check_mount_realness_or_refuse(paths["tile_dir"])
    ancestor, _device = acquire.check_ancestor(paths["tile_dir"])
    require_free(ancestor, peek_owned_bytes(paths["tile"]))
    return ancestor


def run_fetch(args):
    if not (args.first_tile and args.execution_brief_accepted):
        acquire.fail_guard(
            "fetch requires BOTH --first-tile AND --execution-brief-accepted; fetch and install "
            "require the reviewed execution brief (protocol v1.1 section 1, limb L4)"
        )
    target = args.tile
    if target != FIRST_TILE.name:
        if target in OTHER_TILES:
            acquire.fail_guard(
                f"{target} is a stage-2 tile and is NEVER downloadable by this tool; stage 2 needs "
                f"its own reviewed brief (protocol v1.1 section 4 staging table)"
            )
        acquire.fail_guard(f"unknown tile {target!r}; this tool fetches only {FIRST_TILE.name}")
    require_archive_evidence(args.archive_evidence, hard=True)
    cache, _tile_dir = check_probe_paths()
    paths = layout(cache)
    log(f"stage-1 fetch target: {FIRST_TILE.name} ({FIRST_TILE.size} bytes, pinned {FIRST_TILE.url})")
    run_fetch_guards(paths)
    state = load_probe_state(paths["state"])
    mode, resume_from = scan_tile_ownership(paths["tile"], FIRST_TILE, state.get("tile"))
    if mode == "complete":
        digest = verify_complete_tile(state, paths["tile"], FIRST_TILE)
        log(
            f"completed owned tile verified; no network used and no redownload "
            f"({FIRST_TILE.size} bytes, sha256 {digest})"
        )
        return
    acquire.acquire_lock(paths["lock"])
    try:
        head_precheck(FIRST_TILE)
        log(f"downloading: {FIRST_TILE.name} from byte {resume_from} of {FIRST_TILE.size}")
        digest = download_tile(state, paths["state"], paths["tile"], FIRST_TILE, resume_from)
        log(f"stage-1 fetch complete: {FIRST_TILE.size} bytes, sha256 {digest}")
    finally:
        acquire.release_lock(paths["lock"])


def run_head_check():
    tile = FIRST_TILE
    log(f"head-check: {tile.url} (single bounded attempt, pinned UA, same-host redirects only)")
    head_precheck(tile)
    log("head-check ok: Content-Length matches the pinned first-tile size")


def run_verify():
    cache, _tile_dir = check_probe_paths()
    paths = layout(cache)
    acquire.check_ancestor(paths["tile_dir"])
    state = load_probe_state(paths["state"])
    digest = verify_complete_tile(state, paths["tile"], FIRST_TILE)
    log(f"verify ok: {paths['tile']} matches pinned length {FIRST_TILE.size} and recorded sha256 {digest}")


def run_status():
    cache, _tile_dir = check_probe_paths()
    paths = layout(cache)
    state = load_probe_state(paths["state"])
    log(f"probe state file: {paths['state']}")
    tile = state.get("tile")
    if tile is None:
        log("tile: no owned entry in probe state")
    else:
        sha = tile.get("sha256", "none")
        log(
            f"tile: {FIRST_TILE.name} status={tile['status']} "
            f"bytes_on_disk={tile['bytes_on_disk']}/{tile['expected_size']} sha256={sha}"
        )
    if os.path.exists(paths["lock"]):
        try:
            pid, recorded = acquire.read_lock(paths["lock"])
            log(f"lock: present at {paths['lock']} (pid {pid}, recorded {recorded}, alive={acquire.pid_alive(pid)})")
        except Failure as failure:
            log(f"lock: present but unreadable: {failure.message}")
    else:
        log(f"lock: absent (planned {paths['lock']})")
    log(f"tools dir (planned, plan-tools prints the recipe): {paths['tools']}")


def run_preflight(args):
    cache, tile_dir = check_probe_paths()
    paths = layout(cache)
    log(f"stage-1 preflight (read-only): tile dir {tile_dir}")
    verified = acquire.check_mount_realness(tile_dir)
    if not verified:
        log(
            "mount realness NOT verified: preflight continues read-only, but fetch would refuse "
            "to write on a possibly-unmounted path"
        )
    ancestor, _device = acquire.check_ancestor(tile_dir)
    require_free(ancestor, peek_owned_bytes(paths["tile"]))
    state = load_probe_state(paths["state"])
    tile = state.get("tile")
    if tile is None:
        log("probe state: no owned tile entry (fresh stage-1 fetch planned)")
    else:
        sha = tile.get("sha256", "none")
        log(
            f"probe state: tile status={tile['status']} "
            f"bytes_on_disk={tile['bytes_on_disk']}/{tile['expected_size']} sha256={sha}"
        )
    log(f"planned tile file: {paths['tile']}")
    log(f"planned state file: {paths['state']}")
    log(f"planned lock file: {paths['lock']}")
    log(f"planned tools dir: {paths['tools']} (plan-tools prints the isolated venv recipe)")
    require_archive_evidence(args.archive_evidence, hard=False)
    log(f"pinned first tile: {FIRST_TILE.name} ({FIRST_TILE.size} bytes) at {FIRST_TILE.url}")
    for name, size in sorted(OTHER_TILES.items()):
        log(f"pinned stage-2 tile (never downloadable by this tool): {name} ({size} bytes)")
    log(f"AOI (EPSG:32631): centre {AOI_CENTRE_E_32631}/{AOI_CENTRE_N_32631}, half {AOI_HALF_M} m, "
        f"probe margin {PROBE_MARGIN_M} m")
    log("preflight complete; nothing was written and no directories were created")


def run_plan_tools():
    tools = layout(CACHE_ROOT)["tools"]
    lines = [
        "plan-tools: isolated LiDAR tooling recipe (print-only; NOTHING is executed here)",
        f"record: {RECORD_PATH} (protocol v1.1 section 3)",
        f"tools dir: {tools}",
        "1. create the venv: python3 -m venv " + tools,
        "   (venv creation via python3 -m venv only; no global or root pip; no HOME env mutation)",
        f"2. install pinned wheels: {tools}/bin/python -m pip install --no-cache-dir "
        "laspy==<chosen-at-execution> lazrs==<chosen-at-execution>",
        "   laspy and lazrs versions are chosen at execution time from PyPI metadata, never "
        "guessed by this recipe",
        "   pip is invoked ONLY inside the venv; no system-site-packages; the only permitted env "
        "mutation is activating that venv",
        "3. record tools-manifest.json under the tools dir with this schema:",
        '   {"manifest_version": 1,',
        '    "created": "<ISO 8601 local with offset>",',
        '    "python_version": "<venv python version>",',
        '    "packages": [{"name": "laspy", "version": "...", "wheel": "<filename>", '
        '"wheel_sha256": "...", "license": "...", "requires": ["..."]},',
        '                 {"name": "lazrs", "version": "...", "wheel": "<filename>", '
        '"wheel_sha256": "...", "license": "...", "requires": ["..."]}],',
        '    "transitive": [{"name": "numpy", "version": "...", "wheel": "<filename>", '
        '"wheel_sha256": "...", "license": "..."}],',
        '    "commands": ["<exact install commands run>"]}',
        "   laspy pulls numpy transitively: record the resolved numpy pin, wheel sha256 and "
        "license; no unpinned transitives may remain unrecorded",
        "4. run the worker ONLY as: " + tools + "/bin/python <worker script> <request.json>",
        "   the controller (this file) stays stdlib-only and never imports laspy, lazrs or numpy; "
        "the worker runs as a subprocess with JSON file I/O",
    ]
    for line in lines:
        log(line)


def run_cs2cs_points(cs2cs_path, points_32631):
    """Batched cs2cs conversion of arbitrary EPSG:32631 points to EPSG:2154 in ONE
    subprocess invocation (version banner accepted from either stream, per the PROJ
    convention of printing it to stderr with a nonzero exit). Axis order is explicit
    by assertion rather than an +axis flag: EPSG:32631 and EPSG:2154 carry authority
    axis order (easting, northing), the parser reads fields[0]=E and fields[1]=N,
    and the caller's anchor-corner agreement check below fails closed if any line
    ever came back axis-swapped. (An appended '+axis=enu' token was tested against
    PROJ Rel. 9.7.1 on 2026-11-03: cs2cs misparses it with EPSG:code targets and
    silently returns geographic degrees with exit 0, so it must not be used.)"""
    try:
        version_run = subprocess.run(
            [cs2cs_path, "--version"],
            capture_output=True,
            text=True,
            timeout=CS2CS_TIMEOUT_SECONDS,
            check=False,
        )
    except (OSError, subprocess.SubprocessError) as error:
        acquire.fail_guard(f"cs2cs is not usable at {cs2cs_path!r}: {error}; failing closed")
    combined = (version_run.stdout or "") + (version_run.stderr or "")
    version_line = ""
    for line in combined.splitlines():
        if "Rel. " in line:
            version_line = line.strip()
            break
    if not version_line:
        acquire.fail_guard(
            f"cs2cs at {cs2cs_path!r} did not report a PROJ version line; failing closed"
        )
    stdin_text = "".join(f"{e} {n}\n" for e, n in points_32631)
    argv = [cs2cs_path, "-f", "%.6f", "EPSG:32631", "+to", "EPSG:2154"]
    try:
        run = subprocess.run(
            argv,
            input=stdin_text,
            capture_output=True,
            text=True,
            timeout=CS2CS_TIMEOUT_SECONDS,
            check=False,
        )
    except (OSError, subprocess.SubprocessError) as error:
        acquire.fail_guard(f"cs2cs conversion failed at {cs2cs_path!r}: {error}; failing closed")
    if run.returncode != 0:
        acquire.fail_guard(
            f"cs2cs conversion exited {run.returncode} at {cs2cs_path!r}: "
            f"{(run.stderr or run.stdout or '').strip()}; failing closed"
        )
    points_l93 = []
    for line in run.stdout.splitlines():
        fields = line.split()
        if not fields:
            continue
        if len(fields) < 2:
            acquire.fail_guard(f"cs2cs produced an unparseable point line: {line!r}; failing closed")
        try:
            points_l93.append((float(fields[0]), float(fields[1])))
        except ValueError:
            acquire.fail_guard(f"cs2cs produced a non-numeric point line: {line!r}; failing closed")
    if len(points_l93) != len(points_32631):
        acquire.fail_guard(
            f"cs2cs returned {len(points_l93)} point lines, expected {len(points_32631)}; failing closed"
        )
    return {
        "path": cs2cs_path,
        "version": version_line,
        "version_exit_code": version_run.returncode,
        "command": argv,
        "axis_order": "easting northing (authority axis order of EPSG:32631 and EPSG:2154; "
                      "asserted by the anchor-corner agreement check)",
        "input_points_32631": list(points_32631),
        "output_points_l93": points_l93,
    }


def run_cs2cs(cs2cs_path):
    """Probe version and convert the AOI and AOI+margin corners plus the canonical
    lattice SW anchor 32631 -> LAMB93 in ONE batched invocation.

    Fail closed when the binary is missing, the conversion exits nonzero, or any
    line is unparseable. The lattice anchor (AOI centre minus 550 m, the exact SW
    corner of the projected 1100 m extent) is the last input point; the anchor's
    converted position must reproduce the converted SW corner to 1e-6 m, which is
    the explicit axis-order guard (an axis-swapped line could not agree)."""
    corners_32631 = []
    for half in (AOI_HALF_M, AOI_HALF_M + PROBE_MARGIN_M):
        corners_32631.extend(
            [
                (AOI_CENTRE_E_32631 - half, AOI_CENTRE_N_32631 - half),
                (AOI_CENTRE_E_32631 + half, AOI_CENTRE_N_32631 - half),
                (AOI_CENTRE_E_32631 + half, AOI_CENTRE_N_32631 + half),
                (AOI_CENTRE_E_32631 - half, AOI_CENTRE_N_32631 + half),
            ]
        )
    info = run_cs2cs_points(cs2cs_path, corners_32631 + [LATTICE_ORIGIN_32631])
    corners_l93 = info["output_points_l93"][:8]
    anchor_l93 = info["output_points_l93"][8]
    if max(abs(anchor_l93[0] - corners_l93[0][0]), abs(anchor_l93[1] - corners_l93[0][1])) > 1e-6:
        acquire.fail_guard(
            f"cs2cs lattice anchor {anchor_l93} disagrees with the converted AOI SW corner "
            f"{corners_l93[0]}; axis order or frame is inconsistent; failing closed"
        )
    info["input_corners_32631"] = info.pop("input_points_32631")[:8]
    info["output_corners_l93"] = corners_l93
    info["lattice_anchor_32631"] = list(LATTICE_ORIGIN_32631)
    info["lattice_anchor_l93"] = list(anchor_l93)
    info["batched_point_count"] = 9
    log(f"cs2cs: {info['version']} ({cs2cs_path}); converted 8 corners + lattice anchor "
        f"EPSG:32631 -> EPSG:2154 in one batched invocation")
    return info


def bbox_from_corners(corners):
    xs = [point[0] for point in corners]
    ys = [point[1] for point in corners]
    return {"x_min": min(xs), "y_min": min(ys), "x_max": max(xs), "y_max": max(ys)}


def load_lineage(path):
    if not os.path.isfile(path):
        acquire.fail_guard(f"lineage file missing at {path}; the bounded WFS re-query record is required")
    try:
        with open(path, "r", encoding="utf-8") as handle:
            lineage = json.load(handle)
    except (OSError, ValueError) as error:
        acquire.fail_guard(f"lineage file at {path} is not readable JSON: {error}; failing closed")
    if not isinstance(lineage, dict):
        acquire.fail_guard(f"lineage file at {path} is not a JSON object; failing closed")
    return lineage


def invoke_worker(venv_python, worker_script, request):
    argv = [venv_python, worker_script, request["request_path"]]
    log(f"invoking worker subprocess: {' '.join(argv)}")
    try:
        run = subprocess.run(
            argv,
            capture_output=True,
            text=True,
            timeout=WORKER_TIMEOUT_SECONDS,
            check=False,
        )
    except (OSError, subprocess.SubprocessError) as error:
        acquire.fail_guard(f"worker subprocess failed to run ({argv[0]}, {argv[1]}): {error}")
    if run.returncode != 0:
        excerpt = ((run.stderr or "") + (run.stdout or "")).strip()[:500]
        acquire.fail_guard(
            f"worker subprocess exited {run.returncode}; refusing to gate on partial measurements; "
            f"output excerpt: {excerpt}"
        )
    if not os.path.isfile(request["output_path"]):
        acquire.fail_guard(f"worker wrote no measurements file at {request['output_path']}; failing closed")
    try:
        with open(request["output_path"], "r", encoding="utf-8") as handle:
            measurements = json.load(handle)
    except (OSError, ValueError) as error:
        acquire.fail_guard(f"worker measurements at {request['output_path']} are not readable JSON: {error}")
    if not isinstance(measurements, dict):
        acquire.fail_guard(f"worker measurements at {request['output_path']} are not a JSON object")
    return measurements, run


def measurement(mapping, key, expected, default=None):
    """Robust field access: (found, value) so gates fail on missing data instead of crashing."""
    if not isinstance(mapping, dict) or key not in mapping:
        return False, default
    value = mapping[key]
    if expected is not None and not isinstance(value, expected):
        return False, default
    return True, value


def evaluate_gate_a(measurements, tile):
    gate = {"pass": True}
    header = measurements.get("header") if isinstance(measurements.get("header"), dict) else {}
    found_bytes, tile_bytes = measurement(measurements, "tile_bytes", int)
    if not found_bytes:
        gate["pass"] = False
        gate["byte_count"] = "missing"
    else:
        gate["byte_count"] = tile_bytes
        gate["byte_count_matches_pinned"] = tile_bytes == tile.size
        if tile_bytes != tile.size:
            gate["pass"] = False
    found_version, las_version = measurement(header, "las_version", str)
    gate["las_version"] = las_version if found_version else "missing"
    if not found_version or las_version != "1.4":
        gate["pass"] = False
        gate["las_version_ok"] = False
    else:
        gate["las_version_ok"] = True
    found_wkt, crs_wkt = measurement(header, "crs_wkt", str)
    gate["crs_wkt_present"] = found_wkt
    gate["crs_wkt_contains_epsg_2154"] = bool(found_wkt and "EPSG" in crs_wkt and "2154" in crs_wkt)
    if not gate["crs_wkt_contains_epsg_2154"]:
        gate["pass"] = False
    else:
        gate["crs_wkt"] = crs_wkt
    found_copc, copc_present = measurement(header, "copc_vlr_present", bool)
    gate["copc_vlr_present"] = copc_present if found_copc else "missing"
    if not found_copc or not copc_present:
        gate["pass"] = False
    found_header_count, header_count = measurement(header, "header_point_count", int)
    points = measurements.get("points") if isinstance(measurements.get("points"), dict) else {}
    found_total, total_points = measurement(points, "total", int)
    if found_header_count and found_total:
        gate["header_point_count"] = header_count
        gate["chunked_point_count"] = total_points
        gate["point_count_consistent"] = header_count == total_points
        if header_count != total_points:
            gate["pass"] = False
    else:
        gate["pass"] = False
        gate["point_count_consistent"] = "missing measurement fields"
    found_format, point_format = measurement(header, "point_format_id", int)
    gate["point_format_id"] = point_format if found_format else "missing"
    return gate


def evaluate_gate_b(measurements):
    aoi = measurements.get("aoi") if isinstance(measurements.get("aoi"), dict) else {}
    gate = {"pass": True, "hard_band_m": [Z_HARD_MIN_M, Z_HARD_MAX_M]}
    found_count, class2_count = measurement(aoi, "class2_count", int)
    if not found_count or class2_count <= 0:
        gate["pass"] = False
        gate["reason"] = "no AOI class-2 z measurements"
        gate["class2_count"] = class2_count if found_count else "missing"
        return gate
    gate["class2_count"] = class2_count
    for key in ("z_min", "z_max", "z_median"):
        found, value = measurement(aoi, key, (int, float))
        gate[key] = value if found else "missing"
        if not found or not isinstance(value, (int, float)) or isinstance(value, bool):
            gate["pass"] = False
            gate["reason"] = f"missing or non-numeric {key}"
    if not gate["pass"]:
        return gate
    gate["hard_band_ok"] = Z_HARD_MIN_M <= gate["z_min"] and gate["z_max"] <= Z_HARD_MAX_M
    if not gate["hard_band_ok"]:
        gate["pass"] = False
    gate["advisory"] = {
        "levelling_span_m": [LEVELLING_MIN_M, LEVELLING_MAX_M],
        "aoi_class2_z_median": gate["z_median"],
        "within_levelling_span": LEVELLING_MIN_M <= gate["z_median"] <= LEVELLING_MAX_M,
        "gating": False,
    }
    return gate


def evaluate_gate_c(measurements):
    """HISTORICAL v1.1 evaluator (protocol v1.1 section 5 item c, filter-window
    population): retained in the code and covered by its tests, but NOT called by
    the v1.2 report path, whose gate is evaluate_gate_c_v1_2. The recorded v1.1
    execution result (median 11.61 pts/m2 over 9351 cells) stands unchanged."""
    coverage = measurements.get("coverage") if isinstance(measurements.get("coverage"), dict) else {}
    gate = {"pass": True, "threshold_per_m2": DENSITY_MIN_PER_M2, "operationalization": DENSITY_OPERATIONALIZATION}
    found_cells, cells = measurement(coverage, "class2_cell_counts", dict, default={})
    counts = [value / (COVERAGE_CELL_M * COVERAGE_CELL_M) for value in cells.values() if isinstance(value, int)]
    gate["cells_counted"] = len(counts)
    if not counts:
        gate["pass"] = False
        gate["median_class2_points_per_m2"] = None
        gate["reason"] = "no 10 m cells with class-2 points inside the filter window"
        return gate
    median_density = statistics.median(counts)
    gate["median_class2_points_per_m2"] = median_density
    if median_density < DENSITY_MIN_PER_M2:
        gate["pass"] = False
    return gate


def validate_header_footprint(header):
    """Validate the worker-reported ACTUAL LAS header XY footprint. Returns
    (footprint_floats, problem) with problem None when all four edges are finite
    numbers and the rectangle is non-degenerate; the footprint is never derived from
    the tile filename (review blocker B1 fix)."""
    found, footprint = measurement(header, "footprint_l93", dict)
    if not found:
        return None, "worker reported no header footprint_l93 (gate d never assumes the tile filename extent)"
    edges = {}
    for edge in ("x_min", "y_min", "x_max", "y_max"):
        value = footprint.get(edge)
        if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
            raw = {key: footprint.get(key) for key in ("x_min", "y_min", "x_max", "y_max")}
            return None, (
                f"header footprint_l93 edge {edge} is missing, non-numeric or non-finite "
                f"in {raw!r}"
            )
        edges[edge] = float(value)
    if edges["x_max"] <= edges["x_min"] or edges["y_max"] <= edges["y_min"]:
        return None, (
            f"header footprint_l93 is degenerate (non-positive extent) in "
            f"{ {key: edges[key] for key in ('x_min', 'y_min', 'x_max', 'y_max')}!r}"
        )
    return edges, None


def evaluate_gate_d(measurements):
    """HISTORICAL v1.1 evaluator of gate d (every 10 m grid cell of the
    AOI-intersect-header-footprint rectangle contains at least one class-2 point;
    protocol v1.1 section 5 item d): retained in the code and covered by its
    HISTORICAL-labeled tests, but NOT called by the v1.2 report path, whose gate is
    evaluate_gate_d_v1_2. The executed v1.1 result (d FAIL, 718 of 9494 expected
    cells) stands recorded forever and is never rewritten.

    Expected denominator (approved interpretation, review blocker B1 fix): the AOI
    10 m grid, anchored exactly as before at the AOI bbox x_min/y_min (same cs2cs
    corner record and axis conventions), restricted to the cells whose rectangle
    intersects the AOI-intersect-header-footprint rectangle; boundary partial cells
    are INCLUDED. Cells wholly outside the footprint are neither counted missing nor
    massaged. covered = expected cells with >= 1 class-2 point; hole_count =
    expected - covered; PASS iff hole_count == 0, so one dense cell plus huge
    internal holes still FAILS while a perfect intersection-limited cloud PASSES.
    The footprint is the worker-reported scaled LAS header extent, validated before
    use: empty, degenerate, nonsensical, missing, or footprint with zero AOI
    intersection all FAIL (never a vacuous pass)."""
    gate = {"pass": True, "scope_note": GATE_D_SCOPE_NOTE}
    corners = measurements.get("_corners") if isinstance(measurements.get("_corners"), dict) else {}
    aoi_bbox = corners.get("aoi_bbox_l93") if isinstance(corners.get("aoi_bbox_l93"), dict) else {}
    if not aoi_bbox:
        gate["pass"] = False
        gate["reason"] = "AOI bbox unavailable for the expected-cell grid"
        return gate
    header = measurements.get("header") if isinstance(measurements.get("header"), dict) else {}
    footprint, problem = validate_header_footprint(header)
    gate["header_footprint_l93"] = footprint if footprint is not None else (
        header.get("footprint_l93") if isinstance(header.get("footprint_l93"), dict) else "missing"
    )
    if problem is not None:
        gate["pass"] = False
        gate["reason"] = problem
        return gate
    ix_min = max(footprint["x_min"], aoi_bbox["x_min"])
    ix_max = min(footprint["x_max"], aoi_bbox["x_max"])
    iy_min = max(footprint["y_min"], aoi_bbox["y_min"])
    iy_max = min(footprint["y_max"], aoi_bbox["y_max"])
    gate["intersection_bbox_l93"] = {
        "x_min": ix_min, "y_min": iy_min, "x_max": ix_max, "y_max": iy_max,
    }
    if ix_max <= ix_min or iy_max <= iy_min:
        gate["pass"] = False
        gate["reason"] = (
            "header footprint_l93 has zero intersection with the AOI bbox; no cell can "
            "physically contain a class-2 point"
        )
        return gate
    intersection_area = (ix_max - ix_min) * (iy_max - iy_min)
    footprint_area = (footprint["x_max"] - footprint["x_min"]) * (footprint["y_max"] - footprint["y_min"])
    gate["intersection_area_m2"] = intersection_area
    gate["aoi_coverage_fraction_of_footprint"] = intersection_area / footprint_area
    span_x = aoi_bbox["x_max"] - aoi_bbox["x_min"]
    span_y = aoi_bbox["y_max"] - aoi_bbox["y_min"]
    nx = max(1, int(math.ceil(span_x / COVERAGE_CELL_M - 1e-6)))
    ny = max(1, int(math.ceil(span_y / COVERAGE_CELL_M - 1e-6)))
    gate["cell_grid_anchor_l93"] = [aoi_bbox["x_min"], aoi_bbox["y_min"]]
    # Boundary partial cells: floor on both ends includes the edge cells that the
    # intersection rectangle only partially overlaps.
    col_lo = int(math.floor((ix_min - aoi_bbox["x_min"]) / COVERAGE_CELL_M))
    col_hi = min(int(math.floor((ix_max - aoi_bbox["x_min"]) / COVERAGE_CELL_M)), nx - 1)
    row_lo = int(math.floor((iy_min - aoi_bbox["y_min"]) / COVERAGE_CELL_M))
    row_hi = min(int(math.floor((iy_max - aoi_bbox["y_min"]) / COVERAGE_CELL_M)), ny - 1)
    gate["expected_cell_ranges"] = {"cols": [col_lo, col_hi], "rows": [row_lo, row_hi]}
    expected = {
        (col, row)
        for row in range(row_lo, row_hi + 1)
        for col in range(col_lo, col_hi + 1)
    }
    gate["expected_cells"] = len(expected)
    coverage = measurements.get("coverage") if isinstance(measurements.get("coverage"), dict) else {}
    found_cells, cells = measurement(coverage, "class2_cell_counts", dict, default={})
    covered = set()
    for key, value in cells.items():
        if not isinstance(key, str) or not isinstance(value, int) or isinstance(value, bool) or value < 1:
            continue
        parts = key.split(",")
        if len(parts) != 2:
            continue
        try:
            col, row = int(parts[0]), int(parts[1])
        except ValueError:
            continue
        if (col, row) in expected:
            covered.add((col, row))
    holes = sorted(expected - covered, key=lambda cell: (cell[1], cell[0]))
    gate["covered_cells"] = len(covered)
    gate["holes"] = [[col, row] for col, row in holes]
    gate["hole_count"] = len(holes)
    if holes:
        gate["pass"] = False
        gate["reason"] = (
            f"{len(holes)} expected cells of {len(expected)} (AOI grid intersect header "
            f"footprint) contain no class-2 point"
        )
    return gate


def expected_coverage_cells(measurements):
    """Shared expected-set computation for the v1.2 gates: the AOI 10 m grid cells
    intersecting the AOI-intersect-header-footprint rectangle (boundary partial
    cells included), anchored at the cs2cs AOI bbox x_min/y_min. Returns a dict with
    the validated footprint, intersection, ranges and the expected cell set, or a
    fail-closed problem string."""
    corners = measurements.get("_corners") if isinstance(measurements.get("_corners"), dict) else {}
    aoi_bbox = corners.get("aoi_bbox_l93") if isinstance(corners.get("aoi_bbox_l93"), dict) else {}
    if not aoi_bbox:
        return {"problem": "AOI bbox unavailable for the expected-cell grid"}
    header = measurements.get("header") if isinstance(measurements.get("header"), dict) else {}
    footprint, problem = validate_header_footprint(header)
    if problem is not None:
        return {"problem": problem, "header_footprint_l93": footprint}
    ix_min = max(footprint["x_min"], aoi_bbox["x_min"])
    ix_max = min(footprint["x_max"], aoi_bbox["x_max"])
    iy_min = max(footprint["y_min"], aoi_bbox["y_min"])
    iy_max = min(footprint["y_max"], aoi_bbox["y_max"])
    if ix_max <= ix_min or iy_max <= iy_min:
        return {"problem": "header footprint_l93 has zero intersection with the AOI bbox",
                "header_footprint_l93": footprint}
    span_x = aoi_bbox["x_max"] - aoi_bbox["x_min"]
    span_y = aoi_bbox["y_max"] - aoi_bbox["y_min"]
    nx = max(1, int(math.ceil(span_x / COVERAGE_CELL_M - 1e-6)))
    ny = max(1, int(math.ceil(span_y / COVERAGE_CELL_M - 1e-6)))
    col_lo = int(math.floor((ix_min - aoi_bbox["x_min"]) / COVERAGE_CELL_M))
    col_hi = min(int(math.floor((ix_max - aoi_bbox["x_min"]) / COVERAGE_CELL_M)), nx - 1)
    row_lo = int(math.floor((iy_min - aoi_bbox["y_min"]) / COVERAGE_CELL_M))
    row_hi = min(int(math.floor((iy_max - aoi_bbox["y_min"]) / COVERAGE_CELL_M)), ny - 1)
    expected = {
        (col, row)
        for row in range(row_lo, row_hi + 1)
        for col in range(col_lo, col_hi + 1)
    }
    return {
        "aoi_bbox_l93": aoi_bbox,
        "header_footprint_l93": footprint,
        "intersection_bbox_l93": {
            "x_min": ix_min, "y_min": iy_min, "x_max": ix_max, "y_max": iy_max,
        },
        "expected_cell_ranges": {"cols": [col_lo, col_hi], "rows": [row_lo, row_hi]},
        "expected_cells": expected,
    }


def evaluate_gate_c_v1_2(measurements, coverage):
    """Gate c v1.2 (protocol v1.2 item c): median class-2 density over the NON-EMPTY
    10 m cells of the expected set (AOI grid intersect the observed header
    footprint). Population = the coverage class2_cell_counts keys intersected with
    the expected set; estimator (median over non-empty cells) and the 4.0 threshold
    are unchanged from v1.1; missing cells stay reported by gate d_v1_2."""
    gate = {
        "pass": True,
        "threshold_per_m2": C_V1_2_THRESHOLD_PER_M2,
        "operationalization": C_V1_2_NOTE,
        "protocol_version": PROTOCOL_VERSION_L1_2,
    }
    if "problem" in coverage:
        gate["pass"] = False
        gate["reason"] = coverage["problem"]
        gate["median_class2_points_per_m2"] = None
        gate["population_count"] = 0
        return gate
    expected = coverage["expected_cells"]
    coverage_block = (
        measurements.get("coverage") if isinstance(measurements.get("coverage"), dict) else {}
    )
    found_cells, cells = measurement(coverage_block, "class2_cell_counts", dict, default={})
    population = {}
    out_of_grid = 0
    for key, value in cells.items():
        if not isinstance(key, str) or not isinstance(value, int) or isinstance(value, bool) or value < 1:
            continue
        parts = key.split(",")
        if len(parts) != 2:
            continue
        try:
            col, row = int(parts[0]), int(parts[1])
        except ValueError:
            continue
        if (col, row) in expected:
            population[(col, row)] = value
        else:
            out_of_grid += 1
    gate["population_count"] = len(population)
    gate["expected_cells"] = len(expected)
    gate["excluded_out_of_grid_keys"] = out_of_grid
    gate["excludes_out_of_grid_and_margin"] = (
        "population excludes the 575 out-of-grid filter-window keys recorded in the v1.1 "
        "reconciliation and the +50 m probe margin"
    )
    if not population:
        gate["pass"] = False
        gate["median_class2_points_per_m2"] = None
        gate["reason"] = "no non-empty 10 m cells of the expected set carry class-2 points"
        return gate
    counts = [value / (COVERAGE_CELL_M * COVERAGE_CELL_M) for value in population.values()]
    median_density = statistics.median(counts)
    gate["median_class2_points_per_m2"] = median_density
    if median_density < C_V1_2_THRESHOLD_PER_M2:
        gate["pass"] = False
    return gate


def evaluate_gate_c_historical():
    """Recorded v1.1 filter-window measurement provenance (non-gating under v1.2).
    The executed v1.1 result stands; it is not recomputed here and not a new gate."""
    return {
        "gating": False,
        "recorded_v1_1": dict(C_HISTORICAL_RECORDED),
        "note": C_HISTORICAL_NOTE,
    }


def _candidate_value_defective(value):
    """C1 finite-validation defense: a candidate statistic or count cell is
    defective when it is neither None (unavailable) nor a finite real number.
    NaN/Inf must never reach rule comparisons (NaN > threshold is False and
    would silently bypass E4/E5/E7) and a defective cell must never be
    eligible."""
    if value is None:
        return False
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return True
    return not math.isfinite(value)


_CANDIDATE_VALUE_KEYS = (
    "n2", "a", "b", "c", "sigma_fit", "slope_deg", "curv_bound",
    "mix2", "mix3", "mix4", "mix5", "mix6", "mix9", "mix17",
    "e2_dist", "e3_dist",
)
_CANDIDATE_NONFINITE_EXCLUSION = "FIT_NONFINITE"


def build_candidate_records(cand_block, request, aoi_bbox_l93):
    """Map the worker's candidate parallel arrays onto canonical candidate records
    (protocol 6.2/6.3 fields), applying the controller-owned rules: E6 seam/border
    distance from the cs2cs AOI bbox, sector indexing on the canonical full-AOI 3x3
    grid, and the frozen fired-rule evaluation. Returns (records, meta, problem).
    Only cells inside the canonical one-cell-margin enumeration are gated; the
    worker's extra margin-ring cells are counted and ignored. C1 defense: any
    non-finite (or non-numeric) statistic/count marks the cell fit-unavailable
    with a recorded reason and FIT_NONFINITE exclusion; the offending count drops
    to 0, the offending statistic to None; such a cell is NEVER eligible."""
    if not isinstance(cand_block, dict):
        return None, {}, "candidates block is not a JSON object"
    arrays = cand_block.get("arrays")
    columns = cand_block.get("columns")
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
    if length == 0:
        return [], {"cells_contained": 0}, None
    origin_block = cand_block.get("lattice_origin_l93")
    origin_request = (request.get("candidates") or {}).get("lattice_origin_l93")
    if (
        not isinstance(origin_block, list)
        or len(origin_block) != 2
        or not isinstance(origin_request, list)
        or len(origin_request) != 2
        or max(abs(float(origin_block[i]) - float(origin_request[i])) for i in (0, 1)) > 1e-9
    ):
        return None, {}, "worker lattice origin disagrees with the request lattice origin"
    spacing = float(cand_block["spacing_m"])
    anchor_x = float(origin_block[0])
    anchor_y = float(origin_block[1])
    canonical_cells = set(groundtruth.enumerate_lattice_cells())
    footprint = cand_block.get("footprint_observed_l93")
    if not isinstance(footprint, dict):
        return None, {}, "candidates block carries no footprint_observed_l93"

    def cell_value(index, key):
        value = arrays[key][index]
        return None if value is None else value

    def candidate_count(value):
        return 0 if value is None else int(value)

    records = []
    ignored_ring = 0
    containment_failures = 0
    for index in range(length):
        col = int(cell_value(index, "col"))
        row = int(cell_value(index, "row"))
        abs_x = anchor_x + (col + 0.5) * spacing
        abs_y = anchor_y + (row + 0.5) * spacing
        if not groundtruth.window_inside_footprint(
            abs_x, abs_y, groundtruth.WINDOW_R, footprint
        ):
            containment_failures += 1
            continue
        if (row, col) not in canonical_cells:
            ignored_ring += 1
            continue
        utm_x, utm_y = groundtruth.lattice_centre(row, col)
        e6 = groundtruth.seam_border_distance(abs_x, abs_y, aoi_bbox_l93)
        values = {
            key: (arrays[key][index] if key in arrays else None)
            for key in _CANDIDATE_VALUE_KEYS
        }
        nonfinite = [key for key in _CANDIDATE_VALUE_KEYS
                     if _candidate_value_defective(values[key])]
        if nonfinite:
            # C1 defense: offending counts drop to 0 (E1 then fires), offending
            # statistics to None; the cell is never eligible and the exclusion is
            # counted in the report.
            for key in nonfinite:
                values[key] = None
        sigma = values["sigma_fit"]
        slope = values["slope_deg"]
        curv = values["curv_bound"]
        truth_z = values["a"]
        d9 = values["e2_dist"]
        d17 = values["e3_dist"]
        n2 = candidate_count(values["n2"])
        fired = groundtruth.fired_rules(n2, d9, d17, sigma, slope, e6, curv)
        if nonfinite:
            fired.append((_CANDIDATE_NONFINITE_EXCLUSION, ",".join(nonfinite)))
        sr, sc = groundtruth.sector_of(utm_x, utm_y)
        centroid_x, centroid_y = groundtruth.sector_centroid(sr, sc)
        stratum, pool = groundtruth.classify_stratum(slope, sigma)
        records.append({
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
            "n3": candidate_count(values["mix3"]),
            "n4": candidate_count(values["mix4"]),
            "n5": candidate_count(values["mix5"]),
            "n6": candidate_count(values["mix6"]),
            "n9": candidate_count(values["mix9"]),
            "n17": candidate_count(values["mix17"]),
            "d9": d9,
            "d17": d17,
            "e6_dist": e6,
            "curv_unavailable": bool(cell_value(index, "curv_unavailable")),
            "fit_unavailable": bool(cell_value(index, "fit_unavailable")),
            "sector": groundtruth.sector_name(sr, sc),
            "sector_dist": math.hypot(utm_x - centroid_x, utm_y - centroid_y),
            "stratum": stratum,
            "pool": pool,
            "eligible": not fired,
            "fired": fired,
            "rank": None,
        })
        if nonfinite:
            records[-1]["fit_unavailable"] = True
            records[-1]["nonfinite_fields"] = nonfinite
    meta = {
        "cells_contained": int(cand_block.get("cells_contained", length)),
        "cells_enumerated": int(cand_block.get("cells_enumerated", 0)),
        "canonical_candidates": len(records),
        "ignored_margin_ring_cells": ignored_ring,
        "containment_failures_rereported": containment_failures,
        "footprint_observed_l93": footprint,
    }
    return records, meta, None


def evaluate_gate_d_v1_2(measurements, request, coverage):
    """Gate d v1.2 (protocol v1.2 item d): preliminary ground-availability floor.

    The 10 m coverage map and all holes are recorded verbatim; PASS is decided ONLY
    by the availability floor (at least 5 eligible cells across at least 3 distinct
    full-AOI sectors under the complete unchanged section 6 method). Any floor miss
    is FAIL with reason STOP_AND_ESCALATE and is never waived."""
    gate = {"pass": True, "scope_note": D_V1_2_SCOPE_NOTE}
    if "problem" in coverage:
        gate["pass"] = False
        gate["reason"] = f"STOP_AND_ESCALATE: {coverage['problem']}"
        gate["stop_and_escalate"] = True
        gate["holes"] = []
        gate["hole_count"] = 0
        return gate
    expected = coverage["expected_cells"]
    coverage_block = (
        measurements.get("coverage") if isinstance(measurements.get("coverage"), dict) else {}
    )
    found_cells, cells = measurement(coverage_block, "class2_cell_counts", dict, default={})
    covered = set()
    for key, value in cells.items():
        if not isinstance(key, str) or not isinstance(value, int) or isinstance(value, bool) or value < 1:
            continue
        parts = key.split(",")
        if len(parts) != 2:
            continue
        try:
            col, row = int(parts[0]), int(parts[1])
        except ValueError:
            continue
        if (col, row) in expected:
            covered.add((col, row))
    holes = sorted(expected - covered, key=lambda cell: (cell[1], cell[0]))
    gate["expected_cells"] = len(expected)
    gate["covered_cells"] = len(covered)
    gate["holes"] = [[col, row] for col, row in holes]
    gate["hole_count"] = len(holes)
    gate["intersection_bbox_l93"] = coverage["intersection_bbox_l93"]
    gate["expected_cell_ranges"] = coverage["expected_cell_ranges"]
    gate["header_footprint_l93"] = coverage["header_footprint_l93"]
    gate["coverage_note"] = (
        "10 m coverage map and all holes recorded verbatim (v1.1 retention unchanged); "
        "holes do NOT decide this gate: the availability floor below does"
    )

    cand_block = (
        measurements.get("candidates") if isinstance(measurements.get("candidates"), dict) else None
    )
    if cand_block is None:
        gate["pass"] = False
        gate["stop_and_escalate"] = True
        gate["reason"] = (
            "STOP_AND_ESCALATE: worker produced no candidates block; ground availability "
            "was not measured and is never waived"
        )
        return gate
    aoi_bbox = coverage["aoi_bbox_l93"]
    records, meta, problem = build_candidate_records(cand_block, request, aoi_bbox)
    if problem is not None:
        gate["pass"] = False
        gate["stop_and_escalate"] = True
        gate["reason"] = f"STOP_AND_ESCALATE: {problem}"
        return gate
    availability = {
        "stage_1_preliminary": True,
        "never_final_note": STAGE_1_PRELIMINARY_NOTE,
        "method": "complete unchanged section 6 (E1-E7, strata, S1-hard, ladder, 100 m greedy)",
    }
    availability.update(meta)
    exclusion_counts = {}
    for record in records:
        for rule, _trigger in record["fired"]:
            exclusion_counts[rule] = exclusion_counts.get(rule, 0) + 1
    availability["exclusion_counts"] = {rule: exclusion_counts[rule]
                                        for rule in sorted(exclusion_counts)}
    availability["eligible_count"] = sum(1 for record in records if record["eligible"])
    stop = None
    try:
        selection = groundtruth.select(records, stage_1_preliminary=True)
    except groundtruth.ProtocolStop as protocol_stop:
        stop = {"reason": protocol_stop.reason, "details": {
            key: value for key, value in protocol_stop.details.items()
        }}
        gate["pass"] = False
        gate["stop_and_escalate"] = True
        gate["reason"] = f"STOP_AND_ESCALATE: {protocol_stop.reason}"
        selection = None
    if selection is not None:
        availability["primaries"] = [dict(record, role="primary") for record in selection.primaries]
        availability["alternates"] = [dict(record, role="alternate") for record in selection.alternates]
        availability["sectors_covered"] = list(selection.sectors_covered)
        availability["ladder"] = [[pool, count] for pool, count in selection.ladder]
        availability["alternates_shortfall"] = selection.alternates_shortfall
        availability["spacing_rule"] = (
            "100 m minimum pairwise distance by deterministic greedy in ranking order"
        )
        availability["substitution_policy"] = "pre-DEM only; refused after any DEM opening"
        availability["tables"] = {
            "candidates_sha256": hashlib.sha256(
                groundtruth.candidates_table_bytes(records)).hexdigest(),
            "selection_sha256": hashlib.sha256(
                groundtruth.selection_table_bytes(selection)).hexdigest(),
        }
    gate["availability"] = availability
    gate["protocol_version"] = PROTOCOL_VERSION_L1_2
    if stop is not None:
        gate["stop"] = stop
    else:
        gate["stop"] = None
    return gate


def normalize_lineage_date(value):
    """GeoServer serializes xsd:date with a trailing zone designator ('2026-04-29Z');
    strip exactly one trailing 'Z' when the remainder parses as an ISO yyyy-mm-dd date
    (documented normalization, WFS gate e). Non-strings and non-date remainders are
    returned unchanged so the comparison fails closed with the raw value recorded."""
    if not isinstance(value, str) or not value.endswith("Z"):
        return value
    candidate = value[:-1]
    if not re.fullmatch(r"\d{4}-\d{2}-\d{2}", candidate):
        return value
    try:
        datetime.date.fromisoformat(candidate)
    except ValueError:
        return value
    return candidate


def normalize_lineage_capteur(value):
    """The WFS capteur value is a JSON-encoded string of a list of sensor strings;
    decode it and compare it as an ORDER-INSENSITIVE set (documented normalization,
    WFS gate e). Returns the decoded list, or None when the value is not a JSON
    non-empty list of strings (wrong shape fails closed)."""
    if not isinstance(value, str):
        return None
    try:
        decoded = json.loads(value)
    except ValueError:
        return None
    if not isinstance(decoded, list) or not decoded:
        return None
    if not all(isinstance(item, str) for item in decoded):
        return None
    return decoded


def evaluate_gate_e(lineage):
    """Gate e: lineage recheck against the RAW provider WFS feature properties
    (protocol v1.1 section 5 item e; architect-decided code fix for review blocker B2).

    The --lineage-file must contain the verbatim WFS feature properties object (actual
    WFS keys). Gating happens ONLY on WFS-verifiable identity/mission/flight/CRS
    fields: a required key that is missing, null or of the wrong shape fails with the
    field named, and any pinned disagreement after the documented normalizations fails
    listing each mismatch. A self-asserted dict shaped like the removed LINEAGE_PINNED
    echo (keys mission/flight_date/...) has none of the required WFS keys and fails by
    construction. Extra WFS fields (procede_classement, nombre_points, ...) are
    allowed and recorded as non-gating observations. Licence, attribution and
    spec-document strings stay documentary constants of the report (never compared as
    WFS fields, never self-echoed)."""
    pinned = dict(LINEAGE_PINNED_WFS)
    pinned["capteur"] = sorted(LINEAGE_PINNED_CAPTEUR_SET)
    pinned["url_npl"] = f"path tail {LINEAGE_PINNED_URL_NPL_TAIL!r} (endswith)"
    gate = {
        "pass": True,
        "mismatches": [],
        "pinned_wfs": pinned,
        "documentary": {key: dict(value) for key, value in LINEAGE_DOCUMENTARY.items()},
        "documentary_gating": False,
    }
    if not isinstance(lineage, dict):
        gate["pass"] = False
        gate["reason"] = (
            "lineage file is not a JSON object carrying the verbatim WFS feature "
            "properties (IGNF_LIDAR-HD_METADONNEE provider keys)"
        )
        return gate
    required = set(LINEAGE_REQUIRED_WFS_KEYS)
    gate["non_gating_observations"] = {
        key: value for key, value in sorted(lineage.items()) if key not in required
    }

    def reject(field, recorded, expected):
        gate["pass"] = False
        gate["mismatches"].append({"field": field, "recorded": recorded, "pinned": expected})

    recorded = {}
    for key in LINEAGE_REQUIRED_WFS_KEYS:
        value = lineage.get(key)
        recorded[key] = value
        if value is None:
            reject(key, None, "verbatim WFS field required (missing or null)")
            continue
        if key == "capteur":
            decoded = normalize_lineage_capteur(value)
            if decoded is None:
                reject(key, value, "JSON-encoded non-empty list of sensor strings")
            elif set(decoded) != LINEAGE_PINNED_CAPTEUR_SET:
                reject(key, decoded, sorted(LINEAGE_PINNED_CAPTEUR_SET))
        elif key == "url_npl":
            if not isinstance(value, str) or not value.endswith(LINEAGE_PINNED_URL_NPL_TAIL):
                reject(key, value, pinned["url_npl"])
        elif key in ("date_debut_acquisition", "date_fin_acquisition", "date_edition"):
            normalized = normalize_lineage_date(value) if isinstance(value, str) else None
            if normalized != pinned[key]:
                reject(key, value, f"ISO yyyy-mm-dd {pinned[key]} (xsd:date, one trailing 'Z' stripped)")
        else:
            if not isinstance(value, str) or value != pinned[key]:
                reject(key, value, pinned[key])
    gate["recorded"] = recorded
    if gate["mismatches"]:
        fields = ", ".join(item["field"] for item in gate["mismatches"])
        gate["reason"] = f"lineage WFS fields disagree with the pinned provider values: {fields}"
    return gate


def evaluate_gate_f(measurements):
    aoi = measurements.get("aoi") if isinstance(measurements.get("aoi"), dict) else {}
    gate = {"pass": True}
    for key in ("class9_count", "class17_count"):
        found, value = measurement(aoi, key, int)
        gate[key] = value if found else "missing"
        if not found or isinstance(value, bool) or value < 0:
            gate["pass"] = False
            gate.setdefault("reason", f"missing or invalid {key}")
    gate["class9_fallback"] = None
    if gate["pass"] and gate["class9_count"] == 0:
        gate["class9_fallback"] = CLASS9_FALLBACK_NOTE
    return gate


def evaluate_gates(measurements, lineage, cs2cs_info, worker_run, request, tile_file, tile, digest):
    enriched = dict(measurements)
    enriched["_corners"] = {
        "aoi_bbox_l93": bbox_from_corners(cs2cs_info["output_corners_l93"][:4]),
        "filter_bbox_l93": bbox_from_corners(cs2cs_info["output_corners_l93"][4:8]),
    }
    coverage = expected_coverage_cells(enriched)
    gates = {
        "a": evaluate_gate_a(measurements, tile),
        "b": evaluate_gate_b(measurements),
        "c_v1_2": evaluate_gate_c_v1_2(enriched, coverage),
        "c_historical": evaluate_gate_c_historical(),
        "d_v1_2": evaluate_gate_d_v1_2(enriched, request, coverage),
        "e": evaluate_gate_e(lineage),
        "f": evaluate_gate_f(measurements),
    }
    # v1.2 gating set: c_historical is a retained v1.1 provenance record, not a gate.
    gating = ("a", "b", "c_v1_2", "d_v1_2", "e", "f")
    gates_pass = all(gates[key]["pass"] for key in gating)
    report = {
        "report_version": 2,
        "protocol_version": PROTOCOL_VERSION_L1_2,
        "generated": acquire.now_iso(),
        "record": RECORD_PATH,
        "tile": tile.name,
        "tile_path": tile_file,
        "tile_bytes": os.path.getsize(tile_file) if os.path.exists(tile_file) else None,
        "tile_sha256": digest,
        "cs2cs": cs2cs_info,
        "projection_v1_2": {
            "binary": cs2cs_info["path"],
            "version": cs2cs_info["version"],
            "command": cs2cs_info["command"],
            "axis_order": cs2cs_info["axis_order"],
            "batched_point_count": cs2cs_info["batched_point_count"],
            "lattice_anchor_32631": cs2cs_info["lattice_anchor_32631"],
            "lattice_anchor_l93": cs2cs_info["lattice_anchor_l93"],
        },
        "lattice_v1_2": {
            "crs": "EPSG:32631",
            "origin_utm": [LATTICE_ORIGIN_32631[0], LATTICE_ORIGIN_32631[1]],
            "origin_l93": cs2cs_info["lattice_anchor_l93"],
            "spacing_m": groundtruth.LATTICE,
            "window_r_m": groundtruth.WINDOW_R,
            "e23_radius_m": groundtruth.E23_DIST,
            "margin_cells": groundtruth.ONE_CELL_MARGIN,
            "canonical_cells": len(groundtruth.enumerate_lattice_cells()),
            "full_extent_cells": groundtruth.full_extent_cell_counts()[0]
            * groundtruth.full_extent_cell_counts()[1],
            "thresholds": dict(groundtruth.THRESHOLDS),
        },
        "worker": {
            "venv_python": request["venv_python"],
            "script": request["worker_script"],
            "request_path": request["request_path"],
            "measurements_path": request["output_path"],
            "stdout_excerpt": (worker_run.stdout or "").strip()[:500],
            "memory": measurements.get("memory"),
        },
        "request": {key: value for key, value in request.items() if key != "venv_python"},
        "gates": gates,
        "gates_pass": gates_pass,
        "gate_only": True,
        "stage_1_preliminary": True,
        "stage_1_preliminary_note": STAGE_1_PRELIMINARY_NOTE,
        "numeric_proof_pass": False,
        "note": PROOF_NOTE,
        "v1_1_retention": {
            "probe_report_json": "the recorded v1.1 execution report (gates a/b/c/e/f PASS, "
            "d FAIL 718 of 9494) stands recorded forever and is never rewritten; the old "
            "report filename stays immutable; the v1.1 gate d evaluator remains in this "
            "file as the historical evaluator",
            "c_historical": gates["c_historical"],
        },
    }
    # Selected-candidate criteria are already carried per record inside
    # gates["d_v1_2"]["availability"]["primaries"/"alternates"].
    return report


def run_probe(args):
    cache, _tile_dir = check_probe_paths()
    paths = layout(cache)
    acquire.check_ancestor(paths["tile_dir"])
    state = load_probe_state(paths["state"])
    digest = verify_complete_tile(state, paths["tile"], FIRST_TILE)
    log(f"completed owned tile verified before probing: {paths['tile']} (sha256 {digest})")
    cs2cs_info = run_cs2cs(args.cs2cs or CS2CS_PATH)
    aoi_bbox = bbox_from_corners(cs2cs_info["output_corners_l93"][:4])
    filter_bbox = bbox_from_corners(cs2cs_info["output_corners_l93"][4:8])
    os.makedirs(paths["state_dir"], exist_ok=True)
    full_cols, full_rows = groundtruth.full_extent_cell_counts()
    request = {
        "request_version": 1,
        "tile_path": paths["tile"],
        "aoi_bbox_l93": aoi_bbox,
        "filter_bbox_l93": filter_bbox,
        "aoi_bbox_32631": bbox_from_corners(cs2cs_info["input_corners_32631"][:4]),
        "filter_bbox_32631": bbox_from_corners(cs2cs_info["input_corners_32631"][4:8]),
        "cell_size_m": COVERAGE_CELL_M,
        "chunk_size_points": WORKER_CHUNK_POINTS,
        "memory_budget_bytes": WORKER_MEMORY_BUDGET_BYTES,
        # v1.2 candidate-measurement mode: the lattice SW anchor converted in the same
        # batched cs2cs invocation; the footprint here is the AOI-coverage guard only
        # (containment is computed by the worker against the OBSERVED header footprint).
        "candidates": {
            "lattice_origin_l93": list(cs2cs_info["lattice_anchor_l93"]),
            "aoi_sw_l93": list(cs2cs_info["lattice_anchor_l93"]),
            "lattice_cols": full_cols,
            "lattice_rows": full_rows,
            "spacing_m": groundtruth.LATTICE,
            "window_r": groundtruth.WINDOW_R,
            "e23_radius": groundtruth.E23_DIST,
            "footprint_l93": dict(filter_bbox),
        },
        "venv_python": args.worker[0],
        "worker_script": args.worker[1],
        "request_path": paths["request"],
        "output_path": paths["measurements"],
    }
    with open(paths["request"], "w", encoding="utf-8") as handle:
        json.dump(request, handle, indent=2, sort_keys=True)
        handle.write("\n")
    measurements, worker_run = invoke_worker(args.worker[0], args.worker[1], request)
    lineage = load_lineage(args.lineage_file)
    report = evaluate_gates(
        measurements, lineage, cs2cs_info, worker_run, request, paths["tile"], FIRST_TILE, digest
    )
    with open(paths["report_v1_2"], "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2, sort_keys=True)
        handle.write("\n")
    print(json.dumps(report, indent=2, sort_keys=True))
    log(f"probe report written: {paths['report_v1_2']} (report_version=2, "
        f"gates_pass={report['gates_pass']}, gate_only=true, numeric_proof_pass=false, "
        f"stage_1_preliminary=true; {paths['report']} stays immutable)")
    return EXIT_OK if report["gates_pass"] else EXIT_GUARD


def build_parser():
    parser = argparse.ArgumentParser(
        prog="sedan_lidar_probe.py",
        description=(
            "Stage-1 LiDAR first-tile acquisition and data probe (protocol v1.1, released for "
            "review): fetches ONLY the pinned first tile and gates it per protocol section 5. "
            "The default invocation is a read-only preflight. fetch and tool installation "
            "require the reviewed execution brief."
        ),
        epilog=(
            f"Record: {RECORD_PATH}. Exit codes: 0 success; 2 guard or gate failure; 3 "
            f"download/network failure (resumable); 4 state or drift failure. One data-transfer "
            f"attempt per explicit invocation; no retry loops."
        ),
    )
    parser.add_argument(
        "--archive-evidence",
        default=DEFAULT_ARCHIVE_EVIDENCE,
        help=(
            "path to the completed RGE ALTI / BD ORTHO acquisition hash manifest; warning in "
            "preflight, hard guard in fetch"
        ),
    )
    subparsers = parser.add_subparsers(dest="command", metavar="subcommand")
    subparsers.add_parser("preflight", help="read-only checks plus a printed plan (default)")
    subparsers.add_parser(
        "head-check",
        help="single bounded HEAD of the first-tile URL (network, read-only)",
    )
    fetch_parser = subparsers.add_parser(
        "fetch",
        help="fetch the first tile (network + writes; requires the reviewed execution brief)",
    )
    fetch_parser.add_argument("tile", metavar="TILE_NAME", help="tile file name; must be the first tile")
    fetch_parser.add_argument(
        "--first-tile",
        action="store_true",
        help="required acknowledgement that the target is the stage-1 first tile",
    )
    fetch_parser.add_argument(
        "--execution-brief-accepted",
        action="store_true",
        help="required acknowledgement that the reviewed execution brief is accepted",
    )
    subparsers.add_parser("verify", help="local re-verification of the completed owned tile")
    probe_parser = subparsers.add_parser(
        "probe",
        help="run the isolated worker subprocess and apply the protocol section 5 gates (local only)",
    )
    probe_parser.add_argument(
        "--worker",
        nargs=2,
        metavar=("VENV_PYTHON", "WORKER_SCRIPT"),
        required=True,
        help="isolated venv python and worker script; the worker is invoked as a subprocess",
    )
    probe_parser.add_argument(
        "--lineage-file",
        required=True,
        help=(
            "JSON file containing the VERBATIM WFS feature properties object (actual "
            "IGNF_LIDAR-HD_METADONNEE provider keys) recorded from the bounded WFS "
            "re-query at execution time"
        ),
    )
    probe_parser.add_argument(
        "--cs2cs",
        default=None,
        help=f"path to the system cs2cs binary (default {CS2CS_PATH!r} resolved on PATH)",
    )
    subparsers.add_parser("plan-tools", help="print the isolated venv recipe (executes nothing)")
    subparsers.add_parser("status", help="print the probe's own state summary (local only)")
    return parser


def main(argv=None):
    parser = build_parser()
    args = parser.parse_args(argv)
    command = args.command or "preflight"
    code = EXIT_OK
    try:
        if command == "preflight":
            run_preflight(args)
        elif command == "head-check":
            run_head_check()
        elif command == "fetch":
            run_fetch(args)
        elif command == "verify":
            run_verify()
        elif command == "probe":
            code = run_probe(args)
        elif command == "plan-tools":
            run_plan_tools()
        elif command == "status":
            run_status()
        else:
            parser.error(f"unknown subcommand {command!r}")
    except Failure as failure:
        log(f"FAIL({failure.code}): {failure.message}")
        code = failure.code
    log(f"final status: exit={code} mode={command} cache={CACHE_ROOT}")
    return code


if __name__ == "__main__":
    sys.exit(main())
