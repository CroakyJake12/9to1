using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Home;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using HomePermissionRequest = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest;

namespace AvaloniaHome;

internal sealed partial class HomeApp
{
    private ListBox _workspaceTiles = new();
    private ListBox _workspaceRequests = new();
    private AuthenticatedResourceActor? _workspaceActor;
    private Window? _workspaceWindow;
    private bool _workspaceBusy;
    private bool _workspaceInitialized;
    private IReadOnlyList<HomePermissionRequest> _displayedRequests = [];
    private readonly List<Button> _workspaceReviewButtons = [];

    private static readonly string[] LayoutActions =
    [
        "EnableAIGeneratedTiles", "DisableAIGeneratedTiles", "EnableAIReorder", "DisableAIReorder",
        "UndoAiDashboardLayout", "ResetDashboardLayout", "AddDashboardTile", "RemoveSelectedDashboardTile",
        "MoveSelectedDashboardTileUp", "MoveSelectedDashboardTileDown", "PinSelectedDashboardTile",
        "UnpinSelectedDashboardTile", "LockSelectedDashboardTile", "UnlockSelectedDashboardTile",
        "HideSelectedDashboardTile", "ShowSelectedDashboardTile", "RefreshSelectedDashboardTile",
        "ResizeSelectedDashboardTileSmall", "ResizeSelectedDashboardTileMedium",
        "ResizeSelectedDashboardTileLarge", "ResizeSelectedDashboardTileWide"
    ];

    private async Task InitializeWorkspaceAsync()
    {
        var actor = await _nativeComposition.Profiles.GetCurrentAsync(CancellationToken.None).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The current operating-system Home profile is unavailable.");
        var store = new HomeOwnedDashboardStateStore(_coreStateStore, _nativeComposition.Profiles, actor);
        var layouts = new HomeDashboardLayoutService(new HomeCoreDashboardLayoutStore(store),
            new CoreTileRegistry(_homeCore));
        var activity = new HomePermissionActivitySource(_nativeComposition.Permissions, _nativeComposition.Profiles, actor);
        _controller = new HomeCuiController(_dashboard, layoutService: layouts,
            eventsService: new HomeEventsService([activity], activity), featureNavigation: _featureNavigation);
        _workspaceActor = actor;
        foreach (var route in new[] { HomeRoute.Dashboard, HomeRoute.Events, HomeRoute.Settings, HomeRoute.Permissions })
        {
            var registered = _featureNavigation.Register(new OwnedRoute(this, HomeRouteIds.For(route)));
            if (!registered.Succeeded) throw new InvalidOperationException(registered.Message);
        }
        await _controller.RefreshLayoutAsync().ConfigureAwait(false);
        foreach (var tile in _controller.Surface.Layout!.Tiles.Where(tile => tile.Visibility == HomeTileVisibility.Visible))
            await _controller.RefreshTileAsync(tile.TileInstanceId).ConfigureAwait(false);
        _workspaceInitialized = true;
    }

    private async Task RequireWorkspaceActorAsync()
    {
        if (_workspaceActor is null || await _nativeComposition.Profiles.GetCurrentAsync(CancellationToken.None)
                .ConfigureAwait(false) != _workspaceActor)
            throw new UnauthorizedAccessException("The original Home profile changed. Reopen Home.");
    }

    private void RegisterWorkspaceActions()
    {
        foreach (var command in new[] { "NavigateDashboard", "NavigateHome", "NavigateEvents", "NavigateSettings", "NavigatePermissions" })
            RegisterWorkspaceAction(command, () => DispatchWorkspaceAsync(command));
        foreach (var command in LayoutActions)
            RegisterWorkspaceAction(command, () => DispatchWorkspaceAsync(command));
        RegisterWorkspaceAction("RefreshHomePermissions", RefreshWorkspacePermissionsAsync);
        RegisterWorkspaceAction("RefreshHomeWorkspace", RefreshWorkspaceAsync);
        RegisterWorkspaceAction("RefreshEvents", RefreshWorkspaceEventsAsync);
        RegisterWorkspaceAction("OpenEventObject", OpenWorkspaceEventAsync);
        _viewModel.On("SelectEventObject", parameter => _ = RunOriginalNativeWork(() => RunWorkspaceActionAsync(() =>
        {
            if (parameter is string eventId && _controller.Surface.SelectEventObject(eventId, 0))
                _controller.Surface.SetNavigationStatus("Selected activity: " + _controller.Surface.EventItems.Single(item => item.EventId == eventId).Summary);
            else
            {
                if (_controller.Surface.EventsPage is { } current) _controller.Surface.ApplyEventsPage(current);
                _controller.Surface.SetNavigationStatus("This activity has no available linked object.");
            }
            return Task.CompletedTask;
        })));
        // Approval is native owning-window intent, not a Home CUI/AI action capability.
        _viewModel.SetActionAvailability("AcceptHomePermission", false);
        _viewModel.SetActionAvailability("DeclineHomePermission", false);
    }

