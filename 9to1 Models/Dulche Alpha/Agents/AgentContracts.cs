using System.Collections.Immutable;

namespace Dulche.Runtime.Agents;

public enum AgentRunState
{
    Queued,
    Running,
    Waiting,
    Blocked,
    Paused,
    AwaitingApproval,
    Completed,
    Failed,
    Stopped,
    Cancelled,
    Recovering,
    Recovered,
    CannotRecover
}

public enum AgentQueueState { Queued, Leased, Completed, Blocked, Cancelled, Failed }
public enum AgentBlockerState { Open, Recovering, AwaitingHuman, Resolved, Abandoned }
public enum AgentTriggerKind { User, Task, QuickAction, Automation, DelegatedAgent, Recovery }
public enum SubagentState { Created, Queued, Running, Waiting, Pausing, Paused, Completed, Failed, Blocked, Cancelled, TimedOut }
public enum SubagentContextMode { None, Inherit, Selected }
public enum SubagentFailurePolicy { Collect, FailFast, RequireAll, BestEffort }
public enum ParentPausePolicy { PauseChildren, LetChildrenFinish, StopChildren }
public enum BudgetKind { Tokens, Time, Steps, ToolCalls, Cost }
public enum UsageAvailability { Measured, Empty, ProviderUnavailable }

public enum AgentFailureCode
{
    AgentNotFound,
    DefinitionRevisionUnavailable,
    AgentDisabled,
    AgentUnavailableInScope,
    AgentBusy,
    AgentRunNotFound,
    RunNotResumable,
    RunRecoveryFailed,
    QueueItemNotFound,
    CapabilityUnavailable,
    PermissionDenied,
    ApprovalRequired,
    BlockerUnresolved,
    DelegationDenied,
    DelegationDepthExceeded,
    ConcurrencyLimitExceeded,
    BudgetExceeded,
    BudgetUsageUnavailable,
    ModelUnavailable,
    ToolUnavailable,
    MemoryScopeDenied,
    QuickActionUnavailable,
    InvalidInvocationContext,
    RevisionConflict,
    SubagentNotFound,
    MaxChildrenExceeded,
    MaxDepthExceeded,
    DependencyFailed,
    CheckpointUnavailable,
    RestoreConflict,
    IdempotencyMismatch,
    StateStoreUnavailable,
    ExecutionFailed
}

public sealed record AgentFailure(
    AgentFailureCode Code,
    string Message,
    string Target,
    bool Recoverable = false,
    bool Retryable = false,
    IReadOnlyDictionary<string, string>? Details = null);

public sealed record AgentResult<T>(T? Value, AgentFailure? Error)
{
    public bool Succeeded => Error is null;

    public static AgentResult<T> Success(T value) => new(value, null);
    public static AgentResult<T> Failure(AgentFailure error) => new(default, error);
}

public sealed record AgentInvocationContext(
    string CallerId,
    string SurfaceId,
    string? SpaceId = null,
    string? ProjectOrEntityId = null,
    IReadOnlyList<AgentContextReference>? Context = null,
    IReadOnlySet<string>? SurfaceCapabilities = null,
    IReadOnlySet<string>? SpaceCapabilities = null,
    IReadOnlySet<string>? ProjectCapabilities = null,
    IReadOnlySet<string>? CallerCapabilities = null,
    IReadOnlySet<string>? HomeGrantedCapabilities = null,
    bool Temporary = false);

public sealed record AgentContextReference(
    string Id,
    string OwningApp,
    string Revision,
    string AccessScope,
    string? Content = null,
    string? ContentHash = null,
    IReadOnlySet<string>? RequiredCapabilities = null);

public sealed record AgentCapabilityPolicy(
    IReadOnlySet<string> AllowedCapabilities,
    IReadOnlySet<string> DeniedCapabilities,
    bool RequireApprovalForConsequentialActions = true);

public sealed record AgentDelegationPolicy(
    IReadOnlySet<string> AllowedAgentIds,
    IReadOnlySet<string> AllowedAgentClasses,
    bool AllowSubagents,
    int MaximumConcurrency,
    int MaximumDepth,
    AgentBudgetLimits Budget);

