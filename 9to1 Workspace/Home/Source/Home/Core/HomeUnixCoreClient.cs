using System.Net.Sockets;
using System.Text.Json;
using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Consumes the SAME connected socket; the accepting platform remains its owner.
/// One pending original request/read is retained. No socket, listener, Home lease or authority
/// is created here; request correlation and pending approval IDs grant nothing.</summary>
public sealed class HomeUnixCoreClient : IAsyncDisposable
{
    private readonly Socket _socket;
    private readonly IHomeNativeSessionHostVerifier _verifier;
    private readonly HomeNativeSessionHostRequirement _requirement;
    private readonly HomeNativeObservedPeer _observed;
    private readonly HomeNativeInstalledPeer _host;
    private readonly CancellationTokenSource _lifetime;
    private readonly object _sync = new();
    private Task? _pending;
    private Task? _close;
    private bool _closing;

    private HomeUnixCoreClient(Socket socket, IHomeNativeSessionHostVerifier verifier,
        HomeNativeSessionHostRequirement requirement, HomeNativeObservedPeer observed,
        HomeNativeInstalledPeer host, CancellationToken lifetime)
    {
        _socket = socket; _verifier = verifier; _requirement = requirement;
        _observed = observed; _host = host;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
    }
    public static async ValueTask<HomeUnixCoreClient?> AttachAsync(Socket originalConnectedSocket,
        IHomeNativeSessionHostVerifier verifier, HomeNativeSessionHostRequirement trustedHost,
        CancellationToken originalConnectionLifetime, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalConnectedSocket); ArgumentNullException.ThrowIfNull(verifier);
        if (!originalConnectionLifetime.CanBeCanceled || originalConnectionLifetime.IsCancellationRequested ||
            trustedHost is null || string.IsNullOrWhiteSpace(trustedHost.AppId) ||
            string.IsNullOrWhiteSpace(trustedHost.OperatingSystemApplicationId)) return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(originalConnectionLifetime, cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var observed = HomeNativePeerObservation.FromAcceptedUnixSocket(originalConnectedSocket);
        if (observed is null) return null;
        var host = await verifier.VerifyHostAsync(observed, trustedHost, deadline.Token).ConfigureAwait(false);
        if (host is null || !Valid(host, trustedHost)) return null;
        // Copy the observational attestation so mutable role collections cannot change the retained tuple.
        host = host with { Roles = new HashSet<string>(host.Roles, StringComparer.Ordinal),
            AllowedServiceIds = new HashSet<string>(host.AllowedServiceIds, StringComparer.Ordinal) };
        return new(originalConnectedSocket, verifier, trustedHost with { }, observed, host, originalConnectionLifetime);
    }

