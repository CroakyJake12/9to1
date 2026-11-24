from dataclasses import replace
import hashlib
from importlib.util import find_spec
from io import BytesIO
import json
import os
from pathlib import Path
from tempfile import TemporaryDirectory
import threading
import unittest
from uuid import uuid4

from custody.artifacts import CustodyError
from custody.qwen35_rank_assembly import assemble_qwen35_rank
from custody.qwen35_shard_plan import RankPlan, TensorHeader, TensorPlacement
from custody.qwen35_shard_reader import RankShardManifest, RankTensorReader, ShardPin, TensorPin
from custody.qwen35_tensor_stream import StreamRankTensorReader, _ITEM, _OPEN, write_rank_tensors

_SHARD = "model-00001-of-00001.safetensors"
_KEYS = ("model.language_model.layers.0.q_proj.weight", "model.language_model.layers.1.q_proj.weight")
_FOREIGN = "model.language_model.layers.32.q_proj.weight"


def digest(data):
    return hashlib.sha256(data).hexdigest()


class RankTensorByteStreamTests(unittest.TestCase):
    def setUp(self):
        tmp = TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.data = (b"\x80?\x00@", b"\x80@\x00A")
        names = (*_KEYS, _FOREIGN)
        metadata = {key: {"dtype": "BF16", "shape": [2], "data_offsets": [i * 4, (i + 1) * 4]}
                    for i, key in enumerate(names)}
        raw = json.dumps(metadata).encode()
        header = len(raw).to_bytes(8, "little") + raw
        shard = header + b"".join(self.data) + b"tail"
        (self.root / _SHARD).write_bytes(shard)
        index = json.dumps({"weight_map": {key: _SHARD for key in names}}).encode()
        (self.root / "model.safetensors.index.json").write_bytes(index)
        self.placements = tuple(TensorPlacement(key, _SHARD, TensorHeader("BF16", (2,), i * 4, (i + 1) * 4))
                                for i, key in enumerate(_KEYS))
        self.plan = RankPlan(0, 32, self.placements, (_SHARD,), 8)
        self.manifest = RankShardManifest(digest(index), (ShardPin(_SHARD, len(shard), digest(header)),),
                                         tuple(TensorPin(key, digest(data)) for key, data in zip(_KEYS, self.data)))
        self.options = {"attempt_id": str(uuid4()), "plan": self.plan, "manifest": self.manifest,
                        "max_tensor_bytes": 4, "max_backing_bytes": 8}

    def encoded(self):
        stream = BytesIO()
        with RankTensorReader(self.root, self.plan, self.manifest, max_tensor_bytes=4) as reader:
            write_rank_tensors(stream, reader=reader, **self.options)
        return stream.getvalue()

    def test_verified_bytes_and_module_order_do_not_duplicate_backing(self):
        data = self.encoded()
        self.assertNotIn(b"tail", data)
        stream = BytesIO(data + b"next-protocol")
        receiver = StreamRankTensorReader(stream, **self.options)
        second = receiver._take(self.placements[1])
        pending = receiver._pending[_KEYS[0]]
        self.assertIs(receiver._take(self.placements[0]), pending)
        self.assertEqual((pending, second), self.data)
        receiver.finish()
        self.assertEqual(stream.read(), b"next-protocol")
        with self.assertRaises(CustodyError):
            receiver._take(self.placements[0])

    def test_order_oversize_attempt_and_hash_rejected_before_delivery(self):
        raw = self.encoded()
        tests = {"wrong attempt": (raw, self.options | {"attempt_id": str(uuid4())})}
        oversized = bytearray(raw)
        oversized[_OPEN.size + 4:_OPEN.size + 12] = (2**63).to_bytes(8, "big")
        tests["oversized on wire"] = (oversized, self.options)
        wrong_ordinal = bytearray(raw)
        wrong_ordinal[_OPEN.size:_OPEN.size + 4] = (1).to_bytes(4, "big")
        tests["out of order"] = (wrong_ordinal, self.options)
        wrong_digest = bytearray(raw)
        wrong_digest[_OPEN.size + _ITEM.size] ^= 0x01
        tests["wrong payload digest"] = (wrong_digest, self.options)
        tests["truncated payload"] = (raw[:_OPEN.size + _ITEM.size + 1], self.options)
        for description, (payload, options) in tests.items():
            with self.subTest(description=description):
                receiver = StreamRankTensorReader(BytesIO(payload), **options)
                with self.assertRaises(CustodyError):
                    receiver._take(self.placements[0])
                with self.assertRaisesRegex(CustodyError, "retired"):
                    receiver._take(self.placements[0])
        with self.assertRaises(CustodyError):
            StreamRankTensorReader(BytesIO(raw), **(self.options | {"max_backing_bytes": 7}))

    def test_duplicate_foreign_and_unfinished_fail_closed(self):
        raw = self.encoded()
        receiver = StreamRankTensorReader(BytesIO(raw), **self.options)
        receiver._take(self.placements[0])
        with self.assertRaisesRegex(CustodyError, "duplicate"):
            receiver._take(self.placements[0])
        receiver = StreamRankTensorReader(BytesIO(raw), **self.options)
        with self.assertRaisesRegex(CustodyError, "Foreign"):
            receiver._take(replace(self.placements[0], key=_FOREIGN))
        with self.assertRaisesRegex(CustodyError, "complete"):
            StreamRankTensorReader(BytesIO(raw), **self.options).finish()
        receiver = StreamRankTensorReader(BytesIO(raw[:-1]), **self.options)
        for placement in self.placements:
            receiver._take(placement)
        with self.assertRaises(CustodyError):
            receiver.finish()


