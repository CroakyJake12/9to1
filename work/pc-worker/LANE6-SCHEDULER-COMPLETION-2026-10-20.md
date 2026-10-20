# Lane 6 — Scheduler completion/uncertainty fix (handoff, pass 4)

- Worker: Mercury (lane 6), six-worker authorized run, 2026-10-20.
- Worktree/branch: `codex/mercury-automations-20261020` @ base `fb70840d561286ecd46bd62b3c0736bf7d24108b`
  (canonical spec present; SHA-256 `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`).
- This is the **scheduler completion/persistence fix** on top of the frozen pass-3/root-QA state
  (checkpoint `5b4006b340cda863a80db975dbe15b0ff6393b6a`). It edits the working tree only (not committed).
- Canonical authority read: Automations §8088–8375 (esp. 8120–8124, 8241–8253, 8330). All source edits are
  authoring-only; **no .NET/build/test/app commands ran** (slot not allocated).

## Preserved frozen base (unchanged this pass)
- `AutomationRunContinuationPersistenceTests.cs` SHA-256 `53223babe6bf3e40863c1641dc31fb0865b7f1cf04963f222f0b0ae44363664c`
  (matches frozen identity.json) — pass-3/root QA file, untouched.
- `AutomationRepository.cs` SHA-256 `15f9a0714d02e39513299781c22de06eb301744e4d1e4bcb2d6d44a38bfa3d3b`
  (matches frozen identity.json) — pass-3/root QA file, untouched.
- PASS18 harness evidence: `shared/9to1-test-evidence/lane6-private-execution-harness-20261020/root-execution-receipt.json`
  SHA-256 `6afb7dc26f4cfbbf97c1a22b16949472cb1717134ca129a30a342a9aa5fb3ce2` (18/18 PASS) — preserved explicitly.
- Prior lane-6 handoff `LANE6-AUTOMATION-TRANSITIONS-2026-10-20.md` (pass 3) preserved.

## Scope of this pass (scheduler completion, only 4 files changed/added)
1. `9to1 Workspace/shared/src/Haven.Application/Automations/ScheduledTaskRuntime.cs` — production fix.
2. `9to1 Workspace/Browse/Host.Tests/ScheduledTaskCompletionTests.cs` — NEW real-SQLite tests (6 `[Fact]`).
3. `9to1 Workspace/shared/tests/Haven.Desktop.Tests/ScheduledTaskRuntimeTests.cs` — extended: 2 contract tests +
   `AckLossAutomationRepository` nested class.
4. `9to1 Workspace/Browse/Host.Tests/HavenOS.Browse.Host.Tests.csproj` — added
   `<Compile Include="ScheduledTaskCompletionTests.cs" />` (line 19).

## Production correction (`ScheduledTaskRuntime.cs`)
The bounded scheduler-correction requirements, and exactly how each is met:

- **One stable Run identity before execution.** `ExecuteToTerminalDraftAsync` allocates
  `runId = Guid.NewGuid()` once before any model/graph/action work and reuses that single identity across
  success, genuine failure, cancellation, and every completion-ack retry. No terminal ID is generated inside
  a `catch`.
- **Execution outcome separated from completion persistence.** `CompleteLeasedRunAsync` first executes to one
  exact `ScheduledTaskCompletionDraft(Run, NextRunAt, ExecutionFailure)` (payload+next schedule built exactly
  once), then persists it via the existing `repository.CompleteRunAsync`.
- **Typed persistence-uncertainty retains both original failures.** A persistence fault becomes
  `ScheduledTaskCompletionUncertaintyException(draft, persistenceCause)` — `Draft` keeps the exact payload and
  the original execution failure/cancellation; `PersistenceCause` keeps the store fault. Never re-enters
  execution, never fabricates a second terminal identity, never relabels a success as Failed/Cancelled.
- **Preserved original observable control flow.** After a durable ACK: genuine Cancelled rethrows the original
  `OperationCanceledException`; genuine Failed/Succeeded returns that run.
- **Bounded same-payload reconciliation.** `ReconcileCompletionAsync(draft)` reads back via `GetRunsAsync`
  (same store). Identical persisted row → `ConfirmedNoWrite` (zero writes, never touches a possibly newer
  lease/schedule); absent row → `CompleteRunAsync` (`ConfirmedWritten`); `InvalidOperationException` →
  `Refused` (a newer owner is never overwritten); anything else → `Unknown`. It never re-executes, never
  acquires a fresh lease or RunID.
- **`RunDueAsync` counts uncertain separately.** Per-task catch increments `Uncertain`; never relaunches, never
  counts as failed. `ScheduledTaskBatchResult` gained an additive `Uncertain` member; both callers
  (`UtilityPagesViewModels.cs` `Run pass: {result.Succeeded}...`, `MainView.Automations.cs` `result.Started`)
  use named members only, so they are unaffected.

## Tests authored (ALL NOT RUN — no .NET slot)
### `ScheduledTaskCompletionTests.cs` (NEW, real migrated SQLite `AutomationRepository` + actual `ScheduledTaskRunner`)
1. `Lost_ack_after_durable_commit_keeps_one_succeeded_run_and_reconciles_without_writes_or_replay`
   — lost-ACK after durable commit: one Succeeded row, definition lease released/schedule advanced, then
   reconcile under write-blocking triggers → `ConfirmedNoWrite`, zero SQL mutations, no re-execution.
