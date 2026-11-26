"""One explicitly registered CUDA/RPC model, using Dulche's existing lifecycle.

The manifest selects a reviewed local build; it does not install binaries,
change the native Strata profile, or claim a hard VRAM reservation.
"""
from __future__ import annotations

from dataclasses import asdict
import hashlib
import math
import os
from pathlib import Path
import re
import secrets
import socket
import time

from engine_profiles import (EngineBusy, EngineError, GPU_POOL, INKLING_POOL,
                             atomic_json, read_signature)

DEVICES = ("rtx5080", "v100")
ENGINE = "llama.cpp / CUDA + RPC"
MANIFEST = "inkling-pool-source.json"
ACTIVE = ("active", "activating", "deactivating", "reloading")
HEX64 = re.compile(r"[0-9a-f]{64}\Z")


def private_json(path):
    from pooled_runtime import read_private_json
    try:
        return read_private_json(path)
    except (OSError, ValueError, RuntimeError) as exc:
        raise EngineError("Pooled engine metadata is unavailable or not private: " + str(exc)) from exc


def source(root):
    path = Path(root) / MANIFEST
    cfg = private_json(path)
    if (cfg.get("schema") != "dulche-pool-source/v1"
            or cfg.get("alias") != INKLING_POOL
            or cfg.get("physical_devices") != list(DEVICES)
            or not isinstance(cfg.get("path"), str)):
        raise EngineError("Pooled Inkling requires its exact dual-device manifest")
    model = read_signature(cfg["path"])
    if model.name != "Inkling-2-Mini-UD-Q4_K_XL.gguf":
        raise EngineError("Pooled Inkling owns the registered UD-Q4_K_XL artifact")
    for key in ("backend_path", "guard_config"):
        item = cfg.get(key)
        if not isinstance(item, str) or not Path(item).is_absolute() or not Path(item).is_file():
            raise EngineError("Missing absolute pooled artifact: " + key)
    if not isinstance(cfg.get("backend_sha256"), str) or not HEX64.fullmatch(cfg["backend_sha256"]):
        raise EngineError("Pooled backend needs an exact build digest")
    ctx, threads = cfg.get("context", 32768), cfg.get("threads", 4)
    from pooled_backend import BackendError, parallel_configuration
    try:
        parallel_configuration(ctx, cfg.get("parallel", 1), cfg.get("kv_type", "f16"), cfg.get("cpu_moe_layers", 0))
    except BackendError as exc:
        raise EngineError(str(exc)) from exc
    if type(threads) is not int or not 1 <= threads <= 16:
        raise EngineError("Pooled Inkling requires 1–16 CPU threads")
    cfg.update(path=str(model), context=ctx, threads=threads)
    return path, cfg


def specification(root, record=None):
    try:
        path, cfg = source(root)
    except EngineError:
        # A missing/replaced source must not make an already-owned lifetime
        # disappear from the broker or prevent an identity-checked unload.
        if record is None or not Path(record).exists():
            raise
        cfg = private_json(record)
        if (cfg.get("schema") != "dulche-pool-runtime/v1" or cfg.get("alias") != INKLING_POOL
                or cfg.get("physical_devices") != list(DEVICES)
                or not isinstance(cfg.get("path"), str)):
            raise EngineError("The saved pooled lifecycle recipe is invalid")
        path = Path(root) / MANIFEST
    try:
        size = Path(cfg["path"]).stat().st_size
    except OSError:
        size = (cfg.get("model_identity") or {}).get("bytes", 0)
    return dict(id=INKLING_POOL, name="Inkling-2-Mini UD-Q4_K_XL · RTX 5080 + V100",
                engine=ENGINE, gpu=GPU_POOL, physical_devices=list(DEVICES),
                context=cfg["context"], max_context=1048576 // cfg.get("parallel", 1), slots=cfg.get("parallel", 1),
                quantization="UD-Q4_K_XL", source=str(path), path=cfg["path"],
                bytes=size, threads=cfg["threads"])


def status_path(record):
    return Path(record).with_suffix(".pool-status.json")


def owns(manager, unit, rec, args, executable):
    """Prove the supervisor recipe and installed script before any stop/key use."""
    record = manager._record(INKLING_POOL)
    script = Path(rec.get("runtime_path", "/missing"))
    release_root = Path.home() / ".local/share/9to1/dulche-service/releases"
    trusted = script == Path(__file__).with_name("pooled_runtime.py").resolve() or (
        script.name == "pooled_runtime.py" and script.parent.parent == release_root)
    tail = args[1:]
    if tail[:1] == ["-B"]:
        tail = tail[1:]
    return bool(trusted and executable == Path("/usr/bin/python3").resolve()
                and tail == [str(script), str(record)]
                and rec.get("schema") == "dulche-pool-runtime/v1"
                and rec.get("alias") == INKLING_POOL
                and rec.get("physical_devices") == list(DEVICES)
                and re.fullmatch(r"[0-9a-f]{32}", rec.get("launch_id", ""))
                and isinstance(rec.get("runtime_sha256"), str)
                and hashlib.sha256(script.read_bytes()).hexdigest() == rec["runtime_sha256"])


