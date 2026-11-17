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


def get_client_status(home: Path, environment: dict[str, str]) -> str:
    from_env = environment.get("HAVEN_GOOGLE_DRIVE_CLIENT_ID", "").strip()
    if len(from_env) > 10:
        return "configured_unverified" if from_env.endswith(CLIENT_SUFFIX) else "invalid_format"
    # Follow native source's environment -> ~/.config/9to1/client-id file priority.
    config_dir = home / ".config" / "9to1"
    # A symlink in either user-config component can redirect a public ID.
    for directory in (home / ".config", config_dir):
        try:
            metadata = directory.lstat()
        except FileNotFoundError:
            continue
        except OSError:
            return "unsafe_config_path"
        if (not stat.S_ISDIR(metadata.st_mode) or
                stat.S_IMODE(metadata.st_mode) & 0o022 or
                (hasattr(os, "getuid") and metadata.st_uid != os.getuid())):
            return "unsafe_config_path"
    config = config_dir / "google-drive-client-id.txt"
    state = inspect_file(config, require_private=False)
    if state == "absent":
        return "missing"
    if state != "regular":
        return "unsafe_config_path"
    try:
        # Public OAuth client ID only; never read token/account files.
        value = config.read_text(encoding="utf-8").strip()
    except (OSError, UnicodeError):
        return "unreadable_config"
    if len(value) <= 10:
        return "missing"
    return "configured_unverified" if value.endswith(CLIENT_SUFFIX) else "invalid_format"


def check(environment: dict[str, str] | None = None, *, home: Path | None = None) -> dict[str, object]:
    environment = dict(os.environ if environment is None else environment)
    home = (Path.home() if home is None else Path(home)).absolute()
    data_dir = environment.get("HAVEN_DATA_DIR")
    if data_dir:
        profile = Path(data_dir)
        if not profile.is_absolute():
            return {"ready_for_sign_in": False,
                    "client_id": "not_checked", "profile": "unsafe_relative_data_dir",
                    "account_file": "not_checked", "browser_opener": "not_checked",
                    "actions": ["HAVEN_DATA_DIR must be an absolute directory."]}
    else:
        profile = home / ".local" / "share" / "9to1-development" / "profile"

    # Test every existing component under HOME, not only the terminal profile,
    # so a symlinked parent cannot silently redirect future token creation.
    ancestor = profile
    ancestor_problem = None
    while ancestor != ancestor.parent:
        try:
            details = ancestor.lstat()
        except FileNotFoundError:
            details = None
        except OSError:
            ancestor_problem = "uninspectable_ancestor"
            break
        if details is not None:
            if stat.S_ISLNK(details.st_mode):
                ancestor_problem = "symlink_ancestor"
                break
            if not stat.S_ISDIR(details.st_mode) and ancestor != profile:
                ancestor_problem = "not_directory_ancestor"
                break
            # A non-sticky world-writable parent could replace the private
            # profile. Sticky /tmp is acceptable if the profile itself is 0700.
            if details.st_mode & 0o002 and not details.st_mode & stat.S_ISVTX:
                ancestor_problem = "writable_ancestor"
                break
        ancestor = ancestor.parent
    profile_state = ancestor_problem or inspect_directory(profile)
    if profile_state == "private_directory":
        credential_state = inspect_file(profile / EXPECTED_ACCOUNT_FILENAME, require_private=True)
    else:
        credential_state = "not_checked"
    client_state = get_client_status(home, environment)
    opener_state = "available" if shutil.which("xdg-open", path=environment.get("PATH", "")) else "missing"
    actions: list[str] = []
    if client_state != "configured_unverified":
        actions.append("Configure an approved Google OAuth DESKTOP client ID via HAVEN_GOOGLE_DRIVE_CLIENT_ID or ~/.config/9to1/google-drive-client-id.txt; never provide secrets to this checker.")
    if profile_state != "private_directory":
        actions.append("Review the native profile directory and its parents; prepare a real user-owned, non-symlink directory with mode 0700 before connecting. This checker does not modify it.")
    if credential_state not in ("absent", "private_regular", "not_checked"):
        actions.append("Do not use the existing native Drive credential file until a trusted local owner fixes its type, ownership or permission; never copy its contents into logs.")
    if opener_state != "available":
        actions.append("An installed xdg-open helper is required for this Ubuntu browser sign-in workflow.")
    actions.append("Even a passing preflight cannot verify OAuth consent, token validity, a Google Drive connection, or live file access.")
    return {"ready_for_sign_in": (
                client_state == "configured_unverified" and
                profile_state == "private_directory" and
                credential_state in ("absent", "private_regular") and
                opener_state == "available"),
            "client_id": client_state, "profile": profile_state,
            "account_file": credential_state, "browser_opener": opener_state,
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
        for name in ("client_id", "profile", "account_file", "browser_opener"):
            print(f"  {name}: {result[name]}")
        print("  readiness: " + ("PREREQUISITES OBSERVED (not Google-tested)" if result["ready_for_sign_in"] else "OWNER ACTION REQUIRED"))
        for action in result["actions"]:
            print("  - " + action)
    return 0 if result["ready_for_sign_in"] else 2


if __name__ == "__main__":
    sys.exit(main())
