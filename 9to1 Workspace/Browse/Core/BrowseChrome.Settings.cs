namespace HavenOS.Apps.Browse;

public sealed partial class BrowseChrome
{
    /// <summary>Reads the canonical durable setting, including a failed save's rollback.</summary>
    public bool HistoryRecordingEnabled { get { ThrowIfDisposed(); return _data.Settings.SaveHistory; } }
    public bool TabRestoreEnabled { get { ThrowIfDisposed(); return _data.Settings.RestoreTabs; } }

    public async Task<BrowseChromeSnapshot> SetHistoryRecordingAsync(bool enabled, CancellationToken token = default)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        await _data.SetHistoryRecordingAsync(enabled, token).ConfigureAwait(false);
        _status = enabled ? "Browsing history recording is on." : "Browsing history recording is off. Existing history is kept.";
        return Publish();
    }

    public async Task<BrowseChromeSnapshot> SetTabRestoreAsync(bool enabled, CancellationToken token = default)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        await _data.SetTabRestoreAsync(enabled, token).ConfigureAwait(false);
        _status = enabled ? "Tabs will be restored on the next startup." : "Tab restoration is off for the next startup. Current tabs stay open.";
        return Publish();
    }
}
