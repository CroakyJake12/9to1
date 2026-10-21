# Lane 6 — Automations — Pass 8 (Durable resume: real claim settlement, historical-pin, owner-backed registry adapter)

- Date: 2026-10-20
- Branch: `codex/mercury-automations-20261020` (all work uncommitted; no commit/push)
- Source identity: `/workspace/9to1-mercury-automations/9to1 Workspace/shared/src` (Haven.Application / Haven.Infrastructure / Haven.Core) and `/workspace/9to1-mercury-automations/9to1 Workspace/Browse/Host.Tests`.
- Authority: `AGENTS.md` (owner refs, specification `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`). Shared NodeGraph contract frozen at `d46dd7c18b7d3d27b94884aed479806314d276f2`; this worker did NOT edit shared NodeGraph source/tests.
- Delivery model: source + tests only. NO .NET/build/restore/test execution (root owns SDK gate). All new code/tests NOT RUN.

## Scope / requirement IDs
Spec 8199–8207, 8227–8239, 8245–8251, 8330; APP-AC-001..007. Pass8 corrects the seven confirmed blockers on the pass5/7 durable resume surface.

## Pass 8 changes (this turn)

### Contracts — `Haven.Application/Automations/AutomationRunContinuationContracts.cs`
- Added `AutomationRunClaimSettlementKind` (Suspend/Complete/ResumeCancelled/ResumeFault/MalformedResume) and `AutomationRunClaimSettlement` record (retains exact prepared run-json/next-run-at/error).
- Extended `IAutomationRunClaimStore` with `GetResumableRunByIdAsync`, `RecordUncertainSettlementAsync`, `GetUncertainSettlementsAsync` (by-ID approval read, uncertain settlement record/read).

### Migration
- New `Haven.Infrastructure/Database/Migrations/AutomationRunClaimSettlementMigration.cs` (v31): adds `settlement_kind / settlement_run_json / settlement_next_run_at / settlement_error / settled_at` to `automation_run_claims`; registered in `Migrations.cs`.
- `AutomationRunClaimStoreMigration.cs` (v30) comment softened: claim is a fence, never permission; only the repository mints a claim token; address descriptors/actors/snapshots are evidence, not live grants.

