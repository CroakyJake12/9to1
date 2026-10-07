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
        _controller = new HomeCuiController(_dashboard, layoutService: layouts, featureNavigation: _featureNavigation);
        _workspaceActor = actor;
        foreach (var route in new[] { HomeRoute.Dashboard, HomeRoute.Settings, HomeRoute.Permissions })
        {
            var registered = _featureNavigation.Register(new OwnedRoute(this, HomeRouteIds.For(route)));
            if (!registered.Succeeded) throw new InvalidOperationException(registered.Message);
        }
        await _controller.RefreshLayoutAsync().ConfigureAwait(false);
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
        foreach (var command in new[] { "NavigateDashboard", "NavigateHome", "NavigateSettings", "NavigatePermissions" })
            RegisterWorkspaceAction(command, () => DispatchWorkspaceAsync(command));
        foreach (var command in LayoutActions)
            RegisterWorkspaceAction(command, () => DispatchWorkspaceAsync(command));
        RegisterWorkspaceAction("RefreshHomePermissions", RefreshWorkspacePermissionsAsync);
        RegisterWorkspaceAction("RefreshHomeWorkspace", RefreshWorkspaceAsync);
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
            "NavigatePermissions" => HomeRoute.Permissions,
            _ => (HomeRoute?)null
        };
        if (route.HasValue)
        {
            var navigation = await _controller.NavigateFeatureAsync(route.Value);
            _controller.Surface.SetNavigationStatus($"{navigation.Code}: {navigation.Message}");
            if (_controller.Navigation.Current == HomeRoute.Permissions) await RefreshWorkspacePermissionsAsync();
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
        foreach (var tile in _controller.Surface.Layout!.Tiles)
            await _controller.RefreshTileAsync(tile.TileInstanceId);
        if (_controller.Navigation.Current == HomeRoute.Permissions) await RefreshWorkspacePermissionsAsync();
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
        _viewModel.Set("HomePermissionSummary", $"{_displayedRequests.Count} requests for this device profile. " +
            "Select a request to review its exact action and affected objects.");
        UpdateSelectedPermissionDetails();
    }

    private void RegisterWorkspaceControls(CuiControlLoader loader, Window window)
    {
        _workspaceWindow = window;
        _workspaceReviewButtons.Clear();
        var tiles = _workspaceTiles = new ListBox();
        var requests = _workspaceRequests = new ListBox();
        _workspaceTiles.ItemTemplate = new FuncDataTemplate<HomeTileInstance>((tile, _) => new TextBlock
        { Text = tile is null ? "" : $"{tile.TileType} · {tile.Size} · {tile.Visibility}" });
        _workspaceTiles.SelectionChanged += (_, _) =>
        {
            if (ReferenceEquals(_workspaceWindow, window) && tiles.SelectedItem is HomeTileInstance selected &&
                _controller.Surface.Layout?.Tiles.Any(tile => ReferenceEquals(tile, selected)) == true)
                _controller.Surface.SelectDashboardTile(selected.TileInstanceId);
        };
        _workspaceRequests.ItemTemplate = new FuncDataTemplate<HomePermissionRequest>((request, _) => new TextBlock
        { Text = request is null ? "" : $"{request.Caller.DisplayName} · {request.Scope.ActionName}" });
        _workspaceRequests.SelectionChanged += (_, _) =>
        { if (ReferenceEquals(_workspaceWindow, window)) UpdateSelectedPermissionDetails(); };
        loader.RegisterControlType("ListBox", component => component.AuthoredId switch
        {
            "dashboard-tiles" => tiles,
            "home-permission-requests" => requests,
            _ => new ListBox()
        });
        loader.RegisterControlType("Button", component =>
        {
            if (component.AuthoredId is not ("home-permission-accept" or "home-permission-decline"))
                return new Button();
            var accept = component.AuthoredId == "home-permission-accept";
            var button = new Button { Content = accept ? "Allow once" : "Decline", IsEnabled = false };
            _workspaceReviewButtons.Add(button);
            button.Click += (_, _) =>
            {
                if (!ReferenceEquals(_workspaceWindow, window) || !window.IsVisible) return;
                var selected = requests.SelectedItem as HomePermissionRequest;
                _ = RunOriginalNativeWork(() => RunWorkspaceActionAsync(() =>
                    DecideDisplayedPermissionAsync(window, selected, accept ? HomeApprovalChoice.Accept : HomeApprovalChoice.Decline)));
            };
            return button;
        });
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
        _viewModel.Set("HomeProfileSummary", _workspaceActor is null ? "Local profile is unavailable." :
            $"Device profile {_workspaceActor.ProfileId}. Account sign-in is not connected in this native host.");
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
