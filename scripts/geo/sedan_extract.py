#!/usr/bin/env python3
"""Sedan geo-proof selective extraction helper (thin, pinned membership).

Extracts ONLY the approved 27-member selection (33 with --with-dst) from the two
already-acquired split 7z archives in the Sedan cache. Membership is fixed in
code; there is no arbitrary member selector. This helper does byte copy plus
sha256 and nothing more: it never decodes rasters, never opens DEM heights, and
never computes statistics or residuals. The JP2 pixel sampling convention
(center vs corner) is UNKNOWN here and no geotransform is constructed from .tab
files or filenames; GDAL reading of the internal JP2 affine headers belongs to a
future unit. SRC/DST semantics and EMQ determination are a later separate unit;
EMQ remains UNKNOWN (the index provenance dates 2012-2016 are per-member facts;
the Multi group includes 0840_6959). No DEM preparation, no warp, no provider
profiles, no proof pass. Editions are pinned; drift fails and nothing upgrades.

The per-member `7z x -so` form is deliberate: the 7z CLI never controls output
paths or the filesystem. A batch extraction would re-read each solid stream once
but hands 7z path control and is rejected by default (documented tradeoff).

Modes: default = read-only preflight (guards plus plan, never writes);
--verify-archives = read-only sha256 of the 8 parts against the acquisition hash
manifest; --extract = guarded selective extraction into a fresh or resumable
stage under <root>/extracted; --recover-lock = remove a stale stage lock whose
recorded pid is dead. Rerunning --extract after an exit 3 resumes the single
incomplete stage; a state-complete member with matching size and sha256 is
skipped, a mismatching one fails with repairing nothing.

Exit codes: 0 success; 2 guard or preflight failure; 3 process/extraction
failure (stage preserved, rerun to continue); 4 state, pin, collision, or drift
failure. Python 3 standard library only; 7-Zip is a bounded subprocess.
"""

import argparse
import hashlib
import json
import os
import re
import resource
import selectors
import stat
import subprocess
import sys
import time
from datetime import datetime
from typing import NamedTuple

DEFAULT_ROOT = "/mnt/pit2/panopticon/geo-proof/sedan"
MOUNT_POINT = "/mnt/pit2"
PROC_MOUNTS_PATH = "/proc/mounts"
EXTRACTED_DIR_REL = "extracted"
STATE_VERSION = 1
STATE_FILE_REL = os.path.join("state", "state.json")
LOCK_FILE_REL = os.path.join("state", "lock")
STARTED_NAME = "STARTED"
COMPLETED_NAME = "COMPLETED"
RECEIPT_NAME = "RECEIPT.txt"
HASH_MANIFEST_PATH = os.path.join(
    "~", "log", "track-r", "sedan-download-20261103T0905", "hash-manifest.txt")
RESERVE_BYTES = 1 << 30
CHUNK_SIZE = 1 << 20
LISTING_TIMEOUT_SECONDS = 120
LISTING_OUTPUT_CAP = 32 << 20
MEMBER_TIMEOUT_SECONDS = 300.0
TOTAL_BUDGET_SECONDS = 3600.0
CHILD_MEMORY_LIMIT = 2 << 30
STDERR_CAP = 64 << 10

EXIT_OK = 0
EXIT_GUARD = 2
EXIT_EXTRACT = 3
EXIT_STATE = 4

ALLOWED_SUFFIXES = (".tif", ".jp2", ".xml", ".html", ".tab", ".shp", ".shx", ".dbf", ".prj")
GLOB_CHARS = "*?[]"
SLT_FIELD_RE = re.compile(r"^(?P<key>[^=]+) = (?P<value>.*)$")
STUB_SUFFIX_RE = re.compile(r"\.7z\.\d+$")
STAGE_PREFIX = "sedan-extract-"


class Failure(Exception):
    def __init__(self, code, message):
        super().__init__(message)
        self.code = code
        self.message = message


class ArchiveSpec(NamedTuple):
    key: str
    dir_rel: str
    stem: str
    part_sizes: tuple


class Member(NamedTuple):
    archive: str
    path: str
    declared: int
    optional: bool


class ExtractionResult(NamedTuple):
    got: int
    sha256: str
    returncode: int
    problem: str
    stderr: bytes


RGE_SUB = "RGEALTI_2-0_1M_TIF_LAMB93-IGN69_D008_2019-10-14"
BD_SUB = "BDORTHO_1-0_RVB-0M20_JP2-E080_LAMB93_D008_2022-01-01"
ARCHIVES = (
    ArchiveSpec("RGEALTI", os.path.join("archives", "RGEALTI", RGE_SUB), RGE_SUB,
                (4_294_967_296, 1_304_321_008)),
    ArchiveSpec("BDORTHO", os.path.join("archives", "BDORTHO", BD_SUB), BD_SUB,
                (4_294_967_296,) * 5 + (2_761_389_542,)),
)

