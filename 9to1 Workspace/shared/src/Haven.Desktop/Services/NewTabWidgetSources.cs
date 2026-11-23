using System.Globalization;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

internal sealed record NewTabWidgetDefinition(string Id, string Title, string Source, string InitialSource);
internal sealed record NewTabWidgetSourceChoice(string Id, string Label);
internal sealed record NewTabWidgetItem(string Title, string Detail, AppContentAddress? Address = null, Guid? TaskId = null);
internal sealed record NewTabWidgetContent(string Status, IReadOnlyList<NewTabWidgetItem> Items, bool CanRetry = false);

/// <summary>Reads each widget through the same local repositories as its owning app.</summary>
internal sealed class NewTabWidgetSources(
    IPlannerRepository? planner,
    IConversationRepository? conversations,
    IContainerRepository? studyContainers,
    IBoardsWorkspaceService? boards,
    Func<CancellationToken, Task> admit,
    bool libraryAvailable = false)
{
    internal const string LibrarySource = "library:recent";
    internal const string SpacesRecentSource = "spaces:recent";

    internal static IReadOnlyList<NewTabWidgetDefinition> Catalogue { get; } =
    [
        new("up-next", "Up next", "Planner", "planner:calendar"),
        new("pinned-note", "Pinned note", "Boards", "boards:notebook"),
        new("focus-timer", "Focus timer", "Haven", "timer:25"),
        new("continue-working", "Continue working", "Library", LibrarySource),
        new("my-tasks", "My tasks", "Planner", "planner:tasks"),
        new("study-progress", "Study progress", "Study", "study:subject")
    ];

    internal async Task<NewTabWidgetContent> ReadAsync(NewTabWidgetPlacement placement, CancellationToken token)
    {
        try
        {
            await admit(token).ConfigureAwait(false);
            var content = placement.Id switch
            {
                "up-next" => await LoadCalendarAsync(placement.SourceId, token).ConfigureAwait(false),
                "pinned-note" => await LoadNoteAsync(placement.SourceId, token).ConfigureAwait(false),
                "continue-working" => await LoadContinueWorkingAsync(placement.SourceId, token).ConfigureAwait(false),
                "my-tasks" => await LoadTasksAsync(placement.SourceId, token).ConfigureAwait(false),
                "study-progress" => await LoadStudyAsync(placement.SourceId, token).ConfigureAwait(false),
                _ => new NewTabWidgetContent("The saved widget type is unavailable.", [])
            };
            await admit(token).ConfigureAwait(false);
            return content;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException)
        {
            return new("The current profile cannot read this source.", [], CanRetry: true);
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("New Tab widget source read failed: {0}", error);
            return new("The source could not be read. Retry to refresh it.", [], CanRetry: true);
        }
    }

    internal async Task<IReadOnlyList<NewTabWidgetSourceChoice>> ChoicesAsync(string widgetId, CancellationToken token)
    {
        await admit(token).ConfigureAwait(false);
        IReadOnlyList<NewTabWidgetSourceChoice> choices = widgetId switch
        {
            "up-next" when planner is not null =>
                [new("planner:calendar", "All visible calendars"), .. (await planner.GetCalendarsAsync(true, token).ConfigureAwait(false))
                    .Where(calendar => calendar.IsVisible).Select(calendar => Choice("calendar", calendar.Id, calendar.Name))],
            "pinned-note" when boards is not null => (await boards.ListNotebooksAsync(token).ConfigureAwait(false))
                .Select(note => Choice("notebook", note.Id, note.Title)).ToArray(),
            "my-tasks" when planner is not null => (await planner.GetCollectionsAsync(false, token).ConfigureAwait(false))
                .Where(collection => !collection.IsArchived).Select(collection => Choice("list", collection.Id, collection.Name)).ToArray(),
            "study-progress" when studyContainers is not null =>
                [new("study:subject", "All active subjects"), .. (await studyContainers.GetByModeAsync(HavenMode.Study, token).ConfigureAwait(false))
                    .Where(subject => !subject.IsArchived).Select(subject => Choice("subject", subject.Id, subject.Name))],
            "focus-timer" => new[] { 5, 10, 15, 25, 45, 60 }.Select(minutes =>
                new NewTabWidgetSourceChoice($"timer:{minutes}", $"{minutes} minutes")).ToArray(),
            "continue-working" => ContinueWorkingChoices(),
            _ => []
        };
        await admit(token).ConfigureAwait(false);
        return choices;
    }

    internal async Task<string?> CompleteTaskAsync(string sourceId, Guid taskId, CancellationToken token)
    {
        if (planner is null || !TryReadId(sourceId, "list", out var list))
            return "Choose an available Planner list first.";
        try
        {
            await admit(token).ConfigureAwait(false);
            var collections = await planner.GetCollectionsAsync(false, token).ConfigureAwait(false);
            var task = await planner.GetTaskAsync(taskId, token).ConfigureAwait(false);
            if (!collections.Any(collection => collection.Id == list && !collection.IsArchived) ||
                task is null || task.CollectionId != list || !IsOpen(task))
                return "This task is no longer available in the selected Planner list.";
            await admit(token).ConfigureAwait(false);
            await planner.CompleteTaskAsync(task.Id, DateTimeOffset.UtcNow, token).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { return "The current profile cannot complete this Planner task."; }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("New Tab Planner task completion failed: {0}", error);
            return "Planner could not confirm completion. Refresh the widget before trying again.";
        }
    }

    private async Task<NewTabWidgetContent> LoadCalendarAsync(string sourceId, CancellationToken token)
    {
        if (planner is null) return new("Planner is unavailable in this host.", []);
        var calendars = await planner.GetCalendarsAsync(true, token).ConfigureAwait(false);
        var authorized = calendars.Where(calendar => calendar.IsVisible).ToDictionary(calendar => calendar.Id);
        Guid? selected = null;
        if (sourceId != "planner:calendar")
        {
            if (!TryReadId(sourceId, "calendar", out var calendarId) || !authorized.ContainsKey(calendarId))
                return new("The selected calendar is unavailable. Choose another source.", []);
            selected = calendarId;
        }
        if (authorized.Count == 0) return new("No visible Planner calendars are available.", []);
        var now = DateTimeOffset.Now;
        var events = await planner.GetEventsAsync(now, now.AddDays(7), selected, token).ConfigureAwait(false);
        var rows = events.Where(item => item.DeletedAt is null && authorized.ContainsKey(item.CalendarId) &&
                (selected is null || item.CalendarId == selected) && item.EndsAt >= now)
            .OrderBy(item => item.StartsAt).Take(3).Select(item => new NewTabWidgetItem(item.Title,
                $"{item.StartsAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)} · {authorized[item.CalendarId].Name}")).ToArray();
        return new(rows.Length == 0 ? "No calendar events in the next seven days." : "Calendar links are unavailable in this Haven host.", rows);
    }

    private async Task<NewTabWidgetContent> LoadNoteAsync(string sourceId, CancellationToken token)
    {
        if (boards is null) return new("Boards is unavailable in this host.", []);
        if (!TryReadId(sourceId, "notebook", out var id)) return new("Choose a Boards notebook in Options.", []);
        var note = (await boards.ListNotebooksAsync(token).ConfigureAwait(false)).FirstOrDefault(note => note.Id == id);
        return note is null
            ? new("The selected Boards notebook is unavailable.", [])
            : new(string.Empty, [new(note.Title, "Open Boards notebook", AppContentAddress.Parse($"9to1://boards/notebooks/{note.Id:D}"))]);
    }

    private IReadOnlyList<NewTabWidgetSourceChoice> ContinueWorkingChoices()
    {
        var choices = new List<NewTabWidgetSourceChoice>();
        if (libraryAvailable) choices.Add(new(LibrarySource, "Home Library"));
        if (conversations is not null) choices.Add(new(SpacesRecentSource, "Recent saved conversations"));
        return choices;
    }

    private async Task<NewTabWidgetContent> LoadContinueWorkingAsync(string sourceId, CancellationToken token)
    {
        if (sourceId == LibrarySource)
        {
            // Library authorizes every row and opens exact owner links itself. Its current
            // sources do not publish recently opened documents, so link to the actual owner.
            return libraryAvailable
                ? new("Recently opened documents are unavailable here.",
                    [new("View Library", "Find files and notebooks", AppContentAddress.Parse("browse://home/library"))])
                : new("Home Library is unavailable in this host.", []);
        }
        if (sourceId != SpacesRecentSource) return new("The saved source is unavailable. Choose another source.", []);
        if (conversations is null) return new("Spaces is unavailable in this host.", []);
        var items = await conversations.GetRecentAsync(null, 12, token).ConfigureAwait(false);
        var rows = items.Where(item => !item.IsTemporary && !item.IsArchived && item.Id != Guid.Empty)
            .OrderByDescending(item => item.UpdatedAt).Take(3).Select(item => new NewTabWidgetItem(
                string.IsNullOrWhiteSpace(item.Title) ? "Untitled conversation" : item.Title, "Open saved conversation",
                AppContentAddress.Parse(item.SpaceId is { } space && space != Guid.Empty
                    ? $"9to1://spaces/selected/{space:D}/chat/{item.Id:D}"
                    : $"9to1://spaces/chat/{item.Id:D}"))).ToArray();
        return new(rows.Length == 0 ? "No recent saved conversations." : string.Empty, rows);
    }

    private async Task<NewTabWidgetContent> LoadTasksAsync(string sourceId, CancellationToken token)
    {
        if (planner is null) return new("Planner is unavailable in this host.", []);
        if (!TryReadId(sourceId, "list", out var id)) return new("Choose a Planner list in Options.", []);
        var list = (await planner.GetCollectionsAsync(false, token).ConfigureAwait(false))
            .FirstOrDefault(collection => collection.Id == id && !collection.IsArchived);
        if (list is null) return new("The selected Planner list is unavailable.", []);
        var tasks = await planner.GetTasksAsync(new PlannerTaskQuery(CollectionId: id), token).ConfigureAwait(false);
        var rows = tasks.Where(task => task.CollectionId == id && IsOpen(task)).OrderBy(task => task.DueAt ?? DateTimeOffset.MaxValue)
            .ThenBy(task => task.SortOrder).Take(4).Select(task => new NewTabWidgetItem(task.Title,
                task.DueAt is { } due ? $"Due {due.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}" : list.Name,
                TaskId: task.Id)).ToArray();
        return new(rows.Length == 0 ? $"{list.Name} has no open tasks." : list.Name, rows);
    }

    private async Task<NewTabWidgetContent> LoadStudyAsync(string sourceId, CancellationToken token)
    {
        if (studyContainers is null || planner is null) return new("Study assignment progress is unavailable in this host.", []);
        var subjects = (await studyContainers.GetByModeAsync(HavenMode.Study, token).ConfigureAwait(false))
            .Where(subject => !subject.IsArchived).ToArray();
        if (sourceId != "study:subject")
        {
            if (!TryReadId(sourceId, "subject", out var id)) return new("The configured Study subject is invalid.", []);
            subjects = subjects.Where(subject => subject.Id == id).ToArray();
        }
        if (subjects.Length == 0) return new("No active Study subjects match this widget.", []);
        var ids = subjects.Select(subject => subject.Id).ToHashSet();
        var assignments = (await planner.GetTasksAsync(new PlannerTaskQuery(IncludeCompleted: true), token).ConfigureAwait(false))
            .Where(task => task.Status != PlannerTaskStatus.Cancelled &&
                PlannerStudyAssignmentTags.TryRead(task.TagsJson, out var link) && ids.Contains(link.SubjectId)).ToArray();
        var completed = assignments.Count(task => task.Status == PlannerTaskStatus.Completed);
        return new(assignments.Length == 0 ? "No linked Study assignments yet." : $"{completed} of {assignments.Length} linked assignments completed.",
            [new("Open Study", "Study Space", AppContentAddress.Parse($"9to1://spaces/selected/{SpaceRegistry.StudySpaceId:D}"))]);
    }

    private static bool IsOpen(PlannerTask task) => task.Status is not (PlannerTaskStatus.Completed or PlannerTaskStatus.Cancelled);
    private static NewTabWidgetSourceChoice Choice(string kind, Guid id, string label) => new($"{kind}:{id:D}", label);
    private static bool TryReadId(string source, string kind, out Guid id)
    {
        id = Guid.Empty;
        var prefix = kind + ":";
        return source.StartsWith(prefix, StringComparison.Ordinal) && Guid.TryParseExact(source.AsSpan(prefix.Length), "D", out id) && id != Guid.Empty;
    }
}
