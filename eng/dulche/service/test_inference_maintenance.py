"""CPU-only maintenance leases, configuration restoration and uncertain custody."""
import contextlib
import json
import os
from pathlib import Path
import tempfile
import threading
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from engine_profiles import PRIMARY, atomic_json, native_tuning_path
import inference_maintenance as maintenance
import inference_tuner
from state import Fault, State


class Engines:
    def __init__(self, root):
        self.root = root / "engines"
        self.root.mkdir()
        self.loaded = True
        self.loads = []
        self.stops = 0
        self.fail_candidate = False

    def unload(self, mid):
        self.stops += 1
        self.loaded = False
        return {"state": "unloaded"}

    def load(self, mid, settings):
        path = native_tuning_path(self.root, mid)
        overlay = json.loads(path.read_text()) if path.exists() else None
        self.loads.append(overlay)
        if self.fail_candidate and overlay and overlay.get("operation_id"):
            raise RuntimeError("candidate startup failed")
        self.loaded = True
        return {"state": "loaded"}


class Windows(maintenance.InferenceWindows):
    def acquire(self, owner, models):
        doc = {"version": 1, "owner": owner, "phase": "held", "direction": "releasing",
               "models": [{"id": models[0], "physical_devices": ["rtx5080"], "stage": "parked"}]}
        self._save(doc)
        return doc

    def status(self, owner=None):
        return {**self.current(), "release_valid": True}


class MaintenanceTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="dulche-maintenance-")
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        self.state = State(root / "state")
        self.addCleanup(self.state.db.close)
        self.ctx = SimpleNamespace(state=self.state, lock=self.state.lock, phase="ready",
                                   transitions={}, errors={}, finished=threading.Event())
        self.ctx.engines = Engines(root)
        self.ctx._prefs = lambda mid: self.state.get("model:" + mid, {})
        self.ctx.model = lambda mid: {"id": mid, "state": "loaded" if self.ctx.engines.loaded else "unloaded",
                                     "managed": True, "active_profile_id": "current"}
        self.ctx.profiles = lambda mid: [{"id": "current", "settings": {}}]
        self.ctx.refresh = lambda: setattr(self.ctx, "cache", {PRIMARY: self.ctx.model(PRIMARY)})
        self.ctx.refresh()
        self.ctx.training_windows = Windows(self.ctx)
        self.op = "a" * 32
        self.cancel = threading.Event()
        self.previous = {"schema": 1, "model": PRIMARY, "state": "restored"}
        self.candidate = {"schema": 1, "model": PRIMARY, "state": "active", "operation_id": self.op, "settings": {"kv": "int8"}}
        self.overlay = native_tuning_path(self.ctx.engines.root, PRIMARY)
        atomic_json(self.overlay, self.previous)
        self.state.set("inference:operation:" + self.op, {"previous_overlay": self.previous})
        self.state.set("model:" + PRIMARY, {"profile": "current", "want": True})
        self.enterContext(patch.dict(os.environ, {"XDG_RUNTIME_DIR": str(root / "runtime")}))
        self.enterContext(patch.object(maintenance, "capability", return_value={"supported": True}))

    def lease(self):
        return maintenance.maintenance(self.ctx, PRIMARY, self.op, self.cancel)

    def test_measurement_restores_by_fresh_load_with_applied_overlay(self):
        with self.lease() as lease:
            self.assertFalse(self.ctx.engines.loaded)
            self.assertTrue(Path(lease["gpu_lock"]).is_file())
            atomic_json(self.overlay, self.candidate)
            with patch.object(inference_tuner, "run_tuner", return_value={"returncode": 0, "stdout": "{}", "receipt": "settled-fixture"}):
                result = maintenance.run(self.ctx, PRIMARY, self.op, [], cwd="/fixture", cancel_event=self.cancel)
            self.assertEqual(0, result["returncode"])
        self.assertEqual([self.candidate], self.ctx.engines.loads)
        self.assertTrue(self.ctx.engines.loaded)
        self.assertEqual("restored", self.ctx.training_windows.current()["phase"])
        self.assertFalse(self.ctx.transitions)

    def test_failed_measurement_rolls_back_before_model_restoration(self):
        with self.assertRaisesRegex(RuntimeError, "measurement failed"):
            with self.lease():
                atomic_json(self.overlay, self.candidate)
                raise RuntimeError("measurement failed")
        self.assertEqual([self.previous], self.ctx.engines.loads)
        self.assertEqual(self.previous, json.loads(self.overlay.read_text()))
        self.assertTrue(self.ctx.engines.loaded)

    def test_rejected_candidate_reloads_previous_profile_and_reports_failure(self):
        self.ctx.engines.fail_candidate = True
        with self.assertRaisesRegex(Fault, "previous profile was restored"):
            with self.lease():
                atomic_json(self.overlay, self.candidate)
        self.assertEqual([self.candidate, self.previous], self.ctx.engines.loads)
        self.assertTrue(self.ctx.engines.loaded)
        self.assertEqual("restored", self.ctx.training_windows.current()["phase"])

    def test_new_manual_stop_prevents_restoration(self):
        with self.lease() as lease:
            self.state.set("model:" + PRIMARY, {"profile": "current", "manual": True,
                           "want": True, "training_skip_restore": lease["owner"]})
        self.assertFalse(self.ctx.engines.loaded)
        self.assertEqual([], self.ctx.engines.loads)
        self.assertTrue(self.ctx._prefs(PRIMARY)["manual"])

    def test_new_profile_choice_is_retained_without_restoring_old_profile(self):
        with self.lease():
            self.state.set("model:" + PRIMARY, {"profile": "new-choice", "want": True})
        self.assertEqual("new-choice", self.ctx._prefs(PRIMARY)["profile"])
        self.assertEqual([], self.ctx.engines.loads)

    def test_close_policy_prevents_reload_during_shutdown(self):
        with self.lease():
            self.ctx.phase = "stopping"
        self.assertFalse(self.ctx.engines.loaded)
        self.assertEqual([], self.ctx.engines.loads)
        self.assertFalse(self.ctx.transitions)

    def test_uncertain_tuner_custody_keeps_durable_physical_reservation(self):
        error = inference_tuner.CustodyUnsettled("fixture cleanup unknown", unit="fixture-unit", receipt="fixture-receipt")
        with self.assertRaises(inference_tuner.CustodyUnsettled):
            with self.lease():
                with patch.object(inference_tuner, "run_tuner", side_effect=error):
                    maintenance.run(self.ctx, PRIMARY, self.op, [], cwd="/fixture", cancel_event=self.cancel)
        doc = self.ctx.training_windows.current()
        self.assertEqual("blocked", doc["phase"])
        self.assertEqual("fixture-unit", doc["tuner_unit"])
        self.assertEqual([], self.ctx.engines.loads)
        self.assertTrue(self.ctx.training_windows.reserved(PRIMARY))
        restarted = maintenance.InferenceWindows(self.ctx)
        with self.assertRaisesRegex(Fault, "cleanup"):
            restarted.release(doc["owner"])
        with self.assertRaisesRegex(Fault, "cleanup"):
            restarted.acquire(doc["owner"], [PRIMARY])

    def test_tuner_without_exact_lease_is_refused(self):
        with patch.object(inference_tuner, "run_tuner") as runner:
            with self.assertRaisesRegex(Fault, "exact exclusive"):
                maintenance.run(self.ctx, PRIMARY, self.op, [], cwd="/fixture", cancel_event=self.cancel)
        runner.assert_not_called()

    def test_stop_after_handoff_prevents_tuner_launch(self):
        with self.lease():
            self.state.set("stop_all", True)
            with patch.object(inference_tuner, "run_tuner") as runner:
                with self.assertRaisesRegex(Fault, "emergency stop"):
                    maintenance.run(self.ctx, PRIMARY, self.op, [], cwd="/fixture", cancel_event=self.cancel)
            runner.assert_not_called()
        self.assertEqual([], self.ctx.engines.loads)

    def test_manual_stop_cancels_an_admitted_tuner_before_restoration(self):
        def supervised(*args, **kwargs):
            self.state.set("model:" + PRIMARY, {"profile": "current", "manual": True})
            self.assertTrue(kwargs["cancel_event"].wait(2), "manual stop must reach the guarded runner")
            return {"returncode": 130, "stdout": "{}", "receipt": "settled-fixture"}
        with self.lease():
            with patch.object(inference_tuner, "run_tuner", side_effect=supervised):
                result = maintenance.run(self.ctx, PRIMARY, self.op, [], cwd="/fixture", cancel_event=self.cancel)
            self.assertEqual(130, result["returncode"])
        self.assertEqual([], self.ctx.engines.loads)
        self.assertEqual("restored", self.ctx.training_windows.current()["phase"])

    def test_cancel_before_handoff_does_not_unload_model(self):
        self.cancel.set()
        with self.assertRaises(InterruptedError):
            with self.lease():
                self.fail("cancelled lease cannot yield")
        self.assertEqual(0, self.ctx.engines.stops)
        self.assertTrue(self.ctx.engines.loaded)


if __name__ == "__main__":
    unittest.main()
