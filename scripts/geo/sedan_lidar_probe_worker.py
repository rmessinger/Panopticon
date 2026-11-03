#!/usr/bin/env python3
"""Isolated LiDAR data-probe worker for the Sedan stage-1 first-tile gate.

Runs ONLY inside the isolated tool venv (laspy 2.x plus lazrs, numpy transitively;
see sedan_lidar_probe.py plan-tools). Executed under the system python it prints a
clear error and exits 2; it never attempts pip or any installation. The controller
(sedan_lidar_probe.py) invokes this worker as a SUBPROCESS and applies all gate
logic itself; this worker returns RAW measurements only.

Request JSON (path given as the single file argument): tile_path, aoi_bbox_l93,
filter_bbox_l93 (AOI plus margin, both LAMB93), cell_size_m, chunk_size_points,
memory_budget_bytes, output_path.

Streaming: laspy.open plus LasReader.chunk_iterator with a fixed chunk size
(2,000,000 points per chunk by default); the whole cloud is never materialized.
Per chunk the bbox filter is applied first, then the filtered points are reduced
into bounded accumulators: class histograms, per-10-m-cell class-2 counts, and
class-2 z handled as 0.5 m bin counts (median by interpolation inside the
containing bin), never as raw per-point lists. A memory tally (estimated bytes
per point per needed field times chunk size plus accumulator sizes) is enforced
as a real guard before opening the file and re-checked every chunk.

Note on testability: the laspy call path cannot be offline-tested before the
isolated install; it is written to the documented laspy 2.x API (laspy.open,
LasReader.chunk_iterator, scaled x/y/z and classification fields on the chunked
records, header version/point_count/scales/offsets/x_min/x_max/y_min/y_max, VLR
user_id/record_id scanning for the COPC VLR and the OGC WKT projection VLR) and
is validated at execution time.
"""

import json
import math
import os
import sys

EXIT_OK = 0
EXIT_USAGE = 2
EXIT_MEMORY = 3
EXIT_PROCESSING = 4

# Needed fields are x, y, z and classification (4 x 8 B raw); the estimate adds a
# documented safety factor for laspy record arrays, masks and scaled temporaries.
BYTES_PER_POINT_ESTIMATE = 128
ACCUMULATOR_RESERVE_BYTES = 8 << 20
ACCUMULATOR_BYTES_PER_ENTRY = 120
Z_BIN_WIDTH_M = 0.5
MAX_CHUNK_POINTS = 10_000_000
REQUIRED_REQUEST_KEYS = (
    "tile_path",
    "aoi_bbox_l93",
    "filter_bbox_l93",
    "cell_size_m",
    "chunk_size_points",
    "memory_budget_bytes",
    "output_path",
)
CLASS_GROUND = 2
CLASS_WATER = 9
CLASS_BRIDGE = 17
WKT_VLR_USER_ID = "LASF_Projection"
WKT_VLR_RECORD_ID = 2112
COPC_VLR_USER_ID = "copc"


def fail(code, message):
    print(f"sedan_lidar_probe_worker: {message}", file=sys.stderr, flush=True)
    sys.exit(code)


def usage_fail(message):
    fail(EXIT_USAGE, message)


def load_request(path):
    try:
        with open(path, "r", encoding="utf-8") as handle:
            request = json.load(handle)
    except (OSError, ValueError) as error:
        usage_fail(f"cannot read request JSON at {path}: {error}")
    if not isinstance(request, dict) or not all(key in request for key in REQUIRED_REQUEST_KEYS):
        usage_fail(f"request at {path} must be a JSON object with keys {sorted(REQUIRED_REQUEST_KEYS)}")
    for bbox_key in ("aoi_bbox_l93", "filter_bbox_l93"):
        bbox = request[bbox_key]
        if not isinstance(bbox, dict) or not all(
            isinstance(bbox.get(edge), (int, float)) for edge in ("x_min", "y_min", "x_max", "y_max")
        ):
            usage_fail(f"request field {bbox_key} must carry numeric x_min/y_min/x_max/y_max")
    if not isinstance(request["chunk_size_points"], int) or not (
        0 < request["chunk_size_points"] <= MAX_CHUNK_POINTS
    ):
        usage_fail(f"chunk_size_points must be an integer in (0, {MAX_CHUNK_POINTS}]")
    if not isinstance(request["memory_budget_bytes"], int) or request["memory_budget_bytes"] <= 0:
        usage_fail("memory_budget_bytes must be a positive integer")
    return request


def memory_estimate(chunk_size_points, accumulator_entries=0):
    accumulator_bytes = (
        ACCUMULATOR_RESERVE_BYTES + accumulator_entries * ACCUMULATOR_BYTES_PER_ENTRY + 3 * 256 * 8
    )
    return chunk_size_points * BYTES_PER_POINT_ESTIMATE + accumulator_bytes


