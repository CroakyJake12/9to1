#!/usr/bin/env python3
"""Opt-in, local-only first-use writer for Haven Files' Google OAuth client config.

Run interactively on the owner's Ubuntu account, never in cloud automation.
Creates exactly one new client-ID or optional client-secret file; never reads a
saved credential, modifies an existing file, contacts Google, or logs input.
Not a replacement for OAuth registration, consent or secure native runtime I/O.
"""

from __future__ import annotations

import argparse
import errno
import getpass
import os
from pathlib import Path
import secrets
import stat
import sys

CLIENT_SUFFIX = ".apps.googleusercontent.com"
FILENAMES = {"client-id": "google-drive-client-id.txt",
             "client-secret": "google-drive-client-secret.txt"}
DIR_FLAGS = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC
FILE_FLAGS = os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC


class InstallError(Exception):
    """Only use fixed, nonsecret, non-path-bearing messages."""


def validate(kind: str, value: str) -> None:
    if kind not in FILENAMES:
        raise InstallError("Unsupported OAuth configuration kind.")
    if (not isinstance(value, str) or len(value) > 4096 or
            any(ord(c) < 33 or ord(c) > 126 for c in value)):
        raise InstallError("Value must contain only non-whitespace printable ASCII.")
    if kind == "client-id" and (len(value) <= 10 or not value.endswith(CLIENT_SUFFIX)):
        raise InstallError("Client ID has an invalid format; expected a Google OAuth client ID.")
    if kind == "client-secret" and len(value) <= 4:
        raise InstallError("Client secret has an invalid length.")


def _checked_directory(fd: int, *, require_user: bool) -> None:
    entry = os.fstat(fd)
    if not stat.S_ISDIR(entry.st_mode):
        raise InstallError("A configuration path component is not a directory.")
    if require_user and entry.st_uid != os.getuid():
        raise InstallError("A configuration directory is not owned by this user.")
    if entry.st_mode & 0o022:
        # A root-owned, sticky /tmp is a permitted ancestor, not a config dir.
        if require_user or not (entry.st_mode & stat.S_ISVTX and entry.st_uid == 0):
            raise InstallError("A configuration path component is writable by another user.")


def _open_home(home: Path) -> int:
    """Traverse from / with no-follow directory fds, including each ancestor."""
    if not home.is_absolute() or ".." in home.parts:
        raise InstallError("The home directory must be an absolute, nontraversing path.")
    fd = os.open("/", DIR_FLAGS)
    try:
        parts = home.parts[1:]
        for index, component in enumerate(parts):
            if component in ("", ".", ".."):
                raise InstallError("Invalid home directory component.")
            next_fd = os.open(component, DIR_FLAGS, dir_fd=fd)
            os.close(fd)
            fd = next_fd
            _checked_directory(fd, require_user=index == len(parts) - 1)
        _checked_directory(fd, require_user=True)
        return fd
    except Exception:
        os.close(fd)
        raise


def _open_child_directory(parent_fd: int, name: str) -> int:
    try:
        os.mkdir(name, 0o700, dir_fd=parent_fd)
    except FileExistsError:
        pass
    fd = os.open(name, DIR_FLAGS, dir_fd=parent_fd)
    try:
        _checked_directory(fd, require_user=True)
        return fd
    except Exception:
        os.close(fd)
        raise


def _write_one_exclusive(directory_fd: int, name: str, value: str) -> None:
    # Stage fully written, private bytes, then publish with an atomic no-overwrite
    # hard link. Never truncate or follow a pre-existing destination symlink.
    temporary = "." + name + ".tmp." + secrets.token_hex(12)
    fd = -1
    try:
        fd = os.open(temporary, FILE_FLAGS, 0o600, dir_fd=directory_fd)
        info = os.fstat(fd)
        if not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid():
            raise InstallError("The newly created file failed its identity check.")
        os.fchmod(fd, 0o600)  # Before the first sensitive byte is written.
        payload = (value + "\n").encode("ascii")
        view = memoryview(payload)
        while view:
            count = os.write(fd, view)
            if count <= 0:
                raise InstallError("Could not write the new private configuration file.")
            view = view[count:]
        os.fsync(fd)
        os.close(fd)
        fd = -1
        try:
            os.link(temporary, name, src_dir_fd=directory_fd,
                    dst_dir_fd=directory_fd, follow_symlinks=False)
        except FileExistsError as exc:
            raise InstallError("Configuration already exists; nothing was overwritten.") from exc
        os.fsync(directory_fd)
    finally:
        if fd >= 0:
            os.close(fd)
        try:
            os.unlink(temporary, dir_fd=directory_fd)
        except FileNotFoundError:
            pass


def install(kind: str, value: str, *, home: Path | None = None) -> str:
    """Create a single new config file under the specified *real* user home.

    Never resolve symlinks, read old files, replace a target or display values.
    The only successful result is a created file visible to native Haven Files.
    """
    validate(kind, value)  # Validate *before* creating any directories.
    if not hasattr(os, "O_NOFOLLOW") or not hasattr(os, "O_DIRECTORY"):
        raise InstallError("This safe installer requires Linux no-follow directory support.")
    home = Path.home() if home is None else Path(home)
    try:
        home_fd = _open_home(home)
        try:
            config_fd = _open_child_directory(home_fd, ".config")
            try:
                app_fd = _open_child_directory(config_fd, "9to1")
                try:
                    _write_one_exclusive(app_fd, FILENAMES[kind], value)
                finally:
                    os.close(app_fd)
            finally:
                os.close(config_fd)
        finally:
            os.close(home_fd)
    except InstallError:
        raise
    except (OSError, UnicodeError, ValueError) as exc:
        if isinstance(exc, OSError) and exc.errno in (errno.EEXIST, errno.ELOOP):
            raise InstallError("Configuration path or destination already exists unsafely; nothing was overwritten.") from exc
        raise InstallError("Local configuration could not be created safely; no existing file was overwritten.") from exc
    return "created"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--kind", required=True, choices=tuple(FILENAMES))
    args = parser.parse_args(argv)
    try:
        if not sys.stdin.isatty() or not sys.stderr.isatty():
            raise InstallError("Use this installer only in a local interactive terminal.")
        if args.kind == "client-secret":
            value = getpass.getpass("Google OAuth client secret (hidden): ")
        else:
            value = input("Google OAuth client ID (public): ")
        install(args.kind, value)
    except (InstallError, EOFError, KeyboardInterrupt) as exc:
        # Never print values, user paths or underlying OSError details.
        message = str(exc) if isinstance(exc, InstallError) else "Input cancelled."
        print("Refused: " + message, file=sys.stderr)
        return 2
    print("Created a private OAuth configuration file (0600). Verify prerequisites with native_files_drive_preflight.py; this does not sign in or access Drive.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
