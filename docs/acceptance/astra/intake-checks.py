#!/usr/bin/env python3
"""Synthetic custody-validator tests only; no product test or acceptance proof."""
import copy
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

import intake


class CustodyControls(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="astra-custody-self-test-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / "synthetic-source"
        self.source.mkdir()
        self.command("init", "--quiet")
        self.repository = "https://example.invalid/synthetic-custody-test.git"
        self.command("remote", "add", "origin", self.repository)
        for name in ("synthetic-product.py", "dependencies.lock", "donors.lock"):
            (self.source / name).write_text("synthetic custody fixture only\n")
        self.command("add", ".")
        self.command("-c", "user.name=Synthetic Custody Test", "-c",
                     "user.email=custody@example.invalid", "commit", "--quiet", "-m", "Synthetic fixture")
        self.revision = self.command("rev-parse", "HEAD").strip()
        self.artifact = self.file("synthetic-package.bin", "synthetic bytes; not a product package\n")
        self.log = self.file("synthetic-workflow.txt", "synthetic asserted observation; no product ran\n")
        citation = [{"spec_id": "development", "locator": "Synthetic section; manual review required"}]
        self.candidate = {
            "schema_version": 1, "candidate_id": "synthetic-validator-self-test",
            "delivery_index_entry": "synthetic local fixture",
            "source": {"repository": self.repository, "revision": self.revision,
                       "checkout": str(self.source),
                       "source_files": [self.source_record("synthetic-product.py")]},
            "specs": [dict(id=identity, document_id=document_id, revision="synthetic-provider-revision",
                           revision_evidence=self.file(f"{identity}-revision.json", json.dumps({
                               "documentId": document_id, "revisionId": "synthetic-provider-revision"})),
                           **self.file(f"{identity}.txt", "Synthetic spec text; identity not authenticated\n"))
                      for identity, document_id in intake.SPEC_IDS.items()],
            "locks": [dict(role=role, **self.source_record(name))
                      for role, name in (("dependencies", "dependencies.lock"), ("donors", "donors.lock"))],
            "scope": {"claims": [{"id": "synthetic-journey", "description": "Synthetic self-test claim",
                                  "platforms": ["synthetic-platform"], "spec_citations": citation}],
                      "platform_matrix": [{"id": "synthetic-platform", "kind": "native",
                                           "required": True, "spec_citations": citation}]},
            "artifacts": [dict(id="synthetic-package", platform="synthetic-platform",
                               source_repository=self.repository, source_revision=self.revision,
                               **self.artifact)],
        }
        self.report = {
            "report_id": "synthetic-smoke-report", "issuer": "Synthetic independent fixture",
            "issuer_role": "Smoke Tester", "independent": True,
            "candidate_id": self.candidate["candidate_id"], "source_revision": self.revision,
            "source_repository": self.repository,
            "tested_artifacts": [{"id": "synthetic-package", "platform": "synthetic-platform",
                                  "sha256": self.artifact["sha256"]}],
            "environments": [{"platform": "synthetic-platform", "host": "synthetic-host",
                              "os": "synthetic-os", "os_version": "synthetic-version",
                              "architecture": "synthetic-architecture", "runtime": "synthetic-runtime",
                              "session_id": "synthetic-session"}],
            "main_workflows": [{"id": "synthetic-workflow", "platform": "synthetic-platform",
                                "claim_ids": ["synthetic-journey"], "executed": True,
                                "result": "PASS", "evidence": self.log}],
        }
        self.refresh_report()
        self.manifest = self.root / "candidate.json"

    def command(self, *args):
        return subprocess.run(["git", "-C", str(self.source), *args], check=True,
                              capture_output=True, text=True).stdout

    def file(self, name, content):
        path = self.root / name
        path.write_text(content)
        return {"path": name, "sha256": intake.digest(path)}

    def source_record(self, name):
        return {"path": name, "sha256": intake.digest(self.source / name)}

    def refresh_report(self):
        record = self.file("smoke-report.json", json.dumps(self.report))
        self.candidate["smoke"] = dict(report_id=self.report["report_id"],
                                       issuer=self.report["issuer"], **record)

    def run_validator(self):
        self.manifest.write_text(json.dumps(self.candidate))
        return intake.validate(self.manifest)

    def rejected(self, expected):
        result = self.run_validator()
        self.assertEqual("CUSTODY_INTEGRITY_REJECTED", result["status"])
        self.assertIn(expected, result["errors"][0])

    def test_synthetic_consistent_fixture_only_pending_manual_review(self):
        result = self.run_validator()
        self.assertEqual(intake.PENDING, result["status"])
        self.assertTrue(result["manual_review_required"])
        self.assertNotIn("accepted", result)

    def test_packaged_bytes_changed_after_smoke(self):
        (self.root / self.artifact["path"]).write_text("substituted package\n")
        self.rejected("File hash mismatch")

    def test_hash_consistent_manifest_but_smoke_was_for_other_source(self):
        self.report["source_revision"] = "1" * 40
        self.refresh_report()
        self.rejected("Smoke source mismatch")

    def test_smoke_claim_for_other_artifact(self):
        self.report["tested_artifacts"][0]["sha256"] = "1" * 64
        self.refresh_report()
        self.rejected("Smoke artifact hash mismatch")

    def test_spec_snapshot_without_revision(self):
        self.candidate["specs"][0].pop("revision")
        self.rejected("Missing revision")

    def test_pass_label_without_executed_workflow(self):
        self.report["main_workflows"][0]["executed"] = False
        self.refresh_report()
        self.rejected("was not executed")

    def test_package_was_smoked_on_other_platform(self):
        self.report["tested_artifacts"][0]["platform"] = "other-platform"
        self.refresh_report()
        self.rejected("Smoke platform mismatch")

    def test_workflow_evidence_replaced_after_report_hash_was_pinned(self):
        (self.root / self.log["path"]).write_text("changed observed log\n")
        self.rejected("File hash mismatch")

    def test_source_checkout_edited_after_smoke(self):
        (self.source / "synthetic-product.py").write_text("changed source\n")
        self.rejected("Source checkout is dirty")

    def test_source_checkout_moved_to_different_clean_commit(self):
        (self.source / "synthetic-product.py").write_text("different source\n")
        self.command("add", ".")
        self.command("-c", "user.name=Synthetic Custody Test", "-c",
                     "user.email=custody@example.invalid", "commit", "--quiet", "-m", "Different fixture")
        self.rejected("Source checkout revision drift")

    def test_additional_declared_platform_without_package_or_workflow(self):
        other = copy.deepcopy(self.candidate["scope"]["platform_matrix"][0])
        other["id"] = "other-platform"
        self.candidate["scope"]["platform_matrix"].append(other)
        self.candidate["scope"]["claims"][0]["platforms"].append("other-platform")
        self.rejected("Missing packaged artifact")

    def test_committed_donor_lock_required(self):
        self.candidate["locks"] = self.candidate["locks"][:1]
        self.rejected("Missing dependency/donor locks")

    def test_template_placeholders_rejected(self):
        result = intake.validate(Path(__file__).with_name("candidate-template.json"))
        self.assertEqual("CUSTODY_INTEGRITY_REJECTED", result["status"])
        self.assertIn("Placeholder", result["errors"][0])

    def test_nonobject_manifest_rejected_without_traceback(self):
        self.candidate = []
        self.rejected("Manifest must be a JSON object")

    def test_null_nested_source_rejected_without_traceback(self):
        self.candidate["source"] = None
        self.rejected("Expected object for checkout")

    def test_null_nested_workflow_evidence_rejected_without_traceback(self):
        self.report["main_workflows"][0]["evidence"] = None
        self.refresh_report()
        self.rejected("Expected object for path")

    def test_tracked_source_changes_hidden_by_index_flag_rejected(self):
        self.command("update-index", "--assume-unchanged", "synthetic-product.py")
        (self.source / "synthetic-product.py").write_text("hidden changes\n")
        self.rejected("Source index flags hide checkout changes")

    def test_sparse_index_flag_cannot_hide_checkout_drift(self):
        self.command("update-index", "--skip-worktree", "synthetic-product.py")
        (self.source / "synthetic-product.py").write_text("hidden skipped changes\n")
        self.rejected("Source index flags hide checkout changes")

    def test_external_git_filter_cannot_execute_during_custody_status(self):
        filtered = self.source / "additional-filtered-source.py"
        filtered.write_text("a=1\n")
        (self.source / ".gitattributes").write_text("additional-filtered-source.py filter=custodytest\n")
        self.command("add", ".")
        self.command("-c", "user.name=Synthetic Custody Test", "-c",
                     "user.email=custody@example.invalid", "commit", "--quiet", "-m", "Filter fixture")
        revision = self.command("rev-parse", "HEAD").strip()
        self.candidate["source"]["revision"] = revision
        self.candidate["artifacts"][0]["source_revision"] = revision
        self.report["source_revision"] = revision
        self.refresh_report()
        marker = self.root / "filter-invoked.txt"
        self.command("config", "filter.custodytest.clean", f"touch {marker}; cat")
        filtered.write_text("a=2\n")
        self.rejected("Configured external Git clean/process filters")
        self.assertFalse(marker.exists(), "Custody check executed a configured external filter")

    def test_scope_cannot_replace_authoritative_specs_with_supplemental_document(self):
        self.candidate["specs"].append(dict(
            id="supplemental", document_id="synthetic-supplemental-doc", revision="synthetic-revision",
            revision_evidence=self.file("supplemental-revision.json", json.dumps({
                "documentId": "synthetic-supplemental-doc", "revisionId": "synthetic-revision"})),
            **self.file("supplemental.txt", "supplemental synthetic instructions\n")))
        self.candidate["scope"]["claims"][0]["spec_citations"] = [
            {"spec_id": "supplemental", "locator": "synthetic section"}]
        self.rejected("Scope row lacks an authoritative specification citation")

    def test_nonobject_hashed_smoke_report_rejected(self):
        self.candidate["smoke"].update(self.file("smoke-report.json", "[]"))
        self.rejected("Smoke report must be a JSON object")

    def test_nonobject_artifact_record_rejected(self):
        self.candidate["artifacts"] = ["not an artifact record"]
        self.rejected("Invalid artifacts")

    def test_report_without_independent_tester_rejected(self):
        self.report["independent"] = False
        self.refresh_report()
        self.rejected("Independent Smoke Tester declaration missing")

    def test_spec_revision_metadata_for_other_snapshot_rejected(self):
        self.candidate["specs"][0]["revision_evidence"] = self.file("development-revision.json", json.dumps({
            "documentId": intake.SPEC_IDS["development"], "revisionId": "another-revision"}))
        self.rejected("Specification revision evidence mismatch")

    def web_fixture(self):
        platform = self.candidate["scope"]["platform_matrix"][0]
        platform["kind"] = "web"
        deployment = {"deployment_id": "synthetic-immutable-deployment-001", "immutable": True,
                      "url": "https://example.invalid/synthetic-deployment-001",
                      "bundle_sha256": self.artifact["sha256"], "access_boundary": "synthetic signed-in boundary",
                      "session_id": "synthetic-session"}
        self.access = {key: deployment[key] for key in
                       ("deployment_id", "url", "bundle_sha256", "access_boundary", "session_id")}
        self.access.update(source_repository=self.repository, source_revision=self.revision,
                           executed=True, boundary_reached=True,
                           observation=self.file("access-observation.txt", "synthetic boundary observation\n"))
        platform["deployment"] = deployment
        self.refresh_access()

    def refresh_access(self):
        self.candidate["scope"]["platform_matrix"][0]["deployment"]["access_evidence"] = self.file(
            "access-evidence.json", json.dumps(self.access))

    def test_synthetic_web_fixture_only_pending_manual_review(self):
        self.web_fixture()
        self.assertEqual(intake.PENDING, self.run_validator()["status"])

    def test_web_deployment_was_not_accessed(self):
        self.web_fixture()
        self.access["executed"] = False
        self.refresh_access()
        self.rejected("was not observed")

    def test_web_session_belongs_to_another_access_observation(self):
        self.web_fixture()
        self.access["session_id"] = "other-session"
        self.refresh_access()
        self.rejected("Web access evidence session_id mismatch")

    def test_web_bundle_substituted_from_deployed_hash(self):
        self.web_fixture()
        self.candidate["scope"]["platform_matrix"][0]["deployment"]["bundle_sha256"] = "1" * 64
        self.rejected("Web bundle hash mismatch")

    def test_web_mutable_alias_is_not_immutable_identity(self):
        self.web_fixture()
        self.candidate["scope"]["platform_matrix"][0]["deployment"]["deployment_id"] = "latest"
        self.rejected("Mutable web deployment alias")


if __name__ == "__main__":
    unittest.main(verbosity=2)
