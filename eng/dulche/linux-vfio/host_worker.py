"""Prebooted, cgroup-contained host worker guardian; no GPU work before START.

Root creates the user systemd service with MemoryMax=4G, MemorySwapMax=0,
TasksMax=64, CPUQuota=200%, KillMode=control-group, RuntimeMaxSec=180. REAL mode
also requires Type=notify, NotifyAccess=main and WatchdogSec=1s so a frozen
guardian is independently killed by the service manager with its descendants.
checks the cgroup and service settings before readiness. A private Unix socket
is the one-client control barrier. Only the globally claimed controller can
start the fixed pinned llama-server. CPU_MOCK uses only a fixed Python sleeper.
"""
from __future__ import annotations

import argparse
import ctypes
import hashlib
import http.client
import json
import os
from pathlib import Path
import queue
import resource
import secrets
import selectors
import signal
import socket
import struct
import subprocess
import sys
import threading
import time

sys.path.insert(0, str(Path(__file__).resolve().parent))
import proof_authority as authority
import host_proof as host
from proof_safety import Refusal, STALE_NS, WORK_NS, TOTAL_NS

CONTROL = 'dulche.host-worker-control/v1'
MAX_LOG = 4 * 1024**2


def write_once(path: Path, value: dict):
    raw = (json.dumps(value, sort_keys=True, allow_nan=False) + '\n').encode()
    pending = path.with_name(path.name + '.pending-' + secrets.token_hex(8))
    fd = os.open(pending, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o400)
    with os.fdopen(fd, 'wb') as stream:
        stream.write(raw);stream.flush();os.fsync(stream.fileno())
    # Publish complete bytes without clobbering; retain the original hardlink as
    # evidence rather than expose a partly written terminal record to the client.
    os.link(pending, path, follow_symlinks=False)
    directory = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try: os.fsync(directory)
    finally: os.close(directory)


def cgroup_evidence(unit: str) -> dict:
    if not unit.startswith('dulche-proof-host-') or not unit.endswith('.service'):
        raise Refusal('unexpected_worker_service_name')
    lines = Path('/proc/self/cgroup').read_text().splitlines()
    unified = [line[3:] for line in lines if line.startswith('0::')]
    if len(unified) != 1 or not unified[0].endswith('/' + unit):
        raise Refusal('worker_not_in_expected_systemd_cgroup')
    group = Path('/sys/fs/cgroup') / unified[0].lstrip('/')
    values = {key: (group / key).read_text().strip()
              for key in ('memory.max', 'memory.swap.max', 'pids.max', 'cpu.max')}
    try:
        quota, period = map(int, values['cpu.max'].split())
        if not (0 < int(values['memory.max']) <= 4 * 1024**3
                and int(values['memory.swap.max']) == 0 and 0 < int(values['pids.max']) <= 64
                and 0 < quota <= 2 * period):
            raise ValueError('caps differ')
    except ValueError as error:
        raise Refusal('worker_cgroup_caps_unverified') from error
    result = subprocess.run(['/usr/bin/systemctl', '--user', 'show', unit,
                             '--property=KillMode,RuntimeMaxUSec,MainPID,Type,NotifyAccess,WatchdogUSec'],
                            capture_output=True, text=True, timeout=2, check=True)
    properties = dict(line.split('=', 1) for line in result.stdout.splitlines() if '=' in line)
    if properties.get('KillMode') != 'control-group' or properties.get('MainPID') != str(os.getpid()):
        raise Refusal('worker_unit_ownership_unverified')
    # systemd renders the requested 180 seconds as 3min or 3min 0s.
    if properties.get('RuntimeMaxUSec') not in ('3min', '3min 0s', '180s'):
        raise Refusal('worker_service_runtime_limit_unverified')
    if (properties.get('Type') != 'notify' or properties.get('NotifyAccess') != 'main'
            or properties.get('WatchdogUSec') != '1s'):
        raise Refusal('worker_service_watchdog_unverified')
    identity = group.stat()
    return {'path': str(group), 'device': identity.st_dev, 'inode': identity.st_ino,
            'values': values, 'unit': unit, 'properties': properties}


