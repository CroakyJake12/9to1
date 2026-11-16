"""CPU-only guest protocol and independent lease checks; no SSH or GPU access."""
from __future__ import annotations

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

import guest_fixture as guest
import proof_safety as safety

SESSION = 'a' * 32


class ChallengeTests(unittest.TestCase):
    def test_unrelated_guest_clock_uses_host_send_as_age_bound(self):
        host = guest.ChallengeVerifier(SESSION, 100)
        remote = guest.GuestResponder(SESSION)
        challenge = host.issue(100)
        response = remote.respond(challenge, 987_654_321_000_000, 42)
        result = host.accept(response, 200_000_100)
        self.assertEqual(result['round_trip_ns'], 200_000_000)
        self.assertEqual(host.policy.last_sample_ns, 100)
        self.assertEqual(result['acquisition_not_before_host_ns'], 100)
        self.assertTrue(result['fixture_only'])
        second = host.issue(250_000_100)
        host.accept(remote.respond(second, 987_654_321_000_001, 42), 300_000_100)

    def test_delayed_first_response_cannot_be_restamped_fresh(self):
        host = guest.ChallengeVerifier(SESSION, 0)
        response = guest.GuestResponder(SESSION).respond(host.issue(0), 99, 42)
        with self.assertRaises(safety.Refusal):
            host.accept(response, safety.STALE_NS + 1)
        self.assertIsNotNone(host.policy.stopped)
        with self.assertRaises(safety.Refusal):
            host.accept(response, safety.STALE_NS + 2)

    def test_no_response_and_deadline_fail_closed(self):
        for elapsed in (safety.STALE_NS + 1, safety.WORK_NS):
            host = guest.ChallengeVerifier(SESSION, 0)
            host.issue(0)
            with self.assertRaises(safety.Refusal):
                host.tick(elapsed)

    def test_nonce_sequence_session_and_real_gpu_markers_refused(self):
        mutations = (
            lambda r: r.update(nonce='b' * 64),
            lambda r: r.update(sequence=True),
            lambda r: r.update(sequence=1),
            lambda r: r.update(session='b' * 32),
            lambda r: r.update(fixture_only=False),
            lambda r: r.update(gpu_touched=True),
            lambda r: r.update(worker_pid=0),
            lambda r: r.update(guest_acquired_ns=-1),
            lambda r: r.update(extra='not allowed'),
            lambda r: r['gpus']['v100'].update(uuid='GPU-real-not-a-fixture'),
        )
        for mutate in mutations:
            with self.subTest(mutate=mutate):
                host = guest.ChallengeVerifier(SESSION, 0)
                response = guest.GuestResponder(SESSION).respond(host.issue(0), 99, 42)
                mutate(response)
                with self.assertRaises(safety.Refusal):
                    host.accept(response, 100)
                self.assertIsNotNone(host.policy.stopped)

    def test_replayed_response_and_multiple_inflight_refuse(self):
        host = guest.ChallengeVerifier(SESSION, 0)
        response = guest.GuestResponder(SESSION).respond(host.issue(0), 99, 42)
        host.accept(response, 100)
        with self.assertRaises(safety.Refusal):
            host.accept(response, 200)
        other = guest.ChallengeVerifier(SESSION, 0)
        other.issue(0)
        with self.assertRaises(safety.Refusal):
            other.issue(100)

    def test_guest_restart_or_worker_replacement_refuse(self):
        for field, value in (('guest_acquired_ns', 100), ('worker_pid', 43)):
            host = guest.ChallengeVerifier(SESSION, 0)
            remote = guest.GuestResponder(SESSION)
            host.accept(remote.respond(host.issue(0), 100, 42), 10)
            response = remote.respond(host.issue(250_000_000), 101, 42)
            response[field] = value
            with self.assertRaises(safety.Refusal):
                host.accept(response, 250_000_010)

    def test_guest_rejects_replay_and_latches(self):
        host = guest.ChallengeVerifier(SESSION, 0)
        remote = guest.GuestResponder(SESSION)
        challenge = host.issue(0)
        remote.respond(challenge, 100, 42)
        with self.assertRaises(safety.Refusal):
            remote.respond(challenge, 101, 42)
        newer = dict(challenge, sequence=1, nonce='b' * 64)
        with self.assertRaises(safety.Refusal):
            remote.respond(newer, 102, 42)

    def test_json_duplicates_nonfinite_and_oversize_refuse(self):
        for data in (b'{"x":1,"x":2}', b'{"x":NaN}', b'[]', b'bad', b' ' * 4097):
            with self.subTest(data=data[:20]), self.assertRaises(safety.Refusal):
                guest._json_frame(data)


