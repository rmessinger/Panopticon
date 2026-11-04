"""Offline tests for scripts/geo/sedan_prepare_orchestrator.py.

Synthetic cache trees under /tmp/opencode, a fake child runner (RUN_CHILD seam) that records each
helper invocation and applies the helper's documented on-disk effect, fake bootstrap functions
(BOOTSTRAP_MODULE seam), and a patched statvfs. No network, no install, no LAZ or DEM read, no
dotnet. The real subprocesses are tiny python -c children, a local venv created without pip, and
the probe's local status command.
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
import time
import types
import unittest
from unittest import mock

sys.dont_write_bytecode = True

GEO_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TMP_BASE = "/tmp/opencode/sedan-tests"


def load_module(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(GEO_DIR, filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


O = load_module("sedan_prepare_orchestrator", "sedan_prepare_orchestrator.py")
PROBE = O.PROBE_MODULE
BOOTSTRAP = O.BOOTSTRAP_MODULE
ChildResult = O.ChildResult

ACQUIRE_NAME = "sedan_acquire.py"
EXTRACT_NAME = "sedan_extract.py"
PROBE_NAME = "sedan_lidar_probe.py"
STAGE2_NAME = "sedan_lidar_acquire.py"
INTEGRITY_NAME = "sedan_lidar_integrity.py"
FREEZE_NAME = "sedan_lidar_freeze.py"
BACKEND_NAME = "sedan_prepare_inputs.py"
ACCEPTED_BEFORE_FREEZE = {ACQUIRE_NAME, EXTRACT_NAME, PROBE_NAME, STAGE2_NAME, INTEGRITY_NAME}
BRIDGE_KEYS = ("sources", "targetCoordinateSystem", "metersPerGameUnit", "defaultMaterial",
               "extentFeatureId", "verticalPolicy", "rasterPolicy", "tolerances", "controlPoints")
VENV_QUERY = "venv-query"
FIXTURE_RESOURCE = "RES"
FIXTURE_SUBRESOURCE = "SUB"
EVIDENCE_PREFIX = b"# Sedan archive completion evidence "

TLS_STDOUT = (
    "request failed for https://data.geopf.fr/telechargement/download/RGEALTI/x.7z.001: "
    "<urlopen error [SSL: UNEXPECTED_EOF_WHILE_READING] EOF occurred in violation of protocol>\n"
    "FAIL(3): request failed for https://data.geopf.fr/telechargement/download/RGEALTI/x.7z.001\n"
)
TIMEOUT_STDOUT = (
    "FAIL(3): network error during download of https://data.geopf.fr/x.7z.002: timed out; "
    "owned partial preserved; rerun to resume\n"
)
RESET_STDOUT = (
    "FAIL(3): request failed for https://data.geopf.fr/x.7z.003: "
    "<urlopen error [Errno 104] Connection reset by peer>\n"
)
AUTH_STDOUT = (
    "FAIL(3): HTTP 403 from https://data.geopf.fr/x.7z.001: no IGN account or credentials are "
    "supported; failing closed\n"
)


def fixture_parts():
    return [(f"{FIXTURE_SUBRESOURCE}.7z.{number:03d}", f"part{number}".encode("ascii") * number)
            for number in range(1, O.ARCHIVE_PART_COUNT + 1)]


FIXTURE_TOTAL = sum(len(content) for _name, content in fixture_parts())
FIXTURE_PINNED = tuple((name, len(content)) for name, content in fixture_parts())


def part_file(root, name):
    return os.path.join(root, "archives", FIXTURE_RESOURCE, FIXTURE_SUBRESOURCE, name)


def expected_body():
    return "".join(f"{hashlib.sha256(content).hexdigest()}  {len(content)}  {name}\n"
                   for name, content in fixture_parts()).encode("utf-8")


def evidence_with_header(header=EVIDENCE_PREFIX + b"2026-11-04T00:00:00.000+00:00\n"):
    return header + expected_body()


def write_bytes(path, content):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as handle:
        handle.write(content)


def write_json(path, payload):
    write_bytes(path, json.dumps(payload).encode("utf-8"))


def read_bytes(path):
    with open(path, "rb") as handle:
        return handle.read()


def tree_snapshot(root):
    snapshot = {}
    for dirpath, dirnames, filenames in os.walk(root):
        for name in dirnames + filenames:
            path = os.path.join(dirpath, name)
            info = os.lstat(path)
            snapshot[path] = (info.st_mode, info.st_size, info.st_mtime_ns)
    return snapshot


def make_archives_complete(root):
    products = {}
    for name, content in fixture_parts():
        write_bytes(part_file(root, name), content)
        products[f"{FIXTURE_RESOURCE}/{name}"] = {
            "url": f"https://example.invalid/{name}", "expected_size": len(content),
            "bytes_on_disk": len(content), "status": "complete", "mtime_ns": 0,
            "sha256": hashlib.sha256(content).hexdigest(),
        }
    write_json(os.path.join(root, O.STATE_REL), {"version": 1, "products": products})


def make_archive_evidence(root):
    write_bytes(os.path.join(root, O.ARCHIVE_EVIDENCE_REL), evidence_with_header())


def make_extract_complete(root):
    stage = os.path.join(root, O.EXTRACTED_DIR_REL, O.EXTRACT_STAGE_PREFIX + "T")
    os.makedirs(stage, exist_ok=True)
    open(os.path.join(stage, O.COMPLETED_NAME), "w").close()


def make_venv(root):
    path = os.path.join(root, O.VENV_PYTHON_REL)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as handle:
        handle.write("#!/bin/sh\n")
    os.chmod(path, 0o755)
    write_bytes(os.path.join(root, O.VENV_DIR_REL, O.VENV_CONFIG_NAME),
                b"home = /usr\nversion = 3.14.4\n")


def make_lineage(root):
    write_json(os.path.join(root, O.LINEAGE_REL), {"feature": {"CODE_INSEE": "84", "NOM": "x"}})


def make_first_tile_report(root, passed=True):
    write_json(os.path.join(root, O.PROBE_REPORT_REL),
               {"report_version": 2, "tile": O.FIRST_TILE_NAME, "gates_pass": passed})


def make_remaining_complete(root):
    tiles = {name: {"status": "complete", "expected_size": size, "bytes_on_disk": size,
                    "mtime_ns": 0}
             for name, size in O.STAGE2_TILES.items()}
    write_json(os.path.join(root, O.STAGE2_STATE_REL), {"version": 1, "tiles": tiles})


def make_freeze_complete(root, digest="d" * 64):
    stage = os.path.join(root, O.FREEZE_DIR_REL, O.FREEZE_STAGE_PREFIX + "T")
    os.makedirs(stage, exist_ok=True)
    open(os.path.join(stage, O.COMPLETED_NAME), "w").close()
    write_json(os.path.join(stage, O.STATE_REL), {"canonical_digest": digest})


def make_manifest_unconfigured(root):
    entries = {name: {"size": size, "sha256": "e" * 64, "rel_path": name}
               for name, size in list(O.STAGE2_TILES.items()) + [(O.FIRST_TILE_NAME, 1)]}
    write_json(os.path.join(root, O.FREEZE_DIR_REL, O.INTEGRITY_MANIFEST_NAME), entries)


PROOF_OUTPUT_FILES = {
    "manifest": O.PROOF_REL,
    "texture_map": "preview-textures.json",
    "sources_txt": "sources.txt",
    "extent_kml": os.path.join("prepared", "sedan-extent.kml"),
    "dem_tif": os.path.join("prepared", "sedan-dem-32631.tif"),
    "rgb_tif": os.path.join("prepared", "sedan-ortho-32631.tif"),
}


def proof_payload(keys=BRIDGE_KEYS):
    payload = {key: {} for key in keys}
    if "tolerances" in payload:
        payload["tolerances"] = {"verticalMeters": 1.0}
    return payload


def output_record(root, label):
    path = os.path.join(root, PROOF_OUTPUT_FILES[label])
    content = read_bytes(path)
    return {"path": path, "sha256": hashlib.sha256(content).hexdigest(), "size": len(content)}


def make_proof_outputs(root, keys=BRIDGE_KEYS):
    for label, rel in PROOF_OUTPUT_FILES.items():
        if label == "manifest":
            write_json(os.path.join(root, rel), proof_payload(keys))
        else:
            write_bytes(os.path.join(root, rel), f"{label} synthetic output\n".encode("ascii"))


def make_proof_receipt(root, last_event=O.BACKEND_PHASE_COMPLETE):
    ledger = (json.dumps({"event": "freeze_validated_complete"}) + "\n"
              + json.dumps({"event": last_event, "completedAt": "T", "outputs": {}}) + "\n")
    write_bytes(os.path.join(root, O.BACKEND_LEDGER_REL), ledger.encode("utf-8"))
    write_json(os.path.join(root, O.BACKEND_RECEIPT_REL), {
        "receipt_version": 1, "tool": BACKEND_NAME,
        "outputs": {label: output_record(root, label) for label in PROOF_OUTPUT_FILES},
        "commands_sha256": hashlib.sha256(ledger.encode("utf-8")).hexdigest(),
    })


def make_proof_manifest(root, keys=BRIDGE_KEYS):
    write_json(os.path.join(root, O.PROOF_REL), proof_payload(keys))


def make_proof_complete(root):
    make_proof_outputs(root)
    make_proof_receipt(root)


class FakeWorld:
    """Stands in for RUN_CHILD: records argv and applies each helper's documented effect."""

    def __init__(self, root):
        self.root = root
        self.calls = []
        self.scripted = {}
        self.venv_envs = []
        self.identity = None
        self.packages = None
        self.backend_effect = make_proof_complete

    def script(self, name, results):
        self.scripted[name] = list(results)

    def __call__(self, argv, **options):
        name, root, logical = self.parse(argv)
        self.calls.append((name, list(argv)))
        if name == VENV_QUERY:
            self.venv_envs.append(options.get("env"))
            return ChildResult(0, self.venv_answer(argv), "")
        if self.scripted.get(name):
            result = self.scripted[name].pop(0)
            if result.returncode == 0:
                self.apply(name, argv, root, logical)
            return result
        self.apply(name, argv, root, logical)
        return ChildResult(0, "", "")

    def parse(self, argv):
        if argv[1] == "-c":
            return VENV_QUERY, self.root, []
        if argv[1] == O.PROBE_SCRIPT:
            return PROBE_NAME, argv[3], argv[4:]
        return os.path.basename(argv[1]), self.root, argv[2:]

    def venv_answer(self, argv):
        if argv[2] == O.IDENTITY_CODE:
            if self.identity is not None:
                return self.identity
            return f"{os.path.dirname(os.path.dirname(argv[0]))}\n/usr\n"
        if self.packages is not None:
            return self.packages
        return "".join(f"{name} {version}\n" for name, version in O.EXPECTED_PACKAGES)

    def apply(self, name, argv, root, logical):
        if name == ACQUIRE_NAME:
            make_archives_complete(root)
        elif name == EXTRACT_NAME:
            make_extract_complete(root)
        elif name == PROBE_NAME:
            if logical[0] == "fetch":
                write_bytes(os.path.join(root, O.FIRST_TILE_REL), b"tile")
            elif logical[0] == "probe":
                make_first_tile_report(root, passed=True)
        elif name == STAGE2_NAME:
            make_remaining_complete(root)
        elif name == INTEGRITY_NAME:
            make_manifest_unconfigured(root)
        elif name == BACKEND_NAME and "--prepare" in argv:
            self.backend_effect(root)
        elif name == FREEZE_NAME and "--freeze" in argv:
            if not O.check_freeze(O.Config(root, "", "")):
                make_freeze_complete(root)

    def names(self):
        return [name for name, _argv in self.calls if name != VENV_QUERY]

    def count(self, name):
        return self.names().count(name)

    def probe_argvs(self):
        return [argv for name, argv in self.calls if name == PROBE_NAME]

    def venv_queries(self):
        return sum(1 for name, _argv in self.calls if name == VENV_QUERY)


