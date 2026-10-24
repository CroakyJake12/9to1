"""Bounded build-free checks for the upstream-update lock preparation tool.

These tests never compile, link, run inference, download models or install packages.
They exercise the pure metadata generation/verification logic from `prepare-upstream-update.py`
against a synthetic archive and against the maintained lock schema.
"""
import hashlib
import io
import json
import os
import pathlib
import shutil
import subprocess
import sys
import tarfile
import tempfile
import unittest
from unittest import mock

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


class LockRowValidationTests(unittest.TestCase):
    def _fixture(self, root):
        mod = load_tool()
        archive = root / "source.tar.gz"
        archive.write_bytes(make_archive({"a.cpp": b"int value = 1;\n"}))
        lock_path = root / "lock.json"
        args = Args()
        args.archive = archive
        args.origin = "https://example.invalid/upstream"
        args.commit, args.tree = "a" * 40, "b" * 40
        args.archive_url = ""
        args.verified_tree_blobs = None
        args.out = lock_path
        self.assertEqual(mod.generate(args), 0)
        lock = json.loads(lock_path.read_text())
        tree_path = root / "tree.json"
        tree_path.write_text(json.dumps({"treeSha": args.tree,
            "blobs": {row["path"]: row["gitBlobSha"] for row in lock["rows"]}}))
        verify = Args(); verify.archive = archive; verify.lock = lock_path
        tree = Args(); tree.archive = archive; tree.lock = lock_path; tree.tree_blobs = tree_path
        return mod, lock, lock_path, (("verify", verify), ("verify-tree", tree))

    def _invoke(self, mod, mode, args):
        return mod.verify(args) if mode == "verify" else mod.verify_tree(args)

    def test_unique_lock_rows_preserve_valid_round_trip(self):
        with tempfile.TemporaryDirectory() as tmp:
            mod, lock, _, modes = self._fixture(pathlib.Path(tmp))
            self.assertEqual((lock["commit"], lock["gitCommitTree"]), ("a" * 40, "b" * 40))
            self.assertEqual(lock["blobCount"], 1)
            for mode, args in modes:
                with self.subTest(mode=mode):
                    self.assertEqual(self._invoke(mod, mode, args), 0)

    def test_duplicate_lock_rows_rejected_even_when_last_row_is_valid(self):
        with tempfile.TemporaryDirectory() as tmp:
            mod, lock, lock_path, modes = self._fixture(pathlib.Path(tmp))
            valid = dict(lock["rows"][0])
            # Equal duplicates and a conflicting first row must both refuse. A dict
            # comprehension previously hid the first row behind the valid last one.
            for conflicting in (False, True):
                shadowed = dict(valid)
                if conflicting:
                    shadowed["gitBlobSha"], shadowed["sha256"] = "0" * 40, "0" * 64
                lock["rows"] = [shadowed, valid]
                lock["blobCount"] = 1  # same unique count; cannot legitimize duplicate rows
                lock_path.write_text(json.dumps(lock))
                for mode, args in modes:
                    with self.subTest(mode=mode, conflicting=conflicting):
                        with self.assertRaisesRegex(ValueError, "Duplicate source-lock path"):
                            self._invoke(mod, mode, args)

    def test_declared_blob_count_must_match_unique_rows(self):
        with tempfile.TemporaryDirectory() as tmp:
            mod, lock, lock_path, modes = self._fixture(pathlib.Path(tmp))
            for count in (0, 2, -1, True, 1.0, None, "1"):
                lock["blobCount"] = count
                lock_path.write_text(json.dumps(lock))
                for mode, args in modes:
                    with self.subTest(mode=mode, count=count):
                        with self.assertRaisesRegex(ValueError, "source-lock blobCount"):
                            self._invoke(mod, mode, args)


