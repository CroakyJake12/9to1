using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Net.Sockets;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
namespace NineToOne.Os.Shell.Authority;

/// <summary>The installed owner supplies its genuine backend and original declaration/source authority.
/// Authenticated socket observation, not locator or frame fields, authenticates the connected Home host.</summary>
public sealed class LinuxNativeWidgetOwnerConnection : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 20 };
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly HomeNativeObservedPeer _host;
    private readonly HomeNativeSessionHostRequirement _requirement;
    private readonly HomeNativeInstalledPeer _originalHost;
    private readonly HomeNativeInstalledPeer _originalOwner;
    private readonly HomeNativeObservedPeer _self;
    private readonly IHomeNativeInstalledPeerOriginalActorVerifier _ownerVerifier;
    private readonly IHomeNativeSessionHostOriginalActorVerifier _verifier;
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly ResourceAuthorizationService _resources;
    private readonly AuthenticatedResourceActor _original;
    private readonly IReadOnlyList<HomeNativeWidgetDefinition> _definitions;
    private readonly IHomeNativeWidgetOriginalActorRuntimeEndpoint _backend;
    private readonly CancellationTokenSource _lifetime;
    private int _started, _closed;
    private LinuxNativeWidgetOwnerConnection(Socket socket, NetworkStream stream, HomeNativeObservedPeer host,
        HomeNativeSessionHostRequirement requirement, HomeNativeInstalledPeer originalHost,
        HomeNativeInstalledPeer originalOwner, HomeNativeObservedPeer self, IHomeNativeInstalledPeerOriginalActorVerifier ownerVerifier,
        IHomeNativeSessionHostOriginalActorVerifier verifier,
        IAuthenticatedResourceActorSource actors, ResourceAuthorizationService resources, AuthenticatedResourceActor original,
        IReadOnlyList<HomeNativeWidgetDefinition> definitions, IHomeNativeWidgetOriginalActorRuntimeEndpoint backend, CancellationToken ct)
    {
        _socket = socket; _stream = stream; _host = host; _requirement = requirement; _originalHost = originalHost;
        _originalOwner = originalOwner; _self = self; _ownerVerifier = ownerVerifier; _verifier = verifier;
        _actors = actors; _resources = resources; _original = original; _definitions = definitions; _backend = backend;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
    }
    public static async Task<LinuxNativeWidgetOwnerConnection?> ConnectAsync(Socket actualConnectedSocket,
        HomeNativeSessionHostRequirement trustedHost, IHomeNativeSessionHostVerifier verifier,
        IHomeNativeInstalledPeerVerifier installedOwnerVerifier, ITrustedHostPrincipalSource actualPrincipals,
        IAuthenticatedResourceActorSource actors, ResourceAuthorizationService resources,
        IReadOnlyList<HomeNativeWidgetDefinition> declarations, IHomeNativeWidgetRuntimeEndpoint genuineBackend,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(genuineBackend);
        if (verifier is not IHomeNativeSessionHostOriginalActorVerifier originalVerifier ||
            genuineBackend is not IHomeNativeWidgetOriginalActorRuntimeEndpoint originalBackend ||
            installedOwnerVerifier is not IHomeNativeInstalledPeerOriginalActorVerifier originalOwnerVerifier) return null;
        // Freeze bounded typed declarations before any actor/host await; frame never supplies a backend.
        var definitions = declarations.Take(65).ToArray();
        if (definitions.Length is 0 or > 64) throw new InvalidDataException("Explicit bounded owner declarations required.");
        long units = 0;
        foreach (var definition in definitions)
        {
            if (definition is null || definition.ActionIds is null || definition.DataScopes is null ||
                definition.ActionIds.Count > 64 || definition.DataScopes.Count > 64)
                throw new InvalidDataException("Bounded explicit owner metadata required.");
            foreach (var text in new[] { definition.WidgetId, definition.Revision, definition.Label,
                definition.ConfigurationSchemaReference, definition.SurfaceReference }.Concat(definition.ActionIds)
                .Concat(definition.DataScopes.SelectMany(scope => new[] { scope.Kind, scope.Id, scope.Revision })))
            {
                if (text is null) throw new InvalidDataException("Explicit owner metadata required.");
                units += text.Length; if (units > 32768) throw new InvalidDataException("Owner metadata exceeds pre-serialization bound.");
            }
        }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new LinuxNativeWidgetRegistration(1, definitions), Json);
        if (bytes.Length > 65536) throw new InvalidDataException("Owner declarations exceed registration bound.");
        var frozen = JsonSerializer.Deserialize<LinuxNativeWidgetRegistration>(bytes, Json)!.Definitions;
        var original = await actors.GetCurrentAsync(ct).ConfigureAwait(false);
        if (original is null) return null;
        var principal = await actualPrincipals.GetPrincipalAsync(ct).ConfigureAwait(false);
        if (principal is null || original != await actors.GetCurrentAsync(ct).ConfigureAwait(false)) return null;
        // Actual local process identity, never an owner identity/PID supplied by the wire or declarations.
        var self = new HomeNativeObservedPeer(Environment.ProcessId, principal);
        var owner = Snapshot(await originalOwnerVerifier.VerifyForActorAsync(self, original, ct).ConfigureAwait(false));
        if (!OwnerValid(owner) || original != await actors.GetCurrentAsync(ct).ConfigureAwait(false)) return null;
        var host = HomeNativePeerObservation.FromAcceptedUnixSocket(actualConnectedSocket);
        if (host is null) return null;
        var verified = Snapshot(await originalVerifier.VerifyHostForActorAsync(host, trustedHost, original, ct).ConfigureAwait(false));
        if (!HostMatches(verified, trustedHost) || original != await actors.GetCurrentAsync(ct).ConfigureAwait(false)) return null;
        var stream = new NetworkStream(actualConnectedSocket, ownsSocket: false);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(5));
            await Write(stream, bytes, 65536, deadline.Token).ConfigureAwait(false);
            var reply = JsonSerializer.Deserialize<LinuxNativeWidgetRegistrationResult>(await Read(stream, 65536, deadline.Token).ConfigureAwait(false), Json);
            if (reply is not { Schema: 1, Code: "RegisteredLiveOwner" } ||
                original != await actors.GetCurrentAsync(deadline.Token).ConfigureAwait(false)) { stream.Dispose(); return null; }
            return new(actualConnectedSocket, stream, host, trustedHost, verified!, owner!, self, originalOwnerVerifier, originalVerifier, actors, resources, original, frozen, originalBackend, ct);
        }
        catch { stream.Dispose(); throw; }
    }
    public async Task ServeCapturesAsync()
    {
        if (Volatile.Read(ref _closed) != 0) throw new ObjectDisposedException(nameof(LinuxNativeWidgetOwnerConnection));
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("One original owner reader only.");
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); idle.CancelAfter(TimeSpan.FromSeconds(60));
                var frame = JsonSerializer.Deserialize<LinuxNativeWidgetCapture>(await Read(_stream, 65536, idle.Token).ConfigureAwait(false), Json);
                if (frame is null || frame.RequestId == Guid.Empty || frame.OriginalActor != _original || frame.Request is null || frame.Request.Reference is null || frame.Request.GridSize is null)
                    throw new InvalidDataException("Capture does not match the original owner context.");
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); deadline.CancelAfter(TimeSpan.FromSeconds(5));
                var request = frame.Request;
                var definition = _definitions.SingleOrDefault(d => d.WidgetId == request.Reference.WidgetId && d.Revision == request.Reference.DefinitionRevision);
                if (definition is null || request.Reference.AppId != _originalOwner.AppId ||
                    request.Reference.InstalledApplicationId != _originalOwner.InstalledApplicationId ||
                    request.Reference.InstallationRevision != _originalOwner.InstallationRevision ||
                    definition.SurfaceReference != request.SurfaceReference ||
                    request.GridSize.Columns < definition.MinimumSize.Columns || request.GridSize.Columns > definition.MaximumSize.Columns ||
                    request.GridSize.Rows < definition.MinimumSize.Rows || request.GridSize.Rows > definition.MaximumSize.Rows ||
                    !double.IsFinite(request.ViewportWidth) || !double.IsFinite(request.ViewportHeight) ||
                    request.ViewportWidth <= 0 || request.ViewportHeight <= 0 || request.ViewportWidth > 16384 || request.ViewportHeight > 16384 ||
                    !await Current(deadline.Token).ConfigureAwait(false)) throw new UnauthorizedAccessException("Original owner capture unavailable.");
                if (definition.DataScopes.Count > 0 && await _resources.AuthorizeForActorAsync(_original,
                    HomeNativeWidgetRegistry.RenderActionId, definition.DataScopes, deadline.Token).ConfigureAwait(false) != _original)
                    throw new UnauthorizedAccessException("Original widget source access unavailable.");
                if (!await Current(deadline.Token).ConfigureAwait(false)) throw new UnauthorizedAccessException("Original owner retired.");
                var surface = await _backend.CaptureForActorAsync(request, _original, deadline.Token).ConfigureAwait(false);
                if (surface is null || surface.Reference != request.Reference || surface.SurfaceReference != request.SurfaceReference ||
                    !await Current(deadline.Token).ConfigureAwait(false)) throw new UnauthorizedAccessException("Original surface unavailable.");
                if (definition.DataScopes.Count > 0 && await _resources.AuthorizeForActorAsync(_original,
                    HomeNativeWidgetRegistry.RenderActionId, definition.DataScopes, deadline.Token).ConfigureAwait(false) != _original)
                    throw new UnauthorizedAccessException("Original widget source access changed during capture.");
                if (!await Current(deadline.Token).ConfigureAwait(false)) throw new UnauthorizedAccessException("Original owner retired after capture.");
                // Detached bounded data only. No configuration or declared action is dispatched here.
                var bounded = HomeNativeWidgetSurface.Capture(surface.Reference, surface.SurfaceReference, surface.AuthoredCui, surface.Data);
                await Write(_stream, JsonSerializer.SerializeToUtf8Bytes(new LinuxNativeWidgetSurface(frame.RequestId,
                    bounded.Reference, bounded.SurfaceReference, bounded.AuthoredCui, bounded.Data.ToDictionary(x => x.Key, x => x.Value)), Json),
                    384 * 1024, deadline.Token).ConfigureAwait(false);
            }
        }
        finally { Dispose(); _lifetime.Dispose(); }
    }
    private async Task<bool> Current(CancellationToken ct)
    {
        if (Volatile.Read(ref _closed) != 0 || _original != await _actors.GetCurrentAsync(ct).ConfigureAwait(false)) return false;
        var host = Snapshot(await _verifier.VerifyHostForActorAsync(_host, _requirement, _original, ct).ConfigureAwait(false));
        if (!HostMatches(host, _requirement) || !SameInstalled(host!, _originalHost) ||
            Volatile.Read(ref _closed) != 0 || _original != await _actors.GetCurrentAsync(ct).ConfigureAwait(false)) return false;
        var owner = Snapshot(await _ownerVerifier.VerifyForActorAsync(_self, _original, ct).ConfigureAwait(false));
        return OwnerValid(owner) && SameInstalled(owner!, _originalOwner) &&
            _original == await _actors.GetCurrentAsync(ct).ConfigureAwait(false) && Volatile.Read(ref _closed) == 0;
    }
    private static HomeNativeInstalledPeer? Snapshot(HomeNativeInstalledPeer? identity) => identity is null ? null :
        new(identity.AppId, identity.InstalledApplicationId, identity.InstallationRevision, identity.ExecutableIdentity,
            identity.AllowedServiceIds.ToFrozenSet(StringComparer.Ordinal)) { Roles = identity.Roles.ToFrozenSet(StringComparer.Ordinal) };
    private static bool OwnerValid(HomeNativeInstalledPeer? owner) => owner is not null && owner.InstalledApplicationId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(owner.AppId) && !string.IsNullOrWhiteSpace(owner.InstallationRevision) &&
        !string.IsNullOrWhiteSpace(owner.ExecutableIdentity) && owner.AllowedServiceIds.Contains(HomeNativeWidgetRegistry.ServiceId);
    private static bool SameInstalled(HomeNativeInstalledPeer observed, HomeNativeInstalledPeer original) =>
        observed.AppId == original.AppId && observed.InstalledApplicationId == original.InstalledApplicationId &&
        observed.InstallationRevision == original.InstallationRevision && observed.ExecutableIdentity == original.ExecutableIdentity &&
        observed.AllowedServiceIds.SetEquals(original.AllowedServiceIds) && observed.Roles.SetEquals(original.Roles);
    private static bool HostMatches(HomeNativeInstalledPeer? host, HomeNativeSessionHostRequirement requirement) =>
        host is not null && host.InstalledApplicationId != Guid.Empty && !string.IsNullOrWhiteSpace(host.InstallationRevision) &&
        !string.IsNullOrWhiteSpace(host.ExecutableIdentity) && host.AppId == requirement.AppId &&
        host.AllowedServiceIds.Contains(HomeNativeWidgetRegistry.ServiceId) && host.Roles.Contains(HomeNativeSessionHostRequirement.RequiredRole);
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _lifetime.Cancel(); _socket.Dispose(); _stream.Dispose();
        // Active reader owns CTS disposal after its original backend awaits drain.
        if (Volatile.Read(ref _started) == 0) _lifetime.Dispose();
    }
    private static async Task<byte[]> Read(Stream stream, int limit, CancellationToken ct)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32BigEndian(header); if (length <= 0 || length > limit) throw new InvalidDataException("Bounded widget frame required.");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false); return bytes;
    }
    private static async Task Write(Stream stream, byte[] bytes, int limit, CancellationToken ct)
    {
        if (bytes.Length == 0 || bytes.Length > limit) throw new InvalidDataException("Bounded widget frame required.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, bytes.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false); await stream.WriteAsync(bytes, ct).ConfigureAwait(false); await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
