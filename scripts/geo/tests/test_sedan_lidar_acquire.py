"""Offline unit tests for scripts/geo/sedan_lidar_acquire.py (stdlib only, no network).

Conventions follow scripts/geo/tests/test_sedan_acquire.py and test_sedan_lidar_probe.py:
importlib module load, fake HTTP responses injected at the transport seams
(sedan_lidar_acquire.open_tile_url for GET and sedan_lidar_probe.open_tile_url for the
probe HEAD pre-check), tempfile filesystems under /tmp/opencode, injected mounts path
and statvfs. The v1.2 stage-1 report and the archive completion evidence are synthetic
fixtures; the five accepted source pins are verified against the real repository files.
No test touches a real tile, LAZ/DEM byte, publisher endpoint or the network.
"""

import contextlib
import email.message
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

_probe_spec = importlib.util.spec_from_file_location("sedan_lidar_probe", _GEO_DIR / "sedan_lidar_probe.py")
probe_mod = importlib.util.module_from_spec(_probe_spec)
sys.modules["sedan_lidar_probe"] = probe_mod
_probe_spec.loader.exec_module(probe_mod)

_spec = importlib.util.spec_from_file_location("sedan_lidar_acquire", _GEO_DIR / "sedan_lidar_acquire.py")
mod = importlib.util.module_from_spec(_spec)
sys.modules["sedan_lidar_acquire"] = mod
_spec.loader.exec_module(mod)


class FakeResponse:
    def __init__(self, status=200, headers=None, body=b""):
        self.status = status
        self.headers = headers if headers is not None else email.message.Message()
        self._body = body
        self._position = 0

    def read(self, size=-1):
        if size is None or size < 0:
            chunk = self._body[self._position:]
            self._position = len(self._body)
            return chunk
        chunk = self._body[self._position:self._position + size]
        self._position += len(chunk)
        return chunk

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, traceback):
        return False


class TrapBodyResponse(FakeResponse):
    """A HEAD response whose body must never be read (headers-only handling)."""

    def read(self, size=-1):
        raise AssertionError("HEAD response body must never be read")


class DyingResponse:
    """Response whose stream delivers one chunk, then the connection dies."""

    def __init__(self, status, headers, first_chunk):
        self.status = status
        self.headers = headers
        self._first_chunk = first_chunk
        self._reads = 0

    def read(self, size):
        self._reads += 1
        if self._reads == 1:
            return self._first_chunk[:size]
        raise ConnectionResetError("connection lost mid-stream")

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, traceback):
        return False


def message(headers):
    result = email.message.Message()
    for key, value in headers.items():
        result[key] = str(value)
    return result


def blob(size, seed=0):
    return bytes(((index * 7 + seed) % 256) for index in range(size))


def synthetic_manifest_text():
    lines = ["# synthetic archive completion evidence (test fixture, sha256 at completion)"]
    for name, size in probe_mod.ARCHIVE_EVIDENCE_PARTS:
        digest = hashlib.sha256(name.encode("utf-8")).hexdigest()
        lines.append(f"{digest}  {size}  {name}")
    return "\n".join(lines) + "\n"


def availability_payload():
    """Availability anchored to the REAL recorded payload shape: 5 primaries across
    4 distinct sectors (s00/s02/s12/s22), 2 alternates with role "alternate", the
    recorded eligible_count 41464, sectors_covered equal to the primaries' distinct
    sector set, stage_1_preliminary exactly true."""
    def candidate(index, sector, role):
        return {
            "id": f"r{29 + index}c117",
            "role": role,
            "sector": sector,
            "stratum": "S2",
            "rank": index + 1,
            "n2": 17 + index,
            "sigma_fit": 0.007 + index / 1000.0,
            "slope_deg": 4.7 + index / 10.0,
            "truth_z": 155.1 + index / 100.0,
        }

    return {
        "stage_1_preliminary": True,
        "eligible_count": 41464,
        "sectors_covered": ["s00", "s02", "s12", "s22"],
        "primaries": [
            candidate(index, sector, "primary")
            for index, sector in enumerate(["s00", "s02", "s12", "s22", "s02"])
        ],
        "alternates": [candidate(5, "s00", "alternate"), candidate(6, "s12", "alternate")],
        "alternates_shortfall": 0,
    }


def v12_report(**overrides):
    """Synthetic v1.2 stage-1 report with every gating gate a pass and the real-shape
    d_v1_2 availability payload."""
    report = {
        "report_version": 2,
        "protocol_version": "1.2",
        "gates_pass": True,
        "stage_1_preliminary": True,
        "numeric_proof_pass": False,
        "tile": mod.FIRST_TILE_NAME,
        "tile_bytes": mod.FIRST_TILE_SIZE,
        "tile_sha256": mod.FIRST_TILE_SHA256,
        "worker": {"script": "/tools/lidar-venv-worker.py", "venv_python": "/tools/lidar-venv/bin/python"},
        "gates": {
            "a": {"pass": True},
            "b": {"pass": True, "z_median": 152.0},
            "c_historical": {"note": "non-gating v1.1 record, ignored here"},
            "c_v1_2": {"pass": True, "median_class2_points_per_m2": 11.9},
            "d_v1_2": {"pass": True, "availability": availability_payload()},
            "e": {"pass": True},
            "f": {"pass": True},
        },
    }
    report.update(overrides)
    return report


