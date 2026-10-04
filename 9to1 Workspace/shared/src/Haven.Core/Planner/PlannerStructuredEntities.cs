namespace Haven.Core;

public enum PlannerAssignmentStatus { NotStarted = 0, InProgress = 1, Completed = 2, Cancelled = 3 }
public enum PlannerAssignmentItemKind { Required = 0, Optional = 1 }

/// <summary>A stable, permission-checked reference to an entity owned by another 9to1 app.</summary>
public sealed record PlannerEntityReference(string AppId, string EntityType, string EntityId);

/// <summary>A checklist item owned by a Planner Assignment.</summary>
public sealed record PlannerAssignmentItem(
    Guid AssignmentItemId,
    Guid AssignmentId,
    Guid? ParentItemId,
    string Title,
    string? Description,
    PlannerAssignmentItemKind Kind,
    bool IsComplete,
    decimal? Weight,
    string? AssigneeId,
    long Revision,
    DateTimeOffset ModifiedAt);

/// <summary>Structured deadline work; its checklist and references are part of the same canonical aggregate.</summary>
public sealed record PlannerAssignment(
    Guid AssignmentId,
    string Title,
    string? Description,
    PlannerAssignmentStatus Status,
    DateOnly? StartDate,
    DateTimeOffset? DueAt,
    DateTimeOffset? CompletedAt,
    PlannerPriority? Priority,
    string? Category,
    string? OwnerId,
    string? Scope,
    IReadOnlyList<string> Assignees,
    IReadOnlyList<PlannerAssignmentItem> Items,
    IReadOnlyList<PlannerEntityReference> References,
    IReadOnlyList<Guid> ReminderIds,
    IReadOnlyList<string> AutomationRefs,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt,
    decimal? ManualProgress = null)
{
    public IReadOnlyList<PlannerAssignmentItem> RequiredItems => Items.Where(item => item.Kind == PlannerAssignmentItemKind.Required).ToArray();
    public IReadOnlyList<PlannerAssignmentItem> OptionalItems => Items.Where(item => item.Kind == PlannerAssignmentItemKind.Optional).ToArray();

    public bool IsOverdue(DateTimeOffset now) =>
        DueAt is { } dueAt && dueAt < now && Status is not (PlannerAssignmentStatus.Completed or PlannerAssignmentStatus.Cancelled);

    public decimal GetProgress()
    {
        if (ManualProgress is { } manual)
        {
            if (manual is < 0m or > 1m) throw new InvalidOperationException("Manual assignment progress must be between 0 and 1.");
            return manual;
        }
        var required = RequiredItems;
        if (required.Count == 0) return Status == PlannerAssignmentStatus.Completed ? 1m : 0m;
        var hasAnyWeights = required.Any(item => item.Weight.HasValue);
        if (hasAnyWeights && required.Any(item => item.Weight is null or <= 0m))
            throw new InvalidOperationException("Required assignment items must either all have positive weights or all use equal weighting.");
        var weights = required.Select(item => item.Weight ?? 1m).ToArray();
        var total = weights.Sum();
        if (total <= 0m) throw new InvalidOperationException("Required assignment item weights must have a positive total.");
        var complete = required.Select((item, index) => item.IsComplete ? weights[index] : 0m).Sum();
        return complete / total;
    }

    public bool CanComplete => RequiredItems.All(item => item.IsComplete);

    public PlannerAssignment Complete(DateTimeOffset completedAt, bool overrideIncompleteRequiredItems = false)
    {
        if (!CanComplete && !overrideIncompleteRequiredItems)
            throw new InvalidOperationException("Required assignment items remain incomplete. An explicit override is required.");
        return this with { Status = PlannerAssignmentStatus.Completed, CompletedAt = completedAt, ModifiedAt = completedAt, Revision = checked(Revision + 1) };
    }
}

public sealed record PlannerScheduleException(DateOnly Date, IReadOnlyList<PlannerScheduleItem> ReplacementItems, bool IsSkipped = false);

/// <summary>A reusable ordered schedule. Item identity and presentation order are stored separately.</summary>
public sealed record PlannerSchedule(
    Guid ScheduleId,
    string Name,
    string TimeZoneId,
    string? Applicability,
    string? RecurrenceRule,
    IReadOnlyList<Guid> ScheduleItemIds,
    IReadOnlyList<PlannerScheduleException> Exceptions,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt);

