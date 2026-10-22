"""Byte validation and safe extraction tests; no inference, model or app executes."""
from pathlib import Path
import hashlib
import importlib.util
import json
import tempfile
import unittest
from unittest import mock
import zipfile

spec = importlib.util.spec_from_file_location("inference_cache", Path(__file__).with_name("inference_stage.py"))
stage = importlib.util.module_from_spec(spec)
spec.loader.exec_module(stage)


class VerifiedInputCacheTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def test_model_cache_reuses_exact_bytes_without_network(self):
        cached = self.root / "model.gguf"
        payload = b"unit test input only; not a model"
        cached.write_bytes(payload)
        with mock.patch.object(stage.urllib.request, "urlopen", side_effect=AssertionError("network forbidden")):
            self.assertEqual(stage.cached_input(self.root, cached.name, "https://example.invalid/model", hashlib.sha256(payload).hexdigest(), len(payload)), cached)

    def test_bad_cached_model_is_not_used_or_silently_downloaded_over(self):
        cached = self.root / "model.gguf"
        cached.write_bytes(b"bad input")
        with mock.patch.object(stage.urllib.request, "urlopen", side_effect=AssertionError("network forbidden")):
            with self.assertRaises(ValueError):
                stage.cached_input(self.root, cached.name, "https://example.invalid/model", "0" * 64, 1)
        self.assertEqual(cached.read_bytes(), b"bad input")

    def test_symlink_cache_root_or_file_is_refused(self):
        (self.root / "real").mkdir()
        (self.root / "linked").symlink_to(self.root / "real", target_is_directory=True)
        with self.assertRaises(ValueError):
            stage.cached_input(self.root / "linked", "model", "https://example.invalid/model", "0" * 64, 1)
        (self.root / "real" / "input").write_bytes(b"input")
        (self.root / "model").symlink_to(self.root / "real" / "input")
        with self.assertRaises(ValueError):
            stage.cached_input(self.root, "model", "https://example.invalid/model", "0" * 64, 1)

    def test_provider_cache_validates_pins_before_avoiding_expired_origin_request(self):
        cache = self.root / "provider-cache"
        cache.mkdir()
        payload = b"fake ZIP bytes for unit boundary only"
        (cache / "provider.zip").write_bytes(payload)
        metadata = {"id": stage.ARTIFACT, "name": stage.ARTIFACT_NAME,
                    "digest": "sha256:" + hashlib.sha256(payload).hexdigest(), "expired": False,
                    "workflow_run": {"id": stage.RUN, "head_sha": stage.RUN_HEAD}}
        (cache / "origin-metadata.json").write_text(json.dumps(metadata))
        # Production byte count stays47,849,044. This small unit fixture replaces
        # only verify_file's expected-size boundary, retaining its actual SHA check.
        actual_verify = stage.verify_file
        def verify(path, sha, size=None):
            return actual_verify(path, sha, len(payload) if size == 47849044 else size)
        with mock.patch.object(stage, "PROVIDER_ZIP_SHA", hashlib.sha256(payload).hexdigest()), \
                mock.patch.object(stage, "verify_file", side_effect=verify), \
                mock.patch.object(stage.urllib.request, "urlopen", side_effect=AssertionError("network forbidden")):
            receipt = stage.fetch_metadata(self.root / "metadata.json", self.root / "provider.zip", cache)
            self.assertEqual(receipt["acquisition_source"], "EXACT_BYTE_PINNED_CACHE")
            self.assertIn("NOT CHECKED", receipt["original_artifact_current_availability"])
            (cache / "provider.zip").write_bytes(b"corrupt")
            with self.assertRaises(ValueError):
                stage.fetch_metadata(self.root / "bad.json", self.root / "other.zip", cache)

    def test_zip_traversal_symlink_and_duplicate_entries_rejected_before_output(self):
        for case in ("traversal", "symlink", "duplicate"):
            archive = self.root / (case + ".zip")
            with zipfile.ZipFile(archive, "w") as z:
                if case == "traversal":
                    z.writestr("../escaped", "no")
                elif case == "symlink":
                    member = zipfile.ZipInfo("link")
                    member.external_attr = 0o120777 << 16
                    z.writestr(member, "/outside")
                else:
                    z.writestr("one", "first")
                    with self.assertWarns(UserWarning):
                        z.writestr("one", "second")
            output = self.root / (case + "-output")
            with mock.patch.object(stage, "verify_file", return_value=archive):
                with self.assertRaises(ValueError):
                    stage.extract_provider_zip(archive, output)
            self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
