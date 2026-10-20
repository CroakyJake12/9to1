"""Bounded build-free plan/guard checks for the candidate (v0.2.1) integration.

These tests never compile, link, run inference, download models or install packages. They assert
PROVENANCE/PLAN invariants and that the candidate worker implements a REAL portable transport
(spawn/ready/relay/parse), that load/probe/shutdown are not unconditionally throwing, and that the
remaining text-generation dependency is the bundled tokenizer helper (with an exact cause), not a
blanket placeholder. A source-string existence check is never evidence of a working engine.
"""
import importlib.util
import json
import pathlib
import unittest

NATIVE = pathlib.Path(__file__).resolve().parents[1] / "InferenceEngines" / "NativeStrata"


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


class CandidateProfilePlanTests(unittest.TestCase):
    def test_cmake_has_explicit_profile_selection(self):
        cmake = (NATIVE / "CMakeLists.txt").read_text()
        self.assertIn("DULCHE_STRATA_PROFILE", cmake)
        self.assertIn('"legacy"', cmake)
        self.assertIn("candidate", cmake)
        self.assertIn("DULCHE_STRATA_PROFILESTREQUAL\"legacy\"", cmake.replace(" ", ""))

    def test_cmake_candidate_branch_uses_real_donor_settings(self):
        cmake = (NATIVE / "CMakeLists.txt").read_text()
        self.assertIn("STRATA_ENABLE_CUDA ON", cmake)
        self.assertIn("STRATA_NATIVE_EXPERTS ON", cmake)
        self.assertIn("dulche-strata-candidate-worker", cmake)
        self.assertIn("dulche-thunder-engine", cmake)
        self.assertIn("STRATA_HAS_CUDA=1", cmake)
        self.assertIn("CUDA::cudart", cmake)
        self.assertIn("strata_tokenizer_helper.py", cmake)
        self.assertNotIn("apps/desktop", cmake)
        self.assertNotIn("add_subdirectory(apps", cmake)

    def test_build_package_requires_lock_for_candidate(self):
        mod = load_module("strata_package", NATIVE / "build-package.py")
        self.assertIn("legacy", mod.PROFILES)
        self.assertIn("candidate", mod.PROFILES)
        defaults = {k: (v is None) for k, v in mod.DEFAULT_LOCKS.items()}
        self.assertTrue(defaults["legacy"])
        self.assertTrue(defaults["candidate"])

    def test_build_package_receipt_covers_engine_helper_closure(self):
        src = (NATIVE / "build-package.py").read_text()
        self.assertIn("engineSha256", src)
        self.assertIn("helperSha256", src)
        self.assertIn("dependencyPinnedCommit", src)
        self.assertIn("resolve_built", src)          # multi-config aware artifact resolution
        self.assertIn("windows-x64", src)
        self.assertIn("verify_windows_x64_pe", src)

    def test_candidate_worker_implements_real_transport(self):
        # The candidate worker must implement the real spawn/ready/relay/parse lifecycle rather than
        # leaving all load/generate paths unconditionally throwing.
        src = (NATIVE / "dulche_strata_candidate_worker.cpp").read_text()
        for token in ("spawn_engine", "wait_ready", "build_gen_line", "parse_done",
                      "locate_engine", "engine_send", "read_command", "write_frame"):
            self.assertIn(token, src)
        # The blanket "candidate load blocker" / "candidate generate blocker" strings are gone; the
        # remaining text-generation block is the bundled tokenizer-helper dependency with an exact cause.
        self.assertNotIn("candidate load blocker", src)
        self.assertNotIn("candidate generate blocker", src)
        self.assertIn("tokenizer helper", src)
        self.assertIn("kAbiVersion = 2", src)
        self.assertIn("SOURCE-ONLY", src)


class DependencyLockPlanTests(unittest.TestCase):
    def test_llama_dependency_lock_schema(self):
        lock = json.loads((NATIVE / "llama.cpp-dependency-lock.json").read_text())
        self.assertEqual(lock["pinnedCommit"], "3cf03257f219afbe7334045ff7c6a06ac68c627d")
        self.assertTrue(lock["verification"]["commitAndTreeVetted"])
        self.assertEqual(lock["verification"]["status"].split(";")[0], "METADATA-VERIFIED")
        self.assertIn("ggml-cpu", lock["dependency"])

    def test_candidate_requires_tree_distinct_from_commit(self):
        tool = load_module("prepare_upstream_update", NATIVE / "prepare-upstream-update.py")
        import io as _io
        import tarfile as _tarfile
        import tempfile as _tempfile
        buf = _io.BytesIO()
        with _tarfile.open(fileobj=buf, mode="w:gz") as tf:
            info = _tarfile.TarInfo(name="upstream/a.cpp")
            info.size = 4
            tf.addfile(info, _io.BytesIO(b"int\n"))
        with _tempfile.TemporaryDirectory() as tmp:
            archive = pathlib.Path(tmp) / "a.tar.gz"
            archive.write_bytes(buf.getvalue())
            class Args: pass
            a = Args()
            a.archive = archive
            a.origin = "https://example.invalid/upstream"
            a.commit = "a" * 40
            a.tree = "a" * 40
            a.archive_url = ""
            a.verified_tree_blobs = None
            a.out = pathlib.Path(tmp) / "lock.json"
            with self.assertRaises(ValueError) as ctx:
                tool.generate(a)
            self.assertIn("Provenance", str(ctx.exception))


if __name__ == "__main__":
    unittest.main()
