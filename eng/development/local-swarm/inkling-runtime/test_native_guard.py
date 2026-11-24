import copy
import json
from pathlib import Path
import tempfile
import time
import unittest
from unittest.mock import patch

import native_guard as native
import thermal_guard as thermal
import engines


def device(config):
    actions = []
    guard = thermal.DeviceGuard(thermal.Rule(config), None, lambda *a: actions.append(a), lambda _: None)
    return guard, actions


RTX = dict(id="rtx5080", kind="nvml", stop_c=80, warn_c=60, jump_c=None,
           rise_c_per_min=None, min_host_available_mib=8192,
           unreadable_timeout_s=5, telemetry_timeout_s=10, startup_timeout_s=10)


class ThermalTests(unittest.TestCase):
    def test_rtx_ramp_below80_does_not_trip(self):
        guard, actions = device(RTX)
        guard.evaluate({"temp": 30}, 1, 8192)
        guard.evaluate({"temp": 79}, 2, 8192)
        self.assertFalse(any(a[0] == "stop" for a in actions))

    def test_rtx80_trips(self):
        guard, actions = device(RTX)
        guard.evaluate({"temp": 80}, 1, 8192)
        self.assertIn("over_temperature", [a[2] for a in actions])

    def test_host_faults_trip_only_configured_device(self):
        for available, reason in ((8191, "host_memory_below_floor"), (None, "host_memory_unreadable")):
            guard, actions = device(RTX)
            guard.evaluate({"temp": 30}, 1, available)
            self.assertEqual(actions[0][2], reason)
        guard, actions = device(dict(id="v100", stop_c=68, jump_c=10))
        guard.evaluate({"temp": 30}, 1, None)
        self.assertFalse(actions)

    def test_missing_stale_and_unreadable_samples_trip(self):
        for reading in (None, {"temp": 30, "ts": -100}, {"temp": None}):
            guard, actions = device(RTX)
            guard.evaluate({"temp": 30}, 0, 8192)
            guard.evaluate(reading, 1, 8192)
            guard.evaluate(reading, 15, 8192)
            self.assertTrue(any(a[0] == "stop" for a in actions))

    def test_v100_existing_jump_and_ceiling_preserved(self):
        guard, actions = device(dict(id="v100", stop_c=68, jump_c=10, rise_c_per_min=15,
                                    rise_window_s=30, rise_min_c=55))
        guard.evaluate({"temp": 40}, 1)
        guard.evaluate({"temp": 51}, 2)
        self.assertIn("temperature_jump_10C", [a[2] for a in actions])
        guard.evaluate({"temp": 68}, 3)
        self.assertIn("over_temperature", [a[2] for a in actions])

    def test_v100_healthy_admission_ignores_rtx_only_halt(self):
        reading = dict(driver_ok=True, name="Tesla V100-PCIE-16GB", power_limit_active=165, temp=55, temp_mem=60)
        status = dict(ts=time.time(), halted=True, devices={"v100":dict(telemetry_ok=True, stopped=False, reading_full=reading)})
        with patch.object(engines, "load_json", return_value=status):
            self.assertEqual(engines._v100_guard(), reading)
            status["devices"]["v100"]["stopped"] = True
            self.assertEqual(engines._v100_guard(), {})

    def test_non_native_targets_are_refused(self):
        with self.assertRaises(engines.EngineError):
            native.owned(object(), "qwen3.8-27b-v100")

    def test_stop_calls_only_owned_native_units(self):
        target = dict(name="dulche-inkling-test.service", invocation="test", group="/user.slice/test")
        calls = []
        with patch.object(native, "owned", side_effect=[target, None]), patch.object(native, "remaining", side_effect=[{101}, set(), set(), set(), set()]), patch.object(native, "command", side_effect=lambda *args: calls.append(args)):
            self.assertEqual(native.control("stop", object()), [target["name"]])
        self.assertEqual(calls, [("stop", "--no-block", target["name"])])

    def test_changed_invocation_refuses_further_actions(self):
        target = dict(name="dulche-inkling-test.service", invocation="old", group="/user.slice/test")
        with patch.object(native, "query_unit", return_value={"InvocationID":"new", "ControlGroup":target["group"]}), patch.object(native, "members", return_value={42}):
            with self.assertRaises(engines.EngineError):
                native.remaining(target)

    def test_failed_unit_query_is_not_reported_as_absent(self):
        class Result:
            returncode = 1
            stdout = ""
        with patch.object(native.subprocess, "run", return_value=Result()):
            with self.assertRaises(engines.EngineError):
                native.query_unit("dulche-inkling-test.service")

    def test_unreadable_cgroup_is_not_reported_as_empty(self):
        with patch.object(native.Path, "stat", side_effect=PermissionError("denied")):
            with self.assertRaises(PermissionError):
                native.members({"MainPID":"23", "ActiveState":"active", "ControlGroup":"/user.slice/test"})


if __name__ == "__main__":
    unittest.main()
