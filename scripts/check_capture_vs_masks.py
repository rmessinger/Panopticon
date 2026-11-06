#!/usr/bin/env python3
"""Validate a ScenarioCapture manifest against geospatial payload terrain masks.

Verifies:
1. No recorded unit position in any frame carries the Impassable mask bit (round-mapped cells;
   diagnostic-only under --generic-mask-diagnostic-only).
2. Every ordered Move goal is reached within tolerance.
3. Manifest actor IDs (plus Created entries from --results) equal profile actors exactly.
4. Straight-line segment between start and goal intersects impassable cells (necessity).
5. No recorded position falls in an Impassable cell under runtime floor mapping, with penetration depth
   (diagnostic-only under --generic-mask-diagnostic-only).
6. No recorded position or consecutive-sample segment enters a passability/buildability footprint interior.

Also produces a summary line of frames spanned, logic-tick duration at 30 Hz,
per-unit path length in game units, and goal verdicts.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import re
import sys
from typing import Any

POINTS_PER_CHUNK_AXIS = 129
INTERVALS_PER_CHUNK = 128
CORE_CHUNK_BYTES = 16641
DEFAULT_SAMPLE_SPACING_GAME_UNITS = 10.0

FLAG_IMPASSABLE = 0x01
FLAG_UNBUILDABLE = 0x02
DEFINED_MASK_BITS = FLAG_IMPASSABLE | FLAG_UNBUILDABLE  # 0x03

BUILDING_FOOTPRINT_KINDS = ("passability", "buildability")
ON_BOUNDARY_EPS_GU = 1e-6
PARAM_EPS = 1e-9
PARALLEL_EPS = 1e-12
MIN_SPAN_GU = 1e-6
FOOTPRINT_BUCKET_GU = 128.0


class CaptureCheckerError(Exception):
    """Raised when mask loading, parsing, or payload validation fails closed."""


def strip_json_comments(text: str) -> str:
    """Strip // and /* */ comments from JSON text while preserving quoted strings."""
    result: list[str] = []
    i = 0
    n = len(text)
    in_string = False
    escape = False

    while i < n:
        c = text[i]
        if in_string:
            result.append(c)
            if escape:
                escape = False
            elif c == "\\":
                escape = True
            elif c == '"':
                in_string = False
            i += 1
        else:
            if c == '"':
                in_string = True
                result.append(c)
                i += 1
            elif c == "/" and i + 1 < n and text[i + 1] == "/":
                i += 2
                while i < n and text[i] != "\n":
                    i += 1
            elif c == "/" and i + 1 < n and text[i + 1] == "*":
                i += 2
                while i + 1 < n and not (text[i] == "*" and text[i + 1] == "/"):
                    i += 1
                i += 2
            else:
                result.append(c)
                i += 1

    return "".join(result)


def load_json_file(file_path: str) -> dict[str, Any]:
    """Load a JSON file, supporting comments."""
    if not os.path.isfile(file_path):
        raise CaptureCheckerError(f"File not found: {file_path}")
    with open(file_path, "r", encoding="utf-8") as f:
        content = f.read()
    cleaned = strip_json_comments(content)
    try:
        data = json.loads(cleaned)
    except json.JSONDecodeError as exc:
        raise CaptureCheckerError(f"Failed to parse JSON file {file_path}: {exc}") from exc
    if not isinstance(data, dict):
        raise CaptureCheckerError(f"JSON root in {file_path} must be an object")
    return data


def read_and_validate_mask_chunk(file_path: str, chunk_x: int = 0, chunk_y: int = 0) -> bytes:
    """Read a terrain mask chunk file and strictly validate length and flag bits.

    Fails closed if the file length is not exactly 16641 bytes or if any byte carries
    undefined bits outside 1 (Impassable) and 2 (Unbuildable).
    """
    if not os.path.isfile(file_path):
        raise CaptureCheckerError(f"Mask file not found: {file_path}")

    with open(file_path, "rb") as fh:
        data = fh.read()

    if len(data) != CORE_CHUNK_BYTES:
        raise CaptureCheckerError(
            f"Chunk ({chunk_x}, {chunk_y}): the mask buffer must hold exactly {CORE_CHUNK_BYTES} bytes; "
            f"found {len(data)}: {file_path}"
        )

    for idx, b in enumerate(data):
        if (b & ~DEFINED_MASK_BITS) != 0:
            col = idx % POINTS_PER_CHUNK_AXIS
            row = idx // POINTS_PER_CHUNK_AXIS
            raise CaptureCheckerError(
                f"Chunk ({chunk_x}, {chunk_y}): the mask flag byte {b} at core position ({col}, {row}) "
                f"contains undefined bits (defined bits: 0x{DEFINED_MASK_BITS:02x}): {file_path}"
            )

    return data


