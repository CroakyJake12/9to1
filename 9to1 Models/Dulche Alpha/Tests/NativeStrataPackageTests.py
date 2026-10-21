"""Actual packaging predicate controls; no binary build or CUDA proof."""
import hashlib
import importlib.util
import io
import json
import pathlib
import struct
import subprocess
import sys
import tarfile
import tempfile
import unittest

class NativeTargetControls(unittest.TestCase):
    def test_actual_target_refuses_other_architectures(self):
        p=pathlib.Path(__file__).resolve().parents[1]/"InferenceEngines"/"NativeStrata"/"build-package.py"
        s=importlib.util.spec_from_file_location("strata_package",p)
        owner=importlib.util.module_from_spec(s);s.loader.exec_module(owner)
        h=bytearray(64);h[:7]=b"\x7fELF\x02\x01\x01"
        h[16:18]=(2).to_bytes(2,"little");h[18:20]=(62).to_bytes(2,"little");h[20:24]=(1).to_bytes(4,"little")
        self.assertEqual(62,owner.verify_linux_x64_elf(bytes(h))["machine"])
        for offset,value in [(4,1),(5,2),(18,183),(7,9)]:
            b=bytearray(h);b[offset]=value
            with self.subTest(offset=offset),self.assertRaises(ValueError):owner.verify_linux_x64_elf(bytes(b))
        with self.assertRaises(ValueError):owner.verify_linux_x64_elf(b"not ELF")

    def test_windows_pe_check_accepts_pe32p_x64(self):
        p=pathlib.Path(__file__).resolve().parents[1]/"InferenceEngines"/"NativeStrata"/"build-package.py"
        s=importlib.util.spec_from_file_location("strata_package",p)
        owner=importlib.util.module_from_spec(s);s.loader.exec_module(owner)
        def make_pe(machine=0x8664, magic=0x20B, mz=True):
            pe_off=0x80
            body=bytearray(pe_off+64)
            if mz: body[0:2]=b"MZ"
            struct.pack_into("<I", body, 0x3C, pe_off)
            body[pe_off:pe_off+4]=b"PE\x00\x00"
            struct.pack_into("<H", body, pe_off+4, machine)
            struct.pack_into("<H", body, pe_off+24, magic)
            return bytes(body)
        r=owner.verify_windows_x64_pe(make_pe())
        self.assertEqual(r["machine"],0x8664); self.assertEqual(r["targetSystem"],"windows-x64")
        with self.assertRaises(ValueError): owner.verify_windows_x64_pe(make_pe(machine=0x14C))  # i386
        with self.assertRaises(ValueError): owner.verify_windows_x64_pe(make_pe(magic=0x10B))   # PE32
        with self.assertRaises(ValueError): owner.verify_windows_x64_pe(make_pe(mz=False))
        # a Linux worker must never pass as a Windows target and vice versa
        with self.assertRaises(ValueError): owner.verify_windows_x64_pe(b"\x7fELF"+b"\0"*100)
        with self.assertRaises(ValueError): owner.verify_linux_x64_elf(make_pe())

    def test_build_package_supports_profile_lock_target_selection(self):
        p=pathlib.Path(__file__).resolve().parents[1]/"InferenceEngines"/"NativeStrata"/"build-package.py"
        src=p.read_text()
        self.assertIn("--profile", src); self.assertIn("candidate", src); self.assertIn("legacy", src)
        self.assertIn("--lock", src); self.assertIn("--target", src)
        self.assertIn("windows-x64", src); self.assertIn("verify_windows_x64_pe", src)
        self.assertIn("lockSha256", src); self.assertIn("toolchain", src)
        self.assertIn("workerSha256", src)

    def test_cmake_installs_donor_runtime_closure(self):
        # pass-3 installed ONLY the helper; the donor tokenizer + gguf reader + runtime dep lock must
        # be installed beside it or the text path fails (a real packaging defect).
        cm = pathlib.Path(__file__).resolve().parents[1]/"InferenceEngines"/"NativeStrata"/"CMakeLists.txt"
        src = cm.read_text()
        self.assertIn("strata_tokenizer_helper.py", src)
        self.assertIn("strata_tokenizer.py", src)
        self.assertIn("gguf_reader.py", src)
        self.assertIn("strata-runtime-dependencies.json", src)
        self.assertIn("hash-verified runtime closure", src)

    def test_runtime_dependency_lock_pins_regex_jinja2(self):
        native = pathlib.Path(__file__).resolve().parents[1]/"InferenceEngines"/"NativeStrata"
        lock = json.loads((native/"strata-runtime-dependencies.json").read_text())
        self.assertEqual(lock["schema"], "strata-runtime-dependencies/v1")
        self.assertIn("regex", lock["runtimeModules"])
        self.assertIn("jinja2", lock["runtimeModules"])
        self.assertEqual(lock["runtimeModules"]["regex"]["pin"], "2026.9.29")
        self.assertEqual(lock["runtimeModules"]["jinja2"]["pin"], "3.1.6")
        self.assertTrue(lock["runtimeModules"]["regex"]["sdist"]["sha256"].startswith("8b5fcc"))
        self.assertTrue(lock["runtimeModules"]["jinja2"]["sdist"]["sha256"].startswith("0137fb"))

    def test_build_package_verifies_helper_closure(self):
        p=pathlib.Path(__file__).resolve().parents[1]/"InferenceEngines"/"NativeStrata"/"build-package.py"
        src=p.read_text()
        self.assertIn("helperClosure", src)
        self.assertIn("required_closure", src)
        self.assertIn("gguf_reader.py", src)
        self.assertIn("runtimeDepsLockSha256", src)
        self.assertIn("runtimeModulesPinned", src)
        # The closure must be REQUIRED (raise on missing), not optional receipt fields.
        self.assertIn("raise ValueError(\"Installed text-path runtime closure is incomplete", src)


