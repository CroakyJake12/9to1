using System.Runtime.ExceptionServices;
using Haven.Desktop.Services;

namespace Haven.Desktop.ViewModels;

public sealed partial class WorkspaceTabViewModel
{
    private readonly object _originalRetirementGate = new();
    private readonly List<object> _abandonedOriginalPages = [];
    private readonly List<object> _admittedNavigationPages = [];
    private readonly List<Task> _originalNavigationCallbacks = [];
    private readonly HashSet<object> _requestedOriginalPages = new(ReferenceEqualityComparer.Instance);
    private readonly List<Task> _originalRetirementCallbacks = [];
    private readonly List<Exception> _originalRetirementFailures = [];
    [ThreadStatic] private static List<WorkspaceTabViewModel>? _synchronousRetirementOwners;
    private object[]? _originalRetirementPages;
    private Task? _originalRequest;
    private Task? _originalTabClose;

    /// <summary>Actual current/back/forward/abandoned objects, not persisted page identifiers.</summary>
    internal IReadOnlyList<object> CaptureOriginalPageCohort()
    {
        lock (_originalRetirementGate)
            return (_originalRetirementPages ?? CaptureCurrentOriginalPages()).ToArray();
    }

    private object[] CaptureCurrentOriginalPages() => _backHistory.Select(state => state.Page)
        .Concat(_forwardHistory.Select(state => state.Page)).Append(Page)
        .Concat(_abandonedOriginalPages).Concat(_admittedNavigationPages)
        .Distinct(ReferenceEqualityComparer.Instance).ToArray();

    private void DemandOriginalNavigationAdmission()
    {
        lock (_originalRetirementGate)
            if (_originalRetirementPages is not null) throw new ObjectDisposedException(nameof(WorkspaceTabViewModel));
    }

    private T RunOriginalNavigation<T>(object actualDestination, Func<T> actualBody)
    {
        var actual = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_originalRetirementGate)
        {
            if (_originalRetirementPages is not null) throw new ObjectDisposedException(nameof(WorkspaceTabViewModel));
            if (!_admittedNavigationPages.Any(page => ReferenceEquals(page, actualDestination)))
                _admittedNavigationPages.Add(actualDestination);
            _originalNavigationCallbacks.Add(actual.Task); // SAME synchronous original exists before any property/page callback.
        }
        (_synchronousRetirementOwners ??= []).Add(this);
        T result = default!; Exception? failure = null;
        try { result = actualBody(); }
        catch (Exception error)
        {
            failure = error;
            lock (_originalRetirementGate) AddOriginalRetirementFailure(_originalRetirementFailures, error);
        }
        finally { _synchronousRetirementOwners.RemoveAt(_synchronousRetirementOwners.Count - 1); }
        // Publish terminal settlement only AFTER the actual callback's finally bookkeeping.
        if (failure is not null)
        {
            actual.SetException(failure); // Direct cancellation is not a canceled original Task.
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        actual.SetResult(); return result;
    }

    private void RetireOriginalAbandonedPage(object page)
    {
        lock (_originalRetirementGate)
            if (!_abandonedOriginalPages.Any(original => ReferenceEquals(original, page))) _abandonedOriginalPages.Add(page);
        RequestOriginalPageOnce(page);
    }

    private void RequestOriginalPageOnce(object page)
    {
        lock (_originalRetirementGate)
            if (!_requestedOriginalPages.Add(page)) return;
        RunOriginalRetirementCallback(() =>
        {
            if (page is IDesktopOriginalRetirementParticipant participant) participant.RequestRetirement();
            else if (page is IDisposable ordinary) ordinary.Dispose();
        });
    }

