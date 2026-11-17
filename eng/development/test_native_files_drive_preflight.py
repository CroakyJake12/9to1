"""Actual isolated permission/symlink/CLI tests; never touch owner profiles."""
import json
import os
from pathlib import Path
import stat
import subprocess
import sys
import tempfile
import unittest

import native_files_drive_preflight as app

CLIENT = "123456789-testdesktop.apps.googleusercontent.com"


class NativeDrivePreflightTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.home = self.root / "home"
        self.home.mkdir(mode=0o700)
        self.bin = self.root / "bin"
        self.bin.mkdir()
        self.opener = self.bin / "xdg-open"
        self.opener.write_text("#!/bin/sh\nexit 0\n")
        self.opener.chmod(0o700)
        self.environment = {"PATH": str(self.bin)}
        self.profile = self.home / ".local/share/9to1-development/profile"

    def prepare_profile(self, mode=0o700):
        self.profile.mkdir(parents=True)
        self.profile.chmod(mode)

    def output(self):
        return app.check(self.environment, home=self.home)

    def test_missing_client_and_profile_requires_action_without_any_mutation(self):
        before = list(self.home.rglob("*"))
        result = self.output()
        self.assertFalse(result["ready_for_sign_in"])
        self.assertEqual(result["client_id"], "missing")
        self.assertEqual(result["profile"], "absent")
        self.assertEqual(before, list(self.home.rglob("*")))

    def test_empty_account_private_profile_and_desktop_id_can_proceed_to_consent(self):
        self.prepare_profile()
        self.environment["HAVEN_GOOGLE_DRIVE_CLIENT_ID"] = CLIENT
        result = self.output()
        self.assertTrue(result["ready_for_sign_in"])
        self.assertEqual(result["account_file"], "absent")
        self.assertEqual(result["client_id"], "configured_unverified")
        self.assertIn("cannot verify OAuth consent", result["actions"][-1])

    def test_public_client_id_file_matches_native_fallback(self):
        self.prepare_profile()
        config = self.home / ".config/9to1"
        config.mkdir(parents=True)
        (config / "google-drive-client-id.txt").write_text(CLIENT + "\n")
        self.assertTrue(self.output()["ready_for_sign_in"])

    def test_non_private_profile_refused_even_with_valid_client(self):
        self.prepare_profile(0o755)
        self.environment["HAVEN_GOOGLE_DRIVE_CLIENT_ID"] = CLIENT
        result = self.output()
        self.assertFalse(result["ready_for_sign_in"])
        self.assertEqual(result["profile"], "exposed_permissions")

    def test_profile_symlink_refused_instead_of_inspecting_target(self):
        outside = self.root / "outside"
        outside.mkdir(mode=0o700)
        self.profile.parent.mkdir(parents=True)
        self.profile.symlink_to(outside, target_is_directory=True)
        self.environment["HAVEN_GOOGLE_DRIVE_CLIENT_ID"] = CLIENT
        self.assertEqual(self.output()["profile"], "symlink_ancestor")

    def test_token_symlink_refused_without_reading_or_changing_target(self):
        self.prepare_profile()
        secret = "UNIQUE_TEST_SENTINEL_DO_NOT_LOG"
        outside = self.root / "outside.txt"
        outside.write_text(secret)
        (self.profile / "google-drive-account.json").symlink_to(outside)
        self.environment["HAVEN_GOOGLE_DRIVE_CLIENT_ID"] = CLIENT
        result = self.output()
        self.assertEqual(result["account_file"], "symlink")
        self.assertFalse(result["ready_for_sign_in"])
        self.assertNotIn(secret, json.dumps(result))
        self.assertEqual(outside.read_text(), secret)

    def test_exposed_credential_permissions_refused_without_reading_token(self):
        self.prepare_profile()
        secret = "UNIQUE_TEST_REFRESH_TOKEN_DO_NOT_LOG"
        token = self.profile / "google-drive-account.json"
        token.write_text(secret)
        token.chmod(0o644)
        self.environment["HAVEN_GOOGLE_DRIVE_CLIENT_ID"] = CLIENT
        result = self.output()
        self.assertEqual(result["account_file"], "exposed_permissions")
        self.assertFalse(result["ready_for_sign_in"])
        self.assertNotIn(secret, json.dumps(result))
        self.assertEqual(token.read_text(), secret)

    def test_valid_existing_private_file_is_unverified_not_authenticated(self):
        self.prepare_profile()
        token = self.profile / "google-drive-account.json"
        token.write_text("dummy-corrupt-contents")
        token.chmod(0o600)
        self.environment["HAVEN_GOOGLE_DRIVE_CLIENT_ID"] = CLIENT
        result = self.output()
        self.assertTrue(result["ready_for_sign_in"])
        self.assertEqual(result["account_file"], "private_regular")
        self.assertNotIn("connected", json.dumps(result).lower())

    def test_bad_client_id_format_not_mistaken_for_desktop_connection(self):
        self.prepare_profile()
        self.environment["HAVEN_GOOGLE_DRIVE_CLIENT_ID"] = "https://attacker.example/oauth"
        result = self.output()
        self.assertEqual(result["client_id"], "invalid_format")
        self.assertFalse(result["ready_for_sign_in"])

    def test_relative_profile_data_path_refused(self):
        self.environment.update({"HAVEN_GOOGLE_DRIVE_CLIENT_ID": CLIENT,
                                 "HAVEN_DATA_DIR": "relative/profiles"})
        result = self.output()
        self.assertFalse(result["ready_for_sign_in"])
        self.assertEqual(result["profile"], "unsafe_relative_data_dir")

    def test_missing_opener_is_explicit_not_oauth_success(self):
        self.prepare_profile()
        self.environment["HAVEN_GOOGLE_DRIVE_CLIENT_ID"] = CLIENT
        self.opener.unlink()
        result = self.output()
        self.assertEqual(result["browser_opener"], "missing")
        self.assertFalse(result["ready_for_sign_in"])

    def test_client_config_symlink_is_not_followed(self):
        self.prepare_profile()
        config = self.home / ".config/9to1"
        config.parent.mkdir()
        external = self.root / "outside"
        external.mkdir()
        (external / "google-drive-client-id.txt").write_text(CLIENT)
        config.symlink_to(external, target_is_directory=True)
        self.assertEqual(self.output()["client_id"], "unsafe_config_path")

    def test_world_writable_client_id_directory_is_rejected(self):
        self.prepare_profile()
        config = self.home / ".config/9to1"
        config.mkdir(parents=True)
        (config / "google-drive-client-id.txt").write_text(CLIENT)
        config.chmod(0o777)
        self.assertEqual(self.output()["client_id"], "unsafe_config_path")

    def test_outer_config_parent_symlink_is_rejected(self):
        self.prepare_profile()
        external = self.root / "outside-config"
        (external / "9to1").mkdir(parents=True)
        (external / "9to1/google-drive-client-id.txt").write_text(CLIENT)
        (self.home / ".config").symlink_to(external, target_is_directory=True)
        self.assertEqual(self.output()["client_id"], "unsafe_config_path")

    def test_nonsticky_world_writable_data_parent_is_rejected(self):
        self.prepare_profile()
        insecure = self.root / "world-open"
        insecure.mkdir(mode=0o777)
        insecure.chmod(0o777)
        inner = insecure / "profile"
        inner.mkdir(mode=0o700)
        self.environment.update({"HAVEN_DATA_DIR": str(inner),
                                 "HAVEN_GOOGLE_DRIVE_CLIENT_ID": CLIENT})
        self.assertEqual(self.output()["profile"], "writable_ancestor")

    def test_json_cli_return_codes_and_no_credentials_in_output(self):
        self.prepare_profile()
        env = {**os.environ, "HOME": str(self.home), "PATH": str(self.bin),
               "HAVEN_GOOGLE_DRIVE_CLIENT_ID": CLIENT}
        env.pop("HAVEN_DATA_DIR", None)
        script = Path(app.__file__)
        result = subprocess.run([sys.executable, str(script), "--json"], env=env,
                                capture_output=True, text=True, timeout=5)
        self.assertEqual(result.returncode, 0, result.stderr)
        parsed = json.loads(result.stdout)
        self.assertTrue(parsed["ready_for_sign_in"])
        self.assertNotIn(CLIENT, result.stdout)
        self.assertNotIn(str(self.home), result.stdout)
        self.opener.unlink()
        failure = subprocess.run([sys.executable, str(script), "--json"], env=env,
                                 capture_output=True, text=True, timeout=5)
        self.assertEqual(failure.returncode, 2)
        self.assertEqual(json.loads(failure.stdout)["browser_opener"], "missing")


if __name__ == "__main__":
    unittest.main()
