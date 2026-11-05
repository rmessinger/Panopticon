"""Offline unit tests for scripts/geo/sedan_lidar_probe.py (stdlib only, no network).

Conventions follow scripts/geo/tests/test_sedan_acquire.py: importlib module load,
fake HTTP responses, tempfile filesystems, injected mounts path, replaced network
seam, generated stub scripts in place of the worker and cs2cs subprocesses.

Protocol v1.2 additions: the report keys are gates a, b, c_v1_2, c_historical,
d_v1_2, e, f with report_version 2 written to probe-report-v1_2.json (the recorded
v1.1 probe-report.json stays immutable). The v1.1 gate evaluators
(lidar.evaluate_gate_c / lidar.evaluate_gate_d) are HISTORICAL: their tests are
relabeled HISTORICAL and keep their original expectations, calling the evaluators
directly. The v1.2 required synthetic cases live in V12RequiredCaseTests; the
real-laspy synthetic smokes live in RealLaspyCandidateSmokeTests (venv-only).
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

_GEO_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(_GEO_DIR))

import sedan_lidar_select  # noqa: E402  (stdlib-only canonical selection module)

_controller_path = _GEO_DIR / "sedan_lidar_probe.py"
spec = importlib.util.spec_from_file_location("sedan_lidar_probe", _controller_path)
lidar = importlib.util.module_from_spec(spec)
sys.modules["sedan_lidar_probe"] = lidar
spec.loader.exec_module(lidar)

_worker_path = _GEO_DIR / "sedan_lidar_probe_worker.py"
_worker_spec = importlib.util.spec_from_file_location("sedan_lidar_probe_worker", _worker_path)
worker = importlib.util.module_from_spec(_worker_spec)
sys.modules["sedan_lidar_probe_worker"] = worker
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

# Filter (AOI+50 m) corners and the canonical lattice SW anchor, recorded with
# system cs2cs PROJ Rel. 9.7.1 (EPSG:32631 -> EPSG:2154, %.6f, same batch order as
# run_cs2cs: AOI corners, filter corners, lattice anchor last).
REAL_FILTER_CORNERS_L93 = [
    (839924.352442, 6957016.789226),
    (841125.280106, 6957015.273483),
    (841126.804970, 6958216.204938),
    (839925.864385, 6958217.726004),
]
LATTICE_ANCHOR_L93 = (839974.454115, 6957066.765093)

# v1.2 fixture geometry: header footprint containing the fixture candidate windows
# (all windows lie fully inside; the controller re-verifies containment), and
# candidate lattice cells on the canonical 3.048 m lattice spanning 4 full-AOI
# sectors (s00, s01, s11, s22, s20) with slope strata S1H/S2/S3 represented.
FIXTURE_FOOTPRINT_L93 = {
    "x_min": 840090.0, "y_min": 6957180.0, "x_max": 840990.0, "y_max": 6958080.0,
}
# (row, col, sigma_fit, slope_deg): 5 S1H primaries eligible across 4 sectors
# pairwise >= 100 m, plus one S2 (5 deg) and one S3 (10 deg) cell; the frozen
# preferred ladder 3 S1H + 1 S2 + 1 S3 selects primaries A, B, C, F, G with
# alternates D, E (all pairwise spacings below are >= 100 m by construction).
FIXTURE_CANDIDATES = [
    (40, 40, 0.003, 0.5),      # A s00 S1H (primary 1)
    (40, 140, 0.0033, 0.6),    # B s01 S1H (primary 2)
    (60, 300, 0.0042, 0.9),    # E s20 S1H (alternate 2)
    (70, 70, 0.01, 5.0),       # F s00 S2 (primary 4)
    (200, 200, 0.0036, 0.7),   # C s11 S1H (primary 3)
    (240, 240, 0.02, 10.0),    # G s11 S3 (primary 5)
    (330, 330, 0.0039, 0.8),   # D s22 S1H (alternate 1)
]
# Single-sector fixture: dense eligible block entirely inside canonical sector s00
# (UTM offsets 30..310 m < the 366.67 m sector third), pairwise >= 100 m selectable.
SINGLE_SECTOR_CANDIDATES = [
    (10, 10, 0.0030, 0.5),
    (10, 70, 0.0032, 0.5),
    (40, 40, 0.0034, 0.5),
    (70, 10, 0.0036, 0.5),
    (70, 70, 0.0038, 0.5),
    (100, 100, 0.0040, 0.5),
]
SINGLE_SECTOR_FOOTPRINT_L93 = {
    "x_min": 840000.0, "y_min": 6957090.0, "x_max": 840320.0, "y_max": 6957380.0,
}


def candidate_block(records, footprint):
    """Build a worker-shaped candidates block (parallel arrays, row-major) for the
    given (row, col, sigma_fit, slope_deg) records. Flat-plane analytic values:
    n2 121 window points, truth 150.0, zero tilt, zero curvature, no water/bridge
    proximity."""
    arrays = {name: [] for name in (
        "col", "row", "n2", "a", "b", "c", "sigma_fit", "slope_deg", "curv_bound",
        "curv_unavailable", "fit_unavailable",
        "mix2", "mix3", "mix4", "mix5", "mix6", "mix9", "mix17", "e2_dist", "e3_dist",
    )}
    for row, col, sigma, slope in sorted(records, key=lambda item: (item[0], item[1])):
        arrays["col"].append(col)
        arrays["row"].append(row)
        arrays["n2"].append(121)
        arrays["a"].append(150.0)
        arrays["b"].append(0.0)
        arrays["c"].append(0.0)
        arrays["sigma_fit"].append(sigma)
        arrays["slope_deg"].append(slope)
        arrays["curv_bound"].append(0.0)
        arrays["curv_unavailable"].append(False)
        arrays["fit_unavailable"].append(False)
        arrays["mix2"].append(121)
        for key in ("mix3", "mix4", "mix5", "mix6", "mix9", "mix17"):
            arrays[key].append(0)
        arrays["e2_dist"].append(None)
        arrays["e3_dist"].append(None)
    return {
        "candidate_measurement_version": 1,
        "lattice_origin_l93": [LATTICE_ANCHOR_L93[0], LATTICE_ANCHOR_L93[1]],
        "aoi_sw_l93": [LATTICE_ANCHOR_L93[0], LATTICE_ANCHOR_L93[1]],
        "spacing_m": 3.048,
        "lattice_cols": 361,
        "lattice_rows": 361,
        "window_r": 2.5,
        "e23_radius": 3.048,
        "footprint_observed_l93": dict(footprint),
        "footprint_request_l93": dict(footprint),
        "retention_margin_m": 3.048,
        "bin_size_m": 2.5,
        "row_order": "row-major (row ascending, then col ascending)",
        "containment": "window circle fully inside the observed header footprint",
        "points_retained": 121 * len(records),
        "cells_enumerated": 361 * 361,
        "cells_contained": len(records),
        "cells_skipped_containment": 361 * 361 - len(records),
        "columns": list(arrays.keys()),
        "arrays": arrays,
    }


def candidates_block_fixture():
    return candidate_block(FIXTURE_CANDIDATES, FIXTURE_FOOTPRINT_L93)


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


def good_measurements(tile_bytes=64, footprint=None, coverage=None, candidates=None):
    """v1.2 run-level fixture. The header footprint is sized to contain the fixture
    candidate windows; the coverage fills the expected AOI-intersect-footprint cells
    (median 5.0 pts/m2 over the non-empty expected population) minus cell 50,50 so
    a documented hole rides along with a PASSING d_v1_2 (required case 1); the
    candidates block carries 7 contained canonical candidates across 4 sectors."""
    footprint = dict(FIXTURE_FOOTPRINT_L93) if footprint is None else footprint
    if coverage is None:
        coverage = intersection_cells({}, REAL_AOI_BBOX_L93, footprint)
        coverage.pop("50,50", None)
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
            # Worker-reported ACTUAL scaled LAS header extent; sized to contain the
            # fixture candidate windows (containment is re-verified by the controller).
            "footprint_l93": footprint,
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
            "grid_anchor_l93": [REAL_AOI_BBOX_L93["x_min"], REAL_AOI_BBOX_L93["y_min"]],
            "class2_cell_counts": coverage,
        },
        "candidates": (
            candidates_block_fixture() if candidates is None else candidates
        ),
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
    "839974.454115 6957066.765093 0.000000",
    "841075.304966 6957065.375459 0.000000",
    "841076.702265 6958166.229496 0.000000",
    "839975.840557 6958167.623603 0.000000",
    "839924.352442 6957016.789226 0.000000",
    "841125.280106 6957015.273483 0.000000",
    "841126.804970 6958216.204938 0.000000",
    "839925.864385 6958217.726004 0.000000",
    "839974.454115 6957066.765093 0.000000",
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
        return json.loads(Path(self.probe_paths()["report_v1_2"]).read_text())

    def read_report_v1_1_path_absent(self):
        return not Path(self.probe_paths()["report"]).exists()

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
        self.assertEqual(REAL_AOI_BBOX_L93, request["aoi_bbox_l93"])
        self.assertEqual(
            {"x_min": 839924.352442, "y_min": 6957015.273483,
             "x_max": 841126.804970, "y_max": 6958217.726004},
            request["filter_bbox_l93"])
        # v1.2: the batched cs2cs call also converts the lattice SW anchor, which the
        # request carries as both the record origin and the row/col anchoring point.
        self.assertEqual([LATTICE_ANCHOR_L93[0], LATTICE_ANCHOR_L93[1]],
                         request["candidates"]["lattice_origin_l93"])
        self.assertEqual(request["candidates"]["aoi_sw_l93"],
                         request["candidates"]["lattice_origin_l93"])
        self.assertEqual(361, request["candidates"]["lattice_cols"])
        self.assertEqual(361, request["candidates"]["lattice_rows"])
        self.assertEqual(3.048, request["candidates"]["spacing_m"])
        report = self.read_report()
        self.assertEqual("cs2cs Rel. 9.7.1 stub", report["cs2cs"]["version"])
        self.assertEqual([str(stub), "-f", "%.6f", "EPSG:32631", "+to", "EPSG:2154"],
                         report["cs2cs"]["command"])
        self.assertEqual(9, report["cs2cs"]["batched_point_count"])
        self.assertIn("easting northing", report["cs2cs"]["axis_order"])
        self.assertEqual([LATTICE_ANCHOR_L93[0], LATTICE_ANCHOR_L93[1]],
                         report["cs2cs"]["lattice_anchor_l93"])

    def test_batched_single_invocation_for_corners_and_anchor(self):
        calls = []
        original = lidar.run_cs2cs_points

        def recorder(cs2cs_path, points):
            calls.append(list(points))
            return original(cs2cs_path, points)

        self.setup_cache()
        tile = self.tiny_tile(64)
        self.complete_state_and_file(tile)
        lineage = self.lineage_file(wfs_lineage_fixture())
        stub = self.cs2cs_stub("ok")
        worker = self.stub_worker(good_measurements(tile_bytes=tile.size))
        self.patch("run_cs2cs_points", recorder)
        code, _output = self.run_main("probe", "--worker", sys.executable, str(worker),
                                      "--lineage-file", str(lineage), "--cs2cs", str(stub))
        self.assertEqual(lidar.EXIT_OK, code)
        self.assertEqual(1, len(calls))
        self.assertEqual(9, len(calls[0]))
        self.assertEqual(lidar.LATTICE_ORIGIN_32631, calls[0][8])

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
    """v1.2 run-level gate tests: gates a, b, c_v1_2, d_v1_2, e, f with
    c_historical recorded non-gating; report_version 2; the v1.1 report filename
    stays immutable."""

    def test_all_good_measurements_pass_every_gate(self):
        code, output = self.run_probe_case(good_measurements())
        self.assertEqual(lidar.EXIT_OK, code)
        report = self.read_report()
        self.assertEqual(2, report["report_version"])
        self.assertEqual(lidar.PROTOCOL_VERSION_L1_2, report["protocol_version"])
        for gate in ("a", "b", "c_v1_2", "d_v1_2", "e", "f"):
            self.assertTrue(report["gates"][gate]["pass"], gate)
        self.assertFalse(report["gates"]["c_historical"]["gating"])
        self.assertTrue(report["gates_pass"])
        self.assertTrue(report["gate_only"])
        self.assertTrue(report["stage_1_preliminary"])
        self.assertFalse(report["numeric_proof_pass"])
        self.assertIn('"gate_only": true', output)
        self.assertIn('"numeric_proof_pass": false', output)
        self.assertIn('"stage_1_preliminary": true', output)
        self.assertIn("advisory", json.dumps(report["gates"]["b"]))
        # The recorded v1.1 report filename stays IMMUTABLE: nothing writes it.
        self.assertTrue(self.read_report_v1_1_path_absent())
        # Lattice/projection provenance recorded (required report keys).
        self.assertEqual(
            [lidar.LATTICE_ORIGIN_32631[0], lidar.LATTICE_ORIGIN_32631[1]],
            report["lattice_v1_2"]["origin_utm"])
        self.assertEqual([LATTICE_ANCHOR_L93[0], LATTICE_ANCHOR_L93[1]],
                         report["lattice_v1_2"]["origin_l93"])
        self.assertEqual("EPSG:32631", report["lattice_v1_2"]["crs"])
        self.assertEqual(3.048, report["lattice_v1_2"]["spacing_m"])
        self.assertEqual(128881, report["lattice_v1_2"]["canonical_cells"])
        self.assertIn("Rel. 9.7.1", report["projection_v1_2"]["version"])
        self.assertEqual(9, report["projection_v1_2"]["batched_point_count"])
        self.assertEqual([LATTICE_ANCHOR_L93[0], LATTICE_ANCHOR_L93[1]],
                         report["projection_v1_2"]["lattice_anchor_l93"])
        self.assertEqual(dict(sedan_lidar_select.THRESHOLDS),
                         report["lattice_v1_2"]["thresholds"])

    def test_crs_wkt_without_epsg_2154_fails_gate_a(self):
        measurements = good_measurements()
        measurements["header"]["crs_wkt"] = 'PROJCS["WGS 84 / UTM 31N",...,"AUTHORITY[\\"EPSG\\",\\"32631\\"]'
        code, _output = self.run_probe_case(measurements)
        self.assertEqual(lidar.EXIT_GUARD, code)
        report = self.read_report()
        self.assertFalse(report["gates"]["a"]["pass"])
        self.assertFalse(report["gates"]["a"]["crs_wkt_contains_epsg_2154"])
        self.assertFalse(report["gates_pass"])

    def test_zero_aoi_class2_fails_gates_b_and_c_v1_2(self):
        measurements = good_measurements()
        measurements["aoi"].update({
            "class_histogram": {}, "class2_count": 0,
            "z_min": None, "z_max": None, "z_median": None})
        measurements["coverage"]["class2_cell_counts"] = {}
        code, _output = self.run_probe_case(measurements)
        self.assertEqual(lidar.EXIT_GUARD, code)
        report = self.read_report()
        self.assertFalse(report["gates"]["b"]["pass"])
        self.assertFalse(report["gates"]["c_v1_2"]["pass"])
        self.assertIsNone(report["gates"]["c_v1_2"]["median_class2_points_per_m2"])

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


class HISTORICAL_V11GateCEvaluatorTests(unittest.TestCase):
    """HISTORICAL (protocol v1.1 section 5 item c, filter-window metric retained):
    the old evaluate_gate_c stays in the code as the historical v1.1 evaluator; its
    expectations are unchanged and still passing. It is NOT called by the v1.2
    report path (v1.2 gates on c_v1_2; the recorded v1.1 execution measured median
    11.61 pts/m2 over 9351 filter-window cells and stands unchanged)."""

    def test_median_density_below_threshold_fails_gate_c(self):
        measurements = {"coverage": {"class2_cell_counts": {"0,0": 390, "0,1": 390}}}
        gate = lidar.evaluate_gate_c(measurements)
        self.assertFalse(gate["pass"])
        self.assertEqual(3.9, gate["median_class2_points_per_m2"])

    def test_median_density_at_or_above_threshold_passes(self):
        measurements = {"coverage": {"class2_cell_counts": {"0,0": 400, "0,1": 500}}}
        gate = lidar.evaluate_gate_c(measurements)
        self.assertTrue(gate["pass"])
        self.assertEqual(4.5, gate["median_class2_points_per_m2"])

    def test_no_cells_fails_gate_c(self):
        gate = lidar.evaluate_gate_c({"coverage": {"class2_cell_counts": {}}})
        self.assertFalse(gate["pass"])
        self.assertIsNone(gate["median_class2_points_per_m2"])

    def test_historical_provenance_record_is_unchanged(self):
        record = lidar.evaluate_gate_c_historical()
        self.assertFalse(record["gating"])
        self.assertEqual({"median_class2_points_per_m2": 11.61, "cells_counted": 9351},
                         record["recorded_v1_1"])
        self.assertIn("v1.1 measurement, not a new one", record["note"])


class V12RequiredCaseTests(ProbeHarness):
    """The 7 REQUIRED synthetic cases of the approved v1.2 package (proposal-v1_2.md
    section 5), all offline/synthetic through the stub worker."""

    def probe_case1_report(self, **overrides):
        measurements = good_measurements(**overrides)
        code, _output = self.run_probe_case(measurements)
        return code, self.read_report()

    def test_case1_ground_with_documented_holes_passes_and_holes_verbatim(self):
        # Sufficient distributed ground (7 candidates across 4 sectors, preferred
        # ladder satisfied) AND a documented coverage hole (cell 50,50): the v1.2
        # availability gate PASSES while the hole list is preserved verbatim.
        code, report = self.probe_case1_report()
        self.assertEqual(lidar.EXIT_OK, code)
        gates = report["gates"]
        self.assertTrue(gates["c_v1_2"]["pass"])
        self.assertTrue(gates["d_v1_2"]["pass"])
        self.assertEqual([[50, 50]], gates["d_v1_2"]["holes"])
        self.assertEqual(1, gates["d_v1_2"]["hole_count"])
        availability = gates["d_v1_2"]["availability"]
        self.assertTrue(availability["stage_1_preliminary"])
        self.assertEqual(7, availability["canonical_candidates"])
        self.assertEqual(5, len(availability["primaries"]))
        self.assertEqual(["s00", "s01", "s11"], availability["sectors_covered"])
        self.assertTrue(report["gates_pass"])

    def test_case2_dense_single_sector_stops(self):
        # All eligible cells inside one canonical sector: the 3-sector floor is
        # unmet -> STOP_AND_ESCALATE, never waived.
        measurements = good_measurements(
            footprint=dict(SINGLE_SECTOR_FOOTPRINT_L93),
            coverage=intersection_cells({}, REAL_AOI_BBOX_L93, SINGLE_SECTOR_FOOTPRINT_L93),
            candidates=candidate_block(
                SINGLE_SECTOR_CANDIDATES, SINGLE_SECTOR_FOOTPRINT_L93),
        )
        code, _output = self.run_probe_case(measurements)
        report = self.read_report()
        self.assertEqual(lidar.EXIT_GUARD, code)
        gate_d = report["gates"]["d_v1_2"]
        self.assertFalse(gate_d["pass"])
        self.assertTrue(gate_d["stop_and_escalate"])
        self.assertIn("STOP_AND_ESCALATE", gate_d["reason"])
        self.assertEqual("sectors_below_floor", gate_d["stop"]["reason"])
        self.assertEqual(["s00"], gate_d["stop"]["details"]["covered"])
        self.assertFalse(report["gates_pass"])
        availability = gate_d["availability"]
        self.assertEqual(6, availability["eligible_count"])
        self.assertEqual({}, availability["exclusion_counts"])
        self.assertEqual(6, availability["eligible_count"])
        self.assertTrue(report["gates"]["c_v1_2"]["pass"])

    def test_case3_all_hole_zero_eligible_stops(self):
        # Zero-eligible scene: every candidate window is E1 (no class-2 points);
        # availability STOPs and escalates.
        block = candidate_block(SINGLE_SECTOR_CANDIDATES, SINGLE_SECTOR_FOOTPRINT_L93)
        for key in ("n2", "a", "b", "c", "sigma_fit", "slope_deg", "curv_bound",
                    "mix2", "mix3", "mix4", "mix5", "mix6", "mix9", "mix17"):
            block["arrays"][key] = [None if key in ("a", "b", "c", "sigma_fit",
                                                    "slope_deg", "curv_bound") else 0
                                    for _ in block["arrays"][key]]
        measurements = good_measurements(
            footprint=dict(SINGLE_SECTOR_FOOTPRINT_L93),
            coverage=intersection_cells({}, REAL_AOI_BBOX_L93, SINGLE_SECTOR_FOOTPRINT_L93),
            candidates=block,
        )
        code, _output = self.run_probe_case(measurements)
        report = self.read_report()
        self.assertEqual(lidar.EXIT_GUARD, code)
        gate_d = report["gates"]["d_v1_2"]
        self.assertFalse(gate_d["pass"])
        self.assertTrue(gate_d["stop_and_escalate"])
        self.assertEqual("eligible_below_floor", gate_d["stop"]["reason"])
        self.assertEqual({"E1": 6}, gate_d["availability"]["exclusion_counts"])
        self.assertEqual(0, gate_d["availability"]["eligible_count"])
        self.assertFalse(report["gates_pass"])

    def test_case4_truncated_footprint_leaving_under_floor_stops_no_relaxation(self):
        # Only four eligible cells survive: below the 5-primary floor -> STOP; no
        # reduced continuation, no per-cell exceptions anywhere in the report.
        measurements = good_measurements(
            candidates=candidate_block(FIXTURE_CANDIDATES[:4], FIXTURE_FOOTPRINT_L93))
        code, _output = self.run_probe_case(measurements)
        report = self.read_report()
        self.assertEqual(lidar.EXIT_GUARD, code)
        gate_d = report["gates"]["d_v1_2"]
        self.assertFalse(gate_d["pass"])
        self.assertTrue(gate_d["stop_and_escalate"])
        self.assertEqual("eligible_below_floor", gate_d["stop"]["reason"])
        self.assertEqual(4, gate_d["stop"]["details"]["selected"])
        self.assertEqual(4, gate_d["stop"]["details"]["eligible"])
        self.assertFalse(report["gates_pass"])
        # No reduced continuation: no partial selection is emitted below the floor.
        self.assertNotIn("primaries", gate_d["availability"])
        self.assertNotIn("alternates", gate_d["availability"])

    def test_case4_integrity_fail_still_fails_gate_a_and_nothing_waives_it(self):
        measurements = good_measurements()
        measurements["header"]["crs_wkt"] = 'PROJCS["bad",AUTHORITY["EPSG","32631"]]'
        code, _output = self.run_probe_case(measurements)
        report = self.read_report()
        self.assertEqual(lidar.EXIT_GUARD, code)
        self.assertFalse(report["gates"]["a"]["pass"])
        self.assertTrue(report["gates"]["d_v1_2"]["pass"])
        self.assertFalse(report["gates_pass"])

    def test_case5_stage1_preliminary_never_final_and_roles_distinct(self):
        code, report = self.probe_case1_report()
        self.assertEqual(lidar.EXIT_OK, code)
        self.assertTrue(report["stage_1_preliminary"])
        availability = report["gates"]["d_v1_2"]["availability"]
        primary_ids = [record["id"] for record in availability["primaries"]]
        alternate_ids = [record["id"] for record in availability["alternates"]]
        self.assertEqual(["r40c40", "r40c140", "r200c200", "r70c70", "r240c240"], primary_ids)
        self.assertEqual(["r330c330", "r60c300"], alternate_ids)
        self.assertEqual({"primary"}, {record["role"] for record in availability["primaries"]})
        self.assertEqual({"alternate"}, {record["role"] for record in availability["alternates"]})
        # The final-freeze API refuses all-tiles-freeze construction from the
        # stage-1 preliminary output.
        request = {"candidates": {"lattice_origin_l93": [LATTICE_ANCHOR_L93[0],
                                                         LATTICE_ANCHOR_L93[1]]}}
        records, _meta, problem = lidar.build_candidate_records(
            candidates_block_fixture(), request, dict(REAL_AOI_BBOX_L93))
        self.assertIsNone(problem)
        selection = lidar.groundtruth.select(records, stage_1_preliminary=True)
        self.assertTrue(selection.stage_1_preliminary)
        with self.assertRaises(lidar.groundtruth.ProtocolStop) as ctx:
            lidar.groundtruth.freeze(records, selection, str(self.tmp_dir() / "refused"))
        self.assertEqual("stage1_preliminary_never_final", ctx.exception.reason)

    def test_case6_thresholds_byte_equal_to_protocol_values(self):
        self.assertEqual(
            dict(lidar.groundtruth.THRESHOLDS),
            {
                "window_r_m": 2.5,
                "half_footprint_m": 1.524,
                "lattice_spacing_m": 3.048,
                "e1_n2_min": 12,
                "e2_e3_radius_m": 3.048,
                "e4_sigma_fit_max_m": 0.08,
                "e5_slope_max_deg": 12.0,
                "e6_seam_border_m": 3.048,
                "e7_curvature_max_m": 0.05,
                "huber_constant": 1.345,
                "mad_scale": 1.4826,
                "huber_scale_floor_m": 1e-9,
                "s1_hard_sigma_max_m": 0.03,
                "stratum_s1_max_deg": 4.0,
                "stratum_s2_max_deg": 8.0,
                "min_primaries": 5,
                "min_sectors": 3,
                "min_pairwise_spacing_m": 100.0,
                "n_alternates": 2,
                "ladder_preferred": [["S1H", 3], ["S2", 1], ["S3", 1]],
                "ladder_s3_empty": [["S1H", 4], ["S2", 1]],
                "ladder_s2_empty": [["S1H", 4], ["S3", 1]],
                "ladder_all_empty": [["S1H", 5]],
                "seam_lines_l93": [["E", 840000.0], ["E", 841000.0], ["N", 6958000.0]],
                "aoi_centre_utm": [640391.997, 5507318.179],
                "aoi_half_m": 550.0,
                "lattice_origin_utm": [639841.997, 5506768.179],
                "one_cell_margin": 1,
                "exclusion_semantics": "strict: below/above/within; at-threshold eligible",
            })

    def test_case7_traceability_candidates_exclusions_holes(self):
        code, report = self.probe_case1_report()
        self.assertEqual(lidar.EXIT_OK, code)
        gate_d = report["gates"]["d_v1_2"]
        availability = gate_d["availability"]
        # All candidates: canonical count equals the contained count of the block.
        self.assertEqual(7, availability["canonical_candidates"])
        self.assertEqual(7, gate_d["availability"]["cells_contained"])
        # All fired exclusions counted per rule (none in this fixture; a fired case
        # is asserted in test_case3_all_hole_zero_eligible_stops).
        self.assertEqual({}, availability["exclusion_counts"])
        # All holes present verbatim.
        self.assertEqual([[50, 50]], gate_d["holes"])
        # Deterministic selection-table hashes recorded.
        for key in ("candidates_sha256", "selection_sha256"):
            digest = availability["tables"][key]
            self.assertRegex(digest, r"^[0-9a-f]{64}$")
        # Per-selected criteria values recorded.
        first = availability["primaries"][0]
        self.assertEqual(121, first["n2"])
        self.assertEqual(0.003, first["sigma_fit"])
        self.assertEqual(0.5, first["slope_deg"])
        self.assertEqual("s00", first["sector"])
        self.assertEqual("S1H", first["pool"])
        self.assertEqual(1, first["rank"])
        self.assertAlmostEqual(first["density"], 121 / (math.pi * 2.5 * 2.5), delta=1e-9)


class V12PopulationTests(ProbeHarness):
    """Gate c v1.2 population semantics: the expected-intersection frame only."""

    def full_coverage(self, footprint=None):
        footprint = dict(FIXTURE_FOOTPRINT_L93) if footprint is None else footprint
        return intersection_cells({}, REAL_AOI_BBOX_L93, footprint)

    def test_population_excludes_out_of_grid_and_margin_keys(self):
        measurements = good_measurements()
        coverage = measurements["coverage"]["class2_cell_counts"]
        coverage["0,0"] = 500        # outside the expected ranges (cols/rows 11..101)
        coverage["-1,-1"] = 300      # probe-only +50 m margin cell (out of grid)
        coverage["200,200"] = 50     # beyond the expected set
        code, _output = self.run_probe_case(measurements)
        self.assertEqual(lidar.EXIT_OK, code)
        gate_c = self.read_report()["gates"]["c_v1_2"]
        self.assertTrue(gate_c["pass"])
        self.assertEqual(3, gate_c["excluded_out_of_grid_keys"])
        self.assertEqual(8280, gate_c["population_count"])
        self.assertEqual(8281, gate_c["expected_cells"])
        self.assertEqual(5.0, gate_c["median_class2_points_per_m2"])
        self.assertIn("575 out-of-grid", gate_c["excludes_out_of_grid_and_margin"])
        self.assertIn("+50 m", gate_c["excludes_out_of_grid_and_margin"])

    def test_empty_cells_excluded_from_median_population(self):
        # Estimator semantics: median over NON-EMPTY expected cells only; the hole
        # cell (50,50) carries no key and is counted by gate d, not gate c.
        measurements = good_measurements()
        measurements["coverage"]["class2_cell_counts"]["11,11"] = 100
        code, _output = self.run_probe_case(measurements)
        self.assertEqual(lidar.EXIT_OK, code)
        gate_c = self.read_report()["gates"]["c_v1_2"]
        self.assertTrue(gate_c["pass"])
        self.assertEqual(8280, gate_c["population_count"])
        self.assertEqual(5.0, gate_c["median_class2_points_per_m2"])
        gate_d = self.read_report()["gates"]["d_v1_2"]
        self.assertEqual([[50, 50]], gate_d["holes"])

    def test_below_threshold_population_fails_c_v1_2(self):
        measurements = good_measurements()
        coverage = measurements["coverage"]["class2_cell_counts"]
        for key in list(coverage):
            coverage[key] = 390
        code, _output = self.run_probe_case(measurements)
        self.assertEqual(lidar.EXIT_GUARD, code)
        gate_c = self.read_report()["gates"]["c_v1_2"]
        self.assertFalse(gate_c["pass"])
        self.assertEqual(3.9, gate_c["median_class2_points_per_m2"])
        self.assertTrue(self.read_report()["gates"]["d_v1_2"]["pass"])


class WorkerCandidateModeOfflineTests(unittest.TestCase):
    """Offline tests of the worker's candidate-mode pure helpers (stdlib import)."""

    def test_live_set_plan_accepts_real_first_tile_under_frozen_budget(self):
        # Cached WFS metadata header count for block 0840-6958 (worst case: every
        # point retained and class 2), provider-nominal footprint + E2/E3 margin
        # spans, planned 361x361 lattice: the optimized live set must fit the
        # FROZEN 1 GiB budget or the whole design is restructured, never the
        # budget raised.
        span_x = (PROVIDER_NOMINAL_TILE_FOOTPRINT_L93["x_max"]
                  - PROVIDER_NOMINAL_TILE_FOOTPRINT_L93["x_min"]) + 2 * 3.048
        span_y = (PROVIDER_NOMINAL_TILE_FOOTPRINT_L93["y_max"]
                  - PROVIDER_NOMINAL_TILE_FOOTPRINT_L93["y_min"]) + 2 * 3.048
        ok, peak, lines = worker.candidate_live_set_plan(
            22_171_803, 2_000_000, 361 * 361, span_x, span_y,
            lidar.WORKER_MEMORY_BUDGET_BYTES)
        self.assertTrue(ok)
        self.assertLess(peak, lidar.WORKER_MEMORY_BUDGET_BYTES)
        table = "\n".join(lines)
        for phase in ("accumulate", "index_", "candidate_evaluation", "serialization",
                      "report_write"):
            self.assertIn(phase, table)
        for token in ("float32", "uint8", "float64", "int32", "int64"):
            self.assertIn(token, table)
        self.assertIn("int32 bin-code span check", table)

    def test_live_set_plan_boundary_accepted_refused_exactly_at_threshold(self):
        span = 1000.0
        ok, peak, _lines = worker.candidate_live_set_plan(
            1_000_000, 100_000, 1000, span, span, lidar.WORKER_MEMORY_BUDGET_BYTES)
        self.assertTrue(ok)
        ok_at, peak_at, _lines = worker.candidate_live_set_plan(
            1_000_000, 100_000, 1000, span, span, peak)
        self.assertTrue(ok_at)
        self.assertEqual(peak, peak_at)
        ok_over, peak_over, lines_over = worker.candidate_live_set_plan(
            1_000_000, 100_000, 1000, span, span, peak - 1)
        self.assertFalse(ok_over)
        self.assertEqual(peak, peak_over)
        text = "\n".join(lines_over)
        self.assertIn(str(peak_over), text)
        self.assertIn("vs budget", text)

    def test_live_set_plan_refuses_unsafe_int32_bin_span(self):
        ok, _peak, lines = worker.candidate_live_set_plan(
            1000, 1000, 1, 400_000.0, 400_000.0, lidar.WORKER_MEMORY_BUDGET_BYTES)
        self.assertFalse(ok)
        self.assertIn("UNSAFE", "\n".join(lines))

    def test_plan_guard_refuses_before_allocation_with_plan_arithmetic(self):
        # numpy=None proves the refusal precedes ANY allocation: the plan runs and
        # fails closed before the accumulators are built.
        cfg = self._valid_cfg(footprint_l93=dict(PROVIDER_NOMINAL_TILE_FOOTPRINT_L93))
        header_info = {
            "header_point_count": 22_171_803,
            "footprint_l93": dict(PROVIDER_NOMINAL_TILE_FOOTPRINT_L93),
        }
        stderr = io.StringIO()
        with contextlib.redirect_stderr(stderr):
            with self.assertRaises(SystemExit) as ctx:
                worker.open_candidate_accumulator(
                    cfg, header_info, 2_000_000, 100_000_000, None)
        self.assertEqual(worker.EXIT_MEMORY, ctx.exception.code)
        self.assertIn("live-set plan", stderr.getvalue())
        self.assertIn("22171803", stderr.getvalue())
        self.assertIn("planned peak", stderr.getvalue())

    def test_payload_preflight_boundary_and_arithmetic(self):
        self.assertEqual(10 * 21 + 8 * 25 + 2 * 7, worker.CANDIDATE_ROW_BOUND_BYTES)
        max_rows = (
            (worker.CANDIDATE_OUTPUT_BOUND_BYTES - worker.CANDIDATE_PAYLOAD_DIAGNOSTIC_BYTES)
            // worker.CANDIDATE_ROW_BOUND_BYTES
        )
        ok, bound, text = worker.candidate_payload_preflight(max_rows)
        self.assertTrue(ok)
        self.assertEqual(
            max_rows * worker.CANDIDATE_ROW_BOUND_BYTES
            + worker.CANDIDATE_PAYLOAD_DIAGNOSTIC_BYTES, bound)
        self.assertIn("worst-case row", text)
        self.assertIn(str(worker.CANDIDATE_OUTPUT_BOUND_BYTES), text)
        ok_over, bound_over, _text = worker.candidate_payload_preflight(max_rows + 1)
        self.assertFalse(ok_over)
        self.assertEqual(bound + worker.CANDIDATE_ROW_BOUND_BYTES, bound_over)

    def test_real_tile_contained_rows_cannot_exceed_the_preflight_bound(self):
        # The planned real run (361x361 lattice) must fit the 64 MiB preflight
        # bound with the frozen constants; a larger lattice fails closed during
        # enumeration instead of materializing the payload.
        ok, bound, _text = worker.candidate_payload_preflight(361 * 361)
        self.assertTrue(ok)
        self.assertLess(bound, worker.CANDIDATE_OUTPUT_BOUND_BYTES)

    def test_output_bound_constant_and_check(self):
        self.assertEqual(64 << 20, worker.CANDIDATE_OUTPUT_BOUND_BYTES)
        self.assertTrue(worker.candidates_payload_within_bound("x" * (worker.CANDIDATE_OUTPUT_BOUND_BYTES - 1)))
        self.assertFalse(worker.candidates_payload_within_bound(
            "x" * (worker.CANDIDATE_OUTPUT_BOUND_BYTES + 1)))

    def _valid_cfg(self, **overrides):
        cfg = {
            "lattice_origin_l93": [839974.454115, 6957066.765093],
            "aoi_sw_l93": [839974.454115, 6957066.765093],
            "lattice_cols": 361,
            "lattice_rows": 361,
            "spacing_m": 3.048,
            "window_r": 2.5,
            "footprint_l93": {"x_min": 840000.0, "y_min": 6957000.0,
                              "x_max": 841000.0, "y_max": 6958000.0},
            "e23_radius": 3.048,
        }
        cfg.update(overrides)
        return cfg

    def test_valid_candidates_request_passes(self):
        self.assertEqual(self._valid_cfg(), worker.validate_candidates_request(self._valid_cfg()))

    def assert_usage_exit(self, cfg):
        with self.assertRaises(SystemExit) as ctx:
            worker.validate_candidates_request(cfg)
        self.assertEqual(worker.EXIT_USAGE, ctx.exception.code)

    def test_missing_key_fails_closed(self):
        cfg = self._valid_cfg()
        del cfg["window_r"]
        self.assert_usage_exit(cfg)

    def test_non_frozen_spacing_fails_closed(self):
        self.assert_usage_exit(self._valid_cfg(spacing_m=3.0))
        self.assert_usage_exit(self._valid_cfg(window_r=2.0))
        self.assert_usage_exit(self._valid_cfg(e23_radius=3.5))

    def test_anchoring_disagreement_fails_closed(self):
        self.assert_usage_exit(self._valid_cfg(aoi_sw_l93=[839974.5, 6957066.765093]))

    def test_degenerate_or_nonnumeric_footprint_fails_closed(self):
        self.assert_usage_exit(self._valid_cfg(
            footprint_l93={"x_min": 1.0, "y_min": 0.0, "x_max": 1.0, "y_max": 2.0}))
        self.assert_usage_exit(self._valid_cfg(
            footprint_l93={"x_min": "a", "y_min": 0.0, "x_max": 2.0, "y_max": 2.0}))

    def test_oversized_cell_count_fails_closed(self):
        self.assert_usage_exit(self._valid_cfg(lattice_cols=4096, lattice_rows=4096))

    def test_containment_helper_is_the_canonical_module_function(self):
        # The worker imports the canonical containment predicate; single source.
        self.assertIs(worker.groundtruth, sedan_lidar_select)

    def test_header_edge_containment_excludes_crossing_windows(self):
        footprint = dict(FIXTURE_FOOTPRINT_L93)   # x_min 840090.0
        inside = sedan_lidar_select.window_inside_footprint
        # Window crossing the x_min header edge: refused.
        self.assertFalse(inside(840092.0, 6957200.0, 2.5, footprint))
        # Window tangent to the x_min header edge from inside: contained.
        self.assertTrue(inside(840092.5, 6957200.0, 2.5, footprint))
        # Same at the x_max edge (840990.0).
        self.assertFalse(inside(840988.0, 6957200.0, 2.5, footprint))
        self.assertTrue(inside(840987.5, 6957200.0, 2.5, footprint))

    def test_controller_reverification_drops_non_contained_candidates(self):
        block = candidate_block(FIXTURE_CANDIDATES, FIXTURE_FOOTPRINT_L93)
        block["arrays"]["row"].append(0)
        block["arrays"]["col"].append(0)   # window near the anchor, far outside the fixture footprint
        for key in ("n2", "a", "b", "c", "sigma_fit", "slope_deg", "curv_bound"):
            block["arrays"][key].append(121 if key == "n2" else (0.003 if key == "sigma_fit" else None))
        for key in ("curv_unavailable", "fit_unavailable"):
            block["arrays"][key].append(False)
        block["arrays"]["mix2"].append(121)
        for key in ("mix3", "mix4", "mix5", "mix6", "mix9", "mix17"):
            block["arrays"][key].append(0)
        block["arrays"]["e2_dist"].append(None)
        block["arrays"]["e3_dist"].append(None)
        block["cells_contained"] += 1
        request = {"candidates": {"lattice_origin_l93": [LATTICE_ANCHOR_L93[0],
                                                         LATTICE_ANCHOR_L93[1]]}}
        records, meta, problem = lidar.build_candidate_records(
            block, request, dict(REAL_AOI_BBOX_L93))
        self.assertIsNone(problem)
        self.assertEqual(7, len(records))
        self.assertEqual(1, meta["containment_failures_rereported"])


