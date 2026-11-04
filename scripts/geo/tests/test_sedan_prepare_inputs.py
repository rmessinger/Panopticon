#!/usr/bin/env python3
"""Offline unit tests for scripts/geo/sedan_prepare_inputs.py (post-freeze
runtime-input preparer).

Conventions follow the sibling suites (test_sedan_lidar_freeze.py): importlib
module load, tempfile filesystems under /tmp/opencode, patched seams, no real
subprocess anywhere. The ONE seam prep.run_tool is replaced wholesale by
FakeGdal, which records exact argv and returns canned bounded ToolRuns; no GDAL
binding, no real raster, no MNT height, no imagery pixel, no network, no
installation, and no asset transfer is ever touched. One exception:
RealGdalControlReferenceTests runs the real gdal binaries on author-created
synthetic 0.2 m rasters only (never IGN data) and skips explicitly when they are
absent.

The consumer chain is validated through the REAL sedan_lidar_freeze validators
(find_completed_freeze, reverify_completed, read_integrity_manifest) against
synthetic artifacts that mirror the accepted completed-freeze shapes, including
the AUGMENTED integrity manifest carrying the worker override block. The
producer entrypoint (sedan_lidar_integrity.py --prepare-integrity) is asserted
unreachable from both the module source and every executed argv. The real cache
root at /mnt/pit2 is never read, written, or mounted by these tests; the owned
lidar venv is not used here (stdlib only).

Pinned facts under test: noDataSearchRadiusMeters EXACTLY 3.048 (never the 25.0
example placeholder), imageryToleranceBytes EXACTLY 2 (never 8), the 1.0 m
operator engineering tolerance, Budget B 0.0390625 game units, five frozen
primaries + two recorded-never-activated alternates, the NGF-IGN69 datum label
with NO accuracy key anywhere, chronology freezeVerifiedAt < firstDemRasterIoAt
by recorded event order, and no placeholder copy from the committed example
manifest (key sets must match the example; values must be REAL).
"""

import contextlib
import copy
import hashlib
import importlib.util
import io
import json
import math
import os
import shutil
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

_GEO_DIR = Path(__file__).resolve().parents[1]
_REPO_ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(_GEO_DIR))

import sedan_lidar_select as groundtruth  # noqa: E402  (stdlib-only science module)
import sedan_lidar_freeze as freeze  # noqa: E402  (consumer-chain validators)
import sedan_extract  # noqa: E402  (producer pin cross-check; stdlib only)

_spec = importlib.util.spec_from_file_location(
    "sedan_prepare_inputs", _GEO_DIR / "sedan_prepare_inputs.py")
prep = importlib.util.module_from_spec(_spec)
sys.modules["sedan_prepare_inputs"] = prep
_spec.loader.exec_module(prep)

EXAMPLE_MANIFEST_PATH = _REPO_ROOT / "test" / "Panopticon.Geospatial.Tests" / "Examples" \
    / "sedan-ign-proof.json"

# Frozen selection identities from the accepted final freeze (audit record
# review-final-freeze-runtime-20261103T2230-0500): five primaries across three
# sectors and two alternates, ranks verbatim.
FROZEN_PRIMARY_IDS = ["r29c117", "r291c342", "r32c280", "r8c320", "r266c285"]
FROZEN_ALTERNATE_IDS = ["r122c275", "r69c19"]

# Synthetic coordinates for the frozen ids: inside the canonical whole-chunk
# window and inside the planned Sedan lat/lon box the C# example tests pin
# (49.6990..49.7050 / 4.9425..4.9515); truth_z values are non-zero frozen-style
# heights. utm_x/utm_y and wgs84_* are independent fixture columns.
SELECTION_ROWS = {
    "primary": [
        {"id": "r29c117", "rank": 1, "utm_x": 640012.3, "utm_y": 5507112.8,
         "wgs84_lat": 49.70201, "wgs84_lon": 4.94642, "truth_z": 213.66},
        {"id": "r291c342", "rank": 2, "utm_x": 640552.1, "utm_y": 5507348.4,
         "wgs84_lat": 49.70436, "wgs84_lon": 4.95121, "truth_z": 231.05},
        {"id": "r32c280", "rank": 3, "utm_x": 640093.9, "utm_y": 5507214.5,
         "wgs84_lat": 49.70305, "wgs84_lon": 4.94744, "truth_z": 207.52},
        {"id": "r8c320", "rank": 5, "utm_x": 639872.6, "utm_y": 5506941.2,
         "wgs84_lat": 49.70049, "wgs84_lon": 4.94310, "truth_z": 198.41},
        {"id": "r266c285", "rank": 172, "utm_x": 640310.8, "utm_y": 5507592.3,
         "wgs84_lat": 49.70468, "wgs84_lon": 4.94945, "truth_z": 244.87},
    ],
    "alternate": [
        {"id": "r122c275", "rank": 6, "utm_x": 640401.4, "utm_y": 5507060.7,
         "wgs84_lat": 49.70157, "wgs84_lon": 4.94850, "truth_z": 219.03},
        {"id": "r69c19", "rank": 7, "utm_x": 639901.5, "utm_y": 5506855.3,
         "wgs84_lat": 49.69980, "wgs84_lon": 4.94425, "truth_z": 191.28},
    ],
}

TILE_NAMES = tuple(pin["name"] for pin in freeze.TILE_PINS)
BANDED_WORKER_SHA256 = "52e935b5360c0d10563a581e650df3ac1637512eb30bc2cfc953d59cf12d2ab0"
SELECT_MODULE_SHA256 = freeze.SELECT_MODULE_SHA256
BASE_WORKER_SHA256 = freeze.EXPECTED_BASE_WORKER_SHA256

SEL_COLUMNS = [
    "id", "role", "rank", "row", "col", "utm_x", "utm_y", "l93_x", "l93_y",
    "game_x", "game_y", "wgs84_lat", "wgs84_lon",
    "truth_z", "sigma_fit", "slope_deg", "curv_bound", "density",
    "n2", "n3", "n4", "n5", "n6", "n9", "n17", "stratum", "sector",
]
CAND_COLUMNS = [
    "id", "row", "col", "utm_x", "utm_y", "l93_x", "l93_y", "n2", "density",
    "truth_z", "sigma_fit", "slope_deg", "curv_bound",
    "n3", "n4", "n5", "n6", "n9", "n17",
    "sector", "sector_dist", "stratum", "pool", "rank", "eligible", "fired",
]

RGE_MEMBER_PATHS = [
    "RGEALTI_2-0_1M_TIF_LAMB93-IGN69_D008_2019-10-14/RGEALTI/1_DONNEES_LIVRAISON_2021-01-00150"
    f"/RGEALTI_MNT_1M_TIF_LAMB93_IGN69_D008_20210118/RGEALTI_FXX_{tile}_MNT_LAMB93_IGN69.tif"
    for tile in ("0839_6958", "0839_6959", "0840_6958", "0840_6959", "0841_6958", "0841_6959")
]
ORTHO_MEMBER_PATHS = [
    "ORTHOHR_1-0_RVB-0M20_JP2-E080_LAMB93_D008_2022-01-01/ORTHOHR/1_DONNEES_LIVRAISON_2023-06-00036"
    f"/OHR_RVB_0M20_JP2-E080_LAMB93_D08-2022/08-2022-{lot}-LA93-0M20-E080.jp2"
    for lot in ("0835-6960", "0840-6959")
]
RGE_AUX_MEMBER_PATHS = [
    "RGEALTI_2-0_1M_TIF_LAMB93-IGN69_D008_2019-10-14/RGEALTI/3_SUPPLEMENTS_LIVRAISON_2021-01-00150"
    f"/RGEALTI_MNT_1M_TIF_LAMB93_IGN69_D008_20210118/{name}"
    for name in ("dalles.dbf", "dalles.prj", "dalles.shp", "dalles.shx")
]
BD_AUX_MEMBER_PATHS = [
    "ORTHOHR_1-0_RVB-0M20_JP2-E080_LAMB93_D008_2022-01-01/ORTHOHR/3_SUPPLEMENTS_LIVRAISON_2023-06-00036"
    f"/{name}"
    for name in ["dalles.shx", "index.dbf"] + [f"deci-{index}.xml" for index in range(13)]
]

# Prepared-raster geometry: source-corner bounds (storage +/- 4.572 m) outward-
# aligned to the native GSD. The DEM is 1.0 m, the RGB is 0.2 m.
PREPARED_RGB_WIDTH = 5899
PREPARED_RGB_HEIGHT = 5898
PREPARED_RGB_GT = [639837.4, 0.2, 0.0, 5507943.2, 0.0, -0.2]
PREPARED_DEM_GT = [639837.0, 1.0, 0.0, 5507944.0, 0.0, -1.0]
STATS_MINIMUM = 152.4
WINDOW_TE = ["639837.000", "5506763.000", "641018.000", "5507944.000"]
RGB_WINDOW_TE = ["639837.400", "5506763.600", "641017.200", "5507943.200"]
PLAYABLE_CORNERS_UTM = [(639841.997, 5506768.179), (640941.997, 5506768.179),
                        (640941.997, 5507868.179), (639841.997, 5507868.179)]


def sha256_bytes(data):
    return hashlib.sha256(data).hexdigest()


def sha256_file(path):
    return sha256_bytes(Path(path).read_bytes())


def fail_code(callable_):
    """(code, message): run callable_ and require a prep.Failure."""
    try:
        callable_()
    except prep.Failure as failure:
        return failure.code, str(failure)
    raise AssertionError("expected prep.Failure was not raised")


def tiny_pins():
    return tuple(
        {"name": name, "size": 4096 + 64 * index, "rel": f"lidarhd/tiny/{name}"}
        for index, name in enumerate(TILE_NAMES))


def selection_tsv_text():
    lines = ["\t".join(SEL_COLUMNS)]
    for role in ("primary", "alternate"):
        for record in SELECTION_ROWS[role]:
            values = {column: "" for column in SEL_COLUMNS}
            values.update({
                "id": record["id"], "role": role, "rank": str(record["rank"]),
                "row": record["id"][1:].split("c")[0], "col": record["id"].split("c")[1],
                "utm_x": repr(record["utm_x"]), "utm_y": repr(record["utm_y"]),
                "l93_x": "840123.45", "l93_y": "6957123.45",
                "game_x": repr(record["utm_x"] / prep.METERS_PER_GAME_UNIT),
                "game_y": repr(record["utm_y"] / prep.METERS_PER_GAME_UNIT),
                "wgs84_lat": repr(record["wgs84_lat"]),
                "wgs84_lon": repr(record["wgs84_lon"]),
                "truth_z": repr(record["truth_z"]), "sigma_fit": "0.012",
                "slope_deg": "2.5", "curv_bound": "0.004", "density": "12.0",
                "n2": "40", "n3": "30", "n4": "20", "n5": "10", "n6": "6",
                "n9": "3", "n17": "1", "stratum": "s2h", "sector": "s00",
            })
            lines.append("\t".join(values[column] for column in SEL_COLUMNS))
    return ("\n".join(lines) + "\n").encode("utf-8")


def candidates_tsv_text():
    lines = ["\t".join(CAND_COLUMNS)]
    for role in ("primary", "alternate"):
        for record in SELECTION_ROWS[role]:
            values = {column: "" for column in CAND_COLUMNS}
            values.update({
                "id": record["id"], "row": record["id"][1:].split("c")[0],
                "col": record["id"].split("c")[1],
                "utm_x": repr(record["utm_x"]), "utm_y": repr(record["utm_y"]),
                "l93_x": "840123.45", "l93_y": "6957123.45", "n2": "40",
                "density": "12.0", "truth_z": repr(record["truth_z"]),
                "sigma_fit": "0.012", "slope_deg": "2.5", "curv_bound": "0.004",
                "n3": "30", "n4": "20", "n5": "10", "n6": "6", "n9": "3",
                "n17": "1", "sector": "s00", "sector_dist": "0", "stratum": "s2h",
                "pool": "primary" if role == "primary" else "alternate",
                "rank": str(record["rank"]), "eligible": "1", "fired": "S1H",
            })
            lines.append("\t".join(values[column] for column in CAND_COLUMNS))
    return ("\n".join(lines) + "\n").encode("utf-8")


