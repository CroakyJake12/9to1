import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import training_window_recovery as recovery


class RecoveryTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        (self.root / "training").mkdir()
        self.cfg = {
            "state_dir": temp.name,
            "owner": "first",
            "cycle": "original",
            "deadline_epoch": 100,
        }
        self.window = {"owner": "first", "phase": "held", "created_epoch": 0}
        self.req = self.enterContext(
            patch.object(recovery, "request", return_value=self.window)
        )
        self.now = self.enterContext(
            patch.object(recovery.time, "time", return_value=50)
        )
        self.stopped = self.enterContext(patch.object(recovery, "require_no_training"))

    def test_no_window_release_before_failure_or_deadline(self):
        self.assertEqual(recovery.tick(self.root, self.cfg)[0], "armed")
        self.stopped.assert_not_called()
        self.assertEqual(self.req.call_count, 1)

    def test_deadline_waits_for_actual_worker_and_terminal_status(self):
        self.now.return_value = 100
        self.stopped.side_effect = RuntimeError("worker active")
        with self.assertRaisesRegex(RuntimeError, "worker active"):
            recovery.tick(self.root, self.cfg)
        self.stopped.side_effect = None
        p = self.root / "training/jobs/test/status.json"
        p.parent.mkdir(parents=True)
        p.write_text(json.dumps({"device": recovery.RTX, "phase": "running"}))
        self.assertIn("terminal", recovery.tick(self.root, self.cfg)[1])
        p.write_text(json.dumps({"device": recovery.RTX, "phase": "completed"}))
        recovery.tick(self.root, self.cfg)
        self.req.assert_called_with("release", "first")

    def test_restore_verifies_live_profile_before_resuming_only_original_scheduler(
        self,
    ):
        d = self.root / "engines"
        d.mkdir()
        cfg = d / "strata-owned.json"
        cfg.write_text('{"api_key":"fixture"}')
        self.window.update(
            phase="restored",
            models=[
                {
                    "id": recovery.PRIMARY,
                    "native": {
                        "profile_digest": hashlib.sha256(cfg.read_bytes()).hexdigest()
                    },
                }
            ],
        )
        with (
            patch.object(recovery, "resume", return_value="resumed") as resume,
            patch.object(
                recovery, "_payload", return_value={"loaded": False}
            ) as health,
        ):
            self.assertEqual(recovery.tick(self.root, self.cfg)[0], "waiting")
            resume.assert_not_called()
            health.return_value = {"loaded": True, "model": recovery.PRIMARY}
            self.assertEqual(
                recovery.tick(self.root, self.cfg), ("completed", "resumed")
            )
            resume.assert_called_once()

    def test_replaced_invocation_receives_no_signal(self):
        cfg = {"scheduler_pid": 10, "scheduler_invocation": "original"}
        with (
            patch.object(
                recovery,
                "_unit",
                return_value={"InvocationID": "other", "MainPID": "10"},
            ),
            patch.object(recovery.os, "pidfd_open") as opened,
        ):
            self.assertIn("exited", recovery.resume(cfg))
            opened.assert_not_called()


if __name__ == "__main__":
    unittest.main()
