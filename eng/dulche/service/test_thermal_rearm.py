"""Hardware-free safety regressions for recovery of the independently running watchdog.

All readings below are explicit test fixtures. These tests open no GPU, remote socket, or
service manager and never publish fixtures to the production guard status path.
"""
import copy
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import thermal_guard as guard


def sample(now=100.0, **changes):
    value = {
        "ts": now,
        "simulated": False,
        "driver_ok": True,
        "name": "Tesla V100-PCIE-16GB",
        "temp": 32,
        "temp_mem": 34,
        "power_limit_active": 165.0,
        "power_limit_range": [100.0, 250.0],
        "util": 0,
    }
    value.update(changes)
    return value


class FakeSource:
    def __init__(self, reading=None, result=(None, "agent unreachable")):
        self.reading = reading
        self.result = result
        self.set_calls = []

    def read(self):
        return copy.deepcopy(self.reading)

    def set_power_limit(self, watts):
        self.set_calls.append(watts)
        return self.result


class Harness(guard.ThermalGuard):
    def __init__(self, config, sources):
        self.sources = sources
        self.actions = []
        self.logs = []
        super().__init__(config)

    def _source(self, rule):
        return self.sources[rule.id]

    def _act(self, rule):
        def record(level, device, reason, reading, first):
            self.actions.append((level, device, reason, first))
        return record

    def log(self, value):
        self.logs.append(copy.deepcopy(value))

    @staticmethod
    def heartbeat():
        pass


class RearmTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.standby = self.directory / "v100-standby"
        self.status = self.directory / "status.json"

    def make_guard(self, *, initial_result=(None, "agent unreachable"), rtx=False):
        v100 = {
            "id": "v100", "kind": "remote", "host": "127.0.0.1", "port": 19109,
            "warn_c": 60, "stop_c": 68, "clear_c": 5, "rise_c_per_min": 15,
            "rise_min_c": 55, "jump_c": 10, "telemetry_timeout_s": 15,
            "startup_timeout_s": 20, "unreadable_timeout_s": 5,
            "allow_simulated": False, "power_limit_w": 165,
            "standby_file": str(self.standby),
        }
        sources = {"v100": FakeSource(result=initial_result)}
        rules = [v100]
        if rtx:
            rules.insert(0, {
                "id": "rtx5080", "kind": "nvml", "warn_c": 60, "stop_c": 80,
                "clear_c": 6, "power_limit_w": None, "allow_simulated": False,
            })
            sources["rtx5080"] = FakeSource({"temp": 39, "temp_mem": None, "driver_ok": True})
        result = Harness({"devices": rules, "status_file": str(self.status)}, sources)
        with patch.object(guard.time, "time", return_value=100.0):
            result.apply_power_limits()
        return result, sources["v100"], next(d for d in result.devices if d.rule.id == "v100")

    def assert_halted(self, watchdog, device):
        self.assertTrue(device.state["stopped"])
        self.assertFalse(device.state["power_limit_verified"])
        self.assertIsNone(watchdog._apply_results["v100"]["applied_w"])
        self.assertIsNotNone(watchdog._apply_results["v100"]["error"])

    def test_offline_startup_recovers_only_after_independent_cap_readback(self):
        watchdog, source, device = self.make_guard()
        self.assert_halted(watchdog, device)
        source.reading = sample()
        events = watchdog.tick(now=100.0)
        self.assertFalse(device.state["stopped"])
        self.assertTrue(device.state["power_limit_verified"])
        self.assertEqual(watchdog._apply_results["v100"]["applied_w"], 165)
        self.assertIsNone(watchdog._apply_results["v100"]["error"])
        self.assertEqual(watchdog._apply_results["v100"]["verified_sample_ts"], 100.0)
        self.assertEqual(source.set_calls, [165], "revalidation must not send another hardware command")
        self.assertEqual([e["reason"] for e in events], ["cleared"])

    def test_wrong_setter_acknowledgement_never_counts_as_success(self):
        for applied in (250, 164.5, True, "165", float("nan"), float("inf")):
            with self.subTest(applied=applied):
                watchdog, source, device = self.make_guard(initial_result=(applied, None))
                self.assertTrue(device.state["stopped"])
                self.assertFalse(device.state["power_limit_verified"])
                self.assertIsNotNone(watchdog._apply_results["v100"]["error"])
                source.reading = sample(power_limit_active=250)
                watchdog.tick(now=100.0)
                self.assert_halted(watchdog, device)

    def test_invalid_or_untrusted_telemetry_cannot_clear_power_failure(self):
        cases = {
            "stale": {"ts": 94.999}, "future": {"ts": 100.001},
            "missing timestamp": {"ts": None}, "boolean timestamp": {"ts": True},
            "nan timestamp": {"ts": float("nan")}, "infinite timestamp": {"ts": float("inf")},
            "simulation": {"simulated": True}, "unknown provenance": {"simulated": None},
            "driver failed": {"driver_ok": False}, "driver truthy string": {"driver_ok": "true"},
            "wrong cap": {"power_limit_active": 250}, "near cap": {"power_limit_active": 164.5},
            "missing cap": {"power_limit_active": None}, "string cap": {"power_limit_active": "165"},
            "boolean cap": {"power_limit_active": True}, "nan cap": {"power_limit_active": float("nan")},
            "infinite cap": {"power_limit_active": float("inf")},
            "no range": {"power_limit_range": None}, "short range": {"power_limit_range": [100]},
            "inverted range": {"power_limit_range": [250, 100]},
            "excluded cap": {"power_limit_range": [166, 250]},
            "string range": {"power_limit_range": ["100", 250]},
            "boolean range": {"power_limit_range": [True, 250]},
            "nan range": {"power_limit_range": [100, float("nan")]},
            "missing core": {"temp": None}, "string core": {"temp": "32"},
            "nan core": {"temp": float("nan")}, "infinite core": {"temp": float("inf")},
            "missing HBM": {"temp_mem": None}, "boolean HBM": {"temp_mem": False},
            "string HBM": {"temp_mem": "34"}, "nan HBM": {"temp_mem": float("nan")},
            "wrong device": {"name": "Tesla V100-SXM2-32GB"},
        }
        for name, changes in cases.items():
            with self.subTest(name=name):
                watchdog, source, device = self.make_guard()
                source.reading = sample(**changes)
                watchdog.tick(now=100.0)
                self.assert_halted(watchdog, device)
                self.assertNotIn("cleared", [log.get("reason") for log in watchdog.logs])
        for malformed in (None, [], "not JSON object", 165):
            with self.subTest(malformed=malformed):
                watchdog, source, device = self.make_guard()
                source.reading = malformed
                watchdog.tick(now=100.0)
                self.assert_halted(watchdog, device)

    def test_hot_core_and_hbm_keep_the_thermal_latch_after_cap_is_verified(self):
        watchdog, source, device = self.make_guard()
        source.reading = sample(temp=70)
        watchdog.tick(now=100.0)
        self.assertTrue(device.state["power_limit_verified"])
        self.assertTrue(device.state["stopped"])
        self.assertEqual(device.state["stop_reason"], "over_temperature")
        for now, core, memory in ((101, 64, 34), (102, 62, 66), (103, 62, 68)):
            source.reading = sample(now, temp=core, temp_mem=memory)
            watchdog.tick(now=now)
            self.assertTrue(device.state["stopped"], (core, memory))
        source.reading = sample(104, temp=62, temp_mem=62)
        watchdog.tick(now=104)
        self.assertFalse(device.state["stopped"])

    def test_thermal_reason_cannot_erase_an_unresolved_power_failure(self):
        watchdog, source, device = self.make_guard()
        source.reading = sample(temp=70, power_limit_active=250)
        watchdog.tick(now=100.0)
        self.assertEqual(device.state["stop_reason"], "over_temperature")
        source.reading = sample(101, power_limit_active=250)
        watchdog.tick(now=101)
        self.assert_halted(watchdog, device)
        # This is also blocked when a later fault changes the display reason.
        device.state["stop_reason"] = "telemetry_lost"
        source.reading = sample(102, power_limit_active=250)
        watchdog.tick(now=102)
        self.assert_halted(watchdog, device)
        source.reading = sample(103)
        watchdog.tick(now=103)
        self.assertFalse(device.state["stopped"])

    def test_valid_setter_reply_cannot_override_wrong_hardware_readback(self):
        watchdog, source, device = self.make_guard(initial_result=(165, None))
        source.reading = sample(power_limit_active=250)
        watchdog.tick(now=100.0)
        self.assert_halted(watchdog, device)

    def test_running_device_stops_when_a_fresh_reading_reports_cap_drift(self):
        watchdog, source, device = self.make_guard()
        source.reading = sample()
        watchdog.tick(now=100.0)
        watchdog.actions.clear()
        source.reading = sample(101, power_limit_active=250)
        watchdog.tick(now=101)
        self.assert_halted(watchdog, device)
        self.assertIn(("stop", "v100", "power_limit_not_applied", True), watchdog.actions)

    def test_running_device_stops_when_driver_or_required_sensor_becomes_invalid(self):
        cases = {
            "driver failure": {"driver_ok": False},
            "missing HBM": {"temp_mem": None},
            "malformed range": {"power_limit_range": [100, "250"]},
            "missing core": {"temp": None},
            "malformed timestamp": {"ts": float("nan")},
            "future timestamp": {"ts": 102.0},
            "missing provenance": {"simulated": None},
            "wrong device": {"name": "another GPU"},
        }
        for name, changes in cases.items():
            with self.subTest(name=name):
                watchdog, source, device = self.make_guard()
                source.reading = sample()
                watchdog.tick(now=100.0)
                self.assertFalse(device.state["stopped"])
                watchdog.actions.clear()
                source.reading = sample(101.0, **changes)
                watchdog.tick(now=101.0)
                self.assert_halted(watchdog, device)
                self.assertTrue(any(action[0] == "stop" and action[3] for action in watchdog.actions))

    def test_explicit_simulation_cannot_refresh_the_telemetry_timeout(self):
        watchdog, source, device = self.make_guard()
        source.reading = sample()
        watchdog.tick(now=100.0)
        source.reading = sample(101, simulated=True)
        watchdog.tick(now=101.0)
        self.assertFalse(device.state["stopped"])
        self.assertFalse(device.state["power_limit_verified"])
        source.reading = sample(115, simulated=True)
        watchdog.tick(now=115.0)
        self.assertTrue(device.state["stopped"])
        self.assertEqual(device.state["stop_reason"], "telemetry_lost")

    def test_standby_is_reported_and_never_consumed_to_rearm(self):
        watchdog, source, device = self.make_guard(rtx=True)
        self.standby.touch()
        source.reading = sample()
        watchdog.tick(now=100.0)
        self.assert_halted(watchdog, device)
        self.assertTrue(self.standby.exists())
        status = json.loads(self.status.read_text())
        self.assertTrue(status["devices"]["v100"]["standby"])
        self.assertFalse(status["devices"]["rtx5080"]["standby"])
        self.assertFalse(status["devices"]["rtx5080"]["stopped"])
        self.standby.unlink()
        source.reading = sample(101)
        watchdog.tick(now=101)
        self.assertFalse(device.state["stopped"])
        self.assertFalse(json.loads(self.status.read_text())["devices"]["v100"]["standby"])

    def test_status_preserves_sample_provenance_without_inventing_absent_fields(self):
        watchdog, source, _ = self.make_guard(rtx=True)
        source.reading = sample(99.0)
        watchdog.tick(now=100.0)
        status = json.loads(self.status.read_text())
        remote = status["devices"]["v100"]
        local = status["devices"]["rtx5080"]
        self.assertEqual(remote["last_telemetry_ts"], 100.0, "legacy poll timestamp remains unchanged")
        self.assertEqual(remote["reading_full"]["ts"], 99.0)
        self.assertIs(remote["reading_full"]["simulated"], False)
        self.assertNotIn("ts", local["reading_full"])
        self.assertNotIn("simulated", local["reading_full"])
        self.assertTrue(local["power_limit_verified"])

    def test_read_completion_clock_is_used_for_fresh_sample_validation(self):
        watchdog, source, device = self.make_guard()
        source.reading = sample(106.0)
        with patch.object(guard.time, "time", side_effect=[100.0, 106.0, 106.0]):
            watchdog.tick()
        self.assertFalse(device.state["stopped"])
        self.assertEqual(json.loads(self.status.read_text())["ts"], 106.0)

    def test_unreachable_device_keeps_the_existing_telemetry_timeout(self):
        watchdog, source, device = self.make_guard()
        source.reading = sample()
        watchdog.tick(now=100.0)
        source.reading = None
        watchdog.tick(now=101.0)
        self.assertFalse(device.state["stopped"])
        self.assertFalse(device.state["power_limit_verified"])
        watchdog.tick(now=115.0)
        self.assertTrue(device.state["stopped"])
        self.assertEqual(device.state["stop_reason"], "telemetry_lost")


if __name__ == "__main__":
    unittest.main(verbosity=2)
