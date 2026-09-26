using CakeOS.Cui;
using CakeOS.Cui.Language;

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
public sealed class HomeCuiSurface
{
    private readonly Queue<HomeCuiAction> _actions = new();

    public HomeCuiSurface(CuiDocument document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public CuiDocument Document { get; }
    public HomeDashboardSnapshot? Snapshot { get; private set; }
    public bool CanInstallAll { get; private set; }
    public string OperationStatus { get; private set; } = string.Empty;
    public HomeRoute CurrentRoute { get; private set; } = HomeRoute.Dashboard;
    public HomeNavigationState? Navigation { get; private set; }
    public HomeDashboardLayout? Layout { get; private set; }
    public HomeLibraryPage? LibraryPage { get; private set; }
    public HomeEventsPage? EventsPage { get; private set; }

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

    public void ApplyLayout(HomeDashboardLayout layout) => Layout = layout ?? throw new ArgumentNullException(nameof(layout));
    public void ApplyLibraryPage(HomeLibraryPage page) => LibraryPage = page ?? throw new ArgumentNullException(nameof(page));
    public void ApplyEventsPage(HomeEventsPage page) => EventsPage = page ?? throw new ArgumentNullException(nameof(page));
}

/// <summary>Maps authored CUI intent to the Home domain service and projects observed state.</summary>
public sealed class HomeCuiController(HomeDashboard dashboard, HomeCuiSurface? surface = null, HomeNavigationState? navigation = null,
    HomeDashboardLayoutService? layoutService = null, HomeLibraryService? libraryService = null, HomeEventsService? eventsService = null)
{
    private readonly HomeDashboard _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
    public HomeNavigationState Navigation { get; } = navigation ?? new HomeNavigationState();

    public HomeCuiSurface Surface { get; } = surface ?? HomeCuiSurface.LoadDefault();

    public void Navigate(HomeRoute route)
    {
        Navigation.Navigate(route);
        Surface.ApplyNavigation(Navigation);
    }

    public bool OpenDeepLink(HomeDeepLink link, IHomeDeepLinkRouter router)
    {
        var opened = Navigation.OpenDeepLink(link, router);
        Surface.ApplyNavigation(Navigation);
        return opened;
    }

    public async Task<HomeLayoutResult> RefreshLayoutAsync(CancellationToken ct = default)
    {
        var layout = await Required(layoutService, "Dashboard layout").GetAsync(ct).ConfigureAwait(false);
        Surface.ApplyLayout(layout);
        return new(true, "Succeeded", "Dashboard layout loaded.", layout);
    }

    public async Task<HomeLayoutResult> AddTileAsync(long revision, string providerId, string tileInstanceId, CancellationToken ct = default) =>
        await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").AddAsync(revision, providerId, tileInstanceId, ct)).ConfigureAwait(false);

    public async Task<HomeLayoutResult> RemoveTileAsync(long revision, string tileInstanceId, CancellationToken ct = default) =>
        await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").RemoveAsync(revision, tileInstanceId, ct)).ConfigureAwait(false);

    public async Task<HomeLayoutResult> ResizeTileAsync(long revision, string tileInstanceId, HomeTileSize size, CancellationToken ct = default) =>
        await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").ResizeAsync(revision, tileInstanceId, size, ct)).ConfigureAwait(false);

    public async Task<HomeLayoutResult> SetTilePinnedAsync(long revision, string tileInstanceId, bool pinned, CancellationToken ct = default) =>
        await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").SetPinnedAsync(revision, tileInstanceId, pinned, ct)).ConfigureAwait(false);

    public async Task<HomeLayoutResult> SetTileLockedAsync(long revision, string tileInstanceId, bool locked, CancellationToken ct = default) =>
        await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").SetLockedAsync(revision, tileInstanceId, locked, ct)).ConfigureAwait(false);

    public async Task<HomeLayoutResult> SetTileConfigurationAsync(long revision, string tileInstanceId, IReadOnlyDictionary<string, string> configuration, CancellationToken ct = default) =>
        await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").ConfigureTileAsync(revision, tileInstanceId, configuration, ct)).ConfigureAwait(false);

    public async Task<HomeLayoutResult> ReorderTilesAsync(long revision, IReadOnlyList<string> orderedIds, CancellationToken ct = default) =>
        await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").ReorderAsync(revision, orderedIds, ct)).ConfigureAwait(false);

    public async Task<HomeLayoutResult> SetAiLayoutControlsAsync(long revision, bool allowGeneratedTiles, bool allowReorder, CancellationToken ct = default) =>
        await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").SetAiControlsAsync(revision, allowGeneratedTiles, allowReorder, ct)).ConfigureAwait(false);

    public async Task<HomeLayoutResult> ResetLayoutAsync(long revision, CancellationToken ct = default) =>
        await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").ResetAsync(revision, ct)).ConfigureAwait(false);

    public async Task<HomeLayoutResult> UndoAiLayoutAsync(long revision, CancellationToken ct = default) =>
        await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").UndoLastAiChangeAsync(revision, ct)).ConfigureAwait(false);

    public Task<HomeTileContent> RefreshTileAsync(string tileInstanceId, CancellationToken ct = default) =>
        Required(layoutService, "Dashboard layout").RefreshTileAsync(tileInstanceId, ct);

    public async Task<HomeLayoutResult> ReorderTilesAsync(long revision, IReadOnlyList<string> orderedIds, CancellationToken ct = default) =>
        await ApplyLayoutResultAsync(Required(layoutService, "Dashboard layout").ReorderAsync(revision, orderedIds, ct)).ConfigureAwait(false);

    public async Task<HomeLibraryPage> SearchLibraryAsync(HomeLibraryQuery query, CancellationToken ct = default)
    {
        var page = await Required(libraryService, "Library").SearchAsync(query, ct).ConfigureAwait(false);
        Surface.ApplyLibraryPage(page);
        return page;
    }

    public async Task<HomeDomainResult<HomeDeepLink>> OpenLibraryItemAsync(string ownerApp, string artifactId, CancellationToken ct = default)
    {
        var result = await Required(libraryService, "Library").OpenAsync(ownerApp, artifactId, ct).ConfigureAwait(false);
        if (result.Succeeded && result.Value is not null)
        {
            Navigation.Navigate(result.Value.Destination);
            Navigation.State(result.Value.Destination).SelectedObjectId = result.Value.ObjectId;
            Surface.ApplyNavigation(Navigation);
        }
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

    public Task<HomeDomainResult<HomeDeepLink>> OpenEventObjectAsync(string eventId, int objectIndex, IHomeDeepLinkRouter router, CancellationToken ct = default) =>
        Required(eventsService, "Events").OpenObjectAsync(eventId, objectIndex, router, ct);

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
            Navigate(route.Value);
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