def build_member_records():
    """27 synthetic extraction members mirroring the executed receipt shape:
    6 RGEALTI MNT tifs, 2 BD ORTHO jp2s, 19 aux members; unique basenames."""
    records = []
    for index, path in enumerate(RGE_MEMBER_PATHS):
        records.append({"archive": "RGEALTI_2-0_1M_TIF_LAMB93-IGN69_D008_2019-10-14.7z",
                        "path": path, "size": 1_000_000 + 17 * index,
                        "subdir": "01_rge"})
    for index, path in enumerate(ORTHO_MEMBER_PATHS):
        records.append({"archive": "BDORTHO_1-0_RVB-0M20_JP2-E080_LAMB93_D008_2022-01-01.7z",
                        "path": path, "size": 5_000_000 + 23 * index,
                        "subdir": "18_jp2"})
    for index, path in enumerate(RGE_AUX_MEMBER_PATHS):
        records.append({"archive": "RGEALTI_2-0_1M_TIF_LAMB93-IGN69_D008_2019-10-14.7z",
                        "path": path, "size": 200 + 7 * index,
                        "subdir": "03_sup_rge"})
    for index, path in enumerate(BD_AUX_MEMBER_PATHS):
        records.append({"archive": "BDORTHO_1-0_RVB-0M20_JP2-E080_LAMB93_D008_2022-01-01.7z",
                        "path": path, "size": 300 + 7 * index,
                        "subdir": "20_aux"})
    assert len(records) == 27
    # The real extracted tree legitimately carries same-basename aux sidecars in
    # different subdirectories (dalles.shx in both supplements).
    assert len({os.path.basename(record["path"]) for record in records}) < 27
    assert len({record["path"] for record in records}) == 27
    for index, record in enumerate(records):
        record["basename"] = os.path.basename(record["path"])
        record["content"] = bytes((index * 31 + offset) % 251
                                  for offset in range(record["size"]))
        record["sha256"] = sha256_bytes(record["content"])
    return records


def fake_cs2cs_line(easting, northing):
    lon = 4.9425 + (easting - 639841.997) * 1e-5
    lat = 49.6990 + (northing - 5506768.179) * 1e-5
    return f"{lon:.9f} {lat:.9f}"


def warp_geometry(argv):
    """Raster geometry gdalwarp writes for -te/-tr: pixel count rounds the span."""
    extent = [float(part) for part in argv[argv.index("-te") + 1:argv.index("-te") + 5]]
    gsd = float(argv[argv.index("-tr") + 1])
    width = math.floor((extent[2] - extent[0]) / gsd + 0.5)
    height = math.floor((extent[3] - extent[1]) / gsd + 0.5)
    return {"size": [width, height],
            "geoTransform": [extent[0], gsd, 0.0, extent[3], 0.0, -gsd]}


class FakeGdal:
    """The offline GDAL/PROJ seam: records every argv, never spawns anything.
    Warp calls write deterministic stand-in bytes at their destination path so
    the receipt hashing sees a real file, and the header a later gdalinfo reports
    is the geometry that warp's argv implies (optionally rewritten by
    warp_geometry_hook). /vsistdout/ AAIGrid calls return the canned source-ortho
    average text; gdallocationinfo returns canned prepared-DEM samples (optionally
    the NoData sentinel)."""

    def __init__(self, dem_sources, rgb_sources, aaigrid_values=None, allocation=None):
        self.dem_sources = list(dem_sources)
        self.rgb_sources = list(rgb_sources)
        self.aaigrid_values = dict(aaigrid_values or {})
        self.allocation = dict(allocation or {})
        self.calls = []
        self.dem_warp_result = (0, b"")
        self.timed_out_argv_head = None
        self.aaigrid_text_override = None
        self.dem_source_gsd_override = None
        self.rgb_source_gsd_override = None
        self.warp_geometry = {}
        self.warp_geometry_hook = None

    def run(self, argv, timeout_seconds=prep.TOOL_TIMEOUT_SECONDS, input_bytes=None,
            cap_bytes=prep.TOOL_OUTPUT_CAP_BYTES):
        del timeout_seconds, cap_bytes
        self.calls.append({"argv": [str(part) for part in argv],
                           "input": None if input_bytes is None else bytes(input_bytes)})
        tool = os.path.basename(argv[0])
        if tool == "cs2cs":
            lines = input_bytes.decode("ascii").strip().splitlines()
            out = "".join(fake_cs2cs_line(float(part.split()[0]), float(part.split()[1]))
                          + "\n" for part in lines)
            return prep.ToolRun(0, out.encode("ascii"), b"", False, 0.001)
        if tool == "gdalinfo":
            return prep.ToolRun(0, self._gdalinfo_json(argv).encode("utf-8"), b"", False, 0.001)
        if tool == "gdalwarp":
            return self._gdalwarp(argv)
        if tool == "gdallocationinfo":
            easting, northing = float(argv[-2]), float(argv[-1])
            if self.timed_out_argv_head and argv[0] == self.timed_out_argv_head:
                return prep.ToolRun(-9, b"", b"", True, 999.0)
            value = self.allocation.get((round(easting, 3), round(northing, 3)),
                                        150.0 + (easting % 7.0))
            return prep.ToolRun(0, f"{value!r}\n".encode("ascii"), b"", False, 0.001)
        raise AssertionError(f"FakeGdal does not handle tool {tool}: {argv}")

    def _gdalinfo_json(self, argv):
        path = argv[-1]
        stats = "-stats" in argv
        if path.endswith(prep.DEM_NAME):
            geometry = self.warp_geometry[path]
            info = {"size": geometry["size"], "geoTransform": geometry["geoTransform"],
                    "bands": [{"type": "Float32", "noDataValue": prep.DEM_NODATA}],
                    "coordinateSystem": {"wkt": 'ID["EPSG",32631]'}}
            if stats:
                info["bands"][0]["minimum"] = STATS_MINIMUM
                info["bands"][0]["maximum"] = 312.9
            return json.dumps(info)
        if path.endswith(prep.RGB_NAME):
            geometry = self.warp_geometry[path]
            info = {"size": geometry["size"], "geoTransform": geometry["geoTransform"],
                    "bands": [{"type": "Byte", "noDataValue": None}],
                    "coordinateSystem": {"wkt": 'ID["EPSG",32631]'}}
            return json.dumps(info)
        if path in self.dem_sources:
            info = {"size": [1024, 1024], "geoTransform": [639000.0, 1.0, 0.0,
                                                           5507000.0, 0.0, -1.0],
                    "bands": [{"type": "Float32", "noDataValue": prep.DEM_NODATA}],
                    "coordinateSystem": {"wkt": 'ID["EPSG",32631]'}}
            if self.dem_source_gsd_override:
                info["geoTransform"][1] = self.dem_source_gsd_override
            return json.dumps(info)
        if path in self.rgb_sources:
            info = {"size": [5000, 5000], "geoTransform": [835000.0, 0.2, 0.0,
                                                           6960000.0, 0.0, -0.2],
                    "bands": [{"type": "Byte", "noDataValue": None}],
                    "coordinateSystem": {"wkt": 'ID["EPSG",32631]'}}
            if self.rgb_source_gsd_override:
                info["geoTransform"][1] = self.rgb_source_gsd_override
            return json.dumps(info)
        raise AssertionError(f"FakeGdal: unexpected gdalinfo path {path}")

    def _gdalwarp(self, argv):
        if "/vsistdout/" in argv:
            band = int(argv[argv.index("-b") + 1])
            if self.aaigrid_text_override is not None:
                text = self.aaigrid_text_override
            else:
                value = self.aaigrid_values.get(band, 0)
                text = (f"ncols 1\nnrows 1\nxllcorner 640134.497\nycorner 5507645.711\n"
                        f"cellsize 0.3048\nNODATA_value -9999\n{value}\n")
            return prep.ToolRun(0, text.encode("ascii"), b"", False, 0.001)
        destination = argv[-1]
        geometry = warp_geometry(argv)
        if self.warp_geometry_hook is not None:
            geometry = self.warp_geometry_hook(argv, geometry)
        self.warp_geometry[destination] = geometry
        payload = (b"FAKE-DEM-PIXELS\n" if "-dstnodata" in argv else b"FAKE-RGB-PIXELS\n")
        Path(destination).write_bytes(payload + sha256_bytes(payload).encode("ascii"))
        returncode, stderr = self.dem_warp_result if "-dstnodata" in argv else (0, b"")
        return prep.ToolRun(returncode, b"", stderr, False, 0.001)

    def warp_calls(self, tool="gdalwarp"):
        return [call for call in self.calls
                if os.path.basename(call["argv"][0]) == tool]


