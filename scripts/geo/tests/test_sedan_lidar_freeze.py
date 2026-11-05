"""Offline unit tests for scripts/geo/sedan_lidar_freeze.py (stdlib only by default).

Conventions follow scripts/geo/tests/test_sedan_extract.py and
test_sedan_lidar_probe.py: importlib module load, tempfile filesystems under
/tmp/opencode, patched seams, real subprocesses for the bounded runner, and
generated stand-in worker scripts for the orchestrator pipeline. No real LAZ tile,
no DEM, no network, and no installation is ever touched.

The final-freeze pipeline tests run the REAL orchestrator end to end (real
subprocess spawn through the bounded runner, real JSON I/O, real merge, and the
UNCHANGED sedan_lidar_select.select/freeze API) with a generated stand-in worker;
the RealLidarVenvEndToEndTests run the REAL sedan_lidar_probe_worker.py under the
isolated lidar venv when it is already present at the probe's TOOLS_DIR_REL layout
and skip truthfully when it is not. That real-venv coverage is ONE test method
performing TWO full synthetic end-to-end freeze runs inside itself (byte-identity
double run), not two tests. Nothing is ever installed by these tests.

Determinism claim under test: two independent fresh runs reproduce byte-identical
candidates.tsv and selection.tsv and an identical canonical digest. The frozen
manifest is NOT byte-identical across runs (it embeds generated_at, which the
canonical digest strips by design); this is asserted, not assumed away.
"""

import contextlib
import hashlib
import importlib.util
import io
import json
import os
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

_GEO_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(_GEO_DIR))

import sedan_lidar_select  # noqa: E402  (stdlib-only canonical selection module)

_spec = importlib.util.spec_from_file_location("sedan_lidar_freeze", _GEO_DIR / "sedan_lidar_freeze.py")
freeze = importlib.util.module_from_spec(_spec)
sys.modules["sedan_lidar_freeze"] = freeze
_spec.loader.exec_module(freeze)

LOT = freeze.LOT
FIRST_TILE_NAME = freeze.FIRST_TILE_NAME
TILE_ORDER = (
    FIRST_TILE_NAME,
    "LHD_FXX_0839_6958_PTS_LAMB93_IGN69.copc.laz",
    "LHD_FXX_0839_6959_PTS_LAMB93_IGN69.copc.laz",
    "LHD_FXX_0840_6959_PTS_LAMB93_IGN69.copc.laz",
    "LHD_FXX_0841_6958_PTS_LAMB93_IGN69.copc.laz",
    "LHD_FXX_0841_6959_PTS_LAMB93_IGN69.copc.laz",
)

# Executed cs2cs record values (sedan_lidar_probe.py run of 2026-11-03); used as
# the synthetic probe-report fixture everywhere and by the real-venv end-to-end.
ANCHOR_L93 = [839974.454115, 6957066.765093]
AOI_BBOX_L93 = {
    "x_min": 839974.454115, "y_min": 6957065.375459,
    "x_max": 841076.702265, "y_max": 6958167.623603,
}
FILTER_BBOX_L93 = {
    "x_min": 839924.352442, "y_min": 6957015.273483,
    "x_max": 841126.804970, "y_max": 6958217.726004,
}
FIRST_TILE_OBSERVED_FOOTPRINT = {
    "x_min": 840000.0, "y_min": 6957000.0, "x_max": 840999.99, "y_max": 6957999.99,
}


def aoi_corners(bbox):
    return [
        [bbox["x_min"], bbox["y_min"]], [bbox["x_max"], bbox["y_min"]],
        [bbox["x_max"], bbox["y_max"]], [bbox["x_min"], bbox["y_max"]],
    ]


def make_probe_report(first_tile_footprint=FIRST_TILE_OBSERVED_FOOTPRINT):
    return {
        "report_version": 2,
        "protocol_version": "1.2",
        "tile": FIRST_TILE_NAME,
        "projection_v1_2": {
            "lattice_anchor_32631": [639841.997, 5506768.179],
            "lattice_anchor_l93": list(ANCHOR_L93),
        },
        "lattice_v1_2": {"origin_l93": list(ANCHOR_L93)},
        "cs2cs": {
            "output_corners_l93": aoi_corners(AOI_BBOX_L93) + aoi_corners(FILTER_BBOX_L93),
        },
        "gates": {"d_v1_2": {"header_footprint_l93": dict(first_tile_footprint)}},
    }


