from importlib.util import find_spec
import unittest

from .artifacts import CustodyError
from .v8_rank_pager import _owned_v8_lora, _pager_types, _require_frozen_upper_lane


class FrozenPagerTests(unittest.TestCase):
    def test_only_explicit_upper_frozen_fp16_v100(self):
        args = ("upper", "float16", "cuda:0", (7, 0), 1)
        _require_frozen_upper_lane(*args)
        for i, bad in ((0, "rotate"), (0, "lower"), (1, "bfloat16"), (1, None),
                       (2, "cpu"), (2, "cuda:1"), (3, (12, 0)), (4, 0), (4, 2)):
            changed = list(args)
            changed[i] = bad
            with self.subTest(args=changed), self.assertRaises(CustodyError):
                _require_frozen_upper_lane(*changed)

    @unittest.skipUnless(find_spec("torch"), "requires installed PyTorch")
    def test_frozen_pager_retains_cpu_bf16_backing_and_propagates_fp16_grad(self):
        import torch
        from torch import nn
        from torch.nn import functional as F

        calls = []
        duty = lambda: calls.append(True)
        linear, embed = _pager_types(torch)
        base = nn.Linear(3, 2, bias=True, dtype=torch.bfloat16)
        host = nn.Embedding(4, 3, dtype=torch.bfloat16)
        adapted = linear(base, duty, torch.float16, 17)
        paged_embedding = embed(host, duty, torch.float16)
        ids = torch.tensor([[1, 2]])
        hidden = paged_embedding(ids)
        output = adapted(hidden)
        ref = F.embedding(ids, host.weight.detach()).to(torch.float16).requires_grad_(True)
        target = F.linear(ref, base.weight.detach().half(), base.bias.detach().half())
        torch.testing.assert_close(output, target, atol=0, rtol=0)
        output.sum().backward()
        target.sum().backward()
        torch.testing.assert_close(hidden.grad, ref.grad, atol=0, rtol=0)
        self.assertEqual(adapted.layer_id, 17)
        self.assertEqual(adapted.source.data_ptr(), base.weight.data_ptr())
        self.assertEqual(adapted.source.dtype, torch.bfloat16)
        self.assertEqual(adapted.weight.numel(), 0)
        self.assertEqual(tuple(adapted.buffers()), ())
        self.assertEqual(len(calls), 4)
        self.assertIsNone(host.weight.grad)
        self.assertIsNone(base.weight.grad)

    @unittest.skipUnless(find_spec("torch"), "requires installed PyTorch")
    def test_frozen_adapters_are_required_and_checked_against_global_target(self):
        import torch
        from torch import nn

        rows = ((17, "mlp.gate_proj", nn.Linear(3, 2)),)
        a = "model.language_model.layers.17.mlp.gate_proj.lora_A.default.weight"
        b = "model.language_model.layers.17.mlp.gate_proj.lora_B.default.weight"
        state = {a: torch.ones((16, 3)), b: torch.ones((2, 16))}
        self.assertEqual(_owned_v8_lora(rows, state, torch), state)
        for invalid in (None, {}, {a: state[a]}, {**state, b: state[b].half()}, {**state, "other": state[a]}):
            with self.subTest(invalid=type(invalid)), self.assertRaises(CustodyError):
                _owned_v8_lora(rows, invalid, torch)
