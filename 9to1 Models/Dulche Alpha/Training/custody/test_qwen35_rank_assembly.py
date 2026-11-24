from dataclasses import replace
from importlib.util import find_spec
from math import prod
import unittest

from custody.artifacts import CustodyError
from custody.qwen35_rank_assembly import assemble_qwen35_rank
from custody.qwen35_shard_plan import HEAD, RankPlan, TensorHeader, TensorPlacement
from custody.test_qwen35_range import make_tiny_text

_SHARD = "model-00001-of-00001.safetensors"
_TEXT = "model.language_model."


@unittest.skipUnless(find_spec("torch") and find_spec("transformers"), "requires installed native PyTorch/Qwen")
class RankAssemblyTests(unittest.TestCase):
    def setUp(self):
        import torch

        torch.manual_seed(41)
        self.native = make_tiny_text()
        self.native.config.tie_word_embeddings = False
        state = {name: tensor.to(torch.bfloat16) for name, tensor in self.native.state_dict().items()}
        self.native.load_state_dict(state, strict=True, assign=True)
        self.head = torch.nn.Linear(32, 32, bias=False, dtype=torch.bfloat16)
        self.values = {_TEXT + name: tensor for name, tensor in state.items()}
        self.values[HEAD] = self.head.weight.detach()
        self.plans = []
        for rank, owned in enumerate((
            ("layers.0.", "layers.1.", "embed_tokens."), ("layers.2.", "layers.3.", "norm."),
        )):
            placements = []
            count = 0
            for name, tensor in sorted(self.values.items()):
                if not name.startswith(tuple(_TEXT + p for p in owned)) and not (rank == 1 and name == HEAD):
                    continue
                shape = tuple(tensor.shape)
                length = 2 * prod(shape)
                placements.append(TensorPlacement(name, _SHARD, TensorHeader("BF16", shape, count, count + length)))
                count += length
            self.plans.append(RankPlan(rank * 2, rank * 2 + 2, tuple(placements), (_SHARD,), count))

    def test_only_owned_bf16_payload_is_assigned_with_native_rotary_and_tail_head(self):
        import torch

        def assembled(rank):
            plan = self.plans[rank]
            seen = []
            def read_tensor(placement):
                self.assertIn(placement, plan.tensors)
                self.assertNotIn(placement.key, seen)
                seen.append(placement.key)
                return self.values[placement.key]
            result = assemble_qwen35_rank(self.native.config, plan, split=2, rank=rank, read_tensor=read_tensor)
            self.assertEqual(set(seen), {placement.key for placement in plan.tensors})
            self.assertEqual({name for name, _ in result.text._rotary.named_buffers()}, {"inv_freq", "original_inv_freq"})
            for name, buffer in result.text._rotary.named_buffers():
                torch.testing.assert_close(buffer, getattr(self.native.rotary_emb, name), rtol=0, atol=0)
            return result

        early, tail = assembled(0), assembled(1)
        self.assertIsNone(early.lm_head)
        self.assertEqual(tail.lm_head.weight.data_ptr(), self.values[HEAD].data_ptr())
        self.assertEqual(early.text._edge.weight.data_ptr(), self.values[_TEXT + "embed_tokens.weight"].data_ptr())
        self.assertNotEqual(tail.lm_head.weight.data_ptr(), early.text._edge.weight.data_ptr())
        for rank, result in enumerate((early, tail)):
            for index, (_, layer) in enumerate(result.text._layers, start=rank * 2):
                for name, tensor in layer.state_dict().items():
                    source = self.values[f"{_TEXT}layers.{index}.{name}"]
                    self.assertEqual(tensor.dtype, torch.bfloat16)
                    self.assertEqual(tensor.data_ptr(), source.data_ptr())
        ids = torch.tensor([[1, 5, 9, 7, 2]])
        with torch.no_grad():
            expected = self.head(self.native(input_ids=ids, use_cache=False).last_hidden_state)
            actual = tail.lm_head(tail.text.forward(hidden=early.text.forward(input_ids=ids)))
        torch.testing.assert_close(actual, expected, rtol=0, atol=0)

    def test_foreign_missing_duplicate_or_wrong_header_refuses_before_reader(self):
        early, tail = self.plans
        first = early.tensors[0]
        bad_headers = [
            (*early.tensors, tail.tensors[0]),
            early.tensors[:-1],
            (*early.tensors, first),
            (replace(first, header=replace(first.header, dtype="F32")), *early.tensors[1:]),
            (replace(first, header=replace(first.header, shape=(1,))), *early.tensors[1:]),
        ]
        def not_called(_):
            self.fail("must refuse before reading checkpoint payload")
        for tensors in bad_headers:
            with self.assertRaises(CustodyError):
                assemble_qwen35_rank(self.native.config, replace(early, tensors=tensors),
                                     split=2, rank=0, read_tensor=not_called)

    def test_reader_tensor_wrong_dtype_or_shape_is_refused(self):
        import torch

        plan = self.plans[0]
        for reader in (lambda placement: self.values[placement.key].float(),
                       lambda placement: torch.ones((1,), dtype=torch.bfloat16)):
            with self.assertRaisesRegex(CustodyError, "BF16 CPU placement"):
                assemble_qwen35_rank(self.native.config, plan, split=2, rank=0, read_tensor=reader)
