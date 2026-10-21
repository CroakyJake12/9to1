from __future__ import annotations

import json
import pathlib
import struct
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2]))

from bundle.formats import elf_machine_matches, sniff_format  # noqa: E402
from bundle.identity import IdentityError  # noqa: E402
from bundle.tests.helpers import (  # noqa: E402
    build_gguf,
    make_elf,
    make_elf_with_program_table,
    make_gguf,
    make_gguf_high_offset,
    make_gguf_probe_tensor,
    make_gguf_single_tensor,
    make_safetensors,
    make_safetensors_out_of_bounds,
    make_truncated_elf,
    make_truncated_gguf,
)


class FormatsTests(unittest.TestCase):
    def _file(self, data: bytes) -> pathlib.Path:
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        path = pathlib.Path(tmp.name) / "asset"
        path.write_bytes(data)
        return path

    def test_gguf_versions_2_and_3(self) -> None:
        meta2 = sniff_format(self._file(make_gguf(2)), "gguf")
        meta3 = sniff_format(self._file(make_gguf(3)), "gguf")
        self.assertEqual("gguf", meta2["format"])
        self.assertEqual(2, meta2["ggufVersion"])
        self.assertTrue(meta2["structurallyComplete"])
        self.assertEqual("gguf", meta3["format"])
        self.assertEqual(3, meta3["ggufVersion"])
        self.assertTrue(meta3["structurallyComplete"])

    def test_gguf_rejects_bad_magic(self) -> None:
        with self.assertRaises(IdentityError):
            sniff_format(self._file(b"NOTGGUFxx"), "gguf")

    def test_gguf_rejects_truncated_header(self) -> None:
        with self.assertRaises(IdentityError):
            sniff_format(self._file(b"GGUF\x01"), "gguf")

    def test_gguf_rejects_unsupported_version(self) -> None:
        with self.assertRaises(IdentityError):
            sniff_format(self._file(make_gguf(9)), "gguf")

    def test_safetensors_valid(self) -> None:
        meta = sniff_format(self._file(make_safetensors(2)), "safetensors")
        self.assertEqual("safetensors", meta["format"])
        self.assertEqual(2, meta["tensors"])

    def test_safetensors_rejects_malformed_tensor(self) -> None:
        data = make_safetensors()
        # corrupt header to be non-JSON
        corrupted = data[:8] + b"{not-json"
        with self.assertRaises(IdentityError):
            sniff_format(self._file(corrupted), "safetensors")

    def test_elf_x86_64(self) -> None:
        meta = sniff_format(self._file(make_elf(62)), "elf")
        self.assertEqual(62, meta["machine"])
        self.assertTrue(elf_machine_matches(meta, "x86_64"))

    def test_elf_machine_mismatch(self) -> None:
        meta = sniff_format(self._file(make_elf(183)), "elf")
        self.assertFalse(elf_machine_matches(meta, "x86_64"))
        self.assertTrue(elf_machine_matches(meta, "aarch64"))

    def test_opaque_bypasses_format_probe(self) -> None:
        meta = sniff_format(self._file(b"anything"), "opaque")
        self.assertEqual("opaque", meta["format"])
        self.assertFalse(meta["structurallyComplete"])

    def test_unknown_declared_format_is_rejected(self) -> None:
        with self.assertRaises(IdentityError):
            sniff_format(self._file(b"x"), "does-not-exist")

    def test_truncated_gguf_is_rejected(self) -> None:
        # The 12-byte header-truncated GGUF must NOT be admitted as a real
        # model asset; the primary header/tensor table cannot be read.
        with self.assertRaises(IdentityError):
            sniff_format(self._file(make_truncated_gguf()), "gguf")

    def test_truncated_elf_is_rejected(self) -> None:
        # A 20-byte file is below the required 64-byte ELF64 header.
        with self.assertRaises(IdentityError):
            sniff_format(self._file(make_truncated_elf()), "elf")

    def test_safetensors_out_of_bounds_offsets_rejected(self) -> None:
        with self.assertRaises(IdentityError):
            sniff_format(self._file(make_safetensors_out_of_bounds()), "safetensors")

    def test_safetensors_no_tensors_rejected(self) -> None:
        # A structurally parseable header with zero tensors is not a usable
        # model asset.
        import json as _json
        import struct as _struct

        header = _json.dumps({"__metadata__": {"format": "pt"}}, separators=(",", ":")).encode()
        data = _struct.pack("<Q", len(header)) + header
        with self.assertRaises(IdentityError):
            sniff_format(self._file(data), "safetensors")

    # --- Pass 3 causal regressions from the independent probe ---

    def test_gguf_one_tensor_valid_u64_dims_with_payload(self) -> None:
        meta = sniff_format(self._file(make_gguf_single_tensor(dims=(1,))), "gguf")
        self.assertTrue(meta["structurallyComplete"])
        self.assertEqual(1, meta["tensorCount"])
        self.assertEqual(3, meta["ggufVersion"])

    def test_gguf_high_offset_no_payload_rejected(self) -> None:
        # The 57-byte rank-1 uint64-dimension + 4GiB offset probe must be
        # rejected despite the (previously misaligned) parser reading offset 0.
        with self.assertRaises(IdentityError):
            sniff_format(self._file(make_gguf_high_offset()), "gguf")

    def test_gguf_unknown_ggml_type_rejected(self) -> None:
        # Unknown ggml tensor type cannot be declared structurally complete.
        import struct as _struct

        data = make_gguf_single_tensor(dims=(1,), ggml_type=99, data=b"\x00" * 4)
        with self.assertRaises(IdentityError):
            sniff_format(self._file(data), "gguf")

    def test_elf_program_header_table_out_of_bounds_rejected(self) -> None:
        # 64-byte header declaring a 56-byte program header at offset 64 with no
        # table present must be rejected.
        bad = make_elf_with_program_table(phoff=64, phentsize=56, phnum=1, append_table=False)
        self.assertEqual(64, len(bad))
        with self.assertRaises(IdentityError):
            sniff_format(self._file(bad), "elf")

    def test_elf_valid_nonempty_program_table_passes(self) -> None:
        # A header plus an actual appended 56-byte program header is bounded and
        # structurally valid.
        good = make_elf_with_program_table(phoff=64, phentsize=56, phnum=1)
        self.assertEqual(64 + 56, len(good))
        meta = sniff_format(self._file(good), "elf")
        self.assertTrue(meta["structurallyComplete"])
        self.assertEqual(62, meta["machine"])

    def test_safetensors_declared_extent_truncated_rejected(self) -> None:
        # F32 shape[1024] => 4096-byte extent, but offsets [0,1] with a 1-byte
        # payload. This must be rejected as truncated/contradictory.
        import json as _json

        h = _json.dumps({"x": {"dtype": "F32", "shape": [1024], "data_offsets": [0, 1]}}, separators=(",", ":")).encode()
        data = struct.pack("<Q", len(h)) + h + b"x"
        self.assertEqual(66, len(data))
        with self.assertRaises(IdentityError):
            sniff_format(self._file(data), "safetensors")

    def test_safetensors_valid_scalar_empty_shape_passes(self) -> None:
        # A legitimate scalar tensor has shape [] (1 element) and a matching
        # 4-byte F32 extent. It must NOT be rejected.
        import json as _json

        h = _json.dumps({"s": {"dtype": "F32", "shape": [], "data_offsets": [0, 4]}}, separators=(",", ":")).encode()
        data = struct.pack("<Q", len(h)) + h + b"\x00" * 4
        meta = sniff_format(self._file(data), "safetensors")
        self.assertTrue(meta["structurallyComplete"])
        self.assertEqual(1, meta["tensors"])

    def test_safetensors_zero_size_tensor_passes(self) -> None:
        # shape[0] is a legitimate empty tensor with extent 0 (start == end).
        import json as _json

        h = _json.dumps({"z": {"dtype": "F32", "shape": [0], "data_offsets": [0, 0]}}, separators=(",", ":")).encode()
        data = struct.pack("<Q", len(h)) + h
        meta = sniff_format(self._file(data), "safetensors")
        self.assertTrue(meta["structurallyComplete"])

    def test_safetensors_unknown_dtype_rejected(self) -> None:
        # Unknown dtype must not receive structurallyComplete:true.
        import json as _json

        h = _json.dumps({"q": {"dtype": "Q8_0", "shape": [16], "data_offsets": [0, 16]}}, separators=(",", ":")).encode()
        data = struct.pack("<Q", len(h)) + h + b"\x00" * 16
        with self.assertRaises(IdentityError):
            sniff_format(self._file(data), "safetensors")

    # --- Pass 4 causal regressions from the additional structural probe ---
    #
    # GGUF alignment (default 32, general.alignment override):
    # - default32 valid positive (table65 + padding31 + data4)
    # - exact truncated negative (padding7 + data4)  -- the reproduced FAIL
    # - supported explicit-alignment positive (general.alignment=64)
    # - invalid metadata/alignment negative (zero / wrong type)

    def test_gguf_default32_alignment_positive(self) -> None:
        # 65-byte table + 31 pad bytes + 4 data bytes = 100 bytes. The data
        # section starts at align32(65)=96, so the 4-byte payload [96,100)
        # fits exactly.
        data = make_gguf_probe_tensor(padding=31)
        self.assertEqual(100, len(data))
        meta = sniff_format(self._file(data), "gguf")
        self.assertTrue(meta["structurallyComplete"])
        self.assertEqual(32, meta["dataAlignment"])

    def test_gguf_default32_alignment_truncated_rejected(self) -> None:
        # The reproduced FAIL: 65-byte table + padding7 + data4 = 76 bytes. The
        # default 32-byte alignment demands data start 96, so the extent lies
        # beyond EOF and must be rejected.
        data = make_gguf_probe_tensor(padding=7)
        self.assertEqual(76, len(data))
        with self.assertRaises(IdentityError):
            sniff_format(self._file(data), "gguf")

    def test_gguf_explicit_alignment_positive(self) -> None:
        # Supported explicit `general.alignment` = 64 (uint32) with a single F32
        # tensor whose data region is aligned to 64. Must pass.
        data = build_gguf(
            metadata_items=[("general.alignment", 4, struct.pack("<I", 64))],
            tensors=[("x", (1,), 0, 0, b"\x00" * 4)],
            alignment=64,
        )
        meta = sniff_format(self._file(data), "gguf")
        self.assertTrue(meta["structurallyComplete"])
        self.assertEqual(64, meta["dataAlignment"])
        self.assertEqual(1, meta["tensorCount"])

    def test_gguf_invalid_alignment_zero_rejected(self) -> None:
        # general.alignment = 0 is not a supported alignment; fail closed.
        data = build_gguf(
            metadata_items=[("general.alignment", 4, struct.pack("<I", 0))],
            tensors=[("x", (1,), 0, 0, b"\x00" * 4)],
            alignment=32,
        )
        with self.assertRaises(IdentityError):
            sniff_format(self._file(data), "gguf")

    def test_gguf_invalid_alignment_wrong_type_rejected(self) -> None:
        # general.alignment typed as a string is an unsupported declaration.
        data = build_gguf(
            metadata_items=[("general.alignment", 8, _gguf_str_for_test("64"))],
            tensors=[("x", (1,), 0, 0, b"\x00" * 4)],
            alignment=32,
        )
        with self.assertRaises(IdentityError):
            sniff_format(self._file(data), "gguf")

    def test_gguf_unknown_metadata_value_type_rejected(self) -> None:
        # An unknown metadata value type must fail closed even if no tensor data
        # depends on it.
        data = build_gguf(
            metadata_items=[("some.key", 99, b"\x00\x00\x00\x00")],
            tensors=[("x", (1,), 0, 0, b"\x00" * 4)],
            alignment=32,
        )
        with self.assertRaises(IdentityError):
            sniff_format(self._file(data), "gguf")

    # Safetensors exact extent / non-overlapping complete coverage:
    # - overlapping nonempty slices rejected (reproduced FAIL)
    # - extent-mismatch (contradictory padding) rejected (reproduced FAIL)
    # - valid adjacent two-tensor positive
    # - scalar / zero-size tensors retained
    # - holes / trailing uncovered buffer / bool offsets rejected

    @staticmethod
    def _safetensors(obj: dict, payload: bytes) -> bytes:
        h = json.dumps(obj, separators=(",", ":")).encode()
        return struct.pack("<Q", len(h)) + h + payload

    def test_safetensors_overlapping_regions_rejected(self) -> None:
        # Two F32[1] tensors both declaring [0,4] in a 4-byte buffer overlap.
        obj = {
            "x": {"dtype": "F32", "shape": [1], "data_offsets": [0, 4]},
            "y": {"dtype": "F32", "shape": [1], "data_offsets": [0, 4]},
        }
        with self.assertRaises(IdentityError):
            sniff_format(self._file(self._safetensors(obj, b"\x00" * 4)), "safetensors")

    def test_safetensors_extent_mismatch_rejected(self) -> None:
        # F32[1] extent is 4, but the declared slice [0,8] is 8 bytes
        # (contradictory padding). Must be rejected.
        obj = {"x": {"dtype": "F32", "shape": [1], "data_offsets": [0, 8]}}
        with self.assertRaises(IdentityError):
            sniff_format(self._file(self._safetensors(obj, b"\x00" * 8)), "safetensors")

    def test_safetensors_valid_adjacent_two_tensor_positive(self) -> None:
        # Two F32[1] tensors adjacent in an 8-byte buffer, covering it exactly.
        obj = {
            "a": {"dtype": "F32", "shape": [1], "data_offsets": [0, 4]},
            "b": {"dtype": "F32", "shape": [1], "data_offsets": [4, 8]},
        }
        meta = sniff_format(self._file(self._safetensors(obj, b"\x00" * 8)), "safetensors")
        self.assertTrue(meta["structurallyComplete"])
        self.assertEqual(2, meta["tensors"])

    def test_safetensors_gap_rejected(self) -> None:
        # Two tensors with an uncovered gap [4,8) between them.
        obj = {
            "a": {"dtype": "F32", "shape": [1], "data_offsets": [0, 4]},
            "b": {"dtype": "F32", "shape": [1], "data_offsets": [12, 16]},
        }
        with self.assertRaises(IdentityError):
            sniff_format(self._file(self._safetensors(obj, b"\x00" * 16)), "safetensors")

    def test_safetensors_trailing_uncovered_rejected(self) -> None:
        # A single tensor leaving trailing buffer bytes uncovered (a hole).
        obj = {"x": {"dtype": "F32", "shape": [1], "data_offsets": [0, 4]}}
        with self.assertRaises(IdentityError):
            sniff_format(self._file(self._safetensors(obj, b"\x00" * 8)), "safetensors")

    def test_safetensors_bool_offset_rejected(self) -> None:
        # Python bools must not count as valid offset integers.
        obj = {"x": {"dtype": "F32", "shape": [1], "data_offsets": [False, 4]}}
        with self.assertRaises(IdentityError):
            sniff_format(self._file(self._safetensors(obj, b"\x00" * 4)), "safetensors")

    # --- Bundle15 pass5: type-bounds + alignment-type + __metadata__ causal tests ---
    #
    # GGUF ggml type tags (verified against upstream ggml.h):
    # - Q6_K = 14, block 256 elements / 210 bytes; valid block, truncated 209-byte
    #   block, and invalid dim-1 (not divisible) tiny file.
    # - general.alignment must be exactly UINT32 (type 4); int64/other type tags
    #   are rejected before any admission.
    # Safetensors __metadata__ must be string -> string; impostors rejected.

    def test_gguf_q6k_valid_block_passes(self) -> None:
        # Q6_K shape[256] => exactly 1 block => 210 bytes, aligned to 32.
        data = build_gguf(
            tensors=[("w", (256,), 14, 0, b"\x00" * 210)],
            alignment=32,
        )
        meta = sniff_format(self._file(data), "gguf")
        self.assertTrue(meta["structurallyComplete"])
        self.assertEqual(1, meta["tensorCount"])

    def test_gguf_q6k_truncated_block_rejected(self) -> None:
        # Same block extent (210 bytes) but only 209 bytes materialised => beyond
        # EOF and rejected.
        data = build_gguf(
            tensors=[("w", (256,), 14, 0, b"\x00" * 209)],
            alignment=32,
        )
        with self.assertRaises(IdentityError):
            sniff_format(self._file(data), "gguf")

    def test_gguf_q6k_invalid_dim1_rejected(self) -> None:
        # Q6_K shape[1] is not divisible by the 256-element block => rejected.
        data = build_gguf(
            tensors=[("w", (1,), 14, 0, b"\x00")],
            alignment=32,
        )
        with self.assertRaises(IdentityError):
            sniff_format(self._file(data), "gguf")

    def test_gguf_q6k_invalid_row_128_2_rejected(self) -> None:
        # total 256 but row (ne[0]) 128 is not divisible by 256 => must be
        # rejected (ggml blocks run along the innermost dimension).
        data = build_gguf(
            tensors=[("w", (128, 2), 14, 0, b"\x00" * 210)],
            alignment=32,
        )
        with self.assertRaises(IdentityError):
            sniff_format(self._file(data), "gguf")

    def test_gguf_q6k_valid_row_256_1_passes(self) -> None:
        # row 256 is divisible by 256; [256,1] is a legitimate Q6_K shape.
        data = build_gguf(
            tensors=[("w", (256, 1), 14, 0, b"\x00" * 210)],
            alignment=32,
        )
        meta = sniff_format(self._file(data), "gguf")
        self.assertTrue(meta["structurallyComplete"])
        self.assertEqual(1, meta["tensorCount"])

    def test_gguf_alignment_wrong_metadata_type_int64_rejected(self) -> None:
        # general.alignment typed INT64 (tag 11) must be rejected even though the
        # decoded value is a positive int: the contract requires UINT32.
        data = build_gguf(
            metadata_items=[("general.alignment", 11, struct.pack("<q", 64))],
            tensors=[("x", (1,), 0, 0, b"\x00" * 4)],
            alignment=32,
        )
        with self.assertRaises(IdentityError):
            sniff_format(self._file(data), "gguf")

    def test_safetensors_nested_metadata_impostor_rejected(self) -> None:
        obj = {
            "__metadata__": {"format": {"nested": True}},
            "x": {"dtype": "F32", "shape": [1], "data_offsets": [0, 4]},
        }
        with self.assertRaises(IdentityError):
            sniff_format(self._file(self._safetensors(obj, b"\x00" * 4)), "safetensors")

    def test_safetensors_metadata_scalar_impostor_rejected(self) -> None:
        obj = {
            "__metadata__": "not-an-object",
            "x": {"dtype": "F32", "shape": [1], "data_offsets": [0, 4]},
        }
        with self.assertRaises(IdentityError):
            sniff_format(self._file(self._safetensors(obj, b"\x00" * 4)), "safetensors")

    def test_safetensors_valid_string_metadata_passes(self) -> None:
        obj = {
            "__metadata__": {"format": "pt", "foo": "bar"},
            "x": {"dtype": "F32", "shape": [1], "data_offsets": [0, 4]},
        }
        meta = sniff_format(self._file(self._safetensors(obj, b"\x00" * 4)), "safetensors")
        self.assertTrue(meta["structurallyComplete"])

    def test_safetensors_unhashable_dtype_rejected_as_identity_error(self) -> None:
        # A list/dict dtype must raise a typed IdentityError, never a TypeError.
        obj = {"x": {"dtype": ["F32"], "shape": [1], "data_offsets": [0, 4]}}
        with self.assertRaises(IdentityError):
            sniff_format(self._file(self._safetensors(obj, b"\x00" * 4)), "safetensors")
        obj2 = {"x": {"dtype": {"kind": "F32"}, "shape": [1], "data_offsets": [0, 4]}}
        with self.assertRaises(IdentityError):
            sniff_format(self._file(self._safetensors(obj2, b"\x00" * 4)), "safetensors")


    def test_gguf_rejects_non_power_of_two_uint32_alignment(self) -> None:
        # Same real UINT32 metadata and a physically present aligned F32 payload:
        # rejection must be caused by the upstream power-of-two contract.
        data = build_gguf(
            metadata_items=[("general.alignment", 4, struct.pack("<I", 3))],
            tensors=[("x", (1,), 0, 0, b"\x00" * 4)], alignment=3,
        )
        with self.assertRaises(IdentityError):
            sniff_format(self._file(data), "gguf")

    def test_gguf_preserves_upstream_positive_alignment_values(self) -> None:
        for alignment in (1, 2, 32, 2097152):
            with self.subTest(alignment=alignment):
                data = build_gguf(
                    metadata_items=[("general.alignment", 4, struct.pack("<I", alignment))],
                    tensors=[("x", (1,), 0, 0, b"\x00" * 4)], alignment=alignment,
                )
                result = sniff_format(self._file(data), "gguf")
                self.assertTrue(result["structurallyComplete"])
                self.assertEqual(alignment, result["dataAlignment"])

    def test_gguf_rejects_rank_beyond_official_ggml_limit(self) -> None:
        data = build_gguf(tensors=[("x", (1, 1, 1, 1, 1), 0, 0, b"\x00" * 4)])
        with self.assertRaises(IdentityError):
            sniff_format(self._file(data), "gguf")

    def test_gguf_preserves_the_official_rank_four_boundary(self) -> None:
        data = build_gguf(tensors=[("x", (1, 1, 1, 1), 0, 0, b"\x00" * 4)])
        self.assertTrue(sniff_format(self._file(data), "gguf")["structurallyComplete"])


def _gguf_str_for_test(value: str) -> bytes:
    # Small local helper to build a GGUF string metadata value (length + bytes).
    encoded = value.encode("utf-8")
    return struct.pack("<Q", len(encoded)) + encoded


if __name__ == "__main__":
    unittest.main()
