"""Offline tests for the --cache-root override of scripts/geo/sedan_lidar_probe.py.

Conventions follow test_sedan_lidar_freeze.py: importlib module load, tempfile trees and
replaced seams. Every test runs main() against a temporary root. The network seam and
subprocess.run raise if reached, and fetch stops at the mount-realness guard.
"""

import contextlib
import hashlib
import importlib.util
import io
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace

_GEO_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(_GEO_DIR))

_spec = importlib.util.spec_from_file_location("sedan_lidar_probe", _GEO_DIR / "sedan_lidar_probe.py")
lidar = importlib.util.module_from_spec(_spec)
sys.modules["sedan_lidar_probe"] = lidar
_spec.loader.exec_module(lidar)

PRODUCTION_CACHE_ROOT = "/mnt/pit2/panopticon/geo-proof/sedan"
LOT = "NUALHD_1-0__LAZ_LAMB93_OD_2026-04-29"
FIRST_TILE_NAME = "LHD_FXX_0840_6958_PTS_LAMB93_IGN69.copc.laz"
PROBE_ARGV = [
    "probe", "--worker", "/bin/true", "/nonexistent/worker.py",
    "--lineage-file", "/nonexistent/lineage.json",
]
FETCH_ARGV = ["fetch", FIRST_TILE_NAME, "--first-tile", "--execution-brief-accepted"]
SUBCOMMAND_TAILS = (["preflight"], ["status"], ["verify"], ["plan-tools"], PROBE_ARGV, FETCH_ARGV)


def forbidden_network(url, method="GET", headers=None):
    raise AssertionError(f"unexpected network request: {method} {url}")


def forbidden_subprocess(*args, **kwargs):
    raise AssertionError(f"unexpected subprocess execution: {args!r}")


def synthetic_digest(name):
    return hashlib.sha256(name.encode("utf-8")).hexdigest()


def synthetic_manifest_text():
    lines = ["# synthetic archive completion evidence (test fixture)"]
    for name, size in lidar.ARCHIVE_EVIDENCE_PARTS:
        lines.append(f"{synthetic_digest(name)} {size} {name}")
    return "\n".join(lines) + "\n"


def synthetic_state_products(mismatch=False):
    products = {}
    for name, size in lidar.ARCHIVE_EVIDENCE_PARTS:
        key = ("BDORTHO/" if name.startswith("BDORTHO") else "RGEALTI/") + name
        products[key] = {
            "status": "complete",
            "sha256": "0" * 64 if mismatch else synthetic_digest(name),
            "expected_size": size,
            "bytes_on_disk": size,
        }
    return products


def strip_timestamps(text):
    return "\n".join(line.split(" ", 1)[-1] for line in text.splitlines())


class DefaultRootIdentityTests(unittest.TestCase):
    def test_module_default_paths(self):
        self.assertEqual(lidar.CACHE_ROOT, PRODUCTION_CACHE_ROOT)
        self.assertIsNone(lidar.build_parser().parse_args(["status"]).cache_root)
        self.assertEqual(lidar.resolve_root(None), PRODUCTION_CACHE_ROOT)
        paths = lidar.layout(PRODUCTION_CACHE_ROOT)
        self.assertEqual(paths["tile"], os.path.join(PRODUCTION_CACHE_ROOT, "lidarhd", LOT, FIRST_TILE_NAME))
        self.assertEqual(paths["state"], os.path.join(PRODUCTION_CACHE_ROOT, "lidarhd", "state", "state.json"))
        self.assertEqual(paths["tools"], os.path.join(PRODUCTION_CACHE_ROOT, "tools", "lidar-venv"))
        self.assertEqual(lidar.archive_state_path(), os.path.join(PRODUCTION_CACHE_ROOT, "state", "state.json"))


