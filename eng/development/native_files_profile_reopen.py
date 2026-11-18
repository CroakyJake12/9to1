#!/usr/bin/env python3
"""Reopen the *existing* Haven apphost twice in one newly-created disposable X11 profile.

Never connects Drive or touches a user's existing Haven profile. A process-owned
window at each launch proves native startup/reopen only, not user-level close or
persisted Google Drive identities.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import secrets
import signal
import stat
import subprocess
import sys
import time
from typing import Callable


class ReopenError(Exception):
    """Use only fixed, path-free diagnostics."""


def _private_parent(parent: Path) -> None:
    if not parent.is_absolute() or ".." in parent.parts:
        raise ReopenError("Scratch parent must be absolute and nontraversing")
    try:
        info = parent.lstat()
    except OSError as exc:
        raise ReopenError("Scratch parent is unavailable") from exc
    if (not stat.S_ISDIR(info.st_mode) or stat.S_ISLNK(info.st_mode)
            or info.st_uid != os.getuid() or stat.S_IMODE(info.st_mode) != 0o700):
        raise ReopenError("Scratch parent must be owned, real and mode 0700")
    # Existing scratch must not resolve through a symlinked ancestor.
    cursor = parent
    while cursor != cursor.parent:
        if stat.S_ISLNK(cursor.lstat().st_mode):
            raise ReopenError("Scratch parent contains a symlink ancestor")
        cursor = cursor.parent


def _apphost(executable: Path, *, real_elf: bool) -> Path:
    if not executable.is_absolute() or ".." in executable.parts:
        raise ReopenError("Apphost path must be absolute and nontraversing")
    try:
        info = executable.lstat()
        if (not stat.S_ISREG(info.st_mode) or stat.S_ISLNK(info.st_mode)
                or not os.access(executable, os.X_OK)):
            raise ReopenError("Apphost must be a real executable file")
        for name in ("Haven.dll", "Haven.runtimeconfig.json"):
            child = executable.parent / name
            if not stat.S_ISREG(child.lstat().st_mode):
                raise ReopenError("Apphost lacks native Haven runtime assets")
        if real_elf:
            with executable.open("rb") as stream:
                header = stream.read(20)
            if (len(header) != 20 or header[:6] != b"\x7fELF\x02\x01"
                    or header[18:20] != b"\x3e\x00"):
                raise ReopenError("Apphost is not an ELF Linux x64 binary")
    except OSError as exc:
        raise ReopenError("Apphost or its native runtime assets are unavailable") from exc
    return executable


def _env(profile: Path, original: dict[str, str]) -> dict[str, str]:
    # Explicit allowlist excludes all existing Google OAuth/session credentials,
    # custom Files profiles and account env variables.
    keep = {"PATH", "LANG", "LC_ALL", "LC_CTYPE", "TERM", "DISPLAY",
            "XAUTHORITY", "DBUS_SESSION_BUS_ADDRESS", "XDG_RUNTIME_DIR",
            "DOTNET_ROOT", "DOTNET_ROOT_X64", "LD_LIBRARY_PATH", "TZ"}
    env = {key: value for key, value in original.items() if key in keep}
    env.update({
        "HOME": str(profile), "DOTNET_CLI_HOME": str(profile),
        "XDG_CONFIG_HOME": str(profile / "config"),
        "XDG_DATA_HOME": str(profile / "data"),
        "XDG_CACHE_HOME": str(profile / "cache"),
        "XDG_STATE_HOME": str(profile / "state"),
        "HAVEN_DATA_DIR": str(profile / "haven-data"),
        "DOTNET_NOLOGO": "1", "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
    })
    return env


def _one_launch(executable: Path, profile: Path, scratch: Path, number: int,
                probe: Callable[[int], list[str]], timeout: float, stable: float,
                env: dict[str, str]) -> tuple[bool, int, str]:
    proc: subprocess.Popen[bytes] | None = None
    try:
        out_fd = os.open(scratch / f"attempt-{number}.stdout.log",
                         os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600)
        try:
            err_fd = os.open(scratch / f"attempt-{number}.stderr.log",
                             os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600)
        except OSError:
            os.close(out_fd)
            raise
        with os.fdopen(out_fd, "wb") as stdout, os.fdopen(err_fd, "wb") as stderr:
            proc = subprocess.Popen([str(executable)], cwd=executable.parent,
                                    env=env, stdout=stdout, stderr=stderr,
                                    start_new_session=True)
            started = time.monotonic()
            saw_window = False
            while time.monotonic() - started < timeout:
                if proc.poll() is not None:
                    return False, proc.pid, "exited-before-window"
                if probe(proc.pid):
                    saw_window = True
                    break
                time.sleep(0.15)
            if not saw_window:
                return False, proc.pid, "window-timeout"
            stable_start = time.monotonic()
            while time.monotonic() - stable_start < stable:
                if proc.poll() is not None:
                    return False, proc.pid, "exited-after-window"
                time.sleep(0.1)
            return True, proc.pid, "window-observed"
    except (OSError, subprocess.SubprocessError, ValueError) as exc:
        raise ReopenError("Could not start or inspect native apphost") from exc
    finally:
        if proc is not None and proc.poll() is None:
            # Only this test's own new-session process group is signalled.
            try:
                os.killpg(proc.pid, signal.SIGTERM)
                proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(proc.pid, signal.SIGKILL)
                proc.wait(timeout=5)
            except ProcessLookupError:
                pass


def run_reopen(executable: Path, scratch_parent: Path, *,
               timeout: float = 45, stable: float = 2,
               window_probe: Callable[[int], list[str]] | None = None,
               real_elf: bool = True) -> dict[str, object]:
    if not 0 < timeout <= 120 or not 0 <= stable <= 15:
        raise ReopenError("Reopen smoke has an unsupported timeout")
    executable = _apphost(executable, real_elf=real_elf)
    _private_parent(scratch_parent)
    try:
        scratch = scratch_parent / "native-files-profile-reopen"
        scratch.mkdir(mode=0o700, exist_ok=False)
        profile = scratch / "profile"
        profile.mkdir(mode=0o700, exist_ok=False)
        for component in ("config", "data", "cache", "state", "haven-data"):
            (profile / component).mkdir(mode=0o700, exist_ok=False)
    except OSError as exc:
        raise ReopenError("Could not create a new isolated profile") from exc
    if window_probe is None:
        from native_files_linux_smoke import x11_windows
        window_probe = x11_windows
    env = _env(profile, os.environ)
    marker = profile / ".reopen-smoke-owned-marker"
    random_marker = secrets.token_hex(16)
    # It is a harness-only proof of reusing one profile, NOT native Files persistence.
    private_inode = profile.stat().st_ino
    passes: list[bool] = []
    pids: list[int] = []
    reasons: list[str] = []
    for number in (1, 2):
        if number == 2:
            try:
                unchanged = (profile.stat().st_ino == private_inode and
                             not marker.is_symlink() and marker.read_text("ascii") == random_marker)
            except (OSError, UnicodeError):
                unchanged = False
            if not unchanged:
                raise ReopenError("The disposable profile changed between launches")
        ok, pid, reason = _one_launch(executable, profile, scratch, number,
                                      window_probe, timeout, stable, env)
        passes.append(ok)
        pids.append(pid)
        reasons.append(reason)
        if not ok:
            break
        if number == 1:
            fd = os.open(marker, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
            with os.fdopen(fd, "w", encoding="ascii") as stream:
                stream.write(random_marker)
                stream.flush()
                os.fsync(stream.fileno())
    success = len(passes) == 2 and all(passes) and pids[0] != pids[1]
    return {
        "schema": "9to1.native-files-profile-reopen/v1",
        "status": "PASS" if success else "FAIL",
        "attempts": len(passes),
        "attempt_reasons": reasons,
        "same_disposable_profile_in_both_launches": len(passes) == 2 and profile.stat().st_ino == private_inode,
        "independent_processes": len(pids) == 2 and pids[0] != pids[1],
        "close_type": "harness-terminated-process-group; normal-user-close-NOT_RUN",
        "provider_authentication": "NOT_RUN", "drive_files_persistence": "NOT_RUN",
        "evidence": "real process-owned X11 windows when invoked with native window probe; fake-host fixtures are separate",
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--executable", type=Path, required=True)
    parser.add_argument("--scratch-parent", type=Path, required=True)
    parser.add_argument("--timeout-seconds", type=float, default=45)
    args = parser.parse_args(argv)
    try:
        result = run_reopen(args.executable, args.scratch_parent, timeout=args.timeout_seconds)
    except ReopenError as exc:
        result = {"schema": "9to1.native-files-profile-reopen/v1", "status": "BLOCKED",
                  "reason": str(exc), "provider_authentication": "NOT_RUN", "drive_files_persistence": "NOT_RUN"}
    print(json.dumps(result, sort_keys=True))
    return 0 if result["status"] == "PASS" else 2


if __name__ == "__main__":
    sys.exit(main())
