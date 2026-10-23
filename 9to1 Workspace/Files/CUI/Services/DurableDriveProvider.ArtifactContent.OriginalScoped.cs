namespace HavenOS.Files;

public sealed partial class DurableDriveProvider
{
    /// <summary>Original artifact observation with the exact raw existing-store read retained
    /// before its factory scope exits and independently joined if that scope fails.</summary>
    public async Task<FilesResult<FilesArtifactReference>> GetArtifactForOriginalStoreAsync(Guid expectedStoreId,
        HostedItemId fileId, FilesRevisionId? expectedRevision, CancellationToken cancellationToken,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope);
        ArgumentNullException.ThrowIfNull(retainOriginalTask);
        void Scope(Action callback) => FilesOriginalSourceCallbackScope.Invoke(callback, originalSynchronousScope);
        var state = await FilesOriginalDeveloperTaskSource.ObserveAsync(
            () => _store.ReadExistingAsync(cancellationToken), Scope, retainOriginalTask).ConfigureAwait(false);
        FilesResult<FilesArtifactReference>? result = null;
        FilesOriginalDeveloperTaskSource.Invoke(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireOriginalReadStore(state, expectedStoreId);
            var item = state.Items.SingleOrDefault(entry => entry.Metadata.Id == fileId);
            if (item is null || !IsVisible(state, item))
                result = Fail<FilesArtifactReference>(FilesErrorCode.ItemNotFound, "Original artifact is unavailable.", "GetOriginalArtifact", fileId);
            else
            {
                RequireOriginalReadItem(item.Metadata);
                if (item.Metadata.CurrentRevisionId != expectedRevision)
                    result = Fail<FilesArtifactReference>(FilesErrorCode.RevisionConflict, "Original artifact revision changed.", "GetOriginalArtifact", fileId);
                else
                {
                    var reference = state.Artifacts.SingleOrDefault(entry => entry.FileId == fileId);
                    result = reference is null
                        ? Fail<FilesArtifactReference>(FilesErrorCode.ItemNotFound, "Original artifact is unavailable.", "GetOriginalArtifact", fileId)
                        : FilesResult<FilesArtifactReference>.Success(reference with
                            { DisplayName = item.Metadata.Name, ParentFolderId = item.Metadata.ParentId });
                }
            }
        }, Scope);
        return result ?? throw new InvalidOperationException("No actual original artifact observation exists.");
    }
}
