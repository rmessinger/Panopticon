"""Offline tests for scripts/geo/sedan_lidar_integrity.py (stdlib only by default).

Conventions follow test_sedan_lidar_freeze.py: importlib module load, tempfile
trees under /tmp/opencode, patched seams, and real subprocesses. Pins are synthetic
(tiny files, real names and layout), so no real LAZ tile, DEM or network is touched.
Pipeline tests run the real producer against a generated stand-in header worker.
RealVenvHeaderTests run the real sedan_lidar_header_worker.py under the lidar venv
on small synthetic LAS files when the venv and laspy are available, else skip.
"""

import contextlib
import importlib.util
import hashlib
import io
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True

GEO_DIR = "/home/rmessinger/dev/Panopticon-track-r-sedan-proof/scripts/geo"
TMP_BASE = "/tmp/opencode"
REAL_VENV_PYTHON = "/mnt/pit2/panopticon/geo-proof/sedan/tools/lidar-venv/bin/python"
REAL_WORKER = os.path.join(GEO_DIR, "sedan_lidar_header_worker.py")


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


M = load_module("sedan_lidar_integrity", os.path.join(GEO_DIR, "sedan_lidar_integrity.py"))

STAND_IN_WORKER = '''import json, os, sys, time
MODE = "__MODE__"
request = json.load(open(sys.argv[1], encoding="utf-8"))
tile = request["tile_path"]
if MODE == "exit":
    sys.exit(3)
if MODE == "timeout":
    time.sleep(30)
if MODE == "flood":
    sys.stdout.write("x" * 5000)
    sys.stdout.flush()
    sys.exit(0)
if MODE == "garbage":
    with open(request["output_path"], "x", encoding="utf-8") as handle:
        handle.write("{not json")
    sys.exit(0)
header = {"las_version": "1.4", "point_format_id": 6, "point_count": 2,
          "x_min": 839000.0, "x_max": 839010.0, "y_min": 6957000.0, "y_max": 6957010.0,
          "x_scale": 0.01, "y_scale": 0.01, "z_scale": 0.01,
          "x_offset": 0.0, "y_offset": 0.0, "z_offset": 0.0,
          "crs": {"epsg": 2154, "name": "RGF93 v1 / Lambert-93"}}
if MODE == "nan":
    header["x_min"] = float("nan")
if MODE == "outofbox":
    header["x_min"] = 100.0
if MODE == "scale":
    header["x_scale"] = 0.001
if MODE == "crs":
    header["crs"] = {"epsg": 4326, "name": "WGS 84"}
payload = {"header_version": 1, "tile_path": tile, "tile_bytes": os.path.getsize(tile),
           "header": header}
with open(request["output_path"], "x", encoding="utf-8") as handle:
    handle.write(json.dumps(payload))
print("stand-in header ok")
'''

WRITE_SYNTHETIC_LAS = '''import sys
import laspy
import numpy as np
header = laspy.LasHeader(point_format=6, version="1.4")
header.scales = np.array([0.01, 0.01, 0.01])
header.offsets = np.array([0.0, 0.0, 0.0])
las = laspy.LasData(header)
las.x = np.array([839000.0, 839010.0])
las.y = np.array([6957000.0, 6957010.0])
las.z = np.array([1.0, 2.0])
las.update_header()
las.write(sys.argv[1])
'''


def sha(data):
    return hashlib.sha256(data).hexdigest()


def real_venv_skip_reason():
    if not os.path.isfile(REAL_VENV_PYTHON):
        return f"lidar venv python absent at {REAL_VENV_PYTHON}"
    probe = subprocess.run([REAL_VENV_PYTHON, "-c", "import laspy, lazrs"],
                           capture_output=True, text=True, timeout=60)
    if probe.returncode != 0:
        return f"laspy/lazrs unavailable in the lidar venv: {probe.stderr.strip()[-200:]}"
    return None


