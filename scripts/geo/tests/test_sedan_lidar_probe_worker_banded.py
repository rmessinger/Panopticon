"""Tests for scripts/geo/sedan_lidar_probe_worker_banded.py (stdlib unittest).

Stdlib cases run under any interpreter. Real-laspy cases skip truthfully when laspy
or numpy is not importable; they run under the owned lidar venv and compare the banded
candidates block against the accepted worker's candidate path byte for byte.
"""

import contextlib
import hashlib
import importlib.util
import io
import json
import math
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

_GEO_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(_GEO_DIR))

import sedan_lidar_probe_worker as worker  # noqa: E402
import sedan_lidar_probe_worker_banded as banded  # noqa: E402
import sedan_lidar_select as groundtruth  # noqa: E402

FROZEN_BUDGET_BYTES = 1_073_741_824
REAL_HEADER_POINTS = 26_448_036
REAL_TILE_CELLS = 328 * 328
REAL_SPAN_M = 1000.0
MARGIN = 3.048
HAS_LASPY = bool(importlib.util.find_spec("laspy") and importlib.util.find_spec("numpy"))
PROVENANCE_KEYS = (
    "executed_entrypoint_path",
    "executed_entrypoint_sha256",
    "base_measure_module_path",
    "base_measure_module_sha256",
    "select_module_sha256",
)
ACCEPTED_WORKER_SHA256 = "5db920802e4a2da484aee02b598774bb40eb8e5152ed6328f5ff711fe0d3a6da"
ACCEPTED_SELECT_SHA256 = "4570c3507c6fb99442af0691f010a7406f64306622587abce140728873e913a1"


def _sha256_bytes(data):
    return hashlib.sha256(data).hexdigest()


def _cfg(rows, cols, anchor_x, anchor_y, footprint):
    return {
        "lattice_origin_l93": [anchor_x, anchor_y],
        "aoi_sw_l93": [anchor_x, anchor_y],
        "lattice_cols": cols,
        "lattice_rows": rows,
        "spacing_m": groundtruth.LATTICE,
        "window_r": groundtruth.WINDOW_R,
        "e23_radius": groundtruth.E23_DIST,
        "footprint_l93": dict(footprint),
    }


def _brute_envelope(cfg, r0, r1, footprint):
    xs, ys = [], []
    for row in range(r0, r1):
        for col in range(cfg["lattice_cols"]):
            xs.append(cfg["lattice_origin_l93"][0] + (col + 0.5) * cfg["spacing_m"])
            ys.append(cfg["lattice_origin_l93"][1] + (row + 0.5) * cfg["spacing_m"])
    return {
        "x_min": max(min(xs), footprint["x_min"]), "y_min": max(min(ys), footprint["y_min"]),
        "x_max": min(max(xs), footprint["x_max"]), "y_max": min(max(ys), footprint["y_max"]),
    }


