using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Haven.Desktop.Dashboard;
using Haven.Desktop.HavenUI.Components;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

public sealed partial class NewDashboardPage
{
    private DashboardEditPlanner? _dashboardEditPlanner;
    private HavenPanel? _dashboardAssistantPanel;
    private HavenTextInput? _dashboardAssistantInput;
    private TextBlock? _dashboardAssistantStatus;
    private HavenPrimaryButton? _dashboardAssistantApply;
    private HavenButton? _dashboardAssistantRevert;
    private CancellationTokenSource? _dashboardAssistantCancellation;
    private DashboardAssistantSnapshot? _lastDashboardAssistantSnapshot;
    private bool _dashboardAssistantBusy;

    internal void EnableDashboardAssistant(DashboardEditPlanner planner)
    {
        ArgumentNullException.ThrowIfNull(planner);
        if (_dashboardEditPlanner is not null) return;
        _dashboardEditPlanner = planner;
        ConfigureDashboardAssistantPanel();
        EditWithHavenButton.Click += (_, _) => ToggleDashboardAssistant();
    }

    private void ConfigureDashboardAssistantPanel()
    {
        if (_dashboardAssistantPanel is not null || _dashboardRoot is not { } root) return;

        var title = new TextBlock
        {
            Text = "Edit with 9to1",
            FontSize = 20,
            FontWeight = Avalonia.Media.FontWeight.ExtraBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var close = new HavenButton { Content = "Close", Classes = { "subtle" } };
        close.Click += (_, _) => ToggleDashboardAssistant(forceVisible: false);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        header.Children.Add(title);
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        _dashboardAssistantInput = new HavenTextInput
        {
            PlaceholderText = "e.g. Make Plan wide, hide Browse, and move Automations underneath",
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MinHeight = 112,
            MaxHeight = 190,
            MaxLength = 1200
        };
        _dashboardAssistantStatus = new TextBlock
        {
            Text = "9to1 can show, hide, move, resize, reset, or rename this page.",
            Classes = { "muted" },
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 11
        };
        _dashboardAssistantApply = new HavenPrimaryButton { Content = "Apply with 9to1" };
        _dashboardAssistantApply.Click += async (_, _) => await RunHomeEventAsync(_ => ApplyDashboardAssistantAsync());
        _dashboardAssistantRevert = new HavenButton { Content = "Revert last 9to1 change", Classes = { "subtle" }, IsEnabled = false };
        _dashboardAssistantRevert.Click += async (_, _) => await RunHomeEventAsync(_ => RevertDashboardAssistantAsync());

        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        _dashboardAssistantApply.Margin = new Thickness(0, 0, 8, 8);
        _dashboardAssistantRevert.Margin = new Thickness(0, 0, 0, 8);
        buttons.Children.Add(_dashboardAssistantApply);
        buttons.Children.Add(_dashboardAssistantRevert);

        var body = new StackPanel { Spacing = 11 };
        body.Children.Add(header);
        body.Children.Add(new TextBlock
        {
            Text = "Describe the layout you want. The dashboard stays visible while 9to1 applies one reversible, validated layout change.",
            Classes = { "muted" },
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 12
        });
        body.Children.Add(_dashboardAssistantInput);
        body.Children.Add(buttons);
        body.Children.Add(_dashboardAssistantStatus);

        _dashboardAssistantPanel = new HavenPanel
        {
            MaxWidth = 410,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(16, 118, 16, 82),
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(20),
            IsVisible = false,
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = body
            }
        };
        Grid.SetRowSpan(_dashboardAssistantPanel, 3);
        _dashboardAssistantPanel.SetValue(Panel.ZIndexProperty, 40);
        root.AddChild(_dashboardAssistantPanel);
    }

    private void ToggleDashboardAssistant(bool? forceVisible = null)
    {
        if (_dashboardAssistantPanel is null) return;
        _dashboardAssistantPanel.IsVisible = forceVisible ?? !_dashboardAssistantPanel.IsVisible;
        if (_dashboardAssistantPanel.IsVisible) _dashboardAssistantInput?.Focus();
    }

    private Task ApplyDashboardAssistantAsync() => RunHomeWorkAsync(_ => ApplyDashboardAssistantCoreAsync());

