using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CakeOS.Cui.Language;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeUiTests
{
    [AvaloniaFact]
    public void Navigation_vector_assets_are_packaged_in_the_real_NativeUI_assembly()
    {
        foreach (var name in new[] { "nav-back", "nav-forward", "nav-reload", "nav-stop", "search-mode" })
        {
            var address = new Uri("avares://HavenOS.Apps.Browse.NativeUI/Assets/" + name + ".svg");
            Assert.True(Avalonia.Platform.AssetLoader.Exists(address), address.ToString());
            using var stream = Avalonia.Platform.AssetLoader.Open(address);
            var svg = System.Xml.Linq.XDocument.Load(stream);
            Assert.Equal("svg", svg.Root!.Name.LocalName);
            Assert.NotEmpty(svg.Root.Elements());
        }
    }

    [AvaloniaFact]
    public async Task Omnibox_selects_Files_and_9to1_search_with_real_canonical_query_routes()
    {
        var chrome = await BrowseChrome.CreateAsync(new Paths(), internalContentFactory: new TabSurfaceInternalFixtureFactory(), homeProfileId: "fixture-profile");
        var workspace = new BrowseNativeWorkspace(chrome, _ => true, sharedContext: new SharedControlsFakeContext(null));
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            var selector = Assert.Single(window.GetLogicalDescendants().OfType<CuiMenuItem>(), item => item.Name == "BrowseSearchModeButton");
            Assert.True(selector.IsEnabled);
            foreach (var filesOnly in new[] { true, false })
            {
                await workspace.DispatchAsync(filesOnly ? "9to1.Browse.SearchFiles" : "9to1.Browse.SearchHome", null);
                Assert.True(workspace.TrySetValue("Address", "Résumé / quarterly notes"));
                await Click(window, workspace, Find(window, "BrowseNavigateButton"));
                Assert.True(BrowseOmniboxSearchRoute.TryParse(chrome.State.SelectedTab.Address, out var text, out var files));
                Assert.Equal("Résumé / quarterly notes", text); Assert.Equal(filesOnly, files);
                Assert.True(workspace.TryGetValue("Address", out var displayed)); Assert.Equal(text, displayed);
            }
            await chrome.NewTabAsync(true); await Task.WhenAll(workspace.OriginalNotifications);
            Assert.False(workspace.IsActionAvailable("9to1.Browse.SearchFiles"));
            Assert.False(workspace.IsActionAvailable("9to1.Browse.SearchHome"));
            Assert.True(workspace.IsActionAvailable("9to1.Browse.SearchWeb"));
            await CloseAppWindow(window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }

    [AvaloniaFact]
    public async Task Native_reload_preserves_unsaved_view_then_recreates_same_route_after_preflight()
    {
        var factory = new AppUiFixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), appContentFactory: factory, homeProfileId: "fixture-profile");
        await chrome.NavigateAsync("9to1://write/document");
        var route = chrome.State.SelectedTab.Address;
        var owner = Assert.Single(factory.Sessions);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            var parent = owner.OriginalNativeView.Parent;
            var reload = Find(window, "BrowseReloadButton");
            Assert.True(reload.IsEnabled);
            owner.Unsaved = true;
            await Click(window, workspace, reload);
            Assert.Single(factory.Sessions); Assert.Null(owner.OriginalClose);
            Assert.Same(owner, chrome.ObserveOriginalAppContent(chrome.State.SelectedTabId));
            Assert.Same(parent, owner.OriginalNativeView.Parent);
            Assert.Equal(route, chrome.State.SelectedTab.Address);
            owner.Unsaved = false;
            await Click(window, workspace, reload);
            Assert.Equal(2, factory.Sessions.Count);
            Assert.True(owner.CanRetireOriginalView);
            Assert.Same(factory.Sessions[1], chrome.ObserveOriginalAppContent(chrome.State.SelectedTabId));
            Assert.Equal(route, chrome.State.SelectedTab.Address);
            await CloseAppWindow(window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }

    [AvaloniaFact]
    public async Task Real_native_app_view_stays_mounted_across_tab_selection_and_web_controls_are_disabled()
    {
        var factory = new AppUiFixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), appContentFactory: factory, homeProfileId: "fixture-profile");
        await chrome.NavigateAsync("9to1://write/document"); var appId = chrome.State.SelectedTabId;
        var owner = Assert.Single(factory.Sessions);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            var view = owner.OriginalNativeView; var parent = view.Parent; Assert.NotNull(parent); Assert.True(view.IsEffectivelyVisible);
            Assert.False(Find(window, "BrowseChromiumTabButton").IsEnabled); Assert.False(Find(window, "BrowseBookmarkButton").IsEnabled);
            await Click(window, workspace, Find(window, "BrowseNewTabButton"));
            Assert.Same(parent, view.Parent); Assert.False(view.IsEffectivelyVisible);
            await chrome.SelectTabAsync(appId); await Task.WhenAll(workspace.OriginalNotifications);
            Assert.Same(view, owner.OriginalNativeView); Assert.Same(parent, view.Parent); Assert.True(view.IsEffectivelyVisible);
            await CloseAppWindow(window);
            Assert.True(owner.CanRetireOriginalView);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }

    [AvaloniaFact]
    public async Task Window_unsaved_preflight_veto_preserves_live_native_view_then_allows_original_close()
    {
        var factory = new AppUiFixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), appContentFactory: factory, homeProfileId: "fixture-profile");
        await chrome.NavigateAsync("9to1://write/document"); var owner = Assert.Single(factory.Sessions);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); owner.Unsaved = true;
            owner.HostPublication = () => workspace.IsOriginalPublicationCurrent;
            var parent = owner.OriginalNativeView.Parent;
            window.Close(); var original = window.OriginalClosePreparation; Assert.NotNull(original);
            Assert.False(await Observe(original!, window)); Assert.Null(window.OriginalClose);
            Assert.True(window.IsVisible); Assert.True(workspace.IsReady); Assert.Same(parent, owner.OriginalNativeView.Parent);
            Assert.Null(owner.OriginalClose);
            owner.Unsaved = false; await CloseAppWindow(window);
            Assert.False(window.IsVisible); Assert.Equal(2, window.OriginalClosePreparations.Count);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }

    [AvaloniaFact]
    public async Task Direct_workspace_close_veto_keeps_publication_and_commands_usable_for_retry()
    {
        var factory = new AppUiFixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), appContentFactory: factory, homeProfileId: "fixture-profile");
        await chrome.NavigateAsync("9to1://write/document"); var id = chrome.State.SelectedTabId;
        var owner = Assert.Single(factory.Sessions); var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            owner.HostPublication = () => workspace.IsOriginalPublicationCurrent; owner.Unsaved = true;
            var parent = owner.OriginalNativeView.Parent;
            var attempt = workspace.DisposeAsync().AsTask();
            await Assert.ThrowsAsync<BrowseCloseVetoException>(() => Observe(attempt, window));
            Assert.Null(workspace.OriginalClose); Assert.Null(chrome.OriginalClose);
            Assert.True(workspace.IsReady); Assert.True(workspace.IsOriginalPublicationCurrent);
            Assert.Same(parent, owner.OriginalNativeView.Parent); Assert.Null(owner.OriginalClose);
            await Click(window, workspace, Find(window, "BrowseNewTabButton"));
            await chrome.SelectTabAsync(id); await Task.WhenAll(workspace.OriginalNotifications);
            owner.Unsaved = false; await CloseAppWindow(window);
            Assert.All(owner.ObservedPublicationAtPreparation, Assert.True);
            Assert.True(owner.CanRetireOriginalView); Assert.False(window.IsVisible);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }

    [AvaloniaFact]
    public async Task Failed_actual_app_close_keeps_the_same_native_view_and_scene_mounted()
    {
        var factory = new AppUiFixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), appContentFactory: factory, homeProfileId: "fixture-profile");
        await chrome.NavigateAsync("9to1://write/document"); var owner = Assert.Single(factory.Sessions);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        var failure = new IOException("The actual document could not retire.");
        RetainedFailedOwners.Add((window, failure));
        window.Show(); await Observe(window.InitializeAsync(), window);
        owner.HostPublication = () => workspace.IsOriginalPublicationCurrent; owner.CloseFailure = failure;
        var view = owner.OriginalNativeView; var parent = view.Parent;
        window.Close(); Assert.True(await Observe(window.OriginalClosePreparation!, window));
        Dispatcher.UIThread.RunJobs(); Assert.NotNull(window.OriginalClose);
        var retained = await Assert.ThrowsAsync<AggregateException>(() => Observe(window.OriginalClose!, window));
        Assert.Contains(failure, retained.Flatten().InnerExceptions);
        Assert.True(window.IsVisible); Assert.Same(parent, view.Parent); Assert.True(view.IsEffectivelyVisible);
        Assert.False(owner.CanRetireOriginalView); Assert.False(workspace.IsReady);
        Assert.NotNull(window.SceneHost.Content);
    }

    [AvaloniaFact]
    public async Task Actual_CUI_action_queues_navigation_and_settles_before_its_own_surface_retirement()
    {
        var factory = new AppUiFixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), appContentFactory: factory, homeProfileId: "fixture-profile");
        await chrome.NavigateAsync("9to1://write/document"); var id = chrome.State.SelectedTabId; var owner = Assert.Single(factory.Sessions);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            owner.OpenRequested = settlement =>
            {
                Assert.True(workspace.QueueNavigation(new(id, AppContentAddress.Parse("https://next.example/"), false), settlement));
                Assert.Contains(settlement, workspace.OriginalOperations);
                Assert.False(settlement.IsCompleted);
            };
            owner.OpenButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var source = owner.Loader!.WhenActionsIdleAsync(); await Observe(source, window);
            Assert.NotNull(workspace.OriginalCommand); await Observe(workspace.OriginalCommand!, window);
            Assert.Equal(id, chrome.State.SelectedTabId); Assert.Equal("https://next.example/", chrome.State.SelectedTab.Address.Value);
            Assert.True(owner.OriginalClose!.IsCompletedSuccessfully); Assert.True(owner.ActualActionSettlement!.IsCompletedSuccessfully);
            await Retire(window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }

    private static async Task CloseAppWindow(BrowseNativeWindow window)
    {
        window.Close();
        if (window.OriginalClosePreparation is { } preparation) Assert.True(await Observe(preparation, window));
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.OriginalClose); await Observe(window.OriginalClose!, window);
    }

    private sealed class AppUiFixtureFactory : IBrowseAppContentFactory
    {
        public List<AppUiFixtureSession> Sessions { get; } = [];
        public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request)
        { var owner = new AppUiFixtureSession(request.Address); Sessions.Add(owner); return owner; }
    }
    // Real CUI controls and action pipeline, explicit fixture app/readiness. This
    // does not authenticate Home or certify a production native app factory.
    private sealed class AppUiFixtureSession(AppContentAddress address) : IBrowseNativeAppContentSession, ICuiActionDispatcher
    {
        private Control? _view;
        public AppContentAddress Address => address;
        public string Title => "Actual native fixture document";
        public string IconKey => "document";
        public event EventHandler? StateChanged { add { } remove { } }
        public volatile bool Unsaved;
        public Action<Task>? OpenRequested;
        public Func<bool>? HostPublication;
        public Exception? CloseFailure;
        public List<bool> ObservedPublicationAtPreparation { get; } = [];
        public CuiControlLoader? Loader;
        public Button? OpenButton;
        public Task? ActualActionSettlement;
        public Task? OriginalInitialization { get; private set; }
        public Task? OriginalClose { get; private set; }
        public bool IsPublicationCurrent => OriginalInitialization is { IsCompletedSuccessfully: true } && OriginalClose is null && (HostPublication?.Invoke() ?? true);
        public bool CanRetireOriginalView => OriginalClose is { IsCompletedSuccessfully: true };
        public Control OriginalNativeView => _view ?? throw new InvalidOperationException("Initialization has not issued the actual view.");
        public Task InitializeAsync(CancellationToken token = default) => OriginalInitialization ??= Dispatcher.UIThread.InvokeAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            Loader = new CuiControlLoader(); Loader.SetActionDispatcher(this);
            _view = Loader.Load(new CuiRichParser().Parse("<Cui id=\"fixture\" version=\"1\"><Page><Button id=\"Open\" action=\"open\">Open web</Button></Page></Cui>"));
            OpenButton = FindButton(_view ?? throw new InvalidOperationException("The fixture CUI root is absent."));
            Loader.WireBindings(_view);
        }).GetTask();
        private static Button FindButton(Control view) => view.GetLogicalDescendants().OfType<Button>().Single();
        public Task<bool> TryPrepareToCloseAsync(CancellationToken token = default)
        {
            var publication = HostPublication?.Invoke() ?? true;
            lock (ObservedPublicationAtPreparation) ObservedPublicationAtPreparation.Add(publication);
            return Task.FromResult(!Unsaved && publication);
        }
        public Task CloseAndDrainAsync() => OriginalClose ??= CloseAsync();
        private async Task CloseAsync()
        {
            var original = await Dispatcher.UIThread.InvokeAsync<Task>(async () =>
            {
                if (Unsaved) throw new InvalidOperationException("The fixture document remains unsaved.");
                if (CloseFailure is not null) throw CloseFailure;
                if (Loader is not null) { await Loader.WhenActionsIdleAsync(); Loader.Dispose(); }
            });
            await original;
        }
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default)
        {
            ActualActionSettlement = Loader!.WhenActionsIdleAsync();
            OpenRequested!(ActualActionSettlement);
            return ValueTask.CompletedTask;
        }
    }
}
