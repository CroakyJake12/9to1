"""Disposable Linux Git, subprocess, tar and filesystem acceptance for source build orchestration.

The fake publisher emits tiny synthetic ELF fixture bytes. Real Haven compilation and
X11 startup belong to the separately serialised nondeploying native CI workflow.
"""
from __future__ import annotations

import json
import os
from pathlib import Path
import stat
import subprocess
import tempfile
import unittest
from unittest import mock

import native_files_source_candidate as candidate

PUBLISH_SCRIPT = r'''#!/bin/bash
set -euo pipefail
out="$HAVEN_ARTIFACTS_DIR/publish"
mkdir -p "$out"
# Synthetic ELF x64 apphost, solely for package format/admission tests.
python3 - "$out" <<'PYFIX'
import pathlib,sys
root=pathlib.Path(sys.argv[1]); (root/'Haven').write_bytes(b'\x7fELF\x02\x01'+b'\0'*12+b'\x3e\x00'+b'\0'*32)
(root/'Haven').chmod(0o700)
(root/'Haven.dll').write_bytes(b'fixture-dll')
(root/'Haven.runtimeconfig.json').write_text('{}')
(root/'libfile.so').write_bytes(b'fixture-shared')
PYFIX
tar -czf "$HAVEN_ARTIFACTS_DIR/haven-linux-linux-x64.tar.gz" -C "$out" .
if [[ -n "${GOOGLE_CLIENT_SECRET:-}" || -n "${GITHUB_TOKEN:-}" ]]; then
  exit 79
fi
'''


def run(*args: str, cwd: Path | None = None) -> str:
    p = subprocess.run(list(args), cwd=cwd, capture_output=True, text=True, check=True)
    return p.stdout.strip()


class SourceCandidateTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="haven-source-candidate-fixtures-")
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.repo = self.root / "repo"
        self.repo.mkdir()
        (self.repo / candidate.PUBLISHER).parent.mkdir(parents=True)
        (self.repo / candidate.PUBLISHER).write_text(PUBLISH_SCRIPT)
        (self.repo / candidate.PROJECT).parent.mkdir(parents=True)
        (self.repo / candidate.PROJECT).write_text("<Project />\n")
        (self.repo / "global.json").write_text(json.dumps({"sdk": {
            "version": "10.0.301", "rollForward": "latestFeature", "allowPrerelease": False
        }}))
        run("git", "init", "-q", str(self.repo))
        run("git", "config", "user.email", "fixture@example.invalid", cwd=self.repo)
        run("git", "config", "user.name", "Fixture", cwd=self.repo)
        run("git", "add", ".", cwd=self.repo)
        run("git", "commit", "-qm", "fixture", cwd=self.repo)
        self.sha = run("git", "rev-parse", "HEAD", cwd=self.repo)
        self.private = self.root / "private"
        self.private.mkdir(mode=0o700)
        self.destination = self.private / "candidate-one"
        self.dotnet = self.root / "fake-dotnet"
        self.dotnet.write_text("#!/bin/sh\nif [ \"$1\" = --version ]; then echo 10.0.404; else exit 88; fi\n")
        self.dotnet.chmod(0o700)

    def build(self):
        return candidate.build_candidate(self.repo, self.sha, self.destination, dotnet=str(self.dotnet))

    def test_real_subprocess_git_publisher_and_package_parity(self):
        with mock.patch.dict(os.environ, {"GOOGLE_CLIENT_SECRET": "must-not-propagate", "GITHUB_TOKEN": "must-not-propagate"}):
            receipt = self.build()
        self.assertEqual(receipt["source_revision"], self.sha)
        self.assertEqual(receipt["dotnet_sdk"], "10.0.404")
        self.assertEqual(receipt["status"], "BUILT_NOT_DRIVE_ACCEPTED")
        self.assertEqual(receipt["real_drive_checks"], "NOT_RUN")
        self.assertEqual(receipt["graphical_launch"], "NOT_RUN")
        self.assertEqual(len(receipt["archive_sha256"]), 64)
        self.assertTrue((self.destination / "installed" / "Haven").is_file())
        self.assertEqual((self.destination / "installed" / "libfile.so").read_bytes(), b"fixture-shared")
        self.assertEqual(json.loads((self.destination / "receipt.json").read_text()), receipt)
        self.assertEqual(stat.S_IMODE((self.destination / "receipt.json").stat().st_mode), 0o600)
        self.assertEqual(stat.S_IMODE(self.destination.stat().st_mode), 0o700)
        self.assertEqual(stat.S_IMODE((self.destination / "publish.log").stat().st_mode), 0o600)

    def test_idempotency_refuses_overwriting_candidate(self):
        self.build()
        first = (self.destination / "receipt.json").read_bytes()
        with self.assertRaises(candidate.CandidateError):
            self.build()
        self.assertEqual((self.destination / "receipt.json").read_bytes(), first)

    def test_expected_revision_required_before_any_write(self):
        with self.assertRaises(candidate.CandidateError):
            candidate.build_candidate(self.repo, "0" * 40, self.destination, dotnet=str(self.dotnet))
        self.assertFalse(self.destination.exists())

    def test_refuse_untracked_source_before_any_write(self):
        (self.repo / "untracked.txt").write_text("not published")
        with self.assertRaises(candidate.CandidateError):
            self.build()
        self.assertFalse(self.destination.exists())

    def test_refuse_modified_tracked_source_before_any_write(self):
        (self.repo / candidate.PROJECT).write_text("modified")
        with self.assertRaises(candidate.CandidateError):
            self.build()
        self.assertFalse(self.destination.exists())

    def test_refuse_broad_sdk_policy_change(self):
        (self.repo / "global.json").write_text('{"sdk":{"version":"11.0.100"}}')
        run("git", "add", ".", cwd=self.repo)
        run("git", "commit", "-qm", "sdk", cwd=self.repo)
        self.sha = run("git", "rev-parse", "HEAD", cwd=self.repo)
        with self.assertRaises(candidate.CandidateError):
            self.build()
        self.assertFalse(self.destination.exists())

    def test_refuse_wrong_sdk_does_not_publish(self):
        self.dotnet.write_text("#!/bin/sh\necho 9.0.999\n")
        with self.assertRaisesRegex(candidate.CandidateError, "SDK"):
            self.build()
        self.assertFalse((self.destination / "artifacts" / candidate.ARCHIVE).exists())

    def test_refuse_missing_sdk(self):
        with self.assertRaises(candidate.CandidateError):
            candidate.build_candidate(self.repo, self.sha, self.destination, dotnet="/nonexistent/dotnet")
        self.assertFalse((self.destination / "artifacts" / candidate.ARCHIVE).exists())

    def test_refuse_nonprivate_destination_parent(self):
        self.private.chmod(0o755)
        with self.assertRaises(candidate.CandidateError):
            self.build()
        self.assertFalse(self.destination.exists())

    def test_refuse_existing_destination_symlink(self):
        self.destination.symlink_to(self.repo, target_is_directory=True)
        with self.assertRaises(candidate.CandidateError):
            self.build()
        self.assertTrue(self.destination.is_symlink())

    def test_refuse_destination_inside_source_repo(self):
        with self.assertRaises(candidate.CandidateError):
            candidate.build_candidate(self.repo, self.sha, self.repo / "candidate", dotnet=str(self.dotnet))
        self.assertFalse((self.repo / "candidate").exists())

    def test_refuse_publisher_changed_source(self):
        script = (self.repo / candidate.PUBLISHER)
        script.write_text(PUBLISH_SCRIPT + "\necho changed >> global.json\n")
        run("git", "add", ".", cwd=self.repo)
        run("git", "commit", "-qm", "fixture-modifies-tree", cwd=self.repo)
        self.sha = run("git", "rev-parse", "HEAD", cwd=self.repo)
        with self.assertRaises(candidate.CandidateError):
            self.build()
        self.assertFalse((self.destination / "receipt.json").exists())

    def test_refuse_broken_publisher_without_success_receipt(self):
        script = (self.repo / candidate.PUBLISHER)
        script.write_text("#!/bin/bash\nexit 37\n")
        run("git", "add", ".", cwd=self.repo)
        run("git", "commit", "-qm", "fixture-failure", cwd=self.repo)
        self.sha = run("git", "rev-parse", "HEAD", cwd=self.repo)
        with self.assertRaises(candidate.CandidateError):
            self.build()
        self.assertFalse((self.destination / "receipt.json").exists())
        self.assertEqual(stat.S_IMODE((self.destination / "publish.log").stat().st_mode), 0o600)

    def test_private_environment_has_no_source_account_credentials(self):
        # Destination is intentionally created as a private staging root.
        root = self.private / "candidate-env"
        root.mkdir(mode=0o700)
        env = candidate.private_environment(root, {"PATH": "/usr/bin", "GOOGLE_CLIENT_SECRET": "x",
                                                   "GITHUB_TOKEN": "y", "NUGET_AUTH_TOKEN": "z",
                                                   "OPENAI_API_KEY": "private", "HOME": "/actual/home"})
        self.assertEqual(env["PATH"], "/usr/bin")
        for key in ("GOOGLE_CLIENT_SECRET", "GITHUB_TOKEN", "NUGET_AUTH_TOKEN", "OPENAI_API_KEY"):
            self.assertNotIn(key, env)
        self.assertNotEqual(env["HOME"], "/actual/home")


