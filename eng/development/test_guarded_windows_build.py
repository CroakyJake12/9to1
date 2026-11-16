"""Small Windows-only lifecycle tests for guarded_windows_build; no SDK is run."""
from __future__ import annotations

import os
from pathlib import Path
import ctypes
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from unittest import mock

if os.name == "nt":
    import guarded_windows_build as guard
else:  # Keep test discovery on other hosts import-safe.
    guard = None


@unittest.skipUnless(os.name == "nt", "Windows Job Objects and named mutexes are required")
class GuardedWindowsBuildTests(unittest.TestCase):
    def setUp(self):
        # Derive the repository's parent from this maintained eng/development
        # location; no checkout ancestor must be named "Codex". Keeping the
        # fixture beside the checkout also avoids redirected Windows AppData.
        workspace = Path(__file__).resolve().parents[2].parent
        self.temp = tempfile.TemporaryDirectory(prefix="9to1-windows-guard-test-", dir=workspace)
        self.root = Path(self.temp.name)
        self.repo = self.root / "repo"
        self.repo.mkdir()
        self._git("init", "--quiet")
        self._git("config", "user.name", "Guard fixture")
        self._git("config", "user.email", "guard@example.invalid")
        (self.repo / "tracked.txt").write_text("fixture\n", encoding="utf-8")
        self._git("add", "tracked.txt")
        self._git("commit", "--quiet", "-m", "fixture")
        self.head = self._git("rev-parse", "HEAD").strip()
        self.outputs: list[Path] = []

    def tearDown(self):
        for path in self.outputs:
            shutil.rmtree(path, ignore_errors=True)
        self.temp.cleanup()

    def _git(self, *args: str) -> str:
        return subprocess.run(["git", "-C", str(self.repo), *args], check=True,
                              capture_output=True, text=True, timeout=20).stdout

    def _run(self, *args: str, timeout: int = 20):
        result = guard.run_guarded(repo=self.repo, expected_head=self.head, cwd=self.repo,
            executable=sys.executable, allowed_executables=[sys.executable],
            command=[sys.executable, *args], timeout_seconds=timeout,
            memory_floor_mib=64, job_cap_mib=512, disk_reserve_mib=1,
            test_mode=True, poll_seconds=0.05)
        if result.get("output_dir"):
            self.outputs.append(Path(result["output_dir"]))
        return result

    def _still_running(self, pid: int) -> bool:
        handle = guard.kernel32.OpenProcess(guard.PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
        if not handle:
            return False
        guard.kernel32.CloseHandle(handle)
        return True

    def _fake_dotnet_process(self, executable: Path, job=None, suspended: bool = False):
        startup = guard.STARTUPINFOW()
        startup.cb = ctypes.sizeof(startup)
        info = guard.PROCESS_INFORMATION()
        argv = [str(executable), "-c", "import time; time.sleep(30)"]
        command_line = ctypes.create_unicode_buffer(subprocess.list2cmdline(argv))
        flags = guard.CREATE_SUSPENDED | guard.CREATE_NEW_PROCESS_GROUP | 0x08000000 | guard.CREATE_UNICODE_ENVIRONMENT
        self.assertTrue(guard.kernel32.CreateProcessW(str(executable), command_line, None, None, False,
            flags, None, str(self.root), ctypes.byref(startup), ctypes.byref(info)))
        if job is not None:
            self.assertTrue(guard.kernel32.AssignProcessToJobObject(job, info.hProcess))
        if not suspended:
            self.assertNotEqual(0xFFFFFFFF, guard.kernel32.ResumeThread(info.hThread))
            guard.kernel32.CloseHandle(info.hThread)
            return info.hProcess, None
        return info.hProcess, info.hThread

    def test_zero_and_nonzero_exit_are_recorded(self):
        ok = self._run("-c", "print('fixture-ok')")
        self.assertEqual("OK", ok["status"])
        self.assertEqual(0, ok["exit_code"])
        self.assertEqual(self.head, ok["source_sha"])
        self.assertIn("fixture-ok", Path(ok["log_path"]).read_text(encoding="utf-8"))
        failed = self._run("-c", "raise SystemExit(7)")
        self.assertEqual("FAILED", failed["status"])
        self.assertEqual(7, failed["exit_code"])

    def test_negative_copy_budget_refuses_before_output_or_process_creation(self):
        with mock.patch.object(guard, "_new_private_dir") as output, \
             mock.patch.object(guard.kernel32, "CreateProcessW") as spawn, \
             mock.patch.object(guard, "_git") as git_read:
            result = guard.run_guarded(repo=self.repo, expected_head=self.head, cwd=self.repo,
                executable=sys.executable, allowed_executables=[sys.executable],
                command=[sys.executable, "-c", "raise SystemExit('must not run')"],
                copy_budget_mib=-guard.DEFAULT_DISK_RESERVE_MIB)
        self.assertEqual("BLOCKED", result["status"])
        self.assertIn("copy budget must be nonnegative", result["reason"])
        output.assert_not_called()
        spawn.assert_not_called()
        git_read.assert_not_called()

    def test_timeout_closes_only_owned_process_tree(self):
        pid_file = self.root / "grandchild.pid"
        child = "import subprocess,sys,time; p=subprocess.Popen([sys.executable,'-c','import time; time.sleep(60)']); open(sys.argv[1],'w').write(str(p.pid)); time.sleep(60)"
        result = self._run("-c", child, str(pid_file), timeout=2)
        self.assertIn("timeout exceeded", result.get("reason", ""))
        self.assertTrue(pid_file.exists())
        pid = int(pid_file.read_text(encoding="utf-8"))
        deadline = time.monotonic() + 5
        while self._still_running(pid) and time.monotonic() < deadline:
            time.sleep(0.05)
        self.assertFalse(self._still_running(pid), "job close must terminate the assigned descendant")

    def test_source_change_invalidates_successful_child(self):
        changed = self.repo / "created-by-child.txt"
        script = "from pathlib import Path; Path(r'" + str(changed) + "').write_text('changed')"
        result = self._run("-c", script)
        self.assertEqual("INVALIDATED: repository source changed during execution", result["status"])

    def test_dirty_but_unchanged_source_snapshot_is_allowed(self):
        (self.repo / "tracked.txt").write_text("edited before build\n", encoding="utf-8")
        (self.repo / "new-source.txt").write_text("untracked before build\n", encoding="utf-8")
        result = self._run("-c", "print('unchanged-dirty-source')")
        self.assertEqual("OK", result["status"])
        self.assertTrue(result["source_dirty_before"])
        self.assertTrue(result["source_dirty_after"])
        self.assertEqual(result["source_identity"], result["source_identity_after"])

    def test_tracked_source_change_invalidates_snapshot(self):
        changed = self.repo / "tracked.txt"
        script = "from pathlib import Path; Path(r'" + str(changed) + "').write_text('changed during build')"
        result = self._run("-c", script)
        self.assertEqual("INVALIDATED: repository source changed during execution", result["status"])

    def test_dotnet_build_is_serial_and_preserves_project_output_layout(self):
        command, operation = guard._prepare_command(
            [r"C:\Program Files\dotnet\dotnet.exe", "build", "--no-restore"])
        self.assertEqual("build", operation)
        self.assertIn("-m:1", command)
        self.assertIn("-p:UseSharedCompilation=false", command)
        self.assertIn("-nodeReuse:false", command)
        self.assertNotIn("--artifacts-path", command)
        self.assertFalse(any(arg.startswith(("-p:BaseOutputPath", "-p:BaseIntermediateOutputPath",
                                             "-p:ArtifactsPath")) for arg in command))

    def test_dotnet_run_and_restore_are_refused(self):
        exe = r"C:\Program Files\dotnet\dotnet.exe"
        with self.assertRaises(guard.Refusal):
            guard._prepare_command([exe, "run"])
        with self.assertRaises(guard.Refusal):
            guard._prepare_command([exe, "restore"])

    def test_owned_sdk_image_is_not_foreign_but_unassigned_one_is(self):
        fake_dotnet = self.root / "dotnet.exe"
        shutil.copy2(sys.executable, fake_dotnet)
        runtime_dll = Path(sys.executable).with_name(f"python{sys.version_info.major}{sys.version_info.minor}.dll")
        if runtime_dll.exists():
            shutil.copy2(runtime_dll, self.root / runtime_dll.name)
        job = guard.kernel32.CreateJobObjectW(None, None)
        self.assertTrue(job)
        limits = guard.JOBOBJECT_EXTENDED_LIMIT_INFORMATION()
        limits.BasicLimitInformation.LimitFlags = guard.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        self.assertTrue(guard.kernel32.SetInformationJobObject(job,
            guard.JOB_OBJECT_EXTENDED_LIMIT_INFORMATION, ctypes.byref(limits), ctypes.sizeof(limits)))
        owned = owned_thread = foreign = None
        try:
            owned, owned_thread = self._fake_dotnet_process(fake_dotnet, job, suspended=True)
            with mock.patch.object(guard, "_query_process_image_name", return_value=(None, 5)) as image_query:
                self.assertEqual([], guard._foreign_sdk_processes(job),
                                 "a suspended owned child must be classified before image initialization")
                image_query.assert_not_called()
            foreign, _ = self._fake_dotnet_process(fake_dotnet)
            self.assertEqual(["dotnet.exe"], guard._foreign_sdk_processes(job),
                             "an active SDK-named process outside the owned job remains a refusal")
        finally:
            guard.kernel32.CloseHandle(job)
            if owned:
                guard.kernel32.WaitForSingleObject(owned, 5000)
                guard.kernel32.CloseHandle(owned)
            if owned_thread:
                guard.kernel32.CloseHandle(owned_thread)
            if foreign:
                guard.kernel32.TerminateProcess(foreign, 125)
                guard.kernel32.WaitForSingleObject(foreign, 5000)
                guard.kernel32.CloseHandle(foreign)

    def test_repository_mutex_refuses_overlap(self):
        result_holder = {}

        def first_run():
            script = "import pathlib,sys,time; pathlib.Path(sys.argv[1]).write_text('started'); time.sleep(2)"
            result_holder["first"] = self._run("-c", script, str(self.root / "started.flag"))

        worker = threading.Thread(target=first_run)
        worker.start()
        deadline = time.monotonic() + 10
        while not (self.root / "started.flag").exists() and time.monotonic() < deadline:
            time.sleep(0.05)
        self.assertTrue((self.root / "started.flag").exists(), "first guarded child did not start")
        second = self._run("-c", "print('must-not-overlap')")
        self.assertEqual("BLOCKED", second["status"])
        self.assertIn("lease", second.get("reason", ""))
        worker.join(timeout=10)
        self.assertFalse(worker.is_alive())
        self.assertEqual("OK", result_holder["first"]["status"])


if __name__ == "__main__":
    unittest.main()
