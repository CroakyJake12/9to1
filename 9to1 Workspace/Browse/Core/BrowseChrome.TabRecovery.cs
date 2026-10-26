using Haven.Core;
using Haven.Browser;

namespace HavenOS.Apps.Browse;

public sealed partial class BrowseChrome
{
    private const int MaximumClosedTabs = 25;
    private readonly List<ClosedTab> _closedTabs = [];
    private readonly object _closedTabsGate = new();
    private Guid? _reopeningClosedTabId;
    public bool CanReopenClosedTab
    { get { lock (_closedTabsGate) return !_nativeRetiring && !_disposed && _closedTabs.Count > 0; } }
    private bool HasClosedAppTab
    { get { lock (_closedTabsGate) return _closedTabs.Count > 0 && !_closedTabs[^1].Address.IsWeb; } }

    private sealed record ClosedTab(Guid Id, string Title, AppContentAddress Address, string ProfileId, string Group,
        int Index, BrowseEngineKind? EngineOverride, bool IsPinned, DateTimeOffset ClosedAt);

    private void RememberClosedTab(TabRuntime tab, int index, BrowseEngineKind? engineOverride)
    {
        if (tab.Privacy != BrowserTabPrivacy.Standard) return;
        lock (_closedTabsGate)
        {
            _closedTabs.Add(new(tab.Id, tab.Title, tab.Address, tab.ProfileId, tab.Group, index,
                engineOverride, tab.IsPinned, DateTimeOffset.UtcNow));
            if (_closedTabs.Count > MaximumClosedTabs) _closedTabs.RemoveAt(0);
        }
    }

    private void RestoreClosedTabs(IReadOnlyList<BrowseClosedTabProjection> projection)
    {
        lock (_closedTabsGate) _closedTabs.Clear();
        var openIds = _tabs.Select(tab => tab.Id).ToHashSet();
        var seen = new HashSet<Guid>();
        var restored = new List<ClosedTab>();
        foreach (var item in projection.TakeLast(MaximumClosedTabs))
        {
            var saved = item.Tab;
            if (saved.Privacy != BrowserTabPrivacy.Standard || saved.Id == Guid.Empty ||
                openIds.Contains(saved.Id) || !seen.Add(saved.Id) || item.Index < 0 ||
                saved.ProfileId is { Length: > 0 } profile && string.IsNullOrWhiteSpace(profile) ||
                item.EngineOverride is { } engine && !Enum.IsDefined(engine) ||
                !TryStoredAddress(saved.Address, out var address)) continue;
            restored.Add(new(saved.Id, saved.Title, address!,
                string.IsNullOrEmpty(saved.ProfileId) ? _homeProfileId : saved.ProfileId!,
                saved.Group ?? string.Empty, item.Index, item.EngineOverride, saved.IsPinned == true, saved.UpdatedAt));
        }
        lock (_closedTabsGate) _closedTabs.AddRange(restored);
    }

    private void RestoreStandaloneClosedTabs() => RestoreClosedTabs(_data.ClosedTabs.Select(item =>
    {
        var engine = Enum.TryParse<BrowseEngineKind>(item.EngineOverride, ignoreCase: true, out var parsed) &&
            Enum.IsDefined(parsed) ? parsed : (BrowseEngineKind?)null;
        return new BrowseClosedTabProjection(item.Tab, item.Index, engine);
    }).ToArray());

    private IReadOnlyList<BrowseClosedTabProjection> CaptureClosedTabs(IReadOnlySet<Guid>? openIds = null)
    {
        lock (_closedTabsGate)
            return _closedTabs.Where(closed => openIds is null || !openIds.Contains(closed.Id))
                .Select(closed => new BrowseClosedTabProjection(
                new BrowserTabState(closed.Id, closed.Title, closed.Address.Value, BrowserTabPrivacy.Standard,
                    closed.Group, closed.ClosedAt)
                { ProfileId = closed.ProfileId, IsPinned = closed.IsPinned }, closed.Index, closed.EngineOverride))
                .ToArray();
    }

    public Task<BrowseChromeSnapshot> ReopenClosedTabAsync(CancellationToken token = default) =>
        RetainContentTransition(() => ReopenClosedTabCoreAsync(token), token, HasClosedAppTab);

    private async Task<BrowseChromeSnapshot> ReopenClosedTabCoreAsync(CancellationToken token)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        ClosedTab? closed = null;
        lock (_closedTabsGate)
        {
            if (_closedTabs.Count > 0) closed = _closedTabs[^1];
            if (closed is not null && _reopeningClosedTabId is not null)
                throw new InvalidOperationException("A recently closed tab is already being restored.");
            if (closed is not null) _reopeningClosedTabId = closed.Id;
        }
        if (closed is null)
        { _status = "No closed standard tab is available to reopen."; return Publish(); }
        _enginePolicy.SetTabOverride(closed.Id, closed.EngineOverride);
        TabRuntime tab;
        try
        {
            tab = await AddRuntimeAsync(closed.Id, closed.Title, closed.Address,
                BrowserTabPrivacy.Standard, closed.Group, token, closed.ProfileId, isPinned: closed.IsPinned).ConfigureAwait(false);
        }
        catch
        {
            _enginePolicy.SetTabOverride(closed.Id, null);
            lock (_closedTabsGate) _reopeningClosedTabId = null;
            throw;
        }
        // Once the original tab exists, it must not be created again on retry,
        // including when a later durable write fails and is retained by its owner.
        lock (_closedTabsGate)
        {
            var closedIndex = _closedTabs.FindLastIndex(item => item.Id == closed.Id);
            if (closedIndex >= 0) _closedTabs.RemoveAt(closedIndex);
            _reopeningClosedTabId = null;
        }
        _tabs.Remove(tab); _tabs.Insert(Math.Clamp(closed.Index, 0, _tabs.Count), tab);
        NormalizeTabPlacement();
        SelectTabPresentation(tab.Id);
        await SaveEnginePreferencesAsync(token).ConfigureAwait(false);
        await SaveTabsAsync(token).ConfigureAwait(false);
        _status = tab.EngineState == BrowseEngineState.Ready ? "Closed tab reopened." : "Closed tab restored. " + tab.Status;
        return Publish();
    }
}
