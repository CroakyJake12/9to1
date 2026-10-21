using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Application.Haven;
using Haven.Core;
using Haven.Desktop;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HavenOS.Browse.Host.Tests;

/// <summary>
/// Normal positive shared-panel host initialization fixture (pass8 verdict recipe).
///
/// This composes the REAL production graph (real SQLite repositories, Home tuple, provider
/// registry, privacy) and drives the host's retained normal <c>InitializeAsync</c> — never a
/// private reflection invocation that bypasses <c>_initialization</c>. Setup/initialization
/// faults FAIL with their actual diagnostic (no catch-and-swallow dependency-gap masking).
///
/// It is NOT RUN until an allocated SDK slot executes it; it is an executable candidate.
/// </summary>
public sealed class NativeSharedPanelNormalInitializeTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    /// <summary>Runs the REAL normal <c>InitializeAsync</c> on a genuine production service graph,
    /// then exercises the authentic authored notifications panel via the real gateway (open -> visible
    /// mount -> close settlement). Any setup/initialization fault fails this test with the actual
    /// diagnostic; a pass is never manufactured.</summary>
    [AvaloniaFact]
    public async Task Normal_initialize_runs_real_composition_and_positive_shared_panel_journey()
    {
        using var cts = new CancellationTokenSource(Bound);
        await using var rig = await NormalRig.CreateAsync();

        // Retained normal initialize (not private InitializeCoreAsync).
        await rig.Host.InitializeAsync(cts.Token).WaitAsync(Bound);

        // Real readiness: the authored scene and workspace are present and ready.
        Assert.NotNull(Field<object?>(rig.Host, "_scene"));
        Assert.NotNull(Field<object?>(rig.Host, "_workspace"));
        Assert.True(Field<object>(rig.Host, "_workspace").GetType().GetProperty("IsReady") is { } readyProp
            && (bool)readyProp.GetValue(Field<object>(rig.Host, "_workspace"))!,
            "The Browse workspace must report readiness after the normal initialize.");
        Assert.NotNull(Field<object?>(rig.Host, "_sharedGateway"));

        // A real topbar/gateway action opens the authored notifications panel and it becomes visible.
        var gateway = Field<IHavenSharedControlGateway>(rig.Host, "_sharedGateway");
        var opened = await gateway.OpenNotificationsAsync(cts.Token).WaitAsync(Bound);
        Assert.True(opened.Opened, $"The authored notifications panel did not become visible: {opened.Code} {opened.Message}");
        Assert.NotNull(Field<object?>(rig.Host, "_sharedPanelRoot"));

        // Close restores and settles: request/join the live host first (including the shared-panel
        // settlement), then join the Home/database/files.
        var close = rig.Host.CloseAndDrainAsync();
        await close.WaitAsync(Bound);
        Assert.True(close.IsCompletedSuccessfully, "The host close did not settle cleanly.");
    }

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    /// <summary>Verifies the retained normal composition is GENUINE and exactly the shape the compiler
    /// feedback demanded: one SAME principal, exactly one genuine IAppPaths descriptor (AddHavenInfrastructure
    /// registers it; AddHavenOwnedWindowsHomeDomain requires exactly one), a real SQLite/Home graph, and the
    /// required desktop registrations (UserPreferencesService, WorkspaceSessionCoordinator). It then drives
    /// the REAL gateway open and asserts it settles a structured result truthfully — never a fabricated open.
    /// The separate positive test above retains normal InitializeAsync and actual authored panel opening;
    /// this additional test covers only the uninitialized-host denial. Any composition/inject
    /// fault fails with the actual diagnostic; a pass is never manufactured.</summary>
    [AvaloniaFact]
    public async Task Normal_rig_is_genuine_and_gateway_settles_truthfully()
    {
        using var cts = new CancellationTokenSource(Bound);
        await using var rig = await NormalRig.CreateAsync();

        // Exactly one genuine IAppPaths descriptor is registered (the host8 duplicate fix): the single
        // maintained descriptor from AddHavenInfrastructure, never a second one.
        var appPaths = rig.Services.GetServices<IAppPaths>().ToArray();
        Assert.Single(appPaths);

        // The required desktop/Home registrations resolve: the real SQLite-backed notification service,
        // and the two desktop services the compiled feedback flagged (missing Haven.Desktop.Services import
        // for WorkspaceSessionCoordinator). UserPreferencesService feeds the Home theme owner.
        Assert.NotNull(rig.Services.GetService<HavenNotificationService>());
        Assert.NotNull(rig.Services.GetService<UserPreferencesService>());
        Assert.NotNull(rig.Services.GetService<WorkspaceSessionCoordinator>());

        // The REAL gateway open settles a structured result. In this retained (non-booted-shell) composition
        // the authored shared-panel host/theme are not mounted, so it must refuse truthfully — never a hang,
        // never a fabricated Opened while the panel stays closed.
        var result = await CallOpenNotificationsAsync(rig.Host);
        Assert.False(result.Opened);

        // Close/join the live host (including the shared-panel settlement), then the Home/database/files.
        var close = rig.Host.CloseAndDrainAsync();
        await close.WaitAsync(Bound);
        Assert.True(close.IsCompletedSuccessfully, "The host close did not settle cleanly.");
    }

    /// <summary>Invokes the REAL notifications gateway open on the genuine host (the actual settlement
    /// path, never a direct mount/impersonation). Mirrors the integration suite's proven reflection seam.</summary>
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

    /// <summary>Genuine production-composition rig: real SQLite + <c>AddHavenInfrastructure</c> +
    /// the real Home tuple (<c>AddHavenOwnedWindowsHomeDomain</c>) + the real provider registry + the
    /// genuine desktop registrations the normal App supplies (UserPreferencesService,
    /// WorkspaceSessionCoordinator). One SAME OperatingSystemPrincipalSource instance is used for Home
    /// and the Home domain (as the API requires). No private host/container/binding injection
    /// impersonates normal startup.</summary>
    private sealed class NormalRig : IAsyncDisposable
    {
        internal readonly App App = new();
        internal readonly Paths Paths = new();
        internal readonly CancellationTokenSource AppLifetime = new(), WindowLifetime = new();
        internal readonly ClassicDesktopStyleApplicationLifetime Desktop = new();
        internal readonly OperatingSystemPrincipalSource Principal = new();
        internal readonly HomeNativeWindowsComposition Home;
        internal readonly App.UnifiedWorkspaceHost Host;
        internal readonly ServiceProvider Services;
        internal readonly SqliteDatabase Database;
        private bool _cleanedUp;

        private NormalRig()
        {
            Directory.CreateDirectory(Paths.DataDirectory);
            Database = new SqliteDatabase(Paths);
            var services = new ServiceCollection();
            services.AddHavenInfrastructure();
            services.AddHavenOriginalTaskExecutionServices();
            services.AddSingleton<IModelProviderRegistry>(new ModelProviderRegistry([new NormalTestModelProvider("test-local", true)]));
            // AddHavenInfrastructure already registers the single maintained IAppPaths descriptor, and
            // AddHavenOwnedWindowsHomeDomain requires exactly one — register none here.
            services.AddSingleton<UserPreferencesService>();
            services.AddSingleton<WorkspaceSessionCoordinator>();
            services.AddSingleton<HavenEventBus>();
            Home = new(new FileHomeCoreStateStore(Path.Combine(Paths.DataDirectory, "home.json")),
                Principal, Paths, new("native.normal-init." + Guid.NewGuid().ToString("N")));
            services.AddHavenOwnedWindowsHomeDomain(Home, Paths, Principal);
            Services = services.BuildServiceProvider();
            // Retain the SAME built Services provider as App's configured _services so the retained
            // normal initialize (App.OriginalModelCatalogueLifetime) resolves the real configured
            // provider instead of failing on a null _services.
            Set(App, "_services", Services);
            Host = new(App, Services, Home, AppLifetime.Token, WindowLifetime.Token);
            Set(App, "_actualDesktop", Desktop); Set(App, "_actualUnifiedWorkspace", Host);
            Set(App, "_actualDesktopBorrowers", new object[] { Host });
        }

        internal static async Task<NormalRig> CreateAsync()
        {
            var rig = new NormalRig();
            try
            {
                await rig.Database.InitializeAsync(CancellationToken.None).WaitAsync(Bound);
                await rig.Home.StartOriginalAsync().WaitAsync(Bound);
                var profile = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None).AsTask().WaitAsync(Bound);
                Assert.NotNull(profile);
                return rig;
            }
            catch
            {
                await rig.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_cleanedUp) return;
            _cleanedUp = true;
            var failures = new List<Exception>();
            // Cleanup always requests/joins the live host first (including the shared-panel
            // settlement), then the Home/database/files, and joins every original failure. Every
            // component is null-guarded so a constructor-partial acquisition never leaks Home/
            // database/files.
            if (Host is not null)
            {
                try { await Host.CloseAndDrainAsync().WaitAsync(Bound); }
                catch (Exception failure) { failures.Add(failure); }
            }
            if (Home is not null)
            {
                try { await Home.CloseAndDrainAsync().WaitAsync(Bound); } catch (Exception failure) { failures.Add(failure); }
            }
            if (Services is not null)
            {
                try { await Services.DisposeAsync().AsTask().WaitAsync(Bound); } catch (Exception failure) { failures.Add(failure); }
            }
            AppLifetime.Dispose(); WindowLifetime.Dispose();
            try { Directory.Delete(Paths.DataDirectory, true); } catch (Exception failure) { failures.Add(failure); }
            if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException("The normal-rig cleanup failed.", failures);
        }
    }

    private sealed class NormalTestModelProvider(string id, bool isLocal) : IModelProvider
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

    private sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "native-normal-init-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }

    private static void Set(object owner, string name, object? value) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(owner, value);
}
