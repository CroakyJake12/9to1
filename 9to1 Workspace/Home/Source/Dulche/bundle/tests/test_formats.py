from __future__ import annotations

import pathlib
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2]))

from bundle.formats import elf_machine_matches, sniff_format  # noqa: E402
from bundle.identity import IdentityError  # noqa: E402
from bundle.tests.helpers import make_elf, make_gguf, make_safetensors  # noqa: E402


class FormatsTests(unittest.TestCase):
    def _file(self, data: bytes) -> pathlib.Path:
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        path = pathlib.Path(tmp.name) / "asset"
        path.write_bytes(data)
        return path

    def test_gguf_versions_2_and_3(self) -> None:
        self.assertEqual({"format": "gguf", "ggufVersion": 2}, sniff_format(self._file(make_gguf(2)), "gguf"))
        self.assertEqual({"format": "gguf", "ggufVersion": 3}, sniff_format(self._file(make_gguf(3)), "gguf"))

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

    def test_unknown_declared_format_is_rejected(self) -> None:
        with self.assertRaises(IdentityError):
            sniff_format(self._file(b"x"), "does-not-exist")


if __name__ == "__main__":
    unittest.main()
