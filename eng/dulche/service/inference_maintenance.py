"""Exclusive inference maintenance built on the broker's durable resource windows."""
from __future__ import annotations

import contextlib
import json
import os
from pathlib import Path
import re
import threading
import time

from engine_profiles import PRIMARY, atomic_json, native_tuning_path
from resource_transitions import ACTIVE, KEY, ResourceWindows
from state import Fault, identity

KIND = "inference-maintenance"


class InferenceWindows(ResourceWindows):
    """An ordinary training release cannot clear unknown tuner custody after restart."""
    def acquire(self, owner, models):
        current = self.current()
        if current.get("kind") == KIND and current.get("phase") in ACTIVE:
            raise Fault(409, "Inference maintenance still owns these resources; its process cleanup must be verified")
        return super().acquire(owner, models)

    def release(self, owner):
        saved = self.ctx.state.get(KEY + ":" + owner, {})
        if saved.get("kind") == KIND and saved.get("phase") in ACTIVE:
            raise Fault(409, "Only the inference controller can restore a maintenance window after verified tuner cleanup")
        return super().release(owner)


def capability(ctx, mid):
    if mid != PRIMARY:
        return {"supported": False, "reason": "This model has no qualified atomic idle handoff for inference tuning."}
    row = ctx.model(mid)
    if ctx.phase != "ready" or ctx.state.get("stop_all", False):
        return {"supported": False, "reason": "Service admission or emergency stop prevents inference tuning."}
    if ctx.training_windows.reserved(mid):
        return {"supported": False, "reason": "A resource window already owns this model or its physical device."}
    if row["state"] != "loaded" or not row.get("managed"):
        return {"supported": False, "reason": "Load this model through Dulche before measuring its inference settings."}
    if row.get("manual_suspended") or row.get("pressure_suspended") or row.get("swarm_residency"):
        return {"supported": False, "reason": "A suspension or swarm residency owner prevents inference tuning."}
    import inference_tuner
    return inference_tuner.capability(ctx.state.directory, mid, engine_root=ctx.engines.root)


def _save(ctx, doc, **changes):
    with ctx.lock:
        current = ctx.training_windows.current()
        if current.get("owner") != doc["owner"]:
            raise Fault(409, "Inference maintenance resource ownership changed")
        current.update(changes)
        doc.clear()
        doc.update(current)
        ctx.training_windows._save(doc)


def _blocked(ctx, doc, error, *, unit=None, receipt=None):
    with ctx.lock:
        _save(ctx, doc, phase="blocked", maintenance_stage="custody_unverified",
              error=str(error)[:700], tuner_unit=unit, tuner_receipt=receipt)
        for entry in doc["models"]:
            ctx.transitions[entry["id"]] = "maintenance_blocked"
            ctx.errors[entry["id"]] = str(error)[:1200]
        ctx.state.audit("inference.maintenance-blocked", doc["operation_id"], {"error": str(error)[:700]})


def _wait_parked(ctx, owner, timeout=420):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        doc = ctx.training_windows.status(owner)
        if doc["phase"] == "blocked":
            raise Fault(409, doc.get("error") or "The native idle handoff could not be verified")
        worker = ctx.training_windows.worker
        if doc["phase"] == "held" and not (worker and worker.is_alive()):
            if doc.get("release_valid") is not True:
                raise Fault(409, "The native model release is no longer verified")
            return
        if ctx.finished.wait(0.05):
            raise Fault(503, "Service shutdown interrupted the native idle handoff")
    raise Fault(504, "The native idle handoff exceeded its deadline")


def _restore_wanted(ctx, doc):
    pref = ctx._prefs(doc["model"])
    return (ctx.phase == "ready" and not ctx.state.get("stop_all", False)
            and not pref.get("manual") and not pref.get("pressure")
            and pref.get("training_skip_restore") != doc["owner"]
            and pref.get("profile", "current") == doc["selected_profile"])


def _rollback_overlay(ctx, doc):
    path = native_tuning_path(ctx.engines.root, doc["model"])
    if not path.exists():
        return
    current = json.loads(path.read_text())
    if current.get("operation_id") != doc["operation_id"]:
        return
    op = ctx.state.get("inference:operation:" + doc["operation_id"], {})
    atomic_json(path, op.get("previous_overlay") or {"schema": 1, "model": doc["model"], "state": "restored"})


def _restore(ctx, doc, body_failed):
    """Candidate processes are settled; restore through a fresh guarded launch."""
    mid = doc["model"]
    _save(ctx, doc, maintenance_stage="restoring")
    if body_failed:
        _rollback_overlay(ctx, doc)
    restore_error = None
    with ctx.lock:
        wanted = _restore_wanted(ctx, doc)
    if wanted:
        try:
            ctx.engines.load(mid, doc["profile_settings"])
        except Exception as exc:
            restore_error = exc
            # A partial candidate load must retire before restoring the old profile.
            ctx.engines.unload(mid)
            _rollback_overlay(ctx, doc)
            with ctx.lock:
                wanted = _restore_wanted(ctx, doc)
            if wanted:
                try:
                    ctx.engines.load(mid, doc["profile_settings"])
                except Exception:
                    ctx.engines.unload(mid)
                    raise
    with ctx.lock:
        wanted = _restore_wanted(ctx, doc)
    # A manual stop, policy close or profile change racing restoration wins.
    if not wanted:
        ctx.engines.unload(mid)
    ctx.refresh()
    with ctx.lock:
        actual = ctx.cache[mid]
        if wanted and actual["state"] != "loaded":
            raise Fault(503, "Model restoration did not confirm a loaded engine")
        pref = ctx._prefs(mid)
        pref["last_operation"] = {"action": "inference-maintenance", "operation_id": doc["operation_id"],
                                  "at": time.time(), "success": restore_error is None,
                                  "restored": wanted, "error": str(restore_error)[:700] if restore_error else None}
        pref["recovery_owned"] = bool(wanted)
        if wanted:
            pref["active_profile"] = doc["active_profile"]
        ctx.state.set("model:" + mid, pref)
        ctx.errors.pop(mid, None)
        ctx.transitions.pop(mid, None)
        _save(ctx, doc, phase="restored", maintenance_stage="restored", restored=wanted,
              error=str(restore_error)[:700] if restore_error else None)
    if restore_error:
        raise Fault(503, "The measured configuration failed to load; the previous profile was restored: " + str(restore_error)[:500])


