"""CPU arithmetic fixtures through the real TrainingControl file protocol."""
import importlib.util
import json
import os
from pathlib import Path
import time
import unittest
from unittest.mock import patch
from uuid import uuid4

from hf_backend.cpu_guard import install
CUDA_REFUSAL = install()

import torch

from custody import ArtifactStore
from custody.artifacts import create_directory
from alora.checkpoints import CheckpointPublisher
from alora import test_data, worker
from alora.test_support import NumericModel, make_session

_SERVICE = Path(__file__).resolve().parents[4] / "eng/dulche/service/training_worker_client.py"
_SPEC = importlib.util.spec_from_file_location("alora_control_software_fixture", _SERVICE)
_CONTROL = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(_CONTROL)


class WorkerTests(unittest.TestCase):
    def setUp(self):
        self.assertFalse(torch.cuda.is_initialized())
        torch.manual_seed(990)
        torch.set_num_threads(1)
        self.root = Path(os.environ["DULCHE_ALORA_TEST_ARTIFACT_ROOT"]) / ("worker-" + uuid4().hex)
        create_directory(self.root, exclusive=True)
        self.store = ArtifactStore(self.root / "store")
        self.run = self.store.create_run("ALORA worker numeric software fixture",
                                         {"purpose": "software_test_only"})
        fixture = test_data.TrainingDatasetTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        fixture.seal()
        self.dataset = fixture.load()
        self.identity = {"base_model_sha256": "a" * 64,
                         "dataset_manifest_sha256": self.dataset.manifest_sha256,
                         "profile_sha256": "c" * 64}

    def tearDown(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(CUDA_REFUSAL.dulche_guard_calls, 0)

    def publisher(self):
        attempt = self.store.begin_attempt(self.run, {"purpose": "software_test_only"})
        return CheckpointPublisher(self.store, attempt, purpose="software_test_only",
                                   max_bytes=2_000_000)

    def control(self, *, action="run"):
        output = self.root / ("output-" + uuid4().hex)
        create_directory(output, exclusive=True)
        command = output / "control.json"
        job = "software-job-" + uuid4().hex
        command.write_text(json.dumps({"job": job, "action": action,
                                       "permit_until_monotonic": time.monotonic() + 300}))
        with patch.dict(os.environ, {
            "DULCHE_TRAIN_JOB": job, "DULCHE_TRAIN_OUTPUT": str(output),
            "DULCHE_TRAIN_CONTROL": str(command), "DULCHE_RESUME_CHECKPOINT": "",
        }):
            return _CONTROL.TrainingControl()

    def execute(self, session, publisher, control, target):
        return worker.run_training(session, self.dataset, publisher, control,
                                   target_updates=target,
                                   checkpoint_exception=_CONTROL.CheckpointRequired)

    def assert_tree_equal(self, actual, expected):
        if isinstance(expected, torch.Tensor):
            self.assertTrue(torch.equal(actual, expected))
        elif isinstance(expected, dict):
            self.assertEqual(actual.keys(), expected.keys())
            for key in expected:
                self.assert_tree_equal(actual[key], expected[key])
        elif isinstance(expected, (tuple, list)):
            self.assertEqual(len(actual), len(expected))
            for left, right in zip(actual, expected):
                self.assert_tree_equal(left, right)
        else:
            self.assertEqual(actual, expected)

    def test_real_control_reports_progress_only_after_durable_checkpoint_and_measurements(self):
        session = make_session(identity=self.identity)
        publisher, control = self.publisher(), self.control()
        original_progress = control.progress
        acknowledgements = []
        def progress(step):
            evidence = [json.loads(line) for line in
                        (control.output / "optimizer-evidence.jsonl").read_text().splitlines()]
            self.assertIn(step, {row["step"] for row in evidence})
            self.assertTrue(all(row["max_abs_update"] > 0 for row in evidence))
            self.assertTrue(all(row["parameter"] in session.parameters for row in evidence))
            self.assertGreaterEqual(len(self.store.verify_attempt(publisher.attempt).artifacts), 2 * step)
            acknowledgements.append(step)
            original_progress(step)
        with patch.object(control, "progress", side_effect=progress):
            result = self.execute(session, publisher, control, 2)
        self.assertEqual(acknowledgements, [1, 2])
        self.assertEqual(result["phase"], "completed")
        self.assertEqual(control.record["phase"], "completed")
        self.assertEqual(control.record["steps"], 2)
        self.assertEqual(control.record["checkpoint"]["sha256"], result["checkpoint_sha256"])
        self.assertEqual(Path(result["checkpoint_path"]).parent, control.output / "checkpoints")
        self.assertEqual(publisher.load(result["publication"])["updates"], 2)
        self.assertFalse(session.failed)

    def test_stop_before_first_update_publishes_initial_zero_checkpoint_as_paused(self):
        session = make_session(identity=self.identity)
        publisher, control = self.publisher(), self.control(action="checkpoint-and-exit")
        prior = session.snapshot()
        with patch.object(session, "step_row", wraps=session.step_row) as step:
            result = self.execute(session, publisher, control, 1)
        step.assert_not_called()
        self.assertEqual(result["phase"], "paused")
        self.assertEqual(control.record["steps"], 0)
        self.assertEqual(control.record["checkpoint"]["steps"], 0)
        self.assertEqual((control.output / "optimizer-evidence.jsonl").read_bytes(), b"")
        self.assert_tree_equal(publisher.load(result["publication"]), prior)
        self.assertFalse(session.failed)

    def test_expired_permit_at_each_compute_boundary_never_commits_an_update(self):
        for stop_at in (1, 2, 3, 4):
            with self.subTest(boundary=stop_at):
                session = make_session(identity=self.identity)
                publisher, control = self.publisher(), self.control()
                prior = session.snapshot()
                calls = 0
                def permit():
                    nonlocal calls
                    calls += 1
                    return time.monotonic() + (60 if calls < stop_at else -1)
                with patch.object(control, "wait_for_compute", side_effect=permit):
                    result = self.execute(session, publisher, control, 1)
                self.assertEqual(result["phase"], "paused")
                self.assertEqual(session.updates, 0)
                self.assert_tree_equal(publisher.load(result["publication"]), prior)
                self.assertFalse(session.failed)

    def test_malformed_permit_is_a_failure_not_a_successful_pause(self):
        for deadline in (True, float("nan"), float("inf"), "later"):
            with self.subTest(deadline=deadline):
                session = make_session(identity=self.identity)
                publisher, control = self.publisher(), self.control()
                with patch.object(control, "wait_for_compute", return_value=deadline):
                    with self.assertRaises(ValueError):
                        self.execute(session, publisher, control, 1)
                self.assertTrue(session.failed)
                self.assertEqual(session.updates, 0)
                self.assertNotIn("checkpoint", control.record)
                self.assertTrue(list(control.output.glob("worker-failure-*.json")))

    def test_resume_uses_saved_manifest_cursor_and_matches_uninterrupted_updates(self):
        base = NumericModel()
        baseline = make_session(base, identity=self.identity)
        initial = baseline.snapshot()
        self.execute(baseline, self.publisher(), self.control(), 3)
        expected = baseline.snapshot()
        staged = make_session(base, identity=self.identity)
        staged.restore(initial)
        first_publisher = self.publisher()
        first = self.execute(staged, first_publisher, self.control(), 1)
        resumed = make_session(base, identity=self.identity)
        resumed.restore(first_publisher.load(first["publication"]))
        control = self.control()
        result = self.execute(resumed, self.publisher(), control, 3)
        self.assert_tree_equal(resumed.snapshot(), expected)
        self.assertEqual(result["updates"], 3)
        self.assertEqual(resumed.last_record["source"]["sample_id"], "train-0")
        evidence = [json.loads(line) for line in
                    (control.output / "optimizer-evidence.jsonl").read_text().splitlines()]
        self.assertEqual({row["step"] for row in evidence}, {2, 3})

    def test_storage_or_control_failure_after_update_stops_without_retry_or_completion(self):
        for fault in ("log", "progress", "publish_checkpoint", "finish"):
            with self.subTest(fault=fault):
                session = make_session(identity=self.identity)
                publisher, control = self.publisher(), self.control()
                target, name = ((worker, "_optimizer_evidence") if fault == "log"
                                else (control, fault))
                with patch.object(target, name, side_effect=OSError("software write fault")):
                    with self.assertRaises(OSError):
                        self.execute(session, publisher, control, 1)
                self.assertEqual(session.updates, 1)
                self.assertTrue(session.failed)
                self.assertNotEqual(control.record["phase"], "completed")
                failures = list(control.output.glob("worker-failure-*.json"))
                self.assertEqual(len(failures), 1)
                self.assertFalse(json.loads(failures[0].read_text())["retry_current_session"])
                self.assertGreaterEqual(len(self.store.verify_attempt(publisher.attempt).artifacts), 2)
                with self.assertRaises(ValueError):
                    self.execute(session, publisher, self.control(), 2)
                self.assertEqual(session.updates, 1)

    def test_control_computeinterrupted_after_commit_is_not_converted_into_pause(self):
        session = make_session(identity=self.identity)
        publisher, control = self.publisher(), self.control()
        with patch.object(control, "progress",
                          side_effect=worker.ComputeInterrupted("software progress fault")):
            with self.assertRaises(worker.ComputeInterrupted):
                self.execute(session, publisher, control, 1)
        self.assertTrue(session.failed)
        self.assertEqual(session.updates, 1)
        self.assertNotIn("checkpoint", control.record)
        self.assertNotEqual(control.record["phase"], "paused")

    def test_existing_attempt_log_is_preserved_and_cannot_be_reused(self):
        session = make_session(identity=self.identity)
        publisher, control = self.publisher(), self.control()
        log = control.output / "optimizer-evidence.jsonl"
        log.write_bytes(b"retained prior evidence\n")
        with self.assertRaises(FileExistsError):
            self.execute(session, publisher, control, 1)
        self.assertEqual(log.read_bytes(), b"retained prior evidence\n")
        self.assertEqual(session.updates, 0)
        self.assertEqual(control.record["phase"], "preparing")

    def test_targets_require_actual_new_updates_and_a_valid_cursor(self):
        for target in (0, True, 1_000_001):
            session = make_session(identity=self.identity)
            control = self.control()
            with self.subTest(target=target), self.assertRaises(ValueError):
                self.execute(session, self.publisher(), control, target)
            self.assertEqual(control.record["phase"], "preparing")
        session = make_session(identity=self.identity)
        self.execute(session, self.publisher(), self.control(), 1)
        with self.assertRaises(ValueError):
            self.execute(session, self.publisher(), self.control(), 1)
        session.cursor += 1
        with self.assertRaises(ValueError):
            self.execute(session, self.publisher(), self.control(), 2)

    def test_small_fixture_is_not_accepted_as_real_curriculum_but_software_run_is_explicitly_exempt(self):
        with self.assertRaises(ValueError):
            worker._admit_curriculum(self.dataset)
        session = make_session(identity=self.identity)
        with patch.object(worker, "_admit_curriculum",
                          side_effect=AssertionError("software fixtures must stay exempt")) as admission:
            result = self.execute(session, self.publisher(), self.control(), 1)
        admission.assert_not_called()
        self.assertEqual(result["phase"], "completed")


if __name__ == "__main__":
    unittest.main()
