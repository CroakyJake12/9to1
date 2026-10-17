using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

public sealed partial class NewDashboardPage
{
    private DesktopOriginalWorkLifetime _homeWork = null!;
    private readonly List<Task> _homeWrites = [];
    private readonly HashSet<Flyout> _homeEditors = [];
    private Exception? _homeSaveFailure;
    public Task? OriginalClose => _homeWork.OriginalClose;

    private Task<T> AwaitHomeSourceAsync<T>(Task<T> actual) =>
        (_homeWork.Executing ?? throw new InvalidOperationException("Home source work requires its owning operation.")).AwaitAsync(actual);
    private Task AwaitHomeSourceAsync(Task actual) =>
        (_homeWork.Executing ?? throw new InvalidOperationException("Home source work requires its owning operation.")).AwaitAsync(actual);

    private Task RunHomeWorkAsync(Func<CancellationToken, Task> body, CancellationToken token = default) =>
        _homeWork.Executing is { } current
            ? current.AwaitAsync(RunHomeOriginalAsync(current, body, token))
            : _homeWork.RunAsync(original => RunHomeOriginalAsync(original, body, token));

    private static async Task RunHomeOriginalAsync(DesktopOriginalWorkLifetime.Original original,
        Func<CancellationToken, Task> body, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(original.Token, token);
        Task? source = null;
        await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() => { source = body(linked.Token); }).GetTask()).ConfigureAwait(false);
        await original.AwaitAsync(source ?? throw new InvalidOperationException("The Home operation was not acquired.")).ConfigureAwait(false);
    }

    private Task RunHomeSaveAsync(Func<Task> body)
    {
        var task = _homeWork.Executing is { } current
            ? current.AwaitAsync(RunHomeSaveOriginalAsync(current, body))
            : _homeWork.RunAsync(original => RunHomeSaveOriginalAsync(original, body));
        lock (_homeWrites) { _homeWrites.RemoveAll(item => item.IsCompletedSuccessfully); _homeWrites.Add(task); }
        return task;
    }
    private async Task RunHomeSaveOriginalAsync(DesktopOriginalWorkLifetime.Original original, Func<Task> body)
    {
        Task? source = null;
        try
        {
            // Already-admitted saves finish with their original store acknowledgement. Closing
            // cancels discovery/read work but must not abandon a partially persisted layout.
            await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() => { source = body(); }).GetTask()).ConfigureAwait(false);
            await original.AwaitAsync(source ?? throw new InvalidOperationException("The Home save was not acquired.")).ConfigureAwait(false);
            _homeSaveFailure = null;
        }
        catch (Exception error) { _homeSaveFailure = error; throw; }
    }

    private async Task RunHomeEventAsync(Func<CancellationToken, Task> body)
    {
        try { await RunHomeWorkAsync(body); }
        catch (Exception error)
        {
            // The lifetime retains the original failure for its external close. UI event
            // handlers report it here instead of escaping through async-void dispatch.
            if (!_disposed && _widgetStatus is not null) SetDashboardValue(_widgetStatus, "Text", error.Message);
        }
    }

    private void ShowHomeEditor(Flyout flyout, Control anchor)
    {
        if (_homeWork.IsRetiring) return;
        _homeEditors.Add(flyout);
        flyout.Closed += (_, _) => _homeEditors.Remove(flyout);
        flyout.ShowAt(anchor);
    }

    public async Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hasDraft = await Dispatcher.UIThread.InvokeAsync(() => _homeEditors.Any(editor => editor.IsOpen)
            || _dashboardAssistantBusy || !string.IsNullOrWhiteSpace(_dashboardAssistantInput?.Text));
        if (hasDraft) return false;
        Task[] writes; lock (_homeWrites) writes = _homeWrites.ToArray();
        try { await Task.WhenAll(writes).WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return false; }
        return _homeSaveFailure is null;
    }

    public Task CloseAndDrainAsync() => _homeWork.CloseAndDrainAsync();

    private async Task StopHomeWorkAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _disposed = true;
            Deactivate();
            foreach (var editor in _homeEditors.ToArray()) editor.Hide();
        });
    }

    private async Task DetachHomeWorkAsync()
    {
        Task? actions = null;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _dashboardLoader?.Dispose();
            actions = _dashboardLoader?.WhenActionsIdleAsync();
        });
        if (actions is not null) await actions.ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _widgetRefreshCancellation?.Dispose();
            _dashboardAssistantCancellation?.Dispose();
            Content = null;
        });
    }
}
