using System.ComponentModel;
using System.Runtime.ExceptionServices;
using Avalonia.Threading;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class MainWindow
{
    private readonly DesktopOriginalWorkLifetime _originalWindowWork;
    private readonly List<MainView> _originalWindowShells = [];
    private readonly List<Exception> _originalWindowStopFailures = [];
    private MainView? _actualSubscribedShell;
    private Task? _actualWindowCloseDelivery;
    private Task? _actualWindowRetirement;
    private readonly AsyncLocal<LiveWindowDelivery?> _liveWindowDelivery = new();
    private readonly TaskCompletionSource _actualClosedSettlement = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task OriginalNativeClosedCallback => _actualClosedSettlement.Task;

    // Return the SAME existing native window work token; this creates no new lifetime.
    internal CancellationToken AcquireOriginalWindowLifetime()
    {
        Dispatcher.UIThread.VerifyAccess();
        CancellationToken actual = default;
        _originalWindowWork.RunSynchronous(original =>
            AcquireOriginalWindowCallback(original, () => { original.DemandPublication(); actual = original.Token; }));
        return actual;
    }
    private sealed class LiveWindowDelivery(Task actual, LiveWindowDelivery? parent)
    {
        internal Task Actual { get; } = actual;
        internal LiveWindowDelivery? Parent { get; } = parent;
    }
    private Exception? _actualWindowCloseFailure;
    [ThreadStatic] private static List<MainWindow>? _synchronousWindowCallbacks;
    internal Task? OriginalWindowCloseDelivery => _actualWindowCloseDelivery;
    internal Exception? OriginalWindowCloseFailure => _actualWindowCloseFailure;

    private void OnOriginalDataContextChanged(object? sender, EventArgs args)
    {
        if (_originalWindowWork.IsRetiring) return;
        _originalWindowWork.RunSynchronous(original => AcquireOriginalWindowCallback(original, () =>
        {
            if (DataContext is not MainView actual) return;
            if (!_originalWindowShells.Any(shell => ReferenceEquals(shell, actual))) _originalWindowShells.Add(actual);
            _shell = actual;
            SetupBackground(); MainContent.Content = actual;
        }));
    }
    private void OnOriginalShellSurfaceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MainView.CurrentSurface) || _originalWindowWork.IsRetiring) return;
        _originalWindowWork.RunSynchronous(original => AcquireOriginalWindowCallback(original,
            () => { if (_shell is { } actual) _tidalBackground?.SetSurface(actual.CurrentSurface); }));
    }
    private void AcquireOriginalWindowCallback(DesktopOriginalWorkLifetime.Original original, Action callback)
    {
        (_synchronousWindowCallbacks ??= []).Add(this);
        try { callback(); }
        catch (Exception error)
        {
            original.Retain(error);
            if (error is OperationCanceledException) throw new AggregateException("An actual synchronous window callback is not a canceled original Task.", error);
            throw;
        }
        finally { _synchronousWindowCallbacks.RemoveAt(_synchronousWindowCallbacks.Count - 1); }
    }
    public void DemandExternalOriginalRetirementJoin()
    {
        if (_synchronousWindowCallbacks?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("An actual native window callback must return before its external join.");
        for (var original = _liveWindowDelivery.Value; original is not null; original = original.Parent)
            if (!original.Actual.IsCompleted) throw new InvalidOperationException("An actual window delivery must return before its encompassing retirement join.");
        _originalWindowWork.DemandExternalClose();
        foreach (object actual in _originalWindowShells)
            if (actual is IDesktopOriginalRetirementJoinGuard guard) guard.DemandExternalOriginalRetirementJoin();
    }
    public void RequestRetirement() => _originalWindowWork.RequestRetirement();
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        if (_actualWindowRetirement is not null) return _actualWindowRetirement;
        var start = new TaskCompletionSource();
        _actualWindowRetirement = JoinOriginalWindowRetirementAsync(start.Task);
        start.SetResult();
        return _actualWindowRetirement;
    }
    private async Task JoinOriginalWindowRetirementAsync(Task start)
    {
        await start;
        var failures = new List<Exception>();
        var resources = _originalWindowWork.CloseAndDrainAsync();
        var actualDelivery = _actualWindowCloseDelivery;
        await JoinActualWindowTaskAsync(resources, failures);
        if (actualDelivery is not null) await JoinActualWindowTaskAsync(actualDelivery, failures);
        ThrowWindowFailures(failures);
    }
    private static async Task JoinActualWindowTaskAsync(Task actual, List<Exception> failures)
    {
        try { await actual; }
        catch (Exception observed)
        {
            if (actual.Exception is { InnerExceptions.Count: > 0 } group)
                foreach (var original in group.InnerExceptions) AddWindowFailure(failures, original);
            else AddWindowFailure(failures, observed);
        }
    }
    private Task StopOriginalWindowSourcesAsync()
    {
        void Stop(Action original)
        {
            try { _originalWindowWork.RunCloseCallback(original); }
            catch (Exception error) { AddWindowFailure(_originalWindowStopFailures, error); }
        }
        Stop(() => DataContextChanged -= OnOriginalDataContextChanged);
        if (_preferences is not null) Stop(() => _preferences.AppearanceChanged -= OnAppearanceChanged);
        if (_actualSubscribedShell is { } subscribed) Stop(() => subscribed.PropertyChanged -= OnOriginalShellSurfaceChanged);
        foreach (object actual in _originalWindowShells)
            if (actual is IDesktopOriginalRetirementParticipant participant) Stop(participant.RequestRetirement);
        return Task.CompletedTask;
    }
    private async Task CleanupOriginalWindowResourcesAsync()
    {
        var failures = _originalWindowStopFailures.ToList();
        // A setup callback admitted before the seal may finish installing its
        // actual observer later; detach that SAME late reference after originals settle.
        if (_actualSubscribedShell is { } subscribed)
            try { _originalWindowWork.RunCloseCallback(() => subscribed.PropertyChanged -= OnOriginalShellSurfaceChanged); }
            catch (Exception error) { AddWindowFailure(failures, error); }
        var actualTasks = new List<Task>();
        foreach (object actual in _originalWindowShells)
        {
            if (actual is not IDesktopOriginalRetirementParticipant participant || actual is not IDesktopOriginalRetirementJoinGuard guard)
            { AddWindowFailure(failures, new DesktopOriginalRetirementUnavailableException(actual.GetType())); continue; }
            try
            {
                guard.DemandExternalOriginalRetirementJoin();
                Task? original = null;
                _originalWindowWork.RunCloseCallback(() => original = participant.CloseAndDrainAsync());
                actualTasks.Add(original ?? throw new InvalidOperationException("The actual window shell returned no close Task."));
            }
            catch (Exception error) { AddWindowFailure(failures, error); }
        }
        foreach (var actual in actualTasks.Distinct<Task>(ReferenceEqualityComparer.Instance))
            try { await actual; }
            catch (Exception observed)
            {
                if (actual.Exception is { InnerExceptions.Count: > 0 } group)
                    foreach (var original in group.InnerExceptions) AddWindowFailure(failures, original);
                else AddWindowFailure(failures, observed);
            }
        ThrowWindowFailures(failures); // Unknown shell retains borrowed native/UI resources.
        try { _originalWindowWork.RunCloseCallback(() => _tidalBackground?.Dispose()); }
        catch (Exception error) { AddWindowFailure(failures, error); }
        try { _originalWindowWork.RunCloseCallback(() => MainContent.Content = null); }
        catch (Exception error) { AddWindowFailure(failures, error); }
        ThrowWindowFailures(failures);
        _tidalBackground = null; _shell = null; _actualSubscribedShell = null;
    }
    private void RequestOriginalWindowCloseDelivery()
    {
        if (_actualWindowCloseDelivery is not null) return;
        var start = new TaskCompletionSource();
        _actualWindowCloseDelivery = DeliverOriginalWindowCloseAsync(start.Task);
        start.SetResult();
    }
    private async Task DeliverOriginalWindowCloseAsync(Task start)
    {
        await start;
        var previous = _liveWindowDelivery.Value;
        var live = new LiveWindowDelivery(_actualWindowCloseDelivery ?? throw new InvalidOperationException("The actual window delivery was not published."), previous);
        _liveWindowDelivery.Value = live;
        try
        {
            // Original Closing callback returns before independent external joins.
            await AwaitOriginalWindowDeliveryTaskAsync(AcquireActualWindowDeliveryTask(
                () => Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background).GetTask()));
            if (PreserveWorkspaceSessionOnClose && App.Services?.GetService<WorkspaceSessionCoordinator>() is { } sessions)
                await AwaitOriginalWindowDeliveryTaskAsync(AcquireActualWindowDeliveryTask(
                    () => sessions.SaveNowAndCancelPendingAsync(CancellationToken.None)));
            // This delivery owns the subsequent native close; its public encompassing
            // retirement joins this delivery, so it joins ONLY its private resource original here.
            await AwaitOriginalWindowDeliveryTaskAsync(AcquireActualWindowDeliveryTask(_originalWindowWork.CloseAndDrainAsync));
            await AwaitOriginalWindowDeliveryTaskAsync(AcquireActualWindowDeliveryTask(
                () => Dispatcher.UIThread.InvokeAsync(() => _originalWindowWork.RunCloseCallback(Close)).GetTask()));
            if (!OriginalNativeClosedCallback.IsCompleted)
                throw new InvalidOperationException("The actual native window refused its owning close request.");
            await AwaitOriginalWindowDeliveryTaskAsync(OriginalNativeClosedCallback);
        }
        catch (Exception error)
        {
            _actualWindowCloseFailure = error;
            if (error is OperationCanceledException)
                throw new AggregateException("The actual window delivery remains uncertain; its cancellation cause is retained.", error);
            throw;
        }
        finally { _liveWindowDelivery.Value = previous; }
    }
    private Task AcquireActualWindowDeliveryTask(Func<Task> actualSource)
    {
        (_synchronousWindowCallbacks ??= []).Add(this);
        try { return actualSource() ?? throw new InvalidOperationException("The actual window delivery source returned no Task."); }
        catch (OperationCanceledException original)
        { throw new AggregateException("A synchronous window delivery source supplied no canceled original Task.", original); }
        finally { _synchronousWindowCallbacks.RemoveAt(_synchronousWindowCallbacks.Count - 1); }
    }
    private static async Task AwaitOriginalWindowDeliveryTaskAsync(Task actual)
    {
        var failures = new List<Exception>();
        await JoinActualWindowTaskAsync(actual, failures);
        ThrowWindowFailures(failures); // Full direct fault siblings; faulted OCE is never CLR-canceled success.
    }
    private static void AddWindowFailure(List<Exception> failures, Exception error)
    { if (!failures.Any(original => ReferenceEquals(original, error))) failures.Add(error); }
    private static void ThrowWindowFailures(List<Exception> failures)
    {
        if (failures.Count == 1 && failures[0] is not OperationCanceledException) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 0) throw new AggregateException("Actual native window resources remain unresolved.", failures);
    }
}
