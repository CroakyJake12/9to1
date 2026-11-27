"""Cold-start and crash recovery integration fixtures; no real model or GPU is touched."""

import contextlib
import io
import json
import tempfile
import threading
import time
import unittest
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

from controller import Controller
from engine_profiles import EngineError
from state import Fault
from test_service import Catalog, Engines, wait


class LifecycleEngine(Engines):
    def __init__(self):
        super().__init__()
        self.loaded = False
        self.managed = True
        self.load_gate = threading.Event()
        self.load_gate.set()
        self.load_entered = threading.Event()
        self.fail_load = False
        self.fail_generation = None
        self.generation_calls = 0
        self.count_calls = 0
        self.fail_count = False

    def models(self):
        return [{**super().models()[0], "managed": self.managed,
                 "ownership": "dulche" if self.managed else "external-strata"}]

    def status(self, mid):
        return self.models()[0]

    def load(self, mid, settings):
        self.load_count += 1
        self.load_entered.set()
        if not self.load_gate.wait(3):
            raise AssertionError("Fixture load did not settle")
        if self.fail_load:
            raise EngineError("Fixture engine exited during startup")
        self.config = settings
        self.loaded = True

    def count_prompt(self, mid, messages, options=None):
        self.count_calls += 1
        if not self.loaded:
            raise AssertionError("Tokenizer used before model was ready")
        if self.fail_count:
            self.fail_count = False
            self.loaded = False
            raise ConnectionError("Fixture tokenizer process exited")
        return {"input_tokens": 7}

    @contextlib.contextmanager
    def generate(self, mid, payload, *, stream, cancel_event):
        self.generation_calls += 1
        mode, self.fail_generation = self.fail_generation, None
        if mode == "before-output":
            self.loaded = False
            raise ConnectionError("Fixture engine exited before first token")
        if mode == "rejected-prompt":
            raise EngineError("Engine returned HTTP 400: fixture invalid prompt")
        if mode == "after-output":
            try:
                yield io.BytesIO(b'data: {"choices":[{"delta":{"content":"partial"}}]}\n\n')
            finally:
                self.loaded = False
            return
        with super().generate(mid, payload, stream=stream, cancel_event=cancel_event) as result:
            yield result


class InferenceLifecycleTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="dulche-cold-start-")
        self.addCleanup(self.temp.cleanup)
        self.disk = patch("jobs.shutil.disk_usage", return_value=SimpleNamespace(free=100 * 2**30))
        self.disk.start()
        self.addCleanup(self.disk.stop)
        self.eng = LifecycleEngine()
        self.ctx = Controller(self.temp.name, self.eng, Catalog(), interval=1000, cooldown=0.02)
        self.request = {"kind": "chat", "model": "q", "steps": [{"type": "generate", "payload": {
            "messages": [{"role": "user", "content": "hello"}]}}]}

    def tearDown(self):
        self.eng.load_gate.set()
        self.eng.release.set()
        self.ctx.close()
        self.ctx.state.db.close()

    def status(self, run):
        return self.ctx.state.status(run, full=True)

    def completed(self, run):
        return self.status(run)["state"] == "completed"

    def test_held_maintenance_window_blocks_upgrade_without_active_worker(self):
        self.ctx.state.set("training-resource-window", {"kind": "inference-maintenance", "owner": "fixture",
                           "phase": "held", "models": [{"id": "q"}]})
        self.assertFalse(self.ctx.transitions)
        self.assertIsNone(self.ctx.training_windows.worker)
        with self.assertRaisesRegex(Fault, "engine operation or task is active"):
            self.ctx.prepare_upgrade()
        self.assertEqual("ready", self.ctx.phase)

    def test_adapter_binding_is_snapshotted_in_the_durable_queue(self):
        self.assertIs(self.ctx.inference, self.eng.inference_management)
        self.eng.load_gate.clear()
        self.ctx.state.set("inference:adapters:q", [{"binding_id": "selected-a", "specialist_id": None}])
        run = self.ctx.jobs.create(self.request, "binding-snapshot")
        saved = self.status(run)["request"]["steps"][0]["payload"]
        self.assertEqual("selected-a", saved["adapter_binding_id"])
        self.assertNotIn("adapter_binding_id", self.request["steps"][0]["payload"])
        self.ctx.state.set("inference:adapters:q", [{"binding_id": "selected-b", "specialist_id": None}])
        with self.assertRaisesRegex(Fault, "different request"):
            self.ctx.jobs.create(self.request, "binding-snapshot")
        self.ctx.jobs.stop(run=run)
        self.eng.load_gate.set()

    def test_sending_to_cold_model_loads_once_and_completes(self):
        self.assertTrue(self.ctx.model("q")["can_start"])
        run = self.ctx.jobs.create(self.request)
        wait(lambda: self.completed(run))
        self.assertEqual(1, self.eng.load_count)
        self.assertEqual(1, self.eng.generation_calls)
        self.assertEqual("hi", self.status(run)["completed_steps"][0]["choices"][0]["message"]["content"])

    def test_count_preflight_and_concurrent_sends_share_one_load(self):
        self.eng.load_gate.clear()
        with ThreadPoolExecutor(max_workers=2) as executor:
            count = executor.submit(self.ctx.count_prompt, "q", [{"role": "user", "content": "hello"}])
            self.assertTrue(self.eng.load_entered.wait(2))
            first = self.ctx.jobs.create(self.request, "first")
            second = self.ctx.jobs.create(self.request, "second")
            self.assertEqual(1, self.eng.load_count)
            self.eng.load_gate.set()
            self.assertEqual({"input_tokens": 7}, count.result(3))
        wait(lambda: self.completed(first) and self.completed(second))
        self.assertEqual(1, self.eng.load_count)
        self.assertEqual(2, self.eng.generation_calls)

    def test_manual_suspension_prevents_send_and_count_autostart(self):
        self.ctx.change("q", "suspend")
        wait(lambda: "q" not in self.ctx.transitions)
        self.assertFalse(self.ctx.model("q")["can_start"])
        for action in (lambda: self.ctx.jobs.create(self.request),
                       lambda: self.ctx.count_prompt("q", [{"role": "user", "content": "hello"}])):
            with self.assertRaisesRegex(Fault, "Resume"):
                action()
        self.assertEqual(0, self.eng.load_count)

    def test_pressure_suspension_is_not_cleared_by_send(self):
        self.ctx.state.set("model:q", {"pressure": True, "want": True})
        with self.assertRaisesRegex(Fault, "resources"):
            self.ctx.jobs.create(self.request)
        self.assertEqual(0, self.eng.load_count)

    def test_training_window_inhibits_cold_start(self):
        with patch.object(self.ctx.training_windows, "reserved", return_value=True), \
             patch.object(self.ctx.training_windows, "inference_waiter") as waiter:
            with self.assertRaisesRegex(Fault, "training resource window"):
                self.ctx.jobs.create(self.request)
            waiter.assert_called_once_with("q")
        self.assertEqual(0, self.eng.load_count)

    def test_cold_send_cannot_override_swarm_residency_owner(self):
        self.ctx.state.set("model:q", {"swarm_residency": {"owner": "fixture-swarm", "target": "unload"}})
        self.assertFalse(self.ctx.model("q")["can_start"])
        with self.assertRaisesRegex(Fault, "owning swarm"):
            self.ctx.jobs.create(self.request)
        self.assertEqual(0, self.eng.load_count)

    def test_crash_before_first_output_retries_same_durable_run(self):
        self.eng.fail_generation = "before-output"
        run = self.ctx.jobs.create(self.request, "original")
        wait(lambda: self.completed(run))
        self.assertEqual(2, self.eng.load_count)
        self.assertEqual(2, self.eng.generation_calls)
        self.assertEqual(run, self.ctx.jobs.create(self.request, "original"))
        self.assertEqual(1, len(self.status(run)["completed_steps"]))
        self.assertEqual(1, self.ctx.state.get("run-recovery:" + run))

    def test_partial_stream_is_saved_as_failed_and_not_replayed(self):
        self.eng.fail_generation = "after-output"
        self.request["steps"][0]["payload"]["stream"] = True
        run = self.ctx.jobs.create(self.request)
        wait(lambda: self.status(run)["state"] == "failed")
        self.assertEqual(1, self.eng.generation_calls)
        chunks = self.ctx.state.db.execute("SELECT data FROM events WHERE run=? AND kind='chunks'", (run,)).fetchall()
        self.assertTrue(any("partial" in row[0] for row in chunks))
        self.assertTrue(self.ctx.model("q")["recovery"]["pending"])
        self.assertIsNone(self.ctx.state.get("run-recovery:" + run))

    def test_invalid_prompt_does_not_restart_healthy_engine(self):
        self.eng.fail_generation = "rejected-prompt"
        run = self.ctx.jobs.create(self.request)
        wait(lambda: self.status(run)["state"] == "failed")
        self.assertEqual(1, self.eng.load_count)
        self.assertEqual(0, self.eng.stop_count)
        self.assertFalse(self.ctx.model("q")["recovery"]["pending"])

    def test_token_count_retries_once_after_confirmed_crash(self):
        self.eng.fail_count = True
        result = self.ctx.count_prompt("q", [{"role": "user", "content": "hello"}])
        self.assertEqual({"input_tokens": 7}, result)
        self.assertEqual(2, self.eng.load_count)
        self.assertEqual(2, self.eng.count_calls)

    def test_recovery_is_bounded_then_requires_explicit_retry(self):
        self.ctx.ensure_model("q")
        self.eng.loaded = False
        self.eng.fail_load = True
        self.assertTrue(self.ctx.recover_engine("q", "Fixture crash"))
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline and not self.ctx.model("q")["recovery"]["exhausted"]:
            self.ctx._poll()
            time.sleep(0.015)
        self.assertTrue(self.ctx.model("q")["recovery"]["exhausted"])
        self.assertEqual(4, self.eng.load_count)  # One initial load and three recovery attempts.
        for _ in range(3):
            self.ctx._poll()
        self.assertEqual(4, self.eng.load_count)
        with self.assertRaisesRegex(Fault, "Automatic recovery paused"):
            self.ctx.jobs.create(self.request)
        self.eng.fail_load = False
        self.ctx.change("q", "load")
        wait(lambda: self.ctx.model("q")["state"] == "loaded")
        self.assertFalse(self.ctx.model("q")["recovery"]["exhausted"])

    def test_emergency_stop_prevents_pending_automatic_restart(self):
        self.ctx.ensure_model("q")
        self.eng.loaded = False
        self.assertTrue(self.ctx.recover_engine("q", "Fixture crash"))
        self.ctx.jobs.stop()
        with self.assertRaisesRegex(Fault, "emergency stop"):
            self.ctx.prepare_model("q")
        self.assertEqual(1, self.eng.load_count)

    def test_external_engine_failure_is_not_claimed_or_restarted(self):
        self.eng.managed = False
        self.eng.loaded = True
        self.ctx.refresh()
        self.eng.loaded = False
        self.assertFalse(self.ctx.recover_engine("q", "External crash"))
        self.assertEqual(0, self.eng.load_count)

    def test_manual_suspend_during_load_preserves_pending_stop(self):
        self.eng.load_gate.clear()
        run = self.ctx.jobs.create(self.request)
        self.assertTrue(self.eng.load_entered.wait(2))
        self.ctx.change("q", "suspend")
        self.eng.load_gate.set()
        wait(lambda: "q" not in self.ctx.transitions and self.ctx.model("q")["state"] == "suspended")
        self.assertFalse(self.eng.loaded)
        self.assertEqual("checkpointed", self.status(run)["state"])
        self.assertEqual(0, self.eng.generation_calls)

    def _failed_start_after_manual_stop(self, *, recover=False):
        if recover:
            self.ctx.ensure_model("q")
            self.eng.loaded = False
        self.eng.fail_load = True
        self.eng.load_gate.clear()
        self.eng.load_entered.clear()
        if recover:
            self.assertTrue(self.ctx.recover_engine("q", "Fixture owned engine crashed"))
        run = self.ctx.jobs.create(self.request)
        self.assertTrue(self.eng.load_entered.wait(2))
        self.assertEqual("recovering" if recover else "loading", self.ctx.model("q")["state"])
        self.ctx.change("q", "suspend")
        self.assertEqual("suspending", self.ctx.model("q")["state"])
        self.eng.load_gate.set()
        wait(lambda: "q" not in self.ctx.transitions)
        row = self.ctx.model("q")
        self.assertEqual("failed", row["state"])
        self.assertIn("exited during startup", row["error"])
        self.assertTrue(row["manual_suspended"])
        self.assertFalse(row["can_start"])
        self.assertTrue(self.ctx._prefs("q")["pending_suspend"])
        self.assertEqual("checkpointed", self.status(run)["state"])
        self.assertEqual(0, self.eng.generation_calls)
        return run

    def _assert_prior_stop_can_only_be_replaced_by_explicit_resume(self, *, recover=False):
        run = self._failed_start_after_manual_stop(recover=recover)
        loads = self.eng.load_count
        for action in (lambda: self.ctx.jobs.create(self.request),
                       lambda: self.ctx.count_prompt("q", [{"role": "user", "content": "hello"}]),
                       lambda: self.ctx.change("q", "load"),
                       lambda: self.ctx.change("q", "resume", automatic=True)):
            with self.assertRaises(Fault):
                action()
        for _ in range(2):
            self.ctx._poll()
        self.assertEqual(loads, self.eng.load_count)
        self.assertTrue(self.ctx.model("q")["manual_suspended"])
        with self.assertRaisesRegex(Fault, "Unknown performance profile"):
            self.ctx.change("q", "resume", profile="removed-profile")
        self.assertTrue(self.ctx._prefs("q")["pending_suspend"])
        self.assertTrue(self.ctx.model("q")["manual_suspended"])

        self.eng.fail_load = False
        self.eng.load_gate.clear()
        self.eng.load_entered.clear()
        self.ctx.change("q", "resume")
        self.assertTrue(self.eng.load_entered.wait(2))
        self.eng.load_gate.set()
        wait(lambda: "q" not in self.ctx.transitions)
        self.assertEqual("loaded", self.ctx.model("q")["state"])
        wait(lambda: self.completed(run))
        self.assertFalse(self.ctx.model("q")["manual_suspended"])
        self.assertEqual(loads + 1, self.eng.load_count)
        self.assertEqual(1, self.eng.generation_calls)
        self.assertEqual(1, len(self.status(run)["completed_steps"]))

    def test_explicit_resume_consumes_only_prior_stop_after_failed_load(self):
        self._assert_prior_stop_can_only_be_replaced_by_explicit_resume()

    def test_explicit_resume_consumes_only_prior_stop_after_failed_recovery(self):
        self._assert_prior_stop_can_only_be_replaced_by_explicit_resume(recover=True)

    def _assert_newer_stop_wins_during_resume(self, *, fail_resume):
        run = self._failed_start_after_manual_stop()
        loads = self.eng.load_count
        self.eng.fail_load = fail_resume
        self.eng.load_gate.clear()
        self.eng.load_entered.clear()
        self.ctx.change("q", "resume")
        self.assertTrue(self.eng.load_entered.wait(2))
        self.ctx.change("q", "suspend")
        self.assertTrue(self.ctx._prefs("q")["pending_suspend"])
        self.eng.load_gate.set()
        wait(lambda: "q" not in self.ctx.transitions)
        row = self.ctx.model("q")
        self.assertEqual("failed" if fail_resume else "suspended", row["state"])
        self.assertTrue(row["manual_suspended"])
        self.assertFalse(row["can_start"])
        self.assertFalse(self.eng.loaded)
        self.assertEqual(loads + 1, self.eng.load_count)
        self.assertEqual(0, self.eng.generation_calls)
        self.assertEqual("checkpointed", self.status(run)["state"])
        for _ in range(2):
            self.ctx._poll()
        self.assertEqual(loads + 1, self.eng.load_count)
        with self.assertRaisesRegex(Fault, "Resume"):
            self.ctx.jobs.create(self.request)

    def test_newer_manual_stop_wins_after_explicit_resume_loads_successfully(self):
        self._assert_newer_stop_wins_during_resume(fail_resume=False)

    def test_newer_manual_stop_wins_after_explicit_resume_fails(self):
        self._assert_newer_stop_wins_during_resume(fail_resume=True)


if __name__ == "__main__":
    unittest.main()