class PreparedTestBase(unittest.TestCase):
    """Shared plumbing: module-attribute patching with cleanup, /tmp/opencode
    cache roots, and the completed consumer-chain fixture."""

    def patch(self, name, value):
        old = getattr(prep, name)
        setattr(prep, name, value)
        self.addCleanup(setattr, prep, name, old)

    def patch_freeze(self, name, value):
        old = getattr(freeze, name)
        setattr(freeze, name, value)
        self.addCleanup(setattr, freeze, name, old)

    def make_cache(self):
        tmp = tempfile.TemporaryDirectory(prefix="prep-resume-", dir="/tmp/opencode")
        self.addCleanup(tmp.cleanup)
        cache = os.path.join(tmp.name, "sedan")
        os.makedirs(cache)
        return cache

    def patch_pins(self):
        pins = tiny_pins()
        self.patch_freeze("TILE_PINS", pins)
        return pins

    def patch_spawn_guards(self):
        self.patch("check_mount_realness", lambda root: True)
        self.patch("statvfs", lambda path: type("U", (), {"f_bavail": 1 << 21,
                                                          "f_frsize": 4096})())

    def write_completed_chain(self, cache, pins=None):
        """Write the full completed consumer chain: freeze stage (state, tables,
        manifest, receipt), the AUGMENTED integrity manifest with the worker
        override block, six tiny LAZ tiles at the pinned rel paths, and the
        completed extract stage with a 27-member receipt + files. Returns a
        facts dict."""
        pins = pins or self.patch_pins()
        records = build_member_records()
        extract_total = sum(record["size"] for record in records)
        self.patch("EXTRACT_TOTAL_BYTES_EXPECTED", extract_total)
        stage = os.path.join(cache, "freeze", "sedan-freeze-fixture")
        os.makedirs(os.path.join(stage, "state"))
        Path(os.path.join(stage, "COMPLETED")).write_bytes(b"")
        tile_files = {}
        for pin in pins:
            path = os.path.join(cache, pin["rel"])
            os.makedirs(os.path.dirname(path), exist_ok=True)
            content = bytes((offset * 13 + len(pin["name"])) % 251
                            for offset in range(pin["size"]))
            Path(path).write_bytes(content)
            tile_files[pin["name"]] = sha256_bytes(content)
        candidates_path = os.path.join(stage, "candidates.tsv")
        selection_path = os.path.join(stage, "selection.tsv")
        manifest_path = os.path.join(stage, "frozen-manifest.json")
        receipt_path = os.path.join(stage, "freeze-receipt.json")
        Path(candidates_path).write_bytes(candidates_tsv_text())
        Path(selection_path).write_bytes(selection_tsv_text())
        manifest = {
            "protocol_version": groundtruth.PROTOCOL_VERSION,
            "generated_at": "2026-11-03T21:29:31.000-05:00",
            "mode": "final",
            "aoi": {"centre_utm": [640391.997, 5507318.179], "half_m": 550.0},
            "map_transform": {"extent_origin_utm": [639841.997, 5506768.179],
                              "lattice_spacing_m": groundtruth.LATTICE},
            "tables": {"candidates_sha256": sha256_file(candidates_path),
                       "selection_sha256": sha256_file(selection_path)},
            "selection": {"primaries": FROZEN_PRIMARY_IDS,
                          "alternates_recorded_never_activated": FROZEN_ALTERNATE_IDS},
        }
        Path(manifest_path).write_bytes(
            (json.dumps(manifest, indent=2, sort_keys=True) + "\n").encode("utf-8"))
        Path(receipt_path).write_bytes(
            (json.dumps({"mode": "freeze",
                         "canonical_digest": groundtruth.canonical_digest(manifest)},
                        indent=2) + "\n").encode("utf-8"))
        artifacts = {}
        for label, path in (("candidates_tsv", candidates_path),
                            ("selection_tsv", selection_path),
                            ("frozen_manifest", manifest_path),
                            ("receipt", receipt_path)):
            artifacts[label] = {"path": path, "sha256": sha256_file(path),
                                "size": os.path.getsize(path)}
        state = {
            "version": freeze.STATE_VERSION,
            "mode": "freeze",
            "started": "2026-11-03T21:29:31.000-05:00",
            "completed": "2026-11-03T21:31:15.000-05:00",
            "canonical_digest": groundtruth.canonical_digest(manifest),
            "artifacts": artifacts,
            "tiles": {pin["name"]: {"sha256": tile_files[pin["name"]],
                                    "size": pin["size"]} for pin in pins},
            "worker": {"source": "manifest override",
                       "sha256": BANDED_WORKER_SHA256,
                       "base_worker_sha256": BASE_WORKER_SHA256,
                       "select_module_sha256": SELECT_MODULE_SHA256},
        }
        Path(os.path.join(stage, "state", "state.json")).write_bytes(
            (json.dumps(state, indent=2, sort_keys=True) + "\n").encode("utf-8"))
        integrity = {pin["name"]: {"size": pin["size"],
                                   "sha256": tile_files[pin["name"]],
                                   "footprint_l93": {"x_min": 839000.0 + 1000 * index,
                                                     "y_min": 6956000.0,
                                                     "x_max": 840000.0 + 1000 * index,
                                                     "y_max": 6957000.0},
                                   "rel_path": pin["rel"]}
                     for index, pin in enumerate(pins)}
        integrity["worker"] = {"rel_path": "sedan_lidar_probe_worker_banded.py",
                               "sha256": BANDED_WORKER_SHA256}
        Path(os.path.join(cache, "freeze", "lidar-integrity-manifest.json")).write_bytes(
            (json.dumps(integrity, indent=2, sort_keys=True) + "\n").encode("utf-8"))
        extract_stage = os.path.join(cache, "extracted", "sedan-extract-fixture")
        os.makedirs(os.path.join(extract_stage, "files"))
        Path(os.path.join(extract_stage, "COMPLETED")).write_bytes(b"")
        lines = ["# Sedan selective extraction receipt",
                 "finished: 2026-11-03T14:14:10.862-05:00",
                 f"members: {len(records)}"]
        for record in records:
            target = os.path.join(extract_stage, "files", record["subdir"],
                                  record["basename"])
            os.makedirs(os.path.dirname(target), exist_ok=True)
            Path(target).write_bytes(record["content"])
            lines.append(f"member: {record['archive']} {record['path']} "
                         f"declared={record['size']} got={record['size']} "
                         f"sha256={record['sha256']}")
        lines.append(f"total declared: {extract_total}")
        lines.append(f"total got: {extract_total}")
        lines.append("edition: RGEALTI 2-0 1M TIF LAMB93-IGN69 D008 2019-10-14")
        lines.append("edition: ORTHOHR 1-0 RVB-0M20 JP2-E080 LAMB93 D08-2022")
        Path(os.path.join(extract_stage, "RECEIPT.txt")).write_bytes(
            ("\n".join(lines) + "\n").encode("utf-8"))
        raster_suffixes = prep.RASTER_SUFFIXES
        elevation = [os.path.join(extract_stage, "files", record["subdir"],
                                  record["basename"])
                     for record in records if "RGEALTI" in record["path"]
                     and record["basename"].lower().endswith(raster_suffixes)]
        imagery = [os.path.join(extract_stage, "files", record["subdir"],
                                record["basename"])
                   for record in records if "ORTHO" in record["path"]
                   and record["basename"].lower().endswith(raster_suffixes)]
        return {
            "stage": stage, "manifest": manifest, "state": state,
            "tiles_sha256": tile_files, "pins": pins,
            "extract_stage": extract_stage, "extract_total": extract_total,
            "elevation": elevation, "imagery": imagery,
            "integrity_sha256": sha256_file(os.path.join(
                cache, "freeze", "lidar-integrity-manifest.json")),
            "canonical_digest": groundtruth.canonical_digest(manifest),
        }

    def default_aaigrid_values(self):
        # Canned SOURCE-ORTHO area averages per band (identical for both
        # controls: the seam cannot distinguish them, and independence is
        # asserted through the argv/source routing instead).
        return {1: 90, 2: 140, 3: 60}

    def fake_gdal(self, facts, allocation=None):
        # The seam fakes and the spawn guards travel together: a faked run must
        # never hit the real mount or space guards on a /tmp root.
        self.patch_spawn_guards()
        seam = FakeGdal(facts["elevation"], facts["imagery"],
                        self.default_aaigrid_values(), allocation or {})
        self.patch("run_tool", seam.run)
        return seam

    def tree_hashes(self, root):
        result = {}
        for dirpath, _dirnames, filenames in os.walk(root):
            for filename in filenames:
                path = os.path.join(dirpath, filename)
                result[os.path.relpath(path, root)] = sha256_file(path)
        return result


class PinnedConstantsTests(PreparedTestBase):
    def test_extract_pin_matches_producer_and_executed_receipt(self):
        # The executed extraction (~/log/track-r/sedan-extraction-execution-
        # 20261103T1405/receipt.md) recorded exactly 27 members / 205266077
        # bytes; the committed producer pin agrees with both.
        self.assertEqual(prep.EXTRACT_MEMBERS_EXPECTED, 27)
        self.assertEqual(prep.EXTRACT_MEMBERS_EXPECTED, sedan_extract.SELECTED_MEMBER_COUNT)
        self.assertEqual(prep.EXTRACT_TOTAL_BYTES_EXPECTED, 205_266_077)
        self.assertEqual(prep.EXTRACT_TOTAL_BYTES_EXPECTED, sedan_extract.SELECTED_TOTAL)

    def test_operator_policy_pins(self):
        self.assertEqual(prep.NO_DATA_SEARCH_RADIUS_M, 3.048)
        self.assertEqual(prep.NO_DATA_SEARCH_RADIUS_M, groundtruth.LATTICE)
        self.assertNotEqual(prep.NO_DATA_SEARCH_RADIUS_M, 25.0)
        self.assertEqual(prep.IMAGERY_TOLERANCE_BYTES, 2)
        self.assertNotEqual(prep.IMAGERY_TOLERANCE_BYTES, 8)
        self.assertEqual(prep.VERTICAL_TOLERANCE_M, 1.0)
        self.assertEqual(prep.BUDGET_B_GAME_UNITS, 0.0390625)
        self.assertEqual(prep.VERTICAL_DATUM_LABEL, "NGF-IGN69")
        self.assertEqual(prep.METERS_PER_GAME_UNIT, 0.3048)
        self.assertEqual(prep.PREVIEW_TEXTURES, {"grass": "GrassType1"})
        self.assertEqual(prep.TARGET_CRS, {"type": "utm", "zone": 31,
                                           "hemisphere": "north"})
        self.assertEqual(len(prep.UNCERTAINTIES), 5)

    def test_protocol_constants_accept_the_real_siblings(self):
        prep.check_protocol_constants()

    def test_producer_entrypoint_is_unreachable_from_source(self):
        source = Path(prep.__file__).read_text(encoding="utf-8")
        self.assertNotIn("sedan_lidar_integrity", source)
        self.assertNotIn("prepare-integrity", source)


class GridGeometryTests(PreparedTestBase):
    def test_canonical_grid_is_three_whole_chunks_with_apron(self):
        grid = prep.compute_chunk_grid(639841.997, 5506768.179, groundtruth.DEFAULT_AOI.size,
                                       groundtruth.LATTICE)
        self.assertEqual(grid["n_chunks"], 3)
        self.assertEqual(grid["chunk_m"], 128 * groundtruth.LATTICE)
        self.assertAlmostEqual(grid["chunk_m"], 390.144, places=9)
        self.assertAlmostEqual(grid["grid_m"], 1170.432, places=9)
        self.assertEqual(grid["grid_points"], 385)
        self.assertAlmostEqual(grid["apron_m"], groundtruth.LATTICE, places=9)
        playable = grid["playable_bounds"]
        self.assertAlmostEqual(playable.x_min, 639841.997, places=6)
        self.assertAlmostEqual(playable.y_min, 5506768.179, places=6)
        self.assertAlmostEqual(playable.x_max, 640941.997, places=6)
        self.assertAlmostEqual(playable.y_max, 5507868.179, places=6)
        storage = grid["grid_bounds"]
        self.assertAlmostEqual(storage.x_max, 641012.429, places=6)
        self.assertAlmostEqual(storage.y_max, 5507938.611, places=6)
        minimal = grid["source_minimal_bounds"]
        self.assertAlmostEqual(minimal.x_min, 639837.425, places=6)
        self.assertAlmostEqual(minimal.y_min, 5506763.607, places=6)
        self.assertAlmostEqual(minimal.x_max, 641017.001, places=6)
        self.assertAlmostEqual(minimal.y_max, 5507943.183, places=6)
        self.assertAlmostEqual(grid["window"].x_min, 639837.0, places=9)
        self.assertAlmostEqual(grid["window"].y_min, 5506763.0, places=9)
        self.assertAlmostEqual(grid["window"].x_max, 641018.0, places=9)
        self.assertAlmostEqual(grid["window"].y_max, 5507944.0, places=9)
        self.assertAlmostEqual(grid["rgb_window"].x_min, 639837.4, places=6)
        self.assertAlmostEqual(grid["rgb_window"].y_min, 5506763.6, places=6)
        self.assertAlmostEqual(grid["rgb_window"].x_max, 641017.2, places=6)
        self.assertAlmostEqual(grid["rgb_window"].y_max, 5507943.2, places=6)

    def test_old_storage_window_leaves_apron_index_minus_one_uncovered(self):
        grid = prep.compute_chunk_grid(639841.997, 5506768.179, 1100.0, 3.048)
        code, message = fail_code(lambda: prep.assert_extent_covers_apron(
            grid["grid_bounds"], grid, "storage window"))
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("global index -1 ", message)

    def test_apron_index_385_footprint_is_the_closing_sample(self):
        grid = prep.compute_chunk_grid(639841.997, 5506768.179, 1100.0, 3.048)
        self.assertEqual(prep.apron_index_range(grid)[-1], 385)
        self.assertEqual(len(prep.apron_index_range(grid)), 387)
        centre = 639841.997 + 385 * 3.048
        self.assertAlmostEqual(centre - groundtruth.HALF_FOOTPRINT, 641013.953, places=6)
        self.assertAlmostEqual(centre + groundtruth.HALF_FOOTPRINT, 641017.001, places=6)

    def test_planned_windows_cover_every_apron_footprint_in_two_dimensions(self):
        grid = prep.compute_chunk_grid(639841.997, 5506768.179, 1100.0, 3.048)
        half = groundtruth.HALF_FOOTPRINT
        origin_x, origin_y = grid["origin"]
        indices = prep.apron_index_range(grid)
        uncovered = []
        for window in (grid["window"], grid["rgb_window"]):
            for ix in indices:
                left = origin_x + ix * grid["lattice_m"] - half
                right = origin_x + ix * grid["lattice_m"] + half
                for iy in indices:
                    bottom = origin_y + iy * grid["lattice_m"] - half
                    top = origin_y + iy * grid["lattice_m"] + half
                    if not (window.x_min <= left and right <= window.x_max
                            and window.y_min <= bottom and top <= window.y_max):
                        uncovered.append((ix, iy))
        self.assertEqual(uncovered, [])
        prep.assert_planned_windows_cover_apron(grid)

    def test_window_covers_every_frozen_named_bound(self):
        grid = prep.compute_chunk_grid(639841.997, 5506768.179, 1100.0, 3.048)
        bounds = prep.named_half_footprint_bounds(SELECTION_ROWS, groundtruth.HALF_FOOTPRINT)
        self.assertEqual(len(bounds), 7)
        prep.assert_window_covers_named_bounds(grid, bounds)

    def test_window_miss_is_a_guard_failure(self):
        grid = prep.compute_chunk_grid(639841.997, 5506768.179, 1100.0, 3.048)
        outside = [{"id": "r-offmap", "role": "primary", "x_min": 639000.0,
                    "y_min": 5506000.0, "x_max": 639003.0, "y_max": 5506003.0}]
        code, message = fail_code(
            lambda: prep.assert_window_covers_named_bounds(grid, outside))
        self.assertEqual(code, prep.EXIT_GUARD)
        self.assertIn("r-offmap", message)

    def test_quarter_and_three_quarter_centres_stay_inside_the_aoi_square(self):
        grid = prep.compute_chunk_grid(639841.997, 5506768.179, 1100.0, 3.048)
        points = [(c["name"], c["easting"], c["northing"])
                  for c in prep.rgb_control_grid(grid)["controls"]]
        prep.assert_points_inside_playable_area(grid, points)

    def test_centre_outside_the_aoi_square_is_refused(self):
        grid = prep.compute_chunk_grid(639841.997, 5506768.179, 1100.0, 3.048)
        code, _message = fail_code(lambda: prep.assert_points_inside_playable_area(
            grid, [("c", 639841.997 + 1150.0, 5506768.179 + 500.0)]))
        self.assertEqual(code, prep.EXIT_GUARD)

    def test_lattice_drift_is_a_state_failure(self):
        code, _message = fail_code(lambda: prep.compute_chunk_grid(
            639841.997, 5506768.179, 1100.0, 3.0))
        self.assertEqual(code, prep.EXIT_STATE)


