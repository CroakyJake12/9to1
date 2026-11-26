"""Use installed, guarded native services through Dulche's existing lifecycle owner.

Registrations are private machine configuration, never commands supplied by chat.
The registered unit retains its launcher, artifact checks and independent guards.
"""
from __future__ import annotations

import contextlib
import hashlib
import http.client
import json
import os
from pathlib import Path
import re
import socket
import stat
import subprocess
import threading
import time

from engine_profiles import EngineError, GPU_RTX, GPU_V100

CGROUP_ROOT = Path("/sys/fs/cgroup")


def verify_released_groups(unit, before, after):
    """Retain the pre-stop cgroup: systemd can clear ControlGroup on exit."""
    groups = {state.get("ControlGroup") for state in (before, after) if state.get("ControlGroup")}
    if not groups:
        if all(state.get("ActiveState") == "inactive" and int(state.get("MainPID", 0)) <= 1
               for state in (before, after)):
            return
        raise EngineError("The owned native unit has no verifiable cgroup identity")
    root = CGROUP_ROOT.resolve(strict=True)
    for group in groups:
        if not isinstance(group, str) or not group.startswith("/") or not group.endswith("/" + unit):
            raise EngineError("The native cgroup does not identify the registered unit")
        path = (root / group.lstrip("/")).resolve()
        if not path.is_relative_to(root) or path == root or path.name != unit:
            raise EngineError("The native cgroup escaped the registered hierarchy")
        if not path.exists():
            continue
        if not path.is_dir():
            raise EngineError("The native cgroup is not a directory")
        def unreadable(error):
            raise error
        try:
            for directory, _, _ in os.walk(path, onerror=unreadable, followlinks=False):
                pids = (Path(directory) / "cgroup.procs").read_text().splitlines()
                if any(not row.isdigit() or int(row) <= 0 for row in pids):
                    raise EngineError("The native cgroup contains an invalid process observation")
                if pids:
                    raise EngineError("The owned native cgroup still contains processes; release is unverified")
        except OSError as exc:
            if path.exists():
                raise EngineError("The existing native cgroup could not be inspected; release is unverified") from exc


def private_json(path):
    path = Path(path)
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC)
    with os.fdopen(fd) as stream:
        info = os.fstat(stream.fileno())
        if (not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid()
                or info.st_mode & 0o022 or info.st_size > 1024 * 1024):
            raise EngineError("Managed runtime configuration must be an owned regular file")
        value = json.load(stream)
    if not isinstance(value, dict):
        raise EngineError("Managed runtime configuration must be an object")
    return value


def unit_state(name):
    reply = subprocess.run(["systemctl", "--user", "show", name,
                            "-p", "ActiveState", "-p", "SubState", "-p", "MainPID",
                            "-p", "ControlGroup", "-p", "Result"],
                           capture_output=True, text=True, timeout=5)
    if reply.returncode:
        raise EngineError("The registered native service is unavailable")
    return dict(line.split("=", 1) for line in reply.stdout.splitlines() if "=" in line)


