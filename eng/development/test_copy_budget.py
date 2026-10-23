import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import stat
import tempfile
import types
import unittest
from unittest import mock

import copy_budget as budget


class CopyBudgetTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="9to1-copy-budget-test-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.origin = self.root / "pinned.bin"
        self.origin.write_bytes(b"the retained complete asset")
        self.output = self.root / "output.bin"

    def identity(self, path):
        info = path.lstat()
        return dict(path=str(path), bytes=info.st_size,
                    allocated_bytes=info.st_blocks * 512,
                    mode=stat.S_IMODE(info.st_mode), device=info.st_dev,
                    inode=info.st_ino, nlink=info.st_nlink,
                    mtime_ns=info.st_mtime_ns, ctime_ns=info.st_ctime_ns,
                    sha256=hashlib.sha256(path.read_bytes()).hexdigest())

    def row(self, **extra):
        origin = self.identity(self.origin)
        candidate = {key: origin[key] for key in ("bytes", "sha256", "mode")}
        candidate["path"] = str(self.output)
        return dict(candidate=candidate, package_origin=origin, **extra)

    def manifest(self, rows, **extra):
        path = self.root / "manifest.json"
        path.write_text(json.dumps(dict(schema="9to1.copy-budget/v1", rows=rows, **extra)))
        return path, hashlib.sha256(path.read_bytes()).hexdigest()

    def assess(self, rows, free, **extra):
        path, digest = self.manifest(rows, **extra)
        # Only the capacity observation is controlled. Inputs, hashes, identity,
        # absent/stale outputs, symlinks and hardlinks are real temporary files.
        values = types.SimpleNamespace(f_frsize=4096, f_bsize=4096, f_bavail=free // 4096)
        with mock.patch.object(budget.os, "statvfs", return_value=values):
            return budget.assess(path, digest)

    def test_missing_copy_denied_before_reserve_is_spent_without_execution(self):
        before = self.identity(self.origin)
        with mock.patch.object(os, "system", side_effect=AssertionError("must not execute")):
            result = self.assess([self.row()], budget.RESERVE_BYTES)
        self.assertEqual("KNOWN_COPY_BUDGET_DENIED", result["status"])
        self.assertEqual(4096, result["known_copy_allocation_bytes"])
        self.assertEqual(budget.RESERVE_BYTES + 4096, result["filesystems"][0]["required_free_bytes"])
        self.assertFalse(result["sdk_launch_admitted"])
        self.assertFalse(self.output.exists())
        self.assertEqual(before, self.identity(self.origin))

    def test_retained_identical_complete_copy_reuses_bytes_but_never_admits_sdk(self):
        self.output.write_bytes(self.origin.read_bytes())
        result = self.assess([self.row()], budget.RESERVE_BYTES)
        self.assertTrue(result["known_copy_budget_fits"])
        self.assertEqual(0, result["known_copy_allocation_bytes"])
        self.assertEqual(1, result["counts"]["identical"])
        self.assertFalse(result["sdk_launch_admitted"])

    def test_partial_and_equal_length_corrupt_outputs_both_require_complete_copy(self):
        for content in (b"the retained", b"x" * self.origin.stat().st_size):
            with self.subTest(content=content):
                self.output.write_bytes(content)
                result = self.assess([self.row()], budget.RESERVE_BYTES)
                self.assertEqual(1, result["counts"]["stale"])
                self.assertEqual(4096, result["known_copy_allocation_bytes"])
                self.assertFalse(result["known_copy_budget_fits"])
                self.assertEqual(content, self.output.read_bytes())

    def test_allocation_rounds_up_and_distinct_destinations_each_count(self):
        self.origin.write_bytes(b"x" * 4097)
        first = self.row()
        second = self.row()
        second["candidate"]["path"] = str(self.root / "other.bin")
        result = self.assess([first, second], budget.RESERVE_BYTES + 16384)
        self.assertEqual(16384, result["known_copy_allocation_bytes"])
        self.assertTrue(result["known_copy_budget_fits"])
        self.assertEqual(2, result["counts"]["missing"])

    def test_invalid_origin_hash_refuses_without_changing_any_file(self):
        row = self.row()
        row["package_origin"]["sha256"] = row["candidate"]["sha256"] = "0" * 64
        with self.assertRaisesRegex(budget.Refusal, "SHA256 mismatch"):
            self.assess([row], budget.RESERVE_BYTES + 4096)
        self.assertFalse(self.output.exists())
        self.assertEqual(b"the retained complete asset", self.origin.read_bytes())

    def test_stale_origin_identity_refused_even_when_bytes_still_equal(self):
        row = self.row()
        row["package_origin"]["inode"] += 1
        with self.assertRaisesRegex(budget.Refusal, "Stale pinned origin"):
            self.assess([row], budget.RESERVE_BYTES + 4096)

    def test_manifest_hash_mismatch_refused_before_origins_are_opened(self):
        path, _ = self.manifest([self.row()])
        with self.assertRaisesRegex(budget.Refusal, "SHA256 mismatch"):
            budget.assess(path, "0" * 64)

    def test_existing_real_hardlink_needs_no_copy(self):
        os.link(self.origin, self.output)
        result = self.assess([self.row(hardlinks_if_possible=True)], budget.RESERVE_BYTES)
        self.assertTrue(result["known_copy_budget_fits"])
        self.assertEqual(1, result["counts"]["existing_hardlink"])
        self.assertEqual(0, result["known_copy_allocation_bytes"])

    def test_same_device_potential_hardlink_does_not_credit_unproven_fallback_savings(self):
        result = self.assess([self.row(hardlinks_if_possible=True)], budget.RESERVE_BYTES)
        group = result["filesystems"][0]
        self.assertEqual(4096, group["possible_hardlink_savings_bytes_not_credited"])
        self.assertEqual(4096, group["known_copy_allocation_bytes"])
        self.assertFalse(result["known_copy_budget_fits"])
        self.assertFalse(self.output.exists())

    def test_symlink_origin_output_parent_and_manifest_refused(self):
        actual = self.root / "real.bin"
        actual.write_bytes(self.origin.read_bytes())
        for location in ("origin", "output", "parent", "manifest"):
            with self.subTest(location=location):
                row = self.row()
                link = self.root / ("link-" + location)
                if location == "origin":
                    link.symlink_to(self.origin)
                    row["package_origin"]["path"] = str(link)
                elif location == "output":
                    link.symlink_to(actual)
                    row["candidate"]["path"] = str(link)
                elif location == "parent":
                    link.symlink_to(self.root, target_is_directory=True)
                    row["candidate"]["path"] = str(link / "missing.bin")
                else:
                    path, digest = self.manifest([row])
                    link.symlink_to(path)
                    with self.assertRaisesRegex(budget.Refusal, "Symlink"):
                        budget.assess(link, digest)
                    continue
                with self.assertRaisesRegex(budget.Refusal, "Symlink"):
                    self.assess([row], budget.RESERVE_BYTES + 4096)

    def test_same_path_duplicate_path_and_output_as_another_input_refused(self):
        row = self.row()
        row["candidate"]["path"] = str(self.origin)
        with self.assertRaisesRegex(budget.Refusal, "Same/duplicate"):
            self.assess([row], budget.RESERVE_BYTES + 4096)
        row = self.row()
        with self.assertRaisesRegex(budget.Refusal, "Same/duplicate"):
            self.assess([row, row], budget.RESERVE_BYTES + 8192)
        self.output.write_bytes(self.origin.read_bytes())
        first = self.row()
        second = dict(candidate=dict(first["candidate"], path=str(self.root / "third.bin")),
                      package_origin=self.identity(self.output))
        with self.assertRaisesRegex(budget.Refusal, "also a pinned input"):
            self.assess([first, second], budget.RESERVE_BYTES + 8192)

    def test_negative_boolean_sizes_and_unknown_custom_copy_refused(self):
        for field, value in (("bytes", -1), ("bytes", True), ("allocated_bytes", -1)):
            with self.subTest(field=field, value=value):
                row = self.row()
                row["candidate"][field] = value
                with self.assertRaises(budget.Refusal):
                    self.assess([row], budget.RESERVE_BYTES + 4096)
        with self.assertRaisesRegex(budget.Refusal, "Unknown/custom"):
            self.assess([self.row(copy_kind="custom")], budget.RESERVE_BYTES + 4096)
        with self.assertRaisesRegex(budget.Refusal, "Unknown/custom"):
            self.assess([self.row()], budget.RESERVE_BYTES + 4096, copy_target="opaque")

    def test_origin_changed_during_assessment_invalidates_observation(self):
        row = self.row()
        original = budget._read_verified
        changed = False

        def change_after_read(path, *args, **kwargs):
            nonlocal changed
            result = original(path, *args, **kwargs)
            if path == self.origin and not changed:
                self.origin.write_bytes(b"changed after hash")
                changed = True
            return result

        with mock.patch.object(budget, "_read_verified", side_effect=change_after_read):
            with self.assertRaisesRegex(budget.Refusal, "changed during assessment"):
                self.assess([row], budget.RESERVE_BYTES + 4096)

    def test_cli_refusal_emits_no_launch_admission_and_exit_two(self):
        path, digest = self.manifest([self.row(copy_kind="unknown")])
        stdout = io.StringIO()
        with contextlib.redirect_stdout(stdout):
            code = budget.main([str(path), "--sha256", digest])
        result = json.loads(stdout.getvalue())
        self.assertEqual(2, code)
        self.assertEqual("COPY_BUDGET_INPUT_REFUSED", result["status"])
        self.assertFalse(result["sdk_launch_admitted"])

    def test_root_relative_and_traversing_paths_and_directory_inputs_refused(self):
        for path in ("/", "relative.bin", str(self.root / ".." / "output.bin"), str(self.root)):
            with self.subTest(path=path):
                row = self.row()
                row["candidate"]["path"] = path
                with self.assertRaises(budget.Refusal):
                    self.assess([row], budget.RESERVE_BYTES + 4096)

    def test_duplicate_json_keys_and_unknown_schema_refused(self):
        path = self.root / "manifest.json"
        for content in ('{"schema":"a","schema":"b"}', '{"schema":[],"rows":[]}'):
            with self.subTest(content=content):
                path.write_text(content)
                with self.assertRaises(budget.Refusal):
                    budget.assess(path, hashlib.sha256(path.read_bytes()).hexdigest())

    def test_output_created_after_absence_observation_invalidates_result(self):
        row = self.row()
        original = budget._inspect
        changed = False

        def create_after_absence(path, missing_leaf=False, directory=False):
            nonlocal changed
            result = original(path, missing_leaf, directory)
            if path == self.output and result is None and not changed:
                self.output.write_bytes(self.origin.read_bytes())
                changed = True
            return result

        with mock.patch.object(budget, "_inspect", side_effect=create_after_absence):
            with self.assertRaisesRegex(budget.Refusal, "changed during assessment"):
                self.assess([row], budget.RESERVE_BYTES + 4096)


if __name__ == "__main__":
    unittest.main()
