namespace Dulche.Runtime.Agents;

public sealed record AgentExecutionSnapshot(
    int SchemaVersion,
    long Revision,
    AgentRunSnapshot Run,
    IReadOnlyList<AgentQueueItem> QueueItems,
    IReadOnlyList<AgentBlocker> Blockers,
    IReadOnlyList<AgentApprovalRequirement> Approvals,
    IReadOnlyList<AgentCheckpoint> Checkpoints,
    IReadOnlyList<AgentOutputReference> Outputs,
    IReadOnlyList<SubagentRunSnapshot> Subagents,
    IReadOnlyList<BudgetReservation> Reservations,
    IReadOnlyList<AgentExecutionEventEnvelope> Events,
    IReadOnlySet<string> CompletedConsequentialActionIds,
    IReadOnlySet<string> UncertainConsequentialActionIds,
    long LastEventSequence)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record AgentExecutionChangeSet(
    string AgentRunId,
    long ExpectedRevision,
    string OperationId,
    AgentRunSnapshot? Run = null,
    IReadOnlyList<AgentQueueItem>? QueueItems = null,
    IReadOnlyList<AgentBlocker>? Blockers = null,
    IReadOnlyList<AgentApprovalRequirement>? Approvals = null,
    IReadOnlyList<AgentCheckpoint>? Checkpoints = null,
    IReadOnlyList<AgentOutputReference>? Outputs = null,
    IReadOnlyList<SubagentRunSnapshot>? Subagents = null,
    IReadOnlyList<BudgetReservation>? Reservations = null,
    IReadOnlyList<AgentExecutionEventEnvelope>? Events = null,
    IReadOnlySet<string>? CompletedConsequentialActionIds = null,
    IReadOnlySet<string>? UncertainConsequentialActionIds = null)
{
    public AgentFailure? Validate()
    {
        if (string.IsNullOrWhiteSpace(AgentRunId) || string.IsNullOrWhiteSpace(OperationId))
            return new(AgentFailureCode.InvalidInvocationContext, "A run identity and idempotent operation ID are required.", "execution.commit");
        if (ExpectedRevision < 0)
            return new(AgentFailureCode.InvalidInvocationContext, "Expected revision cannot be negative.", "execution.commit.expectedRevision");
        if (Run is not null && Run.AgentRunId != AgentRunId)
            return new(AgentFailureCode.InvalidInvocationContext, "The transaction cannot change its root AgentRun identity.", "execution.commit.run");
        if (Events is { Count: > 0 } events && events.Any(value => value.RootRequestId != AgentRunId))
            return new(AgentFailureCode.InvalidInvocationContext, "Every execution event must belong to the transaction root.", "execution.commit.events");
        return null;
    }
}

public interface IAgentExecutionStateStore
{
    ValueTask<AgentExecutionSnapshot?> ReadAsync(string agentRunId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<AgentRunSnapshot>> ListRunsAsync(string agentId, int limit, CancellationToken cancellationToken = default);
    ValueTask<AgentExecutionSnapshot> CommitAsync(AgentExecutionChangeSet changes, CancellationToken cancellationToken = default);
}

public sealed record ResolvedExecutionTarget(
    string EndpointId,
    string SessionId,
    string ModelId,
    string ProviderId,
    IReadOnlySet<string> Capabilities,
    bool IsLocal,
    string? ArtifactRevision = null);

public sealed record AgentExecutionStep(
    string AgentRunId,
    string AttemptId,
    string Prompt,
    string CallerId,
    string SessionId,
    ResolvedExecutionTarget Target,
    IReadOnlySet<string> EffectiveCapabilities,
    IReadOnlySet<string> EffectiveTools,
    AgentBudgetLimits RemainingBudget,
    IReadOnlyList<AgentContextReference> ContextSnapshot,
    IReadOnlySet<string> CompletedConsequentialActionIds,
    long EventCursor,
    string? ResumeCheckpointId);

public sealed record AgentExecutionStepResult(
    AgentRunState State,
    string? Text,
    string? CheckpointId,
    string? ActionGraphId,
    IReadOnlyList<AgentOutputReference> Outputs,
    IReadOnlyList<AgentApprovalRequirement> Approvals,
    IReadOnlyList<AgentBlocker> Blockers,
    IReadOnlyList<AgentExecutionEventEnvelope> Events,
    IReadOnlyList<string> CompletedConsequentialActionIds,
    IReadOnlyList<string> UncertainConsequentialActionIds,
    AgentBudgetUsage Usage,
    int? KnownProgressPercent,
    AgentFailure? Failure = null);

public interface IAgentExecutionAdapter
{
    ValueTask<AgentResult<ResolvedExecutionTarget>> ResolveTargetAsync(
        PersistentAgentSnapshot agent,
        AgentInvocationContext context,
        AgentModelPolicy requestPolicy,
        CancellationToken cancellationToken = default);

