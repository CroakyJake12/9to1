#!/usr/bin/env python3
"""Manually import a Google *Desktop* OAuth client JSON into native Haven Files.

Reads a private, owned, non-symlink JSON file already downloaded locally by the
owner. Creates the public client ID with the existing 9to1 secure installer; does
not install, print or persist any client secret from the downloaded source.
No Google calls, browser login, or changes to an existing configuration.
"""
from __future__ import annotations

import argparse
import errno
import json
import os
from pathlib import Path
import stat
import sys

import native_files_drive_setup as setup

MAX_SOURCE_BYTES = 32768
READ_FLAGS = os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC | os.O_NONBLOCK


class ImportErrorSafe(Exception):
    """Only fixed, non-secret, non-path-bearing messages may reach the CLI."""


def _reject_duplicate_keys(pairs: list[tuple[str, object]]) -> dict:
    result: dict = {}
    for key, value in pairs:
        if key in result:
            raise ImportErrorSafe("OAuth JSON contains a repeated property.")
        result[key] = value
    return result


def _read_private_json(source: Path, home: Path) -> dict:
    """Traverse owner-controlled directories and read a mode-private JSON fd."""
    if not source.is_absolute() or ".." in source.parts or not home.is_absolute() or ".." in home.parts:
        raise ImportErrorSafe("Select an absolute, nontraversing JSON path in your home folder.")
    try:
        relative = source.relative_to(home)
    except ValueError as exc:
        raise ImportErrorSafe("Select a JSON file inside your own home directory.") from exc
    if len(relative.parts) == 0 or any(x in ("", ".", "..") for x in relative.parts):
        raise ImportErrorSafe("Invalid OAuth JSON source path.")
    if not hasattr(os, "O_NOFOLLOW") or not hasattr(os, "O_DIRECTORY"):
        raise ImportErrorSafe("Linux no-follow file support is required.")

    try:
        home_fd = setup._open_home(home)
    except (OSError, setup.InstallError) as exc:
        raise ImportErrorSafe("Cannot safely access the private OAuth JSON file or its directories.") from exc
    directory_fd = home_fd
    try:
        for component in relative.parts[:-1]:
            next_fd = os.open(component, setup.DIR_FLAGS, dir_fd=directory_fd)
            if directory_fd != home_fd:
                os.close(directory_fd)
            directory_fd = next_fd
            setup._checked_directory(directory_fd, require_user=True)
        fd = os.open(relative.parts[-1], READ_FLAGS, dir_fd=directory_fd)
        try:
            info = os.fstat(fd)
            if (not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid()
                    or info.st_mode & 0o077 or info.st_size > MAX_SOURCE_BYTES
                    or info.st_nlink != 1):
                raise ImportErrorSafe("The OAuth JSON must be a small, owner-only 0600 regular file.")
            data = bytearray()
            while len(data) <= MAX_SOURCE_BYTES:
                chunk = os.read(fd, min(4096, MAX_SOURCE_BYTES + 1 - len(data)))
                if not chunk:
                    break
                data.extend(chunk)
            after = os.fstat(fd)
            if (len(data) > MAX_SOURCE_BYTES or info.st_dev != after.st_dev or
                    info.st_ino != after.st_ino or info.st_size != after.st_size):
                raise ImportErrorSafe("The OAuth JSON source changed or is too large.")
            try:
                parsed = json.loads(data.decode("utf-8"), object_pairs_hook=_reject_duplicate_keys)
            except (UnicodeError, ValueError) as exc:
                raise ImportErrorSafe("OAuth JSON is not valid UTF-8 JSON.") from exc
            if not isinstance(parsed, dict):
                raise ImportErrorSafe("OAuth JSON must be a JSON object.")
            return parsed
        finally:
            os.close(fd)
    except (OSError, setup.InstallError) as exc:
        raise ImportErrorSafe("Cannot safely access the private OAuth JSON file or its directories.") from exc
    finally:
        if directory_fd != home_fd:
            os.close(directory_fd)
        os.close(home_fd)


def import_desktop_json(source: Path, *, home: Path | None = None) -> str:
    """Install only a validated Desktop client ID without exposing its secret."""
    home = Path.home() if home is None else Path(home)
    raw = _read_private_json(Path(source), home)
    if set(raw) & {"web"} or not isinstance(raw.get("installed"), dict):
        raise ImportErrorSafe("Only Google Desktop/installed OAuth JSON is supported; web clients need a separate reviewed configuration.")
    installed = raw["installed"]
    client_id = installed.get("client_id")
    if not isinstance(client_id, str):
        raise ImportErrorSafe("The Desktop OAuth client ID is missing or malformed.")
    try:
        setup.validate("client-id", client_id)
    except setup.InstallError as exc:
        raise ImportErrorSafe("The Desktop OAuth client ID is missing or malformed.") from exc
    redirects = installed.get("redirect_uris")
    if (not isinstance(redirects, list) or not redirects or
            not all(isinstance(uri, str) for uri in redirects) or
            not any(uri in ("http://localhost", "http://127.0.0.1") for uri in redirects)):
        raise ImportErrorSafe("The Desktop client must allow a localhost loopback redirect.")
    if installed.get("token_uri") != "https://oauth2.googleapis.com/token":
        raise ImportErrorSafe("The Desktop client's Google OAuth token endpoint is not recognised.")
    try:
        setup.install("client-id", client_id, home=home)
    except setup.InstallError as exc:
        raise ImportErrorSafe(str(exc)) from exc
    return "created"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--client-json", required=True, type=Path, help="Absolute path to private 0600 Desktop OAuth JSON")
    args = parser.parse_args(argv)
    try:
        if not sys.stdin.isatty() or not sys.stderr.isatty():
            raise ImportErrorSafe("Run the import manually in an interactive terminal.")
        import_desktop_json(args.client_json)
    except (ImportErrorSafe, KeyboardInterrupt, EOFError) as exc:
        message = str(exc) if isinstance(exc, ImportErrorSafe) else "Import cancelled."
        print("Refused: " + message, file=sys.stderr)
        return 2
    print("Installed a private native Files Desktop OAuth client ID (0600). No secret was copied. Run native_files_drive_preflight.py; Google sign-in still requires your consent.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
