#!/usr/bin/env python3
"""Bounded guest control over trusted, host-key-pinned SSH stdio.

REAL mode requires exact manifests and a consumed global receipt. Only the root
coordinator may arm it. CPU_MOCK never imports NVML or starts the backend.
The host starts its own deadline BEFORE sending arm and before device binding.
Guest monotonic timestamps must never be compared directly with host timestamps.
Poll nonces bind samples acquired after the corresponding request; the host must
check its own round-trip bound. A stopped process is not proof of GPU quiescence.
"""
from __future__ import annotations

import argparse
import ctypes
import hashlib
import json
import math
import os
from pathlib import Path
import re
import resource
import selectors
import signal
import socket
import stat
import subprocess
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parent))
import proof_safety as safety

PROOF_ID = 'v100-tinyllama-one-proof-20261116'
COMMIT = 'b1b770d90d94cad9e9ea5b993bdbc383a522fb28'
CONTROL_SCHEMA = 'dulche.guest-control/v1'
COLD_NS = 8_000_000_000
WORK_NS = 25_000_000_000
TOTAL_NS = 30_000_000_000
STALE_NS = 750_000_000
HEX = re.compile(r'^[0-9a-f]{64}$')
UUID = re.compile(r'^GPU-[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$')
BDF = re.compile(r'^[0-9a-f]{4}:[0-9a-f]{2}:[0-9a-f]{2}\.[0-7]$')
Refusal = safety.Refusal


class StopRequested(Exception):
    pass


class NvmlColdPending(Refusal):
    """Only a documented initialization-stage readiness status, never inference."""


def decode(data: bytes) -> dict:
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise ValueError('duplicate key')
            result[key] = value
        return result
    def bad_number(_):
        raise ValueError('nonfinite JSON')
    try:
        if len(data) > 65536:
            raise ValueError('oversize packet')
        result = json.loads(data, object_pairs_hook=pairs, parse_constant=bad_number)
        if not isinstance(result, dict):
            raise ValueError('object required')
        return result
    except (ValueError, UnicodeError, RecursionError) as error:
        raise Refusal('invalid_json_packet') from error


def pinned_json(path: Path, expected: str) -> dict:
    if not isinstance(expected, str) or not HEX.fullmatch(expected):
        raise Refusal('invalid_document_pin')
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, 'rb') as stream:
        st = os.fstat(stream.fileno())
        if not stat.S_ISREG(st.st_mode) or st.st_size > 65536:
            raise Refusal('invalid_pinned_document')
        data = stream.read(65537)
    if hashlib.sha256(data).hexdigest() != expected:
        raise Refusal('document_hash_mismatch')
    return decode(data)


def file_stat(path: str) -> list:
    value = os.stat(path, follow_symlinks=False)
    if not stat.S_ISREG(value.st_mode):
        raise Refusal('pinned_file_not_regular')
    return [value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns, value.st_ctime_ns]


