"""Negative controls for evidence validation; fixtures are NOT product acceptance."""
import copy
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

MODULE_PATH = Path(__file__).resolve().parents[1] / "validate-acceptance-evidence.py"
SPEC = importlib.util.spec_from_file_location("validator", MODULE_PATH)
validator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(validator)


class EvidenceIntegrityTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.log = {"path": "runtime.log", "sha256": validator.digest(b"observed runtime result\n")}
        (self.root / self.log["path"]).write_bytes(b"observed runtime result\n")
        self.source = {"documentId": "test-document", "revisionId": "test-revision"}
        self.paragraphs = [{"index": 1, "end": 30, "text": "Browser must persist an edit.\n"}]
        self.paragraphs_hash = validator.digest(json.dumps(self.paragraphs).encode())
        self.source["paragraphs_sha256"] = self.paragraphs_hash
        self.requirements = {
            "schema_version": 1, "source_revision": "test-revision", "document_id": "test-document",
            "requirements": [{"id": "R-1", "app": "Write", "operation": "edit-save-reopen",
                              "ui": "/write/:id editor", "domain_action": "Write.Save", "expected": "Edited content survives reopen",
                              "state": "VERIFIED", "required": True, "implementer": "fixture-implementer",
                              "reviewer": "fixture-reviewer", "gate_types": ["browser-runtime"], "test_ids": ["T-1"],
                              "source": {"index": 1, "end": 30, "text_sha256": validator.digest(self.paragraphs[0]["text"].encode())}}],
            "source_coverage": [{"index": 1, "end": 30, "text_sha256": validator.digest(self.paragraphs[0]["text"].encode()),
                                 "classification": "applicable", "requirement_ids": ["R-1"],
                                 "reviewer": "fixture-reviewer", "reason": "Persist operation is mapped."}]}
        self.requirements_hash = validator.digest(json.dumps(self.requirements).encode())
        self.candidate = {
            "schema_version": 1, "source_revision": "test-revision", "document_id": "test-document",
            "requirements_sha256": self.requirements_hash, "release_id": "fixture-only", "commit": "a" * 40,
            "environment": "staging", "browser_policy_status": "approved",
            "browsers": [{"id": "chromium-linux", "name": "Chromium", "version": "fixture-version", "platform": "Linux"}],
            "deployments": {role: {"id": f"fixture-{role}", "url": f"https://fixture.invalid/{role}", "source_commit": "a" * 40,
                                   "artifact_sha256": "b" * 64, "config_sha256": "c" * 64} for role in ("frontend", "backend")}}
        self.evidence = {
            "schema_version": 1, "source_revision": "test-revision", "document_id": "test-document",
            "requirements_sha256": self.requirements_hash, "release_id": "fixture-only", "commit": "a" * 40,
            "deployments": copy.deepcopy(self.candidate["deployments"]), "suites": [{
                "id": "S-1", "command": "fixture only", "fixture": "isolated synthetic evidence validation fixture",
                "started_at": "2026-10-03T00:00:00Z", "finished_at": "2026-10-03T00:01:00Z", "exit_status": 0,
                "log": self.log, "counts": {"discovered": 1, "executed": 1, **{outcome: int(outcome == "PASS") for outcome in validator.OUTCOMES}},
                "tests": [{"id": "T-1", "requirement_ids": ["R-1"], "outcome": "PASS", "kind": "browser-runtime",
                           "real_production_path": True, "mocks_used": False, "browser_id": "chromium-linux",
                           "expected": "Content saved", "observed": "Same content reopened", "assertions": ["Actual reopened content equals the edit"],
                           "log": self.log}]}]}

    @property
    def suite(self):
        return self.evidence["suites"][0]

    @property
    def test(self):
        return self.suite["tests"][0]

    @property
    def requirement(self):
        return self.requirements["requirements"][0]

    def defects(self):
        return validator.validate(self.source, self.paragraphs, self.requirements, self.candidate, self.evidence,
                                  self.requirements_hash, self.root, self.paragraphs_hash)

    def assert_code(self, code):
        self.assertIn(code, {error["code"] for error in self.defects()})

    def test_consistent_fixture_has_no_integrity_defects(self):
        self.assertEqual([], self.defects())

    def test_zero_discovery_is_rejected_even_with_successful_exit(self):
        self.suite["tests"] = []
        self.suite["counts"] = {"discovered": 0, "executed": 0, **{outcome: 0 for outcome in validator.OUTCOMES}}
        self.assert_code("ZERO_DISCOVERED_TESTS")
        self.assert_code("REQUIRED_GATE_UNCOVERED")

    def test_absent_suite_cannot_establish_acceptance(self):
        self.evidence["suites"] = []
        self.assert_code("ZERO_DISCOVERED_TESTS")

    def test_unmapped_operation_stays_in_denominator(self):
        missing = copy.deepcopy(self.requirement)
        missing.update(id="R-2", operation="restore-history", state="MISSING", test_ids=[])
        self.requirements["requirements"].append(missing)
        self.assert_code("UNMAPPED_REQUIREMENT")
        self.assert_code("REQUIREMENT_NOT_VERIFIED")
        self.assertEqual(2, len(self.requirements["requirements"]))

    def test_unmapped_source_paragraph_blocks_narrow_inventory(self):
        self.paragraphs.append({"index": 30, "end": 60, "heading": "HEADING_3", "text": "Reopen must preserve history."})
        self.assert_code("CANONICAL_SOURCE_UNMAPPED")

    def test_unjustified_exclusion_cannot_remove_source_requirement(self):
        self.requirements["source_coverage"][0].update(classification="excluded", requirement_ids=[])
        self.assert_code("EXCLUSION_BASIS_MISSING")

    def test_unrelated_test_cannot_impersonate_source_operation_coverage(self):
        paragraph = {"index": 30, "end": 60, "text": "Reopen must preserve history."}
        self.paragraphs.append(paragraph)
        self.requirements["source_coverage"].append({
            "index": 30, "end": 60, "text_sha256": validator.digest(paragraph["text"].encode()),
            "classification": "applicable", "requirement_ids": ["R-1"], "reviewer": "fixture-reviewer", "reason": "Wrong mapping negative control"})
        self.assert_code("SOURCE_MAPPING_CITATION_MISMATCH")

    def test_missing_operation_ui_or_domain_action_is_rejected(self):
        self.requirement.pop("domain_action")
        self.assert_code("OPERATION_MAPPING_MISSING")

    def test_stale_canonical_revision_is_rejected(self):
        self.evidence["source_revision"] = "older-revision"
        self.assert_code("STALE_SOURCE")

    def test_modified_requirement_inventory_is_rejected(self):
        self.evidence["requirements_sha256"] = "d" * 64
        self.assert_code("STALE_REQUIREMENT_INVENTORY")

    def test_stale_candidate_commit_is_rejected(self):
        self.evidence["commit"] = "d" * 40
        self.assert_code("STALE_COMMIT")

    def test_stale_deployment_configuration_is_rejected(self):
        self.evidence["deployments"]["backend"]["config_sha256"] = "e" * 64
        self.assert_code("STALE_DEPLOYMENT")

    def test_absent_deployment_identity_is_rejected(self):
        self.candidate["deployments"]["frontend"].pop("id")
        self.assert_code("DEPLOYMENT_IDENTITY_MISSING")

    def test_dev_server_cannot_pass_deployed_gate(self):
        self.candidate["environment"] = "development"
        self.assert_code("NOT_DEPLOYED_ENVIRONMENT")

    def test_failed_skipped_cancelled_unstable_and_blocked_are_not_passes(self):
        for outcome in validator.OUTCOMES - {"PASS"}:
            with self.subTest(outcome=outcome):
                self.test["outcome"] = outcome
                self.assert_code("NON_PASS_ACCEPTANCE")
                self.assert_code("REQUIRED_GATE_UNCOVERED")

    def test_mocked_or_static_test_cannot_cover_runtime(self):
        for change in ({"mocks_used": True}, {"real_production_path": False}, {"kind": "unit"}):
            with self.subTest(change=change):
                original = copy.deepcopy(self.test)
                self.test.update(change)
                self.assert_code("NON_RUNTIME_ACCEPTANCE")
                self.assert_code("REQUIRED_GATE_UNCOVERED")
                self.suite["tests"][0] = original

    def test_missing_browser_cannot_be_hidden_by_other_browser_pass(self):
        self.candidate["browsers"].append({"id": "firefox-linux", "name": "Firefox", "version": "fixture", "platform": "Linux"})
        self.assert_code("REQUIRED_GATE_UNCOVERED")

    def test_unknown_browser_policy_remains_blocked(self):
        self.candidate["browser_policy_status"] = "OPEN"
        self.assert_code("BROWSER_POLICY_UNRESOLVED")

    def test_swallowed_suite_failure_is_rejected(self):
        self.suite["exit_status"] = 1
        self.assert_code("SUITE_FAILED")

    def test_boolean_exit_status_cannot_impersonate_integer_zero(self):
        self.suite["exit_status"] = False
        self.assert_code("SUITE_FAILED")

    def test_unnamed_implementer_cannot_establish_independent_review(self):
        self.requirement.pop("implementer")
        self.assert_code("INDEPENDENT_REVIEW_MISSING")

    def test_pruned_source_snapshot_cannot_reuse_revision_label(self):
        self.paragraphs_hash = validator.digest(b"altered or pruned paragraphs")
        self.assert_code("PARAGRAPH_SNAPSHOT_MISMATCH")

    def test_count_claim_does_not_replace_individual_results(self):
        self.suite["counts"]["PASS"] = 1000
        self.assert_code("COUNT_MISMATCH")

    def test_missing_or_tampered_log_is_rejected(self):
        (self.root / "runtime.log").write_bytes(b"tampered\n")
        self.assert_code("ARTIFACT_HASH_MISMATCH")
        (self.root / "runtime.log").unlink()
        self.assert_code("ARTIFACT_MISSING")

    def test_evidence_path_escape_is_rejected(self):
        self.test["log"] = {"path": "../outside.log", "sha256": "a" * 64}
        self.assert_code("ARTIFACT_OUTSIDE_ROOT")

    def test_canonical_quote_tampering_is_rejected(self):
        self.requirement["source"]["text_sha256"] = "f" * 64
        self.assert_code("CANONICAL_CITATION_INVALID")

    def test_implemented_unverified_is_never_promoted(self):
        self.requirement["state"] = "IMPLEMENTED-UNVERIFIED"
        self.assert_code("REQUIREMENT_NOT_VERIFIED")
        self.assertEqual("IMPLEMENTED-UNVERIFIED", self.requirement["state"])

    def test_self_review_cannot_establish_verified_state(self):
        self.requirement["reviewer"] = self.requirement["implementer"]
        self.assert_code("INDEPENDENT_REVIEW_MISSING")

    def test_visual_gate_needs_runtime_trace_and_screenshot(self):
        self.requirement["gate_types"] = ["visual"]
        self.test["kind"] = "visual"
        self.assert_code("ARTIFACT_INVALID")

    def test_cross_client_gate_needs_all_three_real_observations(self):
        self.requirement["gate_types"] = ["cross-client"]
        self.test["kind"] = "cross-client"
        self.assert_code("CONTINUITY_JOURNEY_MISSING")

    def test_cross_client_changed_canonical_identity_is_rejected(self):
        self.requirement["gate_types"] = ["cross-client"]
        self.test["kind"] = "cross-client"
        self.test["client_observations"] = [
            {"surface": surface, "artifact_id": identity, "revision": "1", "content_sha256": "a" * 64,
             "permissions": "owner", "history": "1", "runtime_identity": "fixture-runtime"}
            for surface, identity in (("browser", "canonical"), ("desktop", "duplicate"), ("browser", "canonical"))]
        self.assert_code("CONTINUITY_ASSERTIONS_MISSING")

    def test_cli_malformed_input_writes_incomplete_report_and_exits_nonzero(self):
        (self.root / "bad.json").write_text("not JSON")
        report = self.root / "report.json"
        command = [sys.executable, str(MODULE_PATH)]
        for name in ("source-manifest", "paragraphs", "requirements", "candidate", "evidence"):
            command.extend(["--" + name, str(self.root / "bad.json")])
        command.extend(["--evidence-root", str(self.root), "--report", str(report)])
        result = subprocess.run(command, capture_output=True, text=True)
        self.assertEqual(2, result.returncode)
        parsed = json.loads(report.read_text())
        self.assertEqual("INCOMPLETE", parsed["release_status"])
        self.assertFalse(parsed["product_parity_verified"])


if __name__ == "__main__":
    unittest.main()