class OrchestratorTestCase(unittest.TestCase):
    def setUp(self):
        os.makedirs(TMP_BASE, exist_ok=True)
        self.base = tempfile.mkdtemp(dir=TMP_BASE)
        self.addCleanup(shutil.rmtree, self.base, True)
        self.mounts = os.path.join(self.base, "mounts")
        with open(self.mounts, "w", encoding="utf-8") as handle:
            handle.write(f"tmpfs {self.base} tmpfs rw,relatime 0 0\n")
        self.root = os.path.join(self.base, "sedan")
        os.makedirs(self.root)
        self.world = FakeWorld(self.root)
        self.bootstrap_calls = []
        self.bootstrap_stop = None
        self.patch(O, "RUN_CHILD", self.world)
        self.patch(os, "statvfs", mock.Mock(return_value=types.SimpleNamespace(
            f_bavail=1 << 40, f_frsize=1)))
        self.patch(O, "ARCHIVE_TOTAL_BYTES", FIXTURE_TOTAL)
        self.patch(PROBE, "ARCHIVE_EVIDENCE_PARTS", FIXTURE_PINNED)
        self.patch(O, "VENV_LOCK_RECEIPT_LOG", os.path.join(self.base, "no-receipt.txt"))
        self.patch(BOOTSTRAP, "bootstrap_tools", self.fake_bootstrap_tools)
        self.patch(BOOTSTRAP, "fetch_lineage", self.fake_fetch_lineage)

    def patch(self, target, attribute, value):
        patcher = mock.patch.object(target, attribute, value)
        patcher.start()
        self.addCleanup(patcher.stop)

    def fake_bootstrap_tools(self, root, lock_path=None):
        self.bootstrap_calls.append(("tools", root))
        if self.bootstrap_stop is not None:
            raise self.bootstrap_stop
        if not os.path.lexists(os.path.join(root, O.VENV_DIR_REL)):
            make_venv(root)
        return os.path.join(root, O.VENV_PYTHON_REL)

    def fake_fetch_lineage(self, root, url_config=None):
        self.bootstrap_calls.append(("lineage", root))
        path = os.path.join(root, O.LINEAGE_REL)
        if not os.path.lexists(path):
            make_lineage(root)
        return path

    def cfg(self, root=None):
        return O.Config(root=root or self.root, mount_point=self.base, mounts_path=self.mounts)

    def run_quiet(self, function, *args):
        buffer = io.StringIO()
        with contextlib.redirect_stdout(buffer):
            try:
                result = function(*args)
            except O.Stop as stop:
                return stop.code, stop.message, buffer.getvalue()
        return result, None, buffer.getvalue()

    def make_archives_and_extract(self):
        make_archives_complete(self.root)
        make_extract_complete(self.root)

    def make_all_but_freeze(self):
        make_venv(self.root)
        self.make_archives_and_extract()
        make_archive_evidence(self.root)
        make_first_tile_report(self.root, passed=True)
        make_remaining_complete(self.root)


