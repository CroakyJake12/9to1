using System.Runtime.ExceptionServices;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Go;

public sealed partial class GoPage
{
    private readonly DesktopOriginalWorkLifetime _originalGoWork;
    private readonly List<Action> _originalGoDetaches = [];
    private readonly Dictionary<Task, Action> _originalParentBorrowers = new(ReferenceEqualityComparer.Instance);
    internal void RetainOriginalParentBorrower(Task actualOriginal, Action actualProducerJoinGuard)
    {
        ArgumentNullException.ThrowIfNull(actualOriginal);
        ArgumentNullException.ThrowIfNull(actualProducerJoinGuard);
        _originalGoWork.DemandAdmission();
        foreach (var healthy in _originalParentBorrowers.Keys.Where(task => task.IsCompletedSuccessfully).ToArray())
            _originalParentBorrowers.Remove(healthy);
        if (!_originalParentBorrowers.TryAdd(actualOriginal, actualProducerJoinGuard)) return;
        // SAME actual parent task is custody only, never a Task/Run acceptance,
        // permission or presentation receipt. Parent source enrolls before callback.
        _ = _originalGoWork.RunAsync(original => original.AwaitAsync(actualOriginal));
    }
    [ThreadStatic] private static List<GoPage>? _actualSynchronousGoSources;
    internal Task? OriginalClose => _originalGoWork.OriginalClose;

    private T AcquireOriginalGoSource<T>(Func<T> actualSource)
    {
        (_actualSynchronousGoSources ??= []).Add(this);
        try { return actualSource(); }
        catch (OperationCanceledException original)
        { throw new AggregateException("An actual synchronous Go publication supplied no canceled original Task.", original); }
        finally { _actualSynchronousGoSources.RemoveAt(_actualSynchronousGoSources.Count - 1); }
    }
    private void RunOriginalGoSynchronous(Action actualBody) =>
        _originalGoWork.RunSynchronous(original => AcquireOriginalGoSource(() => { actualBody(); return true; }));
    private T RunOriginalGoSynchronous<T>(Func<T> actualBody)
    {
        T result = default!;
        _originalGoWork.RunSynchronous(original => result = AcquireOriginalGoSource(actualBody));
        return result;
    }
    private void RunOriginalGoEvent(Action actualBody)
    {
        if (_disposed || _originalGoWork.IsRetiring) return;
        RunOriginalGoSynchronous(actualBody);
    }
    public void DemandExternalOriginalRetirementJoin()
    {
        if (_actualSynchronousGoSources?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("The actual Go publication must return before joining its original retirement.");
        _originalGoWork.DemandExternalClose();
        foreach (var actual in _originalParentBorrowers)
            if (!actual.Key.IsCompleted) actual.Value();
    }
    public void RequestRetirement() => _originalGoWork.RequestRetirement();
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        return _originalGoWork.CloseAndDrainAsync();
    }
    private Task StopOriginalGoSourcesAsync()
    {
        _disposed = true; // Withdraw future UI admission; no canonical Task/Run cancellation is issued.
        var failures = new List<Exception>();
        void Stop(Action actual)
        {
            try { _originalGoWork.RunCloseCallback(actual); }
            catch (Exception original) { AddGoCause(failures, original); }
        }
        foreach (var actual in _originalGoDetaches) Stop(actual);
        foreach (var (element, handler) in _stateSubscriptions) Stop(() => element.Invalidated -= handler);
        _originalGoDetaches.Clear(); _stateSubscriptions.Clear();
        ThrowGoCauses(failures);
        return Task.CompletedTask;
    }
    private async Task CleanupOriginalGoAsync()
    {
        var failures = new List<Exception>();
        void Cleanup(Action actual)
        {
            try { _originalGoWork.RunCloseCallback(actual); }
            catch (Exception original) { AddGoCause(failures, original); }
        }
        // Enrollment can fail before a wrapper enters its body. The retained
        // SAME raw parent originals remain independently owned and must settle;
        // a proxy/enrollment Task or capacity refusal is not their terminal proof.
        foreach (var actual in _originalParentBorrowers.Keys.ToArray())
            try { await actual; }
            catch (Exception observed)
            {
                if (actual.Exception is { InnerExceptions.Count: > 0 } group)
                    foreach (var cause in group.InnerExceptions) AddGoCause(failures, cause);
                else AddGoCause(failures, observed);
            }
        // SAME factor has joined every actual synchronous publication before
        // releasing this owning route/renderer. External asynchronous subscribers
        // remain separately owned by Controller/Shell; event delivery is no receipt.
        Cleanup(_route.Dispose);
        Cleanup(() => Scene.Root = null);
        Cleanup(() => Disposed?.Invoke(this, EventArgs.Empty));
        ThrowGoCauses(failures);
    }
    private static void AddGoCause(List<Exception> failures, Exception original)
    { if (!failures.Any(cause => ReferenceEquals(cause, original))) failures.Add(original); }
    private static void ThrowGoCauses(List<Exception> failures)
    {
        if (failures.Count == 1 && failures[0] is not OperationCanceledException) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 0) throw new AggregateException("Actual Go stop/cleanup supplied original failures.", failures);
    }
}
