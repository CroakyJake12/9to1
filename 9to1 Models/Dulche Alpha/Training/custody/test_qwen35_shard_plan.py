"""Metadata-only cases; no files or model fixtures are needed."""
from dataclasses import replace
import unittest

from .qwen35_shard_plan import (
    EMBEDDING, HEAD, NORM, ShardHeader, TensorHeader, plan_qwen35_text_shards,
)


A = "model-00001-of-00002.safetensors"
B = "model-00002-of-00002.safetensors"


def layer(i):
    return f"model.language_model.layers.{i}.mlp.proj.weight"


def shard(specs):
    entries = {}
    pos = 0
    for key, shape, width in specs:
        entries[key] = TensorHeader("BF16", shape, pos, pos + width)
        pos += width
    return ShardHeader(pos, entries)


def records():
    a = [(layer(i), (2,), 4) for i in range(32)]
    b = [(layer(i), (2,), 4) for i in range(32, 64)]
    a += [(EMBEDDING, (4, 2), 16), ("model.visual.pos_embed.weight", (2,), 4)]
    b += [(NORM, (2,), 4), (HEAD, (4, 2), 16), ("mtp.layers.0.mlp.weight", (1,), 2)]
    headers = {A: shard(a), B: shard(b)}
    return {key: name for name, header in headers.items() for key in header.tensors}, headers


def plan(index, headers, **kwargs):
    opts = dict(index_payload_bytes=float(sum(h.payload_bytes for h in headers.values())), tied_embeddings=False)
    return plan_qwen35_text_shards(index, headers, **(opts | kwargs))


class RankShardPlanTests(unittest.TestCase):
    def test_keeps_original_ranges_and_untied_owners_in_deterministic_order(self):
        index, headers = records()
        value = plan(index, headers)
        early, tail = value.ranks
        self.assertEqual((0, 32, 32, 64), (early.start_layer, early.stop_layer, tail.start_layer, tail.stop_layer))
        self.assertEqual(({layer(i) for i in range(32)} | {EMBEDDING}), {p.key for p in early.tensors})
        self.assertEqual(({layer(i) for i in range(32, 64)} | {HEAD, NORM}), {p.key for p in tail.tensors})
        self.assertEqual(((A,), (B,)), (early.shards, tail.shards))
        self.assertEqual((144, 148, 2, 6), (early.frozen_backing_bytes, tail.frozen_backing_bytes, value.omitted_tensors, value.omitted_bytes))
        self.assertEqual(value, plan(dict(reversed(tuple(index.items()))), dict(reversed(tuple(headers.items())))))

    def test_rejects_unsafe_names_and_index_header_disagreement(self):
        index, headers = records()
        for unsafe in ("../" + A, "/" + A, "\\" + A, "dir/" + A, "discard.txt"):
            with self.subTest(unsafe=unsafe), self.assertRaisesRegex(ValueError, "relative shard"):
                plan(index | {EMBEDDING: unsafe}, headers)
        with self.assertRaisesRegex(ValueError, "header set"):
            plan(index, {A: headers[A]})
        with self.assertRaisesRegex(ValueError, "tensor set"):
            plan(index | {EMBEDDING: B}, headers)

    def test_rejects_unknown_tensor_namespace_and_missing_original_layer(self):
        index, headers = records()
        for key in ("model.vision_imposter.weight", "model.visual.extra.weight", "mtp.extra.weight", "model.language_model.layers.064.weight", layer(64)):
            with self.subTest(key=key), self.assertRaisesRegex(ValueError, "namespace"):
                tensors = dict(headers[A].tensors)
                tensors[key] = tensors.pop(layer(0))
                changed = dict(index)
                changed[key] = changed.pop(layer(0))
                plan(changed, headers | {A: replace(headers[A], tensors=tensors)})
        tensors = dict(headers[A].tensors)
        tensors["model.visual.blocks.0.fixture_weight"] = tensors.pop(layer(0))
        changed = dict(index)
        changed["model.visual.blocks.0.fixture_weight"] = changed.pop(layer(0))
        with self.assertRaisesRegex(ValueError, "layer ranges"):
            plan(changed, headers | {A: replace(headers[A], tensors=tensors)})

    def test_rejects_offsets_dtype_total_and_tied_or_inconsistent_outer_weights(self):
        index, headers = records()
        a = headers[A]
        t = a.tensors[EMBEDDING]
        for change, failure in (
            (replace(t, start=0, end=16), "gaps or overlaps"),
            (replace(t, dtype="F32"), "BF16"),
            (replace(t, end=t.end + 1), "BF16"),
            (replace(t, shape=(2, 4)), "dimensions"),
        ):
            with self.subTest(change=change), self.assertRaisesRegex(ValueError, failure):
                plan(index, headers | {A: replace(a, tensors=a.tensors | {EMBEDDING: change})})
        with self.assertRaisesRegex(ValueError, "unsafe shard"):
            plan(index, headers | {A: replace(a, payload_bytes=9 << 30)})
        with self.assertRaisesRegex(ValueError, "total"):
            plan(index, headers, index_payload_bytes=299)
        for unsafe in (float("nan"), float("inf"), 299.25, True):
            with self.subTest(unsafe=unsafe), self.assertRaisesRegex(ValueError, "index payload"):
                plan(index, headers, index_payload_bytes=unsafe)
        with self.assertRaisesRegex(ValueError, "untied"):
            plan(index, headers, tied_embeddings=True)


if __name__ == "__main__":
    unittest.main()