class ManagedServing:
    def __init__(self, root):
        self.root = Path(root)
        self.registry = self.root / "managed-serving.json"

    def _entries(self):
        if not self.registry.exists():
            return {}
        value = private_json(self.registry)
        if value.get("schema") != "9to1.managed-serving/v1" or not isinstance(value.get("models"), dict):
            raise EngineError("Unknown managed runtime registry")
        return value["models"]

    def ids(self):
        return list(self._entries())

    def handles(self, mid):
        return mid in self._entries()

    def _registration(self, mid):
        profile = self._entries().get(mid)
        if not isinstance(profile, dict) or profile.get("kind") != "strata-native-service":
            raise EngineError("Unknown managed native model")
        unit = profile.get("unit", "")
        guards = profile.get("guard_units")
        if (not re.fullmatch(r"(?:dulche|alora)-[a-z0-9-]+\.service", unit)
                or not isinstance(guards, list) or not guards
                or any(not isinstance(g, str) or not re.fullmatch(r"(?:dulche|alora)-[a-z0-9-]+\.service", g) for g in guards)):
            raise EngineError("Managed runtime needs its installed guarded service units")
        for key in ("source", "launcher", "ready_file"):
            if not isinstance(profile.get(key), str) or not Path(profile[key]).is_absolute():
                raise EngineError("Managed runtime paths must be absolute")
        return profile

    def _profile(self, mid):
        profile = self._registration(mid)
        for key in ("source", "launcher"):
            path = Path(profile[key])
            info = path.lstat()
            if not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o022:
                raise EngineError("Managed runtime source ownership changed")
            expected = profile.get(key + "_sha256")
            if not isinstance(expected, str) or hashlib.sha256(path.read_bytes()).hexdigest() != expected:
                raise EngineError("Managed runtime " + key + " changed; review its registration")
        cfg = private_json(profile["source"])
        if (cfg.get("model_name") != mid or cfg.get("host") != "127.0.0.1"
                or type(cfg.get("port")) is not int or not 1024 <= cfg["port"] <= 65535
                or not isinstance(cfg.get("api_key"), str) or not cfg["api_key"]):
            raise EngineError("Managed runtime model or loopback identity changed")
        args = cfg.get("args")
        if not isinstance(args, list) or not all(isinstance(x, str) for x in args):
            raise EngineError("Invalid managed native configuration")
        return profile, cfg

    @staticmethod
    def _arg(cfg, flag):
        args = cfg["args"]
        if args.count(flag) != 1 or args.index(flag) + 1 >= len(args):
            raise EngineError("Missing unique native option " + flag)
        return args[args.index(flag) + 1]

    def spec(self, mid):
        profile, cfg = self._profile(mid)
        context = int(self._arg(cfg, "--max-context"))
        gpu = profile.get("gpu", GPU_RTX)
        if gpu not in (GPU_RTX, GPU_V100) or not 512 <= context <= 1048576:
            raise EngineError("Unsupported managed device or context")
        caps = profile.get("capabilities", ["text", "streaming"])
        if not isinstance(caps, list) or any(c not in ("text", "streaming", "vision", "tools", "usage", "structured_output") for c in caps):
            raise EngineError("Invalid declared runtime capabilities")
        return {"id": mid, "name": profile.get("name", mid), "unit": profile["unit"],
                "source": profile["source"], "path": self._arg(cfg, "--native"),
                "base_model": profile.get("base_model"), "adapter_path": profile.get("adapter_path"),
                "fixed_adapter": profile.get("fixed_adapter"), "capabilities": list(caps),
                "engine": "Thunder", "kind": "managed-strata", "gpu": gpu,
                "context": context, "max_context": context, "slots": 1,
                "quantization": profile.get("quantization", "native"),
                "physical_devices": ["rtx5080" if gpu == GPU_RTX else "v100"],
                "port": cfg["port"], "threads": 4}

    def _connection(self, mid, profile, cfg, unit):
        if unit.get("ActiveState") != "active" or int(unit.get("MainPID", 0)) <= 1:
            return None
        try:
            ready = private_json(profile["ready_file"])
            pid = ready.get("pid")
            group = unit.get("ControlGroup")
            if (type(pid) is not int or pid <= 1 or not group
                    or ready.get("model_id") != mid or ready.get("bound_port") != cfg["port"]
                    or ready.get("bound_host") != "127.0.0.1"
                    or ready.get("api_state") != "ready" or ready.get("model_state") != "ready"):
                return None
            memberships = Path(f"/proc/{pid}/cgroup").read_text().splitlines()
            if not any(line.split(":", 2)[-1] == group for line in memberships):
                return None
            from engines import _listener
            if not _listener(pid, cfg["port"]):
                return None
            conn = http.client.HTTPConnection("127.0.0.1", cfg["port"], timeout=2)
            try:
                conn.request("GET", "/health")
                reply = conn.getresponse()
                reply.read(65536)
                return (cfg["port"], cfg["api_key"], pid) if reply.status == 200 else None
            finally:
                conn.close()
        except (OSError, ValueError, KeyError, http.client.HTTPException):
            return None

    def status(self, mid):
        spec = self.spec(mid)
        profile, cfg = self._profile(mid)
        unit = unit_state(profile["unit"])
        connection = self._connection(mid, profile, cfg, unit)
        state = "loaded" if connection else "loading" if unit.get("ActiveState") in ("activating", "active") else "unloaded"
        if unit.get("ActiveState") == "failed":
            state = "failed"
        return {**spec, "state": state, "managed": True, "ownership": "dulche-managed-service",
                "pid": connection[2] if connection else None,
                "settings": self.validate_settings(mid, {}),
                "active_adapter": profile.get("fixed_adapter") if connection else None,
                "error": "The guarded service stopped: " + unit.get("Result", "unknown") if state == "failed" else None}

    def validate_settings(self, mid, settings=None):
        spec = self.spec(mid)
        defaults = {"context": spec["context"], "threads": 4, "gpu": spec["gpu"],
                    "engine": "Thunder", "quantization": spec["quantization"], "profile": "verified"}
        for key, value in (settings or {}).items():
            if key == "context_length":
                key = "context"
            if key not in defaults or value != defaults[key]:
                raise EngineError("This managed profile preserves its verified model, adapter and 512k allocation")
        return defaults

    def load(self, mid, settings=None):
        self.validate_settings(mid, settings)
        profile, _ = self._profile(mid)
        if self.status(mid)["state"] == "loaded":
            return self.status(mid)
        for guard in profile["guard_units"]:
            if unit_state(guard).get("ActiveState") != "active":
                raise EngineError("Required independent guard is not active: " + guard)
        # Type=simple acknowledges the actual start before returning. --no-block
        # can still expose the previous inactive state while the job is queued.
        try:
            subprocess.run(["systemctl", "--user", "start", profile["unit"]],
                           check=True, capture_output=True, timeout=10)
        except (OSError, subprocess.SubprocessError) as start_error:
            # A timed-out client can leave an admitted systemd start job behind.
            # Stop that exact registered unit and prove its cgroup settled before
            # propagating the original failure to the controller.
            try:
                self.unload(mid)
            except Exception as cleanup_error:
                raise EngineError("Native startup failed and cleanup of " + profile["unit"]
                                  + " is unverified: " + str(cleanup_error)) from start_error
            raise
        deadline = time.monotonic() + 360
        while time.monotonic() < deadline:
            row = self.status(mid)
            if row["state"] == "loaded":
                return row
            if row["state"] not in ("loading",):
                raise EngineError("The guarded native startup failed; inspect " + profile["unit"])
            time.sleep(0.25)
        self.unload(mid)
        raise EngineError("The guarded native startup exceeded six minutes")

    def unload(self, mid):
        # Changed source pins must block new inference, never make an already
        # registered service impossible to stop. This command remains confined
        # to its private, validated unit registration.
        profile = self._registration(mid)
        before = unit_state(profile["unit"])
        subprocess.run(["systemctl", "--user", "stop", profile["unit"]],
                       check=True, capture_output=True, timeout=30)
        state = unit_state(profile["unit"])
        if int(state.get("MainPID", 0)) > 1 or state.get("ActiveState") not in ("inactive", "failed"):
            raise EngineError("The owned native unit has not settled")
        verify_released_groups(profile["unit"], before, state)
        try:
            return self.status(mid)
        except (EngineError, OSError, ValueError):
            return {"id": mid, "state": "unloaded", "managed": True,
                    "ownership": "dulche-managed-service", "unit": profile["unit"],
                    "error": "Runtime stopped; source registration needs review before loading"}

    @contextlib.contextmanager
    def _post(self, mid, route, payload, cancel_event=None, timeout=1200):
        profile, cfg = self._profile(mid)
        connection = self._connection(mid, profile, cfg, unit_state(profile["unit"]))
        if connection is None:
            raise EngineError("The registered native endpoint is not ready")
        if cancel_event is not None and cancel_event.is_set():
            raise EngineError("Generation was cancelled")
        conn = http.client.HTTPConnection("127.0.0.1", connection[0], timeout=timeout)
        finished = threading.Event()
        watcher = None
        try:
            conn.connect()
            transport = conn.sock
            def watch():
                while not finished.wait(0.05):
                    if cancel_event.is_set():
                        with contextlib.suppress(OSError):
                            transport.shutdown(socket.SHUT_RDWR)
                        return
            if cancel_event is not None:
                watcher = threading.Thread(target=watch, daemon=True)
                watcher.start()
            conn.request("POST", route, json.dumps(payload).encode(),
                         {"Content-Type": "application/json", "Authorization": "Bearer " + connection[1], "Connection": "close"})
            response = conn.getresponse()
            try:
                if not 200 <= response.status < 300:
                    detail = response.read(1024).decode(errors="replace")
                    raise EngineError(f"Native endpoint returned HTTP {response.status}: {detail}")
                yield response
            finally:
                response.close()
        finally:
            finished.set()
            conn.close()
            if watcher is not None:
                watcher.join(timeout=0.2)

    def generate(self, mid, payload, stream=False, cancel_event=None):
        return self._post(mid, "/v1/chat/completions", {**payload, "model": mid, "stream": bool(stream)}, cancel_event)

    def count_prompt(self, mid, messages, options=None):
        if not isinstance(messages, list) or not messages or any(not isinstance(m, dict)
                or m.get("role") not in ("system", "user", "assistant") or not isinstance(m.get("content"), str) for m in messages):
            raise EngineError("Native token admission requires a text message history")
        options = options or {}
        if set(options) - {"reasoning_effort", "chat_template_kwargs"}:
            raise EngineError("Unsupported native count options")
        system = "\n".join(m["content"] for m in messages if m["role"] == "system")
        body = {"model": mid, "messages": [m for m in messages if m["role"] != "system"]}
        if system:
            body["system"] = system
        if options.get("reasoning_effort") == "none":
            body["thinking"] = {"type": "disabled"}
        with self._post(mid, "/v1/messages/count_tokens", body, timeout=30) as response:
            result = json.loads(response.read(16 * 1024 * 1024))
        count = result.get("input_tokens")
        if type(count) is not int or count < 0:
            raise EngineError("The native tokenizer did not return a token count")
        return {"input_tokens": count + 32, "method": "Strata rendered chat with cross-API margin", "margin_tokens": 32}
