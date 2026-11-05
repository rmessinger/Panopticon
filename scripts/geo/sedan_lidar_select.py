#!/usr/bin/env python3
"""Canonical stage-1 selection machinery for the Sedan ground-truth protocol v1.2.

Pure-python port of the validated throwaway prototype
`~/log/track-r/sedan-alternative-groundtruth-20261103T0900-0500/prototype/groundtruth_select.py`
(26 synthetic analytic tests, record of the same directory; protocol sections 6.2-6.4
of the v1.1 plan, unchanged by the v1.2 amendment). Method is unchanged: median/MAD
with averaged central order statistics, OLS plane then EXACTLY two Huber passes
(scale s = max(1.4826 x MAD, 1e-9), constant 1.345, weights min(1, 1.345 s / |r|)),
sigma_fit = 1.4826 x MAD of the final residuals reported WITHOUT the floor, slope =
atan(|grad|) degrees, curvature bound from a weighted full quadratic fit over the
half-footprint square (4 corners + per-edge 1-D interior vertex, degenerate-safe),
exclusion rules E1-E7 with strict semantics (at-threshold eligible), slope strata
S1/S2/S3 with the S1-hard sub-stratum, total-order ranking, fallback ladder always
totaling 5 primaries, 100 m deterministic greedy spacing, 5-primary/3-sector floor
with STOP semantics, 2 alternates, and pre-DEM-only substitution.

SCOPE: stage-1 PRELIMINARY ground-availability selection only (protocol v1.2 stage-1
gate item d). Preliminary candidates are availability evidence and are NEVER the
final frozen selection: the final 5+2 selection runs later over ALL approved tiles
with the canonical pre-DEM freeze; freeze() below is that final-all-tiles API and it
REFUSES stage-1 preliminary selections.

New canonical geometry helpers (not in the prototype): lattice cell enumeration from
the SW anchor over the 1100 m extent with the one-cell edge margin, sector indexing
on the full-AOI 3 x 3 grid, window-fully-inside-footprint containment (circle vs
rectangle, L93), and the L93 seam/border distance helper for E6. The prototype's
utm_to_l93_approx stays ONLY as a synthetic-scene construction stand-in for tests;
the controller path converts coordinates with system cs2cs and never uses it.

Python standard library only; no network, no assets, no RNG.
"""

from __future__ import annotations

import hashlib
import json
import math
import os
import platform
from dataclasses import dataclass, field
from datetime import datetime
from types import MappingProxyType

PROTOCOL_VERSION = "1.2"

# Frozen protocol constants (v1.1 sections 6.2/6.3/6.4 values, unchanged by v1.2;
# v1.2 changes only the stage-1 gate d criterion and the gate c population).
WINDOW_R = 2.5
HALF_FOOTPRINT = 1.524          # 3.048 / 2
LATTICE = 3.048                 # metersPerGameUnit 0.3048 -> 10 game units per cell
E1_N2 = 12
E23_DIST = 3.048
E4_SIGMA = 0.08
E5_SLOPE_DEG = 12.0
E6_SEAM = 3.048
E7_CURV = 0.05
HUBER_C = 1.345
MAD_SCALE = 1.4826
S_FLOOR = 1e-9                  # frozen v1.1 constant (Huber scale floor)
S1_HARD_SIGMA = 0.03
S2_LOW_DEG = 4.0                # strata: S1 [0,4), S2 [4,8), S3 [8,12] degrees
S3_LOW_DEG = 8.0
MIN_PRIMARY = 5
MIN_SECTORS = 3
MIN_SPACING = 100.0
N_ALTERNATES = 2
SEAM_LINES_L93 = (("E", 840000.0), ("E", 841000.0), ("N", 6958000.0))
ONE_CELL_MARGIN = 1

