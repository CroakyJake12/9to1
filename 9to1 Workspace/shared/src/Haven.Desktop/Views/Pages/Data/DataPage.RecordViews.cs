using Haven.Application;
using HavenOS.Apps.Data.RecordsUI;

namespace Haven.Desktop.Views.Pages.Data;

public sealed partial class DataPage : IAsyncDisposable
{
    private DataRecordViewControl? _recordView;
    /// <summary>Displays the same caller-owned Data session used by a Boards reference. No
    /// copy/import takes place. Caller retains session ownership and closes it after this page.</summary>
    public async Task OpenRecordSessionAsync(DataRecordSession session, Guid? initialView = null,
        CancellationToken token = default)
    {
        Avalonia.Threading.Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_dirty && !await SaveAsync("Save spreadsheet before opening record views", token))
            throw new InvalidOperationException("Save or resolve the current spreadsheet before switching.");
        if (_recordView is not null) await _recordView.DisposeAsync();
        _autosaveTimer.Stop(); _initialized = true; _dirty = false;
        _recordView = new DataRecordViewControl(session, initialView); Content = _recordView;
    }
    private void CloseRecordViewSynchronously()
    {
        if (_recordView is null) return;
        var close = _recordView.DisposeAsync();
        if (!close.IsCompletedSuccessfully) throw new InvalidOperationException("Use DataPage.DisposeAsync to join the pending Data record operation.");
        close.GetAwaiter().GetResult(); _recordView = null;
    }
    public async ValueTask DisposeAsync()
    {
        if (_recordView is not null) { await _recordView.DisposeAsync(); _recordView = null; }
        Dispose();
    }
}