public sealed record AgentModelPolicy(
    bool Inherit,
    string? ModelId = null,
    string? ProviderId = null,
    bool AllowCloud = false,
    bool AllowFallback = true,
    IReadOnlySet<string>? RequiredCapabilities = null);

public sealed record PersistentAgentSnapshot(
    string AgentId,
    long DefinitionRevision,
    string Name,
    bool Enabled,
    string? AgentClass,
    AgentModelPolicy ModelPolicy,
    AgentCapabilityPolicy CapabilityPolicy,
    AgentDelegationPolicy DelegationPolicy,
    string? MemoryNamespaceId = null,
    IReadOnlySet<string>? AllowedTools = null);

public sealed record AgentRunRequest(
    string AgentId,
    long DefinitionRevision,
    string Objective,
    AgentTriggerKind Trigger,
    string TriggerId,
    AgentInvocationContext Context,
    AgentBudgetLimits Budget,
    string? TaskId = null,
    string? QueueItemId = null,
    string? IdempotencyKey = null,
    string? QuickActionId = null,
    IReadOnlyDictionary<string, string>? Inputs = null,
    string? ParentAgentRunId = null,
    string? ParentSubagentId = null,
    AgentHandoff? Handoff = null);

public sealed record AgentRunSnapshot(
    string AgentRunId,
    string AgentId,
    long DefinitionRevision,
    string Objective,
    AgentTriggerKind Trigger,
    string TriggerId,
    string? TaskId,
    string? QueueItemId,
    string? IdempotencyKey,
    string? ParentAgentRunId,
    string? ParentSubagentId,
    IReadOnlyList<string> ChildAgentRunIds,
    AgentRunState State,
    string CallerId,
    string SurfaceId,
    string? SpaceId,
    string? ProjectOrEntityId,
    string SessionId,
    string? EndpointId,
    string? ActionGraphId,
    string? ModelId,
    string? ProviderId,
    IReadOnlySet<string> EffectiveCapabilities,
    IReadOnlyList<AgentContextReference> ContextSnapshot,
    AgentDelegationPolicy EffectiveDelegationPolicy,
    AgentBudgetLimits BudgetLimits,
    AgentBudgetUsage BudgetUsage,
    IReadOnlyList<string> OutputReferenceIds,
    IReadOnlyList<string> BlockerIds,
    IReadOnlyList<string> ApprovalIds,
    IReadOnlyList<string> SubagentRunIds,
    string? CheckpointId,
    string? RecoveryState,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    int? KnownProgressPercent,
    long Revision,
    string CurrentAttemptId,
    IReadOnlyList<AgentAttemptSnapshot> Attempts,
    string? LastErrorCode = null,
    string? LastErrorMessage = null);

public sealed record AgentAttemptSnapshot(
    string AttemptId,
    int Number,
    string? RequestId,
    string? SessionId,
    string? EndpointId,
    string? ModelId,
    string? ProviderId,
    string ContinuationKind,
    IReadOnlyList<string> CompletedActionIds,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    AgentRunState State,
    AgentBudgetUsage Usage,
    string? ErrorCode = null);

public sealed record AgentHandoff(
    string Objective,
    IReadOnlyList<AgentContextReference> RequiredContext,
    IReadOnlySet<string> RequestedCapabilityScope,
    IReadOnlyList<string> ExpectedOutputTypes,
    AgentBudgetLimits Budget,
    string HandoffId);

public sealed record AgentQueueItem(
    string QueueItemId,
    string AgentId,
    string Objective,
    string CreatorId,
    AgentTriggerKind Trigger,
    string TriggerId,
    string? TaskId,
    int Priority,
    long Sequence,
    IReadOnlyList<string> DependencyIds,
    AgentQueueState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? AgentRunId,
    long Revision);

