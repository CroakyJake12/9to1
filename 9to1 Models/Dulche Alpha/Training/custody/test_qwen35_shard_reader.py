from dataclasses import replace
from hashlib import sha256
from importlib.util import find_spec
import json
import os
from pathlib import Path
import struct
from tempfile import TemporaryDirectory
import unittest

from custody.artifacts import CustodyError
from custody.qwen35_shard_plan import RankPlan, TensorHeader, TensorPlacement
from custody.qwen35_shard_reader import RankShardManifest, RankTensorReader, ShardPin, TensorPin

_OWNED = "model.language_model.layers.0.q_proj.weight"
_OTHER = "model.language_model.layers.32.q_proj.weight"
_SHARD = "model-00001-of-00001.safetensors"


class RankShardReaderTests(unittest.TestCase):
    def setUp(self):
        tmp = TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        # BF16 1, -2, 0.5, 4 and an unrelated tail tensor in the same file.
        self.owned = struct.pack("<4H", 0x3F80, 0xC000, 0x3F00, 0x4080)
        self.tail = b"tailtail"
        self.index = self.root / "model.safetensors.index.json"
        self.index.write_text(json.dumps({"weight_map": {_OWNED: _SHARD, _OTHER: _SHARD}}), encoding="utf-8")
        metadata = {
            _OWNED: {"dtype": "BF16", "shape": [2, 2], "data_offsets": [0, 8]},
            _OTHER: {"dtype": "BF16", "shape": [2, 2], "data_offsets": [8, 16]},
        }
        header = json.dumps(metadata).encode()
        self.header = len(header).to_bytes(8, "little") + header
        self.path = self.root / _SHARD
        self.path.write_bytes(self.header + self.owned + self.tail)
        self.tensor = TensorPlacement(_OWNED, _SHARD, TensorHeader("BF16", (2, 2), 0, 8))
        self.plan = RankPlan(0, 32, (self.tensor,), (_SHARD,), 8)
        self.manifest = RankShardManifest(
            sha256(self.index.read_bytes()).hexdigest(),
            (ShardPin(_SHARD, self.path.stat().st_size, sha256(self.header).hexdigest()),),
            (TensorPin(_OWNED, sha256(self.owned).hexdigest()),),
        )

    def reader(self, plan=None, manifest=None, limit=8):
        return RankTensorReader(self.root, plan or self.plan, manifest or self.manifest, max_tensor_bytes=limit)

    def test_owned_data_and_plan_only(self):
        with self.reader() as reader:
            self.assertEqual(reader._read_bytes(self.tensor), self.owned)
            with self.assertRaises(CustodyError):
                reader._read_bytes(replace(self.tensor, key=_OTHER))
            with self.assertRaises(CustodyError):
                reader._read_bytes(replace(self.tensor, header=TensorHeader("BF16", (2, 2), 8, 16)))
        with self.assertRaises(CustodyError):
            reader._read_bytes(self.tensor)
        with self.assertRaisesRegex(CustodyError, "oversized"):
            self.reader(limit=7)

    @unittest.skipIf(os.name == "nt", "Windows may refuse replacing a file with an open descriptor")
    def test_read_keeps_verified_descriptor_after_path_replacement(self):
        with self.reader() as reader:
            replacement = self.root / "replacement"
            replacement.write_bytes(self.header + self.tail + self.owned)
            replacement.replace(self.path)
            self.assertEqual(reader._read_bytes(self.tensor), self.owned)
        with self.reader() as reader, self.assertRaisesRegex(CustodyError, "digest"):
            reader._read_bytes(self.tensor)

    def test_owned_digest_catches_in_place_write_but_other_rank_is_not_read(self):
        with self.reader() as reader, self.path.open("r+b") as modify:
            modify.seek(len(self.header) + 8)
            modify.write(b"changed!")
            modify.flush()
            self.assertEqual(reader._read_bytes(self.tensor), self.owned)
            modify.seek(len(self.header))
            modify.write(b"changed!")
            modify.flush()
            with self.assertRaisesRegex(CustodyError, "digest"):
                reader._read_bytes(self.tensor)

    def test_wrong_pinned_index_or_header_fails_before_any_tensor_request(self):
        with self.assertRaisesRegex(CustodyError, "index failed"):
            with self.reader(manifest=replace(self.manifest, index_sha256="0" * 64)):
                self.fail()
        pin = replace(self.manifest.shards[0], header_sha256="0" * 64)
        with self.assertRaisesRegex(CustodyError, "header failed"):
            with self.reader(manifest=replace(self.manifest, shards=(pin,))):
                self.fail()

    def test_foreign_placement_missing_pins_and_symlink_refused(self):
        bad = replace(self.tensor, key=_OTHER)
        with self.assertRaisesRegex(CustodyError, "foreign"):
            self.reader(plan=replace(self.plan, tensors=(bad,)))
        with self.assertRaisesRegex(CustodyError, "policy"):
            self.reader(manifest=replace(self.manifest, tensors=()))
        old = self.root / "original"
        self.path.rename(old)
        try:
            self.path.symlink_to(old)
        except OSError:
            self.skipTest("symlinks unavailable")
        with self.assertRaisesRegex(CustodyError, "links"):
            with self.reader():
                self.fail()

    @unittest.skipUnless(find_spec("torch"), "requires installed native PyTorch")
    def test_torch_bf16_values_and_lifetime_after_descriptor_closes(self):
        import torch
        with self.reader() as reader:
            tensor = reader(self.tensor)
        self.assertEqual(tensor.device.type, "cpu")
        self.assertEqual(tensor.dtype, torch.bfloat16)
        torch.testing.assert_close(tensor, torch.tensor([[1, -2], [.5, 4]], dtype=torch.bfloat16), rtol=0, atol=0)
