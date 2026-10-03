using Xunit;

namespace Dulche.Runtime.Agents.Tests;

/// <summary>Coordinator reference-boundary fixtures only. The issuer/state/loop below are synthetic,
/// not a real Home session, model invocation, Den persistence or transport acceptance.</summary>
public sealed class AgentOriginalContextBindingTests
{
    [Fact]
    public async Task Exact_original_context_and_owner_step_are_passed_to_issuer_while_copied_step_cannot_get_admission()
    {
        using var lifetime = new CancellationTokenSource();
        var context = new AgentInvocationContext("actual-caller", "Connect");
        var issuer = new OriginalReferenceIssuer(context, lifetime.Token);
        var store = new SyntheticState(); var catalog = new SyntheticCatalog(); var loop = new RecordedLoop(issuer);
        var coordinator = new AgentExecutionService(store, catalog, issuer, loop);
        var started = await coordinator.StartAsync(Request(context));
        var committed = Assert.IsType<AgentExecutionSnapshot>(started.Value);
        Assert.Null(started.Error);
        Assert.Same(context, issuer.BoundContext);
        Assert.Same(committed.Run, issuer.BoundRun);
        var completed = await coordinator.ExecuteNextStepAsync(committed.Run.AgentRunId);
        Assert.Null(completed.Error);
        Assert.Equal(AgentRunState.Completed, Assert.IsType<AgentExecutionSnapshot>(completed.Value).Run.State);
        Assert.Equal(1, loop.Calls);
        Assert.Same(context, issuer.StepContext);
        Assert.Same(issuer.OriginalStep, loop.OriginalStep);
        var copied = await issuer.GetOriginalStepAdmissionAsync(Assert.IsType<AgentExecutionStep>(loop.OriginalStep) with { });
        Assert.Equal(AgentFailureCode.PermissionDenied, copied.Error?.Code);
    }

    [Fact]
    public async Task Persisted_run_identifiers_and_capabilities_cannot_restore_original_context_after_coordinator_restart()
    {
        using var lifetime = new CancellationTokenSource();
        var context = new AgentInvocationContext("actual-caller", "Connect", HomeGrantedCapabilities: new HashSet<string> { "web-search" });
        var issuer = new OriginalReferenceIssuer(context, lifetime.Token);
        var store = new SyntheticState(); var catalog = new SyntheticCatalog(); var loop = new RecordedLoop(issuer);
        var original = new AgentExecutionService(store, catalog, issuer, loop);
        var started = Assert.IsType<AgentExecutionSnapshot>((await original.StartAsync(Request(context))).Value);
        var restarted = new AgentExecutionService(store, catalog, issuer, loop);
        var refused = await restarted.ExecuteNextStepAsync(started.Run.AgentRunId);
        Assert.Equal(AgentRunState.Failed, Assert.IsType<AgentExecutionSnapshot>(refused.Value).Run.State);
        Assert.Equal(AgentFailureCode.PermissionDenied.ToString(), refused.Value!.Run.LastErrorCode);
        Assert.Equal(0, loop.Calls);
        Assert.Null(issuer.OriginalStep);
        Assert.Single(await store.ListRunsAsync("canonical-agent", 10));
    }

    [Fact]
    public async Task Missing_original_issuer_refuses_even_observed_capabilities_and_reports_known_durable_creation()
    {
        var context = new AgentInvocationContext("actual-caller", "Connect", HomeGrantedCapabilities: new HashSet<string> { "web-search" });
        var issuer = new ObservationsOnlyBroker(); var store = new SyntheticState();
        var loop = new RecordedLoop(issuer);
        var coordinator = new AgentExecutionService(store, new SyntheticCatalog(), issuer, loop);
        var refused = await coordinator.StartAsync(Request(context));
        var actual = Assert.IsType<AgentExecutionSnapshot>(refused.Value);
        var error = Assert.IsType<AgentFailure>(refused.Error);
        Assert.Equal(AgentFailureCode.PermissionDenied, error.Code);
        Assert.False(error.Retryable);
        Assert.Equal("true", error.Details!["canonicalRunCreated"]);
        Assert.Equal("false", error.Details["executionAdmitted"]);
        Assert.Equal(AgentRunState.Failed, actual.Run.State);
        Assert.Equal(actual.Run.AgentRunId, Assert.Single(await store.ListRunsAsync("canonical-agent", 10)).AgentRunId);
        Assert.Equal(0, loop.Calls);
    }

