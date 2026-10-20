using System.Diagnostics;
using System.Text.Json;
using Haven.Application.NodeGraph;

namespace Haven.Core.Tests;

public sealed class NodeGraphContinuationRuntimeTests
{
    [Fact]
    public async Task Legacy_adapter_activation_still_works_and_resume_fails_before_callback()
    {
        var (schemas, graph, profile) = Fixture();
        var legacy = new RecordingLegacyAdapter(graph, profile);
        var registry = new NodeGraphRuntimeRegistry(schemas, [legacy]);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);

        var activated = await registry.ActivateAsync(request);

        Assert.True(activated.Succeeded);
        Assert.Equal(GraphRunOutcome.Completed, activated.EffectiveOutcome);
        Assert.Equal(1, legacy.ExecuteCount);
        Assert.Null(activated.Envelope);

        // Produce a structurally valid envelope from a continuation-capable adapter, then attempt resume on the legacy adapter.
        var continuationAdapter = new RecordingContinuationAdapter(graph, profile);
        var envelope = await YieldEnvelopeAsync(continuationAdapter, schemas, request);

        var resumed = await registry.ResumeAsync(envelope, request);
        Assert.False(resumed.Succeeded);
        Assert.Equal("graph.resume.unsupported", resumed.Code);
        Assert.Equal(1, legacy.ExecuteCount); // not called
        Assert.Equal(0, legacy.ResumeCount);
    }

    [Fact]
    public async Task Yield_roundtrips_and_fresh_registry_resumes_same_identity_with_original_consumed_budget()
    {
        var (schemas, graph, profile) = Fixture();
        var adapter = new RecordingContinuationAdapter(graph, profile) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var registry = new NodeGraphRuntimeRegistry(schemas, [adapter]);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);

        var first = await registry.ActivateAsync(request);

        Assert.True(first.Succeeded);
        Assert.Equal(GraphRunOutcome.Yielded, first.Outcome);
        Assert.NotNull(first.Envelope);
        Assert.Equal(3, first.Envelope!.StepsConsumed);
        Assert.Equal(GraphContinuationKind.Wait, first.Envelope.Kind);

        var codec = new GraphRuntimeContinuationCodec();
        var json = codec.Serialize(first.Envelope!);
        var roundTripped = codec.Deserialize(json);
        Assert.Equal(first.Envelope, roundTripped);

        // A fresh registry and owning adapter re-admit and resume the same execution identity.
        var registry2 = new NodeGraphRuntimeRegistry(schemas, [adapter]);
        var second = await registry2.ResumeAsync(roundTripped, request);

        Assert.True(second.Succeeded);
        Assert.Equal(GraphRunOutcome.Completed, second.Outcome);
        Assert.Equal(3, adapter.LastResumeStartingConsumed); // original consumed budget carried forward
        Assert.Equal("run-1", adapter.LastResumeRunId);
        Assert.Equal(4, second.StepsConsumed);
    }

    [Fact]
    public async Task Changed_revision_is_rejected_before_resumed_callback()
    {
        await AssertRejectsBeforeCallbackAsync(envelope => envelope with { Revision = envelope.Revision + 1 }, "graph.resume.caller-mismatch");
    }

    [Fact]
    public async Task Changed_graph_id_is_rejected_before_resumed_callback()
    {
        await AssertRejectsBeforeCallbackAsync(envelope => envelope with { GraphId = Guid.NewGuid() }, "graph.resume.caller-mismatch");
    }

    [Fact]
    public async Task Changed_caller_identity_is_rejected_before_resumed_callback()
    {
        await AssertRejectsBeforeCallbackAsync(envelope => envelope with { SessionId = "other-session" }, "graph.resume.caller-mismatch");
        await AssertRejectsBeforeCallbackAsync(envelope => envelope with { EntityId = "other-entity" }, "graph.resume.caller-mismatch");
        await AssertRejectsBeforeCallbackAsync(envelope => envelope with { ParticipantId = "other-participant" }, "graph.resume.caller-mismatch");
    }

    [Fact]
    public async Task Tampered_source_digest_is_rejected_before_resumed_callback()
    {
        await AssertRejectsBeforeCallbackAsync(envelope => envelope with { Digest = "0000000000000000000000000000000000000000000000000000000000000000" },
            "graph.resume.digest-mismatch");
    }

    [Fact]
    public async Task Unsupported_protocol_version_is_rejected_before_adapter_effects()
    {
        await AssertRejectsBeforeCallbackAsync(envelope => envelope with { ProtocolVersion = 99 }, "graph.resume.version.unsupported");
    }

    [Fact]
    public async Task Bad_node_identity_is_rejected_before_resumed_callback()
    {
        await AssertRejectsBeforeCallbackAsync(envelope => envelope with { NodeId = Guid.NewGuid() }, "graph.resume.node.missing");
    }

    [Fact]
    public async Task Malformed_typed_state_is_rejected_before_adapter_effects()
    {
        await AssertRejectsBeforeCallbackAsync(envelope => envelope with { OwnerState = JsonDocument.Parse("[1,2]").RootElement },
            "graph.resume.state.malformed");
    }

    [Fact]
    public async Task Tampered_budgets_are_rejected_before_adapter_effects()
    {
        await AssertRejectsBeforeCallbackAsync(envelope => envelope with { StepsConsumed = envelope.StepLimit + 1 }, "graph.resume.budget.tampered");
        await AssertRejectsBeforeCallbackAsync(envelope => envelope with { StepLimit = 20000 }, "graph.resume.budget.tampered");
    }

    [Fact]
    public async Task Caller_mutations_do_not_change_admitted_or_returned_snapshot()
    {
        var (schemas, graph, profile) = Fixture();
        var adapter = new RecordingContinuationAdapter(graph, profile) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var registry = new NodeGraphRuntimeRegistry(schemas, [adapter]);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);

        var first = await registry.ActivateAsync(request);
        Assert.NotNull(first.Envelope);

        // Mutate the caller/adapter-owned collections and JSON after admission.
        adapter.LastProvidedNodes!.Clear();
        adapter.LastProvidedProfile!.AllowedNodeTypes.Add("FakeType");

        // A pristine reference run must produce the identical digest: the snapshot was captured independently.
        var referenceAdapter = new RecordingContinuationAdapter(graph, profile) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var referenceRegistry = new NodeGraphRuntimeRegistry(schemas, [referenceAdapter]);
        var referenceFirst = await referenceRegistry.ActivateAsync(request);

        Assert.Equal(referenceFirst.Envelope!.Digest, first.Envelope!.Digest);
    }

    [Fact]
    public async Task Returned_transitions_are_detached_snapshots()
    {
        var (schemas, graph, profile) = Fixture();
        var adapter = new RecordingContinuationAdapter(graph, profile) { Mode = AdapterMode.CompleteWithMutableTransitions };
        var registry = new NodeGraphRuntimeRegistry(schemas, [adapter]);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);

        var result = await registry.ActivateAsync(request);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Transitions.Count);
        Assert.Equal("{\"done\":true}", result.Transitions[0].Values.GetRawText());

        // Mutate the adapter-owned source list after return; the returned snapshot must remain unaffected.
        adapter.LastTransitions!.Add(new(Guid.NewGuid(), "injected", JsonDocument.Parse("{}").RootElement));
        Assert.Equal(1, result.Transitions.Count);
        Assert.Equal("{\"done\":true}", result.Transitions[0].Values.GetRawText());
    }

    [Fact]
    public async Task Wait_yields_immediately_without_delay_or_sleep()
    {
        var (schemas, graph, profile) = Fixture();
        var adapter = new RecordingContinuationAdapter(graph, profile) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var registry = new NodeGraphRuntimeRegistry(schemas, [adapter]);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);

        var stopwatch = Stopwatch.StartNew();
        var first = await registry.ActivateAsync(request);
        stopwatch.Stop();

        Assert.Equal(GraphRunOutcome.Yielded, first.Outcome);
        Assert.Equal(GraphContinuationKind.Wait, first.Envelope!.Kind);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2)); // no sleeping thread / timer loop
    }

    [Fact]
    public async Task Approval_does_not_advance_from_time_alone_and_no_permission_is_granted()
    {
        var (schemas, graph, profile) = Fixture();
        var adapter = new RecordingContinuationAdapter(graph, profile) { Mode = AdapterMode.ApprovalPendingResume };
        var registry = new NodeGraphRuntimeRegistry(schemas, [adapter]);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);

        var first = await registry.ActivateAsync(request);
        Assert.Equal(GraphRunOutcome.Yielded, first.Outcome);
        Assert.Equal(GraphContinuationKind.Approval, first.Envelope!.Kind);

        var codec = new GraphRuntimeContinuationCodec();
        // Put the resume deadline in the past: time alone must NOT auto-approve.
        var envelope = codec.Deserialize(codec.Serialize(first.Envelope)) with { ResumeAt = DateTimeOffset.UtcNow.AddHours(-1) };

        var second = await registry.ResumeAsync(envelope, request);

        Assert.Equal(GraphRunOutcome.Blocked, second.Outcome); // still pending, not auto-approved
        Assert.Equal(GraphContinuationKind.Approval, second.Envelope!.Kind);
        // The generic result carries no approval/grant field: the owning integration supplies the explicit decision.
        Assert.DoesNotContain(second.GetType().GetProperties(), property => property.Name.Contains("Perm", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("Grant", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Four_workflow_outcomes_are_distinct()
    {
        var values = Enum.GetValues<GraphRunOutcome>();
        Assert.Equal(4, values.Length);
        Assert.Equal(values.Distinct().Count(), values.Length);
    }

    [Fact]
    public async Task Exhausted_budget_remains_exhausted_after_restart_style_roundtrip()
    {
        var (schemas, graph, profile) = Fixture();
        var adapter = new RecordingContinuationAdapter(graph, profile) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var registry = new NodeGraphRuntimeRegistry(schemas, [adapter]);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId, stepLimit: 3);

        var first = await registry.ActivateAsync(request);
        Assert.Equal(3, first.Envelope!.StepsConsumed);
        Assert.Equal(3, first.Envelope.StepLimit);

        adapter.Mode = AdapterMode.ExhaustedResume;
        var envelope = first.Envelope;
        for (var i = 0; i < 2; i++)
        {
            var resumed = await registry.ResumeAsync(envelope, request);
            Assert.Equal(GraphRunOutcome.Blocked, resumed.Outcome);
            Assert.Equal(3, resumed.StepsConsumed);            // stays exhausted, never reset
            Assert.Equal(3, resumed.Envelope!.StepsConsumed);  // restart-style roundtrip preserves exhaustion
            envelope = resumed.Envelope;
        }
    }

    [Fact]
    public async Task Bad_acknowledgement_after_callback_remains_needs_recovery()
    {
        var (schemas, graph, profile) = Fixture();
        var adapter = new RecordingContinuationAdapter(graph, profile) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var registry = new NodeGraphRuntimeRegistry(schemas, [adapter]);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);

        var first = await registry.ActivateAsync(request);
        var envelope = first.Envelope!;

        adapter.Mode = AdapterMode.BadAckResume;
        var resumed = await registry.ResumeAsync(envelope, request);

        Assert.True(resumed.NeedsRecovery);
        Assert.Equal("graph.result.needs-recovery", resumed.Code);
        Assert.False(resumed.Succeeded);
        Assert.Equal(1, adapter.ResumeCount); // callback ran, then the invalid ack was treated as uncertainty
    }

    private static async Task AssertRejectsBeforeCallbackAsync(
        Func<GraphRuntimeContinuation, GraphRuntimeContinuation> tamper, string expectedCode)
    {
        var (schemas, graph, profile) = Fixture();
        var adapter = new RecordingContinuationAdapter(graph, profile);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);
        var envelope = await YieldEnvelopeAsync(adapter, schemas, request);

        adapter.Mode = AdapterMode.YieldFirstThenResumeComplete;
        var registry = new NodeGraphRuntimeRegistry(schemas, [adapter]);

        var result = await registry.ResumeAsync(tamper(envelope), request);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.Code);
        Assert.False(adapter.ResumeCalled); // rejected before the resumed callback
        Assert.Equal(0, adapter.ResumeCount);
    }

    private static async Task<GraphRuntimeContinuation> YieldEnvelopeAsync(
        RecordingContinuationAdapter adapter, NodeGraphSchemaRegistry schemas, GraphActivationRequest request)
    {
        adapter.Mode = AdapterMode.YieldFirstThenResumeComplete;
        var registry = new NodeGraphRuntimeRegistry(schemas, [adapter]);
        var first = await registry.ActivateAsync(request);
        Assert.NotNull(first.Envelope);
        return first.Envelope!;
    }

    private static (NodeGraphSchemaRegistry Schemas, GraphDocument Graph, GraphCapabilityProfile Profile) Fixture()
    {
        var configSchema = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}").RootElement;
        var node = new GraphNode(Guid.NewGuid(), "Action", 1, "app", "cap", null, JsonDocument.Parse("{}").RootElement, []);
        var graph = new GraphDocument(GraphDocument.CurrentSchemaVersion, Guid.NewGuid(), 1, "profile1",
            GraphRevisionState.Active, [node], []);
        var type = new GraphNodeType("Action", 1, "app", "cap", configSchema, []);
        var profile = new GraphCapabilityProfile("profile1", new HashSet<string>(StringComparer.Ordinal) { "Action" },
            new HashSet<string>(StringComparer.Ordinal) { "cap" });
        return (new NodeGraphSchemaRegistry([type], [profile]), graph, profile);
    }

    private static GraphActivationRequest MakeRequest(Guid graphId, long revision, Guid startNodeId, int stepLimit = 1000) =>
        new("TestGraph", graphId, revision, startNodeId, "TestEvent", "session1", "entity1", "participant1",
            JsonDocument.Parse("{\"input\":1}").RootElement, stepLimit);

    private sealed class RecordingLegacyAdapter(GraphDocument graph, GraphCapabilityProfile profile) : INodeGraphRuntimeAdapter
    {
        public int ExecuteCount { get; private set; }
        public int ResumeCount { get; private set; }
        public string GraphType => "TestGraph";
        public IReadOnlySet<string> SupportedEvents { get; } = new HashSet<string>(StringComparer.Ordinal) { "TestEvent" };

        public ValueTask<GraphRuntimeAdmission?> AdmitAsync(GraphActivationRequest request, CancellationToken cancellationToken)
            => ValueTask.FromResult<GraphRuntimeAdmission?>(new(graph, profile));

        public ValueTask<GraphActivationResult> ExecuteAsync(GraphActivationRequest request, GraphRuntimeAdmission admission,
            GraphExecutionBudget budget, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecuteCount++;
            budget.TryConsume(cancellationToken);
            return ValueTask.FromResult(new GraphActivationResult(true, "ok",
                [new(request.StartNodeId, "completed", JsonDocument.Parse("{\"done\":true}").RootElement)], 1));
        }
    }

    private enum AdapterMode
    {
        Complete,
        CompleteWithMutableTransitions,
        YieldFirstThenResumeComplete,
        ApprovalPendingResume,
        ExhaustedResume,
        BadAckResume,
    }

    private sealed class RecordingContinuationAdapter(GraphDocument graph, GraphCapabilityProfile profile) : INodeGraphContinuationAdapter
    {
        private readonly Guid _waitNodeId = graph.Nodes[0].NodeId;

        public AdapterMode Mode { get; set; }
        public int ExecuteCount { get; private set; }
        public int ResumeCount { get; private set; }
        public bool ResumeCalled { get; private set; }
        public int LastResumeStartingConsumed { get; private set; }
        public string? LastResumeRunId { get; private set; }
        public List<GraphNode>? LastProvidedNodes { get; private set; }
        public GraphCapabilityProfile? LastProvidedProfile { get; private set; }
        public List<GraphRuntimeTransition>? LastTransitions { get; private set; }
        public string GraphType => "TestGraph";
        public IReadOnlySet<string> SupportedEvents { get; } = new HashSet<string>(StringComparer.Ordinal) { "TestEvent" };

        public ValueTask<GraphRuntimeAdmission?> AdmitAsync(GraphActivationRequest request, CancellationToken cancellationToken)
        {
            LastProvidedNodes = new List<GraphNode>(graph.Nodes);
            LastProvidedProfile = new GraphCapabilityProfile(profile.ProfileId,
                new HashSet<string>(profile.AllowedNodeTypes, StringComparer.Ordinal),
                new HashSet<string>(profile.AllowedCapabilityIds, StringComparer.Ordinal));
            var copy = new GraphDocument(graph.SchemaVersion, graph.GraphId, graph.Revision, graph.CapabilityProfileId,
                graph.State, LastProvidedNodes, graph.Connections);
            return ValueTask.FromResult<GraphRuntimeAdmission?>(new(copy, LastProvidedProfile));
        }

        public ValueTask<GraphActivationResult> ExecuteAsync(GraphActivationRequest request, GraphRuntimeAdmission admission,
            GraphExecutionBudget budget, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecuteCount++;
            if (Mode == AdapterMode.Complete)
            {
                budget.TryConsume(cancellationToken);
                return ValueTask.FromResult(Completed(request, budget));
            }
            if (Mode == AdapterMode.CompleteWithMutableTransitions)
            {
                budget.TryConsume(cancellationToken);
                LastTransitions = [new(request.StartNodeId, "completed", JsonDocument.Parse("{\"done\":true}").RootElement)];
                return ValueTask.FromResult(new GraphActivationResult(true, "ok", LastTransitions, 1)
                { Outcome = GraphRunOutcome.Completed });
            }
            if (Mode is AdapterMode.YieldFirstThenResumeComplete or AdapterMode.ApprovalPendingResume)
            {
                budget.TryConsume(cancellationToken);
                budget.TryConsume(cancellationToken);
                budget.TryConsume(cancellationToken);
                var kind = Mode == AdapterMode.ApprovalPendingResume ? GraphContinuationKind.Approval : GraphContinuationKind.Wait;
                return ValueTask.FromResult(new GraphActivationResult(true, "waiting", [], budget.StepsConsumed)
                {
                    Outcome = GraphRunOutcome.Yielded,
                    Continuation = Capture("run-1", kind),
                });
            }
            throw new InvalidOperationException($"Unsupported execute mode {Mode}.");
        }

        public ValueTask<GraphActivationResult> ResumeAsync(GraphRuntimeContinuation continuation, GraphActivationRequest request,
            GraphRuntimeAdmission admission, GraphExecutionBudget budget, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResumeCount++;
            ResumeCalled = true;
            LastResumeStartingConsumed = budget.StepsConsumed;
            LastResumeRunId = continuation.OwnerRunId;
            if (Mode == AdapterMode.YieldFirstThenResumeComplete)
            {
                budget.TryConsume(cancellationToken);
                return ValueTask.FromResult(Completed(request, budget));
            }
            if (Mode == AdapterMode.ApprovalPendingResume)
            {
                // Time alone never auto-approves: still pending until an explicit owner-supplied outcome.
                return ValueTask.FromResult(new GraphActivationResult(true, "pending-approval", [], budget.StepsConsumed)
                {
                    Outcome = GraphRunOutcome.Blocked,
                    Continuation = Capture("run-1", GraphContinuationKind.Approval),
                });
            }
            if (Mode == AdapterMode.ExhaustedResume)
            {
                if (budget.TryConsume(cancellationToken)) return ValueTask.FromResult(Completed(request, budget));
                return ValueTask.FromResult(new GraphActivationResult(false, "budget-exhausted", [], budget.StepsConsumed)
                {
                    Outcome = GraphRunOutcome.Blocked,
                    Continuation = Capture("run-1", GraphContinuationKind.Wait),
                });
            }
            if (Mode == AdapterMode.BadAckResume)
            {
                // Lie about consumed steps (lower than the real budget) to force NeedsRecovery uncertainty.
                return ValueTask.FromResult(new GraphActivationResult(true, "ok", [], budget.StepsConsumed - 1)
                { Outcome = GraphRunOutcome.Completed });
            }
            throw new InvalidOperationException($"Unsupported resume mode {Mode}.");
        }

        private GraphContinuationCapture Capture(string runId, GraphContinuationKind kind) =>
            new(runId, kind, _waitNodeId, null, default, JsonDocument.Parse("{\"frontier\":[1,2]}").RootElement);

        private static GraphActivationResult Completed(GraphActivationRequest request, GraphExecutionBudget budget)
        {
            var steps = budget.StepsConsumed;
            var transitions = steps == 0
                ? []
                : [new(request.StartNodeId, "completed", JsonDocument.Parse("{\"done\":true}").RootElement)];
            return new GraphActivationResult(true, "ok", transitions, steps) { Outcome = GraphRunOutcome.Completed };
        }
    }
}
