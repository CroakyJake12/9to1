"""CPU-only contracts: no engine, GPU, service, or installed tuner is started."""
from contextlib import contextmanager
import hashlib
import json
from pathlib import Path
import struct
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from engine_profiles import (EngineError, apply_native_tuning, atomic_json,
                             native_config_identity, native_tuning_path,
                             validate_native_tuning)
from inference_management import InferenceManagement
from state import Fault, State


def gguf(path, *, adapter=False, arch="llama"):
    """Small structurally valid FP32 GGUF; no numerical inference is needed."""
    def string(value):
        data = value.encode()
        return struct.pack("<Q", len(data)) + data
    meta = {"general.architecture": arch, "general.type": "adapter" if adapter else "model"}
    if adapter:
        meta["adapter.type"] = "lora"
    names = ["blk.0.attn_q.weight.lora_a", "blk.0.attn_q.weight.lora_b"] if adapter else ["blk.0.attn_q.weight"]
    header = b"GGUF" + struct.pack("<IQQ", 3, len(names), len(meta))
    for key, value in meta.items():
        header += string(key) + struct.pack("<I", 8) + string(value)
    for i, name in enumerate(names):
        header += string(name) + struct.pack("<IQIQ", 1, 8, 0, i * 32)
    header += bytes((-len(header)) % 32)
    path.write_bytes(header + bytes(len(names) * 32))
    return path


class FakeEngines:
    def __init__(self, root, base, source):
        self.root = root / "engines"
        self.root.mkdir()
        self.base, self.source = base, source
        self.observed = {"local-base": {"id": "local-base", "state": "unloaded", "adapter_paths": []},
                         "native-base": {"id": "native-base", "state": "loaded"},
                         "fixed-alora": {"id": "fixed-alora", "state": "loaded", "active_adapter": "alora-trained"}}
    def _spec(self, mid):
        if mid == "fixed-alora":
            return {"id": mid, "engine": "Thunder", "kind": "managed-strata", "source": str(self.source),
                    "path": str(self.base), "base_model": "qwen-base", "fixed_adapter": "alora-trained",
                    "adapter_path": str(self.base.parent / "selected-experts.f32")}
        return {"id": mid, "path": str(self.base), "source": str(self.source),
                "engine": "Thunder / Strata" if mid == "native-base" else "Lightning / llama.cpp"}
    def status(self, mid):
        return dict(self.observed[mid])
    def adapter_capability(self, mid):
        return ({"supported": True, "backend": "llama.cpp"} if mid == "local-base" else
                {"supported": False, "reason": "This native backend has a fixed adapter profile."})


