"""Offline unit tests for scripts/geo/sedan_extract.py (stdlib only, no 7z, no real dataset)."""

import contextlib
import hashlib
import importlib.util
import io
import json
import os
import re
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path
from types import SimpleNamespace

spec = importlib.util.spec_from_file_location(
    "sedan_extract", Path(__file__).resolve().parents[1] / "sedan_extract.py")
sedan = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sedan)

STAGE_NAME_RE = re.compile(r"^sedan-extract-\d{4}-\d{2}-\d{2}T\d{2}\.\d{2}\.\d{2}.*$")


def make_slt(stem, entries):
    """Real-format copy of `7z l -slt` output for a split archive (7-Zip 26.00)."""
    header = (
        "7-Zip 26.00 (x64) : listing fixture\n\n"
        f"Listing archive: {stem}.7z.001\n\n"
        f"--\nPath = {stem}.7z.001\nType = Split\nPhysical Size = 100\n"
        f"Total Physical Size = 100\n"
        f"----\nPath = {stem}.7z\nSize = 100\n--\nPath = {stem}.7z\nType = 7z\nSolid = +\n\n"
        "----------\n"
    )
    blocks = []
    for path, size, is_dir in entries:
        attributes = "D drwxr-xr-x" if is_dir else "A_ -r--r--r--"
        blocks.append(
            f"Path = {path}\nSize = {size}\nPacked Size = {size}\n"
            f"Attributes = {attributes}\nCRC = 00000000\nEncrypted = -\nBlock = 0")
    return header + "\n\n".join(blocks) + "\n"


class FakeReader:
    """Seam stand-in for ChildReader: canned stdout chunks, optional stall."""

    def __init__(self, chunks, returncode=0, stall=0.0):
        self.chunks = list(chunks)
        self.returncode = returncode
        self.stall = stall
        self.stderr = bytearray()
        self.killed = False
        self.closes = 0
        self._out_done = False
        self._err_done = False

    def read_batch(self, timeout_seconds):
        if self.stall:
            time.sleep(self.stall)
        if self.chunks:
            return [("out", self.chunks.pop(0))]
        if not self._out_done:
            self._out_done = True
            return [("out-eof", b"")]
        if not self._err_done:
            self._err_done = True
            return [("err-eof", b"")]
        return []

    def kill(self):
        self.killed = True

    def wait(self, timeout_seconds=None):
        return self.returncode

    def close(self):
        self.closes += 1


A_MEMBER = ("RGEALTI", "rge/RGEALTI_FXX_0839_6958_MNT_LAMB93_IGN69.tif", 64)
B_MEMBER = ("BDORTHO", "bd/08-2022-0835-6960-LA93-0M20-E080.jp2", 32)
A_BASENAME = A_MEMBER[1].rsplit("/", 1)[-1]
B_BASENAME = B_MEMBER[1].rsplit("/", 1)[-1]


