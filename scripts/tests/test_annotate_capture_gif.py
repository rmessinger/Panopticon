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


if __name__ == "__main__":
    unittest.main()
