"""Offline unit tests for scripts/geo/sedan_lidar_probe.py (stdlib only, no network).

Conventions follow scripts/geo/tests/test_sedan_acquire.py: importlib module load,
fake HTTP responses, tempfile filesystems, injected mounts path, replaced network
seam, generated stub scripts in place of the worker and cs2cs subprocesses.
"""

import contextlib
import ast
import email.message
import hashlib
import importlib.util
import io
import json
import math
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace

_controller_path = Path(__file__).resolve().parents[1] / "sedan_lidar_probe.py"
spec = importlib.util.spec_from_file_location("sedan_lidar_probe", _controller_path)
lidar = importlib.util.module_from_spec(spec)
spec.loader.exec_module(lidar)

_worker_path = Path(__file__).resolve().parents[1] / "sedan_lidar_probe_worker.py"
_worker_spec = importlib.util.spec_from_file_location("sedan_lidar_probe_worker", _worker_path)
worker = importlib.util.module_from_spec(_worker_spec)
_worker_spec.loader.exec_module(worker)


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


class DyingResponse:
    """206 whose stream delivers one chunk, then the connection dies."""

    status = 206

    def __init__(self, headers, first_chunk):
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


def synthetic_part_digest(name):
    return hashlib.sha256(name.encode("utf-8")).hexdigest()


def synthetic_manifest_text():
    lines = ["# synthetic archive completion evidence (test fixture, sha256 at completion)"]
    for name, size in lidar.ARCHIVE_EVIDENCE_PARTS:
        lines.append(f"{synthetic_part_digest(name)}  {size}  {name}")
    return "\n".join(lines) + "\n"


def synthetic_state_products():
    products = {}
    for name, size in lidar.ARCHIVE_EVIDENCE_PARTS:
        key = ("BDORTHO/" if name.startswith("BDORTHO") else "RGEALTI/") + name
        products[key] = {
            "url": "https://data.geopf.fr/telechargement/download/" + key,
            "expected_size": size,
            "bytes_on_disk": size,
            "status": "complete",
            "mtime_ns": 0,
            "sha256": synthetic_part_digest(name),
        }
    return products


def wfs_lineage_fixture():
    """Provider-shaped synthetic lineage fixture: exact WFS key names and verbatim
    value shapes (trailing-Z xsd:dates, JSON-encoded capteur list, empty
    moe_classement_manuel, full url_npl string shape), modeled on the recorded cached
    response's coordonnees_nw == 0840-6958 feature."""
    return {
        "code_mission": "24LHDOD",
        "date_debut_acquisition": "2024-11-30Z",
        "date_fin_acquisition": "2024-11-30Z",
        "date_edition": "2026-04-29Z",
        "moa_acquisition": "IGN",
        "moe_acquisition": "Eurosense",
        "capteur": "[\"RIEGL VQ-780 II-S:H2227189\"]",
        "systeme_altimetrique": "IGN69",
        "systeme_planimetrique": "LAMB93",
        "coordonnees_nw": "0840-6958",
        "url_npl": (
            "https://data.geopf.fr/telechargement/download/LiDARHD-NUALID/"
            "NUALHD_1-0__LAZ_LAMB93_OD_2026-04-29/"
            "LHD_FXX_0840_6958_PTS_LAMB93_IGN69.copc.laz"
        ),
        "procede_classement": "IGN_AUTO_V5",
        "nombre_points": 22171803,
        "moe_classement_automatique": "IGN",
        "moe_classement_manuel": "",
        "url_mnt": "https://data.geopf.fr/wms-r/LHD_FXX_0840_6958_MNT_0M50_LAMB93_IGN69.tif",
    }


# AOI geometry recorded with system cs2cs PROJ Rel. 9.7.1 (EPSG:32631 -> EPSG:2154,
# format %.6f, SW/SE/NE/NW order of run_cs2cs), matching plan section 6.1.
REAL_CORNERS_L93 = [
    (839974.454115, 6957066.765093),
    (841075.304966, 6957065.375459),
    (841076.702265, 6958166.229496),
    (839975.840557, 6958167.623603),
]
REAL_AOI_BBOX_L93 = {
    "x_min": 839974.454115, "y_min": 6957065.375459,
    "x_max": 841076.702265, "y_max": 6958167.623603,
}
# PROVIDER-NOMINAL footprint of first tile LHD_FXX_0840_6958 (NF1 correction,
# review-lidar-firsttile-20261103T1145-0500/delta-reverify.md): derived from the cached
# WFS metadata (lidarhd-metadata-sedan.json) whose block naming is NW-corner: name row
# 6958 means y [6957000, 6958000], confirmed by the feature's url_mnt BBOX string
# (839999.75,6957000.25,840999.75,6958000.25, i.e. the 1 km block minus the 0.25 m
# raster inset) and the block polygon converted to LAMB93 (cs2cs PROJ 9.7.1 corners
# (840000,6957000) (841000,6957000) (841000,6958000) (840000,6958000)). NOT read from
# the LAS header, which stays unread until execution; gate d always gates on the OBSERVED
# header footprint at runtime and self-corrects.
PROVIDER_NOMINAL_TILE_FOOTPRINT_L93 = {
    "x_min": 840000.0, "y_min": 6957000.0, "x_max": 841000.0, "y_max": 6958000.0,
}


def intersection_cell_range(bbox, footprint, cell=lidar.COVERAGE_CELL_M):
    """Column/row ranges of the footprint inside the AOI grid anchored at the AOI bbox
    x_min/y_min; boundary partial cells included (floor on both ends)."""
    nx = max(1, int(math.ceil((bbox["x_max"] - bbox["x_min"]) / cell - 1e-6)))
    ny = max(1, int(math.ceil((bbox["y_max"] - bbox["y_min"]) / cell - 1e-6)))
    ix_min = max(footprint["x_min"], bbox["x_min"])
    ix_max = min(footprint["x_max"], bbox["x_max"])
    iy_min = max(footprint["y_min"], bbox["y_min"])
    iy_max = min(footprint["y_max"], bbox["y_max"])
    col_lo = int(math.floor((ix_min - bbox["x_min"]) / cell))
    col_hi = min(int(math.floor((ix_max - bbox["x_min"]) / cell)), nx - 1)
    row_lo = int(math.floor((iy_min - bbox["y_min"]) / cell))
    row_hi = min(int(math.floor((iy_max - bbox["y_min"]) / cell)), ny - 1)
    return col_lo, col_hi, row_lo, row_hi


def intersection_cells(coverage_cells, bbox, footprint, count=500):
    col_lo, col_hi, row_lo, row_hi = intersection_cell_range(bbox, footprint)
    for row in range(row_lo, row_hi + 1):
        for col in range(col_lo, col_hi + 1):
            coverage_cells[f"{col},{row}"] = count
    return coverage_cells


def good_measurements(tile_bytes=64):
    return {
        "measurement_version": 1,
        "tile_path": str(lidar.layout(lidar.CACHE_ROOT)["tile"]),
        "tile_bytes": tile_bytes,
        "header": {
            "las_version": "1.4",
            "point_format_id": 6,
            "header_point_count": 100,
            "scales": [0.001, 0.001, 0.001],
            "offsets": [0.0, 0.0, 0.0],
            "crs_wkt": 'PROJCS["ETRS89 / Lambert-93",GEOGCS["ETRS89",...,"AUTHORITY[\\"EPSG\\",\\"2154\\"]]',
            "global_encoding_wkt_bit": True,
            "copc_vlr_present": True,
            "vlr_count": 3,
            # Worker-reported ACTUAL scaled LAS header extent; here it matches the
            # stub cs2cs AOI exactly so the intersection covers the full tiny AOI.
            "footprint_l93": {
                "x_min": 840000.0, "y_min": 6957000.0,
                "x_max": 840010.0, "y_max": 6957010.0,
            },
        },
        "points": {
            "total": 100,
            "chunk_count": 1,
            "chunk_size_points": 2000000,
            "full_tile_class_histogram": {"2": 100},
        },
        "aoi": {
            "class_histogram": {"2": 100},
            "class2_count": 100,
            "z_min": 150.0,
            "z_max": 155.0,
            "z_median": 152.0,
            "z_bin_width_m": 0.5,
            "z_median_method": "linear interpolation inside the containing 0.5 m z bin",
            "class9_count": 5,
            "class17_count": 2,
        },
        "coverage": {
            "cell_size_m": 10.0,
            "grid_anchor_l93": [840000.0, 6957000.0],
            "class2_cell_counts": {"0,0": 500, "-1,-1": 500, "1,1": 500},
        },
        "memory": {
            "estimated_peak_bytes": 123456,
            "budget_bytes": 1073741824,
            "bytes_per_point_estimate": 128,
        },
    }


WORKER_STUB_TEMPLATE = '''#!/usr/bin/env python3
import json, sys
MARKER = {marker!r}
if MARKER:
    open(MARKER, "w").write("invoked")
request = json.load(open(sys.argv[1], encoding="utf-8"))
measurements = json.loads({payload!r})
with open(request["output_path"], "w", encoding="utf-8") as handle:
    json.dump(measurements, handle, indent=2, sort_keys=True)
    handle.write("\\n")
print("stub worker: measurements written")
'''

CS2CS_OK_STUB = '''#!/usr/bin/env python3
import sys
args = sys.argv[1:]
if args and args[0] == "--version":
    print("cs2cs Rel. 9.7.1 stub")
    sys.exit(0)
lines = [
    "840000.000000 6957000.000000 0.000000",
    "840010.000000 6957000.000000 0.000000",
    "840010.000000 6957010.000000 0.000000",
    "840000.000000 6957010.000000 0.000000",
    "839990.000000 6956990.000000 0.000000",
    "840020.000000 6956990.000000 0.000000",
    "840020.000000 6957020.000000 0.000000",
    "839990.000000 6957020.000000 0.000000",
]
sys.stdout.write("\\n".join(lines) + "\\n")
'''

CS2CS_FAILING_STUB = '''#!/usr/bin/env python3
import sys
if sys.argv[1:] and sys.argv[1] == "--version":
    print("cs2cs Rel. 9.7.1 stub")
    sys.exit(0)
sys.stderr.write("stub conversion failure\\n")
sys.exit(3)
'''