class ExtractTestBase(unittest.TestCase):
    def patch(self, name, value):
        original = getattr(sedan, name)
        setattr(sedan, name, value)
        self.addCleanup(setattr, sedan, name, original)

    def tmp_dir(self):
        parent = "/tmp/opencode" if os.path.isdir("/tmp/opencode") else None
        context = tempfile.TemporaryDirectory(prefix="sedan-extract-test-", dir=parent)
        self.addCleanup(context.cleanup)
        return Path(context.name)

    def run_main(self, *argv):
        buffer = io.StringIO()
        errors = io.StringIO()
        with contextlib.redirect_stdout(buffer), contextlib.redirect_stderr(errors):
            code = sedan.main(list(argv))
        return code, buffer.getvalue() + errors.getvalue()

    def patch_statvfs(self, free_bytes):
        block = 4096
        self.patch("statvfs", lambda path: SimpleNamespace(
            f_bavail=free_bytes // block, f_frsize=block, f_bsize=block))

    def patch_ample_space(self):
        self.patch_statvfs(1 << 45)

    def assert_reaped(self, pid_path):
        pid = int(pid_path.read_text())
        with self.assertRaises(ProcessLookupError):
            os.kill(pid, 0)

    def never_listing(self):
        def seam(argv, timeout_seconds):
            raise AssertionError(f"unexpected 7z listing: {argv}")

        self.patch("run_listing", seam)
        return seam

    def never_stream(self):
        def seam(argv):
            raise AssertionError(f"unexpected 7z extract: {argv}")

        self.patch("open_member_stream", seam)
        return seam

    def recording_listing(self, texts):
        calls = []

        def seam(argv, timeout_seconds):
            self.assertEqual(["7z", "l", "-slt"], argv[:3])
            self.assertEqual(sedan.LISTING_TIMEOUT_SECONDS, timeout_seconds)
            calls.append(argv[3])
            return texts[argv[3]].encode()

        seam.calls = calls
        self.patch("run_listing", seam)
        return seam

    def stream_seam(self, plan):
        created = []

        def seam(argv):
            self.assertEqual(["7z", "x", "-so"], argv[:3])
            self.assertIn(argv[4], plan)
            created.append(argv[4])
            return plan[argv[4]]

        seam.created = created
        self.patch("open_member_stream", seam)
        return seam

    def tiny_archives(self):
        specs = (
            sedan.ArchiveSpec("RGEALTI", "archives/rge", "rge", (256,)),
            sedan.ArchiveSpec("BDORTHO", "archives/bd", "bd", (128,)),
        )
        self.patch("ARCHIVES", specs)
        return specs

    def tiny_members(self, member_specs):
        members = [sedan.Member(a, p, d, False) for a, p, d in member_specs]
        total = sum(member.declared for member in members)
        self.patch("selected_members", lambda with_dst: members)
        self.patch("SELECTED_MEMBER_COUNT", len(members))
        self.patch("SELECTED_TOTAL", total)
        self.patch("WITH_DST_MEMBER_COUNT", len(members))
        self.patch("WITH_DST_TOTAL", total)
        return members

    def member_dispatch_body(self, tif_lines, jp2_lines=None):
        jp2_lines = jp2_lines if jp2_lines is not None else ["os.write(1, bytes(range(32)))"]

        def block(lines):
            return "".join(f"    {line}\n" for line in lines)

        return ("member = sys.argv[1]\n"
                "if member.endswith('.tif'):\n"
                + block(tif_lines)
                + "else:\n"
                + block(jp2_lines))

    def install_real_listing(self, members, specs, extract_body=None):
        """Routes the guard chain through the REAL run_listing and real extract children
        via a PATH-shim 7z serving both the `l -slt` and the `x -so` argv forms."""
        bin_dir = self.tmp_dir() / "bin"
        bin_dir.mkdir()
        fixture_dir = self.tmp_dir() / "slt"
        fixture_dir.mkdir()
        mapping = {}
        for archive_spec in specs:
            entries = [(archive_spec.stem + "/data", 0, True)]
            entries += [(m.path, m.declared, False) for m in members if m.archive == archive_spec.key]
            fixture = fixture_dir / f"{archive_spec.key}.slt"
            fixture.write_text(make_slt(archive_spec.stem, entries))
            mapping[f"{archive_spec.stem}.7z.001"] = str(fixture)
        self.real_child_pid_path = self.tmp_dir() / "extract-child.pid"
        body_lines = [f"open({str(self.real_child_pid_path)!r}, 'w').write(str(os.getpid()))"]
        body_lines.append(extract_body if extract_body else self.member_dispatch_body(
            ["os.write(1, bytes(range(64)))"]))
        body_file = self.tmp_dir() / "extract_body.py"
        body_file.write_text("import os, sys, time\n" + "".join(f"{line}\n" for line in body_lines))
        lines = ["#!/bin/sh", 'if [ "$1" = "x" ]; then',
                 f'  exec "{sys.executable}" "{body_file}" "$4"', "fi", 'case "$3" in']
        for archive_name, fixture in sorted(mapping.items()):
            lines.append(f'  *{archive_name}) cat "{fixture}" ;;')
        lines.append("esac")
        shim = bin_dir / "7z"
        shim.write_text("\n".join(lines) + "\n")
        shim.chmod(0o755)
        saved = os.environ.get("PATH", "")
        os.environ["PATH"] = f"{bin_dir}{os.pathsep}{saved}" if saved else str(bin_dir)
        self.addCleanup(os.environ.__setitem__, "PATH", saved)

    def prepare_extract_env(self, member_specs=(A_MEMBER, B_MEMBER), real_listing=False,
                            extract_body=None):
        root = self.tmp_dir() / "cache"
        (root / "state").mkdir(parents=True)
        specs = self.tiny_archives()
        members = self.tiny_members(member_specs)
        for archive_spec in specs:
            directory = root / archive_spec.dir_rel
            directory.mkdir(parents=True)
            for number, size in enumerate(archive_spec.part_sizes, start=1):
                (directory / f"{archive_spec.stem}.7z.{number:03d}").write_bytes(b"\x00" * size)
        state = {
            "version": sedan.STATE_VERSION,
            "products": {key: {"status": "complete"} for key in sedan.expected_part_keys()},
        }
        (root / "state" / "state.json").write_text(json.dumps(state))
        if real_listing:
            self.install_real_listing(members, specs, extract_body=extract_body)
        else:
            texts = {}
            for archive_spec in specs:
                archive = str(root / archive_spec.dir_rel / f"{archive_spec.stem}.7z.001")
                entries = [(archive_spec.stem + "/data", 0, True)]
                entries += [(m.path, m.declared, False) for m in members if m.archive == archive_spec.key]
                texts[archive] = make_slt(archive_spec.stem, entries)
            self.recording_listing(texts)
        self.patch_ample_space()
        return root, members

    def stage_dir(self, root):
        stages = list((root / "extracted").iterdir())
        self.assertEqual(1, len(stages))
        return stages[0]

    def stage_state(self, stage):
        return json.loads((stage / "state" / "state.json").read_text())


class GoldenMembershipTests(unittest.TestCase):
    def test_golden_table_matches_approved_selection(self):
        rge = "RGEALTI_2-0_1M_TIF_LAMB93-IGN69_D008_2019-10-14/RGEALTI"
        rge_mnt = rge + "/1_DONNEES_LIVRAISON_2021-01-00150/RGEALTI_MNT_1M_TIF_LAMB93_IGN69_D008_20210118"
        rge_src = rge + "/1_DONNEES_LIVRAISON_2021-01-00150/RGEALTI_SRC_1M_TIF_LAMB93_IGN69_D008_20210118"
        rge_meta = rge + "/2_METADONNEES_LIVRAISON_2021-01-00150/RGEALTI_MNT_1M_TIF_LAMB93_IGN69_D008_20210118"
        rge_sup = rge + "/3_SUPPLEMENTS_LIVRAISON_2021-01-00150/RGEALTI_MNT_1M_TIF_LAMB93_IGN69_D008_20210118"
        bd = "ORTHOHR_1-0_RVB-0M20_JP2-E080_LAMB93_D008_2022-01-01/ORTHOHR"
        bd_data = bd + "/1_DONNEES_LIVRAISON_2023-06-00036/OHR_RVB_0M20_JP2-E080_LAMB93_D08-2022"
        bd_meta = bd + "/2_METADONNEES_LIVRAISON_2023-06-00036/OHR_RVB_0M20_JP2-E080_LAMB93_D08-2022"
        bd_sup = bd + "/3_SUPPLEMENTS_LIVRAISON_2023-06-00036/OHR_RVB_0M20_JP2-E080_LAMB93_D08-2022"
        expected = {
            (f"{rge_mnt}/RGEALTI_FXX_0839_6958_MNT_LAMB93_IGN69.tif", 1089059),
            (f"{rge_mnt}/RGEALTI_FXX_0839_6959_MNT_LAMB93_IGN69.tif", 1243770),
            (f"{rge_mnt}/RGEALTI_FXX_0840_6958_MNT_LAMB93_IGN69.tif", 1199456),
            (f"{rge_mnt}/RGEALTI_FXX_0840_6959_MNT_LAMB93_IGN69.tif", 1433927),
            (f"{rge_mnt}/RGEALTI_FXX_0841_6958_MNT_LAMB93_IGN69.tif", 1556520),
            (f"{rge_mnt}/RGEALTI_FXX_0841_6959_MNT_LAMB93_IGN69.tif", 1583856),
        } | {(f"{rge_src}/RGEALTI_FXX_{tile}_SRC_LAMB93_IGN69.tif", 1002982)
             for tile in ("0839_6958", "0839_6959", "0840_6958", "0840_6959", "0841_6958", "0841_6959")}
        expected |= {
            (f"{rge_meta}/IGNF_RGEALTIr_2-0_TIF_1M_LAMB93_IGN69_D008_20210118.xml", 29195),
            (f"{rge_sup}/dalles.dbf", 1380070),
            (f"{rge_sup}/dalles.shp", 753540),
            (f"{rge_sup}/dalles.shx", 44420),
            (f"{rge_sup}/dalles.prj", 451),
            (f"{bd_data}/08-2022-0835-6960-LA93-0M20-E080.jp2", 94382758),
            (f"{bd_data}/08-2022-0840-6960-LA93-0M20-E080.jp2", 94380240),
            (f"{bd_data}/08-2022-0835-6960-LA93-0M20-E080.tab", 436),
            (f"{bd_data}/08-2022-0840-6960-LA93-0M20-E080.tab", 436),
            (f"{bd_meta}/IGNF_ORTHOHR_1-0_JP2-E080_LAMB93_D08-2022.xml", 35454),
            (f"{bd_meta}/IGNF_ORTHOHR_1-0_JP2-E080_LAMB93_D08-2022.html", 28543),
            (f"{bd_sup}/dalles.dbf", 67386),
            (f"{bd_sup}/dalles.shp", 36004),
            (f"{bd_sup}/dalles.shx", 2212),
            (f"{bd_sup}/dalles.prj", 452),
        }
        members = sedan.selected_members(False)
        self.assertEqual(27, len(members))
        self.assertEqual(sedan.SELECTED_MEMBER_COUNT, 27)
        self.assertEqual(expected, {(m.path, m.declared) for m in members})
        self.assertTrue(all(m.archive in ("RGEALTI", "BDORTHO") for m in members))
        self.assertTrue(all(not m.optional for m in members))
        self.assertEqual(sedan.SELECTED_TOTAL, 205266077)
        self.assertEqual(sedan.SELECTED_TOTAL, sum(m.declared for m in members))
        for member in members:
            self.assertNotIn("_6957_", member.path)
            self.assertNotIn("-6955-", member.path)
            self.assertNotIn("_DST_", member.path)
            if member.archive == "BDORTHO":
                self.assertTrue(member.path.startswith("ORTHOHR_1-0_"), member.path)
        with_dst = sedan.selected_members(True)
        self.assertEqual(33, len(with_dst))
        self.assertEqual(members, [m for m in with_dst if not m.optional])
        dst = [m for m in with_dst if m.optional]
        self.assertEqual(sedan.DST_TOTAL, 6017892)
        self.assertEqual(sedan.WITH_DST_TOTAL, 211283969)
        self.assertEqual(sedan.WITH_DST_TOTAL, sum(m.declared for m in with_dst))
        self.assertTrue(all(m.optional for m in dst))
        rge_dst = rge + "/1_DONNEES_LIVRAISON_2021-01-00150/RGEALTI_DST_1M_TIF_LAMB93_IGN69_D008_20210118"
        expected_dst = {
            f"{rge_dst}/RGEALTI_FXX_{tile}_DST_LAMB93_IGN69.tif": 1002982
            for tile in ("0839_6958", "0839_6959", "0840_6958", "0840_6959", "0841_6958", "0841_6959")}
        self.assertEqual(expected_dst, {m.path: m.declared for m in dst})


class TableGuardTests(ExtractTestBase):
    def test_malformed_tables_fail_before_any_filesystem_use(self):
        base = [A_MEMBER, B_MEMBER]
        sum_base = 96
        mutations = {
            "duplicate": (base + [A_MEMBER], 3, sum_base + 64),
            "glob char": ([("RGEALTI", "rge/a*.tif", 64), B_MEMBER], 2, sum_base),
            "absolute": ([("RGEALTI", "/abs/a.tif", 64), B_MEMBER], 2, sum_base),
            "dotdot": ([("RGEALTI", "rge/../a.tif", 64), B_MEMBER], 2, sum_base),
            "backslash": ([("RGEALTI", "rge\\a.tif", 64), B_MEMBER], 2, sum_base),
            "nul": ([("RGEALTI", "rge/a\x00.tif", 64), B_MEMBER], 2, sum_base),
            "bad suffix": ([("RGEALTI", "rge/a.exe", 64), B_MEMBER], 2, sum_base),
            "zero size": ([("RGEALTI", "rge/a.tif", 0), B_MEMBER], 2, sum_base),
            "negative size": ([("RGEALTI", "rge/a.tif", -1), B_MEMBER], 2, sum_base),
        }
        for name, (table, count, total) in mutations.items():
            with self.subTest(case=name):
                root = self.tmp_dir()
                self.tiny_archives()
                self.tiny_members(table)
                self.patch("SELECTED_MEMBER_COUNT", count)
                self.patch("SELECTED_TOTAL", total)
                self.patch("PROC_MOUNTS_PATH", str(root / "missing-mounts"))
                self.never_listing()
                self.never_stream()
                code, output = self.run_main("--extract", str(root))
                self.assertEqual(sedan.EXIT_STATE, code)
                self.assertIn("FAIL(4)", output)
                self.assertEqual([], list(root.iterdir()))

    def test_wrong_total_fails_table(self):
        root = self.tmp_dir()
        self.tiny_archives()
        self.tiny_members([A_MEMBER, B_MEMBER])
        self.patch("SELECTED_TOTAL", 97)
        self.patch("PROC_MOUNTS_PATH", str(root / "missing-mounts"))
        self.never_listing()
        self.never_stream()
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_STATE, code)
        self.assertIn("totals 96 bytes, expected pinned 97", output)
        self.assertEqual([], list(root.iterdir()))

    def test_table_is_validated_before_guard_io(self):
        root = self.tmp_dir()
        self.tiny_archives()
        self.tiny_members([("RGEALTI", "rge/a*.tif", 64), B_MEMBER])
        self.patch("PROC_MOUNTS_PATH", str(root / "missing-mounts"))
        self.never_listing()
        self.never_stream()
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_STATE, code, "table guard must fire before the mounts guard")

    def test_valid_table_lets_guards_run(self):
        root = self.tmp_dir()
        self.tiny_archives()
        self.tiny_members([A_MEMBER, B_MEMBER])
        self.patch("MOUNT_POINT", str(root))
        self.patch("PROC_MOUNTS_PATH", str(root / "missing-mounts"))
        self.never_listing()
        self.never_stream()
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_GUARD, code)
        self.assertIn("cannot read", output)


