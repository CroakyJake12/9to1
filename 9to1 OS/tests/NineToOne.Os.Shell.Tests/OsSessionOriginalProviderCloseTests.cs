using System.Reflection;
using System.Text.Json;
using System.Net.Sockets;
using NineToOne.Os.Shell.Authority;
using Haven.Application;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using NineToOne.Os.Shell;

namespace NineToOne.Os.Shell.Tests;

/// <summary>Actual provider/runtime/OS-close tasks with isolated services and a real temporary lease.
/// No installed publisher, privilege, ready app, native loop or signed child is simulated by these tests.</summary>
public sealed class OsSessionOriginalProviderCloseTests
{
    [Fact]
    public Task Actual_async_only_runtime_is_drained_before_original_lease_retirement() => WithRig(async rig =>
    {
        rig.Service.HoldStop = true;
        var first = rig.Close();
        var again = rig.Home.DisposeAsync().AsTask();
        Assert.Same(first, again);
        await rig.Service.StopEntered.Task.WaitAsync(rig.Deadline.Token);
        Assert.False(first.IsCompleted);
        Assert.True(rig.Lease.IsHeld);
        Assert.False(rig.Service.StopFinallySettled);
        rig.Service.StopRelease.TrySetResult();
        await first.WaitAsync(rig.Deadline.Token);
        Assert.True(rig.Service.StopFinallySettled);
        Assert.False(rig.Lease.IsHeld);
        Assert.Equal(1, rig.Service.StopCalls);
    });

    [Fact]
    public Task Same_original_provider_close_is_published_before_real_stop_callback_reentry() => WithRig(async rig =>
    {
        Task? observed = null;
        rig.Service.HoldStop = true;
        rig.Service.OnStop = () => observed = rig.Home.DisposeAsync().AsTask();
        var original = rig.Close();
        await rig.Service.StopEntered.Task.WaitAsync(rig.Deadline.Token);
        Assert.Same(original, observed);
        Assert.False(original.IsCompleted);
        rig.Service.StopRelease.TrySetResult();
        await original.WaitAsync(rig.Deadline.Token);
        Assert.Equal(1, rig.Service.StopCalls);
    });

