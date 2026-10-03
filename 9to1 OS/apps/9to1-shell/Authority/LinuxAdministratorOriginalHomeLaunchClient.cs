using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace NineToOne.Os.Shell.Authority;

// Separate Home-side authority: the actual local Home lease remains the issuer.
// The administrator observes the independently spawned owner; wire actors never
// replace the Home resource actor source and this client never acquires a lease.
[SupportedOSPlatform("linux")]
internal sealed class LinuxAdministratorOriginalHomeLaunchClient : IHomeNativeControlledLaunchAuthority, IAsyncDisposable
{
    private readonly string _endpoint;
    private readonly HomeNativeSessionLease _lease;
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly AuthenticatedResourceActor _actor;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private Socket? _socket;
    private NetworkStream? _stream;
    private HomeNativeObservedPeer? _administrator;
    private int _retired;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private LinuxAdministratorOriginalHomeLaunchClient(string endpoint, HomeNativeSessionLease lease,
        IAuthenticatedResourceActorSource actors, AuthenticatedResourceActor actor)
        { _endpoint = endpoint; _lease = lease; _actors = actors; _actor = actor; _lifetimeToken = _lifetime.Token; }

    internal static async ValueTask<LinuxAdministratorOriginalHomeLaunchClient?> CaptureAsync(
        string protectedRootSocket, HomeNativeSessionLease originalLease,
        IAuthenticatedResourceActorSource actors, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux() || !Path.IsPathFullyQualified(protectedRootSocket) ||
            Path.GetFullPath(protectedRootSocket) != protectedRootSocket ||
            !LinuxRootOwnedFiles.DirectoryImmutable(Path.GetDirectoryName(protectedRootSocket)!) || !originalLease.IsHeld)
            return null;
        var actor = await actors.GetCurrentAsync(ct).ConfigureAwait(false);
        if (actor is null || actor.OrganisationId is not null || actor.ProfileId != originalLease.ProfileId ||
            string.IsNullOrWhiteSpace(actor.AuthenticationRevision) || !originalLease.IsHeld ||
            actor != await actors.GetCurrentAsync(ct).ConfigureAwait(false) || !originalLease.IsHeld) return null;
        return new(protectedRootSocket, originalLease, actors, actor);
    }
    private async ValueTask<bool> OriginalCurrentAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _retired) != 0 || !_lease.IsHeld) return false;
        var current = _actor == await _actors.GetCurrentAsync(ct).ConfigureAwait(false) && _lease.IsHeld;
        if (!current) Retire();
        return current && Volatile.Read(ref _retired) == 0;
    }
    private void Retire()
    {
        if (Interlocked.Exchange(ref _retired, 1) == 0)
            try { _lifetime.Cancel(); } finally { _socket?.Dispose(); }
    }
    public async ValueTask<bool> IsCurrentAsync(HomeNativeControlledLaunchObservation observation, CancellationToken ct)
    {
        if (observation is null || observation.ProfileId != _actor.ProfileId ||
            observation.SessionLeaseIdentity != _lease.LeaseIdentity.ToString("N") || !await OriginalCurrentAsync(ct)) return false;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeToken);
        linked.CancelAfter(TimeSpan.FromSeconds(15));
        var entered = false;
        try
        {
            await _gate.WaitAsync(linked.Token).ConfigureAwait(false); entered = true;
            linked.Token.ThrowIfCancellationRequested();
            if (!await OriginalCurrentAsync(linked.Token)) return false;
            if (_socket is null)
            {
                // Connect lazily: root first admits this same Home on its readiness channel.
                // There is one original authority connection; failures permanently retire it.
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                _socket = socket;
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(_endpoint), linked.Token).ConfigureAwait(false);
                _administrator = HomeNativePeerObservation.FromAcceptedUnixSocket(socket);
                if (_administrator?.OperatingSystemPrincipalId != "unix-euid:0" || !await OriginalCurrentAsync(linked.Token))
                    { Retire(); return false; }
                _stream = new NetworkStream(socket, ownsSocket: false);
            }
            var connected = _socket ?? throw new InvalidOperationException("Original authority connection unavailable.");
            var stream = _stream ?? throw new InvalidOperationException("Original authority stream unavailable.");
            if (_administrator != HomeNativePeerObservation.FromAcceptedUnixSocket(connected) ||
                !await OriginalCurrentAsync(linked.Token)) { Retire(); return false; }
            var correlation = Guid.NewGuid();
            await LinuxHomeChildLeaseChannel.WriteAsync(stream, new LinuxAdministratorHomeLaunchRequest(
                3, correlation, _lease.LeaseIdentity.ToString("N"), _actor, observation), linked.Token).ConfigureAwait(false);
            if (!await OriginalCurrentAsync(linked.Token)) return false;
            var reply = await LinuxHomeChildLeaseChannel.ReadAsync<LinuxAdministratorHomeLaunchReply>(stream, linked.Token).ConfigureAwait(false);
            if (reply.Schema != 3 || reply.Correlation != correlation ||
                _administrator != HomeNativePeerObservation.FromAcceptedUnixSocket(connected) ||
                !await OriginalCurrentAsync(linked.Token)) { Retire(); return false; }
            // An explicit false launch observation does not retire unrelated allowed tuples.
            return reply.Current;
        }
        catch (Exception error) when (error is IOException or SocketException or UnauthorizedAccessException or
            JsonException or InvalidOperationException or OperationCanceledException)
        {
            Retire(); if (ct.IsCancellationRequested) throw; return false;
        }
        finally { if (entered) _gate.Release(); }
    }
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion = null; Task task;
        lock (_disposeGate)
        {
            if (_disposeTask is null) { completion = new(TaskCreationOptions.RunContinuationsAsynchronously); _disposeTask = completion.Task; }
            task = _disposeTask;
        }
        if (completion is not null) _ = DrainAsync(completion);
        return new(task);
    }
    private async Task DrainAsync(TaskCompletionSource completion)
    {
        Exception? first = null;
        try { Retire(); } catch (Exception error) { first = error; }
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            try { _stream?.Dispose(); } catch (Exception error) { first ??= error; }
            try { _socket?.Dispose(); } catch (Exception error) { first ??= error; }
            try { _lifetime.Dispose(); } catch (Exception error) { first ??= error; }
        }
        finally { _gate.Release(); }
        if (first is null) completion.TrySetResult(); else completion.TrySetException(first);
    }
}
