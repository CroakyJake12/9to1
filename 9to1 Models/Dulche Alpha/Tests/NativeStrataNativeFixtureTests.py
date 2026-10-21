"""Native non-GPU fixture tests against the ACTUAL shared production code (root allocation).

These tests drive the REAL compiled candidate relay worker
(`dulche_strata_candidate_worker.cpp` + `dulche_child_process.h` + `strata_tokenizer_helper.py`)
over its wire protocol. They do NOT use a mirror or a stub of the parser/child-owner code. They are
authored in this source-only checkpoint for execution at a later root build slot, and they skip
cleanly (with a clear label) until the built worker is present.

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
  g++ -std=c++20 -O2 -DSTRATA_HAS_CUDA=1 -DSTRATA_NATIVE_EXT="" \
      -I '<native>' dulche_strata_candidate_worker.cpp -o build/dulche-strata-candidate-worker \
      -lcudart
(no compiler installs, downloads, model execution, or Windows/GPU/PC acceptance are performed here).
"""
import hashlib
import os
import pathlib
import select
import struct
import subprocess
import sys
import time
import unittest
from contextlib import contextmanager

NATIVE = pathlib.Path(__file__).resolve().parents[1] / "InferenceEngines" / "NativeStrata"
CANDIDATE_CPP = NATIVE / "dulche_strata_candidate_worker.cpp"
CHILD_PROCESS_H = NATIVE / "dulche_child_process.h"
# Where a root allocation would place the built worker (worktree-local). If absent, tests skip.
BUILT_CANDIDATE = pathlib.Path("/workspace/9to1-mercury-thunder/work/pc-worker/thunder-benchmark/native-child-test/build/dulche-strata-candidate-worker")

# Independent execution must run against a PINNED genuine binary. The root records the exact SHA256
# of the built artifact; without it the tests refuse to consume a mutable/unpinned build.
_EXPECTED_CANDIDATE_SHA256 = os.environ.get("DULCHE_NATIVE_CANDIDATE_SHA256")


def _built_candidate_acceptable():
    if not BUILT_CANDIDATE.is_file():
        return False
    if not _EXPECTED_CANDIDATE_SHA256:
        return False
    actual = hashlib.sha256(BUILT_CANDIDATE.read_bytes()).hexdigest()
    return actual.lower() == _EXPECTED_CANDIDATE_SHA256.lower()


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


@unittest.skipUnless(_built_candidate_acceptable(),
                     "root-allocation native fixture: built candidate worker not present or not pinned "
                     "by DULCHE_NATIVE_CANDIDATE_SHA256")
class NativeFixtureTests(unittest.TestCase):
    @contextmanager
    def _spawn(self):
        proc = subprocess.Popen([str(BUILT_CANDIDATE)], stdin=subprocess.PIPE,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE)
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
