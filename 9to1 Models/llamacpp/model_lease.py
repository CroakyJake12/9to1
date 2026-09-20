"""Cross-process lifecycle leases for Haven local model operations."""
from __future__ import annotations

import fcntl
import os
import pathlib
import re
import stat
from dataclasses import dataclass

_MODEL_ID_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")


class ModelLeaseError(RuntimeError):
    pass


class ModelLeaseBusy(ModelLeaseError):
    pass


def ensure_private_directory(path: pathlib.Path) -> None:
    path.mkdir(mode=0o700, parents=True, exist_ok=True)
    info = path.lstat()
    if not stat.S_ISDIR(info.st_mode) or info.st_uid != os.getuid():
        raise ModelLeaseError(f"unsafe directory ownership or type: {path}")
    os.chmod(path, 0o700)


def _open_lock(path: pathlib.Path) -> int:
    flags = os.O_RDWR | os.O_CREAT | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_NOFOLLOW", 0)
    fd = os.open(path, flags, 0o600)
    try:
        info = os.fstat(fd)
        if not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid():
            raise ModelLeaseError(f"unsafe lease file ownership or type: {path}")
        os.fchmod(fd, 0o600)
        return fd
    except BaseException:
        os.close(fd)
        raise


@dataclass
class ModelLease:
    fd: int
    path: pathlib.Path
    _released: bool = False

    def release(self) -> None:
        if self._released:
            return
        self._released = True
        try:
            fcntl.flock(self.fd, fcntl.LOCK_UN)
        finally:
            os.close(self.fd)

    def __enter__(self) -> "ModelLease":
        return self

    def __exit__(self, exc_type, exc, tb) -> None:
        self.release()


def _acquire(path: pathlib.Path, *, exclusive: bool, blocking: bool) -> ModelLease:
    ensure_private_directory(path.parent)
    try:
        fd = _open_lock(path)
    except OSError as exc:
        raise ModelLeaseError(f"cannot open lifecycle lease safely: {path}: {exc}") from exc
    operation = fcntl.LOCK_EX if exclusive else fcntl.LOCK_SH
    if not blocking:
        operation |= fcntl.LOCK_NB
    try:
        fcntl.flock(fd, operation)
    except BlockingIOError as exc:
        os.close(fd)
        raise ModelLeaseBusy(f"lifecycle lease is busy: {path.name}") from exc
    except BaseException:
        os.close(fd)
        raise
    return ModelLease(fd=fd, path=path)


def acquire_model_lease(
    runtime_dir: pathlib.Path,
    model_id: str,
    *,
    exclusive: bool,
    blocking: bool = False,
) -> ModelLease:
    if not _MODEL_ID_RE.fullmatch(model_id):
        raise ModelLeaseError("invalid model id for lifecycle lease")
    return _acquire(runtime_dir / f"model-{model_id}.lease", exclusive=exclusive, blocking=blocking)


def acquire_store_lease(runtime_dir: pathlib.Path, *, blocking: bool = False) -> ModelLease:
    return _acquire(runtime_dir / "model-store.lease", exclusive=True, blocking=blocking)
