"""Synthetic analytic tests for scripts/geo/sedan_lidar_select.py.

Tests 1-26 are the validated prototype suite
(~/log/track-r/sedan-alternative-groundtruth-20261103T0900-0500/prototype/test_groundtruth_select.py)
ported to the canonical module API, method unchanged. The remaining tests cover the
new canonical geometry helpers (lattice enumeration, sectors, window containment,
E6 seam distances), the frozen THRESHOLDS table, and determinism.

Every assertion is an analytically derived expectation from the constructed scene;
tolerance 1e-9 unless the case states otherwise. Stdlib only, no RNG, no network.
Run: python3 -m unittest discover -s scripts/geo/tests -p 'test_sedan_lidar_select.py' -v
"""
import math
import os
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import sedan_lidar_select as g

# Clean evaluation site: far from seams (E 840000 / 841000, N 6958000) and AOI border.
CLEAN_UTM = (640350.0, 5507250.0)
CLEAN_L93 = (840400.0, 6957600.0)

OFFS21 = [(i, j) for i in (-2, -1, 0, 1, 2) for j in (-2, -1, 0, 1, 2) if i * i + j * j <= 6.25]
PATTERN21 = (0.35, -0.6, 0.2, -0.15, 0.5, -0.4, 0.1, -0.55, 0.3, -0.2,
             0.45, -0.35, 0.15, -0.5, 0.25, -0.1, 0.4, -0.45, 0.05, -0.3, 0.6)


def flat_cloud(ex, ey, amp=0.0):
    return [g.Pt(ex + i * 1.0, ey + j * 1.0, 150.0 + amp * PATTERN21[k % 21], 2)
            for k, (i, j) in enumerate(OFFS21)]


def plane_cloud(ex, ey, b=0.0, c=0.0):
    return [g.Pt(ex + i * 1.0, ey + j * 1.0, 150.0 + b * i + c * j, 2) for i, j in OFFS21]


def quad_cloud(ex, ey, d=0.0, e=0.0, f=0.0):
    return [g.Pt(ex + i * 1.0, ey + j * 1.0, 150.0 + d * i * i + e * i * j + f * j * j, 2)
            for i, j in OFFS21]


def plane_cell(row, col, b=0.0, c=0.0):
    """A tilted-plane cell at its own lattice position (selection scenes need real spacing)."""
    utm = g.lattice_centre(row, col)
    l93 = g.utm_to_l93_approx(*utm)
    return g.evaluate_candidate(plane_cloud(l93[0], l93[1], b, c), utm, l93, cell=(row, col))


def sparse_cell(row, col):
    utm = g.lattice_centre(row, col)
    l93 = g.utm_to_l93_approx(*utm)
    pts = [g.Pt(l93[0] + i, l93[1] + j, 150.0, 2)
           for i, j in ((-1, 0), (0, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, 1), (-1, 1), (1, -1))]
    return g.evaluate_candidate(pts, utm, l93, cell=(row, col))


def make_cell(row, col, points, centre_l93, utm=None):
    """Evaluate a cloud built around centre_l93 at lattice cell (row, col)."""
    if utm is None:
        utm = g.lattice_centre(row, col)
    return g.evaluate_candidate(points, utm, centre_l93, cell=(row, col))


def flat_cell(row, col, amp=0.0, extra_offsets=()):
    """A flat cell at its own lattice position; extra offsets are (dx, dy, z, cls)."""
    utm = g.lattice_centre(row, col)
    l93 = g.utm_to_l93_approx(*utm)
    pts = flat_cloud(l93[0], l93[1], amp)
    pts += [g.Pt(l93[0] + dx, l93[1] + dy, z, cls) for dx, dy, z, cls in extra_offsets]
    return g.evaluate_candidate(pts, utm, l93, cell=(row, col))


def mk(cid, row, col, sigma, sector, sector_dist, pool="S1H", slope=0.5):
    utm = g.lattice_centre(row, col)
    return {
        "id": cid, "row": row, "col": col,
        "utm_x": utm[0], "utm_y": utm[1], "l93_x": 0.0, "l93_y": 0.0,
        "n2": 21, "density": 21 / (math.pi * 2.5 * 2.5),
        "truth_z": 150.0, "sigma_fit": sigma, "slope_deg": slope, "curv_bound": 0.0,
        "n3": 0, "n4": 0, "n5": 0, "n6": 0, "n9": 0, "n17": 0,
        "d9": None, "d17": None, "e6_dist": 100.0,
        "sector": sector, "sector_dist": sector_dist,
        "stratum": "S1" if pool == "S1H" else pool,
        "pool": pool, "eligible": True, "fired": [], "rank": None,
    }


