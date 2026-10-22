import importlib.util
import json
import subprocess
import sys
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("package_removal", Path(__file__).with_name("package-removal-check.py"))
removal = importlib.util.module_from_spec(spec)
spec.loader.exec_module(removal)


class PersistentDataRemovalContractTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.data = self.root / "9to1-home-ready-fresh/Home/state.json"
        self.data.parent.mkdir(parents=True)
        self.data.write_bytes(b'{"testData":"preserved"}')
        (self.root / "native-startup-receipt.json").write_text(json.dumps({
            "events": [{"kind": "ISOLATED_XDG_CONTEXT", "profile": str(self.data.parent.parent)}]}))

    def test_unchanged_profile_data_matches(self):
        snapshot = removal.profile_snapshot(self.root, self.root)
        self.assertEqual(len(snapshot), 1)
        removal.unchanged(snapshot)

    def test_deleted_or_changed_profile_data_is_detected(self):
        snapshot = removal.profile_snapshot(self.root, self.root)
        self.data.write_bytes(b"changed")
        with self.assertRaises(ValueError):
            removal.unchanged(snapshot)
        self.data.unlink()
        with self.assertRaises(ValueError):
            removal.unchanged(snapshot)

    def test_symlink_profile_and_missing_data_are_never_proof_of_preservation(self):
        self.data.unlink()
        with self.assertRaises(ValueError):
            removal.profile_snapshot(self.root, self.root)
        self.data.symlink_to(self.root / "elsewhere")
        (self.root / "elsewhere").write_bytes(b"data")
        with self.assertRaises(ValueError):
            removal.profile_snapshot(self.root, self.root)

    def test_profile_reference_outside_runner_context_is_refused(self):
        (self.root / "native-startup-receipt.json").write_text(json.dumps({
            "events": [{"kind": "ISOLATED_XDG_CONTEXT", "profile": "/outside/owner-data"}]}))
        with self.assertRaises(ValueError):
            removal.profile_snapshot(self.root, self.root)

    def test_wal_and_other_persisted_records_cannot_silently_disappear(self):
        wal = self.data.parent.parent / "haven.db-wal"
        wal.write_bytes(b"most recent persisted writes")
        snapshot = removal.profile_snapshot(self.root, self.root)
        self.assertEqual(len(snapshot), 2)
        wal.unlink()
        with self.assertRaises(ValueError):
            removal.unchanged(snapshot)

    def test_actual_child_timeout_preserves_stdout_and_stderr(self):
        code = "import sys,time;sys.stdout.buffer.write(b'started stdout\\n');sys.stdout.flush();sys.stderr.buffer.write(b'started stderr\\n');sys.stderr.flush();time.sleep(10)"
        with self.assertRaises(subprocess.TimeoutExpired):
            removal.run_removal(self.root, [sys.executable, "-c", code], timeout=.3)
        self.assertEqual((self.root / "package-removal.stdout.log").read_bytes(), b"started stdout\n")
        self.assertEqual((self.root / "package-removal.stderr.log").read_bytes(), b"started stderr\n")


if __name__ == "__main__":
    unittest.main()
