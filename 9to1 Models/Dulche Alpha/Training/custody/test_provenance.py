import hashlib
from pathlib import Path
import tempfile
import unittest

from custody import CustodyError
from custody.provenance import validate_dataset_manifest, verify_dataset_files


def manifest():
    files = []
    for index, split in enumerate(("train", "validation", "final_held_out")):
        data = f"independent sample {index}".encode()
        digest = hashlib.sha256(data).hexdigest()
        files.append({
            "id": f"file-{index}", "split": split, "path": f"dataset-{index}.txt",
            "sha256": digest, "size_bytes": len(data),
            "lineage": [{
                "role": "examples", "origin": "independent_licensed",
                "producer": "fixture-independent", "evidence": "source-object-id:1",
                "license_ref": "approved-test-license:1",
            }],
            "samples": [{"id": f"sample-{index}", "sha256": digest}],
        })
    return {"schema": 1, "purpose": "real_training", "files": files}


class ProvenanceTests(unittest.TestCase):
    def test_validates_bytes_and_immutable_manifest_in_all_three_splits(self):
        value = manifest()
        record = validate_dataset_manifest(value)
        self.assertEqual(record.sha256, validate_dataset_manifest(record.as_record()).sha256)
        with tempfile.TemporaryDirectory(prefix="dulche-provenance-") as directory:
            root = Path(directory).resolve()
            for index, file in enumerate(record.files):
                (root / file.path).write_bytes(f"independent sample {index}".encode())
            self.assertEqual(len(verify_dataset_files(record, root)), 3)
            (root / record.files[2].path).write_bytes(b"tampered")
            with self.assertRaises(CustodyError):
                verify_dataset_files(record, root)

    def test_denies_openai_and_test_only_lineage_in_every_training_signal(self):
        for role in ("examples", "labels", "rewards", "corrections", "filtering_signals"):
            for origin, producer, model_id in [
                ("openai_generated", "third-party", None),
                ("user_provided", "ChatGPT", None),
                ("local_model", "local-service", "openai/gpt-oss-20b"),
                ("software_test_only", "fixture", None),
            ]:
                with self.subTest(role=role, origin=origin, producer=producer):
                    value = manifest()
                    value["files"][0]["lineage"].append({
                        "role": role, "origin": origin, "producer": producer,
                        "evidence": "recorded-assertion", "model_id": model_id,
                        "license_ref": "supposed-license",
                    })
                    with self.assertRaises(CustodyError):
                        validate_dataset_manifest(value)
        software = manifest()
        software["purpose"] = "software_test_only"
        for file in software["files"]:
            file["lineage"][0]["origin"] = "software_test_only"
        self.assertEqual(validate_dataset_manifest(software, purpose="software_test_only").purpose, "software_test_only")
        with self.assertRaises(CustodyError):
            validate_dataset_manifest(software)

    def test_rejects_duplicates_and_aliases_across_held_out_splits(self):
        for change in ("sample_id", "sample_hash", "file_id", "file_hash", "path", "missing_split", "traversal"):
            with self.subTest(change=change):
                value = manifest()
                first, _, held_out = value["files"]
                if change == "sample_id":
                    held_out["samples"][0]["id"] = first["samples"][0]["id"].upper()
                elif change == "sample_hash":
                    held_out["samples"][0]["sha256"] = first["samples"][0]["sha256"]
                elif change == "file_id":
                    held_out["id"] = first["id"]
                elif change == "file_hash":
                    held_out["sha256"] = first["sha256"]
                elif change == "path":
                    held_out["path"] = first["path"].upper()
                elif change == "missing_split":
                    held_out["split"] = "train"
                else:
                    held_out["path"] = "../unadmitted"
                with self.assertRaises(CustodyError):
                    validate_dataset_manifest(value)
