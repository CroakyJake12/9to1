from dataclasses import replace
from importlib.util import find_spec
import unittest
from uuid import uuid4

from custody.artifacts import CustodyError
from custody.rank_grad_clip import (
    ClipPlan, GradientSlot, NormReceipt, NormSlot, PreparedRankClip, _ordered_norms, decide_global_clip,
)


class ClipEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.plan = ClipPlan(str(uuid4()), 7, ("early-owner", "tail-owner"),
                             (GradientSlot(0, "first"), GradientSlot(1, "middle"), GradientSlot(0, "last")))
        self.a = NormReceipt(self.plan.attempt_id, 7, 0, "early-owner",
                             (NormSlot("first", "float32", 3.0), NormSlot("last", "bfloat16", 2.0)))
        self.b = NormReceipt(self.plan.attempt_id, 7, 1, "tail-owner", (NormSlot("middle", None, None),))

    def test_order_and_missing_slot(self):
        self.assertEqual(_ordered_norms(self.plan, (self.a, self.b)), self.a.slots)
        b = replace(self.b, slots=(NormSlot("middle", "float32", 1.0),))
        self.assertEqual([s.name for s in _ordered_norms(self.plan, (self.a, b))], ["first", "middle", "last"])

    def test_refuses_stale_reordered_forged_or_nonfinite_evidence(self):
        for bad in (replace(self.b, step=6), replace(self.b, attempt_id=str(uuid4())),
                    replace(self.b, owner="early-owner"), replace(self.b, rank=0),
                    replace(self.b, slots=(NormSlot("foreign", "float32", 2.0),)),
                    replace(self.b, slots=(NormSlot("middle", "float32", float("nan")),)),
                    replace(self.b, slots=(NormSlot("middle", "float32", float("inf")),)),
                    replace(self.b, slots=(NormSlot("middle", "float32", -1.0),))):
            with self.subTest(bad=bad), self.assertRaises(CustodyError):
                _ordered_norms(self.plan, (self.a, bad))
        with self.assertRaises(CustodyError):
            _ordered_norms(self.plan, (self.b, self.a))
        with self.assertRaises(CustodyError):
            _ordered_norms(self.plan, (replace(self.a, slots=self.a.slots[::-1]), self.b))
        with self.assertRaises(CustodyError):
            replace(self.plan, slots=(*self.plan.slots, GradientSlot(1, "first")))


@unittest.skipUnless(find_spec("torch"), "requires installed PyTorch")
class NativeClipTests(unittest.TestCase):
    def _exercise(self, rows):
        import torch

        def params():
            out = {}
            for name, rank, dtype, values, frozen in rows:
                parameter = torch.nn.Parameter(torch.zeros(2, dtype=getattr(torch, dtype)), requires_grad=not frozen)
                if values is not None:
                    parameter.grad = torch.tensor(values, dtype=parameter.dtype)
                out[name] = parameter
            return out

        plan = ClipPlan(str(uuid4()), 1, ("root-0", "root-1"),
                        tuple(GradientSlot(rank, name) for name, rank, *_ in rows))
        reference, actual = params(), params()
        native = torch.nn.utils.clip_grad_norm_(tuple(reference.values()), 1.0)
        ranks = tuple(PreparedRankClip(plan, rank=rank,
                                      parameters=tuple((name, actual[name]) for name in plan.names(rank)))
                      for rank in (0, 1))
        decision = decide_global_clip(plan, tuple(r.receipt for r in ranks))
        self.assertEqual(decision.norm, native.item())
        self.assertEqual(decision.dtype, str(native.dtype).removeprefix("torch."))
        for rank in ranks:
            rank.apply(decision)
            with self.assertRaisesRegex(CustodyError, "already consumed"):
                rank.apply(decision)
        for name in actual:
            if reference[name].grad is None:
                self.assertIsNone(actual[name].grad)
            else:
                torch.testing.assert_close(actual[name].grad, reference[name].grad, rtol=0, atol=0)

    def test_native_global_norm_and_scaled_gradients_same_with_bf16_mixed_and_frozen(self):
        self._exercise((("a", 0, "bfloat16", (3, 4), False),
                        ("b", 1, "bfloat16", (11, 2), False),
                        ("c", 0, "bfloat16", (7, -2), False)))
        self._exercise((("a", 0, "float32", (3, 4), False),
                        ("b", 1, "bfloat16", (11, 2), False),
                        ("frozen", 1, "float32", None, True),
                        ("c", 0, "float32", (7, -2), False)))
        self._exercise((("small", 0, "float32", (0.1, 0.1), False),
                        ("frozen", 1, "float32", None, True)))
        self._exercise((("frozen", 0, "float32", None, True),))
        self._exercise((("small", 1, "float32", (0.05, -0.1), False),))

    def test_local_invalid_or_changed_grad_fails_before_apply(self):
        import torch

        plan = ClipPlan(str(uuid4()), 7, ("a", "b"), (GradientSlot(0, "local"),))
        param = torch.nn.Parameter(torch.zeros(2))
        param.grad = torch.tensor([float("inf"), 3.0])
        with self.assertRaisesRegex(CustodyError, "nonfinite"):
            PreparedRankClip(plan, rank=0, parameters=(("local", param),))
        param.grad = torch.tensor([1.0, 3.0])
        rank0 = PreparedRankClip(plan, rank=0, parameters=(("local", param),))
        rank1 = PreparedRankClip(plan, rank=1, parameters=())
        decision = decide_global_clip(plan, (rank0.receipt, rank1.receipt))
        with self.assertRaisesRegex(CustodyError, "does not bind"):
            rank0.apply(replace(decision, norm=0.0))
        param.grad.add_(1)
        with self.assertRaisesRegex(CustodyError, "changed"):
            rank0.apply(decision)
        torch.testing.assert_close(param.grad, torch.tensor([2.0, 4.0]))
