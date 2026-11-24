from dataclasses import asdict
import hashlib
import json
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch

from .artifacts import CustodyError, _encode_record
from .qwen35_shard_reader import RankTensorReader
from .test_qwen35_shard_plan import plan, records
from .v8_publisher_plans import publisher_rank_plans


def _sha(raw):
    return hashlib.sha256(raw).hexdigest()


class PublisherPlansTests(unittest.TestCase):
    def setUp(self):
        tmp = TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        weight_map, headers = records()
        self.expected = plan(weight_map, headers).ranks
        self.config_pin = self.write("config.json", {
            "architectures": ["Qwen3_5ForConditionalGeneration"], "text_config": {"tie_word_embeddings": False},
        })
        index_pin = self.write("model.safetensors.index.json", {
            "weight_map": weight_map, "metadata": {"total_size": sum(h.payload_bytes for h in headers.values())},
        })
        pins = {}
        for name, header in headers.items():
            raw = json.dumps({key: {"dtype": value.dtype, "shape": list(value.shape),
                                  "data_offsets": [value.start, value.end]}
                              for key, value in header.tensors.items()}).encode()
            envelope = len(raw).to_bytes(8, "little") + raw
            (self.root / name).write_bytes(envelope + b"\0" * header.payload_bytes)
            pins[name] = {"name": name, "file_bytes": len(envelope) + header.payload_bytes,
                          "header_sha256": _sha(envelope)}
        self.manifests = tuple(self.write(f"pin-rank{rank}.json", {
            "index_sha256": index_pin[1], "shards": [pins[name] for name in p.shards],
            "tensors": [{"key": t.key, "sha256": _sha(b"\0" * (t.header.end - t.header.start))} for t in p.tensors],
        }) for rank, p in enumerate(self.expected))

    def write(self, name, value):
        path, raw = self.root / name, _encode_record(value)
        path.write_bytes(raw)
        return path, _sha(raw)

    def plans(self):
        return publisher_rank_plans(self.root, self.manifests, config_sha256=self.config_pin[1])

    def test_exact_owned_plans_without_any_payload_read_or_load(self):
        with patch.object(RankTensorReader, "_read_bytes", side_effect=AssertionError("payload opened")):
            self.assertEqual(self.plans(), tuple(_encode_record(asdict(p)) for p in self.expected))

    def test_authenticated_index_or_header_edit_cannot_change_plan(self):
        path = self.root / self.expected[0].shards[0]
        with path.open("r+b") as edit:
            edit.seek(9)
            edit.write(b"x")
        with self.assertRaisesRegex(CustodyError, "header proof"):
            self.plans()
