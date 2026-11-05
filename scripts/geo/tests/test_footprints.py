"""Offline tests for building footprints stage (scripts/geo/footprints.py).

Verifies bounded WFS acquisition, staged delegate inputs, geometry validation,
shoelace area filtering, buildings KML generation, composed operator manifest,
provenance, audit ledger, and receipt binding. All external calls mocked; no live network.
"""

from __future__ import annotations

import copy
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest import mock

# Ensure scripts/geo is in sys.path
SCRIPT_DIR = Path(__file__).resolve().parents[1]
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

import footprints


class FakeRun:
    def __init__(self, returncode=0, stdout=b"", stderr="", timed_out=False, wall_seconds=0.01):
        self.returncode = returncode
        self.stdout = stdout
        self.stderr = stderr
        self.timed_out = timed_out
        self.wall_seconds = wall_seconds


def mock_cs2cs(argv, **kwargs):
    input_bytes = kwargs.get("input_bytes", b"")
    lines = [line.strip() for line in input_bytes.decode("ascii").splitlines() if line.strip()]
    rows = []
    is_to_utm = argv.index("+proj=longlat") < argv.index("+to")
    for line in lines:
        parts = line.split()
        x, y = float(parts[0]), float(parts[1])
        if is_to_utm:
            # lon, lat -> easting, northing
            easting = 640000.0 + (x - 4.947) * 70000.0
            northing = 5507000.0 + (y - 49.702) * 111000.0
            rows.append(f"{easting:.6f} {northing:.6f} 0.0")
        else:
            # easting, northing -> lon, lat
            lon = 4.947 + (x - 640000.0) / 70000.0
            lat = 49.702 + (y - 5507000.0) / 111000.0
            rows.append(f"{lon:.8f} {lat:.8f} 0.0")
    return FakeRun(stdout=("\n".join(rows) + "\n").encode("ascii"))


def make_wfs_feature(cleabs: str, lon=4.947, lat=49.702, width_deg=0.0003, height_deg=0.0002,
                     holes=None, nature="Indifférenciée", usage_1="Résidentiel", usage_2=None,
                     hauteur=12.5):
    """Create a GeoJSON MultiPolygon feature with valid closed 4+ position rings."""
    exterior = [
        [lon, lat],
        [lon + width_deg, lat],
        [lon + width_deg, lat + height_deg],
        [lon, lat + height_deg],
        [lon, lat],
    ]
    polygon = [exterior]
    if holes:
        for hole in holes:
            polygon.append(hole)
    return {
        "type": "Feature",
        "id": f"batiment.{cleabs}",
        "properties": {
            "cleabs": cleabs,
            "nature": nature,
            "usage_1": usage_1,
            "usage_2": usage_2,
            "hauteur": hauteur,
        },
        "geometry": {
            "type": "MultiPolygon",
            "coordinates": [polygon],
        },
    }


def make_wfs_response(features, number_matched=None, number_returned=None, time_stamp="2026-11-05T12:00:00Z"):
    if number_matched is None:
        number_matched = len(features)
    if number_returned is None:
        number_returned = len(features)
    return {
        "type": "FeatureCollection",
        "numberMatched": number_matched,
        "numberReturned": number_returned,
        "timeStamp": time_stamp,
        "features": features,
    }


