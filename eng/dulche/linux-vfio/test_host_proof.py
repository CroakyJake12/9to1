"""Host orchestration tests use only synthetic clocks/transports; no GPU calls."""
import copy
import hashlib
import json
import os
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import host_proof as host
from proof_safety import Refusal, STALE_NS, WORK_NS, TOTAL_NS

UUID = 'GPU-11111111-1111-1111-1111-111111111111'
V100 = 'GPU-22222222-2222-2222-2222-222222222222'
PLAN = {'session': 'a' * 32, 'host_gpu_uuid': UUID, 'guest_manifest_sha256': 'b' * 64}
ADMISSION = {'consumed_sha256': 'c' * 64}


class Clock:
    def __init__(self): self.now = 1_000_000_000
    def __call__(self): return self.now
    def sleep(self, seconds): self.now += int(seconds * 1e9)


class Helper:
    def __init__(self, mode='normal'):
        self.messages = []; self.frames = []; self.mode = mode
    def send(self, value):
        assert value['sequence'] == len(self.messages)
        assert type(value['sent_ns']) is int
        if not self.messages and value['action'] != 'stop': assert value['action'] == 'bind'
        self.messages.append(value)
        action = value['action']
        if action == 'bind': self.frames.append({'event': 'bound', 'session': value['session']})
        if action == 'heartbeat' and self.mode != 'silent':
            self.frames.append({'event': 'alive', 'session': value['session']})
        if action == 'stop' and self.mode != 'no_stop':
            self.frames.append({'event': 'stopped', 'session': value['session'], 'qemu_stopped': True,
                                'binding_restored': self.mode != 'bad_restore'})
    def drain(self):
        result = self.frames;self.frames = [];return result


class Guest:
    def __init__(self, clock, mode='normal'):
        self.clock = clock; self.mode = mode; self.messages = [];self.frames = []
        self.rpc = False;self.sample_number = 0
    def send(self, value):
        self.messages.append(value)
        command = value['command']
        if command == 'stop':
            self.frames.append((self.clock(), {'event': 'stopped', 'kind': 'REAL'}));return
        telemetry = None
        if command == 'arm': state = 'ARMED_COLD'
        elif self.mode == 'cold': state = 'COLD_PENDING'
        else:
            self.sample_number += 1
            telemetry = {'uuid': V100, 'bdf': '0000:01:00.0', 'temperature_c': 35,
                         'guest_acquired_ns': self.sample_number * 250_000_000}
            if self.mode == 'start_hot' and command == 'start': telemetry['temperature_c'] = 55
            if self.mode == 'replay' and self.sample_number > 1: telemetry['guest_acquired_ns'] = 250_000_000
            if command == 'start': self.rpc = True;state = 'RPC_STARTING'
            else: state = 'RPC_ALIVE' if self.rpc else 'BASELINE_READY'
        frame = {'schema': host.GUEST_SCHEMA, 'kind': 'REAL',
                 **{key: value[key] for key in ('session', 'sequence', 'nonce')},
                 'state': state, 'telemetry': telemetry, 'rpc_pid': 1234 if self.rpc else None}
        if self.mode == 'wrong_nonce': frame['nonce'] = 'f' * 64
        if self.mode == 'wrong_kind': frame['kind'] = 'CPU_MOCK'
        delay = 800_000_000 if self.mode == 'late' and command == 'poll' else 0
        if self.mode != 'missing' or command == 'arm': self.frames.append((self.clock() + delay, frame))
    def drain(self):
        due = [frame for at, frame in self.frames if at <= self.clock()]
        self.frames = [(at, frame) for at, frame in self.frames if at > self.clock()]
        return due


class Qmp:
    def __init__(self, fail=False): self.called = 0;self.fail = fail
    def attach_async(self): self.called += 1
    def poll(self): return 'failed' if self.fail else 'attached'