class EmptyStateOrderTests(OrchestratorTestCase):
    def test_empty_state_runs_helpers_in_stage_order(self):
        code, message, output = self.run_quiet(O.run_prepare, self.cfg())
        self.assertIsNone(message, output)
        self.assertEqual(code, 0)
        self.assertEqual(self.world.names(), [
            ACQUIRE_NAME, EXTRACT_NAME, PROBE_NAME, PROBE_NAME, STAGE2_NAME, INTEGRITY_NAME,
            FREEZE_NAME, BACKEND_NAME])

    def test_bootstrap_runs_tools_then_lineage(self):
        self.run_quiet(O.run_prepare, self.cfg())
        self.assertEqual(self.bootstrap_calls, [("tools", self.root), ("lineage", self.root)])

    def test_freeze_runs_only_after_manifest_is_configured(self):
        self.run_quiet(O.run_prepare, self.cfg())
        manifest = json.loads(read_bytes(os.path.join(
            self.root, O.FREEZE_DIR_REL, O.INTEGRITY_MANIFEST_NAME)))
        self.assertEqual(manifest["worker"], {"rel_path": O.BANDED_WORKER_REL,
                                              "sha256": O.BANDED_WORKER_SHA256})
        self.assertEqual(self.world.names()[-2:], [FREEZE_NAME, BACKEND_NAME])

    def test_stage_seven_runs_backend_and_keeps_exit_zero(self):
        code, _message, output = self.run_quiet(O.run_prepare, self.cfg())
        self.assertEqual(code, 0)
        self.assertIn("stage B POST-PREPARE: RUN B prepare completed", output)


class FreshCacheTests(OrchestratorTestCase):
    def test_fresh_cache_runs_backend_once_and_reports_no_pending_stage(self):
        code, _message, output = self.run_quiet(O.run_prepare, self.cfg())
        self.assertEqual(code, 0, output)
        self.assertNotIn("PENDING", output)
        self.assertEqual(self.world.count(BACKEND_NAME), 1)

    def test_every_probe_call_passes_cache_root_before_subcommand(self):
        self.run_quiet(O.run_prepare, self.cfg())
        probe_calls = self.world.probe_argvs()
        self.assertEqual(len(probe_calls), 2)
        for argv in probe_calls:
            self.assertEqual(argv[1], O.PROBE_SCRIPT)
            self.assertEqual(argv[2:4], ["--cache-root", self.root])

    def test_probe_subcommands_carry_expected_arguments(self):
        self.run_quiet(O.run_prepare, self.cfg())
        fetch, probe = self.world.probe_argvs()
        self.assertEqual(fetch[4:], [
            "fetch", O.FIRST_TILE_NAME, "--first-tile", "--execution-brief-accepted"])
        self.assertEqual(probe[4:], [
            "probe", "--worker", os.path.join(self.root, O.VENV_PYTHON_REL),
            O.PROBE_WORKER_PATH, "--lineage-file", os.path.join(self.root, O.LINEAGE_REL)])

    def test_lineage_is_written_under_the_expected_name(self):
        self.run_quiet(O.run_prepare, self.cfg())
        self.assertEqual(os.path.basename(O.LINEAGE_REL), "lineage-0840-6958.json")
        self.assertTrue(os.path.isfile(os.path.join(self.root, O.LINEAGE_REL)))

    def test_fresh_cache_writes_archive_evidence_in_parser_format(self):
        self.run_quiet(O.run_prepare, self.cfg())
        path = os.path.join(self.root, O.ARCHIVE_EVIDENCE_REL)
        content = read_bytes(path)
        header, _separator, body = content.partition(b"\n")
        self.assertTrue(header.startswith(EVIDENCE_PREFIX))
        self.assertEqual(body, expected_body())
        self.assertNotIn(b"TOTAL", content)
        entries = PROBE._parse_archive_manifest(path)
        self.assertEqual(sorted(entries), sorted(name for name, _content in fixture_parts()))


class CachedSkipTests(OrchestratorTestCase):
    def setUp(self):
        super().setUp()
        self.make_all_but_freeze()
        make_freeze_complete(self.root, digest="ab" * 32)
        make_proof_complete(self.root)

    def test_cached_root_skips_every_stage_except_freeze_reverify(self):
        code, _message, output = self.run_quiet(O.run_prepare, self.cfg())
        self.assertEqual(code, 0, output)
        self.assertEqual(self.world.names(), [FREEZE_NAME])
        self.assertIn("--freeze", self.world.calls[-1][1])
        self.assertIn("canonical_digest " + "ab" * 32, output)
        self.assertIn("stage ARCHIVES: SKIP", output)
        self.assertIn("stage B POST-PREPARE: SKIP", output)

    def test_cached_root_never_invokes_producer_or_configures_manifest(self):
        make_manifest_unconfigured(self.root)
        path = os.path.join(self.root, O.FREEZE_DIR_REL, O.INTEGRITY_MANIFEST_NAME)
        before = read_bytes(path)
        self.run_quiet(O.run_prepare, self.cfg())
        self.assertNotIn(INTEGRITY_NAME, self.world.names())
        self.assertEqual(read_bytes(path), before)


class InvalidStateFailTests(OrchestratorTestCase):
    def test_bootstrap_tools_stop_propagates_its_exit_code(self):
        for code in (2, 3, 4):
            with self.subTest(code=code):
                self.bootstrap_stop = BOOTSTRAP.Stop(code, "tools refused")
                result_code, message, _output = self.run_quiet(O.stage_tools, self.cfg(), True)
                self.assertEqual(result_code, code)
                self.assertEqual(message, "TOOLS: tools refused")
        self.assertEqual(self.world.names(), [])

    def test_bootstrap_stop_during_prepare_exits_with_its_code(self):
        self.make_archives_and_extract()
        self.bootstrap_stop = BOOTSTRAP.Stop(4, "lock held by another run")
        code, message, _output = self.run_quiet(O.run_prepare, self.cfg())
        self.assertEqual(code, 4)
        self.assertEqual(message, "TOOLS: lock held by another run")
        self.assertEqual(self.world.names(), [])

    def test_reverify_fail_exits_four_without_producer(self):
        self.make_all_but_freeze()
        make_freeze_complete(self.root)
        self.world.script(FREEZE_NAME, [ChildResult(4, "FAIL(4): corrupt freeze\n", "")])
        code, _message, _output = self.run_quiet(O.run_prepare, self.cfg())
        self.assertEqual(code, 4)
        self.assertEqual(self.world.names(), [FREEZE_NAME])

    def test_corrupt_acquisition_state_exits_four_before_any_child(self):
        path = os.path.join(self.root, O.STATE_REL)
        os.makedirs(os.path.dirname(path))
        with open(path, "w", encoding="utf-8") as handle:
            handle.write("{not json")
        code, _message, _output = self.run_quiet(O.run_prepare, self.cfg())
        self.assertEqual(code, 4)
        self.assertEqual(self.world.names(), [])


class FirstGateTests(OrchestratorTestCase):
    def test_v12_gate_fail_stops_before_stage_five(self):
        self.make_archives_and_extract()
        make_first_tile_report(self.root, passed=False)
        code, message, _output = self.run_quiet(O.run_prepare, self.cfg())
        self.assertEqual(code, 2)
        self.assertIn("v1.2 gate FAIL", message)
        self.assertEqual(self.world.names(), [])


