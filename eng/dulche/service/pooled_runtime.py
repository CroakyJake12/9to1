"""Dulche's leased CUDA/RPC worker, separate from its HTTP/API lifecycle owner.

Integration contract: EngineManager holds its existing resource.lock while it
checks admission and launches this worker. The worker acquires *device* locks,
never resource.lock (load waits for readiness while holding resource.lock).
Every other GPU launcher must call assert_devices_available under resource.lock.

The backend planner supplies bounded argv, not a shell command. This module
owns the child and a separately identity-pinned RPC process. It does not start,
detach, reconfigure or authenticate the VM. An independent thermal guard must
already cover the worker's unit as well as both existing device stop paths.
"""
from __future__ import annotations

from contextlib import contextmanager, ExitStack
from dataclasses import asdict, dataclass
import argparse
import fcntl
import hashlib
import json
import math
import os
from pathlib import Path
import re
import secrets
import signal
import stat
import subprocess
import sys
import threading
import time
from typing import Callable, Mapping

from pooled_backend import CommandPlan, GuestIdentity, RPC_ENDPOINT, RTX_UUID, digest, refresh_fitting_targets
from pooled_budget import PoolPolicy, Snapshot, budget_pool
from pooled_health import DevicePolicy, evaluate, supervise

GPU_POOL = "RTX 5080 + Tesla V100"
POOL_MODEL_ID = "inkling-2-mini-pooled-5080-v100"
DEVICE_ROLES = ("rtx5080", "v100")
LOCK_NAMES = {"rtx5080": "dulche-rtx5080.lock", "v100": "dulche-v100-gguf.lock"}
DISPLAY_EXECUTABLE = "/usr/libexec/gnome-remote-desktop-daemon"
RPC_START_SCHEMA = "dulche-pool-rpc-startup/v1"
MIB = 1 << 20


class RuntimeError_(RuntimeError):
    """A concrete pooled-runtime admission or ownership failure."""


def _integer(value, *, positive=False):
    return type(value) is int and value >= int(positive)


def _runtime_directory(runtime_dir):
    return Path(runtime_dir or os.environ.get("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}"))


def _quarantine_path(directory, role):
    return directory / (LOCK_NAMES[role] + ".unverified.json")


@contextmanager
def claim_devices(roles=DEVICE_ROLES, *, runtime_dir=None, startup_id=None):
    """All-or-nothing, lifetime flock claims; use the existing V100 lock name.

    Fixed ordering prevents competing pools acquiring one device each. Partial
    failure closes every descriptor. Lock files are never unlinked on release,
    because that could create a second lock inode while another caller waits.
    """
    roles = tuple(roles)
    if not roles or len(set(roles)) != len(roles) or set(roles) - set(DEVICE_ROLES):
        raise ValueError("Unknown or duplicate physical device claim")
    directory = _runtime_directory(runtime_dir)
    info = directory.lstat()
    if not stat.S_ISDIR(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o022:
        raise RuntimeError_("Physical claim directory is not owned and protected")
    with ExitStack() as stack:
        for role in DEVICE_ROLES:
            if role not in roles:
                continue
            fd = os.open(directory / LOCK_NAMES[role], os.O_CREAT | os.O_NOFOLLOW | os.O_CLOEXEC | os.O_RDWR, 0o600)
            handle = stack.enter_context(os.fdopen(fd, "a+"))
            info = os.fstat(handle.fileno())
            if not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o077:
                raise RuntimeError_("Physical claim file is not owned and private")
            try:
                fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError as exc:
                raise RuntimeError_(role + " is owned by another model lifetime") from exc
            if os.path.lexists(_quarantine_path(directory, role)):
                if startup_id is None:
                    raise RuntimeError_(role + " has an unverified prior release; physical reconciliation is required")
                pending = read_private_json(_quarantine_path(directory, role))
                if (pending.get("schema") != RPC_START_SCHEMA
                        or pending.get("launch_id") != startup_id):
                    raise RuntimeError_(role + " has an unverified prior release; physical reconciliation is required")
        yield


def _write_fence(path, raw, *, replacing=None):
    temporary = path if replacing is None else path.with_name(path.name + "." + secrets.token_hex(8) + ".tmp")
    fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600)
    try:
        with os.fdopen(fd, "wb") as handle:
            handle.write(raw)
            handle.flush()
            os.fsync(handle.fileno())
        if replacing is not None:
            if digest(path) != replacing:
                raise RuntimeError_("Startup fence changed before residency transfer")
            os.replace(temporary, path)
        directory = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY | os.O_CLOEXEC)
        try:
            os.fsync(directory)
        finally:
            os.close(directory)
    finally:
        if temporary != path and temporary.exists():
            temporary.unlink()
    return hashlib.sha256(raw).hexdigest()


def _arm_residency(runtime_dir, *, guest, plan, startup_id=None):
    """Arm crash-persistent admission fences BEFORE starting any child.

    EngineManager may retire these records only after independently proving
    the recorded wrapper's unit and pinned guest are absent and allocations
    clear. Normal cleanup retires only the exact bytes this invocation wrote.
    No blind public 'clear' operation is provided.
    """
    record = {"schema": "dulche-pool-unverified-release/v1", "created_at": time.time(),
              "host_boot_id": Path("/proc/sys/kernel/random/boot_id").read_text().strip(),
              "owner_token": secrets.token_hex(24), "host_wrapper_pid": os.getpid(),
              "host_wrapper_start_ticks": Path("/proc/self/stat").read_text().rsplit(")", 1)[1].split()[19],
              "backend_sha256": plan.executable_sha256,
              "argv_sha256": hashlib.sha256(json.dumps(plan.argv).encode()).hexdigest(),
              "guest_rpc": asdict(guest.pin)}
    raw = json.dumps(record, sort_keys=True).encode()
    armed = {}
    for role in DEVICE_ROLES:
        path = _quarantine_path(_runtime_directory(runtime_dir), role)
        replacing = None
        if startup_id is not None:
            pending = read_private_json(path)
            if (pending.get("schema") != RPC_START_SCHEMA or pending.get("launch_id") != startup_id
                    or pending.get("rpc_path") != guest.pin.executable
                    or pending.get("rpc_sha256") != guest.pin.executable_sha256
                    or pending.get("v100_uuid") != guest.pin.gpu_uuid):
                raise RuntimeError_("Startup fence does not own the pinned guest RPC")
            replacing = digest(path)
        armed[path] = _write_fence(path, raw, replacing=replacing)
    return armed


def _retire_residency(armed):
    for path, expected in armed.items():
        info = path.lstat()
        if not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid() or digest(path) != expected:
            raise RuntimeError_("Physical residency fence changed; preserving it for reconciliation")
        path.unlink()


def assert_devices_available(roles, *, runtime_dir=None):
    """Admission probe; caller MUST retain EngineManager's resource.lock."""
    with claim_devices(roles, runtime_dir=runtime_dir):
        pass


def read_private_json(path):
    """Read one bounded recipe without following a final symlink."""
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC)
    with os.fdopen(fd, "r") as handle:
        info = os.fstat(handle.fileno())
        if (not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid()
                or info.st_mode & 0o077 or not 1 <= info.st_size <= 262144):
            raise RuntimeError_("Pooled recipe is not an owned private regular file")
        value = json.load(handle)
    if not isinstance(value, dict):
        raise RuntimeError_("Pooled recipe is not an object")
    return value


