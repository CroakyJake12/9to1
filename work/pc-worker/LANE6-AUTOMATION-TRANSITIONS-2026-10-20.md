# Lane 6 — AutomationRepository state-transition repair (handoff, pass 3)

- Worker: Mercury (lane 6), six-worker authorized run, 2026-10-20.
- Worktree/branch: `codex/mercury-automations-20261020` @ base `fb70840d561286ecd46bd62b3c0736bf7d24108b`
  (canonical spec present; SHA-256 `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`).
- Pass-2 source was frozen/reviewed at checkpoint `519d0e6ac5ca4f3df75ebff54c4eb2132d2c99fc`
  (the same files, hashes below). This pass-3 edits the working tree only (not committed).
- Canonical authority read: Automations §8088–8375 (esp. 8108, 8199–8203, 8227–8239, 8245–8251, 8330, 8365).
- Owner/US: no model subagents, no .NET/build until root allocates the serialized slot, no commit/push/deploy,
  no credentials/provider/cap/config changes, preserve all other lanes and existing work. `$5` cap unchanged.

## Scope (owned only)
- `9to1 Workspace/shared/src/Haven.Infrastructure/Persistence/SQLite/AutomationRepository.cs`
- `9to1 Workspace/Browse/Host.Tests/AutomationRunContinuationPersistenceTests.cs`
- No public signature or persisted-schema changes. No invented AUTO-* IDs; RetryPending is a supported
  suspension state (interface contract), never an approval/execution grant.

## Corrections applied in this pass (fixes the remaining exact lane-6 issues)

1. **RetryPending is a supported suspension.** `IsSupportedSuspendedStatus` (repository) now admits
   `Waiting`/`ApprovalRequired`/`RetryPending`, matching `IAutomationRunContinuationStore` (Abstractions
   202–204: "waits, awaits approval or awaits a bounded retry") and `GetResumableRunsAsync` (which already
   read RetryPending). `SuspendRunAsync` doc + rejection message updated to name the states. No retry
   policy or scheduler behavior was added. New test: real migrated SQLite suspend→reopen→due-read for a
   RetryPending run, definition lease/schedule untouched, run stays RetryPending (not approved/executed).

2. **Race test accepts the legitimate serialized loser.** The two-store race test no longer demands the
   CAS path `Concurrent run transition conflict.` exclusively. The serialized loser re-reads the winning
   terminal row and throws `A completed Run cannot be mutated.`; the optimistic-CAS loser throws the
   conflict. The test accepts only those two specific InvalidOperationException messages (anything else,
   including SQLITE_BUSY or a setup fault, fails) and still verifies exactly one winner, the winner's
   exact payload, and the winner's schedule. No unreachable CAS-zero path is forced.

3. **Masking fixtures removed.**
   - Terminal re-suspend now re-acquires the SAME original lease token A (not A→B) and asserts the exact
     `A completed Run cannot be resumed or re-suspended.` message, so finality is the causal guard.
   - Complete invalid-status (Waiting, unknown 999) candidates now carry an otherwise-valid non-null
     `CompletedAt`; both rejection helpers assert the exact status allow-list message so the status guard
     is proven causal, not the completion-timestamp guard.
   - Terminal-suspend and unknown-suspend cases assert the exact suspension status message.

4. **Ack test reconstructs the persisted payload and covers both timings.** The identical-terminal-retry
   test now reopens the store and reconstructs the completion from the persisted run row via
   `GetRunsAsync` (never the original in-memory object). It then exercises two ack timings, each under
   write-blocking triggers on both tables (any INSERT/UPDATE/DELETE fails the test):
   - immediately after the definition lease release (lease null), and
   - after a newer lease/schedule arrived behind the ack's back (lease-2 + advanced schedule preserved).
   Both assert zero writes.

5. **Both-null new history append.** New test appends a brand-new Succeeded Run with a null Run lease to
   a definition that is itself not leased (definition token null), proves no definition schedule/lease/
   timestamp mutation, reopens, reconstructs the persisted history row, and re-acks identically under
   write-blocking triggers with the definition still untouched.

## Tests (18 `[Fact]`, all on the real migrated SQLite; NOT RUN until slot)
Existing 16 pass-2 tests retained (with the fixes above). Two added:
- `RetryPending_suspend_is_persisted_and_reads_due_without_approving_or_executing`
- `Both_null_new_history_append_and_identical_reopened_ack_never_touch_the_definition`

Concrete changed test methods in this pass:
- `Identical_terminal_retry_is_a_no_write_ack_and_never_reschedules_or_touches_a_newer_lease` (reconstruct,
  two-phase ack, triggers)
- `Terminal_run_cannot_be_resumed_or_re_suspended` (same lease A + exact message)
- `Two_stores_racing_different_terminal_outcomes_have_one_winner_and_no_overwrite_or_duplicate`
  (legitimate conflict set)
- `Complete_rejects_waiting_and_unknown_statuses_without_mutation`, `Suspend_rejects_terminal_and_unknown_statuses_without_mutation`
  (exact causal messages)
- helpers `AssertCompleteRejectedWithoutMutationAsync` / `AssertSuspendRejectedWithoutMutationAsync`
  (exact status messages), new `InstallWriteBlockingTriggersAsync` / `DropWriteBlockingTriggersAsync`

## Source identity (working tree, uncommitted)
- `AutomationRepository.cs`: SHA-256 `15f9a0714d02e39513299781c22de06eb301744e4d1e4bcb2d6d44a38bfa3d3b`
- `AutomationRunContinuationPersistenceTests.cs`: SHA-256 `08552a99e40bf5cd1bc05e02344bf2a7b37fc5ec80a4da133edc6d48b06a7d29`
- `git diff` (both files) -> `/tmp/opencode/lane6-pass3.changes.patch`

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
- Only the two scoped files changed (`git status`); balanced braces; single method definitions; the old
  suspension rejection string has no remaining references.
- No production caller regresses: the Suspend allow-list is widened only; `ScheduledTaskRuntime` new-terminal
  writes always construct Succeeded/Failed/Cancelled with `CompletedAt` set; `CanonicalProjectContextStoreReadOwnerTests.AutomationWrites.cs`
  NEW Succeeded rev0/null token sets `CompletedAt`, so the completion-timestamp guard remains compatible.

## Remaining integration gaps (separate mandatory debt, NOT fixed here)
- Original scheduler late `RunID` allocation / different `FailedRunID` on a persistence fault (repository
  acknowledgment does not prove scheduler lost-ack safety; `ScheduledTaskRuntime.cs` 238–253).
- Graph delay still uses process `Task.Delay` (not durable).
- No `Suspend`/`GetResumableRunsAsync` callers and no atomic resume claim; approval contract and durable
  checkpoint replay remain separate required tasks.
- No UI/whole-app/native/web acceptance claim.

## Unresolved decisions
None queued. No owner decision required for this bounded task.
