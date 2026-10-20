# Lane 6 — AutomationRepository state-transition repair (handoff, correction pass)

- Worker: Mercury (lane 6), six-worker authorized run, 2026-10-20.
- Worktree/branch: `codex/mercury-automations-20261020` @ base `fb70840d561286ecd46bd62b3c0736bf7d24108b`
  (canonical spec present; SHA-256 `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`).
- First-pass source frozen at checkpoint `ff89d50473ed54ad7d0536d855471a695960d231`
  (`/workspace/shared/9to1-test-evidence/parallel6-automations-pass1-frozen/`). This pass2 corrects the
  frozen source in the working tree (not committed).
- Canonical authority read: Automations §8088–8375 (esp. 8108, 8199–8203, 8227–8239, 8245–8251, 8330, 8365).
- Owner/US: no model subagents, no .NET/build until root allocates the serialized slot, no commit/push/deploy,
  no credentials/provider/cap/config changes, preserve all other lanes and existing work. `$5` cap unchanged.

## Scope (owned only)
- `9to1 Workspace/shared/src/Haven.Infrastructure/Persistence/SQLite/AutomationRepository.cs`
- `9to1 Workspace/Browse/Host.Tests/AutomationRunContinuationPersistenceTests.cs`
- No public signature or persisted-schema changes. `Abstractions.cs` / `AutomationEntities.cs` untouched (not needed).
- No invented AUTO-* IDs; ApprovalRequired treated as candidate state, never approval.

## Corrections applied in this pass (vs. first-pass overreach)

Reviewer feedback `parallel6-lane6-review-feedback.json` (AUTO-QA-001/002) and the pass2 prompt were
reviewed again. The first-pass handoff overstated guarantees; the following were actually corrected:

1. **Exact incoming status allowlists and invariants.**
   - `CompleteRunAsync` now rejects any desired status that is NOT one of the known terminal statuses
     (Succeeded/Failed/Cancelled) and requires a non-null completion timestamp. It also rejects a negative
     revision, for both brand-new and existing rows.
   - `SuspendRunAsync` now accepts ONLY `Waiting`/`ApprovalRequired` as the desired status, rejects a
     negative revision, and rejects a suspended run that carries a completion timestamp. Unknown/other
     enum values and terminal statuses are rejected. (Previously the desired status was unchecked, so
     Complete could write a Waiting/Running/unknown row and Suspend could write a terminal/unknown row.)

2. **Immutable ORIGINAL Run lease token across existing transitions.**
   - `CompleteRunAsync` now requires `desired.LeaseToken == existing.LeaseToken` for an existing row.
     A stored waiting run with token A can no longer be completed as desired run 8/token B even when the
     definition currently holds B (the old code passed the definition CAS and stole the original run).
     A desired null token likewise cannot bypass the definition CAS for an existing leased waiting run;
     it is rejected with both rows unchanged.
   - `SuspendRunAsync` applies the same original-token immutability after the held-definition-lease
     check. `Stored A / desired B / current definition B` now rejects for both complete and suspend.
   - The identical persisted terminal acknowledgment is still recognized BEFORE any definition
     lease/schedule requirement, so a lost-ack retry returns with no writes even after newer
     leases/schedules.
   - Null-token history compatibility is ONLY for a new terminal row (the null-lease history-only
     append and the legacy new-terminal `Revision 0` acquired-token case are both retained exactly).
   - The definition lease/schedule CAS still guards scheduled writes (non-null lease); 0 affected rows
     rolls back the Run mutation. No unapproved execution/policy/expiry semantics were added.

## Tests added/corrected (16 `[Fact]`, all on the real migrated SQLite; NOT RUN until slot)
1. `A_waiting_run_keeps_its_pinned_revision_and_continuation_across_a_store_reopen` (reopens)
2. `Suspended_run_completes_same_id_to_single_terminal_row_preserving_original_pin` (reopens)
3. `Identical_terminal_retry_is_a_no_write_ack_and_never_reschedules_or_touches_a_newer_lease` —
   corrected: reopens and reconstructs the same serialized payload, changes the current lease and
   schedule, then installs write-blocking SQLite triggers on both `automation_runs` and `automations`
   (INSERT/UPDATE/DELETE) to prove the retry issues NO write (any write aborts).
4. `Stale_jump_or_changed_same_version_transitions_reject_without_mutation` — corrected to target the
   revision fence with terminal candidates.
