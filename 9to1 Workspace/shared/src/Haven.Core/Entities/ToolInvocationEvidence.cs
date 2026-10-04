namespace Haven.Core;

/// <summary>Actual dispatcher facts; a runtime return is not proof of a durable owner mutation.</summary>
public enum ToolInvocationObservationStatus
{
    Unknown = 0,
    DeniedBeforeDispatch = 1,
    UnavailableBeforeDispatch = 2,
    RuntimeReturned = 3,
}

/// <summary>Exact local tool identity supplied to the owning dispatcher, never inferred from display prose.</summary>
public sealed record ToolInvocationEvidence(
    Guid InvocationId,
    string ToolName,
    string? RuntimeKey,
    ToolInvocationObservationStatus Status,
    bool? ReportedResultSucceeded,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    Guid? RetryOfInvocationId = null,
    string? ReportedFailureCode = null);
