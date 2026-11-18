#!/usr/bin/env python3
"""Exercise graceful native Haven close/reopen, never a Google Drive account.

Requires the repository's existing native profile and X11 window helpers. Runs
only an existing real Haven apphost or explicitly injected test-owned fixture.
"""
from __future__ import annotations

import argparse
import ctypes
import ctypes.util
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import time
from typing import Callable

from native_files_profile_reopen import ReopenError, _apphost, _env, _private_parent


class CloseError(Exception):
    """Fixed-string diagnostic without paths, PIDs, tokens or window IDs."""


class _ClientData(ctypes.Union):
    _fields_ = [("b", ctypes.c_byte * 20), ("s", ctypes.c_short * 10),
                ("l", ctypes.c_long * 5)]


class _ClientMessage(ctypes.Structure):
    _fields_ = [("type", ctypes.c_int), ("serial", ctypes.c_ulong),
                ("send_event", ctypes.c_int), ("display", ctypes.c_void_p),
                ("window", ctypes.c_ulong), ("message_type", ctypes.c_ulong),
                ("format", ctypes.c_int), ("data", _ClientData)]


class _XEvent(ctypes.Union):
    _fields_ = [("xclient", _ClientMessage), ("pad", ctypes.c_long * 24)]


def send_wm_delete(window_id: str) -> bool:
    """Send WM_DELETE_WINDOW only to a window advertising that X11 protocol.

    The caller MUST have already established exact _NET_WM_PID ownership with
    the process-owned window probe. False means unsupported/unavailable.
    """
    if not re.fullmatch(r"0x[0-9a-fA-F]{1,16}", window_id):
        return False
    library = ctypes.util.find_library("X11")
    if not library:
        return False
    x = ctypes.CDLL(library)
    x.XOpenDisplay.argtypes = [ctypes.c_char_p]
    x.XOpenDisplay.restype = ctypes.c_void_p
    x.XCloseDisplay.argtypes = [ctypes.c_void_p]
    x.XInternAtom.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int]
    x.XInternAtom.restype = ctypes.c_ulong
    x.XGetWMProtocols.argtypes = [ctypes.c_void_p, ctypes.c_ulong,
                                  ctypes.POINTER(ctypes.POINTER(ctypes.c_ulong)),
                                  ctypes.POINTER(ctypes.c_int)]
    x.XGetWMProtocols.restype = ctypes.c_int
    x.XFree.argtypes = [ctypes.c_void_p]
    x.XSendEvent.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_int,
                              ctypes.c_long, ctypes.POINTER(_XEvent)]
    x.XSendEvent.restype = ctypes.c_int
    x.XFlush.argtypes = [ctypes.c_void_p]
    x.XSync.argtypes = [ctypes.c_void_p, ctypes.c_int]
    x.XSetErrorHandler.argtypes = [ctypes.c_void_p]
    x.XSetErrorHandler.restype = ctypes.c_void_p
    display = x.XOpenDisplay(None)
    if not display:
        return False
    errors: list[int] = []
    handler_type = ctypes.CFUNCTYPE(ctypes.c_int, ctypes.c_void_p, ctypes.c_void_p)
    @handler_type
    def handler(_display: int, _event: int) -> int:
        errors.append(1)
        return 0
    previous = x.XSetErrorHandler(ctypes.cast(handler, ctypes.c_void_p))
    try:
        window = int(window_id, 16)
        protocols = ctypes.POINTER(ctypes.c_ulong)()
        count = ctypes.c_int(0)
        got = x.XGetWMProtocols(display, window, ctypes.byref(protocols), ctypes.byref(count))
        x.XSync(display, 0)
        if not got or errors:
            return False
        try:
            delete = x.XInternAtom(display, b"WM_DELETE_WINDOW", 1)
            supported = bool(delete) and any(protocols[i] == delete for i in range(count.value))
        finally:
            if bool(protocols):
                x.XFree(ctypes.cast(protocols, ctypes.c_void_p))
        if not supported:
            return False
        event = _XEvent()
        event.xclient.type = 33  # ClientMessage
        event.xclient.window = window
        event.xclient.message_type = x.XInternAtom(display, b"WM_PROTOCOLS", 1)
        event.xclient.format = 32
        event.xclient.data.l[0] = delete
        event.xclient.data.l[1] = 0  # CurrentTime
        ok = bool(x.XSendEvent(display, window, 0, 0, ctypes.byref(event)))
        x.XSync(display, 0)
        return ok and not errors
    finally:
        x.XSetErrorHandler(previous)
        x.XCloseDisplay(display)