    private void RegisterWorkspaceAction(string command, Func<Task> callback)
    {
        _viewModel.On(command, _ => _ = RunOriginalNativeWork(() => RunWorkspaceActionAsync(callback)));
        _viewModel.SetActionAvailability(command, _workspaceInitialized);
    }

    private async Task RunWorkspaceActionAsync(Func<Task> callback)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_workspaceBusy)
        {
            _controller.Surface.SetNavigationStatus("A Home operation is still running. Wait for its result.");
            ApplyWorkspaceBindings();
            _loader?.RefreshBindings();
            return;
        }
        _workspaceBusy = true;
        var failures = new List<Exception>();
        try
        {
            ApplyWorkspaceBindings();
            await RequireWorkspaceActorAsync();
            await callback();
            await RequireWorkspaceActorAsync();
        }
        catch (Exception cause)
        {
            failures.Add(cause);
            try { _controller.Surface.SetNavigationStatus($"Home operation failed: {cause.Message}"); }
            catch (Exception projection) { failures.Add(projection); }
        }
        finally
        {
            _workspaceBusy = false;
            try { ApplyWorkspaceBindings(); } catch (Exception projection) { failures.Add(projection); }
            try { _loader?.RefreshBindings(); } catch (Exception projection) { failures.Add(projection); }
        }
        if (failures.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1)
            throw new AggregateException("Original Home operation and presentation failures.", failures);
    }

    private async Task DispatchWorkspaceAsync(string command)
    {
        // This owning callback preserves source failures; the generic status-only dispatcher
        // catches them, so it is not used for our persisted Home layout operations.
        var route = command switch
        {
            "NavigateDashboard" or "NavigateHome" => HomeRoute.Dashboard,
            "NavigateSettings" => HomeRoute.Settings,
            "NavigateEvents" => HomeRoute.Events,
            "NavigatePermissions" => HomeRoute.Permissions,
            _ => (HomeRoute?)null
        };
        if (route.HasValue)
        {
            var navigation = await _controller.NavigateFeatureAsync(route.Value);
            _controller.Surface.SetNavigationStatus($"{navigation.Code}: {navigation.Message}");
            if (_controller.Navigation.Current == HomeRoute.Permissions) await RefreshWorkspacePermissionsAsync();
            if (_controller.Navigation.Current == HomeRoute.Events) await RefreshWorkspaceEventsAsync();
            return;
        }
        var layout = _controller.Surface.Layout ?? (await _controller.RefreshLayoutAsync()).Layout;
        HomeLayoutResult changed;
        if (command is "EnableAIGeneratedTiles" or "DisableAIGeneratedTiles" or "EnableAIReorder" or "DisableAIReorder")
        {
            var generated = command.EndsWith("AIGeneratedTiles", StringComparison.Ordinal)
                ? command.StartsWith("Enable", StringComparison.Ordinal) : layout.AllowAiGeneratedTiles;
            var reorder = command.EndsWith("AIReorder", StringComparison.Ordinal)
                ? command.StartsWith("Enable", StringComparison.Ordinal) : layout.AllowAiReorder;
            changed = await _controller.SetAiLayoutControlsAsync(layout.Revision, generated, reorder);
        }
        else if (command == "AddDashboardTile")
        {
            var provider = _controller.ListTileProviders().FirstOrDefault(item => item.CanInstantiateManually)
                ?? throw new InvalidOperationException("No original Home tile provider is available.");
            changed = await _controller.AddTileAsync(layout.Revision, provider.ProviderId, "tile:" + Guid.NewGuid().ToString("N"));
            if (changed.Succeeded && changed.Layout.Tiles.Count > 0)
                _controller.Surface.SelectDashboardTile(changed.Layout.Tiles[^1].TileInstanceId);
        }
        else if (command is "ResetDashboardLayout" or "UndoAiDashboardLayout")
            changed = command == "ResetDashboardLayout"
                ? await _controller.ResetLayoutAsync(layout.Revision) : await _controller.UndoAiLayoutAsync(layout.Revision);
        else
        {
            if (_controller.Surface.SelectedDashboardTileId is not { } tile)
            {
                _controller.Surface.SetNavigationStatus("TileNotSelected: Select a dashboard tile first.");
                return;
            }
            if (command == "RefreshSelectedDashboardTile")
            {
                var content = await _controller.RefreshTileAsync(tile);
                _controller.Surface.SetNavigationStatus($"{content.State}: {content.Summary}");
                return;
            }
            changed = command switch
            {
                "RemoveSelectedDashboardTile" => await _controller.RemoveTileAsync(layout.Revision, tile),
                "PinSelectedDashboardTile" => await _controller.SetTilePinnedAsync(layout.Revision, tile, true),
                "UnpinSelectedDashboardTile" => await _controller.SetTilePinnedAsync(layout.Revision, tile, false),
                "LockSelectedDashboardTile" => await _controller.SetTileLockedAsync(layout.Revision, tile, true),
                "UnlockSelectedDashboardTile" => await _controller.SetTileLockedAsync(layout.Revision, tile, false),
                "HideSelectedDashboardTile" => await _controller.SetTileVisibilityAsync(layout.Revision, tile, HomeTileVisibility.Hidden),
                "ShowSelectedDashboardTile" => await _controller.SetTileVisibilityAsync(layout.Revision, tile, HomeTileVisibility.Visible),
                "MoveSelectedDashboardTileUp" => await _controller.MoveTileAsync(layout.Revision, tile, -1),
                "MoveSelectedDashboardTileDown" => await _controller.MoveTileAsync(layout.Revision, tile, 1),
                "ResizeSelectedDashboardTileSmall" => await _controller.ResizeTileAsync(layout.Revision, tile, HomeTileSize.Small),
                "ResizeSelectedDashboardTileMedium" => await _controller.ResizeTileAsync(layout.Revision, tile, HomeTileSize.Medium),
                "ResizeSelectedDashboardTileLarge" => await _controller.ResizeTileAsync(layout.Revision, tile, HomeTileSize.Large),
                "ResizeSelectedDashboardTileWide" => await _controller.ResizeTileAsync(layout.Revision, tile, HomeTileSize.Wide),
                _ => throw new InvalidOperationException("The owning Home layout action is unknown.")
            };
        }
        _controller.Surface.SetNavigationStatus($"{changed.Code}: {changed.Message}");
        if (_controller.Surface.SelectedDashboardTileId is { } selected &&
            changed.Layout.Tiles.Any(tile => tile.TileInstanceId == selected))
            await _controller.RefreshTileAsync(selected);
    }

    private async Task RefreshWorkspaceAsync()
    {
        await _controller.RefreshLayoutAsync();
        foreach (var tile in _controller.Surface.Layout!.Tiles.Where(tile => tile.Visibility == HomeTileVisibility.Visible))
            await _controller.RefreshTileAsync(tile.TileInstanceId);
        await RefreshWorkspaceEventsAsync();
        if (_controller.Navigation.Current == HomeRoute.Permissions) await RefreshWorkspacePermissionsAsync();
    }

    private async Task RefreshWorkspaceEventsAsync()
    {
        var selected = _controller.Surface.SelectedEventObject;
        await _controller.RefreshEventsAsync(new HomeEventsQuery(Limit: 200));
        if (selected is not null) _controller.Surface.SelectEventObject(selected.EventId, selected.ObjectIndex);
    }

    private async Task OpenWorkspaceEventAsync()
    {
        if (_controller.Surface.SelectedEventObject is not { } selected)
        {
            _controller.Surface.SetNavigationStatus("Select an activity item first.");
            return;
        }
        var opened = await _controller.OpenEventObjectAsync(selected.EventId, selected.ObjectIndex,
            new HomeFeatureDeepLinkRouter(_featureNavigation));
        _controller.Surface.SetNavigationStatus($"{opened.Code}: {opened.Message}");
        if (opened.Succeeded && _controller.Navigation.Current == HomeRoute.Permissions)
            await RefreshWorkspacePermissionsAsync();
    }

    private async Task RefreshWorkspacePermissionsAsync()
    {
        await RequireWorkspaceActorAsync();
        var snapshot = await _nativeComposition.Permissions.GetSnapshotAsync(cancellationToken: CancellationToken.None);
        await RequireWorkspaceActorAsync();
        // Only requests already bound by the real resource broker to this original local actor.
        // An account claim, public caller ID or another profile never lends approval authority.
        _displayedRequests = snapshot.PendingRequests.Where(request =>
            request.Impact.ResourceBinding?.OriginalActor == _workspaceActor).ToArray();
        _workspaceRequests.ItemsSource = _displayedRequests;
        var selectedId = _controller.Navigation.State(HomeRoute.Permissions).SelectedObjectId;
        if (selectedId is not null)
            _workspaceRequests.SelectedItem = _displayedRequests.FirstOrDefault(request => request.RequestId == selectedId);
        _viewModel.Set("HomePermissionSummary", $"{_displayedRequests.Count} requests for this device profile. " +
            "Select a request to review its exact action and affected objects.");
        UpdateSelectedPermissionDetails();
        if (selectedId is not null && _workspaceRequests.SelectedItem is null)
        {
            var historical = await _nativeComposition.Permissions.ReadRequestObservationAsync(selectedId);
            await RequireWorkspaceActorAsync();
            if (historical is not null && _workspaceActor is not null &&
                historical.Impact.ResourceBinding?.OriginalActor == _workspaceActor)
                _viewModel.Set("SelectedHomePermission", $"{historical.Caller.DisplayName}\n" +
                    $"{historical.Scope.TargetAppId}: {historical.Scope.ActionName}\n{historical.State}: {historical.ResultMessage}\n" +
                    "This activity is historical. It does not grant permission to run the action again.");
        }
    }

    private void RegisterWorkspaceControls(CuiControlLoader loader, Window window)
    {
        _workspaceWindow = window;
        _workspaceReviewButtons.Clear();
        Func<CakeOS.Cui.CuiComponent, Control> dashboardTiles = component =>
        {
            var list = new ListBox();
            list.ItemTemplate = new FuncDataTemplate<HomeTileInstance>((tile, _) => new TextBlock
            { Text = tile is null ? "" : $"{tile.TileType} · {tile.Size} · {tile.Visibility}" });
            list.SelectionChanged += (_, _) =>
            {
                if (ReferenceEquals(_workspaceWindow, window) && list.SelectedItem is HomeTileInstance selected &&
                    _controller.Surface.Layout?.Tiles.Any(tile => ReferenceEquals(tile, selected)) == true)
                    _controller.Surface.SelectDashboardTile(selected.TileInstanceId);
            };
            _workspaceTiles = list;
            ApplyWorkspaceBindings();
            return list;
        };
        Func<CakeOS.Cui.CuiComponent, Control> permissionRequests = component =>
        {
            var list = new ListBox();
            list.ItemTemplate = new FuncDataTemplate<HomePermissionRequest>((request, _) => new TextBlock
            { Text = request is null ? "" : $"{request.Caller.DisplayName} · {request.Scope.ActionName}" });
            list.SelectionChanged += (_, _) =>
            { if (ReferenceEquals(_workspaceWindow, window)) UpdateSelectedPermissionDetails(); };
            _workspaceRequests = list;
            list.ItemsSource = _displayedRequests;
            return list;
        };
        Func<CakeOS.Cui.CuiComponent, Control> reviewButton = component =>
        {
            var accept = component.AuthoredId == "home-permission-accept";
            var button = new Button { Content = accept ? "Allow once" : "Decline", IsEnabled = false, Name = component.AuthoredId };
            _workspaceReviewButtons.RemoveAll(existing => existing.Name == component.AuthoredId);
            _workspaceReviewButtons.Add(button);
            button.Click += (_, _) =>
            {
                if (!ReferenceEquals(_workspaceWindow, window) || !window.IsVisible) return;
                var selected = _workspaceRequests.SelectedItem as HomePermissionRequest;
                _ = RunOriginalNativeWork(() => RunWorkspaceActionAsync(() =>
                    DecideDisplayedPermissionAsync(window, selected, accept ? HomeApprovalChoice.Accept : HomeApprovalChoice.Decline)));
            };
            return button;
        };
        loader.BindAuthoredControl("dashboard-tiles", dashboardTiles);
        loader.BindAuthoredControl("home-permission-requests", permissionRequests);
        loader.BindAuthoredControl("home-permission-accept", reviewButton);
        loader.BindAuthoredControl("home-permission-decline", reviewButton);
        ApplyWorkspaceBindings();
    }

    private void UpdateSelectedPermissionDetails()
    {
        var selected = _workspaceRequests.SelectedItem as HomePermissionRequest;
        var retained = selected is not null && _displayedRequests.Any(request => ReferenceEquals(request, selected));
        _viewModel.Set("SelectedHomePermission", retained
            ? $"{selected!.Caller.DisplayName}\n{selected.Scope.TargetAppId}: {selected.Scope.ActionName}\n" +
                string.Join("\n", selected.Impact.KnownObjects.Select(item => $"{item.ObjectType}: {item.ObjectId}")) +
                $"\n{selected.Impact.ChangePreview}\nRisk: {selected.Policy.Risk}"
            : "Select a current permission request.");
        ApplyReviewAvailability();
        _loader?.RefreshBindings();
    }

    private void ApplyReviewAvailability()
    {
        var selected = _workspaceRequests.SelectedItem as HomePermissionRequest;
        var available = !_workspaceBusy && _workspaceInitialized && _controller.Navigation.Current == HomeRoute.Permissions &&
            selected is not null && _displayedRequests.Any(request => ReferenceEquals(request, selected));
        foreach (var button in _workspaceReviewButtons) button.IsEnabled = available;
    }

    private async Task DecideDisplayedPermissionAsync(Window originalWindow, HomePermissionRequest? selected, HomeApprovalChoice choice)
    {
        bool DisplayCurrent() => selected is not null && ReferenceEquals(_workspaceWindow, originalWindow) && originalWindow.IsVisible &&
            _controller.Navigation.Current == HomeRoute.Permissions &&
            ReferenceEquals(_workspaceRequests.SelectedItem, selected) &&
            _displayedRequests.Any(request => ReferenceEquals(request, selected));
        if (!DisplayCurrent())
        {
            _controller.Surface.SetNavigationStatus("Select a displayed permission request first.");
            return;
        }
        var digest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(selected)));
        var current = await _nativeComposition.Permissions.ReadRequestObservationAsync(selected!.RequestId);
        await RequireWorkspaceActorAsync();
        if (!DisplayCurrent() || current is null || current.Impact.ResourceBinding?.OriginalActor != _workspaceActor ||
            Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(current))) != digest)
            throw new UnauthorizedAccessException("The displayed permission request changed. Refresh and review it again.");
        var shown = await _nativeComposition.Permissions.AcknowledgePromptDisplayedAsync(selected.RequestId, digest);
        await RequireWorkspaceActorAsync();
        if (!shown.Succeeded || !DisplayCurrent())
            throw new UnauthorizedAccessException(shown.Succeeded ? "The original Home review retired." : shown.Message);
        var result = await _nativeComposition.Permissions.DecideAsync(selected.RequestId, choice);
        _controller.Surface.SetNavigationStatus($"{result.Code}: {result.Message}");
        await RefreshWorkspacePermissionsAsync();
    }

    private void ApplyWorkspaceBindings()
    {
        var surface = _controller.Surface;
        _viewModel.Set(nameof(HomeCuiSurface.IsDashboard), surface.IsDashboard);
        _viewModel.Set(nameof(HomeCuiSurface.IsLibrary), surface.IsLibrary);
        _viewModel.Set(nameof(HomeCuiSurface.IsEvents), surface.IsEvents);
        _viewModel.Set("IsSettings", surface.CurrentRoute == HomeRoute.Settings);
        _viewModel.Set("IsPermissions", surface.CurrentRoute == HomeRoute.Permissions);
        _viewModel.Set("NavigationStatus", surface.NavigationStatus);
        _viewModel.Set("BriefSummary", surface.BriefSummary);
        _viewModel.Set("DashboardTilesSummary", surface.DashboardTilesSummary);
        _viewModel.Set("EventsSummary", surface.EventsSummary);
        _viewModel.Set("EventItemsSummary", surface.EventItemsSummary);
        _viewModel.Set("EventItems", surface.EventItems.Select(item => new Dictionary<string, object?>
        {
            ["EventId"] = item.EventId, ["Timestamp"] = item.Timestamp.ToString("g"),
            ["SourceApp"] = item.SourceApp, ["EventType"] = item.EventType,
            ["ActorDisplayName"] = item.ActorDisplayName ?? item.ActorIdentity ?? "Unknown actor",
            ["Severity"] = item.Severity, ["Category"] = item.Category, ["Summary"] = item.Summary,
            ["IsSelected"] = surface.SelectedEventObject?.EventId == item.EventId
        }).ToArray());
        var selectedEvent = surface.EventItems.FirstOrDefault(item => item.EventId == surface.SelectedEventObject?.EventId);
        var selectedLink = selectedEvent?.Objects.ElementAtOrDefault(surface.SelectedEventObject?.ObjectIndex ?? -1);
        var canOpen = _workspaceInitialized && !_workspaceBusy && selectedLink is not null &&
            _featureNavigation.AvailableRoutes.Contains(selectedLink.TargetRouteId ?? HomeRouteIds.For(selectedLink.Destination));
        _viewModel.Set("CanOpenEventObject", canOpen);
        _viewModel.Set("CanRefreshEvents", _workspaceInitialized && !_workspaceBusy);
        _viewModel.Set("CanSelectEventObject", _workspaceInitialized && !_workspaceBusy);
        _viewModel.SetActionAvailability("OpenEventObject", canOpen);
        _viewModel.SetActionAvailability("RefreshEvents", _workspaceInitialized && !_workspaceBusy);
        _viewModel.SetActionAvailability("SelectEventObject", _workspaceInitialized && !_workspaceBusy);
        _viewModel.Set("HomeProfileSummary", _workspaceActor is null ? "Local profile is unavailable." :
            $"Device profile {_workspaceActor.ProfileId}. CAKE ID sign-in is separate from Home permissions. Open CAKE ID account to connect your session.");
        _viewModel.Set("HomeLayoutSummary", surface.Layout is null ? "Dashboard preferences have not loaded." :
            $"Saved revision {surface.Layout.Revision}. AI tiles: {(surface.Layout.AllowAiGeneratedTiles ? "allowed" : "off")}. " +
            $"AI rearrangement: {(surface.Layout.AllowAiReorder ? "allowed" : "off")}.");
        if (_workspaceTiles.ItemsSource != surface.Layout?.Tiles)
        {
            _workspaceTiles.ItemsSource = surface.Layout?.Tiles;
            _workspaceTiles.SelectedItem = surface.Layout?.Tiles.FirstOrDefault(tile => tile.TileInstanceId == surface.SelectedDashboardTileId);
        }
        foreach (var action in LayoutActions)
            _viewModel.SetActionAvailability(action, _workspaceInitialized && !_workspaceBusy);
        ApplyReviewAvailability();
    }

    private sealed class OwnedRoute(HomeApp owner, string routeId) : IHomeFeatureRouteHandler
    {
        public string RouteId => routeId;
        public async Task<HomeFeatureNavigationResult> OpenAsync(HomeFeatureNavigationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await owner.RequireWorkspaceActorAsync();
            return new(true, "HomeDestinationOpened", "Opened this device's Home workspace.", request);
        }
    }

    private sealed class CoreTileRegistry(HomeCoreRuntime originalCore) : IHomeTileProviderRegistry
    {
        private readonly IHomeTileProvider _provider = new CoreStatusTile(originalCore);
        public IReadOnlyList<IHomeTileProvider> GetProviders() => [_provider];
        public IHomeTileProvider? Find(string providerId) => providerId == _provider.Descriptor.ProviderId ? _provider : null;
    }

    private sealed class CoreStatusTile(HomeCoreRuntime originalCore) : IHomeTileProvider
    {
        public HomeTileProviderDescriptor Descriptor { get; } = new(1, "home.original-core", "Home services", "home",
            new HashSet<HomeTileSize> { HomeTileSize.Small, HomeTileSize.Medium, HomeTileSize.Large, HomeTileSize.Wide },
            ["home.core"], [], TimeSpan.FromMinutes(1), [], true, false);
        public Task<HomeTileContent> RefreshAsync(HomeTileInstance tile, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var actual = originalCore.Current;
            var available = actual.Services.Count(service => service.IsAvailable);
            return Task.FromResult(new HomeTileContent("observed", $"Home: {available} of {actual.Services.Count} services available.",
                ["home.core"], new Dictionary<string, string> { ["revision"] = actual.Revision.ToString() }, []));
        }
    }
}


