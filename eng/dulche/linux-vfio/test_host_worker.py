"""Actual disposable CPU guardian children; no NVML, QEMU or inference binary."""
import hashlib
import json
import os
from pathlib import Path
import signal
import socket
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

import host_worker as worker
from proof_safety import Refusal, WORK_NS


class WorkerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='dulche-host-worker-cpu-')
        self.addCleanup(self.temp.cleanup)
        self.runtime = Path(self.temp.name) / 'private';self.runtime.mkdir(mode=0o700)
        self.plan = {'runtime_directory': str(self.runtime), 'session': 'a' * 32}
        self.plan_path = Path(self.temp.name) / 'plan.json'
        self.plan_path.write_text(json.dumps(self.plan))
        self.sha = hashlib.sha256(self.plan_path.read_bytes()).hexdigest()
        self.process = subprocess.Popen([sys.executable, '-I', '-S', '-B', worker.__file__,
            '--plan', str(self.plan_path), '--plan-sha256', self.sha, '--cpu-mock'],
            stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE,
            env={'PATH': '/usr/bin:/bin', 'LANG': 'C', 'CUDA_VISIBLE_DEVICES': '-1'})
        self.client = None
        deadline = time.monotonic() + 2
        while not (self.runtime / 'host-worker-control.sock').exists():
            if self.process.poll() is not None:
                raise AssertionError(self.process.stderr.read().decode())
            if time.monotonic() > deadline: raise AssertionError('CPU guardian socket missing')
            time.sleep(0.01)
        self.client = worker.OwnedWorker(self.plan, self.sha, cpu_mock=True)
        while not self.client.prepared():
            if time.monotonic() > deadline: raise AssertionError('CPU guardian not ready')
            time.sleep(0.01)

    def tearDown(self):
        if self.client is not None:
            try: self.client.request_stop()
            except (OSError, Refusal): pass
            self.client.close()
        if self.process.poll() is None:
            try: self.process.wait(timeout=1)
            except subprocess.TimeoutExpired:
                self.process.kill();self.process.wait(timeout=1)
        self.process.stderr.close()

    def await_terminal(self, timeout=2, heartbeat=False):
        deadline = time.monotonic() + timeout
        while not self.client.is_stopped():
            if time.monotonic() > deadline: raise AssertionError('CPU guardian did not stop')
            if heartbeat:
                try: self.client.heartbeat(time.monotonic_ns())
                except (OSError, Refusal): pass
            time.sleep(0.025)
        self.process.wait(timeout=1)
        return self.client.terminal

    def await_backend(self):
        deadline = time.monotonic() + 1
        while True:
            state = self.client.poll()
            if state.get('backend_pid'): return state['backend_pid']
            if time.monotonic() > deadline: raise AssertionError('CPU backend not acknowledged')
            time.sleep(0.01)

    def test_prepared_service_has_no_backend_before_start(self):
        self.client.request_stop()
        terminal = self.await_terminal()
        self.assertIsNone(terminal['backend_pid'])
        self.assertEqual(terminal['kind'], 'CPU_MOCK')
        self.assertFalse((self.runtime / 'host-backend.log').exists())

    def test_heartbeat_loss_stops_actual_owned_cpu_child(self):
        self.client.start(self.plan, start_ns=time.monotonic_ns())
        self.await_backend()
        terminal = self.await_terminal()
        self.assertEqual(terminal['reason'], 'host_worker_heartbeat_stale')
        self.assertTrue(terminal['stopped'])
        self.assertIsNotNone(terminal['backend_exit_code'])
        self.assertLess(terminal['elapsed_ns'], 1_500_000_000)

    def test_global_deadline_is_not_reset_at_later_backend_start(self):
        self.client.start(self.plan, start_ns=time.monotonic_ns() - WORK_NS + 300_000_000)
        terminal = self.await_terminal(heartbeat=True)
        self.assertEqual(terminal['reason'], 'host_worker_work_deadline')
        self.assertGreaterEqual(terminal['elapsed_ns'], WORK_NS)
        self.assertLess(terminal['elapsed_ns'], WORK_NS + 250_000_000)

    def test_stale_control_cannot_start_child(self):
        self.client._send('start', now=time.monotonic_ns() - 800_000_000,
                          start_ns=time.monotonic_ns())
        terminal = self.await_terminal()
        self.assertIsNone(terminal['backend_pid'])
        self.assertEqual(terminal['reason'], 'host_worker_control_invalid_or_replayed')

    def test_no_second_start_or_retry(self):
        self.client.start(self.plan, start_ns=time.monotonic_ns());pid = self.await_backend()
        self.client.start(self.plan, start_ns=time.monotonic_ns())
        terminal = self.await_terminal()
        self.assertEqual(terminal['backend_pid'], pid)
        self.assertEqual(terminal['reason'], 'host_worker_already_started')

    def test_guardian_sigkill_triggers_kernel_child_parent_death(self):
        self.client.start(self.plan, start_ns=time.monotonic_ns());pid = self.await_backend()
        os.kill(self.process.pid, signal.SIGKILL);self.process.wait(timeout=1)
        deadline = time.monotonic() + 1
        while True:
            path = Path('/proc') / str(pid) / 'stat'
            try: state = path.read_text().rsplit(')', 1)[1].split()[0]
            except FileNotFoundError: break
            if state == 'Z': break  # Dead child awaiting its new reaper, not executing.
            if time.monotonic() > deadline: raise AssertionError('CPU child survived guardian death')
            time.sleep(0.01)


class ManagerTests(unittest.TestCase):
    def test_real_mode_refuses_unverified_systemd_scope(self):
        with self.assertRaisesRegex(Refusal, 'unexpected_worker_service_name'):
            worker.cgroup_evidence('unrelated.service')

    def test_watchdog_notification_uses_only_supplied_unix_manager_socket(self):
        with tempfile.TemporaryDirectory() as directory:
            path = str(Path(directory) / 'notify.sock')
            with socket.socket(socket.AF_UNIX, socket.SOCK_DGRAM) as receiver:
                receiver.bind(path);receiver.settimeout(0.2)
                with patch.dict(os.environ, {'NOTIFY_SOCKET': path}):
                    worker.notify_service(b'WATCHDOG=1')
                self.assertEqual(receiver.recv(128), b'WATCHDOG=1')

    def test_manager_fallback_requires_dead_guardian_and_empty_exact_cgroup(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory);group = root / 'cgroup';group.mkdir()
            (group / 'cgroup.events').write_text('populated 1\n')
            identity = group.stat()
            client = object.__new__(worker.OwnedWorker)
            client.eof = True;client.terminal = None;client.kind = 'REAL';client.path = root
            client.plan = {'session': 'a' * 32};client.plan_sha = 'b' * 64
            client.guardian = {'controller_pid': 999999999, 'controller_start_ticks': 1}
            client.cgroup = {'path': str(group), 'device': identity.st_dev, 'inode': identity.st_ino}
            self.assertFalse(client.is_stopped())
            (group / 'cgroup.events').unlink()
            self.assertFalse(client.is_stopped())
            (group / 'cgroup.events').write_text('populated 0\n')
            self.assertTrue(client.is_stopped())
            self.assertEqual(client.terminal['reason'], 'independent_systemd_cgroup_empty')


if __name__ == '__main__':
    unittest.main()
