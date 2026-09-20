using Haven.UI;
using Haven.UI.Components;
using HuiButton = Haven.UI.Components.Button;
using HuiText = Haven.UI.Components.Text;
using HuiContainer = Haven.UI.Components.Container;

namespace HavenOS.Apps.Plan.Hui;

public enum PlanHuiAction
{
    NewEvent,
    NewTask,
    NewCalendar,
    Today,
    Week,
    Month,
    PreviousPeriod,
    NextPeriod,
    ToggleCalendar,
    Refresh,
    Settings,
}

public sealed class PlanHuiScene
{
    private readonly Queue<PlanHuiAction> _actions = new();
    private readonly Dictionary<string, HuiButton> _calendarButtons = new();
    private string _currentView = "week";

    public PlanHuiScene()
    {
        Root = new Page
        {
            Name = "Plan.Hui.Root",
            Layout = HavenLayout.Grid,
            Columns = "280px 1fr",
            Rows = "Auto 1fr Auto",
        };
        Root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Background, "Surface");

        // Sidebar - Calendars
        var sidebar = new HuiContainer { Name = "Plan.Hui.Sidebar", Layout = HavenLayout.Vertical };
        sidebar.SetValue(HavenProperties.Row, 1);
        sidebar.SetValue(HavenProperties.Column, 0);
        sidebar.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px"));
        sidebar.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        sidebar.SetValue(HavenProperties.Background, "SurfaceVariant");
        sidebar.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        sidebar.SetValue(HavenProperties.BorderColor, "SurfaceBorder");
        sidebar.SetValue(HavenProperties.BorderRight, HavenLength.Px(1));

        var sidebarHeader = new HuiContainer { Name = "Plan.Hui.SidebarHeader", Layout = HavenLayout.Horizontal };
        sidebarHeader.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        sidebarHeader.Add(new HuiText("Calendars") { Name = "Plan.Hui.CalendarsTitle", Level = TextLevel.H3 });
        NewCalendarButton = NewActionButton("Plan.Hui.NewCalendar", "+", PlanHuiAction.NewCalendar);
        sidebarHeader.Add(NewCalendarButton);
        sidebar.Add(sidebarHeader);

        CalendarList = new HuiContainer { Name = "Plan.Hui.CalendarList", Layout = HavenLayout.Vertical };
        CalendarList.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        sidebar.Add(CalendarList);
        Root.Add(sidebar);

        // Main content - Calendar view
        var mainArea = new HuiContainer { Name = "Plan.Hui.MainArea", Layout = HavenLayout.Vertical };
        mainArea.SetValue(HavenProperties.Row, 1);
        mainArea.SetValue(HavenProperties.Column, 1);
        mainArea.SetValue(HavenProperties.Padding, HavenThickness.Parse("16px"));
        mainArea.SetValue(HavenProperties.Gap, HavenLength.Px(12));

        // View toolbar
        var viewToolbar = new HuiContainer { Name = "Plan.Hui.ViewToolbar", Layout = HavenLayout.Horizontal };
        viewToolbar.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        viewToolbar.SetValue(HavenProperties.Alignment, HavenAlignment.SpaceBetween);

        var viewButtons = new HuiContainer { Name = "Plan.Hui.ViewButtons", Layout = HavenLayout.Horizontal };
        viewButtons.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        TodayButton = NewViewButton("Plan.Hui.Today", "Today", PlanHuiAction.Today);
        WeekButton = NewViewButton("Plan.Hui.Week", "Week", PlanHuiAction.Week);
        MonthButton = NewViewButton("Plan.Hui.Month", "Month", PlanHuiAction.Month);
        viewButtons.Add(TodayButton);
        viewButtons.Add(WeekButton);
        viewButtons.Add(MonthButton);
        viewToolbar.Add(viewButtons);

