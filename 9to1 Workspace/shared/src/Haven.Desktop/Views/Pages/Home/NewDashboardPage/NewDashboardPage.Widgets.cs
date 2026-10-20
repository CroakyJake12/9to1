using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Dashboard;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Components;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed record DashboardWidgetLayoutState(
    int Version,
    Dictionary<string, List<DashboardWidgetPlacement>> Pages);

public sealed partial class NewDashboardPage
{
    private const string WidgetLayoutStateKey = "dashboard.widget-layouts.v1";
    private static readonly TimeSpan WidgetStaleAfter = TimeSpan.FromMinutes(10);

    private IDashboardRepository? _widgetDashboard;
    private IDashboardLayoutRepository? _widgetLegacyLayout;
    private IReadOnlyList<IDashboardTileProvider> _widgetProviders = [];
    private readonly DashboardWidgetCanvas _widgetCanvas = new();
    private readonly Dictionary<string, WidgetCacheEntry> _widgetCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _widgetSaveGate = new(1, 1);
    private DashboardWidgetLayoutState? _widgetLayoutState;
    private IReadOnlyList<DashboardWidgetViewState> _widgetViews = [];
    private CancellationTokenSource? _widgetRefreshCancellation;
    private Task? _widgetRefreshTask;
    private bool _widgetCustomizing;
    private bool _widgetRefreshInProgress;
    private bool _widgetWorkspaceConfigured;
    private TextBlock? _widgetStatus;
    private StackPanel? _widgetHiddenHost;
    private WrapPanel? _widgetHiddenPanel;
    private TextBlock? _widgetHiddenEmpty;
    private Button? _widgetArrangeButton;
    private Button? _widgetUndoButton;
    private Button? _widgetRedoButton;

