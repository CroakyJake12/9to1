from copy import deepcopy
from importlib.util import find_spec
import unittest

from custody.artifacts import CustodyError
from custody.qwen35_range import Qwen35TextRange


@unittest.skipUnless(find_spec("torch") and find_spec("transformers"), "requires installed native PyTorch/Qwen")
class Qwen35RangeTests(unittest.TestCase):
    def make_text(self):
        import transformers
        if transformers.__version__ != "5.20.1":
            self.skipTest("requires exact Transformers 5.20.1")
        from transformers import Qwen3_5TextConfig
        from transformers.models.qwen3_5.modeling_qwen3_5 import Qwen3_5TextModel
        config = Qwen3_5TextConfig(
            vocab_size=32, hidden_size=32, intermediate_size=64, num_hidden_layers=4,
            num_attention_heads=2, num_key_value_heads=1, head_dim=16, linear_conv_kernel_dim=2,
            linear_key_head_dim=8, linear_value_head_dim=8, linear_num_key_heads=1,
            linear_num_value_heads=2, layer_types=["linear_attention", "full_attention"] * 2,
            max_position_embeddings=64, pad_token_id=0, use_cache=False, attention_dropout=0,
            rope_parameters={"rope_type": "default", "rope_theta": 10000.,
                             "partial_rotary_factor": 0.5, "mrope_section": [2, 1, 1]},
        )
        config._attn_implementation = "eager"
        return Qwen3_5TextModel(config)

    def test_native_hybrid_forward_shifted_loss_and_gradients_match(self):
        import torch
        torch.manual_seed(41)
        reference = self.make_text()
        ranked = deepcopy(reference)
        early, tail = (Qwen35TextRange(ranked, split=2, rank=i) for i in (0, 1))
        ids = torch.tensor([[1, 5, 9, 7, 2], [4, 3, 10, 6, 2]])
        logits = torch.nn.Linear(32, 32, bias=False)
        other_logits = deepcopy(logits)
        expected = reference(input_ids=ids, use_cache=False).last_hidden_state
        early_hidden = early.forward(input_ids=ids)
        leaf = early_hidden.detach().clone().requires_grad_(True)
        actual = tail.forward(hidden=leaf)
        torch.testing.assert_close(actual, expected, rtol=0, atol=0)

        def loss(head, hidden):
            return torch.nn.functional.cross_entropy(head(hidden[:, :-1]).reshape(-1, 32), ids[:, 1:].reshape(-1))

        expected_loss, actual_loss = loss(logits, expected), loss(other_logits, actual)
        torch.testing.assert_close(actual_loss, expected_loss, rtol=0, atol=0)
        expected_loss.backward()
        actual_loss.backward()
        self.assertIsNotNone(leaf.grad)
        early_hidden.backward(leaf.grad)
        self.assertEqual(set(dict(reference.named_parameters())), set(dict(ranked.named_parameters())))
        for name, parameter in ranked.named_parameters():
            with self.subTest(parameter=name):
                native_gradient = reference.get_parameter(name).grad
                if native_gradient is None:
                    self.assertIsNone(parameter.grad)
                else:
                    torch.testing.assert_close(parameter.grad, native_gradient, rtol=0, atol=0)

    def test_range_and_custom_context_are_refused(self):
        import torch
        text = self.make_text()
        for split, rank in ((0, 0), (4, 1), (2, 2)):
            with self.assertRaises(CustodyError):
                Qwen35TextRange(text, split=split, rank=rank)
        early = Qwen35TextRange(text, split=2, rank=0)
        ids = torch.tensor([[1, 2]])
        for field in ("position_ids", "attention_mask", "past_key_values", "use_cache"):
            with self.subTest(field=field), self.assertRaises(TypeError):
                early.forward(input_ids=ids, **{field: None})
        with self.assertRaises(CustodyError):
            early.forward(hidden=torch.randn(1, 2, 32))
        with self.assertRaises(CustodyError):
            Qwen35TextRange(text, split=2, rank=1).forward(hidden=torch.randn(1, 2, 31))