def median_from_bins(bin_counts, bin_width):
    """Median by linear interpolation inside the containing bin, average of the two
    central order statistics for even totals; deterministic, no raw z lists kept."""
    total = sum(bin_counts.values())
    if total <= 0:
        return None
    values = []
    for rank in (int(math.floor((total - 1) / 2.0)), int(math.floor(total / 2.0))):
        cumulative = 0
        for index in sorted(bin_counts):
            count = bin_counts[index]
            if cumulative + count > rank:
                values.append((index + (rank - cumulative) / count) * bin_width)
                break
            cumulative += count
    return sum(values) / len(values)


def histogram_dict(hist):
    return {str(index): int(count) for index, count in enumerate(hist) if count}


def scan_header(reader):
    header = reader.header
    vlrs = list(header.vlrs)
    evlrs = getattr(header, "evlrs", None)
    if evlrs:
        vlrs.extend(evlrs)
    copc_vlr_present = any(str(getattr(vlr, "user_id", "")) == COPC_VLR_USER_ID for vlr in vlrs)
    crs_wkt = None
    for vlr in vlrs:
        if (
            str(getattr(vlr, "user_id", "")) == WKT_VLR_USER_ID
            and getattr(vlr, "record_id", None) == WKT_VLR_RECORD_ID
        ):
            crs_wkt = getattr(vlr, "string", None) or str(vlr)
            break
    return {
        "las_version": f"{header.version.major}.{header.version.minor}",
        "point_format_id": int(header.point_format.id),
        "header_point_count": int(header.point_count),
        "scales": [float(value) for value in header.scales],
        "offsets": [float(value) for value in header.offsets],
        "crs_wkt": crs_wkt,
        "global_encoding_wkt_bit": bool(getattr(header.global_encoding, "wkt", False)),
        "copc_vlr_present": copc_vlr_present,
        "vlr_count": len(vlrs),
        # ACTUAL LAS header XY footprint in the file CRS: the scaled header min/max
        # extents as laspy exposes them (header.x_min/x_max/y_min/y_max). The
        # controller's coverage gate intersects the AOI with THIS extent; it is never
        # derived from the tile filename (review blocker B1 fix).
        "footprint_l93": {
            "x_min": float(header.x_min),
            "y_min": float(header.y_min),
            "x_max": float(header.x_max),
            "y_max": float(header.y_max),
        },
    }