        var navButtons = new HuiContainer { Name = "Plan.Hui.NavButtons", Layout = HavenLayout.Horizontal };
        navButtons.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        PrevPeriodButton = NewActionButton("Plan.Hui.Prev", "←", PlanHuiAction.PreviousPeriod);
        NextPeriodButton = NewActionButton("Plan.Hui.Next", "→", PlanHuiAction.NextPeriod);
        navButtons.Add(PrevPeriodButton);
        navButtons.Add(NextPeriodButton);
        viewToolbar.Add(navButtons);

        var actionButtons = new HuiContainer { Name = "Plan.Hui.ActionButtons", Layout = HavenLayout.Horizontal };
        actionButtons.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        NewEventButton = NewActionButton("Plan.Hui.NewEvent", "New Event", PlanHuiAction.NewEvent);
        NewTaskButton = NewActionButton("Plan.Hui.NewTask", "New Task", PlanHuiAction.NewTask);
        RefreshButton = NewActionButton("Plan.Hui.Refresh", "Refresh", PlanHuiAction.Refresh);
        SettingsButton = NewActionButton("Plan.Hui.Settings", "Settings", PlanHuiAction.Settings);
        actionButtons.Add(NewEventButton);
        actionButtons.Add(NewTaskButton);
        actionButtons.Add(RefreshButton);
        actionButtons.Add(SettingsButton);
        viewToolbar.Add(actionButtons);

        mainArea.Add(viewToolbar);

        // Calendar grid
        CalendarGrid = new HuiContainer { Name = "Plan.Hui.CalendarGrid", Layout = HavenLayout.Grid };
        CalendarGrid.SetValue(HavenProperties.Columns, "repeat(7, 1fr)");
        CalendarGrid.SetValue(HavenProperties.Gap, HavenLength.Px(1));
        CalendarGrid.SetValue(HavenProperties.Background, "SurfaceBorder");
        CalendarGrid.SetValue(HavenProperties.FlexGrow, 1d);
        mainArea.Add(CalendarGrid);

        Root.Add(mainArea);

        // Status bar
        var statusBar = new HuiContainer { Name = "Plan.Hui.StatusBar", Layout = HavenLayout.Horizontal };
        statusBar.SetValue(HavenProperties.Row, 2);
        statusBar.SetValue(HavenProperties.ColumnSpan, 2);
        statusBar.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 16px"));
        statusBar.SetValue(HavenProperties.Background, "SurfaceVariant");
        statusBar.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        statusBar.SetValue(HavenProperties.BorderColor, "SurfaceBorder");
        statusBar.SetValue(HavenProperties.BorderTop, HavenLength.Px(1));
        statusBar.SetValue(HavenProperties.Gap, HavenLength.Px(16));

        StatusText = new HuiText("Ready") { Name = "Plan.Hui.Status", Level = TextLevel.Body2 };
        StatusText.SetValue(HavenProperties.Foreground, "TextSecondary");
        PeriodText = new HuiText("") { Name = "Plan.Hui.Period", Level = TextLevel.Caption };
        PeriodText.SetValue(HavenProperties.Foreground, "TextSecondary");
        statusBar.Add(StatusText);
        statusBar.Add(new HuiContainer { Name = "Plan.Hui.Spacer" });
        statusBar.Add(PeriodText);
        Root.Add(statusBar);

