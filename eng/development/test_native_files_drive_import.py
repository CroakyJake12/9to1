"""Real disposable Linux filesystem tests for Desktop OAuth JSON importer."""
from __future__ import annotations
import contextlib
import io
import json
import os
from pathlib import Path
import stat
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parent))
import native_files_drive_import as importer

ID = "1234567890-synthetic.apps.googleusercontent.com"
SECRET = "SYNTHETIC-NEVER-STORE-THIS-SECRET"


def fixture(**changes):
    payload = {"installed": {"client_id": ID, "client_secret": SECRET,
            "redirect_uris": ["http://localhost"],
            "token_uri": "https://oauth2.googleapis.com/token"}}
    payload.update(changes)
    return json.dumps(payload)


class ImporterTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.home = Path(self.temp.name) / "home"
        self.home.mkdir(mode=0o700)
        self.downloads = self.home / "Downloads"
        self.downloads.mkdir(mode=0o700)
        self.source = self.downloads / "oauth.json"
        self.source.write_text(fixture())
        self.source.chmod(0o600)
        self.target = self.home / ".config/9to1/google-drive-client-id.txt"

    def run_import(self):
        return importer.import_desktop_json(self.source, home=self.home)

    def test_real_file_import_installs_only_id_with_private_mode(self):
        old_umask = os.umask(0o022)
        try:
            self.assertEqual("created", self.run_import())
        finally:
            os.umask(old_umask)
        self.assertEqual(ID + "\n", self.target.read_text())
        self.assertEqual(0o600, stat.S_IMODE(self.target.stat().st_mode))
        self.assertNotIn(SECRET, self.target.read_text())
        self.assertEqual(0o600, stat.S_IMODE(self.source.stat().st_mode))

    def test_existing_destination_preserved_without_overwrite(self):
        self.run_import()
        self.source.write_text(fixture(installed={"client_id": "changed.apps.googleusercontent.com",
            "redirect_uris": ["http://localhost"], "token_uri": "https://oauth2.googleapis.com/token"}))
        with self.assertRaises(importer.ImportErrorSafe):
            self.run_import()
        self.assertEqual(ID + "\n", self.target.read_text())

    def test_no_source_secret_copied_into_destination_tree(self):
        self.run_import()
        for item in (self.home / ".config").rglob("*"):
            if item.is_file():
                self.assertNotIn(SECRET, item.read_text())

    def test_web_client_refused(self):
        self.source.write_text(fixture(web={"client_id": ID}))
        with self.assertRaisesRegex(importer.ImportErrorSafe, "Desktop"):
            self.run_import()
        self.assertFalse(self.target.exists())

    def test_missing_redirect_refused(self):
        self.source.write_text(fixture(installed={"client_id": ID,
            "token_uri": "https://oauth2.googleapis.com/token", "redirect_uris": ["https://example.com"]}))
        with self.assertRaisesRegex(importer.ImportErrorSafe, "loopback"):
            self.run_import()
        self.assertFalse(self.target.exists())

    def test_invalid_client_id_refused(self):
        self.source.write_text(fixture(installed={"client_id": "invalid", "redirect_uris": ["http://localhost"],
            "token_uri": "https://oauth2.googleapis.com/token"}))
        with self.assertRaises(importer.ImportErrorSafe):
            self.run_import()

    def test_non_google_token_endpoint_refused(self):
        self.source.write_text(fixture(installed={"client_id": ID,
            "redirect_uris": ["http://localhost"], "token_uri": "https://attacker.example/token"}))
        with self.assertRaises(importer.ImportErrorSafe):
            self.run_import()

    def test_public_source_mode_refused(self):
        self.source.chmod(0o644)
        with self.assertRaisesRegex(importer.ImportErrorSafe, "0600"):
            self.run_import()
        self.assertFalse(self.target.exists())

    def test_source_symlink_refused(self):
        self.source.rename(self.downloads / "real.json")
        self.source.symlink_to(self.downloads / "real.json")
        with self.assertRaises(importer.ImportErrorSafe):
            self.run_import()
        self.assertFalse(self.target.exists())

    def test_symlink_directory_refused(self):
        real = self.home / "real-downloads"
        self.downloads.rename(real)
        self.downloads.symlink_to(real, target_is_directory=True)
        with self.assertRaises(importer.ImportErrorSafe):
            self.run_import()

    def test_unowned_source_directory_refused(self):
        self.downloads.chmod(0o777)
        with self.assertRaises(importer.ImportErrorSafe):
            self.run_import()
        self.assertFalse(self.target.exists())

    def test_home_symlink_refused(self):
        real_home = self.home.with_name("real-home")
        self.home.rename(real_home)
        self.home.symlink_to(real_home, target_is_directory=True)
        with self.assertRaises(importer.ImportErrorSafe):
            self.run_import()

    def test_fifo_refused_without_blocking(self):
        self.source.unlink()
        os.mkfifo(self.source, 0o600)
        with self.assertRaises(importer.ImportErrorSafe):
            self.run_import()

    def test_oversized_file_refused(self):
        self.source.write_bytes(b" " * 32769)
        with self.assertRaises(importer.ImportErrorSafe):
            self.run_import()

    def test_duplicate_keys_refused(self):
        self.source.write_text('{"installed":{"client_id":"%s","client_id":"%s"}}' % (ID, ID))
        with self.assertRaises(importer.ImportErrorSafe):
            self.run_import()

    def test_source_outside_home_refused(self):
        outside = Path(self.temp.name) / "other.json"
        outside.write_text(fixture())
        outside.chmod(0o600)
        with self.assertRaises(importer.ImportErrorSafe):
            importer.import_desktop_json(outside, home=self.home)

    def test_does_not_overwrite_existing_symlink_target(self):
        destdir = self.home / ".config/9to1"
        destdir.mkdir(parents=True)
        destination = self.target
        destination.symlink_to(self.source)
        with self.assertRaises(importer.ImportErrorSafe):
            self.run_import()
        self.assertEqual(fixture(), self.source.read_text())

    def test_concurrent_import_only_one_can_create(self):
        # Use separate processes only when the source code is available in this test environment.
        source_root = str(Path(__file__).resolve().parent)
        script = "import sys;sys.path.insert(0,sys.argv[1]);import native_files_drive_import as i;from pathlib import Path;\n" \
                 "try:i.import_desktop_json(Path(sys.argv[2]),home=Path(sys.argv[3]));print('OK')\n" \
                 "except i.ImportErrorSafe:print('REFUSED')"
        command = [sys.executable, "-B", "-c", script, source_root, str(self.source), str(self.home)]
        a = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        b = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        out_a, err_a = a.communicate(timeout=15)
        out_b, err_b = b.communicate(timeout=15)
        self.assertEqual(0, a.returncode, err_a)
        self.assertEqual(0, b.returncode, err_b)
        self.assertCountEqual(["OK", "REFUSED"], [out_a.strip(), out_b.strip()])
        self.assertEqual(ID + "\n", self.target.read_text())

    def test_noninteractive_cli_refuses_without_revealing_source_path(self):
        with patch.object(sys, "stdin") as stdin, patch.object(sys, "stderr") as stderr:
            stdin.isatty.return_value = False
            stderr.isatty.return_value = False
            capture = io.StringIO()
            with contextlib.redirect_stderr(capture):
                result = importer.main(["--client-json", str(self.source)])
        self.assertEqual(2, result)
        self.assertNotIn(str(self.source), capture.getvalue())
        self.assertNotIn(SECRET, capture.getvalue())
        self.assertFalse(self.target.exists())


if __name__ == "__main__":
    unittest.main()