class GeometryTests(unittest.TestCase):
    """Per-cell computation and exclusion semantics (port of prototype tests 1-15)."""

    def test_constant_plane(self):
        rec = make_cell(100, 100, flat_cloud(*CLEAN_L93), CLEAN_L93, utm=CLEAN_UTM)
        self.assertAlmostEqual(rec["truth_z"], 150.0, delta=1e-9)
        self.assertLess(rec["sigma_fit"], 1e-9)
        self.assertLess(rec["slope_deg"], 1e-6)
        self.assertLess(rec["curv_bound"], 1e-9)
        self.assertTrue(rec["eligible"])
        self.assertEqual(rec["fired"], [])
        self.assertEqual(rec["n2"], 21)

    def test_tilted_plane_median_bias(self):
        # One-sided window: u in {0.5, 1.0, 1.5, 2.0} x v in {-1, 0, 1} plus (2.4, 0).
        spec = [(0.5, -1), (0.5, 0), (0.5, 1), (1.0, -1), (1.0, 0), (1.0, 1),
                (1.5, -1), (1.5, 0), (1.5, 1), (2.0, -1), (2.0, 0), (2.0, 1), (2.4, 0)]
        pts = [g.Pt(CLEAN_L93[0] + u, CLEAN_L93[1] + v, 150.0 + 0.1 * u + 0.05 * v, 2)
               for u, v in spec]
        rec = make_cell(100, 100, pts, CLEAN_L93, utm=CLEAN_UTM)
        # Plane evaluated AT the coordinate reproduces 150 exactly (median does not).
        self.assertAlmostEqual(rec["truth_z"], 150.0, delta=1e-9)
        med, _ = g.median_mad([p.z for p in pts])
        # median z = 150 + 0.1 x median(u) + 0.05 x median(v); median u = 1.5, median v = 0.
        self.assertAlmostEqual(med, 150.15, delta=1e-9)
        self.assertAlmostEqual(rec["sigma_fit"], 0.0, delta=1e-9)
        self.assertTrue(rec["eligible"])

    def test_outliers_in_window_floor(self):
        # 48 exact inliers at 150 plus 2 duplicate-centroid outliers at +5 m.
        raw = set()
        for i in range(-5, 6):
            for j in range(-5, 6):
                u, v = i * 0.5, j * 0.5
                if u * u + v * v <= 6.25:
                    raw.add((u, v))
        offs = sorted(raw)[:48]
        self.assertEqual(len(offs), 48)
        uc = sum(u for u, _ in offs) / 48.0
        vc = sum(v for _, v in offs) / 48.0
        pts = [g.Pt(u, v, 150.0, 2) for u, v in offs]
        pts += [g.Pt(uc, vc, 155.0, 2), g.Pt(uc, vc, 155.0, 2)]
        fit = g.fit_plane(pts, (0.0, 0.0))
        # Dictated bound 1e-6 is unreachable in exactly two passes: outliers at the
        # inlier centroid give an exactly constant pull a0 = 10/50 = 0.2 and the floored
        # scale makes weights proportional to 1/|r|, so each pass shrinks the offset by
        # |r_in|/|r_out|: a1 = 5*0.2/(24*4.8) = 8.669e-3, a2 ~ 3.6e-4. Assert analytic.
        self.assertLessEqual(abs(fit["a"] - 150.0), 4e-4)
        self.assertGreater(abs(fit["a"] - 150.0), 1e-6)  # the deviation from 1e-6 is real
        self.assertLess(fit["weights"][-1], 1e-6)
        self.assertLess(fit["weights"][-2], 1e-6)
        self.assertLessEqual(fit["sigma_fit"], 1e-6)  # S_FLOOR: final MAD collapses to 0

    def test_outliers_outside_window(self):
        pts = flat_cloud(*CLEAN_L93)
        pts += [g.Pt(CLEAN_L93[0] + 3.5, CLEAN_L93[1], 155.0, 2),
                g.Pt(CLEAN_L93[1] * 0 + CLEAN_L93[0] - 3.5, CLEAN_L93[1] + 0.5, 155.0, 2)]
        rec = make_cell(100, 100, pts, CLEAN_L93, utm=CLEAN_UTM)
        self.assertEqual(rec["n2"], 21)
        self.assertAlmostEqual(rec["truth_z"], 150.0, delta=1e-9)
        self.assertLessEqual(rec["sigma_fit"], 1e-9)
        self.assertTrue(rec["eligible"])

    def test_curvature_pure_d(self):
        bound = g.curvature_bound_from_coeffs(0.01, 0.0, 0.0)
        self.assertAlmostEqual(bound, 0.01 * 1.524 ** 2, delta=1e-12)  # 0.02322576
        self.assertLess(bound, g.E7_CURV)
        ex, ey = CLEAN_L93
        cloud = quad_cloud(ex, ey, d=0.01)
        fit = g.fit_plane(cloud, (ex, ey))
        got, _ = g.curvature_bound(cloud, fit["weights"], (ex, ey))
        self.assertAlmostEqual(got, 0.02322576, delta=1e-9)

    def test_curvature_e7_fires(self):
        # 0.025 x 1.524^2 = 0.0580644 (dictation's 0.058038 is arithmetically inconsistent
        # with h = 1.524; corrected value used, plan unchanged).
        bound = g.curvature_bound_from_coeffs(0.025, 0.0, 0.0)
        self.assertAlmostEqual(bound, 0.0580644, delta=1e-9)
        self.assertGreater(bound, g.E7_CURV)
        rules = g.fired_rules(21, None, None, 0.0, 0.0, 100.0, bound)
        self.assertEqual([r for r, _ in rules], ["E7"])

    def test_curvature_saddle(self):
        bound = g.curvature_bound_from_coeffs(0.02, 0.0, -0.02)
        self.assertAlmostEqual(bound, 0.02 * 1.524 ** 2, delta=1e-12)  # 0.04645152
        h = 1.524
        corner = abs(0.02 * h * h + (-0.02) * h * h)
        self.assertEqual(corner, 0.0)  # all four corners are exactly 0
        self.assertGreater(bound, corner)  # edge-interior vertex (h, 0) is provably necessary
        self.assertLess(bound, g.E7_CURV)
        ex, ey = CLEAN_L93
        cloud = quad_cloud(ex, ey, d=0.02, f=-0.02)
        fit = g.fit_plane(cloud, (ex, ey))
        got, _ = g.curvature_bound(cloud, fit["weights"], (ex, ey))
        self.assertAlmostEqual(got, 0.04645152, delta=1e-9)

    def test_curvature_mixed_edge_vertex(self):
        # q = 0.01 u^2 + 0.015 u v - 0.01 v^2: the edge-interior vertices at (+-0.75h)
        # strictly dominate all four corners.
        d, e, f = 0.01, 0.015, -0.01
        bound = g.curvature_bound_from_coeffs(d, e, f)
        self.assertAlmostEqual(bound, 0.015625 * 1.524 ** 2, delta=1e-12)  # 0.03629025
        corner_max = abs(d + e + f) * 1.524 ** 2
        self.assertAlmostEqual(corner_max, 0.03483864, delta=1e-9)
        self.assertGreater(bound, corner_max)
        ex, ey = CLEAN_L93
        cloud = quad_cloud(ex, ey, d=d, e=e, f=f)
        fit = g.fit_plane(cloud, (ex, ey))
        got, _ = g.curvature_bound(cloud, fit["weights"], (ex, ey))
        self.assertAlmostEqual(got, bound, delta=1e-9)

    def test_e1_low_count(self):
        pts = [g.Pt(CLEAN_L93[0] + i, CLEAN_L93[1] + j, 150.0, 2) for i, j in OFFS21[:11]]
        rec = make_cell(100, 100, pts, CLEAN_L93, utm=CLEAN_UTM)
        self.assertEqual(rec["n2"], 11)
        self.assertEqual(rec["fired"], [("E1", 11)])
        self.assertFalse(rec["eligible"])

    def test_e2_class9_distances(self):
        for dx, expected in ((3.0, True), (3.1, False)):
            rec = flat_cell(100, 100, extra_offsets=[(dx, 0.0, 149.5, 9)])
            fired = [r for r, _ in rec["fired"]]
            self.assertEqual("E2" in fired, expected, "dx=%s" % dx)
            if expected:
                self.assertAlmostEqual(dict(rec["fired"])["E2"], dx, delta=1e-9)
                self.assertFalse(rec["eligible"])
            else:
                self.assertTrue(rec["eligible"])
        # Exact 3.048 is NOT within (< is strict). Tested at rule level: at AOI-scale
        # coordinates (~8.4e5) a geometric dx of 3.048 carries ~1e-10 cancellation
        # noise, so the tie belongs to the stored-value comparison.
        self.assertEqual(g.fired_rules(21, 3.048, None, 0.0, 0.0, 100.0, 0.0), [])

    def test_e3_class17(self):
        rec = flat_cell(100, 100, extra_offsets=[(2.0, 0.0, 154.0, 17)])
        fired = dict(rec["fired"])
        self.assertIn("E3", fired)
        self.assertAlmostEqual(fired["E3"], 2.0, delta=1e-9)
        self.assertFalse(rec["eligible"])

    def test_e4_checkerboard(self):
        # 4x4 checkerboard is orthogonal to {1, u, v}: OLS recovers the plane exactly,
        # residuals are exactly +-0.1, sigma_fit = 1.4826 x 0.1 = 0.14826.
        offs = (-1.5, -0.5, 0.5, 1.5)
        pts = []
        for a, i in enumerate(offs):
            for b, j in enumerate(offs):
                s = 1.0 if (a + b) % 2 == 0 else -1.0
                pts.append(g.Pt(CLEAN_L93[0] + i, CLEAN_L93[1] + j, 150.0 + 0.1 * s, 2))
        rec = make_cell(100, 100, pts, CLEAN_L93, utm=CLEAN_UTM)
        self.assertAlmostEqual(rec["sigma_fit"], 0.14826, delta=1e-9)
        self.assertEqual([r for r, _ in rec["fired"]], ["E4"])
        self.assertAlmostEqual(dict(rec["fired"])["E4"], 0.14826, delta=1e-9)
        self.assertFalse(rec["eligible"])
        self.assertLess(rec["slope_deg"], 4.0)

    def test_e5_slopes(self):
        for deg, expected in ((15.0, True), (10.0, False)):
            b = math.tan(math.radians(deg))
            rec = make_cell(100, 100, plane_cloud(*CLEAN_L93, b=b), CLEAN_L93, utm=CLEAN_UTM)
            self.assertAlmostEqual(rec["slope_deg"], deg, delta=1e-9)
            fired = [r for r, _ in rec["fired"]]
            self.assertEqual("E5" in fired, expected)
            self.assertEqual(rec["eligible"], not expected)
            if expected:
                self.assertAlmostEqual(dict(rec["fired"])["E5"], deg, delta=1e-9)

    def test_e6_seam_and_border(self):
        cases = [
            ((840001.5, 6957616.498), True, 1.5),    # L93 seam E 840000
            ((840003.5, 6957616.498), False, None),  # 3.5 m: outside the band
            ((839977.0, 6957616.498), True, 1.425),  # AOI border band (E min)
            ((839980.0, 6957616.498), False, None),
        ]
        for (x, y), should_fire, trig in cases:
            rec = make_cell(100, 100, flat_cloud(x, y), (x, y), utm=CLEAN_UTM)
            fired = dict(rec["fired"])
            self.assertEqual("E6" in fired, should_fire, "(%s, %s)" % (x, y))
            if should_fire:
                self.assertAlmostEqual(fired["E6"], trig, delta=1e-9)
                self.assertFalse(rec["eligible"])
            else:
                self.assertTrue(rec["eligible"])

    def test_boundary_semantics(self):
        # Exact threshold values are eligible (strict below/above semantics).
        self.assertEqual(g.fired_rules(12, None, None, 0.08, 12.0, 3.048, 0.05), [])
        self.assertEqual([r for r, _ in g.fired_rules(11, None, None, 0.08, 12.0, 100.0, 0.05)], ["E1"])
        self.assertEqual([r for r, _ in g.fired_rules(12, 3.0479999, None, 0.08, 12.0, 100.0, 0.05)], ["E2"])
        self.assertEqual([r for r, _ in g.fired_rules(12, None, 3.0479999, 0.08, 12.0, 100.0, 0.05)], ["E3"])
        self.assertEqual([r for r, _ in g.fired_rules(12, None, None, 0.0800001, 12.0, 100.0, 0.05)], ["E4"])
        self.assertEqual([r for r, _ in g.fired_rules(12, None, None, 0.08, 12.0001, 100.0, 0.05)], ["E5"])
        self.assertEqual([r for r, _ in g.fired_rules(12, None, None, 0.08, 12.0, 3.0479999, 0.05)], ["E6"])
        self.assertEqual([r for r, _ in g.fired_rules(12, None, None, 0.08, 12.0, 100.0, 0.0500001)], ["E7"])