RGE_PREFIX = RGE_SUB + "/RGEALTI"
# The archive stem (BDORTHO lot name) and the inner content root (ORTHOHR) differ;
# member paths follow the inner root per the validated 7z catalog evidence.
BD_INNER = "ORTHOHR_1-0_RVB-0M20_JP2-E080_LAMB93_D008_2022-01-01"
BD_PREFIX = BD_INNER + "/ORTHOHR"
RGE_MNT_DIR = RGE_PREFIX + "/1_DONNEES_LIVRAISON_2021-01-00150/RGEALTI_MNT_1M_TIF_LAMB93_IGN69_D008_20210118"
RGE_SRC_DIR = RGE_PREFIX + "/1_DONNEES_LIVRAISON_2021-01-00150/RGEALTI_SRC_1M_TIF_LAMB93_IGN69_D008_20210118"
RGE_DST_DIR = RGE_PREFIX + "/1_DONNEES_LIVRAISON_2021-01-00150/RGEALTI_DST_1M_TIF_LAMB93_IGN69_D008_20210118"
RGE_META_DIR = RGE_PREFIX + "/2_METADONNEES_LIVRAISON_2021-01-00150/RGEALTI_MNT_1M_TIF_LAMB93_IGN69_D008_20210118"
RGE_SUP_DIR = RGE_PREFIX + "/3_SUPPLEMENTS_LIVRAISON_2021-01-00150/RGEALTI_MNT_1M_TIF_LAMB93_IGN69_D008_20210118"
BD_DATA_DIR = BD_PREFIX + "/1_DONNEES_LIVRAISON_2023-06-00036/OHR_RVB_0M20_JP2-E080_LAMB93_D08-2022"
BD_META_DIR = BD_PREFIX + "/2_METADONNEES_LIVRAISON_2023-06-00036/OHR_RVB_0M20_JP2-E080_LAMB93_D08-2022"
BD_SUP_DIR = BD_PREFIX + "/3_SUPPLEMENTS_LIVRAISON_2023-06-00036/OHR_RVB_0M20_JP2-E080_LAMB93_D08-2022"

TILES = ("0839_6958", "0839_6959", "0840_6958", "0840_6959", "0841_6958", "0841_6959")
MNT_DECLARED = {
    "0839_6958": 1_089_059,
    "0839_6959": 1_243_770,
    "0840_6958": 1_199_456,
    "0840_6959": 1_433_927,
    "0841_6958": 1_556_520,
    "0841_6959": 1_583_856,
}
TILE_FILE_SIZE = 1_002_982
BD_TILES = ("0835", "0840")
BD_JP2_DECLARED = {"0835": 94_382_758, "0840": 94_380_240}

SELECTED_MEMBER_COUNT = 27
WITH_DST_MEMBER_COUNT = 33
SELECTED_TOTAL = 205_266_077
DST_TOTAL = 6_017_892
WITH_DST_TOTAL = 211_283_969

RGE_EDITION = (
    "RGEALTI 2-0 1M TIF LAMB93-IGN69 D008 2019-10-14 "
    "(livraison 2021-01-00150; index DBF date 2021-01-20)"
)
BD_EDITION = (
    "ORTHOHR 1-0 RVB-0M20 JP2-E080 LAMB93 D008 2022-01-01 "
    "(livraison 2023-06-00036; index DBF date 2023-06-08)"
)
PROVENANCE_NOTE = "RGE dalles index DBF (in-stage)"
FRAME_NOTE = (
    "frame note: pure-UTM planned extent SW 639841.997/5506768.179 NE 640941.997/5507868.179; "
    "rounded 3x390.144 NE 641012.429/5507938.611; apron named: 1 sample 3.048 m + kernel "
    "half-footprint 1.524 m + imagery bound 1.0 m; a future geographic KML recomputes the origin "
    "from its own vertices, never silent recentring"
)


def selected_members(with_dst):
    """The pinned selection, in extraction order; no other membership exists."""
    members = []
    for tile in TILES:
        members.append(Member(
            "RGEALTI", f"{RGE_MNT_DIR}/RGEALTI_FXX_{tile}_MNT_LAMB93_IGN69.tif",
            MNT_DECLARED[tile], False))
    for tile in TILES:
        members.append(Member(
            "RGEALTI", f"{RGE_SRC_DIR}/RGEALTI_FXX_{tile}_SRC_LAMB93_IGN69.tif",
            TILE_FILE_SIZE, False))
    if with_dst:
        for tile in TILES:
            members.append(Member(
                "RGEALTI", f"{RGE_DST_DIR}/RGEALTI_FXX_{tile}_DST_LAMB93_IGN69.tif",
                TILE_FILE_SIZE, True))
    members.append(Member(
        "RGEALTI", f"{RGE_META_DIR}/IGNF_RGEALTIr_2-0_TIF_1M_LAMB93_IGN69_D008_20210118.xml",
        29_195, False))
    for name, size in (("dalles.dbf", 1_380_070), ("dalles.shp", 753_540),
                       ("dalles.shx", 44_420), ("dalles.prj", 451)):
        members.append(Member("RGEALTI", f"{RGE_SUP_DIR}/{name}", size, False))
    for tile in BD_TILES:
        members.append(Member(
            "BDORTHO", f"{BD_DATA_DIR}/08-2022-{tile}-6960-LA93-0M20-E080.jp2",
            BD_JP2_DECLARED[tile], False))
    for tile in BD_TILES:
        members.append(Member(
            "BDORTHO", f"{BD_DATA_DIR}/08-2022-{tile}-6960-LA93-0M20-E080.tab", 436, False))
    for suffix, size in ((".xml", 35_454), (".html", 28_543)):
        name = f"IGNF_ORTHOHR_1-0_JP2-E080_LAMB93_D08-2022{suffix}"
        members.append(Member("BDORTHO", f"{BD_META_DIR}/{name}", size, False))
    for name, size in (("dalles.dbf", 67_386), ("dalles.shp", 36_004),
                       ("dalles.shx", 2_212), ("dalles.prj", 452)):
        members.append(Member("BDORTHO", f"{BD_SUP_DIR}/{name}", size, False))
    return members