# Frozen threshold table, asserted byte-equal to the protocol values by tests.
# Strict exclusion semantics: E1 below, E2/E3 within, E4/E5/E7 above, E6 within;
# a value exactly at a threshold is ELIGIBLE.
THRESHOLDS = MappingProxyType({
    "window_r_m": WINDOW_R,
    "half_footprint_m": HALF_FOOTPRINT,
    "lattice_spacing_m": LATTICE,
    "e1_n2_min": E1_N2,
    "e2_e3_radius_m": E23_DIST,
    "e4_sigma_fit_max_m": E4_SIGMA,
    "e5_slope_max_deg": E5_SLOPE_DEG,
    "e6_seam_border_m": E6_SEAM,
    "e7_curvature_max_m": E7_CURV,
    "huber_constant": HUBER_C,
    "mad_scale": MAD_SCALE,
    "huber_scale_floor_m": S_FLOOR,
    "s1_hard_sigma_max_m": S1_HARD_SIGMA,
    "stratum_s1_max_deg": S2_LOW_DEG,
    "stratum_s2_max_deg": S3_LOW_DEG,
    "min_primaries": MIN_PRIMARY,
    "min_sectors": MIN_SECTORS,
    "min_pairwise_spacing_m": MIN_SPACING,
    "n_alternates": N_ALTERNATES,
    "ladder_preferred": [["S1H", 3], ["S2", 1], ["S3", 1]],
    "ladder_s3_empty": [["S1H", 4], ["S2", 1]],
    "ladder_s2_empty": [["S1H", 4], ["S3", 1]],
    "ladder_all_empty": [["S1H", 5]],
    "seam_lines_l93": [list(seam) for seam in SEAM_LINES_L93],
    "aoi_centre_utm": [640391.997, 5507318.179],
    "aoi_half_m": 550.0,
    "lattice_origin_utm": [639841.997, 5506768.179],
    "one_cell_margin": ONE_CELL_MARGIN,
    "exclusion_semantics": "strict: below/above/within; at-threshold eligible",
})

MAP_TRANSFORM_FACTS = {
    "projected_crs": "EPSG:32631",
    "extent_origin_utm": [639841.997, 5506768.179],
    "meters_per_game_unit": 0.3048,
    "vertical_exaggeration": 1,
    "vertical_origin_meters": None,
    "vertical_origin_note": "extent-minimum convention; recorded when the payload is staged",
    "lattice_spacing_m": LATTICE,
    "measured_scale_l93_vs_utm31n_east": 7.77e-4,
    "measured_scale_l93_vs_utm31n_north": 7.84e-4,
    "s_floor_m": S_FLOOR,
}


class ProtocolStop(Exception):
    """Structured stop: frozen floors unmet, a post-DEM substitution attempt, or a
    final-freeze construction from stage-1 preliminary outputs."""

    def __init__(self, reason, **details):
        super().__init__(reason)
        self.reason = reason
        self.details = details


@dataclass(frozen=True)
class Pt:
    x: float
    y: float
    z: float
    cls: int


@dataclass
class Aoi:
    centre_utm: tuple
    centre_l93: tuple
    half: float = 550.0

    @property
    def size(self):
        return 2.0 * self.half

    @property
    def origin_utm(self):
        return (self.centre_utm[0] - self.half, self.centre_utm[1] - self.half)

    @property
    def origin_l93(self):
        return (self.centre_l93[0] - self.half, self.centre_l93[1] - self.half)

    @property
    def bbox_l93(self):
        return {
            "x_min": self.centre_l93[0] - self.half,
            "y_min": self.centre_l93[1] - self.half,
            "x_max": self.centre_l93[0] + self.half,
            "y_max": self.centre_l93[1] + self.half,
        }


DEFAULT_AOI = Aoi(centre_utm=(640391.997, 5507318.179), centre_l93=(840525.575, 6957616.498))


def utm_to_l93_approx(x_utm, y_utm, aoi=DEFAULT_AOI):
    """First-order linear map using the measured scale factors.

    Prototype stand-in for SYNTHETIC SCENE CONSTRUCTION ONLY (tests); the real run
    converts per point with system PROJ cs2cs and records cs2cs in the report. Never
    used by any measurement or gate path.
    """
    return (
        aoi.centre_l93[0] + (x_utm - aoi.centre_utm[0]) * (1.0 + 7.77e-4),
        aoi.centre_l93[1] + (y_utm - aoi.centre_utm[1]) * (1.0 + 7.84e-4),
    )


def lattice_centre(row, col, aoi=DEFAULT_AOI):
    ox, oy = aoi.origin_utm
    return (ox + (col + 0.5) * LATTICE, oy + (row + 0.5) * LATTICE)


def _col_range(lo_limit, hi_limit):
    """Inclusive integer lattice-column range whose centres lie in [lo_limit, hi_limit],
    where column col has its centre at (col + 0.5) x LATTICE from the origin."""
    lo = max(0, int(math.ceil(lo_limit / LATTICE - 0.5 - 1e-9)))
    hi = int(math.floor(hi_limit / LATTICE - 0.5 + 1e-9))
    return lo, hi


