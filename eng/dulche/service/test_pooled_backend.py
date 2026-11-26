import dataclasses
from pathlib import Path
import struct
import subprocess
import tempfile
import unittest
from unittest import mock

import pooled_backend as backend


DEVICES = """ggml_cuda_init: found 1 CUDA devices:
  Device 0: NVIDIA GeForce RTX 5080, compute capability 12.0, VMM: yes
Available devices:
  RPC0: 127.0.0.1:19110 (16384 MiB, 16000 MiB free)
  CUDA0: NVIDIA GeForce RTX 5080 (16303 MiB, 15000 MiB free)
"""
HELP = ("\n".join(sorted(backend.REQUIRED_FLAGS - {"--fit-target"}))
        + "\n-fitt, --fit-target MiB0,MiB1,MiB2,...   target margin per device\n")
NOW = 10_000_000_000
MAX_AGE = 1_000_000_000
# Artificial test identity only; production requires a caller-observed UUID.
GUEST_UUID = "GPU-11111111-2222-3333-4444-555555555555"


def gguf_prefix(version=3, tensors=1, metadata=1, padding=64):
    return struct.pack("<4sIQQ", b"GGUF", version, tensors, metadata) + b"\0" * padding


class BackendTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.exe = self.root / "llama-server"
        self.exe.write_text("not a real backend; never executed")
        self.exe.chmod(0o700)
        self.model = self.root / "test model;literal.gguf"
        self.model.write_bytes(gguf_prefix())
        self.key = self.root / "api.key"
        self.key.write_text("private-fixture-value")
        self.key.chmod(0o600)

    def proof(self):
        return backend.BackendProof(str(self.exe), backend.digest(self.exe),
                                    "fixture", backend.REQUIRED_FLAGS,
                                    backend.parse_devices(DEVICES), NOW - 200, True)

    def guest(self):
        return backend.GuestIdentity(backend.RPC_ENDPOINT, "RPC0", GUEST_UUID,
                                     "Tesla V100-PCIE-16GB", NOW - 100,
                                     "owned-test-rpc-generation-1")

    def plan(self, proof=None, **overrides):
        args = dict(model=str(self.model), alias="local-pooled-test",
                    key_file=str(self.key), context=4096, port=19142,
                    free_targets_bytes=(1_400_000_000, 0),
                    guest_identity=self.guest(), now_ns=NOW, max_age_ns=MAX_AGE)
        args.update(overrides)
        return backend.command_plan(proof or self.proof(), **args)

    def test_order_is_physical_not_listing_order(self):
        devices = backend.parse_devices(DEVICES)
        self.assertEqual([d.name for d in devices],
                         ["CUDA0", "RPC0"])
        self.assertEqual(devices[1].description, backend.RPC_ENDPOINT)

    def test_foreign_endpoint_and_single_gpu_are_refused(self):
        cases = [DEVICES.replace("127.0.0.1:19110", "127.0.0.1:19111"),
                 "CUDA0: NVIDIA GeForce RTX 5080 (16303 MiB, 15000 MiB free)"]
        for output in cases:
            with self.subTest(output=output), self.assertRaises(backend.BackendError):
                backend.parse_devices(output)

    def test_rpc_listing_needs_separate_guest_hardware_evidence(self):
        # The same RPC listing could come from a CPU-backed guest.
        self.assertEqual(self.proof().devices[1].name, "RPC0")
        for guest in (None, dataclasses.replace(self.guest(), gpu_name="CPU")):
            with self.subTest(guest=guest), self.assertRaises(backend.BackendError):
                self.plan(guest_identity=guest)

    def test_guest_evidence_must_match_owned_endpoint_and_distinct_uuid(self):
        cases = [dict(endpoint="127.0.0.1:19111"), dict(rpc_device_name="RPC1"),
                 dict(gpu_uuid=backend.RTX_UUID), dict(gpu_uuid="unobserved"),
                 dict(gpu_name="Tesla V100-PCIE-32GB"),
                 dict(owned_rpc_server_identity=""), dict(owned_rpc_server_identity="  ")]
        for changes in cases:
            with self.subTest(changes=changes), self.assertRaises(backend.BackendError):
                self.plan(guest_identity=dataclasses.replace(self.guest(), **changes))

    def test_duplicate_and_impossible_memory_are_refused(self):
        for output in (DEVICES + DEVICES,
                       DEVICES.replace("16384 MiB, 16000", "15000 MiB, 16000")):
            with self.assertRaises(backend.BackendError):
                backend.parse_devices(output)

    def test_malformed_device_rows_rejected_but_diagnostic_logs_ignored(self):
        for row in ("RPC0: malformed", "CUDA0: malformed", "RPC1: incomplete",
                    "RPC: malformed", "CUDA 0: malformed", "RPCx: malformed",
                    "RPC0[127.0.0.1:19110]: Tesla V100-PCIE-16GB (16384 MiB, 16000 MiB free)"):
            with self.subTest(row=row), self.assertRaises(backend.BackendError):
                backend.parse_devices(DEVICES + row + "\n")
        output = DEVICES + "ggml_backend: RPC0[127.0.0.1:19110] buffer diagnostic\n"
        self.assertEqual(backend.parse_devices(output), backend.parse_devices(DEVICES))

    def test_duplicate_rpc_endpoint_is_ambiguous(self):
        with self.assertRaises(backend.BackendError):
            backend.parse_devices(DEVICES + "RPC1: 127.0.0.1:19110 (16384 MiB, 16000 MiB free)\n")

    def test_preserves_byte_policy_and_reports_conservative_rounding(self):
        plan = self.plan()
        self.assertEqual(plan.display_reserve_policy_bytes, 1_400_000_000)
        self.assertEqual(plan.fitting_targets_mib, (1336, 0))
        self.assertEqual(plan.fitting_rounding_bytes, (897536, 0))
        self.assertFalse(plan.hardware_partition)
        self.assertFalse(plan.load_validated)
        self.assertIn(str(self.model), plan.argv)
        self.assertNotIn("private-fixture-value", repr(plan))
        self.assertEqual(plan.argv[plan.argv.index("--host") + 1], "127.0.0.1")
        self.assertEqual(plan.argv[plan.argv.index("--parallel") + 1], "1")
        self.assertEqual(plan.argv[plan.argv.index("--device") + 1], "CUDA0,RPC0")
        self.assertEqual(plan.guest_identity.gpu_uuid, GUEST_UUID)
        self.assertEqual(plan.observed_at_ns, NOW - 200)
        self.assertEqual(plan.evaluated_at_ns, NOW)

    def test_current_display_use_is_not_subtracted_again(self):
        # Caller planner found 900 MB already occupied outside managed inference.
        plan = self.plan(free_targets_bytes=(500_000_000, 0))
        self.assertEqual(plan.exact_free_targets_bytes, (500_000_000, 0))
        self.assertEqual(plan.fitting_targets_mib, (477, 0))
        self.assertEqual(plan.display_reserve_policy_bytes, 1_400_000_000)

    def test_plan_preserves_oldest_actual_observation(self):
        guest = dataclasses.replace(self.guest(), observed_at_ns=NOW - 300)
        self.assertEqual(self.plan(guest_identity=guest).observed_at_ns, NOW - 300)

    def test_old_future_and_invalid_backend_or_guest_evidence_rejected(self):
        for timestamp in (NOW - MAX_AGE - 1, NOW + 1, -1, True, 1.5):
            with self.subTest(source="backend", timestamp=timestamp), self.assertRaises(backend.BackendError):
                self.plan(dataclasses.replace(self.proof(), observed_at_ns=timestamp))
            with self.subTest(source="guest", timestamp=timestamp), self.assertRaises(backend.BackendError):
                self.plan(guest_identity=dataclasses.replace(self.guest(), observed_at_ns=timestamp))
        for overrides in (dict(now_ns=True), dict(max_age_ns=0), dict(max_age_ns=-1)):
            with self.subTest(overrides=overrides), self.assertRaises(backend.BackendError):
                self.plan(**overrides)

    def test_freshness_boundary_is_inclusive(self):
        proof = dataclasses.replace(self.proof(), observed_at_ns=NOW - MAX_AGE)
        guest = dataclasses.replace(self.guest(), observed_at_ns=NOW - MAX_AGE)
        self.assertEqual(self.plan(proof, guest_identity=guest).observed_at_ns, NOW - MAX_AGE)

    def test_changed_executable_invalidates_proof(self):
        proof = self.proof()
        self.exe.write_text("changed")
        with self.assertRaises(backend.BackendError):
            self.plan(proof)

    def test_private_key_file_is_required(self):
        self.key.chmod(0o644)
        with self.assertRaises(backend.BackendError):
            self.plan()
        self.key.chmod(0o600)
        link = self.root / "key-link"
        link.symlink_to(self.key)
        with self.assertRaises(backend.BackendError):
            self.plan(key_file=str(link))

    def test_no_shell_or_inherited_model_settings(self):
        env = backend.controlled_environment({"PATH": "/usr/bin", "KEEP": "yes",
              "LLAMA_ARG_MODEL": "foreign", "LLAMA_ARG_API_KEY": "secret",
              "GGML_RPC_SERVERS": "remote", "CUDA_VISIBLE_DEVICES": "all"})
        self.assertNotIn("LLAMA_ARG_MODEL", env)
        self.assertNotIn("LLAMA_ARG_API_KEY", env)
        self.assertNotIn("GGML_RPC_SERVERS", env)
        self.assertEqual(env["CUDA_VISIBLE_DEVICES"], backend.RTX_UUID)
        self.assertEqual(env["KEEP"], "yes")

    def test_plan_and_probe_share_sanitizer_without_storing_secret_environment(self):
        base = {"PATH": "/usr/bin", "KEEP_SECRET": "secret-fixture-value",
                "LLAMA_ARG_N_GPU_LAYERS": "0", "LLAMA_ARG_TENSOR_SPLIT": "1,0",
                "LLAMA_ARG_MODEL_DRAFT": "foreign", "GGML_RPC_SERVERS": "foreign",
                "GGML_CUDA_P2P": "0", "GGML_CUDA_ENABLE_PEER_ACCESS": "1",
                "GGML_CUDA_ENABLE_UNIFIED_MEMORY": "1", "GGML_CUDA_DEVICES": "2",
                "GGML_CUDA_NO_PINNED": "1", "GGML_CUDA_POOL_DEBUG": "1",
                "GGML_OP_OFFLOAD_MIN_BATCH": "1", "CUDA_VISIBLE_DEVICES": "all"}
        original = dict(base)
        with mock.patch.dict(backend.os.environ, base, clear=True):
            plan = self.plan()
            implicit = plan.materialize_environment()
        env = plan.materialize_environment(base)
        self.assertEqual(env, backend.controlled_environment(base))
        self.assertEqual(implicit, env)
        self.assertEqual(env, {"PATH": "/usr/bin", "KEEP_SECRET": "secret-fixture-value",
                               "CUDA_VISIBLE_DEVICES": backend.RTX_UUID})
        self.assertEqual(base, original)
        self.assertNotIn("secret-fixture-value", repr(plan))
        self.assertNotIn("secret-fixture-value", repr(dataclasses.asdict(plan)))
        self.assertIn("GGML_CUDA_", plan.environment_unset_prefixes)

    def test_probe_only_inspects_no_model_or_secret_arguments(self):
        calls = []
        def runner(argv, **kwargs):
            calls.append(argv)
            self.assertNotIn("shell", kwargs)
            self.assertEqual(kwargs["env"]["CUDA_VISIBLE_DEVICES"], backend.RTX_UUID)
            out = ("fixture version" if "--version" in argv else
                   HELP if "--help" in argv else DEVICES)
            # A malformed device-like stderr diagnostic is not list data.
            return subprocess.CompletedProcess(argv, 0, out, "RPC0: diagnostic only\n")
        proof = backend.probe(str(self.exe), runner=runner, clock_ns=lambda: NOW - 200)
        self.assertEqual(len(calls), 3)
        self.assertEqual(calls[-1], [str(self.exe), "--rpc", backend.RPC_ENDPOINT, "--list-devices"])
        self.assertEqual(proof.devices[0].name, "CUDA0")
        self.assertTrue(proof.per_device_fit_targets)
        self.assertEqual(proof.observed_at_ns, NOW - 200)
        self.assertNotIn(str(self.model), repr(calls))

    def test_target_microbatch_and_diagnostics_are_fixed_in_the_launch_plan(self):
        plan = self.plan()
        for flag, value in (("--ubatch-size", "128"), ("--log-verbosity", "4")):
            with self.subTest(flag=flag):
                self.assertEqual(plan.argv.count(flag), 1)
                self.assertEqual(plan.argv[plan.argv.index(flag) + 1], value)
        self.assertNotIn("--spec-draft-n-ubatch", plan.argv)

    def test_missing_target_options_are_refused_before_device_enumeration_or_planning(self):
        for flag in ("--ubatch-size", "--log-verbosity"):
            calls = []
            def runner(argv, **kwargs):
                calls.append(argv)
                text = "fixture" if "--version" in argv else HELP.replace(flag, "")
                return subprocess.CompletedProcess(argv, 0, text, "")
            with self.subTest(flag=flag):
                with self.assertRaisesRegex(backend.BackendError, flag):
                    backend.probe(str(self.exe), runner=runner)
                self.assertEqual(len(calls), 2)
                with self.assertRaises(backend.BackendError):
                    self.plan(dataclasses.replace(self.proof(), flags=self.proof().flags - {flag}))

    def test_dspark_has_its_own_fixed_microbatch_and_preserves_draft_length(self):
        flags = {"--spec-type", "--model-draft", "--spec-draft-n-max", "--spec-draft-n-ubatch"}
        proof = dataclasses.replace(self.proof(), flags=self.proof().flags | flags, dspark_speculation=True)
        draft = backend.DraftConfig(str(self.model), backend.digest(self.model), self.model.stat().st_size, 2)
        plan = self.plan(proof, draft=draft)
        for flag, value in (("--ubatch-size", "128"), ("--spec-draft-n-ubatch", "128"),
                            ("--spec-draft-n-max", "2")):
            with self.subTest(flag=flag):
                self.assertEqual(plan.argv.count(flag), 1)
                self.assertEqual(plan.argv[plan.argv.index(flag) + 1], value)
        for missing in flags:
            with self.subTest(missing=missing), self.assertRaisesRegex(backend.BackendError, "DSpark"):
                self.plan(dataclasses.replace(proof, flags=proof.flags - {missing}), draft=draft)

    def test_advertised_dspark_type_does_not_substitute_for_draft_microbatch_support(self):
        help_text = HELP + "\n--spec-type draft-dspark\n--model-draft FILE\n--spec-draft-n-max N\n"
        def runner(argv, **kwargs):
            text = "fixture" if "--version" in argv else help_text if "--help" in argv else DEVICES
            return subprocess.CompletedProcess(argv, 0, text, "")
        proof = backend.probe(str(self.exe), runner=runner, clock_ns=lambda: NOW - 200)
        self.plan(proof)  # A target-only load does not require draft capabilities.
        draft = backend.DraftConfig(str(self.model), backend.digest(self.model), self.model.stat().st_size)
        with self.assertRaisesRegex(backend.BackendError, "DSpark"):
            self.plan(proof, draft=draft)

    def test_scalar_only_fit_help_is_not_list_capability(self):
        calls = []
        def runner(argv, **kwargs):
            calls.append(argv)
            out = "fixture" if "--version" in argv else HELP.replace("MiB0,MiB1,MiB2,...", "MiB")
            return subprocess.CompletedProcess(argv, 0, out, "")
        with self.assertRaisesRegex(backend.BackendError, "per-device"):
            backend.probe(str(self.exe), runner=runner)
        self.assertEqual(len(calls), 2)
        with self.assertRaisesRegex(backend.BackendError, "per-device"):
            self.plan(dataclasses.replace(self.proof(), per_device_fit_targets=False))

    def test_failed_rpc_probe_cannot_produce_proof(self):
        def runner(argv, **kwargs):
            code = 1 if "--rpc" in argv else 0
            return subprocess.CompletedProcess(argv, code,
                HELP, "private error contents")
        with self.assertRaisesRegex(backend.BackendError, "exited 1") as error:
            backend.probe(str(self.exe), runner=runner)
        self.assertNotIn("private error contents", str(error.exception))

    def test_unreasonable_reserve_invalid_alias_and_reverse_devices_rejected(self):
        for overrides in (dict(free_targets_bytes=(1 << 60, 0)),
                          dict(alias="--bad option"), dict(context=True)):
            with self.subTest(overrides=overrides), self.assertRaises(backend.BackendError):
                self.plan(**overrides)
        proof = dataclasses.replace(self.proof(), devices=tuple(reversed(self.proof().devices)))
        with self.assertRaises(backend.BackendError):
            self.plan(proof)

    def test_direct_device_byte_validation_rejects_sub_mib_invalid_values(self):
        proof = self.proof()
        rtx, rpc = proof.devices
        for changes in (dict(free_bytes=rtx.total_bytes + 1), dict(free_bytes=True),
                        dict(total_bytes=1.5), dict(free_bytes=-1)):
            bad = dataclasses.replace(rtx, **changes)
            with self.subTest(changes=changes), self.assertRaises(backend.BackendError):
                self.plan(dataclasses.replace(proof, devices=(bad, rpc)))

    def test_rounding_must_still_fit_captured_free_bytes(self):
        proof = self.proof()
        rtx = dataclasses.replace(proof.devices[0], free_bytes=backend.MIB + 1)
        with self.assertRaisesRegex(backend.BackendError, "Rounded"):
            self.plan(dataclasses.replace(proof, devices=(rtx, proof.devices[1])),
                      free_targets_bytes=(backend.MIB + 1, 0))

    def test_truncated_unsupported_and_implausible_gguf_candidates_rejected(self):
        cases = [b"", b"GGUF", gguf_prefix()[:23], gguf_prefix()[:24],
                 b"NOPE" + gguf_prefix()[4:],
                 struct.pack(">4sIQQ", b"GGUF", 3, 1, 1) + b"\0" * 64]
        cases += [gguf_prefix(version=v) for v in (0, 1, 4)]
        cases += [gguf_prefix(tensors=t, metadata=m)
                  for t, m in ((0, 1), (1, 0), (1_000_001, 1), (1, 1_000_001))]
        cases.append(gguf_prefix(padding=37))  # One byte below loose count bound.
        for candidate in cases:
            self.model.write_bytes(candidate)
            with self.subTest(length=len(candidate)), self.assertRaises(backend.BackendError):
                self.plan()

    def test_plausible_v2_v3_headers_never_claim_model_compatibility(self):
        for version in (2, 3):
            self.model.write_bytes(gguf_prefix(version=version, padding=38))
            with self.subTest(version=version):
                self.assertFalse(self.plan().load_validated)


if __name__ == "__main__":
    unittest.main()
