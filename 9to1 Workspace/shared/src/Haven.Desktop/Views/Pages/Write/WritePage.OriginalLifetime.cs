using Avalonia.Threading;
using Haven.Desktop.Services;
using System.Runtime.ExceptionServices;

namespace Haven.Desktop.Views.Pages.Write;

public sealed partial class WritePage
{
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly TaskCompletionSource _constructionSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    [ThreadStatic] private static List<WritePage>? _physicalSources;
    private Task? _latestOriginalAction;
    private long _statsGeneration;
    private int _pendingAsyncBodies;
    private bool _readAloudStopUnacknowledged;
    internal Func<Task> StatsDelaySource { get; set; } = () => Task.Delay(TimeSpan.FromMilliseconds(300));
    internal Task? OriginalClose => _work.OriginalClose;

    internal bool IsOriginalDocumentClosePrepared => Dispatcher.UIThread.CheckAccess()
        && !_work.IsRetiring && !_disposed && _initialized && _originalInitializationFailure is null
        && (_initialDocumentId is null || Document is not null) && !_busy && !_closePreparing
        && Volatile.Read(ref _saveRunning) == 0 && Volatile.Read(ref _pendingAsyncBodies) == 0
        && !_dirty && Document?.Recovery.HasUnsavedRecovery != true && !_readAloudStopUnacknowledged
        && _readAloud?.IsActive != true;

    internal Task? LatestOriginalAction => _latestOriginalAction;

    private Task RunOriginalAsync(Func<Task> body)
    {
        if (_work.Executing is { } admitted)
        {
            var actual = RunCountedBodyAsync(admitted, body);
            _latestOriginalAction = actual;
            return actual;
        }
        return _work.RunAsync(original =>
        {
            original.BindPublicationGuard(() => !_disposed);
            return RunCountedBodyAsync(original, body);
        }, actual => _latestOriginalAction = actual);
    }

    private Task<T> RunOriginalAsync<T>(Func<Task<T>> body)
    {
        if (_work.Executing is { } admitted) return RunCountedBodyAsync(admitted, body);
        return _work.RunAsync(original =>
        {
            original.BindPublicationGuard(() => !_disposed);
            return RunCountedBodyAsync(original, body);
        });
    }

    private async Task RunCountedBodyAsync(DesktopOriginalWorkLifetime.Original original, Func<Task> body)
    {
        Interlocked.Increment(ref _pendingAsyncBodies);
        try { await original.AwaitAsync(InvokePhysicalSource(body)); }
        finally { Interlocked.Decrement(ref _pendingAsyncBodies); }
    }

    private async Task<T> RunCountedBodyAsync<T>(DesktopOriginalWorkLifetime.Original original, Func<Task<T>> body)
    {
        Interlocked.Increment(ref _pendingAsyncBodies);
        try { return await original.AwaitAsync(InvokePhysicalSource(body)); }
        finally { Interlocked.Decrement(ref _pendingAsyncBodies); }
    }

    private void RunOriginalEvent(Func<Task> body)
    {
        if (_work.IsRetiring) return;
        try
        {
            _ = RunOriginalAsync(async () =>
            {
                try { await ObserveOriginalAsync(body); }
                catch (Exception error)
                {
                    _work.Executing!.Retain(error);
                    Publish(() => _route.SetStatus("Couldn't finish this Write operation: " + error.Message));
                }
            });
        }
        catch (ObjectDisposedException) when (_work.IsRetiring) { }
    }

    private Task ObserveOriginalAsync(Func<Task> source)
    {
        var original = _work.Executing ?? throw new InvalidOperationException("No original owns this Write operation.");
        // Acquire and retain the actual returned task before any continuation guard.
        try { return original.AwaitAsync(InvokePhysicalSource(source)); }
        catch (Exception error) { original.Retain(error); throw; }
    }

    private Task<T> ObserveOriginalAsync<T>(Func<Task<T>> source)
    {
        var original = _work.Executing ?? throw new InvalidOperationException("No original owns this Write operation.");
        try { return original.AwaitAsync(InvokePhysicalSource(source)); }
        catch (Exception error) { original.Retain(error); throw; }
    }

    private void QualifyOriginalSpeechSource(Task actual, NotesReadAloudController.OriginalSpeechSource issued)
    {
        var original = _work.Executing ?? throw new InvalidOperationException("No original owns this Write speech source.");
        original.QualifyOriginalSpeechSource(actual, issued);
    }

    private async Task StopReadAloudOriginalAsync()
    {
        if (_readAloud is null) return;
        try { await ObserveOriginalAsync(() => _readAloud.StopOriginalAsync(CancellationToken.None, ObserveOriginalAsync)); }
        catch { _readAloudStopUnacknowledged = true; throw; }
    }

    private bool CanPublishOriginal => !_work.IsRetiring && !_disposed
        && (_work.Executing?.IsPublicationCurrent ?? true);

    private void Publish(Action callback)
    {
        if (!CanPublishOriginal) return;
        InvokePhysicalSource(() => { callback(); return true; });
    }

    private void QueueOriginalPublication(Action publication) => RunOriginalEvent(() =>
        ObserveOriginalAsync(() => Dispatcher.UIThread.InvokeAsync(() => Publish(publication)).GetTask()));

