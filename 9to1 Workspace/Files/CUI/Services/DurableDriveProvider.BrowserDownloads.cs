namespace HavenOS.Files;

// Durable selection mapping in the SAME Files envelope. Browser paths/URL values
// are deliberately absent; the owning native service supplies separate Home WRITE.
public sealed record FilesBrowserDownloadRegistration(Guid OperationId, Guid DownloadId, Guid ActionId,
    Guid FilesStoreId, string ActorId, string ProfileId, FilesUploadedContent OriginalContent);

public sealed partial class DurableDriveProvider
{
    public async Task<FilesResult<FilesRevision>> CommitOriginalBrowserDownloadAsync(
        FilesBrowserDownloadRegistration originalRegistration, string originalStoreRevision,
        FilesItemRevisionPrecondition originalParent, FilesCommitAuthorityGuard originalAuthority,
        Func<CancellationToken, Task> copyOriginalContent, Action<Action> originalScope,
        Action<Task> retainOriginalTask, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalRegistration); ArgumentNullException.ThrowIfNull(originalParent);
        ArgumentNullException.ThrowIfNull(originalAuthority); ArgumentNullException.ThrowIfNull(copyOriginalContent);
        var registration = originalRegistration; var content = registration.OriginalContent;
        var hash = content.ContentHash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? content.ContentHash[7..] : content.ContentHash;
        if (registration.OperationId == Guid.Empty || registration.DownloadId == Guid.Empty || registration.ActionId == Guid.Empty ||
            registration.FilesStoreId == Guid.Empty || registration.ActorId != _owner || originalAuthority.ActorId != _owner ||
            !Guid.TryParse(registration.ProfileId, out var profile) || profile == Guid.Empty ||
            content.ActorId != _owner || content.ExpectedRevision is not null || content.FileId.Value == Guid.Empty ||
            content.RevisionId.Value == Guid.Empty || content.ParentFolderId != originalParent.ItemId || originalParent.ExpectedRevision is null ||
            content.CommittedAt == default || content.SizeBytes < 0 || content.SizeBytes > 250L * 1024 * 1024 ||
            hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)) ||
            string.IsNullOrWhiteSpace(originalStoreRevision) || string.IsNullOrWhiteSpace(content.Name) || content.Name.Length > 255 ||
            content.Name is "." or ".." || content.Name.Any(character => char.IsControl(character) || "/\\<>:\"|?*".Contains(character)) ||
            content.ProviderContentReference != ".9to1-browser-" + content.RevisionId.Value.ToString("N") + ".content")
            return Fail<FilesRevision>(FilesErrorCode.InvalidState,
                "Retain the original download, exact canonical destination and once-created immutable upload identities.", "RegisterBrowserDownload", content.FileId);
        FilesResult<FilesRevision>? result = null;
        await _store.UpdateExistingWithinOriginalSourceAsync(async (state, cancellation) =>
        {
            if (state.StoreId != registration.FilesStoreId || state.StoreOwnerPrincipalId != _owner || state.StoreLocationId != Location.Id)
                throw new UnauthorizedAccessException("The SAME original Files store is required.");
            var mappings = state.BrowserDownloads ?? [];
            if (mappings.Any(item => item is null || item.OriginalContent is null))
                throw new InvalidDataException("The saved Browser registration collection is incomplete; preserve it.");
            var matches = mappings.Where(item => item.OperationId == registration.OperationId ||
                item.DownloadId == registration.DownloadId || item.OriginalContent.FileId == content.FileId ||
                item.OriginalContent.RevisionId == content.RevisionId).Take(2).ToArray();
            if (matches.Length != 0)
            {
                if (matches.Length != 1 || matches[0] != registration)
                    throw new InvalidDataException("The original registration IDs already describe different content; preserve both observations.");
                var saved = state.UploadedContents.SingleOrDefault(item => item.RevisionId == content.RevisionId);
                var item = state.Items.SingleOrDefault(item => item.Metadata.Id == content.FileId);
                var revision = state.Revisions.SingleOrDefault(item => item.Id == content.RevisionId);
                if (saved != content || item is null || !IsVisible(state, item) || item.Metadata.CurrentRevisionId != content.RevisionId ||
                    item.Metadata.ParentId != content.ParentFolderId || revision is null || !revision.IsCurrent ||
                    state.RevisionContentReferences.GetValueOrDefault(content.RevisionId.ToString()) != content.ProviderContentReference)
                    throw new InvalidDataException("The exact saved Browser mapping no longer has its original current uploaded revision.");
                result = FilesResult<FilesRevision>.Success(revision);
                return state; // Observe the SAME acknowledged pair; no copy/promotion replay.
            }
            var actualRevision = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(state)));
            if (actualRevision != originalStoreRevision)
            { result = Fail<FilesRevision>(FilesErrorCode.RevisionConflict, "The displayed Files destination changed; refresh before registration.", "RegisterBrowserDownload", content.FileId); return state; }
            if (state.Items.Any(item => item.Metadata.Id == content.FileId) || state.Revisions.Any(item => item.Id == content.RevisionId))
            { result = Fail<FilesRevision>(FilesErrorCode.InvalidState, "The once-created Files upload identity already exists.", "RegisterBrowserDownload", content.FileId); return state; }
            var next = ApplyUploadedContentToState(state, content, [originalParent], registration.FilesStoreId, out result);
            if (result?.IsSuccess != true) return state;
            // Every maintained metadata/name/parent precondition succeeded before bytes.
            // The same guard now takes and retains Home through copy and metadata publish.
            await originalAuthority.ValidateAsync(cancellation).ConfigureAwait(false);
            await FilesOriginalDeveloperTaskSource.ObserveAsync(() => copyOriginalContent(cancellation),
                originalScope, retainOriginalTask).ConfigureAwait(false);
            await originalAuthority.ValidateAsync(cancellation).ConfigureAwait(false);
            return next with { BrowserDownloads = [.. mappings, registration] };
        }, originalAuthority.ValidateAsync, originalScope, retainOriginalTask, token).ConfigureAwait(false);
        if (result is null) throw new InvalidOperationException("No actual Browser registration provider result was observed.");
        if (result.IsSuccess) foreach (var subscriber in _subscribers.Values) subscriber.Writer.TryWrite(true);
        return result;
    }

    // The native issuer must obtain actual current Home Files READ before this lookup.
    // Download IDs select metadata only and do not grant access or return byte paths.
    public async Task<FilesBrowserDownloadRegistration?> ReadOriginalBrowserDownloadRegistrationAsync(
        Guid originalStoreId, Guid originalDownloadId, Guid originalActionId,
        Action<Action> originalScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
        if (originalStoreId == Guid.Empty || originalDownloadId == Guid.Empty || originalActionId == Guid.Empty)
            throw new ArgumentException("Select the original store and download row.");
        var state = await FilesOriginalDeveloperTaskSource.ObserveAsync(() => _store.ReadExistingAsync(token),
            originalScope, retainOriginalTask).ConfigureAwait(false);
        if (state.StoreId != originalStoreId || state.StoreOwnerPrincipalId != _owner || state.StoreLocationId != Location.Id)
            throw new UnauthorizedAccessException("The original Files store changed.");
        var mappings = state.BrowserDownloads ?? [];
        if (mappings.Any(item => item is null || item.OriginalContent is null))
            throw new InvalidDataException("The saved Browser mapping collection is incomplete.");
        var matches = mappings.Where(item => item.DownloadId == originalDownloadId).Take(2).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1 || matches[0].ActionId != originalActionId || matches[0].FilesStoreId != originalStoreId || matches[0].ActorId != _owner)
            throw new InvalidDataException("The original Browser mapping does not match its exact saved Files identity.");
        return matches[0];
    }
    public void DemandExternalOriginalBrowserDownloadJoin() => _store.ThrowIfOriginalUpdateJoinWouldCycle();
    public Task? OriginalBrowserDownloadsClose => _store.OriginalUpdatesClose;
    public Task CloseOriginalBrowserDownloadsAndDrainAsync() => _store.CloseOriginalUpdatesAndDrainAsync();
}
