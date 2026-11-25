"""Per-attempt systemd user scope for the actual owned v8 worker.

The daemon passes previously approved limits. This module renders the
command and verifies kernel limits before returning the real rank pidfd.
No privileged setup, no global service changes, no auto-selected limits.
"""
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
import subprocess
from uuid import UUID

from .artifacts import CustodyError, canonical_id
from .v8_linux_process_owner import LinuxRankHandle, _read_process


@dataclass(frozen=True, slots=True)
class ApprovedScopeLimits:
    memory_bytes: int
    tasks: int
    cpu_percent: int
    wall_seconds: int

    def __post_init__(self):
        if (any(type(v) is not int or v <= 0 for v in
                (self.memory_bytes, self.tasks, self.cpu_percent, self.wall_seconds))
                or self.memory_bytes > 256 * 1024**3 or self.tasks > 8192
                or self.cpu_percent > 6400 or self.wall_seconds > 86400):
            raise CustodyError("Missing bounded registered v8 scope limits.")


def scope_name(attempt, rank):
    if (UUID(canonical_id(attempt)).int == 0 or type(rank) is not int or rank not in (0, 1)):
        raise CustodyError("Invalid registered scope identity.")
    return f"dulche-v8-{attempt}-{'early' if rank == 0 else 'tail'}.scope"


def scoped_worker_argv(argv, *, attempt, rank, limits):
    """systemd-run stays a launcher; its PID is not the worker certificate."""
    if (type(limits) is not ApprovedScopeLimits or type(argv) is not tuple or not argv
            or not argv[0].startswith("/") or any(type(x) is not str or not x or "\0" in x for x in argv)):
        raise CustodyError("No absolute registered scope executable.")
    unit = scope_name(attempt, rank)
    return ("/usr/bin/systemd-run", "--user", "--scope", "--quiet", "--collect",
            "--no-ask-password", "--expand-environment=no", "--unit=" + unit,
            f"--property=MemoryMax={limits.memory_bytes}", f"--property=TasksMax={limits.tasks}",
            f"--property=CPUQuota={limits.cpu_percent}%", f"--property=RuntimeMaxSec={limits.wall_seconds}",
            "--", *argv)


def _manager_cgroup(unit):
    try:
        r = subprocess.run(
            ("/usr/bin/systemctl", "--user", "show", "--value", "--property=ControlGroup", unit),
            check=True, capture_output=True, shell=False, timeout=3, text=True)
    except (OSError, subprocess.SubprocessError) as exc:
        raise CustodyError("The systemd user manager did not prove the registered worker scope.") from exc
    return r.stdout.strip()


def _check_cgroup(raw, unit, *, limits, cgroup_root=Path("/sys/fs/cgroup")):
    path = PurePosixPath(raw)
    if (not path.is_absolute() or path.name != unit or ".." in path.parts or raw != str(path)):
        raise CustodyError("The systemd unit did not report its dedicated registered cgroup.")
    folder = cgroup_root.joinpath(*path.parts[1:])
    try:
        mem = int((folder / "memory.max").read_text().strip())
        tasks = int((folder / "pids.max").read_text().strip())
        quota, period = map(int, (folder / "cpu.max").read_text().split())
    except (OSError, ValueError) as exc:
        raise CustodyError("Missing bounded kernel resources on the registered v8 scope.") from exc
    if (mem, tasks) != (limits.memory_bytes, limits.tasks) or period <= 0 or quota * 100 != limits.cpu_percent * period:
        raise CustodyError("The kernel limits differ from the approved dedicated v8 scope.")
    return raw


def _descends_from(candidate, origin, maximum=32):
    """Bind interpreter hello to the original live child of this systemd-run."""
    if _read_process(origin.pid) != origin:
        return False
    seen = set()
    try:
        for _ in range(maximum):
            if candidate == origin.pid:
                return _read_process(origin.pid) == origin
            if candidate <= 1 or candidate in seen:
                return False
            seen.add(candidate)
            body = Path(f"/proc/{candidate}/stat").read_text()
            prefix, tail = body.rsplit(")", 1)
            fields = tail.split()
            if not prefix.startswith(f"{candidate} (") or fields[0] in ("Z", "X"):
                return False
            candidate = int(fields[1])  # PPid follows process state
    except (OSError, ValueError, IndexError):
        return False
    return False


def pin_scoped_rank(receipt, launcher, *, expected_uid, limits):
    """Verify registered scope+ancestry, then use receipt PID, never wrapper."""
    from .v8_joint_registration import RankProcessReceipt

    if (type(receipt) is not RankProcessReceipt or type(limits) is not ApprovedScopeLimits
            or getattr(launcher, "poll", None) is None or launcher.poll() is not None):
        raise CustodyError("The source launcher and the actual rank are not live.")
    origin = _read_process(launcher.pid)
    unit = scope_name(receipt.attempt_id, receipt.logical_rank)
    cgroup = _check_cgroup(_manager_cgroup(unit), unit, limits=limits)
    if receipt.cgroup != cgroup or not _descends_from(receipt.pid, origin):
        raise CustodyError("The interpreter does not descend from this owned registered scope launch.")
    handle = LinuxRankHandle(receipt.pid, expected_uid=expected_uid, expected_cgroup=cgroup)
    if not handle.confirms(receipt) or not _descends_from(receipt.pid, origin):
        handle.close()
        raise CustodyError("The original actual v8 worker disappeared or changed scope.")
    return handle
