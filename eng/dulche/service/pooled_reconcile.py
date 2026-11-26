"""Retry proof of a terminated pooled lifetime without signalling any process.

Caller must hold EngineManager's resource.lock and lifecycle.lock, in that order.
Both device locks remain held through fresh proof and exact fence retirement.
"""
from __future__ import annotations

from contextlib import contextmanager, ExitStack
from dataclasses import asdict
import fcntl
import hashlib
import json
import math
import os
from pathlib import Path
import re
import stat
import subprocess
import time

import pooled_runtime as runtime

PROOF_SCHEMA = "dulche-pool-release-reconciliation/v1"
FENCE_SCHEMA = "dulche-pool-unverified-release/v1"


def _read(path):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC)
    with os.fdopen(fd, "rb") as stream:
        info = os.fstat(stream.fileno())
        if (not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid()
                or info.st_mode & 0o077 or not 1 <= info.st_size <= 262144):
            raise runtime.RuntimeError_("Reconciliation requires owned private regular records")
        raw = stream.read(262145)
    if len(raw) > 262144:
        raise runtime.RuntimeError_("Reconciliation record is excessive")
    value = json.loads(raw)
    if not isinstance(value, dict):
        raise runtime.RuntimeError_("Reconciliation record is not an object")
    return value, hashlib.sha256(raw).hexdigest()


