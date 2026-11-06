#!/usr/bin/env python3
"""Provider-neutral area preparation driver. Default mode is a read-only preflight."""

import argparse
import hashlib
import json
import math
import os
import re
import selectors
import shlex
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime
from urllib.parse import urlparse
from xml.sax.saxutils import escape

EXIT_OK = 0
EXIT_GUARD = 2

LATTICE_M = 3.048
HALF_FOOTPRINT_M = 1.524
CHUNK_SAMPLE_INTERVALS = 128
SOURCE_KERNEL_REACH_M = 3.048
MARGIN_M = HALF_FOOTPRINT_M + SOURCE_KERNEL_REACH_M
NO_DATA_RADIUS_M = 3.048
DEM_NODATA = -9999.0
APPROX_BOUNDS_MARGIN_M = 10.0
METERS_PER_DEG_LAT = 111320.0
POLAR_LIMIT_DEG = 84.0
UTM_ZONE_COUNT = 60

PROVEN_ORIGIN_UTM = (639841.997, 5506768.179)
WORKDIR_ENV = "PANOPTICON_GEO_PROOF_DIR"
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROVIDERS_DIR = os.path.join(SCRIPT_DIR, "providers")
PROVIDER_ID_RE = re.compile(r"[a-z0-9][a-z0-9-]{0,31}")
NAME_RE = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]{0,63}")
BANDS = ("red", "green", "blue")
SUMMARY_SHORT_NAMES = {"ign-fr": "ign-fr", "copernicus-glo30": "copernicus"}

TOOL_TIMEOUT_SECONDS = 600.0
TOOL_OUTPUT_CAP_BYTES = 32 << 20
HTTP_TIMEOUT_SECONDS = 30.0
HTTP_CAP_BYTES = 2 << 20
READ_CHUNK_BYTES = 65536
EXCERPT_BYTES = 500
EXIT_WORKER = 3
EXIT_STATE = 4
PREPARE_BUDGET_BYTES = 256 << 20
RESERVE_BYTES = 1 << 30
LEDGER_MAX_EVENTS = 4096
STAC_PAGE_LIMIT = 100
STAC_MAX_PAGES = 10
PREPARED_DIR_NAME = "prepared"
PREPARATION_DIR_NAME = "preparation"
RECEIPT_NAME = "receipt.json"
LEDGER_NAME = "commands.jsonl"
LOCK_NAME = "lock"
COMPLETED_NAME = "COMPLETED"
SOURCES_NAME = "sources.txt"
SCHEMA_VERSION = 1


class Failure(Exception):
    def __init__(self, message, code=EXIT_GUARD):
        super().__init__(message)
        self.code = code


def fail(message, code=EXIT_GUARD):
    raise Failure(message, code)


def is_finite_number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def is_int(value):
    return isinstance(value, int) and not isinstance(value, bool)


def nonempty_str(value):
    return isinstance(value, str) and value.strip() != ""


def iso_datetime(value):
    if not isinstance(value, str):
        return False
    try:
        datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return False
    return True


class ToolRun:
    def __init__(self, returncode, stdout, stderr, timed_out, wall_seconds):
        self.returncode = returncode
        self.stdout = stdout
        self.stderr = stderr
        self.timed_out = timed_out
        self.wall_seconds = wall_seconds