        Root.ValidateUniqueNames();
        UpdateViewButtons();
    }

    public Page Root { get; }
    public HuiContainer CalendarList { get; }
    public HuiButton NewCalendarButton { get; }
    public HuiContainer CalendarGrid { get; }
    public HuiButton TodayButton { get; }
    public HuiButton WeekButton { get; }
    public HuiButton MonthButton { get; }
    public HuiButton PrevPeriodButton { get; }
    public HuiButton NextPeriodButton { get; }
    public HuiButton NewEventButton { get; }
    public HuiButton NewTaskButton { get; }
    public HuiButton RefreshButton { get; }
    public HuiButton SettingsButton { get; }
    public HuiText StatusText { get; }
    public HuiText PeriodText { get; }
    public string CurrentView => _currentView;

    public bool TryDequeueAction(out PlanHuiAction action) => _actions.TryDequeue(out action);

    public void SetCalendars(IReadOnlyList<PlanCalendar> calendars)
    {
        CalendarList.Clear();
        _calendarButtons.Clear();

        foreach (var cal in calendars)
        {
            var row = new HuiContainer { Name = $"Plan.Hui.CalRow.{cal.Uid}", Layout = HavenLayout.Horizontal };
            row.SetValue(HavenProperties.Gap, HavenLength.Px(8));
            row.SetValue(HavenProperties.Alignment, HavenAlignment.Center);

            var colorIndicator = new HuiContainer { Name = $"Plan.Hui.CalColor.{cal.Uid}" };
            colorIndicator.SetValue(HavenProperties.Width, HavenLength.Px(12));
            colorIndicator.SetValue(HavenProperties.Height, HavenLength.Px(12));
            colorIndicator.SetValue(HavenProperties.BorderRadius, HavenLength.Px(6));
            colorIndicator.SetValue(HavenProperties.Background, cal.Color);
            colorIndicator.SetValue(HavenProperties.Opacity, cal.Visible ? 1d : 0.3d);

            var nameButton = new HuiButton
            {
                Name = $"Plan.Hui.CalName.{cal.Uid}",
                Content = cal.Name,
                Variant = cal.Visible ? ButtonVariant.Ghost : ButtonVariant.Ghost,
            };
            nameButton.SetValue(HavenProperties.TextAlignment, HavenTextAlignment.Start);
            nameButton.SetValue(HavenProperties.FlexGrow, 1d);
            nameButton.SetState(HavenElementState.Disabled, cal.ReadOnly);

            var captured = cal.Uid;
            nameButton.Invoked += (_, _) => ToggleCalendar(captured);
            colorIndicator.Invoked += (_, _) => ToggleCalendar(captured);

            _calendarButtons[cal.Uid] = nameButton;
            row.Add(colorIndicator);
            row.Add(nameButton);
            CalendarList.Add(row);
        }
    }

    public void SetEvents(IReadOnlyList<PlanEvent> events, string period)
    {
        CalendarGrid.Clear();
        PeriodText.Content = period;

        // Create day headers
        var days = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        foreach (var day in days)
        {
            var header = new HuiText(day) { Name = $"Plan.Hui.DayHeader.{day}", Level = TextLevel.Caption };
            header.SetValue(HavenProperties.TextAlignment, HavenTextAlignment.Center);
            header.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px"));
            header.SetValue(HavenProperties.Background, "SurfaceVariant");
            header.SetValue(HavenProperties.Foreground, "TextSecondary");
            CalendarGrid.Add(header);
        }

        // For week view, add event placeholders
        if (_currentView == "week")
        {
            // Add 7 days x 24 hours grid
            for (int day = 0; day < 7; day++)
            {
                for (int hour = 0; hour < 24; hour++)
                {
                    var cell = new HuiContainer { Name = $"Plan.Hui.Cell.{day}.{hour}" };
                    cell.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
                    cell.SetValue(HavenProperties.BorderColor, "SurfaceBorder");
                    cell.SetValue(HavenProperties.MinHeight, HavenLength.Px(40));
                    CalendarGrid.Add(cell);
                }
            }
        }

        // Place events (simplified)
        foreach (var evt in events)
        {
            // Would calculate grid position based on event time
        }
    }

    public void SetTasks(IReadOnlyList<PlanTask> tasks)
    {
        // Would show tasks in sidebar or separate panel
    }

    public void SetView(string view)
    {
        _currentView = view;
        UpdateViewButtons();
    }

    public void SetStatus(string status) => StatusText.Content = status;
    public void SetPeriod(string period) => PeriodText.Content = period;

    private void ToggleCalendar(string uid)
    {
        _actions.Enqueue(PlanHuiAction.ToggleCalendar);
    }

    private void UpdateViewButtons()
    {
        TodayButton.SetState(HavenElementState.Selected, _currentView == "today");
        WeekButton.SetState(HavenElementState.Selected, _currentView == "week");
        MonthButton.SetState(HavenElementState.Selected, _currentView == "month");
    }

    private HuiButton NewViewButton(string name, string content, PlanHuiAction action)
    {
        var button = new HuiButton
        {
            Name = name,
            Content = content,
            Variant = ButtonVariant.Secondary,
        };
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(32));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 16px"));
        button.Invoked += (_, _) => { _actions.Enqueue(action); };
        return button;
    }

    private HuiButton NewActionButton(string name, string content, PlanHuiAction action)
    {
        var button = new HuiButton
        {
            Name = name,
            Content = content,
            Variant = ButtonVariant.Secondary,
        };
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(32));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 16px"));
        button.Invoked += (_, _) => _actions.Enqueue(action);
        return button;
    }
}

