namespace HavenOS.Home.Core;

/// <summary>Optional actual owning-store evidence source. Legacy unscoped evidence cannot
/// establish nested original callback custody for this path.</summary>
public interface IHomeOriginalScopedLocalStoreEvidenceSource : IHomeLocalStoreEvidenceSource
{
    ValueTask<HomeLocalStoreEvidence?> ReadWithinOriginalSourceAsync(string resourceKind, string storeId,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        CancellationToken cancellationToken);
}

public interface IHomeOriginalScopedLocalStoreEvidenceProvider : IHomeLocalStoreEvidenceProvider
{
    ValueTask<HomeLocalStoreEvidence?> ReadWithinOriginalSourceAsync(string storeId,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        CancellationToken cancellationToken);
}
