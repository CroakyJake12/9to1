namespace HavenOS.Apps.Browse;

public sealed partial class BrowseChrome
{
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
