#!/usr/bin/env python3
"""Offline unit tests for check_capture_vs_masks script."""

from __future__ import annotations

import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

# Dynamically import check_capture_vs_masks from parent directory
SCRIPT_PATH = Path(__file__).resolve().parents[1] / "check_capture_vs_masks.py"
spec = importlib.util.spec_from_file_location("check_capture_vs_masks", SCRIPT_PATH)
checker_mod = importlib.util.module_from_spec(spec)
assert spec and spec.loader
spec.loader.exec_module(checker_mod)


def make_chunk_bytes(
    impassable_cells: list[tuple[int, int]] | None = None,
    unbuildable_cells: list[tuple[int, int]] | None = None,
    length: int = checker_mod.CORE_CHUNK_BYTES,
    bad_byte_pos: int | None = None,
    bad_byte_val: int = 4,
) -> bytes:
    """Create a 16641-byte chunk buffer with optional flags."""
    buf = bytearray(length)
    if impassable_cells:
        for row, col in impassable_cells:
            if 0 <= row < checker_mod.POINTS_PER_CHUNK_AXIS and 0 <= col < checker_mod.POINTS_PER_CHUNK_AXIS:
                buf[row * checker_mod.POINTS_PER_CHUNK_AXIS + col] |= checker_mod.FLAG_IMPASSABLE
    if unbuildable_cells:
        for row, col in unbuildable_cells:
            if 0 <= row < checker_mod.POINTS_PER_CHUNK_AXIS and 0 <= col < checker_mod.POINTS_PER_CHUNK_AXIS:
                buf[row * checker_mod.POINTS_PER_CHUNK_AXIS + col] |= checker_mod.FLAG_UNBUILDABLE
    if bad_byte_pos is not None and bad_byte_pos < length:
        buf[bad_byte_pos] = bad_byte_val
    return bytes(buf)


def create_synthetic_payload(
    payload_dir: str,
    chunks_x: int = 2,
    chunks_y: int = 2,
    chunk_buffers: dict[tuple[int, int], bytes] | None = None,
) -> str:
    """Write synthetic payload.json and mask files."""
    masks_dir = os.path.join(payload_dir, "masks")
    os.makedirs(masks_dir, exist_ok=True)
    chunks_list = []

    for cy in range(chunks_y):
        for cx in range(chunks_x):
            mask_rel = f"masks/{cx}_{cy}.bin"
            mask_full = os.path.join(payload_dir, mask_rel)
            buf = (chunk_buffers or {}).get((cx, cy))
            if buf is None:
                buf = make_chunk_bytes()
            with open(mask_full, "wb") as f:
                f.write(buf)

            chunks_list.append({
                "cx": cx,
                "cy": cy,
                "masks": {
                    "path": mask_rel,
                    "lengthBytes": len(buf),
                    "sha256": "0" * 64,
                },
            })

    payload_json = {
        "kind": "panopticon-geospatial-payload",
        "schemaVersion": 1,
        "grid": {
            "chunksX": chunks_x,
            "chunksY": chunks_y,
            "layout": {
                "sampleSpacingGameUnits": 10.0,
                "intervalsPerChunk": checker_mod.INTERVALS_PER_CHUNK,
                "pointsPerChunkAxis": checker_mod.POINTS_PER_CHUNK_AXIS,
            },
        },
        "chunks": chunks_list,
    }

    payload_json_path = os.path.join(payload_dir, "payload.json")
    with open(payload_json_path, "w", encoding="utf-8") as f:
        json.dump(payload_json, f, indent=2)

    return payload_json_path


