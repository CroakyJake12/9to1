from __future__ import annotations

import pathlib
import sys
import threading
import unittest
from types import SimpleNamespace
from unittest import mock

RUNTIME = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(RUNTIME))

import broker  # noqa: E402


class FakeWorker:
    def __init__(
        self,
        slot: int = 0,
        worker_socket: pathlib.Path | None = None,
        worker_home: pathlib.Path | None = None,
        context_limit: int = broker.CONTEXT_LIMIT,
    ) -> None:
        self.slot = broker.validate_slot(slot)
        self.worker_socket = worker_socket or broker.WORKER_SOCKET
        self.worker_home = worker_home or broker.WORKER_HOME
        self.context_limit = context_limit
        self.pending_context: int | None = None
        self.model: SimpleNamespace | None = None
        self.ready = False
        self.load_calls: list[tuple[str, int, bool]] = []
        self.unload_count = 0

    def load(self, manifest: SimpleNamespace, context_limit: int | None = None, force: bool = False) -> None:
        context = self.context_limit if context_limit is None else broker.validate_context_limit(context_limit)
        self.load_calls.append((manifest.model_id, context, force))
        self.model = manifest
        self.context_limit = context
        self.pending_context = None
        self.ready = True

    def unload(self) -> None:
        self.unload_count += 1
        self.model = None
        self.ready = False
        self.pending_context = None

    def set_context(self, context_limit: int) -> int:
        self.pending_context = broker.validate_context_limit(context_limit)
        return self.pending_context


class DulcheRuntimeTests(unittest.TestCase):
    def setUp(self) -> None:
        self.manifests = {
            "alpha": SimpleNamespace(model_id="alpha"),
            "beta": SimpleNamespace(model_id="beta"),
        }
        self.worker_patch = mock.patch.object(broker, "Worker", FakeWorker)
        self.worker_patch.start()
        self.manifest_patch = mock.patch.object(broker, "load_manifests", return_value=self.manifests)
        self.manifest_patch.start()
        self.runtime = broker.DulcheRuntime(FakeWorker())

    def tearDown(self) -> None:
        self.runtime.stop()
        self.manifest_patch.stop()
        self.worker_patch.stop()

    def test_blank_slot_uses_next_available_and_allocates_private_worker_state(self) -> None:
        alpha = self.runtime.load_model("alpha", context=1024)
        beta = self.runtime.load_model("beta", context=2048)

        self.assertEqual(0, alpha.slot)
        self.assertEqual(1, beta.slot)
        self.assertEqual("alpha", self.runtime.worker_for_slot(0).model.model_id)
        self.assertEqual("beta", self.runtime.worker_for_slot(1).model.model_id)
        self.assertNotEqual(
            self.runtime.worker_for_slot(0).worker_home,
            self.runtime.worker_for_slot(1).worker_home,
        )

    def test_embedding_api_starts_on_the_default_port_and_returns_named_model_handles(self) -> None:
        runtime = broker.DulcheRuntime(FakeWorker())
        with (
            mock.patch.object(broker, "WORKER", runtime._slots[0]),
            mock.patch.object(broker, "DULCHE_RUNTIME", runtime),
            mock.patch.object(broker, "DEFAULT_DULCHE_PORT", 0),
        ):
            port = broker.Dulche.Start("")
            model = broker.Dulche.LoadModel("alpha", context=1024)

            self.assertGreater(port, 0)
            self.assertEqual(port, broker.Dulche.Start(port))
            self.assertEqual(model.slot, broker.Dulche.Model("alpha").slot)
            broker.Dulche.Stop()

    def test_replacing_a_slot_invalidates_its_old_model_handle(self) -> None:
        alpha = self.runtime.load_model("alpha", slot=0)
        self.runtime.load_model("beta", slot=0)

        with self.assertRaisesRegex(broker.BrokerError, "no longer hosts model alpha"):
            alpha.SetContext(1024)

    def test_model_handle_schedules_context_for_the_next_turn(self) -> None:
        alpha = self.runtime.load_model("alpha", slot=0, context=1024)

        self.assertEqual(2048, alpha.SetContext(2048))
        self.assertEqual(1024, self.runtime.worker_for_slot(0).context_limit)
        self.assertEqual(2048, self.runtime.worker_for_slot(0).pending_context)

    def test_permission_timeout_returns_ask_user_without_a_listener(self) -> None:
        request = self.runtime.permissions.request("alpha", 0, "command.execute", "dulche.run_command", {}, timeout_seconds=0)

        self.assertEqual("ask_user", request.decision)

    def test_permission_request_is_broadcast_to_frontend_subscribers(self) -> None:
        subscriber = self.runtime.permissions.subscribe()
        outcome: dict[str, broker.PermissionRequest] = {}

        def request_permission() -> None:
            outcome["request"] = self.runtime.permissions.request(
                "alpha", 0, "endpoint.access", "https://example.test/api", {}, timeout_seconds=10
            )

        thread = threading.Thread(target=request_permission, daemon=True)
        thread.start()
        request = subscriber.get(timeout=1)
        self.assertEqual("alpha", request.model_id)
        self.assertEqual("endpoint.access", request.permission)
        self.assertTrue(self.runtime.permissions.resolve(request.request_id, True))
        thread.join(timeout=1)
        self.runtime.permissions.unsubscribe(subscriber)
        self.assertFalse(thread.is_alive())
        self.assertEqual("approved", outcome["request"].decision)

    def test_stop_unblocks_permission_frontend_subscribers(self) -> None:
        subscriber = self.runtime.permissions.subscribe()

        self.runtime.stop()

        self.assertIsNone(subscriber.get(timeout=1))

    def test_native_tools_are_centrally_permission_gated(self) -> None:
        self.runtime.load_model("alpha", slot=0)
        self.runtime.permissions.add_listener(lambda _: False)

        denied = self.runtime.invoke_tool(0, "dulche.run_code", {"source": "print('must not run')"})

        self.assertEqual("permission_required", denied["status"])
        self.assertEqual("denied", denied["decision"])

    def test_approved_native_tool_runs_in_dulche_temporary_directory(self) -> None:
        self.runtime.load_model("alpha", slot=0)
        self.runtime.permissions.add_listener(lambda _: True)

        result = self.runtime.invoke_tool(0, "dulche.run_code", {"source": "import os; print(os.path.basename(os.getcwd()))"})

        self.assertEqual("completed", result["status"])
        self.assertEqual(0, result["exitCode"])
        self.assertTrue(result["output"].strip().startswith("dulche-tool-"))


if __name__ == "__main__":
    unittest.main()