def enumerate_lattice_cells(aoi=DEFAULT_AOI):
    """Canonical evaluation lattice: (row, col) pairs whose cell centres lie fully
    inside the AOI minus the one-cell edge margin, row-major ascending.

    The margin is ONE CELL (3.048 m): centres must satisfy (col + 0.5) x 3.048 within
    [3.048, 1100 - 3.048], giving columns/rows 1..359 (359 x 359 = 128881 cells) on
    the canonical 1100 m extent.
    """
    margin = ONE_CELL_MARGIN * LATTICE
    col_lo, col_hi = _col_range(margin, aoi.size - margin)
    row_lo, row_hi = _col_range(margin, aoi.size - margin)
    return [(row, col) for row in range(row_lo, row_hi + 1) for col in range(col_lo, col_hi + 1)]


def full_extent_cell_counts(aoi=DEFAULT_AOI):
    """(cols, rows): counts of lattice cells whose centres lie anywhere inside the
    1100 m extent (no margin), 361 x 361 on the canonical AOI. This is the cell
    index space handed to the worker for candidate measurement."""
    lo, hi = _col_range(0.0, aoi.size)
    return hi - lo + 1, hi - lo + 1


def window_inside_footprint(x_l93, y_l93, radius, footprint):
    """True when the closed window circle lies fully inside the closed footprint
    rectangle (L93): a circle tangent to an edge from inside counts as inside; a
    centre whose circle crosses any edge is outside."""
    return (
        x_l93 - radius >= footprint["x_min"]
        and x_l93 + radius <= footprint["x_max"]
        and y_l93 - radius >= footprint["y_min"]
        and y_l93 + radius <= footprint["y_max"]
    )


def seam_border_distance(x_l93, y_l93, bbox_l93):
    """Minimum planar L93 distance from a point to the frozen E6 seams (E=840000,
    E=841000, N=6958000) and to the AOI border rectangle (L93 bbox edges)."""
    distances = [
        abs(x_l93 - 840000.0),
        abs(x_l93 - 841000.0),
        abs(y_l93 - 6958000.0),
        x_l93 - bbox_l93["x_min"],
        bbox_l93["x_max"] - x_l93,
        y_l93 - bbox_l93["y_min"],
        bbox_l93["y_max"] - y_l93,
    ]
    return min(distances)


def median_mad(values):
    """Median and MAD; even counts average the two central order statistics."""
    s = sorted(values)
    n = len(s)
    m = n // 2
    med = s[m] if n % 2 == 1 else (s[m - 1] + s[m]) / 2.0
    mad = _median_of(sorted(abs(v - med) for v in values))
    return med, mad


def _median_of(s):
    n = len(s)
    m = n // 2
    return s[m] if n % 2 == 1 else (s[m - 1] + s[m]) / 2.0


def _solve(a_mat, b_vec):
    """Gaussian elimination with partial pivoting for small dense systems."""
    n = len(b_vec)
    m = [list(a_mat[i]) + [b_vec[i]] for i in range(n)]
    for col in range(n):
        piv = max(range(col, n), key=lambda r: abs(m[r][col]))
        if m[piv][col] == 0.0:
            raise ValueError("singular normal matrix")
        m[col], m[piv] = m[piv], m[col]
        pivval = m[col][col]
        for r in range(col + 1, n):
            f = m[r][col] / pivval
            if f != 0.0:
                for c in range(col, n + 1):
                    m[r][c] -= f * m[col][c]
    x = [0.0] * n
    for i in range(n - 1, -1, -1):
        acc = m[i][n] - sum(m[i][j] * x[j] for j in range(i + 1, n))
        x[i] = acc / m[i][i]
    return x


def _wls_plane(us, vs, zs, ws):
    s11 = s12 = s13 = s22 = s23 = s33 = 0.0
    t1 = t2 = t3 = 0.0
    for u, v, z, w in zip(us, vs, zs, ws):
        s11 += w
        s12 += w * u
        s13 += w * v
        s22 += w * u * u
        s23 += w * u * v
        s33 += w * v * v
        t1 += w * z
        t2 += w * z * u
        t3 += w * z * v
    a, b, c = _solve(
        [[s11, s12, s13], [s12, s22, s23], [s13, s23, s33]],
        [t1, t2, t3],
    )
    return a, b, c


