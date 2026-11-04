#!/usr/bin/env python3
"""Unattended bootstrap for the Sedan ground-truth prepare pipeline: WFS lineage + pinned tools.

Two independent stages over one proof root:

fetch_lineage: one bounded official GetFeature against the geopf WFS layer
IGNF_LIDAR-HD_METADONNEE:metadata, selecting the first-tile feature (coordonnees_nw
0840-6958) and writing its provider properties VERBATIM to
<root>/lidarhd/state/lineage-0840-6958.json -- the exact filename the probe's
--lineage-file consumes (plan record section h; sedan_lidar_probe.evaluate_gate_e
verifies these actual WFS keys). The write is atomic, O_EXCL-owned and idempotent: an
existing identical file is verified and kept untouched, an existing different file is
drift (exit 4, never overwritten).

bootstrap_tools: isolated venv at <root>/tools/lidar-venv with the three wheel-pinned
packages from lidar-tools-lock.txt (laspy 2.7.0, lazrs 0.8.2, numpy 2.5.4), installed
with --require-hashes --only-binary :all: from a local wheel cache filled by one bounded
`pip download` (--no-deps; pip's own retries disabled). Existing venvs are validated
(pyvenv.cfg, sys.prefix identity via a spawned child, pinned package versions) and then
either reused unchanged (matching installed-receipt.json), allowed with explicit
acknowledgment (pre-receipt-policy venv, never adopted or mutated), or refused
(exit 4). A bin/python symlink to the host python3 is LEGAL: the identity is what the
spawned child reports (sys.prefix == venv dir, base prefix different), never the
resolved symlink target.

Transport failures (TLS, read timeout, connection reset) retry up to 3 TOTAL attempts,
the same bounded policy as the orchestrator's transport stages; authentication errors
and everything else stop immediately. No credentials are ever sent or stored.

Python 3 standard library only. Exit codes mirror sedan_acquire: 0 success; 2 guard or
gate failure (preflight guards, platform, WFS structure, auth); 3 transport/child
failure after bounded retries; 4 state or drift failure (lock/lineage/receipt drift,
refusing a foreign or corrupted venv, atomic-write failure).
"""

import argparse
import hashlib
import http.client
import itertools
import json
import os
import platform
import re
import selectors
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime
from typing import NamedTuple

EXIT_OK = 0
EXIT_GUARD = 2
EXIT_NETWORK = 3
EXIT_STATE = 4

# Official geopf WFS GetFeature, verbatim from the recorded execution request:
# ~/log/track-r/sedan-alternative-groundtruth-20261103T0900-0500/requests.txt item 8
# ("Bounded GetFeature on IGNF_LIDAR-HD_METADONNEE:metadata, bbox = AOI box,
# E 839975.58..841075.58, N 6957066.50..6958166.50, Lambert-93"; response saved as
# lidarhd-metadata-sedan.json), extraction recipe in
# ~/log/track-r/lidar-firsttile-runtime-plan-20261103T1315-0500/plan.md section h.
DEFAULT_WFS_URL = (
    "https://data.geopf.fr/wfs/ows?service=WFS&version=2.0.0&request=GetFeature"
    "&typeNames=IGNF_LIDAR-HD_METADONNEE:metadata"
    "&bbox=839975.58,6957066.50,841075.58,6958166.50,EPSG:2154"
    "&outputFormat=application/json"
)
# The geopf CDN rejects the default urllib User-Agent (acquire evidence); identify explicitly.
USER_AGENT = "Panopticon-geo-bootstrap/1.0 (IGN Licence Ouverte 2.0 lineage query; contact via repository)"

TILE_COORDS = "0840-6958"
FIRST_TILE_NAME = "LHD_FXX_0840_6958_PTS_LAMB93_IGN69.copc.laz"
LINEAGE_REL = os.path.join("lidarhd", "state", f"lineage-{TILE_COORDS}.json")
# The 18 provider keys of IGNF_LIDAR-HD_METADONNEE:metadata, from the recorded WFS
# response; every feature must carry exactly this set, no more, no less.
PROVIDER_KEYS = frozenset((
    "capteur", "code_mission", "coordonnees_nw", "date_debut_acquisition", "date_edition",
    "date_fin_acquisition", "moa_acquisition", "moe_acquisition",
    "moe_classement_automatique", "moe_classement_manuel", "nombre_points",
    "procede_classement", "systeme_altimetrique", "systeme_planimetrique",
    "url_mnh", "url_mns", "url_mnt", "url_npl",
))
EXPECTED_FEATURE_MIN = 1
EXPECTED_FEATURE_MAX = 16
WFS_BODY_CAP_BYTES = 1 << 20
LINEAGE_FILE_MAX_BYTES = 1 << 20
DEFAULT_TIMEOUT_SECONDS = 60.0
READ_CHUNK = 1 << 16
NETWORK_ERRORS = (
    urllib.error.URLError,
    TimeoutError,
    ConnectionError,
    OSError,
    http.client.HTTPException,
)

