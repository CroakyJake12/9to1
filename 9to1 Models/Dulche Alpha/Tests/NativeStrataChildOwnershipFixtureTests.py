"""Child-ownership fixture tests for the ACTUAL shared production header.

These tests guard the real `dulche_child_process.h` ownership/teardown contract (Thunder pass-5
Priority 3). They are SOURCE-SCAN custody checks in this checkpoint; the synchronized C++ fixture
(`NativeStrataChildOwnershipFixture.cpp`) is authored against the real header but is NOT compiled or
run here (no root portable-component compile allocation was carried into this dispatch). It is
labelled NOT RUN until a root allocation builds and runs it with one compiler process.
"""
import os
import pathlib
import unittest

NATIVE = pathlib.Path(__file__).resolve().parents[1] / "InferenceEngines" / "NativeStrata"
HEADER = NATIVE / "dulche_child_process.h"
FIXTURE = pathlib.Path(__file__).resolve().parent / "NativeStrataChildOwnershipFixture.cpp"

# Only a root allocation enables compilation/execution of the C++ fixture (one compiler process,
# short timeout). Otherwise it stays NOT RUN.
FIXTURE_ALLOWED = os.environ.get("DULCHE_CHILD_FIXTURE_ALLOWED") == "1"


def header_src():
    return HEADER.read_text(encoding="utf-8")


class HeaderCustodyTests(unittest.TestCase):
    def test_header_and_fixture_exist(self):
        self.assertTrue(HEADER.exists())
        self.assertTrue(FIXTURE.exists())

    def test_poll_based_stderr_drain_with_wake_pipe(self):
        src = header_src()
        # The drain must poll and own a wake pipe so stop() unblocks a blocked read deterministically.
        self.assertIn("::poll(pfd, 2, 200)", src)
        self.assertIn("_wake[2]", src)
        self.assertIn("void wake()", src)
        self.assertIn("entered_blocked", src)

    def test_process_group_kill_via_setpgid(self):
        src = header_src()
        self.assertIn("::setpgid(0, 0)", src)
        self.assertIn("::setpgid(pid, pid)", src)
        self.assertIn("::kill(-_pgid, SIGKILL)", src)

    def test_exec_failure_handshake_status_pipe(self):
        src = header_src()
        self.assertIn("status_pipe", src)
        self.assertIn("FD_CLOEXEC", src)
        self.assertIn("_exit(127)", src)
        self.assertIn("exec_failed", src)

    def test_teardown_order_kill_before_join_drain_before_reap(self):
        src = header_src()
        kill_pos = src.find("if (!_reaped) kill();")
        join_pos = src.find("_stderr.stop_and_join();")
        reap_pos = src.find("_wait_reap_bounded();")
        self.assertGreaterEqual(kill_pos, 0, "kill must come first in shutdown()")
        self.assertGreaterEqual(join_pos, kill_pos, "drain join must follow the kill")
        self.assertGreaterEqual(reap_pos, join_pos, "exact reap must follow the drain join")

    def test_partial_pipe_acquisition_cleanup(self):
        src = header_src()
        self.assertIn("::close(in_pipe[0]); ::close(in_pipe[1]); return false;", src)
        self.assertIn("::close(status_pipe[0]); ::close(status_pipe[1]);", src)

    def test_windows_source_compile_boundaries(self):
        src = header_src()
        # POSIX-only spawn must be excluded on Windows; Windows method must be callable.
        self.assertIn("#if !defined(_WIN32)", src)
        self.assertIn("return _spawn_windows(spec);", src)
        self.assertIn("ToWide", src)
        # The undersized MultiByteToWideChar capacity bug must be gone.
        self.assertNotIn("w.resize(::MultiByteToWideChar", src)
        self.assertIn("MB_ERR_INVALID_CHARS", src)


@unittest.skipUnless(FIXTURE_ALLOWED, "child-ownership C++ fixture: NOT RUN (no root compile allocation this dispatch)")
class ChildOwnershipFixtureRunTests(unittest.TestCase):
    """Runs the synchronized C++ fixture against the real header. Only executed when a root
    allocation explicitly enables it (one compiler process, short timeout)."""

    def test_fixture_runs_clean(self):
        import subprocess
        import sys
        import tempfile
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            out = pathlib.Path(tmp) / "child-ownership-fixture"
            build = subprocess.run(
                ["g++", "-std=c++20", "-O1", "-pthread", "-o", str(out), str(FIXTURE)],
                capture_output=True, text=True)
            self.assertEqual(build.returncode, 0, build.stderr)
            run = subprocess.run([str(out)], capture_output=True, text=True, timeout=60)
            self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
            self.assertIn("failures=0", run.stdout)


if __name__ == "__main__":
    unittest.main()