def notify_service(value: bytes):
    address = os.environ.get('NOTIFY_SOCKET')
    if not address or not address.startswith(('/', '@')):
        raise Refusal('systemd_notify_socket_missing')
    if address.startswith('@'): address = '\0' + address[1:]
    with socket.socket(socket.AF_UNIX, socket.SOCK_DGRAM) as channel:
        channel.settimeout(0.1)
        if channel.sendto(value, address) != len(value): raise Refusal('systemd_notify_incomplete')


def runtime_pins(plan: dict) -> tuple[dict, dict]:
    stats = {}
    for pin in (plan['model'], plan['host_executable'], *plan['pinned_libraries']):
        if pin['path'] in stats: raise Refusal('duplicate_worker_file_pin')
        stats[pin['path']] = host.verify_pin(pin)
    aliases = {}
    for directory in plan['library_directories']:
        if not any(Path(path).parent == Path(directory) for path in stats):
            raise Refusal('unrepresented_worker_library_directory')
        for entry in Path(directory).glob('*.so*'):
            if entry.is_symlink():
                if str(entry.resolve(strict=True)) not in stats:
                    raise Refusal('unpinned_worker_library_alias')
                aliases[str(entry)] = os.readlink(entry)
            elif str(entry) not in stats:
                raise Refusal('unpinned_worker_library')
    return stats, aliases


def verify_start_authority(plan: dict, peer_pid: int) -> dict:
    consumed_bytes, consumed, dispatch_bytes, dispatch = authority._dispatch_record()
    _, publication = authority._read('DISPATCH-PUBLICATION.json')
    _, execution = authority._read('EXECUTION-STARTED.json')
    expected_owner = {**authority.process_identity(peer_pid), 'session': plan['session']}
    if (dispatch['owner'] != expected_owner or execution.get('owner') != expected_owner
            or publication.get('owner') != expected_owner
            or execution.get('schema') != 'dulche.v100-execution-started/v1'
            or publication.get('schema') != 'dulche.v100-dispatch-publication/v1'
            or execution.get('proof_id') != authority.PROOF_ID or execution.get('no_retry') is not True
            or consumed['manifest_sha256'] != plan['guest_manifest_sha256']
            or consumed['host_gpu_uuid'] != plan['host_gpu_uuid']
            or execution.get('consumed_sha256') != hashlib.sha256(consumed_bytes).hexdigest()
            or execution.get('dispatch_sha256') != hashlib.sha256(dispatch_bytes).hexdigest()
            or execution.get('dispatch_commit_sha') != publication.get('dispatch_commit_sha')):
        raise Refusal('worker_start_authority_mismatch')
    return expected_owner


class UnixHttp(http.client.HTTPConnection):
    def __init__(self, path: str, timeout=0.4):
        super().__init__('localhost', timeout=timeout);self.path = path
    def connect(self):
        self.sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.sock.settimeout(self.timeout);self.sock.connect(self.path)


def completion_once(plan: dict, start_ns: int, cancelled: threading.Event, result: queue.Queue):
    """Readiness polling, then exactly one inference POST. No retry after POST."""
    try:
        while not cancelled.is_set() and time.monotonic_ns() - start_ns < WORK_NS:
            try:
                with UnixHttp(plan['http_socket'], 0.2) as connection:
                    connection.request('GET', '/health')
                    response = connection.getresponse();response.read(65537)
                    if response.status == 200: break
                    if response.status != 503: raise Refusal('worker_health_failed')
            except (FileNotFoundError, ConnectionRefusedError, socket.timeout):
                pass
            cancelled.wait(0.05)
        else: raise Refusal('worker_not_ready_before_deadline')
        if cancelled.is_set(): raise Refusal('worker_cancelled_before_request')
        payload = json.dumps({'prompt': 'Complete briefly: One plus one is', 'n_predict': 8,
                              'temperature': 0, 'stream': False, 'cache_prompt': False})
        with UnixHttp(plan['http_socket'], 0.5) as connection:
            connection.request('POST', '/completion', body=payload,
                               headers={'Content-Type': 'application/json'})
            response = connection.getresponse();raw = response.read(65537)
            if response.status != 200: raise Refusal('completion_http_failure')
            value = host.decode(raw)
            if (type(value.get('content')) is not str or type(value.get('tokens_predicted')) is not int
                    or not 1 <= value['tokens_predicted'] <= 8):
                raise Refusal('completion_result_invalid')
        result.put({'state': 'completed', 'result': value})
    except Exception as error:
        result.put({'state': 'failed', 'reason': str(error)})