class GuardOrderTests(ExtractTestBase):
    def test_mount_without_rw_entry_fails_before_mkdir(self):
        root, _members = self.prepare_extract_env()
        prefix = self.tmp_dir() / "mnt" / "pit2"
        prefix.mkdir(parents=True)
        mounts = self.tmp_dir() / "mounts.txt"
        mounts.write_text(f"/dev/sdz {prefix} ext4 ro,relatime 0 0\n")
        self.patch("MOUNT_POINT", str(prefix))
        self.patch("PROC_MOUNTS_PATH", str(mounts))
        cache = prefix / "panopticon" / "geo-proof" / "sedan"
        self.never_listing()
        self.never_stream()
        code, output = self.run_main("--extract", str(cache))
        self.assertEqual(sedan.EXIT_GUARD, code)
        self.assertIn("rw mount", output)
        self.assertFalse(cache.exists())

    @unittest.skipIf(os.geteuid() == 0, "os.access always grants root")
    def test_ancestor_unwritable_fails_before_mkdir(self):
        root, _members = self.prepare_extract_env()
        os.chmod(root, 0o555)
        self.addCleanup(os.chmod, root, 0o755)
        self.never_listing()
        self.never_stream()
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_GUARD, code)
        self.assertIn("not writable", output)
        self.assertFalse((root / "extracted").exists())

    def test_missing_part_fails_guard(self):
        root, _members = self.prepare_extract_env()
        (root / "archives" / "bd" / "bd.7z.001").unlink()
        self.never_listing()
        self.never_stream()
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_GUARD, code)
        self.assertIn("archive part missing", output)
        self.assertFalse((root / "extracted").exists())

    def test_wrong_part_size_fails_guard(self):
        root, _members = self.prepare_extract_env()
        (root / "archives" / "bd" / "bd.7z.001").write_bytes(b"\x00" * 127)
        self.never_listing()
        self.never_stream()
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_GUARD, code)
        self.assertIn("size drift", output)
        self.assertFalse((root / "extracted").exists())

    def prepare_state(self, payload):
        root, _members = self.prepare_extract_env()
        (root / "state" / "state.json").write_text(payload)
        self.never_listing()
        self.never_stream()
        return root

    def test_corrupted_acquisition_state_fails_state(self):
        root = self.prepare_state("{not json")
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_STATE, code)
        self.assertIn("cannot be parsed", output)
        self.assertFalse((root / "extracted").exists())

    def test_wrong_version_acquisition_state_fails_state(self):
        root = self.prepare_state(json.dumps({"version": 99, "products": {}}))
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_STATE, code)
        self.assertIn("unsupported or missing version", output)

    def test_acquisition_state_missing_entry_fails_guard(self):
        root = self.prepare_state(json.dumps({"version": 1, "products": {}}))
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_GUARD, code)
        self.assertIn("no entry for", output)
        self.assertFalse((root / "extracted").exists())

    def test_acquisition_state_partial_entry_fails_guard(self):
        root, _members = self.prepare_extract_env()
        state = {"version": 1, "products": {key: {"status": "complete"}
                                            for key in sedan.expected_part_keys()}}
        first = sorted(state["products"])[0]
        state["products"][first]["status"] = "partial"
        (root / "state" / "state.json").write_text(json.dumps(state))
        self.never_listing()
        self.never_stream()
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_GUARD, code)
        self.assertIn("must be complete", output)


