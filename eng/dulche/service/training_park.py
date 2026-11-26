"""Cooperatively release native model memory at an idle boundary, retaining its exact API owner."""

from __future__ import annotations

import hashlib
import http.client
import json
import os
import sqlite3
import tempfile
import time
from pathlib import Path

from engine_profiles import PRIMARY
from engines import _command, _listener, _members, _memory, _payload, _unit
from state import Fault

RTX = "GPU-6cc11d2c-f0b1-4c95-d8f6-bfc3832655c2"


def require(ok, message):
    if not ok:
        raise Fault(409, message)


def _freeze_state(unit):
    return _command(
        ["systemctl", "--user", "show", unit, "-p", "FreezerState", "--value"]
    ).stdout.strip()


def _native(manager, model, prior=None):
    require(
        model == PRIMARY,
        "This adapter has no atomic idle-only training handoff; leave this model running",
    )
    spec = manager._spec(model)
    unit_name = manager._name(model)
    unit = _unit(unit_name)
    pid = int(unit.get("MainPID") or 0)
    require(
        pid and manager._owns(spec, unit, {}) and _listener(pid, 18500),
        "The original Dulche-owned native control process is unavailable",
    )
    invocation = unit.get("InvocationID")
    require(
        isinstance(invocation, str) and len(invocation) == 32,
        "Engine invocation identity is unavailable",
    )
    cfg = manager.root / "strata-owned.json"
    profile_digest = hashlib.sha256(cfg.read_bytes()).hexdigest()
    if prior:
        require(
            prior["invocation_id"] == invocation
            and prior["server_pid"] == pid
            and prior["profile_digest"] == profile_digest,
            "The original native service or its saved profile changed; operator review required",
        )
    return unit_name, unit, pid, json.loads(cfg.read_text())["api_key"], profile_digest


def _request(key, model, action):
    conn = http.client.HTTPConnection("127.0.0.1", 18500, timeout=600)
    try:
        conn.request(
            "POST",
            "/v1/" + action + "?wait=0",
            json.dumps({"model": model}).encode(),
            {
                "Authorization": "Bearer " + key,
                "Content-Type": "application/json",
                "Connection": "close",
            },
        )
        response = conn.getresponse()
        if response.status == 409 or response.status == 429:
            raise Fault(409, "Native engine is busy; its live request was preserved")
        require(
            response.status == 200, "Native engine refused the cooperative " + action
        )
        value = json.loads(response.read(2 * 1024 * 1024))
        require(
            value.get("model") == model,
            "Native operation returned a different model identity",
        )
        return value
    finally:
        conn.close()


def _connected(port):
    """TCP connections already admitted or queued at the retained loopback listener."""
    count = 0
    for line in Path("/proc/net/tcp").read_text().splitlines()[1:]:
        columns = line.split()
        if columns[1] == f"0100007F:{port:04X}" and columns[3] in ("01", "03", "08"):
            count += 1
    return count


def _allocations():
    result = _command(
        [
            "nvidia-smi",
            "--query-compute-apps=pid,gpu_uuid,used_memory",
            "--format=csv,noheader,nounits",
        ]
    )
    out = {}
    for line in result.stdout.splitlines():
        fields = [x.strip() for x in line.split(",")]
        if not line.strip():
            continue
        require(
            len(fields) == 3 and fields[0].isdigit() and fields[2].isdigit(),
            "GPU process memory is unobservable",
        )
        if fields[1] == RTX:
            out[int(fields[0])] = int(fields[2])
    require(
        RTX
        in _command(
            ["nvidia-smi", "--query-gpu=uuid", "--format=csv,noheader"]
        ).stdout.splitlines(),
        "Target GPU identity was lost",
    )
    return out


def _attributed(pids):
    return sum(mib for pid, mib in _allocations().items() if pid in pids)


