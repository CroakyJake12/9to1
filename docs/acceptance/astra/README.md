# Astra independent acceptance preparation

State: **PREPARATION COMPLETE when the mapping/tool checks pass; PRODUCT ACCEPTANCE NOT EXECUTED**. This directory is maintained on the isolated `acceptance/astra-2026-10-08` branch, based on `98a08827c9fbc486fe987da73f4aee8398120d31`. It does not qualify that older public checkout or newer private Astra source checkpoints as a product candidate.

The current delivery index reports zero whole-app SMOKE-PASSED candidates. No packaged product or eligible Smoke Tester handoff is available locally. Historical component tests, source-recovery ZIPs, successful compilers and rendered test fixtures cannot substitute for an exact smoke-qualified product artifact. Other chats have separate filesystems.

The root-owned [durable pipeline queue](https://drive.google.com/file/d/1QcMJjmi8YZW8_RJRwdEFatGhvmaAao8u/view), version 1 updated at 2026-10-08T18:56:26.572493+00:00, has an empty `items` array. Motion Source53 is being prepared for a Smoke handoff and is not full-app qualified. The integration owner remains the sole queue writer; testers return immutable evidence through the existing private coordination. Its expanded app stage order is IMPLEMENTED → BUILD-VERIFIED → PACKAGEABLE → PACKAGED → SMOKE-PASSED → ACCEPTANCE-PASSED → EXTENSIVELY VERIFIED. Those app stages are separate from per-requirement result classifications.

## Authority and scope

- [Development Specification](https://docs.google.com/document/d/1TJx-TNQTHI5hhriRG4ipRjG65ud1ZmPAIYWAsC63kIg/edit) is the product authority. The fetched snapshot was modified at 2026-10-08T09:47:47.491Z.
- [Post-Release Updates](https://docs.google.com/document/d/1yZCIP-ogTPLBfqcnc5EFMsL2FO5DoLYBTbGGPTk7Aks/edit) supplies current scope decisions. The snapshot was modified at 2026-10-07T16:29:09.962Z.
- The snapshot manifest pins readable text hashes and separately observed provider revision IDs. These are retrieval evidence; a separately read revision is not an atomic export/revision guarantee. Refresh relevant sections and check for changes before each candidate's acceptance.
- The current order is shared frameworks/runtime, Spaces, Dev and standalone Assistants, then Home and subsequent products. The Agentic Update is current scope. Former Lunar Eclipse LE-01–LE-15 is incorporated into Pass 1; retained historical headings do not defer it. Explicit later supersessions take precedence. Other post-release additions require the actual documented scope decision.
- The source says web is required except for explicitly justified exemptions. Mini Computer and Browser have current stated exemptions; this does not exempt useful browser workflows in Spaces, Dev or Assistants. Native-only dependencies must be capability-negotiated honestly.

The JSON files are an initial **test-family mapping**, not an exhaustive atomic requirement catalogue or completed product verification. Before full acceptance, reconcile every applicable paragraph and donor capability against the candidate's complete mandatory requirement list; split grouped families into independently recordable assertions and platform results. No specification reduction is authorised by this preparation.

## Candidate admission

Receive an immutable handoff from the existing Smoke Tester. Preserve its original report and failures. Require exact source repository/revision and donor/dependency locks, available Windows package or exact web deployment/bundle, package hashes, installation/runtime environment, executed main-workflow smoke evidence, source/package/platform bindings and applicable specification snapshots.

Use `intake.py` to check local byte integrity and identity bindings. Its positive result means custody integrity pending manual review; it cannot prove issuer authenticity, truth of a smoke assertion, runtime usability, or acceptance. Authenticate the Smoke Tester handoff and inspect the actual original evidence before admitting it. A template, source ZIP, launch-only shell or unexecuted smoke report is insufficient.

Create a separate disposable profile/data directory and isolated source checkout from the admitted revision. Execute the received package; a newly rebuilt package has a new identity and must return through Smoke Tester. Do not inspect, edit or reuse another chat's paths as if they were local. Do not edit the integration owner's branch.

## Execution and evidence

For each atomic requirement and each applicable platform, retain the original assertion and classify exactly `PASS`, `FAIL`, `NOT EXECUTED`, or `NOT APPLICABLE`. A PASS needs direct evidence against the final candidate. A NOT APPLICABLE needs the exact scoped specification exception or capability rule. Missing hosts, models, accounts, artifacts or evidence mean NOT EXECUTED, never PASS.

Capture source/artifact/dependency/spec identity, environment, timestamp, exact commands or user steps, observed/expected behaviour, canonical entity IDs, logs/results and actual rendered screenshots where relevant. Retain first failures and original assertions; record repairs and reruns as new evidence. Prefer semantic selectors for repeated UI tests and inspect actual screenshots for visual conclusions. Windows and web results are independent; Linux headless checks do not establish Windows input/accessibility/install behaviour.

Use genuine workflows and the original donor/shared engine. Compare resulting canonical state through UI and typed API paths. Exercise save/close/reopen, interruption/recovery, permission denial/revocation, error/cancellation and resource pressure. Recheck only affected requirements after changes unless new evidence justifies broader checks. Runtime/platform work remains pending until available; no simulated model/tool results or sample repository can prove the required Assistant-initiated canonical 9to1 development journey.

## Defects and routing

A minor contained correction may be made on a separate isolated repair branch. Send the changed source to Smoke Tester for affected build/package/smoke requalification, then rerun affected acceptance against the replacement artifact. Preserve valid unaffected evidence only when its source/dependency/platform closure still matches.

For a major omission, architecture/donor-parity failure or fundamental product-fit defect, return a report to the existing Astra Implementer with exact requirement/citation, observed and expected behaviour, reproduction, evidence, source/package/spec identity and recommended scope. Do not create a competing implementation of a shared subsystem or stop healthy unrelated upstream work.

Report accepted applications to the existing Astra coordinator and shared delivery index only after all mandatory applicable requirements pass on the final candidate, including required platform execution. Preserve every FAIL/NOT EXECUTED. Preparation, STAGED and SMOKE-PASSED are separate states from full VERIFIED. No deployment/release is performed by this preparation.

## Files

`common.requirements.json`, `framework.requirements.json`, `spaces.requirements.json` and `dev-assistants.requirements.json` contain source-cited families. Their companion Markdown explains domain scope. `snapshot-manifest.json` pins the authority evidence. `candidate-template.json`, `intake.py`, `intake-checks.py` and `intake.md` support custody checks. `validate-mappings.py` checks citation and preparation-state integrity; its results are tooling checks only.

The durable handoff bundle contains these files, snapshots, original local custody inventory, index retrieval evidence and tooling-check results. No product test result is claimed.