class CandidateNonFiniteDefenseTests(ProbeHarness):
    """C1 finite-validation defense (controller side, fixture level): any
    non-finite or non-numeric statistic/count in the worker's candidate arrays
    marks the cell fit-unavailable with a recorded reason, NEVER eligible, and
    counts in the report exclusions; no exception escapes and no NaN reaches a
    rule comparison."""

    @staticmethod
    def poisoned_block(field, value):
        block = candidates_block_fixture()
        block["arrays"][field][0] = value
        return block

    def build_records(self, block):
        request = {"candidates": {"lattice_origin_l93": [LATTICE_ANCHOR_L93[0],
                                                         LATTICE_ANCHOR_L93[1]]}}
        records, _meta, problem = lidar.build_candidate_records(
            block, request, dict(REAL_AOI_BBOX_L93))
        self.assertIsNone(problem)
        self.assertEqual(7, len(records))
        return records

    def assert_defended(self, field, value):
        records = self.build_records(self.poisoned_block(field, value))
        first = records[0]
        self.assertTrue(first["fit_unavailable"], field)
        self.assertFalse(first["eligible"], field)
        self.assertEqual([field], first["nonfinite_fields"])
        self.assertIn(("FIT_NONFINITE", field),
                      [(rule, trigger) for rule, trigger in first["fired"]])
        # every other fixture cell is untouched
        self.assertTrue(all(record["eligible"] for record in records[1:]))
        self.assertTrue(all("nonfinite_fields" not in record for record in records[1:]))
        return first

    def test_nan_in_each_statistic_path_is_defended(self):
        for field in ("a", "b", "c", "sigma_fit", "slope_deg", "curv_bound",
                      "e2_dist", "e3_dist"):
            first = self.assert_defended(field, float("nan"))
            mapped = {"a": "truth_z", "sigma_fit": "sigma_fit", "slope_deg": "slope_deg",
                      "curv_bound": "curv_bound", "e2_dist": "d9", "e3_dist": "d17"}
            if field in mapped:
                self.assertIsNone(first[mapped[field]], field)

    def test_infinite_counts_and_mixes_are_defended(self):
        for field in ("n2", "mix2", "mix3", "mix4", "mix5", "mix6", "mix9", "mix17"):
            first = self.assert_defended(field, float("inf"))
        n2_record = self.assert_defended("n2", float("inf"))
        self.assertEqual(0, n2_record["n2"])
        self.assertEqual(0, n2_record["n9"])
        rules = [rule for rule, _trigger in n2_record["fired"]]
        self.assertIn("E1", rules)              # n2 dropped to 0 -> E1 fires too
        self.assertIn("FIT_NONFINITE", rules)

    def test_nan_count_is_defended_without_int_overflow(self):
        first = self.assert_defended("n2", float("nan"))
        self.assertEqual(0, first["n2"])

    def test_non_numeric_garbage_is_defended(self):
        self.assert_defended("sigma_fit", "0.003")
        self.assert_defended("n2", "121")

    def test_multiple_defective_fields_are_recorded_together(self):
        block = candidates_block_fixture()
        block["arrays"]["sigma_fit"][0] = float("nan")
        block["arrays"]["curv_bound"][0] = float("-inf")
        first = self.build_records(block)[0]
        self.assertEqual(["sigma_fit", "curv_bound"], first["nonfinite_fields"])
        self.assertFalse(first["eligible"])
        self.assertIsNone(first["sigma_fit"])
        self.assertIsNone(first["curv_bound"])

    def test_finite_fixture_rows_stay_unflagged(self):
        records = self.build_records(candidates_block_fixture())
        self.assertTrue(all("nonfinite_fields" not in record for record in records))
        self.assertTrue(all(record["eligible"] for record in records))

    def test_run_level_exclusion_counts_and_never_eligible(self):
        # One NaN sigma in the shipped fixture: the poisoned primary is excluded,
        # the exclusion is counted in the report, and the remaining valid cells
        # still satisfy the availability floor without any relaxation.
        block = candidates_block_fixture()
        block["arrays"]["sigma_fit"][0] = float("nan")
        measurements = good_measurements(candidates=block)
        code, _output = self.run_probe_case(measurements)
        self.assertEqual(lidar.EXIT_OK, code)
        report = self.read_report()
        gate_d = report["gates"]["d_v1_2"]
        availability = gate_d["availability"]
        self.assertEqual({"FIT_NONFINITE": 1}, availability["exclusion_counts"])
        self.assertEqual(6, availability["eligible_count"])
        self.assertNotIn("r40c40", [record["id"] for record in availability["primaries"]])
        self.assertEqual(5, len(availability["primaries"]))
        self.assertTrue(gate_d["pass"])
        self.assertTrue(report["gates_pass"])

    def test_truncated_candidate_arrays_fail_closed_stop_and_escalate(self):
        block = candidates_block_fixture()
        del block["arrays"]["sigma_fit"][0]
        request = {"candidates": {"lattice_origin_l93": [LATTICE_ANCHOR_L93[0],
                                                         LATTICE_ANCHOR_L93[1]]}}
        records, _meta, problem = lidar.build_candidate_records(
            block, request, dict(REAL_AOI_BBOX_L93))
        self.assertIsNone(records)
        self.assertIn("length disagrees", problem)
        measurements = good_measurements(candidates=block)
        code, _output = self.run_probe_case(measurements)
        self.assertEqual(lidar.EXIT_GUARD, code)
        gate_d = self.read_report()["gates"]["d_v1_2"]
        self.assertFalse(gate_d["pass"])
        self.assertTrue(gate_d["stop_and_escalate"])
        self.assertIn("length disagrees", gate_d["reason"])


