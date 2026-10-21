# Lane 6 — Scheduled scheduler bounded completion correction (pass 5)

- Worker: Mercury (lane 6), six-worker authorized run, 2026-10-20.
- Worktree/branch: `codex/mercury-automations-20261020` @ common HEAD
  `fb70840d561286ecd46bd62b3c0736bf7d24108b` plus frozen pass-4 checkpoint
  `ac2c7736b57ae0ca7a33451a4a0c266512ba0fc9`. Working-tree only; no commit/push/branch changes.
- Canonical authority read: `docs/specification/9to1-development-specification-v2.md` Automations sections
  (8108, 8120–8124, 8199–8253, 8330, 8365); spec SHA-256
  `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`.
- No .NET / MSBuild / restore / test run / apps / network / models / credentials / provider/config/guard
  changes. Root owns SDK/native execution and the unchanged `$5` cap. All new checks: **NOT RUN**.

## Preserved frozen base (unchanged, verified)
- `AutomationRepository.cs` SHA-256 `15f9a0714d02e39513299781c22de06eb301744e4d1e4bcb2d6d44a38bfa3d3b`.
- `AutomationRunContinuationPersistenceTests.cs` SHA-256
  `53223babe6bf3e40863c1641dc31fb0865b7f1cf04963f222f0b0ae44363664c`.
- PASS18 harness receipt and prior lane-6 handoffs preserved. No reviewer artifacts overwritten.

## Scope (exclusive)
- `9to1 Workspace/shared/src/Haven.Application/Automations/ScheduledTaskRuntime.cs`
- `9to1 Workspace/Browse/Host.Tests/ScheduledTaskCompletionTests.cs`
- `9to1 Workspace/shared/tests/Haven.Desktop.Tests/ScheduledTaskRuntimeTests.cs`
- `HavenOS.Browse.Host.Tests.csproj` unchanged (still includes `ScheduledTaskCompletionTests.cs`).

## Required corrections applied
1. **Syntax error fixed** at `ScheduledTaskCompletionTests.cs` (line 52): expected result is now the
   literal `"ok result"` (was corrupted `tokenok result"`). No assertion suppression / disabled analyzers.
2. **Batch compatibility restored.** `ScheduledTaskBatchResult` returns to the original five positional
   fields (`Due, Started, Succeeded, Failed, Skipped`) with the five-parameter constructor and five-out
   Deconstruct intact. The uncertainty data is a minimal non-positional immutable collection:
   `IReadOnlyList<ScheduledTaskCompletionUncertaintyException> Uncertainties` (init-only) and a computed
   `Uncertain => Uncertainties.Count`. Callers (`UtilityPagesViewModels.cs`, `MainView.Automations.cs`)
   use named members only, unchanged.
3. **Per-Run uncertainty retention.** `RunDueAsync` now collects each
   `ScheduledTaskCompletionUncertaintyException` (same draft/RunID/execution outcome/persistence cause)
   into the batch `Uncertainties`, instead of a bare counter. No hidden relaunch; truthful counters.
4. **Operation-only reconciliation.** `ReconcileCompletionAsync` is a single explicit bounded call of the
   existing exact-payload `CompleteRunAsync`. The last-10,000 history scan (`ReadPersistedRunAsync`),
   custom persisted-Run comparison (`SamePersistedRun`) and `UtcAt` are removed. On a successful void
   call it reports only `Acknowledged` — never that this call wrote or performed no write. Enum renamed
   to `Acknowledged`/`Unknown`; tests/docs updated to that honest precision.
5. **Post-commit ACK-loss handling.** A generic `InvalidOperationException` thrown after a real durable
   commit is NOT treated as refusal/rollback. Under the current void contract an unacknowledged call
   stays `Unknown` with the SAME original exception retained; no message inspection, no exception-class
   classification, no fresh execution/lease, no automatic retry. Successful exact historical
   acknowledgement continues to work after a newer legitimate definition lease/schedule.
6. **Separate execution capture / schedule planning / completion persistence.**
   `ExecuteToTerminalDraftAsync` now (a) captures the terminal Run + execution cause first (outside any
   schedule call), (b) computes `nextRunAt` exactly once in a separate phase, then (c) constructs the
   exact draft. A planning fault raises `ScheduledTaskSchedulePlanningFailureException` retaining the
   actual outcome and original planning cause — never relabelling executed work, recalculating
   timestamps/result, retrying the planner, calling persistence with a fabricated null schedule, or
   pretending an unknown-schedule draft is ready for reconciliation.
7. **Identity boundary preserved.** One RunID before execution, one terminal outcome, exact historical
   token/payload/timestamps, persistence outside execution retries, exact held completion awaited until
   settlement; once a complete draft exists every explicit ACK attempt uses it unchanged.

## Tests authored (ALL NOT RUN)
### `ScheduledTaskCompletionTests.cs` (real migrated SQLite `AutomationRepository` + actual `ScheduledTaskRunner`)
1. `Lost_ack_after_durable_commit_reconiles_to_newer_lease_and_schedule_without_writes` — lost-ACK after
   durable commit; reopen; acquire a NEW lease via real `TryAcquireLeaseAsync`; update to a DISTINCT
   schedule via real `UpsertAsync`; snapshot full Run/definition rows; install write-blocking triggers;
   reconcile the exact old draft → `Acknowledged`, zero SQL writes, unchanged newer token/schedule and
   original payload, no new Run/lease/execution.