# Minimal, versioned NVML ABI; no pip module or inference runtime dependency.
# NVIDIA API: nvmlMemory_t (three uint64 fields), nvmlProcessInfo_t (pid,
# usedGpuMemory, gpuInstanceId, computeInstanceId), ComputeRunningProcesses_v3.
# Run this in a bounded subprocess so a blocked driver query cannot freeze the
# supervising thread. Both the host and the already isolated guest use it.
_NVML_SOURCE = r'''
import ctypes as C
import json
def nvml_snapshot(expected_uuid):
    class Memory(C.Structure):
        _fields_ = [("total", C.c_ulonglong), ("free", C.c_ulonglong), ("used", C.c_ulonglong)]
    class Process(C.Structure):
        _fields_ = [("pid", C.c_uint), ("usedGpuMemory", C.c_ulonglong),
                    ("gpuInstanceId", C.c_uint), ("computeInstanceId", C.c_uint)]
    lib = C.CDLL("libnvidia-ml.so.1")
    def call(name, args, *values):
        fn = getattr(lib, name)
        fn.restype, fn.argtypes = C.c_int, args
        result = fn(*values)
        if result != 0:
            raise RuntimeError("NVML " + name + " failed: " + str(result))
    call("nvmlInit_v2", [])
    try:
        handle = C.c_void_p()
        call("nvmlDeviceGetHandleByUUID", [C.c_char_p, C.POINTER(C.c_void_p)],
             expected_uuid.encode("ascii"), C.byref(handle))
        uuid, name = C.create_string_buffer(96), C.create_string_buffer(128)
        call("nvmlDeviceGetUUID", [C.c_void_p, C.c_char_p, C.c_uint], handle, uuid, len(uuid))
        call("nvmlDeviceGetName", [C.c_void_p, C.c_char_p, C.c_uint], handle, name, len(name))
        if uuid.value.decode() != expected_uuid:
            raise RuntimeError("NVML physical GPU UUID differs")
        fn = lib.nvmlDeviceGetComputeRunningProcesses_v3
        fn.restype = C.c_int
        fn.argtypes = [C.c_void_p, C.POINTER(C.c_uint), C.POINTER(Process)]
        count = C.c_uint(0)
        status = fn(handle, C.byref(count), None)
        rows = []
        for attempt in range(4):
            if status == 0 and count.value == 0:
                break
            if status != 7 or not 0 < count.value <= 4096:
                raise RuntimeError("NVML process count unavailable: " + str(status))
            size = min(4096, count.value + 8)
            data = (Process * size)()
            count.value = size
            status = fn(handle, C.byref(count), data)
            if status == 0:
                rows = [{"pid": int(p.pid), "gpu_uuid": expected_uuid, "bytes": int(p.usedGpuMemory)}
                        for p in data[:count.value]]
                break
        else:
            raise RuntimeError("NVML process membership did not stabilize")
        if any(row["bytes"] == (1 << 64) - 1 or row["pid"] <= 1 for row in rows):
            raise RuntimeError("NVML process memory is unavailable")
        memory = Memory()
        call("nvmlDeviceGetMemoryInfo", [C.c_void_p, C.POINTER(Memory)], handle, C.byref(memory))
        if memory.total <= 0 or memory.used > memory.total or memory.free > memory.total:
            raise RuntimeError("NVML memory counters are invalid")
        return {"gpu_uuid": expected_uuid, "name": name.value.decode(), "allocations": rows,
                "memory": {"mem_total": int(memory.total), "mem_used": int(memory.used),
                           "mem_free": int(memory.free)}}
    finally:
        call("nvmlShutdown", [])
'''


def nvml_snapshot(uuid, timeout, *, runner=subprocess.run):
    source = _NVML_SOURCE + "\nprint(json.dumps(nvml_snapshot(" + repr(uuid) + ")))\n"
    env = dict(os.environ)
    env.pop("LD_PRELOAD", None)
    env.pop("LD_LIBRARY_PATH", None)  # Read the installed driver's NVML, not a build stub.
    reply = runner([sys.executable, "-I", "-c", source], stdin=subprocess.DEVNULL,
                   capture_output=True, text=True, timeout=timeout, check=True, env=env)
    if len(reply.stdout) > 262144:
        raise RuntimeError_("NVML observation exceeded its bound")
    result = json.loads(reply.stdout)
    if not isinstance(result, dict) or result.get("gpu_uuid") != uuid:
        raise RuntimeError_("NVML observation has a different physical identity")
    return result


def _owned_json(path):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC)
    with os.fdopen(fd, "r") as stream:
        info = os.fstat(stream.fileno())
        if not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o022 or info.st_size > 1048576:
            raise RuntimeError_("Guard configuration or snapshot is not an owned bounded file")
        return json.load(stream)


def _guard_unit(timeout):
    fields = ("MainPID", "ActiveState", "InvocationID", "ControlGroup")
    args = ["systemctl", "--user", "show", "dulche-v100-thermal.service"]
    for field in fields:
        args += ["-p", field]
    result = subprocess.run(args, capture_output=True, text=True, timeout=timeout, check=True)
    value = dict(line.split("=", 1) for line in result.stdout.splitlines() if "=" in line)
    if value.get("ActiveState") != "active" or set(fields) - value.keys():
        raise RuntimeError_("Independent guard is not active and identified")
    return value


@dataclass(frozen=True)
class GuardIdentity:
    pid: int
    start_ticks: str
    invocation: str
    cmdline_sha256: str
    config_path: str
    config_sha256: str
    status_path: str
    executable: str
    control_group: str


def capture_guard(config_path, *, timeout=3.0):
    """Pin the real running guard; accepts Python's harmless -B interpreter flag."""
    path = Path(config_path).absolute()
    config = _owned_json(path)
    state = _guard_unit(timeout)
    pid = int(state["MainPID"])
    if pid <= 1 or not state["InvocationID"] or not state["ControlGroup"].endswith("/dulche-v100-thermal.service"):
        raise RuntimeError_("Independent guard lacks a process/unit identity")
    proc = Path("/proc") / str(pid)
    if proc.stat().st_uid != os.getuid():
        raise RuntimeError_("Independent guard is not owned by this user")
    executable = (proc / "exe").resolve(strict=True)
    if executable != Path("/usr/bin/python3").resolve(strict=True):
        raise RuntimeError_("Independent guard uses an unexpected interpreter")
    groups = [line.split(":", 2)[2] for line in (proc / "cgroup").read_text().splitlines()
              if line.startswith("0::")]
    if groups != [state["ControlGroup"]]:
        raise RuntimeError_("Independent guard process does not belong to its unit cgroup")
    cmdline = (proc / "cmdline").read_bytes()
    args = cmdline.rstrip(b"\0").split(b"\0")[1:]
    if args[:1] == [b"-B"]:
        args = args[1:]
    if args != [b"-m", b"serve.thermal_guard", b"--config", os.fsencode(path)]:
        raise RuntimeError_("Independent guard command differs from its saved policy")
    return GuardIdentity(pid, (proc / "stat").read_text().rsplit(")", 1)[1].split()[19],
                         state["InvocationID"], hashlib.sha256(cmdline).hexdigest(), str(path),
                         digest(path), str(Path(config["status_file"]).expanduser().absolute()),
                         str(executable), state["ControlGroup"])


def guard_snapshot(pin: GuardIdentity, timeout):
    current = capture_guard(pin.config_path, timeout=timeout)
    if current != pin:
        raise RuntimeError_("Independent guard invocation, process or policy changed")
    return _owned_json(pin.status_path)


def guard_policies(pin: GuardIdentity):
    from thermal_policy import active_cooling_rule
    config = _owned_json(pin.config_path)
    if digest(Path(pin.config_path)) != pin.config_sha256:
        raise RuntimeError_("Independent guard policy changed")
    rules = {row["id"]: row for row in config["devices"]}
    rtx, v100 = rules["rtx5080"], rules["v100"]
    if (type(rtx.get("stop_c")) not in (float, int) or not 0 < rtx["stop_c"] <= 80
            or type(v100.get("stop_c")) not in (float, int) or not (0 < v100["stop_c"] <= 68 or active_cooling_rule(v100, "v100"))
            or v100.get("power_limit_w") != 165 or any(r.get("allow_simulated", False) is not False for r in (rtx, v100))):
        raise RuntimeError_("Saved guard does not preserve the established device policy")
    snap = _owned_json(pin.status_path)
    names = {role: snap["devices"][role]["reading_full"]["name"] for role in DEVICE_ROLES}
    if "RTX 5080" not in names["rtx5080"] or names["v100"] != "Tesla V100-PCIE-16GB":
        raise RuntimeError_("Guard sensors do not identify the expected physical cards")
    return (DevicePolicy("rtx5080", (("name", names["rtx5080"]),), rtx["stop_c"], False),
            DevicePolicy("v100", (("name", names["v100"]),), min(68, v100["stop_c"]), False,
                         min(67, v100["stop_c"]), 165))


