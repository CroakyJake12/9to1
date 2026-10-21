"""Offline, bounded tests for `thunder-artifact-audit.py` (PRIMARY38 fixes).

These exercise the REAL audit functions/CLI on small, genuine filesystem fixtures. The GitHub API
transport, disk/memory probes, and the `readelf` reader are all injected; no test claims a real ELF
was inspected and none reach the network. The real immutable candidate lock is HARDLINKED into fixture
closures (read-only, never modified) so the installed source-lock pin is faithful. MIT notice pins are
separately asserted as production constants and are injected in closure-structure tests to isolate the
structural logic (the production default is never weakened). Fixtures are workspace-scoped; all cases
are bounded far under 30s.
"""
import hashlib
import importlib.util
import json
import os
import pathlib
import subprocess
import sys
import tempfile
import unittest

HERE = pathlib.Path(__file__).resolve().parent
REPO_ROOT = HERE.parents[2]
MOD_PATH = REPO_ROOT / ".github" / "scripts" / "thunder-artifact-audit.py"
REAL_CANDIDATE_LOCK = (REPO_ROOT /
    "9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/prep/strata-v0.2.2-lock.candidate.json")
REAL_GGML_MIT = (REPO_ROOT /
    "9to1 Workspace/Home/Source/Dulche/llamacpp/licenses/llama.cpp-LICENSE")

# Documented independent production values (from CI32 evidence / independent inventory).
CANDIDATE_LOCK_SHA256 = "9952dc3f5bb79c3cd44e831b94039ba564fdc600e21a0317dcecc3e110eeaebf"
MIT_STRATA_SHA256 = "949b9b0aada7476006469e58ee4ffb0e0f68c2d9137e90a356757b61a09d37f0"
MIT_GGML_SHA256 = "94f29bbed6a22c35b992c5c6ebf0e7c92f13b836b90f36f461c9cf2f0f1d010d"
GGML_COMMIT = "3cf03257f219afbe7334045ff7c6a06ac68c627d"
GGML_TREE = "d255198f04f9b8349f1dff24501d513f83cbfeda"
ARCHIVE_SHA256 = "ccfffeaabfb410b734b95d202576b98b3d2f2d28080a69ea8a3c49268d95c93a"

HEAD_SHA = "0749524b904a3b8035d68b2915197293c87fbc11"
RUN_ID = "39660364662"
EVIDENCE_AID = "12377235812"
INSTALL_AID = "12377275853"
REPO = "CroakyJake12/9to1"
REPO_ID = 1378313608


def load_module():
    spec = importlib.util.spec_from_file_location("thunder_artifact_audit", MOD_PATH)
    mod = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = mod
    spec.loader.exec_module(mod)
    return mod


def _sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


ELF_STDOUT = """ELF Header:
  Class:                             ELF64
  Machine:                           Advanced Micro Devices X86-64
  Type:                              EXEC
Program Headers:
  INTERP         0x0000000000000318 0x0000000000400318 0x0000000000400318
                 0x000000000000001c 0x000000000000001c  R      1
      [Requesting program interpreter: /lib64/ld-linux-x86-64.so.2]
Dynamic section at offset 0x2de0 contains 30 entries:
  0x0000000000000001 (NEEDED)             Shared library: [libc.so.6]
  0x0000000000000001 (NEEDED)             Shared library: [libcudart.so.13]
  0x0000000000000001 (NEEDED)             Shared library: [libcuda.so.1]
  0x000000000000000f (RPATH)              Library rpath: [$ORIGIN/../lib]
  0x000000000000001d (RUNPATH)            Library runpath: [$ORIGIN]
"""


