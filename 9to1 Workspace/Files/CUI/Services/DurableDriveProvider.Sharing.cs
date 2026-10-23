using System.Net.Mail;

namespace HavenOS.Files;

public sealed partial class DurableDriveProvider
{
    /// <summary>Trusted typed infrastructure entry point, after Home's security-sensitive approval.
    /// The same held Home/Files guard must cover this exact store, item, recipient, role and expiry.
    /// Recipient resolution is the authenticated account service's responsibility; an address is not authority.</summary>
    public async Task<FilesResult<FilesEmailShare>> GrantEmailAccessAsync(FilesEmailShareRequest request,
        FilesCommitAuthorityGuard ownerAuthority, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(ownerAuthority);
        if (ownerAuthority.ActorId != _owner)
            return ShareDenied<FilesEmailShare>(request.ItemId);
        if (request.StoreId == Guid.Empty || request.ItemId.Value == Guid.Empty || request.ExpectedRevision.Value == Guid.Empty ||
            request.OperationId.Value == Guid.Empty || request.Role is not (FilesAccessRole.Viewer or FilesAccessRole.Editor) ||
            string.IsNullOrWhiteSpace(request.RecipientPrincipalId) || request.RecipientPrincipalId == _owner ||
            request.RecipientPrincipalId.Length > 256 || request.RecipientPrincipalId.Any(char.IsWhiteSpace) || request.RecipientPrincipalId.Any(char.IsControl) ||
            request.RecipientPrincipalId.Contains('@') || !IsShareAddress(request.RecipientEmail))
            return Fail<FilesEmailShare>(FilesErrorCode.InvalidState, "Select a resolved stable recipient, a valid address and Viewer or Editor scope.", "Share.Grant", request.ItemId);

        FilesResult<FilesEmailShare>? result = null;
        try
        {
            var selected = await _store.ReadExistingAsync(cancellationToken).ConfigureAwait(false);
            ValidateSharingState(selected);
            await _store.UpdateAsync(state =>
            {
                ValidateSharingState(state);
                if (state.StoreId != selected.StoreId) throw new FilesCommitAuthorityChangedException();
                if (state.StoreId != request.StoreId) throw new FilesCommitAuthorityChangedException();
                var prior = state.ShareJournal.SingleOrDefault(entry => entry.OperationId == request.OperationId);
                if (prior is not null)
                {
                    result = prior.ActorId == _owner && prior.Action == "Share.Grant" && prior.GrantRequest == request
                        ? FilesResult<FilesEmailShare>.Success(state.Shares.Single(entry => entry.Share.Id == prior.ShareId))
                        : Fail<FilesEmailShare>(FilesErrorCode.InvalidState, "The operation ID already identifies a different permission change.", "Share.Grant", request.ItemId);
                    return state;
                }
                if (state.Operations.Any(entry => entry.Id == request.OperationId))
                { result = Fail<FilesEmailShare>(FilesErrorCode.InvalidState, "The operation ID is already in use.", "Share.Grant", request.ItemId); return state; }
                var now = _shareClock.GetUtcNow();
                var target = state.Items.SingleOrDefault(entry => entry.Metadata.Id == request.ItemId);
                if (target is null || !IsVisible(state, target))
                { result = Fail<FilesEmailShare>(FilesErrorCode.ItemNotFound, "The canonical item is unavailable.", "Share.Grant", request.ItemId); return state; }
                if (target.Metadata.IsShared && !state.Shares.Any(entry => entry.Share.ItemId == request.ItemId))
                    throw new FilesCommitAuthorityChangedException();
                if (target.Metadata.CurrentRevisionId != request.ExpectedRevision)
                { result = Fail<FilesEmailShare>(FilesErrorCode.RevisionConflict, "The item changed; review the current permission scope.", "Share.Grant", request.ItemId); return state; }
                if (request.ExpiresAt is { } expiry && expiry <= now)
                { result = Fail<FilesEmailShare>(FilesErrorCode.InvalidState, "Choose a future expiry.", "Share.Grant", request.ItemId); return state; }
                var granted = new FilesEmailShare(new(new(Guid.NewGuid()), request.ItemId, request.RecipientPrincipalId,
                    request.Role, now, request.ExpiresAt, null, false), request.RecipientEmail, _owner);
                // A new explicit decision replaces this principal's direct grant, never enlarges it by union.
                var shares = state.Shares.Select(entry => entry.Share.ItemId == request.ItemId &&
                    entry.Share.PrincipalId == request.RecipientPrincipalId ? entry with { Share = entry.Share with { IsRevoked = true } } : entry)
                    .Append(granted).ToArray();
                result = FilesResult<FilesEmailShare>.Success(granted);
                return PublishShare(state, target, shares, request.OperationId, "Share.Grant", request.ExpectedRevision, granted, request, now);
            }, ownerAuthority.ValidateAsync, cancellationToken).ConfigureAwait(false);
        }
        catch (FilesCommitAuthorityChangedException) { return ShareDenied<FilesEmailShare>(request.ItemId); }
        catch (FileNotFoundException) { return ShareUnavailable<FilesEmailShare>(request.ItemId); }
        if (result!.IsSuccess) WakeShareObservers();
        return result;
    }

