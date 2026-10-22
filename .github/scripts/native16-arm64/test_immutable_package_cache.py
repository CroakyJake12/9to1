import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("immutable_package_cache", Path(__file__).with_name("immutable-package-cache.py"))
cache = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cache)


class ImmutablePackageReuseTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.package = self.root / "test-only.deb"
        self.package.write_bytes(b"unit fixture; not an installable package")
        self.environment = {"GITHUB_SHA": "1" * 40, "NATIVE_DOTNET_VERSION": "10.0.301",
                            "PIPELINE_ENVIRONMENT_SHA": "2" * 64, "GITHUB_RUN_ID": "123",
                            "GITHUB_ENV": str(self.root / "environment")}
        (self.root / "package-receipt.json").write_text(json.dumps({
            "source_head": "1" * 40, "architecture": "arm64", "bytes": self.package.stat().st_size,
            "sha256": hashlib.sha256(self.package.read_bytes()).hexdigest()}))

    def test_exact_inputs_reuse_exact_package_without_claiming_acceptance(self):
        cache.operate(self.root, "record", self.environment)
        restored = cache.operate(self.root, "restore", self.environment)
        self.assertFalse(restored["release_ready"])
        self.assertEqual(restored["native_application_acceptance"], "NOT RUN")
        self.assertIn("PREVIEW_DEB=", (self.root / "environment").read_text())

    def test_different_revision_environment_or_sdk_prevents_reuse(self):
        cache.operate(self.root, "record", self.environment)
        for key, value in [("GITHUB_SHA", "3" * 40), ("NATIVE_DOTNET_VERSION", "different SDK"),
                           ("PIPELINE_ENVIRONMENT_SHA", "4" * 64)]:
            with self.subTest(key=key), self.assertRaises(ValueError):
                cache.operate(self.root, "restore", {**self.environment, key: value})

    def test_corrupt_package_is_rejected_without_rewriting_receipt(self):
        cache.operate(self.root, "record", self.environment)
        self.package.write_bytes(b"changed")
        with self.assertRaises(ValueError):
            cache.operate(self.root, "restore", self.environment)
        with self.assertRaises(ValueError):
            cache.operate(self.root, "record", self.environment)


if __name__ == "__main__":
    unittest.main()
