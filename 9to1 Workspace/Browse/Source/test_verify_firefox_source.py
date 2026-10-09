"""Offline regression tests for the verifier, using synthetic Git fixtures only."""
from pathlib import Path
import subprocess
import tempfile
import unittest

from verify_firefox_source import REQUIRED_FILES, SOURCE_PATH, SOURCE_URL, git, verify


class SourceVerificationTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.source = self.root / SOURCE_PATH
        self.source.mkdir(parents=True)
        for directory in (self.root, self.source):
            subprocess.run(["git", "init", "-q", str(directory)], check=True)
            git(directory, "config", "user.name", "Verifier test")
            git(directory, "config", "user.email", "verifier@example.invalid")
        for relative in REQUIRED_FILES:
            path = self.source / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("Synthetic test fixture, not Mozilla source.\n")
        git(self.source, "add", ".")
        git(self.source, "commit", "-qm", "Synthetic donor fixture")
        self.lock = {
            "schema": 1, "path": SOURCE_PATH, "url": SOURCE_URL,
            "commit": git(self.source, "rev-parse", "HEAD").strip(),
            "tree": git(self.source, "rev-parse", "HEAD^{tree}").strip(),
        }
        (self.root / ".gitmodules").write_text(
            f'[submodule "{SOURCE_PATH}"]\n\tpath = {SOURCE_PATH}\n\turl = {SOURCE_URL}\n'
        )
        git(self.root, "add", ".gitmodules")
        git(self.root, "update-index", "--add", "--cacheinfo", "160000",
            self.lock["commit"], SOURCE_PATH)
        git(self.root, "commit", "-qm", "Pin fixture donor")

    def test_complete_checkout(self):
        result = verify(self.root, self.lock)
        self.assertTrue(result["full_source_checkout_verified"])
        self.assertEqual(len(REQUIRED_FILES), result["tracked_files_present"])
        self.assertEqual("not assessed", result["cakeui_runtime_integration"])

    def test_declared_but_missing_gitlink(self):
        git(self.root, "update-index", "--force-remove", SOURCE_PATH)
        git(self.root, "commit", "-qm", "Remove fixture gitlink")
        with self.assertRaisesRegex(RuntimeError, "gitlink"):
            verify(self.root, self.lock)

    def test_wrong_pin(self):
        self.lock["commit"] = "0" * 40
        with self.assertRaisesRegex(RuntimeError, "gitlink"):
            verify(self.root, self.lock)

    def test_wrong_tree(self):
        self.lock["tree"] = "0" * 40
        with self.assertRaisesRegex(RuntimeError, "tree"):
            verify(self.root, self.lock)

    def test_dirty_source(self):
        (self.source / "mach").write_text("modified\n")
        with self.assertRaisesRegex(RuntimeError, "modified"):
            verify(self.root, self.lock)

    def test_missing_file(self):
        (self.source / "mach").unlink()
        with self.assertRaisesRegex(RuntimeError, "missing"):
            verify(self.root, self.lock)

    def test_sparse_source(self):
        git(self.source, "update-index", "--skip-worktree", "mach")
        with self.assertRaisesRegex(RuntimeError, "Sparse"):
            verify(self.root, self.lock)

    def test_hidden_modifications_rejected(self):
        git(self.source, "update-index", "--assume-unchanged", "mach")
        with self.assertRaisesRegex(RuntimeError, "Assume-unchanged"):
            verify(self.root, self.lock)

    def test_wrong_url(self):
        git(self.root, "config", "-f", ".gitmodules", f"submodule.{SOURCE_PATH}.url",
            "https://example.invalid/not-mozilla.git")
        with self.assertRaisesRegex(RuntimeError, "URL"):
            verify(self.root, self.lock)

    def test_bad_lock(self):
        self.lock["path"] = "../outside"
        with self.assertRaisesRegex(RuntimeError, "path"):
            verify(self.root, self.lock)


if __name__ == "__main__":
    unittest.main()
