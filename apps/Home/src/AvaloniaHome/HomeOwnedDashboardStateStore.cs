using System;
using System.Threading;
using System.Threading.Tasks;
using Haven.Application;
using HavenOS.Home.Core;

namespace AvaloniaHome;

/// <summary>Borrowed adapter for Home's own dashboard records. Every byte remains in the
/// original Home store; the original OS profile guards the store's final commit.</summary>
internal sealed class HomeOwnedDashboardStateStore(IHomeCoreStateStore originalStore,
    HomeLocalProfileIdentity originalProfiles, AuthenticatedResourceActor originalActor) : IHomeCoreStateStore
{
    private async Task RequireOriginalAsync(CancellationToken token)
    {
        if (await originalProfiles.GetCurrentAsync(token).ConfigureAwait(false) != originalActor)
            throw new UnauthorizedAccessException("The original Home profile changed. Reopen Home.");
    }

    public async Task<HomeStateReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        await RequireOriginalAsync(cancellationToken).ConfigureAwait(false);
        var actual = originalStore.ReadAsync(cancellationToken);
        HomeStateReadResult result;
        try { result = await actual.ConfigureAwait(false); }
        catch when (actual.IsFaulted) { throw actual.Exception!; }
        await RequireOriginalAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expectedRecordRevision,
        CancellationToken cancellationToken = default)
    {
        if (record.RecordType is not ("home.dashboard.layout" or "home.dashboard.layout.revision" or "home.dashboard.layout.conflict")
            || record.Scope != HomeDataScope.DeviceLocal || record.Authority != HomeRecordAuthority.LocalCanonical
            || record.SchemaVersion != 1 || (record.RecordType == "home.dashboard.layout"
                ? record.RecordId != record.RecordType : !record.RecordId.StartsWith(record.RecordType + ".", StringComparison.Ordinal)))
            throw new UnauthorizedAccessException("The Home dashboard adapter cannot write another domain's records.");
        await RequireOriginalAsync(cancellationToken).ConfigureAwait(false);
        var actual = originalStore.WriteGuardedAsync(record, expectedRecordRevision, originalActor,
            originalProfiles, cancellationToken);
        try { return await actual.ConfigureAwait(false); }
        catch when (actual.IsFaulted) { throw actual.Exception!; }
    }
}
