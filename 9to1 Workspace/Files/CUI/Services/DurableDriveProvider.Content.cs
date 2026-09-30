namespace HavenOS.Files;

/// <summary>Trusted Files transfer service supplies a verified immutable byte reference; HTTP/client
/// actor claims are never authority. Replaying the same revision requires identical commit arguments.</summary>
public sealed record FilesUploadedContent(HostedItemId FileId, HostedItemId? ParentFolderId, string Name,
    string? MimeType, FilesRevisionId RevisionId, FilesRevisionId? ExpectedRevision, string ActorId,
    DateTimeOffset CommittedAt, long SizeBytes, string ContentHash, string ProviderContentReference);

public sealed partial class DurableDriveProvider
{
    public async Task<FilesResult<FilesRevision>> CommitUploadedContentAsync(FilesUploadedContent content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.ActorId != _owner) return Fail<FilesRevision>(FilesErrorCode.PermissionDenied, "Only the trusted Files owner can publish content.", "CommitFileContent", content.FileId);
        var hash = content.ContentHash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? content.ContentHash[7..] : content.ContentHash;
        if (content.FileId.Value == Guid.Empty || content.RevisionId.Value == Guid.Empty || content.SizeBytes < 0 ||
            hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)) || string.IsNullOrWhiteSpace(content.Name) ||
            content.Name.IndexOfAny(['/', '\\', '\0']) >= 0 || content.Name != Path.GetFileName(content.Name) || content.Name is "." or ".." ||
            string.IsNullOrWhiteSpace(content.ProviderContentReference) || Path.IsPathFullyQualified(content.ProviderContentReference) ||
            content.ProviderContentReference.Contains('\0') || content.ProviderContentReference.Split(['/', '\\']).Any(part => part is "." or ".."))
            return Fail<FilesRevision>(FilesErrorCode.InvalidState, "Content requires safe canonical identities, name, hash and immutable Files-relative reference.", "CommitFileContent", content.FileId);
        FilesResult<FilesRevision>? result = null;
        await _store.UpdateAsync(state =>
        {
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
        }, cancellationToken).ConfigureAwait(false);
        if (result!.IsSuccess) foreach (var subscriber in _subscribers.Values) subscriber.Writer.TryWrite(true);
        return result;
    }
}
