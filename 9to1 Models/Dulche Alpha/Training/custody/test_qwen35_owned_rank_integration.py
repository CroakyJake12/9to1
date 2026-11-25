"""Offline CPU fixture: sealed synthetic shards to exact two-rank Qwen gradients."""

import hashlib
from importlib.util import find_spec
import json
import multiprocessing
from pathlib import Path
import socket
from tempfile import TemporaryDirectory
import unittest
from uuid import uuid4

from custody.qwen35_rank_assembly import assemble_qwen35_rank
from custody.qwen35_shard_plan import HEAD, ShardHeader, TensorHeader, _rank, plan_qwen35_text_shards
from custody.qwen35_shard_reader import RankShardManifest, RankTensorReader, ShardPin, TensorPin
from custody.qwen35_stage import Qwen35RankStage
from custody.rank_autograd import AutogradBoundary
from custody.rank_stream import BoundaryFrame
from custody.test_qwen35_range import make_tiny_text

_PREFIX = "model.language_model."
_INDEX = "model.safetensors.index.json"


def _bytes(tensor):
    # An exact little-endian byte view; does not convert BF16 through float.
    import torch
    return bytes(tensor.detach().contiguous().reshape(-1).view(torch.uint8).tolist())


def _digest(data):
    return hashlib.sha256(data).hexdigest()


def _reference():
    import torch
    from transformers.models.qwen3_5.modeling_qwen3_5 import Qwen3_5TextModel

    config = make_tiny_text().config
    config.num_hidden_layers = 64
    config.layer_types = ["linear_attention", "full_attention"] * 32
    config.tie_word_embeddings = False
    torch.manual_seed(41)
    text = Qwen3_5TextModel(config)
    # Keep native, derived RoPE buffers at their native FP32 dtype.
    text.load_state_dict({name: value.to(torch.bfloat16) for name, value in text.state_dict().items()},
                         strict=True, assign=True)
    head = torch.nn.Linear(32, 32, bias=False, dtype=torch.bfloat16)
    return config, text, head


def _synthetic_shards(roots, text, head):
    """Write two tiny completely disjoint synthetic BF16 Safetensors files."""
    tensors = {_PREFIX + key: value for key, value in text.state_dict().items()}
    tensors[HEAD] = head.weight.detach()
    shards, headers, all_pins, all_digests, weight_map = [], {}, [], [], {}
    for rank, root in enumerate(roots):
        shard = f"model-{rank + 1:05d}-of-00002.safetensors"
        shards.append(shard)
        metadata, parsed, digests = {}, {}, []
        payload = bytearray()
        for name, tensor in sorted(tensors.items()):
            if _rank(name)[0] != rank:
                continue
            data = _bytes(tensor)
            start, end = len(payload), len(payload) + len(data)
            payload.extend(data)
            shape = tuple(tensor.shape)
            metadata[name] = {"dtype": "BF16", "shape": shape, "data_offsets": [start, end]}
            parsed[name] = TensorHeader("BF16", shape, start, end)
            weight_map[name] = shard
            digests.append(TensorPin(name, _digest(data)))
        assert 0 < len(payload) < 2 * 1024 * 1024, "fixture must remain tiny"
        raw = json.dumps(metadata, separators=(",", ":")).encode("ascii")
        raw += b" " * (-len(raw) % 8)
        prefix = len(raw).to_bytes(8, "little") + raw
        (root / shard).write_bytes(prefix + payload)
        headers[shard] = ShardHeader(len(payload), parsed)
        all_pins.append(ShardPin(shard, len(prefix) + len(payload), _digest(prefix)))
        all_digests.append(tuple(digests))
    total = sum(header.payload_bytes for header in headers.values())
    index = json.dumps({"metadata": {"total_size": total}, "weight_map": weight_map}).encode("ascii")
    for root in roots:
        (root / _INDEX).write_bytes(index)
    plan = plan_qwen35_text_shards(weight_map, headers, index_payload_bytes=total, tied_embeddings=False)
    for rank in (0, 1):
        assert plan.ranks[rank].shards == (shards[rank],)
    manifests = tuple(RankShardManifest(_digest(index), (pin,), digests)
                      for pin, digests in zip(all_pins, all_digests))
    return plan, manifests


def _assemble(root, config, plan, manifest, rank):
    with RankTensorReader(root, plan, manifest, max_tensor_bytes=32 * 1024) as reader:
        return assemble_qwen35_rank(config, plan, split=32, rank=rank, read_tensor=reader)


def _tokens():
    import torch
    return torch.tensor([[1, 5, 9, 7, 2], [4, 3, 10, 6, 2]])


def _loss(logits, ids):
    import torch
    # Fixture values only. Backward remains caller-owned.
    return 2 * torch.nn.functional.cross_entropy(logits[:, :-1].float().reshape(-1, 32), ids[:, 1:].reshape(-1))


