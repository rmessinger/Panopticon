"""Offline provider preparation contracts. No GDAL, network, or game installation."""

import contextlib
import copy
import importlib.util
import io
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from unittest import mock


SCRIPT = Path(__file__).resolve().parents[1] / "prepare_area.py"
SPEC = importlib.util.spec_from_file_location("prepare_area_under_test", SCRIPT)
area = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(area)


def profile():
    return area.load_profile("copernicus-glo30")


def item(identifier="a", cloud=1):
    imagery = profile()["imagery"]
    return {"id": identifier, "collection": imagery["collection"], "bbox": [4, 49, 5, 50],
            "properties": {"datetime": "2023-09-07T00:00:00Z", "eo:cloud_cover": cloud,
                           "s2:mgrs_tile": "31UFR"},
            "assets": {band: {"href": imagery["bucketBaseUrl"] + band + ".tif",
                              "raster:bands": [{"scale": 0.0001, "offset": -0.1, "nodata": 0}]}
                       for band in area.BANDS}}


def raster(bounds, gsd=10, count=1, dtype="UInt16", epsg=32631, nodata=0):
    bands = [{"type": dtype, "metadata": {"": {"STATISTICS_VALID_PERCENT": "100"}}}
             for _ in range(count)]
    if nodata is not None:
        for band in bands:
            band["noDataValue"] = nodata
    return {"driverShortName": "GTiff", "size": [round((bounds[2] - bounds[0]) / gsd),
                                                 round((bounds[3] - bounds[1]) / gsd)],
            "geoTransform": [bounds[0], gsd, 0, bounds[3], 0, -gsd],
            "coordinateSystem": {"wkt": f'PROJCRS["test",ID["EPSG",{epsg}]]'}, "bands": bands}


class FakeTools:
    def __init__(self):
        self.calls = []
        self.rasters = {}

    def __call__(self, argv, **kwargs):
        argv = list(map(str, argv))
        self.calls.append(argv)
        output = ""
        if argv[0] == "cs2cs":
            points = [tuple(map(float, line.split())) for line in kwargs["input_bytes"].decode().splitlines()]
            if argv[3] == "+proj=longlat":
                points = [(640392, 5507318) for _ in points]
            else:
                points = [(4.947 + (x - 640392) / 72000, 49.702 + (y - 5507318) / 111320)
                          for x, y in points]
            output = "\n".join(f"{x} {y} 0" for x, y in points)
        elif argv[0] == "curl":
            if "-o" in argv:
                path = argv[argv.index("-o") + 1]
                Path(path).write_bytes(b"fake full tile")
                info = raster((4, 49, 5, 50), 1 / 3600, dtype="Float32", epsg=4326, nodata=-9999)
                self.rasters[path] = info
            else:
                output = 'HTTP/1.1 200 OK\r\nETag: "test"\r\nContent-Length: 14\r\nLast-Modified: test\r\n'
        elif argv[0] == "gdalinfo":
            info = self.rasters.get(argv[-1])
            if info is None and os.path.basename(argv[-1]) == "dem-tile.tif":
                info = raster((4, 49, 5, 50), 1 / 3600, dtype="Float32", epsg=4326, nodata=-9999)
            output = json.dumps(info)
        elif argv[0] in ("gdal_translate", "gdalwarp"):
            path = argv[-1]
            Path(path).write_bytes(("fake raster " + os.path.basename(path)).encode())
            if "-projwin" in argv:
                i = argv.index("-projwin")
                xmin, ymax, xmax, ymin = map(float, argv[i + 1:i + 5])
                if argv[argv.index("-projwin_srs") + 1] == "EPSG:4326":
                    info = raster((xmin, ymin, xmax, ymax), 1 / 3600, dtype="Float32", epsg=4326, nodata=-9999)
                else:
                    info = raster((xmin, ymin, xmax, ymax))
            elif "-te" in argv:
                i = argv.index("-te")
                bounds = tuple(map(float, argv[i + 1:i + 5]))
                dtype = argv[argv.index("-ot") + 1]
                info = raster(bounds, float(argv[argv.index("-tr") + 1]),
                              3 if dtype == "Byte" else 1, dtype,
                              nodata=None if dtype == "Byte" else -9999)
                if "PHOTOMETRIC=RGB" in argv:
                    for band, color in zip(info["bands"], ("Red", "Green", "Blue")):
                        band["colorInterpretation"] = color
            else:
                info = copy.deepcopy(self.rasters[argv[-2]])
                for band in info["bands"]:
                    band["type"] = "Byte"
                    band.pop("noDataValue", None)
            self.rasters[path] = info
        else:
            raise AssertionError(f"unexpected tool: {argv}")
        return area.ToolRun(0, output, "", False, 0.01)


