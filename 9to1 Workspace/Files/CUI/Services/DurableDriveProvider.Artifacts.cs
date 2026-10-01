namespace HavenOS.Files;

public sealed partial class DurableDriveProvider
{
    /// <summary>Registers a durable artifact created by its owning app; it never creates app internals.</summary>
    public async Task<FilesResult<FilesArtifactReference>> RegisterArtifactAsync(FilesArtifactReference reference,
        string authenticatedActorId, CancellationToken cancellationToken = default)
    {
        FilesResult<FilesArtifactReference>? result = null;
        await _store.UpdateAsync(state =>
        {
            if (authenticatedActorId != _owner)
            { result = Fail<FilesArtifactReference>(FilesErrorCode.PermissionDenied, "Caller does not own this Drive.", "RegisterArtifact", reference.FileId); return state; }
            if (reference.FileId.Value == Guid.Empty || string.IsNullOrWhiteSpace(reference.ArtifactId) ||
                string.IsNullOrWhiteSpace(reference.OwnerAppId) || string.IsNullOrWhiteSpace(reference.ArtifactType) ||
                string.IsNullOrWhiteSpace(reference.DisplayName) || reference.DisplayName is "." or ".." ||
                reference.DisplayName.IndexOfAny(['/', '\\', '\0']) >= 0)
            { result = Fail<FilesArtifactReference>(FilesErrorCode.InvalidName, "A stable artifact reference and safe name are required.", "RegisterArtifact", reference.FileId); return state; }
            var previous = state.Artifacts.SingleOrDefault(item => item.FileId == reference.FileId);
            if (previous is not null)
            {
                result = previous == reference ? FilesResult<FilesArtifactReference>.Success(previous) :
                    Fail<FilesArtifactReference>(FilesErrorCode.InvalidState, "Artifact identity is already registered with different creation arguments.", "RegisterArtifact", reference.FileId);
                return state;
            }
            if (state.Items.Any(item => item.Metadata.Id == reference.FileId))
            { result = Fail<FilesArtifactReference>(FilesErrorCode.InvalidState, "File identity already exists.", "RegisterArtifact", reference.FileId); return state; }
            if (state.Artifacts.Any(item => item.OwnerAppId == reference.OwnerAppId && item.ArtifactId == reference.ArtifactId))
            { result = Fail<FilesArtifactReference>(FilesErrorCode.InvalidState, "The owning artifact already has a canonical Files identity.", "RegisterArtifact", reference.FileId); return state; }
            if (reference.ParentFolderId is { } parent)
            {
                var folder = state.Items.SingleOrDefault(item => item.Metadata.Id == parent);
                if (folder is null || !IsVisible(state, folder) || folder.Metadata.Kind != HostedItemKind.Folder)
                { result = Fail<FilesArtifactReference>(FilesErrorCode.DestinationUnavailable, "Canonical parent folder is unavailable.", "RegisterArtifact", reference.FileId); return state; }
            }
            if (state.Items.Any(item => !item.Deleted && item.Metadata.ParentId == reference.ParentFolderId &&
                item.Metadata.Name.Equals(reference.DisplayName, StringComparison.OrdinalIgnoreCase)))
            { result = Fail<FilesArtifactReference>(FilesErrorCode.NameConflict, "Destination name already exists.", "RegisterArtifact", reference.FileId); return state; }
            var now = DateTimeOffset.UtcNow;
            var metadata = new HostedItemMetadata(reference.FileId, Location.Id, reference.ParentFolderId,
                reference.DisplayName, HostedItemKind.Artifact, null, _owner, "personal", null, now, now,
                null, SyncAvailability.LocalChanges, false, null);
            var change = new FilesChangeEvent(Guid.NewGuid().ToString("N"), new(state.Events.Count + 1), null,
                reference.FileId, _owner, "ArtifactRegistered", null, null, now, metadata);
            result = FilesResult<FilesArtifactReference>.Success(reference);
            return state with { Items = [.. state.Items, new Entry(metadata)], Artifacts = [.. state.Artifacts, reference], Events = [.. state.Events, change] };
        }, cancellationToken);
        if (result!.IsSuccess) foreach (var subscriber in _subscribers.Values) subscriber.Writer.TryWrite(true);
        return result;
    }

