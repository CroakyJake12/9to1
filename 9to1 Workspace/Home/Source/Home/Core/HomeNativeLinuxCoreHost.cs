using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HavenOS.Home.Core;

/// <summary>Actual Linux accepting host for the existing canonical Unix discovery/Core
/// transports. The trusted owning composition supplies its SAME session issuer, discovery
/// owner and held process lease. Listening grants no installed identity, root, permission
/// or publisher authority; the existing transports authenticate every accepted OS peer.</summary>
public sealed partial class HomeNativeLinuxCoreHost : IAsyncDisposable
{
    private sealed class Connection(Socket socket)
    {
        internal readonly Socket Socket = socket;
        internal Task Original = null!;
        internal Task? OriginalServe;
    }
    private readonly HomeNativeDiscoverySession _discovery;
    private readonly HomeNativeCoreApiSessions _sessions;
    private readonly HomeNativeSessionLease _lease;
    private readonly CancellationTokenSource _lifetime;
    private readonly HomeNativeEndpointLocation _endpoint;
    private readonly object _gate = new();
    private readonly List<Connection> _connections = [];
    private readonly TaskCompletionSource _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _accept;
    private Socket? _listener;
    private EndpointCustody? _custody;
    private Task? _close;
    private bool _closing;
    private long _rejected;

    private HomeNativeLinuxCoreHost(HomeNativeDiscoverySession discovery, HomeNativeCoreApiSessions sessions,
        HomeNativeSessionLease lease, HomeNativeEndpointLocation endpoint, CancellationToken lifetime)
    {
        _discovery = discovery; _sessions = sessions; _lease = lease; _endpoint = endpoint;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _accept = AcceptOriginalAsync(start.Task);
        start.SetResult();
    }

