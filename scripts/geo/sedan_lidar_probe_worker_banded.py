#!/usr/bin/env python3
"""Banded candidate worker for the Sedan stage-1 candidate measurement.

Banded entrypoint for tiles whose header point count exceeds the single-pass
live-set plan of sedan_lidar_probe_worker.py. Same request and measurements
contract, same per-window statistics (imported unchanged from the accepted
worker), same scientific contract. Small tiles may keep the accepted worker;
both produce identical candidates blocks for tiles the accepted worker passes.

Invocation: VENV_PYTHON sedan_lidar_probe_worker_banded.py REQUEST_JSON. Runs ONLY
inside the isolated lidar venv; laspy and numpy are imported in main() only.

Pass sequence (one invocation, N + 2 full reads of the tile, disclosed): one base
measurement read through the accepted measure(), one count pre-pass, then one
streamed pass per lattice band. Each band owns a fixed range of GLOBAL lattice
rows, is planned and allocated separately, and is released before the next band.
Cumulative merged output lists coexist and are guarded per band. A band that
fails its guard fails closed with EXIT_MEMORY; there is no shrink, retry, or
fallback to another mode. The frozen worker budget is 1,073,741,824 B; the parent
controller's overhead is not counted inside it.

Frame ownership: band envelopes are derived from the ACTUAL candidate centres of
each band's global rows, minimum and maximum over every column, then expanded by
the accepted gather halo max(window_r, e23_radius). No axis alignment between the
lattice and the point frame is assumed.

Provenance binding: this file is the executed entrypoint. The base measurements
come from the accepted worker's measure() and the per-window statistics from its
unchanged functions, and the ground-truth constants come from sedan_lidar_select.
The merged output carries five top-level fields: executed_entrypoint_path and
executed_entrypoint_sha256 (this file, hashed at run time), base_measure_module_path
and base_measure_module_sha256 (the accepted worker actually imported), and
select_module_sha256 (the select module actually imported). The two module hashes
are verified against the pins before any tile read and the run fails closed with
EXIT_PROCESSING on mismatch. The accepted worker's own output has none of these
fields; its measurement_version and candidate_measurement_version are unchanged.
The scientific arrays, counters, and base measurements are byte-identical to the
accepted output; the whole merged file is not, because of the additive fields.
"""

import hashlib
import json
import math
import os
import sys

import sedan_lidar_probe_worker as worker
import sedan_lidar_select as groundtruth

EXIT_OK = worker.EXIT_OK
EXIT_USAGE = worker.EXIT_USAGE
EXIT_MEMORY = worker.EXIT_MEMORY
EXIT_PROCESSING = worker.EXIT_PROCESSING

BAND_EXTENT_M = 200.0
BAND_ROWS = int(math.floor(BAND_EXTENT_M / groundtruth.LATTICE))

EXPECTED_BASE_MODULE_SHA256 = "5db920802e4a2da484aee02b598774bb40eb8e5152ed6328f5ff711fe0d3a6da"
EXPECTED_SELECT_MODULE_SHA256 = "4570c3507c6fb99442af0691f010a7406f64306622587abce140728873e913a1"


def _sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def provenance_fields():
    entrypoint = os.path.abspath(__file__)
    base_path = os.path.abspath(worker.__file__)
    select_path = os.path.abspath(groundtruth.__file__)
    base_sha = _sha256_file(base_path)
    select_sha = _sha256_file(select_path)
    if base_sha != EXPECTED_BASE_MODULE_SHA256:
        worker.fail(
            EXIT_PROCESSING,
            f"base measure module {base_path} sha256 {base_sha} does not match the pinned "
            f"{EXPECTED_BASE_MODULE_SHA256}; refusing to run",
        )
    if select_sha != EXPECTED_SELECT_MODULE_SHA256:
        worker.fail(
            EXIT_PROCESSING,
            f"select module {select_path} sha256 {select_sha} does not match the pinned "
            f"{EXPECTED_SELECT_MODULE_SHA256}; refusing to run",
        )
    return {
        "executed_entrypoint_path": entrypoint,
        "executed_entrypoint_sha256": _sha256_file(entrypoint),
        "base_measure_module_path": base_path,
        "base_measure_module_sha256": base_sha,
        "select_module_sha256": select_sha,
    }


