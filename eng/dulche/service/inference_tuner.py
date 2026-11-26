"""Run the installed Strata tuner in an independently guarded, owned cgroup.

The controller owns the maintenance reservation and model stop/restore. This
module only owns the temporary tuner: it never stops another engine or changes
an inference profile. A returned result means all temporary processes settled.
An unverified release raises CustodyUnsettled, so the controller must retain its
reservation instead of restoring a model over an unknown process lifetime.
"""
from __future__ import annotations

from dataclasses import asdict
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

from engine_profiles import PRIMARY, atomic_json, strata_home
from pooled_runtime import (GuardIdentity, RTX_UUID, _owned_json, capture_guard,
                            guard_snapshot, read_private_json)
from thermal_guard import DeviceGuard, Rule
from thermal_policy import active_cooling_rule

MIN_AVAILABLE = 12 * 1024 ** 3
MAX_OUTPUT = 4 * 1024 ** 2
MAX_SECONDS = 2700
MAX_AGE = 6.0
ACTIVE = {"active", "activating", "deactivating", "reloading"}
OPERATION = re.compile(r"[0-9a-f]{32}\Z")
UNIT = re.compile(r"dulche-inference-tune-[0-9a-f]{32}-[0-9a-f]{16}\.service\Z")
PROC = Path("/proc")
CGROUP = Path("/sys/fs/cgroup")


class TunerError(RuntimeError):
    pass


class CustodyUnsettled(TunerError):
    """The caller must keep the maintenance reservation closed."""
    def __init__(self, message, *, unit=None, receipt=None):
        super().__init__(message)
        self.unit, self.receipt = unit, receipt


def _digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def _output_json(path):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC)
    with os.fdopen(fd, "r") as stream:
        info = os.fstat(stream.fileno())
        # JSON escaping can expand bounded output (for example an escaped line
        # break); check the decoded byte limit separately before returning it.
        if (not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid()
                or info.st_mode & 0o077 or not 1 <= info.st_size <= MAX_OUTPUT * 6 + 1024):
            raise TunerError("Tuner output is not an owned bounded private file")
        value = json.load(stream)
    if not isinstance(value, dict):
        raise TunerError("Tuner output is not an object")
    return value


def _finite(value):
    return type(value) in (int, float) and math.isfinite(value)


def _memory():
    values = {}
    for line in (PROC / "meminfo").read_text().splitlines():
        key, _, tail = line.partition(":")
        if key in ("MemAvailable", "MemTotal"):
            fields = tail.split()
            if len(fields) != 2 or fields[1] != "kB":
                raise TunerError("Host memory units are unrecognised")
            values[key] = int(fields[0]) * 1024
    if not 0 < values.get("MemAvailable", 0) <= values.get("MemTotal", 0):
        raise TunerError("Host memory could not be measured")
    return values


def process_identity(pid):
    """A PID is insufficient: bind its lifetime, owner, executable and cgroup."""
    if type(pid) is not int or pid <= 1:
        raise TunerError("Invalid process identity")
    root = PROC / str(pid)
    if root.stat().st_uid != os.getuid():
        raise TunerError("Process belongs to another user")
    fields = (root / "stat").read_text().rsplit(")", 1)[1].split()
    if fields[0] in ("Z", "X"):
        raise TunerError("Process has exited")
    groups = [row[3:] for row in (root / "cgroup").read_text().splitlines()
              if row.startswith("0::")]
    if len(groups) != 1:
        raise TunerError("An identified cgroup-v2 process is required")
    return {"pid": pid, "start_ticks": fields[19], "uid": root.stat().st_uid,
            "boot_id": (PROC / "sys/kernel/random/boot_id").read_text().strip(),
            "executable": str((root / "exe").resolve(strict=True)),
            "command_sha256": _digest(root / "cmdline"), "cgroup": groups[0]}


