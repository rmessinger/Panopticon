#!/usr/bin/env python3
"""Building footprint overlay projection and verification tool.

Projects building footprint polygon borders from runtime-ground sidecar vertices
onto capture frames using RECORDED camera matrices (View, Projection, Viewport)
from capture manifest.json. Inferred-camera fallback paths are strictly deleted;
missing camera matrices or sidecars fail closed.

OFFSET PROVENANCE (Architect-audited):
ACTOR_CENTER_Z_OFFSET = 5.75 game units.
Citation:
1. Installed game data: Data/INI/Object/FactionUnit.ini in INI.big
   (path: /home/rmessinger/dev/Command and Conquer Generals/INI.big).
   Block 'Object AmericaVehicleHumvee':
     Geometry = BOX
     GeometryMajorRadius = 14.0
     GeometryMinorRadius = 7.0
     GeometryHeight = 11.5
     GeometryIsSmall = Yes
2. Engine INI parser in src/Panopticon.Simulation/Logic/Object/ObjectDefinition.cs:312:
   { "GeometryHeight", (parser, x) => x._currentGeometryShape.Height = parser.ParseFloat() }
   parses shape.Height = 11.5f.
3. Engine geometry bounds in src/Panopticon.Simulation/Logic/Object/Geometry.cs:127-145:
   For GeometryType.Box: minZ = 0; maxZ = shape.Height = 11.5f; MaxZ = 11.5f.
4. Capture center derivation in src/Panopticon.Tools.TestHarness/ScenarioCaptureRunner.cs:1003:
   var center = gameObject.Translation + new Vector3(0f, 0f, gameObject.Definition.Geometry.MaxZ / 2f);
   where MaxZ / 2f = 11.5f / 2f = 5.75f.
5. All 4 capture actors (humvee-1..humvee-4) in capture-sedan-footprints.json have
   Definition = "AmericaVehicleHumvee".
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import subprocess
import sys
from typing import Any, Dict, List, Optional, Sequence, Tuple

from PIL import Image, ImageDraw, ImageFont


# Engine constants
VIEWPORT_WIDTH = 1024
VIEWPORT_HEIGHT = 768
ACTOR_CENTER_Z_OFFSET = 5.75  # AmericaVehicleHumvee Geometry.MaxZ / 2 (11.5 / 2), see module docstring

# Styling constants
COLOR_CYAN_STROKE = (0, 240, 255, 230)
COLOR_YELLOW_STROKE = (255, 235, 50, 230)
COLOR_FILL_TRANSLUCENT = (0, 200, 255, 30)  # <= 25% transparent fill
COLOR_TEXT_PRIMARY = (255, 255, 255, 255)
COLOR_TEXT_MUTED = (180, 200, 220, 240)
COLOR_BADGE_BG = (10, 15, 20, 200)

LABEL_LEGEND = "Building footprints, projected overlay"
LABEL_OVERLAY = "projected overlay (x-ray: drawn over terrain)"
LABEL_CAPTION = "visualization only, not navigation acceptance"
SPEED_DISCLOSURE = "10fps, 2x speed (stride 6)"


def multiply_matrices_4x4(a: Sequence[float], b: Sequence[float]) -> List[float]:
    """Multiply two 4x4 row-major matrices (result = a * b). Pure stdlib."""
    if len(a) != 16 or len(b) != 16:
        raise ValueError(f"Expected 16 elements for 4x4 matrices, got {len(a)} and {len(b)}")

    res = [0.0] * 16
    for r in range(4):
        r4 = r * 4
        for c in range(4):
            res[r4 + c] = (
                a[r4 + 0] * b[0 * 4 + c]
                + a[r4 + 1] * b[1 * 4 + c]
                + a[r4 + 2] * b[2 * 4 + c]
                + a[r4 + 3] * b[3 * 4 + c]
            )
    return res


def veldrid_project(
    source: Sequence[float],
    proj: Sequence[float],
    view: Sequence[float],
    viewport: Dict[str, Any],
    world: Optional[Sequence[float]] = None,
) -> Tuple[float, float, float, float, float, float, float]:
    """Replicate Panopticon.Graphics.ViewportExtensions.Project exactly without external libs.

    Returns:
        (screen_x, screen_y, screen_z, w, ndc_x, ndc_y, ndc_z)
    """
    if world is None:
        world = [
            1.0, 0.0, 0.0, 0.0,
            0.0, 1.0, 0.0, 0.0,
            0.0, 0.0, 1.0, 0.0,
            0.0, 0.0, 0.0, 1.0,
        ]

    wv = multiply_matrices_4x4(world, view)
    matrix = multiply_matrices_4x4(wv, proj)

    sx, sy, sz = float(source[0]), float(source[1]), float(source[2])

    vx = sx * matrix[0] + sy * matrix[4] + sz * matrix[8] + matrix[12]
    vy = sx * matrix[1] + sy * matrix[5] + sz * matrix[9] + matrix[13]
    vz = sx * matrix[2] + sy * matrix[6] + sz * matrix[10] + matrix[14]
    w = sx * matrix[3] + sy * matrix[7] + sz * matrix[11] + matrix[15]

    if abs(w - 1.0) > 1.401298e-45:
        if abs(w) > 1e-15:
            ndc_x = vx / w
            ndc_y = vy / w
            ndc_z = vz / w
        else:
            ndc_x = ndc_y = ndc_z = float("inf")
    else:
        ndc_x, ndc_y, ndc_z = vx, vy, vz

    vw = float(viewport.get("Width", VIEWPORT_WIDTH))
    vh = float(viewport.get("Height", VIEWPORT_HEIGHT))
    vx_offset = float(viewport.get("X", 0.0))
    vy_offset = float(viewport.get("Y", 0.0))
    min_depth = float(viewport.get("MinDepth", 0.0))
    max_depth = float(viewport.get("MaxDepth", 1.0))

    screen_x = (((ndc_x + 1.0) * 0.5) * vw) + vx_offset
    screen_y = (((-ndc_y + 1.0) * 0.5) * vh) + vy_offset
    screen_z = (ndc_z * (max_depth - min_depth)) + min_depth

    return screen_x, screen_y, screen_z, w, ndc_x, ndc_y, ndc_z


def clip_homogeneous_segment(
    p1: Sequence[float],
    p2: Sequence[float],
    viewport: Dict[str, Any],
) -> Optional[Tuple[Tuple[float, float], Tuple[float, float]]]:
    """Clip segment in 4D homogeneous clip space BEFORE perspective division.

    Clip planes (Right-handed perspective, NDC z in [0, 1]):
        w + x >= 0  (left)
        w - x >= 0  (right)
        w + y >= 0  (bottom)
        w - y >= 0  (top)
        z >= 0      (near plane: System.Numerics depth convention, w > 0)
        w - z >= 0  (far plane)
        w >= 1e-4   (positive w guard)
    """
    t_enter = 0.0
    t_exit = 1.0
    dp = [p2[i] - p1[i] for i in range(4)]

    # Planes expressed as (plane_func(p), plane_func(dp))
    planes = [
        (lambda p: p[3] + p[0], lambda d: d[3] + d[0]),  # left
        (lambda p: p[3] - p[0], lambda d: d[3] - d[0]),  # right
        (lambda p: p[3] + p[1], lambda d: d[3] + d[1]),  # bottom
        (lambda p: p[3] - p[1], lambda d: d[3] - d[1]),  # top
        (lambda p: p[2],        lambda d: d[2]),         # near (z >= 0)
        (lambda p: p[3] - p[2], lambda d: d[3] - d[2]),  # far (w - z >= 0)
        (lambda p: p[3] - 1e-4, lambda d: d[3]),         # positive w
    ]

    for c_p, c_d in planes:
        d1 = c_p(p1)
        dd = c_d(dp)

        if dd == 0.0:
            if d1 < 0.0:
                return None
        else:
            t = -d1 / dd
            if dd > 0.0:  # entering
                if t > t_enter:
                    t_enter = t
            else:  # exiting
                if t < t_exit:
                    t_exit = t
            if t_enter > t_exit:
                return None

    if t_enter > t_exit:
        return None

    pA = [p1[i] + t_enter * dp[i] for i in range(4)]
    pB = [p1[i] + t_exit * dp[i] for i in range(4)]

    if pA[3] <= 0.0 or pB[3] <= 0.0:
        return None

    vw = float(viewport.get("Width", VIEWPORT_WIDTH))
    vh = float(viewport.get("Height", VIEWPORT_HEIGHT))
    vx = float(viewport.get("X", 0.0))
    vy = float(viewport.get("Y", 0.0))

    sAx = (((pA[0] / pA[3] + 1.0) * 0.5) * vw) + vx
    sAy = (((-pA[1] / pA[3] + 1.0) * 0.5) * vh) + vy
    sBx = (((pB[0] / pB[3] + 1.0) * 0.5) * vw) + vx
    sBy = (((-pB[1] / pB[3] + 1.0) * 0.5) * vh) + vy

    return ((sAx, sAy), (sBx, sBy))


def load_sidecar_vertices(sidecar_path: str) -> Dict[str, Any]:
    """Load and validate ground-vertices.json sidecar."""
    if not os.path.isfile(sidecar_path):
        raise FileNotFoundError(f"Sidecar file not found: {sidecar_path}")

    with open(sidecar_path, "r", encoding="utf-8") as fh:
        try:
            data = json.load(fh)
        except json.JSONDecodeError as ex:
            raise ValueError(f"Invalid JSON in sidecar {sidecar_path}: {ex}") from ex

    if not isinstance(data, dict):
        raise ValueError(f"Sidecar root must be a JSON object: {sidecar_path}")

    if "polygons" not in data or not isinstance(data["polygons"], list):
        raise ValueError(f"Sidecar missing 'polygons' list: {sidecar_path}")

    for idx, poly in enumerate(data["polygons"]):
        if not isinstance(poly, dict) or "outer" not in poly:
            raise ValueError(f"Polygon index {idx} in sidecar missing 'outer' ring: {sidecar_path}")
        if not isinstance(poly["outer"], list):
            raise ValueError(f"Polygon index {idx} 'outer' must be a list of vertices: {sidecar_path}")

    return data


def validate_camera_block(camera: Any, frame_idx: int) -> Dict[str, Any]:
    """Validate per-frame Camera block matching exact contract. Fail closed if missing/malformed."""
    if not isinstance(camera, dict):
        raise ValueError(
            f"Frame {frame_idx} is missing required 'Camera' block. "
            "Reconstruction fallback is removed; recorded capture matrices are required."
        )

    for key in ("View", "Projection", "Viewport"):
        if key not in camera:
            raise ValueError(f"Frame {frame_idx} Camera block missing required key '{key}'")

    view = camera["View"]
    proj = camera["Projection"]
    viewport = camera["Viewport"]

    if not isinstance(view, (list, tuple)) or len(view) != 16:
        raise ValueError(f"Frame {frame_idx} Camera.View must be 16 floats")

    if not isinstance(proj, (list, tuple)) or len(proj) != 16:
        raise ValueError(f"Frame {frame_idx} Camera.Projection must be 16 floats")

    if not isinstance(viewport, dict):
        raise ValueError(f"Frame {frame_idx} Camera.Viewport must be a dict")

    for vp_key in ("Width", "Height"):
        if vp_key not in viewport:
            raise ValueError(f"Frame {frame_idx} Camera.Viewport missing '{vp_key}'")

    return {
        "View": [float(x) for x in view],
        "Projection": [float(x) for x in proj],
        "Viewport": viewport,
    }


def densify_polyline(
    points: Sequence[Sequence[float]],
    max_segment_length: float = 5.0,
) -> List[List[float]]:
    """Densify 3D points so segment lengths stay below threshold."""
    if len(points) < 2:
        return [list(p) for p in points]

    result: List[List[float]] = [[float(x) for x in points[0]]]
    for i in range(len(points) - 1):
        p1 = points[i]
        p2 = points[i + 1]
        dx = float(p2[0]) - float(p1[0])
        dy = float(p2[1]) - float(p1[1])
        dz = float(p2[2]) - float(p1[2])
        dist = math.sqrt(dx * dx + dy * dy + dz * dz)
        if dist > max_segment_length:
            steps = int(math.ceil(dist / max_segment_length))
            for s in range(1, steps):
                t = s / steps
                result.append([
                    float(p1[0]) + dx * t,
                    float(p1[1]) + dy * t,
                    float(p1[2]) + dz * t,
                ])
        result.append([float(x) for x in p2])
    return result


def verify_manifest_and_sidecar(
    manifest_path: str,
    sidecar_path: str,
    frame_indices: Optional[Sequence[int]] = None,
) -> Dict[str, Any]:
    """Verify manifest recorded Camera blocks against anchors with Architect-ruled clip domain gate."""
    sidecar_data = load_sidecar_vertices(sidecar_path)

    if not os.path.isfile(manifest_path):
        raise FileNotFoundError(f"Manifest not found: {manifest_path}")

    with open(manifest_path, "r", encoding="utf-8") as fh:
        try:
            manifest = json.load(fh)
        except json.JSONDecodeError as ex:
            raise ValueError(f"Invalid JSON in manifest {manifest_path}: {ex}") from ex

    raw_frames = manifest.get("Frames", [])
    if not raw_frames:
        raise ValueError(f"Manifest contains no 'Frames': {manifest_path}")

    target_indices = (
        list(frame_indices)
        if frame_indices is not None
        else list(range(0, len(raw_frames)))
    )

    unfiltered_max = 0.0
    visible_max = 0.0
    total_anchors = 0
    visible_anchors = 0
    excluded: List[Dict[str, Any]] = []

    for f_idx in target_indices:
        if f_idx >= len(raw_frames):
            continue
        frame = raw_frames[f_idx]
        camera_raw = frame.get("Camera")
        camera = validate_camera_block(camera_raw, f_idx)

        view = camera["View"]
        proj = camera["Projection"]
        viewport = camera["Viewport"]
        vw = float(viewport.get("Width", VIEWPORT_WIDTH))
        vh = float(viewport.get("Height", VIEWPORT_HEIGHT))
        vx_off = float(viewport.get("X", 0.0))
        vy_off = float(viewport.get("Y", 0.0))
        min_d = float(viewport.get("MinDepth", 0.0))
        max_d = float(viewport.get("MaxDepth", 1.0))

        frame_visible_count = 0
        actors = frame.get("Actors", [])
        for a in actors:
            if "WorldX" not in a or "WorldY" not in a or "WorldZ" not in a:
                continue
            wx = float(a["WorldX"])
            wy = float(a["WorldY"])
            wz = float(a["WorldZ"])
            rec_x = float(a["X"])
            rec_y = float(a["Y"])

            pos = (wx, wy, wz + ACTOR_CENTER_Z_OFFSET)
            sx, sy, sz, w, ndc_x, ndc_y, ndc_z = veldrid_project(pos, proj, view, viewport)

            err_x = abs(sx - rec_x)
            err_y = abs(sy - rec_y)
            err = max(err_x, err_y)

            total_anchors += 1
            if err > unfiltered_max:
                unfiltered_max = err

            # Homogeneous clip frustum check (Architect ruling):
            # 1. positive w: w > 0.0
            # 2. depth within backend convention: min_d <= ndc_z <= max_d ([0, 1])
            # 3. x/y within viewport bounds: vx_off <= sx <= vx_off + vw and vy_off <= sy <= vy_off + vh
            reasons = []
            if w <= 0.0:
                reasons.append(f"w<=0 ({w:.4f})")
            if not (min_d <= ndc_z <= max_d):
                reasons.append(f"depth ({ndc_z:.4f} outside [{min_d}, {max_d}])")
            if not (vx_off <= sx <= vx_off + vw and vy_off <= sy <= vy_off + vh):
                reasons.append(f"bounds (sx={sx:.1f}, sy={sy:.1f} outside [{vx_off}..{vx_off+vw}, {vy_off}..{vy_off+vh}])")

            if not reasons:
                visible_anchors += 1
                frame_visible_count += 1
                if err > visible_max:
                    visible_max = err
            else:
                excluded.append({
                    "frame_index": f_idx,
                    "actor_id": a.get("Id", "unknown"),
                    "recorded": (rec_x, rec_y),
                    "projected": (sx, sy, sz),
                    "error": err,
                    "reasons": reasons,
                    "reason_str": ", ".join(reasons),
                })

        # Assert every annotated frame has at least one visible anchor (fail closed)
        if frame_visible_count == 0:
            raise AssertionError(f"Frame {f_idx} has 0 visible anchors inside camera frustum!")

    passed = visible_max < 1.0 and visible_anchors > 0

    return {
        "passed": passed,
        "visible_max": visible_max,
        "unfiltered_max": unfiltered_max,
        "visible_anchors": visible_anchors,
        "excluded_anchors": len(excluded),
        "total_anchors": total_anchors,
        "frame_count": len(target_indices),
        "excluded": excluded,
        "polygon_count": len(sidecar_data.get("polygons", [])),
    }


def render_annotated_frame(
    base_image: Image.Image,
    polygons: List[Dict[str, Any]],
    camera: Dict[str, Any],
    actors: Optional[List[Dict[str, Any]]] = None,
    frame_index: int = 0,
) -> Image.Image:
    """Project building footprint polygons and annotate onto an image copy."""
    img = base_image.copy().convert("RGBA")
    draw_img = ImageDraw.Draw(img)

    view = camera["View"]
    proj = camera["Projection"]
    viewport = camera["Viewport"]
    matrix = multiply_matrices_4x4(view, proj)

    # Project and draw footprint polygons via homogeneous clip-space clipping
    for poly in polygons:
        outer = poly.get("outer", [])
        holes = poly.get("holes", [])

        # Outer ring
        if outer:
            densified_outer = densify_polyline(outer, max_segment_length=5.0)
            clip_pts = []
            for p in densified_outer:
                x, y, z = p[0], p[1], p[2]
                cx = x * matrix[0] + y * matrix[4] + z * matrix[8] + matrix[12]
                cy = x * matrix[1] + y * matrix[5] + z * matrix[9] + matrix[13]
                cz = x * matrix[2] + y * matrix[6] + z * matrix[10] + matrix[14]
                cw = x * matrix[3] + y * matrix[7] + z * matrix[11] + matrix[15]
                clip_pts.append([cx, cy, cz, cw])

            for i in range(len(clip_pts)):
                p1 = clip_pts[i]
                p2 = clip_pts[(i + 1) % len(clip_pts)]
                seg = clip_homogeneous_segment(p1, p2, viewport)
                if seg:
                    draw_img.line(seg, fill=COLOR_CYAN_STROKE, width=2)

        # Holes (inner rings)
        for hole in holes:
            if not hole:
                continue
            densified_hole = densify_polyline(hole, max_segment_length=5.0)
            clip_hole_pts = []
            for p in densified_hole:
                x, y, z = p[0], p[1], p[2]
                cx = x * matrix[0] + y * matrix[4] + z * matrix[8] + matrix[12]
                cy = x * matrix[1] + y * matrix[5] + z * matrix[9] + matrix[13]
                cz = x * matrix[2] + y * matrix[6] + z * matrix[10] + matrix[14]
                cw = x * matrix[3] + y * matrix[7] + z * matrix[11] + matrix[15]
                clip_hole_pts.append([cx, cy, cz, cw])

            for i in range(len(clip_hole_pts)):
                p1 = clip_hole_pts[i]
                p2 = clip_hole_pts[(i + 1) % len(clip_hole_pts)]
                seg = clip_homogeneous_segment(p1, p2, viewport)
                if seg:
                    draw_img.line(seg, fill=COLOR_YELLOW_STROKE, width=2)

    # Actor labels (S1-G4 style)
    if actors:
        for a in actors:
            aid = a.get("Id", "")
            rec_x = a.get("X")
            rec_y = a.get("Y")
            if rec_x is not None and rec_y is not None:
                lbl = aid.replace("humvee-", "H")
                if 0 <= rec_x <= img.width and 0 <= rec_y <= img.height:
                    draw_img.rectangle(
                        [rec_x - 14, rec_y - 20, rec_x + 14, rec_y - 6],
                        fill=COLOR_BADGE_BG,
                    )
                    draw_img.text((rec_x - 10, rec_y - 20), lbl, fill=COLOR_TEXT_PRIMARY)

    # Frame badges & legend
    draw_img.rectangle([10, 10, 360, 52], fill=COLOR_BADGE_BG)
    draw_img.text((16, 14), LABEL_LEGEND, fill=COLOR_CYAN_STROKE)
    draw_img.text((16, 32), LABEL_OVERLAY, fill=COLOR_TEXT_MUTED)

    # Footer caption
    vh = float(viewport.get("Height", img.height))
    draw_img.rectangle([10, int(vh) - 28, 480, int(vh) - 8], fill=COLOR_BADGE_BG)
    draw_img.text((16, int(vh) - 26), f"{LABEL_CAPTION} | {SPEED_DISCLOSURE}", fill=COLOR_TEXT_MUTED)

    return img.convert("RGB")


def render_capture_artifacts(
    manifest_path: str,
    sidecar_path: str,
    frames_dir: str,
    output_dir: str,
    kml_path: Optional[str] = None,
    verification_report: Optional[Dict[str, Any]] = None,
) -> Dict[str, Any]:
    """Render 118-frame sequence, poster, 30-frame retained set, and overlay-manifest."""
    with open(manifest_path, "r", encoding="utf-8") as fh:
        manifest = json.load(fh)

    sidecar = load_sidecar_vertices(sidecar_path)
    polygons = sidecar.get("polygons", [])

    with open(sidecar_path, "rb") as fh:
        sidecar_sha = hashlib.sha256(fh.read()).hexdigest()

    cam_hasher = hashlib.sha256()
    for f in manifest.get("Frames", []):
        cam = f.get("Camera")
        if cam:
            cam_hasher.update(json.dumps(cam, sort_keys=True).encode("utf-8"))
    recorded_matrix_sha = cam_hasher.hexdigest()

    kml_sha = None
    if kml_path and os.path.isfile(kml_path):
        with open(kml_path, "rb") as fh:
            kml_sha = hashlib.sha256(fh.read()).hexdigest()

    os.makedirs(output_dir, exist_ok=True)
    frames_sub_dir = os.path.join(output_dir, "frames", "r2-03-footprints-v2-outlines")
    os.makedirs(frames_sub_dir, exist_ok=True)

    retained_indices = set(range(0, 900, 30))
    annotated_even_indices = list(range(0, min(235, len(manifest.get("Frames", []))), 2))

    retained_shas: Dict[str, str] = {}
    raw_frame_shas: Dict[str, str] = {}
    temp_even_paths: List[str] = []

    poster_path = os.path.join(output_dir, "r2-03-footprints-street-movement-outlines-v1-poster.png")
    gif_path = os.path.join(output_dir, "r2-03-footprints-street-movement-outlines-v1.gif")

    # Render frames
    for idx in range(len(manifest.get("Frames", []))):
        f = manifest["Frames"][idx]
        filename = f.get("File", f"frame_{idx:04d}.png")
        raw_path = os.path.join(frames_dir, filename)
        if not os.path.isfile(raw_path):
            continue

        with open(raw_path, "rb") as fh:
            raw_sha = hashlib.sha256(fh.read()).hexdigest()
        raw_frame_shas[filename] = raw_sha

        raw_img = Image.open(raw_path)
        camera = validate_camera_block(f.get("Camera"), idx)
        actors = f.get("Actors", [])

        ann_img = render_annotated_frame(raw_img, polygons, camera, actors, idx)

        # Poster at mid-drive (frame ~118 or middle of even sequence)
        if idx == annotated_even_indices[len(annotated_even_indices) // 2]:
            ann_img.save(poster_path)

        # Even sequence for GIF
        if idx in annotated_even_indices:
            tmp_path = os.path.join(frames_sub_dir, f"even_{idx:04d}.png")
            ann_img.save(tmp_path)
            temp_even_paths.append(tmp_path)

        # 30 retained frames
        if idx in retained_indices:
            out_retained = os.path.join(frames_sub_dir, filename)
            ann_img.save(out_retained)
            with open(out_retained, "rb") as fh:
                retained_shas[filename] = hashlib.sha256(fh.read()).hexdigest()

    # Generate GIF using ffmpeg
    if temp_even_paths:
        cmd = [
            "ffmpeg", "-y", "-framerate", "10",
            "-pattern_type", "glob", "-i", os.path.join(frames_sub_dir, "even_*.png"),
            "-vf", "scale=480:360,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse",
            "-loop", "0", gif_path,
        ]
        subprocess.run(cmd, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)

        # Clean up temporary even_*.png frames
        for p in temp_even_paths:
            if os.path.exists(p):
                os.remove(p)

    # Write SHA256SUMS in frames subdir
    sums_path = os.path.join(frames_sub_dir, "SHA256SUMS")
    with open(sums_path, "w", encoding="utf-8") as fh:
        for fname in sorted(retained_shas.keys()):
            fh.write(f"{retained_shas[fname]}  {fname}\n")

    # Format verification summary for manifest
    v_info = verification_report or {}
    overlay_manifest = {
        "scenario": manifest.get("Scenario", "sedan-footprints"),
        "retainedFramesCount": len(retained_shas),
        "retainedFrameShas": retained_shas,
        "rawFrameShas": {k: raw_frame_shas[k] for k in retained_shas if k in raw_frame_shas},
        "recordedMatrixSha256": recorded_matrix_sha,
        "sidecarSha256": sidecar_sha,
        "sidecarPath": sidecar_path,
        "kmlPath": kml_path,
        "kmlSha256": kml_sha,
        "speedDisclosure": SPEED_DISCLOSURE,
        "method": "recorded-camera-projection",
        "clipDomainGate": {
            "rule": "positive w AND depth in [0, 1] AND screen within viewport bounds",
            "visibleMaxErrorPx": v_info.get("visible_max"),
            "unfilteredMaxErrorPx": v_info.get("unfiltered_max"),
            "visibleAnchorsCount": v_info.get("visible_anchors"),
            "excludedAnchorsCount": v_info.get("excluded_anchors"),
            "excludedSummary": [
                {"frame": ex["frame_index"], "actor": ex["actor_id"], "errorPx": ex["error"], "reason": ex["reason_str"]}
                for ex in v_info.get("excluded", [])[:10]
            ],
        },
        "offsetProvenance": (
            "Data/INI/Object/FactionUnit.ini (Object AmericaVehicleHumvee Geometry=BOX, "
            "GeometryHeight=11.5) -> ObjectDefinition.cs:312 Height=11.5 -> Geometry.cs:127-145 "
            "MaxZ=11.5 -> ScenarioCaptureRunner.cs:1003 MaxZ/2=5.75"
        ),
    }

    manifest_out_path = os.path.join(frames_sub_dir, "overlay-manifest.json")
    with open(manifest_out_path, "w", encoding="utf-8") as fh:
        json.dump(overlay_manifest, fh, indent=2)

    return {
        "gif_path": gif_path,
        "poster_path": poster_path,
        "frames_dir": frames_sub_dir,
        "manifest_path": manifest_out_path,
        "sha256sums_path": sums_path,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--manifest",
        default="/tmp/panopticon_capture_sedan-footprints-v2-recapture/manifest.json",
        help="Path to capture manifest.json",
    )
    parser.add_argument(
        "--sidecar",
        default="/tmp/panopticon_capture_sedan-footprints-v2-recapture/ground-vertices.json",
        help="Path to ground-vertices.json sidecar",
    )
    parser.add_argument(
        "--frames-dir",
        default="/tmp/panopticon_capture_sedan-footprints-v2-recapture",
        help="Directory containing raw capture frames",
    )
    parser.add_argument(
        "--output-dir",
        default="/mnt/pit2/Network Storage/temp",
        help="Directory to publish outputs",
    )
    parser.add_argument(
        "--kml",
        default="/mnt/pit2/panopticon/geo-proof/r2-03-footprints-v3/prepared/sedan-proof-buildings.kml",
        help="Path to reference KML",
    )
    parser.add_argument(
        "--verify-only",
        action="store_true",
        help="Verify recorded camera anchors and sidecar, exiting non-zero if invalid",
    )

    args = parser.parse_args()

    try:
        report = verify_manifest_and_sidecar(args.manifest, args.sidecar)
    except Exception as ex:
        print(f"VERIFICATION FAILED: {ex}", file=sys.stderr)
        return 2

    print(
        f"Verification result: passed={report['passed']}, "
        f"visible_max={report['visible_max']:.6f}px, "
        f"unfiltered_max={report['unfiltered_max']:.6f}px, "
        f"visible_anchors={report['visible_anchors']}, "
        f"excluded_anchors={report['excluded_anchors']}, "
        f"frame_count={report['frame_count']}, "
        f"polygons={report['polygon_count']}"
    )

    if report["excluded"]:
        print(f"Excluded anchors ({report['excluded_anchors']} total):")
        for ex in report["excluded"][:10]:
            print(f"  Frame {ex['frame_index']} {ex['actor_id']}: err={ex['error']:.2f}px, reason: {ex['reason_str']}")

    if not report["passed"]:
        print(
            f"VERIFICATION FAILED: visible max reprojection error {report['visible_max']:.4f}px >= 1.0px",
            file=sys.stderr,
        )
        return 2

    if args.verify_only:
        print("Verification passed successfully (visible max < 1.0px inside camera clip frustum).")
        return 0

    # Step 2: Render artifacts
    print(f"Rendering artifacts to {args.output_dir}...")
    art = render_capture_artifacts(
        args.manifest,
        args.sidecar,
        args.frames_dir,
        args.output_dir,
        args.kml,
        verification_report=report,
    )
    print("Render complete:")
    print(f"  GIF: {art['gif_path']}")
    print(f"  Poster: {art['poster_path']}")
    print(f"  Manifest: {art['manifest_path']}")
    print(f"  SHA256SUMS: {art['sha256sums_path']}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