def band_partition(lattice_rows, band_rows=BAND_ROWS):
    """Fixed partition of global rows [0, lattice_rows) into (r0, r1) bands, r1 exclusive."""
    return [(r0, min(r0 + band_rows, lattice_rows)) for r0 in range(0, lattice_rows, band_rows)]


def band_retention_rect(cfg, r0, r1, footprint):
    """Point-filter rectangle for global rows [r0, r1): the envelope of the actual
    candidate centres over every column of those rows, clipped to the observed
    footprint. Derived per row from the centres themselves (no shared-y or
    axis-alignment assumption). Every contained centre of the band lies inside this
    rectangle, so the accepted gather halo around it lies inside rect + margin."""
    anchor_x = float(cfg["lattice_origin_l93"][0])
    anchor_y = float(cfg["lattice_origin_l93"][1])
    spacing = float(cfg["spacing_m"])
    x_min = y_min = math.inf
    x_max = y_max = -math.inf
    for row_index in range(r0, r1):
        abs_y = anchor_y + (row_index + 0.5) * spacing
        row_x = [anchor_x + (col_index + 0.5) * spacing for col_index in range(cfg["lattice_cols"])]
        x_min = min(x_min, min(row_x))
        x_max = max(x_max, max(row_x))
        y_min = min(y_min, abs_y)
        y_max = max(y_max, abs_y)
    return {
        "x_min": max(x_min, footprint["x_min"]),
        "y_min": max(y_min, footprint["y_min"]),
        "x_max": min(x_max, footprint["x_max"]),
        "y_max": min(y_max, footprint["y_max"]),
    }


def _inside(x, y, rect, margin):
    return (
        (x >= rect["x_min"] - margin) & (x <= rect["x_max"] + margin)
        & (y >= rect["y_min"] - margin) & (y <= rect["y_max"] + margin)
    )


def count_pass(tile_path, laspy, numpy, chunk_points, footprint, margin, band_rects):
    """Count pre-pass (one full read): bounded counters only. Returns the global
    retained count under the accepted full-footprint predicate and one count per
    band under that band's rectangle, with the same retained-class and bbox
    predicate as accumulate_candidate_chunk."""
    retained_classes = numpy.asarray(worker.RETAINED_CLASSES, dtype=numpy.int64)
    global_rect = dict(footprint)
    band_counts = [0] * len(band_rects)
    global_count = 0
    try:
        with laspy.open(tile_path) as reader:
            for chunk in reader.chunk_iterator(points_per_iteration=chunk_points):
                classification = numpy.asarray(chunk.classification).astype(numpy.int64)
                retained = numpy.isin(classification, retained_classes)
                x = numpy.asarray(chunk.x, dtype=numpy.float64)
                y = numpy.asarray(chunk.y, dtype=numpy.float64)
                global_count += int(numpy.count_nonzero(retained & _inside(x, y, global_rect, margin)))
                for index, rect in enumerate(band_rects):
                    band_counts[index] += int(
                        numpy.count_nonzero(retained & _inside(x, y, rect, margin)))
    except Exception as error:  # noqa: BLE001
        worker.fail(EXIT_PROCESSING, f"count pre-pass failed on {tile_path}: {type(error).__name__}: {error}")
    return global_count, band_counts


ARRAY_COLUMNS = (
    "col", "row", "n2", "a", "b", "c", "sigma_fit", "slope_deg", "curv_bound",
    "curv_unavailable", "fit_unavailable",
    "mix2", "mix3", "mix4", "mix5", "mix6", "mix9", "mix17", "e2_dist", "e3_dist",
)
MAX_CONTAINED_ROWS = (
    (worker.CANDIDATE_OUTPUT_BOUND_BYTES - worker.CANDIDATE_PAYLOAD_DIAGNOSTIC_BYTES)
    // worker.CANDIDATE_ROW_BOUND_BYTES
)


