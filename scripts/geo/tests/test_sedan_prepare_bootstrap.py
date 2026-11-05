"""Offline tests for scripts/geo/sedan_prepare_bootstrap.py.

Synthetic fixture trees under /tmp/opencode/sedan-bootstrap-tests, a patched http_get seam for
the WFS body, a fake RUN_CHILD that records each helper invocation and applies the documented
on-disk effect, and patched /proc/mounts and statvfs seams. No network, no install, no real
venv creation: the real http_get and the real bounded runner are never exercised by the
bootstrap paths (one focused case drives the real runner against a tiny shell stub). All
fixture values are synthetic; nothing is copied from the recorded execution lineage.
"""

import contextlib
import importlib.util
import io
import json
import os
import shutil
import sys
import tempfile
import unittest
import urllib.error
from types import SimpleNamespace
from unittest import mock

sys.dont_write_bytecode = True

GEO_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TMP_BASE = "/tmp/opencode/sedan-bootstrap-tests"


def load_module(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(GEO_DIR, filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


B = load_module("sedan_prepare_bootstrap", "sedan_prepare_bootstrap.py")
ChildResult = B.ChildResult

TLS_STDERR = "pip download error: [SSL: UNEXPECTED_EOF_WHILE_READING] EOF occurred in violation of protocol"
TIMEOUT_STDERR = "Read timed out. (read timeout=60)"
RESET_STDERR = "URLError: <urlopen error [Errno 104] Connection reset by peer>"
AUTH_STDERR = "HTTP 401 Unauthorized from https://data.geopf.example/x: authorization required"

WHEEL_NAMES = (
    "laspy-2.7.0-py3-none-any.whl",
    "lazrs-0.8.2-cp314-cp314-manylinux_2_17_x86_64.manylinux2014_x86_64.whl",
    "numpy-2.5.4-cp314-cp314-manylinux_2_27_x86_64.manylinux_2_28_x86_64.whl",
)

OTHER_TILES = ("0839-6958", "0839-6959", "0840-6959", "0841-6958", "0841-6959", "0842-6958", "0842-6959")


def tile_props(tile, points=1_000_000):
    stem = "LHD_FXX_{}_PTS_LAMB93_IGN69".format(tile.replace("-", "_"))
    return {
        "capteur": '["SYNTH-SENSOR-A:000000"]',
        "code_mission": "24SYNTH",
        "coordonnees_nw": tile,
        "date_debut_acquisition": "2020-01-02Z",
        "date_edition": "2025-06-07Z",
        "date_fin_acquisition": "2020-01-02Z",
        "moa_acquisition": "SYNTH-MOA",
        "moe_acquisition": "SYNTH-MOE",
        "moe_classement_automatique": "SYNTH-AUTO",
        "moe_classement_manuel": "",
        "nombre_points": points,
        "procede_classement": "SYNTH_AUTO_V1",
        "systeme_altimetrique": "SYNTH69",
        "systeme_planimetrique": "SYNTH93",
        "url_mnh": f"https://synthetic.example/wms-r/{stem}_MNH_0M50.tif?REQUEST=GetMap&LAYERS=SYNTH_MNH",
        "url_mns": f"https://synthetic.example/wms-r/{stem}_MNS_0M50.tif?REQUEST=GetMap&LAYERS=SYNTH_MNS",
        "url_mnt": f"https://synthetic.example/wms-r/{stem}_MNT_0M50.tif?REQUEST=GetMap&LAYERS=SYNTH_MNT",
        "url_npl": f"https://synthetic.example/download/SYNTH-LOT/{stem}.copc.laz",
    }


def feature(props, index=0):
    return {
        "type": "Feature",
        "id": f"metadata.{index}",
        "geometry": {"type": "Polygon", "coordinates": [[[1.0, 2.0], [3.0, 4.0], [5.0, 6.0]]]},
        "properties": props,
    }


def collection_bytes(props_list):
    document = {
        "type": "FeatureCollection",
        "totalFeatures": len(props_list),
        "numberMatched": len(props_list),
        "numberReturned": len(props_list),
        "timeStamp": "2026-01-01T00:00:00Z",
        "crs": {"type": "name", "properties": {"name": "urn:ogc:def:crs:EPSG::4326"}},
        "features": [feature(props, index) for index, props in enumerate(props_list)],
    }
    return json.dumps(document).encode("utf-8")


def unrelated_tiles(count):
    return [f"{index:04d}-7000" for index in range(1, count + 1)]


def default_collection():
    props = [tile_props(B.TILE_COORDS)] + [tile_props(tile) for tile in OTHER_TILES]
    return collection_bytes(props)


def lock_text(entries=None):
    pinned = entries or B.PINNED_PACKAGES
    return ("# lidar-tools-lock.txt\n"
            "# Plan id: lidar-firsttile-runtime-plan-20261103T1315-0500\n"
            "# Wheel-only install; hashes enforced (pip --require-hashes --only-binary :all:)\n"
            + "".join(f"{name}=={version} --hash={digest}\n" for name, version, digest in pinned))


def write_lock(directory, entries=None):
    path = os.path.join(directory, "lidar-tools-lock.txt")
    with open(path, "w", encoding="utf-8") as handle:
        handle.write(lock_text(entries))
    return path


class World:
    def __init__(self, venv_dir):
        self.venv_dir = venv_dir
        self.calls = []
        self.installed = False
        self.identity_output = f"{venv_dir}\n/usr/bin\n3.14.4\n"
        self.package_output = "laspy 2.7.0\nlazrs 0.8.2\nnumpy 2.5.4\n"
        self.download_results = []


def fake_child(world):
    def run(argv, deadline=None, cap=None, env=None):
        world.calls.append(list(argv))
        script = " ".join(argv)
        if "-m venv" in script:
            venv_dir = argv[-1]
            os.makedirs(os.path.join(venv_dir, "bin"), exist_ok=True)
            with open(os.path.join(venv_dir, B.VENV_CONFIG_NAME), "w", encoding="utf-8") as handle:
                handle.write("home = /usr/bin\ninclude-system-site-packages = false\n")
            placeholder = os.path.join(venv_dir, "bin", "python")
            with open(placeholder, "w", encoding="utf-8") as handle:
                handle.write("#!/bin/sh\nexit 0\n")
            os.chmod(placeholder, 0o755)
            return ChildResult(0, "", "")
        if "pip download" in script:
            result = world.download_results.pop(0)
            if result.returncode == 0:
                dest = argv[argv.index("--dest") + 1]
                for index, name in enumerate(WHEEL_NAMES):
                    with open(os.path.join(dest, name), "wb") as handle:
                        handle.write(b"w" * ((index + 1) * 16))
            return result
        if "pip install" in script:
            world.installed = True
            return ChildResult(0, "", "")
        if "sys.prefix" in script:
            return ChildResult(0, world.identity_output, "")
        if "metadata.version" in script:
            return ChildResult(0, world.package_output, "")
        return ChildResult(1, "", f"unexpected child invocation: {script[:80]}")
    return run


def patch_mounts(case, options="rw", mount_point="/tmp"):
    path = os.path.join(case.dir, f"mounts-{mount_point.strip('/').replace('/', '-')}-{options}")
    with open(path, "w", encoding="utf-8") as handle:
        handle.write(f"tmpfs {mount_point} tmpfs {options} 0 0\n")
    return mock.patch.object(B, "PROC_MOUNTS_PATH", path)


def patch_free_space(free_bytes):
    usage = SimpleNamespace(f_bavail=max(1, free_bytes // 4096), f_frsize=4096)
    return mock.patch.object(B, "statvfs", lambda path: usage)


def build_venv(base, receipt_text=None, symlink_python=False):
    venv_dir = os.path.join(base, "tools", "lidar-venv")
    os.makedirs(os.path.join(venv_dir, "bin"), exist_ok=True)
    with open(os.path.join(venv_dir, B.VENV_CONFIG_NAME), "w", encoding="utf-8") as handle:
        handle.write("home = /usr/bin\ninclude-system-site-packages = false\nversion = 3.14.4\n")
    python = os.path.join(venv_dir, "bin", "python")
    if symlink_python:
        os.symlink("/usr/bin/python3", python)
    else:
        with open(python, "w", encoding="utf-8") as handle:
            handle.write("#!/bin/sh\nexit 0\n")
        os.chmod(python, 0o755)
    if receipt_text is not None:
        with open(os.path.join(venv_dir, B.RECEIPT_NAME), "w", encoding="utf-8") as handle:
            handle.write(receipt_text)
    return venv_dir


def receipt_document():
    return {
        "receipt_version": 1,
        "created": "2026-11-04T00:00:00.000-05:00",
        "python_version": "3.14.4",
        "packages": [{"name": name, "version": version, "hash": digest}
                     for name, version, digest in B.PINNED_PACKAGES],
    }


class Case(unittest.TestCase):
    def setUp(self):
        os.makedirs(TMP_BASE, exist_ok=True)
        self.dir = tempfile.mkdtemp(prefix="case-", dir=TMP_BASE)
        self.addCleanup(shutil.rmtree, self.dir, True)

    def root(self):
        root = os.path.join(self.dir, "proof")
        os.makedirs(root, exist_ok=True)
        return root

    def guards_ok(self):
        stack = contextlib.ExitStack()
        stack.enter_context(patch_mounts(self))
        stack.enter_context(patch_free_space(4 << 30))
        return stack


class TestClassifyTransportError(unittest.TestCase):
    def test_tls_only_retries(self):
        self.assertEqual(B.classify_transport_error(TLS_STDERR, "", B.EXIT_NETWORK), "retry")
        self.assertEqual(B.classify_transport_error("", "TLS handshake failed", B.EXIT_NETWORK), "retry")

    def test_read_timeout_retries(self):
        self.assertEqual(B.classify_transport_error("", TIMEOUT_STDERR, B.EXIT_NETWORK), "retry")

    def test_connection_reset_retries(self):
        self.assertEqual(B.classify_transport_error("", RESET_STDERR, B.EXIT_NETWORK), "retry")

    def test_auth_only_stops(self):
        self.assertEqual(B.classify_transport_error("", AUTH_STDERR, B.EXIT_NETWORK), "stop")
        self.assertEqual(B.classify_transport_error("", "no IGN account", B.EXIT_NETWORK), "stop")
        self.assertEqual(B.classify_transport_error("", "stored credential leaked", B.EXIT_NETWORK), "stop")

    def test_mixed_auth_and_tls_stops_both_orderings(self):
        self.assertEqual(B.classify_transport_error(TLS_STDERR + " " + AUTH_STDERR, "", B.EXIT_NETWORK), "stop")
        self.assertEqual(B.classify_transport_error(AUTH_STDERR + " " + TLS_STDERR, "", B.EXIT_NETWORK), "stop")
        self.assertEqual(B.classify_transport_error("", "TLS reset after HTTP 403", B.EXIT_NETWORK), "stop")

    def test_mixed_auth_and_connection_reset_stops(self):
        mixed = AUTH_STDERR + " " + RESET_STDERR
        self.assertEqual(B.classify_transport_error("", RESET_STDERR, B.EXIT_NETWORK), "retry")
        self.assertEqual(B.classify_transport_error("", mixed, B.EXIT_NETWORK), "stop")
        self.assertEqual(B.classify_transport_error(mixed, "", B.EXIT_NETWORK), "stop")

    def test_non_transport_stops(self):
        self.assertEqual(B.classify_transport_error("", "No matching distribution found", B.EXIT_NETWORK), "stop")
        self.assertEqual(B.classify_transport_error("", "", B.EXIT_NETWORK), "stop")

    def test_transport_with_wrong_returncode_stops(self):
        self.assertEqual(B.classify_transport_error("", TLS_STDERR, 1), "stop")
        self.assertEqual(B.classify_transport_error("", TLS_STDERR, 0), "stop")


class TestFetchLineage(Case):
    def fetch(self, root, responses, url_config=None):
        calls = []
        queue = list(responses)

        def fake_get(url, timeout=None, cap=None):
            calls.append(url)
            item = queue.pop(0)
            if isinstance(item, BaseException):
                raise item
            return item

        with mock.patch.object(B, "http_get", fake_get):
            outcome = B.fetch_lineage(root, url_config)
        return outcome, calls

    def test_valid_collection_selects_first_tile_and_writes(self):
        root = self.root()
        outcome, calls = self.fetch(root, [default_collection()])
        self.assertEqual(calls, [B.DEFAULT_WFS_URL])
        target = os.path.join(root, B.LINEAGE_REL)
        self.assertEqual(outcome, target)
        with open(target, "rb") as handle:
            stored = json.loads(handle.read())
        self.assertEqual(set(stored), B.PROVIDER_KEYS)
        self.assertEqual(stored["coordonnees_nw"], B.TILE_COORDS)
        self.assertTrue(stored["url_npl"].endswith(B.FIRST_TILE_NAME))
        self.assertEqual(stored["nombre_points"], 1_000_000)

    def test_idempotent_skip_leaves_file_untouched(self):
        root = self.root()
        self.fetch(root, [default_collection()])
        target = os.path.join(root, B.LINEAGE_REL)
        compact = json.dumps(tile_props(B.TILE_COORDS)).encode("utf-8")
        with open(target, "wb") as handle:
            handle.write(compact)
        with mock.patch.object(B, "http_get", lambda url, timeout=None, cap=None: default_collection()):
            outcome = B.fetch_lineage(root)
        self.assertEqual(outcome, target)
        with open(target, "rb") as handle:
            self.assertEqual(handle.read(), compact)

    def test_missing_feature_stops(self):
        root = self.root()
        others = [tile_props(tile) for tile in OTHER_TILES[:7] + ("0999-9999",)]
        with self.assertRaises(B.Stop) as caught:
            self.fetch(root, [collection_bytes(others)])
        self.assertEqual(caught.exception.code, B.EXIT_GUARD)
        self.assertIn("no WFS feature matches", caught.exception.message)
        self.assertFalse(os.path.exists(os.path.join(root, B.LINEAGE_REL)))

    def test_ambiguous_features_stop(self):
        root = self.root()
        props = [tile_props(B.TILE_COORDS, points=1), tile_props(B.TILE_COORDS, points=2)]
        props += [tile_props(tile) for tile in OTHER_TILES[:6]]
        with self.assertRaises(B.Stop) as caught:
            self.fetch(root, [collection_bytes(props)])
        self.assertEqual(caught.exception.code, B.EXIT_GUARD)
        self.assertIn("ambiguous", caught.exception.message)

    def test_malformed_json_stops(self):
        root = self.root()
        with self.assertRaises(B.Stop) as caught:
            self.fetch(root, [b"{not json at all"])
        self.assertEqual(caught.exception.code, B.EXIT_GUARD)
        self.assertFalse(os.path.exists(os.path.join(root, B.LINEAGE_REL)))

    def test_feature_count_within_bounds_selects_the_single_match(self):
        for count in (8, 9, 16):
            with self.subTest(count=count):
                root = os.path.join(self.dir, f"proof-{count}")
                props = [tile_props(B.TILE_COORDS)] + [tile_props(tile) for tile in unrelated_tiles(count - 1)]
                outcome, _calls = self.fetch(root, [collection_bytes(props)])
                with open(outcome, "rb") as handle:
                    self.assertEqual(json.loads(handle.read())["coordonnees_nw"], B.TILE_COORDS)

    def test_feature_count_outside_bounds_stops(self):
        root = self.root()
        cases = {
            0: [],
            17: [tile_props(B.TILE_COORDS)] + [tile_props(tile) for tile in unrelated_tiles(16)],
        }
        for count, props in cases.items():
            with self.subTest(count=count):
                with self.assertRaises(B.Stop) as caught:
                    self.fetch(root, [collection_bytes(props)])
                self.assertEqual(caught.exception.code, B.EXIT_GUARD)
                self.assertIn(f"returned {count} features; expected 1..16", caught.exception.message)
                self.assertFalse(os.path.exists(os.path.join(root, B.LINEAGE_REL)))

    def test_wrong_provider_keys_stop(self):
        root = self.root()
        broken = tile_props(B.TILE_COORDS)
        del broken["url_mnt"]
        props = [broken] + [tile_props(tile) for tile in OTHER_TILES]
        with self.assertRaises(B.Stop) as caught:
            self.fetch(root, [collection_bytes(props)])
        self.assertEqual(caught.exception.code, B.EXIT_GUARD)
        self.assertIn("18 provider keys", caught.exception.message)

    def test_oversized_body_stops(self):
        root = self.root()
        oversized = b"x" * (B.WFS_BODY_CAP_BYTES + 1)
        with self.assertRaises(B.Stop) as caught:
            self.fetch(root, [oversized])
        self.assertEqual(caught.exception.code, B.EXIT_GUARD)
        self.assertIn("output cap", caught.exception.message)
        self.assertFalse(os.path.exists(os.path.join(root, B.LINEAGE_REL)))

    def test_auth_error_stops_immediately(self):
        root = self.root()
        error = urllib.error.HTTPError(B.DEFAULT_WFS_URL, 401, "Unauthorized", None, None)
        with self.assertRaises(B.Stop) as caught:
            self.fetch(root, [error])
        self.assertEqual(caught.exception.code, B.EXIT_GUARD)
        self.assertIn("no IGN account", caught.exception.message)
        self.assertFalse(os.path.exists(os.path.join(root, B.LINEAGE_REL)))

    def test_tls_errors_retry_then_succeed(self):
        root = self.root()
        transient = urllib.error.URLError("[SSL: UNEXPECTED_EOF_WHILE_READING] EOF occurred")
        outcome, _calls = self.fetch(root, [transient, transient, default_collection()])
        self.assertTrue(os.path.isfile(os.path.join(root, B.LINEAGE_REL)))
        self.assertTrue(outcome.endswith("lineage-0840-6958.json"))

    def test_tls_retries_exhaust_at_three_attempts(self):
        root = self.root()
        transient = urllib.error.URLError("timed out")
        calls = []

        def fake_get(url, timeout=None, cap=None):
            calls.append(url)
            raise transient

        with mock.patch.object(B, "http_get", fake_get):
            with self.assertRaises(B.Stop) as caught:
                B.fetch_lineage(root)
        self.assertEqual(caught.exception.code, B.EXIT_NETWORK)
        self.assertEqual(len(calls), B.TRANSPORT_ATTEMPTS)
        self.assertFalse(os.path.exists(os.path.join(root, B.LINEAGE_REL)))

    def test_auth_and_tls_mixed_stops_after_one_attempt(self):
        root = self.root()
        mixed = urllib.error.URLError("TLS handshake aborted; HTTP 401 authorization required")
        calls = []

        def fake_get(url, timeout=None, cap=None):
            calls.append(url)
            raise mixed

        with mock.patch.object(B, "http_get", fake_get):
            with self.assertRaises(B.Stop) as caught:
                B.fetch_lineage(root)
        self.assertEqual(caught.exception.code, B.EXIT_NETWORK)
        self.assertEqual(len(calls), 1)

    def test_atomic_write_failure_leaves_no_partial(self):
        root = self.root()
        with mock.patch.object(B, "http_get", lambda url, timeout=None, cap=None: default_collection()):
            with mock.patch("os.rename", side_effect=OSError("simulated rename failure")):
                with self.assertRaises(B.Stop) as caught:
                    B.fetch_lineage(root)
        self.assertEqual(caught.exception.code, B.EXIT_STATE)
        self.assertIn("no partial file", caught.exception.message)
        state_dir = os.path.dirname(os.path.join(root, B.LINEAGE_REL))
        self.assertTrue(os.path.isdir(state_dir))
        leftovers = [name for name in os.listdir(state_dir) if ".tmp-" in name]
        self.assertEqual(leftovers, [])

    def test_symlinked_ancestor_refused(self):
        root = self.root()
        real = os.path.join(self.dir, "elsewhere")
        os.makedirs(real)
        os.symlink(real, os.path.join(root, "lidarhd"))
        with mock.patch.object(B, "http_get", lambda url, timeout=None, cap=None: default_collection()):
            with self.assertRaises(B.Stop) as caught:
                B.fetch_lineage(root)
        self.assertEqual(caught.exception.code, B.EXIT_GUARD)
        self.assertIn("symlinked ancestor", caught.exception.message)
        self.assertEqual(os.listdir(real), [])

    def test_symlink_at_target_refused(self):
        root = self.root()
        state_dir = os.path.join(root, "lidarhd", "state")
        os.makedirs(state_dir)
        os.symlink("/nonexistent-target", os.path.join(root, B.LINEAGE_REL))
        with mock.patch.object(B, "http_get", lambda url, timeout=None, cap=None: default_collection()):
            with self.assertRaises(B.Stop) as caught:
                B.fetch_lineage(root)
        self.assertEqual(caught.exception.code, B.EXIT_STATE)
        self.assertIn("symlink", caught.exception.message)

    def test_existing_different_lineage_is_drift(self):
        root = self.root()
        self.fetch(root, [default_collection()])
        target = os.path.join(root, B.LINEAGE_REL)
        drifted = tile_props(B.TILE_COORDS, points=99)
        with open(target, "w", encoding="utf-8") as handle:
            json.dump(drifted, handle)
        with mock.patch.object(B, "http_get", lambda url, timeout=None, cap=None: default_collection()):
            with self.assertRaises(B.Stop) as caught:
                B.fetch_lineage(root)
        self.assertEqual(caught.exception.code, B.EXIT_STATE)
        self.assertIn("differs", caught.exception.message)
        with open(target, "r", encoding="utf-8") as handle:
            self.assertEqual(json.load(handle)["nombre_points"], 99)

    def test_existing_malformed_lineage_is_drift(self):
        root = self.root()
        target = os.path.join(root, B.LINEAGE_REL)
        os.makedirs(os.path.dirname(target))
        with open(target, "wb") as handle:
            handle.write(b"{oops")
        with mock.patch.object(B, "http_get", lambda url, timeout=None, cap=None: default_collection()):
            with self.assertRaises(B.Stop) as caught:
                B.fetch_lineage(root)
        self.assertEqual(caught.exception.code, B.EXIT_STATE)

    def test_url_config_override_reaches_the_seam(self):
        root = self.root()
        override = {"url": "https://synthetic.example/wfs/ows?service=WFS"}
        outcome, calls = self.fetch(root, [default_collection()], url_config=override)
        self.assertEqual(calls, [override["url"]])
        self.assertTrue(os.path.isfile(outcome))

    def test_bad_url_config_refused(self):
        root = self.root()
        with self.assertRaises(B.Stop) as caught:
            self.fetch(root, [default_collection()], url_config={"target": "nope"})
        self.assertEqual(caught.exception.code, B.EXIT_GUARD)


class TestLockAndPlatform(Case):
    def test_parse_lock_accepts_the_pinned_entries(self):
        entries = B.parse_lock(lock_text(), "lock")
        self.assertEqual(entries, list(B.PINNED_PACKAGES))

    def test_parse_lock_rejects_wrong_hash(self):
        drifted = [(name, version, "sha256:" + "0" * 64) if name == "laspy" else (name, version, digest)
                   for name, version, digest in B.PINNED_PACKAGES]
        with self.assertRaises(B.Stop) as caught:
            B.parse_lock(lock_text(drifted), "lock")
        self.assertEqual(caught.exception.code, B.EXIT_STATE)
        self.assertIn("drifts", caught.exception.message)

    def test_parse_lock_rejects_wrong_count_and_shape(self):
        two = lock_text(B.PINNED_PACKAGES[:2])
        with self.assertRaises(B.Stop) as caught:
            B.parse_lock(two, "lock")
        self.assertEqual(caught.exception.code, B.EXIT_STATE)
        with self.assertRaises(B.Stop) as caught:
            B.parse_lock("laspy 2.7.0\n", "lock")
        self.assertEqual(caught.exception.code, B.EXIT_STATE)

    def test_resolve_lock_path_prefers_root_copy_then_plan_record(self):
        root = self.root()
        tools_dir = os.path.join(root, "tools")
        os.makedirs(tools_dir)
        copy_path = os.path.join(tools_dir, B.LOCK_NAME)
        with open(copy_path, "w", encoding="utf-8") as handle:
            handle.write(lock_text())
        self.assertEqual(B.resolve_lock_path(root, None), copy_path)
        os.remove(copy_path)
        self.assertEqual(B.resolve_lock_path(root, None), B.DEFAULT_LOCK_PATH)
        self.assertTrue(os.path.isfile(B.resolve_lock_path(root, None)))
        with self.assertRaises(B.Stop) as caught:
            B.resolve_lock_path(root, os.path.join(self.dir, "absent-lock.txt"))
        self.assertEqual(caught.exception.code, B.EXIT_STATE)

    def test_platform_matrix(self):
        self.assertTrue(B.platform_supported("linux", "x86_64", (3, 14)))
        self.assertFalse(B.platform_supported("win32", "x86_64", (3, 14)))
        self.assertFalse(B.platform_supported("linux", "aarch64", (3, 14)))
        self.assertFalse(B.platform_supported("linux", "x86_64", (3, 12)))

    def test_unsupported_platform_stops_before_network(self):
        root = self.root()
        lock_path = write_lock(self.dir)
        world = World(os.path.join(root, "tools", "lidar-venv"))
        with self.guards_ok():
            with mock.patch("platform.machine", return_value="aarch64"):
                with mock.patch.object(B, "RUN_CHILD", fake_child(world)):
                    with self.assertRaises(B.Stop) as caught:
                        B.bootstrap_tools(root, lock_path=lock_path)
        self.assertEqual(caught.exception.code, B.EXIT_GUARD)
        self.assertIn("unsupported platform", caught.exception.message)
        self.assertEqual(world.calls, [])
        self.assertFalse(os.path.exists(os.path.join(root, "tools")))


class TestBootstrapGuards(Case):
    def bootstrap(self, root, mounts=None, free_bytes=None):
        lock_path = write_lock(self.dir)
        if mounts is not None:
            context = mock.patch.object(B, "PROC_MOUNTS_PATH", mounts)
        else:
            context = patch_mounts(self)
        space = patch_free_space(free_bytes) if free_bytes is not None else patch_free_space(4 << 30)
        with context, space:
            return B.bootstrap_tools(root, lock_path=lock_path)

    def test_readonly_mount_refused_without_mkdir(self):
        root = self.root()
        mounts = os.path.join(self.dir, "mounts-ro")
        with open(mounts, "w", encoding="utf-8") as handle:
            handle.write("tmpfs /tmp tmpfs ro 0 0\n")
        with self.assertRaises(B.Stop) as caught:
            self.bootstrap(root, mounts=mounts)
        self.assertEqual(caught.exception.code, B.EXIT_GUARD)
        self.assertIn("not rw", caught.exception.message)
        self.assertFalse(os.path.exists(os.path.join(root, "tools")))

    def test_uncovered_root_refused(self):
        root = self.root()
        mounts = os.path.join(self.dir, "mounts-unrelated")
        with open(mounts, "w", encoding="utf-8") as handle:
            handle.write("ext4 /mnt/pit2 ext4 rw 0 0\n")
        with self.assertRaises(B.Stop) as caught:
            self.bootstrap(root, mounts=mounts)
        self.assertEqual(caught.exception.code, B.EXIT_GUARD)
        self.assertIn("no mount entry", caught.exception.message)
        self.assertFalse(os.path.exists(os.path.join(root, "tools")))

    def test_insufficient_free_space_refused_without_mkdir(self):
        root = self.root()
        with self.assertRaises(B.Stop) as caught:
            self.bootstrap(root, free_bytes=4096)
        self.assertEqual(caught.exception.code, B.EXIT_GUARD)
        self.assertIn("insufficient free space", caught.exception.message)
        self.assertFalse(os.path.exists(os.path.join(root, "tools")))

    def test_symlinked_tools_ancestor_refused(self):
        root = self.root()
        real = os.path.join(self.dir, "elsewhere")
        os.makedirs(real)
        os.symlink(real, os.path.join(root, "tools"))
        with self.assertRaises(B.Stop) as caught:
            self.bootstrap(root)
        self.assertEqual(caught.exception.code, B.EXIT_GUARD)
        self.assertIn("symlinked ancestor", caught.exception.message)
        self.assertEqual(os.listdir(real), [])


class TestVenvReuse(Case):
    def reuse(self, root, identity_output=None, package_output=None):
        lock_path = write_lock(self.dir)
        world = World(os.path.join(root, "tools", "lidar-venv"))
        if identity_output is not None:
            world.identity_output = identity_output
        if package_output is not None:
            world.package_output = package_output
        contexts = self.guards_ok()
        with contexts, mock.patch.object(B, "RUN_CHILD", fake_child(world)):
            outcome = B.bootstrap_tools(root, lock_path=lock_path)
        return outcome, world

    def test_valid_venv_with_matching_receipt_is_reused(self):
        root = self.root()
        venv_dir = build_venv(root, receipt_text=json.dumps(receipt_document()))
        captured = io.StringIO()
        with contextlib.redirect_stdout(captured):
            outcome, world = self.reuse(root)
        self.assertEqual(outcome, os.path.join(venv_dir, "bin", "python"))
        self.assertFalse(any("pip" in " ".join(call) for call in world.calls))
        self.assertFalse(os.path.exists(os.path.join(root, "tools", "wheel-cache")))
        self.assertIn("matching installed receipt", captured.getvalue())

    def test_valid_venv_without_receipt_is_allowed_with_acknowledgment(self):
        root = self.root()
        venv_dir = build_venv(root)
        captured = io.StringIO()
        with contextlib.redirect_stdout(captured):
            outcome, world = self.reuse(root)
        self.assertEqual(outcome, os.path.join(venv_dir, "bin", "python"))
        self.assertFalse(any("pip" in " ".join(call) for call in world.calls))
        self.assertFalse(os.path.exists(os.path.join(root, "tools", "wheel-cache")))
        self.assertIn("receipt absent", captured.getvalue())
        self.assertIn("NOT adopted or modified", captured.getvalue())

    def test_receipt_disagreement_refused(self):
        root = self.root()
        drifted = receipt_document()
        drifted["packages"][0]["version"] = "2.6.0"
        build_venv(root, receipt_text=json.dumps(drifted))
        with self.assertRaises(B.Stop) as caught:
            self.reuse(root)
        self.assertEqual(caught.exception.code, B.EXIT_STATE)
        self.assertIn("disagrees with the pinned lock", caught.exception.message)

    def test_malformed_receipt_refused(self):
        root = self.root()
        build_venv(root, receipt_text="{oops")
        with self.assertRaises(B.Stop) as caught:
            self.reuse(root)
        self.assertEqual(caught.exception.code, B.EXIT_STATE)
        self.assertIn("not valid JSON", caught.exception.message)

    def test_wrong_prefix_refused(self):
        root = self.root()
        build_venv(root)
        with self.assertRaises(B.Stop) as caught:
            self.reuse(root, identity_output=f"/somewhere/else\n/usr/bin\n3.14.4\n")
        self.assertEqual(caught.exception.code, B.EXIT_STATE)
        self.assertIn("does not validate", caught.exception.message)
        self.assertIn("sys.prefix", caught.exception.message)

    def test_wrong_package_version_refused(self):
        root = self.root()
        build_venv(root)
        with self.assertRaises(B.Stop) as caught:
            self.reuse(root, package_output="laspy 2.6.0\nlazrs 0.8.2\nnumpy 2.5.4\n")
        self.assertEqual(caught.exception.code, B.EXIT_STATE)
        self.assertIn("laspy", caught.exception.message)

    def test_base_prefix_equal_to_prefix_refused(self):
        root = self.root()
        venv_dir = build_venv(root)
        with self.assertRaises(B.Stop) as caught:
            self.reuse(root, identity_output=f"{venv_dir}\n{venv_dir}\n3.14.4\n")
        self.assertEqual(caught.exception.code, B.EXIT_STATE)
        self.assertIn("base_prefix", caught.exception.message)

    def test_missing_pyvenv_cfg_refused(self):
        root = self.root()
        venv_dir = build_venv(root)
        os.remove(os.path.join(venv_dir, B.VENV_CONFIG_NAME))
        with self.assertRaises(B.Stop) as caught:
            self.reuse(root)
        self.assertEqual(caught.exception.code, B.EXIT_STATE)

    def test_missing_bin_python_refused(self):
        root = self.root()
        venv_dir = build_venv(root)
        os.remove(os.path.join(venv_dir, "bin", "python"))
        with self.assertRaises(B.Stop) as caught:
            self.reuse(root)
        self.assertEqual(caught.exception.code, B.EXIT_STATE)
        self.assertIn("missing or not executable", caught.exception.message)

    def test_legit_symlink_to_system_python_accepted(self):
        root = self.root()
        venv_dir = build_venv(root, symlink_python=True)
        python = os.path.join(venv_dir, "bin", "python")
        self.assertTrue(os.path.islink(python))
        world = World(venv_dir)
        lock_path = write_lock(self.dir)
        with self.guards_ok(), mock.patch.object(B, "RUN_CHILD", fake_child(world)):
            outcome = B.bootstrap_tools(root, lock_path=lock_path)
        self.assertEqual(outcome, python)
        identity_calls = [call for call in world.calls if "sys.prefix" in " ".join(call)]
        self.assertEqual(identity_calls[0][0], python)
        self.assertNotEqual(os.path.realpath(identity_calls[0][0]), identity_calls[0][0])
        self.assertTrue(os.path.realpath(identity_calls[0][0]).startswith("/usr/bin"))

    def test_arbitrary_executable_at_bin_python_refused_with_real_runner(self):
        root = self.root()
        venv_dir = build_venv(root)
        python = os.path.join(venv_dir, "bin", "python")
        with open(python, "w", encoding="utf-8") as handle:
            handle.write("#!/bin/sh\nprintf 'definitely not a python identity\\n'\n")
        os.chmod(python, 0o755)
        lock_path = write_lock(self.dir)
        with self.guards_ok():
            with self.assertRaises(B.Stop) as caught:
                B.bootstrap_tools(root, lock_path=lock_path)
        self.assertEqual(caught.exception.code, B.EXIT_STATE)
        self.assertIn("does not validate", caught.exception.message)


class TestBootstrapFresh(Case):
    def fresh(self, root, world, download_results=None, package_output=None, identity_output=None):
        world.download_results = list(download_results or [])
        if identity_output is not None:
            world.identity_output = identity_output
        if package_output is not None:
            world.package_output = package_output
        lock_path = write_lock(self.dir)
        with self.guards_ok(), mock.patch.object(B, "RUN_CHILD", fake_child(world)):
            return B.bootstrap_tools(root, lock_path=lock_path)

    def download_calls(self, world):
        return [call for call in world.calls if "pip download" in " ".join(call)]

    def install_calls(self, world):
        return [call for call in world.calls if "pip install" in " ".join(call)]

    def test_full_fresh_bootstrap_with_tls_retries(self):
        root = self.root()
        venv_dir = os.path.join(root, "tools", "lidar-venv")
        world = World(venv_dir)
        tls_fail = ChildResult(1, "", TLS_STDERR)
        outcome = self.fresh(root, world, download_results=[tls_fail, tls_fail, ChildResult(0, "", "")])
        python = os.path.join(venv_dir, "bin", "python")
        self.assertEqual(outcome, python)
        self.assertEqual(len(self.download_calls(world)), 3)
        self.assertEqual(len(self.install_calls(world)), 1)
        download_argv = self.download_calls(world)[0]
        self.assertIn("--only-binary", download_argv)
        self.assertEqual(download_argv[download_argv.index("--only-binary") + 1], ":all:")
        self.assertIn("--no-deps", download_argv)
        self.assertIn("--require-hashes", download_argv)
        self.assertEqual(download_argv[download_argv.index("--retries") + 1], "0")
        self.assertEqual(download_argv[download_argv.index("--dest") + 1],
                         os.path.join(root, "tools", "wheel-cache"))
        install_argv = self.install_calls(world)[0]
        self.assertIn("--no-index", install_argv)
        self.assertIn("--require-hashes", install_argv)
        self.assertEqual(install_argv[install_argv.index("--find-links") + 1],
                         os.path.join(root, "tools", "wheel-cache"))
        cache_dir = os.path.join(root, "tools", "wheel-cache")
        self.assertEqual(sorted(name for name in os.listdir(cache_dir) if name.endswith(".whl")),
                         sorted(WHEEL_NAMES))
        with open(os.path.join(venv_dir, B.RECEIPT_NAME), "r", encoding="utf-8") as handle:
            receipt = json.load(handle)
        self.assertEqual(receipt["receipt_version"], 1)
        self.assertEqual(receipt["python_version"], "3.14.4")
        self.assertEqual(receipt["packages"],
                         [{"name": name, "version": version, "hash": digest}
                          for name, version, digest in B.PINNED_PACKAGES])
        self.assertEqual(receipt["wheel_sizes"],
                         {name: (index + 1) * 16 for index, name in enumerate(WHEEL_NAMES)})

    def test_download_retry_ceiling_is_three_total(self):
        root = self.root()
        world = World(os.path.join(root, "tools", "lidar-venv"))
        tls_fail = ChildResult(1, "", TLS_STDERR)
        with self.assertRaises(B.Stop) as caught:
            self.fresh(root, world, download_results=[tls_fail] * 4)
        self.assertEqual(caught.exception.code, B.EXIT_NETWORK)
        self.assertEqual(len(self.download_calls(world)), 3)
        self.assertEqual(self.install_calls(world), [])
        self.assertFalse(os.path.exists(os.path.join(root, "tools", "lidar-venv", B.RECEIPT_NAME)))

    def test_non_transport_download_failure_never_retries(self):
        root = self.root()
        world = World(os.path.join(root, "tools", "lidar-venv"))
        no_dist = ChildResult(1, "", "ERROR: No matching distribution found for laspy==2.7.0")
        with self.assertRaises(B.Stop) as caught:
            self.fresh(root, world, download_results=[no_dist])
        self.assertEqual(caught.exception.code, B.EXIT_NETWORK)
        self.assertEqual(len(self.download_calls(world)), 1)
        self.assertIn("No matching distribution", caught.exception.message)

    def test_install_failure_stops_without_receipt(self):
        root = self.root()
        world = World(os.path.join(root, "tools", "lidar-venv"))
        lock_path = write_lock(self.dir)
        world.download_results = [ChildResult(0, "", "")]

        def failing_install(argv, deadline=None, cap=None, env=None):
            world.calls.append(list(argv))
            if "pip install" in " ".join(argv):
                return ChildResult(1, "", "THESE PACKAGES DO NOT MATCH THE HASHES")
            return fake_child(world)(argv, deadline=deadline, cap=cap, env=env)

        with self.guards_ok(), mock.patch.object(B, "RUN_CHILD", failing_install):
            with self.assertRaises(B.Stop) as caught:
                B.bootstrap_tools(root, lock_path=lock_path)
        self.assertEqual(caught.exception.code, B.EXIT_NETWORK)
        self.assertIn("never retried", caught.exception.message)
        self.assertFalse(os.path.exists(os.path.join(root, "tools", "lidar-venv", B.RECEIPT_NAME)))

    def test_fresh_venv_failing_validation_refused(self):
        root = self.root()
        venv_dir = os.path.join(root, "tools", "lidar-venv")
        world = World(venv_dir)
        with self.assertRaises(B.Stop) as caught:
            self.fresh(root, world, download_results=[ChildResult(0, "", "")],
                       identity_output="/somewhere/else\n/usr/bin\n3.14.4\n")
        self.assertEqual(caught.exception.code, B.EXIT_STATE)
        self.assertIn("does not validate", caught.exception.message)
        self.assertFalse(os.path.exists(os.path.join(venv_dir, B.RECEIPT_NAME)))


class TestRealRunner(Case):
    def test_real_run_child_basic_and_bounds(self):
        result = B.run_child([sys.executable, "-c", "print('ok')"], deadline=30, cap=1 << 20)
        self.assertEqual(result.returncode, 0)
        self.assertEqual(result.stdout, "ok\n")
        with self.assertRaises(B.Stop) as caught:
            B.run_child([sys.executable, "-c", "print('x' * 128)"], deadline=30, cap=16)
        self.assertEqual(caught.exception.code, B.EXIT_NETWORK)
        self.assertIn("output cap", caught.exception.message)
        with self.assertRaises(B.Stop) as caught:
            B.run_child([sys.executable, "-c", "import time; time.sleep(30)"], deadline=1, cap=1 << 20)
        self.assertEqual(caught.exception.code, B.EXIT_NETWORK)
        self.assertIn("deadline", caught.exception.message)


if __name__ == "__main__":
    unittest.main()