class FirstTileTests(OrchestratorTestCase):
    def setUp(self):
        super().setUp()
        make_lineage(self.root)
        self.lineage = os.path.join(self.root, O.LINEAGE_REL)

    def test_preflight_reports_fetch_then_probe_without_child(self):
        result, message, _output = self.run_quiet(O.stage_first_tile, self.cfg(), False)
        self.assertIsNone(message)
        self.assertEqual(result, (O.STATUS_RUN,
                                  "first tile fetch, then probe run for the v1.2 report"))
        self.assertEqual(self.world.names(), [])
        self.assertEqual(self.bootstrap_calls, [])

    def test_preflight_without_lineage_reports_lineage_fetch(self):
        os.remove(self.lineage)
        result, _message, _output = self.run_quiet(O.stage_first_tile, self.cfg(), False)
        self.assertEqual(result, (O.STATUS_RUN,
                                  "first tile fetch, then probe run for the v1.2 report; "
                                  "WFS lineage fetch first"))
        self.assertEqual(self.bootstrap_calls, [])

    def test_present_tile_runs_probe_only(self):
        write_bytes(os.path.join(self.root, O.FIRST_TILE_REL), b"tile")
        self.run_quiet(O.stage_first_tile, self.cfg(), True)
        self.assertEqual(self.world.names(), [PROBE_NAME])
        self.assertEqual(self.world.calls[0][1][4], "probe")
        self.assertEqual(self.bootstrap_calls, [])

    def test_missing_lineage_is_fetched_through_bootstrap(self):
        os.remove(self.lineage)
        result, message, _output = self.run_quiet(O.stage_first_tile, self.cfg(), True)
        self.assertIsNone(message)
        self.assertEqual(result[0], O.STATUS_RUN)
        self.assertEqual(self.bootstrap_calls, [("lineage", self.root)])
        self.assertEqual(self.world.names(), [PROBE_NAME, PROBE_NAME])

    def test_lineage_fetch_failure_stops_with_its_exit_code(self):
        os.remove(self.lineage)
        self.patch(BOOTSTRAP, "fetch_lineage",
                   mock.Mock(side_effect=BOOTSTRAP.Stop(3, "WFS unreachable")))
        code, message, _output = self.run_quiet(O.stage_first_tile, self.cfg(), True)
        self.assertEqual(code, 3)
        self.assertEqual(message, "FIRST TILE lineage: WFS unreachable")
        self.assertEqual(self.world.names(), [])

    def test_malformed_lineage_fails_closed(self):
        with open(self.lineage, "w", encoding="utf-8") as handle:
            handle.write("{not json")
        code, _message, _output = self.run_quiet(O.stage_first_tile, self.cfg(), True)
        self.assertEqual(code, 4)
        self.assertEqual(self.world.names(), [])

    def test_non_object_lineage_fails_closed(self):
        write_json(self.lineage, ["not", "an", "object"])
        code, _message, _output = self.run_quiet(O.stage_first_tile, self.cfg(), True)
        self.assertEqual(code, 4)
        self.assertEqual(self.world.names(), [])

    def test_empty_object_lineage_fails_closed(self):
        write_json(self.lineage, {})
        code, _message, _output = self.run_quiet(O.stage_first_tile, self.cfg(), True)
        self.assertEqual(code, 4)
        self.assertEqual(self.world.names(), [])


class ProbeCommandTests(unittest.TestCase):
    def test_cache_root_precedes_subcommand(self):
        self.assertEqual(O.probe_command("/tmp/opencode/custom", ["status"]),
                         [sys.executable, O.PROBE_SCRIPT, "--cache-root", "/tmp/opencode/custom",
                          "status"])

    def test_real_status_run_reads_only_the_given_root(self):
        os.makedirs(TMP_BASE, exist_ok=True)
        base = tempfile.mkdtemp(dir=TMP_BASE)
        self.addCleanup(shutil.rmtree, base, True)
        result = O.run_child(O.probe_command(base, ["status"]))
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(f"cache={base}", result.stdout + result.stderr)
        self.assertEqual(tree_snapshot(base), {})

    def test_orchestrator_source_has_no_adapter_or_legacy_names(self):
        with open(os.path.join(GEO_DIR, "sedan_prepare_orchestrator.py"), encoding="utf-8") as handle:
            source = handle.read()
        for forbidden in ("PROBE_ADAPTER", "PROBE_FIXED_ROOT", "CACHE_ROOT", "wfs-lineage",
                          "TOTAL  "):
            self.assertNotIn(forbidden, source)


class TransportRetryTests(OrchestratorTestCase):
    def test_tls_failure_then_success_on_retry_proceeds(self):
        self.world.script(ACQUIRE_NAME, [ChildResult(3, TLS_STDOUT, "")])
        code, _message, output = self.run_quiet(O.run_prepare, self.cfg())
        self.assertEqual(code, 0, output)
        self.assertEqual(self.world.count(ACQUIRE_NAME), 2)
        self.assertIn(EXTRACT_NAME, self.world.names())

    def test_timeout_failures_stop_at_three_total_attempts(self):
        self.world.script(ACQUIRE_NAME, [ChildResult(3, TIMEOUT_STDOUT, "")] * 3)
        code, _message, _output = self.run_quiet(O.run_prepare, self.cfg())
        self.assertEqual(code, 3)
        self.assertEqual(self.world.count(ACQUIRE_NAME), 3)
        self.assertNotIn(EXTRACT_NAME, self.world.names())

    def test_connection_reset_is_retryable(self):
        self.world.script(ACQUIRE_NAME, [ChildResult(3, RESET_STDOUT, "")] * 3)
        code, _message, _output = self.run_quiet(O.run_prepare, self.cfg())
        self.assertEqual(code, 3)
        self.assertEqual(self.world.count(ACQUIRE_NAME), 3)

    def test_auth_failure_stops_without_retry(self):
        self.world.script(ACQUIRE_NAME, [ChildResult(3, AUTH_STDOUT, "")])
        code, _message, _output = self.run_quiet(O.run_prepare, self.cfg())
        self.assertEqual(code, 3)
        self.assertEqual(self.world.count(ACQUIRE_NAME), 1)

    def test_auth_takes_priority_over_tls_retry(self):
        self.world.script(ACQUIRE_NAME, [ChildResult(3, TLS_STDOUT + AUTH_STDOUT, "")])
        code, _message, _output = self.run_quiet(O.run_prepare, self.cfg())
        self.assertEqual(code, 3)
        self.assertEqual(self.world.count(ACQUIRE_NAME), 1)

    def test_exit_two_stops_without_retry(self):
        self.world.script(ACQUIRE_NAME, [ChildResult(2, TLS_STDOUT, "")])
        code, _message, _output = self.run_quiet(O.run_prepare, self.cfg())
        self.assertEqual(code, 2)
        self.assertEqual(self.world.count(ACQUIRE_NAME), 1)

    def test_exit_four_stops_without_retry(self):
        self.world.script(ACQUIRE_NAME, [ChildResult(4, TLS_STDOUT, "")])
        code, _message, _output = self.run_quiet(O.run_prepare, self.cfg())
        self.assertEqual(code, 4)
        self.assertEqual(self.world.count(ACQUIRE_NAME), 1)


