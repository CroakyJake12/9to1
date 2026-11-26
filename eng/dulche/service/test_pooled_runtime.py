"""Local physical-lock and harmless-child integration tests; no GPU/SSH actions."""
import copy
import contextlib
from dataclasses import replace
import json
import io
import os
from pathlib import Path
from types import SimpleNamespace
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from unittest.mock import Mock, patch

from pooled_backend import CommandPlan, GuestIdentity, RPC_ENDPOINT, RTX_UUID, digest
from pooled_budget import PoolPolicy
from pooled_health import DevicePolicy
import pooled_runtime as runtime

V100 = "GPU-11111111-2222-3333-4444-555555555555"
MIB = 1 << 20


def snapshot():
    now = time.time()
    return {
        "ts": now, "halted": False,
        "devices": {
            "rtx5080": {"telemetry_ok": True, "stopped": False, "last_telemetry_ts": now,
                        "reading_full": {"name": "RTX 5080", "driver_ok": True, "temp": 42,
                                         "mem_used": 100 * MIB, "mem_total": 16000 * MIB}},
            "v100": {"telemetry_ok": True, "stopped": False, "last_telemetry_ts": now,
                     "reading_full": {"ts": now, "name": "Tesla V100-PCIE-16GB", "driver_ok": True,
                                      "temp": 40, "temp_mem": 42, "power_limit_active": 165,
                                      "mem_used": 50 * MIB, "mem_total": 16000 * MIB}},
        },
    }


POLICIES = (
    DevicePolicy("rtx5080", (("name", "RTX 5080"),), 80, False),
    DevicePolicy("v100", (("name", "Tesla V100-PCIE-16GB"),), 68, False, 67, 165),
)


class LeaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def test_claims_block_other_pool_and_each_single_gpu_launcher(self):
        with runtime.claim_devices(runtime_dir=self.root):
            for roles in (runtime.DEVICE_ROLES, ("rtx5080",), ("v100",)):
                with self.subTest(roles=roles), self.assertRaises(runtime.RuntimeError_):
                    runtime.assert_devices_available(roles, runtime_dir=self.root)
        runtime.assert_devices_available(runtime.DEVICE_ROLES, runtime_dir=self.root)

    def test_second_gpu_failure_releases_first_claim_without_unlinking_inode(self):
        with runtime.claim_devices(("v100",), runtime_dir=self.root):
            with self.assertRaises(runtime.RuntimeError_):
                with runtime.claim_devices(runtime_dir=self.root):
                    self.fail("Both locks must be acquired")
            runtime.assert_devices_available(("rtx5080",), runtime_dir=self.root)
        path = self.root / runtime.LOCK_NAMES["rtx5080"]
        inode = path.stat().st_ino
        runtime.assert_devices_available(runtime.DEVICE_ROLES, runtime_dir=self.root)
        self.assertEqual(inode, path.stat().st_ino)

    def test_claim_refuses_a_replaced_lock_symlink(self):
        target = self.root / "other"
        target.touch()
        (self.root / runtime.LOCK_NAMES["rtx5080"]).symlink_to(target)
        with self.assertRaises(OSError):
            with runtime.claim_devices(runtime_dir=self.root):
                self.fail("Symlink lock must be refused")

    def test_private_recipe_refuses_symlink_and_shared_permissions(self):
        path = self.root / "recipe.json"
        path.write_text('{"schema":"fixture"}')
        path.chmod(0o600)
        self.assertEqual(runtime.read_private_json(path), {"schema": "fixture"})
        link = self.root / "link.json"
        link.symlink_to(path)
        with self.assertRaises(OSError):
            runtime.read_private_json(link)
        path.chmod(0o644)
        with self.assertRaises(runtime.RuntimeError_):
            runtime.read_private_json(path)


