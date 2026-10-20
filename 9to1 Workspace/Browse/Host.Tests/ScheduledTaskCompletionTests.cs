/*
 * FILE DOCUMENTATION
 * Where: Browse/Host.Tests/ScheduledTaskCompletionTests.cs, the real Haven host route test lane.
 * What: Proves the scheduler completion/persistence boundary on the REAL migrated SQLite store and the
 *       ACTUAL ScheduledTaskRunner: one stable Run identity allocated before execution, execution outcome
 *       separation from completion acknowledgment, and truthful typed persistence-uncertainty plus
 *       bounded same-payload reconciliation. It never rewrites the SQLite repository as a memory fake.
 * Why: Spec 8120-124 and 8241-8253, 8330 require stable run identity, idempotent completion, and typed
 *       uncertain outcomes rather than swallowing persistence faults as ordinary failed execution.
 * Maintenance: Uses the real SQLite schema/migrations and actual repository. Tests that force a
 *       completion failure assert exact row/definition state and that model/action work is never
 *       re-executed by a persistence fault or by reconciliation.
 */

using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HavenOS.Browse.Host.Tests;

public sealed class ScheduledTaskCompletionTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);
    private readonly CancellationToken token = TestContext.Current.CancellationToken;
    private readonly string _root = Directory.CreateTempSubdirectory("haven-scheduler-completion-").FullName;

    [Fact]
    public async Task Lost_ack_after_durable_commit_keeps_one_succeeded_run_and_reconciles_without_writes_or_replay()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var realStore = new AutomationRepository(database);
        var task = DueDefinition(now);
        await realStore.UpsertAsync(task, token).WaitAsync(Bound, token);

        var model = new FakeModelClient("ok result");
        var controlled = new ControlledCompletionRepository(realStore) { ThrowAfterCommit = new AckLossException() };
        var runner = new ScheduledTaskRunner(controlled, model, new ScheduledTaskScheduleCalculator());

        var ex = await Assert.ThrowsAsync<ScheduledTaskCompletionUncertaintyException>(
            () => runner.RunOneAsync(task, now, token)).WaitAsync(Bound, token);

        // Execution ran exactly once; nothing was relabelled Failed/Cancelled.
        Assert.Equal(1, model.CompleteCount);
        var draft = ex.Draft;
        Assert.Equal(AutomationRunStatus.Succeeded, draft.Run.Status);
        Assert.Equal(task.Id, draft.Run.AutomationId);
        Assert.Equal(tokenok result", draft.Run.Result);
        Assert.Equal("ScheduledTaskCompletionUncertain", ex.Code);
        Assert.True(ex.IsRecoverable);
        Assert.IsType<AckLossException>(ex.PersistenceCause);

        // The durable commit already landed: exactly ONE Succeeded row with the exact identity/payload/token.
        var reopened = new AutomationRepository(NewDatabase());
        var runs = await reopened.GetRunsAsync(task.Id, 10, token).WaitAsync(Bound, token);
        var row = Assert.Single(runs);
        Assert.Equal(draft.Run.Id, row.Id);
        Assert.Equal(AutomationRunStatus.Succeeded, row.Status);
        Assert.Equal("ok result", row.Result);
        Assert.Equal(draft.Run.LeaseToken, row.LeaseToken);

        // The commit released the definition lease and advanced the schedule.
        var defAfterLostAck = await ReadAutomationRowAsync(task.Id);
        Assert.Null(defAfterLostAck.LeaseToken);
        Assert.NotNull(defAfterLostAck.NextRunAt);

        // Reconciliation of the EXACT payload under write-blocking triggers is a confirmed no-write ACK:
        // zero SQL mutations, no reschedule/lease change, no re-execution.
        await InstallWriteBlockingTriggersAsync();
        var reconciliation = await new ScheduledTaskRunner(reopened, model, new ScheduledTaskScheduleCalculator())
            .ReconcileCompletionAsync(draft, token).WaitAsync(Bound, token);
        Assert.Equal(ScheduledTaskCompletionReconciliationKind.ConfirmedNoWrite, reconciliation.Kind);
        Assert.Equal(defAfterLostAck, await ReadAutomationRowAsync(task.Id));
        Assert.Equal(1, model.CompleteCount);

        var after = await reopened.GetRunsAsync(task.Id, 10, token).WaitAsync(Bound, token);
        Assert.Single(after);
    }

    [Fact]
    public async Task Forced_completion_write_failure_rolls_back_then_reconcile_writes_once_without_replay()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var realStore = new AutomationRepository(database);
        var task = DueDefinition(now);
        await realStore.UpsertAsync(task, token).WaitAsync(Bound, token);

        var before = await ReadAutomationRowAsync(task.Id);
        Assert.Null(before.LeaseToken);
        var nextBefore = before.NextRunAt;

        // Force ONLY the completion's definition-release update to abort (the lease-acquisition update is
        // unaffected because it sets a non-null token).
        await InstallDefinitionUpdateAbortTriggerAsync();

        var model = new FakeModelClient("ok result");
        var runner = new ScheduledTaskRunner(realStore, model, new ScheduledTaskScheduleCalculator());
        var ex = await Assert.ThrowsAsync<ScheduledTaskCompletionUncertaintyException>(
            () => runner.RunOneAsync(task, now, token)).WaitAsync(Bound, token);

        Assert.Equal(1, model.CompleteCount);
        var draft = ex.Draft;
        Assert.Equal(AutomationRunStatus.Succeeded, draft.Run.Status);
        Assert.IsType<SqliteException>(ex.PersistenceCause);
        Assert.Contains("forced definition update failure", ex.PersistenceCause.Message);

        // The failed completion rolled back fully: no Run row at all, and the definition still holds the
        // runner's lease with the schedule unchanged (the release was rolled back).
        var reopened = new AutomationRepository(NewDatabase());
        Assert.Empty(await reopened.GetRunsAsync(task.Id, 10, token).WaitAsync(Bound, token));
        var afterFailure = await ReadAutomationRowAsync(task.Id);
        Assert.NotNull(afterFailure.LeaseToken);
        Assert.Equal(nextBefore, afterFailure.NextRunAt);

        // Remove the controlled trigger, then reconcile the EXACT payload: it writes/ACKs once with no
        // execution replay.
        await DropDefinitionUpdateAbortTriggerAsync();
        var reconciliation = await new ScheduledTaskRunner(reopened, model, new ScheduledTaskScheduleCalculator())
            .ReconcileCompletionAsync(draft, token).WaitAsync(Bound, token);
        Assert.Equal(ScheduledTaskCompletionReconciliationKind.ConfirmedWritten, reconciliation.Kind);
        Assert.Equal(1, model.CompleteCount);

        var wrote = await reopened.GetRunsAsync(task.Id, 10, token).WaitAsync(Bound, token);
        var row = Assert.Single(wrote);
        Assert.Equal(AutomationRunStatus.Succeeded, row.Status);
        Assert.Equal(draft.Run.Id, row.Id);
        Assert.Equal("ok result", row.Result);
        Assert.Equal(draft.Run.LeaseToken, row.LeaseToken);
    }

    [Fact]
    public async Task Forced_completion_write_failure_refuses_reconciliation_when_a_newer_lease_won()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var realStore = new AutomationRepository(database);
        var task = DueDefinition(now);
        await realStore.UpsertAsync(task, token).WaitAsync(Bound, token);

        await InstallDefinitionUpdateAbortTriggerAsync();
        var model = new FakeModelClient("ok result");
        var runner = new ScheduledTaskRunner(realStore, model, new ScheduledTaskScheduleCalculator());
        var ex = await Assert.ThrowsAsync<ScheduledTaskCompletionUncertaintyException>(
            () => runner.RunOneAsync(task, now, token)).WaitAsync(Bound, token);

        // A newer legitimate owner takes the definition lease behind the failed completion's back.
        await DropDefinitionUpdateAbortTriggerAsync();
        await using (var connection = await NewDatabase().OpenAsync(token))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE automations SET lease_token=$t, lease_until=$u WHERE id=$id;";
            command.Parameters.AddWithValue("$t", "lease-newer");
            command.Parameters.AddWithValue("$u", now.AddMinutes(120).ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$id", task.Id.ToString());
            await command.ExecuteNonQueryAsync(token);
        }
        var defBeforeRefusal = await SerializeDefinitionRowAsync(task.Id);

        var reopened = new AutomationRepository(NewDatabase());
        var refusal = await new ScheduledTaskRunner(reopened, model, new ScheduledTaskScheduleCalculator())
            .ReconcileCompletionAsync(ex.Draft, token).WaitAsync(Bound, token);
        Assert.Equal(ScheduledTaskCompletionReconciliationKind.Refused, refusal.Kind);
        Assert.Equal(defBeforeRefusal, await SerializeDefinitionRowAsync(task.Id)); // both rows unchanged
        Assert.Empty(await reopened.GetRunsAsync(task.Id, 10, token).WaitAsync(Bound, token));
        Assert.Equal(1, model.CompleteCount); // never re-executed
    }

    [Fact]
    public async Task Execution_failure_and_completion_error_retain_both_causes_with_one_stable_id()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var realStore = new AutomationRepository(database);
        var task = DueDefinition(now);
        await realStore.UpsertAsync(task, token).WaitAsync(Bound, token);

        await InstallDefinitionUpdateAbortTriggerAsync();
        var model = new FakeModelClient("unused") { Failure = new IOException("Provider unavailable.") };
        var runner = new ScheduledTaskRunner(realStore, model, new ScheduledTaskScheduleCalculator());
        var ex = await Assert.ThrowsAsync<ScheduledTaskCompletionUncertaintyException>(
            () => runner.RunOneAsync(task, now, token)).WaitAsync(Bound, token);

        // Bounded execution retries only; the persistence fault never re-entered execution (no extra model call).
        Assert.Equal(3, model.CompleteCount);
        var draft = ex.Draft;
        Assert.Equal(AutomationRunStatus.Failed, draft.Run.Status);
        Assert.Contains("Provider unavailable", draft.Run.Error ?? string.Empty);
        Assert.NotNull(draft.ExecutionFailure);
        Assert.Contains("Provider unavailable", draft.ExecutionFailure!.Message);
        Assert.IsType<SqliteException>(ex.PersistenceCause);
        Assert.NotSame(draft.ExecutionFailure, ex.PersistenceCause); // distinct, non-erased causes

        // No second terminal record (the rollback left the store empty).
        var reopened = new AutomationRepository(NewDatabase());
        Assert.Empty(await reopened.GetRunsAsync(task.Id, 10, token).WaitAsync(Bound, token));
    }

    [Fact]
    public async Task Cancellation_during_held_persistence_never_relabels_success_and_settles_on_release()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var realStore = new AutomationRepository(database);
        var task = DueDefinition(now);
        await realStore.UpsertAsync(task, token).WaitAsync(Bound, token);

        var cts = new CancellationTokenSource();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controlled = new ControlledCompletionRepository(realStore)
        {
            BeforeCommitGate = () => gate.Task,
            ThrowAfterCommit = new AckLossException(),
            UseNoneForCommit = true
        };
        var model = new FakeModelClient("ok result") { OnComplete = _ => executed.TrySetResult() };
        var runner = new ScheduledTaskRunner(controlled, model, new ScheduledTaskScheduleCalculator());

        var runTask = runner.RunOneAsync(task, now, cts.Token);
        await executed.Task.WaitAsync(Bound, token); // execution finished
        // A held persistence original prevents an apparent completed/acknowledged runner result before release.
        Assert.False(runTask.IsCompleted);

        cts.Cancel();
        try
        {
            gate.TrySetResult();
        }
        finally
        {
            gate.TrySetResult(); // drain in finally so a failed test cannot strand work
        }

        var ex = await Assert.ThrowsAsync<ScheduledTaskCompletionUncertaintyException>(() => runTask).WaitAsync(Bound, token);
        Assert.Equal(1, model.CompleteCount);
        // It never becomes a Cancelled execution; the exact success payload survives.
        Assert.Equal(AutomationRunStatus.Succeeded, ex.Draft.Run.Status);
        Assert.Equal("ok result", ex.Draft.Run.Result);
        Assert.IsType<AckLossException>(ex.PersistenceCause);

        var reopened = new AutomationRepository(NewDatabase());
        var runs = await reopened.GetRunsAsync(task.Id, 10, token).WaitAsync(Bound, token);
        var row = Assert.Single(runs);
        Assert.Equal(AutomationRunStatus.Succeeded, row.Status);
        Assert.Equal(ex.Draft.Run.Id, row.Id);

        // Reconciliation never executes the model again.
        var reconciliation = await new ScheduledTaskRunner(reopened, model, new ScheduledTaskScheduleCalculator())
            .ReconcileCompletionAsync(ex.Draft, token).WaitAsync(Bound, token);
        Assert.Equal(ScheduledTaskCompletionReconciliationKind.ConfirmedNoWrite, reconciliation.Kind);
        Assert.Equal(1, model.CompleteCount);
    }

    [Fact]
    public async Task RunDue_counts_uncertain_separately_and_never_relaunches_or_counts_it_as_failed()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var realStore = new AutomationRepository(database);

        var taskA = DueDefinition(now);
        var taskB = DueDefinition(now);
        await realStore.UpsertAsync(taskA, token).WaitAsync(Bound, token);
        await realStore.UpsertAsync(taskB, token).WaitAsync(Bound, token);

        var model = new FakeModelClient("ok result");
        var controlled = new ControlledCompletionRepository(realStore)
        {
            ThrowAfterCommit = new AckLossException(),
            ShouldThrowAfterCommit = run => run.AutomationId == taskB.Id
        };
        var runner = new ScheduledTaskRunner(controlled, model, new ScheduledTaskScheduleCalculator());

        var result = await runner.RunDueAsync(now, token).WaitAsync(Bound, token);
        Assert.Equal(2, result.Due);
        Assert.Equal(2, result.Started);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(1, result.Uncertain);
        Assert.Equal(2, model.CompleteCount); // exactly one execution each, never relaunched

        var reopened = new AutomationRepository(NewDatabase());
        var runsA = await reopened.GetRunsAsync(taskA.Id, 10, token).WaitAsync(Bound, token);
        var runsB = await reopened.GetRunsAsync(taskB.Id, 10, token).WaitAsync(Bound, token);
        Assert.Equal(AutomationRunStatus.Succeeded, Assert.Single(runsA).Status);
        Assert.Equal(AutomationRunStatus.Succeeded, Assert.Single(runsB).Status); // committed, ack lost only

        // A second immediate due sweep must not relaunch the uncertain completion.
        var second = await new ScheduledTaskRunner(reopened, model, new ScheduledTaskScheduleCalculator())
            .RunDueAsync(now, token).WaitAsync(Bound, token);
        Assert.Equal(0, second.Started);
        Assert.Equal(2, model.CompleteCount);
    }

    // ---- helpers ----

    private static AutomationDefinition DueDefinition(DateTimeOffset now)
    {
        var schedule = ScheduledTaskScheduleComposer.Compose(AutomationScheduleKind.Daily,
            new ScheduledTaskScheduleDraft(now.AddHours(1), new TimeOnly(8, 0), DayOfWeek.Monday, 1, 60));
        return new AutomationDefinition(Guid.NewGuid(), "Scheduler completion fixture", HavenMode.Chat,
            "execute scheduled instruction", AutomationScheduleKind.Daily, schedule, now, null, true, now, now);
    }

    private sealed class FakeModelClient(string response) : IOllamaClient
    {
        public Exception? Failure { get; init; }
        public Action<CancellationToken>? OnComplete { get; init; }
        public int CompleteCount { get; private set; }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([new ModelDescriptor(
                "qwen-test", 1, "qwen", "test", "test",
                new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow)]);

        public async IAsyncEnumerable<string> StreamChatAsync(
            OllamaChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return response;
        }

        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        {
            CompleteCount++;
            OnComplete?.Invoke(cancellationToken);
            return Failure is null ? Task.FromResult(response) : Task.FromException<string>(Failure);
        }

        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(response, []));
    }

    /// <summary>Delegating repository that can hold a completion at a gate, force a durable commit, and
    /// then throw a distinct ACK-loss exception — the explicit lost-ACK/held-persistence simulation.
    /// It never rewrites the real SQLite store; the durable write always goes through the inner store.</summary>
    private sealed class ControlledCompletionRepository(IAutomationRepository inner) : IAutomationRepository
    {
        public Func<Task>? BeforeCommitGate { get; init; }
        public Exception? ThrowAfterCommit { get; init; }
        public bool UseNoneForCommit { get; init; }
        public Func<AutomationRun, bool>? ShouldThrowAfterCommit { get; init; }

        public async Task CompleteRunAsync(AutomationRun run, DateTimeOffset? nextRunAt, CancellationToken cancellationToken)
        {
            if (BeforeCommitGate is not null) await BeforeCommitGate();
            var commitToken = UseNoneForCommit ? CancellationToken.None : cancellationToken;
            await inner.CompleteRunAsync(run, nextRunAt, commitToken);
            if (ThrowAfterCommit is not null && (ShouldThrowAfterCommit is null || ShouldThrowAfterCommit(run)))
                throw ThrowAfterCommit;
        }

        public Task<IReadOnlyList<AutomationDefinition>> GetAllAsync(CancellationToken cancellationToken) => inner.GetAllAsync(cancellationToken);
        public Task<IReadOnlyList<AutomationDefinition>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken) => inner.GetDueAsync(now, cancellationToken);
        public Task UpsertAsync(AutomationDefinition automation, CancellationToken cancellationToken) => inner.UpsertAsync(automation, cancellationToken);
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => inner.DeleteAsync(id, cancellationToken);
        public Task<bool> TryAcquireLeaseAsync(Guid automationId, string leaseToken, DateTimeOffset leaseUntil, CancellationToken cancellationToken) =>
            inner.TryAcquireLeaseAsync(automationId, leaseToken, leaseUntil, cancellationToken);
        public Task<IReadOnlyList<AutomationRun>> GetRunsAsync(Guid automationId, int limit, CancellationToken cancellationToken) =>
            inner.GetRunsAsync(automationId, limit, cancellationToken);
    }

    private sealed class AckLossException : Exception
    {
        public AckLossException() : base("Simulated acknowledgment loss after durable commit.") { }
    }

    private SqliteDatabase NewDatabase()
    {
        Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", _root);
        return new SqliteDatabase(new AppPaths());
    }

    /// <summary>Turns off the completion path's definition-release update only: the lease-acquisition
    /// update (which sets a NON-null token) is unaffected, while the completion's release (which clears
    /// the token to null) aborts and rolls back the whole Run+definition transaction.</summary>
    private async Task InstallDefinitionUpdateAbortTriggerAsync()
    {
        await using var connection = await NewDatabase().OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TRIGGER force_definition_completion_failure
            BEFORE UPDATE ON automations
            WHEN OLD.lease_token IS NOT NULL AND NEW.lease_token IS NULL
            BEGIN SELECT RAISE(ABORT, 'forced definition update failure'); END;
            """;
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task DropDefinitionUpdateAbortTriggerAsync()
    {
        await using var connection = await NewDatabase().OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP TRIGGER IF EXISTS force_definition_completion_failure;";
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task InstallWriteBlockingTriggersAsync()
    {
        await using var connection = await NewDatabase().OpenAsync(token);
        await using var command = connection.CreateCommand();
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

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", null);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