class ArchiveEvidenceTests(OrchestratorTestCase):
    def setUp(self):
        super().setUp()
        make_archives_complete(self.root)
        self.evidence = os.path.join(self.root, O.ARCHIVE_EVIDENCE_REL)

    def test_prepare_writes_header_and_disk_body_without_total(self):
        result, _message, _output = self.run_quiet(O.stage_archives, self.cfg(), True)
        self.assertEqual(result, (O.STATUS_RUN, "archive-evidence.txt written from disk SHA-256 "
                                                "and sizes"))
        content = read_bytes(self.evidence)
        header, _separator, body = content.partition(b"\n")
        self.assertTrue(header.startswith(EVIDENCE_PREFIX))
        self.assertEqual(body, expected_body())
        self.assertNotIn(b"TOTAL", content)
        self.assertEqual(self.world.names(), [])

    def test_written_evidence_is_accepted_by_acquisition_parser(self):
        self.run_quiet(O.stage_archives, self.cfg(), True)
        entries = PROBE._parse_archive_manifest(self.evidence)
        for name, content in fixture_parts():
            self.assertEqual(entries[name], (hashlib.sha256(content).hexdigest(), len(content)))

    def test_second_run_is_idempotent_and_leaves_file_untouched(self):
        self.run_quiet(O.stage_archives, self.cfg(), True)
        before = os.lstat(self.evidence)
        result, _message, _output = self.run_quiet(O.stage_archives, self.cfg(), True)
        self.assertEqual(result[0], O.STATUS_SKIP)
        after = os.lstat(self.evidence)
        self.assertEqual((before.st_ino, before.st_mtime_ns), (after.st_ino, after.st_mtime_ns))

    def test_preflight_reports_run_and_writes_nothing(self):
        result, _message, _output = self.run_quiet(O.stage_archives, self.cfg(), False)
        self.assertEqual(result[0], O.STATUS_RUN)
        self.assertFalse(os.path.lexists(self.evidence))

    def test_legacy_total_footer_is_refused_not_overwritten(self):
        write_bytes(self.evidence, EVIDENCE_PREFIX + b"2026-11-04T00:00:00.000+00:00\n"
                    + expected_body() + b"TOTAL  29  8 parts\n")
        before = read_bytes(self.evidence)
        with self.assertRaises(O.Stop) as caught:
            O.stage_archives(self.cfg(), True)
        self.assertEqual(caught.exception.code, 4)
        self.assertEqual(read_bytes(self.evidence), before)

    def test_wrong_hash_on_disk_refuses_and_writes_nothing(self):
        with open(part_file(self.root, "SUB.7z.003"), "r+b") as handle:
            handle.write(b"Z")
        with self.assertRaises(O.Stop) as caught:
            O.stage_archives(self.cfg(), True)
        self.assertEqual(caught.exception.code, 4)
        self.assertIn("differs from state", caught.exception.message)
        self.assertFalse(os.path.lexists(self.evidence))

    def test_size_mismatch_refuses(self):
        with open(part_file(self.root, "SUB.7z.002"), "ab") as handle:
            handle.write(b"!")
        with self.assertRaises(O.Stop) as caught:
            O.stage_archives(self.cfg(), True)
        self.assertEqual(caught.exception.code, 4)

    def test_missing_part_refuses(self):
        os.remove(part_file(self.root, "SUB.7z.001"))
        with self.assertRaises(O.Stop) as caught:
            O.stage_archives(self.cfg(), True)
        self.assertEqual(caught.exception.code, 4)
        self.assertIn("missing", caught.exception.message)

    def test_symlinked_part_refuses(self):
        path = part_file(self.root, "SUB.7z.001")
        os.remove(path)
        os.symlink(part_file(self.root, "SUB.7z.002"), path)
        with self.assertRaises(O.Stop) as caught:
            O.stage_archives(self.cfg(), True)
        self.assertEqual(caught.exception.code, 4)
        self.assertIn("not a regular file", caught.exception.message)

    def test_state_entry_without_sha256_refuses(self):
        state_path = os.path.join(self.root, O.STATE_REL)
        state = json.loads(read_bytes(state_path))
        del state["products"]["RES/SUB.7z.004"]["sha256"]
        write_json(state_path, state)
        with self.assertRaises(O.Stop) as caught:
            O.stage_archives(self.cfg(), True)
        self.assertEqual(caught.exception.code, 4)

    def test_existing_different_evidence_is_not_overwritten(self):
        write_bytes(self.evidence, b"stale\n")
        with self.assertRaises(O.Stop) as caught:
            O.stage_archives(self.cfg(), True)
        self.assertEqual(caught.exception.code, 4)
        self.assertEqual(read_bytes(self.evidence), b"stale\n")

    def test_evidence_symlink_refuses(self):
        os.makedirs(os.path.dirname(self.evidence), exist_ok=True)
        os.symlink(part_file(self.root, "SUB.7z.001"), self.evidence)
        with self.assertRaises(O.Stop) as caught:
            O.stage_archives(self.cfg(), True)
        self.assertEqual(caught.exception.code, 4)

    def test_write_is_no_clobber_and_accepts_identical_file(self):
        content = evidence_with_header()
        write_bytes(self.evidence, content)
        O.write_archive_evidence(self.cfg(), content)
        self.assertEqual(read_bytes(self.evidence), content)

    def test_write_refuses_differing_file_created_during_write(self):
        write_bytes(self.evidence, b"other\n")
        with self.assertRaises(O.Stop) as caught:
            O.write_archive_evidence(self.cfg(), evidence_with_header())
        self.assertEqual(caught.exception.code, 4)
        self.assertEqual(read_bytes(self.evidence), b"other\n")

    def test_write_refuses_content_without_header(self):
        with self.assertRaises(O.Stop) as caught:
            O.write_archive_evidence(self.cfg(), expected_body())
        self.assertEqual(caught.exception.code, 4)
        self.assertIn("rejected by the acquisition parser", caught.exception.message)
        self.assertFalse(os.path.lexists(self.evidence))
        self.assertEqual(os.listdir(os.path.dirname(self.evidence)), [])

    def test_write_leaves_no_temp_files(self):
        O.write_archive_evidence(self.cfg(), evidence_with_header())
        self.assertEqual(sorted(os.listdir(os.path.dirname(self.evidence))),
                         [os.path.basename(self.evidence)])


class VenvValidationTests(OrchestratorTestCase):
    def setUp(self):
        super().setUp()
        make_venv(self.root)
        self.venv_dir = os.path.join(self.root, O.VENV_DIR_REL)
        self.python = os.path.join(self.root, O.VENV_PYTHON_REL)

    def test_fake_executable_fails_identity_with_real_runner(self):
        self.patch(O, "RUN_CHILD", O.run_child)
        code, message, _output = self.run_quiet(O.stage_tools, self.cfg(), False)
        self.assertEqual(code, 4)
        self.assertIn("identity query returned 0 lines", message)

    def test_wrong_prefix_refuses(self):
        self.world.identity = "/usr\n/usr\n"
        code, message, _output = self.run_quiet(O.stage_tools, self.cfg(), False)
        self.assertEqual(code, 4)
        self.assertIn("sys.prefix", message)

    def test_base_prefix_equal_to_prefix_refuses(self):
        self.world.identity = f"{self.venv_dir}\n{self.venv_dir}\n"
        code, message, _output = self.run_quiet(O.stage_tools, self.cfg(), False)
        self.assertEqual(code, 4)
        self.assertIn("base_prefix", message)

    def test_wrong_package_version_refuses(self):
        self.world.packages = "laspy 2.6.0\nlazrs 0.8.2\nnumpy 2.5.4\n"
        code, message, _output = self.run_quiet(O.stage_tools, self.cfg(), False)
        self.assertEqual(code, 4)
        self.assertIn("laspy", message)

    def test_missing_pyvenv_cfg_refuses(self):
        os.remove(os.path.join(self.venv_dir, O.VENV_CONFIG_NAME))
        code, message, _output = self.run_quiet(O.stage_tools, self.cfg(), False)
        self.assertEqual(code, 4)
        self.assertIn("unreadable", message)

    def test_correct_packages_pass_read_only_and_missing_receipt_is_logged(self):
        before = tree_snapshot(self.venv_dir)
        result, _message, output = self.run_quiet(O.stage_tools, self.cfg(), False)
        self.assertEqual(result[0], O.STATUS_SKIP)
        self.assertIn("no ownership receipt found", output)
        self.assertEqual(tree_snapshot(self.venv_dir), before)

    def test_receipt_copy_in_venv_is_reported(self):
        write_bytes(os.path.join(self.venv_dir, O.VENV_LOCK_RECEIPT_NAME), b"receipt\n")
        _result, _message, output = self.run_quiet(O.stage_tools, self.cfg(), False)
        self.assertIn("venv ownership receipt:", output)

    def test_venv_queries_run_without_bytecode_writes(self):
        self.run_quiet(O.stage_tools, self.cfg(), False)
        self.assertTrue(self.world.venv_envs)
        for env in self.world.venv_envs:
            self.assertEqual(env["PYTHONDONTWRITEBYTECODE"], "1")

    def test_real_symlinked_venv_passes_identity(self):
        target = os.path.join(self.base, "real-venv")
        subprocess.run([sys.executable, "-m", "venv", "--without-pip", target], check=True,
                       capture_output=True)
        python = os.path.join(target, "bin", "python")
        self.assertTrue(os.path.islink(python))
        self.patch(O, "RUN_CHILD", O.run_child)
        O.check_identity(target, python)

    def test_act_with_existing_venv_delegates_to_bootstrap(self):
        before = tree_snapshot(self.venv_dir)
        result, _message, _output = self.run_quiet(O.stage_tools, self.cfg(), True)
        self.assertEqual(result, (O.STATUS_SKIP, "tools/lidar-venv verified by bootstrap_tools"))
        self.assertEqual(self.bootstrap_calls, [("tools", self.root)])
        self.assertEqual(self.world.venv_queries(), 0)
        self.assertEqual(tree_snapshot(self.venv_dir), before)

    def test_act_with_missing_venv_is_created_by_bootstrap(self):
        shutil.rmtree(self.venv_dir)
        result, _message, _output = self.run_quiet(O.stage_tools, self.cfg(), True)
        self.assertEqual(result, (O.STATUS_RUN, "tools/lidar-venv created with pinned wheels"))
        self.assertTrue(os.path.isfile(self.python))
        self.assertEqual(self.bootstrap_calls, [("tools", self.root)])

    def test_bootstrap_stop_is_propagated_with_its_code(self):
        self.bootstrap_stop = BOOTSTRAP.Stop(4, "lock held by another run")
        code, message, _output = self.run_quiet(O.stage_tools, self.cfg(), True)
        self.assertEqual(code, 4)
        self.assertEqual(message, "TOOLS: lock held by another run")