def verify_file(record: dict) -> list:
    if (not isinstance(record, dict) or set(record) != {'path', 'sha256', 'bytes'}
            or not isinstance(record['path'], str) or not Path(record['path']).is_absolute()
            or not isinstance(record['sha256'], str) or not HEX.fullmatch(record['sha256'])
            or type(record['bytes']) is not int or not 0 < record['bytes'] <= 4 * 1024**3):
        raise Refusal('invalid_file_pin')
    before = file_stat(record['path'])
    if before[2] != record['bytes']:
        raise Refusal('pinned_file_size_changed')
    with open(record['path'], 'rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    if digest != record['sha256'] or file_stat(record['path']) != before:
        raise Refusal('pinned_file_hash_changed')
    return before


def verify_manifest(manifest: dict, receipt: dict, manifest_hash: str) -> dict:
    fields = {'schema', 'proof_id', 'backend_commit', 'rpc_executable',
              'library_directories', 'pinned_libraries', 'nvml_library'}
    if (set(manifest) != fields or manifest['schema'] != 'dulche.guest-runtime/v1'
            or manifest['proof_id'] != PROOF_ID or manifest['backend_commit'] != COMMIT):
        raise Refusal('unsupported_runtime_manifest')
    if (receipt.get('schema') != 'dulche.v100-consumed/v1' or receipt.get('proof_id') != PROOF_ID
            or receipt.get('consumed') is not True or receipt.get('manifest_sha256') != manifest_hash):
        raise Refusal('global_consumed_receipt_mismatch')
    directories = manifest['library_directories']
    if (not isinstance(directories, list) or not 1 <= len(directories) <= 4
            or any(not isinstance(p, str) or not Path(p).is_absolute() or ':' in p
                   or not Path(p).is_dir() for p in directories)):
        raise Refusal('invalid_library_directories')
    libraries = manifest['pinned_libraries']
    if not isinstance(libraries, list) or not libraries or len(libraries) > 32:
        raise Refusal('invalid_library_pins')
    records = [manifest['rpc_executable'], manifest['nvml_library'], *libraries]
    stats = {}
    for record in records:
        observation = verify_file(record)
        if record['path'] in stats:
            raise Refusal('duplicate_file_pin')
        stats[record['path']] = observation
    if Path(manifest['rpc_executable']['path']).name != 'ggml-rpc-server':
        raise Refusal('only_fixed_rpc_backend_is_supported')
    if not os.access(manifest['rpc_executable']['path'], os.X_OK):
        raise Refusal('rpc_not_executable')
    aliases = {}
    for directory in directories:
        pinned = [p for p in stats if Path(p).parent == Path(directory)]
        if not pinned:
            raise Refusal('unrepresented_library_directory')
        for entry in Path(directory).glob('*.so*'):
            if entry.is_symlink():
                if str(entry.resolve(strict=True)) not in stats:
                    raise Refusal('library_alias_target_not_pinned')
                aliases[str(entry)] = os.readlink(entry)
            elif str(entry) not in stats:
                raise Refusal('library_directory_has_unpinned_object')
    return {'mode': 'REAL', 'manifest': manifest, 'file_stats': stats, 'library_aliases': aliases}


def check_stats(config: dict) -> None:
    for path, expected in config.get('file_stats', {}).items():
        if file_stat(path) != expected:
            raise Refusal('runtime_changed_after_preflight')
    for path, expected in config.get('library_aliases', {}).items():
        if not Path(path).is_symlink() or os.readlink(path) != expected:
            raise Refusal('library_alias_changed_after_preflight')


def write_record(path: Path, value: dict) -> None:
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o400)
    with os.fdopen(fd, 'w') as stream:
        json.dump(value, stream, sort_keys=True)
        stream.write('\n')
        stream.flush()
        os.fsync(stream.fileno())
    directory = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(directory)
    finally:
        os.close(directory)


class Policy:
    def __init__(self, start: int, mode: str):
        self.start = self.last_host = self.last_tick = start
        self.mode = mode
        self.last_sample = None
        self.baseline = None
        self.uuid = None
        self.bdf = None
        self.stopped = None

    def fail(self, reason):
        self.stopped = reason
        raise Refusal(reason)

    def tick(self, now: int):
        if self.stopped:
            raise Refusal(self.stopped)
        if type(now) is not int or now < self.last_tick:
            self.fail('guest_clock_reversed')
        self.last_tick = now
        if now - self.start >= WORK_NS:
            self.fail('work_deadline')
        if now - self.last_host > STALE_NS:
            self.fail('host_lease_expired')
        if self.last_sample is None:
            if now - self.start >= COLD_NS:
                self.fail('cold_initialization_deadline')
        elif now - self.last_sample['guest_acquired_ns'] > STALE_NS:
            self.fail('telemetry_stale')

    def host(self, now: int):
        self.tick(now)
        self.last_host = now

    def sample(self, value: dict, now: int):
        self.tick(now)
        if not isinstance(value, dict) or set(value) != {'uuid', 'bdf', 'temperature_c', 'guest_acquired_ns'}:
            self.fail('malformed_gpu_sample')
        acquired = value['guest_acquired_ns']
        temp = value['temperature_c']
        if (type(acquired) is not int or not self.start <= acquired <= now
                or self.last_sample is not None and acquired <= self.last_sample['guest_acquired_ns']
                or now - acquired > STALE_NS):
            self.fail('stale_or_replayed_gpu_sample')
        if (type(temp) not in (int, float) or not 0 <= temp <= 150 or not math.isfinite(temp)):
            self.fail('invalid_gpu_temperature')
        expected_uuid = value['uuid'] == 'CPU-MOCK-V100' if self.mode == 'CPU_MOCK' else (
            isinstance(value['uuid'], str) and UUID.fullmatch(value['uuid']))
        if not expected_uuid or not isinstance(value['bdf'], str) or not BDF.fullmatch(value['bdf']):
            self.fail('invalid_gpu_identity')
        if self.baseline is None:
            if temp > 40:
                self.fail('v100_start_too_hot')
            self.baseline, self.uuid, self.bdf = temp, value['uuid'], value['bdf']
        if value['uuid'] != self.uuid or value['bdf'] != self.bdf:
            self.fail('gpu_identity_changed')
        if temp >= 55 or temp - self.baseline >= 10:
            self.fail('v100_thermal_limit')
        self.last_sample = dict(value)


