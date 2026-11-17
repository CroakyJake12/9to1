#!/usr/bin/env python3
"""Read-only prerequisite check for native Haven Files' direct Google Drive OAuth.

Never opens the token file, calls Google, mutates a profile, or prints identifiers.
This is NOT proof that Google OAuth or a live Drive operation will work.
"""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import shutil
import stat
import sys

CLIENT_SUFFIX = ".apps.googleusercontent.com"
EXPECTED_ACCOUNT_FILENAME = "google-drive-account.json"
EXPECTED_SECRET_FILENAME = "google-drive-client-secret.txt"


def inspect_file(path: Path, *, require_private: bool) -> str:
    """Return an explicitly bounded status without reading file contents."""
    try:
        info = path.lstat()  # Refuse symlinks instead of following them.
    except FileNotFoundError:
        return "absent"
    except OSError:
        return "uninspectable"
    if stat.S_ISLNK(info.st_mode):
        return "symlink"
    if not stat.S_ISREG(info.st_mode):
        return "not_regular"
    if hasattr(os, "getuid") and info.st_uid != os.getuid():
        return "wrong_owner"
    if require_private and stat.S_IMODE(info.st_mode) & 0o077:
        return "exposed_permissions"
    if stat.S_IMODE(info.st_mode) & 0o022:
        return "writable_by_others"
    return "private_regular" if require_private else "regular"


def inspect_directory(path: Path) -> str:
    try:
        info = path.lstat()
    except FileNotFoundError:
        return "absent"
    except OSError:
        return "uninspectable"
    if stat.S_ISLNK(info.st_mode):
        return "symlink"
    if not stat.S_ISDIR(info.st_mode):
        return "not_directory"
    if hasattr(os, "getuid") and info.st_uid != os.getuid():
        return "wrong_owner"
    if stat.S_IMODE(info.st_mode) & 0o077:
        return "exposed_permissions"
    return "private_directory"


def config_path_safe(home: Path) -> bool:
    """Check the actual parent chain for *both* native OAuth config files.

    A valid client ID supplied by an environment variable does NOT mean the
    native app skips its optional client-secret file in ~/.config/9to1.
    """
    for directory in (home, home / ".config", home / ".config" / "9to1"):
        try:
            metadata = directory.lstat()
        except FileNotFoundError:
            continue
        except OSError:
            return False
        if (not stat.S_ISDIR(metadata.st_mode) or
                stat.S_IMODE(metadata.st_mode) & 0o022 or
                (hasattr(os, "getuid") and metadata.st_uid != os.getuid())):
            return False
    return True


def get_client_secret_status(home: Path) -> str:
    """Inspect only inode metadata via no-follow descriptors; never read secret bytes.

    Linux O_PATH holds the file without read permission or opening FIFOs for I/O.
    In particular, a renamed/symlink-swapped config parent cannot redirect
    this check after its parent directory descriptor has been opened.
    """
    if not home.is_absolute() or ".." in home.parts or not hasattr(os, "O_PATH"):
        return "unsafe_config_path"
    directories = [*home.parts[1:], ".config", "9to1"]
    home_index = len(home.parts) - 2
    dir_flags = os.O_PATH | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC
    parent_fd = -1
    secret_fd = -1
    try:
        parent_fd = os.open("/", dir_flags)
        for index, component in enumerate(directories):
            next_fd = os.open(component, dir_flags, dir_fd=parent_fd)
            os.close(parent_fd)
            parent_fd = next_fd
            info = os.fstat(parent_fd)
            sticky_system_dir = info.st_uid == 0 and bool(info.st_mode & stat.S_ISVTX)
            if info.st_mode & 0o022 and not sticky_system_dir:
                return "unsafe_config_path"
            if index >= home_index and info.st_uid != os.getuid():
                return "unsafe_config_path"
        secret_fd = os.open(EXPECTED_SECRET_FILENAME,
                            os.O_PATH | os.O_NOFOLLOW | os.O_CLOEXEC,
                            dir_fd=parent_fd)
        info = os.fstat(secret_fd)
        if stat.S_ISLNK(info.st_mode):
            return "symlink"
        if not stat.S_ISREG(info.st_mode):
            return "not_regular"
        if info.st_uid != os.getuid():
            return "wrong_owner"
        if stat.S_IMODE(info.st_mode) & 0o077:
            return "exposed_permissions"
        return "private_regular"
    except FileNotFoundError:
        return "absent"
    except (OSError, ValueError):
        return "unsafe_config_path"
    finally:
        if secret_fd >= 0:
            os.close(secret_fd)
        if parent_fd >= 0:
            os.close(parent_fd)


