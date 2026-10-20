using System.Diagnostics;
using System.Text.Json;
using Haven.Application.NodeGraph;

namespace Haven.Core.Tests;

public sealed class NodeGraphContinuationRuntimeTests
{
    private static readonly DateTimeOffset PastApprovalAt = DateTimeOffset.UtcNow.AddMinutes(-5);

    [Fact]
    public async Task Legacy_adapter_activation_still_works_and_resume_fails_before_callback()
    {
        var (schemas, graph, profile) = Fixture();
        var legacy = new RecordingLegacyAdapter(graph, profile);
        var registry = new NodeGraphRuntimeRegistry(schemas, [legacy]);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);

        var activated = await registry.ActivateAsync(request, TestContext.Current.CancellationToken);

        Assert.True(activated.Succeeded);
        Assert.Equal(GraphRunOutcome.Completed, activated.EffectiveOutcome);
        Assert.Equal(1, legacy.ExecuteCount);
        Assert.Null(activated.Envelope);

        // Produce an envelope from a continuation-capable adapter, then attempt resume on the legacy adapter.
        var saved = new TestSavedRun();
        var continuationAdapter = new RecordingContinuationAdapter(graph, profile, saved);
        var envelope = await YieldEnvelopeAsync(continuationAdapter, schemas, request);

