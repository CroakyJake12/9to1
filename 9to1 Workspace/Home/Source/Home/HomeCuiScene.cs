using CakeOS.Cui;
using CakeOS.Cui.Language;
using HavenOS.Home.Core;
using System.ComponentModel;

namespace HavenOS.Home;

public enum HomeCuiAction
{
    InstallAllUpdates,
    NavigateDashboard,
    NavigateApps,
    NavigateLibrary,
    NavigateEvents,
    NavigateDiscover,
    NavigateMesh,
    NavigateSettings,
    NavigatePermissions,
    NavigateNotifications,
    NavigateSpaces,
}

/// <summary>
/// Loads the authored Home surface and exposes only typed intent and state for a future CUI host.
/// Rendering is intentionally outside this domain assembly.
/// </summary>
public sealed class HomeCuiSurface : ICuiBindingContext, INotifyPropertyChanged
{
    private readonly Queue<HomeCuiAction> _actions = new();

    public HomeCuiSurface(CuiDocument document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public CuiDocument Document { get; }
    private readonly Dictionary<string, HomeTileContent> _tileContent = new(StringComparer.Ordinal);
    private HomeDashboardSnapshot? _snapshot;
    private HomeDashboardLayout? _layout;
    private HomeLibraryPage? _libraryPage;
    private HomeEventsPage? _eventsPage;
    private HomeRoute _currentRoute = HomeRoute.Dashboard;
    private string _navigationStatus = string.Empty;
    private bool _canInstallAll;
    public event PropertyChangedEventHandler? PropertyChanged;
    public HomeDashboardSnapshot? Snapshot { get => _snapshot; private set { _snapshot = value; Notify(nameof(Snapshot)); Notify(nameof(OperationStatus)); Notify(nameof(CatalogSummary)); Notify(nameof(RuntimeSummary)); } }
    public bool CanInstallAll { get => _canInstallAll; private set { _canInstallAll = value; Notify(nameof(CanInstallAll)); } }
    public string OperationStatus { get; private set; } = string.Empty;
    public HomeRoute CurrentRoute { get => _currentRoute; private set { _currentRoute = value; Notify(nameof(CurrentRoute)); Notify(nameof(IsDashboard)); Notify(nameof(IsLibrary)); Notify(nameof(IsEvents)); } }
    public HomeNavigationState? Navigation { get; private set; }
    public HomeDashboardLayout? Layout { get => _layout; private set { _layout = value; Notify(nameof(Layout)); Notify(nameof(AllowAIGeneratedTiles)); Notify(nameof(AllowAIReorder)); } }
    public HomeLibraryPage? LibraryPage { get => _libraryPage; private set { _libraryPage = value; Notify(nameof(LibrarySummary)); Notify(nameof(LibraryItems)); } }
    public HomeEventsPage? EventsPage { get => _eventsPage; private set { _eventsPage = value; Notify(nameof(EventsSummary)); Notify(nameof(EventItems)); } }
    public string NavigationStatus { get => _navigationStatus; private set { _navigationStatus = value; Notify(nameof(NavigationStatus)); } }
    public string LibrarySearchText { get; set; } = string.Empty;
    public string LibrarySort { get; set; } = "recent";
    public IReadOnlyList<HomeArtifactReference> LibraryItems => LibraryPage?.Items ?? [];
    public IReadOnlyList<HomeActivityEvent> EventItems => EventsPage?.Items ?? [];
    public HomeLibrarySelection? SelectedLibraryItem { get; private set; }
    public HomeEventSelection? SelectedEventObject { get; private set; }
    public string? SelectedDashboardTileId { get; private set; }
    public bool IsDashboard => CurrentRoute == HomeRoute.Dashboard;
    public bool IsLibrary => CurrentRoute == HomeRoute.Library;
    public bool IsEvents => CurrentRoute == HomeRoute.Events;
    public bool? AllowAIGeneratedTiles => Layout?.AllowAiGeneratedTiles;
    public bool? AllowAIReorder => Layout?.AllowAiReorder;
    public string CatalogSummary => Snapshot is null ? "App catalogue is not connected" : $"{Snapshot.InstalledApps.Apps.Count} installed · {Snapshot.Catalog.Apps.Count} available";
    public string RuntimeSummary => Snapshot is null ? "Runtime status is not connected" : $"Runtime {Snapshot.Runtime.Runtime.State}; model {Snapshot.Runtime.Model.State}; voice {Snapshot.Runtime.Voice.State}";
    public string BriefSummary => _tileContent.Count == 0 ? "No dashboard tiles have loaded." : string.Join("  ·  ", _tileContent.Values.Select(x => x.Summary));
    public string DashboardTilesSummary => Layout is null || Layout.Tiles.Count == 0 ? "No dashboard tiles have been added." : string.Join("\n", Layout.Tiles
        .OrderBy(tile => tile.Order).Select(tile => $"{tile.TileType} · {tile.Size} · {(tile.Visibility == HomeTileVisibility.Visible ? "visible" : "hidden")}{(tile.Pinned ? " · pinned" : string.Empty)}{(tile.Locked ? " · locked" : string.Empty)}"));
    public string LibrarySummary => LibraryPage is null ? "Search authorised artifacts across your apps." : LibraryPage.Succeeded
        ? $"{LibraryPage.Items.Count} artifacts{(LibraryPage.HasMore ? " · more available" : string.Empty)}" : $"{LibraryPage.Code}: {LibraryPage.Message}";
    public string LibraryItemsSummary => LibraryItems.Count == 0 ? "No matching artifacts." : string.Join("\n", LibraryItems.Select(item =>
        $"{item.DisplayName} · {item.ArtifactType} · {item.OwnerApp} · {item.LastModified:g}"));
    public string EventsSummary => EventsPage is null ? "No activity has been loaded." : EventsPage.Code == "Unavailable"
        ? EventsPage.Message : $"{EventsPage.Items.Count} activities{(EventsPage.Code == "Partial" ? " · some sources unavailable" : string.Empty)}";
    public string EventItemsSummary => EventItems.Count == 0 ? "No matching activity." : string.Join("\n", EventItems.Select(item =>
        $"{item.Timestamp:g} · {item.SourceApp} · {item.Summary}"));

    public static HomeCuiSurface LoadDefault()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "UI", "Home.cui");
        return new HomeCuiSurface(new CuiRichParser().ParseFile(path));
    }

    public void ApplySnapshot(HomeDashboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot;
        CanInstallAll = snapshot.CanInstallAll;
        OperationStatus = $"{snapshot.LastOperation.State}: {snapshot.LastOperation.Message}";
        Notify(nameof(OperationStatus));
        Notify(nameof(BriefSummary));
    }

    public bool RequestInstallAll()
    {
        if (!CanInstallAll)
            return false;

        _actions.Enqueue(HomeCuiAction.InstallAllUpdates);
        return true;
    }

    public bool RequestNavigate(HomeRoute route)
    {
        var action = route switch
        {
            HomeRoute.Dashboard => HomeCuiAction.NavigateDashboard,
            HomeRoute.Apps => HomeCuiAction.NavigateApps,
            HomeRoute.Library => HomeCuiAction.NavigateLibrary,
            HomeRoute.Events => HomeCuiAction.NavigateEvents,
            HomeRoute.Discover => HomeCuiAction.NavigateDiscover,
            HomeRoute.Mesh => HomeCuiAction.NavigateMesh,
            HomeRoute.Settings => HomeCuiAction.NavigateSettings,
            HomeRoute.Permissions => HomeCuiAction.NavigatePermissions,
            HomeRoute.Notifications => HomeCuiAction.NavigateNotifications,
            HomeRoute.Spaces => HomeCuiAction.NavigateSpaces,
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };
        _actions.Enqueue(action);
        return true;
    }

    public bool TryDequeueAction(out HomeCuiAction action) => _actions.TryDequeue(out action);

    public void ApplyNavigation(HomeNavigationState navigation)
    {
        Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        CurrentRoute = navigation.Current;
    }

    public void ApplyLayout(HomeDashboardLayout layout)
    {
        Layout = layout ?? throw new ArgumentNullException(nameof(layout));
        if (SelectedDashboardTileId is not null && !layout.Tiles.Any(x => x.TileInstanceId == SelectedDashboardTileId))
        { SelectedDashboardTileId = null; Notify(nameof(SelectedDashboardTileId)); }
        Notify(nameof(DashboardTilesSummary));
    }
    public bool SelectDashboardTile(string tileInstanceId)
    {
        if (Layout is null || !Layout.Tiles.Any(x => x.TileInstanceId == tileInstanceId)) return false;
        SelectedDashboardTileId = tileInstanceId;
        Notify(nameof(SelectedDashboardTileId));
        return true;
    }
    public void ApplyLibraryPage(HomeLibraryPage page)
    {
        LibraryPage = page ?? throw new ArgumentNullException(nameof(page));
        SelectedLibraryItem = null;
        Notify(nameof(LibraryItemsSummary));
    }
    public void ApplyEventsPage(HomeEventsPage page)
    {
        EventsPage = page ?? throw new ArgumentNullException(nameof(page));
        SelectedEventObject = null;
        Notify(nameof(EventItemsSummary));
    }
    public bool SelectLibraryItem(string ownerApp, string artifactId)
    {
        if (!LibraryItems.Any(item => item.OwnerApp == ownerApp && item.ArtifactId == artifactId)) return false;
        SelectedLibraryItem = new HomeLibrarySelection(ownerApp, artifactId);
        Notify(nameof(SelectedLibraryItem));
        return true;
    }
    public bool SelectEventObject(string eventId, int objectIndex)
    {
        var activity = EventItems.FirstOrDefault(item => item.EventId == eventId);
        if (activity is null || objectIndex < 0 || objectIndex >= activity.Objects.Count) return false;
        SelectedEventObject = new HomeEventSelection(eventId, objectIndex);
        Notify(nameof(SelectedEventObject));
        return true;
    }
    public void SetNavigationStatus(string status) => NavigationStatus = status ?? string.Empty;
    public void ApplyTileContent(string tileId, HomeTileContent content) { _tileContent[tileId] = content; Notify(nameof(BriefSummary)); }
    public bool TryGetValue(string path, out object? value)
    {
        var key = path[(path.LastIndexOf('.') + 1)..];
        value = key switch
        {
            nameof(IsDashboard) => IsDashboard, nameof(IsLibrary) => IsLibrary, nameof(IsEvents) => IsEvents,
            nameof(AllowAIGeneratedTiles) => AllowAIGeneratedTiles, nameof(AllowAIReorder) => AllowAIReorder,
            nameof(BriefSummary) => BriefSummary, nameof(CatalogSummary) => CatalogSummary,
            nameof(DashboardTilesSummary) => DashboardTilesSummary,
            nameof(RuntimeSummary) => RuntimeSummary, nameof(OperationStatus) => OperationStatus,
            nameof(NavigationStatus) => NavigationStatus, nameof(LibrarySummary) => LibrarySummary,
            nameof(LibraryItems) => LibraryItems, nameof(EventsSummary) => EventsSummary,
            nameof(EventItems) => EventItems, nameof(LibraryItemsSummary) => LibraryItemsSummary,
            nameof(EventItemsSummary) => EventItemsSummary, nameof(LibrarySearchText) => LibrarySearchText,
            nameof(LibrarySort) => LibrarySort, _ => null,
        };
        return value is not null;
    }
    public HomeLibraryQuery CreateLibraryQuery(int offset = 0, int limit = 50) =>
        new(Text: string.IsNullOrWhiteSpace(LibrarySearchText) ? null : LibrarySearchText, Sort: LibrarySort, Offset: offset, Limit: limit);
    private void Notify(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
public sealed record HomeLibrarySelection(string OwnerApp, string ArtifactId);
public sealed record HomeEventSelection(string EventId, int ObjectIndex);

/// <summary>Maps authored CUI intent to the Home domain service and projects observed state.</summary>
public sealed class HomeCuiController(HomeDashboard dashboard, HomeCuiSurface? surface = null, HomeNavigationState? navigation = null,
    HomeDashboardLayoutService? layoutService = null, HomeLibraryService? libraryService = null, HomeEventsService? eventsService = null,
    IHomeFeatureNavigationHost? featureNavigation = null)
{
    private readonly HomeDashboard _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
    public HomeNavigationState Navigation { get; } = navigation ?? new HomeNavigationState();

    public HomeCuiSurface Surface { get; } = surface ?? HomeCuiSurface.LoadDefault();

    public async Task<bool> OpenDeepLinkAsync(HomeDeepLink link, IHomeDeepLinkRouter router, CancellationToken ct = default)
    {
        var opened = await Navigation.OpenDeepLinkAsync(link, router, ct).ConfigureAwait(false);
        Surface.ApplyNavigation(Navigation);
        return opened;
    }

    public async Task<HomeFeatureNavigationResult> NavigateFeatureAsync(HomeRoute route, string? entityType = null,
        string? entityId = null, string? action = null, CancellationToken ct = default)
    {
        var request = new HomeFeatureNavigationRequest(HomeRouteIds.For(route), entityType, entityId, action);
        if (featureNavigation is null)
        {
            var unavailable = new HomeFeatureNavigationResult(false, "HomeServiceUnavailable", "Home feature navigation is not configured.", request);
            Surface.SetNavigationStatus($"{unavailable.Code}: {unavailable.Message}");
            return unavailable;
        }
        var result = await featureNavigation.NavigateAsync(request, ct).ConfigureAwait(false);
        Surface.SetNavigationStatus($"{result.Code}: {result.Message}");
        if (result.Succeeded)
        {
            Navigation.Navigate(route);
            if (entityId is not null) Navigation.State(route).SelectedObjectId = entityId;
            Surface.ApplyNavigation(Navigation);
        }
        return result;
    }

    public async Task<HomeFeatureNavigationResult> NavigateFeatureRouteAsync(string routeId, string? entityType = null,
        string? entityId = null, string? action = null, CancellationToken ct = default)
    {
        var request = new HomeFeatureNavigationRequest(routeId, entityType, entityId, action);
        if (featureNavigation is null)
        {
            var unavailable = new HomeFeatureNavigationResult(false, "HomeServiceUnavailable", "Home feature navigation is not configured.", request);
            Surface.SetNavigationStatus($"{unavailable.Code}: {unavailable.Message}");
            return unavailable;
        }
        var result = await featureNavigation.NavigateAsync(request, ct).ConfigureAwait(false);
        Surface.SetNavigationStatus($"{result.Code}: {result.Message}");
        return result;
    }

    public async Task<HomeLayoutResult> RefreshLayoutAsync(CancellationToken ct = default)
    {
        var layout = await Required(layoutService, "Dashboard layout").GetAsync(ct).ConfigureAwait(false);
        Surface.ApplyLayout(layout);
        return new(true, "Succeeded", "Dashboard layout loaded.", layout);
    }

    public async Task<HomeLayoutResult> AddTileAsync(long revision, string providerId, string tileInstanceId, CancellationToken ct = default) =>
        await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").AddAsync(revision, providerId, tileInstanceId, ct)).ConfigureAwait(false);
    public IReadOnlyList<HomeTileProviderDescriptor> ListTileProviders() => Required(layoutService, "Dashboard layout").ListProviders();

    public async Task<HomeLayoutResult> RemoveTileAsync(long revision, string tileInstanceId, CancellationToken ct = default) =>
        await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").RemoveAsync(revision, tileInstanceId, ct)).ConfigureAwait(false);

    public async Task<HomeLayoutResult> ResizeTileAsync(long revision, string tileInstanceId, HomeTileSize size, CancellationToken ct = default) =>
        await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").ResizeAsync(revision, tileInstanceId, size, ct)).ConfigureAwait(false);

    public async Task<HomeLayoutResult> SetTilePinnedAsync(long revision, string tileInstanceId, bool pinned, CancellationToken ct = default) =>
        await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").SetPinnedAsync(revision, tileInstanceId, pinned, ct)).ConfigureAwait(false);

    public async Task<HomeLayoutResult> SetTileVisibilityAsync(long revision, string tileInstanceId, HomeTileVisibility visibility, CancellationToken ct = default) =>
        await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").SetVisibilityAsync(revision, tileInstanceId, visibility, ct)).ConfigureAwait(false);

    public async Task<HomeLayoutResult> SetTileLockedAsync(long revision, string tileInstanceId, bool locked, CancellationToken ct = default) =>
        await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").SetLockedAsync(revision, tileInstanceId, locked, ct)).ConfigureAwait(false);

    public async Task<HomeLayoutResult> SetTileConfigurationAsync(long revision, string tileInstanceId, IReadOnlyDictionary<string, string> configuration, CancellationToken ct = default) =>
        await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").ConfigureTileAsync(revision, tileInstanceId, configuration, ct)).ConfigureAwait(false);

    public async Task<HomeLayoutResult> ReorderTilesAsync(long revision, IReadOnlyList<string> orderedIds, CancellationToken ct = default) =>
        await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").ReorderAsync(revision, orderedIds, ct)).ConfigureAwait(false);

    public async Task<HomeLayoutResult> MoveTileAsync(long revision, string tileInstanceId, int delta, CancellationToken ct = default)
    {
        if (delta is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(delta));
        var layout = Surface.Layout ?? (await RefreshLayoutAsync(ct).ConfigureAwait(false)).Layout;
        var ordered = layout.Tiles.OrderBy(x => x.Order).Select(x => x.TileInstanceId).ToList();
        var index = ordered.IndexOf(tileInstanceId);
        if (index < 0) return new(false, "TileNotFound", "The requested tile no longer exists.", layout);
        var target = index + delta;
        if (target < 0 || target >= ordered.Count) return new(false, "TileAtBoundary", "The tile is already at this end of the layout.", layout);
        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        return await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").ReorderAsync(revision, ordered, ct)).ConfigureAwait(false);
    }

    public async Task<HomeLayoutResult> SetAiLayoutControlsAsync(long revision, bool allowGeneratedTiles, bool allowReorder, CancellationToken ct = default) =>
        await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").SetAiControlsAsync(revision, allowGeneratedTiles, allowReorder, ct)).ConfigureAwait(false);

    public async Task<HomeLayoutResult> ResetLayoutAsync(long revision, CancellationToken ct = default) =>
        await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").ResetAsync(revision, ct)).ConfigureAwait(false);

    public async Task<HomeLayoutResult> UndoAiLayoutAsync(long revision, CancellationToken ct = default) =>
        await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").UndoLastAiChangeAsync(revision, ct)).ConfigureAwait(false);

    public async Task<HomeTileContent> RefreshTileAsync(string tileInstanceId, CancellationToken ct = default)
    {
        var content = await Required(layoutService, "Dashboard layout").RefreshTileAsync(tileInstanceId, ct).ConfigureAwait(false);
        Surface.ApplyTileContent(tileInstanceId, content);
        return content;
    }

    public async Task<HomeLibraryPage> SearchLibraryAsync(HomeLibraryQuery query, CancellationToken ct = default)
    {
        var page = await Required(libraryService, "Library").SearchAsync(query, ct).ConfigureAwait(false);
        Surface.ApplyLibraryPage(page);
        return page;
    }

    public async Task<HomeDomainResult<HomeDeepLink>> OpenLibraryItemAsync(string ownerApp, string artifactId, CancellationToken ct = default)
    {
        var result = await Required(libraryService, "Library").OpenAsync(ownerApp, artifactId, ct).ConfigureAwait(false);
        if (result.Succeeded && result.Value is not null) ApplyHomeDestinationLink(result.Value);
        return result;
    }

    public async Task<HomeDomainResult<bool>> SetLibraryItemPinnedAsync(string ownerApp, string artifactId, bool pinned, CancellationToken ct = default)
    {
        var result = await Required(libraryService, "Library").SetPinnedAsync(ownerApp, artifactId, pinned, ct).ConfigureAwait(false);
        return result;
    }

    public async Task<HomeEventsPage> RefreshEventsAsync(HomeEventsQuery query, CancellationToken ct = default)
    {
        var page = await Required(eventsService, "Events").ListAsync(query, ct).ConfigureAwait(false);
        Surface.ApplyEventsPage(page);
        return page;
    }

    public async Task<HomeDomainResult<HomeDeepLink>> OpenEventObjectAsync(string eventId, int objectIndex, IHomeDeepLinkRouter router, CancellationToken ct = default)
    {
        var result = await Required(eventsService, "Events").OpenObjectAsync(eventId, objectIndex, router, ct).ConfigureAwait(false);
        if (result.Succeeded && result.Value is not null) ApplyHomeDestinationLink(result.Value);
        return result;
    }

    private void ApplyHomeDestinationLink(HomeDeepLink link)
    {
        if (link.TargetRouteId is null || link.TargetRouteId == HomeRouteIds.For(link.Destination))
        {
            Navigation.Navigate(link.Destination);
            Navigation.State(link.Destination).SelectedObjectId = link.ObjectId;
            Surface.ApplyNavigation(Navigation);
        }
    }

    private async Task<HomeLayoutResult> ApplyLayoutResultAsync(Task<HomeLayoutResult> operation)
    {
        var result = await operation.ConfigureAwait(false);
        Surface.ApplyLayout(result.Layout);
        return result;
    }

    private static T Required<T>(T? service, string name) where T : class =>
        service ?? throw new InvalidOperationException($"The Home {name} service is not configured.");

    public HomeDashboardSnapshot ShowCurrent()
    {
        var snapshot = _dashboard.Current;
        Surface.ApplySnapshot(snapshot);
        Surface.ApplyNavigation(Navigation);
        return snapshot;
    }

    public async Task<HomeDashboardSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _dashboard.RefreshAsync(cancellationToken).ConfigureAwait(false);
        Surface.ApplySnapshot(snapshot);
        Surface.ApplyNavigation(Navigation);
        return snapshot;
    }

    public async Task<HomeDashboardSnapshot> ExecuteAsync(
        HomeCuiAction action,
        CancellationToken cancellationToken = default)
    {
        var route = action switch
        {
            HomeCuiAction.NavigateDashboard => HomeRoute.Dashboard,
            HomeCuiAction.NavigateApps => HomeRoute.Apps,
            HomeCuiAction.NavigateLibrary => HomeRoute.Library,
            HomeCuiAction.NavigateEvents => HomeRoute.Events,
            HomeCuiAction.NavigateDiscover => HomeRoute.Discover,
            HomeCuiAction.NavigateMesh => HomeRoute.Mesh,
            HomeCuiAction.NavigateSettings => HomeRoute.Settings,
            HomeCuiAction.NavigatePermissions => HomeRoute.Permissions,
            HomeCuiAction.NavigateNotifications => HomeRoute.Notifications,
            HomeCuiAction.NavigateSpaces => HomeRoute.Spaces,
            _ => (HomeRoute?)null,
        };
        if (route.HasValue)
        {
            await NavigateFeatureAsync(route.Value, ct: cancellationToken).ConfigureAwait(false);
            Surface.ApplySnapshot(_dashboard.Current);
            return _dashboard.Current;
        }

        var snapshot = action switch
        {
            HomeCuiAction.InstallAllUpdates => await _dashboard.InstallAllAsync(cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        Surface.ApplySnapshot(snapshot);
        Surface.ApplyNavigation(Navigation);
        return snapshot;
    }
}

/// <summary>Allowlisted bridge from authored CUI actions to Home domain operations and registered feature routes.</summary>
public sealed class HomeCuiActionDispatcher(HomeCuiController controller) : ICuiActionDispatcher
{
    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        try
        {
            var route = command switch
            {
                "NavigateDashboard" or "NavigateHome" => HomeRoute.Dashboard,
                "NavigateApps" => HomeRoute.Apps,
                "NavigateLibrary" => HomeRoute.Library,
                "NavigateEvents" => HomeRoute.Events,
                "NavigateDiscover" => HomeRoute.Discover,
                "NavigateMesh" => HomeRoute.Mesh,
                "NavigateSettings" => HomeRoute.Settings,
                "NavigatePermissions" => HomeRoute.Permissions,
                "NavigateNotifications" => HomeRoute.Notifications,
                "NavigateSpaces" => HomeRoute.Spaces,
                _ => (HomeRoute?)null,
            };
            if (route.HasValue)
            {
                var result = await controller.NavigateFeatureAsync(route.Value, ct: cancellationToken).ConfigureAwait(false);
                controller.Surface.SetNavigationStatus($"{result.Code}: {result.Message}");
                return;
            }
            var appRoute = command switch
            {
                "OpenStudio" => "app.studio", "OpenWrite" => "app.write", "OpenBrowse" => "app.browse",
                "OpenData" => "app.data", "OpenBoards" => "app.boards", "NavigateAutomations" => HomeFeatureRouteIds.Automations,
                _ => null,
            };
            if (appRoute is not null)
            {
                var result = await controller.NavigateFeatureRouteAsync(appRoute, ct: cancellationToken).ConfigureAwait(false);
                controller.Surface.SetNavigationStatus($"{result.Code}: {result.Message}");
                return;
            }
            switch (command)
            {
                case "InstallAllUpdates":
                    await controller.ExecuteAsync(HomeCuiAction.InstallAllUpdates, cancellationToken).ConfigureAwait(false);
                    return;
                case "EnableAIGeneratedTiles":
                case "DisableAIGeneratedTiles":
                case "EnableAIReorder":
                case "DisableAIReorder":
                    var layout = controller.Surface.Layout ?? (await controller.RefreshLayoutAsync(cancellationToken).ConfigureAwait(false)).Layout;
                    var allowGenerated = command.EndsWith("AIGeneratedTiles", StringComparison.Ordinal)
                        ? command.StartsWith("Enable", StringComparison.Ordinal) : layout.AllowAiGeneratedTiles;
                    var allowReorder = command.EndsWith("AIReorder", StringComparison.Ordinal)
                        ? command.StartsWith("Enable", StringComparison.Ordinal) : layout.AllowAiReorder;
                    var controls = await controller.SetAiLayoutControlsAsync(layout.Revision, allowGenerated, allowReorder, cancellationToken).ConfigureAwait(false);
                    controller.Surface.SetNavigationStatus($"{controls.Code}: {controls.Message}");
                    return;
                case "AddDashboardTile":
                    layout = controller.Surface.Layout ?? (await controller.RefreshLayoutAsync(cancellationToken).ConfigureAwait(false)).Layout;
                    var provider = controller.ListTileProviders().FirstOrDefault(item => item.CanInstantiateManually);
                    if (provider is null)
                    {
                        controller.Surface.SetNavigationStatus("ProviderUnavailable: No dashboard tile providers are available.");
                        return;
                    }
                    var added = await controller.AddTileAsync(layout.Revision, provider.ProviderId, "tile:" + Guid.NewGuid().ToString("N"), cancellationToken).ConfigureAwait(false);
                    if (added.Succeeded && added.Layout.Tiles.Count > 0) controller.Surface.SelectDashboardTile(added.Layout.Tiles[^1].TileInstanceId);
                    controller.Surface.SetNavigationStatus($"{added.Code}: {added.Message}");
                    return;
                case "RemoveSelectedDashboardTile":
                case "PinSelectedDashboardTile":
                case "UnpinSelectedDashboardTile":
                case "LockSelectedDashboardTile":
                case "UnlockSelectedDashboardTile":
                case "HideSelectedDashboardTile":
                case "ShowSelectedDashboardTile":
                case "MoveSelectedDashboardTileUp":
                case "MoveSelectedDashboardTileDown":
                case "RefreshSelectedDashboardTile":
                    layout = controller.Surface.Layout ?? (await controller.RefreshLayoutAsync(cancellationToken).ConfigureAwait(false)).Layout;
                    if (controller.Surface.SelectedDashboardTileId is not { } tileId)
                    {
                        controller.Surface.SetNavigationStatus("TileNotSelected: Select a dashboard tile first.");
                        return;
                    }
                    if (command == "RefreshSelectedDashboardTile")
                    {
                        var content = await controller.RefreshTileAsync(tileId, cancellationToken).ConfigureAwait(false);
                        controller.Surface.SetNavigationStatus($"{content.State}: {content.Summary}");
                        return;
                    }
                    var tileChange = command switch
                    {
                        "RemoveSelectedDashboardTile" => await controller.RemoveTileAsync(layout.Revision, tileId, cancellationToken).ConfigureAwait(false),
                        "PinSelectedDashboardTile" => await controller.SetTilePinnedAsync(layout.Revision, tileId, true, cancellationToken).ConfigureAwait(false),
                        "UnpinSelectedDashboardTile" => await controller.SetTilePinnedAsync(layout.Revision, tileId, false, cancellationToken).ConfigureAwait(false),
                        "LockSelectedDashboardTile" => await controller.SetTileLockedAsync(layout.Revision, tileId, true, cancellationToken).ConfigureAwait(false),
                        "UnlockSelectedDashboardTile" => await controller.SetTileLockedAsync(layout.Revision, tileId, false, cancellationToken).ConfigureAwait(false),
                        "HideSelectedDashboardTile" => await controller.SetTileVisibilityAsync(layout.Revision, tileId, HomeTileVisibility.Hidden, cancellationToken).ConfigureAwait(false),
                        "ShowSelectedDashboardTile" => await controller.SetTileVisibilityAsync(layout.Revision, tileId, HomeTileVisibility.Visible, cancellationToken).ConfigureAwait(false),
                        "MoveSelectedDashboardTileUp" => await controller.MoveTileAsync(layout.Revision, tileId, -1, cancellationToken).ConfigureAwait(false),
                        "MoveSelectedDashboardTileDown" => await controller.MoveTileAsync(layout.Revision, tileId, 1, cancellationToken).ConfigureAwait(false),
                        _ => throw new InvalidOperationException("Unsupported tile action."),
                    };
                    controller.Surface.SetNavigationStatus($"{tileChange.Code}: {tileChange.Message}");
                    return;
                case "ResizeSelectedDashboardTileSmall":
                case "ResizeSelectedDashboardTileMedium":
                case "ResizeSelectedDashboardTileLarge":
                case "ResizeSelectedDashboardTileWide":
                    layout = controller.Surface.Layout ?? (await controller.RefreshLayoutAsync(cancellationToken).ConfigureAwait(false)).Layout;
                    if (controller.Surface.SelectedDashboardTileId is not { } resizeId)
                    {
                        controller.Surface.SetNavigationStatus("TileNotSelected: Select a dashboard tile first.");
                        return;
                    }
                    var size = Enum.Parse<HomeTileSize>(command["ResizeSelectedDashboardTile".Length..]);
                    var resized = await controller.ResizeTileAsync(layout.Revision, resizeId, size, cancellationToken).ConfigureAwait(false);
                    controller.Surface.SetNavigationStatus($"{resized.Code}: {resized.Message}");
                    return;
                case "ResetDashboardLayout":
                case "UndoAiDashboardLayout":
                    layout = controller.Surface.Layout ?? (await controller.RefreshLayoutAsync(cancellationToken).ConfigureAwait(false)).Layout;
                    var changed = command == "ResetDashboardLayout"
                        ? await controller.ResetLayoutAsync(layout.Revision, cancellationToken).ConfigureAwait(false)
                        : await controller.UndoAiLayoutAsync(layout.Revision, cancellationToken).ConfigureAwait(false);
                    controller.Surface.SetNavigationStatus($"{changed.Code}: {changed.Message}");
                    return;
                case "SearchLibrary":
                    var library = await controller.SearchLibraryAsync(controller.Surface.CreateLibraryQuery(), cancellationToken).ConfigureAwait(false);
                    controller.Surface.SetNavigationStatus($"{library.Code}: {library.Message}");
                    return;
                case "OpenLibraryItem" when (parameter as HomeLibrarySelection ?? controller.Surface.SelectedLibraryItem) is { } selectedArtifact:
                    var open = await controller.OpenLibraryItemAsync(selectedArtifact.OwnerApp, selectedArtifact.ArtifactId, cancellationToken).ConfigureAwait(false);
                    controller.Surface.SetNavigationStatus($"{open.Code}: {open.Message}");
                    return;
                case "PinSelectedLibraryItem" or "UnpinSelectedLibraryItem" when controller.Surface.SelectedLibraryItem is { } pinnedItem:
                    var pin = await controller.SetLibraryItemPinnedAsync(pinnedItem.OwnerApp, pinnedItem.ArtifactId,
                        command == "PinSelectedLibraryItem", cancellationToken).ConfigureAwait(false);
                    controller.Surface.SetNavigationStatus($"{pin.Code}: {pin.Message}");
                    return;
                case "RefreshEvents":
                    var events = await controller.RefreshEventsAsync(new HomeEventsQuery(), cancellationToken).ConfigureAwait(false);
                    controller.Surface.SetNavigationStatus($"{events.Code}: {events.Message}");
                    return;
                case "OpenEventObject" when (parameter as HomeEventSelection ?? controller.Surface.SelectedEventObject) is { } selectedEvent:
                    var eventResult = await controller.OpenEventObjectAsync(selectedEvent.EventId, selectedEvent.ObjectIndex,
                        new HomeFeatureDeepLinkRouterForController(controller), cancellationToken).ConfigureAwait(false);
                    controller.Surface.SetNavigationStatus($"{eventResult.Code}: {eventResult.Message}");
                    return;
                default:
                    controller.Surface.SetNavigationStatus($"UnsupportedAction: Home does not expose '{command}'.");
                    return;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            controller.Surface.SetNavigationStatus($"HomeActionFailed: {exception.Message}");
        }
    }

    private sealed class HomeFeatureDeepLinkRouterForController(HomeCuiController current) : IHomeDeepLinkRouter
    {
        public Task<HomeFeatureNavigationResult> OpenAsync(HomeDeepLink link, CancellationToken cancellationToken = default) =>
            current.NavigateFeatureRouteAsync(link.TargetRouteId ?? HomeRouteIds.For(link.Destination),
                link.OwnerApp, link.ObjectId, link.Action, cancellationToken);
    }
}
