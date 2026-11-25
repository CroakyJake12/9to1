using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Study;

namespace Haven.Desktop.Views.Pages.Home;

internal enum DashboardBriefingDestination { Library, Tasks, Study }

public sealed partial class NewDashboardPage
{
    private bool _homeBriefingEnabled, _briefingLibraryAvailable, _publishedBriefingVisibility;
    private IPlannerRepository? _briefingPlanner;
    private IContainerRepository? _briefingStudy;

    internal event EventHandler<DashboardBriefingDestination>? BriefingDestinationRequested;
    internal event EventHandler? HomeBriefingVisibilityChanged;

    internal void EnableHomeBriefing(IPlannerRepository? planner, IContainerRepository? study)
    {
        _homeBriefingEnabled = true;
        _briefingPlanner = planner;
        _briefingStudy = study;
        _dashboardBindings.Set("HasBriefingTasks", planner is not null);
        _dashboardBindings.Set("HasBriefingStudy", study is not null);
        ApplyDashboardWidth(Bounds.Width);
    }

    internal void ConfigureHomeBriefingLibrary(bool available)
    {
        _briefingLibraryAvailable = available;
        _dashboardBindings.Set("HasBriefingLibrary", available);
    }

    private void ApplyBriefingWidth(double width)
    {
        var wide = width >= 800;
        _dashboardBindings.Set("BriefingColumns", wide ? "3fr 2fr" : "1fr");
        _dashboardBindings.Set("BriefingRows", wide ? "Auto" : "Auto Auto");
        _dashboardBindings.Set("BriefingStudyRow", wide ? 0 : 1);
        _dashboardBindings.Set("BriefingStudyColumn", wide ? 1 : 0);
    }

    internal bool ShowsHomeBriefing => _homeBriefingEnabled && _selectedPageId == HomePageId &&
        _widgetLayoutState?.UsesHomeBriefing == true;

    private void PublishHomeBriefingVisibility()
    {
        var show = ShowsHomeBriefing;
        if (_publishedBriefingVisibility == show) return;
        _publishedBriefingVisibility = show;
        _dashboardBindings.Set("ShowHomeBriefing", show);
        _dashboardBindings.Set("ShowDashboardGreeting", !show);
        HomeBriefingVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private ValueTask OpenBriefingDestination(DashboardBriefingDestination destination)
    {
        var available = destination switch
        {
            DashboardBriefingDestination.Library => _briefingLibraryAvailable,
            DashboardBriefingDestination.Tasks => _briefingPlanner is not null,
            DashboardBriefingDestination.Study => _briefingStudy is not null,
            _ => false
        };
        if (_dashboardActive && ShowsHomeBriefing && available) BriefingDestinationRequested?.Invoke(this, destination);
        return ValueTask.CompletedTask;
    }

    private async Task RefreshHomeBriefingAsync(DashboardSnapshot snapshot, CancellationToken token)
    {
        if (!ShowsHomeBriefing) return;
        await RefreshTodayAsync(snapshot, token);
        await RefreshStudyWeekAsync(token);
    }

    private async Task RefreshTodayAsync(DashboardSnapshot snapshot, CancellationToken token)
    {
        var agenda = snapshot.Agenda.Where(entry => entry.Kind == "task").ToList();
        var status = string.Empty;
        if (_briefingPlanner is { } planner)
        {
            try
            {
                var now = DateTimeOffset.Now;
                var end = new DateTimeOffset(now.Date.AddDays(1), now.Offset);
                var calendars = await AwaitHomeSourceAsync(planner.GetCalendarsAsync(true, token));
                var visible = calendars.Where(calendar => calendar.IsVisible).Select(calendar => calendar.Id).ToHashSet();
                if (visible.Count > 0)
                {
                    var entries = await AwaitHomeSourceAsync(planner.GetEventsAsync(now, end, null, token));
                    agenda.AddRange(entries.Where(entry => entry.DeletedAt is null && visible.Contains(entry.CalendarId))
                        .Select(entry => new DashboardAgendaItem(entry.Id.ToString("D"), "event", entry.Title,
                            string.Empty, entry.StartsAt, false, "plan")));
                }
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException or InvalidOperationException)
            {
                status = "Calendar details are unavailable.";
            }
        }
        else status = "Calendar details are unavailable.";

        token.ThrowIfCancellationRequested();
        if (_disposed || !_dashboardActive || !ShowsHomeBriefing) return;
        var visibleRows = agenda.OrderBy(entry => entry.StartsAt).Take(4).Select(entry => new Dictionary<string, object?>
        {
            ["Key"] = $"{entry.Kind}:{entry.Id}:{entry.StartsAt?.UtcTicks}",
            ["Time"] = entry.IsOverdue ? "Overdue" : entry.StartsAt?.ToLocalTime().ToString("t") ?? string.Empty,
            ["Title"] = entry.Title,
            ["Kind"] = entry.Kind == "event" ? "Calendar event" : "Planner task"
        }).ToArray();
        _dashboardBindings.Set("BriefingTodayItems", visibleRows);
        SetBriefingTodayStatus(visibleRows.Length == 0 && status.Length == 0 ? "Nothing scheduled for today." : status);
    }

    private async Task RefreshStudyWeekAsync(CancellationToken token)
    {
        string? time = null;
        var status = "Recorded Study time is unavailable.";
        if (_briefingStudy is { } study)
        {
            try
            {
                var subjects = (await AwaitHomeSourceAsync(study.GetByModeAsync(HavenMode.Study, token)))
                    .Where(subject => !subject.IsArchived);
                var lessons = await Task.WhenAll(subjects.Select(subject =>
                    AwaitHomeSourceAsync(study.GetLessonsAsync(subject.Id, token))));
                var minutes = StudyLessonMetadata.StudyMinutes(lessons.SelectMany(items => items), DateTimeOffset.Now);
                time = FormatStudyMinutes(minutes.CurrentWeekMinutes);
                status = minutes.WeeklyAverageMinutes > 0
                    ? $"Previous weekly average {FormatStudyMinutes(minutes.WeeklyAverageMinutes)}"
                    : "No previous weekly average yet.";
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException or InvalidOperationException)
            { /* Keep the real source failure and publish the unavailable Study state. */ }
        }

        token.ThrowIfCancellationRequested();
        if (_disposed || !_dashboardActive || !ShowsHomeBriefing) return;
        _dashboardBindings.Set("BriefingStudyTime", time);
        _dashboardBindings.Set("HasBriefingStudyTime", time is not null);
        _dashboardBindings.Set("BriefingStudyStatus", status);
    }

    private void ReportBriefingSnapshotUnavailable()
    {
        if (!ShowsHomeBriefing) return;
        _dashboardBindings.Set("BriefingTodayItems", Array.Empty<Dictionary<string, object?>>());
        SetBriefingTodayStatus("Today’s schedule is unavailable.");
        _dashboardBindings.Set("HasBriefingStudyTime", false);
        _dashboardBindings.Set("BriefingStudyStatus", "Recorded Study time is unavailable.");
    }

    private void SetBriefingTodayStatus(string status)
    {
        _dashboardBindings.Set("BriefingTodayStatus", status);
        _dashboardBindings.Set("HasBriefingTodayStatus", status.Length > 0);
    }

    private static string FormatStudyMinutes(int minutes) => minutes >= 60 ? $"{minutes / 60}h {minutes % 60}m" : $"{minutes}m";
}
