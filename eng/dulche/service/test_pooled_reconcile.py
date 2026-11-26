"""Exercise exact retirement, negative identity cases and interrupted cleanup."""
from contextlib import ExitStack
from dataclasses import asdict
import hashlib
import json
import os
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock

import pooled_reconcile as recovery
import pooled_runtime as runtime
import pooled_engine


class ReconciliationTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.runtime = self.root / "run"
        self.runtime.mkdir(mode=0o700)
        state = self.root / "state"
        engines = state / "engines"
        engines.mkdir(parents=True, mode=0o700)
        self.unit = "dulche-pool-" + hashlib.sha256(str(state.resolve()).encode()).hexdigest()[:12] + ".service"
        self.record = engines / (self.unit + ".json")
        self.status = self.record.with_suffix(".pool-status.json")
        self.boot = Path("/proc/sys/kernel/random/boot_id").read_text().strip()
        self.pin = runtime.RpcProcess("a" * 8 + "-" + "b" * 4 + "-" + "c" * 4 + "-" + "d" * 4 + "-" + "e" * 12,
                                     1234, "88", "/home/v100/strata/bridge/bin/ggml-rpc-server",
                                     "f" * 64, "e" * 64, "GPU-11111111-2222-3333-4444-555555555555")
        self.recipe = {"schema": "dulche-pool-runtime/v1", "alias": runtime.POOL_MODEL_ID,
                       "physical_devices": list(runtime.DEVICE_ROLES), "launch_id": "a" * 32,
                       "backend_sha256": "b" * 64, "rpc_process": asdict(self.pin)}
        self.receipt = {"schema": "dulche-pool-status/v1", "launch_id": "a" * 32,
                        "wrapper_pid": 901234, "wrapper_start_ticks": "999", "backend_pid": 901235,
                        "systemd_invocation": "c" * 32, "rpc_identity": self.pin.key,
                        "phase": "stop_unverified", "status": "stop_unverified", "stop_verified": False,
                        "trigger": "callback_failure", "errors": {"read": "TimeoutError"}, "exit_code": -9}
        self.fence = {"schema": recovery.FENCE_SCHEMA, "host_boot_id": self.boot,
                      "host_wrapper_pid": 901234, "host_wrapper_start_ticks": "999",
                      "backend_sha256": "b" * 64, "argv_sha256": "d" * 64,
                      "owner_token": "e" * 48, "guest_rpc": asdict(self.pin)}
        self.write(self.record, self.recipe)
        self.write(self.status, self.receipt)
        self.fences = [runtime._quarantine_path(self.runtime, role) for role in runtime.DEVICE_ROLES]
        for path in self.fences:
            self.write(path, self.fence)
        self.mocks = self.enterContext(ExitStack())
        self.unit_check = self.mocks.enter_context(mock.patch.object(recovery, "_unit_stopped", return_value={
            "ActiveState": "failed", "MainPID": "0", "ControlPID": "0", "ControlGroup": "", "InvocationID": "c" * 32}))
        self.host_check = self.mocks.enter_context(mock.patch.object(recovery, "_host_processes_stopped"))
        self.display = self.mocks.enter_context(mock.patch.object(runtime, "capture_display", return_value=None))
        self.nvml = self.mocks.enter_context(mock.patch.object(runtime, "nvml_snapshot", return_value={"allocations": []}))
        self.allocations = self.mocks.enter_context(mock.patch.object(runtime, "host_compute_allocations", return_value=[]))
        self.guest = self.mocks.enter_context(mock.patch.object(runtime, "GuestRPC"))
        self.guest.return_value.stopped.return_value = True

    @staticmethod
    def write(path, value):
        path.write_text(json.dumps(value, sort_keys=True))
        path.chmod(0o600)

    def run_recovery(self):
        return recovery.reconcile_release(self.record, self.unit, runtime_dir=self.runtime)

    def assert_preserved(self):
        self.assertTrue(all(path.exists() for path in self.fences))
        self.assertEqual(json.loads(self.status.read_text()), self.receipt)

    def test_success_retires_exact_fences_and_preserves_original_failure(self):
        result = self.run_recovery()
        self.assertTrue(result["stop_verified"])
        self.assertEqual(result["phase"], "stopped")
        self.assertTrue(result["reconciliation"]["fences_retired"])
        self.assertEqual(result["errors"], {"read": "TimeoutError"})
        self.assertEqual(result["exit_code"], -9)
        self.assertEqual(result["trigger"], "callback_failure")
        self.assertEqual(result["reconciliation"]["original_phase"], "stop_unverified")
        self.assertFalse(any(path.exists() for path in self.fences))
        self.guest.assert_called_once_with(self.pin)
        runtime.assert_devices_available(runtime.DEVICE_ROLES, runtime_dir=self.runtime)
        # A repeated reconciliation re-verifies release; it cannot erase new fences.
        self.assertTrue(self.run_recovery()["reconciliation"]["fences_retired"])

    def test_existing_device_claim_blocks_reconciliation(self):
        for path in self.fences:
            path.unlink()
        with runtime.claim_devices(runtime_dir=self.runtime):
            for path in self.fences:
                self.write(path, self.fence)
            with self.assertRaisesRegex(runtime.RuntimeError_, "still holds"):
                self.run_recovery()
        self.assert_preserved()

    def test_mismatched_fences_or_status_never_retire_either_claim(self):
        for changes in ({"owner_token": "f" * 48}, {"host_boot_id": "wrong-boot"},
                        {"host_wrapper_start_ticks": "998"}, {"backend_sha256": "a" * 64},
                        {"guest_rpc": {**asdict(self.pin), "start_ticks": "89"}},
                        {"schema": runtime.RPC_START_SCHEMA}):
            with self.subTest(changes=changes):
                self.write(self.fences[1], {**self.fence, **changes})
                with self.assertRaises(runtime.RuntimeError_):
                    self.run_recovery()
                self.assert_preserved()
        self.fences[1].write_text(json.dumps(self.fence, indent=2))
        with self.assertRaisesRegex(runtime.RuntimeError_, "fence bytes differ"):
            self.run_recovery()
        self.assert_preserved()
        self.write(self.fences[1], self.fence)
        self.write(self.status, {**self.receipt, "launch_id": "b" * 32})
        with self.assertRaises(runtime.RuntimeError_):
            self.run_recovery()
        self.assertTrue(all(path.exists() for path in self.fences))

    def test_guest_failure_or_timeout_preserves_status_and_fences(self):
        for error in (None, TimeoutError("fixture"), RuntimeError("identity mismatch")):
            with self.subTest(error=error):
                self.guest.return_value.stopped.side_effect = error
                self.guest.return_value.stopped.return_value = False
                with self.assertRaises((RuntimeError, TimeoutError)):
                    self.run_recovery()
                self.assert_preserved()

    def test_live_unit_host_pid_or_compute_blocks_retirement(self):
        for target in (self.unit_check, self.host_check):
            with self.subTest(target=target):
                target.side_effect = runtime.RuntimeError_("still live")
                with self.assertRaises(runtime.RuntimeError_):
                    self.run_recovery()
                self.assert_preserved()
                target.side_effect = None
        self.allocations.return_value = [{"pid": 123, "bytes": 4096}]
        with self.assertRaisesRegex(runtime.RuntimeError_, "compute allocations"):
            self.run_recovery()
        self.assert_preserved()

    def test_changed_fence_during_proof_is_preserved(self):
        def changed(_):
            self.write(self.fences[1], {**self.fence, "owner_token": "f" * 48})
            return True
        self.guest.return_value.stopped.side_effect = changed
        with self.assertRaisesRegex(runtime.RuntimeError_, "changed during"):
            self.run_recovery()
        self.assert_preserved()

    def test_interrupted_retirement_requires_write_ahead_proof_and_reverifies(self):
        def partial(fences):
            next(iter(fences)).unlink()
            raise OSError("simulated interruption between fence deletions")
        with mock.patch.object(runtime, "_retire_residency", side_effect=partial):
            with self.assertRaises(OSError):
                self.run_recovery()
        status = json.loads(self.status.read_text())
        self.assertTrue(status["stop_verified"])
        self.assertFalse(status["reconciliation"]["fences_retired"])
        self.assertEqual(sum(path.exists() for path in self.fences), 1)
        self.assertTrue(self.run_recovery()["reconciliation"]["fences_retired"])
        self.assertEqual(self.guest.return_value.stopped.call_count, 2)
        self.assertFalse(any(path.exists() for path in self.fences))

    def test_missing_fence_without_retirement_proof_is_refused(self):
        self.fences[0].unlink()
        with self.assertRaisesRegex(runtime.RuntimeError_, "missing without"):
            self.run_recovery()
        self.assertTrue(self.fences[1].exists())
        self.assertEqual(json.loads(self.status.read_text()), self.receipt)

    def test_both_device_locks_stay_held_during_proof(self):
        def check(_):
            with self.assertRaises(runtime.RuntimeError_):
                runtime.assert_devices_available(runtime.DEVICE_ROLES, runtime_dir=self.runtime)
            return True
        self.guest.return_value.stopped.side_effect = check
        self.run_recovery()

    def test_engine_release_retries_unverified_and_interrupted_terminal_proofs(self):
        manager = SimpleNamespace(_record=lambda _: self.record, _name=lambda _: self.unit)
        inactive = {"ActiveState": "inactive", "MainPID": "0", "ControlGroup": ""}
        for interrupted in (False, True):
            with self.subTest(interrupted=interrupted), mock.patch.dict(os.environ, {"XDG_RUNTIME_DIR": str(self.runtime)}):
                self.write(self.status, self.receipt)
                for path in self.fences:
                    self.write(path, self.fence)
                if interrupted:
                    def partial(fences):
                        next(iter(fences)).unlink()
                        raise OSError("simulated interruption")
                    with mock.patch.object(runtime, "_retire_residency", side_effect=partial):
                        with self.assertRaises(OSError):
                            self.run_recovery()
                command = mock.Mock()
                result = pooled_engine.release(manager, {"id": runtime.POOL_MODEL_ID}, inactive,
                                               self.recipe, command, lambda _: inactive, lambda _: set())
                self.assertTrue(result["release_verified"])
                self.assertEqual(result["state"], "unloaded")
                self.assertEqual(result["pool_stop"]["errors"], {"read": "TimeoutError"})
                command.assert_not_called()
                self.assertFalse(any(path.exists() for path in self.fences))


