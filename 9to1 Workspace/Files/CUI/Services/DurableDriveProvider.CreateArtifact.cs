namespace HavenOS.Files;

public sealed partial class DurableDriveProvider
{
    /// <summary>Atomically publishes a new owning artifact and its first immutable content revision.</summary>
    public Task<FilesResult<FilesRevision>> CommitCreatedArtifactAsync(FilesArtifactReference artifact,
        FilesOwningAppRevisionCommit commit, IReadOnlyList<FilesItemRevisionPrecondition> preconditions,
        FilesCommitAuthorityGuard authority, CancellationToken cancellationToken) =>
        CommitCreatedArtifactCoreAsync(artifact, commit, preconditions, null, authority, cancellationToken);

    /// <summary>Retains the original creation-target store through the final persistent mutation.</summary>
    public Task<FilesResult<FilesRevision>> CommitCreatedArtifactAsync(FilesArtifactReference artifact,
        FilesOwningAppRevisionCommit commit, IReadOnlyList<FilesItemRevisionPrecondition> preconditions,
        Guid expectedStoreId, FilesCommitAuthorityGuard authority, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (expectedStoreId == Guid.Empty)
            return Task.FromResult(Fail<FilesRevision>(FilesErrorCode.InvalidState,
                "Creation requires the original Files store identity.", "CreateArtifact", artifact.FileId));
        return CommitCreatedArtifactCoreAsync(artifact, commit, preconditions, expectedStoreId, authority, cancellationToken);
    }

    private async Task<FilesResult<FilesRevision>> CommitCreatedArtifactCoreAsync(FilesArtifactReference artifact,
        FilesOwningAppRevisionCommit commit, IReadOnlyList<FilesItemRevisionPrecondition> preconditions,
        Guid? expectedStoreId, FilesCommitAuthorityGuard authority, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact); ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(preconditions); ArgumentNullException.ThrowIfNull(authority);
        var guards = preconditions.ToArray();
        FilesResult<FilesRevision> Error(FilesErrorCode code, string message) =>
            Fail<FilesRevision>(code, message, "CreateArtifact", artifact.FileId);
        if (commit.ActorId != _owner || authority.ActorId != _owner)
            return Error(FilesErrorCode.PermissionDenied, "Only the trusted Files owner may create an artifact.");
        var hash = commit.ContentHash;
        if (hash?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true) hash = hash[7..];
        if (artifact.FileId.Value == Guid.Empty || commit.FileId != artifact.FileId ||
            commit.OwningAppId != artifact.OwnerAppId || commit.ExpectedBaseRevisionId is not null ||
            string.IsNullOrWhiteSpace(artifact.ArtifactId) || string.IsNullOrWhiteSpace(artifact.OwnerAppId) ||
            string.IsNullOrWhiteSpace(artifact.ArtifactType) || string.IsNullOrWhiteSpace(commit.OwningAppRevisionId) ||
            string.IsNullOrWhiteSpace(artifact.DisplayName) || artifact.DisplayName is "." or ".." ||
            artifact.DisplayName.IndexOfAny(['/', '\\', '\0']) >= 0 || commit.SizeBytes is null or < 0 ||
            hash is null || hash.Length != 64 || !hash.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(commit.ProviderContentReference) || Path.IsPathFullyQualified(commit.ProviderContentReference) ||
            commit.ProviderContentReference.IndexOfAny([':', '\0']) >= 0 ||
            commit.ProviderContentReference.Split(['/', '\\']).Any(part => part is "." or ".." or ""))
            return Error(FilesErrorCode.InvalidState, "Creation requires a new canonical identity and immutable content proof.");
        if (guards.Length is < 1 or > 256 || guards.Any(g => g is null || g.ItemId.Value == Guid.Empty) ||
            guards.Select(g => g.ItemId).Distinct().Count() != guards.Length ||
            artifact.ParentFolderId is not { } parent || !guards.Any(g => g.ItemId == parent))
            return Error(FilesErrorCode.InvalidState, "Creation requires the current canonical destination revision.");
        FilesResult<FilesRevision>? result = null;
        try
        {
            await _store.UpdateAsync(state =>
            {
                if (expectedStoreId is { } originalStore && state.StoreId != originalStore)
                { throw new OriginalFilesStoreChangedException(); }
                foreach (var guard in guards)
                {
                    var item = state.Items.SingleOrDefault(e => e.Metadata.Id == guard.ItemId);
                    if (item is null || !IsVisible(state, item))
                    { result = Error(FilesErrorCode.ItemNotFound, "A guarded Files item is unavailable."); return state; }
                    if (item.Metadata.CurrentRevisionId != guard.ExpectedRevision)
                    { result = Error(FilesErrorCode.RevisionConflict, "A guarded Files item changed."); return state; }
                }
                if (state.Items.Single(e => e.Metadata.Id == parent).Metadata.Kind != HostedItemKind.Folder)
                { result = Error(FilesErrorCode.DestinationUnavailable, "The canonical destination is not a folder."); return state; }
                if (state.Items.Any(e => e.Metadata.Id == artifact.FileId) ||
                    state.Artifacts.Any(a => a.FileId == artifact.FileId || a.OwnerAppId == artifact.OwnerAppId && a.ArtifactId == artifact.ArtifactId))
                { result = Error(FilesErrorCode.InvalidState, "The artifact identity already exists."); return state; }
                if (state.Items.Any(e => !e.Deleted && e.Metadata.ParentId == parent &&
                    e.Metadata.Name.Equals(artifact.DisplayName, StringComparison.OrdinalIgnoreCase)))
                { result = Error(FilesErrorCode.NameConflict, "The destination name already exists."); return state; }
                var revision = new FilesRevision(new(Guid.NewGuid()), artifact.FileId, null, commit.CommittedAt,
                    _owner, "owning-app", commit.ContentHash, commit.SizeBytes, commit.OwningAppId, commit.OwningAppRevisionId, true);
                var metadata = new HostedItemMetadata(artifact.FileId, Location.Id, parent, artifact.DisplayName,
                    HostedItemKind.Artifact, null, _owner, "personal", commit.SizeBytes, commit.CommittedAt,
                    commit.CommittedAt, revision.Id, SyncAvailability.AvailableOffline, false, commit.ContentHash);
                var change = new FilesChangeEvent(Guid.NewGuid().ToString("N"), new(state.Events.Count + 1), null,
                    artifact.FileId, _owner, "OwningAppRevisionCommitted", null, revision.Id, commit.CommittedAt, metadata);
                result = FilesResult<FilesRevision>.Success(revision);
                return state with
                {
                    Items = [.. state.Items, new Entry(metadata)], Artifacts = [.. state.Artifacts, artifact],
                    Revisions = [.. state.Revisions, revision], Events = [.. state.Events, change],
                    RevisionContentReferences = new Dictionary<string, string?>(state.RevisionContentReferences)
                    { [revision.Id.ToString()] = commit.ProviderContentReference }
                };
            }, authority.ValidateAsync, cancellationToken).ConfigureAwait(false);
        }
        catch (OriginalFilesStoreChangedException)
        { return Error(FilesErrorCode.RevisionConflict, "The original Files creation target store changed."); }
        catch (FilesCommitAuthorityChangedException)
        { return Error(FilesErrorCode.PermissionDenied, "Commit authority changed before publication."); }
        if (result!.IsSuccess) foreach (var subscriber in _subscribers.Values) subscriber.Writer.TryWrite(true);
        return result;
    }
}
