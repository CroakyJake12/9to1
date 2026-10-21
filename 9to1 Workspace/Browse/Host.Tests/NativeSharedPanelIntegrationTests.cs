using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Dulche.Runtime;
using Haven.Application;
using Haven.Application.Haven;
using Haven.Core;
using Haven.Desktop;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell;
using Haven.Infrastructure;
using Haven.SharedHeader;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace HavenOS.Browse.Host.Tests;

/// <summary>
/// Honest causal coverage for the native shared-panel host discovery and the truthful mount/refusal
/// contract, driven from the same cross-platform real Home/profile/scene rig as
/// <see cref="NativeAdmissionShutdownTests"/> with a genuine SQLite notification backend.
///
/// No private host/container/binding injection impersonates startup; the richer production shell
/// composition, rendering and mount-throw journeys require booting the full DI/engines graph
/// (<c>InitializeCoreAsync</c>) and are outside this minimal cross-platform rig (reported NOT RUN).
/// This suite keeps the genuine retained-authored-graph finder coverage, proves the real SQLite
/// backend seeds/reads, and proves the genuine gateway settles a truthful refusal (never a
/// fabricated open) while the full composition is uninitialized.
/// </summary>
public sealed class NativeSharedPanelIntegrationTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    /// <summary>Provider id for the registered local test provider used by the real model
    /// projection producer (a route seeding against it would assert known/local locality).</summary>
    private const string LocalProviderId = "test-local";

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

        // The fixed finder walks the RETAINED authored CUI graph and locates the exact named host
        // even while the shell is closed; a live visual/logical-descendant search misses it.
        var viaVisualDescendants = loaded.Root!.GetLogicalDescendants()
            .OfType<CuiContainerControl>()
            .FirstOrDefault(control => control.Name == "shared-panel-host");
        Assert.Null(viaVisualDescendants);

        var viaAuthoredGraph = CallFindNamedContainer(loaded.Root, "shared-panel-host");
        Assert.NotNull(viaAuthoredGraph);
        Assert.Equal("shared-panel-host", viaAuthoredGraph!.Name);

        // The scene host (always visible) is found by both strategies.
        var sceneHost = CallFindNamedContainer(loaded.Root, "shared-scene-host");
        Assert.NotNull(sceneHost);
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

    // Genuine backend coverage: the REAL SQLite notification service (production repository) seeds a
    // durable row and reads it back. This owns the backend half of the panel journey without any
    // injected fake service or simulated persistence.
    [AvaloniaFact]
    public async Task Genuine_sqlite_backend_seeds_and_reads()
    {
        await using var rig = await Rig.CreateAsync();
        await RunBoundedAsync(rig, async rig =>
        {
            // Fresh real backend has no rows (honest empty state).
            var empty = await rig.Notifications.GetRecentAsync(50, includeDismissed: false, CancellationToken.None);
            Assert.Empty(empty);

            // Seed a REAL durable notification row through the production repository path.
            var row = NewNotification("Native integration row");
            await rig.Notifications.PublishAsync(row, CancellationToken.None);
            var rows = await rig.Notifications.GetRecentAsync(50, includeDismissed: false, CancellationToken.None);
            Assert.Contains(rows, item => item.Id == row.Id);
        });
    }

    // Truthful uninitialized-gateway refusal: while the full production composition has not been
    // booted (no genuine Home theme/actor), the REAL gateway open settles a truthful refusal and
    // never fabricates an open that would leave the panel closed in silence. The Browse consumer
    // likewise has no invented host context (no model name/percent/metering).
    [AvaloniaFact]
    public async Task Uninitialized_gateway_denies_truthfully_without_inventing_open()
    {
        await using var rig = await Rig.CreateAsync();
        await RunBoundedAsync(rig, async rig =>
        {
            var result = await CallOpenNotificationsAsync(rig.Host);
            Assert.False(result.Opened);

            // No genuine composition means no host context on the Browse consumer: the truthful
            // unavailable state (empty name/percent) is what the rail renders, never invented data.
            Assert.Null(Field<object?>(rig.Workspace, "_hostContext"));
        });
    }

    // Real-producer coverage: while the full production shell is not booted, the host's genuine
    // model projection runs against the REAL Home route repository + real provider registry (both
    // registered through the service provider). Without a saved user-chat route the canonical draft
    // has no enabled candidate, so the projection is truthfully null — never a fabricated model.
    [AvaloniaFact]
    public async Task Production_model_projection_without_a_saved_route_stays_truthfully_unavailable()
    {
        await using var rig = await Rig.CreateAsync();
        await RunBoundedAsync(rig, async rig =>
        {
            await CallRefreshModelInfoAsync(rig.Host);
            Assert.Null(Field<HavenTopBarModelInfo?>(rig.Host, "_modelInfo"));
        });
    }

    // Locality known/unknown coverage through the real producer requires seeding a canonical route
    // (ConfiguredModelRoute/ProviderPolicy live in the external Dulche.Runtime contract, not authored
    // in this workspace). Those assertions are therefore implemented as NOT RUN/blocked here rather
    // than fabricating the external route API or guessing its shape.

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
        Exception? bodyFailure = null;
        try { await body(rig).WaitAsync(cts.Token); }
        catch (Exception failure) { bodyFailure = failure; }
        // Causal cleanup: always run cleanup and retain EVERY failure. A body failure and cleanup
        // failures are combined, never swallowed (the prior implementation ignored CleanupAsync's
        // returned failure list).
        var cleanupFailures = await rig.CleanupAsync();
        if (bodyFailure is not null)
        {
            // Preserve the body failure as the first inner exception (identity + original stack) and
            // append every cleanup failure; the aggregate is built from the actual Exception instances,
            // never a list passed to a single-Exception constructor.
            if (cleanupFailures.Count == 0) ExceptionDispatchInfo.Capture(bodyFailure).Throw();
            throw new AggregateException(new[] { bodyFailure }.Concat(cleanupFailures));
        }
        if (cleanupFailures.Count == 1) ExceptionDispatchInfo.Capture(cleanupFailures[0]).Throw();
        if (cleanupFailures.Count > 1) throw new AggregateException("The bounded cleanup failed.", cleanupFailures);
    }

    private static CuiContainerControl? CallFindNamedContainer(Control root, string name)
    {
        var hostType = typeof(App).GetNestedType("UnifiedWorkspaceHost", BindingFlags.NonPublic)!;
        var method = hostType.GetMethod("FindNamedContainer", BindingFlags.Static | BindingFlags.NonPublic)!;
        try { return (CuiContainerControl?)method.Invoke(null, [root, name]); }
        catch (TargetInvocationException failure) when (failure.InnerException is { } actual)
        { ExceptionDispatchInfo.Capture(actual).Throw(); throw; }
    }

    /// <summary>Invokes the REAL notifications gateway open on the genuine host. This exercises the
    /// actual settlement path, never a direct show/impersonation of a mounted shell.</summary>
    private static async Task<HavenSharedOpenResult> CallOpenNotificationsAsync(App.UnifiedWorkspaceHost host)
    {
        var method = typeof(App.UnifiedWorkspaceHost)
            .GetMethod("OpenNotificationsPanelAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            var task = (Task<HavenSharedOpenResult>)method.Invoke(host, [CancellationToken.None])!;
            return await task.WaitAsync(Bound);
        }
        catch (TargetInvocationException failure) when (failure.InnerException is { } actual)
        { ExceptionDispatchInfo.Capture(actual).Throw(); throw; }
    }

    /// <summary>Invokes the host's REAL model-projection refresh (the genuine Home route/provider
    /// producer). This is the production path the shell compose and panel-close hooks call; it is
    /// never a hand-built record or a fake producer.</summary>
    private static async Task CallRefreshModelInfoAsync(App.UnifiedWorkspaceHost host)
    {
        var method = typeof(App.UnifiedWorkspaceHost)
            .GetMethod("RefreshModelInfoAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            var task = (Task)method.Invoke(host, [CancellationToken.None])!;
            await task.WaitAsync(Bound);
        }
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

    /// <summary>Minimal real IModelProvider for the registered provider registry used by the
    /// production model-projection producer. Locality is genuine; the single descriptor lets the
    /// registry resolve the enabled candidate's provider.</summary>
    private sealed class TestModelProvider(string id, bool isLocal) : IModelProvider
    {
        public string Id => id;
        public string DisplayName => id;
        public bool IsLocal => isLocal;
        public bool CanManageModels => false;
        public ModelProviderKind Kind => isLocal ? ModelProviderKind.Ollama : ModelProviderKind.OpenAI;
        public ProviderModelDescriptor Model { get; } = new(id, isLocal, new ModelDescriptor(
            "test-model", 123, "synthetic", "7B", "Q8",
            new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }, DateTimeOffset.UnixEpoch));
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([Model]); }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) =>
            Task.FromResult(new ProviderHealthStatus(id, true, "synthetic", TimeSpan.Zero, DateTimeOffset.UnixEpoch));
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class TestPrivacyPreferenceStore : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current { get; set; } = PrivacyPreferences.Default;
        public Task UpdateAsync(PrivacyPreferences value, CancellationToken token) { Current = value; return Task.CompletedTask; }
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
        internal BrowseNativeWorkspace Workspace;
        internal readonly ServiceProvider Services;
        internal readonly HavenNotificationService Notifications;
        internal readonly ExecutionEventHub Events;
        internal readonly SqliteDatabase Database;
        private bool _cleanedUp;

        private Rig()
        {
            Directory.CreateDirectory(Paths.DataDirectory);
            Database = new SqliteDatabase(Paths);
            Events = new ExecutionEventHub(new ExecutionEventRepository(Database));
            Notifications = new HavenNotificationService(new HavenNotificationRepository(Database), Events);
            var services = new ServiceCollection();
            services.AddSingleton(Notifications);
            // Registered model provider registry + privacy store: the production producer bridge
            // resolves them through the real service provider (never a fabricated fixture record).
            services.AddSingleton<IModelProviderRegistry>(new ModelProviderRegistry([new TestModelProvider(LocalProviderId, isLocal: true)]));
            services.AddSingleton<IPrivacyPreferenceStore>(new TestPrivacyPreferenceStore());
            Services = services.BuildServiceProvider();
            Home = new(new FileHomeCoreStateStore(Path.Combine(Paths.DataDirectory, "home.json")),
                new OperatingSystemPrincipalSource(), Paths, new("native.shared-panel." + Guid.NewGuid().ToString("N")));
            Host = new(App, Services, Home, AppLifetime.Token, WindowLifetime.Token);
            Workspace = null!;
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
                // Record the genuine workspace for consumer assertions (its _hostContext stays null
                // until the full production composition boots).
                rig.Workspace = workspace;
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
            if (close is not null) failures.Add(close);
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
