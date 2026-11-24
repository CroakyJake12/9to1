from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import Mock, patch

from .artifacts import ArtifactStore, CustodyError
from .rank_checkpoint import TwoRankCheckpoint
from .rank_grad_clip import ClipPlan, GradientSlot
from .v8_first_segment import RegisteredV8Sampler, run_first_owned_step
from .v8_flash_window import FLASH_ID


class FirstV8SegmentTests(unittest.TestCase):
    def setUp(self):
        tmp = TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        store = ArtifactStore(Path(tmp.name) / "sealed")
        run = store.create_run("first-step source fixture", {})
        original = store.begin_attempt(run, {})
        base = store.publish_bytes(original, "prior", "checkpoint", b"synthetic")
        attempt = store.begin_attempt(run, {}, resume_from=(base,))
        self.owners = ("guest-run-owner", "native-run-owner")
        self.checkpoint = TwoRankCheckpoint(store, attempt, 1, base, self.owners)
        self.plan = ClipPlan(attempt.attempt_id, 1, self.owners, (GradientSlot(1, "adapter"),))
        self.owner = "joint-v8-lease"
        self.window = {"owner": self.owner, "phase": "held", "models": [
            {"id": FLASH_ID, "prior_state": "loaded", "release": {
                "release_verified": True, "remaining_owned_vram_mib": 0}}]}
        self.request = Mock(return_value=self.window)
        self.launch = Mock(return_value=("early", "tail"))
        self.options = dict(checkpoint=self.checkpoint, clip_plan=self.plan,
                            settings=RegisteredV8Sampler("/pinned", "f" * 64, 42, 512, 4, 0),
                            owner=self.owner, tokenizer=Mock(), verify_registered_launch=lambda: True,
                            permit=lambda: True, request_window=self.request, run_owned_ranks=self.launch,
                            park_deadline=10, clock=lambda: 1, wait=lambda _: None)

    def test_registered_sampler_and_exact_held_lease_bound_before_two_owned_receipts(self):
        def run(**kwargs):
            self.assertIs(kwargs["read_original_batch"], sampler.return_value.microbatch)
            self.assertTrue(kwargs["mentor_quiesced"]())
            self.assertEqual((kwargs["grad_accum"], kwargs["seq_len"]), (4, 512))
            return ("early", "tail")

        self.launch.side_effect = run
        with patch("custody.v8_first_segment.V8HostSampler") as sampler, \
                patch("custody.v8_first_segment.commit_v8_sft_step", return_value="verified") as commit:
            self.assertEqual(run_first_owned_step(**self.options), "verified")
        sampler.assert_called_once_with("/pinned", self.options["tokenizer"], manifest_sha256="f" * 64,
                                        seed=42, seq_len=512, grad_accum=4, start_step=0,
                                        resumed_python_rng=None)
        commit.assert_called_once_with(self.checkpoint, self.plan, early="early", tail="tail")
        self.request.assert_any_call("acquire", owner=self.owner, models=[FLASH_ID], socket_path=None)
        self.assertEqual([item.args[0] for item in self.request.mock_calls], ["acquire", "status", "status", "status"])

    def test_unregistered_launch_or_sampler_failure_never_parks_flash(self):
        with self.assertRaisesRegex(CustodyError, "not admitted"):
            run_first_owned_step(**(self.options | {"verify_registered_launch": lambda: False}))
        with patch("custody.v8_first_segment.V8HostSampler", side_effect=CustodyError("pin mismatch")), \
                self.assertRaisesRegex(CustodyError, "pin mismatch"):
            run_first_owned_step(**self.options)
        self.request.assert_not_called()
        self.launch.assert_not_called()
