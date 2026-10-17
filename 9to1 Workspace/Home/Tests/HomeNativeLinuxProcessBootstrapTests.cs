using System.Runtime.ExceptionServices;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>Real Linux OS profile, durable state and exclusive process lease. Injected service
/// gates exercise failure/drain ordering only; these tests do not establish installed-peer IPC.</summary>
public sealed class HomeNativeLinuxProcessBootstrapTests
{
    private sealed class LinuxFactAttribute : FactAttribute
    { public LinuxFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Requires the actual Linux kernel."; } }

    [LinuxFact]
    public Task Canonical_composition_starts_same_runtime_and_holds_lease_until_close() => WithRigAsync(async rig =>
    {
        rig.Compose();
        var home = rig.Composition!;
        var start = rig.Start();
        Assert.Same(start, home.StartOriginalAsync());
        await start.WaitAsync(rig.Token);
        Assert.Same(home.Runtime, home.Services.GetService(typeof(HomeCoreRuntime)));
        Assert.Same(home.Profiles, home.Services.GetService(typeof(IAuthenticatedResourceActorSource)));
        Assert.Same(home.Sessions, home.Services.GetService(typeof(IHomeCoreAuthorization)));
        foreach (var id in new[] { "home.core", "home.state", "permissions.trust", "productivity.engine" })
        {
            var service = Assert.Single(home.Runtime.Current.Services, row => row.ServiceId == id);
            Assert.True(service.IsAvailable);
            Assert.Equal(HomeServiceLifecycleState.Ready, service.State);
        }
        Assert.False(home.InstalledPeerAdmissionConfigured);
        Assert.False(Assert.Single(home.Runtime.Current.Services, row => row.ServiceId == "packages").IsAvailable);
        var actor = await home.Profiles.GetCurrentAsync(rig.Token);
        Assert.NotNull(actor);
        Assert.Null(actor.AccountId);
        Assert.Null(actor.OrganisationId);
        using (var competing = await rig.AcquireLeaseAsync()) Assert.Null(competing);
        var denied = await home.Api.GetServicesAsync(new("copied", "test", "public-record"), rig.Token);
        Assert.False(denied.Succeeded);
        var close = rig.Close();
        Assert.Same(close, home.CloseAndDrainAsync());
        await close.WaitAsync(rig.Token);
        Assert.False(Assert.Single(home.Runtime.Current.Services, row => row.ServiceId == "home.core").IsAvailable);
        using var released = await rig.AcquireLeaseAsync();
        Assert.NotNull(released);
        Assert.True((await home.StateStore.ReadAsync(rig.Token)).IsSuccess);
    });

    [LinuxFact]
    public Task Existing_process_lease_is_not_replaced_or_released_by_failed_start() => WithRigAsync(async rig =>
    {
        rig.Compose();
        using var existing = await rig.AcquireLeaseAsync();
        Assert.NotNull(existing);
        var failure = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Start().WaitAsync(rig.Token));
        rig.Observe(failure);
        Assert.True(existing.IsHeld);
        using (var competing = await rig.AcquireLeaseAsync()) Assert.Null(competing);
        var closeFailure = await Record.ExceptionAsync(() => rig.Close().WaitAsync(rig.Token));
        Assert.Same(failure, closeFailure);
        Assert.True(existing.IsHeld);
        existing.Dispose();
        using var acquired = await rig.AcquireLeaseAsync();
        Assert.NotNull(acquired);
    });

