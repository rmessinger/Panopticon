#!/usr/bin/env python3
"""Render actual geospatial payload mask cells over ortho imagery.

Draws building footprint / terrain mask cells (impassable and unbuildable-only)
over a north-up cropped ortho GeoTIFF, framed with header (title and bbox),
footer (swatches, counts, share, and attribution), north arrow, and scale bar.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys
from typing import Any

from PIL import Image, ImageDraw, ImageFont

POINTS_PER_CHUNK_AXIS = 129
INTERVALS_PER_CHUNK = 128
CORE_CHUNK_BYTES = 16641
DEFAULT_SAMPLE_SPACING_GAME_UNITS = 10.0

# TerrainMaskFlags
FLAG_IMPASSABLE = 0x01
FLAG_UNBUILDABLE = 0x02
DEFINED_MASK_BITS = FLAG_IMPASSABLE | FLAG_UNBUILDABLE  # 0x03

# GeoTIFF tag constants
TAG_MODEL_PIXEL_SCALE = 33550
TAG_MODEL_TIEPOINT = 33922

ATTRIBUTION_TEXT = "IGN BD ORTHO, Licence Ouverte 2.0"

# Colors (RGBA)
COLOR_IMPASSABLE_FILL = (235, 45, 45, 130)
COLOR_IMPASSABLE_STROKE = (255, 70, 70, 200)
COLOR_UNBUILDABLE_FILL = (255, 160, 20, 130)
COLOR_UNBUILDABLE_STROKE = (255, 180, 40, 200)

COLOR_HEADER_BG = (15, 20, 26)
COLOR_FOOTER_BG = (15, 20, 26)
COLOR_TEXT_PRIMARY = (240, 245, 250)
COLOR_TEXT_SECONDARY = (170, 185, 200)
COLOR_TEXT_MUTED = (140, 155, 170)

TRACK_PALETTE = [
    {"name": "Humvee 1", "stroke": (0, 225, 255, 255), "casing": (10, 15, 20, 220)},   # Cyan
    {"name": "Humvee 2", "stroke": (50, 235, 90, 255), "casing": (10, 15, 20, 220)},   # Lime
    {"name": "Humvee 3", "stroke": (255, 220, 0, 255), "casing": (10, 15, 20, 220)},   # Yellow
    {"name": "Humvee 4", "stroke": (255, 60, 180, 255), "casing": (10, 15, 20, 220)},  # Magenta
]

COLOR_GRAZE_STROKE = (255, 235, 50, 255)
COLOR_GRAZE_FILL = (255, 235, 50, 90)


class FootprintOverlayError(Exception):
    """Raised when overlay rendering fails validation or processing."""


def load_manifest_tracks(manifest_path: str) -> dict[str, list[tuple[float, float]]]:
    """Load per-actor world tracks from a capture manifest JSON."""
    if not os.path.isfile(manifest_path):
        raise FootprintOverlayError(f"Manifest file not found: {manifest_path}")

    with open(manifest_path, "r", encoding="utf-8") as fh:
        try:
            data = json.load(fh)
        except json.JSONDecodeError as ex:
            raise FootprintOverlayError(f"Invalid JSON in manifest {manifest_path}: {ex}") from ex

    frames = data.get("Frames") or data.get("frames", [])
    if not isinstance(frames, list):
        raise FootprintOverlayError(f"Manifest missing 'Frames' list: {manifest_path}")

    tracks: dict[str, list[tuple[float, float]]] = {}
    for frame in frames:
        actors = frame.get("Actors") or frame.get("actors", [])
        for actor in actors:
            aid = actor.get("Id") or actor.get("id") or actor.get("Label")
            if not aid:
                continue
            wx = actor.get("WorldX") if "WorldX" in actor else actor.get("worldX")
            wy = actor.get("WorldY") if "WorldY" in actor else actor.get("worldY")
            if wx is not None and wy is not None:
                tracks.setdefault(str(aid), []).append((float(wx), float(wy)))

    return tracks


def parse_graze_cells(graze_args: list[str] | None) -> list[tuple[int, int]]:
    """Parse cell coordinates from CLI strings such as '134,260' or '134 260'."""
    if not graze_args:
        return []

    tokens: list[str] = []
    for arg in graze_args:
        parts = arg.replace(",", " ").split()
        tokens.extend(parts)

    if len(tokens) % 2 != 0:
        raise FootprintOverlayError(
            f"Graze cells must be coordinate pairs (col, row); found {len(tokens)} numbers: {graze_args}"
        )

    cells: list[tuple[int, int]] = []
    for i in range(0, len(tokens), 2):
        try:
            col = int(tokens[i])
            row = int(tokens[i + 1])
            cells.append((col, row))
        except ValueError as ex:
            raise FootprintOverlayError(f"Invalid integer cell coordinate in {graze_args}: {ex}") from ex

    return cells


def read_and_validate_mask_chunk(file_path: str, chunk_x: int = 0, chunk_y: int = 0) -> bytes:
    """Read a terrain mask chunk file and strictly validate length and flag bits.

    Fails closed if the file length is not exactly 16641 bytes or if any byte carries
    undefined bits outside 1 (Impassable) and 2 (Unbuildable).
    """
    if not os.path.isfile(file_path):
        raise FootprintOverlayError(f"Mask file not found: {file_path}")

    with open(file_path, "rb") as fh:
        data = fh.read()

    if len(data) != CORE_CHUNK_BYTES:
        raise FootprintOverlayError(
            f"Chunk ({chunk_x}, {chunk_y}): the mask buffer must hold exactly {CORE_CHUNK_BYTES} bytes; "
            f"found {len(data)}: {file_path}"
        )

    for idx, b in enumerate(data):
        if (b & ~DEFINED_MASK_BITS) != 0:
            col = idx % POINTS_PER_CHUNK_AXIS
            row = idx // POINTS_PER_CHUNK_AXIS
            raise FootprintOverlayError(
                f"Chunk ({chunk_x}, {chunk_y}): the mask flag byte {b} at core position ({col}, {row}) "
                f"contains undefined bits (defined bits: 0x{DEFINED_MASK_BITS:02x}): {file_path}"
            )

    return data


def read_ortho_bounds(
    image: Image.Image,
    explicit_bounds: tuple[float, float, float, float] | None = None,
) -> tuple[float, float, float, float]:
    """Derive ortho bounds (min_easting, min_northing, max_easting, max_northing) in UTM.

    Derives from embedded GeoTIFF tags ModelPixelScale (33550) and ModelTiepoint (33922)
    unless explicit_bounds overrides.
    """
    if explicit_bounds is not None:
        min_e, min_n, max_e, max_n = explicit_bounds
        if min_e >= max_e or min_n >= max_n:
            raise FootprintOverlayError(
                f"Explicit ortho bounds must have min < max; found: {explicit_bounds}"
            )
        return (float(min_e), float(min_n), float(max_e), float(max_n))

    tags = getattr(image, "tag_v2", None) or getattr(image, "tag", None)
    if not tags:
        raise FootprintOverlayError(
            "GeoTIFF contains no tags and no --ortho-bounds override was provided."
        )

    scale = tags.get(TAG_MODEL_PIXEL_SCALE)
    tiepoint = tags.get(TAG_MODEL_TIEPOINT)

    if scale is None or tiepoint is None:
        raise FootprintOverlayError(
            f"GeoTIFF missing required tags (ModelPixelScale {TAG_MODEL_PIXEL_SCALE} or "
            f"ModelTiepoint {TAG_MODEL_TIEPOINT}) and no --ortho-bounds override was provided."
        )

    if len(scale) < 2 or len(tiepoint) < 6:
        raise FootprintOverlayError(
            f"GeoTIFF tags have invalid tuple length: scale={scale}, tiepoint={tiepoint}"
        )

    scale_x, scale_y = float(scale[0]), float(scale[1])
    if scale_x <= 0 or scale_y <= 0:
        raise FootprintOverlayError(f"GeoTIFF pixel scale must be positive; found ({scale_x}, {scale_y})")

    i, j = float(tiepoint[0]), float(tiepoint[1])
    x, y = float(tiepoint[3]), float(tiepoint[4])

    origin_x = x - i * scale_x
    origin_y = y + j * scale_y

    min_easting = origin_x
    max_easting = origin_x + image.width * scale_x
    min_northing = origin_y - image.height * scale_y
    max_northing = origin_y

    return (min_easting, min_northing, max_easting, max_northing)


def load_payload_metadata_and_masks(payload_dir: str) -> dict[str, Any]:
    """Load payload.json and all referenced mask chunks, populating a global cell map."""
    payload_json_path = os.path.join(payload_dir, "payload.json")
    if not os.path.isfile(payload_json_path):
        raise FootprintOverlayError(f"payload.json not found in {payload_dir}")

    with open(payload_json_path, "r", encoding="utf-8") as fh:
        try:
            payload_data = json.load(fh)
        except json.JSONDecodeError as ex:
            raise FootprintOverlayError(f"Invalid JSON in {payload_json_path}: {ex}") from ex

    grid = payload_data.get("grid")
    if not isinstance(grid, dict):
        raise FootprintOverlayError(f"payload.json missing 'grid' object: {payload_json_path}")

    transform = grid.get("transform")
    if not isinstance(transform, dict):
        raise FootprintOverlayError(f"payload.json missing 'grid.transform' object: {payload_json_path}")

    origin = transform.get("originEastingNorthing")
    if not isinstance(origin, dict) or "x" not in origin or "y" not in origin:
        raise FootprintOverlayError("grid.transform missing 'originEastingNorthing' {x, y}")

    origin_easting = float(origin["x"])
    origin_northing = float(origin["y"])
    meters_per_gu = float(transform.get("metersPerGameUnit", 0.3048))
    if meters_per_gu <= 0:
        raise FootprintOverlayError(f"metersPerGameUnit must be positive; found {meters_per_gu}")

    layout = grid.get("layout", {})
    sample_spacing = float(layout.get("sampleSpacingGameUnits", DEFAULT_SAMPLE_SPACING_GAME_UNITS))
    if sample_spacing <= 0:
        raise FootprintOverlayError(f"sampleSpacingGameUnits must be positive; found {sample_spacing}")

    chunks_list = payload_data.get("chunks", [])
    chunks_x = int(grid.get("chunksX", 1))
    chunks_y = int(grid.get("chunksY", 1))

    cells: dict[tuple[int, int], int] = {}

    if chunks_list:
        for chunk_entry in chunks_list:
            cx = chunk_entry.get("cx", 0)
            cy = chunk_entry.get("cy", 0)
            masks_info = chunk_entry.get("masks")
            if not masks_info or "path" not in masks_info:
                continue
            mask_rel_path = masks_info["path"]
            mask_full_path = os.path.join(payload_dir, mask_rel_path)
            raw_bytes = read_and_validate_mask_chunk(mask_full_path, cx, cy)
            _populate_chunk_cells(cells, raw_bytes, cx, cy)
    else:
        # Fallback: scan for masks/cx_cy.bin
        for cy in range(chunks_y):
            for cx in range(chunks_x):
                candidate = os.path.join(payload_dir, "masks", f"{cx}_{cy}.bin")
                if os.path.isfile(candidate):
                    raw_bytes = read_and_validate_mask_chunk(candidate, cx, cy)
                    _populate_chunk_cells(cells, raw_bytes, cx, cy)

    return {
        "payload_data": payload_data,
        "origin_easting": origin_easting,
        "origin_northing": origin_northing,
        "meters_per_game_unit": meters_per_gu,
        "sample_spacing_game_units": sample_spacing,
        "chunks_x": chunks_x,
        "chunks_y": chunks_y,
        "cells": cells,
    }


def _populate_chunk_cells(cells: dict[tuple[int, int], int], raw_bytes: bytes, cx: int, cy: int) -> None:
    base_sx = cx * INTERVALS_PER_CHUNK
    base_sy = cy * INTERVALS_PER_CHUNK
    for row in range(POINTS_PER_CHUNK_AXIS):
        sy = base_sy + row
        row_offset = row * POINTS_PER_CHUNK_AXIS
        for col in range(POINTS_PER_CHUNK_AXIS):
            sx = base_sx + col
            val = raw_bytes[row_offset + col]
            existing = cells.get((sx, sy))
            if existing is not None and existing != val:
                raise FootprintOverlayError(
                    f"Seam mismatch at global sample ({sx}, {sy}): chunk ({cx}, {cy}) has {val}, previous was {existing}"
                )
            cells[(sx, sy)] = val


def compute_mask_metrics(
    cells: dict[tuple[int, int], int],
    crop_game: tuple[float, float, float, float],
    spacing: float = DEFAULT_SAMPLE_SPACING_GAME_UNITS,
) -> dict[str, Any]:
    """Compute cell counts and percentage shares for impassable, unbuildable, and total masked cells in crop."""
    min_gx, min_gy, max_gx, max_gy = crop_game
    min_sx = math.ceil(min_gx / spacing)
    max_sx = math.floor(max_gx / spacing)
    min_sy = math.ceil(min_gy / spacing)
    max_sy = math.floor(max_gy / spacing)

    impassable_count = 0
    unbuildable_only_count = 0
    clear_count = 0
    total_cells = 0

    if max_sx >= min_sx and max_sy >= min_sy:
        for sy in range(min_sy, max_sy + 1):
            for sx in range(min_sx, max_sx + 1):
                total_cells += 1
                flag = cells.get((sx, sy), 0)
                if (flag & FLAG_IMPASSABLE) != 0:
                    impassable_count += 1
                elif (flag & FLAG_UNBUILDABLE) != 0:
                    unbuildable_only_count += 1
                else:
                    clear_count += 1

    masked_count = impassable_count + unbuildable_only_count
    impassable_share = impassable_count / total_cells if total_cells > 0 else 0.0
    unbuildable_only_share = unbuildable_only_count / total_cells if total_cells > 0 else 0.0
    masked_share = masked_count / total_cells if total_cells > 0 else 0.0

    return {
        "total_cells": total_cells,
        "impassable_count": impassable_count,
        "unbuildable_only_count": unbuildable_only_count,
        "clear_count": clear_count,
        "masked_count": masked_count,
        "impassable_share": impassable_share,
        "unbuildable_only_share": unbuildable_only_share,
        "masked_share": masked_share,
        "min_sx": min_sx,
        "max_sx": max_sx,
        "min_sy": min_sy,
        "max_sy": max_sy,
    }


def draw_north_arrow(image: Image.Image, center_x: int, center_y: int) -> None:
    """Draw a cartographic north arrow pointing up with 'N' label."""
    draw = ImageDraw.Draw(image)
    # Background card
    draw.rounded_rectangle(
        (center_x - 16, center_y - 20, center_x + 16, center_y + 18),
        radius=4,
        fill=(0, 0, 0, 160),
    )
    font = ImageFont.load_default(11)
    draw.text((center_x, center_y - 13), "N", fill=(255, 255, 255, 255), anchor="mm", font=font)

    tip = (center_x, center_y - 5)
    base = (center_x, center_y + 12)
    left_wing = (center_x - 5, center_y + 6)
    right_wing = (center_x + 5, center_y + 6)

    # Left half white, right half gray
    draw.polygon((tip, base, left_wing), fill=(255, 255, 255, 255))
    draw.polygon((tip, base, right_wing), fill=(160, 160, 160, 255))
    draw.line((tip, base), fill=(40, 40, 40, 255), width=1)


def draw_scale_bar(image: Image.Image, left_x: int, bottom_y: int, m_per_px: float, image_width: int) -> None:
    """Draw a cartographic scale bar in metres with ticks and label."""
    draw = ImageDraw.Draw(image)
    target_px = max(40.0, min(140.0, image_width * 0.25))
    target_m = target_px * m_per_px

    candidates = [1.0, 2.0, 5.0, 10.0, 20.0, 25.0, 50.0, 100.0, 200.0, 250.0, 500.0, 1000.0]
    chosen_m = min(candidates, key=lambda c: abs(c - target_m))
    bar_px = max(10, int(round(chosen_m / m_per_px)))

    # Backing card
    draw.rounded_rectangle(
        (left_x - 6, bottom_y - 18, left_x + bar_px + 6, bottom_y + 8),
        radius=4,
        fill=(0, 0, 0, 160),
    )

    font = ImageFont.load_default(10)
    label = f"{int(chosen_m) if chosen_m >= 1 else chosen_m:g} m"
    draw.text((left_x + bar_px / 2, bottom_y - 9), label, fill=(255, 255, 255, 255), anchor="mm", font=font)

    # Bar line and end ticks
    draw.line([(left_x, bottom_y + 2), (left_x + bar_px, bottom_y + 2)], fill=(255, 255, 255, 255), width=2)
    draw.line([(left_x, bottom_y - 1), (left_x, bottom_y + 5)], fill=(255, 255, 255, 255), width=2)
    draw.line([(left_x + bar_px, bottom_y - 1), (left_x + bar_px, bottom_y + 5)], fill=(255, 255, 255, 255), width=2)


def render_footprint_overlay(
    payload_dir: str,
    ortho_path: str,
    crop_game: tuple[float, float, float, float] | None = None,
    ortho_bounds: tuple[float, float, float, float] | None = None,
    title: str | None = None,
    manifest_path: str | None = None,
    graze_cells: list[tuple[int, int]] | None = None,
) -> Image.Image:
    """Render a still image overlaying payload mask cells on ortho GeoTIFF with framing."""
    payload_info = load_payload_metadata_and_masks(payload_dir)
    origin_e = payload_info["origin_easting"]
    origin_n = payload_info["origin_northing"]
    m_per_gu = payload_info["meters_per_game_unit"]
    spacing = payload_info["sample_spacing_game_units"]
    cells = payload_info["cells"]

    tracks = load_manifest_tracks(manifest_path) if manifest_path else {}
    graze = list(graze_cells) if graze_cells else []

    if crop_game is None:
        # Default crop: whole storage extent
        crop_game = (
            0.0,
            0.0,
            float(payload_info["chunks_x"] * INTERVALS_PER_CHUNK * spacing),
            float(payload_info["chunks_y"] * INTERVALS_PER_CHUNK * spacing),
        )

    min_gx, min_gy, max_gx, max_gy = crop_game
    if min_gx >= max_gx or min_gy >= max_gy:
        raise FootprintOverlayError(f"Invalid crop window in game coords (min >= max): {crop_game}")

    if not os.path.isfile(ortho_path):
        raise FootprintOverlayError(f"Ortho file not found: {ortho_path}")

    try:
        with Image.open(ortho_path) as opened_ortho:
            ortho_min_e, ortho_min_n, ortho_max_e, ortho_max_n = read_ortho_bounds(opened_ortho, ortho_bounds)
            ortho_img = opened_ortho.convert("RGB")
    except FootprintOverlayError:
        raise
    except Exception as ex:
        raise FootprintOverlayError(f"Failed to open ortho image {ortho_path}: {ex}") from ex

    crop_min_e = origin_e + min_gx * m_per_gu
    crop_max_e = origin_e + max_gx * m_per_gu
    crop_min_n = origin_n + min_gy * m_per_gu
    crop_max_n = origin_n + max_gy * m_per_gu

    scale_x = (ortho_max_e - ortho_min_e) / float(ortho_img.width)
    scale_y = (ortho_max_n - ortho_min_n) / float(ortho_img.height)

    px0 = (crop_min_e - ortho_min_e) / scale_x
    px1 = (crop_max_e - ortho_min_e) / scale_x
    py0 = (ortho_max_n - crop_max_n) / scale_y
    py1 = (ortho_max_n - crop_min_n) / scale_y

    int_box = (int(round(px0)), int(round(py0)), int(round(px1)), int(round(py1)))
    cropped_ortho = ortho_img.crop(int_box).convert("RGBA")
    w, h = cropped_ortho.size
    if w <= 0 or h <= 0:
        raise FootprintOverlayError(f"Crop window results in degenerate size {w}x{h}")

    # Draw mask cells on an RGBA overlay
    mask_layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    draw_mask = ImageDraw.Draw(mask_layer)

    metrics = compute_mask_metrics(cells, crop_game, spacing)
    min_sx = metrics["min_sx"]
    max_sx = metrics["max_sx"]
    min_sy = metrics["min_sy"]
    max_sy = metrics["max_sy"]

    # Iterate over cells, including half-spacing margin to cover edge overlaps
    for sy in range(min_sy - 1, max_sy + 2):
        for sx in range(min_sx - 1, max_sx + 2):
            flag = cells.get((sx, sy), 0)
            if flag == 0:
                continue

            gx = sx * spacing
            gy = sy * spacing
            c_min_e = origin_e + (gx - spacing / 2.0) * m_per_gu
            c_max_e = origin_e + (gx + spacing / 2.0) * m_per_gu
            c_min_n = origin_n + (gy - spacing / 2.0) * m_per_gu
            c_max_n = origin_n + (gy + spacing / 2.0) * m_per_gu

            cx0 = (c_min_e - ortho_min_e) / scale_x - int_box[0]
            cx1 = (c_max_e - ortho_min_e) / scale_x - int_box[0]
            cy0 = (ortho_max_n - c_max_n) / scale_y - int_box[1]
            cy1 = (ortho_max_n - c_min_n) / scale_y - int_box[1]

            if cx1 <= 0 or cx0 >= w or cy1 <= 0 or cy0 >= h:
                continue

            if (flag & FLAG_IMPASSABLE) != 0:
                fill = COLOR_IMPASSABLE_FILL
                stroke = COLOR_IMPASSABLE_STROKE
            elif (flag & FLAG_UNBUILDABLE) != 0:
                fill = COLOR_UNBUILDABLE_FILL
                stroke = COLOR_UNBUILDABLE_STROKE
            else:
                continue

            draw_mask.rectangle((cx0, cy0, cx1, cy1), fill=fill, outline=stroke, width=1)

    blended_scene = Image.alpha_composite(cropped_ortho, mask_layer)

    # Unit tracks and graze cell markers layer
    if tracks or graze:
        tracks_layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        draw_tracks = ImageDraw.Draw(tracks_layer)

        # Draw graze cells
        for col, row in graze:
            gx = col * spacing
            gy = row * spacing
            c_min_e = origin_e + (gx - spacing / 2.0) * m_per_gu
            c_max_e = origin_e + (gx + spacing / 2.0) * m_per_gu
            c_min_n = origin_n + (gy - spacing / 2.0) * m_per_gu
            c_max_n = origin_n + (gy + spacing / 2.0) * m_per_gu

            cx0 = (c_min_e - ortho_min_e) / scale_x - int_box[0]
            cx1 = (c_max_e - ortho_min_e) / scale_x - int_box[0]
            cy0 = (ortho_max_n - c_max_n) / scale_y - int_box[1]
            cy1 = (ortho_max_n - c_min_n) / scale_y - int_box[1]

            if cx1 <= 0 or cx0 >= w or cy1 <= 0 or cy0 >= h:
                continue

            draw_tracks.rectangle((cx0, cy0, cx1, cy1), fill=COLOR_GRAZE_FILL, outline=COLOR_GRAZE_STROKE, width=2)
            draw_tracks.line([(cx0 - 4, cy0), (cx0, cy0), (cx0, cy0 - 4)], fill=COLOR_GRAZE_STROKE, width=2)
            draw_tracks.line([(cx1 + 4, cy0), (cx1, cy0), (cx1, cy0 - 4)], fill=COLOR_GRAZE_STROKE, width=2)
            draw_tracks.line([(cx0 - 4, cy1), (cx0, cy1), (cx0, cy1 + 4)], fill=COLOR_GRAZE_STROKE, width=2)
            draw_tracks.line([(cx1 + 4, cy1), (cx1, cy1), (cx1, cy1 + 4)], fill=COLOR_GRAZE_STROKE, width=2)

            label = f"Graze ({col},{row})"
            font_badge = ImageFont.load_default(10)
            tw = draw_tracks.textlength(label, font=font_badge)
            bx = cx1 + 8
            by = cy0 - 18
            if bx + tw + 10 > w:
                bx = cx0 - tw - 16
            if by < 4:
                by = cy1 + 6
            center_x = (cx0 + cx1) / 2.0
            center_y = (cy0 + cy1) / 2.0
            draw_tracks.line([(center_x, center_y), (bx if bx > cx1 else bx + tw + 8, by + 8)], fill=COLOR_GRAZE_STROKE, width=1)
            draw_tracks.rounded_rectangle((bx, by, bx + tw + 8, by + 16), radius=3, fill=(15, 20, 26, 230), outline=COLOR_GRAZE_STROKE, width=1)
            draw_tracks.text((bx + 4, by + 2), label, fill=COLOR_GRAZE_STROKE, font=font_badge)

        # Draw routes
        for idx, (aid, raw_pts) in enumerate(sorted(tracks.items())):
            palette = TRACK_PALETTE[idx % len(TRACK_PALETTE)]
            pixel_pts: list[tuple[float, float]] = []
            for gx, gy in raw_pts:
                c_e = origin_e + gx * m_per_gu
                c_n = origin_n + gy * m_per_gu
                px = (c_e - ortho_min_e) / scale_x - int_box[0]
                py = (ortho_max_n - c_n) / scale_y - int_box[1]
                if not pixel_pts or math.hypot(px - pixel_pts[-1][0], py - pixel_pts[-1][1]) >= 1.0:
                    pixel_pts.append((px, py))

            if not pixel_pts:
                continue

            if len(pixel_pts) >= 2:
                draw_tracks.line(pixel_pts, fill=palette["casing"], width=6)
                draw_tracks.line(pixel_pts, fill=palette["stroke"], width=3)

            font_marker = ImageFont.load_default(10)

            # Start marker
            sx, sy = pixel_pts[0]
            draw_tracks.ellipse((sx - 7, sy - 7, sx + 7, sy + 7), fill=(0, 0, 0, 220), outline=palette["casing"])
            draw_tracks.ellipse((sx - 5, sy - 5, sx + 5, sy + 5), fill=palette["stroke"])
            draw_tracks.ellipse((sx - 2, sy - 2, sx + 2, sy + 2), fill=(255, 255, 255, 255))
            s_tag = f"S{idx + 1}"
            stw = draw_tracks.textlength(s_tag, font=font_marker)
            draw_tracks.rounded_rectangle((sx + 8, sy - 8, sx + 14 + stw, sy + 6), radius=3, fill=(15, 20, 26, 220), outline=palette["stroke"], width=1)
            draw_tracks.text((sx + 11, sy - 7), s_tag, fill=COLOR_TEXT_PRIMARY, font=font_marker)

            # Goal marker
            gx, gy = pixel_pts[-1]
            draw_tracks.ellipse((gx - 9, gy - 9, gx + 9, gy + 9), fill=(0, 0, 0, 220), outline=palette["casing"])
            draw_tracks.ellipse((gx - 7, gy - 7, gx + 7, gy + 7), fill=palette["stroke"])
            draw_tracks.ellipse((gx - 4, gy - 4, gx + 4, gy + 4), fill=(15, 20, 26, 255))
            draw_tracks.ellipse((gx - 2, gy - 2, gx + 2, gy + 2), fill=(255, 255, 255, 255))
            g_tag = f"G{idx + 1}"
            gtw = draw_tracks.textlength(g_tag, font=font_marker)
            draw_tracks.rounded_rectangle((gx + 10, gy - 8, gx + 16 + gtw, gy + 6), radius=3, fill=(15, 20, 26, 220), outline=palette["stroke"], width=1)
            draw_tracks.text((gx + 13, gy - 7), g_tag, fill=COLOR_TEXT_PRIMARY, font=font_marker)

        blended_scene = Image.alpha_composite(blended_scene, tracks_layer)

    # In-scene cartographic elements
    if w >= 60 and h >= 60:
        draw_north_arrow(blended_scene, w - 24, 28)
        m_per_px = (crop_max_e - crop_min_e) / float(w)
        draw_scale_bar(blended_scene, 18, h - 22, m_per_px, w)

    # Framing: Header and Footer
    has_extensions = bool(tracks or graze)
    header_h = 56
    footer_h = 76 if has_extensions else 56
    canvas_w = max(w, 480)
    canvas_h = header_h + h + footer_h
    canvas = Image.new("RGB", (canvas_w, canvas_h), COLOR_HEADER_BG)

    scene_x = (canvas_w - w) // 2
    scene_y = header_h
    canvas.paste(blended_scene.convert("RGB"), (scene_x, scene_y))

    draw_canvas = ImageDraw.Draw(canvas)
    font_title = ImageFont.load_default(13)
    font_sub = ImageFont.load_default(10)

    # Header text
    payload_name = os.path.basename(os.path.abspath(payload_dir))
    header_title = title if title else f"Footprint Overlay: {payload_name}"
    sub_parts = [
        f"Game BBox: [{min_gx:g}, {min_gy:g}, {max_gx:g}, {max_gy:g}]",
        f"UTM: [{crop_min_e:.1f}, {crop_min_n:.1f}, {crop_max_e:.1f}, {crop_max_n:.1f}]",
    ]
    if tracks:
        sub_parts.append(f"Routes: {len(tracks)}")
    if graze:
        sub_parts.append(f"Graze Cells: {len(graze)}")
    header_sub = "  |  ".join(sub_parts)
    draw_canvas.text((12, 10), header_title, fill=COLOR_TEXT_PRIMARY, font=font_title)
    draw_canvas.text((12, 32), header_sub, fill=COLOR_TEXT_SECONDARY, font=font_sub)

    # Footer elements
    footer_top = header_h + h
    font_legend = ImageFont.load_default(10)

    if not has_extensions:
        # Impassable swatch
        draw_canvas.rectangle((12, footer_top + 10, 22, footer_top + 20), fill=(235, 45, 45), outline=(255, 70, 70))
        imp_text = f"Impassable: {metrics['impassable_count']} ({metrics['impassable_share']*100:.1f}%)"
        draw_canvas.text((28, footer_top + 9), imp_text, fill=COLOR_TEXT_PRIMARY, font=font_legend)

        # Unbuildable swatch
        draw_canvas.rectangle((12, footer_top + 30, 22, footer_top + 40), fill=(255, 160, 20), outline=(255, 180, 40))
        unb_text = f"Unbuildable: {metrics['unbuildable_only_count']} ({metrics['unbuildable_only_share']*100:.1f}%)"
        draw_canvas.text((28, footer_top + 29), unb_text, fill=COLOR_TEXT_PRIMARY, font=font_legend)

        # Summary
        sum_text = f"Masked: {metrics['masked_count']}/{metrics['total_cells']} ({metrics['masked_share']*100:.1f}%)"
        draw_canvas.text((210, footer_top + 9), sum_text, fill=COLOR_TEXT_SECONDARY, font=font_legend)

        # Attribution line on the right
        attr_len = draw_canvas.textlength(ATTRIBUTION_TEXT, font=font_legend)
        attr_x = max(210.0, canvas_w - attr_len - 14)
        draw_canvas.text((attr_x, footer_top + 29), ATTRIBUTION_TEXT, fill=COLOR_TEXT_MUTED, font=font_legend)
    else:
        # Row 1: Mask layers, totals, and attribution
        draw_canvas.rectangle((12, footer_top + 10, 22, footer_top + 20), fill=(235, 45, 45), outline=(255, 70, 70))
        imp_text = f"Impassable: {metrics['impassable_count']} ({metrics['impassable_share']*100:.1f}%)"
        draw_canvas.text((28, footer_top + 9), imp_text, fill=COLOR_TEXT_PRIMARY, font=font_legend)

        draw_canvas.rectangle((190, footer_top + 10, 200, footer_top + 20), fill=(255, 160, 20), outline=(255, 180, 40))
        unb_text = f"Unbuildable: {metrics['unbuildable_only_count']} ({metrics['unbuildable_only_share']*100:.1f}%)"
        draw_canvas.text((206, footer_top + 9), unb_text, fill=COLOR_TEXT_PRIMARY, font=font_legend)

        sum_text = f"Masked: {metrics['masked_count']}/{metrics['total_cells']} ({metrics['masked_share']*100:.1f}%)"
        draw_canvas.text((370, footer_top + 9), sum_text, fill=COLOR_TEXT_SECONDARY, font=font_legend)

        attr_len = draw_canvas.textlength(ATTRIBUTION_TEXT, font=font_legend)
        attr_x = max(550.0, canvas_w - attr_len - 14)
        draw_canvas.text((attr_x, footer_top + 9), ATTRIBUTION_TEXT, fill=COLOR_TEXT_MUTED, font=font_legend)

        # Row 2: Route tracks and graze markers
        cur_x = 12.0
        for idx, (aid, _) in enumerate(sorted(tracks.items())):
            palette = TRACK_PALETTE[idx % len(TRACK_PALETTE)]
            draw_canvas.line([(cur_x, footer_top + 39), (cur_x + 16, footer_top + 39)], fill=palette["stroke"], width=3)
            draw_canvas.ellipse((cur_x + 6, footer_top + 37, cur_x + 10, footer_top + 41), fill=(255, 255, 255))
            r_label = f"Route {idx + 1} ({aid})"
            draw_canvas.text((cur_x + 22, footer_top + 34), r_label, fill=COLOR_TEXT_PRIMARY, font=font_legend)
            cur_x += draw_canvas.textlength(r_label, font=font_legend) + 36

        if graze:
            draw_canvas.rectangle((cur_x, footer_top + 33, cur_x + 12, footer_top + 45), fill=COLOR_GRAZE_FILL, outline=COLOR_GRAZE_STROKE, width=2)
            g_label = f"Checker-Graze ({len(graze)} cells: 134,260; 125,185; 190,243)" if len(graze) == 3 else f"Checker-Graze ({len(graze)} cells)"
            draw_canvas.text((cur_x + 18, footer_top + 34), g_label, fill=COLOR_GRAZE_STROKE, font=font_legend)

    return canvas


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Render a footprint overlay still displaying payload mask cells over ortho imagery."
    )
    parser.add_argument(
        "--payload",
        help="Path to geospatial payload directory (containing payload.json)",
    )
    parser.add_argument(
        "--ortho",
        help="Path to ortho GeoTIFF image",
    )
    parser.add_argument(
        "--out",
        "-o",
        "--output",
        dest="out",
        help="Output PNG path for rendered overlay still",
    )
    parser.add_argument(
        "--crop",
        nargs=4,
        type=float,
        metavar=("MIN_X", "MIN_Y", "MAX_X", "MAX_Y"),
        help="Crop window in game coordinates (min_x min_y max_x max_y)",
    )
    parser.add_argument(
        "--ortho-bounds",
        nargs=4,
        type=float,
        metavar=("MIN_E", "MIN_N", "MAX_E", "MAX_N"),
        help="Explicit ortho bounds override in UTM (min_easting min_northing max_easting max_northing)",
    )
    parser.add_argument(
        "--title",
        help="Optional title override",
    )
    parser.add_argument(
        "--manifest",
        help="Optional capture manifest JSON containing unit tracks to overlay",
    )
    parser.add_argument(
        "--graze-cells",
        nargs="*",
        help="Optional graze cell coordinates to highlight (e.g. '134,260 125,185 190,243')",
    )
    # Positional fallbacks
    parser.add_argument("pos_payload", nargs="?", help=argparse.SUPPRESS)
    parser.add_argument("pos_ortho", nargs="?", help=argparse.SUPPRESS)
    parser.add_argument("pos_out", nargs="?", help=argparse.SUPPRESS)

    args = parser.parse_args()

    payload_dir = args.payload or args.pos_payload
    ortho_path = args.ortho or args.pos_ortho
    out_path = args.out or args.pos_out

    if not payload_dir or not ortho_path or not out_path:
        parser.error("Missing required arguments: payload, ortho, and output paths must be specified.")

    crop_game = tuple(args.crop) if args.crop is not None else None
    ortho_bounds = tuple(args.ortho_bounds) if args.ortho_bounds is not None else None
    graze_cells = parse_graze_cells(args.graze_cells)

    try:
        overlay = render_footprint_overlay(
            payload_dir=payload_dir,
            ortho_path=ortho_path,
            crop_game=crop_game,
            ortho_bounds=ortho_bounds,
            title=args.title,
            manifest_path=args.manifest,
            graze_cells=graze_cells,
        )
        os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
        overlay.save(out_path, format="PNG")
    except FootprintOverlayError as ex:
        sys.stderr.write(f"annotate_footprint_overlay error: {ex}\n")
        sys.exit(1)
    except Exception as ex:
        sys.stderr.write(f"Unexpected error: {ex}\n")
        sys.exit(1)


if __name__ == "__main__":
    main()
