namespace HavenOS.Apps.Browse;

/// <summary>Two-pane presentation over the existing tab owners and stable tab identities.</summary>
public sealed partial class BrowseChrome
{
    private long _paneLayoutRevision;
    private bool _paneLayoutNeedsSave;
    private readonly List<BrowseSplitViewProjection> _splitViews = [];
    private Guid? _activeSplitViewId;
    private const double PaneRatioStep = 0.05;
    public bool HasSplitView => _paneTabIds.Count == 2;
    public bool HasUnsavedSplitViewLayout => !_disposed && !_nativeRetiring && _paneLayoutNeedsSave;
    public bool CanWidenPrimaryPane => HasSplitView && _primaryPaneRatio < Haven.Core.WorkspaceLayoutSnapshot.MaximumPaneRatio;
    public bool CanNarrowPrimaryPane => HasSplitView && _primaryPaneRatio > Haven.Core.WorkspaceLayoutSnapshot.MinimumPaneRatio;

    public bool CanSplitSelectedTab
    {
        get
        {
            lock (_nativeSourceGate)
            {
                if (_disposed || _nativeRetiring || HasSplitView) return false;
                return _tabs.Any(tab => tab.Id != _selectedTabId && CanOpenSplitViewForTabs(_selectedTabId, tab.Id));
            }
        }
    }

    /// <summary>The canonical capability check for an exact two-tab split. Existing tabs keep
    /// their own profiles and engines; cross-profile splits are supported, private tabs are not.</summary>
    public bool CanOpenSplitViewForTabs(Guid receivingTabId, Guid otherTabId)
    {
        if (_disposed || _nativeRetiring || HasSplitView || receivingTabId == otherTabId) return false;
        lock (_nativeSourceGate)
        {
            var selected = _tabs.FirstOrDefault(tab => tab.Id == _selectedTabId);
            var receiving = _tabs.FirstOrDefault(tab => tab.Id == receivingTabId);
            var other = _tabs.FirstOrDefault(tab => tab.Id == otherTabId);
            return selected?.Privacy == Haven.Core.BrowserTabPrivacy.Standard &&
                receiving?.Privacy == Haven.Core.BrowserTabPrivacy.Standard &&
                other?.Privacy == Haven.Core.BrowserTabPrivacy.Standard &&
                !_splitViews.Any(split => split.PaneTabIds.Contains(receivingTabId) || split.PaneTabIds.Contains(otherTabId));
        }
    }

    public Task<BrowseChromeSnapshot> ToggleSplitViewAsync(CancellationToken token = default) =>
        RetainContentTransition(() => HasSplitView ? CloseSplitViewCoreAsync(token) : OpenSplitViewCoreAsync(token), token);

    /// <summary>Opens a split with the exact two standard tabs selected by a native drop gesture.
    /// The first id occupies the receiving side; both existing tabs and sessions are retained.</summary>
    public Task<BrowseChromeSnapshot> OpenSplitViewForTabsAsync(Guid receivingTabId, Guid otherTabId,
        CancellationToken token = default) =>
        RetainContentTransition(() => OpenSplitViewForTabsCoreAsync(receivingTabId, otherTabId, token), token);

