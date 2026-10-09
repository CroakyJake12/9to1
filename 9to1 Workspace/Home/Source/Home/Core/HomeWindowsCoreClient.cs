using System.Collections.Frozen;
using System.IO.Pipes;
using System.Text.Json;
using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Borrowed original Windows pipe; one retained request/reader. No listener, lease, profile, provider or grant is created.</summary>
public sealed partial class HomeWindowsCoreClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly IHomeNativeSessionHostVerifier _verifier;
    private readonly HomeNativeSessionHostRequirement _requirement;
    private readonly HomeNativeObservedPeer _observed;
    private readonly HomeNativeInstalledPeer _host;
    private readonly CancellationTokenSource _lifetime;
    private readonly object _sync = new();
    private Task? _pending;
    private Task? _close;
    private bool _closing;

    private HomeWindowsCoreClient(NamedPipeClientStream pipe, IHomeNativeSessionHostVerifier verifier,
        HomeNativeSessionHostRequirement requirement, HomeNativeObservedPeer observed,
        HomeNativeInstalledPeer host, CancellationToken lifetime)
    {
        _pipe = pipe; _verifier = verifier; _requirement = requirement;
        _observed = observed; _host = host;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
    }

    public static async ValueTask<HomeWindowsCoreClient?> AttachAsync(NamedPipeClientStream originalConnectedPipe,
        IHomeNativeSessionHostVerifier verifier, HomeNativeSessionHostRequirement trustedHost,
        CancellationToken originalConnectionLifetime, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalConnectedPipe);
        ArgumentNullException.ThrowIfNull(verifier);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || !originalConnectedPipe.IsConnected ||
            !originalConnectionLifetime.CanBeCanceled || originalConnectionLifetime.IsCancellationRequested ||
            trustedHost is null || !Text(trustedHost.AppId, 128) || !Text(trustedHost.OperatingSystemApplicationId, 4096)) return null;
        var requirement = trustedHost with { };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(originalConnectionLifetime, cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var observed = HomeNativeWindowsHostObservation.FromConnectedPipe(originalConnectedPipe);
        if (observed is null) return null;
        var host = Capture(await verifier.VerifyHostAsync(observed, requirement, deadline.Token).ConfigureAwait(false), requirement);
        if (host is null) return null;
        var client = new HomeWindowsCoreClient(originalConnectedPipe, verifier, requirement, observed, host, originalConnectionLifetime);
        Exception? primary = null;
        var retained = false;
        try
        {
            await client.DemandHostAsync(deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            retained = true;
            return client;
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            if (!retained)
                try { await client.DisposeAsync().ConfigureAwait(false); }
                catch (Exception cleanup) when (primary is not null && !ReferenceEquals(primary, cleanup))
                { throw new AggregateException("Original Windows Home attachment and client cleanup failed.", primary, cleanup); }
        }
    }

    public Task<HomeNativeCoreApiResult<HomeCoreStateSnapshot>> GetStateAsync(CancellationToken token = default) =>
        RequestAsync<HomeCoreStateSnapshot>("GetState", null, null, token);
    public Task<HomeNativeCoreApiResult<IReadOnlyList<HomeServiceDescriptor>>> GetServicesAsync(CancellationToken token = default) =>
        RequestAsync<IReadOnlyList<HomeServiceDescriptor>>("GetServices", null, null, token);
    public Task<HomeNativeCoreApiResult<HomeServiceDescriptor>> GetServiceAsync(string serviceId, CancellationToken token = default) =>
        RequestAsync<HomeServiceDescriptor>("GetService", serviceId, null, token);
    public Task<HomeNativeCoreApiResult<HomeCompatibilityResult>> GetCompatibilityAsync(HomeCompatibilityRequest request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequiredServices is null) throw new ArgumentException("Compatibility requirements are absent.", nameof(request));
        var requirements = request.RequiredServices.Take(65).ToArray();
        if (requirements.Length > 64 || requirements.Any(row => row is null))
            throw new ArgumentException("Compatibility requirements exceed the retained bound.", nameof(request));
        return RequestAsync<HomeCompatibilityResult>("GetCompatibility", null,
            request with { RequiredServices = Array.AsReadOnly(requirements.Select(row => row with { }).ToArray()) }, token);
    }

    private Task<HomeNativeCoreApiResult<T>> RequestAsync<T>(string operation, string? serviceId,
        HomeCompatibilityRequest? compatibility, CancellationToken caller)
    {
        if (_originalScoped) return RequestWithinOriginalSourceAsync<T>(operation, serviceId, compatibility, body => body(), _ => { }, caller);
        caller.ThrowIfCancellationRequested();
        var request = new HomeUnixCoreRequest(HomeUnixCoreProtocol.Version, Guid.NewGuid().ToString("N"),
            operation, serviceId, compatibility);
        var payload = HomeUnixCoreProtocol.Payload(request); // Complete detached intent before task publication.
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing || _lifetime.IsCancellationRequested, this);
            if (_pending is { IsCompleted: false }) throw new InvalidOperationException("One original Home pipe request is already pending.");
            var original = ExecuteAsync<T>(start.Task, request, payload, caller);
            _pending = original;
            start.SetResult(); // SAME original task already retained before any callback/I/O.
            return original;
        }
    }

    private async Task<HomeNativeCoreApiResult<T>> ExecuteAsync<T>(Task start, HomeUnixCoreRequest request,
        byte[] payload, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        CancellationTokenSource? active = null;
        List<Exception> failures = [];
        HomeNativeCoreApiResult<T>? result = null;
        try
        {
            active = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, caller);
            active.CancelAfter(TimeSpan.FromSeconds(5));
            await DemandHostAsync(active.Token).ConfigureAwait(false);
            await HomeUnixDiscoveryTransport.WriteFrameAsync(_pipe, payload, active.Token).ConfigureAwait(false);
            var response = HomeUnixCoreProtocol.ReadResponse(
                await HomeUnixDiscoveryTransport.ReadFrameAsync(_pipe, active.Token).ConfigureAwait(false), request);
            result = response.Result.Deserialize<HomeNativeCoreApiResult<T>>(HomeUnixCoreProtocol.Json)
                ?? throw new InvalidDataException("Windows Home Core result is absent.");
            if (result.Operation is null) throw new InvalidDataException("Windows Home operation result is absent.");
            await DemandHostAsync(active.Token).ConfigureAwait(false);
            active.Token.ThrowIfCancellationRequested();
        }
        catch (Exception error)
        {
            HomeUnixCoreTransport.Add(failures, error);
            // An interrupted/partial exchange never becomes a later request's response.
            try { _lifetime.Cancel(); } catch (Exception cleanup) { HomeUnixCoreTransport.Add(failures, cleanup); }
        }
        finally
        {
            try { active?.Dispose(); } catch (Exception cleanup) { HomeUnixCoreTransport.Add(failures, cleanup); }
            // The supplied platform owns this pipe. Client close cancels/drains, never disposes it.
        }
        HomeUnixCoreTransport.Throw(failures);
        return result!;
    }

    private async Task DemandHostAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var observed = HomeNativeWindowsHostObservation.FromConnectedPipe(_pipe);
        if (observed is null || observed != _observed)
            throw new UnauthorizedAccessException("Original connected Windows Home peer changed.");
        var current = Capture(await _verifier.VerifyHostAsync(observed, _requirement, token).ConfigureAwait(false), _requirement);
        if (current is null || current.InstalledApplicationId != _host.InstalledApplicationId ||
            current.InstallationRevision != _host.InstallationRevision || current.ExecutableIdentity != _host.ExecutableIdentity ||
            !current.Roles.SetEquals(_host.Roles) || !current.AllowedServiceIds.SetEquals(_host.AllowedServiceIds))
            throw new UnauthorizedAccessException("Original installed Windows Home host is no longer current.");
        var after = HomeNativeWindowsHostObservation.FromConnectedPipe(_pipe);
        if (after is null || after != _observed) throw new UnauthorizedAccessException("Original Home pipe peer retired during verification.");
        token.ThrowIfCancellationRequested();
        _lifetime.Token.ThrowIfCancellationRequested();
    }

    /// <summary>Retains a fresh attestation check in the same one-operation slot; it performs no wire read or broker grant.</summary>
    internal Task DemandOriginalCurrentAsync(CancellationToken caller)
    {
        if (_originalScoped) return DemandOriginalCurrentWithinSourceAsync(body => body(), _ => { }, caller);
        caller.ThrowIfCancellationRequested();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing || _lifetime.IsCancellationRequested, this);
            if (_pending is { IsCompleted: false }) throw new InvalidOperationException("One original Home pipe operation is already pending.");
            var original = DemandOriginalAsync(start.Task, caller);
            _pending = original;
            start.SetResult();
            return original;
        }
    }
    private async Task DemandOriginalAsync(Task start, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        CancellationTokenSource? active = null;
        List<Exception> failures = [];
        try
        {
            active = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, caller);
            active.CancelAfter(TimeSpan.FromSeconds(5));
            await DemandHostAsync(active.Token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            HomeUnixCoreTransport.Add(failures, error);
            try { _lifetime.Cancel(); } catch (Exception cleanup) { HomeUnixCoreTransport.Add(failures, cleanup); }
        }
        finally
        {
            try { active?.Dispose(); } catch (Exception cleanup) { HomeUnixCoreTransport.Add(failures, cleanup); }
        }
        HomeUnixCoreTransport.Throw(failures);
    }

    public ValueTask DisposeAsync()
    {
        if (_originalScoped) CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        lock (_sync)
        {
            if (_close is not null) return new(_close);
            _closing = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = _originalScoped ? CloseWithinOriginalSourceAsync(start.Task) : CloseOriginalAsync(start.Task, _pending);
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

    private static HomeNativeInstalledPeer? Capture(HomeNativeInstalledPeer? peer, HomeNativeSessionHostRequirement required)
    {
        if (peer is null || peer.AppId != required.AppId || peer.InstalledApplicationId == Guid.Empty ||
            !Text(peer.InstallationRevision, 4096) || !Text(peer.ExecutableIdentity, 4096) ||
            peer.Roles is null || peer.AllowedServiceIds is null) return null;
        var roles = peer.Roles.Take(129).ToArray();
        var services = peer.AllowedServiceIds.Take(129).ToArray();
        if (roles.Length > 128 || services.Length > 128 ||
            roles.Any(row => !Text(row, 128)) || services.Any(row => !Text(row, 128)) ||
            roles.Distinct(StringComparer.Ordinal).Count() != roles.Length ||
            services.Distinct(StringComparer.Ordinal).Count() != services.Length ||
            !roles.Contains(HomeNativeSessionHostRequirement.RequiredRole, StringComparer.Ordinal)) return null;
        return peer with { Roles = roles.ToFrozenSet(StringComparer.Ordinal), AllowedServiceIds = services.ToFrozenSet(StringComparer.Ordinal) };
    }
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximum && value == value.Trim();
}
