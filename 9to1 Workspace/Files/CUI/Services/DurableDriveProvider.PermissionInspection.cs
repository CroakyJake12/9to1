namespace HavenOS.Files;

public sealed partial class DurableDriveProvider
{
    /// <summary>Lists the active direct and inherited grants for one canonical item.
    /// Callers must expose this owner-scoped provider operation only after normal Files authorization.</summary>
    public async Task<FilesPage<FilesPrincipalGrant>> GetGrantsAsync(HostedItemId itemId, string? pageToken,
        CancellationToken cancellationToken = default)
    {
        var state = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        ValidateSharingState(state);
        var item = state.Items.SingleOrDefault(entry => entry.Metadata.Id == itemId && IsVisible(state, entry));
        if (item is null) throw new KeyNotFoundException($"Files item {itemId} was not found.");

        var grants = EffectiveGrants(state, itemId, _shareClock.GetUtcNow());
        int offset = ParseOffset(pageToken);
        if (offset > grants.Length) throw new ArgumentException("Invalid grant page token.", nameof(pageToken));
        const int pageSize = 100;
        var page = grants.Skip(offset).Take(pageSize).ToArray();
        return new(page, offset + page.Length < grants.Length
            ? (offset + page.Length).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null);
    }

    /// <summary>Previews the target item's effective grants at its current and proposed parent.
    /// It is read-only; the returned changes do not authorize or commit a move.</summary>
    public async Task<FilesPermissionChangePreview> PreviewMovePermissionsAsync(HostedItemId itemId,
        HostedItemId? destinationFolderId, FilesPermissionInheritancePolicy policy,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(policy)) throw new ArgumentOutOfRangeException(nameof(policy));
        var state = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        ValidateSharingState(state);
        var item = state.Items.SingleOrDefault(entry => entry.Metadata.Id == itemId && IsVisible(state, entry));
        if (item is null) throw new KeyNotFoundException($"Files item {itemId} was not found.");
        if (destinationFolderId is { } destinationId)
        {
            var destination = state.Items.SingleOrDefault(entry => entry.Metadata.Id == destinationId && IsVisible(state, entry));
            if (destination is null || destination.Metadata.Kind is not HostedItemKind.Folder)
                throw new ArgumentException("The destination is not a visible folder.", nameof(destinationFolderId));
            if (destinationId == itemId || IsDescendantOf(state, destination, itemId))
                throw new ArgumentException("A folder cannot be moved into itself or one of its descendants.", nameof(destinationFolderId));
        }

