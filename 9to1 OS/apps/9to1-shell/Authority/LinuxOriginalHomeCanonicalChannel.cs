using System.Net.Sockets;
using System.Text.Json;
using System.Runtime.Versioning;
using Haven.Application;
using HavenOS.Home.Core;

namespace NineToOne.Os.Shell.Authority;

// Separate future protocol; schema1 supervised Home gate is unchanged.
internal sealed record LinuxHomeCanonicalChallenge(int Schema, Guid Correlation, string SignedDesktopIdentity);
internal sealed record LinuxHomeCanonicalReply(int Schema, Guid Correlation, string LeaseIdentity,
    AuthenticatedResourceActor OriginalHomeActor, LinuxOriginalHomeCanonicalObservation CanonicalObservation);

internal sealed record LinuxHomeWidgetRouteObservation(int Schema, string LeaseIdentity,
    AuthenticatedResourceActor OriginalHomeActor, HomeNativeEndpointLocation Location);

[SupportedOSPlatform("linux")]
internal static class LinuxOriginalHomeCanonicalChannel
{
    internal static async Task ServeAsync(string protectedAdministratorSocket,
        HomeNativeSessionLease originalLease, IAuthenticatedResourceActorSource actors,
        IInstalledApplicationRegistry canonicalRegistry, ResourceAuthorizationService resources,
        CancellationToken ct) => await ServeBoundAsync(protectedAdministratorSocket, originalLease, actors, canonicalRegistry, resources, null, null, false, ct);

