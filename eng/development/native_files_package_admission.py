#!/usr/bin/env python3
"""Admit an existing *real* Haven linux-x64 tarball into a new private directory.

This is a hash-pinned offline verifier/extractor, not a build, downloader,
Google Drive login, app runner, or acceptance test.
"""
from __future__ import annotations

import argparse
import errno
import hashlib
import hmac
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import sys
import tarfile

MAX_FILES = 30000
MAX_EXPANDED_BYTES = 8 * 1024**3
MAX_MEMBER_BYTES = 2 * 1024**3
REQUIRED = {"Haven", "Haven.dll", "Haven.runtimeconfig.json"}
CHUNK = 1024 * 1024


class AdmissionError(Exception):
    """A fixed, non-sensitive user-facing refusal."""


def open_archive(archive: Path):
    """Hold one no-follow archive inode for inspection, extraction and final hash."""
    try:
        fd = os.open(archive, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC | os.O_NONBLOCK)
    except OSError as exc:
        if exc.errno == errno.ELOOP:
            raise AdmissionError("Archive must be a regular file, not a symlink") from exc
        raise AdmissionError("Cannot read the chosen archive") from exc
    try:
        if not stat.S_ISREG(os.fstat(fd).st_mode):
            raise AdmissionError("Archive must be a regular file, not a symlink")
        return os.fdopen(fd, "rb")
    except BaseException:
        os.close(fd)
        raise


def digest_handle(handle, expected_sha256: str) -> str:
    if re.fullmatch(r"[0-9a-fA-F]{64}", expected_sha256) is None:
        raise AdmissionError("Expected SHA-256 must be exactly 64 hex characters")
    try:
        handle.seek(0)
        h = hashlib.sha256()
        for block in iter(lambda: handle.read(CHUNK), b""):
            h.update(block)
        handle.seek(0)
    except (OSError, ValueError) as exc:
        raise AdmissionError("Cannot read the chosen archive") from exc
    actual = h.hexdigest()
    if not hmac.compare_digest(actual, expected_sha256.lower()):
        raise AdmissionError("Archive SHA-256 differs from the supplied trusted digest")
    return actual


def digest_archive(archive: Path, expected_sha256: str) -> str:
    with open_archive(archive) as handle:
        return digest_handle(handle, expected_sha256)

def member_path(raw_name: str) -> tuple[str, ...]:
    if not raw_name or raw_name.startswith("/") or "\\" in raw_name or "\x00" in raw_name:
        raise AdmissionError("Archive contains an unsafe member name")
    # GNU tar -C publish -czf ... . emits './' and './Haven'.
    pieces = raw_name.split("/")
    if any(piece == ".." for piece in pieces):
        raise AdmissionError("Archive contains a parent-traversal member")
    components = tuple(piece for piece in pieces if piece not in ("", "."))
    if any(":" in part for part in components):
        raise AdmissionError("Archive contains an unsafe member name")
    return components


def inspect_members(tf: tarfile.TarFile) -> tuple[list[tuple[tarfile.TarInfo, tuple[str, ...]]], int]:
    found: set[tuple[str, ...]] = set()
    paths: list[tuple[tarfile.TarInfo, tuple[str, ...]]] = []
    total = 0
    count = 0
    kind_by_path: dict[tuple[str, ...], str] = {}
    for entry in tf:
        count += 1
        if count > MAX_FILES:
            raise AdmissionError("Archive contains too many members")
        parts = member_path(entry.name)
        if not parts:
            if not entry.isdir():
                raise AdmissionError("Archive root must be a directory")
            continue
        if parts in found:
            raise AdmissionError("Archive contains duplicate target paths")
        found.add(parts)
        if entry.isdir():
            kind_by_path[parts] = "directory"
        elif entry.isfile() and not entry.issparse():
            if entry.size < 0 or entry.size > MAX_MEMBER_BYTES or total + entry.size > MAX_EXPANDED_BYTES:
                raise AdmissionError("Archive exceeds safe expanded size limits")
            total += entry.size
            kind_by_path[parts] = "file"
        else:
            raise AdmissionError("Archive contains a link or special file")
        paths.append((entry, parts))
    for parts, kind in kind_by_path.items():
        for depth in range(1, len(parts)):
            parent = parts[:depth]
            if parent in kind_by_path and kind_by_path[parent] != "directory":
                raise AdmissionError("Archive contains a file used as a parent directory")
    for name in REQUIRED:
        if kind_by_path.get((name,)) != "file":
            raise AdmissionError("Archive is missing a required Haven runtime file")
    host = next(entry for entry, parts in paths if parts == ("Haven",))
    if not host.mode & 0o100:
        raise AdmissionError("Haven apphost is not owner-executable in the archive")
    return paths, total