public sealed record AgentBlocker(
    string BlockerId,
    string AgentRunId,
    AgentFailureCode Code,
    string Category,
    string Description,
    DateTimeOffset CreatedAtUtc,
    AgentBlockerState State,
    bool AgentCanResolve,
    bool HumanActionRequired,
    string? RequiredHumanAction,
    IReadOnlyList<string> RelatedEntityIds,
    IReadOnlyList<string> EvidenceEventIds,
    IReadOnlyList<string> RecoveryAttemptIds,
    string? Resolution,
    DateTimeOffset? ResolvedAtUtc,
    long Revision);

public sealed record AgentApprovalRequirement(
    string ApprovalId,
    string AgentRunId,
    string CallerId,
    string ActionName,
    string Scope,
    string Risk,
    IReadOnlyList<string> AffectedObjectIds,
    string ImpactDescription,
    string State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionId);

public sealed record AgentOutputReference(
    string Id,
    string OwningApp,
    string EntityType,
    string EntityId,
    long? Revision = null,
    string? DisplayName = null);

public sealed record AgentCheckpoint(
    string CheckpointId,
    string AgentRunId,
    string AttemptId,
    string BackendId,
    string ModelId,
    string CompatibilityVersion,
    string ContinuationKind,
    IReadOnlyList<string> CompletedActionIds,
    IReadOnlyList<string> UncertainActionIds,
    string? PortableStateJson,
    DateTimeOffset CreatedAtUtc);

public sealed record AgentRunEvent(
    string EventId,
    string AgentRunId,
    string AttemptId,
    long Sequence,
    DateTimeOffset AtUtc,
    string EventType,
    string? ParentEventId,
    string? RelatedEntityId,
    string? PayloadJson,
    bool Redacted = false);

public sealed record AgentBudgetLimits(
    long? MaxTokens = null,
    TimeSpan? MaxTime = null,
    long? MaxSteps = null,
    long? MaxToolCalls = null,
    decimal? MaxCost = null)
{
    public AgentFailure? Validate(string target)
    {
        if (MaxTokens is < 0 || MaxTime is { Ticks: < 0 } || MaxSteps is < 0 || MaxToolCalls is < 0 || MaxCost is < 0)
            return new(AgentFailureCode.InvalidInvocationContext, "Budget limits cannot be negative.", target);
        return null;
    }

    public static AgentBudgetLimits Narrow(AgentBudgetLimits parent, AgentBudgetLimits child) => new(
        Minimum(parent.MaxTokens, child.MaxTokens),
        Minimum(parent.MaxTime, child.MaxTime),
        Minimum(parent.MaxSteps, child.MaxSteps),
        Minimum(parent.MaxToolCalls, child.MaxToolCalls),
        Minimum(parent.MaxCost, child.MaxCost));

    private static long? Minimum(long? parent, long? child) => parent is null ? child : child is null ? parent : Math.Min(parent.Value, child.Value);
    private static TimeSpan? Minimum(TimeSpan? parent, TimeSpan? child) => parent is null ? child : child is null ? parent : parent.Value <= child.Value ? parent : child;
    private static decimal? Minimum(decimal? parent, decimal? child) => parent is null ? child : child is null ? parent : Math.Min(parent.Value, child.Value);
}

public sealed record UsageValue<T>(UsageAvailability Availability, T? Value, string? Basis = null)
    where T : struct
{
    public static UsageValue<T> Measured(T value, string? basis = null) => new(UsageAvailability.Measured, value, basis);
    public static UsageValue<T> Empty(string? basis = null) => new(UsageAvailability.Empty, null, basis);
    public static UsageValue<T> Unavailable(string? basis = null) => new(UsageAvailability.ProviderUnavailable, null, basis);
}

