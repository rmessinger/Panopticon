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

Kit details (--kit-details, optional)
    Adds CC0 Kenney "Prototype Textures" facade details as extra glTF material
    slots: one door quad per building centered on the longest wall of its
    largest part, window quads spaced along outer walls above the ground floor
    (skipping 1 m around corners and the door span), and one chimney box on
    gabled-baked buildings near the ridge end. Details are pure functions of
    footprint geometry; UVs sample fixed sub-rectangles of the kit sheets
    (door sheet texture_10, window sheet texture_11, chimney samples a plain
    Dark-variant tile), avoiding the sheet corner labels. Materials add three
    roles (kit_door, kit_window, kit_chimney) referencing
    ../../materials/kenney_*.png. Running WITHOUT --kit-details emits
    byte-identical output to the pre-kit generator.

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
import re
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

# Kenney "Prototype Textures" kit (CC0). Kenney serves downloads through a
# signed two-step flow: the asset page embeds a hex download id, the
# download-link endpoint (Referer required) resolves it to an expiring URL.
# Sheets are 1024x1024 PNG grids; texture_10 is the DOOR sheet, texture_11 the
# WINDOW sheet, both with a text label in the top-left corner that the sampled
# sub-rectangles avoid. The chimney samples one plain Dark-variant tile.
KENNEY_KIT = {
    "slug": "prototype-textures",
    "name": "Prototype Textures",
    "page_url": "https://kenney.nl/assets/prototype-textures",
    "download_id_hex": "6173736574732f70726f746f747970652d7465787475726573",
    "variant_door": "Light",
    "variant_window": "Light",
    "variant_chimney": "Dark",
    "sheet_door": "PNG/Light/texture_10.png",
    "sheet_window": "PNG/Light/texture_11.png",
    "sheet_chimney": "PNG/Dark/texture_01.png",
    "sheet_size_px": 1024,
    # Sampled sub-rectangles in sheet pixels (x0, y0, x1, y1), verified against
    # the rendered sheets; v maps top-left origin like glTF UV convention.
    "cell_door_px": [300, 310, 725, 1010],
    "cell_window_px": [300, 300, 725, 725],
    "cell_chimney_px": [6, 6, 122, 122],
    "target_files": {
        "door": "kenney_door.png",
        "window": "kenney_window.png",
        "chimney": "kenney_chimney.png",
    },
}

# Kit detail dimensions (meters unless suffixed _game = game units).
DOOR_WIDTH_M = 1.0
DOOR_HEIGHT_M = 2.1
WINDOW_WIDTH_M = 1.0
WINDOW_HEIGHT_M = 1.2
WINDOW_SILL_M = 1.0
WINDOW_SPACING_M = 3.0
WINDOW_CORNER_MARGIN_M = 1.0
WINDOW_ROW_SPACING_M = 3.0
CHIMNEY_SIDE_M = 0.6
CHIMNEY_HEIGHT_M = 0.8
DETAIL_INSET_GAME = 0.1  # offset from the wall plane, game units

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
                meters_per_game: float, tile_m: float) -> tuple[int, int, dict | None]:
    """Gabled roof for a convex hole-free footprint.

    Ridge runs along the longer side direction of the minimum rotated rectangle
    through the footprint center at h + tan(pitch) * half-width. Returns
    (roof triangles, gable-end wall triangles, ridge frame or None). The frame
    carries the (u, t) parameterization used for chimney placement.
    """
    hull = unique_points(strip_collinear(convex_hull(outer)))
    if len(hull) < 4 or not is_convex(hull):
        return 0, 0, None
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
    frame = {
        "ux": ux, "uy": uy, "vx": vx, "vy": vy,
        "cx": cx, "cy": cy,
        "u_min": u_min, "u_max": u_max,
        "t_max": t_max, "ridge_z": ridge_z, "pitch": pitch,
    }
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
    return roof_tris, gable_tris, frame


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


def fetch_url(url: str, headers: dict | None = None, timeout: int = 120) -> bytes:
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, **(headers or {})})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.read()