class CandidateMixedWindowStatisticsTests(unittest.TestCase):
    """C1 worker regressions (numpy required): the 2.5 m window fit is CLASS-2
    ONLY (protocol v1.2 s6.2). A mixed window (>= 3 class-2 points plus one
    class 5 / 6 / 9 point inside 2.5 m) must yield statistics byte-identical to
    the class-2-only fit; the mixes and the E2/E3 distances keep using ALL
    retained classes. The full accumulate -> index -> build pipeline runs for
    every scene, so the restructured bin index (int32 codes, stable argsort,
    progressive frees) is exercised deterministically."""

    FOOTPRINT = {"x_min": -10.0, "y_min": -10.0, "x_max": 10.0, "y_max": 10.0}

    @classmethod
    def setUpClass(cls):
        if importlib.util.find_spec("numpy") is None:
            raise unittest.SkipTest("requires numpy (stdlib CI skips truthfully)")
        import numpy
        cls.numpy = numpy

    def cfg(self):
        return {
            "lattice_origin_l93": [0.0, 0.0], "aoi_sw_l93": [0.0, 0.0],
            "lattice_cols": 1, "lattice_rows": 1, "spacing_m": 3.048,
            "window_r": 2.5, "e23_radius": 3.048,
            "footprint_l93": dict(self.FOOTPRINT),
        }

    def scene(self, extra_class):
        # 20 class-2 points on the exact plane z = 10 + 0.01(x-1.524): two
        # concentric rings around the 1x1 lattice cell centre, full rank.
        numpy = self.numpy
        k = numpy.arange(20, dtype=numpy.int64)
        angles = 2.0 * math.pi * k / 20.0
        radii = numpy.where(k % 2 == 0, 2.2, 1.6)
        xs = radii * numpy.cos(angles) + 1.524
        ys = radii * numpy.sin(angles) + 1.524
        zs = 10.0 + 0.01 * (xs - 1.524)
        classes = numpy.full(20, 2, dtype=numpy.int64)
        if extra_class:
            # ONE contaminant 1.0 m from the window centre, inside 2.5 m.
            xs = numpy.concatenate([xs, [2.524]])
            ys = numpy.concatenate([ys, [1.524]])
            zs = numpy.concatenate([zs, [10.5]])
            classes = numpy.concatenate([classes, [extra_class]])
        return xs, ys, zs, classes

    def build_block(self, extra_class):
        from types import SimpleNamespace
        numpy = self.numpy
        footprint = dict(self.FOOTPRINT)
        margin = 3.048
        n_alloc = 64
        cand = {
            "cfg": self.cfg(), "footprint": footprint,
            "origin_x": footprint["x_min"] - margin,
            "origin_y": footprint["y_min"] - margin, "margin": margin,
            "n_alloc": n_alloc, "planned_peak_bytes": 0,
            "cursor": 0, "points_retained": 0,
            "x": numpy.empty(n_alloc, dtype=numpy.float32),
            "y": numpy.empty(n_alloc, dtype=numpy.float32),
            "cls": numpy.empty(n_alloc, dtype=numpy.uint8),
            "z": numpy.empty(n_alloc, dtype=numpy.float64),
        }
        xs, ys, zs, classes = self.scene(extra_class)
        chunk = SimpleNamespace(x=xs, y=ys, z=zs, classification=classes)
        worker.accumulate_candidate_chunk(cand, chunk, numpy)
        request = {"memory_budget_bytes": lidar.WORKER_MEMORY_BUDGET_BYTES}
        return worker.build_candidates_block(cand, request, numpy)

    def stats(self, block):
        keys = ("n2", "mix2", "a", "b", "c", "sigma_fit", "slope_deg", "curv_bound",
                "curv_unavailable", "fit_unavailable")
        return {key: block["arrays"][key][0] for key in keys}

    def test_mixed_class5_window_statistics_byte_identical_to_class2_only(self):
        pure = self.stats(self.build_block(0))
        mixed = self.stats(self.build_block(5))
        self.assertEqual(pure, mixed)
        self.assertEqual(20, mixed["n2"])
        self.assertEqual(20, mixed["mix2"])
        self.assertFalse(mixed["fit_unavailable"])
        self.assertFalse(mixed["curv_unavailable"])
        self.assertLess(mixed["sigma_fit"], 1e-6)
        self.assertAlmostEqual(
            math.degrees(math.atan(0.01)), mixed["slope_deg"], delta=1e-6)
        block = self.build_block(5)
        self.assertEqual(1, block["arrays"]["mix5"][0])
        self.assertIsNone(block["arrays"]["e2_dist"][0])

    def test_mixed_class6_window_statistics_byte_identical_to_class2_only(self):
        pure = self.stats(self.build_block(0))
        mixed = self.stats(self.build_block(6))
        self.assertEqual(pure, mixed)
        self.assertEqual(1, self.build_block(6)["arrays"]["mix6"][0])

    def test_mixed_class9_window_statistics_byte_identical_to_class2_only(self):
        pure_block = self.build_block(0)
        mixed_block = self.build_block(9)
        self.assertEqual(self.stats(pure_block), self.stats(mixed_block))
        # The E2 distance keeps using ALL retained classes: pure none, mixed set.
        self.assertIsNone(pure_block["arrays"]["e2_dist"][0])
        self.assertIsNotNone(mixed_block["arrays"]["e2_dist"][0])
        self.assertLess(mixed_block["arrays"]["e2_dist"][0], 3.048)
        self.assertEqual(1, mixed_block["arrays"]["mix9"][0])

    def test_worker_sanitizes_non_finite_fit_to_unavailable_strict_json(self):
        # Worker-level malformed-statistics discipline (where constructible): a
        # non-finite plane fit is recorded unavailable, nothing non-finite is
        # serialized, and the payload round-trips strictly.
        from unittest import mock
        numpy = self.numpy
        poisoned = (1.0, 0.0, 0.0, float("nan"), numpy.ones(20))
        with mock.patch.object(worker, "_huber_plane_fit", return_value=poisoned):
            block = self.build_block(0)
        arrays = block["arrays"]
        self.assertTrue(arrays["fit_unavailable"][0])
        self.assertTrue(arrays["curv_unavailable"][0])
        for key in ("a", "b", "c", "sigma_fit", "slope_deg", "curv_bound"):
            self.assertIsNone(arrays[key][0], key)
        payload = json.dumps(block, sort_keys=True, allow_nan=False)
        self.assertEqual(block, json.loads(payload))


