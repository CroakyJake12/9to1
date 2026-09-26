using Haven.Core;

namespace Haven.Application;

public enum PlannerStructuredEntityKind { Assignment = 1, Schedule = 2, Countdown = 3 }

public sealed record PlannerStructuredEntityEnvelope(
    Guid Id,
    PlannerStructuredEntityKind Kind,
    int SchemaVersion,
    string Name,
    int? Status,
    DateTimeOffset? DueAt,
    long Revision,
    string PayloadJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt,
    DateTimeOffset? DeletedAt = null);

public sealed record PlannerStructuredEntityQuery(
    PlannerStructuredEntityKind Kind,
    int? Status = null,
    DateTimeOffset? DueBefore = null,
    DateTimeOffset? DueAfter = null,
    string? Search = null,
    int PageSize = 50,
    string? ContinuationToken = null,
    bool IncludeDeleted = false);

public sealed record PlannerStructuredEntityPage(
    IReadOnlyList<PlannerStructuredEntityEnvelope> Items,
    string? ContinuationToken);

public sealed record PlannerScheduleAggregate(PlannerSchedule Schedule, IReadOnlyList<PlannerScheduleItem> Items);

public sealed record PlannerAutomationEventRecord(
    Guid EventId,
    PlannerStructuredEntityKind EntityKind,
    Guid EntityId,
    long EntityRevision,
    string EventType,
    string? OccurrenceId,
    string PayloadJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    int AttemptCount,
    string? LastError);

public sealed class PlannerRevisionConflictException(
    PlannerStructuredEntityKind kind,
    Guid entityId,
    long? expectedRevision,
    long? actualRevision)
    : InvalidOperationException($"Planner {kind} entity {entityId} revision conflict: expected {expectedRevision?.ToString() ?? "new"}, actual {actualRevision?.ToString() ?? "missing"}.")
{
    public PlannerStructuredEntityKind Kind { get; } = kind;
    public Guid EntityId { get; } = entityId;
    public long? ExpectedRevision { get; } = expectedRevision;
    public long? ActualRevision { get; } = actualRevision;
    public string Code => "RevisionConflict";
}

public interface IPlannerStructuredEntityRepository
{
    Task<PlannerStructuredEntityEnvelope?> GetAsync(PlannerStructuredEntityKind kind, Guid id, CancellationToken cancellationToken);
    Task<PlannerStructuredEntityPage> ListAsync(PlannerStructuredEntityQuery query, CancellationToken cancellationToken);
    Task<PlannerStructuredEntityEnvelope> UpsertAsync(PlannerStructuredEntityEnvelope entity, long? expectedRevision, string? eventType, string? occurrenceId, string eventPayloadJson, CancellationToken cancellationToken);
    Task<PlannerStructuredEntityEnvelope> SetDeletedAsync(PlannerStructuredEntityKind kind, Guid id, long expectedRevision, DateTimeOffset? deletedAt, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlannerAutomationEventRecord>> GetPendingAutomationEventsAsync(int limit, CancellationToken cancellationToken);
    Task MarkAutomationEventPublishedAsync(Guid eventId, DateTimeOffset publishedAt, CancellationToken cancellationToken);
    Task RecordAutomationEventFailureAsync(Guid eventId, string error, CancellationToken cancellationToken);
}
