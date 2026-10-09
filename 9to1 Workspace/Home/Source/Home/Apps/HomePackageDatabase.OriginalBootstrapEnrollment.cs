using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Home.Apps;

/// <summary>Versioned observation in the SAME canonical package registry. Detached
/// records are neither publisher trust nor installed/runtime authority. The original
/// native producer independently authenticates its installer and protected store.</summary>
public sealed record HomePackageOriginalPublisherEnrollmentRecord(int SchemaVersion,
    string PublisherCertificateSha256, string IssuerKeyId, string CatalogueRevision,
    string CatalogueSha256, string InstallerExecutableIdentity, Guid OriginalEnrollmentOperationId,
    string OriginalActorId, string OriginalProfileId, string OriginalOsPrincipal,
    string HomePackageId, string RootPackageId, IReadOnlyList<string> SelectedPackageIds,
    DateTimeOffset EnrolledAtUtc, string? OriginalSignedCatalogueBase64 = null)
{
    // Schema 2 retains exact installer-authenticated bytes in this SAME protected
    // registry. Root verifies them against its actual enrolled signer; no key is stored.
    public const int RetainedCatalogueSchemaVersion = 2;
    public const int MaximumRetainedCatalogueBytes = 4 * 1024 * 1024;
    public const string Field = "home.original-publisher-enrollment.v1";
}

public sealed partial class HomePackageDatabase
{
    public bool HasOriginalProfileComposition(IHomeCoreStateStore actualStore, HomeLocalProfileIdentity actualProfiles) =>
        ReferenceEquals(_store, actualStore) && actualProfiles.IsBoundToStore(actualStore);

    public Task<HomePackageDatabaseReadResult> ReadOriginalBootstrapWithinSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        ReadOriginalBootstrapCoreAsync(token, new HomeOwnershipOriginalSourceCallbacks(scope, retain));

    /// <summary>The existing guarded CAS with original nested task custody. This is
    /// storage only: its result issues no trust. The actual installer/root source
    /// must validate the exact enrollment, its current kernel/publisher and policy.</summary>
    public Task<HomePackageDatabaseWriteResult> SaveOriginalBootstrapEnrollmentWithinSourceAsync(
        HomePackageDatabaseSnapshot sameCapturedRegistry, long expectedRevision,
        AuthenticatedResourceActor originalActor, IHomeStateCommitActorGuard actualOriginalGuard,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalActor); ArgumentNullException.ThrowIfNull(actualOriginalGuard);
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var source = new HomeOwnershipOriginalSourceCallbacks(scope, retain);
        var captured = source.Invoke(() => CaptureGuardedSnapshot(sameCapturedRegistry));
        return SaveCoreAsync(captured, expectedRevision, originalActor, actualOriginalGuard, token, source);
    }

    private async Task WaitOriginalBootstrapPackageGateAsync(HomeOwnershipOriginalSourceCallbacks? source, CancellationToken token)
    {
        if (source is null) { await _writeGate.WaitAsync(token).ConfigureAwait(false); return; }
        Task? actual = null;
        try
        {
            await source.ReadAsync(async () =>
            {
                actual = _writeGate.WaitAsync(token); source.Retain(actual);
                await actual.ConfigureAwait(false); return true;
            }).ConfigureAwait(false);
        }
        catch (Exception primary)
        {
            Exception? cleanup = null;
            if (actual?.IsCompletedSuccessfully == true)
                try { source.Run(() => _writeGate.Release()); } catch (Exception cause) { cleanup = cause; }
            if (cleanup is not null) throw new AggregateException("Original package acquisition and independent release failed.", primary, cleanup);
            throw;
        }
    }
}
