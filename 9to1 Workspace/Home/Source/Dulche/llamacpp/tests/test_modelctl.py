from __future__ import annotations

import hashlib
import pathlib
import stat
import struct
import sys
import tempfile
import unittest

RUNTIME = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(RUNTIME))

import modelctl  # noqa: E402
from model_lease import acquire_model_lease  # noqa: E402


class ModelManagerTests(unittest.TestCase):
    def _source(self, root: pathlib.Path, payload: bytes = b"payload") -> pathlib.Path:
        path = root / "source.gguf"
        path.write_bytes(b"GGUF" + struct.pack("<I", 3) + payload)
        return path

    def _manager(self, root: pathlib.Path) -> modelctl.ModelManager:
        return modelctl.ModelManager(root / "models", root / "runtime")

    def test_import_creates_canonical_private_blob_and_manifest(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            source = self._source(root)
            manager = self._manager(root)
            manifest = manager.import_model(
                source,
                model_id="model-one",
                display_name="Model One",
                license_id="MIT",
                source_reference="local-test",
            )
            digest = hashlib.sha256(source.read_bytes()).hexdigest()
            expected = root / "models/blobs/sha256" / digest[:2] / f"{digest}.gguf"
            self.assertEqual(digest, manifest["sha256"])
            self.assertEqual(f"{digest[:2]}/{digest}.gguf", manifest["blob"])
            self.assertTrue(expected.is_file())
            self.assertEqual(0o600, stat.S_IMODE(expected.stat().st_mode))
            manifest_path = root / "models/manifests/model-one.json"
            self.assertEqual(0o600, stat.S_IMODE(manifest_path.stat().st_mode))
            self.assertTrue(source.is_file())
            listed = manager.list_models(verify=True)
            self.assertEqual("llamacpp:model-one", listed[0]["key"])
            self.assertTrue(listed[0]["verified"])

    def test_invalid_gguf_is_never_adopted(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            source = root / "bad.gguf"
            source.write_bytes(b"not-a-gguf")
            manager = self._manager(root)
            with self.assertRaises(modelctl.ModelManagerError):
                manager.import_model(
                    source,
                    model_id="bad-model",
                    display_name="Bad",
                    license_id="MIT",
                    source_reference="local-test",
                )
            self.assertFalse((root / "models/manifests/bad-model.json").exists())
            staging = root / "models/.staging"
            self.assertEqual([], list(staging.iterdir()))

    def test_symlink_source_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            source = self._source(root)
            link = root / "linked.gguf"
            link.symlink_to(source)
            manager = self._manager(root)
            with self.assertRaises(modelctl.ModelManagerError):
                manager.import_model(
                    link,
                    model_id="linked-model",
                    display_name="Linked",
                    license_id="MIT",
                    source_reference="local-test",
                )

    def test_duplicate_requires_explicit_replace_and_old_blob_is_retained(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            manager = self._manager(root)
            first = self._source(root, b"first")
            original = manager.import_model(first, model_id="same", display_name="One", license_id="MIT", source_reference="one")
            with self.assertRaises(modelctl.ModelManagerError):
                manager.import_model(first, model_id="same", display_name="One", license_id="MIT", source_reference="one")
            second = root / "second.gguf"
            second.write_bytes(b"GGUF" + struct.pack("<I", 3) + b"second")
            replacement = manager.import_model(
                second,
                model_id="same",
                display_name="Two",
                license_id="MIT",
                source_reference="two",
                replace=True,
            )
            self.assertNotEqual(original["sha256"], replacement["sha256"])
            old_blob = root / "models/blobs/sha256" / original["blob"]
            self.assertTrue(old_blob.is_file(), "replace must not implicitly purge rollback data")

    def test_delete_refuses_model_held_by_shared_runtime_lease(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            manager = self._manager(root)
            source = self._source(root)
            manager.import_model(source, model_id="busy", display_name="Busy", license_id="MIT", source_reference="local")
            lease = acquire_model_lease(root / "runtime", "busy", exclusive=False, blocking=False)
            try:
                with self.assertRaises(modelctl.ModelManagerError):
                    manager.delete_model("busy", purge_blob=True)
            finally:
                lease.release()

    def test_shared_blob_is_purged_only_after_last_manifest(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            manager = self._manager(root)
            source = self._source(root)
            first = manager.import_model(source, model_id="first", display_name="First", license_id="MIT", source_reference="local")
            manager.import_model(source, model_id="second", display_name="Second", license_id="MIT", source_reference="local")
            blob = root / "models/blobs/sha256" / first["blob"]
            result_one = manager.delete_model("first", purge_blob=True)
            self.assertFalse(result_one["blobRemoved"])
            self.assertTrue(blob.is_file())
            result_two = manager.delete_model("second", purge_blob=True)
            self.assertTrue(result_two["blobRemoved"])
            self.assertFalse(blob.exists())


if __name__ == "__main__":
    unittest.main()