@dataclass(frozen=True)
class DisplayIdentity:
    """The one desktop service known to appear in NVML's compute list."""
    pid: int
    start_ticks: str
    invocation: str
    executable: str
    cmdline_sha256: str
    control_group: str


def _display_unit(timeout):
    args = ["systemctl", "--user", "show", "gnome-remote-desktop.service"]
    for name in ("MainPID", "ActiveState", "InvocationID", "ControlGroup"):
        args += ["-p", name]
    result = subprocess.run(args, capture_output=True, text=True, timeout=timeout, check=True)
    return dict(line.split("=", 1) for line in result.stdout.splitlines() if "=" in line)


def capture_display(*, timeout=3.0):
    """Read-only pin of gnome-remote-desktop; no generic compute exemptions."""
    state = _display_unit(timeout)
    if state.get("ActiveState") != "active":
        return None
    pid = int(state.get("MainPID", "0"))
    group = state.get("ControlGroup", "")
    if (pid <= 1 or not re.fullmatch(r"[a-f0-9]{32}", state.get("InvocationID", ""))
            or not group.endswith("/app.slice/gnome-remote-desktop.service")):
        raise RuntimeError_("Desktop compute exemption lacks an exact unit identity")
    proc = Path("/proc") / str(pid)
    expected = Path(DISPLAY_EXECUTABLE)
    if proc.stat().st_uid != os.getuid() or (proc / "exe").resolve(strict=True) != expected.resolve(strict=True):
        raise RuntimeError_("Desktop compute exemption has a different owner or executable")
    cmdline = (proc / "cmdline").read_bytes()
    if cmdline != os.fsencode(DISPLAY_EXECUTABLE) + b"\0":
        raise RuntimeError_("Desktop compute exemption has an unexpected command line")
    groups = [line.split(":", 2)[2] for line in (proc / "cgroup").read_text().splitlines()
              if line.startswith("0::")]
    if groups != [group]:
        raise RuntimeError_("Desktop compute exemption is outside the pinned service cgroup")
    return DisplayIdentity(pid, (proc / "stat").read_text().rsplit(")", 1)[1].split()[19],
                           state["InvocationID"], str(expected.resolve(strict=True)),
                           hashlib.sha256(cmdline).hexdigest(), group)


def host_compute_allocations(rows, *, display_pin, timeout=3.0):
    """Exclude only a reverified desktop PID; retain all other compute owners.

    GPU memory totals are deliberately unchanged. The exempt process is
    external display occupancy, never attributed to the pooled model.
    """
    if not isinstance(rows, list):
        raise RuntimeError_("Host compute allocation list is unavailable")
    if display_pin is not None:
        if not isinstance(display_pin, DisplayIdentity) or capture_display(timeout=timeout) != display_pin:
            raise RuntimeError_("Pinned desktop service identity changed")
    for row in rows:
        if (not isinstance(row, dict) or not _integer(row.get("pid"), positive=True)
                or row["pid"] <= 1 or row.get("gpu_uuid") != RTX_UUID
                or not _integer(row.get("bytes")) or row["bytes"] >= 1 << 63):
            raise RuntimeError_("Host compute allocation has invalid physical identity or byte accounting")
    return [row for row in rows if display_pin is None or row["pid"] != display_pin.pid]


def _source_fresh(snapshot, *, now=None, max_age=5.0):
    """Require the sampler's original V100 time, not the guard's poll time."""
    now = time.time() if now is None else now
    reading = snapshot.get("devices", {}).get("v100", {}).get("reading_full")
    source = reading.get("ts") if isinstance(reading, dict) else None
    if type(source) not in (int, float) or not math.isfinite(source) or not 0 <= now - source < max_age:
        raise RuntimeError_("V100 original sensor timestamp is missing, stale, invalid or from the future")


def system_hooks(pin: GuardIdentity, status_path, *, min_host_available_mib=8192):
    """Concrete guarded callbacks for the current Dulche service, all read-only
    except the caller-selected private runtime status owned by EngineManager.
    Host allocations and memory counters use one matching NVML observation.
    """
    from engine_profiles import atomic_json
    if type(min_host_available_mib) is not int or min_host_available_mib < 8192:
        raise ValueError("Pooled host RAM floor must preserve at least 8192 MiB")
    sample = threading.local()
    display = capture_display()

    def read(timeout):
        started = time.monotonic()
        snap = guard_snapshot(pin, timeout)
        _source_fresh(snap)
        remaining = timeout - (time.monotonic() - started)
        if remaining <= 0:
            raise TimeoutError("Guard observation used the callback deadline")
        mem = {line.split()[0].rstrip(":"): int(line.split()[1])
               for line in Path("/proc/meminfo").read_text().splitlines()}
        if mem["MemAvailable"] < min_host_available_mib * 1024:
            raise RuntimeError_("Available host RAM fell below the pooled worker floor")
        sample.value = nvml_snapshot(RTX_UUID, remaining)
        sample.time = time.monotonic()
        snap["devices"]["rtx5080"]["reading_full"].update(sample.value["memory"])
        _source_fresh(snap)
        return snap

    def allocations(timeout):
        started = time.monotonic()
        if getattr(sample, "time", 0) + 2 > time.monotonic():
            rows = sample.value["allocations"]
        else:
            rows = nvml_snapshot(RTX_UUID, timeout)["allocations"]
        remaining = timeout - (time.monotonic() - started)
        if remaining <= 0:
            raise TimeoutError("Host allocation observation used the callback deadline")
        return host_compute_allocations(rows, display_pin=display, timeout=remaining)

    return RuntimeHooks(read, allocations, lambda value: atomic_json(Path(status_path), value))


@dataclass(frozen=True)
class RpcProcess:
    """Acquired through v100_recover.ssh (strict host-key checking)."""
    boot_id: str
    pid: int
    start_ticks: str
    executable: str
    executable_sha256: str
    cmdline_sha256: str
    gpu_uuid: str
    port: int = 5555

    def validate(self):
        if (not re.fullmatch(r"[a-f0-9-]{36}", self.boot_id)
                or not _integer(self.pid, positive=True) or self.pid <= 1
                or not isinstance(self.start_ticks, str) or not self.start_ticks.isdigit()
                or not Path(self.executable).is_absolute()
                or Path(self.executable).name not in ("rpc-server", "ggml-rpc-server")
                or not re.fullmatch(r"[a-f0-9]{64}", self.executable_sha256)
                or not re.fullmatch(r"[a-f0-9]{64}", self.cmdline_sha256)
                or not re.fullmatch(r"GPU-[a-fA-F0-9]{8}(?:-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}", self.gpu_uuid)
                or self.gpu_uuid.lower() == RTX_UUID.lower() or self.port != 5555):
            raise ValueError("Invalid pinned V100 RPC identity")

    @property
    def key(self):
        self.validate()
        return hashlib.sha256(json.dumps(asdict(self), sort_keys=True).encode()).hexdigest()