def measure(request, laspy, numpy):
    tile_path = request["tile_path"]
    aoi = request["aoi_bbox_l93"]
    window = request["filter_bbox_l93"]
    cell = float(request["cell_size_m"])
    chunk_points = int(request["chunk_size_points"])
    budget = int(request["memory_budget_bytes"])
    full_hist = numpy.zeros(256, dtype=numpy.int64)
    window_hist = numpy.zeros(256, dtype=numpy.int64)
    aoi_hist = numpy.zeros(256, dtype=numpy.int64)
    cells = {}
    z_bins = {}
    z_min = None
    z_max = None
    total = 0
    chunk_count = 0
    try:
        with laspy.open(tile_path) as reader:
            header_info = scan_header(reader)
            # Documented laspy 2.x API: scaled x/y/z and classification on chunked records.
            for chunk in reader.chunk_iterator(points_per_iteration=chunk_points):
                chunk_count += 1
                classification = numpy.asarray(chunk.classification).astype(numpy.int64)
                total += int(classification.size)
                full_hist += numpy.bincount(classification, minlength=256)[:256]
                x = numpy.asarray(chunk.x)
                y = numpy.asarray(chunk.y)
                in_window = (
                    (x >= window["x_min"]) & (x <= window["x_max"])
                    & (y >= window["y_min"]) & (y <= window["y_max"])
                )
                if in_window.any():
                    window_cls = classification[in_window]
                    window_hist += numpy.bincount(window_cls, minlength=256)[:256]
                    in_aoi = (
                        (x[in_window] >= aoi["x_min"]) & (x[in_window] <= aoi["x_max"])
                        & (y[in_window] >= aoi["y_min"]) & (y[in_window] <= aoi["y_max"])
                    )
                    if in_aoi.any():
                        aoi_hist += numpy.bincount(window_cls[in_aoi], minlength=256)[:256]
                    ground = window_cls == CLASS_GROUND
                    if ground.any():
                        ground_x = x[in_window][ground]
                        ground_y = y[in_window][ground]
                        cols = numpy.floor((ground_x - aoi["x_min"]) / cell).astype(numpy.int64)
                        rows = numpy.floor((ground_y - aoi["y_min"]) / cell).astype(numpy.int64)
                        keys, counts = numpy.unique(
                            numpy.stack([cols, rows], axis=1), axis=0, return_counts=True
                        )
                        for (col, row), count in zip(keys.tolist(), counts.tolist()):
                            key = f"{col},{row}"
                            cells[key] = cells.get(key, 0) + int(count)
                        ground_in_aoi = in_aoi[ground]
                        if ground_in_aoi.any():
                            zs = numpy.asarray(chunk.z)[in_window][ground][ground_in_aoi]
                            if zs.size:
                                z_min = float(zs.min()) if z_min is None else min(z_min, float(zs.min()))
                                z_max = float(zs.max()) if z_max is None else max(z_max, float(zs.max()))
                                bin_keys, bin_counts = numpy.unique(
                                    numpy.floor(zs / Z_BIN_WIDTH_M).astype(numpy.int64),
                                    return_counts=True,
                                )
                                for index, count in zip(bin_keys.tolist(), bin_counts.tolist()):
                                    z_bins[index] = z_bins.get(index, 0) + int(count)
                tally = memory_estimate(chunk_points, len(cells) + len(z_bins))
                if tally > budget:
                    fail(
                        EXIT_MEMORY,
                        f"memory budget exceeded during streaming: tally {tally} > budget {budget} "
                        f"after {chunk_count} chunks; refusing to continue",
                    )
    except Exception as error:  # noqa: BLE001 - the controller gates on any worker failure
        fail(EXIT_PROCESSING, f"failed to measure {tile_path}: {type(error).__name__}: {error}")
    aoi_class2 = int(aoi_hist[CLASS_GROUND])
    measurements = {
        "measurement_version": 1,
        "tile_path": tile_path,
        "tile_bytes": os.path.getsize(tile_path),
        "header": header_info,
        "points": {
            "total": total,
            "chunk_count": chunk_count,
            "chunk_size_points": chunk_points,
            "full_tile_class_histogram": histogram_dict(full_hist),
        },
        "aoi": {
            "class_histogram": histogram_dict(aoi_hist),
            "class2_count": aoi_class2,
            "z_min": z_min,
            "z_max": z_max,
            "z_median": median_from_bins(z_bins, Z_BIN_WIDTH_M),
            "z_bin_width_m": Z_BIN_WIDTH_M,
            "z_median_method": "linear interpolation inside the containing 0.5 m z bin",
            "class9_count": int(aoi_hist[CLASS_WATER]),
            "class17_count": int(aoi_hist[CLASS_BRIDGE]),
        },
        "coverage": {
            "cell_size_m": cell,
            "grid_anchor_l93": [aoi["x_min"], aoi["y_min"]],
            "class2_cell_counts": cells,
        },
        "memory": {
            "estimated_peak_bytes": memory_estimate(chunk_points, len(cells) + len(z_bins)),
            "budget_bytes": budget,
            "bytes_per_point_estimate": BYTES_PER_POINT_ESTIMATE,
        },
    }
    return measurements


def main(argv):
    if len(argv) != 2:
        usage_fail("usage: sedan_lidar_probe_worker.py REQUEST_JSON")
    try:
        import laspy  # noqa: F401  (isolated venv only; never installed from here)
        import numpy
    except ImportError as error:
        usage_fail(
            "laspy is unavailable from this interpreter; this worker runs ONLY inside the "
            "isolated lidar venv (see sedan_lidar_probe.py plan-tools); never run it with the "
            f"system python and never install anything from here (import error: {error})"
        )
    request = load_request(argv[1])
    estimate = memory_estimate(request["chunk_size_points"])
    if estimate > request["memory_budget_bytes"]:
        fail(
            EXIT_MEMORY,
            f"refusing oversized chunk settings: estimated peak {estimate} bytes exceeds the "
            f"budget {request['memory_budget_bytes']} bytes "
            f"(chunk {request['chunk_size_points']} points x {BYTES_PER_POINT_ESTIMATE} "
            f"bytes per point plus accumulators)",
        )
    if not os.path.isfile(request["tile_path"]):
        usage_fail(f"tile_path is not a regular file: {request['tile_path']}")
    measurements = measure(request, laspy, numpy)
    output_path = request["output_path"]
    try:
        with open(output_path, "w", encoding="utf-8") as handle:
            json.dump(measurements, handle, indent=2, sort_keys=True)
            handle.write("\n")
    except OSError as error:
        fail(EXIT_PROCESSING, f"cannot write measurements JSON at {output_path}: {error}")
    print(
        f"measured {measurements['points']['total']} points in "
        f"{measurements['points']['chunk_count']} chunks; measurements at {output_path}"
    )
    return EXIT_OK


if __name__ == "__main__":
    sys.exit(main(sys.argv))
