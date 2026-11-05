"""Bounded WFS acquisition of BD TOPO footprints and operator manifest composition.

Owner: Track R R2-03 (building footprints as terrain masks).
Conventions: stdlib only; fail() style named errors; no writes outside footprint_root;
never mutate delegate files, manifests, sources, markers, or any existing root.
"""

from __future__ import annotations

import importlib.util
import json
import math
import os
import shutil
import sys
import time
import urllib.parse
from xml.sax.saxutils import escape

SYNTHETIC_SEAMS_MODULE = "prepare_area_seams"
USER_AGENT = "Panopticon-geo-footprints/1.0 (IGN Licence Ouverte 2.0; contact via repository)"
COORDINATE_BATCH_POINTS = 50


def get_seams():
    """Load prepare_area sibling seams under the stable synthetic module name."""
    if SYNTHETIC_SEAMS_MODULE in sys.modules:
        return sys.modules[SYNTHETIC_SEAMS_MODULE]
    for candidate in ("prepare_area", "prepare_area_under_test"):
        if candidate in sys.modules:
            mod = sys.modules[candidate]
            sys.modules[SYNTHETIC_SEAMS_MODULE] = mod
            return mod
    script_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "prepare_area.py")
    if not os.path.isfile(script_path):
        raise RuntimeError(f"prepare_area.py not found at {script_path}")
    spec = importlib.util.spec_from_file_location(SYNTHETIC_SEAMS_MODULE, script_path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"failed to load spec for {script_path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[SYNTHETIC_SEAMS_MODULE] = module
    spec.loader.exec_module(module)
    return module


def validate_root_paths(seams, workdir: str, footprint_root: str) -> tuple[str, str]:
    """Defensively validate and resolve workdir and footprint_root paths."""
    if "\0" in workdir:
        seams.fail("work dir contains a null byte")
    if "\0" in footprint_root:
        seams.fail("footprint root contains a null byte")

    norm_work = os.path.abspath(workdir)
    norm_foot = os.path.abspath(footprint_root)

    if os.path.islink(norm_foot) or os.path.islink(norm_foot.rstrip("/\\")):
        seams.fail(f"symlink footprint root: {norm_foot}")

    real_work = os.path.realpath(norm_work)
    real_foot = os.path.realpath(norm_foot)

    if norm_foot == norm_work or real_foot == real_work:
        seams.fail(f"footprint root cannot equal workdir: {norm_foot}")

    for r, w in ((norm_foot, norm_work), (real_foot, real_work)):
        try:
            common = os.path.commonpath([r, w])
        except ValueError:
            common = None
        if common == r:
            seams.fail(f"footprint root cannot contain workdir: {norm_foot}")
        if common == w:
            seams.fail(f"footprint root cannot be inside workdir: {norm_foot}")

    return norm_work, norm_foot


def check_existing_root(seams, footprint_root: str, inputs: dict, required_outputs: set[str]) -> bool:
    """Refuse non-empty existing roots unless verified receipt reuse succeeds."""
    if not os.path.lexists(footprint_root):
        return False
    if not os.path.isdir(footprint_root):
        seams.fail(f"footprint root exists and is not a directory: {footprint_root}")

    prep_dir = os.path.join(footprint_root, seams.PREPARATION_DIR_NAME)
    receipt_path = os.path.join(prep_dir, seams.RECEIPT_NAME)

    if os.path.lexists(receipt_path):
        try:
            with open(receipt_path, encoding="utf-8") as handle:
                rec_data = json.load(handle)
                if isinstance(rec_data, dict):
                    for out in rec_data.get("outputs", []):
                        if isinstance(out, dict) and isinstance(out.get("path"), str) and out["path"].startswith("provenance/pages/"):
                            required_outputs.add(out["path"])
        except (OSError, ValueError):
            pass
        verify_existing_footprint_receipt(seams, footprint_root, receipt_path, inputs, required_outputs)
        return True

    # No receipt: refuse if any file exists (other than perhaps an empty prep_dir / lock)
    top_entries = [e for e in os.listdir(footprint_root) if e != seams.PREPARATION_DIR_NAME]
    if top_entries:
        seams.fail(f"refusing to use existing non-empty footprint root: {footprint_root}")
    if os.path.isdir(prep_dir):
        prep_entries = [e for e in os.listdir(prep_dir) if e != seams.LOCK_NAME]
        if prep_entries:
            seams.fail(f"refusing to use existing non-empty footprint root: {footprint_root}")

    return False


def verify_existing_footprint_receipt(seams, root: str, receipt_path: str, inputs: dict,
                                      required_outputs: set[str]) -> dict:
    """Verify an existing footprint receipt, inventory, and completion marker."""
    seams.checked_path(root, os.path.relpath(receipt_path, root))
    try:
        with open(receipt_path, encoding="utf-8") as handle:
            receipt = json.load(handle)
    except (OSError, ValueError) as error:
        seams.fail(f"existing receipt unreadable: {receipt_path}: {error}")

    if not isinstance(receipt, dict) or receipt.get("schema") != seams.SCHEMA_VERSION:
        seams.fail(f"existing receipt has an unsupported schema: {receipt_path}")
    if receipt.get("inputs") != inputs:
        seams.fail(f"existing receipt {receipt_path} was produced for different inputs; "
                   "remove the prepared outputs to re-prepare")

    completed = seams.checked_path(root, os.path.join(seams.PREPARATION_DIR_NAME, seams.COMPLETED_NAME))
    if not os.path.isfile(completed):
        seams.fail(f"receipt present but COMPLETED marker missing: {completed}")

    outputs = receipt.get("outputs")
    if not isinstance(outputs, list) or not outputs:
        seams.fail("existing receipt has no output inventory")

    seen = set()
    for output in outputs:
        if not isinstance(output, dict) or not isinstance(output.get("path"), str):
            seams.fail("invalid receipt output record")
        rel = output["path"]
        if rel in seen:
            seams.fail(f"duplicate receipt output: {rel}")
        seen.add(rel)
        path = seams.checked_path(root, rel)
        if not os.path.isfile(path):
            seams.fail(f"receipt output missing: {rel}")
        size = output.get("sizeBytes")
        if not seams.is_int(size) or os.path.getsize(path) != size or seams.sha256_file(path) != output.get("sha256"):
            seams.fail(f"receipt output changed on disk: {rel}")

    if not required_outputs <= seen:
        missing = sorted(required_outputs - seen)
        seams.fail(f"existing receipt omits required outputs: {', '.join(missing)}")

    ledger = seams.checked_path(root, os.path.join(seams.PREPARATION_DIR_NAME, seams.LEDGER_NAME))
    try:
        with open(completed, encoding="utf-8") as handle:
            marker = json.load(handle)
    except (OSError, ValueError):
        seams.fail("unreadable completion marker")

    if (not isinstance(marker, dict)
            or marker.get("receiptSha256") != seams.sha256_file(receipt_path)
            or not os.path.isfile(ledger)
            or marker.get("ledgerSha256") != seams.sha256_file(ledger)):
        seams.fail("completion marker does not bind the receipt and ledger")

    return receipt


def parse_delegate_receipt_outputs(outputs_data) -> dict[str, dict]:
    """Index delegate outputs by relative path, supporting both dict and list receipt schemas."""
    result = {}
    if isinstance(outputs_data, dict):
        for label, record in outputs_data.items():
            if isinstance(record, dict) and "path" in record:
                raw_path = record["path"]
                # Normalise relative path
                sha = record.get("sha256")
                size = record.get("size") if record.get("size") is not None else record.get("sizeBytes")
                result[raw_path] = {"sha256": sha, "sizeBytes": size}
    elif isinstance(outputs_data, list):
        for record in outputs_data:
            if isinstance(record, dict) and "path" in record:
                raw_path = record["path"]
                sha = record.get("sha256")
                size = record.get("sizeBytes") if record.get("sizeBytes") is not None else record.get("size")
                result[raw_path] = {"sha256": sha, "sizeBytes": size}
    return result


def find_delegate_manifest(seams, workdir: str, profile: dict, name: str) -> tuple[str, dict]:
    """Find and parse the delegate manifest JSON from workdir."""
    candidates = []
    download = profile.get("elevation", {}).get("download", {})
    if download.get("delegateManifest"):
        candidates.append(download["delegateManifest"])
    candidates += [f"{name}-ign-proof.json", "sedan-ign-proof.json", f"{name}.json"]

    manifest_path = None
    for cand in candidates:
        candidate_path = os.path.join(workdir, cand)
        if os.path.isfile(candidate_path):
            manifest_path = candidate_path
            break

    if manifest_path is None:
        # Check all .json files in workdir excluding receipt/lineage
        for entry in os.listdir(workdir):
            if entry.endswith(".json") and entry not in ("receipt.json", "stac.json"):
                manifest_path = os.path.join(workdir, entry)
                break

    if manifest_path is None or not os.path.isfile(manifest_path):
        seams.fail(f"delegate manifest missing in {workdir}")

    try:
        with open(manifest_path, encoding="utf-8") as handle:
            manifest = json.load(handle)
    except (OSError, ValueError) as error:
        seams.fail(f"delegate manifest unreadable {manifest_path}: {error}")

    if not isinstance(manifest, dict):
        seams.fail(f"delegate manifest {manifest_path} is not an object")

    required_keys = {"sources", "targetCoordinateSystem", "metersPerGameUnit", "defaultMaterial",
                     "verticalPolicy", "rasterPolicy", "tolerances"}
    missing = required_keys - set(manifest)
    if missing:
        seams.fail(f"delegate manifest {manifest_path} missing required keys: {sorted(missing)}")

    return manifest_path, manifest


def stage_delegate_inputs(seams, workdir: str, stage_dir: str, delegate_manifest: dict,
                          delegate_receipt_outputs: dict[str, dict], ledger) -> list[dict]:
    """Materialize verified byte-identical copies of the three delegate inputs into stage/prepared."""
    sources = delegate_manifest.get("sources", [])
    if not isinstance(sources, list):
        seams.fail("delegate manifest sources is not a list")

    by_role = {}
    for src in sources:
        if isinstance(src, dict) and "role" in src:
            by_role[src["role"]] = src

    for required_role in ("extent", "elevation", "imagery"):
        if required_role not in by_role:
            seams.fail(f"delegate manifest missing {required_role} source")

    staged_copies = []
    prepared_stage = os.path.join(stage_dir, seams.PREPARED_DIR_NAME)
    os.makedirs(prepared_stage, exist_ok=True)

    for role in ("extent", "elevation", "imagery"):
        src = by_role[role]
        rel_path = src.get("path")
        if not isinstance(rel_path, str) or not rel_path or os.path.isabs(rel_path) or ".." in rel_path.split("/"):
            seams.fail(f"delegate source {src.get('id')} has invalid path: {rel_path!r}")

        src_full = os.path.join(workdir, rel_path)
        if not os.path.isfile(src_full) or os.path.islink(src_full):
            seams.fail(f"delegate source {rel_path} missing or is a symlink in {workdir}")

        # Look up expected hash in delegate receipt
        expected = None
        for cand_key in (rel_path, os.path.normpath(rel_path), os.path.join(workdir, rel_path)):
            if cand_key in delegate_receipt_outputs:
                expected = delegate_receipt_outputs[cand_key]
                break

        if expected is None:
            # Fall back to match by filename if path prefix differed
            base = os.path.basename(rel_path)
            for k, v in delegate_receipt_outputs.items():
                if os.path.basename(k) == base:
                    expected = v
                    break

        if expected is None or not expected.get("sha256"):
            seams.fail(f"delegate source {rel_path} not found in delegate receipt outputs")

        expected_sha = expected["sha256"]
        expected_size = expected.get("sizeBytes")

        actual_sha_before = seams.sha256_file(src_full)
        actual_size_before = os.path.getsize(src_full)

        if actual_sha_before != expected_sha:
            seams.fail(f"delegate source {rel_path} SHA-256 {actual_sha_before} differs from receipt {expected_sha}")
        if expected_size is not None and actual_size_before != expected_size:
            seams.fail(f"delegate source {rel_path} size {actual_size_before} differs from receipt {expected_size}")

        dst_rel = os.path.join(seams.PREPARED_DIR_NAME, os.path.basename(rel_path))
        dst_full = os.path.join(stage_dir, dst_rel)
        os.makedirs(os.path.dirname(dst_full), exist_ok=True)

        # Staging: hardlink if same device, else copy
        method = "copy"
        try:
            if os.stat(src_full).st_dev == os.stat(os.path.dirname(dst_full)).st_dev:
                try:
                    os.link(src_full, dst_full)
                    method = "hardlink"
                except OSError:
                    shutil.copy2(src_full, dst_full)
                    method = "copy"
            else:
                shutil.copy2(src_full, dst_full)
                method = "copy"
        except OSError as error:
            seams.fail(f"failed to stage delegate input {rel_path}: {error}")

        # Verify after materialization
        actual_sha_after = seams.sha256_file(dst_full)
        actual_size_after = os.path.getsize(dst_full)
        if actual_sha_after != expected_sha or actual_size_after != actual_size_before:
            seams.fail(f"staged delegate copy {dst_rel} corrupted during materialization")

        staged_record = {
            "sourceId": src["id"],
            "role": role,
            "originalPath": rel_path,
            "stagedPath": dst_rel,
            "method": method,
            "sha256": actual_sha_after,
            "sizeBytes": actual_size_after,
        }
        staged_copies.append(staged_record)
        ledger.append("delegate_file_staged", **staged_record)

    return staged_copies


def shoelace_area(points: list[tuple[float, float]]) -> float:
    """Compute polygon area in square units using the shoelace formula."""
    n = len(points)
    if n < 3:
        return 0.0
    total = 0.0
    for i in range(n - 1):
        total += points[i][0] * points[i + 1][1] - points[i + 1][0] * points[i][1]
    return 0.5 * abs(total)


def project_points_batch(seams, points: list[tuple[float, float]], epsg: int) -> list[tuple[float, float]]:
    """Convert lon/lat points to UTM coordinates in bounded batches to respect tool stdin caps."""
    converted = []
    for i in range(0, len(points), COORDINATE_BATCH_POINTS):
        batch = points[i:i + COORDINATE_BATCH_POINTS]
        # lonlat_to_utm expects (lon, lat) tuples
        res = seams.lonlat_to_utm(batch, epsg)
        converted.extend(res)
    return converted


def validate_multipolygon_geometry(seams, geom: dict, cleabs: str, lon_range: tuple[float, float],
                                   lat_range: tuple[float, float]):
    """Strictly validate geometry: MultiPolygon only, closed 4+ position rings, finite coords."""
    if not isinstance(geom, dict):
        seams.fail(f"feature {cleabs}: geometry is not an object")
    geom_type = geom.get("type")
    if geom_type != "MultiPolygon":
        seams.fail(f"feature {cleabs}: unsupported geometry type {geom_type!r}; expected MultiPolygon")

    coords = geom.get("coordinates")
    if not isinstance(coords, list) or not coords:
        seams.fail(f"feature {cleabs}: empty coordinates in MultiPolygon")

    for p_idx, polygon in enumerate(coords):
        if not isinstance(polygon, list) or not polygon:
            seams.fail(f"feature {cleabs}: empty polygon {p_idx} in MultiPolygon")
        for r_idx, ring in enumerate(polygon):
            if not isinstance(ring, list) or len(ring) < 4:
                length = len(ring) if isinstance(ring, list) else 0
                seams.fail(f"feature {cleabs}: ring {r_idx} in polygon {p_idx} must have >= 4 positions, got {length}")

            first, last = ring[0], ring[-1]
            if not (isinstance(first, list) and isinstance(last, list) and len(first) >= 2 and len(last) >= 2):
                seams.fail(f"feature {cleabs}: invalid first/last positions in ring")
            if abs(first[0] - last[0]) > 1e-9 or abs(first[1] - last[1]) > 1e-9:
                seams.fail(f"feature {cleabs}: ring {r_idx} in polygon {p_idx} is not closed")

            for pos in ring:
                if not isinstance(pos, list) or len(pos) < 2:
                    seams.fail(f"feature {cleabs}: position in ring is not a 2D/3D coordinate: {pos!r}")
                x, y = pos[0], pos[1]
                if not (seams.is_finite_number(x) and seams.is_finite_number(y)):
                    seams.fail(f"feature {cleabs}: non-finite coordinate in ring: {pos}")

                # Axis order verification check on coordinates:
                # GeoJSON standard RFC 7946: x is longitude [-180, 180], y is latitude [-90, 90]
                if abs(x) > 180.0 or abs(y) > 90.0:
                    seams.fail(f"feature {cleabs}: coordinate out of WGS84 range: [{x}, {y}]")

                # Detect inverted axis order (where latitude is first and longitude second)
                # If x is near latitude bounds and y is near longitude bounds while they don't overlap
                lonmin, lonmax = lon_range
                latmin, latmax = lat_range
                slack = 0.5
                if (latmin - slack <= x <= latmax + slack and lonmin - slack <= y <= lonmax + slack
                        and not (lonmin - slack <= x <= lonmax + slack and latmin - slack <= y <= latmax + slack)):
                    seams.fail("WFS returned coordinates with inverted axis order (expected [lon, lat])",
                               seams.EXIT_WORKER)


def compute_feature_area(seams, coordinates: list, epsg: int) -> float:
    """Project MultiPolygon rings to target UTM zone and compute net area in m^2 (shell - holes)."""
    total_area = 0.0
    for polygon in coordinates:
        # Flatten all ring points for batch projection
        ring_lengths = [len(ring) for ring in polygon]
        all_pts = [(pos[0], pos[1]) for ring in polygon for pos in ring]
        projected = project_points_batch(seams, all_pts, epsg)

        idx = 0
        shell_pts = projected[idx:idx + ring_lengths[0]]
        idx += ring_lengths[0]
        poly_area = shoelace_area(shell_pts)

        for length in ring_lengths[1:]:
            inner_pts = projected[idx:idx + length]
            idx += length
            poly_area -= shoelace_area(inner_pts)

        total_area += max(0.0, poly_area)
    return total_area


def compute_bbox_bounds(seams, centre: tuple[float, float], size_m: float, grid: dict,
                        utm: tuple[int, int, str, int]) -> tuple[float, float, float, float]:
    """Compute outward-rounded WGS84 bounding box covering full storage plus apron."""
    zone, computed, hemisphere, epsg = utm
    lat, lon = centre
    centre_e, centre_n = seams.lonlat_to_utm([(lon, lat)], epsg)[0]

    origin_e = centre_e - size_m / 2
    origin_n = centre_n - size_m / 2
    grid_m = grid["grid_m"]
    margin = seams.MARGIN_M

    xmin = origin_e - margin
    ymin = origin_n - margin
    xmax = origin_e + grid_m + margin
    ymax = origin_n + grid_m + margin

    corners_utm = [(xmin, ymin), (xmax, ymin), (xmax, ymax), (xmin, ymax)]
    corners_wgs = seams.utm_to_lonlat(corners_utm, epsg)

    lonmin = min(p[0] for p in corners_wgs)
    latmin = min(p[1] for p in corners_wgs)
    lonmax = max(p[0] for p in corners_wgs)
    latmax = max(p[1] for p in corners_wgs)

    # Round outward to 4 decimal places (~10 m resolution)
    return (
        math.floor(lonmin * 10000.0) / 10000.0,
        math.floor(latmin * 10000.0) / 10000.0,
        math.ceil(lonmax * 10000.0) / 10000.0,
        math.ceil(latmax * 10000.0) / 10000.0,
    )


def query_wfs_pages(seams, profile: dict, bbox_bounds: tuple[float, float, float, float],
                    ledger) -> tuple[list[dict], list[dict], list[dict], dict]:
    """Execute bounded paged WFS acquisition with strict page, sort, and cap enforcement."""
    footprints_cfg = profile.get("footprints")
    if not isinstance(footprints_cfg, dict):
        seams.fail("profile missing footprints configuration")

    wfs_url = footprints_cfg.get("wfsUrl")
    if not isinstance(wfs_url, str) or not wfs_url.startswith("https://"):
        seams.fail(f"invalid footprints wfsUrl: {wfs_url!r}")

    type_names = footprints_cfg.get("typeNames", "BDTOPO_V3:batiment")
    output_format = footprints_cfg.get("outputFormat", "application/json")
    stable_id_field = footprints_cfg.get("stableIdField", "cleabs")

    lonmin, latmin, lonmax, latmax = bbox_bounds
    # Strict OGC WFS 2.0 specifies lat,lon for EPSG:4326, but data.geopf.fr interprets
    # EPSG:4326 bboxes in lon,lat order, verified live 2026-11-05 (lon,lat 2425 matched;
    # lat,lon 0 matched).
    bbox_param = f"{lonmin:.6f},{latmin:.6f},{lonmax:.6f},{latmax:.6f},EPSG:4326"

    start_time = time.monotonic()
    start_index = 0
    page_size = 1000
    seen_ids = set()
    pages = []
    request_log = []
    all_features = []
    total_bytes = 0

    initial_timestamp = None
    initial_number_matched = None

    while True:
        elapsed = time.monotonic() - start_time
        if elapsed >= seams.FOOTPRINT_TIMEOUT_SECONDS:
            seams.fail(f"footprints stage exceeded timeout of {seams.FOOTPRINT_TIMEOUT_SECONDS}s", seams.EXIT_WORKER)

        if len(pages) >= seams.FOOTPRINT_PAGE_CAP:
            seams.fail(f"footprints stage exceeded page cap of {seams.FOOTPRINT_PAGE_CAP} pages", seams.EXIT_WORKER)

        params = [
            ("service", "WFS"),
            ("version", "2.0.0"),
            ("request", "GetFeature"),
            ("typeNames", type_names),
            ("outputFormat", output_format),
            ("srsName", "EPSG:4326"),
            ("bbox", bbox_param),
            ("STARTINDEX", str(start_index)),
            ("COUNT", str(page_size)),
            ("SORTBY", stable_id_field),
        ]
        query_str = urllib.parse.urlencode(params)
        full_url = f"{wfs_url}?{query_str}"

        timeout_remaining = max(1.0, seams.FOOTPRINT_TIMEOUT_SECONDS - (time.monotonic() - start_time))
        argv = [
            "curl", "-fsS", "--proto", "=https",
            "--max-time", str(int(math.ceil(timeout_remaining))),
            "-H", f"User-Agent: {USER_AGENT}",
            full_url,
        ]

        run = seams.run_tool(argv, timeout_seconds=timeout_remaining, cap_bytes=seams.FOOTPRINT_RESPONSE_CAP_BYTES)
        if run.timed_out:
            seams.fail(f"WFS curl request timed out for {full_url}", seams.EXIT_WORKER)
        if time.monotonic() - start_time >= seams.FOOTPRINT_TIMEOUT_SECONDS:
            seams.fail(f"footprints stage exceeded timeout of {seams.FOOTPRINT_TIMEOUT_SECONDS}s", seams.EXIT_WORKER)
        if run.returncode != 0:
            seams.fail(f"WFS curl request failed (rc {run.returncode}) for {full_url}: "
                       f"{run.stderr.strip()[-seams.EXCERPT_BYTES:]}", seams.EXIT_WORKER)

        page_data = run.stdout if isinstance(run.stdout, (bytes, bytearray)) else run.stdout.encode("utf-8")
        page_bytes = len(page_data)
        if page_bytes > seams.FOOTPRINT_RESPONSE_CAP_BYTES:
            seams.fail(f"WFS response exceeded per-response cap of {seams.FOOTPRINT_RESPONSE_CAP_BYTES} bytes",
                       seams.EXIT_WORKER)

        total_bytes += page_bytes
        if total_bytes > seams.FOOTPRINT_TOTAL_CAP_BYTES:
            seams.fail(f"WFS total acquisition exceeded total cap of {seams.FOOTPRINT_TOTAL_CAP_BYTES} bytes",
                       seams.EXIT_WORKER)

        try:
            response = json.loads(page_data.decode("utf-8"))
        except (ValueError, UnicodeDecodeError) as error:
            seams.fail(f"WFS response unparseable or truncated: {error}", seams.EXIT_WORKER)

        if not isinstance(response, dict):
            seams.fail("WFS response is not a JSON object", seams.EXIT_WORKER)

        features = response.get("features")
        if not isinstance(features, list):
            seams.fail("WFS response missing features array", seams.EXIT_WORKER)

        returned = response.get("numberReturned")
        if returned is not None and returned != len(features):
            seams.fail(f"WFS numberReturned ({returned}) does not match feature count ({len(features)})",
                       seams.EXIT_WORKER)

        time_stamp = response.get("timeStamp")
        number_matched = response.get("numberMatched")

        if not pages:
            initial_timestamp = time_stamp
            initial_number_matched = number_matched
        else:
            if time_stamp != initial_timestamp:
                seams.fail(f"WFS response timeStamp changed between pages: {initial_timestamp} -> {time_stamp}",
                           seams.EXIT_WORKER)
            if number_matched != initial_number_matched:
                seams.fail(f"WFS response numberMatched changed between pages: {initial_number_matched} -> {number_matched}",
                           seams.EXIT_WORKER)

        page_record = {
            "pageIndex": len(pages),
            "url": full_url,
            "sha256": seams.sha256_bytes(page_data),
            "bytes": page_bytes,
            "featureCount": len(features),
            "acquiredAt": seams.now_iso(),
            "rawBytes": page_data,
            "rawContent": page_data.decode("utf-8"),
        }
        pages.append(page_record)
        request_log.append({
            "url": full_url,
            "time": page_record["acquiredAt"],
            "wallSeconds": run.wall_seconds,
            "bytes": page_bytes,
            "featureCount": len(features),
        })
        ledger.append("wfs_page_acquired", page=page_record["pageIndex"], featureCount=len(features),
                      sha256=page_record["sha256"])

        # Page 0: verify server honoured SORTBY
        if len(pages) == 1:
            page_ids = [f.get("properties", {}).get(stable_id_field) for f in features]
            if len(page_ids) > 1 and page_ids != sorted(page_ids):
                seams.fail(f"WFS server did not honor SORTBY: first page features not sorted by {stable_id_field}",
                           seams.EXIT_WORKER)

        if not features:
            if initial_number_matched is not None and len(seen_ids) < initial_number_matched:
                seams.fail(f"WFS page returned 0 features before numberMatched ({initial_number_matched}) was reached",
                           seams.EXIT_WORKER)
            break

        new_ids_in_page = 0
        for feat in features:
            if not isinstance(feat, dict):
                seams.fail("WFS feature is not a JSON object")
            props = feat.get("properties")
            if not isinstance(props, dict):
                seams.fail("WFS feature missing properties object")
            cleabs = props.get(stable_id_field)
            if not cleabs or not isinstance(cleabs, str):
                seams.fail(f"WFS feature missing stable id {stable_id_field}")

            if cleabs in seen_ids:
                seams.fail(f"duplicate feature ID: {cleabs}", seams.EXIT_WORKER)
            seen_ids.add(cleabs)
            new_ids_in_page += 1
            all_features.append(feat)

        if new_ids_in_page == 0:
            seams.fail("WFS page made no progress (0 new IDs)", seams.EXIT_WORKER)

        if len(all_features) > seams.FOOTPRINT_FEATURE_CAP:
            seams.fail(f"WFS acquisition exceeded feature cap of {seams.FOOTPRINT_FEATURE_CAP} features",
                       seams.EXIT_WORKER)

        if initial_number_matched is not None and len(seen_ids) >= initial_number_matched:
            break
        if initial_number_matched is None and len(features) < page_size:
            break

        start_index += len(features)

    if initial_number_matched is not None and len(seen_ids) != initial_number_matched:
        seams.fail(f"retrieved {len(seen_ids)} features but numberMatched was {initial_number_matched}",
                   seams.EXIT_WORKER)

    provenance_meta = {
        "timeStamp": initial_timestamp,
        "numberMatched": initial_number_matched,
        "totalBytes": total_bytes,
        "pageCount": len(pages),
        "totalFeatures": len(all_features),
        "bbox": [lonmin, latmin, lonmax, latmax],
        "bboxParam": bbox_param,
        "wfsUrl": wfs_url,
        "typeNames": type_names,
        "stableIdField": stable_id_field,
    }
    return all_features, pages, request_log, provenance_meta


def generate_buildings_kml(name: str, kept_features: list[dict], kept_attributes: list[str]) -> bytes:
    """Generate buildings KML with one Folder 'buildings' and one Placemark per kept footprint."""
    lines = [
        '<?xml version="1.0" encoding="UTF-8"?>',
        '<kml xmlns="http://www.opengis.net/kml/2.2">',
        f'  <Document id="{escape(name)}-buildings">',
        '    <Folder>',
        '      <name>buildings</name>',
    ]

    for feat in kept_features:
        props = feat.get("properties", {})
        cleabs = str(props.get("cleabs", ""))
        lines.append(f'      <Placemark id="{escape(cleabs)}">')
        lines.append(f'        <name>{escape(cleabs)}</name>')
        lines.append('        <ExtendedData>')
        for attr in kept_attributes:
            val = props.get(attr)
            val_str = "" if val is None else str(val)
            lines.append(f'          <Data name="{escape(attr)}"><value>{escape(val_str)}</value></Data>')
        lines.append('        </ExtendedData>')

        # Geometry: MultiGeometry of its polygons
        lines.append('        <MultiGeometry>')
        coordinates = feat.get("geometry", {}).get("coordinates", [])
        for polygon in coordinates:
            lines.append('          <Polygon>')
            for r_idx, ring in enumerate(polygon):
                tag = "outerBoundaryIs" if r_idx == 0 else "innerBoundaryIs"
                coords_text = " ".join(f"{pos[0]:.9f},{pos[1]:.9f},{pos[2]:.3f}" if len(pos) >= 3
                                       else f"{pos[0]:.9f},{pos[1]:.9f},0"
                                       for pos in ring)
                lines.append(f'            <{tag}>')
                lines.append('              <LinearRing>')
                lines.append(f'                <coordinates>{coords_text}</coordinates>')
                lines.append('              </LinearRing>')
                lines.append(f'            </{tag}>')
            lines.append('          </Polygon>')
        lines.append('        </MultiGeometry>')
        lines.append('      </Placemark>')

    lines.append('    </Folder>')
    lines.append('  </Document>')
    lines.append('</kml>\n')
    return "\n".join(lines).encode("utf-8")


def compose_operator_manifest(name: str, delegate_manifest: dict, staged_copies: list[dict]) -> dict:
    """Compose the operator manifest with staged delegate inputs and two buildings rules."""
    staged_by_role = {item["role"]: item for item in staged_copies}

    composed_sources = []
    for src in delegate_manifest.get("sources", []):
        role = src.get("role")
        staged = staged_by_role.get(role)
        staged_path = staged["stagedPath"] if staged else src["path"]
        composed_src = dict(src)
        composed_src["path"] = staged_path
        composed_sources.append(composed_src)

    buildings_source_id = f"{name}-buildings"
    composed_sources.append({
        "id": buildings_source_id,
        "kind": "kml",
        "path": f"{get_seams().PREPARED_DIR_NAME}/{name}-buildings.kml",
        "role": "vector",
        "label": "IGN BD TOPO building footprints",
        "attribution": "IGN, Licence Ouverte 2.0",
    })

    rules = [
        {
            "sourceId": buildings_source_id,
            "layerName": "buildings",
            "geometry": "polygon",
            "target": "passability",
            "passable": False,
        },
        {
            "sourceId": buildings_source_id,
            "layerName": "buildings",
            "geometry": "polygon",
            "target": "buildability",
            "buildable": False,
        },
    ]

    composed = {
        "sources": composed_sources,
        "targetCoordinateSystem": dict(delegate_manifest["targetCoordinateSystem"]),
        "metersPerGameUnit": delegate_manifest["metersPerGameUnit"],
        "defaultMaterial": delegate_manifest["defaultMaterial"],
        "extentFeatureId": delegate_manifest["extentFeatureId"],
        "verticalPolicy": dict(delegate_manifest["verticalPolicy"]),
        "rasterPolicy": dict(delegate_manifest["rasterPolicy"]),
        "tolerances": dict(delegate_manifest["tolerances"]),
        "controlPoints": [],
        "featureMappings": rules,
    }

    if "materialRules" in delegate_manifest:
        composed["materialRules"] = list(delegate_manifest["materialRules"])
    if "slopePolicy" in delegate_manifest:
        composed["slopePolicy"] = dict(delegate_manifest["slopePolicy"])
    if "maxScaleError" in delegate_manifest:
        composed["maxScaleError"] = delegate_manifest["maxScaleError"]

    return composed


def format_sources_txt(name: str, kept_count: int, dropped_count: int, min_area_m2: float,
                       wfs_meta: dict, staged_copies: list[dict]) -> str:
    """Format driver-owned sources.txt summary for the footprint stage."""
    lines = [
        f"Footprint Stage: {name}",
        "Attribution: IGN, Licence Ouverte 2.0",
        "Licence: Licence Ouverte 2.0 (Etalab)",
        f"WFS Service: {wfs_meta.get('wfsUrl')} ({wfs_meta.get('typeNames')})",
        "Live-edition caveat: The WFS service is live and not a pinned edition; response timeStamp is observed.",
        f"Acquisition Timestamp: {wfs_meta.get('timeStamp')}",
        f"Filter Threshold: {min_area_m2:g} m^2",
        f"Features: {wfs_meta.get('totalFeatures')} acquired, {kept_count} kept, {dropped_count} dropped below threshold",
        "Staged Delegate Inputs (verified byte-identical copies):",
    ]
    for staged in staged_copies:
        lines.append(f"  - {staged['role']}: {staged['stagedPath']} (method: {staged['method']}, sha256: {staged['sha256']})")
    lines.append(f"Buildings Vector: prepared/{name}-buildings.kml")
    lines.append(f"Operator Manifest: {name}-footprints.json")
    return "\n".join(lines) + "\n"


def run_stage(*, profile, provider_id, workdir, footprint_root, centre, size_m, name,
              utm, grid, min_footprint_m2=None) -> int:
    """Execute the building-footprints acquisition, validation, and manifest composition stage."""
    seams = get_seams()
    if min_footprint_m2 is None:
        min_footprint_m2 = seams.DEFAULT_MIN_FOOTPRINT_M2

    zone, computed, hemisphere, epsg = utm
    norm_work, norm_foot = validate_root_paths(seams, workdir, footprint_root)

    prepare_area_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "prepare_area.py")
    footprints_path = os.path.abspath(__file__)

    inputs = {
        "provider": provider_id,
        "centre": [float(centre[0]), float(centre[1])],
        "sizeMeters": float(size_m),
        "name": name,
        "zone": zone,
        "hemisphere": hemisphere,
        "epsg": epsg,
        "grid": grid,
        "minFootprintM2": float(min_footprint_m2),
        "profileSha256": seams.canonical_sha256(profile),
        "driverSha256": seams.sha256_file(prepare_area_path),
        "stageSha256": seams.sha256_file(footprints_path),
    }

    delegate_manifest_path, delegate_manifest = find_delegate_manifest(seams, norm_work, profile, name)

    delegate_receipt_path = os.path.join(norm_work, seams.PREPARATION_DIR_NAME, seams.RECEIPT_NAME)
    if not os.path.isfile(delegate_receipt_path):
        seams.fail(f"delegate receipt missing: {delegate_receipt_path}")
    try:
        with open(delegate_receipt_path, encoding="utf-8") as handle:
            delegate_receipt = json.load(handle)
    except (OSError, ValueError) as error:
        seams.fail(f"delegate receipt unreadable: {delegate_receipt_path}: {error}")

    delegate_receipt_outputs = parse_delegate_receipt_outputs(delegate_receipt.get("outputs"))

    # Required output inventory for receipt verification
    manifest_rel = f"{name}-footprints.json"
    buildings_kml_rel = f"{seams.PREPARED_DIR_NAME}/{name}-buildings.kml"
    provenance_rel = "provenance/footprints-provenance.json"
    sources_rel = seams.SOURCES_NAME

    required_outputs = {manifest_rel, buildings_kml_rel, provenance_rel, sources_rel}
    for src in delegate_manifest.get("sources", []):
        required_outputs.add(os.path.join(seams.PREPARED_DIR_NAME, os.path.basename(src["path"])))

    if check_existing_root(seams, norm_foot, inputs, required_outputs):
        print(f"SKIPPED: {name} footprints already prepared and verified: "
              f"{os.path.join(norm_foot, seams.PREPARATION_DIR_NAME, seams.RECEIPT_NAME)}")
        return seams.EXIT_OK

    # Ancestor free space check
    ancestor = norm_foot
    while not os.path.exists(ancestor):
        ancestor = os.path.dirname(ancestor)
    seams.check_space(ancestor)

    prep_dir = os.path.join(norm_foot, seams.PREPARATION_DIR_NAME)
    os.makedirs(prep_dir, exist_ok=True)
    lock_path = os.path.join(prep_dir, seams.LOCK_NAME)
    seams.acquire_lock(lock_path)

    stage = None
    published = []
    moved_provenance = False

    try:
        stage = os.path.join(norm_foot, f"stage-{name}-{os.getpid()}-{time.monotonic_ns()}")
        os.mkdir(stage)
        for sub in (seams.PREPARED_DIR_NAME, "provenance", seams.PREPARATION_DIR_NAME, "work"):
            os.mkdir(os.path.join(stage, sub))

        seams.atomic_write_excl(
            os.path.join(stage, seams.PREPARATION_DIR_NAME, "STARTED"),
            (seams.now_iso() + "\n").encode("utf-8"),
        )
        ledger = seams.Ledger(os.path.join(stage, seams.PREPARATION_DIR_NAME, seams.LEDGER_NAME))
        ledger.append("stage_started", provider=provider_id, name=name, footprint_root=norm_foot, workdir=norm_work)

        # 1. Stage delegate inputs (extent, DEM, ortho)
        staged_copies = stage_delegate_inputs(seams, norm_work, stage, delegate_manifest, delegate_receipt_outputs, ledger)

        # 2. Compute bounding box in EPSG:4326 covering storage plus apron
        bbox_bounds = compute_bbox_bounds(seams, centre, size_m, grid, utm)
        lon_range = (bbox_bounds[0], bbox_bounds[2])
        lat_range = (bbox_bounds[1], bbox_bounds[3])

        # 3. Query WFS pages
        all_features, pages, request_log, wfs_meta = query_wfs_pages(seams, profile, bbox_bounds, ledger)

        # Write retained raw WFS pages to provenance/pages/page-{index:04d}.json
        pages_stage_dir = os.path.join(stage, "provenance", "pages")
        os.makedirs(pages_stage_dir, exist_ok=True)
        for p in pages:
            page_rel = f"provenance/pages/page-{p['pageIndex']:04d}.json"
            p["path"] = page_rel
            page_stage_path = os.path.join(stage, page_rel)
            seams.atomic_write_excl(page_stage_path, p["rawBytes"])
            ledger.append("raw_page_written", path=page_rel, bytes=p["bytes"], sha256=p["sha256"])

        # 4. Geometry validation & area filtering
        kept_features = []
        dropped_count = 0
        stable_id_field = profile.get("footprints", {}).get("stableIdField", "cleabs")

        for feat in all_features:
            cleabs = feat.get("properties", {}).get(stable_id_field, "")
            geom = feat.get("geometry", {})
            validate_multipolygon_geometry(seams, geom, cleabs, lon_range, lat_range)
            area_m2 = compute_feature_area(seams, geom.get("coordinates", []), epsg)
            if area_m2 < min_footprint_m2:
                dropped_count += 1
            else:
                kept_features.append(feat)

        ledger.append("footprints_filtered", total=len(all_features), kept=len(kept_features),
                      dropped=dropped_count, threshold=min_footprint_m2)

        if wfs_meta.get("numberMatched") == 0 or len(kept_features) == 0:
            print(
                f"WARNING: building footprints stage acquired {wfs_meta.get('totalFeatures')} features "
                f"and kept {len(kept_features)} for bbox {wfs_meta.get('bbox')}; empty area",
                file=sys.stderr,
            )

        # 5. Write buildings KML
        kept_attributes = profile.get("footprints", {}).get("keptAttributes", [
            "cleabs", "nature", "usage_1", "usage_2", "hauteur"
        ])
        kml_bytes = generate_buildings_kml(name, kept_features, kept_attributes)
        kml_stage_path = os.path.join(stage, buildings_kml_rel)
        seams.atomic_write_excl(kml_stage_path, kml_bytes)
        ledger.append("buildings_kml_written", path=buildings_kml_rel, bytes=len(kml_bytes),
                      sha256=seams.sha256_bytes(kml_bytes))

        # 6. Compose operator manifest
        composed_manifest = compose_operator_manifest(name, delegate_manifest, staged_copies)
        manifest_bytes = (json.dumps(composed_manifest, indent=2) + "\n").encode("utf-8")
        manifest_stage_path = os.path.join(stage, manifest_rel)
        seams.atomic_write_excl(manifest_stage_path, manifest_bytes)
        ledger.append("manifest_composed", path=manifest_rel, bytes=len(manifest_bytes),
                      sha256=seams.sha256_bytes(manifest_bytes))

        # 7. Write sources.txt
        sources_text = format_sources_txt(name, len(kept_features), dropped_count, min_footprint_m2,
                                          wfs_meta, staged_copies)
        sources_bytes = sources_text.encode("utf-8")
        sources_stage_path = os.path.join(stage, sources_rel)
        seams.atomic_write_excl(sources_stage_path, sources_bytes)

        # 8. Write provenance JSON
        combined_pages = "".join(p["rawContent"] for p in pages).encode("utf-8")
        provenance_data = {
            "wfsUrl": wfs_meta["wfsUrl"],
            "typeNames": wfs_meta["typeNames"],
            "stableIdField": wfs_meta["stableIdField"],
            "requestLog": request_log,
            "pages": [{k: v for k, v in p.items() if k not in ("rawContent", "rawBytes")} for p in pages],
            "combinedPagesSha256": seams.sha256_bytes(combined_pages),
            "totalBytes": wfs_meta["totalBytes"],
            "pageCount": wfs_meta["pageCount"],
            "totalFeaturesAcquired": wfs_meta["totalFeatures"],
            "featuresKept": len(kept_features),
            "featuresDropped": dropped_count,
            "filterThresholdM2": min_footprint_m2,
            "numberMatched": wfs_meta["numberMatched"],
            "timeStamp": wfs_meta["timeStamp"],
            "liveEditionCaveat": "The WFS service is live and not a pinned edition; response timeStamp is observed as returned.",
            "sortbyProof": f"First page features verified sorted by {stable_id_field} in ascending order.",
            "bboxWgs84": wfs_meta["bbox"],
            "bboxQueryParam": wfs_meta["bboxParam"],
            "licence": profile.get("footprints", {}).get("licence", {}),
            "attribution": profile.get("footprints", {}).get("attribution", {}),
            "delegateReceiptSha256": seams.sha256_file(delegate_receipt_path),
            "stagedCopies": staged_copies,
        }
        provenance_bytes = (json.dumps(provenance_data, indent=2) + "\n").encode("utf-8")
        provenance_stage_path = os.path.join(stage, provenance_rel)
        seams.atomic_write_excl(provenance_stage_path, provenance_bytes)

        # 9. Write stage receipt
        output_records = [
            {"path": manifest_rel, "sha256": seams.sha256_file(manifest_stage_path),
             "sizeBytes": os.path.getsize(manifest_stage_path)},
            {"path": buildings_kml_rel, "sha256": seams.sha256_file(kml_stage_path),
             "sizeBytes": os.path.getsize(kml_stage_path)},
            {"path": provenance_rel, "sha256": seams.sha256_file(provenance_stage_path),
             "sizeBytes": os.path.getsize(provenance_stage_path)},
            {"path": sources_rel, "sha256": seams.sha256_file(sources_stage_path),
             "sizeBytes": os.path.getsize(sources_stage_path)},
        ]
        for p in pages:
            output_records.append({
                "path": p["path"],
                "sha256": p["sha256"],
                "sizeBytes": p["bytes"],
            })
            required_outputs.add(p["path"])
        for staged in staged_copies:
            staged_full = os.path.join(stage, staged["stagedPath"])
            output_records.append({
                "path": staged["stagedPath"],
                "sha256": seams.sha256_file(staged_full),
                "sizeBytes": os.path.getsize(staged_full),
            })

        receipt_obj = {
            "schema": seams.SCHEMA_VERSION,
            "inputs": inputs,
            "outputs": sorted(output_records, key=lambda x: x["path"]),
            "delegate": {
                "receiptSha256": seams.sha256_file(delegate_receipt_path),
                "manifestSha256": seams.sha256_file(delegate_manifest_path),
                "files": staged_copies,
            },
        }
        receipt_bytes = (json.dumps(receipt_obj, indent=2, sort_keys=True) + "\n").encode("utf-8")
        receipt_stage_path = os.path.join(stage, seams.PREPARATION_DIR_NAME, seams.RECEIPT_NAME)
        seams.atomic_write_excl(receipt_stage_path, receipt_bytes)

        # 10. Write stage COMPLETED marker
        ledger_stage_path = os.path.join(stage, seams.PREPARATION_DIR_NAME, seams.LEDGER_NAME)
        completed_marker = {
            "receiptSha256": seams.sha256_bytes(receipt_bytes),
            "ledgerSha256": seams.sha256_file(ledger_stage_path),
        }
        completed_bytes = (json.dumps(completed_marker, indent=2) + "\n").encode("utf-8")
        completed_stage_path = os.path.join(stage, seams.PREPARATION_DIR_NAME, seams.COMPLETED_NAME)
        seams.atomic_write_excl(completed_stage_path, completed_bytes)

        # 11. Publish to footprint_root
        os.makedirs(os.path.join(norm_foot, seams.PREPARED_DIR_NAME), exist_ok=True)
        if not os.path.lexists(os.path.join(norm_foot, "provenance")):
            os.rename(os.path.join(stage, "provenance"), os.path.join(norm_foot, "provenance"))
            moved_provenance = True

        publish_items = [
            manifest_rel,
            buildings_kml_rel,
            sources_rel,
            os.path.join(seams.PREPARATION_DIR_NAME, seams.RECEIPT_NAME),
            os.path.join(seams.PREPARATION_DIR_NAME, seams.LEDGER_NAME),
            os.path.join(seams.PREPARATION_DIR_NAME, seams.COMPLETED_NAME),
        ]
        for staged in staged_copies:
            publish_items.append(staged["stagedPath"])

        for rel in publish_items:
            src_p = os.path.join(stage, rel)
            dst_p = os.path.join(norm_foot, rel)
            seams.publish_no_overwrite(src_p, dst_p)
            published.append(rel)
            seams.fsync_dir(os.path.dirname(dst_p))

        seams.fsync_dir(norm_foot)
        verify_existing_footprint_receipt(seams, norm_foot,
                                          os.path.join(norm_foot, seams.PREPARATION_DIR_NAME, seams.RECEIPT_NAME),
                                          inputs, required_outputs)
    except Exception:
        if stage is not None:
            for rel in reversed(published):
                try:
                    os.unlink(os.path.join(norm_foot, rel))
                except OSError:
                    pass
            if moved_provenance:
                try:
                    os.rename(os.path.join(norm_foot, "provenance"), os.path.join(stage, "provenance"))
                except OSError:
                    pass
            print(f"failed stage preserved: {stage}", file=sys.stderr)
        raise
    finally:
        seams.release_lock(lock_path)

    try:
        shutil.rmtree(stage)
    except OSError as error:
        print(f"footprint stage cleanup failed: {stage}: {error}", file=sys.stderr)

    print(f"prepared footprints: {os.path.join(norm_foot, manifest_rel)}")
    return seams.EXIT_OK
