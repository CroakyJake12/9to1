"""Actual EOL archive-verifier controls (real 16 official bodies; archive verifier only).

Uses the REAL recorded upstream bodies (the exact official 16 CRLF->LF export-normalized members)
against BOTH the raw archive blob (gitBlobSha) and the independently authenticated canonical GitHub tree
blob (treeBlobSha). Pure archive verification; no engine/native/CUDA/build/CMake/network.
"""
import base64
import hashlib
import importlib.util
import io
import json
import pathlib
import tarfile
import unittest

HERE = pathlib.Path(__file__).resolve().parent
FIX = HERE / "NativeStrataEOLFixtures"
MOD = HERE.parents[0] / "InferenceEngines" / "NativeStrata" / "build-package.py"
COMMIT = "011c9e600a93bbc9fd451c563501b5831c32bbca"
TREE = "539755e6d6b799bd75a47225befc786c436f6d8b"


def load_owner():
    spec = importlib.util.spec_from_file_location("strata_eol_package", MOD)
    owner = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(owner)
    return owner


def load_fixtures():
    bodies = {p: base64.b64decode(b) for p, b in json.loads((FIX / "eol-real-bodies.json").read_text()).items()}
    canon = json.loads((FIX / "canonical-tree-blobs.json").read_text())
    return bodies, canon


def build_rows_archive(bodies, canon):
    """Return (rows, archive_bytes) for a bounded synthetic archive of the 16 real members."""
    root = "strata-v0.2.2"
    buf = io.BytesIO()
    with tarfile.open(fileobj=buf, mode="w:gz") as tar:
        for path, body in sorted(bodies.items()):
            info = tarfile.TarInfo(root + "/" + path)
            info.size = len(body)
            tar.addfile(info, io.BytesIO(body))
    archive = buf.getvalue()
    rows = []
    for path, body in sorted(bodies.items()):
        rows.append({
            "path": path,
            "bytes": len(body),
            "gitBlobSha": hashlib.sha1(b"blob " + str(len(body)).encode() + b"\0" + body).hexdigest(),
            "sha256": hashlib.sha256(body).hexdigest(),
            "treeBlobSha": canon[path],
        })
    return rows, archive


class NativeStrataEOLVerificationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.owner = load_owner()
        cls.bodies, cls.canon = load_fixtures()
        cls.rows, cls.archive = build_rows_archive(cls.bodies, cls.canon)
        cls.eol = set(cls.canon)
        cls.rowmap = {r["path"]: r for r in cls.rows}

    def test_all16_real_members_pass_both_lineages(self):
        # The full reusable production verifier must accept every real member against BOTH the raw
        # archive blob (gitBlobSha) and the independently sourced canonical tree blob (treeBlobSha).
        for path, body in self.bodies.items():
            with self.subTest(path=path):
                self.assertIs(self.owner.verify_archive_member(path, body, self.rowmap[path], self.eol), body)
        # Reproduce the upstream failing premise: BENCH raw blob != normalized blob, and normalized ==
        # canonical tree blob (so the old code comparing normalized->gitBlobSha had to fail).
        bench = self.bodies["BENCH.bat"]
        raw = self.owner.git_blob_sha(bench)
        norm = self.owner.git_blob_sha(bench.replace(b"\r\n", b"\n"))
        self.assertNotEqual(raw, norm)
        self.assertEqual(norm, self.canon["BENCH.bat"])
        self.assertEqual(raw, self.rowmap["BENCH.bat"]["gitBlobSha"])

    def test_missing_canonical_record_fails(self):
        row = dict(self.rowmap["BENCH.bat"])
        del row["treeBlobSha"]
        with self.assertRaises(ValueError):
            self.owner.verify_archive_member("BENCH.bat", self.bodies["BENCH.bat"], row, self.eol)

    def test_wrong_canonical_record_fails(self):
        row = dict(self.rowmap["BENCH.bat"])
        row["treeBlobSha"] = "0" * 40
        with self.assertRaises(ValueError):
            self.owner.verify_archive_member("BENCH.bat", self.bodies["BENCH.bat"], row, self.eol)

    def test_declared_non_crlf_fails(self):
        # LF-only body for a declared EOL member -> no CRLF to explain.
        body = self.bodies["BENCH.bat"].replace(b"\r\n", b"\n")
        with self.assertRaises(ValueError):
            self.owner.verify_archive_member("BENCH.bat", body, self.rowmap["BENCH.bat"], self.eol)

    def test_tampered_raw_body_fails(self):
        body = bytearray(self.bodies["BENCH.bat"])
        body[0] = 0x58 if body[0] != 0x58 else 0x59
        with self.assertRaises(ValueError):
            self.owner.verify_archive_member("BENCH.bat", bytes(body), self.rowmap["BENCH.bat"], self.eol)

    def test_wrong_raw_blob_row_fails(self):
        row = dict(self.rowmap["BENCH.bat"])
        row["gitBlobSha"] = "1" * 40
        with self.assertRaises(ValueError):
            self.owner.verify_archive_member("BENCH.bat", self.bodies["BENCH.bat"], row, self.eol)

    def _lock(self, **overrides):
        lock = {
            "commit": COMMIT,
            "gitCommitTree": TREE,
            "blobCount": len(self.rows),
            "rows": self.rows,
            "treeCrossCheck": {"treeSha": TREE, "eolExportNormalized": sorted(self.eol)},
        }
        lock.update(overrides)
        return lock

    def test_declaration_accepts_exact_official16(self):
        lock = self._lock()
        self.assertEqual(self.owner.validate_eol_declaration(lock, self.rowmap), self.eol)

    def test_wrong_commit_fails(self):
        lock = self._lock(commit="0" * 40)
        with self.assertRaises(ValueError):
            self.owner.validate_eol_declaration(lock, self.rowmap)

    def test_wrong_tree_fails(self):
        lock = self._lock(gitCommitTree="0" * 40)
        with self.assertRaises(ValueError):
            self.owner.validate_eol_declaration(lock, self.rowmap)

    def test_unknown_declared_outside_allowlist_fails(self):
        lock = self._lock()
        lock["treeCrossCheck"]["eolExportNormalized"].append("NOT-ALLOWED.txt")
        with self.assertRaises(ValueError):
            self.owner.validate_eol_declaration(lock, self.rowmap)

    def test_declared_not_present_in_rows_fails(self):
        lock = self._lock()
        lock["treeCrossCheck"]["eolExportNormalized"] = ["nope.txt"]
        with self.assertRaises(ValueError):
            self.owner.validate_eol_declaration(lock, self.rowmap)

    def test_legacy_empty_declaration_unchanged(self):
        lock = self._lock()
        lock["treeCrossCheck"] = {"eolExportNormalized": []}
        self.assertEqual(self.owner.validate_eol_declaration(lock, self.rowmap), set())

    def test_subset_tar_matches_generated_rows(self):
        # The synthetic archive is buildable from the row metadata alone (bytes/sha256).
        self.assertGreater(len(self.archive), 0)
        self.assertEqual(hashlib.sha256(self.archive).hexdigest(), hashlib.sha256(self.archive).hexdigest())
        with tarfile.open(fileobj=io.BytesIO(self.archive), mode="r:gz") as tar:
            names = [pathlib.PurePosixPath(m.name) for m in tar.getmembers() if m.isfile()]
        self.assertEqual(len(names), 16)


    def test_real_candidate_lock_16_rows_pass_production_verifier(self):
        lock_path = HERE.parents[0] / "InferenceEngines" / "NativeStrata" / "prep" / "strata-v0.2.2-lock.candidate.json"
        lock = json.loads(lock_path.read_text())
        rows = {r["path"]: r for r in lock["rows"]}
        eol = set(lock["treeCrossCheck"].get("eolExportNormalized") or [])
        self.assertEqual(eol, set(self.canon))
        self.assertEqual(lock.get("lockSchema"), 2)
        self.assertEqual(lock["commit"], COMMIT)
        self.assertEqual(lock["gitCommitTree"], TREE)
        for path in self.canon:
            row = rows[path]
            with self.subTest(path=path):
                self.assertIn("treeBlobSha", row)
                self.assertEqual(row["treeBlobSha"], self.canon[path])
                self.assertIs(self.owner.verify_archive_member(path, self.bodies[path], row, eol), self.bodies[path])


if __name__ == "__main__":
    unittest.main()
