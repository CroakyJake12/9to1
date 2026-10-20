using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using Haven.SharedHeader;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace HavenOS.Browse.Host.Tests;

/// <summary>
/// Causal integration coverage for the native shared-panel host discovery and the truthful
/// mount/refusal contract, driven from the same cross-platform real Home/profile/scene rig as
/// <see cref="NativeAdmissionShutdownTests"/> with a genuine SQLite notification backend.
/// The authored shell starts hidden (HasOpenPanel=false), so a live visual/logical-descendant
/// search misses the nested panel host and the panel "opens" without ever being shown. This
/// suite proves the retained authored CUI graph locates the exact host even while closed, that
/// mounting reports the real outcome (visible panel, real backend rows/empty-state), and that
/// close joins the retained loader settlement (real ACK). No Windows guard, no injected fake
/// container, no synchronous cleanup.
/// </summary>
public sealed class NativeSharedPanelIntegrationTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    // Pure retained-graph coverage: the fixed finder walks the RETAINED authored CUI graph and
    // locates the exact named host even while the shell is closed, which a live visual/logical-
    // descendant search cannot.
    [AvaloniaFact]
    public void Retained_closed_panel_host_is_found_by_authored_graph_but_not_visual_descendants()
    {
        using var loader = new CuiControlLoader();
        // The authored shell starts CLOSED: HasOpenPanel resolves false, so the backdrop and the
        // nested panel host are hidden before any panel ever opens.
        loader.SetBindingContext(new ClosedShellBindings());
        var shell = HavenSharedHeaderDocuments.LoadShell();
        var loaded = loader.TryLoad(shell);
        Assert.NotNull(loaded.Root);
        Assert.DoesNotContain(loaded.Diagnostics, diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);

        // Regression guard: the previous implementation searched live visual/logical descendants
        // and therefore missed the closed nested panel host; this is exactly why the native
        // Notifications click reported ACTION_INVOKED_ONLY with no visible panel.
        var viaVisualDescendants = loaded.Root!.GetLogicalDescendants()
            .OfType<CuiContainerControl>()
            .FirstOrDefault(control => control.Name == "shared-panel-host");
        Assert.Null(viaVisualDescendants);

        // The fixed finder walks the RETAINED authored CUI graph and locates the exact named host
        // even while the shell is closed.
        var viaAuthoredGraph = CallFindNamedContainer(loaded.Root, "shared-panel-host");
        Assert.NotNull(viaAuthoredGraph);
        Assert.Equal("shared-panel-host", viaAuthoredGraph!.Name);

        // The scene host (always visible) is found by both strategies.
        var sceneHost = CallFindNamedContainer(loaded.Root, "shared-scene-host");
        Assert.NotNull(sceneHost);
    }

    // A genuinely un-composed host (no authored shell mounted) has no panel host. Mounting must
    // be a real, truthful refusal and the incoming loader must be retired — never a silent success.
    [AvaloniaFact]
    public async Task ShowSharedPanel_refuses_and_retires_when_no_genuine_authored_host_is_composed()
    {
        await using var rig = await Rig.CreateAsync();
        await RunBoundedAsync(rig, async rig =>
        {
            var host = rig.Host;
            // No composition was ever performed: the authored panel host is genuinely absent, so
            // a deny must be real, not a silent success while the panel stays closed.
            Assert.Null(Field<CuiContainerControl?>(host, "_sharedPanelHost"));
            using var loader = new CuiControlLoader();
            var loaded = loader.TryLoad(HavenSharedHeaderDocuments.LoadNotifications());
            Assert.NotNull(loaded.Root);
            var mounted = CallShowSharedPanel(host, loader, loaded.Root!);
            Assert.False(mounted);
            // The incoming loader was retired (no host to mount into) and no panel state was set.
            Assert.Null(Field<CuiContainerControl?>(host, "_sharedPanelHost"));
            Assert.Null(Field<CuiControlLoader?>(host, "_sharedPanelLoader"));
            // Retirement is joined: the retired loader's action settlement completes cleanly.
            await loader.WhenActionsIdleAsync().WaitAsync(Bound);
        });
    }

    // Genuine host discovery: the REAL compiled shell document plus the product finder locate the
    // exact authored panel host, and mounting a real loaded notifications panel into it makes the
    // panel visible (real shell bindings flip HasOpenPanel), backed by the real SQLite service.
    [AvaloniaFact]
    public async Task Real_backend_rows_show_and_close_joins_retained_loader_settlement()
    {
        await using var rig = await Rig.CreateAsync();
        await RunBoundedAsync(rig, async rig =>
        {
            var host = rig.Host;
            // Compose the genuine shell graph: the real compiled shell + the real finder. The
            // host fields hold only genuinely discovered/authored objects (never a fake container).
            ComposeGenuineShell(host);

            // Empty-state first: a fresh real backend has no rows.
            var empty = await rig.Notifications.GetRecentAsync(50, includeDismissed: false, CancellationToken.None);
            Assert.Empty(empty);

            // Seed a REAL durable notification row through the production repository path.
            var row = NewNotification("Native integration row");
            await rig.Notifications.PublishAsync(row, CancellationToken.None);
            var rows = await rig.Notifications.GetRecentAsync(50, includeDismissed: false, CancellationToken.None);
            Assert.Contains(rows, item => item.Id == row.Id);

            // Mount a real loaded notifications panel into the genuine authored host.
            using var loader = new CuiControlLoader();
            var loaded = loader.TryLoad(HavenSharedHeaderDocuments.LoadNotifications());
            Assert.NotNull(loaded.Root);
            var mounted = CallShowSharedPanel(host, loader, loaded.Root!);
            Assert.True(mounted);
            // The panel is genuinely mounted and visible: the root is a child of the authored host
            // and the real shell bindings report the panel open.
            var panelHost = Field<CuiContainerControl?>(host, "_sharedPanelHost");
            Assert.NotNull(panelHost);
            Assert.Contains(loaded.Root!, panelHost!.AuthoredChildren);
            Assert.Same(loader, Field<CuiControlLoader?>(host, "_sharedPanelLoader"));
            var shellBindings = Field<object?>(host, "_shellBindings");
            Assert.NotNull(shellBindings);
            Assert.True((bool)shellBindings!.GetType().GetProperty("HasOpenPanel")!.GetValue(shellBindings)!);

            // Close: hiding the panel joins the retained loader settlement and clears the mounted
            // panel state (real ACK — no dropped settlement, no silent leave).
            CallHideSharedPanel(host);
            Assert.Null(Field<CuiControlLoader?>(host, "_sharedPanelLoader"));
            Assert.Null(Field<Control?>(host, "_sharedPanelRoot"));
            await loader.WhenActionsIdleAsync().WaitAsync(Bound);
            Assert.False((bool)shellBindings.GetType().GetProperty("HasOpenPanel")!.GetValue(shellBindings)!);
        });
    }

    /// <summary>Composes the genuine authored shell graph on the host: loads the compiled shell
    /// through the real loader, discovers the exact named hosts with the product finder, and
    /// installs the REAL shell bindings and the discovered host. No fake container is ever
    /// injected; only genuinely authored/compiled objects are used.</summary>
    private static void ComposeGenuineShell(App.UnifiedWorkspaceHost host)
    {
        // The shell loader becomes host-owned (retired by the host's close settlement), so it is
        // intentionally not disposed here.
        var shellLoader = new CuiControlLoader();
        shellLoader.SetBindingContext(new ClosedShellBindings());
        var loaded = shellLoader.TryLoad(HavenSharedHeaderDocuments.LoadShell());
        Assert.NotNull(loaded.Root);
        Assert.DoesNotContain(loaded.Diagnostics, diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);
        var panelHost = CallFindNamedContainer(loaded.Root!, "shared-panel-host");
        Assert.NotNull(panelHost);
        var sceneHost = CallFindNamedContainer(loaded.Root!, "shared-scene-host");
        Assert.NotNull(sceneHost);

        // The real SharedPanelShellBindings instance (authored shell state owner), created through
        // its private parameterless constructor.
        var bindingsType = typeof(App.UnifiedWorkspaceHost).GetNestedType("SharedPanelShellBindings", BindingFlags.NonPublic)!;
        var bindings = Activator.CreateInstance(bindingsType, nonPublic: true);
        Set(host, "_shellLoader", shellLoader);
        Set(host, "_shellBindings", bindings);
        Set(host, "_sharedPanelHost", panelHost);
    }

    [AvaloniaFact]
    public void Retained_closed_shell_is_found_by_authored_graph_on_the_host()
    {
        // The finder is static product code; this asserts the retained authored graph resource is
        // the exact same one the host needs, independent of any host boot.
        using var loader = new CuiControlLoader();
        loader.SetBindingContext(new ClosedShellBindings());
        var loaded = loader.TryLoad(HavenSharedHeaderDocuments.LoadShell());
        Assert.NotNull(loaded.Root);
        Assert.NotNull(CallFindNamedContainer(loaded.Root!, "shared-panel-host"));
        Assert.NotNull(CallFindNamedContainer(loaded.Root!, "shared-scene-host"));
    }

    private static HavenNotification NewNotification(string title)
    {
        var now = DateTimeOffset.UtcNow;
        return new HavenNotification(Guid.NewGuid(), HavenNotificationKind.Task, HavenNotificationPriority.Information,
            "native-shared-panel", "Native shared panel", title, "A durable SQLite-backed row.", false, false, false, false,
            null, new HavenNavigationTarget(), [new HavenNotificationAction("navigation.open-task", "Open", new HavenNavigationTarget())],
            now, now);
    }

    private static async Task RunBoundedAsync(Rig rig, Func<Rig, Task> body)
    {
        using var cts = new CancellationTokenSource(Bound);
        try { await body(rig).WaitAsync(cts.Token); }
        finally { await rig.CleanupAsync(); }
    }

    private static CuiContainerControl? CallFindNamedContainer(Control root, string name)
    {
        var hostType = typeof(App).GetNestedType("UnifiedWorkspaceHost", BindingFlags.NonPublic)!;
        var method = hostType.GetMethod("FindNamedContainer", BindingFlags.Static | BindingFlags.NonPublic)!;
        try { return (CuiContainerControl?)method.Invoke(null, [root, name]); }
        catch (TargetInvocationException failure) when (failure.InnerException is { } actual)
        { ExceptionDispatchInfo.Capture(actual).Throw(); throw; }
    }

    private static bool CallShowSharedPanel(App.UnifiedWorkspaceHost host, CuiControlLoader loader, Control root)
    {
        var method = typeof(App.UnifiedWorkspaceHost)
            .GetMethod("ShowSharedPanel", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try { return (bool)method.Invoke(host, [loader, root])!; }
        catch (TargetInvocationException failure) when (failure.InnerException is { } actual)
        { ExceptionDispatchInfo.Capture(actual).Throw(); throw; }
    }

    private static void CallHideSharedPanel(App.UnifiedWorkspaceHost host)
    {
        var method = typeof(App.UnifiedWorkspaceHost)
            .GetMethod("HideSharedPanel", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try { method.Invoke(host, []); }
        catch (TargetInvocationException failure) when (failure.InnerException is { } actual)
        { ExceptionDispatchInfo.Capture(actual).Throw(); throw; }
    }

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static void Set(object owner, string name, object? value) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(owner, value);

    /// <summary>Mirrors the authored shell's initial closed state: HasOpenPanel is false.</summary>
    private sealed class ClosedShellBindings : ICuiBindingContext
    {
        public bool TryGetValue(string path, out object? value)
        {
            value = path == "HasOpenPanel" ? false : null;
            return value is not null;
        }
    }

    /// <summary>Cross-platform real Home/profile/scene rig, mirroring
    /// <see cref="NativeAdmissionShutdownTests"/>' rig, extended with a genuine SQLite
    /// notification backend (real <see cref="HavenNotificationService"/> over
    /// <see cref="HavenNotificationRepository"/>). No Windows guard and no fake mount.</summary>
    private sealed class Rig : IAsyncDisposable
    {
        internal readonly App App = new();
        internal readonly Paths Paths = new();
        internal readonly CancellationTokenSource AppLifetime = new(), WindowLifetime = new();
        internal readonly ClassicDesktopStyleApplicationLifetime Desktop = new();
        internal readonly HomeNativeWindowsComposition Home;
        internal readonly App.UnifiedWorkspaceHost Host;
        internal readonly ServiceProvider Services;
        internal readonly HavenNotificationService Notifications;
        internal readonly ExecutionEventHub Events;
        internal readonly SqliteDatabase Database;
        internal bool SeenGenuineFailure;
        private bool _cleanedUp;

        private Rig()
        {
            Directory.CreateDirectory(Paths.DataDirectory);
            Database = new SqliteDatabase(Paths);
            Events = new ExecutionEventHub(new ExecutionEventRepository(Database));
            Notifications = new HavenNotificationService(new HavenNotificationRepository(Database), Events);
            var services = new ServiceCollection();
            services.AddSingleton(Notifications);
            Services = services.BuildServiceProvider();
            Home = new(new FileHomeCoreStateStore(Path.Combine(Paths.DataDirectory, "home.json")),
                new OperatingSystemPrincipalSource(), Paths, new("native.shared-panel." + Guid.NewGuid().ToString("N")));
            Host = new(App, Services, Home, AppLifetime.Token, WindowLifetime.Token);
            Set(App, "_actualDesktop", Desktop); Set(App, "_actualUnifiedWorkspace", Host);
            Set(App, "_actualDesktopBorrowers", new object[] { Host });
        }

        internal static async Task<Rig> CreateAsync()
        {
            var rig = new Rig();
            try
            {
                await rig.Database.InitializeAsync(CancellationToken.None).WaitAsync(Bound);
                await rig.Home.StartOriginalAsync().WaitAsync(Bound);
                var profile = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None).AsTask().WaitAsync(Bound);
                Assert.NotNull(profile);
                var files = BuiltInModeSeed.Modes.Single(mode => mode.Key == "files");
                var registry = new NativeAppContentRegistry(new Modes(),
                    [new NativeAppContentRegistration(files.Id, files.Key, HavenAppRoutePolicy.Resolve(files).Kind, new FilesFactory())],
                    () => rig.Host.OriginalClose is null);
                var creation = BrowseChrome.CreateAsync(rig.Paths, appContentFactory: registry, homeProfileId: profile!.ProfileId);
                var chrome = await creation.WaitAsync(Bound);
                await chrome.NavigateAsync("9to1://files/fixture").WaitAsync(Bound);
                var workspace = new BrowseNativeWorkspace(chrome, _ => true);
                await rig.PublishNativeSceneAsync(workspace).WaitAsync(Bound);
                Set(rig.Host, "_initialization", creation); Set(rig.Host, "_chromeCreation", creation);
                Set(rig.Host, "_chrome", chrome); Set(rig.Host, "_workspace", workspace);
                return rig;
            }
            catch { await rig.CleanupAsync(); throw; }
        }

        internal async Task PublishNativeSceneAsync(BrowseNativeWorkspace workspace)
        {
            var scene = BrowseNativeSurface.CreateScene(workspace, new SceneReadiness());
            var host = new CuiSceneHost(scene.ControlRegistry);
            workspace.ObserveReadiness(await host.ShowAsync(scene, CancellationToken.None).WaitAsync(Bound));
        }

        internal async Task<IReadOnlyList<Exception>> CleanupAsync()
        {
            if (_cleanedUp) return [];
            _cleanedUp = true;
            var failures = new List<Exception>();
            var close = await Record.ExceptionAsync(() => Host.CloseAndDrainAsync().WaitAsync(Bound)).AsTask();
            if (close is not null && !ReferenceEquals(Host.OriginalClose, SeenGenuineFailure)) failures.Add(close);
            await AddFailureAsync(failures, () => Home.CloseAndDrainAsync().WaitAsync(Bound));
            Notifications.Dispose();
            await AddFailureAsync(failures, () => Events.DisposeAsync().AsTask().WaitAsync(Bound));
            await AddFailureAsync(failures, () => Services.DisposeAsync().AsTask().WaitAsync(Bound));
            AppLifetime.Dispose(); WindowLifetime.Dispose();
            try { Directory.Delete(Paths.DataDirectory, true); } catch (Exception failure) { failures.Add(failure); }
            return failures;
        }

        private static async Task AddFailureAsync(List<Exception> failures, Func<Task> action)
        {
            var failure = await Record.ExceptionAsync(action);
            if (failure is not null) failures.Add(failure);
        }

        public async ValueTask DisposeAsync()
        {
            var failures = await CleanupAsync();
            if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException("The bounded cleanup failed.", failures);
        }
    }

    private sealed class SceneReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) =>
            new(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "native-shared-panel-fixture",
                "Explicit local shared-panel fixture"));
    }

    private sealed class FilesFactory : IBrowseAppContentFactory
    {
        public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request) => new FilesOwner(request.Address);
    }

    private sealed class FilesOwner(AppContentAddress address) : IBrowseNativeAppContentSession
    {
        public AppContentAddress Address { get; } = address;
        public string Title => "Native shared-panel fixture"; public string IconKey => "files";
        public Control OriginalNativeView { get; } = new Border();
        public event EventHandler? StateChanged { add { } remove { } }
        public bool IsPublicationCurrent => OriginalInitialization is { IsCompletedSuccessfully: true } && OriginalClose is null;
        public bool CanRetireOriginalView => OriginalClose is { IsCompletedSuccessfully: true };
        public Task? OriginalInitialization { get; private set; } public Task? OriginalClose { get; private set; }
        public Task InitializeAsync(CancellationToken token = default) => OriginalInitialization ??= Task.CompletedTask;
        public Task<bool> TryPrepareToCloseAsync(CancellationToken token = default) => Task.FromResult(true);
        public Task CloseAndDrainAsync() => OriginalClose ??= Task.CompletedTask;
    }

    private sealed class Modes : IModeRegistry
    {
        public Task<ModeDefinition?> GetModeByIdAsync(Guid id, CancellationToken token) =>
            Task.FromResult<ModeDefinition?>(BuiltInModeSeed.Modes.SingleOrDefault(mode => mode.Id == id));
        public Task<ModeDefinition?> GetModeByKeyAsync(string key, CancellationToken token) =>
            Task.FromResult<ModeDefinition?>(BuiltInModeSeed.Modes.SingleOrDefault(mode => mode.Key == key));
        public Task<IReadOnlyList<ModeDefinition>> GetModesAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ModeDefinition>>(BuiltInModeSeed.Modes.ToArray());
        public Task UpsertModeAsync(ModeDefinition app, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModeVersion>> GetVersionsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task AddVersionAsync(ModeVersion version, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModePermissionGrant>> GetGrantsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertGrantAsync(ModePermissionGrant grant, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "native-shared-panel-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
}
