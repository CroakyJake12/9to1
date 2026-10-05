namespace HavenOS.Files;

public sealed partial class DurableDriveProvider
{
    /// <summary>Owner-bound canonical lookup; native hosts must first verify the current Home store binding.
    /// Duplicate persisted owner identities are recovery errors, never an arbitrary first match.</summary>
    public async Task<FilesResult<FilesArtifactReference>> GetArtifactByOwnerIdentityAsync(string ownerAppId,
        string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerAppId);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        var state = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        var references = state.Artifacts.Where(reference => reference.OwnerAppId == ownerAppId && reference.ArtifactId == artifactId).ToArray();
        if (references.Length > 1)
            return Fail<FilesArtifactReference>(FilesErrorCode.InvalidState, "Stored artifact identity is ambiguous; recovery is required.", "GetArtifactByOwnerIdentity", default);
        if (references.Length == 0)
            return Fail<FilesArtifactReference>(FilesErrorCode.ItemNotFound, "Artifact is unavailable.", "GetArtifactByOwnerIdentity", default);
        var reference = references[0];
        var entries = state.Items.Where(entry => entry.Metadata.Id == reference.FileId).ToArray();
        if (entries.Length != 1 || !IsVisible(state, entries[0]) || entries[0].Metadata.Kind != HostedItemKind.Artifact)
            return Fail<FilesArtifactReference>(FilesErrorCode.ItemNotFound, "Artifact is unavailable.", "GetArtifactByOwnerIdentity", reference.FileId);
        return FilesResult<FilesArtifactReference>.Success(reference with
        { DisplayName = entries[0].Metadata.Name, ParentFolderId = entries[0].Metadata.ParentId });
    }
}
