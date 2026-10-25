namespace HavenOS.Apps.Browse;

public sealed partial class BrowseChrome
{
    public bool CanManageTabGroups => _tabSessionPersistence is not null && !_disposed && !_nativeRetiring;
    public bool CanCreateTabGroup
    {
        get
        {
            if (!CanManageTabGroups) return false;
            var selected = _tabs.FirstOrDefault(tab => tab.Id == _selectedTabId);
            return IsGroupable(selected);
        }
    }
    public bool CanUngroupSelectedTab => CanManageTabGroups &&
        _tabs.FirstOrDefault(tab => tab.Id == _selectedTabId) is { } selected && TryGetGroupId(selected.Group, out _);
    public bool CanMoveSelectedTabToGroup(Guid groupId)
    {
        if (!CanManageTabGroups || _tabs.FirstOrDefault(tab => tab.Id == _selectedTabId) is not { } selected || !IsGroupable(selected)) return false;
        var group = _tabGroups.FirstOrDefault(item => item.Id == groupId);
        if (group is null) return false;
        if (StringComparer.Ordinal.Equals(selected.Group, group.Id.ToString("D"))) return false;
        var members = _tabs.Where(tab => tab.Group == group.Id.ToString("D")).ToArray();
        return members.Length > 0 && members.All(member => IsGroupCompatible(selected, member));
    }
    private long _tabPlacementRevision;
    private bool _tabPlacementNeedsSave;
    public bool HasUnsavedTabPlacement
    { get { lock (_nativeSourceGate) return !_disposed && !_nativeRetiring && _tabPlacementNeedsSave; } }

    /// <summary>Moves only within the tab's pinned/unpinned section, leaving Home in place.</summary>
    public bool CanMoveTab(Guid tabId, int direction)
    {
        if (_disposed || _nativeRetiring || direction is not (-1 or 1)) return false;
        lock (_nativeSourceGate)
        {
            var index = _tabs.FindIndex(tab => tab.Id == tabId);
            var next = index + direction;
            return index >= 0 && next >= 0 && next < _tabs.Count && !_tabs[index].IsHome &&
                IsSamePlacementOwner(_tabs[index], _tabs[next]);
        }
    }

