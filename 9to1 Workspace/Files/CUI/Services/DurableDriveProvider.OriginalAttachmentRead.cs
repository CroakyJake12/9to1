using System.Security.Cryptography;
using System.Text.Json;

namespace HavenOS.Files;

public sealed record FilesOriginalAttachmentMetadata(HostedItemMetadata Row, string StoreRevision);

public sealed partial class DurableDriveProvider
{
    public async Task<FilesResult<FilesOriginalAttachmentMetadata>> GetOriginalAttachmentMetadataWithinSourceAsync(
        Guid expectedStoreId, HostedItemId fileId, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var state = await FilesOriginalDeveloperTaskSource.ObserveAsync(
            () => _store.ReadExistingWithinOriginalSourceAsync(scope, retain, token), scope, retain).ConfigureAwait(false);
        FilesResult<FilesOriginalAttachmentMetadata>? result = null;
        FilesOriginalDeveloperTaskSource.Invoke(() =>
        {
            token.ThrowIfCancellationRequested(); RequireOriginalReadStore(state, expectedStoreId);
            var item = state.Items.SingleOrDefault(entry => entry.Metadata.Id == fileId);
            if (item is null || !IsVisible(state, item))
                result = Fail<FilesOriginalAttachmentMetadata>(FilesErrorCode.ItemNotFound, "The selected file is unavailable.", "AttachmentRead", fileId);
            else { RequireOriginalReadItem(item.Metadata); result = FilesResult<FilesOriginalAttachmentMetadata>.Success(new(item.Metadata,
                Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(state))))); }
        }, scope);
        return result ?? throw new InvalidOperationException("The original attachment metadata source produced no result.");
    }
    public Task? OriginalAttachmentReadsClose => _store.OriginalReadsClose;
    public void ThrowIfOriginalAttachmentReadJoinWouldCycle() => _store.ThrowIfOriginalReadJoinWouldCycle();
    public Task CloseOriginalAttachmentReadsAndDrainAsync() => _store.CloseOriginalReadsAndDrainAsync();
}