def load_payload(payload_dir: str) -> dict[str, Any]:
    """Load payload.json and all mask chunk buffers.

    Validates that mask files exist, have exact length 16641, and carry defined bits only.
    """
    if os.path.isfile(payload_dir):
        payload_json_path = payload_dir
        payload_dir = os.path.dirname(payload_dir)
    else:
        payload_json_path = os.path.join(payload_dir, "payload.json")

    payload_data = load_json_file(payload_json_path)
    grid = payload_data.get("grid")
    if not isinstance(grid, dict):
        raise CaptureCheckerError(f"payload.json in {payload_dir} lacks a valid 'grid' object")

    chunks_x = int(grid.get("chunksX", 1))
    chunks_y = int(grid.get("chunksY", 1))
    layout = grid.get("layout", {})
    spacing = float(layout.get("sampleSpacingGameUnits", DEFAULT_SAMPLE_SPACING_GAME_UNITS))
    intervals = int(layout.get("intervalsPerChunk", INTERVALS_PER_CHUNK))
    points_axis = int(layout.get("pointsPerChunkAxis", POINTS_PER_CHUNK_AXIS))

    chunks: dict[tuple[int, int], bytes] = {}
    chunks_list = payload_data.get("chunks", [])

    if chunks_list:
        for chunk_entry in chunks_list:
            cx = chunk_entry.get("cx", 0)
            cy = chunk_entry.get("cy", 0)
            masks_info = chunk_entry.get("masks")
            if not masks_info or "path" not in masks_info:
                candidate = os.path.join(payload_dir, "masks", f"{cx}_{cy}.bin")
            else:
                candidate = os.path.join(payload_dir, masks_info["path"])
            chunks[(cx, cy)] = read_and_validate_mask_chunk(candidate, cx, cy)
    else:
        for cy in range(chunks_y):
            for cx in range(chunks_x):
                candidate = os.path.join(payload_dir, "masks", f"{cx}_{cy}.bin")
                chunks[(cx, cy)] = read_and_validate_mask_chunk(candidate, cx, cy)

    # Ensure all expected chunks in grid are loaded
    for cy in range(chunks_y):
        for cx in range(chunks_x):
            if (cx, cy) not in chunks:
                raise CaptureCheckerError(f"Missing required chunk ({cx}, {cy}) in payload {payload_dir}")

    return {
        "chunks_x": chunks_x,
        "chunks_y": chunks_y,
        "spacing": spacing,
        "intervals": intervals,
        "points_axis": points_axis,
        "chunks": chunks,
        "features": payload_data.get("features"),
    }


def world_to_cell(x: float, y: float, spacing: float = DEFAULT_SAMPLE_SPACING_GAME_UNITS) -> tuple[int, int]:
    """Map world coordinates in game units to sample cell indices."""
    return int(round(x / spacing)), int(round(y / spacing))


def world_to_cell_floor(x: float, y: float, spacing: float = DEFAULT_SAMPLE_SPACING_GAME_UNITS) -> tuple[int, int]:
    """Map world coordinates in game units to cell indices with floor semantics, matching the runtime tile lookup."""
    return math.floor(x / spacing), math.floor(y / spacing)


def get_mask_flag(
    payload_info: dict[str, Any],
    ix: int,
    iy: int,
) -> int:
    """Return the mask flag byte for cell index (ix, iy).

    Rejects out-of-grid coordinates as failures, never clamping.
    """
    if ix < 0 or iy < 0:
        raise ValueError(f"Cell index ({ix}, {iy}) is negative (out of grid)")

    chunks_x: int = payload_info["chunks_x"]
    chunks_y: int = payload_info["chunks_y"]
    intervals: int = payload_info["intervals"]
    points_axis: int = payload_info["points_axis"]
    chunks: dict[tuple[int, int], bytes] = payload_info["chunks"]

    max_ix = chunks_x * intervals
    max_iy = chunks_y * intervals

    if ix > max_ix or iy > max_iy:
        raise ValueError(f"Cell index ({ix}, {iy}) exceeds grid extent ({max_ix}, {max_iy})")

    cx = ix // intervals
    cy = iy // intervals
    lx = ix % intervals
    ly = iy % intervals

    if cx == chunks_x and lx == 0:
        cx = chunks_x - 1
        lx = intervals
    elif cx >= chunks_x:
        raise ValueError(f"Chunk x coordinate {cx} is out of bounds for chunksX={chunks_x}")

    if cy == chunks_y and ly == 0:
        cy = chunks_y - 1
        ly = intervals
    elif cy >= chunks_y:
        raise ValueError(f"Chunk y coordinate {cy} is out of bounds for chunksY={chunks_y}")

    chunk_data = chunks.get((cx, cy))
    if chunk_data is None:
        raise ValueError(f"Chunk ({cx}, {cy}) not loaded")

    offset = ly * points_axis + lx
    return chunk_data[offset]


def trace_line_cells(
    x0: float,
    y0: float,
    x1: float,
    y1: float,
    spacing: float = DEFAULT_SAMPLE_SPACING_GAME_UNITS,
) -> list[tuple[int, int]]:
    """Return all grid cell indices (ix, iy) intersected by the straight segment from (x0, y0) to (x1, y1).

    Uses Amanatides-Woo grid traversal on the continuous world line segment.
    """
    u0 = x0 / spacing + 0.5
    v0 = y0 / spacing + 0.5
    u1 = x1 / spacing + 0.5
    v1 = y1 / spacing + 0.5

    cell_x = math.floor(u0)
    cell_y = math.floor(v0)
    end_x = math.floor(u1)
    end_y = math.floor(v1)

    dx = u1 - u0
    dy = v1 - v0

    step_x = 1 if dx > 0 else (-1 if dx < 0 else 0)
    step_y = 1 if dy > 0 else (-1 if dy < 0 else 0)

    if step_x != 0:
        next_x = cell_x + 1 if step_x > 0 else cell_x
        t_max_x = (next_x - u0) / dx
        t_delta_x = abs(1.0 / dx)
    else:
        t_max_x = float("inf")
        t_delta_x = float("inf")

    if step_y != 0:
        next_y = cell_y + 1 if step_y > 0 else cell_y
        t_max_y = (next_y - v0) / dy
        t_delta_y = abs(1.0 / dy)
    else:
        t_max_y = float("inf")
        t_delta_y = float("inf")

    visited = [(cell_x, cell_y)]
    eps = 1e-12
    max_steps = abs(end_x - cell_x) + abs(end_y - cell_y) + 32
    steps = 0

    while (cell_x != end_x or cell_y != end_y) and steps < max_steps:
        steps += 1
        diff = t_max_x - t_max_y
        if abs(diff) < eps:
            cell_x += step_x
            cell_y += step_y
            t_max_x += t_delta_x
            t_max_y += t_delta_y
        elif t_max_x < t_max_y:
            cell_x += step_x
            t_max_x += t_delta_x
        else:
            cell_y += step_y
            t_max_y += t_delta_y
        visited.append((cell_x, cell_y))

    # Deduplicate preserving order
    unique_cells: list[tuple[int, int]] = []
    seen = set()
    for pt in visited:
        if pt not in seen:
            seen.add(pt)
            unique_cells.append(pt)

    return unique_cells


