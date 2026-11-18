"""Disposable process/filesystem tests; actual X11/Haven is a separate CI receipt."""
from __future__ import annotations
import json
import os
from pathlib import Path
import stat
import tempfile
import unittest
from unittest import mock

import native_files_profile_reopen as target


class ReopenTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.private = self.root / "private"
        self.private.mkdir(mode=0o700)
        self.app = self.root / "app"
        self.app.mkdir()
        self.host = self.app / "Haven"
        self.host.write_text("#!/bin/sh\n"
                             "printf 'run\\n' >> \"$HOME/native-fixture-history\"\n"
                             "sleep 30\n")
        self.host.chmod(0o700)
        (self.app / "Haven.dll").write_bytes(b"fixture")
        (self.app / "Haven.runtimeconfig.json").write_text("{}")

    def run_test(self, **kwargs):
        return target.run_reopen(self.host, self.private,
                                 window_probe=lambda pid: ["0x1"], real_elf=False,
                                 timeout=2, stable=0.02, **kwargs)

    def test_twice_same_profile_and_fixture_app_writes_persist(self):
        result = self.run_test()
        self.assertEqual(result["status"], "PASS")
        self.assertEqual(result["attempts"], 2)
        self.assertTrue(result["independent_processes"])
        self.assertTrue(result["same_disposable_profile_in_both_launches"])
        self.assertEqual((self.private / "native-files-profile-reopen/profile/native-fixture-history").read_text(), "run\nrun\n")
        for i in (1, 2):
            path = self.private / f"native-files-profile-reopen/attempt-{i}.stderr.log"
            self.assertEqual(stat.S_IMODE(path.stat().st_mode), 0o600)

    def test_never_inherit_credentials(self):
        with mock.patch.dict(os.environ, {"GOOGLE_CLIENT_SECRET": "sensitive", "GITHUB_TOKEN": "other",
                                              "HAVEN_GOOGLE_DRIVE_CLIENT_ID": "secret-id", "OPENAI_API_KEY": "private"}):
            env = target._env(self.private, os.environ)
        self.assertEqual(env["HOME"], str(self.private))
        for key in ("GOOGLE_CLIENT_SECRET", "GITHUB_TOKEN", "HAVEN_GOOGLE_DRIVE_CLIENT_ID", "OPENAI_API_KEY"):
            self.assertNotIn(key, env)

    def test_existing_candidate_is_not_reused_or_modified(self):
        self.run_test()
        target_dir = self.private / "native-files-profile-reopen"
        marker = target_dir / "keep.txt"
        marker.write_text("precious")
        with self.assertRaises(target.ReopenError):
            self.run_test()
        self.assertEqual(marker.read_text(), "precious")

    def test_nonprivate_parent_refused_without_writing(self):
        self.private.chmod(0o755)
        with self.assertRaises(target.ReopenError):
            self.run_test()
        self.assertFalse((self.private / "native-files-profile-reopen").exists())

    def test_symlink_scratch_refused(self):
        other = self.root / "other"
        other.symlink_to(self.private, target_is_directory=True)
        with self.assertRaises(target.ReopenError):
            target.run_reopen(self.host, other, real_elf=False, window_probe=lambda _: ["0x1"])
        self.assertFalse((self.private / "native-files-profile-reopen").exists())

    def test_missing_native_runtime_refused_without_creating_profile(self):
        (self.app / "Haven.dll").unlink()
        with self.assertRaises(target.ReopenError):
            self.run_test()
        self.assertFalse((self.private / "native-files-profile-reopen").exists())

    def test_fake_script_rejected_as_native_elf_in_normal_mode(self):
        with self.assertRaises(target.ReopenError):
            target.run_reopen(self.host, self.private, window_probe=lambda _: ["0x1"])
        self.assertFalse((self.private / "native-files-profile-reopen").exists())

    def test_window_timeout_does_not_claim_success_or_leave_process(self):
        result = target.run_reopen(self.host, self.private, window_probe=lambda _: [],
                                   real_elf=False, timeout=0.25, stable=0)
        self.assertEqual(result["status"], "FAIL")
        self.assertEqual(result["attempts"], 1)
        self.assertEqual(result["attempt_reasons"], ["window-timeout"])

    def test_run_script_fails_with_no_private_data_in_stdout(self):
        import contextlib, io
        stdout = io.StringIO()
        with contextlib.redirect_stdout(stdout):
            code = target.main(["--executable", str(self.root / "missing-secret-filename"),
                                "--scratch-parent", str(self.private)])
        self.assertEqual(code, 2)
        self.assertNotIn("missing-secret-filename", stdout.getvalue())
        self.assertEqual(json.loads(stdout.getvalue())["status"], "BLOCKED")

    def test_fast_exit_is_failure_not_a_window_receipt(self):
        self.host.write_text("#!/bin/sh\nexit 0\n")
        result = target.run_reopen(self.host, self.private,
                                   window_probe=lambda _: [], real_elf=False,
                                   timeout=2, stable=0)
        self.assertEqual(result["status"], "FAIL")
        self.assertEqual(result["attempt_reasons"], ["exited-before-window"])

    def test_actual_test_processes_are_reaped(self):
        pids = []
        def probe(pid):
            pids.append(pid)
            return ["0x1"]
        result = target.run_reopen(self.host, self.private,
                                   window_probe=probe, real_elf=False,
                                   timeout=2, stable=0.02)
        self.assertEqual(result["status"], "PASS")
        for pid in set(pids):
            with self.assertRaises(ProcessLookupError):
                os.kill(pid, 0)

    def test_invalid_timeout_is_rejected(self):
        with self.assertRaises(target.ReopenError):
            target.run_reopen(self.host, self.private, timeout=1000,
                              window_probe=lambda _: ["0x1"], real_elf=False)


if __name__ == "__main__":
    unittest.main()