    private async Task ApplyDashboardAssistantCoreAsync()
    {
        if (_dashboardEditPlanner is null || _dashboardAssistantInput is null || _dashboardAssistantStatus is null
            || _dashboardAssistantApply is null || _dashboardAssistantBusy) return;

        var instruction = _dashboardAssistantInput.Text?.Trim() ?? string.Empty;
        if (instruction.Length == 0)
        {
            _dashboardAssistantStatus.Text = "Describe the change you want first.";
            _dashboardAssistantInput.Focus();
            return;
        }

        _dashboardAssistantCancellation?.Cancel();
        _dashboardAssistantCancellation?.Dispose();
        _dashboardAssistantCancellation = new CancellationTokenSource();
        var token = _dashboardAssistantCancellation.Token;
        _dashboardAssistantBusy = true;
        _dashboardAssistantApply.IsEnabled = false;
        _dashboardAssistantStatus.Text = "Planning a safe dashboard edit…";

        try
        {
            var views = _widgetViews.Count > 0
                ? _widgetViews
                : _widgetProviders.Select(provider => new DashboardWidgetViewState(provider.Definition, null, DashboardWidgetDataState.Loading)).ToArray();
            var page = SelectedPage;
            var snapshot = new DashboardAssistantSnapshot(page.Id, page.Title, _widgetCanvas.Placements.ToArray());
            var result = await AwaitHomeSourceAsync(_dashboardEditPlanner.PlanAsync(instruction, page.Title, views, snapshot.Layout, token));
            token.ThrowIfCancellationRequested();
            if (_disposed || _homeWork.IsRetiring) return;
            if (SelectedPage.Id != snapshot.PageId || SelectedPage.Title != snapshot.Title ||
                !_widgetCanvas.Placements.SequenceEqual(snapshot.Layout))
            {
                _dashboardAssistantStatus.Text = "The dashboard changed while the edit was being planned. Your changes were preserved; ask again to use the current layout.";
                return;
            }
            if (!result.Succeeded || result.Plan is null)
            {
                _dashboardAssistantStatus.Text = result.Message;
                return;
            }

            var applied = DashboardEditPlanApplier.Apply(
                result.Plan,
                page.Title,
                GetAllWidgetDefinitions(),
                _widgetCanvas.Placements);
            var titleChanged = !applied.Title.Equals(page.Title, StringComparison.Ordinal);
            var layoutChanged = _widgetCanvas.ApplyLayout(applied.Layout);

            if (titleChanged)
            {
                var index = _pages.FindIndex(item => item.Id.Equals(page.Id, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) _pages[index] = _pages[index] with { Title = applied.Title };
                await SavePageStateAsync(CancellationToken.None);
                RebuildPageTabs();
            }

            if (!layoutChanged && !titleChanged)
            {
                _dashboardAssistantStatus.Text = "That plan would not visibly change this dashboard.";
                return;
            }

            if (string.Equals(_dashboardAssistantInput.Text?.Trim(), instruction, StringComparison.Ordinal))
                _dashboardAssistantInput.Text = string.Empty;
            _lastDashboardAssistantSnapshot = snapshot;
            if (_dashboardAssistantRevert is not null) _dashboardAssistantRevert.IsEnabled = true;
            _dashboardAssistantStatus.Text = $"{result.Plan.Summary} · Applied as one reversible change.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _dashboardAssistantStatus.Text = "Dashboard edit cancelled.";
        }
        catch (Exception ex)
        {
            _dashboardAssistantStatus.Text = $"Dashboard edit failed safely: {ex.Message}";
        }
        finally
        {
            _dashboardAssistantBusy = false;
            if (!_disposed && !_homeWork.IsRetiring) _dashboardAssistantApply.IsEnabled = true;
        }
    }

    private Task RevertDashboardAssistantAsync() => RunHomeWorkAsync(_ => RevertDashboardAssistantCoreAsync());

    private async Task RevertDashboardAssistantCoreAsync()
    {
        if (_lastDashboardAssistantSnapshot is not { } snapshot || _dashboardAssistantStatus is null) return;
        if (!snapshot.PageId.Equals(_selectedPageId, StringComparison.OrdinalIgnoreCase))
        {
            _dashboardAssistantStatus.Text = "Switch back to the dashboard page 9to1 edited before reverting it.";
            return;
        }

        var pageIndex = _pages.FindIndex(item => item.Id.Equals(snapshot.PageId, StringComparison.OrdinalIgnoreCase));
        if (pageIndex < 0)
        {
            _dashboardAssistantStatus.Text = "That dashboard page no longer exists.";
            return;
        }

        _pages[pageIndex] = _pages[pageIndex] with { Title = snapshot.Title };
        await SavePageStateAsync(CancellationToken.None);
        _widgetCanvas.ApplyLayout(snapshot.Layout);
        RebuildPageTabs();
        _lastDashboardAssistantSnapshot = null;
        if (_dashboardAssistantRevert is not null) _dashboardAssistantRevert.IsEnabled = false;
        _dashboardAssistantStatus.Text = "Reverted the last 9to1 dashboard change.";
    }

    private sealed record DashboardAssistantSnapshot(
        string PageId,
        string Title,
        IReadOnlyList<DashboardWidgetPlacement> Layout);
}
