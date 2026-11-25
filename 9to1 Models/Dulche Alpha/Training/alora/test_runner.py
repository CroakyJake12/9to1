"""CPU numeric fixtures for service identity and data-only ALORA continuation."""
from dataclasses import replace
import hashlib
import json
import os
from pathlib import Path
import shutil
import unittest
from unittest.mock import patch
from uuid import uuid4

from hf_backend.cpu_guard import install
CUDA_REFUSAL = install()

import torch

from custody import ArtifactStore
from custody.artifacts import create_directory
from alora import runner, test_workflow
from alora.checkpoints import CheckpointPublisher
from alora.test_support import NumericModel, make_session, numeric_loss
from alora.workflow import canonical, canonical_sha256, read_json


class RunnerTests(unittest.TestCase):
    def setUp(self):
        self.assertFalse(torch.cuda.is_initialized())
        torch.set_num_threads(1)
        torch.manual_seed(771)
        self.root = Path(os.environ["DULCHE_ALORA_TEST_ARTIFACT_ROOT"]) / ("runner-" + uuid4().hex)
        create_directory(self.root, exclusive=True)
        self.base = NumericModel()

    def tearDown(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(CUDA_REFUSAL.dulche_guard_calls, 0)

    def publisher(self):
        store = ArtifactStore(self.root / ("store-" + uuid4().hex))
        run = store.create_run("ALORA resume software-only numeric fixture",
                               {"purpose": "software_test_only"})
        attempt = store.begin_attempt(run, {"purpose": "software_test_only"})
        return CheckpointPublisher(store, attempt, purpose="software_test_only", max_bytes=2_000_000)

    def checkpoint(self, session):
        publisher = self.publisher()
        publication = session.publish_current(publisher)
        source = publisher.checkpoint_path(publication)
        directory = self.root / ("software-cas-" + uuid4().hex)
        create_directory(directory, exclusive=True)
        destination = directory / publication.checkpoint.sha256
        shutil.copyfile(source, destination)
        output = self.root / ("software-output-" + uuid4().hex)
        create_directory(output, exclusive=True)
        job = runner.ServiceJob(
            uuid4().hex, output, output.parent / "software-control.json",
            session.updates, destination, publication.checkpoint.sha256,
            publication.checkpoint.size_bytes, "d" * 64)
        return job

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

    def test_service_snapshot_import_preserves_exact_optimizer_rng_and_next_update(self):
        original = make_session(self.base)
        original.step_software(lambda: numeric_loss(original))
        job = self.checkpoint(original)
        prior_bytes = job.resume_path.read_bytes()
        resumed = make_session(self.base)
        publisher = self.publisher()
        reference = runner.restore_service_checkpoint(resumed, publisher, job)
        self.assertEqual(reference.sha256, job.resume_sha256)
        self.assert_tree_equal(resumed.snapshot(), original.snapshot())
        original.step_software(lambda: numeric_loss(original))
        resumed.step_software(lambda: numeric_loss(resumed))
        self.assert_tree_equal(resumed.snapshot(), original.snapshot())
        self.assertEqual(job.resume_path.read_bytes(), prior_bytes)
        artifacts = publisher.store.verify_attempt(publisher.attempt).artifacts
        self.assertEqual(len(artifacts), 2)  # Imported snapshot and bound import evidence.

    def test_tampered_cas_bytes_are_refused_before_snapshot_decoding(self):
        original = make_session(self.base)
        job = self.checkpoint(original)
        with job.resume_path.open("ab") as stream:
            stream.write(b"software tampering")
        resumed = make_session(self.base)
        prior = resumed.snapshot()
        with patch("custody.snapshot.load_snapshot") as decode, self.assertRaises(ValueError):
            runner.restore_service_checkpoint(resumed, self.publisher(), job)
        decode.assert_not_called()
        self.assert_tree_equal(resumed.snapshot(), prior)

    def test_supervisor_step_or_identity_mismatch_cannot_restore_adapter_state(self):
        original = make_session(self.base)
        original.step_software(lambda: numeric_loss(original))
        job = self.checkpoint(original)
        resumed = make_session(self.base)
        prior = resumed.snapshot()
        with self.assertRaises(ValueError):
            runner.restore_service_checkpoint(resumed, self.publisher(), replace(job, completed_steps=2))
        self.assert_tree_equal(resumed.snapshot(), prior)
        wrong = dict(original.identity)
        wrong = {key: wrong[key] for key in
                 ("base_model_sha256", "dataset_manifest_sha256", "profile_sha256")}
        wrong["base_model_sha256"] = "e" * 64
        resumed = make_session(self.base, identity=wrong)
        prior = resumed.snapshot()
        with self.assertRaises(ValueError):
            runner.restore_service_checkpoint(resumed, self.publisher(), job)
        self.assert_tree_equal(resumed.snapshot(), prior)

    def test_resume_requires_content_addressed_name_size_limit_and_real_saved_steps(self):
        original = make_session(self.base)
        job = self.checkpoint(original)
        for changed in (
            replace(job, resume_sha256="f" * 64),
            replace(job, resume_size=2_000_001),
            replace(job, resume_size=True),
            replace(job, resume_path=None, completed_steps=1),
        ):
            with self.subTest(job=changed), self.assertRaises(ValueError):
                runner.restore_service_checkpoint(make_session(self.base), self.publisher(), changed)
        fresh = replace(job, resume_path=None, resume_sha256=None, resume_size=None, completed_steps=0)
        self.assertIsNone(runner.restore_service_checkpoint(make_session(self.base), self.publisher(), fresh))

    def protocol(self):
        fixture_root = self.root / ("protocol-" + uuid4().hex)
        _, result, spec, _ = test_workflow.fixture_bundle(fixture_root)
        # This is only a software_test_only file-protocol fixture. It is never
        # passed through real request validation, registration or model loading.
        workflow = read_json(result["workflow"]["path"])
        job_id = uuid4().hex
        job_dir = fixture_root / "jobs" / job_id
        output = job_dir / "output"
        output.mkdir(parents=True)
        pid = os.getpid()
        unit = f"dulche-training-{job_id}-worker.service"
        status = {
            "id": job_id, "device": "cpu", "worker_pid": pid, "worker_unit": unit,
            "phase": "starting", "workflow": spec["id"],
            "workflow_digest": canonical_sha256(workflow), "steps": 0,
            "checkpoint": None, "resume_of": None,
        }
        launch = {key: workflow[key] for key in ("executable", "arguments", "cwd", "device")}
        launch.update(gpu_minor=None, read_roots=workflow["read_only_roots"])
        control_path = job_dir / "control.json"
        control_path.write_bytes(canonical({"job": job_id, "action": "wait", "permit_until_monotonic": 0}))
        (job_dir / "status.json").write_bytes(canonical(status))
        (job_dir / "launch.json").write_bytes(canonical(launch))
        (output / "isolation.json").write_bytes(canonical({"pid": pid, "device": "cpu", "landlock_abi": 5}))
        env = {"DULCHE_TRAIN_JOB": job_id, "DULCHE_TRAIN_CONTROL": str(control_path),
               "DULCHE_TRAIN_OUTPUT": str(output), "CUDA_VISIBLE_DEVICES": "",
               "DULCHE_RESUME_CHECKPOINT": ""}
        return spec, result["deployment"], env, f"0::/user.slice/{unit}\n", job_dir

    def test_cpu_job_admission_binds_actual_pid_cgroup_isolation_launch_and_workflow(self):
        spec, pin, env, cgroup, _ = self.protocol()
        with patch.dict(os.environ, env), patch.object(runner, "_cgroup", return_value=cgroup):
            admitted = runner.admit_cpu_job(spec, pin)
        self.assertEqual(admitted.job_id, env["DULCHE_TRAIN_JOB"])
        self.assertEqual(admitted.output, Path(env["DULCHE_TRAIN_OUTPUT"]))
        self.assertEqual(admitted.completed_steps, 0)
        self.assertIsNone(admitted.resume_path)
        self.assertFalse((admitted.output / "progress.json").exists())

    def test_wrong_process_gpu_visibility_or_forged_workflow_refused_before_readiness(self):
        for fault in ("pid", "cgroup", "gpu", "workflow", "isolation", "launch", "cancel"):
            spec, pin, env, cgroup, job_dir = self.protocol()
            if fault == "pid":
                status = read_json(job_dir / "status.json")
                status["worker_pid"] += 1
                (job_dir / "status.json").write_bytes(canonical(status))
            elif fault == "cgroup":
                cgroup = "0::/unrelated.service\n"
            elif fault == "gpu":
                env["CUDA_VISIBLE_DEVICES"] = "0"
            elif fault == "workflow":
                status = read_json(job_dir / "status.json")
                status["workflow_digest"] = "0" * 64
                (job_dir / "status.json").write_bytes(canonical(status))
            elif fault == "isolation":
                path = job_dir / "output/isolation.json"
                receipt = read_json(path)
                receipt["landlock_abi"] = 4
                path.write_bytes(canonical(receipt))
            elif fault == "launch":
                path = job_dir / "launch.json"
                launch = read_json(path)
                launch["arguments"] = []
                path.write_bytes(canonical(launch))
            elif fault == "cancel":
                (job_dir / "control.json").write_bytes(canonical({
                    "job": env["DULCHE_TRAIN_JOB"], "action": "checkpoint-and-exit"}))
            with self.subTest(fault=fault), patch.dict(os.environ, env), \
                    patch.object(runner, "_cgroup", return_value=cgroup), self.assertRaises(ValueError):
                runner.admit_cpu_job(spec, pin)
            self.assertFalse((job_dir / "output/progress.json").exists())

    def test_resume_environment_must_match_supervisor_receipt(self):
        spec, pin, env, cgroup, job_dir = self.protocol()
        source_session = make_session(self.base)
        source_session.step_software(lambda: numeric_loss(source_session))
        saved = self.checkpoint(source_session)
        status = read_json(job_dir / "status.json")
        status.update(steps=saved.completed_steps, resume_of=uuid4().hex,
                      checkpoint={"path": str(saved.resume_path), "sha256": saved.resume_sha256,
                                  "size": saved.resume_size, "steps": saved.completed_steps})
        (job_dir / "status.json").write_bytes(canonical(status))
        env["DULCHE_RESUME_CHECKPOINT"] = str(saved.resume_path)
        with patch.dict(os.environ, env), patch.object(runner, "_cgroup", return_value=cgroup):
            admitted = runner.admit_cpu_job(spec, pin)
        self.assertEqual(admitted.resume_sha256, saved.resume_sha256)
        self.assertEqual(admitted.completed_steps, 1)
        status["checkpoint"]["steps"] = 2
        (job_dir / "status.json").write_bytes(canonical(status))
        with patch.dict(os.environ, env), patch.object(runner, "_cgroup", return_value=cgroup), \
                self.assertRaises(ValueError):
            runner.admit_cpu_job(spec, pin)


if __name__ == "__main__":
    unittest.main()
