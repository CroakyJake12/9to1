using Haven.Desktop.Services;
using Avalonia.Threading;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private DispatcherTimer? _automationTimer;
    private CancellationTokenSource? _automationCancellation;
    private int _isRunningDueAutomations;

    private void StartAutomationScheduler()
    {
        if (_automationTimer is not null) return;
        _originalShellWork.DemandAdmission();
        _automationCancellation = new CancellationTokenSource();
        _originalAutomationSources.Add(_automationCancellation);
        _automationTimer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background,
            (_, _) => { if (!IsDisposed) _ = RunDueAutomationsTickAsync(); });
        _automationTimer.Start();
    }

    private void StopAutomationScheduler()
    {
        _automationTimer?.Stop();
        _automationTimer = null;
        var source = _automationCancellation;
        _automationCancellation = null;
        // The same CTS is retained until the actual RunDue original has settled.
        source?.Cancel();
    }

    private Task RunDueAutomationsTickAsync() => _originalShellWork.RunAsync(RunOriginalDueAutomationsTickAsync);

    private async Task RunOriginalDueAutomationsTickAsync(DesktopOriginalWorkLifetime.Original original)
    {
        if (Interlocked.Exchange(ref _isRunningDueAutomations, 1) != 0) return;
        var cancellationSource = _automationCancellation;
        var cancellationToken = cancellationSource is { IsCancellationRequested: false } ? cancellationSource.Token : CancellationToken.None;
        try
        {
            var result = await original.AwaitAsync(AcquireOriginalShellSynchronous(original, () => _automationRunner.RunDueAsync(DateTimeOffset.UtcNow, cancellationToken))).ConfigureAwait(true);
            if (result.Started > 0)
                _bus.Fire("Shell.Automations.DueRan");
        }
        catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested)
        { original.Retain(error); }
        catch (Exception exception)
        {
            original.Retain(exception);
            System.Diagnostics.Debug.WriteLine($"[Scheduled automations] Due run failed: {exception.Message}");
            _bus.Fire("Shell.Automations.DueRunFailed");
        }
        finally
        {
            Interlocked.Exchange(ref _isRunningDueAutomations, 0);
        }
    }
}