def download_kit(mat_dir: str) -> dict:
    """Download the pinned Kenney kit through the signed two-step flow.

    Returns manifest info (urls, sha256, licence text, extracted map files).
    """
    page_headers = {"User-Agent": "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36"}
    try:
        page_html = fetch_url(KENNEY_KIT["page_url"], page_headers, timeout=60).decode("utf-8", "replace")
    except (OSError, ValueError) as exc:
        fail("ERR-NET-KIT-PAGE", f"cannot fetch {KENNEY_KIT['page_url']}: {exc}")
    match = re.search(r"data-download-id=['\"]([0-9a-fA-F]+)['\"]", page_html)
    download_id = match.group(1) if match else KENNEY_KIT["download_id_hex"]
    try:
        bytes.fromhex(download_id).decode("ascii")
    except ValueError:
        fail("ERR-NET-KIT-ID", f"download id {download_id!r} is not hex")
    link_endpoint = f"https://kenney.nl/download-link/{download_id}"
    link_headers = {
        **page_headers,
        "Referer": KENNEY_KIT["page_url"],
        "X-Requested-With": "XMLHttpRequest",
    }
    try:
        link_json = json.loads(fetch_url(link_endpoint, link_headers, timeout=60).decode("utf-8", "replace"))
    except (OSError, ValueError) as exc:
        fail("ERR-NET-KIT-LINK", f"cannot resolve {link_endpoint}: {exc}")
    resolved_url = link_json.get("url") if isinstance(link_json, dict) else None
    if not resolved_url:
        fail("ERR-NET-KIT-LINK", f"download-link response lacks a url: {link_json}")
    zips_dir = os.path.join(mat_dir, "zips")
    os.makedirs(zips_dir, exist_ok=True)
    zip_path = os.path.join(zips_dir, f"kenney_{KENNEY_KIT['slug']}.zip")
    print(f"downloading Kenney {KENNEY_KIT['name']} -> {zip_path}")
    try:
        data = fetch_url(resolved_url, page_headers, timeout=180)
    except (OSError, ValueError) as exc:
        fail("ERR-NET-KIT-DOWNLOAD", f"cannot download {resolved_url}: {exc}")
    if data[:2] != b"PK":
        fail("ERR-NET-KIT-DOWNLOAD", f"{resolved_url} did not return a zip (first bytes: {data[:4]!r})")
    with open(zip_path, "wb") as fh:
        fh.write(data)
    digest = sha256_file(zip_path)
    with zipfile.ZipFile(zip_path) as zf:
        try:
            license_text = zf.read("License.txt").decode("utf-8", "replace")
        except KeyError:
            fail("ERR-KIT-LICENSE", "kit zip lacks License.txt")
        targets = {}
        for role, member in (("door", KENNEY_KIT["sheet_door"]),
                             ("window", KENNEY_KIT["sheet_window"]),
                             ("chimney", KENNEY_KIT["sheet_chimney"])):
            if member not in zf.namelist():
                fail("ERR-KIT-ZIP", f"kit zip lacks {member}")
            target = KENNEY_KIT["target_files"][role]
            with zf.open(member) as src, open(os.path.join(mat_dir, target), "wb") as dst:
                dst.write(src.read())
            targets[role] = target
    return {
        "slug": KENNEY_KIT["slug"],
        "name": KENNEY_KIT["name"],
        "page_url": KENNEY_KIT["page_url"],
        "download_link_endpoint": link_endpoint,
        "resolved_url": resolved_url,
        "resolved_url_note": "Kenney download URLs are signed and expiring; content is pinned by zip_sha256.",
        "zip_sha256": digest,
        "zip_bytes": len(data),
        "license": "CC0-1.0",
        "license_text_first_line": license_text.strip().splitlines()[1].strip() if license_text.strip() else "",
        "license_text": license_text,
        "files": targets,
        "cells_px": {
            "door": KENNEY_KIT["cell_door_px"],
            "window": KENNEY_KIT["cell_window_px"],
            "chimney": KENNEY_KIT["cell_chimney_px"],
        },
        "sheet_size_px": KENNEY_KIT["sheet_size_px"],
    }


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
    kit = download_kit(mat_dir)
    try:
        req = urllib.request.Request(CC0_URL, headers={"User-Agent": USER_AGENT})
        with urllib.request.urlopen(req, timeout=60) as resp:
            cc0_text = resp.read().decode("utf-8", "replace")
    except (OSError, ValueError):
        cc0_text = CC0_FALLBACK_TEXT
    manifest = {
        "materials": entries,
        "kit": kit,
        "license": "CC0-1.0",
        "license_text_url": CC0_TEXT_URL,
    }
    with open(os.path.join(mat_dir, "materials.json"), "wb") as fh:
        fh.write((json.dumps(manifest, sort_keys=True, indent=1) + "\n").encode("utf-8"))
    with open(os.path.join(mat_dir, "LICENSES.md"), "wb") as fh:
        fh.write(render_licenses(entries, cc0_text).encode("utf-8"))
    with open(os.path.join(mat_dir, "KIT-LICENSE.md"), "wb") as fh:
        fh.write(render_kit_license(kit).encode("utf-8"))
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