def process_matches(identity):
    try:
        return process_identity(identity["pid"]) == identity
    except (OSError, ValueError, KeyError, IndexError, TunerError):
        return False


def _run(command, *, timeout=5):
    return subprocess.run(command, stdin=subprocess.DEVNULL, capture_output=True,
                          text=True, timeout=timeout, check=True)


def unit_state(unit):
    if not UNIT.fullmatch(unit):
        raise TunerError("Invalid tuner unit identity")
    fields = ("MainPID", "ActiveState", "SubState", "InvocationID", "ControlGroup",
              "Transient", "LoadState", "Result", "ExecMainStatus")
    command = ["systemctl", "--user", "show", unit]
    for field in fields:
        command += ["-p", field]
    reply = _run(command, timeout=3)
    return dict(row.split("=", 1) for row in reply.stdout.splitlines() if "=" in row)


def _cgroup_path(group, unit):
    if not isinstance(group, str) or not group.startswith("/") or not group.endswith("/" + unit):
        raise TunerError("Tuner cgroup does not identify its exact unit")
    path = (CGROUP / group.lstrip("/")).resolve()
    if not path.is_relative_to(CGROUP.resolve()):
        raise TunerError("Tuner cgroup escaped its hierarchy")
    return path


def _members(group, unit):
    root = _cgroup_path(group, unit)
    if not root.exists():
        return set()
    members = set()
    for path in [root / "cgroup.procs", *root.rglob("cgroup.procs")]:
        try:
            members.update(int(line) for line in path.read_text().splitlines())
        except FileNotFoundError:
            continue
    return members


class OwnedCgroup:
    """Bounded cleanup includes detached descendants, with pidfd identity fences."""
    def __init__(self, unit, invocation, group, *, own_pid=None):
        if not UNIT.fullmatch(unit) or not OPERATION.fullmatch(invocation):
            raise TunerError("The tuner has no valid systemd custody identity")
        _cgroup_path(group, unit)
        self.unit, self.invocation, self.group = unit, invocation, group
        self.own_pid = os.getpid() if own_pid is None else own_pid

    def members(self):
        return _members(self.group, self.unit) - {self.own_pid}

    def signal_member(self, pid, sig):
        # Opening the pidfd first prevents PID reuse between membership proof and
        # signalling. A process outside this cgroup is never signalled.
        try:
            fd = os.pidfd_open(pid, 0)
        except ProcessLookupError:
            return
        try:
            try:
                identity = process_identity(pid)
            except (FileNotFoundError, ProcessLookupError, TunerError):
                return
            if (identity["uid"] != os.getuid()
                    or not (identity["cgroup"] == self.group
                            or identity["cgroup"].startswith(self.group + "/"))):
                raise TunerError("A tuner child moved outside its owned cgroup")
            signal.pidfd_send_signal(fd, sig)
        except ProcessLookupError:
            pass
        finally:
            os.close(fd)

    def stop_children(self, *, reap=lambda: None, clock=time.monotonic, sleep=time.sleep):
        # TasksMax bounds the number of processes. SIGKILL is retried within a
        # fixed deadline so a forking/setsid child cannot outlive a claimed result.
        for sig, duration in ((signal.SIGTERM, 2.0), (signal.SIGKILL, 5.0)):
            deadline = clock() + duration
            while True:
                reap()
                members = self.members()
                if not members:
                    return True
                for pid in members:
                    self.signal_member(pid, sig)
                if clock() >= deadline:
                    break
                sleep(0.05)
        reap()
        return not self.members()


