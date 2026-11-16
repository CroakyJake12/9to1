from dataclasses import replace
import unittest

from custody.resources import (
    BudgetRefusal, GpuObservation, HostObservation, Limits, OperatorPolicy, ResourceUse, admit, check_running,
)

_FIXTURE_UUID = "GPU-10000000-0000-0000-0000-000000000001"


class ResourcePolicyTests(unittest.TestCase):
    def setUp(self):
        self.gpu = GpuObservation(_FIXTURE_UUID, "NVIDIA GeForce RTX 5080", 120, 49, 100)
        self.host = HostObservation("Fixture CPU", 1000, 2000, (self.gpu,), False, logical_cpus=4)
        self.limits = Limits(5, 120, 110, 100, 75, 78, 200, 12)
        self.sample = ResourceUse(119, 109, 99, 76, 11, self.gpu, False, free_ram_bytes=20)
        self.policy = OperatorPolicy((_FIXTURE_UUID,), cpu_threads=2, minimum_free_ram_bytes=20)

    def admit(self, limits, host, **settings):
        return admit(limits, host, policy=self.policy, **settings)

    def test_explicitly_admits_only_approved_gpu_and_disables_cpu_gpu_access(self):
        self.assertEqual(self.admit(self.limits, self.host, device_uuid=self.gpu.uuid).device_uuid, self.gpu.uuid)
        for gpu, expected in [
            (replace(self.gpu, name="NVIDIA Tesla V100"), "v100_cooling_hold"),
            (replace(self.gpu, uuid="GPU-another"), "gpu_not_allowlisted"),
            (replace(self.gpu, name="unknown"), "gpu_not_allowlisted"),
        ]:
            with self.subTest(gpu=gpu):
                with self.assertRaisesRegex(BudgetRefusal, expected):
                    self.admit(self.limits, replace(self.host, gpus=(gpu,)), device_uuid=gpu.uuid)
        with self.assertRaisesRegex(BudgetRefusal, "missing_or_ambiguous"):
            self.admit(self.limits, replace(self.host, gpus=()), device_uuid=self.gpu.uuid)
        with self.assertRaisesRegex(BudgetRefusal, "cpu_job_has_gpu_allocation"):
            self.admit(self.limits, self.host, device_uuid=None)
        cpu = self.admit(replace(self.limits, vram_bytes=0), self.host, device_uuid=None)
        with self.assertRaisesRegex(BudgetRefusal, "unexpected_gpu_use"):
            check_running(cpu, self.sample, elapsed_seconds=0)

    def test_preflight_denies_unknown_or_unavailable_resources(self):
        for host, reason in [
            (replace(self.host, cpu_identity=""), "cpu_observation_missing"),
            (replace(self.host, game_or_protected_work_active=None), "protected_work_active_or_unknown"),
            (replace(self.host, free_ram_bytes=139), "ram_capacity"),
            (replace(self.host, logical_cpus=1), "cpu_thread_capacity"),
            (replace(self.host, free_storage_bytes=174), "storage_capacity"),
            (replace(self.host, gpus=(replace(self.gpu, free_vram_bytes=109),)), "vram_capacity"),
            (replace(self.host, gpus=(replace(self.gpu, temperature_c=float("nan")),)), "invalid_scalar_measurement"),
        ]:
            with self.subTest(reason=reason):
                with self.assertRaisesRegex(BudgetRefusal, reason):
                    self.admit(self.limits, host, device_uuid=self.gpu.uuid)
        with self.assertRaisesRegex(BudgetRefusal, "iteration_capacity"):
            self.admit(self.limits, self.host, device_uuid=self.gpu.uuid, observed_iterations=12)

    def test_live_numbers_trigger_each_budget_and_device_signal(self):
        admission = self.admit(self.limits, self.host, device_uuid=self.gpu.uuid)
        check_running(admission, self.sample, elapsed_seconds=4.9)
        self.assertRaisesRegex(BudgetRefusal, "wall_time_capacity",
                               check_running, admission, self.sample, elapsed_seconds=5)
        for sample, reason in [
            (replace(self.sample, ram_bytes=121), "ram_capacity"),
            (replace(self.sample, free_ram_bytes=19), "host_ram_pressure"),
            (replace(self.sample, vram_bytes=111), "vram_capacity"),
            (replace(self.sample, written_bytes=101), "storage_capacity"),
            (replace(self.sample, free_storage_bytes=74), "storage_capacity"),
            (replace(self.sample, completed_iterations=12), "iteration_capacity"),
            (replace(self.sample, gpu=replace(self.gpu, temperature_c=78)), "temperature_capacity"),
            (replace(self.sample, gpu=replace(self.gpu, power_watts=200)), "power_capacity"),
            (replace(self.sample, game_or_protected_work_active=True), "protected_work_active_or_unknown"),
        ]:
            with self.subTest(reason=reason):
                self.assertRaisesRegex(BudgetRefusal, reason,
                                       check_running, admission, sample, elapsed_seconds=0)

    def test_real_cpu_execution_requires_cpu_thermal_and_power_telemetry(self):
        limits = replace(self.limits, vram_bytes=0)
        with self.assertRaisesRegex(BudgetRefusal, "cpu_telemetry_missing"):
            self.admit(limits, self.host, device_uuid=None, purpose="real_training")
        host = replace(self.host, cpu_temperature_c=50, cpu_power_watts=100)
        admission = self.admit(limits, host, device_uuid=None, purpose="real_training")
        sample = replace(self.sample, gpu=None, vram_bytes=0)
        with self.assertRaisesRegex(BudgetRefusal, "cpu_telemetry_missing"):
            check_running(admission, sample, elapsed_seconds=0)
        check_running(admission, replace(sample, cpu_temperature_c=50, cpu_power_watts=100), elapsed_seconds=0)