    ValueTask<AgentExecutionStepResult> ExecuteStepAsync(
        AgentExecutionStep step,
        Func<AgentExecutionEventEnvelope, CancellationToken, ValueTask> publish,
        CancellationToken cancellationToken = default);

    ValueTask<AgentResult<AgentCheckpoint>> CheckpointAsync(
        AgentRunSnapshot run,
        CancellationToken cancellationToken = default);

    ValueTask<AgentResult<IReadOnlyList<string>>> ReconcileSideEffectsAsync(
        AgentRunSnapshot run,
        IReadOnlyList<string> uncertainActionIds,
        CancellationToken cancellationToken = default);
}

public interface IPersistentAgentCatalog
{
    ValueTask<AgentResult<PersistentAgentSnapshot>> GetAsync(string agentId, CancellationToken cancellationToken = default);
    ValueTask<AgentResult<PersistentAgentSnapshot>> GetRevisionAsync(string agentId, long definitionRevision, CancellationToken cancellationToken = default);
    ValueTask<AgentResult<IReadOnlyList<PersistentAgentSnapshot>>> ResolveForAsync(AgentInvocationContext context, CancellationToken cancellationToken = default);
}

public interface IAgentPermissionBroker
{
    ValueTask<AgentResult<IReadOnlySet<string>>> ResolveCapabilitiesAsync(
        AgentCapabilityPolicy agentPolicy,
        AgentInvocationContext context,
        CancellationToken cancellationToken = default);

    ValueTask<AgentResult<AgentApprovalRequirement>> RequestApprovalAsync(
        AgentApprovalRequirement request,
        CancellationToken cancellationToken = default);
}

public interface IAgentToolDispatcher
{
    ValueTask<AgentResult<AgentToolExecutionResult>> ExecuteAsync(
        AgentToolCall call,
        AgentToolScope scope,
        CancellationToken cancellationToken = default);
}

public sealed record AgentToolCall(
    string ToolCallId,
    string ToolId,
    string ToolVersion,
    string ArgumentsJson,
    long Sequence,
    bool Consequential,
    string? IdempotencyKey);

public sealed record AgentToolScope(
    string CallerId,
    string AgentRunId,
    string AttemptId,
    string? ParentSubagentId,
    string? ProjectOrEntityId,
    IReadOnlySet<string> AllowedToolIds,
    IReadOnlySet<string> GrantedCapabilities,
    AgentCapabilityPolicy Policy,
    IReadOnlyList<AgentContextReference> ContextSnapshot);

public sealed record AgentToolExecutionResult(
    string ToolCallId,
    bool Succeeded,
    string? ResultJson,
    AgentFailure? Error,
    bool SideEffectCompleted,
    string? CanonicalOutputId,
    string? ActionEventId,
    AgentBudgetUsage Usage);

public interface IAgentApprovalService
{
    ValueTask<AgentResult<AgentApprovalRequirement>> RequestAsync(
        AgentApprovalRequirement approval,
        CancellationToken cancellationToken = default);