class HostCachePreparationTests(unittest.TestCase):
    """Tiny local fixtures test the preparation mechanics, not CUDA/model behavior."""
    ENGINE = "src/qwen35/q36_engine.cu"
    BASE = b"cold\nwarm\n"
    PATCH = (b"--- a/src/qwen35/q36_engine.cu\n+++ b/src/qwen35/q36_engine.cu\n"
             b"@@ -1,2 +1,2 @@\n cold\n-warm\n+hot\n")

    def _temporary(self):
        return tempfile.TemporaryDirectory(dir=pathlib.Path(__file__).resolve().parent)

    def _source(self, root):
        files = {self.ENGINE: self.BASE, "include/pool.hpp": b"synchronous\n"}
        rows = {}
        for name, body in files.items():
            path = root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(body)
            rows[name] = {"path": name, "bytes": len(body),
                          "gitBlobSha": hashlib.sha1(b"blob " + str(len(body)).encode() + b"\0" + body).hexdigest(),
                          "sha256": hashlib.sha256(body).hexdigest()}
        return rows

    def test_checked_in_profile_and_original_cpu_proven_payload_are_exact(self):
        mod = load_tool()
        assets, profile = mod.load_host_cache_profile()
        self.assertEqual((profile["release"], profile["commit"], profile["gitCommitTree"]),
                         ("v0.2.3.1", "7ddc17a38fe7f31637e895ecfba58c2cd43d65ec",
                          "ead28543fa3f3d02fb595754c3c0603a10b3580c"))
        self.assertEqual(len(mod.validated_lock_rows(profile)), 10)
        self.assertNotIn("archive", profile)  # never substitute a selected profile for a full lock
        payload = mod.host_cache_payload(assets, profile)
        expected = {
            "src/qwen35/thunder_host_expert_cache.h": "6813edf0dfa4fb5b7c75cbc74647b37d167ae4655bf8e33f4a0bb4fde91031f0",
            "q36_engine.integration.patch": "b756fbc5fbbfc1c83e5988d6d511be6eaaa4321830f7db37d64f9f834f2c297d",
            "smoke/host_expert_cache_smoke.cpp": "51f00026cf1f80c6cf1d61d919ae586ca6484334894bc2106ba770e66118f825",
            "LICENSE.strata": "949b9b0aada7476006469e58ee4ffb0e0f68c2d9137e90a356757b61a09d37f0",
            "notices/ggml/LICENSE": "94f29bbed6a22c35b992c5c6ebf0e7c92f13b836b90f36f461c9cf2f0f1d010d",
            "notices/ggml/VERSION.txt": "c6ee69e9acdf5b4a2f5b8e64191bd380baeaf00b1984de721d67d4698c96b5fb",
        }
        self.assertEqual({name: hashlib.sha256(body).hexdigest() for name, body in payload.items()}, expected)

    def test_selected_external_bytes_verify_and_changed_dependency_refuses(self):
        mod = load_tool()
        with self._temporary() as temporary:
            root = pathlib.Path(temporary)
            rows = self._source(root)
            self.assertEqual(mod.verify_selected_host_cache_source(root, rows)[self.ENGINE], self.BASE)
            (root / "include/pool.hpp").write_bytes(b"asynchronous\n")
            with self.assertRaisesRegex(ValueError, "source identity mismatch"):
                mod.verify_selected_host_cache_source(root, rows)

    def test_strict_patch_has_independently_specified_postimage_and_refuses_fuzz(self):
        mod = load_tool()
        self.assertEqual(mod.strict_host_cache_patch(self.BASE, self.PATCH), b"cold\nhot\n")
        for bad in (self.PATCH.replace(b" cold\n", b" changed\n"),
                    self.PATCH.replace(b"@@ -1,2 +1,2", b"@@ -2,2 +1,2"),
                    self.PATCH.replace(b"a/src/qwen35/q36_engine.cu", b"a/src/core/expert_cache.cpp")):
            with self.assertRaises(ValueError):
                mod.strict_host_cache_patch(self.BASE, bad)

    def test_changed_payload_refuses_before_output(self):
        mod = load_tool()
        assets, profile = mod.load_host_cache_profile()
        original = mod.read_regular_source
        def changed(path):
            body = original(path)
            return body + b"\n" if path.name == "thunder_host_expert_cache.h" else body
        with mock.patch.object(mod, "read_regular_source", changed):
            with self.assertRaisesRegex(ValueError, "Frozen host-cache payload changed"):
                mod.host_cache_payload(assets, profile)

    def test_changed_profile_refuses(self):
        mod = load_tool()
        original = mod.read_regular_source
        with mock.patch.object(mod, "read_regular_source", lambda path: original(path) + b"\n"):
            with self.assertRaisesRegex(ValueError, "preparation profile changed"):
                mod.load_host_cache_profile()

    def test_final_symlink_swap_at_open_refuses(self):
        mod = load_tool()
        with self._temporary() as temporary:
            root = pathlib.Path(temporary)
            source, target = root / "source", root / "target"
            source.write_bytes(b"source")
            target.write_bytes(b"target")
            real_open = os.open
            def swapped(path, flags):
                source.unlink()
                source.symlink_to(target)
                return real_open(path, flags)
            with mock.patch.object(mod.os, "open", swapped):
                with self.assertRaisesRegex(ValueError, "final-link-safe input open refused"):
                    mod.read_regular_source(source)

    def test_fifo_is_nonblocking_and_refused_before_read(self):
        mod = load_tool()
        with self._temporary() as temporary:
            fifo = pathlib.Path(temporary) / "fifo"
            os.mkfifo(fifo)
            real_open = os.open
            def checked(path, flags):
                self.assertTrue(flags & os.O_NONBLOCK)
                return real_open(path, flags)
            with mock.patch.object(mod.os, "open", checked):
                with mock.patch.object(mod.os, "read", side_effect=AssertionError("must not read")):
                    with self.assertRaisesRegex(ValueError, "regular input file required"):
                        mod.read_regular_source(fifo)

    def test_same_size_change_during_descriptor_read_refuses(self):
        mod = load_tool()
        with self._temporary() as temporary:
            source = pathlib.Path(temporary) / "source"
            source.write_bytes(b"original")
            before, real_read = source.stat(), os.read
            changed = False
            def changing(descriptor, count):
                nonlocal changed
                body = real_read(descriptor, count)
                if body and not changed:
                    changed = True
                    with source.open("r+b") as stream:
                        stream.write(b"mutated!")
                    os.utime(source, ns=(before.st_atime_ns, before.st_mtime_ns + 1000000))
                return body
            with mock.patch.object(mod.os, "read", changing):
                with self.assertRaisesRegex(ValueError, "metadata changed during read"):
                    mod.read_regular_source(source)

    def test_missing_platform_enforcement_refuses_before_open(self):
        mod = load_tool()
        with mock.patch.object(mod.os, "O_NOFOLLOW", None):
            with mock.patch.object(mod.os, "open", side_effect=AssertionError("must not open")):
                with self.assertRaisesRegex(ValueError, "platform cannot enforce"):
                    mod.read_regular_source(TOOL)

    def test_output_is_new_outside_upstream_and_never_overwrites(self):
        mod = load_tool()
        with self._temporary() as temporary:
            root = pathlib.Path(temporary)
            source = root / "upstream"
            source.mkdir()
            sentinel = source / "sentinel"
            sentinel.write_bytes(b"unchanged")
            for output in (source, source / "nested"):
                with self.assertRaisesRegex(ValueError, "outside the provided upstream"):
                    mod.emit_host_cache_packet({"x": b"x"}, output, source)
            outside = root / "packet"
            mod.emit_host_cache_packet({"nested/x": b"exact"}, outside, source)
            with self.assertRaises(FileExistsError):
                mod.emit_host_cache_packet({"nested/x": b"replacement"}, outside, source)
            self.assertEqual((outside / "nested/x").read_bytes(), b"exact")
            self.assertEqual(sentinel.read_bytes(), b"unchanged")

    def test_aliased_output_parent_inside_upstream_refuses(self):
        mod = load_tool()
        with self._temporary() as temporary:
            root = pathlib.Path(temporary)
            source = root / "upstream"
            source.mkdir()
            alias = root / "alias"
            alias.symlink_to(source, target_is_directory=True)
            with self.assertRaisesRegex(ValueError, "outside the provided upstream"):
                mod.emit_host_cache_packet({"x": b"x"}, alias / "packet", source)
            self.assertFalse((source / "packet").exists())

    def test_existing_dangling_output_link_refuses(self):
        mod = load_tool()
        with self._temporary() as temporary:
            root = pathlib.Path(temporary)
            source = root / "upstream"
            source.mkdir()
            output = root / "packet"
            output.symlink_to(root / "missing")
            with self.assertRaises(FileExistsError):
                mod.emit_host_cache_packet({"x": b"x"}, output, source)
            self.assertTrue(output.is_symlink())

    def test_output_parent_swap_cannot_redirect_writes_into_upstream(self):
        mod = load_tool()
        with self._temporary() as temporary:
            root = pathlib.Path(temporary)
            source, parent, retired = root / "upstream", root / "parent", root / "retired"
            source.mkdir()
            parent.mkdir()
            real_mkdir = os.mkdir
            changed = False
            def swapped(path, mode=0o777, **kwargs):
                nonlocal changed
                if path == "packet" and kwargs.get("dir_fd") is not None:
                    parent.rename(retired)
                    parent.symlink_to(source, target_is_directory=True)
                    changed = True
                return real_mkdir(path, mode, **kwargs)
            with mock.patch.object(mod.os, "mkdir", swapped):
                with mock.patch.object(mod.os, "supports_dir_fd", os.supports_dir_fd | {swapped}):
                    with self.assertRaisesRegex(ValueError, "output parent changed"):
                        mod.emit_host_cache_packet({"x": b"exact"}, parent / "packet", source)
            self.assertTrue(changed)
            self.assertFalse((source / "packet").exists())
            self.assertEqual((retired / "packet/x").read_bytes(), b"exact")

    def test_cli_requires_both_explicit_source_and_output(self):
        result = subprocess.run([sys.executable, "-B", str(TOOL), "prepare-host-cache"],
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False)
        self.assertEqual(result.returncode, 2)
        self.assertIn(b"--source-root", result.stderr)
        self.assertIn(b"--out", result.stderr)


