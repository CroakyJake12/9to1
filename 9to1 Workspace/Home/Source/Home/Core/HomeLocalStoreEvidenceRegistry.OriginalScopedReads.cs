namespace HavenOS.Home.Core;

public sealed partial class HomeLocalStoreEvidenceRegistry : IHomeOriginalScopedLocalStoreEvidenceSource
{
    public async ValueTask<HomeLocalStoreEvidence?> ReadWithinOriginalSourceAsync(string kind, string storeId,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        var source = new HomeOwnershipOriginalSourceCallbacks(originalSynchronousScope, retainOriginalTask);
        var owners = source.Invoke(() => _providers.Where(provider => provider.ResourceKind == kind).ToArray());
        if (owners.Length != 1) return null;
        if (owners[0] is not IHomeOriginalScopedLocalStoreEvidenceProvider actual)
            throw new InvalidOperationException("HOME_OWNERSHIP_SCOPE_REQUIRED: legacy evidence is unavailable for original nested source custody.");
        return await source.ReadAsync(() => actual.ReadWithinOriginalSourceAsync(storeId, source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
    }
}