    ValueTask<AgentResult<AgentApprovalRequirement>> GetAsync(string approvalId, CancellationToken cancellationToken = default);
}

public interface IAgentExecutionEventSource
{
    IAsyncEnumerable<AgentExecutionEventEnvelope> ReadAsync(
        string agentRunId,
        long afterSequence = 0,
        CancellationToken cancellationToken = default);
}

public interface IAgentQueueScheduler
{
    ValueTask<AgentResult<AgentQueueItem>> EnqueueAsync(
        AgentQueueItem item,
        CancellationToken cancellationToken = default);

    ValueTask<AgentResult<IReadOnlyList<AgentQueueItem>>> ListAsync(
        string agentId,
        CancellationToken cancellationToken = default);

    ValueTask<AgentResult<IReadOnlyList<AgentQueueItem>>> ReorderAsync(
        string agentId,
        IReadOnlyList<AgentQueueReorder> order,
        CancellationToken cancellationToken = default);

    ValueTask<AgentResult<AgentQueueItem?>> DequeueReadyAsync(
        string agentId,
        CancellationToken cancellationToken = default);
}

public static class AgentRunStateMachine
{
    private static readonly IReadOnlyDictionary<AgentRunState, IReadOnlySet<AgentRunState>> Allowed =
        new Dictionary<AgentRunState, IReadOnlySet<AgentRunState>>
        {
            [AgentRunState.Queued] = Set(AgentRunState.Running, AgentRunState.Blocked, AgentRunState.AwaitingApproval, AgentRunState.Cancelled, AgentRunState.Stopped, AgentRunState.Failed),
            [AgentRunState.Running] = Set(AgentRunState.Waiting, AgentRunState.Blocked, AgentRunState.Paused, AgentRunState.AwaitingApproval, AgentRunState.Completed, AgentRunState.Failed, AgentRunState.Stopped, AgentRunState.Cancelled, AgentRunState.Recovering),
            [AgentRunState.Waiting] = Set(AgentRunState.Running, AgentRunState.Blocked, AgentRunState.Paused, AgentRunState.AwaitingApproval, AgentRunState.Failed, AgentRunState.Stopped, AgentRunState.Cancelled, AgentRunState.Recovering),
            [AgentRunState.Blocked] = Set(AgentRunState.Queued, AgentRunState.Running, AgentRunState.Paused, AgentRunState.Cancelled, AgentRunState.Stopped, AgentRunState.Failed),
            [AgentRunState.Paused] = Set(AgentRunState.Queued, AgentRunState.Running, AgentRunState.Blocked, AgentRunState.AwaitingApproval, AgentRunState.Stopped, AgentRunState.Cancelled, AgentRunState.Failed),
            [AgentRunState.AwaitingApproval] = Set(AgentRunState.Queued, AgentRunState.Running, AgentRunState.Blocked, AgentRunState.Paused, AgentRunState.Stopped, AgentRunState.Cancelled, AgentRunState.Failed),
            [AgentRunState.Recovering] = Set(AgentRunState.Recovered, AgentRunState.CannotRecover, AgentRunState.Blocked, AgentRunState.Failed),
            [AgentRunState.Recovered] = Set(AgentRunState.Queued, AgentRunState.Running, AgentRunState.Blocked, AgentRunState.Paused, AgentRunState.Failed),
            [AgentRunState.CannotRecover] = Set(AgentRunState.Blocked, AgentRunState.Failed),
            [AgentRunState.Completed] = Set(),
            [AgentRunState.Failed] = Set(),
            [AgentRunState.Stopped] = Set(),
            [AgentRunState.Cancelled] = Set()
        };

