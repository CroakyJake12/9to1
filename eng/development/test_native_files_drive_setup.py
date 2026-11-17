"""Isolated, actual-disk security regressions for manual native Drive setup.

No Google network, owner HOME, credential source, or real profile is touched.
"""

import contextlib
import io
import multiprocessing
import os
from pathlib import Path
import stat
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

import native_files_drive_setup as setup
import native_files_drive_preflight as preflight

CLIENT_A = "123456789-desktop.apps.googleusercontent.com"
CLIENT_B = "987654321-desktop.apps.googleusercontent.com"
SECRET = "TEST_GOOGLE_SECRET_NEVER_PRINT_IN_CLI_12345"


def race_installer(home: str, client_id: str, queue) -> None:
    try:
        queue.put(("ok", setup.install("client-id", client_id, home=Path(home))))
    except setup.InstallError as exc:
        queue.put(("refused", str(exc)))


class NativeDriveSetupTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.home = self.root / "home"
        self.home.mkdir(mode=0o700)
        self.config = self.home / ".config/9to1"

    def test_install_public_client_under_common_umask_is_private_from_creation(self):
        original = os.umask(0o022)
        try:
            self.assertEqual(setup.install("client-id", CLIENT_A, home=self.home), "created")
        finally:
            os.umask(original)
        client = self.config / "google-drive-client-id.txt"
        self.assertEqual(client.read_text(), CLIENT_A + "\n")
        self.assertEqual(stat.S_IMODE(client.stat().st_mode), 0o600)
        self.assertEqual(stat.S_IMODE(self.config.stat().st_mode), 0o700)
        self.assertEqual(stat.S_IMODE(self.config.parent.stat().st_mode), 0o700)
        self.assertEqual(list(self.config.iterdir()), [client])

    def test_secret_created_private_and_installer_never_prints_its_contents(self):
        output = io.StringIO()
        with contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
            self.assertEqual(setup.install("client-secret", SECRET, home=self.home), "created")
        self.assertEqual(output.getvalue(), "")
        secret = self.config / "google-drive-client-secret.txt"
        self.assertEqual(stat.S_IMODE(secret.stat().st_mode), 0o600)
        self.assertEqual(secret.read_text(), SECRET + "\n")
        self.assertEqual(list(self.config.iterdir()), [secret])

    def test_refuse_overwrite_existing_client_id_without_modification(self):
        self.config.mkdir(parents=True)
        client = self.config / "google-drive-client-id.txt"
        client.write_text("existing-owner-value\n")
        client.chmod(0o600)
        with self.assertRaisesRegex(setup.InstallError, "already exists"):
            setup.install("client-id", CLIENT_A, home=self.home)
        self.assertEqual(client.read_text(), "existing-owner-value\n")
        self.assertEqual(stat.S_IMODE(client.stat().st_mode), 0o600)
        self.assertEqual(list(self.config.iterdir()), [client])

    def test_refuse_symlink_destination_without_modifying_target(self):
        self.config.mkdir(parents=True)
        outside = self.root / "outside.txt"
        outside.write_text("DO_NOT_REPLACE")
        (self.config / "google-drive-client-secret.txt").symlink_to(outside)
        with self.assertRaises(setup.InstallError):
            setup.install("client-secret", SECRET, home=self.home)
        self.assertEqual(outside.read_text(), "DO_NOT_REPLACE")
        self.assertEqual((self.config / "google-drive-client-secret.txt").is_symlink(), True)
        self.assertEqual(list(self.config.iterdir()), [self.config / "google-drive-client-secret.txt"])

    def test_refuse_symlink_config_parent_without_touching_target(self):
        external = self.root / "external-config"
        (external / "9to1").mkdir(parents=True)
        (self.home / ".config").symlink_to(external, target_is_directory=True)
        with self.assertRaises(setup.InstallError):
            setup.install("client-id", CLIENT_A, home=self.home)
        self.assertEqual(list((external / "9to1").iterdir()), [])

    def test_refuse_symlink_in_home_ancestor(self):
        outside = self.root / "actual-home"
        outside.mkdir(mode=0o700)
        alias = self.root / "linked-home"
        alias.symlink_to(outside, target_is_directory=True)
        with self.assertRaises(setup.InstallError):
            setup.install("client-id", CLIENT_A, home=alias)
        self.assertEqual(list(outside.iterdir()), [])

    def test_refuse_other_writable_home_and_config_directories(self):
        self.home.chmod(0o777)
        with self.assertRaises(setup.InstallError):
            setup.install("client-id", CLIENT_A, home=self.home)
        self.home.chmod(0o700)
        self.config.mkdir(parents=True)
        self.config.chmod(0o777)
        with self.assertRaises(setup.InstallError):
            setup.install("client-id", CLIENT_A, home=self.home)
        self.assertFalse((self.config / "google-drive-client-id.txt").exists())

    @unittest.skipUnless(hasattr(os, "mkfifo"), "POSIX FIFO required")
    def test_refuse_non_directory_component(self):
        os.mkfifo(self.home / ".config", 0o600)
        with self.assertRaises(setup.InstallError):
            setup.install("client-secret", SECRET, home=self.home)

    def test_invalid_input_never_creates_directories(self):
        bad_values = ["", "abc", "evil\nline", "123456789-attacker.example", "bogus\x00secret"]
        for value in bad_values:
            with self.subTest(value=repr(value)):
                with self.assertRaises(setup.InstallError):
                    setup.install("client-id", value, home=self.home)
                self.assertEqual(list(self.home.iterdir()), [])
        with self.assertRaises(setup.InstallError):
            setup.install("client-secret", "a b", home=self.home)
        self.assertEqual(list(self.home.iterdir()), [])

    def test_existing_file_stays_untouched_on_staged_link_failure(self):
        # An injected target publication failure must remove only its private
        # temporary stage, never a pre-existing account/config file.
        self.config.mkdir(parents=True)
        other = self.config / "unrelated.txt"
        other.write_text("UNCHANGED")
        with mock.patch.object(setup.os, "link", side_effect=PermissionError("injected")):
            with self.assertRaises(setup.InstallError):
                setup.install("client-secret", SECRET, home=self.home)
        self.assertEqual(other.read_text(), "UNCHANGED")
        self.assertEqual(list(self.config.iterdir()), [other])

    def test_exclusive_commit_race_only_one_client_id_wins(self):
        ctx = multiprocessing.get_context("spawn")
        queue = ctx.Queue()
        processes = [ctx.Process(target=race_installer,
                                 args=(str(self.home), client, queue))
                     for client in (CLIENT_A, CLIENT_B)]
        for p in processes:
            p.start()
        for p in processes:
            p.join(timeout=8)
            self.assertEqual(p.exitcode, 0)
        results = [queue.get(timeout=2) for _ in processes]
        self.assertEqual(sum(kind == "ok" for kind, _ in results), 1)
        self.assertEqual(sum(kind == "refused" for kind, _ in results), 1)
        winner = (self.config / "google-drive-client-id.txt").read_text().strip()
        self.assertIn(winner, (CLIENT_A, CLIENT_B))
        self.assertEqual(len(list(self.config.iterdir())), 1)

    def test_preflight_can_see_installed_client_and_optional_secret(self):
        setup.install("client-id", CLIENT_A, home=self.home)
        setup.install("client-secret", SECRET, home=self.home)
        profile = self.home / ".local/share/9to1-development/profile"
        profile.mkdir(parents=True)
        profile.chmod(0o700)
        bin_dir = self.root / "bin"
        bin_dir.mkdir()
        opener = bin_dir / "xdg-open"
        opener.write_text("#!/bin/sh\nexit 0\n")
        opener.chmod(0o700)
        result = preflight.check({"PATH": str(bin_dir)}, home=self.home)
        self.assertTrue(result["ready_for_sign_in"])
        self.assertEqual(result["client_id"], "configured_unverified")
        self.assertEqual(result["client_secret_file"], "private_regular")
        self.assertNotIn(SECRET, str(result))
        self.assertNotIn(CLIENT_A, str(result))

    def test_noninteractive_cli_refuses_without_prompting_or_mutation(self):
        env = {**os.environ, "HOME": str(self.home)}
        cmd = [sys.executable, str(Path(setup.__file__)), "--kind", "client-secret"]
        done = subprocess.run(cmd, input=SECRET + "\n", capture_output=True,
                              text=True, env=env, timeout=15)
        self.assertEqual(done.returncode, 2)
        self.assertIn("interactive terminal", done.stderr)
        self.assertNotIn(SECRET, done.stderr + done.stdout)
        self.assertEqual(list(self.home.iterdir()), [])


if __name__ == "__main__":
    unittest.main()
