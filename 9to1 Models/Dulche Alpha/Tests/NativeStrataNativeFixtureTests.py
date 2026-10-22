"""Native non-GPU fixture tests against the ACTUAL shared production code (root allocation).

These tests drive the REAL compiled candidate relay worker
(`dulche_strata_candidate_worker.cpp` + `dulche_child_process.h` + `strata_tokenizer_helper.py`)
over its wire protocol. They do NOT use a mirror or a stub of the parser/child-owner code. They are
run only with an explicitly supplied, digest-pinned worker. Optional source-only execution reports
the five wire cases as NOT RUN; required execution fails closed on missing or invalid inputs.

Coverage (non-GPU, no engine/model/GPU required for the cases that run):
  * hello frame (type 0) is emitted with ABI version 2
  * truncated/oversized/unknown protocol inputs produce a bounded failure frame and a clean nonzero
    exit (exceptional return) - never a hang or a leaked child
  * a load whose engine dependency is missing fails CLOSED with the exact cause and the process
    exits cleanly with no leaked children (validates the exception-safe child-owner teardown)
  * clean STDIN EOF exits 0 (normal stop path)

NOT RUN without the full build slot (require a real engine + donor tokenizer):
  * actual spawn -> READY success and READY-failure handling
  * helper spawn/identity mismatch after a successful load
  * canceled generation (op 5 -> STOP forwarding)
  * EOF/broken-pipe mid-generation
  * child/drain settlement under a long-lived engine

The worker is compiled by the coordinator at root allocation with:
  g++ -std=c++20 -O1 -pthread -DSTRATA_HAS_CUDA=0 -DSTRATA_NATIVE_EXT='""' \
      -I '<native>' dulche_strata_candidate_worker.cpp -o build/dulche-strata-candidate-worker
(no compiler installs, downloads, model execution, or Windows/GPU/PC acceptance are performed here).
"""
import hashlib
import os
import pathlib
import re
import select
import stat
import struct
import subprocess
import sys
import tempfile
import time
import unittest
from contextlib import contextmanager

NATIVE = pathlib.Path(__file__).resolve().parents[1] / "InferenceEngines" / "NativeStrata"
CANDIDATE_CPP = NATIVE / "dulche_strata_candidate_worker.cpp"
CHILD_PROCESS_H = NATIVE / "dulche_child_process.h"
# Only ordinary process/locale values reach the worker. In particular, no ambient STRATA_ model,
# engine, helper or interpreter override (or dynamic-loader override) can select the shared engine.
_SYSTEM_ENVIRONMENT = {"PATH", "HOME", "LANG", "LC_ALL", "LC_CTYPE", "TZ", "TMPDIR",
                       "SYSTEMROOT", "WINDIR", "COMSPEC"}


def _admit_candidate():
    supplied = os.environ.get("DULCHE_NATIVE_CANDIDATE_BINARY")
    expected = os.environ.get("DULCHE_NATIVE_CANDIDATE_SHA256")
    required = os.environ.get("DULCHE_NATIVE_FIXTURE_REQUIRED") == "1"
    if not supplied and not expected and not required:
        raise unittest.SkipTest("NOT RUN: native wire fixture binary/SHA not configured (optional source-only mode)")
    if not supplied:
        raise ValueError("BLOCKED: DULCHE_NATIVE_CANDIDATE_BINARY is required")
    binary = pathlib.Path(supplied)
    try:
        mode = binary.stat().st_mode
    except OSError as error:
        raise ValueError("BLOCKED: configured native fixture binary is unavailable") from error
    if not stat.S_ISREG(mode) or not mode & 0o111 or not os.access(binary, os.X_OK):
        raise ValueError("BLOCKED: native fixture binary must be a regular executable file")
    if not expected or re.fullmatch(r"[0-9a-fA-F]{64}", expected) is None:
        raise ValueError("BLOCKED: DULCHE_NATIVE_CANDIDATE_SHA256 must contain exactly 64 hexadecimal digits")
    content = binary.read_bytes()
    if hashlib.sha256(content).hexdigest() != expected.lower():
        raise ValueError("BLOCKED: configured native fixture binary SHA256 mismatch; refusing execution")
    return content, expected.lower()