    public static AgentResult<AgentRunSnapshot> Transition(AgentRunSnapshot run, AgentRunState next, DateTimeOffset nowUtc)
    {
        if (run.State == next) return AgentResult<AgentRunSnapshot>.Success(run);
        if (!Allowed.TryGetValue(run.State, out var states) || !states.Contains(next))
            return AgentResult<AgentRunSnapshot>.Failure(new(AgentFailureCode.RunNotResumable,
                $"A run cannot move from {run.State} to {next}.", run.AgentRunId));

        var terminal = next is AgentRunState.Completed or AgentRunState.Failed or AgentRunState.Stopped or AgentRunState.Cancelled or AgentRunState.CannotRecover;
        var started = run.StartedAtUtc ?? (next == AgentRunState.Running ? nowUtc : null);
        return AgentResult<AgentRunSnapshot>.Success(run with
        {
            State = next,
            StartedAtUtc = started,
            FinishedAtUtc = terminal ? nowUtc : null,
            KnownProgressPercent = next == AgentRunState.Completed ? 100 : run.KnownProgressPercent,
            Revision = checked(run.Revision + 1)
        });
    }

    public static AgentResult<AgentRunSnapshot> Retry(AgentRunSnapshot run, DateTimeOffset nowUtc, string operationId)
    {
        if (run.State is not (AgentRunState.Failed or AgentRunState.CannotRecover))
            return AgentResult<AgentRunSnapshot>.Failure(new(AgentFailureCode.RunNotResumable, "Only a failed or unrecoverable run can be retried.", run.AgentRunId));
        if (string.IsNullOrWhiteSpace(operationId))
            return AgentResult<AgentRunSnapshot>.Failure(new(AgentFailureCode.InvalidInvocationContext, "A retry operation ID is required.", run.AgentRunId));

        var previous = run.Attempts.LastOrDefault();
        var attempt = new AgentAttemptSnapshot(
            Guid.NewGuid().ToString("N"),
            run.Attempts.Count + 1,
            null,
            run.SessionId,
            run.EndpointId,
            run.ModelId,
            run.ProviderId,
            "restart-from-checkpoint",
            run.Attempts.SelectMany(item => item.CompletedActionIds).Distinct(StringComparer.Ordinal).ToArray(),
            nowUtc,
            null,
            null,
            AgentRunState.Queued,
            AgentBudgetUsage.Empty);
        var attempts = run.Attempts.Append(attempt).ToArray();
        return AgentResult<AgentRunSnapshot>.Success(run with
        {
            State = AgentRunState.Queued,
            FinishedAtUtc = null,
            CurrentAttemptId = attempt.AttemptId,
            Attempts = attempts,
            LastErrorCode = null,
            LastErrorMessage = null,
            Revision = checked(run.Revision + 1)
        });
    }

    private static IReadOnlySet<AgentRunState> Set(params AgentRunState[] values) => values.ToHashSet();
}

public static class AgentQueueOrdering
{
    public static AgentResult<IReadOnlyList<AgentQueueItem>> Reorder(
        IReadOnlyList<AgentQueueItem> current,
        IReadOnlyList<AgentQueueReorder> requested,
        string agentId,
        DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            return AgentResult<IReadOnlyList<AgentQueueItem>>.Failure(new(AgentFailureCode.InvalidInvocationContext, "Agent identity is required.", "agentId"));
        if (requested.Count != current.Count || requested.Select(item => item.QueueItemId).Distinct(StringComparer.Ordinal).Count() != requested.Count)
            return AgentResult<IReadOnlyList<AgentQueueItem>>.Failure(new(AgentFailureCode.InvalidInvocationContext, "Reorder must include each queue item exactly once.", agentId));
        if (requested.Any(item => item.Sequence < 0) || requested.Select(item => item.Sequence).Distinct().Count() != requested.Count)
            return AgentResult<IReadOnlyList<AgentQueueItem>>.Failure(new(AgentFailureCode.InvalidInvocationContext, "Queue sequence values must be unique and non-negative.", agentId));
        if (current.Select(item => item.QueueItemId).Distinct(StringComparer.Ordinal).Count() != current.Count)
            return AgentResult<IReadOnlyList<AgentQueueItem>>.Failure(new(AgentFailureCode.InvalidInvocationContext, "The current queue contains duplicate identities.", agentId));

        var currentById = current.ToDictionary(item => item.QueueItemId, StringComparer.Ordinal);
        if (requested.Any(item => !currentById.ContainsKey(item.QueueItemId)))
            return AgentResult<IReadOnlyList<AgentQueueItem>>.Failure(new(AgentFailureCode.QueueItemNotFound, "The queue changed before reorder.", agentId, Recoverable: true, Retryable: true));

        var reordered = requested.OrderBy(item => item.Sequence).Select(item => currentById[item.QueueItemId] with
        {
            Sequence = item.Sequence,
            UpdatedAtUtc = nowUtc,
            Revision = checked(currentById[item.QueueItemId].Revision + 1)
        }).ToArray();
        return AgentResult<IReadOnlyList<AgentQueueItem>>.Success(reordered);
    }