class BudgetTests(unittest.TestCase):
    def setUp(self):
        self.policy = PoolPolicy("CUDA:" + RTX_UUID, V100, RPC_ENDPOINT, 5_000_000_000)

    def test_display_is_counted_once_and_exact_bytes_are_retained(self):
        budget = runtime.health_budget(snapshot(), POLICIES, self.policy, {"rtx5080": 0, "v100": 0})
        rtx = budget.devices[0]
        self.assertEqual(rtx.display_reserved_bytes, 1_400_000_000)
        self.assertEqual(rtx.unconsumed_reserve_bytes, 1_400_000_000 - 100 * MIB)
        self.assertEqual(rtx.owned_limit_bytes, 16000 * MIB - 1_400_000_000)

    def test_fresh_file_cannot_conceal_stale_v100_sample(self):
        value = snapshot()
        value["devices"]["v100"]["last_telemetry_ts"] -= 10
        with self.assertRaisesRegex(runtime.RuntimeError_, "v100.stale"):
            runtime.health_budget(value, POLICIES, self.policy, {"rtx5080": 0, "v100": 0})

    def test_reserve_breach_on_rtx_is_not_covered_by_free_v100(self):
        value = snapshot()
        owned = 16000 * MIB - 1_400_000_000 + 1
        value["devices"]["rtx5080"]["reading_full"]["mem_used"] = owned
        with self.assertRaisesRegex(runtime.RuntimeError_, "display-reserve"):
            runtime.health_budget(value, POLICIES, self.policy, {"rtx5080": owned, "v100": 0})

    def test_competing_gpu_allocation_is_refused_not_attributed_to_pool(self):
        row = {"pid": 456, "gpu_uuid": RTX_UUID, "mib": 10}
        with self.assertRaisesRegex(runtime.RuntimeError_, "competing"):
            runtime.allocation_bound([row], {123}, uuid=RTX_UUID)
        self.assertEqual(runtime.allocation_bound([row], {456}, uuid=RTX_UUID), 11 * MIB)
        self.assertEqual(runtime.allocation_bound([{"pid": 456, "gpu_uuid": RTX_UUID, "bytes": 1234567}],
                                                 {456}, uuid=RTX_UUID), 1234567)


class GuestPinTests(unittest.TestCase):
    def setUp(self):
        self.pin = runtime.RpcProcess("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", 123, "456",
                                      "/home/v100/bin/rpc-server", "a" * 64, "b" * 64, V100)

    def test_actions_use_fixed_python_stdin_transport_with_pinned_payload(self):
        ssh = Mock(return_value=type("Result", (), {"stdout": '{"alive":false,"stopped":true}'})())
        guest = runtime.GuestRPC(self.pin, ssh=ssh)
        guest.stop(5)
        self.assertEqual(ssh.call_args.args, ("python3 -",))
        source = ssh.call_args.kwargs["input"]
        compile(source, "<pinned-guest>", "exec")
        self.assertIn("pidfd_open", source)
        self.assertIn("pidfd_send_signal", source)
        self.assertNotIn("os.kill(", source)

    def test_replacement_or_dead_rpc_is_never_accepted_as_live(self):
        for reply in ({"alive": False, "stopped": True}, {"alive": True, "gpu_uuid": RTX_UUID}):
            with self.subTest(reply=reply):
                ssh = Mock(return_value=type("Result", (), {"stdout": json.dumps(reply)})())
                with self.assertRaises(runtime.RuntimeError_):
                    runtime.GuestRPC(self.pin, ssh=ssh).observe(5)

    def test_guest_process_exit_requires_nvml_allocation_disappearance(self):
        # Exercise the actual remote script's process-absent branch locally;
        # only /proc's boot ID is read; pidfd and all nvidia calls are mocked.
        pin = replace(self.pin, boot_id=Path("/proc/sys/kernel/random/boot_id").read_text().strip())
        for residue, expected in (([{"pid": 123, "bytes": 50 * MIB}], False), ([], True)):
            source = runtime._GUEST_SCRIPT.replace("REQUEST_LITERAL", repr(json.dumps({"pin": pin.__dict__, "action": "observe"})))
            def nvml(_):
                return {"gpu_uuid": V100, "name": "Tesla V100-PCIE-16GB", "allocations": residue}
            captured = io.StringIO()
            with (self.subTest(residue=residue), patch("os.pidfd_open", side_effect=ProcessLookupError),
                  contextlib.redirect_stdout(captured),
                  self.assertRaises(SystemExit)):
                exec(compile(source, "<guest-release-test>", "exec"), {"nvml_snapshot": nvml})
            self.assertEqual(json.loads(captured.getvalue())["stopped"], expected)


