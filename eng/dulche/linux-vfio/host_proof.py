"""Bounded host controller, inert CLI, and injectable proof transports.

No consume/claim, privilege prompt, QEMU boot, SSH setup or inference launch is
automatic. Root prepares guardians, verified resource containment, transport and
the publication/dispatch receipts first. run_proof is the explicit integration
entrypoint; tests inject CPU fakes. CLI only validates a pinned deployment plan.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import secrets
import stat
import subprocess
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parent))
import proof_authority as authority
from proof_authority import ColdStartPolicy, PROOF_ID, MODEL_SHA256
from proof_safety import Refusal, SAMPLE_NS, STALE_NS, WORK_NS, TOTAL_NS

COMMIT = 'b1b770d90d94cad9e9ea5b993bdbc383a522fb28'
GUEST_SCHEMA = 'dulche.guest-control/v1'
MODEL_ID = 'tinyllama-1.1b-chat-v1.0-q4_k_m'
_HEX = re.compile('[0-9a-f]{64}')


def decode(data: bytes) -> dict:
    def unique(pairs):
        value = {}
        for key, item in pairs:
            if key in value:
                raise Refusal('duplicate_json_field')
            value[key] = item
        return value
    def bad_constant(_):
        raise Refusal('nonfinite_json')
    if type(data) is not bytes or len(data) > 65536:
        raise Refusal('invalid_json_size')
    try:
        value = json.loads(data, object_pairs_hook=unique, parse_constant=bad_constant)
    except (ValueError, UnicodeError, RecursionError) as error:
        raise Refusal('invalid_json') from error
    if not isinstance(value, dict):
        raise Refusal('json_object_required')
    return value


def _file_identity(path: Path) -> tuple:
    value = path.stat(follow_symlinks=False)
    if not stat.S_ISREG(value.st_mode):
        raise Refusal('nonregular_pinned_file')
    return value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns, value.st_ctime_ns


def verify_pin(pin: dict) -> tuple:
    if (not isinstance(pin, dict) or not {'path', 'sha256', 'bytes'} <= pin.keys()
            or type(pin['path']) is not str or not Path(pin['path']).is_absolute()
            or type(pin['sha256']) is not str or not _HEX.fullmatch(pin['sha256'])
            or type(pin['bytes']) is not int or not 0 < pin['bytes'] <= 4 * 1024**3):
        raise Refusal('invalid_file_pin')
    path = Path(pin['path']); before = _file_identity(path)
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, 'rb') as stream:
        opened = os.fstat(stream.fileno())
        if (opened.st_dev, opened.st_ino, opened.st_size) != before[:3]:
            raise Refusal('pinned_file_replaced_during_open')
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    if before[2] != pin['bytes'] or digest != pin['sha256'] or _file_identity(path) != before:
        raise Refusal('pinned_file_changed')
    return before


def validate_plan(plan: dict) -> dict:
    """Metadata-only preflight; caller acquires model lease before file hashing."""
    if (plan.get('schema') != 'dulche.host-proof/v1' or plan.get('proof_id') != PROOF_ID
            or plan.get('backend_commit') != COMMIT or plan.get('rpc_endpoint') != '127.0.0.1:50053'
            or not isinstance(plan.get('session'), str) or not re.fullmatch('[0-9a-f]{32}', plan['session'])):
        raise Refusal('unsupported_host_plan')
    authority._uuid(plan.get('host_gpu_uuid'))
    authority._hex(plan.get('guest_manifest_sha256'), 64, 'guest_manifest_sha256')
    model = plan.get('model', {})
    if (model.get('id') != MODEL_ID or model.get('sha256') != MODEL_SHA256
            or model.get('bytes') != 668788096):
        raise Refusal('only_approved_tinyllama_is_allowed')
    executable = plan.get('host_executable', {})
    if Path(executable.get('path', '')).name != 'llama-server':
        raise Refusal('only_pinned_llama_server_is_allowed')
    libraries = plan.get('pinned_libraries')
    directories = plan.get('library_directories')
    if not isinstance(libraries, list) or not libraries or not isinstance(directories, list) or not directories:
        raise Refusal('pinned_library_closure_missing')
    if any(type(item) is not str or not Path(item).is_absolute() or ':' in item for item in directories):
        raise Refusal('invalid_library_directory')
    runtime = Path(plan.get('runtime_directory', ''))
    socket = Path(plan.get('http_socket', ''))
    if not runtime.is_absolute() or runtime.is_symlink() or not runtime.is_dir():
        raise Refusal('runtime_directory_missing_or_unsafe')
    metadata = runtime.stat()
    if metadata.st_uid != os.getuid() or metadata.st_mode & 0o077:
        raise Refusal('runtime_directory_not_private')
    if socket.parent != runtime or not socket.name.endswith('.sock') or socket.exists() or socket.is_symlink():
        raise Refusal('worker_socket_not_fresh_private_path')
    if plan.get('lease_runtime_dir') != f'/run/user/{os.getuid()}/haven':
        raise Refusal('shared_dulche_lease_directory_required')
    return plan


def pinned_plan(path: Path, expected_sha256: str) -> tuple[dict, bytes]:
    authority._hex(expected_sha256, 64, 'deployment_sha256')
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, 'rb') as stream:
        if not stat.S_ISREG(os.fstat(stream.fileno()).st_mode):
            raise Refusal('plan_not_regular')
        raw = stream.read(65537)
    if hashlib.sha256(raw).hexdigest() != expected_sha256:
        raise Refusal('deployment_hash_mismatch')
    return validate_plan(decode(raw)), raw


def verify_admission(plan: dict, admission: dict, deployment_bytes: bytes) -> None:
    """Validate root-issued receipts, not create another dispatch claim."""
    if hashlib.sha256(deployment_bytes).hexdigest() != admission.get('deployment_sha256'):
        raise Refusal('admission_deployment_mismatch')
    if decode(deployment_bytes) != plan:
        raise Refusal('deployment_bytes_differ_from_plan')
    consumed_bytes, consumed = authority.read_consumed()
    publication_bytes, published = authority._read('PUBLICATION.json')
    dispatch_bytes, dispatch = authority._read('DISPATCH-CLAIMED.json')
    dispatch_publication_bytes, dispatch_publication = authority._read('DISPATCH-PUBLICATION.json')
    execution_bytes, execution = authority._read('EXECUTION-STARTED.json')
    for key, raw in (('consumed_sha256', consumed_bytes), ('publication_sha256', publication_bytes),
                     ('dispatch_sha256', dispatch_bytes), ('dispatch_publication_sha256', dispatch_publication_bytes),
                     ('execution_sha256', execution_bytes)):
        if hashlib.sha256(raw).hexdigest() != admission.get(key):
            raise Refusal('admission_receipt_mismatch:' + key)
    if (consumed['manifest_sha256'] != plan['guest_manifest_sha256']
            or consumed['host_gpu_uuid'] != plan['host_gpu_uuid']
            or published.get('consumed_sha256') != admission['consumed_sha256']
            or dispatch.get('consumed_sha256') != admission['consumed_sha256']
            or dispatch.get('proof_id') != PROOF_ID or dispatch.get('no_retry') is not True
            or dispatch.get('consumed_commit_sha') != published.get('consumed_commit_sha')
            or dispatch_publication.get('dispatch_sha256') != admission['dispatch_sha256']
            or dispatch_publication.get('consumed_sha256') != admission['consumed_sha256']
            or execution.get('schema') != 'dulche.v100-execution-started/v1'
            or execution.get('no_retry') is not True or execution.get('proof_id') != PROOF_ID
            or execution.get('consumed_sha256') != admission['consumed_sha256']
            or execution.get('dispatch_sha256') != admission['dispatch_sha256']
            or execution.get('dispatch_commit_sha') != dispatch_publication.get('dispatch_commit_sha')
            or admission.get('fresh_remote_main_sha') != dispatch_publication.get('dispatch_commit_sha')
            or admission.get('fresh_remote_dispatch_sha256') != admission['dispatch_sha256']
            or admission.get('fresh_remote_consumed_sha256') != admission['consumed_sha256']):
        raise Refusal('admission_chain_mismatch')
    owner = {**authority.process_identity(), 'session': plan['session']}
    if any(record.get('owner') != owner for record in (dispatch, dispatch_publication, execution)):
        raise Refusal('admission_not_owned_by_this_controller')


class JsonLinePeer:
    """Nonblocking bounded stdio for a Popen owned/prepared by the root caller."""
    def __init__(self, process: subprocess.Popen):
        if process.stdin is None or process.stdout is None:
            raise Refusal('peer_requires_owned_stdio')
        self.process = process; self.buffer = b''; self.eof = False
        os.set_blocking(process.stdin.fileno(), False)
        os.set_blocking(process.stdout.fileno(), False)

    def send(self, value: dict) -> None:
        raw = (json.dumps(value, separators=(',', ':'), allow_nan=False) + '\n').encode()
        if len(raw) > 4096:
            raise Refusal('control_frame_too_large')
        if os.write(self.process.stdin.fileno(), raw) != len(raw):
            raise Refusal('control_write_incomplete')

    def drain(self) -> list[dict]:
        frames = []
        if self.eof:
            raise Refusal('control_channel_eof')
        while True:
            try:
                raw = os.read(self.process.stdout.fileno(), 8192)
            except BlockingIOError:
                break
            if not raw:
                self.eof = True
                if frames: break
                raise Refusal('control_channel_eof')
            self.buffer += raw
            if len(self.buffer) > 65536:
                raise Refusal('control_output_too_large')
            while b'\n' in self.buffer:
                line, self.buffer = self.buffer.split(b'\n', 1)
                frames.append(decode(line))
                if len(frames) > 32:
                    raise Refusal('control_frame_burst_exceeded')
        return frames


class Host5080Sensor:
    """Only the explicitly pinned 5080; one query at a time, 500 ms timeout.

    tick() is nonblocking. No call to this class occurs in the CPU test suite.
    Root starts it before the physical timer, independently authorized for 5080.
    """
    def __init__(self, uuid: str, nvidia_smi_pin: dict):
        self.uuid = authority._uuid(uuid)
        if Path(nvidia_smi_pin['path']).name != 'nvidia-smi':
            raise Refusal('wrong_host_sensor_binary')
        verify_pin(nvidia_smi_pin)
        self.pin = nvidia_smi_pin; self.process = None
        self.last = None; self.started = None; self.next_query = 0

    def tick(self, now_ns: int) -> None:
        if self.process is not None:
            if self.process.poll() is None:
                if now_ns - self.started >= 500_000_000:
                    self.process.kill(); self.process.wait(timeout=0.1)
                    raise Refusal('host_telemetry_query_timeout')
                return
            stdout, stderr = self.process.communicate(timeout=0.05)
            code = self.process.returncode; self.process = None
            if code or len(stdout) > 4096 or len(stderr) > 4096:
                raise Refusal('host_telemetry_query_failed')
            try:
                fields = stdout.decode('ascii').strip().split(',')
                temperature = float(fields[1].strip())
                if len(fields) != 2 or fields[0].strip() != self.uuid or not math.isfinite(temperature):
                    raise ValueError('identity/value')
            except (ValueError, IndexError, UnicodeError) as error:
                raise Refusal('host_telemetry_invalid') from error
            self.last = {'uuid': self.uuid, 'temperature_c': temperature,
                         'acquired_ns': self.started}  # Conservative request-start bound.
        if now_ns >= self.next_query:
            self.started = now_ns; self.next_query = now_ns + SAMPLE_NS
            self.process = subprocess.Popen(
                [self.pin['path'], '-i', self.uuid, '--query-gpu=uuid,temperature.gpu',
                 '--format=csv,noheader,nounits'], stdin=subprocess.DEVNULL,
                stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                env={'PATH': '/usr/bin:/bin', 'LANG': 'C'}, start_new_session=True)

    def latest(self) -> dict | None:
        return self.last

    def stop(self) -> None:
        if self.process is not None:
            if self.process.poll() is None:
                self.process.kill()
            self.process.communicate(timeout=0.2)
            self.process = None


def fixed_worker_argv(plan: dict) -> list[str]:
    """Arguments only; an independently contained injected owner launches them."""
    return [plan['host_executable']['path'], '--model', plan['model']['path'],
            '--host', plan['http_socket'], '--rpc', '127.0.0.1:50053',
            '--device', 'CUDA0,RPC0', '--split-mode', 'layer', '--tensor-split', '1,1',
            '--gpu-layers', '99', '--ctx-size', '512', '--batch-size', '32', '--ubatch-size', '32',
            '--threads', '2', '--parallel', '1', '--predict', '8', '--fit', 'off', '--no-warmup']


class Controller:
    """Pure orchestration plus injected transports; never consumes or retries.

    Dependencies:
      helper/guest: send(dict), drain()->list[dict], already ready/prepared.
      qmp: attach_async() once for fixed V100/gpuroot; poll()->pending|attached|failed.
      sensor: tick(now), latest()->{uuid,temperature_c,acquired_ns}, stop().
      worker: prepared()->bool (verified cgroup/identity/leases), start(plan),
        heartbeat(now), poll()->{state:idle|loading|completed|failed,result?},
        request_stop(), is_stopped()->bool. No automatic backend/request retry.
    Root must retain/pin each adapter and independently enforce its deadlines.
    """
    def __init__(self, plan, admission, helper, guest, qmp, sensor, worker,
                 *, clock=time.monotonic_ns, sleep=time.sleep, emit=lambda event: None):
        self.plan = plan; self.admission = admission
        self.helper = helper; self.guest = guest; self.qmp = qmp
        self.sensor = sensor; self.worker = worker
        self.clock = clock; self.sleep = sleep; self.emit = emit
        self.sequence = 0; self.pending = None; self.policy = None
        self.helper_bound = False; self.qmp_attached = False; self.guest_armed = False
        self.rpc_started = False; self.worker_started = False
        self.last_guest_acquired = -1; self.helper_last_ack = None
        self.guest_stop_ack = False; self.helper_terminal = None
        self.guest_bdf = None; self.result = None
        self.helper_sequence = 0; self.helper_bind_sent = False

    def _helper_send(self, action, now, **extra):
        self.helper.send({'action': action, 'session': self.plan['session'],
                          'sent_ns': now, 'sequence': self.helper_sequence, **extra})
        self.helper_sequence += 1
        if action == 'bind': self.helper_bind_sent = True

    def _guest_send(self, command, now):
        message = {'schema': GUEST_SCHEMA, 'session': self.plan['session'],
                   'sequence': self.sequence, 'nonce': secrets.token_hex(32), 'command': command}
        self.sequence += 1
        self.guest.send(message)
        if command != 'stop':
            self.pending = (message, now)

    def _host_sample(self, now):
        self.sensor.tick(now)
        sample = self.sensor.latest()
        if (not isinstance(sample, dict) or sample.get('uuid') != self.plan['host_gpu_uuid']
                or type(sample.get('acquired_ns')) is not int
                or not 0 <= now - sample['acquired_ns'] <= STALE_NS):
            raise Refusal('host_telemetry_missing_or_stale')
        temperature = sample.get('temperature_c')
        if (type(temperature) not in (int, float) or not 0 <= temperature < 75
                or not math.isfinite(temperature)):
            raise Refusal('host_thermal_limit_or_invalid')
        return sample

    def _guest_frames(self, now, host):
        for frame in self.guest.drain():
            if frame.get('event') == 'stopped':
                self.guest_stop_ack = True
                raise Refusal('guest_stopped')
            if self.pending is None:
                raise Refusal('unsolicited_guest_response')
            sent, sent_ns = self.pending
            if (frame.get('schema') != GUEST_SCHEMA or frame.get('kind') != 'REAL'
                    or any(frame.get(key) != sent[key] for key in ('session', 'sequence', 'nonce'))):
                raise Refusal('guest_challenge_mismatch')
            if now - sent_ns > STALE_NS:
                raise Refusal('guest_round_trip_stale')
            self.pending = None
            state = frame.get('state')
            if sent['command'] == 'arm':
                if state != 'ARMED_COLD':
                    raise Refusal('guest_arm_failed')
                self.guest_armed = True
                if now - self.policy.start_ns > 500_000_000:
                    raise Refusal('guest_arm_too_late_for_helper_bind')
                self._helper_send('bind', now, start_ns=self.policy.start_ns,
                                  receipt_sha256=self.admission['consumed_sha256'],
                                  manifest_sha256=self.plan['guest_manifest_sha256'])
            telemetry = frame.get('telemetry')
            if telemetry is not None:
                if (not isinstance(telemetry, dict)
                        or type(telemetry.get('guest_acquired_ns')) is not int
                        or telemetry['guest_acquired_ns'] <= self.last_guest_acquired
                        or not isinstance(telemetry.get('bdf'), str)
                        or not re.fullmatch('[0-9a-f]{4}:[0-9a-f]{2}:[0-9a-f]{2}\\.[0-7]', telemetry['bdf'])):
                    raise Refusal('guest_sample_invalid_or_replayed')
                self.last_guest_acquired = telemetry['guest_acquired_ns']
                if self.guest_bdf is not None and self.guest_bdf != telemetry['bdf']:
                    raise Refusal('guest_bdf_changed')
                self.guest_bdf = telemetry['bdf']
                if not self.policy.guest_identity_pinned:
                    # Pinned guest collector checks these exact IDs before NVML.
                    self.policy.pin_guest_identity(uuid=telemetry['uuid'], pci_id='10de:1db4',
                                                   subsystem_id='10de:1214')
                    self.emit({'event': 'guest_identity', 'uuid': telemetry['uuid'], 'bdf': self.guest_bdf})
                sample = {'session': self.plan['session'], 'sequence': self.policy.sequence + 1,
                          'acquired_ns': sent_ns, 'gpus': {
                              'v100': {'uuid': telemetry['uuid'], 'temperature_c': telemetry['temperature_c']},
                              'rtx5080': {'uuid': host['uuid'], 'temperature_c': host['temperature_c']}}}
                self.policy.accept(sample, now)
                self.emit({'event': 'temperature', 'host_acquired_bound_ns': sent_ns,
                           'guest_acquired_ns': telemetry['guest_acquired_ns'],
                           'v100_c': telemetry['temperature_c'], 'rtx5080_c': host['temperature_c']})
            elif state not in ('ARMED_COLD', 'COLD_PENDING'):
                raise Refusal('guest_telemetry_unavailable')
            if state == 'RPC_STARTING':
                self.rpc_started = True
            if (state == 'RPC_ALIVE' and self.rpc_started and self.qmp_attached
                    and self.helper_bound and not self.worker_started):
                if type(frame.get('rpc_pid')) is not int or frame['rpc_pid'] <= 1:
                    raise Refusal('guest_rpc_identity_missing')
                if not self.policy.inference_permitted(now):
                    raise Refusal('inference_without_safe_baseline')
                self.worker.start(self.plan, start_ns=self.policy.start_ns)
                self.worker_started = True
                self.emit({'event': 'host_worker_started', 'guest_rpc_pid': frame['rpc_pid']})

    def run(self):
        if not self.worker.prepared():
            raise Refusal('host_worker_containment_not_prepared')
        # Host-only observation is separately authorized and must be ready first.
        self._host_sample(self.clock())
        start = self.clock()
        self.policy = ColdStartPolicy(self.plan['session'], start, self.plan['host_gpu_uuid'])
        self.helper_last_ack = start
        next_poll = start; next_heartbeat = start
        reason = 'controller_failure'
        try:
            self._guest_send('arm', start)  # First timed operation, before any bind.
            while True:
                now = self.clock(); self.policy.tick(now)
                host = self._host_sample(now)
                if now >= next_heartbeat:
                    if self.helper_bind_sent:
                        self._helper_send('heartbeat', now)
                    self.worker.heartbeat(now)
                    next_heartbeat = now + SAMPLE_NS
                for frame in self.helper.drain():
                    if frame.get('session') != self.plan['session']:
                        raise Refusal('helper_session_mismatch')
                    if frame.get('event') == 'bound':
                        if self.helper_bound:
                            raise Refusal('duplicate_helper_bind')
                        self.helper_bound = True
                        self.qmp.attach_async()
                    elif frame.get('event') != 'alive':
                        self.helper_terminal = frame if frame.get('event') == 'stopped' else None
                        raise Refusal('helper_stopped_or_failed')
                    self.helper_last_ack = now
                if now - self.helper_last_ack > STALE_NS:
                    raise Refusal('helper_ack_stale')
                if self.helper_bound and not self.qmp_attached:
                    state = self.qmp.poll()
                    if state == 'attached': self.qmp_attached = True
                    elif state == 'failed': raise Refusal('qmp_attachment_failed')
                self._guest_frames(now, host)
                if self.pending is not None and now - self.pending[1] > STALE_NS:
                    raise Refusal('guest_response_missing')
                if self.guest_armed and self.pending is None and now >= next_poll:
                    command = 'start' if self.policy.initial_v100 is not None and not self.rpc_started else 'poll'
                    self._guest_send(command, now)
                    next_poll = now + SAMPLE_NS
                if self.worker_started:
                    state = self.worker.poll()
                    if state.get('state') == 'failed': raise Refusal('host_inference_failed')
                    if state.get('state') == 'completed':
                        self.result = state.get('result')
                        if (not isinstance(self.result, dict) or type(self.result.get('content')) is not str
                                or type(self.result.get('tokens_predicted')) is not int
                                or not 1 <= self.result['tokens_predicted'] <= 8):
                            raise Refusal('invalid_completion_evidence')
                        reason = 'one_completion_returned'
                        break
                self.sleep(0.025)
        except (Refusal, OSError, ValueError, KeyError, subprocess.SubprocessError) as error:
            reason = str(error)
        finally:
            # Signal independent stoppers before waiting for any slower teardown.
            for function in (
                lambda: self._guest_send('stop', self.clock()),
                lambda: self._helper_send('stop', self.clock()),
                self.worker.request_stop,
            ):
                try: function()
                except (OSError, Refusal, ValueError): pass
            while self.clock() - start < TOTAL_NS:
                try:
                    for frame in self.helper.drain():
                        if frame.get('event') == 'stopped' and frame.get('session') == self.plan['session']:
                            self.helper_terminal = frame
                except (OSError, Refusal): pass
                try:
                    for frame in self.guest.drain():
                        if frame.get('event') == 'stopped': self.guest_stop_ack = True
                except (OSError, Refusal): pass
                if self.helper_terminal is not None and self.worker.is_stopped(): break
                self.sleep(0.025)
            try: self.sensor.stop()
            except (OSError, Refusal, subprocess.TimeoutExpired): pass
        elapsed = self.clock() - start
        terminal = self.helper_terminal or {}
        stopped = (terminal.get('qemu_stopped') is True and terminal.get('binding_restored') is True
                   and self.worker.is_stopped() and elapsed <= TOTAL_NS)
        outcome = {'proof_id': PROOF_ID, 'reason': reason, 'elapsed_ns': elapsed,
                   'stopped': stopped, 'guest_stop_ack': self.guest_stop_ack,
                   'helper_terminal': terminal, 'result': self.result,
                   'host_worker_terminal': getattr(self.worker, 'terminal', None),
                   'proof': 'NOT_PROVEN', 'physical_gpu_quiescence': 'UNVERIFIED',
                   'note': 'Completion/allocation/residency alone do not prove actual work on both GPUs.'}
        self.emit({'event': 'terminal', **outcome})
        return outcome


def run_proof(plan, *, admission, deployment_bytes, helper, guest, qmp, sensor, worker,
              clock=time.monotonic_ns, sleep=time.sleep, emit=lambda event: None):
    """Explicit root integration entrypoint; read-only preflight precedes timing."""
    validate_plan(plan)
    verify_admission(plan, admission, deployment_bytes)
    owner_path = Path(__file__).resolve().parents[3] / '9to1 Workspace/Home/Source/Dulche/llamacpp'
    sys.path.insert(0, str(owner_path))
    from model_lease import acquire_model_lease
    lease = acquire_model_lease(Path(plan['lease_runtime_dir']), MODEL_ID, exclusive=False, blocking=False)
    try:
        pins = [plan['model'], plan['host_executable'], *plan['pinned_libraries']]
        for pin in pins: verify_pin(pin)
        return Controller(plan, admission, helper, guest, qmp, sensor, worker,
                          clock=clock, sleep=sleep, emit=emit).run()
    finally:
        if worker.is_stopped():
            lease.release()
        else:
            # Preserve live-model custody for the still-running owned worker.
            _RETAINED_LEASES.append(lease)


_RETAINED_LEASES = []


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--plan', required=True, type=Path)
    parser.add_argument('--plan-sha256', required=True)
    args = parser.parse_args()
    plan, raw = pinned_plan(args.plan, args.plan_sha256)
    print(json.dumps({'status': 'VALIDATED_NOT_EXECUTED', 'proof_id': PROOF_ID,
                      'deployment_sha256': hashlib.sha256(raw).hexdigest(),
                      'worker_argv': fixed_worker_argv(plan)}))


if __name__ == '__main__':
    main()