class AuditFixture:
    def __init__(self, td: pathlib.Path):
        self.td = td
        self.install = td / "install"
        self.evidence = td / "evidence"
        self.install.mkdir()
        self.evidence.mkdir()
        self.files = {}  # rel -> content bytes (source-lock is hardlinked, not stored here)

    def add(self, rel, content: bytes):
        p = self.install / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(content)
        self.files[rel] = content

    def add_source_lock_hardlink(self):
        """Hardlink the REAL immutable candidate lock (read-only, never modified)."""
        os.link(REAL_CANDIDATE_LOCK, self.install / "strata-source-lock.json")

    def add_source_lock_tampered(self):
        """Copy then mutate the real lock (never touches the real file)."""
        data = REAL_CANDIDATE_LOCK.read_bytes()
        (self.install / "strata-source-lock.json").write_bytes(data + b"\ntampered")

    def build_default_closure(self, tamper_source_lock=False):
        strata_mit = b"Strata MIT notice (fixture)\n"
        self.add("licenses/MIT-STRATA.txt", strata_mit)
        self.add("licenses/MIT-GGML.txt", REAL_GGML_MIT.read_bytes())
        self.add("licenses/strata/LICENSE", strata_mit)
        self.add("licenses/strata/ggml/LICENSE", REAL_GGML_MIT.read_bytes())
        self.add("runtimes/linux-x64/engine/strata", b"ELFENGINE")
        self.add("runtimes/linux-x64/engine/libstrata_kq.so", b"ELFKQ")
        self.add("runtimes/linux-x64/native/dulche-strata-candidate-worker", b"ELFWORKER")
        self.add("runtimes/linux-x64/native/strata_tokenizer_helper.py", b"# helper\n")
        self.add("runtimes/linux-x64/native/strata_tokenizer.py", b"# tokenizer\n")
        self.add("runtimes/linux-x64/native/gguf_reader.py", b"# gguf\n")
        self.add("runtimes/linux-x64/native/strata-runtime-dependencies.json",
                 json.dumps({"schema": "strata-runtime-dependencies/v1",
                             "runtimeModules": {"a": 1}}).encode())
        if tamper_source_lock:
            self.add_source_lock_tampered()
        else:
            self.add_source_lock_hardlink()
        self.write_receipt()
        return self.make_inventory()

    def make_inventory(self):
        rows = []
        for p in sorted(self.install.rglob("*")):
            if p.is_file() and not p.is_symlink():
                rel = p.relative_to(self.install).as_posix()
                data = p.read_bytes()
                rows.append({"path": rel, "bytes": len(data), "sha256": _sha(data)})
        return rows

    def _relsha(self, rel):
        p = self.install / rel
        return _sha(p.read_bytes()) if p.exists() else None

    def _rellen(self, rel):
        p = self.install / rel
        return len(p.read_bytes()) if p.exists() else None

    def write_receipt(self, profile="candidate", commit=None, tree=None, archive=None,
                      lock_sha=CANDIDATE_LOCK_SHA256, target_ok=True, engine_sha=None,
                      worker_sha=None, helper_ok=True, deps_sha=None):
        if commit is None:
            commit = "011c9e600a93bbc9fd451c563501b5831c32bbca"
        if tree is None:
            tree = "539755e6d6b799bd75a47225befc786c436f6d8b"
        if archive is None:
            archive = ARCHIVE_SHA256
        helpers = {
            "strata_tokenizer_helper.py": {"bytes": self._rellen("runtimes/linux-x64/native/strata_tokenizer_helper.py"),
                                           "sha256": self._relsha("runtimes/linux-x64/native/strata_tokenizer_helper.py")},
            "strata_tokenizer.py": {"bytes": self._rellen("runtimes/linux-x64/native/strata_tokenizer.py"),
                                    "sha256": self._relsha("runtimes/linux-x64/native/strata_tokenizer.py")},
            "gguf_reader.py": {"bytes": self._rellen("runtimes/linux-x64/native/gguf_reader.py"),
                               "sha256": self._relsha("runtimes/linux-x64/native/gguf_reader.py")},
            "strata-runtime-dependencies.json": {"bytes": self._rellen("runtimes/linux-x64/native/strata-runtime-dependencies.json"),
                                                 "sha256": self._relsha("runtimes/linux-x64/native/strata-runtime-dependencies.json")},
        }
        rec = {
            "profile": profile,
            "target": {"elfClass": 64, "endianness": "little", "machine": 62,
                       "targetSystem": "linux-x64"} if target_ok else "not-a-dict",
            "lockSha256": lock_sha,
            "commit": commit,
            "tree": tree,
            "archiveSha256": archive,
            "engineSha256": engine_sha or self._relsha("runtimes/linux-x64/engine/strata"),
            "engineBytes": self._rellen("runtimes/linux-x64/engine/strata"),
            "workerSha256": worker_sha or self._relsha("runtimes/linux-x64/native/dulche-strata-candidate-worker"),
            "workerBytes": self._rellen("runtimes/linux-x64/native/dulche-strata-candidate-worker"),
            "kqDsoSha256": self._relsha("runtimes/linux-x64/engine/libstrata_kq.so"),
            "kqDsoBytes": self._rellen("runtimes/linux-x64/engine/libstrata_kq.so"),
            "helperClosure": helpers if helper_ok else {},
            "runtimeDepsLockSha256": deps_sha or self._relsha("runtimes/linux-x64/native/strata-runtime-dependencies.json"),
        }
        (self.install / "strata-build-receipt.json").write_text(json.dumps(rec) + "\n")

    def write_evidence(self, inventory, success=True, engine_version="strata 0.1.47",
                       archive=ARCHIVE_SHA256, ggml_commit=GGML_COMMIT, ggml_tree=GGML_TREE,
                       engine_sha=None, worker_sha=None):
        (self.evidence / "install-closure-inventory.json").write_text(json.dumps(inventory))
        (self.evidence / "thunder-cuda-compile-evidence.json").write_text(json.dumps({
            "success": success,
            "engineVersion": engine_version,
            "archiveSha256": archive,
            "ggmlCommit": ggml_commit,
            "ggmlTree": ggml_tree,
            "engineSha256": engine_sha or self._relsha("runtimes/linux-x64/engine/strata"),
            "engineBytes": self._rellen("runtimes/linux-x64/engine/strata"),
            "workerSha256": worker_sha or self._relsha("runtimes/linux-x64/native/dulche-strata-candidate-worker"),
            "workerBytes": self._rellen("runtimes/linux-x64/native/dulche-strata-candidate-worker"),
            "helperSha256": self._relsha("runtimes/linux-x64/native/strata_tokenizer_helper.py"),
        }))
        (self.evidence / "preflight-runner.json").write_text("{}")
        (self.evidence / "resource-limits.json").write_text("{}")