    public NewDashboardPage(
        HavenEventBus bus,
        IModeRegistry modes,
        IModeUsageRepository usage,
        IPinRepository pins,
        IConversationRepository conversations,
        IVersionedSettingsStore settings,
        IDashboardRepository dashboard,
        IDashboardLayoutRepository dashboardLayout,
        IEnumerable<IDashboardTileProvider> providers)
        : this(bus, modes, usage, pins, conversations, settings)
    {
        _widgetDashboard = dashboard;
        _widgetLegacyLayout = dashboardLayout;
        _widgetProviders = BuiltInDashboardTiles.Create()
            .Concat(providers)
            .GroupBy(provider => provider.Definition.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(provider => provider.Definition.DefaultOrder)
            .ThenBy(provider => provider.Definition.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ConfigureWidgetWorkspace();
    }

    public event EventHandler<string>? DashboardActionRequested;

    private void ConfigureWidgetWorkspace()
    {
        if (_widgetWorkspaceConfigured) return;
        _widgetWorkspaceConfigured = true;
        DynamicRowsPanel.Children.Clear();

        SetDashboardValue(_widgetControls!, "IsVisible", true);
        AddWidgetButton.Click += (_, _) => ShowCustomWidgetEditor(null, AddWidgetButton);
        RefreshWidgetsButton.Click += async (_, _) => await RunHomeEventAsync(RefreshWidgetSurfaceAsync);
        _widgetUndoButton!.Click += (_, _) => _widgetCanvas.Undo();
        _widgetRedoButton!.Click += (_, _) => _widgetCanvas.Redo();
        _widgetArrangeButton!.Click += (_, _) =>
        {
            _widgetCustomizing = !_widgetCustomizing;
            _widgetCanvas.SetCustomizing(_widgetCustomizing);
            SetDashboardValue(_widgetArrangeButton, "Content", _widgetCustomizing ? "Done arranging" : "Arrange");
            RebuildHiddenWidgetPalette();
            RefreshWidgetToolbar();
        };
        _widgetHiddenPanel = new WrapPanel { Orientation = Orientation.Horizontal };
        _widgetHiddenEmpty = new TextBlock { Text = "No hidden widgets.", Classes = { "muted2" }, FontSize = 11 };
        _widgetHiddenHost = new StackPanel { Spacing = 7, IsVisible = false };
        _widgetHiddenHost.Children.Add(new TextBlock { Text = "Hidden widgets", FontWeight = FontWeight.SemiBold, FontSize = 13 });
        _widgetHiddenHost.Children.Add(_widgetHiddenPanel);
        _widgetHiddenHost.Children.Add(_widgetHiddenEmpty);

        _widgetCanvas.LayoutChanged += OnWidgetLayoutChanged;
        _widgetCanvas.OpenRequested += actionKey =>
        {
            if (!TryOpenCustomWidget(actionKey)) DashboardActionRequested?.Invoke(this, actionKey);
        };

        DynamicRowsPanel.Children.Add(_widgetCanvas);
        DynamicRowsPanel.Children.Add(_widgetHiddenHost);
    }

    private void RenderWidgetPage()
    {
        if (_disposed || _homeWork.IsRetiring) return;
        if (_widgetDashboard is null || !_widgetWorkspaceConfigured) return;
        RebuildPageTabs();
        RefreshClock();
        var placements = GetCurrentWidgetPlacements();
        var providerViews = _widgetViews
            .Where(view => !view.Definition.ProviderKey.Equals("custom-local", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var views = AppendCustomWidgetViews(providerViews.Length > 0
            ? providerViews
            : _widgetProviders.Select(provider => new DashboardWidgetViewState(
                provider.Definition, null, DashboardWidgetDataState.Loading)));
        _widgetViews = views;
        _widgetCanvas.SetWidgets(views, placements, _widgetCustomizing);
        if (_widgetStatus is not null && _widgetRefreshInProgress) SetDashboardValue(_widgetStatus, "Text", "Refreshing live widget data…");
        RebuildHiddenWidgetPalette();
        RefreshWidgetToolbar();
        if (_widgetViews.Count == 0 && !_widgetRefreshInProgress)
            _ = RefreshWidgetSurfaceAsync(CancellationToken.None);
    }

    private Task RefreshWidgetSurfaceAsync(CancellationToken cancellationToken) => RunHomeWorkAsync(RefreshWidgetSurfaceWorkAsync, cancellationToken);

    private Task RefreshWidgetSurfaceWorkAsync(CancellationToken cancellationToken)
    {
        if (_widgetDashboard is null || _disposed || !_dashboardActive) return Task.CompletedTask;
        if (_widgetRefreshTask is { IsCompleted: false } pending)
            return _widgetRefreshCancellation?.IsCancellationRequested == true
                ? RestartWidgetRefreshAfterCancellationAsync(pending, cancellationToken)
                : pending.WaitAsync(cancellationToken);
        return _widgetRefreshTask = RefreshWidgetSurfaceCoreAsync(cancellationToken);
    }

    private async Task RestartWidgetRefreshAfterCancellationAsync(Task pending, CancellationToken cancellationToken)
    {
        await pending.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await RefreshWidgetSurfaceAsync(cancellationToken);
    }

    private async Task RefreshWidgetSurfaceCoreAsync(CancellationToken cancellationToken)
    {
        _widgetRefreshCancellation?.Cancel();
        _widgetRefreshCancellation?.Dispose();
        _widgetRefreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _widgetRefreshCancellation.Token;
        _widgetRefreshInProgress = true;
        if (_widgetStatus is not null) SetDashboardValue(_widgetStatus, "Text", "Refreshing live widget data…");

        string finalStatus;
        try
        {
            await EnsureCustomWidgetStateLoadedAsync(token);
            await EnsureWidgetLayoutStateLoadedAsync(token);
            var snapshot = await AwaitHomeSourceAsync(_widgetDashboard!.GetSnapshotAsync(DateTimeOffset.UtcNow, token));
            token.ThrowIfCancellationRequested();
            var staleSnapshot = DateTimeOffset.UtcNow - snapshot.CapturedAt > WidgetStaleAfter;
            // The briefing sections publish the authorized current source immediately, independently of
            // every unrelated widget provider: one held provider must not hide available Today/Recent.
            UpdateBriefing(snapshot, staleSnapshot, null);
            var loaded = await Task.WhenAll(_widgetProviders.Select(provider => LoadWidgetAsync(provider, snapshot, staleSnapshot, token)));
            token.ThrowIfCancellationRequested();

            _widgetViews = AppendCustomWidgetViews(loaded.Select(item => item.View));
            foreach (var item in loaded)
            {
                if (item.FreshData is not null)
                    _widgetCache[item.View.Definition.Key] = new WidgetCacheEntry(item.FreshData, snapshot.CapturedAt);
            }
            finalStatus = staleSnapshot
                ? $"Snapshot is older than {WidgetStaleAfter.TotalMinutes:0} minutes; values are marked stale."
                : $"Updated {DateTimeOffset.Now:t} · {loaded.Count(item => item.View.State == DashboardWidgetDataState.Ready)} live widgets";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested || _disposed || !_dashboardActive) return;
            // A source failure is treated as an authority/availability boundary: sensitive row content
            // is purged immediately and no old data is retained or shown as current. The authored CUI
            // surfaces a distinct revoked/error explanation with retry.
            RevokeBriefing("Dashboard data is unavailable. This area was cleared and cannot be shown until refreshed.");
            _widgetViews = AppendCustomWidgetViews(_widgetProviders.Select(provider =>
            {
                if (_widgetCache.TryGetValue(provider.Definition.Key, out var cached))
                    return new DashboardWidgetViewState(provider.Definition, cached.Data, DashboardWidgetDataState.Stale, ex.Message);
                return new DashboardWidgetViewState(provider.Definition, null, DashboardWidgetDataState.Error, ex.Message);
            }));
            finalStatus = _widgetCache.Count > 0
                ? "Dashboard refresh failed; last known values are marked stale."
                : "Dashboard data is unavailable. No values have been invented.";
        }
        finally
        {
            _widgetRefreshInProgress = false;
        }

        if (token.IsCancellationRequested || _disposed || !_dashboardActive) return;
        RenderWidgetPage();
        if (_widgetStatus is not null) SetDashboardValue(_widgetStatus, "Text", finalStatus);
    }

    private async Task<(DashboardWidgetViewState View, DashboardTileData? FreshData)> LoadWidgetAsync(
        IDashboardTileProvider provider,
        DashboardSnapshot snapshot,
        bool staleSnapshot,
        CancellationToken token)
    {
        try
        {
            var data = await AwaitHomeSourceAsync(provider.GetDataAsync(snapshot, token));
            return (new DashboardWidgetViewState(
                provider.Definition,
                data,
                staleSnapshot ? DashboardWidgetDataState.Stale : DashboardWidgetDataState.Ready), data);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (_widgetCache.TryGetValue(provider.Definition.Key, out var cached))
                return (new DashboardWidgetViewState(provider.Definition, cached.Data, DashboardWidgetDataState.Stale, ex.Message), null);
            return (new DashboardWidgetViewState(provider.Definition, null, DashboardWidgetDataState.Error, ex.Message), null);
        }
    }

    private async Task EnsureWidgetLayoutStateLoadedAsync(CancellationToken token)
    {
        if (_widgetLayoutState is not null) return;
        var stored = await AwaitHomeSourceAsync(_settings.GetAsync<DashboardWidgetLayoutState>(WidgetLayoutStateKey, token));
        _widgetLayoutState = stored is { Version: 1 }
            ? new DashboardWidgetLayoutState(1, new Dictionary<string, List<DashboardWidgetPlacement>>(stored.Pages, StringComparer.OrdinalIgnoreCase))
            : new DashboardWidgetLayoutState(1, new Dictionary<string, List<DashboardWidgetPlacement>>(StringComparer.OrdinalIgnoreCase));

        if (_widgetLayoutState.Pages.ContainsKey(HomePageId)) return;
        IReadOnlyList<DashboardTileLayout> legacy = [];
        if (_widgetLegacyLayout is not null)
        {
            try { legacy = await AwaitHomeSourceAsync(_widgetLegacyLayout.GetAsync(token)); }
            catch { legacy = []; }
        }
        var legacyByKey = legacy.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
        var seedDefinitions = GetAllWidgetDefinitions().Select(definition =>
        {
            return legacyByKey.TryGetValue(definition.Key, out var item)
                ? definition with { DefaultOrder = item.Order, DefaultSize = item.Size }
                : definition;
        }).ToArray();
        IReadOnlyList<DashboardWidgetPlacement> seeded = DashboardWidgetLayoutEngine.EnsurePlacements(seedDefinitions);
        foreach (var hidden in legacy.Where(item => !item.IsVisible))
            seeded = DashboardWidgetLayoutEngine.SetVisibility(seeded, hidden.Key, false);
        _widgetLayoutState.Pages[HomePageId] = seeded.ToList();
        await SaveWidgetLayoutStateAsync(syncLegacyHome: false);
    }

    private IReadOnlyList<DashboardWidgetPlacement> GetCurrentWidgetPlacements()
    {
        if (_widgetLayoutState is null)
            return DashboardWidgetLayoutEngine.EnsurePlacements(GetAllWidgetDefinitions());
        if (_widgetLayoutState.Pages.TryGetValue(_selectedPageId, out var stored))
        {
            var reconciled = DashboardWidgetLayoutEngine.EnsurePlacements(GetAllWidgetDefinitions(), stored).ToList();
            if (!stored.SequenceEqual(reconciled))
            {
                _widgetLayoutState.Pages[_selectedPageId] = reconciled;
                _ = SaveWidgetLayoutStateAsync(_selectedPageId.Equals(HomePageId, StringComparison.OrdinalIgnoreCase));
            }
            return reconciled;
        }
        var created = DashboardWidgetLayoutEngine.EnsurePlacements(GetAllWidgetDefinitions()).ToList();
        _widgetLayoutState.Pages[_selectedPageId] = created;
        _ = SaveWidgetLayoutStateAsync(syncLegacyHome: false);
        return created;
    }

    private void OnWidgetLayoutChanged(IReadOnlyList<DashboardWidgetPlacement> layout)
    {
        if (_widgetLayoutState is null)
            _widgetLayoutState = new DashboardWidgetLayoutState(1, new Dictionary<string, List<DashboardWidgetPlacement>>(StringComparer.OrdinalIgnoreCase));
        _widgetLayoutState.Pages[_selectedPageId] = layout.ToList();
        RebuildHiddenWidgetPalette();
        RefreshWidgetToolbar();
        _ = SaveWidgetLayoutStateAsync(_selectedPageId.Equals(HomePageId, StringComparison.OrdinalIgnoreCase));
    }

    private Task SaveWidgetLayoutStateAsync(bool syncLegacyHome) => RunHomeSaveAsync(() => SaveWidgetLayoutStateCoreAsync(syncLegacyHome));

    private async Task SaveWidgetLayoutStateCoreAsync(bool syncLegacyHome)
    {
        if (_widgetLayoutState is null) return;
        await _widgetSaveGate.WaitAsync();
        try
        {
            var snapshot = new DashboardWidgetLayoutState(
                1,
                _widgetLayoutState.Pages.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.ToList(),
                    StringComparer.OrdinalIgnoreCase));
            await AwaitHomeSourceAsync(_settings.SetAsync(WidgetLayoutStateKey, snapshot, CancellationToken.None));

            if (syncLegacyHome && _widgetLegacyLayout is not null && snapshot.Pages.TryGetValue(HomePageId, out var home))
            {
                var legacyKeys = _widgetProviders
                    .Select(provider => provider.Definition.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var ordered = home
                    .Where(item => legacyKeys.Contains(item.Key))
                    .OrderBy(item => item.IsVisible ? 0 : 1)
                    .ThenBy(item => item.Row)
                    .ThenBy(item => item.Column)
                    .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var legacy = ordered.Select((item, index) => new DashboardTileLayout(
                    1,
                    item.Key,
                    index,
                    item.IsVisible,
                    DashboardWidgetLayoutEngine.ToTileSize(item))).ToArray();
                await AwaitHomeSourceAsync(_widgetLegacyLayout.SaveAsync(legacy, CancellationToken.None));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Dashboard] Could not persist widget layout: {ex.Message}");
            throw;
        }
        finally
        {
            _widgetSaveGate.Release();
        }
    }

    private void RebuildHiddenWidgetPalette()
    {
        if (_widgetHiddenHost is null || _widgetHiddenPanel is null || _widgetHiddenEmpty is null) return;
        _widgetHiddenHost.IsVisible = _widgetCustomizing;
        _widgetHiddenPanel.Children.Clear();
        if (!_widgetCustomizing) return;
        var hidden = _widgetCanvas.Placements.Where(item => !item.IsVisible).ToArray();
        foreach (var placement in hidden)
        {
            var view = _widgetViews.FirstOrDefault(item => item.Definition.Key.Equals(placement.Key, StringComparison.OrdinalIgnoreCase));
            var title = view?.Definition.Title ?? placement.Key;
            var button = new HavenButton
            {
                Content = $"Show {title}",
                Classes = { "subtle" },
                Margin = new Thickness(0, 0, 7, 7)
            };
            button.Click += (_, _) => _widgetCanvas.ShowWidget(placement.Key);
            _widgetHiddenPanel.Children.Add(button);
        }
        _widgetHiddenEmpty.IsVisible = hidden.Length == 0;
    }

    private void ShowWidgetPageEditor(DashboardPageProfile? existing)
    {
        var isNew = existing is null;
        var source = existing ?? new DashboardPageProfile(
            Guid.NewGuid().ToString("N"), "New page", [], IncludeAllPinned: false, _pages.Count);
        var titleBox = new HavenTextInput
        {
            Text = source.Title,
            PlaceholderText = "Page name",
            MaxLength = 60
        };
        var save = new HavenPrimaryButton
        {
            Content = isNew ? "Create page" : "Save page",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var remove = new HavenNegativeButton
        {
            Content = "Delete page",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsVisible = !isNew && !source.Id.Equals(HomePageId, StringComparison.OrdinalIgnoreCase)
        };
        var editor = new StackPanel
        {
            Width = 420,
            Spacing = 9,
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock
                {
                    Text = isNew ? "Create dashboard page" : "Edit dashboard page",
                    FontSize = 21,
                    FontWeight = FontWeight.ExtraBold
                },
                new TextBlock
                {
                    Text = "Name this workspace here. Add, hide, move and resize its widgets directly on the dashboard.",
                    Classes = { "muted" },
                    TextWrapping = TextWrapping.Wrap
                },
                titleBox,
                save,
                remove
            }
        };
        var flyout = new HavenDropdown
        {
            Content = new HavenDropdownCard
            {
                CornerRadius = new CornerRadius(22),
                Child = editor
            }
        };

        save.Click += async (_, _) => await RunHomeEventAsync(async _ =>
        {
            var title = titleBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                titleBox.Focus();
                return;
            }

            var updated = source with
            {
                Title = title,
                IncludeAllPinned = false,
                ModeIds = []
            };
            if (isNew) _pages.Add(updated);
            else
            {
                var index = _pages.FindIndex(page => page.Id.Equals(source.Id, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) _pages[index] = updated;
            }
            _selectedPageId = updated.Id;
            await SavePageStateAsync(CancellationToken.None);
            RenderPage();
            flyout.Hide();
        });

        var deleteArmed = false;
        remove.Click += async (_, _) => await RunHomeEventAsync(async _ =>
        {
            if (!deleteArmed)
            {
                deleteArmed = true;
                remove.Content = "Confirm delete";
                return;
            }

            _pages.RemoveAll(page => page.Id.Equals(source.Id, StringComparison.OrdinalIgnoreCase));
            if (_widgetLayoutState?.Pages.Remove(source.Id) == true)
                await SaveWidgetLayoutStateAsync(syncLegacyHome: false);
            await EnsureCustomWidgetStateLoadedAsync(CancellationToken.None);
            if (_customWidgetState?.Pages.Remove(source.Id) == true)
                await SaveCustomWidgetStateAsync();
            _selectedPageId = _pages.FirstOrDefault()?.Id ?? HomePageId;
            await SavePageStateAsync(CancellationToken.None);
            RenderPage();
            flyout.Hide();
        });

        ShowHomeEditor(flyout, isNew ? AddPageButton : ConfigurePageButton);
        titleBox.SelectAll();
        titleBox.Focus();
    }

    private void RefreshWidgetToolbar()
    {
        if (_widgetUndoButton is not null) SetDashboardValue(_widgetUndoButton, "IsEnabled", _widgetCanvas.CanUndo);
        if (_widgetRedoButton is not null) SetDashboardValue(_widgetRedoButton, "IsEnabled", _widgetCanvas.CanRedo);
    }

    private sealed record WidgetCacheEntry(DashboardTileData Data, DateTimeOffset CapturedAt);
}