def build_band_index(cand, budget, numpy):
    """Per-band spatial index: the accepted build_candidates_block index steps
    (2.5 m bin codes, stable argsort, bin lookup, fit headroom). Copied because the
    accepted index is embedded in its block builder; test equivalence guards drift."""
    n = cand["cursor"]
    x = cand["x"][:n]
    y = cand["y"][:n]
    cls = cand["cls"][:n]
    z = cand["z"][:n]
    bin_cols = numpy.floor(x / worker.CANDIDATE_BIN_SIZE_M).astype(numpy.int32)
    bin_rows = numpy.floor(y / worker.CANDIDATE_BIN_SIZE_M).astype(numpy.int32)
    code_mult = int(bin_rows.max()) + 2 if n else 1
    if n:
        max_code = int(bin_cols.max()) * code_mult + int(bin_rows.max())
        if max_code > 0x7FFFFFFF:
            worker.fail(
                EXIT_MEMORY,
                f"int32 bin codes would overflow at this retention span "
                f"({max_code} > 2147483647); refusing the band index build",
            )
        codes = bin_cols * numpy.int32(code_mult)
        codes += bin_rows
    else:
        codes = numpy.empty(0, dtype=numpy.int32)
    del bin_cols, bin_rows
    order = numpy.argsort(codes, kind="stable")
    cand["sort_cls"] = cls[order]
    cand["sort_x"] = x[order]
    cand["sort_y"] = y[order]
    cand["sort_z"] = z[order]
    del x, y, cls, z
    for key in ("x", "y", "cls", "z"):
        cand.pop(key, None)
    if n:
        sorted_codes = codes[order]
        boundaries = numpy.flatnonzero(sorted_codes[1:] != sorted_codes[:-1]) + 1
        starts = numpy.concatenate(([0], boundaries))
        uniq = sorted_codes[starts]
        ends = numpy.append(starts[1:], n)
        del sorted_codes, boundaries
    else:
        starts = numpy.empty(0, dtype=numpy.int64)
        uniq = numpy.empty(0, dtype=numpy.int32)
        ends = numpy.empty(0, dtype=numpy.int64)
    del codes, order
    cand["bin_lookup"] = {
        (int(code // code_mult), int(code % code_mult)): (int(s), int(e))
        for code, s, e in zip(uniq.tolist(), starts.tolist(), ends.tolist())
    }
    planned_peak = int(cand.get("planned_peak_bytes", 0))
    cand["fit_headroom_bytes"] = worker.CANDIDATE_FIT_WORKSPACE_ALLOWANCE_BYTES + max(0, budget - planned_peak)


def evaluate_band_rows(cand, cfg, r0, r1, footprint, numpy, lists, contained_total):
    """Global-row evaluation for rows [r0, r1). The centre arithmetic is the accepted
    (row + 0.5) * spacing form on GLOBAL indices, so centres match the single pass bit
    for bit; the window statistics are the accepted _evaluate_candidate_window."""
    anchor_x = float(cfg["lattice_origin_l93"][0])
    anchor_y = float(cfg["lattice_origin_l93"][1])
    spacing = float(cfg["spacing_m"])
    for row_index in range(r0, r1):
        abs_y = anchor_y + (row_index + 0.5) * spacing
        rel_y = abs_y - cand["origin_y"]
        for col_index in range(cfg["lattice_cols"]):
            abs_x = anchor_x + (col_index + 0.5) * spacing
            if not groundtruth.window_inside_footprint(abs_x, abs_y, cfg["window_r"], footprint):
                continue
            contained_total += 1
            if contained_total > MAX_CONTAINED_ROWS:
                _ok, _bound, text = worker.candidate_payload_preflight(contained_total)
                worker.fail(
                    EXIT_MEMORY,
                    f"candidates payload preflight bound exceeded during banded enumeration: {text}; "
                    "refusing to expand the candidate lists further",
                )
            values = worker._evaluate_candidate_window(
                cand, abs_x - cand["origin_x"], rel_y, numpy)
            lists["col"].append(col_index)
            lists["row"].append(row_index)
            for key in ARRAY_COLUMNS[2:]:
                lists[key].append(values[key])
    return contained_total


def build_banded_block(tile_path, laspy, numpy, request, cfg, header_info, band_rows=BAND_ROWS):
    chunk = int(request["chunk_size_points"])
    budget = int(request["memory_budget_bytes"])
    footprint = dict(header_info["footprint_l93"])
    margin = max(cfg["window_r"], cfg["e23_radius"])
    cols = cfg["lattice_cols"]
    span_x_m = (footprint["x_max"] - footprint["x_min"]) + 2.0 * margin
    span_y_m = (footprint["y_max"] - footprint["y_min"]) + 2.0 * margin
    bands = band_partition(cfg["lattice_rows"], band_rows)
    rects = [band_retention_rect(cfg, r0, r1, footprint) for r0, r1 in bands]
    global_count, band_counts = count_pass(
        tile_path, laspy, numpy, chunk, footprint, margin, rects)
    worker_log = [f"banded candidates: {len(bands)} bands of <= {band_rows} rows; "
                  f"count pre-pass points_retained {global_count}"]
    lists = {key: [] for key in ARRAY_COLUMNS}
    contained_total = 0
    for index, (r0, r1) in enumerate(bands):
        count = band_counts[index]
        cumulative_cells = cols * r1
        ok, _peak, lines = worker.candidate_live_set_plan(
            count, chunk, cumulative_cells, span_x_m, span_y_m, budget)
        if not ok:
            worker.fail(
                EXIT_MEMORY,
                f"band {index} rows [{r0}, {r1}) live-set guard refused ({count} points, "
                f"{cumulative_cells} cumulative cells): " + "; ".join(lines)
                + "; failing closed, no shrink or retry",
            )
        clone = dict(cfg, lattice_rows=r1)
        band_header = {"header_point_count": count, "footprint_l93": footprint}
        cand = worker.open_candidate_accumulator(clone, band_header, chunk, budget, numpy)
        cand["footprint"] = rects[index]
        try:
            with laspy.open(tile_path) as reader:
                for records in reader.chunk_iterator(points_per_iteration=chunk):
                    worker.accumulate_candidate_chunk(cand, records, numpy)
        except SystemExit:
            raise
        except Exception as error:  # noqa: BLE001
            worker.fail(EXIT_PROCESSING,
                        f"band {index} stream failed on {tile_path}: {type(error).__name__}: {error}")
        if cand["cursor"] != count:
            worker.fail(EXIT_PROCESSING,
                        f"band {index} streamed {cand['cursor']} points but the count pre-pass counted {count}")
        cand["footprint"] = footprint
        build_band_index(cand, budget, numpy)
        contained_before = contained_total
        contained_total = evaluate_band_rows(cand, clone, r0, r1, footprint, numpy, lists, contained_total)
        cand.clear()
        worker_log.append(f"band {index} rows [{r0}, {r1}) points {count} "
                          f"contained {contained_total - contained_before}")
    for line in worker_log:
        print(line, file=sys.stderr, flush=True)
    cells_enumerated = cols * cfg["lattice_rows"]
    ok, _bound, text = worker.candidate_payload_preflight(contained_total)
    if not ok:
        worker.fail(EXIT_MEMORY, f"candidates payload preflight refused: {text}")
    rows_lists_full = cells_enumerated * worker.CANDIDATE_ROW_LIST_BYTES_PER_ROW
    payload_bound_full = cells_enumerated * worker.CANDIDATE_ROW_BOUND_BYTES + worker.CANDIDATE_PAYLOAD_DIAGNOSTIC_BYTES
    serialization_peak = rows_lists_full + 2 * payload_bound_full + worker.CANDIDATE_PLAN_SLACK_BYTES
    if serialization_peak > budget:
        worker.fail(
            EXIT_MEMORY,
            f"serialization plan {serialization_peak} B (row lists {rows_lists_full} B + 2 x payload "
            f"bound {payload_bound_full} B + slack) exceeds budget {budget} B; refusing to serialize",
        )
    anchor_x = float(cfg["lattice_origin_l93"][0])
    anchor_y = float(cfg["lattice_origin_l93"][1])
    block = {
        "candidate_measurement_version": 1,
        "lattice_origin_l93": [anchor_x, anchor_y],
        "aoi_sw_l93": [float(cfg["aoi_sw_l93"][0]), float(cfg["aoi_sw_l93"][1])],
        "spacing_m": float(cfg["spacing_m"]),
        "lattice_cols": int(cols),
        "lattice_rows": int(cfg["lattice_rows"]),
        "window_r": float(cfg["window_r"]),
        "e23_radius": float(cfg["e23_radius"]),
        "footprint_observed_l93": dict(footprint),
        "footprint_request_l93": dict(cfg["footprint_l93"]),
        "retention_margin_m": float(margin),
        "bin_size_m": worker.CANDIDATE_BIN_SIZE_M,
        "row_order": "row-major (row ascending, then col ascending)",
        "containment": "window circle fully inside the observed header footprint",
        "points_retained": int(global_count),
        "cells_enumerated": int(cells_enumerated),
        "cells_contained": int(contained_total),
        "cells_skipped_containment": int(cells_enumerated - contained_total),
        "columns": list(ARRAY_COLUMNS),
        "arrays": lists,
    }
    payload = json.dumps(block, sort_keys=True, allow_nan=False)
    if not worker.candidates_payload_within_bound(payload):
        worker.fail(
            EXIT_MEMORY,
            f"serialized candidates block is {len(payload.encode('utf-8'))} bytes, beyond "
            f"the {worker.CANDIDATE_OUTPUT_BOUND_BYTES}-byte output bound; refusing to emit",
        )
    return block


def main(argv):
    if len(argv) != 2:
        worker.usage_fail("usage: sedan_lidar_probe_worker_banded.py REQUEST_JSON")
    try:
        import laspy  # noqa: F401  (isolated venv only; never installed from here)
        import numpy
    except ImportError as error:
        worker.usage_fail(
            "laspy is unavailable from this interpreter; this worker runs ONLY inside the "
            "isolated lidar venv (see sedan_lidar_probe.py plan-tools); never run it with the "
            f"system python and never install anything from here (import error: {error})"
        )
    request = worker.load_request(argv[1])
    budget = request["memory_budget_bytes"]
    estimate = worker.memory_estimate(request["chunk_size_points"])
    if estimate > budget:
        worker.fail(
            EXIT_MEMORY,
            f"refusing oversized chunk settings: estimated peak {estimate} bytes exceeds the "
            f"budget {budget} bytes",
        )
    if not os.path.isfile(request["tile_path"]):
        worker.usage_fail(f"tile_path is not a regular file: {request['tile_path']}")
    provenance = provenance_fields()
    base_request = {key: value for key, value in request.items() if key != "candidates"}
    measurements = worker.measure(base_request, laspy, numpy)
    if "candidates" in request:
        cfg = worker.validate_candidates_request(request["candidates"])
        measurements["candidates"] = build_banded_block(
            request["tile_path"], laspy, numpy, request, cfg, measurements["header"])
    measurements.update(provenance)
    output_path = request["output_path"]
    try:
        text = json.dumps(measurements, indent=2, sort_keys=True, allow_nan=False) + "\n"
    except ValueError as error:
        worker.fail(EXIT_PROCESSING, f"measurements JSON is not strictly finite: {error}")
    try:
        with open(output_path, "w", encoding="utf-8") as handle:
            handle.write(text)
    except OSError as error:
        worker.fail(EXIT_PROCESSING, f"cannot write measurements JSON at {output_path}: {error}")
    print(
        f"measured {measurements['points']['total']} points in "
        f"{measurements['points']['chunk_count']} chunks; measurements at {output_path}"
    )
    return EXIT_OK


if __name__ == "__main__":
    sys.exit(main(sys.argv))
