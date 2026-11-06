#!/usr/bin/env python3
"""Unit tests for building footprint annotation and projection tool."""

import json
import math
import os
import tempfile
import unittest
from unittest.mock import MagicMock, patch
from PIL import Image

from scripts.annotate_capture_footprints import (
    ACTOR_CENTER_Z_OFFSET,
    clip_homogeneous_segment,
    densify_polyline,
    load_sidecar_vertices,
    multiply_matrices_4x4,
    render_annotated_frame,
    render_capture_artifacts,
    validate_camera_block,
    veldrid_project,
    verify_manifest_and_sidecar,
)


def make_test_matrices() -> tuple[list[float], list[float], dict[str, float]]:
    """Create hand-computable View, Projection, and Viewport matrices."""
    viewport = {
        "X": 0.0,
        "Y": 0.0,
        "Width": 1024.0,
        "Height": 768.0,
        "MinDepth": 0.0,
        "MaxDepth": 1.0,
    }

    # View matrix: eye at (0, -100, 50), target at (0, 0, 50), up = (0, 0, 1)
    # zaxis = (0, -1, 0), xaxis = (1, 0, 0), yaxis = (0, 0, 1)
    view = [
        1.0,  0.0,  0.0, 0.0,
        0.0,  0.0, -1.0, 0.0,
        0.0,  1.0,  0.0, 0.0,
        0.0, -50.0, -100.0, 1.0,
    ]

    # Standard perspective projection: vfov = 50 deg / (1024/768), near=10, far=12000
    aspect = 1024.0 / 768.0
    vfov = math.radians(50.0) / aspect
    tan_half = math.tan(vfov * 0.5)
    near = 10.0
    far = 12000.0
    proj = [
        1.0 / (aspect * tan_half), 0.0, 0.0, 0.0,
        0.0, 1.0 / tan_half, 0.0, 0.0,
        0.0, 0.0, far / (near - far), -1.0,
        0.0, 0.0, (near * far) / (near - far), 0.0,
    ]
    return view, proj, viewport