def run_tool(argv, timeout_seconds=TOOL_TIMEOUT_SECONDS, cap_bytes=TOOL_OUTPUT_CAP_BYTES,
             input_bytes=None):
    """The one subprocess seam: exact argv, wall timeout, bounded capture."""
    if input_bytes is not None and len(input_bytes) > 4096:
        fail("tool stdin exceeds the bounded coordinate batch", EXIT_WORKER)
    started = time.monotonic()
    try:
        proc = subprocess.Popen(argv,
                                stdin=subprocess.PIPE if input_bytes is not None else subprocess.DEVNULL,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    except OSError as error:
        fail(f"tool failed to start ({argv[0]}): {error}", EXIT_WORKER)
    if input_bytes is not None:
        try:
            proc.stdin.write(input_bytes)
            proc.stdin.close()
        except OSError:
            pass
    selector = selectors.DefaultSelector()
    selector.register(proc.stdout, selectors.EVENT_READ, "out")
    selector.register(proc.stderr, selectors.EVENT_READ, "err")
    buffers = {"out": bytearray(), "err": bytearray()}
    deadline = started + timeout_seconds
    timed_out = False
    finished = False
    try:
        while selector.get_map():
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                timed_out = True
                break
            for key, _ in selector.select(remaining):
                chunk = os.read(key.fd, READ_CHUNK_BYTES)
                if not chunk:
                    selector.unregister(key.fileobj)
                    continue
                buffers[key.data].extend(chunk)
                if len(buffers[key.data]) > cap_bytes:
                    fail(f"tool output exceeded {cap_bytes} bytes ({argv[0]})", EXIT_WORKER)
        finished = not timed_out
    finally:
        if not finished:
            proc.kill()
        try:
            proc.wait(timeout=max(0.01, deadline - time.monotonic()))
        except subprocess.TimeoutExpired:
            timed_out = True
            proc.kill()
            proc.wait()
        selector.close()
        proc.stdout.close()
        proc.stderr.close()
    return ToolRun(proc.returncode, bytes(buffers["out"]).decode("utf-8", "replace"),
                   bytes(buffers["err"]).decode("utf-8", "replace"), timed_out,
                   time.monotonic() - started)


def http_post_json(url, body, timeout=HTTP_TIMEOUT_SECONDS, cap_bytes=HTTP_CAP_BYTES):
    """The one HTTP seam (STAC search). Tests replace this function."""
    if not url.startswith("https://"):
        fail(f"refusing non-https catalogue URL: {url}")
    data = json.dumps(body, sort_keys=True).encode("utf-8")
    request = urllib.request.Request(
        url, data=data, method="POST",
        headers={"Content-Type": "application/json", "Accept": "application/json"})
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            if not response.geturl().startswith("https://"):
                fail(f"catalogue redirected to a non-https URL: {response.geturl()}", EXIT_WORKER)
            raw = response.read(cap_bytes + 1)
    except (OSError, urllib.error.URLError) as error:
        fail(f"STAC request failed: {error}", EXIT_WORKER)
    if len(raw) > cap_bytes:
        fail(f"STAC response exceeds {cap_bytes} bytes", EXIT_WORKER)
    try:
        parsed = json.loads(raw.decode("utf-8"))
    except ValueError as error:
        fail(f"STAC response is not JSON: {error}", EXIT_WORKER)
    if not isinstance(parsed, dict):
        fail("STAC response is not a JSON object", EXIT_WORKER)
    return parsed


def now_iso():
    return datetime.now().astimezone().isoformat(timespec="milliseconds")


def sha256_bytes(data):
    return hashlib.sha256(data).hexdigest()


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for block in iter(lambda: handle.read(READ_CHUNK_BYTES), b""):
            digest.update(block)
    return digest.hexdigest()


def canonical_sha256(value):
    return sha256_bytes(json.dumps(value, sort_keys=True, separators=(",", ":")).encode("utf-8"))


def fsync_dir(path):
    descriptor = os.open(path, os.O_RDONLY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def atomic_write_excl(path, data):
    """Write a new file atomically; an existing target is refused, never replaced."""
    tmp_path = f"{path}.{os.getpid()}.{time.monotonic_ns()}.tmp"
    descriptor = os.open(tmp_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o644)
    try:
        with os.fdopen(descriptor, "wb") as handle:
            handle.write(data)
            handle.flush()
            os.fsync(handle.fileno())
        try:
            os.link(tmp_path, path)
        except FileExistsError:
            fail(f"refusing to overwrite existing file: {path}")
    finally:
        os.unlink(tmp_path)
    fsync_dir(os.path.dirname(path))


def publish_no_overwrite(source, destination):
    try:
        os.link(source, destination)
    except FileExistsError:
        fail(f"refusing to overwrite existing file: {destination}")


def pid_alive(pid):
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


class Ledger:
    def __init__(self, path):
        self.path = path
        self.count = 0

    def append(self, event, **fields):
        if self.count >= LEDGER_MAX_EVENTS:
            fail(f"ledger event limit {LEDGER_MAX_EVENTS} reached")
        line = json.dumps({"time": now_iso(), "event": event, **fields}, sort_keys=True)
        with open(self.path, "a", encoding="utf-8") as handle:
            handle.write(line + "\n")
            handle.flush()
            os.fsync(handle.fileno())
        self.count += 1


def gdal_json(path, stats=False):
    argv = ["gdalinfo", "--config", "GDAL_PAM_ENABLED", "NO", "-json"] + (["-stats"] if stats else []) + [path]
    run = run_tool(argv)
    if run.timed_out or run.returncode != 0:
        fail(f"gdalinfo failed on {os.path.basename(path)}: {run.stderr.strip()[-EXCERPT_BYTES:]}",
             EXIT_WORKER)
    try:
        parsed = json.loads(run.stdout)
    except ValueError as error:
        fail(f"gdalinfo output is not JSON for {os.path.basename(path)}: {error}", EXIT_WORKER)
    return parsed


def find_values(node, key):
    if isinstance(node, dict):
        for name, value in node.items():
            if name == key:
                yield value
            else:
                yield from find_values(value, key)
    elif isinstance(node, list):
        for value in node:
            yield from find_values(value, key)


def require_full_valid(info, label):
    bands = info.get("bands") or []
    if not bands:
        fail(f"{label}: no validity statistics reported", EXIT_WORKER)
    values = []
    for band in bands:
        stats = list(find_values(band, "STATISTICS_VALID_PERCENT"))
        if len(stats) != 1:
            fail(f"{label}: validity statistics missing or ambiguous for a band", EXIT_WORKER)
        values.extend(stats)
    for value in values:
        try:
            percent = float(value)
        except (TypeError, ValueError):
            fail(f"{label}: unreadable validity statistic {value!r}", EXIT_WORKER)
        if not math.isfinite(percent) or percent != 100.0:
            fail(f"{label}: {percent} % valid inside the AOI (no-data present); refusing silent gaps")


def cs2cs_batch(points, spec):
    if not points:
        return []
    text = "".join(f"{x:.12f} {y:.12f}\n" for x, y in points).encode("ascii")
    run = run_tool(["cs2cs", "-f", "%.12f", *spec], input_bytes=text)
    if run.timed_out or run.returncode != 0:
        fail(f"cs2cs failed: {run.stderr.strip()[-EXCERPT_BYTES:]}", EXIT_WORKER)
    rows = [line.split() for line in run.stdout.splitlines() if line.strip()]
    if len(rows) != len(points):
        fail(f"cs2cs returned {len(rows)} rows for {len(points)} inputs", EXIT_WORKER)
    converted = []
    for row in rows:
        try:
            x, y = float(row[0]), float(row[1])
        except (IndexError, ValueError):
            fail(f"cs2cs returned an unreadable row: {' '.join(row)}", EXIT_WORKER)
        if not (math.isfinite(x) and math.isfinite(y)):
            fail("cs2cs returned a non-finite coordinate", EXIT_WORKER)
        converted.append((x, y))
    return converted


def lonlat_to_utm(points, epsg):
    return cs2cs_batch(points, ["+proj=longlat", "+datum=WGS84", "+no_defs", "+to", f"EPSG:{epsg}"])


def utm_to_lonlat(points, epsg):
    return cs2cs_batch(points, [f"EPSG:{epsg}", "+to", "+proj=longlat", "+datum=WGS84", "+no_defs"])


def outward_window(bounds, gsd):
    xmin, ymin, xmax, ymax = bounds
    window = (math.floor(xmin / gsd) * gsd, math.floor(ymin / gsd) * gsd,
              math.ceil(xmax / gsd) * gsd, math.ceil(ymax / gsd) * gsd)
    if window[2] <= window[0] or window[3] <= window[1]:
        fail(f"outward window at {gsd} m has a non-positive width")
    return window


def utm_geometry(centre_easting, centre_northing, size_m, grid):
    origin_e = centre_easting - size_m / 2
    origin_n = centre_northing - size_m / 2
    grid_m = grid["grid_m"]
    return {
        "origin": (origin_e, origin_n),
        "playable": (origin_e, origin_n, origin_e + size_m, origin_n + size_m),
        "storage": (origin_e, origin_n, origin_e + grid_m, origin_n + grid_m),
        "minimal": (origin_e - MARGIN_M, origin_n - MARGIN_M,
                    origin_e + grid_m + MARGIN_M, origin_n + grid_m + MARGIN_M),
    }


def box_corners(bounds):
    xmin, ymin, xmax, ymax = bounds
    return [(xmin, ymin), (xmax, ymin), (xmax, ymax), (xmin, ymax)]


def profile_error(provider_id, reason):
    fail(f"provider {provider_id!r} profile invalid: {reason}")


def validate_coverage(box, provider_id):
    if not (isinstance(box, list) and len(box) == 4 and all(is_finite_number(v) for v in box)):
        profile_error(provider_id, "coverage must be [lonmin, latmin, lonmax, latmax] finite numbers")
    lonmin, latmin, lonmax, latmax = box
    if not (-180 <= lonmin < lonmax <= 180 and -90 <= latmin < latmax <= 90):
        profile_error(provider_id, "coverage out of WGS84 range or min not below max")


def validate_imagery(imagery, provider_id):
    if not isinstance(imagery, dict):
        profile_error(provider_id, "imagery must be an object")
    kind = imagery.get("kind")
    if kind == "delegate":
        if not nonempty_str(imagery.get("delegateTarget")):
            profile_error(provider_id, "imagery delegate target missing")
        return
    if kind != "stac":
        profile_error(provider_id, "imagery kind must be stac or delegate")
    url = imagery.get("catalogueUrl")
    if not (isinstance(url, str) and url.startswith("https://")):
        profile_error(provider_id, "imagery catalogueUrl must be https://")
    if not nonempty_str(imagery.get("collection")):
        profile_error(provider_id, "imagery collection missing")
    window = imagery.get("dateWindow")
    if not (isinstance(window, list) and len(window) == 2 and all(iso_datetime(v) for v in window)):
        profile_error(provider_id, "imagery dateWindow must be two ISO 8601 timestamps")
    dates = [datetime.fromisoformat(v.replace("Z", "+00:00")) for v in window]
    if any(d.tzinfo is None for d in dates) or dates[0] > dates[1]:
        profile_error(provider_id, "imagery dateWindow must be ordered and timezone-qualified")
    cloud = imagery.get("cloudCoverMax")
    if imagery.get("cloudCoverProperty") != "eo:cloud_cover":
        profile_error(provider_id, "imagery cloudCoverProperty must be eo:cloud_cover")
    if not (is_finite_number(cloud) and 0 <= cloud <= 100):
        profile_error(provider_id, "imagery cloudCoverMax must be within 0..100")
    if imagery.get("bandOrder") != list(BANDS):
        profile_error(provider_id, "imagery bandOrder must be exactly red, green, blue")
    keys = imagery.get("assetKeys")
    if not (isinstance(keys, dict) and all(nonempty_str(keys.get(band)) for band in BANDS)):
        profile_error(provider_id, "imagery assetKeys must name red, green, and blue")
    if len({keys[band] for band in BANDS}) != 3:
        profile_error(provider_id, "imagery assetKeys must identify distinct bands")
    if imagery.get("bandIds") != dict(zip(BANDS, ("B04", "B03", "B02"))):
        profile_error(provider_id, "imagery bandIds must be red B04, green B03, blue B02")
    if imagery.get("scaleOffsetFromMetadata") is not True:
        profile_error(provider_id, "imagery scale and offset must come from metadata")
    if imagery.get("reflectanceRange") != [0, 0.2] or imagery.get("outputRange") != [0, 255]:
        profile_error(provider_id, "imagery reflectanceRange must be [0, 0.2] and outputRange [0, 255]")
    if not is_int(imagery.get("nodataValue")):
        profile_error(provider_id, "imagery nodataValue must be an integer")
    if imagery.get("publicHttpsOnly") is not True or imagery.get("noRequesterPays") is not True:
        profile_error(provider_id, "imagery must be public HTTPS with no RequesterPays")
    bucket = imagery.get("bucketBaseUrl")
    if bucket != "https://sentinel-cogs.s3.us-west-2.amazonaws.com/":
        profile_error(provider_id, "imagery bucketBaseUrl must be the public sentinel-cogs HTTPS bucket")


def validate_profile(profile, provider_id):
    if not isinstance(profile, dict):
        profile_error(provider_id, "not a JSON object")
    def check_numbers(value):
        if isinstance(value, float) and not math.isfinite(value):
            profile_error(provider_id, "non-finite number")
        if isinstance(value, dict):
            for child in value.values():
                check_numbers(child)
        elif isinstance(value, list):
            for child in value:
                check_numbers(child)
    check_numbers(profile)
    if profile.get("id") != provider_id:
        profile_error(provider_id, "id does not match the filename")
    if not nonempty_str(profile.get("label")):
        profile_error(provider_id, "label missing")
    validate_coverage(profile.get("coverage"), provider_id)
    caps = profile.get("capabilities")
    if not (isinstance(caps, list) and caps and all(nonempty_str(c) for c in caps)):
        profile_error(provider_id, "capabilities must be a non-empty list of strings")

    elevation = profile.get("elevation")
    if not isinstance(elevation, dict):
        profile_error(provider_id, "elevation missing")
    download = elevation.get("download")
    if not isinstance(download, dict) or download.get("kind") not in ("delegate", "tile-grid"):
        profile_error(provider_id, "elevation download kind must be delegate or tile-grid")
    if download["kind"] == "tile-grid":
        base = download.get("baseUrl")
        if not (isinstance(base, str) and base.startswith("https://")):
            profile_error(provider_id, "tile-grid baseUrl must be https://")
    if elevation.get("strategy") != ("delegate" if download["kind"] == "delegate" else "full"):
        profile_error(provider_id, "strategy does not match the supported download kind")
    for key in ("sourceCrs", "verticalDatum"):
        if not nonempty_str(elevation.get(key)):
            profile_error(provider_id, f"elevation {key} missing")
    if elevation.get("surfaceType") not in ("bareEarth", "surfaceModel"):
        profile_error(provider_id, "elevation surfaceType must be bareEarth or surfaceModel")
    if elevation.get("resampling") != "bilinear":
        profile_error(provider_id, "elevation resampling must be bilinear")
    no_data = elevation.get("noData")
    if not (isinstance(no_data, dict) and no_data.get("policy") == "nearest-valid"
            and is_finite_number(no_data.get("radiusMeters"))
            and math.isclose(no_data["radiusMeters"], NO_DATA_RADIUS_M)
            and no_data.get("edgePolicy") == "fail"):
        profile_error(provider_id, "noData must be nearest-valid, radius 3.048 m, edge fail")

    licence = profile.get("licence")
    if not (isinstance(licence, dict) and nonempty_str(licence.get("name"))):
        profile_error(provider_id, "licence name missing")
    attribution = profile.get("attribution")
    if not (isinstance(attribution, dict) and attribution):
        profile_error(provider_id, "attribution missing")

    manifest = profile.get("manifestRequirements")
    if not (isinstance(manifest, dict) and manifest.get("target") == "utm"
            and manifest.get("zoneRange") == [1, UTM_ZONE_COUNT]
            and manifest.get("hemisphereRequired") is True):
        profile_error(provider_id, "manifestRequirements must require utm, zone 1..60, hemisphere")
    tolerances = manifest.get("tolerances")
    if not isinstance(tolerances, dict) or not isinstance(tolerances.get("verticalMeters"), dict):
        profile_error(provider_id, "manifest vertical tolerance missing")
    tolerance = manifest.get("tolerances", {}).get("verticalMeters", {}).get("schemaValue")
    if not is_finite_number(tolerance) or tolerance <= 0:
        profile_error(provider_id, "manifest vertical tolerance must be finite and positive")
    if download["kind"] == "tile-grid":
        if (download.get("tileDegrees") != 1 or download.get("tileOrigin") != "floor"
                or download.get("latDigits") != 2 or download.get("lonDigits") != 3
                or not nonempty_str(download.get("tilePattern"))):
            profile_error(provider_id, "unsupported tile-grid parameters")
        if not is_finite_number(elevation.get("outputGsdMeters")) or elevation["outputGsdMeters"] <= 0:
            profile_error(provider_id, "outputGsdMeters must be finite and positive")
        if elevation["sourceCrs"] != "EPSG:4326" or elevation.get("pixelInterpretation") != "PixelIsPoint":
            profile_error(provider_id, "tile-grid requires geographic PixelIsPoint elevation")
        pattern = download["tilePattern"]
        try:
            sample = pattern.format(NS="N", EW="E", lat02d="49", lon03d="004")
        except (KeyError, ValueError, IndexError, AttributeError):
            profile_error(provider_id, "invalid tile pattern")
        if not re.fullmatch(r"[A-Za-z0-9_./-]+", sample) or ".." in sample or sample.startswith("/"):
            profile_error(provider_id, "unsafe tile pattern")
        if not isinstance(attribution.get("elevation"), dict):
            profile_error(provider_id, "elevation attribution must be an object")
        for key in ("source", "derived", "liability"):
            if not nonempty_str(attribution.get("elevation", {}).get(key)):
                profile_error(provider_id, f"missing elevation attribution {key}")
        for key in ("licencePdf", "registry", "readme"):
            if not nonempty_str(licence.get(key)):
                profile_error(provider_id, f"missing licence {key}")
        for key in ("noEndorsement", "bindSubsequent", "asIs", "iprStaysProvider", "terminationOnBreach"):
            if licence.get(key) is not True:
                profile_error(provider_id, f"missing licence obligation {key}")
        rights = licence.get("rights")
        if not isinstance(rights, dict) or any(rights.get(k) is not True for k in (
                "reproduction", "distribution", "communication", "adaptation", "combination",
                "worldwide", "unlimitedInTime", "freeOfCharge")):
            profile_error(provider_id, "missing licence rights")
    else:
        if download.get("delegateTarget") != "prepare-sedan-proof.sh":
            profile_error(provider_id, "unsupported delegate target")
        if download.get("delegateArgs") != {"preflight": ["{root}"], "prepare": ["{root}", "--prepare"]}:
            profile_error(provider_id, "unsupported delegate arguments")
        if (profile.get("provenCentre") != [49.702, 4.947] or profile.get("provenSizeMeters") != 1100
                or profile.get("provenName") != "sedan-proof"):
            profile_error(provider_id, "delegate must pin the proven Sedan request")

    imagery = profile.get("imagery")
    if imagery is not None:
        validate_imagery(imagery, provider_id)
    if download["kind"] == "tile-grid":
        if not isinstance(imagery, dict) or imagery.get("kind") != "stac":
            profile_error(provider_id, "tile-grid preparation requires STAC imagery")
        expected = {"bandGsdMeters": 10.0, "nodataValue": 0, "maskBeforeScaling": True,
                    "requireFullAoi": True, "outputBands": 3, "outputType": "Byte",
                    "outputOrder": [1, 2, 3], "imageryPixelsPerSample": 10}
        if any(imagery.get(k) != v for k, v in expected.items()):
            profile_error(provider_id, "unsupported imagery output or mask policy")
        if imagery.get("selection") != {"pages": "all", "order": "leastCloud", "suitable": "complete-aoi",
                                       "tieBreak": "stableItemId", "recordQueryItemAssetsHash": True}:
            profile_error(provider_id, "unsupported STAC selection policy")
        if not isinstance(attribution.get("imagery"), dict):
            profile_error(provider_id, "imagery attribution must be an object")
        for key in ("source", "derived"):
            if not nonempty_str(attribution.get("imagery", {}).get(key)):
                profile_error(provider_id, f"missing imagery attribution {key}")
        legal = imagery.get("licence")
        if not isinstance(legal, dict) or any(not nonempty_str(legal.get(k)) for k in ("name", "legalNotice")):
            profile_error(provider_id, "missing imagery licence")
        if legal.get("liabilityWaiver") is not True or legal.get("warranty") is not False:
            profile_error(provider_id, "missing imagery liability terms")


def load_profile(provider_id):
    if not PROVIDER_ID_RE.fullmatch(provider_id):
        fail(f"provider id {provider_id!r} is not a valid profile identifier")
    path = os.path.join(PROVIDERS_DIR, provider_id + ".json")
    if not os.path.isfile(path):
        fail(f"provider {provider_id!r}: no profile at scripts/geo/providers/{provider_id}.json")
    try:
        with open(path, encoding="utf-8") as handle:
            profile = json.load(handle)
    except (OSError, ValueError) as error:
        fail(f"provider {provider_id!r} profile unreadable: {error}")
    validate_profile(profile, provider_id)
    return profile


def parse_center(text, provider_id):
    parts = text.split(",")
    if len(parts) != 2:
        fail(f"provider {provider_id!r}: --center must be <lat>,<lon>")
    try:
        lat, lon = float(parts[0]), float(parts[1])
    except ValueError:
        fail(f"provider {provider_id!r}: --center values must be numbers")
    if not (math.isfinite(lat) and math.isfinite(lon)):
        fail(f"provider {provider_id!r}: --center must be finite")
    if not -90 <= lat <= 90:
        fail(f"provider {provider_id!r}: --center latitude {lat} outside -90..90")
    if not -180 <= lon <= 180:
        fail(f"provider {provider_id!r}: --center longitude {lon} outside -180..180")
    return lat, lon


def parse_size(text, provider_id):
    try:
        size = float(text)
    except ValueError:
        fail(f"provider {provider_id!r}: --size must be a number of meters")
    if not (math.isfinite(size) and size > 0):
        fail(f"provider {provider_id!r}: --size must be finite and positive")
    return size


def validate_name(name, provider_id):
    if ".." in name or not NAME_RE.fullmatch(name):
        fail(f"provider {provider_id!r}: --name {name!r} must match [A-Za-z0-9][A-Za-z0-9._-]{{0,63}} with no '..'")
    return name


def resolve_workdir(positional):
    workdir = positional or os.environ.get(WORKDIR_ENV)
    if not workdir:
        fail(f"missing work dir: pass <workdir> or set {WORKDIR_ENV}")
    if "\0" in workdir:
        fail("work dir contains a null byte")
    return os.path.abspath(workdir)


def computed_zone(lon):
    return min(UTM_ZONE_COUNT, int(math.floor((lon + 180.0) / 6.0)) + 1)


def resolve_utm(lat, lon, override, provider_id):
    if not -80 <= lat <= POLAR_LIMIT_DEG:
        fail(f"provider {provider_id!r}: centre latitude {lat} outside UTM limits -80..84")
    if override is not None and not 1 <= override <= UTM_ZONE_COUNT:
        fail(f"provider {provider_id!r}: --utm-zone must be 1..{UTM_ZONE_COUNT}")
    computed = computed_zone(lon)
    zone = override if override is not None else computed
    hemisphere = "north" if lat >= 0 else "south"
    epsg = (32600 if hemisphere == "north" else 32700) + zone
    return zone, computed, hemisphere, epsg


def chunk_grid(size_m):
    chunk_m = CHUNK_SAMPLE_INTERVALS * LATTICE_M
    n_chunks = int(math.ceil(size_m / chunk_m - 1e-9))
    if n_chunks <= 0:
        fail("chunk rounding produced a non-positive chunk count")
    grid_m = n_chunks * chunk_m
    return {
        "chunk_m": chunk_m,
        "n_chunks": n_chunks,
        "grid_m": grid_m,
        "grid_points": n_chunks * CHUNK_SAMPLE_INTERVALS + 1,
    }


def approx_wgs84_box(lat, lon, grid_m, size_m=None):
    # Storage is anchored at the playable SW corner, not centred on the request.
    size_m = grid_m if size_m is None else size_m
    reach = max(size_m / 2, grid_m - size_m / 2) + MARGIN_M + APPROX_BOUNDS_MARGIN_M
    lat_delta = reach / METERS_PER_DEG_LAT
    lon_delta = reach / (METERS_PER_DEG_LAT * math.cos(math.radians(lat)))
    return (lon - lon_delta, lat - lat_delta, lon + lon_delta, lat + lat_delta)


def check_extent(box, zone, profile, provider_id):
    lonmin, latmin, lonmax, latmax = box
    cov_lonmin, cov_latmin, cov_lonmax, cov_latmax = profile["coverage"]
    if latmin < -80 or latmax > 84 or latmin < 0 < latmax:
        fail(f"provider {provider_id!r}: extent crosses UTM latitude or hemisphere limits")
    uncovered = []
    for label, (corner_lon, corner_lat) in (
            ("SW", (lonmin, latmin)), ("NW", (lonmin, latmax)),
            ("NE", (lonmax, latmax)), ("SE", (lonmax, latmin))):
        if not (cov_lonmin <= corner_lon <= cov_lonmax and cov_latmin <= corner_lat <= cov_latmax):
            uncovered.append(f"{label} ({corner_lon:.6f}, {corner_lat:.6f})")
    if uncovered:
        fail(f"provider {provider_id!r}: storage extent outside profile coverage "
             f"{profile['coverage']}; uncovered corner(s): {', '.join(uncovered)}")
    if lonmin < -180 or lonmax > 180:
        fail(f"provider {provider_id!r}: extent crosses the antimeridian; not supported yet")
    for label, corner_lon in (("SW", lonmin), ("NE", lonmax)):
        if computed_zone(corner_lon) != zone:
            fail(f"provider {provider_id!r}: extent crosses UTM zones ({label} corner in zone "
                 f"{computed_zone(corner_lon)}, plan zone {zone}); not supported yet")


def check_proven_only(profile, lat, lon, size_m, name, provider_id):
    centre = profile["provenCentre"]
    if (abs(lat - centre[0]) > 1e-9 or abs(lon - centre[1]) > 1e-9
            or size_m != profile["provenSizeMeters"] or name != profile["provenName"]):
        fail(f"provider {provider_id!r} is proven Sedan only (centre {centre[0]},{centre[1]}, "
             f"size {profile['provenSizeMeters']} m, name {profile['provenName']!r}); "
             f"other centres, sizes, and names are rejected")


def imagery_lines(profile, workdir):
    imagery = profile.get("imagery")
    if imagery is None:
        return ["  none"]
    if imagery["kind"] == "delegate":
        return [f"  delegate {imagery['delegateTarget']} (pinned {imagery.get('pinnedArchive', 'n/a')})"]
    return [
        f"  STAC {imagery['catalogueUrl']}",
        f"  collection {imagery['collection']}, dates {imagery['dateWindow'][0]} .. {imagery['dateWindow'][1]}",
        f"  cloud <= {imagery['cloudCoverMax']}, selection {imagery['selection']['pages']} pages, "
        f"{imagery['selection']['order']}, {imagery['selection']['suitable']}, "
        f"{imagery['selection']['tieBreak']} (no network in preflight)",
    ]


def print_plan(profile, provider_id, args, lat, lon, size_m, name, workdir, utm, grid):
    zone, computed, hemisphere, epsg = utm
    elevation = profile["elevation"]
    surface = elevation["surfaceType"]
    storage = grid["grid_m"]
    margin = MARGIN_M
    local_storage = (0.0, 0.0, storage, storage)
    local_minimal = (-margin, -margin, storage + margin, storage + margin)
    approx = approx_wgs84_box(lat, lon, storage, size_m)
    tol = profile["manifestRequirements"]["tolerances"]["verticalMeters"]["schemaValue"]
    lines = [
        "prepare_area preflight (read-only: nothing written, nothing spawned)",
        f"provider: {provider_id} ({profile['label']})",
        f"surface: {surface}, vertical datum {elevation['verticalDatum']}, source CRS {elevation['sourceCrs']}",
        f"centre: lat {lat:.9f} lon {lon:.9f}",
        f"size: {size_m:g} m, name: {name}",
        f"work dir: {workdir} (not created)",
        f"utm: zone {zone} {hemisphere} EPSG:{epsg}",
    ]
    if args.utm_zone is not None and args.utm_zone != computed:
        lines.append(f"  override: --utm-zone {args.utm_zone} (computed zone {computed})")
    lines.append("  local grid: absolute UTM easting/northing are resolved by cs2cs in --prepare, not here")
    if "provenCentre" in profile:
        lines.append(f"  pinned origin: EPSG:{epsg} {PROVEN_ORIGIN_UTM[0]} {PROVEN_ORIGIN_UTM[1]} (proven Sedan)")
    lines += [
        f"grid: lattice {LATTICE_M:g} chunk {grid['chunk_m']:.3f} n_chunks {grid['n_chunks']} "
        f"({grid['n_chunks']} x {grid['n_chunks']}) grid {storage:.3f} m grid_points {grid['grid_points']}",
        f"margin: {margin:.3f} m (HALF_FOOTPRINT {HALF_FOOTPRINT_M} + REACH {SOURCE_KERNEL_REACH_M}); no 50 m buffer",
        f"local storage bounds: [{local_storage[0]:g}, {local_storage[1]:g}, "
        f"{local_storage[2]:.3f}, {local_storage[3]:.3f}]",
        f"local source_minimal: [{local_minimal[0]:.3f}, {local_minimal[1]:.3f}, "
        f"{local_minimal[2]:.3f}, {local_minimal[3]:.3f}]",
        f"approx WGS84 bounds: [{approx[0]:.6f}, {approx[1]:.6f}, {approx[2]:.6f}, {approx[3]:.6f}] "
        "(exact via cs2cs in --prepare)",
    ]
    if "provenCentre" in profile:
        lines.append("coverage: PASS (proven Sedan extent; storage and apron covered by construction)")
    else:
        lines.append(f"coverage: approximate guard within {profile['coverage']}; exact check before download")
    lines.append(f"strategy: {elevation['strategy']} ({elevation['download']['kind']})")
    lines.append(f"licence: {profile['licence']['name']}")
    attribution = profile["attribution"]
    for key in ("source", "derived"):
        if key in attribution:
            lines.append(f"attribution {key}: {attribution[key]}")
    lines.append("imagery:")
    lines += imagery_lines(profile, workdir)
    dem_gsd = "1 m" if surface == "bareEarth" else f"{elevation.get('outputGsdMeters', 30.0):g} m"
    lines += [
        "outputs:",
        f"  prepared/{'sedan' if 'provenCentre' in profile else name}-extent.kml: one EPSG:4326 polygon",
        f"  prepared/{'sedan' if 'provenCentre' in profile else name}-dem-{epsg}.tif: Float32 {dem_gsd}, nodata {DEM_NODATA}",
        f"  prepared/{'sedan' if 'provenCentre' in profile else name}-ortho-{epsg}.tif: uint8 RGB, 3 bands",
        f"  manifest: {'sedan-ign-proof.json' if 'provenCentre' in profile else name + '.json'} "
        f"(verticalDatum {elevation['verticalDatum']}, tolerances.verticalMeters {tol} schema-required, not evaluated, no PASS)",
        "  sources.txt, preparation/receipt.json, preparation/commands.jsonl",
    ]
    if "provenCentre" in profile:
        argv = [os.path.join(SCRIPT_DIR, profile["elevation"]["download"]["delegateTarget"]), workdir]
        lines.append(f"delegate argv (preview, not executed): {shlex.join(argv)}")
    short = SUMMARY_SHORT_NAMES.get(provider_id, provider_id)
    if surface == "bareEarth":
        summary = f"{short}/{epsg}/{grid['n_chunks']} x {grid['n_chunks']}/{storage:.3f}"
    else:
        summary = f"{short}/{epsg}/{surface}"
    lines.append(f"summary: {summary}")
    print("\n".join(lines))


def stac_query_body(imagery, bbox):
    return {
        "collections": [imagery["collection"]],
        "bbox": list(bbox),
        "datetime": f"{imagery['dateWindow'][0]}/{imagery['dateWindow'][1]}",
        "query": {imagery["cloudCoverProperty"]: {"lte": imagery["cloudCoverMax"]}},
        "limit": STAC_PAGE_LIMIT,
    }


def stac_search_all(imagery, bbox):
    base = stac_query_body(imagery, bbox)
    request_body = base
    pages = []
    seen = set()
    for _ in range(STAC_MAX_PAGES):
        request_hash = canonical_sha256(request_body)
        if request_hash in seen:
            fail("STAC pagination repeated a request", EXIT_WORKER)
        seen.add(request_hash)
        page = http_post_json(imagery["catalogueUrl"], request_body)
        if not isinstance(page.get("features"), list):
            fail("STAC page has no features array", EXIT_WORKER)
        pages.append(page)
        next_body = None
        for link in page.get("links") or []:
            if isinstance(link, dict) and link.get("rel") == "next":
                if link.get("method", "GET") != "POST":
                    fail("STAC next link is not a POST; unsupported", EXIT_WORKER)
                if link.get("href") != imagery["catalogueUrl"]:
                    fail("STAC next link changed the catalogue endpoint", EXIT_WORKER)
                next_body = link.get("body") or {}
                if not isinstance(next_body, dict):
                    fail("STAC next link body must be an object", EXIT_WORKER)
                break
        if next_body is None:
            return base, pages
        request_body = {**base, **next_body}
    fail(f"STAC search exceeded {STAC_MAX_PAGES} pages; refusing an incomplete selection",
         EXIT_WORKER)


def item_suitable(item, imagery, bbox):
    if not nonempty_str(item.get("id")):
        return None
    props = item.get("properties") or {}
    if item.get("collection") != imagery["collection"] or not isinstance(props, dict):
        return None
    timestamp = props.get("datetime")
    if not iso_datetime(timestamp):
        return None
    stamp = datetime.fromisoformat(timestamp.replace("Z", "+00:00"))
    window = [datetime.fromisoformat(v.replace("Z", "+00:00")) for v in imagery["dateWindow"]]
    if stamp.tzinfo is None or not window[0] <= stamp <= window[1]:
        return None
    cloud = props.get(imagery["cloudCoverProperty"])
    if not is_finite_number(cloud) or not 0 <= cloud <= imagery["cloudCoverMax"]:
        return None
    item_bbox = item.get("bbox")
    if not (isinstance(item_bbox, list) and len(item_bbox) == 4
            and all(is_finite_number(v) for v in item_bbox)):
        return None
    if not (item_bbox[0] <= bbox[0] and item_bbox[1] <= bbox[1]
            and item_bbox[2] >= bbox[2] and item_bbox[3] >= bbox[3]):
        return None
    assets = item.get("assets") or {}
    if not isinstance(assets, dict):
        return None
    chosen = {}
    for band in BANDS:
        key = imagery["assetKeys"][band]
        asset = assets.get(key)
        if not isinstance(asset, dict):
            return None
        href = asset.get("href")
        if not (isinstance(href, str) and href.startswith(imagery["bucketBaseUrl"])
                and urlparse(href).scheme == "https"
                and urlparse(href).netloc == urlparse(imagery["bucketBaseUrl"]).netloc):
            return None
        bands = asset.get("raster:bands")
        if not (isinstance(bands, list) and len(bands) == 1 and isinstance(bands[0], dict)):
            return None
        meta = bands[0]
        scale, offset = meta.get("scale"), meta.get("offset")
        if not (is_finite_number(scale) and scale > 0 and is_finite_number(offset)):
            return None
        if meta.get("nodata") != imagery["nodataValue"]:
            return None
        chosen[band] = {"href": href, "assetKey": key, "scale": scale, "offset": offset,
                        "nodata": meta["nodata"]}
    return {"id": item["id"], "cloud": cloud, "datetime": props.get("datetime"),
            "mgrs": props.get("s2:mgrs_tile"), "bbox": item_bbox, "chosen": chosen}


def select_item(features, imagery, bbox, provider_id):
    candidates = []
    for item in features:
        if isinstance(item, dict):
            found = item_suitable(item, imagery, bbox)
            if found is not None:
                candidates.append(found)
    if not candidates:
        fail(f"provider {provider_id!r}: no suitable complete-AOI item among {len(features)} "
             f"STAC items")
    candidates.sort(key=lambda candidate: (candidate["cloud"], candidate["id"]))
    return candidates[0], len(candidates)


def dem_tile(download, bbox, provider_id):
    lonmin, latmin, lonmax, latmax = bbox
    lat_floor, lon_floor = math.floor(latmin), math.floor(lonmin)
    if math.floor(latmax) != lat_floor or math.floor(lonmax) != lon_floor:
        fail(f"provider {provider_id!r}: storage extent crosses a DEM tile boundary; "
             f"not supported yet")
    ns = "N" if lat_floor >= 0 else "S"
    ew = "E" if lon_floor >= 0 else "W"
    lat_text = f"{abs(lat_floor):0{download['latDigits']}d}"
    lon_text = f"{abs(lon_floor):0{download['lonDigits']}d}"
    path = (download["tilePattern"].replace("{NS}", ns).replace("{EW}", ew)
            .replace("{lat02d}", lat_text).replace("{lon03d}", lon_text))
    url = download["baseUrl"] + path
    if not url.startswith("https://"):
        fail(f"provider {provider_id!r}: DEM tile URL must be https://")
    return {"tile": f"{ns}{lat_text}_{ew}{lon_text}", "url": url}


def verify_dem_tile(info, bbox, label):
    bands = info.get("bands") or []
    if info.get("driverShortName") != "GTiff" or len(bands) != 1 or bands[0].get("type") != "Float32":
        fail(f"{label}: expected a single-band Float32 GeoTIFF", EXIT_WORKER)
    wkt = (info.get("coordinateSystem") or {}).get("wkt", "")
    codes = re.findall(r'(?:ID|AUTHORITY)\s*\[\s*"EPSG"\s*,\s*"?(\d+)"?\s*\]', wkt)
    if not codes or codes[-1] != "4326":
        fail(f"{label}: DEM tile is not EPSG:4326", EXIT_WORKER)
    transform = info.get("geoTransform", [])
    size = info.get("size", [])
    if (len(transform) != 6 or not all(is_finite_number(v) for v in transform)
            or len(size) != 2 or not all(is_int(v) and v > 0 for v in size)):
        fail(f"{label}: invalid DEM geometry", EXIT_WORKER)
    x0, dx, rx, y0, ry, dy = transform
    width, height = size
    if dx <= 0 or dy >= 0 or rx != 0 or ry != 0 or not math.isclose(dx, -dy):
        fail(f"{label}: expected north-up square DEM pixels", EXIT_WORKER)
    east, south = x0 + width * dx, y0 + height * dy
    lonmin, latmin, lonmax, latmax = bbox
    if not (x0 <= lonmin and east >= lonmax and y0 >= latmax and south <= latmin):
        fail(f"{label}: DEM tile does not cover the storage extent", EXIT_WORKER)


def curl_headers(url):
    run = run_tool(["curl", "-fsSI", "--proto", "=https", "--max-time", "60", url])
    if run.timed_out or run.returncode != 0:
        fail(f"curl header request failed for {url}", EXIT_WORKER)
    headers = {}
    for line in run.stdout.splitlines():
        if ":" in line:
            key, value = line.split(":", 1)
            headers[key.strip().lower()] = value.strip()
    return {"etag": headers.get("etag"), "lastModified": headers.get("last-modified"),
            "contentLength": headers.get("content-length")}


def download_dem(url, path, cache, inputs):
    """Resume only an owned partial file bound to a strong server validator."""
    owner_path = checked_path(cache, "owner.json")
    partial = checked_path(cache, "tile.part")
    completed = checked_path(cache, "complete.json")
    owner_inputs = {"schema": SCHEMA_VERSION, "url": url, "inputs": inputs}
    if os.path.lexists(cache):
        if not os.path.isdir(cache) or not os.path.isfile(owner_path):
            fail(f"unowned DEM download cache: {cache}")
        try:
            with open(owner_path, encoding="utf-8") as handle:
                owner = json.load(handle)
        except (OSError, ValueError):
            fail(f"unreadable DEM download ownership: {cache}")
        if not isinstance(owner, dict) or owner.get("request") != owner_inputs:
            fail(f"DEM download cache belongs to different inputs: {cache}")
        headers = owner.get("headers")
    else:
        headers = curl_headers(url)
        os.mkdir(cache)
        owner = {"request": owner_inputs, "headers": headers}
        atomic_write_excl(owner_path, (json.dumps(owner, sort_keys=True) + "\n").encode())
    if not isinstance(headers, dict):
        fail(f"invalid DEM download validators: {cache}")
    etag = headers.get("etag")
    length_text = headers.get("contentLength")
    if (not isinstance(etag, str) or not re.fullmatch(r'"[^"\r\n]+"', etag)
            or not isinstance(length_text, str) or not length_text.isdigit()
            or not 0 < int(length_text) <= PREPARE_BUDGET_BYTES):
        fail("DEM download needs a strong ETag and bounded Content-Length", EXIT_WORKER)
    length = int(length_text)
    if os.path.isfile(completed):
        try:
            with open(completed, encoding="utf-8") as handle:
                record = json.load(handle)
        except (OSError, ValueError):
            fail(f"unreadable DEM download completion: {cache}")
        if (not isinstance(record, dict) or not os.path.isfile(partial)
                or os.path.getsize(partial) != length or record.get("sha256") != sha256_file(partial)):
            fail(f"completed DEM download changed on disk: {cache}")
    else:
        if os.path.lexists(partial) and not os.path.isfile(partial):
            fail(f"invalid DEM partial file: {partial}")
        if os.path.isfile(partial) and os.path.getsize(partial) > length:
            fail(f"DEM partial file exceeds declared size: {partial}")
        current = curl_headers(url)
        if current.get("etag") != etag or current.get("contentLength") != length_text:
            fail("DEM server object changed; preserved partial download cannot be resumed", EXIT_WORKER)
        run = run_tool(["curl", "-fL", "--proto", "=https", "--proto-redir", "=https",
                        "-C", "-", "--header", f"If-Range: {etag}", "--max-filesize", length_text,
                        "--max-time", "600", "-o", partial, url])
        if run.timed_out or run.returncode != 0:
            fail(f"DEM download failed; partial retained: {run.stderr.strip()[-EXCERPT_BYTES:]}", EXIT_WORKER)
        current = curl_headers(url)
        if (not os.path.isfile(partial) or os.path.getsize(partial) != length
                or current.get("etag") != etag or current.get("contentLength") != length_text):
            fail("DEM download size or server validator changed", EXIT_WORKER)
        with open(partial, "rb") as handle:
            os.fsync(handle.fileno())
        atomic_write_excl(completed, (json.dumps({"sha256": sha256_file(partial)}, sort_keys=True) + "\n").encode())
    publish_no_overwrite(partial, path)
    return headers


def fetch_s2_window(href, window, epsg, path):
    xmin, ymin, xmax, ymax = window
    run = run_tool(["gdal_translate", "-q", "-of", "GTiff", "-co", "COMPRESS=NONE",
                    "-projwin_srs", f"EPSG:{epsg}",
                    "-projwin", f"{xmin:.3f}", f"{ymax:.3f}", f"{xmax:.3f}", f"{ymin:.3f}",
                    "/vsicurl/" + href, path])
    if run.timed_out or run.returncode != 0:
        fail(f"Sentinel-2 window read failed: {run.stderr.strip()[-EXCERPT_BYTES:]}", EXIT_WORKER)


def scale_window(src, dst, asset, reflectance_range):
    scale, offset = asset["scale"], asset["offset"]
    src_min = (reflectance_range[0] - offset) / scale
    src_max = (reflectance_range[1] - offset) / scale
    if not src_max > src_min:
        fail("reflectance range maps to an empty digital-number range", EXIT_WORKER)
    run = run_tool(["gdal_translate", "-q", "-of", "GTiff", "-ot", "Byte",
                    "-scale", f"{src_min:.12g}", f"{src_max:.12g}", "0", "255",
                    "-exponent", "1", "-a_nodata", "none",
                    "-co", "COMPRESS=NONE", src, dst])
    if run.timed_out or run.returncode != 0:
        fail(f"Sentinel-2 scaling failed: {run.stderr.strip()[-EXCERPT_BYTES:]}", EXIT_WORKER)


def write_rgb_vrt(path, sources, width, height, gsd, xmin, ymax, epsg):
    lines = [f'<VRTDataset rasterXSize="{width}" rasterYSize="{height}">',
             f"  <SRS>EPSG:{epsg}</SRS>",
             f"  <GeoTransform>{xmin!r}, {gsd!r}, 0.0, {ymax!r}, 0.0, {-gsd!r}</GeoTransform>"]
    for index, source in enumerate(sources, start=1):
        lines += [f'  <VRTRasterBand dataType="Byte" band="{index}">',
                  f"    <ColorInterp>{('Red', 'Green', 'Blue')[index - 1]}</ColorInterp>",
                  "    <SimpleSource>",
                   f'      <SourceFilename relativeToVRT="1">{escape(os.path.basename(source))}</SourceFilename>',
                  "      <SourceBand>1</SourceBand>",
                  f'      <SrcRect xOff="0" yOff="0" xSize="{width}" ySize="{height}"/>',
                  f'      <DstRect xOff="0" yOff="0" xSize="{width}" ySize="{height}"/>',
                  "    </SimpleSource>",
                  "  </VRTRasterBand>"]
    lines.append("</VRTDataset>")
    atomic_write_excl(path, ("\n".join(lines) + "\n").encode("utf-8"))


def run_gdal(argv, label):
    run = run_tool(argv)
    if run.timed_out or run.returncode != 0:
        fail(f"{label} failed: {run.stderr.strip()[-EXCERPT_BYTES:]}", EXIT_WORKER)


METERS_PER_GAME_UNIT = 0.3048
DEFAULT_MATERIAL = "grass"
VERTICAL_EXAGGERATION = 1.0
DELEGATE_TIMEOUT_SECONDS = 6 * 3600.0
DELEGATE_MANIFEST = "sedan-ign-proof.json"
DELEGATE_OUTPUTS = (DELEGATE_MANIFEST, os.path.join(PREPARED_DIR_NAME, "sedan-extent.kml"),
                    os.path.join(PREPARED_DIR_NAME, "sedan-dem-32631.tif"),
                    os.path.join(PREPARED_DIR_NAME, "sedan-ortho-32631.tif"))


def extent_kml_bytes(name, corners_lonlat):
    ring = list(corners_lonlat) + [corners_lonlat[0]]
    coordinates = " ".join(f"{lon:.9f},{lat:.9f},0" for lon, lat in ring)
    return ('<?xml version="1.0" encoding="UTF-8"?>\n'
            '<kml xmlns="http://www.opengis.net/kml/2.2">\n'
            f'  <Document id="{name}-extent">\n'
            f'    <Placemark id="{name}-extent">\n'
            f"      <name>{name} extent</name>\n"
            "      <Polygon><outerBoundaryIs><LinearRing><coordinates>\n"
            f"        {coordinates}\n"
            "      </coordinates></LinearRing></outerBoundaryIs></Polygon>\n"
            "    </Placemark>\n"
            "  </Document>\n"
            "</kml>\n").encode("utf-8")


def sources_text(profile, artifacts):
    elevation = profile["elevation"]
    imagery = profile["imagery"]
    attribution = profile["attribution"]
    stac = artifacts["stac"]
    selected = stac["selected"]
    year = selected["datetime"][:4]
    dem = artifacts["dem"]
    lines = [
        f"provider: {profile['id']} ({profile['label']})",
        f"coverage: {profile['coverage']}",
        f"surface: {elevation['surfaceType']}, datum {elevation['verticalDatum']}, "
        f"source CRS {elevation['sourceCrs']}; lower-resolution surface model, not bare earth, "
        "not IGN 1 m",
        f"licence (elevation): {profile['licence']['name']}; {profile['licence']['licencePdf']}",
        f"attribution (elevation, source): {attribution['elevation']['source']}",
        f"attribution (elevation, derived): {attribution['elevation']['derived']}",
        f"attribution (elevation, liability): {attribution['elevation']['liability']}",
        f"licence (imagery): {imagery['licence']['name']}; {imagery['licence']['legalNotice']}",
        f"attribution (imagery, source): {attribution['imagery']['source'].replace('[Year]', year)}",
        f"attribution (imagery, derived): {attribution['imagery']['derived'].replace('[Year]', year)}",
        f"licence obligations: {json.dumps(profile['licence'], sort_keys=True)}",
        f"imagery legal notice: {json.dumps(imagery['licence'], sort_keys=True)}",
        f"DEM tile: {dem['tile']} {dem['url']}",
        f"DEM sha256: {dem['sha256']} size {dem['sizeBytes']} bytes",
        f"STAC catalogue: {stac['catalogueUrl']}",
        f"STAC query: {json.dumps(stac['query'], sort_keys=True)}",
        f"STAC pages {stac['pages']}, items {stac['itemsSeen']}, suitable {stac['suitableCount']}, "
        f"response sha256 {stac['responseSha256']}",
        f"selected item: {selected['id']} cloud {selected['cloud']} mgrs {selected['mgrs']} "
        f"datetime {selected['datetime']}",
    ]
    for record in artifacts["s2"]:
        headers = record["headers"]
        lines.append(
            f"S2 {record['band']}: {record['href']} scale {record['scale']} offset {record['offset']} "
            f"nodata {record['nodata']} window {record['window']} etag {headers['etag']} "
            f"last-modified {headers['lastModified']} content-length {headers['contentLength']} "
            f"raw sha256 {record['rawSha256']}")
    lines += [
        f"warp DEM: {shlex.join(artifacts['warps']['dem'])}",
        f"warp RGB: {shlex.join(artifacts['warps']['rgb'])}",
        "RGB: Sentinel-2 L2A uint8 3-band, scaled from reflectance 0..0.2 to 0..255 after the asset "
        "scale and offset; stacked by a manually written VRT",
        "no control PASS: no independent imagery source, no self-RGB claim",
    ]
    return "\n".join(lines) + "\n"


def manifest_dict(profile, name, zone, hemisphere, epsg, dem_rel, rgb_rel, kml_rel):
    elevation = profile["elevation"]
    manifest_req = profile["manifestRequirements"]
    dem_gsd = elevation["outputGsdMeters"]
    rgb_gsd = profile["imagery"]["bandGsdMeters"]
    return {
        "sources": [
            {"id": f"{name}-extent", "kind": "kml", "path": kml_rel, "role": "extent",
             "label": "generated extent KML: exact projected playable AOI square corners "
                      "converted to WGS84",
             "attribution": "generated extent KML from the requested AOI geometry"},
            {"id": f"{name}-dem-{epsg}", "kind": "geotiff", "path": dem_rel, "role": "elevation",
             "verticalDatum": elevation["verticalDatum"],
             "label": f"{profile['label']} surface model (surfaceModel, not bare earth) "
                      f"EPSG:{epsg} Float32 {dem_gsd:g} m; verticalAccuracyMeters omitted "
                      "(no accuracy stated by the source profile)",
             "attribution": profile["attribution"]["elevation"]["derived"]},
            {"id": f"{name}-ortho-{epsg}", "kind": "geotiff", "path": rgb_rel, "role": "imagery",
             "label": f"Sentinel-2 L2A RGB uint8 EPSG:{epsg} {rgb_gsd:g} m; no independent "
                      "imagery control",
             "attribution": profile["attribution"]["imagery"]["derived"]},
        ],
        "targetCoordinateSystem": {"type": "utm", "zone": zone, "hemisphere": hemisphere},
        "metersPerGameUnit": METERS_PER_GAME_UNIT,
        "defaultMaterial": DEFAULT_MATERIAL,
        "extentFeatureId": f"{name}-extent",
        "verticalPolicy": {"originMeters": 0.0, "exaggeration": VERTICAL_EXAGGERATION},
        "rasterPolicy": {
            "noData": "nearest-valid",
            "noDataSearchRadiusMeters": NO_DATA_RADIUS_M,
            "edge": "fail",
            "imageryPixelsPerSample": profile["imagery"]["imageryPixelsPerSample"],
        },
        "tolerances": {"verticalMeters": manifest_req["tolerances"]["verticalMeters"]["schemaValue"]},
        "controlPoints": [],
    }


def check_space(path):
    stats = os.statvfs(path)
    free = stats.f_bavail * stats.f_frsize
    required = PREPARE_BUDGET_BYTES + RESERVE_BYTES
    if free < required:
        fail(f"insufficient free space in {path}: {free} bytes available, {required} required")


def acquire_lock(lock_path):
    try:
        descriptor = os.open(lock_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o644)
    except FileExistsError:
        try:
            with open(lock_path, encoding="utf-8") as handle:
                owner = int(handle.read().strip())
        except (OSError, ValueError):
            owner = None
        if owner is not None and pid_alive(owner):
            fail(f"lock {lock_path} is held by live pid {owner}", EXIT_STATE)
        fail(f"stale lock {lock_path} (pid {owner}); confirm no prepare is running, then remove it")
    with os.fdopen(descriptor, "w", encoding="utf-8") as handle:
        handle.write(f"{os.getpid()}\n")


def release_lock(lock_path):
    try:
        os.unlink(lock_path)
    except FileNotFoundError:
        pass


def make_stage(parent, name):
    stage = os.path.join(parent, f"stage-{name}-{os.getpid()}-{time.monotonic_ns()}")
    os.mkdir(stage)
    for sub in (PREPARED_DIR_NAME, "work", PREPARATION_DIR_NAME):
        os.mkdir(os.path.join(stage, sub))
    return stage


def verify_existing_receipt(workdir, receipt_path, inputs):
    checked_path(workdir, os.path.relpath(receipt_path, workdir))
    try:
        with open(receipt_path, encoding="utf-8") as handle:
            receipt = json.load(handle)
    except (OSError, ValueError) as error:
        fail(f"existing receipt unreadable: {receipt_path}: {error}")
    if not isinstance(receipt, dict) or receipt.get("schema") != SCHEMA_VERSION:
        fail(f"existing receipt has an unsupported schema: {receipt_path}")
    if receipt.get("inputs") != inputs:
        fail(f"existing receipt {receipt_path} was produced for different inputs; "
             "remove the prepared outputs to re-prepare")
    completed = checked_path(workdir, os.path.join(PREPARATION_DIR_NAME, COMPLETED_NAME))
    if not os.path.isfile(completed):
        fail(f"receipt present but COMPLETED marker missing: {completed}")
    outputs = receipt.get("outputs")
    required = set(output_paths(inputs["name"], inputs["epsg"]).values())
    required.update({"acquisition/profile.json", "acquisition/stac.json", "acquisition/dem-tile.tif",
                     "acquisition/dem-source-window.tif", "acquisition/rgb.vrt"})
    required.update(f"acquisition/s2-{band}-{kind}.tif" for band in BANDS for kind in ("raw", "u8"))
    if not isinstance(outputs, list) or not outputs:
        fail("existing receipt has no output inventory")
    seen = set()
    for output in outputs:
        if not isinstance(output, dict) or not isinstance(output.get("path"), str):
            fail("invalid receipt output record")
        rel = output["path"]
        if rel in seen:
            fail(f"duplicate receipt output: {rel}")
        seen.add(rel)
        path = checked_path(workdir, rel)
        if not os.path.isfile(path):
            fail(f"receipt output missing: {rel}")
        if (not is_int(output.get("sizeBytes")) or os.path.getsize(path) != output["sizeBytes"]
                or sha256_file(path) != output.get("sha256")):
            fail(f"receipt output changed on disk: {rel}")
    if not required <= seen:
        fail("existing receipt omits required outputs or provenance")
    ledger = checked_path(workdir, os.path.join(PREPARATION_DIR_NAME, LEDGER_NAME))
    try:
        with open(completed, encoding="utf-8") as handle:
            marker = json.load(handle)
    except (OSError, ValueError):
        fail("unreadable completion marker")
    if (not isinstance(marker, dict) or marker.get("receiptSha256") != sha256_file(receipt_path)
            or not os.path.isfile(ledger) or marker.get("ledgerSha256") != sha256_file(ledger)):
        fail("completion marker does not bind the receipt and ledger")
    return receipt


def checked_path(root, rel):
    if (not isinstance(rel, str) or not rel or os.path.isabs(rel)
            or any(part in ("", ".", "..") for part in rel.split("/")) or "\\" in rel):
        fail(f"unsafe relative output path: {rel!r}")
    path = root
    if os.path.islink(root):
        fail(f"symlink work directory: {root}")
    for part in rel.split("/"):
        path = os.path.join(path, part)
        if os.path.islink(path):
            fail(f"symlink in output path: {path}")
    return path


def output_paths(name, epsg):
    return {"manifest": f"{name}.json", "sources": SOURCES_NAME,
            "kml": f"prepared/{name}-extent.kml", "dem": f"prepared/{name}-dem-{epsg}.tif",
            "rgb": f"prepared/{name}-ortho-{epsg}.tif"}


def verify_window(info, window, gsd, epsg, data_type, count, label, nodata=None):
    expected_size = [round((window[2] - window[0]) / gsd), round((window[3] - window[1]) / gsd)]
    expected_gt = [window[0], gsd, 0, window[3], 0, -gsd]
    transform = info.get("geoTransform") or []
    wkt = info.get("coordinateSystem", {}).get("wkt", "")
    codes = re.findall(r'(?:ID|AUTHORITY)\s*\[\s*"EPSG"\s*,\s*"?(\d+)"?\s*\]', wkt)
    if (info.get("driverShortName") != "GTiff" or info.get("size") != expected_size
            or len(transform) != 6 or any(not is_finite_number(v) for v in transform)
            or any(abs(a - b) > 1e-6 for a, b in zip(transform, expected_gt))
            or not codes or codes[-1] != str(epsg)):
        fail(f"{label}: unexpected raster geometry or CRS", EXIT_WORKER)
    bands = info.get("bands") or []
    if len(bands) != count or any(b.get("type") != data_type or b.get("noDataValue") != nodata for b in bands):
        fail(f"{label}: unexpected band type/count/no-data", EXIT_WORKER)
    require_full_valid(info, label)


def build_copernicus_stage(stage, profile, provider_id, name, centre, size_m, utm, grid, inputs, rels):
    lat, lon = centre
    zone, _, hemisphere, epsg = utm
    elevation = profile["elevation"]
    imagery = profile["imagery"]
    download = elevation["download"]
    gsd_dem = elevation["outputGsdMeters"]
    gsd_rgb = imagery["bandGsdMeters"]
    work = os.path.join(stage, "work")
    ledger = Ledger(os.path.join(stage, PREPARATION_DIR_NAME, LEDGER_NAME))
    ledger.append("start", provider=provider_id, inputs=inputs)

    centre_e, centre_n = lonlat_to_utm([(lon, lat)], epsg)[0]
    geometry = utm_geometry(centre_e, centre_n, size_m, grid)
    playable = utm_to_lonlat(box_corners(geometry["playable"]), epsg)
    dem_window = outward_window(geometry["minimal"], gsd_dem)
    window = outward_window(geometry["minimal"], gsd_rgb)
    source_bounds = (min(dem_window[0], window[0]) - gsd_dem,
                     min(dem_window[1], window[1]) - gsd_dem,
                     max(dem_window[2], window[2]) + gsd_dem,
                     max(dem_window[3], window[3]) + gsd_dem)
    minimal = utm_to_lonlat(box_corners(source_bounds), epsg)
    bbox = (min(c[0] for c in minimal), min(c[1] for c in minimal),
            max(c[0] for c in minimal), max(c[1] for c in minimal))
    check_extent(bbox, zone, profile, provider_id)
    ledger.append("extent", bbox=list(bbox), utmCentre=[centre_e, centre_n])

    tile = dem_tile(download, bbox, provider_id)
    dem_tile_path = os.path.join(work, "dem-tile.tif")
    cache = checked_path(os.path.dirname(stage), "preparation/dem-download")
    dem_headers = download_dem(tile["url"], dem_tile_path, cache, inputs)
    verify_dem_tile(gdal_json(dem_tile_path), bbox, "DEM tile")
    dem_record = {"tile": tile["tile"], "url": tile["url"], "sha256": sha256_file(dem_tile_path),
                   "sizeBytes": os.path.getsize(dem_tile_path), "headers": dem_headers}
    ledger.append("dem-download", **dem_record)
    dem_check = os.path.join(work, "dem-source-window.tif")
    run_gdal(["gdal_translate", "-q", "-projwin_srs", "EPSG:4326", "-projwin",
              str(bbox[0]), str(bbox[3]), str(bbox[2]), str(bbox[1]), dem_tile_path, dem_check],
             "DEM source validity window")
    require_full_valid(gdal_json(dem_check, stats=True), "DEM source validity window")

    base, pages = stac_search_all(imagery, bbox)
    features = [item for page in pages for item in (page.get("features") or []) if isinstance(item, dict)]
    selected, suitable_count = select_item(features, imagery, bbox, provider_id)
    atomic_write_excl(os.path.join(work, "profile.json"), (json.dumps(profile, sort_keys=True) + "\n").encode())
    atomic_write_excl(os.path.join(work, "stac.json"),
                      (json.dumps({"query": base, "pages": pages, "selected": selected}, sort_keys=True) + "\n").encode())
    stac_record = {
        "catalogueUrl": imagery["catalogueUrl"],
        "query": base,
        "pages": len(pages),
        "itemsSeen": len(features),
        "suitableCount": suitable_count,
        "responseSha256": canonical_sha256(pages),
        "selected": {key: selected[key] for key in ("id", "cloud", "mgrs", "datetime")},
    }
    ledger.append("stac", pages=len(pages), itemsSeen=len(features), suitableCount=suitable_count,
                  responseSha256=stac_record["responseSha256"], selected=stac_record["selected"])

    width = round((window[2] - window[0]) / gsd_rgb)
    height = round((window[3] - window[1]) / gsd_rgb)
    s2_records = []
    scaled = []
    for band in BANDS:
        asset = selected["chosen"][band]
        raw = os.path.join(work, f"s2-{band}-raw.tif")
        fetch_s2_window(asset["href"], window, epsg, raw)
        info = gdal_json(raw, stats=True)
        verify_window(info, window, gsd_rgb, epsg, "UInt16", 1, f"Sentinel-2 {band} window", asset["nodata"])
        record = {"band": band, "href": asset["href"], "scale": asset["scale"], "offset": asset["offset"],
                  "nodata": asset["nodata"], "window": list(window), "headers": curl_headers(asset["href"]),
                  "rawSha256": sha256_file(raw)}
        s2_records.append(record)
        out = os.path.join(work, f"s2-{band}-u8.tif")
        scale_window(raw, out, asset, imagery["reflectanceRange"])
        scaled.append(out)
        ledger.append("s2-window", band=band, href=record["href"], rawSha256=record["rawSha256"],
                      headers=record["headers"])

    vrt = os.path.join(work, "rgb.vrt")
    write_rgb_vrt(vrt, scaled, width, height, gsd_rgb, window[0], window[3], epsg)
    dem_argv = ["gdalwarp", "-q", "-t_srs", f"EPSG:{epsg}", "-te", *[str(v) for v in dem_window],
                "-tr", f"{gsd_dem:g}", f"{gsd_dem:g}", "-r", "bilinear", "-ot", "Float32",
                "-dstnodata", str(DEM_NODATA), "-co", "COMPRESS=NONE",
                dem_tile_path, os.path.join(stage, rels["dem"])]
    rgb_argv = ["gdalwarp", "-q", "-t_srs", f"EPSG:{epsg}", "-te", *[str(v) for v in window],
                "-tr", f"{gsd_rgb:g}", f"{gsd_rgb:g}", "-r", "average", "-ot", "Byte",
                "-dstnodata", "None",
                "-co", "COMPRESS=NONE", "-co", "PHOTOMETRIC=RGB", vrt, os.path.join(stage, rels["rgb"])]
    run_gdal(dem_argv, "DEM warp")
    run_gdal(rgb_argv, "RGB warp")
    verify_window(gdal_json(os.path.join(stage, rels["dem"]), stats=True), dem_window,
                  gsd_dem, epsg, "Float32", 1, "DEM output", DEM_NODATA)
    rgb_info = gdal_json(os.path.join(stage, rels["rgb"]), stats=True)
    verify_window(rgb_info, window, gsd_rgb, epsg, "Byte", 3, "RGB output")
    if [band.get("colorInterpretation") for band in rgb_info["bands"]] != ["Red", "Green", "Blue"]:
        fail("RGB output: expected Red, Green, Blue colour interpretations", EXIT_WORKER)
    ledger.append("warp", dem=dem_argv, rgb=rgb_argv)

    atomic_write_excl(os.path.join(stage, rels["kml"]), extent_kml_bytes(name, playable))
    manifest = manifest_dict(profile, name, zone, hemisphere, epsg, rels["dem"], rels["rgb"], rels["kml"])
    manifest["sources"][2]["attribution"] = manifest["sources"][2]["attribution"].replace(
        "[Year]", selected["datetime"][:4])
    atomic_write_excl(os.path.join(stage, rels["manifest"]),
                      (json.dumps(manifest, indent=2, sort_keys=True) + "\n").encode("utf-8"))
    artifacts = {"stac": stac_record, "dem": dem_record, "s2": s2_records,
                 "warps": {"dem": dem_argv, "rgb": rgb_argv}}
    atomic_write_excl(os.path.join(stage, rels["sources"]), sources_text(profile, artifacts).encode("utf-8"))

    outputs = []
    os.rename(work, os.path.join(stage, "acquisition"))
    retained = ["acquisition/" + filename for filename in sorted(os.listdir(os.path.join(stage, "acquisition")))]
    for rel in [*rels.values(), *retained]:
        path = os.path.join(stage, rel)
        outputs.append({"path": rel, "sha256": sha256_file(path), "sizeBytes": os.path.getsize(path)})
    receipt = {"schema": SCHEMA_VERSION, "provider": provider_id, "name": name, "createdAt": now_iso(),
               "inputs": inputs, "stacItem": stac_record["selected"]["id"], "demTile": tile["tile"],
               "outputs": outputs}
    atomic_write_excl(os.path.join(stage, PREPARATION_DIR_NAME, RECEIPT_NAME),
                      (json.dumps(receipt, indent=2, sort_keys=True) + "\n").encode("utf-8"))
    ledger.append("receipt", outputs=len(outputs))
    ledger.append("complete")
    atomic_write_excl(os.path.join(stage, PREPARATION_DIR_NAME, COMPLETED_NAME),
                      (json.dumps({"time": now_iso(), "receiptSha256": sha256_file(
                          os.path.join(stage, PREPARATION_DIR_NAME, RECEIPT_NAME)),
                          "ledgerSha256": sha256_file(ledger.path)}, sort_keys=True) + "\n").encode())


def prepare_copernicus(profile, provider_id, centre, size_m, name, workdir, utm, grid):
    zone, _, hemisphere, epsg = utm
    download = profile["elevation"]["download"]
    imagery = profile["imagery"]
    if download["kind"] != "tile-grid" or imagery is None or imagery["kind"] != "stac":
        fail(f"provider {provider_id!r}: --prepare needs a tile-grid elevation and STAC imagery profile")
    inputs = {"provider": provider_id, "centre": list(centre), "sizeMeters": size_m, "name": name,
              "zone": zone, "hemisphere": hemisphere, "epsg": epsg, "grid": grid,
              "profileSha256": canonical_sha256(profile), "driverSha256": sha256_file(__file__)}
    ancestor = workdir
    while not os.path.exists(ancestor):
        ancestor = os.path.dirname(ancestor)
    check_space(ancestor)
    if os.path.realpath(workdir) != os.path.abspath(workdir):
        fail(f"symlink in work directory: {workdir}")
    prep_dir = os.path.join(workdir, PREPARATION_DIR_NAME)
    receipt_path = os.path.join(prep_dir, RECEIPT_NAME)
    checked_path(workdir, PREPARATION_DIR_NAME)
    os.makedirs(prep_dir, exist_ok=True)
    rels = output_paths(name, epsg)
    publish_rels = [rels["kml"], rels["dem"], rels["rgb"], rels["manifest"], rels["sources"],
                    os.path.join(PREPARATION_DIR_NAME, RECEIPT_NAME),
                    os.path.join(PREPARATION_DIR_NAME, LEDGER_NAME),
                    os.path.join(PREPARATION_DIR_NAME, COMPLETED_NAME)]
    lock_path = os.path.join(prep_dir, LOCK_NAME)
    acquire_lock(lock_path)
    stage = None
    published = []
    moved_acquisition = False
    try:
        if os.path.lexists(receipt_path):
            verify_existing_receipt(workdir, receipt_path, inputs)
            print(f"SKIPPED: {name} is already prepared and verified: {receipt_path}")
            return EXIT_OK
        for rel in [*publish_rels, "acquisition"]:
            path = checked_path(workdir, rel)
            if os.path.lexists(path):
                fail(f"refusing to overwrite existing file: {path}")
        os.makedirs(checked_path(workdir, PREPARED_DIR_NAME), exist_ok=True)
        stage = make_stage(workdir, name)
        atomic_write_excl(os.path.join(stage, PREPARATION_DIR_NAME, "STARTED"), (now_iso() + "\n").encode())
        build_copernicus_stage(stage, profile, provider_id, name, centre, size_m, utm, grid, inputs, rels)
        os.rename(os.path.join(stage, "acquisition"), os.path.join(workdir, "acquisition"))
        moved_acquisition = True
        for rel in publish_rels:
            publish_no_overwrite(os.path.join(stage, rel), os.path.join(workdir, rel))
            published.append(rel)
            fsync_dir(os.path.dirname(os.path.join(workdir, rel)))
        fsync_dir(workdir)
    except (Failure, OSError):
        if stage is not None:
            for rel in reversed(published):
                os.unlink(os.path.join(workdir, rel))
            if moved_acquisition:
                os.rename(os.path.join(workdir, "acquisition"), os.path.join(stage, "acquisition"))
            print(f"failed stage preserved: {stage}", file=sys.stderr)
        raise
    finally:
        release_lock(lock_path)
    try:
        shutil.rmtree(stage)
    except OSError as error:
        print(f"prepared outputs verified; stage cleanup failed: {stage}: {error}", file=sys.stderr)
    print(f"prepared {name}: {os.path.join(workdir, rels['manifest'])}")
    return EXIT_OK


def run_ign_delegate(profile, provider_id, workdir):
    download = profile["elevation"]["download"]
    argv = [os.path.join(SCRIPT_DIR, download["delegateTarget"])]
    argv += [part.replace("{root}", workdir) for part in download["delegateArgs"]["prepare"]]
    run = run_tool(argv, timeout_seconds=DELEGATE_TIMEOUT_SECONDS)
    if run.timed_out or run.returncode != 0:
        fail(f"provider {provider_id!r}: delegate {download['delegateTarget']} failed "
             f"(rc {run.returncode}): {run.stderr.strip()[-EXCERPT_BYTES:]}", EXIT_WORKER)
    missing = [rel for rel in DELEGATE_OUTPUTS if not os.path.isfile(os.path.join(workdir, rel))]
    if missing:
        fail(f"provider {provider_id!r}: delegate finished but outputs are missing: {', '.join(missing)}",
             EXIT_STATE)
    print(f"prepared {provider_id} via {download['delegateTarget']}: "
          f"{os.path.join(workdir, DELEGATE_MANIFEST)}")
    print(f"next: geoimport import --manifest {os.path.join(workdir, DELEGATE_MANIFEST)} --out <payload-dir>")
    return EXIT_OK


def build_parser():
    parser = argparse.ArgumentParser(
        description="Prepare a bounded area from a named provider profile. "
                    "Default mode is a read-only preflight; --prepare writes outputs.")
    parser.add_argument("--provider", required=True, help="profile id in scripts/geo/providers/")
    parser.add_argument("--center", required=True, metavar="LAT,LON", help="centre in WGS84 degrees")
    parser.add_argument("--size", required=True, metavar="METERS", help="playable AOI size in metres")
    parser.add_argument("--name", required=True, help="area name")
    parser.add_argument("--utm-zone", type=int, metavar="1-60", help="override the computed UTM zone")
    parser.add_argument("--prepare", action="store_true", help="write outputs")
    parser.add_argument("workdir", nargs="?", help=f"work dir (default: ${WORKDIR_ENV})")
    return parser


def run(args):
    provider_id = args.provider
    profile = load_profile(provider_id)
    lat, lon = parse_center(args.center, provider_id)
    size_m = parse_size(args.size, provider_id)
    name = validate_name(args.name, provider_id)
    workdir = resolve_workdir(args.workdir)
    if "provenCentre" in profile:
        check_proven_only(profile, lat, lon, size_m, name, provider_id)
    grid = chunk_grid(size_m)
    zone, computed, hemisphere, epsg = resolve_utm(lat, lon, args.utm_zone, provider_id)
    if args.utm_zone is not None and args.utm_zone != computed:
        fail(f"provider {provider_id!r}: requested UTM zone {args.utm_zone} differs from the "
             f"extent zone {computed}; cross-zone overrides are not supported")
    if "provenCentre" not in profile:
        approx = approx_wgs84_box(lat, lon, grid["grid_m"], size_m)
        check_extent(approx, zone, profile, provider_id)
    utm = (zone, computed, hemisphere, epsg)
    if args.prepare:
        if "provenCentre" in profile:
            return run_ign_delegate(profile, provider_id, workdir)
        return prepare_copernicus(profile, provider_id, (lat, lon), size_m, name, workdir,
                                  utm, grid)
    print_plan(profile, provider_id, args, lat, lon, size_m, name, workdir, utm, grid)
    return EXIT_OK


def main(argv=None):
    args = build_parser().parse_args(argv)
    try:
        return run(args)
    except Failure as error:
        print(f"error: {error}", file=sys.stderr)
        return error.code
    except OSError as error:
        print(f"error: {error}", file=sys.stderr)
        return EXIT_STATE


if __name__ == "__main__":
    sys.exit(main())