class UnitProofTests(unittest.TestCase):
    def test_loaded_inactive_and_collected_unit_require_complete_zero_owner_metadata(self):
        good = {"LoadState": "loaded", "ActiveState": "failed", "MainPID": "0", "ControlPID": "0",
                "ControlGroup": "", "InvocationID": "c" * 32}
        collected = {**good, "LoadState": "not-found", "ActiveState": "inactive", "InvocationID": ""}
        variants = [(good, 0, True), (collected, 1, True),
                    ({**good, "ActiveState": "active"}, 0, False),
                    ({**good, "MainPID": "50"}, 0, False),
                    ({**good, "ControlPID": "50"}, 0, False),
                    ({**good, "InvocationID": "d" * 32}, 0, False),
                    ({**good, "ControlGroup": "/system.slice/unrelated.service"}, 0, False),
                    ({**collected, "ControlGroup": "/user.slice/owned.service"}, 1, False),
                    ({}, 1, False)]
        for unit, code, allowed in variants:
            with self.subTest(unit=unit, code=code), mock.patch.object(recovery.subprocess, "run", return_value=SimpleNamespace(
                    returncode=code, stdout="\n".join(f"{key}={value}" for key, value in unit.items()))):
                if allowed:
                    recovery._unit_stopped("dulche-pool-fixture.service", "c" * 32, 2)
                else:
                    with self.assertRaises(runtime.RuntimeError_):
                        recovery._unit_stopped("dulche-pool-fixture.service", "c" * 32, 2)

    def test_actual_live_wrapper_and_backend_pids_are_refused(self):
        started = Path("/proc/self/stat").read_text().rsplit(")", 1)[1].split()[19]
        with self.assertRaisesRegex(runtime.RuntimeError_, "wrapper still exists"):
            recovery._host_processes_stopped(os.getpid(), started, 999999999)
        with self.assertRaisesRegex(runtime.RuntimeError_, "backend PID still exists"):
            recovery._host_processes_stopped(os.getpid(), str(int(started) + 1), os.getpid())


if __name__ == "__main__":
    unittest.main()