    public static AgentResult<AgentQueueItem?> SelectReady(
        IReadOnlyList<AgentQueueItem> items,
        IReadOnlyDictionary<string, AgentQueueState> dependencyStates,
        string agentId)
    {
        var eligible = items.Where(item => item.AgentId == agentId && item.State == AgentQueueState.Queued).ToArray();
        var unknownDependency = eligible.SelectMany(item => item.DependencyIds).FirstOrDefault(id => !dependencyStates.ContainsKey(id));
        if (unknownDependency is not null)
            return AgentResult<AgentQueueItem?>.Failure(new(AgentFailureCode.QueueItemNotFound, "A queue dependency has no known state.", unknownDependency, Recoverable: true));
        var failedDependency = eligible.FirstOrDefault(item => item.DependencyIds.Any(dependencyId =>
            dependencyStates.TryGetValue(dependencyId, out var state) && state is AgentQueueState.Failed or AgentQueueState.Blocked or AgentQueueState.Cancelled));
        if (failedDependency is not null)
            return AgentResult<AgentQueueItem?>.Failure(new(AgentFailureCode.DependencyFailed, "A prerequisite queue item did not complete.", failedDependency.QueueItemId, Recoverable: true));

        var ready = eligible.Where(item => item.DependencyIds.All(dependencyId =>
                dependencyStates.TryGetValue(dependencyId, out var state) && state == AgentQueueState.Completed))
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.Sequence)
            .ThenBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.QueueItemId, StringComparer.Ordinal)
            .FirstOrDefault();
        return AgentResult<AgentQueueItem?>.Success(ready);
    }

    public static AgentFailure? ValidateAcyclicDependencies(IReadOnlyList<AgentQueueItem> items)
    {
        var byId = items.ToDictionary(item => item.QueueItemId, StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item.DependencyIds.Contains(item.QueueItemId, StringComparer.Ordinal))
                return new(AgentFailureCode.InvalidInvocationContext, "A queue item cannot depend on itself.", item.QueueItemId);
            foreach (var dependency in item.DependencyIds)
                if (!byId.ContainsKey(dependency))
                    return new(AgentFailureCode.QueueItemNotFound, "A queue dependency does not exist.", dependency);
        }

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        bool Visit(string id)
        {
            if (visiting.Contains(id)) return false;
            if (!visited.Add(id)) return true;
            visiting.Add(id);
            foreach (var dependency in byId[id].DependencyIds)
                if (!Visit(dependency)) return false;
            visiting.Remove(id);
            return true;
        }

        return items.Any(item => !Visit(item.QueueItemId))
            ? new(AgentFailureCode.InvalidInvocationContext, "Queue dependencies must be acyclic.", "queue.dependencies")
            : null;
    }
}