def fit_plane(points, eval_xy):
    """OLS plane then EXACTLY two Huber reweighting passes (protocol 6.2).

    sigma_fit is MAD_SCALE x final MAD, reported WITHOUT the floor. Final weights are
    the second-pass weights (reused by the supplementary quadratic fit). A zero
    residual keeps full weight (avoids dividing by zero under the floored scale).
    """
    ex, ey = eval_xy
    us = [p.x - ex for p in points]
    vs = [p.y - ey for p in points]
    zs = [p.z for p in points]
    n = len(points)
    if n < 3:
        raise ValueError("plane fit needs at least 3 points")
    a, b, c = _wls_plane(us, vs, zs, [1.0] * n)
    weights = [1.0] * n
    for _ in range(2):
        res = [z - (a + b * u + c * v) for u, v, z in zip(us, vs, zs)]
        _med, mad = median_mad(res)
        s = max(MAD_SCALE * mad, S_FLOOR)
        weights = [1.0 if r == 0.0 else min(1.0, HUBER_C * s / abs(r)) for r in res]
        a, b, c = _wls_plane(us, vs, zs, weights)
    res = [z - (a + b * u + c * v) for u, v, z in zip(us, vs, zs)]
    _med, mad = median_mad(res)
    return {
        "a": a,
        "b": b,
        "c": c,
        "sigma_fit": MAD_SCALE * mad,
        "weights": weights,
        "residuals": res,
    }


def curvature_bound_from_coeffs(d, e, f):
    """Max |q| over the half-footprint square, q = d u^2 + e u v + f v^2.

    The only interior stationary point of q is the origin (q = 0), so the boundary
    maximum is attained at the four corners plus, per edge, the interior vertex of
    that edge's one-dimensional quadratic when it falls strictly inside the edge.
    """
    h = HALF_FOOTPRINT
    cands = [(h, h), (h, -h), (-h, h), (-h, -h)]
    if f != 0.0:
        for u in (h, -h):
            v = -(e * u) / (2.0 * f)
            if abs(v) < h:
                cands.append((u, v))
    if d != 0.0:
        for v in (h, -h):
            u = -(e * v) / (2.0 * d)
            if abs(u) < h:
                cands.append((u, v))
    return max(abs(d * u * u + e * u * v + f * v * v) for u, v in cands)


def curvature_bound(points, weights, eval_xy):
    """Weighted full quadratic fit with the final plane Huber weights; returns (bound, (d,e,f))."""
    ex, ey = eval_xy
    cols = 6
    m = [[0.0] * cols for _ in range(cols)]
    rhs = [0.0] * cols

    def basis(u, v):
        return [1.0, u, v, u * u, u * v, v * v]

    for p, w in zip(points, weights):
        u = p.x - ex
        v = p.y - ey
        bv = basis(u, v)
        for i in range(cols):
            for j in range(cols):
                m[i][j] += w * bv[i] * bv[j]
            rhs[i] += w * bv[i] * p.z
    coeffs = _solve(m, rhs)
    d, e, f = coeffs[3], coeffs[4], coeffs[5]
    return curvature_bound_from_coeffs(d, e, f), (d, e, f)


def fired_rules(n2, d9, d17, sigma_fit, slope_deg, e6_dist, curv_bound):
    """Frozen exclusion rules E1..E7 with strict plan semantics (below/above/within)."""
    out = []
    if n2 < E1_N2:
        out.append(("E1", n2))
    if d9 is not None and d9 < E23_DIST:
        out.append(("E2", d9))
    if d17 is not None and d17 < E23_DIST:
        out.append(("E3", d17))
    if sigma_fit is not None and sigma_fit > E4_SIGMA:
        out.append(("E4", sigma_fit))
    if slope_deg is not None and slope_deg > E5_SLOPE_DEG:
        out.append(("E5", slope_deg))
    if e6_dist is not None and e6_dist < E6_SEAM:
        out.append(("E6", e6_dist))
    if curv_bound is not None and curv_bound > E7_CURV:
        out.append(("E7", curv_bound))
    return out


def classify_stratum(slope_deg, sigma_fit):
    """(stratum, pool) from the frozen slope strata and the S1-hard sub-stratum."""
    if slope_deg is None:
        return "", ""
    stratum = "S1" if slope_deg < S2_LOW_DEG else ("S2" if slope_deg < S3_LOW_DEG else "S3")
    pool = "S1H" if (stratum == "S1" and sigma_fit is not None and sigma_fit <= S1_HARD_SIGMA) else stratum
    return stratum, pool


def _e6_distance(x_l93, y_l93, bbox_l93):
    return seam_border_distance(x_l93, y_l93, bbox_l93)


def sector_of(x_utm, y_utm, aoi=DEFAULT_AOI):
    third = aoi.size / 3.0
    ox, oy = aoi.origin_utm
    sr = min(2, int(math.floor((y_utm - oy) / third)))
    sc = min(2, int(math.floor((x_utm - ox) / third)))
    return sr, sc


