"""Protocol fixtures: real tiny DEBs/files, fake provider transport, NO app/native pass."""
import contextlib
import copy
import datetime as dt
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import warnings
import zipfile

spec = importlib.util.spec_from_file_location("delivery", Path(__file__).with_name("installer_delivery.py"))
d = importlib.util.module_from_spec(spec)
spec.loader.exec_module(d)
SCRATCH = os.environ.get("NINETOONE_DELIVERY_TEST_TMP")


class FixtureGitHub:
    def __init__(self, manifest, archives):
        self.manifest, self.archives, self.calls = manifest, archives, []
        self.repo = {"id": d.REPOSITORY_ID, "full_name": d.REPOSITORY, "private": True}
        self.changes = {}

    def metadata(self, endpoint):
        self.calls.append(endpoint)
        if endpoint == "repos/" + d.REPOSITORY:
            return self.repo
        a = next(a for a in self.manifest["artifacts"] if a["id"] == int(endpoint.rsplit("/", 1)[1]))
        return {"id": a["id"], "name": a["name"], "size_in_bytes": a["bytes"], "digest": "sha256:" + a["sha256"], "expired": False, "expires_at": (dt.datetime.now(dt.timezone.utc) + dt.timedelta(days=1)).isoformat(), "workflow_run": {"id": a["run_id"], "head_sha": a["workflow_head_sha"], "repository_id": d.REPOSITORY_ID}, **self.changes}

    def download(self, a, destination):
        shutil.copyfile(self.archives / f"artifact-{a['id']}.zip", destination)


