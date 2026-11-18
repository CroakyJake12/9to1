#!/usr/bin/env python3
"""Read-only, fresh-profile desktop window smoke for the existing Haven apphost.

This observes an actual X11 client window belonging to the launched native process;
it does not configure Google OAuth, inspect owner state, or exercise Drive mutations.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import signal
import stat
import subprocess
import sys
import time
from typing import Callable


def owned_windows(wmctrl_output: str, pid: int) -> list[str]:
    """Return window ids with an X11 _NET_WM_PID exactly matching our apphost."""
    found: list[str] = []
    for row in wmctrl_output.splitlines():
        fields = row.split(maxsplit=4)
        if len(fields) < 4:
            continue
        if fields[2].isdecimal() and int(fields[2]) == pid and fields[0].startswith("0x"):
            found.append(fields[0])
    return found


def x11_windows(pid: int) -> list[str]:
    result = subprocess.run(
        ["/usr/bin/wmctrl", "-lp"], capture_output=True, text=True,
        timeout=4, check=False,
    )
    if result.returncode != 0:
        return []
    return owned_windows(result.stdout, pid)


def private_child(parent: Path, name: str) -> Path:
    """Create an exclusive owner-private child, refusing an existing path."""
    if not parent.is_dir() or parent.is_symlink():
        raise ValueError("Disposable parent must be an existing real directory")
    child = parent / name
    child.mkdir(mode=0o700, exist_ok=False)
    return child


def run_smoke(
    executable: Path,
    scratch_parent: Path,
    *,
    timeout_seconds: float = 45,
    stable_seconds: float = 3,
    window_probe: Callable[[int], list[str]] = x11_windows,
) -> dict[str, object]:
    """Observe a real process-owned window, then terminate only our process group."""
    executable = executable.absolute()
    info = executable.stat()
    if not stat.S_ISREG(info.st_mode) or not os.access(executable, os.X_OK):
        raise ValueError("Native apphost must be a regular executable")
    if not (executable.parent / "Haven.dll").is_file() or not (executable.parent / "Haven.runtimeconfig.json").is_file():
        raise ValueError("Native publish is missing the actual Haven assembly/runtime configuration")
    if timeout_seconds <= 0 or stable_seconds < 0:
        raise ValueError("Invalid smoke timeout")

    private = private_child(scratch_parent, "native-files-clean-profile")
    env = os.environ.copy()
    for key in list(env):
        upper = key.upper()
        if upper.startswith(("GOOGLE_", "AWS_", "AZURE_", "FILES_DRIVE_", "GH_")) or upper in (
            "GITHUB_TOKEN", "NINETOONE_DEVELOPMENT_STATE", "NINETOONE_REPOSITORY"
        ):
            env.pop(key)
    env.update({
        "HOME": str(private),
        "XDG_CONFIG_HOME": str(private / "config"),
        "XDG_DATA_HOME": str(private / "data"),
        "XDG_CACHE_HOME": str(private / "cache"),
        "XDG_STATE_HOME": str(private / "state"),
        "DOTNET_CLI_HOME": str(private),
    })
    for folder in ("config", "data", "cache", "state"):
        (private / folder).mkdir(mode=0o700)
    started = time.monotonic()
    proc: subprocess.Popen[bytes] | None = None
    window: str | None = None
    reason = "native-window-not-observed"
    with (private / "stdout.log").open("wb") as out, (private / "stderr.log").open("wb") as err:
        try:
            proc = subprocess.Popen(
                [str(executable)], cwd=str(executable.parent), env=env,
                stdout=out, stderr=err, start_new_session=True,
            )
            deadline = started + timeout_seconds
            while time.monotonic() < deadline:
                if proc.poll() is not None:
                    reason = "native-app-exited-before-window"
                    break
                windows = window_probe(proc.pid)
                if windows:
                    window = windows[0]
                    reason = "window-observed"
                    break
                time.sleep(0.3)
            if window is not None:
                end_stable = time.monotonic() + stable_seconds
                while time.monotonic() < end_stable:
                    if proc.poll() is not None:
                        reason = "native-app-exited-during-stability-check"
                        window = None
                        break
                    time.sleep(0.2)
        finally:
            if proc is not None and proc.poll() is None:
                # start_new_session makes proc.pid its own process-group id.
                os.killpg(proc.pid, signal.SIGTERM)
                try:
                    proc.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    os.killpg(proc.pid, signal.SIGKILL)
                    proc.wait(timeout=5)

    return {
        "schema": "9to1.native-files-linux-window-smoke/v1",
        "status": "PASS" if window else "FAIL",
        "reason": reason,
        "window_id": window,
        "elapsed_seconds": round(time.monotonic() - started, 2),
        "evidence": "X11 process-owned window with isolated disposable profile; no Google OAuth or Drive",
        "credential_checks": "NOT_RUN",
        "drive_checks": "NOT_RUN",
        "native_process_exit": "owned process group terminated after observation; normal app close NOT_RUN",
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--executable", type=Path, required=True)
    parser.add_argument("--scratch-parent", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--timeout-seconds", type=float, default=45)
    args = parser.parse_args(argv)
    try:
        result = run_smoke(args.executable, args.scratch_parent, timeout_seconds=args.timeout_seconds)
    except (OSError, ValueError, subprocess.SubprocessError) as exc:
        # Do not serialize exception text, path or environment values into CI logs.
        result = {"schema": "9to1.native-files-linux-window-smoke/v1", "status": "FAIL",
                  "reason": type(exc).__name__, "drive_checks": "NOT_RUN"}
    print(json.dumps(result, sort_keys=True))
    # CI path is new and run-owned; refuse existing report rather than overwrite.
    with args.report.open("x", encoding="utf-8") as handle:
        json.dump(result, handle, indent=2, sort_keys=True)
        handle.write("\n")
    return 0 if result["status"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