class FakeGH:
    """Injected GitHub API transport (numeric ids, repository identity), read-only canned metadata."""

    def __init__(self, run=None, evidence=None, install=None, repo_id=REPO_ID):
        self.repo_id = repo_id
        self.run = run if run is not None else {
            "id": int(RUN_ID), "status": "completed", "conclusion": "success",
            "head_sha": HEAD_SHA,
            "repository": {"id": repo_id}, "head_repository": {"id": repo_id}}
        self.evidence = evidence if evidence is not None else self._artifact(
            int(EVIDENCE_AID), "thunder-cuda-compile-evidence", 15091)
        self.install = install if install is not None else self._artifact(
            int(INSTALL_AID), "thunder-cuda-compile-install-closure", 42950082)

    def _artifact(self, aid, name, size):
        return {"id": aid, "name": name, "size_in_bytes": size, "expired": False,
                "workflow_run": {"id": int(RUN_ID), "repository_id": self.repo_id,
                                 "head_repository_id": self.repo_id, "head_sha": HEAD_SHA}}

    def __call__(self, path, token=""):
        if path.startswith("repos/") and "/actions/runs/" in path:
            return self.run
        if path.startswith("repos/") and "/actions/artifacts/" in path:
            if path.endswith("/" + EVIDENCE_AID):
                return self.evidence
            if path.endswith("/" + INSTALL_AID):
                return self.install
        raise AssertionError("unexpected API path: " + path)


def fake_readelf(binary: pathlib.Path):
    return 0, ELF_STDOUT, ""


def base_inputs(overrides=None):
    d = {"run_id": RUN_ID, "evidence_artifact_id": EVIDENCE_AID, "install_artifact_id": INSTALL_AID,
         "expected_head_sha": HEAD_SHA, "repo": REPO, "token": "tok"}
    if overrides:
        d.update(overrides)
    return d


def synthetic_mit_pins(mod):
    """Inject explicit fixture MIT constants so structural tests isolate the logic (production default
    holds the real, non-weakened pins which are asserted separately)."""
    strata = _sha(b"Strata MIT notice (fixture)\n")
    strata_len = len(b"Strata MIT notice (fixture)\n")
    ggml = _sha(REAL_GGML_MIT.read_bytes())
    ggml_len = len(REAL_GGML_MIT.read_bytes())
    return {
        "licenses/MIT-STRATA.txt": (strata, strata_len),
        "licenses/strata/LICENSE": (strata, strata_len),
        "licenses/MIT-GGML.txt": (ggml, ggml_len),
        "licenses/strata/ggml/LICENSE": (ggml, ggml_len),
    }