class ListingValidationTests(ExtractTestBase):
    def test_listing_size_drift_fails_pin(self):
        root, members = self.prepare_extract_env()
        by_path = {m.path: m.declared for m in members}
        entries = [("rge/data", 0, True)] + [(m.path, by_path[m.path], False)
                                            for m in members if m.archive == "RGEALTI"]
        entries[1] = (entries[1][0], entries[1][1] - 1, False)
        archive = str(root / "archives" / "rge" / "rge.7z.001")
        texts = {archive: make_slt("rge", entries)}
        self.recording_listing(texts)
        self.never_stream()
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_STATE, code)
        self.assertIn("listing size", output)
        self.assertIn("pin drift", output)
        self.assertIn(members[0].path, output)
        self.assertFalse((root / "extracted").exists())

    def test_missing_member_in_listing_fails_pin(self):
        root, members = self.prepare_extract_env()
        entries = [("rge/data", 0, True)] + [(m.path, m.declared, False)
                                            for m in members if m.archive == "RGEALTI"]
        missing = [m.path for m in members if m.archive == "RGEALTI"][-1]
        entries = [entry for entry in entries if entry[0] != missing]
        archive = str(root / "archives" / "rge" / "rge.7z.001")
        self.recording_listing({archive: make_slt("rge", entries)})
        self.never_stream()
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_STATE, code)
        self.assertIn("missing from the 7z listing", output)
        self.assertIn(missing, output)
        self.assertFalse((root / "extracted").exists())

    def test_duplicated_member_in_listing_fails_pin(self):
        root, members = self.prepare_extract_env()
        entries = [("rge/data", 0, True)]
        entries += [(m.path, m.declared, False) for m in members if m.archive == "RGEALTI"]
        entries.append(entries[-1])
        archive = str(root / "archives" / "rge" / "rge.7z.001")
        self.recording_listing({archive: make_slt("rge", entries)})
        self.never_stream()
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_STATE, code)
        self.assertIn("listed 2 times", output)
        self.assertFalse((root / "extracted").exists())


class SpaceGuardTests(ExtractTestBase):
    def test_insufficient_space_fails_before_stage(self):
        root, members = self.prepare_extract_env()
        total = sum(m.declared for m in members)
        required = total + sedan.RESERVE_BYTES
        self.patch_statvfs(required - 1000)
        self.never_stream()
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_GUARD, code)
        self.assertIn("insufficient free space", output)
        self.assertIn(f"required={required}", output)
        self.assertFalse((root / "extracted").exists())