def now_iso():
    return datetime.now().astimezone().isoformat(timespec="milliseconds")


def log(message):
    print(f"{now_iso()} {message}", flush=True)


def fail_guard(message):
    raise Failure(EXIT_GUARD, message)


def fail_state(message):
    raise Failure(EXIT_STATE, message)


def statvfs(path):
    return os.statvfs(path)


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


def archive_for(key):
    for spec in ARCHIVES:
        if spec.key == key:
            return spec
    fail_state(f"no archive spec for {key!r}")


def part_path(root, spec, number):
    return os.path.join(root, spec.dir_rel, f"{spec.stem}.7z.{number:03d}")


def archive_path(root, spec):
    return part_path(root, spec, 1)


def expected_part_keys():
    keys = set()
    for spec in ARCHIVES:
        for number in range(1, len(spec.part_sizes) + 1):
            keys.add(f"{spec.key}/{spec.stem}.7z.{number:03d}")
    return keys


def validate_member_table(with_dst):
    """Pure and first: the pinned table must be exactly the approved selection."""
    members = selected_members(with_dst)
    expected_count = WITH_DST_MEMBER_COUNT if with_dst else SELECTED_MEMBER_COUNT
    expected_total = WITH_DST_TOTAL if with_dst else SELECTED_TOTAL
    if len(members) != expected_count:
        fail_state(f"member table holds {len(members)} entries, expected {expected_count}")
    seen = set()
    for member in members:
        offender = f"member {member.path!r}"
        if os.path.isabs(member.path) or member.path.startswith("/"):
            fail_state(f"{offender} is absolute")
        if "\\" in member.path:
            fail_state(f"{offender} contains a backslash")
        if "\x00" in member.path:
            fail_state(f"{offender} contains a NUL byte")
        components = member.path.split("/")
        if any(component in ("", ".", "..") for component in components):
            fail_state(f"{offender} has an empty, '.', or '..' path component")
        if any(char in member.path for char in GLOB_CHARS):
            fail_state(f"{offender} contains a glob metacharacter")
        if not member.path.endswith(ALLOWED_SUFFIXES):
            fail_state(f"{offender} has a suffix off the allowlist {list(ALLOWED_SUFFIXES)}")
        if member.declared <= 0:
            fail_state(f"{offender} declares non-positive size {member.declared}")
        if member.path in seen:
            fail_state(f"{offender} is declared more than once")
        seen.add(member.path)
    total = sum(member.declared for member in members)
    if total != expected_total:
        fail_state(f"member table totals {total} bytes, expected pinned {expected_total}")
    return members


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


def check_extracted_ancestor(extracted):
    probe = ""
    for component in (piece for piece in extracted.split(os.sep) if piece):
        probe += os.sep + component
        if os.path.islink(probe):
            fail_guard(f"symlink component in the extracted path is not allowed: {probe}")
    ancestor = extracted
    while not os.path.exists(ancestor):
        parent = os.path.dirname(ancestor)
        if parent == ancestor:
            fail_guard("no existing ancestor found for the extracted path")
        ancestor = parent
    if os.path.islink(ancestor):
        fail_guard(f"nearest existing ancestor is a symlink, not a real directory: {ancestor}")
    if not os.path.isdir(ancestor):
        if ancestor == extracted:
            fail_state(f"operator collision: {extracted} exists and is not a directory; left untouched")
        fail_guard(f"nearest existing ancestor is not a real directory: {ancestor}")
    for mode, label in ((os.R_OK, "readable"), (os.W_OK, "writable"), (os.X_OK, "executable")):
        # os.access always grants the superuser, so the writability probe is meaningless there.
        if os.geteuid() == 0 and mode == os.W_OK:
            continue
        if not os.access(ancestor, mode):
            fail_guard(f"nearest existing ancestor {ancestor} is not {label} for the current user")
    log(f"extracted ancestor ok: {ancestor}")
    return ancestor


def check_archive_parts(root):
    for spec in ARCHIVES:
        for number, expected in enumerate(spec.part_sizes, start=1):
            path = part_path(root, spec, number)
            try:
                info = os.lstat(path)
            except OSError:
                fail_guard(f"archive part missing: {path}")
            if os.path.islink(path) or not stat.S_ISREG(info.st_mode):
                fail_guard(f"archive part is not a regular non-symlink file: {path}")
            if info.st_size != expected:
                fail_guard(f"archive part size drift: {path} is {info.st_size} bytes, pinned {expected}")
        log(f"archive ok: {spec.key} ({len(spec.part_sizes)} parts, {sum(spec.part_sizes)} bytes)")


def check_acquisition_state(root):
    path = os.path.join(root, STATE_FILE_REL)
    try:
        with open(path, "r", encoding="utf-8") as handle:
            state = json.load(handle)
    except (OSError, ValueError) as error:
        fail_state(f"acquisition state at {path} cannot be parsed: {error}")
    if not isinstance(state, dict) or state.get("version") != STATE_VERSION:
        fail_state(f"acquisition state at {path} has an unsupported or missing version")
    products = state.get("products")
    if not isinstance(products, dict):
        fail_state(f"acquisition state at {path} has no products object")
    for key in sorted(expected_part_keys()):
        entry = products.get(key)
        if not isinstance(entry, dict):
            fail_guard(f"acquisition state has no entry for {key}; run sedan_acquire.py first")
        if entry.get("status") != "complete":
            fail_guard(f"acquisition state marks {key} status={entry.get('status')!r}; "
                       f"all parts must be complete before extraction")
    log(f"acquisition state ok: {len(expected_part_keys())} parts complete (read-only; never written)")