class ProductionConstantTests(unittest.TestCase):
    def setUp(self):
        self.mod = load_module()

    def test_production_constants_match_independent_evidence(self):
        m = self.mod
        self.assertEqual(m.CANDIDATE_LOCK_SHA256, CANDIDATE_LOCK_SHA256)
        self.assertEqual(m.MIT_STRATA_SHA256, MIT_STRATA_SHA256)
        self.assertEqual(m.MIT_GGML_SHA256, MIT_GGML_SHA256)
        self.assertEqual(m.GGML_COMMIT, GGML_COMMIT)
        self.assertEqual(m.GGML_TREE, GGML_TREE)
        self.assertEqual(m.CANDIDATE_ARCHIVE_SHA256, ARCHIVE_SHA256)
        self.assertEqual(m.CANDIDATE_ENGINE_VERSION, "strata 0.1.47")
        self.assertEqual(m.EXPECTED_ROW_COUNT, 13)
        self.assertEqual(len(m.EXPECTED_CLOSURE_SET), 13)

    def test_real_lock_hash_matches_pin(self):
        self.assertEqual(_sha(REAL_CANDIDATE_LOCK.read_bytes()), CANDIDATE_LOCK_SHA256)


class InputValidationTests(unittest.TestCase):
    def setUp(self):
        self.mod = load_module()

    def test_rejects_bad_run_id(self):
        issues = self.mod.validate_inputs(base_inputs({"run_id": "12x"}))
        self.assertTrue(any("run_id" in i for i in issues))

    def test_rejects_negative_artifact_id(self):
        issues = self.mod.validate_inputs(base_inputs({"install_artifact_id": "-5"}))
        self.assertTrue(any("install_artifact_id" in i for i in issues))

    def test_rejects_non_ascii_decimal(self):
        issues = self.mod.validate_inputs(base_inputs({"run_id": "١٢٣"}))
        self.assertTrue(any("run_id" in i for i in issues))

    def test_rejects_bad_sha(self):
        issues = self.mod.validate_inputs(base_inputs({"expected_head_sha": "not-hex"}))
        self.assertTrue(any("expected_head_sha" in i for i in issues))

    def test_rejects_bad_repo_shape(self):
        issues = self.mod.validate_inputs(base_inputs({"repo": "NoSlash"}))
        self.assertTrue(any("GITHUB_REPOSITORY" in i for i in issues))
        issues = self.mod.validate_inputs(base_inputs({"repo": "Owner/"}))
        self.assertTrue(any("GITHUB_REPOSITORY" in i for i in issues))


class MetadataTests(unittest.TestCase):
    def setUp(self):
        self.mod = load_module()

    def test_verify_ok(self):
        inp = base_inputs()
        inp["_http_json"] = FakeGH()
        run = self.mod.verify_metadata(inp)
        self.assertEqual(run["id"], int(RUN_ID))

    def test_rejects_wrong_run_head(self):
        fake = FakeGH(run={"id": int(RUN_ID), "status": "completed", "conclusion": "success",
                           "head_sha": "0" * 40,
                           "repository": {"id": REPO_ID}, "head_repository": {"id": REPO_ID}})
        inp = base_inputs(); inp["_http_json"] = fake
        with self.assertRaises(self.mod.AuditError):
            self.mod.verify_metadata(inp)

    def test_rejects_run_not_success(self):
        fake = FakeGH(run={"id": int(RUN_ID), "status": "completed", "conclusion": "failure",
                           "head_sha": HEAD_SHA,
                           "repository": {"id": REPO_ID}, "head_repository": {"id": REPO_ID}})
        inp = base_inputs(); inp["_http_json"] = fake
        with self.assertRaises(self.mod.AuditError):
            self.mod.verify_metadata(inp)

    def test_rejects_wrong_artifact_name(self):
        fake = FakeGH(evidence=FakeGH()._artifact(int(EVIDENCE_AID), "wrong-name", 15091))
        inp = base_inputs(); inp["_http_json"] = fake
        with self.assertRaises(self.mod.AuditError):
            self.mod.verify_metadata(inp)

    def test_rejects_artifact_id_mismatch(self):
        # returned artifact id differs from requested input id
        fake = FakeGH(evidence={"id": int(EVIDENCE_AID) + 1, "name": "thunder-cuda-compile-evidence",
                                "size_in_bytes": 15091, "expired": False,
                                "workflow_run": {"id": int(RUN_ID), "repository_id": REPO_ID,
                                                 "head_repository_id": REPO_ID, "head_sha": HEAD_SHA}})
        inp = base_inputs(); inp["_http_json"] = fake
        with self.assertRaises(self.mod.AuditError):
            self.mod.verify_metadata(inp)

    def test_rejects_wrong_repository_identity(self):
        fake = FakeGH()
        fake.run = {"id": int(RUN_ID), "status": "completed", "conclusion": "success",
                    "head_sha": HEAD_SHA,
                    "repository": {"id": REPO_ID}, "head_repository": {"id": 999}}
        inp = base_inputs(); inp["_http_json"] = fake
        with self.assertRaises(self.mod.AuditError):
            self.mod.verify_metadata(inp)

    def test_rejects_oversize_install(self):
        fake = FakeGH(install=FakeGH()._artifact(int(INSTALL_AID),
                                                 "thunder-cuda-compile-install-closure",
                                                 600 * 1024 * 1024))
        inp = base_inputs(); inp["_http_json"] = fake
        with self.assertRaises(self.mod.AuditError):
            self.mod.verify_metadata(inp)


