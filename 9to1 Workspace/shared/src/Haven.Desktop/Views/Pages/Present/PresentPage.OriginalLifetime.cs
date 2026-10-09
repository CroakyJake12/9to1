using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Present;

public sealed partial class PresentPage : IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private readonly DesktopOriginalWorkLifetime _originalPresentWork;
    private readonly TaskCompletionSource _originalPresentConstruction = new(TaskCreationOptions.RunContinuationsAsynchronously);
    [ThreadStatic] private static List<PresentPage>? _actualPresentSources;
    private int _activeOriginalPresentBodies;
    private Task? _latestOriginalPresentAction;
    internal Task? LatestOriginalAction => _latestOriginalPresentAction;
    internal Task? OriginalClose => _originalPresentWork.OriginalClose;

    // The caller's persistence cancellation remains authoritative. Retiring the
    // view seals admission, then joins accepted writes with controls/services alive.
    // No page cancellation is substituted for an accepted repository save.
    private Task RunOriginalPresentAsync(Func<Task> body)
    {
        if (_originalPresentWork.Executing is { } admitted)
            return admitted.AwaitAsync(AcquireOriginalPresentSource(body));
        return _originalPresentWork.RunAsync(async original =>
        {
            Interlocked.Increment(ref _activeOriginalPresentBodies);
            try { await original.AwaitAsync(AcquireOriginalPresentSource(body)); }
            finally { Interlocked.Decrement(ref _activeOriginalPresentBodies); }
        }, actual => _latestOriginalPresentAction = actual);
    }
    private Task<T> RunOriginalPresentAsync<T>(Func<Task<T>> body)
    {
        if (_originalPresentWork.Executing is { } admitted)
            return admitted.AwaitAsync(AcquireOriginalPresentSource(body));
        return _originalPresentWork.RunAsync(async original =>
        {
            Interlocked.Increment(ref _activeOriginalPresentBodies);
            try { return await original.AwaitAsync(AcquireOriginalPresentSource(body)); }
            finally { Interlocked.Decrement(ref _activeOriginalPresentBodies); }
        });
    }
    private Task AwaitOriginalPresentSource(Func<Task> source)
    {
        var original = _originalPresentWork.Executing
            ?? throw new InvalidOperationException("The actual Present service source has no original owner.");
        return original.AwaitAsync(AcquireOriginalPresentSource(source));
    }
    private Task<T> AwaitOriginalPresentSource<T>(Func<Task<T>> source)
    {
        var original = _originalPresentWork.Executing
            ?? throw new InvalidOperationException("The actual Present service source has no original owner.");
        return original.AwaitAsync(AcquireOriginalPresentSource(source));
    }
    private async Task UseOriginalPresentStreamAsync(Func<Task<Stream>> acquire, Func<Stream, Task> body)
    {
        var stream = await AwaitOriginalPresentSource(acquire);
        await UseAcquiredOriginalPresentStreamAsync(stream, body);
    }
    private Task UseOriginalPresentStreamAsync(Func<Stream> acquire, Func<Stream, Task> body) =>
        UseAcquiredOriginalPresentStreamAsync(AcquireOriginalPresentSource(acquire), body);
    private async Task UseAcquiredOriginalPresentStreamAsync(Stream stream, Func<Stream, Task> body)
    {
        try { await AwaitOriginalPresentSource(() => body(stream)); }
        finally
        {
            // Convert this SAME actual disposal ValueTask once. Its independent
            // siblings survive an earlier body failure in the original owner.
            await AwaitOriginalPresentSource(() => stream.DisposeAsync().AsTask());
        }
    }
    private T AcquireOriginalPresentSource<T>(Func<T> body)
    {
        (_actualPresentSources ??= []).Add(this);
        try { return body(); }
        catch (OperationCanceledException error)
        { throw new AggregateException("A synchronous Present callback supplied no canceled original Task.", error); }
        finally { _actualPresentSources.RemoveAt(_actualPresentSources.Count - 1); }
    }
    private T RunOriginalPresentSynchronous<T>(Func<T> body)
    {
        if (_originalPresentWork.Executing is { } admitted)
        {
            try { return AcquireOriginalPresentSource(body); }
            catch (Exception error) { admitted.Retain(error); throw; }
        }
        T result = default!;
        _originalPresentWork.RunSynchronous(original => result = AcquireOriginalPresentSource(body));
        return result;
    }
    private void RunOriginalPresentEvent(Action body)
    {
        if (_disposed || _originalPresentWork.IsRetiring) return;
        RunOriginalPresentSynchronous(() => { body(); return true; });
    }
    private void ObserveOriginalPresentEvent(Func<Task> body)
    {
        if (_disposed || _originalPresentWork.IsRetiring) return;
        // RunAsync publishes the SAME actual task before invoking the event body.
        // Observation prevents an async-void escape; the owner retains failures.
        var actual = RunOriginalPresentAsync(body);
        _ = ObserveAsync(actual);
        static async Task ObserveAsync(Task original)
        { try { await original; } catch { /* retained by the actual lifetime */ } }
    }
    // Current preflight condition only; an external owner still joins the SAME
    // actual originals and every retained failure after sealing retirement.
    internal bool IsOriginalDocumentClosePrepared
    {
        get
        {
            Avalonia.Threading.Dispatcher.UIThread.VerifyAccess();
            return !_originalPresentWork.IsRetiring && !_disposed && _initialized &&
                _originalInitializationFailure is null && !_busy && !_closePreparing &&
                Volatile.Read(ref _saveRunning) == 0 && Volatile.Read(ref _activeOriginalPresentBodies) == 0 &&
                !_dirty && Document?.Recovery.RecoveredFromBackup != true;
        }
    }
    internal void DemandOriginalInitializedDocument()
    {
        _originalPresentWork.DemandAdmission();
        RunOriginalPresentSynchronous(() =>
        {
            if (_originalInitializationFailure is { } originalFailure)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(originalFailure).Throw();
            if (!_initialized || _busy || Document is null || _disposed)
                throw new InvalidOperationException("The actual original Present document initialization is incomplete.");
            return true;
        });
    }
    internal void PublishOriginalDocumentTab(Action publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        _originalPresentWork.DemandAdmission();
        RunOriginalPresentSynchronous(() => { publication(); return true; });
    }
    private void PublishOriginalPresentSource(Action publication) =>
        RunOriginalPresentSynchronous(() => { publication(); return true; });
    private void StartOriginalPresentAutosave() => PublishOriginalPresentSource(() =>
    {
        if (!_originalPresentWork.IsRetiring) _autosaveTimer.Start();
    });
    private void RetainHandledOriginalPresentCause(Exception error) => _originalPresentWork.Executing?.Retain(error);

    public void DemandExternalOriginalRetirementJoin()
    {
        if (_actualPresentSources?.Any(page => ReferenceEquals(page, this)) == true)
            throw new InvalidOperationException("The actual Present callback must return before its external retirement join.");
        _originalPresentWork.DemandExternalClose();
    }
    public void RequestRetirement() => _originalPresentWork.RequestRetirement();
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        return _originalPresentWork.CloseAndDrainAsync();
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private async Task StopOriginalPresentSourcesAsync()
    {
        await _originalPresentConstruction.Task;
        _originalPresentWork.RunCloseCallback(() =>
        {
            _autosaveTimer?.Stop();
            if (_autosaveTimer is not null) _autosaveTimer.Tick -= OnAutosaveTick;
            Loaded -= OnLoaded;
            DetachedFromVisualTree -= OnDetachedFromVisualTree;
        });
    }
    private async Task CleanupOriginalPresentAsync()
    {
        await _originalPresentConstruction.Task;
        _originalPresentWork.RunCloseCallback(() =>
        {
            if (_dirty || Document?.Recovery.RecoveredFromBackup == true || Volatile.Read(ref _saveRunning) != 0)
                throw new InvalidOperationException("The actual Present draft is not acknowledged by durable storage; retain its original document and scene.");
            _disposed = true;
            DisposeOriginalPresentControls();
        });
        // Dirty document remains retained on this exact page, including a failed
        // write. A destructive host close must first pass PrepareToCloseAsync.
    }
}
