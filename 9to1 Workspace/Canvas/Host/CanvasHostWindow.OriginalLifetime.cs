using Avalonia.Threading;

namespace HavenOS.Apps.Canvas;

public sealed partial class CanvasHostWindow
{
    /// <summary>A failed acquired surface must finish its original child drain before the host
    /// releases it. Dispose alone schedules that drain and cannot acknowledge completion.</summary>
    internal static async Task InitializeOwnedSurfaceAsync(CanvasNativeCuiSurface surface, CancellationToken token)
    {
        Task? initialization = null;
        try
        {
            initialization = surface.InitializeAsync(token);
            await initialization;
        }
        catch (Exception initializationError)
        {
            var errors = new List<Exception>();
            CaptureOriginalFailure(errors, initialization, initializationError);
            Task? close = null;
            try { close = surface.CloseAndDrainAsync(); await close; }
            catch (Exception closeError) { CaptureOriginalFailure(errors, close, closeError); }
            if (errors.Count == 1 && errors[0] is not OperationCanceledException)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            throw new AggregateException("Canvas initialization and acquired surface cleanup failed.", errors);
        }
    }
    private static void CaptureOriginalFailure(List<Exception> errors, Task? actual, Exception caught)
    {
        if (actual?.Exception is { } group)
            foreach (var direct in group.InnerExceptions) AddOriginalFailure(errors, direct);
        else AddOriginalFailure(errors, caught);
    }
    private static void AddOriginalFailure(List<Exception> errors, Exception error)
    {
        // Deeper aggregates belong to the source. Even an empty group is a fault.
        if (!errors.Any(previous => ReferenceEquals(previous, error))) errors.Add(error);
    }
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
