using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
namespace NineToOne.Os.Shell.Authority;

// Separate explicitly supervised administrator observation protocol. Neither a
// copied reference nor incoming JSON supplies the Home actor, lease or provider.
internal sealed record LinuxHomeOriginalWidgetChallenge(int Schema, Guid Correlation,
    string Command, string AppId, HomeNativeWidgetReference? OriginalReference);
internal sealed record LinuxHomeOriginalWidgetReply(int Schema, Guid Correlation, string LeaseIdentity,
    IReadOnlyList<HomeNativeWidgetResolution> Definitions, LinuxHomeOriginalWidgetSurfaceObservation? Surface);
internal sealed record LinuxHomeOriginalWidgetSurfaceObservation(HomeNativeWidgetReference Reference,
    string SurfaceReference, string AuthoredCui, Dictionary<string, JsonElement> Data);

[SupportedOSPlatform("linux")]
internal static class LinuxOriginalHomeWidgetObservation
{
    internal static async ValueTask<LinuxHomeOriginalWidgetReply> HandleForOriginalHomeAsync(
        LinuxHomeOriginalWidgetChallenge request, HomeNativeSessionLease originalLease,
        AuthenticatedResourceActor originalActor, IAuthenticatedResourceActorSource actors,
        HomeNativeWidgetRegistry actualWidgets, CancellationToken ct)
    {
        if (request is null || request.Schema != 5 || request.Correlation == Guid.Empty ||
            !Text(request.AppId) || request.Command is not ("list" or "capture") ||
            request.Command == "list" && request.OriginalReference is not null ||
            request.Command == "capture" && !Reference(request.OriginalReference, request.AppId))
            throw new InvalidDataException("Exact bounded original widget observation required.");
        async ValueTask RequireOriginal()
        {
            if (!originalLease.IsHeld || originalActor.OrganisationId is not null ||
                originalActor.ProfileId != originalLease.ProfileId ||
                originalActor != await actors.GetCurrentAsync(ct).ConfigureAwait(false) || !originalLease.IsHeld)
                throw new UnauthorizedAccessException("Original local Home widget context retired.");
        }
        await RequireOriginal().ConfigureAwait(false);
        IReadOnlyList<HomeNativeWidgetResolution> definitions = [];
        HomeNativeWidgetSurface? surface = null;
        if (request.Command == "list")
        {
            var actual = await actualWidgets.ListForActorAsync(originalActor, ct).ConfigureAwait(false);
            await RequireOriginal().ConfigureAwait(false);
            var selected = actual.Where(item => item.Reference.AppId == request.AppId).Take(17).ToArray();
            if (selected.Length > 16) throw new InvalidDataException("Bounded owner widget catalogue required.");
            definitions = Array.AsReadOnly(selected);
        }
        else
        {
            var reference = request.OriginalReference!;
            var resolved = await actualWidgets.ResolveForActorAsync(reference, originalActor, ct).ConfigureAwait(false);
            await RequireOriginal().ConfigureAwait(false);
            if (resolved is not null)
            {
                surface = await actualWidgets.CaptureAsync(reference, originalActor,
                    resolved.Definition.DefaultSize, 320, 120, ct).ConfigureAwait(false);
                await RequireOriginal().ConfigureAwait(false);
            }
        }
        await RequireOriginal().ConfigureAwait(false);
        return new(5, request.Correlation, originalLease.LeaseIdentity.ToString("N"), definitions, surface is null ? null : new(surface.Reference,
            surface.SurfaceReference, surface.AuthoredCui, surface.Data.ToDictionary(pair => pair.Key, pair => pair.Value.Clone())));
    }
    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 4096;
    private static bool Reference(HomeNativeWidgetReference? value, string appId) => value is not null &&
        value.AppId == appId && value.InstalledApplicationId != Guid.Empty && Text(value.InstallationRevision) &&
        Text(value.WidgetId) && Text(value.DefinitionRevision);
}

