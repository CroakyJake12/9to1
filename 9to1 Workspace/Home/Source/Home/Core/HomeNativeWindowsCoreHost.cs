using System.IO.Pipes;

namespace HavenOS.Home.Core;

/// <summary>A configured endpoint name is routing metadata, never installed identity or authority.</summary>
public sealed record HomeNativeWindowsEndpoint(string PipeName)
{
    public const int SchemaVersion = 1;
    internal HomeNativeWindowsEndpoint Capture()
    {
        if (string.IsNullOrWhiteSpace(PipeName) || PipeName.Length > 128 ||
            PipeName.Any(value => !char.IsAsciiLetterOrDigit(value) && value is not '.' and not '-' and not '_'))
            throw new ArgumentException("A bounded scalar Windows Home pipe name is required.");
        return this with { };
    }
}

/// <summary>The trusted Home owner supplies its canonical issuer and held lease. This host owns
/// only its accepting pipes and their original connection tasks; it does not create a provider,
/// profile, installation receipt, permission grant or lease.</summary>
public sealed class HomeNativeWindowsCoreHost : IAsyncDisposable
{
    private sealed class OriginalConnection
    {
        internal NamedPipeServerStream? Pipe;
        internal HomeWindowsCoreConnection? Connection;
        internal Task? Original;
        internal readonly TaskCompletionSource<NamedPipeServerStream> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly HomeNativeCoreApiSessions _sessions;
    private readonly HomeNativeSessionLease _lease;
    private readonly HomeNativeWindowsEndpoint _endpoint;
    private readonly CancellationToken _originalLifetime;
    private readonly CancellationTokenSource _lifetime;
    private readonly object _sync = new();
    private readonly List<OriginalConnection> _originals = [];
    private readonly Task _accept;
    private readonly TaskCompletionSource _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _close;
    private bool _closing;

    private HomeNativeWindowsCoreHost(HomeNativeCoreApiSessions sessions, HomeNativeSessionLease lease,
        HomeNativeWindowsEndpoint endpoint, CancellationToken originalLifetime)
    {
        _sessions = sessions; _lease = lease; _endpoint = endpoint; _originalLifetime = originalLifetime;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(originalLifetime);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _accept = AcceptOriginalsAsync(start.Task);
        start.SetResult();
    }
    public static HomeNativeWindowsCoreHost Start(HomeNativeCoreApiSessions originalSessions,
        HomeNativeSessionLease originalHeldLease, HomeNativeWindowsEndpoint configuredEndpoint,
        CancellationToken originalHomeLifetime)
    {
        ArgumentNullException.ThrowIfNull(originalSessions);
        ArgumentNullException.ThrowIfNull(originalHeldLease);
        ArgumentNullException.ThrowIfNull(configuredEndpoint);
        originalHomeLifetime.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || !originalHomeLifetime.CanBeCanceled || !originalHeldLease.IsHeld)
            throw new UnauthorizedAccessException("Windows, a live owning Home lifetime and its actual held lease are required.");
        return new(originalSessions, originalHeldLease, configuredEndpoint.Capture(), originalHomeLifetime);
    }
    public Task OriginalAcceptTask => _accept;
    public Task OriginalListeningTask => _listening.Task;

    private async Task AcceptOriginalsAsync(Task start)
    {
        await start.ConfigureAwait(false);
        List<Exception> failures = [];
        try
        {
            while (true)
            {
                _lifetime.Token.ThrowIfCancellationRequested();
                if (!_lease.IsHeld) throw new UnauthorizedAccessException("Original Home lease retired before pipe acquisition.");
                OriginalConnection entry;
                TaskCompletionSource gate;
                lock (_sync)
                {
                    if (_closing) break;
                    // A success is pruned only after its SAME serve, close and physical pipe cleanup settled.
                    _originals.RemoveAll(value => value.Original is { IsCompletedSuccessfully: true });
                    if (_originals.Count >= 128)
                        throw new InvalidOperationException("Retain every original Windows connection before accepting another.");
                    entry = new();
                    gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    entry.Original = RunOriginalConnectionAsync(gate.Task, entry);
                    _originals.Add(entry);
                }
                // The original operation and acquisition record are published before pipe construction.
                gate.SetResult();
                // One new accepting instance at a time, while admitted connections retain their own reader.
                var pipeReady = await entry.Ready.Task.ConfigureAwait(false);
                _listening.TrySetResult(); // Only actual successful first pipe construction publishes listening.
                await pipeReady.WaitForConnectionAsync(_lifetime.Token).ConfigureAwait(false);
                lock (_sync)
                {
                    if (_closing) break;
                    _originalLifetime.ThrowIfCancellationRequested();
                    entry.Connection = HomeWindowsCoreConnection.AttachAccepted(pipeReady, _sessions, _lease, _originalLifetime);
                }
                // RunOriginalConnectionAsync owns the SAME serve/close task and pipe from here.
                entry.Connected.TrySetResult();
            }
        }
        catch (OperationCanceledException error) when (_closing && !_originalLifetime.IsCancellationRequested &&
            error.CancellationToken == _lifetime.Token) { }
        catch (Exception error) { _listening.TrySetException(error); HomeUnixCoreTransport.Add(failures, error); }
        finally
        {
            _listening.TrySetCanceled(_lifetime.Token); // Closing before acquisition never manufactures listening.
            try { _lifetime.Cancel(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
            OriginalConnection[] records;
            lock (_sync) records = _originals.ToArray();
            var drains = new List<Task>();
            foreach (var entry in records)
                try { if (entry.Connection is { } connection) drains.Add(connection.CloseAndDrainAsync()); }
                catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
            foreach (var entry in records) entry.Connected.TrySetResult();
            foreach (var original in drains)
                try { await original.ConfigureAwait(false); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
            foreach (var entry in records)
                try { if (entry.Original is not null) await entry.Original.ConfigureAwait(false); }
                catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        }
        HomeUnixCoreTransport.Throw(failures);
    }

    private async Task RunOriginalConnectionAsync(Task start, OriginalConnection entry)
    {
        await start.ConfigureAwait(false);
        List<Exception> failures = [];
        try
        {
            _lifetime.Token.ThrowIfCancellationRequested();
            entry.Pipe = new NamedPipeServerStream(_endpoint.PipeName, PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            entry.Ready.TrySetResult(entry.Pipe);
            await entry.Connected.Task.ConfigureAwait(false);
            if (entry.Connection is { } original)
                await original.OriginalServeTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (_closing && !_originalLifetime.IsCancellationRequested &&
            error.CancellationToken == _lifetime.Token) { entry.Ready.TrySetCanceled(_lifetime.Token); }
        catch (Exception error) { entry.Ready.TrySetException(error); HomeUnixCoreTransport.Add(failures, error); }
        finally
        {
            try { if (entry.Connection is { } original) await original.CloseAndDrainAsync().ConfigureAwait(false); }
            catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
            try { if (entry.Pipe is not null) await entry.Pipe.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        }
        HomeUnixCoreTransport.Throw(failures);
    }

    public Task CloseAndDrainAsync()
    {
        lock (_sync)
        {
            if (_close is not null) return _close;
            _closing = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseOriginalAsync(start.Task);
            start.SetResult();
            return _close;
        }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private async Task CloseOriginalAsync(Task start)
    {
        await start.ConfigureAwait(false);
        List<Exception> failures = [];
        try { _lifetime.Cancel(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { await _accept.ConfigureAwait(false); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { _lifetime.Dispose(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        HomeUnixCoreTransport.Throw(failures);
    }
}