class ConcreteHookTests(unittest.TestCase):
    def test_nvml_abi_returns_exact_bytes_and_handles_the_process_count_protocol(self):
        class Fn:
            def __init__(self, fn): self.fn = fn
            def __call__(self, *args): return self.fn(*args)
        def handle(uuid, pointer):
            pointer._obj.value = 1
            return 0
        def text(value):
            def write(handle, output, size):
                output.value = value
                return 0
            return write
        def processes(handle, count, data):
            if data is None:
                count._obj.value = 1
                return 7
            data[0].pid = 543
            data[0].usedGpuMemory = 1234567
            count._obj.value = 1
            return 0
        def memory(handle, pointer):
            pointer._obj.total = 16000 * MIB
            pointer._obj.used = 1234567 + 100 * MIB
            pointer._obj.free = pointer._obj.total - pointer._obj.used
            return 0
        lib = SimpleNamespace(nvmlInit_v2=Fn(lambda: 0), nvmlShutdown=Fn(lambda: 0),
                              nvmlDeviceGetHandleByUUID=Fn(handle),
                              nvmlDeviceGetUUID=Fn(text(RTX_UUID.encode())),
                              nvmlDeviceGetName=Fn(text(b"NVIDIA GeForce RTX 5080")),
                              nvmlDeviceGetComputeRunningProcesses_v3=Fn(processes),
                              nvmlDeviceGetMemoryInfo=Fn(memory))
        space = {}
        exec(runtime._NVML_SOURCE, space)
        with patch("ctypes.CDLL", return_value=lib):
            result = space["nvml_snapshot"](RTX_UUID)
        self.assertEqual(result["allocations"], [{"pid": 543, "gpu_uuid": RTX_UUID, "bytes": 1234567}])
        self.assertEqual(result["memory"]["mem_used"], 1234567 + 100 * MIB)

    def test_real_guard_identity_accepts_B_and_rejects_replacement_invocation(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            proc = root / "proc" / "543"
            proc.mkdir(parents=True)
            cfg = root / "guard.json"
            cfg.write_text(json.dumps({"status_file": str(root / "status.json")}))
            cfg.chmod(0o600)
            args = ["/usr/bin/python3", "-B", "-m", "serve.thermal_guard", "--config", str(cfg)]
            (proc / "cmdline").write_bytes(b"\0".join(x.encode() for x in args) + b"\0")
            (proc / "stat").write_text("543 (python) " + " ".join(["S"] + ["0"] * 18 + ["4242"] + ["0"] * 3))
            (proc / "exe").symlink_to(Path("/usr/bin/python3").resolve())
            (proc / "cgroup").write_text("0::/user.slice/dulche-v100-thermal.service\n")
            unit = {"MainPID": "543", "InvocationID": "a" * 32, "ActiveState": "active",
                    "ControlGroup": "/user.slice/dulche-v100-thermal.service"}
            real_path = Path
            def paths(value):
                return root / "proc" if str(value) == "/proc" else real_path(value)
            with patch.object(runtime, "_guard_unit", return_value=unit), patch.object(runtime, "Path", side_effect=paths):
                pin = runtime.capture_guard(cfg)
                self.assertEqual(pin.start_ticks, "4242")
                unit["InvocationID"] = "b" * 32
                with self.assertRaisesRegex(runtime.RuntimeError_, "invocation"):
                    runtime.guard_snapshot(pin, 1)
                unit["InvocationID"] = "a" * 32
                (proc / "cgroup").write_text("0::/unrelated.service\n")
                with self.assertRaisesRegex(runtime.RuntimeError_, "cgroup"):
                    runtime.capture_guard(cfg)
                (proc / "cgroup").write_text("0::/user.slice/dulche-v100-thermal.service\n")
                (proc / "exe").unlink()
                (proc / "exe").symlink_to("/usr/bin/true")
                with self.assertRaisesRegex(runtime.RuntimeError_, "interpreter"):
                    runtime.capture_guard(cfg)

    def test_concrete_hooks_pair_host_allocations_and_memory_in_same_sample(self):
        nvml = {"gpu_uuid": RTX_UUID, "allocations": [{"pid": 543, "gpu_uuid": RTX_UUID, "bytes": 1234567}],
                "memory": {"mem_total": 16000 * MIB, "mem_used": 1234567 + 100 * MIB}}
        fake_profiles = SimpleNamespace(atomic_json=Mock())
        with (patch.dict(sys.modules, {"engine_profiles": fake_profiles}),
              patch.object(runtime, "guard_snapshot", side_effect=lambda *_: snapshot()),
              patch.object(runtime, "capture_display", return_value=None),
              patch.object(runtime, "nvml_snapshot", return_value=nvml) as read):
            hooks = runtime.system_hooks(Mock(), "/unused/status.json")
            value = hooks.read_snapshot(3)
            rows = hooks.host_allocations(3)
            read.assert_called_once()
            self.assertEqual(value["devices"]["rtx5080"]["reading_full"]["mem_used"], 1234567 + 100 * MIB)
            self.assertEqual(rows[0]["bytes"], 1234567)

    def test_original_v100_sample_freshness_cannot_be_hidden_by_fresh_guard_poll(self):
        for stamp in (None, True, float("nan"), float("inf"), time.time() - 6, time.time() + 60):
            value = snapshot()
            value["devices"]["v100"]["reading_full"]["ts"] = stamp
            with (self.subTest(stamp=stamp), patch.dict(sys.modules, {"engine_profiles": SimpleNamespace(atomic_json=Mock())}),
                  patch.object(runtime, "capture_display", return_value=None),
                  patch.object(runtime, "guard_snapshot", return_value=value),
                  patch.object(runtime, "nvml_snapshot") as nvml):
                hooks = runtime.system_hooks(Mock(), "/unused/status.json")
                with self.assertRaisesRegex(runtime.RuntimeError_, "original sensor timestamp"):
                    hooks.read_snapshot(3)
                nvml.assert_not_called()

    def test_host_ram_floor_preserves_8192_mib(self):
        with (tempfile.TemporaryDirectory() as temp,
              patch.dict(sys.modules, {"engine_profiles": SimpleNamespace(atomic_json=Mock())}),
              patch.object(runtime, "capture_display", return_value=None),
              patch.object(runtime, "guard_snapshot", side_effect=lambda *_: snapshot()),
              patch.object(runtime, "nvml_snapshot") as nvml):
            memfile = Path(temp) / "meminfo"
            memfile.write_text("MemAvailable: 8388607 kB\n")
            real_path = Path
            def paths(value):
                return memfile if str(value) == "/proc/meminfo" else real_path(value)
            with patch.object(runtime, "Path", side_effect=paths):
                hooks = runtime.system_hooks(Mock(), "/unused/status.json")
                with self.assertRaisesRegex(runtime.RuntimeError_, "RAM"):
                    hooks.read_snapshot(3)
            nvml.assert_not_called()
            with self.assertRaisesRegex(ValueError, "8192"):
                runtime.system_hooks(Mock(), "/unused/status.json", min_host_available_mib=8191)

    def test_display_exemption_pins_service_and_never_owns_desktop_bytes(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            proc = root / "proc" / "2698"
            proc.mkdir(parents=True)
            executable = root / "desktop-daemon"
            executable.write_text("fixture")
            (proc / "exe").symlink_to(executable)
            (proc / "cmdline").write_bytes(runtime.DISPLAY_EXECUTABLE.encode() + b"\0")
            (proc / "stat").write_text("2698 (daemon) " + " ".join(["S"] + ["0"] * 18 + ["4567"] + ["0"] * 3))
            group = "/user.slice/user-1000.slice/user@1000.service/app.slice/gnome-remote-desktop.service"
            (proc / "cgroup").write_text("0::" + group + "\n")
            unit = {"ActiveState": "active", "MainPID": "2698", "InvocationID": "a" * 32, "ControlGroup": group}
            real_path = Path
            def paths(value):
                if str(value) == "/proc": return root / "proc"
                if str(value) == runtime.DISPLAY_EXECUTABLE: return executable
                return real_path(value)
            rows = [{"pid": 2698, "gpu_uuid": RTX_UUID, "bytes": 264 * MIB},
                    {"pid": 987, "gpu_uuid": RTX_UUID, "bytes": 1234567}]
            with patch.object(runtime, "Path", side_effect=paths), patch.object(runtime, "_display_unit", return_value=unit):
                pin = runtime.capture_display()
                filtered = runtime.host_compute_allocations(rows, display_pin=pin)
                self.assertEqual(filtered, [rows[1]])
                self.assertEqual(runtime.allocation_bound(filtered, {987}, uuid=RTX_UUID), 1234567)
                self.assertEqual(runtime.host_compute_allocations(rows, display_pin=None), rows)
                unit["InvocationID"] = "b" * 32
                with self.assertRaisesRegex(runtime.RuntimeError_, "identity changed"):
                    runtime.host_compute_allocations(rows, display_pin=pin)
                unit["InvocationID"] = "a" * 32
                (proc / "cmdline").write_bytes(runtime.DISPLAY_EXECUTABLE.encode() + b"\0--other\0")
                with self.assertRaisesRegex(runtime.RuntimeError_, "command line"):
                    runtime.capture_display()


class LifecycleTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.pin = runtime.RpcProcess("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", 123, "456",
                                      "/home/v100/bin/rpc-server", "a" * 64, "b" * 64, V100)
        self.guest = Mock()
        self.guest.pin = self.pin
        self.guest.observe.return_value = {"allocations": []}
        self.guest.stopped.return_value = True
        exe = str(Path(sys.executable).resolve())
        target = 1_400_000_000 - 100 * MIB
        target_mib = (target + MIB - 1) // MIB
        self.plan = CommandPlan(
            (exe, "-c", "import time; time.sleep(30)", "--fit-target", str(target_mib) + ",0"), digest(Path(exe)), ("CUDA0", "RPC0"),
            (target, 0), (target_mib, 0), (target_mib * MIB - target, 0),
            GuestIdentity(RPC_ENDPOINT, "RPC0", V100, "Tesla V100-PCIE-16GB",
                          time.monotonic_ns(), self.pin.key), time.monotonic_ns(), time.monotonic_ns())
        self.cancel = threading.Event()
        self.published = []
        self.hooks = runtime.RuntimeHooks(lambda _: snapshot(), lambda _: [], self.published.append)
        self.children = []

    def launch(self, *args, **kwargs):
        process = subprocess.Popen(*args, **kwargs)
        self.children.append(process)
        self.addCleanup(lambda: process.kill() if process.poll() is None else None)
        return process

    def run_worker(self, **overrides):
        return runtime.run_owned(self.plan, guest=self.guest, policies=POLICIES,
                                 hooks=self.hooks, cancelled=self.cancel,
                                 runtime_dir=self.temp.name, popen=self.launch, **overrides)

    def test_lost_sensor_stops_real_harmless_child_and_pinned_remote(self):
        calls = 0
        def reading(_):
            nonlocal calls
            calls += 1
            value = snapshot()
            if calls > 1:
                value["devices"]["v100"]["last_telemetry_ts"] -= 10
            return value
        self.hooks = replace(self.hooks, read_snapshot=reading)
        result = self.run_worker(run_timeout=2)
        self.assertTrue(result.stop_verified)
        self.assertIsNotNone(self.children[0].poll())
        self.guest.stop.assert_called_once()
        self.assertEqual(self.published[-1]["status"], "stopped")

    def test_changed_display_budget_refreshes_only_fitting_margin_before_launch(self):
        original = self.plan
        self.plan = replace(original, argv=original.argv[:-1] + ("1336,0",),
                            exact_free_targets_bytes=(1_400_000_000, 0),
                            fitting_targets_mib=(1336, 0), fitting_rounding_bytes=(897536, 0))
        actual_launch = self.launch
        def checked_launch(args, **kwargs):
            self.assertEqual(tuple(args), original.argv)
            return actual_launch(args, **kwargs)
        self.launch = checked_launch
        timer = threading.Timer(0.1, self.cancel.set)
        timer.start()
        self.addCleanup(timer.cancel)
        self.assertTrue(self.run_worker(run_timeout=2).stop_verified)

    def test_prelaunch_cancel_and_invalid_duration_do_not_create_a_child(self):
        self.cancel.set()
        with self.assertRaisesRegex(runtime.RuntimeError_, "cancelled"):
            self.run_worker()
        with self.assertRaises(ValueError):
            self.run_worker(run_timeout=float("nan"))
        self.assertFalse(self.children)

    def test_cancellation_stops_both_and_idle_rpc_does_not_prove_dual_allocation(self):
        timer = threading.Timer(0.2, self.cancel.set)
        timer.start()
        self.addCleanup(timer.cancel)
        result = self.run_worker(run_timeout=3)
        self.assertEqual(result.trigger, "cancelled")
        self.assertTrue(result.stop_verified)
        self.guest.stop.assert_called_once()
        self.assertFalse(self.published[-1]["dual_allocations_observed"])
        self.assertEqual(self.published[-1]["schema"], "dulche-pool-status/v1")
        self.assertEqual(self.published[-1]["wrapper_pid"], os.getpid())
        self.assertTrue(self.published[-1]["wrapper_start_ticks"].isdigit())
        self.assertEqual(len(self.published[-1]["launch_id"]), 32)

    def test_unverified_remote_release_blocks_future_physical_admission(self):
        self.guest.stopped.return_value = False
        timer = threading.Timer(0.1, self.cancel.set)
        timer.start()
        self.addCleanup(timer.cancel)
        result = self.run_worker(run_timeout=2)
        self.assertFalse(result.stop_verified)
        self.assertIsNotNone(self.children[0].poll())
        for role in runtime.DEVICE_ROLES:
            with self.subTest(role=role), self.assertRaisesRegex(runtime.RuntimeError_, "unverified prior release"):
                runtime.assert_devices_available((role,), runtime_dir=self.temp.name)

    def test_residency_fences_exist_before_process_creation_and_retire_on_verified_stop(self):
        original = self.launch
        def checked_launch(*args, **kwargs):
            for role in runtime.DEVICE_ROLES:
                self.assertTrue(runtime._quarantine_path(Path(self.temp.name), role).is_file())
            return original(*args, **kwargs)
        self.launch = checked_launch
        timer = threading.Timer(0.1, self.cancel.set)
        timer.start()
        self.addCleanup(timer.cancel)
        self.assertTrue(self.run_worker(run_timeout=2).stop_verified)
        runtime.assert_devices_available(runtime.DEVICE_ROLES, runtime_dir=self.temp.name)

    def test_delayed_observation_never_republishes_running_after_terminal_state(self):
        calls = 0
        def slow(_):
            nonlocal calls
            calls += 1
            if calls > 1:
                time.sleep(0.2)
            return snapshot()
        self.hooks = replace(self.hooks, read_snapshot=slow)
        result = self.run_worker(run_timeout=0.03)
        self.assertTrue(result.stop_verified)
        self.assertEqual(self.published[-1]["status"], "stopped")
        count = len(self.published)
        time.sleep(0.3)
        self.assertEqual(len(self.published), count)


if __name__ == "__main__":
    unittest.main()
