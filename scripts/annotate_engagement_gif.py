#!/usr/bin/env python3
"""Annotate an engagement capture (PNG sequence + manifest.json) into a GIF.

The manifest is written by the TestHarness `EngagementCapture` scenario and carries, per frame,
the engine's own screen-space projection of every actor. Labels are therefore anchored to what was
actually rendered instead of being positioned by hand.

Usage:
    python3 scripts/annotate_engagement_gif.py <capture-dir> <output.gif> [--scale 1.0] [--duration 120]
"""

import argparse
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ROLE_COLORS = {
    "attacker": (255, 90, 70),
    "target": (120, 220, 255),
    "projectile": (255, 220, 60),
}


def load_font(size):
    for path in (
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/TTF/DejaVuSans-Bold.ttf",
        "/Library/Fonts/Arial Bold.ttf",
    ):
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


MIN_CROP_FRACTION = 0.62


def crop_box(manifest, padding):
    """Smallest box covering every actor position in every frame, plus padding."""
    xs, ys = [], []
    for frame in manifest["Frames"]:
        for actor in frame["Actors"] + frame["Projectiles"]:
            xs.append(actor["X"] - actor["RadiusPixels"])
            xs.append(actor["X"] + actor["RadiusPixels"])
            ys.append(actor["Y"] - actor["RadiusPixels"])
            ys.append(actor["Y"] + actor["RadiusPixels"])

    left = max(0, int(min(xs) - padding))
    right = min(manifest["ViewportWidth"], int(max(xs) + padding))
    top = max(0, int(min(ys) - padding))
    bottom = min(manifest["ViewportHeight"], int(max(ys) + padding))

    # A near-vertical engagement produces a crop too narrow to hold the labels, so widen it to a
    # readable minimum before clamping back inside the viewport.
    left, right = _grow(left, right, MIN_CROP_FRACTION * manifest["ViewportWidth"], manifest["ViewportWidth"])
    top, bottom = _grow(top, bottom, MIN_CROP_FRACTION * manifest["ViewportHeight"], manifest["ViewportHeight"])
    return left, top, right, bottom


def _grow(low, high, minimum, limit):
    """Expand [low, high] around its centre to at least `minimum`, staying within [0, limit]."""
    if high - low >= minimum:
        return low, high
    centre = (low + high) / 2
    low = int(max(0, centre - minimum / 2))
    high = int(min(limit, low + minimum))
    return int(max(0, high - minimum)), high


def fit_font(draw, labels, max_width):
    """Largest font from the preferred size down that keeps every label inside `max_width`."""
    size = max(13, int(max_width / 22))
    while size > 11:
        font = load_font(size)
        if all(draw.textbbox((0, 0), label, font=font)[2] + 24 <= max_width for label in labels):
            return font
        size -= 1
    return load_font(12)


def label_texts(manifest):
    seen = set()
    for frame in manifest["Frames"]:
        for actor in frame["Actors"] + frame["Projectiles"]:
            text = actor["Label"] if actor["Role"] == "projectile" else f"{actor['Role'].upper()}: {actor['Label']}"
            if actor["MaxHealth"] > 0:
                text += f"  {actor['Health']:.0f}/{actor['MaxHealth']:.0f} HP"
            seen.add(text)
    return seen


def overlaps(a, b):
    return a[0] < b[2] and b[0] < a[2] and a[1] < b[3] and b[1] < a[3]


def draw_label(draw, text, anchor, color, font, above, bounds, placed):
    """Draw a label box near an anchor, with a leader line back to the anchor.

    The box is kept inside `bounds` so a label never runs off the crop, and is nudged away from
    boxes already in `placed` so two labels never sit on top of each other.
    """
    tw, th = draw.textbbox((0, 0), text, font=font)[2:]
    x = min(max(anchor[0] - tw / 2, 8), bounds[0] - tw - 8)
    y = anchor[1] - th - 18 if above else anchor[1] + 18
    y = min(max(y, 8), bounds[1] - th - 8)

    step = th + 12
    for _ in range(8):
        box = (x - 6, y - 4, x + tw + 6, y + th + 4)
        if not any(overlaps(box, other) for other in placed):
            break
        y += -step if above else step
        if y < 8 or y > bounds[1] - th - 8:
            y = max(8, min(y, bounds[1] - th - 8))
            break

    box = (x - 6, y - 4, x + tw + 6, y + th + 4)
    placed.append(box)
    draw.line([anchor, (min(max(anchor[0], box[0]), box[2]), box[3] if above else box[1])], fill=color, width=2)
    draw.rectangle(box, fill=(0, 0, 0, 200), outline=color, width=2)
    draw.text((x, y), text, font=font, fill=color)


