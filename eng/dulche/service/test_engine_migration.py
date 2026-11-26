"""Managed route and request-local LoRA boundary fixtures; no subprocess or GPU is used."""

import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

import engines
from engine_profiles import CPU, INKLING, INKLING_POOL, PRIMARY, V100, EngineError


class EngineMigrationTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="dulche-engine-route-")
        self.addCleanup(temporary.cleanup)
        self.path = Path(temporary.name)
        self.manager = engines.EngineManager(self.path / "state")
        self.managed = mock.Mock()
        self.managed.handles.side_effect = lambda mid: mid == "saved-alora"
        self.managed.ids.return_value = ["saved-alora"]
        self.managed.spec.return_value = {"id": "saved-alora", "gpu": "RTX 5080"}
        self.managed.status.return_value = {"id": "saved-alora", "state": "unloaded", "managed": True}
        self.manager.managed = self.managed
        self.spec = {"id": "fixture", "path": str(self.path / "fixture.gguf"), "gpu": "CPU",
                     "engine": "Lightning / llama.cpp", "context": 2048, "max_context": 4096,
                     "slots": 1, "bytes": 32, "quantization": "Q4_K_M"}
        Path(self.spec["path"]).write_bytes(b"GGUF" + b"0" * 32)

    def test_managed_load_holds_physical_admission_and_preserves_settings(self):
        settings = {"context": 524288}
        with mock.patch.object(self.manager, "_change", return_value=contextlib.nullcontext()) as lock, \
             mock.patch.object(self.manager, "_admit_devices") as admit:
            result = self.manager.load("saved-alora", settings)
        lock.assert_called_once_with(physical=True)
        admit.assert_called_once_with(self.managed.spec.return_value, settings)
        self.managed.load.assert_called_once_with("saved-alora", settings)
        self.assertIs(result, self.managed.load.return_value)

    def test_loaded_managed_route_does_not_claim_resources_twice(self):
        self.managed.status.return_value["state"] = "loaded"
        with mock.patch.object(self.manager, "_change", return_value=contextlib.nullcontext()), \
             mock.patch.object(self.manager, "_admit_devices") as admit:
            self.manager.load("saved-alora", {})
        admit.assert_not_called()

    def test_managed_unload_and_token_count_delegate(self):
        with mock.patch.object(self.manager, "_change", return_value=contextlib.nullcontext()) as lock:
            self.assertIs(self.manager.unload("saved-alora"), self.managed.unload.return_value)
        lock.assert_called_once_with(physical=True)
        messages = [{"role": "user", "content": [{"type": "text", "text": "hello"}]}]
        self.manager.count_prompt("saved-alora", messages, {"reasoning_effort": "high"})
        self.managed.count_prompt.assert_called_once_with("saved-alora", messages, {"reasoning_effort": "high"})
        self.assertEqual(self.managed.status.return_value, self.manager.status("saved-alora"))

    def test_managed_generation_uses_binding_resolution_and_forwards_rich_payload(self):
        payload = {"messages": [{"role": "user", "content": [{"type": "image_url", "image_url": {"url": "data:image/png;base64,AA=="}}]}],
                   "tools": [{"type": "function", "function": {"name": "lookup"}}], "reasoning_effort": "high"}
        management = self.manager.inference_management = mock.Mock()
        management.prepare_request.return_value = {**payload, "metadata": "resolved"}
        response = io.BytesIO(b"fixture")
        self.managed.generate.return_value = contextlib.nullcontext(response)
        cancel = object()
        with self.manager.generate("saved-alora", payload, stream=True, cancel_event=cancel) as reply:
            self.assertIs(response, reply)
        management.prepare_request.assert_called_once_with("saved-alora", payload)
        self.managed.generate.assert_called_once_with("saved-alora", management.prepare_request.return_value, True, cancel)

    def test_only_qualified_local_cpu_backend_advertises_adapters(self):
        for mid in ("saved-alora", PRIMARY, INKLING, INKLING_POOL, V100):
            self.assertFalse(self.manager.adapter_capability(mid)["supported"])
        with mock.patch.object(self.manager, "_spec", return_value=self.spec), \
             mock.patch.object(self.manager, "status", return_value={"gpu": "CPU"}):
            self.assertTrue(self.manager.adapter_capability("fixture")["supported"])
        with mock.patch.object(self.manager, "_spec", return_value=self.spec), \
             mock.patch.object(self.manager, "status", return_value={"gpu": "Tesla V100"}):
            self.assertFalse(self.manager.adapter_capability("fixture")["supported"])

    def test_adapter_inventory_is_loaded_disabled_and_recorded_in_order(self):
        first, second = str(self.path / "a.gguf"), str(self.path / "b.gguf")
        self.manager.inference_management = mock.Mock()
        self.manager.inference_management.adapter_paths.return_value = [first, second]
        for path in (first, second):
            Path(path).write_bytes(b"fixture-adapter")
        executable = self.path / "llama-server"
        executable.write_bytes(b"fixture")
        socket = mock.MagicMock()
        socket.__enter__.return_value.getsockname.return_value = ("127.0.0.1", 12345)
        with mock.patch.object(engines, "_memory", return_value={"MemAvailable": 100 * 1024**2}), \
             mock.patch.object(self.manager, "_admit_devices"), \
             mock.patch.object(engines, "dense_files", return_value=(executable, Path(self.spec["path"]))), \
             mock.patch.object(engines, "read_signature", return_value=Path(self.spec["path"])), \
             mock.patch.object(engines.socket, "socket", return_value=socket), \
             mock.patch.object(engines, "_command") as command:
            self.manager._launch(self.spec, 2048, 2, "experimental", {})
        argv = command.call_args.args[0]
        self.assertEqual(["--lora", first, "--lora", second, "--lora-init-without-apply"], argv[-5:])
        record = json.loads(self.manager._record("fixture").read_text())
        self.assertEqual([first, second], record["adapter_paths"])

    def test_adapter_memory_is_budgeted_before_launch(self):
        adapter = self.path / "adapter.gguf"
        with adapter.open("wb") as file:
            file.truncate(1024**2)
        self.manager.inference_management = mock.Mock()
        self.manager.inference_management.adapter_paths.return_value = [str(adapter)]
        executable = self.path / "llama-server"
        executable.write_bytes(b"fixture")
        with mock.patch.object(engines, "_memory", return_value={"MemAvailable": 3 * 1024**2 + 1024}), \
             mock.patch.object(self.manager, "_admit_devices"), \
             mock.patch.object(engines, "dense_files", return_value=(executable, Path(self.spec["path"]))), \
             mock.patch.object(engines, "read_signature", return_value=Path(self.spec["path"])), \
             mock.patch.object(engines, "_command") as command:
            with self.assertRaisesRegex(EngineError, "selected LoRA adapters"):
                self.manager._launch(self.spec, 2048, 2, "experimental", {})
        command.assert_not_called()

    def test_non_cpu_adapter_launch_is_rejected(self):
        self.manager.inference_management = mock.Mock()
        self.manager.inference_management.adapter_paths.return_value = [str(self.path / "a.gguf")]
        with self.assertRaisesRegex(EngineError, "cannot load"):
            self.manager._adapter_paths("fixture", "Tesla V100")

    def test_local_generation_forwards_request_local_adapter_scales(self):
        payload = {"messages": [{"role": "user", "content": "hello"}]}
        resolved = {**payload, "lora": [{"id": 0, "scale": 0}, {"id": 1, "scale": 0.75}]}
        management = self.manager.inference_management = mock.Mock()
        management.prepare_request.return_value = resolved
        response = mock.Mock(status=200)
        connection = mock.Mock()
        connection.getresponse.return_value = response
        with mock.patch.object(self.manager, "_spec", return_value=self.spec), \
             mock.patch.object(self.manager, "_observe", return_value=({"state": "loaded"}, (12345, "fixture", "dulche", 1), {})), \
             mock.patch.object(engines.http.client, "HTTPConnection", return_value=connection):
            with self.manager.generate("fixture", payload):
                pass
        body = json.loads(connection.request.call_args.args[2])
        self.assertEqual(resolved["lora"], body["lora"])
        self.assertNotIn("lora", payload)


if __name__ == "__main__":
    unittest.main()
