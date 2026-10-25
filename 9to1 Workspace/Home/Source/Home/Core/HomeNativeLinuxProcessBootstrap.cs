using System.Runtime.ExceptionServices;
using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Linux owner of the SAME process-local Home provider, runtime and OS-profile lease.
/// An explicitly configured original installed-peer verifier enables the SAME canonical
/// Unix accepting host; the default unavailable verifier keeps startup process-local.
/// This never installs Home or issues a Root/publisher listening observation.</summary>
internal sealed class HomeNativeLinuxProcessBootstrap : IAsyncDisposable
{
    private static readonly string[] RequiredServices = ["home.core", "home.state", "permissions.trust"];
    private readonly HomeNativeServiceSession _original;
    private readonly HomeNativeCoreApiSessions _sessions;
    private readonly IAppPaths _paths;
    private readonly IHomeNativeInstalledPeerOriginalActorVerifier? _installedVerifier;
    private readonly CancellationTokenSource _lifetime;
    private readonly object _sync = new();
    private readonly Task _start;
    private Task<HomeNativeSessionLease?>? _leaseAcquisition;
    private HomeNativeSessionLease? _lease;
    private HomeNativeLinuxCoreHost? _host;
    private Task<HomeCoreStateSnapshot>? _coreStart;
    private Task? _cleanup, _close;
    private bool _closing;

    private HomeNativeLinuxProcessBootstrap(HomeNativeServiceSession original,
        HomeNativeCoreApiSessions sessions, IAppPaths paths,
        IHomeNativeInstalledPeerOriginalActorVerifier? installedVerifier, CancellationToken processLifetime)
    {
        _original = original; _sessions = sessions; _paths = paths;
        _installedVerifier = installedVerifier;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(processLifetime);
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _start = StartOriginalAsync(begin.Task);
        begin.SetResult();
    }

