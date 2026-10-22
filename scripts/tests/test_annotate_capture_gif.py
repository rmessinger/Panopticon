import importlib.util
from pathlib import Path
import unittest

from PIL import Image

spec = importlib.util.spec_from_file_location(
    "annotate_capture_gif", Path(__file__).resolve().parents[1] / "annotate_capture_gif.py")
annotator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(annotator)


class HealthPanelTests(unittest.TestCase):
    def test_health_bar_uses_measured_health(self):
        image = Image.new("RGB", (300, 150), (40, 50, 60))
        record = {"Actors": [{"Label": "EE2 tank", "Health": 25, "MaxHealth": 100}]}
        annotator.draw_health_panel(image, record)
        self.assertEqual((80, 210, 80), image.getpixel((20, 42)))
        self.assertEqual((90, 20, 20), image.getpixel((200, 42)))
        self.assertEqual((40, 50, 60), image.getpixel((200, 100)))

    def test_zero_max_health_and_empty_actors_are_safe(self):
        image = Image.new("RGB", (300, 150))
        annotator.draw_health_panel(image, {"Actors": []})
        annotator.draw_health_panel(image, {"Actors": [{"Label": "Rebel", "Health": 0, "MaxHealth": 0}]})
        self.assertEqual((90, 20, 20), image.getpixel((20, 42)))


def point(x, y):
    return {"X": x, "Y": y}


class NavigationOverlayTests(unittest.TestCase):
    def setUp(self):
        self.record = {
            "AbsoluteLogicFrame": 31,
            "Actors": [{"X": 100, "Y": 100, "RadiusPixels": 5}],
            "Projectiles": [],
            "Navigation": {
                "CellSize": 100,
                "Cells": [{"Corners": [point(20, 20), point(180, 20),
                                       point(180, 180), point(20, 180)],
                           "ObjectIds": [1, 2], "Structures": 0}],
                "Units": [{"Id": 1, "Radius": 15,
                           "Footprint": [point(90, 90), point(110, 90),
                                         point(110, 110), point(90, 110)],
                           "Path": [point(100, 100), point(150, 50)]}],
            },
        }
        self.manifest = {"ViewportWidth": 200, "ViewportHeight": 200,
                         "Frames": [self.record]}

    def test_navigation_crop_includes_grid_without_changing_default(self):
        self.assertEqual(annotator.compute_crop(self.manifest, 0), (75, 75, 125, 125))
        self.assertEqual(annotator.compute_crop(self.manifest, 0, True), (20, 20, 180, 180))

    def test_render_draws_occupancy_footprint_and_path(self):
        image = annotator.draw_navigation(Image.new("RGB", (200, 200)), self.record, 0, 0, 1)
        self.assertEqual(image.size, (200, 264))
        self.assertNotEqual(image.getpixel((30, 94)), (0, 0, 0))
        self.assertEqual(image.getpixel((90, 154)), (65, 255, 255))
        self.assertNotEqual(image.getpixel((125, 139)), (0, 0, 0))

    def test_empty_paths_and_cells_render(self):
        self.record["Navigation"]["Units"][0]["Path"] = [point(100, 100)]
        self.record["Navigation"]["Cells"][0]["ObjectIds"] = []
        image = annotator.draw_navigation(Image.new("RGB", (200, 200)), self.record, 20, 20, 0.5)
        self.assertEqual(image.size, (200, 264))

    def test_playback_uses_actual_frame_stride(self):
        self.manifest["Frames"].append({"AbsoluteLogicFrame": 51})
        self.assertAlmostEqual(annotator.realtime_frame_ms(self.manifest), 2000 / 3)


if __name__ == "__main__":
    unittest.main()
