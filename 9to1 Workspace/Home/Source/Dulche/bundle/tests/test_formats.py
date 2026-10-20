from __future__ import annotations

import pathlib
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2]))

from bundle.formats import elf_machine_matches, sniff_format  # noqa: E402
from bundle.identity import IdentityError  # noqa: E402
from bundle.tests.helpers import (  # noqa: E402
    make_elf,
    make_gguf,
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


if __name__ == "__main__":
    unittest.main()