### Claim store — `AutomationRepository.RunClaims.cs`
- `TryAcquireRunClaimAsync`: eligibility gate `IsEligibleClaimStatus` (Waiting/ApprovalRequired/RetryPending only; terminal/running/unknown refuse) + definition-token equality via `ReadDefinitionStateAsync` (definition must exist, be enabled, and still hold exactly the Run's original lease token → `definition.token.lost`). `GetResumableRunByIdAsync` (bounded by-ID due read). `RecordUncertainSettlementAsync` retains claim row with settlement. `GetUncertainSettlementsAsync`. Removed old `ReadDefinitionEnabledAsync`.
- `AutomationRepository.cs`: `TryAcquireLeaseAsync` now carries the SAME active same-Run fence as GetDue (`NOT EXISTS` active non-terminal run with `r.lease_token = automations.lease_token`). Added+then-removed `GetRunByIdAsync` on `IAutomationRepository` (reverted; see adapter seam below).

### Coordinator — `AutomationRunContinuationCoordinator.cs`
- `SweepResumeAsync` no longer releases the claim in an unconditional `finally`. It reconciles retained uncertain settlements first, claims, resumes, and:
  - pre-effect refusal (`Accepted=false`) → release;
  - re-suspend/terminal persist via `PersistUnderClaimAsync` → on failure/ACK-loss/cancellation/malformed result, `RecordUncertainAsync` records the EXACT prepared operation onto the retained claim (never unsafe-release) and only releases after authoritative durable settlement;
  - `ReconcileSweepAsync` replays ONLY the exact persisted operation (SuspendRunAsync/CompleteRunAsync with stored run-json/next-run-at) — no fresh graph execution, no re-run of B.
- `ApplyApprovalDecisionAsync` uses bounded by-ID `GetResumableRunByIdAsync` (no first-100 pagination), binds current actor via fail-closed authority, refuses recorded duplicate (`approval.decision.duplicate`) and concurrent conflict (`approval.decision.conflict`).

### Historical pin — `AutomationPinnedExecutionSnapshot.cs` + `ScheduledTaskRuntime.cs`
- New authenticated `AutomationPinnedExecutionSnapshot` (+ codec with self-integrity SHA-256 and round-trip determinism).
- `ExecuteDurableGraphCaptureAsync` stores the EXACT executed trigger-scoped projection (graph-json + GraphId/Revision/Schema/Digest + trigger + capability profile) as `AdmissionSnapshotJson`.
- `ResumeScheduledGraphAsync` reloads exactly that snapshot, validates pin/continuation/schema/digest consistency, and executes the snapshot graph — the current Active task payload is never substituted under an old label.

### Owner-backed registry adapter — `AutomationNodeGraphDurableAdapter.cs` (new)
- Implements `INodeGraphOwnerBackedContinuationAdapter`. `AdmitAsync` compiles the canonical Active `GraphDocument` from `IVersionedNodeGraphRepository` via `NodeGraphAutomationAdapter.Compile`. `AdmitResumeAsync` reloads the exact persisted Run, verifies envelope↔continuation pin and kind, applies approval authority gate, and atomically acquires the real claim before returning the detached binding (null = deny/revoke). `ResumeAsync` bridges to `ScheduledTaskRunner.ResumeScheduledGraphAsync`, persists under the claim, records uncertain settlement + retains claim on persist failure, and reports re-suspend as a yield. `ExecuteAsync` fail-closed for fresh (legacy durable start path remains on the scheduler).

## Tests (NOT RUN — root owns SDK gate)
New `HavenOS.Browse.Host.Tests/AutomationPass8UncertainSettlementAndPinTests.cs` (registered in `HavenOS.Browse.Host.Tests.csproj`), real migrated SQLite (schema v30/v31), store reopen, no sleeps/forged tokens:
1. Foreign definition token and terminal status cannot claim (`definition.token.lost`, `claim.status.ineligible`).
2. Direct `TryAcquireLeaseAsync` cannot steal an expired occupied Wait (active same-Run fence); fresh lease works after terminal.
3. Uncertain settlement retains claim; exact reconciliation replays Complete without re-executing B (counting resumer + blocking trigger).
4. Malformed resumer result retains the claim (never unsafe-release).
5. Historical pin executes the OLD pinned revision (4) even when a newer Active payload (rev 5, invalid B, same node IDs) is published.
6. Approval by-ID not hidden by 150 other waiting Runs; conflicting duplicate decision refuses.
7. Owner-backed adapter fails closed on forged/stale/revoked admission; authorised approval proceeds (real claim acquired).

## NOT RUN / BLOCKED external seams
- No .NET builds/tests run this turn (root gates SDK). All source/test above is authored and reviewed, not executed.
- Full resume-through-`NodeGraphRuntimeRegistry` wiring requires canonical GraphDocuments + registered automation node types/profiles from the Graph worker's domain, and envelope production/persistence; stays fail-closed / as an admitted seam and is NOT end-to-end wired. `AdmitAsync` returns null when the versioned graph repository serves no Active revision (fail-closed).
- Fresh start (`ExecuteAsync`) intentionally remains on the scheduler legacy durable start path; the adapter is resume-only.

## Preservation
- Pass5/7 gates preserved (terminal drafts/causes/ACK semantics, immutable token/pins, null-token history append, rollback; no test weakening, no forged SQL tokens, no .NET runs).
- No edits to shared NodeGraph source/tests (`NodeGraphRuntimeRegistry.cs`, `GraphRuntimeContinuation*`, `NodeGraphSchema.cs`, `NodeGraphRepository.cs`).
- Existing `IAutomationRepository` implementers unchanged (level-`GetRunByIdAsync` interface addition).
