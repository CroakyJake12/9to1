using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Tasks;
using Avalonia.Controls;
using Haven.Infrastructure;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

/// <summary>Actual Windows Home runtime/profile controls. Held sources and deliberately
/// substituted principals/aliases are negatives, never installation, native frame,
/// permission, Task execution or GUI acceptance evidence. Linux is a failed prerequisite.</summary>
public sealed class WindowsHomeSameProcessCompositionTests
{
    [Fact]
    public async Task Unstarted_real_Home_is_unavailable_with_zero_principal_reads_and_no_installed_session()
    {
        await using var control = new Control();
        var observed = await control.CheckAsync();
        Assert.Equal(CuiSceneAvailabilityState.Unavailable, observed.State);
        Assert.Equal("SameProcessHomeNotStarted", observed.Code);
        Assert.Equal(0, control.Principal.Calls);
        Assert.Null(control.Home.OriginalStartTask);
        Assert.Null(control.Provider.GetService<IHomeNativeStartupSession>());
        Assert.False(control.Home.InstalledPeerAdmissionConfigured);
        Assert.False(File.Exists(control.ProfileStatePath));
    }

    [Fact]
    public async Task Real_started_services_and_OS_profile_are_observed_without_account_or_permission_authority()
    {
        await using var control = new Control();
        await control.StartAsync();
        var observed = await control.CheckAsync();
        Assert.Equal(CuiSceneAvailabilityState.Ready, observed.State);
        Assert.Equal("SameProcessHomeRuntimeCurrent", observed.Code);
        Assert.False(control.Home.InstalledPeerAdmissionConfigured);
        Assert.Null(control.Provider.GetService<IHomeNativeStartupSession>());
        var homeActor = await control.Home.Profiles.GetCurrentAsync(CancellationToken.None);
        var taskActor = await control.Provider.GetRequiredService<HostLocalTaskActorSource>().GetCurrentAsync(CancellationToken.None);
        Assert.NotNull(homeActor); Assert.NotNull(taskActor);
        Assert.NotEqual(homeActor.ActorId, taskActor.ActorId);
        Assert.NotEqual(homeActor.ProfileId, taskActor.ProfileId);
        Assert.Null(homeActor.AccountId); Assert.Null(homeActor.OrganisationId);
        Assert.Null(taskActor.AccountId); Assert.Null(taskActor.OrganisationId);
        var permissions = await control.Home.Permissions.GetSnapshotAsync(cancellationToken: CancellationToken.None);
        Assert.Empty(permissions.Grants); Assert.Empty(permissions.PendingRequests);
        Assert.Null(await control.Files.GetConfigurationAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Foreign_runtime_alias_refuses_before_profile_or_Files_service_acquisition()
    {
        await using var control = new Control();
        var foreignRuntime = new HomeCoreRuntime();
        var foreign = new ForeignRuntimeProvider(control.Provider, foreignRuntime);
        Assert.Throws<UnauthorizedAccessException>(() => NativeFilesDesktopRoute.BindOriginalSameProcess(
            foreign, control.Home, control.AppLifetime.Token, control.WindowLifetime.Token));
        Assert.Equal(0, foreign.FilesAcquisitions);
        Assert.Equal(0, control.Principal.Calls);
        Assert.Null(control.Home.OriginalStartTask);
        await foreignRuntime.DisposeAsync();
    }

    [Fact]
    public async Task Service_changes_during_profile_read_and_after_observation_never_keep_old_Ready()
    {
        await using var control = new Control();
        await control.StartAsync();
        control.Principal.OnRead = (call, principal) =>
        {
            if (call == 3) control.Home.Runtime.ReportServiceState("permissions.trust", HomeServiceLifecycleState.Unavailable, false);
            return principal;
        };
        var observed = await control.CheckAsync();
        Assert.Equal(CuiSceneAvailabilityState.Unavailable, observed.State);
        control.Principal.OnRead = null;
        control.Home.Runtime.ReportServiceState("permissions.trust", HomeServiceLifecycleState.Ready, true);
        observed = await control.CheckAsync();
        Assert.Equal(CuiSceneAvailabilityState.Ready, observed.State);
        control.Home.Runtime.ReportServiceState("home.state", HomeServiceLifecycleState.Unavailable, false);
        var final = WindowsHomeSameProcessRuntimeObservation.RevalidateBeforePublication(control.Provider, control.Home, observed);
        Assert.Equal(CuiSceneAvailabilityState.Unavailable, final.State);
        Assert.Equal("SameProcessHomeServiceUnready", final.Code);
        Assert.Null(control.Home.OriginalCloseTask);
    }

    [Fact]
    public async Task Late_principal_substitution_refuses_and_preserves_original_profile_state()
    {
        await using var control = new Control();
        await control.StartAsync();
        var originalState = await File.ReadAllBytesAsync(control.ProfileStatePath);
        control.Principal.OnRead = (call, principal) => call == 3 ? principal + ":foreign-negative" : principal;
        var actual = control.CheckAsync();
        var failure = await Record.ExceptionAsync(async () => await actual);
        Assert.NotNull(failure); Assert.True(actual.IsFaulted);
        Assert.True(HasCause<UnauthorizedAccessException>(failure));
        Assert.Equal(originalState, await File.ReadAllBytesAsync(control.ProfileStatePath));
        Assert.Null(control.Home.OriginalCloseTask);
    }

    [Fact]
    public async Task Held_actual_principal_Task_is_not_replaced_by_App_cancellation_or_early_observation()
    {
        await using var control = new Control();
        await control.StartAsync();
        control.Principal.HoldAt = 1;
        var actual = control.CheckAsync();
        await control.Principal.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var raw = control.Principal.HeldOriginal!;
        control.AppLifetime.Cancel();
        Assert.False(raw.IsCompleted); Assert.False(actual.IsCompleted);
        Assert.Null(control.Home.OriginalCloseTask);
        control.Principal.Release.TrySetResult();
        Assert.NotNull(await Record.ExceptionAsync(async () => await actual));
        Assert.True(raw.IsCompletedSuccessfully); Assert.True(actual.IsCompleted);
        Assert.Null(control.Home.OriginalCloseTask); // Borrower cancellation never stops shared Home.
    }

    [AvaloniaFact]
    public async Task Restored_source_context_cannot_self_join_real_Files_route_or_shared_Home_and_external_close_joins_raw_Task()
    {
        var before = ExecutionContext.Capture()!;
        await using var control = new Control();
        await control.StartAsync();
        var route = NativeFilesDesktopRoute.BindOriginalSameProcess(control.Provider, control.Home,
            control.AppLifetime.Token, control.WindowLifetime.Token);
        Exception? routeRefusal = null, homeRefusal = null;
        control.Principal.HoldAt = 1;
        control.Principal.OnRead = (call, principal) =>
        {
            if (call == 1) ExecutionContext.Run(before, _ =>
            {
                routeRefusal = Record.Exception(() => { _ = route.CloseAndDrainAsync(); });
                homeRefusal = Record.Exception(() => { _ = control.Home.CloseAndDrainAsync(); });
            }, null);
            return principal;
        };
        var actual = route.AdmitOriginalShellInitializationAsync(CancellationToken.None);
        await control.Principal.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.IsType<InvalidOperationException>(routeRefusal);
        Assert.IsType<InvalidOperationException>(homeRefusal);
        Assert.Null(control.Home.OriginalCloseTask);
        var raw = control.Principal.HeldOriginal!;
        var close = route.CloseAndDrainAsync();
        Assert.Same(close, route.CloseAndDrainAsync());
        Assert.False(raw.IsCompleted); Assert.False(actual.IsCompleted); Assert.False(close.IsCompleted);
        control.Principal.OnRead = null; control.Principal.Release.TrySetResult();
        Assert.NotNull(await Record.ExceptionAsync(async () => await actual));
        Assert.NotNull(await Record.ExceptionAsync(async () => await close));
        Assert.True(raw.IsCompletedSuccessfully); Assert.True(actual.IsCompleted); Assert.True(close.IsCompleted);
        Assert.Null(control.Home.OriginalCloseTask);
        Assert.Same(control.Home, control.Provider.GetRequiredService<HomeNativeWindowsComposition>());
    }

    private static bool HasCause<T>(Exception actual) where T : Exception => actual is T ||
        actual is AggregateException group && group.InnerExceptions.Any(HasCause<T>) ||
        actual.InnerException is { } inner && HasCause<T>(inner);

    private sealed class ForeignRuntimeProvider(IServiceProvider original, HomeCoreRuntime foreign) : IServiceProvider
    {
        internal int FilesAcquisitions;
        public object? GetService(Type type)
        {
            if (type == typeof(FilesNativeBrowserService)) FilesAcquisitions++;
            return type == typeof(HomeCoreRuntime) ? foreign : original.GetService(type);
        }
    }

    internal sealed class PrincipalControl : ITrustedHostPrincipalSource
    {
        private readonly OperatingSystemPrincipalSource _actual = new();
        internal int Calls;
        internal int HoldAt;
        internal Func<int, string?, string?>? OnRead;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task<string?>? HeldOriginal;
        public ValueTask<string?> GetPrincipalAsync(CancellationToken token)
        {
            var actual = ReadAsync(token);
            if (Calls == HoldAt && HoldAt != 0)
            { HeldOriginal = actual; Entered.TrySetResult(); }
            return new(actual);
        }
        private async Task<string?> ReadAsync(CancellationToken token)
        {
            var actual = await _actual.GetPrincipalAsync(token);
            var call = Interlocked.Increment(ref Calls);
            if (OnRead is { } callback) actual = callback(call, actual);
            if (call == HoldAt)
            {
                await Release.Task; // Deliberately held actual source; cancellation cannot fabricate settlement.
            }
            return actual;
        }
    }

    internal sealed class Control : IAsyncDisposable
    {
        private readonly string _root;
        internal string ProfileStatePath => Path.Combine(_root, "home.json");
        internal readonly PrincipalControl Principal = new();
        internal readonly CancellationTokenSource AppLifetime = new(), WindowLifetime = new();
        internal readonly HomeNativeWindowsComposition Home;
        internal readonly ServiceProvider Provider;
        internal readonly NativeFilesWorkspaceService Files;
        internal Control()
        {
            Assert.True(OperatingSystem.IsWindows(), "This owning control requires actual Windows.");
            _root = Path.Combine(Path.GetTempPath(), "astra-same-process-home51-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            IAppPaths paths = new Paths(_root);
            NativeFilesWorkspaceService? files = null; NativeFilesWorkspaceAuthority? authority = null;
            FilesArtifactResourceResolver? resolver = null;
            Home = new(new FileHomeCoreStateStore(ProfileStatePath), Principal, paths,
                new("9to1.home.same-process-control51." + Guid.NewGuid().ToString("N")),
                configureOriginalStores: identity =>
                {
                    files = new(identity.StateStore, identity.Profiles);
                    return new([files], new Dictionary<Type, object> { [typeof(NativeFilesWorkspaceService)] = files });
                },
                configureOriginalResolvers: ownership =>
                {
                    authority = new(files!, ownership.Identity.Profiles, ownership.Ownership); resolver = new(authority);
                    return new([resolver], new Dictionary<Type, object>
                        { [typeof(NativeFilesWorkspaceAuthority)] = authority, [typeof(FilesArtifactResourceResolver)] = resolver });
                });
            Files = files!;
            var services = new ServiceCollection(); services.AddSingleton(paths);
            services.AddHavenOwnedWindowsHomeDomain(Home, paths, Principal);
            services.AddSingleton(Files); services.AddSingleton(authority!); services.AddSingleton(resolver!);
            services.AddSingleton<HostLocalTaskActorSource>(); services.AddFilesNativeHost();
            Provider = services.BuildServiceProvider();
        }
        internal async Task StartAsync() { await Home.StartOriginalAsync(); Principal.Calls = 0; }
        internal Task<CuiSceneAvailability> CheckAsync() => WindowsHomeSameProcessRuntimeObservation.CheckAsync(
            Provider, Home, AppLifetime.Token, WindowLifetime.Token, body => body(), () => { }, CancellationToken.None);
        public async ValueTask DisposeAsync()
        {
            Principal.OnRead = null; Principal.Release.TrySetResult();
            await Home.CloseAndDrainAsync(); await Provider.DisposeAsync();
            AppLifetime.Dispose(); WindowLifetime.Dispose(); Directory.Delete(_root, true);
        }
    }

    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
}


public sealed partial class SpaceTasksDashboardOriginalWorkTests
{
    [AvaloniaFact]
    public async Task Real_same_process_Home_reopens_the_SAME_canonical_Task_Run_without_writes_or_installed_Ready()
    {
        await using var home = new WindowsHomeSameProcessCompositionTests.Control();
        await home.StartAsync();
        await using var context = await Rig.CreateAsync();
        var forwarder = new NativeReadinessForwarder();
        var page = new SpaceTaskWidgetPage(context.Service, context.Canonical, context.Space.Id,
            context.Conversation.Id, forwarder, expectedOriginalTaskId: context.Snapshot.TaskId,
            expectedOriginalExecutionId: context.Snapshot.ExecutionId);
        var frame = await page.AcquireOriginalNativeReadinessAsync(actual =>
            NativeCanonicalTaskSceneReadiness.BindOriginalSameProcess(actual, home.Provider, home.Home,
                home.Provider.GetRequiredService<HostLocalTaskActorSource>(), home.AppLifetime.Token, home.WindowLifetime.Token));
        forwarder.Owner = frame;
        var window = new Window { Width = 960, Height = 720, Content = page };
        try
        {
            window.Show();
            var activate = page.ActivateAsync(CancellationToken.None);
            await activate.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var observed = await forwarder.ActualCheck!;
            Assert.Equal(CuiSceneAvailabilityState.Ready, observed.State);
            Assert.Equal("SameProcessHomeRuntimeCurrent", observed.Code);
            Assert.Equal(context.Snapshot.TaskId, page.OriginalReadinessContext.TaskId);
            Assert.Equal(context.Snapshot.ExecutionId, page.OriginalReadinessContext.RunId);
            Assert.Equal(context.Snapshot.TaskId, context.Tasks.Current!.TaskId);
            Assert.Equal(context.Snapshot.ExecutionId, context.Tasks.Current.ExecutionId);
            Assert.Equal(0, context.Tasks.Writes);
            Assert.False(home.Home.InstalledPeerAdmissionConfigured);
            Assert.NotNull(page.OriginalHost.Content); // Actual headless scene, no GUI frame receipt.
        }
        finally
        {
            page.RequestRetirement(); await page.CloseAndDrainAsync();
            await frame.CloseAndDrainAsync(); window.Content = null; window.Close();
        }
        Assert.Null(home.Home.OriginalCloseTask); // A native view does not own shared Home retirement.
    }
}