public sealed record AgentBudgetUsage(
    UsageValue<long> Tokens,
    UsageValue<TimeSpan> Time,
    UsageValue<long> Steps,
    UsageValue<long> ToolCalls,
    UsageValue<decimal> Cost,
    long ReservedTokens = 0,
    TimeSpan ReservedTime = default,
    long ReservedSteps = 0,
    long ReservedToolCalls = 0,
    decimal ReservedCost = 0,
    long KnownTokenSubtotal = 0,
    long KnownToolCallSubtotal = 0,
    decimal KnownCostSubtotal = 0,
    int IncludedInvocationCount = 0,
    int UnavailableInvocationCount = 0)
{
    public static AgentBudgetUsage Empty { get; } = new(
        UsageValue<long>.Empty(), UsageValue<TimeSpan>.Empty(), UsageValue<long>.Measured(0),
        UsageValue<long>.Measured(0), UsageValue<decimal>.Empty());
}

public sealed record BudgetReservation(
    string ReservationId,
    string RunId,
    string InvocationId,
    BudgetKind Kind,
    decimal Amount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SettledAtUtc,
    decimal? ActualAmount,
    UsageAvailability? ActualAvailability);

public sealed record SubagentLimits(int MaxChildren, int MaxParallelChildren, int MaxDepth)
{
    public AgentFailure? Validate(string target)
    {
        if (MaxChildren < 0 || MaxParallelChildren < 0 || MaxDepth < 0)
            return new(AgentFailureCode.InvalidInvocationContext, "Subagent limits cannot be negative.", target);
        if (MaxParallelChildren > MaxChildren)
            return new(AgentFailureCode.InvalidInvocationContext, "MaxParallelChildren cannot exceed MaxChildren.", target);
        return null;
    }

    public static SubagentLimits Intersect(SubagentLimits requested, SubagentLimits parent, SubagentLimits runtime) => new(
        Math.Min(requested.MaxChildren, Math.Min(parent.MaxChildren, runtime.MaxChildren)),
        Math.Min(requested.MaxParallelChildren, Math.Min(parent.MaxParallelChildren, runtime.MaxParallelChildren)),
        Math.Min(requested.MaxDepth, Math.Min(parent.MaxDepth, runtime.MaxDepth)));
}

public sealed record SubagentContextSelection(SubagentContextMode Mode, IReadOnlyList<AgentContextReference> References)
{
    public static SubagentContextSelection None { get; } = new(SubagentContextMode.None, []);

    public AgentResult<SubagentContextSelection> Snapshot(IReadOnlyList<AgentContextReference> parentContext)
    {
        return Mode switch
        {
            SubagentContextMode.None when References.Count == 0 => AgentResult<SubagentContextSelection>.Success(None),
            SubagentContextMode.Inherit when References.Count == 0 => AgentResult<SubagentContextSelection>.Success(new(Mode, parentContext.ToImmutableArray())),
            SubagentContextMode.Selected when References.Count > 0 => AgentResult<SubagentContextSelection>.Success(new(Mode, References.ToImmutableArray())),
            SubagentContextMode.Selected => AgentResult<SubagentContextSelection>.Failure(new(AgentFailureCode.InvalidInvocationContext, "Selected context requires at least one explicit reference.", "context")),
            _ => AgentResult<SubagentContextSelection>.Failure(new(AgentFailureCode.InvalidInvocationContext, "The context policy has inconsistent references.", "context"))
        };
    }
}

public sealed record SubagentDefinition(
    string ParentRequestId,
    string? ParentSubagentId,
    string RootRequestId,
    string SessionId,
    string Prompt,
    string? ModelId,
    string? EndpointId,
    SubagentContextSelection Context,
    IReadOnlySet<string> RequestedTools,
    IReadOnlySet<string> RequestedPermissions,
    SubagentFailurePolicy FailurePolicy,
    AgentBudgetLimits Budget,
    SubagentLimits Limits,
    ParentPausePolicy PausePolicy = ParentPausePolicy.PauseChildren)
{
    public AgentFailure? Validate()
    {
        if (string.IsNullOrWhiteSpace(ParentRequestId) || string.IsNullOrWhiteSpace(RootRequestId) || string.IsNullOrWhiteSpace(SessionId))
            return new(AgentFailureCode.InvalidInvocationContext, "Parent request, root request and session identities are required.", "subagent.parent");
        if (string.IsNullOrWhiteSpace(Prompt))
            return new(AgentFailureCode.InvalidInvocationContext, "A Subagent prompt is required.", "subagent.prompt");
        return Budget.Validate("subagent.budget") ?? Limits.Validate("subagent.limits");
    }
}