2. `Forced_completion_write_failure_rolls_back_then_reconcile_writes_once_without_replay` — real rollback
   (no run row, lease retained, schedule unchanged); drop trigger; reconcile → `Acknowledged`; the real
   single row proves the write; no replay.
3. `Forced_completion_write_failure_cannot_overwrite_a_newer_real_lease` — newer owner uses REAL
   `TryAcquireLeaseAsync` under natural lease expiry (fixed past `Now`), not SQL token forging; updates a
   distinct schedule; reconciliation → `Unknown` with the exact preserved `InvalidOperationException`
   guard; actual database proven unchanged (definition unchanged, no run rows); never re-executed.
4. `Execution_failure_and_completion_error_retain_both_causes_with_one_stable_id` — one stable ID; Failed
   draft; both execution and persistence causes distinct and retained; no second terminal record.
5. `Cancellation_during_held_persistence_never_relabels_success_and_settles_on_release` — waits for ENTRY
   to the delegating completion call (entered-persistence gate), cancels while held; whole region in
   try/finally; every gate released and the exact original joined in finally; outcome is uncertainty
   (never settled), success payload survives, reconcile → `Acknowledged`, no replay.
6. `RunDue_retains_inspectable_uncertainties_and_never_relaunches` — inspect the exact retained uncertainty
   (draft/RunID/execution outcome/persistence cause) for the affected Run; truthful counters; explicit
   legacy five-parameter constructor and five-out Deconstruct checks; no hidden relaunch on a second sweep.
7. `Post_commit_invalidoperationexception_ack_loss_stays_uncertain_then_exact_replay_acks` — post-commit
   `InvalidOperationException` → uncertain, original cause retained, exactly one real row; a later exact
   replay acknowledges without re-execution.
8. `Reconciliation_never_enumerates_history` — a synthetic wrapper counts/refuses `GetRunsAsync`;
   reconciliation delegates only to the real SQLite store and never enumerates history.
9. `Schedule_planning_fault_after_model_success_retains_executed_run_without_fake_write` — model execution
   count one; original Succeeded result/RunID retained; planning evaluated once; typed preparation
   failure; no completion write with a fake null schedule; no fabricated Failed row; lease still held.
10. `Schedule_planning_fault_preserves_true_execution_failure_cause` — planning fault after a genuine
    execution failure retains both the execution failure and planning causes by identity.

### `ScheduledTaskRuntimeTests.cs` (memory repo, contract-level)
- `CompletionPersistenceFaultUsesOneStableRunIdentityAndNeverCreatesAFailedRow` — updated to operation-only
  reconciliation (`Acknowledged`); `AckLossAutomationRepository` fails exactly once so an identical
  replay is acknowledged idempotently; `MemoryAutomationRepository.CompleteRunAsync` is idempotent (no
  duplicate row on replay).
- `BatchResultKeepsLegacyFiveParameterConstructorAndFiveOutDeconstruct` — explicit five-parameter ctor and
  five-out Deconstruct checks.
- Original success / failed-path / condition tests retained unchanged.

## Source identity (working tree, uncommitted)
- `ScheduledTaskRuntime.cs`: SHA-256 `14811919704cccaef6fb00ff9d8af22c994ed0b4c1ba610353172e84d6f9a34b`
- `ScheduledTaskCompletionTests.cs`: SHA-256 `d62e8bfac9d5b1f981d3c7cae84b19c86491b3d468d93fd1af54b7dc53472f6d` (new)
- `ScheduledTaskRuntimeTests.cs`: SHA-256 `308acaa1dfc7375c763acf902fe3471904e56ac9699c5618d1e998c44cf2a22b`
- `HavenOS.Browse.Host.Tests.csproj`: SHA-256 `4303572fd55cd815e5c6d70851f10ce4886f042b589640747e0320dc0a536536` (unchanged)
- Frozen `AutomationRepository.cs` / `AutomationRunContinuationPersistenceTests.cs` hashes unchanged.

## RED/GREEN: NOT RUN (no .NET slot yet)
Target command (after root allocates the serialized slot):
```
/workspace/.tools/dotnet/dotnet test "9to1 Workspace/Browse/Host.Tests/HavenOS.Browse.Host.Tests.csproj" \
  -c Release -m:1 --filter "FullyQualifiedName~ScheduledTaskCompletionTests|FullyQualifiedName~ScheduledTaskRuntimeTests"
```
with `DOTNET_CLI_HOME=/workspace/.tools/dotnet-cli` and `NUGET_PACKAGES=/workspace/.tools/nuget/packages`.
Red/green counts, assertion output and source hashes must be recorded here by root after allocation. This
pass is NOT accepted until those run and the preserved repository gate passes.

## Static checks completed (no .NET)
- Production `ScheduledTaskRuntime.cs` braces balanced; no stale `ReadPersistedRunAsync` /
  `SamePersistedRun` / `UtcAt` / old enum references.
- Frozen hashes verified equal to identity.json.
- Test files braces balanced (excluding comments/string literals).
- Legacy batch ctor/Deconstruct checked in source; callers verified to use named members only.

## Remaining durable gaps (separate mandatory open work, NOT implemented or claimed here)
- Durable Wait/Approval, actual node continuation/resume ownership, consequential side-effect replay
  policy, crash recovery, and a Home-owned background scheduler remain separate mandatory open work.
- The next source step is the real graph yield/continuation boundary; this pass is only the bounded
  terminal-completion repair.