class Harness(unittest.TestCase):
    def patch_mod(self, name, value):
        original = getattr(mod, name)
        setattr(mod, name, value)
        self.addCleanup(setattr, mod, name, original)

    def patch_probe(self, name, value):
        original = getattr(mod.probe, name)
        setattr(mod.probe, name, value)
        self.addCleanup(setattr, mod.probe, name, original)

    def patch_acquire(self, name, value):
        original = getattr(mod.acquire, name)
        setattr(mod.acquire, name, value)
        self.addCleanup(setattr, mod.acquire, name, original)

    def patch_os(self, name, wrapper):
        original = getattr(os, name)
        setattr(os, name, wrapper)
        self.addCleanup(setattr, os, name, original)

    def tmp_dir(self):
        parent = "/tmp/opencode" if os.path.isdir("/tmp/opencode") else None
        context = tempfile.TemporaryDirectory(prefix="sedan-lidar-acquire-test-", dir=parent)
        self.addCleanup(context.cleanup)
        return Path(context.name)

    def run_main(self, *argv):
        buffer = io.StringIO()
        with contextlib.redirect_stdout(buffer):
            code = mod.main(list(argv))
        return code, buffer.getvalue()

    def patch_statvfs(self, free_bytes):
        block = 4096
        self.patch_acquire("statvfs", lambda path: SimpleNamespace(
            f_bavail=free_bytes // block, f_frsize=block, f_bsize=block))

    def ample_space(self):
        self.patch_statvfs(1 << 45)

    def instrument_makedirs(self):
        calls = []
        original = os.makedirs

        def recorder(path, *args, **kwargs):
            calls.append(str(path))
            return original(path, *args, **kwargs)

        recorder.calls = calls
        self.patch_os("makedirs", recorder)
        return calls

    def fake_transport(self, handler):
        calls = []

        def fake_open_tile_url(url, method="GET", headers=None):
            record = {"url": url, "method": method, "headers": dict(headers or {})}
            calls.append(record)
            return handler(record)

        fake_open_tile_url.calls = calls
        self.patch_mod("open_tile_url", fake_open_tile_url)
        self.patch_probe("open_tile_url", fake_open_tile_url)
        return fake_open_tile_url

    def fetch_never(self):
        def handler(record):
            raise AssertionError(f"unexpected network call: {record}")

        return self.fake_transport(handler)

    def setup_host(self, mount_options="rw,relatime"):
        root = self.tmp_dir()
        prefix = root / "mnt" / "pit2"
        prefix.mkdir(parents=True)
        self.patch_acquire("DEFAULT_PREFIX", str(prefix))
        mounts = root / "mounts.txt"
        mounts.write_text("sysfs /sys sysfs rw,nosuid,nodev,noexec,relatime 0 0\n"
                          f"/dev/sdz {prefix} ext4 {mount_options} 0 0\n")
        self.patch_acquire("PROC_MOUNTS_PATH", str(mounts))
        probe_cache = root / "probe-cache"
        probe_cache.mkdir()
        self.patch_probe("CACHE_ROOT", str(probe_cache))
        self.ample_space()
        return {"root": root, "prefix": prefix}

    def evidence_file(self, root):
        path = root / "hash-manifest.txt"
        path.write_text(synthetic_manifest_text())
        return path

    def report_file(self, root, payload):
        path = root / "probe-report-v1_2.json"
        if isinstance(payload, (dict, list)):
            path.write_text(json.dumps(payload, indent=2))
        else:
            path.write_text(payload)
        return path

    def setup_world(self, report_payload=None):
        host = self.setup_host()
        world = dict(host)
        world["cache"] = host["prefix"] / "panopticon" / "geo-proof" / "sedan"
        world["evidence"] = self.evidence_file(host["root"])
        world["report"] = self.report_file(host["root"], report_payload if report_payload is not None else v12_report())
        return world

    def preflight_argv(self, world, report=None):
        return [
            "--archive-evidence", str(world["evidence"]),
            "--report", str(report if report is not None else world["report"]),
            "--cache-root", str(world["cache"]),
        ]

    def fetch_argv(self, world, report=None, cache_root=None):
        return [
            "--fetch-remaining",
            "--archive-evidence", str(world["evidence"]),
            "--report", str(report if report is not None else world["report"]),
            "--cache-root", str(cache_root if cache_root is not None else world["cache"]),
        ]

    def patch_pins(self, sizes):
        pins = dict(sizes)
        self.patch_mod("PINS", pins)
        self.patch_mod("REMAINING_TOTAL", sum(pins.values()))
        self.patch_mod("APPROVED_SIX_TOTAL", sum(pins.values()) + mod.FIRST_TILE_SIZE)
        return mod.stage2_tiles()

    def tile_path(self, world, name):
        return Path(world["cache"]) / "lidarhd" / "stage2" / mod.LOT / name

    def stage2_paths(self, world):
        return mod.layout(str(world["cache"]))

    def write_stage2_state(self, world, tiles_entries):
        paths = self.stage2_paths(world)
        Path(paths["state"]).parent.mkdir(parents=True, exist_ok=True)
        mod.write_state(paths["state"], {"version": mod.STATE_VERSION, "tiles": tiles_entries})

    def partial_entry(self, tile_file, tile, data):
        Path(tile_file).parent.mkdir(parents=True, exist_ok=True)
        Path(tile_file).write_bytes(data)
        return {
            "url": tile.url,
            "expected_size": tile.size,
            "bytes_on_disk": len(data),
            "status": "partial",
            "mtime_ns": os.lstat(tile_file).st_mtime_ns,
        }

    def complete_entry(self, tile_file, tile, data):
        Path(tile_file).parent.mkdir(parents=True, exist_ok=True)
        Path(tile_file).write_bytes(data)
        return {
            "url": tile.url,
            "expected_size": tile.size,
            "bytes_on_disk": tile.size,
            "status": "complete",
            "mtime_ns": os.lstat(tile_file).st_mtime_ns,
            "sha256": hashlib.sha256(data).hexdigest(),
        }

    def complete_everything(self, world, tiles):
        entries = {}
        for index, (name, tile) in enumerate(tiles.items()):
            data = blob(tile.size, seed=index)
            entries[name] = self.complete_entry(self.tile_path(world, name), tile, data)
        self.write_stage2_state(world, entries)
        return entries

    def tile_for(self, record, tiles):
        for tile in tiles.values():
            if record["url"].endswith(tile.name):
                return tile
        raise AssertionError(f"unexpected url {record['url']}")


class GateTests(Harness):
    def test_default_preflight_read_only_passes(self):
        world = self.setup_world()
        calls = self.instrument_makedirs()
        transport = self.fetch_never()
        code, output = self.run_main(*self.preflight_argv(world))
        self.assertEqual(mod.EXIT_OK, code)
        self.assertIn("preflight complete", output)
        self.assertIn("nothing was written", output)
        self.assertEqual([], calls)
        self.assertEqual([], transport.calls)
        self.assertFalse(world["cache"].exists())

    def test_gate_missing_report_rejected(self):
        world = self.setup_world()
        absent = world["root"] / "absent-report.json"
        transport = self.fetch_never()
        code, output = self.run_main(*self.preflight_argv(world, report=absent))
        self.assertEqual(mod.EXIT_GUARD, code)
        self.assertIn("missing or unreadable", output)
        self.assertEqual([], transport.calls)

    def test_gate_report_version_1_historic_rejected(self):
        world = self.setup_world(report_payload=v12_report(report_version=1, protocol_version="1.1"))
        code, output = self.run_main(*self.preflight_argv(world))
        self.assertEqual(mod.EXIT_GUARD, code)
        self.assertIn("report_version", output)
        self.assertIn("v1.1", output)

    def test_gate_bad_protocol_version_rejected(self):
        world = self.setup_world(report_payload=v12_report(protocol_version="1.1"))
        code, output = self.run_main(*self.preflight_argv(world))
        self.assertEqual(mod.EXIT_GUARD, code)
        self.assertIn("protocol_version", output)

    def test_gate_faked_self_echo_rejected(self):
        payload = v12_report()
        payload["gates"]["a"] = {"pass": False}
        world = self.setup_world(report_payload=payload)
        code, output = self.run_main(*self.preflight_argv(world))
        self.assertEqual(mod.EXIT_GUARD, code)
        self.assertIn("gate a", output)
        self.assertIn("per-gate checks are mandatory", output)

    def test_gate_nonfinite_values_rejected(self):
        nan_payload = v12_report()
        nan_payload["gates"]["c_v1_2"]["median_class2_points_per_m2"] = float("nan")
        inf_text = json.dumps(v12_report(), indent=2).replace(
            '"z_median": 152.0', '"z_median": 1e400')
        for label, payload in (("NaN token", nan_payload), ("1e400 inf", inf_text)):
            with self.subTest(case=label):
                world = self.setup_world(report_payload=payload)
                code, output = self.run_main(*self.preflight_argv(world))
                self.assertEqual(mod.EXIT_GUARD, code)
                self.assertIn("non-finite", output)

    def test_gate_oversize_report_rejected_before_parse(self):
        payload = v12_report()
        payload["filler"] = "x" * (4 * 1024 * 1024)  # otherwise-valid JSON beyond the bound
        world = self.setup_world(report_payload=payload)
        code, output = self.run_main(*self.preflight_argv(world))
        self.assertEqual(mod.EXIT_GUARD, code)
        self.assertIn("parse bound", output)

    def test_gate_corrupt_json_rejected(self):
        world = self.setup_world(report_payload="{not json")
        code, output = self.run_main(*self.preflight_argv(world))
        self.assertEqual(mod.EXIT_GUARD, code)
        self.assertIn("strictly valid JSON", output)

    def test_gate_wrong_tile_identity_rejected(self):
        cases = (
            ("name", v12_report(tile="LHD_FXX_9999_9999_PTS_LAMB93_IGN69.copc.laz"), "pinned first tile"),
            ("bytes", v12_report(tile_bytes=mod.FIRST_TILE_SIZE + 1), "tile_bytes"),
            ("sha", v12_report(tile_sha256="0" * 64), "tile_sha256"),
        )
        for label, payload, fragment in cases:
            with self.subTest(case=label):
                world = self.setup_world(report_payload=payload)
                code, output = self.run_main(*self.preflight_argv(world))
                self.assertEqual(mod.EXIT_GUARD, code)
                self.assertIn(fragment, output)

    def test_gate_stage_flags_enforced(self):
        proof_missing = v12_report()
        del proof_missing["numeric_proof_pass"]
        cases = (
            ("preliminary-false", v12_report(stage_1_preliminary=False), "stage_1_preliminary"),
            ("proof-true", v12_report(numeric_proof_pass=True), "numeric_proof_pass"),
            ("proof-missing", proof_missing, "numeric_proof_pass"),
        )
        for label, payload, fragment in cases:
            with self.subTest(case=label):
                world = self.setup_world(report_payload=payload)
                code, output = self.run_main(*self.preflight_argv(world))
                self.assertEqual(mod.EXIT_GUARD, code)
                self.assertIn(fragment, output)

    def test_gate_worker_provenance_required(self):
        cases = (
            ("no-worker", v12_report(worker={}), "provenance"),
            ("empty-script", v12_report(worker={"script": "  ", "venv_python": "/venv/bin/python"}), "provenance"),
        )
        for label, payload, fragment in cases:
            with self.subTest(case=label):
                world = self.setup_world(report_payload=payload)
                code, output = self.run_main(*self.preflight_argv(world))
                self.assertEqual(mod.EXIT_GUARD, code)
                self.assertIn(fragment, output)

    def test_archive_evidence_missing_refuses_fetch(self):
        world = self.setup_world()
        absent = world["root"] / "absent-manifest.txt"
        transport = self.fetch_never()
        argv = ["--fetch-remaining", "--archive-evidence", str(absent),
                "--report", str(world["report"]), "--cache-root", str(world["cache"])]
        code, output = self.run_main(*argv)
        self.assertEqual(mod.EXIT_GUARD, code)
        self.assertIn("archive completion evidence", output)
        self.assertEqual([], transport.calls)
        self.assertFalse(world["cache"].exists())

    def test_no_eligibility_refuses_fetch_zero_network(self):
        payload = v12_report()
        payload["gates"]["d_v1_2"] = {"pass": False}
        world = self.setup_world(report_payload=payload)
        transport = self.fetch_never()
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_GUARD, code)
        self.assertIn("d_v1_2", output)
        self.assertEqual([], transport.calls)
        self.assertFalse((world["cache"] / "lidarhd" / "stage2").exists())

    def test_gate_availability_absent_rejected(self):
        payload = v12_report()
        del payload["gates"]["d_v1_2"]["availability"]
        world = self.setup_world(report_payload=payload)
        code, output = self.run_main(*self.preflight_argv(world))
        self.assertEqual(mod.EXIT_GUARD, code)
        self.assertIn("no availability object", output)

    def test_gate_availability_primaries_empty_rejected(self):
        payload = v12_report()
        payload["gates"]["d_v1_2"]["availability"]["primaries"] = []
        world = self.setup_world(report_payload=payload)
        code, output = self.run_main(*self.preflight_argv(world))
        self.assertEqual(mod.EXIT_GUARD, code)
        self.assertIn("primaries is absent or empty", output)

    def test_gate_availability_two_primaries_rejected(self):
        payload = v12_report()
        availability = payload["gates"]["d_v1_2"]["availability"]
        availability["primaries"] = availability["primaries"][:2]
        availability["sectors_covered"] = ["s00", "s02"]
        world = self.setup_world(report_payload=payload)
        code, output = self.run_main(*self.preflight_argv(world))
        self.assertEqual(mod.EXIT_GUARD, code)
        self.assertIn("2 primaries, fewer than the required 5", output)

    def test_gate_availability_one_distinct_sector_rejected(self):
        payload = v12_report()
        availability = payload["gates"]["d_v1_2"]["availability"]
        for candidate in availability["primaries"]:
            candidate["sector"] = "s00"
        availability["sectors_covered"] = ["s00"]
        world = self.setup_world(report_payload=payload)
        code, output = self.run_main(*self.preflight_argv(world))
        self.assertEqual(mod.EXIT_GUARD, code)
        self.assertIn("distinct sector(s), fewer than the required 3", output)

    def test_gate_availability_real_shape_passes(self):
        world = self.setup_world()
        transport = self.fetch_never()
        code, output = self.run_main(*self.preflight_argv(world))
        self.assertEqual(mod.EXIT_OK, code)
        self.assertIn("availability 5-primaries/3-sectors shape validated", output)
        self.assertEqual([], transport.calls)

    def test_gate_availability_type_regressions_rejected(self):
        def mutate_nan_sigma(payload):
            payload["gates"]["d_v1_2"]["availability"]["primaries"][0]["sigma_fit"] = float("nan")

        def mutate_missing_truth_z(payload):
            del payload["gates"]["d_v1_2"]["availability"]["primaries"][0]["truth_z"]

        def mutate_wrong_primary_role(payload):
            payload["gates"]["d_v1_2"]["availability"]["primaries"][0]["role"] = "alternate"

        def mutate_bool_n2(payload):
            payload["gates"]["d_v1_2"]["availability"]["primaries"][0]["n2"] = True

        def mutate_string_eligible(payload):
            payload["gates"]["d_v1_2"]["availability"]["eligible_count"] = "41464"

        def mutate_inconsistent_sectors(payload):
            payload["gates"]["d_v1_2"]["availability"]["sectors_covered"] = ["s00", "s01", "s12", "s22"]

        def mutate_int_preliminary(payload):
            payload["gates"]["d_v1_2"]["availability"]["stage_1_preliminary"] = 1

        def mutate_three_alternates(payload):
            extra = dict(payload["gates"]["d_v1_2"]["availability"]["alternates"][0])
            payload["gates"]["d_v1_2"]["availability"]["alternates"].append(extra)

        def mutate_alternate_primary_role(payload):
            payload["gates"]["d_v1_2"]["availability"]["alternates"][0]["role"] = "primary"

        def mutate_bool_shortfall(payload):
            payload["gates"]["d_v1_2"]["availability"]["alternates_shortfall"] = False

        cases = (
            ("bool-n2", mutate_bool_n2, "non-integral n2"),
            ("string-eligible-count", mutate_string_eligible, "not an integer >= 5"),
            ("nan-sigma-fit", mutate_nan_sigma, "strictly valid JSON"),
            ("sectors-inconsistent", mutate_inconsistent_sectors, "disagrees with"),
            ("preliminary-int-1", mutate_int_preliminary, "not exactly true"),
            ("wrong-primary-role", mutate_wrong_primary_role, "expected 'primary'"),
            ("missing-truth-z", mutate_missing_truth_z, "non-numeric truth_z"),
            ("three-alternates", mutate_three_alternates, "beyond the 2-slot fallback ladder"),
            ("alternate-primary-role", mutate_alternate_primary_role, "expected 'alternate'"),
            ("bool-shortfall", mutate_bool_shortfall, "alternates_shortfall"),
        )
        for label, mutate, fragment in cases:
            with self.subTest(case=label):
                payload = v12_report()
                mutate(payload)
                world = self.setup_world(report_payload=payload)
                code, output = self.run_main(*self.preflight_argv(world))
                self.assertEqual(mod.EXIT_GUARD, code)
                self.assertIn(fragment, output)


class PinTests(Harness):
    def test_remaining_total_arithmetic(self):
        self.assertEqual(701_880_508, mod.REMAINING_TOTAL)
        self.assertEqual(830_532_133, mod.APPROVED_SIX_TOTAL)
        self.assertEqual(128_651_625, mod.FIRST_TILE_SIZE)
        self.assertEqual(mod.APPROVED_SIX_TOTAL - mod.FIRST_TILE_SIZE, mod.REMAINING_TOTAL)
        self.assertEqual(sum(mod.PINS.values()), mod.REMAINING_TOTAL)

    def test_exactly_five_pins_committed_order_verbatim(self):
        self.assertEqual(5, len(mod.PINS))
        self.assertIs(mod.probe.OTHER_TILES, mod.PINS)
        self.assertEqual(list(mod.probe.OTHER_TILES), list(mod.PINS))
        for name, size in mod.PINS.items():
            self.assertEqual(mod.probe.OTHER_TILES[name], size)
        tiles = mod.stage2_tiles()
        self.assertEqual([probe_mod.BASE_URL + name for name in mod.PINS],
                         [tile.url for tile in tiles.values()])
        self.assertNotIn(mod.FIRST_TILE_NAME, mod.PINS)

    def test_first_tile_name_refused_explicitly(self):
        world = self.setup_world()
        drifted = dict(mod.PINS)
        drifted[mod.FIRST_TILE_NAME] = mod.FIRST_TILE_SIZE
        self.patch_mod("PINS", drifted)
        self.patch_mod("REMAINING_TOTAL", sum(drifted.values()))
        self.patch_mod("APPROVED_SIX_TOTAL", sum(drifted.values()))
        transport = self.fetch_never()
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_GUARD, code)
        self.assertIn("never fetched by stage 2", output)
        self.assertEqual([], transport.calls)
        self.assertFalse(world["cache"].exists())

    def test_pin_constants_positive(self):
        for name, size in mod.PINS.items():
            self.assertIsInstance(size, int)
            self.assertGreater(size, 0, name)
        self.assertEqual(2_147_483_648, mod.RESERVE)
        self.assertEqual(2_052_660_777 + 1_073_741_824, mod.OTHER_KNOWN_CONSUMER_BUDGET)
        self.assertEqual(4_294_967_296, mod.MIN_FREE_ABSOLUTE)
        self.assertGreater(mod.RESERVE, 0)
        self.assertGreater(mod.OTHER_KNOWN_CONSUMER_BUDGET, 0)

    def test_exit_codes_mirror_acquire(self):
        self.assertEqual(0, mod.EXIT_OK)
        self.assertEqual(2, mod.EXIT_GUARD)
        self.assertEqual(3, mod.EXIT_NETWORK)
        self.assertEqual(4, mod.EXIT_STATE)
        self.assertIs(mod.acquire.Failure, mod.Failure)
        self.assertIs(mod.probe.Failure, mod.Failure)
        self.assertIs(mod.probe.acquire, mod.acquire)

    def test_missing_fetch_flag_guards_before_anything(self):
        world = self.setup_world()
        calls = self.instrument_makedirs()
        transport = self.fetch_never()
        args = SimpleNamespace(fetch_remaining=False, cache_root=str(world["cache"]))
        with self.assertRaises(mod.Failure) as caught:
            mod.run_fetch_remaining(args)
        self.assertEqual(mod.EXIT_GUARD, caught.exception.code)
        self.assertIn("--fetch-remaining", caught.exception.message)
        self.assertEqual([], calls)
        self.assertEqual([], transport.calls)
        self.assertFalse(world["cache"].exists())