class BandPartitionTests(unittest.TestCase):

    def test_band_height_is_200_m_of_whole_rows(self):
        self.assertEqual(banded.BAND_ROWS, 65)
        self.assertEqual(banded.BAND_ROWS, math.floor(200.0 / groundtruth.LATTICE))

    def test_partition_owns_every_global_row_exactly_once(self):
        for rows in (1, 2, 64, 65, 66, 130, 328, 330):
            for band_rows in (1, 2, 3, 65):
                owned = []
                for r0, r1 in banded.band_partition(rows, band_rows):
                    self.assertLess(r0, r1)
                    owned.extend(range(r0, r1))
                self.assertEqual(owned, list(range(rows)), (rows, band_rows))

    def test_partition_is_deterministic(self):
        self.assertEqual(banded.band_partition(328), banded.band_partition(328))
        self.assertEqual(banded.band_partition(328)[-1], (325, 328))

    def test_envelope_matches_brute_force_centre_extent_on_skewed_frame(self):
        footprint = {"x_min": 1010.0, "y_min": 2003.5, "x_max": 1041.0, "y_max": 2030.0}
        cfg = _cfg(23, 17, 1000.3, 2000.7, footprint)
        for r0, r1 in banded.band_partition(23, 4):
            self.assertEqual(
                banded.band_retention_rect(cfg, r0, r1, footprint),
                _brute_envelope(cfg, r0, r1, footprint),
                (r0, r1),
            )

    def test_envelope_contains_every_contained_centre_of_its_band(self):
        footprint = {"x_min": 1010.0, "y_min": 2003.5, "x_max": 1041.0, "y_max": 2030.0}
        cfg = _cfg(23, 17, 1000.3, 2000.7, footprint)
        for r0, r1 in banded.band_partition(23, 3):
            rect = banded.band_retention_rect(cfg, r0, r1, footprint)
            for row in range(r0, r1):
                for col in range(cfg["lattice_cols"]):
                    cx = cfg["lattice_origin_l93"][0] + (col + 0.5) * cfg["spacing_m"]
                    cy = cfg["lattice_origin_l93"][1] + (row + 0.5) * cfg["spacing_m"]
                    if groundtruth.window_inside_footprint(cx, cy, cfg["window_r"], footprint):
                        self.assertTrue(rect["x_min"] <= cx <= rect["x_max"], (row, col))
                        self.assertTrue(rect["y_min"] <= cy <= rect["y_max"], (row, col))


class PlannerArithmeticTests(unittest.TestCase):

    def test_single_pass_plan_refuses_real_26m_header(self):
        ok, peak, lines = worker.candidate_live_set_plan(
            REAL_HEADER_POINTS, 2_000_000, REAL_TILE_CELLS,
            REAL_SPAN_M + 2 * MARGIN, REAL_SPAN_M + 2 * MARGIN, FROZEN_BUDGET_BYTES)
        self.assertFalse(ok)
        self.assertGreater(peak, FROZEN_BUDGET_BYTES)
        self.assertTrue(any(line.startswith("planned peak") for line in lines))

    def test_banded_plan_passes_per_band_at_real_header_split(self):
        bands = banded.band_partition(328)
        per_band = REAL_HEADER_POINTS // len(bands)
        for index, (_r0, r1) in enumerate(bands):
            count = per_band + (REAL_HEADER_POINTS - per_band * len(bands) if index == len(bands) - 1 else 0)
            ok, peak, _lines = worker.candidate_live_set_plan(
                count, 2_000_000, 328 * r1,
                REAL_SPAN_M + 2 * MARGIN, REAL_SPAN_M + 2 * MARGIN, FROZEN_BUDGET_BYTES)
            self.assertTrue(ok, (index, count, peak))
            self.assertLess(peak, FROZEN_BUDGET_BYTES)

    def test_pathological_single_band_overcount_refuses(self):
        ok, _peak, _lines = worker.candidate_live_set_plan(
            REAL_HEADER_POINTS, 2_000_000, 65 * 328,
            REAL_SPAN_M + 2 * MARGIN, 200.0 + 2 * MARGIN, FROZEN_BUDGET_BYTES)
        self.assertFalse(ok)

    def test_plan_table_lines_match_representative_numbers(self):
        _ok, _peak, lines = worker.candidate_live_set_plan(
            5_000_000, 2_000_000, 1000, 1006.0, 206.0, FROZEN_BUDGET_BYTES)
        for expected in (
            "candidate prealloc x float32 + y float32 + cls uint8 + z float64 = 17 B x 5000000 = 85000000 B",
            "base chunk workspace tally 128 B x 2000000 = 256000000 B",
            "candidate chunk extras 43 B x 2000000 = 86000000 B",
            "candidate row object lists 1000 cells x 810 B = 810000 B",
        ):
            self.assertTrue(any(expected in line for line in lines), expected)


def _dumps(value):
    return json.dumps(value, sort_keys=True, allow_nan=False)


