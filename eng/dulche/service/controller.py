"""Model admission, user policy and frontend lifetime belong to the supervised daemon."""

from pathlib import Path
import json
import logging
import threading
import time

from engine_profiles import EngineBusy
from jobs import Scheduler
from pressure import HostSampler
from state import State, Fault, identity

LOG = logging.getLogger("dulche")
POLICIES = {"always", "resources", "active", "never"}
RECOVERY_LIMIT = 3
RECOVERY_WINDOW_SECONDS = 300


def gpu_matches(label, device):
    label = {"rtx5080": "RTX 5080", "v100": "Tesla V100"}.get(label, label)
    plain = lambda text: str(text).lower().replace("nvidia", "").replace("geforce", "").strip()
    return any(plain(label) == plain(device[k])
               or (k == "name" and plain(label) and plain(label) in plain(device[k]))
               for k in ("id", "name", "gpu", "uuid", "index") if device.get(k) is not None)


def model_uses_gpu(model, device):
    return any(gpu_matches(label, device) for label in
               [model.get("gpu"), *model.get("physical_devices", [])])


def process_start(pid):
    try:
        return Path(f"/proc/{int(pid)}/stat").read_text().rsplit(")", 1)[1].split()[19]
    except (OSError, ValueError, IndexError):
        return None