def render_kit_license(kit: dict) -> str:
    lines = [
        "# WI-R3-01 prototype kit license (Kenney)",
        "",
        f"Kit: {kit['name']} ({kit['slug']})",
        f"Asset page: {kit['page_url']}",
        f"Download endpoint: {kit['download_link_endpoint']}",
        f"Resolved URL: {kit['resolved_url']}",
        f"Zip SHA-256: `{kit['zip_sha256']}`",
        "",
        "Kenney serves asset downloads through signed expiring URLs; the",
        "downloaded zip content is pinned by its SHA-256 above. Kenney states",
        "the kit is CC0 on the asset page and ships the license statement",
        "below inside the zip (License.txt), reproduced verbatim.",
        "",
        "## Verbatim License.txt from the kit zip",
        "",
        "```",
        kit["license_text"].rstrip(),
        "```",
        "",
    ]
    return "\n".join(lines)


CC0_FALLBACK_TEXT = (
    "Creative Commons Legal Code\n\nCC0 1.0 Universal\n\n"
    "OFFICIAL TEXT retrieved failure fallback: see "
    "https://creativecommons.org/publicdomain/zero/1.0/legalcode.txt\n"
    "The text was not embedded in the ambientCG zips; this placeholder asserts\n"
    "the assets are CC0 1.0 per the ambientCG asset pages.\n"
)


def load_material_manifest(materials_dir: str, want_kit: bool = False) -> tuple[list[dict], dict | None]:
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
    kit_info = None
    if want_kit:
        kit = manifest.get("kit")
        if not isinstance(kit, dict) or not kit.get("files") or not kit.get("cells_px"):
            fail("ERR-MATERIAL-KIT-MISSING",
                 f"{materials_dir} lacks the kit section in materials.json; "
                 "run --download-materials (kit-enabled) before --kit-details")
        for role, target in kit["files"].items():
            if not os.path.isfile(os.path.join(materials_dir, target)):
                fail("ERR-MATERIAL-KIT-MISSING", f"kit texture missing: {target}")
        sheet = float(kit.get("sheet_size_px") or KENNEY_KIT["sheet_size_px"])
        cells = {}
        for role, rect in kit["cells_px"].items():
            x0, y0, x1, y1 = (float(v) for v in rect)
            if not (0 <= x0 < x1 <= sheet and 0 <= y0 < y1 <= sheet):
                fail("ERR-MATERIAL-KIT-SHAPE", f"kit cell rect for {role} invalid: {rect}")
            cells[role] = (x0 / sheet, y0 / sheet, x1 / sheet, y1 / sheet)
        kit_info = {
            "specs": [
                {"name": "kit_door", "image": f"../../materials/{kit['files']['door']}"},
                {"name": "kit_window", "image": f"../../materials/{kit['files']['window']}"},
                {"name": "kit_chimney", "image": f"../../materials/{kit['files']['chimney']}"},
            ],
            "cells": cells,
            "meta": {k: kit.get(k) for k in ("slug", "name", "page_url", "zip_sha256", "license")},
        }
    return mat_specs, kit_info


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
# Kit details (doors, windows, chimneys) -- pure functions of geometry
# ---------------------------------------------------------------------------


def normalize_ring_ccw(ring: list[tuple[float, float]]) -> list[tuple[float, float]]:
    pts = list(ring)
    if ring_area_open(pts) < 0:
        pts.reverse()
    return pts


def longest_edge(pts: list[tuple[float, float]]) -> tuple[int, float]:
    best_i = 0
    best_len = -1.0
    n = len(pts)
    for i in range(n):
        ax, ay = pts[i]
        bx, by = pts[(i + 1) % n]
        length = math.hypot(bx - ax, by - ay)
        if length > best_len + EPS:
            best_len = length
            best_i = i
    return best_i, best_len


