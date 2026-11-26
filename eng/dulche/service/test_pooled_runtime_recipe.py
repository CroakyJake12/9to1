"""Bounded CLI recipes and optional DSpark planning; no GPU/SSH execution."""
import copy
from dataclasses import asdict, replace
import json
import os
from pathlib import Path
import struct
import tempfile
import threading
import time
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

import pooled_backend as backend
import pooled_runtime as runtime
from test_pooled_runtime import V100, POLICIES, snapshot


class RecipeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.exe = self.root / "llama-server"
        self.exe.write_bytes(b"fixture backend never executed")
        self.exe.chmod(0o700)
        self.model = self.root / "target.gguf"
        self.model.write_bytes(struct.pack("<4sIQQ", b"GGUF", 3, 1, 1) + b"\0" * 64)
        self.key = self.root / "record.key"
        self.key.write_text("fixture-secret\n")
        self.key.chmod(0o600)
        self.record = self.root / "record.json"
        self.recipe = {
            "schema": "dulche-pool-runtime/v1", "physical_devices": ["rtx5080", "v100"],
            "alias": runtime.POOL_MODEL_ID, "launch_id": "a" * 32,
            "context": 32768, "threads": 4, "port": 19142,
            "library_dirs": [str(self.root)], "backend_path": str(self.exe),
            "backend_sha256": backend.digest(self.exe), "split_weights": [3, 5],
            "rpc_sha256": "a" * 64,
            "rpc_path": "/home/v100/strata/bridge/runtime-fixture/bin/ggml-rpc-server",
            "rpc_library_dirs": ["/home/v100/strata/bridge/runtime-fixture/bin", "/home/v100/strata/lib"],
            "path": str(self.model), "guard_config": str(self.root / "guard.json"), "v100_uuid": V100,
            "model_identity": {"sha256": "e64276656f072e9b2197a61723667cbdb71da66a716ffd966e572c21088f3cd3", "bytes": 17629760736},
            "draft": {"enabled": False},
        }
        self.recipe["rpc_process"] = asdict(runtime.RpcProcess(
            "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", 123, "456", self.recipe["rpc_path"],
            "a" * 64, "b" * 64, V100))
        self.save()

    def save(self, recipe=None):
        self.record.write_text(json.dumps(self.recipe if recipe is None else recipe))
        self.record.chmod(0o600)

    def proof(self):
        return backend.BackendProof(str(self.exe), backend.digest(self.exe), "fixture",
                                    backend.REQUIRED_FLAGS | {"--tensor-split", "--spec-type", "--model-draft", "--spec-draft-n-max", "--spec-draft-n-ubatch"},
                                    (backend.BackendDevice("CUDA0", "NVIDIA GeForce RTX 5080", 16000 << 20, 15000 << 20),
                                     backend.BackendDevice("RPC0", backend.RPC_ENDPOINT, 16000 << 20, 15000 << 20)),
                                    time.monotonic_ns(), True, dspark_speculation=True)

    def plan(self, **overrides):
        pin = runtime.RpcProcess("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", 123, "456", "/home/v100/bin/rpc-server", "a" * 64, "b" * 64, V100)
        kwargs = dict(guest_identity=backend.GuestIdentity(backend.RPC_ENDPOINT, "RPC0", V100,
                                                          "Tesla V100-PCIE-16GB", time.monotonic_ns(), pin.key),
                      now_ns=time.monotonic_ns(), max_age_ns=5_000_000_000,
                      model=str(self.model), alias=runtime.POOL_MODEL_ID, key_file=str(self.key),
                      context=32768, port=19142, free_targets_bytes=(100000000, 0), split_weights=(3, 5))
        proof = self.proof()
        kwargs["now_ns"] = time.monotonic_ns()
        kwargs.update(overrides)
        return backend.command_plan(proof, **kwargs)

    def test_recipe_pins_exact_profile_model_and_two_devices(self):
        self.save()
        with patch.object(runtime, "_artifact", return_value=self.model) as artifact:
            self.assertEqual(runtime.runtime_recipe(self.record)["launch_id"], "a" * 32)
            artifact.assert_called_once_with(str(self.model), self.recipe["model_identity"])
            for change in ({"alias": "unrelated"}, {"physical_devices": ["rtx5080"]},
                           {"launch_id": ""}, {"split_weights": [1, 0]}, {"display_reserved_bytes": 1400000001},
                           {"model_identity": None}, {"model_identity": {"sha256": "f" * 64, "bytes": 17629760736}},
                           {"draft": {"enabled": "false"}}, {"draft": {"enabled": True, "sha256": "f" * 64}},
                           {"threads": True}, {"rpc_sha256": "unverified"}):
                with self.subTest(change=change):
                    altered = {**self.recipe, **change}
                    self.save(altered)
                    with self.assertRaises(runtime.RuntimeError_):
                        runtime.runtime_recipe(self.record)

    def test_model_hash_and_exact_stat_receipt(self):
        identity = {"sha256": backend.digest(self.model), "bytes": self.model.stat().st_size}
        self.assertEqual(runtime._artifact(self.model, identity), self.model)
        stat = self.model.stat()
        identity["stat"] = {"dev": stat.st_dev, "ino": stat.st_ino, "size": stat.st_size, "mtime_ns": stat.st_mtime_ns}
        with patch.object(runtime, "digest", side_effect=AssertionError("exact verified stat receipt should avoid rehash")):
            self.assertEqual(runtime._artifact(self.model, identity), self.model)
        self.model.write_bytes(self.model.read_bytes()[:-1] + b"x")
        with self.assertRaisesRegex(runtime.RuntimeError_, "SHA256"):
            runtime._artifact(self.model, identity)

    def test_optional_dspark_is_explicit_and_identity_checked(self):
        baseline = self.plan()
        self.assertIn("--jinja", baseline.argv)
        self.assertIn("--no-webui", baseline.argv)
        self.assertNotIn("--spec-type", baseline.argv)
        self.assertEqual(baseline.argv[baseline.argv.index("--tensor-split") + 1], "3,5")
        draft = backend.DraftConfig(str(self.model), backend.digest(self.model), self.model.stat().st_size, 3)
        enabled = self.plan(draft=draft)
        self.assertEqual(enabled.argv[-8:], ("--spec-type", "draft-dspark", "--model-draft", str(self.model),
                                            "--spec-draft-n-max", "3", "--spec-draft-n-ubatch", "128"))
        for bad in (replace(draft, sha256="f" * 64), replace(draft, expected_bytes=1), replace(draft, n_max=9)):
            with self.subTest(draft=bad), self.assertRaises(backend.BackendError):
                self.plan(draft=bad)
        with patch.object(self, "proof", return_value=replace(self.proof(), dspark_speculation=False)):
            with self.assertRaisesRegex(backend.BackendError, "DSpark"):
                self.plan(draft=draft)

    def test_split_weights_need_both_devices_and_declared_backend_support(self):
        for split in ((0, 1), (1, -1), (True, 1), (1, 10001), (1,), [1, 1]):
            with self.subTest(split=split), self.assertRaises(backend.BackendError):
                self.plan(split_weights=split)
        with patch.object(self, "proof", return_value=replace(self.proof(), flags=backend.REQUIRED_FLAGS)):
            with self.assertRaisesRegex(backend.BackendError, "split"):
                self.plan()

    def test_final_refresh_only_changes_fitting_fields_and_preserves_policy(self):
        plan = self.plan()
        changed = backend.refresh_fitting_targets(plan, free_targets_bytes=(131477163, 0),
                                                  available_free_bytes=(15000 << 20, 15000 << 20), now_ns=time.monotonic_ns())
        old, new = asdict(plan), asdict(changed)
        for key in ("argv", "exact_free_targets_bytes", "fitting_targets_mib", "fitting_rounding_bytes", "evaluated_at_ns"):
            old.pop(key)
            new.pop(key)
        self.assertEqual(old, new)
        position = plan.argv.index("--fit-target") + 1
        self.assertEqual(plan.argv[:position] + plan.argv[position + 1:], changed.argv[:position] + changed.argv[position + 1:])
        self.assertEqual(changed.fitting_targets_mib[0] * backend.MIB - changed.fitting_rounding_bytes[0], 131477163)
        for target, available in (((1400000001, 0), (15000 << 20, 15000 << 20)),
                                  ((100000000, 1), (15000 << 20, 15000 << 20)),
                                  ((100000001, 0), (100000001, 15000 << 20))):
            with self.subTest(target=target), self.assertRaises(backend.BackendError):
                backend.refresh_fitting_targets(plan, free_targets_bytes=target, available_free_bytes=available, now_ns=time.monotonic_ns())
        broken = replace(plan, argv=plan.argv + ("--fit-target", "0,0"))
        with self.assertRaises(backend.BackendError):
            backend.refresh_fitting_targets(broken, free_targets_bytes=(1, 0), available_free_bytes=(backend.MIB, backend.MIB), now_ns=time.monotonic_ns())

    def test_cli_facade_builds_bounded_plan_and_passes_launch_identity(self):
        pin = runtime.RpcProcess(**self.recipe["rpc_process"])
        guest = Mock(pin=pin)
        guest.observe.return_value = {"allocations": []}
        hooks = runtime.RuntimeHooks(lambda _: snapshot(), lambda _: [], Mock())
        cancel = threading.Event()
        with (patch.object(runtime, "runtime_recipe", return_value=self.recipe),
              patch.object(runtime, "capture_guard", return_value=Mock()),
              patch.object(runtime, "guard_policies", return_value=POLICIES),
              patch.object(runtime, "capture_rpc", return_value=pin),
              patch.object(runtime, "GuestRPC", return_value=guest),
              patch.object(runtime, "system_hooks", return_value=hooks) as hook_factory,
              patch.object(backend, "probe", side_effect=lambda _: self.proof()),
              patch.object(runtime, "run_owned", return_value=SimpleNamespace(stop_verified=True, trigger="cancelled")) as run,
              patch.dict(os.environ, {}, clear=False)):
            self.assertEqual(runtime.run_recipe(self.record, cancelled=cancel), 0)
            plan = run.call_args.args[0]
            self.assertEqual(plan.argv[plan.argv.index("--alias") + 1], runtime.POOL_MODEL_ID)
            self.assertEqual(plan.exact_free_targets_bytes, (1400000000 - 100 * backend.MIB, 0))
            self.assertEqual(run.call_args.kwargs["launch_id"], "a" * 32)
            self.assertEqual(run.call_args.kwargs["startup_id"], "a" * 32)
            self.assertIs(run.call_args.kwargs["cancelled"], cancel)
            self.assertEqual(hook_factory.call_args.args[1], self.record.with_suffix(".pool-status.json"))

    def test_cli_refuses_protocol_compatible_but_different_rpc_build(self):
        pin = replace(runtime.RpcProcess(**self.recipe["rpc_process"]), executable_sha256="b" * 64)
        with (patch.object(runtime, "runtime_recipe", return_value=self.recipe),
              patch.object(runtime, "capture_guard", return_value=Mock()),
              patch.object(runtime, "guard_policies", return_value=POLICIES),
              patch.object(runtime, "capture_rpc", return_value=pin),
              patch.object(runtime, "cleanup_pending_rpc", return_value=True) as cleanup,
              patch.object(runtime, "run_owned") as run,
              patch.dict(os.environ, {}, clear=False)):
            with self.assertRaisesRegex(runtime.RuntimeError_, "paired frozen"):
                runtime.run_recipe(self.record)
            run.assert_not_called()
            cleanup.assert_called_once()


if __name__ == "__main__":
    unittest.main()
