"""Real tiny Git fixtures only; no SDK, network, native or release acceptance."""

import contextlib
import copy
import hashlib
import io
import json
import os
from pathlib import Path
import stat
import subprocess
import tempfile
import unittest
from unittest import mock

import native_source_identity as identity


class NativeSourceIdentityTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(
            prefix="i-", dir=os.environ.get("NINETOONE_SOURCE_IDENTITY_TEST_TMP"))
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.repo = self.root / "repo"
        self.empty = self.root / "empty"
        self.empty.mkdir()
        self.repo.mkdir()
        self.env = dict(os.environ, GIT_CONFIG_GLOBAL=os.devnull, GIT_CONFIG_NOSYSTEM="1",
                        GIT_AUTHOR_NAME="Identity fixture", GIT_AUTHOR_EMAIL="identity@example.invalid",
                        GIT_COMMITTER_NAME="Identity fixture", GIT_COMMITTER_EMAIL="identity@example.invalid",
                        GIT_AUTHOR_DATE="2026-10-24T00:00:00+00:00",
                        GIT_COMMITTER_DATE="2026-10-24T00:00:00+00:00")
        self.git("init", "--quiet", "--template=" + str(self.empty), "-b", "main")
        self.put("keep.txt", b"old retained input\n")
        self.put("drop.txt", b"unpinned old file\n")
        self.put(".github/control.yml", b"old metadata\n")
        self.base = self.commit("base")
        self.prior = {
            "source_commit": self.base,
            "patch_sha256": "1" * 64,
            "original_native16_patch_sha256": "2" * 64,
            "prepared_at": "historical timestamp retained",
            "original_native16_provenance": {"source_commit": self.base, "proof": "historical"},
            "lesson_preview_components": {"old": {"tests": "PASS for old source only"}},
            "acceptance": {"old_candidate": "PASS historical only", "current_combined_candidate": "PASS stale"},
            "changed_files_sha256": {"keep.txt": self.sha(b"old retained input\n"),
                                      ".github/control.yml": self.sha(b"old metadata\n"),
                                      identity.IDENTITY: "0" * 64},
            "current_candidate_input_contract": {"inference_input_count": 44},
        }

    def git(self, *arguments):
        return subprocess.check_output(["git", "--no-optional-locks", "-C", str(self.repo), *arguments],
                                       stderr=subprocess.PIPE, env=self.env).decode().strip()

    def put(self, name, data):
        path = self.repo / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        return path

    @staticmethod
    def sha(data):
        return hashlib.sha256(data).hexdigest()

    def commit(self, message, stage=True):
        if stage:
            self.git("add", "--all")
        self.git("commit", "--quiet", "-m", message)
        return self.git("rev-parse", "HEAD")

    def candidate(self):
        self.put(identity.IDENTITY, identity.workflow.encoded(self.prior))
        return self.commit("candidate")

    def test_union_new_changed_prior_and_github_pins_preserves_history(self):
        self.put("keep.txt", b"current input\n")
        self.put("new.txt", b"new product\n")
        self.put(".github/new.yml", b"new CI metadata\n")
        revision = self.candidate()
        result = identity.generate(self.repo, revision)
        expected = {name: self.sha((self.repo / name).read_bytes()) for name in
                    ("keep.txt", "new.txt", ".github/control.yml", ".github/new.yml")}
        self.assertEqual(result["changed_files_sha256"], expected)
        self.assertNotIn(identity.IDENTITY, result["changed_files_sha256"])
        for key in ("source_commit", "prepared_at", "original_native16_provenance",
                    "lesson_preview_components", "original_native16_patch_sha256"):
            self.assertEqual(result[key], self.prior[key])
        self.assertEqual(result["acceptance"]["old_candidate"], "PASS historical only")
        self.assertEqual(result["acceptance"]["current_combined_candidate"], identity.NOT_RUN)
        contract = result["current_candidate_input_contract"]
        self.assertFalse(contract["regeneration_is_source_review_or_acceptance"])
        self.assertEqual(contract["source_input_count"], 4)
        self.assertEqual(contract["inference_input_count"], 44)

    def test_committed_inputs_ignore_dirty_tracked_untracked_and_identity_files(self):
        self.put("new.txt", b"committed\n")
        revision = self.candidate()
        before = identity.workflow.encoded(identity.generate(self.repo, revision))
        self.put("keep.txt", b"dirty tracked change\n")
        self.put("untracked.txt", b"not committed\n")
        self.put(identity.IDENTITY, b"invalid dirty metadata")
        status = self.git("status", "--porcelain")
        index = (self.repo / ".git/index").read_bytes()
        after = identity.workflow.encoded(identity.generate(self.repo, revision))
        self.assertEqual(before, after)
        self.assertEqual(status, self.git("status", "--porcelain"))
        self.assertEqual(index, (self.repo / ".git/index").read_bytes())
        self.assertEqual(self.git("rev-parse", "HEAD"), revision)

    def test_metadata_only_commit_changes_pins_but_not_product_patch_digest(self):
        self.put("new.txt", b"product\n")
        first = self.candidate()
        before = identity.generate(self.repo, first)
        self.put(".github/control.yml", b"new metadata\n")
        self.prior["acceptance"]["more_history"] = "retained"
        self.put(identity.IDENTITY, identity.workflow.encoded(self.prior))
        second = self.commit("metadata only")
        after = identity.generate(self.repo, second)
        self.assertEqual(before["patch_sha256"], after["patch_sha256"])
        self.assertNotEqual(before["changed_files_sha256"][".github/control.yml"],
                            after["changed_files_sha256"][".github/control.yml"])
        self.assertNotIn(identity.IDENTITY, after["changed_files_sha256"])

    def test_repeat_generation_is_byte_deterministic(self):
        self.put("keep.txt", b"changed product\n")
        revision = self.candidate()
        first = identity.generate(self.repo, revision)
        self.assertEqual(identity.workflow.encoded(first),
                         identity.workflow.encoded(identity.generate(self.repo, revision)))
        historical = subprocess.check_output(["git", "--no-optional-locks", "-C", str(self.repo),
            "diff", "--binary", "--full-index", "--no-renames", self.base, revision,
            "--", ".", ":(exclude).github"], env=self.env)
        self.assertEqual(first["patch_sha256"], self.sha(historical))

    def test_unpinned_deletion_is_reported(self):
        (self.repo / "drop.txt").unlink()
        result = identity.generate(self.repo, self.candidate())
        self.assertEqual(result["current_candidate_input_contract"]["changed_deleted_unpinned_paths"],
                         ["drop.txt"])

    def test_deleted_or_missing_prior_pin_refuses_without_output(self):
        for missing in ("keep.txt", "never-tracked.txt"):
            with self.subTest(missing=missing):
                prior = copy.deepcopy(self.prior)
                prior["changed_files_sha256"][missing] = "0" * 64
                if missing == "keep.txt":
                    (self.repo / missing).unlink()
                self.put(identity.IDENTITY, identity.workflow.encoded(prior))
                revision = self.commit("missing pin " + missing)
                output = self.root / (missing + ".json")
                with contextlib.redirect_stderr(io.StringIO()):
                    self.assertEqual(identity.main(["--repo", str(self.repo), "--revision", revision,
                                                    "--out", str(output)]), 2)
                self.assertFalse(output.exists())

    def test_changed_symlink_and_regular_to_gitlink_refused(self):
        (self.repo / "keep.txt").unlink()
        (self.repo / "keep.txt").symlink_to("drop.txt")
        revision = self.candidate()
        with self.assertRaises(identity.Refusal):
            identity.generate(self.repo, revision)
        self.git("reset", "--hard", self.base)  # Disposable fixture only.
        self.put(identity.IDENTITY, identity.workflow.encoded(self.prior))
        self.git("add", "--all")
        self.git("update-index", "--add", "--cacheinfo", "160000," + self.base + ",keep.txt")
        revision = self.commit("regular to gitlink", stage=False)
        with self.assertRaises(identity.Refusal):
            identity.generate(self.repo, revision)

    def test_gitlink_addition_and_revision_change_reported_separately(self):
        self.put(identity.IDENTITY, identity.workflow.encoded(self.prior))
        self.git("add", "--all")
        self.git("update-index", "--add", "--cacheinfo", "160000," + self.base + ",donor")
        first = self.commit("new gitlink", stage=False)
        before = identity.generate(self.repo, first)
        self.assertEqual(before["current_candidate_input_contract"]["changed_gitlinks"],
                         {"donor": {"before": None, "after": self.base}})
        self.git("update-index", "--cacheinfo", "160000," + first + ",donor")
        second = self.commit("updated gitlink", stage=False)
        after = identity.generate(self.repo, second)
        self.assertEqual(after["current_candidate_input_contract"]["changed_gitlinks"]["donor"]["after"], first)
        self.assertNotIn("donor", after["changed_files_sha256"])
        self.git("config", "diff.ignoreSubmodules", "all")
        self.git("config", "diff.submodule", "log")
        self.assertEqual(after["patch_sha256"], identity.generate(self.repo, second)["patch_sha256"])

    def test_nonancestor_and_changed_original_base_refused(self):
        foreign = self.git("commit-tree", self.git("rev-parse", "HEAD^{tree}"), "-m", "unrelated")
        self.prior["source_commit"] = foreign
        self.prior["original_native16_provenance"]["source_commit"] = foreign
        revision = self.candidate()
        with self.assertRaisesRegex(identity.Refusal, "ancestor"):
            identity.generate(self.repo, revision)
        self.prior["source_commit"] = self.base
        revision = self.candidate()
        with self.assertRaisesRegex(identity.Refusal, "minimum"):
            identity.generate(self.repo, revision)

    def test_diff_ignores_external_config_and_dirty_attributes(self):
        self.put("keep.txt", b"new product line\n")
        self.put("new.txt", b"new product\n")
        revision = self.candidate()
        before = identity.generate(self.repo, revision)["patch_sha256"]
        marker = self.root / "must-not-run"
        external = self.root / "external"
        external.write_text("#!/bin/sh\ntouch '" + str(marker) + "'\n")
        external.chmod(0o700)
        self.git("config", "diff.external", str(external))
        self.git("config", "diff.noprefix", "true")
        self.git("config", "diff.mnemonicPrefix", "true")
        self.git("config", "diff.algorithm", "histogram")
        self.git("config", "diff.context", "99")
        self.git("config", "color.ui", "always")
        self.git("config", "core.bigFileThreshold", "1")
        self.git("config", "diff.indentHeuristic", "false")
        self.git("config", "diff.suppressBlankEmpty", "true")
        order = self.root / "order"
        order.write_text("new.txt\nkeep.txt\n")
        self.git("config", "diff.orderFile", str(order))
        self.put(".gitattributes", b"*.txt -diff\n")
        self.assertEqual(before, identity.generate(self.repo, revision)["patch_sha256"])
        self.assertFalse(marker.exists())

    def test_custom_diff_driver_configuration_is_refused(self):
        self.put(".gitattributes", b"*.txt diff=custom\n")
        revision = self.candidate()
        self.git("config", "diff.custom.binary", "true")
        with self.assertRaisesRegex(identity.Refusal, "Custom diff driver"):
            identity.generate(self.repo, revision)

    def test_ambient_repository_redirect_and_diff_environment_ignored(self):
        revision = self.candidate()
        before = identity.workflow.encoded(identity.generate(self.repo, revision))
        with mock.patch.dict(os.environ, {"GIT_DIR": str(self.empty), "GIT_WORK_TREE": str(self.empty),
                                         "GIT_INDEX_FILE": str(self.empty / "missing-index"),
                                         "GIT_COMMON_DIR": str(self.empty),
                                         "GIT_DIFF_OPTS": "--unified=999"}):
            self.assertEqual(before, identity.workflow.encoded(identity.generate(self.repo, revision)))

    def test_local_attribute_override_refused(self):
        revision = self.candidate()
        path = self.repo / ".git/info/attributes"
        path.parent.mkdir(exist_ok=True)
        path.write_text("*.txt -diff\n")
        with self.assertRaisesRegex(identity.Refusal, "attributes"):
            identity.generate(self.repo, revision)

    def test_fresh_private_output_only_and_existing_output_preserved(self):
        revision = self.candidate()
        value = identity.generate(self.repo, revision)
        output = self.root / "new.json"
        identity.write_new(self.repo, output, value)
        before = output.read_bytes()
        self.assertEqual(stat.S_IMODE(output.stat().st_mode), 0o600)
        with self.assertRaises(identity.Refusal):
            identity.write_new(self.repo, output, value)
        self.assertEqual(before, output.read_bytes())
        for path in (self.repo / "new.json", self.repo / ".git/new.json"):
            with self.subTest(path=path), self.assertRaises(identity.Refusal):
                identity.write_new(self.repo, path, value)
            self.assertFalse(path.exists())
        with self.assertRaises(identity.Refusal):
            identity.write_new(self.repo, Path("/" + str(self.repo / "double-slash.json")), value)

    def test_nonstandard_identity_json_constant_refused(self):
        self.put(identity.IDENTITY, identity.workflow.encoded(self.prior).replace(
            b'"prepared_at":"historical timestamp retained"', b'"prepared_at":NaN'))
        revision = self.commit("invalid metadata")
        with self.assertRaisesRegex(identity.Refusal, "JSON constant"):
            identity.generate(self.repo, revision)

    def test_symlink_output_and_parent_refused_without_following(self):
        revision = self.candidate()
        value = identity.generate(self.repo, revision)
        link = self.root / "output-link"
        link.symlink_to(self.root / "missing")
        with self.assertRaises(identity.Refusal):
            identity.write_new(self.repo, link, value)
        parent = self.root / "parent-link"
        parent.symlink_to(self.empty, target_is_directory=True)
        with self.assertRaises(OSError):
            identity.write_new(self.repo, parent / "new.json", value)
        self.assertFalse((self.empty / "new.json").exists())

    def test_cli_requires_explicit_out_and_success_does_not_change_git(self):
        revision = self.candidate()
        before = (self.git("status", "--porcelain"), self.git("show-ref"))
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            identity.main(["--repo", str(self.repo), "--revision", revision])
        output = self.root / "cli.json"
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(identity.main(["--repo", str(self.repo), "--revision", revision,
                                            "--out", str(output)]), 0)
        self.assertEqual(before, (self.git("status", "--porcelain"), self.git("show-ref")))
        self.assertEqual(json.loads(output.read_text())["acceptance"]["current_combined_candidate"], identity.NOT_RUN)


if __name__ == "__main__":
    unittest.main()