class ListingCaptureTests(ExtractTestBase):
    """Real-subprocess regressions for the bounded run_listing capture (no seams)."""

    def child_script(self, body):
        base = self.tmp_dir()
        pid_path = base / "child.pid"
        script = (
            "import os, sys, time\n"
            f"open({str(pid_path)!r}, 'w').write(str(os.getpid()))\n" + body)
        return [sys.executable, "-c", script], pid_path, base

    def capture_failure(self, argv, timeout_seconds):
        started = time.monotonic()
        with self.assertRaises(sedan.Failure) as caught:
            sedan.run_listing(argv, timeout_seconds)
        return caught.exception, time.monotonic() - started

    def assert_no_writes(self, base):
        self.assertEqual(["child.pid"], [entry.name for entry in base.iterdir()])

    def test_stdout_oversize_kills_and_reaps_before_child_exit(self):
        self.patch("LISTING_OUTPUT_CAP", 4096)
        argv, pid_path, base = self.child_script(
            "for _ in range(8):\n"
            "    os.write(1, b'x' * 1024)\n"
            "    time.sleep(0.05)\n"
            "time.sleep(5)\n")
        failure, elapsed = self.capture_failure(argv, 30.0)
        self.assertEqual(sedan.EXIT_GUARD, failure.code)
        self.assertIn("oversize", failure.message)
        self.assertIn("4096-byte cap", failure.message)
        self.assertLess(elapsed, 4.0, "kill must happen without waiting for the sleeping child")
        self.assert_reaped(pid_path)
        self.assert_no_writes(base)

    def test_oversize_single_large_chunk_kills_and_reaps(self):
        self.patch("LISTING_OUTPUT_CAP", 1024)
        argv, pid_path, base = self.child_script(
            "os.write(1, b'y' * 1025)\n"
            "time.sleep(5)\n")
        failure, elapsed = self.capture_failure(argv, 30.0)
        self.assertEqual(sedan.EXIT_GUARD, failure.code)
        self.assertIn("oversize", failure.message)
        self.assertLess(elapsed, 4.0)
        self.assert_reaped(pid_path)
        self.assert_no_writes(base)

    def test_stderr_flood_kills_and_reaps(self):
        argv, pid_path, base = self.child_script(
            "os.write(1, b'ok\\n')\n"
            "os.write(2, b'e' * 70000)\n"
            "time.sleep(5)\n")
        failure, elapsed = self.capture_failure(argv, 30.0)
        self.assertEqual(sedan.EXIT_GUARD, failure.code)
        self.assertIn("stderr-flood", failure.message)
        self.assertIn("65536-byte cap", failure.message)
        self.assertLess(elapsed, 4.0)
        self.assert_reaped(pid_path)
        self.assert_no_writes(base)

    def test_deadline_with_running_child_kills_and_reaps(self):
        argv, pid_path, base = self.child_script("time.sleep(5)\n")
        failure, elapsed = self.capture_failure(argv, 0.5)
        self.assertEqual(sedan.EXIT_GUARD, failure.code)
        self.assertIn("timeout", failure.message)
        self.assertIn("0.5 s", failure.message)
        self.assertLess(elapsed, 4.0)
        self.assert_reaped(pid_path)
        self.assert_no_writes(base)

    def test_nonzero_exit_names_the_rc(self):
        catalog = self.tmp_dir() / "catalog.slt"
        catalog.write_text(make_slt("rge", [("rge/data", 0, True), ("rge/a.tif", 10, False)]))
        argv, pid_path, base = self.child_script(
            f"os.write(1, open({str(catalog)!r}, 'rb').read())\n"
            "sys.exit(7)\n")
        failure, _elapsed = self.capture_failure(argv, 30.0)
        self.assertEqual(sedan.EXIT_GUARD, failure.code)
        self.assertIn("exited with code 7", failure.message)
        self.assert_reaped(pid_path)
        self.assert_no_writes(base)

    def test_full_guard_chain_runs_through_real_listing(self):
        root, members = self.prepare_extract_env(real_listing=True)
        plan = {
            members[0].path: FakeReader([bytes(range(64))]),
            members[1].path: FakeReader([bytes(range(32))]),
        }
        seam = self.stream_seam(plan)
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_OK, code, output)
        self.assertEqual([members[0].path, members[1].path], seam.created)
        stage = self.stage_dir(root)
        self.assertTrue((stage / "RECEIPT.txt").exists())
        self.assertTrue((stage / "COMPLETED").exists())


