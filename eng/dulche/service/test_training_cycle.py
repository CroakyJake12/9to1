"""Offline cycle protocol tests. Fixtures do not train a model, access a GPU, or start systemd."""

import datetime as dt
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import training_cycle as cycle
import training_jobs as jobs
from engine_profiles import atomic_json


def artifact(path, content):
    path.write_bytes(content)
    return {"path": str(path), "sha256": hashlib.sha256(content).hexdigest()}


class Trainer:
    def __init__(self, state, w):
        self.root = state / "training"
        self.w = w
        self.starts = []
        self.pauses = []

    def workflow(self, _):
        return self.w, "8" * 64

    def job(self, id):
        return self.root / "jobs" / id

    def launch(self, workflow, parent=None, *, attempt, cycle, cutoff, directives):
        self.starts.append((attempt, parent))
        p = self.job(attempt)
        p.mkdir(parents=True)
        j = {
            "id": attempt,
            "cycle": cycle,
            "phase": "running",
            "device": "cpu",
            "steps": 0,
            "started_epoch": 100,
            "created_epoch": 95,
            "worker_pid": 999,
            "worker_unit": "dulche-training-" + attempt + "-worker.service",
            "supervisor_unit": "dulche-training-" + attempt + ".service",
        }
        if parent:
            j["steps"] = jobs.json_file(self.job(parent) / "status.json")["steps"]
        atomic_json(p / "status.json", j)
        return j

    def pause(self, attempt):
        self.pauses.append(attempt)


class Resources:
    def __init__(self):
        self.state = {"phase": "idle", "owner": None, "models": []}
        self.restores = 0
        self.demand = False

    def __call__(self, path, body=None):
        if path == "models/mentor":
            return {"state": "loaded"}
        if path == "resources/training-window/acquire":
            self.state = {
                "owner": body["owner"],
                "phase": "held",
                "release_valid": True,
                "models": [
                    {
                        "id": mid,
                        "prior_state": "loaded",
                        "release": {"release_verified": True},
                    }
                    for mid in body["models"]
                ],
            }
        elif path == "resources/training-window/release":
            assert self.state["owner"] == body["owner"]
            self.restores += 1
            self.state["phase"] = "restored"
        elif path != "resources/training-window":
            raise AssertionError(path)
        return {**self.state, "direct_inference_waiting": self.demand}


