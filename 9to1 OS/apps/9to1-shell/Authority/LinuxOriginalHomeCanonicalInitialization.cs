using Haven.Application;
using HavenOS.Home.Core;

namespace NineToOne.Os.Shell.Authority;

// Explicit designated-Home startup reconciliation, separate from readonly capture.
// Platform discovery records metadata only; no installed-publisher/runtime grant.
internal static class LinuxOriginalHomeCanonicalInitialization
{
    internal static async Task<bool> ReconcileForDesignatedHomeAsync(HomeNativeSessionLease originalLease,
        IAuthenticatedResourceActorSource actors, IInstalledApplicationRegistry registry, CancellationToken ct)
    {
        if (!originalLease.IsHeld || registry is not IInstalledApplicationOriginalActorRegistry originalRegistry)
            return false;
        var original = await actors.GetCurrentAsync(ct).ConfigureAwait(false);
        if (original is null || original.OrganisationId is not null ||
            original.ProfileId != originalLease.ProfileId || !originalLease.IsHeld) return false;
        // The exact original registry port permits genuine first initialization; it
        // owns provider observations and original-actor guarded canonical writes.
        await originalRegistry.RefreshForActorAsync(original, ct).ConfigureAwait(false);
        return originalLease.IsHeld && original == await actors.GetCurrentAsync(ct).ConfigureAwait(false) &&
            originalLease.IsHeld;
    }
}
