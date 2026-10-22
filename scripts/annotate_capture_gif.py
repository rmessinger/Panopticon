#!/usr/bin/env python3
"""Turn a generic scenario capture (PNG sequence + manifest.json written by the
TestHarness ScenarioCapture runner) into a cropped GIF.

Generic companion to annotate_engagement_gif.py: same crop/palette machinery, but no
engagement-specific overlays (no IMPACT flashes). Optional
--labels draws a static legend; --health draws actor labels and measured health bars.
--projectile-orientation draws sampled
travel and local +X vectors from the manifest.
--navigation draws live partition occupancy, collision footprints, and planned paths
from RoutingChoke captures; --annotated-dir preserves the annotated PNG sequence.

Playback defaults to real time: each captured frame is held for the number of logic
frames it covers (manifest AbsoluteLogicFrame delta) at the game's 30 logic frames per
second. --speed scales that (0.5 = half speed), --frame-ms overrides it absolutely.

Usage:
    python3 scripts/annotate_capture_gif.py <captureDir> <out.gif> [--speed 1.0] [--labels]
        [--padding 12]
"""

import argparse
import colorsys
import json
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from annotate_engagement_gif import RESERVED_COLORS, build_palette  # noqa: E402

MIN_CROP_FRACTION = 0.25
PADDING = 12
TRAVEL_COLOR = RESERVED_COLORS[1]
FORWARD_COLOR = RESERVED_COLORS[2]


def _grow(lo, hi, minimum, limit):
    size = hi - lo
    if size >= minimum:
        return lo, hi
    pad = (minimum - size) // 2
    return max(0, lo - pad), min(limit, hi + pad)


def compute_crop(manifest, padding, navigation=False):
    xs, ys = [], []
    for frame in manifest["Frames"]:
        for actor in frame["Actors"] + frame["Projectiles"]:
            xs.append(actor["X"] - actor["RadiusPixels"])
            xs.append(actor["X"] + actor["RadiusPixels"])
            ys.append(actor["Y"] - actor["RadiusPixels"])
            ys.append(actor["Y"] + actor["RadiusPixels"])
        if navigation:
            for cell in frame["Navigation"]["Cells"]:
                xs.extend(point["X"] for point in cell["Corners"])
                ys.extend(point["Y"] for point in cell["Corners"])
    if not xs:
        return None, None, None, None
    vw, vh = manifest["ViewportWidth"], manifest["ViewportHeight"]
    left = max(0, int(min(xs) - padding))
    top = max(0, int(min(ys) - padding))
    right = min(vw, int(max(xs) + padding))
    bottom = min(vh, int(max(ys) + padding))
    left, right = _grow(left, right, MIN_CROP_FRACTION * vw, vw)
    top, bottom = _grow(top, bottom, MIN_CROP_FRACTION * vh, vh)
    return left, top, right, bottom


def frame_paths(manifest, directory):
    return [os.path.join(directory, f["File"]) for f in manifest["Frames"]]


def fit_font(draw, text, max_width):
    size = 14
    while size > 8:
        font = ImageFont.load_default(size)
        if draw.textlength(text, font=font) <= max_width:
            return font
        size -= 1
    return ImageFont.load_default(8)


def draw_arrow(draw, start, end, color, width):
    draw.line((start, end), fill=color, width=width)
    dx = end[0] - start[0]
    dy = end[1] - start[1]
    length = math.hypot(dx, dy)
    if length < 2:
        return

    ux = dx / length
    uy = dy / length
    px = -uy
    py = ux
    arrow_length = 8
    arrow_width = 4
    base_x = end[0] - ux * arrow_length
    base_y = end[1] - uy * arrow_length
    draw.polygon(
        (
            end,
            (base_x + px * arrow_width, base_y + py * arrow_width),
            (base_x - px * arrow_width, base_y - py * arrow_width),
        ),
        fill=color,
    )


def draw_health_panel(image, record):
    actors = record["Actors"]
    if not actors:
        return
    draw = ImageDraw.Draw(image)
    lines = [f"{actor['Label']}: {actor['Health']:.0f}/{actor['MaxHealth']:.0f} HP" for actor in actors]
    font = fit_font(draw, max(lines, key=len), image.width - 16)
    row_height = font.size + 12
    draw.rectangle((0, 0, image.width, 24 + row_height * len(actors)), fill=(0, 0, 0))
    title = "Two-unit duel" if len(actors) == 2 else "Actor health"
    draw.text((8, 4), title, fill=RESERVED_COLORS[1], font=font)
    for i, (actor, line) in enumerate(zip(actors, lines)):
        y = 24 + i * row_height
        draw.text((8, y), line, fill=RESERVED_COLORS[1], font=font)
        ratio = max(0, min(1, actor["Health"] / actor["MaxHealth"])) if actor["MaxHealth"] > 0 else 0
        bar_y = y + font.size + 2
        draw.rectangle((8, bar_y, image.width - 8, bar_y + 4), fill=(90, 20, 20))
        if ratio > 0:
            draw.rectangle((8, bar_y, 8 + (image.width - 16) * ratio, bar_y + 4), fill=(80, 210, 80))