public sealed class PlanHuiController(PlanEngine engine, PlanHuiScene? scene = null)
{
    private readonly PlanEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public PlanHuiScene Scene { get; } = scene ?? new PlanHuiScene();

    public async Task InitializeAsync()
    {
        try
        {
            var calendars = await _engine.GetCalendarsAsync();
            Scene.SetCalendars(calendars);
            await RefreshViewAsync();
            Scene.SetStatus($"Loaded {calendars.Count} calendars");
        }
        catch (Exception ex)
        {
            Scene.SetStatus($"Error: {ex.Message}");
        }
    }

    public async Task RefreshViewAsync()
    {
        try
        {
            var range = GetCurrentRange();
            var calendarUids = new List<string>(); // Would get visible calendars
            var events = await _engine.GetEventsAsync(range, calendarUids);
            Scene.SetEvents(events, FormatPeriod(range));
        }
        catch (Exception ex)
        {
            Scene.SetStatus($"Refresh failed: {ex.Message}");
        }
    }

    public async Task ExecuteAsync(PlanHuiAction action)
    {
        try
        {
            switch (action)
            {
                case PlanHuiAction.NewEvent:
                    Scene.SetStatus("New event - not implemented");
                    break;
                case PlanHuiAction.NewTask:
                    Scene.SetStatus("New task - not implemented");
                    break;
                case PlanHuiAction.NewCalendar:
                    Scene.SetStatus("New calendar - not implemented");
                    break;
                case PlanHuiAction.Today:
                    Scene.SetView("today");
                    await RefreshViewAsync();
                    break;
                case PlanHuiAction.Week:
                    Scene.SetView("week");
                    await RefreshViewAsync();
                    break;
                case PlanHuiAction.Month:
                    Scene.SetView("month");
                    await RefreshViewAsync();
                    break;
                case PlanHuiAction.PreviousPeriod:
                    // Navigate to previous period
                    await RefreshViewAsync();
                    break;
                case PlanHuiAction.NextPeriod:
                    // Navigate to next period
                    await RefreshViewAsync();
                    break;
                case PlanHuiAction.ToggleCalendar:
                    // Toggle calendar visibility
                    break;
                case PlanHuiAction.Refresh:
                    await RefreshViewAsync();
                    break;
                case PlanHuiAction.Settings:
                    Scene.SetStatus("Settings - not implemented");
                    break;
            }
        }
        catch (Exception ex)
        {
            Scene.SetStatus($"Error: {ex.Message}");
        }
    }

    private PlanTimeRange GetCurrentRange()
    {
        var now = DateTimeOffset.Now;
        return _currentView switch
        {
            "today" => new PlanTimeRange(now.Date, now.Date.AddDays(1)),
            "week" => new PlanTimeRange(now.Date.AddDays(-(int)now.DayOfWeek + 1), now.Date.AddDays(-(int)now.DayOfWeek + 8)),
            "month" => new PlanTimeRange(new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset), new DateTimeOffset(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month), 23, 59, 59, now.Offset)),
            _ => new PlanTimeRange(now.Date.AddDays(-(int)now.DayOfWeek + 1), now.Date.AddDays(-(int)now.DayOfWeek + 8))
        };
    }

    private string FormatPeriod(PlanTimeRange range) => $"{range.Start:MMM d} - {range.End:MMM d, yyyy}";
}