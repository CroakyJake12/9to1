"""Tests use CPU_MOCK only. Fake pinned bytes are never executed or loaded."""
from __future__ import annotations

import copy
import hashlib
import json
import os
from pathlib import Path
import secrets
import selectors
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock

import guest_runtime as runtime
import proof_safety as safety


def sample(at, temperature=35, uuid='CPU-MOCK-V100'):
    return {'uuid': uuid, 'bdf': '0000:01:00.0', 'temperature_c': temperature,
            'guest_acquired_ns': at}


class FakeNvmlFunction:
    def __init__(self, call):
        self.call = call
        self.calls = 0

    def __call__(self, *args):
        self.calls += 1
        return self.call(*args)


class FakeNvmlLibrary:
    """Function signatures are assigned by ctypes wrapper; no real library load."""
    def __init__(self, init=(0,), handle=(0,), temperature=(0,), shutdown=0):
        self.init_codes, self.handle_codes, self.temperature_codes = list(init), list(handle), list(temperature)
        def next_code(codes):
            return codes.pop(0) if len(codes) > 1 else codes[0]
        def get_handle(_bdf, target):
            code = next_code(self.handle_codes)
            if not code: target._obj.value = 123
            return code
        def get_uuid(_handle, target, _length):
            target.value = b'GPU-11111111-1111-1111-1111-111111111111'
            return 0
        def get_temperature(_handle, _sensor, target):
            code = next_code(self.temperature_codes)
            if not code: target._obj.value = 35
            return code
        self.nvmlInit_v2 = FakeNvmlFunction(lambda: next_code(self.init_codes))
        self.nvmlShutdown = FakeNvmlFunction(lambda: shutdown)
        self.nvmlDeviceGetHandleByPciBusId_v2 = FakeNvmlFunction(get_handle)
        self.nvmlDeviceGetUUID = FakeNvmlFunction(get_uuid)
        self.nvmlDeviceGetTemperature = FakeNvmlFunction(get_temperature)


