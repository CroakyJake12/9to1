from contextlib import redirect_stdout
from dataclasses import asdict
import hashlib
from io import BytesIO, StringIO
import json
from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch
from uuid import uuid4

from .artifacts import CustodyError
from .qwen35_shard_plan import RankPlan, TensorHeader, TensorPlacement
from .qwen35_shard_reader import RankShardManifest, ShardPin, TensorPin
from .v8_guest_main_channel import FrozenGuestMainChannel
from .v8_joint_step import JointV8Ports
from .v8_owned_endpoints import _RTX, _V100
from .v8_registered_rank import PinnedRankInputs, RegisteredRankRun, _main_ports, main, rank_plan_inputs


class RegisteredRankTests(unittest.TestCase):
    def test_real_cli_dry_route_and_no_live_route_without_installed_joint_owner(self):
        attempt = str(uuid4())
        early = ["early", "--attempt", attempt, "--gpu-uuid", _V100]
        with redirect_stdout(StringIO()) as capture:
            self.assertEqual(main([*early, "--dry-run"]), 0)
        self.assertIn("no launch", capture.getvalue())
        with self.assertRaisesRegex(CustodyError, "no immutable registered joint owner"):
            main(early)
        with self.assertRaises(CustodyError):
            main(["tail", "--attempt", attempt, "--gpu-uuid", _V100, "--dry-run"])

    def test_pinned_original_rank_plan_schema_and_rejects_different_file_or_lane(self):
        with TemporaryDirectory() as raw:
            root = Path(raw)
            shard = "model-00001-of-00018.safetensors"
            key = "model.language_model.layers.0.self_attn.q_proj.weight"
            plan = RankPlan(0, 32, (TensorPlacement(key, shard, TensorHeader("BF16", (2,), 0, 4)),),
                            (shard,), 4)
            manifest = RankShardManifest("a" * 64, (ShardPin(shard, 100, "b" * 64),),
                                         (TensorPin(key, "c" * 64),))
            def write(path, value):
                data = json.dumps(asdict(value)).encode()
                path.write_bytes(data)
                return hashlib.sha256(data).hexdigest()
            paths = PinnedRankInputs(root / "unused", "", root / "plan", write(root / "plan", plan),
                                     root / "manifest", write(root / "manifest", manifest),
                                     root / "unused", "")
            self.assertEqual(rank_plan_inputs(paths, 0), (plan, manifest))
            with self.assertRaises(CustodyError):
                rank_plan_inputs(paths, 1)
            paths.rank_plan.write_bytes(b"{}")
            with self.assertRaisesRegex(CustodyError, "differs"):
                rank_plan_inputs(paths, 0)

    def test_tail_may_not_return_norms_before_both_process_and_transcript_checks(self):
        attempt = str(uuid4())
        sampler, attest = Mock(), Mock(return_value=True)
        ports = JointV8Ports(Mock(), Mock(), Mock(), Mock(), Mock(), Mock())
        channel = FrozenGuestMainChannel(rank=1, attempt_id=attempt, step=1, owner="guest",
                                         grad_accum=8, seq_cap=2048, vocab_size=128, inbound=BytesIO(),
                                         outbound=BytesIO(), sampler=sampler)
        plan = SimpleNamespace(attempt_id=attempt, step=1, owners=("guest", "host"), names=lambda _: ())
        admission = RegisteredRankRun(attempt, 1, "host", "tail", _RTX, None, ports, None, 1024, channel,
                                      host_sampler=sampler, authenticate_guest_completion=attest,
                                      checkpoint=SimpleNamespace(owners=plan.owners), clip_plan=plan)
        next_ports, early = _main_ports(admission, 1)
        with self.assertRaisesRegex(CustodyError, "out of order"):
            next_ports.exchange_norms(1, "native norm")
        self.assertEqual(early, [])
        attest.assert_not_called()
