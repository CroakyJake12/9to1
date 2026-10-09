#!/usr/bin/env python3
"""Fail-closed review of browser acceptance records; does not execute acceptance."""
import argparse
import hashlib
import json
import re
from pathlib import Path

STATES = {"VERIFIED", "IMPLEMENTED-UNVERIFIED", "MISSING", "BLOCKED"}
OUTCOMES = {"PASS", "FAIL", "SKIPPED", "CANCELLED", "NOT-RUN", "UNSTABLE", "BLOCKED"}
KINDS = {"browser-runtime", "cross-client", "provider-runtime", "accessibility", "visual", "performance"}
SHA = re.compile(r"[0-9a-f]{64}\Z")
COMMIT = re.compile(r"[0-9a-f]{40}\Z")


def digest(data):
    return hashlib.sha256(data).hexdigest()


def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def validate(source, paragraphs, requirements, candidate, evidence, requirements_hash, evidence_root, paragraphs_hash):
    """Return defects without changing supplied states or hiding blocked rows."""
    errors = []

    def reject(code, target, detail):
        errors.append({"code": code, "target": target, "detail": detail})

    def nonempty(value):
        return isinstance(value, str) and bool(value.strip())

    def artifact(record, target):
        if not isinstance(record, dict) or not nonempty(record.get("path")) or not SHA.fullmatch(str(record.get("sha256", ""))):
            reject("ARTIFACT_INVALID", target, "A relative path and SHA-256 are required.")
            return
        path = Path(record["path"])
        root = Path(evidence_root).resolve()
        resolved = (root / path).resolve()
        if path.is_absolute() or not resolved.is_relative_to(root):
            reject("ARTIFACT_OUTSIDE_ROOT", target, "Evidence must stay inside the evidence root.")
            return
        if not resolved.is_file() or resolved.stat().st_size == 0:
            reject("ARTIFACT_MISSING", target, record["path"])
        elif digest(resolved.read_bytes()) != record["sha256"]:
            reject("ARTIFACT_HASH_MISMATCH", target, record["path"])

    revision = source.get("revisionId")
    if not SHA.fullmatch(str(source.get("paragraphs_sha256", ""))) or source.get("paragraphs_sha256") != paragraphs_hash:
        reject("PARAGRAPH_SNAPSHOT_MISMATCH", "source", "Exact paragraph snapshot bytes must match the live-fetch manifest hash.")
    if not nonempty(source.get("documentId")) or not nonempty(revision):
        reject("SOURCE_IDENTITY_MISSING", "source", "Document and current live revision IDs are required.")
    for label, doc in (("requirements", requirements), ("candidate", candidate), ("evidence", evidence)):
        if doc.get("schema_version") != 1:
            reject("SCHEMA_VERSION", label, "Only schema version 1 is supported.")
        if doc.get("source_revision") != revision or doc.get("document_id") != source.get("documentId"):
            reject("STALE_SOURCE", label, "Document/revision does not match the current canonical snapshot.")
    if candidate.get("requirements_sha256") != requirements_hash or evidence.get("requirements_sha256") != requirements_hash:
        reject("STALE_REQUIREMENT_INVENTORY", "inventory", "Candidate and evidence must pin the exact reviewed inventory bytes.")
    if not COMMIT.fullmatch(str(candidate.get("commit", ""))):
        reject("COMMIT_IDENTITY_MISSING", "candidate", "A full tested Git commit is required.")
    if not nonempty(candidate.get("release_id")) or evidence.get("release_id") != candidate.get("release_id"):
        reject("STALE_RELEASE", "evidence", "Evidence must identify the same release candidate.")
    if evidence.get("commit") != candidate.get("commit"):
        reject("STALE_COMMIT", "evidence", "Evidence commit differs from candidate.")
    if candidate.get("environment") not in {"staging", "production"}:
        reject("NOT_DEPLOYED_ENVIRONMENT", "candidate", "A dev server or local build cannot pass deployed acceptance.")
    if candidate.get("browser_policy_status") != "approved":
        reject("BROWSER_POLICY_UNRESOLVED", "candidate", "Supported browsers need an explicit approved policy.")
    browsers = candidate.get("browsers", [])
    browser_ids = set()
    if not isinstance(browsers, list) or not browsers:
        reject("BROWSER_MATRIX_EMPTY", "candidate", "At least one declared supported browser/version is required.")
        browsers = []
    for browser in browsers:
        if not isinstance(browser, dict) or not all(nonempty(browser.get(key)) for key in ("id", "name", "version", "platform")):
            reject("BROWSER_IDENTITY_MISSING", "candidate", "Browser ID, name, exact version and platform are required.")
            continue
        if browser["id"] in browser_ids:
            reject("DUPLICATE_BROWSER", browser["id"], "Browser matrix IDs must be unique.")
        browser_ids.add(browser["id"])

    deployments = candidate.get("deployments", {})
    if not isinstance(deployments, dict):
        deployments = {}
    for role in ("frontend", "backend"):
        deployment = deployments.get(role, {})
        if not isinstance(deployment, dict):
            deployment = {}
        if not all(nonempty(deployment.get(key)) for key in ("id", "url", "source_commit")) or not all(SHA.fullmatch(str(deployment.get(key, ""))) for key in ("artifact_sha256", "config_sha256")):
            reject("DEPLOYMENT_IDENTITY_MISSING", role, "Deployment ID, URL, source commit, artifact/config hashes are required.")
        if not COMMIT.fullmatch(str(deployment.get("source_commit", ""))):
            reject("DEPLOYMENT_COMMIT_INVALID", role, "Deployment source commit must be a full Git hash.")
        if not str(deployment.get("url", "")).startswith("https://"):
            reject("DEPLOYMENT_URL_INVALID", role, "Deployed acceptance requires HTTPS.")
        if role == "frontend" and deployment.get("source_commit") != candidate.get("commit"):
            reject("STALE_FRONTEND", role, "Frontend source commit must match the delivered browser candidate.")
    if evidence.get("deployments") != deployments:
        reject("STALE_DEPLOYMENT", "evidence", "Exact frontend/backend IDs, source and configuration must match.")

    source_rows = {(row["index"], row["end"]): row for row in paragraphs}
    rows = requirements.get("requirements", [])
    if not isinstance(rows, list) or not rows:
        reject("EMPTY_REQUIREMENT_INVENTORY", "requirements", "An empty inventory cannot establish parity.")
        rows = []
    requirement_ids = set()
    requirements_by_id = {}
    for row in rows:
        if not isinstance(row, dict):
            reject("REQUIREMENT_INVALID", "inventory", "Requirement must be an object.")
            continue
        rid = row.get("id", "<missing>")
        if not nonempty(rid) or rid in requirement_ids:
            reject("DUPLICATE_OR_MISSING_REQUIREMENT", str(rid), "Every requirement needs a unique ID.")
        requirement_ids.add(rid)
        requirements_by_id[rid] = row
        if not all(nonempty(row.get(key)) for key in ("app", "operation", "expected", "ui", "domain_action")):
            reject("OPERATION_MAPPING_MISSING", str(rid), "Map the actual operation, UI, domain action and expected outcome.")
        if row.get("state") not in STATES:
            reject("STATE_INVALID", str(rid), "Preserve the four canonical requirement states.")
        if row.get("required") is not True:
            reject("REQUIREMENT_LEVEL_UNREVIEWED", str(rid), "This release inventory contains applicable mandatory/default-required criteria only; approved exclusions remain separate.")
        origin = row.get("source", {})
        original = source_rows.get((origin.get("index"), origin.get("end")))
        if original is None or digest(original["text"].encode()) != origin.get("text_sha256"):
            reject("CANONICAL_CITATION_INVALID", str(rid), "Citation must identify an exact paragraph and its text hash.")
        if row.get("state") != "VERIFIED":
            reject("REQUIREMENT_NOT_VERIFIED", str(rid), str(row.get("state")))
        if not nonempty(row.get("implementer")) or not nonempty(row.get("reviewer")) or row.get("reviewer") == row.get("implementer"):
            reject("INDEPENDENT_REVIEW_MISSING", str(rid), "Verification requires a named reviewer distinct from the implementer.")
        if not isinstance(row.get("test_ids"), list) or not row["test_ids"]:
            reject("UNMAPPED_REQUIREMENT", str(rid), "No acceptance test IDs map to this criterion.")
        gates = row.get("gate_types", [])
        if not isinstance(gates, list) or not gates or any(gate not in KINDS for gate in gates):
            reject("GATE_MAPPING_INVALID", str(rid), "Required runtime gate types must be explicit.")

    # Every nonempty source paragraph needs an explicit disposition. Headings can
    # contain contractual sentences, so never filter by heading style or MUST regex.
    coverage = requirements.get("source_coverage", [])
    if not isinstance(coverage, list):
        coverage = []
    dispositions = {}
    for disposition in coverage:
        key = (disposition.get("index"), disposition.get("end"))
        original = source_rows.get(key)
        if key in dispositions:
            reject("DUPLICATE_SOURCE_DISPOSITION", str(key), "Source dispositions must be unique.")
        dispositions[key] = disposition
        if original is None or digest(original["text"].encode()) != disposition.get("text_sha256"):
            reject("SOURCE_DISPOSITION_INVALID", str(key), "Disposition must preserve exact canonical source text identity.")
        if not nonempty(disposition.get("reviewer")) or not nonempty(disposition.get("reason")):
            reject("SOURCE_DISPOSITION_UNREVIEWED", str(key), "Every applicability decision requires a named reviewer and reason.")
        classification = disposition.get("classification")
        mapped_ids = disposition.get("requirement_ids", [])
        if classification == "applicable":
            if not mapped_ids or any(rid not in requirement_ids for rid in mapped_ids):
                reject("SOURCE_OPERATION_UNMAPPED", str(key), "Applicable source must map to declared requirement operations.")
            elif any((requirements_by_id[rid].get("source", {}).get("index"), requirements_by_id[rid].get("source", {}).get("end")) != key for rid in mapped_ids):
                reject("SOURCE_MAPPING_CITATION_MISMATCH", str(key), "Mapped operations must retain this paragraph's source citation.")
        elif classification == "excluded":
            basis = disposition.get("canonical_basis", {})
            basis_row = source_rows.get((basis.get("index"), basis.get("end")))
            if basis_row is None or digest(basis_row["text"].encode()) != basis.get("text_sha256"):
                reject("EXCLUSION_BASIS_MISSING", str(key), "Excluded requirements need an exact canonical basis, not worker preference.")
        elif classification != "non-requirement":
            reject("SOURCE_CLASSIFICATION_INVALID", str(key), "Source is applicable, excluded with canonical basis, or reviewed non-requirement.")
    for key, original in source_rows.items():
        if original["text"].strip() and key not in dispositions:
            reject("CANONICAL_SOURCE_UNMAPPED", str(key), "Nonempty canonical paragraph has no reviewed disposition; full coverage remains unestablished.")

    suites = evidence.get("suites", [])
    if not isinstance(suites, list) or not suites:
        reject("ZERO_DISCOVERED_TESTS", "evidence", "No browser acceptance suite results are supplied.")
        suites = []
    all_tests = {}
    suite_ids = set()
    for suite in suites:
        sid = suite.get("id", "<missing>")
        if not nonempty(sid) or sid in suite_ids:
            reject("SUITE_ID_INVALID", str(sid), "Suite IDs must be unique.")
        suite_ids.add(sid)
        if type(suite.get("exit_status")) is not int or suite["exit_status"] != 0:
            reject("SUITE_FAILED", str(sid), "Suite process did not exit successfully.")
        if not all(nonempty(suite.get(key)) for key in ("command", "fixture", "started_at", "finished_at")):
            reject("REPRODUCTION_MISSING", str(sid), "Exact command/steps, fixture and timestamps are required.")
        artifact(suite.get("log"), str(sid))
        tests = suite.get("tests", [])
        if not isinstance(tests, list):
            tests = []
        counts = suite.get("counts", {})
        if not isinstance(counts, dict):
            counts = {}
        for key in ("discovered", "executed"):
            if type(counts.get(key)) is not int or counts[key] <= 0:
                reject("ZERO_DISCOVERED_TESTS" if key == "discovered" else "ZERO_EXECUTED_TESTS", str(sid), key)
        actual = {outcome: sum(test.get("outcome") == outcome for test in tests) for outcome in OUTCOMES}
        if counts.get("discovered") != len(tests) or counts.get("executed") != sum(actual[outcome] for outcome in ("PASS", "FAIL", "UNSTABLE")) or any(counts.get(outcome) != actual[outcome] for outcome in OUTCOMES):
            reject("COUNT_MISMATCH", str(sid), "Every discovered result must be retained and all counts must match individual outcomes.")
        for test in tests:
            tid = test.get("id", "<missing>")
            if not nonempty(tid) or tid in all_tests:
                reject("TEST_ID_INVALID", str(tid), "Test IDs must be globally unique.")
            all_tests[tid] = test
            if test.get("outcome") not in OUTCOMES:
                reject("OUTCOME_INVALID", str(tid), "Unknown test outcome.")
            if test.get("outcome") != "PASS":
                reject("NON_PASS_ACCEPTANCE", str(tid), str(test.get("outcome")))
            if test.get("kind") not in KINDS or test.get("real_production_path") is not True or test.get("mocks_used") is not False:
                reject("NON_RUNTIME_ACCEPTANCE", str(tid), "Unit, static, fake or mocked results cannot satisfy required runtime acceptance.")
            if not all(nonempty(test.get(key)) for key in ("expected", "observed")) or not isinstance(test.get("assertions"), list) or not test["assertions"] or not all(nonempty(a) for a in test["assertions"]):
                reject("OUTCOME_ASSERTIONS_MISSING", str(tid), "Record expected/observed outcomes and actual substantive assertions.")
            if test.get("browser_id") not in browser_ids:
                reject("BROWSER_RUNTIME_UNDECLARED", str(tid), "Result must use an exact supported browser identity.")
            if not isinstance(test.get("requirement_ids"), list) or not test["requirement_ids"] or any(rid not in requirement_ids for rid in test["requirement_ids"]):
                reject("UNDECLARED_REQUIREMENT", str(tid), "Test must map to known canonical criteria.")
            artifact(test.get("log"), str(tid))
            if test.get("kind") in {"visual", "accessibility"}:
                artifact(test.get("trace"), str(tid) + "/trace")
            if test.get("kind") == "visual":
                artifact(test.get("screenshot"), str(tid) + "/screenshot")
            if test.get("kind") == "performance" and not test.get("measurements"):
                reject("MEASUREMENTS_MISSING", str(tid), "Performance requires workload and measured results.")
            if test.get("kind") == "cross-client":
                observations = test.get("client_observations", [])
                if len(observations) < 3 or [item.get("surface") for item in observations] != ["browser", "desktop", "browser"]:
                    reject("CONTINUITY_JOURNEY_MISSING", str(tid), "Retain browser → desktop → browser observations.")
                elif len({item.get("artifact_id") for item in observations}) != 1 or not all(all(nonempty(item.get(key)) for key in ("artifact_id", "revision", "content_sha256", "permissions", "history", "runtime_identity")) for item in observations):
                    reject("CONTINUITY_ASSERTIONS_MISSING", str(tid), "Assert canonical ID, content, revisions, permissions, history and native/browser runtime identities.")

    for row in rows:
        if not isinstance(row, dict):
            continue
        rid = row.get("id")
        mapped = []
        for tid in row.get("test_ids", []):
            test = all_tests.get(tid)
            if test is None:
                reject("MAPPED_TEST_MISSING", str(rid), str(tid))
            elif rid not in test.get("requirement_ids", []):
                reject("MAPPING_NOT_RECIPROCAL", str(rid), str(tid))
            else:
                mapped.append(test)
        for browser_id in browser_ids:
            for gate in row.get("gate_types", []):
                if not any(test.get("browser_id") == browser_id and test.get("kind") == gate and test.get("outcome") == "PASS" and test.get("real_production_path") is True and test.get("mocks_used") is False for test in mapped):
                    reject("REQUIRED_GATE_UNCOVERED", str(rid), f"{browser_id}/{gate}")
    return errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("source-manifest", "paragraphs", "requirements", "candidate", "evidence", "evidence-root", "report"):
        parser.add_argument("--" + name, required=True)
    args = parser.parse_args()
    try:
        requirements = load(args.requirements)
        evidence = load(args.evidence)
        errors = validate(load(args.source_manifest), load(args.paragraphs), requirements, load(args.candidate), evidence,
                          digest(Path(args.requirements).read_bytes()), args.evidence_root, digest(Path(args.paragraphs).read_bytes()))
        report = {"tool": "browser-acceptance-evidence-validator", "release_status": "EVIDENCE-CONSISTENT" if not errors else "INCOMPLETE",
                  "product_parity_verified": False, "inventory_complete": False,
                  "supplied_requirements_total": len(requirements.get("requirements", [])),
                  "supplied_suites": len(evidence.get("suites", [])), "defects": errors,
                  "limitation": "Checks record integrity only. Independent semantic/runtime review and complete canonical inventory are still required; this tool never promotes a product to VERIFIED."}
        status = 0 if not errors else 1
    except (OSError, ValueError, KeyError, TypeError, AttributeError) as exc:
        report = {"release_status": "INCOMPLETE", "product_parity_verified": False, "inventory_complete": False,
                  "defects": [{"code": "INPUT_INVALID", "target": "inputs", "detail": str(exc)}]}
        status = 2
    destination = Path(args.report)
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"{report['release_status']}: {len(report['defects'])} evidence defects; product parity is not inferred.")
    return status


if __name__ == "__main__":
    raise SystemExit(main())