def process_alive(pid: int) -> bool:
    try:
        status = (Path('/proc') / str(pid) / 'stat').read_text()
        return status[status.rindex(')') + 2] != 'Z'
    except FileNotFoundError:
        return False


class GuestProcessTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='dulche-guest-cpu-')
        self.addCleanup(self.temp.cleanup)
        self.process = None
        self.worker_pid = None
        self.buffer = b''
        self.host = None

    def tearDown(self):
        if self.process is not None:
            if self.process.poll() is None:
                os.kill(self.process.pid, signal.SIGCONT)
                self.process.terminate()
                try:
                    self.process.wait(timeout=1)
                except subprocess.TimeoutExpired:
                    self.process.kill()
                    self.process.wait(timeout=1)
            for stream in (self.process.stdin, self.process.stdout, self.process.stderr):
                if stream is not None and not stream.closed:
                    stream.close()
        if self.worker_pid is not None:
            self.wait_worker_stopped()

    def start(self):
        self.host = guest.ChallengeVerifier(SESSION, time.monotonic_ns())
        self.process = subprocess.Popen(
            [sys.executable, '-I', '-S', '-B', str(Path(guest.__file__).resolve()),
             '--serve-cpu-telemetry', '--cpu-fixture-dir', self.temp.name, '--session', SESSION],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            env=safety._fixture_environment())
        self.exchange()

    def frame(self, timeout=2):
        deadline = time.monotonic() + timeout
        with selectors.DefaultSelector() as selector:
            selector.register(self.process.stdout, selectors.EVENT_READ)
            while b'\n' not in self.buffer:
                remaining = deadline - time.monotonic()
                self.assertGreater(remaining, 0, 'fixture did not return a frame')
                self.assertTrue(selector.select(remaining), 'fixture output timeout')
                chunk = os.read(self.process.stdout.fileno(), 4096)
                self.assertTrue(chunk, 'fixture unexpectedly closed stdout')
                self.buffer += chunk
        line, self.buffer = self.buffer.split(b'\n', 1)
        return json.loads(line)

    def exchange(self):
        challenge = self.host.issue(time.monotonic_ns())
        self.process.stdin.write(json.dumps(challenge).encode() + b'\n')
        self.process.stdin.flush()
        response = self.frame()
        result = self.host.accept(response, time.monotonic_ns())
        self.worker_pid = result['worker_pid']
        return result

    def wait_worker_stopped(self):
        deadline = time.monotonic() + 2
        while process_alive(self.worker_pid) and time.monotonic() < deadline:
            time.sleep(0.01)
        self.assertFalse(process_alive(self.worker_pid), 'fixed guest CPU worker remained alive')

    def test_persistent_round_trips_then_eof_stop(self):
        self.start()
        first = self.worker_pid
        time.sleep(0.25)
        result = self.exchange()
        self.assertEqual(result['worker_pid'], first)
        self.process.stdin.close()
        stopped = self.frame()
        self.assertEqual(stopped['event'], 'cpu_guest_stopped')
        self.assertTrue(stopped['worker_stopped'])
        self.assertEqual(stopped['reason'], 'host_eof')
        self.assertEqual(self.process.wait(timeout=1), 0)

    def test_missing_host_challenges_stop_guest_work(self):
        self.start()
        self.wait_worker_stopped()
        self.assertEqual(self.process.wait(timeout=1), 2)
        stopped = self.frame()
        self.assertTrue(stopped['worker_stopped'])
        self.assertIn(stopped['reason'], ('host_challenge_lease_expired', 'cpu_worker_exited'))

    def test_frozen_guest_supervisor_cannot_keep_worker_alive(self):
        self.start()
        os.kill(self.process.pid, signal.SIGSTOP)
        self.wait_worker_stopped()
        self.assertIsNone(self.process.poll())
        os.kill(self.process.pid, signal.SIGCONT)
        self.assertEqual(self.process.wait(timeout=1), 2)

    def test_killed_guest_supervisor_stops_worker(self):
        self.start()
        self.process.kill()
        self.process.wait(timeout=1)
        self.wait_worker_stopped()

    def test_malformed_input_stops_owned_worker(self):
        self.start()
        self.process.stdin.write(b'not-json\n')
        self.process.stdin.flush()
        self.assertEqual(self.process.wait(timeout=1), 2)
        self.wait_worker_stopped()

    def test_normal_invocation_never_enables_real_execution(self):
        result = subprocess.run([sys.executable, '-I', '-S', '-B', str(Path(guest.__file__).resolve())],
                                capture_output=True, text=True, env=safety._fixture_environment(), timeout=2)
        self.assertEqual(result.returncode, 2)
        self.assertIn('REAL_PROOF_DISABLED', result.stderr)


if __name__ == '__main__':
    unittest.main()