class PreflightTests(OrchestratorTestCase):
    def test_preflight_writes_nothing_and_verifies_venv_read_only(self):
        make_venv(self.root)
        self.make_archives_and_extract()
        before = tree_snapshot(self.root)
        code, _message, _output = self.run_quiet(O.run_preflight, self.cfg())
        self.assertEqual(code, 0)
        self.assertEqual(tree_snapshot(self.root), before)
        self.assertEqual(self.world.names(), [])
        self.assertEqual(self.world.venv_queries(), 2)
        self.assertEqual(self.bootstrap_calls, [])

    def test_preflight_on_empty_root_writes_nothing_and_spawns_nothing(self):
        before = tree_snapshot(self.root)
        code, _message, output = self.run_quiet(O.run_preflight, self.cfg())
        self.assertEqual(code, 0, output)
        self.assertEqual(tree_snapshot(self.root), before)
        self.assertEqual(self.world.names(), [])
        self.assertEqual(self.world.venv_queries(), 0)
        self.assertEqual(self.bootstrap_calls, [])

    def test_preflight_corrupt_state_exits_four(self):
        path = os.path.join(self.root, O.STATE_REL)
        os.makedirs(os.path.dirname(path))
        with open(path, "w", encoding="utf-8") as handle:
            handle.write("{not json")
        code, _message, _output = self.run_quiet(O.run_preflight, self.cfg())
        self.assertEqual(code, 4)
        self.assertEqual(self.world.names(), [])


class IdempotenceTests(OrchestratorTestCase):
    def test_second_prepare_skips_everything_but_freeze_reverify(self):
        self.run_quiet(O.run_prepare, self.cfg())
        before = tree_snapshot(self.root)
        self.world.calls.clear()
        code, _message, output = self.run_quiet(O.run_prepare, self.cfg())
        self.assertEqual(code, 0, output)
        self.assertEqual(self.world.names(), [FREEZE_NAME])
        self.assertEqual(self.world.venv_queries(), 0)
        self.assertEqual(tree_snapshot(self.root), before)


class ConfigureStepTests(OrchestratorTestCase):
    def test_banded_worker_file_matches_pinned_sha(self):
        worker = os.path.join(GEO_DIR, O.BANDED_WORKER_REL)
        self.assertEqual(hashlib.sha256(read_bytes(worker)).hexdigest(),
                         O.BANDED_WORKER_SHA256)

    def test_fresh_path_writes_banded_worker_block(self):
        self.run_quiet(O.run_prepare, self.cfg())
        manifest = json.loads(read_bytes(os.path.join(
            self.root, O.FREEZE_DIR_REL, O.INTEGRITY_MANIFEST_NAME)))
        self.assertEqual(manifest["worker"]["rel_path"], O.BANDED_WORKER_REL)
        self.assertEqual(manifest["worker"]["sha256"], O.BANDED_WORKER_SHA256)
        self.assertEqual(set(manifest) - {"worker"},
                         set(O.STAGE2_TILES) | {O.FIRST_TILE_NAME})

    def test_configure_logs_before_and_after_hashes(self):
        make_manifest_unconfigured(self.root)
        output_buffer = io.StringIO()
        with contextlib.redirect_stdout(output_buffer):
            O.configure_manifest(self.cfg())
        self.assertRegex(output_buffer.getvalue(),
                         r"worker block configured: manifest sha256 [0-9a-f]{64} -> [0-9a-f]{64}")

    def test_configure_refuses_worker_file_drift(self):
        make_manifest_unconfigured(self.root)
        with mock.patch.object(O, "BANDED_WORKER_SHA256", "0" * 64):
            with self.assertRaises(O.Stop) as caught:
                O.configure_manifest(self.cfg())
        self.assertEqual(caught.exception.code, 4)

    def test_configure_refuses_unexpected_tile_set(self):
        make_manifest_unconfigured(self.root)
        path = os.path.join(self.root, O.FREEZE_DIR_REL, O.INTEGRITY_MANIFEST_NAME)
        manifest = json.loads(read_bytes(path))
        manifest.pop(O.FIRST_TILE_NAME)
        write_json(path, manifest)
        with self.assertRaises(O.Stop) as caught:
            O.configure_manifest(self.cfg())
        self.assertEqual(caught.exception.code, 4)


class ParserArgvTests(unittest.TestCase):
    ROOT = "/mnt/pit2/panopticon/geo-proof/sedan"

    def test_probe_fetch_argv_parses_with_cache_root_first(self):
        args = PROBE.build_parser().parse_args(
            ["--cache-root", self.ROOT, "fetch", O.FIRST_TILE_NAME, "--first-tile",
             "--execution-brief-accepted"])
        self.assertEqual(args.cache_root, self.ROOT)
        self.assertEqual(args.command, "fetch")

    def test_probe_probe_argv_parses_with_cache_root_first(self):
        args = PROBE.build_parser().parse_args(
            ["--cache-root", self.ROOT, "probe", "--worker", "/venv/bin/python",
             O.PROBE_WORKER_PATH, "--lineage-file", os.path.join(self.ROOT, O.LINEAGE_REL)])
        self.assertEqual(args.cache_root, self.ROOT)
        self.assertEqual(args.command, "probe")

    def test_extract_argv_parses(self):
        load_module("sedan_extract", EXTRACT_NAME).build_parser().parse_args(
            [self.ROOT, "--extract"])

    def test_acquire_argv_parses(self):
        load_module("sedan_acquire", ACQUIRE_NAME).build_parser().parse_args(
            ["--download-only", self.ROOT])

    def test_stage2_argv_parses(self):
        load_module("sedan_lidar_acquire", STAGE2_NAME).build_parser().parse_args(
            ["--fetch-remaining", "--cache-root", self.ROOT,
             "--report", os.path.join(self.ROOT, O.PROBE_REPORT_REL)])

    def test_freeze_argv_parses(self):
        load_module("sedan_lidar_freeze", FREEZE_NAME).build_parser().parse_args(
            [self.ROOT, "--freeze"])

    def test_integrity_flags_defined_in_helper_source(self):
        with open(os.path.join(GEO_DIR, INTEGRITY_NAME), encoding="utf-8") as handle:
            source = handle.read()
        for flag in ("--prepare-integrity", "--cache"):
            self.assertIn(f'"{flag}"', source)


