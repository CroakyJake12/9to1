namespace Haven.Application.Compatibility;

/// <summary>Observed canonical source revision. A content hash does not establish publisher trust or installation.</summary>
public sealed record CompatibilityPackageSource(Guid FileId, string ContentRevision, string MetadataRevision, string Name, long Length,
    string Sha256, AuthenticatedResourceActor ObservedActor)
{
    /// <summary>Actual owning Files store UUID captured with the original selection; empty is never admissible.</summary>
    public required Guid StoreId { get; init; }
}

/// <summary>
/// Files owns a read-only immutable content lease, admitted under os.compatibility.package.read.
/// This in-process interface is not a serializable path, access token or executable package handle.
/// The owner must enforce its original actor and current canonical resource admission on every revalidation.
/// </summary>
public interface ICompatibilityPackageContentLease : IAsyncDisposable
{
    CompatibilityPackageSource Source { get; }
    /// <summary>Owner-controlled immutable bytes. Stream disposal does not release the parent lease.</summary>
    ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken);
    /// <summary>Rejects actor/profile/session, canonical revision or Files access changes.</summary>
    ValueTask RevalidateAsync(CancellationToken cancellationToken);
}

/// <summary>Implemented by the Files owner. Checks original expected actor and store before lookup/materialization and bounds copy before allocation; absent owner means inspection is unavailable.</summary>
public interface ICompatibilityPackageContentSource
{
    ValueTask<ICompatibilityPackageContentLease> ReadAsync(Guid expectedStoreId, AuthenticatedResourceActor expectedActor, Guid fileId, string expectedContentRevision,
        long maximumBytes, CancellationToken cancellationToken);
}
