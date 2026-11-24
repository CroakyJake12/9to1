"""Bind a Linux trainer or local broker peer to its original process lifetime."""

from dataclasses import dataclass
import os
from pathlib import Path
import select
import socket
import struct

from .artifacts import CustodyError

_PROC = Path("/proc")


@dataclass(frozen=True, slots=True)
class LinuxProcess:
    pid: int
    uid: int
    start_ticks: int
    cgroup: str


def _read_process(pid):
    stem = _PROC / str(pid)
    try:
        directory = stem.stat()
        stat = (stem / "stat").read_text(encoding="utf-8", errors="surrogateescape")
        prefix, tail = stat.rsplit(")", 1)
        fields = tail.split()
        if not prefix.startswith(f"{pid} (") or len(fields) < 20 or fields[0] in ("Z", "X"):
            raise ValueError
        start = int(fields[19])
        lists = (stem / "cgroup").read_text(encoding="utf-8").splitlines()
        groups = [row[3:] for row in lists if row.startswith("0::")]
        if (start <= 0 or len(groups) != 1 or not groups[0].startswith("/")
                or any(part in (".", "..") for part in groups[0].split("/"))):
            raise ValueError
    except (OSError, ValueError, UnicodeError) as error:
        raise CustodyError("No original live Linux process identity is observable.") from error
    return LinuxProcess(pid, directory.st_uid, start, groups[0])


class LinuxRankHandle:
    """Original pidfd and start/cgroup identity, never a process name heuristic.

    Capture only an already-owned child (or an authenticated local peer).
    This is not a CUDA, thermal, Flash or delegated-job admission check.
    """

    def __init__(self, pid, *, expected_uid, expected_cgroup):
        if (type(pid) is not int or pid < 2 or type(expected_uid) is not int or expected_uid < 0
                or type(expected_cgroup) is not str or not expected_cgroup.startswith("/")
                or expected_cgroup == "/"
                or any(part in (".", "..") for part in expected_cgroup.split("/"))):
            raise CustodyError("Exact dedicated Linux process ownership is required.")
        try:
            self._fd = os.pidfd_open(pid, 0)
        except (AttributeError, OSError) as error:
            raise CustodyError("Original Linux process handle unavailable.") from error
        try:
            self.original = _read_process(pid)
            if ((expected_uid, expected_cgroup) != (self.original.uid, self.original.cgroup)
                    or not self.live()):
                raise CustodyError("Linux process differs from its supervisor-owned cgroup.")
        except BaseException:
            self.close()
            raise

    def live(self):
        if self._fd is None:
            return False
        poller = select.poll()
        poller.register(self._fd, select.POLLIN | select.POLLHUP | select.POLLERR)
        if poller.poll(0):
            return False
        try:
            return (_read_process(self.original.pid) == self.original and not poller.poll(0))
        except CustodyError:
            return False

    def confirms(self, receipt):
        """Check the process half of a typed registration receipt."""
        from .v8_joint_registration import RankProcessReceipt

        return (type(receipt) is RankProcessReceipt
                and (receipt.pid, receipt.start_ticks, receipt.cgroup) == (
                    self.original.pid, self.original.start_ticks, self.original.cgroup)
                and self.live())

    def close(self):
        if self._fd is not None:
            fd, self._fd = self._fd, None
            os.close(fd)

    def __enter__(self):
        return self

    def __exit__(self, *args):
        self.close()


def pin_local_peer(connection, *, expected_uid, expected_cgroup):
    """Get the peer from Linux, never accept a PID or UID from client JSON."""
    credentials = struct.Struct("=iii")
    try:
        if connection.family != socket.AF_UNIX:
            raise ValueError
        pid, uid, _ = credentials.unpack(connection.getsockopt(socket.SOL_SOCKET,
                                                               socket.SO_PEERCRED, credentials.size))
        if uid != expected_uid:
            raise ValueError
    except (AttributeError, OSError, ValueError, struct.error) as error:
        raise CustodyError("No authenticated local rank peer credentials.") from error
    return LinuxRankHandle(pid, expected_uid=expected_uid, expected_cgroup=expected_cgroup)