def _build_package_module():
    p = pathlib.Path(__file__).resolve().parents[1] / "InferenceEngines" / "NativeStrata" / "build-package.py"
    spec = importlib.util.spec_from_file_location("strata_package_ci", p)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def _git_blob_sha(body):
    return hashlib.sha1(b"blob " + str(len(body)).encode() + b"\0" + body).hexdigest()


def _make_donor_tar(root, files):
    buf = io.BytesIO()
    with tarfile.open(fileobj=buf, mode="w:gz") as tf:
        rootinfo = tarfile.TarInfo(root + "/")
        rootinfo.type = tarfile.DIRTYPE
        tf.addfile(rootinfo)
        for rel, body in files.items():
            info = tarfile.TarInfo(root + "/" + rel)
            info.size = len(body)
            tf.addfile(info, io.BytesIO(body))
    return buf.getvalue()


class CandidateArchiveVerifierControls(unittest.TestCase):
    """Functional verify-only fixtures for the build-package.py archive verifier (CRLF eolExportNormalized).

    The candidate lock records exactly 16 archive members whose git export is CRLF -> LF normalized.
    The verifier must retain exact archive bytes+SHA and per-file bytes+SHA, accept the LF-normalized
    blob ONLY for the exact declared names whose bytes actually contain CRLF, and reject undeclared
    CRLF, digest mismatch, source escape and member-closure gaps. Tiny generated tar fixtures; no
    28 MB archive download.
    """

    def _invoke_prepare(self, lock, archive_bytes):
        with tempfile.TemporaryDirectory(prefix="thunder-verif-") as td:
            lpath = pathlib.Path(td) / "lock.json"
            lpath.write_text(json.dumps(lock))
            apath = pathlib.Path(td) / "donor.tar.gz"
            apath.write_bytes(archive_bytes)
            work = pathlib.Path(td) / "work"
            old = sys.argv[:]
            sys.argv = ["build-package.py", "--profile", "candidate",
                        "--lock", str(lpath), "--archive", str(apath),
                        "--work", str(work)]
            try:
                _build_package_module().main()
            finally:
                sys.argv = old
            rec = json.loads((work / "source-verification.json").read_text())
            extracted = {}
            for rel in ("README.md", "BENCH.bat"):
                f = work / "upstream" / rel
                if f.exists():
                    extracted[rel] = f.read_bytes()
            return rec, extracted

    # The verified candidate pin (v0.2.2): commit / tree / treeCrossCheck.treeSha.
    PIN_COMMIT = "011c9e600a93bbc9fd451c563501b5831c32bbca"
    PIN_TREE = "539755e6d6b799bd75a47225befc786c436f6d8b"

    def _lock(self, archive, rows, eol_normalized, commit=None, tree=None):
        commit = commit if commit is not None else self.PIN_COMMIT
        tree = tree if tree is not None else self.PIN_TREE
        return {
            "origin": "https://github.com/Niko1221/Strata",
            "commit": commit,
            "gitCommitTree": tree,
            "archive": {"path": None, "bytes": len(archive),
                        "sha256": hashlib.sha256(archive).hexdigest()},
            "blobCount": len(rows),
            "rows": rows,
            "treeCrossCheck": {"treeSha": tree, "blobMatches": len(rows),
                               "missingFromTree": [], "extraInTree": [],
                               "eolExportNormalized": list(eol_normalized)},
        }

    def test_declared_eol_crlf_accepted_and_extracted(self):
        root = "strata-v0.2.2"
        files = {
            "README.md": b"# hello\nplain line\n",
            "BENCH.bat": b"@echo off\r\necho hi\r\n",
        }
        archive = _make_donor_tar(root, files)
        rows = [
            {"path": "README.md", "bytes": len(files["README.md"]),
             "gitBlobSha": _git_blob_sha(files["README.md"]),
             "sha256": hashlib.sha256(files["README.md"]).hexdigest()},
            {"path": "BENCH.bat", "bytes": len(files["BENCH.bat"]),
             "gitBlobSha": _git_blob_sha(files["BENCH.bat"].replace(b"\r\n", b"\n")),
             "sha256": hashlib.sha256(files["BENCH.bat"]).hexdigest()},
        ]
        rec, extracted = self._invoke_prepare(self._lock(archive, rows, ["BENCH.bat"]), archive)
        self.assertEqual(rec["verifiedFiles"], 2)
        self.assertEqual(extracted["BENCH.bat"], files["BENCH.bat"])  # exact raw body retained

    def test_undeclared_crlf_rejected(self):
        # The defect pattern: a non-declared file whose lock gitBlobSha is the LF-normalized blob
        # (archive export has CRLF). It must be REJECTED, never silently normalized.
        root = "strata-v0.2.2"
        files = {"BENCH.bat": b"@echo off\r\necho hi\r\n"}
        archive = _make_donor_tar(root, files)
        rows = [
            {"path": "BENCH.bat", "bytes": len(files["BENCH.bat"]),
             "gitBlobSha": _git_blob_sha(files["BENCH.bat"].replace(b"\r\n", b"\n")),
             "sha256": hashlib.sha256(files["BENCH.bat"]).hexdigest()},
        ]
        with self.assertRaises(ValueError):
            self._invoke_prepare(self._lock(archive, rows, []), archive)

    def test_declared_eol_without_crlf_rejected(self):
        root = "strata-v0.2.2"
        files = {"BENCH.bat": b"@echo off\necho hi\n"}  # LF only, no CRLF to explain
        archive = _make_donor_tar(root, files)
        rows = [
            {"path": "BENCH.bat", "bytes": len(files["BENCH.bat"]),
             "gitBlobSha": _git_blob_sha(files["BENCH.bat"]),
             "sha256": hashlib.sha256(files["BENCH.bat"]).hexdigest()},
        ]
        with self.assertRaises(ValueError):
            self._invoke_prepare(self._lock(archive, rows, ["BENCH.bat"]), archive)

    def test_declared_unknown_name_rejected(self):
        # A declared EOL name outside the official 16-name allowlist must be refused even when the
        # pin matches and the blob has CRLF (never accept arbitrary self-declared normalization).
        root = "strata-v0.2.2"
        files = {"evil.bat": b"@echo off\r\necho hi\r\n"}
        archive = _make_donor_tar(root, files)
        rows = [
            {"path": "evil.bat", "bytes": len(files["evil.bat"]),
             "gitBlobSha": _git_blob_sha(files["evil.bat"].replace(b"\r\n", b"\n")),
             "sha256": hashlib.sha256(files["evil.bat"]).hexdigest()},
        ]
        with self.assertRaises(ValueError):
            self._invoke_prepare(self._lock(archive, rows, ["evil.bat"]), archive)

    def test_wrong_source_pin_rejected(self):
        # Non-empty eolExportNormalized is accepted only for the exact verified candidate commit/tree.
        root = "strata-v0.2.2"
        files = {"BENCH.bat": b"@echo off\r\necho hi\r\n"}
        archive = _make_donor_tar(root, files)
        rows = [
            {"path": "BENCH.bat", "bytes": len(files["BENCH.bat"]),
             "gitBlobSha": _git_blob_sha(files["BENCH.bat"].replace(b"\r\n", b"\n")),
             "sha256": hashlib.sha256(files["BENCH.bat"]).hexdigest()},
        ]
        with self.assertRaises(ValueError):
            self._invoke_prepare(self._lock(archive, rows, ["BENCH.bat"], commit="0" * 40), archive)
        with self.assertRaises(ValueError):
            self._invoke_prepare(self._lock(archive, rows, ["BENCH.bat"], tree="0" * 40), archive)

    def test_declared_normalization_blob_mismatch_rejected(self):
        # Declared official name with CRLF, but the normalized LF blob does not equal the locked
        # gitBlobSha -> must be refused (exact explained conversion required).
        root = "strata-v0.2.2"
        files = {"BENCH.bat": b"@echo off\r\necho hi\r\n"}
        archive = _make_donor_tar(root, files)
        rows = [
            {"path": "BENCH.bat", "bytes": len(files["BENCH.bat"]),
             "gitBlobSha": "a" * 40,  # wrong locked normalized blob
             "sha256": hashlib.sha256(files["BENCH.bat"]).hexdigest()},
        ]
        with self.assertRaises(ValueError):
            self._invoke_prepare(self._lock(archive, rows, ["BENCH.bat"]), archive)

    def test_member_digest_mismatch_rejected(self):
        root = "strata-v0.2.2"
        files = {"README.md": b"# hello\n"}
        archive = _make_donor_tar(root, files)
        rows = [
            {"path": "README.md", "bytes": len(files["README.md"]),
             "gitBlobSha": _git_blob_sha(files["README.md"]),
             "sha256": "0" * 64},  # wrong per-file digest
        ]
        with self.assertRaises(ValueError):
            self._invoke_prepare(self._lock(archive, rows, []), archive)

    def test_archive_source_escape_rejected(self):
        with tempfile.TemporaryDirectory() as td:
            esc = pathlib.Path(td) / "escape.tar.gz"
            with tarfile.open(esc, "w:gz") as tf:
                info = tarfile.TarInfo("strata-v0.2.2/../../etc/passwd")
                info.size = 3
                tf.addfile(info, io.BytesIO(b"abc"))
            archive = esc.read_bytes()
            lock = self._lock(archive, [], [])
            with self.assertRaises(ValueError):
                self._invoke_prepare(lock, archive)

    def test_archive_incomplete_closure_rejected(self):
        root = "strata-v0.2.2"
        files = {"README.md": b"# hello\n"}
        archive = _make_donor_tar(root, files)
        # Lock expects TWO members but the archive has only one -> incomplete immutable donor closure.
        rows = [
            {"path": "README.md", "bytes": len(files["README.md"]),
             "gitBlobSha": _git_blob_sha(files["README.md"]),
             "sha256": hashlib.sha256(files["README.md"]).hexdigest()},
            {"path": "MISSING.txt", "bytes": 3,
             "gitBlobSha": _git_blob_sha(b"abc"), "sha256": hashlib.sha256(b"abc").hexdigest()},
        ]
        with self.assertRaises(ValueError):
            self._invoke_prepare(self._lock(archive, rows, []), archive)


