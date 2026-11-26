import math
import threading
import time
import unittest

from pooled_health import DevicePolicy, evaluate, supervise


POLICIES = (
    DevicePolicy("rtx5080", (("name", "RTX"), ("pci", "0000:01:00.0")), 80, True),
    DevicePolicy("v100", (("name", "V100"),), 68, False, 67, 165),
)


def snapshot(now=None):
    now = time.time() if now is None else now
    return {"ts": now, "halted": False, "devices": {
        "rtx5080": {"stopped": False, "telemetry_ok": True, "last_telemetry_ts": now,
                    "reading_full": {"name": "RTX", "pci": "0000:01:00.0", "temp": 44,
                                     "driver_ok": True, "throttled": False}},
        "v100": {"stopped": False, "telemetry_ok": True, "last_telemetry_ts": now,
                 "reading_full": {"name": "V100", "temp": 52, "temp_mem": 54,
                                  "driver_ok": True, "power_limit_active": 165}},
    }}


class HealthTests(unittest.TestCase):
    def test_only_both_proven_devices_pass(self):
        self.assertTrue(evaluate(snapshot(100), POLICIES, now=100, max_age=5).ok)
        s = snapshot(100)
        del s["devices"]["v100"]
        self.assertIn("v100.missing", evaluate(s, POLICIES, now=100, max_age=5).reasons)

    def test_fresh_file_cannot_hide_stale_rtx_sample(self):
        s = snapshot(100)
        s["devices"]["rtx5080"]["last_telemetry_ts"] = 95
        self.assertIn("rtx5080.stale", evaluate(s, POLICIES, now=100, max_age=5).reasons)

    def test_standby_with_reading_is_not_healthy(self):
        s = snapshot(100)
        s["devices"]["v100"]["last_telemetry_ts"] = None
        self.assertFalse(evaluate(s, POLICIES, now=100, max_age=5).ok)
        s["devices"]["v100"]["last_telemetry_ts"] = 100
        s["devices"]["v100"]["standby"] = True
        self.assertIn("v100.standby", evaluate(s, POLICIES, now=100, max_age=5).reasons)

    def test_invalid_future_and_nonfinite_times_fail(self):
        for value in (True, float("nan"), float("inf"), None, "100", 101):
            with self.subTest(value=value):
                s = snapshot(100)
                s["ts"] = value
                self.assertFalse(evaluate(s, POLICIES, now=100, max_age=5).ok)

    def test_unknown_optional_boolean_cannot_admit_an_inactive_or_simulated_device(self):
        for value in (True, 1, "false", "true", None):
            with self.subTest(field="standby", value=value):
                s = snapshot(100)
                s["devices"]["v100"]["standby"] = value
                self.assertIn("v100.standby", evaluate(s, POLICIES, now=100, max_age=5).reasons)
            with self.subTest(field="simulated", value=value):
                s = snapshot(100)
                s["devices"]["v100"]["reading_full"]["simulated"] = value
                self.assertIn("v100.simulated", evaluate(s, POLICIES, now=100, max_age=5).reasons)
        s = snapshot(100)
        s["devices"]["v100"]["standby"] = False
        s["devices"]["v100"]["reading_full"]["simulated"] = False
        self.assertTrue(evaluate(s, POLICIES, now=100, max_age=5).ok)

    def test_thermal_identity_and_power_evidence_are_required(self):
        for device, field, bad in (("rtx5080", "temp", 80), ("v100", "temp", 68),
                                  ("v100", "temp_mem", 67), ("v100", "temp_mem", None),
                                  ("v100", "temp", math.nan), ("v100", "temp", True),
                                  ("v100", "power_limit_active", 200),
                                  ("rtx5080", "pci", "0000:09:00.0"),
                                  ("rtx5080", "throttled", True),
                                  ("v100", "driver_ok", False)):
            with self.subTest(device=device, field=field, bad=bad):
                s = snapshot(100)
                s["devices"][device]["reading_full"][field] = bad
                self.assertFalse(evaluate(s, POLICIES, now=100, max_age=5).ok)

    def test_invalid_policy_is_not_a_healthy_missing_check(self):
        with self.assertRaises(ValueError):
            evaluate(snapshot(100), POLICIES[:1], now=100, max_age=5)
        with self.assertRaises(ValueError):
            evaluate(snapshot(100), POLICIES, now=100, max_age=float("inf"))


