namespace Haven.Core;

/// <summary>Lifecycle state for one persisted autonomous Agent task.</summary>
public enum AgentRunStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
    Suspended = 5
}

/// <summary>
/// Persisted evidence for one Agent execution. JSON fields remain machine-readable so
/// activity and capability provenance can evolve without breaking existing databases.
/// </summary>
public sealed record AgentRun(
    Guid Id,
    Guid AgentId,
    string AgentName,
    string Task,
    AgentRunStatus Status,
    string ModelName,
    string Result,
    string Error,
    string CapabilitiesJson,
    string ActivityJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    Guid? RetryOfRunId = null,
    string? ResourceReference = null,
    int ProgressPercent = 0)
{
    /// <summary>A display binding observed from this actual invocation; IDs and state grant no authority.</summary>
    public AgentRunCanonicalBinding? CanonicalTask { get; init; }
}

/// <summary>Detached canonical observation persisted within the existing activity JSON; never a grant or recovery receipt.</summary>
public sealed record AgentRunCanonicalBinding(
    Guid TaskId, Guid ContextId, Guid ExecutionId, long PersistenceRevision,
    TaskExecutionLifecycle State, Guid? AttemptId = null);
