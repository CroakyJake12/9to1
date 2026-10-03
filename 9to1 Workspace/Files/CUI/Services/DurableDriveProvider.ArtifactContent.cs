namespace HavenOS.Files;

public sealed record FilesArtifactContentRevision(FilesRevision Revision, string? ProviderContentReference, HostedItemId? UploadAnchorFolderId = null);

public sealed partial class DurableDriveProvider
{
    public async Task<FilesResult<FilesArtifactContentRevision>> GetCurrentArtifactContentAsync(HostedItemId fileId,
        CancellationToken cancellationToken = default)
    {
        var state = await _store.ReadAsync(cancellationToken);
        var item = state.Items.SingleOrDefault(entry => entry.Metadata.Id == fileId);
        if (item is null || !IsVisible(state, item))
            return Fail<FilesArtifactContentRevision>(FilesErrorCode.ItemNotFound, "Artifact is unavailable.", "GetArtifactContent", fileId);
        var revision = state.Revisions.SingleOrDefault(entry => entry.ItemId == fileId && entry.IsCurrent);
        return revision is null ? Fail<FilesArtifactContentRevision>(FilesErrorCode.InvalidState, "No owning-app durable content revision is committed.", "GetArtifactContent", fileId) :
            FilesResult<FilesArtifactContentRevision>.Success(new(revision, state.RevisionContentReferences.GetValueOrDefault(revision.Id.ToString()),
                state.UploadedContents.SingleOrDefault(upload => upload.FileId == fileId && upload.RevisionId == revision.Id)?.ParentFolderId));
    }
    /// <summary>Reads an existing canonical snapshot and validates the original store before returning item metadata.</summary>
    public async Task<FilesResult<HostedItemMetadata>> GetForOriginalStoreAsync(Guid expectedStoreId, HostedItemId fileId,
        CancellationToken cancellationToken = default)
    {
        var state = await _store.ReadExistingAsync(cancellationToken).ConfigureAwait(false);
        RequireOriginalReadStore(state, expectedStoreId);
        var item = state.Items.SingleOrDefault(entry => entry.Metadata.Id == fileId);
        if (item is null || !IsVisible(state, item))
            return Fail<HostedItemMetadata>(FilesErrorCode.ItemNotFound, "Original item is unavailable.", "GetOriginalItem", fileId);
        RequireOriginalReadItem(item.Metadata);
        return FilesResult<HostedItemMetadata>.Success(item.Metadata);
    }

    public async Task<FilesResult<FilesArtifactReference>> GetArtifactForOriginalStoreAsync(Guid expectedStoreId, HostedItemId fileId,
        FilesRevisionId? expectedRevision, CancellationToken cancellationToken = default)
    {
        var state = await _store.ReadExistingAsync(cancellationToken).ConfigureAwait(false);
        RequireOriginalReadStore(state, expectedStoreId);
        var item = state.Items.SingleOrDefault(entry => entry.Metadata.Id == fileId);
        if (item is null || !IsVisible(state, item))
            return Fail<FilesArtifactReference>(FilesErrorCode.ItemNotFound, "Original artifact is unavailable.", "GetOriginalArtifact", fileId);
        RequireOriginalReadItem(item.Metadata);
        if (item.Metadata.CurrentRevisionId != expectedRevision)
            return Fail<FilesArtifactReference>(FilesErrorCode.RevisionConflict, "Original artifact revision changed.", "GetOriginalArtifact", fileId);
        var reference = state.Artifacts.SingleOrDefault(entry => entry.FileId == fileId);
        return reference is null ? Fail<FilesArtifactReference>(FilesErrorCode.ItemNotFound, "Original artifact is unavailable.", "GetOriginalArtifact", fileId)
            : FilesResult<FilesArtifactReference>.Success(reference with { DisplayName = item.Metadata.Name, ParentFolderId = item.Metadata.ParentId });
    }

    public async Task<FilesResult<FilesArtifactContentRevision>> GetCurrentArtifactContentForOriginalStoreAsync(Guid expectedStoreId,
        HostedItemId fileId, FilesRevisionId? expectedRevision, CancellationToken cancellationToken = default)
    {
        var state = await _store.ReadExistingAsync(cancellationToken).ConfigureAwait(false);
        RequireOriginalReadStore(state, expectedStoreId);
        var item = state.Items.SingleOrDefault(entry => entry.Metadata.Id == fileId);
        if (item is null || !IsVisible(state, item))
            return Fail<FilesArtifactContentRevision>(FilesErrorCode.ItemNotFound, "Original artifact is unavailable.", "GetOriginalArtifactContent", fileId);
        RequireOriginalReadItem(item.Metadata);
        if (item.Metadata.CurrentRevisionId != expectedRevision)
            return Fail<FilesArtifactContentRevision>(FilesErrorCode.RevisionConflict, "Original artifact revision changed.", "GetOriginalArtifactContent", fileId);
        var revision = state.Revisions.SingleOrDefault(entry => entry.ItemId == fileId && entry.IsCurrent);
        if (revision is null || revision.Id != expectedRevision)
            return Fail<FilesArtifactContentRevision>(FilesErrorCode.InvalidState, "No original durable content revision is committed.", "GetOriginalArtifactContent", fileId);
        return FilesResult<FilesArtifactContentRevision>.Success(new(revision,
            state.RevisionContentReferences.GetValueOrDefault(revision.Id.ToString()),
            state.UploadedContents.SingleOrDefault(upload => upload.FileId == fileId && upload.RevisionId == revision.Id)?.ParentFolderId));
    }

    private void RequireOriginalReadStore(State state, Guid expectedStoreId)
    {
        if (expectedStoreId == Guid.Empty || state.StoreId != expectedStoreId || state.StoreOwnerPrincipalId != _owner
            || state.StoreLocationId != Location.Id) throw new FilesOriginalStoreReadChangedException();
        if (state.Items is null || state.Artifacts is null || state.Revisions is null
            || state.RevisionContentReferences is null || state.UploadedContents is null)
            throw new InvalidDataException("Original Files metadata needs recovery.");
    }
    private void RequireOriginalReadItem(HostedItemMetadata item)
    {
        if (item.OwnerPrincipalId != _owner || item.LocationId != Location.Id || item.Scope != "personal")
            throw new FilesOriginalStoreReadChangedException();
    }
}

public sealed class FilesOriginalStoreReadChangedException() : UnauthorizedAccessException("The original Files store identity changed.");
