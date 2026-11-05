import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from PIL import Image, TiffImagePlugin

# Dynamically import annotate_footprint_overlay
script_path = Path(__file__).resolve().parents[1] / "annotate_footprint_overlay.py"
spec = importlib.util.spec_from_file_location("annotate_footprint_overlay", script_path)
overlay_mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(overlay_mod)


def create_synthetic_geotiff(
    path: str,
    width: int = 100,
    height: int = 100,
    scale: tuple[float, float, float] = (1.0, 1.0, 0.0),
    tiepoint: tuple[float, float, float, float, float, float] = (0.0, 0.0, 0.0, 1000.0, 2000.0, 0.0),
    color: tuple[int, int, int] = (128, 128, 128),
) -> None:
    """Create a small in-memory synthetic GeoTIFF with PIL for testing."""
    info = TiffImagePlugin.ImageFileDirectory_v2()
    info[33550] = scale
    info[33922] = tiepoint
    im = Image.new("RGB", (width, height), color)
    im.save(path, format="TIFF", tiffinfo=info)


def create_mask_chunk_bytes(
    impassable_cells: list[tuple[int, int]] | None = None,
    unbuildable_cells: list[tuple[int, int]] | None = None,
    length: int = 16641,
    bad_byte_pos: int | None = None,
    bad_byte_val: int = 4,
) -> bytes:
    """Synthesize 16641 mask chunk bytes with optional flags or bad bytes."""
    buf = bytearray(length)
    if impassable_cells:
        for row, col in impassable_cells:
            if row < 129 and col < 129:
                buf[row * 129 + col] |= 1
    if unbuildable_cells:
        for row, col in unbuildable_cells:
            if row < 129 and col < 129:
                buf[row * 129 + col] |= 2
    if bad_byte_pos is not None and bad_byte_pos < length:
        buf[bad_byte_pos] = bad_byte_val
    return bytes(buf)


def create_synthetic_payload(
    payload_dir: str,
    origin: tuple[float, float] = (1000.0, 1000.0),
    meters_per_gu: float = 1.0,
    spacing: float = 10.0,
    chunks_x: int = 1,
    chunks_y: int = 1,
    chunk_bytes_map: dict[tuple[int, int], bytes] | None = None,
) -> str:
    """Synthesize a complete geospatial payload directory structure."""
    os.makedirs(os.path.join(payload_dir, "masks"), exist_ok=True)
    chunks_list = []
    for cy in range(chunks_y):
        for cx in range(chunks_x):
            mask_rel = f"masks/{cx}_{cy}.bin"
            mask_path = os.path.join(payload_dir, mask_rel)
            b = chunk_bytes_map.get((cx, cy)) if chunk_bytes_map else None
            if b is None:
                b = create_mask_chunk_bytes()
            with open(mask_path, "wb") as fh:
                fh.write(b)
            chunks_list.append({
                "cx": cx,
                "cy": cy,
                "masks": {
                    "path": mask_rel,
                    "lengthBytes": len(b),
                    "sha256": "0" * 64,
                },
            })

    manifest = {
        "kind": "panopticon-geospatial-payload",
        "schemaVersion": 1,
        "grid": {
            "chunksX": chunks_x,
            "chunksY": chunks_y,
            "layout": {
                "sampleSpacingGameUnits": spacing,
                "intervalsPerChunk": 128,
                "pointsPerChunkAxis": 129,
            },
            "transform": {
                "originEastingNorthing": {"x": origin[0], "y": origin[1]},
                "metersPerGameUnit": meters_per_gu,
            },
        },
        "chunks": chunks_list,
    }
    with open(os.path.join(payload_dir, "payload.json"), "w", encoding="utf-8") as fh:
        json.dump(manifest, fh)
    return payload_dir