class SelectionTableTests(PreparedTestBase):
    def write(self, text):
        self.path.write_text(text, encoding="utf-8")

    def setUp(self):
        tmp = tempfile.TemporaryDirectory(prefix="prep-sel-", dir="/tmp/opencode")
        self.addCleanup(tmp.cleanup)
        self.path = Path(tmp.name) / "selection.tsv"
        self.path.write_bytes(selection_tsv_text())

    def test_parses_the_frozen_five_plus_two(self):
        rows = prep.parse_selection_tsv(str(self.path))
        self.assertEqual([record["id"] for record in rows["primary"]], FROZEN_PRIMARY_IDS)
        self.assertEqual([record["id"] for record in rows["alternate"]],
                         FROZEN_ALTERNATE_IDS)
        self.assertEqual(rows["primary"][0]["truth_z"], repr(213.66))

    def test_primary_count_floor(self):
        kept = [line for line in selection_tsv_text().decode("utf-8").splitlines()
                if not line.startswith("r8c320\t")]
        self.write("\n".join(kept) + "\n")
        code, message = fail_code(lambda: prep.parse_selection_tsv(str(self.path)))
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("primaries", message)

    def test_alternate_count_floor(self):
        kept = [line for line in selection_tsv_text().decode("utf-8").splitlines()
                if not line.startswith("r69c19\t")]
        self.write("\n".join(kept) + "\n")
        code, _message = fail_code(lambda: prep.parse_selection_tsv(str(self.path)))
        self.assertEqual(code, prep.EXIT_STATE)

    def test_duplicate_id_is_refused(self):
        lines = selection_tsv_text().decode("utf-8").splitlines()
        lines[-1] = lines[-1].replace("r69c19", "r122c275")
        self.write("\n".join(lines) + "\n")
        code, message = fail_code(lambda: prep.parse_selection_tsv(str(self.path)))
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("not unique", message)

    def test_pool_ranks_are_not_dense_and_still_accepted(self):
        # The accepted freeze's primaries carry pool ranks 1, 2, 3, 5, 172; a
        # dense 1..N requirement would reject the real frozen table.
        rows = prep.parse_selection_tsv(str(self.path))
        self.assertEqual([int(record["rank"]) for record in rows["primary"]],
                         [1, 2, 3, 5, 172])

    def test_duplicate_rank_is_refused(self):
        lines = selection_tsv_text().decode("utf-8").splitlines()
        lines[1] = lines[1].replace("\t1\t", "\t2\t", 1)
        self.write("\n".join(lines) + "\n")
        code, message = fail_code(lambda: prep.parse_selection_tsv(str(self.path)))
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("distinct positive integers", message)

    def test_missing_column_is_refused(self):
        lines = selection_tsv_text().decode("utf-8").splitlines()
        lines[0] = lines[0].replace("truth_z\t", "")
        self.write("\n".join(lines) + "\n")
        code, message = fail_code(lambda: prep.parse_selection_tsv(str(self.path)))
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("truth_z", message)

    def test_missing_file_and_symlink_are_refused(self):
        self.path.unlink()
        code, _message = fail_code(lambda: prep.parse_selection_tsv(str(self.path)))
        self.assertEqual(code, prep.EXIT_STATE)
        self.path.symlink_to(self.path.parent / "elsewhere.tsv")
        code, _message = fail_code(lambda: prep.parse_selection_tsv(str(self.path)))
        self.assertEqual(code, prep.EXIT_STATE)


class RelativePathGuardTests(unittest.TestCase):
    def test_accepts_simple_relative_paths_and_normalizes_backslashes(self):
        self.assertEqual(prep.validate_relative_source_path("prepared/sedan-dem-32631.tif"),
                         "prepared/sedan-dem-32631.tif")
        self.assertEqual(prep.validate_relative_source_path("prepared\\sub\\x.tif"),
                         "prepared/sub/x.tif")

    def test_refuses_absolute_url_parent_empty_and_nul_forms(self):
        for bad in ("/abs/x.tif", "//share/x.tif", "http://x/y.tif", "a/../b.tif",
                    "./a.tif", "a//b.tif", "", "a\x00b"):
            code, _message = fail_code(
                lambda bad=bad: prep.validate_relative_source_path(bad))
            self.assertEqual(code, prep.EXIT_STATE, bad)


class KmlAndChronologyTests(PreparedTestBase):
    def journal(self):
        return prep.RunJournal(os.path.join(self.make_cache(), "commands.jsonl"))

    def test_kml_is_a_closed_quadrilateral_with_lon_lat_order(self):
        corners = [(49.701, 4.943), (49.701, 4.951), (49.705, 4.951), (49.705, 4.943)]
        root = ET.fromstring(prep.extent_kml_bytes(corners).decode("utf-8"))
        ns = {"kml": "http://www.opengis.net/kml/2.2"}
        placemark = root.find(".//kml:Placemark", ns)
        self.assertEqual(placemark.get("id"), prep.EXTENT_FEATURE_ID)
        coordinates = placemark.find(".//kml:coordinates", ns).text.split()
        self.assertEqual(len(coordinates), 5)
        points = [tuple(float(v) for v in pair.split(",")[:2]) for pair in coordinates]
        self.assertEqual(points[0], points[-1])
        self.assertEqual(points[0], (4.943, 49.701))

    def test_kml_needs_exactly_four_corners(self):
        code, _message = fail_code(lambda: prep.extent_kml_bytes([(49.7, 4.9)] * 3))
        self.assertEqual(code, prep.EXIT_STATE)

    def test_batched_cs2cs_converts_each_point_once(self):
        self.patch("run_tool", lambda argv, **kwargs: prep.ToolRun(
            0, "\n".join(fake_cs2cs_line(639841.997 + 10.0 * index, 5506768.179)
                         for index in range(4)).encode("ascii"), b"", False, 0.001))
        journal = self.journal()
        journal.freeze_verified_at = "2026-11-04T00:00:00.000-05:00"
        points = [(639841.997 + 10.0 * index, 5506768.179) for index in range(4)]
        converted = prep.convert_utm_batch(journal, points)
        self.assertEqual(len(converted), 4)
        self.assertAlmostEqual(converted[0][0], 49.699, places=9)

    def test_cs2cs_short_read_is_refused(self):
        self.patch("run_tool", lambda argv, **kwargs: prep.ToolRun(
            0, b"4.943000000 49.699000000\n", b"", False, 0.001))
        journal = self.journal()
        journal.freeze_verified_at = "2026-11-04T00:00:00.000-05:00"
        code, message = fail_code(lambda: prep.convert_utm_batch(
            journal, [(639841.997, 5506768.179), (640012.3, 5507112.8)]))
        self.assertEqual(code, prep.EXIT_GUARD)
        self.assertIn("2 inputs", message)

    def test_cs2cs_error_line_and_bad_range_are_refused(self):
        journal = self.journal()
        journal.freeze_verified_at = "2026-11-04T00:00:00.000-05:00"
        self.patch("run_tool", lambda argv, **kwargs: prep.ToolRun(
            0, b"4.943000000 49.699000000\n*ERROR*\n", b"", False, 0.001))
        code, _message = fail_code(lambda: prep.convert_utm_batch(
            journal, [(639841.997, 5506768.179), (640012.3, 5507112.8)]))
        self.assertEqual(code, prep.EXIT_GUARD)
        self.patch("run_tool", lambda argv, **kwargs: prep.ToolRun(
            0, b"200.0 49.699000000\n", b"", False, 0.001))
        code, message = fail_code(lambda: prep.convert_utm_batch(
            journal, [(639841.997, 5506768.179)]))
        self.assertEqual(code, prep.EXIT_GUARD)
        self.assertIn("out-of-range", message)

    def test_subprocess_before_freeze_event_is_a_state_failure(self):
        self.patch("run_tool",
                   lambda argv, **kwargs: self.fail("run_tool must never be reached"))
        journal = self.journal()
        code, message = fail_code(
            lambda: journal.invoke(["cs2cs", "x"], height_semantic=False))
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("chronology", message)


class ManifestGenerationTests(PreparedTestBase):
    def build_manifest(self, origin_meters=152.4, selection_rows=None, rgb_controls=None):
        selection_rows = selection_rows or SELECTION_ROWS
        rgb_controls = rgb_controls or [
            {"name": "rgb-quarter-centre", "latitude": 49.70149, "longitude": 4.94720,
             "rgb": [90, 140, 60]},
            {"name": "rgb-three-quarter-centre", "latitude": 49.70217, "longitude": 4.95009,
             "rgb": [85, 135, 55]},
        ]
        dem_header = {"width": 1181, "height": 1181}
        rgb_header = {"width": PREPARED_RGB_WIDTH, "height": PREPARED_RGB_HEIGHT}
        return prep.build_manifest(
            selection_rows, rgb_controls, "prepared/sedan-dem-32631.tif",
            "prepared/sedan-ortho-32631.tif", "prepared/sedan-extent.kml",
            dem_header, rgb_header, origin_meters)

    def test_generated_manifest_matches_the_committed_example_key_sets(self):
        example = json.loads(EXAMPLE_MANIFEST_PATH.read_text(encoding="utf-8"))
        manifest = self.build_manifest()
        self.assertEqual(set(manifest), set(example))
        self.assertEqual({key for source in manifest["sources"] for key in source},
                         {key for source in example["sources"] for key in source})
        self.assertEqual({key for point in manifest["controlPoints"] for key in point},
                         {key for point in example["controlPoints"] for key in point})
        self.assertEqual(set(manifest["rasterPolicy"]), set(example["rasterPolicy"]))
        self.assertEqual(set(manifest["tolerances"]), set(example["tolerances"]))

    def test_generated_manifest_passes_the_self_validation(self):
        manifest = self.build_manifest()
        validated = prep.validate_generated_manifest(
            manifest, FROZEN_PRIMARY_IDS, forbidden_names=FROZEN_ALTERNATE_IDS)
        self.assertIs(validated, manifest)

    def test_real_values_never_placeholders(self):
        manifest = self.build_manifest()
        self.assertEqual(manifest["rasterPolicy"]["noDataSearchRadiusMeters"], 3.048)
        self.assertNotEqual(manifest["rasterPolicy"]["noDataSearchRadiusMeters"], 25.0)
        self.assertEqual(manifest["rasterPolicy"],
                         {"noData": "nearest-valid",
                          "noDataSearchRadiusMeters": 3.048,
                          "edge": "fail",
                          "imageryPixelsPerSample": 10})
        self.assertEqual(manifest["tolerances"]["verticalMeters"], 1.0)
        rgb_points = [point for point in manifest["controlPoints"] if "imageryRgb" in point]
        self.assertEqual(len(rgb_points), 2)
        for point in rgb_points:
            self.assertEqual(point["imageryToleranceBytes"], 2)
            self.assertNotEqual(point["imageryToleranceBytes"], 8)
            self.assertNotEqual(point["imageryRgb"], [0, 0, 0])
        elevation_points = [point for point in manifest["controlPoints"]
                            if "elevationMeters" in point]
        self.assertEqual([point["name"] for point in elevation_points], FROZEN_PRIMARY_IDS)
        for point in elevation_points:
            self.assertNotEqual(point["elevationMeters"], 0.0)
            self.assertGreater(point["elevationMeters"], 100.0)
        self.assertEqual(manifest["verticalPolicy"]["originMeters"], 152.4)
        self.assertNotEqual(manifest["verticalPolicy"]["originMeters"], 0.0)
        self.assertNotIn("EXAMPLE ONLY", json.dumps(manifest))
        for source in manifest["sources"]:
            self.assertNotIn("verticalAccuracyMeters", source)
            self.assertNotIn("horizontalAccuracyMeters", source)
        self.assertNotIn("5720",
                         json.dumps([source.get("verticalDatum")
                                     for source in manifest["sources"]]))
        sources = {source["role"]: source for source in manifest["sources"]}
        self.assertEqual(sources["elevation"]["verticalDatum"], "NGF-IGN69")
        self.assertIn("IGN, Licence Ouverte 2.0", sources["elevation"]["attribution"])
        self.assertIn("IGN, Licence Ouverte 2.0", sources["imagery"]["attribution"])
        self.assertIn("generated extent KML", sources["extent"]["attribution"])

    def test_alternates_are_never_activated(self):
        manifest = self.build_manifest()
        names = [point["name"] for point in manifest["controlPoints"]]
        for alternate in FROZEN_ALTERNATE_IDS:
            self.assertNotIn(alternate, names)
        poisoned = copy.deepcopy(manifest)
        poisoned["controlPoints"][5]["name"] = FROZEN_ALTERNATE_IDS[0]
        code, message = fail_code(lambda: prep.validate_generated_manifest(
            poisoned, FROZEN_PRIMARY_IDS, forbidden_names=FROZEN_ALTERNATE_IDS))
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("NEVER activated", message)

    def test_zero_elevation_and_zero_origin_are_refused(self):
        rows = copy.deepcopy(SELECTION_ROWS)
        rows["primary"][0]["truth_z"] = 0.0
        manifest = self.build_manifest(selection_rows=rows)
        code, message = fail_code(lambda: prep.validate_generated_manifest(
            manifest, FROZEN_PRIMARY_IDS))
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("zero/invalid elevation", message)
        manifest = self.build_manifest(origin_meters=0.0)
        code, message = fail_code(lambda: prep.validate_generated_manifest(
            manifest, FROZEN_PRIMARY_IDS))
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("originMeters", message)

    def test_example_radius_and_non_lattice_radii_are_refused(self):
        self.patch("NO_DATA_SEARCH_RADIUS_M", 25.0)
        code, message = fail_code(prep.check_no_data_radius)
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("25.0", message)
        self.patch("NO_DATA_SEARCH_RADIUS_M", 6.0)
        code, message = fail_code(prep.check_no_data_radius)
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("lattice", message)

    def test_accuracy_keys_and_example_marker_are_refused(self):
        manifest = self.build_manifest()
        poisoned = copy.deepcopy(manifest)
        poisoned["sources"][1]["verticalAccuracyMeters"] = 0.5
        code, _message = fail_code(lambda: prep.validate_generated_manifest(
            poisoned, FROZEN_PRIMARY_IDS))
        self.assertEqual(code, prep.EXIT_STATE)
        poisoned = copy.deepcopy(manifest)
        poisoned["sources"][1]["verticalDatum"] = "EPSG:5720"
        code, _message = fail_code(lambda: prep.validate_generated_manifest(
            poisoned, FROZEN_PRIMARY_IDS))
        self.assertEqual(code, prep.EXIT_STATE)
        poisoned = copy.deepcopy(manifest)
        poisoned["sources"][0]["label"] = "EXAMPLE ONLY - NOT MEASURED"
        code, _message = fail_code(lambda: prep.validate_generated_manifest(
            poisoned, FROZEN_PRIMARY_IDS))
        self.assertEqual(code, prep.EXIT_STATE)

    def test_mixed_or_wrong_count_control_shapes_are_refused(self):
        manifest = self.build_manifest()
        poisoned = copy.deepcopy(manifest)
        poisoned["controlPoints"][0]["imageryRgb"] = [1, 2, 3]
        code, _message = fail_code(lambda: prep.validate_generated_manifest(
            poisoned, FROZEN_PRIMARY_IDS))
        self.assertEqual(code, prep.EXIT_STATE)
        poisoned = copy.deepcopy(manifest)
        poisoned["controlPoints"] = poisoned["controlPoints"][:6]
        code, _message = fail_code(lambda: prep.validate_generated_manifest(
            poisoned, FROZEN_PRIMARY_IDS))
        self.assertEqual(code, prep.EXIT_STATE)


