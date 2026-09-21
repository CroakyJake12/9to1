from __future__ import annotations

import hashlib
import json
import pathlib
import struct
import sys
import tempfile
import threading
import time
import unittest
from contextlib import ExitStack
from unittest import mock

RUNTIME = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(RUNTIME))

import broker  # noqa: E402


@unittest.skipUnless(broker.HAS_UNIX_SOCKETS, "requires Unix-domain socket support")
class BrokerIntegrationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        root = pathlib.Path(self.temp.name)
        self.runtime_dir = root / "runtime"
        self.data_home = root / "data"
        self.model_root = self.data_home / "models"
        self.manifest_root = self.model_root / "manifests"
        self.blob_root = self.model_root / "blobs" / "sha256"
        self.broker_socket = self.runtime_dir / "inference.sock"
        self.worker_socket = self.runtime_dir / "worker.sock"
        self.worker_home = self.runtime_dir / "worker-home"
        self.fake_server = RUNTIME / "tests/fake_llama_server.py"
        self.fake_session_ready = root / "fake-session-ready"
        self.fake_cancel_proof = root / "fake-cancel-proof"

        payload = b"GGUF" + struct.pack("<I", 3) + b"integration-payload"
        digest = hashlib.sha256(payload).hexdigest()
        relative = broker.canonical_blob_relative(digest)
        blob = self.blob_root / relative
        blob.parent.mkdir(parents=True)
        blob.write_bytes(payload)
        self.manifest_root.mkdir(parents=True)
        (self.manifest_root / "integration-model.json").write_text(
            json.dumps({
                "id": "integration-model",
                "displayName": "Integration Model",
                "sha256": digest,
                "size": len(payload),
                "blob": relative,
                "license": "test-only",
                "source": "generated-test-fixture",
                "ggufVersions": [3],
            }),
            encoding="utf-8",
        )
        (self.manifest_root / "replacement-model.json").write_text(
            json.dumps({
                "id": "replacement-model",
                "displayName": "Replacement Model",
                "sha256": digest,
                "size": len(payload),
                "blob": relative,
                "license": "test-only",
                "source": "generated-test-fixture",
                "ggufVersions": [3],
            }),
            encoding="utf-8",
        )

        self.stack = ExitStack()
        self.stack.enter_context(mock.patch.dict("os.environ", {
            "HAVEN_FAKE_SESSION_READY": str(self.fake_session_ready),
            "HAVEN_FAKE_CANCEL_PROOF": str(self.fake_cancel_proof),
        }))
        replacements = {
            "RUNTIME_DIR": self.runtime_dir,
            "DATA_HOME": self.data_home,
            "MODEL_ROOT": self.model_root,
            "MANIFEST_ROOT": self.manifest_root,
            "BLOB_ROOT": self.blob_root,
            "BROKER_SOCKET": self.broker_socket,
            "WORKER_SOCKET": self.worker_socket,
            "WORKER_HOME": self.worker_home,
            "LLAMA_SERVER": self.fake_server,
            "START_TIMEOUT_SECONDS": 3.0,
        }
        for name, value in replacements.items():
            self.stack.enter_context(mock.patch.object(broker, name, value))
        self.stack.enter_context(mock.patch.object(broker, "WORKER", broker.Worker()))
        self.stack.enter_context(mock.patch.object(broker, "DULCHE_RUNTIME", broker.DulcheRuntime(broker.WORKER)))
        broker.ensure_private_directory(self.runtime_dir)
        broker.ensure_private_directory(self.worker_home)
        self.server = broker.ThreadingUnixServer(str(self.broker_socket), broker.Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, kwargs={"poll_interval": 0.05}, daemon=True)
        self.thread.start()

    def tearDown(self) -> None:
        try:
            broker.DULCHE_RUNTIME.stop()
        finally:
            self.server.shutdown()
            self.server.server_close()
            self.thread.join(timeout=2)
            self.broker_socket.unlink(missing_ok=True)
            self.stack.close()
            self.temp.cleanup()

    def _request(self, method: str, path: str, value: dict | None = None) -> tuple[int, bytes]:
        connection = broker.UnixHTTPConnection(self.broker_socket, timeout=5)
        body = None if value is None else json.dumps(value, separators=(",", ":")).encode("utf-8")
        headers = {} if body is None else {"Content-Type": "application/json"}
        try:
            connection.request(method, path, body=body, headers=headers)
            response = connection.getresponse()
            return response.status, response.read()
        finally:
            connection.close()

    def _load(self) -> None:
        status, body = self._request("POST", "/v1/models/integration-model/load")
        self.assertEqual(200, status, body)
        self.assertTrue(broker.WORKER.ready)

    def test_spawn_health_load_stream_and_unload_are_wired_end_to_end(self) -> None:
        status, body = self._request("GET", "/health")
        self.assertEqual(200, status)
        health = json.loads(body)
        self.assertFalse(health["workerReady"])
        self.assertEqual("single-broker-owned-slot-workers", health["runtime"]["topology"])
        self.assertFalse(health["runtime"]["perAppServers"])

        status, body = self._request("GET", "/v1/provider")
        self.assertEqual(200, status)
        provider_runtime = json.loads(body)["runtime"]
        self.assertEqual("upstream-resumable-stream-delete", provider_runtime["cancellation"]["primary"])
        self.assertFalse(provider_runtime["perAppServers"])

        self._load()
        self.assertTrue(self.worker_socket.exists())
        self.assertEqual(0o600, self.worker_socket.stat().st_mode & 0o777)

        status, body = self._request("GET", "/v1/models")
        models = {model["id"]: model for model in json.loads(body)["models"]}
        self.assertEqual(200, status)
        self.assertEqual("llamacpp:integration-model", models["integration-model"]["key"])
        self.assertTrue(models["integration-model"]["loaded"])

        status, stream = self._request("POST", "/v1/chat/completions", {
            "request_id": "integration-stream-1",
            "messages": [{"role": "user", "content": "hello"}],
        })
        self.assertEqual(200, status)
        self.assertIn(b"hello-from-fake", stream)
        self.assertIn(b"[DONE]", stream)

        status, body = self._request("POST", "/v1/models/integration-model/unload")
        self.assertEqual(200, status, body)
        self.assertFalse(broker.WORKER.ready)
        self.assertFalse(self.worker_socket.exists())

    def test_slots_context_tools_and_permission_resolution_are_broker_owned(self) -> None:
        status, body = self._request("POST", "/v1/models/integration-model/load", {"slot": 0, "context": 1024})
        self.assertEqual(200, status, body)
        status, body = self._request("POST", "/v1/models/replacement-model/load", {"slot": 0, "context": 2048})
        self.assertEqual(200, status, body)
        self.assertEqual("replacement-model", json.loads(body)["model"])

        status, body = self._request("POST", "/v1/models/integration-model/load", {"slot": 1, "context": 1024})
        self.assertEqual(200, status, body)
        self.assertTrue((self.runtime_dir / "llamacpp-worker-1.sock").exists())

        status, body = self._request("POST", "/v1/slots/1/context", {"context": 4096})
        self.assertEqual(202, status, body)
        self.assertEqual("scheduled", json.loads(body)["status"])

        broker.Dulche.OnPermissionRequested(lambda request: True if request.action == "dulche.run_code" else None)
        status, body = self._request("POST", "/v1/slots/1/tools/dulche.run_code", {"source": "print('native-tool')"})
        self.assertEqual(200, status, body)
        result = json.loads(body)
        self.assertEqual("completed", result["status"])
        self.assertIn("native-tool", result["output"])

        outcome: dict[str, tuple[int, bytes]] = {}

        def request_permission() -> None:
            outcome["response"] = self._request("POST", "/v1/slots/1/permissions", {
                "permission": "endpoint.access",
                "action": "https://example.test/api",
            })

        thread = threading.Thread(target=request_permission, daemon=True)
        thread.start()
        deadline = time.monotonic() + 3
        request_id = ""
        while time.monotonic() < deadline:
            status, body = self._request("GET", "/v1/permissions/requests")
            pending = json.loads(body)["requests"]
            if pending:
                request_id = pending[0]["id"]
                break
            time.sleep(0.01)
        self.assertTrue(request_id, "permission request was not broadcast")

        status, body = self._request("POST", f"/v1/permissions/{request_id}/resolve", {"approved": True})
        self.assertEqual(200, status, body)
        self.assertTrue(json.loads(body)["resolved"])
        thread.join(timeout=2)
        self.assertFalse(thread.is_alive())
        status, body = outcome["response"]
        self.assertEqual(200, status, body)
        self.assertEqual("approved", json.loads(body)["decision"])

    def test_cancel_endpoint_actively_unblocks_a_blocked_worker_stream(self) -> None:
        self._load()
        outcome: dict[str, object] = {}

        def run_blocked_chat() -> None:
            try:
                outcome["response"] = self._request("POST", "/v1/chat/completions", {
                    "request_id": "integration-cancel-1",
                    "messages": [{"role": "user", "content": "__BLOCK_UNTIL_CANCELLED__"}],
                })
            except BaseException as exc:  # captured for assertion in the test thread
                outcome["error"] = exc

        chat_thread = threading.Thread(target=run_blocked_chat, daemon=True)
        chat_thread.start()
        deadline = time.monotonic() + 3
        active = False
        while time.monotonic() < deadline:
            with broker.ACTIVE_LOCK:
                active = "integration-cancel-1" in broker.ACTIVE_REQUESTS
            if active:
                break
            time.sleep(0.01)
        self.assertTrue(active, "blocked request never became active")

        while time.monotonic() < deadline and not self.fake_session_ready.exists():
            time.sleep(0.01)
        self.assertTrue(self.fake_session_ready.exists(), "worker never registered the resumable stream session")

        started = time.monotonic()
        status, body = self._request("POST", "/v1/requests/integration-cancel-1/cancel")
        self.assertEqual(202, status, body)
        cancellation = json.loads(body)["cancellation"]
        self.assertEqual("upstream-stream-delete", cancellation["mode"])
        self.assertFalse(cancellation["transportFallback"])
        self.assertFalse(cancellation["workerCompletionConfirmed"])
        chat_thread.join(timeout=2)
        self.assertFalse(chat_thread.is_alive(), "cancel did not unblock the broker stream")
        self.assertLess(time.monotonic() - started, 2.0)
        self.assertNotIn("error", outcome)
        with broker.ACTIVE_LOCK:
            self.assertNotIn("integration-cancel-1", broker.ACTIVE_REQUESTS)
        self.assertEqual("integration-cancel-1", self.fake_cancel_proof.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
