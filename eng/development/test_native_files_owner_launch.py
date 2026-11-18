"""Real Linux disk, tar, ELF subprocess tests. No Haven/Google account involved."""
from __future__ import annotations

import fcntl
import hashlib
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile
import unittest
from unittest.mock import patch

from native_files_owner_launch import ARCHIVE, LaunchError, launch

REV = "a" * 40


class OwnerLaunchTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.root.chmod(0o700)
        self.candidates = self.root / "candidates"
        self.profiles = self.root / "profiles"
        for p in (self.candidates, self.profiles):
            p.mkdir(mode=0o700)
        self.candidate = self.candidates / "source-a"
        self.candidate.mkdir(mode=0o700)
        (self.candidate / "artifacts").mkdir(mode=0o700)
        (self.candidate / "installed").mkdir(mode=0o700)
        self.profile = self.profiles / "owner-profile"
        self.runtime = self.root / "fake-dotnet"
        self.runtime.write_text("#!/bin/sh\nprintf 'Microsoft.NETCore.App 10.0.4 [/opt/dotnet/shared/Microsoft.NETCore.App]\\n'\n")
        self.runtime.chmod(0o700)
        self.native = self.candidate / "installed" / "Haven"
        if not shutil.which("gcc"):
            self.skipTest("gcc required for real x64 ELF fixture")
        c_src = self.root / "elf.c"
        c_src.write_text(r'''#include <stdio.h>
#include <stdlib.h>
#include <string.h>
int main(void) {
 const char *p = getenv("HAVEN_DATA_DIR");
 if (!p || getenv("GOOGLE_APPLICATION_CREDENTIALS") || !getenv("HOME")) return 8;
 char dest[4096]; snprintf(dest,sizeof(dest),"%s/started",p);
 FILE *f = fopen(dest,"a"); if (!f) return 9;
 fprintf(f,"%s\n",getenv("HOME")); fclose(f); return 0;
}''')
        subprocess.run(["gcc", "-o", str(self.native), str(c_src)], check=True,
                       stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        (self.candidate / "installed" / "Haven.dll").write_bytes(b"Synthetic DLL fixture")
        (self.candidate / "installed" / "Haven.runtimeconfig.json").write_bytes(b"{}")
        self._repack()

    def _repack(self):
        archive = self.candidate / "artifacts" / ARCHIVE
        with tarfile.open(archive, "w:gz") as tf:
            for name in ("Haven", "Haven.dll", "Haven.runtimeconfig.json"):
                tf.add(self.candidate / "installed" / name, arcname="./" + name)
        checksum = hashlib.sha256(archive.read_bytes()).hexdigest()
        receipt = {"schema": "9to1.native-files-source-candidate/v1",
                   "status": "BUILT_NOT_DRIVE_ACCEPTED", "source_revision": REV,
                   "target": "linux-x64", "archive": str(archive),
                   "archive_sha256": checksum,
                   "launch_executable": str(self.native), "oauth_checks": "NOT_RUN",
                   "real_drive_checks": "NOT_RUN", "dotnet_sdk": "10.0.404"}
        self._write_receipt(receipt)

    def _write_receipt(self, receipt):
        path = self.candidate / "receipt.json"
        path.write_text(json.dumps(receipt))
        path.chmod(0o600)

    def _go(self, create=True):
        return launch(self.candidate, self.profile, REV,
                      create_profile=create, dotnet=str(self.runtime))

    def test_real_elf_runs_twice_with_same_persistent_profile(self):
        with patch.dict(os.environ, {"GOOGLE_APPLICATION_CREDENTIALS": "/dev/never",
                                          "AWS_SECRET_ACCESS_KEY": "do-not-inherit"}):
            self.assertEqual(self._go()["status"], "EXITED")
            self.assertEqual(self._go(False)["status"], "EXITED")
        marks = (self.profile / "haven-data" / "started").read_text().splitlines()
        self.assertEqual(marks, [str(self.profile), str(self.profile)])
        self.assertTrue(all((self.profile / n).stat().st_mode & 0o777 == 0o700
                            for n in ("config", "data", "cache", "state", "haven-data")))

    def test_missing_profile_requires_explicit_creation(self):
        with self.assertRaises(OSError):
            self._go(False)
        self.assertFalse(self.profile.exists())

    def test_profile_creation_refuses_existing(self):
        self._go()
        with self.assertRaises(FileExistsError):
            self._go(True)
        self.assertEqual((self.profile / "haven-data" / "started").read_text().count("\n"), 1)

    def test_selected_profile_parent_must_be_private(self):
        self.profiles.chmod(0o755)
        with self.assertRaises(LaunchError):
            self._go()

    def test_symlink_profile_ancestor_is_refused(self):
        alias = self.root / "alias"
        alias.symlink_to(self.profiles, target_is_directory=True)
        with self.assertRaises(OSError):
            launch(self.candidate, alias / "owner-profile", REV,
                   create_profile=True, dotnet=str(self.runtime))
        self.assertFalse(self.profile.exists())

    def test_symlink_candidate_ancestor_is_refused(self):
        alias = self.root / "alias"
        alias.symlink_to(self.candidates, target_is_directory=True)
        with self.assertRaises(OSError):
            launch(alias / "source-a", self.profile, REV,
                   create_profile=True, dotnet=str(self.runtime))
        self.assertFalse(self.profile.exists())

    def test_receipt_symlink_is_refused(self):
        data = (self.candidate / "receipt.json").read_bytes()
        (self.candidate / "receipt.json").unlink()
        elsewhere = self.root / "elsewhere"
        elsewhere.write_bytes(data)
        elsewhere.chmod(0o600)
        (self.candidate / "receipt.json").symlink_to(elsewhere)
        with self.assertRaises(OSError):
            self._go()

    def test_archive_digest_mismatch_refuses_launch(self):
        with (self.candidate / "artifacts" / ARCHIVE).open("ab") as stream:
            stream.write(b"changed")
        with self.assertRaises(LaunchError):
            self._go()
        self.assertFalse(self.profile.exists())

    def test_installed_host_modified_refuses_launch(self):
        with self.native.open("ab") as stream:
            stream.write(b"changed")
        with self.assertRaises(LaunchError):
            self._go()
        self.assertFalse(self.profile.exists())

    def test_added_unpublished_runtime_file_is_refused(self):
        (self.candidate / "installed" / "injected.dll").write_bytes(b"unexpected")
        with self.assertRaises(LaunchError):
            self._go()

    def test_runtime_file_symlink_is_refused(self):
        (self.candidate / "installed" / "Haven.dll").unlink()
        (self.candidate / "installed" / "Haven.dll").symlink_to(self.runtime)
        with self.assertRaises(LaunchError):
            self._go()

    def test_original_publisher_artifacts_0755_with_private_root(self):
        """The source publisher creates artifacts via mkdir -p under private root."""
        (self.candidate / "artifacts").chmod(0o755)
        self.assertEqual(self._go()["status"], "EXITED")

    def test_artifacts_group_writable_even_under_private_root_refused(self):
        (self.candidate / "artifacts").chmod(0o775)
        with self.assertRaises(LaunchError):
            self._go()

    def test_profile_lock_refuses_overlapping_launcher(self):
        self._go()
        fd = os.open(self.profile, os.O_RDONLY | os.O_DIRECTORY)
        self.addCleanup(os.close, fd)
        fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        with self.assertRaises(LaunchError):
            self._go(False)

    def test_unsafe_existing_profile_subdir_refused(self):
        self._go()
        shutil.rmtree(self.profile / "config")
        (self.profile / "config").symlink_to(self.root, target_is_directory=True)
        with self.assertRaises(OSError):
            self._go(False)

    def test_wrong_revision_or_receipt_path_refused(self):
        with self.assertRaises(LaunchError):
            launch(self.candidate, self.profile, "b" * 40,
                   create_profile=True, dotnet=str(self.runtime))
        r = json.loads((self.candidate / "receipt.json").read_text())
        r["launch_executable"] = "/tmp/Haven"
        self._write_receipt(r)
        with self.assertRaises(LaunchError):
            self._go()

    def test_empty_unpublished_directory_is_refused(self):
        (self.candidate / "installed" / "surplus").mkdir(mode=0o700)
        with self.assertRaises(LaunchError):
            self._go()

    def test_runtime_file_tampering_rejected(self):
        (self.candidate / "installed" / "Haven.runtimeconfig.json").write_bytes(b"{tampered}")
        with self.assertRaises(LaunchError):
            self._go()

    def test_unavailable_dotnet_runtime_refuses_start(self):
        self.runtime.write_text("#!/bin/sh\nprintf 'Microsoft.NETCore.App 9.0.0 [/other]\\n'\n")
        with self.assertRaises(LaunchError):
            self._go()
        self.assertFalse((self.profile / "haven-data" / "started").exists())

    def test_nested_profile_inside_candidate_refused(self):
        with self.assertRaises(LaunchError):
            launch(self.candidate, self.candidate / "owner", REV,
                   create_profile=True, dotnet=str(self.runtime))

    def test_original_gnu_tar_nested_publish_is_accepted(self):
        """Exercise the real publisher's tar -C publish -czf archive . layout."""
        nested = self.candidate / "installed" / "framework" / "subdir"
        nested.parent.mkdir(mode=0o700)
        nested.mkdir(mode=0o700)
        (nested / "plugin.so").write_bytes(b"nested-runtime-fixture")
        archive = self.candidate / "artifacts" / ARCHIVE
        subprocess.run(["tar", "-C", str(self.candidate / "installed"),
                        "-czf", str(archive), "."], check=True,
                       stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        receipt = json.loads((self.candidate / "receipt.json").read_text())
        receipt["archive_sha256"] = hashlib.sha256(archive.read_bytes()).hexdigest()
        self._write_receipt(receipt)
        self.assertEqual(self._go()["status"], "EXITED")

    def test_archive_duplicate_member_rejected_even_when_receipt_updated(self):
        archive = self.candidate / "artifacts" / ARCHIVE
        with tarfile.open(archive, "w:gz") as tf:
            for name in ("Haven", "Haven.dll", "Haven.runtimeconfig.json", "Haven.dll"):
                tf.add(self.candidate / "installed" / name, arcname="./" + name)
        receipt = json.loads((self.candidate / "receipt.json").read_text())
        receipt["archive_sha256"] = hashlib.sha256(archive.read_bytes()).hexdigest()
        self._write_receipt(receipt)
        with self.assertRaises(LaunchError):
            self._go()

    def test_archive_extra_symlink_rejected_even_with_matching_digest(self):
        archive = self.candidate / "artifacts" / ARCHIVE
        with tarfile.open(archive, "w:gz") as tf:
            for name in ("Haven", "Haven.dll", "Haven.runtimeconfig.json"):
                tf.add(self.candidate / "installed" / name, arcname="./" + name)
            link = tarfile.TarInfo("./unsafe-link")
            link.type = tarfile.SYMTYPE
            link.linkname = "/etc/passwd"
            tf.addfile(link)
        receipt = json.loads((self.candidate / "receipt.json").read_text())
        receipt["archive_sha256"] = hashlib.sha256(archive.read_bytes()).hexdigest()
        self._write_receipt(receipt)
        with self.assertRaises(LaunchError):
            self._go()

    def test_malformed_duplicate_receipt_keys_refused(self):
        path = self.candidate / "receipt.json"
        path.write_text('{"schema":"foo","schema":"bar"}')
        path.chmod(0o600)
        with self.assertRaises(LaunchError):
            self._go()


if __name__ == "__main__":
    unittest.main()
