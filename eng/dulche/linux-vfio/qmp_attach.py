"""Single fixed V100 attachment on an already-owned private QMP connection.

Construction/capability negotiation is GPU-free. attach_async is consequential:
call only after the published single-use dispatch and scoped VFIO bind.
"""
from __future__ import annotations
import json
import os
from pathlib import Path
import socket
import stat
import struct
import threading
import time


class Refusal(RuntimeError):
    pass


class PrivateQmp:
    def __init__(self, path: Path, qemu_pid: int):
        path = Path(path)
        parent = path.parent.stat()
        info = path.lstat()
        if (path.is_symlink() or not stat.S_ISSOCK(info.st_mode)
                or info.st_uid != os.getuid() or parent.st_uid != os.getuid()
                or parent.st_mode & 0o077 or len(os.fsencode(path)) >= 108):
            raise Refusal('QMP socket must be private and owned')
        self.connection = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.connection.settimeout(2)
        self.buffer = b''
        try:
            self.connection.connect(str(path))
            pid, uid, _ = struct.unpack('3i', self.connection.getsockopt(
                socket.SOL_SOCKET, socket.SO_PEERCRED, struct.calcsize('3i')))
            if pid != qemu_pid or uid != os.getuid():
                raise Refusal('QMP peer is not the owned QEMU')
            self.deadline = time.monotonic() + 2
            if 'QMP' not in self.read_message():
                raise Refusal('Missing QMP greeting')
            self.execute({'execute': 'qmp_capabilities', 'id': 'dulche-capabilities'})
        except BaseException:
            self.connection.close()
            raise

    def read_message(self) -> dict:
        while b'\n' not in self.buffer:
            remaining = self.deadline - time.monotonic()
            if remaining <= 0:
                raise TimeoutError('QMP deadline')
            self.connection.settimeout(remaining)
            data = self.connection.recv(4096)
            if not data:
                raise EOFError('QMP disconnected')
            self.buffer += data
            if len(self.buffer) > 65536:
                raise Refusal('Oversize QMP response')
        line, self.buffer = self.buffer.split(b'\n', 1)
        result = json.loads(line)
        if not isinstance(result, dict):
            raise Refusal('Invalid QMP response')
        return result

    def execute(self, command: dict) -> dict:
        self.deadline = time.monotonic() + 2
        self.connection.settimeout(2)
        self.connection.sendall((json.dumps(command) + '\r\n').encode())
        for _ in range(64):
            reply = self.read_message()
            if reply.get('id') == command['id']:
                if 'error' in reply or 'return' not in reply:
                    raise Refusal('QMP refused attachment: ' + repr(reply))
                return reply
        raise Refusal('QMP response id missing')

    def close(self):
        try:
            self.connection.shutdown(socket.SHUT_RDWR)
        except OSError:
            pass
        self.connection.close()


class V100Attachment:
    """Nonblocking controller adapter; no retry or arbitrary QMP command input."""
    def __init__(self, client: PrivateQmp):
        self.client = client
        self.started = False
        self.state = 'pending'
        self.error = None
        self.response = None
        self.thread = None

    def attach_async(self) -> None:
        if self.started:
            raise Refusal('Physical attachment is one-use')
        self.started = True
        def attach():
            try:
                self.response = self.client.execute({
                    'execute': 'device_add', 'id': 'dulche-v100-attach-once',
                    'arguments': {'driver': 'vfio-pci', 'host': '0000:05:00.0',
                                  'id': 'v100', 'bus': 'gpuroot', 'rombar': 0}})
                self.state = 'attached'
            except BaseException as error:
                self.error = repr(error)
                self.state = 'failed'
        self.thread = threading.Thread(target=attach, daemon=True)
        self.thread.start()

    def poll(self) -> str:
        return self.state

    def close(self):
        self.client.close()
        if self.thread is not None:
            self.thread.join(timeout=.1)
