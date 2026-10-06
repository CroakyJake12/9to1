using System.Runtime.ExceptionServices;
using Avalonia.Controls;

namespace Haven.Desktop.Services;

public sealed partial class ComputerUseOverlayCoordinator
{
    private readonly DesktopOriginalWorkLifetime _originalComputerUseWork;
    private readonly Func<Action, Task> _dispatchOriginalComputerUse;
    private readonly Func<Window> _createOriginalComputerUseWindow;
    private readonly object _originalComputerUseGate = new();
    private readonly Dictionary<Window, ComputerUseWindowOriginal> _originalComputerUseWindows = new(ReferenceEqualityComparer.Instance);
    private readonly List<Action> _originalComputerUseDetaches = [];
    private readonly List<Exception> _originalComputerUseFailures = [];
    [ThreadStatic] private static List<ComputerUseOverlayCoordinator>? _synchronousComputerUseSources;
    internal Task? OriginalClose => _originalComputerUseWork.OriginalClose;
    internal Task? LastOriginalUpdate { get; private set; } // Observation only; encompassing close owns the entire cohort.

    private T AcquireOriginalComputerUseSource<T>(Func<T> actualSource)
    {
        (_synchronousComputerUseSources ??= []).Add(this);
        try { return actualSource(); }
        catch (Exception cause)
        {
            lock (_originalComputerUseGate) AddComputerUseCause(_originalComputerUseFailures, cause);
            if (cause is OperationCanceledException)
                throw new AggregateException("A synchronous Computer Use callback supplied no canceled original Task.", cause);
            throw;
        }
        finally { _synchronousComputerUseSources.RemoveAt(_synchronousComputerUseSources.Count - 1); }
    }
    private void RunOriginalComputerUseSynchronous(Action actualSource) =>
        _originalComputerUseWork.RunSynchronous(_ => AcquireOriginalComputerUseSource(() => { actualSource(); return true; }));
    private void RunOriginalComputerUseEvent(Action actualSource)
    {
        if (_disposed || _originalComputerUseWork.IsRetiring) return;
        RunOriginalComputerUseSynchronous(actualSource);
    }
    private void PublishOriginalComputerUseEffect(Action actualSource)
    {
        _originalComputerUseWork.DemandAdmission();
        AcquireOriginalComputerUseSource(() => { actualSource(); return true; });
        _originalComputerUseWork.DemandAdmission(); // A genuine callback may permanently seal subsequent UI effects.
    }
    private Window AcquireOriginalComputerUseWindow()
    {
        _originalComputerUseWork.DemandAdmission();
        var actual = AcquireOriginalComputerUseSource(_createOriginalComputerUseWindow)
            ?? throw new InvalidOperationException("The actual Computer Use Window factory returned no Window.");
        ComputerUseWindowOriginal original;
        lock (_originalComputerUseGate)
        {
            if (_originalComputerUseWindows.ContainsKey(actual))
                throw new InvalidOperationException("The actual Computer Use Window factory returned a previously owned Window.");
            original = new(actual); _originalComputerUseWindows.Add(actual, original); // BEFORE initializer/setter callbacks.
        }
        EventHandler closed = (_, _) => ObserveOriginalComputerUseClosed(original);
        original.ClosedHandler = closed;
        AcquireOriginalComputerUseSource(() => { actual.Closed += closed; return true; });
        _originalComputerUseWork.DemandAdmission();
        ConfigureOriginalComputerUseWindow(actual);
        return actual;
    }
    private void ObserveOriginalComputerUseClosed(ComputerUseWindowOriginal original)
    {
        try
        {
            void Observe() => AcquireOriginalComputerUseSource(() => { return true; });
            if (_originalComputerUseWork.IsRetiring) _originalComputerUseWork.RunCloseCallback(() => Observe());
            else RunOriginalComputerUseSynchronous(() => Observe());
        }
        catch (Exception cause) { lock (_originalComputerUseGate) AddComputerUseCause(original.CallbackFailures, cause); throw; }
        finally { original.ClosedCallback.TrySetResult(); } // AFTER this actual owning callback's bookkeeping, not Window.IsVisible.
    }
    public void DemandExternalOriginalRetirementJoin()
    {
        if (_synchronousComputerUseSources?.Any(actual => ReferenceEquals(actual, this)) == true)
            throw new InvalidOperationException("The actual Computer Use callback must return before joining its encompassing UI retirement.");
        _originalComputerUseWork.DemandExternalClose();
    }
    public void RequestRetirement() => _originalComputerUseWork.RequestRetirement();
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        return _originalComputerUseWork.CloseAndDrainAsync();
    }
    private Task StopOriginalComputerUseAsync()
    {
        _disposed = true; // Withdraw this visual borrower only. NEVER Stop/TogglePause the accepted business controller.
        var detaches = new List<Action> { () => _controller.StateChanged -= OnStateChanged };
        lock (_originalComputerUseGate) detaches.AddRange(_originalComputerUseDetaches);
        foreach (var detach in detaches)
            try { _originalComputerUseWork.RunCloseCallback(() => AcquireOriginalComputerUseSource(() => { detach(); return true; })); }
            catch (Exception cause) { lock (_originalComputerUseGate) AddComputerUseCause(_originalComputerUseFailures, cause); }
        return Task.CompletedTask;
    }
    private async Task CleanupOriginalComputerUseAsync()
    {
        // Shared owner calls cleanup only after all admitted constructor/event/dispatcher originals actually settle.
        // An in-flight raw dispatcher driver is enrolled exactly once in its original AwaitAsync, without WaitAsync cancellation.
        var failures = new List<Exception>(); ComputerUseWindowOriginal[] windows;
        lock (_originalComputerUseGate) windows = _originalComputerUseWindows.Values.ToArray();
        foreach (var original in windows)
        {
            try
            {
                _originalComputerUseWork.RunCloseCallback(() => AcquireOriginalComputerUseSource(() => { original.Window.Close(); return true; }));
                if (!original.ClosedCallback.Task.IsCompleted)
                    throw new InvalidOperationException("The actual Computer Use Window.Close returned without its owning Closed callback; resources remain retained.");
            }
            catch (Exception cause) { AddComputerUseCause(failures, cause); }
            if (original.ClosedCallback.Task.IsCompleted) await JoinOriginalComputerUseTask(original.ClosedCallback.Task, failures);
            lock (_originalComputerUseGate) foreach (var cause in original.CallbackFailures) AddComputerUseCause(failures, cause);
        }
        lock (_originalComputerUseGate) foreach (var cause in _originalComputerUseFailures) AddComputerUseCause(failures, cause);
        ThrowComputerUseCauses(failures); // A veto/fault retains genuine content and native owner references.
        foreach (var original in windows)
            try
            {
                _originalComputerUseWork.RunCloseCallback(() => AcquireOriginalComputerUseSource(() =>
                {
                    if (original.ClosedHandler is { } handler) original.Window.Closed -= handler;
                    original.Window.Content = null;
                    return true;
                }));
            }
            catch (Exception cause) { AddComputerUseCause(failures, cause); }
        ThrowComputerUseCauses(failures);
        lock (_originalComputerUseGate) { _originalComputerUseWindows.Clear(); _originalComputerUseDetaches.Clear(); }
    }
    private static async Task JoinOriginalComputerUseTask(Task actual, List<Exception> failures)
    {
        try { await actual; }
        catch (Exception observed)
        {
            if (actual.Exception is { InnerExceptions.Count: > 0 } group)
                foreach (var cause in group.InnerExceptions) AddComputerUseCause(failures, cause);
            else AddComputerUseCause(failures, observed);
        }
    }
    private static void AddComputerUseCause(List<Exception> failures, Exception cause)
    { if (!failures.Any(actual => ReferenceEquals(actual, cause))) failures.Add(cause); }
    private static void ThrowComputerUseCauses(List<Exception> failures)
    {
        if (failures.Count == 1 && failures[0] is not OperationCanceledException) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 0) throw new AggregateException("Actual Computer Use UI originals and cleanup did not all acknowledge.", failures);
    }
    private sealed class ComputerUseWindowOriginal(Window actual)
    {
        internal Window Window { get; } = actual;
        internal EventHandler? ClosedHandler { get; set; }
        internal readonly TaskCompletionSource ClosedCallback = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<Exception> CallbackFailures = [];
    }
}

// Failed construction does not make an already returned native product disappear.
// Its initiating host must retain/join this exact cleanup original before borrowed services retire.
internal sealed class ComputerUseOverlayAcquisitionException(Exception original, Task originalRetirement)
    : Exception("The actual Computer Use overlay acquisition failed; its partial-product cleanup remains owned.", original)
{
    internal Task OriginalRetirement { get; } = originalRetirement;
}
