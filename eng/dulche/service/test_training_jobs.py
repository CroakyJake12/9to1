"""Pure software fixtures. No updates to model weights, production jobs, or systemd timers."""

import datetime as dt
import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import training_budget as budget
import training_jobs as training


class ComputeTests(unittest.TestCase):
    def setUp(self):
        self.limits = {
            "checkpoint_reserve_bytes": 1000,
            "min_free_ram_mib": 4096,
            "ram_mib": 1024,
            "vram_mib": 1024,
            "max_gpu_temperature_c": 68,
        }
        self.host = {
            "available_disk_bytes": 51 * budget.GiB,
            "available_ram_bytes": 5 * budget.GiB,
            "full_psi_10s": 0,
            "swapped_pages": 0,
        }
        self.group = {"cpu_usec": 0, "memory_bytes": 500, "cgroup_oom_kills": 0}
        self.gpu = {
            "temperature_c": 30,
            "other_sm_pct": 0,
            "worker_sm_pct": 0,
            "worker_vram_mib": 20,
            "free_vram_mib": 2048,
        }
        self.guard = budget.ComputeBudget()
        self.guard.origin = 0

    def evaluate(self, now, gpu=None):
        return self.guard.assess(self.limits, self.host, self.group, gpu, now)

    def test_cpu_percent_is_fraction_of_total_compute(self):
        self.evaluate(0)
        self.group["cpu_usec"] = 400_000
        with mock.patch.object(os, "cpu_count", return_value=4):
            allowed, reason, measured = self.evaluate(1)
        self.assertTrue(allowed)
        self.assertEqual((reason, measured, self.guard.mean), ("cpu-quota", 10, 10))

    def test_gpu_uses_sm_measurement_and_expiring_duty_windows(self):
        self.evaluate(0, self.gpu)
        self.assertTrue(self.evaluate(0.3, self.gpu)[0])
        self.gpu["worker_vram_mib"] = 999
        allow, reason, pct = self.evaluate(2, self.gpu)
        self.assertFalse(allow)
        self.assertEqual(reason, "budget-idle")
        self.assertEqual(pct, 0)
        self.gpu["worker_sm_pct"] = 80
        for second in range(3, 17):
            ret = self.evaluate(second, self.gpu)
        self.assertEqual(ret[1], "measured-compute-overrun")

    def test_storage_pressure_worker_oom_and_stale_samples_refuse_work(self):
        self.evaluate(0)
        self.host["available_disk_bytes"] = 50 * budget.GiB
        self.assertEqual(self.evaluate(1)[1], "storage-floor")
        self.host["available_disk_bytes"] = 51 * budget.GiB
        self.group["cgroup_oom_kills"] = 1
        self.assertEqual(self.evaluate(2)[1], "worker-memory-budget")
        self.group["cgroup_oom_kills"] = 0
        self.assertEqual(self.evaluate(20)[1], "measurement-gap")

    def test_competing_gpu_work_and_heat_yield(self):
        self.gpu["other_sm_pct"] = 12
        self.assertEqual(
            self.evaluate(0, self.gpu)[1], "gpu-temperature-or-competing-work"
        )
        self.gpu.update(other_sm_pct=0, temperature_c=68)
        self.assertEqual(
            self.evaluate(1, self.gpu)[1], "gpu-temperature-or-competing-work"
        )

    def test_pmon_attributes_only_owned_pids_and_labels_unknowns(self):
        lines = "# gpu pid type sm mem fb command\n0 20 C 7 2 323 trainer\n0 30 G 2 1 100 user\n"
        sample = budget.parse_process_samples(lines, {20}, 10)
        self.assertEqual(
            (
                sample["worker_sm_pct"],
                sample["other_sm_pct"],
                sample["worker_vram_mib"],
            ),
            (7, 2, 323),
        )
        sample = budget.parse_process_samples(
            lines.replace("20 C 7", "20 C -"), {20}, 10
        )
        self.assertEqual(
            (sample["worker_sm_pct"], sample["worker_measurement"]),
            (10, "total-gpu-upper-bound"),
        )
        sample = budget.parse_process_samples(
            lines.replace("30 G 2", "30 G -"), {20}, 10
        )
        self.assertEqual(
            (sample["other_sm_pct"], sample["other_measurement"]),
            (10, "total-gpu-upper-bound"),
        )
        with self.assertRaises(ValueError):
            budget.parse_process_samples("# invalid\n", {20}, 10)


class JobTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.m = training.TrainingJobs(self.root)

    def test_status_and_absent_workflow_make_no_files_or_processes(self):
        with mock.patch.object(training, "run") as cmd:
            result = training.training_status(self.root)
            self.assertEqual(
                (result["state"], result["jobs"], result["monitor_registered"]),
                ("unconfigured", [], False),
            )
            with self.assertRaisesRegex(ValueError, "No independently"):
                self.m.launch("absent")
            cmd.assert_not_called()
            self.assertEqual(list(self.root.iterdir()), [])

    def test_fixture_never_admitted_as_real_training(self):
        source = self.root / "test-config.json"
        source.write_text(
            json.dumps({"schema": training.SCHEMA, "purpose": "software_test_only"})
        )
        with mock.patch.object(training, "register_timer") as timer:
            with self.assertRaisesRegex(ValueError, "real workflows"):
                self.m.register(source, training.digest(source))
            timer.assert_not_called()

    def test_calendar_exactly_one_hour_after_process_start_and_persistent(self):
        start = 1795451520.125
        service, timer = training.render_check_units(self.root, "abc", start)
        due = dt.datetime.fromtimestamp(start + 3600, dt.UTC).strftime(
            "%Y-%m-%d %H:%M:%S.%f UTC"
        )
        self.assertIn("OnCalendar=" + due, timer)
        self.assertIn("Persistent=true", timer)
        self.assertIn("AccuracySec=1us", timer)
        self.assertIn("hour-check", service)

    def test_registration_uses_only_fake_user_manager_and_checks_enabling(self):
        responses = []

        def fake(args, **kwargs):
            responses.append(args)
            return subprocess.CompletedProcess(
                args, 0, "active\n" if "is-active" in args else "", ""
            )

        with (
            mock.patch.dict(os.environ, {"XDG_CONFIG_HOME": str(self.root)}),
            mock.patch.object(training, "run", side_effect=fake),
        ):
            value = training.register_timer(self.root, "fixture", 100.125)
        self.assertEqual(value["scheduled_epoch"], 3700.125)
        self.assertEqual(
            [a[2] for a in responses], ["daemon-reload", "enable", "is-active"]
        )
        self.assertEqual(
            len(
                list(
                    (self.root / "systemd/user").glob("dulche-training-check-fixture.*")
                )
            ),
            2,
        )

    def test_existing_different_timer_is_not_stopped_or_replaced(self):
        d = self.root / "systemd/user"
        d.mkdir(parents=True)
        p = d / "dulche-training-check-fixture.timer"
        p.write_text("unrelated")
        with (
            mock.patch.dict(os.environ, {"XDG_CONFIG_HOME": str(self.root)}),
            mock.patch.object(training, "run") as mocked,
        ):
            with self.assertRaisesRegex(ValueError, "different contents"):
                training.register_timer(self.root, "fixture", 100)
            self.assertFalse(
                any(
                    "disable" in arg.args[0] or "stop" in arg.args[0]
                    for arg in mocked.call_args_list
                )
            )
        self.assertEqual(p.read_text(), "unrelated")

    def test_checkpoint_hash_path_size_and_step_checked_before_resume(self):
        d = self.root / "checkpoints"
        d.mkdir()
        f = d / "blob"
        f.write_bytes(b"software fixture; no training content")
        receipt = {"path": "blob", "sha256": training.digest(f), "steps": 4}
        self.assertEqual(training.checkpoint(self.root, receipt, 1024)["steps"], 4)
        for invalid in (
            {**receipt, "path": "../escape"},
            {**receipt, "sha256": "0" * 64},
            {**receipt, "steps": -1},
        ):
            with (
                self.subTest(invalid=invalid),
                self.assertRaises((ValueError, OSError)),
            ):
                training.checkpoint(self.root, invalid, 1024)

    def test_published_resume_survives_worker_copy_truncation(self):
        work = self.root / "output"
        d = work / "checkpoints"
        d.mkdir(parents=True)
        candidate = d / "fixture"
        candidate.write_bytes(b"software custody check only")
        rec = {"path": "fixture", "sha256": training.digest(candidate), "steps": 2}
        with mock.patch.object(
            training,
            "host_sample",
            return_value={"available_disk_bytes": 100 * budget.GiB},
        ):
            cp = training.checkpoint(work, rec, 1024, self.root / "sealed")
        candidate.write_bytes(b"later worker modification")
        self.assertEqual(training.digest(cp["path"]), rec["sha256"])
        self.assertNotEqual(candidate.resolve(), Path(cp["path"]))

    def test_unadmitted_failing_worker_never_registers_monitor(self):
        j = self.m.root / "jobs/foo"
        (j / "output").mkdir(parents=True)
        s = {
            "id": "foo",
            "workflow": "demo",
            "workflow_digest": "digest",
            "phase": "requested",
            "steps": 0,
            "checkpoint": None,
            "device": "cpu",
            "worker_pid": None,
            "started_epoch": None,
            "monitor": None,
            "worker_unit": "mock-worker",
            "supervisor_unit": "mock-super",
        }
        training.atomic_json(j / "status.json", s)
        w = {
            "limits": {"wall_seconds": 30, "max_checkpoint_bytes": 1000},
            "device": "cpu",
        }
        dead = {
            "SubState": "failed",
            "Result": "exit-code",
            "ExecMainStatus": "1",
            "ActiveState": "failed",
        }
        with (
            mock.patch.object(self.m, "workflow", return_value=(w, "digest")),
            mock.patch.object(self.m, "_start_worker", return_value=(55, 10)),
            mock.patch.object(training, "unit_props", return_value=dead),
            mock.patch.object(training, "run"),
            mock.patch.object(training, "register_timer") as timer,
        ):
            result = self.m._supervise("foo")
            self.assertEqual(result["phase"], "failed")
            self.assertIsNone(result["monitor"])
            timer.assert_not_called()

    def test_ready_then_completed_fixture_registers_once_and_seals_progress(self):
        import time

        j = self.m.root / "jobs" / "nontraining-fixture"
        out = j / "output"
        (out / "checkpoints").mkdir(parents=True)
        base = out / "checkpoints" / "transport-only.bin"
        base.write_bytes(b"this is not a trained model")
        s = {
            "id": "nontraining-fixture",
            "last_progress_epoch": None,
            "mean_compute_pct": None,
            "workflow": "fixture",
            "phase": "requested",
            "workflow_digest": "pinned",
            "started_epoch": None,
            "worker_pid": None,
            "worker_unit": "fixture",
            "supervisor_unit": "fixture-super",
            "monitor": None,
            "steps": 0,
            "checkpoint": None,
            "device": "cpu",
        }
        training.atomic_json(j / "status.json", s)
        first = {
            "job": s["id"],
            "pid": 123,
            "steps": 0,
            "phase": "running",
            "training_started": True,
        }
        done = {
            **first,
            "steps": 1,
            "phase": "completed",
            "checkpoint": {
                "path": base.name,
                "steps": 1,
                "sha256": training.digest(base),
            },
        }
        fake_start = time.time() - 0.5
        w = {"device": "cpu"}
        w["limits"] = {
            "wall_seconds": 60,
            "progress_timeout_seconds": 30,
            "checkpoint_grace_seconds": 2,
            "checkpoint_reserve_bytes": 512,
            "max_checkpoint_bytes": 1024,
            "ram_mib": 500,
            "min_free_ram_mib": 1,
        }
        sequence = [
            {
                "MainPID": "123",
                "SubState": "running",
                "ControlGroup": "/user.slice/fixture",
            },
            {
                "MainPID": "0",
                "ExecMainPID": "123",
                "SubState": "exited",
                "Result": "success",
                "ExecMainStatus": "0",
            },
            {"ActiveState": "inactive"},
        ]
        training.atomic_json(out / "progress.json", first)

        def wait_once(_):
            training.atomic_json(out / "progress.json", done)

        host = {
            "time": time.time(),
            "available_disk_bytes": 100 * budget.GiB,
            "available_ram_bytes": 6 * budget.GiB,
            "full_psi_10s": 0,
            "swapped_pages": 0,
        }
        counters = {
            "pids": {123},
            "cpu_usec": 0,
            "memory_bytes": 128,
            "cgroup_oom_kills": 0,
        }
        timer = {"unit": "mock-only.timer", "scheduled_epoch": fake_start + 3600}
        with (
            mock.patch.object(self.m, "workflow", return_value=(w, "pinned")),
            mock.patch.object(self.m, "_start_worker", return_value=(123, fake_start)),
            mock.patch.object(training, "unit_props", side_effect=sequence),
            mock.patch.object(
                training, "register_timer", return_value=timer
            ) as register,
            mock.patch.object(training, "inference_busy", return_value=False),
            mock.patch.object(training, "group_counters", return_value=counters),
            mock.patch.object(training, "host_sample", return_value=host),
            mock.patch.object(training.time, "sleep", side_effect=wait_once),
            mock.patch.object(training, "run"),
        ):
            result = self.m._supervise(s["id"])
        self.assertEqual(result["phase"], "completed", result)
        self.assertEqual(result["steps"], 1)
        self.assertTrue(result["worker_stop_verified"])
        self.assertEqual(
            Path(result["checkpoint"]["path"]).parent, self.m.root / "checkpoints"
        )
        register.assert_called_once_with(self.root, s["id"], fake_start)

    def test_elapsed_health_check_distinguishes_failed_from_unverified(self):
        p = self.root / "output"
        p.mkdir()
        s = {
            "id": "test",
            "device": "cpu",
            "worker_unit": "fixture",
            "worker_pid": 90,
            "phase": "running",
            "steps": 0,
            "checkpoint": None,
            "started_epoch": 1,
            "monitor": {"scheduled_epoch": 3601},
        }
        with (
            mock.patch.object(
                training, "unit_props", return_value={"SubState": "dead"}
            ),
            mock.patch.object(training, "run", side_effect=OSError),
            mock.patch.object(training, "host_sample", return_value={}),
        ):
            self.assertEqual(training.health_report(s, self.root)["health"], "Failed")
            s.update(phase="starting", started_epoch=None)
            self.assertEqual(training.health_report(s, self.root)["health"], "Unknown")


if __name__ == "__main__":
    unittest.main()