def deterministic_bytes(name, size):
    digest = hashlib.sha256(name.encode("utf-8")).digest()
    blocks = (size // len(digest)) + 1
    return (digest * blocks)[:size]


# Stand-in worker for pipeline tests: real subprocess, real JSON file I/O, fixed
# synthetic per-tile cell specs (deterministic across cache roots).
STANDIN_CELLS = {
    FIRST_TILE_NAME: [
        {"row": 150, "col": 150, "sigma": 0.001},
    ],
    "LHD_FXX_0839_6958_PTS_LAMB93_IGN69.copc.laz": [
        {"row": 60, "col": 60, "sigma": 0.002},
        {"row": 60, "col": 240, "sigma": 0.003},
    ],
    "LHD_FXX_0839_6959_PTS_LAMB93_IGN69.copc.laz": [
        {"row": 240, "col": 60, "sigma": 0.004},
        {"row": 240, "col": 240, "sigma": 0.005},
    ],
    "LHD_FXX_0840_6959_PTS_LAMB93_IGN69.copc.laz": [
        {"row": 100, "col": 150, "sigma": 0.006},
    ],
    "LHD_FXX_0841_6958_PTS_LAMB93_IGN69.copc.laz": [
        {"row": 200, "col": 200, "sigma": 0.007},
    ],
    "LHD_FXX_0841_6959_PTS_LAMB93_IGN69.copc.laz": [
        {"row": 300, "col": 8, "sigma": 0.001, "hole": "seam"},
        {"row": 301, "col": 8, "n2": 3, "hole": "e1"},
        {"row": 330, "col": 330, "sigma": 0.009},
    ],
}

STANDIN_WORKER_TEMPLATE = '''
import hashlib, json, os, sys

CELLS = json.loads(r"""{cells}""")
PROVENANCE_FIELDS = json.loads(r"""{prov}""")

request_path = sys.argv[1]
with open(request_path, "r", encoding="utf-8") as handle:
    request = json.load(handle)
cand = request["candidates"]
anchor_x, anchor_y = cand["lattice_origin_l93"]
spacing = cand["spacing_m"]
footprint = cand["footprint_l93"]
name = os.path.basename(request["tile_path"])
columns = ("col", "row", "n2", "a", "b", "c", "sigma_fit", "slope_deg", "curv_bound",
           "curv_unavailable", "fit_unavailable",
           "mix2", "mix3", "mix4", "mix5", "mix6", "mix9", "mix17", "e2_dist", "e3_dist")
arrays = {{key: [] for key in columns}}
emitted = 0
for cell in CELLS.get(name, []):
    abs_x = anchor_x + (cell["col"] + 0.5) * spacing
    abs_y = anchor_y + (cell["row"] + 0.5) * spacing
    if not (abs_x - cand["window_r"] >= footprint["x_min"]
            and abs_x + cand["window_r"] <= footprint["x_max"]
            and abs_y - cand["window_r"] >= footprint["y_min"]
            and abs_y + cand["window_r"] <= footprint["y_max"]):
        continue
    n2 = cell.get("n2", 40)
    sigma = cell.get("sigma", 0.004)
    arrays["col"].append(cell["col"])
    arrays["row"].append(cell["row"])
    arrays["n2"].append(n2)
    has_fit = n2 >= 3
    arrays["a"].append(150.0 if has_fit else None)
    arrays["b"].append(0.0 if has_fit else None)
    arrays["c"].append(0.0 if has_fit else None)
    arrays["sigma_fit"].append(sigma if has_fit else None)
    arrays["slope_deg"].append(0.5 if has_fit else None)
    arrays["curv_bound"].append(0.001 if n2 >= 6 else None)
    arrays["curv_unavailable"].append(False)
    arrays["fit_unavailable"].append(False)
    arrays["mix2"].append(n2)
    arrays["mix3"].append(0)
    arrays["mix4"].append(0)
    arrays["mix5"].append(0)
    arrays["mix6"].append(0)
    arrays["mix9"].append(0)
    arrays["mix17"].append(0)
    arrays["e2_dist"].append(None)
    arrays["e3_dist"].append(None)
    emitted += 1
block = {{
    "candidate_measurement_version": 1,
    "lattice_origin_l93": [anchor_x, anchor_y],
    "aoi_sw_l93": [cand["aoi_sw_l93"][0], cand["aoi_sw_l93"][1]],
    "spacing_m": spacing,
    "lattice_cols": cand["lattice_cols"],
    "lattice_rows": cand["lattice_rows"],
    "window_r": cand["window_r"],
    "e23_radius": cand["e23_radius"],
    "footprint_observed_l93": dict(footprint),
    "footprint_request_l93": dict(cand["footprint_l93"]),
    "retention_margin_m": max(cand["window_r"], cand["e23_radius"]),
    "bin_size_m": 2.5,
    "row_order": "row-major (row ascending, then col ascending)",
    "containment": "window circle fully inside the observed header footprint",
    "points_retained": 1,
    "cells_enumerated": cand["lattice_cols"] * cand["lattice_rows"],
    "cells_contained": emitted,
    "cells_skipped_containment": cand["lattice_cols"] * cand["lattice_rows"] - emitted,
    "columns": list(columns),
    "arrays": arrays,
}}
print("stand-in worker measured", name, file=sys.stdout, flush=True)
print("stand-in worker note for", name, file=sys.stderr, flush=True)
with open(request["output_path"], "w", encoding="utf-8") as handle:
    json.dump({{
        "measurement_version": 1,
        "tile_path": request["tile_path"],
        "tile_bytes": os.path.getsize(request["tile_path"]),
        "header": {{
            "las_version": "1.4",
            "header_point_count": 4000,
            "footprint_l93": dict(footprint),
        }},
        "candidates": block,
    }}, handle, indent=2, sort_keys=True)
    handle.write("\\n")
if PROVENANCE_FIELDS:
    with open(os.path.abspath(__file__), "rb") as handle:
        self_sha = hashlib.sha256(handle.read()).hexdigest()
    for prov_key, prov_value in list(PROVENANCE_FIELDS.items()):
        if prov_value == "SELF":
            PROVENANCE_FIELDS[prov_key] = self_sha
    with open(request["output_path"], "r", encoding="utf-8") as handle:
        merged = json.load(handle)
    merged.update(PROVENANCE_FIELDS)
    with open(request["output_path"], "w", encoding="utf-8") as handle:
        json.dump(merged, handle, indent=2, sort_keys=True)
        handle.write("\\n")
'''


def write_standin_worker(root, provenance=None):
    script = root / "standin-worker.py"
    script.write_text(STANDIN_WORKER_TEMPLATE.format(cells=json.dumps(STANDIN_CELLS),
                                                     prov=json.dumps(provenance or {})),
                      encoding="utf-8")
    return script


def footprint_around(cells, pad=5.0):
    xs = []
    ys = []
    for row, col, *_rest in cells:
        xs.append(ANCHOR_L93[0] + (col + 0.5) * 3.048)
        ys.append(ANCHOR_L93[1] + (row + 0.5) * 3.048)
    return {"x_min": min(xs) - pad, "y_min": min(ys) - pad,
            "x_max": max(xs) + pad, "y_max": max(ys) + pad}


STANDIN_TILE_SIZES = {name: 4096 + 64 * index for index, name in enumerate(TILE_ORDER)}
STANDIN_TILE_CELLS = {
    name: [(cell["row"], cell["col"]) for cell in STANDIN_CELLS[name]]
    for name in TILE_ORDER
}


class FreezeTestBase(unittest.TestCase):
    def patch(self, name, value):
        original = getattr(freeze, name)
        setattr(freeze, name, value)
        self.addCleanup(setattr, freeze, name, original)

    def freeze_stage(self, cache):
        root = Path(freeze.freeze_root(str(cache)))
        return sorted(entry for entry in root.iterdir()
                      if entry.name.startswith(freeze.STAGE_PREFIX))

    def patch_worker(self, worker_script):
        self.patch("worker_command",
                   lambda venv_python, script, request_path:
                   [sys.executable, str(worker_script), request_path])

    def tmp_dir(self):
        parent = "/tmp/opencode" if os.path.isdir("/tmp/opencode") else None
        context = tempfile.TemporaryDirectory(prefix="sedan-lidar-freeze-test-", dir=parent)
        self.addCleanup(context.cleanup)
        return Path(context.name)

    def run_main(self, *argv):
        buffer = io.StringIO()
        with contextlib.redirect_stdout(buffer):
            code = freeze.main(list(argv))
        return code, buffer.getvalue()

    def patch_pins(self, sizes):
        pins = tuple(
            {"name": name, "size": sizes[name], "rel": os.path.join("lidarhd", LOT, name)}
            for name in TILE_ORDER)
        self.patch("TILE_PINS", pins)
        self.patch("FIRST_TILE_PIN", pins[0])
        self.patch("FIRST_TILE_SIZE", sizes[FIRST_TILE_NAME])
        self.patch("OTHER_TILES", {name: sizes[name] for name in TILE_ORDER[1:]})
        self.patch("TOTAL_PINNED_BYTES", sum(sizes.values()))
        return pins

    def patch_spawn_guards(self):
        self.patch("check_mount_realness", lambda root: True)

        def logged_free_space(ancestor, required_bytes):
            freeze.log(f"free space check skipped by test: required={required_bytes}")

        self.patch("check_free_space", logged_free_space)

    def write_tiles(self, cache, sizes, content_overrides=None, only=None, rel_overrides=None):
        content_overrides = content_overrides or {}
        digests = {}
        for name in (only or TILE_ORDER):
            rel = (rel_overrides or {}).get(name, os.path.join("lidarhd", LOT, name))
            path = Path(cache) / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            data = content_overrides.get(name, deterministic_bytes(name, sizes[name]))
            path.write_bytes(data)
            digests[name] = hashlib.sha256(data).hexdigest()
        return digests

    def write_manifest(self, cache, sizes, digests, footprints, omit=(), rel_overrides=None):
        entries = {}
        for name in TILE_ORDER:
            if name in omit:
                continue
            entries[name] = {
                "size": sizes[name],
                "sha256": digests[name],
                "footprint_l93": footprints[name],
            }
            if rel_overrides and name in rel_overrides:
                entries[name]["rel_path"] = rel_overrides[name]
        path = Path(freeze.integrity_manifest_path(str(cache)))
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(entries, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        return path

    def write_probe_report(self, cache, report=None):
        path = Path(freeze.probe_report_path(str(cache)))
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(report or make_probe_report(), indent=2, sort_keys=True) + "\n",
                        encoding="utf-8")
        return path

    def write_venv_placeholder(self, cache):
        venv = Path(cache) / "tools" / "lidar-venv" / "bin" / "python"
        venv.parent.mkdir(parents=True, exist_ok=True)
        venv.write_text("#!/bin/sh\nexit 0\n", encoding="utf-8")
        venv.chmod(0o755)
        return venv

    def setup_ready_cache(self, content_overrides=None, omit_manifest=(),
                          first_tile_footprint=FIRST_TILE_OBSERVED_FOOTPRINT,
                          patch_policy=True, rel_overrides=None):
        root = self.tmp_dir()
        cache = root / "cache"
        if patch_policy:
            def verified_policy(path):
                freeze.log(f"operator policy verified (test fixture mode): {path}")

            self.patch("check_operator_policy", verified_policy)
        self.patch_pins(STANDIN_TILE_SIZES)
        digests = self.write_tiles(cache, STANDIN_TILE_SIZES, content_overrides,
                                   rel_overrides=rel_overrides)
        footprints = {name: footprint_around(STANDIN_TILE_CELLS[name]) for name in TILE_ORDER}
        footprints[FIRST_TILE_NAME] = dict(first_tile_footprint)
        self.write_manifest(cache, STANDIN_TILE_SIZES, digests, footprints, omit=omit_manifest,
                            rel_overrides=rel_overrides)
        self.write_probe_report(cache)
        self.write_venv_placeholder(cache)
        return cache, root


class PreflightTests(FreezeTestBase):
    def test_preflight_readonly_writes_nothing(self):
        cache, root = self.setup_ready_cache()
        before = sorted(str(path) for path in Path(cache).rglob("*"))
        code, output = self.run_main(str(cache))
        self.assertEqual(freeze.EXIT_OK, code)
        after = sorted(str(path) for path in Path(cache).rglob("*"))
        self.assertEqual(before, after)
        for name in TILE_ORDER:
            self.assertIn(name, output)
        self.assertIn("input integrity manifest contract", output)
        self.assertIn(str(freeze.integrity_manifest_path(str(cache))), output)
        self.assertIn("protocol constants verified", output)
        self.assertIn("nothing was written", output)

    def test_preflight_reports_tiles_lacking_manifest_entries_without_failing(self):
        cache, _root = self.setup_ready_cache(omit_manifest=set(TILE_ORDER))
        Path(freeze.integrity_manifest_path(str(cache))).unlink()
        code, output = self.run_main(str(cache))
        self.assertEqual(freeze.EXIT_OK, code)
        self.assertIn("integrity manifest absent", output)
        for name in TILE_ORDER:
            self.assertIn(f"tile lacks an integrity manifest entry (pending input): {name}", output)

    def test_preflight_reports_pending_transport_and_stays_green(self):
        root = self.tmp_dir()
        cache = root / "cache"
        sizes = dict(STANDIN_TILE_SIZES)
        self.patch_pins(sizes)
        digests = self.write_tiles(cache, sizes, only=TILE_ORDER[:1])
        footprints = {name: footprint_around(STANDIN_TILE_CELLS[name]) for name in TILE_ORDER}
        footprints[FIRST_TILE_NAME] = dict(FIRST_TILE_OBSERVED_FOOTPRINT)
        self.write_manifest(cache, sizes, digests, footprints, omit=set(TILE_ORDER[1:]))
        self.write_probe_report(cache)
        self.write_venv_placeholder(cache)
        code, output = self.run_main(str(cache))
        self.assertEqual(freeze.EXIT_OK, code)
        for name in TILE_ORDER[1:]:
            self.assertIn(f"tile pending transport (not present, preflight continues): {name}",
                          output)

    def test_preflight_size_drift_fails_state(self):
        cache, _root = self.setup_ready_cache(
            content_overrides={TILE_ORDER[2]: deterministic_bytes(TILE_ORDER[2], 4096)})
        code, output = self.run_main(str(cache))
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertIn(f"size drift: 4096 on disk vs pinned {STANDIN_TILE_SIZES[TILE_ORDER[2]]}",
                      output)

    def test_preflight_sha_mismatch_fails_state(self):
        victim = TILE_ORDER[3]
        cache, _root = self.setup_ready_cache(
            content_overrides={victim: deterministic_bytes("drift", STANDIN_TILE_SIZES[victim])})
        manifest_path = Path(freeze.integrity_manifest_path(str(cache)))
        entries = json.loads(manifest_path.read_text(encoding="utf-8"))
        entries[victim]["sha256"] = hashlib.sha256(
            deterministic_bytes(victim, STANDIN_TILE_SIZES[victim])).hexdigest()
        manifest_path.write_text(json.dumps(entries, indent=2, sort_keys=True) + "\n",
                                 encoding="utf-8")
        code, output = self.run_main(str(cache))
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertIn(f"tile {victim} sha256 mismatch", output)

    def test_preflight_symlink_tile_fails_state(self):
        cache, root = self.setup_ready_cache()
        victim = Path(cache) / "lidarhd" / LOT / TILE_ORDER[1]
        target = root / "elsewhere.laz"
        target.write_bytes(victim.read_bytes())
        victim.unlink()
        victim.symlink_to(target)
        code, output = self.run_main(str(cache))
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertIn("symlink component", output)
        self.assertIn(f"tile {TILE_ORDER[1]} path", output)

    def test_preflight_policy_drift_fails_state(self):
        cache, root = self.setup_ready_cache(patch_policy=False)
        memo = root / "tampered-memo.md"
        memo.write_text("tampered, no policy content here\n", encoding="utf-8")
        self.patch("OPERATOR_POLICY_PATH", str(memo))
        code, output = self.run_main(str(cache))
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertIn("operator policy memo", output)


class SixInputFailureTests(FreezeTestBase):
    """Every six-input failure exits 4 BEFORE any worker spawn (spy seam)."""

    def freeze_run(self, cache):
        calls = []
        real_command = freeze.worker_command

        def spying_command(venv_python, worker_script, request_path):
            calls.append((venv_python, worker_script, request_path))
            return real_command(venv_python, worker_script, request_path)

        self.patch("worker_command", spying_command)
        code, output = self.run_main("--freeze", str(cache))
        return code, output, calls

    def assert_refused_before_worker(self, cache, expected_fragment):
        code, output, calls = self.freeze_run(cache)
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertEqual([], calls)
        self.assertIn(expected_fragment, output)

    def test_missing_tile(self):
        cache, root = self.setup_ready_cache()
        (Path(cache) / "lidarhd" / LOT / TILE_ORDER[4]).unlink()
        self.patch_spawn_guards()
        self.assert_refused_before_worker(cache, f"tile {TILE_ORDER[4]} missing at ")

    def test_unverified_manifest_missing_entirely(self):
        cache, _root = self.setup_ready_cache(omit_manifest=set(TILE_ORDER))
        Path(freeze.integrity_manifest_path(str(cache))).unlink()
        self.patch_spawn_guards()
        self.assert_refused_before_worker(cache, "integrity manifest missing at ")

    def test_unverified_manifest_entry_missing_for_one_tile(self):
        cache, _root = self.setup_ready_cache(omit_manifest=(TILE_ORDER[5],))
        self.patch_spawn_guards()
        self.assert_refused_before_worker(
            cache, f"integrity manifest has no entry for {TILE_ORDER[5]}")

    def test_size_mismatch_on_disk(self):
        victim = TILE_ORDER[2]
        short = STANDIN_TILE_SIZES[victim] - 1
        cache, _root = self.setup_ready_cache(
            content_overrides={victim: deterministic_bytes(victim, short)})
        self.patch_spawn_guards()
        self.assert_refused_before_worker(
            cache, f"tile {victim} size drift: {short} on disk vs pinned "
                   f"{STANDIN_TILE_SIZES[victim]}")

    def test_sha_mismatch(self):
        victim = TILE_ORDER[3]
        cache, _root = self.setup_ready_cache(
            content_overrides={victim: deterministic_bytes("drift", STANDIN_TILE_SIZES[victim])})
        # The manifest must still record the PINNED-expected content digest, so the
        # drifted file fails verification rather than re-defining the manifest.
        manifest_path = Path(freeze.integrity_manifest_path(str(cache)))
        entries = json.loads(manifest_path.read_text(encoding="utf-8"))
        entries[victim]["sha256"] = hashlib.sha256(
            deterministic_bytes(victim, STANDIN_TILE_SIZES[victim])).hexdigest()
        manifest_path.write_text(json.dumps(entries, indent=2, sort_keys=True) + "\n",
                                 encoding="utf-8")
        self.patch_spawn_guards()
        self.assert_refused_before_worker(cache, f"tile {victim} sha256 mismatch")

    def test_pin_mismatch_manifest_size(self):
        victim = TILE_ORDER[2]
        cache, root = self.setup_ready_cache()
        manifest_path = Path(freeze.integrity_manifest_path(str(cache)))
        entries = json.loads(manifest_path.read_text(encoding="utf-8"))
        entries[victim]["size"] = STANDIN_TILE_SIZES[victim] + 1
        manifest_path.write_text(json.dumps(entries, indent=2, sort_keys=True) + "\n",
                                 encoding="utf-8")
        self.patch_spawn_guards()
        self.assert_refused_before_worker(
            cache, f"integrity manifest size for {victim} is "
                   f"{STANDIN_TILE_SIZES[victim] + 1}, pinned {STANDIN_TILE_SIZES[victim]}")

    def test_missing_footprint_side_tile(self):
        cache, _root = self.setup_ready_cache()
        manifest_path = Path(freeze.integrity_manifest_path(str(cache)))
        entries = json.loads(manifest_path.read_text(encoding="utf-8"))
        entries[TILE_ORDER[3]]["footprint_l93"] = None
        manifest_path.write_text(json.dumps(entries, indent=2, sort_keys=True) + "\n",
                                 encoding="utf-8")
        self.patch_spawn_guards()
        self.assert_refused_before_worker(
            cache, f"no observed header footprint for {TILE_ORDER[3]}")

    def test_missing_footprint_first_tile_without_probe_report(self):
        cache, _root = self.setup_ready_cache()
        manifest_path = Path(freeze.integrity_manifest_path(str(cache)))
        entries = json.loads(manifest_path.read_text(encoding="utf-8"))
        entries[FIRST_TILE_NAME]["footprint_l93"] = None
        manifest_path.write_text(json.dumps(entries, indent=2, sort_keys=True) + "\n",
                                 encoding="utf-8")
        Path(freeze.probe_report_path(str(cache))).unlink()
        self.patch_spawn_guards()
        self.assert_refused_before_worker(
            cache, f"no footprint for {FIRST_TILE_NAME}: manifest sidecar omits it")

    def test_missing_venv_refuses_with_guard_before_spawn(self):
        cache, root = self.setup_ready_cache()
        venv = Path(cache) / "tools" / "lidar-venv" / "bin" / "python"
        venv.unlink()
        self.patch_spawn_guards()
        code, output, calls = self.freeze_run(cache)
        self.assertEqual(freeze.EXIT_GUARD, code)
        self.assertEqual([], calls)
        self.assertIn("isolated lidar venv python missing", output)

    # Manifest rel_path overrides (the acquire receipt records each tile's
    # actual verified location, e.g. lidarhd/stage2/<LOT>/<name>).

    def rel_override_failure(self, victim, rel_path, extra_setup=None):
        cache, root = self.setup_ready_cache()
        manifest_path = Path(freeze.integrity_manifest_path(str(cache)))
        entries = json.loads(manifest_path.read_text(encoding="utf-8"))
        entries[victim]["rel_path"] = rel_path
        manifest_path.write_text(json.dumps(entries, indent=2, sort_keys=True) + "\n",
                                 encoding="utf-8")
        if extra_setup is not None:
            extra_setup(cache, root)
        self.patch_spawn_guards()
        code, output, calls = self.freeze_run(cache)
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertEqual([], calls)
        return output

    def test_rel_path_stage2_layout_verifies_all_six_tiles(self):
        stage2 = {name: f"lidarhd/stage2/{LOT}/{name}" for name in TILE_ORDER[1:]}
        cache, root = self.setup_ready_cache(rel_overrides=stage2)
        self.patch_spawn_guards()
        self.patch_worker(write_standin_worker(root))
        code, output = self.run_main("--freeze", str(cache))
        self.assertEqual(freeze.EXIT_OK, code, output)
        for name in TILE_ORDER[1:]:
            self.assertIn(f"lidarhd/stage2/{LOT}/{name}", output)
        stage = self.freeze_stage(cache)[0]
        self.assertTrue((stage / freeze.COMPLETED_NAME).exists())
        self.assertEqual(6, len(list((stage / freeze.RAW_DIR_REL).iterdir())))

    def test_rel_path_absolute_fails_state(self):
        victim = TILE_ORDER[2]
        output = self.rel_override_failure(
            victim, f"/tmp/opencode/elsewhere/{LOT}/{victim}")
        self.assertIn("is not a relative posix path", output)

    def test_rel_path_dotdot_fails_state(self):
        victim = TILE_ORDER[2]
        output = self.rel_override_failure(
            victim, f"lidarhd/../../outside/{LOT}/{victim}")
        self.assertIn("has an empty, '.', or '..' component", output)

    def test_rel_path_escaping_cache_root_fails_state(self):
        victim = TILE_ORDER[3]
        output = self.rel_override_failure(
            victim, f"lidarhd/stage2/../{LOT}/{victim}")
        self.assertIn("has an empty, '.', or '..' component", output)

    def test_rel_path_symlink_component_fails_state(self):
        victim = TILE_ORDER[4]
        real_dir = None

        def make_symlink(cache, root):
            nonlocal real_dir
            real_dir = root / "elsewhere" / LOT
            real_dir.mkdir(parents=True, exist_ok=True)
            (real_dir / victim).write_bytes(deterministic_bytes(victim, STANDIN_TILE_SIZES[victim]))
            stage2 = Path(cache) / "lidarhd" / "stage2"
            stage2.symlink_to(root / "elsewhere", target_is_directory=True)

        output = self.rel_override_failure(
            victim, f"lidarhd/stage2/{LOT}/{victim}", extra_setup=make_symlink)
        self.assertIn("symlink component", output)

    def test_rel_path_wrong_tile_name_fails_state(self):
        victim = TILE_ORDER[5]
        other = TILE_ORDER[1]
        output = self.rel_override_failure(
            victim, f"lidarhd/stage2/{LOT}/{other}")
        self.assertIn(f"does not end with the pinned tile name {victim!r}", output)
        self.assertIn("pin mismatch", output)

    # Worker script as a configurable pipeline input with approved-hash validation.

    def test_default_worker_resolves_approved(self):
        resolution = freeze.resolve_worker_script(None)
        self.assertEqual(freeze.worker_script_path(), resolution["path"])
        self.assertEqual(freeze.APPROVED_WORKER_SHA256S[0], resolution["sha256"])
        self.assertEqual("pinned default", resolution["source"])

    def test_default_worker_hash_drift_fails_state_before_spawn(self):
        cache, _root = self.setup_ready_cache()
        self.patch("APPROVED_WORKER_SHA256S", ("0" * 64,))
        self.patch_spawn_guards()
        code, output, calls = self.freeze_run(cache)
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertEqual([], calls)
        real_sha = hashlib.sha256(
            Path(freeze.worker_script_path()).read_bytes()).hexdigest()
        self.assertIn(f"worker script sha256 drift: {real_sha}", output)
        self.assertIn("0" * 64, output)
        self.assertIn("add B's released worker hash", output)

    def test_select_module_hash_drift_fails_state_before_spawn(self):
        cache, _root = self.setup_ready_cache()
        self.patch("SELECT_MODULE_SHA256", "0" * 64)
        self.patch_spawn_guards()
        code, output, calls = self.freeze_run(cache)
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertEqual([], calls)
        real_sha = hashlib.sha256(
            Path(freeze.select_module_path()).read_bytes()).hexdigest()
        self.assertIn(f"selection module sha256 drift: {real_sha}", output)
        self.assertIn("0" * 64, output)
        self.assertIn("does not equal the pinned", output)

    def worker_override_failure(self, rel_path, sha, base_setup=None, expect=None):
        cache, root = self.setup_ready_cache()
        if base_setup is not None:
            base = root / "geo-base"
            base.mkdir(parents=True, exist_ok=True)
            base_setup(base)
            self.patch("worker_base_dir", lambda: str(base))
        manifest_path = Path(freeze.integrity_manifest_path(str(cache)))
        entries = json.loads(manifest_path.read_text(encoding="utf-8"))
        entries["worker"] = {"rel_path": rel_path, "sha256": sha}
        manifest_path.write_text(json.dumps(entries, indent=2, sort_keys=True) + "\n",
                                 encoding="utf-8")
        self.patch_spawn_guards()
        code, output, calls = self.freeze_run(cache)
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertEqual([], calls)
        if expect is not None:
            self.assertIn(expect, output)
        return output

    def test_worker_override_unapproved_sha_fails_state_before_spawn(self):
        body = b"# bounded-budget worker stand-in content, never executed by tests\n"
        digest = hashlib.sha256(body).hexdigest()

        def setup(base):
            (base / "bounded").mkdir(parents=True, exist_ok=True)
            (base / "bounded" / "bounded-worker.py").write_bytes(body)

        self.worker_override_failure(
            "bounded/bounded-worker.py", digest, base_setup=setup,
            expect=f"worker script sha256 drift: {digest}")

    def test_worker_override_traversal_rel_path_fails_state_before_spawn(self):
        self.worker_override_failure(
            "../sedan_lidar_probe_worker.py", "a" * 64,
            expect="has an empty, '.', or '..' component")

    def test_worker_override_non_py_fails_state_before_spawn(self):
        self.worker_override_failure(
            "bounded/bounded-worker.txt", "b" * 64, expect="must end .py")

    def test_worker_override_symlink_component_fails_state_before_spawn(self):
        def setup(base):
            (base / "linked.py").symlink_to(Path(freeze.worker_script_path()))

        self.worker_override_failure(
            "linked.py", "c" * 64, base_setup=setup, expect="symlink component")


class BoundedRunnerTests(FreezeTestBase):
    def run_runner(self, code_text, output_path, deadline=10.0, cap=None):
        cap = cap if cap is not None else 1 << 20
        return freeze.run_worker_bounded(
            [sys.executable, "-c", code_text], str(output_path), deadline, cap)

    def assert_reaped(self, run):
        with self.assertRaises(ProcessLookupError):
            os.kill(run.pid, 0)

    def test_normal_completion(self):
        root = self.tmp_dir()
        output = root / "measurements.json"
        run = self.run_runner(
            "import sys; print('hello worker'); sys.exit(0)", output)
        self.assertIsNone(run.problem)
        self.assertEqual(0, run.returncode)
        self.assertIn(b"hello worker", run.stdout)
        self.assertGreater(run.wall_seconds, 0.0)
        self.assertGreaterEqual(run.rss_after_kb, 0)
        self.assert_reaped(run)

    def test_stdout_oversize_capped_during_capture_and_reaped(self):
        root = self.tmp_dir()
        output = root / "measurements.json"
        cap = 1 << 20
        run = self.run_runner(
            "import sys\n"
            f"chunk = b'x' * 65536\n"
            "for _ in range(20):\n"
            "    sys.stdout.buffer.write(chunk)\n"
            "sys.stdout.buffer.flush()\n",
            output, cap=cap)
        self.assertIsNotNone(run.problem)
        self.assertIn("stdout oversize beyond the", run.problem)
        self.assertEqual(cap, len(run.stdout))
        self.assert_reaped(run)
        self.assertNotEqual(0, run.returncode)

    def test_stderr_oversize_capped_and_reaped(self):
        root = self.tmp_dir()
        output = root / "measurements.json"
        cap = 1 << 20
        run = self.run_runner(
            "import sys\n"
            f"chunk = b'e' * 65536\n"
            "for _ in range(20):\n"
            "    sys.stderr.buffer.write(chunk)\n"
            "sys.stderr.buffer.flush()\n",
            output, cap=cap)
        self.assertIsNotNone(run.problem)
        self.assertIn("stderr oversize beyond the", run.problem)
        self.assertEqual(cap, len(run.stderr))
        self.assert_reaped(run)

    def test_stall_timeout_kills_and_reaps(self):
        root = self.tmp_dir()
        output = root / "measurements.json"
        started = time.monotonic()
        run = self.run_runner(
            "import time; time.sleep(30)", output, deadline=0.5)
        elapsed = time.monotonic() - started
        self.assertEqual("timeout", run.problem)
        self.assertLess(elapsed, 20.0)
        self.assert_reaped(run)
        self.assertNotEqual(0, run.returncode)

    def test_nonzero_returncode_is_reported_not_killed(self):
        root = self.tmp_dir()
        output = root / "measurements.json"
        run = self.run_runner(
            "import sys\n"
            "sys.stderr.write('worker refusal\\n')\n"
            "sys.exit(3)\n",
            output)
        self.assertIsNone(run.problem)
        self.assertEqual(3, run.returncode)
        self.assertIn(b"worker refusal", run.stderr)
        self.assert_reaped(run)

    def test_eof_order_variant_consumes_both_streams(self):
        root = self.tmp_dir()
        output = root / "measurements.json"
        run = self.run_runner(
            "import sys, time\n"
            "sys.stderr.write('err first\\n'); sys.stderr.flush()\n"
            "time.sleep(0.2)\n"
            "print('out second'); sys.stdout.flush()\n",
            output)
        self.assertIsNone(run.problem)
        self.assertEqual(0, run.returncode)
        self.assertIn(b"err first", run.stderr)
        self.assertIn(b"out second", run.stdout)
        self.assert_reaped(run)

    def test_output_file_oversize_kills_and_reaps(self):
        root = self.tmp_dir()
        output = root / "measurements.json"
        cap = 4096
        writer = (
            "import sys, time\n"
            "path = sys.argv[1]\n"
            "with open(path, 'wb') as handle:\n"
            "    handle.write(b'z' * 8192)\n"
            "    handle.flush()\n"
            "    time.sleep(5)\n"
        )
        run = freeze.run_worker_bounded(
            [sys.executable, "-c", writer, str(output)], str(output), 10.0, cap)
        self.assertIsNotNone(run.problem)
        self.assertIn("output file beyond the", run.problem)
        self.assert_reaped(run)


class MergeTests(FreezeTestBase):
    ANCHOR = ANCHOR_L93
    FOOTPRINT = {"x_min": 839600.0, "y_min": 6956700.0,
                 "x_max": 841400.0, "y_max": 6958500.0}

    def make_block(self, cells, footprint=None, origin=None):
        origin = origin or self.ANCHOR
        footprint = footprint or self.FOOTPRINT
        columns = ("col", "row", "n2", "a", "b", "c", "sigma_fit", "slope_deg", "curv_bound",
                   "curv_unavailable", "fit_unavailable",
                   "mix2", "mix3", "mix4", "mix5", "mix6", "mix9", "mix17", "e2_dist", "e3_dist")
        arrays = {key: [] for key in columns}
        for cell in cells:
            n2 = cell.get("n2", 40)
            arrays["col"].append(cell["col"])
            arrays["row"].append(cell["row"])
            arrays["n2"].append(n2)
            has_fit = n2 >= 3
            arrays["a"].append(150.0 if has_fit else None)
            arrays["b"].append(0.0 if has_fit else None)
            arrays["c"].append(0.0 if has_fit else None)
            arrays["sigma_fit"].append(cell.get("sigma", 0.004) if has_fit else None)
            arrays["slope_deg"].append(0.5 if has_fit else None)
            arrays["curv_bound"].append(0.001 if n2 >= 6 else None)
            arrays["curv_unavailable"].append(cell.get("curv_unavailable", False))
            arrays["fit_unavailable"].append(cell.get("fit_unavailable", False))
            arrays["mix2"].append(n2)
            arrays["mix3"].append(0)
            arrays["mix4"].append(0)
            arrays["mix5"].append(0)
            arrays["mix6"].append(0)
            arrays["mix9"].append(0)
            arrays["mix17"].append(0)
            arrays["e2_dist"].append(cell.get("e2_dist"))
            arrays["e3_dist"].append(cell.get("e3_dist"))
        return {
            "candidate_measurement_version": 1,
            "lattice_origin_l93": list(origin),
            "spacing_m": 3.048,
            "lattice_cols": 361,
            "lattice_rows": 361,
            "footprint_observed_l93": footprint,
            "cells_contained": len(cells),
            "cells_enumerated": 361 * 361,
            "row_order": "row-major (row ascending, then col ascending)",
            "columns": list(columns),
            "arrays": arrays,
        }

    def convert(self, cells, footprint=None):
        block = self.make_block(cells, footprint)
        canonical = set(sedan_lidar_select.enumerate_lattice_cells())
        records, meta, problem = freeze.records_from_candidates_block(
            block, self.ANCHOR, AOI_BBOX_L93, canonical)
        self.assertIsNone(problem)
        return records, meta

    def make_values(self, **overrides):
        values = {
            "n2": 40, "a": 150.0, "b": 0.0, "c": 0.0, "sigma_fit": 0.004,
            "slope_deg": 0.5, "curv_bound": 0.001, "curv_unavailable": False,
            "fit_unavailable": False, "mix2": 40, "mix3": 0, "mix4": 0, "mix5": 0,
            "mix6": 0, "mix9": 0, "mix17": 0, "e2_dist": None, "e3_dist": None,
        }
        values.update(overrides)
        return values

    def test_canonical_geometry_constants(self):
        self.assertEqual((639841.997, 5506768.179), tuple(sedan_lidar_select.DEFAULT_AOI.origin_utm))
        self.assertEqual((640391.997, 5507318.179), tuple(sedan_lidar_select.DEFAULT_AOI.centre_utm))
        self.assertEqual(3.048, sedan_lidar_select.LATTICE)
        self.assertEqual("1.2", sedan_lidar_select.PROTOCOL_VERSION)
        self.assertEqual(3, sedan_lidar_select.MIN_SECTORS)
        self.assertEqual(5, sedan_lidar_select.MIN_PRIMARY)
        self.assertEqual(2, sedan_lidar_select.N_ALTERNATES)
        self.assertEqual(359 * 359, len(sedan_lidar_select.enumerate_lattice_cells()))
        self.assertEqual((361, 361), sedan_lidar_select.full_extent_cell_counts())
        sector_names = set()
        for row in (0, 179, 359):
            for col in (0, 179, 359):
                x = 639841.997 + (col + 0.5) * 3.048
                y = 5506768.179 + (row + 0.5) * 3.048
                sector_names.add(sedan_lidar_select.sector_name(
                    *sedan_lidar_select.sector_of(x, y)))
        self.assertEqual(9, len(sector_names))
        freeze.check_protocol_constants()

    def test_conversion_eligible_hole_and_ring(self):
        cells = [
            {"row": 100, "col": 100},
            {"row": 200, "col": 200, "n2": 3},
            {"row": 0, "col": 0},
            {"row": 100, "col": 8},
        ]
        records, meta = self.convert(cells)
        self.assertEqual(3, len(records))
        self.assertEqual(1, meta["ignored_margin_ring_cells"])
        by_id = {record["id"]: record for record in records}
        self.assertTrue(by_id["r100c100"]["eligible"])
        hole = by_id["r200c200"]
        self.assertFalse(hole["eligible"])
        self.assertIn("E1", [rule for rule, _trigger in hole["fired"]])
        seam = by_id["r100c8"]
        self.assertFalse(seam["eligible"])
        self.assertIn("E6", [rule for rule, _trigger in seam["fired"]])
        self.assertLess(seam["e6_dist"], sedan_lidar_select.E6_SEAM)

    def test_outside_footprint_cells_are_dropped_with_count(self):
        cells = [{"row": 100, "col": 100}]
        tight = footprint_around([(100, 100)], pad=3.0)
        records, meta = self.convert(cells + [{"row": 350, "col": 350}], footprint=tight)
        self.assertEqual(1, len(records))
        self.assertEqual(1, meta["containment_failures_rereported"])
        self.assertEqual(["r100c100"], [record["id"] for record in records])

    def test_nonfinite_and_fit_unavailable_never_eligible(self):
        records, _meta = self.convert([
            {"row": 10, "col": 10, "sigma": float("nan")},
            {"row": 20, "col": 20, "fit_unavailable": True},
        ])
        self.assertEqual(2, len(records))
        for record in records:
            self.assertFalse(record["eligible"])
        nonfinite = records[0]
        self.assertIn(freeze._NONFINITE_EXCLUSION,
                      [rule for rule, _trigger in nonfinite["fired"]])
        self.assertEqual(["sigma_fit"], nonfinite["nonfinite_fields"])
        unavailable = records[1]
        self.assertIn(freeze._FIT_UNAVAILABLE_EXCLUSION,
                      [rule for rule, _trigger in unavailable["fired"]])

    def test_duplicate_eligible_is_structural_stop(self):
        records_a, _meta = self.convert([{"row": 100, "col": 100, "sigma": 0.004}])
        records_b, _meta = self.convert([{"row": 100, "col": 100, "sigma": 0.005}])
        catalog, sources = {}, {}
        freeze.merge_tile_records(catalog, sources, "tileA.laz", records_a)
        with self.assertRaises(freeze.Failure) as ctx:
            freeze.merge_tile_records(catalog, sources, "tileB.laz", records_b)
        self.assertEqual(freeze.EXIT_GUARD, ctx.exception.code)
        self.assertIn("r100c100", ctx.exception.message)
        self.assertIn("tileA.laz", ctx.exception.message)
        self.assertIn("tileB.laz", ctx.exception.message)

    def test_identical_ineligible_duplicate_merges(self):
        records_a, _meta = self.convert([{"row": 200, "col": 200, "n2": 3}])
        records_b, _meta = self.convert([{"row": 200, "col": 200, "n2": 3}])
        catalog, sources = {}, {}
        freeze.merge_tile_records(catalog, sources, "tileA.laz", records_a)
        freeze.merge_tile_records(catalog, sources, "tileB.laz", records_b)
        self.assertEqual(1, len(catalog))
        self.assertEqual(["tileA.laz", "tileB.laz"], sources["r200c200"])

    def test_differing_ineligible_duplicate_stops(self):
        records_a, _meta = self.convert([{"row": 200, "col": 200, "n2": 3}])
        records_b, _meta = self.convert([{"row": 200, "col": 200, "n2": 4}])
        catalog, sources = {}, {}
        freeze.merge_tile_records(catalog, sources, "tileA.laz", records_a)
        with self.assertRaises(freeze.Failure) as ctx:
            freeze.merge_tile_records(catalog, sources, "tileB.laz", records_b)
        self.assertIn("first differing field", ctx.exception.message)
        self.assertIn("r200c200", ctx.exception.message)

    def test_finalize_record_shape_matches_candidate_columns(self):
        record = freeze.finalize_record(7, 9, self.make_values(), self.ANCHOR[0],
                                        self.ANCHOR[1], 3.048, AOI_BBOX_L93)
        self.assertEqual("r7c9", record["id"])
        for column in sedan_lidar_select._CAND_COLUMNS:
            self.assertIn(column, record)
        utm_x, utm_y = sedan_lidar_select.lattice_centre(7, 9)
        self.assertEqual(utm_x, record["utm_x"])
        expected_x = self.ANCHOR[0] + 9.5 * 3.048
        self.assertAlmostEqual(expected_x, record["l93_x"], places=9)
        self.assertIsNone(record["rank"])
        self.assertTrue(record["eligible"])


class SelectionFloorTests(FreezeTestBase):
    def make_values(self, sigma=0.004, n2=40):
        return {
            "n2": n2, "a": 150.0, "b": 0.0, "c": 0.0, "sigma_fit": sigma,
            "slope_deg": 0.5, "curv_bound": 0.001, "curv_unavailable": False,
            "fit_unavailable": False, "mix2": n2, "mix3": 0, "mix4": 0, "mix5": 0,
            "mix6": 0, "mix9": 0, "mix17": 0, "e2_dist": None, "e3_dist": None,
        }

    def catalog_from(self, cells):
        catalog = []
        for row, col, sigma in cells:
            catalog.append(freeze.finalize_record(
                row, col, self.make_values(sigma=sigma), ANCHOR_L93[0], ANCHOR_L93[1],
                3.048, AOI_BBOX_L93))
        return catalog

    FIVE_PLUS_ALTS = [
        (150, 150, 0.001), (60, 60, 0.002), (60, 240, 0.003),
        (240, 60, 0.004), (240, 240, 0.005),
        (100, 100, 0.006), (200, 200, 0.007),
    ]

    def test_five_primaries_three_sectors_two_alternates(self):
        selection = freeze.select_final(self.catalog_from(self.FIVE_PLUS_ALTS))
        self.assertEqual(5, len(selection.primaries))
        self.assertGreaterEqual(len(selection.sectors_covered), 3)
        self.assertEqual(2, len(selection.alternates))
        self.assertEqual(0, selection.alternates_shortfall)
        self.assertFalse(selection.stage_1_preliminary)
        roles = {record["id"]: "primary" for record in selection.primaries}
        roles.update({record["id"]: "alternate" for record in selection.alternates})
        self.assertEqual(7, len(roles))

    def test_floor_failure_is_honest_stop(self):
        cells = self.FIVE_PLUS_ALTS[:4]
        with self.assertRaises(freeze.Failure) as ctx:
            freeze.select_final(self.catalog_from(cells))
        self.assertEqual(freeze.EXIT_GUARD, ctx.exception.code)
        self.assertIn("eligible_below_floor", ctx.exception.message)

    def test_sector_floor_failure_is_honest_stop(self):
        # Five mutually >=100 m eligible cells, but all inside two of the nine
        # full-AOI sectors: the 3-sector floor must STOP after the 5-primary floor.
        cells = [(10, 10, 0.001), (10, 110, 0.002), (20, 150, 0.003),
                 (40, 180, 0.004), (5, 200, 0.005)]
        with self.assertRaises(freeze.Failure) as ctx:
            freeze.select_final(self.catalog_from(cells))
        self.assertIn("sectors_below_floor", ctx.exception.message)

    def test_stage_1_preliminary_selection_can_never_freeze(self):
        catalog = self.catalog_from(self.FIVE_PLUS_ALTS)
        preliminary = sedan_lidar_select.select(catalog, stage_1_preliminary=True)
        self.assertTrue(preliminary.stage_1_preliminary)
        scratch = self.tmp_dir() / "final"
        with self.assertRaises(freeze.Failure) as ctx:
            freeze.freeze_final(catalog, preliminary, str(scratch), note="test")
        self.assertEqual(freeze.EXIT_GUARD, ctx.exception.code)
        self.assertIn("stage1_preliminary_never_final", ctx.exception.message)
        self.assertFalse(scratch.exists())

    def test_freeze_final_writes_the_three_unchanged_artifacts(self):
        catalog = self.catalog_from(self.FIVE_PLUS_ALTS)
        selection = freeze.select_final(catalog)
        scratch = self.tmp_dir() / "final"
        manifest = freeze.freeze_final(catalog, selection, str(scratch), note="unit note")
        self.assertEqual(
            {"candidates.tsv", "selection.tsv", "frozen-manifest.json"},
            {path.name for path in scratch.iterdir()})
        self.assertEqual(manifest["selection"]["primary_ids"],
                         [record["id"] for record in selection.primaries])
        digest = sedan_lidar_select.canonical_digest(manifest)
        self.assertEqual(64, len(digest))


class OperatorPolicyTests(FreezeTestBase):
    # Verbatim copy of the recorded operator decision memo (the pinned sha256
    # below is the recorded value in docs/roadmap/deep-dives/r2-sedan-ground-truth.md).
    MEMO_TEXT = None  # loaded lazily in setUpClass to keep line lengths sane

    @classmethod
    def setUpClass(cls):
        real = freeze.OPERATOR_POLICY_PATH
        if os.path.isfile(real):
            with open(real, "r", encoding="utf-8") as handle:
                cls.MEMO_TEXT = handle.read()

    def setUp(self):
        if OperatorPolicyTests.MEMO_TEXT is None:
            self.skipTest("operator policy memo not present on this host; "
                          "constant checks still run below")

    def test_pinned_digest_constant_matches_recorded_value(self):
        self.assertEqual("d4e25a8f94da4f3228ed22377bb738881be50f9e20342af10bf595c675d57cbc",
                         freeze.OPERATOR_POLICY_SHA256)

    def test_recorded_memo_matches_pinned_digest(self):
        digest = hashlib.sha256(OperatorPolicyTests.MEMO_TEXT.encode("utf-8")).hexdigest()
        self.assertEqual(freeze.OPERATOR_POLICY_SHA256, digest)

    def test_check_passes_on_verbatim_memo(self):
        root = self.tmp_dir()
        memo = root / "memo.md"
        memo.write_text(OperatorPolicyTests.MEMO_TEXT, encoding="utf-8")
        self.patch("OPERATOR_POLICY_PATH", str(memo))
        freeze.check_operator_policy(str(memo))

    def test_missing_memo_fails_state(self):
        self.patch("OPERATOR_POLICY_PATH", str(self.tmp_dir() / "absent.md"))
        with self.assertRaises(freeze.Failure) as ctx:
            freeze.check_operator_policy(freeze.OPERATOR_POLICY_PATH)
        self.assertEqual(freeze.EXIT_STATE, ctx.exception.code)

    def test_wrong_digest_fails_state(self):
        root = self.tmp_dir()
        memo = root / "memo.md"
        memo.write_text(OperatorPolicyTests.MEMO_TEXT + "\ndrift\n", encoding="utf-8")
        self.patch("OPERATOR_POLICY_PATH", str(memo))
        with self.assertRaises(freeze.Failure) as ctx:
            freeze.check_operator_policy(str(memo))
        self.assertIn("does not match the recorded", ctx.exception.message)

    def test_removed_budget_b_wording_fails_state(self):
        root = self.tmp_dir()
        text = OperatorPolicyTests.MEMO_TEXT.replace("0.0390625 game units", "REDACTED")
        memo = root / "memo.md"
        memo.write_text(text, encoding="utf-8")
        self.patch("OPERATOR_POLICY_PATH", str(memo))
        # The altered memo is pinned by its own digest so the snippet check is what fires.
        self.patch("OPERATOR_POLICY_SHA256",
                   hashlib.sha256(text.encode("utf-8")).hexdigest())
        with self.assertRaises(freeze.Failure) as ctx:
            freeze.check_operator_policy(str(memo))
        self.assertIn("no longer encodes", ctx.exception.message)
        self.assertIn("0.0390625 game units", ctx.exception.message)


class FreezePipelineTests(FreezeTestBase):
    """End-to-end orchestrator runs with the stand-in worker (real subprocesses)."""

    def run_freeze(self, cache):
        return self.run_main("--freeze", str(cache))

    def test_full_freeze_end_to_end(self):
        cache, root = self.setup_ready_cache()
        self.patch_spawn_guards()
        self.patch_worker(write_standin_worker(root))
        code, output = self.run_freeze(cache)
        self.assertEqual(freeze.EXIT_OK, code, output)
        stages = self.freeze_stage(cache)
        self.assertEqual(1, len(stages))
        stage = stages[0]
        self.assertTrue((stage / freeze.COMPLETED_NAME).exists())
        self.assertTrue((stage / freeze.STARTED_NAME).exists())
        self.assertFalse((stage / freeze.LOCK_FILE_REL).exists())
        for relative in (freeze.STATE_FILE_REL, freeze.REQUESTS_DIR_REL, freeze.RAW_DIR_REL,
                         freeze.DIAG_DIR_REL, freeze.FINAL_DIR_REL, freeze.RECEIPT_NAME):
            self.assertTrue((stage / relative).exists(), relative)
        raw_files = sorted((stage / freeze.RAW_DIR_REL).iterdir())
        self.assertEqual(6, len(raw_files))
        diagnostics = sorted((stage / freeze.DIAG_DIR_REL).iterdir())
        self.assertTrue(diagnostics)
        state = json.loads((stage / freeze.STATE_FILE_REL).read_text(encoding="utf-8"))
        self.assertEqual(state["version"], freeze.STATE_VERSION)
        self.assertEqual(6, len(state["worker_runs"]))
        for name, note in state["worker_runs"].items():
            self.assertEqual(1, note["attempt"])
            self.assertIsNone(note["problem"])
            self.assertGreaterEqual(note["wall_seconds"], 0.0)
            self.assertIn("rss_children_maxrss_after_kb", note)
        manifest_path = stage / freeze.FINAL_DIR_REL / "frozen-manifest.json"
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        self.assertEqual("final all-tiles selection freeze", manifest["scope"])
        self.assertEqual(5, len(manifest["selection"]["primary_ids"]))
        self.assertGreaterEqual(len(manifest["selection"]["sectors_covered"]), 3)
        self.assertEqual(2, len(manifest["selection"]["alternate_ids"]))
        self.assertIn("no numeric residual computed here", manifest["note"])
        selection = manifest["selection"]
        candidates_text = (stage / freeze.FINAL_DIR_REL / "candidates.tsv").read_text(
            encoding="utf-8")
        self.assertIn("r301c8", candidates_text)     # hole retained in the union table
        self.assertIn("r300c8", candidates_text)     # seam-excluded cell retained
        self.assertEqual(len(candidates_text.rstrip("\n").split("\n")) - 1,
                         state["catalog_cells"])
        # Per-tile provenance: each raw file hash recorded in state matches disk.
        for name in TILE_ORDER:
            record = state["tiles"][name]
            self.assertEqual("measured", record["status"])
            digest = hashlib.sha256(
                (Path(record["raw_output"])).read_bytes()).hexdigest()
            self.assertEqual(record["raw_output_sha256"], digest)

    def test_double_fresh_run_tables_byte_identical_and_manifest_not(self):
        digests = []
        table_bytes = []
        for _run in range(2):
            cache, root = self.setup_ready_cache()
            self.patch_spawn_guards()
            self.patch_worker(write_standin_worker(root))
            code, output = self.run_freeze(cache)
            self.assertEqual(freeze.EXIT_OK, code, output)
            stage = self.freeze_stage(cache)[0]
            state = json.loads((stage / freeze.STATE_FILE_REL).read_text(encoding="utf-8"))
            digests.append(state["canonical_digest"])
            table_bytes.append((
                Path(state["artifacts"]["candidates_tsv"]["path"]).read_bytes(),
                Path(state["artifacts"]["selection_tsv"]["path"]).read_bytes()))
        self.assertEqual(table_bytes[0][0], table_bytes[1][0])
        self.assertEqual(table_bytes[0][1], table_bytes[1][1])
        self.assertEqual(digests[0], digests[1])

    def test_idempotent_rerun_reverifies_without_workers_or_writes(self):
        cache, root = self.setup_ready_cache()
        self.patch_spawn_guards()
        worker_script = write_standin_worker(root)
        self.patch_worker(worker_script)
        code, output = self.run_freeze(cache)
        self.assertEqual(freeze.EXIT_OK, code, output)
        stage = self.freeze_stage(cache)[0]
        artifact_paths = [stage / freeze.FINAL_DIR_REL / "candidates.tsv",
                          stage / freeze.FINAL_DIR_REL / "selection.tsv",
                          stage / freeze.FINAL_DIR_REL / "frozen-manifest.json",
                          stage / freeze.RECEIPT_NAME]
        mtimes = {str(path): os.stat(path).st_mtime_ns for path in artifact_paths}
        state = json.loads((stage / freeze.STATE_FILE_REL).read_text(encoding="utf-8"))
        receipt_digest = hashlib.sha256(
            (stage / freeze.RECEIPT_NAME).read_bytes()).hexdigest()
        self.assertEqual(receipt_digest, state["artifacts"]["receipt"]["sha256"])
        self.assertEqual((stage / freeze.RECEIPT_NAME).stat().st_size,
                         state["artifacts"]["receipt"]["size"])
        stage_entries_before = sorted(str(path) for path in stage.rglob("*"))
        spawns = []

        def failing_command(*_args):
            spawns.append(_args)
            raise AssertionError("worker spawned during idempotent reverification")

        self.patch("worker_command", failing_command)
        code, output = self.run_freeze(cache)
        self.assertEqual(freeze.EXIT_OK, code, output)
        self.assertEqual([], spawns)
        stage_entries_after = sorted(str(path) for path in stage.rglob("*"))
        self.assertEqual(stage_entries_before, stage_entries_after)
        for path, mtime in mtimes.items():
            self.assertEqual(mtime, os.stat(path).st_mtime_ns, path)
        self.assertIn("idempotent: existing freeze returned", output)
        self.assertIn("canonical digest", output)

    def complete_freeze_stage(self, cache, root):
        self.patch_spawn_guards()
        self.patch_worker(write_standin_worker(root))
        code, output = self.run_main("--freeze", str(cache))
        self.assertEqual(freeze.EXIT_OK, code, output)
        return self.freeze_stage(cache)[0]

    def freeze_root_file_snapshot(self, cache):
        root = Path(freeze.freeze_root(str(cache)))
        return {str(path): (path.stat().st_size, path.stat().st_mtime_ns)
                for path in sorted(root.rglob("*")) if path.is_file()}

    def idempotent_rerun_with_broken_receipt(self, mutate):
        cache, root = self.setup_ready_cache()
        stage = self.complete_freeze_stage(cache, root)
        receipt = stage / freeze.RECEIPT_NAME
        mutate(receipt)
        snapshot = self.freeze_root_file_snapshot(cache)
        spawns = []

        def failing_command(*_args):
            spawns.append(_args)
            raise AssertionError("worker spawned during a failed reverification")

        self.patch("worker_command", failing_command)
        code, output = self.run_main("--freeze", str(cache))
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertEqual([], spawns)
        self.assertEqual(snapshot, self.freeze_root_file_snapshot(cache))
        self.assertFalse(any(path.name.startswith(freeze.STAGE_PREFIX)
                             for path in Path(freeze.freeze_root(str(cache))).iterdir()
                             if path.name != stage.name))
        return output

    def test_missing_receipt_fails_idempotent_rerun(self):
        output = self.idempotent_rerun_with_broken_receipt(lambda receipt: receipt.unlink())
        self.assertIn("completed freeze artifact receipt missing", output)

    def test_mutated_receipt_fails_idempotent_rerun(self):
        def flip_one_byte(receipt):
            data = bytearray(receipt.read_bytes())
            data[-1] ^= 0xFF
            receipt.write_bytes(bytes(data))

        output = self.idempotent_rerun_with_broken_receipt(flip_one_byte)
        self.assertIn("completed freeze artifact receipt sha256 drifted", output)

    def test_truncated_receipt_fails_idempotent_rerun(self):
        output = self.idempotent_rerun_with_broken_receipt(
            lambda receipt: receipt.write_bytes(b""))
        self.assertIn("completed freeze artifact receipt size drifted", output)

    def test_producer_freeze_root_coexists(self):
        """The future integrity producer creates <cache>/freeze/ itself and writes
        the manifest plus its own markers: the freeze consumer must not mistake
        either for an operator collision, on a fresh run or an idempotent rerun."""
        cache, root = self.setup_ready_cache()
        producer_root = Path(freeze.freeze_root(str(cache)))
        (producer_root / "PRODUCER-MARKER").write_text("", encoding="utf-8")
        (producer_root / "producer-work").mkdir(parents=True, exist_ok=True)
        (producer_root / "producer-work" / "state.json").write_text("{}", encoding="utf-8")
        stage = self.complete_freeze_stage(cache, root)
        self.assertTrue((stage / freeze.COMPLETED_NAME).exists())
        spawns = []

        def failing_command(*_args):
            spawns.append(_args)
            raise AssertionError("worker spawned during the idempotent rerun")

        self.patch("worker_command", failing_command)
        code, output = self.run_main("--freeze", str(cache))
        self.assertEqual(freeze.EXIT_OK, code, output)
        self.assertEqual([], spawns)
        self.assertIn("idempotent: existing freeze returned", output)

    def test_ambiguous_completed_freezes_fail_state(self):
        cache, root = self.setup_ready_cache()
        self.patch_spawn_guards()
        self.patch_worker(write_standin_worker(root))
        code, _output = self.run_freeze(cache)
        self.assertEqual(freeze.EXIT_OK, code)
        stale = Path(freeze.freeze_root(str(cache))) / (
            freeze.STAGE_PREFIX + "1999-01-01T00.00.00.000000+02.00")
        (stale / freeze.STATE_FILE_REL).parent.mkdir(parents=True, exist_ok=False)
        (stale / freeze.STATE_FILE_REL).write_text(json.dumps({
            "version": freeze.STATE_VERSION, "mode": "freeze",
            "canonical_digest": "0" * 64,
            "tiles": {name: {"sha256": "0" * 64} for name in TILE_ORDER},
            "artifacts": {
                "candidates_tsv": {"path": str(stale / "c.tsv"), "sha256": "0" * 64},
                "selection_tsv": {"path": str(stale / "s.tsv"), "sha256": "0" * 64},
                "frozen_manifest": {"path": str(stale / "m.json"), "sha256": "0" * 64},
            },
        }), encoding="utf-8")
        (stale / freeze.COMPLETED_NAME).write_text("", encoding="utf-8")
        code, output = self.run_freeze(cache)
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertIn("multiple completed freezes", output)

    def test_worker_protocol_drift_fails_and_preserves_diagnostics(self):
        cache, root = self.setup_ready_cache()
        self.patch_spawn_guards()
        worker_script = write_standin_worker(root)
        text = worker_script.read_text(encoding="utf-8")
        text = text.replace('"lattice_origin_l93": [anchor_x, anchor_y],',
                            '"lattice_origin_l93": [anchor_x + 1.0, anchor_y],')
        drifted = root / "drifted-worker.py"
        drifted.write_text(text, encoding="utf-8")
        self.patch_worker(drifted)
        code, output = self.run_freeze(cache)
        self.assertEqual(freeze.EXIT_WORKER, code)
        self.assertIn("does not match the request", output)
        stage = self.freeze_stage(cache)[0]
        self.assertFalse((stage / freeze.COMPLETED_NAME).exists())
        self.assertFalse((stage / freeze.LOCK_FILE_REL).exists())
        self.assertTrue((stage / freeze.STARTED_NAME).exists())
        self.assertTrue((stage / freeze.DIAG_DIR_REL / (
            f"{TILE_ORDER[0]}.worker-stdout.txt")).exists())
        self.assertTrue((stage / freeze.RAW_DIR_REL / (
            f"{TILE_ORDER[0]}.measurements.json")).exists())

    def test_memory_guard_trip_stops_with_report_and_no_retry(self):
        cache, root = self.setup_ready_cache()
        self.patch_spawn_guards()
        script = root / "memory-refusing-worker.py"
        script.write_text(
            "import sys\n"
            "sys.stderr.write('candidate pre-allocation guard (live-set plan) refused the tile: "
            "planned peak 1073754112 bytes exceeds the budget 1073741824 bytes; refusing to plan "
            "or allocate\\n')\n"
            "sys.exit(3)\n", encoding="utf-8")
        self.patch_worker(script)
        code, output = self.run_freeze(cache)
        self.assertEqual(freeze.EXIT_WORKER, code)
        self.assertIn(f"MEMORY-GUARD TRIP on tile {TILE_ORDER[0]}", output)
        self.assertIn("planned peak", output)
        self.assertIn("no budget raise, no fallback, no second pass, no method change", output)
        stage = self.freeze_stage(cache)[0]
        preserved = (stage / freeze.DIAG_DIR_REL / (
            f"{TILE_ORDER[0]}.worker-stderr.txt")).read_text(encoding="utf-8")
        self.assertIn("live-set plan", preserved)
        # Only the first tile is ever attempted: one request, no second pass.
        self.assertEqual(1, len(list((stage / freeze.REQUESTS_DIR_REL).iterdir())))
        self.assertTrue((stage / freeze.STARTED_NAME).exists())

    def test_worker_timeout_preserves_stage(self):
        cache, root = self.setup_ready_cache()
        self.patch_spawn_guards()
        script = root / "stalling-worker.py"
        script.write_text("import time; time.sleep(60)\n", encoding="utf-8")
        self.patch("WORKER_TIMEOUT_SECONDS", 1.0)
        self.patch_worker(script)
        started = time.monotonic()
        code, output = self.run_freeze(cache)
        self.assertLess(time.monotonic() - started, 30.0)
        self.assertEqual(freeze.EXIT_WORKER, code)
        self.assertIn("timeout", output)
        stage = self.freeze_stage(cache)[0]
        self.assertFalse((stage / freeze.COMPLETED_NAME).exists())
        self.assertTrue((stage / freeze.STARTED_NAME).exists())

    def test_select_floor_stop_after_workers_preserves_scratch(self):
        cache, root = self.setup_ready_cache()
        self.patch_spawn_guards()
        script = root / "ineligible-worker.py"
        spec = {name: [dict(cell, sigma=0.5) for cell in cells]
                for name, cells in STANDIN_CELLS.items()}
        script.write_text(STANDIN_WORKER_TEMPLATE.format(cells=json.dumps(spec),
                                                         prov=json.dumps({})),
                          encoding="utf-8")
        self.patch_worker(script)
        code, output = self.run_freeze(cache)
        self.assertEqual(freeze.EXIT_GUARD, code)
        self.assertIn("final selection STOP: eligible_below_floor", output)
        stage = self.freeze_stage(cache)[0]
        self.assertTrue((stage / freeze.STARTED_NAME).exists())
        self.assertFalse((stage / freeze.COMPLETED_NAME).exists())
        self.assertEqual(6, len(list((stage / freeze.RAW_DIR_REL).iterdir())))

    def test_receipt_content_and_hash_chain(self):
        cache, root = self.setup_ready_cache()
        self.patch_spawn_guards()
        self.patch_worker(write_standin_worker(root))
        code, output = self.run_freeze(cache)
        self.assertEqual(freeze.EXIT_OK, code, output)
        stage = self.freeze_stage(cache)[0]
        receipt = json.loads((stage / freeze.RECEIPT_NAME).read_text(encoding="utf-8"))
        self.assertEqual(1, receipt["receipt_version"])
        self.assertEqual("1.2", receipt["protocol_version"])
        self.assertEqual(str(stage), receipt["freeze_dir"])
        self.assertEqual([639841.997, 5506768.179], receipt["lattice_anchor_32631"])
        self.assertEqual(freeze.OPERATOR_POLICY_PATH, receipt["operator_policy"]["path"])
        self.assertEqual(freeze.OPERATOR_POLICY_SHA256, receipt["operator_policy"]["sha256"])
        budget_a = receipt["budget_a"]
        self.assertEqual(1.0, budget_a["limit_m"])
        self.assertIn("engineering-only", budget_a["scope"])
        self.assertIn("<= 1.0 m", budget_a["metric"])
        self.assertIn("no pruning", budget_a["residuals"])
        self.assertIn("no failure-based alternate activation", budget_a["residuals"])
        self.assertEqual(1.0, budget_a["future_manifest_encoding"]["tolerances.verticalMeters"])
        self.assertIn("absent", budget_a["future_manifest_encoding"]["source.verticalAccuracyMeters"])
        self.assertIn("escalates before any DEM comparison", budget_a["escalation"])
        budget_b = receipt["budget_b"]
        self.assertEqual(0.0390625, budget_b["limit_game_units"])
        self.assertIn("|E - (P - C)|", budget_b["metric"])
        self.assertIn("restoredEngineMeters = Z0 + (E + C) * metersPerGameUnit", budget_b["restore"])
        self.assertIn("ACTUAL header verticalOriginMeters", budget_b["z0"])
        self.assertIn("pending at freeze time", budget_b["z0"])
        carried = " ".join(receipt["carried_unresolved"])
        for fragment in ("UNKNOWN", "auto-classification bias", "epoch mismatch"):
            self.assertIn(fragment, carried)
        chain = receipt["provenance_hash_chain"]
        self.assertEqual(6, len(chain["tiles"]))
        self.assertEqual(6, len(chain["raw_outputs"]))
        self.assertEqual(freeze.APPROVED_WORKER_SHA256S[0], chain["worker_sha256"])
        state = json.loads((stage / freeze.STATE_FILE_REL).read_text(encoding="utf-8"))
        for name in TILE_ORDER:
            self.assertEqual(state["tiles"][name]["sha256"], chain["tiles"][name])
            self.assertEqual(state["tiles"][name]["raw_output_sha256"], chain["raw_outputs"][name])
        self.assertEqual(state["artifacts"]["candidates_tsv"]["sha256"],
                         chain["candidates_sha256"])
        self.assertEqual(state["artifacts"]["selection_tsv"]["sha256"], chain["selection_sha256"])
        self.assertEqual(state["canonical_digest"], chain["canonical_digest"])
        self.assertEqual(freeze.SELECT_MODULE_SHA256, chain["select_module_sha256"])
        self.assertEqual(freeze.EXPECTED_BASE_WORKER_SHA256, chain["base_worker_sha256"])
        self.assertEqual(freeze.EXPECTED_BASE_WORKER_SHA256, state["worker"]["base_worker_sha256"])
        self.assertEqual(freeze.EXPECTED_BASE_WORKER_SHA256, receipt["worker"]["base_worker_sha256"])
        self.assertEqual(freeze.SELECT_MODULE_SHA256, state["worker"]["select_module_sha256"])
        self.assertEqual(freeze.SELECT_MODULE_SHA256, receipt["worker"]["select_module_sha256"])
        self.assertFalse(receipt["numeric_residual_computed_here"])
        self.assertFalse(receipt["numeric_proof_pass"])
        self.assertFalse(receipt["provider_02_framework"])
        self.assertTrue(receipt["stage_1_preliminary_can_never_be_frozen"])
        # B's manifest schema stays untouched: no policy fields, only the frozen keys.
        manifest = json.loads((stage / freeze.FINAL_DIR_REL / "frozen-manifest.json")
                              .read_text(encoding="utf-8"))
        self.assertEqual(
            {"protocol_version", "scope", "constants", "map_transform", "tables",
             "python_version", "generated_at", "selection", "note"},
            set(manifest))
        # Receipt digest logged with an ISO-local timestamp line.
        receipt_digest = hashlib.sha256(
            (stage / freeze.RECEIPT_NAME).read_bytes()).hexdigest()
        self.assertIn(f"policy sidecar written: {stage / freeze.RECEIPT_NAME} "
                      f"(sha256 {receipt_digest})", output)

    def test_worker_override_verified_spawned_and_recorded(self):
        cache, root = self.setup_ready_cache()
        real_worker = Path(freeze.worker_script_path())
        worker_sha = hashlib.sha256(real_worker.read_bytes()).hexdigest()
        base = root / "geo-base"
        (base / "bounded").mkdir(parents=True)
        (base / "bounded" / "bounded-worker.py").write_bytes(real_worker.read_bytes())
        self.patch("worker_base_dir", lambda: str(base))
        manifest_path = Path(freeze.integrity_manifest_path(str(cache)))
        entries = json.loads(manifest_path.read_text(encoding="utf-8"))
        entries["worker"] = {"rel_path": "bounded/bounded-worker.py", "sha256": worker_sha}
        manifest_path.write_text(json.dumps(entries, indent=2, sort_keys=True) + "\n",
                                 encoding="utf-8")
        self.patch_spawn_guards()
        standin = write_standin_worker(root)
        spawned_scripts = []
        captured_requests = []

        def capturing_command(venv_python, worker_script, request_path):
            spawned_scripts.append(str(worker_script))
            with open(request_path, "r", encoding="utf-8") as handle:
                captured_requests.append(json.load(handle))
            return [sys.executable, str(standin), request_path]

        self.patch("worker_command", capturing_command)
        code, output = self.run_freeze(cache)
        self.assertEqual(freeze.EXIT_OK, code, output)
        resolved = str(base / "bounded" / "bounded-worker.py")
        self.assertEqual([resolved] * 6, spawned_scripts)
        for request in captured_requests:
            self.assertEqual(worker_sha, request["worker_script_sha256"])
            self.assertEqual(freeze.SELECT_MODULE_SHA256, request["select_module_sha256"])
        self.assertIn(f"tooling ok: worker script {resolved} "
                      f"(sha256 {worker_sha}, manifest override, approved)", output)
        stage = self.freeze_stage(cache)[0]
        state = json.loads((stage / freeze.STATE_FILE_REL).read_text(encoding="utf-8"))
        self.assertEqual(resolved, state["worker"]["path"])
        self.assertEqual(worker_sha, state["worker"]["sha256"])
        self.assertEqual("manifest override", state["worker"]["source"])
        self.assertEqual(freeze.SELECT_MODULE_SHA256, state["worker"]["select_module_sha256"])
        receipt = json.loads((stage / freeze.RECEIPT_NAME).read_text(encoding="utf-8"))
        self.assertEqual(resolved, receipt["worker"]["path"])
        self.assertEqual(worker_sha, receipt["worker"]["sha256"])
        self.assertEqual(worker_sha, receipt["provenance_hash_chain"]["worker_sha256"])
        self.assertEqual(freeze.SELECT_MODULE_SHA256,
                         receipt["provenance_hash_chain"]["select_module_sha256"])
        self.assertEqual(freeze.SELECT_MODULE_SHA256, receipt["worker"]["select_module_sha256"])
        self.assertIn(freeze.APPROVED_WORKER_SHA256S[0], receipt["worker"]["approved_registry"])

    def test_banded_worker_override_end_to_end_with_provenance(self):
        """The accepted banded entrypoint (registered by hash) resolves approved
        through a manifest override and is the script the spawn seam receives,
        with both the entrypoint sha and the unchanged select-module pin recorded."""
        cache, root = self.setup_ready_cache()
        banded_real = Path(_GEO_DIR / "sedan_lidar_probe_worker_banded.py")
        if not banded_real.is_file():
            self.skipTest("banded worker file not present on disk")
        banded_sha = hashlib.sha256(banded_real.read_bytes()).hexdigest()
        self.assertIn(banded_sha, freeze.APPROVED_WORKER_SHA256S)
        base = root / "geo-base"
        (base / "bounded").mkdir(parents=True)
        (base / "bounded" / "sedan_lidar_probe_worker_banded.py").write_bytes(
            banded_real.read_bytes())
        self.patch("worker_base_dir", lambda: str(base))
        manifest_path = Path(freeze.integrity_manifest_path(str(cache)))
        entries = json.loads(manifest_path.read_text(encoding="utf-8"))
        entries["worker"] = {
            "rel_path": "bounded/sedan_lidar_probe_worker_banded.py",
            "sha256": banded_sha,
        }
        manifest_path.write_text(json.dumps(entries, indent=2, sort_keys=True) + "\n",
                                 encoding="utf-8")
        self.patch_spawn_guards()
        # The spawn seam delegates to the stand-in, but the stand-in output must
        # carry the banded provenance block its entrypoint now requires.
        standin = write_standin_worker(root, {
            "executed_entrypoint_sha256": banded_sha,
            "base_measure_module_sha256": freeze.EXPECTED_BASE_WORKER_SHA256,
            "select_module_sha256": freeze.SELECT_MODULE_SHA256,
        })
        spawned_scripts = []
        captured_requests = []

        def capturing_command(venv_python, worker_script, request_path):
            spawned_scripts.append(str(worker_script))
            with open(request_path, "r", encoding="utf-8") as handle:
                captured_requests.append(json.load(handle))
            return [sys.executable, str(standin), request_path]

        self.patch("worker_command", capturing_command)
        code, output = self.run_freeze(cache)
        self.assertEqual(freeze.EXIT_OK, code, output)
        resolved = str(base / "bounded" / "sedan_lidar_probe_worker_banded.py")
        self.assertEqual([resolved] * 6, spawned_scripts)
        for request in captured_requests:
            self.assertEqual(banded_sha, request["worker_script_sha256"])
            self.assertEqual(freeze.SELECT_MODULE_SHA256, request["select_module_sha256"])
        stage = self.freeze_stage(cache)[0]
        state = json.loads((stage / freeze.STATE_FILE_REL).read_text(encoding="utf-8"))
        self.assertEqual(resolved, state["worker"]["path"])
        self.assertEqual(banded_sha, state["worker"]["sha256"])
        self.assertEqual("manifest override", state["worker"]["source"])
        self.assertEqual(freeze.SELECT_MODULE_SHA256, state["worker"]["select_module_sha256"])
        receipt = json.loads((stage / freeze.RECEIPT_NAME).read_text(encoding="utf-8"))
        self.assertEqual(banded_sha, receipt["worker"]["sha256"])
        self.assertEqual(banded_sha, receipt["provenance_hash_chain"]["worker_sha256"])
        self.assertEqual(freeze.SELECT_MODULE_SHA256,
                         receipt["provenance_hash_chain"]["select_module_sha256"])
        self.assertIn(freeze.APPROVED_WORKER_SHA256S[0], receipt["worker"]["approved_registry"])
        self.assertIn(freeze.APPROVED_WORKER_SHA256S[1], receipt["worker"]["approved_registry"])

    def provenance_output_tests_setup(self, provenance_builder, use_nonbase_entrypoint):
        """Stand-in worker emitting provenance fields built by the builder ("SELF"
        placeholders resolve to the standin's own sha at run time, mirroring the
        banded entrypoint's runtime self-hash); when use_nonbase_entrypoint is
        set, that standin is registered as a non-base approved entrypoint through
        a manifest override (its sha differs from the base pin, so output
        provenance becomes required)."""
        cache, root = self.setup_ready_cache()
        standin = write_standin_worker(root, provenance_builder(None))
        standin_sha = hashlib.sha256(standin.read_bytes()).hexdigest()
        self.patch_spawn_guards()
        if use_nonbase_entrypoint:
            base = root / "geo-base-alt"
            base.mkdir(parents=True)
            (base / "bounded-worker.py").write_bytes(standin.read_bytes())
            self.patch("worker_base_dir", lambda: str(base))
            self.patch("APPROVED_WORKER_SHA256S",
                       (freeze.EXPECTED_BASE_WORKER_SHA256, standin_sha))
            manifest_path = Path(freeze.integrity_manifest_path(str(cache)))
            entries = json.loads(manifest_path.read_text(encoding="utf-8"))
            entries["worker"] = {"rel_path": "bounded-worker.py", "sha256": standin_sha}
            manifest_path.write_text(json.dumps(entries, indent=2, sort_keys=True) + "\n",
                                     encoding="utf-8")
            # The standin is stdlib-only, so the resolved override script is
            # executed directly: the spawn is the real resolved entrypoint.
            self.patch("worker_command",
                       lambda venv_python, worker_script, request_path:
                       [sys.executable, str(worker_script), request_path])
        else:
            self.patch_worker(standin)
        return cache

    def test_provenance_missing_keys_on_nonbase_entrypoint_fails(self):
        cache = self.provenance_output_tests_setup(lambda sha: {}, use_nonbase_entrypoint=True)
        code, output = self.run_freeze(cache)
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertIn("worker output provenance missing required key", output)
        stage = self.freeze_stage(cache)[0]
        self.assertFalse((stage / freeze.COMPLETED_NAME).exists())

    def test_provenance_wrong_base_hash_fails(self):
        def builder(_standin_sha):
            return {
                "executed_entrypoint_sha256": "SELF",
                "base_measure_module_sha256": "0" * 64,
                "select_module_sha256": freeze.SELECT_MODULE_SHA256,
            }

        cache = self.provenance_output_tests_setup(builder, use_nonbase_entrypoint=True)
        code, output = self.run_freeze(cache)
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertIn("worker output provenance key base_measure_module_sha256 contradicts "
                      "the pinned value", output)
        self.assertIn(f"expected {freeze.EXPECTED_BASE_WORKER_SHA256}, actual {'0' * 64}", output)

    def test_provenance_contradictory_executed_entrypoint_fails(self):
        def builder(_standin_sha):
            return {
                "executed_entrypoint_sha256": "1" * 64,
                "base_measure_module_sha256": freeze.EXPECTED_BASE_WORKER_SHA256,
                "select_module_sha256": freeze.SELECT_MODULE_SHA256,
            }

        cache = self.provenance_output_tests_setup(builder, use_nonbase_entrypoint=True)
        code, output = self.run_freeze(cache)
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertIn("worker output provenance key executed_entrypoint_sha256 contradicts "
                      "the pinned value", output)
        self.assertIn(f"actual {'1' * 64}", output)

    def test_provenance_nonstring_value_fails(self):
        def builder(_standin_sha):
            return {
                "executed_entrypoint_sha256": "SELF",
                "base_measure_module_sha256": freeze.EXPECTED_BASE_WORKER_SHA256,
                "select_module_sha256": 12345,
            }

        cache = self.provenance_output_tests_setup(builder, use_nonbase_entrypoint=True)
        code, output = self.run_freeze(cache)
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertIn("worker output provenance key select_module_sha256 is not a sha256 "
                      "string", output)

    def test_legacy_output_without_provenance_accepted(self):
        cache = self.provenance_output_tests_setup(lambda sha: {}, use_nonbase_entrypoint=False)
        code, output = self.run_freeze(cache)
        self.assertEqual(freeze.EXIT_OK, code, output)
        stage = self.freeze_stage(cache)[0]
        state = json.loads((stage / freeze.STATE_FILE_REL).read_text(encoding="utf-8"))
        # The verified base identity is still recorded even when the legacy
        # output omits the additive keys: it equals the disk-checked base pin.
        self.assertEqual(freeze.EXPECTED_BASE_WORKER_SHA256,
                         state["worker"]["base_measure_module_sha256"])

    def test_legacy_output_with_consistent_provenance_accepted(self):
        def builder(_standin_sha):
            # The legacy resolved entrypoint IS the base worker: the consistent
            # executed value is the base pin, not the standin's own sha.
            return {
                "executed_entrypoint_sha256": freeze.EXPECTED_BASE_WORKER_SHA256,
                "base_measure_module_sha256": freeze.EXPECTED_BASE_WORKER_SHA256,
                "select_module_sha256": freeze.SELECT_MODULE_SHA256,
            }

        cache = self.provenance_output_tests_setup(builder, use_nonbase_entrypoint=False)
        code, output = self.run_freeze(cache)
        self.assertEqual(freeze.EXIT_OK, code, output)
        stage = self.freeze_stage(cache)[0]
        state = json.loads((stage / freeze.STATE_FILE_REL).read_text(encoding="utf-8"))
        self.assertEqual(freeze.EXPECTED_BASE_WORKER_SHA256,
                         state["worker_runs"][FIRST_TILE_NAME]["base_measure_module_sha256"])
        self.assertEqual(freeze.EXPECTED_BASE_WORKER_SHA256,
                         state["worker"]["base_measure_module_sha256"])

    def test_legacy_output_with_inconsistent_provenance_fails(self):
        def builder(_standin_sha):
            return {
                "executed_entrypoint_sha256": "1" * 64,
                "base_measure_module_sha256": freeze.EXPECTED_BASE_WORKER_SHA256,
                "select_module_sha256": freeze.SELECT_MODULE_SHA256,
            }

        cache = self.provenance_output_tests_setup(builder, use_nonbase_entrypoint=False)
        code, output = self.run_freeze(cache)
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertIn("worker output provenance key executed_entrypoint_sha256 contradicts "
                      "the pinned value", output)
        self.assertIn(f"expected {freeze.EXPECTED_BASE_WORKER_SHA256}, actual {'1' * 64}", output)

    def test_base_worker_disk_identity_drift_fails_before_spawn(self):
        cache, root = self.setup_ready_cache()
        self.patch("EXPECTED_BASE_WORKER_SHA256", "0" * 64)
        self.patch_spawn_guards()
        self.patch_worker(write_standin_worker(root))
        spawns = []

        def failing_command(*_args):
            spawns.append(_args)
            raise AssertionError("worker spawned despite base identity drift")

        self.patch("worker_command", failing_command)
        code, output = self.run_main("--freeze", str(cache))
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertEqual([], spawns)
        real_sha = hashlib.sha256(
            Path(freeze.base_worker_file_path()).read_bytes()).hexdigest()
        self.assertIn(f"base measurement worker sha256 drift: {real_sha}", output)
        self.assertIn("0" * 64, output)

    def test_idempotent_rerun_modified_recorded_base_identity_fails(self):
        cache, root = self.setup_ready_cache()
        stage = self.complete_freeze_stage(cache, root)
        state_path = stage / freeze.STATE_FILE_REL
        state = json.loads(state_path.read_text(encoding="utf-8"))
        state["worker"]["base_worker_sha256"] = "0" * 64
        state_path.write_text(json.dumps(state, indent=2, sort_keys=True) + "\n",
                              encoding="utf-8")
        snapshot = self.freeze_root_file_snapshot(cache)
        spawns = []

        def failing_command(*_args):
            spawns.append(_args)
            raise AssertionError("worker spawned during a failed reverification")

        self.patch("worker_command", failing_command)
        code, output = self.run_main("--freeze", str(cache))
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertEqual([], spawns)
        self.assertEqual(snapshot, self.freeze_root_file_snapshot(cache))
        self.assertIn("completed freeze recorded base worker identity drifted", output)
        self.assertIn("0" * 64, output)

    def test_missing_probe_report_fails_state_before_workers(self):
        cache, root = self.setup_ready_cache()
        Path(freeze.probe_report_path(str(cache))).unlink()
        self.patch_spawn_guards()
        spawns = []

        def failing_command(*_args):
            spawns.append(_args)
            raise AssertionError("worker spawned without the executed cs2cs record")

        self.patch("worker_command", failing_command)
        code, output = self.run_freeze(cache)
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertEqual([], spawns)
        self.assertIn("probe state report missing", output)

    def test_probe_report_geometry_drift_fails_state(self):
        cache, root = self.setup_ready_cache()
        report = make_probe_report()
        report["projection_v1_2"]["lattice_anchor_32631"] = [639842.0, 5506768.179]
        path = Path(freeze.probe_report_path(str(cache)))
        path.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        self.patch_spawn_guards()
        code, output = self.run_freeze(cache)
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertIn("does not equal the pinned SW origin", output)

    def test_request_carries_full_enumeration_and_frozen_budget(self):
        cache, root = self.setup_ready_cache()
        self.patch_spawn_guards()
        standin = write_standin_worker(root)
        seen = {}

        def capturing_command(venv_python, worker_script, request_path):
            with open(request_path, "r", encoding="utf-8") as handle:
                seen[request_path] = json.load(handle)
            return [sys.executable, str(standin), request_path]

        self.patch("worker_command", capturing_command)
        code, output = self.run_freeze(cache)
        self.assertEqual(freeze.EXIT_OK, code, output)
        self.assertEqual(6, len(seen))
        for request_path, request in seen.items():
            self.assertEqual(361, request["candidates"]["lattice_cols"])
            self.assertEqual(361, request["candidates"]["lattice_rows"])
            self.assertEqual(freeze.WORKER_MEMORY_BUDGET_BYTES, request["memory_budget_bytes"])
            self.assertEqual(freeze.WORKER_CHUNK_POINTS, request["chunk_size_points"])
            self.assertEqual(sedan_lidar_select.LATTICE, request["candidates"]["spacing_m"])
            self.assertEqual(sedan_lidar_select.WINDOW_R, request["candidates"]["window_r"])
            self.assertEqual(sedan_lidar_select.E23_DIST, request["candidates"]["e23_radius"])
            self.assertEqual(ANCHOR_L93, request["candidates"]["lattice_origin_l93"])
            self.assertEqual(ANCHOR_L93, request["candidates"]["aoi_sw_l93"])
            self.assertEqual(AOI_BBOX_L93, request["aoi_bbox_l93"])
            self.assertEqual(FILTER_BBOX_L93, request["filter_bbox_l93"])
            self.assertEqual(freeze.APPROVED_WORKER_SHA256S[0],
                             request["worker_script_sha256"])
            self.assertEqual(freeze.SELECT_MODULE_SHA256, request["select_module_sha256"])
            self.assertEqual(freeze.EXPECTED_BASE_WORKER_SHA256,
                             request["base_worker_sha256"])
            self.assertEqual(1 << 30, request["memory_budget_bytes"])
            del request["candidates"]["footprint_l93"]
            self.assertTrue(os.path.isfile(request["venv_python"]))


class CompletedReverifyRelPathTests(FreezeTestBase):
    """Idempotent reverification resolves tile paths through the manifest rel_path."""

    STAGE2 = {name: f"lidarhd/stage2/{LOT}/{name}" for name in TILE_ORDER[1:]}

    def completed_cache(self, rel_overrides=None):
        cache, root = self.setup_ready_cache(rel_overrides=rel_overrides)
        self.patch_spawn_guards()
        self.patch_worker(write_standin_worker(root))
        code, output = self.run_main("--freeze", str(cache))
        self.assertEqual(freeze.EXIT_OK, code, output)
        return cache

    def rerun_without_workers(self, cache):
        spawns = []

        def failing_command(*_args):
            spawns.append(_args)
            raise AssertionError("worker spawned during idempotent reverification")

        self.patch("worker_command", failing_command)
        code, output = self.run_main("--freeze", str(cache))
        self.assertEqual([], spawns)
        return code, output

    def set_manifest_rel_path(self, cache, name, rel_path):
        path = Path(freeze.integrity_manifest_path(str(cache)))
        entries = json.loads(path.read_text(encoding="utf-8"))
        entries[name]["rel_path"] = rel_path
        path.write_text(json.dumps(entries, indent=2, sort_keys=True) + "\n", encoding="utf-8")

    def test_stage2_manifest_rel_paths_reverify_without_workers(self):
        cache = self.completed_cache(rel_overrides=self.STAGE2)
        for name in TILE_ORDER[1:]:
            self.assertFalse((Path(cache) / "lidarhd" / LOT / name).exists())
        code, output = self.rerun_without_workers(cache)
        self.assertEqual(freeze.EXIT_OK, code, output)
        self.assertIn("idempotent: existing freeze returned", output)

    def test_absent_manifest_falls_back_to_pinned_paths(self):
        cache = self.completed_cache()
        Path(freeze.integrity_manifest_path(str(cache))).unlink()
        code, output = self.rerun_without_workers(cache)
        self.assertEqual(freeze.EXIT_OK, code, output)
        self.assertIn("idempotent: existing freeze returned", output)

    def test_null_rel_path_falls_back_to_pinned_path(self):
        cache = self.completed_cache()
        self.set_manifest_rel_path(cache, TILE_ORDER[3], None)
        code, output = self.rerun_without_workers(cache)
        self.assertEqual(freeze.EXIT_OK, code, output)

    def test_empty_rel_path_fails_state_before_worker(self):
        cache = self.completed_cache()
        self.set_manifest_rel_path(cache, TILE_ORDER[2], "")
        code, output = self.rerun_without_workers(cache)
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertIn("must be a non-empty string", output)

    def test_wrong_tile_name_rel_path_fails_state_before_worker(self):
        cache = self.completed_cache(rel_overrides=self.STAGE2)
        self.set_manifest_rel_path(cache, TILE_ORDER[5], f"lidarhd/stage2/{LOT}/{TILE_ORDER[1]}")
        code, output = self.rerun_without_workers(cache)
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertIn("does not end with the pinned tile name", output)

    def test_dotdot_rel_path_fails_state_before_worker(self):
        cache = self.completed_cache(rel_overrides=self.STAGE2)
        victim = TILE_ORDER[2]
        self.set_manifest_rel_path(cache, victim, f"lidarhd/stage2/../{LOT}/{victim}")
        code, output = self.rerun_without_workers(cache)
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertIn("has an empty, '.', or '..' component", output)

    def test_absolute_rel_path_fails_state_before_worker(self):
        cache = self.completed_cache(rel_overrides=self.STAGE2)
        victim = TILE_ORDER[4]
        self.set_manifest_rel_path(cache, victim, f"/tmp/opencode/elsewhere/{LOT}/{victim}")
        code, output = self.rerun_without_workers(cache)
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertIn("is not a relative posix path", output)


class RecoverLockTests(FreezeTestBase):
    def stage_with_lock(self, cache, pid, stamp="1999-01-01T00.00.00.000000+02.00"):
        stage = Path(freeze.freeze_root(str(cache))) / (freeze.STAGE_PREFIX + stamp)
        (stage / "state").mkdir(parents=True, exist_ok=False)
        (stage / freeze.STARTED_NAME).write_text("", encoding="utf-8")
        (stage / freeze.LOCK_FILE_REL).write_text(f"{pid} 1999-01-01T00:00:00.000000+02:00\n",
                                                  encoding="utf-8")
        return stage

    def test_recover_removes_stale_lock(self):
        cache, _root = self.setup_ready_cache()
        stage = self.stage_with_lock(cache, pid=999999)
        code, output = self.run_main("--recover-lock", str(cache))
        self.assertEqual(freeze.EXIT_OK, code)
        self.assertFalse((stage / freeze.LOCK_FILE_REL).exists())
        self.assertIn("stale lock removed", output)

    def test_recover_refuses_live_pid(self):
        cache, _root = self.setup_ready_cache()
        stage = self.stage_with_lock(cache, pid=os.getpid())
        code, output = self.run_main("--recover-lock", str(cache))
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertTrue((stage / freeze.LOCK_FILE_REL).exists())
        self.assertIn("held by live pid", output)

    def test_recover_without_stage(self):
        cache, _root = self.setup_ready_cache()
        code, output = self.run_main("--recover-lock", str(cache))
        self.assertEqual(freeze.EXIT_OK, code)
        self.assertIn("nothing to recover", output)

    def test_multiple_incomplete_stages_fail_state(self):
        cache, _root = self.setup_ready_cache()
        self.stage_with_lock(cache, pid=999998, stamp="1999-01-01T00.00.00.000001+02.00")
        self.stage_with_lock(cache, pid=999997, stamp="1999-01-01T00.00.00.000002+02.00")
        code, output = self.run_main("--recover-lock", str(cache))
        self.assertEqual(freeze.EXIT_STATE, code)
        self.assertIn("multiple incomplete freezes", output)

    def test_completed_stage_is_not_a_recover_target(self):
        cache, _root = self.setup_ready_cache()
        stage = self.stage_with_lock(cache, pid=999996)
        (stage / freeze.COMPLETED_NAME).write_text("", encoding="utf-8")
        code, output = self.run_main("--recover-lock", str(cache))
        self.assertEqual(freeze.EXIT_OK, code)
        self.assertIn("nothing to recover", output)
        self.assertTrue((stage / freeze.LOCK_FILE_REL).exists())


class ModeAndContractTests(FreezeTestBase):
    def test_modes_are_mutually_exclusive(self):
        with self.assertRaises(SystemExit):
            freeze.main(["--freeze", "--recover-lock", "/tmp/opencode"])

    def test_module_never_imports_acquire_probe_or_worker(self):
        source = (_GEO_DIR / "sedan_lidar_freeze.py").read_text(encoding="utf-8")
        self.assertNotIn("import sedan_acquire", source)
        self.assertNotIn('spec_from_file_location("sedan_acquire"', source)
        self.assertNotIn("import sedan_lidar_probe_worker", source)
        self.assertNotIn('spec_from_file_location("sedan_lidar_probe_worker"', source)
        self.assertNotIn("import sedan_lidar_probe\n", source)
        self.assertNotIn('spec_from_file_location("sedan_lidar_probe"', source)
        self.assertIn("import sedan_lidar_select as groundtruth", source)
        for symbol in ("select", "freeze", "canonical_digest", "fired_rules",
                       "classify_stratum", "sector_of", "lattice_centre",
                       "enumerate_lattice_cells", "window_inside_footprint",
                       "seam_border_distance"):
            self.assertIn(f"groundtruth.{symbol}", source)

    def test_epilog_documents_exit_codes_and_readonly_default(self):
        parser = freeze.build_parser()
        self.assertIn("read-only preflight", parser.epilog)
        self.assertIn("Exit codes: 0 success; 2 guard failure", parser.epilog)
        self.assertIn("--recover-lock", parser.epilog)
        self.assertIn("BEFORE any DEM access", parser.description)

    def test_iso_local_log_lines(self):
        cache, root = self.setup_ready_cache()
        self.patch_spawn_guards()
        self.patch_worker(write_standin_worker(root))
        _code, output = self.run_main("--freeze", str(cache))
        stamp_pattern = r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}[+-]\d{2}:\d{2} "
        for line in output.splitlines():
            if not line.strip():
                continue
            self.assertRegex(line, stamp_pattern)

    def test_six_pins_and_total(self):
        pins = freeze.validate_tile_table()
        self.assertEqual(6, len(pins))
        self.assertEqual(FIRST_TILE_NAME, pins[0]["name"])
        self.assertEqual(128_651_625, pins[0]["size"])
        self.assertEqual(119_931_651, pins[1]["size"])
        self.assertEqual(146_099_989, pins[2]["size"])
        self.assertEqual(130_615_694, pins[3]["size"])
        self.assertEqual(153_352_444, pins[4]["size"])
        self.assertEqual(151_880_730, pins[5]["size"])
        self.assertEqual(
            sum(pin["size"] for pin in pins), freeze.TOTAL_PINNED_BYTES)
        for pin in pins:
            self.assertEqual(os.path.join("lidarhd", freeze.LOT, pin["name"]), pin["rel"])

    def test_footprint_validator(self):
        good = {"x_min": 0.0, "y_min": 0.0, "x_max": 1.0, "y_max": 2.0}
        edges, problem = freeze.validate_footprint(good, "unit")
        self.assertIsNone(problem)
        self.assertEqual(1.0, edges["x_max"])
        for bad in ({"x_min": 0.0, "y_min": 0.0, "x_max": 0.0, "y_max": 2.0},
                    {"x_min": 0.0, "y_min": 0.0, "x_max": float("nan"), "y_max": 2.0},
                    {"x_min": 0.0, "y_min": 0.0, "x_max": 1.0},
                    "not a dict"):
            _edges, problem = freeze.validate_footprint(bad, "unit")
            self.assertIsNotNone(problem)