    public async Task<FilesResult<FilesEmailShare>> RevokeEmailAccessAsync(FilesShareId shareId,
        FilesRevisionId expectedRevision, FilesOperationId operationId, FilesCommitAuthorityGuard ownerAuthority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownerAuthority);
        if (ownerAuthority.ActorId != _owner) return ShareDenied<FilesEmailShare>(default);
        if (shareId.Value == Guid.Empty || operationId.Value == Guid.Empty || expectedRevision.Value == Guid.Empty)
            return Fail<FilesEmailShare>(FilesErrorCode.InvalidState, "Retain the exact share, item revision and operation ID.", "Share.Revoke", default);
        FilesResult<FilesEmailShare>? result = null;
        try
        {
            var selected = await _store.ReadExistingAsync(cancellationToken).ConfigureAwait(false);
            ValidateSharingState(selected);
            await _store.UpdateAsync(state =>
            {
                ValidateSharingState(state);
                if (state.StoreId != selected.StoreId) throw new FilesCommitAuthorityChangedException();
                var prior = state.ShareJournal.SingleOrDefault(entry => entry.OperationId == operationId);
                if (prior is not null)
                {
                    result = prior.ActorId == _owner && prior.Action == "Share.Revoke" && prior.ShareId == shareId && prior.ExpectedRevision == expectedRevision
                        ? FilesResult<FilesEmailShare>.Success(state.Shares.Single(entry => entry.Share.Id == prior.ShareId))
                        : Fail<FilesEmailShare>(FilesErrorCode.InvalidState, "The operation ID already identifies a different permission change.", "Share.Revoke", default);
                    return state;
                }
                var grant = state.Shares.SingleOrDefault(entry => entry.Share.Id == shareId);
                if (grant is null) { result = ShareDenied<FilesEmailShare>(default); return state; }
                var target = state.Items.SingleOrDefault(entry => entry.Metadata.Id == grant.Share.ItemId);
                if (state.Operations.Any(entry => entry.Id == operationId))
                { result = Fail<FilesEmailShare>(FilesErrorCode.InvalidState, "The operation ID is already in use.", "Share.Revoke", grant.Share.ItemId); return state; }
                if (target is null || target.Metadata.CurrentRevisionId != expectedRevision)
                { result = Fail<FilesEmailShare>(FilesErrorCode.RevisionConflict, "The item changed; inspect its current grants before revoking.", "Share.Revoke", grant.Share.ItemId); return state; }
                var revoked = grant with { Share = grant.Share with { IsRevoked = true } };
                var shares = state.Shares.Select(entry => entry.Share.Id == shareId ? revoked : entry).ToArray();
                result = FilesResult<FilesEmailShare>.Success(revoked);
                return PublishShare(state, target, shares, operationId, "Share.Revoke", expectedRevision, revoked, null, _shareClock.GetUtcNow());
            }, ownerAuthority.ValidateAsync, cancellationToken).ConfigureAwait(false);
        }
        catch (FilesCommitAuthorityChangedException) { return ShareDenied<FilesEmailShare>(default); }
        catch (FileNotFoundException) { return ShareUnavailable<FilesEmailShare>(default); }
        if (result!.IsSuccess) WakeShareObservers();
        return result;
    }

    /// <summary>The current CUI Files browser can consume this same canonical provider contract.
    /// The trusted host supplies the recipient's live authenticated guard, never an email/request principal.
    /// Views expose metadata and scoped Rename only; bytes and owning-app/Data edits retain their separate admission.</summary>
    public async Task<FilesResult<IFilesProvider>> OpenSharedViewAsync(FilesShareId shareId,
        FilesCommitAuthorityGuard recipientAuthority, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipientAuthority);
        return await ReadSharedAsync<IFilesProvider>(shareId, recipientAuthority, (state, grant) =>
        {
            var root = state.Items.Single(entry => entry.Metadata.Id == grant.Share.ItemId).Metadata;
            return FilesResult<IFilesProvider>.Success(new SharedView(this, grant, root.Name, recipientAuthority, state.StoreId!.Value));
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FilesResult<FilesPage<FilesEmailShare>>> GetEmailSharesAsync(HostedItemId itemId,
        string? pageToken, FilesCommitAuthorityGuard ownerAuthority, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownerAuthority);
        if (ownerAuthority.ActorId != _owner) return ShareDenied<FilesPage<FilesEmailShare>>(itemId);
        try
        {
            await using var lease = await _store.AcquireExistingReadLeaseAsync(cancellationToken).ConfigureAwait(false);
            await ownerAuthority.ValidateAsync(cancellationToken).ConfigureAwait(false);
            var state = lease.Snapshot; ValidateSharingState(state);
            var offset = ParseOffset(pageToken);
            var grants = state.Shares.Where(entry => entry.Share.ItemId == itemId).OrderBy(entry => entry.Share.CreatedAt).ThenBy(entry => entry.Share.Id.Value).ToArray();
            if (offset > grants.Length) throw new ArgumentException("Invalid share page token.", nameof(pageToken));
            var page = grants.Skip(offset).Take(100).ToArray();
            await ownerAuthority.ValidateAsync(cancellationToken).ConfigureAwait(false);
            return FilesResult<FilesPage<FilesEmailShare>>.Success(new(page,
                offset + page.Length < grants.Length ? (offset + page.Length).ToString(System.Globalization.CultureInfo.InvariantCulture) : null));
        }
        catch (FilesCommitAuthorityChangedException) { return ShareDenied<FilesPage<FilesEmailShare>>(itemId); }
        catch (FileNotFoundException) { return ShareUnavailable<FilesPage<FilesEmailShare>>(itemId); }
    }

    private State PublishShare(State state, Entry target, IReadOnlyList<FilesEmailShare> shares,
        FilesOperationId operationId, string action, FilesRevisionId expectedRevision, FilesEmailShare result,
        FilesEmailShareRequest? request, DateTimeOffset now)
    {
        var revision = new FilesRevisionId(Guid.NewGuid());
        var metadata = target.Metadata with { CurrentRevisionId = revision, ModifiedAt = now,
            IsShared = shares.Any(entry => entry.Share.ItemId == target.Metadata.Id && IsActiveShare(entry, now)) };
        var operation = new FilesOperation(operationId, _owner, metadata.Id, metadata.ParentId, null, action,
            expectedRevision, revision, FilesOperationState.Committed, now, now, null, null, state.Events.Count + 1);
        var change = new FilesChangeEvent(Guid.NewGuid().ToString("N"), new(state.Events.Count + 1), operationId,
            metadata.Id, _owner, action, expectedRevision, revision, now, metadata);
        return state with { Shares = shares,
            ShareJournal = [.. state.ShareJournal, new(operationId, _owner, action, result.Share.Id, request, expectedRevision, result)],
            Items = state.Items.Select(entry => entry.Metadata.Id == metadata.Id ? entry with { Metadata = metadata } : entry).ToArray(),
            Operations = [.. state.Operations, operation], Events = [.. state.Events, change] };
    }

    private async Task<FilesResult<T>> ReadSharedAsync<T>(FilesShareId shareId, FilesCommitAuthorityGuard authority,
        Func<State, FilesEmailShare, FilesResult<T>> select, CancellationToken cancellationToken, Guid? expectedStoreId = null)
    {
        try
        {
            await using var lease = await _store.AcquireExistingReadLeaseAsync(cancellationToken).ConfigureAwait(false);
            await authority.ValidateAsync(cancellationToken).ConfigureAwait(false);
            var state = lease.Snapshot; ValidateSharingState(state);
            if (expectedStoreId is { } expected && state.StoreId != expected) return ShareDenied<T>(default);
            var grant = state.Shares.SingleOrDefault(entry => entry.Share.Id == shareId);
            if (grant is null || !CanUseShare(state, shareId, authority.ActorId, grant.Share.ItemId, write: false))
                return ShareDenied<T>(default);
            var result = select(state, grant);
            await authority.ValidateAsync(cancellationToken).ConfigureAwait(false);
            return CanUseShare(state, shareId, authority.ActorId, grant.Share.ItemId, write: false)
                ? result : ShareDenied<T>(default);
        }
        catch (FilesCommitAuthorityChangedException) { return ShareDenied<T>(default); }
        catch (FileNotFoundException) { return ShareUnavailable<T>(default); }
    }

    private bool CanUseShare(State state, FilesShareId shareId, string principalId, HostedItemId itemId, bool write)
    {
        ValidateSharingState(state);
        var grant = state.Shares.SingleOrDefault(entry => entry.Share.Id == shareId);
        if (grant is null || grant.Share.PrincipalId != principalId || !IsActiveShare(grant, _shareClock.GetUtcNow()) ||
            write && grant.Share.Role != FilesAccessRole.Editor) return false;
        var root = state.Items.SingleOrDefault(entry => entry.Metadata.Id == grant.Share.ItemId);
        var target = state.Items.SingleOrDefault(entry => entry.Metadata.Id == itemId);
        if (root is null || target is null || !IsVisible(state, root) || !IsVisible(state, target)) return false;
        if (root.Metadata.Id == target.Metadata.Id) return true;
        if (root.Metadata.Kind != HostedItemKind.Folder) return false;
        var seen = new HashSet<HostedItemId>();
        while (target.Metadata.ParentId is { } parent && seen.Add(target.Metadata.Id))
        {
            if (parent == root.Metadata.Id) return true;
            target = state.Items.SingleOrDefault(entry => entry.Metadata.Id == parent);
            if (target is null || target.Metadata.Kind != HostedItemKind.Folder) return false;
        }
        return false;
    }

    private void ValidateSharingState(State state)
    {
        if (state.StoreId is null || state.StoreId == Guid.Empty || state.StoreOwnerPrincipalId != _owner || state.StoreLocationId != Location.Id)
            throw new FilesCommitAuthorityChangedException();
        ValidateSharingRecords(state);
    }

    private void ValidateSharingRecords(State state)
    {
        if (state.Shares is null || state.ShareJournal is null ||
            state.Shares.Any(entry => entry is null || entry.Share is null || entry.Share.Id.Value == Guid.Empty ||
                entry.Share.ItemId.Value == Guid.Empty || entry.Share.Role is not (FilesAccessRole.Viewer or FilesAccessRole.Editor) ||
                string.IsNullOrWhiteSpace(entry.Share.PrincipalId) || entry.GrantedByPrincipalId != _owner || !IsShareAddress(entry.RecipientEmail)) ||
            state.Shares.Select(entry => entry.Share.Id).Distinct().Count() != state.Shares.Count ||
            state.ShareJournal.Any(entry => entry is null || entry.OperationId.Value == Guid.Empty || entry.ActorId != _owner ||
                entry.Action is not ("Share.Grant" or "Share.Revoke") || entry.Result is null) ||
            state.ShareJournal.Select(entry => entry.OperationId).Distinct().Count() != state.ShareJournal.Count)
            throw new InvalidDataException("Files sharing metadata is invalid; preserve it for recovery.");
    }

    /// <summary>Effective sharing is an observation, not a persisted ACL copy. Original personal
    /// project qualification consumes this SAME projection as normal Files metadata reads.</summary>
    private HostedItemMetadata ProjectSharingMetadata(State state, HostedItemMetadata metadata)
    {
        ValidateSharingRecords(state);
        var now = _shareClock.GetUtcNow();
        var seen = new HashSet<HostedItemId>();
        var current = metadata;
        while (seen.Add(current.Id))
        {
            var direct = state.Shares.Where(entry => entry.Share.ItemId == current.Id).ToArray();
            // Untracked legacy sharing cannot be silently interpreted as personal authority.
            if (direct.Any(entry => IsActiveShare(entry, now)) || current.IsShared && direct.Length == 0)
                return metadata with { IsShared = true };
            if (current.ParentId is not { } parent) return metadata with { IsShared = false };
            var ancestor = state.Items.SingleOrDefault(entry => entry.Metadata.Id == parent);
            if (ancestor is null || ancestor.Deleted || ancestor.Metadata.Kind != HostedItemKind.Folder) break;
            current = ancestor.Metadata;
        }
        throw new InvalidDataException("Files ancestry is unavailable; personal qualification is refused.");
    }

    private static bool IsShareAddress(string? address) => address is { Length: > 3 and <= 320 } &&
        !address.Any(char.IsControl) && MailAddress.TryCreate(address, out var parsed) && parsed.Address == address && parsed.DisplayName.Length == 0;
    private static bool IsActiveShare(FilesEmailShare grant, DateTimeOffset now) => !grant.Share.IsRevoked &&
        (grant.Share.ExpiresAt is null || grant.Share.ExpiresAt.Value > now);
    private static FilesResult<T> ShareDenied<T>(HostedItemId itemId) => Fail<T>(FilesErrorCode.PermissionDenied,
        "Current authenticated access does not cover this shared item and action.", "Share", itemId);
    private static FilesResult<T> ShareUnavailable<T>(HostedItemId itemId) => Fail<T>(FilesErrorCode.StorageUnavailable,
        "The existing canonical Files store is unavailable; restore it before retrying.", "Share", itemId);
    private void WakeShareObservers() { foreach (var subscriber in _subscribers.Values) subscriber.Writer.TryWrite(true); }

    private sealed class SharedView(DurableDriveProvider owner, FilesEmailShare grant, string name,
        FilesCommitAuthorityGuard authority, Guid storeId) : IFilesProvider
    {
        public FilesLocation Location { get; } = owner.Location with { Name = name,
            Capabilities = FilesProviderCapabilities.Read | FilesProviderCapabilities.Search |
                (grant.Share.Role == FilesAccessRole.Editor ? FilesProviderCapabilities.Rename : FilesProviderCapabilities.None) };
        private HostedItemMetadata Project(HostedItemMetadata metadata) => metadata with {
            ParentId = metadata.Id == grant.Share.ItemId ? null : metadata.ParentId, IsShared = true };

        public Task<FilesResult<HostedItemMetadata>> GetAsync(HostedItemId itemId, CancellationToken cancellationToken) =>
            owner.ReadSharedAsync(grant.Share.Id, authority, (state, _) =>
                owner.CanUseShare(state, grant.Share.Id, authority.ActorId, itemId, write: false)
                    ? FilesResult<HostedItemMetadata>.Success(Project(state.Items.Single(entry => entry.Metadata.Id == itemId).Metadata))
                    : ShareDenied<HostedItemMetadata>(itemId), cancellationToken, storeId);

        public async Task<FilesPage<HostedItemMetadata>> ListAsync(HostedItemId? parentId, FilesSearchQuery? query,
            string? pageToken, CancellationToken cancellationToken)
        {
            var result = await owner.ReadSharedAsync(grant.Share.Id, authority, (state, _) =>
            {
                if (query?.LocationId is { } location && location != Location.Id || query?.ParentId is { } queryParent && queryParent != parentId ||
                    parentId is { } parent && (!owner.CanUseShare(state, grant.Share.Id, authority.ActorId, parent, write: false) ||
                        state.Items.Single(entry => entry.Metadata.Id == parent).Metadata.Kind != HostedItemKind.Folder))
                    return ShareDenied<FilesPage<HostedItemMetadata>>(parentId ?? grant.Share.ItemId);
                var offset = ParseOffset(pageToken); var limit = query?.Limit ?? 100;
                if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(query));
                var items = state.Items.Where(entry => IsVisible(state, entry) &&
                    (parentId is null ? entry.Metadata.Id == grant.Share.ItemId : entry.Metadata.ParentId == parentId) &&
                    (query is null || entry.Metadata.Name.Contains(query.Text, StringComparison.OrdinalIgnoreCase)))
                    .Select(entry => Project(entry.Metadata)).OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Id.Value).ToArray();
                if (offset > items.Length) throw new ArgumentException("Invalid page token.", nameof(pageToken));
                var page = items.Skip(offset).Take(limit).ToArray();
                return FilesResult<FilesPage<HostedItemMetadata>>.Success(new(page,
                    offset + page.Length < items.Length ? (offset + page.Length).ToString(System.Globalization.CultureInfo.InvariantCulture) : null));
            }, cancellationToken, storeId).ConfigureAwait(false);
            return result.IsSuccess ? result.Value! : throw new UnauthorizedAccessException(result.Error!.Message);
        }

        public Task<FilesResult<FilesOperation>> MutateAsync(FilesOperation operation, string? newName, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(operation);
            if (operation.ActorId != authority.ActorId || operation.Operation != "Rename" ||
                operation.State != FilesOperationState.Pending || operation.Id.Value == Guid.Empty)
                return Task.FromResult(ShareDenied<FilesOperation>(operation.ItemId));
            return owner.MutateCoreAsync(operation, newName, null, [], authority, cancellationToken, sharedAccess: grant.Share.Id, sharedStoreId: storeId);
        }

        // A shared scope must not expose the owner's unfiltered event history/cursors.
        public Task<FilesPage<FilesChangeEvent>> GetChangesAsync(FilesChangeCursor? after, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Scoped Files change feed is unavailable.");
        public IAsyncEnumerable<FilesChangeEvent> SubscribeAsync(FilesChangeCursor? after, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Scoped Files change feed is unavailable.");
    }
}
