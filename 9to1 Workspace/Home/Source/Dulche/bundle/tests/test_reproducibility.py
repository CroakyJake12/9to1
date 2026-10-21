from __future__ import annotations

import pathlib
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2]))

from bundle.manifest import canonical_dumps  # noqa: E402
from bundle.packager import pack_bundle  # noqa: E402
from bundle.receipt import receipt_bytes  # noqa: E402
from bundle.spec import parse_spec  # noqa: E402
from bundle.tests.helpers import make_gguf, minimal_spec, write_fixture  # noqa: E402


class ReproducibilityTests(unittest.TestCase):
    def _pack_twice(self, *, timestamp: bool) -> tuple[dict, dict]:
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        root = pathlib.Path(tmp.name)
        assets = root / "assets"
        assets.mkdir(parents=True)
        write_fixture(assets, "fixtures/smollm.gguf", make_gguf())
        spec = parse_spec(minimal_spec())
        first = pack_bundle(spec, assets, include_timestamp=timestamp)
        second = pack_bundle(spec, assets, include_timestamp=timestamp)
        return first, second

    def test_identical_inputs_produce_identical_manifest_bytes(self) -> None:
        first, second = self._pack_twice(timestamp=False)
        self.assertEqual(canonical_dumps(first.manifest), canonical_dumps(second.manifest))
        self.assertNotIn("generatedAtUtc", first.manifest)

    def test_identical_inputs_produce_identical_receipt_bytes(self) -> None:
        first, second = self._pack_twice(timestamp=False)
        self.assertEqual(receipt_bytes(first.receipt), receipt_bytes(second.receipt))

    def test_receipt_is_deterministic_even_with_timestamp_manifest(self) -> None:
        first, second = self._pack_twice(timestamp=True)
        self.assertIn("generatedAtUtc", first.manifest)
        self.assertIn("generatedAtUtc", second.manifest)
        # Receipts remain byte-identical regardless of the manifest timestamp.
        self.assertEqual(receipt_bytes(first.receipt), receipt_bytes(second.receipt))

    def test_manifest_sha256_matches_receipt(self) -> None:
        first, _ = self._pack_twice(timestamp=False)
        from bundle.receipt import manifest_sha256

        self.assertEqual(first.receipt["manifestSha256"], manifest_sha256(first.manifest))


if __name__ == "__main__":
    unittest.main()