def _child_exec(parent: int, plan_path: Path, plan_sha: str, claim_sha: str):
    libc = ctypes.CDLL(None, use_errno=True)
    if libc.prctl(1, signal.SIGKILL, 0, 0, 0) != 0 or os.getppid() != parent:
        raise Refusal('host_child_parent_changed')
    plan, _ = host.pinned_plan(plan_path, plan_sha)
    data, claim = authority._read('HOST-WORKER-CLAIMED.json')
    if (hashlib.sha256(data).hexdigest() != claim_sha or claim.get('guardian_pid') != parent
            or claim.get('plan_sha256') != plan_sha or claim.get('session') != plan['session']):
        raise Refusal('host_child_claim_mismatch')
    for path, expected in claim['file_stats'].items():
        if list(host._file_identity(Path(path))) != expected: raise Refusal('host_child_file_changed')
    for path, target in claim['library_aliases'].items():
        if not Path(path).is_symlink() or os.readlink(path) != target:
            raise Refusal('host_child_library_alias_changed')
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    resource.setrlimit(resource.RLIMIT_FSIZE, (MAX_LOG, MAX_LOG))
    env = {'PATH': '/usr/bin:/bin', 'LANG': 'C', 'CUDA_VISIBLE_DEVICES': plan['host_gpu_uuid'],
           'LD_LIBRARY_PATH': ':'.join(plan['library_directories']),
           'OMP_NUM_THREADS': '2', 'OPENBLAS_NUM_THREADS': '2', 'MKL_NUM_THREADS': '2',
           'GGML_RPC_NO_RDMA': '1', 'GGML_RPC_NO_COMM': '1'}
    os.execve(plan['host_executable']['path'], host.fixed_worker_argv(plan), env)


def _send(connection: socket.socket, value: dict):
    raw = (json.dumps(value, separators=(',', ':'), allow_nan=False) + '\n').encode()
    if len(raw) > 65536 or connection.send(raw) != len(raw):
        raise Refusal('host_worker_control_write_failed')