class GateDFootprintTests(ProbeHarness):
    """HISTORICAL v1.1 gate-d evaluator (direct calls to lidar.evaluate_gate_d, the
    retained coverage-totality evaluator) plus the two run-level tests that assert
    the v1.2 report's coverage record (the verbatim 10 m coverage map now rides in
    gate d_v1_2). B1 regressions (NF1-corrected geometry, delta-reverify.md): the
    historical evaluator denominates the AOI grid cells intersecting the
    AOI-intersect-header-footprint rectangle (boundary partial cells included), with
    the worker-reported LAS header footprint validated before use. Synthetic
    fixtures use the provider-nominal footprint of first tile 0840-6958: x [840000,
    841000] x y [6957000, 6958000] (NW-corner block naming per the cached WFS
    metadata), giving expected cells 9494 (cols 2..102 x rows 0..93) and
    intersection area 934624.541 m2 (93.5 ha, 76.9% of the AOI).

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
        # HISTORICAL v1.1 metric retained: evaluate_gate_c counts all filter-window
        # cells including out-of-grid ones; expectations unchanged and still passing.
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
        self.assertFalse(report["gates"]["d_v1_2"]["pass"])
        self.assertTrue(report["gates"]["d_v1_2"]["stop_and_escalate"])
        self.assertIn("STOP_AND_ESCALATE", report["gates"]["d_v1_2"]["reason"])
        self.assertIn("footprint_l93", report["gates"]["d_v1_2"]["reason"])

    def test_run_level_stub_aoi_intersection_passes(self):
        code, _output = self.run_probe_case(good_measurements())
        self.assertEqual(lidar.EXIT_OK, code)
        report = self.read_report()
        gate_d = report["gates"]["d_v1_2"]
        self.assertTrue(gate_d["pass"])
        # Coverage record retained verbatim: 8281 expected cells of the fixture
        # footprint intersect the AOI grid; cell 50,50 is the documented hole.
        self.assertEqual(8281, gate_d["expected_cells"])
        self.assertEqual(8280, gate_d["covered_cells"])
        self.assertEqual(1, gate_d["hole_count"])
        self.assertEqual([[50, 50]], gate_d["holes"])
        self.assertEqual({"cols": [11, 101], "rows": [11, 101]}, gate_d["expected_cell_ranges"])


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
        # Repo-local stdlib-only siblings are permitted (the canonical selection
        # module is python-stdlib-only and is itself purity-checked by its tests).
        repo_local = {"sedan_lidar_select"}
        non_stdlib = {root for root in roots
                      if root not in sys.stdlib_module_names and root not in repo_local}
        self.assertEqual(set(), non_stdlib)
        self.assertTrue(repo_local.issubset(roots))
        for banned in ("laspy", "lazrs", "numpy"):
            self.assertNotIn(banned, roots)
        select_source = (_GEO_DIR / "sedan_lidar_select.py").read_text()
        select_tree = ast.parse(select_source)
        select_roots = set()
        for node in ast.walk(select_tree):
            if isinstance(node, ast.Import):
                select_roots.update(alias.name.split(".")[0] for alias in node.names)
            elif isinstance(node, ast.ImportFrom) and node.module:
                select_roots.add(node.module.split(".")[0])
        self.assertEqual(set(), {root for root in select_roots if root not in sys.stdlib_module_names})


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


@unittest.skipUnless(
    importlib.util.find_spec("laspy") and importlib.util.find_spec("numpy"),
    "requires the owned venv laspy/numpy (stdlib CI skips truthfully)")
class RealLaspyCandidateSmokeTests(unittest.TestCase):
    """v1.2 candidate-path smokes through the REAL installed laspy + numpy: a
    synthetic LAS with known analytic ground truth is measured end-to-end through
    worker.main (single streaming pass, chunk_iterator with points_per_iteration,
    preallocated candidate accumulators, 2.5 m bin index) and the controller gate
    code evaluates the REAL measurements. Geometry is scaled down (small synthetic
    tile footprint, lattice still 3.048, reduced candidate grid where allowed).

    Analytic expectations: ground patches are EXACT planes z = 150.0 on a 0.4 m
    grid, so sigma_fit, slope and the curvature bound are analytically 0 (asserted
    < 1e-9); window point counts are phase-sensitive by construction and asserted
    in a computed range; water patches (class 9, no class 2) produce documented
    coverage holes and E2 proximities. Same-input reruns are byte-identical.
    """

    ANCHOR_X = 839974.454115
    ANCHOR_Y = 6957066.765093
    # Coverage-grid anchor: the AOI bbox minimum the 10 m cells are keyed on
    # (distinct from the lattice Y anchor by the cs2cs projection offset).
    COVERAGE_X = 839974.454115
    COVERAGE_Y = 6957065.375459
    SPACING = 3.048
    PATCHES = ((40, 40), (95, 20), (180, 180), (280, 60), (320, 320))
    WATER_CELL = (3, 4)   # 10 m coverage cell holding only class-9 points
    CHUNK = 10_000

    def tmp_dir(self):
        parent = "/tmp/opencode" if os.path.isdir("/tmp/opencode") else None
        context = tempfile.TemporaryDirectory(prefix="sedan-lidar-cand-smoke-", dir=parent)
        self.addCleanup(context.cleanup)
        return Path(context.name)

    def cell_centre(self, row, col):
        return (self.ANCHOR_X + (col + 0.5) * self.SPACING,
                self.ANCHOR_Y + (row + 0.5) * self.SPACING)

    def snapped_patch_origin(self, row, col, extent_m):
        cx, cy = self.cell_centre(row, col)
        x_min = math.floor((cx - extent_m / 2.0 - self.COVERAGE_X) / 10.0) * 10.0 + self.COVERAGE_X
        y_min = math.floor((cy - extent_m / 2.0 - self.COVERAGE_Y) / 10.0) * 10.0 + self.COVERAGE_Y
        return x_min, y_min

    def write_synthetic_las(self, root, name="synthetic-candidates.las", patches=PATCHES,
                            extent_m=30.0, with_water=True, contaminants=(),
                            interior_water=None):
        import laspy
        import numpy

        xs, ys, zs, classes = [], [], [], []
        # 0.2 m inset from the snapped patch edges: no point sits on a 10 m
        # coverage-cell boundary (mm quantisation would flip it into the neighbour),
        # and each fully covered cell receives exactly 25 x 25 = 625 points.
        offsets = [0.2 + 0.4 * i for i in range(int(extent_m / 0.4))]
        for row, col in patches:
            x_min, y_min = self.snapped_patch_origin(row, col, extent_m)
            for dx in offsets:
                for dy in offsets:
                    xs.append(x_min + dx)
                    ys.append(y_min + dy)
                    zs.append(150.0)
                    classes.append(2)
        # Contaminant classes laid over ground patches (same xy grid): windows
        # there stay mixed-class while the class-2-only statistics remain the
        # exact-plane analytic values.
        for row, col, classification, z in contaminants:
            x_min, y_min = self.snapped_patch_origin(row, col, extent_m)
            for dx in offsets:
                for dy in offsets:
                    xs.append(x_min + dx)
                    ys.append(y_min + dy)
                    zs.append(z)
                    classes.append(classification)
        if with_water:
            wx_min = self.COVERAGE_X + self.WATER_CELL[0] * 10.0 + 1.5
            wy_min = self.COVERAGE_Y + self.WATER_CELL[1] * 10.0 + 1.5
            water_offsets = [0.4 * i for i in range(18)]   # 7.2 m x 7.2 m inside the cell
            for dx in water_offsets:
                for dy in water_offsets:
                    xs.append(wx_min + dx)
                    ys.append(wy_min + dy)
                    zs.append(149.5)
                    classes.append(9)
        if interior_water is not None:
            # Class-9 points INSIDE a ground patch: overlapping windows carry
            # ground and water together (mix2 > 0 AND mix9 > 0 AND e2_dist set).
            row, col = interior_water
            wx_min, wy_min = self.snapped_patch_origin(row, col, extent_m)
            water_offsets = [10.0 + 0.4 * i for i in range(20)]   # 8 m square, patch interior
            for dx in water_offsets:
                for dy in water_offsets:
                    xs.append(wx_min + dx)
                    ys.append(wy_min + dy)
                    zs.append(149.5)
                    classes.append(9)
        las = laspy.create(file_version="1.4", point_format=6)
        las.header.scales = [0.001, 0.001, 0.001]
        las.header.offsets = [840000.0, 6957000.0, 150.0]
        las.x = numpy.asarray(xs, dtype=numpy.float64)
        las.y = numpy.asarray(ys, dtype=numpy.float64)
        las.z = numpy.asarray(zs, dtype=numpy.float64)
        las.classification = numpy.asarray(classes, dtype=numpy.int64)
        las.vlrs.append(laspy.VLR(user_id="copc", record_id=1,
                                  description="synthetic copc marker", record_data=b"0" * 32))
        wkt = 'PROJCS["ETRS89 / Lambert-93",AUTHORITY["EPSG","2154"]]'
        las.vlrs.append(laspy.VLR(user_id="LASF_Projection", record_id=2112,
                                  description=wkt, record_data=wkt.encode()))
        path = root / name
        las.write(str(path))
        return path

    def build_request(self, root, las_path, output_name, request_name="worker-request.json",
                      lattice_cols=361, lattice_rows=361,
                      budget=lidar.WORKER_MEMORY_BUDGET_BYTES):
        request = {
            "request_version": 1,
            "tile_path": str(las_path),
            "aoi_bbox_l93": dict(REAL_AOI_BBOX_L93),
            "filter_bbox_l93": {
                "x_min": 839924.352442, "y_min": 6957015.273483,
                "x_max": 841126.804970, "y_max": 6958217.726004},
            "cell_size_m": 10.0,
            "chunk_size_points": self.CHUNK,
            "memory_budget_bytes": budget,
            "candidates": {
                "lattice_origin_l93": [self.ANCHOR_X, self.ANCHOR_Y],
                "aoi_sw_l93": [self.ANCHOR_X, self.ANCHOR_Y],
                "lattice_cols": lattice_cols,
                "lattice_rows": lattice_rows,
                "spacing_m": self.SPACING,
                "window_r": 2.5,
                "e23_radius": 3.048,
                "footprint_l93": {
                    "x_min": 839924.352442, "y_min": 6957015.273483,
                    "x_max": 841126.804970, "y_max": 6958217.726004},
            },
            "output_path": str(root / output_name),
        }
        path = root / request_name
        path.write_text(json.dumps(request), encoding="utf-8")
        return request

    def run_worker(self, request, request_name="worker-request.json"):
        buffer = io.StringIO()
        with contextlib.redirect_stdout(buffer):
            code = worker.main(["sedan_lidar_probe_worker.py",
                                str(Path(request["output_path"]).parent / request_name)])
        return code

    def canned_cs2cs_info(self):
        points = list(REAL_CORNERS_L93) + list(REAL_FILTER_CORNERS_L93)
        return {
            "path": "canned",
            "version": "cs2cs Rel. 9.7.1 canned",
            "version_exit_code": 0,
            "command": ["cs2cs", "-f", "%.6f", "EPSG:32631", "+to", "EPSG:2154"],
            "axis_order": "easting northing",
            "input_corners_32631": [lidar.LATTICE_ORIGIN_32631] * 8,
            "output_corners_l93": points,
            "lattice_anchor_32631": [lidar.LATTICE_ORIGIN_32631[0], lidar.LATTICE_ORIGIN_32631[1]],
            "lattice_anchor_l93": [self.ANCHOR_X, self.ANCHOR_Y],
            "batched_point_count": 9,
        }

    def evaluate_gates_on(self, measurements, request, las_path):
        tile = lidar.FIRST_TILE._replace(size=os.path.getsize(las_path))
        digest = hashlib.sha256(Path(las_path).read_bytes()).hexdigest()
        enriched_request = dict(request)
        enriched_request.update({
            "venv_python": sys.executable,
            "worker_script": str(_worker_path),
            "request_path": str(Path(request["output_path"]).parent / "worker-request.json"),
        })
        lineage = wfs_lineage_fixture()
        worker_run = SimpleNamespace(stdout="candidate smoke run")
        return lidar.evaluate_gates(
            measurements, lineage, self.canned_cs2cs_info(), worker_run,
            enriched_request, str(las_path), tile, digest)

    def candidate_row(self, measurements, row, col):
        block = measurements["candidates"]
        arrays = block["arrays"]
        for index, (r, c) in enumerate(zip(arrays["row"], arrays["col"])):
            if r == row and c == col:
                return {key: arrays[key][index] for key in arrays}
        return None

    def test_candidate_mode_end_to_end_gates_pass_with_documented_holes(self):
        root = self.tmp_dir()
        las_path = self.write_synthetic_las(root)
        request = self.build_request(root, las_path, "measurements.json")
        code = self.run_worker(request)
        self.assertEqual(worker.EXIT_OK, code)
        measurements = json.loads(Path(request["output_path"]).read_text(encoding="utf-8"))
        total_points = len(self.PATCHES) * 75 * 75 + 18 * 18
        self.assertEqual(total_points, measurements["points"]["total"])
        self.assertGreaterEqual(measurements["points"]["chunk_count"], 2)
        block = measurements["candidates"]
        self.assertEqual(1, block["candidate_measurement_version"])
        self.assertGreater(block["cells_contained"], 1000)
        # Analytic flat-plane expectations at patch-interior lattice cells.
        for row, col in self.PATCHES:
            record = self.candidate_row(measurements, row, col)
            self.assertIsNotNone(record, (row, col))
            self.assertGreaterEqual(record["n2"], 100)   # phase-sensitive count, >= ~117
            self.assertLessEqual(record["n2"], 140)
            self.assertEqual(record["n2"], record["mix2"])
            self.assertAlmostEqual(record["a"], 150.0, delta=1e-9)
            self.assertLess(abs(record["sigma_fit"]), 1e-9)
            self.assertLess(record["slope_deg"], 1e-9)
            self.assertLess(record["curv_bound"], 1e-9)
            self.assertIsNone(record["e2_dist"])
            self.assertIsNone(record["e3_dist"])
            self.assertFalse(record["curv_unavailable"])
            self.assertFalse(record["fit_unavailable"])
        # Water cell: mixed classes and E2 proximity at the contained candidate
        # whose window overlaps the water patch.
        water_record = self.candidate_row(measurements, 14, 11)
        self.assertIsNotNone(water_record)
        self.assertGreater(water_record["mix9"], 0)
        self.assertIsNotNone(water_record["e2_dist"])
        self.assertLess(water_record["e2_dist"], 3.048)
        self.assertGreaterEqual(water_record["e2_dist"], 0.0)
        # Controller gates on the REAL measurements: availability PASS with the
        # water hole recorded verbatim; coverage median exactly the analytic 6.25.
        report = self.evaluate_gates_on(measurements, request, las_path)
        gates = report["gates"]
        self.assertTrue(gates["a"]["pass"])
        self.assertTrue(gates["b"]["pass"])
        self.assertTrue(gates["c_v1_2"]["pass"])
        self.assertAlmostEqual(6.25, gates["c_v1_2"]["median_class2_points_per_m2"], delta=1e-9)
        gate_d = gates["d_v1_2"]
        self.assertTrue(gate_d["pass"])
        self.assertIn([self.WATER_CELL[0], self.WATER_CELL[1]], gate_d["holes"])
        availability = gate_d["availability"]
        self.assertTrue(availability["stage_1_preliminary"])
        self.assertEqual(5, len(availability["primaries"]))
        self.assertGreaterEqual(len(availability["sectors_covered"]), 3)
        self.assertTrue(report["gates_pass"])
        self.assertFalse(report["numeric_proof_pass"])

    def test_candidate_mode_single_sector_stops_and_escalates(self):
        root = self.tmp_dir()
        las_path = self.write_synthetic_las(
            root, name="synthetic-single-sector.las",
            patches=((60, 60),), extent_m=150.0, with_water=False)
        request = self.build_request(root, las_path, "measurements.json",
                                     lattice_cols=140, lattice_rows=140)
        code = self.run_worker(request)
        self.assertEqual(worker.EXIT_OK, code)
        measurements = json.loads(Path(request["output_path"]).read_text(encoding="utf-8"))
        report = self.evaluate_gates_on(measurements, request, las_path)
        gate_d = report["gates"]["d_v1_2"]
        self.assertFalse(gate_d["pass"])
        self.assertTrue(gate_d["stop_and_escalate"])
        self.assertIn(gate_d["stop"]["reason"], ("sectors_below_floor", "eligible_below_floor"))
        availability = gate_d["availability"]
        self.assertGreater(availability["eligible_count"], 100)  # dense single-sector block
        self.assertFalse(report["gates_pass"])
        self.assertTrue(report["gates"]["c_v1_2"]["pass"])

    def test_candidate_mode_same_input_byte_identical(self):
        root = self.tmp_dir()
        las_path = self.write_synthetic_las(root, name="synthetic-determinism.las")
        request_a = self.build_request(root, las_path, "measurements-a.json",
                                       request_name="worker-request-a.json")
        request_b = self.build_request(root, las_path, "measurements-b.json",
                                       request_name="worker-request-b.json")
        self.assertEqual(worker.EXIT_OK, self.run_worker(request_a, "worker-request-a.json"))
        self.assertEqual(worker.EXIT_OK, self.run_worker(request_b, "worker-request-b.json"))
        bytes_a = Path(request_a["output_path"]).read_bytes()
        bytes_b = Path(request_b["output_path"]).read_bytes()
        self.assertEqual(bytes_a, bytes_b)
        block = json.loads(bytes_a)["candidates"]
        payload = json.dumps(block, sort_keys=True)
        self.assertTrue(worker.candidates_payload_within_bound(payload))

    def test_chunk_iterator_takes_points_per_iteration_keyword(self):
        # Regression guard for the count= bug class: the real reader's chunk
        # iterator takes points_per_iteration (signature check), the worker source
        # calls it with that exact keyword, and every end-to-end run above would
        # fail closed on any signature mismatch.
        import inspect
        import laspy
        from laspy import LasReader
        self.assertIn("points_per_iteration", inspect.signature(LasReader.chunk_iterator).parameters)
        source = Path(_worker_path).read_text()
        self.assertIn("chunk_iterator(points_per_iteration=", source)
        root = self.tmp_dir()
        las_path = self.write_synthetic_las(root, name="synthetic-signature.las")
        with laspy.open(str(las_path)) as reader:
            chunks = list(reader.chunk_iterator(points_per_iteration=self.CHUNK))
        self.assertGreaterEqual(len(chunks), 2)

    def test_candidate_mode_mixed_class_windows_keep_class2_only_statistics(self):
        # C1 real-laspy regression: ground patches share their windows with class
        # 5 (vegetation), class 6 (building) and class 9 (water) contaminants;
        # the fitted statistics stay the exact-plane class-2-only analytic
        # values, n2 == mix2, the mixes see every retained class, and the
        # written JSON carries no non-finite tokens.
        root = self.tmp_dir()
        las_path = self.write_synthetic_las(
            root, name="synthetic-mixed.las",
            patches=((40, 40), (95, 20), (320, 320)), with_water=False,
            contaminants=((40, 40, 5, 150.4), (95, 20, 6, 151.0)),
            interior_water=(320, 320))
        request = self.build_request(root, las_path, "measurements.json")
        self.assertEqual(worker.EXIT_OK, self.run_worker(request))
        raw = Path(request["output_path"]).read_bytes()
        self.assertNotIn(b"NaN", raw)
        self.assertNotIn(b"Infinity", raw)
        measurements = json.loads(raw)
        block = measurements["candidates"]
        self.assertGreater(block["cells_contained"], 1000)
        expectations = {40: ("mix5", 5), 95: ("mix6", 6), 320: ("mix9", 9)}
        for row, col in ((40, 40), (95, 20), (320, 320)):
            record = self.candidate_row(measurements, row, col)
            self.assertIsNotNone(record, (row, col))
            # Window is mixed: >= 100 class-2 points (phase-sensitive count on
            # the 0.4 m grid) plus the contaminant grid points inside 2.5 m.
            self.assertGreaterEqual(record["n2"], 100)
            self.assertEqual(record["n2"], record["mix2"])
            mix_key, _cls = expectations[row]
            self.assertGreaterEqual(record[mix_key], 1)
            self.assertFalse(record["fit_unavailable"])
            self.assertFalse(record["curv_unavailable"])
            # Exact-plane analytic class-2-only statistics (z constant 150.0).
            self.assertAlmostEqual(record["a"], 150.0, delta=1e-9)
            self.assertLess(abs(record["sigma_fit"]), 1e-9)
            self.assertLess(record["slope_deg"], 1e-9)
            self.assertLess(record["curv_bound"], 1e-9)
        water_record = self.candidate_row(measurements, 320, 320)
        self.assertIsNotNone(water_record["e2_dist"])
        self.assertGreaterEqual(water_record["e2_dist"], 0.0)
        self.assertLess(water_record["e2_dist"], 3.048)

    def test_payload_preflight_refuses_overbound_before_serialization(self):
        # C3: a contained lattice whose worst-case payload cannot fit the 64 MiB
        # preflight bound fails closed DURING enumeration, with the preflight
        # arithmetic logged, and never materializes the payload string (the
        # serialization call is spied and must not fire).
        root = self.tmp_dir()
        import laspy
        import numpy
        las = laspy.create(file_version="1.4", point_format=6)
        las.header.scales = [0.001, 0.001, 0.001]
        las.header.offsets = [840000.0, 6957000.0, 150.0]
        las.x = numpy.asarray([840000.0, 841590.0, 840000.0, 841590.0])
        las.y = numpy.asarray([6957000.0, 6957000.0, 6958590.0, 6958590.0])
        las.z = numpy.asarray([150.0] * 4)
        las.classification = numpy.asarray([2] * 4, dtype=numpy.int64)
        las_path = root / "synthetic-wide.las"
        las.write(str(las_path))
        request = self.build_request(root, las_path, "measurements.json",
                                     request_name="worker-request-overbound.json",
                                     lattice_cols=540, lattice_rows=540)
        from unittest import mock
        stderr = io.StringIO()
        with mock.patch.object(worker.json, "dumps",
                               side_effect=AssertionError(
                                   "serialization must be bounded by the preflight")):
            with contextlib.redirect_stderr(stderr):
                with self.assertRaises(SystemExit) as ctx:
                    self.run_worker(request, "worker-request-overbound.json")
        self.assertEqual(worker.EXIT_MEMORY, ctx.exception.code)
        self.assertIn("preflight bound exceeded during enumeration", stderr.getvalue())
        self.assertIn("424 B", stderr.getvalue())
        self.assertIn("contained rows", stderr.getvalue())
        self.assertFalse(Path(request["output_path"]).exists())

    def test_worker_rss_growth_stays_coarsely_bounded(self):
        # Portable coarse bound (Linux /proc, no timing): one full candidate-mode
        # measurement must grow RSS far less than the frozen 1 GiB live-set plan.
        if not os.path.isdir("/proc/self"):
            self.skipTest("portable RSS probe requires /proc/self (Linux)")

        def rss_kib():
            for line in Path("/proc/self/status").read_text().splitlines():
                if line.startswith("VmRSS:"):
                    return int(line.split()[1])
            return -1

        root = self.tmp_dir()
        las_path = self.write_synthetic_las(root, name="synthetic-rss.las")
        request = self.build_request(root, las_path, "measurements.json",
                                     request_name="worker-request-rss.json",
                                     lattice_cols=40, lattice_rows=40)
        before = rss_kib()
        self.assertEqual(worker.EXIT_OK, self.run_worker(request, "worker-request-rss.json"))
        growth_kib = rss_kib() - before
        self.assertGreaterEqual(growth_kib, 0)
        self.assertLess(growth_kib, 384 * 1024)

    def test_pre_allocation_guard_refuses_oversized_request_end_to_end(self):
        root = self.tmp_dir()
        las_path = self.write_synthetic_las(root, name="synthetic-tiny.las",
                                            patches=((40, 40),), with_water=False)
        # Budget passes the base chunk-settings check (main) but the live-set
        # plan refuses BEFORE any point is streamed or anything is allocated.
        request = self.build_request(root, las_path, "measurements.json",
                                     request_name="worker-request-guard.json",
                                     lattice_cols=20, lattice_rows=20, budget=30_000_000)
        stderr = io.StringIO()
        with contextlib.redirect_stderr(stderr):
            with self.assertRaises(SystemExit) as ctx:
                self.run_worker(request, "worker-request-guard.json")
        self.assertEqual(worker.EXIT_MEMORY, ctx.exception.code)
        self.assertIn("candidate pre-allocation guard", stderr.getvalue())
        self.assertIn("live-set plan", stderr.getvalue())
        self.assertIn("planned peak", stderr.getvalue())
        self.assertFalse(Path(request["output_path"]).exists())


if __name__ == "__main__":
    unittest.main()