class Delivery(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.whole = tempfile.TemporaryDirectory(dir=SCRATCH)
        cls.root = Path(cls.whole.name)
        cls.payload = {}
        for arch in ("arm64", "amd64"):
            tree = cls.root / arch
            (tree / "DEBIAN").mkdir(parents=True)
            (tree / "DEBIAN").chmod(0o755)
            (tree / "DEBIAN/control").write_text(f"Package: 9to1-delivery-fixture\nVersion: 0.1.0~fixture\nArchitecture: {arch}\nMaintainer: Fixture <fixture@example.invalid>\nDescription: Delivery protocol fixture only\n")
            p = cls.root / (arch + ".deb")
            built = subprocess.run(["dpkg-deb", "--root-owner-group", "--build", str(tree), str(p)], capture_output=True, env={**os.environ, "TMPDIR": str(cls.root)})
            if built.returncode:
                sys.stderr.write(built.stderr.decode(errors="replace"))
                built.check_returncode()
            cls.payload[arch] = p.read_bytes()

    @classmethod
    def tearDownClass(cls):
        cls.whole.cleanup()

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(dir=SCRATCH)
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.archives = self.root / "browser-downloads"; self.archives.mkdir()
        self.dest = self.root / "new-candidate"; self.dest.mkdir(mode=0o700)
        old = self.root / "old-baseline"; old.mkdir(mode=0o700)
        self.baseline = old / d.BASELINE["file_name"]
        self.baseline.write_bytes(b"fixture-only old baseline")
        self.baseline_id = {"file_name": self.baseline.name, **d.identity(self.baseline)}
        self.name = "9to1-delivery-fixture_0.1.0~fixture_arm64.deb"
        payload = self.payload["arm64"]
        package = {"file_name": self.name, "member": self.name, "bytes": len(payload), "sha256": hashlib.sha256(payload).hexdigest(), "name": "9to1-delivery-fixture", "version": "0.1.0~fixture", "architecture": "arm64"}
        proof = {"status": "READY", "coordinator": "fixture-coordinator", "reviewer": "fixture-reviewer", "implementer": "fixture-implementer", "source_revision": "f" * 40, "package_sha256": package["sha256"], "mandatory_gates": dict.fromkeys(d.GATES, "PASS"), "quality": dict.fromkeys(d.QUALITY, "PASS")}
        raw = json.dumps(proof).encode()
        acceptance = {**proof, "evidence_member": "evidence/independent-ready.json", "evidence": {"bytes": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}}
        self.m = {"schema": "9to1.installer-delivery/v1", "repository": d.REPOSITORY, "source_revision": "f" * 40, "source_patch_sha256": "0" * 64, "package": package, "acceptance": acceptance, "artifacts": [], "target": {"distribution": "ubuntu", "version": "26.04", "architecture": "arm64", "gnome_acceptance": "NOT RUN"}}
        self.build = {"source_head": "f" * 40, "source_patch_sha256": "0" * 64, "architecture": "arm64", "package": "/ci/" + self.name, "bytes": package["bytes"], "sha256": package["sha256"], "package_status": "ASSEMBLED_NOT_NATIVE_ACCEPTED"}
        self.entries = {self.name: payload, "package-receipt.json": json.dumps(self.build).encode(), "SHA256SUMS": (package["sha256"] + "  " + self.name + "\n").encode()}
        for n, role in enumerate(("package", "corresponding-source-and-evidence", "browser-sidecar", "upstream-engine-source"), 1):
            self.m["artifacts"].append({"id": n, "run_id": n + 100, "workflow_head_sha": "d" * 40, "name": "fixture-" + role, "roles": [role]})
            self.repack(n, self.entries if n == 1 else {"evidence/independent-ready.json": raw} if n == 2 else {"NOTICE.txt": b"fixture source, not actual app"})
        self.client = FixtureGitHub(self.m, self.archives)

    def repack(self, n, entries):
        p = self.archives / f"artifact-{n}.zip"
        with zipfile.ZipFile(p, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name, value in entries.items(): archive.writestr(name, value)
        self.m["artifacts"][n - 1].update(d.identity(p))

    def receive(self, local=True):
        with patch.object(d, "host_preflight", return_value={"scope": "UNIT host-policy fixture, NOT native verification"}), patch.object(d, "BASELINE", self.baseline_id):
            return d.receive(d.validate_manifest(self.m), "a" * 64, self.dest, self.baseline, self.archives if local else None, self.client)

    def test_actual_deb_fixture_is_arm64(self):
        p = Path(self.whole.name) / "arm64.deb"
        self.assertEqual(subprocess.check_output(["dpkg-deb", "--field", str(p), "Architecture"], text=True).strip(), "arm64")

    def test_local_fixture_retains_exact_bytes_baseline_sources_and_honest_status(self):
        before = d.identity(self.baseline)
        result = self.receive()
        self.assertEqual(result["status"], "DELIVERED_BYTES_VERIFIED")
        self.assertEqual(d.identity(result["package"]["path"]), {k: self.m["package"][k] for k in ("bytes", "sha256")})
        self.assertEqual(d.identity(self.baseline), before)
        self.assertEqual(result["GNOME_acceptance"], "NOT RUN")
        self.assertEqual(result["installation"], "NOT RUN")
        self.assertTrue(result["source_access"]["authentication"].startswith("NOT CHECKED"))
        self.assertEqual(len(result["archives"]), 4)
        self.assertTrue(Path(result["receipt_path"]).is_file())
        self.assertFalse(list(self.dest.glob(".9to1-receive-*")))

    def test_authenticated_fixture_transport_checks_private_identity_contract(self):
        result = self.receive(local=False)
        self.assertTrue(result["source_access"]["private"])
        self.assertEqual(len(self.client.calls), 5)
        self.assertTrue(all(x.startswith("repos/" + d.REPOSITORY) for x in self.client.calls))

    def test_retry_never_overwrites_unchanged_candidate(self):
        a, b = self.receive(), self.receive()
        self.assertEqual(a["package"], b["package"])
        self.assertNotEqual(a["receipt_path"], b["receipt_path"])

    def test_assembled_only_cannot_authorize_transfer(self):
        self.m["acceptance"]["status"] = "ASSEMBLED_NOT_NATIVE_ACCEPTED"
        with self.assertRaises(d.Blocked): self.receive()
        self.assertFalse((self.dest / self.name).exists())

    def test_every_mandatory_and_quality_gate_remains_required(self):
        for category, names in (("mandatory_gates", d.GATES), ("quality", d.QUALITY)):
            for key in names:
                candidate = copy.deepcopy(self.m); candidate["acceptance"][category][key] = "NOT RUN"
                with self.subTest(category=category, key=key), self.assertRaises(d.Blocked): d.validate_manifest(candidate)

    def test_independent_exact_candidate_acceptance_required(self):
        for key, value in (("reviewer", "fixture-implementer"), ("source_revision", "e" * 40), ("package_sha256", "e" * 64)):
            m = copy.deepcopy(self.m); m["acceptance"][key] = value
            with self.subTest(key=key), self.assertRaises(d.Blocked): d.validate_manifest(m)

    def test_all_source_and_licence_artifact_roles_required(self):
        for role in d.ROLES:
            m = copy.deepcopy(self.m); m["artifacts"] = [a for a in m["artifacts"] if role not in a["roles"]]
            with self.subTest(role=role), self.assertRaises(d.Blocked): d.validate_manifest(m)

    def test_unique_artifacts_and_safe_member_names_required(self):
        m = copy.deepcopy(self.m); m["artifacts"][1]["id"] = 1
        with self.assertRaises(d.Blocked): d.validate_manifest(m)
        for member in ("../new_arm64.deb", "/new_arm64.deb", "a\\new_arm64.deb", "a//new_arm64.deb"):
            m = copy.deepcopy(self.m); m["package"]["member"] = member
            with self.subTest(member=member), self.assertRaises(d.Blocked): d.validate_manifest(m)

    def test_public_wrong_repository_or_missing_auth_identity_refused(self):
        for changes in ({"private": False}, {"id": 42}, {"full_name": "other/repo"}):
            self.client.repo = {"id": d.REPOSITORY_ID, "full_name": d.REPOSITORY, "private": True, **changes}
            with self.subTest(changes=changes), self.assertRaises(d.Blocked): d.remote_preflight(self.m, self.client)

    def test_expiry_archive_digest_and_workflow_provenance_refused(self):
        for changes in ({"expired": True}, {"expires_at": "2000-01-01T00:00:00Z"}, {"digest": "sha256:" + "0" * 64}, {"workflow_run": {"id": 101, "head_sha": "a" * 40, "repository_id": d.REPOSITORY_ID}}):
            self.client.changes = changes
            with self.subTest(changes=changes), self.assertRaises(d.Blocked): d.remote_preflight(self.m, self.client)

    def test_outer_archive_tamper_prevents_extraction(self):
        with (self.archives / "artifact-1.zip").open("ab") as stream: stream.write(b"tampered")
        with self.assertRaises(d.Blocked): self.receive()
        self.assertFalse((self.dest / self.name).exists())

    def test_valid_outer_digest_cannot_hide_wrong_inner_bytes(self):
        self.entries[self.name] = b"bad bytes"; self.repack(1, self.entries)
        with self.assertRaises(d.Blocked): self.receive()

    def test_build_source_and_checksum_must_match(self):
        self.build["source_head"] = "e" * 40
        self.entries["package-receipt.json"] = json.dumps(self.build).encode(); self.repack(1, self.entries)
        with self.assertRaises(d.Blocked): self.receive()
        self.build["source_head"] = "f" * 40
        self.entries["package-receipt.json"] = json.dumps(self.build).encode()
        self.entries["SHA256SUMS"] = b"0  wrong.deb\n"; self.repack(1, self.entries)
        with self.assertRaises(d.Blocked): self.receive()

    def test_duplicate_inner_member_refused(self):
        p = self.archives / "artifact-1.zip"
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)
            with zipfile.ZipFile(p, "a") as archive: archive.writestr(self.name, self.payload["arm64"])
        self.m["artifacts"][0].update(d.identity(p))
        with self.assertRaises(d.Blocked): self.receive()

    def test_retained_assembled_checkpoint_cannot_pass_ready_manifest(self):
        proof = json.loads(self.proof_bytes()); proof["status"] = "CHECKPOINT"
        raw = json.dumps(proof).encode()
        self.m["acceptance"]["evidence"] = {"bytes": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}
        self.repack(2, {"evidence/independent-ready.json": raw})
        with self.assertRaises(d.Blocked): self.receive()

    def proof_bytes(self):
        with zipfile.ZipFile(self.archives / "artifact-2.zip") as archive: return archive.read("evidence/independent-ready.json")

    def test_actual_wrong_deb_architecture_refused(self):
        payload = self.payload["amd64"]; package = self.m["package"]
        package.update(bytes=len(payload), sha256=hashlib.sha256(payload).hexdigest())
        self.build.update(bytes=package["bytes"], sha256=package["sha256"])
        self.m["acceptance"]["package_sha256"] = package["sha256"]
        proof = json.loads(self.proof_bytes()); proof["package_sha256"] = package["sha256"]
        raw = json.dumps(proof).encode()
        self.m["acceptance"]["evidence"] = {"bytes": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}
        self.repack(2, {"evidence/independent-ready.json": raw})
        self.entries.update({self.name: payload, "package-receipt.json": json.dumps(self.build).encode(), "SHA256SUMS": (package["sha256"] + "  " + self.name + "\n").encode()}); self.repack(1, self.entries)
        with self.assertRaises(d.Blocked): self.receive()

    def test_baseline_cannot_be_proposed_as_new_or_followed_symlink(self):
        m = copy.deepcopy(self.m); m["package"].update(file_name=d.BASELINE["file_name"], member=d.BASELINE["file_name"])
        with self.assertRaises(d.Blocked): d.validate_manifest(m)
        link = self.root / self.baseline.name; link.symlink_to(self.baseline)
        with patch.object(d, "BASELINE", self.baseline_id), self.assertRaises(OSError): d.baseline_preflight(link)

    def test_existing_owner_bytes_or_symlink_never_overwritten(self):
        target = self.dest / self.name; target.write_bytes(b"owner file")
        with self.assertRaises(d.Blocked): self.receive()
        self.assertEqual(target.read_bytes(), b"owner file"); target.unlink()
        outside = self.root / "personal"; outside.write_bytes(b"personal")
        target.symlink_to(outside)
        with self.assertRaises(d.Blocked): self.receive()
        self.assertEqual(outside.read_bytes(), b"personal")

    def test_private_directory_capacity_and_baseline_directory_guards(self):
        self.dest.chmod(0o755)
        with self.assertRaises(d.Blocked): d.prepare_destination(self.m, self.dest, self.baseline)
        self.dest.chmod(0o700)
        with patch.object(d.shutil, "disk_usage", return_value=type("Space", (), {"free": 1})()), self.assertRaises(d.Blocked): d.prepare_destination(self.m, self.dest, self.baseline)
        with self.assertRaises(d.Blocked): d.prepare_destination(self.m, self.baseline.parent, self.baseline)

    def test_baseline_change_blocks_candidate_publication(self):
        real = self.client.download
        def mutate(a, target):
            real(a, target); self.baseline.write_bytes(b"changed fixture baseline")
        self.client.download = mutate
        with self.assertRaises(d.Blocked): self.receive(local=False)
        self.assertFalse((self.dest / self.name).exists())

    def test_wrong_host_is_not_native_verification(self):
        with patch.object(d.platform, "machine", return_value="x86_64"), self.assertRaises(d.Blocked): d.host_preflight()
        with patch.object(d.platform, "machine", return_value="aarch64"), patch.object(d.platform, "freedesktop_os_release", return_value={"ID": "ubuntu", "VERSION_ID": "24.04"}), self.assertRaises(d.Blocked): d.host_preflight()

    def test_cli_retains_actual_blocked_receipt_and_no_fake_download(self):
        p = self.root / "manifest.json"; p.write_text(json.dumps({**self.m, "acceptance": {"status": "BLOCKED"}}))
        out = io.StringIO()
        with contextlib.redirect_stdout(out): code = d.main(["receive", "--manifest", str(p), "--destination", str(self.dest), "--baseline", str(self.baseline)])
        receipt = json.loads(out.getvalue())
        self.assertEqual(code, 2)
        self.assertEqual(receipt["delivery"], "NOT CONFIRMED")
        self.assertEqual(json.loads(Path(receipt["failure_receipt_path"]).read_text())["status"], "BLOCKED")
        self.assertFalse((self.dest / self.name).exists())

    def test_adjacent_ready_proof_avoids_rebuilding_immutable_package_artifacts(self):
        raw = self.proof_bytes()
        acceptance = self.m["acceptance"]
        acceptance.pop("evidence_member")
        acceptance["evidence_file"] = "independent-ready.json"
        proof = self.root / "independent-ready.json"; proof.write_bytes(raw)
        path = self.root / "manifest.json"; path.write_text(json.dumps(self.m))
        manifest, sha, loaded = d.load_manifest(path)
        self.assertEqual(loaded, raw)
        with patch.object(d, "host_preflight", return_value={"scope": "UNIT host-policy fixture"}), patch.object(d, "BASELINE", self.baseline_id):
            receipt = d.receive(manifest, sha, self.dest, self.baseline, self.archives, local_proof=loaded)
        self.assertEqual(receipt["status"], "DELIVERED_BYTES_VERIFIED")
        proof.write_bytes(b"changed")
        with self.assertRaises(d.Blocked): d.load_manifest(path)

    def test_absent_or_ambiguous_local_proof_refused(self):
        self.m["acceptance"]["evidence_file"] = "independent-ready.json"
        with self.assertRaises(d.Blocked): d.validate_manifest(self.m)
        self.m["acceptance"].pop("evidence_member")
        path = self.root / "manifest.json"; path.write_text(json.dumps(self.m))
        with self.assertRaises(OSError): d.load_manifest(path)

    def test_no_debug_token_logging_or_interactive_login(self):
        with patch.dict(d.os.environ, {"GH_DEBUG": "api"}):
            self.assertNotIn("GH_DEBUG", d.gh_environment())
            self.assertEqual(d.gh_environment()["GH_PROMPT_DISABLED"], "1")


if __name__ == "__main__":
    unittest.main()