def capture_rpc(expected_uuid, *, ssh=None, timeout=8.0):
    """Read the sole owned rpc-server actually listening on the guest's 5555.

    The caller first restores the existing VM/RPC through v100_recover. No
    process is started or stopped here. Host key, identity file and loopback
    port remain exclusively owned by that established transport helper.
    """
    if ssh is None:
        from v100_recover import ssh
    source = _NVML_SOURCE + r'''
import hashlib, os, pathlib
expected_uuid = EXPECTED_UUID_LITERAL
reading = nvml_snapshot(expected_uuid)
if reading["name"] != "Tesla V100-PCIE-16GB":
    raise RuntimeError("Guest UUID is not the expected V100")
inodes = {"socket:[" + row[9] + "]" for line in pathlib.Path("/proc/net/tcp").read_text().splitlines()[1:]
          if len(row := line.split()) > 9 and row[1].endswith(":15B3") and row[3] == "0A"}
matches = []
for proc in pathlib.Path("/proc").glob("[0-9]*"):
    try:
        if proc.stat().st_uid != os.getuid():
            continue
        executable = (proc / "exe").resolve(strict=True)
        if executable.name not in ("rpc-server", "ggml-rpc-server"):
            continue
        sockets = set()
        for link in (proc / "fd").iterdir():
            try:
                sockets.add(os.readlink(link))
            except FileNotFoundError:
                pass
        if not sockets.intersection(inodes):
            continue
        with executable.open("rb") as stream:
            executable_hash = hashlib.file_digest(stream, "sha256").hexdigest()
        matches.append({"boot_id": pathlib.Path("/proc/sys/kernel/random/boot_id").read_text().strip(),
                        "pid": int(proc.name), "start_ticks": (proc / "stat").read_text().rsplit(")", 1)[1].split()[19],
                        "executable": str(executable), "executable_sha256": executable_hash,
                        "cmdline_sha256": hashlib.sha256((proc / "cmdline").read_bytes()).hexdigest(),
                        "gpu_uuid": expected_uuid, "port": 5555})
    except (FileNotFoundError, PermissionError):
        continue
if len(matches) != 1:
    raise RuntimeError("Expected one owned V100 RPC listener, observed " + str(len(matches)))
print(json.dumps(matches[0]), flush=True)
'''
    source = source.replace("EXPECTED_UUID_LITERAL", repr(expected_uuid))
    result = ssh("python3 -", input=source, timeout=timeout)
    if len(result.stdout) > 16384:
        raise RuntimeError_("Guest identity observation exceeded its bound")
    pin = RpcProcess(**json.loads(result.stdout))
    pin.validate()
    if pin.gpu_uuid != expected_uuid:
        raise RuntimeError_("Captured guest RPC UUID differs")
    GuestRPC(pin, ssh=ssh).observe(timeout)
    return pin


# Code sent through the already owned strict-host-key SSH transport. Request
# data is embedded using repr(JSON), never interpolated into a shell command.
# pidfd pins the process BEFORE identity verification and all later signals.
_GUEST_SCRIPT = r'''
import hashlib, json, os, pathlib, select, signal, subprocess, time
request = json.loads(REQUEST_LITERAL)
pin = request["pin"]
def sha(path):
    with open(path, "rb") as f:
        return hashlib.file_digest(f, "sha256").hexdigest()
def emit(value):
    print(json.dumps(value), flush=True)
    raise SystemExit(0)
def gpu_sample():
    result = nvml_snapshot(pin["gpu_uuid"])
    if result["name"] != "Tesla V100-PCIE-16GB":
        raise RuntimeError("Guest physical GPU identity changed")
    return result
def finished():
    # Process exit alone does not prove the driver released its allocations.
    pending = any(row["pid"] == pin["pid"] for row in gpu_sample()["allocations"])
    emit({"alive": False, "stopped": not pending, "pending_allocations": pending})
if pathlib.Path("/proc/sys/kernel/random/boot_id").read_text().strip() != pin["boot_id"]:
    raise RuntimeError("Guest boot identity changed")
proc = pathlib.Path("/proc") / str(pin["pid"])
try:
    fd = os.pidfd_open(pin["pid"])
except ProcessLookupError:
    finished()
poll = select.poll()
poll.register(fd, select.POLLIN)
def gone():
    return bool(poll.poll(0))
if gone():
    finished()
try:
    start = (proc / "stat").read_text().rsplit(")", 1)[1].split()[19]
    executable = str((proc / "exe").resolve(strict=True))
    command_hash = hashlib.sha256((proc / "cmdline").read_bytes()).hexdigest()
except FileNotFoundError:
    if gone():
        finished()
    raise
if (proc.stat().st_uid != os.getuid() or start != pin["start_ticks"]
        or executable != pin["executable"] or command_hash != pin["cmdline_sha256"]
        or sha(executable) != pin["executable_sha256"]):
    raise RuntimeError("Guest RPC identity changed; refusing action")
if request["action"] == "stop":
    for sig, wait in ((signal.SIGTERM, 2.0), (signal.SIGKILL, 1.0)):
        if gone():
            break
        signal.pidfd_send_signal(fd, sig)
        poll.poll(int(wait * 1000))
    if not gone():
        raise RuntimeError("Pinned guest RPC process did not exit")
    finished()
inodes = set()
for line in pathlib.Path("/proc/net/tcp").read_text().splitlines()[1:]:
    row = line.split()
    if row[1].endswith(":" + format(pin["port"], "04X")) and row[3] == "0A":
        inodes.add("socket:[" + row[9] + "]")
sockets = set()
for link in (proc / "fd").iterdir():
    try:
        sockets.add(os.readlink(link))
    except FileNotFoundError:
        pass
if not sockets.intersection(inodes):
    raise RuntimeError("Pinned guest RPC no longer owns the listener")
reading = gpu_sample()
if gone():
    finished()
emit({"alive": True, "stopped": False, "gpu_uuid": pin["gpu_uuid"], "name": "Tesla V100-PCIE-16GB",
      "allocations": reading["allocations"], "memory": reading["memory"]})
'''


class GuestRPC:
    def __init__(self, pin: RpcProcess, *, ssh=None):
        pin.validate()
        self.pin = pin
        if ssh is None:
            from v100_recover import ssh
        self.ssh = ssh

    def request(self, action, timeout):
        if action not in ("observe", "stop") or timeout <= 0:
            raise ValueError("Invalid pinned guest operation")
        payload = json.dumps({"pin": asdict(self.pin), "action": action})
        source = _NVML_SOURCE + _GUEST_SCRIPT.replace("REQUEST_LITERAL", repr(payload))
        result = self.ssh("python3 -", input=source, timeout=timeout)
        value = json.loads(result.stdout)
        if not isinstance(value, dict) or type(value.get("alive")) is not bool:
            raise RuntimeError_("Guest RPC returned an invalid identity observation")
        return value

    def observe(self, timeout):
        value = self.request("observe", timeout)
        if value.get("alive") is not True:
            raise RuntimeError_("Pinned guest RPC is unavailable")
        if value.get("gpu_uuid") != self.pin.gpu_uuid:
            raise RuntimeError_("Guest observation belongs to another GPU")
        return value

    def stop(self, timeout):
        if self.request("stop", timeout).get("stopped") is not True:
            raise RuntimeError_("Pinned guest RPC stop is unverified")

    def stopped(self, timeout):
        return self.request("observe", timeout).get("stopped") is True


def _rpc_recipe(recipe):
    """Validate only the bounded guest launch fields; no remote filesystem IO."""
    if (not isinstance(recipe, dict) or recipe.get("alias") != POOL_MODEL_ID
            or recipe.get("physical_devices") != list(DEVICE_ROLES)
            or not re.fullmatch(r"[a-f0-9]{32}", str(recipe.get("launch_id", "")))
            or not re.fullmatch(r"[a-f0-9]{64}", str(recipe.get("rpc_sha256", "")))
            or not re.fullmatch(r"GPU-[a-fA-F0-9]{8}(?:-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}", str(recipe.get("v100_uuid", "")))
            or recipe["v100_uuid"].lower() == RTX_UUID.lower()):
        raise RuntimeError_("Pooled RPC recipe has an invalid physical or launch identity")
    path = recipe.get("rpc_path")
    if (not isinstance(path, str) or not Path(path).is_absolute()
            or ".." in Path(path).parts or Path(path).name != "ggml-rpc-server"):
        raise RuntimeError_("Pooled RPC requires its absolute frozen ggml-rpc-server path")
    dirs = recipe.get("rpc_library_dirs")
    if (not isinstance(dirs, list) or not dirs
            or any(not isinstance(p, str) or not Path(p).is_absolute() or ".." in Path(p).parts for p in dirs)):
        raise RuntimeError_("Pooled RPC requires explicit absolute library directories")
    return {key: recipe[key] for key in ("launch_id", "rpc_path", "rpc_sha256", "rpc_library_dirs", "v100_uuid")}


def _arm_rpc_startup(recipe, runtime_dir):
    fields = _rpc_recipe(recipe)
    record = {"schema": RPC_START_SCHEMA, **fields, "created_at": time.time(),
              "host_boot_id": Path("/proc/sys/kernel/random/boot_id").read_text().strip(),
              "owner_pid": os.getpid(),
              "owner_start_ticks": Path("/proc/self/stat").read_text().rsplit(")", 1)[1].split()[19]}
    raw = json.dumps(record, sort_keys=True).encode()
    armed = {}
    with claim_devices(runtime_dir=runtime_dir):
        try:
            for role in DEVICE_ROLES:
                path = _quarantine_path(_runtime_directory(runtime_dir), role)
                armed[path] = _write_fence(path, raw)
        except BaseException:
            _retire_residency(armed)  # No RPC request has been issued yet.
            raise
    return armed