public sealed record EffectiveSubagentAuthority(
    IReadOnlySet<string> Capabilities,
    IReadOnlySet<string> Tools,
    IReadOnlySet<string> Permissions)
{
    public static EffectiveSubagentAuthority Narrow(
        EffectiveSubagentAuthority caller,
        EffectiveSubagentAuthority parent,
        IEnumerable<string> requestedCapabilities,
        IEnumerable<string> requestedTools,
        IEnumerable<string> requestedPermissions) => new(
            Intersection(caller.Capabilities, parent.Capabilities, requestedCapabilities),
            Intersection(caller.Tools, parent.Tools, requestedTools),
            Intersection(caller.Permissions, parent.Permissions, requestedPermissions));

    private static IReadOnlySet<string> Intersection(
        IReadOnlySet<string> caller,
        IReadOnlySet<string> parent,
        IEnumerable<string> requested)
    {
        var parentSet = new HashSet<string>(parent, StringComparer.OrdinalIgnoreCase);
        var requestedSet = new HashSet<string>(requested, StringComparer.OrdinalIgnoreCase);
        parentSet.IntersectWith(caller);
        parentSet.IntersectWith(requestedSet);
        return parentSet.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record SubagentRunSnapshot(
    string SubagentId,
    string AgentRunId,
    string? ParentSubagentId,
    string ParentRequestId,
    string RootRequestId,
    string SessionId,
    string EndpointId,
    int Depth,
    SubagentState State,
    string? ModelId,
    EffectiveSubagentAuthority EffectiveAuthority,
    SubagentContextSelection ContextSnapshot,
    AgentBudgetLimits BudgetLimits,
    AgentBudgetUsage Usage,
    string Prompt,
    string? Result,
    IReadOnlyList<AgentFailure> Errors,
    IReadOnlyList<string> BlockerIds,
    IReadOnlyList<string> ChildSubagentIds,
    IReadOnlyList<string> CompletedActionIds,
    string? CheckpointId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    long Revision,
    int AttemptNumber = 1);

public sealed record SubagentCheckpoint(
    string CheckpointId,
    string RootRequestId,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<SubagentRunSnapshot> Subagents,
    IReadOnlyList<AgentExecutionEventEnvelope> PendingEvents,
    IReadOnlyList<string> CompletedConsequentialActionIds,
    IReadOnlyList<string> UncertainConsequentialActionIds,
    string CompatibilityVersion,
    string ContinuationKind,
    long Revision);

public sealed record SubagentOutcome(
    string SubagentId,
    SubagentState State,
    string? Result,
    IReadOnlyList<AgentFailure> Errors,
    IReadOnlyList<string> BlockerIds,
    AgentBudgetUsage Usage,
    string? FinishReason);

public sealed record SubagentJoinResult(
    string JoinId,
    string ParentRequestId,
    IReadOnlyList<SubagentOutcome> Outcomes,
    bool Succeeded,
    bool CancelledSiblings,
    SubagentFailurePolicy Policy);

public sealed record AgentRecoveryReport(
    string AgentRunId,
    AgentRunState State,
    bool ExactContinuation,
    string ContinuationKind,
    IReadOnlyList<string> ReconciledActionIds,
    IReadOnlyList<string> UncertainActionIds,
    IReadOnlyList<string> BlockerIds,
    string? Reason);

public sealed record AgentQueueReorder(string QueueItemId, long Sequence);

public sealed record AgentExecutionEventEnvelope(
    string RequestId,
    string? ParentRequestId,
    string? ParentSubagentId,
    string RootRequestId,
    string ExecutionId,
    long Sequence,
    DateTimeOffset AtUtc,
    string Kind,
    string? Detail,
    bool ConcurrentSpan = false);