def send_packet(channel: socket.socket, value: dict):
    data = json.dumps(value, separators=(',', ':')).encode()
    if len(data) > 65536 or channel.send(data) != len(data):
        raise Refusal('child_control_write_failed')


def recv_packet(channel: socket.socket) -> dict:
    data = channel.recv(65537)
    if not data:
        raise EOFError
    return decode(data)


def _emit(value: dict):
    data = (json.dumps(value, separators=(',', ':')) + '\n').encode()
    # stdout is nonblocking; failure stops owned work instead of hanging a guard.
    if os.write(sys.stdout.fileno(), data) != len(data):
        raise Refusal('host_output_blocked')


def child_channel(kind: str, config: dict, extra: dict | None = None, log_fd=None):
    parent, child = socket.socketpair(socket.AF_UNIX, socket.SOCK_SEQPACKET)
    process = subprocess.Popen(
        [sys.executable, '-I', '-S', '-B', str(Path(__file__).resolve()),
         '__' + kind, str(child.fileno()), str(os.getpid())],
        pass_fds=(child.fileno(),), stdin=subprocess.DEVNULL,
        stdout=log_fd if log_fd is not None else subprocess.DEVNULL,
        stderr=log_fd if log_fd is not None else subprocess.DEVNULL,
        start_new_session=True, env=safety._fixture_environment())
    child.close()
    parent.setblocking(False)
    try:
        send_packet(parent, {'config': config, **(extra or {})})
    except BaseException:
        parent.close()
        safety._stop_owned(process, time.monotonic_ns() + 1_000_000_000)
        raise
    return process, parent


def v100_bdf(sys_root=Path('/sys/bus/pci/devices')) -> str | None:
    nvidia = []
    for path in sys_root.glob('*'):
        try:
            if (path / 'vendor').read_text().strip() == '0x10de':
                nvidia.append(path)
        except FileNotFoundError:
            continue
    if not nvidia:
        return None
    if len(nvidia) != 1:
        raise Refusal('guest_has_unexpected_nvidia_devices')
    path = nvidia[0]
    if ((path / 'device').read_text().strip() != '0x1db4'
            or (path / 'subsystem_vendor').read_text().strip() != '0x10de'
            or (path / 'subsystem_device').read_text().strip() != '0x1214'
            or not BDF.fullmatch(path.name)):
        raise Refusal('guest_pci_identity_mismatch')
    return path.name


def v100_driver_ready(bdf: str, pci_root=Path('/sys/bus/pci/devices'),
                      proc_root=Path('/proc/driver/nvidia/gpus')) -> bool:
    """Wait for guest udev/driver registration without invoking NVML."""
    driver = pci_root / bdf / 'driver'
    try:
        name = driver.resolve(strict=True).name
    except FileNotFoundError:
        return False
    if name != 'nvidia':
        raise Refusal('unexpected_guest_gpu_driver')
    return (proc_root / bdf / 'information').is_file()


