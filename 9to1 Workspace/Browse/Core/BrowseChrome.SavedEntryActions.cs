using Haven.Browser;

namespace HavenOS.Apps.Browse;

public sealed partial class BrowseChrome
{
    public Task<BrowseChromeSnapshot> OpenBookmarkAsync(Guid originalEntryId, CancellationToken token = default)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        var original = _data.Bookmarks.SingleOrDefault(entry => entry.Id == originalEntryId)
            ?? throw new ArgumentException("The original saved bookmark is no longer present.", nameof(originalEntryId));
        return NavigateAsync(original.Address, token);
    }
    /// <summary>Removes the identified saved bookmark without navigating or changing tabs.</summary>
    public async Task<BrowseChromeSnapshot> RemoveBookmarkAsync(Guid originalEntryId, CancellationToken token = default)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        var original = _data.Bookmarks.SingleOrDefault(entry => entry.Id == originalEntryId)
            ?? throw new ArgumentException("The original saved bookmark is no longer present.", nameof(originalEntryId));
        await _data.RemoveBookmarkAsync(original.Id, token).ConfigureAwait(false);
        _status = "Bookmark removed locally.";
        return Publish();
    }
    public Task<BrowseChromeSnapshot> OpenHistoryEntryAsync(Guid originalEntryId, CancellationToken token = default)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        var original = _data.History.SingleOrDefault(entry => entry.Id == originalEntryId)
            ?? throw new ArgumentException("The original saved history entry is no longer present.", nameof(originalEntryId));
        // History is a record of web visits, not an alternate route into native
        // apps. Old or externally modified stores can contain arbitrary absolute
        // addresses, so reject non-web schemes before dispatch. NavigateAsync still
        // applies the full canonical top-level policy (including credential checks).
        // This guard is history-specific; bookmark and registered-route handling is unchanged.
        if (!Uri.TryCreate(original.Address, UriKind.Absolute, out var address) ||
            address.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("A saved history visit must use HTTP or HTTPS.");
        return NavigateAsync(original.Address, token);
    }
    /// <summary>Removes an exact recorded visit without changing the current tab or document.</summary>
    public async Task<BrowseChromeSnapshot> RemoveHistoryEntryAsync(Guid originalEntryId, CancellationToken token = default)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        var original = _data.History.SingleOrDefault(entry => entry.Id == originalEntryId)
            ?? throw new ArgumentException("The original history visit is no longer present.", nameof(originalEntryId));
        await _data.RemoveHistoryEntryAsync(original.Id, token).ConfigureAwait(false);
        _status = "History visit removed locally.";
        return Publish();
    }
    public Task<BrowseChromeSnapshot> ResetSelectedTabEngineAsync(CancellationToken token = default) =>
        RetainContentTransition(() => ResetSelectedTabEngineCoreAsync(token), token);

    private async Task<BrowseChromeSnapshot> ResetSelectedTabEngineCoreAsync(CancellationToken token)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        var tab = SelectedRuntime();
        if (!tab.Address.IsWeb) throw new InvalidOperationException("Native Browse pages do not use a web engine.");
        var hadOverride = _enginePolicy.TabOverrides.TryGetValue(tab.Id, out var previous);
        _enginePolicy.SetTabOverride(tab.Id, null);
        try { await SaveEnginePreferencesAsync(token).ConfigureAwait(false); }
        catch
        {
            if (hadOverride) _enginePolicy.SetTabOverride(tab.Id, previous);
            throw;
        }
        await SetTabEngineAsync(tab, _enginePolicy.Resolve(tab.Address.WebUri!, tab.Id), token, persistTabOverride: false).ConfigureAwait(false);
        _status = "This tab now follows your site and default engine preferences. " + tab.Status;
        return Publish();
    }

    public Task<BrowseChromeSnapshot> ResetSiteEngineAsync(CancellationToken token = default) =>
        RetainContentTransition(() => ResetSiteEngineCoreAsync(token), token);

    private async Task<BrowseChromeSnapshot> ResetSiteEngineCoreAsync(CancellationToken token)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        var originalTab = SelectedRuntime(); EnsureWebAddress(originalTab.Address);
        var address = originalTab.Address.WebUri!;
        var host = address.IdnHost.TrimEnd('.');
        var hadOverride = _enginePolicy.SiteOverrides.TryGetValue(host, out var previous);
        _enginePolicy.SetSiteOverride(address, null);
        try { await SaveEnginePreferencesAsync(token).ConfigureAwait(false); }
        catch
        {
            if (hadOverride) _enginePolicy.SetSiteOverride(address, previous);
            throw;
        }
        var actualChoice = _enginePolicy.Resolve(address, originalTab.Id);
        await SetTabEngineAsync(originalTab, actualChoice, token, persistTabOverride: false).ConfigureAwait(false);
        _status = _enginePolicy.TabOverrides.ContainsKey(originalTab.Id)
            ? "Site engine preference reset. This tab keeps its explicit engine choice."
            : "Site engine preference reset. This tab follows your default engine. " + originalTab.Status;
        return Publish();
    }
}
