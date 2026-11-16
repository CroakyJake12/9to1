import concurrent.futures
import copy
import json
import os
from pathlib import Path
import selectors
import signal
import subprocess
import sys
import tempfile
import time
import unittest

import proof_safety as safety


class PolicyTests(unittest.TestCase):
    def setUp(self):
        self.policy = safety.SafetyPolicy("fixture", 1_000_000_000, {"v100": "v", "rtx5080": "r"})
        self.sample = {"session": "fixture", "sequence": 0, "acquired_ns": 1_010_000_000,
                       "gpus": {"v100": {"uuid": "v", "temperature_c": 35},
                                "rtx5080": {"uuid": "r", "temperature_c": 45}}}

    def test_real_proof_unconditionally_disabled(self):
        with self.assertRaisesRegex(safety.Refusal, "REAL_PROOF_DISABLED"):
            safety.require_real_proof()

    def test_start_temperature_boundary(self):
        self.sample["gpus"]["v100"]["temperature_c"] = 40
        self.policy.accept(self.sample, self.sample["acquired_ns"])
        self.assertEqual(self.policy.initial_v100, 40)

    def test_unsafe_start(self):
        self.sample["gpus"]["v100"]["temperature_c"] = 40.01
        with self.assertRaisesRegex(safety.Refusal, "v100_start_too_hot"):
            self.policy.accept(self.sample, self.sample["acquired_ns"])

    def test_both_absolute_and_relative_v100_limits(self):
        for initial, final in [(35, 45), (40, 55)]:
            with self.subTest(initial=initial, final=final):
                self.setUp()
                self.sample["gpus"]["v100"]["temperature_c"] = initial
                self.policy.accept(self.sample, self.sample["acquired_ns"])
                self.sample["sequence"] = 1
                self.sample["acquired_ns"] += safety.SAMPLE_NS
                self.sample["gpus"]["v100"]["temperature_c"] = final
                with self.assertRaisesRegex(safety.Refusal, "v100_thermal_limit"):
                    self.policy.accept(self.sample, self.sample["acquired_ns"])

    def test_host_limit(self):
        self.sample["gpus"]["rtx5080"]["temperature_c"] = 75
        with self.assertRaisesRegex(safety.Refusal, "rtx5080_thermal_limit"):
            self.policy.accept(self.sample, self.sample["acquired_ns"])

    def test_invalid_samples_fail_closed(self):
        mutations = [
            lambda s: s.update(session="wrong"),
            lambda s: s.update(sequence=True),
            lambda s: s.update(sequence=8),
            lambda s: s.update(acquired_ns=True),
            lambda s: s.update(acquired_ns=s["acquired_ns"] + 1),
            lambda s: s.update(acquired_ns=0),
            lambda s: s["gpus"].pop("v100"),
            lambda s: s["gpus"]["v100"].update(uuid="different"),
            lambda s: s["gpus"]["v100"].update(temperature_c=float("nan")),
            lambda s: s["gpus"]["v100"].update(temperature_c=float("inf")),
            lambda s: s["gpus"]["v100"].update(temperature_c=True),
            lambda s: s["gpus"]["v100"].update(temperature_c=-1),
            lambda s: s["gpus"]["v100"].update(temperature_c=10 ** 400),
            lambda s: s.update(extra="untrusted"),
        ]
        for mutation in mutations:
            with self.subTest(mutation=mutation):
                self.setUp()
                mutation(self.sample)
                with self.assertRaises(safety.Refusal):
                    self.policy.accept(self.sample, 1_010_000_000)
                with self.assertRaises(safety.Refusal):
                    self.policy.tick(1_020_000_000)

    def test_replay_and_source_timestamp_replay(self):
        self.policy.accept(self.sample, self.sample["acquired_ns"])
        with self.assertRaisesRegex(safety.Refusal, "replayed_or_missing_sample"):
            self.policy.accept(self.sample, self.sample["acquired_ns"])
        self.setUp()
        self.policy.accept(self.sample, self.sample["acquired_ns"])
        self.sample["sequence"] = 1
        with self.assertRaisesRegex(safety.Refusal, "replayed_sample_time"):
            self.policy.accept(self.sample, self.sample["acquired_ns"] + 1)

    def test_missing_and_stale_telemetry(self):
        with self.assertRaisesRegex(safety.Refusal, "telemetry_stale"):
            self.policy.tick(self.policy.start_ns + safety.STALE_NS + 1)
        self.setUp()
        self.policy.accept(self.sample, self.sample["acquired_ns"])
        self.policy.tick(self.sample["acquired_ns"] + safety.STALE_NS)
        with self.assertRaisesRegex(safety.Refusal, "telemetry_stale"):
            self.policy.tick(self.sample["acquired_ns"] + safety.STALE_NS + 1)

    def test_delayed_frame_cannot_refresh_stale_data(self):
        self.sample["acquired_ns"] = self.policy.start_ns
        with self.assertRaisesRegex(safety.Refusal, "telemetry_stale"):
            self.policy.accept(self.sample, self.policy.start_ns + safety.STALE_NS + 1)

    def test_deadline_never_resets_on_fresh_telemetry(self):
        now = self.sample["acquired_ns"]
        while now - self.policy.start_ns < safety.WORK_NS:
            self.sample["acquired_ns"] = now
            self.policy.accept(copy.deepcopy(self.sample), now)
            self.sample["sequence"] += 1
            now += safety.SAMPLE_NS
        with self.assertRaisesRegex(safety.Refusal, "work_deadline"):
            self.policy.tick(self.policy.start_ns + safety.WORK_NS)

    def test_supervisor_clock_cannot_go_backwards_or_change_type(self):
        for now in (1_000_000_001, True, float('nan')):
            with self.subTest(now=now):
                self.setUp()
                self.policy.tick(1_010_000_000)
                with self.assertRaisesRegex(safety.Refusal, "monotonic_clock_invalid"):
                    self.policy.tick(now)

    def test_initial_clock_and_identities_are_validated(self):
        for start in (-1, True, float('nan')):
            with self.subTest(start=start), self.assertRaises(ValueError):
                safety.SafetyPolicy('fixture', start, {'v100': 'v', 'rtx5080': 'r'})
        with self.assertRaises(ValueError):
            safety.SafetyPolicy('fixture', 0, {'v100': 'same', 'rtx5080': 'same'})