class GuardTests(Harness):
    TINY = {name: size for name, size in zip(mod.PINS, (32, 24, 48, 40, 56))}

    def test_gates_pass_before_any_mkdir_or_network(self):
        cases = (
            ("missing-report", lambda w: self.fetch_argv(w, report=w["root"] / "absent.json")),
            ("missing-evidence", lambda w: ["--fetch-remaining",
                                            "--archive-evidence", str(w["root"] / "absent.txt"),
                                            "--report", str(w["report"]),
                                            "--cache-root", str(w["cache"])]),
        )
        for label, argv_of in cases:
            with self.subTest(case=label):
                world = self.setup_world()
                calls = self.instrument_makedirs()
                transport = self.fetch_never()
                code, output = self.run_main(*argv_of(world))
                self.assertEqual(mod.EXIT_GUARD, code)
                self.assertEqual([], calls)
                self.assertEqual([], transport.calls)
                self.assertFalse(world["cache"].exists())

    def test_budget_failure_before_any_mkdir_or_network(self):
        world = self.setup_world()
        tiles = self.patch_pins(self.TINY)
        required = mod.REMAINING_TOTAL + mod.RESERVE + mod.OTHER_KNOWN_CONSUMER_BUDGET
        self.patch_statvfs(required - 1)
        calls = self.instrument_makedirs()
        transport = self.fetch_never()
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_GUARD, code)
        self.assertIn("insufficient free space", output)
        self.assertEqual([], calls)
        self.assertEqual([], transport.calls)
        self.assertFalse(world["cache"].exists())

    def test_successful_fetch_mkdirs_only_under_stage2(self):
        world = self.setup_world()
        tiles = self.patch_pins(dict(list(self.TINY.items())[:1]))
        data = {name: blob(tile.size, seed=index) for index, (name, tile) in enumerate(tiles.items())}

        def handler(record):
            tile = self.tile_for(record, tiles)
            if record["method"] == "HEAD":
                return FakeResponse(200, message({"Content-Length": tile.size}))
            return FakeResponse(200, message({"Content-Length": tile.size}), data[tile.name])

        self.fake_transport(handler)
        calls = self.instrument_makedirs()
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_OK, code)
        self.assertTrue(calls)
        # os.makedirs recurses through the patched recorder, so parent intermediates
        # are recorded too; the first recorded intent must be the stage-2 state dir.
        self.assertIn("stage2", calls[0])

    def test_source_pin_mismatch_exit_4_no_fetch(self):
        world = self.setup_world()
        self.patch_pins(dict(list(self.TINY.items())[:1]))
        sources = world["root"] / "sources"
        for rel in mod.SOURCE_PINS:
            target = sources / rel
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(Path(mod.SOURCES_DIR) / rel, target)
        (sources / "sedan_lidar_probe.py").write_text("# drifted content\n")
        transport = self.fetch_never()
        code, output = self.run_main(*self.fetch_argv(world) + ["--sources-dir", str(sources)])
        self.assertEqual(mod.EXIT_STATE, code)
        self.assertIn("accepted source drifted", output)
        self.assertIn("nothing fetched", output)
        self.assertEqual([], transport.calls)
        self.assertFalse((world["cache"] / "lidarhd" / "stage2").exists())

    def test_mount_realness_refuse(self):
        world = self.setup_world()
        self.patch_pins(self.TINY)
        mounts = world["root"] / "mounts-ro.txt"
        mounts.write_text(f"/dev/sdz {world['prefix']} ext4 ro,relatime 0 0\n")
        self.patch_acquire("PROC_MOUNTS_PATH", str(mounts))
        transport = self.fetch_never()
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_GUARD, code)
        self.assertIn("rw mount", output)
        self.assertEqual([], transport.calls)
        self.assertFalse((world["cache"] / "lidarhd" / "stage2").exists())

    def test_symlink_components_refused(self):
        world = self.setup_world()
        self.patch_pins(self.TINY)
        outside = world["root"] / "outside"
        outside.mkdir()
        stage2 = world["cache"] / "lidarhd" / "stage2"
        stage2.parent.mkdir(parents=True)
        stage2.symlink_to(outside)
        transport = self.fetch_never()
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_GUARD, code)
        self.assertIn("symlinked component", output)
        self.assertEqual([], transport.calls)
        self.assertEqual([], list(outside.iterdir()))

    def test_cache_root_symlink_refused(self):
        world = self.setup_world()
        real = world["root"] / "real-cache"
        real.mkdir()
        link = world["root"] / "cache-link"
        link.symlink_to(real)
        transport = self.fetch_never()
        code, output = self.run_main(*self.fetch_argv(world, cache_root=link))
        self.assertEqual(mod.EXIT_GUARD, code)
        self.assertIn("symlink", output)
        self.assertEqual([], transport.calls)
        self.assertEqual([], list(real.iterdir()))

    def test_operator_collision_untouched(self):
        world = self.setup_world()
        tiles = self.patch_pins(dict(list(self.TINY.items())[:1]))
        name = next(iter(tiles))
        operator_bytes = b"OPERATOR INPUT DATA"
        path = self.tile_path(world, name)
        path.parent.mkdir(parents=True)
        path.write_bytes(operator_bytes)
        transport = self.fetch_never()
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_STATE, code)
        self.assertIn("collision", output)
        self.assertEqual(operator_bytes, path.read_bytes())
        self.assertEqual([], transport.calls)
        self.assertFalse(Path(self.stage2_paths(world)["state"]).exists())
        self.assertFalse(Path(self.stage2_paths(world)["lock"]).exists())

    def test_rogue_file_in_tiles_dir_refused(self):
        world = self.setup_world()
        tiles = self.patch_pins(dict(list(self.TINY.items())[:2]))
        self.complete_everything(world, tiles)
        rogue = self.tile_path(world, next(iter(tiles))).parent / "OPERATOR_ROGUE_TILE.copc.laz"
        rogue.write_bytes(b"rogue bytes")
        transport = self.fetch_never()
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_STATE, code)
        self.assertIn("OPERATOR_ROGUE_TILE.copc.laz", output)
        self.assertIn("unexpected entry", output)
        self.assertEqual(b"rogue bytes", rogue.read_bytes())
        self.assertEqual([], transport.calls)
        self.assertFalse(Path(self.stage2_paths(world)["lock"]).exists())

    def test_rogue_directory_and_symlink_entries_refused(self):
        # The dir shape carries an ALLOWED pin name (no completed file for it here) so
        # the regular-file check is what refuses; symlink shapes carry unexpected names
        # (allowed-name symlinks are refused earlier, exit 2, by reject_symlinks).
        for shape, expected_fragment in (
            ("dir", "not a regular file"),
            ("symlink-file", "unexpected entry"),
            ("symlink-dir", "unexpected entry"),
        ):
            with self.subTest(shape=shape):
                world = self.setup_world()
                tiles = self.patch_pins(dict(list(self.TINY.items())[:2]))
                first_name = list(tiles)[0]
                second_name = list(tiles)[1]
                first = tiles[first_name]
                data = blob(first.size, seed=0)
                first_path = self.tile_path(world, first_name)
                first_path.parent.mkdir(parents=True, exist_ok=True)
                first_path.write_bytes(data)
                self.write_stage2_state(world, {first_name: {
                    "url": first.url,
                    "expected_size": first.size,
                    "bytes_on_disk": first.size,
                    "status": "complete",
                    "mtime_ns": os.lstat(first_path).st_mtime_ns,
                    "sha256": hashlib.sha256(data).hexdigest(),
                }})
                tiles_dir = first_path.parent
                target = world["root"] / "rogue-target"
                target.parent.mkdir(parents=True, exist_ok=True)
                entry_name = second_name if shape == "dir" else f"ROGUE_{shape.upper()}"
                if shape == "dir":
                    entry = tiles_dir / entry_name
                    entry.mkdir()
                elif shape == "symlink-file":
                    target.write_bytes(b"x")
                    entry = tiles_dir / entry_name
                    entry.symlink_to(target)
                else:
                    target.mkdir()
                    entry = tiles_dir / entry_name
                    entry.symlink_to(target)
                transport = self.fetch_never()
                code, output = self.run_main(*self.fetch_argv(world))
                self.assertEqual(mod.EXIT_STATE, code)
                self.assertIn(entry_name, output)
                self.assertIn(expected_fragment, output)
                self.assertTrue(entry.exists(follow_symlinks=False))
                self.assertEqual([], transport.calls)
                self.assertFalse(Path(self.stage2_paths(world)["lock"]).exists())

    def test_inventory_allows_completed_five_and_fresh_empty(self):
        completed = self.setup_world()
        tiles = self.patch_pins(self.TINY)
        self.complete_everything(completed, tiles)
        transport = self.fetch_never()
        code, output = self.run_main(*self.fetch_argv(completed))
        self.assertEqual(mod.EXIT_OK, code)
        self.assertIn("tiles dir inventory ok: 5 scanned", output)
        self.assertEqual([], transport.calls)

        fresh = self.setup_world()
        fresh_tiles = self.patch_pins(dict(list(self.TINY.items())[:1]))
        data = {name: blob(tile.size, seed=0) for name, tile in fresh_tiles.items()}

        def handler(record):
            tile = self.tile_for(record, fresh_tiles)
            if record["method"] == "HEAD":
                return FakeResponse(200, message({"Content-Length": tile.size}))
            return FakeResponse(200, message({"Content-Length": tile.size}), data[tile.name])

        self.fake_transport(handler)
        code, output = self.run_main(*self.fetch_argv(fresh))
        self.assertEqual(mod.EXIT_OK, code)
        self.assertIn("tiles dir absent, fresh start", output)

    def test_state_known_partial_name_allowed(self):
        world = self.setup_world()
        tiles = self.patch_pins({name: 64 for name in list(mod.PINS)[:1]})
        name, tile = next(iter(tiles.items()))
        full = blob(64)
        prefix = full[:40]
        self.partial_entry(self.tile_path(world, name), tile, prefix)
        self.write_stage2_state(world, {name: {
            "url": tile.url,
            "expected_size": tile.size,
            "bytes_on_disk": len(prefix),
            "status": "partial",
            "mtime_ns": os.lstat(self.tile_path(world, name)).st_mtime_ns,
        }})

        def handler(record):
            if record["method"] == "HEAD":
                return FakeResponse(200, message({"Content-Length": tile.size}))
            return FakeResponse(206, message({"Content-Range": f"bytes 40-63/{tile.size}"}), full[40:])

        transport = self.fake_transport(handler)
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_OK, code)
        self.assertIn("tiles dir inventory ok: 1 scanned", output)
        self.assertEqual(["HEAD", "GET"], [call["method"] for call in transport.calls])

    def test_corrupt_state_nothing_adopted(self):
        world = self.setup_world()
        tiles = self.patch_pins(dict(list(self.TINY.items())[:1]))
        name = next(iter(tiles))
        paths = self.stage2_paths(world)
        Path(paths["state"]).parent.mkdir(parents=True)
        Path(paths["state"]).write_text("{not json")
        transport = self.fetch_never()
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_STATE, code)
        self.assertIn("corrupted stage-2 state", output)
        self.assertEqual([], transport.calls)
        self.assertFalse(self.tile_path(world, name).exists())
        self.assertFalse(Path(paths["lock"]).exists())

    def test_state_version_mismatch_exit_4(self):
        world = self.setup_world()
        tiles = self.patch_pins(dict(list(self.TINY.items())[:1]))
        name = next(iter(tiles))
        self.write_stage2_state(world, {})
        paths = self.stage2_paths(world)
        Path(paths["state"]).write_text(json.dumps({"version": 99, "tiles": {}}))
        transport = self.fetch_never()
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_STATE, code)
        self.assertIn("unsupported or missing version", output)
        self.assertEqual([], transport.calls)
        self.assertFalse(self.tile_path(world, name).exists())
        self.assertFalse(Path(paths["lock"]).exists())

    def test_oversize_state_rejected(self):
        world = self.setup_world()
        self.patch_pins(dict(list(self.TINY.items())[:1]))
        paths = self.stage2_paths(world)
        Path(paths["state"]).parent.mkdir(parents=True)
        Path(paths["state"]).write_text(json.dumps({"version": 1, "tiles": {}, "pad": "x" * 70_000}))
        transport = self.fetch_never()
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_STATE, code)
        self.assertIn("byte bound", output)
        self.assertEqual([], transport.calls)

    def test_bounded_head_response(self):
        world = self.setup_world()
        tiles = self.patch_pins(dict(list(self.TINY.items())[:1]))
        tile = next(iter(tiles.values()))

        def handler(record):
            self.assertEqual("HEAD", record["method"])
            return TrapBodyResponse(200, message({"Content-Length": tile.size + 1}))

        self.fake_transport(handler)
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_STATE, code)
        self.assertIn("drift", output)

    def test_budget_arithmetic_includes_other_consumer_term(self):
        world = self.setup_world()
        self.patch_pins(self.TINY)
        required = mod.REMAINING_TOTAL + mod.RESERVE + mod.OTHER_KNOWN_CONSUMER_BUDGET
        self.patch_statvfs(required - 1)
        transport = self.fetch_never()
        code, output = self.run_main(*self.preflight_argv(world))
        self.assertEqual(mod.EXIT_GUARD, code)
        self.assertIn("insufficient free space", output)
        self.assertIn(f"required={required}", output)
        self.assertIn(f"remaining pins {mod.REMAINING_TOTAL}", output)
        self.assertIn(f"other known consumer budget {mod.OTHER_KNOWN_CONSUMER_BUDGET}", output)
        self.assertIn(f"floor {mod.MIN_FREE_ABSOLUTE}", output)
        self.assertEqual([], transport.calls)

    def test_stale_lock_exit_2_manual_removal(self):
        world = self.setup_world()
        tiles = self.patch_pins(dict(list(self.TINY.items())[:1]))
        finished = subprocess.Popen(["true"])
        finished.wait()
        paths = self.stage2_paths(world)
        Path(paths["lock"]).parent.mkdir(parents=True)
        Path(paths["lock"]).write_text(f"{finished.pid} 2026-11-03T18:00:00.000-05:00\n")
        transport = self.fetch_never()
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_GUARD, code)
        self.assertIn("stale stage-2 lock", output)
        self.assertIn("remove it manually", output)
        self.assertTrue(Path(paths["lock"]).exists())
        self.assertEqual([], transport.calls)

    def test_live_lock_exit_2(self):
        world = self.setup_world()
        self.patch_pins(dict(list(self.TINY.items())[:1]))
        paths = self.stage2_paths(world)
        Path(paths["lock"]).parent.mkdir(parents=True)
        Path(paths["lock"]).write_text(f"{os.getpid()} 2026-11-03T18:00:00.000-05:00\n")
        transport = self.fetch_never()
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_GUARD, code)
        self.assertIn("live pid", output)
        self.assertTrue(Path(paths["lock"]).exists())
        self.assertEqual([], transport.calls)

    def test_status_read_only(self):
        world = self.setup_world()
        tiles = self.patch_pins(dict(list(self.TINY.items())[:2]))
        entries = {}
        for index, (name, tile) in enumerate(tiles.items()):
            entries[name] = self.partial_entry(self.tile_path(world, name), tile, blob(8, seed=index))
        self.write_stage2_state(world, entries)
        calls = self.instrument_makedirs()
        transport = self.fetch_never()
        argv = ["--status", "--cache-root", str(world["cache"])]
        code, output = self.run_main(*argv)
        self.assertEqual(mod.EXIT_OK, code)
        self.assertIn("status complete", output)
        self.assertIn("status=partial", output)
        self.assertEqual([], calls)
        self.assertEqual([], transport.calls)


