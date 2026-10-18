using System.Runtime.ExceptionServices;
using Avalonia.Threading;

namespace Haven.Desktop.Services;

public sealed partial class NotificationService
{
    private readonly DesktopOriginalWorkLifetime _originalWork;
    private readonly object _admissionGate = new();
    private readonly List<Task> _opening = [];
    private readonly List<Exception> _acquisitionFailures = [];
    private Task? _close;
    [ThreadStatic] private static List<NotificationService>? _physicalSources;

    internal Task? OriginalClose { get { lock (_admissionGate) return _close; } }

    private Task? StartOriginal(Func<DesktopOriginalWorkLifetime.Original, Task> body, bool refuseRetired = false)
    {
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_admissionGate)
        {
            if (_disposed != 0)
            {
                if (refuseRetired) throw new ObjectDisposedException(nameof(NotificationService));
                return null; // A detached timer/event arriving late has no remaining effects.
            }
            _opening.RemoveAll(static task => task.IsCompletedSuccessfully);
            _opening.Add(opened.Task);
        }
        try
        {
            return _originalWork.RunAsync(original =>
            {
                original.BindPublicationGuard(() => Volatile.Read(ref _disposed) == 0);
                return body(original);
            });
        }
        catch (Exception cause)
        {
            lock (_admissionGate) AddNotificationCause(_acquisitionFailures, cause);
            throw;
        }
        finally { opened.SetResult(); }
    }

    private static Task PublishOriginalAsync(DesktopOriginalWorkLifetime.Original original, Action publication) =>
        Dispatcher.UIThread.InvokeAsync(() => original.RunAcceptedPublicationCallback(publication),
            DispatcherPriority.Background).GetTask();

    private async Task<bool> AwaitOriginalBackendAsync(DesktopOriginalWorkLifetime.Original original, Task actual)
    {
        // This SAME accepted body owns and awaits the raw repository task through
        // terminal completion. Never cancel a wrapper wait or discard fault siblings.
        try { await actual.ConfigureAwait(false); return true; }
        catch (OperationCanceledException stopped) when (actual.IsCanceled &&
            _lifetime.IsCancellationRequested && stopped.CancellationToken == _lifetime.Token)
        { return false; }
        catch (Exception cause) { original.Capture(actual, cause); throw; }
    }

    public void DemandExternalOriginalRetirementJoin()
    {
        if (_physicalSources?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("The original notification source callback must return before its encompassing retirement is joined.");
        _originalWork.DemandExternalClose();
    }

    public void RequestRetirement() => _ = AcquireOriginalClose();
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        return AcquireOriginalClose();
    }

    private Task AcquireOriginalClose()
    {
        var start = new TaskCompletionSource();
        Task close;
        lock (_admissionGate)
        {
            if (_close is not null) return _close;
            Volatile.Write(ref _disposed, 1);
            _close = close = CloseOriginalNotificationsAsync(start.Task, _opening.ToArray());
        }
        start.SetResult(); // The SAME close is public and admission sealed before callbacks.
        return close;
    }

    private async Task CloseOriginalNotificationsAsync(Task start, Task[] opening)
    {
        await start;
        var failures = new List<Exception>();
        foreach (var stop in new Action[]
        {
            _cleanupTimer.Stop,
            () => _cleanupTimer.Elapsed -= OnCleanupElapsed,
            () => { if (_backend is not null) _backend.Changed -= OnBackendChanged; },
            _lifetime.Cancel
        })
            try { RunPhysicalNotificationSource(stop); }
            catch (Exception cause) { AddNotificationCause(failures, cause); }

        // A timer may have reserved admission but not yet opened its canonical body.
        // Wait only for those acquisitions before canceling canonical originals, so
        // normal stop cannot race the lifetime's pre-body token check.
        foreach (var actual in opening) await JoinNotificationTask(actual, failures);
        _originalWork.RequestRetirement();
        var retirement = _originalWork.OriginalClose
            ?? throw new InvalidOperationException("The original notification retirement task was not published.");
        await JoinNotificationTask(retirement, failures);
        lock (_admissionGate)
            foreach (var cause in _acquisitionFailures) AddNotificationCause(failures, cause);
        ThrowNotificationCauses(failures);
    }

    private async Task CleanupOriginalNotificationsAsync()
    {
        var failures = new List<Exception>();
        foreach (var release in new Action[] { _cleanupTimer.Dispose, _lifetime.Dispose })
            try { RunPhysicalNotificationSource(release); }
            catch (Exception cause) { AddNotificationCause(failures, cause); }
        Task? clear = null;
        try
        {
            clear = Dispatcher.UIThread.InvokeAsync(() => RunPhysicalNotificationSource(() =>
            {
                _local.Clear();
                Notifications.Clear();
            }), DispatcherPriority.Background).GetTask();
        }
        catch (Exception cause) { AddNotificationCause(failures, cause); }
        if (clear is not null) await JoinNotificationTask(clear, failures);
        ThrowNotificationCauses(failures);
    }

    private void RunPhysicalNotificationSource(Action callback)
    {
        var owners = _physicalSources ??= [];
        owners.Add(this);
        try { callback(); }
        finally { owners.RemoveAt(owners.Count - 1); }
    }

    private static async Task JoinNotificationTask(Task actual, List<Exception> failures)
    {
        try { await actual.ConfigureAwait(false); }
        catch (Exception observed)
        {
            if (actual.Exception is { InnerExceptions.Count: > 0 } group)
                foreach (var cause in group.InnerExceptions) AddNotificationCause(failures, cause);
            else AddNotificationCause(failures, observed);
        }
    }

    private static void AddNotificationCause(List<Exception> failures, Exception cause)
    { if (!failures.Any(existing => ReferenceEquals(existing, cause))) failures.Add(cause); }
    private static void ThrowNotificationCauses(List<Exception> failures)
    {
        if (failures.Count == 1 && failures[0] is not OperationCanceledException)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 0) throw new AggregateException("Original notification work or cleanup failed.", failures);
    }
}
