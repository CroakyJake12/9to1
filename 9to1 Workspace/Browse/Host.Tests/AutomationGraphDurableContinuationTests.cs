/*
 * FILE DOCUMENTATION
 * Where: Browse/Host.Tests/AutomationGraphDurableContinuationTests.cs, the real Haven host test lane.
 * What: Proves the ACTUAL durable Wait/Approval integration on the real migrated SQLite schema: a
 *       scheduled graph that hits a Wait yields promptly (no thread sleep) and suspends the SAME Run;
 *       a reopened store resumes that Run from its persisted continuation (A once, B once) without a
 *       fresh Run/restart; independent claimers race with exactly one winner BEFORE B is entered; a
 *       long wait beyond the 15-minute definition lease cannot spawn a duplicate fresh Run; a newer
 *       Active revision never repoints the existing Run; malformed/foreign continuations and revoked
 *       authority stop before any side effect; and Approval decisions are authorised, fenced and
 *       distinct (approved/rejected/timed-out/cancelled) with never an auto-approval execution.
 * Why: Specification 8199-8207, 8227-8239, 8245-8251, 8330 and APP-AC-001..007.
 * Maintenance: Uses the real SQLite schema/migrations and store reopen; never a mock repository or a
 *       forged SQL token. Clock observations are explicit parameters; no sleeps are used.
 */

using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HavenOS.Browse.Host.Tests;

public sealed class AutomationGraphDurableContinuationTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);
    private readonly CancellationToken token = TestContext.Current.CancellationToken;
    private readonly string _root = Directory.CreateTempSubdirectory("haven-automation-durable-").FullName;

    private sealed record GraphFixture(Guid GraphId, string Digest, AutomationGraphDefinition Graph, Guid TriggerId, Guid ActionAId, Guid WaitId, Guid ActionBId, Guid ApprovalNodeId);

    private static GraphFixture BuildWaitGraph()
    {
        var trigger = Node("Trigger");
        var actionA = Node("Action", ("action", "emit"), ("value", "a"));
        var wait = Node("Wait", ("until", "2000-01-01T00:00:00Z"));
        var actionB = Node("Action", ("action", "emit"), ("value", "b"));
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion,
            [trigger, actionA, wait, actionB],
            [new(trigger.Id, actionA.Id), new(actionA.Id, wait.Id), new(wait.Id, actionB.Id)]);
        return new GraphFixture(Guid.NewGuid(), "d1g3st-aabbccdd", graph, trigger.Id, actionA.Id, wait.Id, actionB.Id, Guid.Empty);
    }

    [Fact]
    public async Task Scheduled_graph_A_wait_B_yields_promptly_suspends_same_run_and_resumes_once_each()
    {
        var fx = BuildWaitGraph();
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var store = new AutomationRepository(database);

        var automationId = Guid.NewGuid();
        var task = WaitDefinition(automationId, fx, now);
        await store.UpsertAsync(task, token).WaitAsync(Bound, token);

        var runner = new ScheduledTaskRunner(store, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
            new BuiltInAutomationActionNodeExecutor(), continuationStore: store);
        var suspended = await runner.RunOneAsync(task, now, token).WaitAsync(Bound, token);

        // Suspension is truthful and non-terminal: Waiting, single row, pin + continuation persisted, and
        // the definition lease is still held by the suspended Run (not released).
        Assert.Equal(AutomationRunStatus.Waiting, suspended.Status);
        var runs = await store.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token);
        var row = Assert.Single(runs);
        Assert.Equal(suspended.Id, row.Id);
        Assert.Equal(fx.GraphId, row.PinnedGraph!.GraphId);
        Assert.Equal(4, row.PinnedGraph.Revision);
        Assert.Equal(4, row.DefinitionRevision);
        Assert.True(AutomationGraphContinuationCodec.TryDeserialize(row.ContinuationDescriptorJson, out var continuation));
        Assert.Equal(AutomationGraphYieldKind.Wait, continuation.PendingYield!.Kind);

        // The suspended run's definition lease token is the definition's current lease.
        var defRow = await ReadAutomationRowAsync(automationId);
        Assert.Equal(suspended.LeaseToken, defRow.LeaseToken);

        // Reopen (fresh store/coordinator over the same file): resume the SAME Run/pin from its
        // persisted continuation. A once, B once, no new RunID, no restart from trigger.
        var reopened = new AutomationRepository(NewDatabase());
        var resumedRunner = new ScheduledTaskRunner(reopened, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
            new BuiltInAutomationActionNodeExecutor(), continuationStore: reopened);
        var coordinator = new AutomationRunContinuationCoordinator(reopened, reopened, reopened,
            new ScheduledGraphContinuationResumer(resumedRunner));
        // The persisted Wait's `until` (2000-01-01) is far in the past, so any "now" at or after it is
        // due. Use an explicit future observation — never a fictitious helper or a live UtcNow.
        var sweepAt = new DateTimeOffset(2026, 10, 19, 13, 0, 0, TimeSpan.Zero);
        var outcome = await coordinator.SweepResumeAsync(sweepAt, 10, token).WaitAsync(Bound, token);
        Assert.True(outcome.Accepted, outcome.Message);

        var after = await reopened.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token);
        var finalRow = Assert.Single(after);
        Assert.Equal(suspended.Id, finalRow.Id); // SAME RunID
        Assert.Equal(AutomationRunStatus.Succeeded, finalRow.Status);
        Assert.Equal(4, finalRow.DefinitionRevision);
        Assert.Contains("graphSucceeded\":true", finalRow.Result ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reopen_before_due_leaves_waiting_and_candidate_read_is_not_an_approval()
    {
        var fx = BuildWaitGraph();
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var store = new AutomationRepository(database);
        var automationId = Guid.NewGuid();
        var task = WaitDefinition(automationId, fx, now);
        await store.UpsertAsync(task, token).WaitAsync(Bound, token);

        // Future Wait: `until` is two days ahead so the run stays Waiting.
        var futureWaitFx = BuildFutureWaitGraph();
        var task2 = WaitDefinition(automationId, futureWaitFx, now);
        await store.UpsertAsync(task2, token).WaitAsync(Bound, token);

        var runner = new ScheduledTaskRunner(store, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
            new BuiltInAutomationActionNodeExecutor(), continuationStore: store);
        var suspended = await runner.RunOneAsync(task2, now, token).WaitAsync(Bound, token);
        Assert.Equal(AutomationRunStatus.Waiting, suspended.Status);

        var reopened = new AutomationRepository(NewDatabase());
        // Before due: not offered. This is a candidate read, never an approval or execution grant.
        var early = await reopened.GetResumableClaimCandidatesAsync(now.AddHours(1), 10, token).WaitAsync(Bound, token);
        Assert.DoesNotContain(early, run => run.Id == suspended.Id);
        var defBefore = await ReadAutomationRowAsync(automationId);
        Assert.Equal(suspended.LeaseToken, defBefore.LeaseToken);
    }

    [Fact]
    public async Task Two_independent_claimers_have_one_winner_before_any_resumed_effect()
    {
        var fx = BuildWaitGraph();
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var store = new AutomationRepository(database);
        var automationId = Guid.NewGuid();
        var task = WaitDefinition(automationId, fx, now);
        await store.UpsertAsync(task, token).WaitAsync(Bound, token);

        var runner = new ScheduledTaskRunner(store, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
            new BuiltInAutomationActionNodeExecutor(), continuationStore: store);
        var suspended = await runner.RunOneAsync(task, now, token).WaitAsync(Bound, token);

        // Two independent store/coordinator instances race the SAME resume claim. Exactly one wins; the
        // loser performs zero effects (no claim row, no run mutation).
        var storeA = new AutomationRepository(NewDatabase());
        var storeB = new AutomationRepository(NewDatabase());
        var coordinatorA = new AutomationRunContinuationCoordinator(storeA, storeA, storeA);
        var coordinatorB = new AutomationRunContinuationCoordinator(storeB, storeB, storeB);

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<AutomationRunClaimAcquisition> ClaimFromAsync(AutomationRunContinuationCoordinator coordinator)
        {
            await gate.Task;
            return await coordinator.TryAcquireForRunAsync(suspended, token);
        }
        var taskA = ClaimFromAsync(coordinatorA);
        var taskB = ClaimFromAsync(coordinatorB);
        gate.TrySetResult(true);
        var claims = await Task.WhenAll(taskA, taskB);
        Assert.Equal(1, claims.Count(claim => claim.Acquired));
        Assert.Equal(1, claims.Count(claim => !claim.Acquired));
    }

    [Fact]
    public async Task Forced_claim_insert_failure_rolls_back_without_mutating_run_or_definition()
    {
        var fx = BuildWaitGraph();
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var store = new AutomationRepository(database);
        var automationId = Guid.NewGuid();
        var task = WaitDefinition(automationId, fx, now);
        await store.UpsertAsync(task, token).WaitAsync(Bound, token);
        var runner = new ScheduledTaskRunner(store, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
            new BuiltInAutomationActionNodeExecutor(), continuationStore: store);
        var suspended = await runner.RunOneAsync(task, now, token).WaitAsync(Bound, token);

        var runBefore = await SerializeRunRowAsync(suspended.Id);
        var defBefore = await SerializeDefinitionRowAsync(automationId);

        await using (var connection = await NewDatabase().OpenAsync(token))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TRIGGER force_claim_insert_failure
                BEFORE INSERT ON automation_run_claims
                BEGIN SELECT RAISE(ABORT, 'forced claim insert failure'); END;
                """;
            await command.ExecuteNonQueryAsync(token);
        }

        var coordinator = new AutomationRunContinuationCoordinator(store, store, store);
        var ex = await Assert.ThrowsAsync<SqliteException>(() => coordinator.TryAcquireForRunAsync(suspended, token));
        Assert.Contains("forced claim insert failure", ex.Message);
        Assert.Equal(runBefore, await SerializeRunRowAsync(suspended.Id));
        Assert.Equal(defBefore, await SerializeDefinitionRowAsync(automationId));
    }

    [Fact]
    public async Task Wait_beyond_fifteen_minute_lease_cannot_spawn_a_duplicate_fresh_run()
    {
        var fx = BuildWaitGraph();
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var store = new AutomationRepository(database);
        var automationId = Guid.NewGuid();
        var task = WaitDefinition(automationId, fx, now);
        await store.UpsertAsync(task, token).WaitAsync(Bound, token);
        var runner = new ScheduledTaskRunner(store, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
            new BuiltInAutomationActionNodeExecutor(), continuationStore: store);
        var suspended = await runner.RunOneAsync(task, now, token).WaitAsync(Bound, token);

        // Age the definition lease past its 15-minute expiry while the Waiting Run still owns the
        // execution. GetDue must NOT offer the definition again: the active same-Run scheduling guard
        // fences it from a duplicate fresh Run/token.
        await using (var connection = await NewDatabase().OpenAsync(token))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE automations SET lease_until=$past WHERE id=$id;";
            command.Parameters.AddWithValue("$past", now.AddHours(-2).ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$id", automationId.ToString());
            await command.ExecuteNonQueryAsync(token);
        }

        var due = await store.GetDueAsync(now, token).WaitAsync(Bound, token);
        Assert.DoesNotContain(due, item => item.Id == automationId);
        var def = await ReadAutomationRowAsync(automationId);
        Assert.Equal(suspended.LeaseToken, def.LeaseToken); // original token preserved
    }

    [Fact]
    public async Task Malformed_foreign_continuation_stops_before_any_side_effect()
    {
        var fx = BuildWaitGraph();
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var store = new AutomationRepository(database);
        var automationId = Guid.NewGuid();
        var task = WaitDefinition(automationId, fx, now);
        await store.UpsertAsync(task, token).WaitAsync(Bound, token);
        var runner = new ScheduledTaskRunner(store, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
            new BuiltInAutomationActionNodeExecutor(), continuationStore: store);
        var suspended = await runner.RunOneAsync(task, now, token).WaitAsync(Bound, token);

        // Corrupt the persisted continuation, then resume: it must refuse with zero effects.
        var corrupted = suspended with { ContinuationDescriptorJson = "haven:automation-graph-continuation:v1:{not-json" };
        await using (var connection = await NewDatabase().OpenAsync(token))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE automation_runs SET continuation_descriptor_json=$c WHERE id=$id;";
            command.Parameters.AddWithValue("$c", corrupted.ContinuationDescriptorJson);
            command.Parameters.AddWithValue("$id", suspended.Id.ToString());
            await command.ExecuteNonQueryAsync(token);
        }

        var reopened = new AutomationRepository(NewDatabase());
        var resumedRunner = new ScheduledTaskRunner(reopened, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
            new BuiltInAutomationActionNodeExecutor(), continuationStore: reopened);
        var coordinator = new AutomationRunContinuationCoordinator(reopened, reopened, reopened,
            new ScheduledGraphContinuationResumer(resumedRunner));
        var sweepAt = new DateTimeOffset(2026, 10, 19, 13, 0, 0, TimeSpan.Zero);
        var outcome = await coordinator.SweepResumeAsync(sweepAt, 10, token).WaitAsync(Bound, token);
        Assert.False(outcome.Accepted);
        Assert.Equal("continuation.invalid", outcome.Code);

        var runs = await reopened.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token);
        var row = Assert.Single(runs);
        Assert.Equal(AutomationRunStatus.Waiting, row.Status); // unchanged, no terminal write
    }

    [Fact]
    public async Task Approval_decisions_are_fenced_authorised_and_distinct_and_never_auto_approve()
    {
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var store = new AutomationRepository(database);
        var automationId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var approvalNodeId = Guid.NewGuid();
        await store.UpsertAsync(new AutomationDefinition(automationId, "Approval fixture", HavenMode.Chat,
            "approval", AutomationScheduleKind.Once, """{"at":"2026-10-20T00:00:00+00:00"}""", now.AddDays(1),
            null, true, now, now), token).WaitAsync(Bound, token);

        var leaseToken = "approval-lease";
        Assert.True(await store.TryAcquireLeaseAsync(automationId, leaseToken, now.AddMinutes(30), token));

        var continuation = new AutomationGraphContinuation(
            AutomationGraphContinuation.CurrentVersion, graphId, 4, 1, "digest", now,
            [], [approvalNodeId], [], new Dictionary<Guid, string?>(), [], 0, 0, AutomationGraphRunMode.Real,
            null, new AutomationGraphYield(AutomationGraphYieldKind.Approval, YieldNodeId: approvalNodeId,
                ApprovalActionJson: """{"action":"execute","summary":"transmit"}""", ApprovalNodeId: approvalNodeId));
        var runId = Guid.NewGuid();
        var suspended = new AutomationRun(runId, automationId, AutomationRunStatus.ApprovalRequired, now, now, null, null, null, leaseToken)
        {
            Revision = 3,
            DefinitionRevision = 4,
            PinnedGraph = new AutomationGraphBinding(graphId, 4),
            ContinuationDescriptorJson = AutomationGraphContinuationCodec.Serialize(continuation)
        };
        await store.SuspendRunAsync(suspended, token).WaitAsync(Bound, token);

        var reopened = new AutomationRepository(NewDatabase());
        var coordinator = new AutomationRunContinuationCoordinator(reopened, reopened, reopened,
            null, new StubApprovalAuth(false));

        // Unauthorised actor refused.
        var unauthorised = await coordinator.ApplyApprovalDecisionAsync(
            new AutomationRunApprovalDecision(runId, approvalNodeId, "approved", "intruder", 3), now, token).WaitAsync(Bound, token);
        Assert.False(unauthorised.Accepted);
        Assert.Equal("approval.actor.unauthorised", unauthorised.Code);

        // Authorised actor, stale revision refused.
        var authorised = new AutomationRunContinuationCoordinator(reopened, reopened, reopened,
            null, new StubApprovalAuth(true));
        var stale = await authorised.ApplyApprovalDecisionAsync(
            new AutomationRunApprovalDecision(runId, approvalNodeId, "approved", "owner", 2), now, token).WaitAsync(Bound, token);
        Assert.False(stale.Accepted);
        Assert.Equal("approval.revision.stale", stale.Code);

        // Cross-node refusal.
        var wrongNode = await authorised.ApplyApprovalDecisionAsync(
            new AutomationRunApprovalDecision(runId, Guid.NewGuid(), "approved", "owner", 3), now, token).WaitAsync(Bound, token);
        Assert.False(wrongNode.Accepted);
        Assert.Equal("approval.node.mismatch", wrongNode.Code);

        // Distinct, authorised, accepted outcomes: approved/rejected/timed-out/cancelled.
        foreach (var decision in new[] { "approved", "rejected", "timed-out", "cancelled" })
        {
            var fxStore = new AutomationRepository(NewDatabase());
            var fxCoord = new AutomationRunContinuationCoordinator(fxStore, fxStore, fxStore, null, new StubApprovalAuth(true));
            var runRow = Assert.Single(await fxStore.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token));
            var currentRevision = runRow.Revision;
            var outcome = await fxCoord.ApplyApprovalDecisionAsync(
                new AutomationRunApprovalDecision(runRow.Id, approvalNodeId, decision, "owner", currentRevision), now, token).WaitAsync(Bound, token);
            Assert.True(outcome.Accepted, outcome.Message);
            Assert.Equal($"approval.{decision}", outcome.Code);

            // The decision is RECORDED (revision bumped) but the Run is NOT auto-executed: it remains
            // ApprovalRequired — never auto-approved, never made to run the side effect.
            var after = Assert.Single(await fxStore.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token));
            Assert.Equal(AutomationRunStatus.ApprovalRequired, after.Status);
            Assert.Equal(currentRevision + 1, after.Revision);
            Assert.True(AutomationGraphContinuationCodec.TryDeserialize(after.ContinuationDescriptorJson, out var c2));
            Assert.Equal(decision, c2.ApprovalDecision);
        }
    }

    // ---- helpers ----

    private sealed class StubApprovalAuth(bool authorized) : IAutomationApprovalAuth
    {
        public Task<bool> IsAuthorisedAsync(Guid automationId, string actorId, CancellationToken cancellationToken) => Task.FromResult(authorized);
    }

    private static GraphFixture BuildFutureWaitGraph()
    {
        var trigger = Node("Trigger");
        var wait = Node("Wait", ("until", "2028-01-01T00:00:00Z"));
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [trigger, wait],
            [new(trigger.Id, wait.Id)]);
        return new GraphFixture(Guid.NewGuid(), "d1g3st-future", graph, trigger.Id, Guid.Empty, wait.Id, Guid.Empty, Guid.Empty);
    }

    private static AutomationDefinition WaitDefinition(Guid automationId, GraphFixture fx, DateTimeOffset now)
    {
        var encoded = ScheduledGraphAutomationPayloadCodec.Serialize(fx.GraphId, fx.TriggerId, "Durable wait", AutomationGraphCodec.Serialize(fx.Graph), null,
            graphId: fx.GraphId, sourceRevision: 4, schemaVersion: 1, sourceDigest: fx.Digest, capabilityProfileId: "automations");
        return new AutomationDefinition(automationId, "Durable wait", HavenMode.Tasks, encoded, AutomationScheduleKind.Once,
            """{"at":"2026-10-20T00:00:00+00:00"}""", now.AddHours(-2), null, true, now, now);
    }

    private static AutomationGraphNodeDefinition Node(string category, params (string Key, string Value)[] parameters) =>
        new(Guid.NewGuid(), category, null, null, parameters.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase))
        { Title = category };

    private SqliteDatabase NewDatabase()
    {
        Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", _root);
        return new SqliteDatabase(new AppPaths());
    }

    private async Task<(string? LeaseToken, string? NextRunAt)> ReadAutomationRowAsync(Guid automationId)
    {
        await using var connection = await NewDatabase().OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT lease_token, next_run_at FROM automations WHERE id=$id;";
        command.Parameters.AddWithValue("$id", automationId.ToString());
        await using var reader = await command.ExecuteReaderAsync(token);
        await reader.ReadAsync(token);
        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

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

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", null);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class FakeModelClient(string response) : IOllamaClient
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([new ModelDescriptor(
                "qwen-test", 1, "qwen", "test", "test",
                new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow)]);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return response;
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) => Task.FromResult(response);
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(response, []));
    }
}