class MaskValidationTests(unittest.TestCase):
    def test_valid_mask_chunk_passes(self):
        with tempfile.NamedTemporaryFile(delete=False) as tf:
            b = create_mask_chunk_bytes(impassable_cells=[(0, 0), (10, 10)], unbuildable_cells=[(5, 5)])
            tf.write(b)
            tf_path = tf.name

        try:
            read_bytes = overlay_mod.read_and_validate_mask_chunk(tf_path, 0, 0)
            self.assertEqual(len(read_bytes), 16641)
            self.assertEqual(read_bytes[0], 1)
            self.assertEqual(read_bytes[5 * 129 + 5], 2)
            self.assertEqual(read_bytes[10 * 129 + 10], 1)
        finally:
            if os.path.exists(tf_path):
                os.remove(tf_path)

    def test_mask_chunk_wrong_length_fails(self):
        with tempfile.NamedTemporaryFile(delete=False) as tf:
            tf.write(b"too short")
            tf_path = tf.name

        try:
            with self.assertRaises(overlay_mod.FootprintOverlayError) as ctx:
                overlay_mod.read_and_validate_mask_chunk(tf_path, 0, 0)
            self.assertIn("must hold exactly 16641 bytes", str(ctx.exception))
        finally:
            if os.path.exists(tf_path):
                os.remove(tf_path)

    def test_mask_chunk_undefined_bits_fails(self):
        with tempfile.NamedTemporaryFile(delete=False) as tf:
            # Set bad byte with bit 4
            b = create_mask_chunk_bytes(bad_byte_pos=150, bad_byte_val=4)
            tf.write(b)
            tf_path = tf.name

        try:
            with self.assertRaises(overlay_mod.FootprintOverlayError) as ctx:
                overlay_mod.read_and_validate_mask_chunk(tf_path, 1, 2)
            self.assertIn("undefined bits", str(ctx.exception))
            self.assertIn("Chunk (1, 2)", str(ctx.exception))
        finally:
            if os.path.exists(tf_path):
                os.remove(tf_path)

    def test_missing_file_fails(self):
        with self.assertRaises(overlay_mod.FootprintOverlayError) as ctx:
            overlay_mod.read_and_validate_mask_chunk("/nonexistent/mask.bin", 0, 0)
        self.assertIn("not found", str(ctx.exception))


class OrthoBoundsTests(unittest.TestCase):
    def test_geotiff_tags_derive_bounds(self):
        with tempfile.NamedTemporaryFile(suffix=".tif", delete=False) as tf:
            tf_path = tf.name

        try:
            create_synthetic_geotiff(
                tf_path,
                width=100,
                height=50,
                scale=(2.0, 2.0, 0.0),
                tiepoint=(0.0, 0.0, 0.0, 500.0, 1000.0, 0.0),
            )
            with Image.open(tf_path) as img:
                min_e, min_n, max_e, max_n = overlay_mod.read_ortho_bounds(img)
            self.assertAlmostEqual(min_e, 500.0)
            self.assertAlmostEqual(max_e, 700.0)  # 500 + 100 * 2
            self.assertAlmostEqual(max_n, 1000.0)
            self.assertAlmostEqual(min_n, 900.0)  # 1000 - 50 * 2
        finally:
            if os.path.exists(tf_path):
                os.remove(tf_path)

    def test_missing_tags_without_override_fails(self):
        img = Image.new("RGB", (50, 50), (100, 100, 100))
        with self.assertRaises(overlay_mod.FootprintOverlayError):
            overlay_mod.read_ortho_bounds(img)

    def test_explicit_bounds_override(self):
        img = Image.new("RGB", (50, 50), (100, 100, 100))
        bounds = overlay_mod.read_ortho_bounds(img, explicit_bounds=(100.0, 200.0, 300.0, 400.0))
        self.assertEqual(bounds, (100.0, 200.0, 300.0, 400.0))

    def test_invalid_explicit_bounds_fails(self):
        img = Image.new("RGB", (50, 50), (100, 100, 100))
        with self.assertRaises(overlay_mod.FootprintOverlayError):
            overlay_mod.read_ortho_bounds(img, explicit_bounds=(300.0, 400.0, 100.0, 200.0))