def inspect_native(manager, model):
    unit_name, unit, pid, key, profile_digest = _native(manager, model)
    require(_freeze_state(unit_name) == "running", "Engine shell is already parked")
    current = _payload(18500, "/v1/status", key=key)
    require(
        current.get("loaded") is True
        and current.get("model") == model
        and current.get("activity", {}).get("in_flight") == 0,
        "Native model is unloaded, busy or unobservable",
    )
    admission = _payload(18500, "/v1/telemetry", key=key).get("work", {})
    require(
        admission.get("active") == 0 and admission.get("queued") == 0,
        "Native inference is active or queued",
    )
    pids = _members(unit)
    all_allocations = _allocations()
    before = sum(n for p, n in all_allocations.items() if p in pids)
    require(before > 0, "Original GPU allocation cannot be attributed to this model")
    return {
        "model": model,
        "unit": unit_name,
        "server_pid": pid,
        "invocation_id": unit["InvocationID"],
        "profile_digest": profile_digest,
        "resident_pids": sorted(pids),
        "gpu_uuid": RTX,
        "before_mib": before,
        "other_gpu_pids": sorted(p for p in all_allocations if p not in pids),
        "source_digest": hashlib.sha256(
            Path(manager._spec(model)["source"]).read_bytes()
        ).hexdigest(),
    }


def park_native(manager, prior):
    with manager._change(physical=True):
        mid = prior["model"]
        name, unit, pid, key, _ = _native(manager, mid, prior)
        if _freeze_state(name) != "frozen":
            current = _payload(18500, "/v1/status", key=key)
            require(
                current.get("model") == mid
                and current.get("activity", {}).get("in_flight") == 0,
                "A native request is running or queued; release was deferred",
            )
            if current.get("loaded") is True:
                result = _request(key, mid, "unload")
                require(
                    result.get("loaded") is False,
                    "Native model did not confirm release",
                )
            else:
                require(
                    current.get("loaded") is False,
                    "Cannot establish native model state",
                )
            # No generation is stopped. Freeze only the emptied API wrapper so an unbrokered
            # direct request cannot automatically load another copy during the training window.
            require(
                _members(unit) == {pid} and not _connected(18500),
                "Direct work arrived during release; the native owner remains available",
            )
            _command(["systemctl", "--user", "freeze", name])
        try:
            _, fresh, _, _, _ = _native(manager, mid, prior)
            require(
                _freeze_state(name) == "frozen"
                and _members(fresh) == {pid}
                and not _connected(18500),
                "Native admission changed across idle parking",
            )
            remaining = _attributed(set(prior["resident_pids"]) | _members(fresh))
            require(
                remaining == 0, "Model GPU allocations remain; training is not admitted"
            )
        except BaseException:
            _command(["systemctl", "--user", "thaw", name], check=False)
            raise
        return {
            "release_verified": True,
            "released_vram_mib": prior["before_mib"],
            "released_process_ids": [n for n in prior["resident_pids"] if n != pid],
            "release_detail": "Native FIFO confirmed idle unload; original wrapper retained without live engine children.",
            "native_server_pid": pid,
            "remaining_owned_vram_mib": remaining,
        }


def parked_native(manager, prior):
    try:
        name, unit, pid, _, _ = _native(manager, prior["model"], prior)
        return (
            _freeze_state(name) == "frozen"
            and _members(unit) == {pid}
            and _attributed(_members(unit)) == 0
        )
    except (Fault, OSError, ValueError, RuntimeError):
        return False


def _restore_capacity(manager, prior):
    require(
        _memory()["MemAvailable"] >= 70 * 1024**2,
        "Insufficient host RAM to restore the verified native profile",
    )
    gpu = _command(
        [
            "nvidia-smi",
            "--id=" + RTX,
            "--query-gpu=memory.free",
            "--format=csv,noheader,nounits",
        ]
    ).stdout.strip()
    require(
        gpu.isdigit() and int(gpu) >= prior["before_mib"],
        "Insufficient free RTX memory for the previous model allocation",
    )
    try:
        policy = json.loads((manager.root / "v100-guard.json").read_text())
        limit = next(d["stop_c"] for d in policy["devices"] if d["id"] == "rtx5080")
        status = json.loads(Path(policy["status_file"]).expanduser().read_text())
        device = status["devices"]["rtx5080"]
        reading = device["reading_full"]
        now = time.time()
        safe = (
            0 <= now - status["ts"] < 6
            and 0 <= now - device["last_telemetry_ts"] < 6
            and device["telemetry_ok"] is True
            and device["stopped"] is False
            and reading["driver_ok"] is True
            and reading["name"] == "NVIDIA GeForce RTX 5080"
            and 0 < reading["temp"] < limit <= 80
        )
    except (KeyError, TypeError, ValueError, StopIteration, OSError):
        safe = False
    require(
        safe, "Live RTX thermal protection is unavailable or prevents model restoration"
    )


