#!/usr/bin/env python3
"""Sedan geo-proof acquisition helper (WI-R2-01 preparation).

Acquisition stage of the future card-mandated `prepare-sedan-proof.sh <work dir>` shell
entry (WI-R2-01); downloads and verifies the two pinned IGN archives. Extraction and its
exact uncompressed-space guard (7z listing before extract) are deliberately NOT
implemented here and must not be claimed by this helper. Editions are pinned per
~/log/track-r/sedan-provider-discovery.md (data.geopf.fr official, session-verified
2026-11-02 via Atom metadata plus HEAD Content-Length). Drift fails; this helper never
upgrades to another edition.

Python 3 standard library only. No credentials are ever sent or stored; HTTP 401/403
fails closed with a no-account message. The default preflight mode never writes and
never touches the network.

Exit codes: 0 success; 2 guard or preflight failure; 3 download/network failure
(resumable); 4 state or drift failure (edition drift, corrupted state, operator
collision).
"""

import argparse
import hashlib
import http.client
import json
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime
from typing import NamedTuple

DEFAULT_WORKDIR = "/mnt/pit2/panopticon/geo-proof/sedan"
DEFAULT_PREFIX = "/mnt/pit2"
PROC_MOUNTS_PATH = "/proc/mounts"
# Bookkeeping reserve on top of archive bytes: state files, lock, extraction headroom.
RESERVE_BYTES = 1 << 30
CHUNK_SIZE = 1 << 20
STATE_VERSION = 1
STATE_FILE_REL = os.path.join("state", "state.json")
LOCK_FILE_REL = os.path.join("state", "lock")
DEFAULT_TIMEOUT_SECONDS = 60.0
# The Géoplateforme CDN rejects the default urllib User-Agent with HTTP 403 (public
# data, no credentials involved), so requests identify themselves explicitly.
USER_AGENT = (
    "Panopticon-geo-acquire/1.0 (IGN Licence Ouverte 2.0 data preparation; contact via repository)"
)
CONTENT_RANGE_RE = re.compile(r"bytes (\d+)-(\d+)/(\d+)")
REQUIRED_ENTRY_KEYS = {"url", "expected_size", "bytes_on_disk", "status", "mtime_ns"}

EXIT_OK = 0
EXIT_GUARD = 2
EXIT_NETWORK = 3
EXIT_STATE = 4

NETWORK_ERRORS = (
    urllib.error.URLError,
    TimeoutError,
    ConnectionError,
    OSError,
    http.client.HTTPException,
)


class Product(NamedTuple):
    resource: str
    subresource: str
    part_sizes: tuple


class Part(NamedTuple):
    key: str
    url: str
    expected_size: int
    rel_path: str


PRODUCTS = (
    Product(
        resource="RGEALTI",
        subresource="RGEALTI_2-0_1M_TIF_LAMB93-IGN69_D008_2019-10-14",
        part_sizes=(4_294_967_296, 1_304_321_008),
    ),
    Product(
        resource="BDORTHO",
        subresource="BDORTHO_1-0_RVB-0M20_JP2-E080_LAMB93_D008_2022-01-01",
        part_sizes=(4_294_967_296,) * 5 + (2_761_389_542,),
    ),
)

# Aggregate denominator, derived from the pinned per-part sizes (single source of
# truth); each part size is HEAD-verified ground truth.
TOTAL_PINNED_BYTES = sum(size for product in PRODUCTS for size in product.part_sizes)


class Failure(Exception):
    def __init__(self, code, message):
        super().__init__(message)
        self.code = code
        self.message = message


def now_iso():
    return datetime.now().astimezone().isoformat(timespec="milliseconds")


def log(message):
    print(f"{now_iso()} {message}", flush=True)


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


