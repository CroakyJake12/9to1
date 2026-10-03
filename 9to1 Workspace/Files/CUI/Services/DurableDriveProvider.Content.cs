namespace HavenOS.Files;

/// <summary>Trusted Files transfer service supplies a verified immutable byte reference; HTTP/client
/// actor claims are never authority. Replaying the same revision requires identical commit arguments.</summary>
public sealed record FilesUploadedContent(HostedItemId FileId, HostedItemId? ParentFolderId, string Name,
    string? MimeType, FilesRevisionId RevisionId, FilesRevisionId? ExpectedRevision, string ActorId,
    DateTimeOffset CommittedAt, long SizeBytes, string ContentHash, string ProviderContentReference);

/// <summary>Current visible item revision required atomically with an uploaded-content commit.</summary>
public sealed record FilesItemRevisionPrecondition(HostedItemId ItemId, FilesRevisionId? ExpectedRevision);

public sealed partial class DurableDriveProvider
{
    public Task<FilesResult<FilesRevision>> CommitUploadedContentAsync(FilesUploadedContent content,
        CancellationToken cancellationToken = default) => CommitUploadedContentAsync(content, [], cancellationToken);

    public Task<FilesResult<FilesRevision>> CommitUploadedContentAsync(FilesUploadedContent content,
        IReadOnlyList<FilesItemRevisionPrecondition> preconditions, CancellationToken cancellationToken = default) =>
        CommitUploadedContentCoreAsync(content, preconditions, null, null, cancellationToken);