CS2CS_NOVERSION_STUB = '''#!/usr/bin/env python3
import sys
if sys.argv[1:] and sys.argv[1] == "--version":
    print("nothing that resembles a version banner")
    sys.exit(0)
lines = ["1.0 2.0 0.0"] * 8
sys.stdout.write("\\n".join(lines) + "\\n")
'''


class ProbeHarness(unittest.TestCase):
    def patch(self, name, value):
        original = getattr(lidar, name)
        setattr(lidar, name, value)
        self.addCleanup(setattr, lidar, name, original)

    def patch_acquire(self, name, value):
        original = getattr(lidar.acquire, name)
        setattr(lidar.acquire, name, value)
        self.addCleanup(setattr, lidar.acquire, name, original)

    def patch_os(self, name, wrapper):
        original = getattr(os, name)
        setattr(os, name, wrapper)
        self.addCleanup(setattr, os, name, original)

    def instrument_makedirs(self):
        calls = []
        original = os.makedirs

        def recorder(path, *args, **kwargs):
            calls.append(str(path))
            return original(path, *args, **kwargs)

        recorder.calls = calls
        self.patch_os("makedirs", recorder)
        return calls

    def instrument_subprocess_run(self):
        calls = []
        original = subprocess.run

        def recorder(*args, **kwargs):
            calls.append(args)
            raise AssertionError(f"unexpected subprocess execution: {args!r}")

        recorder.calls = calls
        setattr(subprocess, "run", recorder)
        self.addCleanup(setattr, subprocess, "run", original)
        return calls

    def tmp_dir(self):
        parent = "/tmp/opencode" if os.path.isdir("/tmp/opencode") else None
        context = tempfile.TemporaryDirectory(prefix="sedan-lidar-probe-test-", dir=parent)
        self.addCleanup(context.cleanup)
        return Path(context.name)

    def run_main(self, *argv):
        buffer = io.StringIO()
        with contextlib.redirect_stdout(buffer):
            code = lidar.main(list(argv))
        return code, buffer.getvalue()

    def fake_network(self, handler):
        calls = []

        def fake_open_tile_url(url, method="GET", headers=None):
            record = {"url": url, "method": method, "headers": dict(headers or {})}
            calls.append(record)
            return handler(record)

        fake_open_tile_url.calls = calls
        self.patch("open_tile_url", fake_open_tile_url)
        return fake_open_tile_url

    def fetch_never(self):
        def handler(record):
            raise AssertionError(f"unexpected network call: {record}")

        return self.fake_network(handler)

    def patch_statvfs(self, free_bytes):
        block = 4096
        self.patch_acquire("statvfs", lambda path: SimpleNamespace(
            f_bavail=free_bytes // block, f_frsize=block, f_bsize=block))

    def ample_space(self):
        self.patch_statvfs(1 << 45)

    def setup_cache(self, mount_options="rw,relatime"):
        root = self.tmp_dir()
        prefix = root / "mnt" / "pit2"
        prefix.mkdir(parents=True)
        self.patch_acquire("DEFAULT_PREFIX", str(prefix))
        mounts = root / "mounts.txt"
        mounts.write_text("sysfs /sys sysfs rw,nosuid,nodev,noexec,relatime 0 0\n"
                          f"/dev/sdz {prefix} ext4 {mount_options} 0 0\n")
        self.patch_acquire("PROC_MOUNTS_PATH", str(mounts))
        cache = prefix / "panopticon" / "geo-proof" / "sedan"
        self.patch("CACHE_ROOT", str(cache))
        self.ample_space()
        evidence = root / "hash-manifest.txt"
        evidence.write_text(synthetic_manifest_text())
        self.patch("DEFAULT_ARCHIVE_EVIDENCE", str(evidence))
        return {"root": root, "prefix": prefix, "cache": cache, "evidence": evidence}

    def probe_paths(self):
        return lidar.layout(lidar.CACHE_ROOT)

    def tiny_tile(self, size=64):
        tile = lidar.FIRST_TILE._replace(size=size)
        self.patch("FIRST_TILE", tile)
        return tile

    def write_probe_state(self, tile_entry):
        paths = self.probe_paths()
        state_path = Path(paths["state"])
        state_path.parent.mkdir(parents=True, exist_ok=True)
        lidar.acquire.write_state(
            str(state_path), {"version": lidar.PROBE_STATE_VERSION, "tile": tile_entry})
        return state_path

    def partial_entry(self, tile, data):
        path = Path(self.probe_paths()["tile"])
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        return {
            "url": tile.url,
            "expected_size": tile.size,
            "bytes_on_disk": len(data),
            "status": "partial",
            "mtime_ns": os.lstat(path).st_mtime_ns,
        }

    def complete_state_and_file(self, tile, data=None):
        if data is None:
            data = bytes(range(tile.size))
        path = Path(self.probe_paths()["tile"])
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        self.write_probe_state({
            "url": tile.url,
            "expected_size": tile.size,
            "bytes_on_disk": len(data),
            "status": "complete",
            "mtime_ns": os.lstat(path).st_mtime_ns,
            "sha256": hashlib.sha256(data).hexdigest(),
        })
        return path, data

    def stub_worker(self, measurements, marker=None):
        root = self.tmp_dir()
        stub = root / "worker_stub.py"
        payload = json.dumps(measurements)
        stub.write_text(WORKER_STUB_TEMPLATE.format(marker=marker, payload=payload))
        return stub

    def cs2cs_stub(self, kind="ok"):
        root = self.tmp_dir()
        stub = root / f"cs2cs_{kind}_stub.py"
        text = {
            "ok": CS2CS_OK_STUB,
            "failing": CS2CS_FAILING_STUB,
            "noversion": CS2CS_NOVERSION_STUB,
        }[kind]
        stub.write_text(text)
        stub.chmod(0o755)
        return stub

    def lineage_file(self, payload):
        root = self.tmp_dir()
        path = root / "lineage.json"
        path.write_text(json.dumps(payload))
        return path

    def read_report(self):
        return json.loads(Path(self.probe_paths()["report"]).read_text())

    def run_probe_case(self, measurements, lineage_payload=None):
        self.setup_cache()
        tile = self.tiny_tile(64)
        self.complete_state_and_file(tile)
        lineage = self.lineage_file(lineage_payload or wfs_lineage_fixture())
        stub = self.cs2cs_stub("ok")
        worker = self.stub_worker(measurements)
        code, output = self.run_main("probe", "--worker", sys.executable, str(worker),
                                     "--lineage-file", str(lineage), "--cs2cs", str(stub))
        return code, output


class DefaultPreflightTests(ProbeHarness):
    def test_no_subcommand_runs_readonly_preflight(self):
        root = self.tmp_dir()
        self.patch("CACHE_ROOT", str(root / "cache"))
        self.ample_space()
        evidence = root / "hash-manifest.txt"
        evidence.write_text(synthetic_manifest_text())
        self.patch("DEFAULT_ARCHIVE_EVIDENCE", str(evidence))
        fetch_never = self.fetch_never()
        makedirs = self.instrument_makedirs()
        code, output = self.run_main()
        self.assertEqual(lidar.EXIT_OK, code)
        self.assertEqual([], fetch_never.calls)
        self.assertEqual([], makedirs)
        self.assertIn("preflight complete", output)
        self.assertIn("nothing was written", output)
        self.assertFalse((root / "cache" / "lidarhd").exists())


class PreflightPathSafetyTests(ProbeHarness):
    def test_unsafe_cache_roots_fail_guard_without_creating_anything(self):
        root = self.tmp_dir()
        self.ample_space()
        link_target = root / "real_dir"
        link_target.mkdir()
        link = root / "symlink_dir"
        os.symlink(link_target, link)
        regular = root / "regular_file"
        regular.write_text("x")
        before = sorted(p.name for p in root.iterdir())
        cases = (
            ("relative", "sedan-cache-relative"),
            ("dotdot", str(root / ".." / "escape")),
            ("root", "/"),
            ("symlink", str(link)),
            ("regular-file", str(regular)),
        )
        for name, cache_root in cases:
            with self.subTest(case=name):
                self.patch("CACHE_ROOT", cache_root)
                self.fetch_never()
                code, output = self.run_main("preflight")
                self.assertEqual(lidar.EXIT_GUARD, code)
                self.assertIn("FAIL(2)", output)
                self.assertEqual(before, sorted(p.name for p in root.iterdir()))


class MountRealnessTests(ProbeHarness):
    def test_missing_rw_mount_entry_fails_guard(self):
        self.setup_cache(mount_options="ro,relatime")
        self.fetch_never()
        makedirs = self.instrument_makedirs()
        code, output = self.run_main("preflight")
        self.assertEqual(lidar.EXIT_GUARD, code)
        self.assertIn("rw mount", output)
        self.assertEqual([], makedirs)
        self.assertFalse(Path(self.probe_paths()["tile_dir"]).exists())
        code, _output = self.run_main("fetch", lidar.FIRST_TILE.name,
                                      "--first-tile", "--execution-brief-accepted")
        self.assertEqual(lidar.EXIT_GUARD, code)
        self.assertFalse(Path(self.probe_paths()["tile_dir"]).exists())


class GuardOrderTests(ProbeHarness):
    def test_insufficient_free_space_fails_before_mkdir_or_network(self):
        self.setup_cache()
        tile = self.tiny_tile(64)
        required = tile.size + lidar.WORKSPACE_RESERVE_BYTES
        self.patch_statvfs(required - 1000)
        fetch_never = self.fetch_never()
        makedirs = self.instrument_makedirs()
        code, output = self.run_main("fetch", tile.name,
                                     "--first-tile", "--execution-brief-accepted")
        self.assertEqual(lidar.EXIT_GUARD, code)
        self.assertIn("insufficient free space", output)
        self.assertEqual([], makedirs)
        self.assertEqual([], fetch_never.calls)
        self.assertFalse(Path(self.probe_paths()["tile_dir"]).exists())


class Stage1OnlyTests(ProbeHarness):
    def setUp(self):
        self.setup_cache()

    def test_other_pinned_tile_is_refused_without_network(self):
        fetch_never = self.fetch_never()
        makedirs = self.instrument_makedirs()
        code, output = self.run_main(
            "fetch", "LHD_FXX_0839_6959_PTS_LAMB93_IGN69.copc.laz",
            "--first-tile", "--execution-brief-accepted")
        self.assertEqual(lidar.EXIT_GUARD, code)
        self.assertIn("stage-2", output)
        self.assertIn("its own reviewed brief", output)
        self.assertEqual([], fetch_never.calls)
        self.assertEqual([], makedirs)

    def test_unknown_tile_name_is_refused(self):
        fetch_never = self.fetch_never()
        code, output = self.run_main(
            "fetch", "LHD_FXX_9999_9999_PTS_LAMB93_IGN69.copc.laz",
            "--first-tile", "--execution-brief-accepted")
        self.assertEqual(lidar.EXIT_GUARD, code)
        self.assertIn("unknown tile", output)
        self.assertEqual([], fetch_never.calls)

    def test_missing_first_tile_flag_fails_before_write_or_network(self):
        fetch_never = self.fetch_never()
        makedirs = self.instrument_makedirs()
        code, output = self.run_main("fetch", lidar.FIRST_TILE.name,
                                     "--execution-brief-accepted")
        self.assertEqual(lidar.EXIT_GUARD, code)
        self.assertIn("--first-tile", output)
        self.assertIn("execution brief", output)
        self.assertEqual([], makedirs)
        self.assertEqual([], fetch_never.calls)

    def test_missing_brief_acknowledgement_fails_before_write_or_network(self):
        fetch_never = self.fetch_never()
        makedirs = self.instrument_makedirs()
        code, output = self.run_main("fetch", lidar.FIRST_TILE.name, "--first-tile")
        self.assertEqual(lidar.EXIT_GUARD, code)
        self.assertIn("execution brief", output)
        self.assertEqual([], makedirs)
        self.assertEqual([], fetch_never.calls)

    def test_missing_archive_evidence_fails_guard(self):
        self.patch("DEFAULT_ARCHIVE_EVIDENCE", str(self.tmp_dir() / "absent-manifest.txt"))
        fetch_never = self.fetch_never()
        code, output = self.run_main(
            "fetch", lidar.FIRST_TILE.name, "--first-tile", "--execution-brief-accepted")
        self.assertEqual(lidar.EXIT_GUARD, code)
        self.assertIn("archive completion evidence missing", output)
        self.assertEqual([], fetch_never.calls)


class CollisionTests(ProbeHarness):
    def test_preexisting_tile_without_state_is_left_untouched(self):
        self.setup_cache()
        tile = self.tiny_tile(64)
        operator_bytes = b"OPERATOR INPUT DATA"
        path = Path(self.probe_paths()["tile"])
        path.parent.mkdir(parents=True)
        path.write_bytes(operator_bytes)
        fetch_never = self.fetch_never()
        code, output = self.run_main("fetch", tile.name,
                                     "--first-tile", "--execution-brief-accepted")
        self.assertEqual(lidar.EXIT_STATE, code)
        self.assertIn("collision", output)
        self.assertEqual(operator_bytes, path.read_bytes())
        self.assertFalse(Path(self.probe_paths()["state"]).exists())
        self.assertEqual([], fetch_never.calls)


class HeadCheckTests(ProbeHarness):
    def test_content_length_drift_fails_as_state(self):
        self.setup_cache()
        tile = self.tiny_tile(64)
        fetch = self.fake_network(lambda record: FakeResponse(
            200, message({"Content-Length": tile.size + 1})))
        code, output = self.run_main("head-check")
        self.assertEqual(lidar.EXIT_STATE, code)
        self.assertIn("drift", output)
        self.assertEqual(["HEAD"], [call["method"] for call in fetch.calls])
        self.assertEqual(1, len(fetch.calls))

    def test_missing_content_length_fails_as_state(self):
        self.setup_cache()
        self.tiny_tile(64)
        fetch = self.fake_network(lambda record: FakeResponse(200, message({})))
        code, output = self.run_main("head-check")
        self.assertEqual(lidar.EXIT_STATE, code)
        self.assertIn("drift", output)
        self.assertEqual(["HEAD"], [call["method"] for call in fetch.calls])

    def test_opener_uses_pinned_user_agent_and_same_host_redirect_handler(self):
        captured = {}
        original_opener = lidar.OPENER

        class RecordingOpener:
            def open(self, request, timeout=None):
                captured["url"] = request.full_url
                captured["method"] = request.get_method()
                captured["ua"] = request.headers.get("User-agent")
                return FakeResponse(200, message({"Content-Length": lidar.FIRST_TILE.size}))

        self.patch("OPENER", RecordingOpener())
        code, _output = self.run_main("head-check")
        self.assertEqual(lidar.EXIT_OK, code)
        self.assertEqual(lidar.FIRST_TILE.url, captured["url"])
        self.assertEqual("HEAD", captured["method"])
        self.assertEqual(lidar.USER_AGENT, captured["ua"])
        handlers = getattr(original_opener, "handlers", [])
        self.assertTrue(any(isinstance(item, lidar.acquire.SameHostRedirectHandler)
                            for item in handlers))


class AuthTests(ProbeHarness):
    def setUp(self):
        self.setup_cache()
        self.tile = self.tiny_tile(64)

    def test_head_check_401_fails_closed(self):
        fetch = self.fake_network(lambda record: FakeResponse(401, message({})))
        code, output = self.run_main("head-check")
        self.assertEqual(lidar.EXIT_NETWORK, code)
        self.assertIn("no IGN account", output)
        self.assertEqual(["HEAD"], [call["method"] for call in fetch.calls])
        for call in fetch.calls:
            self.assertNotIn("Authorization", call["headers"])

    def test_head_check_403_fails_closed(self):
        fetch = self.fake_network(lambda record: FakeResponse(403, message({})))
        code, output = self.run_main("head-check")
        self.assertEqual(lidar.EXIT_NETWORK, code)
        self.assertIn("no IGN account", output)

    def test_get_401_fails_closed(self):
        def handler(record):
            if record["method"] == "HEAD":
                return FakeResponse(200, message({"Content-Length": self.tile.size}))
            return FakeResponse(401, message({}))

        fetch = self.fake_network(handler)
        code, output = self.run_main("fetch", self.tile.name,
                                     "--first-tile", "--execution-brief-accepted")
        self.assertEqual(lidar.EXIT_NETWORK, code)
        self.assertIn("no IGN account", output)
        self.assertEqual(["HEAD", "GET"], [call["method"] for call in fetch.calls])


class ResumeTests(ProbeHarness):
    def setUp(self):
        self.setup_cache()
        self.tile = self.tiny_tile(64)

    def test_partial_resume_appends_once_and_completes(self):
        prefix = bytes(range(40))
        self.write_probe_state(self.partial_entry(self.tile, prefix))
        remainder = bytes(range(40, 64))

        def handler(record):
            if record["method"] == "HEAD":
                return FakeResponse(200, message({"Content-Length": self.tile.size}))
            self.assertEqual({"Range": "bytes=40-"}, record["headers"])
            return FakeResponse(206, message({"Content-Range": "bytes 40-63/64"}), remainder)

        fetch = self.fake_network(handler)
        code, output = self.run_main("fetch", self.tile.name,
                                     "--first-tile", "--execution-brief-accepted")
        self.assertEqual(lidar.EXIT_OK, code)
        path = Path(self.probe_paths()["tile"])
        self.assertEqual(bytes(range(64)), path.read_bytes())
        state = json.loads(Path(self.probe_paths()["state"]).read_text())
        entry = state["tile"]
        self.assertEqual("complete", entry["status"])
        self.assertEqual(64, entry["bytes_on_disk"])
        self.assertEqual(hashlib.sha256(bytes(range(64))).hexdigest(), entry["sha256"])
        self.assertEqual(["HEAD", "GET"], [call["method"] for call in fetch.calls])
        self.assertEqual(1, sum(1 for call in fetch.calls if call["method"] == "GET"))

    def test_short_read_records_partial_and_next_invocation_completes(self):
        prefix = bytes(range(40))
        served = bytes(range(40, 50))
        self.write_probe_state(self.partial_entry(self.tile, prefix))

        def truncated(record):
            if record["method"] == "HEAD":
                return FakeResponse(200, message({"Content-Length": self.tile.size}))
            return FakeResponse(206, message({"Content-Range": "bytes 40-63/64"}), served)

        fetch = self.fake_network(truncated)
        code, output = self.run_main("fetch", self.tile.name,
                                     "--first-tile", "--execution-brief-accepted")
        self.assertEqual(lidar.EXIT_NETWORK, code)
        self.assertIn("short read", output)
        path = Path(self.probe_paths()["tile"])
        self.assertEqual(bytes(range(50)), path.read_bytes())
        state = json.loads(Path(self.probe_paths()["state"]).read_text())
        self.assertEqual("partial", state["tile"]["status"])
        self.assertEqual(50, state["tile"]["bytes_on_disk"])
        self.assertEqual(state["tile"]["mtime_ns"], os.lstat(path).st_mtime_ns)
        self.assertEqual(["HEAD", "GET"], [call["method"] for call in fetch.calls])

        remainder = bytes(range(50, 64))

        def completing(record):
            if record["method"] == "HEAD":
                return FakeResponse(200, message({"Content-Length": self.tile.size}))
            self.assertEqual({"Range": "bytes=50-"}, record["headers"])
            return FakeResponse(206, message({"Content-Range": "bytes 50-63/64"}), remainder)

        fetch = self.fake_network(completing)
        code, output = self.run_main("fetch", self.tile.name,
                                     "--first-tile", "--execution-brief-accepted")
        self.assertEqual(lidar.EXIT_OK, code)
        self.assertEqual(bytes(range(64)), path.read_bytes())
        state = json.loads(Path(self.probe_paths()["state"]).read_text())
        self.assertEqual("complete", state["tile"]["status"])
        self.assertEqual(["HEAD", "GET"], [call["method"] for call in fetch.calls])

    def test_midstream_connection_death_preserves_partial(self):
        prefix = bytes(range(40))
        served = bytes(range(40, 50))
        self.write_probe_state(self.partial_entry(self.tile, prefix))

        def dying(record):
            if record["method"] == "HEAD":
                return FakeResponse(200, message({"Content-Length": self.tile.size}))
            return DyingResponse(message({"Content-Range": "bytes 40-63/64"}), served)

        fetch = self.fake_network(dying)
        code, output = self.run_main("fetch", self.tile.name,
                                     "--first-tile", "--execution-brief-accepted")
        self.assertEqual(lidar.EXIT_NETWORK, code)
        self.assertIn("network error", output)
        path = Path(self.probe_paths()["tile"])
        self.assertEqual(bytes(range(50)), path.read_bytes())
        state = json.loads(Path(self.probe_paths()["state"]).read_text())
        self.assertEqual("partial", state["tile"]["status"])
        self.assertEqual(50, state["tile"]["bytes_on_disk"])
        self.assertEqual(["HEAD", "GET"], [call["method"] for call in fetch.calls])

    def test_overlong_body_stops_at_cap_and_fails_as_drift(self):
        fetch = self.fake_network(lambda record: FakeResponse(
            200, message({"Content-Length": self.tile.size}), bytes(range(100))))
        code, output = self.run_main("fetch", self.tile.name,
                                     "--first-tile", "--execution-brief-accepted")
        self.assertEqual(lidar.EXIT_STATE, code)
        self.assertIn("drift", output)
        self.assertIn("stopped at the cap", output)
        path = Path(self.probe_paths()["tile"])
        self.assertEqual(64, path.stat().st_size)
        state = json.loads(Path(self.probe_paths()["state"]).read_text())
        self.assertEqual("partial", state["tile"]["status"])
        self.assertEqual(64, state["tile"]["bytes_on_disk"])

    def test_server_ignoring_range_refuses_to_append(self):
        prefix = bytes(range(40))
        self.write_probe_state(self.partial_entry(self.tile, prefix))
        fetch = self.fake_network(lambda record: FakeResponse(
            200, message({"Content-Length": self.tile.size}), bytes(range(100))))
        code, output = self.run_main("fetch", self.tile.name,
                                     "--first-tile", "--execution-brief-accepted")
        self.assertEqual(lidar.EXIT_NETWORK, code)
        self.assertIn("ignored Range", output)
        path = Path(self.probe_paths()["tile"])
        self.assertEqual(prefix, path.read_bytes())
        state = json.loads(Path(self.probe_paths()["state"]).read_text())
        self.assertEqual(40, state["tile"]["bytes_on_disk"])

    def test_completed_tile_never_redownloaded(self):
        path, data = self.complete_state_and_file(self.tile)
        before = os.stat(path).st_mtime_ns
        fetch = self.fetch_never()
        code, output = self.run_main("fetch", self.tile.name,
                                     "--first-tile", "--execution-brief-accepted")
        self.assertEqual(lidar.EXIT_OK, code)
        self.assertEqual([], fetch.calls)
        self.assertIn("no redownload", output)
        self.assertIn("sha256", output)
        self.assertEqual(before, os.stat(path).st_mtime_ns)
        self.assertFalse(Path(self.probe_paths()["lock"]).exists())


class VerifyTests(ProbeHarness):
    def test_tampered_completed_file_fails_verification(self):
        self.setup_cache()
        tile = self.tiny_tile(64)
        path, data = self.complete_state_and_file(tile)
        path.write_bytes(data[:-1] + bytes([data[-1] ^ 0xFF]))
        fetch_never = self.fetch_never()
        code, output = self.run_main("verify")
        self.assertEqual(lidar.EXIT_STATE, code)
        self.assertIn("sha256 verification", output)
        self.assertEqual([], fetch_never.calls)


class Cs2csTests(ProbeHarness):
    def test_canned_corners_build_request_and_are_recorded(self):
        self.setup_cache()
        tile = self.tiny_tile(64)
        self.complete_state_and_file(tile)
        lineage = self.lineage_file(wfs_lineage_fixture())
        stub = self.cs2cs_stub("ok")
        worker = self.stub_worker(good_measurements(tile_bytes=tile.size))
        code, output = self.run_main("probe", "--worker", sys.executable, str(worker),
                                     "--lineage-file", str(lineage), "--cs2cs", str(stub))
        self.assertEqual(lidar.EXIT_OK, code)
        request = json.loads(Path(self.probe_paths()["request"]).read_text())
        self.assertEqual(
            {"x_min": 840000.0, "y_min": 6957000.0, "x_max": 840010.0, "y_max": 6957010.0},
            request["aoi_bbox_l93"])
        self.assertEqual(
            {"x_min": 839990.0, "y_min": 6956990.0, "x_max": 840020.0, "y_max": 6957020.0},
            request["filter_bbox_l93"])
        report = self.read_report()
        self.assertEqual("cs2cs Rel. 9.7.1 stub", report["cs2cs"]["version"])
        self.assertEqual([str(stub), "-f", "%.6f", "EPSG:32631", "+to", "EPSG:2154"],
                         report["cs2cs"]["command"])

    def test_missing_cs2cs_fails_closed_before_worker(self):
        self.setup_cache()
        tile = self.tiny_tile(64)
        self.complete_state_and_file(tile)
        lineage = self.lineage_file(wfs_lineage_fixture())
        marker = self.tmp_dir() / "worker-marker.txt"
        worker = self.stub_worker(good_measurements(tile_bytes=tile.size), marker=str(marker))
        code, output = self.run_main("probe", "--worker", sys.executable, str(worker),
                                     "--lineage-file", str(lineage),
                                     "--cs2cs", str(self.tmp_dir() / "absent" / "cs2cs"))
        self.assertEqual(lidar.EXIT_GUARD, code)
        self.assertIn("failing closed", output)
        self.assertFalse(marker.exists())

    def test_nonzero_cs2cs_fails_closed_before_worker(self):
        self.setup_cache()
        tile = self.tiny_tile(64)
        self.complete_state_and_file(tile)
        lineage = self.lineage_file(wfs_lineage_fixture())
        marker = self.tmp_dir() / "worker-marker.txt"
        worker = self.stub_worker(good_measurements(tile_bytes=tile.size), marker=str(marker))
        stub = self.cs2cs_stub("failing")
        code, output = self.run_main("probe", "--worker", sys.executable, str(worker),
                                     "--lineage-file", str(lineage), "--cs2cs", str(stub))
        self.assertEqual(lidar.EXIT_GUARD, code)
        self.assertIn("exited 3", output)
        self.assertFalse(marker.exists())

    def test_cs2cs_without_version_banner_fails_closed(self):
        self.setup_cache()
        tile = self.tiny_tile(64)
        self.complete_state_and_file(tile)
        lineage = self.lineage_file(wfs_lineage_fixture())
        marker = self.tmp_dir() / "worker-marker.txt"
        worker = self.stub_worker(good_measurements(tile_bytes=tile.size), marker=str(marker))
        stub = self.cs2cs_stub("noversion")
        code, output = self.run_main("probe", "--worker", sys.executable, str(worker),
                                     "--lineage-file", str(lineage), "--cs2cs", str(stub))
        self.assertEqual(lidar.EXIT_GUARD, code)
        self.assertIn("version", output)
        self.assertFalse(marker.exists())


class ProbeGateTests(ProbeHarness):
    def test_all_good_measurements_pass_every_gate(self):
        code, output = self.run_probe_case(good_measurements())
        self.assertEqual(lidar.EXIT_OK, code)
        report = self.read_report()
        for gate in "abcdef":
            self.assertTrue(report["gates"][gate]["pass"], gate)
        self.assertTrue(report["gates_pass"])
        self.assertTrue(report["gate_only"])
        self.assertFalse(report["numeric_proof_pass"])
        self.assertIn('"gate_only": true', output)
        self.assertIn('"numeric_proof_pass": false', output)
        self.assertIn("advisory", json.dumps(report["gates"]["b"]))

    def test_crs_wkt_without_epsg_2154_fails_gate_a(self):
        measurements = good_measurements()
        measurements["header"]["crs_wkt"] = 'PROJCS["WGS 84 / UTM 31N",...,"AUTHORITY[\\"EPSG\\",\\"32631\\"]'
        code, _output = self.run_probe_case(measurements)
        self.assertEqual(lidar.EXIT_GUARD, code)
        report = self.read_report()
        self.assertFalse(report["gates"]["a"]["pass"])
        self.assertFalse(report["gates"]["a"]["crs_wkt_contains_epsg_2154"])
        self.assertFalse(report["gates_pass"])

    def test_zero_aoi_class2_fails_gates_b_and_c(self):
        measurements = good_measurements()
        measurements["aoi"].update({
            "class_histogram": {}, "class2_count": 0,
            "z_min": None, "z_max": None, "z_median": None})
        measurements["coverage"]["class2_cell_counts"] = {}
        code, _output = self.run_probe_case(measurements)
        self.assertEqual(lidar.EXIT_GUARD, code)
        report = self.read_report()
        self.assertFalse(report["gates"]["b"]["pass"])
        self.assertFalse(report["gates"]["c"]["pass"])

    def test_median_density_below_threshold_fails_gate_c(self):
        measurements = good_measurements()
        measurements["coverage"]["class2_cell_counts"] = {"0,0": 390, "0,1": 390}
        code, _output = self.run_probe_case(measurements)
        self.assertEqual(lidar.EXIT_GUARD, code)
        report = self.read_report()
        self.assertFalse(report["gates"]["c"]["pass"])
        self.assertEqual(3.9, report["gates"]["c"]["median_class2_points_per_m2"])
        self.assertTrue(report["gates"]["d"]["pass"])

    def test_missing_coverage_cells_fail_gate_d_with_hole_list(self):
        measurements = good_measurements()
        measurements["coverage"]["class2_cell_counts"] = {"-1,0": 500}
        code, _output = self.run_probe_case(measurements)
        self.assertEqual(lidar.EXIT_GUARD, code)
        report = self.read_report()
        self.assertFalse(report["gates"]["d"]["pass"])
        self.assertIn([0, 0], report["gates"]["d"]["holes"])
        self.assertEqual(1, report["gates"]["d"]["hole_count"])

    def test_z_median_90_fails_hard_band(self):
        measurements = good_measurements()
        measurements["aoi"].update({"z_min": 89.0, "z_max": 91.0, "z_median": 90.0})
        code, _output = self.run_probe_case(measurements)
        self.assertEqual(lidar.EXIT_GUARD, code)
        report = self.read_report()
        self.assertFalse(report["gates"]["b"]["pass"])
        self.assertFalse(report["gates"]["b"]["hard_band_ok"])

    def test_z_median_260_fails_hard_band(self):
        measurements = good_measurements()
        measurements["aoi"].update({"z_min": 259.0, "z_max": 261.0, "z_median": 260.0})
        code, _output = self.run_probe_case(measurements)
        self.assertEqual(lidar.EXIT_GUARD, code)
        report = self.read_report()
        self.assertFalse(report["gates"]["b"]["pass"])

    def test_z_median_within_levelling_span_is_advisory_only(self):
        measurements = good_measurements()
        measurements["aoi"].update({"z_min": 149.5, "z_max": 156.0, "z_median": 149.9})
        code, _output = self.run_probe_case(measurements)
        self.assertEqual(lidar.EXIT_OK, code)
        report = self.read_report()
        self.assertTrue(report["gates"]["b"]["pass"])
        advisory = report["gates"]["b"]["advisory"]
        self.assertTrue(advisory["within_levelling_span"])
        self.assertFalse(advisory["gating"])
        self.assertEqual([lidar.LEVELLING_MIN_M, lidar.LEVELLING_MAX_M], advisory["levelling_span_m"])

    def test_lineage_mismatch_fails_gate_e(self):
        lineage = wfs_lineage_fixture()
        lineage.update({
            "code_mission": "99LHDXX", "date_fin_acquisition": "2020-01-01Z",
            "capteur": "[\"OTHER SENSOR:1\"]", "moe_acquisition": "Other"})
        measurements = good_measurements()
        code, _output = self.run_probe_case(measurements, lineage_payload=lineage)
        self.assertEqual(lidar.EXIT_GUARD, code)
        report = self.read_report()
        self.assertFalse(report["gates"]["e"]["pass"])
        mismatched_fields = {item["field"] for item in report["gates"]["e"]["mismatches"]}
        self.assertEqual(
            {"code_mission", "date_fin_acquisition", "capteur", "moe_acquisition"},
            mismatched_fields)

    def test_zero_class9_records_weaker_fallback_explicitly(self):
        measurements = good_measurements()
        measurements["aoi"]["class9_count"] = 0
        code, _output = self.run_probe_case(measurements)
        self.assertEqual(lidar.EXIT_OK, code)
        report = self.read_report()
        self.assertTrue(report["gates"]["f"]["pass"])
        self.assertIn("weaker fallback", report["gates"]["f"]["class9_fallback"])
        self.assertIn("density/roughness", report["gates"]["f"]["class9_fallback"])


class GateDFootprintTests(ProbeHarness):
    """B1 regressions (NF1-corrected geometry, delta-reverify.md): gate d denominates
    the AOI grid cells intersecting the AOI-intersect-header-footprint rectangle
    (boundary partial cells included), with the worker-reported LAS header footprint
    validated before use. Synthetic fixtures use the provider-nominal footprint of
    first tile 0840-6958: x [840000, 841000] x y [6957000, 6958000] (NW-corner block
    naming per the cached WFS metadata), giving expected cells 9494 (cols 2..102 x
    rows 0..93) and intersection area 934624.541 m2 (93.5 ha, 76.9% of the AOI).

    COVERAGE-ONLY synthetic gate-d pass: these fixtures exercise the coverage
    arithmetic with a perfect synthetic cloud over the provider-nominal footprint;
    they are NOT a claim of a full physical pass. z, density, lineage and exclusion
    facts are separate gates and the real cloud is only measured at execution time.
    """

    def real_measurements(self, footprint=None, coverage=None):
        return {
            "header": dict(
                good_measurements()["header"],
                footprint_l93=dict(
                    PROVIDER_NOMINAL_TILE_FOOTPRINT_L93 if footprint is None else footprint),
            ),
            "coverage": {
                "cell_size_m": 10.0,
                "grid_anchor_l93": [REAL_AOI_BBOX_L93["x_min"], REAL_AOI_BBOX_L93["y_min"]],
                "class2_cell_counts": coverage if coverage is not None else intersection_cells(
                    {}, REAL_AOI_BBOX_L93, PROVIDER_NOMINAL_TILE_FOOTPRINT_L93),
            },
            "_corners": {
                "aoi_bbox_l93": dict(REAL_AOI_BBOX_L93),
                "filter_bbox_l93": dict(REAL_AOI_BBOX_L93),
            },
        }

    def gate_d(self, measurements):
        return lidar.evaluate_gate_d(measurements)

    def test_perfect_intersection_cloud_passes_expected_9494(self):
        # COVERAGE-ONLY synthetic pass over the provider-nominal footprint.
        gate = self.gate_d(self.real_measurements())
        self.assertTrue(gate["pass"])
        self.assertEqual(9494, gate["expected_cells"])
        self.assertEqual(9494, gate["covered_cells"])
        self.assertEqual(0, gate["hole_count"])
        self.assertEqual([], gate["holes"])
        self.assertEqual({"cols": [2, 102], "rows": [0, 93]}, gate["expected_cell_ranges"])
        self.assertEqual(
            {"x_min": 840000.0, "y_min": 6957065.375459, "x_max": 841000.0, "y_max": 6958000.0},
            gate["intersection_bbox_l93"])
        area = (841000.0 - 840000.0) * (6958000.0 - 6957065.375459)
        self.assertAlmostEqual(934624.541, area, delta=934624.541 * 1e-6)
        # 1e-6 relative tolerance, as the numeric tests use elsewhere.
        self.assertAlmostEqual(934624.541, gate["intersection_area_m2"],
                               delta=934624.541 * 1e-6)
        self.assertAlmostEqual(0.934624541, gate["aoi_coverage_fraction_of_footprint"],
                               delta=0.934624541 * 1e-6)
        span_x = REAL_AOI_BBOX_L93["x_max"] - REAL_AOI_BBOX_L93["x_min"]
        span_y = REAL_AOI_BBOX_L93["y_max"] - REAL_AOI_BBOX_L93["y_min"]
        aoi_grid_area = span_x * span_y          # ~1214951 m2 = the 121.5 ha AOI
        self.assertAlmostEqual(0.769, gate["intersection_area_m2"] / aoi_grid_area, places=2)
        self.assertIn("stage-2", gate["scope_note"])
        self.assertIn("header footprint", gate["scope_note"])
        self.assertIn("no whole-AOI coverage", gate["scope_note"])

    def test_cells_outside_footprint_not_counted_missing(self):
        coverage = intersection_cells({}, REAL_AOI_BBOX_L93, PROVIDER_NOMINAL_TILE_FOOTPRINT_L93)
        coverage["0,0"] = 500      # wholly outside the footprint (cols start at 2)
        coverage["110,0"] = 500    # wholly outside on the far side
        gate = self.gate_d(self.real_measurements(coverage=coverage))
        self.assertTrue(gate["pass"])
        self.assertEqual(9494, gate["expected_cells"])
        self.assertEqual(9494, gate["covered_cells"])
        self.assertEqual(0, gate["hole_count"])

    def test_true_internal_hole_fails_and_is_listed(self):
        coverage = intersection_cells({}, REAL_AOI_BBOX_L93, PROVIDER_NOMINAL_TILE_FOOTPRINT_L93)
        del coverage["50,50"]      # strictly inside the intersection (cols 2..102, rows 0..93)
        gate = self.gate_d(self.real_measurements(coverage=coverage))
        self.assertFalse(gate["pass"])
        self.assertEqual(9494, gate["expected_cells"])
        self.assertEqual(9493, gate["covered_cells"])
        self.assertEqual(1, gate["hole_count"])
        self.assertEqual([[50, 50]], gate["holes"])
        self.assertIn("contain no class-2 point", gate["reason"])

    def test_boundary_partial_cells_included_offset_footprint(self):
        # Same 5 m offset correction applied consistently to the provider-nominal
        # footprint: partial edge columns 3 and 103 (x shifted) and partial edge
        # rows 0 and 93 all stay in the expected denominator.
        shifted = {"x_min": 840005.0, "y_min": 6957005.0,
                   "x_max": 841005.0, "y_max": 6958005.0}
        measurements = self.real_measurements(footprint=shifted)
        gate = self.gate_d(measurements)
        self.assertEqual({"cols": [3, 103], "rows": [0, 93]}, gate["expected_cell_ranges"])
        self.assertEqual(9494, gate["expected_cells"])
        coverage = intersection_cells({}, REAL_AOI_BBOX_L93, shifted)
        measurements["coverage"]["class2_cell_counts"] = coverage
        self.assertTrue(self.gate_d(measurements)["pass"])
        del coverage["3,0"]        # a partially overlapped edge cell still gates
        failing = self.gate_d(measurements)
        self.assertFalse(failing["pass"])
        self.assertIn([3, 0], failing["holes"])

    def test_partial_edge_cells_arithmetic_obvious(self):
        bbox = {"x_min": 0.0, "y_min": 0.0, "x_max": 25.0, "y_max": 10.0}
        footprint = {"x_min": 5.0, "y_min": 0.0, "x_max": 15.0, "y_max": 10.0}
        measurements = {
            "header": {"footprint_l93": dict(footprint)},
            "coverage": {"cell_size_m": 10.0, "class2_cell_counts": {"0,0": 10, "1,0": 10}},
            "_corners": {"aoi_bbox_l93": dict(bbox)},
        }
        gate = self.gate_d(measurements)
        self.assertTrue(gate["pass"])
        self.assertEqual({"cols": [0, 1], "rows": [0, 0]}, gate["expected_cell_ranges"])
        self.assertEqual(2, gate["expected_cells"])
        measurements["coverage"]["class2_cell_counts"] = {"2,0": 10}  # wholly outside footprint
        failing = self.gate_d(measurements)
        self.assertFalse(failing["pass"])
        self.assertEqual([[0, 0], [1, 0]], failing["holes"])

    def test_empty_degenerate_or_nonsensical_footprint_fails_closed(self):
        cases = {
            "empty-dict": {},
            "zero-extent-x": {"x_min": 840000.0, "y_min": 6957000.0,
                              "x_max": 840000.0, "y_max": 6958000.0},
            "inverted-y": {"x_min": 840000.0, "y_min": 6958000.0,
                           "x_max": 841000.0, "y_max": 6957000.0},
            "non-numeric": {"x_min": "abc", "y_min": 6957000.0,
                            "x_max": 841000.0, "y_max": 6958000.0},
            "null-edge": {"x_min": 840000.0, "y_min": None,
                          "x_max": 841000.0, "y_max": 6958000.0},
            "non-finite": {"x_min": float("nan"), "y_min": 6957000.0,
                           "x_max": 841000.0, "y_max": 6958000.0},
            "bool-edge": {"x_min": True, "y_min": 6957000.0,
                          "x_max": 841000.0, "y_max": 6958000.0},
        }
        for label, footprint in cases.items():
            with self.subTest(case=label):
                measurements = self.real_measurements()
                measurements["header"]["footprint_l93"] = footprint
                gate = self.gate_d(measurements)
                self.assertFalse(gate["pass"])
                self.assertTrue(gate.get("reason"))

    def test_footprint_wholly_outside_or_touching_aoi_fails(self):
        cases = {
            "far-away": {"x_min": 850000.0, "y_min": 6960000.0,
                         "x_max": 851000.0, "y_max": 6961000.0},
            "touching-corner": {"x_min": 841076.702265, "y_min": 6957065.375459,
                                "x_max": 842000.0, "y_max": 6960000.0},
        }
        for label, footprint in cases.items():
            with self.subTest(case=label):
                gate = self.gate_d(self.real_measurements(footprint=footprint))
                self.assertFalse(gate["pass"])
                self.assertIn("zero intersection", gate["reason"])

    def test_one_dense_cell_plus_internal_holes_still_fails(self):
        measurements = self.real_measurements(coverage={"2,0": 900000})
        gate_d = self.gate_d(measurements)
        self.assertFalse(gate_d["pass"])
        self.assertEqual(9494, gate_d["expected_cells"])
        self.assertEqual(1, gate_d["covered_cells"])
        self.assertEqual(9493, gate_d["hole_count"])
        gate_c = lidar.evaluate_gate_c(measurements)
        self.assertTrue(gate_c["pass"])           # gate d is the backstop for gate c
        self.assertEqual(9000.0, gate_c["median_class2_points_per_m2"])

    def test_gate_c_density_semantics_unchanged_by_footprint(self):
        coverage = intersection_cells({}, REAL_AOI_BBOX_L93, PROVIDER_NOMINAL_TILE_FOOTPRINT_L93)
        coverage["0,0"] = 300     # non-empty cell wholly outside the footprint
        measurements = self.real_measurements(coverage=coverage)
        gate_c = lidar.evaluate_gate_c(measurements)
        self.assertTrue(gate_c["pass"])
        self.assertEqual(9495, gate_c["cells_counted"])
        self.assertEqual(5.0, gate_c["median_class2_points_per_m2"])
        gate_d = self.gate_d(measurements)
        self.assertTrue(gate_d["pass"])
        self.assertEqual(9494, gate_d["expected_cells"])
        self.assertEqual(9494, gate_d["covered_cells"])

    def test_worker_missing_footprint_fails_gate_d_run_level(self):
        measurements = good_measurements()
        del measurements["header"]["footprint_l93"]
        code, _output = self.run_probe_case(measurements)
        self.assertEqual(lidar.EXIT_GUARD, code)
        report = self.read_report()
        self.assertFalse(report["gates"]["d"]["pass"])
        self.assertIn("footprint_l93", report["gates"]["d"]["reason"])

    def test_run_level_stub_aoi_intersection_passes(self):
        code, _output = self.run_probe_case(good_measurements())
        self.assertEqual(lidar.EXIT_OK, code)
        report = self.read_report()
        gate_d = report["gates"]["d"]
        self.assertTrue(gate_d["pass"])
        self.assertEqual(1, gate_d["expected_cells"])
        self.assertEqual(1, gate_d["covered_cells"])
        self.assertEqual(0, gate_d["hole_count"])


class GateELineageWfsTests(ProbeHarness):
    """B2 regressions: gate e binds only to WFS-verifiable provider fields taken from
    the verbatim feature properties; the old self-echo path fails by construction."""

    def test_provider_shaped_fixture_passes(self):
        gate = lidar.evaluate_gate_e(wfs_lineage_fixture())
        self.assertTrue(gate["pass"])
        self.assertEqual([], gate["mismatches"])
        self.assertFalse(gate["documentary_gating"])
        self.assertEqual("IGN_AUTO_V5", gate["non_gating_observations"]["procede_classement"])
        self.assertEqual(22171803, gate["non_gating_observations"]["nombre_points"])
        self.assertEqual("", gate["non_gating_observations"]["moe_classement_manuel"])

    def test_old_self_echo_shape_fails_by_construction(self):
        echo = {
            "mission": "24LHDOD", "flight_date": "2024-11-30",
            "date_edition": "2026-04-29", "moa": "IGN", "moe": "Eurosense",
            "sensor": "RIEGL VQ-780 II-S:H2227189",
            "altimetric_system": "IGN69 (EPSG:5720) altitudes normales",
            "planimetric_system": "LAMB93 (EPSG:2154)",
            "licence": "Licence Ouverte 2.0 (lov2)",
            "attribution": "IGN (LiDAR HD), Licence Ouverte 2.0",
            "spec_document": "DC_LiDAR_HD_1-0.pdf",
        }
        gate = lidar.evaluate_gate_e(echo)
        self.assertFalse(gate["pass"])
        # The echo dict happens to carry date_edition with a value equal to the pinned
        # WFS expectation; every other required WFS key is absent and named.
        self.assertEqual(
            set(lidar.LINEAGE_REQUIRED_WFS_KEYS) - {"date_edition"},
            {item["field"] for item in gate["mismatches"]})
        self.assertIn("WFS", gate["reason"])

    def test_each_wrong_verbatim_field_fails_named(self):
        wrong = {
            "code_mission": "99LHDXX",
            "date_debut_acquisition": "2024-11-29Z",
            "date_fin_acquisition": "2024-12-01",
            "date_edition": "2025-01-01Z",
            "moa_acquisition": "NotIGN",
            "moe_acquisition": "NotEurosense",
            "systeme_altimetrique": "ETRS89",
            "systeme_planimetrique": "RGF93",
            "coordonnees_nw": "0839-6958",   # the cached response's features[0] tile
        }
        for field, value in wrong.items():
            with self.subTest(field=field):
                lineage = wfs_lineage_fixture()
                lineage[field] = value
                gate = lidar.evaluate_gate_e(lineage)
                self.assertFalse(gate["pass"])
                self.assertEqual([field], [item["field"] for item in gate["mismatches"]])

    def test_wrong_sensor_set_fails_and_set_equality_is_order_insensitive(self):
        lineage = wfs_lineage_fixture()
        lineage["capteur"] = "[\"RIEGL VQ-780 II-S:H2227189\",\"OTHER:1\"]"
        gate = lidar.evaluate_gate_e(lineage)
        self.assertFalse(gate["pass"])
        self.assertEqual(["capteur"], [item["field"] for item in gate["mismatches"]])
        lineage["capteur"] = "[\"RIEGL VQ-780 II-S:H2227189\",\"RIEGL VQ-780 II-S:H2227189\"]"
        gate = lidar.evaluate_gate_e(lineage)
        self.assertTrue(gate["pass"])      # documented set equality ignores order/duplicates
        self.assertEqual([], gate["mismatches"])

    def test_url_npl_tail_binding(self):
        lineage = wfs_lineage_fixture()
        lineage["url_npl"] = (
            "https://data.geopf.fr/telechargement/download/LiDARHD-NUALID/"
            "NUALHD_1-0__LAZ_LAMB93_OD_2026-04-29/"
            "LHD_FXX_0839_6958_PTS_LAMB93_IGN69.copc.laz")
        gate = lidar.evaluate_gate_e(lineage)
        self.assertFalse(gate["pass"])
        self.assertEqual(["url_npl"], [item["field"] for item in gate["mismatches"]])

    def test_date_without_trailing_z_passes_normalization(self):
        lineage = wfs_lineage_fixture()
        lineage["date_debut_acquisition"] = "2024-11-30"
        lineage["date_fin_acquisition"] = "2024-11-30"
        lineage["date_edition"] = "2026-04-29"
        self.assertTrue(lidar.evaluate_gate_e(lineage)["pass"])

    def test_malformed_date_fails_named(self):
        lineage = wfs_lineage_fixture()
        lineage["date_edition"] = "not-a-dateZ"
        gate = lidar.evaluate_gate_e(lineage)
        self.assertFalse(gate["pass"])
        self.assertEqual(["date_edition"], [item["field"] for item in gate["mismatches"]])

    def test_missing_or_null_required_key_fails_named(self):
        for field in lidar.LINEAGE_REQUIRED_WFS_KEYS:
            with self.subTest(field=field):
                lineage = wfs_lineage_fixture()
                del lineage[field]
                gate = lidar.evaluate_gate_e(lineage)
                self.assertFalse(gate["pass"])
                self.assertEqual([field], [item["field"] for item in gate["mismatches"]])
                lineage = wfs_lineage_fixture()
                lineage[field] = None
                gate = lidar.evaluate_gate_e(lineage)
                self.assertFalse(gate["pass"])
                self.assertEqual([field], [item["field"] for item in gate["mismatches"]])

    def test_empty_capteur_list_fails(self):
        lineage = wfs_lineage_fixture()
        lineage["capteur"] = "[]"
        gate = lidar.evaluate_gate_e(lineage)
        self.assertFalse(gate["pass"])
        self.assertEqual(["capteur"], [item["field"] for item in gate["mismatches"]])

    def test_bare_string_capteur_is_wrong_shape(self):
        lineage = wfs_lineage_fixture()
        lineage["capteur"] = "RIEGL VQ-780 II-S:H2227189"
        gate = lidar.evaluate_gate_e(lineage)
        self.assertFalse(gate["pass"])
        self.assertEqual(["capteur"], [item["field"] for item in gate["mismatches"]])

    def test_documentary_constants_present_and_never_gating(self):
        lineage = wfs_lineage_fixture()
        lineage["licence"] = "SELF-ASSERTED"      # not a WFS field: ignored entirely
        gate = lidar.evaluate_gate_e(lineage)
        self.assertTrue(gate["pass"])
        self.assertEqual("Licence Ouverte 2.0 (lov2)", gate["documentary"]["licence"]["value"])
        self.assertEqual("IGN (LiDAR HD), Licence Ouverte 2.0",
                         gate["documentary"]["attribution"]["value"])
        self.assertEqual("DC_LiDAR_HD_1-0.pdf", gate["documentary"]["spec_document"]["value"])
        self.assertIn("lov2", gate["documentary"]["licence"]["source"])
        self.assertFalse(gate["documentary_gating"])


class ArchiveEvidenceTests(ProbeHarness):
    """B3 regressions: the archive evidence guard binds the 8 pinned part identities
    through a bounded strict parser of the actual 9-line manifest format."""

    def setUp(self):
        self.setup_cache()

    def capture_log(self):
        lines = []

        def recorder(message):
            lines.append(message)

        self.patch("log", recorder)
        return lines

    def write_manifest(self, text):
        path = self.tmp_dir() / "hash-manifest.txt"
        path.write_text(text)
        return path

    def write_state(self, products):
        state_path = Path(lidar.archive_state_path())
        state_path.parent.mkdir(parents=True, exist_ok=True)
        state_path.write_text(json.dumps({"version": 1, "products": products}) + "\n")
        return state_path

    def assert_hard_failure(self, path, fragment):
        with self.assertRaises(lidar.acquire.Failure) as caught:
            lidar.require_archive_evidence(str(path), hard=True)
        self.assertEqual(lidar.EXIT_GUARD, caught.exception.code)
        self.assertIn(fragment, caught.exception.message)
        return caught.exception

    def test_real_shaped_synthetic_manifest_passes_hard(self):
        lines = self.capture_log()
        path = self.write_manifest(synthetic_manifest_text())
        self.assertTrue(lidar.require_archive_evidence(str(path), hard=True))
        self.assertTrue(any("not publisher-authenticated" in line for line in lines))
        self.assertTrue(any("manifest-only verification" in line for line in lines))

    def test_state_crosscheck_matching_passes_and_is_recorded(self):
        self.write_state(synthetic_state_products())
        lines = self.capture_log()
        path = self.write_manifest(synthetic_manifest_text())
        self.assertTrue(lidar.require_archive_evidence(str(path), hard=True))
        self.assertTrue(any("cross-check passed" in line for line in lines))

    def test_state_crosscheck_disagreeing_digest_fails(self):
        products = synthetic_state_products()
        first_key = sorted(products)[0]
        products[first_key]["sha256"] = "0" * 64
        self.write_state(products)
        path = self.write_manifest(synthetic_manifest_text())
        self.assert_hard_failure(path, "disagree")

    def test_state_crosscheck_incomplete_status_fails(self):
        products = synthetic_state_products()
        products[sorted(products)[0]]["status"] = "partial"
        self.write_state(products)
        path = self.write_manifest(synthetic_manifest_text())
        self.assert_hard_failure(path, "status")

    def test_state_crosscheck_empty_products_fails(self):
        self.write_state({})
        path = self.write_manifest(synthetic_manifest_text())
        self.assert_hard_failure(path, "no unambiguous completed owned record")

    def test_one_byte_stub_fails_hard_and_warns_soft(self):
        path = self.write_manifest("x")
        self.assert_hard_failure(path, "comment header")
        lines = self.capture_log()
        self.assertFalse(lidar.require_archive_evidence(str(path), hard=False))
        self.assertTrue(any("WARNING" in line for line in lines))

    def test_duplicate_part_line_fails(self):
        lines = synthetic_manifest_text().splitlines()
        lines.append(lines[1])
        self.assert_hard_failure(self.write_manifest("\n".join(lines) + "\n"), "more than once")

    def test_seven_parts_fail(self):
        lines = synthetic_manifest_text().splitlines()
        del lines[1]
        self.assert_hard_failure(
            self.write_manifest("\n".join(lines) + "\n"), "missing pinned part")

    def test_wrong_name_and_wrong_edition_fail(self):
        # A renamed line both drops the pinned name and adds an unpinned one; the
        # parser reports presence first, identity second. Either message fails closed.
        lines = synthetic_manifest_text().splitlines()
        lines[1] = lines[1].replace(".7z.001", ".7z.009")
        self.assert_hard_failure(
            self.write_manifest("\n".join(lines) + "\n"), "pinned part")
        lines = synthetic_manifest_text().splitlines()
        lines[1] = lines[1].replace("_D008_", "_D009_")
        self.assert_hard_failure(
            self.write_manifest("\n".join(lines) + "\n"), "pinned part")

    def test_wrong_size_fails(self):
        lines = synthetic_manifest_text().splitlines()
        lines[1] = lines[1].replace("4294967296", "4294967295")
        self.assert_hard_failure(self.write_manifest("\n".join(lines) + "\n"), "wrong size")

    def test_non_hex_and_wrong_length_hash_fail(self):
        lines = synthetic_manifest_text().splitlines()
        lines[1] = lines[1].upper()
        self.assert_hard_failure(self.write_manifest("\n".join(lines) + "\n"), "malformed line")
        lines = synthetic_manifest_text().splitlines()
        lines[1] = lines[1][1:]                # 63-char digest
        self.assert_hard_failure(self.write_manifest("\n".join(lines) + "\n"), "malformed line")
        lines = synthetic_manifest_text().splitlines()
        fields = lines[1].split("  ")
        fields[0] = "g" + fields[0][1:]        # non-hex character
        lines[1] = "  ".join(fields)
        self.assert_hard_failure(self.write_manifest("\n".join(lines) + "\n"), "malformed line")

    def test_extra_data_line_fails(self):
        lines = synthetic_manifest_text().splitlines()
        lines.append(f"{'a' * 64}  1  OPERATOR_EXTRA.7z.001")
        self.assert_hard_failure(
            self.write_manifest("\n".join(lines) + "\n"), "unpinned part name")

    def test_empty_file_fails(self):
        self.assert_hard_failure(self.write_manifest(""), "empty")

    def test_missing_comment_header_fails(self):
        lines = synthetic_manifest_text().splitlines()[1:]
        self.assert_hard_failure(
            self.write_manifest("\n".join(lines) + "\n"), "comment header")

    def test_oversized_and_malformed_inputs_fail_bounded(self):
        self.assert_hard_failure(
            self.write_manifest("#" + "x" * 9000), "byte parse bound")
        self.assert_hard_failure(
            self.write_manifest("#" + "x" * 600), "char line bound")
        self.assert_hard_failure(
            self.write_manifest("\n".join(["#comment"] * 40) + "\n"), "line bound")

    def test_symlinked_evidence_path_fails(self):
        real = self.write_manifest(synthetic_manifest_text())
        link = self.tmp_dir() / "link-manifest.txt"
        os.symlink(real, link)
        self.assert_hard_failure(link, "symlinked component")
        target_dir = self.tmp_dir() / "real-dir"
        target_dir.mkdir()
        (target_dir / "hash-manifest.txt").write_text(synthetic_manifest_text())
        dir_link = self.tmp_dir() / "dir-link"
        os.symlink(target_dir, dir_link)
        self.assert_hard_failure(dir_link / "hash-manifest.txt", "symlinked component")

    def test_fetch_evidence_failure_precedes_mkdir_and_network(self):
        self.patch("DEFAULT_ARCHIVE_EVIDENCE", str(self.write_manifest("x")))
        tile = self.tiny_tile(64)
        fetch_never = self.fetch_never()
        makedirs = self.instrument_makedirs()
        code, output = self.run_main("fetch", tile.name,
                                     "--first-tile", "--execution-brief-accepted")
        self.assertEqual(lidar.EXIT_GUARD, code)
        self.assertIn("archive completion evidence", output)
        self.assertEqual([], makedirs)
        self.assertEqual([], fetch_never.calls)
        self.assertFalse(Path(self.probe_paths()["tile_dir"]).exists())


class WorkerScanHeaderFootprintTests(unittest.TestCase):
    """B1 producer side: scan_header reports the ACTUAL scaled LAS header extent."""

    def fake_reader(self, x_min=840000.25, y_min=6958000.5, x_max=840999.75, y_max=6958999.25):
        return SimpleNamespace(header=SimpleNamespace(
            version=SimpleNamespace(major=1, minor=4),
            point_format=SimpleNamespace(id=6),
            point_count=42,
            scales=(0.001, 0.001, 0.001),
            offsets=(0.0, 0.0, 0.0),
            global_encoding=SimpleNamespace(wkt=True),
            vlrs=[], evlrs=[],
            x_min=x_min, y_min=y_min, x_max=x_max, y_max=y_max,
        ))

    def test_scan_header_reports_actual_scaled_header_extent(self):
        info = worker.scan_header(self.fake_reader())
        self.assertEqual(
            {"x_min": 840000.25, "y_min": 6958000.5,
             "x_max": 840999.75, "y_max": 6958999.25},
            info["footprint_l93"])

    def test_scan_header_footprint_follows_header_not_any_filename(self):
        first = worker.scan_header(self.fake_reader())["footprint_l93"]
        second = worker.scan_header(
            self.fake_reader(x_min=100.0, y_min=200.0, x_max=300.0, y_max=400.0)
        )["footprint_l93"]
        self.assertNotEqual(first, second)
        self.assertEqual(
            [100.0, 200.0, 300.0, 400.0],
            [second[key] for key in ("x_min", "y_min", "x_max", "y_max")])


class StdlibPurityTests(ProbeHarness):
    def test_controller_imports_are_stdlib_only(self):
        source = Path(_controller_path).read_text()
        tree = ast.parse(source)
        roots = set()
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                roots.update(alias.name.split(".")[0] for alias in node.names)
            elif isinstance(node, ast.ImportFrom) and node.module:
                roots.add(node.module.split(".")[0])
        non_stdlib = {root for root in roots if root not in sys.stdlib_module_names}
        self.assertEqual(set(), non_stdlib)
        for banned in ("laspy", "lazrs", "numpy"):
            self.assertNotIn(banned, roots)


class IsolationTests(ProbeHarness):
    def test_fetch_and_probe_leave_acquire_paths_unchanged(self):
        cache = self.setup_cache()["cache"]
        acquire_state = cache / "state" / "state.json"
        acquire_state.parent.mkdir(parents=True)
        acquire_state.write_text(json.dumps(
            {"version": 1, "products": synthetic_state_products()}) + "\n")
        archive = cache / "archives" / "RGEALTI" / "RGEALTI_2-0_1M_TIF_LAMB93-IGN69_D008_2019-10-14" / "part.7z.001"
        archive.parent.mkdir(parents=True)
        archive.write_bytes(b"ACQUIRE ARCHIVE DATA")

        def snapshot(path):
            return path.read_bytes(), path.stat().st_mtime_ns

        seeded = {
            acquire_state: snapshot(acquire_state),
            archive: snapshot(archive),
        }
        tile = self.tiny_tile(64)
        stub = self.cs2cs_stub("ok")
        lineage = self.lineage_file(wfs_lineage_fixture())
        worker = self.stub_worker(good_measurements(tile_bytes=tile.size))

        def handler(record):
            if record["method"] == "HEAD":
                return FakeResponse(200, message({"Content-Length": tile.size}))
            return FakeResponse(200, message({"Content-Length": tile.size}), bytes(range(tile.size)))

        self.fake_network(handler)
        code, _output = self.run_main("fetch", tile.name,
                                      "--first-tile", "--execution-brief-accepted")
        self.assertEqual(lidar.EXIT_OK, code)
        code, _output = self.run_main("probe", "--worker", sys.executable, str(worker),
                                      "--lineage-file", str(lineage), "--cs2cs", str(stub))
        self.assertEqual(lidar.EXIT_OK, code)
        for path, expected in seeded.items():
            self.assertEqual(expected, snapshot(path))
        self.assertEqual({"state.json"}, {p.name for p in (cache / "state").iterdir()})
        self.assertEqual({"part.7z.001"}, {p.name for p in archive.parent.iterdir()})


class PlanToolsTests(ProbeHarness):
    def test_plan_tools_prints_recipe_and_executes_nothing(self):
        paths = self.setup_cache()
        makedirs = self.instrument_makedirs()
        subprocess_calls = self.instrument_subprocess_run()
        code, output = self.run_main("plan-tools")
        self.assertEqual(lidar.EXIT_OK, code)
        self.assertIn("python3 -m venv", output)
        self.assertIn(str(paths["cache"] / "tools" / "lidar-venv"), output)
        self.assertIn("tools-manifest.json", output)
        self.assertIn("laspy", output)
        self.assertIn("lazrs", output)
        self.assertIn("numpy", output)
        self.assertIn("ONLY inside the venv", output)
        self.assertEqual([], makedirs)
        self.assertEqual([], subprocess_calls)
        self.assertFalse((paths["cache"] / "tools").exists())


class StatusTests(ProbeHarness):
    def test_status_prints_state_summary(self):
        self.setup_cache()
        tile = self.tiny_tile(64)
        self.complete_state_and_file(tile)
        code, output = self.run_main("status")
        self.assertEqual(lidar.EXIT_OK, code)
        self.assertIn("status=complete", output)
        self.assertIn("64/64", output)
        self.assertIn("lock: absent", output)


class WorkerInterpreterTests(ProbeHarness):
    def test_worker_under_system_python_exits_2_without_installing(self):
        worker_path = Path(__file__).resolve().parents[1] / "sedan_lidar_probe_worker.py"
        run = subprocess.run(
            [sys.executable, str(worker_path), "/nonexistent/request.json"],
            capture_output=True, text=True, timeout=60)
        self.assertEqual(2, run.returncode)
        combined = run.stdout + run.stderr
        self.assertTrue("isolated" in combined or "cannot read request" in combined)
        self.assertNotIn("pip install", combined)


@unittest.skipUnless(
    importlib.util.find_spec("laspy") and importlib.util.find_spec("numpy"),
    "requires the owned venv laspy/numpy (stdlib CI skips truthfully)")
class RealLaspySmokeTests(unittest.TestCase):
    """Real-laspy regression over the ACTUAL worker measurement path: a synthetic LAS
    with known ground truth (5000 points: 3000x class 2, 1000x class 9, 1000x class 17;
    integer-scaled coordinates near L93 values) is measured end-to-end through
    worker.main and the real LasReader.chunk_iterator. No fakes, no signature checks.
    Skipped truthfully under stdlib CI; runs only under the isolated lidar venv.
    """

    AOI_BBOX_L93 = {
        "x_min": 840000.0, "y_min": 6957000.0, "x_max": 840100.0, "y_max": 6957100.0,
    }
    FILTER_BBOX_L93 = {
        "x_min": 839990.0, "y_min": 6956990.0, "x_max": 840110.0, "y_max": 6957110.0,
    }
    CHUNK_SIZE_POINTS = 1500
    # (classification, count, group origin x/y, point stride m, z cycle): each group
    # spans at most 9 x stride < 10 m, so all its points land in the origin's cell.
    GROUPS = (
        (2, 1500, (840000.0, 6957000.0), 1.0, (150.0, 150.25)),
        (2, 1500, (840050.0, 6957070.0), 0.5, (150.5, 150.75)),
        (9, 1000, (840020.0, 6957030.0), 0.1, (150.0, 150.25, 150.5, 150.75)),
        (17, 1000, (840080.0, 6957090.0), 0.5, (150.5, 151.0)),
    )

    def tmp_dir(self):
        parent = "/tmp/opencode" if os.path.isdir("/tmp/opencode") else None
        context = tempfile.TemporaryDirectory(prefix="sedan-lidar-smoke-", dir=parent)
        self.addCleanup(context.cleanup)
        return Path(context.name)

    def file_descriptor_count(self):
        return len(os.listdir("/proc/self/fd")) if os.path.isdir("/proc/self/fd") else -1

    def write_synthetic_las(self, root):
        import laspy
        import numpy

        xs, ys, zs, classes = [], [], [], []
        for classification, count, (x0, y0), stride, z_cycle in self.GROUPS:
            for i in range(count):
                xs.append(x0 + (i % 10) * stride)
                ys.append(y0 + ((i // 10) % 10) * stride)
                zs.append(z_cycle[i % len(z_cycle)])
                classes.append(classification)
        las = laspy.create(file_version="1.4", point_format=6)
        las.header.scales = [0.001, 0.001, 0.001]
        las.header.offsets = [840000.0, 6957000.0, 150.0]
        las.x = numpy.asarray(xs, dtype=numpy.float64)
        las.y = numpy.asarray(ys, dtype=numpy.float64)
        las.z = numpy.asarray(zs, dtype=numpy.float64)
        las.classification = numpy.asarray(classes, dtype=numpy.int64)
        path = root / "synthetic-smoke.las"
        las.write(str(path))
        return path

    def expected_ground_cells(self):
        cells = {}
        x_anchor = self.AOI_BBOX_L93["x_min"]
        y_anchor = self.AOI_BBOX_L93["y_min"]
        for classification, count, (x0, y0), _stride, _z_cycle in self.GROUPS:
            if classification != worker.CLASS_GROUND:
                continue
            key = (
                f"{int(math.floor((x0 - x_anchor) / lidar.COVERAGE_CELL_M))},"
                f"{int(math.floor((y0 - y_anchor) / lidar.COVERAGE_CELL_M))}"
            )
            cells[key] = cells.get(key, 0) + count
        return cells

    def build_request(self, root, las_path):
        request = {
            "tile_path": str(las_path),
            "aoi_bbox_l93": dict(self.AOI_BBOX_L93),
            "filter_bbox_l93": dict(self.FILTER_BBOX_L93),
            "cell_size_m": lidar.COVERAGE_CELL_M,
            "chunk_size_points": self.CHUNK_SIZE_POINTS,
            "memory_budget_bytes": lidar.WORKER_MEMORY_BUDGET_BYTES,
            "output_path": str(root / "measurements.json"),
        }
        path = root / "worker-request.json"
        path.write_text(json.dumps(request), encoding="utf-8")
        return path, root / "measurements.json"

    def test_worker_measures_synthetic_las_end_to_end_with_real_chunk_iterator(self):
        root = self.tmp_dir()
        las_path = self.write_synthetic_las(root)
        request_path, output_path = self.build_request(root, las_path)
        fds_before = self.file_descriptor_count()
        buffer = io.StringIO()
        with contextlib.redirect_stdout(buffer):
            code = worker.main(["sedan_lidar_probe_worker.py", str(request_path)])
        self.assertEqual(worker.EXIT_OK, code)
        self.assertEqual(fds_before, self.file_descriptor_count())
        self.assertTrue(output_path.is_file())
        measurements = json.loads(output_path.read_text(encoding="utf-8"))
        self.assertEqual(1, measurements["measurement_version"])
        points = measurements["points"]
        self.assertEqual(5000, points["total"])
        self.assertEqual(self.CHUNK_SIZE_POINTS, points["chunk_size_points"])
        self.assertGreaterEqual(points["chunk_count"], 2)
        self.assertEqual(
            {"2": 3000, "9": 1000, "17": 1000}, points["full_tile_class_histogram"])
        aoi = measurements["aoi"]
        self.assertEqual({"2": 3000, "9": 1000, "17": 1000}, aoi["class_histogram"])
        self.assertEqual(3000, aoi["class2_count"])
        self.assertEqual(1000, aoi["class9_count"])
        self.assertEqual(1000, aoi["class17_count"])
        self.assertEqual(
            self.expected_ground_cells(), measurements["coverage"]["class2_cell_counts"])
        self.assertAlmostEqual(150.0, aoi["z_min"], places=6)
        self.assertAlmostEqual(150.75, aoi["z_max"], places=6)
        header = measurements["header"]
        self.assertEqual("1.4", header["las_version"])
        self.assertEqual(6, header["point_format_id"])
        self.assertEqual(5000, header["header_point_count"])
        footprint = header["footprint_l93"]
        for edge in footprint.values():
            self.assertTrue(math.isfinite(edge))
        self.assertLess(footprint["x_min"], footprint["x_max"])
        self.assertLess(footprint["y_min"], footprint["y_max"])
        self.assertAlmostEqual(840000.0, footprint["x_min"], places=6)
        self.assertAlmostEqual(6957000.0, footprint["y_min"], places=6)
        self.assertAlmostEqual(840084.5, footprint["x_max"], places=6)
        self.assertAlmostEqual(6957094.5, footprint["y_max"], places=6)

    def test_real_reader_chunk_iterator_takes_points_per_iteration(self):
        import laspy

        root = self.tmp_dir()
        las_path = self.write_synthetic_las(root)
        with laspy.open(str(las_path)) as reader:
            chunks = list(reader.chunk_iterator(points_per_iteration=self.CHUNK_SIZE_POINTS))
        self.assertEqual([1500, 1500, 1500, 500], [len(chunk) for chunk in chunks])


if __name__ == "__main__":
    unittest.main()