class TransportTests(Harness):
    TINY = {name: size for name, size in zip(mod.PINS, (32, 24, 48, 40, 56))}

    def test_sequential_fetch_completes_all_five_in_committed_order(self):
        world = self.setup_world()
        tiles = self.patch_pins(self.TINY)
        data = {name: blob(tile.size, seed=index) for index, (name, tile) in enumerate(tiles.items())}

        def handler(record):
            tile = self.tile_for(record, tiles)
            if record["method"] == "HEAD":
                return FakeResponse(200, message({"Content-Length": tile.size}))
            return FakeResponse(200, message({"Content-Length": tile.size}), data[tile.name])

        transport = self.fake_transport(handler)
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_OK, code)
        expected_methods = ["HEAD", "GET"] * len(tiles)
        self.assertEqual(expected_methods, [call["method"] for call in transport.calls])
        expected_urls = [mod.probe.BASE_URL + name for name in tiles]
        head_urls = [call["url"] for call in transport.calls if call["method"] == "HEAD"]
        self.assertEqual(expected_urls, head_urls)
        for name, tile in tiles.items():
            path = self.tile_path(world, name)
            self.assertEqual(data[name], path.read_bytes())
        state = json.loads(Path(self.stage2_paths(world)["state"]).read_text())
        for name, tile in tiles.items():
            entry = state["tiles"][name]
            self.assertEqual("complete", entry["status"])
            self.assertEqual(tile.size, entry["bytes_on_disk"])
            self.assertEqual(hashlib.sha256(data[name]).hexdigest(), entry["sha256"])
        self.assertIn("six-tile total", output)
        self.assertFalse(Path(self.stage2_paths(world)["lock"]).exists())

    def test_resume_206_appends_completes_seeded_hash(self):
        world = self.setup_world()
        tiles = self.patch_pins({name: 64 for name in list(mod.PINS)[:1]})
        name, tile = next(iter(tiles.items()))
        full = blob(64)
        prefix = full[:40]
        self.partial_entry(self.tile_path(world, name), tile, prefix)
        self.write_stage2_state(world, {name: {
            "url": tile.url,
            "expected_size": tile.size,
            "bytes_on_disk": len(prefix),
            "status": "partial",
            "mtime_ns": os.lstat(self.tile_path(world, name)).st_mtime_ns,
        }})

        def handler(record):
            if record["method"] == "HEAD":
                return FakeResponse(200, message({"Content-Length": tile.size}))
            self.assertEqual({"Range": "bytes=40-"}, record["headers"])
            return FakeResponse(206, message({"Content-Range": f"bytes 40-63/{tile.size}"}), full[40:])

        transport = self.fake_transport(handler)
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_OK, code)
        self.assertEqual(["HEAD", "GET"], [call["method"] for call in transport.calls])
        self.assertEqual(full, self.tile_path(world, name).read_bytes())
        state = json.loads(Path(self.stage2_paths(world)["state"]).read_text())
        entry = state["tiles"][name]
        self.assertEqual("complete", entry["status"])
        self.assertEqual(hashlib.sha256(full).hexdigest(), entry["sha256"])

    def test_200_on_resume_refused_exit_3(self):
        world = self.setup_world()
        tiles = self.patch_pins({name: 24 for name in list(mod.PINS)[:1]})
        name, tile = next(iter(tiles.items()))
        prefix = blob(10)
        self.partial_entry(self.tile_path(world, name), tile, prefix)
        self.write_stage2_state(world, {name: {
            "url": tile.url,
            "expected_size": tile.size,
            "bytes_on_disk": len(prefix),
            "status": "partial",
            "mtime_ns": os.lstat(self.tile_path(world, name)).st_mtime_ns,
        }})

        def handler(record):
            if record["method"] == "HEAD":
                return FakeResponse(200, message({"Content-Length": tile.size}))
            return FakeResponse(200, message({"Content-Length": tile.size}), blob(24))

        transport = self.fake_transport(handler)
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_NETWORK, code)
        self.assertIn("refusing to append", output)
        self.assertEqual(prefix, self.tile_path(world, name).read_bytes())
        state = json.loads(Path(self.stage2_paths(world)["state"]).read_text())
        entry = state["tiles"][name]
        self.assertEqual("partial", entry["status"])
        self.assertEqual(len(prefix), entry["bytes_on_disk"])

    def test_http_401_and_403_fail_no_account(self):
        for status in (401, 403):
            with self.subTest(status=status):
                world = self.setup_world()
                tiles = self.patch_pins(dict(list(self.TINY.items())[:1]))
                tile = next(iter(tiles.values()))

                def handler(record, status=status):
                    if record["method"] == "HEAD":
                        return FakeResponse(200, message({"Content-Length": tile.size}))
                    return FakeResponse(status, message({"Content-Length": tile.size}))

                transport = self.fake_transport(handler)
                code, output = self.run_main(*self.fetch_argv(world))
                self.assertEqual(mod.EXIT_NETWORK, code)
                self.assertIn("no IGN account", output)
                self.assertEqual(["HEAD", "GET"], [call["method"] for call in transport.calls])
                for call in transport.calls:
                    self.assertNotIn("Authorization", call["headers"])

    def test_head_401_fails_closed(self):
        world = self.setup_world()
        self.patch_pins(dict(list(self.TINY.items())[:1]))

        def handler(record):
            return FakeResponse(401, message({"Content-Length": 64}))

        transport = self.fake_transport(handler)
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_NETWORK, code)
        self.assertIn("no IGN account", output)
        self.assertEqual(["HEAD"], [call["method"] for call in transport.calls])

    def test_short_read_exit_3_partial_preserved(self):
        world = self.setup_world()
        tiles = self.patch_pins({name: 24 for name in list(mod.PINS)[:1]})
        name, tile = next(iter(tiles.items()))
        full = blob(24)
        prefix = full[:10]
        served = full[10:15]
        self.partial_entry(self.tile_path(world, name), tile, prefix)
        self.write_stage2_state(world, {name: {
            "url": tile.url,
            "expected_size": tile.size,
            "bytes_on_disk": len(prefix),
            "status": "partial",
            "mtime_ns": os.lstat(self.tile_path(world, name)).st_mtime_ns,
        }})

        def handler(record):
            if record["method"] == "HEAD":
                return FakeResponse(200, message({"Content-Length": tile.size}))
            return FakeResponse(206, message({"Content-Range": f"bytes 10-23/{tile.size}"}), served)

        transport = self.fake_transport(handler)
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_NETWORK, code)
        self.assertIn("short read", output)
        path = self.tile_path(world, name)
        self.assertEqual(prefix + served, path.read_bytes())
        state = json.loads(Path(self.stage2_paths(world)["state"]).read_text())
        entry = state["tiles"][name]
        self.assertEqual("partial", entry["status"])
        self.assertEqual(len(prefix + served), entry["bytes_on_disk"])
        self.assertEqual(entry["mtime_ns"], os.lstat(path).st_mtime_ns)
        self.assertEqual(["HEAD", "GET"], [call["method"] for call in transport.calls])

    def test_overlong_body_exit_4_capped_at_pin(self):
        world = self.setup_world()
        tiles = self.patch_pins({name: 24 for name in list(mod.PINS)[:1]})
        name, tile = next(iter(tiles.items()))

        def handler(record):
            if record["method"] == "HEAD":
                return FakeResponse(200, message({"Content-Length": tile.size}))
            return FakeResponse(200, message({"Content-Length": tile.size}), blob(40))

        self.fake_transport(handler)
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_STATE, code)
        self.assertIn("drift", output)
        self.assertIn("stopped at the cap", output)
        path = self.tile_path(world, name)
        self.assertEqual(tile.size, path.stat().st_size)
        state = json.loads(Path(self.stage2_paths(world)["state"]).read_text())
        self.assertEqual("partial", state["tiles"][name]["status"])

    def test_midstream_death_exit_3_single_get(self):
        world = self.setup_world()
        tiles = self.patch_pins({name: 64 for name in list(mod.PINS)[:1]})
        name, tile = next(iter(tiles.items()))

        def handler(record):
            if record["method"] == "HEAD":
                return FakeResponse(200, message({"Content-Length": tile.size}))
            return DyingResponse(200, message({"Content-Length": tile.size}), blob(10))

        transport = self.fake_transport(handler)
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_NETWORK, code)
        self.assertIn("network error", output)
        get_calls = [call for call in transport.calls if call["method"] == "GET"]
        self.assertEqual(1, len(get_calls))
        path = self.tile_path(world, name)
        self.assertEqual(blob(10), path.read_bytes())
        state = json.loads(Path(self.stage2_paths(world)["state"]).read_text())
        entry = state["tiles"][name]
        self.assertEqual("partial", entry["status"])
        self.assertEqual(10, entry["bytes_on_disk"])

    def test_interruption_between_files_preserves_completed(self):
        world = self.setup_world()
        pins = dict(list(self.TINY.items())[:3])
        tiles = self.patch_pins(pins)
        names = list(tiles)
        first_data = blob(tiles[names[0]].size, seed=0)

        def handler(record):
            tile = self.tile_for(record, tiles)
            if tile.name == names[0]:
                if record["method"] == "HEAD":
                    return FakeResponse(200, message({"Content-Length": tile.size}))
                return FakeResponse(200, message({"Content-Length": tile.size}), first_data)
            if tile.name == names[1]:
                if record["method"] == "HEAD":
                    return FakeResponse(200, message({"Content-Length": tile.size}))
                return DyingResponse(200, message({"Content-Length": tile.size}), blob(6, seed=1))
            raise AssertionError(f"tile {tile.name} must never be attempted after a failure")

        transport = self.fake_transport(handler)
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_NETWORK, code)
        self.assertEqual(["HEAD", "GET", "HEAD", "GET"],
                         [call["method"] for call in transport.calls])
        self.assertEqual(first_data, self.tile_path(world, names[0]).read_bytes())
        state = json.loads(Path(self.stage2_paths(world)["state"]).read_text())
        self.assertEqual("complete", state["tiles"][names[0]]["status"])
        self.assertEqual("partial", state["tiles"][names[1]]["status"])
        self.assertNotIn(names[2], state["tiles"])
        self.assertFalse(self.tile_path(world, names[2]).exists())

    def test_idempotent_second_invocation_zero_network(self):
        world = self.setup_world()
        tiles = self.patch_pins(self.TINY)
        data = {name: blob(tile.size, seed=index) for index, (name, tile) in enumerate(tiles.items())}

        def handler(record):
            tile = self.tile_for(record, tiles)
            if record["method"] == "HEAD":
                return FakeResponse(200, message({"Content-Length": tile.size}))
            return FakeResponse(200, message({"Content-Length": tile.size}), data[tile.name])

        self.fake_transport(handler)
        code, _ = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_OK, code)
        before = {name: os.stat(self.tile_path(world, name)).st_mtime_ns for name in tiles}
        transport = self.fetch_never()
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_OK, code)
        self.assertEqual([], transport.calls)
        self.assertIn("no redownload", output)
        for name in tiles:
            self.assertEqual(before[name], os.stat(self.tile_path(world, name)).st_mtime_ns)

    def test_all_complete_summary_zero_network(self):
        world = self.setup_world()
        tiles = self.patch_pins(self.TINY)
        self.complete_everything(world, tiles)
        transport = self.fetch_never()
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_OK, code)
        self.assertEqual([], transport.calls)
        self.assertIn("metadata hash summary", output)
        for name, tile in tiles.items():
            digest = hashlib.sha256(blob(tile.size, seed=list(tiles).index(name))).hexdigest()
            self.assertIn(f"{tile.size} bytes sha256 {digest}", output)
        self.assertIn(f"six-tile total: {mod.APPROVED_SIX_TOTAL} bytes == pinned {mod.APPROVED_SIX_TOTAL}", output)
        self.assertIn("not publisher-authenticated", output)

    def test_head_edition_drift_exit_4(self):
        world = self.setup_world()
        tiles = self.patch_pins(dict(list(self.TINY.items())[:1]))
        tile = next(iter(tiles.values()))

        def handler(record):
            self.assertEqual("HEAD", record["method"])
            return FakeResponse(200, message({"Content-Length": tile.size + 1}))

        transport = self.fake_transport(handler)
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_STATE, code)
        self.assertIn("drift", output)
        self.assertIn("never upgrades", output)
        self.assertEqual(["HEAD"], [call["method"] for call in transport.calls])

    def test_summary_first_tile_verified_or_refused(self):
        first_data = blob(32)
        self.patch_mod("FIRST_TILE_SIZE", 32)
        self.patch_mod("FIRST_TILE_SHA256", hashlib.sha256(first_data).hexdigest())
        world = self.setup_world()
        tiles = self.patch_pins(dict(list(self.TINY.items())[:1]))
        self.patch_mod("APPROVED_SIX_TOTAL", sum(tile.size for tile in tiles.values()) + 32)
        self.complete_everything(world, tiles)
        first_path = Path(self.stage2_paths(world)["first_tile"])
        first_path.parent.mkdir(parents=True, exist_ok=True)

        first_path.write_bytes(first_data)
        transport = self.fetch_never()
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_OK, code)
        self.assertIn("read-only recheck", output)
        self.assertEqual([], transport.calls)

        first_path.write_bytes(blob(32, seed=9))
        transport = self.fetch_never()
        code, output = self.run_main(*self.fetch_argv(world))
        self.assertEqual(mod.EXIT_STATE, code)
        self.assertIn("first tile failed sha256", output)
        self.assertEqual([], transport.calls)


if __name__ == "__main__":
    unittest.main()