@contextlib.contextmanager
def maintenance(ctx, mid, operation_id, cancel_event):
    import inference_tuner
    if not isinstance(operation_id, str) or not re.fullmatch(r"[0-9a-f]{32}", operation_id):
        raise Fault(400, "Invalid inference maintenance operation")
    cap = capability(ctx, mid)
    if not cap.get("supported"):
        raise Fault(422, cap.get("reason", "Inference maintenance is unavailable"))
    if cancel_event.is_set():
        raise InterruptedError("Inference maintenance was cancelled before its handoff")
    owner = "tune:" + operation_id + ":" + identity()[:8]
    with ctx.lock:
        row = ctx.model(mid)
        selected = ctx._prefs(mid).get("profile", "current")
        active = row.get("active_profile_id", selected)
        profiles = {p["id"]: p for p in ctx.profiles(mid)}
        if active not in profiles:
            raise Fault(409, "The active model profile is unavailable for restoration")
        # The acquisition worker cannot run before this shared lock records the kind.
        doc = ctx.training_windows.acquire(owner, [mid])
        doc.update(kind=KIND, model=mid, operation_id=operation_id, selected_profile=selected,
                   active_profile=active, profile_settings={} if active == "current" else profiles[active]["settings"],
                   maintenance_stage="acquiring", tuner_custody="not_started")
        ctx.training_windows._save(doc)
    ready, uncertain, body_failed = False, False, False
    try:
        _wait_parked(ctx, owner)
        with ctx.lock:
            ctx.transitions[mid] = "optimising"
        # The original wrapper was atomically emptied and frozen, so it cannot admit direct work.
        ctx.engines.unload(mid)
        ctx.refresh()
        _save(ctx, doc, maintenance_stage="held")
        ready = True
        if cancel_event.is_set():
            raise InterruptedError("Inference maintenance was cancelled after its handoff")
        runtime = Path(os.environ.get("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")) / "dulche-engine-admission"
        runtime.mkdir(mode=0o700, parents=True, exist_ok=True)
        if runtime.is_symlink() or runtime.stat().st_uid != os.getuid():
            raise Fault(409, "The shared physical admission directory is not owned")
        lock = runtime / "resource.lock"
        fd = os.open(lock, os.O_CREAT | os.O_WRONLY | os.O_NOFOLLOW, 0o600)
        os.close(fd)
        yield {"gpu_lock": str(lock), "owner": owner}
    except inference_tuner.CustodyUnsettled as exc:
        uncertain = True
        _blocked(ctx, doc, exc, unit=exc.unit, receipt=exc.receipt)
        raise
    except BaseException:
        body_failed = True
        raise
    finally:
        if not uncertain:
            if ready:
                try:
                    _restore(ctx, doc, body_failed)
                except Exception as exc:
                    # _restore may report a rejected candidate after already restoring the prior model.
                    if ctx.training_windows.current().get("phase") != "restored":
                        _blocked(ctx, doc, "Model restoration requires review: " + str(exc))
                    raise
            else:
                _blocked(ctx, doc, "The original native handoff did not settle; inspect the retained resource window")


def run(ctx, mid, operation_id, argv, *, cwd, cancel_event, timeout_seconds=2700):
    import inference_tuner
    with ctx.lock:
        doc = ctx.training_windows.current()
        if (doc.get("kind") != KIND or doc.get("operation_id") != operation_id
                or doc.get("model") != mid or doc.get("phase") != "held"
                or doc.get("maintenance_stage") != "held" or ctx.transitions.get(mid) != "optimising"):
            raise Fault(409, "The tuner requires its exact exclusive inference maintenance lease")
        if ctx.phase != "ready" or ctx.state.get("stop_all", False):
            raise Fault(409, "Service admission or emergency stop prevents tuner launch")
        if cancel_event.is_set():
            raise InterruptedError("Inference tuning was cancelled before launch")
        _save(ctx, doc, tuner_custody="running")
    finished = threading.Event()

    def watch_stop_intent():
        while not finished.wait(0.1):
            with ctx.lock:
                stop = not _restore_wanted(ctx, doc)
            if stop:
                cancel_event.set()
                return

    watcher = threading.Thread(target=watch_stop_intent, name="dulche-tuning-stop", daemon=True)
    watcher.start()
    try:
        result = inference_tuner.run_tuner(ctx.state.directory, mid, operation_id, argv, cwd=cwd,
                    cancel_event=cancel_event, timeout_seconds=timeout_seconds, engine_root=ctx.engines.root)
    except inference_tuner.CustodyUnsettled as exc:
        _blocked(ctx, doc, exc, unit=exc.unit, receipt=exc.receipt)
        raise
    except BaseException:
        # run_tuner raises CustodyUnsettled for every post-admission unproven release.
        _save(ctx, doc, tuner_custody="settled")
        raise
    finally:
        finished.set()
        watcher.join(timeout=0.3)
    _save(ctx, doc, tuner_custody="settled", tuner_receipt=result.get("receipt"))
    return result
