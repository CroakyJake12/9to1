"""Actual packaging predicate controls; no binary build or CUDA proof."""
import base64
import hashlib
import importlib.util
import io
import json
import os
import pathlib
import shutil
import struct
import subprocess
import sys
import tarfile
import tempfile
import unittest

TQ_FIX = pathlib.Path(__file__).resolve().parent / "NativeStrataEOLFixtures" / "tq-vendor-bodies.json"


def _tq_bodies():
    return {p: base64.b64decode(b) for p, b in json.loads(TQ_FIX.read_text()).items()}


def _tq_rows():
    return [
        {"path": p, "bytes": len(b), "gitBlobSha": _git_blob_sha(b),
         "sha256": hashlib.sha256(b).hexdigest()}
        for p, b in sorted(_tq_bodies().items())
    ]


def _build_donor_tar_with_tq(archive_bytes):
    """Rebuild a tiny donor tar with the two REAL pinned trellis vendor members added.

    The candidate build preparation requires `third_party/ggml-tq/{tq,tq-mmq-tiles}.cuh`
    (the embedded source-root relink custody), so the synthetic candidate fixtures carry them.
    """
    members = {}
    with tarfile.open(fileobj=io.BytesIO(archive_bytes), mode="r:gz") as tf:
        for m in tf.getmembers():
            if m.isfile():
                members[m.name] = tf.extractfile(m).read()
    for rel, body in _tq_bodies().items():
        members["strata-v0.2.2/" + rel] = body
    buf = io.BytesIO()
    with tarfile.open(fileobj=buf, mode="w:gz") as tf:
        rootinfo = tarfile.TarInfo("strata-v0.2.2/")
        rootinfo.type = tarfile.DIRTYPE
        tf.addfile(rootinfo)
        for name, body in members.items():
            info = tarfile.TarInfo(name)
            info.size = len(body)
            tf.addfile(info, io.BytesIO(body))
    return buf.getvalue()

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

    def _invoke_prepare(self, lock, archive_bytes, *, profile="candidate", lock_edit=None, expect_no_work=False):
        if profile == "candidate":
            archive_bytes = _build_donor_tar_with_tq(archive_bytes)
        rows = list(lock["rows"]) + (_tq_rows() if profile == "candidate" else [])
        lock = dict(lock)
        lock["rows"] = rows
        lock["blobCount"] = len(rows)
        if lock_edit is not None:
            lock_edit(lock)
        lock["archive"] = {"path": None, "bytes": len(archive_bytes),
                           "sha256": hashlib.sha256(archive_bytes).hexdigest()}
        with tempfile.TemporaryDirectory(prefix="thunder-verif-") as td:
            lpath = pathlib.Path(td) / "lock.json"
            lpath.write_text(json.dumps(lock))
            apath = pathlib.Path(td) / "donor.tar.gz"
            apath.write_bytes(archive_bytes)
            work = pathlib.Path(td) / "work"
            old = sys.argv[:]
            sys.argv = ["build-package.py", "--profile", profile,
                        "--lock", str(lpath), "--archive", str(apath),
                        "--work", str(work),
                        "--cmake", str(pathlib.Path(td) / "unavailable-cmake"),
                        "--nvcc", str(pathlib.Path(td) / "unavailable-nvcc")]
            try:
                _build_package_module().main()
            finally:
                sys.argv = old
                if expect_no_work:
                    self.assertFalse(work.exists(), "Invalid lock must refuse before extraction creates work")
            rec = json.loads((work / "source-verification.json").read_text())
            # Path-custody regression: the candidate embedded build must expose the pinned
            # trellis vendor closure at the fork root (top-level CMake source dir) so the
            # upstream overlay's `third_party/ggml-tq` resolves; verify the symlink + real bodies.
            fork_link = work / "dulche-fork" / "third_party" / "ggml-tq"
            if profile == "candidate":
                self.assertTrue(fork_link.is_symlink(), "candidate prepare must expose fork third_party/ggml-tq")
                self.assertTrue((fork_link / "tq.cuh").is_file())
                self.assertTrue((fork_link / "tq-mmq-tiles.cuh").is_file())
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

    def test_duplicate_lock_rows_rejected_before_extraction(self):
        body = b"source\n"
        archive = _make_donor_tar("strata-v0.2.2", {"README.md": body})
        row = {"path": "README.md", "bytes": len(body), "gitBlobSha": _git_blob_sha(body),
               "sha256": hashlib.sha256(body).hexdigest()}
        for conflicting in (False, True):
            with self.subTest(conflicting=conflicting):
                def duplicate(lock):
                    shadow = dict(lock["rows"][0])
                    if conflicting:
                        shadow["sha256"] = "0" * 64
                    lock["rows"].insert(0, shadow)
                    # Keep blobCount at the unique path count: the old dict-comprehension
                    # hid this earlier row and admitted/extracted the otherwise valid archive.
                with self.assertRaisesRegex(ValueError, "Duplicate source-lock path"):
                    self._invoke_prepare(self._lock(archive, [row], []), archive,
                                         lock_edit=duplicate, expect_no_work=True)

    def test_invalid_blob_count_rejected_before_extraction(self):
        body = b"source\n"
        archive = _make_donor_tar("strata-v0.2.2", {"README.md": body})
        row = {"path": "README.md", "bytes": len(body), "gitBlobSha": _git_blob_sha(body),
               "sha256": hashlib.sha256(body).hexdigest()}
        # Three unique candidate members after the real TQ closure is added. 3.0
        # compared equal to len(verified) before the strict integer admission.
        for count in (None, False, True, "3", 3.0, 2, 4):
            with self.subTest(count=count, kind=type(count).__name__):
                with self.assertRaisesRegex(ValueError, "Invalid source-lock blobCount"):
                    self._invoke_prepare(self._lock(archive, [row], []), archive,
                        lock_edit=lambda lock: lock.update(blobCount=count), expect_no_work=True)

    def test_boolean_blob_count_rejected_in_single_member_legacy_lock(self):
        body = b"source\n"
        archive = _make_donor_tar("strata-v0.2.2", {"README.md": body})
        row = {"path": "README.md", "bytes": len(body), "gitBlobSha": _git_blob_sha(body),
               "sha256": hashlib.sha256(body).hexdigest()}
        with self.assertRaisesRegex(ValueError, "Invalid source-lock blobCount"):
            self._invoke_prepare(self._lock(archive, [row], []), archive, profile="legacy",
                lock_edit=lambda lock: lock.update(blobCount=True), expect_no_work=True)

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
             "gitBlobSha": _git_blob_sha(files["BENCH.bat"]),
             "treeBlobSha": _git_blob_sha(files["BENCH.bat"].replace(b"\r\n", b"\n")),
             "sha256": hashlib.sha256(files["BENCH.bat"]).hexdigest()},
        ]
        rec, extracted = self._invoke_prepare(self._lock(archive, rows, ["BENCH.bat"]), archive)
        self.assertEqual(rec["verifiedFiles"], 4)  # README + BENCH + 2 trellis vendor members
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

    def test_declared_canonical_blob_mismatch_rejected(self):
        # Declared official name with CRLF and correct raw archive blob, but the canonical tree blob
        # does not match the LF-normalized body -> must be refused (exact explained conversion required).
        root = "strata-v0.2.2"
        files = {"BENCH.bat": b"@echo off\r\necho hi\r\n"}
        archive = _make_donor_tar(root, files)
        rows = [
            {"path": "BENCH.bat", "bytes": len(files["BENCH.bat"]),
             "gitBlobSha": _git_blob_sha(files["BENCH.bat"]),
             "treeBlobSha": "a" * 40,  # wrong canonical tree blob
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

    def test_relative_work_path_relink_resolves_real_bodies(self):
        # Production prepare driven from a controlled cwd with a RELATIVE --work path. This is the
        # demonstrated CLI bug: the old symlink stored a relative target (work/upstream/...), which
        # resolved relative to work/dulche-fork/third_party and dangled. The corrected production
        # prepare normalizes the verified vendor directory to absolute, so BOTH real pinned tq bodies
        # resolve through the fork alias with their exact SHA-256 values.
        tq = _tq_bodies()
        files = {"README.md": b"# hello\n"}
        archive = _build_donor_tar_with_tq(_make_donor_tar("strata-v0.2.2", files))
        rows = [
            {"path": "README.md", "bytes": len(files["README.md"]),
             "gitBlobSha": _git_blob_sha(files["README.md"]),
             "sha256": hashlib.sha256(files["README.md"]).hexdigest()},
        ] + _tq_rows()
        lock = self._lock(archive, rows, [])
        with tempfile.TemporaryDirectory(prefix="thunder-relwork-") as td:
            lpath = pathlib.Path(td) / "lock.json"
            lpath.write_text(json.dumps(lock))
            apath = pathlib.Path(td) / "donor.tar.gz"
            apath.write_bytes(archive)
            old_cwd = os.getcwd()
            old_argv = sys.argv[:]
            try:
                os.chdir(td)
                sys.argv = ["build-package.py", "--profile", "candidate",
                            "--lock", str(lpath), "--archive", str(apath),
                            "--work", "work"]
                _build_package_module().main()
            finally:
                sys.argv = old_argv
                os.chdir(old_cwd)
            link = pathlib.Path(td) / "work" / "dulche-fork" / "third_party" / "ggml-tq"
            self.assertTrue(link.is_symlink())
            # The alias target must be the canonical ABSOLUTE upstream vendor directory.
            self.assertTrue(pathlib.Path(link.readlink()).is_absolute())
            for rel, body in tq.items():
                member = rel.split("/")[-1]
                with self.subTest(member=member):
                    self.assertEqual(hashlib.sha256((link / member).read_bytes()).hexdigest(),
                                     hashlib.sha256(body).hexdigest())


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


class InstallIdentityParityControls(unittest.TestCase):
    """Real build/install byte-parity controls for the actual packaging closure.

    The accepted contract is that every installed native artifact (worker, engine, optional
    K-quant MMQ DSO) is byte-for-byte the exact inspected build output. A post-install RPATH
    rewrite (CMake "Set non-toolchain portion of runtime path ... to ''") changes the installed ELF
    bytes and must be rejected, not silently accepted. These are functional predicates over the real
    production helper, not source-string mirrors.
    """

    def setUp(self):
        self.mod = _build_package_module()
        self.td = tempfile.TemporaryDirectory(prefix="thunder-parity-")
        self.built = pathlib.Path(self.td.name) / "built.bin"
        self.installed = pathlib.Path(self.td.name) / "installed.bin"
        payload = b"\x7fELF" + b"\x00" * 60 + b"payload"
        self.built.write_bytes(payload)
        self.installed.write_bytes(payload)

    def tearDown(self):
        self.td.cleanup()

    def test_identical_build_install_accepted(self):
        self.mod.verify_installed_identical(self.built, self.installed, "worker")

    def test_missing_installed_rejected(self):
        self.installed.unlink()
        with self.assertRaises(ValueError):
            self.mod.verify_installed_identical(self.built, self.installed, "worker")

    def test_missing_built_rejected(self):
        self.built.unlink()
        with self.assertRaises(ValueError):
            self.mod.verify_installed_identical(self.built, self.installed, "worker")

    def test_installed_differs_from_built_rejected(self):
        # The exact defect pattern: the install produced a different binary (e.g. RPATH scrubbed).
        self.installed.write_bytes(self.built.read_bytes() + b"\x01")
        with self.assertRaises(ValueError):
            self.mod.verify_installed_identical(self.built, self.installed, "worker")

    def test_installed_empty_rejected(self):
        self.installed.write_bytes(b"")
        with self.assertRaises(ValueError):
            self.mod.verify_installed_identical(self.built, self.installed, "worker")

    def test_error_message_names_the_artifact(self):
        with self.assertRaisesRegex(ValueError, "K-quant MMQ DSO"):
            self.mod.verify_installed_identical(self.built, self.built.parent / "nope", "K-quant MMQ DSO")


class PackagingConfigParityControls(unittest.TestCase):
    """Configuration guards that enforce the build/install parity fix and the optional K-quant MMQ
    closure install. These guard the CMake/driver wiring; the behavioral parity is exercised by
    InstallIdentityParityControls and the CI-native RpathParityBuildFixture."""

    def _cmake_src(self):
        path = pathlib.Path(__file__).resolve().parents[1] / "InferenceEngines" / "NativeStrata" / "CMakeLists.txt"
        return path.read_text()

    def test_cmake_builds_with_install_rpath(self):
        src = self._cmake_src()
        self.assertIn("CMAKE_BUILD_WITH_INSTALL_RPATH", src)
        self.assertIn("set(CMAKE_BUILD_WITH_INSTALL_RPATH ON)", src)

    def test_cmake_installs_kq_dso_beside_engine(self):
        src = self._cmake_src()
        self.assertIn("TARGET strata_kq", src)
        self.assertIn("strata_kq", src)
        self.assertIn("runtimes/${_strata_target_dir}/engine", src)

    def test_build_package_verifies_engine_and_kq_parity_and_closure(self):
        src = pathlib.Path(__file__).resolve().parents[1] / "InferenceEngines" / "NativeStrata" / "build-package.py"
        text = src.read_text()
        self.assertIn("verify_installed_identical", text)
        self.assertIn("Installed engine binary is missing", text)
        self.assertIn("K-quant MMQ DSO", text)
        self.assertIn("kqDsoSha256", text)
        self.assertIn("kqDsoBytes", text)


class RpathParityBuildFixture(unittest.TestCase):
    """A SMALL REAL CMake RPATH fixture proving the inspected BUILD artifact is byte-identical to the
    INSTALLED artifact under CMAKE_BUILD_WITH_INSTALL_RPATH=ON.

    NOTRUN locally: this environment has no local cmake/native toolchain (source-only worker). It
    runs in the supported CI only when the harness explicitly opts in via
    THUNDER_RUN_NATIVE_FIXTURE=1. It never downloads anything and builds only the tiny fixture.
    """

    FIXTURE = "NativeStrataRpathParityFixture"

    def test_build_install_byte_parity(self):
        if os.environ.get("THUNDER_RUN_NATIVE_FIXTURE") != "1":
            self.skipTest("NOTRUN: native CMake fixture requires CI (no local cmake/native)")
        if shutil.which("cmake") is None:
            self.skipTest("NOTRUN: cmake unavailable")
        fixture = pathlib.Path(__file__).resolve().parent / self.FIXTURE
        with tempfile.TemporaryDirectory(prefix="thunder-rpath-fixture-") as td:
            td = pathlib.Path(td)
            build = td / "build"
            install = td / "install"
            subprocess.run(["cmake", "-S", str(fixture), "-B", str(build),
                            "-DCMAKE_INSTALL_PREFIX=" + str(install)], check=True)
            subprocess.run(["cmake", "--build", str(build)], check=True)
            subprocess.run(["cmake", "--install", str(build)], check=True)
            built = build / "rpath_probe"
            installed = install / "bin" / "rpath_probe"
            self.assertTrue(built.is_file())
            self.assertTrue(installed.is_file())
            self.assertEqual(built.read_bytes(), installed.read_bytes(),
                             "installed probe must be byte-identical to the built probe")


class TargetArtifactResolutionControls(unittest.TestCase):
    """Functional, deterministic resolution of ACTUAL built artifacts from declared CMake target
    output locations (included-upstream layout for the engine/DSO; build root for the relay worker).

    These exercise the production resolver with real filesystem layouts: single-config upstream
    (`build/upstream/strata`), Windows-style multi-config (`build/upstream/<CONFIG>/strata`), build
    root worker, and the `libstrata_kq.so` DSO. Ambiguous and missing outputs must be rejected, not
    resolved by a free first scan (no ordinary filename-string mirror).
    """

    def setUp(self):
        self.mod = _build_package_module()
        self.td = tempfile.TemporaryDirectory(prefix="thunder-targetres-")
        self.build = pathlib.Path(self.td.name) / "build"
        self.build.mkdir()

    def tearDown(self):
        self.td.cleanup()

    def _touch(self, rel):
        p = self.build / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(b"ELF" + rel.encode())
        return p

    def test_engine_resolves_in_included_upstream_layout(self):
        # add_subdirectory(upstream EXCLUDE_FROM_ALL) -> single-config engine at build/upstream/strata.
        engine = self._touch("upstream/strata")
        got = self.mod.resolve_built_artifact(self.build, "strata", ["upstream"], "engine")
        self.assertEqual(got, engine)

    def test_multi_config_engine_resolves_under_upstream_config(self):
        # Windows multi-config layout: no build/upstream/strata, only build/upstream/Release/strata.
        cfg = self._touch("upstream/Release/strata")
        got = self.mod.resolve_built_artifact(self.build, "strata", ["upstream"], "engine")
        self.assertEqual(got, cfg)

    def test_worker_resolves_at_buildroot(self):
        w = self._touch("dulche-strata-candidate-worker")
        got = self.mod.resolve_built_artifact(self.build, "dulche-strata-candidate-worker", ["."], "worker")
        self.assertEqual(got, w)

    def test_dso_resolves_in_upstream(self):
        d = self._touch("upstream/libstrata_kq.so")
        got = self.mod.resolve_built_artifact(self.build, "libstrata_kq.so", ["upstream"], "K-quant MMQ DSO")
        self.assertEqual(got, d)

    def test_ambiguous_target_rejected(self):
        # Both single-config and multi-config outputs present -> ambiguous, never silently pick one.
        self._touch("upstream/strata")
        self._touch("upstream/Release/strata")
        with self.assertRaises(ValueError):
            self.mod.resolve_built_artifact(self.build, "strata", ["upstream"], "engine")

    def test_missing_target_rejected(self):
        with self.assertRaises(ValueError):
            self.mod.resolve_built_artifact(self.build, "strata", ["upstream"], "engine")

    def test_not_a_free_first_scan(self):
        # An unrelated stale binary elsewhere must NOT satisfy the resolver (no rglob/name-only match).
        self._touch("other/libstrata_kq.so")
        with self.assertRaises(ValueError):
            self.mod.resolve_built_artifact(self.build, "libstrata_kq.so", ["upstream"], "K-quant MMQ DSO")


if __name__ == "__main__":
    unittest.main()