class IntegrityFixture(unittest.TestCase):
    def setUp(self):
        os.makedirs(TMP_BASE, exist_ok=True)
        self.base = tempfile.mkdtemp(prefix="integrity-", dir=TMP_BASE)
        self.addCleanup(shutil.rmtree, self.base, True)
        self.cache = os.path.join(self.base, "cache")
        os.makedirs(self.cache)
        self.mounts = os.path.join(self.base, "mounts")
        self.write_mounts("rw")
        self.patch(M, "MOUNT_POINT", self.base)
        self.patch(M, "PROC_MOUNTS_PATH", self.mounts)
        self.spawns = []
        original_spawn = M.spawn_process

        def spy(argv):
            self.spawns.append(list(argv))
            return original_spawn(argv)

        self.patch(M, "spawn_process", spy)
        self.venv = os.path.join(self.cache, *M.VENV_PYTHON_REL.split("/"))
        os.makedirs(os.path.dirname(self.venv))
        os.symlink(sys.executable, self.venv)
        self.worker = self.stand_in("ok")
        self.pins = self.make_fixture()

    def patch(self, obj, name, value):
        original = getattr(obj, name)
        setattr(obj, name, value)
        self.addCleanup(setattr, obj, name, original)

    def write_mounts(self, options):
        with open(self.mounts, "w", encoding="utf-8") as handle:
            handle.write(f"tmpfs {self.base} tmpfs {options} 0 0\n")

    def stand_in(self, mode):
        path = os.path.join(self.base, f"stand_in_{mode}.py")
        with open(path, "w", encoding="utf-8") as handle:
            handle.write(STAND_IN_WORKER.replace("__MODE__", mode))
        return path

    def make_fixture(self):
        specs = [(M.FIRST_TILE_NAME, 64)] + [(name, 40 + i) for i, (name, _s) in enumerate(M.STAGE2_TILES)]
        pins, data_by_name = [], {}
        for index, (name, size) in enumerate(specs):
            rel = (f"lidarhd/{M.LOT}/{name}" if index == 0 else f"lidarhd/stage2/{M.LOT}/{name}")
            data = (name.encode() * 10)[:size]
            path = os.path.join(self.cache, *rel.split("/"))
            os.makedirs(os.path.dirname(path), exist_ok=True)
            with open(path, "wb") as handle:
                handle.write(data)
            data_by_name[name] = data
            pins.append({"name": name, "size": size,
                         "sha256": sha(data) if index == 0 else None, "rel": rel})
        self.data_by_name = data_by_name
        self.pins = pins
        self.write_state(None)
        return pins

    def state_tiles(self):
        return {pin["name"]: {"status": "complete", "expected_size": pin["size"],
                              "bytes_on_disk": pin["size"], "mtime_ns": 1,
                              "sha256": sha(self.data_by_name[pin["name"]]),
                              "url": "https://example.invalid/" + pin["name"]}
                for pin in self.pins[1:]}

    def write_state(self, tiles):
        path = os.path.join(self.cache, *M.STATE_REL.split("/"))
        os.makedirs(os.path.dirname(path), exist_ok=True)
        if tiles is None:
            tiles = self.state_tiles()
        with open(path, "w", encoding="utf-8") as handle:
            json.dump({"version": 1, "tiles": tiles}, handle)

    def tile_path(self, pin):
        return os.path.join(self.cache, *pin["rel"].split("/"))

    def snapshot(self):
        listing = []
        for root, dirs, files in os.walk(self.cache):
            for name in dirs + files:
                listing.append(os.path.relpath(os.path.join(root, name), self.cache))
        return sorted(listing)

    def prepare(self, worker=None, venv=None):
        return M.prepare(self.cache, self.pins, venv or self.venv, worker or self.worker)

    def expect_failure(self, code, fn):
        with self.assertRaises(M.Failure) as caught:
            fn()
        self.assertEqual(caught.exception.code, code, caught.exception.message)
        return caught.exception.message

    def manifest_path(self):
        return os.path.join(self.cache, M.FREEZE_DIR, M.MANIFEST_NAME)

    def receipt_path(self):
        return os.path.join(self.cache, M.FREEZE_DIR, M.RECEIPT_NAME)

    def freeze_leftovers(self):
        freeze = os.path.join(self.cache, M.FREEZE_DIR)
        if not os.path.isdir(freeze):
            return []
        return sorted(os.listdir(freeze))


