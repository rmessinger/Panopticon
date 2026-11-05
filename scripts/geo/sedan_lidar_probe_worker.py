#!/usr/bin/env python3
"""Isolated LiDAR data-probe worker for the Sedan stage-1 first-tile gate.

Runs ONLY inside the isolated tool venv (laspy 2.x plus lazrs, numpy transitively;
see sedan_lidar_probe.py plan-tools). Executed under the system python it prints a
clear error and exits 2; it never attempts pip or any installation. The controller
(sedan_lidar_probe.py) invokes this worker as a SUBPROCESS and applies all gate
logic itself; this worker returns RAW measurements only.

Request JSON (path given as the single file argument): tile_path, aoi_bbox_l93,
filter_bbox_l93 (AOI plus margin, both LAMB93), cell_size_m, chunk_size_points,
memory_budget_bytes, output_path. Optional: candidates (protocol v1.2 stage-1
candidate-measurement mode; see below).

Streaming: laspy.open plus LasReader.chunk_iterator with a fixed chunk size
(2,000,000 points per chunk by default); the whole cloud is never materialized.
Per chunk the bbox filter is applied first, then the filtered points are reduced
into bounded accumulators: class histograms, per-10-m-cell class-2 counts, and
class-2 z handled as 0.5 m bin counts (median by interpolation inside the
containing bin), never as raw per-point lists. A memory tally (estimated bytes
per point per needed field times chunk size plus accumulator sizes) is enforced
as a real guard before opening the file and re-checked every chunk.

Candidate mode (request "candidates"): when the request carries the candidates
object {lattice_origin_l93, lattice_cols, lattice_rows, spacing_m, aoi_sw_l93,
window_r, footprint_l93, e23_radius}, the SAME single streaming pass additionally
retains the points inside the observed header footprint expanded by the E2/E3
radius, keeping classes 2, 3, 4, 5, 6, 9, 17 in preallocated numpy arrays
(float32 x/y RELATIVE to the retention bbox minimum: absolute float32 at
Lambert-93 magnitudes quantises at ~0.03 m and would corrupt window membership
and the Huber fits, relative storage keeps the 4-byte fields at ~1e-4 m; uint8
class; float64 z aligned per point, valid on class-2 rows). A conservative
operation-by-operation LIVE-SET plan runs from the observed header point count
BEFORE the point reader streams or any accumulator is allocated: every phase
(accumulate; index build; sort; sorted copies; per-candidate evaluation;
serialization) sums the arrays coexisting at its worst instant from
header-derived upper bounds, and a planned peak beyond the budget fails closed
EXIT_MEMORY with the full arithmetic table logged. After streaming, a
deterministic spatial
index (2.5 m bin grid over the retention bbox, numpy lexsort by bin column, bin
row, then insertion order) serves the per-candidate loop: every requested lattice
cell whose 2.5 m window circle lies fully inside the OBSERVED header footprint
(the request footprint_l93 is only a wrong-tile intersection guard; containment
is never derived from it) gets the exact section-6 statistics: class-2 window
points, the OLS plane fit over the CLASS-2 rows of the window ONLY (protocol
v1.2 s6.2; the class mixes and the E2/E3 distances keep using all retained
classes), EXACTLY two Huber passes,
sigma_fit without the floor, slope in degrees, the curvature bound from a
weighted full quadratic fit with the final Huber weights (rank-deficient designs
recorded as unavailable, never guessed), class-mix counts, and the minimum
distance to any class-9 / class-17 point within the E2/E3 radius (null when
none). Results are written as compact column-major parallel arrays (row-major
order). The per-candidate Python loop costs roughly 2-4 minutes on a full 1 km
tile (~100k contained candidates); the payload size is preflight-bounded BEFORE
the JSON string is allocated and the serialized block must stay under 64 MiB or
the worker fails closed (a NaN/Inf statistic can never be serialized:
allow_nan=False). all output is deterministic: same input, same bytes.

Note on testability: the laspy call path cannot be offline-tested before the
isolated install; it is written to the documented laspy 2.x API (laspy.open,
LasReader.chunk_iterator, scaled x/y/z and classification fields on the chunked
records, header version/point_count/scales/offsets/x_min/x_max/y_min/y_max, VLR
user_id/record_id scanning for the COPC VLR and the OGC WKT projection VLR) and
is validated at execution time. The candidate-mode geometry and guard helpers are
pure or stdlib-importable and are unit-tested offline; the full candidate path is
exercised by the real-laspy synthetic smokes under the isolated venv.
"""

import json
import math
import os
import sys

import sedan_lidar_select as groundtruth

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

# Candidate-measurement mode (protocol v1.2 stage-1 gate d): per-cell §6 statistics
# on the canonical 3.048 m lattice. The window classification mix retains classes
# 2, 3, 4, 5, 6, 9 and 17 only. Costs are disclosed: the live-set plan bounds the
# whole candidate path from the header point count before anything is allocated,
# accumulation is preallocated from that count (float32 x/y, uint8 class, float64
# z for class-2 rows), and the per-candidate loop below costs roughly 3-10 s per
# 100k candidates on this class of machine (about 2-4 minutes for a full 1 km
# tile's ~100k footprint-contained candidates), dominated by small numpy slice
# operations.
RETAINED_CLASSES = (2, 3, 4, 5, 6, 9, 17)
CANDIDATE_OUTPUT_BOUND_BYTES = 64 << 20
CANDIDATE_BIN_SIZE_M = 2.5
CANDIDATE_MAX_CELLS = 4_000_000

