"""Persistent model reservations for independently supervised cyclic training; private API only."""

from __future__ import annotations

import json
import os
import re
import threading
import time

import training_park as park
from state import Fault

KEY = "training-resource-window"
OWNER = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.:-]{0,127}\Z")
ACTIVE = ("releasing", "held", "restoring", "blocked")


class ResourceWindows:
    def __init__(self, ctx):
        self.ctx = ctx
        self.worker = None

    def current(self):
        return self.ctx.state.get(KEY, {})

    def reserved(self, mid):
        doc = self.current()
        if doc.get("phase") not in ACTIVE:
            return False
        if any(e["id"] == mid for e in doc["models"]):
            return True
        try:
            requested = set(self.ctx.engines.physical_devices(self.ctx.engines._spec(mid)))
        except (OSError, ValueError, RuntimeError, KeyError):
            return False
        return any(requested.intersection(e.get("physical_devices") or self.ctx.engines.physical_devices(
                       {"id": e["id"], "gpu": e.get("settings", {}).get("gpu")}))
                   for e in doc["models"])

    def inference_waiter(self, mid):
        d = self.current()
        if not d or not self.reserved(mid):
            return
        target = self.ctx.state.directory / (
            "training-window-demand-" + d["owner"] + ".json"
        )
        try:
            fd = os.open(
                target, os.O_CREAT | os.O_EXCL | os.O_WRONLY | os.O_NOFOLLOW, 0o600
            )
        except FileExistsError:
            return
        with os.fdopen(fd, "w") as f:
            json.dump({"model": mid, "epoch": time.time()}, f)

    def _save(self, doc):
        doc["updated_epoch"] = time.time()
        # Both keys are changed by one SQLite transaction. Duplicate late requests retain their
        # original receipt even after the next cycle starts.
        with self.ctx.state.transaction() as db:
            encoded = json.dumps(doc, allow_nan=False)
            for key in (KEY, KEY + ":" + doc["owner"]):
                db.execute(
                    "INSERT OR REPLACE INTO config(k,value) VALUES(?,?)", (key, encoded)
                )

    def status(self, owner=None):
        with self.ctx.lock:
            doc = self.ctx.state.get(KEY + ":" + owner, {}) if owner else self.current()
            if not doc:
                return {"phase": "idle", "owner": None, "models": []}
            out = dict(doc)
            out["runner_active"] = bool(
                self.worker
                and self.worker.is_alive()
                and self.current().get("owner") == doc["owner"]
            )
            out["broker_inference_waiting"] = (
                self.ctx.state.directory
                / ("training-window-demand-" + doc["owner"] + ".json")
            ).is_file()
            if out["phase"] == "held":
                # Recheck real custody before each grant. This is also the cycle worker's API
                # for detecting an unexpected restart or a direct connection during a hold.
                out["release_valid"] = all(
                    park.parked_native(self.ctx.engines, e["native"])
                    for e in doc["models"]
                    if e.get("native")
                )
                out["direct_inference_waiting"] = any(
                    park.direct_waiting(e["native"])
                    for e in doc["models"]
                    if e.get("native")
                )
            return out

    def _idle(self, models):
        ctx = self.ctx
        if ctx.phase != "ready" or ctx.state.get("stop_all", False):
            raise Fault(
                409,
                "Service admission or emergency stop does not permit a training window",
            )
        if ctx.transitions:
            raise Fault(409, "A model lifecycle operation is active")
        if hasattr(ctx, "swarm") and any(
            d["state"] in ("running", "pausing", "waiting_for_resources")
            for d in ctx.swarm._all()
        ):
            raise Fault(409, "An admitted swarm owns or may need the models")
        placeholders = ",".join("?" for _ in models)
        sql = f"SELECT 1 FROM runs WHERE model IN ({placeholders}) AND state IN ('queued','running') LIMIT 1"
        if ctx.state.db.execute(sql, models).fetchone() or any(
            a["model"] in models for a in ctx.jobs.active.values()
        ):
            raise Fault(
                409, "Selected inference is running or queued; no work was interrupted"
            )

    def _start(self, doc, direction):
        doc.update(phase=direction, direction=direction, error=None)
        self._save(doc)
        self.worker = threading.Thread(
            target=self._run,
            args=(doc["owner"], direction),
            daemon=True,
            name="dulche-resource-window",
        )
        self.worker.start()
        return doc

    def acquire(self, owner, models):
        if not isinstance(owner, str) or not OWNER.fullmatch(owner):
            raise Fault(400, "Invalid training window owner")
        if (
            not isinstance(models, list)
            or not 1 <= len(models) <= 8
            or not all(isinstance(mid, str) and 0 < len(mid) <= 200 for mid in models)
            or len(set(models)) != len(models)
        ):
            raise Fault(400, "Select one to eight distinct registered model IDs")
        with self.ctx.lock:
            old = self.ctx.state.get(KEY + ":" + owner)
            if old and [e["id"] for e in old["models"]] != models:
                raise Fault(409, "This owner already identifies a different window")
            if old and old["phase"] == "restored":
                return old
            current = self.current()
            if current.get("phase") in ACTIVE and current["owner"] != owner:
                raise Fault(409, "Another cycle owns the resource window")
            if old:
                if (
                    old["phase"] == "held"
                    or old["direction"] != "releasing"
                    or self.worker
                    and self.worker.is_alive()
                ):
                    return self.status(owner)
                self._idle(models)
                return self._start(old, "releasing")
            self._idle(models)
            entries = []
            for mid in models:
                m = self.ctx.model(mid)
                pref = self.ctx._prefs(mid)
                if m.get("swarm_residency") or m["state"] in (
                    "loading",
                    "unloading",
                    "resuming",
                    "suspending",
                    "failed",
                ):
                    raise Fault(
                        409, "A selected model has a foreign or unsettled lifecycle"
                    )
                physical = self.ctx.engines.status(mid)
                if physical["state"] == "loaded" and (
                    pref.get("manual")
                    or pref.get("pressure")
                    or not physical.get("managed")
                ):
                    raise Fault(
                        409,
                        "Cannot park a model with conflicting suspension or external ownership",
                    )
                if physical["state"] not in ("loaded", "unloaded"):
                    raise Fault(409, "Selected model has no stable physical state")
                if (
                    mid == park.PRIMARY
                    and physical["state"] == "loaded"
                    and park.owner_turn_active(self.ctx.state.directory)
                ):
                    raise Fault(
                        409,
                        "The independent Qwen CLI turn still owns its context; retry after it exits",
                    )
                native = (
                    park.inspect_native(self.ctx.engines, mid)
                    if physical["state"] == "loaded"
                    else None
                )
                entries.append(
                    {
                        "id": mid,
                        "prior_state": physical["state"],
                        "profile_id": m.get("active_profile_id", "current"),
                        "settings": dict(m.get("settings", {})),
                        "physical_devices": self.ctx.engines.physical_devices(physical),
                        "native": native,
                        "stage": "planned" if native else "unchanged",
                        "release": None,
                    }
                )
            if not any(e.get("native") for e in entries):
                raise Fault(
                    409,
                    "No loaded managed model was selected; no release capacity can be verified",
                )
            return self._start(
                {
                    "version": 1,
                    "owner": owner,
                    "created_epoch": time.time(),
                    "models": entries,
                },
                "releasing",
            )

    def release(self, owner):
        if not isinstance(owner, str) or not OWNER.fullmatch(owner):
            raise Fault(400, "Invalid training window owner")
        with self.ctx.lock:
            doc = self.ctx.state.get(KEY + ":" + owner)
            if not doc:
                raise Fault(404, "Unknown training resource window")
            if doc["phase"] == "restored":
                return doc
            if self.current().get("owner") != owner:
                raise Fault(409, "Resource window owner changed")
            if self.worker and self.worker.is_alive():
                raise Fault(409, "A resource transition is still executing")
            self._idle([e["id"] for e in doc["models"]])
            park.require_no_training(self.ctx.state.directory)
            return self._start(doc, "restoring")

    def _run(self, owner, direction):
        with self.ctx.lock:
            doc = self.current()
        if doc.get("owner") != owner:
            return
        try:
            entries = (
                doc["models"] if direction == "releasing" else reversed(doc["models"])
            )
            for e in entries:
                if not e.get("native"):
                    continue
                mid = e["id"]
                with self.ctx.lock:
                    self._idle([mid])
                    if (
                        direction == "releasing"
                        and e["stage"] == "parked"
                        and park.parked_native(self.ctx.engines, e["native"])
                    ):
                        continue
                    if direction == "restoring" and e["stage"] in (
                        "planned",
                        "restored",
                        "unchanged",
                    ):
                        continue
                    pref = self.ctx._prefs(mid)
                    e["stage"] = (
                        "unloading" if direction == "releasing" else "restoring"
                    )
                    self._save(doc)
                if direction == "releasing":
                    e["checkpoint"] = park.refresh_mentor_checkpoint(
                        self.ctx.state.directory
                    )
                    with self.ctx.lock:
                        self._save(doc)
                    if park.owner_turn_active(self.ctx.state.directory):
                        raise Fault(
                            409,
                            "A new independent Qwen CLI turn arrived before release",
                        )
                    e["release"] = park.park_native(self.ctx.engines, e["native"])
                    if not e["release"].get("release_verified"):
                        raise Fault(409, "Physical release was not verified")
                    e["stage"] = "parked"
                else:
                    park.require_no_training(self.ctx.state.directory)
                    e["restore"] = park.restore_native(
                        self.ctx.engines,
                        e["native"],
                        load=not (
                            pref.get("manual")
                            or pref.get("pressure")
                            or pref.get("training_skip_restore") == owner
                        ),
                    )
                    e["stage"] = "restored"
                with self.ctx.lock:
                    self._save(doc)
            doc["phase"] = "held" if direction == "releasing" else "restored"
        except Exception as exc:
            doc.update(phase="blocked", error=str(exc)[:600])
        finally:
            with self.ctx.lock:
                self._save(doc)
            try:
                self.ctx.refresh()
            except (RuntimeError, OSError):
                pass

    def handle(self, method, action, body, query):
        if method == "GET" and action is None:
            return 200, self.status((query.get("owner") or [None])[0])
        if (
            method == "POST"
            and action == "acquire"
            and set(body) == {"owner", "models"}
        ):
            return 202, self.acquire(body["owner"], body["models"])
        if method == "POST" and action == "release" and set(body) == {"owner"}:
            return 202, self.release(body["owner"])
        raise Fault(400, "Invalid training window operation")
