"""Focused lifecycle regressions; no GPU, remote process, or service is started."""
import os
from dataclasses import asdict
from pathlib import Path
import tempfile
import threading
import time
from types import SimpleNamespace
import unittest
from unittest import mock

import controller
import engine_placement
import engines
from engine_profiles import (CPU, GPU_POOL, GPU_RTX, GPU_V100, INKLING, INKLING_POOL,
                             PRIMARY, V100, EngineBusy, EngineError, atomic_json)
import native_guard
import physical_admission
import pooled_engine
import pooled_guard
import pooled_runtime
import resource_transitions
import training_park


class PoolIntegrationTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.runtime = self.root / "run"
        self.runtime.mkdir(mode=0o700)
        env = mock.patch.dict(os.environ, {"XDG_RUNTIME_DIR": str(self.runtime)})
        env.start()
        self.addCleanup(env.stop)
        self.manager = engines.EngineManager(self.root / "state")
        self.model = self.root / "Inkling-2-Mini-UD-Q4_K_XL.gguf"
        self.model.write_bytes(b"GGUF" + b"0" * 32)
        self.backend = self.root / "llama-server"
        self.backend.write_bytes(b"fixture, never executed")
        self.guard = self.root / "guard.json"
        atomic_json(self.guard, {})
        self.manifest = dict(schema="dulche-pool-source/v1", alias=INKLING_POOL,
                             physical_devices=["rtx5080", "v100"], path=str(self.model),
                             backend_path=str(self.backend), backend_sha256="a" * 64,
                             guard_config=str(self.guard), context=32768, threads=4)
        atomic_json(self.manager.root / pooled_engine.MANIFEST, self.manifest)
        self.record = dict(launch_id="1" * 32, port=12345, key="fixture-key", context=32768,
                           threads=4, gpu=GPU_POOL, backend_path=str(self.backend))
        atomic_json(self.manager._record(INKLING_POOL), self.record)
        self.unit = dict(MainPID=str(os.getpid()), ActiveState="active", InvocationID="a" * 32)
        self.live = dict(schema="dulche-pool-status/v1", launch_id=self.record["launch_id"],
                         wrapper_pid=os.getpid(), wrapper_start_ticks=Path("/proc/self/stat").read_text().rsplit(")", 1)[1].split()[19],
                         systemd_invocation=self.unit["InvocationID"], observed_at=time.time(),
                         phase="running", dual_allocations_observed=True,
                         backend_pid=os.getpid(), budget={"devices": []})

    def save_status(self, **changes):
        atomic_json(pooled_engine.status_path(self.manager._record(INKLING_POOL)), {**self.live, **changes})

    def test_new_registration_preserves_native_profile_and_physical_identity(self):
        pool = self.manager._spec(INKLING_POOL)
        self.assertEqual(pool["physical_devices"], ["rtx5080", "v100"])
        self.assertEqual(pool["slots"], 1)
        self.assertEqual(self.manager.default_settings(INKLING_POOL)["profile"], "experimental")
        cfg = {"args": ["--max-context", "32768", "--native", str(self.model)]}
        with mock.patch.object(engines, "find_inkling_profile", return_value=(self.root / "native.json", cfg)):
            native = self.manager._spec(INKLING)
        self.assertEqual(native["gpu"], GPU_RTX)
        self.assertEqual(native["port"], 19042)
        self.assertNotEqual(self.manager._name(INKLING), self.manager._name(INKLING_POOL))
        self.assertFalse((self.manager.root / "inkling-owned.json").exists())

    def test_gpu_pool_refuses_incomplete_or_nonprivate_registration(self):
        for changes in ({"physical_devices": ["rtx5080"]}, {"alias": INKLING}, {"context": True}):
            atomic_json(self.manager.root / pooled_engine.MANIFEST, {**self.manifest, **changes})
            with self.subTest(changes=changes), self.assertRaises(EngineError):
                self.manager._spec(INKLING_POOL)
        path = self.manager.root / pooled_engine.MANIFEST
        atomic_json(path, self.manifest)
        path.chmod(0o644)
        with self.assertRaises(EngineError):
            self.manager._spec(INKLING_POOL)

    def test_missing_source_does_not_hide_an_owned_lifecycle(self):
        saved = {**self.manifest, **self.record, "schema": "dulche-pool-runtime/v1"}
        atomic_json(self.manager._record(INKLING_POOL), saved)
        (self.manager.root / pooled_engine.MANIFEST).unlink()
        self.assertEqual(self.manager._spec(INKLING_POOL)["id"], INKLING_POOL)
        with self.assertRaises(EngineError):
            pooled_engine.source(self.manager.root)

    def test_real_pool_claim_blocks_all_service_gpu_profiles_but_keeps_cpu(self):
        with pooled_runtime.claim_devices(runtime_dir=self.runtime):
            for mid, gpu in ((PRIMARY, GPU_RTX), (INKLING, GPU_RTX), (V100, GPU_V100),
                             ("local-model", GPU_V100), (INKLING_POOL, GPU_POOL)):
                with self.subTest(mid=mid), self.manager._change(physical=True), self.assertRaises(EngineBusy):
                    self.manager._admit_devices({"id": mid, "gpu": gpu})
            with self.manager._change(physical=True):
                self.manager._admit_devices({"id": CPU, "gpu": "CPU"})
        with self.manager._change(physical=True):
            self.manager._admit_devices({"id": INKLING_POOL, "gpu": GPU_POOL})

    def test_gpu_training_holds_same_physical_claim(self):
        with physical_admission.training_claim(pooled_runtime.RTX_UUID):
            with self.manager._change(physical=True), self.assertRaises(EngineBusy):
                self.manager._admit_devices({"id": INKLING_POOL, "gpu": GPU_POOL})
        marker = self.runtime / (pooled_runtime.LOCK_NAMES["rtx5080"] + ".unverified.json")
        marker.write_text("preserved failed cleanup")
        with self.assertRaisesRegex(RuntimeError, "unverified"):
            with physical_admission.training_claim(pooled_runtime.RTX_UUID):
                self.fail("Quarantined training was admitted")

    def test_parked_native_restore_cannot_bypass_a_pool_claim(self):
        native = {"id": PRIMARY, "gpu": GPU_RTX}
        with pooled_runtime.claim_devices(runtime_dir=self.runtime), \
             mock.patch.object(training_park, "_unit", return_value={"ActiveState": "active"}), \
             mock.patch.object(self.manager, "_spec", return_value=native), \
             mock.patch.object(training_park, "_native") as inspect:
            with self.assertRaises(EngineBusy):
                training_park.restore_native(self.manager, {"model": PRIMARY})
            inspect.assert_not_called()

    def test_training_window_reserves_physical_members_before_trainer_launch(self):
        ctx = mock.Mock(engines=self.manager)
        ctx.state.get.return_value = {"phase": "held", "models": [
            {"id": PRIMARY, "settings": {"gpu": GPU_RTX}, "physical_devices": ["rtx5080"]}]}
        windows = resource_transitions.ResourceWindows(ctx)
        self.assertTrue(windows.reserved(INKLING_POOL))
        with mock.patch.object(self.manager, "_spec", return_value={"id": CPU, "gpu": "CPU"}):
            self.assertFalse(windows.reserved(CPU))

    def test_status_rejects_previous_launch_reused_pid_and_stale_observation(self):
        self.save_status()
        self.assertEqual(pooled_engine.snapshot(self.manager, self.unit, self.record)["phase"], "running")
        for changes in ({"launch_id": "2" * 32}, {"wrapper_start_ticks": "0"},
                        {"systemd_invocation": "b" * 32}, {"observed_at": time.time() - 30},
                        {"observed_at": float("nan")}, {"wrapper_pid": os.getpid() + 100000}):
            self.save_status(**changes)
            with self.subTest(changes=changes):
                self.assertEqual(pooled_engine.snapshot(self.manager, self.unit, self.record), {})

    def test_http_ready_does_not_admit_a_single_gpu_or_stale_pool(self):
        spec = self.manager._spec(INKLING_POOL)
        for dual, phase, expect in ((False, "running", "loading"), (True, "loading", "loading"),
                                    (True, "running", "loaded")):
            self.save_status(dual_allocations_observed=dual, phase=phase)
            with mock.patch.object(engines, "_unit", return_value=self.unit), \
                 mock.patch.object(self.manager, "_connection", return_value=(12345, "key", "dulche-pool", os.getpid())), \
                 mock.patch.object(engines, "_payload", return_value={"status": "ok"}):
                actual = self.manager._observe(spec)[0]
            self.assertEqual(actual["state"], expect)
            self.assertEqual(actual["gpu"], GPU_POOL)
            self.assertEqual(actual["physical_devices"], ["rtx5080", "v100"])
        self.assertEqual(actual["placement"]["kind"], "pooled_gpu_layers")
        self.assertFalse(actual["placement"]["hard_reservation_supported"])

    def test_release_requires_both_verified_cleanup_and_retired_fences(self):
        stopped = {"MainPID": "0", "ActiveState": "inactive"}
        command = mock.Mock()
        self.save_status(phase="stop_unverified", stop_verified=False)
        with self.assertRaisesRegex(EngineError, "release is unverified"):
            pooled_engine.release(self.manager, {}, stopped, self.record, command, lambda _: stopped, lambda _: set())
        self.save_status(phase="stopped", stop_verified=True)
        marker = self.runtime / (pooled_runtime.LOCK_NAMES["v100"] + ".unverified.json")
        marker.write_text("untouched quarantine")
        with self.assertRaisesRegex(EngineError, "quarantined"):
            pooled_engine.release(self.manager, {}, stopped, self.record, command, lambda _: stopped, lambda _: set())
        self.assertEqual(marker.read_text(), "untouched quarantine")
        marker.unlink()
        result = pooled_engine.release(self.manager, {}, stopped, self.record, command, lambda _: stopped, lambda _: set())
        self.assertTrue(result["release_verified"])
        self.assertEqual(result["released_vram_mib"], 0)
        command.assert_not_called()

    def test_pressure_matches_both_members_without_treating_pool_as_one_card(self):
        row = {"gpu": GPU_POOL, "physical_devices": ["rtx5080", "v100"]}
        self.assertTrue(controller.model_uses_gpu(row, {"name": "NVIDIA GeForce RTX 5080"}))
        self.assertTrue(controller.model_uses_gpu(row, {"name": "Tesla V100-PCIE-16GB"}))
        self.assertFalse(controller.model_uses_gpu(row, {"name": "RTX 3090"}))

    def test_guard_calls_start_both_sides_even_if_host_stop_fails(self):
        guest_ran = threading.Event()
        def fail():
            raise RuntimeError("host unit not stoppable")
        with self.assertRaisesRegex(EngineError, "host"):
            pooled_guard.parallel({"host": fail, "guest": guest_ran.set}, timeout=1)
        self.assertTrue(guest_ran.is_set())

    def test_new_rpc_failure_cannot_suppress_legacy_vm_escalation(self):
        for action in ("stop", "kill", "verify"):
            with self.subTest(action=action), \
                 mock.patch.object(pooled_guard, "paired_guest_action", side_effect=EngineError("new RPC is unverified")), \
                 mock.patch.object(pooled_guard, "legacy_guest_action") as legacy:
                with self.assertRaisesRegex(EngineError, "new RPC is unverified"):
                    pooled_guard.guest_action(action, self.manager)
                legacy.assert_called_once_with(action)

    def test_thermal_new_rpc_stop_and_verify_use_saved_pin_without_erasing_fences(self):
        pin = pooled_runtime.RpcProcess("11111111-1111-1111-1111-111111111111", 2222, "100", "/frozen/ggml-rpc-server",
                                       "a" * 64, "b" * 64, "GPU-22222222-2222-2222-2222-222222222222")
        markers = []
        for name in pooled_runtime.LOCK_NAMES.values():
            marker = self.runtime / (name + ".unverified.json")
            atomic_json(marker, {"schema": "dulche-pool-unverified-release/v1", "guest_rpc": asdict(pin)})
            markers.append((marker, marker.read_bytes()))
        with mock.patch.object(pooled_runtime, "stop_pooled_rpc", return_value=True) as stop:
            pooled_guard.paired_guest_action("stop", self.manager)
            self.assertEqual(stop.call_args.args[0], pin)
            self.assertNotIn("recipe", stop.call_args.kwargs)
        with mock.patch.object(pooled_runtime, "GuestRPC") as guest, \
             mock.patch.object(pooled_runtime, "stop_pooled_rpc") as stop:
            guest.return_value.stopped.return_value = False
            with self.assertRaisesRegex(EngineError, "allocations remain"):
                pooled_guard.paired_guest_action("verify", self.manager)
            stop.assert_not_called()
        for marker, raw in markers:
            self.assertEqual(marker.read_bytes(), raw)

    def test_thermal_pending_rpc_recovers_tag_before_stop_and_preserves_startup_fences(self):
        pin = pooled_runtime.RpcProcess("11111111-1111-1111-1111-111111111111", 2222, "100", "/frozen/ggml-rpc-server",
                                       "a" * 64, "b" * 64, "GPU-22222222-2222-2222-2222-222222222222")
        saved = {"schema": pooled_runtime.RPC_START_SCHEMA, "launch_id": "3" * 32,
                 "rpc_path": pin.executable, "rpc_sha256": pin.executable_sha256,
                 "rpc_library_dirs": ["/frozen"], "v100_uuid": pin.gpu_uuid}
        markers = []
        for name in pooled_runtime.LOCK_NAMES.values():
            marker = self.runtime / (name + ".unverified.json")
            atomic_json(marker, saved)
            markers.append((marker, marker.read_bytes()))
        with mock.patch.object(pooled_runtime, "recover_pooled_rpc", return_value=pin) as recover, \
             mock.patch.object(pooled_runtime, "stop_pooled_rpc", return_value=True) as stop:
            pooled_guard.paired_guest_action("stop", self.manager)
            recover.assert_called_once()
            self.assertEqual(recover.call_args.args[0]["launch_id"], saved["launch_id"])
            self.assertEqual(stop.call_args.args[0], pin)
        for marker, raw in markers:
            self.assertEqual(marker.read_bytes(), raw)

    def test_failed_pool_lookup_never_suppresses_existing_native_stop(self):
        for action in ("stop", "kill"):
            with self.subTest(action=action), \
                 mock.patch.object(pooled_guard, "pool_may_own_guest", side_effect=EngineError("pool lookup failed")), \
                 mock.patch.object(native_guard, "control_host", return_value=["native.service"]) as host:
                with self.assertRaisesRegex(EngineError, "pool lookup failed"):
                    native_guard.control(action, self.manager)
                host.assert_called_once_with(action, self.manager)

    def test_startup_deadline_retires_the_owned_pool_before_returning_error(self):
        current = {"id": INKLING_POOL, "state": "loading", "settings": {}}
        with mock.patch.object(self.manager, "_observe", return_value=(current, None, self.unit)), \
             mock.patch.object(engines.time, "monotonic", side_effect=[0, 1000]), \
             mock.patch.object(pooled_engine, "release") as release:
            with self.assertRaisesRegex(EngineError, "startup deadline"):
                self.manager.load(INKLING_POOL)
            release.assert_called_once()

    def test_guard_cannot_verify_only_native_when_pool_identity_is_unproven(self):
        def found(_manager, mid):
            if mid == INKLING_POOL:
                raise EngineError("pool identity differs")
            return None
        with mock.patch.object(native_guard, "owned", side_effect=found), self.assertRaises(EngineError):
            native_guard.control_host("verify", self.manager)

    def test_both_guard_device_commands_must_point_to_this_candidate(self):
        cfg = {"devices": []}
        for role, name in (("rtx5080", "native_guard.py"), ("v100", "pooled_guard.py")):
            row = {"id": role}
            for field, action in (("stop_cmd", "stop"), ("kill_cmd", "kill"), ("verify_cmd", "verify")):
                row[field] = ["/usr/bin/python3", str(Path(pooled_engine.__file__).with_name(name).resolve()), action]
            cfg["devices"].append(row)
        pin = mock.Mock(config_path="/fixture")
        with mock.patch.object(pooled_runtime, "capture_guard", return_value=pin), \
             mock.patch.object(pooled_runtime, "guard_policies"), \
             mock.patch.object(pooled_runtime, "_owned_json", return_value=cfg):
            self.assertIs(pooled_engine.guard_coverage("/fixture"), pin)
            cfg["devices"][1]["kill_cmd"] = ["/usr/bin/true"]
            with self.assertRaises(EngineBusy):
                pooled_engine.guard_coverage("/fixture")

    def test_rpc_is_restored_on_each_launch_and_owned_before_host_start(self):
        script = self.root / "pooled_runtime.py"
        script.write_text("# fixture wrapper is never executed\n")
        state = {"host": False, "rpc": False, "starts": 0}
        def unit(name):
            active = name == "astra-v100-vm.service" or (name == self.manager._name(INKLING_POOL) and state["host"])
            return {"ActiveState": "active" if active else "inactive", "MainPID": "1234" if active else "0"}
        def start(recipe):
            self.assertFalse(state["rpc"], "The previous RPC must have stopped before reload")
            state.update(rpc=True, starts=state["starts"] + 1)
            return pooled_runtime.RpcProcess("1" * 8 + "-" + "1" * 4 + "-" + "1" * 4 + "-" + "1" * 4 + "-" + "1" * 12,
                                             2000 + state["starts"], "100", "/frozen/ggml-rpc-server",
                                             "a" * 64, "b" * 64, "GPU-22222222-2222-2222-2222-222222222222")
        def command(args, **kwargs):
            if args[0] == "systemd-run":
                rec = pooled_engine.private_json(self.manager._record(INKLING_POOL))
                self.assertEqual(rec["rpc_process"]["pid"], 2000 + state["starts"])
                self.assertTrue(state["rpc"])
                self.assertTrue(rec["rpc_pending"])
                state["host"] = True
            elif "stop" in args:
                rec = pooled_engine.private_json(self.manager._record(INKLING_POOL))
                state.update(host=False, rpc=False)
                atomic_json(pooled_engine.status_path(self.manager._record(INKLING_POOL)),
                            {"schema": "dulche-pool-status/v1", "launch_id": rec["launch_id"],
                             "phase": "stopped", "stop_verified": True})
            return SimpleNamespace(stdout="", returncode=0)
        with mock.patch.object(pooled_engine, "__file__", str(self.root / "pooled_engine.py")), \
             mock.patch.object(pooled_engine, "guard_coverage"), \
             mock.patch.object(pooled_runtime, "nvml_snapshot", return_value={"allocations": []}), \
             mock.patch.object(pooled_runtime, "capture_display", return_value=None), \
             mock.patch.object(pooled_runtime, "runtime_recipe"), \
             mock.patch.object(pooled_runtime, "start_pooled_rpc", side_effect=start), \
             mock.patch.object(self.manager, "_owns", return_value=True):
            for _ in range(2):
                with self.manager._change(physical=True):
                    pooled_engine.launch(self.manager, self.manager._spec(INKLING_POOL), 32768, 4, "experimental",
                                         ["systemd-run"], command, unit)
                    rec = pooled_engine.private_json(self.manager._record(INKLING_POOL))
                    result = pooled_engine.release(self.manager, {"state": "loaded"}, unit(self.manager._name(INKLING_POOL)),
                                                   rec, command, unit,
                                                   lambda value: {1234} if value["MainPID"] != "0" else set())
                    self.assertTrue(result["release_verified"])
                    self.assertFalse(state["rpc"])
        self.assertEqual(state["starts"], 2)

    def test_host_unit_start_failure_reconciles_its_tagged_rpc(self):
        (self.root / "pooled_runtime.py").write_text("# never executed\n")
        pin = pooled_runtime.RpcProcess("11111111-1111-1111-1111-111111111111", 2222, "100", "/frozen/ggml-rpc-server",
                                       "a" * 64, "b" * 64, "GPU-22222222-2222-2222-2222-222222222222")
        def command(args, **kwargs):
            if args[0] == "systemd-run":
                raise EngineError("unit creation failed")
            return SimpleNamespace(stdout="", returncode=0)
        def unit(name):
            return {"ActiveState": "active" if name == "astra-v100-vm.service" else "inactive", "MainPID": "0"}
        with mock.patch.object(pooled_engine, "__file__", str(self.root / "pooled_engine.py")), \
             mock.patch.object(pooled_engine, "guard_coverage"), \
             mock.patch.object(pooled_runtime, "nvml_snapshot", return_value={"allocations": []}), \
             mock.patch.object(pooled_runtime, "capture_display", return_value=None), \
             mock.patch.object(pooled_runtime, "runtime_recipe"), \
             mock.patch.object(pooled_runtime, "start_pooled_rpc", return_value=pin), \
             mock.patch.object(pooled_runtime, "cleanup_pending_rpc", return_value=True) as cleanup:
            with self.manager._change(physical=True), self.assertRaisesRegex(EngineError, "unit creation failed"):
                pooled_engine.launch(self.manager, self.manager._spec(INKLING_POOL), 32768, 4, "experimental",
                                     ["systemd-run"], command, unit)
            cleanup.assert_called_once()
            self.assertEqual(cleanup.call_args.args[0]["rpc_process"]["pid"], pin.pid)
            self.assertFalse(pooled_engine.private_json(self.manager._record(INKLING_POOL))["rpc_pending"])


if __name__ == "__main__":
    unittest.main()