def snapshot(manager, unit, rec, *, terminal=False):
    """Reject old receipts after a relaunch or a changed systemd invocation."""
    try:
        value = private_json(status_path(manager._record(INKLING_POOL)))
        if (value.get("schema") != "dulche-pool-status/v1"
                or value.get("launch_id") != rec.get("launch_id")
                or not re.fullmatch(r"[0-9a-f]{32}", rec.get("launch_id", ""))):
            return {}
        if terminal:
            return value
        pid = int(unit.get("MainPID") or 0)
        started = Path(f"/proc/{pid}/stat").read_text().rsplit(")", 1)[1].split()[19]
        now, observed = time.time(), value.get("observed_at")
        if (pid <= 1 or value.get("wrapper_pid") != pid
                or value.get("wrapper_start_ticks") != started
                or not unit.get("InvocationID")
                or value.get("systemd_invocation") != unit["InvocationID"]
                or type(observed) not in (int, float) or not math.isfinite(observed)
                or not 0 <= now - observed <= 8):
            return {}
        return value
    except (OSError, ValueError, KeyError, TypeError, IndexError, EngineError):
        return {}


def connection(manager, unit, rec, members, listener):
    value = snapshot(manager, unit, rec)
    pid = value.get("backend_pid")
    if type(pid) is not int or pid <= 1 or pid not in members(unit):
        return None
    if Path(f"/proc/{pid}/exe").resolve() != Path(rec["backend_path"]).resolve():
        return None
    if not listener(pid, rec["port"]):
        return None
    return rec["port"], rec["key"], "dulche-pool", pid


def observe(actual, manager, unit, rec):
    value = snapshot(manager, unit, rec)
    actual.update(physical_devices=list(DEVICES), gpu=GPU_POOL, gpu_options=[GPU_POOL], engine=ENGINE)
    if actual["state"] == "loaded" and not (
        value.get("phase") == "running" and value.get("dual_allocations_observed") is True
    ):
        actual.update(state="loading", detail="Waiting for fresh attributable allocations on both GPUs")
    if value:
        actual["pool_runtime"] = {key: value[key] for key in
                                  ("phase", "dual_allocations_observed", "budget", "observed_at",
                                   "hard_reservation_supported", "display_reserved_bytes") if key in value}
    return value


def guard_coverage(config_path):
    """Admission needs the running guard to own both sides, including forced exit."""
    from pooled_runtime import capture_guard, guard_policies, _owned_json
    pin = capture_guard(config_path)
    guard_policies(pin)
    cfg = _owned_json(pin.config_path)
    rows = {r["id"]: r for r in cfg["devices"]}
    helper = Path(__file__).with_name("native_guard.py").resolve()
    chain = Path(__file__).with_name("pooled_guard.py").resolve()
    for role, script in (("rtx5080", helper), ("v100", chain)):
        for field, action in (("stop_cmd", "stop"), ("kill_cmd", "kill"), ("verify_cmd", "verify")):
            if rows[role].get(field) != ["/usr/bin/python3", str(script), action]:
                raise EngineBusy("Independent thermal guard has not adopted the pooled " + role + " stop path")
    return pin