class BoundedMetadataTests(PreparedTestBase):
    def test_read_json_bound_enforces_the_size_bound(self):
        path = os.path.join(self.make_cache(), "big.json")
        self.patch("READ_BOUND_BYTES", 1_000_000)
        Path(path).write_bytes(b"{" + b" " * 1_100_000 + b"}")
        code, _message = fail_code(lambda: prep.read_json_bound(path, "big json"))
        self.assertEqual(code, prep.EXIT_STATE)

    def test_read_json_bound_refuses_symlinks_and_non_json(self):
        cache = self.make_cache()
        real = os.path.join(cache, "real.json")
        Path(real).write_text("{}", encoding="utf-8")
        link = os.path.join(cache, "link.json")
        os.symlink(real, link)
        code, _message = fail_code(lambda: prep.read_json_bound(link, "linked json"))
        self.assertEqual(code, prep.EXIT_STATE)
        Path(real).write_text("not json", encoding="utf-8")
        code, _message = fail_code(lambda: prep.read_json_bound(real, "bad json"))
        self.assertEqual(code, prep.EXIT_STATE)

    def test_ledger_event_bound_and_malformed_lines(self):
        path = os.path.join(self.make_cache(), "commands.jsonl")
        self.patch("LEDGER_MAX_EVENTS", 3)
        Path(path).write_text(
            "".join(json.dumps({"event": f"e{index}"}) + "\n" for index in range(4)),
            encoding="utf-8")
        code, _message = fail_code(lambda: prep.read_ledger(path))
        self.assertEqual(code, prep.EXIT_STATE)
        Path(path).write_text("{broken\n", encoding="utf-8")
        code, _message = fail_code(lambda: prep.read_ledger(path))
        self.assertEqual(code, prep.EXIT_STATE)


class GdalArgvPlanTests(unittest.TestCase):
    GRID = prep.compute_chunk_grid(639841.997, 5506768.179, 1100.0, 3.048)

    def test_dem_warp_plan_is_bilinear_float32_native_gsd_compression_none(self):
        sources = ["a.tif", "b.tif"]
        argv = prep.dem_warp_argv(self.GRID["window"], 1.0, sources, "out.tif")
        self.assertEqual(argv[0], "gdalwarp")
        self.assertEqual(argv[argv.index("-r") + 1], "bilinear")
        self.assertEqual(argv[argv.index("-ot") + 1], "Float32")
        self.assertEqual(argv[argv.index("-tr") + 1:argv.index("-tr") + 3], ["1.0", "1.0"])
        self.assertEqual(argv[argv.index("-co") + 1], "COMPRESS=NONE")
        self.assertEqual(float(argv[argv.index("-dstnodata") + 1]), prep.DEM_NODATA)
        self.assertEqual(argv[argv.index("-te") + 1:argv.index("-te") + 5], WINDOW_TE)
        self.assertEqual(argv[-1], "out.tif")
        self.assertEqual(argv[-3:-1], sources)

    def test_rgb_warp_plan_is_average_byte_at_native_gsd(self):
        argv = prep.rgb_warp_argv(self.GRID["rgb_window"], 0.2, ["o.jp2"], "rgb.tif")
        self.assertEqual(argv[argv.index("-r") + 1], "average")
        self.assertEqual(argv[argv.index("-ot") + 1], "Byte")
        self.assertEqual(argv[argv.index("-tr") + 1:argv.index("-tr") + 3], ["0.2", "0.2"])
        self.assertEqual(argv[argv.index("-co") + 1], "COMPRESS=NONE")
        self.assertEqual(argv[argv.index("-te") + 1:argv.index("-te") + 5], RGB_WINDOW_TE)
        self.assertEqual(argv[-1], "rgb.tif")

    def test_gdallocationinfo_plan_is_geoloc_valonly(self):
        argv = prep.gdallocationinfo_argv("dem.tif", 640012.3, 5507112.8)
        self.assertEqual(argv[:4], ["gdallocationinfo", "-geoloc", "-valonly", "dem.tif"])
        self.assertEqual(float(argv[-2]), 640012.3)


class RgbControlPositionTests(unittest.TestCase):
    def test_controls_sit_at_generated_pixel_indices_960_and_2880(self):
        grid = prep.compute_chunk_grid(639841.997, 5506768.179, 1100.0, 3.048)
        layout = prep.rgb_control_grid(grid)
        self.assertEqual(layout["pixels_per_axis"], 3840)
        self.assertAlmostEqual(layout["spacing_m"], 0.3048, places=12)
        self.assertEqual([c["name"] for c in layout["controls"]],
                         ["rgb-quarter-centre", "rgb-three-quarter-centre"])
        self.assertEqual([c["pixel"] for c in layout["controls"]], [(960, 960), (2880, 2880)])

    def test_centres_are_canonical_origin_plus_half_pixel_offsets(self):
        grid = prep.compute_chunk_grid(639841.997, 5506768.179, 1100.0, 3.048)
        quarter, three_quarter = prep.rgb_control_grid(grid)["controls"]
        self.assertAlmostEqual(quarter["easting"], 640134.7574, places=6)
        self.assertAlmostEqual(quarter["northing"], 5507060.9394, places=6)
        self.assertAlmostEqual(three_quarter["easting"], 640719.9734, places=6)
        self.assertAlmostEqual(three_quarter["northing"], 5507646.1554, places=6)

    def test_pixel_indices_follow_the_chunk_count(self):
        grid = prep.compute_chunk_grid(639841.997, 5506768.179, 400.0, 3.048)
        layout = prep.rgb_control_grid(grid)
        self.assertEqual(grid["n_chunks"], 2)
        self.assertEqual(layout["pixels_per_axis"], 2560)
        self.assertEqual([c["pixel"] for c in layout["controls"]], [(640, 640), (1920, 1920)])


class CoreRgbByteTests(unittest.TestCase):
    def test_halves_round_away_from_zero(self):
        for value, expected in ((0.5, 1), (127.5, 128), (128.5, 129), (167.5, 168),
                                (254.5, 255), (0.49999999999999994, 0)):
            self.assertEqual(prep.core_rgb_byte(value), expected, value)

    def test_values_clamp_to_the_byte_range(self):
        for value, expected in ((-3.0, 0), (-0.4, 0), (0.0, 0), (255.0, 255), (300.0, 255)):
            self.assertEqual(prep.core_rgb_byte(value), expected, value)

    def test_non_half_values_round_to_nearest(self):
        self.assertEqual(prep.core_rgb_byte(167.3228302001953125), 167)
        self.assertEqual(prep.core_rgb_byte(167.6), 168)


class AaigridValueTests(unittest.TestCase):
    HEADER = "ncols 1\nnrows 1\nxllcorner 0\nyllcorner 0\ncellsize 0.3048\nNODATA_value -9999\n"

    def run_of(self, text):
        return prep.ToolRun(0, text.encode("ascii"), b"", False, 0.001)

    def test_float_noise_and_wkt_tail_are_read_as_one_value(self):
        text = self.HEADER + "167.3228302001953125\nPROJCS[\"WGS 84 / UTM zone 31N\"]\n"
        self.assertEqual(prep.parse_aaigrid_value(self.run_of(text), "control"), 167)

    def test_half_value_rounds_away_from_zero(self):
        self.assertEqual(prep.parse_aaigrid_value(self.run_of(self.HEADER + "127.5\n"), "c"), 128)

    def test_multi_cell_grid_is_a_state_failure(self):
        text = self.HEADER.replace("ncols 1", "ncols 2") + "10 20\n"
        code, _message = fail_code(lambda: prep.parse_aaigrid_value(self.run_of(text), "c"))
        self.assertEqual(code, prep.EXIT_STATE)


class ControlReferenceArgvTests(unittest.TestCase):
    FOOTPRINT = {"x_min": 640134.605, "y_min": 5507060.787,
                 "x_max": 640134.9098, "y_max": 5507061.0918}

    def test_reference_is_a_one_cell_float_average_of_the_prepared_rgb(self):
        argv = prep.control_reference_argv("prepared/sedan-ortho-32631.tif", 2, self.FOOTPRINT)
        self.assertEqual(argv[0], "gdalwarp")
        self.assertEqual(argv[-1], "/vsistdout/")
        self.assertEqual(argv[-2], "prepared/sedan-ortho-32631.tif")
        self.assertEqual(argv[argv.index("-ts") + 1:argv.index("-ts") + 3], ["1", "1"])
        self.assertEqual(argv[argv.index("-r") + 1], "average")
        self.assertEqual(argv[argv.index("-ot") + 1], "Float32")
        self.assertEqual(argv[argv.index("-b") + 1], "2")
        self.assertEqual(argv[argv.index("-of") + 1], "AAIGrid")
        self.assertEqual(argv[argv.index("-t_srs") + 1], "EPSG:32631")
        self.assertNotIn("-tr", argv)
        self.assertNotIn("Byte", argv)

    def test_footprint_is_the_exact_0_3048_m_cell_around_the_centre(self):
        argv = prep.control_reference_argv("p.tif", 1, self.FOOTPRINT)
        extent = [float(part) for part in argv[argv.index("-te") + 1:argv.index("-te") + 5]]
        self.assertEqual(extent, [640134.605, 5507060.787, 640134.9098, 5507061.0918])