    internal static Task ServeOriginalTupleWithWidgetRouteAsync(string protectedAdministratorSocket,
        HomeNativeSessionLease originalLease, IAuthenticatedResourceActorSource actors,
        IInstalledApplicationRegistry canonicalRegistry, ResourceAuthorizationService resources,
        HomeNativeEndpointLocation originalWidgetLocation, CancellationToken ct) =>
        ServeBoundAsync(protectedAdministratorSocket, originalLease, actors, canonicalRegistry, resources, originalWidgetLocation, null, false, ct);
    internal static Task ServeOriginalWidgetObservationsAsync(string protectedAdministratorSocket,
        HomeNativeSessionLease originalLease, IAuthenticatedResourceActorSource actors,
        IInstalledApplicationRegistry canonicalRegistry, ResourceAuthorizationService resources,
        HomeNativeWidgetRegistry actualWidgets, CancellationToken ct) =>
        ServeBoundAsync(protectedAdministratorSocket, originalLease, actors, canonicalRegistry, resources, null, actualWidgets, true, ct);
    private static async Task ServeBoundAsync(string protectedAdministratorSocket,
        HomeNativeSessionLease originalLease, IAuthenticatedResourceActorSource actors,
        IInstalledApplicationRegistry canonicalRegistry, ResourceAuthorizationService resources,
        HomeNativeEndpointLocation? originalWidgetLocation, HomeNativeWidgetRegistry? actualWidgets, bool widgetOnly, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux() || !Path.IsPathFullyQualified(protectedAdministratorSocket) ||
            Path.GetFullPath(protectedAdministratorSocket) != protectedAdministratorSocket ||
            !LinuxRootOwnedFiles.DirectoryImmutable(Path.GetDirectoryName(protectedAdministratorSocket)!))
            throw new UnauthorizedAccessException("Original protected administrator endpoint required.");
        var original = await actors.GetCurrentAsync(ct).ConfigureAwait(false);
        if (original is null || original.OrganisationId is not null || original.ProfileId != originalLease.ProfileId ||
            !originalLease.IsHeld) throw new UnauthorizedAccessException("Original actual Home context required.");
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(protectedAdministratorSocket), ct).ConfigureAwait(false);
        if (HomeNativePeerObservation.FromAcceptedUnixSocket(socket)?.OperatingSystemPrincipalId != "unix-euid:0")
            throw new UnauthorizedAccessException("Actual kernel administrator peer required.");
        using var stream = new NetworkStream(socket, ownsSocket: false);
        if (originalWidgetLocation is not null)
        {
            if (originalWidgetLocation.SchemaVersion != 1 || originalWidgetLocation.HostProcessId != Environment.ProcessId ||
                originalWidgetLocation.ProfileId != original.ProfileId || originalWidgetLocation.Epoch == Guid.Empty ||
                !Path.IsPathFullyQualified(originalWidgetLocation.SocketPath) || !originalLease.IsHeld ||
                original != await actors.GetCurrentAsync(ct).ConfigureAwait(false) || !originalLease.IsHeld)
                throw new UnauthorizedAccessException("Original actual widget listener route unavailable.");
            // Actual listener routing only; connected peer/installed/resource checks still mandatory.
            await LinuxHomeChildLeaseChannel.WriteAsync(stream, new LinuxHomeWidgetRouteObservation(4,
                originalLease.LeaseIdentity.ToString("N"), original, originalWidgetLocation), ct).ConfigureAwait(false);
            if (!originalLease.IsHeld || original != await actors.GetCurrentAsync(ct).ConfigureAwait(false) || !originalLease.IsHeld)
                throw new UnauthorizedAccessException("Original Home retired after routing observation.");
        }
        while (true)
        {
            var frame = await LinuxOriginalControlFrameReader.ReadAsync<JsonElement>(stream, ct).ConfigureAwait(false);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(60));
            
            if (frame.ValueKind != JsonValueKind.Object || !frame.TryGetProperty("Schema", out var schema) ||
                !schema.TryGetInt32(out var schemaValue)) throw new InvalidDataException("Exact typed original observation schema required.");
            if (schemaValue == 5)
            {
                if (actualWidgets is null || !widgetOnly)
                    throw new UnauthorizedAccessException("Actual supervised widget graph unavailable.");
                var request = frame.Deserialize<LinuxHomeOriginalWidgetChallenge>()
                    ?? throw new InvalidDataException("Original typed widget challenge required.");
                var reply = await LinuxOriginalHomeWidgetObservation.HandleForOriginalHomeAsync(request, originalLease,
                    original, actors, actualWidgets, deadline.Token).ConfigureAwait(false);
                if (!originalLease.IsHeld || original != await actors.GetCurrentAsync(deadline.Token).ConfigureAwait(false) || !originalLease.IsHeld)
                    throw new UnauthorizedAccessException("Original Home retired before widget observation reply.");
                await LinuxHomeChildLeaseChannel.WriteAsync(stream, reply, deadline.Token).ConfigureAwait(false);
                if (!originalLease.IsHeld || original != await actors.GetCurrentAsync(deadline.Token).ConfigureAwait(false) || !originalLease.IsHeld)
                    throw new UnauthorizedAccessException("Original Home retired after widget observation reply.");
                continue;
            }
            if (widgetOnly) throw new InvalidDataException("Dedicated original widget observation channel required.");
            var challenge = frame.Deserialize<LinuxHomeCanonicalChallenge>()
                ?? throw new InvalidDataException("Original canonical challenge required.");
            if (challenge.Schema != 2 || challenge.Correlation == Guid.Empty)
                throw new InvalidDataException("Original canonical observation protocol required.");
            var canonical = await LinuxOriginalHomeCanonicalObserver.ReadAsync(originalLease, original, actors,
                canonicalRegistry, resources, challenge.SignedDesktopIdentity, deadline.Token).ConfigureAwait(false);
            if (canonical is null || original != await actors.GetCurrentAsync(deadline.Token).ConfigureAwait(false) ||
                !originalLease.IsHeld) throw new UnauthorizedAccessException("Original canonical Home observation unavailable.");
            await LinuxHomeChildLeaseChannel.WriteAsync(stream, new LinuxHomeCanonicalReply(2, challenge.Correlation,
                originalLease.LeaseIdentity.ToString("N"), original, canonical), deadline.Token).ConfigureAwait(false);
            if (!originalLease.IsHeld || original != await actors.GetCurrentAsync(deadline.Token).ConfigureAwait(false) ||
                !originalLease.IsHeld) throw new UnauthorizedAccessException("Original Home retired after observation reply.");
        }
    }
}
