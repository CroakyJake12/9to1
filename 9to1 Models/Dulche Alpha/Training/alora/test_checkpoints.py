from copy import deepcopy
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

from alora.checkpoints import CheckpointPublication, CheckpointPublisher
from custody import ArtifactStore, CustodyError
from custody.artifacts import create_directory
from custody.snapshot import load_snapshot

_PURPOSE = "software_test_only"
_BYTES = 100_000


class CheckpointPublisherTests(unittest.TestCase):
    def setUp(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(CUDA_REFUSAL.dulche_guard_calls, 0)
        artifact_root = os.environ.get("DULCHE_ALORA_TEST_ARTIFACT_ROOT")
        if artifact_root is None:
            artifact_root = os.environ["DULCHE_HF_TEST_ARTIFACT_ROOT"]
        root = Path(artifact_root) / f"alora-checkpoints-{uuid4()}"
        create_directory(root, exclusive=True)
        self.store = ArtifactStore(root / "store")
        run = self.store.create_run("ALORA checkpoint software fixture", {"purpose": _PURPOSE})
        self.attempt = self.store.begin_attempt(run, {"purpose": _PURPOSE})
        self.publisher = CheckpointPublisher(
            self.store, self.attempt, purpose=_PURPOSE, max_bytes=_BYTES,
        )

    def tearDown(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(CUDA_REFUSAL.dulche_guard_calls, 0)

    @staticmethod
    def sample(status="completed"):
        updates = 1 if status == "completed" else 0
        failed = status == "failed"
        identity = {
            "base_model_sha256": "a" * 64,
            "dataset_manifest_sha256": "b" * 64,
            "profile_sha256": "c" * 64,
            "plan": {"experts": {"layer.0": [1, 3]}, "rank": 2},
            "optimizer": {"name": "AdamW", "learning_rate": 0.001},
        }
        snapshot = {
            "schema": 1, "kind": "alora-training", "purpose": _PURPOSE,
            "updates": updates, "failed": failed, "identity": identity, "cursor": updates,
            "adapters": {"layer.0.1.A": torch.tensor([[0.2, 0.3]], device="cpu"),
                         "layer.0.1.B": torch.tensor([[float("nan") if failed else 0.4]], device="cpu")},
            "optimizer": {"state": {0: {"step": torch.tensor(float(updates), device="cpu"),
                                       "exp_avg": torch.tensor([0.1], device="cpu")}},
                          "param_groups": [{"params": [0], "lr": 0.001}]},
            "torch_cpu_rng": torch.get_rng_state().clone(),
        }
        record = {
            "schema": 1, "purpose": _PURPOSE, "status": status,
            "updates": updates, "identity": deepcopy(identity),
        }
        return snapshot, record

    def test_initial_completed_and_failed_snapshots_round_trip_with_bound_evidence(self):
        references = set()
        for status in ("initial", "completed", "failed"):
            with self.subTest(status=status):
                snapshot, record = self.sample(status)
                publication = self.publisher.publish(snapshot, record)
                restored = self.publisher.load(publication)
                self.assertEqual(restored["identity"], snapshot["identity"])
                self.assertEqual(restored["updates"], snapshot["updates"])
                self.assertEqual(restored["cursor"], snapshot["cursor"])
                self.assertIs(restored["failed"], snapshot["failed"])
                for name, tensor in snapshot["adapters"].items():
                    torch.testing.assert_close(
                        restored["adapters"][name], tensor, rtol=0, atol=0, equal_nan=True,
                    )
                torch.testing.assert_close(
                    restored["optimizer"]["state"][0]["exp_avg"],
                    snapshot["optimizer"]["state"][0]["exp_avg"], rtol=0, atol=0,
                )
                self.assertTrue(torch.equal(restored["torch_cpu_rng"], snapshot["torch_cpu_rng"]))
                checkpoint_path = self.publisher.checkpoint_path(publication)
                self.assertEqual(checkpoint_path, self.store.verify(publication.checkpoint))
                evidence_path = self.store.verify(publication.evidence)
                evidence = json.loads(evidence_path.read_bytes())
                manifest = json.loads((evidence_path.parent / "manifest.json").read_bytes())
                self.assertEqual(evidence["checkpoint"], asdict(publication.checkpoint))
                self.assertEqual(evidence["attempt"], asdict(self.attempt))
                self.assertEqual(evidence["record"], record)
                self.assertEqual(evidence["status"], status)
                self.assertEqual(manifest["inputs"], [asdict(publication.checkpoint)])
                references.update((publication.checkpoint, publication.evidence))
        self.assertEqual(len(references), 6)
        self.assertEqual(set(self.store.verify_attempt(self.attempt).artifacts), references)

    def test_invalid_schema_purpose_identity_counts_and_outcomes_write_nothing(self):
        snapshot, record = self.sample()
        cases = [
            ({**snapshot, "schema": True}, record),
            ({**snapshot, "kind": "hf-local-session"}, record),
            ({**snapshot, "purpose": "real_training"}, record),
            (snapshot, {**record, "purpose": "real_training"}),
            (snapshot, {**record, "identity": {"base_model_sha256": "d" * 64}}),
            (snapshot, {**record, "updates": 2}),
            (snapshot, {**record, "updates": True}),
            ({**snapshot, "updates": -1}, {**record, "updates": -1}),
            (snapshot, {**record, "status": "failed"}),
            (snapshot, {**record, "status": "initial"}),
            ({**snapshot, "failed": True}, record),
            ({**snapshot, "adapters": {}}, record),
            ({**snapshot, "adapters": {"broken": [1.0]}}, record),
            ({**snapshot, "adapters": {"broken": torch.tensor([float("nan")], device="cpu")}}, record),
            ({**snapshot, "torch_cpu_rng": torch.tensor([1.0], device="cpu")}, record),
            ({**snapshot, "cursor": True}, record),
            (snapshot, {**record, "loss": float("nan")}),
        ]
        initial, initial_record = self.sample("initial")
        cases.append((initial, {**initial_record, "status": "completed"}))
        for index, (candidate, evidence) in enumerate(cases):
            with self.subTest(index=index), self.assertRaises(CustodyError):
                self.publisher.publish(candidate, evidence)
        with self.assertRaises(CustodyError):
            replace(self.publisher, purpose="real_training")
        self.assertEqual(self.store.verify_attempt(self.attempt).artifacts, ())

    def test_mixed_publications_and_tampered_references_are_rejected_before_decode(self):
        first = self.publisher.publish(*self.sample())
        second = self.publisher.publish(*self.sample())
        mixed = CheckpointPublication(first.checkpoint, second.evidence)
        bad_digest = replace(first, checkpoint=replace(first.checkpoint, sha256="0" * 64))
        for publication in (mixed, bad_digest):
            with self.subTest(publication=publication):
                with patch("alora.checkpoints.load_snapshot") as decode:
                    with self.assertRaises(CustodyError):
                        self.publisher.load(publication)
                    decode.assert_not_called()

    def test_valid_artifacts_with_forged_purpose_or_update_evidence_are_rejected(self):
        publication = self.publisher.publish(*self.sample())
        original = json.loads(self.store.verify(publication.evidence).read_bytes())
        for field, value in (("purpose", "real_training"), ("updates", 2)):
            evidence = {**original, field: value}
            reference = self.store.publish_bytes(
                self.attempt, f"wrong-{field}.json", "evidence",
                json.dumps(evidence, allow_nan=False).encode(),
                inputs=(publication.checkpoint,), max_bytes=_BYTES,
            )
            forged = replace(publication, evidence=reference)
            with self.subTest(field=field), self.assertRaises(CustodyError):
                self.publisher.load(forged)

    def test_artifacts_from_another_attempt_are_rejected(self):
        publication = self.publisher.publish(*self.sample())
        other_attempt = self.store.begin_attempt(self.attempt.run_id, {"purpose": _PURPOSE})
        other_publisher = CheckpointPublisher(
            self.store, other_attempt, purpose=_PURPOSE, max_bytes=_BYTES,
        )
        with patch("alora.checkpoints.load_snapshot") as decode:
            with self.assertRaises(CustodyError):
                other_publisher.load(publication)
            decode.assert_not_called()

    def test_changed_checkpoint_bytes_are_rejected_before_decode(self):
        publication = self.publisher.publish(*self.sample())
        checkpoint_path = self.store.verify(publication.checkpoint)
        content = checkpoint_path.read_bytes()
        checkpoint_path.write_bytes(content[:-1] + bytes([content[-1] ^ 1]))
        with patch("alora.checkpoints.load_snapshot") as decode:
            with self.assertRaises(CustodyError):
                self.publisher.checkpoint_path(publication)
            decode.assert_not_called()

    def test_evidence_write_failure_retains_the_recoverable_failed_snapshot(self):
        snapshot, record = self.sample("failed")
        with patch.object(self.store, "publish_bytes", side_effect=OSError("metadata write fault")):
            with self.assertRaisesRegex(OSError, "metadata write fault"):
                self.publisher.publish(snapshot, record)
        checkpoint, = self.store.verify_attempt(self.attempt).artifacts
        restored = load_snapshot(self.store, checkpoint, max_bytes=_BYTES)
        self.assertTrue(restored["failed"])
        self.assertEqual(restored["updates"], 0)
        torch.testing.assert_close(
            restored["adapters"]["layer.0.1.B"], snapshot["adapters"]["layer.0.1.B"],
            rtol=0, atol=0, equal_nan=True,
        )