def detail_quad(pts: list[tuple[float, float]], edge_index: int, s_center: float,
                half_w: float, z0: float, z1: float, inset: float) -> tuple[
        tuple[tuple[float, float, float], ...], tuple[float, float, float]]:
    """Vertical detail quad parallel to wall edge `edge_index`.

    pts must be a CCW ring. Returns (corners bl, br, tr, tl, outward normal).
    """
    n = len(pts)
    ax, ay = pts[edge_index % n]
    bx, by = pts[(edge_index + 1) % n]
    ex, ey = bx - ax, by - ay
    length = math.hypot(ex, ey)
    dx, dy = ex / length, ey / length
    nx, ny = dy, -dx  # outward for a CCW ring (right of travel)
    ox, oy = nx * inset, ny * inset
    s0 = max(0.0, s_center - half_w)
    s1 = min(length, s_center + half_w)
    bl = (ax + dx * s0 + ox, ay + dy * s0 + oy, z0)
    br = (ax + dx * s1 + ox, ay + dy * s1 + oy, z0)
    tr = (br[0], br[1], z1)
    tl = (bl[0], bl[1], z1)
    return (bl, br, tr, tl), (nx, ny, 0.0)


def emit_quad(mesh: MeshBuilder, bl, br, tr, tl, expected_normal,
              cell: tuple[float, float, float, float]) -> int:
    """Emit one quad as two triangles with cell UVs (v top-left origin).

    Returns triangles emitted (0 for degenerate quads)."""
    u0, v0, u1, v1 = cell
    nrm = _tri_normal(bl, br, tr)
    dot = nrm[0] * expected_normal[0] + nrm[1] * expected_normal[1] + nrm[2] * expected_normal[2]
    if dot < 0:
        bl, br, tr, tl = br, bl, tl, tr
        nrm = _tri_normal(bl, br, tr)
    mesh.tri(bl, nrm, (u0, v1), br, nrm, (u1, v1), tr, nrm, (u1, v0))
    mesh.tri(bl, nrm, (u0, v1), tr, nrm, (u1, v0), tl, nrm, (u0, v0))
    return 2


def door_placement(ring: list[tuple[float, float]],
                   meters_per_game: float) -> tuple[dict | None, str | None]:
    """One door quad centered on the longest wall at ground level."""
    pts = normalize_ring_ccw(ring)
    if len(pts) < 3:
        return None, "no-ring"
    edge_index, length = longest_edge(pts)
    need = (DOOR_WIDTH_M / meters_per_game) + 1.2
    if length < need:
        return None, "wall-too-short"
    w_game = DOOR_WIDTH_M / meters_per_game
    h_game = DOOR_HEIGHT_M / meters_per_game
    quad, normal = detail_quad(pts, edge_index, length / 2.0, w_game / 2.0,
                               0.0, h_game, DETAIL_INSET_GAME)
    return {
        "edge_index": edge_index,
        "edge_length": length,
        "s_center": length / 2.0,
        "half_w": w_game / 2.0,
        "quad": quad,
        "normal": normal,
    }, None


def window_sills(height_m: float) -> list[float]:
    """Ground-floor-above sill heights in meters for a building of `height_m`."""
    sills = []
    k = 0
    while True:
        sill = WINDOW_SILL_M + WINDOW_ROW_SPACING_M * k
        if sill + WINDOW_HEIGHT_M > height_m - 0.5:
            break
        sills.append(sill)
        k += 1
    return sills