[SupportedOSPlatform("linux")]
internal static class LinuxRootOriginalHomeWidgetObservation
{
    internal static async Task<LinuxHomeOriginalWidgetReply> ReadAsync(Socket actualAcceptedHomeSocket,
        LinuxRootSupervisedHome originalHome, LinuxRootSupervisedInstalledWidgetOwner originalOwner,
        HomeNativeWidgetReference? originalReference, CancellationToken ct)
    {
        var gate = LinuxRootOriginalHomeCanonicalReader.OriginalSocketGate(actualAcceptedHomeSocket);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var context = originalHome.ObserveOriginalContext();
            async Task RequireOriginal(CancellationToken token)
            {
                if (await new OperatingSystemPrincipalSource().GetPrincipalAsync(token).ConfigureAwait(false) != "unix-euid:0" ||
                    !ReferenceEquals(originalOwner.OriginalHomeContext, context) ||
                    HomeNativePeerObservation.FromAcceptedUnixSocket(actualAcceptedHomeSocket) != context.OriginalHostPeer ||
                    !await originalHome.IsOriginalIssuedContextCurrentAsync(context, token).ConfigureAwait(false) ||
                    !await originalOwner.IsOriginalCurrentAsync(token).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("Same actual root-issued Home and independent owner required.");
            }
            await RequireOriginal(ct).ConfigureAwait(false);
            var appId = originalOwner.OriginalOwnerAppId;
            if (string.IsNullOrWhiteSpace(appId) || appId.Length > 4096 ||
                originalReference is not null && (originalReference.AppId != appId ||
                    originalReference.InstalledApplicationId == Guid.Empty ||
                    string.IsNullOrWhiteSpace(originalReference.InstallationRevision) || originalReference.InstallationRevision.Length > 4096 ||
                    string.IsNullOrWhiteSpace(originalReference.WidgetId) || originalReference.WidgetId.Length > 4096 ||
                    string.IsNullOrWhiteSpace(originalReference.DefinitionRevision) || originalReference.DefinitionRevision.Length > 4096))
                throw new UnauthorizedAccessException("Original owner widget reference required.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            using var stream = new NetworkStream(actualAcceptedHomeSocket, ownsSocket: false);
            var correlation = Guid.NewGuid();
            await LinuxHomeChildLeaseChannel.WriteAsync(stream, new LinuxHomeOriginalWidgetChallenge(5,
                correlation, originalReference is null ? "list" : "capture", appId, originalReference), deadline.Token).ConfigureAwait(false);
            await RequireOriginal(deadline.Token).ConfigureAwait(false);
            var reply = await LinuxHomeChildLeaseChannel.ReadAsync<LinuxHomeOriginalWidgetReply>(stream, deadline.Token).ConfigureAwait(false);
            if (reply.Schema != 5 || reply.Correlation != correlation || reply.LeaseIdentity != context.SessionLeaseIdentity ||
                reply.Definitions is null || reply.Definitions.Count > 16 ||
                reply.Definitions.Any(item => item is null || item.Reference is null || item.Reference.AppId != appId || item.Reference.InstalledApplicationId == Guid.Empty ||
                    item.Definition is null || item.Definition.WidgetId != item.Reference.WidgetId ||
                    item.Definition.Revision != item.Reference.DefinitionRevision) ||
                originalReference is null && reply.Surface is not null ||
                originalReference is not null && (reply.Definitions.Count != 0 || reply.Surface is not null && reply.Surface.Reference != originalReference))
                throw new InvalidDataException("Original authenticated widget observation reply required.");
            if (reply.Surface is { } received)
            {
                // Validate and detach the bounded observation using the maintained owner
                // surface constructor; JSON never invokes a private issuance constructor.
                var bounded = HomeNativeWidgetSurface.Capture(received.Reference, received.SurfaceReference,
                    received.AuthoredCui, received.Data);
                reply = reply with { Surface = new(bounded.Reference, bounded.SurfaceReference,
                    bounded.AuthoredCui, bounded.Data.ToDictionary(pair => pair.Key, pair => pair.Value.Clone())) };
            }
            await RequireOriginal(deadline.Token).ConfigureAwait(false);
            return reply;
        }
        finally { gate.Release(); }
    }
}
