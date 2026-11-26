"""Standalone cold recovery must respect model claims without gating emergency stops."""
import fcntl
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import pooled_runtime
import v100_recover


class RecoveryAdmissionTests(unittest.TestCase):
    def test_cold_guard_preserves_policy_and_distinct_device_stop_owners(self):
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            script = root / "v100_recover.py"
            for name in ("native_guard.py", "pooled_guard.py"):
                (root / name).write_text("# offline test helper\n")
            cfg = {"devices": [
                {"id": "rtx5080", "warn_c": 60, "stop_c": 80, "clear_c": 6,
                 "allow_simulated": False, "min_host_available_mib": 8192},
                {"id": "v100", "warn_c": 60, "stop_c": 68, "clear_c": 5,
                 "power_limit_w": 165, "allow_simulated": False}], "interval_s": 1.0}
            (root / "v100-guard.json").write_text(json.dumps(cfg))
            with (patch.object(v100_recover, "active", return_value=False),
                  patch.object(v100_recover, "other_guard", return_value=False),
                  patch.object(v100_recover, "guard_runtime", return_value=root / "frozen-guard"),
                  patch.object(v100_recover, "SCRIPT", script),
                  patch.object(v100_recover, "run") as run):
                v100_recover.start_guard(root)
            saved = json.loads((root / "v100-guard.json").read_text())
            for old, new in zip(cfg["devices"], saved["devices"]):
                for key, value in old.items():
                    self.assertEqual(new[key], value)
                expected = "native_guard.py" if old["id"] == "rtx5080" else "pooled_guard.py"
                self.assertEqual(new["stop_cmd"], ["/usr/bin/python3", str(root / expected), "stop"])
            args = run.call_args.args[0]
            self.assertIn("WatchdogSec=20s", args)
            self.assertIn("WorkingDirectory=" + str(root / "frozen-guard"), args)
            self.assertIn("-B", args)

    def test_transport_only_restore_does_not_start_a_legacy_worker(self):
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            with (patch.object(v100_recover, "authority"),
                  patch.object(v100_recover, "assets"),
                  patch.object(v100_recover, "owned_vm", return_value=True),
                  patch.object(v100_recover, "safe_sensor") as sensor,
                  patch.object(v100_recover, "guarded") as guarded,
                  patch.object(v100_recover, "STANDBY", root / "standby"),
                  patch.object(v100_recover, "sample", return_value={"temp": 30}),
                  patch.object(v100_recover, "ssh") as ssh):
                result = v100_recover.up(root, start_worker=False)
                self.assertTrue(result["ready"])
                sensor.assert_called_once()
                guarded.assert_called_once()
                ssh.assert_not_called()

    def test_global_admission_held_across_the_recovery_boundary(self):
        with tempfile.TemporaryDirectory() as raw, patch.dict(os.environ, XDG_RUNTIME_DIR=raw):
            root = Path(raw)
            with v100_recover.cli_physical_admission():
                with (root / "dulche-engine-admission/resource.lock").open("a") as other:
                    with self.assertRaises(BlockingIOError):
                        fcntl.flock(other, fcntl.LOCK_EX | fcntl.LOCK_NB)
            with (root / "dulche-engine-admission/resource.lock").open("a") as other:
                fcntl.flock(other, fcntl.LOCK_EX | fcntl.LOCK_NB)

    def test_live_v100_lifetime_blocks_cli_recovery(self):
        with tempfile.TemporaryDirectory() as raw, patch.dict(os.environ, XDG_RUNTIME_DIR=raw):
            with pooled_runtime.claim_devices(("v100",), runtime_dir=raw):
                with self.assertRaisesRegex(RuntimeError, "owned by another model"):
                    with v100_recover.cli_physical_admission():
                        self.fail("Recovery must not enter an owned device")

    def test_unverified_release_blocks_cli_recovery(self):
        with tempfile.TemporaryDirectory() as raw, patch.dict(os.environ, XDG_RUNTIME_DIR=raw):
            fence = Path(raw) / "dulche-v100-gguf.lock.unverified.json"
            fence.write_text("{}")
            fence.chmod(0o600)
            with self.assertRaisesRegex(RuntimeError, "unverified prior release"):
                with v100_recover.cli_physical_admission():
                    self.fail("Recovery must not erase an unresolved device lifetime")


if __name__ == "__main__":
    unittest.main()
