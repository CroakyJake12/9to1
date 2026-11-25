"""Software-only guard/CLI contracts and exact retained calibration continuation."""
import copy
import fcntl
import io
import os
from pathlib import Path
import tempfile
import time
import unittest
from unittest.mock import patch

from hf_backend.cpu_guard import install
CUDA_REFUSAL = install()

import torch

from alora import __main__ as commands, calibration_cli, test_calibration, test_workflow, workflow
from alora.calibration import calibrate
from alora.data import TrainingDataset
from alora.session import ComputeInterrupted


class CalibrationCliTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="alora-calibration-cli-software-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        original, _, _, source = test_workflow.fixture_bundle(self.root / "fixture")
        self.source = source
        self.lock = self.root / "software-sdk.lock"
        self.lock.write_bytes(b"")
        self.guard = self.root / "guarded_lowmem_build.py"
        actual_guard = Path(__file__).resolve().parents[4] / "eng/development/guarded_lowmem_build.py"
        self.guard.write_bytes(actual_guard.read_bytes())
        self.request = {
            "schema": workflow.CALIBRATION_SCHEMA, "purpose": "software_test_only",
            "id": "software-calibration", "model": "software_test_only numeric fixture",
            "python": original["python"], "dataset": original["dataset"],
            "config": original["config"], "shards": original["shards"],
            "settings": {name: original["settings"][name] for name in workflow._CALIBRATION_SETTINGS
                         if name != "samples_per_category"},
            "guard": {"script": workflow.artifact(self.guard), "sdk_lock": str(self.lock),
                      "floor_mib": 8192, "timeout_seconds": 120},
            "resume": None,
        }
        self.request["settings"]["samples_per_category"] = 1

    def tearDown(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(CUDA_REFUSAL.dulche_guard_calls, 0)

    def bundle(self, name="calibration"):
        return workflow._emit_calibration_bundle(
            self.request, self.root / name, request_sha256="d" * 64, source_root=self.source)

    def lineage(self, spec, pin):
        ticks = int((time.clock_gettime(time.CLOCK_BOOTTIME) - 1) * os.sysconf("SC_CLK_TCK"))
        owner = {"pid": 900001, "uid": os.getuid(), "ppid": 900000, "start_ticks": ticks - 100,
                 "argv": ["software_test_only_owner"], "executable": spec["deployment"]["python_resolved"]["path"]}
        guard = {"pid": 900002, "uid": os.getuid(), "ppid": owner["pid"], "start_ticks": ticks,
                 "argv": workflow.calibration_guard_command(spec, pin),
                 "executable": spec["deployment"]["python_resolved"]["path"]}
        return guard, owner

    def environment(self):
        return {"CUDA_VISIBLE_DEVICES": "-1", "OMP_NUM_THREADS": "1", "MKL_NUM_THREADS": "1"}

    def test_exact_parent_guard_and_outer_kernel_lock_are_required_without_readiness_claim(self):
        spec, pin, _ = self.bundle()
        guard, owner = self.lineage(spec, pin)
        with patch.object(workflow, "sdk_lock_path", return_value=self.lock), \
                patch.dict(os.environ, self.environment()), \
                patch.object(os, "getppid", return_value=guard["pid"]), \
                patch.object(calibration_cli, "_process",
                             side_effect=lambda pid: {guard["pid"]: guard, owner["pid"]: owner}[pid]), \
                patch.object(calibration_cli, "_held_sdk_lock", return_value=True):
            permit = calibration_cli.admit_calibration_guard(spec, pin)
        self.assertEqual(permit.guard, guard)
        self.assertEqual(permit.owner, owner)
        self.assertFalse((self.root / "calibration/progress.json").exists())
        command = workflow.calibration_guard_command(spec, pin)
        self.assertEqual(command[3], str(self.guard))
        self.assertEqual(command[10], "--")
        self.assertEqual(command[11:], [str(self.root / "calibration/worker"),
                                       "--spec", pin["path"], "--spec-sha256", pin["sha256"]])

    def test_forged_lineage_visibility_threads_or_missing_lock_refused_before_compute(self):
        spec, pin, _ = self.bundle()
        for fault in ("argv", "executable", "owner", "lock", "cuda", "threads", "training-job"):
            guard, owner = self.lineage(spec, pin)
            env = self.environment()
            if fault == "argv":
                guard["argv"] = guard["argv"] + ["--unregistered"]
            elif fault == "executable":
                guard["executable"] = "/software/unregistered-python"
            elif fault == "owner":
                owner["uid"] += 1
            elif fault == "cuda":
                env["CUDA_VISIBLE_DEVICES"] = "0"
            elif fault == "threads":
                env["OMP_NUM_THREADS"] = "2"
            elif fault == "training-job":
                env["DULCHE_TRAIN_JOB"] = "a" * 32
            with self.subTest(fault=fault), patch.object(workflow, "sdk_lock_path", return_value=self.lock), \
                    patch.dict(os.environ, env), patch.object(os, "getppid", return_value=guard["pid"]), \
                    patch.object(calibration_cli, "_process",
                                 side_effect=lambda pid: {guard["pid"]: guard, owner["pid"]: owner}[pid]), \
                    patch.object(calibration_cli, "_held_sdk_lock", return_value=fault != "lock"), \
                    self.assertRaises(ValueError):
                calibration_cli.admit_calibration_guard(spec, pin)

    def test_unavailable_guard_observations_become_cooperative_interruptions(self):
        spec, pin, _ = self.bundle()
        guard, owner = self.lineage(spec, pin)
        for operation in ("_process", "_held_sdk_lock", "_memory_mib"):
            permit = calibration_cli.CalibrationPermit(spec, guard, owner)
            permit.interruption_type = ComputeInterrupted
            with self.subTest(observation=operation), patch.object(os, "getppid", return_value=guard["pid"]), \
                    patch.object(calibration_cli, "_process",
                                 side_effect=lambda pid: {guard["pid"]: guard, owner["pid"]: owner}[pid]), \
                    patch.object(calibration_cli, "_held_sdk_lock", return_value=True), \
                    patch.object(calibration_cli, "_memory_mib", return_value=65536), \
                    patch.object(calibration_cli, operation, side_effect=FileNotFoundError("software lost observation")), \
                    self.assertRaises(ComputeInterrupted) as raised:
                permit()
            self.assertIsInstance(raised.exception.__cause__, FileNotFoundError)

    def test_memory_reserve_deadline_and_signal_stop_before_another_compute_operation(self):
        spec, pin, _ = self.bundle()
        guard, owner = self.lineage(spec, pin)
        for reason in ("memory", "deadline", "signal"):
            permit = calibration_cli.CalibrationPermit(spec, guard, owner)
            permit.interruption_type = ComputeInterrupted
            permit.stop_requested = reason == "signal"
            elapsed = 100 if reason == "deadline" else 1
            available = 8192 + 128 if reason == "memory" else 65536
            with self.subTest(reason=reason), patch.object(os, "getppid", return_value=guard["pid"]), \
                    patch.object(calibration_cli, "_process",
                                 side_effect=lambda pid: {guard["pid"]: guard, owner["pid"]: owner}[pid]), \
                    patch.object(calibration_cli, "_held_sdk_lock", return_value=True), \
                    patch.object(calibration_cli, "_memory_mib", return_value=available), \
                    patch.object(time, "clock_gettime", return_value=permit.started + elapsed), \
                    self.assertRaises(ComputeInterrupted):
                permit()

    def test_kernel_flock_observation_binds_the_actual_owner_and_exact_inode(self):
        self.assertFalse(calibration_cli._held_sdk_lock(self.lock, os.getpid()))
        with self.lock.open("rb") as held:
            fcntl.flock(held.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
            self.assertTrue(calibration_cli._held_sdk_lock(self.lock, os.getpid()))
            self.assertFalse(calibration_cli._held_sdk_lock(self.lock, os.getpid() + 1))
        self.assertFalse(calibration_cli._held_sdk_lock(self.lock, os.getpid()))

    def test_same_name_self_pinned_imitation_is_not_the_reviewed_memory_guard(self):
        guard = copy.deepcopy(self.request["guard"])
        self.guard.write_text('"""software_test_only imitation, deliberately refused"""\n')
        guard["script"] = workflow.artifact(self.guard)
        with patch.object(workflow, "sdk_lock_path", return_value=self.lock), self.assertRaises(ValueError):
            workflow.validate_calibration_guard(guard)

    def test_busy_existing_lock_stops_without_launching_or_creating_output(self):
        request_path = self.root / "request.json"
        request_path.write_bytes(workflow.canonical(self.request))
        pin = workflow.artifact(request_path)
        with self.lock.open("rb") as held:
            fcntl.flock(held.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
            with patch.object(workflow, "sdk_lock_path", return_value=self.lock), \
                    patch.object(calibration_cli.subprocess, "run") as launch, \
                    self.assertRaisesRegex(RuntimeError, "SDK lock is busy"):
                calibration_cli.run_calibration(request_path, request_sha256=pin["sha256"],
                                                output=self.root / "not-created")
            launch.assert_not_called()
        self.assertFalse((self.root / "not-created").exists())

    def test_calibration_cli_routes_pins_and_public_real_command_refuses_software_data(self):
        request_path = self.root / "request.json"
        request_path.write_bytes(workflow.canonical(self.request))
        pin = workflow.artifact(request_path)
        output = self.root / "not-created"
        with patch.object(calibration_cli, "run_calibration",
                          return_value={"purpose": "software_test_only", "phase": "interrupted",
                                        "complete": False}) as run, patch("sys.stdout", new_callable=io.StringIO):
            self.assertEqual(commands.main(["calibrate", "--request", str(request_path),
                                            "--request-sha256", pin["sha256"], "--output", str(output)]), 0)
        run.assert_called_once_with(request_path, request_sha256=pin["sha256"], output=output)
        with patch.object(workflow, "sdk_lock_path", return_value=self.lock), \
                patch.object(calibration_cli, "_memory_mib", return_value=65536), \
                patch.object(calibration_cli.subprocess, "run") as launch, self.assertRaises(ValueError):
            calibration_cli.run_calibration(request_path, request_sha256=pin["sha256"], output=output)
        launch.assert_not_called()
        self.assertFalse(output.exists())

    def test_parent_structural_preparation_defers_corpus_admission_to_the_guarded_child(self):
        request = copy.deepcopy(self.request)
        # Deliberately invalid relabelling remains unacceptable to full admission.
        request["purpose"] = "real_training"
        config = self.root / "numeric-config.json"
        config.write_bytes(workflow.canonical({"vocab_size": 32, "max_position_embeddings": 16}))
        request["config"] = workflow.artifact(config)
        with patch.object(workflow, "sdk_lock_path", return_value=self.lock), \
                patch("alora.data.TrainingDataset") as dataset:
            _, admitted, _, counts = workflow.validate_calibration_request(request, verify_corpus=False)
        dataset.assert_not_called()
        self.assertIsNone(admitted)
        self.assertIsNone(counts)
        with patch.object(workflow, "sdk_lock_path", return_value=self.lock), self.assertRaises(ValueError):
            workflow.validate_calibration_request(request)

    def test_deployment_preserves_pinned_partial_locators_and_resumes_real_numeric_observations(self):
        fixture = test_calibration.CalibrationTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)

        def interrupted():
            raise ComputeInterrupted("software-only pre-first-row stop")
        partial = calibrate(fixture.model, fixture.dataset, base_model_sha256=fixture.base_sha,
                            output_path=fixture.output("deployment-source"),
                            samples_per_category=1, check_permit=interrupted)
        self.assertFalse(partial["complete"])
        self.assertEqual(partial["completed_samples"], 0)
        original_profile = Path(partial["profile_path"]).read_bytes()
        original_receipt = Path(partial["receipt_path"]).read_bytes()
        self.request["resume"] = partial["resume"]
        self.request["dataset"] = {
            "manifest": workflow.artifact(fixture.dataset.manifest_path),
            "root": str(fixture.dataset.dataset_root), "vocab_size": 32, "max_tokens": 4}
        spec, _, _ = self.bundle()
        self.assertEqual(spec["purpose"], "software_test_only")
        self.assertEqual(spec["resume"], partial["resume"])
        self.assertNotIn("resume-profile.json", {path.name for path in (self.root / "calibration/inputs").iterdir()})
        deployed = TrainingDataset(Path(spec["dataset"]["manifest"]["path"]), Path(spec["dataset"]["root"]),
                                   expected_sha256=spec["dataset"]["manifest"]["sha256"],
                                   purpose="software_test_only", vocab_size=32, max_tokens=4)
        continued = calibrate(copy.deepcopy(fixture.model), deployed,
                              base_model_sha256=fixture.base_sha,
                              output_path=Path(spec["deployment"]["profile_path"]),
                              samples_per_category=1, resume=spec["resume"])
        reference = calibrate(copy.deepcopy(fixture.model), fixture.dataset,
                              base_model_sha256=fixture.base_sha,
                              output_path=fixture.output("deployment-reference"),
                              samples_per_category=1)
        self.assertTrue(continued["complete"])
        self.assertEqual(continued["completed_samples"], 5)
        self.assertEqual(continued["profile_sha256"], reference["profile_sha256"])
        self.assertEqual(Path(partial["profile_path"]).read_bytes(), original_profile)
        self.assertEqual(Path(partial["receipt_path"]).read_bytes(), original_receipt)


if __name__ == "__main__":
    unittest.main()