# Live-set plan constants (audit Critical 2): the plan bounds every concurrent
# array of the candidate path from the observed header point count and refuses
# BEFORE the point reader streams or anything is preallocated. All values are
# documented conservative upper bounds, never measurements.
CANDIDATE_CHUNK_EXTRA_BYTES_PER_POINT = 43
# classification int64 copy 8 + chunk x/y float64 16 + bbox/retained/combined
# masks 3 + selected indices int64 8 + float32 class copy 1 = 36, rounded up.
CANDIDATE_PLAN_SLACK_BYTES = 32 << 20
CANDIDATE_LOOKUP_BYTES_PER_ENTRY = 128
CANDIDATE_FIT_WORKSPACE_ALLOWANCE_BYTES = 16 << 20
CANDIDATE_FIT_BYTES_PER_POINT = 256
CANDIDATE_REPORT_DUMP_ALLOWANCE_BYTES = 16 << 20
# python-object row cost in the block lists: 20 columns x (boxed number 24-28 B
# + list slot 8 B) = 640-720, plus list growth over-allocation ~12.5 percent.
CANDIDATE_ROW_LIST_BYTES_PER_ROW = 810
# Payload preflight widths (audit Critical 3): worst-case ASCII field width per
# column plus the separating comma. int64 is at most 19 digits + sign; repr of
# -sys.float_info.max is 24 chars; "false" is 5 chars.
CANDIDATE_ROW_BOUND_BYTES = 10 * 21 + 8 * 25 + 2 * 7
CANDIDATE_PAYLOAD_DIAGNOSTIC_BYTES = 4096

CANDIDATE_REQUIRED_KEYS = (
    "lattice_origin_l93",
    "lattice_cols",
    "lattice_rows",
    "spacing_m",
    "aoi_sw_l93",
    "window_r",
    "footprint_l93",
    "e23_radius",
)
CANDIDATE_FROZEN_VALUES = {
    "spacing_m": groundtruth.LATTICE,
    "window_r": groundtruth.WINDOW_R,
    "e23_radius": groundtruth.E23_DIST,
}


def candidate_payload_preflight(contained_rows):
    """Payload bound BEFORE serialization (audit Critical 3): contained rows x
    worst-case field width plus the diagnostic block structure. Refusal happens
    before any payload string is allocated; the post-serialization check below
    stays as a belt."""
    bound = contained_rows * CANDIDATE_ROW_BOUND_BYTES + CANDIDATE_PAYLOAD_DIAGNOSTIC_BYTES
    text = (
        f"{contained_rows} contained rows x {CANDIDATE_ROW_BOUND_BYTES} B worst-case row "
        f"(10 int cols x 21 B + 8 float cols x 25 B + 2 bool cols x 7 B) + "
        f"{CANDIDATE_PAYLOAD_DIAGNOSTIC_BYTES} B diagnostic structure = {bound} B vs "
        f"output bound {CANDIDATE_OUTPUT_BOUND_BYTES} B"
    )
    return bound <= CANDIDATE_OUTPUT_BOUND_BYTES, bound, text


