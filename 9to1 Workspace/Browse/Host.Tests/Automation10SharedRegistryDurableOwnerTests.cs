/*
 * FILE DOCUMENTATION
 * Where: Browse/Host.Tests/Automation10SharedRegistryDurableOwnerTests.cs, a real Haven host test lane.
 * What: Closes the remaining real owner path: the REAL shared NodeGraphRuntimeRegistry + the REAL
 *       AutomationNodeGraphDurableAdapter over the REAL SQLite store — fresh start, resume, historical
 *       pinned-revision resume after a newer Active revision, forged owner-state/budget/request rejection
 *       without a stranded claim, and the coordinator's bounded uncertain-settlement recording with exact
 *       reconciliation (B executed once, original + recording causes preserved).
 * Why: Automations10 blockers 1-4. Uses no SDK/app/network/model calls; tests only, root compiles and
 *       runs the Browse/Host.Tests filter.
 * Maintenance: Uses the real SQLite schema/migrations and store reopen. The adapter is registered into the
 *       shared registry via its real factory (the production caller seam). Clock observations are explicit.
 */

using Haven.Application;
using Haven.Application.Automations;
using Haven.Application.NodeGraph;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

namespace HavenOS.Browse.Host.Tests;

public sealed class Automation10SharedRegistryDurableOwnerTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);
    private readonly CancellationToken token = TestContext.Current.CancellationToken;
    private readonly string _root = Directory.CreateTempSubdirectory("haven-automation10-").FullName;

    // ---- Test 1: shared registry + real adapter resumes a real suspended Run from stored authority ----

    [Fact]
    public async Task Shared_registry_resumes_real_suspended_run_from_stored_authority()
    {
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var (store, fx, automationId) = await SeedSuspendedRunAsync(now);
        // Reopen over the same file so the coordinator/registry see the persisted Run.
        var reopened = new AutomationRepository(NewDatabase());

        var adapter = BuildAdapter(reopened, fx, activeRevision: 4, historicalRevisions: [4]);
        var registry = BuildRegistry(adapter, fx);
        var request = ActivationRequest(fx, expectedRevision: 4);

        var envelope = await BuildEnvelopeFromStored(reopened, automationId, fx, request).WaitAsync(Bound, token);

        var result = await registry.ResumeAsync(envelope, request, token).WaitAsync(Bound, token);
        Assert.True(result.Succeeded, result.Code);
        Assert.Equal(GraphRunOutcome.Completed, result.EffectiveOutcome);

        var runs = await reopened.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token);
        var finalRow = Assert.Single(runs);
        Assert.Equal(AutomationRunStatus.Succeeded, finalRow.Status);
        Assert.Contains("graphSucceeded\":true", finalRow.Result ?? string.Empty, StringComparison.Ordinal);
        // No retained claim/settlement after durable completion.
        Assert.Empty(await reopened.GetUncertainSettlementsAsync(token).WaitAsync(Bound, token));
    }

    // ---- Test 2: forged owner-state/budget/request is rejected with no stranded claim ----

    [Fact]
    public async Task Forged_ownerstate_budget_request_rejected_without_stranded_claim()
    {
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var (store, fx, automationId) = await SeedSuspendedRunAsync(now);
        var reopened = new AutomationRepository(NewDatabase());

        var adapter = BuildAdapter(reopened, fx, activeRevision: 4, historicalRevisions: [4]);
        var registry = BuildRegistry(adapter, fx);
        var request = ActivationRequest(fx, expectedRevision: 4);
        var envelope = await BuildEnvelopeFromStored(reopened, automationId, fx, request).WaitAsync(Bound, token);

        // (a) Forged owner-state: tamper the envelope's OwnerState (the digest no longer matches the
        // authoritative binding derived from stored state, and the binding differs from the envelope).
        var forgedOwnerState = envelope with { OwnerState = JsonSerializer.SerializeToElement(new { trace = new[] { "forged" } }) };
        var rejectedOwner = await registry.ResumeAsync(forgedOwnerState, request, token).WaitAsync(Bound, token);
        Assert.False(rejectedOwner.Succeeded);

        // (b) Forged budget: a tampered step limit/consumed step contradicts the stored consumed budget.
        var forgedBudget = envelope with { StepsConsumed = 0, StepLimit = 5 };
        var rejectedBudget = await registry.ResumeAsync(forgedBudget, request, token).WaitAsync(Bound, token);
        Assert.False(rejectedBudget.Succeeded);

        // (c) Forged request: a different caller identity cannot resume the Run.
        var foreignRequest = ActivationRequest(fx, expectedRevision: 4) with { ParticipantId = "intruder", SessionId = "forged-session" };
        var rejectedCaller = await registry.ResumeAsync(envelope, foreignRequest, token).WaitAsync(Bound, token);
        Assert.False(rejectedCaller.Succeeded);

        // Crucially: NONE of the forged admissions stranded a claim (the claim is acquired only at the
        // real effect boundary, after validation). The run is still resumable by the legitimate caller.
        var claims = await reopened.GetUncertainSettlementsAsync(token).WaitAsync(Bound, token);
        Assert.Empty(claims);

        var legit = await registry.ResumeAsync(envelope, request, token).WaitAsync(Bound, token);
        Assert.True(legit.Succeeded, legit.Code);
        var finalRow = Assert.Single(await reopened.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token));
        Assert.Equal(AutomationRunStatus.Succeeded, finalRow.Status);
    }

    // ---- Test 3: old V4 resumes after a newer Active V5 is published (historical pinned revision) ----

    [Fact]
    public async Task Old_V4_resumes_after_newer_active_V5_published_using_pinned_revision()
    {
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        // Seed a suspended run pinned to revision 4.
        var (store, fx, automationId) = await SeedSuspendedRunAsync(now);
        var reopened = new AutomationRepository(NewDatabase());

        // The repository now has an Active V5 (a materially different payload) while the Run stays pinned
        // to historical V4. AdmitAsync must admit the OWNING historical pinned revision for resume, not
        // reject because V5 is Active.
        var adapter = BuildAdapter(reopened, fx, activeRevision: 5, historicalRevisions: [4]);
        var registry = BuildRegistry(adapter, fx);
        var request = ActivationRequest(fx, expectedRevision: 4);

        var envelope = await BuildEnvelopeFromStored(reopened, automationId, fx, request).WaitAsync(Bound, token);

        var result = await registry.ResumeAsync(envelope, request, token).WaitAsync(Bound, token);
        Assert.True(result.Succeeded, result.Code);

        var finalRow = Assert.Single(await reopened.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token));
        Assert.Equal(4, finalRow.DefinitionRevision); // pinned V4, never the Active V5 payload
        Assert.Equal(AutomationRunStatus.Succeeded, finalRow.Status);
        Assert.Contains("graphSucceeded\":true", finalRow.Result ?? string.Empty, StringComparison.Ordinal);
    }

    // ---- Test 4: two SQLite coordinators over the same store: exactly one winner claims the Run ----

    [Fact]
    public async Task Two_sqlite_coordinators_share_store_and_exactly_one_wins()
    {
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var (store, _, automationId) = await SeedSuspendedRunAsync(now);
        var suspended = Assert.Single(await store.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token));

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

    // ---- Test 5: bounded uncertain settlement preserves original + recording causes; reconcile replays ----

    [Fact]
    public async Task Uncertain_settlement_preserves_causes_and_exact_reconciliation_does_not_rerun_B()
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
        Assert.Equal(AutomationRunStatus.Waiting, suspended.Status);

        // Force terminal persistence to fail AFTER B has run (post-commit-like ACK loss / rollback).
        await using (var connection = await NewDatabase().OpenAsync(token))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TRIGGER force_terminal_fail
                BEFORE UPDATE ON automation_runs
                WHEN NEW.status=2 AND OLD.status<>2
                BEGIN SELECT RAISE(ABORT,'forced terminal fail'); END;
                """;
            await command.ExecuteNonQueryAsync(token);
        }

        var counting = new CountingTerminalResumer();
        var coordinator = new AutomationRunContinuationCoordinator(store, store, store, counting);
        var sweepAt = new DateTimeOffset(2026, 10, 19, 13, 0, 0, TimeSpan.Zero);
        var ex = await Assert.ThrowsAsync<SqliteException>(() => coordinator.SweepResumeAsync(sweepAt, 10, token)).WaitAsync(Bound, token);

        Assert.Equal(1, counting.Executions); // B ran exactly once
        var settlement = Assert.Single(await store.GetUncertainSettlementsAsync(token).WaitAsync(Bound, token));
        Assert.Equal(AutomationRunClaimSettlementKind.Complete, settlement.Kind);
        Assert.False(string.IsNullOrWhiteSpace(settlement.RunJson));
        Assert.Contains("forced terminal fail", settlement.Error, StringComparison.Ordinal);

        // The claim is retained: a second sweep cannot re-claim / re-run B.
        await coordinator.SweepResumeAsync(sweepAt, 10, token);
        Assert.Equal(1, counting.Executions);

        // Drop the blocker and reconcile the EXACT persistence operation (no fresh B execution).
        await using (var c2 = await NewDatabase().OpenAsync(token))
        await using (var cmd2 = c2.CreateCommand())
        {
            cmd2.CommandText = "DROP TRIGGER force_terminal_fail;";
            await cmd2.ExecuteNonQueryAsync(token);
        }
        var reconciled = await coordinator.ReconcileSweepAsync(token).WaitAsync(Bound, token);
        Assert.Equal(1, reconciled);
        Assert.Equal(1, counting.Executions); // B NOT re-run during reconcile
        var terminalRow = Assert.Single(await store.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token));
        Assert.Equal(AutomationRunStatus.Succeeded, terminalRow.Status);
        _ = ex;
    }

    // ---- helpers ----

    private async Task<(AutomationRepository Store, GraphFixture Fx, Guid AutomationId)> SeedSuspendedRunAsync(DateTimeOffset now)
    {
        var fx = BuildWaitGraph();
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var store = new AutomationRepository(database);
        var automationId = Guid.NewGuid();
        var task = WaitDefinition(automationId, fx, now);
        await store.UpsertAsync(task, token).WaitAsync(Bound, token);
        var runner = new ScheduledTaskRunner(store, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
            new BuiltInAutomationActionNodeExecutor(), continuationStore: store);
        var suspended = await runner.RunOneAsync(task, now, token).WaitAsync(Bound, token);
        Assert.Equal(AutomationRunStatus.Waiting, suspended.Status);
        return (store, fx, automationId);
    }

    private static GraphActivationRequest ActivationRequest(GraphFixture fx, long expectedRevision)
        => new(AutomationNodeGraphDurableAdapter.GraphTypeValue, fx.GraphId, expectedRevision, fx.TriggerId,
            "schedule", "session", "entity", "participant", JsonSerializer.SerializeToElement(new { }), 1000);

    private static AutomationNodeGraphDurableAdapter BuildAdapter(AutomationRepository store, GraphFixture fx, long activeRevision, long[] historicalRevisions)
    {
        var (schemas, graph) = BuildSchemaFixture(fx.GraphId, fx.TriggerId, fx.WaitId);
        var repo = new StoredGraphRepository(graph, fx.GraphId, activeRevision, historicalRevisions);
        return AutomationNodeGraphDurableAdapter.Create(
            new NodeGraphAutomationAdapter(schemas, BuildBindings()),
            repo, store, store, store,
            new ScheduledTaskRunner(store, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
                new BuiltInAutomationActionNodeExecutor(), continuationStore: store));
    }

    private static NodeGraphRuntimeRegistry BuildRegistry(AutomationNodeGraphDurableAdapter adapter, GraphFixture fx)
    {
        var (schemas, _) = BuildSchemaFixture(fx.GraphId, fx.TriggerId, fx.WaitId);
        return new NodeGraphRuntimeRegistry(schemas, [adapter]);
    }

    /// <summary>Builds the envelope exactly as the adapter derives its authoritative binding from stored
    /// state, so the registry's binding↔envelope and digest checks pass for the legitimate caller.</summary>
    private static async Task<GraphRuntimeContinuation> BuildEnvelopeFromStored(
        AutomationRepository store, Guid automationId, GraphFixture fx, GraphActivationRequest request)
    {
        var runs = await store.GetRunsAsync(automationId, 10, CancellationToken.None).ConfigureAwait(false);
        var run = runs.FirstOrDefault(r => r.Status == AutomationRunStatus.Waiting);
        if (run is null)
            throw new InvalidOperationException("Could not locate the suspended Run for envelope derivation.");
        if (!AutomationGraphContinuationCodec.TryDeserialize(run.ContinuationDescriptorJson, out var cont) ||
            cont.PendingYield is not { } pending)
            throw new InvalidOperationException("The stored Run has no pending yield.");

        var nodeId = pending.Kind == AutomationGraphYieldKind.Approval
            ? (pending.ApprovalNodeId ?? Guid.Empty) : (pending.YieldNodeId ?? Guid.Empty);
        var condition = DeriveCondition(cont);
        var ownerState = DeriveOwnerState(cont);
        var kind = pending.Kind == AutomationGraphYieldKind.Approval ? GraphContinuationKind.Approval : GraphContinuationKind.Wait;
        var binding = new GraphContinuationAdmission(run.Id.ToString(), kind, nodeId, cont.ResumeAt, condition, ownerState,
            request.StepLimit, cont.ConsumedBudget);

        var (_, graph) = BuildSchemaFixture(fx.GraphId, fx.TriggerId, fx.WaitId);
        var active = graph with { Revision = request.ExpectedRevision };
        var digest = GraphContinuationDigest.Compute(active, request.Variables, binding);

        return new GraphRuntimeContinuation(
            GraphRuntimeContinuation.CurrentProtocolVersion,
            AutomationNodeGraphDurableAdapter.GraphTypeValue,
            run.Id.ToString(),
            fx.GraphId, request.ExpectedRevision, "automations", 1, request.StartNodeId, request.EventType,
            request.SessionId, request.EntityId, request.ParticipantId,
            kind, nodeId, cont.ResumeAt, condition, ownerState, cont.ConsumedBudget, request.StepLimit, digest);
    }

    private static JsonElement DeriveCondition(AutomationGraphContinuation c)
    {
        var condition = c.PendingYield?.Condition;
        if (string.IsNullOrWhiteSpace(condition)) return JsonSerializer.SerializeToElement(new { });
        try { using var doc = JsonDocument.Parse(condition); return doc.RootElement.Clone(); }
        catch (JsonException) { return JsonSerializer.SerializeToElement(new { value = condition }); }
    }

    private static JsonElement DeriveOwnerState(AutomationGraphContinuation c)
    {
        var trace = c.Trace.Select(item => string.Format("{0}:{1}", item.NodeId.ToString("D"), (int)item.Status)).ToArray();
        var frontier = c.Frontier.Select(id => id.ToString("D")).ToArray();
        return JsonSerializer.SerializeToElement(new { trace, frontier });
    }

    private static (NodeGraphSchemaRegistry Schemas, GraphDocument Graph) BuildSchemaFixture(Guid graphId, Guid triggerId, Guid waitId)
    {
        // Deterministic port/connection GUIDs so every call yields bit-identical graphs. The envelope
        // digest is computed over exactly the graph the registry admits; any nondeterminism here would
        // make the digest mismatch spuriously (graph.resume.digest-mismatch).
        var inPort1 = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var outPort1 = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var inPort2 = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var outPort2 = Guid.Parse("10000000-0000-0000-0000-000000000004");
        var connId = Guid.Parse("10000000-0000-0000-0000-000000000005");
        var emptyConfig = JsonSerializer.SerializeToElement(new { });

        var triggerType = new GraphNodeType("trigger", 1, "automations", "trigger", emptyConfig,
            [new GraphPortTemplate("in", GraphPortDirection.Input, "flow", true), new GraphPortTemplate("out", GraphPortDirection.Output, "flow", true)]);
        var waitType = new GraphNodeType("wait", 1, "automations", "wait", emptyConfig,
            [new GraphPortTemplate("in", GraphPortDirection.Input, "flow", true), new GraphPortTemplate("out", GraphPortDirection.Output, "flow", true)]);
        var profile = new GraphCapabilityProfile("automations",
            new HashSet<string>(StringComparer.Ordinal) { "trigger", "wait" },
            new HashSet<string>(StringComparer.Ordinal) { "trigger", "wait" });
        var schemas = new NodeGraphSchemaRegistry([triggerType, waitType], [profile]);

        var triggerNode = new GraphNode(triggerId, "trigger", 1, "automations", "trigger", null, emptyConfig,
            [new GraphPort(inPort1, "in", GraphPortDirection.Input, "flow", true), new GraphPort(outPort1, "out", GraphPortDirection.Output, "flow", true)]);
        var waitNode = new GraphNode(waitId, "wait", 1, "automations", "wait", null, emptyConfig,
            [new GraphPort(inPort2, "in", GraphPortDirection.Input, "flow", true), new GraphPort(outPort2, "out", GraphPortDirection.Output, "flow", true)]);
        var graph = new GraphDocument(1, graphId, 4, "automations", GraphRevisionState.Active,
            Array.AsReadOnly(new[] { triggerNode, waitNode }),
            Array.AsReadOnly(new[] { new GraphConnection(connId, outPort1, inPort2) }));
        return (schemas, graph);
    }

    private static IEnumerable<AutomationNodeGraphBinding> BuildBindings()
    {
        yield return new AutomationNodeGraphBinding("trigger", 1, "automations", "trigger",
            _ => true,
            node => new AutomationGraphNodeDefinition(node.NodeId, "Trigger", null, null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            { Title = "Trigger" });
        yield return new AutomationNodeGraphBinding("wait", 1, "automations", "wait",
            _ => true,
            node => new AutomationGraphNodeDefinition(node.NodeId, "Wait", null, null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["until"] = "2000-01-01T00:00:00Z" })
            { Title = "Wait" });
    }

    /// <summary>Real versioned repository fake: returns the canonical graph at the requested revision by
    /// selecting the Active or the owning historical activated revision. Persisted evidence only.</summary>
    private sealed class StoredGraphRepository : IVersionedNodeGraphRepository
    {
        private readonly GraphDocument _graph;
        private readonly Guid _graphId;
        private readonly long _activeRevision;
        private readonly long[] _historicalRevisions;

        public StoredGraphRepository(GraphDocument graph, Guid graphId, long activeRevision, long[] historicalRevisions)
        {
            _graph = graph;
            _graphId = graphId;
            _activeRevision = activeRevision;
            _historicalRevisions = historicalRevisions;
        }

        public Task<OwnedGraphDefinition?> GetAsync(Guid graphId, CancellationToken cancellationToken)
        {
            if (graphId != _graphId) return Task.FromResult<OwnedGraphDefinition?>(null);
            var active = _graph with { Revision = _activeRevision, State = GraphRevisionState.Active };
            var history = _historicalRevisions.Select(revision => _graph with { Revision = revision, State = GraphRevisionState.Active }).ToArray();
            var owned = new OwnedGraphDefinition("automations", "entity", _graph with { Revision = _activeRevision + 1 }, active)
            {
                ActivatedRevisions = Array.AsReadOnly(history)
            };
            return Task.FromResult<OwnedGraphDefinition?>(owned);
        }

        public Task<bool> TrySaveDraftAsync(string ownerAppId, string ownerEntityId, GraphDocument draft, long expectedGraphRevision, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<bool> TryActivateAsync(Guid graphId, long expectedGraphRevision, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    // ---- fixture helpers (mirror existing durable test lane) ----

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

    private sealed class CountingTerminalResumer : IAutomationRunResumer
    {
        public int Executions { get; private set; }
        public Task<AutomationRunResumeResult> ResumeAsync(AutomationRun run, CancellationToken cancellationToken)
        {
            Executions++;
            var updated = run with
            {
                Revision = run.Revision + 1,
                Status = AutomationRunStatus.Succeeded,
                CompletedAt = DateTimeOffset.UtcNow,
                Error = null,
                Result = "{\"graphSucceeded\":true}"
            };
            return Task.FromResult(new AutomationRunResumeResult(updated, run.ScheduledFor.AddDays(1), false,
                new(true, AutomationResumeErrorKind.None, "resume.succeeded", "ok", run.Id)));
        }
    }

    private SqliteDatabase NewDatabase()
    {
        Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", _root);
        return new SqliteDatabase(new AppPaths());
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

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", null);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