class PreflightTests(unittest.TestCase):
    def invoke(self, root, provider="copernicus-glo30", extra=()):
        return area.main(["--provider", provider, "--center", "49.702,4.947", "--size", "1100",
                          "--name", "sedan-proof", *extra, str(root)])

    def test_all_shipped_profiles_validate(self):
        for path in Path(area.PROVIDERS_DIR).glob("*.json"):
            with self.subTest(path=path):
                self.assertEqual(path.stem, area.load_profile(path.stem)["id"])

    def test_preflight_has_no_process_network_or_writes(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "absent"
            with mock.patch.object(area, "run_tool", side_effect=AssertionError("spawn")), \
                    mock.patch.object(area, "http_post_json", side_effect=AssertionError("network")), \
                    contextlib.redirect_stdout(io.StringIO()) as out:
                self.assertEqual(0, self.invoke(root))
                self.assertEqual(0, self.invoke(root, "ign-fr"))
            self.assertFalse(root.exists())
            self.assertIn("read-only", out.getvalue())
            self.assertIn("1170.432", out.getvalue())

    def test_invalid_identifiers(self):
        for bad in ("../ign-fr", "a/b", "A", "", "a" * 33):
            with self.subTest(bad=bad), self.assertRaises(area.Failure):
                area.load_profile(bad)
        for bad in ("../name", "a..b", "bad name", "a&b", ""):
            with self.subTest(bad=bad), self.assertRaises(area.Failure):
                area.validate_name(bad, "test")

    def test_nonfinite_and_invalid_requests(self):
        for value in ("nan,4", "49,inf", "91,4", "49,181", "x", "x,y"):
            with self.subTest(value=value), self.assertRaises(area.Failure):
                area.parse_center(value, "test")
        for value in ("nan", "inf", "0", "-1", "text"):
            with self.subTest(value=value), self.assertRaises(area.Failure):
                area.parse_size(value, "test")

    def test_utm_edges_and_hemispheres(self):
        for lon, expected in ((-180, 1), (-174.0001, 1), (-174, 2), (0, 31), (180, 60)):
            self.assertEqual(expected, area.computed_zone(lon))
        self.assertEqual((31, 31, "north", 32631), area.resolve_utm(0, 4, None, "test"))
        self.assertEqual((31, 31, "south", 32731), area.resolve_utm(-1, 4, None, "test"))
        self.assertEqual(32, area.resolve_utm(49, 4, 32, "test")[0])
        for lat, zone in ((84.1, None), (-80.1, None), (49, 0), (49, 61)):
            with self.subTest(lat=lat, zone=zone), self.assertRaises(area.Failure):
                area.resolve_utm(lat, 4, zone, "test")

    def test_grid_margin_and_sw_anchor(self):
        grid = area.chunk_grid(1100)
        self.assertEqual(3, grid["n_chunks"])
        self.assertEqual(385, grid["grid_points"])
        self.assertAlmostEqual(1170.432, grid["grid_m"])
        geom = area.utm_geometry(1000, 2000, 1100, grid)
        self.assertEqual((450, 1450, 1550, 2550), geom["playable"])
        self.assertAlmostEqual(1620.432, geom["storage"][2])
        self.assertAlmostEqual(445.428, geom["minimal"][0])
        self.assertEqual((0, 0, 30, 30), area.outward_window((1, 1, 29, 29), 30))
        box = area.approx_wgs84_box(49, 4, grid["grid_m"], 1100)
        self.assertGreater((box[3] - 49) * area.METERS_PER_DEG_LAT, 620)

    def test_full_extent_guards(self):
        p = profile()
        for bounds in ((4, 34.99, 4.1, 35.1), (5.99, 49, 6.01, 49.1),
                       (179.9, 49, 180.1, 49.1), (4, -0.1, 4.1, 0.1)):
            with self.subTest(bounds=bounds), self.assertRaisesRegex(area.Failure, "test"):
                area.check_extent(bounds, 31, p, "test")

    def test_ign_rejects_other_request_and_override(self):
        p = area.load_profile("ign-fr")
        with self.assertRaises(area.Failure):
            area.check_proven_only(p, 49.703, 4.947, 1100, "sedan-proof", "ign-fr")
        with contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(area.EXIT_GUARD, self.invoke("/unused", "ign-fr", ("--utm-zone", "32")))

    def test_profile_policy_mutations_fail(self):
        paths = [("coverage", [4, float("nan"), 5, 50]), ("elevation.strategy", "window"),
                 ("elevation.noData.radiusMeters", 50), ("elevation.noData.edgePolicy", "clamp"),
                 ("elevation.download.tilePattern", "../../tile"), ("licence.bindSubsequent", False),
                 ("attribution.elevation.liability", ""), ("imagery.bandGsdMeters", 20),
                 ("imagery.cloudCoverProperty", None), ("imagery.bandIds", {}),
                 ("imagery.assetKeys", {"red": "red", "green": "red", "blue": "blue"}),
                 ("imagery.bucketBaseUrl", "https://sentinel-cogs.evil/"),
                 ("imagery.dateWindow", ["2024-01-01", "2023-01-01"]),
                 ("attribution.elevation", None), ("attribution.imagery", []),
                 ("manifestRequirements.tolerances", None),
                 ("manifestRequirements.tolerances.verticalMeters.schemaValue", -1)]
        for path, value in paths:
            p = profile()
            target = p
            keys = path.split(".")
            for key in keys[:-1]:
                target = target[key]
            target[keys[-1]] = value
            with self.subTest(path=path), self.assertRaises(area.Failure):
                area.validate_profile(p, p["id"])


class SelectionAndRasterTests(unittest.TestCase):
    def test_all_pages_and_stable_selection(self):
        imagery = profile()["imagery"]
        pages = [{"features": [item("first", 10)], "links": [
            {"rel": "next", "method": "POST", "href": imagery["catalogueUrl"], "body": {"token": "next"}}]},
                 {"features": [item("z", 0), item("a", 0)], "links": []}]
        with mock.patch.object(area, "http_post_json", side_effect=pages) as http:
            base, found = area.stac_search_all(imagery, [4.9, 49.7, 4.95, 49.71])
        self.assertEqual(2, http.call_count)
        self.assertIn("datetime", base)
        selected, count = area.select_item([i for p in found for i in p["features"]], imagery,
                                           [4.9, 49.7, 4.95, 49.71], "test")
        self.assertEqual("a", selected["id"])
        self.assertEqual(3, count)

    def test_invalid_item_metadata_is_not_suitable(self):
        imagery = profile()["imagery"]
        for change in ("scale", "offset", "url", "date", "collection", "coverage", "cloud"):
            candidate = item()
            if change in ("scale", "offset"):
                candidate["assets"]["red"]["raster:bands"][0][change] = None
            elif change == "url":
                candidate["assets"]["red"]["href"] = "https://evil.test/red"
            elif change == "date":
                candidate["properties"]["datetime"] = "2025-01-01T00:00:00Z"
            elif change == "collection":
                candidate["collection"] = "other"
            elif change == "coverage":
                candidate["bbox"] = [4, 49, 4.91, 49.71]
            else:
                candidate["properties"]["eo:cloud_cover"] = -1
            with self.subTest(change=change):
                self.assertIsNone(area.item_suitable(candidate, imagery, [4.9, 49.7, 4.95, 49.71]))

    def test_no_suitable_item_fails_without_partial_selection(self):
        with self.assertRaisesRegex(area.Failure, "no suitable complete-AOI"):
            area.select_item([item(cloud=99)], profile()["imagery"], [4.9, 49.7, 4.95, 49.71], "test")

    def test_pagination_repetition_and_foreign_endpoint_fail(self):
        imagery = profile()["imagery"]
        for href in (imagery["catalogueUrl"], "https://other.test/search"):
            page = {"features": [], "links": [{"rel": "next", "method": "POST", "href": href}]}
            with mock.patch.object(area, "http_post_json", return_value=page), self.assertRaises(area.Failure):
                area.stac_search_all(imagery, [4, 49, 5, 50])

    def test_scale_offset_once_clamps_and_removes_nodata_tag(self):
        asset = {"scale": 0.0001, "offset": -0.1}
        with mock.patch.object(area, "run_tool", return_value=area.ToolRun(0, "", "", False, 0)) as tool:
            area.scale_window("raw", "scaled", asset, [0, 0.2])
        argv = tool.call_args.args[0]
        start = argv.index("-scale")
        self.assertEqual(["1000", "3000", "0", "255"], argv[start + 1:start + 5])
        self.assertEqual("1", argv[argv.index("-exponent") + 1])
        self.assertEqual("none", argv[argv.index("-a_nodata") + 1])

    def test_rgb_vrt_declares_band_order_and_escapes_paths(self):
        with tempfile.TemporaryDirectory() as temp:
            path = str(Path(temp) / "rgb.vrt")
            sources = [str(Path(temp) / f"{band}&scaled.tif") for band in area.BANDS]
            area.write_rgb_vrt(path, sources, 10, 20, 10, 640000, 5500000, 32631)
            bands = ET.parse(path).getroot().findall("VRTRasterBand")
            self.assertEqual(["Red", "Green", "Blue"], [b.findtext("ColorInterp") for b in bands])
            self.assertEqual([Path(p).name for p in sources],
                             [b.findtext("SimpleSource/SourceFilename") for b in bands])

    def test_every_band_requires_finite_full_validity(self):
        for value in ("99", "nan", "inf", None):
            info = raster((0, 0, 20, 20), count=3)
            info["bands"][1]["metadata"][""]["STATISTICS_VALID_PERCENT"] = value
            with self.subTest(value=value), self.assertRaises(area.Failure):
                area.require_full_valid(info, "RGB")

    def test_window_rejects_wrong_crs_transform_and_band(self):
        bounds = (0, 0, 20, 20)
        for field, value in (("size", [1, 2]), ("geoTransform", [1, 10, 0, 20, 0, -10]),
                             ("coordinateSystem", {"wkt": 'ID["EPSG",32632]'}),
                             ("bands", [])):
            info = raster(bounds)
            info[field] = value
            with self.subTest(field=field), self.assertRaises(area.Failure):
                area.verify_window(info, bounds, 10, 32631, "UInt16", 1, "raw", 0)

    def test_dem_header_rejects_rotated_or_uncovered_raster(self):
        bbox = (4.1, 49.1, 4.9, 49.9)
        for change in ("rotation", "coverage", "negative", "crs", "size"):
            info = raster((4, 49, 5, 50), 1 / 3600, dtype="Float32", epsg=4326, nodata=-9999)
            if change == "rotation":
                info["geoTransform"][2] = 0.01
            elif change == "coverage":
                info["geoTransform"][0] = 4.5
            elif change == "negative":
                info["geoTransform"][1] *= -1
            elif change == "crs":
                info["coordinateSystem"]["wkt"] += 'ID["EPSG",32631]'
            else:
                info["size"] = [-1, 3600]
            with self.subTest(change=change), self.assertRaises(area.Failure):
                area.verify_dem_tile(info, bbox, "DEM")

    def test_tile_hemisphere_and_cross_boundary(self):
        download = profile()["elevation"]["download"]
        self.assertEqual("S02_W005", area.dem_tile(download, [-4.9, -1.9, -4.8, -1.8], "test")["tile"])
        with self.assertRaises(area.Failure):
            area.dem_tile(download, [4.9, 49.7, 5.1, 49.8], "test")


class PreparationTests(unittest.TestCase):
    def invoke(self, root, provider="copernicus-glo30", extra=()):
        return area.main(["--provider", provider, "--center", "49.702,4.947", "--size", "1100",
                          "--name", "sedan-proof", *extra, str(root)])

    def prepare(self, root):
        return area.prepare_copernicus(profile(), "copernicus-glo30", (49.702, 4.947), 1100,
                                       "sedan-proof", str(root), (31, 31, "north", 32631), area.chunk_grid(1100))

    def test_partial_dem_download_resumes_only_with_matching_validator(self):
        headers = {"etag": '"version1"', "contentLength": "10"}
        with tempfile.TemporaryDirectory() as temp:
            cache = str(Path(temp) / "cache")
            path = str(Path(temp) / "tile.tif")

            def interrupted(argv):
                Path(argv[argv.index("-o") + 1]).write_bytes(b"12345")
                return area.ToolRun(28, "", "timeout", False, 0)

            with mock.patch.object(area, "curl_headers", return_value=headers), \
                    mock.patch.object(area, "run_tool", side_effect=interrupted), self.assertRaises(area.Failure):
                area.download_dem("https://test/tile", path, cache, {"name": "test"})
            self.assertEqual(b"12345", (Path(cache) / "tile.part").read_bytes())
            with mock.patch.object(area, "curl_headers", return_value={**headers, "etag": '"version2"'}), \
                    mock.patch.object(area, "run_tool", side_effect=AssertionError("must not resume")), \
                    self.assertRaisesRegex(area.Failure, "object changed"):
                area.download_dem("https://test/tile", path, cache, {"name": "test"})

            def resume(argv):
                self.assertEqual(["-C", "-"], argv[argv.index("-C"):argv.index("-C") + 2])
                self.assertIn('If-Range: "version1"', argv)
                with open(argv[argv.index("-o") + 1], "ab") as handle:
                    handle.write(b"67890")
                return area.ToolRun(0, "", "", False, 0)

            with mock.patch.object(area, "curl_headers", return_value=headers), \
                    mock.patch.object(area, "run_tool", side_effect=resume):
                area.download_dem("https://test/tile", path, cache, {"name": "test"})
            self.assertEqual(b"1234567890", Path(path).read_bytes())
            with mock.patch.object(area, "curl_headers", side_effect=AssertionError("verified cache is local")):
                area.download_dem("https://test/tile", str(Path(temp) / "other.tif"), cache, {"name": "test"})
            with self.assertRaisesRegex(area.Failure, "different inputs"):
                area.download_dem("https://test/other", str(Path(temp) / "third.tif"), cache, {"name": "test"})

    def test_unowned_download_cache_is_never_adopted(self):
        with tempfile.TemporaryDirectory() as temp:
            cache = Path(temp) / "cache"
            cache.mkdir()
            (cache / "tile.part").write_bytes(b"operator")
            with self.assertRaisesRegex(area.Failure, "unowned"):
                area.download_dem("https://test/tile", str(Path(temp) / "tile"), str(cache), {})
            self.assertEqual(b"operator", (cache / "tile.part").read_bytes())

    def test_fake_end_to_end_retains_provenance_and_skips_verified(self):
        fake = FakeTools()
        with tempfile.TemporaryDirectory() as temp, mock.patch.object(area, "run_tool", fake), \
                mock.patch.object(area, "http_post_json", return_value={"features": [item()], "links": []}), \
                contextlib.redirect_stdout(io.StringIO()):
            root = Path(temp) / "cop"
            self.assertEqual(0, self.prepare(root))
            receipt = json.loads((root / "preparation/receipt.json").read_text())
            paths = {v["path"] for v in receipt["outputs"]}
            self.assertIn("acquisition/stac.json", paths)
            self.assertIn("acquisition/profile.json", paths)
            self.assertIn("acquisition/s2-red-raw.tif", paths)
            self.assertIn("acquisition/dem-tile.tif", paths)
            self.assertIn("profileSha256", receipt["inputs"])
            manifest = json.loads((root / "sedan-proof.json").read_text())
            self.assertEqual([], manifest["controlPoints"])
            self.assertEqual("EGM2008", manifest["sources"][1]["verticalDatum"])
            self.assertEqual(3.048, manifest["rasterPolicy"]["noDataSearchRadiusMeters"])
            self.assertIn("2023", manifest["sources"][2]["attribution"])
            self.assertNotIn("[Year]", (root / "sources.txt").read_text())
            calls = len(fake.calls)
            before = {str(p.relative_to(root)): p.read_bytes() for p in root.rglob("*") if p.is_file()}
            self.assertEqual(0, self.prepare(root))
            self.assertEqual(calls, len(fake.calls))
            after = {str(p.relative_to(root)): p.read_bytes() for p in root.rglob("*") if p.is_file()}
            self.assertEqual(before, after)
            rgb_warp = next(a for a in fake.calls if a[0] == "gdalwarp" and "Byte" in a)
            self.assertIn("PHOTOMETRIC=RGB", rgb_warp)
            self.assertNotIn("-srcnodata", rgb_warp)
            self.assertEqual("None", rgb_warp[rgb_warp.index("-dstnodata") + 1])
            for band in area.BANDS:
                self.assertTrue((root / f"acquisition/s2-{band}-raw.tif").is_file())
            (root / "acquisition/s2-red-raw.tif").write_bytes(b"changed")
            with self.assertRaisesRegex(area.Failure, "changed on disk"):
                self.prepare(root)

    def test_empty_receipt_and_unsafe_paths_fail(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "preparation").mkdir()
            receipt = root / "preparation/receipt.json"
            inputs = {"name": "x", "epsg": 32631}
            receipt.write_text(json.dumps({"schema": 1, "inputs": inputs, "outputs": []}))
            (root / "preparation/COMPLETED").write_text("done")
            with self.assertRaisesRegex(area.Failure, "no output inventory"):
                area.verify_existing_receipt(str(root), str(receipt), inputs)
            for rel in ("../outside", "/absolute", "a/../b", "a\\b", "a//b"):
                with self.subTest(rel=rel), self.assertRaises(area.Failure):
                    area.checked_path(str(root), rel)

    def test_receipt_mutations_are_refused_without_network(self):
        for mutation in ("inputs", "omitted", "duplicate", "traversal", "completion", "ledger", "symlink"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temp, \
                    mock.patch.object(area, "run_tool", FakeTools()), \
                    mock.patch.object(area, "http_post_json", return_value={"features": [item()], "links": []}), \
                    contextlib.redirect_stdout(io.StringIO()):
                root = Path(temp) / "cop"
                self.prepare(root)
                receipt_path = root / "preparation/receipt.json"
                receipt = json.loads(receipt_path.read_text())
                if mutation == "inputs":
                    receipt["inputs"]["profileSha256"] = "changed"
                elif mutation == "omitted":
                    receipt["outputs"] = [o for o in receipt["outputs"]
                                          if o["path"] != "acquisition/s2-red-raw.tif"]
                elif mutation == "duplicate":
                    receipt["outputs"].append(receipt["outputs"][0])
                elif mutation == "traversal":
                    receipt["outputs"][0]["path"] = "../outside"
                elif mutation == "completion":
                    (root / "preparation/COMPLETED").unlink()
                elif mutation == "ledger":
                    (root / "preparation/commands.jsonl").write_text("changed")
                else:
                    raw = root / "acquisition/s2-red-raw.tif"
                    raw.unlink()
                    raw.symlink_to(root / "acquisition/s2-green-raw.tif")
                receipt_path.write_text(json.dumps(receipt))
                with mock.patch.object(area, "run_tool", side_effect=AssertionError("unexpected tool")), \
                        mock.patch.object(area, "http_post_json", side_effect=AssertionError("unexpected HTTP")), \
                        self.assertRaises(area.Failure):
                    self.prepare(root)

    def test_partial_publication_is_rolled_back_and_stage_preserved(self):
        real_publish = area.publish_no_overwrite
        for failure_at in range(1, 9):
            with self.subTest(failure_at=failure_at), tempfile.TemporaryDirectory() as temp, \
                    mock.patch.object(area, "run_tool", FakeTools()), \
                    mock.patch.object(area, "http_post_json", return_value={"features": [item()], "links": []}), \
                    contextlib.redirect_stderr(io.StringIO()):
                root = Path(temp) / "cop"
                count = 0

                def failing_publish(src, dst):
                    nonlocal count
                    if Path(src).name == "tile.part":
                        return real_publish(src, dst)
                    count += 1
                    if count == failure_at:
                        raise OSError("injected publication failure")
                    real_publish(src, dst)

                with mock.patch.object(area, "publish_no_overwrite", side_effect=failing_publish), \
                        self.assertRaisesRegex(OSError, "injected"):
                    self.prepare(root)
                self.assertFalse((root / "acquisition").exists())
                self.assertFalse((root / "sedan-proof.json").exists())
                self.assertEqual([], list((root / "prepared").iterdir()))
                self.assertEqual(["dem-download"], [p.name for p in (root / "preparation").iterdir()])
                self.assertEqual(b"fake full tile", (root / "preparation/dem-download/tile.part").read_bytes())
                stages = list(root.glob("stage-*"))
                self.assertEqual(1, len(stages))
                self.assertTrue((stages[0] / "acquisition/dem-tile.tif").is_file())

    def test_unowned_outputs_are_never_overwritten(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "cop"
            root.mkdir()
            existing = root / "sources.txt"
            existing.write_text("operator-owned")
            with mock.patch.object(area, "run_tool", side_effect=AssertionError("unexpected tool")), \
                    self.assertRaisesRegex(area.Failure, "refusing to overwrite"):
                self.prepare(root)
            self.assertEqual("operator-owned", existing.read_text())

    def test_symlink_directories_and_outputs_are_refused(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "cop"
            root.mkdir()
            outside = Path(temp) / "outside"
            outside.mkdir()
            (root / "preparation").symlink_to(outside, target_is_directory=True)
            with self.assertRaisesRegex(area.Failure, "symlink"):
                self.prepare(root)
            self.assertEqual([], list(outside.iterdir()))

    def test_live_and_stale_locks_refused_without_deletion(self):
        with tempfile.TemporaryDirectory() as temp:
            path = str(Path(temp) / "lock")
            area.acquire_lock(path)
            with self.assertRaisesRegex(area.Failure, "live pid"):
                area.acquire_lock(path)
            Path(path).write_text("not-a-pid")
            with self.assertRaisesRegex(area.Failure, "stale lock"):
                area.acquire_lock(path)
            self.assertTrue(Path(path).exists())

    def test_failed_stage_preserved_and_lock_released(self):
        with tempfile.TemporaryDirectory() as temp, \
                mock.patch.object(area, "build_copernicus_stage", side_effect=area.Failure("injected")), \
                contextlib.redirect_stderr(io.StringIO()):
            root = Path(temp) / "cop"
            with self.assertRaisesRegex(area.Failure, "injected"):
                self.prepare(root)
            self.assertFalse((root / "preparation/lock").exists())
            self.assertFalse((root / "preparation/COMPLETED").exists())
            self.assertEqual(1, len(list(root.glob("stage-*/preparation/STARTED"))))

    def test_free_space_guard_precedes_directory_creation(self):
        with tempfile.TemporaryDirectory() as temp, \
                mock.patch.object(area, "check_space", side_effect=area.Failure("no space")):
            root = Path(temp) / "absent"
            with self.assertRaisesRegex(area.Failure, "no space"):
                self.prepare(root)
            self.assertFalse(root.exists())

    def test_ign_delegates_exact_existing_wrapper(self):
        p = area.load_profile("ign-fr")
        with mock.patch.object(area, "run_tool", return_value=area.ToolRun(0, "", "", False, 1)) as tool, \
                mock.patch.object(area.os.path, "isfile", return_value=True), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(0, area.run_ign_delegate(p, "ign-fr", "/root with space"))
        self.assertEqual([str(SCRIPT.parent / "prepare-sedan-proof.sh"), "/root with space", "--prepare"],
                         tool.call_args.args[0])

    def test_runner_timeout_and_output_limit(self):
        run = area.run_tool([sys.executable, "-c", "import time; time.sleep(5)"], timeout_seconds=0.05)
        self.assertTrue(run.timed_out)
        with self.assertRaisesRegex(area.Failure, "output exceeded"):
            area.run_tool([sys.executable, "-c", "print('x'*10000)"], cap_bytes=100)
        run = area.run_tool([sys.executable, "-c",
                             "import os,time; os.close(1); os.close(2); time.sleep(5)"], timeout_seconds=0.05)
        self.assertTrue(run.timed_out)
        with self.assertRaisesRegex(area.Failure, "stdin"):
            area.run_tool([sys.executable, "-c", "pass"], input_bytes=b"x" * 4097)

    def test_footprints_schema_and_capability_validation(self):
        p = area.load_profile("ign-fr")
        self.assertIn("footprints", p["capabilities"])
        self.assertIsInstance(p.get("footprints"), dict)

        def mutated(profile_dict, path, value):
            target = copy.deepcopy(profile_dict)
            parts = path.split(".")
            curr = target
            for part in parts[:-1]:
                curr = curr[part]
            if value is None:
                del curr[parts[-1]]
            else:
                curr[parts[-1]] = value
            return target

        # Missing footprints block when capability present
        p_no_block = mutated(p, "footprints", None)
        with self.assertRaisesRegex(area.Failure, "profile declares footprints capability"):
            area.validate_profile(p_no_block, "ign-fr")

        # Footprints block present when capability missing
        cop = area.load_profile("copernicus-glo30")
        cop_with_fp = copy.deepcopy(cop)
        cop_with_fp["footprints"] = copy.deepcopy(p["footprints"])
        with self.assertRaisesRegex(area.Failure, "footprints block present without footprints capability"):
            area.validate_profile(cop_with_fp, "copernicus-glo30")

        # Kind wfs inside elevation or imagery download is rejected
        elev_wfs = mutated(p, "elevation.download.kind", "wfs")
        with self.assertRaisesRegex(area.Failure, "elevation download kind must be"):
            area.validate_profile(elev_wfs, "ign-fr")
        img_wfs = mutated(cop, "imagery.kind", "wfs")
        with self.assertRaisesRegex(area.Failure, "imagery kind must be"):
            area.validate_profile(img_wfs, "copernicus-glo30")

        # Specific footprints block validations
        cases = [
            ("footprints.kind", "stac", "footprints kind must be wfs"),
            ("footprints.wfsUrl", "http://insecure.org", "footprints wfsUrl must be https://"),
            ("footprints.typeNames", "OTHER:layer", "footprints typeNames must be BDTOPO_V3:batiment"),
            ("footprints.outputFormat", "text/xml", "footprints outputFormat must be application/json"),
            ("footprints.stableIdField", "id", "footprints stable id field must be cleabs"),
            ("footprints.keptAttributes", ["cleabs"], "footprints keptAttributes"),
            ("footprints.geometry", "Polygon", "footprints geometry must be MultiPolygon"),
            ("footprints.sourceCrs", "EPSG:2154", "footprints source CRS must be EPSG:4326"),
            ("footprints.licence.name", "", "footprints licence name missing"),
            ("footprints.attribution", {}, "footprints attribution missing"),
            ("footprints.badNumber", float("nan"), "non-finite number"),
        ]
        for path, val, err_re in cases:
            with self.subTest(path=path, val=val), self.assertRaisesRegex(area.Failure, err_re):
                area.validate_profile(mutated(p, path, val), "ign-fr")

    def test_footprints_cli_argv_and_conflicts(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "work"
            # Conflict --footprint-root + --no-footprints
            with contextlib.redirect_stderr(io.StringIO()) as err:
                code = self.invoke(root, "ign-fr", ("--footprint-root", "/tmp/custom-fp", "--no-footprints"))
                self.assertEqual(area.EXIT_GUARD, code)
                self.assertIn("cannot specify --footprint-root with --no-footprints", err.getvalue())

            # Conflict --footprints + --no-footprints
            with contextlib.redirect_stderr(io.StringIO()) as err:
                code = self.invoke(root, "ign-fr", ("--footprints", "--no-footprints"))
                self.assertEqual(area.EXIT_GUARD, code)
                self.assertIn("cannot specify both --footprints and --no-footprints", err.getvalue())

            # Capability error on copernicus-glo30 with --footprints
            with contextlib.redirect_stderr(io.StringIO()) as err:
                code = self.invoke(root, "copernicus-glo30", ("--footprints",))
                self.assertEqual(area.EXIT_GUARD, code)
                self.assertIn("copernicus-glo30", err.getvalue())
                self.assertIn("footprints", err.getvalue())

            # Capability error on copernicus-glo30 with --footprint-root
            with contextlib.redirect_stderr(io.StringIO()) as err:
                code = self.invoke(root, "copernicus-glo30", ("--footprint-root", "/tmp/fp",))
                self.assertEqual(area.EXIT_GUARD, code)
                self.assertIn("copernicus-glo30", err.getvalue())
                self.assertIn("footprints", err.getvalue())

            # --min-footprint-m2 non-positive / non-finite rejections
            for bad in ("nan", "inf", "0", "--min-footprint-m2=-inf", "--min-footprint-m2=-10"):
                extra = (bad,) if bad.startswith("--") else ("--min-footprint-m2", bad)
                with self.subTest(bad=bad), contextlib.redirect_stderr(io.StringIO()) as err:
                    code = self.invoke(root, "ign-fr", extra)
                    self.assertEqual(area.EXIT_GUARD, code)
                    self.assertIn("--min-footprint-m2 must be a finite positive number", err.getvalue())

    def test_footprint_root_safety_rejections(self):
        with self.assertRaisesRegex(area.Failure, "null byte"):
            area.resolve_footprint_root("/tmp/a\0b", "/tmp/work")
        with self.assertRaisesRegex(area.Failure, "cannot equal workdir"):
            area.resolve_footprint_root("/tmp/work", "/tmp/work")
        with self.assertRaisesRegex(area.Failure, "cannot contain workdir"):
            area.resolve_footprint_root("/tmp/work", "/tmp/work/nested")
        with self.assertRaisesRegex(area.Failure, "cannot be inside workdir"):
            area.resolve_footprint_root("/tmp/work/nested", "/tmp/work")
        with tempfile.TemporaryDirectory() as temp:
            real_dir = Path(temp) / "real"
            real_dir.mkdir()
            sym_dir = Path(temp) / "sym"
            sym_dir.symlink_to(real_dir, target_is_directory=True)

            # A symlinked workdir resolves rather than failing
            resolved = area.resolve_footprint_root(str(Path(temp) / "fp"), str(sym_dir))
            self.assertEqual(os.path.abspath(str(Path(temp) / "fp")), resolved)
            default_resolved = area.resolve_footprint_root(None, str(sym_dir))
            self.assertEqual(os.path.abspath(str(Path(temp) / "sym-footprints")), default_resolved)

            # Symlinked footprint root itself is rejected
            with self.assertRaisesRegex(area.Failure, "symlink footprint root"):
                area.resolve_footprint_root(str(sym_dir), str(Path(temp) / "workdir"))

            # Containment hiding via symlink is still rejected (workdir reached via symlink or vice versa)
            with self.assertRaisesRegex(area.Failure, "cannot be inside workdir"):
                area.resolve_footprint_root(str(real_dir / "nested"), str(sym_dir))
            with self.assertRaisesRegex(area.Failure, "cannot be inside workdir"):
                area.resolve_footprint_root(str(sym_dir / "nested"), str(real_dir))
            with self.assertRaisesRegex(area.Failure, "cannot contain workdir"):
                area.resolve_footprint_root(str(real_dir), str(sym_dir / "nested"))
            with self.assertRaisesRegex(area.Failure, "cannot equal workdir"):
                area.resolve_footprint_root(str(real_dir / "leaf"), str(sym_dir / "leaf"))

    def test_footprints_preflight_purity(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "absent"
            fp_root = Path(temp) / "absent-footprints"
            run_tool_mock = mock.MagicMock(side_effect=AssertionError("spawn"))
            with mock.patch.object(area, "run_tool", run_tool_mock), \
                    mock.patch.object(area, "http_post_json", side_effect=AssertionError("network")), \
                    contextlib.redirect_stdout(io.StringIO()) as out:
                self.assertEqual(0, self.invoke(root, "ign-fr"))
            self.assertEqual(0, run_tool_mock.call_count)
            self.assertFalse(root.exists())
            self.assertFalse(fp_root.exists())
            val = out.getvalue()
            self.assertIn("stage: building-footprints (WFS live query)", val)
            self.assertIn(f"root: {fp_root} (not created)", val)
            self.assertIn("threshold: min area 20 m^2", val)
            self.assertIn("caps: per-response 16777216 B", val)
            self.assertIn("prepared/sedan-proof-buildings.kml", val)
            self.assertIn("manifest: sedan-proof-footprints.json", val)

            # Preflight with --no-footprints hides footprint stage
            with mock.patch.object(area, "run_tool", run_tool_mock), \
                    mock.patch.object(area, "http_post_json", side_effect=AssertionError("network")), \
                    contextlib.redirect_stdout(io.StringIO()) as out_no:
                self.assertEqual(0, self.invoke(root, "ign-fr", ("--no-footprints",)))
            self.assertNotIn("stage: building-footprints", out_no.getvalue())

    def test_footprints_prepare_calls_hook_or_skips(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "ign"
            root.mkdir()
            prepared = root / "prepared"
            prepared.mkdir()
            for name in ("sedan-extent.kml", "sedan-dem-32631.tif", "sedan-ortho-32631.tif"):
                (prepared / name).write_bytes(b"data")
            (root / "sedan-ign-proof.json").write_bytes(b"{}")
            tool_run = area.ToolRun(0, "", "", False, 0.01)
            hook_mock = mock.MagicMock(return_value=area.EXIT_OK)
            with mock.patch.object(area, "run_tool", return_value=tool_run), \
                    mock.patch.object(area, "load_footprint_stage", hook_mock), \
                    contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(0, self.invoke(root, "ign-fr", ("--prepare",)))
            hook_mock.assert_called_once()
            kwargs = hook_mock.call_args.kwargs
            self.assertEqual("ign-fr", kwargs["provider_id"])
            self.assertEqual(str(root.resolve()), str(kwargs["workdir"]))
            self.assertEqual(str(root.resolve()) + "-footprints", str(kwargs["footprint_root"]))
            self.assertEqual((49.702, 4.947), kwargs["centre"])
            self.assertEqual(1100.0, kwargs["size_m"])
            self.assertEqual("sedan-proof", kwargs["name"])
            self.assertEqual(20.0, kwargs["min_footprint_m2"])

            # With --no-footprints, hook is skipped
            hook_mock.reset_mock()
            with mock.patch.object(area, "run_tool", return_value=tool_run), \
                    mock.patch.object(area, "load_footprint_stage", hook_mock), \
                    contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(0, self.invoke(root, "ign-fr", ("--prepare", "--no-footprints")))
            hook_mock.assert_not_called()

    def test_footprints_missing_module_fails_loudly(self):
        with self.assertRaisesRegex(area.Failure, "footprints stage module missing"):
            area.load_footprint_stage()

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "ign"
            root.mkdir()
            prepared = root / "prepared"
            prepared.mkdir()
            for name in ("sedan-extent.kml", "sedan-dem-32631.tif", "sedan-ortho-32631.tif"):
                (prepared / name).write_bytes(b"data")
            (root / "sedan-ign-proof.json").write_bytes(b"{}")
            tool_run = area.ToolRun(0, "", "", False, 0.01)
            with mock.patch.object(area, "run_tool", return_value=tool_run), \
                    contextlib.redirect_stderr(io.StringIO()) as err, \
                    contextlib.redirect_stdout(io.StringIO()):
                code = self.invoke(root, "ign-fr", ("--prepare",))
            self.assertEqual(area.EXIT_GUARD, code)
            self.assertIn("footprints stage module missing", err.getvalue())


if __name__ == "__main__":
    unittest.main()