    [Fact]
    public Task Original_body_and_actual_provider_stop_errors_survive_independent_close() => WithRig(async rig =>
    {
        var body = new IOException("private-body-detail");
        var stop = new InvalidOperationException("private-stop-detail");
        rig.Service.StopError = stop;
        var original = NineToOne.Os.Shell.Program.RunOriginalBodyAndCloseAsync(
            () => Task.FromException(body), () => new ValueTask(rig.Close()));
        rig.Track(original);
        var combined = await Assert.ThrowsAsync<AggregateException>(() => original);
        rig.Observed(combined);
        Assert.Contains(combined.Flatten().InnerExceptions, error => ReferenceEquals(error, body));
        Assert.Contains(combined.Flatten().InnerExceptions, error => ReferenceEquals(error, stop));
        Assert.False(rig.Lease.IsHeld);
        Assert.True(rig.Service.StopFinallySettled);
        var closeError = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Close());
        Assert.Same(stop, closeError);
        rig.Observed(closeError);
    });

    [Fact]
    public Task Caller_first_original_finally_blocks_real_provider_and_lease_cleanup() => WithRig(async rig =>
    {
        using var caller = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        OperationCanceledException? originalError = null;
        var original = rig.Work.RunAsync(async token =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); return 0; }
            catch (OperationCanceledException error) { originalError = error; throw; }
            finally { held.SetResult(); await release.Task; }
        }, caller.Token);
        rig.Track(original); rig.Releases.Add(() => release.TrySetResult());
        await entered.Task.WaitAsync(rig.Deadline.Token);
        caller.Cancel();
        await held.Task.WaitAsync(rig.Deadline.Token);
        var close = rig.Close();
        Assert.False(original.IsCompleted);
        Assert.False(close.IsCompleted);
        Assert.False(rig.Service.StopEntered.Task.IsCompleted);
        Assert.True(rig.Lease.IsHeld);
        release.TrySetResult();
        var fromOriginal = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original);
        rig.Observed(fromOriginal);
        var fromClose = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => close);
        rig.Observed(fromClose);
        Assert.Same(originalError, fromOriginal);
        Assert.Same(originalError, fromClose);
        Assert.True(rig.Service.StopFinallySettled);
        Assert.False(rig.Lease.IsHeld);
    });

    [Fact]
    public async Task Composition_repair_preserves_same_original_task_error_without_serializing_it()
    {
        var host = CreateHost();
        var original = new IOException("PRIVATE-COMPOSITION-MESSAGE");
        var calls = 0;
        Task<HomeNativeServiceSession> Compose(CancellationToken token)
        { calls++; return Task.FromException<HomeNativeServiceSession>(original); }
        var first = await host.EnsureAsync("isolated-original-host", Compose, ["home.core"]);
        var second = await host.EnsureAsync("isolated-original-host", Compose, ["home.core"]);
        Assert.Equal("HomeBootstrapFailed", first.Code);
        Assert.Equal(HomeNativeOriginalFailureStage.Composition, first.ObserveOriginalFailureStage());
        Assert.Same(original, Assert.Throws<IOException>(first.RethrowOriginalFailureIfPresent));
        Assert.Same(original, Assert.Throws<IOException>(second.RethrowOriginalFailureIfPresent));
        Assert.Equal(1, calls);
        var json = JsonSerializer.Serialize(first);
        Assert.DoesNotContain("PRIVATE-COMPOSITION-MESSAGE", json, StringComparison.Ordinal);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal(new[] { "Code", "Message", "Services", "Snapshot", "State" },
            parsed.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("IOException", OsSessionHome.FixedOriginalExceptionType(original));
    }

    [Fact]
    public async Task Required_service_repair_preserves_real_original_start_cause_and_safe_envelope()
    {
        var original = new IOException("PRIVATE-SERVICE-MESSAGE");
        var service = new ControlledService { StartError = original };
        var runtime = new HomeCoreRuntime([service]);
        var provider = new NullProvider();
        var actors = new Actors();
        var host = CreateHost();
        var failures = new OsOriginalFailures();
        try
        {
            Task<HomeNativeServiceSession> Compose(CancellationToken token) =>
                Task.FromResult(new HomeNativeServiceSession(provider, runtime, actors));
            var first = await host.EnsureAsync("isolated-required-host", Compose, ["fixture.lifecycle"]);
            var second = await host.EnsureAsync("isolated-required-host", Compose, ["fixture.lifecycle"]);
            Assert.Equal(HomeNativeHostState.RequiresHomeRepair, first.State);
            Assert.Null(first.Services);
            Assert.Equal("HomeServiceUnavailable", first.Code);
            Assert.Equal(HomeNativeOriginalFailureStage.RequiredServiceStartup, first.ObserveOriginalFailureStage());
            Assert.Same(original, Assert.Throws<IOException>(first.RethrowOriginalFailureIfPresent));
            Assert.Same(original, Assert.Throws<IOException>(second.RethrowOriginalFailureIfPresent));
            Assert.Equal(1, service.StartCalls);
            Assert.Equal(HomeServiceLifecycleState.Degraded,
                first.Snapshot!.Services.Single(item => item.ServiceId == "fixture.lifecycle").State);
            // Existing service diagnostic is retained; new original fields remain method-only.
            var type = typeof(HomeNativeHostResult);
            Assert.DoesNotContain(type.GetProperties(), property => typeof(Exception).IsAssignableFrom(property.PropertyType));
        }
        catch (Exception error) { failures.Retain(error); }
        try { await runtime.DisposeAsync(); } catch (Exception error) { failures.Retain(error); }
        failures.ThrowIfAny("Required-service fixture body and original runtime close failures retained.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_runtime_async_close_retains_stop_error_after_earlier_explicit_stop(bool foreignCancellation)
    {
        using var foreign = new CancellationTokenSource();
        foreign.Cancel();
        Exception original = foreignCancellation ?
            new OperationCanceledException("PRIVATE-FIRST-STOP", null, foreign.Token) : new IOException("PRIVATE-FIRST-STOP");
        var service = new ControlledService { StopError = original };
        var runtime = new HomeCoreRuntime([service]);
        var failures = new OsOriginalFailures();
        Exception? observed = null;
        try
        {
            await runtime.StartAsync();
            if (foreignCancellation)
            {
                observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.StopAsync(explicitlyRequested: true));
                Assert.Same(original, observed);
            }
            else await runtime.StopAsync(explicitlyRequested: true);
            service.StopError = new InvalidOperationException("later-error-must-not-replace-original");
            var first = runtime.DisposeAsync().AsTask();
            var again = runtime.DisposeAsync().AsTask();
            Assert.Same(first, again);
            observed = await Assert.ThrowsAnyAsync<Exception>(() => first);
            Assert.Same(original, observed);
            Assert.Equal(1, service.StopCalls);
        }
        catch (Exception error) { failures.Retain(error); }
        try { await runtime.DisposeAsync(); }
        catch (Exception error) { if (!ReferenceEquals(error, observed)) failures.Retain(error); }
        failures.ThrowIfAny("First original stop fixture and cleanup failures retained.");
    }

    [Fact]
    public async Task Foreign_original_stop_cancellation_does_not_skip_other_actual_services_during_close()
    {
        using var foreign = new CancellationTokenSource();
        foreign.Cancel();
        var original = new OperationCanceledException("PRIVATE-FOREIGN-CLOSE", null, foreign.Token);
        var failed = new ControlledService { StopError = original };
        var remaining = new ControlledService("fixture.aaa");
        var runtime = new HomeCoreRuntime([failed, remaining]);
        var failures = new OsOriginalFailures();
        Exception? observed = null;
        try
        {
            await runtime.StartAsync();
            var close = runtime.DisposeAsync().AsTask();
            observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => close);
            Assert.Same(original, observed);
            Assert.Equal(1, failed.StopCalls);
            Assert.Equal(1, remaining.StopCalls);
            Assert.True(failed.StopFinallySettled);
            Assert.True(remaining.StopFinallySettled);
        }
        catch (Exception error) { failures.Retain(error); }
        try { await runtime.DisposeAsync(); }
        catch (Exception error) { if (!ReferenceEquals(error, observed)) failures.Retain(error); }
        failures.ThrowIfAny("Foreign original stop and all remaining original service drains retained.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Actual_four_worker_finally_and_cancel_error_are_drained_before_provider(bool widget) => WithRig(async rig =>
    {
        var callbackError = new IOException("PRIVATE-CANCEL-CALLBACK");
        var actors = new HeldActors(callbackError);
        IAsyncDisposable endpoint;
        HomeNativeEndpointLocation location;
        if (widget)
        {
            var registry = new HomeNativeWidgetRegistry(new UnavailableHomeNativeInstalledPeerVerifier(), actors,
                new ResourceAuthorizationService(actors, []));
            var actual = await LinuxNativeWidgetRegistrationServer.StartAsync(rig.Lease, new Paths(rig.Root),
                actors, rig.Runtime, registry, rig.Deadline.Token);
            endpoint = actual; location = actual.Location;
            rig.Endpoints.Add(endpoint);
            Rig.Set(rig.Home, "_widgetRegistrationServer", actual);
        }
        else
        {
            var discovery = new HomeNativeDiscoverySession(new UnavailableHomeNativeInstalledPeerVerifier(), actors, rig.Runtime);
            var actual = await LinuxSessionDiscoveryServer.StartAsync(rig.Lease, new Paths(rig.Root),
                actors, rig.Runtime, discovery, rig.Deadline.Token);
            endpoint = actual; location = actual.Location;
            rig.Endpoints.Add(endpoint);
            Rig.Set(rig.Home, "_discoveryServer", actual);
        }
        actors.Hold = true;
        rig.Releases.Add(() => actors.Release.TrySetResult());
        for (var index = 0; index < 4; index++)
        {
            var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            rig.Resources.Add(client);
            await client.ConnectAsync(new UnixDomainSocketEndPoint(location.SocketPath), rig.Deadline.Token);
        }
        await actors.AllEntered.Task.WaitAsync(rig.Deadline.Token);
        var originalWorkers = (Task[])(endpoint.GetType().GetField("_workers", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(endpoint) ?? throw new InvalidOperationException("Original worker cohort is unavailable."));
        Assert.Equal(4, originalWorkers.Length);
        foreach (var original in originalWorkers) rig.Track(original);
        var close = rig.Close();
        var endpointClose = endpoint.DisposeAsync().AsTask();
        Assert.Same(endpointClose, endpoint.DisposeAsync().AsTask());
        await actors.AllFinallyHeld.Task.WaitAsync(rig.Deadline.Token);
        Assert.False(close.IsCompleted);
        Assert.False(endpointClose.IsCompleted);
        Assert.All(originalWorkers, original => Assert.False(original.IsCompleted));
        Assert.False(rig.Service.StopEntered.Task.IsCompleted);
        Assert.True(rig.Lease.IsHeld);
        actors.Release.TrySetResult();
        var fromEndpoint = await Assert.ThrowsAsync<AggregateException>(() => endpointClose);
        rig.Observed(fromEndpoint);
        var fromOwner = await Assert.ThrowsAsync<AggregateException>(() => close);
        rig.Observed(fromOwner);
        Assert.Same(fromEndpoint, fromOwner);
        Assert.Contains(fromOwner.Flatten().InnerExceptions, error => ReferenceEquals(error, callbackError));
        Assert.Equal(4, actors.FinallySettled);
        Assert.All(originalWorkers, original => Assert.True(original.IsCompletedSuccessfully));
        Assert.True(rig.Service.StopFinallySettled);
        Assert.False(rig.Lease.IsHeld);
        Assert.False(File.Exists(location.SocketPath));
        Assert.False(File.Exists(location.LocatorPath));
    });

    private static HomeNativeServiceHost CreateHost() =>
        (HomeNativeServiceHost)(Activator.CreateInstance(typeof(HomeNativeServiceHost), nonPublic: true) ??
            throw new InvalidOperationException("The maintained original host constructor is unavailable."));

    private static async Task WithRig(Func<Rig, Task> body)
    {
        Rig? rig = null;
        var failures = new OsOriginalFailures();
        try { rig = await Rig.OpenAsync(); await body(rig); }
        catch (Exception error) { failures.Retain(error); }
        if (rig is not null)
        {
            foreach (var release in rig.Releases)
                try { release(); } catch (Exception error) { failures.Retain(error); }
            rig.Service.StopRelease.TrySetResult();
            foreach (var endpoint in rig.Endpoints)
            {
                // Teardown retains SAME original worker tokens and listener even if an earlier assertion
                // or a deliberately broken close omitted cancellation. No new work/authority is issued.
                try
                {
                    var owner = (CancellationTokenSource)(endpoint.GetType().GetField("_shutdown",
                        BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(endpoint) ??
                        throw new InvalidOperationException("Original listener cancellation source unavailable."));
                    if (!owner.IsCancellationRequested) owner.Cancel();
                }
                catch (Exception error) { rig.RetainIfUnobserved(failures, error); }
                try
                {
                    var listener = (Socket)(endpoint.GetType().GetField("_listener",
                        BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(endpoint) ??
                        throw new InvalidOperationException("Original listener unavailable."));
                    listener.Dispose();
                }
                catch (Exception error) { rig.RetainIfUnobserved(failures, error); }
                try { await endpoint.DisposeAsync(); } catch (Exception error) { rig.RetainIfUnobserved(failures, error); }
            }
            Task? originalClose = null;
            try { originalClose = rig.Close(); } catch (Exception error) { failures.Retain(error); }
            foreach (var original in rig.Tasks)
                try { await original; } catch (Exception error) { rig.RetainIfUnobserved(failures, error); }
            if (originalClose is not null)
                try { await originalClose; } catch (Exception error) { rig.RetainIfUnobserved(failures, error); }
            foreach (var resource in rig.Resources)
                try { resource.Dispose(); } catch (Exception error) { failures.Retain(error); }
            try { rig.Deadline.Dispose(); } catch (Exception error) { failures.Retain(error); }
            try { if (Directory.Exists(rig.Root)) Directory.Delete(rig.Root, true); }
            catch (Exception error) { failures.Retain(error); }
        }
        failures.ThrowIfAny("OS provider fixture primary and all original resource drains retained.");
    }

    private sealed class Rig
    {
        internal readonly string Root;
        internal readonly ControlledService Service;
        internal readonly OsSessionHome Home;
        internal readonly HomeNativeSessionLease Lease;
        internal readonly OsSessionOriginalWork Work;
        internal readonly CancellationTokenSource Deadline = new(TimeSpan.FromSeconds(30));
        internal readonly List<Task> Tasks = [];
        internal readonly List<Action> Releases = [];
        internal readonly List<IAsyncDisposable> Endpoints = [];
        internal readonly List<IDisposable> Resources = [];
        internal readonly HomeCoreRuntime Runtime;
        private readonly List<Exception> _observed = [];
        private Task? _close;
        private Rig(string root, ControlledService service, OsSessionHome home, HomeNativeSessionLease lease,
            OsSessionOriginalWork work, HomeCoreRuntime runtime)
        { Root = root; Service = service; Home = home; Lease = lease; Work = work; Runtime = runtime; }
        internal static async Task<Rig> OpenAsync()
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This owning lease cohort requires Linux.");
            var root = Path.Combine(Path.GetTempPath(), "oc-" + Guid.NewGuid().ToString("N")[..10]);
            ServiceProvider? provider = null;
            HomeNativeSessionLease? lease = null;
            OsSessionHome? home = null;
            var failures = new OsOriginalFailures();
            try
            {
                var service = new ControlledService();
                var services = new ServiceCollection();
                services.AddSingleton<IHomeCoreService>(service);
                services.AddSingleton<IHomeCoreService>(new ReadyService("home.state"));
                services.AddSingleton<IHomeCoreService>(new ReadyService("apps.installed"));
                services.AddSingleton<HomeCoreRuntime>();
                provider = services.BuildServiceProvider();
                var runtime = provider.GetRequiredService<HomeCoreRuntime>();
                await runtime.StartAsync();
                lease = await HomeNativeSessionLease.TryAcquireAsync(new Actors(), new Paths(root)) ??
                    throw new InvalidOperationException("Actual isolated lease acquisition refused.");
                // This exercises actual disposal only: no UI constructor, profile issuer or Ready fallback.
                home = new OsSessionHome(null!);
                Set(home, "_services", provider); Set(home, "_lease", lease);
                var work = (OsSessionOriginalWork)(typeof(OsSessionHome).GetField("_originalWork",
                    BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(home) ??
                    throw new InvalidOperationException("Original work owner is unavailable."));
                return new(root, service, home, lease, work, runtime);
            }
            catch (Exception error) { failures.Retain(error); }
            if (home is not null)
                try { await home.DisposeAsync(); } catch (Exception error) { failures.Retain(error); }
            if (provider is not null)
                try { await provider.DisposeAsync(); } catch (Exception error) { failures.Retain(error); }
            try { lease?.Dispose(); } catch (Exception error) { failures.Retain(error); }
            try { if (Directory.Exists(root)) Directory.Delete(root, true); }
            catch (Exception error) { failures.Retain(error); }
            failures.ThrowIfAny("Original provider fixture acquisition and cleanup failures retained.");
            throw new InvalidOperationException("Unreachable original acquisition failure.");
        }
        internal static void Set(object instance, string name, object value) =>
            (instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new InvalidOperationException("Maintained original owning field is unavailable.")).SetValue(instance, value);
        internal Task Close() => _close ??= Home.DisposeAsync().AsTask();
        internal void Track(Task original) => Tasks.Add(original);
        internal void Observed(Exception error)
        { _observed.Add(error); if (error is AggregateException combined) _observed.AddRange(combined.Flatten().InnerExceptions); }
        internal void RetainIfUnobserved(OsOriginalFailures failures, Exception error)
        {
            if (_observed.Any(observed => ReferenceEquals(observed, error))) return;
            failures.Retain(error);
        }
    }

    private sealed class ControlledService(string id = "fixture.lifecycle") : IHomeCoreService
    {
        public HomeServiceDescriptor Descriptor => new(id, HomeCoreServiceCatalog.CurrentContractVersion,
            HomeServiceLifecycleState.Stopped, false, null);
        public IReadOnlyList<string> Dependencies => ["home.core"];
        internal Exception? StartError;
        internal Exception? StopError;
        internal bool HoldStop;
        internal Action? OnStop;
        internal int StartCalls, StopCalls;
        internal bool StopFinallySettled;
        internal readonly TaskCompletionSource StopEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource StopRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task StartAsync(CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); StartCalls++; return StartError is null ? Task.CompletedTask : Task.FromException(StartError); }
        public async Task StopAsync(CancellationToken ct = default)
        {
            StopCalls++;
            try
            {
                OnStop?.Invoke();
                StopEntered.TrySetResult();
                if (HoldStop) await StopRelease.Task;
                if (StopError is { } original) throw original;
                ct.ThrowIfCancellationRequested();
            }
            finally { StopFinallySettled = true; }
        }
    }
    private sealed class ReadyService(string id) : IHomeCoreService
    {
        public HomeServiceDescriptor Descriptor => new(id, HomeCoreServiceCatalog.CurrentContractVersion,
            HomeServiceLifecycleState.Stopped, false, null);
        public IReadOnlyList<string> Dependencies => ["home.core"];
        public Task StartAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    }
    private sealed class HeldActors(Exception callbackError) : IAuthenticatedResourceActorSource
    {
        internal volatile bool Hold;
        internal readonly TaskCompletionSource AllEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource AllFinallyHeld = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _entered, _held;
        internal int FinallySettled;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) =>
            Hold ? new(HoldOriginalAsync(ct)) : ValueTask.FromResult<AuthenticatedResourceActor?>(new("actor", "profile", null, null, "session"));
        private async Task<AuthenticatedResourceActor?> HoldOriginalAsync(CancellationToken ct)
        {
            using var registered = ct.Register(() => throw callbackError);
            if (Interlocked.Increment(ref _entered) == 4) AllEntered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); return null; }
            finally
            {
                if (Interlocked.Increment(ref _held) == 4) AllFinallyHeld.TrySetResult();
                await Release.Task;
                Interlocked.Increment(ref FinallySettled);
            }
        }
    }
    private sealed class NullProvider : IServiceProvider { public object? GetService(Type type) => null; }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult<AuthenticatedResourceActor?>(new("actor", "profile", null, null, "session")); }
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "database");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy");
    }
}