class PreflightTests(unittest.TestCase):
    def setUp(self):
        self.mod = load_module()

    def test_preflight_passes_within_budget(self):
        inp = base_inputs()
        inp["_http_json"] = FakeGH()
        inp["_disk_free"] = lambda p: 1024 * 1024 * 1024
        inp["_avail_mem"] = lambda: 4 * 1024 * 1024 * 1024
        rec = self.mod.preflight(inp)
        self.assertEqual(rec["run"], int(RUN_ID))

    def test_preflight_rejects_low_disk(self):
        inp = base_inputs()
        inp["_http_json"] = FakeGH()
        inp["_disk_free"] = lambda p: 1024 * 1024
        inp["_avail_mem"] = lambda: 4 * 1024 * 1024 * 1024
        with self.assertRaises(self.mod.AuditError):
            self.mod.preflight(inp)


class ClosureSecurityTests(unittest.TestCase):
    def setUp(self):
        self.mod = load_module()
        self._td = tempfile.TemporaryDirectory(prefix="thunder-audit-", dir=HERE)
        self.addCleanup(self._td.cleanup)
        self.td = pathlib.Path(self._td.name)
        self.fx = AuditFixture(self.td)

    def test_valid_structure_passes_with_injected_mit(self):
        self.fx.build_default_closure()
        inventory = self.fx.make_inventory()
        issues = self.mod.validate_closure(self.fx.install, inventory, mit_pins=synthetic_mit_pins(self.mod))
        self.assertEqual(issues, [])

    def test_absolute_inventory_path_rejected(self):
        self.fx.build_default_closure()
        inventory = self.fx.make_inventory() + [{"path": "/etc/passwd", "bytes": 1, "sha256": "0" * 64}]
        issues = self.mod.validate_closure(self.fx.install, inventory, mit_pins=synthetic_mit_pins(self.mod))
        self.assertTrue(any("absolute" in i for i in issues))

    def test_parent_traversal_inventory_path_rejected(self):
        self.fx.build_default_closure()
        inventory = self.fx.make_inventory() + [{"path": "../escape.txt", "bytes": 1, "sha256": "0" * 64}]
        issues = self.mod.validate_closure(self.fx.install, inventory, mit_pins=synthetic_mit_pins(self.mod))
        self.assertTrue(any("traversal" in i for i in issues))

    def test_backslash_inventory_path_rejected(self):
        self.fx.build_default_closure()
        inventory = self.fx.make_inventory() + [{"path": "a\\b", "bytes": 1, "sha256": "0" * 64}]
        issues = self.mod.validate_closure(self.fx.install, inventory, mit_pins=synthetic_mit_pins(self.mod))
        self.assertTrue(any("backslash" in i for i in issues))

    def test_duplicate_inventory_row_rejected(self):
        self.fx.build_default_closure()
        inventory = self.fx.make_inventory()
        inventory.append(dict(inventory[0]))
        issues = self.mod.validate_closure(self.fx.install, inventory, mit_pins=synthetic_mit_pins(self.mod))
        self.assertTrue(any("duplicate" in i for i in issues))

    def test_missing_expected_file_fails(self):
        self.fx.build_default_closure()
        (self.fx.install / "runtimes/linux-x64/native/strata_tokenizer_helper.py").unlink()
        inventory = self.fx.make_inventory()
        issues = self.mod.validate_closure(self.fx.install, inventory, mit_pins=synthetic_mit_pins(self.mod))
        self.assertTrue(any("missing expected closure file" in i for i in issues))

    def test_extra_file_fails(self):
        self.fx.build_default_closure()
        inventory = self.fx.make_inventory()
        (self.fx.install / "extra.bin").write_bytes(b"extra")
        issues = self.mod.validate_closure(self.fx.install, inventory, mit_pins=synthetic_mit_pins(self.mod))
        self.assertTrue(any("extra file not in expected closure" in i for i in issues))

    def test_member_symlink_rejected_before_hash(self):
        self.fx.build_default_closure()
        outside = self.td / "outside.bin"
        outside.write_bytes(b"outside")
        ln = self.fx.install / "runtimes/linux-x64/native/link.bin"
        ln.symlink_to(outside)
        inventory = self.fx.make_inventory()
        issues = self.mod.validate_closure(self.fx.install, inventory, mit_pins=synthetic_mit_pins(self.mod))
        self.assertTrue(any("symlink" in i for i in issues))

    def test_root_symlink_rejected(self):
        # Isolated symlink-root scenario (never mutates the shared fixture install dir).
        base = self.td / "symlinkcase"
        real = base / "real"
        real.mkdir(parents=True)
        ln = base / "install"
        ln.symlink_to(real)
        issues = self.mod.validate_closure(ln, [], mit_pins=synthetic_mit_pins(self.mod))
        self.assertTrue(any("root is a symlink" in i for i in issues))

    def test_cumulative_size_cap(self):
        self.fx.build_default_closure()
        old = self.mod.INSTALL_CAP_BYTES
        self.mod.INSTALL_CAP_BYTES = 100
        try:
            issues = self.mod.validate_closure(self.fx.install, self.fx.make_inventory(),
                                               mit_pins=synthetic_mit_pins(self.mod))
            self.assertTrue(any("cumulative closure bytes" in i for i in issues))
        finally:
            self.mod.INSTALL_CAP_BYTES = old

    def test_mit_hash_mismatch_fails_on_production_default(self):
        # Production MIT pins are enforced by default (not weakened).
        self.fx.build_default_closure()
        issues = self.mod.validate_closure(self.fx.install, self.fx.make_inventory())
        self.assertTrue(any("MIT notice hash mismatch" in i for i in issues))


