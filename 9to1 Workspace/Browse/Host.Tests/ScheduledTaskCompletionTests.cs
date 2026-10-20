/*
 * FILE DOCUMENTATION
 * Where: Browse/Host.Tests/ScheduledTaskCompletionTests.cs, the real Haven host route test lane.
 * What: Proves the scheduler completion/persistence boundary on the REAL migrated SQLite store and the
 *       ACTUAL ScheduledTaskRunner: one stable Run identity allocated before execution, execution capture
 *       separated from schedule planning and completion persistence, and truthful typed persistence-
 *       uncertainty with operation-only, bounded same-payload reconciliation. It never rewrites the
 *       SQLite repository as a memory fake.
 * Why: Spec 8120-124 and 8241-8253, 8330 require stable run identity, idempotent completion, separate
 *       execution/planning/persistence phases, and typed uncertain outcomes rather than swallowing
 *       persistence faults as ordinary failed execution. Reconciliation is an explicit bounded call of
 *       the existing exact-payload CompleteRunAsync; a void success means only acknowledged, never a
 *       claim that this call wrote or performed no write (real SQL mutation/no-write evidence lives in
 *       these real SQLite tests).
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

    // A fixed past point: leases derived from it (15-minute originals) are naturally expired under the
    // real acquisition clock, so newer owners can legitimately acquire through the real API.
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Lost_ack_after_durable_commit_reconiles_to_newer_lease_and_schedule_without_writes()
    {
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var realStore = new AutomationRepository(database);
        var task = DueDefinition(Now);
        await realStore.UpsertAsync(task, token).WaitAsync(Bound, token);

        var model = new FakeModelClient("ok result");
        var controlled = new ControlledCompletionRepository(realStore) { ThrowAfterCommit = new AckLossException() };
        var runner = new ScheduledTaskRunner(controlled, model, new ScheduledTaskScheduleCalculator());

        var ex = await Assert.ThrowsAsync<ScheduledTaskCompletionUncertaintyException>(
            () => runner.RunOneAsync(task, Now, token)).WaitAsync(Bound, token);

        // Execution ran exactly once; nothing was relabelled Failed/Cancelled.
        Assert.Equal(1, model.CompleteCount);
        var draft = ex.Draft;
        Assert.Equal(AutomationRunStatus.Succeeded, draft.Run.Status);
        Assert.Equal(task.Id, draft.Run.AutomationId);
        Assert.Equal("ok result", draft.Run.Result);
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

        // A NEWER legitimate owner now acquires a fresh lease through the REAL lease API (the original
        // lease was released) and updates to a DISTINCT next schedule using the real definition API.
        Assert.True(await reopened.TryAcquireLeaseAsync(task.Id, "lease-newer", Now.AddDays(1), token).WaitAsync(Bound, token));
        var distinctSchedule = Now.AddDays(7);
        await reopened.UpsertAsync(task with { NextRunAt = distinctSchedule, UpdatedAt = Now }, token).WaitAsync(Bound, token);

        // Snapshot complete Run/definition rows before reconciliation.
        var definitionSnapshot = await SerializeDefinitionRowAsync(task.Id);
        var runSnapshot = await SerializeRunRowsAsync(task.Id);

        // Reconciliation uses ONLY the exact existing operation and must not write anything under these
        // write-blocking triggers (any real SQL mutation would abort).
        await InstallWriteBlockingTriggersAsync();
        var reconciliation = await new ScheduledTaskRunner(reopened, model, new ScheduledTaskScheduleCalculator())
            .ReconcileCompletionAsync(draft, token).WaitAsync(Bound, token);

        // Truthful outcome: the exact completion operation was ACKNOWLEDGED. The enum name alone is not
        // evidence; the unchanged snapshots prove no SQL writes and no new Run/lease/execution.
        Assert.Equal(ScheduledTaskCompletionReconciliationKind.Acknowledged, reconciliation.Kind);
        Assert.Null(reconciliation.Cause);
        Assert.Equal(definitionSnapshot, await SerializeDefinitionRowAsync(task.Id)); // newer token/schedule unchanged
        Assert.Equal(runSnapshot, await SerializeRunRowsAsync(task.Id));               // original payload unchanged
        Assert.Equal(1, model.CompleteCount);                                         // never re-executed
    }

    [Fact]
    public async Task Forced_completion_write_failure_rolls_back_then_reconcile_writes_once_without_replay()
    {
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var realStore = new AutomationRepository(database);
        var task = DueDefinition(Now);
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
            () => runner.RunOneAsync(task, Now, token)).WaitAsync(Bound, token);

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

        // Remove the controlled trigger, then reconcile the EXACT payload. The real row now exists, which
        // is the SQLite-side evidence of the write; the reconciliation outcome is Acknowledged.
        await DropDefinitionUpdateAbortTriggerAsync();
        var reconciliation = await new ScheduledTaskRunner(reopened, model, new ScheduledTaskScheduleCalculator())
            .ReconcileCompletionAsync(draft, token).WaitAsync(Bound, token);
        Assert.Equal(ScheduledTaskCompletionReconciliationKind.Acknowledged, reconciliation.Kind);
        Assert.Null(reconciliation.Cause);
        Assert.Equal(1, model.CompleteCount);

        var wrote = await reopened.GetRunsAsync(task.Id, 10, token).WaitAsync(Bound, token);
        var row = Assert.Single(wrote);
        Assert.Equal(AutomationRunStatus.Succeeded, row.Status);
        Assert.Equal(draft.Run.Id, row.Id);
        Assert.Equal("ok result", row.Result);
        Assert.Equal(draft.Run.LeaseToken, row.LeaseToken);
    }

    [Fact]
    public async Task Forced_completion_write_failure_cannot_overwrite_a_newer_real_lease()
    {
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var realStore = new AutomationRepository(database);
        var task = DueDefinition(Now);
        await realStore.UpsertAsync(task, token).WaitAsync(Bound, token);

        await InstallDefinitionUpdateAbortTriggerAsync();
        var model = new FakeModelClient("ok result");
        var runner = new ScheduledTaskRunner(realStore, model, new ScheduledTaskScheduleCalculator());
        var ex = await Assert.ThrowsAsync<ScheduledTaskCompletionUncertaintyException>(
            () => runner.RunOneAsync(task, Now, token)).WaitAsync(Bound, token);

        // The failed completion rolled back: no Run row, and the definition still holds the runner's
        // original 15-minute lease whose expiry (Now+15m) is naturally in the past under the real clock.
        Assert.Empty(await realStore.GetRunsAsync(task.Id, 10, token).WaitAsync(Bound, token));

        // Drop the forced-failure trigger, then a NEWER legitimate owner acquires the definition through
        // the REAL lease API (the naturally-expired original lease does not fence it) and updates a
        // distinct schedule. No direct SQL forging of a token is used.
        await DropDefinitionUpdateAbortTriggerAsync();
        Assert.True(await realStore.TryAcquireLeaseAsync(task.Id, "lease-newer", Now.AddDays(1), token).WaitAsync(Bound, token));
        var distinctSchedule = Now.AddDays(14);
        await realStore.UpsertAsync(task with { NextRunAt = distinctSchedule, UpdatedAt = Now }, token).WaitAsync(Bound, token);
        var definitionBefore = await SerializeDefinitionRowAsync(task.Id);

        var reopened = new AutomationRepository(NewDatabase());
        var reconciliation = await new ScheduledTaskRunner(reopened, model, new ScheduledTaskScheduleCalculator())
            .ReconcileCompletionAsync(ex.Draft, token).WaitAsync(Bound, token);

        // Honest answer under this void contract: Unknown with the exact preserved guard exception, and
        // the actual database proves the newer owner/schedule was never overwritten (rollback leaves no
        // run row and the definition untouched).
        Assert.Equal(ScheduledTaskCompletionReconciliationKind.Unknown, reconciliation.Kind);
        Assert.NotNull(reconciliation.Cause);
        Assert.IsType<InvalidOperationException>(reconciliation.Cause);
        Assert.Equal(definitionBefore, await SerializeDefinitionRowAsync(task.Id));
        Assert.Empty(await reopened.GetRunsAsync(task.Id, 10, token).WaitAsync(Bound, token));
        Assert.Equal(1, model.CompleteCount); // never re-executed
    }

    [Fact]
    public async Task Execution_failure_and_completion_error_retain_both_causes_with_one_stable_id()
    {
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var realStore = new AutomationRepository(database);
        var task = DueDefinition(Now);
        await realStore.UpsertAsync(task, token).WaitAsync(Bound, token);

        await InstallDefinitionUpdateAbortTriggerAsync();
        var model = new FakeModelClient("unused") { Failure = new IOException("Provider unavailable.") };
        var runner = new ScheduledTaskRunner(realStore, model, new ScheduledTaskScheduleCalculator());
        var ex = await Assert.ThrowsAsync<ScheduledTaskCompletionUncertaintyException>(
            () => runner.RunOneAsync(task, Now, token)).WaitAsync(Bound, token);

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
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var realStore = new AutomationRepository(database);
        var task = DueDefinition(Now);
        await realStore.UpsertAsync(task, token).WaitAsync(Bound, token);

        var cts = new CancellationTokenSource();
        var enteredPersistence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holdGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controlled = new ControlledCompletionRepository(realStore)
        {
            // Wait for ENTRY to the actual delegating completion call, then hold it.
            BeforeCommitGate = () =>
            {
                enteredPersistence.TrySetResult();
                return holdGate.Task;
            },
            ThrowAfterCommit = new AckLossException(),
            UseNoneForCommit = true
        };
        var model = new FakeModelClient("ok result");
        var runner = new ScheduledTaskRunner(controlled, model, new ScheduledTaskScheduleCalculator());

        var runTask = runner.RunOneAsync(task, Now, cts.Token);
        ScheduledTaskCompletionUncertaintyException? captured = null;
        try
        {
            await enteredPersistence.Task.WaitAsync(Bound, token); // entry to the delegating completion call
            // A held persistence gate prevents an apparent completed/acknowledged runner result before release.
            Assert.False(runTask.IsCompleted);
            cts.Cancel(); // cancel while that original is held
        }
        finally
        {
            // Always release every gate and join the exact original so an earlier assertion cannot strand it.
            holdGate.TrySetResult();
            try
            {
                captured = await Assert.ThrowsAsync<ScheduledTaskCompletionUncertaintyException>(() => runTask).WaitAsync(Bound, token);
            }
            catch
            {
                // Preserve the original try-block failure (if any) as the surface failure.
            }
        }

        Assert.NotNull(captured);
        Assert.Equal(1, model.CompleteCount);
        // It never becomes a Cancelled execution; the exact success payload survives.
        Assert.Equal(AutomationRunStatus.Succeeded, captured!.Draft.Run.Status);
        Assert.Equal("ok result", captured.Draft.Run.Result);
        Assert.IsType<AckLossException>(captured.PersistenceCause);

        var reopened = new AutomationRepository(NewDatabase());
        var runs = await reopened.GetRunsAsync(task.Id, 10, token).WaitAsync(Bound, token);
        var row = Assert.Single(runs);
        Assert.Equal(AutomationRunStatus.Succeeded, row.Status);
        Assert.Equal(captured.Draft.Run.Id, row.Id);

        // Reconciliation never executes the model again; the exact already-committed payload is acknowledged.
        var reconciliation = await new ScheduledTaskRunner(reopened, model, new ScheduledTaskScheduleCalculator())
            .ReconcileCompletionAsync(captured.Draft, token).WaitAsync(Bound, token);
        Assert.Equal(ScheduledTaskCompletionReconciliationKind.Acknowledged, reconciliation.Kind);
        Assert.Null(reconciliation.Cause);
        Assert.Equal(1, model.CompleteCount);
    }

    [Fact]
    public async Task RunDue_retains_inspectable_uncertainties_and_never_relaunches()
    {
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var realStore = new AutomationRepository(database);

        var taskA = DueDefinition(Now);
        var taskB = DueDefinition(Now);
        await realStore.UpsertAsync(taskA, token).WaitAsync(Bound, token);
        await realStore.UpsertAsync(taskB, token).WaitAsync(Bound, token);

        var model = new FakeModelClient("ok result");
        var controlled = new ControlledCompletionRepository(realStore)
        {
            ThrowAfterCommit = new AckLossException(),
            ShouldThrowAfterCommit = run => run.AutomationId == taskB.Id
        };
        var runner = new ScheduledTaskRunner(controlled, model, new ScheduledTaskScheduleCalculator());

        var result = await runner.RunDueAsync(Now, token).WaitAsync(Bound, token);
        Assert.Equal(2, result.Due);
        Assert.Equal(2, result.Started);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(1, result.Uncertain);

        // The batch outcome retains the exact uncertain draft/cause for the affected Run, with truthful
        // counters and no hidden relaunch.
        var uncertainty = Assert.Single(result.Uncertainties);
        Assert.Equal(taskB.Id, uncertainty.Draft.Run.AutomationId);
        Assert.Equal(AutomationRunStatus.Succeeded, uncertainty.Draft.Run.Status);
        Assert.Equal("ok result", uncertainty.Draft.Run.Result);
        Assert.IsType<AckLossException>(uncertainty.PersistenceCause);

        // Legacy five-parameter constructor and five-out Deconstruct remain intact (not merely optional
        // argument source compatibility).
        var positional = new ScheduledTaskBatchResult(2, 2, 1, 0, 0);
        var (due, started, succeeded, failed, skipped) = positional;
        Assert.Equal((2, 2, 1, 0, 0), (due, started, succeeded, failed, skipped));
        Assert.Equal(0, positional.Uncertain);

        Assert.Equal(2, model.CompleteCount); // exactly one execution each, never relaunched

        var reopened = new AutomationRepository(NewDatabase());
        var runsA = await reopened.GetRunsAsync(taskA.Id, 10, token).WaitAsync(Bound, token);
        var runsB = await reopened.GetRunsAsync(taskB.Id, 10, token).WaitAsync(Bound, token);
        Assert.Equal(AutomationRunStatus.Succeeded, Assert.Single(runsA).Status);
        Assert.Equal(AutomationRunStatus.Succeeded, Assert.Single(runsB).Status); // committed, ack lost only

        // A second immediate due sweep must not relaunch the uncertain completion.
        var second = await new ScheduledTaskRunner(reopened, model, new ScheduledTaskScheduleCalculator())
            .RunDueAsync(Now, token).WaitAsync(Bound, token);
        Assert.Equal(0, second.Started);
        Assert.Equal(2, model.CompleteCount);
    }

    [Fact]
    public async Task Post_commit_invalidoperationexception_ack_loss_stays_uncertain_then_exact_replay_acks()
    {
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var realStore = new AutomationRepository(database);
        var task = DueDefinition(Now);
        await realStore.UpsertAsync(task, token).WaitAsync(Bound, token);

        var model = new FakeModelClient("ok result");
        var controlled = new ControlledCompletionRepository(realStore)
        {
            ThrowAfterCommit = new InvalidOperationException("Simulated post-commit ACK loss.")
        };
        var runner = new ScheduledTaskRunner(controlled, model, new ScheduledTaskScheduleCalculator());

        var ex = await Assert.ThrowsAsync<ScheduledTaskCompletionUncertaintyException>(
            () => runner.RunOneAsync(task, Now, token)).WaitAsync(Bound, token);

        // A generic post-commit InvalidOperationException does not prove refusal/rollback: the result is
        // uncertain, the ORIGINAL cause is retained, and exactly one real row exists.
        Assert.Equal(AutomationRunStatus.Succeeded, ex.Draft.Run.Status);
        Assert.IsType<InvalidOperationException>(ex.PersistenceCause);
        Assert.Contains("post-commit", ex.PersistenceCause.Message);

        var reopened = new AutomationRepository(NewDatabase());
        var runs = await reopened.GetRunsAsync(task.Id, 10, token).WaitAsync(Bound, token);
        var row = Assert.Single(runs);
        Assert.Equal(AutomationRunStatus.Succeeded, row.Status);
        Assert.Equal("ok result", row.Result);

        // A later exact replay acknowledges without re-execution (idempotent, identical row).
        var reconciliation = await new ScheduledTaskRunner(reopened, model, new ScheduledTaskScheduleCalculator())
            .ReconcileCompletionAsync(ex.Draft, token).WaitAsync(Bound, token);
        Assert.Equal(ScheduledTaskCompletionReconciliationKind.Acknowledged, reconciliation.Kind);
        Assert.Null(reconciliation.Cause);
        Assert.Equal(1, model.CompleteCount);
        Assert.Single(await reopened.GetRunsAsync(task.Id, 10, token).WaitAsync(Bound, token));
    }

    [Fact]
    public async Task Reconciliation_never_enumerates_history()
    {
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var realStore = new AutomationRepository(database);
        var task = DueDefinition(Now);
        await realStore.UpsertAsync(task, token).WaitAsync(Bound, token);

        var model = new FakeModelClient("ok result");
        var controlled = new ControlledCompletionRepository(realStore) { ThrowAfterCommit = new AckLossException() };
        var runner = new ScheduledTaskRunner(controlled, model, new ScheduledTaskScheduleCalculator());
        var ex = await Assert.ThrowsAsync<ScheduledTaskCompletionUncertaintyException>(
            () => runner.RunOneAsync(task, Now, token)).WaitAsync(Bound, token);

        // A synthetic wrapper refuses/counts history enumeration, but persistence still delegates to the
        // real SQLite store. Reconciliation must not enumerate history at all.
        var noHistory = new NoHistoryEnumerationRepository(realStore);
        var reconciliation = await new ScheduledTaskRunner(noHistory, model, new ScheduledTaskScheduleCalculator())
            .ReconcileCompletionAsync(ex.Draft, token).WaitAsync(Bound, token);
        Assert.Equal(ScheduledTaskCompletionReconciliationKind.Acknowledged, reconciliation.Kind);
        Assert.Null(reconciliation.Cause);
        Assert.Equal(0, noHistory.GetRunsCalls);
        Assert.Single(await realStore.GetRunsAsync(task.Id, 10, token).WaitAsync(Bound, token));
    }

    [Fact]
    public async Task Schedule_planning_fault_after_model_success_retains_executed_run_without_fake_write()
    {
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var realStore = new AutomationRepository(database);
        var task = InvalidScheduleDefinition(Now);
        await realStore.UpsertAsync(task, token).WaitAsync(Bound, token);

        var model = new FakeModelClient("ok result");
        var runner = new ScheduledTaskRunner(realStore, model, new ScheduledTaskScheduleCalculator());

        var ex = await Assert.ThrowsAsync<ScheduledTaskSchedulePlanningFailureException>(
            () => runner.RunOneAsync(task, Now, token)).WaitAsync(Bound, token);

        // Model executed exactly once; planning evaluated once; original Succeeded result/RunID retained.
        Assert.Equal(1, model.CompleteCount);
        Assert.Equal(AutomationRunStatus.Succeeded, ex.Run.Status);
        Assert.Equal("ok result", ex.Run.Result);
        Assert.Null(ex.ExecutionFailure);
        Assert.IsType<FormatException>(ex.PlanningCause);

        // No completion write with a fake null schedule and no fabricated Failed row.
        Assert.Empty(await realStore.GetRunsAsync(task.Id, 10, token).WaitAsync(Bound, token));

        // The definition lease is still held by the runner (completion never attempted); schedule unchanged.
        var def = await ReadAutomationRowAsync(task.Id);
        Assert.NotNull(def.LeaseToken);
    }

    [Fact]
    public async Task Schedule_planning_fault_preserves_true_execution_failure_cause()
    {
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var realStore = new AutomationRepository(database);
        var task = InvalidScheduleDefinition(Now);
        await realStore.UpsertAsync(task, token).WaitAsync(Bound, token);

        var model = new FakeModelClient("unused") { Failure = new IOException("Provider unavailable.") };
        var runner = new ScheduledTaskRunner(realStore, model, new ScheduledTaskScheduleCalculator());

        var ex = await Assert.ThrowsAsync<ScheduledTaskSchedulePlanningFailureException>(
            () => runner.RunOneAsync(task, Now, token)).WaitAsync(Bound, token);

        // Bounded execution retries happened; the failure path was never relabelled.
        Assert.Equal(3, model.CompleteCount);
        Assert.Equal(AutomationRunStatus.Failed, ex.Run.Status);
        Assert.NotNull(ex.ExecutionFailure);
        Assert.Contains("Provider unavailable", ex.ExecutionFailure!.Message);
        Assert.IsType<FormatException>(ex.PlanningCause);
        Assert.NotSame(ex.ExecutionFailure, ex.PlanningCause);
        Assert.Empty(await realStore.GetRunsAsync(task.Id, 10, token).WaitAsync(Bound, token));
    }

    // ---- helpers ----

    private static AutomationDefinition DueDefinition(DateTimeOffset now)
    {
        var schedule = ScheduledTaskScheduleComposer.Compose(AutomationScheduleKind.Daily,
            new ScheduledTaskScheduleDraft(now.AddHours(1), new TimeOnly(8, 0), DayOfWeek.Monday, 1, 60));
        return new AutomationDefinition(Guid.NewGuid(), "Scheduler completion fixture", HavenMode.Chat,
            "execute scheduled instruction", AutomationScheduleKind.Daily, schedule, now, null, true, now, now);
    }

    /// <summary>An otherwise-executable definition whose schedule JSON is invalid, so schedule planning
    /// faults AFTER a successful model execution (during the separate planning phase).</summary>
    private static AutomationDefinition InvalidScheduleDefinition(DateTimeOffset now) =>
        new(Guid.NewGuid(), "Invalid schedule fixture", HavenMode.Chat,
            "execute scheduled instruction", AutomationScheduleKind.Daily, "{invalid",
            now, null, true, now, now);

    private sealed class FakeModelClient(string response) : IOllamaClient
    {
        public Exception? Failure { get; init; }
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

    /// <summary>Synthetic wrapper that counts and refuses history enumeration. Reconciliation must not
    /// enumerate history at all, so its GetRunsCalls stays zero while persistence delegates to the real
    /// SQLite store.</summary>
    private sealed class NoHistoryEnumerationRepository(IAutomationRepository inner) : IAutomationRepository
    {
        public int GetRunsCalls { get; private set; }

        public Task<IReadOnlyList<AutomationRun>> GetRunsAsync(Guid automationId, int limit, CancellationToken cancellationToken)
        {
            GetRunsCalls++;
            throw new InvalidOperationException("History enumeration is forbidden during reconciliation.");
        }

        public Task CompleteRunAsync(AutomationRun run, DateTimeOffset? nextRunAt, CancellationToken cancellationToken) =>
            inner.CompleteRunAsync(run, nextRunAt, cancellationToken);
        public Task<IReadOnlyList<AutomationDefinition>> GetAllAsync(CancellationToken cancellationToken) => inner.GetAllAsync(cancellationToken);
        public Task<IReadOnlyList<AutomationDefinition>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken) => inner.GetDueAsync(now, cancellationToken);
        public Task UpsertAsync(AutomationDefinition automation, CancellationToken cancellationToken) => inner.UpsertAsync(automation, cancellationToken);
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => inner.DeleteAsync(id, cancellationToken);
        public Task<bool> TryAcquireLeaseAsync(Guid automationId, string leaseToken, DateTimeOffset leaseUntil, CancellationToken cancellationToken) =>
            inner.TryAcquireLeaseAsync(automationId, leaseToken, leaseUntil, cancellationToken);
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

    private async Task<string> SerializeRunRowsAsync(Guid automationId)
    {
        await using var connection = await NewDatabase().OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM automation_runs WHERE automation_id=$automationId ORDER BY id;";
        command.Parameters.AddWithValue("$automationId", automationId.ToString());
        await using var reader = await command.ExecuteReaderAsync(token);
        var sb = new System.Text.StringBuilder();
        while (await reader.ReadAsync(token))
        {
            for (var i = 0; i < reader.FieldCount; i++)
            {
                sb.Append(reader.GetName(i)).Append('=');
                sb.Append(reader.IsDBNull(i) ? "<NULL>" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture));
                sb.Append('|');
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", null);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