    public Task<HomeNativeCoreApiResult<HomeCoreStateSnapshot>> GetStateAsync(CancellationToken token = default)
        => RequestAsync<HomeCoreStateSnapshot>("GetState", null, null, token);
    public Task<HomeNativeCoreApiResult<IReadOnlyList<HomeServiceDescriptor>>> GetServicesAsync(CancellationToken token = default)
        => RequestAsync<IReadOnlyList<HomeServiceDescriptor>>("GetServices", null, null, token);
    public Task<HomeNativeCoreApiResult<HomeServiceDescriptor>> GetServiceAsync(string serviceId, CancellationToken token = default)
        => RequestAsync<HomeServiceDescriptor>("GetService", serviceId, null, token);
    public Task<HomeNativeCoreApiResult<HomeCompatibilityResult>> GetCompatibilityAsync(HomeCompatibilityRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequiredServices is null || request.RequiredServices.Any(row => row is null))
            throw new ArgumentException("Compatibility requirements are absent.", nameof(request));
        return RequestAsync<HomeCompatibilityResult>("GetCompatibility", null,
            request with { RequiredServices = request.RequiredServices.Select(row => row with { }).ToArray() }, token);
    }
    private Task<HomeNativeCoreApiResult<T>> RequestAsync<T>(string operation, string? serviceId,
        HomeCompatibilityRequest? compatibility, CancellationToken caller)
    {
        var request = new HomeUnixCoreRequest(HomeUnixCoreProtocol.Version, Guid.NewGuid().ToString("N"), operation,
            serviceId, compatibility);
        var payload = HomeUnixCoreProtocol.Payload(request); // Frozen before any await.
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            if (_closing || _lifetime.IsCancellationRequested) throw new ObjectDisposedException(nameof(HomeUnixCoreClient));
            if (_pending is { IsCompleted: false }) throw new InvalidOperationException("One original Home Core request is already pending.");
            var original = ExecuteAsync<T>(start.Task, request, payload, caller);
            _pending = original; // Publish SAME task before cancellation callbacks or I/O.
            start.SetResult();
            return original;
        }
    }
    private async Task<HomeNativeCoreApiResult<T>> ExecuteAsync<T>(Task start, HomeUnixCoreRequest request,
        byte[] payload, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        var active = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, caller);
        active.CancelAfter(TimeSpan.FromSeconds(5));
        NetworkStream? stream = null;
        List<Exception> failures = [];
        HomeNativeCoreApiResult<T>? result = null;
        try
        {
            await DemandHostAsync(active.Token).ConfigureAwait(false);
            stream = new NetworkStream(_socket, ownsSocket: false);
            await HomeUnixDiscoveryTransport.WriteFrameAsync(stream, payload, active.Token).ConfigureAwait(false);
            var response = HomeUnixCoreProtocol.ReadResponse(
                await HomeUnixDiscoveryTransport.ReadFrameAsync(stream, active.Token).ConfigureAwait(false), request);
            result = response.Result.Deserialize<HomeNativeCoreApiResult<T>>(HomeUnixCoreProtocol.Json)
                ?? throw new InvalidDataException("Home Core result is absent.");
            if (result.Operation is null) throw new InvalidDataException("Home Core operation result is absent.");
            await DemandHostAsync(active.Token).ConfigureAwait(false);
            active.Token.ThrowIfCancellationRequested();
        }
        catch (Exception error)
        {
            HomeUnixCoreTransport.Add(failures, error);
            // A failed/partial exchange cannot be reused as another frame's response.
            try { _lifetime.Cancel(); } catch (Exception cleanup) { HomeUnixCoreTransport.Add(failures, cleanup); }
        }
        finally
        {
            try { stream?.Dispose(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
            try { active.Dispose(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        }
        HomeUnixCoreTransport.Throw(failures);
        return result!;
    }
    private async Task DemandHostAsync(CancellationToken token)
    {
        var observed = HomeNativePeerObservation.FromAcceptedUnixSocket(_socket);
        if (observed is null || observed != _observed) throw new UnauthorizedAccessException("Original connected Home peer changed.");
        var current = await _verifier.VerifyHostAsync(observed, _requirement, token).ConfigureAwait(false);
        if (current is null || !Valid(current, _requirement) || current.InstalledApplicationId != _host.InstalledApplicationId ||
            current.InstallationRevision != _host.InstallationRevision || current.ExecutableIdentity != _host.ExecutableIdentity)
            throw new UnauthorizedAccessException("Original installed Home host is no longer current.");
    }
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_close is not null) return new(_close);
            _closing = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseOriginalAsync(start.Task, _pending);
            start.SetResult();
            return new(_close);
        }
    }
    private async Task CloseOriginalAsync(Task start, Task? original)
    {
        await start.ConfigureAwait(false);
        List<Exception> failures = [];
        try { _lifetime.Cancel(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { if (original is not null) await original.ConfigureAwait(false); }
        catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { _lifetime.Dispose(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        HomeUnixCoreTransport.Throw(failures);
    }
    private static bool Valid(HomeNativeInstalledPeer? peer, HomeNativeSessionHostRequirement required) => peer is not null &&
        peer.AppId == required.AppId && peer.InstalledApplicationId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(peer.InstallationRevision) && !string.IsNullOrWhiteSpace(peer.ExecutableIdentity) &&
        peer.Roles is not null && peer.Roles.Contains(HomeNativeSessionHostRequirement.RequiredRole) && peer.AllowedServiceIds is not null;
}