class PublishIndexFdTests(unittest.TestCase):
    """Actual Linux file descriptor and symlink-race checks; no Drive account."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="haven-publish-index-")
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name) / "publish"
        self.root.mkdir()
        (self.root / "nested").mkdir()
        (self.root / "nested" / "document.bin").write_bytes(b"hello")
        (self.root / "Haven").write_bytes(b"elf")
        self.outside = Path(self.tmp.name) / "external.bin"
        self.outside.write_bytes(b"foreign")

    def test_nested_real_bytes_are_compared(self):
        import hashlib
        self.assertEqual(candidate._file_index(self.root), {
            "Haven": hashlib.sha256(b"elf").hexdigest(),
            "nested/document.bin": hashlib.sha256(b"hello").hexdigest()})

    def test_symlinked_file_is_not_hashed(self):
        target = self.root / "nested" / "document.bin"
        target.unlink()
        target.symlink_to(self.outside)
        with self.assertRaises(candidate.CandidateError):
            candidate._file_index(self.root)

    def test_symlinked_directory_is_not_traversed(self):
        (self.root / "nested" / "document.bin").unlink()
        (self.root / "nested").rmdir()
        (self.root / "nested").symlink_to(self.outside.parent, target_is_directory=True)
        with self.assertRaises(candidate.CandidateError):
            candidate._file_index(self.root)

    def test_racing_file_replacement_does_not_follow_symlink(self):
        target = self.root / "nested" / "document.bin"
        original_open = os.open
        def replace(path, flags, *args, **kwargs):
            if path == "document.bin" and kwargs.get("dir_fd") is not None and flags & os.O_NOFOLLOW:
                target.unlink()
                target.symlink_to(self.outside)
            return original_open(path, flags, *args, **kwargs)
        with mock.patch.object(candidate.os, "open", side_effect=replace):
            with self.assertRaises(candidate.CandidateError):
                candidate._file_index(self.root)
        self.assertEqual(self.outside.read_bytes(), b"foreign")

    def test_same_bytes_but_replaced_inode_is_refused(self):
        target = self.root / "nested" / "document.bin"
        original_stat = os.stat
        def replace(path, *args, **kwargs):
            if path == "document.bin" and kwargs.get("dir_fd") is not None and kwargs.get("follow_symlinks") is False:
                target.unlink()
                target.write_bytes(b"hello")
            return original_stat(path, *args, **kwargs)
        with mock.patch.object(candidate.os, "stat", side_effect=replace):
            with self.assertRaises(candidate.CandidateError):
                candidate._file_index(self.root)

    def test_symlinked_ancestor_of_publish_root_is_refused(self):
        alias = Path(self.tmp.name) / "alias"
        alias.symlink_to(self.root.parent, target_is_directory=True)
        with self.assertRaises(candidate.CandidateError):
            candidate._file_index(alias / "publish")

    def test_relative_publish_root_is_refused(self):
        with self.assertRaises(candidate.CandidateError):
            candidate._file_index(Path("relative/publish"))

    def test_symlinked_publish_root_is_refused(self):
        alias = Path(self.tmp.name) / "redirect"
        alias.symlink_to(self.root, target_is_directory=True)
        with self.assertRaises(candidate.CandidateError):
            candidate._file_index(alias)


if __name__ == "__main__":
    unittest.main()