VENV_PYTHON = os.path.join(freeze.DEFAULT_CACHE_ROOT, freeze.VENV_PYTHON_REL)


class RealLidarVenvEndToEndTests(FreezeTestBase):
    """The REAL worker under the isolated lidar venv (already present per the
    probe's plan-tools recipe; these tests never create or install anything)."""

    LAS_PATCHES = {
        FIRST_TILE_NAME: [(150, 150)],
        "LHD_FXX_0839_6958_PTS_LAMB93_IGN69.copc.laz": [(60, 60), (60, 240)],
        "LHD_FXX_0839_6959_PTS_LAMB93_IGN69.copc.laz": [(240, 60), (240, 240)],
        "LHD_FXX_0840_6959_PTS_LAMB93_IGN69.copc.laz": [(100, 150)],
        "LHD_FXX_0841_6958_PTS_LAMB93_IGN69.copc.laz": [(200, 200)],
        "LHD_FXX_0841_6959_PTS_LAMB93_IGN69.copc.laz": [(300, 8), (330, 330)],
    }

    @staticmethod
    def venv_has_laspy():
        try:
            completed = subprocess.run(
                [VENV_PYTHON, "-c", "import laspy, numpy"],
                capture_output=True, text=True, timeout=120, check=False)
        except (OSError, subprocess.TimeoutExpired):
            return False
        return completed.returncode == 0

    @unittest.skipIf(not os.path.isfile(VENV_PYTHON),
                     "isolated lidar venv not present at the probe TOOLS_DIR_REL path "
                     f"({VENV_PYTHON}); tests never create it")
    def setUp(self):
        if not self.venv_has_laspy():
            self.skipTest(f"laspy/numpy unavailable inside {VENV_PYTHON}; "
                          "nothing is installed by tests")

    def write_synthetic_las(self, path, cells):
        """Flat ground patches (z = 150.0, 0.4 m grid) centred on the given
        lattice cell centres; windows of exactly those cells sit inside each
        patch, and the header bbox is the union extent."""
        script = f'''
import laspy, numpy, math, json, sys
cells = json.loads(sys.argv[2])
anchor_x = {ANCHOR_L93[0]!r}
anchor_y = {ANCHOR_L93[1]!r}
offsets = [0.4 * i for i in range(19)]
xs, ys, zs, cls = [], [], [], []
for row, col in cells:
    centre_x = anchor_x + (col + 0.5) * 3.048
    centre_y = anchor_y + (row + 0.5) * 3.048
    for dx in offsets:
        for dy in offsets:
            xs.append(centre_x - 3.6 + dx)
            ys.append(centre_y - 3.6 + dy)
            zs.append(150.0)
            cls.append(2)
las = laspy.create(file_version="1.4", point_format=6)
las.header.scales = [0.001, 0.001, 0.001]
las.header.offsets = [840000.0, 6957000.0, 150.0]
las.x = numpy.asarray(xs, dtype=numpy.float64)
las.y = numpy.asarray(ys, dtype=numpy.float64)
las.z = numpy.asarray(zs, dtype=numpy.float64)
las.classification = numpy.asarray(cls, dtype=numpy.int64)
las.write(sys.argv[1])
'''
        completed = subprocess.run(
            [VENV_PYTHON, "-c", script, str(path), json.dumps([[row, col] for row, col in cells])],
            capture_output=True, text=True, timeout=300, check=False)
        if completed.returncode != 0:
            self.fail(f"synthetic LAS write failed under the venv: {completed.stderr}")

    def cell_footprint(self, row, col, pad=6.0):
        cx = ANCHOR_L93[0] + (col + 0.5) * 3.048
        cy = ANCHOR_L93[1] + (row + 0.5) * 3.048
        return {"x_min": cx - pad, "y_min": cy - pad, "x_max": cx + pad, "y_max": cy + pad}

    def setup_real_cache(self, root):
        cache = Path(root) / "cache"
        sizes = {}
        footprints = {}
        digests = {}
        for name in TILE_ORDER:
            sizes[name] = None  # filled after the LAS write
            footprints[name] = None
        for name in TILE_ORDER:
            path = cache / "lidarhd" / LOT / name
            path.parent.mkdir(parents=True, exist_ok=True)
            cells = self.LAS_PATCHES[name]
            self.write_synthetic_las(path, cells)
            merged = None
            for row, col in cells:
                box = self.cell_footprint(row, col)
                merged = box if merged is None else {
                    "x_min": min(merged["x_min"], box["x_min"]),
                    "y_min": min(merged["y_min"], box["y_min"]),
                    "x_max": max(merged["x_max"], box["x_max"]),
                    "y_max": max(merged["y_max"], box["y_max"]),
                }
            sizes[name] = path.stat().st_size
            digests[name] = freeze.sha256_file(str(path))
            footprints[name] = merged
        self.patch_pins(sizes)
        self.write_manifest(cache, sizes, digests, footprints)
        self.write_probe_report(cache)
        return cache

    def run_real_freeze(self, cache):
        self.patch_spawn_guards()
        # The venv lookup is relocated to the already-present isolated venv
        # (read-only use of the probe's installed tooling); the worker command
        # stays the real [venv_python, worker_script, request] invocation.
        self.patch("venv_python_path", lambda cache_root: VENV_PYTHON)
        # The REAL worker command is used unchanged: [venv_python, worker_script, request].
        return self.run_main("--freeze", str(cache))

    def test_real_worker_end_to_end_freeze(self):
        digests = []
        table_bytes = []
        for _run in range(2):
            root = self.tmp_dir()
            cache = self.setup_real_cache(root)
            code, output = self.run_real_freeze(cache)
            self.assertEqual(freeze.EXIT_OK, code, output)
            stage = self.freeze_stage(cache)[0]
            state = json.loads((stage / freeze.STATE_FILE_REL).read_text(encoding="utf-8"))
            digests.append(state["canonical_digest"])
            table_bytes.append((
                Path(state["artifacts"]["candidates_tsv"]["path"]).read_bytes(),
                Path(state["artifacts"]["selection_tsv"]["path"]).read_bytes()))
            if _run == 0:
                manifest = json.loads(
                    (stage / freeze.FINAL_DIR_REL / "frozen-manifest.json").read_text(
                        encoding="utf-8"))
                self.assertEqual(5, len(manifest["selection"]["primary_ids"]))
                self.assertGreaterEqual(len(manifest["selection"]["sectors_covered"]), 3)
                self.assertEqual(2, len(manifest["selection"]["alternate_ids"]))
                self.assertEqual(0, manifest["selection"]["alternates_shortfall"])
                self.assertEqual("1.2", manifest["protocol_version"])
                candidates_text = (stage / freeze.FINAL_DIR_REL / "candidates.tsv").read_text(
                    encoding="utf-8")
                # The seam-window cell from tile six is contained and measured but never
                # eligible (E6), and stays in the union catalog.
                self.assertIn("r300c8", candidates_text)
        self.assertEqual(table_bytes[0][0], table_bytes[1][0])
        self.assertEqual(table_bytes[0][1], table_bytes[1][1])
        self.assertEqual(digests[0], digests[1])


if __name__ == "__main__":
    unittest.main()