@unittest.skipUnless(HAS_LASPY, "requires the owned venv laspy/numpy (stdlib CI skips truthfully)")
class RealLaspyBandedTests(unittest.TestCase):
    """Real laspy and numpy: banded candidates versus the accepted candidate path on
    synthetic LAS tiles. Banded output must match the accepted output byte for byte."""

    BUDGET = FROZEN_BUDGET_BYTES
    CHUNK = 3000
    SPACING = groundtruth.LATTICE

    def setUp(self):
        import laspy
        import numpy
        self.laspy = laspy
        self.numpy = numpy
        parent = "/tmp/opencode" if os.path.isdir("/tmp/opencode") else None
        context = tempfile.TemporaryDirectory(prefix="sedan-banded-", dir=parent)
        self.addCleanup(context.cleanup)
        self.root = Path(context.name)

    def write_las(self, xs, ys, zs, classes, name="tile.las"):
        numpy = self.numpy
        las = self.laspy.create(file_version="1.4", point_format=6)
        las.header.scales = [0.001, 0.001, 0.001]
        las.header.offsets = [840000.0, 6957000.0, 100.0]
        las.x = numpy.round(numpy.asarray(xs, dtype=numpy.float64), 3)
        las.y = numpy.round(numpy.asarray(ys, dtype=numpy.float64), 3)
        las.z = numpy.round(numpy.asarray(zs, dtype=numpy.float64), 3)
        las.classification = numpy.asarray(classes, dtype=numpy.int64)
        path = self.root / name
        las.write(str(path))
        return path

    def scene(self, seed, origin, extent, count, cls_choices=(2, 2, 2, 2, 3, 4, 5, 6, 9, 17)):
        rng = self.numpy.random.default_rng(seed)
        xs = origin[0] + rng.uniform(0.0, extent[0], count)
        ys = origin[1] + rng.uniform(0.0, extent[1], count)
        zs = 150.0 + 0.02 * (xs - origin[0]) + rng.normal(0.0, 0.05, count)
        classes = rng.choice(list(cls_choices), size=count)
        return xs, ys, zs, classes

    def request(self, las_path, anchor, cols, rows, xs, ys, name="request.json"):
        footprint = {
            "x_min": float(self.numpy.round(xs.min(), 3)), "y_min": float(self.numpy.round(ys.min(), 3)),
            "x_max": float(self.numpy.round(xs.max(), 3)), "y_max": float(self.numpy.round(ys.max(), 3)),
        }
        return {
            "request_version": 1,
            "tile_path": str(las_path),
            "aoi_bbox_l93": {
                "x_min": anchor[0], "y_min": anchor[1],
                "x_max": anchor[0] + cols * self.SPACING, "y_max": anchor[1] + rows * self.SPACING,
            },
            "filter_bbox_l93": {
                "x_min": footprint["x_min"] - 10.0, "y_min": footprint["y_min"] - 10.0,
                "x_max": footprint["x_max"] + 10.0, "y_max": footprint["y_max"] + 10.0,
            },
            "cell_size_m": 10.0,
            "chunk_size_points": self.CHUNK,
            "memory_budget_bytes": self.BUDGET,
            "candidates": {
                "lattice_origin_l93": [anchor[0], anchor[1]],
                "aoi_sw_l93": [anchor[0], anchor[1]],
                "lattice_cols": cols,
                "lattice_rows": rows,
                "spacing_m": self.SPACING,
                "window_r": groundtruth.WINDOW_R,
                "e23_radius": groundtruth.E23_DIST,
                "footprint_l93": footprint,
            },
            "output_path": str(self.root / name.replace(".json", "-out.json")),
        }

    def assert_identical(self, request, band_rows_list=(None,)):
        accepted = worker.measure(dict(request), self.laspy, self.numpy)
        base_request = {key: value for key, value in request.items() if key != "candidates"}
        base = worker.measure(base_request, self.laspy, self.numpy)
        self.assertEqual(
            _dumps({k: v for k, v in accepted.items() if k != "candidates"}), _dumps(base))
        cfg = worker.validate_candidates_request(request["candidates"])
        for rows in band_rows_list:
            with contextlib.redirect_stderr(io.StringIO()):
                block = banded.build_banded_block(
                    request["tile_path"], self.laspy, self.numpy, request, cfg, base["header"],
                    band_rows=rows or banded.BAND_ROWS)
            self.assertEqual(_dumps(block), _dumps(accepted["candidates"]), f"band_rows={rows}")
        return accepted["candidates"]

    def arrays_by_cell(self, block):
        arrays = block["arrays"]
        return {
            (row, col): {key: arrays[key][index] for key in arrays if key not in ("col", "row")}
            for index, (col, row) in enumerate(zip(arrays["col"], arrays["row"]))
        }

    def test_mixed_classes_full_lattice_identical_across_band_heights(self):
        origin = (840000.0, 6957000.0)
        xs, ys, zs, classes = self.scene(1, origin, (60.0, 60.0), 8000)
        las = self.write_las(xs, ys, zs, classes)
        request = self.request(las, (origin[0] + 0.7, origin[1] + 0.3), 18, 18, xs, ys)
        block = self.assert_identical(request, band_rows_list=(None, 1, 2, 3, 5))
        self.assertGreater(block["cells_contained"], 100)
        self.assertGreater(block["points_retained"], 0)

    def test_partial_footprint_distant_anchor_identical(self):
        origin = (839980.0, 6957050.0)
        xs, ys, zs, classes = self.scene(2, origin, (60.0, 60.0), 8000)
        las = self.write_las(xs, ys, zs, classes)
        anchor = (839974.454115, 6957066.765093)
        request = self.request(las, anchor, 24, 24, xs, ys)
        block = self.assert_identical(request, band_rows_list=(None, 1, 3, 7))
        self.assertLess(block["cells_contained"], block["cells_enumerated"])

    def test_dense_and_empty_bands_identical(self):
        origin = (840000.0, 6957000.0)
        xs, ys, zs, classes = self.scene(3, origin, (60.0, 60.0), 6000)
        keep = ys < origin[1] + 20.0
        xs = self.numpy.concatenate([xs[keep], xs[keep][:800] * 0 + 840010.0])
        ys = self.numpy.concatenate([ys[keep], ys[keep][:800] * 0 + 6957010.0])
        zs = self.numpy.concatenate([zs[keep], zs[keep][:800] * 0 + 150.0])
        classes = self.numpy.concatenate([classes[keep], self.numpy.full(800, 2)])
        las = self.write_las(xs, ys, zs, classes)
        request = self.request(las, (origin[0] + 0.7, origin[1] + 0.3), 18, 18, xs, ys)
        self.assert_identical(request, band_rows_list=(None, 2, 3))

    def test_degenerate_collinear_ground_identical_with_fit_unavailable(self):
        origin = (840000.0, 6957000.0)
        count = 1500
        xs = origin[0] + self.numpy.linspace(0.0, 60.0, count)
        ys = self.numpy.full(count, origin[1] + 30.0)
        zs = self.numpy.full(count, 150.0)
        classes = self.numpy.full(count, 2)
        xs = self.numpy.concatenate([xs, [origin[0], origin[0] + 60.0]])
        ys = self.numpy.concatenate([ys, [origin[1], origin[1] + 60.0]])
        zs = self.numpy.concatenate([zs, [150.0, 150.0]])
        classes = self.numpy.concatenate([classes, [6, 6]])
        las = self.write_las(xs, ys, zs, classes)
        request = self.request(las, (origin[0] + 0.7, origin[1] + 0.3), 18, 18, xs, ys)
        block = self.assert_identical(request, band_rows_list=(None, 2, 3))
        self.assertTrue(any(block["arrays"]["fit_unavailable"]))

    def test_halo_near_exact_distances_and_far_class9_none(self):
        origin = (840000.0, 6957000.0)
        xs, ys, zs, classes = self.scene(4, origin, (60.0, 60.0), 6000, cls_choices=(2,))
        anchor = (origin[0] + 0.7, origin[1] + 0.3)
        near_cell = (4, 4)
        far_cell = (14, 14)
        near_cx = anchor[0] + (near_cell[1] + 0.5) * self.SPACING
        near_cy = anchor[1] + (near_cell[0] + 0.5) * self.SPACING
        far_cx = anchor[0] + (far_cell[1] + 0.5) * self.SPACING
        far_cy = anchor[1] + (far_cell[0] + 0.5) * self.SPACING
        xs = self.numpy.concatenate([xs, [near_cx + 2.0, far_cx + 4.0]])
        ys = self.numpy.concatenate([ys, [near_cy, far_cy]])
        zs = self.numpy.concatenate([zs, [150.0, 150.0]])
        classes = self.numpy.concatenate([classes, [9, 9]])
        las = self.write_las(xs, ys, zs, classes)
        request = self.request(las, anchor, 18, 18, xs, ys)
        block = self.assert_identical(request, band_rows_list=(None, 1, 2, 3))
        cells = self.arrays_by_cell(block)
        self.assertAlmostEqual(cells[near_cell]["e2_dist"], 2.0, delta=1e-3)
        self.assertIsNone(cells[far_cell]["e2_dist"])

    def test_edge_straddling_class9_at_3m_identical_and_exact(self):
        origin = (840000.0, 6957000.0)
        xs, ys, zs, classes = self.scene(5, origin, (60.0, 60.0), 6000, cls_choices=(2,))
        anchor = (origin[0] + 0.7, origin[1] + 0.3)
        cell = (6, 5)
        cx = anchor[0] + (cell[1] + 0.5) * self.SPACING
        cy = anchor[1] + (cell[0] + 0.5) * self.SPACING
        xs = self.numpy.concatenate([xs, [cx, cx + 0.5]])
        ys = self.numpy.concatenate([ys, [cy - 3.0, cy + 2.5]])
        zs = self.numpy.concatenate([zs, [150.0, 150.0]])
        classes = self.numpy.concatenate([classes, [9, 17]])
        las = self.write_las(xs, ys, zs, classes)
        request = self.request(las, anchor, 18, 18, xs, ys)
        block = self.assert_identical(request, band_rows_list=(1, 2, 3, 4))
        cells = self.arrays_by_cell(block)
        self.assertAlmostEqual(cells[cell]["e2_dist"], 3.0, delta=1e-3)
        self.assertLessEqual(cells[cell]["e2_dist"], groundtruth.E23_DIST)

    def test_row_ownership_unique_and_row_major(self):
        origin = (840000.0, 6957000.0)
        xs, ys, zs, classes = self.scene(6, origin, (60.0, 60.0), 6000)
        las = self.write_las(xs, ys, zs, classes)
        request = self.request(las, (origin[0] + 0.7, origin[1] + 0.3), 18, 18, xs, ys)
        block = self.assert_identical(request, band_rows_list=(2,))
        pairs = list(zip(block["arrays"]["row"], block["arrays"]["col"]))
        self.assertEqual(len(pairs), len(set(pairs)))
        self.assertEqual(pairs, sorted(pairs))
        self.assertEqual(len(pairs), block["cells_contained"])

    def test_end_to_end_main_scientific_identity_with_explicit_provenance(self):
        origin = (840000.0, 6957000.0)
        xs, ys, zs, classes = self.scene(7, origin, (60.0, 60.0), 8000)
        las = self.write_las(xs, ys, zs, classes)
        request = self.request(las, (origin[0] + 0.7, origin[1] + 0.3), 18, 18, xs, ys)
        accepted_request = dict(request, output_path=str(self.root / "accepted.json"))
        accepted_path = self.root / "accepted-request.json"
        accepted_path.write_text(json.dumps(accepted_request), encoding="utf-8")
        banded_request = dict(request, output_path=str(self.root / "banded.json"))
        banded_path = self.root / "banded-request.json"
        banded_path.write_text(json.dumps(banded_request), encoding="utf-8")
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(worker.main(["sedan_lidar_probe_worker.py", str(accepted_path)]), 0)
            self.assertEqual(banded.main(["sedan_lidar_probe_worker_banded.py", str(banded_path)]), 0)
        accepted_bytes = (self.root / "accepted.json").read_bytes()
        banded_bytes = (self.root / "banded.json").read_bytes()
        accepted = json.loads(accepted_bytes)
        banded_out = json.loads(banded_bytes)

        for key in PROVENANCE_KEYS:
            self.assertIn(key, banded_out)
            self.assertNotIn(key, accepted)
        self.assertNotEqual(banded_bytes, accepted_bytes)
        self.assertEqual(banded_out["executed_entrypoint_path"], os.path.abspath(banded.__file__))
        self.assertEqual(
            banded_out["executed_entrypoint_sha256"], _sha256_bytes(Path(banded.__file__).read_bytes()))
        self.assertEqual(banded_out["base_measure_module_path"], os.path.abspath(worker.__file__))
        self.assertEqual(banded_out["base_measure_module_sha256"], ACCEPTED_WORKER_SHA256)
        self.assertEqual(
            banded_out["base_measure_module_sha256"],
            _sha256_bytes(Path(worker.__file__).read_bytes()))
        self.assertEqual(banded_out["select_module_sha256"], ACCEPTED_SELECT_SHA256)
        self.assertEqual(
            banded_out["select_module_sha256"],
            _sha256_bytes(Path(groundtruth.__file__).read_bytes()))

        self.assertEqual(banded_out["measurement_version"], accepted["measurement_version"])
        self.assertEqual(
            banded_out["candidates"]["candidate_measurement_version"],
            accepted["candidates"]["candidate_measurement_version"])
        self.assertEqual(accepted["measurement_version"], 1)
        self.assertEqual(accepted["candidates"]["candidate_measurement_version"], 1)

        scientific = {key: value for key, value in banded_out.items() if key not in PROVENANCE_KEYS}
        self.assertEqual(
            json.dumps(scientific, indent=2, sort_keys=True, allow_nan=False) + "\n",
            accepted_bytes.decode("utf-8"))
        self.assertEqual(_dumps(banded_out["candidates"]), _dumps(accepted["candidates"]))

    def test_pinned_module_mismatch_fails_closed_with_no_output(self):
        origin = (840000.0, 6957000.0)
        xs, ys, zs, classes = self.scene(12, origin, (60.0, 60.0), 6000)
        las = self.write_las(xs, ys, zs, classes)
        request = self.request(las, (origin[0] + 0.7, origin[1] + 0.3), 18, 18, xs, ys)
        path = self.root / "mismatch.json"
        path.write_text(json.dumps(request), encoding="utf-8")
        for pin in ("EXPECTED_BASE_MODULE_SHA256", "EXPECTED_SELECT_MODULE_SHA256"):
            with self.subTest(pin=pin):
                Path(request["output_path"]).unlink(missing_ok=True)
                with mock.patch.object(banded, pin, "0" * 64):
                    with contextlib.redirect_stderr(io.StringIO()):
                        with self.assertRaises(SystemExit) as raised:
                            banded.main(["sedan_lidar_probe_worker_banded.py", str(path)])
                self.assertEqual(raised.exception.code, banded.EXIT_PROCESSING)
                self.assertFalse(Path(request["output_path"]).exists())

    def test_two_runs_are_byte_identical(self):
        origin = (840000.0, 6957000.0)
        xs, ys, zs, classes = self.scene(8, origin, (60.0, 60.0), 8000)
        las = self.write_las(xs, ys, zs, classes)
        outputs = []
        for name in ("run-a.json", "run-b.json"):
            request = self.request(las, (origin[0] + 0.7, origin[1] + 0.3), 18, 18, xs, ys, name=name)
            path = self.root / f"{name}-request.json"
            path.write_text(json.dumps(request), encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(banded.main(["sedan_lidar_probe_worker_banded.py", str(path)]), 0)
            outputs.append(Path(request["output_path"]).read_bytes())
        self.assertEqual(outputs[0], outputs[1])

    def test_exactly_n_plus_2_full_reads(self):
        origin = (840000.0, 6957000.0)
        xs, ys, zs, classes = self.scene(9, origin, (60.0, 60.0), 6000)
        las = self.write_las(xs, ys, zs, classes)
        request = self.request(las, (origin[0] + 0.7, origin[1] + 0.3), 18, 18, xs, ys)
        cfg = worker.validate_candidates_request(request["candidates"])
        reads = {"count": 0}
        real_laspy = self.laspy

        class CountingLaspy:
            def __getattr__(self, name):
                return getattr(real_laspy, name)

            def open(self, *args, **kwargs):
                reads["count"] += 1
                return real_laspy.open(*args, **kwargs)

        counting = CountingLaspy()
        base_request = {key: value for key, value in request.items() if key != "candidates"}
        base = worker.measure(base_request, counting, self.numpy)
        block = banded.build_banded_block(
            request["tile_path"], counting, self.numpy, request, cfg, base["header"], band_rows=3)
        bands = len(banded.band_partition(cfg["lattice_rows"], 3))
        self.assertEqual(reads["count"], bands + 2)
        self.assertEqual(_dumps(block), _dumps(worker.measure(dict(request), self.laspy, self.numpy)["candidates"]))

    def test_band_guard_refusal_fails_closed_without_shrink_or_retry(self):
        origin = (840000.0, 6957000.0)
        xs, ys, zs, classes = self.scene(10, origin, (60.0, 60.0), 6000)
        las = self.write_las(xs, ys, zs, classes)
        request = self.request(las, (origin[0] + 0.7, origin[1] + 0.3), 18, 18, xs, ys)
        cfg = worker.validate_candidates_request(request["candidates"])
        base_request = {key: value for key, value in request.items() if key != "candidates"}
        base = worker.measure(base_request, self.laspy, self.numpy)
        bands = banded.band_partition(cfg["lattice_rows"], 2)
        refused_cells = cfg["lattice_cols"] * bands[1][1]
        calls = []
        real_plan = worker.candidate_live_set_plan

        def plan(count, chunk, cells, span_x, span_y, budget):
            calls.append(cells)
            if cells == refused_cells:
                return False, budget + 1, ["forced refusal for the no-shrink test"]
            return real_plan(count, chunk, cells, span_x, span_y, budget)

        worker.candidate_live_set_plan = plan
        self.addCleanup(setattr, worker, "candidate_live_set_plan", real_plan)
        stderr = io.StringIO()
        with contextlib.redirect_stderr(stderr):
            with self.assertRaises(SystemExit) as raised:
                banded.build_banded_block(
                    request["tile_path"], self.laspy, self.numpy, request, cfg, base["header"],
                    band_rows=2)
        self.assertEqual(raised.exception.code, banded.EXIT_MEMORY)
        self.assertIn("band 1 rows [2, 4)", stderr.getvalue())
        self.assertNotIn("band 2", stderr.getvalue())
        self.assertTrue(all(cells <= refused_cells for cells in calls))
        self.assertEqual(calls.count(refused_cells), 1)

    def test_small_budget_refuses_with_no_output_file(self):
        origin = (840000.0, 6957000.0)
        xs, ys, zs, classes = self.scene(11, origin, (60.0, 60.0), 6000)
        las = self.write_las(xs, ys, zs, classes)
        request = self.request(las, (origin[0] + 0.7, origin[1] + 0.3), 18, 18, xs, ys)
        request["memory_budget_bytes"] = 40 << 20
        path = self.root / "small-budget.json"
        path.write_text(json.dumps(request), encoding="utf-8")
        with contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit) as raised:
                banded.main(["sedan_lidar_probe_worker_banded.py", str(path)])
        self.assertEqual(raised.exception.code, banded.EXIT_MEMORY)
        self.assertFalse(Path(request["output_path"]).exists())