def _startup_fences(recipe, runtime_dir):
    """Only this launch's startup markers may be retired by RPC-only cleanup."""
    expected = _rpc_recipe(recipe)
    found = {}
    for role in DEVICE_ROLES:
        path = _quarantine_path(_runtime_directory(runtime_dir), role)
        if not os.path.lexists(path):
            continue
        value = read_private_json(path)
        if value.get("schema") != RPC_START_SCHEMA:
            raise RuntimeError_("RPC ownership transferred to inference residency; full release proof is required")
        if any(value.get(key) != val for key, val in expected.items()):
            raise RuntimeError_("RPC startup fence belongs to a different launch")
        found[path] = digest(path)
    return found


# The saved guest receipt and the child's exact environment launch tag cover
# an SSH timeout or manager crash between Popen and returning the process pin.
# A new launch never adopts the old legacy worker or another launch's process.
_RPC_LAUNCH_SCRIPT = r'''
import fcntl, hashlib, json, os, pathlib, select, signal, stat, subprocess, time
request = json.loads(REQUEST_LITERAL)
cfg = request["recipe"]
base = pathlib.Path.home() / "strata" / "bridge"
info = base.lstat()
if not stat.S_ISDIR(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o022:
    raise RuntimeError("Guest RPC receipt directory is not owned and protected")
receipt = base / "pooled-rpc-owner.json"
lock = os.open(base / "pooled-rpc-owner.lock", os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
info = os.fstat(lock)
if not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o077:
    raise RuntimeError("Guest RPC receipt lock is not private")
fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
def sha(path):
    with open(path, "rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()
def read_receipt():
    if not os.path.lexists(receipt):
        return None
    fd = os.open(receipt, os.O_RDONLY | os.O_NOFOLLOW)
    with os.fdopen(fd) as stream:
        info = os.fstat(stream.fileno())
        if not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o077 or info.st_size > 16384:
            raise RuntimeError("Guest RPC receipt is not private and bounded")
        return json.load(stream)
def save(value):
    temporary = receipt.with_name(receipt.name + "." + cfg["launch_id"] + ".tmp")
    fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, "w") as stream:
        json.dump(value, stream, sort_keys=True)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, receipt)
    fd = os.open(base, os.O_RDONLY | os.O_DIRECTORY)
    try: os.fsync(fd)
    finally: os.close(fd)
def emit(value):
    print(json.dumps(value), flush=True)
    raise SystemExit(0)
exe = pathlib.Path(cfg["rpc_path"]).resolve(strict=True)
if str(exe) != cfg["rpc_path"] or exe.name != "ggml-rpc-server" or sha(exe) != cfg["rpc_sha256"]:
    raise RuntimeError("Guest RPC executable differs from the frozen paired build")
argv = [str(exe), "--host", "0.0.0.0", "--port", "5555", "--device", "CUDA0"]
command = b"\0".join(os.fsencode(a) for a in argv) + b"\0"
tag = ("DULCHE_POOL_LAUNCH_ID=" + cfg["launch_id"]).encode()
boot = pathlib.Path("/proc/sys/kernel/random/boot_id").read_text().strip()
def pin_process(pid):
    proc = pathlib.Path("/proc") / str(pid)
    if (proc.stat().st_uid != os.getuid() or str((proc / "exe").resolve(strict=True)) != str(exe)
            or (proc / "cmdline").read_bytes() != command or tag not in (proc / "environ").read_bytes().split(b"\0")):
        raise RuntimeError("Guest process is not this launch's exact RPC child")
    return {"boot_id": boot, "pid": pid, "start_ticks": (proc / "stat").read_text().rsplit(")", 1)[1].split()[19],
            "executable": str(exe), "executable_sha256": cfg["rpc_sha256"],
            "cmdline_sha256": hashlib.sha256(command).hexdigest(), "gpu_uuid": cfg["v100_uuid"], "port": 5555}
def tagged():
    pins = []
    for proc in pathlib.Path("/proc").glob("[0-9]*"):
        try:
            if proc.stat().st_uid == os.getuid() and tag in (proc / "environ").read_bytes().split(b"\0"):
                pins.append(pin_process(int(proc.name)))
        except (FileNotFoundError, PermissionError):
            pass
    if len(pins) > 1:
        raise RuntimeError("More than one child has this exact pooled launch tag")
    return pins
def gpu():
    value = nvml_snapshot(cfg["v100_uuid"])
    if value["name"] != "Tesla V100-PCIE-16GB":
        raise RuntimeError("Guest physical V100 identity changed")
    return value
prior = read_receipt()
if request["action"] == "recover":
    if prior and prior.get("launch_id") != cfg["launch_id"]:
        raise RuntimeError("Guest RPC receipt belongs to another launch")
    if prior and prior.get("pin"):
        emit({"pin": prior["pin"]})
    pins = tagged()
    if pins:
        save({"schema": "dulche-pool-guest-rpc/v1", "launch_id": cfg["launch_id"], "phase": "owned", "pin": pins[0]})
        emit({"pin": pins[0]})
    if gpu()["allocations"]:
        raise RuntimeError("No pinned RPC child but GPU allocations remain")
    emit({"stopped": True})
if request["action"] != "start":
    raise RuntimeError("Unsupported bounded RPC action")
if tagged() or gpu()["allocations"]:
    raise RuntimeError("Guest compute must be idle before pooled RPC startup")
if prior and prior.get("pin"):
    old = prior["pin"]
    proc = pathlib.Path("/proc") / str(old["pid"])
    if old.get("boot_id") == boot and proc.exists():
        try:
            current_start = (proc / "stat").read_text().rsplit(")", 1)[1].split()[19]
        except FileNotFoundError:
            current_start = None
        if current_start == old.get("start_ticks"):
            try:
                exact = (proc.stat().st_uid == os.getuid()
                         and str((proc / "exe").resolve(strict=True)) == old.get("executable")
                         and hashlib.sha256((proc / "cmdline").read_bytes()).hexdigest() == old.get("cmdline_sha256"))
            except FileNotFoundError:
                exact = False
            raise RuntimeError("An earlier exact RPC child is still live" if exact
                               else "An earlier RPC process changed identity; reconciliation is required")
if any(row[1].endswith(":15B3") and row[3] == "0A"
       for line in pathlib.Path("/proc/net/tcp").read_text().splitlines()[1:] if len(row := line.split()) > 3):
    raise RuntimeError("Guest RPC listener is already occupied")
for directory in cfg["rpc_library_dirs"]:
    if not pathlib.Path(directory).is_dir():
        raise RuntimeError("Guest RPC library directory is unavailable")
save({"schema": "dulche-pool-guest-rpc/v1", "launch_id": cfg["launch_id"], "phase": "starting", "boot_id": boot})
env = dict(os.environ)
for key in list(env):
    if key.startswith(("LLAMA_ARG_", "GGML_CUDA_", "GGML_RPC_")) or key in ("LD_PRELOAD", "GGML_BACKEND_PATH", "GGML_OP_OFFLOAD_MIN_BATCH"):
        del env[key]
env.update(CUDA_VISIBLE_DEVICES=cfg["v100_uuid"], LD_LIBRARY_PATH=os.pathsep.join(cfg["rpc_library_dirs"]),
           DULCHE_POOL_LAUNCH_ID=cfg["launch_id"])
logfd = os.open(base / ("pooled-rpc-" + cfg["launch_id"] + ".log"), os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
child = None
try:
    with os.fdopen(logfd, "w") as output:
        child = subprocess.Popen(argv, stdin=subprocess.DEVNULL, stdout=output, stderr=output,
                                 env=env, start_new_session=True, close_fds=True)
    pin = pin_process(child.pid)
    save({"schema": "dulche-pool-guest-rpc/v1", "launch_id": cfg["launch_id"], "phase": "owned", "pin": pin})
    deadline = time.monotonic() + 7
    while time.monotonic() < deadline:
        if child.poll() is not None:
            raise RuntimeError("Paired RPC exited before its listener was ready")
        inodes = {"socket:[" + row[9] + "]" for line in pathlib.Path("/proc/net/tcp").read_text().splitlines()[1:]
                  if len(row := line.split()) > 9 and row[1].endswith(":15B3") and row[3] == "0A"}
        sockets = set()
        for link in (pathlib.Path("/proc") / str(child.pid) / "fd").iterdir():
            try: sockets.add(os.readlink(link))
            except FileNotFoundError: pass
        if sockets.intersection(inodes):
            if any(row["pid"] != child.pid for row in gpu()["allocations"]):
                raise RuntimeError("A foreign guest compute process appeared during startup")
            emit({"pin": pin})
        time.sleep(0.05)
    raise RuntimeError("Paired RPC listener startup exceeded its deadline")
except Exception:
    if child is not None:
        if child.poll() is None:
            child.terminate()
            try: child.wait(timeout=2)
            except subprocess.TimeoutExpired:
                child.kill()
                child.wait(timeout=1)
        released = not any(row["pid"] == child.pid for row in gpu()["allocations"])
        if released:
            save({"schema": "dulche-pool-guest-rpc/v1", "launch_id": cfg["launch_id"], "phase": "stopped"})
    raise
'''