    [Fact]
    public async Task Refused_rebinding_of_existing_idempotent_run_preserves_original_state_and_never_dispatches()
    {
        using var lifetime = new CancellationTokenSource();
        var context = new AgentInvocationContext("actual-caller", "Connect");
        var issuer = new OriginalReferenceIssuer(context, lifetime.Token);
        var store = new SyntheticState(); var loop = new RecordedLoop(issuer);
        var coordinator = new AgentExecutionService(store, new SyntheticCatalog(), issuer, loop);
        var request = Request(context) with { IdempotencyKey = "same-original-request" };
        var started = Assert.IsType<AgentExecutionSnapshot>((await coordinator.StartAsync(request)).Value);
        issuer.RefuseBinding = true;
        var refused = await coordinator.StartAsync(request);
        Assert.Equal(AgentFailureCode.PermissionDenied, refused.Error?.Code);
        Assert.Equal("false", refused.Error?.Details?["canonicalRunCreated"]);
        var current = Assert.IsType<AgentExecutionSnapshot>(await store.ReadAsync(started.Run.AgentRunId));
        Assert.Equal(started.Revision, current.Revision);
        Assert.Equal(AgentRunState.Queued, current.Run.State);
        Assert.Single(current.Events);
        Assert.Equal(0, loop.Calls);
    }

    private static AgentRunRequest Request(AgentInvocationContext context) => new("canonical-agent", 1,
        "Original objective", AgentTriggerKind.User, "original-trigger", context, new(MaxToolCalls: 2));

    private sealed class SyntheticCatalog : IPersistentAgentCatalog
    {
        public ValueTask<AgentResult<PersistentAgentSnapshot>> GetAsync(string id, CancellationToken ct = default) =>
            ValueTask.FromResult(AgentResult<PersistentAgentSnapshot>.Success(new(id, 1, "Agent", true, null,
                new(Inherit: true), new(new HashSet<string>(), new HashSet<string>()),
                new(new HashSet<string>(), new HashSet<string>(), false, 1, 0, new(MaxToolCalls: 2)))));
        public ValueTask<AgentResult<PersistentAgentSnapshot>> GetRevisionAsync(string id, long revision, CancellationToken ct = default) => GetAsync(id, ct);
        public ValueTask<AgentResult<IReadOnlyList<PersistentAgentSnapshot>>> ResolveForAsync(AgentInvocationContext context, CancellationToken ct = default) =>
            ValueTask.FromResult(AgentResult<IReadOnlyList<PersistentAgentSnapshot>>.Success([]));
    }

    private class ObservationsOnlyBroker : IAgentPermissionBroker
    {
        public ValueTask<AgentResult<IReadOnlySet<string>>> ResolveCapabilitiesAsync(AgentCapabilityPolicy policy,
            AgentInvocationContext context, CancellationToken ct = default) =>
            ValueTask.FromResult(AgentResult<IReadOnlySet<string>>.Success(context.HomeGrantedCapabilities ?? new HashSet<string>()));
        public ValueTask<AgentResult<AgentApprovalRequirement>> RequestApprovalAsync(AgentApprovalRequirement request, CancellationToken ct = default) =>
            ValueTask.FromResult(AgentResult<AgentApprovalRequirement>.Failure(new(AgentFailureCode.ApprovalRequired, "Fixture has no approval.", request.AgentRunId)));
    }

    private sealed class OriginalReferenceIssuer(AgentInvocationContext original, CancellationToken lifetime) : ObservationsOnlyBroker, IAgentPermissionBroker
    {
        private readonly object token = new();
        public AgentInvocationContext? BoundContext { get; private set; }
        public AgentRunSnapshot? BoundRun { get; private set; }
        public AgentInvocationContext? StepContext { get; private set; }
        public AgentExecutionStep? OriginalStep { get; private set; }
        public bool RefuseBinding { get; set; }
        public ValueTask<AgentResult<bool>> BindOriginalRunAsync(AgentInvocationContext context, AgentRunSnapshot run, CancellationToken ct = default)
        {
            if (!ReferenceEquals(context, original) || RefuseBinding) return Refusal<bool>();
            BoundContext = context; BoundRun = run;
            return ValueTask.FromResult(AgentResult<bool>.Success(true));
        }
        public ValueTask<AgentResult<bool>> AuthorizeOriginalStepAsync(AgentInvocationContext context, AgentExecutionStep step, CancellationToken ct = default)
        {
            if (!ReferenceEquals(context, original) || BoundRun?.AgentRunId != step.AgentRunId) return Refusal<bool>();
            StepContext = context; OriginalStep = step;
            return ValueTask.FromResult(AgentResult<bool>.Success(true));
        }
        public ValueTask<AgentResult<AgentStepAdmission>> GetOriginalStepAdmissionAsync(AgentExecutionStep step, CancellationToken ct = default) =>
            ReferenceEquals(step, OriginalStep)
                ? ValueTask.FromResult(AgentResult<AgentStepAdmission>.Success(new(step.EffectiveCapabilities, token, lifetime)))
                : Refusal<AgentStepAdmission>();
        private static ValueTask<AgentResult<T>> Refusal<T>() => ValueTask.FromResult(AgentResult<T>.Failure(
            new(AgentFailureCode.PermissionDenied, "Exact fixture original reference required.", "fixture")));
    }

