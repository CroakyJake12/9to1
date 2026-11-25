"""Private guest-local rendezvous for already-owned v8 control and weight pipes."""
from contextlib import ExitStack, contextmanager
import os
from pathlib import Path
import selectors
import socket
import stat
from time import monotonic

from .artifacts import CustodyError, canonical_id, checked_path
from .v8_linux_process_owner import LinuxRankHandle, pin_local_peer

_ROLES = {"control": "c", "weights": "w"}
_CHUNK = 64 * 1024


class GuestRankRendezvous:
    """One private directory and one independently authenticated peer per lane."""

    def __init__(self, runtime, attempt, *, expected_uid):
        path = Path(runtime)
        root = checked_path(path, directory=True)
        if type(expected_uid) is not int or os.getuid() != expected_uid or root.st_uid != expected_uid:
            raise CustodyError("Guest runtime is not owned by the admitted UID.")
        if stat.S_IMODE(root.st_mode) != 0o700:
            raise CustodyError("Guest runtime directory must be private.")
        self._base = path / ("v8-" + canonical_id(attempt))
        self._uid, self._stack, self._items = expected_uid, ExitStack(), {}
        if len(os.fsencode(self._base / "c")) > 100:
            raise CustodyError("Registered guest rendezvous path is too long.")
        self._base.mkdir(mode=0o700, exist_ok=False)
        self._stack.callback(self._base.rmdir)
        try:
            for role, filename in _ROLES.items():
                endpoint = self._base / filename
                listener = self._stack.enter_context(socket.socket(socket.AF_UNIX, socket.SOCK_STREAM))
                listener.bind(str(endpoint))
                self._stack.callback(endpoint.unlink)
                os.chmod(endpoint, 0o600, follow_symlinks=False)
                listener.listen(1)
                self._items[role] = endpoint, listener
        except BaseException:
            self.close()
            raise

    def __enter__(self):
        return self

    def __exit__(self, *args):
        self.close()

    def close(self):
        self._stack.close()

    def path(self, role):
        if role not in self._items:
            raise CustodyError("Unsupported registered guest rendezvous role.")
        return self._items[role][0]

    @contextmanager
    def accept_owned(self, role, expected, *, deadline):
        if role not in self._items or type(expected) is not LinuxRankHandle or not expected.live():
            raise CustodyError("No original guest process assigned to the rendezvous.")
        _, listener = self._items.pop(role)
        remaining = deadline - monotonic()
        if remaining <= 0:
            raise CustodyError("Owned guest rendezvous expired before accepting.")
        listener.settimeout(remaining)
        connection, _ = listener.accept()
        with connection:
            original = expected.original
            with pin_local_peer(connection, expected_uid=self._uid, expected_cgroup=original.cgroup) as peer:
                if peer.original != original or not expected.live():
                    raise CustodyError("Guest rendezvous peer is not the exact original process.")
                yield connection


def relay_weight_channel(connection, std_in, std_out, *, deadline, still_owned):
    """Bidirectional, bounded SSH relay over Unix socket, no external listener.

    stdout carries original bytes only. No network, file selection, model
    interpretation, or release authority is added here.
    """
    if (type(connection) is not socket.socket or connection.family != socket.AF_UNIX
            or any(not callable(getattr(stream, "fileno", None)) for stream in (std_in, std_out))
            or not callable(still_owned)):
        raise CustodyError("Invalid already-owned guest weight relay endpoints.")
    src, sink, sock = std_in.fileno(), std_out.fileno(), connection.fileno()
    if len({src, sink, sock}) != 3:
        raise CustodyError("Guest weight relay endpoints must be independent pipes.")
    before = {fd: os.get_blocking(fd) for fd in (src, sink, sock)}
    left, right = bytearray(), bytearray()
    src_eof = sock_eof = half_closed = False
    try:
        for fd in before:
            os.set_blocking(fd, False)
        while not (src_eof and sock_eof and not left and not right):
            remaining = deadline - monotonic()
            if remaining <= 0 or still_owned() is not True:
                raise CustodyError("The owned guest weight relay lost its live lease.")
            if src_eof and not left and not half_closed:
                connection.shutdown(socket.SHUT_WR)
                half_closed = True
            with selectors.DefaultSelector() as found:
                if not src_eof and len(left) < _CHUNK:
                    found.register(src, selectors.EVENT_READ)
                if right:
                    found.register(sink, selectors.EVENT_WRITE)
                mask = ((selectors.EVENT_READ if not sock_eof and len(right) < _CHUNK else 0)
                        | (selectors.EVENT_WRITE if left else 0))
                if mask:
                    found.register(sock, mask)
                for key, flags in found.select(min(remaining, .25)):
                    try:
                        if key.fd == src:
                            data = os.read(src, _CHUNK - len(left))
                            src_eof = not data
                            left.extend(data)
                        elif key.fd == sink:
                            n = os.write(sink, right)
                            del right[:n]
                        else:
                            if flags & selectors.EVENT_READ:
                                data = connection.recv(_CHUNK - len(right))
                                sock_eof = not data
                                right.extend(data)
                            if flags & selectors.EVENT_WRITE:
                                n = connection.send(left)
                                del left[:n]
                    except BlockingIOError:
                        continue
    finally:
        for fd, blocking in before.items():
            os.set_blocking(fd, blocking)
