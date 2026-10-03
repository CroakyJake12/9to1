namespace Haven.Application;

/// <summary>A verified personal store binding. It grants no object access or organisation authority.</summary>
public sealed record VerifiedResourceStoreOwnership(string ResourceKind, string StoreId, string ProfileId,
    string ObservedStoreRevision)
{
    public ResourceStoreBindingReceipt? Receipt { get; init; }
}

public sealed record ResourceStoreBindingReceipt(long Revision, string Fingerprint);

/// <summary>Rechecks only the Home binding and current actor; never re-enters the owning repository.
/// This observation does not make two independent stores one atomic transaction.</summary>
public interface IResourceStoreOwnershipReceiptAuthority : IResourceStoreOwnershipAuthority
{
    ValueTask<bool> IsCurrentAsync(VerifiedResourceStoreOwnership captured,
        AuthenticatedResourceActor expectedActor, CancellationToken cancellationToken);
}

/// <summary>Queries Home's existing binding against the actual owning repository. Unknown or unimported stores deny.</summary>
public interface IResourceStoreOwnershipAuthority
{
    ValueTask<VerifiedResourceStoreOwnership?> GetVerifiedAsync(string resourceKind, string storeId,
        CancellationToken cancellationToken);
}