class LedgerTests(unittest.TestCase):
    def test_existing_partial_record_is_never_reused(self):
        with tempfile.TemporaryDirectory(prefix="dulche-cpu-fixture-") as tmp:
            path = Path(tmp) / "CPU-FIXTURE-CONSUMED.json"
            path.write_bytes(b"partial")
            with self.assertRaises(safety.Refusal):
                safety.consume_fixture_record(Path(tmp), "fixture")
            self.assertEqual(path.read_bytes(), b"partial")

    def test_concurrent_consumers_only_one_wins(self):
        with tempfile.TemporaryDirectory(prefix="dulche-cpu-fixture-") as tmp:
            def consume(i):
                try:
                    return safety.consume_fixture_record(Path(tmp), f"fixture-{i}")
                except safety.Refusal:
                    return None
            with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
                results = list(pool.map(consume, range(4)))
            self.assertEqual(sum(x is not None for x in results), 1)
            record = json.loads((Path(tmp) / "CPU-FIXTURE-CONSUMED.json").read_text())
            self.assertFalse(record["real_proof"])
            self.assertTrue(record["consumed_before_dispatch"])

    def test_symlink_ledger_refused(self):
        with tempfile.TemporaryDirectory(prefix="dulche-cpu-fixture-") as tmp:
            path = Path(tmp)
            (path / "link").symlink_to(path, target_is_directory=True)
            with self.assertRaises(safety.Refusal):
                safety.consume_fixture_record(path / "link", "fixture")


class LifecycleTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dulche-cpu-fixture-")
        self.process = None
        self.buffer = b''

    def tearDown(self):
        if self.process is not None:
            if self.process.poll() is None:
                self.process.kill()
            self.process.communicate(timeout=3)
        self.tmp.cleanup()

    def start_fixture(self, scenario="normal", env=None):
        self.process = subprocess.Popen([sys.executable, "-I", "-S", "-B", str(Path(safety.__file__).resolve()),
                                         "--cpu-fixture", self.tmp.name, "--scenario", scenario],
                                        stdout=subprocess.PIPE, stderr=subprocess.PIPE, bufsize=0,
                                        env=safety._fixture_environment() if env is None else env)
        # Read raw bytes: buffered readline plus select can hide a ready frame.
        events = []
        self.buffer = b''
        deadline = time.monotonic() + 3
        with selectors.DefaultSelector() as selector:
            selector.register(self.process.stdout, selectors.EVENT_READ)
            while len(events) < 2:
                while b'\n' in self.buffer and len(events) < 2:
                    line, self.buffer = self.buffer.split(b'\n', 1)
                    events.append(json.loads(line))
                if len(events) == 2:
                    break
                remaining = max(0, deadline - time.monotonic())
                self.assertTrue(selector.select(timeout=remaining), "fixture did not become ready")
                data = os.read(self.process.stdout.fileno(), 4096)
                self.assertTrue(data, "fixture exited before readiness")
                self.buffer += data
        self.assertEqual([x["event"] for x in events], ["primary", "worker"])
        return events[0]["pid"], events[1]["pid"]

    def assert_stops(self, reason, timeout=3):
        stdout, stderr = self.process.communicate(timeout=timeout)
        self.assertEqual(self.process.returncode, 0, stderr)
        result = json.loads((self.buffer + stdout).strip().splitlines()[-1])
        self.assertEqual(result["reason"], reason)
        self.assertTrue(result["stopped"])
        self.assertTrue(result["within_total_deadline"])
        self.assertFalse(result["real_proof"])
        return result

    def test_real_25_second_work_deadline(self):
        self.start_fixture()
        result = self.assert_stops('work_deadline', timeout=29)
        self.assertGreaterEqual(result['elapsed_ns'], safety.WORK_NS)
        self.assertLess(result['elapsed_ns'], safety.TOTAL_NS)

    def test_primary_sigkill_stops_owned_worker(self):
        primary, worker = self.start_fixture()
        with_pid = os.pidfd_open(primary)
        try:
            signal.pidfd_send_signal(with_pid, signal.SIGKILL)
        finally:
            os.close(with_pid)
        self.assert_stops("primary_guard_lost")
        self.assertFalse(Path(f"/proc/{worker}").exists())

    def test_worker_sigkill_is_detected(self):
        _, worker = self.start_fixture()
        fd = os.pidfd_open(worker)
        try:
            signal.pidfd_send_signal(fd, signal.SIGKILL)
        finally:
            os.close(fd)
        self.assert_stops("worker_exited")

    def test_watchdog_sigkill_triggers_child_parent_death(self):
        primary, worker = self.start_fixture()
        fds = [os.pidfd_open(pid) for pid in (primary, worker)]
        try:
            self.process.kill()
            self.process.communicate(timeout=3)
            with selectors.DefaultSelector() as selector:
                for fd in fds:
                    selector.register(fd, selectors.EVENT_READ)
                exited = set()
                until = time.monotonic() + 2
                while len(exited) < 2 and time.monotonic() < until:
                    for key, _ in selector.select(timeout=0.05):
                        exited.add(key.fd)
                self.assertEqual(exited, set(fds))
        finally:
            for fd in fds:
                os.close(fd)

    def test_frozen_watchdog_cannot_keep_worker_running(self):
        _, worker = self.start_fixture()
        fd = os.pidfd_open(worker)
        try:
            self.process.send_signal(signal.SIGSTOP)
            with selectors.DefaultSelector() as selector:
                selector.register(fd, selectors.EVENT_READ)
                self.assertTrue(selector.select(timeout=1.5), 'worker ignored its heartbeat deadline')
        finally:
            self.process.send_signal(signal.SIGCONT)
            os.close(fd)
        # Parent resumes only to reap its fixed children and retain its receipt.
        stdout, stderr = self.process.communicate(timeout=3)
        self.assertEqual(self.process.returncode, 0, stderr)
        result = json.loads((self.buffer + stdout).strip().splitlines()[-1])
        self.assertTrue(result['stopped'])
        self.assertIn(result['reason'], ('telemetry_stale', 'worker_exited'))

    def test_live_fail_closed_telemetry_scenarios(self):
        for scenario, reason in [("hot", "v100_thermal_limit"), ("malformed", "malformed_telemetry"),
                                 ("replay", "replayed_or_missing_sample"), ("silence", "telemetry_stale")]:
            with self.subTest(scenario=scenario):
                self.start_fixture(scenario)
                self.assert_stops(reason)
                # Every test run gets a new, explicitly CPU-only authority directory.
                self.process = None
                self.tmp.cleanup()
                self.tmp = tempfile.TemporaryDirectory(prefix="dulche-cpu-fixture-")

    def test_failed_fixture_cannot_retry_its_consumed_record(self):
        self.start_fixture('hot')
        self.assert_stops('v100_thermal_limit')
        record = Path(self.tmp.name) / 'CPU-FIXTURE-CONSUMED.json'
        before = record.read_bytes()
        attempt = subprocess.run([sys.executable, '-I', '-S', '-B', str(Path(safety.__file__).resolve()),
                                  '--cpu-fixture', self.tmp.name, '--scenario', 'hot'],
                                 capture_output=True, timeout=3, env=safety._fixture_environment())
        self.assertEqual(attempt.returncode, 2)
        self.assertEqual(attempt.stdout, b'')
        self.assertIn(b'already_consumed', attempt.stderr)
        self.assertEqual(record.read_bytes(), before)

    def test_inherited_python_startup_hook_cannot_execute(self):
        root = Path(self.tmp.name)
        hook = root / 'hook'
        hook.mkdir()
        marker = root / 'unexpected-startup-execution'
        (hook / 'sitecustomize.py').write_text(
            f"from pathlib import Path\nPath({str(marker)!r}).write_text('unexpected')\n")
        poison = dict(os.environ, PYTHONPATH=str(hook), PYTHONSTARTUP=str(hook / 'sitecustomize.py'))
        self.start_fixture('hot', env=poison)
        self.assert_stops('v100_thermal_limit')
        self.assertFalse(marker.exists())


if __name__ == "__main__":
    unittest.main()