def _rpc_launch_request(recipe, action, *, timeout, ssh=None):
    if ssh is None:
        from v100_recover import ssh
    payload = json.dumps({"recipe": _rpc_recipe(recipe), "action": action})
    source = _NVML_SOURCE + _RPC_LAUNCH_SCRIPT.replace("REQUEST_LITERAL", repr(payload))
    result = ssh("python3 -", input=source, timeout=timeout)
    if len(result.stdout) > 16384:
        raise RuntimeError_("RPC launch receipt exceeded its bound")
    value = json.loads(result.stdout)
    if not isinstance(value, dict):
        raise RuntimeError_("RPC launch receipt is unavailable")
    return value


def _recipe_pin(recipe, value):
    pin = RpcProcess(**value)
    pin.validate()
    if (pin.gpu_uuid != recipe["v100_uuid"] or pin.executable != recipe["rpc_path"]
            or pin.executable_sha256 != recipe["rpc_sha256"]):
        raise RuntimeError_("RPC ownership receipt differs from the exact paired launch")
    return pin


def stop_pooled_rpc(pin, *, recipe=None, timeout=8.0, ssh=None, runtime_dir=None):
    """Stop one immutable pin; optionally retire only its verified startup fences."""
    if recipe is not None:
        _recipe_pin(recipe, asdict(pin))
    guest = GuestRPC(pin, ssh=ssh)
    started = time.monotonic()
    guest.stop(timeout)
    remaining = timeout - (time.monotonic() - started)
    if remaining <= 0 or guest.stopped(remaining) is not True:
        raise RuntimeError_("Paired RPC release is not verified")
    if recipe is not None:
        _retire_residency(_startup_fences(recipe, runtime_dir))
    return True


def recover_pooled_rpc(recipe, *, timeout=5.0, ssh=None):
    """Guard recovery of a pending pin; never clear any host residency fence.

    None requires explicit guest proof of no tagged process and no NVML
    compute allocations. A returned historical pin still needs stopped() or
    stop_pooled_rpc(): a receipt alone is never evidence of release.
    """
    value = _rpc_launch_request(recipe, "recover", timeout=timeout, ssh=ssh)
    if "pin" in value:
        return _recipe_pin(recipe, value["pin"])
    if value.get("stopped") is True:
        return None
    raise RuntimeError_("Pending RPC observation lacks an exact ownership or stopped proof")


def cleanup_pending_rpc(recipe, *, timeout=8.0, ssh=None, runtime_dir=None):
    """Reconcile a timed-out startup; never erase an inference residency fence."""
    armed = _startup_fences(recipe, runtime_dir)
    started = time.monotonic()
    receipt = _rpc_launch_request(recipe, "recover", timeout=timeout, ssh=ssh)
    remaining = timeout - (time.monotonic() - started)
    if "pin" in receipt:
        if remaining <= 0:
            raise RuntimeError_("RPC cleanup exhausted its observation deadline")
        stop_pooled_rpc(_recipe_pin(recipe, receipt["pin"]), timeout=remaining, ssh=ssh)
    elif receipt.get("stopped") is not True:
        raise RuntimeError_("Pending RPC release lacks an exact stopped proof")
    _retire_residency(armed)
    return True


def start_pooled_rpc(recipe, *, timeout=12.0, ssh=None, runtime_dir=None):
    """Launch the separate paired RPC under a durable two-device startup claim.

    EngineManager retains resource.lock and saves rpc_pending before calling.
    The returned pin must be saved as recipe.rpc_process before the wrapper
    begins. On a failed or timed-out start, cleanup has a separate bounded
    eight-second allowance; failed proof leaves both admission fences intact.
    """
    if type(timeout) not in (int, float) or not math.isfinite(timeout) or timeout <= 0:
        raise ValueError("RPC startup timeout must be finite and positive")
    _arm_rpc_startup(recipe, runtime_dir)
    try:
        reply = _rpc_launch_request(recipe, "start", timeout=timeout, ssh=ssh)
        pin = _recipe_pin(recipe, reply["pin"])
        GuestRPC(pin, ssh=ssh).observe(min(5, timeout))
        return pin
    except BaseException:
        try:
            cleanup_pending_rpc(recipe, timeout=8, ssh=ssh, runtime_dir=runtime_dir)
        except BaseException:
            pass  # Keep the original failure and durable unreconciled fences.
        raise


def allocation_bound(rows, allowed_pids, *, uuid=None):
    """Exact NVML bytes, or conservative bounds for explicitly MiB observations.

    Other compute processes cause refusal. Graphics/display allocations remain
    in guard mem_used and are accounted once by budget_pool's external_bytes.
    """
    total = 0
    for row in rows:
        pid = row.get("pid")
        byte_exact = "bytes" in row
        used = row.get("bytes") if byte_exact else row.get("mib")
        if not _integer(pid, positive=True) or not _integer(used) or used >= 1 << 63:
            raise RuntimeError_("GPU allocation observation is incomplete")
        if uuid is not None and row.get("gpu_uuid") != uuid:
            raise RuntimeError_("Allocation belongs to a different physical GPU")
        if pid not in allowed_pids:
            raise RuntimeError_("A competing compute process acquired a pooled device")
        total += used if byte_exact else (used + 1) * MIB
    return total


def _guest_memory(snapshot, remote):
    # Guard readings continue to own temperature/power/freshness. Pair current
    # memory counters with the current per-process NVML observation when the
    # concrete guest adapter provides it. Legacy injected tests use guard bytes.
    if "memory" in remote:
        memory = remote["memory"]
        if (not isinstance(memory, dict) or not _integer(memory.get("mem_total"), positive=True)
                or not _integer(memory.get("mem_used")) or memory["mem_used"] > memory["mem_total"]):
            raise RuntimeError_("Guest NVML memory counters are incomplete")
        snapshot["devices"]["v100"]["reading_full"].update(memory)


def health_budget(snapshot, policies, pool_policy, owned, *, now=None, now_ns=None):
    """Validate each sensor timestamp before using exact NVML totals/usage."""
    now = time.time() if now is None else now
    now_ns = time.monotonic_ns() if now_ns is None else now_ns
    _source_fresh(snapshot, now=now, max_age=pool_policy.max_age_ns / 1e9)
    verdict = evaluate(snapshot, policies, now=now, max_age=pool_policy.max_age_ns / 1e9)
    if not verdict.ok:
        raise RuntimeError_("Pooled device guard closed admission: " + ", ".join(verdict.reasons))
    values = []
    for role, uuid, endpoint in pool_policy.identities():
        reading = snapshot["devices"][role]["reading_full"]
        used, total = reading.get("mem_used"), reading.get("mem_total")
        if not _integer(used) or not _integer(total, positive=True) or used > total:
            raise RuntimeError_("Exact GPU memory telemetry is unavailable")
        # The conservative MiB bound cannot exceed all memory used on the GPU.
        upper = min(owned[role], used)
        values.append(Snapshot(uuid, endpoint, now_ns, total, used, upper, True, True))
    budget = budget_pool(pool_policy, values, now_ns=now_ns)
    if budget.current_deficit_bytes:
        raise RuntimeError_("Observed pooled allocation exceeds its exact display-reserve policy")
    return budget