def candidate_live_set_plan(header_point_count, chunk_size_points, lattice_cells,
                            span_x_m, span_y_m, budget):
    """Conservative operation-by-operation LIVE-SET plan for candidate mode
    (audit Critical 2). Each phase sums the arrays coexisting at its worst
    instant, from header-derived upper bounds (worst case: every header point is
    retained and class 2, and every lattice cell is contained). Returns
    (ok, peak_bytes, lines): ok is False when the planned peak exceeds the budget
    or the int32 bin-code span is unsafe; lines carry every counted array with
    dtype, bytes and lifetime. int64 is reserved for point indices; bins and
    codes use int32 because the retention span (~1 km / 2.5 m bins) gives only
    a few hundred bins per axis, verified here against the 2^31-1 code bound."""
    count = int(header_point_count)
    chunk = int(chunk_size_points)
    cells = min(int(lattice_cells), CANDIDATE_MAX_CELLS)
    bins_x = int(span_x_m / CANDIDATE_BIN_SIZE_M) + 2
    bins_y = int(span_y_m / CANDIDATE_BIN_SIZE_M) + 2
    bins_total = bins_x * bins_y
    span_ok = bins_total <= 0x7FFFFFFF
    slack = CANDIDATE_PLAN_SLACK_BYTES
    acc = ACCUMULATOR_RESERVE_BYTES + 3 * 256 * 8
    prealloc = 17 * count   # x float32 + y float32 + cls uint8 + z float64
    lookup_bytes = min(count, bins_total) * CANDIDATE_LOOKUP_BYTES_PER_ENTRY
    rows_lists = cells * CANDIDATE_ROW_LIST_BYTES_PER_ROW
    payload_bound = cells * CANDIDATE_ROW_BOUND_BYTES + CANDIDATE_PAYLOAD_DIAGNOSTIC_BYTES
    phases = [
        ("accumulate", prealloc + (128 + CANDIDATE_CHUNK_EXTRA_BYTES_PER_POINT) * chunk + acc, [
            f"candidate prealloc x float32 + y float32 + cls uint8 + z float64 = 17 B x {count} = {prealloc} B "
            "(lifetime: allocated before streaming, freed field-by-field after the sorted copies)",
            f"base chunk workspace tally 128 B x {chunk} = {128 * chunk} B "
            "(laspy scaled records, masks, histogram temporaries; freed when the streaming frame exits)",
            f"candidate chunk extras {CANDIDATE_CHUNK_EXTRA_BYTES_PER_POINT} B x {chunk} = "
            f"{CANDIDATE_CHUNK_EXTRA_BYTES_PER_POINT * chunk} B "
            "(classification int64 copy, bbox/retained masks, selected indices, uint8 class copy)",
            f"accumulator reserve {acc} B (class histograms, 10 m cells, z bins; lives through candidate mode)",
        ]),
        ("index_bin_floors", prealloc + 16 * count, [
            f"unsorted prealloc still alive = 17 B x {count} = {prealloc} B",
            f"floor division float64 temporary = 8 B x {count} = {8 * count} B (freed within the statement)",
            f"floor result float64 temporary = 8 B x {count} = {8 * count} B (freed by the int32 cast)",
        ]),
        ("index_codes", prealloc + 16 * count, [
            f"unsorted prealloc still alive = 17 B x {count} = {prealloc} B",
            f"bin_cols int32 = 4 B x {count} = {4 * count} B (freed once codes exist)",
            f"bin_rows int32 = 4 B x {count} = {4 * count} B (freed once codes exist)",
            f"codes multiply int32 temporary = 4 B x {count} = {4 * count} B (freed within the statement)",
            f"codes int32 = 4 B x {count} = {4 * count} B (bin_col * code_mult + bin_row, span-checked below)",
        ]),
        ("index_sort_order", prealloc + 28 * count, [
            f"unsorted prealloc still alive = 17 B x {count} = {prealloc} B (needed by the copies below)",
            f"codes int32 = 4 B x {count} = {4 * count} B",
            f"stable argsort workspace = 16 B x {count} = {16 * count} B "
            "(conservative: index permutation plus aux buffer)",
            f"order int64 = 8 B x {count} = {8 * count} B (point indices; int64 by design)",
        ]),
        ("index_bin_boundaries", prealloc + 16 * count, [
            f"unsorted prealloc still alive = 17 B x {count} = {prealloc} B",
            f"order int64 = 8 B x {count} = {8 * count} B",
            f"sorted_codes int32 = 4 B x {count} = {4 * count} B (codes[order]; freed with codes)",
            f"codes int32 = 4 B x {count} = {4 * count} B",
        ]),
        ("index_sorted_copies", prealloc + 16 * count, [
            f"worst step (sort_z copy): remaining prealloc z float64 8 B x {count} = {8 * count} B "
            "(x/y/cls fields already copied and freed)",
            f"earlier sorted copies sort_x float32 + sort_y float32 + sort_cls uint8 = 9 B x {count} = "
            f"{9 * count} B",
            f"order int64 = 8 B x {count} = {8 * count} B (deleted after the copies)",
            f"sort_z float64 = 8 B x {count} = {8 * count} B",
        ]),
        ("candidate_evaluation", prealloc + lookup_bytes + rows_lists
            + CANDIDATE_FIT_WORKSPACE_ALLOWANCE_BYTES + acc, [
            f"sorted copies sort_x float32 + sort_y float32 + sort_cls uint8 + sort_z float64 = "
            f"17 B x {count} = {prealloc} B (unsorted prealloc deleted after the copies)",
            f"bin_lookup dict <= min({count}, {bins_total} bins) entries x "
            f"{CANDIDATE_LOOKUP_BYTES_PER_ENTRY} B = {lookup_bytes} B",
            f"candidate row object lists {cells} cells x {CANDIDATE_ROW_LIST_BYTES_PER_ROW} B = "
            f"{rows_lists} B (bounded by the payload preflight mid-enumeration)",
            f"per-window fit workspace allowance {CANDIDATE_FIT_WORKSPACE_ALLOWANCE_BYTES} B "
            f"({CANDIDATE_FIT_BYTES_PER_POINT} B x gathered points, runtime-checked per candidate)",
            f"accumulator reserve {acc} B (base-mode dicts still alive)",
        ]),
        ("serialization", rows_lists + 2 * payload_bound, [
            f"candidate row object lists {cells} cells x {CANDIDATE_ROW_LIST_BYTES_PER_ROW} B = "
            f"{rows_lists} B",
            f"payload string <= preflight bound {payload_bound} B",
            f"post-check utf-8 encode copy <= {payload_bound} B (belt)",
            "sorted copies and bin_lookup deleted before serialization",
        ]),
        ("report_write", rows_lists + CANDIDATE_REPORT_DUMP_ALLOWANCE_BYTES, [
            f"candidate row object lists {cells} cells x {CANDIDATE_ROW_LIST_BYTES_PER_ROW} B = "
            f"{rows_lists} B",
            f"incremental json.dump chunk allowance {CANDIDATE_REPORT_DUMP_ALLOWANCE_BYTES} B",
        ]),
    ]
    lines = [
        f"int32 bin-code span check: bins_x {bins_x} x bins_y {bins_y} = {bins_total} "
        + ("<= 2147483647 OK" if span_ok else "> 2147483647 UNSAFE (int32 codes would overflow)")
    ]
    peak = 0
    peak_phase = ""
    for name, total, arrays in phases:
        peak_phase = name if total > peak else peak_phase
        peak = max(peak, total)
        lines.append(f"phase {name}: {total} B")
        lines.extend(f"  {line}" for line in arrays)
    lines.append(f"planned peak {peak} B in phase {peak_phase} + fixed slack "
                 f"{slack} B = {peak + slack} B vs budget {budget} B")
    ok = span_ok and peak + slack <= budget
    return ok, peak + slack, lines