def guardian(plan: dict, plan_path: Path, plan_sha: str, *, unit: str | None, mock: bool):
    runtime = Path(plan['runtime_directory']);control = runtime / 'host-worker-control.sock'
    if control.exists() or control.is_symlink(): raise Refusal('host_worker_socket_exists')
    kind = 'CPU_MOCK' if mock else 'REAL'
    managed = unit is not None
    if not mock and not managed: raise Refusal('real_worker_requires_systemd_manager')
    cgroup = cgroup_evidence(unit) if managed else {'cpu_mock': True}
    lease = None
    if mock:
        stats, aliases = {}, {}
    else:
        owner_path = Path(__file__).resolve().parents[3] / '9to1 Workspace/Home/Source/Dulche/llamacpp'
        sys.path.insert(0, str(owner_path))
        from model_lease import acquire_model_lease
        lease = acquire_model_lease(Path(plan['lease_runtime_dir']), host.MODEL_ID, exclusive=False, blocking=False)
        try: stats, aliases = runtime_pins(plan)
        except BaseException:
            lease.release();raise
    identity = authority.process_identity()
    write_once(runtime / 'host-worker-ready.json', {'schema': CONTROL, 'kind': kind,
               'guardian': identity, 'plan_sha256': plan_sha, 'session': plan['session'], 'cgroup': cgroup})
    server = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
    old_umask = os.umask(0o077)
    try: server.bind(str(control))
    finally: os.umask(old_umask)
    server.listen(1);server.setblocking(False)
    connection = None;peer_pid = None;child = None;buffer = b'';sequence = 0
    start = None;last_host = None;last_sent = -1;opened = time.monotonic_ns()
    outcome = {'state': 'idle'};reason = 'not_started';stopped = True
    cancellation = threading.Event();results = queue.Queue(maxsize=1)
    log = None;http_thread = None;next_notify = 0
    try:
        with selectors.DefaultSelector() as selected:
            selected.register(server, selectors.EVENT_READ)
            if managed: notify_service(b'READY=1')
            while True:
                now = time.monotonic_ns()
                if managed and now >= next_notify:
                    notify_service(b'WATCHDOG=1');next_notify = now + 250_000_000
                if start is None:
                    if now - opened >= 180_000_000_000: raise Refusal('host_worker_prepare_timeout')
                else:
                    if now - start >= WORK_NS: raise Refusal('host_worker_work_deadline')
                    if now - last_host > STALE_NS: raise Refusal('host_worker_heartbeat_stale')
                    if child.poll() is not None: raise Refusal('host_backend_exited')
                    if log is not None and os.fstat(log.fileno()).st_size > MAX_LOG:
                        raise Refusal('host_backend_log_limit')
                    try:
                        report = results.get_nowait()
                        outcome = report;_send(connection, {'schema': CONTROL, 'kind': kind, **report})
                        if report['state'] == 'failed': raise Refusal(report.get('reason', 'completion_failed'))
                    except queue.Empty: pass
                for key, _ in selected.select(0.025):
                    if key.fileobj is server:
                        candidate, _ = server.accept()
                        if connection is not None: candidate.close();raise Refusal('second_host_worker_client')
                        credentials = candidate.getsockopt(socket.SOL_SOCKET, socket.SO_PEERCRED, 12)
                        peer_pid, uid, _ = struct.unpack('3i', credentials)
                        if uid != os.getuid(): candidate.close();raise Refusal('host_worker_peer_uid')
                        connection = candidate;connection.setblocking(False)
                        selected.register(connection, selectors.EVENT_READ)
                        _send(connection, {'schema': CONTROL, 'kind': kind, 'state': 'ready',
                              'guardian': identity, 'plan_sha256': plan_sha, 'session': plan['session'],
                              'cgroup_verified': managed, 'cgroup': cgroup})
                    else:
                        raw = connection.recv(8192)
                        if not raw: raise Refusal('host_worker_controller_disconnected')
                        buffer += raw
                        if len(buffer) > 8192: raise Refusal('host_worker_frame_too_large')
                        while b'\n' in buffer:
                            line, buffer = buffer.split(b'\n', 1);message = host.decode(line)
                            fields = {'schema', 'action', 'session', 'sequence', 'sent_ns'}
                            if message.get('action') == 'start': fields.add('start_ns')
                            now = time.monotonic_ns();sent = message.get('sent_ns')
                            if (set(message) != fields or message.get('schema') != CONTROL
                                    or message.get('session') != plan['session']
                                    or type(message.get('sequence')) is not int or message['sequence'] != sequence
                                    or type(sent) is not int or sent <= last_sent or not 0 <= now - sent <= STALE_NS
                                    or message['action'] not in ('start', 'heartbeat', 'stop')):
                                raise Refusal('host_worker_control_invalid_or_replayed')
                            sequence += 1;last_sent = sent;last_host = sent
                            if message['action'] == 'stop': reason = 'explicit_stop';raise StopIteration
                            if message['action'] == 'start':
                                if start is not None: raise Refusal('host_worker_already_started')
                                start = message['start_ns']
                                if type(start) is not int or not 0 <= now - start < WORK_NS:
                                    raise Refusal('host_worker_global_start_invalid')
                                if not mock:
                                    owner = verify_start_authority(plan, peer_pid)
                                    for path, expected in stats.items():
                                        if host._file_identity(Path(path)) != expected: raise Refusal('worker_pin_changed')
                                    claim = {'schema': 'dulche.host-worker-claimed/v1', 'proof_id': authority.PROOF_ID,
                                             'session': plan['session'], 'plan_sha256': plan_sha, 'owner': owner,
                                             'guardian_pid': os.getpid(), 'guardian_start_ticks': identity['controller_start_ticks'],
                                             'start_ns': start, 'file_stats': stats, 'library_aliases': aliases}
                                    claim_sha = authority._write_once('HOST-WORKER-CLAIMED.json', claim)
                                    log = open(runtime / 'host-backend.log', 'xb', buffering=0)
                                    child = subprocess.Popen([sys.executable, '-I', '-S', '-B', str(Path(__file__).resolve()),
                                             '__exec', str(os.getpid()), str(plan_path), plan_sha, claim_sha],
                                             stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT,
                                             env={'PATH': '/usr/bin:/bin', 'LANG': 'C'}, start_new_session=True)
                                    http_thread = threading.Thread(target=completion_once,
                                             args=(plan, start, cancellation, results), daemon=True)
                                    http_thread.start()
                                else:
                                    child = subprocess.Popen([sys.executable, '-I', '-S', '-B', str(Path(__file__).resolve()),
                                             '__mock_child', str(os.getpid())], stdin=subprocess.DEVNULL,
                                             stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                                             env={'PATH': '/usr/bin:/bin', 'LANG': 'C', 'CUDA_VISIBLE_DEVICES': '-1'},
                                             start_new_session=True)
                                outcome = {'state': 'loading'}
                                _send(connection, {'schema': CONTROL, 'kind': kind, 'state': 'loading', 'backend_pid': child.pid})
    except StopIteration: pass
    except (Refusal, OSError, ValueError, subprocess.SubprocessError) as error:
        reason = str(error)
    finally:
        cancellation.set()
        if child is not None:
            deadline = start + TOTAL_NS if start is not None else time.monotonic_ns() + 1_000_000_000
            if child.poll() is None:
                child.terminate()
                try: child.wait(timeout=min(0.2, max(0, (deadline-time.monotonic_ns())/1e9)))
                except subprocess.TimeoutExpired: child.kill()
            try: child.wait(timeout=max(0, min(0.5, (deadline-time.monotonic_ns())/1e9)))
            except subprocess.TimeoutExpired: stopped = False
        terminal = {'schema': CONTROL, 'kind': kind, 'state': 'stopped', 'stopped': stopped,
                    'reason': reason, 'session': plan['session'], 'guardian': identity,
                    'plan_sha256': plan_sha, 'backend_pid': None if child is None else child.pid,
                    'backend_exit_code': None if child is None else child.returncode,
                    'elapsed_ns': 0 if start is None else time.monotonic_ns() - start}
        write_once(runtime / 'host-worker-terminal.json', terminal)
        if connection is not None:
            try: _send(connection, terminal)
            except (OSError, Refusal): pass
            connection.close()
        if log is not None: log.close()
        server.close()
        if lease is not None and stopped: lease.release()
    return 0 if stopped else 2


