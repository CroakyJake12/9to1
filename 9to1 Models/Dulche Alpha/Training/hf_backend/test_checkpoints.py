from dataclasses import asdict, replace
import json
import os
from pathlib import Path
import unittest
from unittest.mock import patch
from uuid import uuid4

from hf_backend.cpu_guard import install

CUDA_REFUSAL = install()

import torch

from custody import ArtifactStore, CustodyError
from custody.artifacts import create_directory
from custody.snapshot import load_snapshot, publish_snapshot
from hf_backend.checkpoints import VerifiedCheckpointPublisher


_PURPOSE = "software_test_only"
_BYTES = 100_000


class CheckpointPublisherTests(unittest.TestCase):
    def setUp(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(CUDA_REFUSAL.dulche_guard_calls, 0)
        root = Path(os.environ["DULCHE_HF_TEST_ARTIFACT_ROOT"]) / f"checkpoint-publisher-{uuid4()}"
        create_directory(root, exclusive=True)
        self.store = ArtifactStore(root / "store")
        run = self.store.create_run("Verified HF checkpoint publisher fixture", {"purpose": _PURPOSE})
        self.attempt = self.store.begin_attempt(run, {"purpose": _PURPOSE})
        self.publisher = VerifiedCheckpointPublisher(self.store, self.attempt, purpose=_PURPOSE, max_bytes=_BYTES)

    def tearDown(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(CUDA_REFUSAL.dulche_guard_calls, 0)

    @staticmethod
    def sample(failed=False):
        snapshot = {
            "schema": 1, "kind": "hf-local-session", "purpose": _PURPOSE, "failed": failed,
            "updates": 0 if failed else 1, "dataset_manifest_sha256": "a" * 64,
            "model": {"weight": torch.tensor([4.0, float("nan") if failed else 9.0], device="cpu")},
            "optimizer": {"state": {0: {"exp_avg": torch.tensor([0.125], device="cpu")}}},
        }
        record = {
            "schema": 1, "purpose": _PURPOSE, "dataset_manifest_sha256": "a" * 64,
            "dataset_file_sha256": "b" * 64,
        }
        if failed:
            record.update(status="failed", attempted_update=1, completed_updates=0, phase="optimizer_update")
        else:
            record.update(status="completed", update=1)
        return snapshot, record

    def test_seals_success_and_failed_checkpoints_with_exact_linked_evidence(self):
        references = set()
        for failed in (False, True):
            snapshot, record = self.sample(failed)
            receipt = self.publisher.publish(snapshot, record)
            self.assertEqual(receipt.checkpoint.attempt, self.attempt)
            self.assertEqual(receipt.evidence.attempt, self.attempt)
            self.assertNotEqual(receipt.checkpoint, receipt.evidence)
            saved = load_snapshot(self.store, receipt.checkpoint, max_bytes=_BYTES)
            torch.testing.assert_close(saved["model"]["weight"], snapshot["model"]["weight"],
                                       equal_nan=True, rtol=0, atol=0)
            torch.testing.assert_close(saved["optimizer"]["state"][0]["exp_avg"],
                                       snapshot["optimizer"]["state"][0]["exp_avg"], rtol=0, atol=0)
            self.assertIs(saved["failed"], failed)

            path = self.store.verify(receipt.evidence)
            evidence = json.loads(path.read_bytes())
            manifest = json.loads((path.parent / "manifest.json").read_bytes())
            self.assertEqual(evidence, {
                "schema": 1, "purpose": _PURPOSE, "status": record["status"],
                "checkpoint": asdict(receipt.checkpoint), "record": record,
            })
            self.assertEqual(manifest["inputs"], [asdict(receipt.checkpoint)])
            self.assertEqual(manifest["kind"], "evidence")
            references.update((receipt.checkpoint, receipt.evidence))
        self.assertEqual(len(references), 4)
        self.assertEqual(set(self.store.verify_attempt(self.attempt).artifacts), references)

    def test_refuses_wrong_purpose_schema_and_update_counts_before_writing(self):
        snapshot, record = self.sample()
        cases = (
            ({**snapshot, "purpose": "real_training"}, record),
            (snapshot, {**record, "purpose": "real_training"}),
            ({**snapshot, "schema": True}, record),
            (snapshot, {**record, "update": 2}),
            (snapshot, {**record, "dataset_manifest_sha256": "c" * 64}),
        )
        for candidate, evidence in cases:
            with self.subTest(purpose=candidate["purpose"], update=evidence["update"]):
                with self.assertRaises(CustodyError):
                    self.publisher.publish(candidate, evidence)
        with self.assertRaises(CustodyError):
            replace(self.publisher, purpose="real_training")
        self.assertEqual(self.store.verify_attempt(self.attempt).artifacts, ())

    def test_preserves_verified_checkpoint_when_evidence_publication_fails(self):
        snapshot, record = self.sample(failed=True)
        with patch.object(self.store, "publish_bytes", side_effect=OSError("software metadata-write fault")):
            with self.assertRaisesRegex(OSError, "metadata-write fault"):
                self.publisher.publish(snapshot, record)
        checkpoint, = self.store.verify_attempt(self.attempt).artifacts
        saved = load_snapshot(self.store, checkpoint, max_bytes=_BYTES)
        self.assertTrue(saved["failed"])
        torch.testing.assert_close(saved["model"]["weight"], snapshot["model"]["weight"], equal_nan=True, rtol=0, atol=0)

    def test_refuses_a_bad_checkpoint_reference_and_retains_the_actual_snapshot(self):
        snapshot, record = self.sample()

        def bad_receipt(*args, **kwargs):
            return replace(publish_snapshot(*args, **kwargs), sha256="0" * 64)

        with patch("hf_backend.checkpoints.publish_snapshot", side_effect=bad_receipt):
            with self.assertRaisesRegex(CustodyError, "artifact reference"):
                self.publisher.publish(snapshot, record)
        checkpoint, = self.store.verify_attempt(self.attempt).artifacts
        self.assertEqual(load_snapshot(self.store, checkpoint, max_bytes=_BYTES)["updates"], 1)

    def test_refuses_a_bad_evidence_reference_and_retains_both_actual_artifacts(self):
        snapshot, record = self.sample()
        publish_bytes = self.store.publish_bytes

        def bad_receipt(*args, **kwargs):
            return replace(publish_bytes(*args, **kwargs), sha256="0" * 64)

        with patch.object(self.store, "publish_bytes", side_effect=bad_receipt):
            with self.assertRaisesRegex(CustodyError, "artifact reference"):
                self.publisher.publish(snapshot, record)
        retained = self.store.verify_attempt(self.attempt).artifacts
        self.assertEqual(len(retained), 2)
        records = [json.loads((self.store.verify(ref).parent / "manifest.json").read_bytes()) for ref in retained]
        self.assertEqual({document["kind"] for document in records}, {"segment_checkpoint", "evidence"})
