namespace HavenOS.Apps.Plan;

public sealed record PlanEvent(
    string Uid,
    string Summary,
    string Description,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool AllDay,
    string Location,
    IReadOnlyList<string> Categories,
    string RecurrenceRule,
    IReadOnlyList<PlanAlarm> Alarms);

public sealed record PlanAlarm(string Uid, DateTimeOffset Trigger, string Action, string Description);

public sealed record PlanTask(
    string Uid,
    string Summary,
    string Description,
    DateTimeOffset? Due,
    DateTimeOffset? Start,
    int Priority,
    bool Completed,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<string> Categories);

public sealed record PlanCalendar(string Uid, string Name, string Color, bool Visible, bool ReadOnly);

public sealed record PlanTimeRange(DateTimeOffset Start, DateTimeOffset End);

public interface IPlanEngine : IAsyncDisposable
{
    Task<IReadOnlyList<PlanCalendar>> GetCalendarsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlanEvent>> GetEventsAsync(PlanTimeRange range, IReadOnlyList<string> calendarUids, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlanTask>> GetTasksAsync(IReadOnlyList<string> calendarUids, CancellationToken cancellationToken = default);
    Task<PlanEvent> CreateEventAsync(PlanEvent evt, CancellationToken cancellationToken = default);
    Task<PlanEvent> UpdateEventAsync(string uid, PlanEvent evt, CancellationToken cancellationToken = default);
    Task DeleteEventAsync(string uid, CancellationToken cancellationToken = default);
    Task<PlanTask> CreateTaskAsync(PlanTask task, CancellationToken cancellationToken = default);
    Task<PlanTask> UpdateTaskAsync(string uid, PlanTask task, CancellationToken cancellationToken = default);
    Task DeleteTaskAsync(string uid, CancellationToken cancellationToken = default);
    Task<PlanCalendar> CreateCalendarAsync(PlanCalendar calendar, CancellationToken cancellationToken = default);
    Task SetCalendarVisibilityAsync(string uid, bool visible, CancellationToken cancellationToken = default);
    void SetEventCallback(Action<PlanEngineEvent> callback);
}

public enum PlanEngineEventType
{
    EventAdded = 0,
    EventUpdated = 1,
    EventDeleted = 2,
    TaskAdded = 3,
    TaskUpdated = 4,
    TaskDeleted = 5,
    CalendarAdded = 6,
    CalendarUpdated = 7,
    CalendarDeleted = 8,
    SyncStarted = 9,
    SyncCompleted = 10,
    SyncFailed = 11,
}

public sealed record PlanEngineEvent(PlanEngineEventType Type, string Payload);

public sealed class PlanAppService(IPlanEngine engine)
{
    public IPlanEngine Engine { get; } = engine ?? throw new ArgumentNullException(nameof(engine));
}