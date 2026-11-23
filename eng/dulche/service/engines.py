"""Loopback inference adapters and independently supervised, identity-checked model lifetimes."""

from __future__ import annotations

import contextlib
import fcntl
import hashlib
import http.client
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import sys
import threading
import time

from engine_placement import model_placements
from engine_profiles import (
    CPU,
    GPU_RTX,
    GPU_V100,
    PRIMARY,
    V100,
    EngineError,
    EngineBusy,
    atomic_json,
    dense_files,
    find_native_profile,
    load_json,
    prepare_native,
    read_signature,
)


def _command(args, timeout=10, check=True):
    result = subprocess.run(args, capture_output=True, text=True, timeout=timeout)
    if check and result.returncode:
        # Command args may carry private data. Preserve diagnostic stderr without echoing argv.
        raise EngineError(
            (
                result.stderr or result.stdout or f"Command exited {result.returncode}"
            ).strip()[:700]
        )
    return result


def _memory():
    return {
        line.split()[0].rstrip(":"): int(line.split()[1])
        for line in Path("/proc/meminfo").read_text().splitlines()
    }


def _unit(name):
    proc = _command(
        [
            "systemctl",
            "--user",
            "show",
            name,
            "-p",
            "MainPID",
            "-p",
            "ActiveState",
            "-p",
            "SubState",
            "-p",
            "ControlGroup",
            "-p",
            "MemoryCurrent",
            "-p",
            "Description",
            "-p",
            "InvocationID",
        ],
        check=False,
    )
    return dict(line.split("=", 1) for line in proc.stdout.splitlines() if "=" in line)


def _members(unit):
    group = unit.get("ControlGroup", "")
    if not group.startswith("/user.slice/"):
        return set()
    cgroup = Path("/sys/fs/cgroup" + group)
    try:
        return {
            int(pid)
            for file in cgroup.rglob("cgroup.procs")
            for pid in file.read_text().split()
        }
    except (OSError, ValueError):
        return set()


def _gpu_allocations():
    result = _command(
        [
            "nvidia-smi",
            "--query-compute-apps=pid,gpu_uuid,used_memory",
            "--format=csv,noheader,nounits",
        ],
        check=False,
    )
    records = []
    for line in result.stdout.splitlines():
        fields = [v.strip() for v in line.split(",")]
        if len(fields) == 3 and fields[0].isdigit() and fields[2].isdigit():
            records.append((int(fields[0]), fields[1], int(fields[2])))
    return records


def _listener(pid, port):
    """Prove socket ownership before sending bearer secrets or acting on a service."""
    if not pid:
        return False
    try:
        inodes = {
            row[9]
            for line in Path("/proc/net/tcp").read_text().splitlines()[1:]
            if len(row := line.split()) > 9
            and row[1] == f"0100007F:{int(port):04X}"
            and row[3] == "0A"
        }
        wanted = {f"socket:[{inode}]" for inode in inodes}
        for fd in (Path("/proc") / str(pid) / "fd").iterdir():
            try:
                if os.readlink(fd) in wanted:
                    return True
            except FileNotFoundError:
                continue
        return False
    except (OSError, ValueError):
        return False


def _payload(port, path="/health", key=None, timeout=2):
    conn = http.client.HTTPConnection("127.0.0.1", int(port), timeout=timeout)
    try:
        conn.request(
            "GET", path, headers={"Authorization": "Bearer " + key} if key else {}
        )
        reply = conn.getresponse()
        if reply.status != 200:
            return {}
        return json.loads(reply.read(2 * 1024 * 1024))
    except (OSError, ValueError, http.client.HTTPException):
        return {}
    finally:
        conn.close()


def _native_external():
    """Only accept the official Strata Python/runtime and its private native profile."""
    try:
        source, cfg = find_native_profile()
        con = _command(["ss", "-H", "-ltnp", "sport = :18500"], check=False).stdout
        import re

        pids = set(int(i) for i in re.findall(r"pid=(\d+)", con))
        for pid in pids:
            proc = Path("/proc") / str(pid)
            if proc.stat().st_uid != os.getuid() or not _listener(pid, 18500):
                continue
            args = (
                proc.joinpath("cmdline").read_bytes().rstrip(b"\0").decode().split("\0")
            )
            if "--config" not in args or "serve.server" not in args:
                continue
            candidate = Path(args[args.index("--config") + 1])
            if (
                candidate.parent.resolve() != source.parent.resolve()
                or candidate.is_symlink()
            ):
                continue
            rec = load_json(candidate)
            if rec.get("model_name") != PRIMARY or not rec.get("api_key"):
                continue
            executable = proc.joinpath("exe").resolve()
            if "Strata/control/python/bin" not in str(
                executable
            ) and "strata/components/control" not in str(executable):
                continue
            health = _payload(18500)
            if health.get("loaded") and health.get("model") == PRIMARY:
                return pid, rec
    except (EngineError, OSError, ValueError, IndexError):
        pass
    return None