class NvmlTemperature:
    """Created only in the REAL collector child after physical PCI discovery."""
    # Signatures and target-initialization semantics verified against:
    # https://docs.nvidia.com/deploy/nvml-api/latest/api/group__nvmlDeviceQueries.html
    # Only init_v2 status9 and PCI-handle status6 are readiness cases:
    # https://docs.nvidia.com/deploy/nvml-api/latest/api/group__nvmlInitializationAndCleanup.html
    # NVIDIA/go-nvml nvml.h: NOT_FOUND=6, DRIVER_NOT_LOADED=9.
    def __init__(self, path: str):
        self.lib = ctypes.CDLL(path)
        self.initialized = False
        self.handle = None
        for name, args in (
            ('nvmlInit_v2', []),
            ('nvmlShutdown', []),
            ('nvmlDeviceGetHandleByPciBusId_v2', [ctypes.c_char_p, ctypes.POINTER(ctypes.c_void_p)]),
            ('nvmlDeviceGetUUID', [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_uint]),
            ('nvmlDeviceGetTemperature', [ctypes.c_void_p, ctypes.c_uint, ctypes.POINTER(ctypes.c_uint)]),
        ):
            fn = getattr(self.lib, name)
            fn.argtypes, fn.restype = args, ctypes.c_int

    def connect(self, bdf: str):
        if self.initialized:
            raise Refusal('nvml_already_initialized')
        code = self.lib.nvmlInit_v2()
        if code == 9:
            raise NvmlColdPending('nvml_driver_not_loaded')
        self.checked(code)
        self.initialized = True
        try:
            self.handle = ctypes.c_void_p()
            code = self.lib.nvmlDeviceGetHandleByPciBusId_v2(bdf.encode(), ctypes.byref(self.handle))
            if code == 6:
                raise NvmlColdPending('nvml_pci_device_not_ready')
            self.checked(code)
            uuid = ctypes.create_string_buffer(96)
            self.checked(self.lib.nvmlDeviceGetUUID(self.handle, uuid, len(uuid)))
            self.uuid, self.bdf = uuid.value.decode('ascii'), bdf
            if not UUID.fullmatch(self.uuid):
                raise Refusal('nvml_uuid_invalid')
        except BaseException:
            # Balance every successful init even when handle lookup fails.
            # A shutdown failure is fatal, not another readiness retry.
            self.close()
            raise

    def close(self):
        if self.initialized:
            self.initialized = False
            self.handle = None
            self.checked(self.lib.nvmlShutdown())

    @staticmethod
    def checked(code):
        if code:
            raise Refusal('nvml_error_' + str(code))

    def sample(self) -> dict:
        if not self.initialized or self.handle is None:
            raise Refusal('nvml_not_connected')
        temperature = ctypes.c_uint()
        self.checked(self.lib.nvmlDeviceGetTemperature(self.handle, 0, ctypes.byref(temperature)))
        return {'uuid': self.uuid, 'bdf': self.bdf, 'temperature_c': temperature.value,
                'guest_acquired_ns': time.monotonic_ns()}


def wait_for_nvml(path: str, start: int, *, clock=time.monotonic_ns, sleep=time.sleep,
                  discover=v100_bdf, ready=v100_driver_ready, factory=NvmlTemperature):
    """Readiness only, inside the same already-armed cold deadline; no reset."""
    deadline = start + COLD_NS
    sensor = None
    seen_bdf = None
    try:
        while clock() < deadline:
            bdf = discover()
            if seen_bdf is not None and bdf != seen_bdf:
                raise Refusal('guest_pci_changed_during_initialization')
            if bdf is not None:
                seen_bdf = bdf
                if ready(bdf):
                    if sensor is None:
                        sensor = factory(path)  # One persistent library wrapper.
                    try:
                        sensor.connect(bdf)
                    except NvmlColdPending:
                        pass
                    else:
                        if clock() >= deadline:
                            raise Refusal('cold_initialization_deadline')
                        return sensor
            sleep(min(0.25, max(0, (deadline - clock()) / 1e9)))
        raise Refusal('cold_initialization_deadline')
    except BaseException:
        if sensor is not None:
            sensor.close()
        raise