    private void ScheduleOriginalStats(Action publication)
    {
        var generation = Interlocked.Increment(ref _statsGeneration);
        RunOriginalEvent(async () =>
        {
            await ObserveOriginalAsync(StatsDelaySource);
            await ObserveOriginalAsync(() => Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Volatile.Read(ref _statsGeneration) == generation) Publish(publication);
            }).GetTask());
        });
    }

    private IAsyncDisposable OwnOriginalStreamDisposal(Stream actualStream) => new OriginalStreamDisposal(this, actualStream);

    private sealed class OriginalStreamDisposal(WritePage owner, Stream actualStream) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => new(owner.ObserveOriginalAsync(() => actualStream.DisposeAsync().AsTask()));
    }

    private void OwnOriginalSceneCallback(Action callback)
    {
        if (!CanPublishOriginal) return;
        InvokePhysicalSource(() =>
        {
            _work.RunSynchronous(original =>
            {
                original.BindPublicationGuard(() => !_disposed);
                original.DemandPublication();
                callback();
            });
            return true;
        });
    }

    internal void PublishOriginalDocumentTab(Action publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        _work.DemandAdmission();
        OwnOriginalSceneCallback(publication);
    }

    internal void DemandOriginalInitializedDocument()
    {
        _work.DemandAdmission();
        OwnOriginalSceneCallback(() =>
        {
            if (_originalInitializationFailure is { } failure) ExceptionDispatchInfo.Capture(failure).Throw();
            if (!_initialized || _busy || (_initialDocumentId is not null && Document is null))
                throw new InvalidOperationException("The original Write document workspace has not initialized.");
            // A launch without a document ID intentionally initializes the real local library.
        });
    }

    internal void DemandOriginalPreparedClose()
    {
        Dispatcher.UIThread.VerifyAccess();
        _work.DemandAdmission();
        // This is a pure preflight observation. A normal refusal admits no work
        // and must leave the same page usable for completing its save and retrying.
        if (_originalInitializationFailure is { } failure) ExceptionDispatchInfo.Capture(failure).Throw();
        if (!IsOriginalDocumentClosePrepared)
            throw new InvalidOperationException("The original Write workspace is no longer ready to close; finish its operation and save the current draft.");
    }

    private T InvokePhysicalSource<T>(Func<T> source)
    {
        var stack = _physicalSources ??= [];
        stack.Add(this);
        try { return source(); }
        finally { stack.RemoveAt(stack.Count - 1); }
    }

    public void DemandExternalOriginalRetirementJoin()
    {
        if (_physicalSources?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("An original Write source callback cannot join its own page retirement.");
        _work.DemandExternalClose();
    }

    public void RequestRetirement() => _work.RequestRetirement();
    public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); return _work.CloseAndDrainAsync(); }
    public void Dispose() => RequestRetirement();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private async Task StopOriginalPresentationAsync()
    {
        await _constructionSettled.Task.ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => _work.RunCloseCallback(() =>
        {
            DetachOriginalCallbacks();
            _route?.RequestRetirement();
        })).GetTask().ConfigureAwait(false);
        // Borrowed speech services are stopped, never disposed by this page. Accepted
        // document storage is deliberately not canceled by presentation retirement.
        var failures = new List<Exception>();
        var speechSources = new List<Task>();
        var speechGate = new object();
        Task ObserveClosingSpeechSource(Func<Task> source)
        {
            var actual = InvokePhysicalSource(source);
            lock (speechGate) speechSources.Add(actual);
            return actual;
        }
        Task? cancellation = null, stop = null;
        try { cancellation = InvokePhysicalSource(_readAloudSessionCts.CancelAsync); }
        catch (Exception error) { failures.Add(error); }
        try { if (_readAloud is not null) stop = InvokePhysicalSource(() => _readAloud.StopOriginalAsync(CancellationToken.None, ObserveClosingSpeechSource)); }
        catch (Exception error) { failures.Add(error); }
        foreach (var actual in new[] { cancellation, stop })
        {
            if (actual is null) continue;
            try { await actual.ConfigureAwait(false); }
            catch (Exception error)
            {
                if (actual.Exception is { InnerExceptions.Count: > 0 } group) failures.AddRange(group.InnerExceptions);
                else failures.Add(error);
            }
        }
        Task[] actualSpeechSources;
        lock (speechGate) actualSpeechSources = speechSources.ToArray();
        foreach (var actual in actualSpeechSources)
            try { await actual.ConfigureAwait(false); }
            catch (Exception error)
            {
                if (actual.Exception is { InnerExceptions.Count: > 0 } group)
                    foreach (var cause in group.InnerExceptions)
                        if (!failures.Any(existing => ReferenceEquals(existing, cause))) failures.Add(cause);
                else if (!failures.Any(existing => ReferenceEquals(existing, error))) failures.Add(error);
            }
        if (failures.Count != 0) _readAloudStopUnacknowledged = true;
        if (failures.Count != 0) throw new AggregateException("Original Write read-aloud stop failed.", failures);
    }

    private async Task CleanupOriginalPresentationAsync()
    {
        await _constructionSettled.Task.ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => _work.RunCloseCallback(() =>
        {
            if (_dirty || Document?.Recovery.HasUnsavedRecovery == true || Volatile.Read(ref _saveRunning) != 0)
                throw new InvalidOperationException("The current Write draft has not been acknowledged by storage; its document and scene remain retained.");
            if (_readAloudStopUnacknowledged || _readAloud?.IsActive == true)
                throw new InvalidOperationException("Original Write speech playback has not stopped; its scene remains retained.");
            _route?.Dispose();
            _readAloudSessionCts.Dispose();
            if (_route is not null && ReferenceEquals(Scene?.Root, _route.Root)) Scene.Root = null;
            Content = null;
        })).GetTask().ConfigureAwait(false);
    }
}