def candidates_payload_within_bound(payload_text):
    """Output bound: the serialized candidates block must stay under 64 MiB."""
    return len(payload_text.encode("utf-8")) <= CANDIDATE_OUTPUT_BOUND_BYTES


def _finite_number(value):
    return (
        isinstance(value, (int, float))
        and not isinstance(value, bool)
        and math.isfinite(value)
    )


def _footprint_ok(footprint):
    return (
        isinstance(footprint, dict)
        and all(_finite_number(footprint.get(edge)) for edge in ("x_min", "y_min", "x_max", "y_max"))
        and footprint["x_max"] > footprint["x_min"]
        and footprint["y_max"] > footprint["y_min"]
    )


def validate_candidates_request(cfg):
    """Structural + frozen-value validation of the request's candidates object;
    exits EXIT_USAGE on any defect. The frozen protocol constants (lattice spacing,
    window radius, E2/E3 radius) are not free parameters."""
    if not isinstance(cfg, dict) or not all(key in cfg for key in CANDIDATE_REQUIRED_KEYS):
        usage_fail(
            f"request candidates object must carry keys {sorted(CANDIDATE_REQUIRED_KEYS)}"
        )
    for key in ("lattice_origin_l93", "aoi_sw_l93"):
        point = cfg[key]
        if (
            not isinstance(point, list)
            or len(point) != 2
            or not all(_finite_number(value) for value in point)
        ):
            usage_fail(f"candidates.{key} must be a [x, y] pair of finite numbers")
    dx = abs(float(cfg["lattice_origin_l93"][0]) - float(cfg["aoi_sw_l93"][0]))
    dy = abs(float(cfg["lattice_origin_l93"][1]) - float(cfg["aoi_sw_l93"][1]))
    if max(dx, dy) > 1e-6:
        usage_fail(
            "candidates.aoi_sw_l93 must anchor row/col at lattice_origin_l93 "
            f"(disagreement {max(dx, dy)} m)"
        )
    for key in ("lattice_cols", "lattice_rows"):
        value = cfg[key]
        if not isinstance(value, int) or isinstance(value, bool) or not (1 <= value <= 4096):
            usage_fail(f"candidates.{key} must be an integer in [1, 4096]")
    if cfg["lattice_cols"] * cfg["lattice_rows"] > CANDIDATE_MAX_CELLS:
        usage_fail(
            f"candidates cell count {cfg['lattice_cols'] * cfg['lattice_rows']} exceeds "
            f"the {CANDIDATE_MAX_CELLS}-cell bound"
        )
    for key, frozen in CANDIDATE_FROZEN_VALUES.items():
        value = cfg[key]
        if not _finite_number(value) or float(value) != frozen:
            usage_fail(f"candidates.{key} must be exactly the frozen value {frozen}")
    if not _footprint_ok(cfg["footprint_l93"]):
        usage_fail("candidates.footprint_l93 must be a non-degenerate numeric rectangle")
    return cfg


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


def open_candidate_accumulator(cfg, header_info, chunk_size_points, budget, numpy):
    """Validate, run the live-set plan guard, and preallocate the candidate-mode
    accumulators from the observed header. The guard runs BEFORE the point reader
    streams and before anything is allocated: the planned peak of every phase
    (see candidate_live_set_plan) must fit the budget or the worker fails closed
    EXIT_MEMORY with the arithmetic table logged. Coordinates are stored as
    float32 RELATIVE to the retention bbox minimum (header footprint expanded by
    max(window_r, e23_radius)): at Lambert-93 magnitudes (~8.4e5) absolute float32
    quantisation is ~0.03 m, which would corrupt window membership and the Huber
    fits; relative storage keeps the 4-byte-per-field memory model at ~1e-4 m
    resolution. z is one aligned float64 row per retained point (valid for
    class-2 rows, NaN elsewhere), matching the plan arithmetic exactly."""
    cfg = validate_candidates_request(cfg)
    header_count = header_info["header_point_count"]
    footprint = dict(header_info["footprint_l93"])
    margin = max(cfg["window_r"], cfg["e23_radius"])
    span_x_m = (footprint["x_max"] - footprint["x_min"]) + 2.0 * margin
    span_y_m = (footprint["y_max"] - footprint["y_min"]) + 2.0 * margin
    lattice_cells = cfg["lattice_cols"] * cfg["lattice_rows"]
    ok, peak, lines = candidate_live_set_plan(
        header_count, chunk_size_points, lattice_cells, span_x_m, span_y_m, budget)
    if not ok:
        fail(
            EXIT_MEMORY,
            "candidate pre-allocation guard (live-set plan) refused the tile: "
            + "; ".join(lines)
            + "; refusing to plan or allocate",
        )
    origin_x = footprint["x_min"] - margin
    origin_y = footprint["y_min"] - margin
    requested = cfg["footprint_l93"]
    intersects = (
        footprint["x_min"] <= requested["x_max"]
        and footprint["x_max"] >= requested["x_min"]
        and footprint["y_min"] <= requested["y_max"]
        and footprint["y_max"] >= requested["y_min"]
    )
    if not intersects:
        fail(
            EXIT_PROCESSING,
            "observed header footprint does not intersect the request footprint_l93 "
            f"(observed {footprint}, request {requested}); refusing candidate mode "
            "(possible wrong tile)",
        )
    return {
        "cfg": cfg,
        "footprint": footprint,
        "origin_x": origin_x,
        "origin_y": origin_y,
        "margin": margin,
        "n_alloc": header_count,
        "planned_peak_bytes": peak,
        "cursor": 0,
        "points_retained": 0,
        "x": numpy.empty(header_count, dtype=numpy.float32),
        "y": numpy.empty(header_count, dtype=numpy.float32),
        "cls": numpy.empty(header_count, dtype=numpy.uint8),
        "z": numpy.empty(header_count, dtype=numpy.float64),
    }