@contextmanager
def _devices(directory):
    info = directory.lstat()
    if not stat.S_ISDIR(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o022:
        raise runtime.RuntimeError_("Reconciliation claim directory is not owned and protected")
    with ExitStack() as stack:
        for role in runtime.DEVICE_ROLES:
            fd = os.open(directory / runtime.LOCK_NAMES[role],
                         os.O_CREAT | os.O_NOFOLLOW | os.O_CLOEXEC | os.O_RDWR, 0o600)
            handle = stack.enter_context(os.fdopen(fd, "a+"))
            info = os.fstat(handle.fileno())
            if not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o077:
                raise runtime.RuntimeError_("Reconciliation device lock is not owned and private")
            try:
                fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError as exc:
                raise runtime.RuntimeError_("A model lifetime still holds the " + role + " lease") from exc
        yield


def _unit_stopped(name, invocation, timeout):
    fields = ("LoadState", "ActiveState", "MainPID", "ControlPID", "ControlGroup", "InvocationID")
    args = ["systemctl", "--user", "show", name]
    for field in fields:
        args += ["-p", field]
    result = subprocess.run(args, capture_output=True, text=True, timeout=timeout, check=False)
    unit = dict(line.split("=", 1) for line in result.stdout.splitlines() if "=" in line)
    collected = result.returncode == 1 and unit.get("LoadState") == "not-found"
    if ((result.returncode != 0 and not collected) or set(fields) - unit.keys()
            or unit["LoadState"] not in ("loaded", "not-found")
            or unit["ActiveState"] not in ("inactive", "failed")
            or unit["MainPID"] != "0" or unit["ControlPID"] != "0"
            or unit["InvocationID"] not in ("", invocation)):
        raise runtime.RuntimeError_("Pooled unit is active, replaced or not fully identified")
    if unit["LoadState"] == "not-found" and (unit["ActiveState"] != "inactive"
                                           or unit["ControlGroup"] or unit["InvocationID"]):
        raise runtime.RuntimeError_("Collected pooled unit still reports a lifetime identity")
    group = unit["ControlGroup"]
    if group:
        if (not group.startswith(f"/user.slice/user-{os.getuid()}.slice/")
                or not group.endswith("/" + name) or ".." in Path(group).parts):
            raise runtime.RuntimeError_("Pooled cgroup identity differs")
        path = Path("/sys/fs/cgroup") / group.lstrip("/")
        if path.exists():
            if not (path / "cgroup.procs").is_file():
                raise runtime.RuntimeError_("Pooled cgroup membership is unavailable")
            for members in path.rglob("cgroup.procs"):
                if members.read_text().strip():
                    raise runtime.RuntimeError_("Pooled cgroup still contains processes")
    return {key: unit[key] for key in fields}


def _host_processes_stopped(wrapper_pid, wrapper_start, backend_pid):
    proc = Path("/proc") / str(wrapper_pid)
    try:
        started = (proc / "stat").read_text().rsplit(")", 1)[1].split()[19]
    except FileNotFoundError:
        started = None
    if started == wrapper_start:
        raise runtime.RuntimeError_("The saved pooled wrapper still exists")
    # Older receipts did not save backend start ticks. Never guess on PID reuse.
    if (Path("/proc") / str(backend_pid)).exists():
        raise runtime.RuntimeError_("The saved backend PID still exists; release is ambiguous")


def _replace_status(path, value, expected):
    current, checksum = _read(path)
    if checksum != expected:
        raise runtime.RuntimeError_("Pooled status changed during reconciliation")
    raw = json.dumps(value, sort_keys=True).encode()
    return runtime._write_fence(path, raw, replacing=expected)


def reconcile_release(record_path, unit_name, *, runtime_dir=None, timeout=24):
    """Verify and retire only this recipe's ended residency, retaining failure evidence.

    This is called under the manager's shared physical and lifecycle locks.
    It performs no stop, kill, VM action or launch. A timeout or changed identity
    preserves admission fences. A write-ahead receipt permits safe retry after
    interruption between deleting the two matching fence files.
    """
    if type(timeout) not in (int, float) or not math.isfinite(timeout) or not 0 < timeout <= 60:
        raise ValueError("Reconciliation timeout must be positive and at most 60 seconds")
    record_path = Path(record_path).absolute()
    expected_unit = "dulche-pool-" + hashlib.sha256(str(record_path.parent.parent.resolve()).encode()).hexdigest()[:12] + ".service"
    if unit_name != expected_unit or record_path.name != unit_name + ".json":
        raise runtime.RuntimeError_("Reconciliation recipe does not identify this managed unit")
    directory = runtime._runtime_directory(runtime_dir)
    status_path = record_path.with_suffix(".pool-status.json")
    deadline = time.monotonic() + timeout

    def remaining(cap=8):
        value = deadline - time.monotonic()
        if value <= 0:
            raise TimeoutError("Pooled release reconciliation deadline expired")
        return min(cap, value)

    with _devices(directory):
        recipe, recipe_hash = _read(record_path)
        receipt, receipt_hash = _read(status_path)
        if (recipe.get("schema") != "dulche-pool-runtime/v1" or recipe.get("alias") != runtime.POOL_MODEL_ID
                or recipe.get("physical_devices") != list(runtime.DEVICE_ROLES)
                or not re.fullmatch(r"[a-f0-9]{32}", str(recipe.get("launch_id", "")))
                or not re.fullmatch(r"[a-f0-9]{64}", str(recipe.get("backend_sha256", "")))
                or receipt.get("schema") != "dulche-pool-status/v1"
                or receipt.get("launch_id") != recipe["launch_id"]
                or receipt.get("phase") not in ("stop_unverified", "stopped")
                or not re.fullmatch(r"[a-f0-9]{32}", str(receipt.get("systemd_invocation", "")))):
            raise runtime.RuntimeError_("Saved pooled recipe and terminal receipt do not match")
        pin = runtime.RpcProcess(**recipe["rpc_process"])
        pin.validate()
        if receipt.get("rpc_identity") != pin.key:
            raise runtime.RuntimeError_("Terminal receipt belongs to another guest RPC")
        wrapper_pid, backend_pid = receipt.get("wrapper_pid"), receipt.get("backend_pid")
        wrapper_start = receipt.get("wrapper_start_ticks")
        if (type(wrapper_pid) is not int or wrapper_pid <= 1 or type(backend_pid) is not int or backend_pid <= 1
                or not isinstance(wrapper_start, str) or not wrapper_start.isdigit()):
            raise runtime.RuntimeError_("Terminal receipt has invalid process identities")
        boot = Path("/proc/sys/kernel/random/boot_id").read_text().strip()
        previous_proof = receipt.get("reconciliation") or {}
        if not isinstance(previous_proof, dict):
            raise runtime.RuntimeError_("Prior reconciliation proof is malformed")
        if previous_proof and (previous_proof.get("schema") != PROOF_SCHEMA
                               or previous_proof.get("launch_id") != recipe["launch_id"]
                               or previous_proof.get("host_boot_id") != boot
                               or previous_proof.get("rpc_identity") != pin.key
                               or any(previous_proof.get(key) is not True for key in (
                                   "host_wrapper_absent", "host_backend_absent", "host_compute_clear", "guest_rpc_released"))):
            raise runtime.RuntimeError_("Prior reconciliation proof belongs to another lifetime")
        expected = previous_proof.get("fence_sha256", {})
        if previous_proof and (not isinstance(expected, dict) or set(expected) != set(runtime.DEVICE_ROLES)
                               or len(set(expected.values())) != 1
                               or not all(re.fullmatch(r"[a-f0-9]{64}", str(value)) for value in expected.values())):
            raise runtime.RuntimeError_("Prior retirement receipt lacks exact paired fence digests")
        fences = {}
        fence_value = None
        for role in runtime.DEVICE_ROLES:
            path = runtime._quarantine_path(directory, role)
            if not os.path.lexists(path):
                if not re.fullmatch(r"[a-f0-9]{64}", str(expected.get(role, ""))):
                    raise runtime.RuntimeError_("A residency fence is missing without an exact retirement receipt")
                continue
            value, checksum = _read(path)
            if (value.get("schema") != FENCE_SCHEMA or value.get("host_boot_id") != boot
                    or value.get("host_wrapper_pid") != wrapper_pid
                    or value.get("host_wrapper_start_ticks") != wrapper_start
                    or value.get("backend_sha256") != recipe.get("backend_sha256")
                    or not re.fullmatch(r"[a-f0-9]{64}", str(value.get("argv_sha256", "")))
                    or not re.fullmatch(r"[a-f0-9]{48}", str(value.get("owner_token", "")))
                    or value.get("guest_rpc") != asdict(pin)):
                raise runtime.RuntimeError_("Residency fence does not own the saved host and guest lifetime")
            if fence_value is not None and value != fence_value:
                raise runtime.RuntimeError_("The two physical fences describe different lifetimes")
            if fences and checksum != next(iter(fences.values())):
                raise runtime.RuntimeError_("The paired physical fence bytes differ")
            if expected and expected.get(role) != checksum:
                raise runtime.RuntimeError_("Residency fence differs from the prior retirement receipt")
            fence_value = value
            fences[path] = checksum
        hashes = dict(expected) if expected else {role: fences[runtime._quarantine_path(directory, role)] for role in runtime.DEVICE_ROLES}

        _unit_stopped(unit_name, receipt["systemd_invocation"], remaining())
        _host_processes_stopped(wrapper_pid, wrapper_start, backend_pid)
        display = runtime.capture_display(timeout=remaining(3))
        host = runtime.nvml_snapshot(runtime.RTX_UUID, remaining(3))
        if runtime.host_compute_allocations(host["allocations"], display_pin=display, timeout=remaining(3)):
            raise runtime.RuntimeError_("Host compute allocations remain; preserving both physical fences")
        if runtime.GuestRPC(pin).stopped(remaining()) is not True:
            raise runtime.RuntimeError_("Pinned guest RPC allocation release is still unverified")
        unit = _unit_stopped(unit_name, receipt["systemd_invocation"], remaining())
        _host_processes_stopped(wrapper_pid, wrapper_start, backend_pid)
        if _read(record_path)[1] != recipe_hash or _read(status_path)[1] != receipt_hash:
            raise runtime.RuntimeError_("Saved lifecycle records changed during release proof")
        for path, checksum in fences.items():
            if _read(path)[1] != checksum:
                raise runtime.RuntimeError_("Physical residency fence changed during release proof")
        remaining()
        proof = {"schema": PROOF_SCHEMA, "launch_id": recipe["launch_id"], "host_boot_id": boot,
                 "verified_at": time.time(), "previous_status_sha256": receipt_hash,
                 "original_status_sha256": previous_proof.get("original_status_sha256", receipt_hash),
                 "original_phase": previous_proof.get("original_phase", receipt["phase"]),
                 "fence_sha256": hashes, "fences_retired": False,
                 "host_unit": unit, "host_wrapper_absent": True, "host_backend_absent": True,
                 "host_compute_clear": True, "guest_rpc_released": True, "rpc_identity": pin.key}
        updated = {**receipt, "phase": "stopped", "status": "stopped", "stop_verified": True,
                   "observed_at": time.time(), "reconciliation": proof}
        # Keep original trigger, errors and exit_code; they remain diagnostic evidence.
        receipt_hash = _replace_status(status_path, updated, receipt_hash)
        runtime._retire_residency(fences)
        fd = os.open(directory, os.O_RDONLY | os.O_DIRECTORY | os.O_CLOEXEC)
        try:
            os.fsync(fd)
        finally:
            os.close(fd)
        updated["reconciliation"] = {**proof, "fences_retired": True}
        _replace_status(status_path, updated, receipt_hash)
        return updated