class OwnedWorker:
    """Nonblocking client for a root-prepared user-systemd guardian service."""
    def __init__(self, plan: dict, plan_sha: str, *, cpu_mock=False):
        self.plan = plan;self.plan_sha = plan_sha;self.kind = 'CPU_MOCK' if cpu_mock else 'REAL'
        self.path = Path(plan['runtime_directory']);self.sequence = 0;self.buffer = b''
        self.connection = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.connection.settimeout(0.5);self.connection.connect(str(self.path / 'host-worker-control.sock'))
        peer = self.connection.getsockopt(socket.SOL_SOCKET, socket.SO_PEERCRED, 12)
        self.server_pid, server_uid, _ = struct.unpack('3i', peer)
        if server_uid != os.getuid(): raise Refusal('worker_server_uid_mismatch')
        self.connection.setblocking(False);self.state = {'state': 'idle'};self.ready = False
        self.terminal = None;self.guardian = None;self.eof = False;self.cgroup = None

    def _send(self, action, *, now=None, start_ns=None):
        message = {'schema': CONTROL, 'action': action, 'session': self.plan['session'],
                   'sequence': self.sequence, 'sent_ns': time.monotonic_ns() if now is None else now}
        if start_ns is not None: message['start_ns'] = start_ns
        _send(self.connection, message);self.sequence += 1

    def _drain(self):
        while not self.eof:
            try: raw = self.connection.recv(8192)
            except BlockingIOError: break
            except ConnectionResetError:
                self.eof = True;break
            if not raw: self.eof = True;break
            self.buffer += raw
            if len(self.buffer) > 65536: raise Refusal('worker_client_output_bound')
            while b'\n' in self.buffer:
                line, self.buffer = self.buffer.split(b'\n', 1);frame = host.decode(line)
                if frame.get('schema') != CONTROL or frame.get('kind') != self.kind:
                    raise Refusal('worker_client_identity_mismatch')
                state = frame.get('state')
                if state == 'ready':
                    if frame.get('plan_sha256') != self.plan_sha or frame.get('session') != self.plan['session']:
                        raise Refusal('worker_client_plan_mismatch')
                    self.guardian = frame['guardian']
                    if self.guardian != authority.process_identity(self.server_pid):
                        raise Refusal('worker_server_process_identity_mismatch')
                    self.cgroup = frame.get('cgroup')
                    self.ready = self.kind == 'CPU_MOCK' or frame.get('cgroup_verified') is True
                elif state == 'stopped': self.terminal = frame
                elif state in ('loading', 'completed', 'failed'): self.state = frame
                else: raise Refusal('worker_client_unknown_state')
        if self.terminal is None and (self.path / 'host-worker-terminal.json').is_file():
            frame = host.decode((self.path / 'host-worker-terminal.json').read_bytes())
            if (frame.get('session') != self.plan['session'] or frame.get('plan_sha256') != self.plan_sha
                    or frame.get('guardian') != self.guardian or frame.get('kind') != self.kind):
                raise Refusal('worker_terminal_custody_mismatch')
            self.terminal = frame
        # A systemd watchdog kill cannot write a guardian terminal file. Verify
        # both old guardian cessation and the exact owned cgroup being empty (or
        # removed after emptiness), rather than treating EOF alone as a stop.
        if self.terminal is None and self.eof and self.kind == 'REAL' and self.cgroup:
            try:
                text = (Path('/proc') / str(self.guardian['controller_pid']) / 'stat').read_text()
                fields = text.rsplit(')', 1)[1].split()
                guardian_running = (int(fields[19]) == self.guardian['controller_start_ticks']
                                    and fields[0] not in ('Z', 'X'))
            except FileNotFoundError:
                guardian_running = False
            group = Path(self.cgroup['path'])
            empty = False
            try:
                identity = group.stat()
                if (identity.st_dev, identity.st_ino) != (self.cgroup['device'], self.cgroup['inode']):
                    raise Refusal('worker_cgroup_identity_changed')
            except FileNotFoundError:
                empty = True
            else:
                try:
                    events = dict(line.split() for line in (group / 'cgroup.events').read_text().splitlines())
                    empty = events.get('populated') == '0'
                except FileNotFoundError:
                    # Events missing from a still-existing group is unknown.
                    empty = not group.exists()
            if not guardian_running and empty:
                self.terminal = {'schema': CONTROL, 'kind': self.kind, 'state': 'stopped',
                                 'stopped': True, 'reason': 'independent_systemd_cgroup_empty',
                                 'session': self.plan['session'], 'plan_sha256': self.plan_sha,
                                 'guardian': self.guardian, 'cgroup': self.cgroup}

    def prepared(self): self._drain();return self.ready and self.terminal is None
    def start(self, plan, *, start_ns):
        if plan != self.plan or not self.prepared(): raise Refusal('worker_start_plan_or_readiness')
        self._send('start', start_ns=start_ns)
    def heartbeat(self, now): self._send('heartbeat', now=now)
    def poll(self):
        self._drain()
        if self.terminal is not None: return {'state': 'failed', 'reason': self.terminal.get('reason')}
        return self.state
    def request_stop(self):
        if self.terminal is None and not self.eof: self._send('stop')
    def is_stopped(self):
        self._drain()
        return self.terminal is not None and self.terminal.get('state') == 'stopped' and self.terminal.get('stopped') is True
    def close(self): self.connection.close()