def launch(manager, spec, context, threads, profile, props, command, unit_state):
    """Called under the shared physical resource lock; worker holds device leases."""
    from pooled_runtime import (RTX_UUID, nvml_snapshot, capture_display, host_compute_allocations,
                                runtime_recipe, start_pooled_rpc, cleanup_pending_rpc)
    _, cfg = source(manager.root)
    for name in ("astra-v100-inference.service",):
        if unit_state(name).get("ActiveState") in ACTIVE:
            raise EngineBusy("Suspend the existing V100 engine before pooled loading")
    active = command(["systemctl", "--user", "list-units", "--state=active,activating,deactivating",
                      "--no-legend", "--plain", "dulche-primary-*.service", "dulche-inkling-*.service",
                      "dulche-pool-*.service", "dulche-training-*-worker.service"]).stdout
    if active.strip():
        raise EngineBusy("An existing inference or training unit owns a pooled GPU")
    if host_compute_allocations(nvml_snapshot(RTX_UUID, 3)["allocations"], display_pin=capture_display()):
        raise EngineBusy("An existing RTX compute process must release the device before pooled loading")
    if unit_state("astra-v100-vm.service").get("ActiveState") != "active":
        from v100_recover import up
        up(manager.root, start_worker=False)
    guard_coverage(cfg["guard_config"])
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        port = sock.getsockname()[1]
    record = manager._record(INKLING_POOL)
    script = Path(__file__).with_name("pooled_runtime.py").resolve()
    key = secrets.token_hex(32)
    from pooled_backend import parallel_configuration
    parallel_configuration(context, cfg.get("parallel", 1), cfg.get("kv_type", "f16"), cfg.get("cpu_moe_layers", 0))
    cfg.update(schema="dulche-pool-runtime/v1", alias=INKLING_POOL, physical_devices=list(DEVICES),
               context=context, threads=threads, port=port, profile=profile, gpu=GPU_POOL,
               key=key, launch_id=secrets.token_hex(16), runtime_path=str(script),
               runtime_sha256=hashlib.sha256(script.read_bytes()).hexdigest(), rpc_pending=True)
    fd = os.open(record.with_suffix(".key"), os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, "w") as stream:
        stream.write(key + "\n")
        stream.flush()
        os.fsync(stream.fileno())
    atomic_json(record, cfg)
    runtime_recipe(record)  # Validate all pinned artifacts before starting the idle RPC.
    try:
        cfg["rpc_process"] = asdict(start_pooled_rpc(cfg))
        atomic_json(record, cfg)
        command(props + ["-p", "BindsTo=dulche-v100-thermal.service", "-p", "After=dulche-v100-thermal.service",
                         "-p", "MemorySwapMax=0", "-p", "OOMPolicy=stop",
                         "/usr/bin/python3", "-B", str(script), str(record)])
    except BaseException as exc:
        # systemd-run can time out after creating a unit. Stop only its proven
        # wrapper before reconciling the tagged startup RPC. The runtime owns
        # normal residency fences after handoff; cleanup must never erase them.
        started = unit_state(manager._name(INKLING_POOL))
        if started.get("ActiveState") in ACTIVE:
            if not manager._owns({"id": INKLING_POOL}, started, cfg):
                raise EngineError("Pooled startup failed with an unproven unit; retain its physical fences") from exc
            command(["systemctl", "--user", "stop", manager._name(INKLING_POOL)], timeout=45)
        try:
            cleanup_pending_rpc(cfg)
        except Exception as cleanup:
            raise EngineError("Pooled startup failed and its RPC release remains unverified: " + str(cleanup)) from exc
        cfg["rpc_pending"] = False
        atomic_json(record, cfg)
        raise


def release(manager, current, unit, rec, command, unit_state, members):
    """A stop receipt is usable only when the owning unit is gone and both fences retire."""
    from pooled_runtime import assert_devices_available
    active = unit.get("ActiveState") in ACTIVE
    pids = members(unit)
    if not active and not rec:
        return {**current, "state": "unloaded", "release_verified": False,
                "released_vram_mib": 0, "release_detail": "The pooled model has not been launched."}
    if active:
        if not pids or not manager._owns({"id": INKLING_POOL}, unit, rec):
            raise EngineError("Cannot prove pooled process ownership for unload")
        command(["systemctl", "--user", "stop", manager._name(INKLING_POOL)], timeout=45)
    after = unit_state(manager._name(INKLING_POOL))
    if after.get("ActiveState") in ACTIVE or members(after):
        raise EngineError("The pooled unit still owns processes; release is unverified")
    receipt = snapshot(manager, unit, rec, terminal=True)
    verified = receipt.get("phase") == "stopped" and receipt.get("stop_verified") is True
    needs_reconciliation = not verified
    if verified:
        try:
            assert_devices_available(DEVICES)
        except RuntimeError:
            needs_reconciliation = True
    if needs_reconciliation:
        from pooled_reconcile import reconcile_release
        try:
            receipt = reconcile_release(manager._record(INKLING_POOL), manager._name(INKLING_POOL))
            verified = receipt.get("phase") == "stopped" and receipt.get("stop_verified") is True
            if not verified:
                raise EngineError("Reconciliation did not verify the pooled release")
            assert_devices_available(DEVICES)
        except Exception as exc:
            raise EngineError("Pooled release is unverified and remains quarantined; physical fences and original failure evidence are retained: " + str(exc)) from exc
    released = 0
    per_device = {}
    for device in (receipt.get("budget") or {}).get("devices", []):
        amount = device.get("owned_bytes")
        if type(amount) is int and amount >= 0:
            released += amount
            if device.get("role") in DEVICES:
                per_device[device["role"]] = (amount + 2**20 - 1) // 2**20
    inactive = {key: value for key, value in current.items()
                if key not in ("pid", "base_url", "ownership", "pool_runtime", "placement_observation")}
    inactive.update(placement_observation=None, placement=current.get("placement_requested"))
    return {**inactive, "state": "unloaded" if verified else "failed", "managed": True,
            "release_verified": verified, "released_vram_mib": released // 2**20 if verified and active else 0,
            "released_devices_mib": per_device if verified and active else {},
            "released_process_ids": sorted(pids),
            "release_detail": ("Verified host and guest allocations retired." if verified else
                               "Pooled release is unverified; physical admission remains closed."),
            "pool_stop": {k: receipt[k] for k in ("phase", "stop_verified", "trigger", "errors") if k in receipt}}