def sample_world_position(actor: dict[str, Any]) -> tuple[str, float, float] | None:
    """Return (actor id, world x, world y) for a manifest actor entry, or None when no position is recorded."""
    aid = actor.get("Id") or actor.get("id") or actor.get("Label") or actor.get("label")
    aid_str = str(aid) if aid else "unknown"

    wx = actor.get("WorldX") if actor.get("WorldX") is not None else actor.get("worldX")
    wy = actor.get("WorldY") if actor.get("WorldY") is not None else actor.get("worldY")
    if wx is None or wy is None:
        wx = actor.get("X") if actor.get("X") is not None else actor.get("x")
        wy = actor.get("Y") if actor.get("Y") is not None else actor.get("y")
    if wx is None or wy is None:
        return None

    x, y = float(wx), float(wy)
    if not (math.isfinite(x) and math.isfinite(y)):
        raise CaptureCheckerError(f"actor '{aid_str}' has a non-finite world position ({wx}, {wy})")
    return aid_str, x, y


def _is_finite_number(value: Any) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def _parse_ring(raw: Any, where: str) -> list[tuple[float, float]]:
    """Return distinct ring vertices; a repeated closing vertex is dropped because closure is implicit."""
    positions = raw.get("positions") if isinstance(raw, dict) else None
    if not isinstance(positions, list):
        raise CaptureCheckerError(f"{where}: ring lacks a 'positions' list")

    vertices: list[tuple[float, float]] = []
    for idx, pos in enumerate(positions):
        if not isinstance(pos, dict) or not (_is_finite_number(pos.get("x")) and _is_finite_number(pos.get("y"))):
            raise CaptureCheckerError(f"{where}: vertex {idx} has missing or non-finite coordinates")
        vertices.append((float(pos["x"]), float(pos["y"])))

    if len(vertices) > 1 and vertices[0] == vertices[-1]:
        vertices.pop()
    if len(vertices) < 3:
        raise CaptureCheckerError(f"{where}: ring is empty or degenerate (fewer than 3 distinct vertices)")
    return vertices


class Footprint:
    """Polygon region: outer ring minus inner rings. Inner rings are excluded from coverage."""

    def __init__(self, key: str, outer: list[tuple[float, float]], holes: list[list[tuple[float, float]]]):
        self.key = key
        self.outer = outer
        self.holes = holes
        xs = [x for x, _ in outer]
        ys = [y for _, y in outer]
        self.min_x, self.max_x = min(xs), max(xs)
        self.min_y, self.max_y = min(ys), max(ys)


def load_building_footprints(features: Any) -> tuple[list[Footprint], int]:
    """Parse passability and buildability polygons from payload features, deduplicated by identity.

    Returns the footprints and the total number of features items. Fails closed on any malformed entry.
    """
    if not isinstance(features, dict):
        raise CaptureCheckerError("payload.json lacks a 'features' object")
    items = features.get("items")
    if not isinstance(items, list):
        raise CaptureCheckerError("payload.json features section lacks an 'items' list")

    footprints: dict[tuple[str, str, str], Footprint] = {}
    geometry_by_identity: dict[tuple[str, str, str], str] = {}
    for idx, item in enumerate(items):
        if not isinstance(item, dict):
            raise CaptureCheckerError(f"features item {idx} is not an object")
        if item.get("kind") not in BUILDING_FOOTPRINT_KINDS:
            continue

        identity = (item.get("sourceId"), item.get("featureId"), item.get("partId"))
        display = "/".join(str(part) for part in identity)
        if not all(isinstance(part, str) and part for part in identity):
            raise CaptureCheckerError(f"features item {idx} lacks a sourceId, featureId, or partId")

        geometry = item.get("geometry")
        if not isinstance(geometry, dict) or geometry.get("type") != "polygon":
            raise CaptureCheckerError(f"features item {idx} ({display}) is not a polygon geometry")

        canonical = json.dumps(geometry, sort_keys=True)
        if identity in geometry_by_identity:
            if geometry_by_identity[identity] != canonical:
                raise CaptureCheckerError(f"features items for '{display}' disagree in geometry")
            continue
        geometry_by_identity[identity] = canonical

        outer = _parse_ring(geometry.get("outerRing"), f"'{display}' outerRing")
        raw_holes = geometry.get("innerRings", [])
        if not isinstance(raw_holes, list):
            raise CaptureCheckerError(f"'{display}' innerRings is not a list")
        holes = [_parse_ring(ring, f"'{display}' innerRings[{j}]") for j, ring in enumerate(raw_holes)]
        footprints[identity] = Footprint(display, outer, holes)

    return list(footprints.values()), len(items)