TOOLS_REL = "tools"
LOCK_NAME = "lidar-tools-lock.txt"
DEFAULT_LOCK_PATH = os.path.expanduser(
    "~/log/track-r/lidar-firsttile-runtime-plan-20261103T1315-0500/lidar-tools-lock.txt")
LOCK_MAX_BYTES = 1 << 16
VENV_DIR_REL = os.path.join("tools", "lidar-venv")
VENV_PYTHON_REL = os.path.join(VENV_DIR_REL, "bin", "python")
WHEEL_CACHE_REL = os.path.join("tools", "wheel-cache")
VENV_CONFIG_NAME = "pyvenv.cfg"
RECEIPT_NAME = "installed-receipt.json"
RECEIPT_MAX_BYTES = 1 << 16
# Pinned identities from lidar-tools-lock.txt (plan record
# lidar-firsttile-runtime-plan-20261103T1315-0500); any drift fails closed.
PINNED_PACKAGES = (
    ("laspy", "2.7.0", "sha256:15f5344c62a1023461996bdf5d1ba5fdd813e96694a524fee712931134f3792f"),
    ("lazrs", "0.8.2", "sha256:f481e27206db60e3f9d1d057c37d4c2ecc6e2af21e4c6b60b3a9fa347062aa50"),
    ("numpy", "2.5.4", "sha256:d549420b8858885cea8838a727842249218b9c1da24dd517e25c9c7a948310a3"),
)
LOCK_ENTRY_RE = re.compile(r"^(\S+)==(\S+)[ \t]+--hash=sha256:([0-9a-f]{64})$")

SUPPORTED_PLATFORM = "linux"
SUPPORTED_MACHINE = "x86_64"
SUPPORTED_PYTHON = (3, 14)
# Wheel total (~35 MiB per the lock) + pip cache + venv overhead (~100 MiB) + reserve.
WHEEL_TOTAL_BYTES = 35 << 20
PIP_CACHE_BYTES = 64 << 20
VENV_OVERHEAD_BYTES = 100 << 20
RESERVE_BYTES = 1 << 30
REQUIRED_FREE_BYTES = WHEEL_TOTAL_BYTES + PIP_CACHE_BYTES + VENV_OVERHEAD_BYTES + RESERVE_BYTES
PROC_MOUNTS_PATH = "/proc/mounts"

TRANSPORT_ATTEMPTS = 3
TRANSPORT_RE = re.compile(
    r"SSL|TLS|CERTIFICATE_VERIFY|timed out|timeout|Connection reset|Connection aborted"
    r"|RemoteDisconnected|Remote end closed"
)
# Authentication indicators outrank transport indicators: a mixed output never retries.
AUTH_RE = re.compile(r"auth|credential|HTTP 40[13]|\b40[13]\b|no IGN account", re.IGNORECASE)

CHILD_DEADLINE_SECONDS = 1800
CHILD_OUTPUT_CAP = 16 << 20
VENV_QUERY_SECONDS = 60
VENV_QUERY_CAP_BYTES = 1 << 20

IDENTITY_CODE = "import sys; print(sys.prefix); print(sys.base_prefix); print('%d.%d.%d' % sys.version_info[:3])"
PACKAGE_CODE = ("import importlib.metadata as metadata, laspy, lazrs, numpy\n"
                + "".join(f"print({name!r}, metadata.version({name!r}))\n"
                          for name, _version, _digest in PINNED_PACKAGES))


class Stop(Exception):
    def __init__(self, code, message):
        super().__init__(message)
        self.code = code
        self.message = message


class ChildResult(NamedTuple):
    returncode: int
    stdout: str
    stderr: str


def now_iso():
    return datetime.now().astimezone().isoformat(timespec="milliseconds")


def log(message):
    print(f"{now_iso()} {message}", flush=True)


def child_exit_code(returncode):
    return returncode if returncode in (EXIT_GUARD, EXIT_NETWORK, EXIT_STATE) else EXIT_NETWORK


