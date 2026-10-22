import copy
import importlib.util
from pathlib import Path
import unittest
import urllib.request

spec = importlib.util.spec_from_file_location("retain_artifacts", Path(__file__).with_name("retain-immutable-artifacts.py"))
retention = importlib.util.module_from_spec(spec)
spec.loader.exec_module(retention)


class RetentionIdentityTests(unittest.TestCase):
    def metadata(self):
        artifact_id = 12393400325
        name, size, sha = retention.ARTIFACTS[artifact_id]
        return {"id": artifact_id, "name": name, "size_in_bytes": size,
                "digest": "sha256:" + sha, "expired": False,
                "workflow_run": {"id": retention.RUN, "head_sha": retention.HEAD}}

    def test_exact_original_metadata_is_accepted(self):
        retention.validate_metadata(self.metadata(), 12393400325)

    def test_wrong_revision_digest_size_run_and_expired_are_rejected(self):
        changes = [("id", 1), ("name", "another package"), ("size_in_bytes", 1),
                   ("digest", "sha256:" + "0" * 64), ("expired", True)]
        for key, value in changes:
            with self.subTest(key=key):
                metadata = self.metadata()
                metadata[key] = value
                with self.assertRaises(ValueError):
                    retention.validate_metadata(metadata, 12393400325)
        for key in ("id", "head_sha"):
            metadata = self.metadata()
            metadata["workflow_run"][key] = "other"
            with self.assertRaises(ValueError):
                retention.validate_metadata(metadata, 12393400325)

    def test_cross_origin_redirect_strips_authorization(self):
        request = urllib.request.Request("https://api.github.com/repos/example", headers={"Authorization": "Bearer test-only"})
        redirected = retention.SafeRedirect().redirect_request(request, None, 302, "Found", {}, "https://artifact.example/archive.zip")
        self.assertIsNone(redirected.get_header("Authorization"))

    def test_insecure_or_url_credential_redirect_is_rejected(self):
        request = urllib.request.Request("https://api.github.com/repos/example")
        for target in ["http://artifact.example/a", "https://user:secret@artifact.example/a"]:
            with self.subTest(target=target), self.assertRaises(ValueError):
                retention.SafeRedirect().redirect_request(request, None, 302, "Found", {}, target)


if __name__ == "__main__":
    unittest.main()