class SameHostRedirectHandler(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        origin = urllib.parse.urlsplit(req.full_url)
        target = urllib.parse.urlsplit(newurl)
        if origin[:2] != target[:2]:
            raise urllib.error.URLError(f"cross-host redirect refused: {req.full_url} -> {newurl}")
        return super().redirect_request(req, fp, code, msg, headers, newurl)


OPENER = urllib.request.build_opener(SameHostRedirectHandler())


def open_url(url, method="GET", headers=None):
    """Network seam: TLS default-verified, timeout-bound; tests replace this function."""
    sent_headers = dict(headers or {})
    sent_headers.setdefault("User-Agent", USER_AGENT)
    request = urllib.request.Request(url, headers=sent_headers, method=method)
    return OPENER.open(request, timeout=DEFAULT_TIMEOUT_SECONDS)


def build_parts():
    parts = []
    for product in PRODUCTS:
        base = (
            "https://data.geopf.fr/telechargement/download/"
            f"{product.resource}/{product.subresource}/{product.subresource}.7z"
        )
        for number, size in enumerate(product.part_sizes, start=1):
            name = f"{product.subresource}.7z.{number:03d}"
            parts.append(
                Part(
                    key=f"{product.resource}/{name}",
                    url=f"{base}.{number:03d}",
                    expected_size=size,
                    rel_path=os.path.join("archives", product.resource, product.subresource, name),
                )
            )
    return parts


def fail_guard(message):
    raise Failure(EXIT_GUARD, message)


def check_path_safety(raw):
    if not raw or not raw.strip():
        fail_guard("workdir is empty")
    if "\x00" in raw:
        fail_guard("workdir contains a NUL byte")
    if ".." in raw.replace(os.sep, "/").split("/"):
        fail_guard("workdir must not contain a '..' path component")
    if not os.path.isabs(raw):
        fail_guard(
            f"workdir must be an absolute path (got {raw!r}); resolution order is "
            f"positional > env PANOPTICON_GEO_PROOF_DIR > default {DEFAULT_WORKDIR}"
        )
    normalized = os.path.normpath(raw)
    if ".." in normalized.split(os.sep):
        fail_guard("workdir still contains a '..' component after normalization")
    if normalized == os.sep:
        fail_guard("refusing the filesystem root as workdir")
    if os.path.isfile(normalized):
        fail_guard(f"workdir exists and is a regular file: {normalized}")
    probe = os.sep
    for component in (piece for piece in normalized.split(os.sep) if piece):
        probe = os.path.join(probe, component)
        if os.path.islink(probe):
            fail_guard(f"symlink in workdir path is not allowed: {probe}")
    return normalized


def read_mounts(path=None):
    mounts_path = path or PROC_MOUNTS_PATH
    entries = []
    with open(mounts_path, "r", encoding="utf-8") as handle:
        for line in handle:
            fields = line.split()
            if len(fields) >= 4:
                entries.append((fields[0], fields[1], fields[2], fields[3].split(",")))
    return entries


def is_under_prefix(workdir, prefix):
    return workdir == prefix or workdir.startswith(prefix + os.sep)


def check_mount_realness(workdir):
    if not is_under_prefix(workdir, DEFAULT_PREFIX):
        log(
            f"mount realness NOT verified: {workdir} is not under the default prefix "
            f"{DEFAULT_PREFIX}"
        )
        return False
    for device, mount_point, fstype, options in read_mounts():
        if mount_point == DEFAULT_PREFIX and "rw" in options:
            log(f"mount realness verified: {DEFAULT_PREFIX} is a rw mount ({fstype}, device {device})")
            return True
    fail_guard(
        f"requires an exact rw mount entry with mount point {DEFAULT_PREFIX} in "
        f"{PROC_MOUNTS_PATH}: refusing to create anything on a possibly-unmounted path "
        f"(no -T style fallback, no faked mounts, no chmod/chown)"
    )


def check_ancestor(workdir):
    ancestor = workdir
    while not os.path.exists(ancestor):
        parent = os.path.dirname(ancestor)
        if parent == ancestor:
            fail_guard("no existing ancestor found")
        ancestor = parent
    if os.path.islink(ancestor):
        fail_guard(f"nearest existing ancestor is a symlink, not a real directory: {ancestor}")
    if not os.path.isdir(ancestor):
        fail_guard(f"nearest existing ancestor is not a real directory: {ancestor}")
    for mode, label in ((os.R_OK, "readable"), (os.W_OK, "writable"), (os.X_OK, "executable")):
        if not os.access(ancestor, mode):
            fail_guard(f"nearest existing ancestor {ancestor} is not {label} for the current user")
    ancestor_device = os.lstat(ancestor).st_dev
    final_parent = os.path.dirname(workdir)
    if os.path.lexists(final_parent):
        if os.path.islink(final_parent):
            fail_guard(f"final path parent is a symlink: {final_parent}")
        if os.lstat(final_parent).st_dev != ancestor_device:
            fail_guard(
                f"device mismatch: ancestor {ancestor} is not on the same device as {final_parent}"
            )
    log(f"ancestor ok: {ancestor} (device {ancestor_device}, readable+writable+executable)")
    return ancestor, ancestor_device


def check_free_space(ancestor, owned_bytes):
    usage = statvfs(ancestor)
    free = usage.f_bavail * usage.f_frsize
    required = TOTAL_PINNED_BYTES - owned_bytes + RESERVE_BYTES
    log(
        f"free space: free={free} required={required} "
        f"(pinned {TOTAL_PINNED_BYTES} - owned {owned_bytes} + reserve {RESERVE_BYTES})"
    )
    if free < required:
        fail_guard(
            f"insufficient free space on {ancestor}: free={free} required={required} "
            f"remaining={required - free}"
        )
    return free, required


def state_paths(workdir):
    return os.path.join(workdir, STATE_FILE_REL), os.path.join(workdir, LOCK_FILE_REL)


def part_disk_path(workdir, part):
    return os.path.join(workdir, part.rel_path)


def load_state(state_path):
    if not os.path.exists(state_path):
        return {"version": STATE_VERSION, "products": {}}
    try:
        with open(state_path, "r", encoding="utf-8") as handle:
            state = json.load(handle)
    except (OSError, ValueError) as error:
        raise Failure(EXIT_STATE, f"corrupted state at {state_path}: {error}; nothing will be adopted")
    if not isinstance(state, dict) or state.get("version") != STATE_VERSION:
        raise Failure(
            EXIT_STATE,
            f"state at {state_path} has an unsupported or missing version; nothing will be adopted",
        )
    products = state.get("products")
    if not isinstance(products, dict):
        raise Failure(EXIT_STATE, f"corrupted state at {state_path}: products is not an object")
    for key, entry in products.items():
        if not isinstance(entry, dict) or not REQUIRED_ENTRY_KEYS.issubset(entry):
            raise Failure(EXIT_STATE, f"corrupted state at {state_path}: malformed entry {key!r}")
        if entry["status"] not in ("partial", "complete"):
            raise Failure(EXIT_STATE, f"corrupted state at {state_path}: bad status in {key!r}")
        if not isinstance(entry["expected_size"], int) or not isinstance(entry["bytes_on_disk"], int):
            raise Failure(EXIT_STATE, f"corrupted state at {state_path}: bad sizes in {key!r}")
    return state


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
        raise Failure(EXIT_STATE, f"unreadable lock at {lock_path}: {error}")
    fields = text.split(None, 1)
    if len(fields) != 2 or not fields[0].isdigit():
        raise Failure(
            EXIT_STATE,
            f"unparseable lock at {lock_path}; after confirming no acquisition is running, "
            f"remove it manually",
        )
    return int(fields[0]), fields[1]


def acquire_lock(lock_path):
    if os.path.exists(lock_path):
        pid, recorded = read_lock(lock_path)
        if pid_alive(pid):
            raise Failure(
                EXIT_STATE,
                f"acquisition lock at {lock_path} is held by live pid {pid} (recorded {recorded})",
            )
        raise Failure(
            EXIT_STATE,
            f"stale acquisition lock at {lock_path} (dead pid {pid}, recorded {recorded}); "
            f"rerun with --recover-lock",
        )
    os.makedirs(os.path.dirname(lock_path), exist_ok=True)
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
        raise Failure(
            EXIT_STATE,
            f"lock at {lock_path} is held by live pid {pid} (recorded {recorded}); refusing to remove",
        )
    os.unlink(lock_path)
    log(f"stale lock removed: {lock_path} (dead pid {pid}, recorded {recorded})")


def scan_ownership(workdir, parts, state):
    """Read-only pass; returns {key: adoptable bytes on disk}.

    A part file is resumable only with a matching owned state entry. An existing file
    without state, or beyond the pinned size, is an operator collision: exit 4, file
    untouched. Corrupted state entries never adopt anything.
    """
    adoption = {}
    products = state["products"]
    for part in parts:
        path = part_disk_path(workdir, part)
        entry = products.get(part.key)
        if not os.path.lexists(path):
            if entry is not None and entry["status"] == "complete":
                raise Failure(
                    EXIT_STATE, f"state marks {part.key} complete but the file is missing: {path}"
                )
            adoption[part.key] = 0
            continue
        if entry is None:
            raise Failure(
                EXIT_STATE,
                f"operator collision: file exists without owned state; left untouched: {path}",
            )
        if os.path.islink(path) or not os.path.isfile(path):
            raise Failure(
                EXIT_STATE, f"operator collision: {path} is not a regular file; left untouched"
            )
        if entry["url"] != part.url or entry["expected_size"] != part.expected_size:
            raise Failure(
                EXIT_STATE,
                f"edition drift in state for {part.key}: state records "
                f"url={entry['url']} expected={entry['expected_size']} but pinning says "
                f"url={part.url} expected={part.expected_size}; pinned editions only",
            )
        if entry["bytes_on_disk"] > part.expected_size:
            raise Failure(
                EXIT_STATE,
                f"corrupted state for {part.key}: bytes_on_disk {entry['bytes_on_disk']} "
                f"exceeds pinned {part.expected_size}; nothing will be adopted",
            )
        info = os.lstat(path)
        if info.st_size > part.expected_size:
            raise Failure(
                EXIT_STATE,
                f"operator collision: {path} is {info.st_size} bytes, beyond the pinned "
                f"{part.expected_size}; left untouched",
            )
        if entry["status"] == "complete":
            if info.st_size != part.expected_size:
                raise Failure(
                    EXIT_STATE,
                    f"state marks {part.key} complete but {path} is {info.st_size} bytes",
                )
            adoption[part.key] = info.st_size
            continue
        recorded = entry["bytes_on_disk"]
        if info.st_size < recorded:
            raise Failure(
                EXIT_STATE,
                f"operator collision: {path} shrank below the recorded {recorded} bytes; "
                f"left untouched",
            )
        if info.st_size == recorded and entry["mtime_ns"] != info.st_mtime_ns:
            raise Failure(
                EXIT_STATE,
                f"operator collision: {path} was modified after the recorded state "
                f"(mtime mismatch); left untouched",
            )
        adoption[part.key] = info.st_size
    return adoption


def owned_bytes(state, adoption, parts):
    total = 0
    products = state["products"]
    for part in parts:
        entry = products.get(part.key)
        if entry is None:
            continue
        if entry["status"] == "complete":
            total += part.expected_size
        else:
            total += adoption[part.key]
    return total


def run_guards(normalized, state, parts):
    check_mount_realness(normalized)
    ancestor, _device = check_ancestor(normalized)
    adoption = scan_ownership(normalized, parts, state)
    owned = owned_bytes(state, adoption, parts)
    check_free_space(ancestor, owned)
    return adoption, owned


def mark_partial(state, state_path, workdir, part, bytes_on_disk):
    path = part_disk_path(workdir, part)
    entry = state["products"].setdefault(part.key, {})
    entry.update(
        {
            "url": part.url,
            "expected_size": part.expected_size,
            "bytes_on_disk": bytes_on_disk,
            "status": "partial",
            "mtime_ns": os.lstat(path).st_mtime_ns if os.path.exists(path) else 0,
        }
    )
    entry.pop("sha256", None)
    write_state(state_path, state)


def mark_complete(state, state_path, workdir, part, digest):
    path = part_disk_path(workdir, part)
    entry = state["products"].setdefault(part.key, {})
    entry.update(
        {
            "url": part.url,
            "expected_size": part.expected_size,
            "bytes_on_disk": part.expected_size,
            "status": "complete",
            "mtime_ns": os.lstat(path).st_mtime_ns,
            "sha256": digest,
        }
    )
    write_state(state_path, state)


def seed_hasher(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        while True:
            block = handle.read(CHUNK_SIZE)
            if not block:
                break
            digest.update(block)
    return digest


def drift_message(part, detail):
    return (
        f"{part.url}: pinned editions only; drift fails, never upgrades ({detail}; "
        f"pinned={part.expected_size})"
    )


def no_account_message(url, status):
    return f"HTTP {status} from {url}: no IGN account or credentials are supported; failing closed"


def classify_http_error(url, error):
    if error.code in (401, 403):
        return Failure(EXIT_NETWORK, no_account_message(url, error.code))
    return Failure(EXIT_NETWORK, f"HTTP {error.code} from {url}: {error.reason}")


def head_precheck(part):
    try:
        response = open_url(part.url, method="HEAD")
    except urllib.error.HTTPError as error:
        raise classify_http_error(part.url, error)
    except NETWORK_ERRORS as error:
        raise Failure(EXIT_NETWORK, f"HEAD pre-check failed for {part.url}: {error}")
    with response:
        if response.status in (401, 403):
            raise Failure(EXIT_NETWORK, no_account_message(part.url, response.status))
        if response.status != 200:
            raise Failure(EXIT_NETWORK, f"HEAD pre-check returned HTTP {response.status} for {part.url}")
        reported = response.headers.get("Content-Length")
    if reported is None:
        raise Failure(EXIT_STATE, drift_message(part, "HEAD returned no Content-Length"))
    try:
        reported_size = int(reported)
    except ValueError:
        raise Failure(EXIT_STATE, drift_message(part, f"HEAD Content-Length={reported!r} is not an integer"))
    if reported_size != part.expected_size:
        raise Failure(EXIT_STATE, drift_message(part, f"HEAD Content-Length={reported_size}"))


def verify_completed(workdir, parts, state):
    products = state["products"]
    for part in parts:
        entry = products.get(part.key)
        if entry is None or entry["status"] != "complete":
            continue
        path = part_disk_path(workdir, part)
        digest = hashlib.sha256()
        with open(path, "rb") as handle:
            while True:
                block = handle.read(CHUNK_SIZE)
                if not block:
                    break
                digest.update(block)
        if digest.hexdigest() != entry.get("sha256"):
            raise Failure(
                EXIT_STATE, f"completed part failed sha verification; repairing nothing: {path}"
            )
        log(f"verified complete part: {part.key} ({part.expected_size} bytes, sha256 ok)")


def download_part(workdir, part, resume_from, state, state_path):
    head_precheck(part)
    headers = {"Range": f"bytes={resume_from}-"} if resume_from else {}
    try:
        response = open_url(part.url, method="GET", headers=headers)
    except urllib.error.HTTPError as error:
        raise classify_http_error(part.url, error)
    except NETWORK_ERRORS as error:
        raise Failure(EXIT_NETWORK, f"request failed for {part.url}: {error}")
    path = part_disk_path(workdir, part)
    cap = part.expected_size - resume_from
    received = 0
    failure = None
    digest = None
    try:
        with response:
            if response.status in (401, 403):
                failure = Failure(EXIT_NETWORK, no_account_message(part.url, response.status))
            elif response.status == 206:
                match = CONTENT_RANGE_RE.match(response.headers.get("Content-Range") or "")
                if match is None:
                    failure = Failure(EXIT_NETWORK, f"206 without parseable Content-Range for {part.url}")
                elif int(match.group(3)) != part.expected_size:
                    failure = Failure(
                        EXIT_STATE,
                        drift_message(
                            part,
                            f"Content-Range total {match.group(3)} disagrees with the pinned size",
                        ),
                    )
                elif int(match.group(1)) != resume_from:
                    failure = Failure(
                        EXIT_NETWORK,
                        f"server resumed at byte {match.group(1)} instead of {resume_from} "
                        f"for {part.url}",
                    )
            elif response.status == 200 and resume_from:
                failure = Failure(
                    EXIT_NETWORK,
                    f"server ignored Range for {part.url}; refusing to append a full body "
                    f"(rerun to resume)",
                )
            elif response.status != 200:
                failure = Failure(EXIT_NETWORK, f"unexpected HTTP {response.status} for {part.url}")
            if failure is None:
                hasher = seed_hasher(path) if resume_from else hashlib.sha256()
                os.makedirs(os.path.dirname(path), exist_ok=True)
                sink = open(path, "ab")
                try:
                    while True:
                        want = min(CHUNK_SIZE, cap - received)
                        if want == 0:
                            if response.read(1):
                                failure = Failure(
                                    EXIT_STATE,
                                    f"overlong body beyond the pinned {part.expected_size} bytes "
                                    f"(drift); download stopped at the cap for {part.url}",
                                )
                            break
                        chunk = response.read(want)
                        if not chunk:
                            break
                        sink.write(chunk)
                        hasher.update(chunk)
                        received += len(chunk)
                        if received > cap:
                            failure = Failure(
                                EXIT_STATE,
                                f"overlong body beyond the pinned {part.expected_size} bytes "
                                f"(drift); download stopped at the cap for {part.url}",
                            )
                            break
                    if failure is None and received < cap:
                        failure = Failure(
                            EXIT_NETWORK,
                            f"short read for {part.url}: got {received} of {cap} bytes; owned "
                            f"partial preserved; rerun to resume",
                        )
                    if failure is None:
                        sink.flush()
                        os.fsync(sink.fileno())
                        digest = hasher.hexdigest()
                except NETWORK_ERRORS as error:
                    failure = Failure(
                        EXIT_NETWORK,
                        f"network error during download of {part.url}: {error}; owned partial "
                        f"preserved; rerun to resume",
                    )
                finally:
                    sink.close()
    except NETWORK_ERRORS as error:
        failure = Failure(
            EXIT_NETWORK,
            f"network error during download of {part.url}: {error}; owned partial preserved; "
            f"rerun to resume",
        )
    finally:
        if failure is not None:
            mark_partial(state, state_path, workdir, part, resume_from + received)
        elif digest is not None:
            mark_complete(state, state_path, workdir, part, digest)
    if failure is not None:
        raise failure
    return digest


def verify_remote_sizes(parts):
    # Preflight-verify failures are preflight failures (exit 2); download-time drift is 4.
    log(
        f"verifying remote part sizes via HEAD at {len(parts)} URLs "
        f"(headers only, TLS default-verified, same-host redirects only)"
    )
    for part in parts:
        try:
            response = open_url(part.url, method="HEAD")
        except urllib.error.HTTPError as error:
            raise Failure(
                EXIT_GUARD,
                f"preflight HEAD verification failed for {part.url}: "
                f"{classify_http_error(part.url, error).message}",
            )
        except NETWORK_ERRORS as error:
            raise Failure(EXIT_GUARD, f"preflight HEAD verification failed for {part.url}: {error}")
        with response:
            if response.status in (401, 403):
                raise Failure(EXIT_GUARD, no_account_message(part.url, response.status))
            if response.status != 200:
                raise Failure(
                    EXIT_GUARD,
                    f"preflight HEAD verification returned HTTP {response.status} for {part.url}",
                )
            reported = response.headers.get("Content-Length")
        try:
            reported_size = int(reported)
        except (TypeError, ValueError):
            raise Failure(EXIT_GUARD, drift_message(part, f"HEAD Content-Length={reported!r}"))
        if reported_size != part.expected_size:
            raise Failure(EXIT_GUARD, drift_message(part, f"HEAD Content-Length={reported_size}"))
        log(f"remote size verified: {part.url} = {reported_size} bytes")


def run_preflight(raw, source, parts, verify_remote):
    normalized = check_path_safety(raw)
    log(f"workdir: {normalized} (source: {source})")
    log("path safety: ok (absolute, normalized, no '..', no symlinks, not a file, not the root)")
    state_path, lock_path = state_paths(normalized)
    state = load_state(state_path)
    adoption, owned = run_guards(normalized, state, parts)
    products = state["products"]
    completed = sum(1 for part in parts if products.get(part.key, {}).get("status") == "complete")
    log(f"state: {completed} complete, {len(parts) - completed} outstanding, {owned} owned bytes on disk")
    if os.path.exists(lock_path):
        pid, recorded = read_lock(lock_path)
        log(f"lock present at {lock_path} (pid {pid}, recorded {recorded}, alive={pid_alive(pid)})")
    for product in PRODUCTS:
        product_parts = [part for part in parts if part.key.startswith(f"{product.resource}/")]
        subtotal = sum(part.expected_size for part in product_parts)
        log(f"pinned product: {product.subresource} ({len(product_parts)} parts, {subtotal} bytes)")
    log(
        f"pinned total: {TOTAL_PINNED_BYTES} bytes aggregate denominator "
        f"(+{RESERVE_BYTES} bookkeeping reserve)"
    )
    log(
        "plan: acquisition only; extraction and its exact uncompressed-space guard "
        "(7z listing before extract) are NOT implemented by this helper"
    )
    if verify_remote:
        verify_remote_sizes(parts)
    log("preflight complete; nothing was written and no directories were created")


def run_download(normalized):
    parts = build_parts()
    state_path, lock_path = state_paths(normalized)
    state = load_state(state_path)
    adoption, owned = run_guards(normalized, state, parts)
    log(
        f"plan: {len(parts)} pinned parts, {owned} owned bytes on disk, "
        f"{TOTAL_PINNED_BYTES} pinned aggregate, reserve {RESERVE_BYTES}"
    )
    acquire_lock(lock_path)
    try:
        verify_completed(normalized, parts, state)
        for part in parts:
            entry = state["products"].get(part.key)
            if entry is not None and entry["status"] == "partial" and adoption[part.key] != entry["bytes_on_disk"]:
                mark_partial(state, state_path, normalized, part, adoption[part.key])
        for part in parts:
            entry = state["products"].get(part.key)
            if entry is not None and entry["status"] == "complete":
                continue
            resume_from = adoption[part.key]
            log(f"downloading: {part.key} from byte {resume_from} of {part.expected_size}")
            download_part(normalized, part, resume_from, state, state_path)
        log("all pinned parts present and sha-verified; extraction is NOT implemented by this helper")
    finally:
        release_lock(lock_path)


def run_recover(raw, source):
    normalized = check_path_safety(raw)
    log(f"workdir: {normalized} (source: {source})")
    check_mount_realness(normalized)
    check_ancestor(normalized)
    _state_path, lock_path = state_paths(normalized)
    recover_lock(lock_path)


def resolve_raw_workdir(args):
    if args.workdir:
        return args.workdir, "positional"
    env_value = os.environ.get("PANOPTICON_GEO_PROOF_DIR")
    if env_value:
        return env_value, "env PANOPTICON_GEO_PROOF_DIR"
    return DEFAULT_WORKDIR, "default"


def build_parser():
    parser = argparse.ArgumentParser(
        prog="sedan_acquire.py",
        description=(
            "Acquisition helper for the Sedan geo-proof cache (WI-R2-01): downloads and "
            "verifies the two pinned IGN D008 archives from data.geopf.fr. Extraction and "
            "its uncompressed-space guard are NOT implemented."
        ),
        epilog=(
            f"workdir resolution order: 1) positional argument, 2) env "
            f"PANOPTICON_GEO_PROOF_DIR, 3) default {DEFAULT_WORKDIR}. Modes: default or "
            f"--preflight = read-only checks plus a printed plan (never writes, never "
            f"touches the network); --preflight --verify-remote adds bounded HEAD "
            f"Content-Length verification of every part URL; --download-only runs guards, "
            f"metadata verify, then download; --recover-lock removes a stale lock whose "
            f"recorded pid is no longer alive. Exit codes: 0 success; 2 guard or preflight "
            f"failure; 3 download/network failure (resumable); 4 state or drift failure."
        ),
    )
    parser.add_argument(
        "workdir",
        nargs="?",
        default=None,
        help=(
            f"target work dir; resolution order: positional > env PANOPTICON_GEO_PROOF_DIR "
            f"> default {DEFAULT_WORKDIR}"
        ),
    )
    parser.add_argument(
        "--preflight",
        action="store_true",
        help="read-only host checks plus a printed plan (default when no mode flag is given)",
    )
    parser.add_argument(
        "--verify-remote",
        action="store_true",
        help=(
            "with --preflight: bounded HEAD Content-Length verification of every part URL "
            "(headers only, TLS default-verified, same-host redirects only)"
        ),
    )
    parser.add_argument(
        "--download-only",
        action="store_true",
        help="perform the acquisition: guards, metadata verify, then download; extraction is NOT implemented",
    )
    parser.add_argument(
        "--recover-lock",
        action="store_true",
        help="remove a stale state lock only when its recorded pid is no longer alive",
    )
    return parser


def main(argv=None):
    parser = build_parser()
    args = parser.parse_args(argv)
    if args.download_only and (args.preflight or args.verify_remote):
        parser.error("--download-only cannot be combined with --preflight/--verify-remote")
    if args.download_only and args.recover_lock:
        parser.error("--download-only cannot be combined with --recover-lock")
    if args.recover_lock and (args.preflight or args.verify_remote):
        parser.error("--recover-lock cannot be combined with --preflight/--verify-remote")
    raw, source = resolve_raw_workdir(args)
    if args.download_only:
        mode = "download"
    elif args.recover_lock:
        mode = "recover-lock"
    elif args.verify_remote:
        mode = "preflight+verify-remote"
    else:
        mode = "preflight"
    code = EXIT_OK
    try:
        if args.download_only:
            normalized = check_path_safety(raw)
            log(f"workdir: {normalized} (source: {source})")
            run_download(normalized)
        elif args.recover_lock:
            run_recover(raw, source)
        else:
            run_preflight(raw, source, build_parts(), args.verify_remote)
    except Failure as failure:
        log(f"FAIL({failure.code}): {failure.message}")
        code = failure.code
    log(f"final status: exit={code} mode={mode} workdir={raw}")
    return code


if __name__ == "__main__":
    sys.exit(main())