def sector_centroid(sr, sc, aoi=DEFAULT_AOI):
    third = aoi.size / 3.0
    ox, oy = aoi.origin_utm
    return (ox + (sc + 0.5) * third, oy + (sr + 0.5) * third)


def sector_name(sr, sc):
    return "s%d%d" % (sr, sc)


def evaluate_candidate(scene_points, eval_xy_utm, eval_xy_l93, aoi=DEFAULT_AOI, cell=None):
    """Full per-cell record per protocol 6.2/6.3: window fit, class mix, E1..E7,
    sector, stratum. Synthetic-scene path (tests / future scene validation); the
    stage-1 measurement path builds records from worker arrays in the controller."""
    ex_utm, ey_utm = eval_xy_utm
    ex, ey = eval_xy_l93
    if cell is None:
        ox, oy = aoi.origin_utm
        col = int(math.floor((ex_utm - ox) / LATTICE))
        row = int(math.floor((ey_utm - oy) / LATTICE))
    else:
        row, col = cell

    win2 = []
    mix = {2: 0, 3: 0, 4: 0, 5: 0, 6: 0, 9: 0, 17: 0}
    d9 = None
    d17 = None
    r2max = WINDOW_R * WINDOW_R
    for p in scene_points:
        dx = p.x - ex
        dy = p.y - ey
        d2 = dx * dx + dy * dy
        if p.cls == 2:
            if d2 <= r2max:
                win2.append(p)
                mix[2] += 1
        elif p.cls in mix:
            if d2 <= r2max:
                mix[p.cls] += 1
        if p.cls == 9:
            d = math.sqrt(d2)
            if d9 is None or d < d9:
                d9 = d
        elif p.cls == 17:
            d = math.sqrt(d2)
            if d17 is None or d < d17:
                d17 = d

    n2 = mix[2]
    truth_z = sigma = slope = curv = None
    curv_unavailable = False
    if len(win2) >= 3:
        fit = fit_plane(win2, (ex, ey))
        truth_z = fit["a"]
        sigma = fit["sigma_fit"]
        slope = math.degrees(math.atan(math.hypot(fit["b"], fit["c"])))
        if len(win2) >= 6:
            try:
                curv, _ = curvature_bound(win2, fit["weights"], (ex, ey))
            except ValueError:
                # Degenerate window geometry: E7 is unevaluable and recorded as such.
                curv = None
                curv_unavailable = True
        else:
            curv_unavailable = True

    e6 = _e6_distance(ex, ey, aoi.bbox_l93)
    fired = fired_rules(n2, d9, d17, sigma, slope, e6, curv)

    sr, sc = sector_of(ex_utm, ey_utm, aoi)
    cx, cy = sector_centroid(sr, sc, aoi)
    sector_dist = math.hypot(ex_utm - cx, ey_utm - cy)
    stratum, pool = classify_stratum(slope, sigma)

    return {
        "id": "r%dc%d" % (row, col),
        "row": row,
        "col": col,
        "utm_x": ex_utm,
        "utm_y": ey_utm,
        "l93_x": ex,
        "l93_y": ey,
        "n2": n2,
        "density": n2 / (math.pi * WINDOW_R * WINDOW_R),
        "truth_z": truth_z,
        "sigma_fit": sigma,
        "slope_deg": slope,
        "curv_bound": curv,
        "n3": mix[3],
        "n4": mix[4],
        "n5": mix[5],
        "n6": mix[6],
        "n9": mix[9],
        "n17": mix[17],
        "d9": d9,
        "d17": d17,
        "e6_dist": e6,
        "curv_unavailable": curv_unavailable,
        "sector": sector_name(sr, sc),
        "sector_dist": sector_dist,
        "stratum": stratum,
        "pool": pool,
        "eligible": not fired,
        "fired": fired,
        "rank": None,
    }


def rank_key(cell):
    """sigma_fit asc, n2 desc, sector-centroid distance asc, row asc, col asc: total order."""
    return (cell["sigma_fit"], -cell["n2"], cell["sector_dist"], cell["row"], cell["col"])


