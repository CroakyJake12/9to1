"""Guarded CPU resume fixtures with retained source and destination attempts."""

import copy
from dataclasses import asdict
import json
import unittest
from unittest.mock import patch
from uuid import uuid4

from hf_backend import test_local_session as fixtures
from hf_backend.adapter import LocalSession
from hf_backend.checkpoints import CheckpointPublication, VerifiedCheckpointPublisher
from custody import CustodyError
from custody.artifacts import _encode_record
from custody.snapshot import load_snapshot, publish_snapshot

torch = fixtures.torch
_PURPOSE = "software_test_only"
_BYTES = 8 * 1024 * 1024


class ResumeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        class Artifacts(fixtures.LocalSessionTests):
            pass
        Artifacts.setUpClass()
        cls.fixture = Artifacts

    def setUp(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(fixtures.CUDA_REFUSAL.dulche_guard_calls, 0)

    def tearDown(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(fixtures.CUDA_REFUSAL.dulche_guard_calls, 0)

    def session(self, method="full", sources=()):
        fixture = self.fixture
        lineage = tuple(ref for source in sources for ref in (source.checkpoint, source.evidence))
        attempt = fixture.store.begin_attempt(fixture.run_id, {"purpose": _PURPOSE, "test": self.id()},
                                              resume_from=lineage)
        publisher = VerifiedCheckpointPublisher(fixture.store, attempt, purpose=_PURPOSE, max_bytes=_BYTES)
        session = LocalSession(fixture.store, fixture.bundle, fixture.manifest, fixture.data_dir,
                               purpose=_PURPOSE, method=method, checkpoint_publisher=publisher)
        self.addCleanup(lambda: publish_snapshot(fixture.store, attempt, "final-resume-test-state",
                                               session.snapshot(), max_bytes=_BYTES))
        return session, attempt

    def baseline(self, method="full"):
        session, _ = self.session(method=method)
        record = session.step_file("train")
        self.assertIs(type(session.last_publication), CheckpointPublication)
        return session, session.last_publication, {key: value for key, value in record.items() if key != "publication"}

    def assert_state(self, actual, expected):
        self.assertIs(type(actual), type(expected))
        if isinstance(expected, torch.Tensor):
            self.assertEqual(actual.shape, expected.shape)
            self.assertEqual(actual.dtype, expected.dtype)
            self.assertTrue(torch.equal(actual.contiguous().reshape(-1).view(torch.uint8),
                                        expected.contiguous().reshape(-1).view(torch.uint8)))
        elif isinstance(expected, dict):
            self.assertEqual(actual.keys(), expected.keys())
            for key in expected:
                self.assert_state(actual[key], expected[key])
        elif isinstance(expected, (list, tuple)):
            self.assertEqual(len(actual), len(expected))
            for left, right in zip(actual, expected, strict=True):
                self.assert_state(left, right)
        else:
            self.assertEqual(actual, expected)

    def test_full_and_lora_next_update_matches_model_adam_and_rng_bit_for_bit(self):
        store = self.fixture.store
        for method in ("full", "lora"):
            with self.subTest(method=method):
                uninterrupted, first, _ = self.baseline(method)
                expected_record = uninterrupted.step_file("train")
                expected = load_snapshot(store, uninterrupted.last_publication.checkpoint, max_bytes=_BYTES)

                resumed, attempt = self.session(method=method, sources=(first,))
                resumed.restore_checkpoint(first)
                self.assertEqual(resumed.updates, 1)
                self.assertIsNone(resumed.last_publication)
                actual_record = resumed.step_file("train")
                self.assertEqual(actual_record["loss"], expected_record["loss"])
                self.assertEqual(resumed.last_publication.checkpoint.attempt, attempt)
                actual = load_snapshot(store, resumed.last_publication.checkpoint, max_bytes=_BYTES)
                self.assert_state(actual, expected)
                retained = json.loads((store.attempt_path(attempt) / "attempt.json").read_bytes())
                self.assertEqual(retained["resume_from"], [asdict(first.checkpoint), asdict(first.evidence)])

    def test_mixed_receipts_and_unrecorded_lineage_are_refused(self):
        session, first, _ = self.baseline()
        session.step_file("train")
        mixed = CheckpointPublication(first.checkpoint, session.last_publication.evidence)
        destination, _ = self.session(sources=(mixed,))
        with self.assertRaisesRegex(CustodyError, "exact sealed evidence"):
            destination.restore_checkpoint(mixed)
        with self.assertRaisesRegex(CustodyError, "retain both source references"):
            destination.restore_checkpoint(first)
        self.assertEqual(destination.updates, 0)

    def test_corrupt_and_incompatible_checkpoints_leave_the_destination_unchanged(self):
        _, source, metrics = self.baseline()
        fixture = self.fixture
        store = fixture.store
        snapshot = load_snapshot(store, source.checkpoint, max_bytes=_BYTES)
        attempt = store.begin_attempt(fixture.run_id, {"purpose": _PURPOSE, "phase": "invalid-resume-inputs"})
        publisher = VerifiedCheckpointPublisher(store, attempt, purpose=_PURPOSE, max_bytes=_BYTES)

        identity = snapshot["resume"]["compatibility"]
        refs = identity["base_refs"]
        different_refs = {**refs, "config.json": {**refs["config.json"], "artifact_id": str(uuid4())}}
        wrong_identity = {**snapshot["resume"], "compatibility": {**identity, "base_refs": different_refs}}
        adam = copy.deepcopy(snapshot["optimizer"])
        next(iter(adam["state"].values()))["exp_avg_sq"].fill_(-1)
        cases = (
            {"torch_version": "another-runtime"}, {"method": "lora"},
            {"adapter_config": {"rank": 17}}, {"resume": wrong_identity},
            {"optimizer": adam}, {"rng": snapshot["rng"][:-1]},
        )
        receipts = [publisher.publish({**snapshot, **change}, metrics) for change in cases]
        invalid = store.publish_bytes(attempt, "synthetic-corrupt-resume", "segment_checkpoint", b"not a snapshot archive")
        evidence = store.publish_bytes(attempt, "synthetic-corrupt-resume-evidence", "evidence", _encode_record({
            "schema": 1, "purpose": _PURPOSE, "status": "completed", "checkpoint": asdict(invalid), "record": metrics,
        }), inputs=(invalid,))
        receipts.append(CheckpointPublication(invalid, evidence))

        destination, _ = self.session(sources=receipts)
        before = destination.snapshot()
        for receipt in receipts:
            with self.subTest(artifact=receipt.checkpoint.artifact_id), self.assertRaises(CustodyError):
                destination.restore_checkpoint(receipt)
            self.assert_state(destination.snapshot(), before)

    def test_restore_rolls_back_partial_writes_and_stops_on_uncertain_rollback(self):
        _, source, _ = self.baseline()
        for uncertain in (False, True):
            with self.subTest(uncertain=uncertain):
                destination, attempt = self.session(sources=(source,))
                before = destination.snapshot()
                optimizer = destination.optimizer
                load = destination.model.load_state_dict
                calls = 0

                def fault(state, *, strict):
                    nonlocal calls
                    calls += 1
                    if calls == 1:
                        with torch.no_grad():
                            next(destination.model.parameters()).add_(1)
                        destination.model.training = not destination.model.training
                        torch.rand(1, device="cpu")
                        raise OSError("software fault after partial restore")
                    if uncertain:
                        raise RuntimeError("software fault during rollback")
                    return load(state, strict=strict)

                with patch.object(destination.model, "load_state_dict", side_effect=fault):
                    if uncertain:
                        with self.assertRaises(ExceptionGroup) as raised:
                            destination.restore_checkpoint(source)
                        self.assertIsInstance(raised.exception.exceptions[0], OSError)
                        self.assertIn("during rollback", str(raised.exception.exceptions[1]))
                    else:
                        with self.assertRaisesRegex(OSError, "after partial restore"):
                            destination.restore_checkpoint(source)
                self.assertIs(destination.optimizer, optimizer)
                self.assertEqual(destination.updates, 0)
                if uncertain:
                    self.assertTrue(destination.failed)
                    with self.assertRaisesRegex(RuntimeError, "Session failed"):
                        destination.step_file("train")
                    self.assertEqual(len(self.fixture.store.verify_attempt(attempt).artifacts), 2)
                else:
                    self.assert_state(destination.snapshot(), before)