    public static HomeNativeLinuxCoreHost Start(HomeNativeDiscoverySession originalDiscovery,
        HomeNativeCoreApiSessions originalSessions, HomeNativeSessionLease originalHeldLease,
        HomeNativeEndpointLocation originalEndpoint, CancellationToken originalHomeLifetime)
    {
        ArgumentNullException.ThrowIfNull(originalDiscovery); ArgumentNullException.ThrowIfNull(originalSessions);
        ArgumentNullException.ThrowIfNull(originalHeldLease); ArgumentNullException.ThrowIfNull(originalEndpoint);
        originalHomeLifetime.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsLinux() || !originalHomeLifetime.CanBeCanceled || !originalHeldLease.IsHeld ||
            originalEndpoint.SchemaVersion != 1 || originalEndpoint.ProfileId != originalHeldLease.ProfileId ||
            originalEndpoint.HostProcessId != Environment.ProcessId || originalEndpoint.Epoch == Guid.Empty ||
            !Path.IsPathFullyQualified(originalEndpoint.SocketPath) || Encoding.UTF8.GetByteCount(originalEndpoint.SocketPath) > 100)
            throw new UnauthorizedAccessException("Linux, the actual held Home lease, owning lifetime and same-profile endpoint are required.");
        return new(originalDiscovery, originalSessions, originalHeldLease, originalEndpoint with { }, originalHomeLifetime);
    }

    public Task OriginalListeningTask => _listening.Task;
    public Task OriginalAcceptTask => _accept;
    public HomeNativeEndpointLocation Endpoint => _endpoint with { };
    public long RejectedConnectionCount => Interlocked.Read(ref _rejected);

    private async Task AcceptOriginalAsync(Task start)
    {
        await start.ConfigureAwait(false);
        List<Exception> failures = [];
        try
        {
            _lifetime.Token.ThrowIfCancellationRequested();
            if (!_lease.IsHeld) throw new UnauthorizedAccessException("The original Home lease retired before Unix binding.");
            _custody = new EndpointCustody(_endpoint.SocketPath);
            // Never remove/adopt an existing endpoint. Bind's actual EADDRINUSE is
            // retained, including a preexisting socket, ordinary file or symlink.
            _listener = _custody.BindOriginalSocket();
            _listener.Listen(32);
            _custody.DemandCurrent();
            _listening.TrySetResult();
            while (true)
            {
                _lifetime.Token.ThrowIfCancellationRequested();
                if (!_lease.IsHeld) throw new UnauthorizedAccessException("The original Home lease retired while accepting Unix peers.");
                _custody.DemandCurrent();
                var socket = await _listener.AcceptAsync(_lifetime.Token).ConfigureAwait(false);
                TaskCompletionSource? gate = null;
                try
                {
                    lock (_gate)
                    {
                        // Success here means serve/rejection and physical socket close
                        // settled. Failed originals remain retained for owning recovery.
                        _connections.RemoveAll(value => value.Original.IsCompletedSuccessfully);
                        if (_closing || !_lease.IsHeld || _lifetime.IsCancellationRequested || _connections.Count >= 32)
                        { Interlocked.Increment(ref _rejected); continue; }
                        _custody.DemandCurrent();
                        var entry = new Connection(socket);
                        gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        entry.Original = ServeOriginalAsync(entry, gate.Task);
                        _connections.Add(entry);
                        socket = null!; // ownership transfers before the original serve runs
                    }
                }
                finally { socket?.Dispose(); gate?.SetResult(); }
            }
        }
        catch (OperationCanceledException error) when (_lifetime.IsCancellationRequested && error.CancellationToken == _lifetime.Token)
        { _listening.TrySetCanceled(_lifetime.Token); }
        catch (Exception cause) { _listening.TrySetException(cause); HomeUnixCoreTransport.Add(failures, cause); }
        finally
        {
            _listening.TrySetCanceled(_lifetime.Token);
            try { _lifetime.Cancel(); } catch (Exception cause) { HomeUnixCoreTransport.Add(failures, cause); }
            try { _listener?.Dispose(); } catch (Exception cause) { HomeUnixCoreTransport.Add(failures, cause); }
            Connection[] originals;
            lock (_gate) originals = _connections.ToArray();
            foreach (var entry in originals)
                try { await entry.Original.ConfigureAwait(false); }
                catch (Exception cause) { HomeUnixCoreTransport.Add(failures, cause); }
            try { _custody?.RemoveOwnedSocket(); } catch (Exception cause) { HomeUnixCoreTransport.Add(failures, cause); }
            try { _custody?.Dispose(); } catch (Exception cause) { HomeUnixCoreTransport.Add(failures, cause); }
        }
        HomeUnixCoreTransport.Throw(failures);
    }

    private async Task ServeOriginalAsync(Connection entry, Task start)
    {
        await start.ConfigureAwait(false);
        try
        {
            _lifetime.Token.ThrowIfCancellationRequested();
            if (!_lease.IsHeld) throw new UnauthorizedAccessException("Original Home lease unavailable before peer admission.");
            // One retained original socket reader; identity still comes exclusively
            // from HomeNativePeerObservation inside the existing transports.
            entry.OriginalServe = HomeUnixCoreTransport.ServeAcceptedAsync(entry.Socket, _discovery,
                _sessions, _lease, _lifetime.Token);
            await entry.OriginalServe.ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (_lifetime.IsCancellationRequested && error.CancellationToken == _lifetime.Token) { }
        catch (Exception cause) when (cause is UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            // An unknown/untrusted peer or malformed request is an ordinary rejection,
            // with no successful service response. Do not let unauthenticated traffic
            // exhaust the bounded retained-fault budget of a healthy Home process.
            Interlocked.Increment(ref _rejected);
        }
        finally { entry.Socket.Dispose(); }
    }

    public Task CloseAndDrainAsync()
    {
        TaskCompletionSource? start = null;
        Task original;
        lock (_gate)
        {
            if (_close is null)
            {
                _closing = true;
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _close = CloseOriginalAsync(start.Task);
            }
            original = _close;
        }
        start?.SetResult();
        return original;
    }
    private async Task CloseOriginalAsync(Task start)
    {
        await start.ConfigureAwait(false);
        List<Exception> failures = [];
        try { _lifetime.Cancel(); } catch (Exception cause) { HomeUnixCoreTransport.Add(failures, cause); }
        try { await _accept.ConfigureAwait(false); } catch (Exception cause) { HomeUnixCoreTransport.Add(failures, cause); }
        try { _lifetime.Dispose(); } catch (Exception cause) { HomeUnixCoreTransport.Add(failures, cause); }
        HomeUnixCoreTransport.Throw(failures);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