def accumulate_candidate_chunk(cand, chunk, numpy):
    """One-pass accumulation: bbox + retained-class filter of the chunk into the
    preallocated candidate arrays. The header point count is the allocation bound;
    a file that streams more points than its header claims fails closed."""
    classification = numpy.asarray(chunk.classification).astype(numpy.int64)
    x = numpy.asarray(chunk.x, dtype=numpy.float64)
    y = numpy.asarray(chunk.y, dtype=numpy.float64)
    in_bbox = (
        (x >= cand["footprint"]["x_min"] - cand["margin"])
        & (x <= cand["footprint"]["x_max"] + cand["margin"])
        & (y >= cand["footprint"]["y_min"] - cand["margin"])
        & (y <= cand["footprint"]["y_max"] + cand["margin"])
    )
    retained = numpy.isin(classification, numpy.asarray(RETAINED_CLASSES, dtype=numpy.int64))
    selected = numpy.flatnonzero(in_bbox & retained)
    if selected.size == 0:
        return
    if cand["cursor"] + selected.size > cand["n_alloc"]:
        fail(
            EXIT_MEMORY,
            f"streamed points exceed the header point count {cand['n_alloc']}; "
            "refusing to grow beyond the guarded pre-allocation",
        )
    start = cand["cursor"]
    end = start + selected.size
    cand["x"][start:end] = (x[selected] - cand["origin_x"]).astype(numpy.float32)
    cand["y"][start:end] = (y[selected] - cand["origin_y"]).astype(numpy.float32)
    classes = classification[selected].astype(numpy.uint8)
    cand["cls"][start:end] = classes
    zs = numpy.asarray(chunk.z, dtype=numpy.float64)[selected]
    cand["z"][start:end] = numpy.where(classes == CLASS_GROUND, zs, numpy.nan)
    cand["cursor"] = end
    cand["points_retained"] += int(selected.size)


def _huber_plane_fit(u, v, z, numpy):
    """OLS plane via lstsq on the [1, u, v] design, then EXACTLY two Huber passes
    (scale max(1.4826 x MAD, 1e-9), constant 1.345, weights min(1, 1.345 s / |r|),
    zero residual keeps full weight); sigma_fit = 1.4826 x final MAD without the
    floor. Returns (a, b, c, sigma_fit, weights) or None when the design is
    rank-deficient (degenerate-safe; recorded as fit_unavailable)."""
    count = u.size
    design = numpy.stack([numpy.ones(count), u, v], axis=1)
    solution, _residuals, rank, _sv = numpy.linalg.lstsq(design, z, rcond=None)
    if rank < 3:
        return None
    weights = numpy.ones(count)
    for _ in range(2):
        residuals = z - design @ solution
        median = numpy.median(residuals)
        mad = numpy.median(numpy.abs(residuals - median))
        scale = max(groundtruth.MAD_SCALE * mad, groundtruth.S_FLOOR)
        weights = numpy.ones(count)
        nonzero = residuals != 0.0
        weights[nonzero] = numpy.minimum(
            1.0, groundtruth.HUBER_C * scale / numpy.abs(residuals[nonzero])
        )
        sqrt_w = numpy.sqrt(weights)
        solution, _residuals, rank, _sv = numpy.linalg.lstsq(
            design * sqrt_w[:, None], z * sqrt_w, rcond=None
        )
        if rank < 3:
            return None
    residuals = z - design @ solution
    median = numpy.median(residuals)
    mad = numpy.median(numpy.abs(residuals - median))
    sigma_fit = groundtruth.MAD_SCALE * mad
    return solution[0], solution[1], solution[2], sigma_fit, weights


def _curvature_bound_fit(u, v, z, weights, numpy):
    """Weighted full quadratic fit with the final plane Huber weights; returns the
    max |q| bound over the half-footprint square via the frozen coefficient bound,
    or None when the quadratic design is rank-deficient."""
    count = u.size
    quad = numpy.stack(
        [numpy.ones(count), u, v, u * u, u * v, v * v], axis=1
    )
    sqrt_w = numpy.sqrt(weights)
    solution, _residuals, rank, _sv = numpy.linalg.lstsq(
        quad * sqrt_w[:, None], z * sqrt_w, rcond=None
    )
    if rank < 6:
        return None
    d, e, f = (float(solution[3]), float(solution[4]), float(solution[5]))
    return groundtruth.curvature_bound_from_coeffs(d, e, f)