def _read_public_client_id(home: Path) -> str:
    """Read only the bounded public ID through checked, no-follow directory fds.

    A preceding lstat of a path is not a safe admission for Path.read_text:
    a renamed parent or file could be replaced by a symlink between the calls.
    The token and optional secret are never opened by this function.
    """
    if not home.is_absolute() or ".." in home.parts:
        return "unsafe_config_path"
    directories = [*home.parts[1:], ".config", "9to1"]
    flags = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC
    directory_fd = -1
    client_fd = -1
    try:
        directory_fd = os.open("/", flags)
        home_index = len(home.parts) - 2
        for index, component in enumerate(directories):
            next_fd = os.open(component, flags, dir_fd=directory_fd)
            os.close(directory_fd)
            directory_fd = next_fd
            metadata = os.fstat(directory_fd)
            # The system-owned, sticky /tmp ancestor is allowed. No config
            # directory or untrusted non-sticky ancestor may be writable by others.
            sticky_system_dir = (metadata.st_uid == 0 and
                                 metadata.st_mode & stat.S_ISVTX)
            if (metadata.st_mode & 0o022) and not sticky_system_dir:
                return "unsafe_config_path"
            if index >= home_index and metadata.st_uid != os.getuid():
                return "unsafe_config_path"
        client_fd = os.open("google-drive-client-id.txt",
                            os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC,
                            dir_fd=directory_fd)
        info = os.fstat(client_fd)
        if (not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid() or
                stat.S_IMODE(info.st_mode) & 0o022):
            return "unsafe_config_path"
        if info.st_size > 4096:
            return "unreadable_config"
        raw = os.read(client_fd, 4097)
        if len(raw) > 4096:
            return "unreadable_config"
        value = raw.decode("utf-8").strip()
        if len(value) <= 10:
            return "missing"
        return "configured_unverified" if value.endswith(CLIENT_SUFFIX) else "invalid_format"
    except FileNotFoundError:
        return "missing"
    except UnicodeError:
        return "unreadable_config"
    except (OSError, ValueError):
        return "unsafe_config_path"
    finally:
        if client_fd >= 0:
            os.close(client_fd)
        if directory_fd >= 0:
            os.close(directory_fd)


def get_client_status(home: Path, environment: dict[str, str]) -> str:
    from_env = environment.get("HAVEN_GOOGLE_DRIVE_CLIENT_ID", "").strip()
    if len(from_env) > 10:
        return "configured_unverified" if from_env.endswith(CLIENT_SUFFIX) else "invalid_format"
    # Match native source's environment -> public client-ID file priority.
    if not config_path_safe(home):
        return "unsafe_config_path"
    return _read_public_client_id(home)