2. `Forced_completion_write_failure_rolls_back_then_reconcile_writes_once_without_replay`
   — forced completion write failure rolls back fully (no run row, lease retained, schedule unchanged); after
   dropping the trigger, reconcile → `ConfirmedWritten` once, no replay.
3. `Forced_completion_write_failure_refuses_reconciliation_when_a_newer_lease_won`
   — a newer lease behind the failed completion’s back → `Refused`, both rows byte-for-byte unchanged, never
   overwritten, never re-executed.
4. `Execution_failure_and_completion_error_retain_both_causes_with_one_stable_id`
   — execution failure (3 bounded retries) + completion error: `Draft` Failed status, `ExecutionFailure` and
   `PersistenceCause` both retained and distinct, no second terminal record.
5. `Cancellation_during_held_persistence_never_relabels_success_and_settles_on_release`
   — held persistence gate + cancellation: run stays `Pending` until release, throws uncertainty, the exact
   success payload survives (never relabelled Cancelled), reconcile = `ConfirmedNoWrite`, no replay.
6. `RunDue_counts_uncertain_separately_and_never_relaunches_or_counts_it_as_failed`
   — two due tasks, one ack-lost: `Due=2, Started=2, Succeeded=1, Failed=0, Skipped=0, Uncertain=1`, exactly one
   execution each, second immediate due sweep does not relaunch (`Started=0`).

### `ScheduledTaskRuntimeTests.cs` (extended, memory repo for contract-level logic)
- `CompletionPersistenceFaultUsesOneStableRunIdentityAndNeverCreatesAFailedRow` — one stable ID, never a
  fabricated Failed row, reconcile = `ConfirmedNoWrite`, no replay.
- `AckLossAutomationRepository` nested class — completes the durable memory write then throws a distinct ACK-loss
  exception (exact lost-ACK simulation).
- Existing memory-repo tests retained unchanged (success returns run; failure returns Failed run).
- New real-SQLite helpers used in `ScheduledTaskCompletionTests.cs` (`NewDatabase`, `AppPaths`,
  `HAVEN_DATA_DIR`, `AutomationRepository(SqliteDatabase)`) mirror the established
  `AutomationRunContinuationPersistenceTests.cs` pattern exactly.

## Source identity (working tree, uncommitted)
- `ScheduledTaskRuntime.cs`: SHA-256 `6edbaed60066b2568523e452a62cf3cfb9292a35ccaa51f653064be78307f666`
- `ScheduledTaskCompletionTests.cs`: SHA-256 `7258e758c094022c9a7d4b6118b17c1594a255c8a5c51743a99d47e929da6aad` (new, untracked)
- `ScheduledTaskRuntimeTests.cs`: SHA-256 `7949d9e3a15c4649ec0858e0a6d8bbbdc43077fcac8a89952d52fffb1b26983a`
- `HavenOS.Browse.Host.Tests.csproj`: SHA-256 `4303572fd55cd815e5c6d70851f10ce4886f042b589640747e0320dc0a536536`
- `git status --short` shows only: M ScheduledTaskRuntime.cs, M ScheduledTaskRuntimeTests.cs, M csproj,
  M AutomationRepository.cs (frozen), M AutomationRunContinuationPersistenceTests.cs (frozen), ?? ScheduledTaskCompletionTests.cs.

## Static checks completed (no .NET)
- `ScheduledTaskRuntime.cs` braces balanced 89/89; no leftover `ScheduledTaskCompletionAck` references.
- `ScheduledTaskBatchResult` additive `Uncertain`; callers verified to use named members only.
- Frozen pass-3 hashes verified equal to identity.json; PASS18 receipt hash verified.
- Test helpers match the existing continuation test’s real-SQLite pattern; `TestContext.Current.CancellationToken`
  is the established xunit.v3 pattern in this project.

## RED/GREEN: NOT RUN (no .NET slot yet)
Target command (after root allocates the serialized slot):
```
/workspace/.tools/dotnet/dotnet test "9to1 Workspace/Browse/Host.Tests/HavenOS.Browse.Host.Tests.csproj" \
  -c Release -m:1 --filter "FullyQualifiedName~ScheduledTaskCompletionTests|FullyQualifiedName~ScheduledTaskRuntimeTests"
```
with `DOTNET_CLI_HOME=/workspace/.tools/dotnet-cli` and `NUGET_PACKAGES=/workspace/.tools/nuget/packages`.
Red/green counts, assertion output and source hashes must be recorded here by the parent/requester after
allocation. This correction is NOT accepted until those run.

## Remaining durable gaps (separate mandatory debt, NOT fixed here)
- **Durable Wait/Approval:** no UI/`Suspend`/`GetResumableRunsAsync` callers and no atomic resume claim; approval
  contract and durable checkpoint replay remain separate required tasks (the pass-3 freeze proved the store side only).
- **Scheduler loop:** `ScheduledTaskRunner` deliberately owns no timer/OS-scheduler/worker loop; due-sweep
  invocation still comes from app surfaces. No scheduler-loop lifecycle/durability claim.
- **Crash recovery:** reconciliation is explicit and callable, but nothing yet auto-invokes
  `ReconcileCompletionAsync` on startup; crash-recovery sweeping of uncertain completions is a separate task.
- No UI/whole-app/native/web acceptance claim.

## Unresolved decisions
None queued. No owner decision required for this bounded task; if a full store/`CompleteRunAsync` fault is later
judged non-recoverable (beyond the typed `InvalidOperationException` refusal), that is an acceptance decision to
queue, not inferred here.