5. `Wrong_identity_lease_or_stolen_lease_rejects_run_and_definition_unchanged` — corrected per-target
   candidates (each exercises its intended guard).
6. `Terminal_run_cannot_be_resumed_or_re_suspended`
7. `Forced_definition_update_failure_rolls_back_the_run_transition` — corrected: asserts the exact
   `SqliteException` trigger failure and that ALL persisted Run and definition columns are byte-for-byte
   unchanged after rollback.
8. `Two_stores_racing_different_terminal_outcomes_have_one_winner_and_no_overwrite_or_duplicate` —
   corrected: two connections gate-coordinated before the write; exactly one winner; the losing outcome
   is asserted to be the specific `Concurrent run transition conflict.` `InvalidOperationException` (a
   generic catch no longer hides SQLITE_BUSY or a setup fault); the winner's exact payload and schedule
   are verified.
9. `Legacy_leased_new_terminal_and_null_lease_history_append_both_write_only_intended_rows` (both null
   history-append coverage and legacy rev-0 acquired-token coverage)
10. `Suspend_leaves_schedule_and_lease_intact_and_reading_due_approval_does_not_approve` (reopens)
11. `The_store_exposes_the_continuation_capability` (honest capability surface check; not a
    fake-is-not-interface tautology)
12. `Stored_A_desired_B_complete_rejects_when_definition_now_holds_B_both_rows_unchanged`
13. `Stored_A_desired_B_suspend_rejects_when_definition_now_holds_B_both_rows_unchanged`
14. `Stored_A_desired_null_complete_rejects_both_rows_unchanged`
15. `Complete_rejects_waiting_and_unknown_statuses_without_mutation`
16. `Suspend_rejects_terminal_and_unknown_statuses_without_mutation`

Not every test reopens; only the tests that specifically exercise reopen/resume do (1, 2, 3, 10). No
timing sleeps / weak assertions / tests that mirror constants were added.

## Source identity (working tree, uncommitted)
- `AutomationRepository.cs`: SHA-256 `5c89adf6a341298854974156466431cfe1f5571e53d51376606324f53503ba86`
- `AutomationRunContinuationPersistenceTests.cs`: SHA-256 `b48ead2ef4269e3acb8e1aeb26b0eaba6d97a188f748d38e79a5f57e7c47e0fc`
- `git diff` (both files) -> `/tmp/opencode/lane6-pass2.changes.patch`, SHA-256
  `cf4fea7fc69e7f00bbe9379fcbcbfa526bc23508452a7a9d84ac3c870c2ae06e` (981 lines).

## RED/GREEN: NOT RUN (no .NET slot yet)
Target command (after allocation):
```
/workspace/.tools/dotnet/dotnet test "9to1 Workspace/Browse/Host.Tests/HavenOS.Browse.Host.Tests.csproj" \
  -c Release -m:1 --filter FullyQualifiedName~AutomationRunContinuationPersistenceTests
```
with `DOTNET_CLI_HOME=/workspace/.tools/dotnet-cli` and `NUGET_PACKAGES=/workspace/.tools/nuget/packages`.
Red/green counts, assertion output and source hashes must be recorded here by the parent/requester after
allocation. This correction is NOT accepted until those run.

## Static checks completed (no .NET)
- Only the two scoped files changed (`git status`); balanced braces; single method definitions.
- No other production caller of `CompleteRunAsync`/`SuspendRunAsync` regresses: `ScheduledTaskRuntime`
  new-terminal defaultRevision0 with acquired non-null token always constructs Succeeded/Failed/Cancelled
  with `StartedAt`/`CompletedAt` set; `CanonicalProjectContextStoreReadOwnerTests.AutomationWrites.cs:25`
  NEW Succeeded rev0/null token sets `CompletedAt`, so the new completion-timestamp guard is compatible.

## Remaining integration gaps (separate mandatory debt, NOT fixed here)
- Original scheduler late `RunID` allocation / different `FailedRunID` on a persistence fault: the
  repository acknowledgment does not prove scheduler lost-ack safety (`ScheduledTaskRuntime.cs` 238–253).
- Graph delay still uses process `Task.Delay` (not durable).
- No `Suspend`/`GetResumableRunsAsync` callers and no atomic resume claim; approval contract and durable
  checkpoint replay remain separate required tasks.
- No UI/whole-app/native/web acceptance claim.

## Unresolved decisions
None queued. No owner decision required for this bounded task.
