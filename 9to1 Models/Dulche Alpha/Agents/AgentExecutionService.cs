namespace Dulche.Runtime.Agents;

/// <summary>Coordinates durable AgentRun lifecycle transitions around the runtime adapter.</summary>
public sealed class AgentExecutionService(
    IAgentExecutionStateStore store,
    IPersistentAgentCatalog catalog,
    IAgentPermissionBroker permissions,
    IAgentExecutionAdapter adapter,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _runLocks = new(StringComparer.Ordinal);

    public async ValueTask<AgentResult<AgentExecutionSnapshot>> StartAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.AgentId) || string.IsNullOrWhiteSpace(request.Objective) ||
            string.IsNullOrWhiteSpace(request.TriggerId) || string.IsNullOrWhiteSpace(request.Context.CallerId) ||
            string.IsNullOrWhiteSpace(request.Context.SurfaceId))
            return Fail<AgentExecutionSnapshot>(AgentFailureCode.InvalidInvocationContext, "Agent, objective, trigger, caller and surface are required.", "agent.start");
        if (request.Budget.Validate("agent.start.budget") is { } invalidBudget)
            return AgentResult<AgentExecutionSnapshot>.Failure(invalidBudget);

        var definitionResult = await catalog.GetRevisionAsync(request.AgentId, request.DefinitionRevision, cancellationToken);
        if (definitionResult.Error is { } definitionError) return AgentResult<AgentExecutionSnapshot>.Failure(definitionError);
        var definition = definitionResult.Value!;
        if (!definition.Enabled)
            return Fail<AgentExecutionSnapshot>(AgentFailureCode.AgentDisabled, "This Agent is disabled.", request.AgentId);

        var effectiveCapabilities = await permissions.ResolveCapabilitiesAsync(definition.CapabilityPolicy, request.Context, cancellationToken);
        if (effectiveCapabilities.Error is { } capabilityError) return AgentResult<AgentExecutionSnapshot>.Failure(capabilityError);
        var targetResult = await adapter.ResolveTargetAsync(definition, request.Context, definition.ModelPolicy, cancellationToken);
        if (targetResult.Error is { } targetError) return AgentResult<AgentExecutionSnapshot>.Failure(targetError);
        var target = targetResult.Value!;

        var idempotencyKey = request.IdempotencyKey;
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var recent = await store.ListRunsAsync(request.AgentId, 10_000, cancellationToken);
            var prior = recent.FirstOrDefault(run => run.IdempotencyKey == idempotencyKey);
            if (prior is not null)
            {
                if (prior.Objective != request.Objective || prior.DefinitionRevision != request.DefinitionRevision || prior.CallerId != request.Context.CallerId)
                    return Fail<AgentExecutionSnapshot>(AgentFailureCode.IdempotencyMismatch, "The run idempotency key was already used for a different invocation.", idempotencyKey);
                var priorSnapshot = await store.ReadAsync(prior.AgentRunId, cancellationToken);
                return priorSnapshot is null
                    ? Fail<AgentExecutionSnapshot>(AgentFailureCode.StateStoreUnavailable, "The idempotent run index points to a missing snapshot.", prior.AgentRunId, recoverable: true)
                    : AgentResult<AgentExecutionSnapshot>.Success(priorSnapshot);
            }
        }

        var now = _clock.GetUtcNow();
        var runId = Guid.NewGuid().ToString("N");
        var attemptId = Guid.NewGuid().ToString("N");
        var context = (request.Context.Context ?? []).ToArray();
        if (request.Handoff is { } handoff)
        {
            if (request.ParentAgentRunId is null || string.IsNullOrWhiteSpace(handoff.HandoffId) || string.IsNullOrWhiteSpace(handoff.Objective))
                return Fail<AgentExecutionSnapshot>(AgentFailureCode.DelegationDenied, "A delegated handoff requires a parent run and an identified, typed objective.", "agent.handoff");
            var delegatedCapabilities = new HashSet<string>(effectiveCapabilities.Value!, StringComparer.OrdinalIgnoreCase);
            delegatedCapabilities.IntersectWith(handoff.RequestedCapabilityScope);
            effectiveCapabilities = AgentResult<IReadOnlySet<string>>.Success(delegatedCapabilities);
            context = handoff.RequiredContext.ToArray();
        }

        var attempt = new AgentAttemptSnapshot(attemptId, 1, null, target.SessionId, target.EndpointId, target.ModelId,
            target.ProviderId, "new", [], now, null, null, AgentRunState.Queued, AgentBudgetUsage.Empty);
        var run = new AgentRunSnapshot(runId, definition.AgentId, definition.DefinitionRevision, request.Objective,
            request.Trigger, request.TriggerId, request.TaskId, request.QueueItemId, idempotencyKey,
            request.ParentAgentRunId, request.ParentSubagentId, [], AgentRunState.Queued,
            request.Context.CallerId, request.Context.SurfaceId, request.Context.SpaceId, request.Context.ProjectOrEntityId,
            target.SessionId, target.EndpointId, null, target.ModelId, target.ProviderId,
            effectiveCapabilities.Value!, context, definition.DelegationPolicy,
            AgentBudgetLimits.Narrow(definition.DelegationPolicy.Budget, request.Budget), AgentBudgetUsage.Empty,
            [], [], [], [], null, null, now, null, null, null, 0, attemptId, [attempt]);
        var startedEvent = Event(run, 1, now, "run.queued", request.TriggerId);
        var initial = new AgentExecutionChangeSet(runId, 0, "create:" + (idempotencyKey ?? runId), Run: run,
            Events: [startedEvent]);
        if (initial.Validate() is { } invalid) return AgentResult<AgentExecutionSnapshot>.Failure(invalid);
        try
        {
            var created = await store.CommitAsync(initial, cancellationToken);
            return AgentResult<AgentExecutionSnapshot>.Success(created);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Fail<AgentExecutionSnapshot>(AgentFailureCode.StateStoreUnavailable, "The AgentRun could not be durably created.", runId, recoverable: true,
                details: new Dictionary<string, string> { ["exception"] = exception.GetType().Name });
        }
    }

    public ValueTask<AgentResult<AgentExecutionSnapshot>> GetAsync(string agentRunId, CancellationToken cancellationToken = default) =>
        ReadResultAsync(agentRunId, cancellationToken);

    public ValueTask<AgentResult<AgentExecutionSnapshot>> PauseAsync(string agentRunId, CancellationToken cancellationToken = default) =>
        TransitionAsync(agentRunId, AgentRunState.Paused, "run.paused", cancellationToken);

    public ValueTask<AgentResult<AgentExecutionSnapshot>> StopAsync(string agentRunId, CancellationToken cancellationToken = default) =>
        TransitionAsync(agentRunId, AgentRunState.Stopped, "run.stopped", cancellationToken);

    public ValueTask<AgentResult<AgentExecutionSnapshot>> CancelAsync(string agentRunId, CancellationToken cancellationToken = default) =>
        TransitionAsync(agentRunId, AgentRunState.Cancelled, "run.cancelled", cancellationToken);

    public async ValueTask<AgentResult<AgentExecutionSnapshot>> ResumeAsync(string agentRunId, CancellationToken cancellationToken = default)
    {
        var result = await TransitionAsync(agentRunId, AgentRunState.Running, "run.resumed", cancellationToken);
        if (result.Error is not null) return result;
        return await ExecuteNextStepAsync(agentRunId, cancellationToken);
    }

    public async ValueTask<AgentResult<AgentExecutionSnapshot>> ExecuteNextStepAsync(string agentRunId, CancellationToken cancellationToken = default)
    {
        var gate = _runLocks.GetOrAdd(agentRunId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await store.ReadAsync(agentRunId, cancellationToken);
            if (current is null) return Fail<AgentExecutionSnapshot>(AgentFailureCode.AgentRunNotFound, "AgentRun was not found.", agentRunId);
            if (current.Run.State is not (AgentRunState.Queued or AgentRunState.Running or AgentRunState.Waiting or AgentRunState.Recovered))
                return AgentResult<AgentExecutionSnapshot>.Success(current);

            var now = _clock.GetUtcNow();
            var running = current.Run.State == AgentRunState.Running
                ? AgentResult<AgentRunSnapshot>.Success(current.Run)
                : AgentRunStateMachine.Transition(current.Run, AgentRunState.Running, now);
            if (running.Error is { } transitionError) return AgentResult<AgentExecutionSnapshot>.Failure(transitionError);
            var execution = await CommitRunAsync(current, running.Value!, "run.dispatch:" + Guid.NewGuid().ToString("N"), "run.started", now, cancellationToken);
            if (execution.Error is not null) return execution;
            current = execution.Value!;

            var resolved = await catalog.GetRevisionAsync(current.Run.AgentId, current.Run.DefinitionRevision, cancellationToken);
            if (resolved.Error is { } catalogError) return await FailRunAsync(current, catalogError, cancellationToken);
            var definition = resolved.Value!;
            var requestContext = new AgentInvocationContext(current.Run.CallerId, current.Run.SurfaceId, current.Run.SpaceId,
                current.Run.ProjectOrEntityId, current.Run.ContextSnapshot, current.Run.EffectiveCapabilities,
                current.Run.EffectiveCapabilities, current.Run.EffectiveCapabilities, current.Run.EffectiveCapabilities);
            var step = new AgentExecutionStep(current.Run.AgentRunId, current.Run.CurrentAttemptId, current.Run.Objective,
                current.Run.CallerId, current.Run.SessionId,
                new(current.Run.EndpointId ?? "", current.Run.SessionId, current.Run.ModelId ?? "", current.Run.ProviderId ?? "",
                    current.Run.EffectiveCapabilities, false), current.Run.EffectiveCapabilities,
                definition.AllowedTools ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase), current.Run.BudgetLimits,
                current.Run.ContextSnapshot, current.CompletedConsequentialActionIds, current.LastEventSequence, current.Run.CheckpointId);

            AgentExecutionStepResult result;
            try
            {
                result = await adapter.ExecuteStepAsync(step, async (executionEvent, token) =>
                {
                    var published = await PublishEventAsync(agentRunId, executionEvent, token);
                    if (published.Error is { } error)
                        throw new InvalidOperationException($"Execution event persistence failed: {error.Code}.");
                }, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var latest = await store.ReadAsync(agentRunId, CancellationToken.None);
                return latest is null ? Fail<AgentExecutionSnapshot>(AgentFailureCode.StateStoreUnavailable, "Run disappeared during cancellation.", agentRunId) : AgentResult<AgentExecutionSnapshot>.Success(latest);
            }
            catch (Exception exception)
            {
                return await FailRunAsync(current, new(AgentFailureCode.ExecutionFailed, "The execution adapter failed.", agentRunId, true, true,
                    new Dictionary<string, string> { ["exception"] = exception.GetType().Name }), cancellationToken);
            }

            return await ApplyStepResultAsync(agentRunId, result, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async ValueTask<AgentResult<AgentExecutionSnapshot>> RecoverAsync(string agentRunId, CancellationToken cancellationToken = default)
    {
        var current = await store.ReadAsync(agentRunId, cancellationToken);
        if (current is null) return Fail<AgentExecutionSnapshot>(AgentFailureCode.AgentRunNotFound, "AgentRun was not found.", agentRunId);
        if (current.Run.State is not (AgentRunState.Running or AgentRunState.Waiting or AgentRunState.Recovering))
            return AgentResult<AgentExecutionSnapshot>.Success(current);

        var now = _clock.GetUtcNow();
        var recovery = current.Run with { State = AgentRunState.Recovering, RecoveryState = "reconciling", Revision = current.Run.Revision + 1 };
        var committed = await CommitRunAsync(current, recovery, "recover.begin:" + Guid.NewGuid().ToString("N"), "run.recovery.started", now, cancellationToken);
        if (committed.Error is not null) return committed;
        current = committed.Value!;
        var reconciled = await adapter.ReconcileSideEffectsAsync(current.Run, current.UncertainConsequentialActionIds.ToArray(), cancellationToken);
        if (reconciled.Error is not null || reconciled.Value is null)
            return await CreateRecoveryBlockerAsync(current, reconciled.Error ?? new(AgentFailureCode.RunRecoveryFailed, "Side-effect reconciliation did not return a result.", agentRunId, true), cancellationToken);

        var remainingUncertain = current.UncertainConsequentialActionIds.Except(reconciled.Value, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        if (remainingUncertain.Count != 0)
            return await CreateRecoveryBlockerAsync(current, new(AgentFailureCode.RunRecoveryFailed, "One or more consequential actions remain uncertain and will not be replayed.", agentRunId, true,
                Details: new Dictionary<string, string> { ["uncertainActionIds"] = string.Join(",", remainingUncertain) }), cancellationToken);

        var checkpoint = current.Run.CheckpointId is null ? null : current.Checkpoints.FirstOrDefault(item => item.CheckpointId == current.Run.CheckpointId);
        var canContinue = checkpoint is not null && checkpoint.ModelId == current.Run.ModelId &&
            checkpoint.BackendId == current.Run.EndpointId && checkpoint.ContinuationKind is "exact" or "portable-exact";
        var resumed = current.Run with
        {
            State = canContinue ? AgentRunState.Recovered : AgentRunState.CannotRecover,
            RecoveryState = canContinue ? "exact-checkpoint" : "continuation-unavailable",
            Revision = current.Run.Revision + 1,
            LastErrorCode = canContinue ? null : AgentFailureCode.CheckpointUnavailable.ToString(),
            LastErrorMessage = canContinue ? null : "No compatible checkpoint is available; consequential actions were not replayed."
        };
        return await CommitRunAsync(current, resumed, "recover.finish:" + Guid.NewGuid().ToString("N"), canContinue ? "run.recovered" : "run.cannot_recover", now, cancellationToken);
    }

    private async ValueTask<AgentResult<AgentExecutionSnapshot>> ApplyStepResultAsync(string runId, AgentExecutionStepResult result, CancellationToken cancellationToken)
    {
        var current = await store.ReadAsync(runId, cancellationToken);
        if (current is null) return Fail<AgentExecutionSnapshot>(AgentFailureCode.AgentRunNotFound, "AgentRun was not found.", runId);
        if (result.State == AgentRunState.AwaitingApproval && result.Approvals.Count == 0)
            return await FailRunAsync(current, new(AgentFailureCode.ExecutionFailed, "The adapter requested approval without a persisted approval requirement.", runId), cancellationToken);
        if (result.State == AgentRunState.Blocked && result.Blockers.Count == 0)
            return await FailRunAsync(current, new(AgentFailureCode.ExecutionFailed, "The adapter returned Blocked without a blocker record.", runId), cancellationToken);

        var now = _clock.GetUtcNow();
        var nextRun = current.Run with
        {
            State = result.State,
            ActionGraphId = result.ActionGraphId ?? current.Run.ActionGraphId,
            CheckpointId = result.CheckpointId ?? current.Run.CheckpointId,
            OutputReferenceIds = current.Run.OutputReferenceIds.Concat(result.Outputs.Select(output => output.Id)).Distinct(StringComparer.Ordinal).ToArray(),
            BlockerIds = current.Run.BlockerIds.Concat(result.Blockers.Select(blocker => blocker.BlockerId)).Distinct(StringComparer.Ordinal).ToArray(),
            ApprovalIds = current.Run.ApprovalIds.Concat(result.Approvals.Select(approval => approval.ApprovalId)).Distinct(StringComparer.Ordinal).ToArray(),
            BudgetUsage = result.Usage,
            KnownProgressPercent = result.State == AgentRunState.Completed ? 100 : result.KnownProgressPercent ?? current.Run.KnownProgressPercent,
            FinishedAtUtc = result.State is AgentRunState.Completed or AgentRunState.Failed or AgentRunState.Stopped or AgentRunState.Cancelled ? now : null,
            LastErrorCode = result.Failure?.Code.ToString(),
            LastErrorMessage = result.Failure?.Message,
            Revision = current.Run.Revision + 1
        };
        var newEvents = result.Events.Select((item, index) => item with { RootRequestId = runId, Sequence = current.LastEventSequence + index + 1 }).ToArray();
        var change = new AgentExecutionChangeSet(runId, current.Revision, "step.result:" + Guid.NewGuid().ToString("N"),
            Run: nextRun, Blockers: result.Blockers, Approvals: result.Approvals, Outputs: result.Outputs,
            Events: newEvents, CompletedConsequentialActionIds: result.CompletedConsequentialActionIds.ToHashSet(StringComparer.Ordinal),
            UncertainConsequentialActionIds: result.UncertainConsequentialActionIds.ToHashSet(StringComparer.Ordinal));
        try { return AgentResult<AgentExecutionSnapshot>.Success(await store.CommitAsync(change, cancellationToken)); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return Fail<AgentExecutionSnapshot>(AgentFailureCode.StateStoreUnavailable, "The execution result could not be committed.", runId, true,
            details: new Dictionary<string, string> { ["exception"] = exception.GetType().Name }); }
    }

    private async ValueTask<AgentResult<AgentExecutionSnapshot>> PublishEventAsync(string runId, AgentExecutionEventEnvelope item, CancellationToken cancellationToken)
    {
        var current = await store.ReadAsync(runId, cancellationToken);
        if (current is null) return Fail<AgentExecutionSnapshot>(AgentFailureCode.AgentRunNotFound, "AgentRun was not found.", runId);
        var sequenced = item with { RootRequestId = runId, Sequence = current.LastEventSequence + 1 };
        try
        {
            var change = new AgentExecutionChangeSet(runId, current.Revision, "event:" + sequenced.ExecutionId + ":" + sequenced.Sequence,
                Events: [sequenced]);
            return AgentResult<AgentExecutionSnapshot>.Success(await store.CommitAsync(change, cancellationToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return Fail<AgentExecutionSnapshot>(AgentFailureCode.StateStoreUnavailable, "The execution event could not be persisted.", runId, true,
            details: new Dictionary<string, string> { ["exception"] = exception.GetType().Name }); }
    }

    private async ValueTask<AgentResult<AgentExecutionSnapshot>> TransitionAsync(string runId, AgentRunState target, string kind, CancellationToken cancellationToken)
    {
        var current = await store.ReadAsync(runId, cancellationToken);
        if (current is null) return Fail<AgentExecutionSnapshot>(AgentFailureCode.AgentRunNotFound, "AgentRun was not found.", runId);
        var transitioned = AgentRunStateMachine.Transition(current.Run, target, _clock.GetUtcNow());
        if (transitioned.Error is { } error) return AgentResult<AgentExecutionSnapshot>.Failure(error);
        return await CommitRunAsync(current, transitioned.Value!, kind + ":" + Guid.NewGuid().ToString("N"), kind, _clock.GetUtcNow(), cancellationToken);
    }

    private async ValueTask<AgentResult<AgentExecutionSnapshot>> CommitRunAsync(AgentExecutionSnapshot current, AgentRunSnapshot run,
        string operationId, string eventKind, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var eventRecord = Event(run, current.LastEventSequence + 1, now, eventKind, run.State.ToString());
        try
        {
            var changes = new AgentExecutionChangeSet(run.AgentRunId, current.Revision, operationId, Run: run, Events: [eventRecord]);
            return AgentResult<AgentExecutionSnapshot>.Success(await store.CommitAsync(changes, cancellationToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return Fail<AgentExecutionSnapshot>(AgentFailureCode.StateStoreUnavailable, "The AgentRun state transition could not be committed.", run.AgentRunId, true,
            details: new Dictionary<string, string> { ["exception"] = exception.GetType().Name }); }
    }

    private async ValueTask<AgentResult<AgentExecutionSnapshot>> FailRunAsync(AgentExecutionSnapshot current, AgentFailure failure, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var failed = current.Run with { State = AgentRunState.Failed, FinishedAtUtc = now, LastErrorCode = failure.Code.ToString(), LastErrorMessage = failure.Message, Revision = current.Run.Revision + 1 };
        return await CommitRunAsync(current, failed, "run.failed:" + Guid.NewGuid().ToString("N"), "run.failed", now, cancellationToken);
    }

    private async ValueTask<AgentResult<AgentExecutionSnapshot>> CreateRecoveryBlockerAsync(AgentExecutionSnapshot current, AgentFailure failure, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var blocker = new AgentBlocker(Guid.NewGuid().ToString("N"), current.Run.AgentRunId, failure.Code, "recovery", failure.Message,
            now, AgentBlockerState.AwaitingHuman, false, true, "Review uncertain consequential actions and select a safe recovery path.",
            [], [], [], null, null, 0);
        var blocked = current.Run with { State = AgentRunState.Blocked, RecoveryState = "human-reconciliation-required", BlockerIds = current.Run.BlockerIds.Append(blocker.BlockerId).ToArray(), Revision = current.Run.Revision + 1 };
        var record = Event(blocked, current.LastEventSequence + 1, now, "run.recovery.blocked", failure.Code.ToString());
        try
        {
            var changes = new AgentExecutionChangeSet(blocked.AgentRunId, current.Revision, "recover.blocked:" + Guid.NewGuid().ToString("N"),
                Run: blocked, Blockers: [blocker], Events: [record], UncertainConsequentialActionIds: current.UncertainConsequentialActionIds);
            return AgentResult<AgentExecutionSnapshot>.Success(await store.CommitAsync(changes, cancellationToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return Fail<AgentExecutionSnapshot>(AgentFailureCode.StateStoreUnavailable, "The recovery blocker could not be committed.", blocked.AgentRunId, true,
            details: new Dictionary<string, string> { ["exception"] = exception.GetType().Name }); }
    }

    private async ValueTask<AgentResult<AgentExecutionSnapshot>> ReadResultAsync(string runId, CancellationToken cancellationToken)
    {
        var snapshot = await store.ReadAsync(runId, cancellationToken);
        return snapshot is null
            ? Fail<AgentExecutionSnapshot>(AgentFailureCode.AgentRunNotFound, "AgentRun was not found.", runId)
            : AgentResult<AgentExecutionSnapshot>.Success(snapshot);
    }

    private static AgentExecutionEventEnvelope Event(AgentRunSnapshot run, long sequence, DateTimeOffset now, string kind, string? detail) =>
        new(run.CurrentAttemptId, run.ParentAgentRunId, run.ParentSubagentId, run.AgentRunId, run.CurrentAttemptId,
            sequence, now, kind, detail);

    private static AgentResult<T> Fail<T>(AgentFailureCode code, string message, string target, bool recoverable = false,
        IReadOnlyDictionary<string, string>? details = null) => AgentResult<T>.Failure(new(code, message, target, recoverable, false, details));
}