class RealChildExtractionTests(ExtractTestBase):
    """Real local child processes through the REAL ChildReader/pump_member/extract_one
    path (PATH-shim 7z or direct open_member_stream); no FakeReader, no assets."""

    def pump_real_child(self, body_lines, declared, member_timeout=None):
        if member_timeout is not None:
            self.patch("MEMBER_TIMEOUT_SECONDS", member_timeout)
        base = self.tmp_dir()
        target = base / "member.bin"
        body = "import os, sys, time\n" + "".join(f"{line}\n" for line in body_lines)
        out_fd = os.open(target, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o644)
        try:
            reader = sedan.open_member_stream([sys.executable, "-c", body])
            try:
                result = sedan.pump_member(reader, out_fd, declared)
            finally:
                reader.close()
        finally:
            os.close(out_fd)
        return result, reader, target, base

    def assert_stage_only_writes(self, root):
        self.assertEqual({"archives", "state", "extracted"},
                         {entry.name for entry in root.iterdir()})
        self.assertEqual(1, len(list((root / "extracted").iterdir())))

    def test_exact_bytes_with_stderr_line_completes_stage(self):
        root, members = self.prepare_extract_env(real_listing=True, extract_body=self.member_dispatch_body(
            ["os.write(1, bytes(range(64)))", "os.write(2, b'stream ok\\n')"]))
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_OK, code, output)
        stage = self.stage_dir(root)
        self.assertTrue((stage / "RECEIPT.txt").exists())
        self.assertTrue((stage / "COMPLETED").exists())
        state = self.stage_state(stage)
        record = state["members"][members[0].path]
        self.assertEqual("complete", record["status"])
        self.assertEqual(hashlib.sha256(bytes(range(64))).hexdigest(), record["sha256"])
        self.assertEqual(bytes(range(64)), (stage / "files" / "1_tif" / A_BASENAME).read_bytes())
        self.assert_reaped(self.real_child_pid_path)

    def test_stdout_eof_before_stderr_eof_succeeds_in_pump(self):
        result, reader, target, _base = self.pump_real_child(
            ["os.write(1, bytes(range(64)))", "os.close(1)", "time.sleep(0.05)",
             "os.write(2, b'done\\n')", "os.close(2)"], 64)
        self.assertIsNone(result.problem)
        self.assertEqual(64, result.got)
        self.assertEqual(0, result.returncode)
        self.assertEqual(0, reader.proc.poll())
        self.assertEqual(bytes(range(64)), target.read_bytes())

    def test_deadline_stops_pump_and_reaps_running_child(self):
        result, reader, target, _base = self.pump_real_child(
            ["os.write(1, bytes(range(32)))", "time.sleep(30)"], 32, member_timeout=0.6)
        self.assertEqual("timeout", result.problem)
        self.assertEqual(32, result.got)
        self.assertIsNotNone(reader.proc.poll())
        self.assertEqual(32, target.stat().st_size)

    def test_deadline_stall_fails_exit3_and_preserves_partial(self):
        root, members = self.prepare_extract_env(real_listing=True, extract_body=self.member_dispatch_body(
            ["os.write(1, bytes(range(32)))", "time.sleep(30)"]))
        self.patch("MEMBER_TIMEOUT_SECONDS", 0.7)
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_EXTRACT, code)
        self.assertIn("timeout", output)
        stage = self.stage_dir(root)
        partial = stage / "files" / "1_tif" / A_BASENAME
        self.assertEqual(32, partial.stat().st_size)
        record = self.stage_state(stage)["members"][members[0].path]
        self.assertEqual("partial", record["status"])
        self.assertEqual(32, record["got"])
        self.assertFalse((stage / "COMPLETED").exists())
        self.assert_reaped(self.real_child_pid_path)
        self.assert_stage_only_writes(root)

    def test_nonzero_exit_with_exact_bytes_fails_naming_rc(self):
        root, members = self.prepare_extract_env(real_listing=True, extract_body=self.member_dispatch_body(
            ["os.write(1, bytes(range(64)))", "sys.exit(7)"]))
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_EXTRACT, code)
        self.assertIn("7z exit code 7", output)
        stage = self.stage_dir(root)
        partial = stage / "files" / "1_tif" / A_BASENAME
        self.assertEqual(64, partial.stat().st_size)
        record = self.stage_state(stage)["members"][members[0].path]
        self.assertEqual("partial", record["status"])
        self.assertEqual(64, record["got"])
        self.assert_reaped(self.real_child_pid_path)
        self.assert_stage_only_writes(root)

    def test_stderr_flood_with_exact_stdout_succeeds(self):
        root, members = self.prepare_extract_env(real_listing=True, extract_body=self.member_dispatch_body(
            ["os.write(1, bytes(range(64)))", "os.write(2, b'e' * 5000000)"]))
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_OK, code, output)
        stage = self.stage_dir(root)
        self.assertTrue((stage / "COMPLETED").exists())
        state = self.stage_state(stage)
        self.assertEqual("complete", state["members"][members[0].path]["status"])
        self.assert_reaped(self.real_child_pid_path)

    def test_real_overrun_fails_drift_keeping_declared_plus_one(self):
        root, members = self.prepare_extract_env(real_listing=True, extract_body=self.member_dispatch_body(
            ["os.write(1, bytes(range(65)))", "time.sleep(5)"]))
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_STATE, code)
        self.assertIn("overlong", output)
        self.assertIn("drift", output)
        stage = self.stage_dir(root)
        partial = stage / "files" / "1_tif" / A_BASENAME
        self.assertEqual(65, partial.stat().st_size)
        record = self.stage_state(stage)["members"][members[0].path]
        self.assertEqual("partial", record["status"])
        self.assertEqual(65, record["got"])
        self.assertFalse((stage / "COMPLETED").exists())
        self.assert_reaped(self.real_child_pid_path)
        self.assert_stage_only_writes(root)


class ProcessTests(ExtractTestBase):
    def a_reader(self, chunks, returncode=0, stall=0.0):
        return FakeReader(chunks, returncode=returncode, stall=stall)

    def test_success_writes_files_state_receipt_and_marker(self):
        root, members = self.prepare_extract_env()
        a_bytes = bytes(range(64))
        b_bytes = bytes(range(32))
        plan = {
            members[0].path: self.a_reader([a_bytes[:32], a_bytes[32:]]),
            members[1].path: self.a_reader([b_bytes]),
        }
        seam = self.stream_seam(plan)
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_OK, code, output)
        self.assertEqual([members[0].path, members[1].path], seam.created)
        stage = self.stage_dir(root)
        self.assertRegex(stage.name, STAGE_NAME_RE)
        self.assertEqual(a_bytes, (stage / "files" / "1_tif" / A_BASENAME).read_bytes())
        self.assertEqual(b_bytes, (stage / "files" / "2_jp2" / B_BASENAME).read_bytes())
        self.assertTrue((stage / "STARTED").exists())
        self.assertTrue((stage / "COMPLETED").exists())
        self.assertTrue((stage / "RECEIPT.txt").exists())
        self.assertFalse((stage / "state" / "lock").exists())
        state = self.stage_state(stage)
        self.assertEqual("complete", state["members"][members[0].path]["status"])
        self.assertEqual(hashlib.sha256(b_bytes).hexdigest(),
                         state["members"][members[1].path]["sha256"])
        receipt = (stage / "RECEIPT.txt").read_text()
        self.assertIn("2021-01-00150", receipt)
        self.assertIn("index DBF date 2021-01-20", receipt)
        self.assertIn("2023-06-00036", receipt)
        self.assertIn("index DBF date 2023-06-08", receipt)
        self.assertIn("SW 639841.997/5506768.179", receipt)
        self.assertIn("NE 641012.429/5507938.611", receipt)
        self.assertIn("3.048", receipt)
        self.assertIn("1.524", receipt)
        self.assertIn("1.0 m", receipt)
        self.assertIn("never silent recentring", receipt)
        self.assertIn("provenance: RGE dalles index DBF (in-stage)", receipt)
        self.assertIn("hash-manifest.txt", receipt)
        self.assertIn(f"member: RGEALTI {members[0].path} declared=64 got=64", receipt)
        self.assertIn("total declared: 96", receipt)
        self.assertIn("total got: 96", receipt)
        self.assertIn("receipt and COMPLETED marker written", output)

    def test_short_read_fails_exit3_preserving_stage(self):
        root, members = self.prepare_extract_env()
        plan = {
            members[0].path: self.a_reader([bytes(range(64))]),
            members[1].path: self.a_reader([b"xxxx"]),
        }
        self.stream_seam(plan)
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_EXTRACT, code)
        self.assertIn("byte count 4 != pinned 32", output)
        self.assertIn("stage preserved", output)
        stage = self.stage_dir(root)
        self.assertTrue((stage / "STARTED").exists())
        self.assertFalse((stage / "COMPLETED").exists())
        self.assertFalse((stage / "RECEIPT.txt").exists())
        partial = stage / "files" / "2_jp2" / B_BASENAME
        self.assertEqual(b"xxxx", partial.read_bytes())
        record = self.stage_state(stage)["members"][members[1].path]
        self.assertEqual("partial", record["status"])
        self.assertEqual(4, record["got"])

    def test_overlong_stream_fails_drift_exit4_keeping_partial(self):
        root, members = self.prepare_extract_env()
        plan = {
            members[0].path: self.a_reader([bytes(range(64))]),
            members[1].path: self.a_reader([bytes(range(33))]),
        }
        reader = plan[members[1].path]
        self.stream_seam(plan)
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_STATE, code)
        self.assertIn("drift", output)
        self.assertIn("overlong", output)
        self.assertTrue(reader.killed)
        stage = self.stage_dir(root)
        partial = stage / "files" / "2_jp2" / B_BASENAME
        self.assertEqual(33, partial.stat().st_size)
        record = self.stage_state(stage)["members"][members[1].path]
        self.assertEqual("partial", record["status"])
        self.assertFalse((stage / "COMPLETED").exists())

    def test_nonzero_7z_exit_fails_exit3(self):
        root, members = self.prepare_extract_env()
        plan = {
            members[0].path: self.a_reader([bytes(range(64))]),
            members[1].path: self.a_reader([bytes(range(32))], returncode=1),
        }
        self.stream_seam(plan)
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_EXTRACT, code)
        self.assertIn("7z exit code 1", output)
        stage = self.stage_dir(root)
        record = self.stage_state(stage)["members"][members[1].path]
        self.assertEqual("partial", record["status"])
        self.assertEqual(32, record["got"])

    def test_member_timeout_fails_exit3_and_kills_child(self):
        root, members = self.prepare_extract_env()
        self.patch("MEMBER_TIMEOUT_SECONDS", 0.05)
        plan = {
            members[0].path: self.a_reader([b"hello"], stall=0.2),
            members[1].path: self.a_reader([bytes(range(32))]),
        }
        reader = plan[members[0].path]
        self.stream_seam(plan)
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_EXTRACT, code)
        self.assertIn("timeout", output)
        self.assertTrue(reader.killed)
        stage = self.stage_dir(root)
        record = self.stage_state(stage)["members"][members[0].path]
        self.assertEqual("partial", record["status"])


