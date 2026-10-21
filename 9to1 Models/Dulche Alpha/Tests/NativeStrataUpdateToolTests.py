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


class Args:
    pass


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
            a.commit = "a" * 40
            a.tree = "b" * 40
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
            a.commit = "a" * 40
            a.tree = "b" * 40
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


class ProvenanceGuardsTests(unittest.TestCase):
    def _args(self, commit, tree, archive, out):
        class Args:
            pass
        a = Args()
        a.archive = archive
        a.origin = "https://example.invalid/upstream"
        a.commit = commit
        a.tree = tree
        a.archive_url = ""
        a.verified_tree_blobs = None
        a.out = out
        return a

    def test_generate_rejects_same_commit_tree(self):
        mod = load_tool()
        archive = make_archive({"a.cpp": b"int main() { return 0; }\n"})
        with tempfile.TemporaryDirectory() as tmp:
            archive_path = pathlib.Path(tmp) / "src.tar.gz"
            archive_path.write_bytes(archive)
            lock_path = pathlib.Path(tmp) / "lock.json"
            a = self._args("c" * 40, "c" * 40, archive_path, lock_path)
            with self.assertRaises(ValueError) as ctx:
                mod.generate(a)
            self.assertIn("Provenance", str(ctx.exception))

    def test_generate_rejects_non_hex_identifiers(self):
        mod = load_tool()
        archive = make_archive({"a.cpp": b"int main() { return 0; }\n"})
        with tempfile.TemporaryDirectory() as tmp:
            archive_path = pathlib.Path(tmp) / "src.tar.gz"
            archive_path.write_bytes(archive)
            lock_path = pathlib.Path(tmp) / "lock.json"
            a = self._args("c" * 40, "t" * 40, archive_path, lock_path)
            with self.assertRaises(ValueError) as ctx:
                mod.generate(a)
            self.assertIn("40-hex", str(ctx.exception))

    def test_generate_dictates_tree_from_tree_argument(self):
        mod = load_tool()
        archive = make_archive({"a.cpp": b"int main() { return 0; }\n"})
        with tempfile.TemporaryDirectory() as tmp:
            archive_path = pathlib.Path(tmp) / "src.tar.gz"
            archive_path.write_bytes(archive)
            lock_path = pathlib.Path(tmp) / "lock.json"
            a = self._args("a" * 40, "b" * 40, archive_path, lock_path)
            self.assertEqual(mod.generate(a), 0)
            lock = json.loads(lock_path.read_text())
            self.assertEqual(lock["commit"], "a" * 40)
            self.assertEqual(lock["gitCommitTree"], "b" * 40)
            self.assertNotEqual(lock["commit"], lock["gitCommitTree"])


class DuplicateMemberTests(unittest.TestCase):
    def test_extract_rows_rejects_duplicate_members(self):
        mod = load_tool()
        buf = io.BytesIO()
        with tarfile.open(fileobj=buf, mode="w:gz") as tf:
            for _ in range(2):
                info = tarfile.TarInfo(name="upstream/a.cpp")
                info.size = 4
                tf.addfile(info, io.BytesIO(b"int\n"))
        with tempfile.TemporaryDirectory() as tmp:
            archive_path = pathlib.Path(tmp) / "dup.tar.gz"
            archive_path.write_bytes(buf.getvalue())
            with self.assertRaises(ValueError) as ctx:
                mod.extract_rows(archive_path)
            self.assertIn("Duplicate", str(ctx.exception))


