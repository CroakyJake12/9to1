import copy
import json
import os
from pathlib import Path
import threading
import unittest
from unittest.mock import patch
from uuid import uuid4

from hf_backend.cpu_guard import install
CUDA_REFUSAL = install()

import torch
from custody import ArtifactStore
from custody.artifacts import create_directory
from alora.checkpoints import CheckpointPublisher
from alora.session import ComputeInterrupted
from alora.test_support import NumericModel, make_session, numeric_loss
from alora import test_data


class SessionTests(unittest.TestCase):
    def setUp(self):
        torch.manual_seed(770)
        torch.set_num_threads(1)
        root = Path(os.environ["DULCHE_ALORA_TEST_ARTIFACT_ROOT"]) / ("session-" + uuid4().hex)
        create_directory(root, exclusive=True)
        self.store = ArtifactStore(root / "store")
        run = self.store.create_run("ALORA numeric optimizer fixture", {"purpose": "software_test_only"})
        attempt = self.store.begin_attempt(run, {"purpose": "software_test_only"})
        self.publisher = CheckpointPublisher(self.store, attempt, purpose="software_test_only", max_bytes=2_000_000)

    def tearDown(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(CUDA_REFUSAL.dulche_guard_calls, 0)

    def assert_tree_equal(self, actual, expected):
        if isinstance(expected, torch.Tensor):
            self.assertTrue(torch.equal(actual, expected))
        elif isinstance(expected, dict):
            self.assertEqual(actual.keys(), expected.keys())
            for key in expected:
                self.assert_tree_equal(actual[key], expected[key])
        elif isinstance(expected, (list, tuple)):
            self.assertEqual(len(actual), len(expected))
            for a, b in zip(actual, expected):
                self.assert_tree_equal(a, b)
        else:
            self.assertEqual(actual, expected)

    def test_fresh_session_resume_exactly_matches_uninterrupted_second_update(self):
        base = NumericModel()
        original = make_session(base)
        def loss(session):
            return numeric_loss(session) * (0.5 + torch.rand((), dtype=torch.float64))
        first, publication = original.step_software(lambda: loss(original), publisher=self.publisher)
        saved = self.publisher.load(publication)
        expected_record, _ = original.step_software(lambda: loss(original))
        expected = original.snapshot()
        resumed = make_session(base)
        resumed.restore(saved)
        actual_record, _ = resumed.step_software(lambda: loss(resumed))
        self.assertEqual(first["updates"], 1)
        self.assertEqual(actual_record["loss"], expected_record["loss"])
        self.assert_tree_equal(resumed.snapshot(), expected)
        self.assertEqual(resumed.cursor, 2)

    def test_training_row_uses_reverified_custody_and_publishes_true_update(self):
        fixture = test_data.TrainingDatasetTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        fixture.seal()
        dataset = fixture.load()
        identity = {"base_model_sha256": "a" * 64, "dataset_manifest_sha256": dataset.manifest_sha256,
                    "profile_sha256": "c" * 64}
        session = make_session(identity=identity)
        row = next(dataset.rows())
        record, publication = session.step_row(dataset, row, self.publisher)
        self.assertEqual(record["source"], row["_source"])
        self.assertEqual(self.publisher.load(publication)["updates"], 1)
        self.assertNotEqual(record["parameter_before_sha256"], record["parameter_after_sha256"])
        (fixture.root / "train.jsonl").write_bytes(b"changed")
        with self.assertRaises(ValueError):
            session.step_row(dataset, row, self.publisher)
        self.assertEqual(session.updates, 1)

    def test_restore_rejects_corruption_before_mutating_destination(self):
        source = make_session()
        source.step_software(lambda: numeric_loss(source))
        good = source.snapshot()
        mutations = [
            lambda s: s.update(schema=True),
            lambda s: s["identity"].update(base_model_sha256="d" * 64),
            lambda s: s["adapters"].pop(next(iter(s["adapters"]))),
            lambda s: s["optimizer"]["state"].clear(),
            lambda s: next(iter(s["optimizer"]["state"].values())).update(step=torch.tensor(True)),
            lambda s: next(iter(s["optimizer"]["state"].values())).update(step=torch.tensor([1.])),
            lambda s: next(iter(s["optimizer"]["state"].values()))["exp_avg_sq"].fill_(-1),
            lambda s: s.update(torch_cpu_rng=torch.tensor([1], dtype=torch.uint8)),
        ]
        destination = make_session()
        prior = destination.snapshot()
        for mutate in mutations:
            incoming = copy.deepcopy(good)
            mutate(incoming)
            with self.subTest(mutation=mutate), self.assertRaises((ValueError, RuntimeError)):
                destination.restore(incoming)
            self.assert_tree_equal(destination.snapshot(), prior)

    def test_permit_interruption_at_every_boundary_preserves_completed_state(self):
        for interrupt_at in (1, 2, 3):
            session = make_session()
            prior = session.snapshot()
            calls = 0
            def permit():
                nonlocal calls
                calls += 1
                if calls == interrupt_at:
                    raise ComputeInterrupted("software permit expired")
            with self.subTest(boundary=interrupt_at), self.assertRaises(ComputeInterrupted):
                session.step_software(lambda: numeric_loss(session) * torch.rand(()),
                                      before_compute=permit)
            self.assert_tree_equal(session.snapshot(), prior)
            self.assertFalse(session.failed)
            published = session.publish_current(self.publisher)
            self.assertEqual(self.publisher.load(published)["updates"], 0)

    def test_nonfinite_loss_is_failed_evidence_and_stops_retry(self):
        session = make_session()
        with self.assertRaises(ValueError):
            session.step_software(lambda: numeric_loss(session) * float("nan"), publisher=self.publisher)
        self.assertTrue(session.failed)
        self.assertEqual(session.updates, 0)
        self.assertEqual(session.last_record["status"], "failed")
        before = len(self.store.verify_attempt(self.publisher.attempt).artifacts)
        with self.assertRaises(RuntimeError):
            session.step_software(lambda: numeric_loss(session), publisher=self.publisher)
        self.assertEqual(len(self.store.verify_attempt(self.publisher.attempt).artifacts), before)
        self.assertTrue(all(p.grad is None for p in session.parameters.values()))

    def test_partial_optimizer_failure_retains_failed_changed_state_without_success(self):
        session = make_session()
        prior = session.snapshot()
        parameter = next(iter(session.parameters.values()))
        def partial():
            with torch.no_grad():
                parameter.add_(0.1)
            raise RuntimeError("software optimizer fault after write")
        with patch.object(session.optimizer, "step", side_effect=partial):
            with self.assertRaises(RuntimeError):
                session.step_software(lambda: numeric_loss(session), publisher=self.publisher)
        self.assertTrue(session.failed)
        self.assertEqual(session.updates, 0)
        self.assertFalse(torch.equal(parameter, prior["adapters"][next(iter(session.parameters))]))
        evidence = [a for a in self.store.verify_attempt(self.publisher.attempt).artifacts
                    if json.loads((self.store.verify(a).parent / "manifest.json").read_text())["kind"] == "evidence"]
        self.assertEqual(len(evidence), 1)
        self.assertEqual(json.loads(self.store.verify(evidence[0]).read_text())["status"], "failed")

    def test_malformed_failed_optimizer_state_is_preserved_as_raw_evidence(self):
        from custody.snapshot import load_snapshot
        session = make_session()
        parameter = next(iter(session.parameters.values()))
        def corrupt_then_fail():
            session.optimizer.state[parameter] = {
                "step": torch.tensor(float("nan")),
                "exp_avg": torch.zeros_like(parameter),
                "exp_avg_sq": torch.zeros_like(parameter),
            }
            raise RuntimeError("software malformed optimizer state")
        with patch.object(session.optimizer, "step", side_effect=corrupt_then_fail):
            with self.assertRaises(RuntimeError):
                session.step_software(lambda: numeric_loss(session), publisher=self.publisher)
        checkpoints = [a for a in self.store.verify_attempt(self.publisher.attempt).artifacts
                       if json.loads((self.store.verify(a).parent / "manifest.json").read_text())["kind"] == "segment_checkpoint"]
        self.assertEqual(len(checkpoints), 1)
        saved = load_snapshot(self.store, checkpoints[0], max_bytes=2_000_000)
        self.assertTrue(saved["failed"])
        self.assertTrue(torch.isnan(saved["optimizer"]["state"][0]["step"]).item())
        self.assertIsNone(saved["optimizer_slots"][next(iter(session.parameters))])
        with self.assertRaises(ValueError):
            session.restore(saved)

    def test_publication_failure_after_update_stops_and_retains_failure_evidence(self):
        session = make_session()
        publish = CheckpointPublisher.publish
        def fail_success(publisher, snapshot, record):
            if record["status"] == "completed":
                raise OSError("software publication fault")
            return publish(publisher, snapshot, record)
        with patch.object(CheckpointPublisher, "publish", fail_success):
            with self.assertRaises(OSError):
                session.step_software(lambda: numeric_loss(session), publisher=self.publisher)
        self.assertEqual(session.updates, 1)
        self.assertTrue(session.failed)
        self.assertEqual(session.last_record["attempted_update"], 1)
        with self.assertRaises(RuntimeError):
            session.step_software(lambda: numeric_loss(session))

    def test_concurrent_snapshot_and_reentrant_update_are_refused(self):
        session = make_session()
        errors = []
        def inspect():
            try:
                session.snapshot()
            except RuntimeError as error:
                errors.append(str(error))
        def loss():
            thread = threading.Thread(target=inspect)
            thread.start()
            thread.join(timeout=5)
            self.assertFalse(thread.is_alive())
            with self.assertRaises(RuntimeError):
                session.step_software(lambda: numeric_loss(session))
            return numeric_loss(session)
        session.step_software(loss)
        self.assertEqual(len(errors), 1)
        self.assertEqual(session.updates, 1)

    def test_optimizer_configuration_drift_cannot_emit_a_successful_update(self):
        session = make_session()
        prior = {n: p.clone() for n, p in session.parameters.items()}
        session.optimizer.param_groups[0]["lr"] *= 2
        with self.assertRaises(ValueError):
            session.step_software(lambda: numeric_loss(session), publisher=self.publisher)
        self.assertTrue(session.failed)
        self.assertEqual(session.updates, 0)
        for name, parameter in session.parameters.items():
            self.assertTrue(torch.equal(parameter, prior[name]))

    def test_real_training_cannot_use_arithmetic_fixture_path(self):
        # Constructor may require a bound real profile before this stage.
        with self.assertRaises(ValueError):
            session = make_session(purpose="real_training")
            session.step_software(lambda: numeric_loss(session))
