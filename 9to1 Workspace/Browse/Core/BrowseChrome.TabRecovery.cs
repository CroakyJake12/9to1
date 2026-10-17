using Haven.Core;

namespace HavenOS.Apps.Browse;

public sealed partial class BrowseChrome
{
    private const int MaximumClosedTabs = 25;
    private readonly List<ClosedTab> _closedTabs = [];
    public bool CanReopenClosedTab => !_nativeRetiring && !_disposed && _closedTabs.Count > 0;

    private sealed record ClosedTab(Guid Id, string Title, AppContentAddress Address, string ProfileId, string Group, int Index, BrowseEngineKind? EngineOverride);

    private void RememberClosedTab(TabRuntime tab, int index, BrowseEngineKind? engineOverride)
    {
        if (tab.Privacy != BrowserTabPrivacy.Standard) return;
        _closedTabs.Add(new(tab.Id, tab.Title, tab.Address, tab.ProfileId, tab.Group, index, engineOverride));
        if (_closedTabs.Count > MaximumClosedTabs) _closedTabs.RemoveAt(0);
    }

    public Task<BrowseChromeSnapshot> ReopenClosedTabAsync(CancellationToken token = default) =>
        _closedTabs.Count > 0 && !_closedTabs[^1].Address.IsWeb
            ? RetainNativeAdmission(() => ReopenClosedTabCoreAsync(token), token) : ReopenClosedTabCoreAsync(token);

    private async Task<BrowseChromeSnapshot> ReopenClosedTabCoreAsync(CancellationToken token)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        if (_closedTabs.Count == 0)
        { _status = "No closed standard tab is available to reopen."; return Publish(); }
        var closed = _closedTabs[^1];
        _enginePolicy.SetTabOverride(closed.Id, closed.EngineOverride);
        TabRuntime tab;
        try
        {
            tab = await AddRuntimeAsync(closed.Id, closed.Title, closed.Address,
                BrowserTabPrivacy.Standard, closed.Group, token, closed.ProfileId).ConfigureAwait(false);
        }
        catch
        {
            _enginePolicy.SetTabOverride(closed.Id, null);
            throw;
        }
        // Once the original tab exists, it must not be created again on retry,
        // including when a later durable write fails and is retained by its owner.
        _closedTabs.RemoveAt(_closedTabs.Count - 1);
        _tabs.Remove(tab); _tabs.Insert(Math.Clamp(closed.Index, 0, _tabs.Count), tab);
        _selectedTabId = tab.Id;
        await SaveEnginePreferencesAsync(token).ConfigureAwait(false);
        await SaveTabsAsync(token).ConfigureAwait(false);
        _status = tab.EngineState == BrowseEngineState.Ready ? "Closed tab reopened." : "Closed tab restored. " + tab.Status;
        return Publish();
    }
}