    // Publish each actual synchronous callback's settlement BEFORE entering it.
    // A callback fault is never inferred to mean no child work was started.
    private void RunOriginalRetirementCallback(Action callback)
    {
        var original = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_originalRetirementGate) _originalRetirementCallbacks.Add(original.Task);
        (_synchronousRetirementOwners ??= []).Add(this);
        try { callback(); original.SetResult(); }
        catch (Exception error)
        {
            lock (_originalRetirementGate) AddOriginalRetirementFailure(_originalRetirementFailures, error);
            original.SetException(error); // Even direct OCE is a fault, not a canceled original.
            throw;
        }
        finally { _synchronousRetirementOwners.RemoveAt(_synchronousRetirementOwners.Count - 1); }
    }

    public void RequestRetirement()
    {
        var start = new TaskCompletionSource();
        Task actualRequest;
        lock (_originalRetirementGate)
        {
            if (_originalRequest is not null) return; // Request only, external close joins the SAME actual driver.
            _originalRetirementPages = CaptureCurrentOriginalPages();
            Interlocked.Exchange(ref _disposed, 1);
            _originalRequest = actualRequest = RequestOriginalCohortAsync(start.Task,
                _originalRetirementPages.ToArray(), _originalNavigationCallbacks.ToArray());
        }
        start.SetResult(); // Actual request driver published before native/page callbacks, no callback under gate.
        if (actualRequest.IsFaulted)
        {
            List<Exception> failures; lock (_originalRetirementGate) failures = _originalRetirementFailures.ToList();
            ThrowOriginalRetirementFailures(failures); // Preserve immediate ordinary request failures when settled synchronously.
        }
    }

    private async Task RequestOriginalCohortAsync(Task start, object[] pages, Task[] navigation)
    {
        await start;
        var failures = new List<Exception>();
        // A reentrant request seals immediately, but no borrowed page/tokens are
        // stopped/destroyed before every already-admitted navigation body returns.
        foreach (var actual in navigation)
            await JoinOriginalRetirementTaskAsync(actual, failures);
        (_synchronousRetirementOwners ??= []).Add(this);
        try
        {
            foreach (var page in pages.Where(page => page is IDesktopOriginalRetirementParticipant))
                try { RequestOriginalPageOnce(page); }
                catch (Exception error) { AddOriginalRetirementFailure(failures, error); }
            try { RunOriginalRetirementCallback(() => _lifetime.Cancel()); }
            catch (Exception error) { AddOriginalRetirementFailure(failures, error); }
            foreach (var page in pages.Where(page => page is not IDesktopOriginalRetirementParticipant))
                try { RequestOriginalPageOnce(page); }
                catch (Exception error) { AddOriginalRetirementFailure(failures, error); }
            try { RunOriginalRetirementCallback(() => { _backHistory.Clear(); _forwardHistory.Clear(); RaiseHistoryChanged(); }); }
            catch (Exception error) { AddOriginalRetirementFailure(failures, error); }
        }
        finally
        {
            lock (_originalRetirementGate)
                foreach (var error in failures) AddOriginalRetirementFailure(_originalRetirementFailures, error);
            _synchronousRetirementOwners.RemoveAt(_synchronousRetirementOwners.Count - 1);
        }
        ThrowOriginalRetirementFailures(failures);
    }

    public void DemandExternalOriginalRetirementJoin()
    {
        if (_synchronousRetirementOwners?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("An actual tab retirement callback must return before its external owner joins it.");
        foreach (var page in CaptureOriginalPageCohort())
            if (page is IDesktopOriginalRetirementJoinGuard guard) guard.DemandExternalOriginalRetirementJoin();
    }

    public Task CloseAndDrainAsync()
    {
        // Preflight the ENTIRE actual cohort BEFORE returning even an existing parent join.
        // Guard denial itself is not evidence of a failed child effect or successful close.
        DemandExternalOriginalRetirementJoin();
        try { RequestRetirement(); }
        catch (Exception error) { lock (_originalRetirementGate) AddOriginalRetirementFailure(_originalRetirementFailures, error); }
        var start = new TaskCompletionSource();
        Task close;
        lock (_originalRetirementGate)
        {
            if (_originalTabClose is not null) return _originalTabClose;
            _originalTabClose = close = CloseOriginalPageCohortAsync(start.Task,
                _originalRetirementPages!.ToArray(), _originalRequest!);
        }
        start.SetResult();
        return close;
    }

    private async Task CloseOriginalPageCohortAsync(Task start, object[] pages, Task actualRequest)
    {
        await start;
        var failures = new List<Exception>();
        await JoinOriginalRetirementTaskAsync(actualRequest, failures);
        var acquisitions = new List<Exception>();
        var children = new List<Task>();
        (_synchronousRetirementOwners ??= []).Add(this);
        try
        {
            // Acquiring child cleanup is deferred until all admitted navigation
            // bodies and every actual request callback are genuinely terminal.
            foreach (var page in pages)
                if (page is IDesktopOriginalRetirementParticipant participant && page is IDesktopOriginalRetirementJoinGuard)
                    try { children.Add(participant.CloseAndDrainAsync() ?? throw new InvalidOperationException("A page returned no actual original close Task.")); }
                    catch (Exception error) { AddOriginalRetirementFailure(acquisitions, error); }
        }
        finally { _synchronousRetirementOwners.RemoveAt(_synchronousRetirementOwners.Count - 1); }
        foreach (var error in acquisitions) AddOriginalRetirementFailure(failures, error);
        Task[] callbacks;
        lock (_originalRetirementGate) callbacks = _originalRetirementCallbacks.Concat(_originalNavigationCallbacks).ToArray();
        foreach (var actual in callbacks.Concat(children).Distinct<Task>(ReferenceEqualityComparer.Instance))
            await JoinOriginalRetirementTaskAsync(actual, failures);
        foreach (var page in pages)
            if (page is not IDesktopOriginalRetirementParticipant || page is not IDesktopOriginalRetirementJoinGuard)
                AddOriginalRetirementFailure(failures, new DesktopOriginalRetirementUnavailableException(page.GetType()));
        lock (_originalRetirementGate)
            foreach (var cause in _originalRetirementFailures) AddOriginalRetirementFailure(failures, cause);
        if (acquisitions.Count == 0 && pages.All(page => page is IDesktopOriginalRetirementParticipant
            && page is IDesktopOriginalRetirementJoinGuard))
            try { _lifetime.Dispose(); }
            catch (Exception error) { AddOriginalRetirementFailure(failures, error); }
        ThrowOriginalRetirementFailures(failures);
    }

    private static async Task JoinOriginalRetirementTaskAsync(Task actual, List<Exception> failures)
    {
        try { await actual.ConfigureAwait(false); }
        catch (Exception observed)
        {
            if (actual.Exception is { InnerExceptions.Count: > 0 } group)
                foreach (var cause in group.InnerExceptions) AddOriginalRetirementFailure(failures, cause);
            else AddOriginalRetirementFailure(failures, observed);
        }
    }

    private static void AddOriginalRetirementFailure(List<Exception> failures, Exception error)
    {
        if (!failures.Any(actual => ReferenceEquals(actual, error))) failures.Add(error);
    }

    private static void ThrowOriginalRetirementFailures(List<Exception> failures)
    {
        if (failures.Count == 1 && failures[0] is not OperationCanceledException)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 0) throw new AggregateException("Actual workspace page retirement remains unresolved.", failures);
    }
}