def inspect_profile_and_account(profile: Path) -> tuple[str, str]:
    """Inspect the native profile and token inode without reading credentials.

    Linux O_PATH closes the path-based check/use race between ancestor
    validation and credential metadata. It never opens the token for reading.
    """
    if not profile.is_absolute() or ".." in profile.parts or not hasattr(os, "O_PATH"):
        return "uninspectable_ancestor", "not_checked"
    components = profile.parts[1:]
    if not components:
        return "wrong_owner", "not_checked"
    directory_fd = -1
    credential_fd = -1
    try:
        directory_fd = os.open("/", os.O_PATH | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
        for index, component in enumerate(components):
            opened_fd = os.open(component, os.O_PATH | os.O_NOFOLLOW | os.O_CLOEXEC,
                                dir_fd=directory_fd)
            os.close(directory_fd)
            directory_fd = opened_fd
            info = os.fstat(directory_fd)
            if stat.S_ISLNK(info.st_mode):
                return "symlink_ancestor", "not_checked"
            if not stat.S_ISDIR(info.st_mode):
                status = "not_directory" if index == len(components) - 1 else "not_directory_ancestor"
                return status, "not_checked"
            if info.st_mode & 0o002 and not info.st_mode & stat.S_ISVTX:
                return "writable_ancestor", "not_checked"
            if index == len(components) - 1:
                if info.st_uid != os.getuid():
                    return "wrong_owner", "not_checked"
                if stat.S_IMODE(info.st_mode) & 0o077:
                    return "exposed_permissions", "not_checked"
        try:
            credential_fd = os.open(EXPECTED_ACCOUNT_FILENAME,
                                    os.O_PATH | os.O_NOFOLLOW | os.O_CLOEXEC,
                                    dir_fd=directory_fd)
        except FileNotFoundError:
            return "private_directory", "absent"
        info = os.fstat(credential_fd)
        if stat.S_ISLNK(info.st_mode):
            status = "symlink"
        elif not stat.S_ISREG(info.st_mode):
            status = "not_regular"
        elif info.st_uid != os.getuid():
            status = "wrong_owner"
        elif stat.S_IMODE(info.st_mode) & 0o077:
            status = "exposed_permissions"
        else:
            status = "private_regular"
        return "private_directory", status
    except FileNotFoundError:
        return "absent", "not_checked"
    except (OSError, ValueError):
        return "uninspectable_ancestor", "not_checked"
    finally:
        if credential_fd >= 0:
            os.close(credential_fd)
        if directory_fd >= 0:
            os.close(directory_fd)


def check(environment: dict[str, str] | None = None, *, home: Path | None = None) -> dict[str, object]:
    environment = dict(os.environ if environment is None else environment)
    home = (Path.home() if home is None else Path(home)).absolute()
    data_dir = environment.get("HAVEN_DATA_DIR")
    if data_dir:
        profile = Path(data_dir)
        if not profile.is_absolute():
            return {"ready_for_sign_in": False,
                    "client_id": "not_checked", "profile": "unsafe_relative_data_dir",
                    "account_file": "not_checked", "client_secret_file": "not_checked",
                    "browser_opener": "not_checked",
                    "actions": ["HAVEN_DATA_DIR must be an absolute directory."]}
    else:
        profile = home / ".local" / "share" / "9to1-development" / "profile"

    # Inspect profile and saved-token metadata through one anchored path chain.
    # Never open a token for reading or follow symlink-swapped parents.
    profile_state, credential_state = inspect_profile_and_account(profile)
    client_state = get_client_status(home, environment)
    client_secret_state = get_client_secret_status(home)
    opener_state = "available" if shutil.which("xdg-open", path=environment.get("PATH", "")) else "missing"
    actions: list[str] = []
    if client_state != "configured_unverified":
        actions.append("Configure an approved Google OAuth DESKTOP client ID via HAVEN_GOOGLE_DRIVE_CLIENT_ID or ~/.config/9to1/google-drive-client-id.txt; never provide secrets to this checker.")
    if profile_state != "private_directory":
        actions.append("Review the native profile directory and its parents; prepare a real user-owned, non-symlink directory with mode 0700 before connecting. This checker does not modify it.")
    if credential_state not in ("absent", "private_regular", "not_checked"):
        actions.append("Do not use the existing native Drive credential file until a trusted local owner fixes its type, ownership or permission; never copy its contents into logs.")
    if client_secret_state not in ("absent", "private_regular"):
        actions.append("The optional native Google OAuth client-secret file or its directory is unsafe. A trusted local owner must correct it before sign-in; never copy or print the secret. Desktop clients do not need this file.")
    if opener_state != "available":
        actions.append("Automatic browser opening is unavailable. A reviewed private manual consent fallback may still work; do not share OAuth URLs, and do not rely on unsafe shared /tmp files.")
    actions.append("Even a passing preflight cannot verify OAuth consent, token validity, a Google Drive connection, or live file access.")
    return {"ready_for_sign_in": (
                client_state == "configured_unverified" and
                profile_state == "private_directory" and
                credential_state in ("absent", "private_regular") and
                client_secret_state in ("absent", "private_regular")),
            "client_id": client_state, "profile": profile_state,
            "account_file": credential_state, "client_secret_file": client_secret_state,
            "browser_opener": opener_state,
            "actions": actions}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--json", action="store_true", help="Print bounded, secret-free JSON status")
    args = parser.parse_args(argv)
    result = check()
    if args.json:
        print(json.dumps(result, indent=2, sort_keys=True))
    else:
        print("Native Haven Files / Google Drive — read-only first-use preflight")
        for name in ("client_id", "profile", "account_file", "client_secret_file", "browser_opener"):
            print(f"  {name}: {result[name]}")
        print("  readiness: " + ("PREREQUISITES OBSERVED (not Google-tested)" if result["ready_for_sign_in"] else "OWNER ACTION REQUIRED"))
        for action in result["actions"]:
            print("  - " + action)
    return 0 if result["ready_for_sign_in"] else 2


if __name__ == "__main__":
    sys.exit(main())
