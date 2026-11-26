"""Stop only this Dulche instance's identity-proven RTX inference units.

Invoked by the independent shared thermal guard. This helper neither changes
GPU settings. A pooled model also requires the existing guest stop chain.
Synthetic tests use an isolated
DULCHE_STATE_DIR; production inherits the user's existing default state root.
"""
from __future__ import annotations

import argparse
import os
from pathlib import Path
import subprocess
import time

from engine_profiles import INKLING, INKLING_POOL, PRIMARY, EngineError
from engines import EngineManager

MODELS = (INKLING, PRIMARY, INKLING_POOL)


def query_unit(name):
    fields = ("LoadState", "MainPID", "ActiveState", "ControlGroup", "InvocationID")
    argv = ["systemctl", "--user", "show", name]
    for field in fields:
        argv.extend(("-p", field))
    result = subprocess.run(argv, capture_output=True, text=True, timeout=3)
    state = dict(line.split("=", 1) for line in result.stdout.splitlines() if "=" in line)
    absent = state.get("LoadState") == "not-found" and state.get("MainPID") == "0" and not state.get("ControlGroup")
    if set(fields) - set(state) or (result.returncode and not absent):
        raise EngineError("Cannot inspect native unit; absence has not been proven")
    return state


def members(state, group=None):
    group = group if group is not None else state.get("ControlGroup", "")
    inactive = state.get("MainPID") == "0" and state.get("ActiveState") in ("inactive", "failed")
    if not group:
        if inactive:
            return set()
        raise EngineError("Active native unit has no inspectable cgroup")
    if not group.startswith("/user.slice/") or ".." in Path(group).parts:
        raise EngineError("Invalid native cgroup")
    root = Path("/sys/fs/cgroup" + group)
    try:
        root.stat()
    except FileNotFoundError:
        if inactive:
            return set()
        raise EngineError("Native cgroup disappeared without an inactive unit")
    found = set()
    def failed(error):
        raise error
    for directory, _, files in os.walk(root, onerror=failed):
        if "cgroup.procs" not in files:
            raise EngineError("Cannot inspect native cgroup membership")
        found.update(int(pid) for pid in (Path(directory) / "cgroup.procs").read_text().split())
    return found


def owned(manager, mid):
    if mid not in MODELS:
        raise EngineError("Native stop cannot target a non-native model")
    name = manager._name(mid)
    state = query_unit(name)
    pids = members(state)
    pid = int(state.get("MainPID") or 0)
    if not pid and not pids:
        if state.get("ActiveState") in ("inactive", "failed"):
            return None
        raise EngineError("Native unit is changing state; absence has not been proven")
    group = state.get("ControlGroup", "")
    if (pid <= 1 or not group.startswith("/user.slice/")
            or not group.endswith("/" + name)
            or pid not in pids
            or (Path("/proc") / str(pid)).stat().st_uid != os.getuid()
            or not manager._owns({"id": mid}, state, {})):
        raise EngineError("Refusing native stop: process/unit ownership was not proven")
    invocation = state.get("InvocationID")
    if not invocation:
        raise EngineError("Native unit lacks an invocation identity")
    return {"name": name, "invocation": invocation, "group": group}


def remaining(target):
    state = query_unit(target["name"])
    pids = members(state, target["group"])
    if not pids:
        return set()
    if state.get("InvocationID") != target["invocation"] or state.get("ControlGroup") != target["group"]:
        raise EngineError("Native unit identity changed during stop; refusing further actions")
    return pids


def command(*args):
    result = subprocess.run(["systemctl", "--user", *args], capture_output=True,
                            text=True, timeout=3)
    if result.returncode:
        raise EngineError((result.stderr or "Native unit command failed")[:500])


def control_host(action, manager=None, models=MODELS):
    if action not in ("stop", "kill", "verify"):
        raise EngineError("Unknown native stop action")
    manager = manager or EngineManager()
    if any(mid not in MODELS for mid in models):
        raise EngineError("Thermal stop received an unknown model")
    targets, failures = [], []
    for mid in models:
        try:
            target = owned(manager, mid)
            if target is not None:
                targets.append(target)
        except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as exc:
            failures.append(str(exc))
    if action == "verify":
        if targets or failures:
            raise EngineError("An owned native inference process is still running")
        return []
    for target in targets:
        if not remaining(target):
            continue
        if action == "kill":
            command("kill", "--kill-whom=all", "--signal=KILL", target["name"])
        else:
            command("stop", "--no-block", target["name"])
    deadline = time.monotonic() + 5
    while any(remaining(target) for target in targets) and time.monotonic() < deadline:
        time.sleep(0.1)
    for target in targets:
        if remaining(target):
            command("kill", "--kill-whom=all", "--signal=KILL", target["name"])
    deadline = time.monotonic() + 2
    while any(remaining(target) for target in targets) and time.monotonic() < deadline:
        time.sleep(0.1)
    if any(remaining(target) for target in targets):
        raise EngineError("Native inference stop could not be verified")
    if failures:
        raise EngineError("Some unit identities could not be verified: " + "; ".join(failures))
    return [target["name"] for target in targets]


def control(action, manager=None):
    manager = manager or EngineManager()
    from pooled_guard import parallel, guest_action, pool_may_own_guest
    names = []
    def stop_pooled_guest():
        if pool_may_own_guest(manager):
            guest_action(action, manager)
    parallel({"rtx": lambda: names.extend(control_host(action, manager)),
              "pooled_guest": stop_pooled_guest})
    return names


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("stop", "kill", "verify"))
    args = parser.parse_args()
    try:
        names = control(args.action)
        print("native_guard", args.action, "verified", ",".join(names), flush=True)
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as exc:
        parser.exit(1, "native_guard: " + str(exc) + "\n")


if __name__ == "__main__":
    main()
