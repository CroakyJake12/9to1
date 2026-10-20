"""Bounded build-free checks for the upstream-update lock preparation tool.

These tests never compile, link, run inference, download models or install packages.
They exercise the pure metadata generation/verification logic from `prepare-upstream-update.py`
against a synthetic archive and against the maintained lock schema.
"""
import hashlib
import io
import json
import pathlib
import tarfile
import tempfile
import unittest

TOOL = pathlib.Path(__file__).resolve().parents[1] / "InferenceEngines" / "NativeStrata" / "prepare-upstream-update.py"


def load_tool():
    import importlib.util
    spec = importlib.util.spec_from_file_location("prepare_upstream_update", TOOL)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def make_archive(files):
    """files: dict path->bytes; returns bytes of a tar.gz with a single root dir."""
    buf = io.BytesIO()
    with tarfile.open(fileobj=buf, mode="w:gz") as tf:
        for rel, body in sorted(files.items()):
            info = tarfile.TarInfo(name="upstream/" + rel)
            info.size = len(body)
            tf.addfile(info, io.BytesIO(body))
    return buf.getvalue()


class SchemaCompatibilityTests(unittest.TestCase):
    def test_candidate_lock_matches_maintained_schema(self):
        maintained = json.loads(
            (TOOL.parent / "strata-source-lock.json").read_text())
        self.assertEqual(
            set(maintained.keys()),
            {"origin", "commit", "gitCommitTree", "archiveUrl", "archive",
             "blobCount", "sourceBytes", "rows"})
        row_keys = {"path", "bytes", "gitBlobSha", "sha256"}
        for row in maintained["rows"]:
            self.assertEqual(set(row.keys()), row_keys)
        self.assertEqual(len(maintained["rows"]), maintained["blobCount"])

    def test_git_blob_sha_matches_git_definition(self):
        mod = load_tool()
        body = b"#!/usr/bin/env python3\n"
        expected = hashlib.sha1(
            b"blob " + str(len(body)).encode("ascii") + b"\0" + body).hexdigest()
        self.assertEqual(mod.git_blob_sha(body), expected)


class GenerateVerifyRoundTripTests(unittest.TestCase):
    def test_generate_then_verify(self):
        mod = load_tool()
        files = {
            "CMakeLists.txt": b"cmake_minimum_required(VERSION 3.24)\n",
            "LICENSE": b"MIT License\n",
            "src/engine.cpp": b"namespace strata { int answer() { return 42; } }\n",
        }
        archive = make_archive(files)
        with tempfile.TemporaryDirectory() as tmp:
            archive_path = pathlib.Path(tmp) / "src.tar.gz"
            archive_path.write_bytes(archive)
            lock_path = pathlib.Path(tmp) / "lock.json"
            class Args:
                pass
            a = Args()
            a.archive = archive_path
            a.origin = "https://example.invalid/upstream"
            a.commit = "c" * 40
            a.tree = "t" * 40
            a.archive_url = "https://example.invalid/a.tar.gz"
            a.out = lock_path
            self.assertEqual(mod.generate(a), 0)
            lock = json.loads(lock_path.read_text())
            self.assertEqual(lock["blobCount"], len(files))
            self.assertEqual(lock["sourceBytes"], sum(len(v) for v in files.values()))
            self.assertEqual(lock["archive"]["bytes"], len(archive))
            # verify round-trip
            v = Args()
            v.archive = archive_path
            v.lock = lock_path
            self.assertEqual(mod.verify(v), 0)

    def test_verify_rejects_altered_body(self):
        mod = load_tool()
        files = {"a.cpp": b"int main() { return 0; }\n"}
        archive = make_archive(files)
        with tempfile.TemporaryDirectory() as tmp:
            archive_path = pathlib.Path(tmp) / "src.tar.gz"
            archive_path.write_bytes(archive)
            lock_path = pathlib.Path(tmp) / "lock.json"
            class Args:
                pass
            a = Args()
            a.archive = archive_path
            a.origin = "o"
            a.commit = "c" * 40
            a.tree = "t" * 40
            a.archive_url = ""
            a.out = lock_path
            mod.generate(a)
            # Now fingerprint a tampered archive with same name but different bytes.
            tampered = make_archive({"a.cpp": b"int main() { return 7; }\n"})
            bad_path = pathlib.Path(tmp) / "bad.tar.gz"
            bad_path.write_bytes(tampered)
            v = Args()
            v.archive = bad_path
            v.lock = lock_path
            with self.assertRaises(ValueError):
                mod.verify(v)


if __name__ == "__main__":
    unittest.main()
