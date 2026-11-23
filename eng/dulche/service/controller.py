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
        self.jobs = Scheduler(self.state, engines, self.permit, self.capacity)
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
            row["last_operation"] = opts.get("last_operation")
            row["active_profile_id"] = opts.get("active_profile", row["profile_id"])
            row["swarm_residency"] = opts.get("swarm_residency")
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
        if self.phase != "ready" and not (queued and self.phase == "draining"):
            raise Fault(503, "The service is draining or stopping")
        row = self.model(mid)
        if row["state"] != "loaded":
            raise Fault(409, "Model unavailable: " + str(row["state"]))

    def capacity(self, mid):
        row = self.cache.get(mid, {})
        return max(1, min(12, int(row.get("capacity") or row.get("slots") or 1)))

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
        return self.engines.count_prompt(model_id, messages)

    def count_prompt_with_options(self, model_id, messages, options):
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
            if self.phase != "ready" and action not in ("unload", "suspend"):
                raise Fault(503, "Service shutdown in progress")
            row = self.model(mid)
            pref = self._prefs(mid)
            if mid in self.transitions:
                if (
                    action == "suspend"
                    and not automatic
                    and self.transitions[mid] in ("loading", "resuming", "suspending")
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
                return row
            if automatic and pref.get("manual"):
                raise Fault(409, "Manually suspended")
            if action in ("load", "resume", "switch"):
                if profile is None:
                    profile = pref.get("profile", "current")
                entries = {p["id"]: p for p in self.profiles(mid)}
                if profile not in entries:
                    raise Fault(404, "Unknown performance profile")
                self._register(mid)
                overrides = {} if profile == "current" else entries[profile]["settings"]
                self.engines.validate_settings(mid, overrides)
                if row["state"] == "loaded" and profile == row["active_profile_id"]:
                    return row
            else:
                overrides = {}
            marker = {
                "load": "loading",
                "unload": "unloading",
                "suspend": "suspending",
                "resume": "resuming",
                "switch": "loading",
            }
            if action not in marker:
                raise Fault(404, "Unknown model action")
            self.errors.pop(mid, None)
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
            if action in ("suspend", "unload", "switch"):
                self.jobs.stop(model=mid, emergency=False)
                release = self.engines.unload(mid) or {}
            if action in ("load", "resume", "switch"):
                self.engines.load(mid, overrides)
            self.refresh()
            with self.lock:
                obs = self.cache[mid]["state"]
                desired = action in ("load", "resume", "switch")
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
                elif desired:
                    pref.update(manual=True, pressure=False, want=True, profile=profile)
                proof = {
                    k: release[k]
                    for k in ("release_verified", "released_vram_mib", "release_detail")
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
                else:
                    self.errors[mid] = str(exc)[:1200]
                self.transitions.pop(mid, None)
                self.state.audit(
                    "model." + action, mid, {"success": False, "error": str(exc)[:1200]}
                )
            LOG.warning("%s %s failed: %s", mid, action, exc)

    def prepare_upgrade(self):
        with self.lock:
            if self.phase != "ready" or self.transitions or self.jobs.active:
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
                    if mid in self.transitions or mid in self.errors:
                        continue
                    pref = self._prefs(mid)
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
                and not self._prefs(r["id"]).get("manual")
            ]
            gpu_rows = backend.get("gpus", [])
            if isinstance(gpu_rows, dict):
                gpu_rows = list(gpu_rows.values())

            def gpu_matches(label, device):
                # Engine display labels can omit NVIDIA/GeForce while UUIDs and indices are exact.
                plain = lambda text: (
                    str(text)
                    .lower()
                    .replace("nvidia", "")
                    .replace("geforce", "")
                    .strip()
                )
                return any(
                    plain(label) == plain(device[k])
                    or (
                        k == "name"
                        and plain(label)
                        and plain(label) in plain(device[k])
                    )
                    for k in ("id", "name", "gpu", "uuid", "index")
                    if device.get(k) is not None
                )

            for device in gpu_rows:
                if device.get("free_mib") is None:
                    continue
                key = device.get("uuid") or device.get("name")
                free = device["free_mib"]
                prior = self.previous_gpu_free.get(key, free)
                self.previous_gpu_free[key] = free
                if free < 256 and prior - free >= 128 and not self.transitions:
                    for row in eligible:
                        if gpu_matches(row.get("gpu"), device):
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
                        r for r in eligible if gpu_matches(r.get("gpu"), observed)
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
                        and not pref.get("manual")
                        and mid not in self.errors
                        and time.time() >= pref.get("retry_at", 0)
                    ):
                        required = pref.get("reclaim_mib", 0)
                        observed = next(
                            (
                                g
                                for g in gpu_rows
                                if gpu_matches(self.cache[mid].get("gpu"), g)
                            ),
                            None,
                        )
                        if required and (
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
        self.finished.set()
        self.monitor.join(timeout=8)
        self.swarm.close()
        self.jobs.shutdown()
        self.catalog.close()