class SupervisionTests(unittest.TestCase):
    def run_guard(self, *, read=None, alive=None, stop=None, verify=None, cancelled=None):
        self.stops = []
        self.verifications = []
        def stopping(key):
            def callback(_timeout):
                self.stops.append(key)
            return callback
        def verifying(key):
            def callback(_timeout):
                self.verifications.append(key)
                return True
            return callback
        return supervise(policies=POLICIES, max_age=5,
                         read_snapshot=read or (lambda _: snapshot()),
                         process_alive=alive or (lambda _: True),
                         stop=stop or {p.key: stopping(p.key) for p in POLICIES},
                         verify_stopped=verify or {p.key: verifying(p.key) for p in POLICIES},
                         cancelled=cancelled or threading.Event(), run_timeout=.08,
                         interval=.005, callback_timeout=.02, cleanup_timeout=.08)

    def test_v100_loss_stops_both_exactly_once(self):
        s = snapshot()
        s["devices"]["v100"]["telemetry_ok"] = False
        out = self.run_guard(read=lambda _: s)
        self.assertEqual(out.trigger, "health_failure")
        self.assertCountEqual(self.stops, ["rtx5080", "v100"])
        self.assertCountEqual(self.verifications, ["rtx5080", "v100"])
        self.assertTrue(out.stop_verified)

    def test_failed_stop_cannot_skip_peer_or_claim_verified(self):
        called = []
        failure = RuntimeError("stop failed")
        def failed(_):
            called.append("rtx5080")
            raise failure
        out = self.run_guard(alive=lambda _: False,
                             stop={"rtx5080": failed, "v100": lambda _: called.append("v100")})
        self.assertCountEqual(called, ["rtx5080", "v100"])
        self.assertIs(out.failures["stop:rtx5080"], failure)
        self.assertFalse(out.stop_verified)

    def test_normal_process_exit_cleans_up_both(self):
        out = self.run_guard(alive=lambda _: False)
        self.assertEqual(out.trigger, "process_exited")
        self.assertCountEqual(self.stops, ["rtx5080", "v100"])
        self.assertTrue(out.stop_verified)

    def test_unproved_verification_cannot_claim_stopped(self):
        out = self.run_guard(alive=lambda _: False,
                             verify={"rtx5080": lambda _: True, "v100": lambda _: None})
        self.assertFalse(out.stop_verified)

    def test_read_failure_preserves_error_and_stops_both(self):
        failure = OSError("status unavailable")
        def read(_):
            raise failure
        out = self.run_guard(read=read)
        self.assertEqual(out.trigger, "callback_failure")
        self.assertIs(out.failures["read"], failure)
        self.assertCountEqual(self.stops, ["rtx5080", "v100"])
        self.assertTrue(out.stop_verified)

    def test_hung_stop_is_bounded_and_peer_still_stops(self):
        release = threading.Event()
        peer = threading.Event()
        started = time.monotonic()
        try:
            out = self.run_guard(alive=lambda _: False,
                                 stop={"rtx5080": lambda _: release.wait(2),
                                       "v100": lambda _: peer.set()})
            self.assertTrue(peer.is_set())
            self.assertLess(time.monotonic() - started, .5)
            self.assertIsInstance(out.failures["stop:rtx5080"], TimeoutError)
            self.assertFalse(out.stop_verified)
        finally:
            release.set()

    def test_cancel_and_deadline_both_cleanup(self):
        event = threading.Event()
        event.set()
        out = self.run_guard(cancelled=event)
        self.assertEqual(out.trigger, "cancelled")
        self.assertCountEqual(self.stops, ["rtx5080", "v100"])
        out = self.run_guard()
        self.assertEqual(out.trigger, "deadline")
        self.assertTrue(out.stop_verified)
        self.assertCountEqual(self.stops, ["rtx5080", "v100"])


if __name__ == "__main__":
    unittest.main()
