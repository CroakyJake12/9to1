from __future__ import annotations

import pathlib
import json
import sys
import unittest

RUNTIME = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(RUNTIME))

import manifest_validation  # noqa: E402
from manifest_validation import ManifestValidationError  # noqa: E402


class ManifestValidationTests(unittest.TestCase):
    def test_example_manifest_zero_hash_is_rejected(self) -> None:
        example = RUNTIME / "model-manifest.example.json"
        raw = json.loads(example.read_text(encoding="utf-8"))
        with self.assertRaises(ManifestValidationError):
            manifest_validation.validate_model_manifest(raw, filename_stem="model-manifest.example")

    def test_valid_manifest_passes(self) -> None:
        digest = "a" * 64
        valid = {
            "schemaVersion": 1,
            "id": "smollm-q4",
            "displayName": "SmolLM 2 Q4",
            "sha256": digest,
            "size": 123456,
            "blob": f"{digest[:2]}/{digest}.gguf",
            "license": "Apache-2.0",
            "source": "https://huggingface.co/unsloth/SmolLM2-135M-Instruct-GGUF",
            "ggufVersions": [3],
            "capabilities": ["chat"],
            "redistribution": "review-required",
        }
        result = manifest_validation.validate_model_manifest(valid, filename_stem="smollm-q4")
        self.assertEqual(digest, result["sha256"])

    def test_noncanonical_blob_path_is_rejected(self) -> None:
        digest = "b" * 64
        raw = {
            "id": "m1",
            "displayName": "M1",
            "sha256": digest,
            "size": 100,
            "blob": "wrong/path.gguf",
            "license": "MIT",
            "source": "https://example.invalid",
            "ggufVersions": [2],
        }
        with self.assertRaises(ManifestValidationError):
            manifest_validation.validate_model_manifest(raw, filename_stem="m1")

    def test_placeholder_digest_is_flag(self) -> None:
        self.assertTrue(manifest_validation.is_zero_sha256("0" * 64))
        self.assertFalse(manifest_validation.is_zero_sha256("a" * 64))


if __name__ == "__main__":
    unittest.main()