def _rtx_rule(config):
    rows = [row for row in config.get("devices", []) if row.get("id") == "rtx5080"]
    if len(rows) != 1:
        raise TunerError("Exactly one RTX thermal rule is required")
    cfg = dict(rows[0])
    if cfg.get("kind") != "nvml" or cfg.get("index") != 0 or cfg.get("allow_simulated") is not False:
        raise TunerError("Tuning requires the real configured RTX sensor")
    if cfg.get("standby_file") or not _finite(cfg.get("stop_c")) or not 0 < cfg["stop_c"] <= 80:
        raise TunerError("The RTX thermal rule is not qualified for tuning")
    if cfg.get("thermal_policy_version") is not None and not active_cooling_rule(cfg, "rtx5080"):
        raise TunerError("The active-cooling rule is not qualified")
    floor = cfg.get("min_host_available_mib", 0)
    if not _finite(floor) or floor < 0:
        raise TunerError("Invalid thermal host-memory floor")
    cfg["min_host_available_mib"] = max(MIN_AVAILABLE // 2 ** 20, floor)
    return Rule(cfg)


class ThermalMonitor:
    """Use the existing thermal state machine on real, fresh, pinned snapshots."""
    def __init__(self, pin):
        self.pin = pin
        config = _owned_json(pin.config_path)
        if _digest(pin.config_path) != pin.config_sha256:
            raise TunerError("Thermal policy changed before tuner admission")
        self.rule = _rtx_rule(config)
        self.stops = []
        self.guard = DeviceGuard(self.rule, None, self._action, lambda _: None)

    def _action(self, action, device, reason, reading, first):
        if action == "stop":
            self.stops.append(reason)

    def check(self, *, snapshot=None, memory=None, now=None):
        snapshot = guard_snapshot(self.pin, 3) if snapshot is None else snapshot
        memory = _memory() if memory is None else memory
        now = time.time() if now is None else now
        row = snapshot.get("devices", {}).get("rtx5080", {})
        for timestamp in (snapshot.get("ts"), row.get("last_telemetry_ts")):
            if not _finite(timestamp) or not 0 <= now - timestamp <= MAX_AGE:
                raise TunerError("RTX thermal telemetry is stale or unreadable")
        if (snapshot.get("halted") is True or row.get("telemetry_ok") is not True
                or row.get("stopped") is not False or row.get("standby") is not False
                or row.get("power_limit_verified") is not True):
            raise TunerError("The independent thermal guard has closed admission")
        reading = row.get("reading_full")
        if (not isinstance(reading, dict) or reading.get("driver_ok") is not True
                or reading.get("simulated") is True
                or reading.get("name") != "NVIDIA GeForce RTX 5080"
                or not _finite(reading.get("temp")) or not 0 < reading["temp"] < 150):
            raise TunerError("RTX telemetry does not prove a real healthy device")
        expected = {"version": self.rule.policy_version, "warn_c": self.rule.warn_c,
                    "stop_c": self.rule.stop_c, "core_grace_s": self.rule.core_grace_s,
                    "core_emergency_c": self.rule.core_emergency_c,
                    "memory_stop_c": self.rule.memory_stop_c}
        if row.get("thermal_policy") != expected:
            raise TunerError("The live thermal policy differs from its pinned configuration")
        available = memory.get("MemAvailable")
        if type(available) is not int or available < self.rule.min_host_available_mib * 2 ** 20:
            raise TunerError("Host memory fell below the qualified floor (at least 12 GiB)")
        self.guard.evaluate(reading, now, host_available_mib=available / 2 ** 20)
        if self.stops:
            raise TunerError("Thermal guard stopped tuning: " + self.stops[-1])


def _control():
    controls = strata_home() / "components/control"
    active = _owned_json(controls / "active.json")
    control = (controls / active["folder"]).resolve(strict=True)
    if control.parent != controls.resolve():
        raise TunerError("Installed Strata Control has an invalid identity")
    paths = [control / "python/bin/python3.12", control / "strata.py",
             control / "strata_cli/tune_commands.py"]
    if not all(path.is_file() for path in paths):
        raise TunerError("The installed Strata tuner is incomplete")
    return control


def _guard(state_dir, engine_root=None):
    root = Path(engine_root) if engine_root else Path(state_dir) / "engines"
    pin = capture_guard(root / "v100-guard.json")
    ThermalMonitor(pin).check()
    return pin


def capability(state_dir, mid, *, engine_root=None):
    if mid != PRIMARY:
        return {"supported": False, "reason": "This model has not qualified a guarded Strata tuning lifetime."}
    try:
        _control()
        if not (hasattr(os, "pidfd_open") and hasattr(signal, "pidfd_send_signal")
                and (CGROUP / "cgroup.controllers").is_file()
                and Path("/usr/bin/systemd-run").is_file()):
            raise TunerError("Tuning requires cgroup-v2 and pidfd process custody")
        _guard(state_dir, engine_root)
    except (OSError, ValueError, KeyError, RuntimeError, subprocess.SubprocessError) as exc:
        return {"supported": False, "reason": str(exc)[:500]}
    return {"supported": True, "guardian": "systemd-cgroup", "min_host_available_bytes": MIN_AVAILABLE}


def _validate_command(state_dir, mid, op_id, argv, cwd):
    if mid != PRIMARY or not OPERATION.fullmatch(op_id):
        raise TunerError("This tuning model or operation is not qualified")
    work = Path(state_dir).resolve() / "inference-management" / op_id
    if work.is_symlink() or not work.is_dir() or work.stat().st_uid != os.getuid():
        raise TunerError("The operation work directory is not owned")
    control = _control()
    if Path(cwd).resolve() != control:
        raise TunerError("Tuner working directory differs from the installed control")
    if not isinstance(argv, list) or not all(isinstance(x, str) and "\0" not in x for x in argv):
        raise TunerError("Invalid tuner arguments")
    prefix = [str(control / "python/bin/python3.12"), str(control / "strata.py"),
              "tune", "auto", str(work / "source.json")]
    if argv[:5] != prefix:
        raise TunerError("Tuner executable or source does not match the operation")
    options = argv[5:]
    if options.count("--no-save") != 1:
        raise TunerError("The supervised tuner must not change Strata presets")
    options = [arg for arg in options if arg != "--no-save"]
    if len(options) % 2:
        raise TunerError("Invalid tuner option pairs")
    keys = options[::2]
    if len(set(keys)) != len(keys):
        raise TunerError("Duplicate tuner options are not permitted")
    values = dict(zip(keys, options[1::2]))
    expected = {"--mode": "quick", "--goal": "speed", "--use": "coding", "--time": "20",
                "--work-dir": str(work), "--status-file": str(work / "status.json"), "--output": "json"}
    if set(values) != set(expected) | {"--gpu-lock"} or any(values[k] != v for k, v in expected.items()):
        raise TunerError("Unsupported tuner options or guard bypass")
    lock = Path(values["--gpu-lock"])
    if not lock.is_absolute() or lock.is_symlink() or not lock.parent.is_dir():
        raise TunerError("The maintenance lease must provide an absolute owned GPU lock")
    if lock.parent.stat().st_uid != os.getuid() or (lock.exists() and lock.stat().st_uid != os.getuid()):
        raise TunerError("The GPU lock belongs to another user")
    source = read_private_json(work / "source.json")
    args = source.get("args", [])
    if source.get("model_name") != mid or not isinstance(args, list):
        raise TunerError("The tuner source does not identify the selected model")
    try:
        context = int(args[args.index("--max-context") + 1])
    except (ValueError, IndexError, TypeError) as exc:
        raise TunerError("The tuner source has no explicit context") from exc
    if not 512 <= context <= 1048576 or any(x in args for x in ("--disable-guard", "--no-guard")):
        raise TunerError("Invalid context or disabled source guard")
    return work, context


def _systemd_quote(value):
    return '"' + str(value).replace("\\", "\\\\").replace('"', '\\"').replace("%", "%%") + '"'


def service_command(recipe_path, recipe):
    """No shell: systemd parses only the quoted ExecStopPost property."""
    script = recipe["script"]
    post = " ".join(_systemd_quote(x) for x in
                    ["/usr/bin/python3", "-B", script, "settle", str(recipe_path)])
    properties = {"Type": "exec", "KillMode": "control-group", "SendSIGKILL": "yes",
                  "TimeoutStopSec": "10s", "RuntimeMaxSec": str(recipe["timeout_seconds"] + 30),
                  "MemoryMax": str(recipe["memory_max"]), "MemorySwapMax": "0", "OOMPolicy": "kill",
                  "TasksMax": "128", "UMask": "0077", "NoNewPrivileges": "yes", "LimitCORE": "0",
                  "LimitFSIZE": str(64 * 1024 ** 2), "Restart": "no", "StandardOutput": "null",
                  "StandardError": "journal", "ExecStopPost": post}
    command = ["systemd-run", "--user", "--unit=" + recipe["unit"], "--working-directory=" + recipe["cwd"]]
    for key, value in properties.items():
        command += ["--property=" + key + "=" + value]
    return command + ["--", "/usr/bin/python3", "-B", script, "guardian", str(recipe_path)]


def _recipe(path):
    path = Path(path).absolute()
    value = read_private_json(path)
    if (value.get("schema") != "dulche-inference-tuner/v1" or not UNIT.fullmatch(value.get("unit", ""))
            or not OPERATION.fullmatch(value.get("operation_id", ""))
            or value.get("model") != PRIMARY or type(value.get("timeout_seconds")) is not int
            or not 1 <= value["timeout_seconds"] <= MAX_SECONDS
            or type(value.get("memory_max")) is not int or value["memory_max"] <= 0):
        raise TunerError("The tuner custody recipe is invalid")
    if (Path(value["script"]).resolve() != Path(__file__).resolve()
            or _digest(value["script"]) != value.get("script_sha256")):
        raise TunerError("The guardian source changed after admission")
    work, context = _validate_command(value["state_dir"], value["model"], value["operation_id"],
                                      value["argv"], value["cwd"])
    if path != work / "tuner-runner" / "recipe.json" or context != value.get("context_tokens"):
        raise TunerError("The tuner recipe is outside its operation or changed context")
    if _digest(work / "source.json") != value.get("source_sha256"):
        raise TunerError("The source configuration changed after tuner admission")
    return value


def _receipt(path, recipe, **changes):
    value = {"schema": "dulche-inference-tuner-receipt/v1", "unit": recipe["unit"],
             "operation_id": recipe["operation_id"], "model": recipe["model"],
             "context_tokens": recipe["context_tokens"], "updated_epoch": time.time(), **changes}
    atomic_json(path, value)
    return value


class BoundedOutput:
    def __init__(self, pipe):
        self.pipe, self.data, self.overflow = pipe, bytearray(), False
        self.thread = threading.Thread(target=self._drain, daemon=True, name="tuner-output")
        self.thread.start()

    def _drain(self):
        while True:
            chunk = self.pipe.read(8192)
            if not chunk:
                return
            room = MAX_OUTPUT - len(self.data)
            self.data.extend(chunk[:room])
            if len(chunk) > room:
                self.overflow = True

    def finish(self):
        self.thread.join(timeout=2)
        if self.thread.is_alive():
            raise TunerError("A tuner output descriptor remains open after cleanup")
        self.pipe.close()
        return self.data.decode("utf-8", errors="replace")


def supervise_child(child, custody, *, check, parent_alive, cancelled, timeout,
                    output, clock=time.monotonic, sleep=time.sleep):
    """The guardian, not a daemon thread, owns this loop and bounded cleanup."""
    deadline = clock() + timeout
    code, state, reason = 1, "failed", None
    try:
        while True:
            if not parent_alive():
                raise TunerError("The initiating service process ended or changed identity")
            if cancelled():
                raise InterruptedError("The tuning operation was cancelled")
            if clock() >= deadline:
                raise TunerError("The tuning time limit was reached")
            if output.overflow:
                raise TunerError("Tuner output exceeded its bound")
            check()
            value = child.poll()
            if value is not None:
                code = value
                state = "completed" if value == 0 else "failed"
                break
            sleep(0.5)
    except InterruptedError as exc:
        code, state, reason = 130, "cancelled", str(exc)
    except BaseException as exc:
        code, state, reason = 1, "failed", str(exc)[:500]
    try:
        verified = custody.stop_children(reap=child.poll)
    except BaseException as exc:
        verified = False
        reason = "Tuner cleanup failed: " + str(exc)[:400]
    if not verified:
        code, state = 1, "cleanup_unverified"
        reason = reason or "Temporary tuner processes remain in the owned cgroup"
    return {"returncode": code, "state": state, "error": reason, "cleanup_verified": verified}


def guardian(path):
    recipe = _recipe(path)
    root = Path(path).parent
    state = unit_state(recipe["unit"])
    mine = process_identity(os.getpid())
    if (state.get("Transient") != "yes" or state.get("MainPID") != str(os.getpid())
            or state.get("InvocationID") != os.environ.get("INVOCATION_ID")
            or state.get("ControlGroup") != mine["cgroup"]):
        raise TunerError("The guardian is not the main process of its exact transient unit")
    custody = OwnedCgroup(recipe["unit"], state["InvocationID"], mine["cgroup"])
    pin = {"invocation": custody.invocation, "cgroup": custody.group, "guardian": mine}
    _receipt(root / "receipt.json", recipe, state="starting", settled=False, cleanup_verified=False, **pin)
    signals = threading.Event()
    for sig in (signal.SIGTERM, signal.SIGINT):
        signal.signal(sig, lambda *_: signals.set())
    child = output = None
    result = {"returncode": 1, "state": "failed", "cleanup_verified": False}
    try:
        if not process_matches(recipe["parent"]):
            raise TunerError("The initiating service process is no longer present")
        monitor = ThermalMonitor(GuardIdentity(**recipe["guard"]))
        monitor.check()
        if (root / "cancel.json").exists():
            raise InterruptedError("The tuning operation was cancelled before launch")
        environment = dict(os.environ)
        for key in ("LD_PRELOAD", "LD_LIBRARY_PATH", "PYTHONPATH", "PYTHONHOME", "STRATA_API_KEY"):
            environment.pop(key, None)
        environment.update(PYTHONNOUSERSITE="1", PYTHONDONTWRITEBYTECODE="1", CUDA_VISIBLE_DEVICES=RTX_UUID)
        child = subprocess.Popen(recipe["argv"], cwd=recipe["cwd"], env=environment,
                                 stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                                 stderr=subprocess.STDOUT, start_new_session=True)
        output = BoundedOutput(child.stdout)
        _receipt(root / "receipt.json", recipe, state="running", settled=False,
                 cleanup_verified=False, child=process_identity(child.pid), **pin)
        result = supervise_child(child, custody, check=monitor.check,
                                 parent_alive=lambda: process_matches(recipe["parent"]),
                                 cancelled=lambda: signals.is_set() or (root / "cancel.json").exists(),
                                 timeout=recipe["timeout_seconds"], output=output)
    except BaseException as exc:
        result.update(error=str(exc)[:500], state="cancelled" if isinstance(exc, InterruptedError) else "failed",
                      returncode=130 if isinstance(exc, InterruptedError) else 1)
        try:
            result["cleanup_verified"] = custody.stop_children(reap=child.poll if child else lambda: None)
        except BaseException:
            result["cleanup_verified"] = False
    stdout = ""
    if output:
        try:
            stdout = output.finish()
        except TunerError as exc:
            result.update(returncode=1, state="failed", error=str(exc))
    if result.get("error"):
        stdout = json.dumps({"ok": False, "error": {"message": result["error"]}}) + "\n"
    atomic_json(root / "output.json", {"stdout": stdout, "returncode": result["returncode"]})
    _receipt(root / "receipt.json", recipe, settled=False, **result, **pin)
    return result["returncode"]


def settle(path):
    """ExecStopPost persists proof even after OOM, guardian death or runtime expiry."""
    # Do not re-open source/config artifacts: their change can cause the failure
    # whose custody receipt must still be retained after systemd cleans the unit.
    recipe = read_private_json(path)
    unit = recipe["unit"]
    mine = process_identity(os.getpid())
    invocation = os.environ.get("INVOCATION_ID", "")
    custody = OwnedCgroup(unit, invocation, mine["cgroup"])
    root = Path(path).parent
    try:
        prior = read_private_json(root / "receipt.json")
    except (OSError, ValueError, RuntimeError):
        prior = {}
    if prior and (prior.get("unit") != unit or prior.get("invocation") != invocation):
        raise TunerError("The terminal receipt belongs to another unit invocation")
    empty = not custody.members()
    service_result = os.environ.get("SERVICE_RESULT", "unknown")
    success = (empty and service_result == "success" and prior.get("state") == "completed"
               and prior.get("cleanup_verified") is True and prior.get("returncode") == 0)
    state = "completed" if success else (prior.get("state") if prior.get("state") in ("cancelled", "failed") else "failed")
    _receipt(root / "receipt.json", recipe, invocation=invocation, cgroup=mine["cgroup"],
             state=state, returncode=0 if success else (130 if state == "cancelled" else 1),
             error=None if success else prior.get("error") or "The tuner service ended: " + service_result,
             cleanup_verified=empty, settled=empty, service_result=service_result)
    return 0 if empty else 1


def _stop_owned(recipe, receipt):
    state = unit_state(recipe["unit"])
    if state.get("ActiveState") not in ACTIVE:
        return
    if (not receipt or receipt.get("unit") != recipe["unit"]
            or state.get("Transient") != "yes" or state.get("InvocationID") != receipt.get("invocation")
            or state.get("ControlGroup") != receipt.get("cgroup")):
        raise CustodyUnsettled("Cannot prove the tuner unit identity; no foreign unit was stopped",
                               unit=recipe["unit"])
    _run(["systemctl", "--user", "stop", recipe["unit"]], timeout=20)


def _settled(recipe, receipt):
    if (not receipt or receipt.get("unit") != recipe["unit"]
            or receipt.get("operation_id") != recipe["operation_id"]
            or receipt.get("settled") is not True or receipt.get("cleanup_verified") is not True):
        return False
    state = unit_state(recipe["unit"])
    if state.get("ActiveState") in ACTIVE:
        return False
    invocation = state.get("InvocationID")
    if invocation and invocation != receipt.get("invocation"):
        raise CustodyUnsettled("The tuner unit was replaced before cleanup was verified", unit=recipe["unit"])
    return not _members(receipt["cgroup"], recipe["unit"])


def run_tuner(state_dir, mid, operation_id, argv, *, cwd, cancel_event,
              timeout_seconds=MAX_SECONDS, engine_root=None):
    """Called only inside the controller's exclusive maintenance reservation."""
    if type(timeout_seconds) is not int or not 1 <= timeout_seconds <= MAX_SECONDS:
        raise TunerError("Invalid bounded tuning duration")
    work, context = _validate_command(state_dir, mid, operation_id, argv, cwd)
    if cancel_event.is_set():
        raise InterruptedError("The tuning operation was cancelled")
    guard = _guard(state_dir, engine_root)
    memory = _memory()
    floor = _rtx_rule(_owned_json(guard.config_path)).min_host_available_mib * 2 ** 20
    budget = int(min(memory["MemTotal"] - floor, memory["MemAvailable"] - floor))
    if budget < 1024 ** 3:
        raise TunerError("At least 1 GiB of tuning capacity above the 12 GiB host reserve is required")
    root = work / "tuner-runner"
    root.mkdir(mode=0o700)  # An operation cannot reuse a previous process receipt.
    script = str(Path(__file__).resolve())
    recipe = {"schema": "dulche-inference-tuner/v1", "model": mid, "operation_id": operation_id,
              "unit": "dulche-inference-tune-" + operation_id + "-" + secrets.token_hex(8) + ".service",
              "state_dir": str(Path(state_dir).resolve()), "argv": argv, "cwd": str(Path(cwd).resolve()),
              "timeout_seconds": timeout_seconds, "context_tokens": context, "memory_max": budget,
              "script": script, "script_sha256": _digest(script), "source_sha256": _digest(work / "source.json"),
              "parent": process_identity(os.getpid()), "guard": asdict(guard), "created_epoch": time.time()}
    path = root / "recipe.json"
    atomic_json(path, recipe)
    launch_error = None
    try:
        _run(service_command(path, recipe), timeout=15)
    except (OSError, subprocess.SubprocessError) as exc:
        # systemd-run can time out after admission. Never treat its client exit as
        # proof the temporary engine could not have started.
        launch_error = str(exc)[:300]
        try:
            atomic_json(root / "cancel.json", {"reason": "Launch acknowledgement failed", "epoch": time.time()})
        except OSError:
            pass  # The bounded independent guardian still owns cleanup.
    deadline = time.monotonic() + (30 if launch_error else timeout_seconds + 45)
    receipt, proven_settled = None, False
    try:
        while time.monotonic() < deadline:
            if cancel_event.is_set() and not (root / "cancel.json").exists():
                atomic_json(root / "cancel.json", {"reason": "Caller cancelled", "epoch": time.time()})
            try:
                receipt = read_private_json(root / "receipt.json")
                if _settled(recipe, receipt):
                    proven_settled = True
                    if receipt["returncode"] != 0 or launch_error:
                        error = launch_error or receipt.get("error") or "The supervised tuner failed"
                        return {"returncode": receipt["returncode"] or 1,
                                "stdout": json.dumps({"ok": False, "error": {"message": error}}),
                                "receipt": str(root / "receipt.json")}
                    output = _output_json(root / "output.json")
                    if (output.get("returncode") != 0 or not isinstance(output.get("stdout"), str)
                            or len(output["stdout"].encode()) > MAX_OUTPUT):
                        raise TunerError("The settled tuner output is invalid or oversized")
                    return {**output, "receipt": str(root / "receipt.json")}
            except (FileNotFoundError, subprocess.SubprocessError):
                pass
            time.sleep(0.25)
    except BaseException as exc:
        if proven_settled:
            raise
        launch_error = "Tuner observation failed: " + str(exc)[:300]
    try:
        atomic_json(root / "cancel.json", {"reason": "Caller time limit", "epoch": time.time()})
        _stop_owned(recipe, receipt)
        for _ in range(20):
            try:
                receipt = read_private_json(root / "receipt.json")
                if _settled(recipe, receipt):
                    return {"returncode": 1, "stdout": json.dumps({"ok": False, "error": {"message": "Tuner time limit or launch failure"}}),
                            "receipt": str(root / "receipt.json")}
            except (FileNotFoundError, subprocess.SubprocessError):
                pass
            time.sleep(0.25)
    except BaseException:
        pass
    raise CustodyUnsettled("Tuner process release is unverified; retain the maintenance reservation",
                           unit=recipe["unit"], receipt=str(root / "receipt.json"))


if __name__ == "__main__":
    if len(sys.argv) != 3 or sys.argv[1] not in ("guardian", "settle"):
        raise SystemExit("Usage: inference_tuner.py guardian|settle PRIVATE_RECIPE")
    raise SystemExit(guardian(sys.argv[2]) if sys.argv[1] == "guardian" else settle(sys.argv[2]))