class IdempotenceTests(ExtractTestBase):
    def failing_then_retry_plan(self):
        return {
            A_MEMBER[1]: FakeReader([bytes(range(64))]),
            B_MEMBER[1]: FakeReader([b"xxxx"]),
        }

    def test_rerun_resumes_stage_and_skips_complete_members(self):
        root, members = self.prepare_extract_env()
        self.stream_seam(self.failing_then_retry_plan())
        code, _output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_EXTRACT, code)
        stage = self.stage_dir(root)
        complete_file = stage / "files" / "1_tif" / A_BASENAME
        before = os.stat(complete_file).st_mtime_ns
        retry_plan = {B_MEMBER[1]: FakeReader([bytes(range(32))])}
        seam = self.stream_seam(retry_plan)
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_OK, code, output)
        self.assertEqual([B_MEMBER[1]], seam.created)
        self.assertIn("skipping complete member", output)
        self.assertIn("resuming incomplete stage", output)
        self.assertEqual(before, os.stat(complete_file).st_mtime_ns)
        self.assertEqual(bytes(range(64)), complete_file.read_bytes())
        self.assertEqual(b"xxxx", (stage / "files" / "2_jp2" / B_BASENAME).read_bytes())
        self.assertEqual(bytes(range(32)), (stage / "files" / "2_2_jp2" / B_BASENAME).read_bytes())
        self.assertTrue((stage / "COMPLETED").exists())
        receipt = (stage / "RECEIPT.txt").read_text()
        self.assertIn(f"member: RGEALTI {A_MEMBER[1]} declared=64 got=64", receipt)
        self.assertIn(f"member: BDORTHO {B_MEMBER[1]} declared=32 got=32", receipt)

    def test_completed_member_sha_mismatch_fails_repairing_nothing(self):
        root, members = self.prepare_extract_env()
        self.stream_seam(self.failing_then_retry_plan())
        code, _output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_EXTRACT, code)
        stage = self.stage_dir(root)
        complete_file = stage / "files" / "1_tif" / A_BASENAME
        tampered = bytes(range(64, 128))
        complete_file.write_bytes(tampered)
        self.never_stream()
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_STATE, code)
        self.assertIn("repairing nothing", output)
        self.assertEqual(tampered, complete_file.read_bytes())
        self.assertFalse((stage / "COMPLETED").exists())
        record = self.stage_state(stage)["members"][B_MEMBER[1]]
        self.assertEqual("partial", record["status"])
        self.assertEqual(4, record["got"])


class StageSafetyTests(ExtractTestBase):
    def test_existing_stage_stamp_is_refused(self):
        root, _members = self.prepare_extract_env()
        self.patch("local_stamp", lambda: "2026-11-03T12.00.00.000-05.00")
        stage = root / "extracted" / "sedan-extract-2026-11-03T12.00.00.000-05.00"
        stage.mkdir(parents=True)
        self.never_stream()
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_STATE, code)
        self.assertIn("stamp collision", output)
        self.assertEqual([], list(stage.iterdir()))

    def test_extracted_as_regular_file_is_refused(self):
        root, _members = self.prepare_extract_env()
        (root / "extracted").write_text("operator data")
        self.never_stream()
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_STATE, code)
        self.assertIn("operator collision", output)
        self.assertEqual(b"operator data", (root / "extracted").read_bytes())

    def test_symlink_component_fails_guard(self):
        root, _members = self.prepare_extract_env()
        target = self.tmp_dir() / "elsewhere"
        target.mkdir()
        (root / "extracted").symlink_to(target)
        self.never_stream()
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_GUARD, code)
        self.assertIn("symlink", output)
        self.assertEqual([], list(target.iterdir()))

    def test_multiple_incomplete_stages_fail_state(self):
        root, _members = self.prepare_extract_env()
        for name in ("sedan-extract-2026-11-03T12.00.00.000-05.00",
                     "sedan-extract-2026-11-03T12.00.01.000-05.00"):
            stage = root / "extracted" / name
            stage.mkdir(parents=True)
            (stage / "STARTED").write_text("x\n")
        self.never_stream()
        code, output = self.run_main("--extract", str(root))
        self.assertEqual(sedan.EXIT_STATE, code)
        self.assertIn("multiple incomplete stages", output)