class GgmlCheckoutVerifierControls(unittest.TestCase):
    """--ggml-dir must require a real git work tree pinned to the lock commit/tree, clean closed."""

    def setUp(self):
        self.mod = _build_package_module()
        self.td = tempfile.TemporaryDirectory(prefix="thunder-ggml-")
        self.repo = pathlib.Path(self.td.name) / "llama.cpp"
        self.repo.mkdir()
        subprocess.run(["git", "init", "-q", str(self.repo)], check=True)
        subprocess.run(["git", "-C", str(self.repo), "config", "user.email", "ci@test"], check=True)
        subprocess.run(["git", "-C", str(self.repo), "config", "user.name", "CI"], check=True)
        (self.repo / "CMakeLists.txt").write_text("project(llama)\n")
        subprocess.run(["git", "-C", str(self.repo), "add", "CMakeLists.txt"], check=True)
        subprocess.run(["git", "-C", str(self.repo), "commit", "-qm", "init"], check=True)

    def tearDown(self):
        self.td.cleanup()

    def _head_tree(self):
        head = subprocess.run(["git", "-C", str(self.repo), "rev-parse", "HEAD"],
                              check=True, capture_output=True, text=True).stdout.strip()
        tree = subprocess.run(["git", "-C", str(self.repo), "rev-parse", "HEAD^{tree}"],
                              check=True, capture_output=True, text=True).stdout.strip()
        return head, tree

    def test_clean_pinned_checkout_accepted(self):
        head, tree = self._head_tree()
        result = self.mod.verify_ggml_checkout(self.repo, {"pinnedCommit": head, "tree": tree})
        self.assertTrue(result["clean"])
        self.assertEqual(result["pinnedCommit"], head)

    def test_dirty_checkout_rejected(self):
        head, tree = self._head_tree()
        (self.repo / "uncommitted.txt").write_text("x")
        with self.assertRaises(ValueError):
            self.mod.verify_ggml_checkout(self.repo, {"pinnedCommit": head, "tree": tree})

    def test_wrong_head_rejected(self):
        _, tree = self._head_tree()
        with self.assertRaises(ValueError):
            self.mod.verify_ggml_checkout(self.repo, {"pinnedCommit": "0" * 40, "tree": tree})

    def test_bad_marker_listing_rejected(self):
        head, tree = self._head_tree()
        (self.repo / "CMakeLists.txt").unlink()
        with self.assertRaises(ValueError):
            self.mod.verify_ggml_checkout(self.repo, {"pinnedCommit": head, "tree": tree})

    def test_non_git_directory_with_marker_rejected(self):
        head, tree = self._head_tree()
        fake = self.repo / "not-a-repo"
        fake.mkdir()
        (fake / "CMakeLists.txt").write_text("project(llama)\n")
        with self.assertRaises(ValueError):
            self.mod.verify_ggml_checkout(fake, {"pinnedCommit": head, "tree": tree})

    def test_subdirectory_inheriting_parent_repo_rejected(self):
        # A subdir under this git repo that merely contains a COakeLists marker inherits the parent
        # repo; its --show-toplevel != approved root, so it must be refused.
        head, tree = self._head_tree()
        sub = self.repo / "third_party" / "ggml"
        sub.mkdir(parents=True)
        (sub / "CMakeLists.txt").write_text("project(llama)\n")
        with self.assertRaises(ValueError):
            self.mod.verify_ggml_checkout(sub, {"pinnedCommit": head, "tree": tree})


