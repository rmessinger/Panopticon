#!/usr/bin/env python3
"""Validate a ScenarioCapture manifest against geospatial payload terrain masks.

Verifies:
1. No recorded unit position in any frame carries the Impassable mask bit.
2. Every ordered Move goal is reached within tolerance.
3. Manifest actor IDs (plus Created entries from --results) equal profile actors exactly.
4. Straight-line segment between start and goal intersects impassable cells (necessity).

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
    }


def world_to_cell(x: float, y: float, spacing: float = DEFAULT_SAMPLE_SPACING_GAME_UNITS) -> tuple[int, int]:
    """Map world coordinates in game units to sample cell indices."""
    return int(round(x / spacing)), int(round(y / spacing))


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

    def __init__(self, name: str, passed: bool, summary: str, details: list[str] | None = None):
        self.name = name
        self.passed = passed
        self.summary = summary
        self.details = details or []


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
) -> tuple[bool, list[CheckResult], str]:
    """Execute all four checks against the manifest, payload, profile, and optional results."""
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
    check1 = CheckResult("no impassable traversal", c1_passed, c1_summary, impassable_violations)

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

    all_passed = c1_passed and c2_passed and c3_passed and c4_passed
    return all_passed, [check1, check2, check3, check4], summary_line


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
        )
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1

    for idx, check in enumerate(checks, start=1):
        status_tag = "[PASS]" if check.passed else "[FAIL]"
        print(f"{status_tag} Check {idx} ({check.name}): {check.summary}")
        for detail in check.details:
            print(f"    {detail}")

    print(summary)
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