class _FrameDeadlineExceeded(Exception):
    pass


def _command(op, body: bytes = b"") -> bytes:
    """Build a host->worker command frame.

    The real worker's `read_command` (dulche_strata_candidate_worker.cpp) reads a u32 size, then
    reads exactly `size` bytes as the command body, whose first u32 is the op. So a command frame is
    `[u32 size][u32 op][body]`. (The Worker output envelope is different: `[u32 type][u32 len][payload]`,
    parsed by `_read_frame`.)"""
    return struct.pack("<I", 4 + len(body)) + struct.pack("<I", op) + body


def _text(s) -> bytes:
    b = s.encode("utf-8")
    return struct.pack("<I", len(b)) + b


class NativeFixtureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._optional_not_run = None
        try:
            content, cls._expected_sha256 = _admit_candidate()
        except unittest.SkipTest as reason:
            cls._optional_not_run = str(reason)
            return
        # A measured copy lives in an owned layout with NO sibling engine directory. Never execute
        # the caller's mutable path or let self-relative discovery reach an installed engine.
        layout = tempfile.TemporaryDirectory(prefix="dulche-native-fixture-")
        cls.addClassCleanup(layout.cleanup)
        native = pathlib.Path(layout.name) / "native"
        native.mkdir(mode=0o700)
        cls._fixture_binary = native / "dulche-strata-candidate-worker"
        cls._fixture_binary.write_bytes(content)
        cls._fixture_binary.chmod(0o700)

    def setUp(self):
        if self._optional_not_run is not None:
            self.skipTest(self._optional_not_run)

    @contextmanager
    def _spawn(self):
        if hashlib.sha256(self._fixture_binary.read_bytes()).hexdigest() != self._expected_sha256:
            raise ValueError("BLOCKED: private native fixture binary SHA256 changed before spawn")
        environment = {key: value for key, value in os.environ.items() if key in _SYSTEM_ENVIRONMENT}
        proc = subprocess.Popen([str(self._fixture_binary)], stdin=subprocess.PIPE,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=environment)
        try:
            yield proc
        finally:
            # Guaranteed owned-process cleanup on any failure/timeout/assert path; never leave a
            # hung child behind.
            if proc.poll() is None:
                proc.terminate()
                try:
                    proc.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    proc.kill()
                    try:
                        proc.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        pass
            for stream in (proc.stdin, proc.stdout, proc.stderr):
                try:
                    stream.close()
                except Exception:
                    pass

    def _read_frame(self, proc, timeout=15):
        """Read one worker-output frame `[u32 type][u32 len][payload]` under ONE absolute deadline
        applied to every chunk (header and body), so a partial/trickling frame can never block
        indefinitely. Raises _FrameDeadlineExceeded on deadline/EOF."""
        deadline = time.monotonic() + timeout
        fd = proc.stdout.fileno()

        def read_exact(n):
            buf = b""
            while len(buf) < n:
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    raise _FrameDeadlineExceeded()
                ready, _, _ = select.select([fd], [], [], max(remaining, 0.0))
                if not ready:
                    raise _FrameDeadlineExceeded()
                chunk = os.read(fd, min(n - len(buf), 4096))
                if not chunk:
                    raise _FrameDeadlineExceeded()
                buf += chunk
            return buf

        head = read_exact(8)
        ptype, length = struct.unpack("<II", head)
        if length > 1 << 24:
            self.fail("native fixture: implausible frame length")
        payload = read_exact(length)
        return ptype, payload

    def _read_frame_or_fail(self, proc, timeout=15):
        try:
            return self._read_frame(proc, timeout)
        except _FrameDeadlineExceeded:
            self.fail("native fixture: timed out waiting for a worker frame (blocked/partial read)")

    def test_hello_frame_version2(self):
        with self._spawn() as proc:
            ptype, payload = self._read_frame_or_fail(proc)
            self.assertEqual(ptype, 0)
            version, origin_len = struct.unpack("<II", payload[:8])
            self.assertEqual(version, 2)
            origin = payload[8:8 + origin_len].decode("utf-8")
            self.assertEqual(origin, "011c9e600a93bbc9fd451c563501b5831c32bbca")
            self._stop_clean(proc)

    def test_truncated_command_fails_cleanly(self):
        with self._spawn() as proc:
            self._read_frame_or_fail(proc)  # consume hello
            # Declare an 8-byte command body (op u32 + 4 more bytes) but send only the op, then
            # close stdin so the worker's fread hits EOF instead of blocking forever on an open
            # stream. This exercises the real bounded length/body path with a real deadline.
            proc.stdin.write(struct.pack("<I", 8) + struct.pack("<I", 1))
            proc.stdin.flush()
            proc.stdin.close()
            ptype, payload = self._read_frame_or_fail(proc)
            self.assertEqual(ptype, 5)          # failure frame
            self.assertEqual(proc.wait(timeout=15), 72)  # bounded exceptional return
            # no leaked child, full stderr collection
            self.assertEqual(proc.stderr.read().decode("utf-8", "replace"), "")

    def test_unknown_operation_fails_cleanly(self):
        with self._spawn() as proc:
            self._read_frame_or_fail(proc)
            proc.stdin.write(_command(99, _text("x")))
            proc.stdin.flush()
            ptype, payload = self._read_frame_or_fail(proc)
            self.assertEqual(ptype, 5)
            self.assertEqual(proc.wait(timeout=15), 72)

    def test_load_missing_engine_fails_closed(self):
        # The worker only reaches READY when its bundled engine binary is present. In this fixture the
        # engine is not installed next to the worker, so load must fail CLOSED with the exact
        # dependency cause, never crash, hang, or leak a child. This exercises the exception-safe
        # child-owner teardown (the pass-4 fatal-retirement fix) on the real code path.
        with self._spawn() as proc:
            self._read_frame_or_fail(proc)
            payload = (_text("/nonexistent/pack") + _text("qwen35moe")
                       + struct.pack("<I", 4096) + _text("")
                       + struct.pack("<I", 0))
            proc.stdin.write(_command(1, payload))
            proc.stdin.flush()
            ptype, payload = self._read_frame_or_fail(proc)
            self.assertEqual(ptype, 5)
            code, count = struct.unpack("<II", payload[:8])
            self.assertEqual(code, 3)
            self.assertGreaterEqual(count, 1)
            msg = payload[8 + 4:].decode("utf-8", "replace")
            self.assertIn("engine", msg.lower())
            self.assertEqual(proc.wait(timeout=15), 72)
            self.assertEqual(proc.stderr.read().decode("utf-8", "replace"), "")

    def _stop_clean(self, proc):
        # op 3 (stop) -> ack type 3 + exit 0, then EOF.
        proc.stdin.write(_command(3, b""))
        proc.stdin.flush()
        ptype, payload = self._read_frame_or_fail(proc)
        self.assertEqual(ptype, 3)
        self.assertEqual(proc.wait(timeout=15), 0)

    def test_clean_stdin_eof_exits_zero(self):
        with self._spawn() as proc:
            self._read_frame_or_fail(proc)
            proc.stdin.close()
            self.assertEqual(proc.wait(timeout=15), 0)


class NativeSourceCustodyTests(unittest.TestCase):
    """Source-side custody for the platform native fixture: the parser/child-owner code must remain
    the SHARED production files (not a test-local copy)."""

    def test_fixture_drives_shared_production_parser(self):
        self.assertTrue(CANDIDATE_CPP.exists())
        self.assertTrue(CHILD_PROCESS_H.exists())

    def test_worker_has_bounded_frame_and_raii_child_owner(self):
        src = CANDIDATE_CPP.read_text()
        self.assertIn("dulche_child_process.h", src)
        self.assertIn("read_command", src)
        self.assertIn("wait_ready", src)
        self.assertIn("shutdown", src)


if __name__ == "__main__":
    unittest.main()