class FakeController:
    def __init__(self, root):
        self.state = State(root / "state")
        self.phase = "ready"
        self.base = gguf(root / "base.gguf")
        self.source = root / "source.json"
        self.config = {"model_name": "native-base", "exe": "/installed/custom-strata",
                       "args": ["--native", str(self.base), "--max-context", "524288", "--kv", "int8",
                                "--vram-reserve-mib", "1536", "--ram-keep-gib", "12", "--spec", "6"],
                       "env": {"STRATA_KVA": "0", "STRATA_LFUSE": "0"},
                       "vision": {"gpu": False, "max_tokens": 80}, "api_key": "private-test-key"}
        atomic_json(self.source, self.config)
        self.engines = FakeEngines(root, self.base, self.source)
        self.control = root / "control"
        for file in ("python/bin/python3.12", "strata.py", "strata_cli/tune_commands.py"):
            path = self.control / file
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("test fixture; never executed")
        self.calls, self.work = [], []
        self.result = {"outcome": "improved", "answers": {"class": "exact"},
                       "settings": {"args": {"--kv-resident": 65536}, "env": {}, "per_request": {}},
                       "message": "Measured faster at the existing context", "gain_pct": 7.1,
                       "base": {"decode_tok_s": 10}, "chosen": {"decode_tok_s": 10.71}}
        self.after_run = None
        self.fail_restore = False
        self.manager = InferenceManagement(self, control_dir=self.control, start_thread=self.work.append)
    def model(self, mid):
        if mid not in self.engines.observed:
            raise Fault(404, "Unknown installed model")
        return self.engines.status(mid)
    def status(self):
        return {"models": list(self.engines.observed.values())}
    def inference_tuning_capability(self, mid):
        return {"supported": mid == "native-base"}
    @contextmanager
    def inference_maintenance(self, mid, op_id, cancel):
        self.calls.append(("lease-enter", mid))
        try:
            yield {"gpu_lock": str(self.state.directory / "physical.lock")}
            # Real hook must call the central loader at this boundary. Exercise
            # the same source/overlay read to catch applying after restoration.
            effective = apply_native_tuning(self.engines.root, mid, self.config, source_config=self.config)
            self.calls.append(("restore", effective))
            if self.fail_restore:
                raise RuntimeError("The central reload failed")
        finally:
            self.calls.append(("lease-exit", mid))
    def run_inference_tuner(self, mid, op_id, command, **kwargs):
        self.calls.append(("runner", command, kwargs))
        if self.after_run:
            self.after_run()
        return {"returncode": 0, "stdout": json.dumps({"ok": True, "result": self.result})}
    def run_pending(self):
        self.work.pop(0)()
    def binding(self, name="adapter", specialist=None, scale=1.0, **manifest_changes):
        path = gguf(self.base.parent / (name + ".gguf"), adapter=True)
        manifest = {"schema": "dulche-lora-v1", "backend": "llama.cpp", "base_model_id": "local-base",
                    "base_files": [{"path": str(self.base), "sha256": hashlib.sha256(self.base.read_bytes()).hexdigest()}],
                    "adapter_sha256": hashlib.sha256(path.read_bytes()).hexdigest(), **manifest_changes}
        atomic_json(str(path) + ".dulche.json", manifest)
        return {"path": str(path), "scale": scale, "specialist_id": specialist}


class InferenceManagementTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.ctx = FakeController(Path(self.temp.name))
        self.manager = self.ctx.manager
    def tearDown(self):
        self.manager.close()
        self.ctx.state.db.close()
        self.temp.cleanup()
    def start(self):
        return self.manager.optimise("native-base", {"action": "start", "context_tokens": 524288, "goal": "speed"})
    def test_capabilities_are_per_backend_and_never_start_work(self):
        rows = {r["id"]: r for r in self.manager.capabilities()["models"]}
        self.assertTrue(rows["native-base"]["optimise"]["supported"])
        self.assertFalse(rows["native-base"]["adapters"]["supported"])
        self.assertTrue(rows["local-base"]["adapters"]["supported"])
        self.assertFalse(rows["fixed-alora"]["optimise"]["supported"])
        self.assertEqual([], self.ctx.calls)
        self.assertEqual([], self.ctx.work)
    def test_missing_guard_qualification_is_unavailable_before_admission(self):
        self.ctx.inference_tuning_capability = None
        with self.assertRaises(Fault) as failure:
            self.start()
        self.assertEqual(422, failure.exception.status)
        self.assertEqual([], self.ctx.work)
    def test_catalogue_lookup_refusal_preserves_other_model_capabilities(self):
        external = "catalogue-only"
        self.ctx.engines.observed[external] = {"id": external, "state": "external"}
        original_spec = self.ctx.engines._spec
        original_adapter = self.ctx.engines.adapter_capability
        for failure in (EngineError("Unknown engine"), OSError("Source disappeared"),
                        Fault(404, "Model is no longer registered")):
            with self.subTest(failure=type(failure).__name__):
                def spec(mid):
                    if mid == external:
                        raise failure
                    return original_spec(mid)
                def adapter(mid):
                    self.ctx.engines._spec(mid)
                    return original_adapter(mid)
                with patch.object(self.ctx.engines, "_spec", spec), \
                     patch.object(self.ctx.engines, "adapter_capability", adapter):
                    caps = self.manager.capabilities()
                rows = {row["id"]: row for row in caps["models"]}
                self.assertEqual(set(self.ctx.engines.observed), set(rows))
                self.assertTrue(caps["optimise"]["supported"])
                self.assertTrue(caps["adapters"]["supported"])
                self.assertTrue(rows["native-base"]["optimise"]["supported"])
                self.assertTrue(rows["local-base"]["adapters"]["supported"])
                for control in ("optimise", "adapters"):
                    self.assertEqual({"supported": False, "reason": str(failure)},
                                     rows[external][control])
        self.assertEqual([], self.ctx.calls)
        self.assertEqual([], self.ctx.work)
    def test_context_reduction_rejected_and_duplicate_start_coalesced(self):
        with self.assertRaises(Fault):
            self.manager.optimise("native-base", {"context_tokens": 32768})
        first = self.start()
        self.assertEqual(first["id"], self.start()["id"])
        self.assertEqual(1, len(self.ctx.work))
    def test_real_cli_contract_and_success_apply_before_central_restore(self):
        original = self.ctx.source.read_bytes()
        op = self.start()
        self.ctx.run_pending()
        done = self.manager.operation(op["id"])
        self.assertEqual("completed", done["state"])
        self.assertEqual(524288, done["context_tokens"])
        self.assertTrue(done["rollback_available"])
        command = next(call[1] for call in self.ctx.calls if call[0] == "runner")
        self.assertEqual(["tune", "auto"], command[2:4])
        self.assertIn("--no-save", command)
        self.assertNotIn("--no-quiet-check", command)
        snapshot = json.loads(Path(command[4]).read_text())
        self.assertEqual(self.ctx.config, snapshot)
        restored = next(call[1] for call in self.ctx.calls if call[0] == "restore")
        self.assertIn("65536", restored["args"])
        self.assertEqual(self.ctx.config["env"], restored["env"])
        self.assertEqual(original, self.ctx.source.read_bytes())
        self.assertNotIn("private-test-key", json.dumps(done))
    def test_restore_returns_previous_overlay_through_controller(self):
        op = self.start()
        self.ctx.run_pending()
        self.manager.optimise("native-base", {"action": "restore", "operation_id": op["id"]})
        self.ctx.run_pending()
        self.assertEqual("restored", self.manager.operation(op["id"])["state"])
        effective = apply_native_tuning(self.ctx.engines.root, "native-base", self.ctx.config)
        self.assertEqual(self.ctx.config, effective)

    def test_capability_discovery_recovers_current_rollback_after_panel_or_service_restart(self):
        op = self.start()
        self.ctx.run_pending()
        recovered = InferenceManagement(self.ctx, control_dir=self.ctx.control, start_thread=self.ctx.work.append)
        before = list(self.ctx.calls)
        row = next(row for row in recovered.capabilities()["models"] if row["id"] == "native-base")
        self.assertEqual(op["id"], row["latest_operation"]["id"])
        self.assertEqual(op["id"], row["rollback_operation"]["id"])
        self.assertTrue(row["rollback_operation"]["rollback_available"])
        self.assertNotIn("previous_overlay", row["rollback_operation"])
        self.assertNotIn("private-test-key", json.dumps(row))
        self.assertEqual(before, self.ctx.calls)
        self.assertEqual([], self.ctx.work)
        recovered.optimise("native-base", {"action": "restore", "operation_id": row["rollback_operation"]["id"]})
        self.ctx.run_pending()
        row = next(row for row in recovered.capabilities()["models"] if row["id"] == "native-base")
        self.assertIsNone(row["rollback_operation"])
        self.assertEqual("restored", row["latest_operation"]["state"])

    def test_discovery_keeps_failed_latest_attempt_separate_from_current_rollback(self):
        original = self.start()
        self.ctx.run_pending()
        newer = self.start()
        self.ctx.result = {"outcome": "base_failed", "message": "Measurement unavailable"}
        self.ctx.run_pending()
        caps = self.manager.capabilities()
        row = next(row for row in caps["models"] if row["id"] == "native-base")
        self.assertEqual(newer["id"], row["latest_operation"]["id"])
        self.assertEqual("failed", row["latest_operation"]["state"])
        self.assertEqual(original["id"], row["rollback_operation"]["id"])
        self.assertIsNone(caps["active_operation"])
        path = native_tuning_path(self.ctx.engines.root, "native-base")
        changed = json.loads(path.read_text())
        changed["operation_id"] = "0" * 32
        atomic_json(path, changed)
        row = next(row for row in self.manager.capabilities()["models"] if row["id"] == "native-base")
        self.assertIsNone(row["rollback_operation"])

    def test_discovery_identifies_active_maintenance_without_starting_another_trial(self):
        operation = self.start()
        caps = self.manager.capabilities()
        self.assertEqual(operation["id"], caps["active_operation"]["id"])
        self.assertEqual("queued", caps["active_operation"]["state"])
        self.assertEqual(1, len(self.ctx.work))
        self.assertEqual([], self.ctx.calls)
    def test_changed_source_or_guard_candidate_cannot_be_applied(self):
        for kind in ("source", "context", "environment", "precision", "nonexact", "perrequest"):
            with self.subTest(kind=kind):
                self.ctx.after_run = None
                self.ctx.result["answers"] = {"class": "exact"}
                self.ctx.result["settings"] = {"args": {}, "env": {}, "per_request": {}}
                if kind == "source":
                    self.ctx.after_run = lambda: atomic_json(self.ctx.source, {**self.ctx.config, "drafting": "newer-user-choice"})
                elif kind == "context":
                    self.ctx.result["settings"]["args"] = {"--max-context": 32768}
                elif kind == "environment":
                    self.ctx.result["settings"]["env"] = {"STRATA_KVA": "1"}
                elif kind == "precision":
                    self.ctx.result["settings"]["args"] = {"--kv": "fp16"}
                elif kind == "nonexact":
                    self.ctx.result["answers"] = {"class": "placement"}
                else:
                    self.ctx.result["settings"]["per_request"] = {"spec_min_p": 0.001}
                op = self.start()
                self.ctx.run_pending()
                self.assertEqual("failed", self.manager.operation(op["id"])["state"])
                self.assertFalse(native_tuning_path(self.ctx.engines.root, "native-base").exists())
                atomic_json(self.ctx.source, self.ctx.config)
    def test_user_stop_and_failed_reload_keep_previous_settings(self):
        op = self.start()
        self.ctx.after_run = lambda: self.ctx.state.set("stop_all", True)
        self.ctx.run_pending()
        self.assertEqual("cancelled", self.manager.operation(op["id"])["state"])
        self.assertFalse(native_tuning_path(self.ctx.engines.root, "native-base").exists())
        self.ctx.state.set("stop_all", False)
        self.ctx.after_run = None
        self.ctx.fail_restore = True
        op = self.start()
        self.ctx.run_pending()
        self.assertEqual("failed", self.manager.operation(op["id"])["state"])
        self.assertFalse(self.manager.operation(op["id"])["rollback_available"])
        self.assertEqual(self.ctx.config, apply_native_tuning(self.ctx.engines.root, "native-base", self.ctx.config))
    def test_restart_does_not_restart_or_claim_completion_of_lost_trial(self):
        op = self.start()
        recovered = InferenceManagement(self.ctx, control_dir=self.ctx.control, start_thread=self.ctx.work.append)
        self.assertEqual("interrupted", recovered.operation(op["id"])["state"])
        self.assertEqual(1, len(self.ctx.work))
        self.assertEqual([], self.ctx.calls)
    def test_newer_overlay_is_not_overwritten_by_restore(self):
        op = self.start()
        self.ctx.run_pending()
        path = native_tuning_path(self.ctx.engines.root, "native-base")
        saved = json.loads(path.read_text())
        saved["operation_id"] = "0" * 32
        atomic_json(path, saved)
        with self.assertRaises(Fault):
            self.manager.optimise("native-base", {"action": "restore", "operation_id": op["id"]})
        self.assertEqual(saved, json.loads(path.read_text()))
    def test_selection_is_persisted_lazy_and_specialist_overrides_default(self):
        default = self.ctx.binding()
        specialist = self.ctx.binding("reviewer", "reviewer-id", 0.5)
        self.manager.attach("local-base", default)
        self.manager.attach("local-base", specialist)
        self.assertEqual([], self.ctx.calls)
        self.assertTrue(all(row["state"] == "selected" for row in self.manager.adapters("local-base")["items"]))
        recovered = InferenceManagement(self.ctx, control_dir=self.ctx.control)
        paths = recovered.adapter_paths("local-base")
        self.ctx.engines.observed["local-base"].update(state="loaded", adapter_paths=paths)
        plain = recovered.prepare_request("local-base", {"messages": []})
        scoped = recovered.prepare_request("local-base", {"specialist_id": "reviewer-id", "messages": []})
        self.assertEqual([1.0, 0.0], [x["scale"] for x in plain["lora"]])
        self.assertEqual([0.0, 0.5], [x["scale"] for x in scoped["lora"]])
        self.assertNotIn("specialist_id", scoped)
        self.assertEqual([1.0, 0.0], [x["scale"] for x in recovered.prepare_request("local-base", {"specialist_id": "other"})["lora"]])
    def test_missing_loaded_adapter_never_dispatches_base_inference(self):
        self.manager.attach("local-base", self.ctx.binding())
        with self.assertRaises(Fault) as error:
            self.manager.prepare_request("local-base", {"messages": []})
        self.assertEqual(409, error.exception.status)
    def test_detach_zeros_existing_engine_slots_and_invalidates_admitted_snapshot(self):
        self.manager.attach("local-base", self.ctx.binding())
        self.ctx.engines.observed["local-base"].update(state="loaded", adapter_paths=self.manager.adapter_paths("local-base"))
        request = self.manager.snapshot_request("local-base", {"messages": []})
        binding = self.manager.adapters("local-base")["items"][0]
        self.manager.detach("local-base", {"binding_id": binding["binding_id"]})
        with self.assertRaises(Fault):
            self.manager.prepare_request("local-base", request)
        plain = self.manager.prepare_request("local-base", {"messages": []})
        self.assertEqual([{"id": 0, "scale": 0.0}], plain["lora"])
    def test_wrong_base_backend_scale_and_changed_artifact_fail_closed(self):
        for changes in ({"base_model_id": "different"}, {"backend": "peft"},
                        {"base_files": [{"path": str(self.ctx.base), "sha256": "0" * 64}]}):
            with self.subTest(changes=changes), self.assertRaises(Fault):
                self.manager.attach("local-base", self.ctx.binding(**changes))
        body = self.ctx.binding()
        for scale in (True, float("nan"), float("inf"), 17):
            with self.subTest(scale=scale), self.assertRaises(Fault):
                self.manager.attach("local-base", {**body, "scale": scale})
        self.manager.attach("local-base", body)
        self.ctx.base.write_bytes(self.ctx.base.read_bytes() + b"changed")
        self.assertEqual("incompatible", self.manager.adapters("local-base")["items"][0]["state"])
        with self.assertRaises(EngineError):
            self.manager.adapter_paths("local-base")
    def test_fixed_native_adapter_is_visible_but_not_detachable(self):
        result = self.manager.adapters("fixed-alora")
        self.assertFalse(result["supported"])
        self.assertEqual("active", result["items"][0]["state"])
        self.assertFalse(result["items"][0]["detachable"])
        with self.assertRaises(Fault):
            self.manager.detach("fixed-alora", {"binding_id": result["items"][0]["binding_id"]})
        self.assertEqual({"messages": []}, self.manager.prepare_request("fixed-alora", {"messages": [], "specialist_id": "reviewer"}))
    def test_raw_lora_indices_cannot_bypass_binding_scope(self):
        with self.assertRaises(Fault):
            self.manager.prepare_request("local-base", {"lora": [{"id": 0, "scale": 1}]})
    def test_overlay_receipt_is_pinned_to_exact_source_configuration(self):
        cfg = self.ctx.config
        path = native_tuning_path(self.ctx.engines.root, "native-base")
        atomic_json(path, {"state": "active", "model": "native-base", "base_identity": native_config_identity(cfg),
                           "settings": {"args": {"--kv-resident": 65536}}})
        with self.assertRaises(EngineError):
            apply_native_tuning(self.ctx.engines.root, "native-base", {**cfg, "drafting": "different"})
        with self.assertRaises(EngineError):
            validate_native_tuning(cfg, {"args": {"--vram-reserve-mib": 0}})

    def route(self, path, method="GET", body=None):
        from daemon import Handler
        handler = Handler.__new__(Handler)
        handler.server = SimpleNamespace(controller=self.ctx)
        self.ctx.inference = self.manager
        handler.path, handler.command, handler.headers = path, method, {}
        handler.connection = SimpleNamespace(getsockopt=lambda *args: struct.pack("3i", 1, 1, 1))
        handler._body = lambda: body or {}
        handler.send_json = lambda status, value: (status, value)
        return handler.handle_api()

    def test_http_inference_routes_use_the_owned_manager_and_preserve_async_receipt(self):
        status, caps = self.route("/v1/inference/capabilities")
        self.assertEqual(200, status)
        self.assertTrue(caps["optimise"]["supported"])
        status, op = self.route("/v1/models/native-base/optimise", "POST", {"context_tokens": 524288})
        self.assertEqual((202, "queued"), (status, op["state"]))
        self.assertEqual((200, op), self.route("/v1/inference/operations/" + op["id"]))
        status, fixed = self.route("/v1/models/fixed-alora/adapters")
        self.assertEqual((200, "active"), (status, fixed["items"][0]["state"]))
        status, attached = self.route("/v1/models/local-base/adapters", "POST", self.ctx.binding())
        self.assertEqual((201, "selected"), (status, attached["items"][0]["state"]))
        status, detached = self.route("/v1/models/local-base/adapters/detach", "POST",
                                      {"binding_id": attached["items"][0]["binding_id"]})
        self.assertEqual((200, []), (status, detached["items"]))
        self.assertEqual([], self.ctx.calls)

    def test_http_capabilities_include_catalogue_only_model_without_service_error(self):
        external = "local-c1f51015c3da7bed0e122ff3"
        self.ctx.engines.observed[external] = {"id": external, "state": "external"}
        original_spec = self.ctx.engines._spec
        original_adapter = self.ctx.engines.adapter_capability
        def spec(mid):
            if mid == external:
                raise EngineError("Unknown engine model: " + mid)
            return original_spec(mid)
        def adapter(mid):
            self.ctx.engines._spec(mid)
            return original_adapter(mid)
        with patch.object(self.ctx.engines, "_spec", spec), \
             patch.object(self.ctx.engines, "adapter_capability", adapter):
            status, caps = self.route("/v1/inference/capabilities")
        self.assertEqual(200, status)
        rows = {row["id"]: row for row in caps["models"]}
        self.assertFalse(rows[external]["adapters"]["supported"])
        self.assertIn(external, rows[external]["adapters"]["reason"])
        self.assertTrue(rows["local-base"]["adapters"]["supported"])
        self.assertTrue(rows["native-base"]["optimise"]["supported"])
        self.assertEqual([], self.ctx.calls)
        self.assertEqual([], self.ctx.work)

    def test_http_training_window_route_and_draining_gate_are_preserved(self):
        self.ctx.training_windows = SimpleNamespace(handle=lambda *args: (202, {"args": args}))
        status, reply = self.route("/v1/resources/training-window/acquire?owner=cycle", "POST", {"owner": "cycle", "models": ["native-base"]})
        self.assertEqual(202, status)
        self.assertEqual("acquire", reply["args"][1])
        self.assertEqual({"owner": ["cycle"]}, reply["args"][3])
        self.ctx.phase = "draining"
        with self.assertRaises(Fault) as failure:
            self.route("/v1/models/local-base/adapters", "POST", {})
        self.assertEqual(503, failure.exception.status)


if __name__ == "__main__":
    unittest.main()
