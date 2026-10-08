# Candidate custody intake

This tool checks local custody and matching declarations. It does not build, install, launch, test, fetch, or evaluate a product. It cannot prove report-issuer authenticity, real workflow execution, deployed behavior, scope completeness, or acceptance. A structurally consistent synthetic handoff can satisfy these checks; the self-tests demonstrate that limitation intentionally.

The only successful tool status is **`CUSTODY_INTEGRITY_OK_PENDING_MANUAL_REVIEW`**. The candidate remains ineligible until an independent reviewer authenticates the Smoke Tester identity and the smoke assertions, confirms the artifact/source mapping, and reconciles the declared scope/platform matrix with the pinned authoritative specifications. A process exit code of zero is a custody-tool result only.

## Run

Requires Python 3.11+ and Git. Put a completed copy of `candidate-template.json` outside the product source checkout, together with local artifact and evidence files. Relative paths resolve against the candidate-manifest directory; smoke evidence paths resolve against the report directory; web observation paths resolve against the access-evidence directory. Source/lock paths resolve against `source.checkout`. Absolute local paths are supported.

```bash
python /workspace/9to1-acceptance/docs/acceptance/astra/intake.py /absolute/path/candidate.json
```

The template deliberately contains placeholders and must be rejected. Missing files, empty strings, placeholder strings, changed bytes, inconsistent identity/hash/source/platform declarations, missing scoped evidence, missing executed/PASS smoke assertions, or source checkout drift reject the handoff. Rejection reports the first failing check; correct it and rerun custody validation before manual review.

The source checkout must contain the **actual product candidate**, at its full exact committed revision, with matching `origin`, no tracked modifications and no untracked files. Index flags that hide changes (`assume-unchanged` or `skip-worktree`) reject the checkout. `source_files` must identify committed product source files; dependency and donor locks must also be inside that checkout and match the revision's committed bytes. Ignored build output is not treated as source drift, but its packaged bytes must be separately pinned as artifacts. Changes on this acceptance-preparation branch are testing preparation, not a candidate. Do not fabricate a candidate from those edits or run a product before eligible handoff.

Git fsmonitor hooks are disabled during these reads. Configured external clean/process filters reject the checkout before `git status` can execute them; this includes initialized submodule repositories. Use an independently prepared filter-free custody checkout when necessary rather than changing the original candidate configuration. The tool does not initialize missing submodules, so manual source completeness/provenance review remains required. Submodule-ignore configuration cannot hide dirty initialized submodules from the status check.

## Required declarations

- `candidate_id` and traceable `delivery_index_entry`; exact source repository URL, full commit hash, local clean checkout, and actual committed source files with hashes.
- At least one local packaged artifact per required platform; every artifact has an ID, platform, path, observed SHA-256 and exact source repository/revision. A URL without local bytes cannot satisfy this offline hash check.
- Both authoritative spec snapshots, provider revision IDs, local hashes, and hashed revision-evidence JSON containing matching `documentId`/`revisionId`: Development Specification `1TJx-TNQTHI5hhriRG4ipRjG65ud1ZmPAIYWAsC63kIg`; Post-Release Updates `1yZCIP-ogTPLBfqcnc5EFMsL2FO5DoLYBTbGGPTk7Aks`. Verify snapshot provenance and currency manually; a matching local metadata file alone cannot authenticate provider content.
- Committed hashed locks with roles `dependencies` and `donors`. A candidate with no applicable donors still needs an explicit provenance lock recording that reviewed state.
- Bounded `scope.claims` linked to spec IDs and exact requirement/section locators. Each claim names its required platforms. The `platform_matrix` contains platform IDs, `native`/`web` kind, boolean `required`, and spec citations. Every claim/platform row must cite at least one of the two authoritative specifications. Supplemental references cannot replace that citation. Exempt platforms additionally require `exemption_reason`; reviewers determine whether the cited spec actually permits the exemption. No app roster or operating-system roster is hardcoded.
- A hashed structured JSON report from the independent Smoke Tester, whose report ID/issuer match the manifest's `smoke` fields. The report must bind the candidate, exact source and every artifact/hash/platform, identify each platform environment/session, and attach hashed evidence for executed main workflows with reported `PASS` covering every declared claim/platform. `PASS` here is a report assertion whose truth must be independently reviewed, never a validator product verdict.

## Smoke report shape

```json
{
  "report_id": "tester-issued-report-identity",
  "issuer": "identifiable independent tester",
  "issuer_role": "Smoke Tester",
  "independent": true,
  "candidate_id": "same ID as manifest",
  "source_repository": "same exact repository as manifest",
  "source_revision": "same full revision as manifest",
  "tested_artifacts": [{"id": "package-ID", "platform": "platform-ID", "sha256": "same package hash"}],
  "environments": [{
    "platform": "platform-ID", "host": "machine/device identity", "os": "operating system",
    "os_version": "exact version", "architecture": "architecture", "runtime": "runtime/version",
    "session_id": "tested session identity"
  }],
  "main_workflows": [{
    "id": "workflow-ID", "platform": "platform-ID", "claim_ids": ["scope-claim-ID"],
    "executed": true, "result": "PASS",
    "evidence": {"path": "local-observed-workflow-evidence", "sha256": "evidence hash"}
  }]
}
```

Replace descriptive strings above with actual observed values and full hashes. This shape illustrates report fields, not a report or qualifying evidence.

## Web candidate binding

Each required web platform needs one exact bundle artifact and a `deployment` object in its platform row:

```json
{
  "deployment_id": "immutable-provider-version-ID",
  "immutable": true,
  "url": "actual deployment URL",
  "bundle_sha256": "the local deployed bundle artifact hash",
  "access_boundary": "actual tested authentication/access boundary",
  "session_id": "same tested session as the smoke environment",
  "access_evidence": {"path": "access-evidence.json", "sha256": "access report hash"}
}
```

`access-evidence.json` must repeat `deployment_id`, `url`, `bundle_sha256`, `access_boundary`, `session_id`, exact `source_repository`/`source_revision`, and include `executed: true`, `boundary_reached: true`, plus `observation: {"path": "observed-access-evidence", "sha256": "observed evidence hash"}`. The immutable declaration and matching session/access evidence are required; manual review must establish that the deployment ID really is immutable, the bundle was deployed there, and the observed session reached the relevant boundary. Mutable aliases such as `latest` cannot serve as deployment identity. Do not include session secrets or credentials.

Web-only limitations or engine exemptions must cite the relevant pinned specification and remain visible in scope. The tool checks the citation/bindings, not whether the exemption is substantively justified.

## Validator self-tests

```bash
python /workspace/9to1-acceptance/docs/acceptance/astra/intake-checks.py
```

The fixtures use temporary synthetic Git commits, fake package bytes, fake report issuers and fake workflow observations. Positive controls must return pending manual review only. Negative controls exercise package substitution, smoke/source mismatch, missing spec revision, unexecuted smoke, platform mismatch, changed evidence, source modifications and clean revision drift, missing locks/platform artifacts, placeholders, and web bundle/deployment/session access mismatch. These results are tool self-tests; **no product smoke or acceptance evidence is produced**.

Preserve the self-test output at `/workspace/astra-acceptance-evidence/custody/intake-self-tests.txt` for this preparation pass. Re-run the custody tool immediately before manual review to catch later local drift. It does not provide atomic filesystem isolation or a signature verification scheme.