class ReceiptTests(unittest.TestCase):
    def setUp(self):
        self.mod = load_module()
        self._td = tempfile.TemporaryDirectory(prefix="thunder-audit-", dir=HERE)
        self.addCleanup(self._td.cleanup)
        self.td = pathlib.Path(self._td.name)
        self.fx = AuditFixture(self.td)

    def test_valid_receipt_passes(self):
        self.fx.build_default_closure()
        issues = self.mod.validate_receipt(self.fx.install, self.fx.make_inventory(), REAL_CANDIDATE_LOCK)
        self.assertEqual(issues, [])

    def test_source_lock_tamper_fails(self):
        self.fx.build_default_closure(tamper_source_lock=True)
        issues = self.mod.validate_receipt(self.fx.install, self.fx.make_inventory(), REAL_CANDIDATE_LOCK)
        self.assertTrue(any("source-lock hash" in i for i in issues))

    def test_lock_pin_mismatch_fails(self):
        self.fx.build_default_closure()
        self.fx.write_receipt(lock_sha="0" * 64)
        issues = self.mod.validate_receipt(self.fx.install, self.fx.make_inventory(), REAL_CANDIDATE_LOCK)
        self.assertTrue(any("lockSha256" in i for i in issues))

    def test_target_not_dict_fails(self):
        self.fx.build_default_closure()
        self.fx.write_receipt(target_ok=False)
        issues = self.mod.validate_receipt(self.fx.install, self.fx.make_inventory(), REAL_CANDIDATE_LOCK)
        self.assertTrue(any("target" in i for i in issues))

    def test_kq_hash_mismatch_fails(self):
        self.fx.build_default_closure()
        inventory = self.fx.make_inventory()
        for r in inventory:
            if r["path"] == "runtimes/linux-x64/engine/libstrata_kq.so":
                r["sha256"] = _sha(b"different")
        issues = self.mod.validate_receipt(self.fx.install, inventory, REAL_CANDIDATE_LOCK)
        self.assertTrue(any("kq" in i for i in issues))

    def test_kq_length_mismatch_fails(self):
        self.fx.build_default_closure()
        inventory = self.fx.make_inventory()
        for r in inventory:
            if r["path"] == "runtimes/linux-x64/engine/libstrata_kq.so":
                r["bytes"] = r["bytes"] + 1
        issues = self.mod.validate_receipt(self.fx.install, inventory, REAL_CANDIDATE_LOCK)
        self.assertTrue(any("kq" in i for i in issues))

    def test_helper_mismatch_fails(self):
        self.fx.build_default_closure()
        self.fx.write_receipt(helper_ok=False)
        issues = self.mod.validate_receipt(self.fx.install, self.fx.make_inventory(), REAL_CANDIDATE_LOCK)
        self.assertTrue(any("helperClosure" in i for i in issues))

    def test_runtime_deps_lock_mismatch_fails(self):
        self.fx.build_default_closure()
        self.fx.write_receipt(deps_sha="0" * 64)
        issues = self.mod.validate_receipt(self.fx.install, self.fx.make_inventory(), REAL_CANDIDATE_LOCK)
        self.assertTrue(any("runtimeDepsLockSha256" in i for i in issues))


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.mod = load_module()
        self._td = tempfile.TemporaryDirectory(prefix="thunder-audit-", dir=HERE)
        self.addCleanup(self._td.cleanup)
        self.td = pathlib.Path(self._td.name)
        self.fx = AuditFixture(self.td)

    def test_valid_evidence_passes(self):
        self.fx.build_default_closure()
        inventory = self.fx.make_inventory()
        self.fx.write_evidence(inventory)
        issues = self.mod.validate_evidence(self.fx.evidence, inventory)
        self.assertEqual(issues, [])

    def test_false_success_fails(self):
        self.fx.build_default_closure()
        inventory = self.fx.make_inventory()
        self.fx.write_evidence(inventory, success=False)
        issues = self.mod.validate_evidence(self.fx.evidence, inventory)
        self.assertTrue(any("success" in i for i in issues))

    def test_wrong_engine_version_fails(self):
        self.fx.build_default_closure()
        inventory = self.fx.make_inventory()
        self.fx.write_evidence(inventory, engine_version="strata 0.1.46")
        issues = self.mod.validate_evidence(self.fx.evidence, inventory)
        self.assertTrue(any("engineVersion" in i for i in issues))

    def test_wrong_archive_pin_fails(self):
        self.fx.build_default_closure()
        inventory = self.fx.make_inventory()
        self.fx.write_evidence(inventory, archive="0" * 64)
        issues = self.mod.validate_evidence(self.fx.evidence, inventory)
        self.assertTrue(any("archiveSha256" in i for i in issues))

    def test_wrong_ggml_pin_fails(self):
        self.fx.build_default_closure()
        inventory = self.fx.make_inventory()
        self.fx.write_evidence(inventory, ggml_commit="0" * 40)
        issues = self.mod.validate_evidence(self.fx.evidence, inventory)
        self.assertTrue(any("ggmlCommit" in i for i in issues))

    def test_engine_hash_mismatch_fails(self):
        self.fx.build_default_closure()
        inventory = self.fx.make_inventory()
        self.fx.write_evidence(inventory, engine_sha="0" * 64)
        issues = self.mod.validate_evidence(self.fx.evidence, inventory)
        self.assertTrue(any("engineSha256" in i for i in issues))