class NvmlReadinessTests(unittest.TestCase):
    def sensor(self, library):
        with mock.patch.object(runtime.ctypes, 'CDLL', return_value=library):
            return runtime.NvmlTemperature('/FAKE-NVML-NEVER-LOAD.so')

    def test_driver_not_loaded_before_init_success_does_not_increment_refcount(self):
        lib = FakeNvmlLibrary(init=(9, 0)); sensor = self.sensor(lib)
        with self.assertRaises(runtime.NvmlColdPending): sensor.connect('0000:01:00.0')
        self.assertEqual(lib.nvmlShutdown.calls, 0)
        sensor.connect('0000:01:00.0')
        self.assertEqual(sensor.sample()['temperature_c'], 35)
        sensor.close(); self.assertEqual(lib.nvmlShutdown.calls, 1)

    def test_handle_not_found_balances_successful_init_before_retry(self):
        lib = FakeNvmlLibrary(handle=(6, 0)); sensor = self.sensor(lib)
        with self.assertRaises(runtime.NvmlColdPending): sensor.connect('0000:01:00.0')
        self.assertEqual(lib.nvmlShutdown.calls, 1)
        self.assertFalse(sensor.initialized)
        sensor.connect('0000:01:00.0'); sensor.close()
        self.assertEqual(lib.nvmlInit_v2.calls, 2)
        self.assertEqual(lib.nvmlShutdown.calls, 2)

    def test_status_is_retryable_only_for_its_documented_initialization_call(self):
        for library in (FakeNvmlLibrary(init=(6,)), FakeNvmlLibrary(handle=(9,)),
                        FakeNvmlLibrary(init=(4,)), FakeNvmlLibrary(handle=(15,))):
            with self.subTest(library=library):
                with self.assertRaises(runtime.Refusal) as failure:
                    self.sensor(library).connect('0000:01:00.0')
                self.assertNotIsInstance(failure.exception, runtime.NvmlColdPending)

    def test_shutdown_failure_prevents_another_initialization_attempt(self):
        lib = FakeNvmlLibrary(handle=(6,), shutdown=999); sensor = self.sensor(lib)
        with self.assertRaisesRegex(runtime.Refusal, 'nvml_error_999'):
            sensor.connect('0000:01:00.0')
        self.assertEqual(lib.nvmlInit_v2.calls, 1)
        self.assertEqual(lib.nvmlShutdown.calls, 1)

    def test_after_first_sample_any_nvml_error_is_fatal_without_retry(self):
        lib = FakeNvmlLibrary(temperature=(0, 9)); sensor = self.sensor(lib)
        sensor.connect('0000:01:00.0'); sensor.sample()
        with self.assertRaisesRegex(runtime.Refusal, 'nvml_error_9'): sensor.sample()
        self.assertEqual(lib.nvmlInit_v2.calls, 1)
        self.assertEqual(lib.nvmlDeviceGetHandleByPciBusId_v2.calls, 1)
        sensor.close()

    def test_waits_for_driver_before_one_library_wrapper_and_never_restarts_clock(self):
        clock = [0]; ready_calls = []
        def sleep(seconds): clock[0] += int(seconds * 1e9)
        def ready(_):
            ready_calls.append(clock[0]); return len(ready_calls) > 1
        lib = FakeNvmlLibrary(init=(9, 0))
        with mock.patch.object(runtime.ctypes, 'CDLL', return_value=lib) as load:
            sensor = runtime.wait_for_nvml('/FAKE.so', 0, clock=lambda: clock[0], sleep=sleep,
                discover=lambda: '0000:01:00.0', ready=ready)
        self.assertEqual(load.call_count, 1)
        self.assertEqual(clock[0], 500_000_000)
        self.assertEqual(lib.nvmlInit_v2.calls, 2)
        sensor.close()

    def test_repeated_transient_status_stops_at_same_eight_second_deadline(self):
        clock = [0]
        def sleep(seconds): clock[0] += int(seconds * 1e9)
        lib = FakeNvmlLibrary(init=(9,))
        with mock.patch.object(runtime.ctypes, 'CDLL', return_value=lib) as load:
            with self.assertRaisesRegex(runtime.Refusal, 'cold_initialization_deadline'):
                runtime.wait_for_nvml('/FAKE.so', 0, clock=lambda: clock[0], sleep=sleep,
                    discover=lambda: '0000:01:00.0', ready=lambda _: True)
        self.assertEqual(load.call_count, 1)
        self.assertEqual(clock[0], runtime.COLD_NS)
        self.assertEqual(lib.nvmlInit_v2.calls, 32)
        self.assertEqual(lib.nvmlShutdown.calls, 0)

    def test_successful_connect_returning_after_deadline_is_closed_and_refused(self):
        clock = [0]; lib = FakeNvmlLibrary()
        def late_init(): clock[0] = runtime.COLD_NS; return 0
        lib.nvmlInit_v2 = FakeNvmlFunction(late_init)
        with mock.patch.object(runtime.ctypes, 'CDLL', return_value=lib):
            with self.assertRaisesRegex(runtime.Refusal, 'cold_initialization_deadline'):
                runtime.wait_for_nvml('/FAKE.so', 0, clock=lambda: clock[0], sleep=lambda _: None,
                    discover=lambda: '0000:01:00.0', ready=lambda _: True)
        self.assertEqual(lib.nvmlShutdown.calls, 1)

    def test_driver_and_proc_registration_are_required_before_nvml(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); pci = root/'pci'; proc = root/'proc'
            bdf = '0000:01:00.0'; device = pci/bdf; device.mkdir(parents=True)
            self.assertFalse(runtime.v100_driver_ready(bdf, pci, proc))
            driver = root/'nvidia'; driver.mkdir(); (device/'driver').symlink_to(driver)
            self.assertFalse(runtime.v100_driver_ready(bdf, pci, proc))
            (proc/bdf).mkdir(parents=True); (proc/bdf/'information').write_text('CPU fixture')
            self.assertTrue(runtime.v100_driver_ready(bdf, pci, proc))
            (device/'driver').unlink(); other = root/'nouveau'; other.mkdir()
            (device/'driver').symlink_to(other)
            with self.assertRaisesRegex(runtime.Refusal, 'unexpected_guest_gpu_driver'):
                runtime.v100_driver_ready(bdf, pci, proc)