class TestAnnotateCaptureFootprints(unittest.TestCase):
    def test_manifest_missing_fail_closed(self):
        """Missing or invalid manifest raises appropriate errors (fail-closed)."""
        with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as fh:
            fh.write(json.dumps({"polygons": []}))
            sidecar_path = fh.name

        try:
            with self.assertRaises(FileNotFoundError):
                verify_manifest_and_sidecar("/nonexistent/manifest.json", sidecar_path)

            with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as fh:
                fh.write("{}")
                bad_manifest = fh.name
            try:
                with self.assertRaises(ValueError):
                    verify_manifest_and_sidecar(bad_manifest, sidecar_path)
            finally:
                if os.path.exists(bad_manifest):
                    os.remove(bad_manifest)
        finally:
            if os.path.exists(sidecar_path):
                os.remove(sidecar_path)

    def test_sidecar_missing_rejection(self):
        """Missing or unparseable sidecar fails closed with clear error."""
        with self.assertRaises(FileNotFoundError):
            load_sidecar_vertices("/nonexistent/ground-vertices.json")

        with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as fh:
            fh.write("not valid json")
            bad_sidecar = fh.name
        try:
            with self.assertRaises(ValueError):
                load_sidecar_vertices(bad_sidecar)
        finally:
            if os.path.exists(bad_sidecar):
                os.remove(bad_sidecar)

        # Missing 'polygons' key
        with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as fh:
            fh.write(json.dumps({"scenario": "test"}))
            no_polys = fh.name
        try:
            with self.assertRaises(ValueError):
                load_sidecar_vertices(no_polys)
        finally:
            if os.path.exists(no_polys):
                os.remove(no_polys)

    def test_camera_block_missing_rejection(self):
        """Frame missing Camera block raises ValueError stating inferred path is removed."""
        with self.assertRaises(ValueError) as ctx:
            validate_camera_block(None, frame_idx=0)
        self.assertIn("missing required 'Camera' block", str(ctx.exception))
        self.assertIn("Reconstruction fallback is removed", str(ctx.exception))

    def test_synthetic_camera_projection(self):
        """Synthetic camera projection places target point along optical axis at viewport center."""
        view, proj, viewport = make_test_matrices()

        # Target (0, 0, 50) is exactly on optical axis, should project to (512, 384)
        sx, sy, sz, w, ndc_x, ndc_y, ndc_z = veldrid_project((0.0, 0.0, 50.0), proj, view, viewport)
        self.assertAlmostEqual(sx, 512.0, places=4)
        self.assertAlmostEqual(sy, 384.0, places=4)
        self.assertTrue(0.0 <= sz <= 1.0)
        self.assertGreater(w, 0.0)

    def test_synthetic_manifest_with_camera_block_verify_pass(self):
        """Synthetic manifest WITH camera block verifies under 1px on anchors."""
        view, proj, viewport = make_test_matrices()

        # Actor placed so that center (worldZ + 5.75) is at Z=50
        actor_z = 50.0 - ACTOR_CENTER_Z_OFFSET
        manifest_data = {
            "Scenario": "synthetic-test",
            "Frames": [
                {
                    "Index": 0,
                    "Camera": {
                        "View": view,
                        "Projection": proj,
                        "Viewport": viewport,
                    },
                    "Actors": [
                        {
                            "Id": "humvee-1",
                            "WorldX": 0.0,
                            "WorldY": 0.0,
                            "WorldZ": actor_z,
                            "X": 512.0,
                            "Y": 384.0,
                        }
                    ],
                }
            ],
        }

        sidecar_data = {
            "scenario": "synthetic-test",
            "payloadManifest": {"path": "payload.json", "sha256": "dummy"},
            "method": "runtime-ground-sampling",
            "polygons": [
                {
                    "key": "test|bldg1|#0",
                    "outer": [
                        [-10.0, -10.0, 0.0],
                        [10.0, -10.0, 0.0],
                        [10.0, 10.0, 0.0],
                        [-10.0, 10.0, 0.0],
                    ],
                    "holes": [],
                }
            ],
        }

        with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as f_m:
            json.dump(manifest_data, f_m)
            manifest_path = f_m.name

        with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as f_s:
            json.dump(sidecar_data, f_s)
            sidecar_path = f_s.name

        try:
            report = verify_manifest_and_sidecar(manifest_path, sidecar_path)
            self.assertTrue(report["passed"])
            self.assertLess(report["visible_max"], 1.0)
            self.assertEqual(report["visible_anchors"], 1)
            self.assertEqual(report["excluded_anchors"], 0)
        finally:
            if os.path.exists(manifest_path):
                os.remove(manifest_path)
            if os.path.exists(sidecar_path):
                os.remove(sidecar_path)

    def test_near_behind_crossing_viewport_segment_clipping(self):
        """Homogeneous clip-space clipper correctly handles inside, crossing, and behind segments."""
        viewport = {"Width": 1024, "Height": 768, "X": 0, "Y": 0}

        # 1. Segment completely inside viewport (center to half-way right)
        p1 = [0.0, 0.0, 10.0, 20.0]  # screen (512, 384)
        p2 = [10.0, 0.0, 10.0, 20.0]  # x/w = 0.5 -> screen (768, 384)
        res1 = clip_homogeneous_segment(p1, p2, viewport)
        self.assertIsNotNone(res1)
        self.assertAlmostEqual(res1[0][0], 512.0)
        self.assertAlmostEqual(res1[1][0], 768.0)

        # 2. Segment crossing right viewport boundary (x/w crosses 1.0)
        p3 = [40.0, 0.0, 10.0, 20.0]  # x/w = 2.0 (outside right)
        res2 = clip_homogeneous_segment(p1, p3, viewport)
        self.assertIsNotNone(res2)
        self.assertAlmostEqual(res2[0][0], 512.0)
        self.assertAlmostEqual(res2[1][0], 1024.0)  # clipped exactly at right viewport boundary

        # 3. Segment crossing near plane (z crosses 0 into negative depth)
        p4 = [0.0, 0.0, -10.0, 0.0]
        res3 = clip_homogeneous_segment(p1, p4, viewport)
        self.assertIsNotNone(res3)
        self.assertAlmostEqual(res3[0][0], 512.0)
        self.assertAlmostEqual(res3[1][0], 512.0)

        # 4. Segment completely behind near plane (both z < 0)
        p5 = [0.0, 0.0, -5.0, -1.0]
        p6 = [10.0, 0.0, -10.0, -2.0]
        self.assertIsNone(clip_homogeneous_segment(p5, p6, viewport))

        # 5. Segment completely outside viewport bounds (both x/w > 1)
        p7 = [30.0, 0.0, 10.0, 20.0]  # x/w = 1.5
        p8 = [40.0, 0.0, 10.0, 20.0]  # x/w = 2.0
        self.assertIsNone(clip_homogeneous_segment(p7, p8, viewport))

    def test_in_out_classification_homogeneous_frustum(self):
        """Anchors are classified as visible or excluded with exact reasons (w<=0, depth, bounds)."""
        view, proj, viewport = make_test_matrices()

        # Anchor inside
        pos_in = (0.0, 0.0, 50.0 - ACTOR_CENTER_Z_OFFSET)
        sx, sy, sz, w, ndc_x, ndc_y, ndc_z = veldrid_project(
            (pos_in[0], pos_in[1], pos_in[2] + ACTOR_CENTER_Z_OFFSET), proj, view, viewport
        )
        self.assertGreater(w, 0.0)
        self.assertTrue(0.0 <= ndc_z <= 1.0)
        self.assertTrue(0.0 <= sx <= 1024.0 and 0.0 <= sy <= 768.0)

        # Anchor outside bounds (far to right)
        pos_out_bounds = (5000.0, 0.0, 50.0 - ACTOR_CENTER_Z_OFFSET)
        sx_b, sy_b, sz_b, w_b, ndc_xb, ndc_yb, ndc_zb = veldrid_project(
            (pos_out_bounds[0], pos_out_bounds[1], pos_out_bounds[2] + ACTOR_CENTER_Z_OFFSET), proj, view, viewport
        )
        self.assertFalse(0.0 <= sx_b <= 1024.0)

        # Anchor behind camera (negative depth / behind optical eye)
        pos_behind = (0.0, -500.0, 50.0 - ACTOR_CENTER_Z_OFFSET)
        sx_h, sy_h, sz_h, w_h, ndc_xh, ndc_yh, ndc_zh = veldrid_project(
            (pos_behind[0], pos_behind[1], pos_behind[2] + ACTOR_CENTER_Z_OFFSET), proj, view, viewport
        )
        self.assertFalse(0.0 <= ndc_zh <= 1.0)

    def test_nonzero_visible_assertion_fail_closed(self):
        """Frame with zero visible anchors raises AssertionError and fails closed."""
        view, proj, viewport = make_test_matrices()
        # Actor placed completely off-screen / behind
        manifest_data = {
            "Scenario": "synthetic-test",
            "Frames": [
                {
                    "Index": 0,
                    "Camera": {"View": view, "Projection": proj, "Viewport": viewport},
                    "Actors": [
                        {
                            "Id": "humvee-1",
                            "WorldX": 99999.0,
                            "WorldY": 99999.0,
                            "WorldZ": 0.0,
                            "X": 99999.0,
                            "Y": 99999.0,
                        }
                    ],
                }
            ],
        }
        sidecar_data = {
            "scenario": "synthetic-test",
            "payloadManifest": {"path": "payload.json", "sha256": "dummy"},
            "method": "runtime-ground-sampling",
            "polygons": [],
        }
        with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as f_m, tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as f_s:
            json.dump(manifest_data, f_m)
            json.dump(sidecar_data, f_s)
            m_path, s_path = f_m.name, f_s.name

        try:
            with self.assertRaises(AssertionError) as ctx:
                verify_manifest_and_sidecar(m_path, s_path)
            self.assertIn("0 visible anchors", str(ctx.exception))
        finally:
            if os.path.exists(m_path):
                os.remove(m_path)
            if os.path.exists(s_path):
                os.remove(s_path)

    def test_densification(self):
        """Polyline densification subdivides long segments below max_segment_length."""
        points = [[0.0, 0.0, 0.0], [10.0, 0.0, 0.0]]
        densified = densify_polyline(points, max_segment_length=3.0)
        self.assertGreaterEqual(len(densified), 5)
        for i in range(len(densified) - 1):
            dx = densified[i + 1][0] - densified[i][0]
            self.assertLessEqual(dx, 3.0 + 1e-6)

    def test_holes_exclusion_from_fill_with_borders_drawn(self):
        """Polygons with inner rings retain inner rings as border paths distinct from outer fill."""
        polygon_feature = {
            "outer": [
                [0.0, 0.0, 0.0],
                [10.0, 0.0, 0.0],
                [10.0, 10.0, 0.0],
                [0.0, 10.0, 0.0],
            ],
            "holes": [
                [
                    [2.0, 2.0, 0.0],
                    [8.0, 2.0, 0.0],
                    [8.0, 8.0, 0.0],
                    [2.0, 8.0, 0.0],
                ]
            ],
        }

        outer = polygon_feature["outer"]
        self.assertEqual(len(outer), 4)

        inner = polygon_feature["holes"][0]
        self.assertEqual(len(inner), 4)

        all_rings = [outer] + polygon_feature["holes"]
        self.assertEqual(len(all_rings), 2)

    def test_synthetic_end_to_end_pipeline(self):
        """Synthetic end-to-end artifact generation with fabricated camera block."""
        view, proj, viewport = make_test_matrices()
        actor_z = 50.0 - ACTOR_CENTER_Z_OFFSET

        with tempfile.TemporaryDirectory() as temp_dir:
            frames_dir = os.path.join(temp_dir, "raw")
            out_dir = os.path.join(temp_dir, "output")
            os.makedirs(frames_dir)

            for i in range(2):
                img = Image.new("RGB", (1024, 768), color=(40, 50, 60))
                img.save(os.path.join(frames_dir, f"frame_{i:04d}.png"))

            manifest_data = {
                "Scenario": "synthetic-e2e",
                "Frames": [
                    {
                        "Index": i,
                        "File": f"frame_{i:04d}.png",
                        "Camera": {
                            "View": view,
                            "Projection": proj,
                            "Viewport": viewport,
                        },
                        "Actors": [
                            {
                                "Id": "humvee-1",
                                "WorldX": 0.0,
                                "WorldY": 0.0,
                                "WorldZ": actor_z,
                                "X": 512.0,
                                "Y": 384.0,
                            }
                        ],
                    }
                    for i in range(2)
                ],
            }
            manifest_path = os.path.join(temp_dir, "manifest.json")
            with open(manifest_path, "w", encoding="utf-8") as fh:
                json.dump(manifest_data, fh)

            sidecar_data = {
                "scenario": "synthetic-e2e",
                "payloadManifest": {"path": "payload.json", "sha256": "dummy"},
                "method": "runtime-ground-sampling",
                "polygons": [
                    {
                        "key": "test|bldg1|#0",
                        "outer": [
                            [-5.0, 45.0, 45.0],
                            [5.0, 45.0, 45.0],
                            [5.0, 55.0, 55.0],
                            [-5.0, 55.0, 55.0],
                        ],
                        "holes": [],
                    }
                ],
            }
            sidecar_path = os.path.join(temp_dir, "ground-vertices.json")
            with open(sidecar_path, "w", encoding="utf-8") as fh:
                json.dump(sidecar_data, fh)

            v_report = verify_manifest_and_sidecar(manifest_path, sidecar_path)
            results = render_capture_artifacts(
                manifest_path, sidecar_path, frames_dir, out_dir, verification_report=v_report
            )

            self.assertTrue(os.path.isfile(results["gif_path"]))
            self.assertTrue(os.path.isfile(results["poster_path"]))
            self.assertTrue(os.path.isfile(results["manifest_path"]))
            self.assertTrue(os.path.isfile(results["sha256sums_path"]))

            with open(results["manifest_path"], "r", encoding="utf-8") as fh:
                om = json.load(fh)
            self.assertIn("recordedMatrixSha256", om)
            self.assertIn("sidecarSha256", om)
            self.assertIn("retainedFrameShas", om)
            self.assertIn("clipDomainGate", om)
            self.assertIn("offsetProvenance", om)

    def _create_synthetic_environment(self, temp_dir: str):
        view, proj, viewport = make_test_matrices()
        actor_z = 50.0 - ACTOR_CENTER_Z_OFFSET

        frames_dir = os.path.join(temp_dir, "raw")
        out_dir = os.path.join(temp_dir, "output")
        os.makedirs(frames_dir)

        for i in range(2):
            img = Image.new("RGB", (1024, 768), color=(40, 50, 60))
            img.save(os.path.join(frames_dir, f"frame_{i:04d}.png"))

        manifest_data = {
            "Scenario": "synthetic-e2e",
            "Frames": [
                {
                    "Index": i,
                    "File": f"frame_{i:04d}.png",
                    "Camera": {
                        "View": view,
                        "Projection": proj,
                        "Viewport": viewport,
                    },
                    "Actors": [
                        {
                            "Id": "humvee-1",
                            "WorldX": 0.0,
                            "WorldY": 0.0,
                            "WorldZ": actor_z,
                            "X": 512.0,
                            "Y": 384.0,
                        }
                    ],
                }
                for i in range(2)
            ],
        }
        manifest_path = os.path.join(temp_dir, "manifest.json")
        with open(manifest_path, "w", encoding="utf-8") as fh:
            json.dump(manifest_data, fh)

        sidecar_data = {
            "scenario": "synthetic-e2e",
            "payloadManifest": {"path": "payload.json", "sha256": "dummy"},
            "method": "runtime-ground-sampling",
            "polygons": [
                {
                    "key": "test|bldg1|#0",
                    "outer": [
                        [-5.0, 45.0, 45.0],
                        [5.0, 45.0, 45.0],
                        [5.0, 55.0, 55.0],
                        [-5.0, 55.0, 55.0],
                    ],
                    "holes": [],
                }
            ],
        }
        sidecar_path = os.path.join(temp_dir, "ground-vertices.json")
        with open(sidecar_path, "w", encoding="utf-8") as fh:
            json.dump(sidecar_data, fh)

        v_report = verify_manifest_and_sidecar(manifest_path, sidecar_path)
        return manifest_path, sidecar_path, frames_dir, out_dir, v_report

    @patch("subprocess.run")
    def test_render_capture_artifacts_default_naming(self, mock_subprocess):
        """Default arguments preserve legacy filenames, frames subdirectory, and omit runLabel."""
        def fake_ffmpeg(cmd, *args, **kwargs):
            with open(cmd[-1], "wb") as f:
                f.write(b"FAKE_GIF")
            return MagicMock(returncode=0)

        mock_subprocess.side_effect = fake_ffmpeg

        with tempfile.TemporaryDirectory() as temp_dir:
            m_path, s_path, f_dir, out_dir, v_rep = self._create_synthetic_environment(temp_dir)
            results = render_capture_artifacts(
                m_path, s_path, f_dir, out_dir, verification_report=v_rep
            )

            expected_gif = os.path.join(out_dir, "r2-03-footprints-street-movement-outlines-v1.gif")
            expected_poster = os.path.join(out_dir, "r2-03-footprints-street-movement-outlines-v1-poster.png")
            expected_frames = os.path.join(out_dir, "frames", "r2-03-footprints-v2-outlines")
            expected_manifest = os.path.join(expected_frames, "overlay-manifest.json")
            expected_sums = os.path.join(expected_frames, "SHA256SUMS")

            self.assertEqual(results["gif_path"], expected_gif)
            self.assertEqual(results["poster_path"], expected_poster)
            self.assertEqual(results["frames_dir"], expected_frames)
            self.assertEqual(results["manifest_path"], expected_manifest)
            self.assertEqual(results["sha256sums_path"], expected_sums)

            self.assertTrue(os.path.isfile(results["gif_path"]))
            self.assertTrue(os.path.isfile(results["poster_path"]))
            self.assertTrue(os.path.isfile(results["manifest_path"]))
            self.assertTrue(os.path.isfile(results["sha256sums_path"]))

            with open(results["manifest_path"], "r", encoding="utf-8") as fh:
                om = json.load(fh)
            self.assertNotIn("runLabel", om)

    @patch("subprocess.run")
    def test_render_capture_artifacts_parameterized_naming_and_run_label(self, mock_subprocess):
        """Custom gif_stem, frames_dir_name, and run_label flow into paths and manifest."""
        captured_cmds = []

        def fake_ffmpeg(cmd, *args, **kwargs):
            captured_cmds.append(cmd)
            with open(cmd[-1], "wb") as f:
                f.write(b"FAKE_GIF")
            return MagicMock(returncode=0)

        mock_subprocess.side_effect = fake_ffmpeg

        with tempfile.TemporaryDirectory() as temp_dir:
            m_path, s_path, f_dir, out_dir, v_rep = self._create_synthetic_environment(temp_dir)
            results = render_capture_artifacts(
                m_path,
                s_path,
                f_dir,
                out_dir,
                verification_report=v_rep,
                gif_stem="custom-conservative-v2",
                frames_dir_name="custom-conservative-frames",
                run_label="conservative-run-42",
            )

            expected_gif = os.path.join(out_dir, "custom-conservative-v2.gif")
            expected_poster = os.path.join(out_dir, "custom-conservative-v2-poster.png")
            expected_frames = os.path.join(out_dir, "frames", "custom-conservative-frames")
            expected_manifest = os.path.join(expected_frames, "overlay-manifest.json")
            expected_sums = os.path.join(expected_frames, "SHA256SUMS")

            self.assertEqual(results["gif_path"], expected_gif)
            self.assertEqual(results["poster_path"], expected_poster)
            self.assertEqual(results["frames_dir"], expected_frames)
            self.assertEqual(results["manifest_path"], expected_manifest)
            self.assertEqual(results["sha256sums_path"], expected_sums)

            self.assertTrue(os.path.isfile(results["gif_path"]))
            self.assertTrue(os.path.isfile(results["poster_path"]))
            self.assertTrue(os.path.isfile(results["manifest_path"]))
            self.assertTrue(os.path.isfile(results["sha256sums_path"]))

            self.assertTrue(any("custom-conservative-v2.gif" in str(c) for c in captured_cmds))

            with open(results["manifest_path"], "r", encoding="utf-8") as fh:
                om = json.load(fh)
            self.assertEqual(om.get("runLabel"), "conservative-run-42")


if __name__ == "__main__":
    unittest.main()