    public Task<FilesResult<FilesRevision>> CommitUploadedContentAsync(FilesUploadedContent content,
        IReadOnlyList<FilesItemRevisionPrecondition> preconditions, FilesCommitAuthorityGuard authority,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authority);
        return CommitUploadedContentCoreAsync(content, preconditions, null, authority, cancellationToken);
    }

    /// <summary>Final original-destination store fence under the same Files publication lease.</summary>
    public Task<FilesResult<FilesRevision>> CommitUploadedContentAsync(FilesUploadedContent content,
        IReadOnlyList<FilesItemRevisionPrecondition> preconditions, Guid expectedStoreId,
        FilesCommitAuthorityGuard authority, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content); ArgumentNullException.ThrowIfNull(preconditions);
        ArgumentNullException.ThrowIfNull(authority);
        if (expectedStoreId == Guid.Empty)
            return Task.FromResult(Fail<FilesRevision>(FilesErrorCode.InvalidState,
                "Select the original Files destination UUID before upload.", "CommitFileContent", content.FileId));
        return CommitUploadedContentCoreAsync(content, preconditions, expectedStoreId, authority, cancellationToken);
    }

    private async Task<FilesResult<FilesRevision>> CommitUploadedContentCoreAsync(FilesUploadedContent content,
        IReadOnlyList<FilesItemRevisionPrecondition> preconditions, Guid? expectedStoreId,
        FilesCommitAuthorityGuard? authority, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(preconditions);
        var captured = preconditions.ToArray();
        if (captured.Length > 256 || captured.Any(item => item is null || item.ItemId.Value == Guid.Empty) ||
            captured.Select(item => item.ItemId).Distinct().Count() != captured.Length)
            return Fail<FilesRevision>(FilesErrorCode.InvalidState, "Upload preconditions require unique canonical item identities.", "CommitFileContent", content.FileId);
        if (content.ActorId != _owner || authority is not null && authority.ActorId != _owner) return Fail<FilesRevision>(FilesErrorCode.PermissionDenied, "Only the trusted Files owner can publish content.", "CommitFileContent", content.FileId);
        var hash = content.ContentHash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? content.ContentHash[7..] : content.ContentHash;
        if (content.FileId.Value == Guid.Empty || content.RevisionId.Value == Guid.Empty || content.SizeBytes < 0 ||
            hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)) || string.IsNullOrWhiteSpace(content.Name) ||
            content.Name.IndexOfAny(['/', '\\', '\0']) >= 0 || content.Name != Path.GetFileName(content.Name) || content.Name is "." or ".." ||
            string.IsNullOrWhiteSpace(content.ProviderContentReference) || Path.IsPathFullyQualified(content.ProviderContentReference) ||
            content.ProviderContentReference.Contains('\0') || content.ProviderContentReference.Split(['/', '\\']).Any(part => part is "." or ".."))
            return Fail<FilesRevision>(FilesErrorCode.InvalidState, "Content requires safe canonical identities, name, hash and immutable Files-relative reference.", "CommitFileContent", content.FileId);
        FilesResult<FilesRevision>? result = null;
        try
        {
        await _store.UpdateAsync(state =>
        {
            if (expectedStoreId is { } originalStore && state.StoreId != originalStore)
                throw new OriginalFilesStoreChangedException();
            foreach (var condition in captured)
            {
                var guarded = state.Items.SingleOrDefault(item => item.Metadata.Id == condition.ItemId);
                if (guarded is null || !IsVisible(state, guarded))
                { result = Fail<FilesRevision>(FilesErrorCode.ItemNotFound, "A required Files item is unavailable.", "CommitFileContent", content.FileId); return state; }
                if (guarded.Metadata.CurrentRevisionId != condition.ExpectedRevision)
                { result = Fail<FilesRevision>(FilesErrorCode.RevisionConflict, "A required Files revision changed before publication.", "CommitFileContent", content.FileId); return state; }
            }
            var replay = state.UploadedContents.SingleOrDefault(item => item.RevisionId == content.RevisionId);
            if (replay is not null)
            {
                result = replay == content
                    ? FilesResult<FilesRevision>.Success(state.Revisions.Single(item => item.Id == content.RevisionId))
                    : Fail<FilesRevision>(FilesErrorCode.InvalidState, "Revision replay differs from its committed arguments.", "CommitFileContent", content.FileId);
                return state;
            }
            var existing = state.Items.SingleOrDefault(item => item.Metadata.Id == content.FileId);
            if (existing is not null && (!IsVisible(state, existing) || existing.Metadata.Kind != HostedItemKind.File))
            { result = Fail<FilesRevision>(FilesErrorCode.ItemNotFound, "Canonical file is unavailable.", "CommitFileContent", content.FileId); return state; }
            if (existing?.Metadata.CurrentRevisionId != content.ExpectedRevision)
            { result = Fail<FilesRevision>(FilesErrorCode.RevisionConflict, "Files revision changed; refresh before publishing.", "CommitFileContent", content.FileId); return state; }
            if (state.Revisions.Any(item => item.Id == content.RevisionId))
            { result = Fail<FilesRevision>(FilesErrorCode.InvalidState, "Revision identity already exists.", "CommitFileContent", content.FileId); return state; }
            if (existing is not null && (existing.Metadata.ParentId != content.ParentFolderId || existing.Metadata.Name != content.Name))
            { result = Fail<FilesRevision>(FilesErrorCode.InvalidState, "Content publication cannot silently rename or move a file.", "CommitFileContent", content.FileId); return state; }
            if (content.ParentFolderId is { } parent)
            {
                var folder = state.Items.SingleOrDefault(item => item.Metadata.Id == parent);
                if (folder is null || !IsVisible(state, folder) || folder.Metadata.Kind != HostedItemKind.Folder)
                { result = Fail<FilesRevision>(FilesErrorCode.DestinationUnavailable, "Files destination is unavailable.", "CommitFileContent", content.FileId); return state; }
            }
            if (existing is null && state.Items.Any(item => !item.Deleted && item.Metadata.ParentId == content.ParentFolderId && item.Metadata.Name.Equals(content.Name, StringComparison.OrdinalIgnoreCase)))
            { result = Fail<FilesRevision>(FilesErrorCode.NameConflict, "Destination name already exists.", "CommitFileContent", content.FileId); return state; }
            var metadata = existing?.Metadata ?? new HostedItemMetadata(content.FileId, Location.Id, content.ParentFolderId,
                content.Name, HostedItemKind.File, content.MimeType, _owner, "personal", null, content.CommittedAt,
                content.CommittedAt, null, SyncAvailability.LocalChanges, false, null);
            metadata = metadata with { CurrentRevisionId = content.RevisionId, ContentHash = content.ContentHash, ContentType = content.MimeType,
                SizeBytes = content.SizeBytes, ModifiedAt = content.CommittedAt, Availability = SyncAvailability.AvailableOffline };
            var revision = new FilesRevision(content.RevisionId, content.FileId, content.ExpectedRevision, content.CommittedAt,
                _owner, "files-upload", content.ContentHash, content.SizeBytes, "files", content.RevisionId.ToString(), true);
            var change = new FilesChangeEvent(Guid.NewGuid().ToString("N"), new(state.Events.Count + 1), null,
                content.FileId, _owner, "FileContentCommitted", content.ExpectedRevision, content.RevisionId, content.CommittedAt, metadata);
            result = FilesResult<FilesRevision>.Success(revision);
            return state with
            {
                Items = [.. state.Items.Where(item => item.Metadata.Id != content.FileId), new Entry(metadata)],
                Revisions = [.. state.Revisions.Select(item => item.ItemId == content.FileId ? item with { IsCurrent = false } : item), revision],
                RevisionContentReferences = new Dictionary<string, string?>(state.RevisionContentReferences) { [revision.Id.ToString()] = content.ProviderContentReference },
                UploadedContents = [.. state.UploadedContents, content], Events = [.. state.Events, change]
            };
        }, authority is null ? null : authority.ValidateAsync, cancellationToken).ConfigureAwait(false);
        }
        catch (OriginalFilesStoreChangedException)
        { return Fail<FilesRevision>(FilesErrorCode.RevisionConflict, "The original Files destination changed before publication.", "CommitFileContent", content.FileId); }
        catch (FilesCommitAuthorityChangedException)
        { return Fail<FilesRevision>(FilesErrorCode.PermissionDenied, "Commit authority changed before publication.", "CommitFileContent", content.FileId); }
        if (result!.IsSuccess) foreach (var subscriber in _subscribers.Values) subscriber.Writer.TryWrite(true);
        return result;
    }
}
