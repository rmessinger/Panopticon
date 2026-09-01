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

# Exact overlay colors reserved in the shared GIF palette (see build_palette).
RESERVED_COLORS = [
    (255, 60, 40),    # IMPACT flash, DESTROYED readout
    (255, 255, 255),  # health readout
    (64, 224, 255),   # projectile local +X vector
]


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


def build_palette(frames, samples=12):
    step = max(1, len(frames) // samples)
    sampled = frames[::step]
    thumb = (max(1, frames[0].width // 4), max(1, frames[0].height // 4))
    montage = Image.new("RGB", (thumb[0] * len(sampled), thumb[1]))
    for index, frame in enumerate(sampled):
        montage.paste(frame.resize(thumb, Image.LANCZOS), (index * thumb[0], 0))
    base = montage.quantize(colors=250, method=Image.MEDIANCUT)

    # The montage is mostly terrain, so the overlay colors get remapped to pale lookalikes
    # (white text would quantize to a cream indistinguishable from ground). Reserve the
    # last palette slots for the exact overlay colors; they then quantize to themselves
    # while the scene keeps the montage-derived colors.
    palette = base.getpalette()
    for slot, color in enumerate(RESERVED_COLORS):
        palette[(250 + slot) * 3:(250 + slot) * 3 + 3] = list(color)
    base.putpalette(palette)
    return base


def target_health(frame):
    for actor in frame["Actors"]:
        if actor["Role"] == "target":
            return actor
    return None


def annotate(capture_dir, output, scale, duration, padding):
    with open(os.path.join(capture_dir, "manifest.json")) as handle:
        manifest = json.load(handle)

    left, top, right, bottom = crop_box(manifest, padding)
    width, height = right - left, bottom - top
    out_size = (int(width * scale), int(height * scale))

    # Frames where the target's health drops: the impact moments. Each gets a held flash so a
    # viewer cannot miss that the warhead landed (the impact FX itself is small next to launch).
    damage_frames = {}
    previous = None
    for record in manifest["Frames"]:
        target = target_health(record)
        health = target["Health"] if target else None
        if previous is not None and health is not None and health < previous:
            damage_frames[record["Index"]] = previous - health
        previous = health

    frames = []
    health_font = load_font(max(16, min(28, out_size[1] // 18)))
    for record in manifest["Frames"]:
        path = os.path.join(capture_dir, record["File"])
        image = Image.open(path).convert("RGB").crop((left, top, right, bottom)).resize(out_size, Image.LANCZOS)
        overlay = Image.new("RGBA", out_size, (0, 0, 0, 0))
        draw = ImageDraw.Draw(overlay)

        # Target health readout, bottom-left: the engagement's scoreboard. The target
        # actor disappears from the manifest once dead, hence the DESTROYED fallback.
        target = target_health(record)
        destroyed = target is None or target["Health"] <= 0
        health_text = ("TARGET DESTROYED" if destroyed
                       else f"TARGET {target['Health']:.0f}/{target['MaxHealth']:.0f} HP")
        box = draw.textbbox((0, 0), health_text, font=health_font)
        x0, y0 = 10, out_size[1] - (box[3] - box[1]) - 12
        draw.rectangle((x0 - 6, y0 - 6, x0 + (box[2] - box[0]) + 6, out_size[1]),
                       fill=(0, 0, 0, 200))
        draw.text((x0, y0 - box[1]), health_text, font=health_font,
                  fill=(255, 60, 40) if destroyed else (255, 255, 255))

        index = record["Index"]
        for offset in range(3):
            if index - offset in damage_frames:
                amount = damage_frames[index - offset]
                tx = next((actor["X"] - left for actor in record["Actors"]
                           if actor["Role"] == "target"), out_size[0] / 2) * scale
                ty = next((actor["Y"] - top for actor in record["Actors"]
                           if actor["Role"] == "target"), out_size[1] / 2) * scale
                fade = 255 - offset * 60
                flash = load_font(34)
                text = f"IMPACT -{amount:.0f} HP"
                box = draw.textbbox((0, 0), text, font=flash)
                draw.rectangle((tx - (box[2] - box[0]) / 2 - 10, ty - 120,
                                tx + (box[2] - box[0]) / 2 + 10, ty - 52),
                               fill=(0, 0, 0, 200), outline=(255, 60, 40, fade), width=3)
                draw.text((tx - (box[2] - box[0]) / 2, ty - 112), text,
                          font=flash, fill=(255, 60, 40, fade))

        # Nothing is drawn on the units themselves: the engine renders the units, the
        # projectile, and the FX, and overlays only obscure them. The manifest still
        # sizes the crop (crop_box) and drives the IMPACT flashes and verification
        # (damage frames, projectile sightings).

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
