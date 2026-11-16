#!/usr/bin/env python3
"""CPU-only guest lease and challenge/response fixture, never a GPU launcher.

Copy this file and the reviewed proof_safety.py into one private guest directory.
Run with python3 -I -S -B over an authenticated, host-key-pinned SSH channel.
The host sends one challenge every 250 ms and checks the response with
ChallengeVerifier. Its send/receive times must come from the SAME host monotonic
clock. Guest timestamps are checked only for order, never compared with host time.

The fixed synthetic sample is acquired after reading a fresh challenge. Therefore
the host round-trip duration bounds sample age; retaining the HOST SEND time as
the conservative acquisition bound never makes a delayed sample appear newer.
This argument requires trusted reviewed collector code and authenticated transit.
It is not proof of real sensor freshness, guest scheduling, or GPU cessation.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import secrets
import selectors
import struct
import subprocess
import sys
import time

# Isolated Python excludes script-directory imports. Admit only the explicitly
# provisioned directory containing this file and its reviewed sibling module.
sys.path.insert(0, str(Path(__file__).resolve().parent))
import proof_safety as safety

SCHEMA = '9to1.dulche.cpu-guest-challenge/v1'
HEX32 = re.compile(r'^[0-9a-f]{32}$')
HEX64 = re.compile(r'^[0-9a-f]{64}$')
IDENTITIES = {'v100': 'CPU-FIXTURE-V100', 'rtx5080': 'CPU-FIXTURE-5080'}
MAX_FRAME = 4096


def _session(value: object) -> str:
    if not isinstance(value, str) or not HEX32.fullmatch(value):
        raise safety.Refusal('invalid_fixture_session')
    return value


def _json_frame(data: bytes) -> dict:
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise ValueError('duplicate key')
            result[key] = value
        return result
    def nonfinite(value):
        raise ValueError('nonfinite number')
    try:
        if len(data) > MAX_FRAME:
            raise ValueError('oversize frame')
        result = json.loads(data, object_pairs_hook=pairs, parse_constant=nonfinite)
        if not isinstance(result, dict):
            raise ValueError('not an object')
        return result
    except (ValueError, UnicodeError, RecursionError) as error:
        raise safety.Refusal('malformed_challenge_frame') from error


class GuestResponder:
    """Stateful fixed synthetic collector; response is created after validation."""
    def __init__(self, session: str):
        self.session = _session(session)
        self.next_sequence = 0
        self.nonces: set[str] = set()
        self.last_acquired = -1
        self.failed = False

    def respond(self, request: dict, acquired_ns: int, worker_pid: int) -> dict:
        if self.failed:
            raise safety.Refusal('guest_refusal_latched')
        valid = (
            isinstance(request, dict)
            and set(request) == {'schema', 'session', 'sequence', 'nonce', 'fixture_only'}
            and request['schema'] == SCHEMA and request['session'] == self.session
            and request['fixture_only'] is True
            and type(request['sequence']) is int and request['sequence'] == self.next_sequence
            and isinstance(request['nonce'], str) and HEX64.fullmatch(request['nonce'])
            and request['nonce'] not in self.nonces
            and type(acquired_ns) is int and acquired_ns > self.last_acquired
            and type(worker_pid) is int and worker_pid > 0)
        if not valid or self.next_sequence >= 1024:
            self.failed = True
            raise safety.Refusal('invalid_or_replayed_guest_challenge')
        self.nonces.add(request['nonce'])
        self.next_sequence += 1
        self.last_acquired = acquired_ns
        return {
            'schema': SCHEMA, 'session': self.session, 'sequence': request['sequence'],
            'nonce': request['nonce'], 'fixture_only': True, 'gpu_touched': False,
            'guest_acquired_ns': acquired_ns, 'worker_pid': worker_pid,
            'gpus': {'v100': {'uuid': IDENTITIES['v100'], 'temperature_c': 35},
                     'rtx5080': {'uuid': IDENTITIES['rtx5080'], 'temperature_c': 45}},
        }


class ChallengeVerifier:
    """One outstanding challenge; all failure states latch until a new fixture.

    This CPU-fixture type deliberately requires synthetic identities and markers.
    It cannot validate or authorize real GPU telemetry.
    """
    def __init__(self, session: str, start_ns: int):
        self.session = _session(session)
        self.policy = safety.SafetyPolicy(session, start_ns, IDENTITIES)
        self.pending = None
        self.next_sequence = 0
        self.last_guest_acquired = -1
        self.worker_pid = None

    def tick(self, now_ns: int) -> None:
        self.policy.tick(now_ns)

    def issue(self, now_ns: int) -> dict:
        self.tick(now_ns)
        if self.pending is not None:
            self.policy.refuse('challenge_already_pending')
        request = {'schema': SCHEMA, 'session': self.session,
                   'sequence': self.next_sequence, 'nonce': secrets.token_hex(32),
                   'fixture_only': True}
        self.pending = (dict(request), now_ns)
        return request

    def accept(self, response: object, received_ns: int) -> dict:
        self.tick(received_ns)
        if self.pending is None:
            self.policy.refuse('unsolicited_or_replayed_guest_response')
        request, sent_ns = self.pending
        required = {'schema', 'session', 'sequence', 'nonce', 'fixture_only',
                    'gpu_touched', 'guest_acquired_ns', 'worker_pid', 'gpus'}
        if (not isinstance(response, dict) or set(response) != required
                or any(response.get(key) != request[key] for key in (
                    'schema', 'session', 'sequence', 'nonce'))
                or type(response['sequence']) is not int
                or response['fixture_only'] is not True or response['gpu_touched'] is not False):
            self.policy.refuse('guest_response_mismatch')
        acquired = response['guest_acquired_ns']
        pid = response['worker_pid']
        if (type(acquired) is not int or acquired <= self.last_guest_acquired
                or type(pid) is not int or pid <= 0
                or self.worker_pid is not None and self.worker_pid != pid):
            self.policy.refuse('guest_clock_or_worker_identity_changed')
        if not 0 <= received_ns - sent_ns <= safety.STALE_NS:
            self.policy.refuse('guest_round_trip_too_old')
        # send time is an age upper bound, not a claim of synchronized clocks.
        sample = {'session': self.session, 'sequence': request['sequence'],
                  'acquired_ns': sent_ns, 'gpus': response['gpus']}
        self.policy.accept(sample, received_ns)
        self.last_guest_acquired = acquired
        self.worker_pid = pid
        self.next_sequence += 1
        self.pending = None
        return {'fixture_only': True, 'round_trip_ns': received_ns - sent_ns,
                'acquisition_not_before_host_ns': sent_ns, 'worker_pid': pid}


def _write_line(value: dict, deadline_ns: int) -> None:
    """Bound stdout backpressure; a frozen consumer cannot extend the lease."""
    data = (json.dumps(value, separators=(',', ':')) + '\n').encode()
    with selectors.DefaultSelector() as writable:
        writable.register(sys.stdout.fileno(), selectors.EVENT_WRITE)
        while data:
            remaining = (deadline_ns - time.monotonic_ns()) / 1e9
            if remaining <= 0 or not writable.select(min(0.025, remaining)):
                if time.monotonic_ns() >= deadline_ns:
                    raise safety.Refusal('host_output_blocked')
                continue
            try:
                written = os.write(sys.stdout.fileno(), data)
            except BlockingIOError:
                continue
            if written == 0:
                raise safety.Refusal('host_output_closed')
            data = data[written:]


def serve_cpu_fixture(directory: Path, session: str) -> int:
    """Serve challenges and lease one fixed CPU child; never imports GPU code."""
    responder = GuestResponder(session)
    safety.consume_fixture_record(directory, session)
    start = time.monotonic_ns()
    end = start + safety.WORK_NS
    worker = None
    read_fd = write_fd = None
    last_challenge = start
    reason = 'fixture_error'
    result = 2
    os.set_blocking(sys.stdin.fileno(), False)
    os.set_blocking(sys.stdout.fileno(), False)
    try:
        read_fd, write_fd = os.pipe2(os.O_CLOEXEC | os.O_NONBLOCK)
        worker = subprocess.Popen(
            [sys.executable, '-I', '-S', '-B', str(Path(safety.__file__).resolve()),
             '__worker', str(os.getpid()), str(read_fd), str(start)],
            stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
            pass_fds=(read_fd,), start_new_session=True, env=safety._fixture_environment())
        os.close(read_fd)
        read_fd = None
        buffer = b''
        with selectors.DefaultSelector() as readable:
            readable.register(sys.stdin.fileno(), selectors.EVENT_READ)
            while True:
                now = time.monotonic_ns()
                if now >= end:
                    reason, result = 'work_deadline', 0
                    break
                if now - last_challenge > safety.STALE_NS:
                    raise safety.Refusal('host_challenge_lease_expired')
                if worker.poll() is not None:
                    raise safety.Refusal('cpu_worker_exited')
                for _, _ in readable.select(0.025):
                    data = os.read(sys.stdin.fileno(), MAX_FRAME)
                    if not data:
                        raise EOFError
                    buffer += data
                    if len(buffer) > MAX_FRAME:
                        raise safety.Refusal('host_frame_too_large')
                    while b'\n' in buffer:
                        line, buffer = buffer.split(b'\n', 1)
                        request = _json_frame(line)
                        # The only sample source is these fixed synthetic values,
                        # acquired after the current challenge has been received.
                        acquired = time.monotonic_ns()
                        response = responder.respond(request, acquired, worker.pid)
                        last_challenge = acquired
                        if os.write(write_fd, struct.pack('!Q', acquired)) != 8:
                            raise safety.Refusal('cpu_worker_lease_failed')
                        _write_line(response, min(end, acquired + safety.STALE_NS))
    except EOFError:
        reason, result = 'host_eof', 0
    except (safety.Refusal, OSError) as error:
        reason = str(error)
    finally:
        for fd in (read_fd, write_fd):
            if fd is not None:
                os.close(fd)
        stopped = safety._stop_owned(worker, start + safety.TOTAL_NS)
        try:
            _write_line({'event': 'cpu_guest_stopped', 'fixture_only': True,
                         'reason': reason, 'worker_stopped': stopped,
                         'elapsed_ns': time.monotonic_ns() - start},
                        min(start + safety.TOTAL_NS, time.monotonic_ns() + 100_000_000))
        except (OSError, safety.Refusal):
            pass
        if not stopped:
            result = 2
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--serve-cpu-telemetry', action='store_true')
    parser.add_argument('--cpu-fixture-dir', type=Path)
    parser.add_argument('--session')
    args = parser.parse_args()
    try:
        if not args.serve_cpu_telemetry:
            safety.require_real_proof()
        if args.cpu_fixture_dir is None or args.session is None:
            raise safety.Refusal('CPU fixture directory and session are required')
        return serve_cpu_fixture(args.cpu_fixture_dir, args.session)
    except (OSError, safety.Refusal) as error:
        print(str(error), file=sys.stderr)
        return 2


if __name__ == '__main__':
    raise SystemExit(main())