class Sensor:
    def __init__(self, mode='normal'): self.now = 0;self.mode = mode;self.stopped = False
    def tick(self, now): self.now = now
    def latest(self):
        value = {'uuid': UUID, 'temperature_c': 45, 'acquired_ns': self.now}
        if self.mode == 'stale': value['acquired_ns'] -= STALE_NS + 1
        if self.mode == 'hot' and self.now > 1_400_000_000: value['temperature_c'] = 75
        return value
    def stop(self): self.stopped = True


class Worker:
    def __init__(self, mode='normal'):
        self.starts = 0;self.heartbeats = 0;self.stopped = True;self.mode = mode
    def prepared(self): return self.mode != 'unprepared'
    def start(self, plan, *, start_ns): self.starts += 1;self.stopped = False;self.start_ns = start_ns
    def heartbeat(self, now): self.heartbeats += 1
    def poll(self):
        if self.mode == 'failure': return {'state': 'failed'}
        if self.mode == 'never': return {'state': 'loading'}
        return {'state': 'completed', 'result': {'content': 'fixture only', 'tokens_predicted': 8}}
    def request_stop(self): self.stopped = self.mode != 'unstoppable'
    def is_stopped(self): return self.stopped


class ControllerTests(unittest.TestCase):
    def setup_fixture(self, *, guest='normal', helper='normal', worker='normal', sensor='normal', qmp_fail=False):
        self.clock = Clock();self.helper = Helper(helper);self.guest = Guest(self.clock, guest)
        self.qmp = Qmp(qmp_fail);self.sensor = Sensor(sensor);self.worker = Worker(worker);self.events = []
        return host.Controller(PLAN, ADMISSION, self.helper, self.guest, self.qmp, self.sensor, self.worker,
                               clock=self.clock, sleep=self.clock.sleep, emit=self.events.append)

    def test_order_one_request_and_independent_stop_messages(self):
        controller = self.setup_fixture();result = controller.run()
        self.assertEqual(self.worker.starts, 1)
        self.assertEqual(self.qmp.called, 1)
        self.assertEqual([v['command'] for v in self.guest.messages].count('arm'), 1)
        self.assertEqual([v['command'] for v in self.guest.messages].count('start'), 1)
        self.assertEqual(self.helper.messages[0]['action'], 'bind')
        self.assertEqual(self.helper.messages[0]['start_ns'], 1_000_000_000)
        self.assertEqual(self.helper.messages[-1]['action'], 'stop')
        self.assertTrue(result['stopped']);self.assertTrue(self.sensor.stopped)
        self.assertEqual(result['proof'], 'NOT_PROVEN')
        self.assertEqual(result['reason'], 'one_completion_returned')
        self.assertLess(result['elapsed_ns'], WORK_NS)

    def test_cold_init_expiry_never_starts_rpc_or_host_backend(self):
        result = self.setup_fixture(guest='cold').run()
        self.assertEqual(result['reason'], 'cold_start_deadline')
        self.assertEqual(self.worker.starts, 0)
        self.assertNotIn('start', [message['command'] for message in self.guest.messages])
        self.assertTrue(result['stopped'])
        self.assertLess(result['elapsed_ns'], 8_100_000_000)

    def test_missing_delayed_wrong_nonce_and_mock_guest_responses_fail_closed(self):
        for mode in ('missing', 'late', 'wrong_nonce', 'wrong_kind', 'replay', 'start_hot'):
            with self.subTest(mode=mode):
                result = self.setup_fixture(guest=mode).run()
                self.assertNotEqual(result['reason'], 'one_completion_returned')
                self.assertTrue(result['stopped'])
                self.assertEqual(self.worker.starts, 0)
                self.assertEqual([v['command'] for v in self.guest.messages].count('arm'), 1)

    def test_qmp_failure_does_not_start_host_worker(self):
        result = self.setup_fixture(qmp_fail=True).run()
        self.assertEqual(result['reason'], 'qmp_attachment_failed')
        self.assertEqual(self.worker.starts, 0)
        self.assertTrue(result['stopped'])

    def test_host_thermal_failure_stops_owned_chain(self):
        result = self.setup_fixture(sensor='hot').run()
        self.assertEqual(result['reason'], 'host_thermal_limit_or_invalid')
        self.assertTrue(result['stopped'])

    def test_no_dispatch_with_stale_host_sample_or_missing_containment(self):
        for values in ({'sensor': 'stale'}, {'worker': 'unprepared'}):
            with self.subTest(values=values), self.assertRaises(Refusal):
                self.setup_fixture(**values).run()
            self.assertEqual(self.helper.messages, [])
            self.assertEqual(self.guest.messages, [])

    def test_helper_lease_failure_stops_guest(self):
        result = self.setup_fixture(helper='silent', worker='never').run()
        self.assertEqual(result['reason'], 'helper_ack_stale')
        self.assertTrue(result['stopped'])

    def test_absolute_work_cutoff_never_restarts_model(self):
        result = self.setup_fixture(worker='never').run()
        self.assertEqual(result['reason'], 'work_deadline')
        self.assertEqual(self.worker.starts, 1)
        self.assertGreaterEqual(result['elapsed_ns'], WORK_NS)
        self.assertLess(result['elapsed_ns'], TOTAL_NS)
        self.assertTrue(result['stopped'])

    def test_unverified_stop_or_restore_cannot_be_success(self):
        for values in ({'worker': 'unstoppable'}, {'helper': 'bad_restore'}, {'helper': 'no_stop'}):
            with self.subTest(values=values):
                result = self.setup_fixture(**values).run()
                self.assertFalse(result['stopped'])
                self.assertEqual(result['proof'], 'NOT_PROVEN')
                self.assertLessEqual(result['elapsed_ns'], TOTAL_NS)


