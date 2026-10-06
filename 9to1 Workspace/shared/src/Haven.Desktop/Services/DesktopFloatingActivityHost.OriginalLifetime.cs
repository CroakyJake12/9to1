using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Media;
using Haven.Core;
using Haven.Application;

namespace Haven.Desktop.Services;

public sealed partial class DesktopFloatingActivityHost
{
    private const int RetainedFloatingOwnerLimit = 128;
    private readonly object _originalFloatingGate = new();
    private DesktopOriginalWorkLifetime? _originalFloatingWork;
    private readonly HashSet<IFloatingActivityContent> _originalFloatingWrappers = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object?> _originalFloatingContents = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Window, FloatingWindowOriginal> _originalFloatingWindows = new(ReferenceEqualityComparer.Instance);
    private readonly List<Action> _originalFloatingDetaches = [];
    private readonly List<Exception> _originalFloatingFailures = [];
    [ThreadStatic] private static List<DesktopFloatingActivityHost>? _actualSynchronousFloatingSources;
    private DesktopOriginalWorkLifetime OriginalFloatingWork
    {
        get { lock (_originalFloatingGate) return _originalFloatingWork ??= new(StopOriginalFloatingAsync, CleanupOriginalFloatingAsync); }
    }
    internal Task? OriginalClose => OriginalFloatingWork.OriginalClose;