    internal static HomeNativeLinuxProcessBootstrap Start(HomeNativeServiceSession original,
        IAppPaths trustedPaths, CancellationToken processLifetime)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(trustedPaths);
        processLifetime.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsLinux() || !processLifetime.CanBeCanceled)
            throw new UnauthorizedAccessException("Linux and the genuine owning Home process lifetime are required.");
        var provider = original.Services ?? throw new ArgumentException("The canonical Home provider is absent.");
        var sessions = provider.GetService(typeof(HomeNativeCoreApiSessions)) as HomeNativeCoreApiSessions;
        if (original.Runtime is null || original.Actors is null || sessions is null ||
            !ReferenceEquals(provider.GetService(typeof(HomeCoreRuntime)), original.Runtime) ||
            !ReferenceEquals(provider.GetService(typeof(IAuthenticatedResourceActorSource)), original.Actors) ||
            !ReferenceEquals(provider.GetService(typeof(IHomeCoreAuthorization)), sessions))
            throw new UnauthorizedAccessException("Retain the SAME canonical Home runtime, OS-profile source and private session issuer.");
        var installed = provider.GetService(typeof(IHomeNativeInstalledPeerVerifier)) as IHomeNativeInstalledPeerOriginalActorVerifier;
        if (installed is not null && !sessions.HasOriginalLinuxHostComposition(original.Actors, installed))
            throw new UnauthorizedAccessException("The configured Linux installed-peer verifier must belong to the SAME canonical session issuer and actor source.");
        return new(original, sessions, trustedPaths, installed, processLifetime);
    }

    internal Task OriginalStartTask => _start;

    private async Task StartOriginalAsync(Task begin)
    {
        await begin.ConfigureAwait(false);
        try
        {
            var token = _lifetime.Token;
            var actor = await _original.Actors.GetCurrentAsync(token).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The original OS-local Home profile is unavailable.");
            if (actor.AccountId is not null || actor.OrganisationId is not null || string.IsNullOrWhiteSpace(actor.ProfileId))
                throw new UnauthorizedAccessException("Linux process-local Home requires its actual local OS profile.");
            _leaseAcquisition = HomeNativeSessionLease.TryAcquireAsync(_original.Actors, _paths, token).AsTask();
            _lease = await _leaseAcquisition.ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The original Home lease is unavailable; another live host must not be replaced.");
            if (_lease.ProfileId != actor.ProfileId || actor != await _original.Actors.GetCurrentAsync(token).ConfigureAwait(false))
                throw new UnauthorizedAccessException("Home profile changed during process lease acquisition.");
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_closing, this);
                token.ThrowIfCancellationRequested();
                _coreStart = _original.Runtime.StartAsync(token);
            }
            var snapshot = await _coreStart.ConfigureAwait(false);
            if (RequiredServices.Any(id => !snapshot.Services.Any(service => service.ServiceId == id &&
                service.IsAvailable && service.State == HomeServiceLifecycleState.Ready &&
                service.ContractVersion == HomeCoreServiceCatalog.CurrentContractVersion)))
            {
                var cause = _original.Runtime.CaptureOriginalStartFailures(RequiredServices);
                if (cause is not null) ExceptionDispatchInfo.Capture(cause).Throw();
                throw new InvalidOperationException("The actual Home Core, durable state and permission services are not ready.");
            }
            if (actor != await _original.Actors.GetCurrentAsync(token).ConfigureAwait(false) || !_lease.IsHeld)
                throw new UnauthorizedAccessException("The original Home actor or process lease retired during startup.");
            if (_installedVerifier is not null)
            {
                // Explicit production configuration, never a fallback admission policy.
                // Discovery is only an adapter over the SAME verifier/actor/runtime graph.
                var endpoint = await HomeNativeEndpointLocations.CreateForLeasedHostAsync(_lease, _paths,
                    _original.Actors, token).ConfigureAwait(false);
                HomeNativeLinuxCoreHost host;
                lock (_sync)
                {
                    ObjectDisposedException.ThrowIf(_closing, this);
                    token.ThrowIfCancellationRequested();
                    host = _host = HomeNativeLinuxCoreHost.Start(new(_installedVerifier, _original.Actors, _original.Runtime),
                        _sessions, _lease, endpoint, _lifetime.Token);
                }
                await host.OriginalListeningTask.ConfigureAwait(false);
                if (actor != await _original.Actors.GetCurrentAsync(token).ConfigureAwait(false) || !_lease.IsHeld)
                    throw new UnauthorizedAccessException("The original Home actor or process lease retired during Unix listener startup.");
            }
            token.ThrowIfCancellationRequested();
            lock (_sync) ObjectDisposedException.ThrowIf(_closing, this);
        }
        catch (Exception primary)
        {
            // A failed start must not strand the process lease behind an error screen.
            // Cleanup never joins _start (which owns this path), so close can safely join both.
            List<Exception> failures = [primary];
            try { _lifetime.Cancel(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
            try { await CleanupOriginalAsync().ConfigureAwait(false); }
            catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
            HomeUnixCoreTransport.Throw(failures);
        }
    }

    public Task CloseAndDrainAsync()
    {
        Task result; TaskCompletionSource begin;
        lock (_sync)
        {
            if (_close is not null) return _close;
            _closing = true;
            begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            result = _close = CloseOriginalAsync(begin.Task);
        }
        begin.SetResult(); // Publish the SAME close before any cancellation callback can reenter.
        return result;
    }

    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private async Task CloseOriginalAsync(Task begin)
    {
        await begin.ConfigureAwait(false);
        List<Exception> failures = [];
        try { _lifetime.Cancel(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { await _start.ConfigureAwait(false); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { await CleanupOriginalAsync().ConfigureAwait(false); }
        catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { _lifetime.Dispose(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        HomeUnixCoreTransport.Throw(failures);
    }

    private Task CleanupOriginalAsync()
    {
        Task result; TaskCompletionSource begin;
        lock (_sync)
        {
            if (_cleanup is not null) return _cleanup;
            begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            result = _cleanup = CleanupCoreAsync(begin.Task);
        }
        begin.SetResult();
        return result;
    }

    private async Task CleanupCoreAsync(Task begin)
    {
        await begin.ConfigureAwait(false);
        List<Exception> failures = [];
        // Acquire/start originals have already settled before this method is admitted.
        // Accepted socket/session work settles before the canonical sessions/runtime,
        // and every actual service stop settles before the OS lease is released.
        try { if (_host is not null) await _host.CloseAndDrainAsync().ConfigureAwait(false); }
        catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { await _sessions.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { await _original.Runtime.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { _lease?.Dispose(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        HomeUnixCoreTransport.Throw(failures);
    }
}