@dataclass
class Selection:
    primaries: list = field(default_factory=list)
    alternates: list = field(default_factory=list)
    ladder: list = field(default_factory=list)
    sectors_covered: list = field(default_factory=list)
    alternates_shortfall: int = 0
    substitutions: list = field(default_factory=list)
    post_dem: bool = False
    stage_1_preliminary: bool = False

    def mark_post_dem(self):
        self.post_dem = True

    def substitute_alternate(self, primary_id):
        """PRE-DEM ONLY (protocol 6.4): refused once any height or residual was inspected."""
        if self.post_dem:
            raise ProtocolStop("substitution_after_dem_open")
        idx = next((i for i, c in enumerate(self.primaries) if c["id"] == primary_id), None)
        if idx is None:
            raise ValueError("unknown primary id: %s" % primary_id)
        if not self.alternates:
            raise ProtocolStop("no_alternates_available")
        alt = self.alternates.pop(0)
        removed = self.primaries[idx]
        self.primaries[idx] = alt
        self.substitutions.append({"removed": removed["id"], "added": alt["id"]})


def mark_post_dem(selection):
    selection.mark_post_dem()


def substitute_alternate(selection, primary_id):
    selection.substitute_alternate(primary_id)


def select(candidates, stage_1_preliminary=False):
    """Frozen ladder, 100 m greedy in ranking order, hard 5-primary/3-sector floor
    (protocol 6.4). stage_1_preliminary marks the output as stage-1 availability
    evidence only (v1.2): freeze() refuses such selections."""
    eligible = [c for c in candidates if c["eligible"]]
    ranked = sorted(eligible, key=rank_key)
    for i, c in enumerate(ranked):
        c["rank"] = i + 1
    pools = {"S1H": [], "S2": [], "S3": []}
    for c in ranked:
        if c["pool"] in pools:
            pools[c["pool"]].append(c)
    has_s2 = bool(pools["S2"])
    has_s3 = bool(pools["S3"])
    if has_s2 and has_s3:
        ladder = [("S1H", 3), ("S2", 1), ("S3", 1)]
    elif has_s3:
        ladder = [("S1H", 4), ("S3", 1)]
    elif has_s2:
        ladder = [("S1H", 4), ("S2", 1)]
    else:
        ladder = [("S1H", 5)]

    remaining = dict(ladder)
    primaries = []
    for c in ranked:
        pool = c["pool"]
        if pool not in remaining or remaining[pool] <= 0:
            continue
        if all(math.hypot(c["utm_x"] - p["utm_x"], c["utm_y"] - p["utm_y"]) >= MIN_SPACING
               for p in primaries):
            primaries.append(c)
            remaining[pool] -= 1
            if all(v == 0 for v in remaining.values()):
                break

    if len(primaries) < MIN_PRIMARY:
        raise ProtocolStop(
            "eligible_below_floor",
            eligible=len(eligible),
            selected=len(primaries),
            ladder=ladder,
        )
    sectors = sorted(set(c["sector"] for c in primaries))
    if len(sectors) < MIN_SECTORS:
        raise ProtocolStop(
            "sectors_below_floor",
            covered=sectors,
            selected=len(primaries),
            ladder=ladder,
        )

    alternates = []
    for c in ranked:
        if len(alternates) >= N_ALTERNATES:
            break
        if c["pool"] != "S1H" or any(c is p for p in primaries):
            continue
        if all(math.hypot(c["utm_x"] - p["utm_x"], c["utm_y"] - p["utm_y"]) >= MIN_SPACING
               for p in primaries + alternates):
            alternates.append(c)

    return Selection(
        primaries=primaries,
        alternates=alternates,
        ladder=ladder,
        sectors_covered=sectors,
        alternates_shortfall=N_ALTERNATES - len(alternates),
        stage_1_preliminary=stage_1_preliminary,
    )


_CAND_COLUMNS = [
    "id", "row", "col", "utm_x", "utm_y", "l93_x", "l93_y", "n2", "density",
    "truth_z", "sigma_fit", "slope_deg", "curv_bound",
    "n3", "n4", "n5", "n6", "n9", "n17",
    "sector", "sector_dist", "stratum", "pool", "rank", "eligible", "fired",
]
_SEL_COLUMNS = [
    "id", "role", "rank", "row", "col", "utm_x", "utm_y", "l93_x", "l93_y",
    "game_x", "game_y", "wgs84_lat", "wgs84_lon",
    "truth_z", "sigma_fit", "slope_deg", "curv_bound", "density",
    "n2", "n3", "n4", "n5", "n6", "n9", "n17", "stratum", "sector",
]


def _fmt(v):
    if v is None:
        return ""
    if isinstance(v, bool):
        return "1" if v else "0"
    if isinstance(v, int):
        return str(v)
    if isinstance(v, float):
        return "%.6f" % v
    return str(v)


