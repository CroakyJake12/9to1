# LANE 6 — PASS 17 — Automations12 Shipping path source repair (production caller + owning tests)

## Status
- Lane: `parallel16-automations14-accepted-graph-20261021` — **the** shipping path source repair (real production code + owning tests).
- Date: Wed 2026-10-21.
- Source identity: HEAD `fb70840d561286ecd46bd62b3c0736bf7d24108b` (branch `codex/mercury-automations-20261020`). **All changes are uncommitted working-tree edits; no commit/push.**
- Preserved: 44 accepted Graph tests + root registry `9to1` untouched. **No manual parallel registry replacing production proof.**
- No .NET build/test slot → **static compile audit only; all new Facts registered and `NOT RUN` pending owner compile.**

## 4 review items — how each is closed
1. **Production invokes the registered gateway** — new `AutomationShippingScheduler` drives the registered `AutomationShippingComposition.IAutomationRunGateway` (fresh/run-now/resume). Added `IAutomationRunAuditDrain` seam so the scheduler drains retained Home audits **without** re-running graph effects. Registered in `AddAutomationShipping`.
2. **Exact Run/concurrent original authority** — `HomeAutomationRunAuthority` now wraps bind-check/create in `SemaphoreSlim _authorizeGate` so concurrent identical admits coalesce into ONE original Home review; independent operations stay per-operation-key.
3. **Audit/claim-loser settlement + drain with production caller** — claim-loser settles the already-claimed Home handle (`succeeded=false`, code `graph.automation.claim.lost`) and surfaces unsettled audit via `WithUnsettledAudit`; scheduler's `DrainPendingAuditsAsync` calls the `IAutomationRunAuditDrain` implementation on the real authority; `StopAutomationScheduler` drains on close.
4. **Compiler/API call-site fixes** — `AutomationGraphContinuation` ctor calls pass `0` for `AuthorizedStepLimit` (2 test files); scheduler/resolver/authority/DI signatures corrected.

## Exact changed paths (all uncommitted)
- `shared/src/Haven.Infrastructure/Automations/AutomationShippingScheduler.cs` — **new** production gateway consumer: `RunDueAsync`/`RunNowAsync`/`ResumeAsync`/`DrainPendingAuditsAsync`; exposes `Gateway`.
- `shared/src/Haven.Infrastructure/Automations/AutomationShippingComposition.cs` — `AddAutomationShipping` registers `AutomationShippingScheduler`; scheduler now depends on `IAutomationRunAuditDrain?` (was `IAutomationRunAuthority?`).
- `shared/src/Haven.Infrastructure/Automations/HomeAutomationRunAuthority.cs` — implements `IAutomationRunAuthority, IAutomationRunAuditDrain`; `_authorizeGate` semaphore race fix; pending-audit retry/drain.
- `shared/src/Haven.Application/Automations/AutomationRunContinuationContracts.cs` — added `IAutomationRunAuditDrain` interface.
- `shared/src/Haven.Infrastructure/DI/WindowsHomeDomainServiceCollectionExtensions.cs` — `AddHavenAutomationRunAuthority` registers the same singleton under both `IAutomationRunAuthority` and `IAutomationRunAuditDrain`.
- `shared/src/Haven.Infrastructure/Automations/HomeAutomationRunResourceResolver.cs` — tightened historical-pin grant (exact `PinnedGraph` pin == expectedRevision only; dropped loose DefinitionRevision fallback).
- `shared/src/Haven.Application/Automations/AutomationNodeGraphDurableAdapter.cs` — claim-loser Home settlement (`ResumeAsync` ~317) + budget/audit settlement.
- `shared/src/Haven.Desktop/Interface/Shell/MainView.axaml.cs` / `MainView.Automations.cs` — `_automationShipping` field + optional ctor param (after `agentRuntime`) + using + assignment; tick uses scheduler when present else `_automationRunner`; `StopAutomationScheduler` drains via shell work.
- `shared/src/Haven.Desktop/ViewModels/UtilityPagesViewModels.cs` — `AutomationsPageViewModel` optional ctor param + `RunNowAsync`/`RunDueAsync` route to scheduler when present.
- `shared/src/Haven.Desktop/Controls/PlanScheduledTaskControl.cs` — resolves `AutomationShippingScheduler` from `App.Services`; `RunNowAsync` uses scheduler when present.
- `Browse/Host.Tests/Automation12RealHomeOwnerTests.cs` — fixups (ValueTask→`.AsTask().WaitAsync(Bound, token)`, `DecideAsync(cancellationToken: token)`, `HAVEN_DATA_DIR` capture/restore) + **new Test 5b** invoking the real registered gateway with real Home approval (fresh→Wait→resume B after newer Active V5, exact budget) + helpers `BuildEnvelopeFromStored`/`DeriveCondition`/`DeriveOwnerState`.
- `Browse/Host.Tests/AutomationGraphDurableContinuationTests.cs` + `AutomationPass8UncertainSettlementAndPinTests.cs` — fixed `AuthorizedStepLimit` ctor calls.
- `Browse/Host.Tests/Automation11ShippingOwnerTests.cs` — reference for real SQLite/registry + envelope-building patterns.

## Test registration
- `HavenOS.Browse.Host.Tests.csproj` already includes `Automation12RealHomeOwnerTests.cs` (line 24), `Automation11ShippingOwnerTests.cs` (line 23), `AutomationGraphDurableContinuationTests.cs` (line 21), `AutomationPass8UncertainSettlementAndPinTests.cs` (line 19). New Fact 5b lives in the already-registered file. All use `TestContext.Current.CancellationToken`.

## Static-audit notes
- `GraphActivationResult` has `Succeeded`/`Code`/`EffectiveOutcome`/`StepsConsumed`; `ScheduledTaskBatchResult` has `Due/Started/Succeeded/Failed/Skipped/Uncertainties`.
- `IAutomationRunGateway` (nested in `AutomationShippingComposition`) exposes `ActivateAsync`/`ResumeAsync` returning `ValueTask<GraphActivationResult>`.
- `HomePermissionTrustService.DecideAsync` returns `HomePermissionOperationResult` with `.Succeeded`; `HomeApprovalChoice.Accept` confirmed.
- `HomeAutomationRunResourceResolver.RunEffectAction` constant exists; resolver requires `OwnerBinding` profile/actor match + current-Active (or exact persisted `PinnedGraph` pin for resume).
- Scheduler `BuildFreshActivationRequestAsync` fails closed (returns null) when no current Home actor; non-graph instructions fall back to `ScheduledTaskRunner` (no recursion).

## Blockers / unresolved
- **No .NET build/test slot** — compile audited statically only; not executed. All new Facts `NOT RUN`.
- Fresh verification reuses a maintained definition that already carries `OwnerBinding`; auto-minted definitions lacking `OwnerBinding` remain an authority edge (known, fail-closed).
- Registry `NodeGraphRuntimeRegistry.FinalizeResult` invariant (`binding.AuthorizedStepLimit == continuation.StepLimit`) constrains envelope step-limit; Test 5b uses the proven `BuildEnvelopeFromStored` pattern to satisfy digest/binding validation.

## Next move
- Owner compile (guarded `dotnet ... -m:1`) → run `Browse/Host.Tests` filter; fix any runtime discrepancies in Test 5b (expected `StepsConsumed`/yield semantics) without weakening assertions.