    public async Task<FilesResult<FilesArtifactReference>> GetArtifactAsync(HostedItemId fileId, CancellationToken cancellationToken = default)
    {
        var state = await _store.ReadAsync(cancellationToken);
        var metadata = state.Items.SingleOrDefault(item => item.Metadata.Id == fileId);
        var reference = state.Artifacts.SingleOrDefault(item => item.FileId == fileId);
        return metadata is null || !IsVisible(state, metadata) || reference is null
            ? Fail<FilesArtifactReference>(FilesErrorCode.ItemNotFound, "Artifact is unavailable.", "GetArtifact", fileId)
            : FilesResult<FilesArtifactReference>.Success(reference with { DisplayName = metadata.Metadata.Name, ParentFolderId = metadata.Metadata.ParentId });
    }

    public Task<FilesResult<FilesRevision>> CommitDurableRevisionAsync(FilesOwningAppRevisionCommit commit, CancellationToken cancellationToken) =>
        CommitDurableRevisionCoreAsync(commit, [], null, null, cancellationToken);

    public Task<FilesResult<FilesRevision>> CommitDurableRevisionAsync(FilesOwningAppRevisionCommit commit,
        FilesCommitAuthorityGuard authority, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authority);
        return CommitDurableRevisionCoreAsync(commit, [], null, authority, cancellationToken);
    }

    /// <summary>Checks captured canonical source revisions in the same metadata mutation as publication.
    /// The authority callback must never recursively read Files while this store lease is held.</summary>
    public Task<FilesResult<FilesRevision>> CommitDurableRevisionAsync(FilesOwningAppRevisionCommit commit,
        IReadOnlyList<FilesItemRevisionPrecondition> preconditions, FilesCommitAuthorityGuard authority,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit); ArgumentNullException.ThrowIfNull(preconditions);
        ArgumentNullException.ThrowIfNull(authority);
        var guards = preconditions.ToArray();
        if (guards.Length is < 1 or > 256 || guards.Any(guard => guard is null || guard.ItemId.Value == Guid.Empty) ||
            guards.Select(guard => guard.ItemId).Distinct().Count() != guards.Length)
            return Task.FromResult(Fail<FilesRevision>(FilesErrorCode.InvalidState,
                "Source publication requires bounded unique canonical revision preconditions.", "CommitOwningAppRevision", commit.FileId));
        return CommitDurableRevisionCoreAsync(commit, guards, null, authority, cancellationToken);
    }

    /// <summary>Retains the original store identity and source revisions through the final persistent mutation.
    /// Empty source guards support source-less artifacts; the store identity is always mandatory.</summary>
    public Task<FilesResult<FilesRevision>> CommitDurableRevisionAsync(FilesOwningAppRevisionCommit commit,
        IReadOnlyList<FilesItemRevisionPrecondition> preconditions, Guid expectedStoreId,
        FilesCommitAuthorityGuard authority, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit); ArgumentNullException.ThrowIfNull(preconditions);
        ArgumentNullException.ThrowIfNull(authority);
        var guards = preconditions.ToArray();
        if (expectedStoreId == Guid.Empty || guards.Length > 256 ||
            guards.Any(guard => guard is null || guard.ItemId.Value == Guid.Empty) ||
            guards.Select(guard => guard.ItemId).Distinct().Count() != guards.Length)
            return Task.FromResult(Fail<FilesRevision>(FilesErrorCode.InvalidState,
                "Publication requires the original store identity and bounded unique source revisions.", "CommitOwningAppRevision", commit.FileId));
        return CommitDurableRevisionCoreAsync(commit, guards, expectedStoreId, authority, cancellationToken);
    }

    private async Task<FilesResult<FilesRevision>> CommitDurableRevisionCoreAsync(FilesOwningAppRevisionCommit commit,
        IReadOnlyList<FilesItemRevisionPrecondition> guards, Guid? expectedStoreId, FilesCommitAuthorityGuard? authority, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (authority is not null && authority.ActorId != _owner)
            return Fail<FilesRevision>(FilesErrorCode.PermissionDenied, "Commit authority does not own this Drive.", "CommitOwningAppRevision", commit.FileId);
        FilesResult<FilesRevision>? result = null;
        try
        {
        await _store.UpdateAsync(state =>
        {
            if (expectedStoreId is { } originalStore && state.StoreId != originalStore)
            { throw new OriginalFilesStoreChangedException(); }
            var entry = state.Items.SingleOrDefault(item => item.Metadata.Id == commit.FileId);
            var reference = state.Artifacts.SingleOrDefault(item => item.FileId == commit.FileId);
            if (commit.ActorId != _owner || reference?.OwnerAppId != commit.OwningAppId)
            { result = Fail<FilesRevision>(FilesErrorCode.PermissionDenied, "Only the authorised owning app may commit this artifact revision.", "CommitOwningAppRevision", commit.FileId); return state; }
            foreach (var guard in guards)
            {
                var guarded = state.Items.SingleOrDefault(item => item.Metadata.Id == guard.ItemId);
                if (guarded is null || !IsVisible(state, guarded))
                { result = Fail<FilesRevision>(FilesErrorCode.ItemNotFound, "A canonical source is unavailable.", "CommitOwningAppRevision", commit.FileId); return state; }
                if (guarded.Metadata.CurrentRevisionId != guard.ExpectedRevision)
                { result = Fail<FilesRevision>(FilesErrorCode.RevisionConflict, "A canonical source changed before publication.", "CommitOwningAppRevision", commit.FileId); return state; }
            }
            if (entry is null || !IsVisible(state, entry))
            { result = Fail<FilesRevision>(FilesErrorCode.ItemNotFound, "Artifact is unavailable.", "CommitOwningAppRevision", commit.FileId); return state; }
            if (string.IsNullOrWhiteSpace(commit.OwningAppRevisionId) || commit.SizeBytes is < 0)
            { result = Fail<FilesRevision>(FilesErrorCode.InvalidState, "A durable owning-app revision is required.", "CommitOwningAppRevision", commit.FileId); return state; }
            var prior = state.Revisions.SingleOrDefault(item => item.ItemId == commit.FileId && item.OwningAppId == commit.OwningAppId && item.OwningAppRevisionId == commit.OwningAppRevisionId);
            if (prior is not null)
            {
                result = prior.ActorId == commit.ActorId && prior.ContentHash == commit.ContentHash && prior.SizeBytes == commit.SizeBytes &&
                    prior.ParentRevisionId == commit.ExpectedBaseRevisionId && state.RevisionContentReferences.GetValueOrDefault(prior.Id.ToString()) == commit.ProviderContentReference
                    ? FilesResult<FilesRevision>.Success(prior) : Fail<FilesRevision>(FilesErrorCode.InvalidState, "Owning-app revision replay has different arguments.", "CommitOwningAppRevision", commit.FileId);
                return state;
            }
            if (entry.Metadata.CurrentRevisionId != commit.ExpectedBaseRevisionId)
            { result = Fail<FilesRevision>(FilesErrorCode.RevisionConflict, "Artifact changed since the owning app read its revision.", "CommitOwningAppRevision", commit.FileId); return state; }
            var revision = new FilesRevision(new(Guid.NewGuid()), commit.FileId, commit.ExpectedBaseRevisionId,
                commit.CommittedAt, _owner, "owning-app", commit.ContentHash, commit.SizeBytes, commit.OwningAppId, commit.OwningAppRevisionId, true);
            var metadata = entry.Metadata with { CurrentRevisionId = revision.Id, ModifiedAt = commit.CommittedAt,
                ContentHash = commit.ContentHash, SizeBytes = commit.SizeBytes, Availability = SyncAvailability.AvailableOffline };
            var change = new FilesChangeEvent(Guid.NewGuid().ToString("N"), new(state.Events.Count + 1), null, commit.FileId,
                _owner, "OwningAppRevisionCommitted", commit.ExpectedBaseRevisionId, revision.Id, commit.CommittedAt, metadata);
            result = FilesResult<FilesRevision>.Success(revision);
            return state with { Items = [.. state.Items.Where(item => item.Metadata.Id != commit.FileId), entry with { Metadata = metadata }],
                Revisions = [.. state.Revisions.Select(item => item.ItemId == commit.FileId ? item with { IsCurrent = false } : item), revision],
                RevisionContentReferences = new Dictionary<string, string?>(state.RevisionContentReferences) { [revision.Id.ToString()] = commit.ProviderContentReference },
                Events = [.. state.Events, change] };
        }, authority is null ? null : authority.ValidateAsync, cancellationToken);
        }
        catch (OriginalFilesStoreChangedException)
        { return Fail<FilesRevision>(FilesErrorCode.RevisionConflict, "The original Files store changed before publication.", "CommitOwningAppRevision", commit.FileId); }
        catch (FilesCommitAuthorityChangedException)
        { return Fail<FilesRevision>(FilesErrorCode.PermissionDenied, "Commit authority changed before publication.", "CommitOwningAppRevision", commit.FileId); }
        if (result!.IsSuccess) foreach (var subscriber in _subscribers.Values) subscriber.Writer.TryWrite(true);
        return result;
    }
    // Refusal inside UpdateAsync aborts before its serializer can rewrite a substituted store.
    private sealed class OriginalFilesStoreChangedException : Exception { }

}
