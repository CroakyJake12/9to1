namespace HavenOS.Files;

/// <summary>Verified current local-source observation supplied by the genuine original native
/// setup producer. This descriptor is neither authority nor an immutable content reference.</summary>
public sealed record FilesOriginalLocalDeveloperSource(HostedItemId FileId, HostedItemId ParentFolderId,
    string Name, string? MimeType, FilesRevisionId RevisionId, string ActorId, DateTimeOffset ObservedAt,
    long SizeBytes, string ContentSha256);

public sealed partial class DurableDriveProvider
{
    /// <summary>Create-only canonical local source metadata, without copying/overwriting bytes.
    /// The SAME genuine Home original entry and current native handle/root predicate must remain
    /// held through this original result/cleanup. No UploadedContent or immutable historical
    /// reference is created; content readers must refuse unavailable immutable revision bytes.</summary>
    public async Task<FilesResult<FilesRevision>> RegisterOriginalLocalDeveloperFileAsync(
        FilesOriginalLocalDeveloperSource observedSource, IReadOnlyList<FilesItemRevisionPrecondition> originalParents,
        Guid originalStoreId, FilesCommitAuthorityGuard originalAuthority, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observedSource); ArgumentNullException.ThrowIfNull(originalParents);
        ArgumentNullException.ThrowIfNull(originalAuthority);
        var source = observedSource; var parents = originalParents.ToArray();
        FilesError Error(FilesErrorCode code, string message) => new(code, message,
            "9to1.Files.RegisterLocalDeveloperSource", source.FileId.ToString(), true, false);
        if (source.ActorId != _owner || originalAuthority.ActorId != _owner || originalStoreId == Guid.Empty ||
            source.FileId.Value == Guid.Empty || source.ParentFolderId.Value == Guid.Empty || source.RevisionId.Value == Guid.Empty ||
            source.ObservedAt == default || source.SizeBytes < 0 || source.SizeBytes > 4L * 1024 * 1024 ||
            string.IsNullOrWhiteSpace(source.Name) || source.Name is "." or ".." || source.Name != Path.GetFileName(source.Name) ||
            source.Name.Any(c => c < 32 || c is '/' or '\\' or ':' or '\0') ||
            source.ContentSha256 is not { Length: 64 } || !source.ContentSha256.All(Uri.IsHexDigit) ||
            parents.Length is 0 or > 65 || parents.Any(value => value is null || value.ItemId.Value == Guid.Empty || value.ExpectedRevision is null) ||
            parents.Select(value => value.ItemId).Distinct().Count() != parents.Length || !parents.Any(value => value.ItemId == source.ParentFolderId))
            return FilesResult<FilesRevision>.Failure(Error(FilesErrorCode.InvalidState, "Retain the original finite verified local source and canonical parent observations."));
        FilesRevision? acknowledged = null;
        Task<State> actual;
        try
        {
            actual = _store.UpdateAsync(state =>
            {
                void Refuse(FilesErrorCode code, string message) => throw new OriginalLocalDeveloperRefusal(Error(code, message));
                if (state.StoreId != originalStoreId || state.StoreOwnerPrincipalId != _owner || state.StoreLocationId != Location.Id)
                    Refuse(FilesErrorCode.RevisionConflict, "The actual original Files store changed.");
                foreach (var parent in parents)
                {
                    var current = state.Items.SingleOrDefault(value => value.Metadata.Id == parent.ItemId);
                    if (current is null || !IsVisible(state, current) || current.Metadata.Kind != HostedItemKind.Folder ||
                        current.Metadata.CurrentRevisionId != parent.ExpectedRevision)
                        Refuse(FilesErrorCode.RevisionConflict, "An actual original project parent changed before local registration.");
                }
                if (state.Items.Any(value => value.Metadata.Id == source.FileId) || state.Revisions.Any(value => value.Id == source.RevisionId))
                    Refuse(FilesErrorCode.InvalidState, "The once-created source identity/revision already exists; inspect without replay/remint.");
                if (state.Items.Any(value => !value.Deleted && value.Metadata.ParentId == source.ParentFolderId &&
                    value.Metadata.Name.Equals(source.Name, StringComparison.OrdinalIgnoreCase)))
                    Refuse(FilesErrorCode.NameConflict, "The canonical source destination already has that name.");
                var hash = "sha256:" + source.ContentSha256.ToLowerInvariant();
                var metadata = new HostedItemMetadata(source.FileId, Location.Id, source.ParentFolderId, source.Name,
                    HostedItemKind.File, source.MimeType, _owner, "personal", source.SizeBytes, source.ObservedAt, source.ObservedAt,
                    source.RevisionId, SyncAvailability.AvailableOffline, false, hash);
                var revision = new FilesRevision(source.RevisionId, source.FileId, null, source.ObservedAt, _owner,
                    "local-source-observation", hash, source.SizeBytes, "dev", source.RevisionId.ToString(), true);
                var change = new FilesChangeEvent(Guid.NewGuid().ToString("N"), new(state.Events.Count + 1), null,
                    source.FileId, _owner, "LocalDeveloperSourceRegistered", null, source.RevisionId, source.ObservedAt, metadata);
                acknowledged = revision;
                return state with { Items = [.. state.Items, new Entry(metadata)], Revisions = [.. state.Revisions, revision],
                    Events = [.. state.Events, change] };
                // UploadedContents and RevisionContentReferences remain exact original collections.
            }, originalAuthority.ValidateAsync, cancellationToken);
        }
        catch (OperationCanceledException original)
        {
            // No original Task was returned by source acquisition. Preserve this exact direct
            // cause as a fault, independently from a genuine returned Task's cancellation.
            throw new AggregateException("Original local-source acquisition failed before returning its Task; effect may be unknown.", original);
        }
        try { await actual.ConfigureAwait(false); }
        catch (OriginalLocalDeveloperRefusal refusal) when (actual.Exception?.InnerExceptions.Count == 1) { return FilesResult<FilesRevision>.Failure(refusal.OriginalError); }
        catch (FilesCommitAuthorityChangedException) when (actual.Exception?.InnerExceptions.Count == 1)
        { return FilesResult<FilesRevision>.Failure(Error(FilesErrorCode.PermissionDenied, "The genuine final source/Home authority changed before publication.")); }
        catch when (actual.IsFaulted) { throw actual.Exception!; }
        if (acknowledged is null) throw new InvalidOperationException("No original local-source metadata acknowledgement was observed.");
        foreach (var subscriber in _subscribers.Values) subscriber.Writer.TryWrite(true);
        return FilesResult<FilesRevision>.Success(acknowledged);
    }
    private sealed class OriginalLocalDeveloperRefusal(FilesError originalError) : InvalidOperationException(originalError.Message)
    { internal FilesError OriginalError { get; } = originalError; }
}