@unittest.skipUnless(find_spec("torch") and find_spec("transformers"), "requires installed PyTorch/Qwen")
class RankTensorAssemblyStreamTests(unittest.TestCase):
    def test_real_pipe_reader_into_native_rank_assembly_without_guest_shards(self):
        import torch
        from custody.test_qwen35_owned_rank_integration import _reference, _synthetic_shards

        torch.set_num_threads(1)
        config, reference, head = _reference()
        with TemporaryDirectory() as tmp:
            root = Path(tmp)
            roots = tuple(root / f"rank{i}" for i in (0, 1))
            for path in roots:
                path.mkdir()
            plans, manifests = _synthetic_shards(roots, reference, head)
            rank = 1
            plan, manifest = plans.ranks[rank], manifests[rank]
            options = {"attempt_id": str(uuid4()), "plan": plan, "manifest": manifest,
                       "max_tensor_bytes": 32 * 1024, "max_backing_bytes": plan.frozen_backing_bytes}
            inbound, outbound = os.pipe()
            errors = []

            def host():
                try:
                    with os.fdopen(outbound, "wb") as output, RankTensorReader(
                        roots[rank], plan, manifest, max_tensor_bytes=options["max_tensor_bytes"]
                    ) as reader:
                        write_rank_tensors(output, reader=reader, **options)
                except Exception as error:
                    errors.append(error)

            writer = threading.Thread(target=host, daemon=True)
            writer.start()
            with os.fdopen(inbound, "rb") as input_stream:
                reader = StreamRankTensorReader(input_stream, **options)
                loaded = assemble_qwen35_rank(config, plan, split=32, rank=rank, read_tensor=reader)
                reader.finish()
            writer.join(5)
            self.assertFalse(writer.is_alive())
            self.assertFalse(errors)
            torch.testing.assert_close(loaded.lm_head.weight, head.weight, rtol=0, atol=0)
            for index, (_, layer) in enumerate(loaded.text._layers, 32):
                for name, value in layer.state_dict().items():
                    torch.testing.assert_close(value, reference.layers[index].state_dict()[name], rtol=0, atol=0)
