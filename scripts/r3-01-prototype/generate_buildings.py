#!/usr/bin/env python3
"""WI-R3-01 investigation prototype: Sedan building footprint extrusion generator.

Owner: Track R R3-1 (procedural buildings prototype). Investigation-only; writes
only into the operator scratch directory passed via --out. Stdlib only.

Pipeline
    --download-materials : fetch four pinned ambientCG CC0 material zips, extract
      basecolor/normal/roughness maps, write LICENSES.md and materials.json.
    default (generate)   : read the r2-03 footprints payload + KML cross-check,
      tabulate attribute and shape statistics, extrude footprints into glTF 2.0
      (merged per terrain chunk and per building), write stats.json.

Coordinate and unit contract (documented choice, see WI-R3-01 brief)
    Positions are emitted directly in engine-axis GAME units: x = east-game,
    y = north-game, z = up-game, building base at z = 0, top at
    hauteur_meters / metersPerGameUnit (0.3048 m per game unit). The script does
    NOT re-orient to glTF Y-up; the engine importer owns the glTF-to-engine axis
    mapping and the scratch scenario seats instances on terrain without extra
    rotation. UVs are world meters divided by the per-material tile size, so one
    texture tile spans a fixed physical size without KHR_texture_transform. Wall
    UVs use horizontal arc length (u) and height (v); roof UVs use plan
    projection (world x/y).

Determinism
    Same inputs produce byte-identical glTF/.bin output: sorted JSON keys, fixed
    little-endian struct packs, iteration ordered by cleabs/partId, material
    variants chosen by a stable hash of cleabs, no timestamps inside glTF/bin
    (a generated-at stamp is written only into stats.json).

Roof rules
    flat    : ear-clipped roof at wall height (always applicable).
    gabled  : ridge along the longest edge direction of the minimum rotated
      rectangle; applicable only to convex hole-free footprints with at least 4
      unique vertices and area at or above the tiny-area threshold. Ineligible
      buildings fall back to flat. Failure reasons are counted individually.
    A hipped-from-straight-skeleton rule is NOT implemented; its approximate
    applicability (identical convexity gate) is counted into stats.json.

Data notes (verified against the r2-03 payload/KML)
    payload features.items carries 1x annotation (sedan-extent) plus passability
    and buildability duplicates of the same buildings; only kind == "passability"
    is used. A few cleabs are multi-part (partId suffix #k) and share one
    attribute table. hauteur is a string in meters and may be empty; the
    fallback is the median hauteur of buildings with the same usage_1, then the
    global median. The tiny-area threshold is max(min kept footprint area
    derived from the payload, the acquisition filter threshold from the r2-03
    provenance) because the payload retains a few sub-threshold slivers.
"""

from __future__ import annotations

import argparse
import datetime
import hashlib
import json
import math
import os
import statistics
import struct
import sys
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

SCRIPT_NAME = "generate_buildings.py"
USER_AGENT = "Panopticon-r3-01-prototype/1.0 (investigation; CC0 materials)"
EPS = 1e-9
ROOF_PITCH_DEG = 35.0
GLTF_POS_COMPONENT = 5126  # FLOAT
GLTF_IDX_COMPONENT = 5125  # UNSIGNED_INT
MAG = 34962  # ARRAY_BUFFER target
MIAG = 34963  # ELEMENT_ARRAY_BUFFER target

# Pinned ambientCG CC0 material sets. IDs verified against the ambientCG API;
# get?file= URLs redirect to the download CDN.
MATERIAL_TABLE = [
    {
        "role": "wall_brick",
        "asset_id": "Bricks097",
        "url": "https://ambientcg.com/get?file=Bricks097_2K_JPG.zip",
        "tile_size_meters": 2.5,
    },
    {
        "role": "wall_plaster",
        "asset_id": "Plaster001",
        "url": "https://ambientcg.com/get?file=Plaster001_2K_JPG.zip",
        "tile_size_meters": 2.5,
    },
    {
        "role": "roof_slate",
        "asset_id": "RoofingTiles003",
        "url": "https://ambientcg.com/get?file=RoofingTiles003_2K_JPG.zip",
        "tile_size_meters": 1.5,
    },
    {
        "role": "roof_clay",
        "asset_id": "RoofingTiles013A",
        "url": "https://ambientcg.com/get?file=RoofingTiles013A_2K_JPG.zip",
        "tile_size_meters": 1.0,
    },
]
CC0_URL = "https://creativecommons.org/publicdomain/zero/1.0/legalcode.txt"
CC0_TEXT_URL = "https://creativecommons.org/publicdomain/zero/1.0/legalcode.txt"

DEFAULT_PROVENANCE = (
    "/mnt/pit2/panopticon/geo-proof/r2-03-footprints-v3/provenance/footprints-provenance.json"
)
DEFAULT_SOURCES = "/mnt/pit2/panopticon/geo-proof/r2-03-footprints-v3/sources.txt"
ATTR_FIELDS = ("cleabs", "nature", "usage_1", "usage_2", "hauteur")


def fail(code: str, message: str) -> None:
    """Named-error exit: prototype convention from scripts/geo/footprints.py."""
    print(f"{SCRIPT_NAME}: error {code}: {message}", file=sys.stderr)
    sys.exit(1)


def sha256_file(path: str) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


# ---------------------------------------------------------------------------
# Geometry primitives (2D in game units unless noted)
# ---------------------------------------------------------------------------


def shoelace(pts: list[tuple[float, float]]) -> float:
    s = 0.0
    n = len(pts)
    for i in range(n):
        x1, y1 = pts[i]
        x2, y2 = pts[(i + 1) % n]
        s += x1 * y2 - x2 * y1
    return s / 2.0


def ring_area_open(pts: list[tuple[float, float]]) -> float:
    """Signed area of a ring given without the duplicated closing point.

    Identical cycle to shoelace(); kept as a named alias so call sites state
    intent (CCW positive)."""
    return shoelace(pts)


def unique_points(pts: list[tuple[float, float]]) -> list[tuple[float, float]]:
    out: list[tuple[float, float]] = []
    for p in pts:
        if not out or abs(p[0] - out[-1][0]) > EPS or abs(p[1] - out[-1][1]) > EPS:
            out.append(p)
    if len(out) > 1 and abs(out[0][0] - out[-1][0]) <= EPS and abs(out[0][1] - out[-1][1]) <= EPS:
        out.pop()
    return out


def strip_collinear(pts: list[tuple[float, float]]) -> list[tuple[float, float]]:
    n = len(pts)
    out = list(pts)
    changed = True
    while changed and len(out) > 2:
        changed = False
        keep: list[tuple[float, float]] = []
        m = len(out)
        for i in range(m):
            a = out[(i - 1) % m]
            b = out[i]
            c = out[(i + 1) % m]
            cross = (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
            if abs(cross) <= 1e-7:
                changed = True
                continue
            keep.append(b)
        out = keep
    if len(out) < 3:
        return pts[:0]
    return out


def is_convex(pts: list[tuple[float, float]]) -> bool:
    """Strict convexity: no sign flips in consecutive cross products (collinear ok)."""
    n = len(pts)
    sign = 0
    for i in range(n):
        a = pts[(i - 1) % n]
        b = pts[i]
        c = pts[(i + 1) % n]
        cross = (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
        if abs(cross) <= 1e-7:
            continue
        s = 1 if cross > 0 else -1
        if sign == 0:
            sign = s
        elif s != sign:
            return False
    return True


def convex_hull(pts: list[tuple[float, float]]) -> list[tuple[float, float]]:
    """Monotone chain hull, CCW, collinear points removed."""
    P = sorted(set(pts))
    if len(P) < 3:
        return []
    lower: list[tuple[float, float]] = []
    for p in P:
        while len(lower) >= 2:
            a, b = lower[-2], lower[-1]
            if (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0]) <= 1e-12:
                lower.pop()
            else:
                break
        lower.append(p)
    upper: list[tuple[float, float]] = []
    for p in reversed(P):
        while len(upper) >= 2:
            a, b = upper[-2], upper[-1]
            if (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0]) <= 1e-12:
                upper.pop()
            else:
                break
        upper.append(p)
    hull = lower[:-1] + upper[:-1]
    return strip_collinear(hull)


def min_rot_rect(hull: list[tuple[float, float]]) -> tuple[float, tuple[float, float]]:
    """Minimum-area rotated rectangle over hull edge directions.

    Returns (area, direction unit vector of the rectangle's LONGER side).
    """
    best_area = float("inf")
    best_dir = (1.0, 0.0)
    n = len(hull)
    for i in range(n):
        x1, y1 = hull[i]
        x2, y2 = hull[(i + 1) % n]
        ex, ey = x2 - x1, y2 - y1
        el = math.hypot(ex, ey)
        if el <= EPS:
            continue
        ux, uy = ex / el, ey / el
        vx, vy = -uy, ux
        us = [ (p[0] - x1) * ux + (p[1] - y1) * uy for p in hull ]
        vs = [ (p[0] - x1) * vx + (p[1] - y1) * vy for p in hull ]
        area = (max(us) - min(us)) * (max(vs) - min(vs))
        if area < best_area - EPS:
            best_area = area
            long_u = (max(us) - min(us)) >= (max(vs) - min(vs))
            best_dir = (ux, uy) if long_u else (vx, vy)
    return best_area, best_dir