def flat_scene(cells):
    return [flat_cell(row, col, amp) for row, col, amp in cells]


class SelectionTests(unittest.TestCase):
    """Floor, ladder, spacing, ties (port of prototype tests 16-23)."""

    def test_stop_no_eligible(self):
        with self.assertRaises(g.ProtocolStop) as ctx:
            g.select([])
        self.assertEqual(ctx.exception.reason, "eligible_below_floor")
        self.assertEqual(ctx.exception.details["eligible"], 0)

    def test_stop_four_eligible(self):
        cells = [(40, 40, 0.003), (180, 180, 0.0033), (320, 300, 0.0036), (250, 50, 0.0039)]
        with self.assertRaises(g.ProtocolStop) as ctx:
            g.select(flat_scene(cells))
        self.assertEqual(ctx.exception.reason, "eligible_below_floor")
        self.assertEqual(ctx.exception.details["eligible"], 4)
        self.assertEqual(ctx.exception.details["selected"], 4)

    def test_stop_three_eligible(self):
        cells = [(40, 40, 0.003), (180, 180, 0.0033), (320, 300, 0.0036)]
        with self.assertRaises(g.ProtocolStop) as ctx:
            g.select(flat_scene(cells))
        self.assertEqual(ctx.exception.reason, "eligible_below_floor")
        self.assertEqual(ctx.exception.details["eligible"], 3)

    def test_stop_two_sectors(self):
        cells = [(40, 40, 0.003), (80, 20, 0.0032), (100, 60, 0.0034),
                 (130, 130, 0.0036), (200, 200, 0.0038), (230, 240, 0.0040)]
        with self.assertRaises(g.ProtocolStop) as ctx:
            g.select(flat_scene(cells))
        self.assertEqual(ctx.exception.reason, "sectors_below_floor")
        self.assertEqual(ctx.exception.details["covered"], ["s00", "s11"])
        self.assertEqual(ctx.exception.details["selected"], 5)

    def test_ladder_s3_empty(self):
        cells = [(40, 40, 0.003), (80, 20, 0.0033), (180, 180, 0.0036), (320, 300, 0.0039)]
        cands = flat_scene(cells)
        tilt = math.tan(math.radians(5.0))
        # Both S2 cells are >= 100 m from every flat and from each other, so the
        # composition holds regardless of which one ranks first.
        cands.append(plane_cell(60, 90, b=tilt))
        cands.append(plane_cell(60, 200, b=tilt))
        sel = g.select(cands)
        self.assertEqual(len(sel.primaries), 5)
        self.assertEqual(sel.ladder, [("S1H", 4), ("S2", 1)])
        pools = [c["pool"] for c in sel.primaries]
        self.assertEqual(pools.count("S1H"), 4)
        self.assertEqual(pools.count("S2"), 1)
        self.assertGreaterEqual(len(sel.sectors_covered), 3)

    def test_ladder_s2_s3_empty(self):
        cells = [(40, 40, 0.003), (80, 20, 0.0033), (150, 9, 0.0036),
                 (180, 180, 0.0039), (320, 300, 0.0042)]
        sel = g.select(flat_scene(cells))
        self.assertEqual(len(sel.primaries), 5)
        self.assertEqual(sel.ladder, [("S1H", 5)])
        self.assertTrue(all(c["pool"] == "S1H" for c in sel.primaries))
        self.assertGreaterEqual(len(sel.sectors_covered), 3)

    def test_greedy_spacing(self):
        cells = [(5, 30, 0.002), (5, 51, 0.004), (5, 80, 0.006),
                 (135, 80, 0.008), (265, 30, 0.010), (265, 80, 0.012)]
        sel = g.select(flat_scene(cells))
        # B (r5c51) sits 21 x 3.048 = 64.008 m from A: skipped by spacing; C at
        # 152.4 m fills the slot instead.
        self.assertEqual([c["id"] for c in sel.primaries],
                         ["r5c30", "r5c80", "r135c80", "r265c30", "r265c80"])
        chosen = [c["id"] for c in sel.primaries + sel.alternates]
        self.assertNotIn("r5c51", chosen)
        self.assertEqual(sel.alternates_shortfall, 2)
        a_rec = sel.primaries[0]
        self.assertAlmostEqual(math.hypot(a_rec["utm_x"] - g.lattice_centre(5, 51)[0],
                                           a_rec["utm_y"] - g.lattice_centre(5, 51)[1]),
                               64.008, delta=1e-9)

    def test_tiebreak_row_then_col(self):
        # X (row 59, col 245) and Y (row 245, col 59) tie on sigma, n2 and sector
        # distance; row precedes column in the key, so X outranks Y (columns disagree).
        d0 = 2.2485
        recs = [
            mk("A", 40, 40, 0.001, "s00", 10.0),
            mk("B", 180, 180, 0.002, "s11", 10.0),
            mk("C", 290, 80, 0.003, "s20", 10.0),
            mk("D", 320, 300, 0.004, "s22", 10.0),
            mk("X", 59, 245, 0.010, "s02", d0),
            mk("Y", 245, 59, 0.010, "s20", d0),
        ]
        x_rec, y_rec = recs[4], recs[5]
        self.assertLess(g.rank_key(x_rec), g.rank_key(y_rec))
        self.assertGreater(math.hypot(x_rec["utm_x"] - y_rec["utm_x"],
                                      x_rec["utm_y"] - y_rec["utm_y"]), 100.0)
        sel = g.select(recs)
        self.assertEqual([c["id"] for c in sel.primaries], ["A", "B", "C", "D", "X"])
        self.assertEqual([c["id"] for c in sel.alternates], ["Y"])


