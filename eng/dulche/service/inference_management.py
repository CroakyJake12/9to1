"""Durable inference tuning and request-scoped LoRA selection.

This module owns configuration receipts, never model processes. A real tune is
enabled only when the controller supplies a guarded maintenance lease and a
supervised runner. The lease must reserve physical devices, reject admitted work,
honour newer stop intent, and restore through EngineManager (which reads the
overlay) on exit. An unqualified backend reports why it cannot perform an action.
"""
from __future__ import annotations

from copy import deepcopy
import hashlib
import json
import math
import os
from pathlib import Path
import re
import threading
import time

from engine_profiles import (CONTROL_MOUNT, EngineError, atomic_json,
                             apply_native_tuning, native_config_identity,
                             native_tuning_path, strata_home, validate_native_tuning)
from model_formats import gguf_shard, shard_paths
from state import Fault, identity

ACTIVE = {"queued", "running", "validating", "applying", "restoring"}
OPERATIONS = "inference:operations"
BINDINGS = "inference:adapters:"
MAX_JSON = 4 * 1024 * 1024
MAX_ADAPTERS = 16


def _json(path):
    path = Path(path)
    if path.is_symlink() or not path.is_file() or path.stat().st_size > MAX_JSON:
        raise ValueError("Missing, linked or oversized configuration: " + path.name)
    value = json.loads(path.read_text())
    if not isinstance(value, dict):
        raise ValueError("Configuration must be a JSON object")
    return value


def _fingerprint(path):
    path = Path(path)
    st = path.stat()
    if not path.is_file():
        raise ValueError("A regular artifact file is required")
    return {"path": str(path.resolve()), "device": st.st_dev, "inode": st.st_ino,
            "size": st.st_size, "mtime_ns": st.st_mtime_ns, "ctime_ns": st.st_ctime_ns}


def _scope(value):
    if value is None:
        return None
    if not isinstance(value, str) or not value.strip() or len(value) > 200 or any(ord(c) < 32 for c in value):
        raise Fault(400, "Specialist ID must be a nonempty identifier")
    return value