@dataclass(frozen=True)
class RuntimeHooks:
    """OS observation adapters; timeouts belong to every underlying operation.

    read_snapshot verifies the SAME guard invocation and immutable policy;
    host_allocations queries the pinned RTX UUID; publish writes a private
    atomic status record owned by EngineManager. None may launch a model.
    """
    read_snapshot: Callable[[float], dict]
    host_allocations: Callable[[float], list]
    publish: Callable[[dict], None]


def run_owned(plan: CommandPlan, *, guest: GuestRPC, policies: tuple[DevicePolicy, DevicePolicy],
              hooks: RuntimeHooks, cancelled: threading.Event, max_age=5.0,
              runtime_dir=None, run_timeout=24 * 3600, popen=subprocess.Popen,
              launch_id=None, startup_id=None):
    """Launch one endpoint under both leases, then stop BOTH owners on any exit.

    EngineManager remains the API/router/lease owner. This entry point does not
    provide a standalone HTTP service or bypass broker request admission.
    Caller installs its signal handlers before invoking it and starts the
    worker under KillMode=control-group, TimeoutStopSec>=20s and BindsTo=guard.
    """
    if any(type(value) not in (int, float) or not math.isfinite(value) or value <= 0
           for value in (max_age, run_timeout)):
        raise ValueError("Pooled supervision durations must be finite and positive")
    launch_id = secrets.token_hex(16) if launch_id is None else launch_id
    if not isinstance(launch_id, str) or not re.fullmatch(r"[0-9a-f]{32}", launch_id):
        raise ValueError("Invalid pooled launch identity")
    if not isinstance(plan, CommandPlan) or plan.guest_identity.owned_rpc_server_identity != guest.pin.key:
        raise RuntimeError_("Command plan does not belong to the pinned guest RPC")
    if plan.guest_identity.gpu_uuid != guest.pin.gpu_uuid or plan.guest_identity.endpoint != RPC_ENDPOINT:
        raise RuntimeError_("Command plan has a different physical V100 endpoint")
    if plan.device_order != ("CUDA0", plan.guest_identity.rpc_device_name):
        raise RuntimeError_("Command plan device order changed")
    if digest(Path(plan.argv[0])) != plan.executable_sha256:
        raise RuntimeError_("Pooled backend executable changed after inspection")
    policy = PoolPolicy("CUDA:" + RTX_UUID, guest.pin.gpu_uuid, RPC_ENDPOINT, int(max_age * 1e9))
    child = None
    if startup_id is not None and startup_id != launch_id:
        raise RuntimeError_("RPC startup identity differs from the pooled launch")
    with claim_devices(runtime_dir=runtime_dir, startup_id=startup_id):
        # The guest was deliberately restored beforehand; do not kill it if a
        # cold admission fails before this wrapper launches its own child.
        snapshot = hooks.read_snapshot(3)
        remote = guest.observe(5)
        _guest_memory(snapshot, remote)
        owned = {
            "rtx5080": allocation_bound(hooks.host_allocations(3), set(), uuid=RTX_UUID),
            "v100": allocation_bound(remote["allocations"], {guest.pin.pid}),
        }
        before = health_budget(snapshot, policies, policy, owned)
        targets = tuple(d.unconsumed_reserve_bytes for d in before.devices)
        if targets != plan.exact_free_targets_bytes:
            plan = refresh_fitting_targets(plan, free_targets_bytes=targets,
                                           available_free_bytes=tuple(d.total_bytes - d.used_bytes for d in before.devices),
                                           now_ns=time.monotonic_ns())
        if cancelled.is_set():
            raise RuntimeError_("Pooled launch was cancelled before allocation")
        armed = _arm_residency(runtime_dir, guest=guest, plan=plan, startup_id=startup_id)
        try:
            child = popen(list(plan.argv), stdin=subprocess.DEVNULL,
                          env=plan.materialize_environment(), start_new_session=False)
        except BaseException:
            if startup_id is None:
                _retire_residency(armed)  # No child exists; the prior idle RPC is untouched.
            else:
                try:
                    guest.stop(8)
                    if guest.stopped(3) is True:
                        _retire_residency(armed)
                except BaseException:
                    pass  # This launch's idle RPC may remain; preserve both fences.
            raise
        last = {"schema": "dulche-pool-status/v1", "launch_id": launch_id,
                "wrapper_pid": os.getpid(),
                "wrapper_start_ticks": Path("/proc/self/stat").read_text().rsplit(")", 1)[1].split()[19],
                "systemd_invocation": os.environ.get("INVOCATION_ID", ""),
                "phase": "loading", "status": "loading", "backend_pid": child.pid, "pid": child.pid,
                "physical_devices": list(before.physical_devices),
                "display_reserved_bytes": before.display_reserved_bytes, "hard_reservation_supported": False,
                "dual_allocations_observed": False, "rpc_identity": guest.pin.key}
        closing = threading.Event()
        status_lock = threading.Lock()

        def snapshot_and_budget(timeout):
            deadline = time.monotonic() + timeout

            def remaining():
                value = deadline - time.monotonic()
                if value <= 0 or closing.is_set():
                    raise TimeoutError("Pooled observation deadline expired or lifetime is closing")
                return value

            snap = hooks.read_snapshot(remaining())
            remote = guest.observe(remaining())
            _guest_memory(snap, remote)
            own = {
                "rtx5080": allocation_bound(hooks.host_allocations(remaining()), {child.pid}, uuid=RTX_UUID),
                "v100": allocation_bound(remote["allocations"], {guest.pin.pid}),
            }
            measured = health_budget(snap, policies, policy, own)
            remaining()
            # Allocations do not prove correlated GPU compute or output quality.
            with status_lock:
                if closing.is_set():
                    raise TimeoutError("Pooled lifetime is closing; late status discarded")
                last.update(phase="running", status="running", budget=measured.to_dict(),
                            dual_allocations_observed=all(d.owned_bytes > baseline.owned_bytes
                                                         for d, baseline in zip(measured.devices, before.devices)),
                            observed_at=time.time())
                hooks.publish(dict(last))
            return snap

        def stop_host(timeout):
            closing.set()
            if child.poll() is None:
                child.terminate()
                try:
                    child.wait(timeout=max(0.01, timeout / 2))
                except subprocess.TimeoutExpired:
                    child.kill()
                    child.wait(timeout=max(0.01, timeout / 2))

        def stop_guest(timeout):
            closing.set()
            guest.stop(timeout)

        def host_stopped(timeout):
            if child.poll() is None:
                return False
            return not any(r.get("pid") == child.pid for r in hooks.host_allocations(timeout))

        # supervise guarantees two independent bounded stops, even when one
        # raises or hangs. It retains the immutable guest identity in callbacks.
        outcome = supervise(
            policies=policies, max_age=max_age, read_snapshot=snapshot_and_budget,
            process_alive=lambda _: child.poll() is None,
            stop={"rtx5080": stop_host, "v100": stop_guest},
            verify_stopped={"rtx5080": host_stopped, "v100": guest.stopped},
            cancelled=cancelled, run_timeout=run_timeout, interval=0.5,
            callback_timeout=6, cleanup_timeout=16,
        )
        if outcome.stop_verified:
            _retire_residency(armed)
        with status_lock:
            last.update(phase="stopped" if outcome.stop_verified else "stop_unverified",
                        status="stopped" if outcome.stop_verified else "stop_unverified",
                        trigger=outcome.trigger, stop_verified=outcome.stop_verified,
                        errors={key: type(value).__name__ for key, value in outcome.failures.items()},
                        exit_code=child.poll(), observed_at=time.time())
            hooks.publish(dict(last))
        return outcome