class PrepareStagesBeforeFreezeTests(OrchestratorTestCase):
    def test_only_accepted_helpers_run_before_freeze(self):
        self.run_quiet(O.run_prepare, self.cfg())
        names = self.world.names()
        before_freeze = names[:names.index(FREEZE_NAME)]
        self.assertTrue(before_freeze)
        self.assertTrue(set(before_freeze) <= ACCEPTED_BEFORE_FREEZE)


class StageSevenTests(OrchestratorTestCase):
    def setUp(self):
        super().setUp()
        self.make_all_but_freeze()
        make_freeze_complete(self.root)

    def stage(self, act=True):
        return O.stage_proof(self.cfg(), act)

    def backend_calls(self):
        return [argv for name, argv in self.world.calls if name == BACKEND_NAME]

    def effect_with(self, last_event=O.BACKEND_PHASE_COMPLETE, keys=BRIDGE_KEYS):
        def effect(root):
            make_proof_outputs(root, keys=keys)
            make_proof_receipt(root, last_event=last_event)
        return effect

    def effect_symlinked_manifest(self, root):
        make_proof_complete(root)
        real = os.path.join(self.base, "real-manifest.json")
        os.replace(os.path.join(root, O.PROOF_REL), real)
        os.symlink(real, os.path.join(root, O.PROOF_REL))

    def effect_corrupt_receipt(self, root):
        make_proof_complete(root)
        write_bytes(os.path.join(root, O.BACKEND_RECEIPT_REL), b"{not json")

    def effect_missing_manifest(self, root):
        make_proof_complete(root)
        os.remove(os.path.join(root, O.PROOF_REL))

    def assert_defect_stops(self, effect):
        self.world.backend_effect = effect
        with self.assertRaises(O.Stop) as caught:
            self.stage(True)
        self.assertEqual(caught.exception.code, 4)
        self.assertEqual(len(self.backend_calls()), 1)

    def test_verified_output_skips_without_any_child(self):
        make_proof_complete(self.root)
        for act in (True, False):
            with self.subTest(act=act):
                status, _detail = self.stage(act)
                self.assertEqual(status, O.STATUS_SKIP)
        self.assertEqual(self.world.calls, [])

    def test_preflight_reports_run_without_spawning(self):
        status, detail = self.stage(False)
        self.assertEqual(status, O.STATUS_RUN)
        self.assertIn("B prepare needed", detail)
        self.assertEqual(self.world.calls, [])

    def test_prepare_invokes_backend_with_exact_argv_and_reverifies(self):
        status, detail = self.stage(True)
        self.assertEqual(status, O.STATUS_RUN)
        self.assertIn("B prepare completed", detail)
        self.assertEqual(self.backend_calls(), [["python3", O.BACKEND_SCRIPT, self.root, "--prepare"]])

    def test_wrong_terminal_phase_fails_with_exit_four(self):
        self.assert_defect_stops(self.effect_with(last_event="preparation_started"))

    def test_missing_receipt_fails_with_exit_four(self):
        self.assert_defect_stops(make_proof_manifest)

    def test_missing_manifest_fails_with_exit_four(self):
        self.assert_defect_stops(self.effect_missing_manifest)

    def test_corrupt_receipt_fails_with_exit_four(self):
        self.assert_defect_stops(self.effect_corrupt_receipt)

    def test_manifest_with_eight_keys_fails_with_exit_four(self):
        self.assert_defect_stops(self.effect_with(keys=BRIDGE_KEYS[:8]))

    def test_manifest_with_ten_keys_fails_with_exit_four(self):
        self.assert_defect_stops(self.effect_with(keys=BRIDGE_KEYS + ("extraOne", "extraTwo")))

    def test_symlinked_manifest_fails_with_exit_four(self):
        self.assert_defect_stops(self.effect_symlinked_manifest)

    def test_child_exit_codes_propagate(self):
        for child_code, expected in ((2, 2), (3, 3), (4, 4), (1, 3)):
            with self.subTest(child_code=child_code):
                self.world.script(BACKEND_NAME, [ChildResult(child_code, "", "")])
                with self.assertRaises(O.Stop) as caught:
                    self.stage(True)
                self.assertEqual(caught.exception.code, expected)

    def test_missing_backend_script_stops_before_any_call(self):
        self.patch(O, "BACKEND_SCRIPT", os.path.join(self.base, "missing_backend.py"))
        with self.assertRaises(O.Stop) as caught:
            self.stage(True)
        self.assertEqual(caught.exception.code, 4)
        self.assertEqual(self.world.calls, [])

    def test_run_prepare_prints_follow_on_and_runs_backend_last(self):
        code, _message, output = self.run_quiet(O.run_prepare, self.cfg())
        expected = (f'PANOPTICON_GEO_PROOF_DIR={self.root} CNC_GENERALS_PATH="/home/rmessinger/'
                    f'dev/Command and Conquer Generals" dotnet test src --filter '
                    f'"FullyQualifiedName~SedanOperatorProof"')
        self.assertEqual(code, 0, output)
        self.assertIn(expected, output)
        self.assertEqual(self.world.names(), [FREEZE_NAME, BACKEND_NAME])


class ProofOutputBindingTests(OrchestratorTestCase):
    def setUp(self):
        super().setUp()
        make_proof_complete(self.root)

    def proof_path(self, label):
        return os.path.join(self.root, PROOF_OUTPUT_FILES[label])

    def edit_receipt(self, mutate):
        path = os.path.join(self.root, O.BACKEND_RECEIPT_REL)
        receipt = json.loads(read_bytes(path))
        mutate(receipt["outputs"])
        write_json(path, receipt)

    def assert_refused(self, fragment=None):
        with self.assertRaises(O.Stop) as caught:
            O.verify_proof(self.cfg())
        self.assertEqual(caught.exception.code, 4)
        if fragment is not None:
            self.assertIn(fragment, caught.exception.message)

    def test_complete_receipt_with_six_matching_outputs_skips_without_children(self):
        status, _detail = O.stage_proof(self.cfg(), True)
        self.assertEqual(status, O.STATUS_SKIP)
        self.assertEqual(self.world.calls, [])

    def test_multi_chunk_hashing_matches_receipt(self):
        self.patch(O, "OUTPUT_CHUNK_BYTES", 7)
        O.verify_proof(self.cfg())

    def test_manifest_value_mutation_with_same_keys_is_refused(self):
        payload = proof_payload()
        payload["tolerances"]["verticalMeters"] = 100
        write_json(self.proof_path("manifest"), payload)
        self.assert_refused("output manifest")

    def test_bit_flipped_dem_is_refused(self):
        path = self.proof_path("dem_tif")
        content = bytearray(read_bytes(path))
        content[0] ^= 0x01
        write_bytes(path, bytes(content))
        self.assert_refused("output dem_tif")

    def test_size_mismatch_is_refused(self):
        size = os.path.getsize(self.proof_path("texture_map"))
        self.edit_receipt(lambda outputs: outputs["texture_map"].__setitem__("size", size + 1))
        self.assert_refused("receipt records")

    def test_malformed_size_is_refused(self):
        for bad in (True, -1, "9", None):
            with self.subTest(bad=bad):
                make_proof_complete(self.root)
                self.edit_receipt(lambda outputs: outputs["sources_txt"].__setitem__("size", bad))
                self.assert_refused("malformed size")

    def test_missing_output_label_is_refused(self):
        self.edit_receipt(lambda outputs: outputs.pop("rgb_tif"))
        self.assert_refused("missing ['rgb_tif']")

    def test_extra_output_label_is_refused(self):
        self.edit_receipt(lambda outputs: outputs.__setitem__("extra", dict(outputs["rgb_tif"])))
        self.assert_refused("unexpected ['extra']")

    def test_malformed_sha_is_refused(self):
        for bad in ("zz", "A" * 64, "a" * 63, "a" * 64 + "\n", True, None):
            with self.subTest(bad=bad):
                make_proof_complete(self.root)
                self.edit_receipt(lambda outputs: outputs["dem_tif"].__setitem__("sha256", bad))
                self.assert_refused("malformed sha256")

    def test_escaping_paths_are_refused(self):
        for bad in ("/etc/passwd", "../outside.tif", os.path.join("prepared", "..", "..", "x"),
                    self.root + "-sibling/x.tif", "", "a\x00b"):
            with self.subTest(bad=bad):
                make_proof_complete(self.root)
                self.edit_receipt(lambda outputs: outputs["rgb_tif"].__setitem__("path", bad))
                self.assert_refused()

    def test_manifest_record_bound_to_another_file_is_refused(self):
        other = os.path.join(self.root, "other-manifest.json")
        write_json(other, proof_payload())
        self.edit_receipt(lambda outputs: outputs["manifest"].__setitem__("path", other))
        self.assert_refused("not the proof manifest")

    def test_missing_output_file_is_refused(self):
        os.remove(self.proof_path("rgb_tif"))
        self.assert_refused("output rgb_tif")

    def test_symlinked_output_is_refused(self):
        path = self.proof_path("rgb_tif")
        target = os.path.join(self.base, "rgb-copy.tif")
        os.replace(path, target)
        os.symlink(target, path)
        self.assert_refused("output rgb_tif")

    def test_symlinked_ancestor_directory_is_refused(self):
        prepared = os.path.join(self.root, "prepared")
        os.rename(prepared, os.path.join(self.base, "real-prepared"))
        os.symlink(os.path.join(self.base, "real-prepared"), prepared)
        self.assert_refused("symlink ancestor")

    def test_directory_in_place_of_output_is_refused(self):
        path = self.proof_path("extent_kml")
        os.remove(path)
        os.mkdir(path)
        self.assert_refused("not a regular file")

    def test_refusal_invokes_no_child_and_writes_nothing(self):
        payload = proof_payload()
        payload["tolerances"]["verticalMeters"] = 100
        write_json(self.proof_path("manifest"), payload)
        before = tree_snapshot(self.root)
        self.assert_refused("output manifest")
        self.assertEqual(self.world.calls, [])
        self.assertEqual(tree_snapshot(self.root), before)