def _gradients(modules):
    return {prefix + name: (None if parameter.grad is None else _digest(_bytes(parameter.grad)))
            for prefix, module in modules
            for name, parameter in module.named_parameters()}


def _owned_gradients(assembled, rank):
    text = assembled.text
    edge = "embed_tokens" if rank == 0 else "norm"
    modules = [(_PREFIX + edge + ".", text._edge)]
    modules.extend((_PREFIX + f"layers.{i}.", layer)
                   for i, (_, layer) in enumerate(text._layers, start=32 * rank))
    if rank == 1:
        modules.append(("lm_head.", assembled.lm_head))
    return _gradients(modules)


def _early(peer, root, config, plan, manifest, boundary, results):
    import torch
    torch.set_num_threads(1)
    assembled = _assemble(root, config, plan, manifest, rank=0)
    assert assembled.lm_head is None
    stage = Qwen35RankStage(assembled.text, boundary)
    # Spawn reconstructs the fd, not Python's socket timeout setting.
    peer.settimeout(35)
    with peer, peer.makefile("rb") as inbound, peer.makefile("wb") as outbound:
        stage.early(_tokens(), inbound, outbound, step=1, microbatch=0)
    results.put((0, None, _owned_gradients(assembled, rank=0)))


def _tail(peer, root, config, plan, manifest, boundary, results):
    import torch
    torch.set_num_threads(1)
    assembled = _assemble(root, config, plan, manifest, rank=1)
    stage = Qwen35RankStage(assembled.text, boundary)

    def backward_loss(hidden):
        logits = assembled.lm_head(hidden)
        loss = _loss(logits, _tokens())
        loss.backward()
        return loss.item(), hidden.detach().tolist(), logits.detach().tolist()

    # Spawn reconstructs the fd, not Python's socket timeout setting.
    peer.settimeout(35)
    with peer, peer.makefile("rb") as inbound, peer.makefile("wb") as outbound:
        result = stage.tail(inbound, outbound, step=1, microbatch=0, device="cpu", backward_loss=backward_loss)
    results.put((1, result, _owned_gradients(assembled, rank=1)))


@unittest.skipUnless(find_spec("torch") and find_spec("transformers"), "requires installed native PyTorch/Qwen")
class OwnedRankIntegrationTests(unittest.TestCase):
    def test_pinned_disjoint_shards_through_two_rank_bf16_forward_loss_and_backward(self):
        import torch
        torch.set_num_threads(1)
        config, reference, head = _reference()
        ids = _tokens()
        with TemporaryDirectory() as work:
            roots = tuple(Path(work) / f"rank-{rank}" for rank in (0, 1))
            for root in roots:
                root.mkdir()
            plans, manifests = _synthetic_shards(roots, reference, head)
            for rank, root in enumerate(roots):
                self.assertEqual({p.name for p in root.iterdir()}, {_INDEX, plans.ranks[rank].shards[0]})
            boundary = AutogradBoundary(BoundaryFrame(str(uuid4()), 1, 0, 0, "activation", "bfloat16", (2, 5, 32)),
                                        max_bytes=640)
            ctx = multiprocessing.get_context("spawn")
            left, right = socket.socketpair()
            results = ctx.Queue()
            children = []
            try:
                for rank, (worker, stream) in enumerate(((_early, left), (_tail, right))):
                    child = ctx.Process(target=worker, args=(stream, roots[rank], config, plans.ranks[rank],
                                                             manifests[rank], boundary, results))
                    child.start()
                    children.append(child)
                    stream.close()
                by_rank = {}
                for _ in children:
                    rank, values, grads = results.get(timeout=35)
                    self.assertNotIn(rank, by_rank)
                    by_rank[rank] = values, grads
                for child in children:
                    child.join(10)
                    self.assertEqual(child.exitcode, 0)
                _, early_grads = by_rank[0]
                (loss, hidden, logits), tail_grads = by_rank[1]

                expected_hidden = reference(input_ids=ids, use_cache=False).last_hidden_state
                expected_logits = head(expected_hidden)
                expected_loss = _loss(expected_logits, ids)
                expected_loss.backward()
                self.assertEqual(loss, expected_loss.item())
                torch.testing.assert_close(torch.tensor(hidden, dtype=torch.bfloat16), expected_hidden, rtol=0, atol=0)
                torch.testing.assert_close(torch.tensor(logits, dtype=torch.bfloat16), expected_logits, rtol=0, atol=0)
                self.assertFalse(early_grads.keys() & tail_grads.keys())
                expected_grads = _gradients(((_PREFIX, reference), ("lm_head.", head)))
                self.assertEqual(early_grads | tail_grads, expected_grads)
                self.assertTrue(any(d for d in early_grads.values()))
                self.assertTrue(any(d for d in tail_grads.values()))
            finally:
                left.close()
                right.close()
                for child in children:
                    if child.is_alive():
                        child.terminate()
                        child.join(5)
                results.close()
