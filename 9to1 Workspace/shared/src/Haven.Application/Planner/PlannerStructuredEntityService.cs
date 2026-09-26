using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Core;

namespace Haven.Application;

/// <summary>Validates and persists structured Planner aggregates as typed, revisioned entities.</summary>
public sealed class PlannerStructuredEntityService(IPlannerStructuredEntityRepository repository)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<PlannerStructuredEntityEnvelope?> GetAsync(PlannerStructuredEntityKind kind, Guid id, CancellationToken cancellationToken = default) =>
        repository.GetAsync(kind, id, cancellationToken);

    public Task<PlannerStructuredEntityPage> ListAsync(PlannerStructuredEntityQuery query, CancellationToken cancellationToken = default) =>
        repository.ListAsync(query, cancellationToken);

    public async Task<PlannerAssignment?> GetAssignmentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await GetAsync(PlannerStructuredEntityKind.Assignment, id, cancellationToken).ConfigureAwait(false);
        if (entity is null) return null;
        var value = JsonSerializer.Deserialize<PlannerAssignment>(entity.PayloadJson, JsonOptions)
            ?? throw new InvalidDataException("Saved Planner Assignment payload is empty.");
        if (value.AssignmentId != entity.Id || value.Revision != entity.Revision)
            throw new InvalidDataException("Saved Planner Assignment identity or revision does not match its entity envelope.");
        ValidateAssignment(value);
        return value;
    }

    public async Task<PlannerScheduleAggregate?> GetScheduleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await GetAsync(PlannerStructuredEntityKind.Schedule, id, cancellationToken).ConfigureAwait(false);
        if (entity is null) return null;
        using var document = JsonDocument.Parse(entity.PayloadJson);
        var root = document.RootElement;
        var schedule = root.GetProperty("schedule").Deserialize<PlannerSchedule>(JsonOptions)
            ?? throw new InvalidDataException("Saved Planner Schedule payload is empty.");
        var items = root.GetProperty("items").Deserialize<List<PlannerScheduleItem>>(JsonOptions) ?? [];
        if (schedule.ScheduleId != entity.Id || schedule.Revision != entity.Revision)
            throw new InvalidDataException("Saved Planner Schedule identity or revision does not match its entity envelope.");
        ValidateSchedule(schedule, items);
        return new PlannerScheduleAggregate(schedule, items);
    }

    public async Task<PlannerCountdownEntity?> GetCountdownAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await GetAsync(PlannerStructuredEntityKind.Countdown, id, cancellationToken).ConfigureAwait(false);
        if (entity is null) return null;
        var value = JsonSerializer.Deserialize<PlannerCountdownEntity>(entity.PayloadJson, JsonOptions)
            ?? throw new InvalidDataException("Saved Planner Countdown payload is empty.");
        if (value.CountdownId != entity.Id || value.Revision != entity.Revision)
            throw new InvalidDataException("Saved Planner Countdown identity or revision does not match its entity envelope.");
        ValidateCountdown(value);
        return value;
    }

    public Task<PlannerStructuredEntityEnvelope> SaveAssignmentAsync(PlannerAssignment assignment, long? expectedRevision, CancellationToken cancellationToken = default)
    {
        ValidateAssignment(assignment);
        return SaveAsync(assignment.AssignmentId, PlannerStructuredEntityKind.Assignment, assignment.Title,
            (int)assignment.Status, assignment.DueAt, assignment.Revision, assignment.CreatedAt, assignment.ModifiedAt,
            assignment, expectedRevision, "PlannerAssignmentChanged", cancellationToken);
    }

    public Task<PlannerStructuredEntityEnvelope> SaveScheduleAsync(PlannerSchedule schedule, IReadOnlyList<PlannerScheduleItem> items, long? expectedRevision, CancellationToken cancellationToken = default)
    {
        ValidateSchedule(schedule, items);
        var dueAt = items.Count == 0 ? null : items.Min(item => (DateTimeOffset?)item.StartsAt);
        return SaveAsync(schedule.ScheduleId, PlannerStructuredEntityKind.Schedule, schedule.Name, null, dueAt,
            schedule.Revision, schedule.CreatedAt, schedule.ModifiedAt, new { Schedule = schedule, Items = items },
            expectedRevision, "PlannerScheduleChanged", cancellationToken);
    }

    public Task<PlannerStructuredEntityEnvelope> SaveCountdownAsync(PlannerCountdownEntity countdown, long? expectedRevision, CancellationToken cancellationToken = default)
    {
        ValidateCountdown(countdown);
        DateTimeOffset? dueAt = countdown.DateKind == PlannerCountdownDateKind.DateTime ? countdown.TargetDateTime : null;
        return SaveAsync(countdown.CountdownId, PlannerStructuredEntityKind.Countdown, countdown.Name, null, dueAt,
            countdown.Revision, countdown.CreatedAt, countdown.ModifiedAt, countdown, expectedRevision,
            "PlannerCountdownChanged", cancellationToken);
    }

    private Task<PlannerStructuredEntityEnvelope> SaveAsync<T>(Guid id, PlannerStructuredEntityKind kind, string name,
        int? status, DateTimeOffset? dueAt, long revision, DateTimeOffset createdAt, DateTimeOffset modifiedAt,
        T aggregate, long? expectedRevision, string eventType, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Planner entities require a stable ID and name.");
        if (expectedRevision is null && revision != 1 || expectedRevision is { } expected && revision != expected)
            throw new ArgumentException("Aggregate revision must match the expected persisted revision (or be 1 for a new entity).");
        var resultingRevision = expectedRevision is null ? 1 : checked(expectedRevision.Value + 1);
        var payload = JsonNode.Parse(JsonSerializer.Serialize(aggregate, JsonOptions)) as JsonObject
            ?? throw new InvalidOperationException("Planner aggregate must serialize as a JSON object.");
        payload["revision"] = resultingRevision;
        var envelope = new PlannerStructuredEntityEnvelope(id, kind, 1, name.Trim(), status, dueAt, Math.Max(1, revision),
            payload.ToJsonString(JsonOptions), createdAt, modifiedAt);
        var eventPayload = JsonSerializer.Serialize(new { entityId = id, entityKind = kind, name = name.Trim() }, JsonOptions);
        return repository.UpsertAsync(envelope, expectedRevision, eventType, null, eventPayload, cancellationToken);
    }

    private static void ValidateAssignment(PlannerAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        if (!Enum.IsDefined(assignment.Status) || assignment.Revision < 1) throw new ArgumentException("Assignment status or revision is invalid.", nameof(assignment));
        if (assignment.ManualProgress is < 0m or > 1m) throw new ArgumentOutOfRangeException(nameof(assignment), "Manual progress must be from 0 to 1.");
        var items = assignment.Items ?? throw new ArgumentException("Assignment items are required.", nameof(assignment));
        var byId = items.ToDictionary(item => item.AssignmentItemId);
        if (byId.Count != items.Count || byId.ContainsKey(Guid.Empty)) throw new ArgumentException("Assignment item IDs must be unique and stable.", nameof(assignment));
        foreach (var item in items)
        {
            if (item.AssignmentId != assignment.AssignmentId || !Enum.IsDefined(item.Kind) || item.Revision < 1 || string.IsNullOrWhiteSpace(item.Title))
                throw new ArgumentException("Assignment items must belong to the aggregate and have valid fields.", nameof(assignment));
            if (item.ParentItemId is { } parent && !byId.ContainsKey(parent)) throw new ArgumentException("Assignment parent item does not exist.", nameof(assignment));
            if (item.Weight is <= 0m) throw new ArgumentException("Assignment weights must be positive when supplied.", nameof(assignment));
            var seen = new HashSet<Guid> { item.AssignmentItemId };
            var cursor = item.ParentItemId;
            while (cursor is { } parentId)
            {
                if (!seen.Add(parentId)) throw new ArgumentException("Assignment item hierarchy contains a cycle.", nameof(assignment));
                cursor = byId[parentId].ParentItemId;
            }
        }
        ValidateReferences(assignment.References);
        _ = assignment.GetProgress();
    }

    private static void ValidateSchedule(PlannerSchedule schedule, IReadOnlyList<PlannerScheduleItem> items)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(items);
        if (schedule.ScheduleId == Guid.Empty || string.IsNullOrWhiteSpace(schedule.Name) || schedule.Revision < 1)
            throw new ArgumentException("Schedule identity, name, or revision is invalid.", nameof(schedule));
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId); }
        catch (TimeZoneNotFoundException ex) { throw new ArgumentException("Schedule time zone is not recognized.", nameof(schedule), ex); }
        catch (InvalidTimeZoneException ex) { throw new ArgumentException("Schedule time zone is invalid.", nameof(schedule), ex); }
        if (schedule.ScheduleItemIds.Count != items.Count || schedule.ScheduleItemIds.Distinct().Count() != items.Count
            || !schedule.ScheduleItemIds.ToHashSet().SetEquals(items.Select(item => item.ScheduleItemId)))
            throw new ArgumentException("Schedule item ordering must contain every item ID exactly once.", nameof(schedule));
        foreach (var item in items) ValidateReferences(item.References);
        if (schedule.Exceptions.Select(exception => exception.Date).Distinct().Count() != schedule.Exceptions.Count)
            throw new ArgumentException("Schedule exception dates must be unique.", nameof(schedule));
        foreach (var exception in schedule.Exceptions)
        {
            if (exception.IsSkipped && exception.ReplacementItems.Count != 0)
                throw new ArgumentException("A skipped schedule exception cannot contain replacement items.", nameof(schedule));
            _ = PlannerScheduleProgressCalculator.Calculate(exception.ReplacementItems, DateTimeOffset.MinValue);
        }
        _ = PlannerScheduleProgressCalculator.Calculate(items, DateTimeOffset.MinValue);
    }

    private static void ValidateCountdown(PlannerCountdownEntity countdown)
    {
        ArgumentNullException.ThrowIfNull(countdown);
        if (countdown.CountdownId == Guid.Empty || string.IsNullOrWhiteSpace(countdown.Name) || countdown.Revision < 1)
            throw new ArgumentException("Countdown identity, name, or revision is invalid.", nameof(countdown));
        var validTarget = countdown.DateKind switch
        {
            PlannerCountdownDateKind.Date => countdown.TargetDate is not null && countdown.TargetDateTime is null,
            PlannerCountdownDateKind.DateTime => countdown.TargetDateTime is not null && countdown.TargetDate is null,
            _ => false
        };
        if (countdown.LinkedTarget is null && !validTarget) throw new ArgumentException("Countdown must have exactly one date target.", nameof(countdown));
        if (countdown.LinkedTarget is not null) ValidateReference(countdown.LinkedTarget);
        ValidateReferences(countdown.References);
        if (string.IsNullOrWhiteSpace(countdown.TimeZoneId)) throw new ArgumentException("Countdown time zone is required.", nameof(countdown));
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(countdown.TimeZoneId); }
        catch (TimeZoneNotFoundException ex) { throw new ArgumentException("Countdown time zone is not recognized.", nameof(countdown), ex); }
        catch (InvalidTimeZoneException ex) { throw new ArgumentException("Countdown time zone is invalid.", nameof(countdown), ex); }
    }

    private static void ValidateReferences(IEnumerable<PlannerEntityReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        foreach (var reference in references) ValidateReference(reference);
    }

    private static void ValidateReference(PlannerEntityReference reference)
    {
        if (string.IsNullOrWhiteSpace(reference.AppId) || string.IsNullOrWhiteSpace(reference.EntityType) || string.IsNullOrWhiteSpace(reference.EntityId))
            throw new ArgumentException("Planner cross-app references require an app ID, entity type, and entity ID.");
    }
}