class VerifyTreeCrossCheckTests(unittest.TestCase):
    def _make_archive_and_lock(self, files, tmp):
        archive = make_archive(files)
        archive_path = pathlib.Path(tmp) / "src.tar.gz"
        archive_path.write_bytes(archive)
        lock_path = pathlib.Path(tmp) / "lock.json"
        a = Args()
        a.archive = archive_path
        a.origin = "https://example.invalid/upstream"
        a.commit = "a" * 40
        a.tree = "b" * 40
        a.archive_url = ""
        a.verified_tree_blobs = None
        a.out = lock_path
        load_tool().generate(a)
        return lock_path, archive_path

    def test_verify_tree_cross_check_ok(self):
        mod = load_tool()
        files = {"a.cpp": b"int main() { return 0; }\n"}
        with tempfile.TemporaryDirectory() as tmp:
            lock_path, archive_path = self._make_archive_and_lock(files, tmp)
            lock = json.loads(lock_path.read_text())
            # Build an authenticated blob map from the generated lock (same bytes).
            blobs = {r["path"]: r["gitBlobSha"] for r in lock["rows"]}
            tree_path = pathlib.Path(tmp) / "tree.json"
            tree_path.write_text(json.dumps({"treeSha": lock["gitCommitTree"], "blobs": blobs}))
            class Args:
                pass
            a = Args(); a.lock = lock_path; a.tree_blobs = tree_path; a.archive = archive_path
            self.assertEqual(mod.verify_tree(a), 0)

    def test_verify_tree_detects_unexplained_mismatch(self):
        mod = load_tool()
        files = {"a.cpp": b"int main() { return 0; }\n"}
        with tempfile.TemporaryDirectory() as tmp:
            lock_path, archive_path = self._make_archive_and_lock(files, tmp)
            lock = json.loads(lock_path.read_text())
            blobs = {r["path"]: ("0" * 40) for r in lock["rows"]}  # wrong tree blob
            tree_path = pathlib.Path(tmp) / "tree.json"
            tree_path.write_text(json.dumps({"treeSha": lock["gitCommitTree"], "blobs": blobs}))
            class Args:
                pass
            a = Args(); a.lock = lock_path; a.tree_blobs = tree_path; a.archive = archive_path
            self.assertEqual(mod.verify_tree(a), 2)

    def test_verify_tree_rejects_header_mismatch(self):
        # A tree whose header does not equal the lock's tree must fail even if blobs match.
        mod = load_tool()
        files = {"a.cpp": b"int main() { return 0; }\n"}
        with tempfile.TemporaryDirectory() as tmp:
            lock_path, archive_path = self._make_archive_and_lock(files, tmp)
            lock = json.loads(lock_path.read_text())
            blobs = {r["path"]: r["gitBlobSha"] for r in lock["rows"]}
            tree_path = pathlib.Path(tmp) / "tree.json"
            tree_path.write_text(json.dumps({"treeSha": "c" * 40, "blobs": blobs}))
            class Args:
                pass
            a = Args(); a.lock = lock_path; a.tree_blobs = tree_path; a.archive = archive_path
            self.assertEqual(mod.verify_tree(a), 2)

    def test_generate_header_binding_and_blanket_exemption_removed(self):
        # A body that differs from the tree only by a CRLF->LF export transform is EXPLAINED by
        # recomputing the normalized hash; a pathname exemption alone must never accept it.
        mod = load_tool()
        body_crlf = b"int main() { return 0; }\r\n"
        files = {"a.cpp": body_crlf}
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
            a.commit = "a" * 40
            a.tree = "b" * 40
            a.archive_url = ""
            a.out = lock_path
            mod.generate(a)
            # The authenticated tree stores the LF normalized blob for a.cpp.
            tree_blob = mod.git_blob_sha(body_crlf.replace(b"\r\n", b"\n"))
            tree = {"treeSha": "b" * 40, "blobs": {"a.cpp": tree_blob},
                    "eolExportExempt": ["a.cpp"]}   # even if a legacy exempt list claims it
            tree_path = pathlib.Path(tmp) / "tree.json"
            tree_path.write_text(json.dumps(tree))
            b = Args()
            b.archive = archive_path
            b.origin = "o"
            b.commit = "a" * 40
            b.tree = "b" * 40
            b.archive_url = ""
            b.verified_tree_blobs = tree_path
            b.out = lock_path
            self.assertEqual(mod.generate(b), 0)
            lock = json.loads(lock_path.read_text())
            self.assertEqual(lock["treeCrossCheck"]["eolExportNormalized"], ["a.cpp"])
            # A header that does not match the selected tree must fail even though the EOL normalization
            # would otherwise explain the body.
            tree_bad = {"treeSha": "e" * 40, "blobs": tree["blobs"]}
            tree_bad_path = pathlib.Path(tmp) / "tree_bad.json"
            tree_bad_path.write_text(json.dumps(tree_bad))
            c = Args()
            c.archive = archive_path
            c.origin = "o"
            c.commit = "a" * 40
            c.tree = "b" * 40
            c.archive_url = ""
            c.verified_tree_blobs = tree_bad_path
            c.out = lock_path
            with self.assertRaises(ValueError) as ctx:
                mod.generate(c)
            self.assertIn("header mismatch", str(ctx.exception))

    def test_unexplained_mismatch_fails_despite_exempt_list(self):
        # A genuinely different body is NOT explained by CRLF->LF normalization, and a pathname exempt
        # list must NOT rescue it.
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
            a.commit = "a" * 40
            a.tree = "b" * 40
            a.archive_url = ""
            a.out = lock_path
            mod.generate(a)
            tree = {"treeSha": "b" * 40, "blobs": {"a.cpp": "0" * 40},
                    "eolExportExempt": ["a.cpp"]}
            tree_path = pathlib.Path(tmp) / "tree.json"
            tree_path.write_text(json.dumps(tree))
            b = Args()
            b.archive = archive_path
            b.origin = "o"
            b.commit = "a" * 40
            b.tree = "b" * 40
            b.archive_url = ""
            b.verified_tree_blobs = tree_path
            b.out = lock_path
            with self.assertRaises(ValueError) as ctx:
                mod.generate(b)
            self.assertIn("cross-check failed", str(ctx.exception))


if __name__ == "__main__":
    unittest.main()