    private T AcquireOriginalFloatingSource<T>(Func<T> actualSource)
    {
        (_actualSynchronousFloatingSources ??= []).Add(this);
        try { return actualSource(); }
        catch (Exception original)
        {
            lock (_originalFloatingGate) AddFloatingCause(_originalFloatingFailures, original);
            if (original is OperationCanceledException)
                throw new AggregateException("A synchronous floating source supplied no canceled original Task.", original);
            throw;
        }
        finally { _actualSynchronousFloatingSources.RemoveAt(_actualSynchronousFloatingSources.Count - 1); }
    }
    private T RunOriginalFloatingSynchronous<T>(Func<T> actualBody)
    {
        T result = default!;
        OriginalFloatingWork.RunSynchronous(_ => result = AcquireOriginalFloatingSource(actualBody));
        return result;
    }
    private void RunOriginalFloatingEvent(Action actualBody)
    {
        if (OriginalFloatingWork.IsRetiring) return;
        _ = RunOriginalFloatingSynchronous(() => { actualBody(); return true; });
    }
    private void RetainOriginalFloatingWrapper(IFloatingActivityContent actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        lock (_originalFloatingGate)
        {
            if (!_originalFloatingWrappers.Contains(actual)) DemandFloatingRetentionCapacity(_originalFloatingWrappers.Count);
            _originalFloatingWrappers.Add(actual);
        }
    }
    private void RetainOriginalFloatingContent(object? actual)
    {
        lock (_originalFloatingGate)
        {
            if (!_originalFloatingContents.Contains(actual)) DemandFloatingRetentionCapacity(_originalFloatingContents.Count);
            _originalFloatingContents.Add(actual);
        }
    }
    private void DemandOriginalFloatingWindowCapacity()
    { lock (_originalFloatingGate) DemandFloatingRetentionCapacity(_originalFloatingWindows.Count); }
    private Window? CaptureCurrentFloatingWindow(Guid activityId) => _windows.GetValueOrDefault(activityId);
    private void DemandOriginalFloatingCurrent(Guid activityId, Window? actualExpected, bool actualClosedObservation = false)
    {
        if (!actualClosedObservation) OriginalFloatingWork.DemandAdmission();
        if (!ReferenceEquals(CaptureCurrentFloatingWindow(activityId), actualExpected))
            throw new InvalidOperationException("The actual floating owning record was replaced during its original callback.");
    }
    private void PublishOriginalFloatingEffect(Guid activityId, Window? actualExpected, Action actualEffect)
    {
        DemandOriginalFloatingCurrent(activityId, actualExpected);
        AcquireOriginalFloatingSource(() => { actualEffect(); return true; });
        DemandOriginalFloatingCurrent(activityId, actualExpected); // Acknowledged effect is retained; never blindly rolled back/replayed.
    }
    private Window CreateOriginalFloatingWindow(Guid activityId, Window? actualExpected)
    {
        DemandOriginalFloatingCurrent(activityId, actualExpected); DemandOriginalFloatingWindowCapacity();
        var actual = AcquireOriginalFloatingSource(() => new Window());
        RetainOriginalFloatingWindow(activityId, actual); // BEFORE ANY property initializer/configuration callback.
        DemandOriginalFloatingCurrent(activityId, actualExpected);
        return actual;
    }
    private void ConfigureOriginalFloatingWindow(Window actual, FloatingActivityDefinition definition, Window? actualExpected)
    {
        void Set(Action source) => PublishOriginalFloatingEffect(definition.Id, actualExpected, source);
        Set(() => actual.Title = definition.Title);
        Set(() => actual.Width = 420); Set(() => actual.Height = 280);
        Set(() => actual.MinWidth = 240); Set(() => actual.MinHeight = 160);
        Set(() => actual.CanResize = true); Set(() => actual.ShowInTaskbar = false);
        Set(() => actual.Topmost = definition.AlwaysOnTop);
        Set(() => actual.WindowDecorations = WindowDecorations.None);
        Set(() => actual.Background = Brushes.Transparent);
        Set(() => actual.TransparencyBackgroundFallback = Brushes.Transparent);
        Set(() => actual.TransparencyLevelHint = [WindowTransparencyLevel.Transparent]);
    }
    private void RetainOriginalFloatingWindow(Guid activityId, Window actual)
    {
        FloatingWindowOriginal original;
        lock (_originalFloatingGate)
        {
            if (_originalFloatingWindows.ContainsKey(actual)) return;
            DemandFloatingRetentionCapacity(_originalFloatingWindows.Count);
            original = new(activityId, actual); _originalFloatingWindows.Add(actual, original);
        }
        EventHandler closed = (_, _) => ObserveActualFloatingClosed(original);
        original.ClosedHandler = closed;
        AcquireOriginalFloatingSource(() => { actual.Closed += closed; return true; }); // Own callback exists before configuration.
    }
    private void DemandFloatingRetentionCapacity(int retained)
    {
        if (retained < RetainedFloatingOwnerLimit) return;
        var actual = new InvalidOperationException("Floating original owner retention requires external retirement before further presentation.");
        AddFloatingCause(_originalFloatingFailures, actual);
        throw actual;
    }
    private void AttachOriginalFloatingDetach(Action actualDetach)
    { lock (_originalFloatingGate) _originalFloatingDetaches.Add(actualDetach); }
    [ThreadStatic] private static List<FloatingWindowOriginal>? _actualFloatingClosedObservations;
    private bool IsActualFloatingClosedObservation(Window actual) =>
        _actualFloatingClosedObservations?.Any(original => ReferenceEquals(original.Window, actual)) == true;
    private void AttachOriginalFloatingClosed(Window window, Action actualCallback)
    {
        lock (_originalFloatingGate) _originalFloatingWindows[window].ActualClosedCallback = actualCallback;
    }
    private void ObserveActualFloatingClosed(FloatingWindowOriginal original)
    {
        (_actualFloatingClosedObservations ??= []).Add(original);
        try
        {
            void Publish() => AcquireOriginalFloatingSource(() => { original.ActualClosedCallback?.Invoke(); return true; });
            if (OriginalFloatingWork.IsRetiring) OriginalFloatingWork.RunCloseCallback(Publish);
            else _ = RunOriginalFloatingSynchronous(() => { Publish(); return true; });
        }
        catch (Exception cause) { AddFloatingCause(original.CallbackFailures, cause); throw; }
        finally
        {
            _actualFloatingClosedObservations.RemoveAt(_actualFloatingClosedObservations.Count - 1);
            original.ClosedCallback.TrySetResult(); // Complete only AFTER actual owning callback bookkeeping.
        }
    }
    public void DemandExternalOriginalRetirementJoin()
    {
        if (_actualSynchronousFloatingSources?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("An actual floating publication must return before joining its encompassing retirement.");
        OriginalFloatingWork.DemandExternalClose();
        foreach (var owner in CaptureOriginalFloatingContentOwners()) owner.Guard.DemandExternalOriginalRetirementJoin();
    }
    public void RequestRetirement() => OriginalFloatingWork.RequestRetirement();
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        return OriginalFloatingWork.CloseAndDrainAsync();
    }
    private (IDesktopOriginalRetirementParticipant Participant, IDesktopOriginalRetirementJoinGuard Guard)[] CaptureOriginalFloatingContentOwners()
    {
        object?[] actualWrappers; object?[] actualContents;
        lock (_originalFloatingGate)
        { actualWrappers = _originalFloatingWrappers.Cast<object?>().ToArray(); actualContents = _originalFloatingContents.ToArray(); }
        var result = new List<(IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard)>();
        foreach (var wrapper in actualWrappers)
        {
            if (wrapper is DesktopFloatingActivityContent) continue; // Exact sealed zero-producer wrapper, not its child.
            AddOwner(wrapper, allowStatic: false);
        }
        foreach (var content in actualContents) AddOwner(content, allowStatic: true);
        return result.ToArray();
        void AddOwner(object? actual, bool allowStatic)
        {
            if (allowStatic && (actual is null || actual is string)) return;
            if (actual is not IDesktopOriginalRetirementParticipant participant || actual is not IDesktopOriginalRetirementJoinGuard guard)
                throw new DesktopOriginalRetirementUnavailableException(actual?.GetType() ?? typeof(object));
            if (!result.Any(owner => ReferenceEquals(owner.Item1, participant))) result.Add((participant, guard));
        }
    }
    private Task StopOriginalFloatingAsync()
    {
        _disposed = true; // UI admission only. No durable canonical Task/Run cancellation.
        Action[] detach;
        lock (_originalFloatingGate) detach = _originalFloatingDetaches.ToArray();
        foreach (var actual in detach)
            try { OriginalFloatingWork.RunCloseCallback(() => AcquireOriginalFloatingSource(() => { actual(); return true; })); }
            catch (Exception cause) { lock (_originalFloatingGate) AddFloatingCause(_originalFloatingFailures, cause); }
        // Keep the actual Closed callback through final native Close. Ordinary hide/dismiss
        // has not certified the captured child cohort as retired.
        return Task.CompletedTask;
    }
    private async Task CleanupOriginalFloatingAsync()
    {
        var failures = new List<Exception>();
        var owners = CaptureOriginalFloatingContentOwners(); // Whole genuine cohort before any child teardown.
        foreach (var owner in owners) owner.Guard.DemandExternalOriginalRetirementJoin();
        foreach (var owner in owners)
            try { OriginalFloatingWork.RunCloseCallback(() => AcquireOriginalFloatingSource(() => { owner.Participant.RequestRetirement(); return true; })); }
            catch (Exception cause) { AddFloatingCause(failures, cause); }
        var closes = new List<Task>();
        foreach (var owner in owners)
            try
            {
                Task? actual = null;
                OriginalFloatingWork.RunCloseCallback(() => actual = AcquireOriginalFloatingSource(owner.Participant.CloseAndDrainAsync));
                closes.Add(actual ?? throw new InvalidOperationException("The actual floating content returned no original close Task."));
            }
            catch (Exception cause) { AddFloatingCause(failures, cause); }
        foreach (var actual in closes.Distinct<Task>(ReferenceEqualityComparer.Instance)) await JoinFloatingOriginal(actual, failures);
        lock (_originalFloatingGate) foreach (var cause in _originalFloatingFailures) AddFloatingCause(failures, cause);
        ThrowFloatingCauses(failures); // Unknown/failed child retains the genuine content and native windows.

        FloatingWindowOriginal[] windows;
        lock (_originalFloatingGate) windows = _originalFloatingWindows.Values.ToArray();
        foreach (var original in windows)
        {
            try
            {
                OriginalFloatingWork.RunCloseCallback(() => AcquireOriginalFloatingSource(() => { original.Window.Close(); return true; }));
                if (!original.ClosedCallback.Task.IsCompleted)
                    throw new InvalidOperationException("The actual floating Window.Close returned without its owning Closed callback; content remains retained.");
            }
            catch (Exception cause) { AddFloatingCause(failures, cause); }
            if (original.ClosedCallback.Task.IsCompleted) await JoinFloatingOriginal(original.ClosedCallback.Task, failures);
            foreach (var cause in original.CallbackFailures) AddFloatingCause(failures, cause);
        }
        lock (_originalFloatingGate) foreach (var cause in _originalFloatingFailures) AddFloatingCause(failures, cause);
        ThrowFloatingCauses(failures); // Raw native close plus this callback are scoped proof, not all external subscribers.
        foreach (var original in windows)
            try
            {
                OriginalFloatingWork.RunCloseCallback(() => AcquireOriginalFloatingSource(() =>
                {
                    if (original.ClosedHandler is { } handler) original.Window.Closed -= handler;
                    original.Window.Content = null;
                    return true;
                }));
            }
            catch (Exception cause) { AddFloatingCause(failures, cause); }
        ThrowFloatingCauses(failures);
        lock (_originalFloatingGate)
        { _windows.Clear(); _originalFloatingWindows.Clear(); _originalFloatingDetaches.Clear(); _originalFloatingContents.Clear(); _originalFloatingWrappers.Clear(); }
    }
    private static async Task JoinFloatingOriginal(Task actual, List<Exception> failures)
    {
        try { await actual; }
        catch (Exception observed)
        {
            if (actual.Exception is { InnerExceptions.Count: > 0 } group)
                foreach (var cause in group.InnerExceptions) AddFloatingCause(failures, cause);
            else AddFloatingCause(failures, observed);
        }
    }
    private static void AddFloatingCause(List<Exception> failures, Exception original)
    { if (!failures.Any(cause => ReferenceEquals(cause, original))) failures.Add(original); }
    private static void ThrowFloatingCauses(List<Exception> failures)
    {
        if (failures.Count == 1 && failures[0] is not OperationCanceledException) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 0) throw new AggregateException("Actual floating originals remain unresolved.", failures);
    }
    private sealed class FloatingWindowOriginal(Guid activityId, Window window)
    {
        internal Guid ActivityId { get; } = activityId;
        internal Window Window { get; } = window;
        internal EventHandler? ClosedHandler { get; set; }
        internal Action? ActualClosedCallback { get; set; }
        internal TaskCompletionSource ClosedCallback { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<Exception> CallbackFailures { get; } = [];
    }
}
