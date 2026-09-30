namespace Haven.Application;

/// <summary>A verified personal store binding. It grants no object access or organisation authority.</summary>
public sealed record VerifiedResourceStoreOwnership(string ResourceKind, string StoreId, string ProfileId,
    string ObservedStoreRevision);

/// <summary>Queries Home's existing binding against the actual owning repository. Unknown or unimported stores deny.</summary>
public interface IResourceStoreOwnershipAuthority
{
    ValueTask<VerifiedResourceStoreOwnership?> GetVerifiedAsync(string resourceKind, string storeId,
        CancellationToken cancellationToken);
}