def build_palette(frames, samples=12):
    step = max(1, len(frames) // samples)
    sampled = frames[::step]
    thumb = (max(1, frames[0].width // 4), max(1, frames[0].height // 4))
    montage = Image.new("RGB", (thumb[0] * len(sampled), thumb[1]))
    for index, frame in enumerate(sampled):
        montage.paste(frame.resize(thumb, Image.LANCZOS), (index * thumb[0], 0))
    return montage.quantize(colors=255, method=Image.MEDIANCUT)


def annotate(capture_dir, output, scale, duration, padding):
    with open(os.path.join(capture_dir, "manifest.json")) as handle:
        manifest = json.load(handle)

    left, top, right, bottom = crop_box(manifest, padding)
    width, height = right - left, bottom - top
    out_size = (int(width * scale), int(height * scale))
    font = fit_font(ImageDraw.Draw(Image.new("RGB", (1, 1))), label_texts(manifest), out_size[0])

    frames = []
    for record in manifest["Frames"]:
        path = os.path.join(capture_dir, record["File"])
        image = Image.open(path).convert("RGB").crop((left, top, right, bottom)).resize(out_size, Image.LANCZOS)
        overlay = Image.new("RGBA", out_size, (0, 0, 0, 0))
        draw = ImageDraw.Draw(overlay)

        placed = []
        for actor in record["Actors"] + record["Projectiles"]:
            color = ROLE_COLORS.get(actor["Role"], (255, 255, 255))
            x = (actor["X"] - left) * scale
            y = (actor["Y"] - top) * scale
            radius = max(8.0, actor["RadiusPixels"] * scale * 0.7)
            draw.ellipse((x - radius, y - radius, x + radius, y + radius), outline=color, width=3)

            text = actor["Label"] if actor["Role"] == "projectile" else f"{actor['Role'].upper()}: {actor['Label']}"
            if actor["MaxHealth"] > 0:
                text += f"  {actor['Health']:.0f}/{actor['MaxHealth']:.0f} HP"
            draw_label(
                draw,
                text,
                (x, y - radius if actor["Role"] != "projectile" else y + radius),
                color,
                font,
                above=actor["Role"] != "projectile",
                bounds=out_size,
                placed=placed,
            )

        frames.append(Image.alpha_composite(image.convert("RGBA"), overlay).convert("RGB"))

    # One shared palette for every frame: per-frame palettes make static scenery shimmer and mute
    # the annotation colors. It is derived from a montage sampled across the whole sequence so that
    # colors appearing only mid-flight (the projectile marker, launch and impact FX) are represented.
    base = build_palette(frames)
    quantized = [frame.quantize(palette=base, dither=Image.NONE) for frame in frames]
    quantized[0].save(
        output,
        save_all=True,
        append_images=quantized[1:],
        duration=duration,
        loop=0,
        optimize=True,
    )
    print(f"{output}: {len(quantized)} frames, {out_size[0]}x{out_size[1]}, crop {left},{top},{right},{bottom}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture_dir")
    parser.add_argument("output")
    parser.add_argument("--scale", type=float, default=1.0)
    parser.add_argument("--duration", type=int, default=120)
    parser.add_argument("--padding", type=int, default=70)
    args = parser.parse_args()

    if not os.path.exists(os.path.join(args.capture_dir, "manifest.json")):
        sys.exit(f"No manifest.json in {args.capture_dir}; run the EngagementCapture scenario first.")

    annotate(args.capture_dir, args.output, args.scale, args.duration, args.padding)


if __name__ == "__main__":
    main()
