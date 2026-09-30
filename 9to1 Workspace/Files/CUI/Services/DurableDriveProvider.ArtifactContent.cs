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
}