    private sealed class RecordedLoop(IAgentPermissionBroker issuer) : IAgentExecutionAdapter
    {
        public int Calls { get; private set; }
        public AgentExecutionStep? OriginalStep { get; private set; }
        public ValueTask<AgentResult<ResolvedExecutionTarget>> ResolveTargetAsync(PersistentAgentSnapshot agent,
            AgentInvocationContext context, AgentModelPolicy policy, CancellationToken ct = default) =>
            ValueTask.FromResult(AgentResult<ResolvedExecutionTarget>.Success(new("fixture-local", "original-session", "fixture-model",
                "fixture-provider", new HashSet<string>(), true)));
        public async ValueTask<AgentExecutionStepResult> ExecuteStepAsync(AgentExecutionStep step,
            Func<AgentExecutionEventEnvelope, CancellationToken, ValueTask> publish, CancellationToken ct = default)
        {
            var admission = await issuer.GetOriginalStepAdmissionAsync(step, ct);
            var value = Assert.IsType<AgentStepAdmission>(admission.Value);
            Assert.Null(admission.Error); Assert.True(value.OriginalLifetime.CanBeCanceled);
            Assert.False(value.OriginalLifetime.IsCancellationRequested);
            Calls++; OriginalStep = step;
            return new(AgentRunState.Completed, "synthetic loop result", null, null, [], [], [], [], [], [], AgentBudgetUsage.Empty, 100);
        }
        public ValueTask<AgentResult<AgentCheckpoint>> CheckpointAsync(AgentRunSnapshot run, CancellationToken ct = default) =>
            ValueTask.FromResult(AgentResult<AgentCheckpoint>.Failure(new(AgentFailureCode.CheckpointUnavailable, "Fixture has no checkpoint.", run.AgentRunId)));
        public ValueTask<AgentResult<IReadOnlyList<string>>> ReconcileSideEffectsAsync(AgentRunSnapshot run, IReadOnlyList<string> ids, CancellationToken ct = default) =>
            ValueTask.FromResult(AgentResult<IReadOnlyList<string>>.Success([]));
    }

    private sealed class SyntheticState : IAgentExecutionStateStore
    {
        private readonly Dictionary<string, AgentExecutionSnapshot> values = new(StringComparer.Ordinal);
        public ValueTask<AgentExecutionSnapshot?> ReadAsync(string id, CancellationToken ct = default) => ValueTask.FromResult(values.GetValueOrDefault(id));
        public ValueTask<IReadOnlyList<AgentRunSnapshot>> ListRunsAsync(string id, int limit, CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<AgentRunSnapshot>>(values.Values.Where(value => value.Run.AgentId == id).Select(value => value.Run).Take(limit).ToArray());
        public ValueTask<AgentExecutionSnapshot> CommitAsync(AgentExecutionChangeSet change, CancellationToken ct = default)
        {
            values.TryGetValue(change.AgentRunId, out var prior);
            Assert.Equal(change.ExpectedRevision, prior?.Revision ?? 0);
            var events = (prior?.Events ?? []).Concat(change.Events ?? []).ToArray();
            var result = new AgentExecutionSnapshot(1, change.ExpectedRevision + 1,
                change.Run ?? prior!.Run, change.QueueItems ?? prior?.QueueItems ?? [], change.Blockers ?? prior?.Blockers ?? [],
                change.Approvals ?? prior?.Approvals ?? [], change.Checkpoints ?? prior?.Checkpoints ?? [],
                change.Outputs ?? prior?.Outputs ?? [], change.Subagents ?? prior?.Subagents ?? [],
                change.Reservations ?? prior?.Reservations ?? [], events, change.CompletedConsequentialActionIds ?? new HashSet<string>(),
                change.UncertainConsequentialActionIds ?? new HashSet<string>(), events.Length == 0 ? 0 : events[^1].Sequence);
            values[change.AgentRunId] = result;
            return ValueTask.FromResult(result);
        }
    }
}
