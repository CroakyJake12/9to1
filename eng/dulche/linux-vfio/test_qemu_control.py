"""Actual QMP/owned-process CPU checks; no guest OS, disks, NICs or PCI devices.

This is a stopper-component check, not evidence of guest/GPU quiescence or of
the complete independent watchdog chain. The executable and arguments are fixed.
"""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import socket
import stat
import subprocess
import sys
import tempfile
import time
import unittest

QEMU = Path('/usr/bin/qemu-system-x86_64')


def executable_identity() -> dict:
    """Observe the installed binary without invoking it; no installation fallback."""
    with QEMU.open('rb') as stream:
        before = os.fstat(stream.fileno())
        if not stat.S_ISREG(before.st_mode) or not os.access(QEMU, os.X_OK):
            raise RuntimeError('Installed QEMU must be an executable regular file')
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        after = os.fstat(stream.fileno())
    identity = lambda item: (item.st_dev, item.st_ino, item.st_size,
                              item.st_mtime_ns, item.st_ctime_ns)
    if identity(before) != identity(after):
        raise RuntimeError('QEMU changed during read-only identity inspection')
    return {'path': str(QEMU), 'sha256': digest, 'bytes': before.st_size,
            'device': before.st_dev, 'inode': before.st_ino,
            'mtime_ns': before.st_mtime_ns, 'ctime_ns': before.st_ctime_ns}


class QmpReader:
    def __init__(self, connection: socket.socket, deadline: float):
        self.connection = connection
        self.deadline = deadline
        self.buffer = b''

    def message(self) -> dict:
        while b'\n' not in self.buffer:
            remaining = self.deadline - time.monotonic()
            if remaining <= 0:
                raise TimeoutError('QMP response deadline')
            self.connection.settimeout(remaining)
            data = self.connection.recv(4096)
            if not data:
                raise EOFError('QMP closed before the required response')
            self.buffer += data
            if len(self.buffer) > 65536:
                raise ValueError('QMP fixture frame exceeded its bound')
        line, self.buffer = self.buffer.split(b'\n', 1)
        message = json.loads(line)
        if not isinstance(message, dict):
            raise ValueError('QMP fixture requires an object')
        return message

    def execute(self, name: str, request_id: str) -> dict:
        self.connection.settimeout(max(0.001, self.deadline - time.monotonic()))
        self.connection.sendall((json.dumps({'execute': name, 'id': request_id}) + '\r\n').encode())
        for _ in range(16):
            message = self.message()
            if message.get('id') == request_id:
                if 'error' in message or 'return' not in message:
                    raise ValueError('QMP command failed: ' + repr(message))
                return message
        raise ValueError('Too many unrelated QMP fixture messages')


def stop_owned(process: subprocess.Popen) -> None:
    """Only an actual direct-child handle is accepted, never a stored PID/name."""
    if process.poll() is None:
        process.terminate()
        try:
            process.wait(timeout=0.2)
        except subprocess.TimeoutExpired:
            process.kill()
    process.wait(timeout=1)


class QemuControlTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.identity = executable_identity()
        print('QEMU_IDENTITY ' + json.dumps(cls.identity, sort_keys=True), flush=True)

    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix='dulche-qmp-cpu-')
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.process = None
        self.sentinel = None

    def tearDown(self):
        for process in (self.process, self.sentinel):
            if process is not None:
                stop_owned(process)
                if process.stderr is not None:
                    process.stderr.close()
        self.assertEqual(executable_identity(), self.identity)

    def start_qemu(self) -> Path:
        self.assertEqual(executable_identity(), self.identity)
        qmp_path = self.root / 'qmp.sock'
        self.process = subprocess.Popen(
            [str(QEMU), '-machine', 'none', '-accel', 'tcg', '-m', '64M', '-S',
             '-nodefaults', '-no-user-config', '-display', 'none', '-monitor', 'none',
             '-serial', 'none', '-parallel', 'none', '-nic', 'none',
             '-qmp', f'unix:{qmp_path},server=on,wait=off'],
            stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE,
            env={'PATH': '/usr/bin:/bin', 'LANG': 'C', 'CUDA_VISIBLE_DEVICES': '-1'},
            cwd=self.root, start_new_session=True, umask=0o077,
        )
        return qmp_path

    def connect_owned(self, path: Path, deadline: float) -> socket.socket:
        while time.monotonic() < deadline:
            self.assertIsNone(self.process.poll(), 'fixed QEMU fixture exited before QMP readiness')
            connection = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
            connection.settimeout(max(0.001, deadline - time.monotonic()))
            try:
                connection.connect(str(path))
                return connection
            except (FileNotFoundError, ConnectionRefusedError):
                connection.close()
                time.sleep(0.005)
        raise TimeoutError('Fixed QEMU did not expose its private QMP socket')

    def test_qmp_capabilities_and_quit_stop_actual_owned_qemu(self):
        started = time.monotonic()
        path = self.start_qemu()
        with self.connect_owned(path, started + 2) as connection:
            reader = QmpReader(connection, started + 3)
            self.assertIn('QMP', reader.message())
            self.assertEqual(reader.execute('qmp_capabilities', 'fixture-cap')['return'], {})
            self.assertEqual(reader.execute('quit', 'fixture-quit')['return'], {})
        self.assertEqual(self.process.wait(timeout=1), 0)
        elapsed = time.monotonic() - started
        self.assertLess(elapsed, 5)
        print('QMP_CPU_STOP ' + json.dumps({'method': 'qmp_quit', 'seconds': elapsed,
                                           'exit_code': self.process.returncode,
                                           'guest_os': False, 'gpu_attached': False}), flush=True)

    def test_failed_connection_falls_back_only_to_owned_handle(self):
        started = time.monotonic()
        self.sentinel = subprocess.Popen(
            [sys.executable, '-I', '-S', '-B', '-c', 'import time; time.sleep(30)'],
            stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
            env={'PATH': '/usr/bin:/bin', 'LANG': 'C', 'CUDA_VISIBLE_DEVICES': '-1'},
        )
        path = self.start_qemu()
        # Prove that QEMU is live before intentionally using an absent endpoint.
        with self.connect_owned(path, started + 2) as connection:
            self.assertIn('QMP', QmpReader(connection, started + 3).message())
        with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as failed:
            failed.settimeout(0.1)
            with self.assertRaises(FileNotFoundError):
                failed.connect(str(self.root / 'absent-qmp.sock'))
        stop_owned(self.process)
        self.assertIsNotNone(self.process.returncode)
        self.assertIsNone(self.sentinel.poll(), 'unrelated sentinel was signalled')
        elapsed = time.monotonic() - started
        self.assertLess(elapsed, 5)
        print('QMP_CPU_STOP ' + json.dumps({'method': 'owned_handle_after_connection_failure',
                                           'seconds': elapsed, 'exit_code': self.process.returncode,
                                           'unrelated_sentinel_alive': True,
                                           'guest_os': False, 'gpu_attached': False}), flush=True)


if __name__ == '__main__':
    unittest.main()