    private async Task<BrowseChromeSnapshot> OpenSplitViewForTabsCoreAsync(Guid receivingTabId, Guid otherTabId,
        CancellationToken token)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        if (!CanOpenSplitViewForTabs(receivingTabId, otherTabId))
            throw new InvalidOperationException("The current selected, receiving, and companion tabs must be standard tabs, with no split already open.");
        var receiving = RequireTab(receivingTabId);
        var other = RequireTab(otherTabId);
        _paneTabIds.Clear(); _paneTabIds.Add(receiving.Id); _paneTabIds.Add(other.Id);
        _activeSplitViewId = Guid.NewGuid();
        if (!_paneTabIds.Contains(_selectedTabId)) _selectedTabId = receiving.Id;
        _splitOrientation = Haven.Core.SplitOrientation.Vertical;
        return await SavePaneLayoutCoreAsync("Split view opened. Both panes keep their original tabs and sessions.", token).ConfigureAwait(false);
    }

    public Task<BrowseChromeSnapshot> FocusSplitPaneAsync(Guid tabId, CancellationToken token = default) =>
        RetainContentTransition(() => FocusSplitPaneCoreAsync(tabId, token), token);

    public Task<BrowseChromeSnapshot> SwapSplitPanesAsync(CancellationToken token = default) =>
        RetainContentTransition(() => SwapSplitPanesCoreAsync(token), token);

    public Task<BrowseChromeSnapshot> CloseSplitViewAsync(CancellationToken token = default) =>
        RetainContentTransition(() => CloseSplitViewCoreAsync(token), token);

    public Task<BrowseChromeSnapshot> SaveSplitViewLayoutAsync(CancellationToken token = default) =>
        RetainContentTransition(() => SavePaneLayoutCoreAsync("Split view layout saved.", token), token);

    public Task<BrowseChromeSnapshot> ToggleSplitOrientationAsync(CancellationToken token = default) =>
        RetainContentTransition(() => ToggleSplitOrientationCoreAsync(token), token);

    public Task<BrowseChromeSnapshot> AdjustPrimaryPaneAsync(int direction, CancellationToken token = default)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        return RetainContentTransition(() => AdjustPrimaryPaneCoreAsync(direction, token), token);
    }

    private async Task<BrowseChromeSnapshot> OpenSplitViewCoreAsync(CancellationToken token)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        var selected = RequireTab(_selectedTabId);
        if (selected.Privacy != Haven.Core.BrowserTabPrivacy.Standard)
            throw new InvalidOperationException("Only standard tabs can enter a split view.");
        var second = _tabs.FirstOrDefault(tab => CanOpenSplitViewForTabs(selected.Id, tab.Id));
        if (second is null)
        {
            _status = "Open another standard tab before splitting the view.";
            return Publish();
        }
        _paneTabIds.Clear(); _paneTabIds.Add(selected.Id); _paneTabIds.Add(second.Id);
        _activeSplitViewId = Guid.NewGuid();
        return await SavePaneLayoutCoreAsync("Split view opened. Both panes keep their original tabs and sessions.", token).ConfigureAwait(false);
    }

    private async Task<BrowseChromeSnapshot> FocusSplitPaneCoreAsync(Guid tabId, CancellationToken token)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        if (!HasSplitView || !_paneTabIds.Contains(tabId))
            throw new ArgumentException("The tab is not in the current split view.", nameof(tabId));
        if (_selectedTabId == tabId) return Publish();
        _selectedTabId = tabId;
        return await SavePaneLayoutCoreAsync("Split pane focused.", token).ConfigureAwait(false);
    }

    private async Task<BrowseChromeSnapshot> SwapSplitPanesCoreAsync(CancellationToken token)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        if (!HasSplitView) return Publish();
        (_paneTabIds[0], _paneTabIds[1]) = (_paneTabIds[1], _paneTabIds[0]);
        return await SavePaneLayoutCoreAsync("Split panes swapped.", token).ConfigureAwait(false);
    }

    private async Task<BrowseChromeSnapshot> CloseSplitViewCoreAsync(CancellationToken token)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        if (_paneTabIds.Count == 0) return Publish();
        _splitViews.RemoveAll(split => split.Id == _activeSplitViewId);
        _activeSplitViewId = null;
        _paneTabIds.Clear();
        return await SavePaneLayoutCoreAsync("Split view closed. Both tabs remain open.", token).ConfigureAwait(false);
    }

    private async Task<BrowseChromeSnapshot> ToggleSplitOrientationCoreAsync(CancellationToken token)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        if (!HasSplitView) return Publish();
        _splitOrientation = _splitOrientation == Haven.Core.SplitOrientation.Horizontal
            ? Haven.Core.SplitOrientation.Vertical : Haven.Core.SplitOrientation.Horizontal;
        return await SavePaneLayoutCoreAsync("Split orientation changed.", token).ConfigureAwait(false);
    }

    private async Task<BrowseChromeSnapshot> AdjustPrimaryPaneCoreAsync(int direction, CancellationToken token)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        if (!HasSplitView) return Publish();
        var next = _primaryPaneRatio + PaneRatioStep * direction;
        _primaryPaneRatio = Math.Clamp(next, Haven.Core.WorkspaceLayoutSnapshot.MinimumPaneRatio,
            Haven.Core.WorkspaceLayoutSnapshot.MaximumPaneRatio);
        return await SavePaneLayoutCoreAsync("Split pane size saved.", token).ConfigureAwait(false);
    }

    private async Task<BrowseChromeSnapshot> SavePaneLayoutCoreAsync(string status, CancellationToken token)
    {
        CaptureActiveSplitView();
        var revision = _paneLayoutRevision = checked(_paneLayoutRevision + 1);
        _paneLayoutNeedsSave = true;
        try { await SaveTabsAsync(token).ConfigureAwait(false); }
        catch
        {
            if (_paneLayoutRevision == revision) _paneLayoutNeedsSave = true;
            _status = "The split view layout could not be saved. Retry saving the current layout.";
            try { Publish(); }
            catch (Exception publicationFailure)
            { System.Diagnostics.Trace.TraceError("Browse retained its original split layout save failure: {0}", publicationFailure); }
            throw;
        }
        if (_paneLayoutRevision == revision) _paneLayoutNeedsSave = false;
        _status = status;
        return Publish();
    }

    public Task<BrowseChromeSnapshot> SelectSplitViewAsync(Guid splitId, CancellationToken token = default)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        var split = _splitViews.SingleOrDefault(item => item.Id == splitId)
            ?? throw new ArgumentException("The split tab is no longer open.", nameof(splitId));
        return SelectTabAsync(split.FocusedTabId, token);
    }

    private void CaptureActiveSplitView()
    {
        if (_activeSplitViewId is not { } id || _paneTabIds.Count != 2 || !_paneTabIds.Contains(_selectedTabId)) return;
        var value = new BrowseSplitViewProjection(id, Array.AsReadOnly(_paneTabIds.ToArray()),
            _selectedTabId, _splitOrientation, _primaryPaneRatio);
        var index = _splitViews.FindIndex(split => split.Id == id);
        if (index >= 0) _splitViews[index] = value; else _splitViews.Add(value);
    }

    private IReadOnlyList<BrowseSplitViewProjection> CaptureSplitViews() => Array.AsReadOnly(_splitViews
        .Select(split => split with { PaneTabIds = Array.AsReadOnly(split.PaneTabIds.ToArray()) }).ToArray());

    private void SelectTabPresentation(Guid tabId)
    {
        CaptureActiveSplitView();
        _selectedTabId = tabId;
        var split = _splitViews.SingleOrDefault(item => item.PaneTabIds.Contains(tabId));
        _paneTabIds.Clear(); _activeSplitViewId = split?.Id;
        if (split is null) return;
        _paneTabIds.AddRange(split.PaneTabIds);
        _splitOrientation = split.Orientation; _primaryPaneRatio = split.PrimaryPaneRatio;
        CaptureActiveSplitView();
    }

    private void RemoveSplitViewForClosedTab(Guid tabId)
    {
        _splitViews.RemoveAll(split => split.PaneTabIds.Contains(tabId));
        if (!_paneTabIds.Contains(tabId)) return;
        _paneTabIds.Clear(); _activeSplitViewId = null;
    }

    private void RestoreSplitViews(BrowseTabSessionProjection? projection)
    {
        if (projection is null) return;
        IReadOnlyList<BrowseSplitViewProjection> restored = projection.SplitViews ?? [];
        if (projection.SplitViews is null && projection.PaneTabIds is { Count: 2 } legacy && legacy.Contains(_selectedTabId))
            restored = [new(Guid.NewGuid(), legacy, _selectedTabId,
                projection.SplitOrientation ?? Haven.Core.SplitOrientation.Vertical, projection.PrimaryPaneRatio ?? 0.5)];
        var ids = new HashSet<Guid>(); var members = new HashSet<Guid>();
        foreach (var split in restored)
        {
            if (split is null || split.Id == Guid.Empty || ids.Contains(split.Id) || split.PaneTabIds is not { Count: 2 } panes ||
                panes[0] == panes[1] || !panes.Contains(split.FocusedTabId) || !Enum.IsDefined(split.Orientation) ||
                !IsValidPaneRatio(split.PrimaryPaneRatio) || panes.Any(id => members.Contains(id) || !_tabs.Any(tab =>
                    tab.Id == id && tab.Privacy == Haven.Core.BrowserTabPrivacy.Standard))) continue;
            ids.Add(split.Id); members.UnionWith(panes);
            _splitViews.Add(split with { PaneTabIds = Array.AsReadOnly(panes.ToArray()) });
        }
        SelectTabPresentation(_selectedTabId);
    }
}
