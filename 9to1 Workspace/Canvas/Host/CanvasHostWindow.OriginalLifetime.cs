using Avalonia.Threading;

namespace HavenOS.Apps.Canvas;

public sealed partial class CanvasHostWindow
{
    public Task? OriginalCloseTask => _originalWork.OriginalCloseTask;
    public Task CloseAndDrainAsync()
    {
        if (!Dispatcher.UIThread.CheckAccess()) return Dispatcher.UIThread.InvokeAsync(CloseAndDrainAsync);
        _closed = true;
        return _originalWork.CloseAndDrainAsync(CloseOriginalChildrenAsync);
    }
    private async Task CloseOriginalChildrenAsync()
    {
        var errors = new List<Exception>();
        try { _lifetime.Cancel(); } catch (Exception error) { errors.Add(error); }
        Task? view = null;
        try
        {
            if (_view is CanvasNativeCuiSurface native) { view = native.CloseAndDrainAsync(); await view; }
            else _view?.Dispose();
        }
        catch (Exception error) { errors.Add((Exception?)view?.Exception ?? error); }
        // Local native/presentation owners retire independently; Home/Files remain borrowed.
        try { _pendingDisposable?.Dispose(); } catch (Exception error) { errors.Add(error); }
        Task? approvals = null; Task? shell = null;
        try { approvals = _approvals.CloseAndDrainAsync(); }
        catch (Exception error) { errors.Add(error); }
        try { shell = _shell.CloseOriginalAsync(); }
        catch (Exception error) { errors.Add(error); }
        foreach (var actual in new[] { approvals, shell }.OfType<Task>())
            try { await actual; } catch (Exception error) { errors.Add((Exception?)actual.Exception ?? error); }
        try { _lifetime.Dispose(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("Canvas window native children did not drain successfully.", errors);
    }
    private async Task ObserveStatusAsync(Task actual)
    {
        try { await actual; }
        catch (Exception error)
        {
            if (_closed) return;
            _ready = false; SetStatus(((Exception?)actual.Exception ?? error).Message); RefreshBindings();
        }
    }
    private async Task CloseFromWindowAsync()
    {
        var actual = CloseAndDrainAsync();
        try { await actual; }
        catch (Exception error) { SetStatus("Canvas close needs recovery: " + ((Exception?)actual.Exception ?? error).Message); return; }
        await Dispatcher.UIThread.InvokeAsync(() => { _closeAccepted = true; Close(); });
    }
}