def _bucket_range(min_x: float, min_y: float, max_x: float, max_y: float):
    for bx in range(math.floor(min_x / FOOTPRINT_BUCKET_GU), math.floor(max_x / FOOTPRINT_BUCKET_GU) + 1):
        for by in range(math.floor(min_y / FOOTPRINT_BUCKET_GU), math.floor(max_y / FOOTPRINT_BUCKET_GU) + 1):
            yield bx, by


class FootprintIndex:
    """Uniform-grid bucket index over footprint bounding boxes, used as a prefilter."""

    def __init__(self, footprints: list[Footprint]):
        self._buckets: dict[tuple[int, int], list[Footprint]] = {}
        for fp in footprints:
            for bucket in _bucket_range(fp.min_x, fp.min_y, fp.max_x, fp.max_y):
                self._buckets.setdefault(bucket, []).append(fp)

    def query(self, min_x: float, min_y: float, max_x: float, max_y: float) -> list[Footprint]:
        found: dict[str, Footprint] = {}
        for bucket in _bucket_range(min_x, min_y, max_x, max_y):
            for fp in self._buckets.get(bucket, ()):
                if (
                    fp.min_x <= max_x + ON_BOUNDARY_EPS_GU
                    and fp.max_x >= min_x - ON_BOUNDARY_EPS_GU
                    and fp.min_y <= max_y + ON_BOUNDARY_EPS_GU
                    and fp.max_y >= min_y - ON_BOUNDARY_EPS_GU
                ):
                    found[fp.key] = fp
        return [found[key] for key in sorted(found)]


def _orient_sign(ax: float, ay: float, bx: float, by: float, px: float, py: float) -> int:
    """Side of p relative to directed line a->b: +1 left, -1 right, 0 within ON_BOUNDARY_EPS_GU of the line."""
    cross = (bx - ax) * (py - ay) - (by - ay) * (px - ax)
    if abs(cross) <= ON_BOUNDARY_EPS_GU * math.hypot(bx - ax, by - ay):
        return 0
    return 1 if cross > 0 else -1


def _on_segment(px: float, py: float, ax: float, ay: float, bx: float, by: float) -> bool:
    return (
        _orient_sign(ax, ay, bx, by, px, py) == 0
        and min(ax, bx) - ON_BOUNDARY_EPS_GU <= px <= max(ax, bx) + ON_BOUNDARY_EPS_GU
        and min(ay, by) - ON_BOUNDARY_EPS_GU <= py <= max(ay, by) + ON_BOUNDARY_EPS_GU
    )


def _locate_in_ring(px: float, py: float, ring: list[tuple[float, float]]) -> int:
    """Return 1 strictly inside, 0 on the boundary, -1 outside, using winding number with implicit closure."""
    winding = 0
    count = len(ring)
    for i in range(count):
        ax, ay = ring[i]
        bx, by = ring[(i + 1) % count]
        if _on_segment(px, py, ax, ay, bx, by):
            return 0
        side = _orient_sign(ax, ay, bx, by, px, py)
        if ay <= py < by and side > 0:
            winding += 1
        elif by <= py < ay and side < 0:
            winding -= 1
    return 1 if winding != 0 else -1


def footprint_covers(fp: Footprint, px: float, py: float) -> bool:
    """Closed-set coverage: outer and hole boundaries count as covered; hole interiors do not."""
    if _locate_in_ring(px, py, fp.outer) < 0:
        return False
    return all(_locate_in_ring(px, py, hole) <= 0 for hole in fp.holes)


def segment_covered_spans(
    fp: Footprint,
    ax: float,
    ay: float,
    bx: float,
    by: float,
) -> list[tuple[float, float]]:
    """Return covered sub-intervals (t_start, t_end) of segment a->b in segment parameter space [0, 1].

    Breakpoints are the parameters where the segment meets ring edges; each span between breakpoints
    is covered when its midpoint is covered, so spans are either wholly inside or wholly outside.
    """
    dx = bx - ax
    dy = by - ay
    seg_len = math.hypot(dx, dy)
    if seg_len <= MIN_SPAN_GU:
        return []

    seg_len_sq = seg_len * seg_len
    cuts = {0.0, 1.0}
    for ring in [fp.outer, *fp.holes]:
        count = len(ring)
        for i in range(count):
            cx, cy = ring[i]
            ex, ey = ring[(i + 1) % count]
            ux = ex - cx
            uy = ey - cy
            denom = dx * uy - dy * ux
            wx = cx - ax
            wy = cy - ay
            if abs(denom) > PARALLEL_EPS * seg_len * math.hypot(ux, uy):
                t = (wx * uy - wy * ux) / denom
                u = (wx * dy - wy * dx) / denom
                if -PARAM_EPS <= t <= 1.0 + PARAM_EPS and -PARAM_EPS <= u <= 1.0 + PARAM_EPS:
                    cuts.add(min(1.0, max(0.0, t)))
            elif _orient_sign(ax, ay, bx, by, cx, cy) == 0 and _orient_sign(ax, ay, bx, by, ex, ey) == 0:
                for qx, qy in ((cx, cy), (ex, ey)):
                    t_q = ((qx - ax) * dx + (qy - ay) * dy) / seg_len_sq
                    cuts.add(min(1.0, max(0.0, t_q)))

    ordered = sorted(cuts)
    spans: list[tuple[float, float]] = []
    for t0, t1 in zip(ordered, ordered[1:]):
        if (t1 - t0) * seg_len <= MIN_SPAN_GU:
            continue
        tm = 0.5 * (t0 + t1)
        if footprint_covers(fp, ax + tm * dx, ay + tm * dy):
            spans.append((t0, t1))
    return spans


