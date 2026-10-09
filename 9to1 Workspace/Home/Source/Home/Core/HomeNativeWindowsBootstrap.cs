using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Designated Home process bootstrap. The original platform composition supplies its
/// real provider, current actor and trusted paths. This owns the genuine lease acquisition,
/// accepting host and Core shutdown; it does not install Home or create replacement identity,
/// stores, publishers, brokers, models or permission policy.</summary>
public sealed partial class HomeNativeWindowsBootstrap : IAsyncDisposable
{
    private readonly HomeNativeServiceSession _original;
    private readonly IAppPaths _paths;
    private readonly HomeNativeWindowsEndpoint _endpoint;
    private readonly CancellationToken _originalLifetime;
    private readonly CancellationTokenSource _startupLifetime;
    private readonly object _sync = new();
    private readonly Task _start;
    private Task<HomeNativeSessionLease?>? _originalLeaseAcquisition;
    private HomeNativeSessionLease? _lease;
    private HomeNativeWindowsHostOwner? _owner;
    private Task? _close;
    private bool _closing;
    private HomeNativeWindowsBootstrap(HomeNativeServiceSession original, IAppPaths paths,
        HomeNativeWindowsEndpoint endpoint, CancellationToken originalLifetime)
    {
        _original = original; _paths = paths; _endpoint = endpoint; _originalLifetime = originalLifetime;
        _startupLifetime = CancellationTokenSource.CreateLinkedTokenSource(originalLifetime);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _start = StartOriginalAsync(gate.Task);
        gate.SetResult();
    }
    public static HomeNativeWindowsBootstrap Start(HomeNativeServiceSession originalCanonicalHome,
        IAppPaths originalTrustedHomePaths, HomeNativeWindowsEndpoint configuredEndpoint,
        CancellationToken originalProcessLifetime)
    {
        ArgumentNullException.ThrowIfNull(originalCanonicalHome);
        ArgumentNullException.ThrowIfNull(originalTrustedHomePaths);
        ArgumentNullException.ThrowIfNull(configuredEndpoint);
        originalProcessLifetime.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || !originalProcessLifetime.CanBeCanceled)
            throw new UnauthorizedAccessException("Windows and the genuine owning Home process lifetime are required.");
        return new(originalCanonicalHome, originalTrustedHomePaths, configuredEndpoint.Capture(), originalProcessLifetime);
    }
    public Task OriginalStartTask => _start;
    internal HomeNativeWindowsListeningLease CaptureOriginalListeningLease(HomeNativeServiceSession sameSession)
    {
        lock (_sync)
        {
            if (_closing || _close is not null || !_start.IsCompletedSuccessfully || _originalLifetime.IsCancellationRequested ||
                !ReferenceEquals(_original.Services, sameSession.Services) || !ReferenceEquals(_original.Runtime, sameSession.Runtime) ||
                !ReferenceEquals(_original.Actors, sameSession.Actors) || _owner is null || _lease is null || !_lease.IsHeld)
                throw new UnauthorizedAccessException("The SAME actual Home bootstrap/listening owner and held process lease are required.");
            return _owner.CaptureOriginalListeningLease(_original, _lease);
        }
    }
    private async Task StartOriginalAsync(Task gate)
    {
        await gate.ConfigureAwait(false);
        _originalLeaseAcquisition = HomeNativeSessionLease.TryAcquireAsync(_original.Actors, _paths,
            _startupLifetime.Token).AsTask();
        _lease = await _originalLeaseAcquisition.ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original Home lease is unavailable; never replace another live Home host.");
        HomeNativeWindowsHostOwner owner;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            _startupLifetime.Token.ThrowIfCancellationRequested();
            owner = _owner = HomeNativeWindowsHostOwner.AttachOriginal(_original, _lease, _endpoint, _originalLifetime);
        }
        await owner.OriginalStartTask.ConfigureAwait(false);
        _startupLifetime.Token.ThrowIfCancellationRequested();
        lock (_sync) ObjectDisposedException.ThrowIf(_closing, this);
    }
    /// <summary>Wire this SAME task to HomeHostOriginalLifetime's explicit Core-close delegate.
    /// Do not register the long-running accepting task as finite app work; normal window close keeps Home alive.</summary>
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
        Task? originalOwnerClose = null;
        lock (_sync)
            try { if (_owner is not null) originalOwnerClose = _owner.CloseAndDrainAsync(); }
            catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { _startupLifetime.Cancel(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { await _start.ConfigureAwait(false); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { if (originalOwnerClose is not null) await originalOwnerClose.ConfigureAwait(false); }
        catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { if (_originalLeaseAcquisition is not null) await _originalLeaseAcquisition.ConfigureAwait(false); }
        catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { await _original.Runtime.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { _lease?.Dispose(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { _startupLifetime.Dispose(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        HomeUnixCoreTransport.Throw(failures);
    }
}
