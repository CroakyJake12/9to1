#!/usr/bin/env python3
"""Real stdlib file/ZIP refusal controls; synthetic anonymous fixture, no runtime."""
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import stat
import subprocess
import sys
import tempfile
import unittest
import zipfile

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("sealed_public_transport", HERE / "split-sealed-publication.py")
transport = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(transport)
COMMIT = "1" * 40


def encoded(value):
    return (json.dumps(value, indent=2) + "\n").encode()


class SealedTransportControls(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="sealed-public-unit-")
        self.root = Path(self.temporary.name).resolve()
        self.publication = self.root / "publication"
        self.publication.mkdir()
        self.output = self.root / "parts"
        self.fixture()

    def tearDown(self):
        self.temporary.cleanup()

    def fixture(self, name="payload.bin", payload=None):
        payload = bytes(range(256)) * 2048 if payload is None else payload
        entries = [("index.html", b"Anonymous transport UNIT fixture"), (name, payload)]
        with zipfile.ZipFile(self.publication / "wwwroot.zip", "w", compression=zipfile.ZIP_STORED) as archive:
            for path, data in entries:
                archive.writestr(path, data)
        self.receipt = {"sourceCommit": COMMIT, "sourceCommitAfter": COMMIT, "exitCode": 0,
                        "fileCount": len(entries), "totalBytes": sum(len(data) for _, data in entries),
                        "publishFiles": [{"path": path, "bytes": len(data), "sha256": transport.sha_bytes(data)}
                                         for path, data in entries]}
        self.seal = {"sourceCommit": COMMIT, "zipBytes": (self.publication / "wwwroot.zip").stat().st_size,
                     "zipSha256": transport.digest(self.publication / "wwwroot.zip"),
                     "fileCount": self.receipt["fileCount"], "totalBytes": self.receipt["totalBytes"]}
        self.write_metadata()

    def write_metadata(self):
        raw = encoded(self.receipt)
        (self.publication / "publish-manifest.json").write_bytes(raw)
        self.seal["receiptSha256"] = transport.sha_bytes(raw)
        (self.publication / "seal.json").write_bytes(encoded(self.seal))

    def split(self, part_bytes=300000):
        return transport.split_publication(self.publication, self.output, COMMIT, part_bytes)

    def refused(self, **kwargs):
        with self.assertRaises((ValueError, zipfile.BadZipFile, FileNotFoundError)):
            transport.split_publication(self.publication, self.output, kwargs.get("commit", COMMIT),
                                        kwargs.get("part_bytes", 300000))
        self.assertFalse(self.output.exists())

    def rewrite_index(self, index):
        raw = encoded(index)
        for folder in self.output.iterdir():
            (folder / "parts-manifest.json").write_bytes(raw)
        return transport.sha_bytes(raw)

    def test_exact_two_part_reassembly_and_original_metadata_unchanged(self):
        before = {path.name: path.read_bytes() for path in self.publication.iterdir()}
        index, sha = self.split()
        self.assertEqual(len(index["parts"]), 2)
        self.assertEqual(transport.verify_parts(self.output, COMMIT, sha), index)
        joined = b"".join((self.output / row["path"]).read_bytes() for row in index["parts"])
        self.assertEqual(joined, before["wwwroot.zip"])
        self.assertEqual({path.name: path.read_bytes() for path in self.publication.iterdir()}, before)

    def test_default_one_part_retains_original_bytes(self):
        index, sha = transport.split_publication(self.publication, self.output, COMMIT)
        self.assertEqual(len(index["parts"]), 1)
        self.assertEqual(transport.verify_parts(self.output, COMMIT, sha), index)

    def test_cli_emits_exact_tuple_and_part_count_after_verified_split(self):
        github_output = self.root / "github-output"
        actual = subprocess.run([sys.executable, str(HERE / "split-sealed-publication.py"),
                                 "--publication", str(self.publication), "--output", str(self.output),
                                 "--expected-commit", COMMIT, "--github-output", str(github_output)],
                                capture_output=True, text=True, check=True, timeout=20)
        value = json.loads(actual.stdout)
        self.assertEqual(github_output.read_text(), "part_count=1\n")
        self.assertEqual(value["sourceCommit"], COMMIT)
        self.assertEqual(value["originalSealSha256"], transport.digest(self.publication / "seal.json"))
        self.assertEqual(value["originalReceiptSha256"], transport.digest(self.publication / "publish-manifest.json"))
        self.assertEqual(value["archiveSha256"], transport.digest(self.publication / "wwwroot.zip"))
        self.assertFalse(value["runtimeAccepted"])
        transport.verify_parts(self.output, COMMIT, value["indexSha256"])

    def test_default_19mib_boundary_produces_two_bounded_parts(self):
        self.fixture(payload=b"x" * (20 * 1024 * 1024))
        index, sha = transport.split_publication(self.publication, self.output, COMMIT)
        self.assertEqual(len(index["parts"]), 2)
        self.assertEqual(index["parts"][0]["bytes"], 19 * 1024 * 1024)
        self.assertEqual(transport.verify_parts(self.output, COMMIT, sha), index)

    def test_wrong_expected_commit_refused(self):
        self.refused(commit="2" * 40)

    def test_wrong_original_zip_size_refused(self):
        self.seal["zipBytes"] += 1
        self.write_metadata()
        self.refused()

    def test_wrong_original_zip_sha_refused(self):
        self.seal["zipSha256"] = "0" * 64
        self.write_metadata()
        self.refused()

    def test_wrong_original_receipt_sha_refused(self):
        self.seal["receiptSha256"] = "0" * 64
        (self.publication / "seal.json").write_bytes(encoded(self.seal))
        self.refused()

    def test_wrong_full_zip_member_body_sha_refused(self):
        self.receipt["publishFiles"][1]["sha256"] = "0" * 64
        self.write_metadata()
        self.refused()

    def test_wrong_file_count_refused(self):
        self.seal["fileCount"] += 1
        self.write_metadata()
        self.refused()

    def test_duplicate_zip_member_refused(self):
        with zipfile.ZipFile(self.publication / "wwwroot.zip", "a") as archive:
            archive.writestr("extra.bin", b"duplicate")
            archive.writestr("extra.bin", b"duplicate")
        self.seal["zipBytes"] = (self.publication / "wwwroot.zip").stat().st_size
        self.seal["zipSha256"] = transport.digest(self.publication / "wwwroot.zip")
        self.write_metadata()
        self.refused(part_bytes=400000)

    def test_traversal_inventory_refused(self):
        self.fixture(name="../payload.bin")
        self.refused()

    def test_private_inventory_refused(self):
        self.fixture(name="profiles/payload.bin")
        self.refused()

    def test_zip_symlink_member_refused(self):
        with zipfile.ZipFile(self.publication / "wwwroot.zip", "w") as archive:
            archive.writestr("index.html", b"Anonymous transport UNIT fixture")
            info = zipfile.ZipInfo("payload.bin")
            info.create_system = 3
            info.external_attr = (stat.S_IFLNK | 0o777) << 16
            archive.writestr(info, bytes(range(256)) * 2048)
        self.seal["zipBytes"] = (self.publication / "wwwroot.zip").stat().st_size
        self.seal["zipSha256"] = transport.digest(self.publication / "wwwroot.zip")
        self.write_metadata()
        self.refused()

    def test_source_symlink_refused(self):
        original = self.root / "original.zip"
        (self.publication / "wwwroot.zip").rename(original)
        (self.publication / "wwwroot.zip").symlink_to(original)
        self.refused()

    def test_extra_producer_file_refused(self):
        (self.publication / "not-public.bin").write_bytes(b"unit")
        self.refused()

    def test_existing_output_never_overwritten(self):
        self.output.mkdir()
        marker = self.output / "sentinel"
        marker.write_bytes(b"existing owner")
        with self.assertRaises(ValueError):
            self.split()
        self.assertEqual(marker.read_bytes(), b"existing owner")

    def test_archive_exceeding_two_part_capacity_refused(self):
        self.refused(part_bytes=100)

    def test_truncated_part_refused(self):
        index, sha = self.split()
        part = self.output / index["parts"][0]["path"]
        part.write_bytes(part.read_bytes()[:-1])
        with self.assertRaises(ValueError):
            transport.verify_parts(self.output, COMMIT, sha)

    def test_wrong_part_index_order_refused(self):
        index, _ = self.split()
        index["parts"].reverse()
        sha = self.rewrite_index(index)
        with self.assertRaises(ValueError):
            transport.verify_parts(self.output, COMMIT, sha)

    def test_self_consistent_part_hashes_wrong_whole_reassembly_refused(self):
        index, _ = self.split()
        paths = [self.output / row["path"] for row in index["parts"]]
        left, right = [path.read_bytes() for path in paths]
        paths[0].write_bytes(right)
        paths[1].write_bytes(left)
        for path, row in zip(paths, index["parts"]):
            row.update(bytes=path.stat().st_size, sha256=transport.digest(path))
        sha = self.rewrite_index(index)
        with self.assertRaisesRegex(ValueError, "Reassembly size/hash"):
            transport.verify_parts(self.output, COMMIT, sha)

    def test_missing_second_part_refused(self):
        _, sha = self.split()
        shutil.rmtree(self.output / "part02")
        with self.assertRaises(ValueError):
            transport.verify_parts(self.output, COMMIT, sha)

    def test_changed_original_seal_copy_refused(self):
        _, sha = self.split()
        path = self.output / "part02/seal.json"
        path.write_bytes(path.read_bytes() + b"\n")
        with self.assertRaises(ValueError):
            transport.verify_parts(self.output, COMMIT, sha)

    def test_wrong_independent_index_hash_refused(self):
        self.split()
        with self.assertRaises(ValueError):
            transport.verify_parts(self.output, COMMIT, "0" * 64)


if __name__ == "__main__":
    unittest.main(verbosity=2)