        var now = _shareClock.GetUtcNow();
        var inheritedToPreserve = policy is FilesPermissionInheritancePolicy.PreserveExplicitGrants
            ? InheritedSharesToPreserve(state, itemId, destinationFolderId, now)
            : Array.Empty<FilesEmailShare>();
        var affected = state.Items.Where(entry => IsVisible(state, entry) &&
            (entry.Metadata.Id == itemId || IsDescendantOf(state, entry, itemId)))
            .OrderBy(entry => entry.Metadata.Id.Value).Select(entry =>
            {
                var currentGrants = EffectiveGrants(state, entry.Metadata.Id, now);
                var resultingGrants = EffectiveGrants(state, entry.Metadata.Id, now, itemId,
                    destinationFolderId, inheritedToPreserve);
                return new FilesItemPermissionChange(entry.Metadata.Id, currentGrants, resultingGrants);
            }).ToArray();
        var current = affected.Single(change => change.ItemId == itemId).CurrentEffectiveGrants;
        var resulting = affected.Single(change => change.ItemId == itemId).ResultingEffectiveGrants;
        var changed = affected.SelectMany(change =>
            ChangedPrincipals(change.CurrentEffectiveGrants, change.ResultingEffectiveGrants))
            .Distinct(StringComparer.Ordinal).OrderBy(principal => principal, StringComparer.Ordinal).ToArray();
        return new FilesPermissionChangePreview(itemId, item.Metadata.ParentId, destinationFolderId, policy,
            current, resulting, changed,
            policy is FilesPermissionInheritancePolicy.RequireApprovalOnChange && changed.Length != 0,
            affected);
    }

    private FilesPrincipalGrant[] EffectiveGrants(State state, HostedItemId itemId, DateTimeOffset now) =>
        EffectiveGrants(state, itemId, now, null, null);

    private FilesPrincipalGrant[] EffectiveGrants(State state, HostedItemId itemId, DateTimeOffset now,
        HostedItemId? movedRootId, HostedItemId? replacementParentId,
        IReadOnlyList<FilesEmailShare>? preservedInheritedGrants = null)
    {
        var chain = new List<HostedItemId> { itemId };
        var currentItem = state.Items.Single(entry => entry.Metadata.Id == itemId);
        HostedItemId? parentId = currentItem.Metadata.Id == movedRootId
            ? replacementParentId : currentItem.Metadata.ParentId;
        var seen = new HashSet<HostedItemId> { itemId };
        while (parentId is { } currentId)
        {
            if (!seen.Add(currentId)) throw new InvalidDataException("Files item ancestry contains a cycle.");
            var ancestor = state.Items.SingleOrDefault(entry => entry.Metadata.Id == currentId && IsVisible(state, entry));
            if (ancestor is null || ancestor.Metadata.Kind is not HostedItemKind.Folder)
                throw new InvalidDataException("Files item ancestry is unavailable or invalid.");
            chain.Add(currentId);
            parentId = ancestor.Metadata.Id == movedRootId
                ? replacementParentId : ancestor.Metadata.ParentId;
        }

        var itemOwner = currentItem.Metadata.OwnerPrincipalId;
        var ownerGrant = new FilesPrincipalGrant(itemOwner, FilesAccessRole.Owner, FilesGrantOrigin.Direct,
            itemId, state.StoreCreatedAtUtc ?? currentItem.Metadata.CreatedAt, itemOwner);
        var scopedGrants = chain.SelectMany((scopeId, index) =>
        {
            var grants = state.Shares.Where(entry => entry.Share.ItemId == scopeId && IsActiveShare(entry, now))
                .Select(entry => new FilesPrincipalGrant(entry.Share.PrincipalId, entry.Share.Role,
                    index == 0 ? FilesGrantOrigin.Direct : FilesGrantOrigin.Inherited,
                    scopeId, entry.Share.CreatedAt, entry.GrantedByPrincipalId, entry.RecipientEmail));
            if (scopeId != movedRootId || preservedInheritedGrants is null) return grants;
            var preserved = preservedInheritedGrants.Select(entry => new FilesPrincipalGrant(entry.Share.PrincipalId,
                entry.Share.Role, index == 0 ? FilesGrantOrigin.Direct : FilesGrantOrigin.Inherited,
                scopeId, entry.Share.CreatedAt, entry.GrantedByPrincipalId, entry.RecipientEmail));
            return grants.Concat(preserved);
        });
        return new[] { ownerGrant }.Concat(scopedGrants)
            .OrderBy(grant => grant.PrincipalId, StringComparer.Ordinal)
            .ThenBy(grant => grant.Origin)
            .ThenBy(grant => grant.GrantedOnItemId.Value)
            .ToArray();
    }

    private FilesEmailShare[] InheritedSharesToPreserve(State state, HostedItemId movedItemId,
        HostedItemId? destinationParentId, DateTimeOffset now)
    {
        var sourceAncestors = AncestorIds(state, state.Items.Single(entry => entry.Metadata.Id == movedItemId).Metadata.ParentId);
        var destinationAncestors = AncestorIds(state, destinationParentId).ToHashSet();
        return state.Shares.Where(entry => sourceAncestors.Contains(entry.Share.ItemId) &&
                !destinationAncestors.Contains(entry.Share.ItemId) && IsActiveShare(entry, now))
            .ToArray();
    }

    private static HashSet<HostedItemId> AncestorIds(State state, HostedItemId? parentId)
    {
        var result = new HashSet<HostedItemId>();
        while (parentId is { } currentId)
        {
            if (!result.Add(currentId)) throw new InvalidDataException("Files item ancestry contains a cycle.");
            var parent = state.Items.SingleOrDefault(entry => entry.Metadata.Id == currentId && IsVisible(state, entry));
            if (parent is null || parent.Metadata.Kind is not HostedItemKind.Folder)
                throw new InvalidDataException("Files item ancestry is unavailable or invalid.");
            parentId = parent.Metadata.ParentId;
        }
        return result;
    }

    private static string[] ChangedPrincipals(IEnumerable<FilesPrincipalGrant> current,
        IEnumerable<FilesPrincipalGrant> resulting)
    {
        var currentRoles = EffectiveRoles(current);
        var resultingRoles = EffectiveRoles(resulting);
        return currentRoles.Keys.Concat(resultingRoles.Keys).Distinct(StringComparer.Ordinal)
            .Where(principal => !currentRoles.TryGetValue(principal, out var before) ||
                !resultingRoles.TryGetValue(principal, out var after) || before != after)
            .ToArray();
    }

    private bool MoveChangesEffectivePermissions(State state, HostedItemId itemId,
        HostedItemId? destinationFolderId, DateTimeOffset now)
    {
        foreach (var entry in state.Items.Where(value => IsVisible(state, value) &&
            (value.Metadata.Id == itemId || IsDescendantOf(state, value, itemId))))
        {
            var current = EffectiveGrants(state, entry.Metadata.Id, now);
            var resulting = EffectiveGrants(state, entry.Metadata.Id, now, itemId, destinationFolderId);
            if (ChangedPrincipals(current, resulting).Length != 0) return true;
        }
        return false;
    }

    private static Dictionary<string, FilesAccessRole> EffectiveRoles(IEnumerable<FilesPrincipalGrant> grants) =>
        grants.GroupBy(grant => grant.PrincipalId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => group.Any(grant => grant.Role is FilesAccessRole.Owner)
                    ? FilesAccessRole.Owner
                    : group.Any(grant => grant.Role is FilesAccessRole.Editor)
                        ? FilesAccessRole.Editor : FilesAccessRole.Viewer,
                StringComparer.Ordinal);

    private static bool IsDescendantOf(State state, Entry candidate, HostedItemId ancestorId)
    {
        var seen = new HashSet<HostedItemId>();
        HostedItemId? parentId = candidate.Metadata.ParentId;
        while (parentId is { } currentId)
        {
            if (currentId == ancestorId) return true;
            if (!seen.Add(currentId)) throw new InvalidDataException("Files item ancestry contains a cycle.");
            var parent = state.Items.SingleOrDefault(entry => entry.Metadata.Id == currentId && IsVisible(state, entry));
            if (parent is null) throw new InvalidDataException("Files item ancestry is unavailable.");
            parentId = parent.Metadata.ParentId;
        }
        return false;
    }
}
