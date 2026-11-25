from importlib.util import find_spec
import unittest

from custody.artifacts import CustodyError
from custody.v8_rank_pager import _pager_types


@unittest.skipUnless(find_spec("torch"), "requires installed native PyTorch")
class StreamedFrozenLinearTests(unittest.TestCase):
    def test_streamed_and_resident_exact_forward_and_backward_with_bias(self):
        import torch

        linear, _ = _pager_types(torch)
        source = torch.nn.Linear(3, 2, bias=True, dtype=torch.bfloat16)
        with torch.no_grad():
            source.weight.copy_(torch.tensor([[1, -2, 3], [-1, 1, 2]], dtype=torch.bfloat16))
            source.bias.copy_(torch.tensor([.5, 1], dtype=torch.bfloat16))
        skeleton = torch.nn.Linear(3, 2, bias=True, dtype=torch.bfloat16, device="meta")
        skeleton.bias = torch.nn.Parameter(source.bias.detach(), requires_grad=False)
        events = []
        key = "model.language_model.layers.0.self_attn.q_proj.weight"

        def materialize(received, shape):
            self.assertEqual((received, shape), (key, (2, 3)))
            events.append("fetch")
            return source.weight.detach().to(dtype=torch.float16)

        streamed = linear(skeleton, lambda: events.append("duty"), torch.float16, 0,
                          weight_path=key, materialize_weight=materialize)
        resident = linear(source, lambda: None, torch.float16, 0)
        self.assertIsNone(streamed.source)
        self.assertEqual(streamed.weight.numel(), 0)
        self.assertEqual([p.numel() for p in streamed.parameters()], [0])
        inputs = torch.tensor([[1.0, 2.0, 3.0]], requires_grad=True)
        duplicate = inputs.detach().clone().requires_grad_(True)
        actual, expected = streamed(inputs), resident(duplicate)
        torch.testing.assert_close(actual, expected, rtol=0, atol=0)
        actual.sum().backward()
        expected.sum().backward()
        torch.testing.assert_close(inputs.grad, duplicate.grad, rtol=0, atol=0)
        self.assertEqual(events, ["duty", "fetch", "duty", "fetch", "duty"])

    def test_streamed_embedding_is_same_gather_and_releases_weight(self):
        import torch

        _, embedding = _pager_types(torch)
        source = torch.nn.Embedding(6, 3, padding_idx=0, dtype=torch.bfloat16)
        skeleton = torch.nn.Embedding(6, 3, padding_idx=0, dtype=torch.bfloat16, device="meta")
        calls = []
        def materialize(key, shape):
            calls.append((key, shape))
            return source.weight.detach().to(dtype=torch.float16)
        resident = embedding(source, lambda: None, torch.float16)
        streamed = embedding(skeleton, lambda: None, torch.float16, materialize_weight=materialize)
        ids = torch.tensor([[0, 4, 2]])
        value = streamed(ids)
        self.assertTrue(value.requires_grad and value.is_leaf)
        self.assertIsNone(streamed.source)
        torch.testing.assert_close(value, resident(ids), rtol=0, atol=0)
        self.assertEqual(calls, [("model.language_model.embed_tokens.weight", (6, 3))])

    def test_callback_wrong_dtype_shape_gradient_or_storage_is_refused(self):
        import torch

        linear, _ = _pager_types(torch)
        original = torch.nn.Linear(3, 2, bias=False, dtype=torch.bfloat16, device="meta")
        for result in (torch.zeros(2, 3), torch.zeros(3, 2, dtype=torch.float16),
                       torch.zeros(2, 3, dtype=torch.float16, requires_grad=True),
                       torch.zeros(12, dtype=torch.float16)[:6].reshape(2, 3)):
            with self.subTest(result=result), self.assertRaisesRegex(CustodyError, "exact FP16"):
                streamed = linear(original, lambda: None, torch.float16, 0,
                                  weight_path="exact.weight", materialize_weight=lambda *_: result)
                streamed(torch.zeros(1, 3))
        with self.assertRaisesRegex(CustodyError, "resident"):
            linear(original, lambda: None, torch.float16, 0)