def _evaluate_candidate_window(cand, rel_x, rel_y, numpy):
    """§6 statistics for one contained candidate window centre (retention-frame
    coordinates). The plane and curvature fits use the CLASS-2 rows of the window
    ONLY (protocol v1.2 s6.2); the class mixes and the E2/E3 distances use all
    retained classes. Returns the per-cell column values as plain python types.
    Every emitted statistic is finite: a non-finite fit invalidates the whole
    plane fit and is recorded unavailable, never serialized."""
    cfg = cand["cfg"]
    window_r = cfg["window_r"]
    e23_radius = cfg["e23_radius"]
    gather_r = max(window_r, e23_radius)
    sx, sy, scls, sz = cand["sort_x"], cand["sort_y"], cand["sort_cls"], cand["sort_z"]
    lookup = cand["bin_lookup"]
    bc0 = int(math.floor((rel_x - gather_r) / CANDIDATE_BIN_SIZE_M))
    bc1 = int(math.floor((rel_x + gather_r) / CANDIDATE_BIN_SIZE_M))
    br0 = int(math.floor((rel_y - gather_r) / CANDIDATE_BIN_SIZE_M))
    br1 = int(math.floor((rel_y + gather_r) / CANDIDATE_BIN_SIZE_M))
    pieces = []
    for bin_col in range(bc0, bc1 + 1):
        for bin_row in range(br0, br1 + 1):
            span = lookup.get((bin_col, bin_row))
            if span is not None:
                pieces.append(numpy.arange(span[0], span[1]))
    row = {
        "n2": 0, "a": None, "b": None, "c": None,
        "sigma_fit": None, "slope_deg": None, "curv_bound": None,
        "curv_unavailable": False, "fit_unavailable": False,
        "mix2": 0, "mix3": 0, "mix4": 0, "mix5": 0, "mix6": 0, "mix9": 0, "mix17": 0,
        "e2_dist": None, "e3_dist": None,
    }
    if not pieces:
        return row
    nearby = numpy.concatenate(pieces)
    headroom = cand.get("fit_headroom_bytes")
    if headroom is not None and nearby.size * CANDIDATE_FIT_BYTES_PER_POINT > headroom:
        fail(
            EXIT_MEMORY,
            f"candidate window fit workspace would need {nearby.size} gathered points x "
            f"{CANDIDATE_FIT_BYTES_PER_POINT} B = {nearby.size * CANDIDATE_FIT_BYTES_PER_POINT} B, "
            f"beyond the planned headroom {headroom} B; refusing the fit",
        )
    dx = sx[nearby].astype(numpy.float64) - rel_x
    dy = sy[nearby].astype(numpy.float64) - rel_y
    d2 = dx * dx + dy * dy
    in_window = d2 <= window_r * window_r
    classes_near = scls[nearby]
    win_classes = classes_near[in_window]
    histogram = numpy.bincount(win_classes, minlength=18)
    row["mix2"] = int(histogram[2])
    row["mix3"] = int(histogram[3])
    row["mix4"] = int(histogram[4])
    row["mix5"] = int(histogram[5])
    row["mix6"] = int(histogram[6])
    row["mix9"] = int(histogram[9])
    row["mix17"] = int(histogram[17])
    row["n2"] = row["mix2"]
    for cls_value, key in ((CLASS_WATER, "e2_dist"), (CLASS_BRIDGE, "e3_dist")):
        near_mask = (classes_near == cls_value) & (d2 <= e23_radius * e23_radius)
        if near_mask.any():
            distance = math.sqrt(float(d2[near_mask].min()))
            if distance <= e23_radius:
                row[key] = distance
    if row["n2"] >= 3:
        win = nearby[in_window & (classes_near == CLASS_GROUND)]
        u = sx[win].astype(numpy.float64) - rel_x
        v = sy[win].astype(numpy.float64) - rel_y
        z = sz[win]
        fit = _huber_plane_fit(u, v, z, numpy)
        if fit is None:
            row["fit_unavailable"] = True
            return row
        a, b, c_coef, sigma_fit, weights = fit
        row["a"] = float(a)
        row["b"] = float(b)
        row["c"] = float(c_coef)
        row["sigma_fit"] = float(sigma_fit)
        row["slope_deg"] = math.degrees(math.atan(math.hypot(b, c_coef)))
        if row["n2"] >= 6:
            bound = _curvature_bound_fit(u, v, z, weights, numpy)
            if bound is None:
                row["curv_unavailable"] = True
            else:
                row["curv_bound"] = float(bound)
        else:
            row["curv_unavailable"] = True
    plane_keys = ("a", "b", "c", "sigma_fit", "slope_deg")
    if any(row[key] is not None and not math.isfinite(row[key]) for key in plane_keys):
        for key in plane_keys:
            row[key] = None
        row["curv_bound"] = None
        row["curv_unavailable"] = True
        row["fit_unavailable"] = True
    for key in ("curv_bound", "e2_dist", "e3_dist"):
        value = row[key]
        if value is not None and not math.isfinite(value):
            row[key] = None
            if key == "curv_bound":
                row["curv_unavailable"] = True
    return row