def run_child(argv, deadline=CHILD_DEADLINE_SECONDS, cap=CHILD_OUTPUT_CAP, env=None):
    start = time.monotonic()
    deadline_at = start + max(1.0, deadline)
    buffers = {"stdout": bytearray(), "stderr": bytearray()}
    try:
        proc = subprocess.Popen(argv, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env)
    except OSError as error:
        raise Stop(EXIT_NETWORK, f"child failed to start ({argv[0] if argv else argv}): {error}")
    selector = selectors.DefaultSelector()
    try:
        for key, stream in (("stdout", proc.stdout), ("stderr", proc.stderr)):
            os.set_blocking(stream.fileno(), False)
            selector.register(stream, selectors.EVENT_READ, key)
        open_streams = 2
        while open_streams:
            remaining = deadline_at - time.monotonic()
            if remaining <= 0:
                raise Stop(EXIT_NETWORK, f"child exceeded {deadline} s deadline: {argv[:2]}")
            for key, _events in selector.select(timeout=remaining):
                chunk = os.read(key.fileobj.fileno(), READ_CHUNK)
                if not chunk:
                    selector.unregister(key.fileobj)
                    open_streams -= 1
                    continue
                buffers[key.data].extend(chunk)
                if len(buffers["stdout"]) + len(buffers["stderr"]) > cap:
                    raise Stop(EXIT_NETWORK, f"child exceeded the {cap}-byte output cap: {argv[:2]}")
        while proc.poll() is None:
            try:
                proc.wait(timeout=max(0.1, deadline_at - time.monotonic()))
            except subprocess.TimeoutExpired:
                raise Stop(EXIT_NETWORK, f"child did not exit after closing its pipes: {argv[:2]}")
    finally:
        if proc.poll() is None:
            proc.kill()
            proc.wait()
        selector.close()
        proc.stdout.close()
        proc.stderr.close()
    return ChildResult(proc.returncode,
                       buffers["stdout"].decode("utf-8", "replace"),
                       buffers["stderr"].decode("utf-8", "replace"))


RUN_CHILD = run_child


def classify_transport_error(stdout, stderr, returncode):
    """Bounded-retry decision shared by the WFS stage and the pip stages (auth wins)."""
    combined = (stdout or "") + (stderr or "")
    if AUTH_RE.search(combined):
        return "stop"
    if returncode == EXIT_NETWORK and TRANSPORT_RE.search(combined):
        return "retry"
    return "stop"


def normalize_root(raw, label):
    root = os.path.normpath(os.path.abspath(os.path.expanduser(raw)))
    if root == os.path.sep:
        raise Stop(EXIT_GUARD, f"refusing the filesystem root as the {label}")
    return root


# ---------------------------------------------------------------------------
# WFS lineage stage
# ---------------------------------------------------------------------------


