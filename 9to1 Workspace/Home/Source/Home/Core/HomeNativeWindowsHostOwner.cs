using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Home's explicit process owner over its existing canonical provider. Apps borrow the
/// issued startup connection; they never close this owner. No provider, state store, actor,
/// installed verifier, broker, device registry or lease is created here.</summary>
public sealed class HomeNativeWindowsHostOwner : IAsyncDisposable
{
    private readonly HomeNativeServiceSession _original;
    private readonly HomeNativeCoreApiSessions _sessions;
    private readonly HomeNativeSessionLease _lease;
    private readonly HomeNativeWindowsEndpoint _endpoint;
    private readonly CancellationToken _originalLifetime;
    private readonly CancellationTokenSource _lifetime;
    private readonly object _sync = new();
    private readonly Task _start;
    private Task<HomeCoreStateSnapshot>? _originalCoreStart;
    private HomeNativeWindowsCoreHost? _host;
    private Task? _close;
    private bool _closing;

    private HomeNativeWindowsHostOwner(HomeNativeServiceSession original, HomeNativeCoreApiSessions sessions,
        HomeNativeSessionLease lease, HomeNativeWindowsEndpoint endpoint, CancellationToken lifetime)
    {
        _original = original; _sessions = sessions; _lease = lease; _endpoint = endpoint;
        _originalLifetime = lifetime;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _start = StartOriginalAsync(gate.Task);
        gate.SetResult();
    }
    public static HomeNativeWindowsHostOwner AttachOriginal(HomeNativeServiceSession originalCanonicalHome,
        HomeNativeSessionLease originalHeldLease, HomeNativeWindowsEndpoint configuredEndpoint,
        CancellationToken originalProcessLifetime)
    {
        ArgumentNullException.ThrowIfNull(originalCanonicalHome);
        ArgumentNullException.ThrowIfNull(originalHeldLease);
        ArgumentNullException.ThrowIfNull(configuredEndpoint);
        originalProcessLifetime.ThrowIfCancellationRequested();
        var provider = originalCanonicalHome.Services ?? throw new ArgumentException("Canonical provider is absent.");
        if (!OperatingSystem.IsWindows() || !originalProcessLifetime.CanBeCanceled || !originalHeldLease.IsHeld ||
            originalCanonicalHome.Runtime is null || originalCanonicalHome.Actors is null)
            throw new UnauthorizedAccessException("Actual Windows Home process composition, lifetime and held lease are required.");
        var sessions = provider.GetService(typeof(HomeNativeCoreApiSessions)) as HomeNativeCoreApiSessions;
        if (sessions is null ||
            !ReferenceEquals(provider.GetService(typeof(IHomeCoreAuthorization)), sessions) ||
            !ReferenceEquals(provider.GetService(typeof(HomeCoreRuntime)), originalCanonicalHome.Runtime) ||
            !ReferenceEquals(provider.GetService(typeof(IAuthenticatedResourceActorSource)), originalCanonicalHome.Actors))
            throw new UnauthorizedAccessException("Home must supply the SAME registered canonical runtime, actor source and private session issuer.");
        return new(originalCanonicalHome, sessions, originalHeldLease, configuredEndpoint.Capture(), originalProcessLifetime);
    }
    public Task OriginalStartTask => _start;
    private async Task StartOriginalAsync(Task gate)
    {
        await gate.ConfigureAwait(false);
        var token = _lifetime.Token;
        var actor = await _original.Actors.GetCurrentAsync(token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Home's original current profile is unavailable.");
        _originalCoreStart = _original.Runtime.StartAsync(token);
        var snapshot = await _originalCoreStart.ConfigureAwait(false);
        if (!new[] { "home.core", "home.state" }.All(id => snapshot.Services.Any(service =>
            service.ServiceId == id && service.IsAvailable && service.State == HomeServiceLifecycleState.Ready &&
            service.ContractVersion == HomeCoreServiceCatalog.CurrentContractVersion)))
            throw new InvalidOperationException("Actual canonical Home Core and durable state must be ready before accepting clients.");
        if (actor != await _original.Actors.GetCurrentAsync(token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Home profile retired during original startup.");
        token.ThrowIfCancellationRequested();
        HomeNativeWindowsCoreHost originalHost;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            // Original endpoint construction starts only after SAME real Core start and current actor checks.
            originalHost = _host = HomeNativeWindowsCoreHost.Start(_sessions, _lease, _endpoint, _originalLifetime);
        }
        await originalHost.OriginalListeningTask.ConfigureAwait(false);
        if (actor != await _original.Actors.GetCurrentAsync(token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Home profile retired during physical listener acquisition.");
        token.ThrowIfCancellationRequested();
        lock (_sync) ObjectDisposedException.ThrowIf(_closing, this);
    }
    /// <summary>The explicit Home shutdown callback calls this BEFORE desktop exit.
    /// Ordinary window close remains independent and keeps Core alive.</summary>
    public Task CloseAndDrainAsync()
    {
        lock (_sync)
        {
            if (_close is not null) return _close;
            _closing = true;
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseOriginalAsync(gate.Task);
            gate.SetResult();
            return _close;
        }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private async Task CloseOriginalAsync(Task gate)
    {
        await gate.ConfigureAwait(false);
        List<Exception> failures = [];
        Task? originalHostClose = null;
        lock (_sync)
            try { if (_host is not null) originalHostClose = _host.CloseAndDrainAsync(); }
            catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        // Host close seals/cancels its original readers before the owning startup token is canceled.
        try { _lifetime.Cancel(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { await _start.ConfigureAwait(false); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { if (originalHostClose is not null) await originalHostClose.ConfigureAwait(false); }
        catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { if (_originalCoreStart is not null) await _originalCoreStart.ConfigureAwait(false); }
        catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { await _sessions.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { await _original.Runtime.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { _lifetime.Dispose(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        // The original platform retains and retires its lease and provider AFTER this complete drain.
        HomeUnixCoreTransport.Throw(failures);
    }
}