def inspect_archive_handle(handle, expected_sha256: str) -> dict[str, object]:
    digest = digest_handle(handle, expected_sha256)
    try:
        with tarfile.open(fileobj=handle, mode="r:gz") as tf:
            entries, total = inspect_members(tf)
            host = next(entry for entry, parts in entries if parts == ("Haven",))
            h = tf.extractfile(host)
            if h is None:
                raise AdmissionError("Haven executable is not readable in the archive")
            with h:
                elf = h.read(20)
            if not is_elf_x64(elf):
                raise AdmissionError("Haven executable is not a Linux x86-64 ELF apphost")
    except (tarfile.TarError, OSError, EOFError, ValueError) as exc:
        raise AdmissionError("Archive cannot be parsed safely") from exc
    return {"status": "PASS", "sha256": digest, "files_and_dirs": len(entries),
            "expanded_bytes": total, "target": "linux-x64", "runtime": "Haven.Desktop",
            "drive_login": "NOT_RUN", "native_launch": "NOT_RUN"}


def inspect_archive(archive: Path, expected_sha256: str) -> dict[str, object]:
    with open_archive(archive) as handle:
        return inspect_archive_handle(handle, expected_sha256)

def is_elf_x64(header: bytes) -> bool:
    return (len(header) >= 20 and header[:4] == b"\x7fELF" and
            header[4] == 2 and header[5] == 1 and header[18:20] == b"\x3e\x00")


def open_private_parent(parent: Path) -> int:
    if not parent.is_absolute():
        raise AdmissionError("Destination parent must be absolute")
    parts = parent.parts
    fd = os.open("/", os.O_RDONLY | os.O_DIRECTORY)
    try:
        for part in parts[1:]:
            if part in ("", ".", ".."):
                raise AdmissionError("Destination path contains an unsafe component")
            next_fd = os.open(part, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=fd)
            os.close(fd)
            fd = next_fd
        s = os.fstat(fd)
        if s.st_uid != os.geteuid() or stat.S_IMODE(s.st_mode) != 0o700:
            raise AdmissionError("Destination parent must be owned by the user and mode 0700")
        return fd
    except (OSError, ValueError) as exc:
        raise AdmissionError("Destination parent is missing or contains an unsafe directory") from exc
    finally:
        # The successful caller owns fd; this branch is only for errors.
        if sys.exc_info()[0] is not None:
            os.close(fd)


def create_child_dirs(root_fd: int, parts: tuple[str, ...]) -> int:
    """Return an owned dirfd for private archive parents without pathname lookup."""
    fd = os.dup(root_fd)
    try:
        for part in parts:
            try:
                os.mkdir(part, mode=0o700, dir_fd=fd)
            except FileExistsError:
                pass
            next_fd = os.open(part, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW,
                              dir_fd=fd)
            os.close(fd)
            fd = next_fd
        return fd
    except BaseException:
        os.close(fd)
        raise


def copy_member(tf: tarfile.TarFile, entry: tarfile.TarInfo, parent_fd: int, name: str) -> None:
    source = tf.extractfile(entry)
    if source is None:
        raise AdmissionError("An archive member could not be read")
    with source:
        fd = os.open(name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW,
                     0o600, dir_fd=parent_fd)
        try:
            written = 0
            with os.fdopen(fd, "wb", closefd=False) as dest:
                while written < entry.size:
                    block = source.read(min(CHUNK, entry.size - written))
                    if not block:
                        raise AdmissionError("An archive member was truncated")
                    dest.write(block)
                    written += len(block)
                dest.flush()
                os.fchmod(fd, 0o700 if entry.mode & 0o100 else 0o600)
        finally:
            os.close(fd)


