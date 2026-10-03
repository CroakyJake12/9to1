namespace HavenOS.Files;

public sealed record FilesImportedArtifactCommit(FilesRevision SourceRevision, FilesRevision ArtifactRevision);

public sealed partial class DurableDriveProvider
{
    /// <summary>Publishes a new immutable raw source and its new owning artifact in one Files transaction.
    /// The owning app supplies verified immutable byte references; Files never interprets app content.</summary>
    public Task<FilesResult<FilesImportedArtifactCommit>> CommitImportedArtifactAsync(
        FilesUploadedContent source, FilesArtifactReference artifact, FilesOwningAppRevisionCommit commit,
        IReadOnlyList<FilesItemRevisionPrecondition> preconditions, CancellationToken cancellationToken = default) =>
        CommitImportedArtifactCoreAsync(source, artifact, commit, preconditions, null, null, cancellationToken);

    public Task<FilesResult<FilesImportedArtifactCommit>> CommitImportedArtifactAsync(
        FilesUploadedContent source, FilesArtifactReference artifact, FilesOwningAppRevisionCommit commit,
        IReadOnlyList<FilesItemRevisionPrecondition> preconditions, FilesCommitAuthorityGuard authority,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authority);
        return CommitImportedArtifactCoreAsync(source, artifact, commit, preconditions, null, authority, cancellationToken);
    }

    /// <summary>Final original-store fence inside the same compound source/artifact publication transaction.</summary>
    public Task<FilesResult<FilesImportedArtifactCommit>> CommitImportedArtifactAsync(
        FilesUploadedContent source, FilesArtifactReference artifact, FilesOwningAppRevisionCommit commit,
        IReadOnlyList<FilesItemRevisionPrecondition> preconditions, Guid expectedStoreId,
        FilesCommitAuthorityGuard authority, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(commit); ArgumentNullException.ThrowIfNull(preconditions);
        ArgumentNullException.ThrowIfNull(authority);
        if (expectedStoreId == Guid.Empty)
            return Task.FromResult(Fail<FilesImportedArtifactCommit>(FilesErrorCode.InvalidState,
                "Select the original Files store UUID before import.", "ImportArtifact", artifact.FileId));
        return CommitImportedArtifactCoreAsync(source, artifact, commit, preconditions, expectedStoreId, authority, cancellationToken);
    }

    private async Task<FilesResult<FilesImportedArtifactCommit>> CommitImportedArtifactCoreAsync(
        FilesUploadedContent source, FilesArtifactReference artifact, FilesOwningAppRevisionCommit commit,
        IReadOnlyList<FilesItemRevisionPrecondition> preconditions, Guid? expectedStoreId,
        FilesCommitAuthorityGuard? authority, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(commit); ArgumentNullException.ThrowIfNull(preconditions);
        var guards = preconditions.ToArray();
        FilesResult<FilesImportedArtifactCommit> Error(FilesErrorCode code, string message) =>
            Fail<FilesImportedArtifactCommit>(code, message, "ImportArtifact", artifact.FileId);
        static bool SafeName(string? value) => !string.IsNullOrWhiteSpace(value) && value is not "." and not ".." &&
            value.IndexOfAny(['/', '\\', '\0']) < 0;
        static bool SafeReference(string? value) => !string.IsNullOrWhiteSpace(value) &&
            !Path.IsPathFullyQualified(value) && value.IndexOfAny([':', '\0']) < 0 &&
            !value.Split(['/', '\\']).Any(part => part is "." or ".." or "");
        static bool SafeHash(string? value)
        {
            if (value is null) return false;
            var hash = value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? value[7..] : value;
            return hash.Length == 64 && hash.All(Uri.IsHexDigit);
        }
        if (source.ActorId != _owner || commit.ActorId != _owner || authority is not null && authority.ActorId != _owner)
            return Error(FilesErrorCode.PermissionDenied, "Only the trusted Files owner may publish an import.");
        if (guards.Length is < 1 or > 256 || guards.Any(g => g is null || g.ItemId.Value == Guid.Empty) ||
            guards.Select(g => g.ItemId).Distinct().Count() != guards.Length ||
            source.ParentFolderId is not { } sourceParent || artifact.ParentFolderId is not { } artifactParent ||
            !guards.Any(g => g.ItemId == sourceParent) || !guards.Any(g => g.ItemId == artifactParent))
            return Error(FilesErrorCode.InvalidState, "Import requires unique revision guards for both canonical destinations.");
        if (source.FileId.Value == Guid.Empty || artifact.FileId.Value == Guid.Empty || source.FileId == artifact.FileId ||
            source.RevisionId.Value == Guid.Empty || source.ExpectedRevision is not null || commit.ExpectedBaseRevisionId is not null ||
            commit.FileId != artifact.FileId || commit.OwningAppId != artifact.OwnerAppId ||
            string.IsNullOrWhiteSpace(artifact.ArtifactId) || string.IsNullOrWhiteSpace(artifact.OwnerAppId) ||
            string.IsNullOrWhiteSpace(artifact.ArtifactType) || string.IsNullOrWhiteSpace(commit.OwningAppRevisionId) ||
            !SafeName(source.Name) || !SafeName(artifact.DisplayName) || source.SizeBytes < 0 || commit.SizeBytes is null or < 0 ||
            !SafeHash(source.ContentHash) || !SafeHash(commit.ContentHash) ||
            !SafeReference(source.ProviderContentReference) || !SafeReference(commit.ProviderContentReference) ||
            source.ProviderContentReference == commit.ProviderContentReference ||
            sourceParent == artifactParent && source.Name.Equals(artifact.DisplayName, StringComparison.OrdinalIgnoreCase))
            return Error(FilesErrorCode.InvalidState, "Import requires distinct new identities, safe immutable references and complete content proofs.");
        FilesResult<FilesImportedArtifactCommit>? result = null;
        try
        {
        await _store.UpdateAsync(state =>
        {
            if (expectedStoreId is { } originalStore && state.StoreId != originalStore)
                throw new OriginalFilesStoreChangedException();
            foreach (var guard in guards)
            {
                var item = state.Items.SingleOrDefault(e => e.Metadata.Id == guard.ItemId);
                if (item is null || !IsVisible(state, item))
                { result = Error(FilesErrorCode.ItemNotFound, "A guarded Files item is unavailable."); return state; }
                if (item.Metadata.CurrentRevisionId != guard.ExpectedRevision)
                { result = Error(FilesErrorCode.RevisionConflict, "A guarded Files item changed before import."); return state; }
            }
            foreach (var parent in new[] { sourceParent, artifactParent }.Distinct())
            {
                var item = state.Items.SingleOrDefault(e => e.Metadata.Id == parent);
                if (item is null || !IsVisible(state, item) || item.Metadata.Kind != HostedItemKind.Folder)
                { result = Error(FilesErrorCode.DestinationUnavailable, "An import destination is unavailable."); return state; }
            }
            if (state.Items.Any(e => e.Metadata.Id == source.FileId || e.Metadata.Id == artifact.FileId) ||
                state.Revisions.Any(r => r.Id == source.RevisionId) ||
                state.Artifacts.Any(a => a.OwnerAppId == artifact.OwnerAppId && a.ArtifactId == artifact.ArtifactId))
            { result = Error(FilesErrorCode.InvalidState, "An import identity is already registered."); return state; }
            if (state.Items.Any(e => !e.Deleted &&
                (e.Metadata.ParentId == sourceParent && e.Metadata.Name.Equals(source.Name, StringComparison.OrdinalIgnoreCase) ||
                 e.Metadata.ParentId == artifactParent && e.Metadata.Name.Equals(artifact.DisplayName, StringComparison.OrdinalIgnoreCase))))
            { result = Error(FilesErrorCode.NameConflict, "An import destination name already exists."); return state; }
            var rawRevision = new FilesRevision(source.RevisionId, source.FileId, null, source.CommittedAt, _owner,
                "files-upload", source.ContentHash, source.SizeBytes, "files", source.RevisionId.ToString(), true);
            var artifactRevision = new FilesRevision(new(Guid.NewGuid()), artifact.FileId, null, commit.CommittedAt,
                _owner, "owning-app", commit.ContentHash, commit.SizeBytes, commit.OwningAppId, commit.OwningAppRevisionId, true);
            var rawMetadata = new HostedItemMetadata(source.FileId, Location.Id, sourceParent, source.Name,
                HostedItemKind.File, source.MimeType, _owner, "personal", source.SizeBytes, source.CommittedAt,
                source.CommittedAt, rawRevision.Id, SyncAvailability.AvailableOffline, false, source.ContentHash);
            var artifactMetadata = new HostedItemMetadata(artifact.FileId, Location.Id, artifactParent, artifact.DisplayName,
                HostedItemKind.Artifact, null, _owner, "personal", commit.SizeBytes, commit.CommittedAt,
                commit.CommittedAt, artifactRevision.Id, SyncAvailability.AvailableOffline, false, commit.ContentHash);
            var rawEvent = new FilesChangeEvent(Guid.NewGuid().ToString("N"), new(state.Events.Count + 1), null,
                source.FileId, _owner, "FileContentCommitted", null, rawRevision.Id, source.CommittedAt, rawMetadata);
            var artifactEvent = new FilesChangeEvent(Guid.NewGuid().ToString("N"), new(state.Events.Count + 2), null,
                artifact.FileId, _owner, "OwningAppRevisionCommitted", null, artifactRevision.Id, commit.CommittedAt, artifactMetadata);
            result = FilesResult<FilesImportedArtifactCommit>.Success(new(rawRevision, artifactRevision));
            return state with
            {
                Items = [.. state.Items, new Entry(rawMetadata), new Entry(artifactMetadata)],
                Artifacts = [.. state.Artifacts, artifact], UploadedContents = [.. state.UploadedContents, source],
                Revisions = [.. state.Revisions, rawRevision, artifactRevision],
                RevisionContentReferences = new Dictionary<string, string?>(state.RevisionContentReferences)
                { [rawRevision.Id.ToString()] = source.ProviderContentReference, [artifactRevision.Id.ToString()] = commit.ProviderContentReference },
                Events = [.. state.Events, rawEvent, artifactEvent]
            };
        }, authority is null ? null : authority.ValidateAsync, cancellationToken).ConfigureAwait(false);
        }
        catch (OriginalFilesStoreChangedException)
        { return Error(FilesErrorCode.RevisionConflict, "The original Files store changed before import."); }
        catch (FilesCommitAuthorityChangedException)
        { return Error(FilesErrorCode.PermissionDenied, "Commit authority changed before publication."); }
        if (result!.IsSuccess) foreach (var subscriber in _subscribers.Values) subscriber.Writer.TryWrite(true);
        return result;
    }
}