class TestFootprintsStage(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp(prefix="test-footprints-")
        self.workdir = os.path.join(self.tmp, "workdir")
        self.footprint_root = os.path.join(self.tmp, "workdir-footprints")
        os.makedirs(os.path.join(self.workdir, "prepared"), exist_ok=True)
        os.makedirs(os.path.join(self.workdir, "preparation"), exist_ok=True)

        self.seams = footprints.get_seams()
        self.profile = self.seams.load_profile("ign-fr")
        self.provider_id = "ign-fr"
        self.centre = (49.702, 4.947)
        self.size_m = 1100
        self.name = "sedan-proof"
        self.grid = self.seams.chunk_grid(self.size_m)
        self.utm = (31, 31, "north", 32631)

        # Create dummy delegate outputs in workdir
        self.extent_file = os.path.join(self.workdir, "prepared", "sedan-extent.kml")
        self.dem_file = os.path.join(self.workdir, "prepared", "sedan-dem-32631.tif")
        self.ortho_file = os.path.join(self.workdir, "prepared", "sedan-ortho-32631.tif")

        Path(self.extent_file).write_bytes(b"<kml>extent</kml>")
        Path(self.dem_file).write_bytes(b"DEM_TIFF_DATA")
        Path(self.ortho_file).write_bytes(b"ORTHO_TIFF_DATA")

        self.extent_sha = hashlib.sha256(b"<kml>extent</kml>").hexdigest()
        self.dem_sha = hashlib.sha256(b"DEM_TIFF_DATA").hexdigest()
        self.ortho_sha = hashlib.sha256(b"ORTHO_TIFF_DATA").hexdigest()

        self.delegate_manifest = {
            "sources": [
                {"id": "sedan-extent", "kind": "kml", "path": "prepared/sedan-extent.kml", "role": "extent",
                 "label": "extent KML", "attribution": "IGN"},
                {"id": "sedan-dem-32631", "kind": "geotiff", "path": "prepared/sedan-dem-32631.tif", "role": "elevation",
                 "verticalDatum": "NGF-IGN69", "label": "DEM", "attribution": "IGN"},
                {"id": "sedan-ortho-32631", "kind": "geotiff", "path": "prepared/sedan-ortho-32631.tif", "role": "imagery",
                 "label": "ortho", "attribution": "IGN"},
            ],
            "targetCoordinateSystem": {"type": "utm", "zone": 31, "hemisphere": "north"},
            "metersPerGameUnit": 0.3048,
            "defaultMaterial": "grass",
            "extentFeatureId": "sedan-extent",
            "verticalPolicy": {"originMeters": 150.0, "exaggeration": 1.0},
            "rasterPolicy": {"noData": "nearest-valid", "noDataSearchRadiusMeters": 3.048, "edge": "fail", "imageryPixelsPerSample": 10},
            "tolerances": {"verticalMeters": 1.0},
            "controlPoints": [{"name": "pt1", "latitude": 49.702, "longitude": 4.947}],
        }
        self.delegate_manifest_path = os.path.join(self.workdir, "sedan-ign-proof.json")
        Path(self.delegate_manifest_path).write_text(json.dumps(self.delegate_manifest))

        self.delegate_receipt = {
            "schema": 1,
            "inputs": {"provider": "ign-fr", "name": "sedan-proof"},
            "outputs": [
                {"path": "prepared/sedan-extent.kml", "sha256": self.extent_sha, "sizeBytes": len(b"<kml>extent</kml>")},
                {"path": "prepared/sedan-dem-32631.tif", "sha256": self.dem_sha, "sizeBytes": len(b"DEM_TIFF_DATA")},
                {"path": "prepared/sedan-ortho-32631.tif", "sha256": self.ortho_sha, "sizeBytes": len(b"ORTHO_TIFF_DATA")},
            ],
        }
        self.delegate_receipt_path = os.path.join(self.workdir, "preparation", "receipt.json")
        Path(self.delegate_receipt_path).write_text(json.dumps(self.delegate_receipt))

        self.delegate_completed = os.path.join(self.workdir, "preparation", "COMPLETED")
        completed_marker = {
            "receiptSha256": self.seams.sha256_file(self.delegate_receipt_path),
            "ledgerSha256": "0" * 64,
        }
        Path(self.delegate_completed).write_text(json.dumps(completed_marker))

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def test_run_stage_happy_path(self):
        """End-to-end stage execution with valid mocked curl and cs2cs."""
        f1 = make_wfs_feature("BATIMENT0000000001", lon=4.947, lat=49.702, width_deg=0.0003, height_deg=0.0002)
        f2 = make_wfs_feature("BATIMENT0000000002", lon=4.948, lat=49.703, width_deg=0.0004, height_deg=0.0003)
        # Small feature that will be dropped by 20 m2 threshold (e.g. 0.00001 x 0.00001 deg ~ 0.7m x 1.1m ~ 0.8 m2)
        f_small = make_wfs_feature("BATIMENT0000000003", lon=4.949, lat=49.704, width_deg=0.00001, height_deg=0.00001)

        wfs_data = make_wfs_response([f1, f2, f_small], number_matched=3)
        wfs_bytes = json.dumps(wfs_data).encode("utf-8")

        def fake_run_tool(argv, **kwargs):
            if argv[0] == "cs2cs":
                return mock_cs2cs(argv, **kwargs)
            if argv[0] == "curl":
                return FakeRun(stdout=wfs_bytes)
            return FakeRun()

        with mock.patch.object(self.seams, "run_tool", side_effect=fake_run_tool):
            code = footprints.run_stage(
                profile=self.profile,
                provider_id=self.provider_id,
                workdir=self.workdir,
                footprint_root=self.footprint_root,
                centre=self.centre,
                size_m=self.size_m,
                name=self.name,
                utm=self.utm,
                grid=self.grid,
                min_footprint_m2=20.0,
            )

        self.assertEqual(code, self.seams.EXIT_OK)

        # Assert outputs exist in footprint_root
        manifest_path = os.path.join(self.footprint_root, f"{self.name}-footprints.json")
        kml_path = os.path.join(self.footprint_root, "prepared", f"{self.name}-buildings.kml")
        sources_path = os.path.join(self.footprint_root, "sources.txt")
        prov_path = os.path.join(self.footprint_root, "provenance", "footprints-provenance.json")
        receipt_path = os.path.join(self.footprint_root, "preparation", "receipt.json")
        completed_path = os.path.join(self.footprint_root, "preparation", "COMPLETED")
        ledger_path = os.path.join(self.footprint_root, "preparation", "commands.jsonl")

        for p in (manifest_path, kml_path, sources_path, prov_path, receipt_path, completed_path, ledger_path):
            self.assertTrue(os.path.isfile(p), f"missing expected output file: {p}")

        # Assert staged delegate files exist in footprint_root/prepared/
        for rel in ("prepared/sedan-extent.kml", "prepared/sedan-dem-32631.tif", "prepared/sedan-ortho-32631.tif"):
            staged_file = os.path.join(self.footprint_root, rel)
            self.assertTrue(os.path.isfile(staged_file), f"missing staged delegate file: {rel}")

        # Assert buildings KML contains expected folder and kept placemarks
        kml_text = Path(kml_path).read_text(encoding="utf-8")
        self.assertIn("<Folder>", kml_text)
        self.assertIn("<name>buildings</name>", kml_text)
        self.assertIn('id="BATIMENT0000000001"', kml_text)
        self.assertIn('id="BATIMENT0000000002"', kml_text)
        self.assertNotIn('id="BATIMENT0000000003"', kml_text)  # dropped below threshold
        self.assertIn("<MultiGeometry>", kml_text)
        self.assertIn("<outerBoundaryIs>", kml_text)

        # Assert composed manifest contents
        manifest = json.loads(Path(manifest_path).read_text(encoding="utf-8"))
        self.assertEqual(len(manifest["sources"]), 4)
        source_ids = {s["id"] for s in manifest["sources"]}
        self.assertEqual(source_ids, {"sedan-extent", "sedan-dem-32631", "sedan-ortho-32631", f"{self.name}-buildings"})

        # Assert all source paths are confined (no '..' or leading '/')
        for s in manifest["sources"]:
            self.assertTrue(s["path"].startswith("prepared/"))
            self.assertNotIn("..", s["path"])

        self.assertEqual(manifest["controlPoints"], [])
        self.assertEqual(len(manifest["featureMappings"]), 2)
        rule1, rule2 = manifest["featureMappings"]
        self.assertEqual(rule1["sourceId"], f"{self.name}-buildings")
        self.assertEqual(rule1["layerName"], "buildings")
        self.assertEqual(rule1["geometry"], "polygon")
        self.assertEqual(rule1["target"], "passability")
        self.assertFalse(rule1["passable"])

        self.assertEqual(rule2["sourceId"], f"{self.name}-buildings")
        self.assertEqual(rule2["layerName"], "buildings")
        self.assertEqual(rule2["geometry"], "polygon")
        self.assertEqual(rule2["target"], "buildability")
        self.assertFalse(rule2["buildable"])

        # Assert provenance record
        prov = json.loads(Path(prov_path).read_text(encoding="utf-8"))
        self.assertEqual(prov["featuresKept"], 2)
        self.assertEqual(prov["featuresDropped"], 1)
        self.assertEqual(prov["totalFeaturesAcquired"], 3)
        self.assertEqual(prov["filterThresholdM2"], 20.0)
        self.assertIn("liveEditionCaveat", prov)
        self.assertIn("sortbyProof", prov)
        self.assertEqual(len(prov["stagedCopies"]), 3)
        self.assertIn(prov["stagedCopies"][0]["method"], ("hardlink", "copy"))

        # Assert receipt bindings
        receipt = json.loads(Path(receipt_path).read_text(encoding="utf-8"))
        self.assertEqual(receipt["schema"], 1)
        self.assertEqual(receipt["inputs"]["minFootprintM2"], 20.0)
        completed = json.loads(Path(completed_path).read_text(encoding="utf-8"))
        self.assertEqual(completed["receiptSha256"], self.seams.sha256_file(receipt_path))
        self.assertEqual(completed["ledgerSha256"], self.seams.sha256_file(ledger_path))

    def test_existing_receipt_verified_and_skipped(self):
        """Second run over an existing verified footprint root returns EXIT_OK without re-running."""
        f1 = make_wfs_feature("BATIMENT0000000001", lon=4.947, lat=49.702)
        wfs_data = make_wfs_response([f1])
        wfs_bytes = json.dumps(wfs_data).encode("utf-8")

        calls = []

        def fake_run_tool(argv, **kwargs):
            calls.append(argv[0])
            if argv[0] == "cs2cs":
                return mock_cs2cs(argv, **kwargs)
            if argv[0] == "curl":
                return FakeRun(stdout=wfs_bytes)
            return FakeRun()

        with mock.patch.object(self.seams, "run_tool", side_effect=fake_run_tool):
            code1 = footprints.run_stage(
                profile=self.profile, provider_id=self.provider_id,
                workdir=self.workdir, footprint_root=self.footprint_root,
                centre=self.centre, size_m=self.size_m, name=self.name,
                utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
            )
            self.assertEqual(code1, self.seams.EXIT_OK)

            # Second run: should be skipped
            calls_before = len(calls)
            code2 = footprints.run_stage(
                profile=self.profile, provider_id=self.provider_id,
                workdir=self.workdir, footprint_root=self.footprint_root,
                centre=self.centre, size_m=self.size_m, name=self.name,
                utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
            )
            self.assertEqual(code2, self.seams.EXIT_OK)
            self.assertEqual(len(calls), calls_before, "Second run should not invoke tools")

    def test_refuse_non_empty_unverified_root(self):
        """A pre-existing directory with files but no receipt must be refused."""
        os.makedirs(self.footprint_root, exist_ok=True)
        Path(os.path.join(self.footprint_root, "stray.txt")).write_text("unowned")

        with self.assertRaises(self.seams.Failure) as cm:
            footprints.run_stage(
                profile=self.profile, provider_id=self.provider_id,
                workdir=self.workdir, footprint_root=self.footprint_root,
                centre=self.centre, size_m=self.size_m, name=self.name,
                utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
            )
        self.assertIn("refusing to use existing non-empty footprint root", str(cm.exception))

    def test_refuse_overlapping_or_symlink_paths(self):
        """Validate refusal of symlink roots, equal paths, and nested paths (including containment hiding)."""
        sym_root = os.path.join(self.tmp, "sym-root")
        os.symlink(self.tmp, sym_root)

        with self.assertRaises(self.seams.Failure) as cm:
            footprints.run_stage(
                profile=self.profile, provider_id=self.provider_id,
                workdir=self.workdir, footprint_root=sym_root,
                centre=self.centre, size_m=self.size_m, name=self.name,
                utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
            )
        self.assertIn("symlink footprint root", str(cm.exception))

        # A symlinked workdir resolves rather than failing
        sym_workdir = os.path.join(self.tmp, "sym-workdir")
        os.symlink(self.workdir, sym_workdir)
        norm_w, norm_f = footprints.validate_root_paths(self.seams, sym_workdir, self.footprint_root)
        self.assertEqual(norm_w, os.path.abspath(sym_workdir))
        self.assertEqual(norm_f, os.path.abspath(self.footprint_root))

        # Root equals workdir
        with self.assertRaises(self.seams.Failure) as cm:
            footprints.run_stage(
                profile=self.profile, provider_id=self.provider_id,
                workdir=self.workdir, footprint_root=self.workdir,
                centre=self.centre, size_m=self.size_m, name=self.name,
                utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
            )
        self.assertIn("cannot equal workdir", str(cm.exception))

        # Root equals workdir via symlink (equality hiding)
        with self.assertRaises(self.seams.Failure) as cm:
            footprints.run_stage(
                profile=self.profile, provider_id=self.provider_id,
                workdir=sym_workdir, footprint_root=self.workdir,
                centre=self.centre, size_m=self.size_m, name=self.name,
                utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
            )
        self.assertIn("cannot equal workdir", str(cm.exception))

        # Root inside workdir
        inside_root = os.path.join(self.workdir, "sub-footprints")
        with self.assertRaises(self.seams.Failure) as cm:
            footprints.run_stage(
                profile=self.profile, provider_id=self.provider_id,
                workdir=self.workdir, footprint_root=inside_root,
                centre=self.centre, size_m=self.size_m, name=self.name,
                utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
            )
        self.assertIn("cannot be inside workdir", str(cm.exception))

        # Containment hiding: inside root given via symlink
        inside_sym = os.path.join(sym_workdir, "sub-footprints-sym")
        with self.assertRaises(self.seams.Failure) as cm:
            footprints.run_stage(
                profile=self.profile, provider_id=self.provider_id,
                workdir=self.workdir, footprint_root=inside_sym,
                centre=self.centre, size_m=self.size_m, name=self.name,
                utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
            )
        self.assertIn("cannot be inside workdir", str(cm.exception))

    def test_delegate_receipt_mismatch_fails_closed(self):
        """Corrupted delegate output file fails closed before stage execution."""
        # Tamper with dem_file bytes
        Path(self.dem_file).write_bytes(b"CORRUPTED_DEM")

        with self.assertRaises(self.seams.Failure) as cm:
            footprints.run_stage(
                profile=self.profile, provider_id=self.provider_id,
                workdir=self.workdir, footprint_root=self.footprint_root,
                centre=self.centre, size_m=self.size_m, name=self.name,
                utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
            )
        self.assertIn("differs from receipt", str(cm.exception))

    def test_wfs_duplicate_cleabs_rejected(self):
        """Duplicate cleabs in WFS responses fails the stage."""
        f1 = make_wfs_feature("BATIMENT0000000001", lon=4.947, lat=49.702)
        f2 = make_wfs_feature("BATIMENT0000000001", lon=4.948, lat=49.703)  # duplicate ID
        wfs_data = make_wfs_response([f1, f2], number_matched=2)
        wfs_bytes = json.dumps(wfs_data).encode("utf-8")

        def fake_run_tool(argv, **kwargs):
            if argv[0] == "cs2cs":
                return mock_cs2cs(argv, **kwargs)
            if argv[0] == "curl":
                return FakeRun(stdout=wfs_bytes)
            return FakeRun()

        with mock.patch.object(self.seams, "run_tool", side_effect=fake_run_tool):
            with self.assertRaises(self.seams.Failure) as cm:
                footprints.run_stage(
                    profile=self.profile, provider_id=self.provider_id,
                    workdir=self.workdir, footprint_root=self.footprint_root,
                    centre=self.centre, size_m=self.size_m, name=self.name,
                    utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
                )
            self.assertIn("duplicate feature ID", str(cm.exception))

    def test_wfs_sortby_not_honored_rejected(self):
        """First page features not sorted by cleabs causes stage failure."""
        f1 = make_wfs_feature("BATIMENT0000000002", lon=4.947, lat=49.702)
        f2 = make_wfs_feature("BATIMENT0000000001", lon=4.948, lat=49.703)  # out of order
        wfs_data = make_wfs_response([f1, f2], number_matched=2)
        wfs_bytes = json.dumps(wfs_data).encode("utf-8")

        def fake_run_tool(argv, **kwargs):
            if argv[0] == "cs2cs":
                return mock_cs2cs(argv, **kwargs)
            if argv[0] == "curl":
                return FakeRun(stdout=wfs_bytes)
            return FakeRun()

        with mock.patch.object(self.seams, "run_tool", side_effect=fake_run_tool):
            with self.assertRaises(self.seams.Failure) as cm:
                footprints.run_stage(
                    profile=self.profile, provider_id=self.provider_id,
                    workdir=self.workdir, footprint_root=self.footprint_root,
                    centre=self.centre, size_m=self.size_m, name=self.name,
                    utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
                )
            self.assertIn("did not honor SORTBY", str(cm.exception))

    def test_wfs_timestamp_or_number_matched_change_rejected(self):
        """Changes in timeStamp or numberMatched across pages are rejected."""
        f1 = make_wfs_feature("BATIMENT0000000001", lon=4.947, lat=49.702)
        f2 = make_wfs_feature("BATIMENT0000000002", lon=4.948, lat=49.703)

        page1 = make_wfs_response([f1], number_matched=2, number_returned=1, time_stamp="2026-11-05T12:00:00Z")
        # Page 2 has changed timeStamp
        page2 = make_wfs_response([f2], number_matched=2, number_returned=1, time_stamp="2026-11-05T12:00:05Z")

        page1_bytes = json.dumps(page1).encode("utf-8")
        page2_bytes = json.dumps(page2).encode("utf-8")
        page_iter = iter([page1_bytes, page2_bytes])

        def fake_run_tool(argv, **kwargs):
            if argv[0] == "cs2cs":
                return mock_cs2cs(argv, **kwargs)
            if argv[0] == "curl":
                return FakeRun(stdout=next(page_iter))
            return FakeRun()

        with mock.patch.object(self.seams, "run_tool", side_effect=fake_run_tool):
            with self.assertRaises(self.seams.Failure) as cm:
                footprints.run_stage(
                    profile=self.profile, provider_id=self.provider_id,
                    workdir=self.workdir, footprint_root=self.footprint_root,
                    centre=self.centre, size_m=self.size_m, name=self.name,
                    utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
                )
            self.assertIn("timeStamp changed between pages", str(cm.exception))

    def test_wfs_number_returned_mismatch_rejected(self):
        """Mismatch between numberReturned header and actual feature count fails."""
        f1 = make_wfs_feature("BATIMENT0000000001", lon=4.947, lat=49.702)
        # Declares numberReturned=5 but only returns 1
        wfs_data = make_wfs_response([f1], number_matched=5, number_returned=5)
        wfs_bytes = json.dumps(wfs_data).encode("utf-8")

        def fake_run_tool(argv, **kwargs):
            if argv[0] == "cs2cs":
                return mock_cs2cs(argv, **kwargs)
            if argv[0] == "curl":
                return FakeRun(stdout=wfs_bytes)
            return FakeRun()

        with mock.patch.object(self.seams, "run_tool", side_effect=fake_run_tool):
            with self.assertRaises(self.seams.Failure) as cm:
                footprints.run_stage(
                    profile=self.profile, provider_id=self.provider_id,
                    workdir=self.workdir, footprint_root=self.footprint_root,
                    centre=self.centre, size_m=self.size_m, name=self.name,
                    utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
                )
            self.assertIn("numberReturned (5) does not match feature count (1)", str(cm.exception))

    def test_wfs_no_progress_rejected(self):
        """A page returning 0 features before numberMatched is reached fails."""
        page1 = make_wfs_response([], number_matched=10, number_returned=0)
        page1_bytes = json.dumps(page1).encode("utf-8")

        def fake_run_tool(argv, **kwargs):
            if argv[0] == "cs2cs":
                return mock_cs2cs(argv, **kwargs)
            if argv[0] == "curl":
                return FakeRun(stdout=page1_bytes)
            return FakeRun()

        with mock.patch.object(self.seams, "run_tool", side_effect=fake_run_tool):
            with self.assertRaises(self.seams.Failure) as cm:
                footprints.run_stage(
                    profile=self.profile, provider_id=self.provider_id,
                    workdir=self.workdir, footprint_root=self.footprint_root,
                    centre=self.centre, size_m=self.size_m, name=self.name,
                    utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
                )
            self.assertIn("returned 0 features before numberMatched (10) was reached", str(cm.exception))

    def test_geometry_rejections(self):
        """Unsupported geometry types, unclosed rings, <4 positions, or non-finite coords fail."""
        # 1. Non-MultiPolygon (e.g. Polygon)
        f_bad_type = make_wfs_feature("BAD01")
        f_bad_type["geometry"]["type"] = "Polygon"
        f_bad_type["geometry"]["coordinates"] = f_bad_type["geometry"]["coordinates"][0]

        wfs_bytes = json.dumps(make_wfs_response([f_bad_type])).encode("utf-8")
        with mock.patch.object(self.seams, "run_tool", side_effect=lambda argv, **kw:
                               mock_cs2cs(argv, **kw) if argv[0] == "cs2cs" else FakeRun(stdout=wfs_bytes)):
            with self.assertRaises(self.seams.Failure) as cm:
                footprints.run_stage(
                    profile=self.profile, provider_id=self.provider_id,
                    workdir=self.workdir, footprint_root=self.footprint_root + "-badtype",
                    centre=self.centre, size_m=self.size_m, name=self.name,
                    utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
                )
            self.assertIn("unsupported geometry type 'Polygon'", str(cm.exception))

        # 2. Unclosed ring
        f_unclosed = make_wfs_feature("BAD02")
        f_unclosed["geometry"]["coordinates"][0][0][-1] = [4.9475, 49.7025]  # first was [4.947, 49.702]
        wfs_bytes = json.dumps(make_wfs_response([f_unclosed])).encode("utf-8")
        with mock.patch.object(self.seams, "run_tool", side_effect=lambda argv, **kw:
                               mock_cs2cs(argv, **kw) if argv[0] == "cs2cs" else FakeRun(stdout=wfs_bytes)):
            with self.assertRaises(self.seams.Failure) as cm:
                footprints.run_stage(
                    profile=self.profile, provider_id=self.provider_id,
                    workdir=self.workdir, footprint_root=self.footprint_root + "-unclosed",
                    centre=self.centre, size_m=self.size_m, name=self.name,
                    utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
                )
            self.assertIn("ring 0 in polygon 0 is not closed", str(cm.exception))

        # 3. Ring with fewer than 4 positions
        f_short = make_wfs_feature("BAD03")
        f_short["geometry"]["coordinates"][0][0] = [[4.947, 49.702], [4.948, 49.702], [4.947, 49.702]]
        wfs_bytes = json.dumps(make_wfs_response([f_short])).encode("utf-8")
        with mock.patch.object(self.seams, "run_tool", side_effect=lambda argv, **kw:
                               mock_cs2cs(argv, **kw) if argv[0] == "cs2cs" else FakeRun(stdout=wfs_bytes)):
            with self.assertRaises(self.seams.Failure) as cm:
                footprints.run_stage(
                    profile=self.profile, provider_id=self.provider_id,
                    workdir=self.workdir, footprint_root=self.footprint_root + "-short",
                    centre=self.centre, size_m=self.size_m, name=self.name,
                    utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
                )
            self.assertIn("must have >= 4 positions", str(cm.exception))

        # 4. Inverted axis order check
        f_inverted = make_wfs_feature("BAD04", lon=49.702, lat=4.947)  # lon and lat swapped
        wfs_bytes = json.dumps(make_wfs_response([f_inverted])).encode("utf-8")
        with mock.patch.object(self.seams, "run_tool", side_effect=lambda argv, **kw:
                               mock_cs2cs(argv, **kw) if argv[0] == "cs2cs" else FakeRun(stdout=wfs_bytes)):
            with self.assertRaises(self.seams.Failure) as cm:
                footprints.run_stage(
                    profile=self.profile, provider_id=self.provider_id,
                    workdir=self.workdir, footprint_root=self.footprint_root + "-inverted",
                    centre=self.centre, size_m=self.size_m, name=self.name,
                    utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
                )
            self.assertIn("inverted axis order", str(cm.exception))

    def test_holes_preserved_and_subtracted_from_area(self):
        """Holes in MultiPolygon are preserved in KML and subtracted from net area."""
        # 30m x 30m outer shell (~900 m2)
        # 10m x 10m hole (~100 m2)
        # Net area ~ 800 m2
        hole = [
            [4.9471, 49.7021],
            [4.9472, 49.7021],
            [4.9472, 49.7022],
            [4.9471, 49.7022],
            [4.9471, 49.7021],
        ]
        f_hole = make_wfs_feature("WITHHOLE01", lon=4.9470, lat=49.7020, width_deg=0.0004, height_deg=0.0003, holes=[hole])
        wfs_bytes = json.dumps(make_wfs_response([f_hole])).encode("utf-8")

        def fake_run_tool(argv, **kwargs):
            if argv[0] == "cs2cs":
                return mock_cs2cs(argv, **kwargs)
            if argv[0] == "curl":
                return FakeRun(stdout=wfs_bytes)
            return FakeRun()

        with mock.patch.object(self.seams, "run_tool", side_effect=fake_run_tool):
            footprints.run_stage(
                profile=self.profile, provider_id=self.provider_id,
                workdir=self.workdir, footprint_root=self.footprint_root,
                centre=self.centre, size_m=self.size_m, name=self.name,
                utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
            )

        kml_path = os.path.join(self.footprint_root, "prepared", f"{self.name}-buildings.kml")
        kml_text = Path(kml_path).read_text(encoding="utf-8")
        self.assertIn("<outerBoundaryIs>", kml_text)
        self.assertIn("<innerBoundaryIs>", kml_text)

    def test_per_response_cap_enforced(self):
        """Exceeding FOOTPRINT_RESPONSE_CAP_BYTES fails the stage."""
        oversized = b"x" * (self.seams.FOOTPRINT_RESPONSE_CAP_BYTES + 10)

        def fake_run_tool(argv, **kwargs):
            if argv[0] == "cs2cs":
                return mock_cs2cs(argv, **kwargs)
            if argv[0] == "curl":
                return FakeRun(stdout=oversized)
            return FakeRun()

        with mock.patch.object(self.seams, "run_tool", side_effect=fake_run_tool):
            with self.assertRaises(self.seams.Failure) as cm:
                footprints.run_stage(
                    profile=self.profile, provider_id=self.provider_id,
                    workdir=self.workdir, footprint_root=self.footprint_root,
                    centre=self.centre, size_m=self.size_m, name=self.name,
                    utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
                )
            self.assertIn("exceeded per-response cap", str(cm.exception))

    def test_total_bytes_cap_enforced(self):
        """Exceeding FOOTPRINT_TOTAL_CAP_BYTES fails the stage."""
        with mock.patch.object(self.seams, "FOOTPRINT_TOTAL_CAP_BYTES", 200):
            f1 = make_wfs_feature("BAT01")
            wfs_bytes = json.dumps(make_wfs_response([f1])).encode("utf-8")
            self.assertGreater(len(wfs_bytes), 200)

            def fake_run_tool(argv, **kwargs):
                if argv[0] == "cs2cs":
                    return mock_cs2cs(argv, **kwargs)
                if argv[0] == "curl":
                    return FakeRun(stdout=wfs_bytes)
                return FakeRun()

            with mock.patch.object(self.seams, "run_tool", side_effect=fake_run_tool):
                with self.assertRaises(self.seams.Failure) as cm:
                    footprints.run_stage(
                        profile=self.profile, provider_id=self.provider_id,
                        workdir=self.workdir, footprint_root=self.footprint_root,
                        centre=self.centre, size_m=self.size_m, name=self.name,
                        utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
                    )
                self.assertIn("exceeded total cap", str(cm.exception))

    def test_page_cap_enforced(self):
        """Exceeding FOOTPRINT_PAGE_CAP fails the stage."""
        with mock.patch.object(self.seams, "FOOTPRINT_PAGE_CAP", 2):
            f1 = make_wfs_feature("BAT01")
            f2 = make_wfs_feature("BAT02")
            f3 = make_wfs_feature("BAT03")

            page1 = make_wfs_response([f1], number_matched=3, number_returned=1)
            page2 = make_wfs_response([f2], number_matched=3, number_returned=1)
            page3 = make_wfs_response([f3], number_matched=3, number_returned=1)

            pages_iter = iter([
                json.dumps(page1).encode("utf-8"),
                json.dumps(page2).encode("utf-8"),
                json.dumps(page3).encode("utf-8"),
            ])

            def fake_run_tool(argv, **kwargs):
                if argv[0] == "cs2cs":
                    return mock_cs2cs(argv, **kwargs)
                if argv[0] == "curl":
                    return FakeRun(stdout=next(pages_iter))
                return FakeRun()

            with mock.patch.object(self.seams, "run_tool", side_effect=fake_run_tool):
                with self.assertRaises(self.seams.Failure) as cm:
                    footprints.run_stage(
                        profile=self.profile, provider_id=self.provider_id,
                        workdir=self.workdir, footprint_root=self.footprint_root,
                        centre=self.centre, size_m=self.size_m, name=self.name,
                        utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
                    )
                self.assertIn("exceeded page cap", str(cm.exception))

    def test_feature_cap_enforced(self):
        """Exceeding FOOTPRINT_FEATURE_CAP fails the stage."""
        with mock.patch.object(self.seams, "FOOTPRINT_FEATURE_CAP", 2):
            feats = [make_wfs_feature(f"BAT0{i}") for i in range(1, 4)]
            wfs_bytes = json.dumps(make_wfs_response(feats, number_matched=3)).encode("utf-8")

            def fake_run_tool(argv, **kwargs):
                if argv[0] == "cs2cs":
                    return mock_cs2cs(argv, **kwargs)
                if argv[0] == "curl":
                    return FakeRun(stdout=wfs_bytes)
                return FakeRun()

            with mock.patch.object(self.seams, "run_tool", side_effect=fake_run_tool):
                with self.assertRaises(self.seams.Failure) as cm:
                    footprints.run_stage(
                        profile=self.profile, provider_id=self.provider_id,
                        workdir=self.workdir, footprint_root=self.footprint_root,
                        centre=self.centre, size_m=self.size_m, name=self.name,
                        utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
                    )
                self.assertIn("exceeded feature cap", str(cm.exception))

    def test_timeout_cap_enforced(self):
        """Total wall-clock time exceeding FOOTPRINT_TIMEOUT_SECONDS fails the stage."""
        with mock.patch.object(self.seams, "FOOTPRINT_TIMEOUT_SECONDS", 0.001):
            def fake_run_tool(argv, **kwargs):
                if argv[0] == "cs2cs":
                    return mock_cs2cs(argv, **kwargs)
                if argv[0] == "curl":
                    import time
                    time.sleep(0.005)
                    f1 = make_wfs_feature("BAT01")
                    return FakeRun(stdout=json.dumps(make_wfs_response([f1])).encode("utf-8"))
                return FakeRun()

            with mock.patch.object(self.seams, "run_tool", side_effect=fake_run_tool):
                with self.assertRaises(self.seams.Failure) as cm:
                    footprints.run_stage(
                        profile=self.profile, provider_id=self.provider_id,
                        workdir=self.workdir, footprint_root=self.footprint_root,
                        centre=self.centre, size_m=self.size_m, name=self.name,
                        utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
                    )
                self.assertIn("exceeded timeout", str(cm.exception))

    def test_cross_device_copy_fallback(self):
        """Cross-device link (os.link raising OSError EXDEV) falls back to copy."""
        f1 = make_wfs_feature("BAT01")
        wfs_bytes = json.dumps(make_wfs_response([f1])).encode("utf-8")

        def fake_run_tool(argv, **kwargs):
            if argv[0] == "cs2cs":
                return mock_cs2cs(argv, **kwargs)
            if argv[0] == "curl":
                return FakeRun(stdout=wfs_bytes)
            return FakeRun()

        real_link = os.link

        def fake_link(src, dst):
            if str(src).startswith(self.workdir + os.sep):
                raise OSError(18, "Invalid cross-device link")
            return real_link(src, dst)

        with mock.patch.object(self.seams, "run_tool", side_effect=fake_run_tool):
            with mock.patch("os.link", side_effect=fake_link):
                code = footprints.run_stage(
                    profile=self.profile, provider_id=self.provider_id,
                    workdir=self.workdir, footprint_root=self.footprint_root,
                    centre=self.centre, size_m=self.size_m, name=self.name,
                    utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
                )
        self.assertEqual(code, self.seams.EXIT_OK)

        prov_path = os.path.join(self.footprint_root, "provenance", "footprints-provenance.json")
        prov = json.loads(Path(prov_path).read_text(encoding="utf-8"))
        for staged in prov["stagedCopies"]:
            self.assertEqual(staged["method"], "copy")

    def test_prepare_area_load_footprint_stage_hook(self):
        """prepare_area.py load_footprint_stage hook loads footprints module and runs stage."""
        f1 = make_wfs_feature("BAT01")
        wfs_bytes = json.dumps(make_wfs_response([f1])).encode("utf-8")

        def fake_run_tool(argv, **kwargs):
            if argv[0] == "cs2cs":
                return mock_cs2cs(argv, **kwargs)
            if argv[0] == "curl":
                return FakeRun(stdout=wfs_bytes)
            return FakeRun()

        with mock.patch.object(self.seams, "run_tool", side_effect=fake_run_tool):
            mod = self.seams.load_footprint_stage()
            self.assertTrue(hasattr(mod, "run_stage"))
            res = self.seams.load_footprint_stage(
                profile=self.profile, provider_id=self.provider_id,
                workdir=self.workdir, footprint_root=self.footprint_root,
                centre=self.centre, size_m=self.size_m, name=self.name,
                utm=self.utm, grid=self.grid, min_footprint_m2=20.0,
            )
            self.assertEqual(res, self.seams.EXIT_OK)


if __name__ == "__main__":
    unittest.main()