class InferenceManagement:
    def __init__(self, controller, *, control_dir=None, start_thread=None):
        self.ctx, self.state = controller, controller.state
        self.lock = self.state.lock
        self.root = self.state.directory / "inference-management"
        self.root.mkdir(mode=0o700, parents=True, exist_ok=True)
        self.control_dir = Path(control_dir) if control_dir else None
        self._start_thread = start_thread or self._thread
        self._cancel = {}
        self._hashes = {}
        # A restarted daemon must not claim that a lost measurement completed or
        # silently restart GPU work. The previous overlay and evidence stay intact.
        with self.lock:
            for op_id in self.state.get(OPERATIONS, []):
                op = self.state.get("inference:operation:" + op_id)
                if op and op.get("state") in ACTIVE:
                    op.update(state="interrupted", error="The service restarted during this operation; review or restore its saved settings.",
                              updated=time.time())
                    self._save_operation(op)

    @staticmethod
    def _thread(work):
        threading.Thread(target=work, name="dulche-inference-management", daemon=True).start()

    def _model(self, mid):
        if not isinstance(mid, str) or not mid or len(mid) > 250:
            raise Fault(400, "Invalid model ID")
        return self.ctx.model(mid)

    def _engine_capability(self, mid):
        method = getattr(self.ctx.engines, "adapter_capability", None)
        if not callable(method):
            return {"supported": False, "reason": "This engine has no qualified request-scoped LoRA loader."}
        answer = method(mid)
        if not isinstance(answer, dict) or answer.get("supported") is not True:
            return {"supported": False, "reason": (answer if isinstance(answer, dict) else {}).get("reason", "This backend cannot attach additional LoRA adapters.")}
        if answer.get("backend") != "llama.cpp":
            return {"supported": False, "reason": "This adapter backend has no qualified binding validator."}
        return {**answer, "supported": True, "format": "gguf-lora",
                "requires": "Exact base identity in a .dulche.json sidecar; selected adapters load on the next model start."}

    def _control(self):
        controls = strata_home() / "components/control"
        if self.control_dir is not None:
            control = self.control_dir.resolve()
        else:
            active = _json(controls / "active.json")
            folder = active.get("folder")
            if not isinstance(folder, str):
                raise ValueError("Installed Strata Control has no active version")
            control = (controls / folder).resolve()
            if control.parent != controls.resolve():
                raise ValueError("Invalid installed Strata Control path")
        interpreter, entry = control / "python/bin/python3.12", control / "strata.py"
        if not all(p.is_file() for p in (interpreter, entry, control / "strata_cli/tune_commands.py")):
            raise ValueError("The installed Strata optimiser or its Python runtime is unavailable")
        return control, interpreter, entry

    def _target(self, mid):
        row = self._model(mid)
        spec = self.ctx.engines._spec(mid)
        if spec.get("kind") == "managed-strata":
            raise ValueError("This verified managed serving route cannot apply optimisation overlays without changing its pinned launch profile")
        source = spec.get("source") or row.get("source")
        if not source or "strata" not in str(spec.get("engine", "")).lower():
            raise ValueError("Strata optimisation is available only for a registered native Strata source profile")
        source = Path(source).expanduser()
        cfg = _json(source)
        if cfg.get("model_name") != mid:
            raise ValueError("The source profile does not identify this exact model")
        if cfg.get("arch") in ("inkling2_loop", "hy4") or mid.startswith(("inkling-", "hy4-")):
            raise ValueError("This installed Strata optimiser does not support the selected model architecture")
        args = cfg.get("args", [])
        if not isinstance(args, list) or not all(isinstance(x, str) for x in args):
            raise ValueError("Invalid native source arguments")
        if args.count("--native") != 1 or args.count("--max-context") != 1:
            raise ValueError("The native source must declare one unambiguous model and context")
        try:
            native = Path(args[args.index("--native") + 1]).resolve(strict=True)
            context = int(args[args.index("--max-context") + 1])
        except (ValueError, IndexError, OSError) as exc:
            raise ValueError("The source profile must declare its installed model and context") from exc
        if str(native) != str(Path(spec["path"]).resolve()) or context <= 0:
            raise ValueError("The source profile disagrees with the selected model identity")
        validate_native_tuning(cfg, {})
        control, interpreter, entry = self._control()
        def relocate(value):
            if isinstance(value, dict):
                return {k: relocate(v) for k, v in value.items()}
            if isinstance(value, list):
                return [relocate(v) for v in value]
            return CONTROL_MOUNT.sub(str(control), value) if isinstance(value, str) else value
        runtime = relocate(cfg)
        runtime = apply_native_tuning(self.ctx.engines.root, mid, runtime, source_config=cfg)
        return {"source": source, "config": cfg, "runtime": runtime, "context": context,
                "identity": native_config_identity(cfg), "control": control,
                "interpreter": interpreter, "entry": entry}

    def _optimise_capability(self, mid):
        try:
            target = self._target(mid)
        except (OSError, ValueError, KeyError, RuntimeError) as exc:
            return {"supported": False, "reason": str(exc)}
        for method in ("inference_maintenance", "run_inference_tuner"):
            if not callable(getattr(self.ctx, method, None)):
                return {"supported": False, "reason": "The installed optimiser requires a qualified guarded maintenance runner for this backend.",
                        "context_tokens": target["context"]}
        qualifier = getattr(self.ctx, "inference_tuning_capability", None)
        if not callable(qualifier):
            return {"supported": False, "reason": "The controller has not qualified tuning supervision for this backend."}
        qualified = qualifier(mid)
        if not isinstance(qualified, dict) or qualified.get("supported") is not True:
            return {"supported": False, "reason": (qualified if isinstance(qualified, dict) else {}).get("reason", "Tuning supervision is unavailable for this backend.")}
        return {"supported": True, "context_tokens": target["context"], "minutes": 20,
                "goal": "speed", "method": "strata-tune-auto"}

    def capabilities(self):
        rows = []
        for row in self.ctx.status().get("models", []):
            mid = row["id"]
            rows.append({"id": mid, "optimise": self._optimise_capability(mid),
                         "adapters": self._engine_capability(mid)})
        def aggregate(key):
            if any(row[key]["supported"] for row in rows):
                return {"supported": True}
            return {"supported": False, "reason": "No installed model currently has a qualified " + ("optimisation runner." if key == "optimise" else "LoRA attachment backend.")}
        return {"optimise": aggregate("optimise"), "adapters": aggregate("adapters"), "models": rows}

    def _save_operation(self, op):
        self.state.set("inference:operation:" + op["id"], op)

    def _update(self, op_id, **updates):
        with self.lock:
            op = self.state.get("inference:operation:" + op_id)
            op.update(updates, updated=time.time())
            self._save_operation(op)
            return op

    def operation(self, op_id):
        if not isinstance(op_id, str) or not re.fullmatch(r"[0-9a-f]{32}", op_id):
            raise Fault(404, "Unknown inference operation")
        op = self.state.get("inference:operation:" + op_id)
        if not op:
            raise Fault(404, "Unknown inference operation")
        public = {k: deepcopy(v) for k, v in op.items() if k not in ("previous_overlay", "candidate_overlay", "source_identity")}
        if op["state"] in ACTIVE:
            progress = self.root / op_id / "status.json"
            try:
                value = _json(progress)
                public["progress"] = {k: value[k] for k in ("stage", "message", "minutes_left") if k in value}
            except (OSError, ValueError, TypeError):
                pass
        return public

    def optimise(self, mid, body):
        self._model(mid)
        if not isinstance(body, dict) or set(body) - {"action", "context_tokens", "goal", "operation_id"}:
            raise Fault(400, "Invalid optimisation request")
        action = body.get("action", "start")
        if action not in ("start", "restore") or body.get("goal", "speed") != "speed":
            raise Fault(400, "Select start or restore with the speed goal")
        if action == "restore":
            return self._restore(mid, body.get("operation_id"))
        capability = self._optimise_capability(mid)
        if not capability["supported"]:
            raise Fault(422, capability["reason"])
        target = self._target(mid)
        context = body.get("context_tokens")
        if type(context) is not int or context != target["context"]:
            raise Fault(409, "Optimisation must keep the selected source context of " + str(target["context"]) + " tokens")
        if body.get("operation_id") is not None:
            raise Fault(400, "An operation ID is only used when restoring saved settings")
        with self.lock:
            ids = self.state.get(OPERATIONS, [])
            for op_id in ids:
                old = self.state.get("inference:operation:" + op_id, {})
                if old.get("state") in ACTIVE:
                    if old.get("model") == mid and old.get("context_tokens") == context:
                        return self.operation(op_id)
                    raise Fault(409, "Another inference optimisation operation owns the maintenance slot")
            op_id = identity()
            path = native_tuning_path(self.ctx.engines.root, mid)
            previous = _json(path) if path.exists() else None
            op = {"id": op_id, "model": mid, "state": "queued", "goal": "speed",
                  "context_tokens": context, "rollback_available": False,
                  "previous_overlay": previous, "source_identity": target["identity"],
                  "created": time.time(), "updated": time.time(),
                  "message": "Waiting for guarded inference maintenance."}
            self._save_operation(op)
            self.state.set(OPERATIONS, [*ids, op_id])
            self._cancel[op_id] = threading.Event()
        self._start_thread(lambda: self._run(op_id))
        return self.operation(op_id)

    def _check_stop(self, op_id):
        cancel = self._cancel[op_id]
        if cancel.is_set() or self.state.get("stop_all", False) or self.ctx.phase != "ready":
            raise InterruptedError("Optimisation stopped; the previous settings were retained")

    @staticmethod
    def _result(output):
        if hasattr(output, "returncode"):
            code, stdout = output.returncode, output.stdout
        elif isinstance(output, dict) and "returncode" in output:
            code, stdout = output["returncode"], output.get("stdout", "")
        else:
            raise ValueError("The supervised tuner did not return a process result")
        if not isinstance(stdout, str) or len(stdout.encode()) > MAX_JSON:
            raise ValueError("The tuner result was missing or exceeded its output limit")
        lines = [line for line in stdout.splitlines() if line.strip()]
        if not lines:
            raise ValueError("The tuner returned no result")
        payload = json.loads(lines[-1])
        if code != 0 or payload.get("ok") is not True or not isinstance(payload.get("result"), dict):
            error = payload.get("error", {})
            raise ValueError(str(error.get("message", "Strata did not complete a successful measurement"))[:500])
        return payload["result"]

    def _overlay(self, op, target, result):
        if result.get("outcome") not in ("improved", "default_confirmed"):
            raise ValueError(result.get("message") or "Strata could not establish a reliable performance result")
        if (result.get("answers") or {}).get("class") != "exact":
            raise ValueError("Only a measured exact-answer candidate may be applied automatically")
        settings = validate_native_tuning(target["runtime"], result.get("settings", {}))
        previous = op.get("previous_overlay") or {}
        existing = previous.get("settings", {}) if previous.get("state") == "active" else {}
        merged = {"args": {**existing.get("args", {}), **settings["args"]},
                  "env": {**existing.get("env", {}), **settings["env"]}, "per_request": {}}
        merged = validate_native_tuning(target["config"], merged)
        return {"schema": 1, "state": "active", "model": op["model"], "operation_id": op["id"],
                "base_identity": target["identity"], "context_tokens": op["context_tokens"], "settings": merged}

    def _run(self, op_id):
        op = self.state.get("inference:operation:" + op_id)
        path = native_tuning_path(self.ctx.engines.root, op["model"])
        applied = False
        try:
            self._check_stop(op_id)
            target = self._target(op["model"])
            if target["identity"] != op["source_identity"]:
                raise ValueError("The selected source configuration changed before the optimisation began")
            work = self.root / op_id
            work.mkdir(mode=0o700, exist_ok=True)
            cfg_path, status = work / "source.json", work / "status.json"
            atomic_json(cfg_path, target["runtime"])
            self._update(op_id, state="running", message="Measuring the current configuration and exact-answer candidates.")
            with self.ctx.inference_maintenance(op["model"], op_id, self._cancel[op_id]) as lease:
                if not isinstance(lease, dict) or not lease.get("gpu_lock"):
                    raise ValueError("The controller did not supply a shared physical-admission lock")
                command = [str(target["interpreter"]), str(target["entry"]), "tune", "auto", str(cfg_path),
                           "--mode", "quick", "--goal", "speed", "--use", "coding", "--time", "20", "--no-save",
                           "--gpu-lock", str(lease["gpu_lock"]), "--work-dir", str(work), "--status-file", str(status), "--output", "json"]
                self._check_stop(op_id)
                output = self.ctx.run_inference_tuner(op["model"], op_id, command, cwd=str(target["control"]),
                                                     cancel_event=self._cancel[op_id], timeout_seconds=2700)
                self._check_stop(op_id)
                result = self._result(output)
                atomic_json(work / "result.json", result)
                self._update(op_id, state="validating")
                current = _json(target["source"])
                if native_config_identity(current) != target["identity"]:
                    raise ValueError("The source profile changed during measurement; the candidate was not applied")
                now = _json(path) if path.exists() else None
                if now != op.get("previous_overlay"):
                    raise ValueError("The selected optimisation changed during measurement; its newer settings were retained")
                candidate = self._overlay(op, target, result)
                summary = {k: result[k] for k in ("outcome", "message", "gain_pct", "base", "chosen", "values", "noise", "pairs") if k in result}
                self._update(op_id, state="applying", candidate_overlay=candidate, result=summary,
                             rollback_available=True, message="Applying the measured settings through the model controller.")
                self._check_stop(op_id)
                atomic_json(path, candidate)
                applied = True
            self._update(op_id, state="completed", message=result.get("message") or "Optimisation completed at the existing context length.")
        except Exception as exc:
            rollback_available = False
            if applied:
                # Compare-and-restore: an unrelated newer change is never overwritten.
                try:
                    current = _json(path)
                    if current.get("operation_id") == op_id:
                        atomic_json(path, op.get("previous_overlay") or {"schema": 1, "model": op["model"], "state": "restored"})
                except (OSError, ValueError):
                    rollback_available = True
            self._update(op_id, state="cancelled" if isinstance(exc, InterruptedError) else "failed",
                         rollback_available=rollback_available, error=str(exc)[:700],
                         message="The candidate was not accepted; previous settings and measurement receipts are retained.")
        finally:
            self._cancel.pop(op_id, None)

    def _restore(self, mid, op_id):
        op = self.operation(op_id)
        if op["model"] != mid:
            raise Fault(409, "The operation belongs to a different model")
        if op["state"] in ACTIVE:
            raise Fault(409, "Wait for the running operation before restoring")
        if not op.get("rollback_available"):
            raise Fault(409, "This operation has no saved optimisation to restore")
        if not callable(getattr(self.ctx, "inference_maintenance", None)):
            raise Fault(422, "Guarded model maintenance is unavailable")
        with self.lock:
            receipt = _json(native_tuning_path(self.ctx.engines.root, mid))
            if receipt.get("operation_id") != op_id or receipt.get("state") != "active":
                raise Fault(409, "A different configuration is current; this restore would overwrite newer settings")
            for other in self.state.get(OPERATIONS, []):
                if self.state.get("inference:operation:" + other, {}).get("state") in ACTIVE:
                    raise Fault(409, "Another inference operation owns maintenance")
            self._cancel[op_id] = threading.Event()
            self._update(op_id, state="restoring", error=None, message="Restoring the previous inference settings.")
        self._start_thread(lambda: self._run_restore(op_id))
        return self.operation(op_id)

    def _run_restore(self, op_id):
        op = self.state.get("inference:operation:" + op_id)
        path = native_tuning_path(self.ctx.engines.root, op["model"])
        restored = False
        try:
            self._check_stop(op_id)
            with self.ctx.inference_maintenance(op["model"], op_id, self._cancel[op_id]):
                current = _json(path)
                if current.get("operation_id") != op_id or current.get("state") != "active":
                    raise ValueError("The current optimisation changed before restoration")
                self._check_stop(op_id)
                atomic_json(path, op.get("previous_overlay") or {"schema": 1, "model": op["model"], "state": "restored"})
                restored = True
            self._update(op_id, state="restored", rollback_available=False, message="The previous inference settings were restored.")
        except Exception as exc:
            self._update(op_id, state="cancelled" if isinstance(exc, InterruptedError) else "failed",
                         rollback_available=not restored, error=str(exc)[:700],
                         message="The previous configuration was selected but model restoration failed." if restored else "Restoration did not change the selected configuration.")
        finally:
            self._cancel.pop(op_id, None)

    def close(self):
        for cancel in tuple(self._cancel.values()):
            cancel.set()

    def _hash(self, path):
        mark = _fingerprint(path)
        key = json.dumps(mark, sort_keys=True)
        if key not in self._hashes:
            digest = hashlib.sha256()
            with Path(path).open("rb") as stream:
                for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                    digest.update(chunk)
            if _fingerprint(path) != mark:
                raise ValueError("Artifact changed during validation")
            self._hashes[key] = digest.hexdigest()
        return self._hashes[key], mark

    def _base_files(self, mid):
        spec = self.ctx.engines._spec(mid)
        return [Path(p).resolve(strict=True) for p in shard_paths(Path(spec["path"]))]

    def _validate_adapter(self, mid, supplied):
        if not isinstance(supplied, str) or not supplied or any(ord(c) < 32 for c in supplied):
            raise Fault(400, "Select an existing absolute GGUF LoRA path")
        path = Path(supplied).expanduser()
        if not path.is_absolute() or path.is_symlink() or path.suffix.casefold() != ".gguf":
            raise Fault(422, "This backend requires a regular GGUF LoRA adapter; PEFT directories require a compatible serving backend")
        path = path.resolve(strict=True)
        # llama's --lora option is CSV; reject ambiguous paths rather than pass an
        # unintended second artifact to the engine parser.
        if "," in str(path):
            raise Fault(422, "The installed LoRA loader cannot address paths containing commas")
        meta, names, _ = gguf_shard(path)
        if meta.get("general.type") != "adapter" or meta.get("adapter.type") != "lora":
            raise Fault(422, "The selected GGUF is not a LoRA adapter")
        left = {n[:-7] for n in names if n.endswith(".lora_a")}
        right = {n[:-7] for n in names if n.endswith(".lora_b")}
        if not left or left != right or len(names) != len(left) * 2:
            raise Fault(422, "The LoRA adapter must contain matched A/B tensor pairs")
        sidecar = Path(str(path) + ".dulche.json")
        manifest_fingerprint = _fingerprint(sidecar)
        manifest = _json(sidecar)
        if (manifest.get("schema") != "dulche-lora-v1" or manifest.get("backend") != "llama.cpp"
                or manifest.get("base_model_id") != mid):
            raise Fault(422, "The adapter manifest does not identify this exact base model and backend")
        bases = self._base_files(mid)
        declared = manifest.get("base_files")
        if not isinstance(declared, list) or len(declared) != len(bases):
            raise Fault(422, "The adapter must declare every exact base shard and its SHA-256")
        fingerprints, base_names, base_meta = [], set(), {}
        for base, claim in zip(bases, declared):
            if not isinstance(claim, dict) or claim.get("path") != str(base):
                raise Fault(422, "The adapter base shard path does not match the selected model")
            digest, fingerprint = self._hash(base)
            if claim.get("sha256") != digest:
                raise Fault(422, "The adapter was built for different base weights")
            fingerprints.append(fingerprint)
            metadata, tensors, _ = gguf_shard(base)
            if not base_meta:
                base_meta = metadata
            base_names.update(tensors)
        if not left.issubset(base_names):
            raise Fault(422, "The adapter targets tensors that are absent from the exact base")
        if meta.get("general.architecture") != base_meta.get("general.architecture"):
            raise Fault(422, "The adapter architecture does not match the selected base")
        digest, fingerprint = self._hash(path)
        if manifest.get("adapter_sha256") != digest:
            raise Fault(422, "The adapter artifact does not match its exact identity manifest")
        if _fingerprint(sidecar) != manifest_fingerprint:
            raise Fault(422, "The adapter identity manifest changed during validation")
        return {"path": str(path), "artifact": fingerprint, "artifact_sha256": digest,
                "base_files": fingerprints, "manifest": manifest_fingerprint}

    def _current(self, mid, binding):
        if _fingerprint(binding["path"]) != binding["artifact"]:
            return False
        if [_fingerprint(path) for path in self._base_files(mid)] != binding["base_files"]:
            return False
        return _fingerprint(binding["manifest"]["path"]) == binding["manifest"]

    def attach(self, mid, body):
        self._model(mid)
        cap = self._engine_capability(mid)
        if not cap["supported"]:
            raise Fault(422, cap["reason"])
        if not isinstance(body, dict) or set(body) - {"path", "scale", "specialist_id"}:
            raise Fault(400, "Invalid adapter binding request")
        scale = body.get("scale", 1.0)
        if type(scale) not in (float, int) or not math.isfinite(scale) or not -16 <= scale <= 16:
            raise Fault(400, "Adapter scale must be a finite number from -16 to 16")
        specialist = _scope(body.get("specialist_id"))
        try:
            verified = self._validate_adapter(mid, body.get("path"))
        except (OSError, ValueError, KeyError) as exc:
            raise Fault(422, "Adapter validation failed: " + str(exc)) from exc
        with self.lock:
            rows = self.state.get(BINDINGS + mid, [])
            matching = next((r for r in rows if r.get("specialist_id") == specialist), None)
            if matching and matching["path"] == verified["path"] and matching["scale"] == scale and matching["artifact"] == verified["artifact"]:
                return self.adapters(mid)
            rows = [r for r in rows if r.get("specialist_id") != specialist]
            if len(rows) >= MAX_ADAPTERS:
                raise Fault(409, "This model already has the maximum number of specialist adapter selections")
            rows.append({**verified, "binding_id": identity(), "scale": float(scale),
                         "specialist_id": specialist, "created": time.time()})
            self.state.set(BINDINGS + mid, rows)
            self.state.audit("adapter-selected", mid, {"binding_id": rows[-1]["binding_id"], "specialist_id": specialist})
        return self.adapters(mid)

    def detach(self, mid, body):
        self._model(mid)
        if not isinstance(body, dict) or set(body) != {"binding_id"} or not isinstance(body["binding_id"], str):
            raise Fault(400, "Specify the adapter binding ID to detach")
        if body["binding_id"] == "fixed:" + mid:
            raise Fault(422, "This adapter is part of the verified managed model profile; select a different model to use other weights")
        with self.lock:
            rows = self.state.get(BINDINGS + mid, [])
            kept = [r for r in rows if r["binding_id"] != body["binding_id"]]
            if len(kept) == len(rows):
                raise Fault(404, "Unknown adapter binding")
            self.state.set(BINDINGS + mid, kept)
            self.state.audit("adapter-detached", mid, body)
        return self.adapters(mid)

    def adapters(self, mid):
        row = self._model(mid)
        cap = self._engine_capability(mid)
        observed = self.ctx.engines.status(mid)
        loaded = observed.get("adapter_paths", []) if observed.get("state") == "loaded" else []
        items, active = [], None
        spec = self.ctx.engines._spec(mid)
        if spec.get("fixed_adapter") and spec.get("adapter_path"):
            enabled = observed.get("state") == "loaded" and bool(observed.get("active_adapter"))
            fixed_id = "fixed:" + mid
            items.append({"binding_id": fixed_id, "path": spec["adapter_path"], "scale": 1.0,
                          "specialist_id": None, "state": "active" if enabled else "selected",
                          "active": enabled, "detachable": False, "base_model": spec.get("base_model"),
                          "message": "Included in the verified model profile; additional adapter attachment is unsupported by this backend."})
            if enabled:
                active = fixed_id
        for binding in self.state.get(BINDINGS + mid, []):
            try:
                compatible = cap["supported"] and self._current(mid, binding)
            except (OSError, ValueError, KeyError):
                compatible = False
            # The default binding is effective for subsequent ordinary requests;
            # specialist bindings remain explicitly scoped to those requests.
            enabled = bool(compatible and binding["path"] in loaded and binding.get("specialist_id") is None)
            if enabled:
                active = binding["binding_id"]
            item = {k: binding[k] for k in ("binding_id", "path", "scale", "specialist_id")}
            item.update(active=enabled, state="incompatible" if not compatible else "active" if enabled else "selected")
            item["message"] = ("Adapter or exact base identity changed; attach a compatible artifact again." if not compatible else
                               "Applied to model requests." if enabled else "Selected; used for its scope after the model loads this adapter.")
            items.append(item)
        return {"items": items, "active_binding_id": active, "supported": cap["supported"], **({"reason": cap["reason"]} if not cap["supported"] else {})}

    def adapter_paths(self, mid):
        if not self._engine_capability(mid)["supported"]:
            return []
        paths = []
        for binding in self.state.get(BINDINGS + mid, []):
            if not self._current(mid, binding):
                raise EngineError("A selected LoRA adapter or its exact base changed; reattach it before starting")
            if binding["path"] not in paths:
                paths.append(binding["path"])
        return paths

    def resolve_binding(self, mid, specialist_id=None):
        specialist_id = _scope(specialist_id)
        rows = self.state.get(BINDINGS + mid, [])
        if specialist_id is not None:
            selected = next((r for r in rows if r.get("specialist_id") == specialist_id), None)
            if selected:
                return selected
        return next((r for r in rows if r.get("specialist_id") is None), None)

    def snapshot_request(self, mid, payload):
        result = dict(payload)
        if ("specialist_id" in result and "dulche_specialist_id" in result
                and result["specialist_id"] != result["dulche_specialist_id"]):
            raise Fault(400, "Conflicting specialist identities")
        specialist = result.get("specialist_id", result.get("dulche_specialist_id"))
        selected = self.resolve_binding(mid, specialist)
        result["adapter_binding_id"] = selected["binding_id"] if selected else ""
        return result

    def prepare_request(self, mid, payload):
        result = dict(payload)
        if ("specialist_id" in result and "dulche_specialist_id" in result
                and result["specialist_id"] != result["dulche_specialist_id"]):
            raise Fault(400, "Conflicting specialist identities")
        specialist = result.pop("specialist_id", result.pop("dulche_specialist_id", None))
        selected = self.resolve_binding(mid, specialist)
        if "adapter_binding_id" in result:
            pinned = result.pop("adapter_binding_id")
            if pinned != (selected["binding_id"] if selected else ""):
                raise Fault(409, "The selected adapter changed after this request was admitted; retry with the current selection")
        if "lora" in result:
            raise Fault(422, "Select a model or specialist adapter binding instead of raw runtime LoRA indices")
        cap = self._engine_capability(mid)
        if not cap["supported"]:
            if selected:
                raise Fault(422, cap["reason"])
            return result
        observed = self.ctx.engines.status(mid)
        loaded = observed.get("adapter_paths", [])
        if not isinstance(loaded, list) or not all(isinstance(p, str) for p in loaded):
            raise Fault(409, "The engine has not reported its loaded adapter inventory")
        scales = [{"id": index, "scale": 0.0} for index in range(len(loaded))]
        if selected:
            try:
                current = self._current(mid, selected)
            except (OSError, ValueError, KeyError):
                current = False
            if not current:
                raise Fault(422, "The selected adapter or exact base identity changed")
            if selected["path"] not in loaded:
                raise Fault(409, "The selected adapter is waiting for a model reload; no base-only inference was dispatched")
            scales[loaded.index(selected["path"])]["scale"] = selected["scale"]
        # Every loaded adapter is explicit, including zero after detach. Never
        # mutate global adapter scales shared by another specialist's request.
        result["lora"] = scales
        return result