def collector(channel: socket.socket, config: dict, parent: int, start: int):
    safety._parent_death(parent)
    check_stats(config)
    real = config['mode'] == 'REAL'
    scenario = config.get('mock_scenario', 'normal')
    if real:
        sensor = wait_for_nvml(config['manifest']['nvml_library']['path'], start)
    elif scenario == 'cold_timeout':
        time.sleep(60)
    send_packet(channel, {'event': 'collector_ready'})
    count = 0
    while True:
        request = recv_packet(channel)
        if set(request) != {'nonce', 'sequence'}:
            raise Refusal('invalid_collector_request')
        if real:
            sample = sensor.sample()
        else:
            if scenario == 'telemetry_hang' and count:
                time.sleep(60)
            sample = {'uuid': 'CPU-MOCK-V100', 'bdf': '0000:01:00.0',
                      'temperature_c': 55 if scenario == 'hot' and count else 35,
                      'guest_acquired_ns': time.monotonic_ns()}
        count += 1
        send_packet(channel, {'event': 'sample', **request, 'telemetry': sample})


def _rpc_exec(channel: socket.socket, config: dict, parent: int, uuid: str):
    safety._parent_death(parent)
    check_stats(config)
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    resource.setrlimit(resource.RLIMIT_FSIZE, (4 * 1024**2, 4 * 1024**2))
    if config['mode'] == 'CPU_MOCK':
        while True:
            time.sleep(60)
    if not UUID.fullmatch(uuid):
        raise Refusal('rpc_uuid_invalid')
    env = {'PATH': '/usr/bin:/bin', 'LANG': 'C', 'LC_ALL': 'C',
           'CUDA_VISIBLE_DEVICES': uuid, 'CUDA_DEVICE_ORDER': 'PCI_BUS_ID',
           'OMP_NUM_THREADS': '2', 'GGML_RPC_NO_RDMA': '1', 'GGML_RPC_NO_COMM': '1',
           'GGML_RPC_DEBUG': '3',
           'LD_LIBRARY_PATH': ':'.join(config['manifest']['library_directories'])}
    executable = config['manifest']['rpc_executable']['path']
    channel.close()
    os.execve(executable, [executable, '--host', '127.0.0.1', '--port', '50052',
                           '--device', 'CUDA0', '--threads', '2'], env)


