"""Fixture-only safety, recovery and checkpoint tests. No live model or training job is touched."""

import hashlib
import json
import sqlite3
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

import training_park as park
from engines import EngineManager, GPU_RTX
from resource_transitions import ResourceWindows
from state import Fault, State

MID = park.PRIMARY


class Engine:
    physical_devices = staticmethod(EngineManager.physical_devices)

    def __init__(self):
        self.loaded = True
        self.calls = []
        self.busy = False

    def _spec(self, mid):
        if mid != MID:
            raise KeyError(mid)
        return {"id": mid, "gpu": GPU_RTX}

    def status(self, mid):
        return {
            **self._spec(mid),
            "state": "loaded" if self.loaded else "unloaded",
            "managed": True,
        }


class Context:
    def __init__(self, d):
        self.state = State(d)
        self.lock = self.state.lock
        self.engines = Engine()
        self.phase = "ready"
        self.transitions = {}
        self.jobs = Mock()
        self.jobs.active = {}
        self.swarm = Mock()
        self.swarm._all.return_value = []

    def model(self, mid):
        if mid != MID:
            raise Fault(404, "absent")
        return dict(
            self.engines.status(mid),
            settings={"context": 524288},
            active_profile_id="original",
        )

    def _prefs(self, mid):
        return self.state.get("model:" + mid, {})

    def refresh(self):
        pass


class WindowTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.ctx = Context(self.tmp.name)
        self.addCleanup(self.ctx.state.db.close)
        self.w = ResourceWindows(self.ctx)
        self.turn = self.mock("owner_turn_active", False)
        self.mock(
            "inspect_native",
            {"model": MID, "profile_digest": "abc", "before_mib": 7000},
        )
        self.backup = self.mock(
            "refresh_mentor_checkpoint",
            {"integrity_check": "ok", "sha256": "backup-digest"},
        )

        def unload(*a):
            if self.ctx.engines.busy:
                raise Fault(409, "native busy")
            self.ctx.engines.calls.append("unload")
            self.ctx.engines.loaded = False
            return {"release_verified": True, "released_vram_mib": 7000}

        def load(*a, load=True):
            self.ctx.engines.calls.append("restore" if load else "skip")
            self.ctx.engines.loaded = load
            return {"restored": load}

        self.mock("park_native", effect=unload)
        self.restore = self.mock("restore_native", effect=load)
        self.mock("parked_native", effect=lambda *a: not self.ctx.engines.loaded)
        self.mock("direct_waiting", False)
        self.check = self.mock("require_no_training", None)

    def mock(self, fn, value=None, effect=None):
        m = patch.object(park, fn, side_effect=effect, return_value=value)
        v = m.start()
        self.addCleanup(m.stop)
        return v

    def finish(self):
        self.w.worker.join(2)
        self.assertFalse(self.w.worker.is_alive())
        return self.w.status("cycle:1")

    def acquire(self):
        return self.w.acquire("cycle:1", [MID])

    def test_active_run_swarm_cli_and_transition_refuse_before_side_effects(self):
        for setup, reset in [
            (
                lambda: self.ctx.jobs.active.update(a={"model": MID}),
                lambda: self.ctx.jobs.active.clear(),
            ),
            (
                lambda: setattr(
                    self.ctx.swarm._all, "return_value", [{"state": "running"}]
                ),
                lambda: setattr(self.ctx.swarm._all, "return_value", []),
            ),
            (
                lambda: setattr(self.turn, "return_value", True),
                lambda: setattr(self.turn, "return_value", False),
            ),
            (
                lambda: self.ctx.transitions.update(x="loading"),
                lambda: self.ctx.transitions.clear(),
            ),
        ]:
            setup()
            with self.assertRaises(Fault):
                self.acquire()
            reset()
        self.assertEqual(self.ctx.engines.calls, [])
        self.assertEqual(self.w.status()["phase"], "idle")
        self.backup.assert_not_called()

    def test_held_window_rejects_other_owner_and_preserves_exact_restore(self):
        self.acquire()
        row = self.finish()
        self.assertEqual(row["phase"], "held")
        self.assertTrue(row["release_valid"])
        self.assertEqual(row["models"][0]["profile_id"], "original")
        self.assertEqual(row["models"][0]["release"]["released_vram_mib"], 7000)
        with self.assertRaises(Fault):
            self.w.acquire("cycle:2", [MID])
        self.assertEqual(self.acquire()["phase"], "held")
        self.w.inference_waiter(MID)
        self.assertTrue(self.w.status()["broker_inference_waiting"])
        self.w.release("cycle:1")
        self.assertEqual(self.finish()["phase"], "restored")
        self.assertEqual(self.ctx.engines.calls, ["unload", "restore"])
        self.assertEqual(self.w.release("cycle:1")["phase"], "restored")

    def test_durable_restart_does_not_automatically_replay_an_unload(self):
        self.acquire()
        self.finish()
        self.w = ResourceWindows(self.ctx)
        self.assertTrue(self.w.reserved(MID))
        self.assertEqual(self.w.status()["phase"], "held")
        self.assertEqual(self.ctx.engines.calls, ["unload"])
        self.w.release("cycle:1")
        self.finish()
        self.assertEqual(self.ctx.engines.calls, ["unload", "restore"])

    def test_native_race_is_blocked_and_retry_refreshes_checkpoint(self):
        self.ctx.engines.busy = True
        self.acquire()
        self.assertEqual(self.finish()["phase"], "blocked")
        self.assertTrue(self.ctx.engines.loaded)
        self.ctx.engines.busy = False
        self.acquire()
        self.assertEqual(self.finish()["phase"], "held")
        self.assertEqual(self.backup.call_count, 2)

    def test_external_training_and_manual_override(self):
        self.acquire()
        self.finish()
        self.check.side_effect = Fault(409, "training active")
        with self.assertRaises(Fault):
            self.w.release("cycle:1")
        self.restore.assert_not_called()
        self.check.side_effect = None
        self.ctx.state.set("model:" + MID, {"manual": True})
        self.w.release("cycle:1")
        self.assertEqual(self.finish()["phase"], "restored")
        self.assertEqual(self.ctx.engines.calls, ["unload", "skip"])

    def test_foreign_and_unloaded_models_do_not_claim_verified_capacity(self):
        self.ctx.engines.loaded = False
        with self.assertRaises(Fault):
            self.acquire()
        with patch.object(
            self.ctx.engines,
            "status",
            return_value={"state": "loaded", "managed": False},
        ):
            with self.assertRaises(Fault):
                self.acquire()
        self.backup.assert_not_called()