class PinAndPreflightTests(IntegrityFixture):
    def test_real_pin_table_validates_and_confinement_refuses_drift(self):
        self.assertEqual(len(M.validate_pin_table(M.PINS)), 6)
        renamed = [dict(p) for p in M.PINS]
        renamed[1]["name"] = "LHD_FXX_0839_6958_PTS_LAMB93_IGN69.laz"
        self.expect_failure(M.EXIT_STATE, lambda: M.validate_pin_table(renamed))
        escaped = [dict(p) for p in M.PINS]
        escaped[2]["rel"] = "lidarhd/stage2/../" + escaped[2]["name"]
        self.expect_failure(M.EXIT_STATE, lambda: M.validate_pin_table(escaped))
        zero = [dict(p) for p in M.PINS]
        zero[3]["size"] = 0
        self.expect_failure(M.EXIT_STATE, lambda: M.validate_pin_table(zero))

    def test_preflight_writes_nothing_and_spawns_nothing(self):
        before = self.snapshot()
        self.assertEqual(M.preflight(self.cache, self.pins, self.venv), M.EXIT_OK)
        self.assertEqual(self.snapshot(), before)
        self.assertEqual(self.spawns, [])

    def test_mount_not_rw_is_guard_exit_2(self):
        self.write_mounts("ro")
        self.expect_failure(M.EXIT_GUARD, self.prepare)
        self.assertEqual(self.spawns, [])
        self.assertFalse(os.path.exists(os.path.join(self.cache, M.FREEZE_DIR)))


class TransportStateGuardTests(IntegrityFixture):
    def assert_refused_before_children(self, code):
        self.expect_failure(code, self.prepare)
        self.assertEqual(self.spawns, [])
        self.assertFalse(os.path.exists(self.manifest_path()))
        self.assertFalse(os.path.exists(os.path.join(self.cache, M.FREEZE_DIR)))

    def test_missing_state_is_guard_exit_2(self):
        os.unlink(os.path.join(self.cache, *M.STATE_REL.split("/")))
        self.assert_refused_before_children(M.EXIT_GUARD)

    def test_corrupt_state_is_exit_4(self):
        path = os.path.join(self.cache, *M.STATE_REL.split("/"))
        with open(path, "w", encoding="utf-8") as handle:
            handle.write("{truncated")
        self.assert_refused_before_children(M.EXIT_STATE)

    def test_uncompleted_state_is_exit_4(self):
        tiles = self.state_tiles()
        tiles[self.pins[1]["name"]]["status"] = "downloading"
        self.write_state(tiles)
        self.assert_refused_before_children(M.EXIT_STATE)

    def test_state_size_mismatch_is_exit_4(self):
        tiles = self.state_tiles()
        tiles[self.pins[2]["name"]]["expected_size"] += 1
        self.write_state(tiles)
        self.assert_refused_before_children(M.EXIT_STATE)

    def test_state_with_wrong_tile_names_is_exit_4(self):
        tiles = self.state_tiles()
        tiles["LHD_FXX_0999_0000_PTS_LAMB93_IGN69.copc.laz"] = tiles.pop(self.pins[1]["name"])
        self.write_state(tiles)
        self.assert_refused_before_children(M.EXIT_STATE)

    def test_on_disk_size_mismatch_is_exit_4(self):
        with open(self.tile_path(self.pins[3]), "ab") as handle:
            handle.write(b"x")
        self.assert_refused_before_children(M.EXIT_STATE)

    def test_same_size_flipped_bytes_fail_sha_before_children(self):
        path = self.tile_path(self.pins[4])
        data = bytearray(open(path, "rb").read())
        data[0] ^= 0xFF
        with open(path, "wb") as handle:
            handle.write(bytes(data))
        message = self.expect_failure(M.EXIT_STATE, self.prepare)
        self.assertIn(self.pins[4]["name"], message)
        self.assertEqual(self.spawns, [])
        self.assertFalse(os.path.exists(os.path.join(self.cache, M.FREEZE_DIR)))

    def test_first_tile_sha_mismatch_is_exit_4(self):
        path = self.tile_path(self.pins[0])
        with open(path, "wb") as handle:
            handle.write(b"\x00" * self.pins[0]["size"])
        self.expect_failure(M.EXIT_STATE, self.prepare)
        self.assertEqual(self.spawns, [])

    def test_first_tile_symlink_is_exit_4(self):
        path = self.tile_path(self.pins[0])
        target = path + ".real"
        os.rename(path, target)
        os.symlink(target, path)
        self.expect_failure(M.EXIT_STATE, self.prepare)
        self.assertEqual(self.spawns, [])

    def test_freeze_path_as_file_is_exit_4_and_untouched(self):
        freeze = os.path.join(self.cache, M.FREEZE_DIR)
        with open(freeze, "w", encoding="utf-8") as handle:
            handle.write("operator file")
        self.expect_failure(M.EXIT_STATE, self.prepare)
        self.assertEqual(self.spawns, [])
        with open(freeze, encoding="utf-8") as handle:
            self.assertEqual(handle.read(), "operator file")

    def test_missing_venv_is_guard_exit_2_before_any_write(self):
        os.unlink(self.venv)
        self.expect_failure(M.EXIT_GUARD, self.prepare)
        self.assertEqual(self.spawns, [])
        self.assertFalse(os.path.exists(os.path.join(self.cache, M.FREEZE_DIR)))


