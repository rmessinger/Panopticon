#!/usr/bin/env python3
"""Turn a generic scenario capture (PNG sequence + manifest.json written by the
TestHarness ScenarioCapture runner) into a cropped GIF.

Generic companion to annotate_engagement_gif.py: same crop/palette machinery, but no
engagement-specific overlays (no target-health readout, no IMPACT flashes). Optional
--labels draws a static legend of tracked actor labels.

Playback defaults to real time: each captured frame is held for the number of logic
frames it covers (manifest AbsoluteLogicFrame delta) at the game's 30 logic frames per
second. --speed scales that (0.5 = half speed), --frame-ms overrides it absolutely.

Usage:
    python3 scripts/annotate_capture_gif.py <captureDir> <out.gif> [--speed 1.0] [--labels]
"""

import argparse
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from annotate_engagement_gif import RESERVED_COLORS, build_palette  # noqa: E402

MIN_CROP_FRACTION = 0.25
PADDING = 12


def _grow(lo, hi, minimum, limit):
    size = hi - lo
    if size >= minimum:
        return lo, hi
    pad = (minimum - size) // 2
    return max(0, lo - pad), min(limit, hi + pad)


def compute_crop(manifest):
    xs, ys = [], []
    for frame in manifest["Frames"]:
        for actor in frame["Actors"] + frame["Projectiles"]:
            xs.append(actor["X"] - actor["RadiusPixels"])
            xs.append(actor["X"] + actor["RadiusPixels"])
            ys.append(actor["Y"] - actor["RadiusPixels"])
            ys.append(actor["Y"] + actor["RadiusPixels"])
    if not xs:
        return None, None, None, None
    vw, vh = manifest["ViewportWidth"], manifest["ViewportHeight"]
    left = max(0, int(min(xs) - PADDING))
    top = max(0, int(min(ys) - PADDING))
    right = min(vw, int(max(xs) + PADDING))
    bottom = min(vh, int(max(ys) + PADDING))
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
    args = parser.parse_args()

    with open(os.path.join(args.capture_dir, "manifest.json")) as fh:
        manifest = json.load(fh)

    crop = compute_crop(manifest)
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
    for path in paths:
        im = Image.open(path).convert("RGB").crop((left, top, right, bottom))
        im = im.resize((out_w, out_h), Image.LANCZOS)
        if legend:
            draw = ImageDraw.Draw(im)
            font = fit_font(draw, max(legend, key=len), out_w - 8)
            y = 4
            for line in legend:
                draw.text((4, y), line, fill=RESERVED_COLORS[1], font=font)
                y += font.size + 2
        frames.append(im)

    base = build_palette(frames)
    per_frame_ms = args.frame_ms if args.frame_ms is not None else realtime_frame_ms(manifest) / args.speed
    per_frame_ms = max(10, round(per_frame_ms))
    frames[0].save(
        args.output,
        save_all=True,
        append_images=frames[1:],
        duration=per_frame_ms,
        loop=0,
        palette=base,
    )
    print(f"{args.output}: {len(frames)} frames, {out_w}x{out_h}, crop {left},{top},{right},{bottom}, {per_frame_ms} ms/frame")
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