class FreezeTests(unittest.TestCase):
    """Freeze determinism, permutation invariance, pre-DEM substitution
    (port of prototype tests 24-26)."""

    def _tempdir(self):
        d = tempfile.mkdtemp(prefix="gts-", dir="/tmp/opencode")
        self.addCleanup(shutil.rmtree, d, True)
        return d

    def _scene12(self):
        cands = [flat_cell(40, 40, 0.0030), flat_cell(80, 20, 0.0034),
                 flat_cell(180, 180, 0.0038), flat_cell(290, 80, 0.0042),
                 flat_cell(320, 300, 0.0046), flat_cell(150, 9, 0.0050)]
        tilt = math.tan(math.radians(5.0))
        cands.append(plane_cell(60, 90, b=tilt))
        cands.append(plane_cell(170, 190, b=tilt))
        tilt10 = math.tan(math.radians(10.0))
        cands.append(plane_cell(50, 300, c=tilt10))
        cands.append(plane_cell(300, 60, c=tilt10))
        cands.append(flat_cell(190, 170, 0.0044,
                               extra_offsets=[(2.0, 0.0, 149.5, 9), (0.0, -2.0, 149.5, 9)]))
        cands.append(sparse_cell(160, 160))
        return cands

    def test_permutation_determinism(self):
        cands = self._scene12()
        self.assertGreaterEqual(len([c for c in cands if c["eligible"]]), 9)
        orders = [list(cands), list(reversed(cands)), cands[7:] + cands[:7]]
        digests = []
        cand_bytes = []
        sel_bytes = []
        for i, order in enumerate(orders):
            sel = g.select(order)
            out = os.path.join(self._tempdir(), "run%d" % i)
            manifest = g.freeze(order, sel, out, note="permutation determinism check")
            with open(os.path.join(out, "candidates.tsv"), "rb") as fh:
                cand_bytes.append(fh.read())
            with open(os.path.join(out, "selection.tsv"), "rb") as fh:
                sel_bytes.append(fh.read())
            digests.append(g.canonical_digest(manifest))
        self.assertEqual(cand_bytes[0], cand_bytes[1])
        self.assertEqual(cand_bytes[0], cand_bytes[2])
        self.assertEqual(sel_bytes[0], sel_bytes[1])
        self.assertEqual(sel_bytes[0], sel_bytes[2])
        self.assertEqual(len(set(digests)), 1)

    def test_pre_dem_substitution(self):
        cells = [(40, 40, 0.0030), (80, 20, 0.0034), (150, 9, 0.0038),
                 (180, 180, 0.0042), (290, 80, 0.0046), (320, 300, 0.0050),
                 (250, 250, 0.0054)]
        sel = g.select(flat_scene(cells))
        self.assertEqual(len(sel.primaries), 5)
        self.assertEqual(len(sel.alternates), 2)
        self.assertEqual(sel.alternates_shortfall, 0)
        victim = sel.primaries[0]
        row, col = victim["row"], victim["col"]
        # Recompute the primary with degraded points so E4 fires (pre-DEM knowledge):
        # the orthogonal 4x4 checkerboard gives derived sigma_fit = 1.4826 x 0.1
        # = 0.14826 > 0.08 with a full-rank quadratic design.
        utm = g.lattice_centre(row, col)
        l93 = g.utm_to_l93_approx(*utm)
        pts = []
        for a, i in enumerate((-1.5, -0.5, 0.5, 1.5)):
            for b, j in enumerate((-1.5, -0.5, 0.5, 1.5)):
                s = 1.0 if (a + b) % 2 == 0 else -1.0
                pts.append(g.Pt(l93[0] + i, l93[1] + j, 150.0 + 0.1 * s, 2))
        rec = g.evaluate_candidate(pts, utm, l93, cell=(row, col))
        self.assertAlmostEqual(rec["sigma_fit"], 0.14826, delta=1e-9)
        self.assertEqual(rec["fired"], [("E4", rec["sigma_fit"])])
        self.assertFalse(rec["eligible"])
        victim_id = victim["id"]
        alt_id = sel.alternates[0]["id"]
        g.substitute_alternate(sel, victim_id)
        self.assertNotIn(victim_id, [c["id"] for c in sel.primaries])
        self.assertIn(alt_id, [c["id"] for c in sel.primaries])
        self.assertEqual(sel.substitutions, [{"removed": victim_id, "added": alt_id}])
        g.mark_post_dem(sel)
        with self.assertRaises(g.ProtocolStop) as ctx:
            g.substitute_alternate(sel, alt_id)
        self.assertEqual(ctx.exception.reason, "substitution_after_dem_open")

    def test_freeze_hash_stability(self):
        cands = self._scene12()
        sel1 = g.select(list(cands))
        out1 = self._tempdir()
        man1 = g.freeze(list(cands), sel1, out1, note="stability")
        sel2 = g.select(list(cands))
        out2 = self._tempdir()
        man2 = g.freeze(list(cands), sel2, out2, note="stability")
        with open(os.path.join(out1, "candidates.tsv"), "rb") as fh:
            c1 = fh.read()
        with open(os.path.join(out2, "candidates.tsv"), "rb") as fh:
            c2 = fh.read()
        with open(os.path.join(out1, "selection.tsv"), "rb") as fh:
            s1 = fh.read()
        with open(os.path.join(out2, "selection.tsv"), "rb") as fh:
            s2 = fh.read()
        self.assertEqual(c1, c2)
        self.assertEqual(s1, s2)
        self.assertEqual(man1["tables"], man2["tables"])
        self.assertEqual(g.canonical_digest(man1), g.canonical_digest(man2))