class SameHostRedirectHandler(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        original = urllib.parse.urlsplit(req.full_url)
        target = urllib.parse.urlsplit(newurl)
        if (target.scheme, target.netloc) != (original.scheme, original.netloc):
            raise Stop(EXIT_GUARD, f"cross-host redirect refused: {req.full_url} -> {newurl}")
        return super().redirect_request(req, fp, code, msg, headers, newurl)


_OPENER = urllib.request.build_opener(SameHostRedirectHandler())


def http_get(url, timeout=DEFAULT_TIMEOUT_SECONDS, cap=WFS_BODY_CAP_BYTES):
    request = urllib.request.Request(
        url,
        headers={"User-Agent": USER_AGENT, "Accept": "application/geo+json, application/json"},
    )
    body = bytearray()
    with _OPENER.open(request, timeout=timeout) as response:
        while True:
            chunk = response.read(READ_CHUNK)
            if not chunk:
                break
            body.extend(chunk)
            if len(body) > cap:
                raise Stop(EXIT_GUARD, f"WFS response exceeds the {cap}-byte output cap; failing closed")
    return bytes(body)


def resolve_url(url_config):
    if url_config is None:
        return DEFAULT_WFS_URL
    if isinstance(url_config, dict) and isinstance(url_config.get("url"), str) and url_config["url"]:
        return url_config["url"]
    raise Stop(EXIT_GUARD, "url_config must be None or a dict carrying a non-empty 'url' string")


def bounded_get(url):
    last = "no attempt recorded"
    for attempt in range(1, TRANSPORT_ATTEMPTS + 1):
        try:
            return http_get(url)
        except Stop:
            raise
        except urllib.error.HTTPError as error:
            last = f"HTTP {error.code} from {url}: {error.reason}"
            if error.code in (401, 403):
                raise Stop(EXIT_GUARD, f"{last}: no IGN account or credentials are supported; failing closed")
        except NETWORK_ERRORS as error:
            last = f"{type(error).__name__}: {error}"
        decision = classify_transport_error("", last, EXIT_NETWORK)
        if decision == "retry" and attempt < TRANSPORT_ATTEMPTS:
            log(f"WFS transport failure (TLS, read timeout, or reset): retrying ({attempt}/{TRANSPORT_ATTEMPTS})")
            continue
        raise Stop(EXIT_NETWORK, f"WFS GET failed (attempt {attempt}/{TRANSPORT_ATTEMPTS}, no retry): {last}")
    raise Stop(EXIT_NETWORK, f"WFS GET exhausted {TRANSPORT_ATTEMPTS} attempts: {last}")


def parse_feature_collection(body):
    if len(body) > WFS_BODY_CAP_BYTES:
        raise Stop(EXIT_GUARD, f"WFS response exceeds the {WFS_BODY_CAP_BYTES}-byte output cap; failing closed")
    try:
        document = json.loads(body)
    except ValueError as error:
        raise Stop(EXIT_GUARD, f"WFS response is not valid JSON: {error}; failing closed")
    if not isinstance(document, dict) or document.get("type") != "FeatureCollection":
        raise Stop(EXIT_GUARD, "WFS response is not a GeoJSON FeatureCollection; failing closed")
    features = document.get("features")
    if not isinstance(features, list):
        raise Stop(EXIT_GUARD, "FeatureCollection carries no features array; failing closed")
    if not (EXPECTED_FEATURE_MIN <= len(features) <= EXPECTED_FEATURE_MAX):
        raise Stop(EXIT_GUARD, f"WFS returned {len(features)} features; expected "
                               f"{EXPECTED_FEATURE_MIN}..{EXPECTED_FEATURE_MAX}; failing closed")
    properties_list = []
    for feature in features:
        properties = feature.get("properties") if isinstance(feature, dict) else None
        if not isinstance(properties, dict):
            raise Stop(EXIT_GUARD, "WFS feature without a properties object; failing closed")
        if set(properties) != PROVIDER_KEYS:
            missing = sorted(PROVIDER_KEYS - set(properties))
            extra = sorted(set(properties) - PROVIDER_KEYS)
            raise Stop(EXIT_GUARD, f"feature properties are not exactly the 18 provider keys "
                                   f"(missing {missing}, extra {extra}); failing closed")
        properties_list.append(properties)
    return properties_list


def select_first_tile(properties_list):
    matches = [properties for properties in properties_list
               if properties.get("coordonnees_nw") == TILE_COORDS]
    if not matches:
        raise Stop(EXIT_GUARD, f"no WFS feature matches tile {TILE_COORDS}; failing closed")
    if len(matches) > 1:
        raise Stop(EXIT_GUARD, f"{len(matches)} WFS features match tile {TILE_COORDS}; ambiguous; failing closed")
    properties = matches[0]
    npl = properties.get("url_npl")
    if not isinstance(npl, str) or not npl.endswith(FIRST_TILE_NAME):
        raise Stop(EXIT_GUARD, f"selected feature url_npl does not end with {FIRST_TILE_NAME}; failing closed")
    return properties


_TEMP_COUNTER = itertools.count()


def atomic_write_excl(path, payload):
    """Write payload to path atomically: O_EXCL temp, fsync, rename; no partial on failure."""
    directory = os.path.dirname(path)
    tmp = os.path.join(directory, f".{os.path.basename(path)}.tmp-{os.getpid()}-{next(_TEMP_COUNTER)}")
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
    if hasattr(os, "O_NOFOLLOW"):
        flags |= os.O_NOFOLLOW
    try:
        fd = os.open(tmp, flags, 0o644)
        try:
            with os.fdopen(fd, "wb") as handle:
                handle.write(payload)
                handle.flush()
                os.fsync(handle.fileno())
            os.rename(tmp, path)
        except BaseException:
            try:
                os.unlink(tmp)
            except OSError:
                pass
            raise
    except OSError as error:
        raise Stop(EXIT_STATE, f"atomic write of {path} failed: {error}; no partial file was left behind")
    dir_fd = None
    try:
        dir_fd = os.open(directory, os.O_RDONLY)
        os.fsync(dir_fd)
    except OSError:
        pass
    finally:
        if dir_fd is not None:
            os.close(dir_fd)


def ensure_no_symlink_ancestors(path):
    probe = os.path.dirname(os.path.abspath(path))
    while True:
        if os.path.islink(probe):
            raise Stop(EXIT_GUARD, f"symlinked ancestor refused: {probe}")
        parent = os.path.dirname(probe)
        if parent == probe:
            return
        probe = parent


def read_bounded(path, cap, label):
    try:
        with open(path, "rb") as handle:
            data = handle.read(cap + 1)
    except OSError as error:
        raise Stop(EXIT_STATE, f"unreadable {label} at {path}: {error}")
    if len(data) > cap:
        raise Stop(EXIT_STATE, f"{label} at {path} exceeds the {cap}-byte bound")
    return data


def fetch_lineage(root, url_config=None):
    """Fetch the WFS feature collection and own the verbatim lineage file; returns its path."""
    root = normalize_root(root, "lineage root")
    url = resolve_url(url_config)
    target = os.path.join(root, LINEAGE_REL)
    if os.path.islink(target):
        raise Stop(EXIT_STATE, f"lineage path exists as a symlink at {target}; refusing")
    if os.path.isdir(target):
        raise Stop(EXIT_STATE, f"lineage path exists as a directory at {target}; refusing")
    ensure_no_symlink_ancestors(target)
    body = bounded_get(url)
    properties = select_first_tile(parse_feature_collection(body))
    payload = json.dumps(properties, sort_keys=True, indent=2).encode("utf-8") + b"\n"
    if os.path.lexists(target):
        raw = read_bounded(target, LINEAGE_FILE_MAX_BYTES, "existing lineage file")
        try:
            existing = json.loads(raw)
        except ValueError as error:
            raise Stop(EXIT_STATE, f"existing lineage file at {target} is not readable JSON: {error}; "
                                   "drift; refusing to overwrite")
        if existing == properties:
            log(f"lineage already owned and identical to the live WFS selection: SKIP ({target})")
            return target
        raise Stop(EXIT_STATE, f"existing lineage at {target} differs from the live WFS selection; "
                               "drift; refusing to overwrite (exit 4)")
    os.makedirs(os.path.dirname(target), exist_ok=True)
    atomic_write_excl(target, payload)
    log(f"lineage written verbatim from the WFS feature properties: {target}")
    return target


# ---------------------------------------------------------------------------
# Tools bootstrap stage
# ---------------------------------------------------------------------------


def parse_lock(text, source):
    entries = []
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        match = LOCK_ENTRY_RE.match(stripped)
        if not match:
            raise Stop(EXIT_STATE, f"lock entry in {source} does not match "
                                   f"`name==version --hash=sha256:<64 hex>`: {stripped!r}")
        entries.append((match.group(1), match.group(2), f"sha256:{match.group(3)}"))
    if len(entries) != len(PINNED_PACKAGES):
        raise Stop(EXIT_STATE, f"lock at {source} carries {len(entries)} pinned entries; "
                               f"expected exactly {len(PINNED_PACKAGES)}")
    if tuple(entries) != PINNED_PACKAGES:
        expected = [f"{name}=={version} {digest}" for name, version, digest in PINNED_PACKAGES]
        raise Stop(EXIT_STATE, f"lock at {source} drifts from the pinned tool set; "
                               f"expected {expected}; refusing (exit 4)")
    return entries


def resolve_lock_path(root, lock_path):
    if lock_path:
        if not os.path.isfile(lock_path):
            raise Stop(EXIT_STATE, f"lock file not found at {lock_path}")
        return lock_path
    root_copy = os.path.join(root, TOOLS_REL, LOCK_NAME)
    if os.path.isfile(root_copy):
        return root_copy
    if os.path.isfile(DEFAULT_LOCK_PATH):
        return DEFAULT_LOCK_PATH
    raise Stop(EXIT_STATE, f"no tool lock file found (root copy {root_copy}, plan record {DEFAULT_LOCK_PATH})")


def read_mounts(path=None):
    mounts_path = path or PROC_MOUNTS_PATH
    entries = []
    with open(mounts_path, "r", encoding="utf-8") as handle:
        for line in handle:
            parts = line.split()
            if len(parts) >= 4:
                entries.append((parts[0], parts[1], parts[2], parts[3].split(",")))
    return entries


def require_rw_mount(root):
    try:
        entries = read_mounts()
    except OSError as error:
        raise Stop(EXIT_GUARD, f"mount table unreadable at {PROC_MOUNTS_PATH}: {error}; refusing")
    best = None
    for entry in entries:
        device, mount_point, fstype, options = entry
        if root == mount_point or root.startswith(mount_point.rstrip("/") + "/"):
            if best is None or len(mount_point) > len(best[1]):
                best = entry
    if best is None:
        raise Stop(EXIT_GUARD, f"no mount entry in {PROC_MOUNTS_PATH} covers {root}; refusing to "
                               "create anything on a possibly-unmounted path")
    if "rw" not in best[3]:
        raise Stop(EXIT_GUARD, f"mount for {root} ({best[1]}) is not rw ({','.join(best[3])}); refusing")


def statvfs(path):
    return os.statvfs(path)


def nearest_existing_ancestor(path):
    probe = path
    while not os.path.exists(probe):
        parent = os.path.dirname(probe)
        if parent == probe:
            raise Stop(EXIT_GUARD, f"no existing ancestor for {path}")
        probe = parent
    return probe


def require_free_space(root):
    anchor = nearest_existing_ancestor(root)
    usage = statvfs(anchor)
    free = usage.f_bavail * usage.f_frsize
    if free < REQUIRED_FREE_BYTES:
        raise Stop(EXIT_GUARD, f"insufficient free space at {anchor}: {free} bytes available, "
                               f"{REQUIRED_FREE_BYTES} required (wheels ~35 MiB + pip cache + "
                               "venv overhead ~100 MiB + 1 GiB reserve)")


def platform_supported(platform_name, machine, python_version):
    return (platform_name == SUPPORTED_PLATFORM
            and machine == SUPPORTED_MACHINE
            and tuple(python_version[:2]) == SUPPORTED_PYTHON)


def check_platform():
    machine = platform.machine()
    current = tuple(sys.version_info[:2])
    if not platform_supported(sys.platform, machine, current):
        raise Stop(EXIT_GUARD, f"unsupported platform for the pinned wheels: {sys.platform} {machine} "
                               f"python {'.'.join(str(part) for part in current)}; pinned host is "
                               f"{SUPPORTED_PLATFORM} {SUPPORTED_MACHINE} python "
                               f"{SUPPORTED_PYTHON[0]}.{SUPPORTED_PYTHON[1]}")


def child_env():
    env = {key: value for key, value in os.environ.items() if key not in ("PYTHONPATH", "PYTHONHOME")}
    env["PYTHONDONTWRITEBYTECODE"] = "1"
    return env


def venv_identity(python):
    result = RUN_CHILD([python, "-c", IDENTITY_CODE], deadline=VENV_QUERY_SECONDS,
                       cap=VENV_QUERY_CAP_BYTES, env=child_env())
    if result.returncode != 0:
        raise Stop(EXIT_STATE, f"venv python identity query exited {result.returncode}: "
                               f"{result.stderr.strip()[:300]}")
    lines = result.stdout.splitlines()
    if len(lines) != 3:
        raise Stop(EXIT_STATE, f"venv python identity query returned {len(lines)} lines; expected 3")
    return lines[0], lines[1], lines[2]


def venv_packages(python):
    result = RUN_CHILD([python, "-c", PACKAGE_CODE], deadline=VENV_QUERY_SECONDS,
                       cap=VENV_QUERY_CAP_BYTES, env=child_env())
    if result.returncode != 0:
        raise Stop(EXIT_STATE, f"venv package query exited {result.returncode}: "
                               f"{result.stderr.strip()[:300]}")
    found = {}
    for line in result.stdout.splitlines():
        name, _separator, version = line.partition(" ")
        found[name] = version
    return found


def validate_venv(venv_dir, python):
    """Return None when the venv is ours and pinned-complete, else a one-line reason."""
    config_path = os.path.join(venv_dir, VENV_CONFIG_NAME)
    try:
        with open(config_path, "rb") as handle:
            handle.read(READ_CHUNK)
    except OSError as error:
        return f"{VENV_CONFIG_NAME} unreadable at {config_path}: {error}"
    # A normal venv symlinks bin/python to the host python3; that is LEGAL. The spawned
    # identity child is the only authority; the symlink target is never the identity.
    if not os.path.isfile(python) or not os.access(python, os.X_OK):
        return f"venv python is missing or not executable at {python}"
    try:
        prefix, base_prefix, _version = venv_identity(python)
    except Stop as stop:
        return stop.message
    if os.path.normpath(prefix) != os.path.normpath(venv_dir):
        return f"venv python reports sys.prefix {prefix}; expected {venv_dir}"
    if base_prefix == prefix:
        return "venv python reports sys.base_prefix equal to sys.prefix"
    try:
        found = venv_packages(python)
    except Stop as stop:
        return stop.message
    for name, version, _digest in PINNED_PACKAGES:
        if found.get(name) != version:
            return f"venv package {name} is {found.get(name)!r}; expected {version}"
    return None


def read_receipt(receipt_path):
    if not os.path.isfile(receipt_path):
        return None
    raw = read_bounded(receipt_path, RECEIPT_MAX_BYTES, "installed venv receipt")
    try:
        receipt = json.loads(raw)
    except ValueError as error:
        raise Stop(EXIT_STATE, f"installed venv receipt at {receipt_path} is not valid JSON: {error}; "
                               "drift; failing closed")
    if not isinstance(receipt, dict):
        raise Stop(EXIT_STATE, f"installed venv receipt at {receipt_path} is not a JSON object; failing closed")
    return receipt


def receipt_matches(receipt, entries):
    if receipt.get("receipt_version") != 1:
        return False
    expected = [{"name": name, "version": version, "hash": digest}
                for name, version, digest in entries]
    return receipt.get("packages") == expected


def adopt_or_reuse(venv_dir, python, entries):
    if os.path.islink(venv_dir) or not os.path.isdir(venv_dir):
        raise Stop(EXIT_STATE, f"venv path at {venv_dir} is a symlink or not a directory; refusing")
    reason = validate_venv(venv_dir, python)
    if reason is not None:
        raise Stop(EXIT_STATE, f"existing venv at {venv_dir} does not validate ({reason}); "
                               "refusing to touch it (exit 4)")
    receipt_path = os.path.join(venv_dir, RECEIPT_NAME)
    receipt = read_receipt(receipt_path)
    if receipt is None:
        log("verified existing venv (packages correct, receipt absent; created before receipt "
            "policy); allowed with explicit acknowledgment; NOT adopted or modified")
        return python
    if not receipt_matches(receipt, entries):
        raise Stop(EXIT_STATE, f"installed receipt at {receipt_path} disagrees with the pinned lock; "
                               "drift; refusing (exit 4)")
    log(f"verified existing venv with matching installed receipt: reusing without modification ({venv_dir})")
    return python


def create_venv(venv_dir):
    argv = [sys.executable, "-m", "venv", venv_dir]
    log(f"run: VENV create: {' '.join(argv)}")
    result = RUN_CHILD(argv, deadline=CHILD_DEADLINE_SECONDS, cap=CHILD_OUTPUT_CAP)
    if result.returncode != 0:
        excerpt = ((result.stderr or "") + (result.stdout or "")).strip()[:500]
        raise Stop(child_exit_code(result.returncode), f"venv creation exited {result.returncode}: {excerpt}")


def wheel_cache_sizes(cache_dir):
    sizes = {}
    try:
        with os.scandir(cache_dir) as entries:
            for entry in entries:
                if entry.name.endswith(".whl") and entry.is_file():
                    sizes[entry.name] = entry.stat().st_size
    except OSError as error:
        raise Stop(EXIT_STATE, f"wheel cache at {cache_dir} is unreadable: {error}")
    if len(sizes) != len(PINNED_PACKAGES):
        raise Stop(EXIT_STATE, f"wheel cache at {cache_dir} holds {len(sizes)} wheels; expected exactly "
                               f"{len(PINNED_PACKAGES)} (--no-deps, pinned requirement set)")
    return sizes


def download_wheels(python, lock_path, cache_dir):
    argv = [python, "-m", "pip", "download", "--only-binary", ":all:", "--no-deps", "--require-hashes",
            "--retries", "0", "--dest", cache_dir, "-r", lock_path]
    log(f"run: WHEELS download: {' '.join(argv)}")
    for attempt in range(1, TRANSPORT_ATTEMPTS + 1):
        result = RUN_CHILD(argv, deadline=CHILD_DEADLINE_SECONDS, cap=CHILD_OUTPUT_CAP)
        if result.returncode == 0:
            return
        decision = classify_transport_error(result.stdout, result.stderr, child_exit_code(result.returncode))
        if decision == "retry" and attempt < TRANSPORT_ATTEMPTS:
            log(f"WHEELS transport failure (TLS, read timeout, or reset): retrying ({attempt}/{TRANSPORT_ATTEMPTS})")
            continue
        excerpt = ((result.stderr or "") + (result.stdout or "")).strip()[:500]
        raise Stop(child_exit_code(result.returncode),
                   f"pip download exited {result.returncode} on attempt {attempt}/{TRANSPORT_ATTEMPTS} "
                   f"(no retry): {excerpt}")
    raise Stop(EXIT_NETWORK, "pip download exhausted its bounded attempts")


def install_wheels(python, lock_path, cache_dir):
    argv = [python, "-m", "pip", "install", "--no-index", "--require-hashes", "--only-binary", ":all:",
            "--retries", "0", "--find-links", cache_dir, "-r", lock_path]
    log(f"run: WHEELS install: {' '.join(argv)}")
    result = RUN_CHILD(argv, deadline=CHILD_DEADLINE_SECONDS, cap=CHILD_OUTPUT_CAP)
    if result.returncode != 0:
        excerpt = ((result.stderr or "") + (result.stdout or "")).strip()[:500]
        raise Stop(child_exit_code(result.returncode),
                   f"pip install exited {result.returncode} (local wheels only, never retried): {excerpt}")


def receipt_payload(entries, python_version, lock_path, lock_bytes, wheel_sizes, venv_dir):
    return {
        "receipt_version": 1,
        "created": now_iso(),
        "python_version": python_version,
        "python": os.path.join(venv_dir, "bin", "python"),
        "lock": {"path": lock_path, "sha256": hashlib.sha256(lock_bytes).hexdigest()},
        "packages": [{"name": name, "version": version, "hash": digest}
                     for name, version, digest in entries],
        "wheel_sizes": wheel_sizes,
    }


def bootstrap_tools(root, lock_path=None):
    """Isolate and pin the tool venv under <root>/tools; returns the venv python path."""
    root = normalize_root(root, "tools root")
    lock_resolved = resolve_lock_path(root, lock_path)
    lock_bytes = read_bounded(lock_resolved, LOCK_MAX_BYTES, "tool lock file")
    try:
        lock_text = lock_bytes.decode("utf-8")
    except UnicodeDecodeError as error:
        raise Stop(EXIT_STATE, f"tool lock file at {lock_resolved} is not UTF-8: {error}")
    entries = parse_lock(lock_text, lock_resolved)
    venv_dir = os.path.join(root, VENV_DIR_REL)
    python = os.path.join(root, VENV_PYTHON_REL)
    cache_dir = os.path.join(root, WHEEL_CACHE_REL)
    # Guards run before ANY mkdir or network: mount realness, ancestor no-follow, space.
    require_rw_mount(root)
    ensure_no_symlink_ancestors(venv_dir)
    require_free_space(root)
    check_platform()
    if os.path.lexists(venv_dir):
        return adopt_or_reuse(venv_dir, python, entries)
    os.makedirs(os.path.dirname(venv_dir), exist_ok=True)
    create_venv(venv_dir)
    os.makedirs(cache_dir, exist_ok=True)
    download_wheels(python, lock_resolved, cache_dir)
    wheel_sizes = wheel_cache_sizes(cache_dir)
    install_wheels(python, lock_resolved, cache_dir)
    reason = validate_venv(venv_dir, python)
    if reason is not None:
        raise Stop(EXIT_STATE, f"freshly created venv does not validate ({reason}); failing closed")
    prefix, _base_prefix, python_version = venv_identity(python)
    if os.path.normpath(prefix) != os.path.normpath(venv_dir):
        raise Stop(EXIT_STATE, f"venv identity drifted between validation and receipt: {prefix}")
    receipt = receipt_payload(entries, python_version, lock_resolved, lock_bytes, wheel_sizes, venv_dir)
    atomic_write_excl(os.path.join(venv_dir, RECEIPT_NAME),
                      json.dumps(receipt, sort_keys=True, indent=2).encode("utf-8") + b"\n")
    log(f"tools bootstrap complete; venv python: {python}")
    return python


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="WFS lineage + pinned-tools bootstrap for the unattended Sedan prepare pipeline")
    parser.add_argument("root", help="proof root (lineage under <root>/lidarhd/state, tools under <root>/tools)")
    parser.add_argument("--lock-path", default=None,
                        help="tool lock file (default: <root>/tools/lidar-tools-lock.txt, "
                             "then the plan-record lock)")
    parser.add_argument("--only", choices=("lineage", "tools", "all"), default="all",
                        help="restrict the run to one stage")
    parser.add_argument("--url-config", default=None,
                        help="JSON file with {\"url\": ...} overriding the WFS GetFeature URL "
                             "(offline testing only)")
    args = parser.parse_args(argv)
    try:
        url_config = None
        if args.url_config:
            raw = read_bounded(args.url_config, LOCK_MAX_BYTES, "url config")
            try:
                url_config = json.loads(raw)
            except ValueError as error:
                raise Stop(EXIT_GUARD, f"url config at {args.url_config} is not valid JSON: {error}")
        if args.only in ("lineage", "all"):
            log(f"lineage: {fetch_lineage(args.root, url_config)}")
        if args.only in ("tools", "all"):
            log(f"venv python: {bootstrap_tools(args.root, args.lock_path)}")
    except Stop as stop:
        log(f"STOP({stop.code}): {stop.message}")
        return stop.code
    return EXIT_OK


if __name__ == "__main__":
    sys.exit(main())