def run_listing(argv, timeout_seconds):
    """Process seam: one bounded `7z l -slt` invocation; tests replace this function.

    Bounded capture: stdout is spooled only while within LISTING_OUTPUT_CAP and stderr
    only within STDERR_CAP; exceeding either cap, any pipe read error, or the deadline
    kills and reaps the child immediately, before any further parent-side allocation.
    """
    try:
        proc = subprocess.Popen(
            argv, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    except OSError as error:
        fail_guard(f"7z listing failed to start ({error}): {' '.join(argv)}")
    selector = selectors.DefaultSelector()
    selector.register(proc.stdout, selectors.EVENT_READ, "out")
    selector.register(proc.stderr, selectors.EVENT_READ, "err")
    out = bytearray()
    err = bytearray()
    problem = None
    out_open = True
    err_open = True
    deadline = time.monotonic() + timeout_seconds
    while problem is None and (out_open or err_open):
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            problem = "timeout"
            break
        for key, _mask in selector.select(remaining):
            try:
                chunk = os.read(key.fileobj.fileno(), CHUNK_SIZE)
            except OSError:
                problem = "read-error"
                break
            kind = key.data
            if kind == "out":
                if not chunk:
                    out_open = False
                    continue
                sink, cap, label = out, LISTING_OUTPUT_CAP, "oversize"
            else:
                if not chunk:
                    err_open = False
                    continue
                sink, cap, label = err, STDERR_CAP, "stderr-flood"
            if len(sink) + len(chunk) > cap:
                problem = label
                break
            sink.extend(chunk)
    if problem is not None:
        proc.kill()
    try:
        returncode = proc.wait(timeout=10.0)
    except subprocess.TimeoutExpired:
        proc.kill()
        returncode = proc.wait()
    selector.close()
    for stream in (proc.stdout, proc.stderr):
        try:
            stream.close()
        except OSError:
            pass
    if problem == "timeout":
        fail_guard(f"7z listing failure (timeout): exceeded {timeout_seconds} s: {' '.join(argv)}")
    if problem == "oversize":
        fail_guard(f"7z listing failure (oversize): stdout exceeded the {LISTING_OUTPUT_CAP}-byte "
                   f"cap after {len(out)} bytes: {' '.join(argv)}")
    if problem == "stderr-flood":
        fail_guard(f"7z listing failure (stderr-flood): stderr exceeded the {STDERR_CAP}-byte cap "
                   f"after {len(err)} bytes: {' '.join(argv)}")
    if problem == "read-error":
        fail_guard(f"7z listing failure (read-error): os.read failed on a child pipe: {' '.join(argv)}")
    if returncode != 0:
        tail = err[-200:].decode("utf-8", errors="replace").strip()
        suffix = f"; stderr tail: {tail}" if tail else ""
        fail_guard(f"7z listing exited with code {returncode}: {' '.join(argv)}{suffix}")
    return bytes(out)


def parse_listing(text, archive_key):
    text = text.replace("\r\n", "\n")
    entries = []
    for block in re.split(r"\n[ \t]*\n", text.strip()):
        fields = {}
        for line in block.split("\n"):
            match = SLT_FIELD_RE.match(line.strip())
            if match:
                fields[match.group("key")] = match.group("value")
        if fields.get("Type") == "Split":
            continue  # archive-info block, not a member
        path = fields.get("Path")
        if path is None:
            continue
        if path.endswith(".7z") or STUB_SUFFIX_RE.search(path):
            continue  # logical archive stub, not a member
        size_text = fields.get("Size")
        if size_text is None or not size_text.isdigit():
            fail_guard(f"7z listing of {archive_key} has a block for {path!r} without a parseable Size")
        attributes = fields.get("Attributes", "")
        tokens = attributes.split()
        entries.append({"path": path, "size": int(size_text), "is_dir": bool(tokens) and "D" in tokens[0]})
    return entries


def check_listings(root, members):
    by_archive = {}
    for member in members:
        by_archive.setdefault(member.archive, []).append(member)
    for key, archive_members in by_archive.items():
        spec = archive_for(key)
        output = run_listing(["7z", "l", "-slt", archive_path(root, spec)], LISTING_TIMEOUT_SECONDS)
        entries = parse_listing(output.decode("utf-8", errors="replace"), key)
        for member in archive_members:
            matches = [entry for entry in entries if entry["path"] == member.path]
            if not matches:
                fail_state(f"pin drift: member missing from the 7z listing: {member.path}")
            if len(matches) > 1:
                fail_state(f"pin drift: member listed {len(matches)} times in the 7z listing: {member.path}")
            entry = matches[0]
            if entry["is_dir"]:
                fail_state(f"pin drift: member is a directory entry in the 7z listing: {member.path}")
            if entry["size"] != member.declared:
                fail_state(f"pin drift: {member.path} listing size {entry['size']} disagrees with "
                           f"pinned {member.declared}")
        log(f"listing ok: {key}: {len(archive_members)} pinned members present exactly once "
            f"with pinned sizes")


def check_free_space(ancestor, selected_total, owned_bytes):
    usage = statvfs(ancestor)
    free = usage.f_bavail * usage.f_frsize
    required = selected_total - owned_bytes + RESERVE_BYTES
    log(f"free space: free={free} required={required} "
        f"(selected {selected_total} - verified owned {owned_bytes} + reserve {RESERVE_BYTES})")
    if free < required:
        fail_guard(f"insufficient free space on {ancestor}: free={free} required={required} "
                   f"remaining={required - free}")


def extracted_dir(root):
    return os.path.join(root, EXTRACTED_DIR_REL)


def local_stamp():
    return datetime.now().astimezone().isoformat(timespec="milliseconds").replace(":", ".")


def stage_state_path(stage):
    return os.path.join(stage, STATE_FILE_REL)


def write_state(state_path, state):
    tmp_path = state_path + ".tmp"
    with open(tmp_path, "w", encoding="utf-8") as handle:
        json.dump(state, handle, indent=2, sort_keys=True)
        handle.write("\n")
        handle.flush()
        os.fsync(handle.fileno())
    os.replace(tmp_path, state_path)


def find_incomplete_stage(extracted):
    if not os.path.lexists(extracted):
        return None
    if not os.path.isdir(extracted) or os.path.islink(extracted):
        fail_state(f"operator collision: {extracted} exists and is not a real directory; left untouched")
    incomplete = []
    with os.scandir(extracted) as entries:
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
        fail_state(f"multiple incomplete stages under {extracted}; resolve them manually: "
                   f"{sorted(incomplete)}")
    return incomplete[0] if incomplete else None


def load_stage_state(stage):
    path = stage_state_path(stage)
    try:
        with open(path, "r", encoding="utf-8") as handle:
            state = json.load(handle)
    except (OSError, ValueError) as error:
        fail_state(f"stage state at {path} cannot be parsed: {error}")
    if not isinstance(state, dict) or state.get("version") != STATE_VERSION:
        fail_state(f"stage state at {path} has an unsupported or missing version")
    if not isinstance(state.get("members"), dict):
        fail_state(f"stage state at {path} has no members object")
    return state, path


def check_stage_selection(state, members, with_dst, stage):
    same = (state.get("with_dst") == with_dst
            and state.get("member_count") == len(members)
            and state.get("total_declared") == sum(member.declared for member in members))
    if not same:
        fail_state(f"stage {stage} was started with a different selection; stages are never "
                   f"upgraded or mixed")


def verify_completed_members(stage, state, members):
    records = state["members"]
    by_path = {member.path: member for member in members}
    owned = 0
    for path in sorted(records):
        record = records[path]
        if record.get("status") != "complete":
            continue
        member = by_path.get(path)
        if member is None:
            fail_state(f"stage state marks a member outside the pinned selection complete: {path}")
        file_rel = record.get("file")
        if not isinstance(file_rel, str):
            fail_state(f"corrupted stage record for {path}: no file")
        file_abs = os.path.join(stage, file_rel)
        if os.path.islink(file_abs) or not os.path.isfile(file_abs):
            fail_state(f"state marks {path} complete but its file is missing or not regular: {file_abs}")
        info = os.lstat(file_abs)
        if info.st_size != member.declared:
            fail_state(f"completed member fails verification (size {info.st_size} != pinned "
                       f"{member.declared}); repairing nothing: {path}")
        digest = hashlib.sha256()
        with open(file_abs, "rb") as handle:
            while True:
                block = handle.read(CHUNK_SIZE)
                if not block:
                    break
                digest.update(block)
        if digest.hexdigest() != record.get("sha256"):
            fail_state(f"completed member fails verification (sha256 mismatch); repairing nothing: {path}")
        owned += member.declared
        log(f"verified complete member: {path} ({member.declared} bytes, sha256 ok)")
    return owned


def read_lock(lock_path):
    try:
        with open(lock_path, "r", encoding="utf-8") as handle:
            text = handle.read().strip()
    except OSError as error:
        fail_state(f"unreadable lock at {lock_path}: {error}")
    fields = text.split(None, 1)
    if len(fields) != 2 or not fields[0].isdigit():
        fail_state(f"unparseable lock at {lock_path}; after confirming no extraction is running, "
                   f"resolve it manually")
    return int(fields[0]), fields[1]


def acquire_lock(lock_path):
    if os.path.exists(lock_path):
        pid, recorded = read_lock(lock_path)
        if pid_alive(pid):
            fail_state(f"extraction lock at {lock_path} is held by live pid {pid} (recorded {recorded})")
        fail_state(f"stale extraction lock at {lock_path} (dead pid {pid}, recorded {recorded}); "
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


def limit_child_memory():
    resource.setrlimit(resource.RLIMIT_AS, (CHILD_MEMORY_LIMIT, CHILD_MEMORY_LIMIT))


class ChildReader:
    """Bounded stdout/stderr reader around one `7z x -so` child process."""

    def __init__(self, proc):
        self.proc = proc
        self.selector = selectors.DefaultSelector()
        self.selector.register(proc.stdout, selectors.EVENT_READ, "out")
        self.selector.register(proc.stderr, selectors.EVENT_READ, "err")
        self.stderr = bytearray()

    def read_batch(self, timeout_seconds):
        """Every ready (kind, data) event of one select batch, in order.

        An EOF unregisters its fd: a closed pipe is level-triggered ready forever,
        so leaving it registered would starve the other stream's events sitting
        behind it in every select batch.
        """
        batch = []
        for key, _mask in self.selector.select(timeout_seconds):
            chunk = os.read(key.fileobj.fileno(), CHUNK_SIZE)
            if chunk:
                if key.data == "err":
                    room = max(STDERR_CAP - len(self.stderr), 0)
                    self.stderr.extend(chunk[:room])
                batch.append((key.data, chunk))
            else:
                self.selector.unregister(key.fileobj)
                batch.append((key.data + "-eof", b""))
        return batch

    def kill(self):
        self.proc.kill()

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


def open_member_stream(argv):
    """Process seam: one `7z x -so <archive> <member>`; tests replace this function.

    Per-member -so is chosen so the 7z CLI never controls output paths or the
    filesystem; a batch alternative would re-read solid streams once but hands
    7z path control and is rejected by default (documented tradeoff).
    """
    try:
        proc = subprocess.Popen(
            argv, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            preexec_fn=limit_child_memory)
    except OSError as error:
        fail_guard(f"7z extraction failed to start: {error}")
    return ChildReader(proc)


def write_all(fd, data):
    view = memoryview(data)
    while view:
        view = view[os.write(fd, view):]


def pump_member(reader, out_fd, declared):
    """Stream one member; one explicit attempt, no retries, stop on first failure."""
    digest = hashlib.sha256()
    got = 0
    problem = None
    out_eof = False
    err_eof = False
    deadline = time.monotonic() + MEMBER_TIMEOUT_SECONDS
    while problem is None and not (out_eof and err_eof):
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            problem = "timeout"
            break
        for kind, data in reader.read_batch(remaining):
            if kind == "out":
                if got + len(data) > declared:
                    room = max(declared + 1 - got, 0)
                    piece = data[:room]
                    if piece:
                        write_all(out_fd, piece)
                        digest.update(piece)
                        got += len(piece)
                    problem = "overrun"
                    break
                write_all(out_fd, data)
                digest.update(data)
                got += len(data)
            elif kind == "out-eof":
                out_eof = True
            elif kind == "err-eof":
                err_eof = True
        if problem is None and not (out_eof and err_eof) and time.monotonic() > deadline:
            problem = "timeout"
    if problem is not None:
        reader.kill()
    elif deadline - time.monotonic() <= 0:
        problem = "timeout"
        reader.kill()
    code = reader.wait(max(deadline - time.monotonic(), 0))
    return ExtractionResult(got, digest.hexdigest(), code, problem, bytes(reader.stderr))


def extract_one(root, member, target_path):
    spec = archive_for(member.archive)
    try:
        out_fd = os.open(target_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o644)
    except FileExistsError:
        fail_state(f"target file already exists; refusing to overwrite: {target_path}")
    try:
        reader = open_member_stream(["7z", "x", "-so", archive_path(root, spec), member.path])
        try:
            result = pump_member(reader, out_fd, member.declared)
        finally:
            reader.close()
        if result.problem is None and result.returncode == 0 and result.got == member.declared:
            os.fsync(out_fd)
        return result
    finally:
        os.close(out_fd)


def files_rel_dir(number, attempt, suffix):
    return f"{number}_{suffix}" if attempt == 1 else f"{number}_{attempt}_{suffix}"


def init_stage(stage, members, with_dst):
    os.mkdir(os.path.join(stage, "state"), 0o755)
    os.mkdir(os.path.join(stage, "files"), 0o755)
    state = {
        "version": STATE_VERSION,
        "with_dst": with_dst,
        "member_count": len(members),
        "total_declared": sum(member.declared for member in members),
        "members": {},
    }
    write_state(stage_state_path(stage), state)
    fd = os.open(os.path.join(stage, STARTED_NAME), os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o644)
    with os.fdopen(fd, "w", encoding="utf-8") as handle:
        handle.write(f"{os.getpid()} {now_iso()} extract members={len(members)} "
                     f"declared={state['total_declared']}\n")


def ensure_extracted_dir(extracted):
    try:
        os.mkdir(extracted, 0o755)
        log(f"created extracted directory: {extracted}")
    except FileExistsError:
        if os.path.islink(extracted) or not os.path.isdir(extracted):
            fail_state(f"operator collision: {extracted} exists and is not a real directory; "
                       f"left untouched")


def create_stage(extracted, members, with_dst):
    stage = os.path.join(extracted, STAGE_PREFIX + local_stamp())
    try:
        os.mkdir(stage, 0o755)
    except FileExistsError:
        fail_state(f"stage directory already exists; refusing to reuse or overwrite "
                   f"(stamp collision): {stage}")
    init_stage(stage, members, with_dst)
    log(f"stage created: {stage}")
    return stage


def receipt_lines(members, records):
    lines = ["# Sedan selective extraction receipt", f"finished: {now_iso()}",
             f"members: {len(members)}"]
    for member in members:
        record = records[member.path]
        lines.append(f"member: {member.archive} {member.path} declared={member.declared} "
                     f"got={record['got']} sha256={record['sha256']}")
    lines.append(f"total declared: {sum(member.declared for member in members)}")
    lines.append(f"total got: {sum(records[member.path]['got'] for member in members)}")
    lines.append(f"edition: {RGE_EDITION}")
    lines.append(f"edition: {BD_EDITION}")
    lines.append(f"acquisition integrity record: {os.path.expanduser(HASH_MANIFEST_PATH)}")
    lines.append(f"provenance: {PROVENANCE_NOTE}")
    lines.append(FRAME_NOTE)
    return lines


def write_receipt(stage, members, records):
    fd = os.open(os.path.join(stage, RECEIPT_NAME), os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o644)
    with os.fdopen(fd, "w", encoding="utf-8") as handle:
        handle.write("\n".join(receipt_lines(members, records)) + "\n")
        handle.flush()
        os.fsync(handle.fileno())


def write_completed_marker(stage):
    tmp_path = os.path.join(stage, COMPLETED_NAME + ".tmp")
    fd = os.open(tmp_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o644)
    with os.fdopen(fd, "w", encoding="utf-8") as handle:
        handle.write(f"completed {now_iso()}\n")
        handle.flush()
        os.fsync(handle.fileno())
    os.replace(tmp_path, os.path.join(stage, COMPLETED_NAME))
    dir_fd = os.open(stage, os.O_RDONLY)
    try:
        os.fsync(dir_fd)
    finally:
        os.close(dir_fd)


def extract_members(root, stage, state, state_path, members):
    records = state["members"]
    budget_start = time.monotonic()
    for number, member in enumerate(members, start=1):
        record = records.get(member.path)
        if record is not None and record.get("status") == "complete":
            log(f"skipping complete member (already sha256-verified): {member.path}")
            continue
        if time.monotonic() - budget_start > TOTAL_BUDGET_SECONDS:
            raise Failure(EXIT_EXTRACT, f"wall budget {TOTAL_BUDGET_SECONDS} s exhausted before "
                                        f"member {number}/{len(members)}; stage preserved; rerun to continue")
        attempt = (record.get("attempt", 0) + 1) if record is not None else 1
        suffix = member.path.rsplit(".", 1)[-1]
        rel_dir = files_rel_dir(number, attempt, suffix)
        while os.path.lexists(os.path.join(stage, "files", rel_dir)):
            attempt += 1
            rel_dir = files_rel_dir(number, attempt, suffix)
        os.mkdir(os.path.join(stage, "files", rel_dir), 0o755)
        target_rel = os.path.join("files", rel_dir, os.path.basename(member.path))
        target_abs = os.path.join(stage, target_rel)
        log(f"extracting ({number}/{len(members)}): {member.path} declared={member.declared}")
        result = extract_one(root, member, target_abs)
        if result.problem is None and result.returncode == 0 and result.got == member.declared:
            records[member.path] = {
                "archive": member.archive, "declared": member.declared, "attempt": attempt,
                "file": target_rel, "got": result.got, "sha256": result.sha256,
                "status": "complete",
            }
            write_state(state_path, state)
            log(f"complete: {member.path} got={result.got} sha256={result.sha256}")
            continue
        records[member.path] = {
            "archive": member.archive, "declared": member.declared, "attempt": attempt,
            "file": target_rel, "got": result.got, "status": "partial",
        }
        write_state(state_path, state)
        if result.problem == "overrun":
            fail_state(f"overlong stream beyond the pinned {member.declared} bytes (drift); "
                       f"partial kept, never deleted: {member.path}")
        detail = result.problem
        if detail is None:
            detail = (f"byte count {result.got} != pinned {member.declared}"
                      if result.got != member.declared else f"7z exit code {result.returncode}")
        if result.stderr:
            tail = result.stderr[-200:].decode("utf-8", errors="replace").strip()
            detail += f"; stderr tail: {tail}"
        raise Failure(EXIT_EXTRACT, f"member {number}/{len(members)} failed: {detail}; stage "
                                    f"preserved; rerun to continue: {member.path}")
    write_receipt(stage, members, records)
    write_completed_marker(stage)
    log(f"all {len(members)} members complete; receipt and COMPLETED marker written")


def run_extract(root, with_dst):
    members = validate_member_table(with_dst)
    total_declared = sum(member.declared for member in members)
    log(f"plan: extract {len(members)} pinned members, {total_declared} bytes (with_dst={with_dst})")
    check_mount_realness(root)
    extracted = os.path.normpath(extracted_dir(root))
    ancestor = check_extracted_ancestor(extracted)
    check_archive_parts(root)
    check_acquisition_state(root)
    check_listings(root, members)
    stage = find_incomplete_stage(extracted)
    if stage is None:
        check_free_space(ancestor, total_declared, 0)
        ensure_extracted_dir(extracted)
        stage = create_stage(extracted, members, with_dst)
        state, state_path = load_stage_state(stage)
    else:
        log(f"resuming incomplete stage: {stage}")
        state, state_path = load_stage_state(stage)
        check_stage_selection(state, members, with_dst, stage)
        owned = verify_completed_members(stage, state, members)
        check_free_space(ancestor, total_declared, owned)
    lock_path = os.path.join(stage, LOCK_FILE_REL)
    acquire_lock(lock_path)
    try:
        extract_members(root, stage, state, state_path, members)
    finally:
        release_lock(lock_path)


def parse_hash_manifest(path):
    try:
        with open(path, "r", encoding="utf-8") as handle:
            lines = handle.read().splitlines()
    except OSError as error:
        fail_guard(f"hash manifest cannot be read at {path}: {error}")
    recorded = {}
    for number, line in enumerate(lines, start=1):
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        fields = stripped.split()
        if len(fields) != 3 or not re.fullmatch(r"[0-9a-f]{64}", fields[0]) or not fields[1].isdigit():
            fail_guard(f"hash manifest line {number} is not '<sha256> <size> <name>': {stripped!r}")
        recorded[fields[2]] = (fields[0], int(fields[1]))
    return recorded


def run_verify_archives(root, source):
    log(f"cache root: {root} (source: {source})")
    log("verify-archives: read-only sha256 of the 8 parts against the acquisition hash manifest "
        "(never automatic)")
    check_archive_parts(root)
    recorded = parse_hash_manifest(os.path.expanduser(HASH_MANIFEST_PATH))
    for spec in ARCHIVES:
        for number, expected_size in enumerate(spec.part_sizes, start=1):
            name = f"{spec.stem}.7z.{number:03d}"
            path = part_path(root, spec, number)
            entry = recorded.get(name)
            if entry is None:
                fail_state(f"hash manifest has no line for {name}; refusing to pass unrecorded parts")
            if entry[1] != expected_size:
                fail_state(f"hash manifest size for {name} is {entry[1]}, pinned {expected_size}")
            digest = hashlib.sha256()
            with open(path, "rb") as handle:
                while True:
                    block = handle.read(CHUNK_SIZE)
                    if not block:
                        break
                    digest.update(block)
            if digest.hexdigest() != entry[0]:
                fail_state(f"sha256 mismatch for {name}: manifest {entry[0]} "
                           f"actual {digest.hexdigest()}")
            log(f"verified: {name} sha256 ok ({expected_size} bytes)")
    log(f"all {len(expected_part_keys())} parts match the acquisition hash manifest")


def run_recover(root, source):
    log(f"cache root: {root} (source: {source})")
    check_mount_realness(root)
    extracted = os.path.normpath(extracted_dir(root))
    check_extracted_ancestor(extracted)
    stage = find_incomplete_stage(extracted)
    if stage is None:
        log("no incomplete stage found; nothing to recover")
        return
    recover_lock(os.path.join(stage, LOCK_FILE_REL))


def run_preflight(root, source, with_dst):
    members = validate_member_table(with_dst)
    total_declared = sum(member.declared for member in members)
    log(f"cache root: {root} (source: {source})")
    log(f"plan: {len(members)} pinned members, {total_declared} bytes (with_dst={with_dst})")
    for member in members:
        marker = " (optional DST, excluded from this run)" if member.optional else ""
        log(f"pinned member: {member.archive} {member.path} {member.declared}{marker}")
    check_mount_realness(root)
    extracted = os.path.normpath(extracted_dir(root))
    ancestor = check_extracted_ancestor(extracted)
    check_archive_parts(root)
    check_acquisition_state(root)
    check_listings(root, members)
    stage = find_incomplete_stage(extracted)
    if stage is not None:
        log(f"incomplete stage present (resume target for --extract): {stage}")
    check_free_space(ancestor, total_declared, 0)
    log("preflight complete; nothing was written, no directory was created, no network was touched")


def resolve_raw_root(args):
    if args.root:
        return args.root, "positional"
    env_value = os.environ.get("PANOPTICON_GEO_PROOF_DIR")
    if env_value:
        return env_value, "env PANOPTICON_GEO_PROOF_DIR"
    return DEFAULT_ROOT, "default"


def build_parser():
    parser = argparse.ArgumentParser(
        prog="sedan_extract.py",
        description=(
            "Selective extraction helper for the Sedan geo-proof cache: extracts only the approved "
            "27-member pinned selection (33 with --with-dst) from the two acquired split 7z "
            "archives. Byte copy plus sha256 only; no raster decoding, no DEM heights, no "
            "geotransforms."
        ),
        epilog=(
            f"cache root resolution order: 1) positional argument, 2) env "
            f"PANOPTICON_GEO_PROOF_DIR, 3) default {DEFAULT_ROOT}. Modes: default = read-only "
            f"preflight plus plan (never writes); --verify-archives = read-only sha256 of the 8 "
            f"parts against the acquisition hash manifest; --extract = guarded selective "
            f"extraction of the pinned members only; --with-dst adds the 6 optional pinned RGE "
            f"DST members to --extract (listed as excluded in the preflight plan); "
            f"--recover-lock removes a stale stage lock whose recorded pid is no longer alive. "
            f"Exit codes: 0 success; 2 guard or preflight failure; 3 process/extraction failure "
            f"(stage preserved, rerun resumes); 4 state, pin, collision, or drift failure."
        ),
    )
    parser.add_argument(
        "root", nargs="?", default=None,
        help=(f"cache root; resolution order: positional > env PANOPTICON_GEO_PROOF_DIR > "
              f"default {DEFAULT_ROOT}"))
    parser.add_argument(
        "--extract", action="store_true",
        help="runtime-authorized selective extraction; requires every guard to pass")
    parser.add_argument(
        "--with-dst", action="store_true",
        help="add the 6 optional pinned RGE DST members (only meaningful with --extract)")
    parser.add_argument(
        "--verify-archives", action="store_true",
        help="read-only: sha256 the 8 parts against the acquisition hash manifest")
    parser.add_argument(
        "--recover-lock", action="store_true",
        help="remove a stale stage lock only when its recorded pid is no longer alive")
    return parser


def main(argv=None):
    parser = build_parser()
    args = parser.parse_args(argv)
    modes = [name for name, given in (
        ("extract", args.extract),
        ("verify-archives", args.verify_archives),
        ("recover-lock", args.recover_lock)) if given]
    if len(modes) > 1:
        parser.error(f"{' and '.join(modes)} cannot be combined")
    raw, source = resolve_raw_root(args)
    mode = modes[0] if modes else "preflight"
    code = EXIT_OK
    try:
        if args.recover_lock:
            run_recover(raw, source)
        elif args.verify_archives:
            run_verify_archives(raw, source)
        elif args.extract:
            run_extract(raw, args.with_dst)
        else:
            run_preflight(raw, source, args.with_dst)
    except Failure as failure:
        log(f"FAIL({failure.code}): {failure.message}")
        code = failure.code
    log(f"final status: exit={code} mode={mode} root={raw}")
    return code


if __name__ == "__main__":
    sys.exit(main())
