from __future__ import annotations

import pathlib
import struct
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2]))

from bundle.formats import elf_machine_matches, sniff_format  # noqa: E402
from bundle.identity import IdentityError  # noqa: E402
from bundle.tests.helpers import (  # noqa: E402
    make_elf,
    make_elf_with_program_table,
    make_gguf,
    make_gguf_high_offset,
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


if __name__ == "__main__":
    unittest.main()