def _v100_guard():
    try:
        state = load_json(Path.home() / "strata-run/thermal_guard_status.json")
        v = state["devices"]["v100"]
        reading = v["reading_full"]
        if (
            0 <= time.time() - state["ts"] < 6
            and state["halted"] is False
            and v["telemetry_ok"] is True
            and v["stopped"] is False
            and reading.get("driver_ok") is True
            and reading.get("name") == "Tesla V100-PCIE-16GB"
            and reading.get("power_limit_active") == 165
            and isinstance(reading.get("temp"), (int, float))
            and reading["temp"] < 68
            and isinstance(reading.get("temp_mem"), (int, float))
            and reading["temp_mem"] < 67
        ):
            return reading
    except (KeyError, TypeError, OSError, ValueError):
        pass
    return {}


class EngineManager:
    def __init__(self, state_dir=None):
        self.state_dir = Path(
            state_dir
            or os.environ.get("DULCHE_STATE_DIR")
            or Path(os.environ.get("XDG_STATE_HOME", Path.home() / ".local/state"))
            / "9to1/dulche"
        ).resolve()
        self.root = self.state_dir / "engines"
        self.root.mkdir(parents=True, exist_ok=True, mode=0o700)
        self.tag = hashlib.sha256(str(self.state_dir).encode()).hexdigest()[:12]
        self.registry = self.root / "registrations.json"
        self.thread_lock = threading.RLock()
        self.catalog = None

    @contextlib.contextmanager
    def _change(self, physical=False):
        with self.thread_lock, contextlib.ExitStack() as locks:
            if physical:
                runtime = (
                    Path(os.environ.get("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}"))
                    / "dulche-engine-admission"
                )
                runtime.mkdir(exist_ok=True, mode=0o700)
                shared = locks.enter_context((runtime / "resource.lock").open("a"))
                fcntl.flock(shared, fcntl.LOCK_EX)
            with (self.root / "lifecycle.lock").open("a") as f:
                fcntl.flock(f, fcntl.LOCK_EX)
                yield

    def use_catalog(self, catalog):
        """Use the broker's canonical catalogue for registry/uninstall/restart consistency."""
        self.catalog = catalog

    def _registered(self):
        if self.catalog is not None:
            return {
                r["id"]: r
                for r in self.catalog.installed()
                if r.get("format") == "gguf" and r.get("backend") == "llama.cpp"
            }
        return load_json(self.registry) if self.registry.exists() else {}

    def register_gguf(self, model_id, path, metadata=None):
        if (
            not isinstance(model_id, str)
            or not model_id
            or len(model_id) > 250
            or model_id in (CPU, PRIMARY, V100)
        ):
            raise EngineError("Invalid local model registration ID")
        real = read_signature(path)
        meta = metadata or {}
        if meta.get("adapter") or meta.get("format", "gguf").lower() != "gguf":
            raise EngineError(
                "This inference adapter requires a complete GGUF base model"
            )
        if not meta.get("backend", "llama.cpp") == "llama.cpp":
            raise EngineError(
                "The checkpoint architecture has no compatible GGUF adapter"
            )
        if self.catalog is not None:
            entry = self._registered().get(model_id)
            if not entry or Path(entry["path"]).resolve() != real:
                raise EngineError(
                    "The canonical catalogue does not own this compatible model registration"
                )
            return self.status(model_id)
        with self._change():
            entries = self._registered()
            rec = {
                "path": str(real),
                "name": str(meta.get("name", real.stem))[:170],
                "quantization": str(meta.get("quantization") or "file-defined")[:80],
                "bytes": meta.get("bytes") or real.stat().st_size,
                "context_length": meta.get("context_length"),
            }
            if model_id in entries and entries[model_id]["path"] != str(real):
                raise EngineError("A different file already owns this registration")
            entries[model_id] = rec
            atomic_json(self.registry, entries)
        return self.status(model_id)

    def _spec(self, model_id):
        _, model = dense_files()
        if model_id == PRIMARY:
            path, cfg = find_native_profile()
            args = cfg["args"]
            context = int(args[args.index("--max-context") + 1])
            return dict(
                id=PRIMARY,
                name="Qwen3.8-Flash-Next UD-IQ4_XS",
                engine="Thunder / Strata",
                gpu=GPU_RTX,
                context=context,
                slots=1,
                quantization="UD-IQ4_XS",
                source=str(path),
                path=args[args.index("--native") + 1],
                port=18500,
            )
        if model_id == V100:
            return dict(
                id=V100,
                name="Qwen3.8-27B UD-IQ4_XS · V100",
                engine="Lightning / llama.cpp RPC",
                gpu=GPU_V100,
                context=32768,
                slots=2,
                quantization="UD-IQ4_XS",
                path=str(model),
                port=18642,
            )
        if model_id == CPU:
            data = {
                "name": "Qwen3.8-27B UD-IQ4_XS · CPU",
                "quantization": "UD-IQ4_XS",
                "path": str(model),
            }
        else:
            data = self._registered().get(model_id)
            if not data:
                raise EngineError("Unknown installed model: " + str(model_id)[:170])
        return dict(
            id=model_id,
            name=data["name"],
            engine="Lightning / llama.cpp",
            gpu="CPU",
            context=min(32768, data.get("context_length") or 32768),
            slots=1,
            quantization=data["quantization"],
            bytes=data.get("bytes") or Path(data["path"]).stat().st_size,
            max_context=data.get("context_length") or 131072,
            path=data["path"],
        )

    def _name(self, model_id):
        if model_id == PRIMARY:
            return f"dulche-primary-{self.tag}.service"
        if model_id == V100:
            return "astra-v100-inference.service"
        digest = hashlib.sha256(model_id.encode()).hexdigest()[:14]
        return f"dulche-cpu-{self.tag}-{digest}.service"

    def _record(self, model_id):
        return self.root / (self._name(model_id) + ".json")

    def _owns(self, spec, unit, rec):
        pid = int(unit.get("MainPID") or 0)
        if not pid:
            return False
        try:
            args = (
                (Path("/proc") / str(pid) / "cmdline")
                .read_bytes()
                .rstrip(b"\0")
                .decode()
                .split("\0")
            )
            exe = (Path("/proc") / str(pid) / "exe").resolve()
            if spec["id"] == PRIMARY:
                return (
                    exe.name == "python3.12"
                    and "serve.server" in args
                    and "--config" in args
                    and args[args.index("--config") + 1]
                    == str(self.root / "strata-owned.json")
                )
            if spec["id"] == V100:
                return exe.name == "node" and any(
                    s.endswith("/outputs/service/lane.mjs") for s in args
                )
            if rec.get("gpu") == GPU_V100:
                script = Path(args[-2]).resolve() if len(args) >= 3 else Path("/")
                release_root = Path.home() / ".local/share/9to1/dulche-service/releases"
                trusted = script == Path(__file__).with_name(
                    "v100_gguf.py"
                ).resolve() or (
                    script.name == "v100_gguf.py"
                    and script.parent.parent == release_root
                )
                return (
                    trusted
                    and exe == Path("/usr/bin/python3").resolve()
                    and args[-1] == str(self._record(spec["id"]))
                    and rec.get("path") == spec["path"]
                )
            executable, _ = dense_files()
            return (
                exe == executable.resolve()
                and "-m" in args
                and args[args.index("-m") + 1] == spec["path"]
                and rec.get("path") == spec["path"]
                and str(self.root / (self._name(spec["id"]) + ".key")) in args
            )
        except (OSError, IndexError, ValueError):
            return False

    def _connection(self, spec, unit, rec, include_external=True):
        mid = spec["id"]
        pid = int(unit.get("MainPID") or 0)
        port = rec.get("port", spec.get("port"))
        owned = port and self._owns(spec, unit, rec)
        if owned and rec.get("gpu") == GPU_V100 and mid not in (PRIMARY, V100):
            exe, _ = dense_files()
            for worker in _members(unit):
                try:
                    if Path(
                        f"/proc/{worker}/exe"
                    ).resolve() == exe.resolve() and _listener(worker, port):
                        return port, rec["key"], "dulche-v100-rpc", worker
                except OSError:
                    continue
        if owned and _listener(pid, port):
            if mid == PRIMARY:
                return (
                    port,
                    load_json(self.root / "strata-owned.json")["api_key"],
                    "dulche",
                    pid,
                )
            if mid == V100:
                return port, None, "astra-v100-service", pid
            if mid not in (PRIMARY, V100) and rec.get("key"):
                return port, rec["key"], "dulche", pid
        if include_external and mid == PRIMARY:
            external = _native_external()
            if external:
                return 18500, external[1]["api_key"], "external-strata", external[0]
        return None

    def _observe(self, spec):
        mid = spec["id"]
        unit = _unit(self._name(mid))
        p = self._record(mid)
        rec = load_json(p) if p.exists() else {}
        connection = self._connection(spec, unit, rec)
        actual = {
            **spec,
            "state": "unloaded",
            "managed": False,
            "unit": self._name(mid),
            "capacity": spec["slots"],
        }
        if connection:
            port, key, owner, pid = connection
            health = _payload(port, key=key)
            match = health.get("status") == "ok" and (
                (mid not in (PRIMARY, V100)) or health.get("model") == mid
            )
            actual.update(
                state="loaded" if match else "loading",
                managed=owner != "external-strata",
                ownership=owner,
                pid=pid,
                base_url=f"http://127.0.0.1:{port}/v1",
            )
            if mid == V100 and match:
                actual["context"] = health["context_per_slot"]
                actual["slots"] = actual["capacity"] = health["slots"]
            if mid not in (PRIMARY, V100):
                actual["context"] = rec.get("context", 32768)
        elif unit.get("ActiveState") in ("activating", "active"):
            controlled = self._owns(spec, unit, rec)
            actual.update(
                state="loading" if controlled else "failed",
                managed=controlled,
                pid=int(unit.get("MainPID") or 0),
            )
            if not controlled:
                actual["detail"] = (
                    "Unit process identity differs from its installed engine recipe"
                )
        elif unit.get("ActiveState") == "failed":
            actual.update(
                state="failed",
                detail="Engine exited. Inspect the named user unit's journal.",
            )
        elif mid == V100 and not _v100_guard():
            actual["detail"] = (
                "V100 VM, thermal telemetry and privileged passthrough must be restored before load."
            )
        if mid not in (PRIMARY, V100):
            actual["context"] = rec.get("context", actual["context"])
        if mid not in (PRIMARY, V100):
            actual["gpu"] = rec.get("gpu", "CPU")
            actual["gpu_options"] = (
                ["CPU", GPU_V100] if mid.startswith("local-") else ["CPU"]
            )
            if actual["gpu"] == GPU_V100:
                actual["engine"] = "Lightning / llama.cpp RPC"
        actual["profile"] = rec.get("profile", "verified")
        actual["settings"] = {
            "profile": actual["profile"],
            "gpu": actual["gpu"],
            "context": actual["context"],
            "quantization": spec["quantization"],
            "engine": actual["engine"],
        }
        if mid not in (PRIMARY, V100):
            actual["settings"]["threads"] = rec.get("threads", 4)
            if actual["gpu"] == GPU_V100:
                actual["settings"]["kv_type"] = rec.get("kv_type", "q8_0")
                if rec.get("gpu_layers") is not None:
                    for k in ("gpu_layers", "vram_ceiling_mib"):
                        actual["settings"][k] = rec[k]
                reading = _v100_guard() if actual["state"] == "loaded" else {}
                if reading and rec.get("baseline_v100_mib") is not None:
                    actual["observed_model_vram_mib"] = max(
                        0, reading["mem_used"] // 2**20 - rec["baseline_v100_mib"]
                    )
                    actual["residency"] = (
                        "partial" if rec.get("gpu_layers") is not None else "gpu"
                    )

        actual.update(model_placements(spec, actual, unit, rec, connection, _payload))
        return actual, connection, unit

    def status(self, model_id):
        return self._observe(self._spec(model_id))[0]

    def models(self):
        result = []
        exe, cpu = dense_files()
        ids = [PRIMARY, V100, CPU, *self._registered()]
        for mid in ids:
            try:
                spec = self._spec(mid)
                if not Path(spec["path"]).is_file() and not self._record(mid).is_file():
                    continue
                result.append(self._observe(spec)[0])
            except (EngineError, FileNotFoundError):
                continue
        return result

    def resources(self):
        mem = _memory()
        devices = []
        try:
            raw = _command(
                [
                    "nvidia-smi",
                    "--query-gpu=name,uuid,memory.total,memory.used,temperature.gpu,utilization.gpu",
                    "--format=csv,noheader,nounits",
                ],
                timeout=3,
                check=False,
            ).stdout
            for line in raw.splitlines():
                parts = [x.strip() for x in line.split(",")]
                if len(parts) == 6 and all(x.isdigit() for x in parts[2:]):
                    name, uuid, total, used, temp, util = parts
                    devices.append(
                        dict(
                            name=name,
                            uuid=uuid,
                            total_mib=int(total),
                            used_mib=int(used),
                            free_mib=int(total) - int(used),
                            temperature_c=int(temp),
                            utilization=int(util),
                        )
                    )
        except (OSError, subprocess.TimeoutExpired):
            pass
        guest = _v100_guard()
        if guest:
            used = guest["mem_used"] // 1048576
            total = guest["mem_total"] // 1048576
            devices.append(
                dict(
                    name=guest["name"],
                    total_mib=total,
                    used_mib=used,
                    free_mib=total - used,
                    temperature_c=guest["temp"],
                    hbm_temperature_c=guest["temp_mem"],
                    utilization=guest["util"],
                )
            )
        return dict(
            host_available_mib=mem["MemAvailable"] // 1024,
            host_total_mib=mem["MemTotal"] // 1024,
            swap_free_mib=mem["SwapFree"] // 1024,
            swap_total_mib=mem["SwapTotal"] // 1024,
            gpus=devices,
            cpu_load_1m=os.getloadavg()[0],
        )

    def _settings(self, spec, settings):
        settings = dict(settings or {})
        unknown = set(settings) - {
            "context",
            "context_length",
            "gpu",
            "profile",
            "quantization",
            "threads",
            "engine",
            "kv_type",
            "gpu_layers",
            "vram_ceiling_mib",
        }
        if unknown:
            raise EngineError(
                "Unsupported model settings: " + ", ".join(sorted(unknown))
            )
        gpu = settings.get("gpu", spec["gpu"])
        allowed = (
            ("CPU", GPU_V100) if spec["id"].startswith("local-") else (spec["gpu"],)
        )
        if gpu not in allowed:
            raise EngineError("Unsupported GPU for this model profile")
        expected_engine = (
            "Lightning / llama.cpp RPC"
            if gpu == GPU_V100 and spec["id"] != V100
            else spec["engine"]
        )
        if (
            settings.get("engine", expected_engine) != expected_engine
            or settings.get("quantization", spec["quantization"])
            != spec["quantization"]
        ):
            raise EngineError(
                "Engine and quantization must match the selected model and device"
            )
        if "gpu_layers" in settings or "vram_ceiling_mib" in settings:
            layers = settings.get("gpu_layers")
            ceiling = settings.get("vram_ceiling_mib")
            if (
                not spec["id"].startswith("local-")
                or gpu != GPU_V100
                or type(layers) is not int
                or not 1 <= layers <= 1024
                or type(ceiling) is not int
                or not 512 <= ceiling <= 16384
            ):
                raise EngineError(
                    "A partial V100 profile needs gpu_layers and vram_ceiling_mib (512–16384)"
                )
        kv = settings.get("kv_type")
        if kv is not None and (
            not spec["id"].startswith("local-")
            or gpu != GPU_V100
            or kv not in ("q4_0", "q8_0", "f16")
        ):
            raise EngineError("Unsupported KV type for this device profile")
        if not isinstance(settings.get("profile", "verified"), str) or settings.get(
            "profile", "verified"
        ).casefold() not in ("verified", "experimental"):
            raise EngineError("Unknown performance profile")
        ctx = settings.get("context", settings.get("context_length", spec["context"]))
        threads = settings.get("threads", 4)
        if type(ctx) is not int or type(threads) is not int:
            raise EngineError("Context and CPU thread count must be integers")
        if spec["id"] in (PRIMARY, V100):
            if ctx != spec["context"] or "threads" in settings:
                raise EngineError(
                    "This verified device profile preserves its existing context and thread allocation"
                )
        elif not (
            min(512, spec["max_context"]) <= ctx <= spec["max_context"]
            and 1 <= threads <= min(16, os.cpu_count() or 1)
        ):
            raise EngineError(
                "Context must fit this model’s declared limit; CPU thread count must be 1–16"
            )
        return ctx, threads

    def count_prompt(self, model_id, messages, options=None):
        """Count a text-only swarm history without performing a generation."""
        if (
            not isinstance(messages, list)
            or not messages
            or not all(
                isinstance(m, dict)
                and m.get("role") in ("system", "user", "assistant")
                and isinstance(m.get("content"), str)
                for m in messages
            )
        ):
            raise EngineError("Token counting supports text message histories")
        options = dict(options or {})
        if set(options) - {"reasoning_effort", "chat_template_kwargs"}:
            raise EngineError("Unsupported count options")
        current, connection, _ = self._observe(self._spec(model_id))
        if current["state"] != "loaded" or not connection:
            raise EngineError("Tokenizer is not online")
        port, key, *_ = connection

        def post(route, body):
            conn = http.client.HTTPConnection("127.0.0.1", port, timeout=12)
            try:
                headers = {"Content-Type": "application/json"}
                if key:
                    headers["Authorization"] = "Bearer " + key
                conn.request("POST", route, json.dumps(body).encode(), headers)
                reply = conn.getresponse()
                if reply.status != 200:
                    raise EngineError("Tokenizer refused this conversation")
                data = reply.read(16 * 1024**2 + 1)
                if len(data) > 16 * 1024**2:
                    raise EngineError("Excessive tokenizer result")
                return json.loads(data)
            finally:
                conn.close()

        if model_id == PRIMARY:
            system = "\n".join(m["content"] for m in messages if m["role"] == "system")
            body = {
                "messages": [m for m in messages if m["role"] != "system"],
                "model": model_id,
            }
            if system:
                body["system"] = system
            effort = options.get("reasoning_effort")
            if effort == "none":
                body["thinking"] = {"type": "disabled"}
            elif effort:
                body["thinking"] = {
                    "type": "enabled",
                    "budget_tokens": {
                        "low": 1024,
                        "medium": 4096,
                        "high": 8192,
                        "xhigh": 16384,
                    }.get(effort, 4096),
                }
            n = post("/v1/messages/count_tokens", body).get("input_tokens")
            if type(n) is not int or n < 0:
                raise EngineError("Native tokenizer did not return a count")
            return {
                "input_tokens": n + 32,
                "method": "Strata rendered chat under count_tokens, 32-token cross-API margin",
                "margin_tokens": 32,
            }
        if model_id != V100:
            rendered = post("/apply-template", {"messages": messages, **options}).get(
                "prompt"
            )
            if not isinstance(rendered, str):
                raise EngineError("Missing rendered chat template")
            tokens = post(
                "/tokenize",
                {"content": rendered, "add_special": True, "parse_special": True},
            ).get("tokens")
            if not isinstance(tokens, list):
                raise EngineError("Missing model token IDs")
            return {
                "input_tokens": len(tokens),
                "method": "llama.cpp rendered template and tokenizer",
                "margin_tokens": 0,
            }
        n = 0
        for m in messages:
            tokens = post(
                "/tokenize",
                {"content": m["content"], "add_special": False, "parse_special": False},
            ).get("tokens")
            if not isinstance(tokens, list):
                raise EngineError("V100 tokenizer did not return token IDs")
            n += len(tokens)
        margin = 256 + 32 * len(messages)
        return {
            "input_tokens": n + margin,
            "method": "V100 model tokenizer plus conservative chat-template allowance",
            "margin_tokens": margin,
        }

    def default_settings(self, model_id):
        return self.validate_settings(model_id, {})

    def validate_settings(self, model_id, settings=None):
        spec = self._spec(model_id)
        ctx, threads = self._settings(spec, settings)
        result = {
            "context": ctx,
            "gpu": spec["gpu"],
            "engine": spec["engine"],
            "quantization": spec["quantization"],
            "profile": (settings or {}).get("profile", "verified").casefold(),
        }
        if model_id not in (PRIMARY, V100):
            result["threads"] = threads
            result["gpu"] = (settings or {}).get("gpu", spec["gpu"])
            if result["gpu"] == GPU_V100:
                result["engine"] = "Lightning / llama.cpp RPC"
                result["kv_type"] = (settings or {}).get("kv_type", "q8_0")
                if "gpu_layers" in (settings or {}):
                    result["gpu_layers"] = settings["gpu_layers"]
                    result["vram_ceiling_mib"] = settings["vram_ceiling_mib"]
        return result

    def load(self, model_id, settings=None):
        with self._change(physical=True):
            spec = self._spec(model_id)
            current, _, _ = self._observe(spec)
            # A resume without overrides restores the last admitted choices. An explicit profile switch
            # starts from that profile's protected defaults; the broker persists each named profile.
            requested = (
                dict(settings) if settings is not None else dict(current["settings"])
            )
            ctx, threads = self._settings(spec, requested)
            if current["state"] == "loaded":
                chosen = self.validate_settings(model_id, requested)
                if any(current["settings"].get(k) != v for k, v in chosen.items()):
                    raise EngineError(
                        "Unload this model before changing its active engine settings"
                    )
                return current
            if current["state"] != "loading":
                self._launch(
                    spec,
                    ctx,
                    threads,
                    requested.get("profile", "verified").lower(),
                    requested,
                )
            deadline = time.monotonic() + (300 if model_id == PRIMARY else 150)
            while time.monotonic() < deadline:
                current, _, unit = self._observe(spec)
                if current["state"] == "loaded":
                    return current
                if unit.get("ActiveState") not in ("activating", "active"):
                    raise EngineError(
                        "Engine startup failed. Inspect journalctl --user -u "
                        + self._name(model_id)
                    )
                time.sleep(0.5)
            raise EngineError(
                "Engine did not become ready within its startup deadline; inspect "
                + self._name(model_id)
            )

    def _launch(self, spec, ctx, threads, profile, requested=None):
        mid = spec["id"]
        unit = self._name(mid)
        mem = _memory()
        device = (requested or {}).get("gpu", spec["gpu"])
        if device == GPU_V100:
            active = _command(
                [
                    "systemctl",
                    "--user",
                    "list-units",
                    "--state=active,activating",
                    "--no-legend",
                    "--plain",
                    "dulche-cpu-*.service",
                ],
                check=False,
            )
            for line in active.stdout.splitlines():
                words = line.split()
                if not words:
                    continue
                name = words[0]
                if name.startswith("dulche-cpu-") and name.endswith(".service"):
                    reg = self.root / (name + ".json")
                    if reg.exists() and load_json(reg).get("gpu") == GPU_V100:
                        raise EngineBusy(
                            "Suspend the model currently using the V100 before switching"
                        )
        if mid in (PRIMARY, V100):
            atomic_json(self._record(mid), {"port": spec["port"], "profile": profile})
        if mid == V100:
            if (
                not _v100_guard()
                or _unit("astra-v100-vm.service").get("ActiveState") != "active"
            ):
                from v100_authority import SOCKET

                if not SOCKET.is_socket():
                    raise EngineError(
                        "V100 unavailable after boot: run authorize-v100-this-boot.sh locally to authorize its isolated VFIO authority."
                    )
                from v100_recover import up

                up(self.root)
            if mem["MemAvailable"] < 3 * 1024**2:
                raise EngineBusy(
                    "The existing V100 service requires at least 3 GiB available host RAM to load"
                )
            _command(["systemctl", "--user", "start", unit])
            return
        props = [
            "systemd-run",
            "--user",
            "--collect",
            "--unit=" + unit,
            "-p",
            "Type=exec",
            "-p",
            "KillMode=control-group",
            "-p",
            "Restart=no",
            "-p",
            "TimeoutStopSec=30s",
        ]
        if mid == PRIMARY:
            # This profile's measured expert arena is ~55 GiB plus vision, host and engine buffers.
            if mem["MemAvailable"] < 70 * 1024**2:
                raise EngineBusy(
                    "The saved Flash-Next load requires 70 GiB available host RAM"
                )
            active = _command(
                [
                    "systemctl",
                    "--user",
                    "list-units",
                    "--state=active,activating",
                    "--no-legend",
                    "--plain",
                    "dulche-primary-*.service",
                ],
                check=False,
            ).stdout.strip()
            if active:
                raise EngineError(
                    "A different Dulche instance already owns a primary engine or its startup"
                )
            with socket.socket() as sock:
                sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
                try:
                    sock.bind(("127.0.0.1", 18500))
                except OSError as exc:
                    raise EngineBusy(
                        "Primary port 18500 is occupied by another service"
                    ) from exc
            _, config, control, interpreter, _ = prepare_native(self.root)
            command = props + [
                "-p",
                f"WorkingDirectory={control}",
                str(interpreter),
                "-m",
                "serve.server",
                "--engine",
                "strata",
                "--config",
                str(config),
                "--host",
                "127.0.0.1",
                "--port",
                "18500",
                "--shutdown-timeout",
                "15",
            ]
        else:
            exe, _ = dense_files()
            path = read_signature(spec["path"])
            if not exe.is_file():
                raise EngineError(
                    "The installed llama.cpp CPU-capable executable was not found"
                )
            plan = {}
            baseline = None
            layers = (requested or {}).get("gpu_layers")
            ceiling = (requested or {}).get("vram_ceiling_mib")
            if device == GPU_V100:
                guest = _v100_guard()
                if not guest or _unit("astra-v100-inference.service").get(
                    "ActiveState"
                ) in ("active", "activating"):
                    raise EngineBusy(
                        "Suspend the current V100 model and verify its thermal guard before loading"
                    )
                if mem["MemAvailable"] < 3072 * 1024:
                    raise EngineBusy("V100 loading requires 3 GiB of host RAM")
                baseline = guest["mem_used"] // 2**20
                if layers:
                    from gguf_placement import layer_budget

                    plan = layer_budget(
                        path, layers, ctx, (requested or {}).get("kv_type", "q8_0")
                    )
                    if plan["estimated_vram_mib"] > ceiling:
                        raise EngineBusy(
                            f"Estimated partial footprint {plan['estimated_vram_mib']} MiB exceeds {ceiling} MiB"
                        )
                    if (
                        mem["MemAvailable"] // 1024
                        < plan["estimated_cpu_weight_mib"] + 3072
                    ):
                        raise EngineBusy(
                            "Insufficient available RAM for CPU-resident weights and serving buffers"
                        )
                need = ceiling * 2**20 if layers else spec["bytes"] + 512 * 1024**2
                if need > guest["mem_total"] - guest["mem_used"]:
                    raise EngineBusy(
                        "Insufficient V100 VRAM for the weights and minimum engine buffers"
                    )
            elif mem["MemAvailable"] * 1024 < spec["bytes"] + 3 * 1024**3:
                raise EngineBusy(
                    "Insufficient host RAM for this GGUF plus serving buffers"
                )
            with socket.socket() as sock:
                sock.bind(("127.0.0.1", 0))
                port = sock.getsockname()[1]
            key = secrets.token_hex(32)
            atomic_json(
                self._record(mid),
                dict(
                    path=str(path),
                    gpu=device,
                    alias=mid,
                    gpu_layers=layers,
                    vram_ceiling_mib=ceiling,
                    baseline_v100_mib=baseline,
                    placement_plan=plan,
                    kv_type=(requested or {}).get("kv_type", "q8_0"),
                    port=port,
                    key=key,
                    context=ctx,
                    threads=threads,
                    profile=profile,
                ),
            )
            keyfile = self.root / (unit + ".key")
            fd = os.open(
                keyfile, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o600
            )
            with os.fdopen(fd, "w") as f:
                f.write(key + "\n")
            command = props + [
                "-p",
                "Nice=8",
                "-p",
                "MemorySwapMax=0",
                "-p",
                "OOMScoreAdjust=700",
                "--setenv=CUDA_VISIBLE_DEVICES=",
                "--setenv=LD_LIBRARY_PATH="
                + os.pathsep.join(
                    [str(exe.parent), str(Path.home() / ".cache/strata-cuda-12.9/lib")]
                ),
                str(exe),
                "-m",
                str(path),
                "-ngl",
                "0",
                "-c",
                str(ctx),
                "-np",
                "1",
                "-t",
                str(threads),
                "-tb",
                str(threads),
                "-b",
                "128",
                "-ub",
                "64",
                "--poll",
                "0",
                "--cache-ram",
                "0",
                "--no-webui",
                "--jinja",
                "--host",
                "127.0.0.1",
                "--port",
                str(port),
                "--alias",
                mid,
                "--api-key-file",
                str(keyfile),
            ]
            if device == GPU_V100:
                host_weights = plan.get("estimated_cpu_weight_mib", 0)
                command = props + [
                    "-p",
                    "Nice=8",
                    "-p",
                    f"MemoryMax={host_weights + 2304}M",
                    "-p",
                    f"MemoryHigh={host_weights + 1856}M",
                    "-p",
                    "MemorySwapMax=0",
                    "-p",
                    "OOMScoreAdjust=700",
                    "-p",
                    "OOMPolicy=stop",
                    "/usr/bin/python3",
                    "-B",
                    str(Path(__file__).with_name("v100_gguf.py").resolve()),
                    str(self._record(mid)),
                ]
        _command(command)

    def unload(self, model_id):
        with self._change(physical=True):
            spec = self._spec(model_id)
            current, conn, unit = self._observe(spec)
            if current.get("ownership") == "external-strata":
                raise EngineError(
                    "Strata Desktop owns this engine. Its own unload control is required to release its GPU memory."
                )
            active = unit.get("ActiveState") in ("active", "activating", "deactivating")
            if not active:
                return {
                    **current,
                    "state": "unloaded",
                    "release_verified": False,
                    "released_vram_mib": 0,
                    "release_detail": "Engine was already stopped; no allocation was reclaimed by this request.",
                }
            pids = _members(unit)
            recipe = self._record(model_id)
            if not pids or not self._owns(
                spec, unit, load_json(recipe) if recipe.exists() else {}
            ):
                raise EngineError("Cannot prove engine process ownership for unload")
            before = _gpu_allocations() if model_id == PRIMARY else []
            owned = sum(mib for pid, _, mib in before if pid in pids)
            guest_before = _v100_guard() if current.get("gpu") == GPU_V100 else {}
            _command(["systemctl", "--user", "stop", self._name(model_id)], timeout=45)
            if _unit(self._name(model_id)).get("ActiveState") in (
                "active",
                "activating",
                "deactivating",
            ):
                raise EngineError(
                    "The owned unit remains active; release is unverified"
                )
            remaining = (
                sum(mib for pid, _, mib in _gpu_allocations() if pid in pids)
                if model_id == PRIMARY
                else 0
            )
            if current.get("gpu") == GPU_V100:
                released = 0
                deadline = time.monotonic() + 12
                while time.monotonic() < deadline and guest_before:
                    now = _v100_guard()
                    if now:
                        released = max(
                            0, (guest_before["mem_used"] - now["mem_used"]) // 1048576
                        )
                        if released >= 1024:
                            break
                    time.sleep(0.4)
                verified = released >= 1024
            else:
                released = max(0, owned - remaining)
                verified = owned > 0 and not remaining
            return {
                **self.status(model_id),
                "release_verified": verified,
                "released_vram_mib": released,
                "release_detail": (
                    "Retired the owned GPU allocations."
                    if verified
                    else "Owned engine exited; no attributable GPU allocation change was verified."
                ),
                "released_process_ids": sorted(pids),
            }

    @contextlib.contextmanager
    def generate(self, model_id, payload, stream=False, cancel_event=None):
        spec = self._spec(model_id)
        status, conninfo, _ = self._observe(spec)
        if status["state"] != "loaded" or not conninfo:
            raise EngineError(
                "Selected engine is unavailable; load or resume it before generating"
            )
        port, key, _, _ = conninfo
        data = {**payload, "model": model_id, "stream": bool(stream)}
        if cancel_event is not None and cancel_event.is_set():
            raise EngineError("Generation was cancelled")
        conn = http.client.HTTPConnection("127.0.0.1", port, timeout=1200)
        finished = threading.Event()
        watcher = None
        try:
            conn.connect()
            transport = conn.sock

            def cancel_wait():
                while not finished.wait(0.05):
                    if cancel_event.is_set():
                        with contextlib.suppress(OSError):
                            transport.shutdown(socket.SHUT_RDWR)
                        return

            if cancel_event is not None:
                watcher = threading.Thread(target=cancel_wait, daemon=True)
                watcher.start()
            headers = {"Content-Type": "application/json", "Connection": "close"}
            if key:
                headers["Authorization"] = "Bearer " + key
            conn.request(
                "POST", "/v1/chat/completions", json.dumps(data).encode(), headers
            )
            response = conn.getresponse()
            try:
                if not 200 <= response.status < 300:
                    error = response.read(1024).decode(errors="replace")
                    raise EngineError(
                        f"Engine returned HTTP {response.status}: {error}"
                    )
                yield response
            finally:
                response.close()
        finally:
            finished.set()
            conn.close()
            if watcher:
                watcher.join(timeout=0.2)