def _fmt9(v):
    return "" if v is None else "%.9f" % v


def _fired_str(fired):
    return ";".join("%s=%s" % (r, _fmt(t)) for r, t in fired)


def _table_bytes(header, rows):
    lines = ["\t".join(header)]
    lines.extend("\t".join(r) for r in rows)
    return ("\n".join(lines) + "\n").encode("utf-8")


def utm_to_wgs84(easting, northing, zone=31):
    """Standard UTM-Snyder inverse series on WGS84; informational precision (~cm)."""
    a = 6378137.0
    f = 1.0 / 298.257223563
    e2 = f * (2.0 - f)
    ep2 = e2 / (1.0 - e2)
    k0 = 0.9996
    x = easting - 500000.0
    y = northing
    m = y / k0
    mu = m / (a * (1.0 - e2 / 4.0 - 3.0 * e2 ** 2 / 64.0 - 5.0 * e2 ** 3 / 256.0))
    e1 = (1.0 - math.sqrt(1.0 - e2)) / (1.0 + math.sqrt(1.0 - e2))
    p1 = (mu
          + (3.0 * e1 / 2.0 - 27.0 * e1 ** 3 / 32.0) * math.sin(2.0 * mu)
          + (21.0 * e1 ** 2 / 16.0 - 55.0 * e1 ** 4 / 32.0) * math.sin(4.0 * mu)
          + (151.0 * e1 ** 3 / 96.0) * math.sin(6.0 * mu)
          + (1097.0 * e1 ** 4 / 512.0) * math.sin(8.0 * mu))
    s2 = math.sin(p1) ** 2
    c1 = ep2 * math.cos(p1) ** 2
    t1 = math.tan(p1) ** 2
    n1 = a / math.sqrt(1.0 - e2 * s2)
    r1 = a * (1.0 - e2) / ((1.0 - e2 * s2) ** 1.5)
    d = x / (n1 * k0)
    lat = p1 - (n1 * math.tan(p1) / r1) * (
        d ** 2 / 2.0
        - (5.0 + 3.0 * t1 + 10.0 * c1 - 4.0 * c1 * c1 - 9.0 * ep2) * d ** 4 / 24.0
        + (61.0 + 90.0 * t1 + 298.0 * c1 + 45.0 * t1 * t1 - 252.0 * ep2 - 3.0 * c1 * c1) * d ** 6 / 720.0
    )
    lon = (d
           - (1.0 + 2.0 * t1 + c1) * d ** 3 / 6.0
           + (5.0 - 2.0 * c1 + 28.0 * t1 - 3.0 * c1 * c1 + 8.0 * ep2 + 24.0 * t1 * t1) * d ** 5 / 120.0
           ) / math.cos(p1)
    lon0 = math.radians(6.0 * zone - 183.0)
    return math.degrees(lat), math.degrees(lon) + (6.0 * zone - 183.0)


def candidate_rows(candidates):
    rows = []
    for c in sorted(candidates, key=lambda c: (c["row"], c["col"])):
        rows.append([
            c["id"], str(c["row"]), str(c["col"]),
            _fmt(c["utm_x"]), _fmt(c["utm_y"]), _fmt(c["l93_x"]), _fmt(c["l93_y"]),
            str(c["n2"]), _fmt(c["density"]), _fmt(c["truth_z"]), _fmt(c["sigma_fit"]),
            _fmt(c["slope_deg"]), _fmt(c["curv_bound"]),
            str(c["n3"]), str(c["n4"]), str(c["n5"]), str(c["n6"]), str(c["n9"]), str(c["n17"]),
            c["sector"], _fmt(c["sector_dist"]), c["stratum"], c["pool"],
            "" if c["rank"] is None else str(c["rank"]),
            "1" if c["eligible"] else "0", _fired_str(c["fired"]),
        ])
    return rows


def selection_rows(selection, aoi):
    ox, oy = aoi.origin_utm
    rows = []
    entries = [("primary", c) for c in sorted(selection.primaries, key=lambda c: c["rank"])]
    entries += [("alternate", c) for c in sorted(selection.alternates, key=lambda c: c["rank"])]
    for role, c in entries:
        lat, lon = utm_to_wgs84(c["utm_x"], c["utm_y"])
        rows.append([
            c["id"], role, str(c["rank"]), str(c["row"]), str(c["col"]),
            _fmt(c["utm_x"]), _fmt(c["utm_y"]), _fmt(c["l93_x"]), _fmt(c["l93_y"]),
            _fmt((c["utm_x"] - ox) / 0.3048), _fmt((c["utm_y"] - oy) / 0.3048),
            _fmt9(lat), _fmt9(lon),
            _fmt(c["truth_z"]), _fmt(c["sigma_fit"]), _fmt(c["slope_deg"]), _fmt(c["curv_bound"]),
            _fmt(c["density"]), str(c["n2"]),
            str(c["n3"]), str(c["n4"]), str(c["n5"]), str(c["n6"]), str(c["n9"]), str(c["n17"]),
            c["stratum"], c["sector"],
        ])
    return rows