def restore_native(manager, prior, load=True):
    mid = prior["model"]
    if _unit(manager._name(mid)).get("ActiveState") not in (
        "active",
        "activating",
        "deactivating",
    ):
        if not load:
            return {"restored": False, "operator_suspended": True}
        require(
            hashlib.sha256(Path(manager._spec(mid)["source"]).read_bytes()).hexdigest()
            == prior["source_digest"],
            "The saved native source changed; cannot recreate a prior profile after restart",
        )
        require(
            not (set(_allocations()) - set(prior["other_gpu_pids"])),
            "A new GPU workload holds the training device",
        )
        _restore_capacity(manager, prior)
        restored = manager.load(mid)
        require(
            restored.get("state") == "loaded"
            and hashlib.sha256(
                (manager.root / "strata-owned.json").read_bytes()
            ).hexdigest()
            == prior["profile_digest"],
            "Recreated model profile does not match the saved configuration",
        )
        return {
            "restored": True,
            "server_pid": restored.get("pid"),
            "profile_digest": prior["profile_digest"],
            "recreated": True,
        }
    with manager._change(physical=True):
        mid = prior["model"]
        if load:
            manager._admit_devices(manager._spec(mid))
        name, unit, pid, key, _ = _native(manager, mid, prior)
        require(
            not (set(_allocations()) - set(prior["other_gpu_pids"]) - _members(unit)),
            "A new GPU workload holds the training device; do not restore inference yet",
        )
        # Enforce the same saved Flash-Next host requirement used by normal cold starts.
        if load and not (
            _freeze_state(name) == "running" and _attributed(_members(unit)) > 0
        ):
            _restore_capacity(manager, prior)
        if not load:
            if _freeze_state(name) == "frozen" and _members(unit) == {pid}:
                # A deliberate operator unload or Suspend overrides restoration. No generation
                # or engine child is present; retiring this idle shell prevents its auto-load.
                _command(["systemctl", "--user", "stop", name], timeout=40)
            else:
                require(
                    _payload(18500, "/v1/status", key=key).get("loaded") is False,
                    "The model is live; normal operator suspension is required",
                )
            return {
                "restored": False,
                "operator_suspended": True,
                "native_server_pid": pid,
            }
        if _freeze_state(name) == "frozen":
            _command(["systemctl", "--user", "thaw", name])
        current = _payload(18500, "/v1/status", key=key)
        if current.get("loaded") is not True:
            require(
                _request(key, mid, "load").get("loaded") is True,
                "Native reload is not verified",
            )
        current = _payload(18500, "/v1/status", key=key)
        require(
            current.get("loaded") is True
            and current.get("model") == mid
            and _attributed(_members(_unit(name))) > 0,
            "Restored native model identity or owned allocation is unavailable",
        )
        return {
            "restored": True,
            "server_pid": pid,
            "profile_digest": prior["profile_digest"],
        }