class ProducerPipelineTests(IntegrityFixture):
    def test_success_writes_schema_receipt_and_clears_scratch(self):
        result = self.prepare()
        self.assertEqual(result["status"], "written")
        self.assertEqual(len(self.spawns), 6)
        with open(self.manifest_path(), encoding="utf-8") as handle:
            manifest = json.load(handle)
        self.assertEqual(set(manifest), {p["name"] for p in self.pins})
        first = manifest[self.pins[0]["name"]]
        self.assertEqual(first["rel_path"], self.pins[0]["rel"])
        self.assertEqual(first["sha256"], self.pins[0]["sha256"])
        self.assertEqual(first["footprint_l93"],
                         {"x_min": 839000.0, "y_min": 6957000.0, "x_max": 839010.0, "y_max": 6957010.0})
        self.assertNotIn("worker", manifest)
        with open(self.receipt_path(), encoding="utf-8") as handle:
            receipt = json.load(handle)
        self.assertEqual(receipt["manifest"]["sha256"], sha(open(self.manifest_path(), "rb").read()))
        self.assertEqual(receipt["transport_state"]["sha256"],
                         sha(open(os.path.join(self.cache, *M.STATE_REL.split("/")), "rb").read()))
        self.assertEqual(self.freeze_leftovers(), sorted([M.MANIFEST_NAME, M.RECEIPT_NAME]))

    def test_rerun_on_identical_inputs_is_idempotent_with_zero_children(self):
        self.prepare()
        manifest_bytes = open(self.manifest_path(), "rb").read()
        spawned = len(self.spawns)
        result = self.prepare()
        self.assertEqual(result["status"], "idempotent")
        self.assertEqual(len(self.spawns), spawned)
        self.assertEqual(open(self.manifest_path(), "rb").read(), manifest_bytes)

    def test_different_existing_manifest_is_exit_4_without_overwrite(self):
        self.prepare()
        with open(self.manifest_path(), encoding="utf-8") as handle:
            manifest = json.load(handle)
        manifest[self.pins[1]["name"]]["sha256"] = "0" * 64
        broken = json.dumps(manifest, sort_keys=True).encode()
        with open(self.manifest_path(), "wb") as handle:
            handle.write(broken)
        spawned = len(self.spawns)
        self.expect_failure(M.EXIT_STATE, self.prepare)
        self.assertEqual(len(self.spawns), spawned)
        self.assertEqual(open(self.manifest_path(), "rb").read(), broken)

    def test_manifest_without_receipt_is_exit_4(self):
        self.prepare()
        os.unlink(self.receipt_path())
        spawned = len(self.spawns)
        self.expect_failure(M.EXIT_STATE, self.prepare)
        self.assertEqual(len(self.spawns), spawned)

    def test_transport_state_modified_after_manifest_is_drift_exit_4(self):
        self.prepare()
        manifest_bytes = open(self.manifest_path(), "rb").read()
        tiles = self.state_tiles()
        tiles[self.pins[5]["name"]]["mtime_ns"] = 2
        self.write_state(tiles)
        spawned = len(self.spawns)
        message = self.expect_failure(M.EXIT_STATE, self.prepare)
        self.assertIn("drift", message)
        self.assertEqual(len(self.spawns), spawned)
        self.assertEqual(open(self.manifest_path(), "rb").read(), manifest_bytes)

    def test_state_mutated_after_children_before_promotion_publishes_nothing(self):
        original = M.validate_header_payload
        validated = []

        def mutate_after_last_child(payload, record, tile_path):
            checked = original(payload, record, tile_path)
            validated.append(record["name"])
            if len(validated) == len(self.pins):
                tiles = self.state_tiles()
                tiles[self.pins[1]["name"]]["mtime_ns"] = 2
                self.write_state(tiles)
            return checked

        self.patch(M, "validate_header_payload", mutate_after_last_child)
        message = self.expect_failure(M.EXIT_STATE, self.prepare)
        self.assertIn("drift", message)
        self.assertEqual(len(self.spawns), 6)
        self.assertFalse(os.path.exists(self.manifest_path()))
        self.assertFalse(os.path.exists(self.receipt_path()))
        retained = [name for name in self.freeze_leftovers() if name.startswith(M.SCRATCH_PREFIX)]
        self.assertEqual(len(retained), 1)
        self.assertTrue(os.path.isfile(os.path.join(self.cache, M.FREEZE_DIR, retained[0],
                                                    "PROMOTION-DRIFT.txt")))
        self.assertFalse(os.path.exists(M.producer_marker_path(self.cache)))

    def test_worker_nonzero_exit_is_exit_3_and_leaves_nothing(self):
        self.expect_failure(M.EXIT_WORKER, lambda: self.prepare(worker=self.stand_in("exit")))
        self.assertFalse(os.path.exists(self.manifest_path()))
        self.assertFalse(os.path.exists(self.receipt_path()))
        self.assertFalse(os.path.exists(M.producer_marker_path(self.cache)))
        self.assertEqual(self.freeze_leftovers(), [])

    def test_worker_timeout_is_exit_3(self):
        self.patch(M, "CHILD_DEADLINE_SECONDS", 0.5)
        self.expect_failure(M.EXIT_WORKER, lambda: self.prepare(worker=self.stand_in("timeout")))
        self.assertEqual(self.freeze_leftovers(), [])

    def test_worker_output_flood_is_exit_3(self):
        self.patch(M, "CHILD_CAP_BYTES", 1000)
        self.expect_failure(M.EXIT_WORKER, lambda: self.prepare(worker=self.stand_in("flood")))
        self.assertFalse(os.path.exists(self.manifest_path()))

    def test_truncated_or_garbage_header_output_is_exit_4(self):
        self.expect_failure(M.EXIT_STATE, lambda: self.prepare(worker=self.stand_in("garbage")))
        self.assertFalse(os.path.exists(self.manifest_path()))
        self.assertEqual(self.freeze_leftovers(), [])

    def test_nonfinite_header_value_is_reported_exit_4(self):
        message = self.expect_failure(M.EXIT_STATE, lambda: self.prepare(worker=self.stand_in("nan")))
        self.assertIn("non-finite", message)
        self.assertFalse(os.path.exists(self.manifest_path()))

    def test_out_of_family_header_is_reported_not_clamped_exit_4(self):
        message = self.expect_failure(M.EXIT_STATE, lambda: self.prepare(worker=self.stand_in("outofbox")))
        self.assertIn("x_min", message)
        self.assertIn("100.0", message)
        self.assertFalse(os.path.exists(self.manifest_path()))

    def test_scale_drift_is_exit_4(self):
        message = self.expect_failure(M.EXIT_STATE, lambda: self.prepare(worker=self.stand_in("scale")))
        self.assertIn("x_scale", message)

    def test_non_lambert93_crs_is_exit_4(self):
        message = self.expect_failure(M.EXIT_STATE, lambda: self.prepare(worker=self.stand_in("crs")))
        self.assertIn("4326", message)

    def test_marker_present_refuses_before_children_and_recover_lock_rules(self):
        marker = M.producer_marker_path(self.cache)
        os.makedirs(os.path.dirname(marker), exist_ok=True)
        with open(marker, "w", encoding="utf-8") as handle:
            handle.write(json.dumps({"pid": os.getpid(), "started": "x"}))
        self.expect_failure(M.EXIT_STATE, self.prepare)
        self.assertEqual(self.spawns, [])
        with self.assertRaises(M.Failure) as caught:
            M.recover_lock(self.cache)
        self.assertEqual(caught.exception.code, M.EXIT_STATE)
        self.assertTrue(os.path.exists(marker))
        dead = subprocess.Popen(["true"])
        dead.wait()
        with open(marker, "w", encoding="utf-8") as handle:
            handle.write(json.dumps({"pid": dead.pid, "started": "x"}))
        self.assertEqual(M.recover_lock(self.cache), M.EXIT_OK)
        self.assertFalse(os.path.exists(marker))

    def test_producer_manifest_is_accepted_by_consumer_reader(self):
        self.prepare()
        consumer = load_module("sedan_lidar_freeze_reader", os.path.join(GEO_DIR, "sedan_lidar_freeze.py"))
        entries, worker, present, problem = consumer.read_integrity_manifest(self.manifest_path())
        self.assertTrue(present)
        self.assertIsNone(problem)
        self.assertIsNone(worker)
        self.assertEqual(set(entries), {p["name"] for p in consumer.TILE_PINS})
        self.assertEqual(entries[self.pins[0]["name"]]["rel_path"], self.pins[0]["rel"])
        self.assertEqual(entries[self.pins[1]["name"]]["rel_path"], self.pins[1]["rel"])

    def test_main_cli_default_is_preflight_and_mutually_exclusive_modes(self):
        self.patch(M, "PINS", tuple(self.pins))
        self.patch(M, "WORKER_PATH", self.worker)
        self.assertEqual(M.main(["--cache", self.cache]), M.EXIT_OK)
        self.assertEqual(self.spawns, [])
        with self.assertRaises(SystemExit), contextlib.redirect_stderr(io.StringIO()):
            M.main(["--cache", self.cache, "--prepare-integrity", "--recover-lock"])
        self.assertEqual(M.main(["--cache", self.cache, "--prepare-integrity"]), M.EXIT_OK)
        self.assertEqual(len(self.spawns), 6)