def _artifact(path, identity):
    """Verify content, reusing only a caller-owned exact stat/hash receipt."""
    from pooled_backend import _gguf_candidate
    file = Path(path).resolve(strict=True)
    _gguf_candidate(file)
    if not isinstance(identity, dict) or not re.fullmatch(r"[a-f0-9]{64}", str(identity.get("sha256", ""))):
        raise RuntimeError_("A verified model SHA256 identity is required")
    before = file.stat()
    actual = {"dev": before.st_dev, "ino": before.st_ino, "size": before.st_size, "mtime_ns": before.st_mtime_ns}
    if not _integer(identity.get("bytes"), positive=True) or before.st_size != identity["bytes"]:
        raise RuntimeError_("Model byte size differs from its verified identity")
    if identity.get("stat") != actual and digest(file) != identity["sha256"]:
        raise RuntimeError_("Model content differs from its verified SHA256")
    after = file.stat()
    if (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns) != (
            after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns):
        raise RuntimeError_("Model changed while its identity was checked")
    return file


def runtime_recipe(path):
    """Validate the private EngineManager recipe; never accept arbitrary argv."""
    value = read_private_json(path)
    if (value.get("schema") != "dulche-pool-runtime/v1"
            or value.get("physical_devices") != list(DEVICE_ROLES)
            or value.get("alias") != POOL_MODEL_ID
            or not re.fullmatch(r"[a-f0-9]{32}", str(value.get("launch_id", "")))):
        raise RuntimeError_("Pooled recipe identity or physical membership differs")
    for name, low, high in (("context", 512, 1048576), ("threads", 1, 16), ("port", 1024, 65535)):
        if type(value.get(name)) is not int or not low <= value[name] <= high:
            raise RuntimeError_("Pooled recipe has an invalid " + name)
    from pooled_backend import BackendError, parallel_configuration
    try:
        parallel_configuration(value["context"], value.get("parallel", 1), value.get("kv_type", "f16"), value.get("cpu_moe_layers", 0))
    except BackendError as exc:
        raise RuntimeError_(str(exc)) from exc
    if value.get("display_reserved_bytes", 1_400_000_000) != 1_400_000_000:
        raise RuntimeError_("Pooled recipe changes the exact display reserve")
    weights = value.get("split_weights")
    if (not isinstance(weights, list) or len(weights) != 2
            or any(type(n) is not int or not 1 <= n <= 10000 for n in weights)):
        raise RuntimeError_("Pooled recipe requires two explicit positive split weights")
    if (not isinstance(value.get("library_dirs"), list) or not value["library_dirs"]
            or any(not isinstance(p, str) or not Path(p).is_absolute() or not Path(p).is_dir()
                   for p in value["library_dirs"])):
        raise RuntimeError_("Pooled backend library directories are unavailable")
    backend = Path(value["backend_path"]).resolve(strict=True)
    if (backend.name != "llama-server" or not os.access(backend, os.X_OK)
            or not re.fullmatch(r"[a-f0-9]{64}", str(value.get("backend_sha256", "")))
            or digest(backend) != value["backend_sha256"]):
        raise RuntimeError_("Pooled backend differs from the installed verified binary")
    if not re.fullmatch(r"[a-f0-9]{64}", str(value.get("rpc_sha256", ""))):
        raise RuntimeError_("Pooled recipe requires the paired RPC executable digest")
    _rpc_recipe(value)
    identity = value.get("model_identity")
    if (not isinstance(identity, dict)
            or identity.get("sha256") != "e64276656f072e9b2197a61723667cbdb71da66a716ffd966e572c21088f3cd3"
            or identity.get("bytes") != 17629760736):
        raise RuntimeError_("This pooled profile requires its verified Inkling target")
    _artifact(value["path"], value["model_identity"])
    draft = value.get("draft", {"enabled": False})
    if not isinstance(draft, dict) or type(draft.get("enabled")) is not bool:
        raise RuntimeError_("Pooled draft setting must explicitly enable or disable speculation")
    if draft["enabled"]:
        if (draft.get("sha256") != "3da00cf7c31bbe5662b3c0d373ecf782d74d32ef43990acc158ce60c2add72e0"
                or draft.get("expected_bytes") != 503889952
                or type(draft.get("n_max", 3)) is not int or not 1 <= draft.get("n_max", 3) <= 8):
            raise RuntimeError_("Pooled DSpark sidecar identity differs from the verified target-specific GGUF")
        _artifact(draft["path"], {"sha256": draft["sha256"], "bytes": draft["expected_bytes"]})
    return value


def _run_recipe_checked(record_path, *, cancelled=None):
    from pooled_backend import DraftConfig, probe, command_plan
    record_path = Path(record_path).absolute()
    recipe = runtime_recipe(record_path)
    os.environ.pop("LD_PRELOAD", None)
    os.environ["LD_LIBRARY_PATH"] = os.pathsep.join(recipe["library_dirs"])
    guard = capture_guard(recipe["guard_config"])
    policies = guard_policies(guard)
    rpc = capture_rpc(recipe["v100_uuid"])
    expected = _recipe_pin(recipe, recipe["rpc_process"])
    if rpc != expected:
        raise RuntimeError_("Running guest RPC differs from the exact paired frozen process")
    guest = GuestRPC(rpc)
    hooks = system_hooks(guard, record_path.with_suffix(".pool-status.json"))
    proof = probe(recipe["backend_path"])
    acquired = time.monotonic_ns()
    remote = guest.observe(5)
    snap = hooks.read_snapshot(3)
    _guest_memory(snap, remote)
    policy = PoolPolicy("CUDA:" + RTX_UUID, rpc.gpu_uuid, RPC_ENDPOINT, 5_000_000_000)
    before = health_budget(snap, policies, policy, {
        "rtx5080": allocation_bound(hooks.host_allocations(3), set(), uuid=RTX_UUID),
        "v100": allocation_bound(remote["allocations"], {rpc.pid}),
    })
    draft = recipe.get("draft", {"enabled": False})
    optional = DraftConfig(draft["path"], draft["sha256"], draft["expected_bytes"], draft.get("n_max", 3)) if draft["enabled"] else None
    plan = command_plan(
        proof, guest_identity=GuestIdentity(RPC_ENDPOINT, proof.devices[1].name, rpc.gpu_uuid,
                                           "Tesla V100-PCIE-16GB", acquired, rpc.key),
        now_ns=time.monotonic_ns(), max_age_ns=5_000_000_000,
        model=recipe["path"], alias=recipe["alias"], key_file=str(record_path.with_suffix(".key")),
        context=recipe["context"], port=recipe["port"], threads=recipe["threads"],
        free_targets_bytes=tuple(d.unconsumed_reserve_bytes for d in before.devices),
        split_weights=tuple(recipe["split_weights"]), draft=optional,
        parallel=recipe.get("parallel", 1), kv_type=recipe.get("kv_type", "f16"),
        cpu_moe_layers=recipe.get("cpu_moe_layers", 0),
    )
    outcome = run_owned(plan, guest=guest, policies=policies, hooks=hooks,
                        cancelled=cancelled or threading.Event(), launch_id=recipe["launch_id"],
                        startup_id=recipe["launch_id"],
                        run_timeout=recipe.get("run_timeout", 86400))
    return 0 if outcome.stop_verified and outcome.trigger in ("cancelled", "process_exited") else 1


def run_recipe(record_path, *, cancelled=None):
    """Owned unit entry; failed pre-load validation also cleans its idle RPC."""
    initial = read_private_json(record_path)
    try:
        return _run_recipe_checked(record_path, cancelled=cancelled)
    except BaseException:
        try:
            # A transferred inference fence makes this helper refuse. The
            # owning supervisor/guard must then prove the full two-sided stop.
            cleanup_pending_rpc(initial)
        except BaseException:
            pass  # Preserve the validation error and durable failed-release state.
        raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("record", type=Path)
    args = parser.parse_args()
    cancelled = threading.Event()
    for sig in (signal.SIGTERM, signal.SIGINT):
        signal.signal(sig, lambda *_: cancelled.set())
    try:
        return run_recipe(args.record, cancelled=cancelled)
    except (OSError, ValueError, RuntimeError, KeyError, TypeError, subprocess.SubprocessError) as exc:
        # Configuration, command lines, key content and raw subprocess stderr
        # are deliberately excluded from the public diagnostic.
        print("pooled_runtime: " + type(exc).__name__ + ": " + str(exc)[:500], file=sys.stderr, flush=True)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
