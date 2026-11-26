"""Exact daemon-worker stop settlement. Temporary state and CPU-only Unix fixtures."""

import contextlib
import http.client
import io
import json
import os
from pathlib import Path
import socket
import tempfile
import threading
import time
from types import SimpleNamespace
import unittest

from daemon import Handler, UnixServer
from jobs import Scheduler
from state import Fault, State


class ObservedEvent(threading.Event):
    def __init__(self):
        super().__init__()
        self.wait_entered = threading.Event()
        self.wait_timeouts = []

    def wait(self, timeout=None):
        self.wait_timeouts.append(timeout)
        self.wait_entered.set()
        return super().wait(timeout)


class HeldCleanupEngine:
    def __init__(self):
        self.cleanup_entered = threading.Event()
        self.release_cleanup = threading.Event()
        self.cleanup_finished = threading.Event()

    @contextlib.contextmanager
    def generate(self, model, payload, *, stream, cancel_event):
        try:
            yield io.BytesIO(
                b'{"choices":[{"message":{"content":"fixture"},"finish_reason":"stop"}]}'
            )
        finally:
            self.cleanup_entered.set()
            if not self.release_cleanup.wait(5):
                raise AssertionError("Fixture cleanup was not released")
            self.cleanup_finished.set()


class SwarmFixture:
    def __init__(self, lock):
        self.lock = lock
        self.stopped_runs = []
        self.stop_all_calls = 0

    def on_job_stop(self, run):
        with self.lock:
            self.stopped_runs.append(run)

    def stop_all(self):
        with self.lock:
            self.stop_all_calls += 1