class RealVenvHeaderTests(IntegrityFixture):
    def setUp(self):
        reason = real_venv_skip_reason()
        if reason is not None:
            self.skipTest(reason)
        super().setUp()

    def test_real_venv_header_worker_round_trip_on_small_synthetic_las(self):
        for pin in self.pins:
            path = self.tile_path(pin)
            os.unlink(path)
            result = subprocess.run([REAL_VENV_PYTHON, "-c", WRITE_SYNTHETIC_LAS, path],
                                    capture_output=True, text=True, timeout=120)
            if result.returncode != 0:
                self.skipTest(f"lidar venv cannot write synthetic LAS: {result.stderr.strip()[-200:]}")
            pin["size"] = os.path.getsize(path)
            pin["sha256"] = sha(open(path, "rb").read()) if pin is self.pins[0] else None
        tiles = self.state_tiles()
        for pin in self.pins[1:]:
            tiles[pin["name"]].update({"expected_size": pin["size"], "bytes_on_disk": pin["size"],
                                       "sha256": sha(open(self.tile_path(pin), "rb").read())})
        self.write_state(tiles)
        result = M.prepare(self.cache, self.pins, REAL_VENV_PYTHON, REAL_WORKER)
        self.assertEqual(result["status"], "written")
        self.assertEqual(len(self.spawns), 6)
        with open(self.receipt_path(), encoding="utf-8") as handle:
            receipt = json.load(handle)
        for pin in self.pins:
            record = receipt["tiles"][pin["name"]]
            self.assertEqual(record["point_count"], 2)
            self.assertEqual(record["point_format_id"], 6)
            self.assertIsNone(record["crs"])
            self.assertEqual(record["footprint_l93"]["x_max"], 839010.0)


if __name__ == "__main__":
    unittest.main()