class ELFParsingTests(unittest.TestCase):
    def setUp(self):
        self.mod = load_module()

    def test_interpreter_label_stripped(self):
        info = self.mod._parse_elf(ELF_STDOUT)
        self.assertEqual(info["interpreter"], "/lib64/ld-linux-x86-64.so.2")

    def test_libcudart13_detected(self):
        info = self.mod._parse_elf(ELF_STDOUT)
        self.assertTrue(info["libcudart13Needed"])
        self.assertIn("libcuda.so.1", info["otherCudaSonames"])
        self.assertNotIn("libcudart.so.13", info["otherCudaSonames"])

    def test_malformed_missing_machine(self):
        info = self.mod._parse_elf("# no ELF header\n")
        self.assertTrue(info["malformed"])


class WiringTests(unittest.TestCase):
    """`_run_audit` must preserve the injected transport and run fully offline."""

    def setUp(self):
        self.mod = load_module()
        self._td = tempfile.TemporaryDirectory(prefix="thunder-audit-", dir=HERE)
        self.addCleanup(self._td.cleanup)
        self.td = pathlib.Path(self._td.name)
        self.fx = AuditFixture(self.td)

    def test_full_pipeline_offline_with_injected_transport(self):
        self.fx.build_default_closure()
        inventory = self.fx.make_inventory()
        self.fx.write_evidence(inventory)
        inp = base_inputs()
        inp["_http_json"] = FakeGH()
        rec = self.mod._run_audit(inp, self.fx.install, self.fx.evidence,
                                   REAL_CANDIDATE_LOCK, readelf_runner=fake_readelf,
                                   report_dir=self.td / "report")
        # Offline (FakeGH transport preserved), production MIT pins enforced -> fail, never error.
        self.assertEqual(rec["status"], "fail")
        self.assertTrue(any("MIT notice hash mismatch" in i for i in rec["closure"]["issues"]))
        self.assertTrue((self.td / "report" / "audit-report.json").is_file())