class CheckpointTests(unittest.TestCase):
    def test_sqlite_online_backup_is_private_atomic_and_verified(self):
        with tempfile.TemporaryDirectory() as d:
            src = Path(d) / "source"
            dest = Path(d) / "backup"
            with sqlite3.connect(src) as db:
                db.execute("pragma journal_mode=WAL")
                db.execute("create table notes(id integer)")
                db.execute("insert into notes values(8)")
            dest.touch(mode=0o600)
            src.chmod(0o600)
            receipt = park.checkpoint_context(src, dest)
            self.assertEqual(receipt["integrity_check"], "ok")
            self.assertEqual(
                receipt["sha256"], hashlib.sha256(dest.read_bytes()).hexdigest()
            )
            self.assertEqual(dest.stat().st_mode & 0o777, 0o600)
            with sqlite3.connect(dest) as db:
                self.assertEqual(db.execute("select id from notes").fetchone(), (8,))
            other = Path(d) / "link"
            other.symlink_to(dest)
            with self.assertRaises(Fault):
                park.checkpoint_context(src, other)

    def test_restore_capacity_uses_actual_guard_threshold_freshness_and_free_memory(
        self,
    ):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            manager = Mock(root=root)
            policy = {
                "devices": [{"id": "rtx5080", "stop_c": 68}],
                "status_file": str(root / "sensor"),
            }
            (root / "v100-guard.json").write_text(json.dumps(policy))
            reading = {"driver_ok": True, "name": "NVIDIA GeForce RTX 5080", "temp": 67}
            device = {
                "reading_full": reading,
                "stopped": False,
                "telemetry_ok": True,
                "last_telemetry_ts": 98,
            }
            state = {"ts": 99, "devices": {"rtx5080": device}}

            def verify():
                (root / "sensor").write_text(json.dumps(state))
                park._restore_capacity(manager, {"before_mib": 3400})

            with (
                patch.object(park.time, "time", return_value=100),
                patch.object(
                    park, "_memory", return_value={"MemAvailable": 71 * 1024**2}
                ) as ram,
                patch.object(park, "_command", return_value=Mock(stdout="3500")) as gpu,
            ):
                verify()
                reading["temp"] = 68
                with self.assertRaises(Fault):
                    verify()
                reading["temp"] = 40
                device["last_telemetry_ts"] = 93
                with self.assertRaises(Fault):
                    verify()
                device["last_telemetry_ts"] = 99
                gpu.return_value.stdout = "3399"
                with self.assertRaises(Fault):
                    verify()
                gpu.return_value.stdout = "3500"
                ram.return_value = {"MemAvailable": 70 * 1024**2 - 1}
                with self.assertRaises(Fault):
                    verify()

    def test_running_native_traffic_never_reaches_unload(self):
        import contextlib

        m = Mock()
        m._change.return_value = contextlib.nullcontext()
        p = {"model": MID, "resident_pids": [100], "before_mib": 700}
        with (
            patch.object(
                park, "_native", return_value=("unit", {}, 100, "secret", "digest")
            ),
            patch.object(park, "_freeze_state", return_value="running"),
            patch.object(
                park,
                "_payload",
                return_value={
                    "model": MID,
                    "loaded": True,
                    "activity": {"in_flight": 1},
                },
            ),
            patch.object(park, "_request") as op,
        ):
            with self.assertRaises(Fault):
                park.park_native(m, p)
            op.assert_not_called()

    def test_freeze_race_thaws_without_terminating_or_canceling(self):
        import contextlib

        m = Mock()
        m._change.return_value = contextlib.nullcontext()
        p = {"model": MID, "resident_pids": [100, 101], "before_mib": 700}
        with (
            patch.object(
                park, "_native", return_value=("unit", {}, 100, "key", "digest")
            ),
            patch.object(park, "_freeze_state", side_effect=["running", "frozen"]),
            patch.object(
                park,
                "_payload",
                return_value={
                    "model": MID,
                    "loaded": True,
                    "activity": {"in_flight": 0},
                },
            ),
            patch.object(park, "_request", return_value={"loaded": False}),
            patch.object(park, "_members", side_effect=[{100}, {100, 102}]),
            patch.object(park, "_connected", return_value=0),
            patch.object(park, "_command") as cmd,
        ):
            with self.assertRaises(Fault):
                park.park_native(m, p)
            self.assertEqual(
                [c.args[0][2] for c in cmd.call_args_list], ["freeze", "thaw"]
            )


if __name__ == "__main__":
    unittest.main()