        var resumed = await registry.ResumeAsync(envelope, request, TestContext.Current.CancellationToken);
        Assert.False(resumed.Succeeded);
        Assert.Equal("graph.resume.unsupported", resumed.Code);
        Assert.Equal(1, legacy.ExecuteCount); // not called
        Assert.Equal(0, legacy.ResumeCount);
    }

    [Fact]
    public async Task Yield_roundtrips_and_fresh_registry_resumes_same_identity_with_original_consumed_budget()
    {
        var (schemas, graph, profile) = Fixture();
        var saved = new TestSavedRun();
        var actAdapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var actRegistry = new NodeGraphRuntimeRegistry(schemas, [actAdapter]);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);

        var first = await actRegistry.ActivateAsync(request, TestContext.Current.CancellationToken);

        Assert.True(first.Succeeded);
        Assert.Equal(GraphRunOutcome.Yielded, first.Outcome);
        Assert.NotNull(first.Envelope);
        Assert.Equal(3, first.Envelope!.StepsConsumed);
        Assert.Equal(GraphContinuationKind.Wait, first.Envelope.Kind);

        var codec = new GraphRuntimeContinuationCodec();
        var json = codec.Serialize(first.Envelope!);
        var roundTripped = codec.Deserialize(json);
        AssertContinuationEquivalent(first.Envelope!, roundTripped);

        // A genuine NEW owning adapter with the SAME authoritative saved state re-admits and resumes the same identity.
        var resumeAdapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var freshRegistry = new NodeGraphRuntimeRegistry(schemas, [resumeAdapter]);
        var second = await freshRegistry.ResumeAsync(roundTripped, request, TestContext.Current.CancellationToken);

        Assert.True(second.Succeeded);
        Assert.Equal(GraphRunOutcome.Completed, second.Outcome);
        Assert.Equal(3, resumeAdapter.LastResumeStartingConsumed); // original consumed budget carried forward
        Assert.Equal("run-1", resumeAdapter.LastResumeRunId);
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
    public async Task Changed_capability_profile_id_is_rejected_at_descriptor_guard_before_resumed_callback()
    {
        // Tampering only the capability profile leaves caller identity matching, so the registry reaches the
        // descriptor/schema guard (not the earlier caller-binding guard) and rejects there with zero resume callback.
        await AssertRejectsBeforeCallbackAsync(envelope => envelope with { CapabilityProfileId = "other-profile" },
            "graph.resume.descriptor-mismatch");
    }

    [Fact]
    public async Task Changed_schema_version_is_rejected_at_descriptor_guard_before_resumed_callback()
    {
        await AssertRejectsBeforeCallbackAsync(envelope => envelope with { SchemaVersion = envelope.SchemaVersion + 1 },
            "graph.resume.descriptor-mismatch");
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
        var saved = new TestSavedRun();
        var adapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var registry = new NodeGraphRuntimeRegistry(schemas, [adapter]);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);

        var first = await registry.ActivateAsync(request, TestContext.Current.CancellationToken);
        Assert.NotNull(first.Envelope);

        // Mutate the caller/adapter-owned backing sets and JSON snap admission (the set reference is retained mutable).
        adapter.LastProvidedNodes!.Clear();
        adapter.LastProvidedAllowedNodes!.Add("FakeType");

        // A pristine reference run must produce the identical digest: the snapshot was captured independently.
        var saved2 = new TestSavedRun();
        var referenceAdapter = new RecordingContinuationAdapter(graph, profile, saved2) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var referenceRegistry = new NodeGraphRuntimeRegistry(schemas, [referenceAdapter]);
        var referenceFirst = await referenceRegistry.ActivateAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(referenceFirst.Envelope!.Digest, first.Envelope!.Digest);
    }

    [Fact]
    public async Task Returned_transitions_are_detached_snapshots()
    {
        var (schemas, graph, profile) = Fixture();
        var saved = new TestSavedRun();
        var adapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.CompleteWithMutableTransitions };
        var registry = new NodeGraphRuntimeRegistry(schemas, [adapter]);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);

        var result = await registry.ActivateAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Single(result.Transitions);
        Assert.Equal("{\"done\":true}", result.Transitions[0].Values.GetRawText());

        // Mutate the adapter-owned source list after return; the returned snapshot must remain unaffected.
        adapter.LastTransitions!.Add(new(Guid.NewGuid(), "injected", JsonDocument.Parse("{}").RootElement));
        Assert.Single(result.Transitions);
        Assert.Equal("{\"done\":true}", result.Transitions[0].Values.GetRawText());
    }

    [Fact]
    public async Task Wait_yields_immediately_without_delay_or_sleep()
    {
        var (schemas, graph, profile) = Fixture();
        var saved = new TestSavedRun();
        var adapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var registry = new NodeGraphRuntimeRegistry(schemas, [adapter]);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);

        var stopwatch = Stopwatch.StartNew();
        var first = await registry.ActivateAsync(request, TestContext.Current.CancellationToken);
        stopwatch.Stop();

        Assert.Equal(GraphRunOutcome.Yielded, first.Outcome);
        Assert.Equal(GraphContinuationKind.Wait, first.Envelope!.Kind);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2)); // no sleeping thread / timer loop
    }

    [Fact]
    public async Task Approval_does_not_advance_from_time_alone_and_no_permission_is_granted()
    {
        var (schemas, graph, profile) = Fixture();
        var saved = new TestSavedRun();
        var actAdapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.ApprovalPendingResume };
        var actRegistry = new NodeGraphRuntimeRegistry(schemas, [actAdapter]);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);

        var first = await actRegistry.ActivateAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(GraphRunOutcome.Yielded, first.Outcome);
        Assert.Equal(GraphContinuationKind.Approval, first.Envelope!.Kind);
        Assert.True(first.Envelope.ResumeAt is { } && first.Envelope.ResumeAt < DateTimeOffset.UtcNow); // already due

        // A genuine fresh registry/owning adapter with the same authoritative saved state resumes; due time alone
        // must NOT auto-approve.
        var resumeAdapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.ApprovalPendingResume };
        var freshRegistry = new NodeGraphRuntimeRegistry(schemas, [resumeAdapter]);
        var second = await freshRegistry.ResumeAsync(first.Envelope, request, TestContext.Current.CancellationToken);

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
    public void Approval_decision_outcomes_are_distinct_and_not_inferred_from_arbitrary_strings()
    {
        var values = Enum.GetValues<GraphApprovalDecision>();
        Assert.Equal(4, values.Length);
        Assert.Equal(values.Distinct().Count(), values.Length);
        Assert.False(Enum.TryParse<GraphApprovalDecision>("banana", ignoreCase: false, out _));
        Assert.True(Enum.TryParse<GraphApprovalDecision>("Approved", ignoreCase: false, out var approved));
        Assert.Equal(GraphApprovalDecision.Approved, approved);
    }

    [Fact]
    public async Task Exhausted_budget_remains_exhausted_after_restart_style_roundtrip()
    {
        var (schemas, graph, profile) = Fixture();
        var saved = new TestSavedRun();
        var actAdapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var actRegistry = new NodeGraphRuntimeRegistry(schemas, [actAdapter]);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId, stepLimit: 3);

        var first = await actRegistry.ActivateAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(3, first.Envelope!.StepsConsumed);
        Assert.Equal(3, first.Envelope.StepLimit);

        // Fresh registry + fresh adapter, same authoritative saved state (already exhausted).
        var resumeAdapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var freshRegistry = new NodeGraphRuntimeRegistry(schemas, [resumeAdapter]);
        var envelope = first.Envelope;
        for (var i = 0; i < 2; i++)
        {
            var resumed = await freshRegistry.ResumeAsync(envelope, request, TestContext.Current.CancellationToken);
            Assert.Equal(GraphRunOutcome.Blocked, resumed.Outcome);
            Assert.Equal("graph.budget.exhausted", resumed.Code);
            Assert.Equal(3, resumed.StepsConsumed);            // stays exhausted, never reset
            Assert.Equal(3, resumed.Envelope!.StepsConsumed);  // restart-style roundtrip preserves exhaustion
            Assert.Equal(0, resumeAdapter.ResumeCount);        // exhausted saved budget never runs the callback again
            envelope = resumed.Envelope;
        }
    }

    [Fact]
    public async Task Bad_acknowledgement_after_callback_remains_needs_recovery()
    {
        var (schemas, graph, profile) = Fixture();
        var saved = new TestSavedRun();
        var actAdapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var actRegistry = new NodeGraphRuntimeRegistry(schemas, [actAdapter]);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);

        var first = await actRegistry.ActivateAsync(request, TestContext.Current.CancellationToken);
        var envelope = first.Envelope!;

        var resumeAdapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.BadAckResume };
        var registry = new NodeGraphRuntimeRegistry(schemas, [resumeAdapter]);
        var resumed = await registry.ResumeAsync(envelope, request, TestContext.Current.CancellationToken);

        Assert.True(resumed.NeedsRecovery);
        Assert.Equal("graph.result.needs-recovery", resumed.Code);
        Assert.False(resumed.Succeeded);
        Assert.Equal(1, resumeAdapter.ResumeCount); // callback ran, then the invalid ack was treated as uncertainty
    }

    [Fact]
    public async Task Zero_new_step_fabricated_transition_is_needs_recovery()
    {
        var (schemas, graph, profile) = Fixture();
        var saved = new TestSavedRun();
        var actAdapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var actRegistry = new NodeGraphRuntimeRegistry(schemas, [actAdapter]);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);

        var first = await actRegistry.ActivateAsync(request, TestContext.Current.CancellationToken);
        var resumeAdapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.FabricatedTransitionResume };
        var registry = new NodeGraphRuntimeRegistry(schemas, [resumeAdapter]);
        var resumed = await registry.ResumeAsync(first.Envelope!, request, TestContext.Current.CancellationToken);

        Assert.True(resumed.NeedsRecovery);
        Assert.Equal("graph.result.needs-recovery", resumed.Code);
        Assert.Equal(1, resumeAdapter.ResumeCount); // callback ran, but its fabricated transition was not funded by a new step
    }

    [Fact]
    public async Task Malicious_direct_typed_oversized_input_is_rejected_before_callback()
    {
        var (schemas, graph, profile) = Fixture();
        var saved = new TestSavedRun();
        var actAdapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var actRegistry = new NodeGraphRuntimeRegistry(schemas, [actAdapter]);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);

        var first = await actRegistry.ActivateAsync(request, TestContext.Current.CancellationToken);
        // Build a direct in-memory envelope whose owner state alone exceeds the serialized size bound.
        var blob = "{\"blob\":\"" + new string('x', 2 * 1024 * 1024) + "\"}";
        var oversized = first.Envelope! with { OwnerState = JsonDocument.Parse(blob).RootElement };

        var resumeAdapter = new RecordingContinuationAdapter(graph, profile, saved);
        var registry = new NodeGraphRuntimeRegistry(schemas, [resumeAdapter]);
        var resumed = await registry.ResumeAsync(oversized, request, TestContext.Current.CancellationToken);

        Assert.False(resumed.Succeeded);
        Assert.Equal("graph.resume.envelope.oversized", resumed.Code);
        Assert.Equal(0, resumeAdapter.ResumeCount);
    }

    [Fact]
    public async Task Revoked_or_denied_admission_fails_closed_before_callback()
    {
        var (schemas, graph, profile) = Fixture();
        var saved = new TestSavedRun();
        var actAdapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var actRegistry = new NodeGraphRuntimeRegistry(schemas, [actAdapter]);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);

        var first = await actRegistry.ActivateAsync(request, TestContext.Current.CancellationToken);
        var resumeAdapter = new RecordingContinuationAdapter(graph, profile, saved) { DenyAdmission = true };
        var registry = new NodeGraphRuntimeRegistry(schemas, [resumeAdapter]);
        var resumed = await registry.ResumeAsync(first.Envelope!, request, TestContext.Current.CancellationToken);

        Assert.False(resumed.Succeeded);
        Assert.Equal("graph.resume.denied", resumed.Code);
        Assert.Equal(0, resumeAdapter.ResumeCount);
    }

    [Fact]
    public async Task Changed_authoritative_run_identity_is_rejected_before_callback()
    {
        await AssertAdmissionRejectsBeforeCallbackAsync(saved => saved.RunId = "run-other", "graph.resume.admission-mismatch");
    }

    [Fact]
    public async Task Changed_authoritative_frontier_kind_condition_are_rejected_before_callback()
    {
        await AssertAdmissionRejectsBeforeCallbackAsync(saved => saved.Kind = GraphContinuationKind.Custom, "graph.resume.admission-mismatch");
        await AssertAdmissionRejectsBeforeCallbackAsync(saved => saved.OwnerState = JsonDocument.Parse("{\"frontier\":[9]}").RootElement, "graph.resume.admission-mismatch");
        await AssertAdmissionRejectsBeforeCallbackAsync(saved => saved.Condition = JsonDocument.Parse("{\"device\":\"offline\"}").RootElement, "graph.resume.admission-mismatch");
    }

    [Fact]
    public async Task Changed_authoritative_budget_is_rejected_before_callback()
    {
        await AssertAdmissionRejectsBeforeCallbackAsync(saved => saved.Consumed = 0, "graph.resume.admission-mismatch");
        await AssertAdmissionRejectsBeforeCallbackAsync(saved => saved.Limit = 999999, "graph.resume.budget.tampered");
    }

    [Fact]
    public async Task Caller_json_mutation_during_held_admit_does_not_affect_snapshot()
    {
        var (schemas, graph, profile) = Fixture();
        var saved = new TestSavedRun();
        var adapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.DelayedAdmitYield };
        var registry = new NodeGraphRuntimeRegistry(schemas, [adapter]);
        using var sourceDoc = JsonDocument.Parse("{\"input\":1}");
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId, variables: sourceDoc.RootElement);

        var task = registry.ActivateAsync(request, TestContext.Current.CancellationToken);
        sourceDoc.Dispose(); // dispose the caller-owned source while Admit is held; the registry already cloned it

        var result = await task;
        Assert.True(result.Succeeded);
        Assert.Equal(GraphRunOutcome.Yielded, result.Outcome);
        Assert.NotNull(result.Envelope);
    }

    [Fact]
    public async Task Already_disposed_caller_input_is_rejected_before_effects_with_precise_code()
    {
        var (schemas, graph, profile) = Fixture();
        var saved = new TestSavedRun();
        var actAdapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var actRegistry = new NodeGraphRuntimeRegistry(schemas, [actAdapter]);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);
        var envelope = await YieldEnvelopeAsync(actAdapter, schemas, request);

        // Recreate the envelope with an OwnerState backed by a document we control, then dispose that document so the
        // registry's pre-await detach sees a disposed backing document instead of leaking ObjectDisposedException.
        var disposedEnvelope = CreateDisposedEnvelope(envelope);

        var resumeAdapter = new RecordingContinuationAdapter(graph, profile, saved);
        var registry = new NodeGraphRuntimeRegistry(schemas, [resumeAdapter]);
        var result = await registry.ResumeAsync(disposedEnvelope, request, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("graph.resume.input.invalid", result.Code);
        Assert.Equal(0, resumeAdapter.ResumeCount); // rejected before any owner effect / resume callback
    }

    [Fact]
    public async Task Dispose_caller_envelope_while_admit_resume_is_held_does_not_affect_snapshot()
    {
        var (schemas, graph, profile) = Fixture();
        var saved = new TestSavedRun();
        var actAdapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var actRegistry = new NodeGraphRuntimeRegistry(schemas, [actAdapter]);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);
        var envelope = await YieldEnvelopeAsync(actAdapter, schemas, request);

        // Own a fresh backing document for the caller envelope so the test can dispose it mid-resume.
        var envelopeDoc = JsonDocument.Parse(envelope.OwnerState.GetRawText());
        var controlledEnvelope = envelope with { OwnerState = envelopeDoc.RootElement };

        var resumeAdapter = new RecordingContinuationAdapter(graph, profile, saved)
        { Mode = AdapterMode.YieldFirstThenResumeComplete, HoldAdmitResumeForDisposal = true };
        var registry = new NodeGraphRuntimeRegistry(schemas, [resumeAdapter]);

        var task = registry.ResumeAsync(controlledEnvelope, request, TestContext.Current.CancellationToken);
        // Wait until AdmitResumeAsync actually holds; the registry has already detached a snapshot before this await.
        await resumeAdapter.AdmitResumeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        envelopeDoc.Dispose(); // dispose the caller's envelope backing document while owner admission is held

        resumeAdapter.ReleaseAdmitResume();
        var result = await task;

        Assert.True(result.Succeeded);
        Assert.Equal(GraphRunOutcome.Completed, result.Outcome);
        Assert.Equal(1, resumeAdapter.ResumeCount); // detached snapshot survived the caller disposal
    }

    [Fact]
    public async Task Owner_capture_disposal_after_publication_leaves_envelope_independent()
    {
        var (schemas, graph, profile) = Fixture();
        var saved = new TestSavedRun();
        var adapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var registry = new NodeGraphRuntimeRegistry(schemas, [adapter]);
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);

        var first = await registry.ActivateAsync(request, TestContext.Current.CancellationToken);
        Assert.NotNull(first.Envelope);
        var envelopeSnapshot = first.Envelope!.OwnerState.GetRawText();

        // Dispose the owner's backing capture documents after publication; the returned envelope is a detached capture
        // and must remain fully usable/independent.
        adapter.DisposeOwnedCaptureDocuments();

        var codec = new GraphRuntimeContinuationCodec();
        var json = codec.Serialize(first.Envelope!);
        var back = codec.Deserialize(json);
        Assert.Equal(envelopeSnapshot, back.OwnerState.GetRawText());
    }

    private static GraphRuntimeContinuation CreateDisposedEnvelope(GraphRuntimeContinuation envelope)
    {
        var doc = JsonDocument.Parse(envelope.OwnerState.GetRawText());
        var disposed = envelope with { OwnerState = doc.RootElement };
        doc.Dispose();
        return disposed;
    }

    private static void AssertContinuationEquivalent(GraphRuntimeContinuation expected, GraphRuntimeContinuation actual)
    {
        Assert.Equal(expected.ProtocolVersion, actual.ProtocolVersion);
        Assert.Equal(expected.GraphType, actual.GraphType);
        Assert.Equal(expected.OwnerRunId, actual.OwnerRunId);
        Assert.Equal(expected.GraphId, actual.GraphId);
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.CapabilityProfileId, actual.CapabilityProfileId);
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.StartNodeId, actual.StartNodeId);
        Assert.Equal(expected.EventType, actual.EventType);
        Assert.Equal(expected.SessionId, actual.SessionId);
        Assert.Equal(expected.EntityId, actual.EntityId);
        Assert.Equal(expected.ParticipantId, actual.ParticipantId);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.NodeId, actual.NodeId);
        Assert.Equal(expected.ResumeAt, actual.ResumeAt);
        Assert.Equal(expected.StepsConsumed, actual.StepsConsumed);
        Assert.Equal(expected.StepLimit, actual.StepLimit);
        Assert.Equal(expected.Digest, actual.Digest);
        Assert.Equal(Canonical(expected.Condition), Canonical(actual.Condition));
        Assert.Equal(Canonical(expected.OwnerState), Canonical(actual.OwnerState));
    }

    private static string Canonical(JsonElement element) =>
        element.ValueKind == JsonValueKind.Undefined ? "<absent>" : element.GetRawText();

    private static async Task AssertRejectsBeforeCallbackAsync(
        Func<GraphRuntimeContinuation, GraphRuntimeContinuation> tamper, string expectedCode)
    {
        var (schemas, graph, profile) = Fixture();
        var saved = new TestSavedRun();
        var yieldAdapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);
        var envelope = await YieldEnvelopeAsync(yieldAdapter, schemas, request);

        var resumeAdapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var registry = new NodeGraphRuntimeRegistry(schemas, [resumeAdapter]);

        var result = await registry.ResumeAsync(tamper(envelope), request, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.Code);
        Assert.False(resumeAdapter.ResumeCalled); // rejected before the resumed callback
        Assert.Equal(0, resumeAdapter.ResumeCount);
    }

    private static async Task AssertAdmissionRejectsBeforeCallbackAsync(Action<TestSavedRun> mutateSaved, string expectedCode)
    {
        var (schemas, graph, profile) = Fixture();
        var saved = new TestSavedRun();
        var yieldAdapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var request = MakeRequest(graph.GraphId, graph.Revision, graph.Nodes[0].NodeId);
        var envelope = await YieldEnvelopeAsync(yieldAdapter, schemas, request);

        mutateSaved(saved); // the authoritative persisted state now disagrees with the transported envelope

        var resumeAdapter = new RecordingContinuationAdapter(graph, profile, saved) { Mode = AdapterMode.YieldFirstThenResumeComplete };
        var registry = new NodeGraphRuntimeRegistry(schemas, [resumeAdapter]);

        var result = await registry.ResumeAsync(envelope, request, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.Code);
        Assert.False(resumeAdapter.ResumeCalled);
        Assert.Equal(0, resumeAdapter.ResumeCount);
    }

    private static async Task<GraphRuntimeContinuation> YieldEnvelopeAsync(
        RecordingContinuationAdapter adapter, NodeGraphSchemaRegistry schemas, GraphActivationRequest request)
    {
        adapter.Mode = AdapterMode.YieldFirstThenResumeComplete;
        var registry = new NodeGraphRuntimeRegistry(schemas, [adapter]);
        var first = await registry.ActivateAsync(request, TestContext.Current.CancellationToken);
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

    private static GraphActivationRequest MakeRequest(Guid graphId, long revision, Guid startNodeId, int stepLimit = 1000,
        JsonElement? variables = null) =>
        new("TestGraph", graphId, revision, startNodeId, "TestEvent", "session1", "entity1", "participant1",
            variables ?? JsonDocument.Parse("{\"input\":1}").RootElement, stepLimit);

    private sealed class TestSavedRun
    {
        public string RunId = "run-1";
        public GraphContinuationKind Kind = GraphContinuationKind.Wait;
        public Guid NodeId;
        public DateTimeOffset? ResumeAt;
        public JsonElement Condition = default;
        public JsonElement OwnerState = default;
        public int Consumed;
        public int Limit;
    }

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
        BadAckResume,
        FabricatedTransitionResume,
        DelayedAdmitYield,
    }

    private sealed class RecordingContinuationAdapter(GraphDocument graph, GraphCapabilityProfile profile, TestSavedRun saved)
        : INodeGraphOwnerBackedContinuationAdapter
    {
        private readonly Guid _waitNodeId = graph.Nodes[0].NodeId;
        private readonly List<JsonDocument> _ownedCaptureDocuments = [];

        public AdapterMode Mode { get; set; }
        public bool DenyAdmission { get; set; }
        public bool HoldAdmitResumeForDisposal { get; set; }
        public TaskCompletionSource<bool> AdmitResumeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> AdmitResumeGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ExecuteCount { get; private set; }
        public int ResumeCount { get; private set; }
        public bool ResumeCalled { get; private set; }
        public int LastResumeStartingConsumed { get; private set; }
        public string? LastResumeRunId { get; private set; }
        public List<GraphNode>? LastProvidedNodes { get; private set; }
        public HashSet<string>? LastProvidedAllowedNodes { get; private set; }
        public HashSet<string>? LastProvidedAllowedCaps { get; private set; }
        public List<GraphRuntimeTransition>? LastTransitions { get; private set; }
        public string GraphType => "TestGraph";
        public IReadOnlySet<string> SupportedEvents { get; } = new HashSet<string>(StringComparer.Ordinal) { "TestEvent" };

        public async ValueTask<GraphRuntimeAdmission?> AdmitAsync(GraphActivationRequest request, CancellationToken cancellationToken)
        {
            if (Mode == AdapterMode.DelayedAdmitYield)
            {
                // Hold admission truly asynchronously so the caller can dispose/mutate its source JSON in the window
                // between the call and the resumed work; the registry must already have cloned the variables.
                await Task.Delay(30, cancellationToken).ConfigureAwait(false);
            }
            LastProvidedNodes = new List<GraphNode>(graph.Nodes);
            LastProvidedAllowedNodes = new HashSet<string>(profile.AllowedNodeTypes, StringComparer.Ordinal);
            LastProvidedAllowedCaps = new HashSet<string>(profile.AllowedCapabilityIds, StringComparer.Ordinal);
            LastProvidedProfile = new GraphCapabilityProfile(profile.ProfileId, LastProvidedAllowedNodes, LastProvidedAllowedCaps);
            var copy = new GraphDocument(graph.SchemaVersion, graph.GraphId, graph.Revision, graph.CapabilityProfileId,
                graph.State, LastProvidedNodes, graph.Connections);
            return new(copy, LastProvidedProfile);
        }
        private GraphCapabilityProfile? LastProvidedProfile { get; set; }

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
            if (Mode is AdapterMode.YieldFirstThenResumeComplete or AdapterMode.ApprovalPendingResume or AdapterMode.DelayedAdmitYield)
            {
                budget.TryConsume(cancellationToken);
                budget.TryConsume(cancellationToken);
                budget.TryConsume(cancellationToken);
                var kind = Mode == AdapterMode.ApprovalPendingResume ? GraphContinuationKind.Approval : GraphContinuationKind.Wait;
                var at = kind == GraphContinuationKind.Approval ? PastApprovalAt : (DateTimeOffset?)null;
                var capture = Capture("run-1", kind, at);
                Persist(capture, budget);
                return ValueTask.FromResult(new GraphActivationResult(true, "waiting", [], budget.StepsConsumed)
                { Outcome = GraphRunOutcome.Yielded, Continuation = capture });
            }
            throw new InvalidOperationException($"Unsupported execute mode {Mode}.");
        }

        public async ValueTask<GraphContinuationAdmission?> AdmitResumeAsync(GraphRuntimeContinuation continuation,
            GraphActivationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DenyAdmission) return null;
            if (HoldAdmitResumeForDisposal)
            {
                // Signal the test that the adapter is now holding inside AdmitResumeAsync, so it may safely dispose
                // the caller's envelope document; the registry already detached a snapshot before this await.
                AdmitResumeEntered.TrySetResult(true);
                await AdmitResumeGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            return new GraphContinuationAdmission(saved.RunId, saved.Kind, saved.NodeId, saved.ResumeAt,
                saved.Condition, saved.OwnerState, saved.Limit, saved.Consumed);
        }

        public void ReleaseAdmitResume() => AdmitResumeGate.TrySetResult(true);

        public void DisposeOwnedCaptureDocuments()
        {
            foreach (var document in _ownedCaptureDocuments) document.Dispose();
            _ownedCaptureDocuments.Clear();
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
                // Time alone never auto-approves: still pending until an explicit owner-supplied decision.
                var capture = Capture("run-1", GraphContinuationKind.Approval, PastApprovalAt);
                return ValueTask.FromResult(new GraphActivationResult(true, "pending-approval", [], budget.StepsConsumed)
                { Outcome = GraphRunOutcome.Blocked, Continuation = capture });
            }
            if (Mode == AdapterMode.BadAckResume)
            {
                // Lie about consumed steps (lower than the real budget) to force NeedsRecovery uncertainty.
                return ValueTask.FromResult(new GraphActivationResult(true, "ok", [], budget.StepsConsumed - 1)
                { Outcome = GraphRunOutcome.Completed });
            }
            if (Mode == AdapterMode.FabricatedTransitionResume)
            {
                // Consumes nothing, yet fabricates a transition — not funded by a new step in this segment.
                return ValueTask.FromResult(new GraphActivationResult(true, "ok",
                    [new(request.StartNodeId, "completed", JsonDocument.Parse("{\"done\":true}").RootElement)],
                    budget.StepsConsumed) { Outcome = GraphRunOutcome.Completed });
            }
            throw new InvalidOperationException($"Unsupported resume mode {Mode}.");
        }

        private void Persist(GraphContinuationCapture capture, GraphExecutionBudget budget)
        {
            saved.RunId = capture.OwnerRunId;
            saved.Kind = capture.Kind;
            saved.NodeId = capture.NodeId;
            saved.ResumeAt = capture.ResumeAt;
            saved.Condition = capture.Condition;
            saved.OwnerState = capture.OwnerState;
            saved.Consumed = budget.StepsConsumed;
            saved.Limit = budget.Limit;
        }

        private GraphContinuationCapture Capture(string runId, GraphContinuationKind kind, DateTimeOffset? resumeAt)
        {
            var document = JsonDocument.Parse("{\"frontier\":[1,2]}");
            _ownedCaptureDocuments.Add(document);
            return new(runId, kind, _waitNodeId, resumeAt, default, document.RootElement);
        }

        private static GraphActivationResult Completed(GraphActivationRequest request, GraphExecutionBudget budget)
        {
            var steps = budget.StepsConsumed;
            IReadOnlyList<GraphRuntimeTransition> transitions = steps == 0
                ? []
                : [new(request.StartNodeId, "completed", JsonDocument.Parse("{\"done\":true}").RootElement)];
            return new GraphActivationResult(true, "ok", transitions, steps) { Outcome = GraphRunOutcome.Completed };
        }
    }
}
