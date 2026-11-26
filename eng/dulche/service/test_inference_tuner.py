"""CPU-only custody and failure tests; no systemd unit, model or GPU is started."""
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import threading
import time
from types import SimpleNamespace
import unittest
from unittest import mock

import inference_tuner as tuner
from engine_profiles import atomic_json
from pooled_runtime import GuardIdentity


OP = "1" * 32
INVOCATION = "2" * 32
UNIT = "dulche-inference-tune-" + OP + "-" + "3" * 16 + ".service"


class TunerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.work = self.root / "inference-management" / OP
        self.work.mkdir(parents=True, mode=0o700)
        self.control = self.root / "control"
        self.control.mkdir()
        self.config = {"model_name": tuner.PRIMARY, "args": ["--native", "/models/model.bin",
                       "--max-context", "524288", "--mtp", "/models/draft.bin"]}
        atomic_json(self.work / "source.json", self.config)
        self.command = [str(self.control / "python/bin/python3.12"), str(self.control / "strata.py"),
                        "tune", "auto", str(self.work / "source.json"), "--mode", "quick", "--goal", "speed",
                        "--use", "coding", "--time", "20", "--no-save", "--gpu-lock", str(self.root / "gpu.lock"),
                        "--work-dir", str(self.work), "--status-file", str(self.work / "status.json"), "--output", "json"]
        self.rule = {"id": "rtx5080", "kind": "nvml", "index": 0, "warn_c": 75, "stop_c": 80,
                     "core_grace_s": 10, "core_emergency_c": 85, "memory_stop_c": 80, "jump_min_c": 75,
                     "thermal_policy_version": "active-cooling-v1", "allow_simulated": False,
                     "min_host_available_mib": 8192, "rise_c_per_min": None, "jump_c": None}
        self.guard_config = self.root / "guard.json"
        atomic_json(self.guard_config, {"devices": [self.rule], "status_file": str(self.root / "thermal.json")})
        self.pin = GuardIdentity(1234, "100", INVOCATION, "0" * 64, str(self.guard_config),
                                 tuner._digest(self.guard_config), str(self.root / "thermal.json"),
                                 "/usr/bin/python3", "/guard.service")

    def tearDown(self):
        self.temp.cleanup()

    def snapshot(self, now=100, temp=45):
        return {"ts": now, "halted": False, "devices": {"rtx5080": {
            "last_telemetry_ts": now, "telemetry_ok": True, "stopped": False, "standby": False,
            "power_limit_verified": True,
            "thermal_policy": {"version": "active-cooling-v1", "warn_c": 75, "stop_c": 80,
                               "core_grace_s": 10, "core_emergency_c": 85, "memory_stop_c": 80},
            "reading_full": {"temp": temp, "temp_mem": None, "driver_ok": True,
                             "name": "NVIDIA GeForce RTX 5080", "power_limit_active": 360}}}}

    def test_command_preserves_512k_and_rejects_bypass_or_other_operation(self):
        before = (self.work / "source.json").read_bytes()
        with mock.patch.object(tuner, "_control", return_value=self.control):
            work, context = tuner._validate_command(self.root, tuner.PRIMARY, OP, self.command, self.control)
            self.assertEqual((work, context), (self.work, 524288))
            for command in (self.command + ["--no-quiet-check"], self.command + ["--disable-guard"],
                            [x for x in self.command if x != "--no-save"],
                            ["/usr/bin/python3", *self.command[1:]]):
                with self.subTest(command=command[-1]):
                    with self.assertRaises(tuner.TunerError):
                        tuner._validate_command(self.root, tuner.PRIMARY, OP, command, self.control)
        self.assertEqual((self.work / "source.json").read_bytes(), before)

    def test_thermal_monitor_uses_real_snapshot_and_stricter_memory_floor(self):
        monitor = tuner.ThermalMonitor(self.pin)
        self.assertEqual(monitor.rule.min_host_available_mib, 12288)
        monitor.check(snapshot=self.snapshot(), memory={"MemAvailable": tuner.MIN_AVAILABLE}, now=100)
        for scenario in ("stale", "memory", "simulated", "stopped", "policy", "driver"):
            sample = self.snapshot()
            row = sample["devices"]["rtx5080"]
            memory = {"MemAvailable": tuner.MIN_AVAILABLE}
            if scenario == "stale": sample["ts"] = 93
            if scenario == "memory": memory["MemAvailable"] -= 1
            if scenario == "simulated": row["reading_full"]["simulated"] = True
            if scenario == "stopped": row["stopped"] = True
            if scenario == "policy": row["thermal_policy"]["stop_c"] = 81
            if scenario == "driver": row["reading_full"]["driver_ok"] = False
            with self.subTest(scenario=scenario), self.assertRaises(tuner.TunerError):
                tuner.ThermalMonitor(self.pin).check(snapshot=sample, memory=memory, now=100)

    def test_existing_thermal_grace_and_emergency_rules_are_reused(self):
        monitor = tuner.ThermalMonitor(self.pin)
        memory = {"MemAvailable": tuner.MIN_AVAILABLE}
        monitor.check(snapshot=self.snapshot(100, 80), memory=memory, now=100)
        monitor.check(snapshot=self.snapshot(109, 80), memory=memory, now=109)
        with self.assertRaisesRegex(tuner.TunerError, "over_temperature"):
            monitor.check(snapshot=self.snapshot(110, 80), memory=memory, now=110)
        with self.assertRaisesRegex(tuner.TunerError, "core_emergency_temperature"):
            tuner.ThermalMonitor(self.pin).check(snapshot=self.snapshot(100, 85), memory=memory, now=100)

    def test_parent_loss_thermal_fault_and_cancel_all_clean_owned_children(self):
        for reason in ("parent", "thermal", "cancel", "output"):
            child, custody = mock.Mock(), mock.Mock()
            child.poll.return_value = None
            custody.stop_children.return_value = True
            check = mock.Mock(side_effect=tuner.TunerError("stale telemetry") if reason == "thermal" else None)
            result = tuner.supervise_child(child, custody, check=check,
                        parent_alive=lambda: reason != "parent", cancelled=lambda: reason == "cancel",
                        timeout=10, output=SimpleNamespace(overflow=reason == "output"))
            with self.subTest(reason=reason):
                self.assertNotEqual(result["returncode"], 0)
                self.assertTrue(result["cleanup_verified"])
                custody.stop_children.assert_called_once()

    def test_success_is_not_returned_for_unverified_process_cleanup(self):
        child, custody = mock.Mock(), mock.Mock()
        child.poll.return_value = 0
        custody.stop_children.return_value = False
        result = tuner.supervise_child(child, custody, check=lambda: None, parent_alive=lambda: True,
                    cancelled=lambda: False, timeout=10, output=SimpleNamespace(overflow=False))
        self.assertEqual(result["state"], "cleanup_unverified")
        self.assertNotEqual(result["returncode"], 0)

    def test_timeout_is_independently_bounded_and_cleans_children(self):
        custody = mock.Mock()
        custody.stop_children.return_value = True
        result = tuner.supervise_child(mock.Mock(), custody, check=lambda: None, parent_alive=lambda: True,
                    cancelled=lambda: False, timeout=10, output=SimpleNamespace(overflow=False),
                    clock=mock.Mock(side_effect=[100, 111]))
        self.assertIn("time limit", result["error"])
        custody.stop_children.assert_called_once()

    def test_systemd_bounds_cgroup_and_writes_a_terminal_receipt_after_guardian_death(self):
        recipe = {"script": "/owned/inference_tuner.py", "timeout_seconds": 2700,
                  "memory_max": 24 * 2 ** 30, "unit": UNIT, "cwd": str(self.control)}
        command = tuner.service_command(self.work / "recipe.json", recipe)
        for expected in ("--property=KillMode=control-group", "--property=MemorySwapMax=0",
                         "--property=OOMPolicy=kill", "--property=RuntimeMaxSec=2730", "--property=TasksMax=128"):
            self.assertIn(expected, command)
        self.assertTrue(any(x.startswith("--property=ExecStopPost=") and "settle" in x for x in command))
        self.assertNotIn("sh", command)

    def test_pidfd_cleanup_kills_only_listed_owned_processes(self):
        # Real CPU subprocesses and real pidfd signals. Only the cgroup inventory
        # is mocked because the test runner must not create a systemd service.
        owned = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(30)"], start_new_session=True)
        unrelated = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(30)"], start_new_session=True)
        try:
            group = tuner.process_identity(owned.pid)["cgroup"]
            with mock.patch.object(tuner, "_cgroup_path", return_value=self.root):
                custody = tuner.OwnedCgroup(UNIT, INVOCATION, group)
            custody.members = lambda: {owned.pid} if owned.poll() is None else set()
            self.assertTrue(custody.stop_children(reap=owned.poll))
            self.assertIsNotNone(owned.poll())
            self.assertIsNone(unrelated.poll())
        finally:
            for child in (owned, unrelated):
                if child.poll() is None: child.kill()
                child.wait(timeout=3)

    def test_pid_reuse_or_cgroup_mismatch_never_signals_another_process(self):
        child = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(30)"])
        try:
            with mock.patch.object(tuner, "_cgroup_path", return_value=self.root):
                custody = tuner.OwnedCgroup(UNIT, INVOCATION, "/owned/" + UNIT)
            with self.assertRaisesRegex(tuner.TunerError, "outside"):
                custody.signal_member(child.pid, signal.SIGTERM)
            self.assertIsNone(child.poll())
        finally:
            child.kill()
            child.wait(timeout=3)

    def test_foreign_unit_is_not_stopped_and_active_unit_is_not_settled(self):
        recipe = {"unit": UNIT, "operation_id": OP}
        receipt = {"unit": UNIT, "operation_id": OP, "invocation": INVOCATION,
                   "cgroup": "/owned/" + UNIT, "settled": True, "cleanup_verified": True}
        state = {"ActiveState": "active", "Transient": "yes", "InvocationID": "f" * 32,
                 "ControlGroup": receipt["cgroup"]}
        with mock.patch.object(tuner, "unit_state", return_value=state), mock.patch.object(tuner, "_run") as run:
            with self.assertRaises(tuner.CustodyUnsettled): tuner._stop_owned(recipe, receipt)
            run.assert_not_called()
            self.assertFalse(tuner._settled(recipe, receipt))

    def test_receipt_alone_cannot_claim_cleanup_with_remaining_children(self):
        recipe = {"unit": UNIT, "operation_id": OP}
        receipt = {**recipe, "invocation": INVOCATION, "cgroup": "/owned/" + UNIT,
                   "settled": True, "cleanup_verified": True}
        with mock.patch.object(tuner, "unit_state", return_value={"ActiveState": "inactive", "InvocationID": INVOCATION}), \
                mock.patch.object(tuner, "_members", return_value={999}):
            self.assertFalse(tuner._settled(recipe, receipt))

    def test_unobserved_admission_raises_custody_unsettled(self):
        with mock.patch.object(tuner, "_control", return_value=self.control), \
                mock.patch.object(tuner, "_guard", return_value=self.pin), \
                mock.patch.object(tuner, "_memory", return_value={"MemTotal": 64 * 2 ** 30, "MemAvailable": 40 * 2 ** 30}), \
                mock.patch.object(tuner, "_run"), mock.patch.object(tuner, "_stop_owned"), \
                mock.patch.object(tuner.time, "monotonic", side_effect=[0, 4000]), \
                mock.patch.object(tuner.time, "sleep"):
            with self.assertRaises(tuner.CustodyUnsettled) as error:
                tuner.run_tuner(self.root, tuner.PRIMARY, OP, self.command, cwd=self.control,
                                cancel_event=threading.Event())
        self.assertTrue(error.exception.unit.startswith("dulche-inference-tune-"))
        self.assertTrue((self.work / "tuner-runner/recipe.json").is_file())
        self.assertTrue((self.work / "tuner-runner/cancel.json").is_file())


if __name__ == "__main__":
    unittest.main()
