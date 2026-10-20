/*
 * FILE DOCUMENTATION
 * Where: Browse/Host.Tests/AutomationRunContinuationPersistenceTests.cs, the real Haven host route test lane.
 * What: Proves an Automation Run's canonical state transitions, including durable wait continuation,
 *       atomic completion semantics, revision fencing, definition-lease validation and rollback,
 *       using the real migrated SQLite schema and a store reopen (never a mock repository).
 * Why: Specification 8199-8203, 8227-8239, 8245-8251, 8330 and APP-AC-001..007 require durable
 *       Wait/resume, durably versioned Runs that cannot be silently corrupted, and idempotent
 *       completion that never crashes historical identity or an unrelated newer definition lease.
 * Maintenance: Uses the real SQLite schema and migrations. Tests that reject a transition assert both
 *       the Run row and the definition row are byte-for-byte unchanged.
 */

using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HavenOS.Browse.Host.Tests;

public sealed class AutomationRunContinuationPersistenceTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);
    private readonly CancellationToken token = TestContext.Current.CancellationToken;
    private readonly string _root = Directory.CreateTempSubdirectory("haven-automation-run-").FullName;

    [Fact]
    public async Task A_waiting_run_keeps_its_pinned_revision_and_continuation_across_a_store_reopen()
    {
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var resumeAt = now.AddMinutes(45);
        var automationId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var graphId = Guid.NewGuid();

        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var store = new AutomationRepository(database);

        await store.UpsertAsync(new AutomationDefinition(automationId, "Durable wait fixture", HavenMode.Chat,
            "Wait then continue", AutomationScheduleKind.Daily, """{"time":"08:00"}""", now.AddDays(1),
            null, true, now, now), token).WaitAsync(Bound, token);

        // The suspended Run must still hold the definition lease.
        var leaseToken = "lease-1";
        Assert.True(await store.TryAcquireLeaseAsync(automationId, leaseToken, now.AddMinutes(30), token));

        // A Run suspended inside a Wait node. The continuation is the only thing that lets a restarted
        // process resume at the same node instead of restarting the graph and repeating side effects.
        var suspended = new AutomationRun(runId, automationId, AutomationRunStatus.Waiting, resumeAt,
            now, null, null, null, leaseToken)
        {
            Revision = 7,
            DefinitionRevision = 4,
            PinnedGraph = new AutomationGraphBinding(graphId, 4),
            AdmissionSnapshotJson = """{"admitted":"capabilities"}""",
            ContinuationDescriptorJson = """{"nodeId":"wait-2","resumeAt":"2026-10-19T12:45:00+00:00"}""",
            Details = new AutomationRunDetails(Guid.NewGuid(), "evt-1", """{"input":"value"}""", null,
                """[{"nodeId":"trigger-1","state":"completed"},{"nodeId":"action-1","state":"committed"}]""",
                null, """[{"runId":"parent-1"}]""", """["audit-1"]""", Guid.NewGuid())
        };
        await store.SuspendRunAsync(suspended, token).WaitAsync(Bound, token);

        // A second store over the same file stands in for a restarted process.
        var reopened = new AutomationRepository(NewDatabase());
        var due = await reopened.GetResumableRunsAsync(resumeAt, 10, token).WaitAsync(Bound, token);
        var restored = Assert.Single(due, run => run.Id == runId);
        Assert.Equal(AutomationRunStatus.Waiting, restored.Status);
        Assert.Equal(automationId, restored.AutomationId);
        Assert.Equal(7, restored.Revision);
        Assert.Equal(4, restored.DefinitionRevision);
        Assert.NotNull(restored.PinnedGraph);
        Assert.Equal(graphId, restored.PinnedGraph!.GraphId);
        Assert.Equal(4, restored.PinnedGraph.Revision);
        Assert.Equal("""{"admitted":"capabilities"}""", restored.AdmissionSnapshotJson);
        Assert.Contains("wait-2", restored.ContinuationDescriptorJson);
        Assert.NotNull(restored.Details);
        Assert.Contains("committed", restored.Details!.NodeExecutionStatesJson);
        Assert.Contains("parent-1", restored.Details.CausalAncestryJson);
        Assert.Equal("evt-1", restored.Details.TriggerEventId);

        // The same Run read through the Library history path keeps its pinned revision too.
        var history = await reopened.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token);
        Assert.Contains(history, run => run.Id == runId && run.DefinitionRevision == 4 && run.PinnedGraph is not null);

        // A wait that is not due yet must not be offered for resume.
        var early = await reopened.GetResumableRunsAsync(resumeAt.AddMinutes(-1), 10, token).WaitAsync(Bound, token);
        Assert.DoesNotContain(early, run => run.Id == runId);

        // Suspending a Run must not consume the definition's schedule or release its lease: the
        // definition is still occupied by the suspended Run.
        var definitions = await reopened.GetAllAsync(token).WaitAsync(Bound, token);
        var definition = Assert.Single(definitions, item => item.Id == automationId);
        Assert.Equal(now.AddDays(1), definition.NextRunAt);
    }

    [Fact]
    public async Task Suspended_run_completes_same_id_to_single_terminal_row_preserving_original_pin()
    {
        var fx = await NewLeasedFixtureAsync();
        var suspended = SuspendedRun(fx.RunId, fx.AutomationId, fx.Token, fx.Now.AddMinutes(45), fx.Now, revision: 7);
        await fx.Store.SuspendRunAsync(suspended, token).WaitAsync(Bound, token);

        var reopened = new AutomationRepository(NewDatabase());
        var completed = suspended with { Status = AutomationRunStatus.Succeeded, CompletedAt = fx.Now.AddMinutes(1), Result = "ok", Revision = 8 };
        await reopened.CompleteRunAsync(completed, fx.Now.AddDays(2), token).WaitAsync(Bound, token);

        var runs = await reopened.GetRunsAsync(fx.AutomationId, 10, token).WaitAsync(Bound, token);
        var row = Assert.Single(runs);
        Assert.Equal(fx.RunId, row.Id);
        Assert.Equal(fx.AutomationId, row.AutomationId);
        Assert.Equal(AutomationRunStatus.Succeeded, row.Status);
        Assert.Equal(8, row.Revision);
        Assert.Equal(4, row.DefinitionRevision);
        Assert.NotNull(row.PinnedGraph);
        Assert.Equal(suspended.PinnedGraph!.GraphId, row.PinnedGraph!.GraphId);
        Assert.Equal(4, row.PinnedGraph.Revision);
        Assert.Equal(suspended.AdmissionSnapshotJson, row.AdmissionSnapshotJson);
        Assert.Equal(suspended.ContinuationDescriptorJson, row.ContinuationDescriptorJson);
        Assert.NotNull(row.Details);
        Assert.Equal(suspended.Details!.TriggerEventId, row.Details!.TriggerEventId);
        Assert.Equal(suspended.Details!.NodeExecutionStatesJson, row.Details!.NodeExecutionStatesJson);
        Assert.Equal(suspended.Details!.CausalAncestryJson, row.Details!.CausalAncestryJson);

        // The definition lease is released and the schedule advanced by the completion.
        var def = await ReadAutomationRowAsync(fx.AutomationId);
        Assert.Null(def.LeaseToken);
        Assert.Equal(fx.Now.AddDays(2).ToUniversalTime().ToString("O"), def.NextRunAt);
    }

    [Fact]
    public async Task Identical_terminal_retry_is_a_no_write_ack_and_never_reschedules_or_touches_a_newer_lease()
    {
        var fx = await NewLeasedFixtureAsync();
        var suspended = SuspendedRun(fx.RunId, fx.AutomationId, fx.Token, fx.Now.AddMinutes(45), fx.Now, revision: 7);
        await fx.Store.SuspendRunAsync(suspended, token).WaitAsync(Bound, token);

        var completed = suspended with { Status = AutomationRunStatus.Succeeded, CompletedAt = fx.Now.AddMinutes(1), Result = "ok", Revision = 8 };
        await fx.Store.CompleteRunAsync(completed, fx.Now.AddDays(2), token).WaitAsync(Bound, token);

        // Reopen (reconstruct the same serialized payload from disk) as a restarted process would.
        var reopened = new AutomationRepository(NewDatabase());
        var afterFirst = await ReadAutomationRowAsync(fx.AutomationId);

        // A newer lease is acquired and the schedule is advanced behind the ack's back.
        Assert.True(await reopened.TryAcquireLeaseAsync(fx.AutomationId, "lease-2", fx.Now.AddMinutes(60), token));
        await using (var connection = await NewDatabase().OpenAsync(token))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE automations SET next_run_at=$nr WHERE id=$id;";
            command.Parameters.AddWithValue("$nr", fx.Now.AddYears(9).ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$id", fx.AutomationId.ToString());
            await command.ExecuteNonQueryAsync(token);
        }
        var beforeNewer = await ReadAutomationRowAsync(fx.AutomationId);

        // Write-blocking triggers prove the retry issues NO write to either table: any INSERT/UPDATE/
        // DELETE on automation_runs or automations would raise and fail the test.
        await using (var connection = await NewDatabase().OpenAsync(token))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TRIGGER block_run_insert BEFORE INSERT ON automation_runs
                BEGIN SELECT RAISE(ABORT, 'blocked run insert'); END;
                CREATE TRIGGER block_run_update BEFORE UPDATE ON automation_runs
                BEGIN SELECT RAISE(ABORT, 'blocked run update'); END;
                CREATE TRIGGER block_run_delete BEFORE DELETE ON automation_runs
                BEGIN SELECT RAISE(ABORT, 'blocked run delete'); END;
                CREATE TRIGGER block_def_update BEFORE UPDATE ON automations
                BEGIN SELECT RAISE(ABORT, 'blocked definition update'); END;
                """;
            await command.ExecuteNonQueryAsync(token);
        }

        // The identical retry with a DIFFERENT nextRunAt must be a pure no-write acknowledgement.
        await reopened.CompleteRunAsync(completed, fx.Now.AddYears(5), token).WaitAsync(Bound, token);
        Assert.Equal(beforeNewer, await ReadAutomationRowAsync(fx.AutomationId));
        Assert.Equal("lease-2", (await ReadAutomationRowAsync(fx.AutomationId)).LeaseToken);
        var runs = await reopened.GetRunsAsync(fx.AutomationId, 10, token).WaitAsync(Bound, token);
        Assert.Single(runs);
        Assert.Equal(8, Assert.Single(runs).Revision);
    }

    [Fact]
    public async Task Stale_jump_or_changed_same_version_transitions_reject_without_mutation()
    {
        // stale
        await AssertRejectedTransition((suspended, fx) => suspended with { Status = AutomationRunStatus.Succeeded, CompletedAt = fx.Now.AddMinutes(1), Revision = 6 });
        // jump
        await AssertRejectedTransition((suspended, fx) => suspended with { Status = AutomationRunStatus.Succeeded, CompletedAt = fx.Now.AddMinutes(1), Revision = 9 });
        // changed-same-version: same revision as stored but a different persisted payload
        await AssertRejectedTransition((suspended, fx) => suspended with { Status = AutomationRunStatus.Succeeded, CompletedAt = fx.Now.AddMinutes(1), Revision = 7, Result = "changed-without-revision-bump" });
    }

    [Fact]
    public async Task Wrong_identity_lease_or_stolen_lease_rejects_run_and_definition_unchanged()
    {
        // wrong AutomationId
        await AssertRejectedRunAndDefinitionUnchanged((suspended, fx) => suspended with { Status = AutomationRunStatus.Succeeded, CompletedAt = fx.Now.AddMinutes(1), Revision = 8, AutomationId = Guid.NewGuid() });

        // wrong lease token: the desired run's token differs from the run's OWN original stored token
        await AssertRejectedRunAndDefinitionUnchanged((suspended, fx) => suspended with { Status = AutomationRunStatus.Succeeded, CompletedAt = fx.Now.AddMinutes(1), Revision = 8, LeaseToken = "wrong-token" });

        // changed pinned graph identity
        await AssertRejectedRunAndDefinitionUnchanged((suspended, fx) => suspended with { Status = AutomationRunStatus.Succeeded, CompletedAt = fx.Now.AddMinutes(1), Revision = 8, PinnedGraph = new AutomationGraphBinding(Guid.NewGuid(), 4) });

        // changed pinned definition revision
        await AssertRejectedRunAndDefinitionUnchanged((suspended, fx) => suspended with { Status = AutomationRunStatus.Succeeded, CompletedAt = fx.Now.AddMinutes(1), Revision = 8, DefinitionRevision = 9 });

        // stolen definition lease: definition lease replaced behind the run's back
        await AssertStolenLeaseRejects();
    }

    [Fact]
    public async Task Terminal_run_cannot_be_resumed_or_re_suspended()
    {
        var fx = await NewLeasedFixtureAsync();
        var suspended = SuspendedRun(fx.RunId, fx.AutomationId, fx.Token, fx.Now.AddMinutes(45), fx.Now, revision: 7);
        await fx.Store.SuspendRunAsync(suspended, token).WaitAsync(Bound, token);

        var completed = suspended with { Status = AutomationRunStatus.Succeeded, CompletedAt = fx.Now.AddMinutes(1), Revision = 8 };
        await fx.Store.CompleteRunAsync(completed, fx.Now.AddDays(2), token).WaitAsync(Bound, token);

        // Re-acquire a lease so the only thing preventing suspension is the terminal-state guard.
        Assert.True(await fx.Store.TryAcquireLeaseAsync(fx.AutomationId, "lease-2", fx.Now.AddMinutes(60), token));
        var resume = suspended with { Status = AutomationRunStatus.Waiting, ScheduledFor = fx.Now.AddMinutes(90), Revision = 9, LeaseToken = "lease-2" };
        await Assert.ThrowsAnyAsync<Exception>(() => fx.Store.SuspendRunAsync(resume, token)).WaitAsync(Bound, token);

        var runs = await fx.Store.GetRunsAsync(fx.AutomationId, 10, token).WaitAsync(Bound, token);
        var row = Assert.Single(runs);
        Assert.Equal(AutomationRunStatus.Succeeded, row.Status);
        Assert.Equal(8, row.Revision);
    }

    [Fact]
    public async Task Forced_definition_update_failure_rolls_back_the_run_transition()
    {
        var fx = await NewLeasedFixtureAsync();
        var suspended = SuspendedRun(fx.RunId, fx.AutomationId, fx.Token, fx.Now.AddMinutes(45), fx.Now, revision: 7);
        await fx.Store.SuspendRunAsync(suspended, token).WaitAsync(Bound, token);

        var runBefore = await SerializeRunRowAsync(fx.RunId);
        var defBefore = await SerializeDefinitionRowAsync(fx.AutomationId);

        await using (var connection = await NewDatabase().OpenAsync(token))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TRIGGER force_automation_update_failure
                BEFORE UPDATE ON automations
                BEGIN SELECT RAISE(ABORT, 'forced definition update failure'); END;
                """;
            await command.ExecuteNonQueryAsync(token);
        }

        var completed = suspended with { Status = AutomationRunStatus.Succeeded, CompletedAt = fx.Now.AddMinutes(1), Revision = 8 };
        var ex = await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(
            () => fx.Store.CompleteRunAsync(completed, fx.Now.AddDays(2), token));
        Assert.Contains("forced definition update failure", ex.Message);

        // The Run transition rolled back with the failed definition CAS: every persisted Run and
        // definition column is byte-for-byte unchanged.
        Assert.Equal(runBefore, await SerializeRunRowAsync(fx.RunId));
        Assert.Equal(defBefore, await SerializeDefinitionRowAsync(fx.AutomationId));
    }

    [Fact]
    public async Task Two_stores_racing_different_terminal_outcomes_have_one_winner_and_no_overwrite_or_duplicate()
    {
        var fx = await NewLeasedFixtureAsync();
        var suspended = SuspendedRun(fx.RunId, fx.AutomationId, fx.Token, fx.Now.AddMinutes(45), fx.Now, revision: 7);
        await fx.Store.SuspendRunAsync(suspended, token).WaitAsync(Bound, token);

        var first = suspended with { Status = AutomationRunStatus.Succeeded, CompletedAt = fx.Now.AddMinutes(1), Result = "first", Revision = 8 };
        var second = suspended with { Status = AutomationRunStatus.Failed, CompletedAt = fx.Now.AddMinutes(1), Error = "second", Revision = 8 };

        // Coordinate both stores to start their write transactions together. The losing outcome must be
        // the specific optimistic-conflict guard, so a generic catch must never hide an unrelated
        // SQLITE_BUSY or a setup fault.
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<RaceOutcome> RunFromStoreAsync(AutomationRun candidate, DateTimeOffset schedule)
        {
            await gate.Task;
            var store = new AutomationRepository(NewDatabase());
            try { await store.CompleteRunAsync(candidate, schedule, token); return new RaceOutcome(true, null); }
            catch (Exception error) { return new RaceOutcome(false, error); }
        }

        var taskA = RunFromStoreAsync(first, fx.Now.AddDays(2));
        var taskB = RunFromStoreAsync(second, fx.Now.AddDays(3));
        gate.TrySetResult(true);
        var outcomes = await Task.WhenAll(taskA, taskB);

        Assert.Equal(1, outcomes.Count(outcome => outcome.Ok));
        var loser = Assert.Single(outcomes.Where(outcome => !outcome.Ok));
        var conflict = Assert.IsType<InvalidOperationException>(loser!.Error);
        Assert.Equal("Concurrent run transition conflict.", conflict.Message);

        var aWon = outcomes[0].Ok;
        var runs = await fx.Store.GetRunsAsync(fx.AutomationId, 10, token).WaitAsync(Bound, token);
        var row = Assert.Single(runs);
        Assert.Equal(8, row.Revision);
        if (aWon)
        {
            Assert.Equal(AutomationRunStatus.Succeeded, row.Status);
            Assert.Equal("first", row.Result);
        }
        else
        {
            Assert.Equal(AutomationRunStatus.Failed, row.Status);
            Assert.Equal("second", row.Error);
        }

        // The persisted schedule is exactly the winner's, never the loser's.
        var expectedNext = (aWon ? fx.Now.AddDays(2) : fx.Now.AddDays(3)).ToUniversalTime().ToString("O");
        Assert.Equal(expectedNext, (await ReadAutomationRowAsync(fx.AutomationId)).NextRunAt);
    }

    [Fact]
    public async Task Legacy_leased_new_terminal_and_null_lease_history_append_both_write_only_intended_rows()
    {
        // Legacy scheduled write: a brand-new terminal Run carrying the acquired lease and NO canonical
        // fields (revision defaults to zero). It must complete AND release/advance the definition.
        var fx = await NewLeasedFixtureAsync();
        var legacy = new AutomationRun(Guid.NewGuid(), fx.AutomationId, AutomationRunStatus.Succeeded, fx.Now,
            fx.Now, fx.Now, "legacy", null, fx.Token);
        await fx.Store.CompleteRunAsync(legacy, fx.Now.AddDays(2), token).WaitAsync(Bound, token);

        var defAfterLegacy = await ReadAutomationRowAsync(fx.AutomationId);
        Assert.Null(defAfterLegacy.LeaseToken);
        Assert.Equal(fx.Now.AddDays(2).ToUniversalTime().ToString("O"), defAfterLegacy.NextRunAt);
        var legacyRuns = await fx.Store.GetRunsAsync(fx.AutomationId, 10, token).WaitAsync(Bound, token);
        Assert.Contains(legacyRuns, run => run.Id == legacy.Id && run.Revision == 0);

        // History-only append: a brand-new Succeeded Run with a NULL lease while the definition holds an
        // unrelated active lease. It must append history WITHOUT mutating the definition at all.
        var fx2 = await NewLeasedFixtureAsync();
        var before = await ReadAutomationRowAsync(fx2.AutomationId);
        var history = new AutomationRun(Guid.NewGuid(), fx2.AutomationId, AutomationRunStatus.Succeeded, fx2.Now,
            fx2.Now, fx2.Now, "history", null, null);
        await fx2.Store.CompleteRunAsync(history, fx2.Now.AddDays(9), token).WaitAsync(Bound, token);

        var after = await ReadAutomationRowAsync(fx2.AutomationId);
        Assert.Equal(before, after);
        Assert.Equal(fx2.Token, after.LeaseToken); // unrelated active lease preserved
        Assert.Equal(fx2.Now.AddDays(1).ToUniversalTime().ToString("O"), after.NextRunAt);
        var historyRuns = await fx2.Store.GetRunsAsync(fx2.AutomationId, 10, token).WaitAsync(Bound, token);
        Assert.Contains(historyRuns, run => run.Id == history.Id && run.Revision == 0);
    }

    [Fact]
    public async Task Suspend_leaves_schedule_and_lease_intact_and_reading_due_approval_does_not_approve()
    {
        var fx = await NewLeasedFixtureAsync();
        var before = await ReadAutomationRowAsync(fx.AutomationId);

        var approval = SuspendedRun(fx.RunId, fx.AutomationId, fx.Token, fx.Now, fx.Now, revision: 7,
            status: AutomationRunStatus.ApprovalRequired);
        await fx.Store.SuspendRunAsync(approval, token).WaitAsync(Bound, token);

        Assert.Equal(before, await ReadAutomationRowAsync(fx.AutomationId));

        var reopened = new AutomationRepository(NewDatabase());
        var due = await reopened.GetResumableRunsAsync(fx.Now, 10, token).WaitAsync(Bound, token);
        var row = Assert.Single(due, run => run.Id == fx.RunId);
        Assert.Equal(AutomationRunStatus.ApprovalRequired, row.Status);
        Assert.NotEqual(AutomationRunStatus.Succeeded, row.Status);

        Assert.Equal(before, await ReadAutomationRowAsync(fx.AutomationId));
    }

    [Fact]
    public async Task The_store_exposes_the_continuation_capability()
    {
        // The continuation contract is a separate reviewed capability, so a caller must be able to tell
        // that a store supports durable suspension/resume rather than assume it does.
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        Assert.IsAssignableFrom<IAutomationRunContinuationStore>(new AutomationRepository(database));
    }

    [Fact]
    public async Task Stored_A_desired_B_complete_rejects_when_definition_now_holds_B_both_rows_unchanged()
    {
        var fx = await NewLeasedFixtureAsync();
        var suspended = SuspendedRun(fx.RunId, fx.AutomationId, fx.Token, fx.Now.AddMinutes(45), fx.Now, revision: 7);
        await fx.Store.SuspendRunAsync(suspended, token).WaitAsync(Bound, token);

        // The definition now holds a NEWER lease B behind the run's back.
        Assert.True(await fx.Store.TryAcquireLeaseAsync(fx.AutomationId, "lease-B", fx.Now.AddMinutes(61), token));
        var runBefore = await SerializeRunRowAsync(fx.RunId);
        var defBefore = await SerializeDefinitionRowAsync(fx.AutomationId);

        // Desired carries B and the definition currently holds B: it must still reject because the
        // run's ORIGINAL stored token is A (A->B must not mask the status/lease guard).
        var completed = suspended with { Status = AutomationRunStatus.Succeeded, CompletedAt = fx.Now.AddMinutes(1), LeaseToken = "lease-B", Revision = 8 };
        await Assert.ThrowsAnyAsync<Exception>(() => fx.Store.CompleteRunAsync(completed, fx.Now.AddDays(2), token));

        Assert.Equal(runBefore, await SerializeRunRowAsync(fx.RunId));
        Assert.Equal(defBefore, await SerializeDefinitionRowAsync(fx.AutomationId));
    }

    [Fact]
    public async Task Stored_A_desired_B_suspend_rejects_when_definition_now_holds_B_both_rows_unchanged()
    {
        var fx = await NewLeasedFixtureAsync();
        var suspended = SuspendedRun(fx.RunId, fx.AutomationId, fx.Token, fx.Now.AddMinutes(45), fx.Now, revision: 7);
        await fx.Store.SuspendRunAsync(suspended, token).WaitAsync(Bound, token);

        Assert.True(await fx.Store.TryAcquireLeaseAsync(fx.AutomationId, "lease-B", fx.Now.AddMinutes(61), token));
        var runBefore = await SerializeRunRowAsync(fx.RunId);
        var defBefore = await SerializeDefinitionRowAsync(fx.AutomationId);

        var resume = suspended with { LeaseToken = "lease-B", ScheduledFor = fx.Now.AddMinutes(90), Revision = 8 };
        await Assert.ThrowsAnyAsync<Exception>(() => fx.Store.SuspendRunAsync(resume, token));

        Assert.Equal(runBefore, await SerializeRunRowAsync(fx.RunId));
        Assert.Equal(defBefore, await SerializeDefinitionRowAsync(fx.AutomationId));
    }

    [Fact]
    public async Task Stored_A_desired_null_complete_rejects_both_rows_unchanged()
    {
        var fx = await NewLeasedFixtureAsync();
        var suspended = SuspendedRun(fx.RunId, fx.AutomationId, fx.Token, fx.Now.AddMinutes(45), fx.Now, revision: 7);
        await fx.Store.SuspendRunAsync(suspended, token).WaitAsync(Bound, token);

        var runBefore = await SerializeRunRowAsync(fx.RunId);
        var defBefore = await SerializeDefinitionRowAsync(fx.AutomationId);

        // A null desired token would otherwise bypass the definition CAS for an existing leased waiting
        // run; it must be rejected with both rows unchanged.
        var completed = suspended with { Status = AutomationRunStatus.Succeeded, CompletedAt = fx.Now.AddMinutes(1), LeaseToken = null, Revision = 8 };
        await Assert.ThrowsAnyAsync<Exception>(() => fx.Store.CompleteRunAsync(completed, fx.Now.AddDays(2), token));

        Assert.Equal(runBefore, await SerializeRunRowAsync(fx.RunId));
        Assert.Equal(defBefore, await SerializeDefinitionRowAsync(fx.AutomationId));
    }

    [Fact]
    public async Task Complete_rejects_waiting_and_unknown_statuses_without_mutation()
    {
        var fx = await NewLeasedFixtureAsync();
        var suspended = SuspendedRun(fx.RunId, fx.AutomationId, fx.Token, fx.Now.AddMinutes(45), fx.Now, revision: 7);
        await fx.Store.SuspendRunAsync(suspended, token).WaitAsync(Bound, token);

        await AssertCompleteRejectedWithoutMutationAsync(fx, suspended with { Status = AutomationRunStatus.Waiting, Revision = 8 });
        await AssertCompleteRejectedWithoutMutationAsync(fx, suspended with { Status = (AutomationRunStatus)999, Revision = 8 });
    }

    [Fact]
    public async Task Suspend_rejects_terminal_and_unknown_statuses_without_mutation()
    {
        var fx = await NewLeasedFixtureAsync();
        var suspended = SuspendedRun(fx.RunId, fx.AutomationId, fx.Token, fx.Now.AddMinutes(45), fx.Now, revision: 7);
        await fx.Store.SuspendRunAsync(suspended, token).WaitAsync(Bound, token);

        await AssertSuspendRejectedWithoutMutationAsync(fx, suspended with { Status = AutomationRunStatus.Succeeded, CompletedAt = fx.Now.AddMinutes(1), Revision = 8 });
        await AssertSuspendRejectedWithoutMutationAsync(fx, suspended with { Status = (AutomationRunStatus)999, Revision = 8 });
    }

    // ---- helpers ----

    private sealed record LeasedFixture(
        SqliteDatabase Database, AutomationRepository Store, Guid AutomationId, Guid RunId, DateTimeOffset Now, string Token);

    private async Task<LeasedFixture> NewLeasedFixtureAsync()
    {
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var automationId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var store = new AutomationRepository(database);

        await store.UpsertAsync(new AutomationDefinition(automationId, "Transition fixture", HavenMode.Chat,
            "transition", AutomationScheduleKind.Daily, """{"time":"08:00"}""", now.AddDays(1),
            null, true, now, now), token).WaitAsync(Bound, token);

        var leaseToken = "lease-1";
        Assert.True(await store.TryAcquireLeaseAsync(automationId, leaseToken, now.AddMinutes(30), token));
        return new LeasedFixture(database, store, automationId, runId, now, leaseToken);
    }

    private static AutomationRun SuspendedRun(Guid runId, Guid automationId, string token, DateTimeOffset scheduledFor,
        DateTimeOffset now, long revision = 7, AutomationRunStatus status = AutomationRunStatus.Waiting, Guid? graphId = null)
    {
        var graph = graphId ?? Guid.NewGuid();
        return new AutomationRun(runId, automationId, status, scheduledFor, now, null, null, null, token)
        {
            Revision = revision,
            DefinitionRevision = 4,
            PinnedGraph = new AutomationGraphBinding(graph, 4),
            AdmissionSnapshotJson = """{"admitted":"capabilities"}""",
            ContinuationDescriptorJson = """{"nodeId":"wait-2","resumeAt":"2026-10-19T12:45:00+00:00"}""",
            Details = new AutomationRunDetails(Guid.NewGuid(), "evt-1", """{"input":"value"}""", null,
                """[{"nodeId":"trigger-1","state":"completed"},{"nodeId":"action-1","state":"committed"}]""",
                null, """[{"runId":"parent-1"}]""", """["audit-1"]""", Guid.NewGuid())
        };
    }

    private async Task AssertRejectedTransition(
        Func<AutomationRun, LeasedFixture, AutomationRun> mutate)
    {
        var fx = await NewLeasedFixtureAsync();
        var suspended = SuspendedRun(fx.RunId, fx.AutomationId, fx.Token, fx.Now.AddMinutes(45), fx.Now, revision: 7);
        await fx.Store.SuspendRunAsync(suspended, token).WaitAsync(Bound, token);

        var completed = mutate(suspended, fx);
        await Assert.ThrowsAnyAsync<Exception>(() => fx.Store.CompleteRunAsync(completed, fx.Now.AddDays(2), token)).WaitAsync(Bound, token);

        var runs = await fx.Store.GetRunsAsync(fx.AutomationId, 10, token).WaitAsync(Bound, token);
        var row = Assert.Single(runs);
        Assert.Equal(7, row.Revision);
        Assert.Equal(AutomationRunStatus.Waiting, row.Status);
    }

    private async Task AssertRejectedRunAndDefinitionUnchanged(
        Func<AutomationRun, LeasedFixture, AutomationRun> mutate)
    {
        var fx = await NewLeasedFixtureAsync();
        var suspended = SuspendedRun(fx.RunId, fx.AutomationId, fx.Token, fx.Now.AddMinutes(45), fx.Now, revision: 7);
        await fx.Store.SuspendRunAsync(suspended, token).WaitAsync(Bound, token);

        var before = await ReadAutomationRowAsync(fx.AutomationId);
        var completed = mutate(suspended, fx);
        await Assert.ThrowsAnyAsync<Exception>(() => fx.Store.CompleteRunAsync(completed, fx.Now.AddDays(2), token)).WaitAsync(Bound, token);

        var runs = await fx.Store.GetRunsAsync(fx.AutomationId, 10, token).WaitAsync(Bound, token);
        var row = Assert.Single(runs);
        Assert.Equal(7, row.Revision);
        Assert.Equal(AutomationRunStatus.Waiting, row.Status);
        Assert.Equal(suspended.PinnedGraph!.GraphId, row.PinnedGraph!.GraphId);

        Assert.Equal(before, await ReadAutomationRowAsync(fx.AutomationId));
    }

    private async Task AssertStolenLeaseRejects()
    {
        var fx = await NewLeasedFixtureAsync();
        var suspended = SuspendedRun(fx.RunId, fx.AutomationId, fx.Token, fx.Now.AddMinutes(45), fx.Now, revision: 7);
        await fx.Store.SuspendRunAsync(suspended, token).WaitAsync(Bound, token);

        // Replace the definition lease behind the run's back (stolen/released then re-leased).
        await using (var connection = await NewDatabase().OpenAsync(token))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE automations SET lease_token=$t WHERE id=$id;";
            command.Parameters.AddWithValue("$t", "stolen-token");
            command.Parameters.AddWithValue("$id", fx.AutomationId.ToString());
            await command.ExecuteNonQueryAsync(token);
        }

        var completed = suspended with { Status = AutomationRunStatus.Succeeded, CompletedAt = fx.Now.AddMinutes(1), Revision = 8 };
        await Assert.ThrowsAnyAsync<Exception>(() => fx.Store.CompleteRunAsync(completed, fx.Now.AddDays(2), token)).WaitAsync(Bound, token);

        var runs = await fx.Store.GetRunsAsync(fx.AutomationId, 10, token).WaitAsync(Bound, token);
        var row = Assert.Single(runs);
        Assert.Equal(7, row.Revision);
        Assert.Equal(AutomationRunStatus.Waiting, row.Status);

        var def = await ReadAutomationRowAsync(fx.AutomationId);
        Assert.Equal("stolen-token", def.LeaseToken);
        Assert.Equal(fx.Now.AddDays(1).ToUniversalTime().ToString("O"), def.NextRunAt);
    }

    private sealed record RaceOutcome(bool Ok, Exception? Error);

    private async Task<string> SerializeRunRowAsync(Guid runId)
    {
        await using var connection = await NewDatabase().OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM automation_runs WHERE id=$id;";
        command.Parameters.AddWithValue("$id", runId.ToString());
        await using var reader = await command.ExecuteReaderAsync(token);
        await reader.ReadAsync(token);
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            sb.Append(reader.GetName(i)).Append('=');
            sb.Append(reader.IsDBNull(i) ? "<NULL>" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture));
            sb.Append('|');
        }
        return sb.ToString();
    }

    private async Task<string> SerializeDefinitionRowAsync(Guid automationId)
    {
        await using var connection = await NewDatabase().OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM automations WHERE id=$id;";
        command.Parameters.AddWithValue("$id", automationId.ToString());
        await using var reader = await command.ExecuteReaderAsync(token);
        await reader.ReadAsync(token);
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            sb.Append(reader.GetName(i)).Append('=');
            sb.Append(reader.IsDBNull(i) ? "<NULL>" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture));
            sb.Append('|');
        }
        return sb.ToString();
    }

    private async Task AssertCompleteRejectedWithoutMutationAsync(LeasedFixture fx, AutomationRun completed)
    {
        var runBefore = await SerializeRunRowAsync(fx.RunId);
        var defBefore = await SerializeDefinitionRowAsync(fx.AutomationId);
        await Assert.ThrowsAnyAsync<Exception>(() => fx.Store.CompleteRunAsync(completed, fx.Now.AddDays(2), token)).WaitAsync(Bound, token);
        Assert.Equal(runBefore, await SerializeRunRowAsync(fx.RunId));
        Assert.Equal(defBefore, await SerializeDefinitionRowAsync(fx.AutomationId));
    }

    private async Task AssertSuspendRejectedWithoutMutationAsync(LeasedFixture fx, AutomationRun resume)
    {
        var runBefore = await SerializeRunRowAsync(fx.RunId);
        var defBefore = await SerializeDefinitionRowAsync(fx.AutomationId);
        await Assert.ThrowsAnyAsync<Exception>(() => fx.Store.SuspendRunAsync(resume, token)).WaitAsync(Bound, token);
        Assert.Equal(runBefore, await SerializeRunRowAsync(fx.RunId));
        Assert.Equal(defBefore, await SerializeDefinitionRowAsync(fx.AutomationId));
    }

    private async Task<(string? LeaseToken, string? LeaseUntil, string? NextRunAt, string? UpdatedAt)> ReadAutomationRowAsync(Guid automationId)
    {
        await using var connection = await NewDatabase().OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT lease_token, lease_until, next_run_at, updated_at FROM automations WHERE id=$id;";
        command.Parameters.AddWithValue("$id", automationId.ToString());
        await using var reader = await command.ExecuteReaderAsync(token);
        await reader.ReadAsync(token);
        return (reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private SqliteDatabase NewDatabase()
    {
        Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", _root);
        return new SqliteDatabase(new AppPaths());
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", null);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