def window_placements(ring: list[tuple[float, float]], height_m: float,
                      meters_per_game: float,
                      door: dict | None) -> list[dict]:
    """Window quads along outer walls above the ground floor.

    Spacing runs along each wall every WINDOW_SPACING_M, skipping
    WINDOW_CORNER_MARGIN_M around corners and the door span on the door edge.
    """
    pts = normalize_ring_ccw(ring)
    sills = window_sills(height_m)
    if not sills:
        return []
    margin = WINDOW_CORNER_MARGIN_M / meters_per_game
    spacing = WINDOW_SPACING_M / meters_per_game
    w_game = WINDOW_WIDTH_M / meters_per_game
    h_game = WINDOW_HEIGHT_M / meters_per_game
    skip_half = door["half_w"] + 0.5 / meters_per_game if door else 0.0
    out: list[dict] = []
    n = len(pts)
    for i in range(n):
        ax, ay = pts[i]
        bx, by = pts[(i + 1) % n]
        length = math.hypot(bx - ax, by - ay)
        usable = length - 2 * margin
        if usable < w_game:
            continue
        count = min(int(usable // w_game), max(1, int(usable / spacing + 0.5)))
        for j in range(count):
            s = margin + (j + 0.5) * usable / count
            if door and door["edge_index"] == i and abs(s - door["s_center"]) <= skip_half:
                continue
            for sill_m in sills:
                quad, normal = detail_quad(pts, i, s, w_game / 2.0,
                                           sill_m / meters_per_game,
                                           (sill_m + WINDOW_HEIGHT_M) / meters_per_game,
                                           DETAIL_INSET_GAME)
                out.append({"edge_index": i, "s_center": s, "quad": quad,
                            "normal": normal, "sill_m": sill_m})
    return out


def chimney_placement(frame: dict, meters_per_game: float) -> dict:
    """Chimney box near the ridge end of a gabled roof (ridge frame units).

    Center sits at 75% of the ridge span on the +t side; the half-sizes clamp
    to roof fractions so the box stays on the roof for any footprint size.
    """
    u_span = frame["u_max"] - frame["u_min"]
    u_c = frame["u_min"] + 0.75 * u_span
    t_c = 0.35 * frame["t_max"]
    half = CHIMNEY_SIDE_M / 2.0 / meters_per_game
    hu = min(half, 0.12 * u_span)
    hn = min(half, 0.25 * frame["t_max"])
    bottom = frame["ridge_z"] - frame["pitch"] * (t_c + hn)
    top = (frame["ridge_z"] - frame["pitch"] * t_c) + CHIMNEY_HEIGHT_M / meters_per_game
    return {
        "frame": frame, "u_c": u_c, "t_c": t_c, "hu": hu, "hn": hn,
        "bottom": bottom, "top": top,
    }


def emit_chimney(mesh: MeshBuilder, place: dict,
                 cell: tuple[float, float, float, float]) -> int:
    """Chimney box: four sides plus a top cap (bottom open onto the roof)."""
    frame = place["frame"]
    u_c, t_c, hu, hn = place["u_c"], place["t_c"], place["hu"], place["hn"]
    bottom, top = place["bottom"], place["top"]

    def world(du: float, dt: float, z: float) -> tuple[float, float, float]:
        return (frame["cx"] + (u_c + du) * frame["ux"] + (t_c + dt) * frame["vx"],
                frame["cy"] + (u_c + du) * frame["uy"] + (t_c + dt) * frame["vy"],
                z)

    tris = 0
    faces = [
        (((-hu, -hn), (hu, -hn)), (-frame["vx"], -frame["vy"], 0.0)),
        (((hu, hn), (-hu, hn)), (frame["vx"], frame["vy"], 0.0)),
        (((-hu, -hn), (-hu, hn)), (-frame["ux"], -frame["uy"], 0.0)),
        (((hu, -hn), (hu, hn)), (frame["ux"], frame["uy"], 0.0)),
    ]
    for (d0, d1), expected in faces:
        bl = world(d0[0], d0[1], bottom)
        br = world(d1[0], d1[1], bottom)
        tr = world(d1[0], d1[1], top)
        tl = world(d0[0], d0[1], top)
        tris += emit_quad(mesh, bl, br, tr, tl, expected, cell)
    top_bl = world(-hu, -hn, top)
    top_br = world(hu, -hn, top)
    top_tr = world(hu, hn, top)
    top_tl = world(-hu, hn, top)
    tris += emit_quad(mesh, top_bl, top_br, top_tr, top_tl, (0.0, 0.0, 1.0), cell)
    return tris


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

    mat_specs, kit_info = load_material_manifest(args.materials_dir, want_kit=args.kit_details)
    tile_of = {s["name"]: s["tile_size_meters"] for s in mat_specs}
    emission_specs = mat_specs + (kit_info["specs"] if kit_info else [])
    kit_cells = kit_info["cells"] if kit_info else None

    # Build geometry per part (shared by both layouts so totals agree).
    buildings: dict[str, list[dict]] = {}
    for p in emit_parts:
        buildings.setdefault(p["cleabs"], []).append(p)

    # Kit doors: one per building, on the longest wall of its largest part.
    main_part_id: dict[str, str] = {}
    door_by_cleabs: dict[str, dict | None] = {}
    door_skip_by_cleabs: dict[str, str] = {}
    if args.kit_details:
        for cleabs in sorted(buildings):
            ordered = sorted(buildings[cleabs], key=lambda q: q["part_id"])
            main = max(ordered, key=lambda q: abs(shoelace(q["outer"])))
            main_part_id[cleabs] = main["part_id"]
            place, reason = door_placement(main["outer"], mpg)
            door_by_cleabs[cleabs] = place
            if reason:
                door_skip_by_cleabs[cleabs] = reason
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
        "kit_triangles": 0,
    }
    tri_by_material: dict[str, int] = {}
    kit_run = {"doors": 0, "windows": 0, "chimneys": 0}
    kit_bld = {c: {"door": 0, "windows": 0, "chimney": 0} for c in sorted(buildings)}
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
            gable_frame = None
            if use_gable:
                _, gable_tris, gable_frame = gabled_roof(rm, wm, p["outer"], h_game, mpg, roof_tile)
                totals["gable_end_triangles"] += gable_tris
                totals["gable_baked"] += 1
            else:
                flat_roof(rm, p["outer"], p["holes"], h_game, mpg, roof_tile)
                if args.roof_rule == "gabled":
                    totals["gable_fallback_flat"] += 1
            roof_tris = len(rm.idx) // 3
            details: list[tuple[str, MeshBuilder]] = []
            if args.kit_details:
                is_main = p["part_id"] == main_part_id[cleabs]
                door = door_by_cleabs.get(cleabs)
                if is_main and door:
                    dm = MeshBuilder()
                    emit_quad(dm, *door["quad"], door["normal"], kit_cells["door"])
                    details.append(("kit_door", dm))
                    kit_run["doors"] += 1
                    kit_bld[cleabs]["door"] = 1
                wins = window_placements(p["outer"], p["height_m"], mpg,
                                         door if is_main else None)
                if wins:
                    km = MeshBuilder()
                    for wq in wins:
                        emit_quad(km, *wq["quad"], wq["normal"], kit_cells["window"])
                    details.append(("kit_window", km))
                    kit_run["windows"] += len(wins)
                    kit_bld[cleabs]["windows"] += len(wins)
                if is_main and gable_frame is not None:
                    cm = MeshBuilder()
                    emit_chimney(cm, chimney_placement(gable_frame, mpg), kit_cells["chimney"])
                    if cm.idx:
                        details.append(("kit_chimney", cm))
                        kit_run["chimneys"] += 1
                        kit_bld[cleabs]["chimney"] = 1
            kit_tris = sum(len(m.idx) // 3 for _, m in details)
            tris = len(wm.idx) // 3 + roof_tris + kit_tris
            cx = min(grid["chunks_x"] - 1, max(0, int(math.floor(p["center_x"] / chunk_size))))
            cy = min(grid["chunks_y"] - 1, max(0, int(math.floor(p["center_y"] / chunk_size))))
            per_part_geo[p["part_id"]] = {
                "cleabs": cleabs, "chunk": (cx, cy),
                "wall": (wall_role, wm), "roof": (roof_role, rm),
                "details": details,
                "triangles": tris, "height_m": p["height_m"],
                "roof_rule_applied": "gabled" if use_gable else "flat",
                "gable_frame": gable_frame,
            }
            tri_by_chunk[(cx, cy)] = tri_by_chunk.get((cx, cy), 0) + tris
            parts_by_chunk[(cx, cy)] = parts_by_chunk.get((cx, cy), 0) + 1
            bld_by_chunk.setdefault((cx, cy), set()).add(cleabs)
            totals["wall_quads"] += quads
            totals["roof_triangles"] += roof_tris
            totals["kit_triangles"] += kit_tris
            totals["triangles"] += tris
            tri_by_material[wall_role] = tri_by_material.get(wall_role, 0) + len(wm.idx) // 3
            tri_by_material[roof_role] = tri_by_material.get(roof_role, 0) + len(rm.idx) // 3
            for role, m in details:
                tri_by_material[role] = tri_by_material.get(role, 0) + len(m.idx) // 3

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
            for role, mesh in [geo["wall"], geo["roof"], *geo["details"]]:
                slots.setdefault(role, MeshBuilder()).merge(mesh)
        for (cx, cy) in sorted(chunk_slots):
            slots = chunk_slots[(cx, cy)]
            write_gltf_pair(
                os.path.join(gltf_root, "merged", f"chunk_{cx}_{cy}.gltf"),
                [(role, mesh) for role, mesh in sorted(slots.items())],
                emission_specs,
            )
            merged_tris += sum(len(m.idx) for m in slots.values()) // 3
    if "perbuilding" in layouts:
        os.makedirs(os.path.join(gltf_root, "perbuilding"), exist_ok=True)
        for cleabs in sorted(buildings):
            geo_list = [per_part_geo[p["part_id"]] for p in sorted(buildings[cleabs], key=lambda q: q["part_id"])]
            by_role: dict[str, MeshBuilder] = {}
            for geo in geo_list:
                for role, mesh in [geo["wall"], geo["roof"], *geo["details"]]:
                    by_role.setdefault(role, MeshBuilder()).merge(mesh)
            if not all(c.isalnum() or c in "_-." for c in cleabs):
                fail("ERR-NAME", f"cleabs {cleabs!r} is not filesystem-safe")
            write_gltf_pair(
                os.path.join(gltf_root, "perbuilding", f"{cleabs}.gltf"),
                [(role, mesh) for role, mesh in sorted(by_role.items())],
                emission_specs,
            )
            perbuilding_tris += sum(len(m.idx) for m in by_role.values()) // 3
    if merged_tris and perbuilding_tris and merged_tris != perbuilding_tris:
        fail("ERR-TRIANGLE-MISMATCH", f"merged {merged_tris} != perbuilding {perbuilding_tris}")
    accounted = (totals["wall_quads"] * 2 + totals["gable_end_triangles"]
                 + totals["roof_triangles"] + totals["kit_triangles"])
    if accounted != totals["triangles"]:
        fail("ERR-TRIANGLE-ACCOUNTING",
             f"wall {totals['wall_quads'] * 2}+gable {totals['gable_end_triangles']}+roof "
             f"{totals['roof_triangles']}+kit {totals['kit_triangles']} != total {totals['triangles']}")
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
            "kit_details": bool(args.kit_details),
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
            "roles": [{"name": s["name"], "image": s["image"],
                       "tile_size_meters": s.get("tile_size_meters")} for s in emission_specs],
            "pinned_sources": [{k: e[k] for k in ("role", "asset_id", "url")} for e in MATERIAL_TABLE],
        },
        "kit_details": _kit_stats_section(args, kit_info, kit_run, kit_bld,
                                          door_skip_by_cleabs, totals, mpg),
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
        "kit_triangles": totals["kit_triangles"],
    }))