class Controller:
    def __init__(self, directory, engines, catalog, *, interval=4, cooldown=30):
        self.state = State(directory)
        self.engines, self.catalog = engines, catalog
        self.lock = self.state.lock
        from inference_maintenance import InferenceWindows

        self.training_windows = InferenceWindows(self)
        self.transitions = {}
        self.cache = {}
        self.resources = {}
        self.errors = {}
        self.instance = identity()
        self.boot = Path("/proc/sys/kernel/random/boot_id").read_text().strip()
        saved_frontends = self.state.get("frontends", {})
        if saved_frontends.get("boot_id") != self.boot:
            saved_frontends = {}
        self.sessions = saved_frontends.get("sessions", {})
        self.leases = saved_frontends.get("leases", {})
        self.reconcile_startup = True
        saved_requests = self.state.get("pressure_requests", {})
        self.requests = (
            saved_requests.get("requests", {})
            if saved_requests.get("boot") == self.boot
            else {}
        )
        self.healthy_since = None
        self.bad_samples = 0
        self.cooldown = cooldown
        self.previous_gpu_free = {}
        closing = self.state.get("close_intent")
        self.phase = (
            ("draining" if closing == "active" else "stopping") if closing else "ready"
        )
        self.shutdown_requested = threading.Event()
        self.finished = threading.Event()
        self.stop_server = None
        self.sampler = HostSampler(directory)
        self.refresh()
        from inference_management import InferenceManagement
        self.inference = InferenceManagement(self)
        self.engines.inference_management = self.inference
        self.jobs = Scheduler(
            self.state, engines, self.permit, self.capacity,
            admit=self.admit, prepare=self.prepare_model, recover=self.recover_engine,
        )
        self.upgrade_deadline = None
        from swarm import Swarm

        self.swarm = Swarm(self)
        self.interval = interval
        self.monitor = threading.Thread(
            target=self._monitor, name="dulche-monitor", daemon=True
        )
        self.monitor.start()
        if closing:
            threading.Thread(
                target=self._end_lifecycle, args=(closing,), daemon=True
            ).start()

    def refresh(self):
        observed = {r["id"]: dict(r) for r in self.engines.models()}
        imported = {r["id"]: dict(r) for r in self.catalog.installed()}
        for k, row in imported.items():
            if k not in observed:
                observed[k] = {
                    **row,
                    "state": "unloaded",
                    "engine": row.get("backend"),
                    "gpu": None,
                }
        with self.lock:
            self.cache = observed

    def _prefs(self, mid):
        return self.state.get("model:" + mid, {})

    def model(self, mid):
        with self.lock:
            if mid not in self.cache and mid.startswith("local-"):
                try:
                    rec = self.catalog.get(mid)
                    self.cache[mid] = {
                        **rec,
                        "state": "unloaded",
                        "engine": rec.get("backend"),
                        "gpu": "CPU",
                    }
                except KeyError:
                    pass
            if mid not in self.cache:
                raise Fault(404, "Unknown installed model")
            row = dict(self.cache[mid])
            opts = self._prefs(mid)
            row["manual_suspended"] = bool(opts.get("manual"))
            row["profile_id"] = opts.get("profile", "current")
            row["pressure_suspended"] = bool(opts.get("pressure"))
            if mid in self.transitions:
                row["state"] = self.transitions[mid]
            elif mid in self.errors:
                row.update(state="failed", error=self.errors[mid])
            elif opts.get("manual"):
                row["state"] = "suspended"
            elif opts.get("pressure"):
                row["state"] = "waiting_for_resources"
            recovery = self.state.get("recovery:" + mid, {})
            if recovery.get("pending") and mid not in self.transitions and not (
                opts.get("manual") or opts.get("pressure")
            ):
                row["state"] = "recovering"
            row["recovery"] = {
                "attempts": len(self._recovery_attempts(recovery)),
                "limit": RECOVERY_LIMIT,
                "window_seconds": RECOVERY_WINDOW_SECONDS,
                "pending": bool(recovery.get("pending")),
                "exhausted": bool(recovery.get("exhausted")),
                "retry_at": recovery.get("retry_at"),
                "last_error": recovery.get("last_error"),
            }
            row["can_start"] = bool(
                self.phase == "ready" and not opts.get("manual")
                and not opts.get("pressure") and not self.training_windows.reserved(mid)
                and row["state"] in ("loaded", "unloaded", "failed", "loading", "resuming", "recovering")
                and mid not in self.errors and not recovery.get("exhausted")
                and (not opts.get("swarm_residency") or row["state"] == "loaded")
                and (not row.get("format") or str(row["format"]).lower() == "gguf")
            )
            row["last_operation"] = opts.get("last_operation")
            row["active_profile_id"] = opts.get("active_profile", row["profile_id"])
            row["swarm_residency"] = opts.get("swarm_residency")
            if self.training_windows.reserved(mid):
                row["training_window"] = self.training_windows.current()["owner"]
            row.setdefault("name", mid)
            return row

    def status(self):
        with self.lock:
            return {
                "version": 1,
                "instance_id": self.instance,
                "service": "Dulche",
                "phase": self.phase,
                "accepting": self.phase == "ready",
                "policy": self.state.get("policy", "never"),
                "models": [self.model(k) for k in self.cache],
                "agents": self.jobs.state.recent(32),
                "stop_all": self.state.get("stop_all", False),
                "resources": self.resources,
                "service_error": self.errors.get("__service__"),
            }

    def permit(self, mid, queued=False):
        if self.training_windows.reserved(mid):
            self.training_windows.inference_waiter(mid)
            raise Fault(
                409,
                "The model has a checkpointed training resource window; retry after restoration",
            )
        if self.phase != "ready" and not (queued and self.phase == "draining"):
            raise Fault(503, "The service is draining or stopping")
        row = self.model(mid)
        if row["state"] != "loaded":
            raise Fault(409, "Model unavailable: " + str(row["state"]))

    @staticmethod
    def _recovery_attempts(recovery):
        now = time.time()
        return [at for at in recovery.get("attempts", [])
                if isinstance(at, (int, float)) and 0 <= now - at < RECOVERY_WINDOW_SECONDS]

    def admit(self, mid, queued=False):
        """Admit a durable request while its managed model is still cold."""
        with self.lock:
            if self.training_windows.reserved(mid):
                self.training_windows.inference_waiter(mid)
                raise Fault(409, "The model has a checkpointed training resource window; retry after restoration")
            if self.phase != "ready" and not (queued and self.phase == "draining"):
                raise Fault(503, "The service is draining or stopping")
            row = self.model(mid)
            if row["manual_suspended"]:
                raise Fault(409, "Use Resume to clear manual suspension")
            if row["pressure_suspended"]:
                raise Fault(409, "Model is waiting for resources")
            if self._prefs(mid).get("swarm_residency") and row["state"] != "loaded":
                raise Fault(409, "Wait for the owning swarm to finish its model residency transition")
            if row["recovery"]["exhausted"]:
                raise Fault(503, "Automatic recovery paused after repeated engine failures; inspect the error and explicitly retry Load")
            if mid in self.errors:
                raise Fault(503, self.errors[mid])
            if row["state"] not in ("loaded", "unloaded", "failed", "loading", "resuming", "recovering"):
                raise Fault(409, "Model unavailable: " + str(row["state"]))
            if row.get("format") and str(row["format"]).lower() != "gguf":
                raise Fault(422, "No installed serving adapter for " + str(row["format"]))
            return row

    def prepare_model(self, mid):
        """Start exactly one controller-owned transition; the dispatcher never waits under its lock."""
        with self.lock:
            row = self.admit(mid, queued=True)
            if row["state"] == "loaded":
                return True
            if mid in self.transitions:
                return False
            recovery = self.state.get("recovery:" + mid, {})
            if recovery.get("pending"):
                if self.state.get("stop_all", False):
                    raise Fault(409, "Automatic recovery is paused by the emergency stop")
                if time.time() < recovery.get("retry_at", 0):
                    return False
                attempts = self._recovery_attempts(recovery)
                if len(attempts) >= RECOVERY_LIMIT:
                    self._queue_recovery(mid, recovery.get("last_error", "Engine failed"))
                    self.admit(mid, queued=True)
                self.change(mid, "recover", automatic=True)
                recovery.update(attempts=[*attempts, time.time()], pending=False)
                self.state.set("recovery:" + mid, recovery)
            else:
                self.change(mid, "load", automatic=True)
            return False

    def ensure_model(self, mid, *, timeout=420, cancel_event=None):
        """Share the queue's cold-start path with token counting and other read-only preflight."""
        if type(timeout) not in (int, float) or not 0 < timeout <= 600:
            raise Fault(400, "Model readiness timeout must be between zero and 600 seconds")
        deadline = time.monotonic() + timeout
        while True:
            if cancel_event is not None and cancel_event.is_set():
                raise Fault(409, "Model readiness wait was cancelled")
            with self.lock:
                self.admit(mid)
                if self.prepare_model(mid):
                    return self.model(mid)
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise Fault(504, "Model did not become ready before the request deadline")
            self.finished.wait(min(0.1, remaining))
            if self.finished.is_set():
                raise Fault(503, "Service shutdown in progress")

    def _queue_recovery(self, mid, error):
        """Only call while holding the controller lock and after proving prior managed ownership."""
        recovery = self.state.get("recovery:" + mid, {})
        attempts = self._recovery_attempts(recovery)
        exhausted = len(attempts) >= RECOVERY_LIMIT
        recovery.update(
            attempts=attempts, pending=not exhausted, exhausted=exhausted,
            last_error=str(error)[:1200],
            retry_at=time.time() + min(120, min(5, self.cooldown) * (2 ** len(attempts))),
        )
        self.state.set("recovery:" + mid, recovery)
        if exhausted:
            self.errors[mid] = "Automatic recovery paused after repeated failures: " + str(error)[:1000]
        else:
            self.errors.pop(mid, None)
        self.state.audit("model.recovery", mid, {
            "pending": not exhausted, "attempts": len(attempts), "error": str(error)[:1200],
        })
        return not exhausted

    def recover_engine(self, mid, error):
        """Recover a confirmed crashed managed engine, never a rejected prompt or an external process."""
        observed = (self.engines.status(mid) if hasattr(self.engines, "status") else
                    next((r for r in self.engines.models() if r["id"] == mid), {}))
        with self.lock:
            prior = self.cache.get(mid, {})
            pref = self._prefs(mid)
            if (self.phase != "ready" or self.state.get("stop_all", False)
                    or pref.get("manual") or pref.get("pressure")
                    or self.training_windows.reserved(mid)
                    or pref.get("swarm_residency")
                    or not (pref.get("recovery_owned") or (prior.get("managed") and pref.get("want")))):
                return False
            if observed.get("ownership") == "external-strata" or observed.get("state") not in ("failed", "unloaded"):
                return False
            if observed:
                self.cache[mid] = dict(observed)
            if mid in self.transitions:
                return self.transitions[mid] == "recovering"
            recovery = self.state.get("recovery:" + mid, {})
            if recovery.get("exhausted"):
                return False
            if recovery.get("pending"):
                return True
            return self._queue_recovery(mid, error)

    def capacity(self, mid):
        row = self.cache.get(mid, {})
        # Native Inkling's observed slot count is the admission limit. Other
        # model adapters retain their existing cap until separately qualified.
        ceiling = 64 if mid == "inkling-2-mini-strata-sq-10" and row.get("state") == "loaded" else 12
        return max(1, min(ceiling, int(row.get("capacity") or row.get("slots") or 1)))

    def profiles(self, mid):
        row = self.model(mid)
        defaults = row.get("settings", {})
        if hasattr(self.engines, "default_settings") and row.get("engine"):
            defaults = self.engines.default_settings(mid)
        proof = self.state.get("generation-proof:" + mid, {})
        verified = (
            mid in ("qwen3.8-flash-next-unsloth-ud-iq4_xs", "qwen3.8-27b-v100")
            or proof.get("profile") == "current"
        )
        saved = self.state.get("profiles:" + mid, {})
        return [
            {
                "id": "current",
                "name": "Current — Verified"
                if verified
                else "Current — Awaiting verification",
                "settings": defaults,
                "protected": True,
                "functionally_verified": verified,
                "verification": row.get(
                    "verification",
                    "baseline configuration; consult engine measurements",
                ),
            },
            *[{"id": key, **val} for key, val in self._overrides(saved).items()],
        ]

    @staticmethod
    def _overrides(saved):
        return {
            "experimental": {
                "name": "Optimised — Experimental",
                "settings": {},
                "protected": False,
            },
            **saved,
        }

    def edit_profile(self, mid, body, source=None, action=None):
        with self.lock:
            self.model(mid)
            entries = self._overrides(self.state.get("profiles:" + mid, {}))
            if action == "select":
                if source != "current" and source not in entries:
                    raise Fault(404, "Unknown profile")
                if self.model(mid)["state"] == "loaded":
                    return self.change(mid, "switch", profile=source)
                pref = self._prefs(mid)
                pref["profile"] = source
                self.state.set("model:" + mid, pref)
                return self.model(mid)
            if action and source == "current":
                raise Fault(403, "The verified profile is protected")
            name = body.get("name", "")
            if not isinstance(name, str) or not name.strip() or len(name) > 120:
                raise Fault(400, "Invalid profile name")
            if action == "rename":
                if source not in entries:
                    raise Fault(404, "Unknown profile")
                entries[source]["name"] = name
            else:
                origin = body.get("source_id", "current")
                if origin != "current" and origin not in entries:
                    raise Fault(404, "Unknown source profile")
                overrides = dict(entries.get(origin, {}).get("settings", {}))
                if not isinstance(body.get("settings", {}), dict):
                    raise Fault(400, "Settings must be an object")
                overrides.update(body.get("settings", {}))
                self._register(mid)
                self.engines.validate_settings(mid, overrides)
                source = identity()
                entries[source] = {
                    "name": name,
                    "settings": overrides,
                    "protected": False,
                }
            self.state.set("profiles:" + mid, entries)
            return {"id": source, **entries[source]}

    def count_prompt(self, model_id, messages):
        return self.count_prompt_with_options(model_id, messages, {})

    def count_prompt_with_options(self, model_id, messages, options):
        self.ensure_model(model_id)
        try:
            return self.engines.count_prompt(model_id, messages, options)
        except Exception as exc:
            # Token counting is read-only and can safely retry once after a proven crash.
            if not self.recover_engine(model_id, exc):
                raise
            self.ensure_model(model_id)
            return self.engines.count_prompt(model_id, messages, options)

    def _register(self, mid):
        if not mid.startswith("local-"):
            return
        rec = self.catalog.get(mid)
        if rec["format"].lower() != "gguf":
            raise Fault(422, "No installed serving adapter for " + rec["format"])
        self.engines.register_gguf(mid, rec["path"], rec)

    def change(self, mid, action, *, profile=None, automatic=False, owner=None):
        with self.lock:
            if action == "recover" and not automatic:
                raise Fault(403, "Recovery is a controller-owned transition")
            if self.training_windows.reserved(mid):
                slot = self.training_windows.current()
                if (
                    action in ("unload", "suspend")
                    and not automatic
                    and owner is None
                    and slot["direction"] != "restoring"
                ):
                    pref = self._prefs(mid)
                    pref.update(
                        manual=action == "suspend",
                        want=action == "suspend",
                        pressure=False,
                        training_skip_restore=slot["owner"],
                    )
                    self.state.set("model:" + mid, pref)
                    return self.model(mid)
                raise Fault(
                    409,
                    "The cycle owns this model until its training window is released",
                )
            if self.phase != "ready" and action not in ("unload", "suspend"):
                raise Fault(503, "Service shutdown in progress")
            row = self.model(mid)
            pref = self._prefs(mid)
            if mid in self.transitions:
                if (
                    action == "suspend"
                    and not automatic
                    and self.transitions[mid] in ("loading", "resuming", "recovering", "suspending")
                ):
                    pref.update(
                        manual=True, pressure=False, want=True, pending_suspend=True
                    )
                    self.state.set("model:" + mid, pref)
                    self.transitions[mid] = "suspending"
                    self.jobs.stop(model=mid, emergency=False)
                    return self.model(mid)
                raise Fault(409, "Another model operation is in progress")
            if action == "load" and pref.get("manual"):
                raise Fault(409, "Use Resume to clear manual suspension")
            if action == "resume" and not automatic and row["state"] == "loaded":
                self.errors.pop(mid, None)
                self.state.set("recovery:" + mid, {})
                return self.model(mid)
            if automatic and pref.get("manual"):
                raise Fault(409, "Manually suspended")
            if action in ("load", "resume", "switch", "recover"):
                if profile is None:
                    profile = pref.get("profile", "current")
                entries = {p["id"]: p for p in self.profiles(mid)}
                if profile not in entries:
                    raise Fault(404, "Unknown performance profile")
                self._register(mid)
                overrides = {} if profile == "current" else entries[profile]["settings"]
                self.engines.validate_settings(mid, overrides)
                if row["state"] == "loaded" and profile == row["active_profile_id"]:
                    if not automatic and owner is None:
                        self.errors.pop(mid, None)
                        self.state.set("recovery:" + mid, {})
                    return self.model(mid)
            else:
                overrides = {}
            marker = {
                "load": "loading",
                "unload": "unloading",
                "suspend": "suspending",
                "resume": "resuming",
                "switch": "loading",
                "recover": "recovering",
            }
            if action not in marker:
                raise Fault(404, "Unknown model action")
            self.errors.pop(mid, None)
            if not automatic and owner is None:
                self.state.set("recovery:" + mid, {})
            if not automatic and owner is None:
                pref.pop("swarm_residency", None)
            if owner is not None:
                if pref.get("manual") or pref.get("pressure"):
                    raise Fault(409, "Operator or resource suspension takes precedence")
                pref["swarm_residency"] = {"owner": owner, "target": action}
            elif action in ("suspend", "unload"):
                pref.update(
                    manual=action == "suspend" and not automatic,
                    pressure=automatic,
                    want=action == "suspend",
                )
            elif not automatic:
                pref.update(manual=False, pressure=False, want=True)
            self.state.set("model:" + mid, pref)
            self.state.audit(
                "model." + action,
                mid,
                {"automatic": automatic, "profile_id": profile, "stage": "requested"},
            )
            self.transitions[mid] = marker[action]
            threading.Thread(
                target=self._apply,
                args=(mid, action, profile, overrides, owner),
                daemon=True,
            ).start()
            return self.model(mid)

    def swarm_residency(self, mid, target, profile, owner):
        with self.lock:
            if target == "release" and owner:
                pref = self._prefs(mid)
                if (pref.get("swarm_residency") or {}).get("owner") == owner:
                    pref.pop("swarm_residency", None)
                    self.state.set("model:" + mid, pref)
                return self.model(mid)
            if not owner or self.state.get("stop_all", False) or self.phase != "ready":
                raise Fault(409, "Swarm transitions are unavailable")
            row = self.model(mid)
            pref = self._prefs(mid)
            if pref.get("manual") or pref.get("pressure"):
                raise Fault(409, "Operator or resource suspension takes precedence")
            reservation = pref.get("swarm_residency")
            if reservation and reservation["owner"] != owner:
                raise Fault(409, "Another swarm owns the transition")
            if mid in self.transitions:
                return row
            if target not in ("load", "unload", "partial"):
                raise Fault(400, "Unknown residency strategy")
            if target == "unload" and row["state"] == "unloaded":
                return row
            if (
                target == "load"
                and row["state"] == "loaded"
                and (profile or "current") == row["active_profile_id"]
            ):
                return row
            if any(
                a["model"] == mid and a.get("phase") != "tool"
                for a in self.jobs.active.values()
            ):
                raise Fault(409, "Wait for admitted inference to reach a safe boundary")
            if self.state.db.execute(
                "SELECT 1 FROM runs WHERE model=? AND state='queued' LIMIT 1", (mid,)
            ).fetchone():
                raise Fault(409, "Previously admitted model work is queued")
            if target == "partial":
                specs = {p["id"]: p for p in self.profiles(mid)}
                config = specs.get(profile, {}).get("settings", {})
                if (
                    mid.startswith("local-") is False
                    or config.get("gpu") != "Tesla V100"
                    or not config.get("vram_ceiling_mib")
                ):
                    raise Fault(
                        422,
                        "Choose a V100 GGUF profile with an explicit layer count and VRAM ceiling",
                    )
                if row["state"] == "loaded" and row["active_profile_id"] == profile:
                    return row
            action = (
                "unload"
                if target == "unload"
                else ("switch" if row["state"] == "loaded" else "load")
            )
            return self.change(mid, action, profile=profile or "current", owner=owner)

    def _apply(self, mid, action, profile, overrides, owner=None):
        try:
            release = {}
            if action in ("suspend", "unload", "switch", "recover"):
                if action != "recover":
                    self.jobs.stop(model=mid, emergency=False)
                release = self.engines.unload(mid) or {}
            if action in ("load", "resume", "switch", "recover"):
                self.engines.load(mid, overrides)
            self.refresh()
            with self.lock:
                obs = self.cache[mid]["state"]
                desired = action in ("load", "resume", "switch", "recover")
                if (desired and obs != "loaded") or (not desired and obs == "loaded"):
                    raise Fault(
                        503, "Engine did not confirm the requested lifecycle state"
                    )
                pref = self._prefs(mid)
                pending = pref.pop("pending_suspend", False)
                if desired and not pending:
                    if not owner:
                        pref.update(
                            manual=False,
                            pressure=False,
                            want=True,
                            profile=profile,
                            retry_count=0,
                            retry_at=0,
                        )
                    pref["active_profile"] = profile
                    pref["recovery_owned"] = True
                    recovery = self.state.get("recovery:" + mid, {})
                    recovery.update(pending=False, exhausted=False, retry_at=None)
                    self.state.set("recovery:" + mid, recovery)
                elif desired:
                    pref.update(manual=True, pressure=False, want=True, profile=profile)
                proof = {
                    k: release[k]
                    for k in ("release_verified", "released_vram_mib", "released_devices_mib", "release_detail")
                    if k in release
                }
                pref["last_operation"] = {
                    "action": action,
                    "at": time.time(),
                    "success": True,
                    **proof,
                }
                if action == "suspend" and pref.get("pressure"):
                    pref["reclaim_mib"] = release.get("released_vram_mib", 0)
                    pref["reclaim_devices_mib"] = release.get("released_devices_mib", {})
                self.state.audit("model." + action, mid, pref["last_operation"])
                self.state.set("model:" + mid, pref)
                self.transitions.pop(mid, None)
                if desired and pending:
                    self.change(mid, "suspend")
                elif desired and self.phase == "ready":
                    self.jobs.recover_model(mid)
        except Exception as exc:
            with self.lock:
                pref = self._prefs(mid)
                if (
                    action == "resume"
                    and pref.get("pressure")
                    and isinstance(exc, EngineBusy)
                ):
                    retry_count = min(4, pref.get("retry_count", 0) + 1)
                    pref.update(
                        retry_count=retry_count,
                        retry_at=time.time() + self.cooldown * (2 ** (retry_count - 1)),
                        last_operation={
                            "action": action,
                            "success": False,
                            "retry": True,
                            "error": str(exc),
                        },
                    )
                    self.state.set("model:" + mid, pref)
                elif action == "recover" and not (pref.get("manual") or pref.get("pressure")):
                    self._queue_recovery(mid, exc)
                else:
                    self.errors[mid] = str(exc)[:1200]
                self.transitions.pop(mid, None)
                self.state.audit(
                    "model." + action, mid, {"success": False, "error": str(exc)[:1200]}
                )
            LOG.warning("%s %s failed: %s", mid, action, exc)

    def inference_tuning_capability(self, mid):
        from inference_maintenance import capability
        return capability(self, mid)

    def inference_maintenance(self, mid, operation_id, cancel_event):
        from inference_maintenance import maintenance
        return maintenance(self, mid, operation_id, cancel_event)

    def run_inference_tuner(self, mid, operation_id, argv, *, cwd, cancel_event, timeout_seconds=2700):
        from inference_maintenance import run
        return run(self, mid, operation_id, argv, cwd=cwd,
                   cancel_event=cancel_event, timeout_seconds=timeout_seconds)

    def prepare_upgrade(self):
        with self.lock:
            if (
                self.phase != "ready"
                or self.transitions
                or self.jobs.active
                or (self.training_windows.current().get("kind") == "inference-maintenance"
                    and self.training_windows.current().get("phase") in ("releasing", "held", "restoring", "blocked"))
                or (
                    self.training_windows.worker
                    and self.training_windows.worker.is_alive()
                )
            ):
                raise Fault(
                    409,
                    "An engine operation or task is active; retry the update after it finishes",
                )
            if self.state.db.execute(
                "SELECT 1 FROM runs WHERE state IN ('queued','running') LIMIT 1"
            ).fetchone():
                raise Fault(409, "Previously admitted jobs are waiting")
            self.phase = "upgrading"
            self.upgrade_deadline = time.monotonic() + 120
            return {"phase": self.phase, "models_preserved": True}

    def set_policy(self, value):
        if value not in POLICIES:
            raise Fault(400, "Unknown Keep Models Loaded policy")
        with self.lock:
            self.state.set("policy", value)
            self.state.audit("policy", "frontend", {"value": value})
            self.healthy_since = None
            self.bad_samples = 0
            self.previous_gpu_free.clear()
        return {"policy": value}

    def _save_frontends(self):
        self.state.set(
            "frontends",
            {"boot_id": self.boot, "sessions": self.sessions, "leases": self.leases},
        )

    def frontend(self, action, body, peer):
        now = time.monotonic()
        with self.lock:
            self.sessions = {
                k: v for k, v in self.sessions.items() if process_start(v[0]) == v[1]
            }
            self.leases = {
                k: v
                for k, v in self.leases.items()
                if v[1] > now and v[0] in self.sessions
            }
            sid = body.get("session_id")
            if action == "lease":
                target = next(
                    (k for k, v in self.sessions.items() if v[0] == body.get("pid")),
                    None,
                )
                if not target:
                    raise Fault(404, "Frontend process has no registered session")
                ttl = int(body.get("ttl_seconds", 120))
                if not 1 <= ttl <= 300 or body.get("reason") not in (
                    "restart",
                    "update",
                    "development",
                ):
                    raise Fault(400, "Invalid frontend development lease")
                lease = identity()
                self.leases[lease] = (target, now + ttl)
                self._save_frontends()
                return {"lease_id": lease, "expires_at": time.time() + ttl}
            if not isinstance(sid, str) or not 1 <= len(sid) <= 128:
                raise Fault(400, "Invalid session ID")
            if action == "connect":
                if self.phase != "ready":
                    raise Fault(503, "Service is draining")
                if sid in self.sessions and self.sessions[sid][0] != peer:
                    raise Fault(403, "Foreign session")
                binding = (peer, process_start(peer))
                if tuple(self.sessions.get(sid, ())) != binding:
                    self.sessions[sid] = binding
                    self._save_frontends()
                if body.get("policy"):
                    self.set_policy(body["policy"])
                return {
                    "connected": True,
                    "version": 1,
                    "policy": self.state.get("policy", "never"),
                }
            if action != "close":
                raise Fault(404, "Unknown frontend operation")
            if self.sessions.get(sid, (None,))[0] != peer:
                raise Fault(403, "Close must come from the registered frontend")
            leases = [k for k, v in self.leases.items() if v[0] == sid]
            reason = body.get("reason", "user")
            if reason not in ("user", "restart", "update", "development"):
                raise Fault(400, "Unknown close reason")
            if reason != "user" and not leases:
                raise Fault(409, "Development restart requires an active lease")
            self.sessions.pop(sid)
            for k in leases:
                self.leases.pop(k)
            self._save_frontends()
            self.state.audit(
                "frontend.close",
                sid,
                {"reason": reason, "development_lease": bool(leases)},
            )
            if leases or self.sessions:
                return {"closed": True, "action": "keep"}
            policy = self.state.get("policy", "never")
            if policy in ("active", "never"):
                self.state.set("close_intent", policy)
                self.phase = "draining" if policy == "active" else "stopping"
                threading.Thread(
                    target=self._end_lifecycle, args=(policy,), daemon=True
                ).start()
            return {"closed": True, "action": self.phase}

    def _end_lifecycle(self, policy):
        try:
            self.inference.close()
            self.swarm.checkpoint("Frontend close policy " + policy)
            if policy == "active":
                while True:
                    with self.lock:
                        for r in self.state.db.execute(
                            "SELECT id,model,request FROM runs WHERE state='queued'"
                        ).fetchall():
                            if (
                                self.model(r["model"])["state"] != "loaded"
                                or json.loads(r["request"]).get("not_before", 0)
                                > time.time()
                            ):
                                self.jobs.stop(run=r["id"], emergency=False)
                        pending = self.state.db.execute(
                            "SELECT COUNT(*) FROM runs WHERE state IN ('queued','running')"
                        ).fetchone()[0]
                    if not pending:
                        break
                    if self.finished.wait(0.3):
                        return
            self.swarm.close()
            self.jobs.shutdown()
            # Catalog worker shutdown flushes durable offsets and stops background writers.
            self.catalog.close()
            while self.transitions and not self.finished.wait(0.2):
                pass
            if self.finished.is_set():
                return
            self.refresh()
            for mid in list(self.cache):
                if self.cache[mid]["state"] == "loaded" or (
                    self.cache[mid].get("managed")
                    and self.cache[mid]["state"] in ("loading", "failed")
                ):
                    self.engines.unload(mid)
            self.refresh()
            if any(
                row["state"] in ("loaded", "loading") for row in self.cache.values()
            ):
                raise RuntimeError("An engine still holds a model")
            with self.lock:
                for mid in self.cache:
                    pref = self._prefs(mid)
                    pref["recovery_owned"] = False
                    self.state.set("model:" + mid, pref)
                    self.state.set("recovery:" + mid, {})
            self.state.set("close_intent", None)
            self.phase = "stopped"
            self.shutdown_requested.set()
            if self.stop_server:
                self.stop_server()
        except Exception as e:
            self.phase = "degraded"
            self.errors["__service__"] = str(e)
            LOG.exception("Controlled shutdown incomplete")

    def resource_request(self, body):
        with self.lock:
            rid = body.get("id") or identity()
            if body.get("release"):
                self.requests.pop(rid, None)
            else:
                amount = int(body.get("free_mib", 0))
                ttl = int(body.get("ttl_seconds", 120))
                if (
                    not 1 <= amount <= 1048576
                    or not 1 <= ttl <= 3600
                    or not isinstance(body.get("gpu"), str)
                ):
                    raise Fault(400, "Specify gpu, free_mib and a bounded ttl_seconds")
                self.requests[rid] = (body["gpu"], amount, time.monotonic() + ttl)
            self.healthy_since = None
            self.state.set(
                "pressure_requests", {"boot": self.boot, "requests": self.requests}
            )
            return {"id": rid, "active": rid in self.requests}

    def _poll(self):
        self.refresh()
        host = self.sampler.sample()
        backend = self.engines.resources()
        with self.lock:
            if (
                self.phase == "upgrading"
                and self.upgrade_deadline is not None
                and time.monotonic() > self.upgrade_deadline
            ):
                self.phase = "ready"
                self.upgrade_deadline = None
            self.resources = {
                **backend,
                **host,
                "ram_available_mib": host["available_ram_mib"],
            }
            # An external restart cannot clear a manual latch. Restore only previously requested
            # models when Always is active; untouched catalogue entries stay unloaded.
            if self.phase == "ready":
                for mid, observed in self.cache.items():
                    if (
                        mid in self.transitions
                        or mid in self.errors
                        or self.training_windows.reserved(mid)
                    ):
                        continue
                    pref = self._prefs(mid)
                    if observed["state"] == "loaded" and observed.get("managed") and pref.get("want") and not pref.get("recovery_owned"):
                        pref["recovery_owned"] = True
                        self.state.set("model:" + mid, pref)
                    recovery = self.state.get("recovery:" + mid, {})
                    if recovery.get("pending") and not (pref.get("manual") or pref.get("pressure")):
                        try:
                            self.prepare_model(mid)
                        except Fault:
                            pass
                        continue
                    if (pref.get("recovery_owned") and pref.get("want")
                            and observed["state"] in ("unloaded", "failed")
                            and not pref.get("manual") and not pref.get("pressure")
                            and not pref.get("swarm_residency")
                            and not self.state.get("stop_all", False)
                            and not recovery.get("exhausted")):
                        self._queue_recovery(mid, observed.get("detail", "Managed engine exited unexpectedly"))
                        continue
                    if pref.get("manual") and observed["state"] == "loaded":
                        self.change(mid, "suspend")
                    elif (
                        (self.reconcile_startup or pref.get("pressure"))
                        and self.state.get("policy", "never") == "always"
                        and not pref.get("swarm_residency")
                        and time.time() >= pref.get("retry_at", 0)
                        and pref.get("want")
                        and not pref.get("manual")
                        and observed["state"] == "unloaded"
                    ):
                        self.change(mid, "resume", automatic=True)
                self.reconcile_startup = False
            if (
                self.phase != "ready"
                or self.state.get("policy", "never") != "resources"
            ):
                self.healthy_since = None
                return
            now = time.monotonic()
            self.requests = {k: v for k, v in self.requests.items() if v[2] > now}
            # Existing VRAM occupancy alone cannot identify another application's demand.
            pressure = (
                host["available_ram_mib"] < 1536
                or (host["swap_mib_s"] or 0) > 16
                or (host["memory_stall_pct"] or 0) > 10
                or (host["desktop_roundtrip_ms"] or 0) > 250
            )
            self.bad_samples = self.bad_samples + 1 if pressure else 0
            eligible = [
                r
                for r in self.cache.values()
                if r["state"] == "loaded"
                and r["id"] not in self.transitions
                and not self.training_windows.reserved(r["id"])
                and not self._prefs(r["id"]).get("manual")
            ]
            gpu_rows = backend.get("gpus", [])
            if isinstance(gpu_rows, dict):
                gpu_rows = list(gpu_rows.values())

            for device in gpu_rows:
                if device.get("free_mib") is None:
                    continue
                key = device.get("uuid") or device.get("name")
                free = device["free_mib"]
                prior = self.previous_gpu_free.get(key, free)
                self.previous_gpu_free[key] = free
                if free < 256 and prior - free >= 128 and not self.transitions:
                    for row in eligible:
                        if model_uses_gpu(row, device):
                            self.change(row["id"], "suspend", automatic=True)
                            self.healthy_since = None
                            return
            for gpu, amount, _ in self.requests.values():
                observed = next((g for g in gpu_rows if gpu_matches(gpu, g)), None)
                if observed is None:
                    continue
                free = observed.get("free_mib")
                if free is not None and free < amount:
                    candidates = [
                        r for r in eligible if model_uses_gpu(r, observed)
                    ]
                    if candidates and not self.transitions:
                        self.change(candidates[0]["id"], "suspend", automatic=True)
                    self.healthy_since = None
                    return
            if self.bad_samples >= 2:
                if eligible and not self.transitions:
                    self.change(eligible[-1]["id"], "suspend", automatic=True)
                self.healthy_since = None
            elif self.requests or pressure or host["available_ram_mib"] < 3072:
                self.healthy_since = None
            elif self.healthy_since is None:
                self.healthy_since = now
            elif now - self.healthy_since >= self.cooldown and not self.transitions:
                for mid in self.cache:
                    pref = self._prefs(mid)
                    if (
                        pref.get("pressure")
                        and not self.training_windows.reserved(mid)
                        and not pref.get("manual")
                        and mid not in self.errors
                        and time.time() >= pref.get("retry_at", 0)
                    ):
                        required = pref.get("reclaim_mib", 0)
                        per_device = pref.get("reclaim_devices_mib", {})
                        if per_device and any(
                            not any(gpu_matches(role, g) and g.get("free_mib", 0) >= amount
                                    for g in gpu_rows)
                            for role, amount in per_device.items()
                        ):
                            continue
                        observed = next(
                            (
                                g
                                for g in gpu_rows
                                if model_uses_gpu(self.cache[mid], g)
                            ),
                            None,
                        )
                        if required and not per_device and (
                            not observed or observed.get("free_mib", 0) < required
                        ):
                            continue
                        self.change(mid, "resume", automatic=True)
                        self.healthy_since = now
                        break

    def _monitor(self):
        while not self.finished.is_set():
            try:
                self._poll()
            except Exception as exc:
                LOG.warning("Resource observation failed: %s", exc)
            self.finished.wait(self.interval)

    def close(self):
        self.inference.close()
        self.finished.set()
        self.monitor.join(timeout=8)
        self.swarm.close()
        self.jobs.shutdown()
        self.catalog.close()