def guardian(channel: socket.socket, config: dict, parent: int, start: int, baseline: dict, initial_temperature: float):
    """Own the RPC child independently of the outer controller and SSH lease."""
    def terminate(_signum, _frame):
        raise Refusal('guardian_parent_or_signal_lost')
    signal.signal(signal.SIGTERM, terminate)
    libc = ctypes.CDLL(None, use_errno=True)
    if libc.prctl(1, signal.SIGTERM, 0, 0, 0) != 0 or os.getppid() != parent:
        raise Refusal('guardian_parent_missing')
    child = None
    last = baseline
    reason = 'guardian_failed'
    channel.setblocking(False)
    try:
        log_fd = os.open(Path(config['state_dir']) / 'rpc.log',
                         os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
        try:
            child, child_socket = child_channel('rpc', config, {'uuid': baseline['uuid']}, log_fd=log_fd)
        finally:
            os.close(log_fd)
        child_socket.close()
        send_packet(channel, {'event': 'rpc_started', 'rpc_pid': child.pid})
        with selectors.DefaultSelector() as selected:
            selected.register(channel, selectors.EVENT_READ)
            while True:
                now = time.monotonic_ns()
                if now - start >= WORK_NS or now - last['guest_acquired_ns'] > STALE_NS:
                    raise Refusal('guardian_deadline_or_telemetry_lease')
                if child.poll() is not None:
                    raise Refusal('rpc_exited')
                for _, _ in selected.select(0.025):
                    message = recv_packet(channel)
                    now = time.monotonic_ns()
                    if message.get('command') == 'stop':
                        raise Refusal('explicit_stop')
                    sample = message.get('telemetry')
                    if (not isinstance(sample, dict) or sample.get('uuid') != baseline['uuid']
                            or sample.get('bdf') != baseline['bdf']
                            or type(sample.get('guest_acquired_ns')) is not int
                            or not last['guest_acquired_ns'] < sample['guest_acquired_ns'] <= now
                            or now - sample['guest_acquired_ns'] > STALE_NS):
                        raise Refusal('guardian_invalid_sample')
                    temp = sample.get('temperature_c')
                    if (type(temp) not in (int, float) or not 0 <= temp < 55
                            or not math.isfinite(temp) or temp - initial_temperature >= 10):
                        raise Refusal('guardian_thermal_limit')
                    last = sample
                    send_packet(channel, {'event': 'guardian_alive', 'acquired_ns': last['guest_acquired_ns']})
    except (Refusal, EOFError, OSError) as error:
        reason = str(error)
        try:
            send_packet(channel, {'event': 'guardian_stopping', 'reason': str(error)})
        except OSError:
            pass
    finally:
        # Further TERM signals must not interrupt the reaping path. SIGKILL still
        # activates the direct RPC child's kernel parent-death safeguard.
        signal.signal(signal.SIGTERM, signal.SIG_IGN)
        stopped = safety._stop_owned(child, start + TOTAL_NS)
        write_record(Path(config['state_dir']) / 'rpc-terminal.json', {
            'kind': config['mode'], 'rpc_pid': None if child is None else child.pid,
            'rpc_exit_code': None if child is None else child.returncode,
            'rpc_reaped': stopped, 'reason': reason,
            'elapsed_ns': time.monotonic_ns() - start})
        try:
            send_packet(channel, {'event': 'guardian_stopped', 'rpc_stopped': stopped})
        except OSError:
            pass


def runtime(config: dict, state_dir: Path):
    config = {**config, 'state_dir': str(state_dir.resolve())}
    write_record(state_dir / 'guest-invocation.json', {'kind': config['mode'], 'proof_id': PROOF_ID,
                  'at_ns': time.monotonic_ns(), 'gpu_dispatch_before_arm': False})
    os.set_blocking(sys.stdin.fileno(), False)
    os.set_blocking(sys.stdout.fileno(), False)
    _emit({'event': 'ready', 'kind': config['mode'], 'schema': CONTROL_SCHEMA})
    policy = None
    collector_process = guardian_process = None
    collector_socket = guardian_socket = None
    collector_ready = False
    expected_sequence = 0
    session = None
    used_nonces = set()
    pending = None
    rpc_pid = None
    guardian_ack = None
    buffer = b''
    reason = 'not_armed'
    status = 2
    wait_start = time.monotonic_ns()

    def reply(command, telemetry=None, state=None):
        _emit({'schema': CONTROL_SCHEMA, 'kind': config['mode'],
               **{key: command[key] for key in ('session', 'sequence', 'nonce')},
               'state': state or ('RPC_ALIVE' if rpc_pid else 'BASELINE_READY'),
               'telemetry': telemetry, 'rpc_pid': rpc_pid,
               'collector_pid': collector_process.pid if collector_process else None,
               'guardian_pid': guardian_process.pid if guardian_process else None,
               'elapsed_ns': 0 if policy is None else time.monotonic_ns() - policy.start})

    try:
        with selectors.DefaultSelector() as selected:
            selected.register(sys.stdin.fileno(), selectors.EVENT_READ, 'host')
            while True:
                now = time.monotonic_ns()
                if policy is not None:
                    policy.tick(now)
                    if collector_process.poll() is not None:
                        raise Refusal('collector_exited')
                    if guardian_process is not None:
                        if guardian_process.poll() is not None:
                            raise Refusal('guardian_exited')
                        if guardian_ack is not None and now - guardian_ack > STALE_NS:
                            raise Refusal('guardian_ack_stale')
                elif now - wait_start > 30_000_000_000:
                    raise Refusal('arm_not_received')
                for key, _ in selected.select(0.025):
                    if key.data == 'guardian':
                        event = recv_packet(guardian_socket)
                        if event.get('event') == 'rpc_started':
                            rpc_pid = event['rpc_pid']
                            guardian_ack = time.monotonic_ns()
                        elif event.get('event') == 'guardian_alive':
                            guardian_ack = time.monotonic_ns()
                        else:
                            raise Refusal('guardian_stopped_or_failed')
                    elif key.data == 'collector':
                        event = recv_packet(collector_socket)
                        if event.get('event') == 'collector_ready':
                            collector_ready = True
                            continue
                        if (pending is None or event.get('event') != 'sample'
                                or event.get('nonce') != pending['nonce']
                                or event.get('sequence') != pending['sequence']):
                            raise Refusal('collector_response_mismatch')
                        policy.sample(event['telemetry'], time.monotonic_ns())
                        if pending['command'] == 'start':
                            if policy.last_sample['temperature_c'] > 40:
                                raise Refusal('start_sample_too_hot')
                            guardian_process, guardian_socket = child_channel('guardian', config,
                                {'start_ns': policy.start, 'baseline': policy.last_sample,
                                 'initial_temperature': policy.baseline})
                            guardian_ack = time.monotonic_ns()
                            selected.register(guardian_socket, selectors.EVENT_READ, 'guardian')
                        elif guardian_socket is not None:
                            send_packet(guardian_socket, {'command': 'safe_sample', 'telemetry': policy.last_sample})
                        reply(pending, policy.last_sample,
                              'RPC_STARTING' if pending['command'] == 'start' else None)
                        pending = None
                    else:
                        data = os.read(sys.stdin.fileno(), 4096)
                        if not data:
                            raise Refusal('host_channel_closed')
                        buffer += data
                        if len(buffer) > 8192:
                            raise Refusal('host_frame_too_large')
                        while b'\n' in buffer:
                            line, buffer = buffer.split(b'\n', 1)
                            command = decode(line)
                            if (set(command) != {'schema', 'session', 'sequence', 'nonce', 'command'}
                                    or command['schema'] != CONTROL_SCHEMA
                                    or not isinstance(command['session'], str)
                                    or not re.fullmatch('[0-9a-f]{32}', command['session'])
                                    or type(command['sequence']) is not int
                                    or command['sequence'] != expected_sequence
                                    or not isinstance(command['nonce'], str)
                                    or not HEX.fullmatch(command['nonce'])
                                    or command['nonce'] in used_nonces
                                    or len(used_nonces) >= 1024
                                    or command['command'] not in ('arm', 'poll', 'start', 'stop')
                                    or session is not None and command['session'] != session):
                                raise Refusal('invalid_host_challenge')
                            if pending is not None and command['command'] != 'stop':
                                raise Refusal('multiple_pending_challenges')
                            expected_sequence += 1
                            used_nonces.add(command['nonce'])
                            session = command['session']
                            if command['command'] == 'arm':
                                if policy is not None:
                                    raise Refusal('already_armed')
                                policy = Policy(time.monotonic_ns(), config['mode'])
                                collector_process, collector_socket = child_channel(
                                    'collector', config, {'start_ns': policy.start})
                                selected.register(collector_socket, selectors.EVENT_READ, 'collector')
                                reply(command, state='ARMED_COLD')
                                continue
                            if policy is None:
                                raise Refusal('not_armed')
                            policy.host(time.monotonic_ns())
                            if command['command'] == 'stop':
                                reply(command, state='STOPPING')
                                raise StopRequested
                            if command['command'] == 'start' and (policy.last_sample is None or guardian_process is not None):
                                raise Refusal('start_requires_baseline_and_no_prior_rpc')
                            if not collector_ready:
                                reply(command, state='COLD_PENDING')
                                continue
                            pending = command
                            send_packet(collector_socket, {'nonce': command['nonce'], 'sequence': command['sequence']})
    except StopRequested:
        reason, status = 'explicit_stop', 0
    except (Refusal, OSError, EOFError) as error:
        reason = str(error)
    finally:
        deadline = (policy.start + TOTAL_NS) if policy is not None else time.monotonic_ns() + 1_000_000_000
        if guardian_socket is not None:
            try:
                send_packet(guardian_socket, {'command': 'stop'})
            except OSError:
                pass
            try:
                guardian_process.wait(timeout=min(0.3, max(0, (deadline-time.monotonic_ns())/1e9)))
            except subprocess.TimeoutExpired:
                pass
        # Guardian owns/reaps RPC. If it is frozen, killing it causes RPC's
        # kernel parent-death signal. Root still must verify and stop the VM.
        guardian_stopped = safety._stop_owned(guardian_process, deadline)
        collector_stopped = safety._stop_owned(collector_process, deadline)
        for channel in (guardian_socket, collector_socket):
            if channel is not None:
                channel.close()
        terminal = {'event': 'stopped', 'kind': config['mode'], 'reason': reason,
                    'guardian_reaped': guardian_stopped, 'collector_reaped': collector_stopped,
                    'rpc_pid': rpc_pid, 'physical_gpu_quiescence': 'UNVERIFIED',
                    'elapsed_ns': 0 if policy is None else time.monotonic_ns() - policy.start}
        write_record(state_dir / 'guest-terminal.json', terminal)
        try:
            _emit(terminal)
        except OSError:
            pass
        if not guardian_stopped or not collector_stopped or time.monotonic_ns() > deadline:
            status = 2
    return status


def main():
    if len(sys.argv) == 4 and sys.argv[1] in ('__collector', '__guardian', '__rpc'):
        channel = socket.socket(fileno=int(sys.argv[2]))
        channel.settimeout(1)
        payload = recv_packet(channel)
        channel.settimeout(None)
        config, parent = payload['config'], int(sys.argv[3])
        if os.getppid() != parent or config.get('mode') not in ('REAL', 'CPU_MOCK'):
            raise Refusal('invalid_child_custody')
        if sys.argv[1] == '__collector':
            collector(channel, config, parent, payload['start_ns'])
        elif sys.argv[1] == '__guardian':
            guardian(channel, config, parent, payload['start_ns'], payload['baseline'], payload['initial_temperature'])
        else:
            _rpc_exec(channel, config, parent, payload['uuid'])
        return 0
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--state-dir', required=True, type=Path)
    parser.add_argument('--cpu-mock', action='store_true')
    parser.add_argument('--mock-scenario', choices=('normal', 'cold_timeout', 'telemetry_hang', 'hot'), default='normal')
    parser.add_argument('--manifest', type=Path)
    parser.add_argument('--manifest-sha256')
    parser.add_argument('--consumed-receipt', type=Path)
    parser.add_argument('--receipt-sha256')
    args = parser.parse_args()
    try:
        if args.state_dir.is_symlink() or not args.state_dir.is_dir():
            raise Refusal('private_existing_state_directory_required')
        if args.state_dir.stat().st_mode & 0o077:
            raise Refusal('state_directory_must_be_private')
        if args.cpu_mock:
            if any((args.manifest, args.manifest_sha256, args.consumed_receipt, args.receipt_sha256)):
                raise Refusal('mock_and_real_inputs_cannot_mix')
            config = {'mode': 'CPU_MOCK', 'mock_scenario': args.mock_scenario}
        else:
            if not all((args.manifest, args.manifest_sha256, args.consumed_receipt, args.receipt_sha256)):
                raise Refusal('real_manifest_and_consumed_receipt_required')
            if args.mock_scenario != 'normal':
                raise Refusal('mock_scenarios_cannot_affect_real_runtime')
            manifest = pinned_json(args.manifest, args.manifest_sha256)
            receipt = pinned_json(args.consumed_receipt, args.receipt_sha256)
            config = verify_manifest(manifest, receipt, args.manifest_sha256)
        return runtime(config, args.state_dir)
    except (Refusal, OSError, ValueError, KeyError) as error:
        print(str(error), file=sys.stderr)
        return 2


if __name__ == '__main__':
    raise SystemExit(main())
