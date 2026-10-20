# Lane 6 — AutomationRepository state-transition repair (handoff)

- Worker: Mercury (lane 6), six-worker authorized run, 2026-10-20.
- Worktree/branch: `codex/mercury-automations-20261020` @ base `fb70840d561286ecd46bd62b3c0736bf7d24108b`
  (canonical spec present; SHA-256 `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`).
- Canonical authority read: Automations §8088–8375 (esp. 8108, 8199–8203, 8227–8239, 8245–8251, 8330, 8365).
- Owner/US: no model subagents, no .NET/build until root allocates the serialized slot, no commit/push/deploy,
  no credentials/provider/cap/config changes, preserve all other lanes and existing work.

## Scope (owned only)
- `9to1 Workspace/shared/src/Haven.Infrastructure/Persistence/SQLite/AutomationRepository.cs`
- `9to1 Workspace/Browse/Host.Tests/AutomationRunContinuationPersistenceTests.cs`
- No public signature or persisted-schema changes. `Abstractions.cs` / `AutomationEntities.cs` untouched (not needed).
- No invented AUTO-* IDs; ApprovalRequired treated as candidate state, never approval.

## Fixed defect
SuspendRunAsync upserted the same RunID unconditionally; CompleteRunAsync plain-INSERT collided with a suspended
primary key and never validated the definition lease or revision fence. Now both transitions run in one SQLite
write transaction with: immutable RunID/AutomationID/historical pin guards, an exact revision fence
(desired = stored+1; reject stale/jump/changed-same-version), no-write acknowledgement for a byte-identical
persisted payload (recognized before any lease requirement; supplied nextRunAt ignored and never reapplied),
terminal finality (Succeeded/Failed/Cancelled only), a definition-lease CAS on scheduled writes (token
equality is the fence; 0 affected rows rolls back the Run mutation), and Suspend touching ONLY the Run row
while still validating the held definition lease (lost/stolen tokens rejected). History-only appends (null
lease) never touch definition schedule/lease/updated_at.

## Tests added (11 [Fact], all using the real migrated SQLite + reopen)
1. A_waiting_run_keeps_its_pinned_revision_and_continuation_across_a_store_reopen (updated: acquires real definition lease)
2. Suspended_run_completes_same_id_to_single_terminal_row_preserving_original_pin
3. Identical_terminal_retry_is_a_no_write_ack_and_never_reschedules_or_touches_a_newer_lease
4. Stale_jump_or_changed_same_version_transitions_reject_without_mutation
5. Wrong_identity_lease_or_stolen_lease_rejects_run_and_definition_unchanged
6. Terminal_run_cannot_be_resumed_or_re_suspended
7. Forced_definition_update_failure_rolls_back_the_run_transition
8. Two_stores_racing_different_terminal_outcomes_have_one_winner_and_no_overwrite_or_duplicate
9. Legacy_leased_new_terminal_and_null_lease_history_append_both_write_only_intended_rows
10. Suspend_leaves_schedule_and_lease_intact_and_reading_due_approval_does_not_approve
11. The_store_exposes_the_continuation_capability (renamed from the misleading `A_store_without_durable_wait_support...`; honest capability check)

## RED/GREEN: NOT RUN (no .NET slot yet)
Target command (after allocation):
```
/workspace/.tools/dotnet/dotnet test "9to1 Workspace/Browse/Host.Tests/HavenOS.Browse.Host.Tests.csproj" \
  -c Release -m:1 --filter FullyQualifiedName~AutomationRunContinuationPersistenceTests
```
with `DOTNET_CLI_HOME=/workspace/.tools/dotnet-cli` and `NUGET_PACKAGES=/workspace/.tools/nuget/packages`.
Red/green counts, assertion output and source hashes must be recorded here by the parent/requester after allocation.

## Static checks completed (no .NET)
- Only the two scoped files changed (`git status`), balanced braces, single method definitions.
- Confirmed no other production caller of `CompleteRunAsync`/`SuspendRunAsync` breaks:
  `ScheduledTaskRuntime` new-terminal defaultRevision0 with acquired non-null token (create → releases lease), and
  `CanonicalProjectContextStoreReadOwnerTests.AutomationWrites.cs:25` NEW Succeeded rev0/null token (history-only append, no definition mutation).

## Remaining integration gaps (recorded, NOT fixed here)
- `ScheduledTaskRuntime.cs` 238–253 catches a persistence fault and then writes a DIFFERENT Failed RunID, so the
  repository ack does not prove scheduler lost-ack safety.
- Graph delay still uses process `Task.Delay`; RunID allocated late; no Suspend/GetResumable callers and no atomic
  resume claim; approval contract and durable checkpoint replay remain separate required tasks.
- No UI/whole-app/native/web acceptance claim.

## Unresolved decisions
None queued. No owner decision required for this bounded task.
