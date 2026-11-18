"""Real disposable tar and Linux filesystem tests for Haven package admission."""
from __future__ import annotations

import hashlib
import io
import json
import os
from pathlib import Path
import stat
import subprocess
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch

from native_files_package_admission import (
    AdmissionError, inspect_archive, install_archive, main, member_path,
)

ELF_X64 = b"\x7fELF\x02\x01" + bytes(12) + b"\x3e\x00" + bytes(44)


class NativePackageAdmissionTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.root = Path(self._tmp.name)
        self.private = self.root / "private"
        self.private.mkdir(mode=0o700)
        self.archive = self.root / "haven-linux-linux-x64.tar.gz"

    def package(self, extras=(), *, include_host=True, host_bytes=ELF_X64, executable=True):
        files = [
            ("./Haven.dll", b"MZfixture", 0o644, "file"),
            ("./Haven.runtimeconfig.json", b'{}', 0o644, "file"),
            ("./Haven.deps.json", b'{}', 0o644, "file"),
        ]
        if include_host:
            files.insert(0, ("./Haven", host_bytes, 0o755 if executable else 0o644, "file"))
        files += list(extras)
        with tarfile.open(self.archive, "w:gz") as tf:
            root = tarfile.TarInfo("./")
            root.type = tarfile.DIRTYPE
            tf.addfile(root)
            for name, data, mode, kind in files:
                e = tarfile.TarInfo(name)
                e.mode = mode
                if kind == "symlink":
                    e.type = tarfile.SYMTYPE
                    e.linkname = "../../outside"
                    tf.addfile(e)
                elif kind == "hardlink":
                    e.type = tarfile.LNKTYPE
                    e.linkname = "./Haven"
                    tf.addfile(e)
                elif kind == "directory":
                    e.type = tarfile.DIRTYPE
                    tf.addfile(e)
                elif kind == "fifo":
                    e.type = tarfile.FIFOTYPE
                    tf.addfile(e)
                else:
                    e.size = len(data)
                    tf.addfile(e, io.BytesIO(data))
        return hashlib.sha256(self.archive.read_bytes()).hexdigest()

    def test_real_archive_verify_and_private_install(self):
        sha = self.package([("./assets/data.txt", b"test\n", 0o644, "file")])
        checks = inspect_archive(self.archive, sha)
        self.assertEqual(checks["status"], "PASS")
        self.assertEqual(checks["drive_login"], "NOT_RUN")
        destination = self.private / "haven-test-commit"
        result = install_archive(self.archive, sha, destination)
        self.assertTrue(result["installed"])
        self.assertEqual((destination / "assets/data.txt").read_bytes(), b"test\n")
        self.assertEqual((destination / "Haven").read_bytes(), ELF_X64)
        self.assertEqual(stat.S_IMODE(destination.stat().st_mode), 0o700)
        self.assertEqual(stat.S_IMODE((destination / "Haven").stat().st_mode), 0o700)
        self.assertEqual(stat.S_IMODE((destination / "Haven.dll").stat().st_mode), 0o600)
        with self.assertRaisesRegex(AdmissionError, "installed safely"):
            
            install_archive(self.archive, sha, destination)
        self.assertEqual((destination / "Haven").read_bytes(), ELF_X64)

    def test_incorrect_digest_refuses_without_creating_directory(self):
        self.package()
        with self.assertRaisesRegex(AdmissionError, "SHA-256 differs"):
            install_archive(self.archive, "0" * 64, self.private / "missing")
        self.assertFalse((self.private / "missing").exists())

    def test_bad_sha_format_is_refused(self):
        self.package()
        with self.assertRaises(AdmissionError):
            inspect_archive(self.archive, "123")

    def test_archive_symlink_is_refused(self):
        sha = self.package()
        link = self.root / "link.tar.gz"
        link.symlink_to(self.archive)
        with self.assertRaisesRegex(AdmissionError, "symlink"):
            inspect_archive(link, sha)

    def test_path_traversal_is_refused(self):
        for name in ("../../escape", "./x/../escape", "/absolute/path"):
            with self.subTest(name=name):
                sha = self.package([(name, b"bad", 0o644, "file")])
                with self.assertRaises(AdmissionError):
                    install_archive(self.archive, sha, self.private / "safe")
                self.assertFalse((self.private / "safe").exists())
                self.assertFalse((self.root / "escape").exists())

    def test_symlink_hardlink_fifo_entries_are_refused(self):
        for kind in ("symlink", "hardlink", "fifo"):
            with self.subTest(kind=kind):
                sha = self.package([("./poison", b"", 0o600, kind)])
                with self.assertRaisesRegex(AdmissionError, "link or special"):
                    inspect_archive(self.archive, sha)

    def test_duplicate_normalized_path_is_refused(self):
        sha = self.package([("Haven", b"overwrite", 0o755, "file")])
        with self.assertRaisesRegex(AdmissionError, "duplicate"):
            inspect_archive(self.archive, sha)

    def test_required_runtime_and_executable_mode_are_checked(self):
        sha = self.package(include_host=False)
        with self.assertRaisesRegex(AdmissionError, "missing"):
            inspect_archive(self.archive, sha)
        sha = self.package(executable=False)
        with self.assertRaisesRegex(AdmissionError, "owner-executable"):
            inspect_archive(self.archive, sha)

    def test_elf_identity_fails_closed(self):
        for host in (b"#!/bin/bash\n", b"\x7fELF\x01\x01" + bytes(14), b"\x7fELF\x02\x01" + bytes(14)):
            with self.subTest(host=host[:8]):
                sha = self.package(host_bytes=host)
                with self.assertRaisesRegex(AdmissionError, "x86-64"):
                    inspect_archive(self.archive, sha)

    def test_existing_destination_remains_unmodified(self):
        sha = self.package()
        dest = self.private / "existing"
        dest.mkdir()
        marker = dest / "saved.txt"
        marker.write_text("precious")
        with self.assertRaisesRegex(AdmissionError, "installed safely"):
            
            install_archive(self.archive, sha, dest)
        self.assertEqual(marker.read_text(), "precious")

    def test_parent_permissions_and_symlinks_are_refused(self):
        sha = self.package()
        public = self.root / "public"
        public.mkdir(mode=0o755)
        with self.assertRaisesRegex(AdmissionError, "0700"):
            install_archive(self.archive, sha, public / "candidate")
        link = self.root / "private-link"
        link.symlink_to(self.private, target_is_directory=True)
        with self.assertRaises(AdmissionError):
            install_archive(self.archive, sha, link / "candidate")
        self.assertFalse((self.private / "candidate").exists())

    def test_non_archive_is_refused_without_traceback(self):
        self.archive.write_text("hello")
        sha = hashlib.sha256(self.archive.read_bytes()).hexdigest()
        with self.assertRaisesRegex(AdmissionError, "parsed"):
            inspect_archive(self.archive, sha)

    def test_file_as_parent_is_refused(self):
        sha = self.package([("./Haven/child", b"x", 0o644, "file")])
        with self.assertRaisesRegex(AdmissionError, "parent"):
            inspect_archive(self.archive, sha)

    def test_truncation_during_extract_cleans_own_directory(self):
        sha = self.package()
        from native_files_package_admission import copy_member
        dest = self.private / "truncated"
        with patch("native_files_package_admission.copy_member", side_effect=AdmissionError("truncated")):
            with self.assertRaises(AdmissionError):
                install_archive(self.archive, sha, dest)
        self.assertFalse(dest.exists())

    def test_cli_outputs_fixed_refusal_without_sensitive_path(self):
        dest = self.private / "private-OAuth-dont-display"
        from contextlib import redirect_stdout
        output = io.StringIO()
        with redirect_stdout(output):
            code = main(["verify", "--archive", str(self.root / "private-secret-file"), "--sha256", "0" * 64])
        self.assertEqual(code, 2)
        self.assertNotIn("private-secret-file", output.getvalue())
        self.assertEqual(json.loads(output.getvalue())["status"], "BLOCKED")
        self.assertFalse(dest.exists())

    def test_reject_overexpanded_member_header_before_copy(self):
        sha = self.package()
        with patch("native_files_package_admission.MAX_MEMBER_BYTES", 2):
            with self.assertRaisesRegex(AdmissionError, "expanded"):
                inspect_archive(self.archive, sha)


    def test_fd_archive_fifo_refused_without_blocking(self):
        fifo = self.root / "fifo.tar.gz"
        os.mkfifo(fifo)
        with self.assertRaises(AdmissionError):
            inspect_archive(fifo, "0" * 64)

    def test_fd_archive_symlink_replacement_at_open_refused(self):
        sha = self.package()
        alternate = self.root / "alternate.tar.gz"
        alternate.write_bytes(self.archive.read_bytes())
        moved = self.root / "moved-original.tar.gz"
        real_open = os.open
        swapped = False

        def replace_at_open(path, flags, *args, **kwargs):
            nonlocal swapped
            if str(path) == str(self.archive) and not swapped:
                swapped = True
                self.archive.rename(moved)
                self.archive.symlink_to(alternate)
            return real_open(path, flags, *args, **kwargs)

        with patch("native_files_package_admission.os.open", side_effect=replace_at_open):
            with self.assertRaises(AdmissionError):
                inspect_archive(self.archive, sha)
        self.assertTrue(swapped)

    def test_fd_swapped_destination_after_mkdir_does_not_write_victim(self):
        sha = self.package()
        destination = self.private / "candidate"
        kept = self.private / "relocated-original"
        victim = self.root / "victim"
        victim.mkdir(mode=0o700)
        (victim / "precious.txt").write_text("preserve")
        real_mkdir = os.mkdir
        swapped = False

        def replace_after_mkdir(path, *args, **kwargs):
            nonlocal swapped
            value = real_mkdir(path, *args, **kwargs)
            if path == destination.name and kwargs.get("dir_fd") is not None and not swapped:
                swapped = True
                destination.rename(kept)
                destination.symlink_to(victim, target_is_directory=True)
            return value

        with patch("native_files_package_admission.os.mkdir", side_effect=replace_after_mkdir):
            with self.assertRaises(AdmissionError):
                install_archive(self.archive, sha, destination)
        self.assertTrue(swapped)
        self.assertEqual((victim / "precious.txt").read_text(), "preserve")
        self.assertFalse((victim / "Haven").exists())

    def test_fd_swapped_destination_mid_copy_does_not_mutate_or_clean_victim(self):
        sha = self.package()
        destination = self.private / "candidate"
        kept = self.private / "relocated-original"
        victim = self.root / "victim"
        victim.mkdir(mode=0o700)
        (victim / "precious.txt").write_text("preserve")
        from native_files_package_admission import copy_member
        swapped = False

        def replace_after_copy(*args, **kwargs):
            nonlocal swapped
            value = copy_member(*args, **kwargs)
            if not swapped:
                swapped = True
                destination.rename(kept)
                destination.symlink_to(victim, target_is_directory=True)
            return value

        with patch("native_files_package_admission.copy_member", side_effect=replace_after_copy):
            with self.assertRaises(AdmissionError):
                install_archive(self.archive, sha, destination)
        self.assertTrue(swapped)
        self.assertTrue((kept / "Haven").exists())
        self.assertFalse((victim / "Haven").exists())
        self.assertEqual((victim / "precious.txt").read_text(), "preserve")

    def test_fd_same_open_archive_mutation_invalidates_install_and_rolls_back(self):
        sha = self.package()
        destination = self.private / "candidate"
        from native_files_package_admission import copy_member
        changed = False

        def mutate_after_copy(*args, **kwargs):
            nonlocal changed
            result = copy_member(*args, **kwargs)
            if not changed:
                changed = True
                with self.archive.open("ab") as stream:
                    stream.write(b"unexpected-same-inode-change")
            return result

        with patch("native_files_package_admission.copy_member", side_effect=mutate_after_copy):
            with self.assertRaises(AdmissionError):
                install_archive(self.archive, sha, destination)
        self.assertFalse(destination.exists())


if __name__ == "__main__":
    unittest.main()