def draw_navigation(image, record, left, top, scale):
    """Render measured broadphase membership, collision circles, and remaining waypoints."""
    navigation = record["Navigation"]
    overlay = Image.new("RGBA", image.size)
    draw = ImageDraw.Draw(overlay)

    def project(points):
        return [((point["X"] - left) * scale, (point["Y"] - top) * scale)
                for point in points]

    for cell in navigation["Cells"]:
        corners = project(cell["Corners"])
        count = len(cell["ObjectIds"])
        color = (255, 85, 70) if cell["Structures"] else (255, 180, 45)
        if count:
            draw.polygon(corners, fill=(*color, 45))
        draw.line(corners + [corners[0]], fill=(*color, 150) if count else (190, 205, 220, 85), width=1)
        center = tuple(sum(point[axis] for point in corners) / 4 for axis in (0, 1))
        draw.text(center, str(count), fill=(255, 255, 255, 240), anchor="mm",
                  stroke_width=1, stroke_fill=(0, 0, 0, 220))

    for unit in navigation["Units"]:
        color = tuple(round(component * 255) for component in
                      colorsys.hsv_to_rgb((unit["Id"] * 0.61803398875) % 1, 0.65, 1))
        path = project(unit["Path"])
        if len(path) > 1:
            draw.line(path, fill=(*color, 180), width=2)
            for x, y in path[1:]:
                draw.ellipse((x - 2, y - 2, x + 2, y + 2), fill=(*color, 230))
        footprint = project(unit["Footprint"])
        draw.line(footprint + [footprint[0]], fill=(65, 255, 255, 255), width=2)

    image = Image.alpha_composite(image.convert("RGBA"), overlay).convert("RGB")
    result = Image.new("RGB", (image.width, image.height + 64), (12, 16, 22))
    result.paste(image, (0, 64))
    draw = ImageDraw.Draw(result)
    font = ImageFont.load_default(12)
    active = sum(len(unit["Path"]) > 1 for unit in navigation["Units"])
    lines = [
        f"Logic frame {record['AbsoluteLogicFrame']} | partition size {navigation['CellSize']:g} | {active} active paths",
        "Grid numbers: object memberships (not reservations). Orange: occupied; red: structures.",
        "CYAN: collision footprints | colored lines/dots: remaining paths/waypoints",
    ]
    for index, line in enumerate(lines):
        draw.text((6, 4 + index * 19), line, fill=(225, 240, 250), font=font)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture_dir")
    parser.add_argument("output")
    parser.add_argument("--speed", type=float, default=1.0,
                        help="playback speed multiplier over real time (0.5 = half speed; default 1.0)")
    parser.add_argument("--frame-ms", type=int, default=None,
                        help="explicit per-frame delay in milliseconds, overriding real-time pacing")
    parser.add_argument("--labels", action="store_true",
                         help="draw a static legend of tracked actor labels")
    parser.add_argument("--health", action="store_true",
                         help="draw a duel panel with actor labels and measured HP, without synthetic hit flashes")
    parser.add_argument("--projectile-orientation", action="store_true",
                        help="draw projectile travel (white) and local +X (cyan) vectors")
    parser.add_argument("--padding", type=int, default=PADDING,
                         help=f"crop padding in source pixels (default {PADDING})")
    parser.add_argument("--navigation", action="store_true",
                        help="draw captured partition occupancy, unit footprints, and live planned paths")
    parser.add_argument("--annotated-dir", help="new directory for annotated PNG frames")
    args = parser.parse_args()

    with open(os.path.join(args.capture_dir, "manifest.json")) as fh:
        manifest = json.load(fh)

    if args.navigation and any("Navigation" not in frame for frame in manifest["Frames"]):
        parser.error("--navigation requires a new capture with Navigation metadata")
    if args.navigation and (args.health or args.labels or args.projectile_orientation):
        parser.error("--navigation cannot be combined with other overlays")
    if args.annotated_dir:
        os.makedirs(args.annotated_dir, exist_ok=False)

    crop = compute_crop(manifest, max(0, args.padding), args.navigation)
    if crop[0] is None:
        print("manifest records no actors or projectiles; nothing to crop")
        return 1
    left, top, right, bottom = crop
    crop_w, crop_h = right - left, bottom - top

    paths = frame_paths(manifest, args.capture_dir)
    first = Image.open(paths[0]).convert("RGB").crop((left, top, right, bottom))
    out_w = min(900, crop_w)
    out_h = max(1, round(out_w * crop_h / crop_w))
    scale = out_w / crop_w
    panel_height = 24 + 26 * max(len(frame["Actors"]) for frame in manifest["Frames"]) if args.health else 0

    legend = []
    if args.labels:
        seen = set()
        for frame in manifest["Frames"]:
            for actor in frame["Actors"]:
                key = (actor["Role"], actor["Label"])
                if key not in seen:
                    seen.add(key)
                    legend.append(f"{actor['Label']} ({actor['Role']})")

    frames = []
    for path, record in zip(paths, manifest["Frames"]):
        im = Image.open(path).convert("RGB").crop((left, top, right, bottom))
        im = im.resize((out_w, out_h), Image.LANCZOS)
        if args.health:
            # Keep annotations outside the rendered scene so effects and model bounds remain unobscured.
            annotated = Image.new("RGB", (out_w, out_h + panel_height), (0, 0, 0))
            annotated.paste(im, (0, panel_height))
            draw_health_panel(annotated, record)
            im = annotated
        elif legend:
            draw = ImageDraw.Draw(im)
            font = fit_font(draw, max(legend, key=len), out_w - 8)
            y = 4
            for line in legend:
                draw.text((4, y), line, fill=RESERVED_COLORS[1], font=font)
                y += font.size + 2
        if args.projectile_orientation:
            draw = ImageDraw.Draw(im)
            summaries = []
            for projectile in record["Projectiles"]:
                orientation = projectile.get("Orientation")
                if orientation is None:
                    continue

                start = (
                    (projectile["X"] - left) * scale,
                    (projectile["Y"] - top) * scale + panel_height,
                )
                travel_x = orientation.get("TravelScreenX")
                travel_y = orientation.get("TravelScreenY")
                if travel_x is not None and travel_y is not None:
                    travel_end = (
                        (travel_x - left) * scale,
                        (travel_y - top) * scale + panel_height,
                    )
                    draw_arrow(draw, start, travel_end, TRAVEL_COLOR, 5)

                forward_end = (
                    (orientation["ForwardScreenX"] - left) * scale,
                    (orientation["ForwardScreenY"] - top) * scale + panel_height,
                )
                draw_arrow(draw, start, forward_end, FORWARD_COLOR, 2)

                alignment = orientation.get("AlignmentDot")
                travel_z = orientation.get("TravelZ")
                if alignment is not None and travel_z is not None:
                    phase = "ASCENT" if travel_z > 0.01 else "DESCENT" if travel_z < -0.01 else "LEVEL"
                    summaries.append(
                        f"{projectile['Definition']}: {phase}, dot {alignment:.3f}")

            if summaries:
                lines = ["WHITE travel / CYAN local +X", *summaries[:3]]
                font = fit_font(draw, max(lines, key=len), out_w - 8)
                line_height = font.size + 2
                panel_width = max(draw.textlength(line, font=font) for line in lines) + 8
                panel_height = len(lines) * line_height + 6
                panel_left = out_w - panel_width - 4
                draw.rectangle(
                    (panel_left, 4, out_w - 4, 4 + panel_height),
                    fill=(0, 0, 0),
                )
                y = 7
                for line in lines:
                    draw.text((panel_left + 4, y), line, fill=TRAVEL_COLOR, font=font)
                    y += line_height
        if args.navigation:
            im = draw_navigation(im, record, left, top, scale)
        if args.annotated_dir:
            im.save(os.path.join(args.annotated_dir, record["File"]))
        frames.append(im)

    base = build_palette(frames)
    # Frames must be quantized to the shared palette before saving. Pillow's
    # `palette=` save argument accepts only bytes/list/ImagePalette, so passing the
    # P-mode image is silently ignored and each RGB frame gets its own adaptive
    # palette, displayed through frame 0's global palette: hues drift as the scene
    # changes (blue props turning green, labels quantized off pure white).
    quantized = [frame.quantize(palette=base, dither=Image.NONE) for frame in frames]
    per_frame_ms = args.frame_ms if args.frame_ms is not None else realtime_frame_ms(manifest) / args.speed
    per_frame_ms = max(10, round(per_frame_ms))
    quantized[0].save(
        args.output,
        save_all=True,
        append_images=quantized[1:],
        duration=per_frame_ms,
        loop=0,
    )
    print(f"{args.output}: {len(frames)} frames, {quantized[0].width}x{quantized[0].height}, crop {left},{top},{right},{bottom}, {per_frame_ms} ms/frame")
    return 0


def realtime_frame_ms(manifest):
    """Per-frame delay matching real time: captured logic frames per PNG times the
    30-frames-per-second logic clock (Generals and Zero Hour both tick at 30 Hz)."""
    import statistics
    logic_frames = [f["AbsoluteLogicFrame"] for f in manifest["Frames"]]
    deltas = [b - a for a, b in zip(logic_frames, logic_frames[1:]) if b > a]
    stride = statistics.median(deltas) if deltas else 1
    return stride * 1000.0 / 30.0


if __name__ == "__main__":
    raise SystemExit(main())
