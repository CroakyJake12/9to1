"""Durable RPC startup/transfer and executed guest-script fixtures; no hardware."""
from dataclasses import asdict, replace
import contextlib
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

import pooled_runtime as runtime
import test_pooled_runtime as fixtures


class StartupTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.recipe = {
            "alias": runtime.POOL_MODEL_ID, "physical_devices": list(runtime.DEVICE_ROLES),
            "launch_id": "a" * 32, "rpc_path": "/home/v100/strata/bridge/runtime-fixture/bin/ggml-rpc-server",
            "rpc_sha256": "b" * 64, "rpc_library_dirs": ["/home/v100/strata/bridge/runtime-fixture/bin", "/home/v100/strata/lib"],
            "v100_uuid": fixtures.V100,
        }
        self.pin = runtime.RpcProcess("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", 123, "456",
                                      self.recipe["rpc_path"], "b" * 64, "c" * 64, fixtures.V100)

    def test_startup_claims_both_before_transport_and_stops_exact_pin(self):
        def request(recipe, action, **kwargs):
            self.assertEqual(action, "start")
            for role in runtime.DEVICE_ROLES:
                value = runtime.read_private_json(runtime._quarantine_path(self.root, role))
                self.assertEqual(value["schema"], runtime.RPC_START_SCHEMA)
                self.assertEqual(value["launch_id"], self.recipe["launch_id"])
                with self.assertRaisesRegex(runtime.RuntimeError_, "unverified prior"):
                    runtime.assert_devices_available((role,), runtime_dir=self.root)
            return {"pin": asdict(self.pin)}
        guest = Mock()
        guest.stopped.return_value = True
        with patch.object(runtime, "_rpc_launch_request", side_effect=request), patch.object(runtime, "GuestRPC", return_value=guest):
            self.assertEqual(runtime.start_pooled_rpc(self.recipe, runtime_dir=self.root), self.pin)
            guest.observe.assert_called_once()
            self.assertTrue(runtime.stop_pooled_rpc(self.pin, recipe=self.recipe, runtime_dir=self.root))
            guest.stop.assert_called_once()
        runtime.assert_devices_available(runtime.DEVICE_ROLES, runtime_dir=self.root)

    def test_start_timeout_reconciles_lost_reply_or_retains_both_fences(self):
        for recover in ({"pin": asdict(self.pin)}, RuntimeError("guest unreachable")):
            directory = self.root / ("released" if isinstance(recover, dict) else "unverified")
            directory.mkdir(mode=0o700)
            guest = Mock()
            guest.stopped.return_value = True
            with (self.subTest(recover=recover),
                  patch.object(runtime, "_rpc_launch_request", side_effect=[TimeoutError("lost start reply"), recover]),
                  patch.object(runtime, "GuestRPC", return_value=guest)):
                with self.assertRaisesRegex(TimeoutError, "lost start"):
                    runtime.start_pooled_rpc(self.recipe, runtime_dir=directory)
            if isinstance(recover, dict):
                runtime.assert_devices_available(runtime.DEVICE_ROLES, runtime_dir=directory)
                guest.stop.assert_called_once()
            else:
                for role in runtime.DEVICE_ROLES:
                    with self.assertRaisesRegex(runtime.RuntimeError_, "unverified prior"):
                        runtime.assert_devices_available((role,), runtime_dir=directory)

    def test_only_same_launch_adopts_and_rpc_cleanup_cannot_erase_inference_fences(self):
        runtime._arm_rpc_startup(self.recipe, self.root)
        with self.assertRaisesRegex(runtime.RuntimeError_, "unverified prior"):
            with runtime.claim_devices(runtime_dir=self.root, startup_id="f" * 32):
                self.fail("Foreign launch may not adopt the startup")
        plan = SimpleNamespace(argv=("fixture",), executable_sha256="d" * 64)
        with runtime.claim_devices(runtime_dir=self.root, startup_id=self.recipe["launch_id"]):
            armed = runtime._arm_residency(self.root, guest=SimpleNamespace(pin=self.pin), plan=plan,
                                          startup_id=self.recipe["launch_id"])
        for role in runtime.DEVICE_ROLES:
            value = runtime.read_private_json(runtime._quarantine_path(self.root, role))
            self.assertEqual(value["schema"], "dulche-pool-unverified-release/v1")
        with patch.object(runtime, "_rpc_launch_request") as transport:
            with self.assertRaisesRegex(runtime.RuntimeError_, "transferred"):
                runtime.cleanup_pending_rpc(self.recipe, runtime_dir=self.root)
            transport.assert_not_called()
        with patch.object(runtime, "_rpc_launch_request", return_value={"pin": asdict(self.pin)}):
            self.assertEqual(runtime.recover_pooled_rpc(self.recipe), self.pin)
        guest = Mock()
        guest.stopped.return_value = True
        with patch.object(runtime, "GuestRPC", return_value=guest):
            self.assertTrue(runtime.stop_pooled_rpc(self.pin))
        for path, expected in armed.items():
            self.assertEqual(runtime.digest(path), expected)
        runtime._retire_residency(armed)

    def test_backend_popen_failure_cleans_new_rpc_before_retiring_residency(self):
        case = fixtures.LifecycleTests("runTest")
        case.setUp()
        self.addCleanup(case.doCleanups)
        case.pin = self.pin
        case.guest.pin = self.pin
        case.plan = replace(case.plan, guest_identity=replace(case.plan.guest_identity, owned_rpc_server_identity=self.pin.key))
        runtime._arm_rpc_startup(self.recipe, case.temp.name)
        case.launch = Mock(side_effect=OSError("fixture Popen refused"))
        with self.assertRaisesRegex(OSError, "Popen"):
            case.run_worker(launch_id=self.recipe["launch_id"], startup_id=self.recipe["launch_id"])
        case.guest.stop.assert_called_once()
        runtime.assert_devices_available(runtime.DEVICE_ROLES, runtime_dir=case.temp.name)

    def test_guest_start_script_persists_intent_before_popen_and_recovers_exact_child(self):
        import pathlib
        home = self.root / "home"
        base = home / "strata" / "bridge"
        base.mkdir(parents=True, mode=0o700)
        exe = base / "runtime" / "bin" / "ggml-rpc-server"
        exe.parent.mkdir(parents=True)
        exe.write_bytes(b"harmless fixture, Popen is mocked")
        recipe = {**self.recipe, "rpc_path": str(exe), "rpc_sha256": runtime.digest(exe),
                  "rpc_library_dirs": [str(exe.parent)]}
        procroot = self.root / "proc"
        (procroot / "net").mkdir(parents=True)
        (procroot / "sys/kernel/random").mkdir(parents=True)
        (procroot / "sys/kernel/random/boot_id").write_text(self.pin.boot_id)
        (procroot / "net/tcp").write_text("sl local_address remote_address st\n")
        # A prior verified stop can leave a historical receipt whose PID has
        # since been reused. The different start ticks prove it is not alive.
        (procroot / "111").mkdir()
        (procroot / "111/stat").write_text("111 (other) " + " ".join(["S"] + ["0"] * 18 + ["999"] + ["0"] * 3))
        old = {"schema": "dulche-pool-guest-rpc/v1", "launch_id": "f" * 32,
               "pin": {**asdict(self.pin), "pid": 111, "start_ticks": "998"}}
        (base / "pooled-rpc-owner.json").write_text(json.dumps(old))
        (base / "pooled-rpc-owner.json").chmod(0o600)
        allocations = []
        child = Mock(pid=321)
        child.poll.return_value = None
        def launch(argv, **kwargs):
            intent = json.loads((base / "pooled-rpc-owner.json").read_text())
            self.assertEqual(intent["phase"], "starting")
            self.assertEqual(intent["launch_id"], recipe["launch_id"])
            self.assertEqual(argv, [str(exe), "--host", "0.0.0.0", "--port", "5555", "--device", "CUDA0"])
            self.assertNotIn("GGML_BACKEND_PATH", kwargs["env"])
            self.assertEqual(kwargs["env"]["CUDA_VISIBLE_DEVICES"], fixtures.V100)
            proc = procroot / "321"
            (proc / "fd").mkdir(parents=True)
            (proc / "exe").symlink_to(exe)
            (proc / "cmdline").write_bytes(b"\0".join(x.encode() for x in argv) + b"\0")
            (proc / "environ").write_bytes(("DULCHE_POOL_LAUNCH_ID=" + recipe["launch_id"]).encode() + b"\0")
            (proc / "stat").write_text("321 (rpc) " + " ".join(["S"] + ["0"] * 18 + ["789"] + ["0"] * 3))
            (proc / "fd/3").symlink_to("socket:[42]")
            (procroot / "net/tcp").write_text("sl local_address remote_address st\n0: 00000000:15B3 00000000:0000 0A 0 0 0 0 0 42\n")
            allocations.append({"pid": 321, "gpu_uuid": fixtures.V100, "bytes": 1234567})
            return child
        realpath = Path
        def paths(value):
            text = str(value)
            return procroot / text.removeprefix("/proc/") if text.startswith("/proc/") else procroot if text == "/proc" else realpath(value)
        def nvml(_):
            return {"name": "Tesla V100-PCIE-16GB", "gpu_uuid": fixtures.V100, "allocations": allocations}
        returned = []
        for action in ("start", "recover"):
            output = io.StringIO()
            source = runtime._RPC_LAUNCH_SCRIPT.replace("REQUEST_LITERAL", repr(json.dumps({"recipe": runtime._rpc_recipe(recipe), "action": action})))
            space = {"nvml_snapshot": nvml}
            with (patch.object(pathlib, "Path", side_effect=paths) as pathmock,
                  patch.object(subprocess, "Popen", side_effect=launch) as popen,
                  patch.dict(os.environ, {"GGML_BACKEND_PATH": "/legacy-directory"}),
                  contextlib.redirect_stdout(output), self.assertRaises(SystemExit)):
                pathmock.home.return_value = home
                exec(compile(source, "<guest-rpc-start-fixture>", "exec"), space)
            os.close(space["lock"])  # Each real SSH Python process closes this on exit.
            returned.append(json.loads(output.getvalue())["pin"])
            self.assertEqual(popen.call_count, 1 if action == "start" else 0)
        self.assertEqual(returned[0], returned[1])
        self.assertEqual(returned[0]["pid"], 321)
        self.assertEqual(returned[0]["executable_sha256"], recipe["rpc_sha256"])


if __name__ == "__main__":
    unittest.main()
