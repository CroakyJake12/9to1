# C5 semantic online-service requirement map

This is a manually decomposed **C5 service slice**, not the whole specification
denominator and not full SH-10/SH-11 product acceptance. It covers each sentence
in SH-10/SH-11, separating independent guarantees, plus relevant hosted Automation,
Agent and notification obligations. General platform/privacy/API contracts and
all owning-application requirements remain active in the root/A/B/C ledger.
Repeated source lines are deliberate: one sentence imposes several independently
testable obligations. Source-line or keyword counts do not establish coverage.

Canonical source: `1TJx-TNQTHI5hhriRG4ipRjG65ud1ZmPAIYWAsC63kIg`, revision
`AHj4eMQyVp2mJyiR-7I0WS7iJcwSYMmwoEkTb7sm6ijqRk99EaQHJbJWrLy37ObunOyOJESw8FHvgyJNClwj6QdcNpG8weoklA97x6a9tG0`,
fresh read `2026-10-03T17:30:25.713Z`. Snapshot
`/workspace/team-c/spec-fresh.txt` SHA256
`117677d224cbd1d3f909cd63d47b137b04d5ed4cb7e97a7c012b5443a92baca1`.
Fresh SH-10/SH-11 inspected at lines 9941–9955; deployed-service acceptance is
explicit at 9957 and independent operation at 9980. Automation detail is linked
at 5089, 5105, 5144, 5147, 5151, 5166, 5169 and 5181; Agent detail at 8489/8491;
notification identity/state at 1684/1686.

[c5-services.jsonl](c5-services.jsonl) has **72 semantic records**:

| Requirement state | Count | Meaning in this map |
|---|---:|---|
| VERIFIED | 0 | No deployed/provider service requirement has current acceptance proof |
| IMPLEMENTED-UNVERIFIED | 6 | Relevant implemented local behaviour exists; mandatory real hosted journey remains unverified |
| MISSING | 53 | Hosted implementation/integration is absent from verified main baseline; related source does not change that |
| BLOCKED | 13 | Explicit authority/configuration/approval/runtime prerequisite prevents affected acceptance |

All 72 prescribed real-service test outcomes are **NOT_RUN**. Requirement state,
local test result and deployed test outcome remain separate fields. A blocked
configuration does not erase missing implementation; each record also explains
dependencies and current evidence. These states are conservative source/evidence
assessments, not claims to have exhaustively executed every owning application's
tests. Root must reconcile duplicate cross-owner records before any combined count.

## Record schema

Every JSONL object includes stable `id`, canonical `source_revision`, snapshot
hash, exact source line and quote; observable `behavior` and `invariant`;
`normative_level`; supported-service/platform scope with explicit canonical
`platform_basis`; `primary_owner`, `owner_dependencies`; requirement `state` and
reason; revision-bound source evidence; separately labelled partial tests;
and prescribed `required_tests` with procedure, expected invariant, outcome and
authorisation boundary. `unmerged_dependency_evidence` identifies existing draft
authorities that must be reused without pretending they are accepted/deployed.

## Boundaries and dependencies

C5 owns gateway/provider adapters, hosted job/notification delivery and connected
service integration. Team A owns canonical job/Agent/Automation/model/permission,
notification and app-semantic contracts; Team B owns browser adapters/rendering;
C1 owns approved runtime/deployments/operations; C2 owns current canonical identity,
membership, sessions and grants; C3 owns authoritative personal/organisation
resources/Dust/entitlements; C4 owns canonical hosted artifacts and revisions.
Root integrates releases and C6 independently validates. Each record names the
dependencies rather than transferring their authority to C5.

C3 inspected unaccepted/unmerged Astra source
`a63d77fe5a9dfea56c938eaa85678a9e170368ec`: existing `AccountLedger` and
`IOrganisationCloudFundingAuthority` provide canonical funding/reservation/
dispatch-recheck/observed-settlement/cancellation. See
`/workspace/team-c/evidence/c3/draft-account-bridge.md`. A Team A acknowledged
transactional persistence/hosting bridge is required; single-host JSON/mutex
state is not a validated Cloudflare multihost authority. This map introduces
no alternate balance, price, period, quota or rollover policy.

Canonical SH-13 lines 9986–9990 leave commercial policy, production service
configuration, infrastructure budgets, supported browser/capacity and recovery
objectives open. Those inputs remain blockers only for their affected paths.
Real provider credentials, spending, live billing and production routing have
not been authorised or exercised by this documentation pass.

## Existing partial evidence

Verified remote main source: `98a08827c9fbc486fe987da73f4aee8398120d31`;
identity verified, full-specification acceptance **UNVERIFIED**.
Proposed C5 private provider repair: `4f6040271094748f9065b3b53f1239b0d7f2190d`.
Exact-head source-linked Release harness executed **16/16 local cases**, exit 0,
using the full real provider class and production source dependencies with fake
transport/configuration/secret fixtures. Ten stream cases and six endpoint
regressions cover completion/error/EOF/cancellation/usage capture locally.
Evidence: `/workspace/team-c/evidence/c5/exact-head/c5-exact-4f604027.trx`, SHA256
`b7bbd5fbf34c93ade5054bf0f2d5d24c291b7e5ba03019893c965e2713e55329`.
Original-source negative control failed 7/16, detecting the original defects.

This establishes **no real provider, deployed gateway, durable cloud job,
transactional settlement or end-to-end acceptance**. The historical C5 production-project
Debug/Release attempts based on main `98a08827` failed before discovery on
`CanvasArtifactSession.cs:511` CS1593. These are not failures against newer
unmerged Astra/Team A sources: root reports C6's current exact `a63d77f` Core run
386/386 PASS and Team A's registered integrator handover acknowledgement. That
source remains distinct from verified remote main and neither its Core result
nor the old main failure establishes hosted acceptance. Team A A1/A3 review of
C5 commit `4f604027` is pending. Team A ownership acknowledgement still
gates integration of the proposed private implementation. `[DONE]` framing
must be validated against every supported real compatible provider. C6's local
independent reproduction does not waive these gates. See the earlier
[C5 evidence](../C5-online-services-evidence.md) for exact commands and preserved
failures.

## Validation of this documentation

JSON parse, unique stable IDs, allowed states, source revision/hash and exact
source-line/quote correspondence, required test procedure/expected/outcome and
nonempty owner/platform basis are validated locally. Record counts and hashes
describe this artifact only, never overall release verification.

JSONL SHA256 is recorded in the final validation/handoff for this exact artifact.