    [LinuxFact]
    public Task Held_start_cancellation_publishes_same_close_and_joins_finally_before_lease_release() => WithRigAsync(async rig =>
    {
        var held = rig.Probe = new ProbeService { HoldStart = true };
        rig.ComposeBootstrap(held);
        Task? reentered = null;
        held.OnCancellation = () => reentered = rig.Bootstrap!.CloseAndDrainAsync();
        var start = rig.Start();
        await held.StartEntered.Task.WaitAsync(rig.Token);
        var close = rig.Close();
        await held.StartFinally.Task.WaitAsync(rig.Token);
        Assert.Same(close, reentered);
        Assert.Same(close, rig.Bootstrap!.CloseAndDrainAsync());
        Assert.False(close.IsCompleted);
        Assert.False(start.IsCompleted);
        using (var competing = await rig.AcquireLeaseAsync()) Assert.Null(competing);
        held.ReleaseStart.TrySetResult();
        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start.WaitAsync(rig.Token));
        rig.Observe(failure);
        var closeFailure = await Record.ExceptionAsync(() => close.WaitAsync(rig.Token));
        Assert.Same(failure, closeFailure);
        using var acquired = await rig.AcquireLeaseAsync();
        Assert.NotNull(acquired);
    });

    [LinuxFact]
    public Task Held_service_stop_keeps_process_lease_until_actual_stop_settles() => WithRigAsync(async rig =>
    {
        var held = rig.Probe = new ProbeService { HoldStop = true };
        rig.ComposeBootstrap(held);
        await rig.Start().WaitAsync(rig.Token);
        var close = rig.Close();
        await held.StopEntered.Task.WaitAsync(rig.Token);
        Assert.Same(close, rig.Bootstrap!.CloseAndDrainAsync());
        Assert.False(close.IsCompleted);
        using (var competing = await rig.AcquireLeaseAsync()) Assert.Null(competing);
        held.ReleaseStop.TrySetResult();
        await close.WaitAsync(rig.Token);
        using var acquired = await rig.AcquireLeaseAsync();
        Assert.NotNull(acquired);
    });

    [LinuxFact]
    public Task Required_service_failure_retains_original_cause_and_releases_lease_without_waiting_for_window_close() => WithRigAsync(async rig =>
    {
        var cause = new IOException("actual-required-service-failure");
        var failing = rig.Probe = new ProbeService { ServiceId = "permissions.trust", StartFailure = cause };
        rig.ComposeBootstrap(failing, replacePermissions: true);
        var failure = await Assert.ThrowsAsync<IOException>(() => rig.Start().WaitAsync(rig.Token));
        Assert.Same(cause, failure);
        rig.Observe(failure);
        Assert.True(failing.StopEntered.Task.IsCompletedSuccessfully);
        using var acquired = await rig.AcquireLeaseAsync();
        Assert.NotNull(acquired); // Cleanup completed before startup reported failure.
        Assert.Same(failure, await Record.ExceptionAsync(() => rig.Close().WaitAsync(rig.Token)));
    });

    [LinuxFact]
    public Task Linux_process_owner_cannot_issue_a_windows_root_listening_observation() => WithRigAsync(async rig =>
    {
        rig.Compose();
        await rig.Start().WaitAsync(rig.Token);
        var home = rig.Composition!;
        var failure = await Record.ExceptionAsync(() => home.ObserveOriginalRootListeningWithinSourceAsync(
            body => body(), _ => { }, rig.Token));
        Assert.NotNull(failure);
        Assert.Contains(Flatten(failure!), error => error is UnauthorizedAccessException);
        rig.Observe(failure!);
        var closeFailure = await Record.ExceptionAsync(() => rig.Close().WaitAsync(rig.Token));
        Assert.NotNull(closeFailure); // The refused original read remains visible during retirement.
        rig.Observe(closeFailure!);
        using var acquired = await rig.AcquireLeaseAsync();
        Assert.NotNull(acquired);
    });

    private static async Task WithRigAsync(Func<Rig, Task> body)
    {
        var rig = new Rig();
        Exception? primary = null;
        try { await body(rig); }
        catch (Exception error) { primary = error; }
        finally
        {
            try { await rig.DisposeAsync(); }
            catch (Exception cleanup) { primary = primary is null ? cleanup : new AggregateException(primary, cleanup); }
        }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "home-linux-process-" + Guid.NewGuid().ToString("N"));
        private readonly CancellationTokenSource _bound = new(TimeSpan.FromSeconds(30));
        private readonly CancellationTokenSource _process = new();
        private readonly HashSet<Exception> _observed = new(ReferenceEqualityComparer.Instance);
        private Task? _start, _close;
        private IAuthenticatedResourceActorSource? _actors;
        internal HomeNativeWindowsComposition? Composition;
        internal HomeNativeLinuxProcessBootstrap? Bootstrap;
        internal ProbeService? Probe;
        internal CancellationToken Token => _bound.Token;
        private Paths Paths => new(_root);
        internal void Compose()
        {
            Composition = new(new FileHomeCoreStateStore(Path.Combine(_root, "Home", "state.json")),
                new OperatingSystemPrincipalSource(), Paths, new("linux.process.test"));
            _actors = Composition.Profiles;
        }
        internal void ComposeBootstrap(ProbeService probe, bool replacePermissions = false)
        {
            var state = new FileHomeCoreStateStore(Path.Combine(_root, "Home", "state.json"));
            var profiles = new HomeLocalProfileIdentity(state, new OperatingSystemPrincipalSource());
            var permissions = new HomePermissionTrustService(state, (_, _) => null);
            HomeCoreApi? api = null;
            var sessions = new HomeNativeCoreApiSessions(permissions, profiles,
                new UnavailableHomeNativeInstalledPeerVerifier(), () => api!);
            List<IHomeCoreService> services = [new HomeCoreStateService(state), probe];
            if (!replacePermissions) services.Add(new HomePermissionsCoreService(permissions, profiles));
            var runtime = new HomeCoreRuntime(services, sessions);
            api = new(runtime, sessions, profiles);
            var provider = new OriginalProvider(new Dictionary<Type, object>
            {
                [typeof(HomeCoreRuntime)] = runtime,
                [typeof(IAuthenticatedResourceActorSource)] = profiles,
                [typeof(HomeNativeCoreApiSessions)] = sessions,
                [typeof(IHomeCoreAuthorization)] = sessions
            });
            _actors = profiles;
            Bootstrap = HomeNativeLinuxProcessBootstrap.Start(new(provider, runtime, profiles), Paths, _process.Token);
        }
        internal Task Start() => _start ??= Composition?.StartOriginalAsync() ?? Bootstrap!.OriginalStartTask;
        internal Task Close() => _close ??= Composition?.CloseAndDrainAsync() ?? Bootstrap!.CloseAndDrainAsync();
        internal ValueTask<HomeNativeSessionLease?> AcquireLeaseAsync() =>
            HomeNativeSessionLease.TryAcquireAsync(_actors!, Paths, Token);
        internal void Observe(Exception error)
        { foreach (var cause in Flatten(error)) _observed.Add(cause); }
        public async ValueTask DisposeAsync()
        {
            Probe?.ReleaseStart.TrySetResult(); Probe?.ReleaseStop.TrySetResult();
            List<Exception> failures = [];
            if (Composition is not null || Bootstrap is not null)
            {
                var close = Close();
                foreach (var original in new[] { _start, close })
                {
                    if (original is null) continue;
                    try { await original.WaitAsync(TimeSpan.FromSeconds(10)); }
                    catch (Exception error)
                    { foreach (var cause in Flatten(error)) if (!_observed.Contains(cause)) HomeUnixCoreTransport.Add(failures, cause); }
                }
            }
            _process.Dispose(); _bound.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            HomeUnixCoreTransport.Throw(failures);
        }
    }

    private sealed class ProbeService : IHomeCoreService
    {
        internal string ServiceId = "test.lifecycle";
        internal bool HoldStart, HoldStop;
        internal Exception? StartFailure;
        internal Action? OnCancellation;
        internal readonly TaskCompletionSource StartEntered = Signal(), StartFinally = Signal(), ReleaseStart = Signal();
        internal readonly TaskCompletionSource StopEntered = Signal(), ReleaseStop = Signal();
        public HomeServiceDescriptor Descriptor => new(ServiceId, HomeCoreServiceCatalog.CurrentContractVersion,
            HomeServiceLifecycleState.Stopped, false, null);
        public IReadOnlyList<string> Dependencies => ["home.state"];
        public async Task StartAsync(CancellationToken token)
        {
            if (StartFailure is not null) ExceptionDispatchInfo.Capture(StartFailure).Throw();
            using var registration = token.Register(() => OnCancellation?.Invoke());
            StartEntered.TrySetResult();
            if (!HoldStart) return;
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { StartFinally.TrySetResult(); await ReleaseStart.Task; }
        }
        public async Task StopAsync(CancellationToken token)
        { StopEntered.TrySetResult(); if (HoldStop) await ReleaseStop.Task; }
    }
    private sealed class OriginalProvider(Dictionary<Type, object> values) : IServiceProvider
    { public object? GetService(Type type) => values.GetValueOrDefault(type); }
    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy");
    }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static IEnumerable<Exception> Flatten(Exception error) => error is AggregateException group
        ? group.InnerExceptions.SelectMany(Flatten) : [error];
}
