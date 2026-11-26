"""Preserve the V100 guard's stop chain while also stopping a pooled host.

Both actions start independently. A blocked host stop cannot prevent the
existing guest stop/escalation from running within the guard's 15-second call.
No residency fence is erased by a thermal action.
"""
from __future__ import annotations

import argparse
import os
from pathlib import Path
import subprocess
import threading
import time

from engine_profiles import EngineError, INKLING_POOL


def parallel(actions, timeout=13):
    done, errors = {}, {}
    lock = threading.Lock()
    def invoke(name, callback):
        try:
            callback()
        except Exception as exc:
            with lock:
                errors[name] = type(exc).__name__ + ": " + str(exc)[:300]
        finally:
            with lock:
                done[name] = True
    for name, callback in actions.items():
        threading.Thread(target=invoke, args=(name, callback), daemon=True).start()
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        with lock:
            if len(done) == len(actions):
                break
        time.sleep(0.02)
    with lock:
        failures = {**errors, **{name: "deadline exceeded" for name in actions if name not in done}}
    if failures:
        raise EngineError("Independent thermal stops were not all verified: " + str(failures))


def legacy_guest_action(action):
    choice = {"stop": "stop-worker", "kill": "emergency-detach", "verify": "verify-stopped"}[action]
    result = subprocess.run(["/usr/bin/python3", str(Path(__file__).with_name("v100_recover.py").resolve()), choice],
                            capture_output=True, text=True, timeout=12)
    if result.returncode:
        raise EngineError((result.stderr or "V100 guard action failed")[:400])


def paired_rpc(manager, timeout):
    """Recover only an exact durable pool identity; never adopt a legacy worker."""
    from pooled_runtime import (LOCK_NAMES, RPC_START_SCHEMA, RpcProcess,
                                read_private_json, recover_pooled_rpc)
    runtime = Path(os.environ.get("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}"))
    pins, startup = [], []
    for name in LOCK_NAMES.values():
        path = runtime / (name + ".unverified.json")
        if not os.path.lexists(path):
            continue
        saved = read_private_json(path)
        if saved.get("schema") == RPC_START_SCHEMA:
            recipe = {**saved, "alias": INKLING_POOL, "physical_devices": ["rtx5080", "v100"]}
            if recipe not in startup:
                startup.append(recipe)
        elif saved.get("schema") == "dulche-pool-unverified-release/v1":
            pin = RpcProcess(**saved["guest_rpc"])
            pin.validate()
            pins.append(pin)
        else:
            raise EngineError("The pooled residency fence has no recognized RPC identity")
    if startup:
        # Both device markers are written from one exact record. Conflicting
        # startup identities require escalation, never a best-guess target.
        if len(startup) != 1:
            raise EngineError("Conflicting pooled RPC startup identities")
        pin = recover_pooled_rpc(startup[0], timeout=timeout)
        if pin is not None:
            pins.append(pin)
    if pins:
        if any(pin != pins[0] for pin in pins[1:]):
            raise EngineError("Pooled device fences name different RPC owners")
        return pins[0]
    if startup:
        return None  # The guest itself proved no tagged process or allocation.
    from native_guard import query_unit
    unit = query_unit(manager._name(INKLING_POOL))
    if unit.get("MainPID") in (None, "0") and unit.get("ActiveState") in ("inactive", "failed"):
        return None
    rec = read_private_json(manager._record(INKLING_POOL))
    if rec.get("alias") != INKLING_POOL or rec.get("physical_devices") != ["rtx5080", "v100"]:
        raise EngineError("Active pool has no exact physical recipe")
    if rec.get("rpc_process"):
        pin = RpcProcess(**rec["rpc_process"])
        pin.validate()
        if (pin.gpu_uuid != rec.get("v100_uuid") or pin.executable != rec.get("rpc_path")
                or pin.executable_sha256 != rec.get("rpc_sha256")):
            raise EngineError("Saved pooled RPC process differs from its frozen launch")
        return pin
    return recover_pooled_rpc(rec, timeout=timeout)


def paired_guest_action(action, manager):
    """Thermal actions stop/verify the new RPC and leave all admission fences."""
    from pooled_runtime import GuestRPC, stop_pooled_rpc
    deadline = time.monotonic() + 10
    pin = paired_rpc(manager, timeout=4)
    if pin is None:
        return
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise EngineError("Paired RPC identity observation exceeded its deadline")
    if action == "verify":
        if GuestRPC(pin).stopped(remaining) is not True:
            raise EngineError("Paired RPC process or its GPU allocations remain")
    else:
        stop_pooled_rpc(pin, timeout=remaining)


def guest_action(action, manager):
    # The legacy escalation can detach the owned VM even if receipt recovery,
    # SSH, or the new RPC stop fails. Neither branch waits for the other first.
    parallel({"paired_rpc": lambda: paired_guest_action(action, manager),
              "legacy_v100": lambda: legacy_guest_action(action)}, timeout=12.5)


def pool_may_own_guest(manager):
    """A pooled unit or its durable release fence also requires the guest stop."""
    from native_guard import query_unit
    from pooled_runtime import LOCK_NAMES
    state = query_unit(manager._name(INKLING_POOL))
    if state.get("MainPID") not in (None, "0") or state.get("ActiveState") in ("active", "activating", "deactivating"):
        return True
    runtime = Path(os.environ.get("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}"))
    return any(os.path.lexists(runtime / (name + ".unverified.json")) for name in LOCK_NAMES.values())


def control(action, manager=None):
    from engines import EngineManager
    from native_guard import control_host
    manager = manager or EngineManager()
    parallel({"pooled_host": lambda: control_host(action, manager, (INKLING_POOL,)),
              "v100": lambda: guest_action(action, manager)})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("stop", "kill", "verify"))
    args = parser.parse_args()
    try:
        control(args.action)
        print("pooled_guard", args.action, "verified", flush=True)
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as exc:
        parser.exit(1, "pooled_guard: " + str(exc) + "\n")


if __name__ == "__main__":
    main()