    public Task<BrowseChromeSnapshot> SetTabPinnedAsync(Guid tabId, bool pinned, CancellationToken token = default)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        DemandPlaceableTab(tabId);
        return RetainContentTransition(() => SetTabPinnedCoreAsync(tabId, pinned, token), token);
    }

    private async Task<BrowseChromeSnapshot> SetTabPinnedCoreAsync(Guid tabId, bool pinned, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_nativeSourceGate)
        {
            var tab = DemandPlaceableTab(tabId);
            if (tab.IsPinned != pinned)
            {
                tab.IsPinned = pinned;
                NormalizeTabPlacement();
                _tabPlacementRevision = checked(_tabPlacementRevision + 1);
                _tabPlacementNeedsSave = true;
            }
        }
        return await SaveTabPlacementCoreAsync(pinned ? "Tab pinned." : "Tab unpinned.", token).ConfigureAwait(false);
    }

    public Task<BrowseChromeSnapshot> MoveTabAsync(Guid tabId, int direction, CancellationToken token = default)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        DemandPlaceableTab(tabId);
        var admittedOrder = CapturePlacementOrder();
        return RetainContentTransition(() => MoveTabCoreAsync(tabId, direction, admittedOrder, token), token);
    }

    /// <summary>Reorders one existing tab relative to another without crossing tab owners or pin sections.</summary>
    public Task<BrowseChromeSnapshot> MoveTabRelativeAsync(Guid tabId, Guid targetTabId, bool beforeTarget,
        CancellationToken token = default)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        DemandPlaceableTab(tabId); DemandPlaceableTab(targetTabId);
        if (tabId == targetTabId) return Task.FromResult(Publish());
        var admittedOrder = CapturePlacementOrder();
        return RetainContentTransition(() => MoveTabRelativeCoreAsync(tabId, targetTabId, beforeTarget, admittedOrder, token), token);
    }

    public bool CanMoveTabRelative(Guid tabId, Guid targetTabId)
    {
        if (_disposed || _nativeRetiring || tabId == targetTabId) return false;
        lock (_nativeSourceGate)
        {
            var source = _tabs.FirstOrDefault(tab => tab.Id == tabId);
            var target = _tabs.FirstOrDefault(tab => tab.Id == targetTabId);
            return source is not null && target is not null && IsSamePlacementOwner(source, target);
        }
    }

    private async Task<BrowseChromeSnapshot> MoveTabRelativeCoreAsync(Guid tabId, Guid targetTabId, bool beforeTarget,
        string admittedOrder, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_nativeSourceGate)
        {
            if (!string.Equals(admittedOrder, CapturePlacementOrder(), StringComparison.Ordinal))
                throw new InvalidOperationException("Tab placement changed before this move; use the current tab controls.");
            var source = DemandPlaceableTab(tabId);
            var target = DemandPlaceableTab(targetTabId);
            if (!IsSamePlacementOwner(source, target))
                throw new InvalidOperationException("Tabs can be reordered only within the same privacy, profile, and pin section.");
            var sourceIndex = _tabs.IndexOf(source);
            var targetIndex = _tabs.IndexOf(target);
            _tabs.RemoveAt(sourceIndex);
            targetIndex = _tabs.IndexOf(target);
            _tabs.Insert(beforeTarget ? targetIndex : targetIndex + 1, source);
            if (sourceIndex != _tabs.IndexOf(source))
            {
                _tabPlacementRevision = checked(_tabPlacementRevision + 1);
                _tabPlacementNeedsSave = true;
            }
        }
        return await SaveTabPlacementCoreAsync("Tab order saved.", token).ConfigureAwait(false);
    }

    private async Task<BrowseChromeSnapshot> MoveTabCoreAsync(Guid tabId, int direction, string admittedOrder, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_nativeSourceGate)
        {
            DemandPlaceableTab(tabId);
            if (!string.Equals(admittedOrder, CapturePlacementOrder(), StringComparison.Ordinal))
                throw new InvalidOperationException("Tab placement changed before this move; use the current tab controls.");
            if (CanMoveTab(tabId, direction))
            {
                var index = _tabs.FindIndex(tab => tab.Id == tabId);
                (_tabs[index], _tabs[index + direction]) = (_tabs[index + direction], _tabs[index]);
                _tabPlacementRevision = checked(_tabPlacementRevision + 1);
                _tabPlacementNeedsSave = true;
            }
        }
        return await SaveTabPlacementCoreAsync("Tab order saved.", token).ConfigureAwait(false);
    }

    /// <summary>Explicit retry saves current placement without toggling the user's pending pin.</summary>
    public Task<BrowseChromeSnapshot> SaveTabPlacementAsync(CancellationToken token = default) =>
        RetainContentTransition(() => SaveTabPlacementCoreAsync("Tab placement saved.", token), token);

    public Task<BrowseChromeSnapshot> CreateTabGroupAsync(string name, CancellationToken token = default) =>
        RetainContentTransition(() => CreateTabGroupCoreAsync(name, token), token);

    public Task<BrowseChromeSnapshot> MoveSelectedTabToGroupAsync(Guid groupId, CancellationToken token = default) =>
        RetainContentTransition(() => MoveTabToGroupCoreAsync(_selectedTabId, groupId, token), token);

    public Task<BrowseChromeSnapshot> RenameTabGroupAsync(Guid groupId, string name, CancellationToken token = default) =>
        RetainContentTransition(() => RenameTabGroupCoreAsync(groupId, name, token), token);

    public Task<BrowseChromeSnapshot> SetTabGroupCollapsedAsync(Guid groupId, bool collapsed, CancellationToken token = default) =>
        RetainContentTransition(() => SetTabGroupCollapsedCoreAsync(groupId, collapsed, token), token);

    public Task<BrowseChromeSnapshot> UngroupSelectedTabAsync(CancellationToken token = default) =>
        RetainContentTransition(() => UngroupTabCoreAsync(_selectedTabId, token), token);

    public Task<BrowseChromeSnapshot> DissolveTabGroupAsync(Guid groupId, CancellationToken token = default) =>
        RetainContentTransition(() => DissolveTabGroupCoreAsync(groupId, token), token);

    private async Task<BrowseChromeSnapshot> CreateTabGroupCoreAsync(string name, CancellationToken token)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        DemandGroupPersistence(); var normalized = NormalizeGroupName(name);
        var selected = RequireTab(_selectedTabId);
        if (!IsGroupable(selected)) throw new InvalidOperationException("Only standard tabs outside Home can join a tab group.");
        var id = Guid.NewGuid(); var key = id.ToString("D");
        DissolveTabFromGroup(selected);
        selected.Group = key;
        _tabGroups.Add(new(id, normalized, false, OrderedGroupMembers(key)));
        await SaveGroupChangeAsync("Tab group created.", token).ConfigureAwait(false);
        return Publish();
    }

    private async Task<BrowseChromeSnapshot> MoveTabToGroupCoreAsync(Guid tabId, Guid groupId, CancellationToken token)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested(); DemandGroupPersistence();
        var tab = RequireTab(tabId); var group = _tabGroups.SingleOrDefault(item => item.Id == groupId)
            ?? throw new ArgumentException("The current tab group is no longer available.", nameof(groupId));
        if (!IsGroupable(tab)) throw new InvalidOperationException("Only standard tabs outside Home can join a tab group.");
        var members = _tabs.Where(item => StringComparer.Ordinal.Equals(item.Group, groupId.ToString("D"))).ToArray();
        if (members.Any(member => !IsGroupCompatible(tab, member)))
            throw new InvalidOperationException("A group can contain tabs only when each member is a standard tab.");
        if (StringComparer.Ordinal.Equals(tab.Group, groupId.ToString("D"))) return Publish();
        DissolveTabFromGroup(tab); tab.Group = groupId.ToString("D");
        ReplaceGroupMembers(group);
        await SaveGroupChangeAsync("Tab added to group.", token).ConfigureAwait(false);
        return Publish();
    }

    private async Task<BrowseChromeSnapshot> RenameTabGroupCoreAsync(Guid groupId, string name, CancellationToken token)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested(); DemandGroupPersistence();
        token.ThrowIfCancellationRequested();
        var index = _tabGroups.FindIndex(item => item.Id == groupId);
        if (index < 0) throw new ArgumentException("The current tab group is no longer available.", nameof(groupId));
        _tabGroups[index] = _tabGroups[index] with { Name = NormalizeGroupName(name) };
        await SaveGroupChangeAsync("Tab group renamed.", token).ConfigureAwait(false);
        return Publish();
    }

    private async Task<BrowseChromeSnapshot> SetTabGroupCollapsedCoreAsync(Guid groupId, bool collapsed, CancellationToken token)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested(); DemandGroupPersistence();
        var index = _tabGroups.FindIndex(item => item.Id == groupId);
        if (index < 0) throw new ArgumentException("The current tab group is no longer available.", nameof(groupId));
        if (_tabGroups[index].IsCollapsed == collapsed) return Publish();
        _tabGroups[index] = _tabGroups[index] with { IsCollapsed = collapsed };
        await SaveGroupChangeAsync(collapsed ? "Tab group collapsed." : "Tab group expanded.", token).ConfigureAwait(false);
        return Publish();
    }

    private async Task<BrowseChromeSnapshot> UngroupTabCoreAsync(Guid tabId, CancellationToken token)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested(); DemandGroupPersistence();
        var tab = RequireTab(tabId); if (!TryGetGroupId(tab.Group, out var groupId)) return Publish();
        DissolveTabFromGroup(tab);
        await SaveGroupChangeAsync("Selected tab removed from its group.", token).ConfigureAwait(false);
        return Publish();
    }

    private async Task<BrowseChromeSnapshot> DissolveTabGroupCoreAsync(Guid groupId, CancellationToken token)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested(); DemandGroupPersistence();
        var group = _tabGroups.SingleOrDefault(item => item.Id == groupId)
            ?? throw new ArgumentException("The current tab group is no longer available.", nameof(groupId));
        foreach (var tab in _tabs.Where(item => StringComparer.Ordinal.Equals(item.Group, group.Id.ToString("D")))) tab.Group = string.Empty;
        _tabGroups.Remove(group);
        lock (_closedTabsGate)
            for (var index = 0; index < _closedTabs.Count; index++)
                if (StringComparer.Ordinal.Equals(_closedTabs[index].Group, group.Id.ToString("D")))
                    _closedTabs[index] = _closedTabs[index] with { Group = string.Empty };
        await SaveGroupChangeAsync("Tab group dissolved.", token).ConfigureAwait(false);
        return Publish();
    }

    private async Task SaveGroupChangeAsync(string status, CancellationToken token)
    {
        _tabPlacementRevision = checked(_tabPlacementRevision + 1); _tabPlacementNeedsSave = true;
        await SaveTabPlacementCoreAsync(status, token).ConfigureAwait(false);
    }

    private void RestoreTabGroups(IReadOnlyList<BrowseTabGroupProjection>? groups)
    {
        _tabGroups.Clear();
        if (groups is null) return;
        var seen = new HashSet<Guid>();
        foreach (var group in groups)
        {
            if (group is null || group.Id == Guid.Empty || !seen.Add(group.Id) || string.IsNullOrWhiteSpace(group.Name) ||
                group.Name.Trim().Length > 64 || group.Name.Any(char.IsControl) || group.OrderedTabIds is null ||
                group.OrderedTabIds.Distinct().Count() != group.OrderedTabIds.Count)
                continue;
            var members = group.OrderedTabIds.Select(id => _tabs.FirstOrDefault(tab => tab.Id == id)).ToArray();
            if (members.Any(tab => tab is null || !IsGroupable(tab) || tab.Group != group.Id.ToString("D")) ||
                members.Skip(1).Any(tab => !IsGroupCompatible(members[0]!, tab!))) continue;
            _tabGroups.Add(group with { Name = group.Name.Trim(), OrderedTabIds = Array.AsReadOnly(members.Select(tab => tab!.Id).ToArray()) });
        }
        var accepted = _tabGroups.Select(group => group.Id.ToString("D")).ToHashSet(StringComparer.Ordinal);
        foreach (var tab in _tabs.Where(tab => TryGetGroupId(tab.Group, out _)).ToArray())
            if (!accepted.Contains(tab.Group)) tab.Group = string.Empty;
    }

    private IReadOnlyList<BrowseTabGroupProjection> CaptureTabGroups() => _tabGroups
        .Select(group => group with { OrderedTabIds = Array.AsReadOnly(OrderedGroupMembers(group.Id.ToString("D")).ToArray()) }).ToArray();

    private BrowseTabGroupProjection? GroupFor(string group) => TryGetGroupId(group, out var id)
        ? _tabGroups.FirstOrDefault(item => item.Id == id) : null;

    private static bool TryGetGroupId(string? value, out Guid id) => Guid.TryParse(value, out id) && id != Guid.Empty;
    private static string NormalizeGroupName(string name)
    {
        var value = name?.Trim() ?? string.Empty;
        if (value.Length is < 1 or > 64 || value.Any(char.IsControl))
            throw new ArgumentException("A tab group name must contain 1 to 64 printable characters.", nameof(name));
        return value;
    }

    private void DemandGroupPersistence()
    {
        if (_tabSessionPersistence is null)
            throw new NotSupportedException("Tab groups are available only in the canonical workspace session.");
    }

    private static bool IsGroupable(TabRuntime? tab) => tab is { Privacy: Haven.Core.BrowserTabPrivacy.Standard, IsHome: false };
    private static bool IsGroupCompatible(TabRuntime first, TabRuntime second) => IsGroupable(first) && IsGroupable(second) &&
        StringComparer.Ordinal.Equals(first.ProfileId, second.ProfileId);

    private Guid[] OrderedGroupMembers(string groupKey) => _tabs.Where(tab => tab.Privacy == Haven.Core.BrowserTabPrivacy.Standard &&
        StringComparer.Ordinal.Equals(tab.Group, groupKey)).Select(tab => tab.Id).ToArray();

    private void ReplaceGroupMembers(BrowseTabGroupProjection group)
    {
        var index = _tabGroups.FindIndex(item => item.Id == group.Id);
        if (index >= 0) _tabGroups[index] = group with { OrderedTabIds = Array.AsReadOnly(OrderedGroupMembers(group.Id.ToString("D"))) };
    }

    private void DissolveTabFromGroup(TabRuntime tab)
    {
        if (!TryGetGroupId(tab.Group, out var groupId)) return;
        tab.Group = string.Empty;
        var remaining = OrderedGroupMembers(groupId.ToString("D"));
        if (remaining.Length == 0)
        {
            _tabGroups.RemoveAll(group => group.Id == groupId);
            lock (_closedTabsGate)
                for (var index = 0; index < _closedTabs.Count; index++)
                    if (StringComparer.Ordinal.Equals(_closedTabs[index].Group, groupId.ToString("D")))
                        _closedTabs[index] = _closedTabs[index] with { Group = string.Empty };
        }
        else
        {
            var index = _tabGroups.FindIndex(group => group.Id == groupId);
            if (index >= 0) _tabGroups[index] = _tabGroups[index] with { OrderedTabIds = Array.AsReadOnly(remaining) };
        }
    }

    private async Task<BrowseChromeSnapshot> SaveTabPlacementCoreAsync(string status, CancellationToken token)
    {
        long revision;
        lock (_nativeSourceGate) revision = _tabPlacementRevision;
        try
        {
            await SaveTabsAsync(token).ConfigureAwait(false);
            lock (_nativeSourceGate)
                if (_tabPlacementRevision == revision) _tabPlacementNeedsSave = false;
            _status = status;
            return Publish();
        }
        catch
        {
            // The canonical owner may already have accepted this snapshot. Keep the
            // actual live state and failed original task; never invent a rollback.
            lock (_nativeSourceGate) _tabPlacementNeedsSave = true;
            _status = "Tab placement could not be saved. Retry saving the current layout.";
            throw;
        }
    }

    private TabRuntime DemandPlaceableTab(Guid tabId)
    {
        lock (_nativeSourceGate)
        {
            var tab = _tabs.SingleOrDefault(item => item.Id == tabId)
                ?? throw new ArgumentException("The current tab identity is no longer present.", nameof(tabId));
            if (tab.IsHome) throw new InvalidOperationException("The permanent Home tab stays in its own position.");
            return tab;
        }
    }

    private void NormalizeTabPlacement()
    {
        lock (_nativeSourceGate)
        {
            var ordered = _tabs.OrderBy(tab => tab.IsHome ? 0 : tab.IsPinned ? 1 : 2).ToArray();
            _tabs.Clear(); _tabs.AddRange(ordered);
        }
    }

    private static bool IsSamePlacementOwner(TabRuntime first, TabRuntime second) =>
        !first.IsHome && !second.IsHome && first.IsPinned == second.IsPinned &&
        first.Privacy == second.Privacy && StringComparer.Ordinal.Equals(first.ProfileId, second.ProfileId);

    private string CapturePlacementOrder()
    {
        lock (_nativeSourceGate) return string.Join(";", _tabs.Select(tab =>
            tab.Id.ToString("N") + (tab.IsPinned ? ":p" : ":u") + (tab.IsHome ? ":h" : string.Empty)));
    }
}