class ThunderPathPrepControls(unittest.TestCase):
    """Runs the bounded shell fixture for the production path-preparation function (no network/SDK)."""

    def test_thunder_path_prep_fixture(self):
        fixture = pathlib.Path(__file__).resolve().parent / "NativeStrataThunderPathFixture.sh"
        proc = subprocess.run(["bash", str(fixture)], capture_output=True, text=True)
        self.assertEqual(proc.returncode, 0, "stderr: " + proc.stderr)
        self.assertIn("THUNDER-PATH-PREP-FIXTURE-OK", proc.stdout)


class ThunderInventoryFixture(unittest.TestCase):
    """Runs the bounded shell fixture for the production install-closure inventory function
    (non-executed file-metadata trees; no binary/runtime/GPU claim)."""

    def test_thunder_inventory_fixture(self):
        fixture = pathlib.Path(__file__).resolve().parent / "NativeStrataThunderInventoryFixture.sh"
        proc = subprocess.run(["bash", str(fixture)], capture_output=True, text=True)
        self.assertEqual(proc.returncode, 0, "stderr: " + proc.stderr)
        self.assertIn("THUNDER-INVENTORY-FIXTURE-OK", proc.stdout)


class ThunderShellFailureJsonFixture(unittest.TestCase):
    """Runs the bounded shell fixture proving the EXIT-trap shell-only fallback emits parseable JSON
    when python3 is hidden (no network/SDK/apt)."""

    def test_thunder_shell_failure_json_fixture(self):
        fixture = pathlib.Path(__file__).resolve().parent / "NativeStrataThunderShellFailureJsonFixture.sh"
        proc = subprocess.run(["bash", str(fixture)], capture_output=True, text=True)
        self.assertEqual(proc.returncode, 0, "stderr: " + proc.stderr)
        self.assertIn("THUNDER-SHELL-FAILURE-JSON-FIXTURE-OK", proc.stdout)


class ThunderPythonBootstrapFixture(unittest.TestCase):
    """Runs the bounded shell fixture proving the bootstrap selects FULL python3 and verifies stdlib
    before preflight, including the present-but-incomplete repair path (no network/SDK/apt)."""

    def test_thunder_python_bootstrap_fixture(self):
        fixture = pathlib.Path(__file__).resolve().parent / "NativeStrataThunderPythonBootstrapFixture.sh"
        proc = subprocess.run(["bash", str(fixture)], capture_output=True, text=True)
        self.assertEqual(proc.returncode, 0, "stderr: " + proc.stderr)
        self.assertIn("THUNDER-PYTHON-BOOTSTRAP-FIXTURE-OK", proc.stdout)


if __name__ == "__main__":
    unittest.main()