class RootResolutionTests(unittest.TestCase):
    def test_positional_beats_env_beats_default(self):
        self.assertEqual(O.resolve_root("/a/positional", {O.ROOT_ENV: "/b/env"}),
                         ("/a/positional", "positional"))
        self.assertEqual(O.resolve_root(None, {O.ROOT_ENV: "/b/env"}),
                         ("/b/env", f"env {O.ROOT_ENV}"))
        self.assertEqual(O.resolve_root(None, {}), (O.DEFAULT_ROOT, "default"))

    def test_relative_and_filesystem_root_are_refused(self):
        with self.assertRaises(O.Stop):
            O.resolve_root("relative/path", {})
        with self.assertRaises(O.Stop):
            O.resolve_root("/", {})

    def test_empty_or_whitespace_positional_is_usage_error_without_fallback(self):
        for raw in ("", "   ", "\t"):
            with self.subTest(raw=raw):
                with self.assertRaises(O.Stop) as caught:
                    O.resolve_root(raw, {O.ROOT_ENV: "/b/env"})
                self.assertEqual(caught.exception.code, 2)

    def test_positional_with_spaces_is_accepted(self):
        self.assertEqual(O.resolve_root("/tmp/my dir", {}), ("/tmp/my dir", "positional"))


class EmptyPositionalCliTests(OrchestratorTestCase):
    def test_empty_positional_is_usage_error_without_side_effects(self):
        for raw in ("", "   "):
            with self.subTest(raw=raw):
                before = tree_snapshot(self.base)
                code, _message, output = self.run_quiet(O.main, [raw])
                self.assertEqual(code, 2)
                self.assertIn("positional root is empty", output)
                self.assertEqual(tree_snapshot(self.base), before)
                self.assertEqual(self.world.calls, [])
                self.assertEqual(self.bootstrap_calls, [])


class GuardTests(OrchestratorTestCase):
    def test_mount_without_rw_entry_refuses(self):
        with open(self.mounts, "w", encoding="utf-8") as handle:
            handle.write(f"tmpfs {self.base} tmpfs ro 0 0\n")
        with self.assertRaises(O.Stop) as caught:
            O.check_mount(self.cfg())
        self.assertEqual(caught.exception.code, 2)

    def test_symlink_component_refuses(self):
        real = os.path.join(self.base, "real")
        os.makedirs(real)
        link = os.path.join(self.base, "link")
        os.symlink(real, link)
        with self.assertRaises(O.Stop) as caught:
            O.check_ancestor(self.cfg(os.path.join(link, "sedan")))
        self.assertEqual(caught.exception.code, 2)

    def test_live_lock_refuses_overlapping_job(self):
        lock = os.path.join(self.root, O.LOCK_REL)
        os.makedirs(os.path.dirname(lock))
        with open(lock, "w", encoding="utf-8") as handle:
            handle.write("1 now\n")
        with self.assertRaises(O.Stop) as caught:
            O.check_guards(self.cfg())
        self.assertEqual(caught.exception.code, 2)

    def test_insufficient_free_space_refuses(self):
        self.patch(os, "statvfs", mock.Mock(return_value=types.SimpleNamespace(
            f_bavail=0, f_frsize=1)))
        code, _message, _output = self.run_quiet(O.check_guards, self.cfg())
        self.assertEqual(code, 2)

    def test_recover_refuses_manual_probe_lock(self):
        lock = os.path.join(self.root, O.PROBE_LOCK_REL)
        os.makedirs(os.path.dirname(lock))
        with open(lock, "w", encoding="utf-8") as handle:
            handle.write("1 now\n")
        code, message, _output = self.run_quiet(O.run_recover, self.cfg())
        self.assertEqual(code, 2)
        self.assertIn("no recover flag exists", message)


class BoundedRunnerTests(unittest.TestCase):
    def test_captures_stdout_and_exit_code(self):
        result = O.run_child([sys.executable, "-c", "print('ok')"])
        self.assertEqual(result.returncode, 0)
        self.assertEqual(result.stdout, "ok\n")

    def test_output_cap_kills_child(self):
        with self.assertRaises(O.Stop) as caught:
            O.run_child([sys.executable, "-c", "print('x' * 200)"], cap=16)
        self.assertEqual(caught.exception.code, 3)

    def test_deadline_kills_child(self):
        started = time.monotonic()
        with self.assertRaises(O.Stop) as caught:
            O.run_child([sys.executable, "-c", "import time; time.sleep(30)"], deadline=0.5)
        self.assertEqual(caught.exception.code, 3)
        self.assertLess(time.monotonic() - started, 10)

    def test_env_is_passed_to_child(self):
        result = O.run_child([sys.executable, "-c", "import os; print(os.environ['SEDAN_T'])"],
                             env={"SEDAN_T": "1"})
        self.assertEqual(result.stdout, "1\n")


class MainTests(OrchestratorTestCase):
    def test_main_default_preflight_on_cached_root_exits_zero(self):
        self.make_all_but_freeze()
        make_freeze_complete(self.root)
        with mock.patch.object(O, "MOUNT_POINT", self.base), \
                mock.patch.object(O, "PROC_MOUNTS_PATH", self.mounts), \
                contextlib.redirect_stdout(io.StringIO()):
            code = O.main([self.root])
        self.assertEqual(code, 0)
        self.assertEqual(self.world.names(), [])

    def test_main_refuses_relative_root_with_exit_two(self):
        with contextlib.redirect_stdout(io.StringIO()):
            code = O.main(["relative/root"])
        self.assertEqual(code, 2)
        self.assertEqual(self.world.names(), [])


if __name__ == "__main__":
    unittest.main()