public sealed record PlannerScheduleItem(
    Guid ScheduleItemId,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Color,
    string? Icon,
    string? Location,
    IReadOnlyList<PlannerEntityReference> References,
    Guid? EventId = null,
    Guid? PlannerTaskId = null,
    Guid? AssignmentId = null,
    long Revision = 0);

public sealed record PlannerScheduleProgress(
    PlannerScheduleItem? Previous,
    PlannerScheduleItem? Current,
    PlannerScheduleItem? Next,
    TimeSpan Elapsed,
    TimeSpan Remaining,
    decimal PercentageElapsed,
    decimal PercentageRemaining);

public static class PlannerScheduleProgressCalculator
{
    public static PlannerScheduleProgress Calculate(IEnumerable<PlannerScheduleItem> scheduleItems, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(scheduleItems);
        var items = scheduleItems.OrderBy(item => item.StartsAt).ThenBy(item => item.EndsAt).ToArray();
        if (items.Any(item => item.ScheduleItemId == Guid.Empty || item.EndsAt <= item.StartsAt)
            || items.Select(item => item.ScheduleItemId).Distinct().Count() != items.Length)
            throw new ArgumentException("Schedule items require stable IDs and an end time after their start time.", nameof(scheduleItems));
        if (items.Zip(items.Skip(1), (left, right) => left.EndsAt > right.StartsAt).Any(overlap => overlap))
            throw new ArgumentException("Schedule items cannot overlap because a schedule has only one current item at a time.", nameof(scheduleItems));
        var currentIndex = Array.FindIndex(items, item => item.StartsAt <= now && now < item.EndsAt);
        if (currentIndex < 0)
            return new(null, null, items.FirstOrDefault(item => item.StartsAt > now), TimeSpan.Zero, TimeSpan.Zero, 0m, 0m);
        var current = items[currentIndex];
        var elapsed = now - current.StartsAt;
        var duration = current.EndsAt - current.StartsAt;
        var elapsedFraction = Math.Clamp((decimal)(elapsed.Ticks / (double)duration.Ticks), 0m, 1m);
        return new(
            currentIndex > 0 ? items[currentIndex - 1] : null,
            current,
            currentIndex + 1 < items.Length ? items[currentIndex + 1] : null,
            elapsed,
            current.EndsAt - now,
            elapsedFraction,
            1m - elapsedFraction);
    }
}

public enum PlannerCountdownDateKind { Date = 0, DateTime = 1 }

/// <summary>A user-owned countdown that may be standalone or resolve its date from another canonical Planner entity.</summary>
public sealed record PlannerCountdownEntity(
    Guid CountdownId,
    string Name,
    PlannerCountdownDateKind DateKind,
    DateOnly? TargetDate,
    DateTimeOffset? TargetDateTime,
    string TimeZoneId,
    string? Icon,
    string? ImageReference,
    string? Category,
    string? RecurrenceRule,
    IReadOnlyList<PlannerEntityReference> References,
    PlannerEntityReference? LinkedTarget,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt);

public sealed record PlannerCountdownTarget(
    PlannerCountdownDateKind DateKind,
    DateOnly? Date,
    DateTimeOffset? DateTime,
    string TimeZoneId);

public static class PlannerCountdownTargetResolver
{
    public static PlannerCountdownTarget? Resolve(
        PlannerCountdownEntity countdown,
        Func<PlannerEntityReference, PlannerCountdownTarget?> resolveCanonicalTarget)
    {
        ArgumentNullException.ThrowIfNull(countdown);
        ArgumentNullException.ThrowIfNull(resolveCanonicalTarget);
        if (countdown.LinkedTarget is not null)
            return resolveCanonicalTarget(countdown.LinkedTarget);
        return countdown.DateKind switch
        {
            PlannerCountdownDateKind.Date when countdown.TargetDate is { } date =>
                new PlannerCountdownTarget(PlannerCountdownDateKind.Date, date, null, countdown.TimeZoneId),
            PlannerCountdownDateKind.DateTime when countdown.TargetDateTime is { } dateTime =>
                new PlannerCountdownTarget(PlannerCountdownDateKind.DateTime, null, dateTime, countdown.TimeZoneId),
            _ => null
        };
    }
}
