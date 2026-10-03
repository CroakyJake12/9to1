using System.Globalization;
using Haven.Application;
using HavenOS.Home.Core;

namespace NineToOne.Os.Shell.Authority;

// Observation from the actual Home child only. The administrator must independently
// bind it to the original connected child, signed receipt, runtime and held lease.
// This record is never an installed-peer issuance or a resource authorization grant.
internal sealed record LinuxOriginalHomeCanonicalObservation(Guid ApplicationId, long ApplicationRevision,
    string RegistryRecordId, long RegistryRevision, string DesktopIdentity, string DesktopEntryDigest);

internal static class LinuxOriginalHomeCanonicalObserver
{
    internal static async ValueTask<LinuxOriginalHomeCanonicalObservation?> ReadAsync(
        HomeNativeSessionLease originalLease, AuthenticatedResourceActor originalActor,
        IAuthenticatedResourceActorSource actors, IInstalledApplicationRegistry registry,
        ResourceAuthorizationService resources, string signedDesktopIdentity, CancellationToken ct)
    {
        if (originalActor is null || originalActor.OrganisationId is not null ||
            originalActor.ProfileId != originalLease.ProfileId || !originalLease.IsHeld ||
            string.IsNullOrWhiteSpace(signedDesktopIdentity) || signedDesktopIdentity.Length > 4096 ||
            registry is not IInstalledApplicationOriginalReadRegistry originalRead ||
            originalActor != await actors.GetCurrentAsync(ct).ConfigureAwait(false) || !originalLease.IsHeld)
            return null;
        var saved = await originalRead.ReadExistingForActorAsync(originalActor, ct).ConfigureAwait(false);
        if (saved is null || originalActor != await actors.GetCurrentAsync(ct).ConfigureAwait(false) ||
            !originalLease.IsHeld) return null;
        var candidates = saved.Applications.Where(a => a.ProviderId == "linux.xdg-desktop" &&
            a.OsApplicationId == signedDesktopIdentity && a.HomeProfileId == originalActor.ProfileId &&
            a.Enabled && a.ProfileAccessible).Take(2).ToArray();
        if (candidates.Length != 1) return null;
        var app = candidates[0];
        if (app.Version is not { Length: 64 } digest || digest.Any(c => !char.IsAsciiHexDigit(c))) return null;
        var scope = new ResourceScope("os.installed-application", app.ApplicationId.ToString("D"),
            app.Revision.ToString(CultureInfo.InvariantCulture), ResourceAccess.Read);
        if (await resources.AuthorizeForActorAsync(originalActor, "os.application.read", [scope], ct)
                .ConfigureAwait(false) != originalActor ||
            originalActor != await actors.GetCurrentAsync(ct).ConfigureAwait(false) || !originalLease.IsHeld)
            return null;
        // No initialization, reconciliation, launch or write occurs in this observer.
        return new(app.ApplicationId, app.Revision, saved.RegistryRecordId, saved.RegistryRevision,
            signedDesktopIdentity, digest);
    }
}