def main():
    if len(sys.argv) > 1 and sys.argv[1] == '__mock_child':
        libc = ctypes.CDLL(None, use_errno=True)
        if libc.prctl(1, signal.SIGKILL, 0, 0, 0) != 0 or os.getppid() != int(sys.argv[2]): return 126
        time.sleep(60);return 0
    if len(sys.argv) > 1 and sys.argv[1] == '__exec':
        _child_exec(int(sys.argv[2]), Path(sys.argv[3]), sys.argv[4], sys.argv[5]);return 126
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--plan', type=Path, required=True);parser.add_argument('--plan-sha256', required=True)
    parser.add_argument('--unit');parser.add_argument('--cpu-mock', action='store_true')
    args = parser.parse_args()
    if args.cpu_mock:
        raw = args.plan.read_bytes()
        if hashlib.sha256(raw).hexdigest() != args.plan_sha256: raise Refusal('mock_plan_hash')
        plan = host.decode(raw)
        path = Path(plan['runtime_directory'])
        if not path.is_absolute() or path.is_symlink() or not path.is_dir() or path.stat().st_mode & 0o077:
            raise Refusal('mock_private_directory_required')
    else:
        plan, _ = host.pinned_plan(args.plan, args.plan_sha256)
        if not args.unit: raise Refusal('verified_systemd_unit_required')
    return guardian(plan, args.plan, args.plan_sha256, unit=args.unit, mock=args.cpu_mock)


if __name__ == '__main__':
    raise SystemExit(main())
