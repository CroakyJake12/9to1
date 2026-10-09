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
    public Task<BrowseChromeSnapshot> OpenHistoryEntryAsync(Guid originalEntryId, CancellationToken token = default)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        var original = _data.History.SingleOrDefault(entry => entry.Id == originalEntryId)
            ?? throw new ArgumentException("The original saved history entry is no longer present.", nameof(originalEntryId));
        return NavigateAsync(original.Address, token);
    }
    public async Task<BrowseChromeSnapshot> ResetSiteEngineAsync(CancellationToken token = default)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        var originalTab = SelectedRuntime(); EnsureWebAddress(originalTab.Address);
        _enginePolicy.SetSiteOverride(originalTab.Address, null);
        await SaveEnginePreferencesAsync(token).ConfigureAwait(false);
        var actualChoice = _enginePolicy.Resolve(originalTab.Address, originalTab.Id);
        await SetTabEngineAsync(originalTab, actualChoice, token, persistTabOverride: false).ConfigureAwait(false);
        _status = _enginePolicy.TabOverrides.ContainsKey(originalTab.Id)
            ? "Site engine preference reset. This tab keeps its explicit engine choice."
            : "Site engine preference reset. This tab follows your default engine. " + originalTab.Status;
        return Publish();
    }
}