class ExternalHostCachePreparationTests(unittest.TestCase):
    """Actual selected upstream bytes are supplied explicitly; no network per test."""
    @classmethod
    def setUpClass(cls):
        selected = os.environ.get("DULCHE_STRATA_HOST_CACHE_SOURCE_ROOT")
        if not selected:
            raise unittest.SkipTest("NOT RUN: supply genuine frozen selected-source directory via DULCHE_STRATA_HOST_CACHE_SOURCE_ROOT")
        cls.source = pathlib.Path(selected)
        cls.mod = load_tool()
        cls.assets, cls.profile = cls.mod.load_host_cache_profile()
        cls.rows = cls.mod.validated_lock_rows(cls.profile)
        cls.mod.verify_selected_host_cache_source(cls.source, cls.rows)  # supplied-but-invalid must fail

    def _invoke(self, tool, output):
        result = subprocess.run([sys.executable, "-B", str(tool), "prepare-host-cache",
                                 "--source-root", str(self.source), "--out", str(output)],
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False)
        self.assertEqual(result.returncode, 0, result.stderr.decode())
        return {path.relative_to(output).as_posix(): path.read_bytes()
                for path in output.rglob("*") if path.is_file()}

    def test_real_external_source_prepares_without_modifying_inputs(self):
        before = self.mod.verify_selected_host_cache_source(self.source, self.rows)
        with tempfile.TemporaryDirectory(dir=pathlib.Path(__file__).resolve().parent) as temporary:
            payload = self._invoke(TOOL, pathlib.Path(temporary) / "packet")
            receipt = json.loads(payload["preparation-receipt.json"])
            self.assertEqual(receipt["enginePostimageSha256"],
                             "991caec2b613372ed4a7c34a16b808250982c691c5f066c7d4702edbf565b820")
            self.assertFalse(receipt["providedUpstreamModified"])
            self.assertFalse(receipt["candidatePinChanged"])
        self.assertEqual(self.mod.verify_selected_host_cache_source(self.source, self.rows), before)

    def test_relocated_small_tool_and_assets_emit_identical_packet(self):
        with tempfile.TemporaryDirectory(dir=pathlib.Path(__file__).resolve().parent) as temporary:
            root = pathlib.Path(temporary)
            relocated = root / "checkout" / TOOL.name
            relocated.parent.mkdir()
            shutil.copyfile(TOOL, relocated)
            shutil.copytree(self.assets, relocated.parent / "prep" / self.assets.name)
            original = self._invoke(TOOL, root / "original-packet")
            moved = self._invoke(relocated, root / "moved-packet")
            self.assertEqual(original, moved)


if __name__ == "__main__":
    unittest.main()
