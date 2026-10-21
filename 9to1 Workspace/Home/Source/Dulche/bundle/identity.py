"""Path-and-file identity helpers: safe regular-file access, hashing, containment.

All file access for asset admission goes through these helpers so a symlink can
never be followed outside an owned root and only regular files are accepted.
"""
from __future__ import annotations

import errno
import hashlib
import os
import pathlib
import stat
from dataclasses import dataclass


class IdentityError(ValueError):
    """Raised when a path/identity is not safe or does not match."""


@dataclass(frozen=True)
class FileIdentity:
    digest: str
    size: int


def _open_regular_readonly(path: pathlib.Path | str, *, require_regular: bool = True) -> int:
    flags = os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_NOFOLLOW", 0)
    try:
        fd = os.open(path, flags)
    except OSError as exc:
        raise IdentityError(f"cannot open path safely: {path}: {exc}") from exc
    try:
        info = os.fstat(fd)
        if not stat.S_ISREG(info.st_mode):
            raise IdentityError(f"refusing non-regular file: {path}")
        return fd
    except BaseException:
        os.close(fd)
        raise


def is_regular_file_no_follow(path: pathlib.Path | str) -> bool:
    """True only when *path* is a regular file and not a symlink."""
    try:
        info = pathlib.Path(path).lstat()
    except OSError:
        return False
    return stat.S_ISREG(info.st_mode)


def file_identity(path: pathlib.Path | str) -> FileIdentity:
    """Return (sha256, size) of a regular file, refusing symlinks/other types."""
    fd = _open_regular_readonly(path)
    digest = hashlib.sha256()
    total = 0
    try:
        while True:
            chunk = os.read(fd, 1024 * 1024)
            if not chunk:
                break
            digest.update(chunk)
            total += len(chunk)
    finally:
        os.close(fd)
    return FileIdentity(digest=digest.hexdigest(), size=total)


def validate_sha256(digest: str) -> str:
    value = str(digest).lower()
    if len(value) != 64 or any(char not in "0123456789abcdef" for char in value):
        raise IdentityError("expected a 64-character lowercase SHA-256 hex digest")
    return value


def _is_symlink_escape(resolved: pathlib.Path, root: pathlib.Path) -> bool:
    return root not in resolved.parents and resolved != root


def resolve_within(root: pathlib.Path, relative: str) -> pathlib.Path:
    """Resolve a relative asset path against *root* and reject any escape.

    Rejects absolute paths, ``..`` components, and symlink traversal that lands
    outside the root. The returned path may still not exist; the caller decides
    whether presence is required.
    """
    root_resolved = root.resolve()
    raw = pathlib.PurePosixPath(str(relative))
    if raw.is_absolute():
        raise IdentityError(f"asset path must be relative: {relative}")
    if ".." in raw.parts:
        raise IdentityError(f"asset path must not contain '..': {relative}")
    candidate = root_resolved.joinpath(*raw.parts)
    try:
        resolved = candidate.resolve(strict=False)
    except OSError as exc:
        raise IdentityError(f"cannot resolve asset path {relative}: {exc}") from exc
    if _is_symlink_escape(resolved, root_resolved):
        raise IdentityError(f"asset path escapes bundle root via symlink: {relative}")
    return resolved


def ensure_path_has_no_symlinks(root: pathlib.Path, relative: str) -> None:
    """Reject any symlink component in *relative* beneath *root*.

    This is stricter than ``resolve_within``: even a symlink that resolves back
    inside the root is refused, so an installed tree cannot contain any symlink
    indirection.
    """
    raw = pathlib.PurePosixPath(str(relative))
    if raw.is_absolute() or ".." in raw.parts:
        raise IdentityError(f"unsafe installed path: {relative}")
    root_resolved = root.resolve()
    current = root_resolved
    for part in raw.parts:
        current = current / part
        try:
            info = current.lstat()
        except OSError:
            # Component does not exist yet; nothing to check.
            continue
        if stat.S_ISLNK(info.st_mode):
            raise IdentityError(f"installed asset path contains a symlink component: {relative}")


def reject_symlink_escapes(root: pathlib.Path, relative_files: list[str]) -> None:
    """Walk the given relative files and ensure none resolve outside root."""
    root_resolved = root.resolve()
    for relative in relative_files:
        resolved = resolve_within(root, relative)
        if _is_symlink_escape(resolved, root_resolved):
            raise IdentityError(f"installed asset escapes bundle root: {relative}")


def ensure_owned_directory(root: pathlib.Path) -> None:
    """Create root as a private owner-only directory (best-effort mode)."""
    root.mkdir(parents=True, exist_ok=True)
    try:
        os.chmod(root, 0o700)
    except OSError:
        pass


def atomic_write(path: pathlib.Path, data: bytes, *, mode: int = 0o600) -> None:
    """Write *data* atomically into *path*'s directory with fsync durability."""
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp_name = os.mkstemp(prefix=f".{path.name}.", suffix=".tmp", dir=str(path.parent))
    try:
        with os.fdopen(fd, "wb", closefd=True) as handle:
            handle.write(data)
            handle.flush()
            os.fsync(handle.fileno())
        os.chmod(tmp_name, mode)
        os.replace(tmp_name, path)
        flags = os.O_RDONLY | getattr(os, "O_DIRECTORY", 0)
        dir_fd = os.open(path.parent, flags)
        try:
            os.fsync(dir_fd)
        finally:
            os.close(dir_fd)
    except BaseException:
        try:
            os.unlink(tmp_name)
        except OSError:
            pass
        raise
