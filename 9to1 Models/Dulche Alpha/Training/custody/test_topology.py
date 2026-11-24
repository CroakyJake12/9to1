from dataclasses import replace
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from custody.resources import BudgetRefusal, GpuObservation, Limits
from custody.supervisor import ProcessSupervisor
from custody.topology import OwnedLaneObservation, TrainingLane, preflight_heterogeneous_training


class TrainingTopologyTests(unittest.TestCase):
    def setUp(self):
        self.policy = (
            TrainingLane(0, "native", "native-host", "GPU-00000000-0000-0000-0000-000000000001", "a" * 64),
            TrainingLane(1, "legacy_vm", "legacy-guest", "GPU-00000000-0000-0000-0000-000000000002", "b" * 64),
        )
        self.observations = tuple(
            OwnedLaneObservation(lane, "fresh-attempt", f"owner-{lane.rank}", "optimizer-participant-v1", True, True, True, False,
                                 GpuObservation(lane.gpu_uuid, model, 103, 50, 60))
            for lane, model in zip(self.policy, ("NVIDIA GeForce RTX 5080", "Tesla V100-PCIE-16GB"))
        )
        self.limits = Limits(5, 10, 100, 10, 10, 70, 100, 1)

    def test_exact_preflight_does_not_admit_the_local_launcher(self):
        preflight_heterogeneous_training(self.policy, self.observations, self.limits, expected_attempt_id="fresh-attempt")
        for topology in (self.policy, (), {"lanes": 2}):
            with self.subTest(topology=topology):
                # A poison self verifies rejection precedes touching any store, worker or dataset.
                with self.assertRaisesRegex(BudgetRefusal, "^heterogeneous_training_executor_unavailable$"):
                    ProcessSupervisor.run(None, None, "worker", {}, purpose="real_training", limits=self.limits,
                                          host=None, device_uuid=None, observe=None, training_topology=topology)

    def test_packaged_operator_cli_imports_its_custody_dependencies(self):
        from worker_api.prepare_bundle import prepare_bundle

        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "operator.zip"
            prepare_bundle(archive, cli=True)
            probe = (
                "import sys; sys.path.insert(0,sys.argv[1]); "
                "import worker_api.bridge, custody.topology; "
                "assert custody.topology.__file__.startswith(sys.argv[1])"
            )
            completed = subprocess.run([sys.executable, "-I", "-S", "-B", "-c", probe, str(archive)],
                                       cwd=directory, capture_output=True, text=True, timeout=10)
            self.assertEqual(completed.returncode, 0, completed.stderr)

    def test_prefixed_exact_nvml_alias_is_accepted(self):
        guest = self.observations[1]
        observed = replace(guest, gpu=replace(guest.gpu, name="NVIDIA Tesla V100-PCIE-16GB"))
        preflight_heterogeneous_training(self.policy, self.observations[:1] + (observed,), self.limits,
                                         expected_attempt_id="fresh-attempt")

    def test_order_and_bound_identities(self):
        for policy, observations, code in (
            (self.policy, self.observations[::-1], "training_lane_identity_mismatch"),
            (self.policy, self.observations[:1], "training_lane_observations_missing"),
            (self.policy[::-1], self.observations, "invalid_heterogeneous_training_policy"),
            (self.policy[:1] + (replace(self.policy[1], host_id=self.policy[0].host_id),),
             self.observations, "duplicate_training_lane"),
            (self.policy[:1] + (replace(self.policy[1], gpu_uuid=self.policy[0].gpu_uuid),),
             self.observations, "duplicate_training_lane"),
            (self.policy, self.observations[:1] + (replace(self.observations[1], lane=replace(
                self.policy[1], runtime_sha256="f" * 64)),), "training_lane_identity_mismatch"),
        ):
            with self.subTest(code=code), self.assertRaisesRegex(BudgetRefusal, f"^{code}$"):
                preflight_heterogeneous_training(policy, observations, self.limits, expected_attempt_id="fresh-attempt")

    def test_guest_cannot_use_inference_or_bypass_per_lane_safety(self):
        guest = self.observations[1]
        for changed, code in (
            (replace(guest, attempt_id="previous-attempt"), "training_lane_attempt_mismatch"),
            (replace(guest, capability="inference-v1"), "training_lane_capability_missing"),
            (replace(guest, ownership_id=self.observations[0].ownership_id), "training_lane_not_exclusively_owned"),
            (replace(guest, exclusive_ownership_verified=False), "training_lane_not_exclusively_owned"),
            (replace(guest, cooling_verified=None), "training_lane_cooling_unverified"),
            (replace(guest, mentor_gpu_released=False), "training_lane_gpu_not_released"),
            (replace(guest, protected_work_active=None), "protected_work_active_or_unknown"),
            (replace(guest, gpu=replace(guest.gpu, uuid=self.policy[0].gpu_uuid)), "training_lane_gpu_mismatch"),
            (replace(guest, gpu=replace(guest.gpu, temperature_c=70)), "temperature_capacity"),
            (replace(guest, gpu=replace(guest.gpu, power_watts=100)), "power_capacity"),
            (replace(guest, gpu=replace(guest.gpu, free_vram_bytes=99)), "vram_capacity"),
        ):
            with self.subTest(code=code), self.assertRaisesRegex(BudgetRefusal, f"^{code}$"):
                preflight_heterogeneous_training(self.policy, self.observations[:1] + (changed,), self.limits, expected_attempt_id="fresh-attempt")