class WorkflowWiringTests(unittest.TestCase):
    """String-level configuration binding checks for the manual audit job (only the audit choices/
    inputs/transports; validate/compile jobs are unaffected)."""

    def setUp(self):
        self.wf = (REPO_ROOT / ".github/workflows/haven-chat-model-ci.yml").read_text()

    def test_audit_job_passes_readonly_token(self):
        self.assertIn("GITHUB_TOKEN: ${{ github.token }}", self.wf)

    def test_audit_job_downloads_by_validated_ids(self):
        # Scope to the audit job only (the compile job legitimately uploads artifacts by name).
        idx = self.wf.index("thunder-artifact-audit:\n")
        audit = self.wf[idx:]
        self.assertEqual(audit.count("artifact-ids:"), 2)
        self.assertIn("artifact-ids: ${{ github.event.inputs.audit_evidence_artifact_id }}", audit)
        self.assertIn("artifact-ids: ${{ github.event.inputs.audit_install_artifact_id }}", audit)
        # no name-based fetch in the audit job (uses ids only); only the report upload carries the
        # fetch artifact names and the report name is present.
        self.assertNotIn("name: thunder-cuda-compile-evidence", audit)
        self.assertNotIn("name: thunder-cuda-compile-install-closure", audit)
        self.assertIn("name: thunder-artifact-audit-report", audit)

    def test_audit_job_merge_multiple_direct_layout(self):
        # Each download step is a single prevalidated id; merge-multiple puts the artifact's files
        # directly in `path`, which is what the audit script's install/evidence direct-root layout
        # expects (no artifact-named subdir nesting).
        idx = self.wf.index("thunder-artifact-audit:\n")
        audit = self.wf[idx:]
        self.assertEqual(audit.count("merge-multiple: true"), 2)
        # the two fetch steps target the direct roots the script reads
        self.assertIn("path: work/pc-worker/thunder-artifact-audit/evidence", audit)
        self.assertIn("path: work/pc-worker/thunder-artifact-audit/install", audit)

    def test_pipeline_choices_and_gates_present(self):
        self.assertIn("pipeline == 'validate'", self.wf)
        self.assertIn("pipeline == 'thunder-cuda-compile'", self.wf)
        self.assertIn("pipeline == 'thunder-artifact-audit'", self.wf)


class CLITests(unittest.TestCase):
    def test_failing_input_subprocess_no_network(self):
        ws = tempfile.mkdtemp(prefix="thunder-cli-", dir=HERE)
        self.addCleanup(lambda: os.path.isdir(ws) and __import__("shutil").rmtree(ws))
        env = {k: v for k, v in os.environ.items()
               if not k.startswith("AUDIT_") and k not in ("GITHUB_REPOSITORY", "GITHUB_TOKEN")}
        env["GITHUB_WORKSPACE"] = ws
        r = subprocess.run([sys.executable, str(MOD_PATH), "--preflight"],
                           capture_output=True, text=True, env=env,
                           cwd=str(HERE), timeout=30)
        self.assertEqual(r.returncode, 2)
        self.assertIn("input validation failed", r.stderr)
        # fail-closed bounded report written without any network/API call
        report = pathlib.Path(ws) / "work/pc-worker/thunder-artifact-audit-report/audit-report.json"
        self.assertTrue(report.is_file())
        data = json.loads(report.read_text())
        self.assertEqual(data["status"], "fail")


if __name__ == "__main__":
    unittest.main()