class MetricsCalculationTests(unittest.TestCase):
    def test_empty_mask_metrics(self):
        cells = {}
        crop = (0.0, 0.0, 100.0, 100.0)
        metrics = overlay_mod.compute_mask_metrics(cells, crop, spacing=10.0)
        self.assertEqual(metrics["total_cells"], 121)  # 11 x 11
        self.assertEqual(metrics["impassable_count"], 0)
        self.assertEqual(metrics["unbuildable_only_count"], 0)
        self.assertEqual(metrics["masked_count"], 0)
        self.assertEqual(metrics["clear_count"], 121)
        self.assertEqual(metrics["impassable_share"], 0.0)

    def test_populated_mask_metrics(self):
        cells = {
            (2, 2): 1,  # Impassable
            (2, 3): 3,  # Impassable + Unbuildable -> counts as Impassable
            (4, 4): 2,  # Unbuildable only
        }
        crop = (0.0, 0.0, 100.0, 100.0)
        metrics = overlay_mod.compute_mask_metrics(cells, crop, spacing=10.0)
        self.assertEqual(metrics["impassable_count"], 2)
        self.assertEqual(metrics["unbuildable_only_count"], 1)
        self.assertEqual(metrics["masked_count"], 3)
        self.assertEqual(metrics["clear_count"], 118)
        self.assertAlmostEqual(metrics["impassable_share"], 2 / 121)
        self.assertAlmostEqual(metrics["unbuildable_only_share"], 1 / 121)
        self.assertAlmostEqual(metrics["masked_share"], 3 / 121)


class OverlayRenderingTests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()

    def tearDown(self):
        self.temp_dir.cleanup()

    def test_masked_tint_placement(self):
        # Create ortho: 200x200 pixels, scale 1.0 m/px, tiepoint at (1000.0, 1200.0)
        # So UTM covers: Easting [1000.0, 1200.0], Northing [1000.0, 1200.0]
        ortho_path = os.path.join(self.temp_dir.name, "ortho.tif")
        create_synthetic_geotiff(
            ortho_path,
            width=200,
            height=200,
            scale=(1.0, 1.0, 0.0),
            tiepoint=(0.0, 0.0, 0.0, 1000.0, 1200.0, 0.0),
            color=(100, 100, 100),
        )

        # Create payload with:
        # Sample (5, 5) -> Impassable (1). Game coords (50, 50), UTM (1050, 1050)
        # Sample (8, 8) -> Unbuildable (2). Game coords (80, 80), UTM (1080, 1080)
        payload_dir = os.path.join(self.temp_dir.name, "payload")
        chunk_bytes = create_mask_chunk_bytes(
            impassable_cells=[(5, 5)],
            unbuildable_cells=[(8, 8)],
        )
        create_synthetic_payload(
            payload_dir,
            origin=(1000.0, 1000.0),
            meters_per_gu=1.0,
            spacing=10.0,
            chunk_bytes_map={(0, 0): chunk_bytes},
        )

        # Crop game window [0, 0, 150, 150]
        img = overlay_mod.render_footprint_overlay(
            payload_dir=payload_dir,
            ortho_path=ortho_path,
            crop_game=(0.0, 0.0, 150.0, 150.0),
        )

        self.assertIsInstance(img, Image.Image)
        # Verify canvas size has header (56) + ortho crop (150) + footer (56) = 262 height
        self.assertEqual(img.height, 56 + 150 + 56)

        # Header offset is 56.
        # Ortho crop has width 150. Canvas width is max(150, 480) = 480.
        # Ortho is centered horizontally: offset_x = (480 - 150) // 2 = 165.
        offset_x = (img.width - 150) // 2
        offset_y = 56

        # In ortho crop:
        # Cell (5, 5): game X=50, UTM Easting=1050 -> pixel x in crop = 1050 - 1000 = 50
        # UTM Northing=1050 -> pixel y in crop = (1150 - 1050) = 100  (since crop top northing is 1150)
        # Check pixel at (offset_x + 50, offset_y + 100): must have red tint (R > G and R > B)
        imp_pixel = img.getpixel((offset_x + 50, offset_y + 100))
        self.assertGreater(imp_pixel[0], imp_pixel[1], "Impassable cell must have red tint")
        self.assertGreater(imp_pixel[0], imp_pixel[2], "Impassable cell must have red tint")

        # Cell (8, 8): game X=80 -> pixel x in crop = 80
        # UTM Northing=1080 -> pixel y in crop = (1150 - 1080) = 70
        # Check pixel at (offset_x + 80, offset_y + 70): must have orange tint (R > B and G > B)
        unb_pixel = img.getpixel((offset_x + 80, offset_y + 70))
        self.assertGreater(unb_pixel[0], unb_pixel[2], "Unbuildable cell must have R > B")
        self.assertGreater(unb_pixel[1], unb_pixel[2], "Unbuildable cell must have G > B")

        # Cell (2, 6): clear cell at game X=20 -> pixel x in crop = 20
        # UTM Northing=1060 -> pixel y in crop = (1150 - 1060) = 90 (away from scale bar)
        # Check pixel at (offset_x + 20, offset_y + 90): must remain untinted gray (100, 100, 100)
        clear_pixel = img.getpixel((offset_x + 20, offset_y + 90))
        self.assertEqual(clear_pixel, (100, 100, 100), "Clear cell must remain untinted")

    def test_empty_mask_renders_cleanly(self):
        ortho_path = os.path.join(self.temp_dir.name, "ortho.tif")
        create_synthetic_geotiff(ortho_path, width=100, height=100)
        payload_dir = os.path.join(self.temp_dir.name, "payload_empty")
        create_synthetic_payload(payload_dir)

        img = overlay_mod.render_footprint_overlay(
            payload_dir=payload_dir,
            ortho_path=ortho_path,
            crop_game=(0.0, 0.0, 50.0, 50.0),
        )
        self.assertIsInstance(img, Image.Image)


