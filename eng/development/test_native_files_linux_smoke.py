"""Real-disposable-process tests for native window observation; no user profile."""
from __future__ import annotations

import os
from pathlib import Path
import stat
import tempfile
import unittest
from unittest.mock import patch
import subprocess

from native_files_linux_smoke import owned_windows, x11_windows, private_child, run_smoke


class NativeFilesLinuxSmokeTests(unittest.TestCase):
    def test_matches_only_exact_owning_pid(self):
        rows = "0x01  0 123 host Haven\n0x02 0 1234 host Other\n0x03 0 123 host Haven second\n0x04 0 N/A host Missing\n"
        self.assertEqual(owned_windows(rows, 123), ["0x01", "0x03"])

    def test_rejects_partial_or_nonwindow_rows(self):
        self.assertEqual(owned_windows("0x01 0 123\nwindow 0 123 host Haven", 123), [])

    def test_xprop_exact_process_pid_and_unrelated_window(self):
        outputs = [
            subprocess.CompletedProcess([], 0, "_NET_CLIENT_LIST(WINDOW): window id # 0x0001, 0x0002", ""),
            subprocess.CompletedProcess([], 0, "_NET_WM_PID(CARDINAL) = 1234", ""),
            subprocess.CompletedProcess([], 0, "_NET_WM_PID(CARDINAL) = 123", ""),
        ]
        with patch("native_files_linux_smoke.subprocess.run", side_effect=outputs) as proc:
            self.assertEqual(x11_windows(123), ["0x0002"])
        self.assertEqual(proc.call_count, 3)

    def test_xprop_no_property_fails_closed(self):
        outputs = [
            subprocess.CompletedProcess([], 0, "_NET_CLIENT_LIST(WINDOW): window id # 0x04", ""),
            subprocess.CompletedProcess([], 0, "_NET_WM_PID: not found.", ""),
        ]
        with patch("native_files_linux_smoke.subprocess.run", side_effect=outputs):
            self.assertEqual(x11_windows(123), [])

    def test_private_child_is_exclusive_and_mode_private(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = private_child(Path(tmp), "profile")
            self.assertEqual(stat.S_IMODE(folder.stat().st_mode), 0o700)
            with self.assertRaises(FileExistsError):
                private_child(Path(tmp), "profile")

    def test_private_child_refuses_symlink_parent(self):
        with tempfile.TemporaryDirectory() as tmp:
            real = Path(tmp) / "real"
            real.mkdir()
            link = Path(tmp) / "link"
            link.symlink_to(real, target_is_directory=True)
            with self.assertRaises(ValueError):
                private_child(link, "profile")

    def make_fake_publish(self, root: Path, contents: str) -> Path:
        published = root / "publish"
        published.mkdir()
        program = published / "Haven"
        program.write_text("#!/bin/sh\n" + contents, encoding="utf-8")
        program.chmod(0o700)
        (published / "Haven.dll").write_bytes(b"fixture")
        (published / "Haven.runtimeconfig.json").write_text("{}")
        return program

    def test_real_child_process_window_observation_and_cleanup(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            app = self.make_fake_publish(root, "sleep 40\n")
            seen = []
            def mock_window(pid):
                seen.append(pid)
                return ["0xabcdef"] if len(seen) >= 2 else []
            result = run_smoke(app, root, timeout_seconds=2, stable_seconds=0.25, window_probe=mock_window)
            self.assertEqual(result["status"], "PASS")
            self.assertGreaterEqual(len(seen), 2)
            self.assertEqual(result["drive_checks"], "NOT_RUN")
            self.assertEqual(stat.S_IMODE((root / "native-files-clean-profile").stat().st_mode), 0o700)
            self.assertTrue((root / "native-files-clean-profile" / "stdout.log").is_file())

    def test_real_immediate_exit_is_failure(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            app = self.make_fake_publish(root, "exit 17\n")
            result = run_smoke(app, root, timeout_seconds=2, stable_seconds=0, window_probe=lambda pid: [])
            self.assertEqual(result["status"], "FAIL")
            self.assertEqual(result["reason"], "native-app-exited-before-window")

    def test_missing_runtime_is_rejected_before_profile_created(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            app = root / "Haven"
            app.write_text("#!/bin/sh\nexit 0\n")
            app.chmod(0o700)
            with self.assertRaisesRegex(ValueError, "missing"):
                run_smoke(app, root)
            self.assertFalse((root / "native-files-clean-profile").exists())

    def test_missing_executable_or_wrong_modes(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            app = self.make_fake_publish(root, "exit 0\n")
            app.chmod(0o600)
            with self.assertRaisesRegex(ValueError, "regular executable"):
                run_smoke(app, root)

    def test_existing_profile_is_refused_without_modification(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            app = self.make_fake_publish(root, "exit 0\n")
            previous = root / "native-files-clean-profile"
            previous.mkdir()
            sentinel = previous / "keep"
            sentinel.write_text("owner")
            with self.assertRaises(FileExistsError):
                run_smoke(app, root)
            self.assertEqual(sentinel.read_text(), "owner")


if __name__ == "__main__":
    unittest.main()