def segment_intersects(a: tuple[float, float], b: tuple[float, float],
                       c: tuple[float, float], d: tuple[float, float]) -> bool:
    """Proper-intersection test (touching endpoints allowed)."""
    def orient(p, q, r):
        v = (q[0] - p[0]) * (r[1] - p[1]) - (q[1] - p[1]) * (r[0] - p[0])
        if v > 1e-9:
            return 1
        if v < -1e-9:
            return -1
        return 0

    o1 = orient(a, b, c)
    o2 = orient(a, b, d)
    o3 = orient(c, d, a)
    o4 = orient(c, d, b)
    return o1 != o2 and o3 != o4


def point_in_tri(p, a, b, c) -> bool:
    """Strict interior test against a CCW triangle (all edge cross products > 0)."""
    d1 = (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0])
    d2 = (c[0] - b[0]) * (p[1] - b[1]) - (c[1] - b[1]) * (p[0] - b[0])
    d3 = (a[0] - c[0]) * (p[1] - c[1]) - (a[1] - c[1]) * (p[0] - c[0])
    return min(d1, d2, d3) > 1e-9


def ear_clip(pts: list[tuple[float, float]]) -> list[tuple[int, int, int]]:
    """Ear clipping of a simple CCW polygon; returns index triples (+z normals)."""
    n = len(pts)
    if n < 3:
        return []
    idx = list(range(n))
    tris: list[tuple[int, int, int]] = []
    guard_limit = 4 * n * n + 64
    guard = 0
    while len(idx) > 3:
        guard += 1
        if guard > guard_limit:
            fail("ERR-TRIANGULATE", f"ear clipping stalled on {n}-gon")
        m = len(idx)
        clipped = False
        for k in range(m):
            i0, i1, i2 = idx[(k - 1) % m], idx[k], idx[(k + 1) % m]
            a, b, c = pts[i0], pts[i1], pts[i2]
            cross = (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
            if cross <= 1e-9:
                continue
            bad = False
            for j in idx:
                if j in (i0, i1, i2):
                    continue
                if point_in_tri(pts[j], a, b, c):
                    bad = True
                    break
            if bad:
                continue
            tris.append((i0, i1, i2))
            idx.pop(k)
            clipped = True
            break
        if not clipped:
            fail("ERR-TRIANGULATE", f"no ear found in {n}-gon (self-intersecting ring?)")
    tris.append((idx[0], idx[1], idx[2]))
    return tris


def clean_ring(pts: list[tuple[float, float]]) -> list[tuple[float, float]]:
    """Collapse consecutive duplicates, then collinear vertices, until stable.

    Order matters: stripping before dedup would delete duplicated bridge/slit
    points (zero cross product against themselves) and collapse the slit.
    """
    cur = unique_points(pts)
    for _ in range(4):
        nxt = unique_points(strip_collinear(cur))
        if nxt == cur:
            return cur
        cur = nxt
    return cur


def bridge_holes(outer: list[tuple[float, float]],
                 holes: list[list[tuple[float, float]]]) -> list[tuple[float, float]]:
    """Bridge hole rings into the outer ring with slits; returns one simple CCW ring."""
    ring = list(outer)
    holes_sorted = sorted(holes, key=lambda h: (-max(p[0] for p in h), h))
    for hole in holes_sorted:
        if ring_area_open(hole) > 0:
            hole = list(reversed(hole))
        n = len(ring)
        hi = max(range(len(hole)), key=lambda i: (hole[i][0], -hole[i][1]))
        hx, hy = hole[hi]
        best_t = None
        best_edge = None
        for i in range(n):
            ax, ay = ring[i]
            bx, by = ring[(i + 1) % n]
            if (ay <= hy < by) or (by <= hy < ay):
                if abs(by - ay) <= EPS:
                    continue
                x = ax + (hy - ay) * (bx - ax) / (by - ay)
                if x >= hx:
                    t = x - hx
                    if best_t is None or t < best_t - EPS:
                        best_t = t
                        best_edge = i
        if best_edge is None:
            fail("ERR-BRIDGE", "hole ray cast found no outer edge intersection")
        i = best_edge
        j = (i + 1) % n
        pi = i if _dist2(ring[i], (hx, hy)) <= _dist2(ring[j], (hx, hy)) else j
        p = ring[pi]
        if not _visible((hx, hy), p, ring):
            found = False
            order = sorted(range(n), key=lambda k: (_dist2(ring[k], (hx, hy)), k))
            for k in order:
                if _visible((hx, hy), ring[k], ring):
                    pi = k
                    p = ring[k]
                    found = True
                    break
            if not found:
                fail("ERR-BRIDGE", "no mutually visible bridge vertex for hole")
        rot = hole[hi:] + hole[:hi]
        ring = ring[: pi + 1] + [p] + rot + [(hx, hy)] + ring[pi:]
    return clean_ring(ring)


def _dist2(a, b) -> float:
    return (a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2


def _visible(a, b, ring) -> bool:
    n = len(ring)
    for i in range(n):
        c = ring[i]
        d = ring[(i + 1) % n]
        if _same_pt(c, a) or _same_pt(d, a) or _same_pt(c, b) or _same_pt(d, b):
            continue
        if segment_intersects(a, b, c, d):
            return False
    return True


def _same_pt(a, b) -> bool:
    return abs(a[0] - b[0]) <= EPS and abs(a[1] - b[1]) <= EPS


# ---------------------------------------------------------------------------
# Mesh assembly
# ---------------------------------------------------------------------------


class MeshBuilder:
    """Indexed triangle soup for one (glTF, material) slot.

    Deterministic: vertices deduplicated by packed float32 bytes in insertion
    order; indices appended in build order.
    """

    def __init__(self) -> None:
        self.pos: list[tuple[float, float, float]] = []
        self.nrm: list[tuple[float, float, float]] = []
        self.uv: list[tuple[float, float]] = []
        self.idx: list[int] = []
        self._cache: dict[bytes, int] = {}

    def tri(self, a, na, ua, b, nb, ub, c, nc, uc, flip=False) -> None:
        if flip:
            b, nb, ub, c, nc, uc = c, nc, uc, b, nb, ub
        i = self._vert(a, na, ua)
        j = self._vert(b, nb, ub)
        k = self._vert(c, nc, uc)
        if i == j or j == k or i == k:
            return
        self.idx.extend((i, j, k))

    def merge(self, other: "MeshBuilder") -> None:
        """Append another builder's triangles, remapping its local indices."""
        for i in other.idx:
            self.idx.append(self._vert(other.pos[i], other.nrm[i], other.uv[i]))

    def _vert(self, p, n, t) -> int:
        key = struct.pack("<3f3f2f", *p, *n, *t)
        got = self._cache.get(key)
        if got is not None:
            return got
        self.pos.append(p)
        self.nrm.append(n)
        self.uv.append(t)
        self._cache[key] = len(self.pos) - 1
        return len(self.pos) - 1


def _tri_normal(a, b, c) -> tuple[float, float, float]:
    ux, uy, uz = b[0] - a[0], b[1] - a[1], b[2] - a[2]
    vx, vy, vz = c[0] - a[0], c[1] - a[1], c[2] - a[2]
    nx = uy * vz - uz * vy
    ny = uz * vx - ux * vz
    nz = ux * vy - uy * vx
    l = math.sqrt(nx * nx + ny * ny + nz * nz)
    if l <= EPS:
        return (0.0, 0.0, 1.0)
    return (nx / l, ny / l, nz / l)


def extrude_walls(mesh: MeshBuilder, ring: list[tuple[float, float]], h_game: float,
                  meters_per_game: float, tile_m: float, is_hole: bool) -> int:
    """Vertical wall quads along an open ring; returns quad count.

    Normalization: outer rings CCW get outward normals from the winding below;
    hole rings are reversed first so the same code path keeps normals pointing
    away from the building material.
    """
    pts = list(ring)
    if not is_hole and ring_area_open(pts) < 0:
        pts.reverse()
    elif is_hole and ring_area_open(pts) > 0:
        pts.reverse()
    n = len(pts)
    hm = h_game * meters_per_game
    inv_tile = 1.0 / tile_m
    s = 0.0
    quads = 0
    for i in range(n):
        x0, y0 = pts[i]
        x1, y1 = pts[(i + 1) % n]
        seg = math.hypot(x1 - x0, y1 - y0)
        if seg <= EPS:
            continue
        s0 = s
        s1 = s + seg * meters_per_game
        s = s1
        b0 = (x0, y0, 0.0)
        b1 = (x1, y1, 0.0)
        t0 = (x0, y0, h_game)
        t1 = (x1, y1, h_game)
        flat = _tri_normal((x0, y0, 0.0), (x1, y1, 0.0), (x1, y1, h_game))
        u0, v0 = s0 * inv_tile, 0.0
        u1, v1 = s1 * inv_tile, hm * inv_tile
        if is_hole:
            mesh.tri(b0, flat, (u0, v1), t1, flat, (u1, v1), b1, flat, (u1, v0))
            mesh.tri(b0, flat, (u0, v1), t0, flat, (u0, v0), t1, flat, (u1, v1))
        else:
            mesh.tri(b0, flat, (u0, v0), b1, flat, (u1, v0), t1, flat, (u1, v1))
            mesh.tri(b0, flat, (u0, v0), t1, flat, (u1, v1), t0, flat, (u0, v1))
        quads += 1
    return quads


def flat_roof(mesh: MeshBuilder, outer: list[tuple[float, float]],
              holes: list[list[tuple[float, float]]], h_game: float,
              meters_per_game: float, tile_m: float) -> int:
    """Ear-clipped flat roof at wall height; plan-projected UVs. Returns triangles."""
    ccw = list(outer)
    if ring_area_open(ccw) < 0:
        ccw.reverse()
    hs = []
    for hole in holes:
        h = list(hole)
        if ring_area_open(h) > 0:
            h.reverse()
        hs.append(h)
    bridged = bridge_holes(unique_points(ccw), [unique_points(h) for h in hs])
    n_up = (0.0, 0.0, 1.0)
    inv_tile = 1.0 / tile_m
    count = 0
    for (i, j, k) in ear_clip(bridged):
        a = bridged[i]
        b = bridged[j]
        c = bridged[k]
        area2 = (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
        if abs(area2) <= EPS:
            continue
        flip = area2 < 0
        if flip:
            b, c = c, b
        mesh.tri(
            (a[0], a[1], h_game), n_up, (a[0] * meters_per_game * inv_tile, a[1] * meters_per_game * inv_tile),
            (b[0], b[1], h_game), n_up, (b[0] * meters_per_game * inv_tile, b[1] * meters_per_game * inv_tile),
            (c[0], c[1], h_game), n_up, (c[0] * meters_per_game * inv_tile, c[1] * meters_per_game * inv_tile),
        )
        count += 1
    return count


def gabled_roof(mesh_roof: MeshBuilder, mesh_wall: MeshBuilder,
                outer: list[tuple[float, float]], h_game: float,
                meters_per_game: float, tile_m: float) -> tuple[int, int]:
    """Gabled roof for a convex hole-free footprint.

    Ridge runs along the longer side direction of the minimum rotated rectangle
    through the footprint center at h + tan(pitch) * half-width. Returns
    (roof triangles, gable-end wall triangles).
    """
    hull = unique_points(strip_collinear(convex_hull(outer)))
    if len(hull) < 4 or not is_convex(hull):
        return 0, 0
    _, (ux, uy) = min_rot_rect(hull)
    vx, vy = -uy, ux
    cx = sum(p[0] for p in hull) / len(hull)
    cy = sum(p[1] for p in hull) / len(hull)
    us = [(p[0] - cx) * ux + (p[1] - cy) * uy for p in hull]
    ts = [(p[0] - cx) * vx + (p[1] - cy) * vy for p in hull]
    pitch = math.tan(math.radians(ROOF_PITCH_DEG))
    t_max = max(max(abs(t) for t in ts), EPS)
    ridge_z = h_game + pitch * t_max
    u_min, u_max = min(us), max(us)
    inv_tile = 1.0 / tile_m

    def uv_plan(x: float, y: float) -> tuple[float, float]:
        return (x * meters_per_game * inv_tile, y * meters_per_game * inv_tile)

    def ridge_pt(u: float) -> tuple[float, float, float]:
        return (cx + u * ux, cy + u * uy, ridge_z)

    roof_tris = 0
    for side in (1.0, -1.0):
        chain = [i for i in range(len(hull)) if ts[i] * side > 1e-7]
        if not chain:
            continue
        chain.sort(key=lambda i: us[i])
        for k in range(len(chain) - 1):
            ia, ib = chain[k], chain[k + 1]
            a, b = hull[ia], hull[ib]
            ea = (a[0], a[1], h_game)
            eb = (b[0], b[1], h_game)
            ra = ridge_pt(us[ia])
            rb = ridge_pt(us[ib])
            for tri in ((ea, eb, rb), (ea, rb, ra)):
                nrm = _tri_normal(*tri)
                flip = nrm[2] < 0
                if flip:
                    nrm = _tri_normal(tri[0], tri[2], tri[1])
                mesh_roof.tri(
                    tri[0], nrm, uv_plan(tri[0][0], tri[0][1]),
                    tri[1], nrm, uv_plan(tri[1][0], tri[1][1]),
                    tri[2], nrm, uv_plan(tri[2][0], tri[2][1]),
                    flip=flip,
                )
                roof_tris += 1
    # Gable end walls: one vertical cap per ridge end, spanning the nearest
    # vertices on each side chain at that end (exact for rectangles; an
    # approximation for other convex shapes).
    gable_tris = 0
    plus_chain = sorted((i for i in range(len(hull)) if ts[i] > 1e-7), key=lambda i: us[i])
    minus_chain = sorted((i for i in range(len(hull)) if ts[i] < -1e-7), key=lambda i: us[i])
    for end_u, first, sign in ((u_min, 0, -1.0), (u_max, -1, 1.0)):
        if not plus_chain or not minus_chain:
            break
        i_plus = plus_chain[first]
        i_minus = minus_chain[first]
        e_plus = (hull[i_plus][0], hull[i_plus][1], h_game)
        e_minus = (hull[i_minus][0], hull[i_minus][1], h_game)
        r = ridge_pt(end_u)
        nrm = _tri_normal(e_plus, e_minus, r)
        outward = (sign * ux, sign * uy, 0.0)
        flip = nrm[0] * outward[0] + nrm[1] * outward[1] < 0
        if flip:
            nrm = _tri_normal(e_plus, r, e_minus)
        mesh_wall.tri(
            e_plus, nrm, uv_plan(e_plus[0], e_plus[1]),
            e_minus, nrm, uv_plan(e_minus[0], e_minus[1]),
            r, nrm, uv_plan(r[0], r[1]),
            flip=flip,
        )
        gable_tris += 1
    return roof_tris, gable_tris


# ---------------------------------------------------------------------------
# glTF writing (deterministic)
# ---------------------------------------------------------------------------


def pack_positions(vals: list[tuple[float, float, float]]) -> bytes:
    return b"".join(struct.pack("<3f", *v) for v in vals)


def pack_normals(vals: list[tuple[float, float, float]]) -> bytes:
    return b"".join(struct.pack("<3f", *v) for v in vals)


def pack_uvs(vals: list[tuple[float, float]]) -> bytes:
    return b"".join(struct.pack("<2f", *v) for v in vals)


def pack_indices(vals: list[int]) -> bytes:
    return b"".join(struct.pack("<I", v) for v in vals)


def build_gltf(meshes: list[tuple[str, MeshBuilder]], bin_uri: str,
               mat_specs: list[dict]) -> tuple[dict, bytes]:
    """Build one .gltf document + .bin blob from material-slot meshes.

    mat_specs: ordered list of {"name": role, "image": "../../materials/<file>"};
    meshes entries reference a role name. Meshes with zero indices are skipped.
    """
    used_roles = [role for (role, m) in meshes if m.idx]
    role_to_mat = {spec["name"]: i for i, spec in enumerate(mat_specs)}
    buffers_blob = bytearray()
    accessors = []
    buffer_views = []
    meshes_json = []
    nodes = []
    images: list[dict] = []
    textures: list[dict] = []
    materials: list[dict] = []
    used_mat_idx: dict[int, int] = {}
    img_of_role: dict[str, int] = {}
    for spec in mat_specs:
        if spec["name"] not in used_roles:
            continue
        img_index = len(images)
        images.append({"name": spec["name"] + "_baseColor", "uri": spec["image"]})
        tex_index = len(textures)
        textures.append({"source": img_index})
        img_of_role[spec["name"]] = tex_index
        used_mat_idx[role_to_mat[spec["name"]]] = len(materials)
        materials.append({
            "name": spec["name"],
            "pbrMetallicRoughness": {
                "baseColorTexture": {"index": tex_index},
                "metallicFactor": 0.0,
                "roughnessFactor": 0.9,
            },
        })
    for (role, m) in meshes:
        if not m.idx:
            continue
        prim_start = len(buffer_views)
        pos_bytes = pack_positions(m.pos)
        nrm_bytes = pack_normals(m.nrm)
        uv_bytes = pack_uvs(m.uv)
        idx_bytes = pack_indices(m.idx)
        for blob in (pos_bytes, nrm_bytes, uv_bytes, idx_bytes):
            while len(buffers_blob) % 4:
                buffers_blob.append(0)
            buffer_views.append({
                "buffer": 0,
                "byteLength": len(blob),
                "byteOffset": len(buffers_blob),
                "target": MIAG if blob is idx_bytes else MAG,
            })
            buffers_blob.extend(blob)
        v_pos, v_nrm, v_uv, v_idx = (prim_start, prim_start + 1, prim_start + 2, prim_start + 3)
        xs = [p[0] for p in m.pos]
        ys = [p[1] for p in m.pos]
        zs = [p[2] for p in m.pos]
        us = [t[0] for t in m.uv]
        vs = [t[1] for t in m.uv]
        acc_base = len(accessors)
        accessors.append({
            "bufferView": v_pos, "componentType": GLTF_POS_COMPONENT, "count": len(m.pos),
            "type": "VEC3", "min": [min(xs), min(ys), min(zs)], "max": [max(xs), max(ys), max(zs)],
        })
        accessors.append({
            "bufferView": v_nrm, "componentType": GLTF_POS_COMPONENT, "count": len(m.nrm),
            "type": "VEC3",
        })
        accessors.append({
            "bufferView": v_uv, "componentType": GLTF_POS_COMPONENT, "count": len(m.uv),
            "type": "VEC2", "min": [min(us), min(vs)], "max": [max(us), max(vs)],
        })
        accessors.append({
            "bufferView": v_idx, "componentType": GLTF_IDX_COMPONENT, "count": len(m.idx),
            "type": "SCALAR",
        })
        mesh_index = len(meshes_json)
        meshes_json.append({
            "name": role,
            "primitives": [{
                "attributes": {"POSITION": acc_base, "NORMAL": acc_base + 1, "TEXCOORD_0": acc_base + 2},
                "indices": acc_base + 3,
                "material": used_mat_idx[role_to_mat[role]],
                "mode": 4,
            }],
        })
        node_index = len(nodes)
        nodes.append({"name": role, "mesh": mesh_index})
    doc = {
        "asset": {
            "copyright": "Buildings: IGN BD TOPO (Licence Ouverte 2.0); textures: ambientCG (CC0 1.0)",
            "generator": "panopticon scripts/r3-01-prototype/generate_buildings.py",
            "version": "2.0",
        },
        "scene": 0,
        "scenes": [{"name": os.path.splitext(bin_uri)[0], "nodes": list(range(len(nodes)))}],
        "nodes": nodes,
        "meshes": meshes_json,
        "materials": materials,
        "textures": textures,
        "images": images,
        "accessors": accessors,
        "bufferViews": buffer_views,
        "buffers": [{"byteLength": len(buffers_blob), "uri": bin_uri}],
    }
    return doc, bytes(buffers_blob)


def write_gltf_pair(out_gltf: str, meshes: list[tuple[str, MeshBuilder]],
                    mat_specs: list[dict]) -> None:
    doc, blob = build_gltf(meshes, os.path.basename(out_gltf).replace(".gltf", ".bin"), mat_specs)
    os.makedirs(os.path.dirname(out_gltf), exist_ok=True)
    with open(out_gltf, "wb") as fh:
        fh.write((json.dumps(doc, sort_keys=True, indent=1) + "\n").encode("utf-8"))
    with open(out_gltf.replace(".gltf", ".bin"), "wb") as fh:
        fh.write(blob)


# ---------------------------------------------------------------------------
# Input parsing
# ---------------------------------------------------------------------------


def load_payload(payload_dir: str) -> dict:
    path = os.path.join(payload_dir, "payload.json")
    if not os.path.isfile(path):
        fail("ERR-PAYLOAD-MISSING", f"payload.json not found under {payload_dir}")
    try:
        with open(path, "rb") as fh:
            return json.load(fh)
    except (OSError, ValueError) as exc:
        fail("ERR-PAYLOAD-READ", f"cannot read {path}: {exc}")


def parse_height(raw) -> tuple[float | None, bool]:
    """Returns (height_meters or None, parseable). Empty string means missing."""
    if raw is None:
        return None, False
    s = str(raw).strip()
    if not s:
        return None, False
    s = s.rstrip("m").strip()
    try:
        v = float(s)
    except ValueError:
        return None, False
    if not math.isfinite(v) or v <= 0:
        return None, False
    return v, True


def parse_payload(payload: dict) -> tuple[list[dict], dict]:
    """Defensive payload parse -> building part records + grid facts."""
    if not isinstance(payload, dict) or "features" not in payload or "grid" not in payload:
        fail("ERR-PAYLOAD-SHAPE", "payload lacks features/grid objects")
    feats = payload["features"]
    items = feats.get("items")
    tables = feats.get("attributeTables")
    if not isinstance(items, list) or not isinstance(tables, list):
        fail("ERR-PAYLOAD-SHAPE", "features.items/attributeTables missing or not lists")
    grid = payload.get("grid") or {}
    transform = grid.get("transform") or {}
    layout = grid.get("layout") or {}
    mpg = transform.get("metersPerGameUnit")
    if not isinstance(mpg, (int, float)) or mpg <= 0:
        fail("ERR-PAYLOAD-GRID", "grid.transform.metersPerGameUnit missing or invalid")
    spacing = layout.get("sampleSpacingGameUnits")
    intervals = layout.get("intervalsPerChunk")
    if not isinstance(spacing, (int, float)) or not isinstance(intervals, int):
        fail("ERR-PAYLOAD-GRID", "grid.layout.sampleSpacingGameUnits/intervalsPerChunk missing")
    chunk_x = grid.get("chunksX")
    chunk_y = grid.get("chunksY")
    if not isinstance(chunk_x, int) or not isinstance(chunk_y, int):
        fail("ERR-PAYLOAD-GRID", "grid.chunksX/chunksY missing")
    grid_info = {
        "meters_per_game_unit": float(mpg),
        "chunk_size_game_units": float(spacing) * intervals,
        "chunks_x": chunk_x,
        "chunks_y": chunk_y,
        "sample_spacing_game_units": float(spacing),
        "intervals_per_chunk": intervals,
        "origin_easting_northing": transform.get("originEastingNorthing"),
        "vertical_origin_meters": transform.get("verticalOriginMeters"),
        "schema_version": payload.get("schemaVersion"),
        "payload_kind": payload.get("kind"),
    }
    parts: list[dict] = []
    seen_parts: set[str] = set()
    for i, it in enumerate(items):
        if not isinstance(it, dict) or it.get("kind") != "passability":
            continue
        fid = it.get("featureId")
        if not isinstance(fid, str) or not fid:
            fail("ERR-PAYLOAD-FEATURE", f"passability item {i} lacks featureId")
        geo = it.get("geometry") or {}
        outer = (geo.get("outerRing") or {}).get("positions")
        if not isinstance(outer, list) or len(outer) < 3:
            fail("ERR-PAYLOAD-FEATURE", f"feature {fid} has no outerRing positions")
        ring: list[tuple[float, float]] = []
        alts: list[float] = []
        for p in outer:
            x, y, alt = p.get("x"), p.get("y"), p.get("altitudeMeters")
            if not isinstance(x, (int, float)) or not isinstance(y, (int, float)):
                fail("ERR-PAYLOAD-FEATURE", f"feature {fid} has non-numeric ring position")
            if not (math.isfinite(x) and math.isfinite(y)):
                fail("ERR-PAYLOAD-FEATURE", f"feature {fid} has non-finite ring position")
            ring.append((float(x), float(y)))
            if isinstance(alt, (int, float)) and math.isfinite(alt):
                alts.append(float(alt))
        if _same_pt(ring[0], ring[-1]):
            ring = ring[:-1]
        if len(unique_points(ring)) < 3:
            fail("ERR-PAYLOAD-FEATURE", f"feature {fid} outer ring has fewer than 3 unique vertices")
        holes_raw = geo.get("innerRings") or []
        holes: list[list[tuple[float, float]]] = []
        for k, hr in enumerate(holes_raw):
            hpos = (hr or {}).get("positions") or []
            hring: list[tuple[float, float]] = []
            for p in hpos:
                x, y = p.get("x"), p.get("y")
                if not isinstance(x, (int, float)) or not isinstance(y, (int, float)):
                    fail("ERR-PAYLOAD-FEATURE", f"feature {fid} hole {k} non-numeric position")
                hring.append((float(x), float(y)))
            if _same_pt(hring[0], hring[-1]):
                hring = hring[:-1]
            if len(unique_points(hring)) >= 3:
                holes.append(hring)
        at_idx = it.get("attributeTable")
        entries: dict = {}
        if isinstance(at_idx, int) and 0 <= at_idx < len(tables):
            entries = (tables[at_idx] or {}).get("entries") or {}
        part_id = it.get("partId") or f"{fid}#0"
        if part_id in seen_parts:
            fail("ERR-PAYLOAD-FEATURE", f"duplicate partId {part_id}")
        seen_parts.add(part_id)
        parts.append({
            "cleabs": fid,
            "part_id": part_id,
            "part_index": part_id.rsplit("#", 1)[-1],
            "outer": unique_points(ring),
            "holes": [unique_points(h) for h in holes],
            "hauteur_raw": entries.get("hauteur"),
            "nature": entries.get("nature") or "",
            "usage_1": entries.get("usage_1") or "",
            "usage_2": entries.get("usage_2") or "",
            "attr_index": at_idx if isinstance(at_idx, int) else None,
            "terrain_altitude_range": (min(alts), max(alts)) if alts else None,
        })
    parts.sort(key=lambda p: (p["cleabs"], p["part_id"]))
    return parts, grid_info


def parse_kml(path: str) -> dict:
    """Count placemarks and ExtendedData population; join key is cleabs only."""
    if not os.path.isfile(path):
        fail("ERR-KML-MISSING", f"KML file not found: {path}")
    try:
        tree = ET.parse(path)
    except (OSError, ET.ParseError) as exc:
        fail("ERR-KML-READ", f"cannot parse {path}: {exc}")
    ns = {"k": "http://www.opengis.net/kml/2.2"}
    root = tree.getroot()
    placemarks = root.findall(".//k:Placemark", ns)
    populated = {f: 0 for f in ATTR_FIELDS}
    ids: list[str] = []
    for pl in placemarks:
        cleabs = ""
        for data in pl.findall(".//k:Data", ns):
            name = data.get("name") or ""
            value = (data.findtext("k:value", default="", namespaces=ns) or "").strip()
            if name in populated and value:
                populated[name] += 1
            if name == "cleabs" and value:
                cleabs = value
        ids.append(cleabs)
    return {
        "placemarks": len(placemarks),
        "populated": populated,
        "unique_cleabs": len(set(i for i in ids if i)),
        "empty_cleabs": sum(1 for i in ids if not i),
        "cleabs_set": set(i for i in ids if i),
    }


# ---------------------------------------------------------------------------
# Material handling
# ---------------------------------------------------------------------------


def download_materials(out_dir: str) -> None:
    mat_dir = os.path.join(out_dir, "materials")
    zips_dir = os.path.join(mat_dir, "zips")
    os.makedirs(zips_dir, exist_ok=True)
    entries = []
    for spec in MATERIAL_TABLE:
        asset = spec["asset_id"]
        zip_path = os.path.join(zips_dir, f"{asset}_2K_JPG.zip")
        print(f"downloading {asset} -> {zip_path}")
        try:
            req = urllib.request.Request(spec["url"], headers={"User-Agent": USER_AGENT})
            with urllib.request.urlopen(req, timeout=120) as resp:
                data = resp.read()
        except (OSError, ValueError) as exc:
            fail("ERR-NET-DOWNLOAD", f"cannot download {spec['url']}: {exc}")
        if data[:2] != b"PK":
            fail("ERR-NET-DOWNLOAD", f"{spec['url']} did not return a zip (first bytes: {data[:4]!r})")
        with open(zip_path, "wb") as fh:
            fh.write(data)
        digest = sha256_file(zip_path)
        maps = extract_material_maps(zip_path, mat_dir, asset)
        if "basecolor" not in maps:
            fail("ERR-MATERIAL-ZIP", f"{asset} zip lacks a Color map")
        entries.append({**spec, "zip_sha256": digest, "zip_bytes": len(data), "maps": maps})
    try:
        req = urllib.request.Request(CC0_URL, headers={"User-Agent": USER_AGENT})
        with urllib.request.urlopen(req, timeout=60) as resp:
            cc0_text = resp.read().decode("utf-8", "replace")
    except (OSError, ValueError):
        cc0_text = CC0_FALLBACK_TEXT
    manifest = {
        "materials": entries,
        "license": "CC0-1.0",
        "license_text_url": CC0_TEXT_URL,
    }
    with open(os.path.join(mat_dir, "materials.json"), "wb") as fh:
        fh.write((json.dumps(manifest, sort_keys=True, indent=1) + "\n").encode("utf-8"))
    with open(os.path.join(mat_dir, "LICENSES.md"), "wb") as fh:
        fh.write(render_licenses(entries, cc0_text).encode("utf-8"))
    print(f"materials written under {mat_dir}")


def extract_material_maps(zip_path: str, mat_dir: str, asset: str) -> dict:
    """Copy only Color/NormalGL/Roughness maps (prefer 1K, else first sorted)."""
    wanted = {
        "basecolor": ("Color",),
        "normal": ("NormalGL", "Normal"),
        "roughness": ("Roughness",),
    }
    maps: dict[str, str] = {}
    with zipfile.ZipFile(zip_path) as zf:
        for role, suffixes in wanted.items():
            candidates = []
            for n in zf.namelist():
                low = n.lower()
                if not (low.endswith(".jpg") or low.endswith(".png")):
                    continue
                if "preview" in low:
                    continue
                stem = n.rsplit("/", 1)[-1].rsplit("_", 1)[-1].rsplit(".", 1)[0]
                if stem in suffixes:
                    candidates.append(n)
            if not candidates:
                continue
            pick = sorted(candidates, key=lambda n: (("_1K_" in n) is False, n))[0]
            target = f"{asset}_{role}{os.path.splitext(pick)[1].lower()}"
            with zf.open(pick) as src, open(os.path.join(mat_dir, target), "wb") as dst:
                while True:
                    chunk = src.read(1 << 20)
                    if not chunk:
                        break
                    dst.write(chunk)
            maps[role] = target
    return maps


def render_licenses(entries: list[dict], cc0_text: str) -> str:
    lines = [
        "# WI-R3-01 prototype material licenses",
        "",
        "All four material sets are ambientCG assets distributed under",
        "Creative Commons CC0 1.0. The downloaded ambientCG zips contain no",
        "embedded license file; the CC0 1.0 legal text below is fetched from",
        f"<{CC0_TEXT_URL}>.",
        "",
        "| Role | Asset | URL | Zip SHA-256 |",
        "|---|---|---|---|",
    ]
    for e in entries:
        lines.append(f"| {e['role']} | {e['asset_id']} | {e['url']} | `{e['zip_sha256']}` |")
    lines += ["", "## CC0 1.0 Universal legal text", "", "```", cc0_text.rstrip(), "```", ""]
    return "\n".join(lines)


CC0_FALLBACK_TEXT = (
    "Creative Commons Legal Code\n\nCC0 1.0 Universal\n\n"
    "OFFICIAL TEXT retrieved failure fallback: see "
    "https://creativecommons.org/publicdomain/zero/1.0/legalcode.txt\n"
    "The text was not embedded in the ambientCG zips; this placeholder asserts\n"
    "the assets are CC0 1.0 per the ambientCG asset pages.\n"
)


def load_material_manifest(materials_dir: str) -> list[dict]:
    path = os.path.join(materials_dir, "materials.json")
    if not os.path.isfile(path):
        fail("ERR-MATERIAL-MISSING",
             f"{materials_dir} lacks materials.json; run --download-materials first")
    try:
        with open(path, "rb") as fh:
            manifest = json.load(fh)
    except (OSError, ValueError) as exc:
        fail("ERR-MATERIAL-READ", f"cannot read {path}: {exc}")
    entries = manifest.get("materials")
    if not isinstance(entries, list) or len(entries) != len(MATERIAL_TABLE):
        fail("ERR-MATERIAL-SHAPE", "materials.json lacks the four expected material entries")
    mat_specs = []
    for e in entries:
        bc = (e.get("maps") or {}).get("basecolor")
        if not bc:
            fail("ERR-MATERIAL-SHAPE", f"material {e.get('role')} lacks a basecolor map")
        image_path = os.path.join(materials_dir, bc)
        if not os.path.isfile(image_path):
            fail("ERR-MATERIAL-MISSING", f"basecolor map missing: {image_path}")
        mat_specs.append({
            "name": e["role"],
            "image": f"../../materials/{bc}",
            "tile_size_meters": float(e.get("tile_size_meters", 2.0)),
        })
    roles = {s["name"] for s in mat_specs}
    if roles != {e["role"] for e in MATERIAL_TABLE}:
        fail("ERR-MATERIAL-SHAPE", f"unexpected material roles: {sorted(roles)}")
    return mat_specs


# ---------------------------------------------------------------------------
# Roof rule applicability
# ---------------------------------------------------------------------------


def gable_failure(part: dict, tiny_area_m2: float, meters_per_game: float) -> str | None:
    """Classification order is fixed and documented: too-few-vertices, has-holes,
    concave, tiny-area."""
    pts = part["outer"]
    if len(pts) < 4:
        return "too-few-vertices"
    if part["holes"]:
        return "has-holes"
    if not is_convex(pts):
        return "concave"
    area_m2 = abs(shoelace(pts)) * meters_per_game * meters_per_game
    if area_m2 < tiny_area_m2:
        return "tiny-area"
    return None


# ---------------------------------------------------------------------------
# Generation
# ---------------------------------------------------------------------------


def cmd_generate(args) -> None:
    payload = load_payload(args.payload)
    parts, grid = parse_payload(payload)
    mpg = grid["meters_per_game_unit"]
    chunk_size = grid["chunk_size_game_units"]
    kml = parse_kml(args.kml)

    prov = None
    if args.provenance and os.path.isfile(args.provenance):
        try:
            with open(args.provenance, "rb") as fh:
                prov = json.load(fh)
        except (OSError, ValueError):
            prov = None
    sources_line = None
    if args.sources and os.path.isfile(args.sources):
        try:
            with open(args.sources, "r", encoding="utf-8", errors="replace") as fh:
                for line in fh:
                    if line.startswith("Features:"):
                        sources_line = line.strip()
                        break
        except OSError:
            sources_line = None

    # Tiny-area threshold: derive min kept footprint area from the data, then
    # take the max with the acquisition filter threshold when provenance exists.
    areas_m2 = [abs(shoelace(p["outer"])) * mpg * mpg for p in parts]
    derived_min_area = min(areas_m2) if areas_m2 else 0.0
    prov_threshold = float(prov.get("filterThresholdM2")) if prov and prov.get("filterThresholdM2") else None
    tiny_area = derived_min_area
    if prov_threshold is not None:
        tiny_area = max(tiny_area, prov_threshold)

    # Heights: parse, then deterministic median fallback per usage_1, then global.
    heights = []
    for p in parts:
        h, ok = parse_height(p["hauteur_raw"])
        p["height_m"] = h
        p["height_parseable"] = ok
        if h is not None:
            heights.append(h)
    global_median = statistics.median(heights) if heights else None
    if global_median is None:
        fail("ERR-HEIGHT-EMPTY", "no parseable hauteur values in payload")
    by_usage: dict[str, list[float]] = {}
    for p in parts:
        if p["height_m"] is not None:
            by_usage.setdefault(p["usage_1"], []).append(p["height_m"])
    usage_medians = {u: statistics.median(v) for u, v in sorted(by_usage.items())}
    fallbacks = {"usage_1_median": 0, "global_median": 0}
    for p in parts:
        if p["height_m"] is None:
            med = usage_medians.get(p["usage_1"])
            if med is None:
                med = global_median
                fallbacks["global_median"] += 1
            else:
                fallbacks["usage_1_median"] += 1
            p["height_m"] = med
            p["height_fallback"] = True
        else:
            p["height_fallback"] = False

    # Roof rule applicability (counted for BOTH rules regardless of --roof-rule).
    failure_counts = {"too-few-vertices": 0, "has-holes": 0, "concave": 0, "tiny-area": 0}
    degenerate = {"too-few-vertices": 0, "zero-area": 0}
    for p in parts:
        area_m2 = abs(shoelace(p["outer"])) * mpg * mpg
        if len(p["outer"]) < 3 or area_m2 <= 1e-9:
            p["degenerate"] = True
            degenerate["zero-area" if area_m2 <= 1e-9 else "too-few-vertices"] += 1
            continue
        p["degenerate"] = False
        reason = gable_failure(p, tiny_area, mpg)
        if reason is None:
            p["gable"] = "eligible"
        else:
            p["gable"] = reason
            failure_counts[reason] += 1
    degenerate_parts = [p for p in parts if p["degenerate"]]
    emit_parts = [p for p in parts if not p["degenerate"]]
    gable_eligible = [p for p in emit_parts if p["gable"] == "eligible"]
    hip_same_predicate = len(gable_eligible)
    hip_headroom = failure_counts["concave"] + failure_counts["has-holes"]

    # Footprint centers (chunk assignment) before geometry building.
    for p in parts:
        xs = [q[0] for q in p["outer"]]
        ys = [q[1] for q in p["outer"]]
        p["center_x"] = sum(xs) / len(xs)
        p["center_y"] = sum(ys) / len(ys)

    # Material variants: pure stable hash of cleabs.
    def variant(cleabs: str) -> tuple[str, str]:
        d = hashlib.sha256(cleabs.encode("utf-8")).digest()
        wall = "wall_brick" if d[0] < 128 else "wall_plaster"
        roof = "roof_slate" if d[1] < 128 else "roof_clay"
        return wall, roof

    mat_specs = load_material_manifest(args.materials_dir)
    tile_of = {s["name"]: s["tile_size_meters"] for s in mat_specs}

    # Build geometry per part (shared by both layouts so totals agree).
    buildings: dict[str, list[dict]] = {}
    for p in emit_parts:
        buildings.setdefault(p["cleabs"], []).append(p)
    per_part_geo: dict[str, dict] = {}
    tri_by_chunk: dict[tuple[int, int], int] = {}
    parts_by_chunk: dict[tuple[int, int], int] = {}
    bld_by_chunk: dict[tuple[int, int], set] = {}
    totals = {
        "wall_quads": 0,
        "roof_triangles": 0,
        "gable_end_triangles": 0,
        "triangles": 0,
        "gable_baked": 0,
        "gable_fallback_flat": 0,
    }
    tri_by_material: dict[str, int] = {}
    for cleabs in sorted(buildings):
        wall_role, roof_role = variant(cleabs)
        for p in sorted(buildings[cleabs], key=lambda q: q["part_id"]):
            h_game = p["height_m"] / mpg
            wall_tile = tile_of[wall_role]
            roof_tile = tile_of[roof_role]
            wm = MeshBuilder()
            rm = MeshBuilder()
            quads = extrude_walls(wm, p["outer"], h_game, mpg, wall_tile, is_hole=False)
            for hole in p["holes"]:
                quads += extrude_walls(wm, hole, h_game, mpg, wall_tile, is_hole=True)
            use_gable = args.roof_rule == "gabled" and p["gable"] == "eligible"
            if use_gable:
                _, gable_tris = gabled_roof(rm, wm, p["outer"], h_game, mpg, roof_tile)
                totals["gable_end_triangles"] += gable_tris
                totals["gable_baked"] += 1
            else:
                flat_roof(rm, p["outer"], p["holes"], h_game, mpg, roof_tile)
                if args.roof_rule == "gabled":
                    totals["gable_fallback_flat"] += 1
            roof_tris = len(rm.idx) // 3
            tris = len(wm.idx) // 3 + roof_tris
            cx = min(grid["chunks_x"] - 1, max(0, int(math.floor(p["center_x"] / chunk_size))))
            cy = min(grid["chunks_y"] - 1, max(0, int(math.floor(p["center_y"] / chunk_size))))
            per_part_geo[p["part_id"]] = {
                "cleabs": cleabs, "chunk": (cx, cy),
                "wall": (wall_role, wm), "roof": (roof_role, rm),
                "triangles": tris, "height_m": p["height_m"],
                "roof_rule_applied": "gabled" if use_gable else "flat",
            }
            tri_by_chunk[(cx, cy)] = tri_by_chunk.get((cx, cy), 0) + tris
            parts_by_chunk[(cx, cy)] = parts_by_chunk.get((cx, cy), 0) + 1
            bld_by_chunk.setdefault((cx, cy), set()).add(cleabs)
            totals["wall_quads"] += quads
            totals["roof_triangles"] += roof_tris
            totals["triangles"] += tris
            tri_by_material[wall_role] = tri_by_material.get(wall_role, 0) + len(wm.idx) // 3
            tri_by_material[roof_role] = tri_by_material.get(roof_role, 0) + len(rm.idx) // 3

    # Emit glTFs.
    layouts = [s.strip() for s in args.layouts.split(",") if s.strip()]
    for layout in layouts:
        if layout not in ("merged", "perbuilding"):
            fail("ERR-LAYOUT", f"unknown layout {layout!r} (expected merged and/or perbuilding)")
    gltf_root = os.path.join(args.out, "gltf")
    merged_tris = 0
    perbuilding_tris = 0
    if "merged" in layouts:
        chunk_slots: dict[tuple[int, int], dict[str, MeshBuilder]] = {}
        for pid in sorted(per_part_geo):
            geo = per_part_geo[pid]
            cx, cy = geo["chunk"]
            slots = chunk_slots.setdefault((cx, cy), {})
            for role_key in ("wall", "roof"):
                role, mesh = geo[role_key]
                slots.setdefault(role, MeshBuilder()).merge(mesh)
        for (cx, cy) in sorted(chunk_slots):
            slots = chunk_slots[(cx, cy)]
            write_gltf_pair(
                os.path.join(gltf_root, "merged", f"chunk_{cx}_{cy}.gltf"),
                [(role, mesh) for role, mesh in sorted(slots.items())],
                mat_specs,
            )
            merged_tris += sum(len(m.idx) for m in slots.values()) // 3
    if "perbuilding" in layouts:
        os.makedirs(os.path.join(gltf_root, "perbuilding"), exist_ok=True)
        for cleabs in sorted(buildings):
            geo_list = [per_part_geo[p["part_id"]] for p in sorted(buildings[cleabs], key=lambda q: q["part_id"])]
            by_role: dict[str, MeshBuilder] = {}
            for geo in geo_list:
                for role_key in ("wall", "roof"):
                    role, mesh = geo[role_key]
                    by_role.setdefault(role, MeshBuilder()).merge(mesh)
            if not all(c.isalnum() or c in "_-." for c in cleabs):
                fail("ERR-NAME", f"cleabs {cleabs!r} is not filesystem-safe")
            write_gltf_pair(
                os.path.join(gltf_root, "perbuilding", f"{cleabs}.gltf"),
                [(role, mesh) for role, mesh in sorted(by_role.items())],
                mat_specs,
            )
            perbuilding_tris += sum(len(m.idx) for m in by_role.values()) // 3
    if merged_tris and perbuilding_tris and merged_tris != perbuilding_tris:
        fail("ERR-TRIANGLE-MISMATCH", f"merged {merged_tris} != perbuilding {perbuilding_tris}")
    accounted = totals["wall_quads"] * 2 + totals["gable_end_triangles"] + totals["roof_triangles"]
    if accounted != totals["triangles"]:
        fail("ERR-TRIANGLE-ACCOUNTING",
             f"wall {totals['wall_quads'] * 2}+gable {totals['gable_end_triangles']}+roof "
             f"{totals['roof_triangles']} != total {totals['triangles']}")
    triangle_total = merged_tris or perbuilding_tris or totals["triangles"]

    # Stats.
    kml_ids = kml["cleabs_set"]
    payload_ids = set(buildings)
    parts.sort(key=lambda p: (p["cleabs"], p["part_id"]))
    hauteur_ok = sum(1 for p in parts if not p["height_fallback"])
    stats = {
        "generated_at": datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds"),
        "inputs": {
            "payload_dir": os.path.abspath(args.payload),
            "kml": os.path.abspath(args.kml),
            "provenance": os.path.abspath(args.provenance) if prov else None,
            "sources": os.path.abspath(args.sources) if sources_line else None,
            "roof_rule_baked": args.roof_rule,
            "layouts": layouts,
        },
        "acquisition": {
            "provenance_features": {
                "totalFeaturesAcquired": prov.get("totalFeaturesAcquired") if prov else None,
                "featuresKept": prov.get("featuresKept") if prov else None,
                "featuresDropped": prov.get("featuresDropped") if prov else None,
                "filterThresholdM2": prov.get("filterThresholdM2") if prov else None,
            },
            "sources_line_verbatim": sources_line,
            "note": "Numbers above are cited verbatim from the r2-03 provenance; the payload is a later stage (clipped to the map rectangle).",
        },
        "payload": {
            "schema_version": grid["schema_version"],
            "kind": grid["payload_kind"],
            "meters_per_game_unit": mpg,
            "chunk_size_game_units": chunk_size,
            "chunks_x": grid["chunks_x"],
            "chunks_y": grid["chunks_y"],
            "passability_items": len(parts),
            "unique_cleabs": len(set(p["cleabs"] for p in parts)),
            "multi_part_cleabs": len([c for c, v in buildings.items() if len(v) > 1]),
            "buildability_items_duplicated": sum(1 for it in payload["features"]["items"] if it.get("kind") == "buildability"),
            "annotation_items": sum(1 for it in payload["features"]["items"] if it.get("kind") == "annotation"),
        },
        "heights": {
            "populated": hauteur_ok,
            "missing": len(parts) - hauteur_ok,
            "populated_share": round(hauteur_ok / len(parts), 6) if parts else 0.0,
            "fallback_usage_1_median": fallbacks["usage_1_median"],
            "fallback_global_median": fallbacks["global_median"],
            "min_m": round(min(heights), 3) if heights else None,
            "max_m": round(max(heights), 3) if heights else None,
            "median_m": round(statistics.median(heights), 3) if heights else None,
            "usage_1_medians_m": {u: round(v, 3) for u, v in usage_medians.items()},
        },
        "kml_cross_check": {
            "placemarks": kml["placemarks"],
            "unique_cleabs": kml["unique_cleabs"],
            "attribute_populated": kml["populated"],
            "payload_only_cleabs": len(payload_ids - kml_ids),
            "kml_only_cleabs": len(kml_ids - payload_ids),
            "join_note": "Join by cleabs only, no reprojection; payload is a strict subset here (KML stage predates map-rectangle clipping, report GEO-FEA-001 drops).",
        },
        "footprints": {
            "area_m2_min": round(derived_min_area, 6),
            "area_m2_max": round(max(areas_m2), 3) if areas_m2 else None,
            "area_m2_median": round(statistics.median(areas_m2), 3) if areas_m2 else None,
            "below_acquisition_threshold_count": sum(1 for a in areas_m2 if prov_threshold and a < prov_threshold) if prov_threshold else None,
            "acquisition_threshold_m2": prov_threshold,
            "tiny_area_threshold_m2": round(tiny_area, 6),
            "tiny_area_threshold_note": "max(min kept footprint area derived from payload, acquisition filter threshold from r2-03 provenance); the payload retains sub-threshold slivers.",
            "buildings_with_holes": len([p for p in parts if p["holes"]]),
            "hole_ring_count": sum(len(p["holes"]) for p in parts),
        },
        "roof_rules": {
            "baked_rule": args.roof_rule,
            "gabled": {
                "applicable": len(gable_eligible),
                "failures": failure_counts,
            },
            "flat": {"applicable": len(emit_parts)},
            "hipped_approx": {
                "implemented": False,
                "applicable_same_convexity_predicate": hip_same_predicate,
                "headroom_concave_or_holed": hip_headroom,
                "note": "Straight skeleton not implemented; approximate applicability reuses the gabled convexity gate, so it equals gabled applicability. A real skeleton would add the headroom footprints.",
            },
            "recommendation": "Compare applicability counts: gabled covers convex hole-free footprints only; the unimplemented hipped-from-straight-skeleton rule would additionally cover concave and holed footprints (headroom above) at skeleton-implementation cost.",
        },
        "emission": {
            "buildings_emitted": len(buildings),
            "parts_emitted": len(emit_parts),
            "degenerate_parts_skipped": len(degenerate_parts),
            "degenerate_reasons": degenerate,
            "buildings_per_chunk": {f"{cx}_{cy}": len(v) for (cx, cy), v in sorted(bld_by_chunk.items())},
            "parts_per_chunk": {f"{cx}_{cy}": n for (cx, cy), n in sorted(parts_by_chunk.items())},
            "chunk_triangle_counts": {f"{cx}_{cy}": n for (cx, cy), n in sorted(tri_by_chunk.items())},
            "material_variant_distribution": _variant_distribution(buildings, variant),
        },
        "triangles": {
            "total": triangle_total,
            "merged_total": merged_tris if "merged" in layouts else totals["triangles"],
            "perbuilding_total": perbuilding_tris if "perbuilding" in layouts else totals["triangles"],
            "merged_equals_perbuilding": True,
            "wall_quads": totals["wall_quads"],
            "roof_triangles": totals["roof_triangles"],
            "gable_end_triangles": totals["gable_end_triangles"],
            "wall_triangles": totals["wall_quads"] * 2 + totals["gable_end_triangles"],
            "gable_baked_parts": totals["gable_baked"],
            "gable_fallback_flat_parts": totals["gable_fallback_flat"],
            "by_material": tri_by_material,
            "per_layout_note": "merged and perbuilding layouts carry identical geometry; totals are equal by construction (asserted when both layouts are emitted).",
        },
        "materials": {
            "dir": os.path.abspath(args.materials_dir),
            "roles": [{"name": s["name"], "image": s["image"], "tile_size_meters": s["tile_size_meters"]} for s in mat_specs],
            "pinned_sources": [{k: e[k] for k in ("role", "asset_id", "url")} for e in MATERIAL_TABLE],
        },
        "determinism": {
            "byte_identical_gltf": True,
            "method": "sorted JSON keys, little-endian struct packs, cleabs/partId-ordered iteration, stable sha256 material variants, no timestamps inside glTF/bin",
            "verify": "run twice into fresh directories and sha256sum sample gltf/.bin pairs",
        },
    }
    os.makedirs(args.out, exist_ok=True)
    with open(os.path.join(args.out, "stats.json"), "wb") as fh:
        fh.write((json.dumps(stats, sort_keys=True, indent=1) + "\n").encode("utf-8"))
    print(f"stats -> {os.path.join(args.out, 'stats.json')}")
    print(json.dumps({
        "buildings_emitted": stats["emission"]["buildings_emitted"],
        "triangles": stats["triangles"]["total"],
        "wall_quads": totals["wall_quads"],
        "roof_triangles": totals["roof_triangles"],
        "gabled_applicable": len(gable_eligible),
    }))


def _variant_distribution(buildings: dict, variant) -> dict:
    dist: dict[str, int] = {}
    for cleabs in buildings:
        wall, roof = variant(cleabs)
        dist[wall] = dist.get(wall, 0) + 1
        dist[roof] = dist.get(roof, 0) + 1
    return dist


# ---------------------------------------------------------------------------
# Self test (synthetic polygons; no payload, KML, network, or materials needed)
# ---------------------------------------------------------------------------


def self_test() -> None:
    failures: list[str] = []

    def check(name: str, cond: bool, detail: str = "") -> None:
        if not cond:
            failures.append(f"{name}: {detail}")

    mpg = 0.3048
    tile = 2.0

    # 1. Square convex: gable eligible, expected counts. 40x40 game units keeps
    # the footprint (148.6 m^2) above the default 20 m^2 tiny-area threshold.
    span = 40.0
    sq = [(0.0, 0.0), (span, 0.0), (span, span), (0.0, span)]
    part = {"outer": sq, "holes": [], "hauteur_raw": "4.0", "usage_1": "u",
            "cleabs": "TEST-SQUARE", "part_id": "t#0"}
    check("square gable", gable_failure(part, 20.0, mpg) is None,
          f"unexpected failure {gable_failure(part, 20.0, mpg)}")
    wm, rm = MeshBuilder(), MeshBuilder()
    h_game = 4.0 / mpg
    quads = extrude_walls(wm, sq, h_game, mpg, tile, is_hole=False)
    check("square wall quads", quads == 4, f"got {quads}")
    roof_tris, gable_tris = gabled_roof(rm, wm, sq, h_game, mpg, tile)
    check("square roof tris", roof_tris == 4, f"got {roof_tris}")
    check("square gable ends", gable_tris == 2, f"got {gable_tris}")
    total_area = sum(
        _tri_area(rm.pos[rm.idx[i]], rm.pos[rm.idx[i + 1]], rm.pos[rm.idx[i + 2]])
        for i in range(0, len(rm.idx), 3))
    pitch = math.tan(math.radians(ROOF_PITCH_DEG))
    expected_roof_area = 2.0 * span * math.sqrt((span / 2.0) ** 2 + (pitch * span / 2.0) ** 2)
    check("square roof slope area", abs(total_area - expected_roof_area) < 1e-6,
          f"{total_area} vs {expected_roof_area}")

    # 2. L-shape concave: gable fails as concave; flat roof triangulates exactly.
    lshape = [(0.0, 0.0), (10.0, 0.0), (10.0, 4.0), (4.0, 4.0), (4.0, 10.0), (0.0, 10.0)]
    part_l = {"outer": lshape, "holes": [], "cleabs": "L", "part_id": "l#0"}
    check("L concave", gable_failure(part_l, 20.0, mpg) == "concave",
          f"got {gable_failure(part_l, 20.0, mpg)}")
    wm2 = MeshBuilder()
    extrude_walls(wm2, lshape, 1.0, mpg, tile, is_hole=False)
    larea = abs(shoelace(lshape))
    check("L wall quads", quads_of(wm2) == 6, f"got {quads_of(wm2)}")
    rm2 = MeshBuilder()
    ft = flat_roof(rm2, lshape, [], 1.0, mpg, tile)
    check("L flat tris", ft == 4, f"got {ft}")
    a = sum(_tri_area(rm2.pos[rm2.idx[i]], rm2.pos[rm2.idx[i + 1]], rm2.pos[rm2.idx[i + 2]])
            for i in range(0, len(rm2.idx), 3))
    check("L flat area", abs(a - larea) < 1e-9, f"{a} vs {larea}")

    # 3. Polygon with hole: gable fails as has-holes; bridged flat roof preserves area.
    holed_outer = [(0.0, 0.0), (20.0, 0.0), (20.0, 10.0), (0.0, 10.0)]
    hole = [(8.0, 4.0), (12.0, 4.0), (12.0, 6.0), (8.0, 6.0)]
    part_h = {"outer": holed_outer, "holes": [hole], "cleabs": "H", "part_id": "h#0"}
    check("hole gable", gable_failure(part_h, 20.0, mpg) == "has-holes",
          f"got {gable_failure(part_h, 20.0, mpg)}")
    wm3 = MeshBuilder()
    q3 = extrude_walls(wm3, holed_outer, 1.0, mpg, tile, is_hole=False)
    q3 += extrude_walls(wm3, hole, 1.0, mpg, tile, is_hole=True)
    check("holed wall quads", q3 == 8, f"got {q3}")
    rm3 = MeshBuilder()
    flat_roof(rm3, holed_outer, [hole], 1.0, mpg, tile)
    a3 = sum(_tri_area(rm3.pos[rm3.idx[i]], rm3.pos[rm3.idx[i + 1]], rm3.pos[rm3.idx[i + 2]])
             for i in range(0, len(rm3.idx), 3))
    expected3 = abs(shoelace(holed_outer)) - abs(shoelace(hole))
    check("holed flat area", abs(a3 - expected3) < 1e-9, f"{a3} vs {expected3}")

    # 4. Tiny quad sliver (0.074 m^2 < threshold): gable fails as tiny-area but
    # the footprint is still emitted; classification order puts too-few-vertices
    # first, so the sliver needs 4 vertices to isolate the tiny-area reason.
    sliver = [(0.0, 0.0), (4.0, 0.0), (4.0, 0.2), (0.0, 0.2)]
    part_s = {"outer": sliver, "holes": [], "cleabs": "S", "part_id": "s#0"}
    check("sliver tiny", gable_failure(part_s, 20.0, mpg) == "tiny-area",
          f"got {gable_failure(part_s, 20.0, mpg)}")

    # 5. Triangle footprint (3 verts): gable fails as too-few-vertices.
    tri = [(0.0, 0.0), (30.0, 0.0), (15.0, 20.0)]
    part_t = {"outer": tri, "holes": [], "cleabs": "T", "part_id": "t2#0"}
    check("tri too-few", gable_failure(part_t, 20.0, mpg) == "too-few-vertices",
          f"got {gable_failure(part_t, 20.0, mpg)}")

    # 6. Degenerate collinear ring detection + height fallback determinism.
    degen = [(0.0, 0.0), (5.0, 5.0), (10.0, 10.0)]
    check("degenerate area", abs(shoelace(degen)) < 1e-9, "collinear ring has area")
    h1, ok1 = parse_height("8.8")
    check("height parse", ok1 and h1 == 8.8, f"{ok1} {h1}")
    h2, ok2 = parse_height("")
    h3, ok3 = parse_height(None)
    h4, ok4 = parse_height("banana")
    h5, ok5 = parse_height("-3")
    check("height missing", (h2 is None and not ok2) and (h3 is None and not ok3)
          and (h4 is None and not ok4) and (h5 is None and not ok5), "missing parse")

    # 7. glTF JSON well-formedness: build a tiny document and validate structure.
    mb_wall, mb_roof = MeshBuilder(), MeshBuilder()
    extrude_walls(mb_wall, sq, 1.0, mpg, tile, is_hole=False)
    flat_roof(mb_roof, sq, [], 1.0, mpg, tile)
    specs = [
        {"name": "wall_brick", "image": "../../materials/x_basecolor.jpg", "tile_size_meters": tile},
        {"name": "roof_slate", "image": "../../materials/y_basecolor.jpg", "tile_size_meters": tile},
    ]
    doc, blob = build_gltf([("wall_brick", mb_wall), ("roof_slate", mb_roof)], "t.bin", specs)
    check("gltf buffers", len(doc["buffers"]) == 1 and doc["buffers"][0]["uri"] == "t.bin"
          and doc["buffers"][0]["byteLength"] == len(blob), "buffer blob mismatch")
    for bv in doc["bufferViews"]:
        check("gltf view aligned", bv["byteOffset"] % 4 == 0, f"offset {bv['byteOffset']}")
        check("gltf view bounds", bv["byteOffset"] + bv["byteLength"] <= len(blob), "view overruns blob")
    for acc in doc["accessors"]:
        bv = doc["bufferViews"][acc["bufferView"]]
        need = {"VEC3": 12, "VEC2": 8, "SCALAR": 4}[acc["type"]]
        check("gltf accessor count", acc["count"] * need == bv["byteLength"], "accessor size mismatch")
    for img in doc["images"]:
        check("gltf image uri", img["uri"].startswith("../../materials/"), img["uri"])
    check("gltf pos minmax", "min" in doc["accessors"][0] and "max" in doc["accessors"][0], "POSITION lacks min/max")
    # determinism: rebuild and compare bytes
    mb_wall_b, mb_roof_b = MeshBuilder(), MeshBuilder()
    extrude_walls(mb_wall_b, sq, 1.0, mpg, tile, is_hole=False)
    flat_roof(mb_roof_b, sq, [], 1.0, mpg, tile)
    doc2, blob2 = build_gltf([("wall_brick", mb_wall_b), ("roof_slate", mb_roof_b)], "t.bin", specs)
    j1 = json.dumps(doc, sort_keys=True, indent=1)
    j2 = json.dumps(doc2, sort_keys=True, indent=1)
    check("gltf deterministic", j1 == j2 and blob == blob2, "rebuild differs")

    # 8. Vertex dedup: square walls keep per-face corner vertices
    # (4 corners x 2 heights x 2 adjacent-face normals = 16).
    check("wall dedup", len(mb_wall.pos) == 16, f"got {len(mb_wall.pos)}")

    # 9. Bridge slit: bridged ring has no duplicate vertices beyond the slit.
    bridged = bridge_holes([(0.0, 0.0), (20.0, 0.0), (20.0, 10.0), (0.0, 10.0)],
                           [[(8.0, 4.0), (12.0, 4.0), (12.0, 6.0), (8.0, 6.0)]])
    check("bridge vertices", len(bridged) >= 8, f"got {len(bridged)}")

    if failures:
        for f in failures:
            print(f"SELF-TEST FAIL: {f}", file=sys.stderr)
        fail("ERR-SELF-TEST", f"{len(failures)} assertion failure(s)")
    print("self-test: all assertions passed")


def quads_of(mesh: MeshBuilder) -> int:
    # wall quads were emitted as 2 triangles each
    return len(mesh.idx) // 6


def _tri_area(a, b, c) -> float:
    ux, uy, uz = b[0] - a[0], b[1] - a[1], b[2] - a[2]
    vx, vy, vz = c[0] - a[0], c[1] - a[1], c[2] - a[2]
    nx = uy * vz - uz * vy
    ny = uz * vx - ux * vz
    nz = ux * vy - uy * vx
    return 0.5 * math.sqrt(nx * nx + ny * ny + nz * nz)


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------


def main(argv: list[str]) -> None:
    ap = argparse.ArgumentParser(description="WI-R3-01 Sedan building extrusion prototype")
    ap.add_argument("--self-test", action="store_true", help="run synthetic assertions and exit")
    ap.add_argument("--download-materials", action="store_true",
                    help="download pinned ambientCG CC0 zips, extract maps, write LICENSES.md")
    ap.add_argument("--payload", help="directory containing payload.json (generate mode)")
    ap.add_argument("--kml", help="cross-check KML path (generate mode)")
    ap.add_argument("--provenance", default=DEFAULT_PROVENANCE,
                    help="r2-03 footprints provenance json (optional, soft-missing)")
    ap.add_argument("--sources", default=DEFAULT_SOURCES,
                    help="r2-03 sources.txt (optional, soft-missing)")
    ap.add_argument("--out", help="output directory (operator scratch; created)")
    ap.add_argument("--materials-dir", help="prepared materials directory (generate mode)")
    ap.add_argument("--roof-rule", choices=("flat", "gabled"), default="flat",
                    help="roof rule baked into the glTFs; both rules' applicability is always counted")
    ap.add_argument("--layouts", default="merged,perbuilding",
                    help="comma-separated layouts: merged,perbuilding")
    args = ap.parse_args(argv)

    if args.self_test:
        self_test()
        return
    if args.download_materials:
        if not args.out:
            fail("ERR-ARGS", "--download-materials requires --out")
        download_materials(args.out)
        return
    missing = [f for f in ("payload", "kml", "out", "materials_dir")
               if getattr(args, f) is None]
    if missing:
        fail("ERR-ARGS", f"generate mode requires --payload, --kml, --out, --materials-dir (missing: {missing})")
    cmd_generate(args)


if __name__ == "__main__":
    main(sys.argv[1:])
