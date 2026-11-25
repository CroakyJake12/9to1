"""Tiny numeric GGUF software fixtures, never a training corpus or real model."""
from dataclasses import replace
import hashlib
import os
from pathlib import Path
import unittest
from unittest.mock import patch
from uuid import uuid4

import numpy as np
from gguf import GGUFWriter, GGMLQuantizationType as Q, GGML_QUANT_SIZES, GGUFValueType
from custody.artifacts import create_directory
from alora.gguf_store import GGUFWeightStore
from alora.read_limits import FingerprintCapacityExceeded


class GGUFWeightStoreTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(os.environ["DULCHE_ALORA_TEST_ARTIFACT_ROOT"]) / ("gguf-store-" + uuid4().hex)
        create_directory(self.root, exclusive=True)

    def write(self, name="fixture.gguf", tensors=None, arch="qwen4exp", metadata=None):
        path = self.root / name
        writer = GGUFWriter(path, arch)
        writer.add_name("ALORA numeric software fixture")
        writer.add_string("alora.purpose", "software_test_only")
        if metadata:
            metadata(writer)
        tensors = tensors or {"matrix": (np.arange(128, dtype=np.float32).reshape(4, 32), None)}
        try:
            for key, (values, qtype) in tensors.items():
                writer.add_tensor(key, values, raw_dtype=qtype)
            writer.write_header_to_file()
            writer.write_kv_data_to_file()
            writer.write_tensors_to_file()
        finally:
            writer.close()
        return path

    @staticmethod
    def pins(paths):
        return {path.name: {"size_bytes": path.stat().st_size,
                            "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
                for path in paths}

    def store(self, paths, **kwargs):
        store = GGUFWeightStore(paths, expected_shards=self.pins(paths), **kwargs)
        self.addCleanup(store.close)
        return store

    @staticmethod
    def q8_rows():
        # Independent known Q8_0 blocks: fp16 scale followed by 32 signed bytes.
        integers = np.arange(-16, 16, dtype=np.int8)
        scales = (0.25, 2.0, 0.5)
        raw = np.stack([np.concatenate((
            np.array([scale], dtype=np.float16).view(np.uint8),
            integers.view(np.uint8))) for scale in scales])
        expected = np.stack([integers.astype(np.float32) * scale for scale in scales])
        return raw, expected

    def test_f32_matrix_and_packed_expert_are_sliced_before_copy_or_decode(self):
        matrix = np.arange(128, dtype=np.float32).reshape(4, 32)
        packed = np.arange(384, dtype=np.float32).reshape(3, 4, 32)
        path = self.write(tensors={"matrix": (matrix, None), "packed": (packed, None)})
        store = self.store([path], max_decoded_bytes=512, max_read_bytes=512)
        ref = store.matrix_ref("packed", expert=2, row_start=1, row_count=2)
        self.assertEqual(ref.shape, (2, 32))
        self.assertEqual(store.tensor_info("packed")["shape"], (3, 4, 32))
        actual = store.read(ref)
        np.testing.assert_array_equal(actual, packed[2, 1:3])
        self.assertTrue(actual.flags.c_contiguous)
        self.assertTrue(actual.flags.owndata)
        actual.fill(-1)
        np.testing.assert_array_equal(store.read(ref), packed[2, 1:3])
        self.assertEqual(store.stats["max_read_bytes"], 256)
        self.assertEqual(store.stats["max_decoded_bytes"], 256)
        with self.assertRaisesRegex(ValueError, "Whole tensor"):
            store.read_tensor("packed")

    def test_mixed_f32_and_q8_0_follow_actual_tensor_types_and_numeric_reference(self):
        raw, expected = self.q8_rows()
        matrix = np.arange(64, dtype=np.float32).reshape(2, 32)
        path = self.write(tensors={"quantized": (raw, Q.Q8_0), "float": (matrix, None)})
        store = self.store([path])
        self.assertEqual(store.tensor_info("quantized")["qtype"], "Q8_0")
        self.assertEqual(store.tensor_info("quantized")["shape"], expected.shape)
        np.testing.assert_array_equal(store.read(store.matrix_ref("quantized", row_start=1, row_count=2)),
                                      expected[1:])
        np.testing.assert_array_equal(store.read_tensor("float"), matrix)
        self.assertEqual(store.stats["decode_count"], 2)

    def test_all_observed_quant_types_decode_bounded_zero_blocks(self):
        tensors = {}
        for qtype in (Q.IQ3_S, Q.IQ4_XS, Q.IQ4_NL, Q.Q8_0, Q.Q6_K, Q.BF16):
            _block, size = GGML_QUANT_SIZES[qtype]
            tensors[qtype.name] = (np.zeros((2, size), dtype=np.uint8), qtype)
        tensors["half"] = (np.zeros((2, 8), dtype=np.float16), None)
        store = self.store([self.write(tensors=tensors)])
        for name, (_values, qtype) in tensors.items():
            with self.subTest(qtype=name):
                actual = store.read_tensor(name)
                columns = GGML_QUANT_SIZES[qtype][0] if qtype else 8
                self.assertEqual(actual.shape, (2, columns))
                np.testing.assert_array_equal(actual, np.zeros_like(actual))
                self.assertEqual(actual.dtype, np.float32)

    def test_small_bf16_vector_and_depthwise_tensor_retain_original_shapes(self):
        vector = np.array([0.25, -1.0, 0.0, 2.0], dtype=np.float32)
        raw = (vector.view(np.uint32) >> 16).astype(np.uint16).view(np.uint8)
        conv = np.arange(24, dtype=np.float32).reshape(2, 3, 4)
        store = self.store([self.write(tensors={"norm": (raw, Q.BF16), "conv": (conv, None)})])
        np.testing.assert_array_equal(store.read_tensor("norm"), vector)
        np.testing.assert_array_equal(store.read_tensor("conv"), conv)
        with self.assertRaises(ValueError):
            store.matrix_ref("norm")

    def test_gather_reads_only_unique_rows_preserving_order_and_duplicates(self):
        raw, expected = self.q8_rows()
        store = self.store([self.write(tensors={"table": (raw, Q.Q8_0)})])
        result = store.gather_rows("table", np.array([2, 0, 2], dtype=np.int64))
        np.testing.assert_array_equal(result, expected[[2, 0, 2]])
        self.assertTrue(result.flags.owndata)
        self.assertTrue(result.flags.c_contiguous)
        self.assertEqual(store.stats["read_count"], 2)
        self.assertEqual(store.stats["total_read_bytes"], 68)
        self.assertEqual(store.gather_rows("table", []).shape, (0, 32))
        self.assertEqual(store.stats["read_count"], 2)

    def test_read_and_decode_budgets_reject_before_dequantization(self):
        raw, _expected = self.q8_rows()
        path = self.write(tensors={"matrix": (raw, Q.Q8_0)})
        for limits, operation in [
            ({"max_read_bytes": 33}, lambda s: s.matrix_ref("matrix", row_count=1)),
            ({"max_decoded_bytes": 127}, lambda s: s.matrix_ref("matrix", row_count=1)),
            ({"max_read_bytes": 67}, lambda s: s.gather_rows("matrix", [0, 1])),
            ({"max_decoded_bytes": 255}, lambda s: s.gather_rows("matrix", [0, 0])),
            ({"max_decoded_bytes": 128}, lambda s: s.read_tensor("matrix")),
        ]:
            with self.subTest(limits=limits):
                store = self.store([path], **limits)
                with patch("alora.gguf_store.quants.dequantize") as dequant:
                    with self.assertRaises(ValueError):
                        operation(store)
                dequant.assert_not_called()
                self.assertEqual(store.stats["read_count"], 0)

    def test_fingerprint_capacity_refuses_new_slices_before_copy_and_preserves_reuse(self):
        path = self.write()
        store = self.store([path], max_fingerprints=2)
        refs = [store.matrix_ref("matrix", row_start=i, row_count=1) for i in range(3)]
        first = store.read(refs[0])
        store.read(refs[1])
        before = store.stats
        self.assertEqual(before["fingerprint_count"], 2)
        self.assertEqual(before["fingerprint_limit"], 2)
        with patch("alora.gguf_store.np.array") as copy_array, patch("alora.gguf_store.quants.dequantize") as dequant:
            for operation in (lambda: store.read(refs[2]), lambda: store.read_tensor("matrix")):
                with self.assertRaisesRegex(FingerprintCapacityExceeded, "Fingerprint capacity"):
                    operation()
            copy_array.assert_not_called()
            dequant.assert_not_called()
        self.assertEqual(store.stats, before)
        np.testing.assert_array_equal(store.read(refs[0]), first)
        self.assertEqual(store.stats["fingerprint_count"], 2)
        self.assertEqual(store.stats["decode_count"], before["decode_count"] + 1)
        self.mutate_weight(path, store._tensors["matrix"][1].data_offset)
        with patch.object(store, "_check_shard", return_value=None):
            with self.assertRaisesRegex(ValueError, "slice bytes changed"):
                store.read(refs[0])

    def test_fingerprint_gather_preflights_unique_new_rows_without_partial_reads(self):
        store = self.store([self.write()], max_fingerprints=2)
        store.gather_rows("matrix", [0, 0])
        before = store.stats
        self.assertEqual(before["fingerprint_count"], 1)
        with patch("alora.gguf_store.np.empty") as allocate, patch("alora.gguf_store.quants.dequantize") as dequant:
            with self.assertRaisesRegex(FingerprintCapacityExceeded, "Fingerprint capacity"):
                store.gather_rows("matrix", [1, 2])
            allocate.assert_not_called()
            dequant.assert_not_called()
        self.assertEqual(store.stats, before)
        matrix = np.arange(128, dtype=np.float32).reshape(4, 32)
        np.testing.assert_array_equal(store.gather_rows("matrix", [0, 1, 0]), matrix[[0, 1, 0]])
        self.assertEqual(store.stats["fingerprint_count"], 2)
        np.testing.assert_array_equal(store.gather_rows("matrix", [1, 0, 1]), matrix[[1, 0, 1]])
        self.assertEqual(store.stats["fingerprint_count"], 2)

    def test_fingerprint_limit_is_bounded_and_does_not_change_source_identity(self):
        path = self.write()
        for limit in (0, -1, True, 1.0, 1048577):
            with self.subTest(limit=limit), patch("alora.gguf_store.GGUFReader") as reader:
                with self.assertRaisesRegex(ValueError, "Fingerprint limit"):
                    GGUFWeightStore([path], expected_shards=self.pins([path]), max_fingerprints=limit)
                reader.assert_not_called()
        small = self.store([path], max_fingerprints=1)
        default = self.store([path])
        self.assertEqual(small.source_identity, default.source_identity)
        self.assertEqual(small.identity, default.identity)
        self.assertEqual(default.stats["fingerprint_limit"], 262144)
        self.assertEqual(default.stats["fingerprint_count"], 0)
        ref = small.matrix_ref("matrix", row_count=1)
        small.read(ref)
        np.testing.assert_array_equal(default.read(ref), small.read(ref))
        self.assertEqual(small.source_identity, default.source_identity)

    def test_invalid_expert_row_indices_and_forged_references_are_rejected(self):
        packed = np.zeros((2, 3, 32), dtype=np.float32)
        path = self.write(tensors={"packed": (packed, None), "matrix": (packed[0], None)})
        store = self.store([path])
        for kwargs in ({}, {"expert": -1}, {"expert": 2}, {"expert": True},
                       {"expert": 0, "row_start": True}, {"expert": 0, "row_start": -1},
                       {"expert": 0, "row_count": 0}, {"expert": 0, "row_count": 4}):
            with self.subTest(kwargs=kwargs), self.assertRaises(ValueError):
                store.matrix_ref("packed", **kwargs)
        ref = store.matrix_ref("matrix", row_count=1)
        for forged in (replace(ref, source_identity="0" * 64), replace(ref, qtype="F16"),
                       replace(ref, shape=(1, 16))):
            with self.assertRaises(ValueError):
                store.read(forged)
        for indices in ([True], [-1], [3], [0.5], np.array([[1]]), np.array([True])):
            with self.subTest(indices=str(indices)), self.assertRaises(ValueError):
                store.gather_rows("matrix", indices)

    def mutate_weight(self, path, offset):
        with path.open("r+b") as stream:
            stream.seek(offset)
            stream.write(np.array([123.0], dtype=np.float32).tobytes())
            stream.flush()
            os.fsync(stream.fileno())

    def test_changed_stat_identity_is_rejected_even_if_original_mtime_is_restored(self):
        path = self.write()
        store = self.store([path])
        ref = store.matrix_ref("matrix", row_count=1)
        store.read(ref)
        before = path.stat()
        offset = store._tensors["matrix"][1].data_offset
        self.mutate_weight(path, offset)
        os.utime(path, ns=(before.st_atime_ns, before.st_mtime_ns))
        with self.assertRaisesRegex(ValueError, "identity changed"):
            store.read(ref)

    def test_slice_fingerprint_independently_rejects_changed_bytes_on_reuse(self):
        path = self.write()
        store = self.store([path])
        ref = store.matrix_ref("matrix", row_count=1)
        store.read(ref)
        self.mutate_weight(path, store._tensors["matrix"][1].data_offset)
        # Fault-inject missed stat observation to exercise the separate content pin.
        with patch.object(store, "_check_shard", return_value=None):
            with self.assertRaisesRegex(ValueError, "slice bytes changed"):
                store.read(ref)
        self.assertEqual(store.stats["decode_count"], 1)

    def test_duplicate_tensor_names_wrong_architecture_and_wrong_shard_size_fail(self):
        first, second = self.write("one.gguf"), self.write("two.gguf")
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            GGUFWeightStore([first, second], expected_shards=self.pins([first, second]))
        wrong = self.write("wrong.gguf", arch="llama")
        with self.assertRaisesRegex(ValueError, "qwen4exp"):
            GGUFWeightStore([wrong], expected_shards=self.pins([wrong]))
        pins = self.pins([first])
        pins[first.name]["size_bytes"] += 1
        with self.assertRaisesRegex(ValueError, "size differs"):
            GGUFWeightStore([first], expected_shards=pins)

    def test_external_attestation_label_and_int64_structural_metadata_are_exact(self):
        integers = [2**53 + 1, 23703573157769]
        def metadata(writer):
            writer.add_key_value("qwen4exp.ple.layer_multipliers", integers,
                                 GGUFValueType.ARRAY, sub_type=GGUFValueType.INT64)
        path = self.write(metadata=metadata)
        store = self.store([path])
        identity = store.identity
        self.assertEqual(identity["verification"], "external_sha256_attestation_not_rehashed")
        self.assertIs(identity["fresh_full_file_hash_verified"], False)
        self.assertEqual(identity["metadata"]["qwen4exp.ple.layer_multipliers"], integers)
        self.assertEqual(identity["shards"][0]["attested_sha256"], self.pins([path])[path.name]["sha256"])
        identity["metadata"]["qwen4exp.ple.layer_multipliers"][0] = 0
        self.assertEqual(store.identity["metadata"]["qwen4exp.ple.layer_multipliers"], integers)

    def test_symlinks_are_refused_and_closing_one_store_does_not_close_another(self):
        path = self.write()
        alias = self.root / "alias.gguf"
        alias.symlink_to(path)
        pins = {alias.name: self.pins([path])[path.name]}
        with self.assertRaises(ValueError):
            GGUFWeightStore([alias], expected_shards=pins)
        first, second = self.store([path]), self.store([path])
        ref = second.matrix_ref("matrix", row_count=1)
        first.close()
        first.close()
        self.assertEqual(second.read(ref).shape, (1, 32))
        with self.assertRaisesRegex(ValueError, "closed"):
            first.read(ref)

    def test_unsupported_weight_type_is_not_silently_downcast(self):
        store = self.store([self.write(tensors={"double": (np.ones((2, 8), dtype=np.float64), None)})])
        with self.assertRaisesRegex(ValueError, "not qualified"):
            store.read_tensor("double")


if __name__ == "__main__":
    unittest.main()