class RootOverrideTests(unittest.TestCase):
    def setUp(self):
        base = Path(tempfile.mkdtemp(prefix="sedan-lidar-probe-root-"))
        self.addCleanup(shutil.rmtree, str(base), True)
        self.base = base
        self.default_root = str(base / "default-cache")
        self.custom_root = str(base / "custom-cache")
        self.manifest = base / "hash-manifest.txt"
        self.manifest.write_text(synthetic_manifest_text())
        self.replace(lidar, "CACHE_ROOT", self.default_root)
        self.replace(lidar.acquire, "statvfs", lambda path: SimpleNamespace(f_bavail=1 << 30, f_frsize=4096))
        self.replace(lidar, "open_tile_url", forbidden_network)
        self.replace(subprocess, "run", forbidden_subprocess)

    def replace(self, target, name, value):
        original = getattr(target, name)
        setattr(target, name, value)
        self.addCleanup(setattr, target, name, original)

    def run_main(self, *argv):
        buffer = io.StringIO()
        with contextlib.redirect_stdout(buffer):
            code = lidar.main(list(argv))
        return code, buffer.getvalue()

    def tile_path(self, root):
        return os.path.join(root, "lidarhd", LOT, FIRST_TILE_NAME)

    def state_path(self, root):
        return os.path.join(root, "lidarhd", "state", "state.json")

    def archive_args(self):
        return ["--archive-evidence", str(self.manifest)]

    def test_absent_flag_uses_module_constant(self):
        code, out = self.run_main("status")
        self.assertEqual(code, lidar.EXIT_OK)
        self.assertIn(self.state_path(self.default_root), out)
        self.assertNotIn(self.custom_root, out)

    def test_global_flag_parses_before_subcommand(self):
        args = lidar.build_parser().parse_args(["--cache-root", "/x", "preflight"])
        self.assertEqual((args.cache_root, args.command), ("/x", "preflight"))

    def test_flag_after_subcommand_is_rejected(self):
        parser = lidar.build_parser()
        for argv in (["preflight", "--cache-root", "/x"], ["status", "--cache-root", "/x"]):
            with self.subTest(argv=argv), contextlib.redirect_stderr(io.StringIO()):
                with self.assertRaises(SystemExit) as caught:
                    parser.parse_args(argv)
                self.assertEqual(caught.exception.code, 2)

    def test_custom_root_resolves_every_layout_path(self):
        paths = lidar.layout(self.custom_root)
        self.assertEqual(paths["cache"], self.custom_root)
        for key, value in paths.items():
            if key != "cache":
                self.assertTrue(value.startswith(self.custom_root + os.sep), key)

    def test_preflight_threads_custom_root(self):
        code, out = self.run_main("--cache-root", self.custom_root, *self.archive_args(), "preflight")
        self.assertEqual(code, lidar.EXIT_OK)
        self.assertIn(f"planned tile file: {self.tile_path(self.custom_root)}", out)
        self.assertIn(f"planned state file: {self.state_path(self.custom_root)}", out)
        self.assertNotIn(self.default_root, out)
        self.assertFalse(os.path.exists(self.custom_root))

    def test_status_threads_custom_root(self):
        code, out = self.run_main("--cache-root", self.custom_root, "status")
        self.assertEqual(code, lidar.EXIT_OK)
        self.assertIn(f"probe state file: {self.state_path(self.custom_root)}", out)
        lock = os.path.join(self.custom_root, "lidarhd", "state", "lock")
        self.assertIn(f"lock: absent (planned {lock})", out)
        self.assertNotIn(self.default_root, out)

    def test_verify_threads_custom_root(self):
        code, out = self.run_main("--cache-root", self.custom_root, "verify")
        self.assertEqual(code, lidar.EXIT_STATE)
        self.assertIn(f"FAIL({lidar.EXIT_STATE}): ", out)
        self.assertIn(self.tile_path(self.custom_root), out)
        self.assertNotIn(self.default_root, out)

    def test_probe_threads_custom_root(self):
        code, out = self.run_main("--cache-root", self.custom_root, *PROBE_ARGV)
        self.assertEqual(code, lidar.EXIT_STATE)
        self.assertIn(self.tile_path(self.custom_root), out)
        self.assertNotIn(self.default_root, out)

    def test_fetch_threads_custom_root_to_mount_guard(self):
        code, out = self.run_main("--cache-root", self.custom_root, *self.archive_args(), *FETCH_ARGV)
        self.assertEqual(code, lidar.EXIT_GUARD)
        self.assertIn(f"mount realness NOT verified for {os.path.join(self.custom_root, 'lidarhd', LOT)}", out)
        self.assertFalse(os.path.exists(self.custom_root))

    def test_plan_tools_threads_custom_root(self):
        code, out = self.run_main("--cache-root", self.custom_root, "plan-tools")
        self.assertEqual(code, lidar.EXIT_OK)
        self.assertIn(f"tools dir: {os.path.join(self.custom_root, 'tools', 'lidar-venv')}", out)
        self.assertNotIn(self.default_root, out)

    def test_archive_owned_state_is_read_from_root(self):
        state_file = Path(lidar.archive_state_path(self.custom_root))
        self.assertEqual(str(state_file), os.path.join(self.custom_root, "state", "state.json"))
        state_file.parent.mkdir(parents=True)
        state_file.write_text(json.dumps({"version": 1, "products": synthetic_state_products(mismatch=True)}))
        with self.assertRaises(lidar.Failure) as caught:
            with contextlib.redirect_stdout(io.StringIO()):
                lidar.require_archive_evidence(str(self.manifest), hard=True, root=self.custom_root)
        self.assertEqual(caught.exception.code, lidar.EXIT_GUARD)
        self.assertIn(str(state_file), caught.exception.message)
        with contextlib.redirect_stdout(io.StringIO()) as buffer:
            self.assertTrue(lidar.require_archive_evidence(str(self.manifest), hard=True, root=self.default_root))
        self.assertIn("manifest-only verification", buffer.getvalue())

    def test_preflight_reports_root_state_mismatch(self):
        state_file = Path(lidar.archive_state_path(self.custom_root))
        state_file.parent.mkdir(parents=True)
        state_file.write_text(json.dumps({"version": 1, "products": synthetic_state_products(mismatch=True)}))
        code, out = self.run_main("--cache-root", self.custom_root, *self.archive_args(), "preflight")
        self.assertEqual(code, lidar.EXIT_OK)
        self.assertIn(f"WARNING: archive owned-state at {state_file}", out)

    def test_invalid_roots_fail_with_guard_exit(self):
        plain_file = self.base / "plain-file"
        plain_file.write_text("not a directory\n")
        invalid_roots = {
            "empty": "",
            "relative": "relative/cache",
            "dotdot": f"{self.base}/x/../custom-cache",
            "regular-file": str(plain_file),
        }
        for label, root in invalid_roots.items():
            for argv in SUBCOMMAND_TAILS:
                with self.subTest(root=label, command=argv[0]):
                    code, out = self.run_main("--cache-root", root, *self.archive_args(), *argv)
                    self.assertEqual(code, lidar.EXIT_GUARD)
                    self.assertIn(f"FAIL({lidar.EXIT_GUARD})", out)
        self.assertEqual(plain_file.read_text(), "not a directory\n")
        self.assertFalse(os.path.exists(self.custom_root))

    def test_override_does_not_mutate_module_constant(self):
        self.run_main("--cache-root", self.custom_root, "status")
        self.assertEqual(lidar.CACHE_ROOT, self.default_root)
        code, out = self.run_main("status")
        self.assertIn(self.state_path(self.default_root), out)
        self.assertNotIn(self.custom_root, out)

    def test_verify_result_is_identical_across_roots(self):
        payload = b"synthetic first tile bytes for the verify round trip\n"
        digest = hashlib.sha256(payload).hexdigest()
        self.replace(lidar, "FIRST_TILE", lidar.FIRST_TILE._replace(size=len(payload)))
        roots = [self.custom_root, str(self.base / "other-custom-cache")]
        for root in roots:
            tile = Path(lidar.layout(root)["tile"])
            tile.parent.mkdir(parents=True)
            tile.write_bytes(payload)
            state = {
                "version": lidar.PROBE_STATE_VERSION,
                "tile": {
                    "url": lidar.FIRST_TILE.url,
                    "expected_size": len(payload),
                    "bytes_on_disk": len(payload),
                    "status": "complete",
                    "mtime_ns": tile.lstat().st_mtime_ns,
                    "sha256": digest,
                },
            }
            state_file = Path(lidar.layout(root)["state"])
            state_file.parent.mkdir(parents=True)
            state_file.write_text(json.dumps(state) + "\n")
        results = [self.run_main("--cache-root", root, "verify") for root in roots]
        normalized = []
        for root, (code, out) in zip(roots, results):
            self.assertEqual(code, lidar.EXIT_OK)
            self.assertIn(digest, out)
            normalized.append(strip_timestamps(out).replace(root, "<ROOT>"))
        self.assertEqual(normalized[0], normalized[1])


if __name__ == "__main__":
    unittest.main()
