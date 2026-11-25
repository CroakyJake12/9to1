from pathlib import Path
import subprocess
import sys
from tempfile import TemporaryDirectory
import unittest
from uuid import uuid4

from .artifacts import CustodyError
from .v8_linux_process_owner import _read_process
from .v8_scoped_linux import ApprovedScopeLimits, _check_cgroup, _descends_from, scope_name, scoped_worker_argv


class OwnedScopeTests(unittest.TestCase):
    def setUp(self):
        self.attempt = str(uuid4())
        self.limits = ApprovedScopeLimits(768 * 1024**2, 32, 100, 10)

    def test_exact_named_user_scope_and_mandatory_kernel_limits(self):
        unit = scope_name(self.attempt, 1)
        argv = scoped_worker_argv(("/usr/bin/python3", "-I", "argument with spaces"), attempt=self.attempt,
                                  rank=1, limits=self.limits)
        self.assertEqual(argv[-4:], ("--", "/usr/bin/python3", "-I", "argument with spaces"))
        self.assertIn("--unit=" + unit, argv)
        self.assertIn("--user", argv)
        self.assertIn("--no-ask-password", argv)
        self.assertIn("--property=RuntimeMaxSec=10", argv)
        self.assertNotIn("sudo", argv)
        with TemporaryDirectory() as temp:
            folder = Path(temp) / "user.slice" / unit
            folder.mkdir(parents=True)
            (folder / "memory.max").write_text(str(self.limits.memory_bytes))
            (folder / "pids.max").write_text(str(self.limits.tasks))
            (folder / "cpu.max").write_text("100000 100000")
            cg = "/user.slice/" + unit
            self.assertEqual(_check_cgroup(cg, unit, limits=self.limits, cgroup_root=Path(temp)), cg)
            (folder / "cpu.max").write_text("max 100000")
            with self.assertRaisesRegex(CustodyError, "bounded"):
                _check_cgroup(cg, unit, limits=self.limits, cgroup_root=Path(temp))

    def test_actual_child_pid_is_not_launcher_or_unrelated_sibling(self):
        program = """
import subprocess,sys
p=subprocess.Popen([sys.executable,'-c','import time;time.sleep(3)'])
print(p.pid,flush=True)
sys.stdin.buffer.read(1)
p.terminate()
p.wait(timeout=1)
"""
        if not Path("/proc/self/stat").exists():
            self.skipTest("Linux /proc required")
        with subprocess.Popen([sys.executable, "-B", "-c", program], stdin=subprocess.PIPE,
                              stdout=subprocess.PIPE) as wrapper, subprocess.Popen(
                                  [sys.executable, "-B", "-c", "import time; time.sleep(3)"]) as sibling:
            try:
                child = int(wrapper.stdout.readline())
                origin = _read_process(wrapper.pid)
                self.assertNotEqual(wrapper.pid, child)
                self.assertTrue(_descends_from(child, origin))
                self.assertFalse(_descends_from(sibling.pid, origin))
            finally:
                wrapper.stdin.write(b"x")
                wrapper.stdin.flush()
                wrapper.wait(timeout=2)
                sibling.terminate()
                sibling.wait(timeout=2)