class PolicyTests(unittest.TestCase):
    def test_cold_grace_requires_host_lease_and_stops_at_eight_seconds(self):
        policy = runtime.Policy(0, 'CPU_MOCK')
        for tick in range(0, runtime.COLD_NS, 250_000_000):
            policy.host(tick)
        with self.assertRaisesRegex(runtime.Refusal, 'cold_initialization_deadline'):
            policy.tick(runtime.COLD_NS)
        other = runtime.Policy(0, 'CPU_MOCK')
        with self.assertRaisesRegex(runtime.Refusal, 'host_lease_expired'):
            other.tick(runtime.STALE_NS + 1)

    def test_first_sample_does_not_restart_total_work_clock(self):
        policy = runtime.Policy(0, 'CPU_MOCK')
        for tick in range(0, 7_500_000_001, 250_000_000):
            policy.host(tick)
        policy.sample(sample(7_500_000_000), 7_500_000_000)
        for tick in range(7_750_000_000, runtime.WORK_NS, 250_000_000):
            policy.host(tick)
            policy.sample(sample(tick), tick)
        with self.assertRaisesRegex(runtime.Refusal, 'work_deadline'):
            policy.tick(runtime.WORK_NS)

    def test_after_first_sample_eight_second_grace_is_gone(self):
        policy = runtime.Policy(0, 'CPU_MOCK')
        policy.sample(sample(0), 0)
        for tick in (250_000_000, 500_000_000, 750_000_000):
            policy.host(tick)
        with self.assertRaisesRegex(runtime.Refusal, 'telemetry_stale'):
            policy.tick(750_000_001)

    def test_start_temperature_absolute_relative_and_identity_checks(self):
        for temp in (40.01, 55, float('nan'), True, -1):
            with self.subTest(temp=temp), self.assertRaises(runtime.Refusal):
                runtime.Policy(0, 'CPU_MOCK').sample(sample(0, temp), 0)
        for temp in (45, 55):
            policy = runtime.Policy(0, 'CPU_MOCK')
            policy.sample(sample(0), 0)
            with self.assertRaises(runtime.Refusal):
                policy.sample(sample(1, temp), 1)
        policy = runtime.Policy(0, 'REAL')
        with self.assertRaises(runtime.Refusal):
            policy.sample(sample(0), 0)

    def test_real_identity_freezes_after_first_sample(self):
        a = 'GPU-11111111-1111-1111-1111-111111111111'
        b = 'GPU-22222222-2222-2222-2222-222222222222'
        policy = runtime.Policy(0, 'REAL')
        policy.sample(sample(0, uuid=a), 0)
        with self.assertRaisesRegex(runtime.Refusal, 'gpu_identity_changed'):
            policy.sample(sample(1, uuid=b), 1)


class ManifestTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='dulche-fake-manifest-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.lib = self.root / 'lib'
        self.lib.mkdir()
        def record(path, data):
            path.write_bytes(data)
            return {'path': str(path), 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()}
        rpc = record(self.root / 'ggml-rpc-server', b'NOT AN EXECUTABLE; MUST NEVER RUN')
        os.chmod(rpc['path'], 0o700)
        nvml = record(self.root / 'libnvidia-ml.so.1', b'NOT A LIBRARY; MUST NEVER LOAD')
        library = record(self.lib / 'libggml.so.1', b'FAKE BYTES ONLY')
        (self.lib / 'libggml.so').symlink_to('libggml.so.1')
        self.manifest = {'schema': 'dulche.guest-runtime/v1', 'proof_id': runtime.PROOF_ID,
                         'backend_commit': runtime.COMMIT, 'rpc_executable': rpc,
                         'library_directories': [str(self.lib)], 'pinned_libraries': [library],
                         'nvml_library': nvml}
        self.receipt = {'schema': 'dulche.v100-consumed/v1', 'proof_id': runtime.PROOF_ID,
                        'consumed': True, 'manifest_sha256': 'a' * 64}

    def test_hash_verification_is_metadata_only_and_catches_changes(self):
        config = runtime.verify_manifest(self.manifest, self.receipt, 'a' * 64)
        runtime.check_stats(config)
        Path(self.manifest['rpc_executable']['path']).write_bytes(b'changed')
        with self.assertRaises(runtime.Refusal):
            runtime.check_stats(config)
        with self.assertRaises(runtime.Refusal):
            runtime.verify_manifest(self.manifest, self.receipt, 'a' * 64)

    def test_unconsumed_receipt_wrong_manifest_and_extra_command_refuse(self):
        for field, value in (('consumed', False), ('manifest_sha256', 'b'*64), ('proof_id', 'another')):
            with self.subTest(field=field), self.assertRaises(runtime.Refusal):
                runtime.verify_manifest(self.manifest, dict(self.receipt, **{field:value}), 'a'*64)
        with self.assertRaises(runtime.Refusal):
            runtime.verify_manifest(dict(self.manifest, command=['anything']), self.receipt, 'a'*64)

    def test_unpinned_or_changed_alias_refuses(self):
        config = runtime.verify_manifest(self.manifest, self.receipt, 'a'*64)
        alias = self.lib / 'libggml.so'
        alias.unlink()
        alias.symlink_to(self.manifest['nvml_library']['path'])
        with self.assertRaises(runtime.Refusal):
            runtime.check_stats(config)
        (self.lib / 'libunreviewed.so').write_bytes(b'unreviewed')
        with self.assertRaises(runtime.Refusal):
            runtime.verify_manifest(self.manifest, self.receipt, 'a'*64)

    def test_sysfs_pci_discovery_rejects_extra_nvidia(self):
        root = self.root / 'pci'
        root.mkdir()
        self.assertIsNone(runtime.v100_bdf(root))
        gpu = root / '0000:01:00.0'
        gpu.mkdir()
        for key, value in {'vendor':'0x10de','device':'0x1db4','subsystem_vendor':'0x10de','subsystem_device':'0x1214'}.items():
            (gpu / key).write_text(value)
        self.assertEqual(runtime.v100_bdf(root), gpu.name)
        extra = root / '0000:02:00.0'
        extra.mkdir()
        (extra/'vendor').write_text('0x10de')
        with self.assertRaises(runtime.Refusal):
            runtime.v100_bdf(root)


def alive(pid):
    if pid is None:
        return False
    try:
        content = (Path('/proc') / str(pid) / 'stat').read_text()
        return content[content.rindex(')')+2] != 'Z'
    except (FileNotFoundError, ProcessLookupError):
        return False


class RuntimeProcessTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='dulche-runtime-mock-')
        self.addCleanup(self.temp.cleanup)
        self.state = Path(self.temp.name)
        self.process = None
        self.buffer = b''
        self.sequence = 0
        self.session = secrets.token_hex(16)
        self.child_pids = set()
        self.rpc_pid = self.guardian_pid = None

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
                try:
                    stream.close()
                except BrokenPipeError:
                    pass
        deadline = time.monotonic()+2
        while any(alive(pid) for pid in self.child_pids) and time.monotonic()<deadline:
            time.sleep(.01)
        self.assertFalse([pid for pid in self.child_pids if alive(pid)], 'Owned CPU mock child survived')

    def frame(self, timeout=2):
        deadline = time.monotonic()+timeout
        with selectors.DefaultSelector() as selected:
            selected.register(self.process.stdout, selectors.EVENT_READ)
            while b'\n' not in self.buffer:
                remaining = deadline-time.monotonic()
                self.assertGreater(remaining,0)
                self.assertTrue(selected.select(remaining), 'runtime output timed out')
                data = os.read(self.process.stdout.fileno(),8192)
                if not data:
                    self.fail('runtime output closed before the expected frame')
                self.buffer += data
        line,self.buffer=self.buffer.split(b'\n',1)
        return json.loads(line)

    def launch(self, scenario='normal'):
        self.process = subprocess.Popen([sys.executable,'-I','-S','-B',str(Path(runtime.__file__).resolve()),
                    '--state-dir',str(self.state),'--cpu-mock','--mock-scenario',scenario],
                    stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,
                    env=safety._fixture_environment())
        ready=self.frame()
        self.assertEqual(ready['event'],'ready')
        self.assertEqual(ready['kind'],'CPU_MOCK')
        self.command('arm')

    def command(self, name):
        command={'schema':runtime.CONTROL_SCHEMA,'session':self.session,'sequence':self.sequence,
                 'nonce':secrets.token_hex(32),'command':name}
        self.sequence+=1
        sent=time.monotonic_ns()
        self.process.stdin.write(json.dumps(command).encode()+b'\n');self.process.stdin.flush()
        response=self.frame()
        if response.get('event')=='stopped':
            return response
        self.assertEqual(response['kind'],'CPU_MOCK')
        for key in ('session','sequence','nonce'):
            self.assertEqual(response[key],command[key])
        self.assertLessEqual(time.monotonic_ns()-sent,runtime.STALE_NS)
        for field in ('rpc_pid','collector_pid','guardian_pid'):
            if response[field]:self.child_pids.add(response[field])
        self.rpc_pid=response['rpc_pid'] or self.rpc_pid
        self.guardian_pid=response['guardian_pid'] or self.guardian_pid
        return response

    def baseline(self):
        for _ in range(10):
            response=self.command('poll')
            if response.get('telemetry') is not None:return response
            time.sleep(.05)
        self.fail('CPU collector did not become ready')

    def start_rpc(self):
        self.baseline()
        self.assertEqual(self.command('start')['state'],'RPC_STARTING')
        for _ in range(10):
            time.sleep(.03)
            response=self.command('poll')
            if response.get('rpc_pid') is not None:return response
        self.fail('CPU RPC mock did not start')

    def wait_dead(self, pid, timeout=2):
        deadline=time.monotonic()+timeout
        while alive(pid) and time.monotonic()<deadline:time.sleep(.01)
        self.assertFalse(alive(pid))

    def test_normal_baseline_start_poll_and_stop_reap_owned_children(self):
        self.launch();self.start_rpc()
        time.sleep(.25);self.command('poll')
        self.assertEqual(self.command('stop')['state'],'STOPPING')
        terminal=self.frame()
        self.assertEqual(terminal['event'],'stopped')
        self.assertTrue(terminal['guardian_reaped']);self.assertTrue(terminal['collector_reaped'])
        self.assertEqual(self.process.wait(timeout=2),0)
        rpc_terminal=json.loads((self.state/'rpc-terminal.json').read_text())
        self.assertTrue(rpc_terminal['rpc_reaped'])
        self.assertEqual((self.state/'rpc.log').read_bytes(),b'')

    def test_host_silence_stops_rpc(self):
        self.launch();self.start_rpc()
        self.wait_dead(self.rpc_pid)
        self.assertEqual(self.process.wait(timeout=2),2)

    def test_frozen_main_supervisor_does_not_extend_rpc_lease(self):
        self.launch();self.start_rpc()
        os.kill(self.process.pid,signal.SIGSTOP)
        self.wait_dead(self.rpc_pid)
        os.kill(self.process.pid,signal.SIGCONT)
        self.assertEqual(self.process.wait(timeout=2),2)

    def test_killed_main_supervisor_lets_guardian_reap_rpc(self):
        self.launch();self.start_rpc()
        self.process.kill();self.process.wait(timeout=1)
        self.wait_dead(self.rpc_pid)
        self.wait_dead(self.guardian_pid)
        self.assertTrue(json.loads((self.state/'rpc-terminal.json').read_text())['rpc_reaped'])

    def test_killed_guardian_triggers_rpc_parent_death(self):
        self.launch();self.start_rpc()
        os.kill(self.guardian_pid,signal.SIGKILL)
        self.wait_dead(self.rpc_pid)
        self.assertEqual(self.process.wait(timeout=2),2)

    def test_frozen_guardian_is_detected_while_host_keeps_polling(self):
        self.launch();self.start_rpc()
        os.kill(self.guardian_pid,signal.SIGSTOP)
        terminal=None
        for _ in range(15):
            time.sleep(.1)
            try:
                response=self.command('poll')
            except BrokenPipeError:
                response=self.frame()
            if response.get('event')=='stopped':
                terminal=response
                break
        self.assertIsNotNone(terminal)
        self.assertEqual(terminal['reason'],'guardian_ack_stale')
        self.wait_dead(self.rpc_pid)
        self.assertEqual(self.process.wait(timeout=2),2)

    def test_real_eight_second_cold_timeout_with_host_lease_preserved(self):
        started=time.monotonic()
        self.launch('cold_timeout')
        terminal=None
        while time.monotonic()-started<10:
            time.sleep(.2)
            try:
                response=self.command('poll')
            except BrokenPipeError:
                response=self.frame()
            if response.get('event')=='stopped':
                terminal=response
                break
            self.assertEqual(response['state'],'COLD_PENDING')
            self.assertIsNone(response['telemetry'])
        self.assertIsNotNone(terminal)
        self.assertEqual(terminal['reason'],'cold_initialization_deadline')
        self.assertGreaterEqual(terminal['elapsed_ns'],runtime.COLD_NS)
        self.assertLess(terminal['elapsed_ns'],9_000_000_000)
        self.assertEqual(self.process.wait(timeout=2),2)
        self.assertFalse((self.state/'rpc.log').exists())

    def test_second_start_is_refused_and_owned_rpc_stops(self):
        self.launch();self.start_rpc()
        response=self.command('start')
        self.assertEqual(response['event'],'stopped')
        self.assertEqual(response['reason'],'start_requires_baseline_and_no_prior_rpc')
        self.wait_dead(self.rpc_pid)
        self.assertEqual(self.process.wait(timeout=2),2)

    def test_hung_temperature_collector_stops_instead_of_blocking_controller(self):
        self.launch('telemetry_hang');self.baseline()
        response=self.command('start')
        self.assertEqual(response['event'],'stopped')
        self.assertEqual(self.process.wait(timeout=2),2)
        self.assertFalse((self.state/'rpc.log').exists())

    def test_hot_sample_refuses_start(self):
        self.launch('hot');self.baseline()
        response=self.command('start')
        self.assertEqual(response['event'],'stopped')
        self.assertIn('thermal',response['reason'])
        self.assertEqual(self.process.wait(timeout=2),2)
        self.assertFalse((self.state/'rpc.log').exists())

    def test_start_without_prior_baseline_refuses(self):
        self.launch()
        response=self.command('start')
        self.assertEqual(response['event'],'stopped')
        self.assertEqual(self.process.wait(timeout=2),2)

    def test_real_mode_without_receipts_refuses_before_any_child(self):
        result=subprocess.run([sys.executable,'-I','-S','-B',str(Path(runtime.__file__).resolve()),
                               '--state-dir',str(self.state)],capture_output=True,text=True,
                              env=safety._fixture_environment(),timeout=2)
        self.assertEqual(result.returncode,2)
        self.assertIn('real_manifest_and_consumed_receipt_required',result.stderr)
        self.assertFalse((self.state/'guest-invocation.json').exists())


if __name__=='__main__':
    unittest.main()
