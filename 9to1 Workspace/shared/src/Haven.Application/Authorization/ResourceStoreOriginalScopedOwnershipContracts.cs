namespace Haven.Application;

/// <summary>Optional SAME ownership issuer. Carries original callback/task custody through
/// current actor, Home binding and receipt reads; issues no store or object access grant.</summary>
public interface IResourceStoreOriginalScopedOwnershipAuthority : IResourceStoreOwnershipReceiptAuthority
{
    ValueTask<VerifiedResourceStoreOwnership?> GetVerifiedWithinOriginalSourceAsync(string resourceKind,
        string storeId, Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        CancellationToken cancellationToken);
    ValueTask<bool> IsCurrentWithinOriginalSourceAsync(VerifiedResourceStoreOwnership captured,
        AuthenticatedResourceActor expectedActor, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken cancellationToken);
}
