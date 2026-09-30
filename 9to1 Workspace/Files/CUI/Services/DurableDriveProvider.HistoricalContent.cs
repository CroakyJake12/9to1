namespace HavenOS.Files;

public sealed partial class DurableDriveProvider
{
    /// <summary>Returns retained content proof only. The trusted host must authorise current item access before materialising bytes.</summary>
    public async Task<FilesResult<FilesArtifactContentRevision>> GetArtifactRevisionContentAsync(
        HostedItemId fileId, FilesRevisionId revisionId, CancellationToken cancellationToken = default)
    {
        var state = await _store.ReadAsync(cancellationToken);
        var item = state.Items.SingleOrDefault(entry => entry.Metadata.Id == fileId);
        if (item is null || !IsVisible(state, item))
            return Fail<FilesArtifactContentRevision>(FilesErrorCode.ItemNotFound, "Item is unavailable.", "GetRevisionContent", fileId);
        var revision = state.Revisions.SingleOrDefault(entry => entry.ItemId == fileId && entry.Id == revisionId);
        var reference = state.RevisionContentReferences.GetValueOrDefault(revisionId.ToString());
        return revision is null || string.IsNullOrWhiteSpace(reference)
            ? Fail<FilesArtifactContentRevision>(FilesErrorCode.InvalidState, "The requested content revision is not retained.", "GetRevisionContent", fileId)
            : FilesResult<FilesArtifactContentRevision>.Success(new(revision, reference));
    }
}