class TestCheckCaptureVsMasks(unittest.TestCase):
    """Offline unit test suite for check_capture_vs_masks."""

    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = self.temp_dir.name
        self.payload_dir = os.path.join(self.root, "payload")
        os.makedirs(self.payload_dir, exist_ok=True)

        # Place an obstacle at (col 50, row 20) in chunk (0, 0)
        c00 = make_chunk_bytes(impassable_cells=[(20, 50)])
        self.chunk_map = {
            (0, 0): c00,
            (1, 0): make_chunk_bytes(),
            (0, 1): make_chunk_bytes(),
            (1, 1): make_chunk_bytes(),
        }
        create_synthetic_payload(self.payload_dir, chunks_x=2, chunks_y=2, chunk_buffers=self.chunk_map)

        # Base profile: start at (200, 200), goal at (800, 200)
        self.profile = {
            "Name": "test-scenario",
            "Actors": [
                {
                    "Id": "unit-1",
                    "Definition": "AmericaVehicleHumvee",
                    "X": 200.0,
                    "Y": 200.0,
                }
            ],
            "Commands": [
                {
                    "Type": "Move",
                    "ActorId": "unit-1",
                    "X": 800.0,
                    "Y": 200.0,
                }
            ],
        }

        # Base manifest: navigates around the obstacle at (500, 200)
        self.manifest = {
            "SchemaVersion": 1,
            "Scenario": "test-scenario",
            "Frames": [
                {
                    "Index": 0,
                    "AbsoluteLogicFrame": 0,
                    "Actors": [{"Id": "unit-1", "WorldX": 200.0, "WorldY": 200.0}],
                },
                {
                    "Index": 1,
                    "AbsoluteLogicFrame": 15,
                    "Actors": [{"Id": "unit-1", "WorldX": 350.0, "WorldY": 280.0}],
                },
                {
                    "Index": 2,
                    "AbsoluteLogicFrame": 30,
                    "Actors": [{"Id": "unit-1", "WorldX": 500.0, "WorldY": 280.0}],
                },
                {
                    "Index": 3,
                    "AbsoluteLogicFrame": 45,
                    "Actors": [{"Id": "unit-1", "WorldX": 650.0, "WorldY": 280.0}],
                },
                {
                    "Index": 4,
                    "AbsoluteLogicFrame": 60,
                    "Actors": [{"Id": "unit-1", "WorldX": 800.0, "WorldY": 200.0}],
                },
            ],
        }

        self.profile_path = os.path.join(self.root, "profile.json")
        self.manifest_path = os.path.join(self.root, "manifest.json")
        self._write_files()

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def _write_files(self) -> None:
        with open(self.profile_path, "w", encoding="utf-8") as f:
            json.dump(self.profile, f, indent=2)
        with open(self.manifest_path, "w", encoding="utf-8") as f:
            json.dump(self.manifest, f, indent=2)

    def test_pass_case(self) -> None:
        """Verify standard scenario passes all 4 checks and outputs summary line."""
        passed, checks, summary = checker_mod.validate_capture(
            manifest_path=self.manifest_path,
            payload_path=self.payload_dir,
            profile_path=self.profile_path,
            tolerance=60.0,
        )
        self.assertTrue(passed)
        self.assertEqual(len(checks), 4)
        for check in checks:
            self.assertTrue(check.passed, f"Check failed: {check.name} - {check.summary}")

        self.assertIn("frames spanned: 5", summary)
        self.assertIn("60 ticks", summary)
        self.assertIn("2.00s at 30 Hz", summary)
        self.assertIn("unit-1: REACHED", summary)

    def test_injected_impassable_traversal_fails(self) -> None:
        """Verify injected position on an impassable mask cell fails Check 1."""
        # Frame 2 placed directly on obstacle cell (col 50, row 20) -> world (500, 200)
        self.manifest["Frames"][2]["Actors"][0]["WorldY"] = 200.0
        self._write_files()

        passed, checks, _ = checker_mod.validate_capture(
            manifest_path=self.manifest_path,
            payload_path=self.payload_dir,
            profile_path=self.profile_path,
        )
        self.assertFalse(passed)
        self.assertFalse(checks[0].passed)
        self.assertIn("carries Impassable bit", checks[0].details[0])

    def test_missed_goal_fails(self) -> None:
        """Verify final actor position outside tolerance fails Check 2."""
        # Final position stops at X=700, distance 100 > tolerance 60
        self.manifest["Frames"][4]["Actors"][0]["WorldX"] = 700.0
        self._write_files()

        passed, checks, _ = checker_mod.validate_capture(
            manifest_path=self.manifest_path,
            payload_path=self.payload_dir,
            profile_path=self.profile_path,
            tolerance=60.0,
        )
        self.assertFalse(passed)
        self.assertFalse(checks[1].passed)
        self.assertIn("missed", checks[1].summary.lower())
        self.assertIn("MISSED", checks[1].details[0])

    def test_extra_actor_fails(self) -> None:
        """Verify unintended actor in manifest fails Check 3."""
        self.manifest["Frames"][0]["Actors"].append({
            "Id": "unintended-actor",
            "WorldX": 200.0,
            "WorldY": 200.0,
        })
        self._write_files()

        passed, checks, _ = checker_mod.validate_capture(
            manifest_path=self.manifest_path,
            payload_path=self.payload_dir,
            profile_path=self.profile_path,
        )
        self.assertFalse(passed)
        self.assertFalse(checks[2].passed)
        self.assertIn("unintended-actor", checks[2].summary)

    def test_missing_actor_fails(self) -> None:
        """Verify actor defined in profile but missing from manifest fails Check 3."""
        self.profile["Actors"].append({
            "Id": "missing-actor",
            "Definition": "AmericaVehicleHumvee",
            "X": 300.0,
            "Y": 300.0,
        })
        self._write_files()

        passed, checks, _ = checker_mod.validate_capture(
            manifest_path=self.manifest_path,
            payload_path=self.payload_dir,
            profile_path=self.profile_path,
        )
        self.assertFalse(passed)
        self.assertFalse(checks[2].passed)
        self.assertIn("missing-actor", checks[2].summary)

    def test_out_of_grid_position_fails(self) -> None:
        """Verify unit positions outside the payload grid boundaries fail Check 1."""
        # Negative coordinate
        self.manifest["Frames"][1]["Actors"][0]["WorldX"] = -100.0
        self._write_files()

        passed, checks, _ = checker_mod.validate_capture(
            manifest_path=self.manifest_path,
            payload_path=self.payload_dir,
            profile_path=self.profile_path,
        )
        self.assertFalse(passed)
        self.assertFalse(checks[0].passed)
        self.assertIn("out-of-grid", checks[0].details[0])

        # Exceeding grid extent (2 chunks * 128 * 10 = 2560)
        self.manifest["Frames"][1]["Actors"][0]["WorldX"] = 5000.0
        self._write_files()

        passed, checks, _ = checker_mod.validate_capture(
            manifest_path=self.manifest_path,
            payload_path=self.payload_dir,
            profile_path=self.profile_path,
        )
        self.assertFalse(passed)
        self.assertFalse(checks[0].passed)

    def test_straight_line_zero_case_fails(self) -> None:
        """Verify 0 impassable cells along straight line fails Check 4 when unexempt."""
        # Recreate payload with no obstacle (all clear)
        all_clear = {
            (0, 0): make_chunk_bytes(),
            (1, 0): make_chunk_bytes(),
            (0, 1): make_chunk_bytes(),
            (1, 1): make_chunk_bytes(),
        }
        create_synthetic_payload(self.payload_dir, chunks_x=2, chunks_y=2, chunk_buffers=all_clear)

        passed, checks, _ = checker_mod.validate_capture(
            manifest_path=self.manifest_path,
            payload_path=self.payload_dir,
            profile_path=self.profile_path,
        )
        self.assertFalse(passed)
        self.assertFalse(checks[3].passed)
        self.assertIn("0 impassable cells", checks[3].details[0])

    def test_straight_line_zero_with_profile_exemption_passes(self) -> None:
        """Verify 0 impassable cells along straight line passes when profile documents reason."""
        all_clear = {
            (0, 0): make_chunk_bytes(),
            (1, 0): make_chunk_bytes(),
            (0, 1): make_chunk_bytes(),
            (1, 1): make_chunk_bytes(),
        }
        create_synthetic_payload(self.payload_dir, chunks_x=2, chunks_y=2, chunk_buffers=all_clear)

        self.profile["StraightLineExemption"] = "Testing unblocked traversal scenario"
        self._write_files()

        passed, checks, _ = checker_mod.validate_capture(
            manifest_path=self.manifest_path,
            payload_path=self.payload_dir,
            profile_path=self.profile_path,
        )
        self.assertTrue(passed)
        self.assertTrue(checks[3].passed)
        self.assertIn("EXEMPT", checks[3].details[0])

    def test_actor_set_with_results_created(self) -> None:
        """Verify Created entries in --results file are factored into actor set check."""
        self.profile["Actors"].append({
            "Id": "spawned-reinforcement",
            "Definition": "AmericaVehicleHumvee",
            "X": 100.0,
            "Y": 100.0,
        })
        self._write_files()

        # Without results, missing spawned-reinforcement fails
        passed_without, checks_without, _ = checker_mod.validate_capture(
            manifest_path=self.manifest_path,
            payload_path=self.payload_dir,
            profile_path=self.profile_path,
        )
        self.assertFalse(passed_without)
        self.assertFalse(checks_without[2].passed)

        # Write results file with Created entry
        results_path = os.path.join(self.root, "results.txt")
        with open(results_path, "w", encoding="utf-8") as f:
            f.write("# TestHarness action results\n")
            f.write("10\tCreated\tObjectLifecycle\tUSA\tactorId=spawned-reinforcement def=AmericaVehicleHumvee\n")

        passed_with, checks_with, _ = checker_mod.validate_capture(
            manifest_path=self.manifest_path,
            payload_path=self.payload_dir,
            profile_path=self.profile_path,
            results_path=results_path,
        )
        self.assertTrue(passed_with)
        self.assertTrue(checks_with[2].passed)

    def test_mask_file_invalid_length_fails_closed(self) -> None:
        """Verify non-16641 byte mask files fail closed."""
        bad_chunk = make_chunk_bytes(length=1000)
        bad_map = {
            (0, 0): bad_chunk,
            (1, 0): make_chunk_bytes(),
            (0, 1): make_chunk_bytes(),
            (1, 1): make_chunk_bytes(),
        }
        create_synthetic_payload(self.payload_dir, chunks_x=2, chunks_y=2, chunk_buffers=bad_map)

        with self.assertRaises(checker_mod.CaptureCheckerError):
            checker_mod.validate_capture(
                manifest_path=self.manifest_path,
                payload_path=self.payload_dir,
                profile_path=self.profile_path,
            )

    def test_mask_file_undefined_bits_fails_closed(self) -> None:
        """Verify mask flag byte with undefined bits fails closed."""
        bad_chunk = make_chunk_bytes(bad_byte_pos=10, bad_byte_val=0x08)
        bad_map = {
            (0, 0): bad_chunk,
            (1, 0): make_chunk_bytes(),
            (0, 1): make_chunk_bytes(),
            (1, 1): make_chunk_bytes(),
        }
        create_synthetic_payload(self.payload_dir, chunks_x=2, chunks_y=2, chunk_buffers=bad_map)

        with self.assertRaises(checker_mod.CaptureCheckerError):
            checker_mod.validate_capture(
                manifest_path=self.manifest_path,
                payload_path=self.payload_dir,
                profile_path=self.profile_path,
            )

    def test_cli_execution(self) -> None:
        """Verify command-line interface execution and exit codes."""
        cmd = [
            sys.executable,
            str(SCRIPT_PATH),
            "--manifest",
            self.manifest_path,
            "--payload",
            self.payload_dir,
            "--profile",
            self.profile_path,
            "--tolerance",
            "60",
        ]
        proc = subprocess.run(cmd, capture_output=True, text=True)
        self.assertEqual(proc.returncode, 0, f"STDOUT:\n{proc.stdout}\nSTDERR:\n{proc.stderr}")
        self.assertIn("[PASS] Check 1", proc.stdout)
        self.assertIn("[PASS] Check 2", proc.stdout)
        self.assertIn("[PASS] Check 3", proc.stdout)
        self.assertIn("[PASS] Check 4", proc.stdout)
        self.assertIn("Summary:", proc.stdout)

        # Invalidate goal to test exit code 1
        self.manifest["Frames"][4]["Actors"][0]["WorldX"] = 0.0
        self._write_files()

        proc_fail = subprocess.run(cmd, capture_output=True, text=True)
        self.assertEqual(proc_fail.returncode, 1)
        self.assertIn("[FAIL] Check 2", proc_fail.stdout)


if __name__ == "__main__":
    unittest.main()
