"""Managed-serving custody tests: CPU fixtures, never real service actions."""
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import subprocess
import tempfile
import threading
import unittest
from unittest import mock

from engine_profiles import EngineError, GPU_RTX, atomic_json
import managed_serving


MODEL = "qwen3.8-flash-next-alora-test"
UNIT = "alora-test-serving.service"
GUARD = "alora-test-thermal.service"


class ManagedServingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.posts = []
        self.health_calls = []
        owner = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *_):
                pass

            def reply(self, value):
                raw = json.dumps(value).encode()
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(raw)))
                self.end_headers()
                self.wfile.write(raw)

            def do_GET(self):
                owner.health_calls.append(self.path)
                self.reply({"status": "ok"})

            def do_POST(self):
                body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
                owner.posts.append((self.path, dict(self.headers), body))
                self.reply({"choices": [{"message": {"role": "assistant", "content": "fixture"}}]})

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=lambda: self.server.serve_forever(poll_interval=0.01), daemon=True)
        self.thread.start()
        self.port = self.server.server_address[1]
        self.source = self.root / "source.json"
        self.launcher = self.root / "launch.py"
        self.ready = self.root / "ready.json"
        self.config = {"model_name": MODEL, "host": "127.0.0.1", "port": self.port, "api_key": "cpu-fixture-key",
                       "args": ["--native", "/models/base.strata", "--max-context", "524288",
                                "--alora-merged", "/models/adapter.safetensors"]}
        atomic_json(self.source, self.config)
        self.launcher.write_text("# CPU test registration; never executed.\n")
        self.launcher.chmod(0o700)
        self.profile = {"kind": "strata-native-service", "unit": UNIT, "guard_units": [GUARD],
                        "source": str(self.source), "launcher": str(self.launcher), "ready_file": str(self.ready),
                        "source_sha256": self.digest(self.source), "launcher_sha256": self.digest(self.launcher),
                        "gpu": GPU_RTX, "capabilities": ["text", "streaming", "vision", "tools", "structured_output"],
                        "fixed_adapter": "fixture-adapter", "adapter_path": "/models/adapter.safetensors"}
        self.save_registry()
        self.set_ready()
        self.group = "/user.slice/" + UNIT
        self.cgroup_root = self.root / "cgroups"
        self.cgroup = self.cgroup_root / self.group.lstrip("/")
        self.cgroup.mkdir(parents=True)
        (self.cgroup / "cgroup.procs").write_text("")
        self.cgroup_patch = mock.patch.object(managed_serving, "CGROUP_ROOT", self.cgroup_root)
        self.cgroup_patch.start()
        read_text = Path.read_text
        def membership(path, *args, **kwargs):
            if path == Path(f"/proc/{os.getpid()}/cgroup"):
                return "0::" + self.group + "\n"
            return read_text(path, *args, **kwargs)
        self.membership_patch = mock.patch.object(Path, "read_text", membership)
        self.membership_patch.start()
        self.active = {"ActiveState": "active", "MainPID": str(os.getpid()), "ControlGroup": self.group, "Result": "success"}
        self.inactive = {"ActiveState": "inactive", "MainPID": "0", "ControlGroup": self.group, "Result": "success"}
        self.service_state = dict(self.active)
        self.guard_state = {"ActiveState": "active", "MainPID": "123"}
        self.units = mock.patch.object(managed_serving, "unit_state", side_effect=self.unit)
        self.units.start()
        self.listener = mock.patch("engines._listener", side_effect=lambda pid, port: pid == os.getpid() and port == self.port)
        self.listener_mock = self.listener.start()
        self.bridge = managed_serving.ManagedServing(self.root)

    @staticmethod
    def digest(path):
        return hashlib.sha256(path.read_bytes()).hexdigest()

    def unit(self, name):
        if name == UNIT: return dict(self.service_state)
        if name == GUARD: return dict(self.guard_state)
        raise AssertionError("Unexpected service unit: " + name)

    def save_registry(self):
        atomic_json(self.root / "managed-serving.json", {"schema": "9to1.managed-serving/v1", "models": {MODEL: self.profile}})

    def set_ready(self, **changes):
        atomic_json(self.ready, {"pid": os.getpid(), "model_id": MODEL, "bound_port": self.port,
                                "bound_host": "127.0.0.1", "api_state": "ready", "model_state": "ready", **changes})

    def tearDown(self):
        self.membership_patch.stop()
        self.cgroup_patch.stop()
        self.listener.stop()
        self.units.stop()
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=2)
        self.temp.cleanup()

    def test_rich_generation_preserves_tools_images_and_output_contract(self):
        payload = {"model": "untrusted-request-model", "stream": False,
                   "messages": [{"role": "user", "content": [{"type": "text", "text": "Describe"},
                                {"type": "image_url", "image_url": {"url": "data:image/png;base64,AA=="}}]},
                                {"role": "assistant", "tool_calls": [{"id": "call", "type": "function", "function": {"name": "inspect", "arguments": "{}"}}]},
                                {"role": "tool", "tool_call_id": "call", "content": "done"}],
                   "tools": [{"type": "function", "function": {"name": "inspect", "parameters": {"type": "object"}}}],
                   "tool_choice": "auto", "response_format": {"type": "json_object"}, "reasoning_effort": "high"}
        with self.bridge.generate(MODEL, payload, stream=True) as response:
            self.assertEqual(json.loads(response.read())["choices"][0]["message"]["content"], "fixture")
        route, headers, body = self.posts[-1]
        self.assertEqual(route, "/v1/chat/completions")
        self.assertEqual(body, {**payload, "model": MODEL, "stream": True})
        self.assertEqual(headers["Authorization"], "Bearer cpu-fixture-key")
        self.listener_mock.assert_called_with(os.getpid(), self.port)
        self.assertIn("/health", self.health_calls)

    def test_ready_requires_exact_model_host_port_state_cgroup_and_listener(self):
        self.assertEqual(self.bridge.status(MODEL)["state"], "loaded")
        for changes in ({"model_id": "another-model"}, {"bound_host": "0.0.0.0"},
                        {"bound_port": self.port + 1}, {"api_state": "starting"},
                        {"model_state": "loading"}, {"pid": True}):
            self.set_ready(**changes)
            with self.subTest(changes=changes):
                self.assertEqual(self.bridge.status(MODEL)["state"], "loading")
        self.set_ready()
        self.service_state["ControlGroup"] = "/a-different-service"
        self.assertEqual(self.bridge.status(MODEL)["state"], "loading")
        self.service_state = dict(self.active)
        self.listener_mock.side_effect = lambda *_: False
        self.assertEqual(self.bridge.status(MODEL)["state"], "loading")

    def test_load_starts_only_exact_unit_and_waits_for_acknowledged_ready_state(self):
        self.service_state = dict(self.inactive)
        def run(command, **kwargs):
            self.assertEqual(command, ["systemctl", "--user", "start", UNIT])
            self.assertNotIn("--no-block", command)
            self.assertTrue(kwargs["check"])
            self.service_state = dict(self.active)
            return subprocess.CompletedProcess(command, 0)
        with mock.patch.object(managed_serving.subprocess, "run", side_effect=run) as called:
            result = self.bridge.load(MODEL, {"context": 524288})
        self.assertEqual(result["state"], "loaded")
        called.assert_called_once()

    def test_inactive_independent_guard_blocks_load_without_service_action(self):
        self.service_state = dict(self.inactive)
        self.guard_state = {"ActiveState": "failed"}
        with mock.patch.object(managed_serving.subprocess, "run") as run:
            with self.assertRaisesRegex(EngineError, "guard is not active"):
                self.bridge.load(MODEL)
            run.assert_not_called()

    def test_source_and_launcher_pin_changes_refuse_load(self):
        for path in (self.source, self.launcher):
            original = path.read_bytes()
            path.write_bytes(original + b"\n")
            try:
                with self.subTest(path=path.name), mock.patch.object(managed_serving.subprocess, "run") as run:
                    with self.assertRaisesRegex(EngineError, "changed"):
                        self.bridge.load(MODEL)
                    run.assert_not_called()
            finally:
                path.write_bytes(original)

    def test_changed_source_still_unloads_exact_registered_unit(self):
        self.source.write_text(self.source.read_text() + "\n")
        def run(command, **kwargs):
            self.assertEqual(command, ["systemctl", "--user", "stop", UNIT])
            self.service_state = dict(self.inactive)
            return subprocess.CompletedProcess(command, 0)
        with mock.patch.object(managed_serving.subprocess, "run", side_effect=run) as called:
            result = self.bridge.unload(MODEL)
        called.assert_called_once()
        self.assertEqual(result["state"], "unloaded")
        self.assertIn("registration needs review", result["error"])

    def test_surviving_main_pid_prevents_false_unload_receipt(self):
        self.service_state = {**self.inactive, "MainPID": "999"}
        with mock.patch.object(managed_serving.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)):
            with self.assertRaisesRegex(EngineError, "not settled"):
                self.bridge.unload(MODEL)

    def test_inactive_unit_with_surviving_child_retains_pre_stop_cgroup(self):
        child_group = self.cgroup / "detached-child"
        child_group.mkdir()
        (child_group / "cgroup.procs").write_text("999\n")
        def run(command, **kwargs):
            self.assertEqual(command, ["systemctl", "--user", "stop", UNIT])
            self.service_state = {**self.inactive, "ControlGroup": ""}
            return subprocess.CompletedProcess(command, 0)
        with mock.patch.object(managed_serving.subprocess, "run", side_effect=run):
            with self.assertRaisesRegex(EngineError, "still contains processes"):
                self.bridge.unload(MODEL)

    def test_escaped_cgroup_cannot_produce_a_release_receipt(self):
        self.service_state = {**self.active, "ControlGroup": "/../../outside/" + UNIT}
        def run(command, **kwargs):
            self.service_state = {**self.inactive, "ControlGroup": ""}
            return subprocess.CompletedProcess(command, 0)
        with mock.patch.object(managed_serving.subprocess, "run", side_effect=run):
            with self.assertRaisesRegex(EngineError, "escaped"):
                self.bridge.unload(MODEL)

    def test_unreadable_existing_cgroup_fails_closed(self):
        (self.cgroup / "cgroup.procs").unlink()
        self.service_state = dict(self.inactive)
        with mock.patch.object(managed_serving.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)):
            with self.assertRaisesRegex(EngineError, "could not be inspected"):
                self.bridge.unload(MODEL)

    def test_already_inactive_unit_with_no_cgroup_can_settle(self):
        self.service_state = {**self.inactive, "ControlGroup": ""}
        with mock.patch.object(managed_serving.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)):
            self.assertEqual(self.bridge.unload(MODEL)["state"], "unloaded")

    def test_start_timeout_cleans_exact_unit_before_propagating_failure(self):
        self.service_state = dict(self.inactive)
        calls = []
        def run(command, **kwargs):
            calls.append(command)
            if command[2] == "start":
                self.service_state = dict(self.active)
                raise subprocess.TimeoutExpired(command, 10)
            self.assertEqual(command, ["systemctl", "--user", "stop", UNIT])
            self.service_state = dict(self.inactive)
            return subprocess.CompletedProcess(command, 0)
        with mock.patch.object(managed_serving.subprocess, "run", side_effect=run):
            with self.assertRaises(subprocess.TimeoutExpired): self.bridge.load(MODEL)
        self.assertEqual([command[2] for command in calls], ["start", "stop"])

    def test_start_failure_keeps_uncertain_cleanup_visible(self):
        self.service_state = dict(self.inactive)
        def run(command, **kwargs):
            if command[2] == "start":
                self.service_state = dict(self.active)
                raise subprocess.CalledProcessError(1, command)
            self.service_state = {**self.inactive, "MainPID": "999"}
            return subprocess.CompletedProcess(command, 0)
        with mock.patch.object(managed_serving.subprocess, "run", side_effect=run):
            with self.assertRaisesRegex(EngineError, "cleanup.*unverified"):
                self.bridge.load(MODEL)

    def test_profile_settings_cannot_silently_reduce_context_or_change_adapter(self):
        self.assertEqual(self.bridge.validate_settings(MODEL, {"context_length": 524288})["context"], 524288)
        for settings in ({"context": 8192}, {"gpu": "Other GPU"}, {"adapter": "different"}):
            with self.subTest(settings=settings), self.assertRaises(EngineError):
                self.bridge.validate_settings(MODEL, settings)

    def test_unsafe_unit_registration_cannot_trigger_stop(self):
        self.profile["unit"] = "unrelated-user-service.service"
        self.save_registry()
        with mock.patch.object(managed_serving.subprocess, "run") as run:
            with self.assertRaises(EngineError): self.bridge.unload(MODEL)
            run.assert_not_called()


if __name__ == "__main__":
    unittest.main()
