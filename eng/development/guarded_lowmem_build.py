#!/usr/bin/env python3
"""Run a heavy build/test command under a live memory floor, so a build can never take the machine down.

This exists because the resident model host on this PC is this session's own inference server: an OOM kill
would most likely land on the largest process, which is exactly the one that must never be stopped. The
owner's instruction of 31 October 2026 is to proceed with framework builds anyway, slower if necessary,
rather than deferring the work - so the guard is what makes "proceed" safe.

What it does:
  * starts the command in its own process group, at idle CPU/IO priority;
  * samples MemAvailable continuously;
  * if MemAvailable drops to the floor, TERM then KILL the whole group and report the guard as the reason;
  * records the observed minimum MemAvailable, the process tree's peak RSS, wall time and the exact command,
    so the outcome is evidence and not a guess.

It grants no acceptance. A build that is stopped by the memory guard is PARTIAL and NOT REUSABLE.
"""
from __future__ import annotations

import argparse
import json
import os
import signal
import subprocess
import sys
import time
from pathlib import Path

DEFAULT_FLOOR_MIB = 1100          # keep this much memory free for the resident host and the desktop


def mem_available_kib() -> int:
    for line in Path("/proc/meminfo").read_text().splitlines():
        if line.startswith("MemAvailable:"):
            return int(line.split()[1])
    raise RuntimeError("MemAvailable is not reported by this kernel")


def tree_rss_kib(root_pid: int) -> int:
    """Sum RSS of a process and all descendants by walking /proc, with no external tools."""
    children: dict[int, list[int]] = {}
    stat: dict[int, tuple[int, int, int]] = {}
    for entry in Path("/proc").iterdir():
        if not entry.name.isdigit():
            continue
        pid = int(entry.name)
        try:
            fields = (entry / "stat").read_text().rsplit(")", 1)
            parts = fields[1].split()
            ppid, rss = int(parts[1]), int(parts[21])
            stat[pid] = (ppid, rss, pid)
            children.setdefault(ppid, []).append(pid)
        except (OSError, ValueError, IndexError):
            continue
    total, stack = 0, [root_pid]
    while stack:
        pid = stack.pop()
        if pid in stat:
            total += stat[pid][1]
        stack.extend(children.get(pid, []))
    return total


def guarded(command, floor_mib=DEFAULT_FLOOR_MIB, timeout=3600, log=None, nice=True):
    """Run command under a memory floor. Returns an outcome dict; never trusts the child to survive."""
    floor_kib = floor_mib * 1024
    env = dict(os.environ)
    env.update({"DOTNET_gcServer": "0",                    # server GC pins heaps aggressively
                "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
                "DOTNET_NOLOGO": "1",
                "MSBUILDDISABLENODEREUSE": "1",
                "DOTNET_PARALLEL_COMPILE": "0"})
    pre = []
    if nice:
        # "slow down the build a bit" (owner, 31 October 2026): idle priority keeps the model host responsive.
        pre = ["nice", "-n", "19", "ionice", "-c3"] if _have("ionice") else ["nice", "-n", "19"]
    started = time.monotonic()
    tail = []
    handle = open(log, "ab") if log else None
    try:
        proc = subprocess.Popen(pre + list(command), cwd=None, env=env,
                                stdout=(handle or subprocess.DEVNULL),
                                stderr=subprocess.STDOUT, start_new_session=True)
    except OSError as exc:
        if handle:
            handle.close()
        return {"command": list(command), "result": "FAILED-TO-START", "detail": str(exc)}

    min_available, peak_rss, reason, code = mem_available_kib(), 0, "exited", None
    try:
        while True:
            alive = proc.poll() is None
            available = mem_available_kib()
            min_available = min(min_available, available)
            if alive:
                peak_rss = max(peak_rss, tree_rss_kib(proc.pid))
            if available <= floor_kib and alive:
                reason = f"MEM GUARD: MemAvailable fell to {available // 1024} MiB (floor {floor_mib} MiB)"
                os.killpg(os.getpgid(proc.pid), signal.SIGTERM)
                deadline = time.monotonic() + 10
                while proc.poll() is None and time.monotonic() < deadline:
                    time.sleep(0.2)
                if proc.poll() is None:
                    os.killpg(os.getpgid(proc.pid), signal.SIGKILL)
                    reason += "; SIGKILL required"
                code = proc.poll()
                break
            if not alive:
                code = proc.returncode
                break
            if time.monotonic() - started > timeout:
                reason = f"TIME GUARD: exceeded {timeout}s"
                os.killpg(os.getpgid(proc.pid), signal.SIGKILL)
                code = proc.poll()
                break
            time.sleep(0.5)
    finally:
        if handle:
            handle.close()
    outcome = {"command": list(command), "result": "OK" if (code == 0 and reason == "exited") else reason,
               "exit_code": code, "signal_reason": reason,
               "seconds": round(time.monotonic() - started, 1),
               "min_mem_available_mib": min_available // 1024,
               "peak_process_tree_rss_mib": peak_rss // 1024,
               "floor_mib": floor_mib,
               "evidence_note": "Compile/test evidence only. Nothing here renders, runs natively, packages "
                                "or accepts anything. A guard stop means PARTIAL and NOT REUSABLE."}
    if log:
        Path(str(log) + ".guard.json").write_text(json.dumps(outcome, indent=2) + "\n")
    return outcome


def _have(tool: str) -> bool:
    from shutil import which
    return which(tool) is not None


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--floor-mib", type=int, default=DEFAULT_FLOOR_MIB)
    ap.add_argument("--timeout", type=int, default=3600)
    ap.add_argument("--log", default=None, help="where to keep the command output")
    ap.add_argument("--no-nice", action="store_true")
    ap.add_argument("command", nargs=argparse.REMAINDER)
    args = ap.parse_args(argv)
    command = [c for c in args.command if c != "--"]
    if not command:
        print("REFUSED: no command given", file=sys.stderr)
        return 2
    outcome = guarded(command, args.floor_mib, args.timeout, args.log, nice=not args.no_nice)
    print(json.dumps(outcome, indent=2))
    return 0 if outcome["result"] == "OK" else (124 if "MEM GUARD" in outcome["result"] else 1)


if __name__ == "__main__":
    sys.exit(main())