class AdapterTests(unittest.TestCase):
    def test_json_terminal_frame_is_not_lost_when_pipe_closes(self):
        incoming, writer = os.pipe();reader, outgoing = os.pipe()
        process = SimpleNamespace(stdout=os.fdopen(incoming, 'rb', buffering=0),
                                  stdin=os.fdopen(outgoing, 'wb', buffering=0))
        try:
            peer = host.JsonLinePeer(process)
            os.write(writer, b'{"event":"stopped"}\n');os.close(writer);writer = None
            self.assertEqual(peer.drain(), [{'event': 'stopped'}])
            with self.assertRaisesRegex(Refusal, 'eof'): peer.drain()
        finally:
            if writer is not None: os.close(writer)
            os.close(reader);process.stdout.close();process.stdin.close()

    def test_host_sensor_command_can_only_target_the_exact_host_uuid(self):
        with tempfile.TemporaryDirectory() as temp:
            binary = Path(temp) / 'nvidia-smi';binary.write_bytes(b'CPU fixture; never executed')
            pin = {'path': str(binary), 'bytes': binary.stat().st_size,
                   'sha256': hashlib.sha256(binary.read_bytes()).hexdigest()}
            process = SimpleNamespace(poll=lambda: 0, returncode=0,
                                      communicate=lambda **_: ((UUID + ', 42\n').encode(), b''))
            with patch.object(host.subprocess, 'Popen', return_value=process) as spawn:
                sensor = host.Host5080Sensor(UUID, pin);sensor.tick(1_000_000_000);sensor.tick(1_010_000_000)
                command = spawn.call_args.args[0]
                self.assertEqual(command[1:3], ['-i', UUID])
                self.assertEqual(command[3], '--query-gpu=uuid,temperature.gpu')
                self.assertEqual(sensor.latest()['temperature_c'], 42)

    def test_fixed_worker_arguments_do_not_enable_warmup_or_extra_requests(self):
        plan = {'host_executable': {'path': '/pinned/llama-server'},
                'model': {'path': '/canonical/model.gguf'}, 'http_socket': '/private/llama.sock'}
        values = host.fixed_worker_argv(plan)
        self.assertLess(values.index('--rpc'), values.index('--device'))
        self.assertIn('--no-warmup', values)
        self.assertEqual(values[values.index('--predict') + 1], '8')
        self.assertEqual(values[values.index('--fit') + 1], 'off')
        self.assertNotIn('--host', values[values.index('--host') + 2:])


if __name__ == '__main__':
    unittest.main()
