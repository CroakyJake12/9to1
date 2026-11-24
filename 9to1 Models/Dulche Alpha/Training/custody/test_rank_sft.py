from dataclasses import replace
from importlib.util import find_spec
import io
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest

from .artifacts import ArtifactStore, CustodyError
from .rank_checkpoint import RankStageReceipt, TwoRankCheckpoint
from .rank_grad_clip import ClipPlan, GradientSlot, decide_global_clip
from .rank_sft import RankSftStep, RankSftUpdate, commit_sft_update


class SftCustodyTests(unittest.TestCase):
    def setUp(self):
        tmp = TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.store = ArtifactStore(Path(tmp.name) / "private")
        run = self.store.create_run("CPU SFT fixture", {})
        origin = self.store.begin_attempt(run, {})
        self.base = self.store.publish_bytes(origin, "previous", "checkpoint", b"old")
        self.attempt = self.store.begin_attempt(run, {}, resume_from=(self.base,))
        self.owners = ("early", "tail")
        self.checkpoint = TwoRankCheckpoint(self.store, self.attempt, 1, self.base, self.owners)
        self.plan = ClipPlan(self.attempt.attempt_id, 1, self.owners,
                             (GradientSlot(0, "x"), GradientSlot(1, "y")))

    def test_pending_or_unmeasured_update_cannot_commit(self):
        for updates in ((), (RankSftUpdate(None, None, 0.0),) * 2):
            with self.assertRaisesRegex(CustodyError, "verified finite update"):
                commit_sft_update(self.checkpoint, self.plan, (), updates)
        self.assertIsNone(self.checkpoint.committed())

    @unittest.skipUnless(find_spec("torch"), "requires installed PyTorch")
    def test_accumulation_and_configured_optimizer_match_native_and_no_replay(self):
        import torch

        def pair():
            return [torch.nn.Parameter(torch.tensor([value])) for value in (2.0, 3.0)]

        actual, expected = pair(), pair()
        make = lambda params: torch.optim.AdamW(params, lr=0.01)
        optimizers = [make([p]) for p in actual]
        reference = make(expected)
        steps = [RankSftStep(self.checkpoint, self.plan, rank=i,
                             parameters=((name, p),), optimizer=o, grad_accum=2)
                 for i, (name, p, o) in enumerate(zip(("x", "y"), actual, optimizers))]
        for batch, factor in enumerate((1, 2)):
            steps[0].microbatch(batch, lambda: (actual[0].square() * factor / 2).sum().backward())
            steps[1].microbatch(batch, lambda backward: backward((actual[1].square() * factor).sum()))
            ((expected[0].square() + expected[1].square()) * factor / 2).sum().backward()
        receipts = tuple(step.prepare_clip() for step in steps)
        decision = decide_global_clip(self.plan, receipts)
        self.assertEqual(decision.norm, torch.nn.utils.clip_grad_norm_(expected, 1.0).item())
        reference.step()

        def fragment(rank):
            buf = io.BytesIO()
            torch.save({"adapter": actual[rank].detach(), "optim": optimizers[rank].state_dict()}, buf)
            ref = self.store.publish_bytes(self.attempt, f"fragment-{rank}", "segment_checkpoint",
                                           buf.getvalue(), inputs=(self.base,))
            return RankStageReceipt(self.attempt, 1, self.base, rank, self.owners[rank], ref)

        a = steps[0].apply_and_stage(decision, lambda: fragment(0))
        self.assertIsNone(self.checkpoint.committed())
        with self.assertRaisesRegex(CustodyError, "no longer replayable"):
            steps[0].apply_and_stage(decision, lambda: fragment(0))
        b = steps[1].apply_and_stage(decision, lambda: fragment(1))
        with self.assertRaisesRegex(CustodyError, "different global clip"):
            bad = (receipts[0], replace(receipts[1], slots=(replace(receipts[1].slots[0], value=1.0),)))
            commit_sft_update(self.checkpoint, self.plan, bad, (a, b))
        result = commit_sft_update(self.checkpoint, self.plan, receipts, (a, b))
        self.assertEqual(self.checkpoint.committed(), result)
        for value, ref in zip(actual, expected):
            torch.testing.assert_close(value, ref, rtol=0, atol=0)
            self.assertIsNone(value.grad)

    @unittest.skipUnless(find_spec("torch"), "requires installed PyTorch")
    def test_cancel_after_joint_clip_before_optimizer_mutates_no_parameters_or_state(self):
        import torch
        from .rank_grad_clip import ClipPlan, GradientSlot
        from .rank_sft import RankSftStep

        plan = ClipPlan(self.attempt.attempt_id, 1, self.owners, (GradientSlot(1, "y"),))
        parameter = torch.nn.Parameter(torch.tensor([3.0]))
        before = parameter.detach().clone()
        adam = torch.optim.AdamW([parameter], lr=0.01)
        a = RankSftStep(self.checkpoint, plan, rank=0, parameters=(), optimizer=None, grad_accum=1)
        b = RankSftStep(self.checkpoint, plan, rank=1, parameters=(("y", parameter),), optimizer=adam, grad_accum=1)
        a.microbatch(0, lambda: None)
        b.microbatch(0, lambda backward: backward(parameter.square().sum()))
        receipts = (a.prepare_clip(), b.prepare_clip())
        decision = decide_global_clip(plan, receipts)

        def cancelled():
            # The guard is after global clipping, but no optimizer side effects exist.
            self.assertLessEqual(parameter.grad.norm().item(), 1.0)
            self.assertEqual(adam.state_dict()["state"], {})
            torch.testing.assert_close(parameter, before, rtol=0, atol=0)
            raise CustodyError("lease revoked before optimizer")

        def unexpected_publish():
            self.fail("cancelled rank should not publish")

        with self.assertRaisesRegex(CustodyError, "lease revoked"):
            b.apply_and_stage(decision, unexpected_publish, before_optimizer=cancelled)
        torch.testing.assert_close(parameter, before, rtol=0, atol=0)
        self.assertEqual(adam.state_dict()["state"], {})
        self.assertIsNone(self.checkpoint.committed())
        with self.assertRaisesRegex(CustodyError, "no longer replayable"):
            b.apply_and_stage(decision, unexpected_publish)