class _SeamJournal:
    """Routes journal.invoke straight to the FakeGdal seam; no ledger, no chronology."""

    def __init__(self, seam):
        self.seam = seam
        self.height_flags = []

    def invoke(self, argv, height_semantic, label, input_bytes=None):
        del label
        self.height_flags.append(height_semantic)
        return self.seam.run(argv, input_bytes=input_bytes)


class RgbControlResolveTests(unittest.TestCase):
    PREPARED_BYTES = b"PREPARED-RGB-BYTES\n"

    def setUp(self):
        os.makedirs("/tmp/opencode", exist_ok=True)
        tmp = tempfile.TemporaryDirectory(prefix="prep-rgbref-", dir="/tmp/opencode")
        self.addCleanup(tmp.cleanup)
        self.prepared = os.path.join(tmp.name, prep.RGB_NAME)
        Path(self.prepared).write_bytes(self.PREPARED_BYTES)
        self.seam = FakeGdal([], [], aaigrid_values={1: 90, 2: 140, 3: 60})
        self.journal = _SeamJournal(self.seam)
        self.grid = prep.compute_chunk_grid(639841.997, 5506768.179, 1100.0, 3.048)

    def resolve(self):
        return prep.resolve_rgb_controls(self.journal, self.prepared, self.grid)

    def test_expected_bytes_bind_the_prepared_source_hash_and_layout(self):
        result = self.resolve()
        self.assertEqual(result["source_sha256"], sha256_file(self.prepared))
        self.assertEqual(result["source_size"], len(self.PREPARED_BYTES))
        self.assertEqual(result["pixels_per_axis"], 3840)
        self.assertAlmostEqual(result["spacing_m"], 0.3048, places=12)
        self.assertAlmostEqual(result["footprint_half_m"], 0.1524, places=12)
        self.assertEqual([c["pixel"] for c in result["controls"]], [(960, 960), (2880, 2880)])
        self.assertEqual([c["rgb"] for c in result["controls"]], [[90, 140, 60], [90, 140, 60]])

    def test_reference_reads_only_the_prepared_rgb_on_0_3048_m_footprints(self):
        self.resolve()
        references = [call["argv"] for call in self.seam.warp_calls()
                      if "/vsistdout/" in call["argv"]]
        self.assertEqual(len(references), 6)
        centres = [(640134.7574, 5507060.9394), (640719.9734, 5507646.1554)]
        for index, argv in enumerate(references):
            easting, northing = centres[index // 3]
            self.assertEqual(argv[-2], self.prepared)
            self.assertFalse(any(part.endswith(".jp2") for part in argv))
            extent = [float(part) for part in argv[argv.index("-te") + 1:argv.index("-te") + 5]]
            expected = (easting - 0.1524, northing - 0.1524,
                        easting + 0.1524, northing + 0.1524)
            for got, want in zip(extent, expected):
                self.assertAlmostEqual(got, want, places=6)
        self.assertEqual(set(self.journal.height_flags), {False})

    def test_centres_convert_through_one_batched_cs2cs_call(self):
        result = self.resolve()
        cs2cs = self.seam.warp_calls("cs2cs")
        self.assertEqual(len(cs2cs), 1)
        lines = cs2cs[0]["input"].decode("ascii").strip().splitlines()
        self.assertEqual(len(lines), 2)
        self.assertAlmostEqual(float(lines[0].split()[0]), 640134.7574, places=6)
        self.assertAlmostEqual(float(lines[1].split()[1]), 5507646.1554, places=6)
        self.assertAlmostEqual(result["controls"][0]["longitude"], 4.9425 + 292.7604e-5,
                               places=6)
        self.assertAlmostEqual(result["controls"][1]["latitude"], 49.6990 + 877.9764e-5,
                               places=6)


class RealGdalControlReferenceTests(unittest.TestCase):
    """Real gdalwarp on author-created synthetic 0.2 m rasters (never IGN data). The
    analytic model is the area-weighted 0.3048 m bin average at the exact centre, the
    Core ImageryChunkGenerator footprint, rounded with core_rgb_byte."""

    CELLS = 40
    CELL_M = 0.2
    CENTRE = 4.1
    HALF_NEW = 0.1524
    HALF_OLD = 0.1

    def setUp(self):
        if shutil.which("gdalwarp") is None or shutil.which("gdal_translate") is None:
            self.skipTest("gdalwarp/gdal_translate not on PATH; real-GDAL synthetic "
                          "check not executed")
        os.makedirs("/tmp/opencode", exist_ok=True)
        tmp = tempfile.TemporaryDirectory(prefix="prep-realgdal-", dir="/tmp/opencode")
        self.addCleanup(tmp.cleanup)
        self.tmp = tmp.name

    def build_source(self, value_of):
        lines = [f"ncols {self.CELLS}", f"nrows {self.CELLS}", "xllcorner 0", "yllcorner 0",
                 f"cellsize {self.CELL_M}"]
        for row in range(self.CELLS):
            y_centre = self.CELLS * self.CELL_M - (row + 0.5) * self.CELL_M
            lines.append(" ".join(str(value_of((col + 0.5) * self.CELL_M, y_centre))
                                  for col in range(self.CELLS)))
        asc = os.path.join(self.tmp, "synthetic.asc")
        tif = os.path.join(self.tmp, "synthetic.tif")
        Path(asc).write_text("\n".join(lines) + "\n", encoding="ascii")
        self.run_ok(["gdal_translate", "-q", "-of", "GTiff", "-ot", "Byte",
                     "-a_srs", "EPSG:32631", asc, tif])
        return tif

    def run_ok(self, argv):
        run = prep.run_tool(argv)
        self.assertEqual(run.returncode, 0, run.stderr[:300])
        return run

    def footprint(self, half):
        return {"x_min": self.CENTRE - half, "y_min": self.CENTRE - half,
                "x_max": self.CENTRE + half, "y_max": self.CENTRE + half}

    def legacy_reference_argv(self, tif, half):
        """The superseded 0.2 m single-footprint reference, kept only to prove it fails."""
        lo, hi = self.CENTRE - half, self.CENTRE + half
        return ["gdalwarp", "-t_srs", "EPSG:32631", "-te", repr(lo), repr(lo), repr(hi), repr(hi),
                "-tr", "0.2", "0.2", "-r", "average", "-ot", "Float32", "-b", "1",
                "-of", "AAIGrid", tif, "/vsistdout/"]

    def analytic_average(self, value_of, half):
        lo, hi = self.CENTRE - half, self.CENTRE + half
        total = 0.0
        for col in range(self.CELLS):
            x0 = col * self.CELL_M
            overlap_x = max(0.0, min(hi, x0 + self.CELL_M) - max(lo, x0))
            if overlap_x == 0.0:
                continue
            for row in range(self.CELLS):
                y0 = row * self.CELL_M
                overlap_y = max(0.0, min(hi, y0 + self.CELL_M) - max(lo, y0))
                total += overlap_x * overlap_y * value_of(x0 + self.CELL_M / 2,
                                                          y0 + self.CELL_M / 2)
        return total / ((2 * half) ** 2)

    def gdal_float(self, run):
        values = [float(line) for line in run.stdout.decode("ascii").splitlines()
                  if line.strip() and (line.strip()[0].isdigit() or line.strip()[0] == "-")]
        self.assertEqual(len(values), 1)
        return values[0]

    def test_stripes_new_reference_matches_core_model_and_old_reference_fails(self):
        def value_of(x, y):
            return 240 if x < 4.2 else 20
        tif = self.build_source(value_of)
        model = self.analytic_average(value_of, self.HALF_NEW)
        model_byte = prep.core_rgb_byte(model)
        self.assertEqual(model_byte, 202)
        new_run = self.run_ok(prep.control_reference_argv(tif, 1, self.footprint(self.HALF_NEW)))
        old_run = self.run_ok(self.legacy_reference_argv(tif, self.HALF_OLD))
        self.assertAlmostEqual(self.gdal_float(new_run), model, delta=1e-3)
        self.assertLessEqual(abs(prep.parse_aaigrid_value(new_run, "new") - model_byte),
                             prep.IMAGERY_TOLERANCE_BYTES)
        self.assertGreater(abs(prep.parse_aaigrid_value(old_run, "old") - model_byte),
                           prep.IMAGERY_TOLERANCE_BYTES)

    def test_asymmetric_quadrants_new_reference_matches_core_model_and_old_fails(self):
        quadrants = {(False, False): 10, (True, False): 250,
                     (False, True): 60, (True, True): 200}

        def value_of(x, y):
            return quadrants[(x >= 4.2, y >= 4.2)]
        tif = self.build_source(value_of)
        model = self.analytic_average(value_of, self.HALF_NEW)
        model_byte = prep.core_rgb_byte(model)
        self.assertEqual(model_byte, 57)
        new_run = self.run_ok(prep.control_reference_argv(tif, 1, self.footprint(self.HALF_NEW)))
        old_run = self.run_ok(self.legacy_reference_argv(tif, self.HALF_OLD))
        self.assertAlmostEqual(self.gdal_float(new_run), model, delta=1e-3)
        self.assertLessEqual(abs(prep.parse_aaigrid_value(new_run, "new") - model_byte),
                             prep.IMAGERY_TOLERANCE_BYTES)
        self.assertEqual(prep.parse_aaigrid_value(old_run, "old"), 10)
        self.assertGreater(abs(10 - model_byte), prep.IMAGERY_TOLERANCE_BYTES)


class PrepareGuardsTests(PreparedTestBase):
    def test_missing_freeze_refuses_before_any_subprocess_or_write(self):
        cache = self.make_cache()
        self.patch_spawn_guards()
        seam = FakeGdal([], [])
        self.patch("run_tool", seam.run)
        code, message = fail_code(lambda: prep.run_prepare(cache))
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertEqual(seam.calls, [])
        self.assertEqual(os.listdir(cache), [])

    def test_incomplete_freeze_refuses(self):
        cache = self.make_cache()
        facts = self.write_completed_chain(cache)
        os.unlink(os.path.join(facts["stage"], "COMPLETED"))
        self.patch_spawn_guards()
        code, message = fail_code(lambda: prep.run_prepare(cache))
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("completed final", message)

    def test_two_completed_freezes_refuse(self):
        cache = self.make_cache()
        self.write_completed_chain(cache)
        second = os.path.join(cache, "freeze", "sedan-freeze-second")
        os.makedirs(second)
        Path(os.path.join(second, "COMPLETED")).write_bytes(b"")
        self.patch_spawn_guards()
        code, message = fail_code(lambda: prep.run_prepare(cache))
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("multiple completed", message)

    def test_extract_total_drift_refuses_before_any_subprocess_or_write(self):
        cache = self.make_cache()
        facts = self.write_completed_chain(cache)
        self.patch("EXTRACT_TOTAL_BYTES_EXPECTED", facts["extract_total"] + 1)
        self.patch_spawn_guards()
        seam = self.fake_gdal(facts)
        code, message = fail_code(lambda: prep.run_prepare(cache))
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("total got", message)
        self.assertEqual(seam.calls, [])
        self.assertNotIn("preparation", os.listdir(cache))

    def test_tile_drift_refuses_inside_consumer_reverify(self):
        cache = self.make_cache()
        facts = self.write_completed_chain(cache)
        tile_path = os.path.join(cache, facts["pins"][0]["rel"])
        content = bytearray(Path(tile_path).read_bytes())
        content[0] ^= 0xFF  # same size, different bytes: a pure sha256 drift
        Path(tile_path).write_bytes(bytes(content))
        self.patch_spawn_guards()
        seam = self.fake_gdal(facts)
        code, message = fail_code(lambda: prep.run_prepare(cache))
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("sha256 drifted", message)
        self.assertEqual(seam.calls, [])
        self.assertEqual(sorted(os.listdir(cache)), ["extracted", "freeze", "lidarhd"])

    def test_operator_collision_refuses_and_preserves_the_file(self):
        cache = self.make_cache()
        self.write_completed_chain(cache)
        self.patch_spawn_guards()
        owned = prep.owned_output_paths(cache)
        Path(owned["manifest"]).write_bytes(b"operator bytes")
        code, message = fail_code(lambda: prep.run_prepare(cache))
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("operator collision", message)
        self.assertEqual(Path(owned["manifest"]).read_bytes(), b"operator bytes")
        self.assertNotIn("preparation", os.listdir(cache))

    def test_space_guard_refuses_before_any_write(self):
        cache = self.make_cache()
        self.write_completed_chain(cache)
        self.patch("check_mount_realness", lambda root: True)
        self.patch("statvfs", lambda path: type("U", (), {"f_bavail": 1,
                                                          "f_frsize": 4096})())
        code, message = fail_code(lambda: prep.run_prepare(cache))
        self.assertEqual(code, prep.EXIT_GUARD)
        self.assertIn("insufficient free space", message)
        self.assertEqual(sorted(os.listdir(cache)), ["extracted", "freeze", "lidarhd"])

    def test_unverified_mount_refuses_in_prepare_mode(self):
        cache = self.make_cache()
        self.write_completed_chain(cache)
        # The REAL check_mount_realness: a /tmp root is not under /mnt/pit2, so
        # --prepare must refuse (the repaired return-value gate).
        code, message = fail_code(lambda: prep.run_prepare(cache))
        self.assertEqual(code, prep.EXIT_GUARD)
        self.assertIn("mount realness", message)
        self.assertEqual(sorted(os.listdir(cache)), ["extracted", "freeze", "lidarhd"])

    def test_non_directory_preparation_root_refuses(self):
        cache = self.make_cache()
        self.write_completed_chain(cache)
        self.patch_spawn_guards()
        Path(os.path.join(cache, "preparation")).write_bytes(b"not a dir")
        code, message = fail_code(lambda: prep.run_prepare(cache))
        self.assertEqual(code, prep.EXIT_STATE)
        # The nearest-existing-ancestor guard refuses a non-directory root.
        self.assertIn("operator collision", message)

    def test_receipt_without_intact_ledger_is_never_adopted(self):
        cache = self.make_cache()
        os.makedirs(os.path.join(cache, "preparation"))
        Path(prep.receipt_path(cache)).write_bytes(
            (json.dumps({"receipt_version": prep.STATE_VERSION,
                         "tool": "sedan_prepare_inputs.py",
                         "commands_sha256": "deadbeef", "outputs": {}}, indent=2)
             + "\n").encode("utf-8"))
        self.write_completed_chain(cache)
        self.patch_spawn_guards()
        code, message = fail_code(lambda: prep.run_prepare(cache))
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("ledger missing", message)


class PrepareEndToEndTests(PreparedTestBase):
    def run_happy_prepare(self, allocation=None):
        cache = self.make_cache()
        facts = self.write_completed_chain(cache)
        facts["frozen_before"] = self.tree_hashes(os.path.join(cache, "freeze"))
        facts["extracted_before"] = self.tree_hashes(os.path.join(cache, "extracted"))
        seam = self.fake_gdal(facts, allocation=allocation)
        receipt = prep.run_prepare(cache)
        return cache, facts, seam, receipt

    def test_happy_prepare_produces_every_output_with_a_valid_chronology(self):
        cache, facts, seam, receipt = self.run_happy_prepare()
        owned = prep.owned_output_paths(cache)
        for label, path in owned.items():
            self.assertTrue(os.path.isfile(path), label)
        self.assertTrue(os.path.isfile(prep.receipt_path(cache)))
        # Receipt bindings: frozen ids, recorded-never-activated alternates,
        # the operator policy pins, the chain hashes, and the extract facts.
        self.assertEqual(receipt["primaries"], FROZEN_PRIMARY_IDS)
        self.assertEqual(receipt["alternates"], FROZEN_ALTERNATE_IDS)
        self.assertIn("NEVER activated", receipt["alternates_note"])
        policy = receipt["operator_policy"]
        self.assertEqual(policy["vertical_tolerance_meters"], 1.0)
        self.assertEqual(policy["budget_b_game_units"], 0.0390625)
        self.assertEqual(policy["vertical_datum_label"], "NGF-IGN69")
        self.assertNotIn("vertical_accuracy_meters", policy)
        self.assertIn("ABSENT", policy["vertical_accuracy_note"])
        # The datum note is REQUIRED to state the geokey absence explicitly; no
        # datum label or accuracy VALUE anywhere carries the 5720 token.
        self.assertIn("NO EPSG:5720 geokey", policy["vertical_datum_note"])
        self.assertNotIn("5720", policy["vertical_datum_label"])
        self.assertNotIn("5720", json.dumps(receipt["outputs"]))
        self.assertEqual(len(receipt["uncertainties"]), 5)
        reference = receipt["rgb_control_reference"]
        self.assertEqual(reference["source_sha256"], receipt["outputs"]["rgb_tif"]["sha256"])
        self.assertEqual(reference["pixels_per_axis"], 3840)
        self.assertEqual(reference["control_pixels"], [960, 2880])
        self.assertEqual(reference["tolerance_bytes"], 2)
        self.assertEqual(receipt["extract_receipt"]["members"], 27)
        self.assertEqual(receipt["extract_receipt"]["total_bytes"], facts["extract_total"])
        self.assertEqual(receipt["freeze"]["laz_tiles_sha256"], facts["tiles_sha256"])
        self.assertEqual(receipt["freeze"]["integrity_manifest_sha256"],
                         facts["integrity_sha256"])
        self.assertEqual(receipt["freeze"]["canonical_digest"], facts["canonical_digest"])
        self.assertEqual(receipt["commands_sha256"], sha256_file(prep.ledger_path(cache)))
        for label, record in receipt["outputs"].items():
            self.assertEqual(sha256_file(record["path"]), record["sha256"], label)
        # Chronology order on the executed ledger: the freeze event is first,
        # only metadata (heightSemantic False) events precede the first height
        # read, dem_rasterio_start carries the DEM warp argv, and the completion
        # event closes the ledger.
        events = prep.read_ledger(prep.ledger_path(cache))
        self.assertEqual(events[0]["event"], "freeze_validated_complete")
        self.assertEqual(events[-1]["event"], "preparation_completed")
        start_index = next(index for index, event in enumerate(events)
                           if event.get("event") == "dem_rasterio_start")
        for event in events[1:start_index]:
            self.assertEqual(event.get("event"), "tool_invoked")
            self.assertIs(event["heightSemantic"], False)
        dem_warp_argv = seam.warp_calls()[0]["argv"]
        self.assertEqual(events[start_index]["argv"], dem_warp_argv)
        height_events = [event for event in events
                         if event.get("event") == "tool_invoked" and event["heightSemantic"]]
        self.assertTrue(height_events)
        for event in events:
            if event.get("event") == "tool_invoked":
                joined = " ".join(event["argv"])
                self.assertNotIn("sedan_lidar_integrity", joined)
                self.assertNotIn("prepare-integrity", joined)
                if event["tool"] == "gdalinfo":
                    stats = "-stats" in event["argv"]
                    self.assertIs(event["heightSemantic"], stats)
        # Manifest at its owned path re-validates and carries the fake stats
        # minimum as a non-zero origin.
        manifest = json.loads(Path(owned["manifest"]).read_text(encoding="utf-8"))
        prep.validate_generated_manifest(manifest, FROZEN_PRIMARY_IDS,
                                         forbidden_names=FROZEN_ALTERNATE_IDS)
        self.assertEqual(manifest["verticalPolicy"]["originMeters"], STATS_MINIMUM)
        rgb_points = {point["name"]: point for point in manifest["controlPoints"]
                      if "imageryRgb" in point}
        self.assertEqual(sorted(rgb_points),
                         ["rgb-quarter-centre", "rgb-three-quarter-centre"])
        quarter = rgb_points["rgb-quarter-centre"]
        self.assertAlmostEqual(quarter["longitude"], 4.9425 + 292.7604e-5, places=6)
        self.assertAlmostEqual(quarter["latitude"], 49.6990 + 292.7604e-5, places=6)
        self.assertEqual(quarter["imageryRgb"], [90, 140, 60])
        three_quarter = rgb_points["rgb-three-quarter-centre"]
        self.assertAlmostEqual(three_quarter["longitude"], 4.9425 + 877.9764e-5, places=6)
        self.assertAlmostEqual(three_quarter["latitude"], 49.6990 + 877.9764e-5, places=6)
        self.assertEqual(three_quarter["imageryRgb"], [90, 140, 60])
        # Exactly two batched cs2cs calls: 4 KML corners, then 2 control centres.
        cs2cs_calls = seam.warp_calls("cs2cs")
        self.assertEqual(len(cs2cs_calls), 2)
        self.assertEqual(len(cs2cs_calls[0]["input"].strip().splitlines()), 4)
        self.assertEqual(len(cs2cs_calls[1]["input"].strip().splitlines()), 2)
        # The KML is the 1100 m playable square, in batch order, and its corners
        # convert back to the playable UTM corners through the fake inverse.
        ns = {"kml": "http://www.opengis.net/kml/2.2"}
        root = ET.parse(owned["extent_kml"]).getroot()
        ring = [tuple(float(value) for value in pair.split(",")[:2])
                for pair in root.find(".//kml:coordinates", ns).text.split()]
        self.assertEqual(len(ring), 5)
        self.assertEqual(ring[0], ring[-1])
        for (lon, lat), (easting, northing) in zip(ring, PLAYABLE_CORNERS_UTM):
            expected_lon, expected_lat = fake_cs2cs_line(easting, northing).split()
            self.assertEqual((lon, lat), (float(expected_lon), float(expected_lat)))
            self.assertAlmostEqual((lon - 4.9425) / 1e-5 + 639841.997, easting, places=3)
            self.assertAlmostEqual((lat - 49.6990) / 1e-5 + 5506768.179, northing, places=3)
        # The DEM warp argv carries the whole-chunk window and every elevation
        # member; the control reference argv reads the prepared RGB to /vsistdout/
        # and never the raw imagery members.
        dem_calls = [call for call in seam.warp_calls() if "-dstnodata" in call["argv"]]
        self.assertEqual(len(dem_calls), 1)
        te = dem_calls[0]["argv"]
        self.assertEqual(te[te.index("-te") + 1:te.index("-te") + 5], WINDOW_TE)
        self.assertEqual(seam.warp_geometry[te[-1]]["geoTransform"], PREPARED_DEM_GT)
        rgb_calls = [call for call in seam.warp_calls() if call["argv"][-1].endswith(prep.RGB_NAME)]
        self.assertEqual(len(rgb_calls), 1)
        rgb_te = rgb_calls[0]["argv"]
        self.assertEqual(rgb_te[rgb_te.index("-te") + 1:rgb_te.index("-te") + 5], RGB_WINDOW_TE)
        rgb_geometry = seam.warp_geometry[rgb_te[-1]]
        self.assertEqual(rgb_geometry["geoTransform"], PREPARED_RGB_GT)
        self.assertEqual(rgb_geometry["size"], [PREPARED_RGB_WIDTH, PREPARED_RGB_HEIGHT])
        for member in facts["elevation"]:
            self.assertIn(member, dem_calls[0]["argv"])
        reference_calls = [call for call in seam.warp_calls() if "/vsistdout/" in call["argv"]]
        self.assertEqual(len(reference_calls), 6)
        for call in reference_calls:
            self.assertTrue(call["argv"][-2].endswith(prep.RGB_NAME))
            self.assertFalse(any(part.endswith(".jp2") for part in call["argv"]))
            for member in facts["imagery"]:
                self.assertNotIn(member, call["argv"])
            self.assertNotIn(owned["rgb_tif"], call["argv"])
            self.assertNotIn(owned["dem_tif"], call["argv"])
        # The freeze and extract subtrees were never modified (read-only pins).
        self.assertEqual(self.tree_hashes(os.path.join(cache, "freeze")),
                         facts["frozen_before"])
        self.assertEqual(self.tree_hashes(os.path.join(cache, "extracted")),
                         facts["extracted_before"])
        # Exactly one stage dir, marker-complete, lock released, alternates
        # recorded in the state.
        stage_dirs = [name for name in os.listdir(os.path.join(cache, "preparation"))
                      if name.startswith(prep.STAGE_PREFIX)]
        self.assertEqual(len(stage_dirs), 1)
        stage = os.path.join(cache, "preparation", stage_dirs[0])
        self.assertTrue(os.path.exists(os.path.join(stage, "STARTED")))
        self.assertTrue(os.path.exists(os.path.join(stage, "COMPLETED")))
        self.assertFalse(os.path.exists(os.path.join(stage, "state", "lock")))
        state = json.loads(Path(os.path.join(stage, "state", "state.json"))
                           .read_text(encoding="utf-8"))
        self.assertEqual(state["alternates_recorded_never_activated"],
                         FROZEN_ALTERNATE_IDS)
        sources_text = Path(owned["sources_txt"]).read_text(encoding="utf-8")
        self.assertIn(f"manifest: {prep.MANIFEST_NAME} sha256="
                      f"{receipt['outputs']['manifest']['sha256']}", sources_text)
        self.assertIn("NGF-IGN69", sources_text)
        self.assertIn("nearest-valid within 3.048 m", sources_text)
        self.assertIn("preparation/receipt.json", sources_text)

    def test_idempotent_rerun_verifies_and_skips_without_subprocesses(self):
        cache, _facts, _seam, receipt = self.run_happy_prepare()

        def refuse(*_args, **_kwargs):
            raise AssertionError("idempotent rerun must not spawn anything")

        self.patch("run_tool", refuse)
        owned_before = {label: sha256_file(path)
                        for label, path in prep.owned_output_paths(cache).items()}
        rerun = prep.run_prepare(cache)
        self.assertEqual(rerun["commands_sha256"], receipt["commands_sha256"])
        owned_after = {label: sha256_file(path)
                       for label, path in prep.owned_output_paths(cache).items()}
        self.assertEqual(owned_before, owned_after)

    def run_short_raster(self, hook):
        cache = self.make_cache()
        facts = self.write_completed_chain(cache)
        seam = self.fake_gdal(facts)
        seam.warp_geometry_hook = hook
        code, message = fail_code(lambda: prep.run_prepare(cache))
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("global index -1 ", message)
        self.assertEqual(seam.warp_calls("gdallocationinfo"), [])
        for path in prep.owned_output_paths(cache).values():
            self.assertFalse(os.path.lexists(path))
        self.assertFalse(os.path.exists(prep.receipt_path(cache)))
        return seam, message

    def test_short_actual_dem_refuses_before_rgb_warp_and_manifest(self):
        def short_dem(argv, geometry):
            if argv[-1].endswith(prep.DEM_NAME):
                return {"size": [1177, 1177],
                        "geoTransform": [639838.949, 1.0, 0.0, 5507941.659, 0.0, -1.0]}
            return geometry

        seam, message = self.run_short_raster(short_dem)
        self.assertIn("prepared DEM actual extent", message)
        rgb_warps = [call for call in seam.warp_calls()
                     if call["argv"][-1].endswith(prep.RGB_NAME)]
        self.assertEqual(rgb_warps, [])

    def test_short_actual_rgb_refuses_before_manifest(self):
        def short_rgb(argv, geometry):
            if argv[-1].endswith(prep.RGB_NAME):
                return {"size": [5852, 5852],
                        "geoTransform": [639841.997, 0.2, 0.0, 5507938.611, 0.0, -0.2]}
            return geometry

        _seam, message = self.run_short_raster(short_rgb)
        self.assertIn("prepared RGB actual extent", message)

    def test_failed_warp_preserves_stage_and_ledger_and_retries_cleanly(self):
        cache = self.make_cache()
        facts = self.write_completed_chain(cache)
        seam = self.fake_gdal(facts)
        seam.dem_warp_result = (2, b"gdalwarp boom")
        code, message = fail_code(lambda: prep.run_prepare(cache))
        self.assertEqual(code, prep.EXIT_WORKER)
        self.assertIn("gdalwarp boom", message)
        stage_dirs = [name for name in os.listdir(os.path.join(cache, "preparation"))
                      if name.startswith(prep.STAGE_PREFIX)]
        self.assertEqual(len(stage_dirs), 1)
        stage = os.path.join(cache, "preparation", stage_dirs[0])
        self.assertTrue(os.path.exists(os.path.join(stage, "STARTED")))
        self.assertTrue(os.path.exists(os.path.join(stage, "state", "state.json")))
        self.assertFalse(os.path.exists(os.path.join(stage, "state", "lock")))
        events = prep.read_ledger(prep.ledger_path(cache))
        self.assertEqual(events[0]["event"], "freeze_validated_complete")
        self.assertEqual(events[-1]["event"], "tool_invoked")
        self.assertEqual(events[-1]["returncode"], 2)
        self.assertIsNone(prep.verify_completed_preparation(cache))
        for path in prep.owned_output_paths(cache).values():
            self.assertFalse(os.path.lexists(path))
        # A retry with the seam healed completes the whole preparation: no
        # self-collision, exactly one completed receipt at the end.
        seam.dem_warp_result = (0, b"")
        receipt = prep.run_prepare(cache)
        self.assertEqual(receipt["primaries"], FROZEN_PRIMARY_IDS)
        self.assertTrue(os.path.isfile(prep.receipt_path(cache)))

    def test_primary_on_nodata_refuses_manifest_generation(self):
        cache = self.make_cache()
        facts = self.write_completed_chain(cache)
        nodata_site = SELECTION_ROWS["primary"][0]
        seam = self.fake_gdal(facts, allocation={
            (round(nodata_site["utm_x"], 3), round(nodata_site["utm_y"], 3)):
                prep.DEM_NODATA})
        code, message = fail_code(lambda: prep.run_prepare(cache))
        self.assertEqual(code, prep.EXIT_GUARD)
        self.assertIn(nodata_site["id"], message)
        self.assertIn("NoData", message)
        for path in prep.owned_output_paths(cache).values():
            self.assertFalse(os.path.lexists(path))
        self.assertFalse(os.path.exists(prep.receipt_path(cache)))

    def test_timed_out_height_command_propagates_as_worker_failure(self):
        cache = self.make_cache()
        facts = self.write_completed_chain(cache)
        seam = self.fake_gdal(facts)
        seam.timed_out_argv_head = "gdallocationinfo"
        code, message = fail_code(lambda: prep.run_prepare(cache))
        self.assertEqual(code, prep.EXIT_WORKER)
        self.assertIn("timed out", message)

    def test_bad_aaigrid_payload_is_a_state_failure(self):
        cache = self.make_cache()
        facts = self.write_completed_chain(cache)
        seam = self.fake_gdal(facts)
        seam.aaigrid_text_override = ("ncols 2\nnrows 1\nxllcorner 0\nycorner 0\n"
                                      "cellsize 0.2\nNODATA_value -9999\n10 20\n")
        code, message = fail_code(lambda: prep.run_prepare(cache))
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("1 x 1 AAIGrid", message)

    def test_source_gsd_drift_is_a_guard_failure(self):
        cache = self.make_cache()
        facts = self.write_completed_chain(cache)
        seam = self.fake_gdal(facts)
        seam.rgb_source_gsd_override = 0.25
        code, message = fail_code(lambda: prep.run_prepare(cache))
        self.assertEqual(code, prep.EXIT_GUARD)
        self.assertIn("GSD", message)

    def test_prepared_dem_nodata_mismatch_is_a_state_failure(self):
        cache = self.make_cache()
        facts = self.write_completed_chain(cache)

        class BrokenNodata(FakeGdal):
            def _gdalinfo_json(self, argv):
                payload = super()._gdalinfo_json(argv)
                if argv[-1].endswith(prep.DEM_NAME) and "-stats" not in argv:
                    info = json.loads(payload)
                    info["bands"][0]["noDataValue"] = 25.0
                    return json.dumps(info)
                return payload

        seam = BrokenNodata(facts["elevation"], facts["imagery"],
                            self.default_aaigrid_values(), {})
        self.patch_spawn_guards()
        self.patch("run_tool", seam.run)
        code, message = fail_code(lambda: prep.run_prepare(cache))
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("noDataValue", message)

    def test_prepared_rgb_not_uint8_is_a_state_failure(self):
        cache = self.make_cache()
        facts = self.write_completed_chain(cache)

        class BrokenBand(FakeGdal):
            def _gdalinfo_json(self, argv):
                payload = super()._gdalinfo_json(argv)
                if argv[-1].endswith(prep.RGB_NAME):
                    info = json.loads(payload)
                    info["bands"][0]["type"] = "UInt16"
                    return json.dumps(info)
                return payload

        seam = BrokenBand(facts["elevation"], facts["imagery"],
                          self.default_aaigrid_values(), {})
        self.patch_spawn_guards()
        self.patch("run_tool", seam.run)
        code, message = fail_code(lambda: prep.run_prepare(cache))
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("uint8", message)

    def test_existing_output_without_a_receipt_is_never_adopted(self):
        cache = self.make_cache()
        facts = self.write_completed_chain(cache)
        self.fake_gdal(facts)
        prepared = os.path.join(cache, prep.PREPARED_DIR_REL)
        os.makedirs(prepared)
        Path(os.path.join(prepared, prep.KML_NAME)).write_bytes(b"operator kml")
        code, message = fail_code(lambda: prep.run_prepare(cache))
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("operator collision", message)
        self.assertEqual(Path(os.path.join(prepared, prep.KML_NAME)).read_bytes(),
                         b"operator kml")
        self.assertFalse(os.path.exists(prep.receipt_path(cache)))


class PreflightAndCliTests(PreparedTestBase):
    def test_preflight_writes_nothing_and_spawns_nothing(self):
        cache = self.make_cache()
        facts = self.write_completed_chain(cache)
        seam = self.fake_gdal(facts)
        before = self.tree_hashes(cache)
        stdout = io.StringIO()
        with contextlib.redirect_stdout(stdout):
            prep.run_preflight(cache)
        self.assertEqual(seam.calls, [])
        self.assertEqual(self.tree_hashes(cache), before)
        self.assertIn("preflight complete", stdout.getvalue())
        self.assertIn("3 x 3 chunks", stdout.getvalue())

    def test_preflight_refuses_an_origin_drifted_map_transform(self):
        cache = self.make_cache()
        facts = self.write_completed_chain(cache)
        manifest_path = os.path.join(facts["stage"], "frozen-manifest.json")
        manifest = json.loads(Path(manifest_path).read_text(encoding="utf-8"))
        manifest["map_transform"] = {"extent_origin_utm": [1.0, 2.0],
                                     "lattice_spacing_m": 3.048}
        Path(manifest_path).write_text(json.dumps(manifest, indent=2), encoding="utf-8")
        # Rebind the state chain to the drifted manifest so the DRIFT ITSELF is
        # not what fails; only the canonical-origin check may fire.
        state_path = os.path.join(facts["stage"], "state", "state.json")
        state = json.loads(Path(state_path).read_text(encoding="utf-8"))
        state["artifacts"]["frozen_manifest"]["sha256"] = sha256_file(manifest_path)
        state["canonical_digest"] = groundtruth.canonical_digest(manifest)
        Path(state_path).write_text(json.dumps(state, indent=2), encoding="utf-8")
        code, message = fail_code(lambda: prep.run_preflight(cache))
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("canonical SW origin", message)

    def test_resolve_cache_root_order(self):
        args = prep.build_parser().parse_args(["/tmp/one"])
        self.assertEqual(prep.resolve_cache_root(args), ("/tmp/one", "positional"))
        args = prep.build_parser().parse_args([])
        saved = os.environ.pop("PANOPTICON_GEO_PROOF_DIR", None)
        try:
            self.assertEqual(prep.resolve_cache_root(args),
                             (prep.DEFAULT_CACHE_ROOT, "default"))
            os.environ["PANOPTICON_GEO_PROOF_DIR"] = "/tmp/from-env"
            self.assertEqual(prep.resolve_cache_root(args),
                             ("/tmp/from-env", "env PANOPTICON_GEO_PROOF_DIR"))
        finally:
            if saved is None:
                os.environ.pop("PANOPTICON_GEO_PROOF_DIR", None)
            else:
                os.environ["PANOPTICON_GEO_PROOF_DIR"] = saved

    def test_cli_prepare_flag_parses(self):
        args = prep.build_parser().parse_args(["--prepare", "/tmp/x"])
        self.assertTrue(args.prepare)
        self.assertEqual(args.root, "/tmp/x")
        args = prep.build_parser().parse_args([])
        self.assertFalse(args.prepare)
        self.assertIsNone(args.root)

    def test_main_exit_codes(self):
        cache = self.make_cache()
        self.write_completed_chain(cache)
        self.assertEqual(prep.main([cache]), prep.EXIT_OK)
        cache = self.make_cache()
        stdout = io.StringIO()
        with contextlib.redirect_stdout(stdout):
            code = prep.main([cache])
        self.assertEqual(code, prep.EXIT_STATE)
        self.assertIn("FAIL(4)", stdout.getvalue())
        cache = self.make_cache()
        self.write_completed_chain(cache)
        stdout = io.StringIO()
        with contextlib.redirect_stdout(stdout):
            code = prep.main(["--prepare", cache])
        self.assertEqual(code, prep.EXIT_GUARD)
        self.assertIn("FAIL(2)", stdout.getvalue())


if __name__ == "__main__":
    unittest.main()
