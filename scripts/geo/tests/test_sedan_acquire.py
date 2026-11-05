"""Offline unit tests for scripts/geo/sedan_acquire.py (stdlib only, no network)."""

import contextlib
import email.message
import hashlib
import importlib.util
import io
import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace

spec = importlib.util.spec_from_file_location(
    "sedan_acquire", Path(__file__).resolve().parents[1] / "sedan_acquire.py")
sedan = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sedan)


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


class SedanAcquireTests(unittest.TestCase):
    def patch(self, name, value):
        original = getattr(sedan, name)
        setattr(sedan, name, value)
        self.addCleanup(setattr, sedan, name, original)

    def patch_statvfs(self, free_bytes):
        block = 4096
        self.patch("statvfs", lambda path: SimpleNamespace(
            f_bavail=free_bytes // block, f_frsize=block, f_bsize=block))

    def patch_ample_space(self):
        self.patch_statvfs(1 << 45)

    def tmp_dir(self):
        parent = "/tmp/opencode" if os.path.isdir("/tmp/opencode") else None
        context = tempfile.TemporaryDirectory(prefix="sedan-acquire-test-", dir=parent)
        self.addCleanup(context.cleanup)
        return Path(context.name)

    def run_main(self, *argv):
        buffer = io.StringIO()
        with contextlib.redirect_stdout(buffer):
            code = sedan.main(list(argv))
        return code, buffer.getvalue()

    def fetch(self, handler):
        calls = []

        def fake_open_url(url, method="GET", headers=None):
            record = {"url": url, "method": method, "headers": dict(headers or {})}
            calls.append(record)
            return handler(record)

        fake_open_url.calls = calls
        self.patch("open_url", fake_open_url)
        return fake_open_url

    def fetch_never(self):
        def handler(record):
            raise AssertionError(f"unexpected network call: {record}")

        return self.fetch(handler)

    def tiny_parts(self, sizes_first, sizes_second=None):
        products = [sedan.Product(resource="TST", subresource="TST_A", part_sizes=tuple(sizes_first))]
        if sizes_second:
            products.append(sedan.Product(resource="TST", subresource="TST_B", part_sizes=tuple(sizes_second)))
        self.patch("PRODUCTS", tuple(products))
        return sedan.build_parts()

    def part_file(self, workdir, part, data):
        path = Path(workdir) / part.rel_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        return path

    def partial_entry(self, workdir, part, data):
        path = self.part_file(workdir, part, data)
        return {
            "url": part.url,
            "expected_size": part.expected_size,
            "bytes_on_disk": len(data),
            "status": "partial",
            "mtime_ns": os.lstat(path).st_mtime_ns,
        }

    def complete_entry(self, workdir, part, data):
        path = self.part_file(workdir, part, data)
        return {
            "url": part.url,
            "expected_size": part.expected_size,
            "bytes_on_disk": len(data),
            "status": "complete",
            "mtime_ns": os.lstat(path).st_mtime_ns,
            "sha256": hashlib.sha256(data).hexdigest(),
        }

    def write_state(self, workdir, products):
        state_path = Path(workdir) / sedan.STATE_FILE_REL
        state_path.parent.mkdir(parents=True, exist_ok=True)
        sedan.write_state(str(state_path), {"version": sedan.STATE_VERSION, "products": products})
        return state_path


class GuardTests(SedanAcquireTests):
    def test_missing_mount_entry_fails_before_any_mkdir(self):
        root = self.tmp_dir()
        prefix = root / "mnt" / "pit2"
        (root / "mnt").mkdir()
        self.patch("DEFAULT_PREFIX", str(prefix))
        mounts = root / "mounts.txt"
        mounts.write_text("sysfs /sys sysfs rw,nosuid,nodev,noexec,relatime 0 0\n")
        self.patch("PROC_MOUNTS_PATH", str(mounts))
        workdir = prefix / "panopticon" / "geo-proof" / "sedan"
        code, output = self.run_main("--download-only", str(workdir))
        self.assertEqual(sedan.EXIT_GUARD, code)
        self.assertIn("rw mount", output)
        self.assertIn(str(prefix), output)
        self.assertFalse(workdir.exists())

    def test_readonly_mount_entry_fails_closed(self):
        root = self.tmp_dir()
        prefix = root / "mnt" / "pit2"
        (root / "mnt").mkdir()
        self.patch("DEFAULT_PREFIX", str(prefix))
        mounts = root / "mounts.txt"
        mounts.write_text(f"/dev/sdz {prefix} ext4 ro,relatime 0 0\n")
        self.patch("PROC_MOUNTS_PATH", str(mounts))
        workdir = prefix / "panopticon" / "geo-proof" / "sedan"
        code, output = self.run_main("--download-only", str(workdir))
        self.assertEqual(sedan.EXIT_GUARD, code)
        self.assertIn("rw mount", output)
        self.assertFalse(workdir.exists())

    def test_rw_mount_entry_passes_mount_guard(self):
        root = self.tmp_dir()
        prefix = root / "mnt" / "pit2"
        (root / "mnt").mkdir()
        self.patch("DEFAULT_PREFIX", str(prefix))
        mounts = root / "mounts.txt"
        mounts.write_text(f"/dev/sdz {prefix} ext4 rw,relatime 0 0\n")
        self.patch("PROC_MOUNTS_PATH", str(mounts))
        self.patch_ample_space()
        workdir = prefix / "panopticon" / "geo-proof" / "sedan"
        code, output = self.run_main("--preflight", str(workdir))
        self.assertEqual(sedan.EXIT_OK, code)
        self.assertIn("mount realness verified", output)
        self.assertFalse(workdir.exists())

    @unittest.skipIf(os.geteuid() == 0, "os.access always grants root")
    def test_ancestor_unwritable_fails_before_any_mkdir(self):
        root = self.tmp_dir()
        ancestor = root / "cache"
        ancestor.mkdir()
        os.chmod(ancestor, 0o555)
        self.addCleanup(os.chmod, ancestor, 0o755)
        self.patch_ample_space()
        workdir = ancestor / "work"
        code, output = self.run_main("--download-only", str(workdir))
        self.assertEqual(sedan.EXIT_GUARD, code)
        self.assertIn("not writable", output)
        self.assertFalse(workdir.exists())

    def test_low_free_space_fails_closed(self):
        root = self.tmp_dir()
        required = sedan.TOTAL_PINNED_BYTES + sedan.RESERVE_BYTES
        self.patch_statvfs(required - 1000)
        free = ((required - 1000) // 4096) * 4096
        workdir = root / "work"
        code, output = self.run_main("--download-only", str(workdir))
        self.assertEqual(sedan.EXIT_GUARD, code)
        self.assertIn("insufficient free space", output)
        self.assertIn(f"free={free}", output)
        self.assertIn(f"required={required}", output)
        self.assertIn(f"remaining={required - free}", output)
        self.assertFalse(workdir.exists())

    def test_preflight_writes_nothing_and_touches_no_network(self):
        root = self.tmp_dir()
        self.patch_ample_space()
        self.fetch_never()
        workdir = root / "work"
        code, output = self.run_main("--preflight", str(workdir))
        self.assertEqual(sedan.EXIT_OK, code)
        self.assertIn("mount realness NOT verified", output)
        self.assertIn("preflight complete", output)
        self.assertFalse(workdir.exists())


class PathSafetyTests(SedanAcquireTests):
    def test_regular_file_workdir_rejected(self):
        root = self.tmp_dir()
        target = root / "notadir"
        target.write_text("x")
        code, output = self.run_main("--download-only", str(target))
        self.assertEqual(sedan.EXIT_GUARD, code)
        self.assertIn("regular file", output)
        self.assertEqual(["notadir"], sorted(p.name for p in root.iterdir()))

    def test_dotdot_component_rejected(self):
        root = self.tmp_dir()
        workdir = str(root / "a" / ".." / "escape")
        code, output = self.run_main("--download-only", workdir)
        self.assertEqual(sedan.EXIT_GUARD, code)
        self.assertIn("'..'", output)
        self.assertFalse((root / "escape").exists())
        self.assertFalse((root / "a").exists())

    def test_root_path_rejected(self):
        code, output = self.run_main("--download-only", "/")
        self.assertEqual(sedan.EXIT_GUARD, code)
        self.assertIn("filesystem root", output)


class DownloadTests(SedanAcquireTests):
    def setUp(self):
        self.root = self.tmp_dir()
        self.workdir = self.root / "work"
        self.patch_ample_space()

    def head_ok(self, size):
        return FakeResponse(200, message({"Content-Length": size}))

    def test_head_size_drift_fails_without_download(self):
        part = self.tiny_parts([64])[0]
        fetch = self.fetch(lambda record: self.head_ok(part.expected_size + 1))
        code, output = self.run_main("--download-only", str(self.workdir))
        self.assertEqual(sedan.EXIT_STATE, code)
        self.assertIn("drift", output)
        self.assertIn("never upgrades", output)
        self.assertEqual(["HEAD"], [call["method"] for call in fetch.calls])
        self.assertFalse((self.workdir / "archives").exists())

    def test_partial_resume_appends_and_completes(self):
        part = self.tiny_parts([64])[0]
        prefix = bytes(range(40))
        self.write_state(self.workdir, {part.key: self.partial_entry(self.workdir, part, prefix)})
        remainder = bytes(range(40, 64))

        def handler(record):
            if record["method"] == "HEAD":
                return self.head_ok(part.expected_size)
            self.assertEqual("GET", record["method"])
            self.assertEqual({"Range": "bytes=40-"}, record["headers"])
            return FakeResponse(206, message({"Content-Range": f"bytes 40-63/{part.expected_size}"}), remainder)

        fetch = self.fetch(handler)
        code, output = self.run_main("--download-only", str(self.workdir))
        self.assertEqual(sedan.EXIT_OK, code)
        path = self.workdir / part.rel_path
        self.assertEqual(bytes(range(64)), path.read_bytes())
        state = json.loads((self.workdir / sedan.STATE_FILE_REL).read_text())
        entry = state["products"][part.key]
        self.assertEqual("complete", entry["status"])
        self.assertEqual(hashlib.sha256(bytes(range(64))).hexdigest(), entry["sha256"])
        self.assertEqual(64, entry["bytes_on_disk"])
        self.assertEqual(["HEAD", "GET"], [call["method"] for call in fetch.calls])

    def test_completed_parts_never_redownloaded(self):
        first, second = self.tiny_parts([32], [16])
        data_first = bytes(range(32))
        data_second = bytes(range(16))
        self.write_state(self.workdir, {
            first.key: self.complete_entry(self.workdir, first, data_first),
            second.key: self.complete_entry(self.workdir, second, data_second),
        })
        before = {
            part.key: os.stat(self.workdir / part.rel_path).st_mtime_ns
            for part in (first, second)
        }
        fetch = self.fetch_never()
        code, output = self.run_main("--download-only", str(self.workdir))
        self.assertEqual(sedan.EXIT_OK, code)
        self.assertEqual([], fetch.calls)
        self.assertIn("sha256 ok", output)
        for part in (first, second):
            self.assertEqual(before[part.key], os.stat(self.workdir / part.rel_path).st_mtime_ns)

    def test_operator_collision_without_state_fails_untouched(self):
        part = self.tiny_parts([64])[0]
        operator_bytes = b"OPERATOR INPUT DATA"
        self.part_file(self.workdir, part, operator_bytes)
        self.fetch_never()
        code, output = self.run_main("--download-only", str(self.workdir))
        self.assertEqual(sedan.EXIT_STATE, code)
        self.assertIn("collision", output)
        self.assertEqual(operator_bytes, (self.workdir / part.rel_path).read_bytes())
        self.assertFalse((self.workdir / sedan.STATE_FILE_REL).exists())

    def test_corrupted_and_wrong_version_state_fail_without_adoption(self):
        part = self.tiny_parts([64])[0]
        owned = bytes(range(10))
        cases = (
            ("bad-json", "{not json"),
            ("bad-version", json.dumps({"version": 99, "products": {}})),
        )
        for name, payload in cases:
            with self.subTest(payload=payload):
                workdir = self.root / name
                self.part_file(workdir, part, owned)
                state_path = Path(workdir) / sedan.STATE_FILE_REL
                state_path.parent.mkdir(parents=True, exist_ok=True)
                state_path.write_text(payload)
                fetch = self.fetch_never()
                code, output = self.run_main("--download-only", str(workdir))
                self.assertEqual(sedan.EXIT_STATE, code)
                self.assertIn("state", output)
                self.assertEqual([], fetch.calls)
                self.assertEqual(owned, (workdir / part.rel_path).read_bytes())
                self.assertEqual(payload, state_path.read_text())

    def test_midstream_interruption_preserves_partial_and_exits_3(self):
        part = self.tiny_parts([64])[0]
        prefix = bytes(range(40))
        served = bytes(range(40, 50))
        self.write_state(self.workdir, {part.key: self.partial_entry(self.workdir, part, prefix)})

        def handler(record):
            if record["method"] == "HEAD":
                return self.head_ok(part.expected_size)
            return DyingResponse(message({"Content-Range": f"bytes 40-63/{part.expected_size}"}), served)

        fetch = self.fetch(handler)
        code, output = self.run_main("--download-only", str(self.workdir))
        self.assertEqual(sedan.EXIT_NETWORK, code)
        self.assertIn("network error", output)
        path = self.workdir / part.rel_path
        self.assertEqual(bytes(range(50)), path.read_bytes())
        state = json.loads((self.workdir / sedan.STATE_FILE_REL).read_text())
        entry = state["products"][part.key]
        self.assertEqual("partial", entry["status"])
        self.assertEqual(50, entry["bytes_on_disk"])
        self.assertEqual(entry["mtime_ns"], os.lstat(path).st_mtime_ns)
        self.assertEqual(["HEAD", "GET"], [call["method"] for call in fetch.calls])

    def test_http_401_fails_closed_without_credentials_or_retries(self):
        part = self.tiny_parts([64])[0]

        def handler(record):
            if record["method"] == "HEAD":
                return self.head_ok(part.expected_size)
            return FakeResponse(401, message({"Content-Length": 64}))

        fetch = self.fetch(handler)
        code, output = self.run_main("--download-only", str(self.workdir))
        self.assertEqual(sedan.EXIT_NETWORK, code)
        self.assertIn("no IGN account", output)
        self.assertEqual(["HEAD", "GET"], [call["method"] for call in fetch.calls])
        for call in fetch.calls:
            self.assertNotIn("Authorization", call["headers"])
            self.assertNotIn("authorization", call["headers"])

    def test_overlong_body_stops_at_cap_and_fails_as_drift(self):
        part = self.tiny_parts([64])[0]
        body = bytes(range(100))

        def handler(record):
            if record["method"] == "HEAD":
                return self.head_ok(part.expected_size)
            return FakeResponse(200, message({"Content-Length": part.expected_size}), body)

        self.fetch(handler)
        code, output = self.run_main("--download-only", str(self.workdir))
        self.assertEqual(sedan.EXIT_STATE, code)
        self.assertIn("drift", output)
        self.assertIn("stopped at the cap", output)
        path = self.workdir / part.rel_path
        self.assertEqual(part.expected_size, path.stat().st_size)
        state = json.loads((self.workdir / sedan.STATE_FILE_REL).read_text())
        self.assertEqual("partial", state["products"][part.key]["status"])


class LockTests(SedanAcquireTests):
    def setUp(self):
        self.root = self.tmp_dir()
        self.workdir = self.root / "work"
        self.patch_ample_space()
        (Path(self.workdir) / "state").mkdir(parents=True)

    def write_lock(self, pid):
        lock_path = Path(self.workdir) / sedan.LOCK_FILE_REL
        lock_path.write_text(f"{pid} 2026-11-02T21:00:00.000-05:00\n")
        return lock_path

    def test_stale_lock_requires_recover_then_recovers(self):
        part = self.tiny_parts([64])[0]
        finished = subprocess.Popen(["true"])
        finished.wait()
        lock_path = self.write_lock(finished.pid)
        self.fetch_never()
        code, output = self.run_main("--download-only", str(self.workdir))
        self.assertEqual(sedan.EXIT_STATE, code)
        self.assertIn("--recover-lock", output)
        self.assertTrue(lock_path.exists())
        code, output = self.run_main("--recover-lock", str(self.workdir))
        self.assertEqual(sedan.EXIT_OK, code)
        self.assertFalse(lock_path.exists())
        code, output = self.run_main("--preflight", str(self.workdir))
        self.assertEqual(sedan.EXIT_OK, code)

    def test_live_lock_blocks_download_and_recover_refuses(self):
        self.tiny_parts([64])
        lock_path = self.write_lock(os.getpid())
        fetch = self.fetch_never()
        code, output = self.run_main("--download-only", str(self.workdir))
        self.assertEqual(sedan.EXIT_STATE, code)
        self.assertIn("live pid", output)
        code, output = self.run_main("--recover-lock", str(self.workdir))
        self.assertEqual(sedan.EXIT_STATE, code)
        self.assertIn("refusing to remove", output)
        self.assertTrue(lock_path.exists())
        self.assertEqual([], fetch.calls)


if __name__ == "__main__":
    unittest.main()