class CycleTests(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory()
        self.addCleanup(self.scratch.cleanup)
        self.root = Path(self.scratch.name)
        self.now = 100.0
        f = artifact(
            self.root / "dataset",
            json.dumps(
                {
                    "files": [
                        {"split": s, "sha256": str(i) * 64}
                        for i, s in enumerate(
                            ["train", "validation", "final_held_out"], 1
                        )
                    ]
                }
            ).encode(),
        )
        self.trainer = Trainer(
            self.root,
            {
                "dataset_manifest": f,
                "limits": {
                    "checkpoint_grace_seconds": 8,
                    "progress_timeout_seconds": 60,
                },
            },
        )
        self.res = Resources()
        self.units = "inactive"
        self.m = cycle.Cycle(
            self.root,
            "fixture",
            trainer=self.trainer,
            api=self.res,
            clock=lambda: self.now,
            unit=lambda _: self.units,
        )
        plan = {
            "schema": cycle.SCHEMA,
            "id": "fixture",
            "objective": "Fixture protocol only",
            "mentor": "mentor",
            "workflow": "w",
            "models": ["mentor"],
            "required_checks": ["behavior", "regression"],
            "validation_id": "dev",
            "heldout_id": "final",
            "target_cycles": 2,
            "max_cycles": 3,
            "deadline": dt.datetime.fromtimestamp(3700, dt.UTC).isoformat(),
        }
        p = self.root / "plan"
        atomic_json(p, plan)
        with patch.object(cycle.time, "time", return_value=self.now):
            self.m.create(p, jobs.digest(p))

    def tick(self, want):
        d = self.m.tick()
        self.assertEqual(want, d["phase"], d.get("last_error") or d["reason"])
        return d

    def gate(self, stage, score, decision="continue", ok=True):
        d = self.tick(stage)
        g = self.m.request(d)
        ev = artifact(
            self.root / ("e" + g["request_id"]),
            ("independent fixture " + stage).encode(),
        )
        ctx = artifact(
            self.root / ("c" + g["request_id"]),
            ("fixture context " + g["request_id"]).encode(),
        )
        r = {
            k: g[k]
            for k in (
                "cycle",
                "request_id",
                "stage",
                "benchmark_id",
                "benchmark_sha256",
            )
        }
        r.update(
            checkpoint_sha256=(g.get("checkpoint") or {}).get("sha256"),
            score=score,
            checks={"behavior": ok, "regression": ok},
            evidence=ev,
            mentor_context=ctx,
            decision=decision,
        )
        if stage == "review":
            r["optimizer"] = {
                "updates": g["optimizer_updates_expected"],
                "parameter_before_sha256": "1" * 64,
                "parameter_after_sha256": "2" * 64,
                "optimizer_state_sha256": "3" * 64,
            }
        if stage == "final":
            r["heldout_previously_unseen"] = True
        return g, r

    def submit(self, r):
        p = self.root / "submit.json"
        atomic_json(p, r)
        return self.m.submit(p)

    def start_worker(self):
        a = self.tick("acquire")["active"]
        self.now += 3
        self.tick("launch")
        self.tick("training")
        return a["attempt"]

    def complete_worker(self, attempt, steps):
        self.now += 10
        folder = self.trainer.job(attempt)
        ref = artifact(
            folder / "checkpoint",
            f"fixture weights+optimizer at {attempt}:{steps}".encode(),
        )
        j = jobs.json_file(folder / "status.json")
        j.update(
            phase="paused",
            steps=steps,
            last_progress_epoch=self.now - 1,
            worker_stop_verified=True,
            checkpoint={**ref, "steps": steps},
            ended_epoch=self.now,
        )
        atomic_json(folder / "status.json", j)
        return ref

    def test_two_verified_rounds_adapt_and_finish_on_distinct_unseen_split(self):
        g, r = self.gate("baseline", 0.5)
        self.now += 4
        self.submit(r)
        self.tick("plan")
        first = self.start_worker()
        self.complete_worker(first, 1)
        self.tick("restore")
        self.now += 2
        self.tick("review")
        _, r = self.gate("review", 0.65)
        self.now += 3
        self.submit(r)
        d = self.tick("plan")
        self.assertEqual(
            1,
            d["rounds"][0]["review"]["optimizer"]["updates"],
        )
        second = self.start_worker()
        self.assertEqual(first, self.trainer.starts[-1][1])
        d = jobs.json_file(self.m.file)
        self.assertGreater(d["active"]["allocation"]["measured_updates_s"], 0)
        self.complete_worker(second, 4)
        self.tick("restore")
        self.tick("review")
        _, r = self.gate("review", 0.7, decision="finish")
        self.submit(r)
        self.tick("final")
        final, r = self.gate("final", 0.6)
        self.assertNotEqual(g["benchmark_sha256"], final["benchmark_sha256"])
        self.submit(r)
        d = self.tick("completed")
        self.assertEqual(second, d["best"]["attempt"])
        self.assertEqual(2, self.res.restores)
        self.assertEqual(
            4, cycle.list_cycles(self.root)[0]["verified_optimizer_updates"]
        )

    def test_rollback_keeps_prior_best_and_rejects_missing_optimizer_or_changed_context(
        self,
    ):
        _, r = self.gate("baseline", 0.5)
        self.submit(r)
        self.tick("plan")
        first = self.start_worker()
        self.complete_worker(first, 2)
        self.tick("restore")
        self.tick("review")
        _, r = self.gate("review", 0.7)
        del r["optimizer"]["optimizer_state_sha256"]
        with self.assertRaises(ValueError):
            self.submit(r)
        _, r = self.gate("review", 0.7)
        self.submit(r)
        self.tick("plan")
        second = self.start_worker()
        self.complete_worker(second, 3)
        self.tick("restore")
        self.tick("review")
        _, r = self.gate("review", 0.9, ok=False)
        self.submit(r)
        d = self.tick("plan")
        self.assertFalse(d["rounds"][-1]["accepted"])
        self.assertEqual(first, d["best"]["attempt"])
        self.start_worker()
        self.assertEqual(first, self.trainer.starts[-1][1])

    def test_pause_mid_training_and_refuse_restoration_without_unit_stop(self):
        _, r = self.gate("baseline", 0.5)
        self.submit(r)
        self.tick("plan")
        job = self.start_worker()
        self.m.control("pause")
        self.tick("training")
        self.assertIn(job, self.trainer.pauses)
        self.complete_worker(job, 1)
        self.units = "active"
        self.tick("training")
        self.assertEqual(0, self.res.restores)
        self.units = "inactive"
        self.tick("restore")
        self.tick("paused")
        self.m.control("resume")
        self.tick("review")
        self.assertEqual(1, len(self.trainer.starts))

    def test_deadline_blocks_first_worker_and_is_scoped_to_one_exact_unit(self):
        self.now = 3701
        self.m.control("deadline")
        self.tick("no_training")
        self.assertFalse(self.trainer.starts)

    def test_bad_gate_and_absent_hold_never_launch_training(self):
        g, r = self.gate("baseline", 0.3)
        r["benchmark_sha256"] = "0" * 64
        with self.assertRaises(ValueError):
            self.submit(r)
        self.assertFalse(self.trainer.starts)
        _, r = self.gate("baseline", 0.3)
        self.submit(r)
        self.tick("plan")
        self.tick("acquire")
        self.tick("launch")
        self.res.state["release_valid"] = False
        d = self.tick("launch")
        self.assertIn("not currently verified", d["last_error"])
        self.assertFalse(self.trainer.starts)

    def test_adopt_scopes_existing_attempt_without_relaunch_and_hard_stop_targets_only_it(
        self,
    ):
        _, receipt = self.gate("baseline", 0.5)
        self.submit(receipt)
        d = self.tick("plan")
        owner = "fixture:first"
        d["plan"]["first_window_owner"] = owner
        self.m.save(d)
        self.res(
            "resources/training-window/acquire", {"owner": owner, "models": ["mentor"]}
        )
        attempt = "b" * 32
        j = self.trainer.launch(
            "w", attempt=attempt, cycle=None, cutoff=3700, directives=None
        )
        j.update(
            workflow="w",
            workflow_digest=d["workflow_sha256"],
            absolute_deadline=3700,
            resume_of=None,
        )
        atomic_json(self.trainer.job(attempt) / "status.json", j)
        self.assertFalse(self.m.adopt(attempt, owner)["worker_started_by_cycle"])
        self.assertTrue(self.m.adopt(attempt, owner)["already_adopted"])
        self.now = 3701
        self.units = "active"
        with patch.object(jobs, "run") as command:
            self.m.control("deadline")
        command.assert_called_once_with(
            ["systemctl", "--user", "stop", "--no-block", j["worker_unit"]]
        )
        self.assertEqual([attempt], self.trainer.pauses)
        self.assertEqual(1, len(self.trainer.starts))

    def test_missing_final_reservation_does_not_consume_or_certify_heldout(self):
        d = jobs.json_file(self.m.file)
        d["plan"]["heldout_pending"] = True
        self.m.event(d, "final", "test pending")
        state = self.tick("final")
        self.assertIn("not been reserved", state["last_error"])
        self.assertFalse((self.m.stage_dir(d) / "request.json").exists())
        self.assertNotIn("final", state)

    def test_no_new_steps_can_never_be_marked_as_real_training(self):
        _, r = self.gate("baseline", 0.4)
        self.submit(r)
        self.tick("plan")
        job = self.start_worker()
        self.complete_worker(job, 0)
        self.tick("restore")
        d = self.tick("paused")
        self.assertEqual([], d["rounds"])
        self.assertEqual(
            0, cycle.list_cycles(self.root)[0]["verified_optimizer_updates"]
        )


if __name__ == "__main__":
    unittest.main()
