from dataclasses import replace
import io
import os
from pathlib import Path
import tempfile
import unittest

from custody.artifacts import ArtifactStore, CustodyError


class ArtifactStoreTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="dulche-custody-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve() / "store"
        self.store = ArtifactStore(self.root)
        self.run = self.store.create_run("CPU software custody test", {"purpose": "software_test_only"})
        self.attempt = self.store.begin_attempt(self.run, {"seed": 1})

    def test_preserves_versions_and_recovers_a_checkpoint(self):
        first = self.store.publish_bytes(self.attempt, "checkpoints/segment-1", "segment_checkpoint", b"original")
        second = self.store.publish_bytes(self.attempt, "checkpoints/segment-1", "segment_checkpoint", b"candidate")
        self.assertNotEqual(first.artifact_id, second.artifact_id)
        self.store.record_outcome(self.attempt, "paused", {"reason": "storage"})

        reopened = ArtifactStore(self.root)
        resumed = reopened.begin_attempt(self.run, {"seed": 1}, resume_from=(first,))
        self.assertNotEqual(resumed.attempt_id, self.attempt.attempt_id)
        restored = reopened.restore(first, resumed, "checkpoints/recovered")
        self.assertEqual(restored.read_bytes(), b"original")
        with self.assertRaises(FileExistsError):
            reopened.restore(second, resumed, "checkpoints/recovered")
        with self.assertRaises(FileExistsError):
            reopened.record_outcome(self.attempt, "passed", {})
        self.assertEqual(restored.read_bytes(), b"original")
        self.assertEqual({first, second}, set(reopened.verify_attempt(self.attempt).artifacts))

    def test_detects_corrupted_payload_and_invalid_manifest_reference(self):
        ref = self.store.publish_bytes(self.attempt, "checkpoint", "checkpoint", b"expected")
        self.store.verify(ref).write_bytes(b"tampered")
        with self.assertRaises(CustodyError):
            self.store.verify(ref)
        with self.assertRaises(CustodyError):
            self.store.restore(ref, self.attempt, "still-not-created")
        self.assertFalse((self.store.attempt_path(self.attempt) / "restored" / "still-not-created").exists())
        with self.assertRaises(CustodyError):
            self.store.verify(replace(ref, sha256="0" * 64))

    def test_detects_manually_altered_manifest_and_retains_unsealed_content(self):
        source = self.store.publish_bytes(self.attempt, "job.cfg", "config", b"first version")
        directory = self.store.attempt_path(self.attempt) / "artifacts" / source.artifact_id
        manifest = directory / "manifest.json"
        manifest.write_bytes(manifest.read_bytes() + b"\n")
        with self.assertRaisesRegex(CustodyError, "manifest failed"):
            self.store.verify(source)
        other = self.store.begin_attempt(self.run, {})
        checkpoint = self.store.publish_bytes(other, "weights", "checkpoint", b"kept")
        partial = self.store.attempt_path(other) / "artifacts" / checkpoint.artifact_id
        (partial / "seal.json").rename(partial / "undelivered-seal.fixture")
        self.assertEqual(self.store.verify_attempt(other).incomplete_artifact_ids, (checkpoint.artifact_id,))
        self.assertEqual((partial / "payload").read_bytes(), b"kept")

    def test_failed_write_preserves_partial_evidence_without_claiming_an_artifact(self):
        with self.assertRaises(CustodyError):
            self.store.publish(self.attempt, "large", "log", io.BytesIO(b"x" * 1500000), max_bytes=1200000)
        verification = self.store.verify_attempt(self.attempt)
        self.assertEqual(verification.artifacts, ())
        orphan, = verification.incomplete_artifact_ids
        directory = self.store.attempt_path(self.attempt) / "artifacts" / orphan
        self.assertTrue((directory / "intent.json").exists())
        self.assertFalse((directory / "manifest.json").exists())
        partial, = directory.glob(".payload.*.partial")
        self.assertEqual(partial.stat().st_size, 1048576)

    def test_refuses_traversal_and_actual_hardlink_aliases(self):
        run_directory = self.store.attempt_path(self.attempt)
        with self.assertRaises(CustodyError):
            self.store.publish_bytes(self.attempt, "../../elsewhere", "checkpoint", b"x")
        with self.assertRaises(CustodyError):
            self.store.publish_bytes(self.attempt, "bad/CON.bin", "checkpoint", b"x")
        original = Path(self.temporary.name).resolve() / "external"
        original.write_bytes(b"keep")
        os.link(original, run_directory / "work" / "aliased")
        with self.assertRaises(CustodyError):
            self.store.publish_file(self.attempt, "checkpoint", "checkpoint", "aliased")
        ref = self.store.publish_bytes(self.attempt, "checkpoint", "checkpoint", b"safe")
        with self.assertRaises(CustodyError):
            self.store.restore(ref, self.attempt, "C:\\external")
        self.assertEqual(original.read_bytes(), b"keep")


if __name__ == "__main__":
    unittest.main()