def parse_results_file(results_path: str) -> set[str]:
    """Parse Created actor IDs from an action-results text file."""
    created_actors: set[str] = set()
    if not os.path.isfile(results_path):
        raise CaptureCheckerError(f"Results file not found: {results_path}")

    with open(results_path, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#"):
                continue

            parts = line.split("\t")
            is_created = any(p.strip() == "Created" for p in parts) or line.startswith("Created")
            if not is_created:
                continue

            # Look for actorId, actor, id
            m = re.search(r"\b(?:actorId|actor_id|actor)\s*=\s*([^\s,]+)", line, re.IGNORECASE)
            if m:
                created_actors.add(m.group(1))
                continue

            m = re.search(r"\bid\s*=\s*([^\s,]+)", line, re.IGNORECASE)
            if m:
                created_actors.add(m.group(1))
                continue

            # Direct column Candidate e.g. Created \t humvee-1
            if len(parts) >= 2:
                candidate = parts[1].strip()
                if candidate and candidate not in ("ObjectLifecycle", "Created"):
                    created_actors.add(candidate)
                    continue

            # Details column Candidate
            if len(parts) >= 5:
                cand = parts[4].strip()
                m_obj = re.search(r"\bobjectId=(\d+)", cand)
                if m_obj:
                    created_actors.add(m_obj.group(1))

    return created_actors


class CheckResult:
    """Encapsulates outcome for one validation check."""

    def __init__(
        self,
        name: str,
        passed: bool,
        summary: str,
        details: list[str] | None = None,
        gating: bool = True,
    ):
        self.name = name
        self.passed = passed
        self.summary = summary
        self.details = details or []
        self.gating = gating


def check_straight_line_exemption(
    actor_id: str,
    profile: dict[str, Any],
) -> tuple[bool, str]:
    """Check if profile documents why straight-line impassable count may be zero."""
    exemption_keys = (
        "AllowZeroStraightLine",
        "AllowZeroStraightLineImpassable",
        "AllowClearStraightLine",
        "StraightLineExemption",
        "StraightLineReason",
        "StraightLineNecessityExemption",
        "StraightLineAllowedClear",
        "StraightLineExemptionReason",
        "allow_zero_straight_line",
        "straight_line_exemption",
    )

    for k in exemption_keys:
        val = profile.get(k)
        if val is True:
            return True, f"profile flag {k}=True"
        if isinstance(val, str) and val.strip():
            return True, f"profile note: {val.strip()}"

    for actor in profile.get("Actors", []):
        aid = actor.get("Id") or actor.get("id")
        if aid == actor_id:
            for k in exemption_keys:
                val = actor.get(k)
                if val is True:
                    return True, f"actor flag {k}=True"
                if isinstance(val, str) and val.strip():
                    return True, f"actor note: {val.strip()}"

    for cmd in profile.get("Commands", []):
        cid = cmd.get("ActorId") or cmd.get("actorId") or cmd.get("Actor")
        if cid == actor_id:
            for k in exemption_keys:
                val = cmd.get(k)
                if val is True:
                    return True, f"command flag {k}=True"
                if isinstance(val, str) and val.strip():
                    return True, f"command note: {val.strip()}"

    return False, ""


def validate_capture(
    manifest_path: str,
    payload_path: str,
    profile_path: str,
    results_path: str | None = None,
    tolerance: float = 60.0,
    generic_mask_diagnostic_only: bool = False,
) -> tuple[bool, list[CheckResult], str]:
    """Execute all six checks against the manifest, payload, profile, and optional results.

    With generic_mask_diagnostic_only, Checks 1 and 5 are reported but excluded from the pass verdict.
    """
    manifest = load_json_file(manifest_path)
    profile = load_json_file(profile_path)
    payload_info = load_payload(payload_path)
    spacing = payload_info["spacing"]

    frames: list[dict[str, Any]] = manifest.get("Frames", [])

    # Collect profile actors
    profile_actors: set[str] = set()
    profile_starts: dict[str, tuple[float, float]] = {}
    for actor in profile.get("Actors", []):
        aid = actor.get("Id") or actor.get("id")
        if aid:
            aid_str = str(aid)
            profile_actors.add(aid_str)
            sx = actor.get("X") if actor.get("X") is not None else actor.get("x")
            sy = actor.get("Y") if actor.get("Y") is not None else actor.get("y")
            if sx is not None and sy is not None:
                profile_starts[aid_str] = (float(sx), float(sy))

    # Collect profile move goals
    goals: dict[str, tuple[float, float]] = {}
    for cmd in profile.get("Commands", []):
        cmd_type = str(cmd.get("Type") or cmd.get("type") or "").strip().lower()
        if cmd_type in ("move", "moveto"):
            aid = cmd.get("ActorId") or cmd.get("actorId") or cmd.get("Actor") or cmd.get("actor")
            gx = cmd.get("X") if cmd.get("X") is not None else cmd.get("x")
            gy = cmd.get("Y") if cmd.get("Y") is not None else cmd.get("y")
            if aid and gx is not None and gy is not None:
                goals[str(aid)] = (float(gx), float(gy))

    # --- Check 1: No recorded unit position in any frame carries Impassable bit ---
    # Also tracks path lengths and last positions
    sample_count = 0
    impassable_violations: list[str] = []
    actor_last_positions: dict[str, tuple[float, float]] = {}
    manifest_actors: set[str] = set()
    path_lengths: dict[str, float] = {}
    actor_prev_positions: dict[str, tuple[float, float]] = {}

    for frame in frames:
        f_idx = frame.get("Index", 0)
        f_logic = frame.get("AbsoluteLogicFrame", 0)
        actors_list = frame.get("Actors", [])

        for actor in actors_list:
            aid = actor.get("Id") or actor.get("id") or actor.get("Label") or actor.get("label")
            if aid:
                aid_str = str(aid)
                manifest_actors.add(aid_str)
            else:
                aid_str = "unknown"

            wx = actor.get("WorldX") if actor.get("WorldX") is not None else actor.get("worldX")
            wy = actor.get("WorldY") if actor.get("WorldY") is not None else actor.get("worldY")
            if wx is None or wy is None:
                wx = actor.get("X") if actor.get("X") is not None else actor.get("x")
                wy = actor.get("Y") if actor.get("Y") is not None else actor.get("y")

            if wx is None or wy is None:
                continue

            wx_f = float(wx)
            wy_f = float(wy)
            sample_count += 1
            actor_last_positions[aid_str] = (wx_f, wy_f)

            # Accumulate path length
            if aid_str not in path_lengths:
                path_lengths[aid_str] = 0.0
            if aid_str in actor_prev_positions:
                px, py = actor_prev_positions[aid_str]
                path_lengths[aid_str] += math.hypot(wx_f - px, wy_f - py)
            actor_prev_positions[aid_str] = (wx_f, wy_f)

            # Verify against mask
            try:
                ix, iy = world_to_cell(wx_f, wy_f, spacing)
                flag = get_mask_flag(payload_info, ix, iy)
                if (flag & FLAG_IMPASSABLE) != 0:
                    impassable_violations.append(
                        f"frame {f_idx} (logic {f_logic}): actor '{aid_str}' at world ({wx_f:.2f}, {wy_f:.2f}) "
                        f"-> cell ({ix}, {iy}) carries Impassable bit (flag=0x{flag:02x})"
                    )
            except ValueError as err:
                impassable_violations.append(
                    f"frame {f_idx} (logic {f_logic}): actor '{aid_str}' at world ({wx_f:.2f}, {wy_f:.2f}) "
                    f"-> out-of-grid position failure: {err}"
                )

    c1_passed = len(impassable_violations) == 0
    if c1_passed:
        c1_summary = f"0 impassable traversals across {sample_count} position samples in {len(frames)} frames"
    else:
        c1_summary = (
            f"{len(impassable_violations)} impassable/invalid traversals detected across "
            f"{sample_count} position samples in {len(frames)} frames"
        )
    check1 = CheckResult(
        "no impassable traversal",
        c1_passed,
        c1_summary,
        impassable_violations,
        gating=not generic_mask_diagnostic_only,
    )

    # --- Check 2: Every ordered goal is reached within tolerance ---
    goal_verdicts: dict[str, str] = {}
    goal_details: list[str] = []
    reached_count = 0

    for aid_str, (gx, gy) in goals.items():
        if aid_str not in actor_last_positions:
            verdict = "MISSED (actor not recorded in any manifest frame)"
            goal_verdicts[aid_str] = verdict
            goal_details.append(f"actor '{aid_str}': {verdict} [target ({gx:.1f}, {gy:.1f})]")
            continue

        fx, fy = actor_last_positions[aid_str]
        dist = math.hypot(fx - gx, fy - gy)
        if dist <= tolerance:
            reached_count += 1
            verdict = f"REACHED (dist {dist:.2f} <= {tolerance:.1f})"
            goal_verdicts[aid_str] = "REACHED"
            goal_details.append(
                f"actor '{aid_str}': {verdict} [final ({fx:.1f}, {fy:.1f}), target ({gx:.1f}, {gy:.1f})]"
            )
        else:
            verdict = f"MISSED (dist {dist:.2f} > {tolerance:.1f})"
            goal_verdicts[aid_str] = "MISSED"
            goal_details.append(
                f"actor '{aid_str}': {verdict} [final ({fx:.1f}, {fy:.1f}), target ({gx:.1f}, {gy:.1f})]"
            )

    total_goals = len(goals)
    c2_passed = total_goals == 0 or reached_count == total_goals
    if c2_passed:
        c2_summary = f"{reached_count}/{total_goals} goals reached within {tolerance:.1f} game units"
    else:
        c2_summary = (
            f"{reached_count}/{total_goals} goals reached within {tolerance:.1f} game units "
            f"({total_goals - reached_count} missed)"
        )
    check2 = CheckResult("goals reached", c2_passed, c2_summary, goal_details)

    # --- Check 3: Actor set check ---
    observed_actors = set(manifest_actors)
    if results_path:
        created_actors = parse_results_file(results_path)
        observed_actors.update(created_actors)

    missing_actors = sorted(list(profile_actors - observed_actors))
    unexpected_actors = sorted(list(observed_actors - profile_actors))
    c3_passed = len(missing_actors) == 0 and len(unexpected_actors) == 0

    c3_details: list[str] = []
    if missing_actors:
        c3_details.append(f"missing actors from profile: {', '.join(missing_actors)}")
    if unexpected_actors:
        c3_details.append(f"unexpected actors not in profile: {', '.join(unexpected_actors)}")

    if c3_passed:
        c3_summary = (
            f"exact match: {len(profile_actors)} expected, {len(observed_actors)} observed "
            f"(0 missing, 0 unexpected)"
        )
    else:
        c3_summary = (
            f"set mismatch: {len(missing_actors)} missing ({missing_actors}), "
            f"{len(unexpected_actors)} unexpected ({unexpected_actors})"
        )
    check3 = CheckResult("actor set", c3_passed, c3_summary, c3_details)

    # --- Check 4: Straight-line necessity ---
    c4_details: list[str] = []
    straight_zero_count = 0
    total_pairs = 0
    pair_counts: dict[str, int] = {}

    for aid_str, (gx, gy) in goals.items():
        sx, sy = profile_starts.get(aid_str, (None, None))
        if sx is None or sy is None:
            # Fallback to first frame position if profile omitted start
            if frames and frames[0].get("Actors"):
                for a in frames[0]["Actors"]:
                    if (a.get("Id") or a.get("id")) == aid_str:
                        sx = float(a.get("WorldX") or a.get("worldX") or a.get("X") or a.get("x") or 0.0)
                        sy = float(a.get("WorldY") or a.get("worldY") or a.get("Y") or a.get("y") or 0.0)
                        break

        if sx is None or sy is None:
            continue

        total_pairs += 1
        line_cells = trace_line_cells(sx, sy, gx, gy, spacing)
        impass_count = 0
        for lx, ly in line_cells:
            try:
                flag = get_mask_flag(payload_info, lx, ly)
                if (flag & FLAG_IMPASSABLE) != 0:
                    impass_count += 1
            except ValueError:
                pass

        pair_counts[aid_str] = impass_count
        exempt, ex_reason = check_straight_line_exemption(aid_str, profile)

        if impass_count > 0:
            c4_details.append(
                f"actor '{aid_str}': {impass_count} impassable cells intersected "
                f"(start ({sx:.1f}, {sy:.1f}) -> goal ({gx:.1f}, {gy:.1f}))"
            )
        elif exempt:
            c4_details.append(
                f"actor '{aid_str}': 0 impassable cells intersected (start ({sx:.1f}, {sy:.1f}) -> goal ({gx:.1f}, {gy:.1f})) "
                f"[EXEMPT: {ex_reason}]"
            )
        else:
            straight_zero_count += 1
            c4_details.append(
                f"actor '{aid_str}': 0 impassable cells intersected (start ({sx:.1f}, {sy:.1f}) -> goal ({gx:.1f}, {gy:.1f})) "
                f"[FAILED: straight line clear, profile documents no exemption]"
            )

    c4_passed = straight_zero_count == 0
    c4_summary = (
        f"{total_pairs - straight_zero_count}/{total_pairs} start-goal pairs have impassable cells "
        f"(counts: {pair_counts})"
    )
    check4 = CheckResult("straight-line necessity", c4_passed, c4_summary, c4_details)

    # --- Check 5: Runtime-consistent floor-mapped impassable traversal ---
    c5_violations: list[str] = []
    c5_sample_count = 0
    actor_samples: dict[str, list[tuple[int, int, float, float]]] = {}

    for frame in frames:
        f_idx = frame.get("Index", 0)
        f_logic = frame.get("AbsoluteLogicFrame", 0)

        for actor in frame.get("Actors", []):
            located = sample_world_position(actor)
            if located is None:
                continue
            aid_str, wx_f, wy_f = located
            c5_sample_count += 1
            actor_samples.setdefault(aid_str, []).append((f_idx, f_logic, wx_f, wy_f))

            try:
                ix, iy = world_to_cell_floor(wx_f, wy_f, spacing)
                flag = get_mask_flag(payload_info, ix, iy)
            except ValueError as err:
                c5_violations.append(
                    f"frame {f_idx} (logic {f_logic}): actor '{aid_str}' at world ({wx_f:.2f}, {wy_f:.2f}) "
                    f"-> out-of-grid position failure: {err}"
                )
                continue

            if (flag & FLAG_IMPASSABLE) != 0:
                depth = min(
                    wx_f - ix * spacing,
                    (ix + 1) * spacing - wx_f,
                    wy_f - iy * spacing,
                    (iy + 1) * spacing - wy_f,
                )
                c5_violations.append(
                    f"frame {f_idx} (logic {f_logic}): actor '{aid_str}' at world ({wx_f:.2f}, {wy_f:.2f}) "
                    f"-> floor cell ({ix}, {iy}) carries Impassable bit (flag=0x{flag:02x}), "
                    f"penetration depth {depth:.2f} game units"
                )

    c5_passed = len(c5_violations) == 0
    if c5_passed:
        c5_summary = f"0 impassable floor-cell traversals across {c5_sample_count} position samples in {len(frames)} frames"
    else:
        c5_summary = (
            f"{len(c5_violations)} impassable/invalid floor-cell traversals detected across "
            f"{c5_sample_count} position samples in {len(frames)} frames"
        )
    check5 = CheckResult(
        "floor-mapped impassable traversal",
        c5_passed,
        c5_summary,
        c5_violations,
        gating=not generic_mask_diagnostic_only,
    )

    # --- Check 6: Building-interior swept exclusion ---
    footprints, footprint_item_count = load_building_footprints(payload_info["features"])
    footprint_index = FootprintIndex(footprints)
    c6_violations: list[str] = []
    c6_details: list[str] = []
    point_count = 0
    segment_count = 0

    for aid_str in sorted(actor_samples):
        samples = actor_samples[aid_str]

        for f_idx, f_logic, px, py in samples:
            point_count += 1
            for fp in footprint_index.query(px, py, px, py):
                if footprint_covers(fp, px, py):
                    c6_violations.append(
                        f"point: actor '{aid_str}' frame {f_idx} (logic {f_logic}) sample ({px:.2f}, {py:.2f}) "
                        f"inside polygon '{fp.key}', entry point ({px:.2f}, {py:.2f}), inside-length 0.00 game units"
                    )

        for (f0, l0, x0, y0), (f1, l1, x1, y1) in zip(samples, samples[1:]):
            segment_count += 1
            seg_len = math.hypot(x1 - x0, y1 - y0)
            for fp in footprint_index.query(min(x0, x1), min(y0, y1), max(x0, x1), max(y0, y1)):
                spans = segment_covered_spans(fp, x0, y0, x1, y1)
                if not spans:
                    continue
                inside_length = sum((t1 - t0) * seg_len for t0, t1 in spans)
                if inside_length <= MIN_SPAN_GU:
                    continue
                entry_t = spans[0][0]
                entry_x = x0 + entry_t * (x1 - x0)
                entry_y = y0 + entry_t * (y1 - y0)
                c6_violations.append(
                    f"segment: actor '{aid_str}' frames {f0}->{f1} (logic {l0}->{l1}) "
                    f"from ({x0:.2f}, {y0:.2f}) to ({x1:.2f}, {y1:.2f}) crosses polygon '{fp.key}', "
                    f"entry point ({entry_x:.2f}, {entry_y:.2f}), inside-length {inside_length:.2f} game units"
                )

    c6_passed = len(c6_violations) == 0
    c6_details = c6_violations
    if not footprints:
        c6_details = [
            f"0 passability/buildability polygons in features.items ({footprint_item_count} items); "
            f"check passes vacuously"
        ]
    if c6_passed:
        c6_summary = (
            f"0 building-interior intersections across {point_count} position samples and "
            f"{segment_count} segments against {len(footprints)} footprint polygons"
        )
    else:
        c6_summary = (
            f"{len(c6_violations)} building-interior intersections across {point_count} position samples "
            f"and {segment_count} segments against {len(footprints)} footprint polygons"
        )
    check6 = CheckResult("building-interior swept exclusion", c6_passed, c6_summary, c6_details)

    # --- Summary line ---
    frame_count = len(frames)
    first_logic = frames[0].get("AbsoluteLogicFrame", 0) if frames else 0
    last_logic = frames[-1].get("AbsoluteLogicFrame", 0) if frames else 0
    logic_duration_ticks = max(0, last_logic - first_logic)
    duration_seconds = logic_duration_ticks / 30.0

    lengths_str = ", ".join(f"{aid}={path_lengths[aid]:.1f}" for aid in sorted(path_lengths)) or "none"
    verdicts_str = ", ".join(f"{aid}: {goal_verdicts.get(aid, 'N/A')}" for aid in sorted(goals)) or "none"

    summary_line = (
        f"Summary: frames spanned: {frame_count} "
        f"(logic {first_logic}..{last_logic}, {logic_duration_ticks} ticks, {duration_seconds:.2f}s at 30 Hz); "
        f"path lengths (game units): [{lengths_str}]; "
        f"goal verdicts: [{verdicts_str}]"
    )

    checks = [check1, check2, check3, check4, check5, check6]
    all_passed = all(check.passed for check in checks if check.gating)
    return all_passed, checks, summary_line


def build_parser() -> argparse.ArgumentParser:
    """Build the argument parser."""
    parser = argparse.ArgumentParser(
        description="Validate ScenarioCapture manifest against payload terrain masks."
    )
    parser.add_argument("--manifest", required=True, help="ScenarioCapture manifest JSON path")
    parser.add_argument("--payload", required=True, help="Payload directory path containing payload.json and masks/")
    parser.add_argument("--profile", required=True, help="Scenario capture profile JSON path")
    parser.add_argument("--results", default=None, help="Optional action-results .txt path")
    parser.add_argument(
        "--tolerance",
        type=float,
        default=60.0,
        help="Goal arrival tolerance radius in game units (default: 60)",
    )
    parser.add_argument(
        "--generic-mask-diagnostic-only",
        action="store_true",
        help=(
            "Report Check 1 (round-mapped) and Check 5 (floor-mapped) mask traversal without gating "
            "the exit code; Checks 2-4 and 6 always gate"
        ),
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    """CLI entry point."""
    parser = build_parser()
    args = parser.parse_args(argv)

    try:
        passed, checks, summary = validate_capture(
            manifest_path=args.manifest,
            payload_path=args.payload,
            profile_path=args.profile,
            results_path=args.results,
            tolerance=args.tolerance,
            generic_mask_diagnostic_only=args.generic_mask_diagnostic_only,
        )
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1

    for idx, check in enumerate(checks, start=1):
        status_tag = "[PASS]" if check.passed else "[FAIL]"
        role = "gating" if check.gating else "diagnostic"
        print(f"{status_tag} Check {idx} ({check.name}) [{role}]: {check.summary}")
        for detail in check.details:
            print(f"    {detail}")

    print(summary)
    failing_gating = [f"Check {idx}" for idx, c in enumerate(checks, start=1) if c.gating and not c.passed]
    failing_diagnostic = [f"Check {idx}" for idx, c in enumerate(checks, start=1) if not c.gating and not c.passed]
    print(
        f"Verdict: {'PASS' if passed else 'FAIL'} "
        f"(gating failing: {', '.join(failing_gating) or 'none'}; "
        f"diagnostic failing: {', '.join(failing_diagnostic) or 'none'})"
    )
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