class CanonicalGeometryTests(unittest.TestCase):
    """New canonical helpers (v1.2): lattice enumeration, sectors, containment,
    E6 distances, THRESHOLDS, stage-1 preliminary refusal, determinism."""

    def _tempdir(self):
        d = tempfile.mkdtemp(prefix="gts-canon-", dir="/tmp/opencode")
        self.addCleanup(shutil.rmtree, d, True)
        return d

    def test_lattice_enumeration_counts_and_sw_anchor(self):
        cells = g.enumerate_lattice_cells()
        rows = sorted({cell[0] for cell in cells})
        cols = sorted({cell[1] for cell in cells})
        self.assertEqual((rows[0], rows[-1]), (1, 359))
        self.assertEqual((cols[0], cols[-1]), (1, 359))
        self.assertEqual(len(cells), 359 * 359)
        self.assertEqual(len(rows), 359)
        self.assertEqual(len(cols), 359)
        # First enumerated cell (row 1, col 1): centre at origin + 1.5 cells.
        first = g.lattice_centre(1, 1)
        ox, oy = g.DEFAULT_AOI.origin_utm
        self.assertAlmostEqual(first[0], ox + 1.5 * g.LATTICE, delta=1e-12)
        self.assertAlmostEqual(first[1], oy + 1.5 * g.LATTICE, delta=1e-12)
        self.assertAlmostEqual(first[0], 639841.997 + 1.5 * 3.048, delta=1e-12)
        self.assertAlmostEqual(first[1], 5506768.179 + 1.5 * 3.048, delta=1e-12)
        # Last cell (359, 359): centre inside the extent minus the one-cell margin.
        last = g.lattice_centre(359, 359)
        self.assertLessEqual(last[0], ox + 1100.0 - g.LATTICE + 1e-9)
        self.assertLessEqual(last[1], oy + 1100.0 - g.LATTICE + 1e-9)
        # Cells 0 and 360 exist in the full extent but are excluded by the margin.
        counts = g.full_extent_cell_counts()
        self.assertEqual(counts, (361, 361))
        self.assertNotIn((0, 0), cells)
        self.assertNotIn((360, 360), cells)
        self.assertIn((359, 359), cells)
        # Row-major ordering.
        self.assertEqual(cells[0], (1, 1))
        self.assertEqual(cells[1], (1, 2))
        self.assertEqual(cells[359], (2, 1))

    def test_sector_mapping_non_overlap_and_full_coverage(self):
        aoi = g.DEFAULT_AOI
        ox, oy = aoi.origin_utm
        third = aoi.size / 3.0
        # Partition check over a fine sample grid: each point maps to exactly one
        # sector and every sector cell of the 3x3 grid is reached.
        seen = set()
        steps = 61
        for i in range(steps):
            for j in range(steps):
                x = ox + aoi.size * i / (steps - 1)
                y = oy + aoi.size * j / (steps - 1)
                sr, sc = g.sector_of(x, y)
                self.assertTrue(0 <= sr <= 2)
                self.assertTrue(0 <= sc <= 2)
                seen.add((sr, sc))
        self.assertEqual(seen, {(r, c) for r in range(3) for c in range(3)})
        # Boundary: x beyond the first third boundary moves to sector column 1
        # (a fine offset, since the exact third sits on a float rounding edge).
        self.assertEqual(g.sector_of(ox + third * 1.001, oy + 1.0), (0, 1))
        self.assertEqual(g.sector_of(ox + third * 2.001, oy + 1.0), (0, 2))
        # Far edge clamps to sector 2.
        self.assertEqual(g.sector_of(ox + aoi.size, oy + aoi.size), (2, 2))
        # Centroids sit at the sector centres.
        cx, cy = g.sector_centroid(1, 2)
        self.assertAlmostEqual(cx, ox + 2.5 * third, delta=1e-12)
        self.assertAlmostEqual(cy, oy + 1.5 * third, delta=1e-12)
        self.assertEqual(g.sector_name(2, 1), "s21")

    def test_window_containment_tangent_edge_cases(self):
        fp = {"x_min": 0.0, "y_min": 0.0, "x_max": 10.0, "y_max": 10.0}
        r = 2.5
        self.assertTrue(g.window_inside_footprint(5.0, 5.0, r, fp))
        # Tangent from inside: circle touches x_min / x_max exactly and is contained.
        self.assertTrue(g.window_inside_footprint(2.5, 5.0, r, fp))
        self.assertTrue(g.window_inside_footprint(7.5, 5.0, r, fp))
        self.assertTrue(g.window_inside_footprint(5.0, 2.5, r, fp))
        self.assertTrue(g.window_inside_footprint(5.0, 7.5, r, fp))
        self.assertTrue(g.window_inside_footprint(2.5, 2.5, r, fp))  # corner tangent
        # One epsilon outside any edge crosses the boundary and is refused.
        self.assertFalse(g.window_inside_footprint(2.5 - 1e-9, 5.0, r, fp))
        self.assertFalse(g.window_inside_footprint(7.5 + 1e-9, 5.0, r, fp))
        self.assertFalse(g.window_inside_footprint(5.0, 2.4, r, fp))
        self.assertFalse(g.window_inside_footprint(5.0, 7.6, r, fp))
        self.assertFalse(g.window_inside_footprint(12.0, 5.0, r, fp))
        self.assertFalse(g.window_inside_footprint(-1.0, -1.0, r, fp))

    def test_e6_seam_distances(self):
        bbox = g.DEFAULT_AOI.bbox_l93
        self.assertAlmostEqual(g.seam_border_distance(840001.5, 6957500.0, bbox), 1.5, delta=1e-9)
        self.assertAlmostEqual(g.seam_border_distance(840998.5, 6957500.0, bbox), 1.5, delta=1e-9)
        self.assertAlmostEqual(g.seam_border_distance(840500.0, 6958001.5, bbox), 1.5, delta=1e-9)
        self.assertAlmostEqual(g.seam_border_distance(840003.5, 6957500.0, bbox), 3.5, delta=1e-9)
        self.assertAlmostEqual(g.seam_border_distance(841001.5, 6957500.0, bbox), 1.5, delta=1e-9)
        # Border distances come from the AOI bbox edges (canonical square).
        self.assertAlmostEqual(g.seam_border_distance(839977.0, 6957500.0, bbox), 1.425, delta=1e-9)
        self.assertAlmostEqual(g.seam_border_distance(841074.575, 6957500.0, bbox), 1.0, delta=1e-9)
        y_near_max = bbox["y_max"] - 2.0
        self.assertAlmostEqual(g.seam_border_distance(840500.0, y_near_max, bbox), 2.0, delta=1e-9)
        # Interior point: minimum over all six constraints is large and exact.
        d = g.seam_border_distance(840500.0, 6957500.0, bbox)
        expected = min(500.0, 1000.0, 500.0 - 0.0,
                       840500.0 - bbox["x_min"], bbox["x_max"] - 840500.0,
                       6957500.0 - bbox["y_min"], bbox["y_max"] - 6957500.0)
        self.assertAlmostEqual(d, expected, delta=1e-12)

    def test_thresholds_byte_equality_with_protocol_values(self):
        self.assertEqual(
            dict(g.THRESHOLDS),
            {
                "window_r_m": 2.5,
                "half_footprint_m": 1.524,
                "lattice_spacing_m": 3.048,
                "e1_n2_min": 12,
                "e2_e3_radius_m": 3.048,
                "e4_sigma_fit_max_m": 0.08,
                "e5_slope_max_deg": 12.0,
                "e6_seam_border_m": 3.048,
                "e7_curvature_max_m": 0.05,
                "huber_constant": 1.345,
                "mad_scale": 1.4826,
                "huber_scale_floor_m": 1e-9,
                "s1_hard_sigma_max_m": 0.03,
                "stratum_s1_max_deg": 4.0,
                "stratum_s2_max_deg": 8.0,
                "min_primaries": 5,
                "min_sectors": 3,
                "min_pairwise_spacing_m": 100.0,
                "n_alternates": 2,
                "ladder_preferred": [["S1H", 3], ["S2", 1], ["S3", 1]],
                "ladder_s3_empty": [["S1H", 4], ["S2", 1]],
                "ladder_s2_empty": [["S1H", 4], ["S3", 1]],
                "ladder_all_empty": [["S1H", 5]],
                "seam_lines_l93": [["E", 840000.0], ["E", 841000.0], ["N", 6958000.0]],
                "aoi_centre_utm": [640391.997, 5507318.179],
                "aoi_half_m": 550.0,
                "lattice_origin_utm": [639841.997, 5506768.179],
                "one_cell_margin": 1,
                "exclusion_semantics": "strict: below/above/within; at-threshold eligible",
            },
        )
        # Module constants agree with the frozen table (single source of truth).
        self.assertEqual(g.WINDOW_R, g.THRESHOLDS["window_r_m"])
        self.assertEqual(g.E1_N2, g.THRESHOLDS["e1_n2_min"])
        self.assertEqual(g.MIN_SPACING, g.THRESHOLDS["min_pairwise_spacing_m"])
        self.assertEqual(g.SEAM_LINES_L93,
                         tuple((axis, value) for axis, value in g.THRESHOLDS["seam_lines_l93"]))

    def test_stage1_preliminary_never_final_freeze(self):
        cands = flat_scene([(40, 40, 0.003), (80, 20, 0.0033), (180, 180, 0.0036),
                            (290, 80, 0.0039), (320, 300, 0.0042)])
        sel_final = g.select(cands, stage_1_preliminary=False)
        self.assertFalse(sel_final.stage_1_preliminary)
        out = os.path.join(self._tempdir(), "final")
        manifest = g.freeze(list(cands), sel_final, out, note="final path")
        self.assertTrue(os.path.isfile(os.path.join(out, "frozen-manifest.json")))
        self.assertEqual(manifest["scope"], "final all-tiles selection freeze")
        sel_pre = g.select(list(cands), stage_1_preliminary=True)
        self.assertTrue(sel_pre.stage_1_preliminary)
        with self.assertRaises(g.ProtocolStop) as ctx:
            g.freeze(list(cands), sel_pre, os.path.join(self._tempdir(), "refused"))
        self.assertEqual(ctx.exception.reason, "stage1_preliminary_never_final")
        self.assertFalse(os.path.exists(os.path.join(self._tempdir(), "refused")))

    def test_determinism_same_input_identical_tables_and_digest(self):
        cands = self._scene12_shared()
        sel1 = g.select([dict(c) for c in cands], stage_1_preliminary=True)
        sel2 = g.select([dict(c) for c in cands], stage_1_preliminary=True)
        table1 = g.candidates_table_bytes(cands)
        table2 = g.candidates_table_bytes(cands)
        self.assertEqual(table1, table2)
        sel_bytes1 = g.selection_table_bytes(sel1)
        sel_bytes2 = g.selection_table_bytes(sel2)
        self.assertEqual(sel_bytes1, sel_bytes2)
        digest1 = g.canonical_digest({
            "tables": {
                "candidates_sha256": __import__("hashlib").sha256(table1).hexdigest(),
                "selection_sha256": __import__("hashlib").sha256(sel_bytes1).hexdigest(),
            },
            "selection": {
                "primary_ids": [c["id"] for c in sorted(sel1.primaries, key=lambda c: c["rank"])],
                "alternate_ids": [c["id"] for c in sorted(sel1.alternates, key=lambda c: c["rank"])],
                "sectors_covered": list(sel1.sectors_covered),
                "ladder": [[p, n] for p, n in sel1.ladder],
            },
        })
        digest2 = g.canonical_digest({
            "tables": {
                "candidates_sha256": __import__("hashlib").sha256(table2).hexdigest(),
                "selection_sha256": __import__("hashlib").sha256(sel_bytes2).hexdigest(),
            },
            "selection": {
                "primary_ids": [c["id"] for c in sorted(sel2.primaries, key=lambda c: c["rank"])],
                "alternate_ids": [c["id"] for c in sorted(sel2.alternates, key=lambda c: c["rank"])],
                "sectors_covered": list(sel2.sectors_covered),
                "ladder": [[p, n] for p, n in sel2.ladder],
            },
        })
        self.assertEqual(digest1, digest2)
        # Roles are distinct in the table: primary rows and alternate rows.
        roles = [line.split("\t")[1] for line in sel_bytes1.decode().splitlines()[1:]]
        self.assertEqual(roles.count("primary"), 5)
        self.assertEqual(roles.count("alternate"), len(sel1.alternates))

    def _scene12_shared(self):
        cands = [flat_cell(40, 40, 0.0030), flat_cell(180, 180, 0.0038),
                 flat_cell(320, 300, 0.0046), flat_cell(290, 80, 0.0042),
                 flat_cell(80, 20, 0.0034)]
        tilt = math.tan(math.radians(5.0))
        cands.append(plane_cell(60, 90, b=tilt))
        tilt10 = math.tan(math.radians(10.0))
        cands.append(plane_cell(50, 300, c=tilt10))
        return cands


if __name__ == "__main__":
    unittest.main()