@unittest.skipUnless(
    hasattr(socket, "AF_UNIX") and hasattr(socket, "SO_PEERCRED"),
    "Dulche management uses Linux Unix sockets",
)
class StopSettlementTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="dulche-stop-settlement-")
        self.state = State(Path(self.temp.name) / "state")
        # Synthetic controls avoid a dispatcher, model service, or production state.
        # The worker test below invokes the real _execute method with a held fake engine.
        self.jobs = Scheduler.__new__(Scheduler)
        self.jobs.state = self.state
        self.jobs.cv = threading.Condition(self.state.lock)
        self.jobs.active = {}
        self.jobs.closing = False
        self.jobs.permitted = lambda *args, **kwargs: None
        self.swarm = SwarmFixture(self.state.lock)
        self.ctx = SimpleNamespace(
            state=self.state,
            lock=self.state.lock,
            jobs=self.jobs,
            swarm=self.swarm,
        )
        self.server = None
        self.server_thread = None
        self.threads = []
        self.cleanup_releases = []

    def tearDown(self):
        for release in self.cleanup_releases:
            release.set()
        for thread in self.threads:
            thread.join(timeout=5)
            self.assertFalse(thread.is_alive(), "Fixture thread did not retire")
        if self.server is not None:
            self.server.shutdown()
            self.server.server_close()
            self.server_thread.join(timeout=3)
            self.assertFalse(self.server_thread.is_alive())
        self.state.db.close()
        self.temp.cleanup()

    @staticmethod
    def control():
        return {
            "model": "fixture",
            "cancel": threading.Event(),
            "process": None,
            "phase": "starting",
            "finished": ObservedEvent(),
        }

    def seed(self, run, *, run_state="running", active=True, kind="agent"):
        request = {
            "kind": kind,
            "model": "fixture",
            "steps": [
                {
                    "type": "generate",
                    "payload": {"messages": [{"role": "user", "content": "fixture"}]},
                }
            ],
        }
        with self.jobs.cv, self.state.transaction() as db:
            now = time.time()
            db.execute(
                "INSERT INTO runs(id,request,model,state,created,updated) VALUES(?,?,?,?,?,?)",
                (run, json.dumps(request), "fixture", run_state, now, now),
            )
            if active:
                control = self.control()
                self.jobs.active[run] = control
                return control
        return None

    def start_http(self):
        self.socket_path = str(Path(self.temp.name) / "fixture.sock")
        self.server = UnixServer(self.socket_path, Handler)
        os.chmod(self.socket_path, 0o600)
        self.server.controller = self.ctx
        self.server_thread = threading.Thread(
            target=self.server.serve_forever,
            kwargs={"poll_interval": 0.01},
            daemon=True,
        )
        self.server_thread.start()

    def post(self, path):
        client = http.client.HTTPConnection("fixture", timeout=4)
        client.sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        client.sock.settimeout(4)
        try:
            client.sock.connect(self.socket_path)
            client.request(
                "POST", path, body=b"{}", headers={"Content-Type": "application/json"}
            )
            response = client.getresponse()
            return response.status, json.loads(response.read())
        finally:
            client.close()

    def test_terminal_database_state_does_not_acknowledge_a_held_worker(self):
        control = self.seed("owned")
        receipt = self.jobs.request_stop("owned")

        self.assertTrue(control["cancel"].is_set())
        self.assertEqual("stopped", self.state.status("owned")["state"])
        result = receipt.wait(0)

        self.assertEqual("owned", result["id"])
        self.assertFalse(result["settled"])
        self.assertIs(control, self.jobs.active["owned"])
        self.assertFalse(control["finished"].is_set())

    def test_removing_active_entry_does_not_replace_original_finished_ack(self):
        control = self.seed("owned")
        receipt = self.jobs.request_stop("owned")
        with self.jobs.cv:
            self.jobs.active.pop("owned")

        self.assertFalse(receipt.wait(0)["settled"])
        self.assertFalse(control["finished"].is_set())

    def test_another_run_finishing_cannot_acknowledge_the_owned_run(self):
        owned = self.seed("owned")
        other = self.seed("other")
        receipt = self.jobs.request_stop("owned")
        with self.jobs.cv:
            self.jobs.active.pop("other")
            self.state.db.execute("UPDATE runs SET state='completed' WHERE id='other'")
            other["finished"].set()

        self.assertFalse(receipt.wait(0)["settled"])
        self.assertFalse(owned["finished"].is_set())
        self.assertFalse(other["cancel"].is_set())

    def test_old_finished_event_cannot_cover_active_or_queued_replacement(self):
        original = self.seed("owned")
        receipt = self.jobs.request_stop("owned")
        replacement = self.control()
        with self.jobs.cv:
            original["finished"].set()
            self.jobs.active["owned"] = replacement

        # Even a terminal database row cannot settle an active replacement.
        self.assertFalse(receipt.wait(0)["settled"])
        self.assertFalse(replacement["cancel"].is_set())
        with self.jobs.cv:
            self.jobs.active.pop("owned")
            self.state.db.execute("UPDATE runs SET state='queued' WHERE id='owned'")

        self.assertFalse(receipt.wait(0)["settled"])

    def test_nonactive_runs_can_prove_no_owned_execution(self):
        other = self.seed("other")
        for run_state in ("queued", "completed", "stopped", "failed", "review", "checkpointed"):
            with self.subTest(run_state=run_state):
                run = "inactive-" + run_state
                self.seed(run, run_state=run_state, active=False)
                result = self.jobs.request_stop(run).wait(0)
                self.assertEqual(run, result["id"])
                self.assertTrue(result["settled"])
                self.assertNotIn(run, self.jobs.active)

        self.assertFalse(other["cancel"].is_set())
        self.assertFalse(self.state.get("stop_all", False))

    def test_unknown_or_empty_identity_does_not_stop_other_runs(self):
        other = self.seed("other")
        for run, status in (("unknown", 404), ("", 400)):
            with self.subTest(run=run):
                with self.assertRaises(Fault) as failure:
                    self.jobs.request_stop(run)
                self.assertEqual(status, failure.exception.status)

        self.assertFalse(other["cancel"].is_set())
        self.assertFalse(self.state.get("stop_all", False))

    def test_settlement_wait_cannot_exceed_two_seconds(self):
        control = self.seed("owned")
        receipt = self.jobs.request_stop("owned")
        for timeout in (-1, 2.01, float("inf"), float("nan"), True):
            with self.subTest(timeout=timeout):
                with self.assertRaises(ValueError):
                    receipt.wait(timeout)
        self.assertEqual([], control["finished"].wait_timeouts)

    def test_http_stop_waits_for_real_execute_cleanup_without_holding_shared_lock(self):
        engine = HeldCleanupEngine()
        self.jobs.engines = engine
        self.cleanup_releases.append(engine.release_cleanup)
        control = self.seed("owned", kind="chat")
        worker = threading.Thread(
            target=self.jobs._execute, args=("owned", control), daemon=True
        )
        self.threads.append(worker)
        worker.start()
        self.assertTrue(engine.cleanup_entered.wait(2))
        self.start_http()
        outcome = []
        responded = threading.Event()

        def request_stop():
            try:
                outcome.append(self.post("/v1/agents/owned/stop"))
            except Exception as error:
                outcome.append(error)
            finally:
                responded.set()

        requester = threading.Thread(target=request_stop, daemon=True)
        self.threads.append(requester)
        requester.start()
        try:
            self.assertTrue(control["cancel"].wait(2))
            self.assertTrue(control["finished"].wait_entered.wait(2))
            self.assertFalse(control["finished"].is_set())
            self.assertFalse(responded.is_set())
            self.assertFalse(engine.cleanup_finished.is_set())
        finally:
            engine.release_cleanup.set()

        self.assertTrue(responded.wait(3))
        worker.join(timeout=3)
        self.assertFalse(worker.is_alive())
        self.assertEqual(1, len(outcome))
        self.assertIsInstance(outcome[0], tuple)
        status, result = outcome[0]
        self.assertEqual(200, status)
        self.assertEqual("owned", result["id"])
        self.assertEqual("stopped", result["state"])
        self.assertTrue(result["settled"])
        self.assertTrue(engine.cleanup_finished.is_set())
        self.assertTrue(control["finished"].is_set())
        self.assertNotIn("owned", self.jobs.active)
        self.assertEqual(["owned"], self.swarm.stopped_runs)

    def test_http_timeout_is_unsettled_until_the_exact_control_finishes(self):
        control = self.seed("owned")
        other = self.seed("other")
        self.start_http()

        status, result = self.post("/v1/agents/owned/stop")

        self.assertEqual(200, status)
        self.assertEqual("owned", result["id"])
        self.assertEqual("stopped", result["state"])
        self.assertFalse(result["settled"])
        self.assertEqual([2.0], control["finished"].wait_timeouts)
        self.assertFalse(control["finished"].is_set())
        self.assertFalse(other["cancel"].is_set())
        with self.jobs.cv:
            self.jobs.active.pop("owned")
            control["finished"].set()

        status, settled = self.post("/v1/agents/owned/stop")
        self.assertEqual(200, status)
        self.assertEqual("owned", settled["id"])
        self.assertTrue(settled["settled"])
        self.assertFalse(other["cancel"].is_set())

    def test_stop_all_retains_its_existing_scope_and_response(self):
        agent = self.seed("agent")
        chat = self.seed("chat", kind="chat")
        self.start_http()

        status, result = self.post("/v1/agents/stop-all")

        self.assertEqual(200, status)
        self.assertEqual({"stopped": ["agent"]}, result)
        self.assertTrue(agent["cancel"].is_set())
        self.assertFalse(chat["cancel"].is_set())
        self.assertEqual([], agent["finished"].wait_timeouts)
        self.assertEqual([], chat["finished"].wait_timeouts)
        self.assertTrue(self.state.get("stop_all"))
        self.assertEqual(1, self.swarm.stop_all_calls)
        self.assertEqual([], self.swarm.stopped_runs)


if __name__ == "__main__":
    unittest.main()