class CliEndToEndTests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()

    def tearDown(self):
        self.temp_dir.cleanup()

    def test_cli_end_to_end_generates_png(self):
        ortho_path = os.path.join(self.temp_dir.name, "ortho.tif")
        create_synthetic_geotiff(ortho_path, width=120, height=120)

        payload_dir = os.path.join(self.temp_dir.name, "payload")
        chunk_bytes = create_mask_chunk_bytes(impassable_cells=[(1, 1)], unbuildable_cells=[(2, 2)])
        create_synthetic_payload(payload_dir, chunk_bytes_map={(0, 0): chunk_bytes})

        out_png = os.path.join(self.temp_dir.name, "output.png")

        cmd = [
            sys.executable,
            str(script_path),
            "--payload",
            payload_dir,
            "--ortho",
            ortho_path,
            "--out",
            out_png,
            "--crop",
            "0",
            "0",
            80,
            80,
        ]
        res = subprocess.run([str(c) for c in cmd], capture_output=True, text=True)
        self.assertEqual(res.returncode, 0, f"CLI failed: {res.stderr}")
        self.assertTrue(os.path.isfile(out_png))

        # Check valid PNG
        with Image.open(out_png) as im:
            self.assertEqual(im.format, "PNG")
            self.assertGreater(im.width, 0)
            self.assertGreater(im.height, 0)

    def test_cli_bad_mask_fails_closed(self):
        ortho_path = os.path.join(self.temp_dir.name, "ortho.tif")
        create_synthetic_geotiff(ortho_path, width=100, height=100)

        payload_dir = os.path.join(self.temp_dir.name, "payload_corrupt")
        bad_chunk = create_mask_chunk_bytes(length=1000)  # Bad length
        create_synthetic_payload(payload_dir, chunk_bytes_map={(0, 0): bad_chunk})

        out_png = os.path.join(self.temp_dir.name, "output_corrupt.png")

        cmd = [
            sys.executable,
            str(script_path),
            "--payload",
            payload_dir,
            "--ortho",
            ortho_path,
            "--out",
            out_png,
            "--crop",
            "0",
            "0",
            50,
            50,
        ]
        res = subprocess.run([str(c) for c in cmd], capture_output=True, text=True)
        self.assertNotEqual(res.returncode, 0)
        self.assertIn("annotate_footprint_overlay error", res.stderr)
        self.assertFalse(os.path.exists(out_png))


if __name__ == "__main__":
    unittest.main()
