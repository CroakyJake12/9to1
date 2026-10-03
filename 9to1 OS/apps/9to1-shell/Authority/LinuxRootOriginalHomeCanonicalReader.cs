using System.Runtime.CompilerServices;
using System.Net.Sockets;
using System.Runtime.Versioning;
using HavenOS.Home.Core;

namespace NineToOne.Os.Shell.Authority;

// Administrator-only consumer of the SAME already admitted Home process. The caller
// obtains signedDesktopIdentity from the retained protected signed preparation, never
// from an owner registration request. Observation is not atomic through later I/O.
[SupportedOSPlatform("linux")]
internal static class LinuxRootOriginalHomeCanonicalReader
{
    private static readonly ConditionalWeakTable<Socket, SemaphoreSlim> OriginalReaders = new();
    internal static SemaphoreSlim OriginalSocketGate(Socket actualSocket) => OriginalReaders.GetValue(actualSocket, static _ => new(1, 1));
    internal static async Task<HomeNativeEndpointLocation?> ReadOriginalWidgetRouteAsync(Socket actualAcceptedHomeSocket,
        LinuxRootSupervisedHome originalHome, CancellationToken ct)
    {
        var gate = OriginalSocketGate(actualAcceptedHomeSocket);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct).ConfigureAwait(false) != "unix-euid:0") return null;
            var context = originalHome.ObserveOriginalContext();
            if (HomeNativePeerObservation.FromAcceptedUnixSocket(actualAcceptedHomeSocket) != context.OriginalHostPeer ||
                !await originalHome.IsOriginalIssuedContextCurrentAsync(context, ct).ConfigureAwait(false)) return null;
            using var stream = new NetworkStream(actualAcceptedHomeSocket, ownsSocket: false);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var reply = await LinuxHomeChildLeaseChannel.ReadAsync<LinuxHomeWidgetRouteObservation>(stream, deadline.Token).ConfigureAwait(false);
            var route = reply.Location;
            if (reply.Schema != 4 || reply.LeaseIdentity != context.SessionLeaseIdentity ||
                reply.OriginalHomeActor != originalHome.OriginalHomeActorObservation || route is null ||
                route.SchemaVersion != 1 || route.ProfileId != context.ProfileId || route.Epoch == Guid.Empty ||
                route.HostProcessId != context.OriginalHostPeer.ProcessId ||
                !Path.IsPathFullyQualified(route.SocketPath) || Path.GetFullPath(route.SocketPath) != route.SocketPath ||
                !Path.IsPathFullyQualified(route.LocatorPath) || Path.GetFullPath(route.LocatorPath) != route.LocatorPath ||
                HomeNativePeerObservation.FromAcceptedUnixSocket(actualAcceptedHomeSocket) != context.OriginalHostPeer ||
                !await originalHome.IsOriginalIssuedContextCurrentAsync(context, deadline.Token).ConfigureAwait(false)) return null;
            // Route is not a peer/resource grant. Owner connects and verifies SAME original Home independently.
            return route;
        }
        finally { gate.Release(); }
    }
    internal static Task<LinuxOriginalHomeCanonicalObservation?> ReadAsync(Socket actualAcceptedHomeSocket,
        LinuxRootSupervisedHome originalHome, LinuxRootHomeStartPreparation actualSignedPreparation,
        CancellationToken ct) => ReadBoundAsync(actualAcceptedHomeSocket, originalHome,
            actualSignedPreparation.SignedDesktopIdentity, actualSignedPreparation.SignedDesktopEntryDigest,
            actualSignedPreparation.IsCurrentForAdministratorAsync, ct);
    internal static Task<LinuxOriginalHomeCanonicalObservation?> ReadOwnerAsync(Socket actualAcceptedHomeSocket,
        LinuxRootSupervisedHome originalHome, LinuxRootInstalledWidgetOwnerStartPreparation actualSignedPreparation,
        CancellationToken ct) => ReadBoundAsync(actualAcceptedHomeSocket, originalHome,
            actualSignedPreparation.SignedDesktopIdentity, actualSignedPreparation.SignedDesktopEntryDigest,
            actualSignedPreparation.IsCurrentForAdministratorAsync, ct);
    private static async Task<LinuxOriginalHomeCanonicalObservation?> ReadBoundAsync(Socket actualAcceptedHomeSocket,
        LinuxRootSupervisedHome originalHome, string signedDesktopIdentity, string signedDesktopDigest,
        Func<CancellationToken, Task<bool>> signedPreparationCurrent, CancellationToken ct)
    {
        var gate = OriginalSocketGate(actualAcceptedHomeSocket);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await ReadOriginalAsync(actualAcceptedHomeSocket, originalHome, signedDesktopIdentity,
            signedDesktopDigest, signedPreparationCurrent, ct).ConfigureAwait(false); }
        finally { gate.Release(); }
    }
    private static async Task<LinuxOriginalHomeCanonicalObservation?> ReadOriginalAsync(Socket actualAcceptedHomeSocket,
        LinuxRootSupervisedHome originalHome, string signedDesktopIdentity, string signedDesktopDigest,
        Func<CancellationToken, Task<bool>> signedPreparationCurrent, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(signedDesktopIdentity) ||
            await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct).ConfigureAwait(false) != "unix-euid:0" ||
            !await signedPreparationCurrent(ct).ConfigureAwait(false)) return null;
        var context = originalHome.ObserveOriginalContext();
        if (HomeNativePeerObservation.FromAcceptedUnixSocket(actualAcceptedHomeSocket) != context.OriginalHostPeer ||
            !await originalHome.IsOriginalIssuedContextCurrentAsync(context, ct).ConfigureAwait(false)) return null;
        // Own no replacement lease and mint no process identity from wire fields.
        using var stream = new NetworkStream(actualAcceptedHomeSocket, ownsSocket: false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var correlation = Guid.NewGuid();
        await LinuxHomeChildLeaseChannel.WriteAsync(stream,
            new LinuxHomeCanonicalChallenge(2, correlation, signedDesktopIdentity), deadline.Token).ConfigureAwait(false);
        var reply = await LinuxHomeChildLeaseChannel.ReadAsync<LinuxHomeCanonicalReply>(stream, deadline.Token)
            .ConfigureAwait(false);
        var canonical = reply.CanonicalObservation;
        if (reply.Schema != 2 || reply.Correlation != correlation || reply.LeaseIdentity != context.SessionLeaseIdentity ||
            reply.OriginalHomeActor != originalHome.OriginalHomeActorObservation || canonical is null ||
            canonical.ApplicationId == Guid.Empty || canonical.ApplicationRevision < 1 || canonical.RegistryRevision < 1 ||
            string.IsNullOrWhiteSpace(canonical.RegistryRecordId) || canonical.RegistryRecordId.Length > 4096 ||
            canonical.DesktopIdentity != signedDesktopIdentity ||
            !string.Equals(canonical.DesktopEntryDigest, signedDesktopDigest,
                StringComparison.OrdinalIgnoreCase) ||
            !await signedPreparationCurrent(deadline.Token).ConfigureAwait(false) ||
            !await originalHome.IsOriginalIssuedContextCurrentAsync(context, deadline.Token).ConfigureAwait(false)) return null;
        return canonical;
    }
}