def candidates_table_bytes(candidates):
    """Deterministic TSV bytes of every candidate record (protocol 6.5 table shape)."""
    return _table_bytes(_CAND_COLUMNS, candidate_rows(candidates))


def selection_table_bytes(selection, aoi=DEFAULT_AOI):
    """Deterministic TSV bytes of primaries (role=primary) and alternates
    (role=alternate) in rank order."""
    return _table_bytes(_SEL_COLUMNS, selection_rows(selection, aoi))


def freeze(candidates, selection, out_dir, aoi=DEFAULT_AOI, note=""):
    """FINAL all-tiles freeze (protocol 6.5): candidates.tsv, selection.tsv,
    frozen-manifest.json. Deterministic tables. REFUSES stage-1 preliminary
    selections (protocol v1.2): preliminary availability evidence is never frozen;
    the final selection runs over ALL approved tiles with its own pre-DEM freeze."""
    if selection.stage_1_preliminary:
        raise ProtocolStop(
            "stage1_preliminary_never_final",
            detail="stage-1 preliminary candidates are availability evidence only; "
                   "the final freeze requires the all-tiles selection",
        )
    os.makedirs(out_dir, exist_ok=True)
    cand_bytes = candidates_table_bytes(candidates)
    sel_bytes = selection_table_bytes(selection, aoi)
    cand_path = os.path.join(out_dir, "candidates.tsv")
    sel_path = os.path.join(out_dir, "selection.tsv")
    with open(cand_path, "wb") as fh:
        fh.write(cand_bytes)
    with open(sel_path, "wb") as fh:
        fh.write(sel_bytes)
    manifest = {
        "protocol_version": PROTOCOL_VERSION,
        "scope": "final all-tiles selection freeze",
        "constants": {
            "window_r": WINDOW_R, "half_footprint": HALF_FOOTPRINT, "lattice": LATTICE,
            "e1_n2": E1_N2, "e23_dist": E23_DIST, "e4_sigma": E4_SIGMA,
            "e5_slope_deg": E5_SLOPE_DEG, "e6_seam": E6_SEAM, "e7_curv": E7_CURV,
            "huber_c": HUBER_C, "mad_scale": MAD_SCALE, "s_floor": S_FLOOR,
            "s1_hard_sigma": S1_HARD_SIGMA, "strata_bounds_deg": [S2_LOW_DEG, S3_LOW_DEG],
            "min_primary": MIN_PRIMARY, "min_sectors": MIN_SECTORS,
            "min_spacing_m": MIN_SPACING, "n_alternates": N_ALTERNATES,
            "seam_lines_l93": [list(s) for s in SEAM_LINES_L93],
        },
        "map_transform": MAP_TRANSFORM_FACTS,
        "tables": {
            "candidates_sha256": hashlib.sha256(cand_bytes).hexdigest(),
            "selection_sha256": hashlib.sha256(sel_bytes).hexdigest(),
        },
        "python_version": platform.python_version(),
        "generated_at": datetime.now().astimezone().isoformat(timespec="seconds"),
        "selection": {
            "primary_ids": [c["id"] for c in sorted(selection.primaries, key=lambda c: c["rank"])],
            "alternate_ids": [c["id"] for c in sorted(selection.alternates, key=lambda c: c["rank"])],
            "sectors_covered": list(selection.sectors_covered),
            "ladder": [[pool, n] for pool, n in selection.ladder],
            "alternates_shortfall": selection.alternates_shortfall,
            "substitutions": [dict(s) for s in selection.substitutions],
        },
        "note": note,
    }
    man_path = os.path.join(out_dir, "frozen-manifest.json")
    with open(man_path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(json.dumps(manifest, indent=2, sort_keys=True) + "\n")
    return manifest


def canonical_digest(manifest):
    """sha256 over canonical JSON of the manifest with the timestamp removed."""
    m = dict(manifest)
    m.pop("generated_at", None)
    payload = json.dumps(m, sort_keys=True, separators=(",", ":"), ensure_ascii=True)
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()