class PreflightTests(ExtractTestBase):
    def test_default_preflight_writes_nothing(self):
        root = self.tmp_dir() / "cache"
        self.tiny_archives()
        self.tiny_members([A_MEMBER, B_MEMBER])
        self.never_stream()
        code, output = self.run_main(str(root))
        self.assertEqual(sedan.EXIT_GUARD, code)
        self.assertIn("archive part missing", output)
        self.assertFalse(root.exists())

    def test_with_dst_preflight_reports_optional_set_as_excluded(self):
        root = self.tmp_dir() / "cache"
        self.never_stream()
        code, output = self.run_main("--with-dst", str(root))
        self.assertEqual(sedan.EXIT_GUARD, code)
        for tile in ("0839_6958", "0839_6959", "0840_6958", "0840_6959", "0841_6958", "0841_6959"):
            self.assertIn(f"RGEALTI_FXX_{tile}_DST_LAMB93_IGN69.tif", output)
        self.assertIn("optional DST, excluded", output)
        self.assertIn("33 pinned members", output)
        self.assertIn("211283969", output)
        self.assertFalse(root.exists())

    def test_default_preflight_passes_on_full_env(self):
        root, _members = self.prepare_extract_env()
        self.never_stream()
        code, output = self.run_main(str(root))
        self.assertEqual(sedan.EXIT_OK, code, output)
        self.assertIn("preflight complete", output)
        self.assertIn("nothing was written", output)
        self.assertFalse((root / "extracted").exists())


class VerifyArchivesTests(ExtractTestBase):
    def write_manifest(self, path, entries):
        lines = ["# test acquisition hash manifest"]
        for name, size, digest in entries:
            lines.append(f"{digest}  {size}  {name}")
        path.write_text("\n".join(lines) + "\n")

    def prepare_parts(self):
        root = self.tmp_dir() / "cache"
        specs = self.tiny_archives()
        self.tiny_members([A_MEMBER, B_MEMBER])
        contents = {}
        for archive_spec in specs:
            directory = root / archive_spec.dir_rel
            directory.mkdir(parents=True)
            for number, size in enumerate(archive_spec.part_sizes, start=1):
                data = bytes(range(256)) * (size // 256) + bytes(range(size % 256))
                (directory / f"{archive_spec.stem}.7z.{number:03d}").write_bytes(data)
                contents[f"{archive_spec.stem}.7z.{number:03d}"] = data
        manifest = self.tmp_dir() / "hash-manifest.txt"
        self.patch("HASH_MANIFEST_PATH", str(manifest))
        return root, manifest, contents

    def test_verify_archives_ok(self):
        root, manifest, contents = self.prepare_parts()
        self.write_manifest(manifest, [
            (name, len(data), hashlib.sha256(data).hexdigest()) for name, data in contents.items()])
        code, output = self.run_main("--verify-archives", str(root))
        self.assertEqual(sedan.EXIT_OK, code, output)
        self.assertIn("sha256 ok", output)
        self.assertIn("match the acquisition hash manifest", output)

    def test_verify_archives_mismatch_fails_state(self):
        root, manifest, contents = self.prepare_parts()
        entries = [(name, len(data), hashlib.sha256(data).hexdigest())
                   for name, data in contents.items()]
        name, _size, digest = entries[0]
        entries[0] = (name, entries[0][1], "0" * 64)
        self.write_manifest(manifest, entries)
        code, output = self.run_main("--verify-archives", str(root))
        self.assertEqual(sedan.EXIT_STATE, code)
        self.assertIn("sha256 mismatch", output)
        self.assertIn(name, output)

    def test_verify_archives_missing_manifest_fails_guard(self):
        root, manifest, _contents = self.prepare_parts()
        code, output = self.run_main("--verify-archives", str(root))
        self.assertEqual(sedan.EXIT_GUARD, code)
        self.assertIn("cannot be read", output)
        self.assertIn(str(manifest), output)


class RecoverLockTests(ExtractTestBase):
    def manual_stage(self, root):
        stage = root / "extracted" / "sedan-extract-2026-11-03T12.00.00.000-05.00"
        (stage / "state").mkdir(parents=True)
        (stage / "files").mkdir()
        (stage / "STARTED").write_text("started\n")
        state = {"version": sedan.STATE_VERSION, "with_dst": False, "member_count": 2,
                 "total_declared": 96, "members": {}}
        (stage / "state" / "state.json").write_text(json.dumps(state))
        return stage

    def dead_pid(self):
        finished = subprocess.Popen(["true"])
        finished.wait()
        return finished.pid

    def test_recover_removes_stale_stage_lock(self):
        root, _members = self.prepare_extract_env()
        stage = self.manual_stage(root)
        lock_path = stage / "state" / "lock"
        lock_path.write_text(f"{self.dead_pid()} 2026-11-03T12:00:00.000-05:00\n")
        code, output = self.run_main("--recover-lock", str(root))
        self.assertEqual(sedan.EXIT_OK, code, output)
        self.assertIn("stale lock removed", output)
        self.assertFalse(lock_path.exists())
        self.assertTrue((stage / "STARTED").exists())

    def test_recover_refuses_live_lock(self):
        root, _members = self.prepare_extract_env()
        stage = self.manual_stage(root)
        lock_path = stage / "state" / "lock"
        lock_path.write_text(f"{os.getpid()} 2026-11-03T12:00:00.000-05:00\n")
        code, output = self.run_main("--recover-lock", str(root))
        self.assertEqual(sedan.EXIT_STATE, code)
        self.assertIn("refusing to remove", output)
        self.assertTrue(lock_path.exists())

    def test_recover_without_stage_is_a_no_op(self):
        root, _members = self.prepare_extract_env()
        code, output = self.run_main("--recover-lock", str(root))
        self.assertEqual(sedan.EXIT_OK, code, output)
        self.assertIn("nothing to recover", output)


class CliTests(ExtractTestBase):
    def test_mode_flags_are_mutually_exclusive(self):
        root = self.tmp_dir()
        for argv in (("--extract", "--recover-lock"), ("--extract", "--verify-archives"),
                     ("--verify-archives", "--recover-lock")):
            with self.subTest(flags=argv):
                with self.assertRaises(SystemExit) as caught:
                    self.run_main(*argv, str(root))
                self.assertEqual(2, caught.exception.code)

    def test_with_dst_alone_is_a_valid_preflight(self):
        root = self.tmp_dir() / "cache"
        code, output = self.run_main("--with-dst", str(root))
        self.assertEqual(sedan.EXIT_GUARD, code)
        self.assertIn("33 pinned members", output)


if __name__ == "__main__":
    unittest.main()