def checkpoint_context(source, destination):
    """Refresh an operator-selected existing SQLite conversation checkpoint; no inference KV claim."""
    source, destination = Path(source), Path(destination)
    require(
        source.is_absolute() and destination.is_absolute() and source != destination,
        "Invalid checkpoint paths",
    )
    for path in (source, destination):
        require(
            path.is_file()
            and not path.is_symlink()
            and path.stat().st_uid == os.getuid()
            and not path.stat().st_mode & 0o077,
            "Conversation checkpoints must be existing private, user-owned SQLite files",
        )
    require(
        source.resolve() != destination.resolve()
        and not os.path.samefile(source, destination),
        "Source and checkpoint must differ",
    )
    fd, tmp = tempfile.mkstemp(prefix=".context-", dir=destination.parent)
    os.close(fd)
    try:
        from contextlib import closing

        with (
            closing(
                sqlite3.connect(
                    source.resolve().as_uri() + "?mode=ro", uri=True, timeout=5
                )
            ) as a,
            closing(sqlite3.connect(tmp)) as b,
        ):
            a.backup(b, pages=128, sleep=0.02)
            b.commit()
            require(
                b.execute("PRAGMA integrity_check").fetchone() == ("ok",),
                "SQLite context integrity check failed",
            )
        with open(tmp, "rb") as f:
            digest = hashlib.file_digest(f, "sha256").hexdigest()
            os.fsync(f.fileno())
        os.replace(tmp, destination)
        directory = os.open(destination.parent, os.O_RDONLY | os.O_DIRECTORY)
        try:
            os.fsync(directory)
        finally:
            os.close(directory)
        return {
            "path": str(destination),
            "sha256": digest,
            "bytes": destination.stat().st_size,
            "created_epoch": time.time(),
            "integrity_check": "ok",
            "kind": "persisted SQLite conversation state",
        }
    finally:
        Path(tmp).unlink(missing_ok=True)


def direct_waiting(prior):
    return _connected(18500) > 0 if prior["model"] == PRIMARY else False


def checkpoint_config(state):
    p = Path(state) / "mentor-context-checkpoint.json"
    require(
        p.is_file()
        and not p.is_symlink()
        and p.stat().st_uid == os.getuid()
        and not p.stat().st_mode & 0o077,
        "Configure a private mentor-context checkpoint before parking Flash",
    )
    return json.loads(p.read_text())


def owner_turn_active(state):
    config = checkpoint_config(state)
    name = config.get("owner_unit")
    if not name:
        return False
    require(
        name == "dulche-independent-qwen-owner.service",
        "Unrecognized training-owner unit",
    )
    unit = _unit(name)
    if unit.get("ActiveState") not in ("active", "activating", "deactivating"):
        return False
    main = int(unit.get("MainPID") or 0)
    require(main > 0, "Independent owner process identity is unsettled")
    for pid in _members(unit) - {main}:
        try:
            state = (
                (Path("/proc") / str(pid) / "stat")
                .read_text()
                .rsplit(") ", 1)[1]
                .split()[0]
            )
            if state not in ("Z", "X"):
                return True
        except (FileNotFoundError, ProcessLookupError):
            continue
    return False


def refresh_mentor_checkpoint(state):
    c = checkpoint_config(state)
    require(not owner_turn_active(state), "Independent Qwen CLI turn has not drained")
    result = checkpoint_context(c["source"], c["destination"])
    sidecar = Path(c["destination"]).with_suffix(".json")
    if sidecar.is_file():
        from datetime import UTC, datetime
        from engine_profiles import atomic_json

        require(
            not sidecar.is_symlink()
            and sidecar.stat().st_uid == os.getuid()
            and not sidecar.stat().st_mode & 0o077,
            "Context receipt must be a private operator-owned file",
        )
        metadata = json.loads(sidecar.read_text())
        sid = metadata.get("session_id")
        if sid:
            db = sqlite3.connect(
                Path(c["destination"]).resolve().as_uri() + "?mode=ro", uri=True
            )
            try:
                result["persisted_messages"] = db.execute(
                    "SELECT count(*) FROM session_message WHERE session_id=?", (sid,)
                ).fetchone()[0]
            finally:
                db.close()
        metadata.update(
            created_at=datetime.fromtimestamp(result["created_epoch"], UTC).isoformat(),
            database_sha256=result["sha256"],
            integrity_check="ok",
            kind=result["kind"],
            persisted_messages=result.get("persisted_messages"),
        )
        atomic_json(sidecar, metadata)
    return result


def require_no_training(state):
    # The outer scheduler independently verifies guest/device release. Also refuse to
    # overwrite a running Dulche-supervised worker, supervisor or unsettled checkpoint.
    from training_jobs import unit_props

    for f in (Path(state) / "training/jobs").glob("*/status.json"):
        job = json.loads(f.read_text())
        for key in ("worker_unit", "supervisor_unit"):
            name = job.get(key)
            if name and unit_props(name).get("ActiveState") in (
                "activating",
                "active",
                "deactivating",
                "reloading",
            ):
                raise Fault(
                    409, "An independently supervised training process is still active"
                )