def _kill_owned(proc: subprocess.Popen[bytes]) -> None:
    if proc.poll() is not None:
        proc.wait()
        return
    try:
        os.killpg(proc.pid, signal.SIGTERM)
    except ProcessLookupError:
        pass
    try:
        proc.wait(timeout=5)
    except subprocess.TimeoutExpired:
        try:
            os.killpg(proc.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        proc.wait(timeout=5)


def _launch_and_close(exe: Path, scratch: Path, env: dict[str, str], ordinal: int,
                      probe: Callable[[int], list[str]],
                      close_sender: Callable[[str], bool],
                      timeout: float, stable: float) -> tuple[bool, str, int]:
    try:
        flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_CLOEXEC | os.O_NOFOLLOW
        outfd = os.open(scratch / f"attempt-{ordinal}.stdout.log", flags, 0o600)
        try:
            errfd = os.open(scratch / f"attempt-{ordinal}.stderr.log", flags, 0o600)
        except OSError:
            os.close(outfd)
            raise
        with os.fdopen(outfd, "wb") as stdout, os.fdopen(errfd, "wb") as stderr:
            proc = subprocess.Popen([str(exe)], cwd=exe.parent, env=env,
                                    stdout=stdout, stderr=stderr, start_new_session=True)
            try:
                deadline = time.monotonic() + timeout
                windows: list[str] = []
                while time.monotonic() < deadline:
                    if proc.poll() is not None:
                        return False, "exited-before-window", proc.pid
                    windows = probe(proc.pid)
                    if windows:
                        break
                    time.sleep(0.1)
                if not windows:
                    return False, "window-timeout", proc.pid
                end_stable = time.monotonic() + stable
                while time.monotonic() < end_stable:
                    if proc.poll() is not None:
                        return False, "exited-after-window", proc.pid
                    time.sleep(0.1)
                # Reconfirm this same window belongs to the same live apphost
                # immediately before sending the normal window-close request.
                if proc.poll() is not None or windows[0] not in probe(proc.pid):
                    return False, "owned-window-lost", proc.pid
                if not close_sender(windows[0]):
                    return False, "wm-delete-not-supported", proc.pid
                try:
                    exit_code = proc.wait(timeout=min(timeout, 20))
                except subprocess.TimeoutExpired:
                    return False, "normal-close-timeout", proc.pid
                return (exit_code == 0,
                        "normal-close-exit-zero" if exit_code == 0 else "normal-close-nonzero-exit", proc.pid)
            finally:
                _kill_owned(proc)
    except Exception as exc:
        raise CloseError("Native normal-close launch or observation failed") from exc


def run_normal_close(executable: Path, scratch_parent: Path, *, timeout: float = 45,
                     stable: float = 0.5,
                     window_probe: Callable[[int], list[str]] | None = None,
                     close_sender: Callable[[str], bool] = send_wm_delete,
                     real_elf: bool = True) -> dict[str, object]:
    if not 0 < timeout <= 120 or not 0 <= stable <= 15:
        raise CloseError("Invalid close/reopen timeout")
    try:
        exe = _apphost(executable, real_elf=real_elf)
        _private_parent(scratch_parent)
    except (ReopenError, OSError, ValueError) as exc:
        raise CloseError("Unsafe executable or scratch parent") from exc
    if window_probe is None:
        from native_files_linux_smoke import x11_windows
        window_probe = x11_windows
    try:
        scratch = scratch_parent / "native-files-normal-close"
        scratch.mkdir(mode=0o700, exist_ok=False)
        profile = scratch / "profile"
        profile.mkdir(mode=0o700, exist_ok=False)
        for name in ("config", "data", "cache", "state", "haven-data"):
            (profile / name).mkdir(mode=0o700, exist_ok=False)
    except OSError as exc:
        raise CloseError("Cannot create exclusive disposable profile") from exc
    env = _env(profile, os.environ)
    inode = profile.stat().st_ino
    pids: list[int] = []
    reasons: list[str] = []
    for attempt in (1, 2):
        if profile.is_symlink() or profile.stat().st_ino != inode:
            raise CloseError("Disposable profile changed between launches")
        passed, reason, pid = _launch_and_close(exe, scratch, env, attempt,
                                                window_probe, close_sender, timeout, stable)
        reasons.append(reason)
        pids.append(pid)
        if not passed:
            break
    passed = len(reasons) == 2 and all(r == "normal-close-exit-zero" for r in reasons) and pids[0] != pids[1]
    return {
        "schema": "9to1.native-files-normal-close/v1",
        "status": "PASS" if passed else "FAIL",
        "attempts": len(reasons),
        "attempt_reasons": reasons,
        "independent_processes": len(pids) == 2 and pids[0] != pids[1],
        "same_disposable_profile": profile.stat().st_ino == inode,
        "close_type": "X11 WM_DELETE_WINDOW; zero-exit-required",
        "drive_authentication": "NOT_RUN",
        "drive_data_persistence": "NOT_RUN",
        "file_operations": "NOT_RUN",
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--executable", required=True, type=Path)
    parser.add_argument("--scratch-parent", required=True, type=Path)
    parser.add_argument("--timeout-seconds", type=float, default=45)
    args = parser.parse_args(argv)
    try:
        result = run_normal_close(args.executable, args.scratch_parent,
                                  timeout=args.timeout_seconds)
    except CloseError as exc:
        result = {"schema": "9to1.native-files-normal-close/v1", "status": "BLOCKED",
                  "reason": str(exc), "drive_authentication": "NOT_RUN",
                  "drive_data_persistence": "NOT_RUN"}
    print(json.dumps(result, sort_keys=True))
    return 0 if result["status"] == "PASS" else 2


if __name__ == "__main__":
    sys.exit(main())
