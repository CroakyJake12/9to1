namespace HavenOS.Files;

public sealed partial class DurableDriveProvider
{
    /// <summary>Caller-owned raw repository custody for original execution binding reads.
    /// Ordinary entry points and their permission/visibility policy remain unchanged.</summary>
    public async Task<FilesResult<HostedItemMetadata>> GetForOriginalStoreAsync(Guid expectedStoreId,
        HostedItemId fileId, CancellationToken cancellationToken,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask)
    {
        var state = await FilesOriginalDeveloperTaskSource.ObserveAsync(
            () => _store.ReadExistingAsync(cancellationToken), originalSynchronousScope, retainOriginalTask).ConfigureAwait(false);
        FilesResult<HostedItemMetadata>? result = null;
        FilesOriginalDeveloperTaskSource.Invoke(() =>
        {
            cancellationToken.ThrowIfCancellationRequested(); RequireOriginalReadStore(state, expectedStoreId);
            var item = state.Items.SingleOrDefault(entry => entry.Metadata.Id == fileId);
            if (item is null || !IsVisible(state, item))
                result = Fail<HostedItemMetadata>(FilesErrorCode.ItemNotFound, "Original item is unavailable.", "GetOriginalItem", fileId);
            else { RequireOriginalReadItem(item.Metadata); result = FilesResult<HostedItemMetadata>.Success(item.Metadata); }
        }, originalSynchronousScope);
        return result ?? throw new InvalidOperationException("No actual original metadata observation exists.");
    }
}
