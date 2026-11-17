"""Disposable file-system acceptance for the ordinary browser publisher's map custody."""

from pathlib import Path
import shutil
import stat
import tempfile
import unittest

from generated_map_custody import TRACKED_MAPS, restore_generated_maps, snapshot_generated_maps


class TrackedMapCustodyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="9to1-map-custody-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "repo"
        self.root.mkdir()
        for index, relative in enumerate(TRACKED_MAPS):
            path = self.root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(f"original-{index}".encode())
            path.chmod(0o644)

    def test_restores_original_tracked_bytes_without_tampering_with_published_maps(self):
        originals = snapshot_generated_maps(self.root)
        destination = Path(self.temp.name) / "published"
        destination.mkdir()
        built = {}
        for index, relative in enumerate(TRACKED_MAPS):
            path = self.root / relative
            generated = f"generated-{index}".encode()
            path.write_bytes(generated)
            # A genuine publish copies assets to another, sealed directory.
            output = destination / path.name
            shutil.copyfile(path, output)
            built[path.name] = generated
        restore_generated_maps(self.root, originals)
        for relative, (contents, mode) in originals.items():
            path = self.root / relative
            self.assertEqual(path.read_bytes(), contents)
            self.assertEqual(stat.S_IMODE(path.stat().st_mode), mode)
            self.assertEqual((destination / path.name).read_bytes(), built[path.name])
        restore_generated_maps(self.root, originals)  # idempotent cleanup

    def test_unrelated_source_change_is_not_hidden_by_custody_recovery(self):
        originals = snapshot_generated_maps(self.root)
        unrelated = self.root / "apps/Web/keep.cs"
        unrelated.parent.mkdir(parents=True)
        unrelated.write_text("original")
        for relative in TRACKED_MAPS:
            (self.root / relative).write_bytes(b"regenerated")
        unrelated.write_text("changed")
        restore_generated_maps(self.root, originals)
        self.assertEqual(unrelated.read_text(), "changed")
        for relative, (contents, _) in originals.items():
            self.assertEqual((self.root / relative).read_bytes(), contents)

    def test_rejects_missing_and_symlink_targets_without_partial_recovery(self):
        for replacement in ("missing", "symlink"):
            with self.subTest(replacement=replacement):
                for index, relative in enumerate(TRACKED_MAPS):
                    (self.root / relative).write_bytes(f"original-{index}".encode())
                originals = snapshot_generated_maps(self.root)
                first = self.root / TRACKED_MAPS[0]
                first.write_bytes(b"regenerated-first")
                last = self.root / TRACKED_MAPS[-1]
                last.unlink()
                if replacement == "symlink":
                    sentinel = Path(self.temp.name) / "sentinel"
                    sentinel.write_bytes(b"external-target")
                    last.symlink_to(sentinel)
                with self.assertRaises((RuntimeError, FileNotFoundError)):
                    restore_generated_maps(self.root, originals)
                self.assertEqual(first.read_bytes(), b"regenerated-first", "preflight must refuse partial restore")
                if replacement == "symlink":
                    self.assertEqual(sentinel.read_bytes(), b"external-target", "never follow source-map symlinks")
                    last.unlink()
                last.write_bytes(originals[TRACKED_MAPS[-1]][0])

    def test_rejects_incomplete_or_forged_snapshots(self):
        original = snapshot_generated_maps(self.root)
        for invalid in ({}, {TRACKED_MAPS[0]: original[TRACKED_MAPS[0]]}, {**original, "other": (b"a", 0o644)}):
            with self.assertRaisesRegex(RuntimeError, "precisely"):
                restore_generated_maps(self.root, invalid)
        bad = {**original, TRACKED_MAPS[0]: ("not bytes", 0o644)}
        with self.assertRaisesRegex(RuntimeError, "Invalid"):
            restore_generated_maps(self.root, bad)

    def test_snapshot_rejects_symlink_before_reading_external_contents(self):
        last = self.root / TRACKED_MAPS[-1]
        last.unlink()
        outside = Path(self.temp.name) / "external"
        outside.write_bytes(b"must-not-read-as-source")
        last.symlink_to(outside)
        with self.assertRaisesRegex(RuntimeError, "not a regular file"):
            snapshot_generated_maps(self.root)
        self.assertEqual(outside.read_bytes(), b"must-not-read-as-source")

    def test_mode_changed_by_build_is_restored(self):
        original = snapshot_generated_maps(self.root)
        path = self.root / TRACKED_MAPS[0]
        path.chmod(0o600)
        restore_generated_maps(self.root, original)
        self.assertEqual(stat.S_IMODE(path.stat().st_mode), 0o644)


if __name__ == "__main__":
    unittest.main()