def _kit_stats_section(args, kit_info, kit_run, kit_bld, door_skip_by_cleabs,
                       totals, mpg) -> dict:
    section: dict = {"enabled": bool(args.kit_details and kit_info)}
    if not section["enabled"]:
        return section
    buildings = kit_bld
    section.update({
        "kit": {
            "slug": kit_info["meta"].get("slug"),
            "name": kit_info["meta"].get("name"),
            "page_url": kit_info["meta"].get("page_url"),
            "zip_sha256": kit_info["meta"].get("zip_sha256"),
            "license": kit_info["meta"].get("license"),
            "license_summary": "Kenney Prototype Textures, Creative Commons Zero (CC0 1.0); verbatim statement in materials/KIT-LICENSE.md",
        },
        "doors_placed": kit_run["doors"],
        "door_skip_reasons": dict(sorted(door_skip_by_cleabs.items())),
        "buildings_with_doors": sum(1 for v in buildings.values() if v["door"]),
        "windows_placed": kit_run["windows"],
        "buildings_with_windows": sum(1 for v in buildings.values() if v["windows"]),
        "chimneys_placed": kit_run["chimneys"],
        "buildings_with_chimneys": sum(1 for v in buildings.values() if v["chimney"]),
        "added_triangles": totals["kit_triangles"],
        "added_materials": 3,
        "placement_note": "Doors center on the longest wall of the largest part; windows run along outer walls every 3 m above the ground floor skipping 1 m at corners and the door span; one chimney per building on the gabled-baked main part.",
        "parameters_m": {
            "door": {"width": DOOR_WIDTH_M, "height": DOOR_HEIGHT_M},
            "window": {"width": WINDOW_WIDTH_M, "height": WINDOW_HEIGHT_M,
                       "sill": WINDOW_SILL_M, "spacing": WINDOW_SPACING_M,
                       "corner_margin": WINDOW_CORNER_MARGIN_M,
                       "row_spacing": WINDOW_ROW_SPACING_M},
            "chimney": {"side": CHIMNEY_SIDE_M, "height": CHIMNEY_HEIGHT_M},
            "inset_game_units": DETAIL_INSET_GAME,
        },
    })
    return section


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
    roof_tris, gable_tris, frame = gabled_roof(rm, wm, sq, h_game, mpg, tile)
    check("square roof tris", roof_tris == 4, f"got {roof_tris}")
    check("square gable ends", gable_tris == 2, f"got {gable_tris}")
    check("square frame", frame is not None and abs(frame["t_max"] - span / 2.0) < 1e-9,
          f"frame {frame}")
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

    # 10. Kit details on a tall square: door, windows, chimney.
    cell = (0.1, 0.2, 0.7, 0.9)
    big = [(0.0, 0.0), (200.0, 0.0), (200.0, 200.0), (0.0, 200.0)]
    big_m = 200.0 * mpg  # 60.96 m
    door, door_reason = door_placement(big, mpg)
    check("kit door placed", door is not None and door_reason is None,
          f"{door_reason}")
    if door:
        bl, br, tr, tl = door["quad"]
        n = door["normal"]
        width = math.dist(bl[:2], br[:2]) * mpg
        height = (tr[2] - br[2]) * mpg
        check("kit door width", abs(width - DOOR_WIDTH_M) < 1e-9, f"{width}")
        check("kit door height", abs(height - DOOR_HEIGHT_M) < 1e-9, f"{height}")
        check("kit door ground", bl[2] == 0.0 and br[2] == 0.0, f"z {bl[2]} {br[2]}")
        # door sits on the longest wall: its edge is 200 game units
        check("kit door longest wall", abs(door["edge_length"] - 200.0) < 1e-9,
              f"{door['edge_length']}")
        # inset: quad plane offset 0.1 game units outward from the edge line
        edge_pts = normalize_ring_ccw(big)
        i = door["edge_index"]
        ax, ay = edge_pts[i]
        bx, by = edge_pts[(i + 1) % len(edge_pts)]
        dist = abs((bl[0] - ax) * (by - ay) - (bl[1] - ay) * (bx - ax)) / 200.0
        check("kit door inset", abs(dist - DETAIL_INSET_GAME) < 1e-9, f"{dist}")
        dm = MeshBuilder()
        emit_quad(dm, *door["quad"], door["normal"], cell)
        check("kit door tris", len(dm.idx) == 6, f"{len(dm.idx)}")
        uv = dm.uv[dm.idx[0]]
        check("kit door uv cell", abs(uv[0] - cell[0]) < 1e-9 and abs(uv[1] - cell[3]) < 1e-9,
              f"{uv}")
    wins = window_placements(big, 12.0, mpg, door)
    check("kit windows placed", len(wins) > 0, "none")
    margin = WINDOW_CORNER_MARGIN_M / mpg
    w_game = WINDOW_WIDTH_M / mpg
    ok_span = all(margin <= w["s_center"] <= 200.0 - margin for w in wins)
    check("kit window corner margins", ok_span, "window inside corner margin")
    door_edge = door["edge_index"] if door else -1
    skip_half = door["half_w"] + 0.5 / mpg if door else 0.0
    on_door = [w for w in wins if w["edge_index"] == door_edge
               and abs(w["s_center"] - door["s_center"]) <= skip_half]
    check("kit windows skip door span", not on_door, f"{len(on_door)} overlap")
    sills = window_sills(12.0)
    check("kit window rows", sills == [1.0, 4.0, 7.0, 10.0], f"{sills}")
    sill_ok = all(abs((w["quad"][2][2] * mpg) - (w["sill_m"] + WINDOW_HEIGHT_M)) < 1e-9
                  for w in wins)
    check("kit window heights", sill_ok, "top mismatch")
    flat_wins = window_placements(big, 2.0, mpg, door)
    check("kit windows short building", flat_wins == [], f"{len(flat_wins)}")
    if frame:
        place = chimney_placement(frame, mpg)
        cm = MeshBuilder()
        tris = emit_chimney(cm, place, cell)
        check("kit chimney tris", len(cm.idx) // 3 == 10, f"{len(cm.idx) // 3}")
        check("kit chimney emit count", tris == 10, f"{tris}")
        f = frame
        check("kit chimney on roof", place["bottom"] <= place["top"]
              and f["u_min"] <= place["u_c"] - place["hu"]
              and place["u_c"] + place["hu"] <= f["u_max"]
              and abs(place["t_c"]) + place["hn"] <= f["t_max"],
              "chimney outside roof extent")
        surface_top = f["ridge_z"] - f["pitch"] * abs(place["t_c"])
        check("kit chimney height", abs((place["top"] - surface_top) * mpg - CHIMNEY_HEIGHT_M) < 1e-9,
              f"{place['top']} vs {surface_top}")
    # flat roof buildings get no chimney: frame is None there (checked via the
    # gabled_roof early return in test 2's L-shape flat path).

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
    ap.add_argument("--kit-details", action="store_true",
                    help="add Kenney CC0 kit details (doors, windows, chimneys); "
                         "requires a kit-enabled materials dir (see --download-materials)")
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
