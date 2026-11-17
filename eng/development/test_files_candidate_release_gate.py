#!/usr/bin/env python3
"""Independent, disposable corruption tests for the public Files candidate gate."""
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import stat
import subprocess
import sys
import tempfile
import unittest
import warnings
import zipfile

MODULE = Path(__file__).with_name("files_candidate_release_gate.py")
spec = importlib.util.spec_from_file_location("files_candidate_release_gate", MODULE)
gate = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = gate
spec.loader.exec_module(gate)
COMMIT = "a" * 40
BOOTSTRAP = {
    "index.html": b"<!doctype html><title>Files</title>",
    "main.js": b"import './files-browser.js';",
    "files-browser.js": b"export function createBridge(){}",
    "_framework/dotnet.js": b"// loader",
    "_framework/avalonia.js": b"// UI",
    "_framework/storage.js": b"// state",
    "_framework/dotnet.native.abc123.wasm": b"\x00asm\x01\x00\x00\x00",
}


def zip_bytes(files, *, extra=None):
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as bundle:
        for name, body in files.items():
            bundle.writestr(name, body)
        if extra:
            for name, body in extra:
                with warnings.catch_warnings():
                    warnings.simplefilter("ignore", UserWarning)
                    bundle.writestr(name, body)
    return output.getvalue()


def artifact(files=None, *, seal_update=None, receipt_update=None, inner_extra=None, outer_extra=None,
             incorrect_seal=False, inner=None):
    files = BOOTSTRAP.copy() if files is None else files
    rows = [{"path": n, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}
            for n, data in files.items()]
    receipt = {"sourceCommit": COMMIT, "sourceCommitAfter": COMMIT, "exitCode": 0,
               "trackedChangesAfter": [], "addedSourceInputs": [], "removedSourceInputs": [],
               "changedSourceInputs": [], "publishFiles": rows, "fileCount": len(rows),
               "totalBytes": sum(row["bytes"] for row in rows)}
    if receipt_update:
        receipt_update(receipt)
    receipt_bytes = (json.dumps(receipt, indent=2) + "\n").encode()
    inner = zip_bytes(files, extra=inner_extra) if inner is None else inner
    seal = {"sourceCommit": COMMIT, "receiptSha256": hashlib.sha256(receipt_bytes).hexdigest(),
            "zipSha256": hashlib.sha256(inner).hexdigest(), "zipBytes": len(inner),
            "fileCount": len(rows), "totalBytes": receipt["totalBytes"], "runtimeVerified": False,
            "deploymentVerified": False, "fullParityVerified": False}
    if seal_update:
        seal_update(seal)
    if incorrect_seal:
        seal["zipSha256"] = "0" * 64
    return zip_bytes({"seal.json": json.dumps(seal).encode(),
                      "publish-manifest.json": receipt_bytes, "wwwroot.zip": inner},
                     extra=outer_extra)


class GateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.path = self.root / "candidate.zip"

    def check(self, data, *, extraction=False, source=COMMIT):
        self.path.write_bytes(data)
        return gate.verify(self.path, source,
                           extract=self.root / "public" if extraction else None)

    def rejects(self, data, label=""):
        with self.assertRaises(gate.RejectedCandidate, msg=label):
            self.check(data)

    def test_correct_realistic_fixture_and_bounded_safe_extraction(self):
        result = self.check(artifact(), extraction=True)
        self.assertEqual(result["status"], "PASS")
        self.assertEqual(result["publicFilesVerified"], len(BOOTSTRAP))
        self.assertFalse(result["readyToUseAsRealDrive"])
        self.assertFalse(result["liveDriveAccessVerifiedByThisTool"])
        self.assertEqual((self.root / "public/_framework/dotnet.native.abc123.wasm").read_bytes(), BOOTSTRAP["_framework/dotnet.native.abc123.wasm"])
        self.assertEqual((self.root / "public/index.html").read_bytes(), BOOTSTRAP["index.html"])

    def test_preexisting_extraction_destination_never_overwritten(self):
        previous = self.root / "public"
        previous.mkdir()
        (previous / "precious.txt").write_text("preserve")
        self.path.write_bytes(artifact())
        with self.assertRaises(gate.RejectedCandidate):
            gate.verify(self.path, COMMIT, extract=previous)
        self.assertEqual((previous / "precious.txt").read_text(), "preserve")

    def test_wrong_expected_source_rejected(self):
        self.path.write_bytes(artifact())
        with self.assertRaisesRegex(gate.RejectedCandidate, "Seal source"):
            gate.verify(self.path, "b" * 40)
        with self.assertRaisesRegex(gate.RejectedCandidate, "40-hex"):
            gate.verify(self.path, "main")

    def test_malformed_outer_zip_fails_closed(self):
        self.rejects(b"not a ZIP")

    def test_windows_drive_like_path_rejected(self):
        files = BOOTSTRAP.copy()
        files["C:/system.txt"] = b"not allowed"
        self.rejects(artifact(files))

    def test_modified_zip_digest_rejected(self):
        self.rejects(artifact(incorrect_seal=True))

    def test_wrong_manifest_integrity_rejected(self):
        self.rejects(artifact(seal_update=lambda seal: seal.update(receiptSha256="f" * 64)))

    def test_inner_file_mutation_with_resigned_outer_seal_rejected(self):
        altered = BOOTSTRAP.copy()
        altered["main.js"] = b"changed file"
        self.rejects(artifact(inner=zip_bytes(altered)))

    def test_duplicate_inner_name_rejected(self):
        self.rejects(artifact(inner_extra=[("index.html", b"duplicate")]))

    def test_duplicate_outer_member_rejected(self):
        self.rejects(artifact(outer_extra=[("seal.json", b"{}")]))

    def test_outer_extra_member_rejected(self):
        self.rejects(artifact(outer_extra=[("extra.txt", b"unexpected")]))

    def test_traversal_zip_path_rejected_even_with_matching_manifest(self):
        files = BOOTSTRAP.copy()
        files["../secrets.txt"] = b"not allowed"
        self.rejects(artifact(files))

    def test_absolute_zip_path_rejected(self):
        files = BOOTSTRAP.copy()
        files["/tmp/injected.txt"] = b"not allowed"
        self.rejects(artifact(files))

    def test_private_file_rejected(self):
        files = BOOTSTRAP.copy()
        files[".env"] = b"SECRET=abc"
        self.rejects(artifact(files))

    def test_missing_bootstrap_rejected(self):
        files = BOOTSTRAP.copy()
        del files["_framework/avalonia.js"]
        self.rejects(artifact(files))

    def test_missing_actual_wasm_rejected(self):
        files = BOOTSTRAP.copy()
        del files["_framework/dotnet.native.abc123.wasm"]
        self.rejects(artifact(files))

    def test_false_source_custody_rejected(self):
        self.rejects(artifact(receipt_update=lambda receipt: receipt.update(trackedChangesAfter=["main.js"])))
        self.rejects(artifact(receipt_update=lambda receipt: receipt.update(sourceCommitAfter="b" * 40)))

    def test_member_size_and_manifest_aggregate_rejected(self):
        self.rejects(artifact(receipt_update=lambda receipt: receipt.update(totalBytes=1)))
        self.rejects(artifact(receipt_update=lambda receipt: receipt["publishFiles"][0].update(bytes=9_999)))

    def test_zip_member_as_symlink_rejected(self):
        outer_data = artifact()
        with zipfile.ZipFile(io.BytesIO(outer_data)) as stream:
            members = {n: stream.read(n) for n in stream.namelist()}
        inner_bytes = members["wwwroot.zip"]
        with zipfile.ZipFile(io.BytesIO(inner_bytes)) as stream:
            files = {n: stream.read(n) for n in stream.namelist()}
        output = io.BytesIO()
        with zipfile.ZipFile(output, "w") as archive:
            for name, payload in files.items():
                entry = zipfile.ZipInfo(name)
                if name == "main.js":
                    entry.create_system = 3
                    entry.external_attr = (stat.S_IFLNK | 0o777) << 16
                archive.writestr(entry, payload)
        self.rejects(artifact(inner=output.getvalue()))

    def test_result_never_infers_runtime_from_seal_boolean(self):
        result = self.check(artifact(seal_update=lambda seal: seal.update(runtimeVerified=True,
                                                                          deploymentVerified=True,
                                                                          fullParityVerified=True)))
        self.assertFalse(result["browserStartupVerifiedByThisTool"])
        self.assertFalse(result["googleOAuthVerifiedByThisTool"])
        self.assertFalse(result["productionDeploymentVerifiedByThisTool"])

    def test_command_line_success_and_failure(self):
        self.path.write_bytes(artifact())
        ok = subprocess.run([sys.executable, str(MODULE), str(self.path), "--expected-commit", COMMIT],
                            text=True, capture_output=True, check=False)
        self.assertEqual(ok.returncode, 0, ok.stderr)
        self.assertEqual(json.loads(ok.stdout)["status"], "PASS")
        failed = subprocess.run([sys.executable, str(MODULE), str(self.path), "--expected-commit", "b" * 40],
                                text=True, capture_output=True, check=False)
        self.assertNotEqual(failed.returncode, 0)
        self.assertEqual(json.loads(failed.stderr)["status"], "FAIL")


if __name__ == "__main__":
    unittest.main()