def build_candidates_block(cand, request, numpy):
    """Deterministic spatial index (2.5 m bin grid over the retention bbox; bins
    are floor(relative_coord / 2.5) with the retention bbox minimum as the grid
    origin; a stable argsort of the combined int32 bin code gives the same
    (bin_col, bin_row, insertion order) lexsort order because rows are
    non-negative and strictly below code_mult) then the per-candidate loop over
    the requested lattice cells, emitting only cells whose window circle lies
    fully inside the OBSERVED header footprint. Rows are ordered row-major
    (row asc, then col asc). The live set stays inside the planned peak: the
    unsorted prealloc fields are deleted as their sorted fancy-index copies are
    built, and the bin/order/codes temporaries are deleted as soon as they are
    consumed. The payload is preflight-bounded BEFORE the JSON string is
    allocated (the post-serialization 64 MiB check stays as a belt) and the
    sorted copies plus the bin lookup are freed before serialization."""
    cfg = cand["cfg"]
    n = cand["cursor"]
    x = cand["x"][:n]
    y = cand["y"][:n]
    cls = cand["cls"][:n]
    z = cand["z"][:n]
    bin_cols = numpy.floor(x / CANDIDATE_BIN_SIZE_M).astype(numpy.int32)
    bin_rows = numpy.floor(y / CANDIDATE_BIN_SIZE_M).astype(numpy.int32)
    code_mult = int(bin_rows.max()) + 2 if n else 1
    if n:
        max_code = int(bin_cols.max()) * code_mult + int(bin_rows.max())
        if max_code > 0x7FFFFFFF:
            fail(
                EXIT_MEMORY,
                f"int32 bin codes would overflow at this retention span "
                f"({max_code} > 2147483647); refusing the index build",
            )
        codes = bin_cols * numpy.int32(code_mult)
        codes += bin_rows
    else:
        codes = numpy.empty(0, dtype=numpy.int32)
    del bin_cols, bin_rows
    order = numpy.argsort(codes, kind="stable")
    cand["sort_cls"] = cls[order]
    cls = None
    cand.pop("cls", None)
    cand["sort_x"] = x[order]
    x = None
    cand.pop("x", None)
    cand["sort_y"] = y[order]
    y = None
    cand.pop("y", None)
    if n:
        sorted_codes = codes[order]
        boundaries = numpy.flatnonzero(sorted_codes[1:] != sorted_codes[:-1]) + 1
        starts = numpy.concatenate(([0], boundaries))
        uniq = sorted_codes[starts]
        ends = numpy.append(starts[1:], n)
        del codes, sorted_codes, boundaries
    else:
        starts = numpy.empty(0, dtype=numpy.int64)
        uniq = numpy.empty(0, dtype=numpy.int32)
        ends = numpy.empty(0, dtype=numpy.int64)
        del codes
    cand["sort_z"] = z[order]
    z = None
    cand.pop("z", None)
    order = None
    cand["bin_lookup"] = {
        (int(code // code_mult), int(code % code_mult)): (int(s), int(e))
        for code, s, e in zip(uniq.tolist(), starts.tolist(), ends.tolist())
    }
    budget = int(request["memory_budget_bytes"])
    planned_peak = int(cand.get("planned_peak_bytes", 0))
    cand["fit_headroom_bytes"] = CANDIDATE_FIT_WORKSPACE_ALLOWANCE_BYTES + max(0, budget - planned_peak)
    max_contained_rows = (
        (CANDIDATE_OUTPUT_BOUND_BYTES - CANDIDATE_PAYLOAD_DIAGNOSTIC_BYTES)
        // CANDIDATE_ROW_BOUND_BYTES
    )
    anchor_x = float(cfg["lattice_origin_l93"][0])
    anchor_y = float(cfg["lattice_origin_l93"][1])
    spacing = float(cfg["spacing_m"])
    arrays = {key: [] for key in (
        "col", "row", "n2", "a", "b", "c", "sigma_fit", "slope_deg", "curv_bound",
        "curv_unavailable", "fit_unavailable",
        "mix2", "mix3", "mix4", "mix5", "mix6", "mix9", "mix17", "e2_dist", "e3_dist",
    )}
    contained = 0
    for row_index in range(cfg["lattice_rows"]):
        abs_y = anchor_y + (row_index + 0.5) * spacing
        rel_y = abs_y - cand["origin_y"]
        for col_index in range(cfg["lattice_cols"]):
            abs_x = anchor_x + (col_index + 0.5) * spacing
            if not groundtruth.window_inside_footprint(
                abs_x, abs_y, cfg["window_r"], cand["footprint"]
            ):
                continue
            contained += 1
            values = _evaluate_candidate_window(cand, abs_x - cand["origin_x"], rel_y, numpy)
            arrays["col"].append(col_index)
            arrays["row"].append(row_index)
            for key in ("n2", "a", "b", "c", "sigma_fit", "slope_deg", "curv_bound",
                        "curv_unavailable", "fit_unavailable",
                        "mix2", "mix3", "mix4", "mix5", "mix6", "mix9", "mix17",
                        "e2_dist", "e3_dist"):
                arrays[key].append(values[key])
            if contained > max_contained_rows:
                _ok, _bound, text = candidate_payload_preflight(contained)
                fail(
                    EXIT_MEMORY,
                    f"candidates payload preflight bound exceeded during enumeration: {text}; "
                    "refusing to expand the candidate lists further",
                )
    ok, _bound, text = candidate_payload_preflight(contained)
    if not ok:
        fail(EXIT_MEMORY, f"candidates payload preflight refused: {text}")
    # Free the spatial index and the sorted copies BEFORE serialization: the
    # block lists are the only candidate data still alive while the payload
    # string is built (plan phase serialization).
    for key in ("sort_x", "sort_y", "sort_cls", "sort_z", "bin_lookup",
                "fit_headroom_bytes", "planned_peak_bytes"):
        cand.pop(key, None)
    block = {
        "candidate_measurement_version": 1,
        "lattice_origin_l93": [anchor_x, anchor_y],
        "aoi_sw_l93": [float(cfg["aoi_sw_l93"][0]), float(cfg["aoi_sw_l93"][1])],
        "spacing_m": spacing,
        "lattice_cols": int(cfg["lattice_cols"]),
        "lattice_rows": int(cfg["lattice_rows"]),
        "window_r": float(cfg["window_r"]),
        "e23_radius": float(cfg["e23_radius"]),
        "footprint_observed_l93": dict(cand["footprint"]),
        "footprint_request_l93": dict(cfg["footprint_l93"]),
        "retention_margin_m": float(cand["margin"]),
        "bin_size_m": CANDIDATE_BIN_SIZE_M,
        "row_order": "row-major (row ascending, then col ascending)",
        "containment": "window circle fully inside the observed header footprint",
        "points_retained": int(cand["points_retained"]),
        "cells_enumerated": int(cfg["lattice_cols"] * cfg["lattice_rows"]),
        "cells_contained": int(contained),
        "cells_skipped_containment": int(cfg["lattice_cols"] * cfg["lattice_rows"] - contained),
        "columns": list(arrays.keys()),
        "arrays": arrays,
    }
    payload = json.dumps(block, sort_keys=True, allow_nan=False)
    if not candidates_payload_within_bound(payload):
        fail(
            EXIT_MEMORY,
            f"serialized candidates block is {len(payload.encode('utf-8'))} bytes, beyond "
            f"the {CANDIDATE_OUTPUT_BOUND_BYTES}-byte output bound; refusing to emit",
        )
    return block



def measure(request, laspy, numpy):
    tile_path = request["tile_path"]
    aoi = request["aoi_bbox_l93"]
    window = request["filter_bbox_l93"]
    cell = float(request["cell_size_m"])
    chunk_points = int(request["chunk_size_points"])
    budget = int(request["memory_budget_bytes"])
    candidates_cfg = request.get("candidates")
    cand = None
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
            if candidates_cfg is not None:
                # Live-set plan guard + preallocation happen BEFORE any point is
                # streamed, from the observed header point count; the
                # base-measurement tally continues to be re-checked every chunk
                # below.
                cand = open_candidate_accumulator(
                    candidates_cfg, header_info, chunk_points, budget, numpy)

            def stream_chunks():
                """One streaming pass. Every per-chunk temporary (laspy scaled
                records, masks, unique-key arrays) lives only inside this frame:
                explicit coexistence control (audit Critical 2), so the
                candidate-mode index build starts with the chunk workspace
                freed."""
                nonlocal total, chunk_count, z_min, z_max
                nonlocal full_hist, window_hist, aoi_hist
                # Documented laspy 2.x API: scaled x/y/z and classification on chunked records.
                for chunk in reader.chunk_iterator(points_per_iteration=chunk_points):
                    chunk_count += 1
                    classification = numpy.asarray(chunk.classification).astype(numpy.int64)
                    total += int(classification.size)
                    full_hist += numpy.bincount(classification, minlength=256)[:256]
                    x = numpy.asarray(chunk.x)
                    y = numpy.asarray(chunk.y)
                    if cand is not None:
                        accumulate_candidate_chunk(cand, chunk, numpy)
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
            stream_chunks()
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
    if cand is not None:
        # Outside the base try: a candidates output-bound refusal exits EXIT_MEMORY,
        # not the base EXIT_PROCESSING remap; any other build error is a processing
        # failure of the candidate mode only.
        try:
            measurements["candidates"] = build_candidates_block(cand, request, numpy)
        except SystemExit:
            raise
        except Exception as error:  # noqa: BLE001
            fail(
                EXIT_PROCESSING,
                f"failed to build the candidates block: {type(error).__name__}: {error}",
            )
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
            json.dump(measurements, handle, indent=2, sort_keys=True, allow_nan=False)
            handle.write("\n")
    except OSError as error:
        fail(EXIT_PROCESSING, f"cannot write measurements JSON at {output_path}: {error}")
    except ValueError as error:
        fail(EXIT_PROCESSING, f"measurements JSON is not strictly finite at {output_path}: {error}")
    print(
        f"measured {measurements['points']['total']} points in "
        f"{measurements['points']['chunk_count']} chunks; measurements at {output_path}"
    )
    return EXIT_OK


if __name__ == "__main__":
    sys.exit(main(sys.argv))