def same_child(parent_fd: int, name: str, root_fd: int) -> bool:
    """Never follow a substituted symlink or remove somebody else's directory."""
    try:
        path_inode = os.stat(name, dir_fd=parent_fd, follow_symlinks=False)
        original_inode = os.fstat(root_fd)
        return (stat.S_ISDIR(path_inode.st_mode) and
                (path_inode.st_dev, path_inode.st_ino) ==
                (original_inode.st_dev, original_inode.st_ino))
    except OSError:
        return False


def remove_own_tree(root_fd: int) -> None:
    # fwalk uses anchored directory descriptors; it never follows an attacker
    # substituted symlink to another tree during rollback.
    for _, dirs, files, walk_fd in os.fwalk(".", topdown=False, follow_symlinks=False,
                                            dir_fd=root_fd):
        for name in files:
            os.unlink(name, dir_fd=walk_fd)
        for name in dirs:
            os.rmdir(name, dir_fd=walk_fd)


def install_archive(archive: Path, expected_sha256: str, destination: Path) -> dict[str, object]:
    if not destination.is_absolute() or destination.name in ("", ".", ".."):
        raise AdmissionError("Destination must be an absolute, new named folder")
    if not re.fullmatch(r"[a-zA-Z0-9][a-zA-Z0-9._-]{0,100}", destination.name):
        raise AdmissionError("Destination folder name contains unsupported characters")
    with open_archive(archive) as handle:
        result = inspect_archive_handle(handle, expected_sha256)
        parent_fd = open_private_parent(destination.parent)
        root_fd = None
        made = False
        try:
            os.mkdir(destination.name, mode=0o700, dir_fd=parent_fd)
            made = True
            root_fd = os.open(destination.name, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW,
                              dir_fd=parent_fd)
            # Every archived file and directory is created relative to the newly
            # opened private root FD, never a separately re-resolved pathname.
            handle.seek(0)
            with tarfile.open(fileobj=handle, mode="r:gz") as tf:
                entries, _ = inspect_members(tf)
                for entry, parts in entries:
                    parent = create_child_dirs(root_fd, parts if entry.isdir() else parts[:-1])
                    try:
                        if not entry.isdir():
                            copy_member(tf, entry, parent, parts[-1])
                    finally:
                        os.close(parent)
            # Compare the *same opened archive inode* used for verification and
            # extraction, even if its original pathname is renamed/replaced.
            digest_handle(handle, expected_sha256)
            if not same_child(parent_fd, destination.name, root_fd):
                raise AdmissionError("Destination changed during installation")
            fd = os.open("Haven", os.O_RDONLY | os.O_NOFOLLOW, dir_fd=root_fd)
            with os.fdopen(fd, "rb") as binary:
                if not is_elf_x64(binary.read(20)):
                    raise AdmissionError("Extracted executable does not match Linux x86-64")
            result["installed"] = True
            result["launch"] = "./Haven (from the new private destination; requires .NET 10 and a graphical Linux session)"
            return result
        except (OSError, tarfile.TarError, EOFError, ValueError) as exc:
            raise AdmissionError("Archive could not be installed safely") from exc
        finally:
            try:
                if made and "installed" not in result and root_fd is not None:
                    # Roll back only our *own same-inode* directory. If a
                    # same-user process renamed it, preserve both paths rather
                    # than traversing or deleting the replacement.
                    if same_child(parent_fd, destination.name, root_fd):
                        remove_own_tree(root_fd)
                        os.rmdir(destination.name, dir_fd=parent_fd)
            finally:
                if root_fd is not None:
                    os.close(root_fd)
                os.close(parent_fd)

def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("action", choices=("verify", "install"))
    ap.add_argument("--archive", required=True, type=Path)
    ap.add_argument("--sha256", required=True)
    ap.add_argument("--destination", type=Path)
    args = ap.parse_args(argv)
    try:
        if args.action == "install":
            if args.destination is None:
                raise AdmissionError("Install requires --destination")
            result = install_archive(args.archive, args.sha256, args.destination)
        else:
            if args.destination is not None:
                raise AdmissionError("Verify does not accept a destination")
            result = inspect_archive(args.archive, args.sha256)
        print(json.dumps(result, sort_keys=True))
        return 0
    except AdmissionError as exc:
        # Refusals are deliberately redacted: no archive paths, env or credentials.
        print(json.dumps({"status": "BLOCKED", "reason": str(exc)}, sort_keys=True))
        return 2


if __name__ == "__main__":
    sys.exit(main())
