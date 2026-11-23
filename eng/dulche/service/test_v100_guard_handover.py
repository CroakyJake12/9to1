"""No GPU operations; verify scoped standby ownership at the production guard handover boundary."""

import contextlib
import os
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

import v100_recover as recovery


class GuardHandoverTests(unittest.TestCase):
    def setUp(self):
        self.stack = contextlib.ExitStack()
        self.addCleanup(self.stack.close)
        self.root = Path(self.stack.enter_context(tempfile.TemporaryDirectory()))
        self.marker = self.root / "v100-standby"
        self.stack.enter_context(patch.object(recovery, "STANDBY", self.marker))
        self.stack.enter_context(patch.object(recovery, "active", return_value=True))
        self.stack.enter_context(
            patch.object(
                recovery, "detached_guard_ready", return_value=(os.getpid(), 60)
            )
        )
        self.stack.enter_context(patch.object(recovery.time, "sleep"))
        self.samples = self.stack.enter_context(
            patch.object(recovery, "rtx_temperature", return_value=39)
        )
        self.guest = self.stack.enter_context(
            patch.object(recovery, "safe_sensor", return_value={"temp": 31})
        )
        self.stack.enter_context(
            patch.object(
                recovery, "run", return_value=SimpleNamespace(stdout=str(os.getpid()))
            )
        )
        self.stack.enter_context(patch.object(recovery, "wait_for"))

    def test_existing_rtx_guard_is_kept_and_only_boot_marker_is_retired(self):
        with recovery.detached_boot_standby(self.root, {}):
            self.assertTrue(self.marker.exists())
            self.guest.assert_not_called()
        self.guest.assert_called_once_with(idle=True)
        self.assertFalse(self.marker.exists())
        self.assertGreaterEqual(self.samples.call_count, 5)

    def test_never_clears_a_preexisting_operator_marker(self):
        self.marker.touch()
        with (
            self.assertRaisesRegex(RuntimeError, "operator standby"),
            recovery.detached_boot_standby(self.root, {}),
        ):
            self.fail("must not enter recovery")
        self.assertTrue(self.marker.exists())

    def test_standby_is_removed_on_sensor_or_boot_failure(self):
        for fail_after_boot in (False, True):
            with self.subTest(fail_after_boot=fail_after_boot):
                self.guest.side_effect = (
                    RuntimeError("no safe sensor") if fail_after_boot else None
                )
                with (
                    self.assertRaises(RuntimeError),
                    recovery.detached_boot_standby(self.root, {}),
                ):
                    if not fail_after_boot:
                        raise RuntimeError("boot failed")
                self.assertFalse(self.marker.exists())

    def test_warming_rtx_rejects_boot_before_marker_is_created(self):
        self.samples.side_effect = (40, 41, 43, 45)
        with (
            self.assertRaisesRegex(RuntimeError, "stabilized"),
            recovery.detached_boot_standby(self.root, {}),
        ):
            self.fail("must not enter recovery")
        self.assertFalse(self.marker.exists())


if __name__ == "__main__":
    unittest.main()
