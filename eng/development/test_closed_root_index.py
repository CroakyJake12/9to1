"""Bounded real-Git controls; fixtures never use a programme repository/index."""

import gzip
import hashlib
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import tempfile
import unittest
from unittest import mock

import closed_root_index as tool
import native_source_identity


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


class ClosedRootIndexTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="closed-root-index-test-")
        self.addCleanup(self.temp.cleanup)
        self.base = Path(self.temp.name)
        self.repo = self.base / "repo"
        self.repo.mkdir()
        self.empty = self.base / "empty-template"
        self.empty.mkdir()
        self.git("init", "-q", "--template=" + str(self.empty), "-b", "main")
        self.git("config", "user.name", "Tiny fixture")
        self.git("config", "user.email", "fixture@example.invalid")
        (self.repo / "a.txt").write_text("alpha\n")
        (self.repo / "executable").write_text("beta\n")
        (self.repo / "executable").chmod(0o755)
        self.git("add", "--", "a.txt", "executable")
        self.git("commit", "-q", "-m", "Tiny committed fixture")
        self.revision = self.git("rev-parse", "HEAD").decode().strip()
        self.tree = self.git("rev-parse", "HEAD^{tree}").decode().strip()
        self.index = self.base / "9to1-root-compose7.index"
        shutil.copyfile(self.repo / ".git/index", self.index)
        self.index.chmod(0o600)
        self.raw = self.index.read_bytes()
        self.canonical = (self.repo / ".git/index").read_bytes()
        self.archive = self.base / "closed.index.gz"
        self.receipt = self.base / "prepared.json"

    def git(self, *args, index=None, input=None, repo=None):
        env = native_source_identity.git_environment()
        env.update(GIT_AUTHOR_DATE="2026-10-24T00:00:00+0000",
                   GIT_COMMITTER_DATE="2026-10-24T00:00:00+0000")
        if index is not None:
            env["GIT_INDEX_FILE"] = str(index)
        # Git replaces indexes via lock files using the child process umask.
        # Keep private fixture indexes private under the CI runner's usual 022.
        return subprocess.check_output(["git", "--no-optional-locks", "-C", str(repo or self.repo), *args],
                                       input=input, stderr=subprocess.PIPE, env=env, umask=0o077)

    def prepare(self, **overrides):
        values = dict(repo=self.repo, index=self.index, expected_sha256=sha(self.index),
                      revision=self.revision, tree=self.tree, archive=self.archive,
                      receipt=self.receipt, closed=True)
        values.update(overrides)
        return tool.prepare(**values)

    def refused(self, pattern=None, **overrides):
        context = self.assertRaisesRegex((tool.Refusal, OSError, ValueError), pattern) if pattern else self.assertRaises((tool.Refusal, OSError, ValueError))
        with context:
            self.prepare(**overrides)
        self.assertEqual((self.repo / ".git/index").read_bytes(), self.canonical)

    def test_archive_recovers_exact_bytes_and_preserves_indices_refs_and_files(self):
        before = self.index.stat()
        refs = self.git("show-ref")
        result = self.prepare()
        self.assertEqual(result["status"], "PREPARED_NO_UNLINK")
        self.assertFalse(result["raw_removed"])
        self.assertFalse(result["adopted"])
        self.assertFalse(result["sdk_launch_admitted"])
        self.assertEqual(gzip.decompress(self.archive.read_bytes()), self.raw)
        self.assertEqual(self.index.read_bytes(), self.raw)
        after = self.index.stat()
        self.assertEqual((after.st_ino, after.st_size, after.st_mtime_ns, after.st_ctime_ns),
                         (before.st_ino, before.st_size, before.st_mtime_ns, before.st_ctime_ns))
        self.assertEqual((self.repo / ".git/index").read_bytes(), self.canonical)
        self.assertEqual(self.git("show-ref"), refs)
        self.assertEqual((self.repo / "a.txt").read_text(), "alpha\n")
        self.assertEqual(json.loads(self.receipt.read_bytes()), result)
        self.assertEqual(result["committed"]["stage0_entries"], 2)
        for path in (self.archive, self.receipt):
            self.assertEqual(stat.S_IMODE(path.stat().st_mode), 0o600)

    def test_gzip_is_deterministic_for_the_exact_index_body(self):
        self.prepare()
        second = self.base / "second.gz"
        self.prepare(archive=second, receipt=self.base / "second.json")
        self.assertEqual(second.read_bytes(), self.archive.read_bytes())

    def test_requires_closed_assertion_and_exact_root_cache_name(self):
        for args in [dict(closed=False), dict(index=self.base / "ordinary.index")]:
            with self.subTest(args=args):
                self.refused(**args)
        self.assertFalse(self.archive.exists())

    def test_wrong_sha_revision_and_tree_refuse_before_outputs(self):
        for args in [dict(expected_sha256="0" * 64), dict(revision="HEAD"),
                     dict(revision="0" * 40), dict(tree="0" * 40),
                     dict(expected_sha256=None)]:
            with self.subTest(args=args):
                self.refused(**args)
        self.assertFalse(self.archive.exists())
        self.assertFalse(self.receipt.exists())

    def test_real_index_mode_difference_is_not_a_closed_committed_cache(self):
        self.git("update-index", "--chmod=-x", "executable", index=self.index)
        self.refused("stage-0")
        self.assertFalse(self.archive.exists())

    def test_real_unmerged_stages_are_rejected_and_retained(self):
        oid = self.git("rev-parse", "HEAD:a.txt").decode().strip()
        rows = ("0 " + "0" * 40 + "\ta.txt\n" +
                "100644 " + oid + " 1\ta.txt\n" +
                "100644 " + oid + " 2\ta.txt\n" +
                "100644 " + oid + " 3\ta.txt\n").encode()
        self.git("update-index", "--index-info", index=self.index, input=rows)
        retained = self.index.read_bytes()
        self.refused("Unmerged")
        self.assertEqual(self.index.read_bytes(), retained)
        self.assertFalse(self.archive.exists())

    def test_real_split_index_is_rejected_before_git_semantic_reads(self):
        self.git("update-index", "--split-index", index=self.index)
        retained = self.index.read_bytes()
        with mock.patch.object(tool, "_git", side_effect=AssertionError("must not read shared index")):
            self.refused("Split index")
        self.assertEqual(self.index.read_bytes(), retained)

    def test_real_v4_index_is_explicitly_unsupported(self):
        self.git("update-index", "--index-version=4", index=self.index)
        self.refused("versions 2/3")

    def test_checksum_corruption_is_rejected(self):
        raw = bytearray(self.raw)
        raw[-1] ^= 1
        self.index.write_bytes(raw)
        self.refused("checksum")

    def test_bound_index_bytes_before_loading_or_git(self):
        with mock.patch.object(tool, "MAX_INDEX_BYTES", len(self.raw) - 1):
            self.refused("8 MiB")

    def test_nonprivate_mode_is_rejected(self):
        self.index.chmod(0o644)
        self.refused("private")

    def test_lock_and_dangling_lock_symlink_are_both_preserved(self):
        lock = self.index.with_name(self.index.name + ".lock")
        lock.write_text("owner")
        self.refused("lock")
        self.assertEqual(lock.read_text(), "owner")
        lock.unlink()  # Only this test-owned fixture file.
        lock.symlink_to(self.base / "missing-owner")
        self.refused("lock")
        self.assertTrue(lock.is_symlink())
        self.assertFalse(self.archive.exists())

    def test_hardlinked_input_is_refused_without_changing_either_name(self):
        other = self.base / "retained-index"
        os.link(self.index, other)
        self.refused("nlink")
        self.assertEqual(other.read_bytes(), self.raw)
        self.assertEqual(self.index.read_bytes(), self.raw)

    def test_symlink_input_and_symlink_parent_are_refused(self):
        link = self.base / "9to1-root-compose8.index"
        link.symlink_to(self.index)
        self.refused(index=link)
        directory = self.base / "linked-parent"
        directory.symlink_to(self.base, target_is_directory=True)
        self.refused(index=directory / self.index.name)
        self.assertFalse(self.archive.exists())

    def test_fifo_is_refused_without_opening_or_blocking(self):
        fifo = self.base / "9to1-root-compose9.index"
        os.mkfifo(fifo, 0o600)
        self.refused(index=fifo, expected_sha256=sha(self.index))

    def test_repo_index_and_named_cache_inside_worktree_are_refused(self):
        self.refused(index=self.repo / ".git/index")
        inside = self.repo / self.index.name
        shutil.copyfile(self.index, inside)
        inside.chmod(0o600)
        self.refused(index=inside)
        self.refused(archive=self.repo / "archive.gz")
        self.refused(receipt=self.repo / ".git/receipt.json")

    def test_linked_worktree_and_unrelated_bare_git_output_are_refused(self):
        worktree = self.base / "linked-worktree"
        self.git("worktree", "add", "--quiet", "--detach", str(worktree), self.revision)
        self.refused(archive=worktree / "archive.gz")
        bare = self.base / "unrelated-bare"
        self.git("init", "--quiet", "--bare", "--template=" + str(self.empty), str(bare))
        self.refused(receipt=bare / "receipt.json")

    def test_existing_either_output_is_preserved_before_creating_the_other(self):
        self.receipt.write_bytes(b"existing receipt")
        self.refused("Existing output")
        self.assertEqual(self.receipt.read_bytes(), b"existing receipt")
        self.assertFalse(self.archive.exists())
        self.receipt.unlink()  # Only this test-owned fixture file.
        self.archive.write_bytes(b"existing archive")
        self.refused("Existing output")
        self.assertEqual(self.archive.read_bytes(), b"existing archive")
        self.assertFalse(self.receipt.exists())

    def test_dangling_output_symlink_and_symlink_parent_are_preserved(self):
        self.archive.symlink_to(self.base / "missing")
        self.refused("Existing output")
        self.assertTrue(self.archive.is_symlink())
        parent = self.base / "output-link"
        parent.symlink_to(self.base, target_is_directory=True)
        self.refused(archive=parent / "new.gz")

    def test_paths_must_be_distinct_absolute_and_nontraversing(self):
        for args in [dict(receipt=self.archive), dict(archive=self.index),
                     dict(archive=Path("relative.gz")),
                     dict(receipt=Path(str(self.base) + "/../escape.json"))]:
            with self.subTest(args=args):
                self.refused(**args)

    def test_input_mutation_during_readonly_git_is_detected_before_archive(self):
        original = tool._git
        def changed(repo, index, *args):
            result = original(repo, index, *args)
            if args[:1] == ("ls-tree",):
                self.index.write_bytes(self.raw + b"changed")
            return result
        with mock.patch.object(tool, "_git", side_effect=changed):
            self.refused("identity changed")
        self.assertFalse(self.archive.exists())
        self.assertEqual(self.index.read_bytes(), self.raw + b"changed")

    def test_input_mutation_after_archive_keeps_archive_and_refuses_receipt(self):
        original = tool._verify_archive
        def changed(*args):
            result = original(*args)
            self.index.write_bytes(self.raw + b"changed")
            return result
        with mock.patch.object(tool, "_verify_archive", side_effect=changed):
            self.refused("identity changed")
        self.assertEqual(gzip.decompress(self.archive.read_bytes()), self.raw)
        self.assertFalse(self.receipt.exists())
        self.assertEqual(self.index.read_bytes(), self.raw + b"changed")

    def test_archive_damage_keeps_failure_artifact_and_original_index(self):
        original = tool._verify_archive
        def damaged(*args):
            self.archive.write_bytes(b"damaged gzip")
            return original(*args)
        with mock.patch.object(tool, "_verify_archive", side_effect=damaged):
            self.refused()
        self.assertEqual(self.archive.read_bytes(), b"damaged gzip")
        self.assertEqual(self.index.read_bytes(), self.raw)
        self.assertFalse(self.receipt.exists())

    def test_complete_gzip_footer_exists_before_file_fsync(self):
        real_fsync = os.fsync
        observed = []
        def fsync(fd):
            info = os.fstat(fd)
            if self.archive.exists() and info.st_ino == self.archive.stat().st_ino:
                self.assertEqual(gzip.decompress(self.archive.read_bytes()), self.raw)
                observed.append("complete archive")
            return real_fsync(fd)
        with mock.patch.object(tool.os, "fsync", side_effect=fsync):
            self.prepare()
        self.assertEqual(observed, ["complete archive"])

    def test_receipt_creation_race_preserves_both_new_archive_and_existing_receipt(self):
        original = tool._new_file
        def raced(parent):
            if parent.path == self.receipt:
                self.receipt.write_bytes(b"other owner receipt")
            return original(parent)
        with mock.patch.object(tool, "_new_file", side_effect=raced):
            self.refused()
        self.assertEqual(self.receipt.read_bytes(), b"other owner receipt")
        self.assertEqual(gzip.decompress(self.archive.read_bytes()), self.raw)
        self.assertEqual(self.index.read_bytes(), self.raw)

    def test_environment_redirects_are_ignored_and_explicit_index_wins(self):
        with mock.patch.dict(os.environ, {"GIT_INDEX_FILE": str(self.base / "missing-index"),
                                         "GIT_DIR": str(self.base / "missing.git"),
                                         "GIT_CONFIG_COUNT": "1", "GIT_CONFIG_KEY_0": "core.fsmonitor",
                                         "GIT_CONFIG_VALUE_0": "/not-a-command"}):
            self.prepare()
        self.assertEqual(self.index.read_bytes(), self.raw)
        self.assertEqual((self.repo / ".git/index").read_bytes(), self.canonical)

    def test_readonly_git_output_bound_is_enforced(self):
        with mock.patch.object(tool, "MAX_GIT_BYTES", 1):
            self.refused("output bound")
        self.assertFalse(self.archive.exists())

    def test_same_body_index_replacement_during_git_is_refused(self):
        original = tool._git
        def replaced(repo, index, *args):
            result = original(repo, index, *args)
            if args[:1] == ("ls-tree",):
                replacement = self.base / "new-index"
                replacement.write_bytes(self.raw)
                replacement.chmod(0o600)
                os.replace(replacement, self.index)
            return result
        with mock.patch.object(tool, "_git", side_effect=replaced):
            self.refused("identity changed")
        self.assertEqual(self.index.read_bytes(), self.raw)
        self.assertFalse(self.archive.exists())

    def test_lock_appearing_during_git_is_retained_and_refused(self):
        original = tool._git
        lock = self.index.with_name(self.index.name + ".lock")
        def locked(repo, index, *args):
            result = original(repo, index, *args)
            if args[:1] == ("ls-tree",):
                lock.write_bytes(b"new owner lock")
            return result
        with mock.patch.object(tool, "_git", side_effect=locked):
            self.refused("lock")
        self.assertEqual(lock.read_bytes(), b"new owner lock")
        self.assertFalse(self.archive.exists())

    def test_git_marker_appearing_during_git_refuses_before_archive(self):
        original = tool._git
        def marker(repo, index, *args):
            result = original(repo, index, *args)
            if args[:1] == ("ls-tree",):
                (self.base / ".git").write_text("new worktree marker")
            return result
        with mock.patch.object(tool, "_git", side_effect=marker):
            self.refused("outside Git")
        self.assertFalse(self.archive.exists())
        self.assertFalse(self.receipt.exists())

    def test_git_marker_appearing_after_archive_keeps_it_and_refuses_receipt(self):
        original = tool._verify_archive
        def marker(*args):
            result = original(*args)
            (self.base / ".git").write_text("new worktree marker")
            return result
        with mock.patch.object(tool, "_verify_archive", side_effect=marker):
            self.refused("outside Git")
        self.assertEqual(gzip.decompress(self.archive.read_bytes()), self.raw)
        self.assertFalse(self.receipt.exists())

    def test_same_body_receipt_replacement_after_fsync_is_refused(self):
        real_fsync = os.fsync
        replaced = []
        def fsync(fd):
            result = real_fsync(fd)
            info = os.fstat(fd)
            if (not replaced and self.receipt.exists()
                    and info.st_ino == self.receipt.stat().st_ino):
                replacement = self.base / "replacement-receipt"
                replacement.write_bytes(self.receipt.read_bytes())
                replacement.chmod(0o600)
                os.replace(replacement, self.receipt)
                replaced.append(True)
            return result
        with mock.patch.object(tool.os, "fsync", side_effect=fsync):
            self.refused("Receipt identity changed")
        self.assertEqual(replaced, [True])
        self.assertTrue(self.receipt.exists())
        self.assertEqual(gzip.decompress(self.archive.read_bytes()), self.raw)
        self.assertEqual(self.index.read_bytes(), self.raw)

    def test_decompression_length_limit_rejects_a_valid_gzip_extra_body(self):
        original = tool._verify_archive
        def larger(*args):
            self.archive.write_bytes(gzip.compress(self.raw + b"excess body", mtime=0))
            return original(*args)
        with mock.patch.object(tool, "_verify_archive", side_effect=larger):
            self.refused("exceeds the original length")
        self.assertEqual(gzip.decompress(self.archive.read_bytes()), self.raw + b"excess body")
        self.assertFalse(self.receipt.exists())
        self.assertEqual(self.index.read_bytes(), self.raw)

    def test_git_trace_file_environment_cannot_create_extra_outputs(self):
        trace = self.base / "unexpected-trace"
        event = self.base / "unexpected-trace2"
        with mock.patch.dict(os.environ, {"GIT_TRACE": str(trace),
                                         "GIT_TRACE2_EVENT": str(event)}):
            self.prepare()
        self.assertFalse(trace.exists())
        self.assertFalse(event.exists())
        self.assertEqual(self.index.read_bytes(), self.raw)

    def test_readonly_git_time_bound_is_enforced(self):
        with mock.patch.object(tool, "GIT_SECONDS", 0):
            self.refused("time bound")
        self.assertFalse(self.archive.exists())

    def test_cli_requires_closed_and_reports_prepare_without_unlink(self):
        args = ["--repo", str(self.repo), "--index", str(self.index),
                "--expected-sha256", sha(self.index), "--revision", self.revision,
                "--tree", self.tree, "--archive", str(self.archive), "--receipt", str(self.receipt)]
        with self.assertRaises(SystemExit) as error:
            tool.main(args)
        self.assertEqual(error.exception.code, 2)
        with mock.patch("builtins.print") as output:
            self.assertEqual(tool.main(args + ["--closed"]), 0)
        self.assertEqual(json.loads(output.call_args.args[0])["status"], "PREPARED_NO_UNLINK")
        self.assertEqual(self.index.read_bytes(), self.raw)


if __name__ == "__main__":
    unittest.main()
