using System.Runtime.ExceptionServices;
using Haven.Application;

namespace Haven.Desktop.Services;

/// <summary>One native host's actual shutdown originals. This is not a permission,
/// Task/Run store or cleanliness authority: the prepared writer remains the sole final marker owner.</summary>
internal sealed class DesktopOriginalShutdownSequence(
    Func<Task> retireAndJoinActualAppProducers,
    Func<Task> captureAndSaveOriginalSession,
    Func<Task<IStartupRecoveryFinalCleanWriter>> prepareOriginalFinalWriter,
    Func<Task> retireAndJoinActualBorrowers,
    Func<Task> disposeActualProvider,
    Func<Action, Task>? scheduleExternalOriginalStart = null)
{
    private readonly object _gate = new();
    private readonly List<Task> _actualSources = [];
    private readonly List<Exception> _failures = [];
    private readonly Dictionary<Exception, Exception[]> _ownedGroups = new(ReferenceEqualityComparer.Instance);
    private readonly AsyncLocal<LiveOriginal?> _executing = new();
    [ThreadStatic] private static List<DesktopOriginalShutdownSequence>? _synchronousSources;
    private Task? _originalShutdown;
    private Task<IStartupRecoveryFinalCleanWriter>? _actualRequiredDrain;
    private Task? _actualExternalStart;

    private sealed class LiveOriginal(LiveOriginal? parent)
    {
        internal LiveOriginal? Parent { get; } = parent;
        internal bool Active = true;
        internal Task? ActualSource;
        internal bool IsLive => Volatile.Read(ref Active) && Volatile.Read(ref ActualSource) is not { IsCompleted: true };
    }
    internal Task? OriginalShutdown { get { lock (_gate) return _originalShutdown; } }
    internal Task? OriginalRequiredDrain { get { lock (_gate) return _actualRequiredDrain; } }
    internal IReadOnlyList<Task> ActualSourceTasks { get { lock (_gate) return _actualSources.ToArray(); } }
    internal IReadOnlyList<Exception> OriginalFailures { get { lock (_gate) return _failures.ToArray(); } }

    internal void RequestShutdown() => _ = AcquireOriginalShutdown();
    internal Task CloseAndDrainAsync()
    {
        for (var live = _executing.Value; live is not null; live = live.Parent)
            if (live.IsLive) throw new InvalidOperationException("An actual native shutdown source must return before its external owner joins it.");
        if (_synchronousSources?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("An actual native shutdown callback must return before its external owner joins it.");
        return AcquireOriginalShutdown();
    }
    private Task AcquireOriginalShutdown()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startCaptured = new TaskCompletionSource();
        Task original;
        lock (_gate)
        {
            if (_originalShutdown is not null) return _originalShutdown;
            _originalShutdown = original = RunOriginalShutdownAsync(start.Task, startCaptured.Task);
        }
        if (scheduleExternalOriginalStart is null)
        {
            startCaptured.SetResult(); start.SetResult();
        }
        else
        {
            try
            {
                var previous = _executing.Value;
                var sourceScope = new LiveOriginal(previous);
                _executing.Value = sourceScope;
                try
                {
                    var actual = AcquireActual(() => scheduleExternalOriginalStart(() => start.TrySetResult()));
                    Volatile.Write(ref sourceScope.ActualSource, actual);
                    lock (_gate) _actualExternalStart = actual;
                }
                catch { Volatile.Write(ref sourceScope.Active, false); throw; }
                finally { _executing.Value = previous; }
            }
            catch (Exception error)
            {
                Retain(error); start.TrySetResult(); // No phase was admitted; expose failed original scheduling.
            }
            finally { startCaptured.SetResult(); }
        }
        return original;
    }

    private async Task RunOriginalShutdownAsync(Task start, Task startCaptured)
    {
        await start;
        await startCaptured; // Scheduling source is captured even if its callback signals synchronously.
        Task? actualStart; lock (_gate) actualStart = _actualExternalStart;
        // The exact UI scheduling callback must have returned, not merely posted a signal,
        // before external joins run. This is no presentation/Ready witness.
        if (actualStart is not null) await JoinActualAsync(actualStart);
        ThrowOriginalFailures();
        // Preserve the native caller's genuine UI context; no Task.Run/delay witness.
        var previous = _executing.Value;
        var live = new LiveOriginal(previous);
        _executing.Value = live;
        try
        {
            var drainStart = new TaskCompletionSource();
            Task<IStartupRecoveryFinalCleanWriter> actualDrain;
            lock (_gate) _actualRequiredDrain = actualDrain = DrainActualRequiredOriginalsAsync(drainStart.Task);
            drainStart.SetResult(); // Publish actual drain before callbacks, outside metadata gate.
            var writer = await actualDrain;
            // The exact real required-drain Task is already terminal. Never pass this
            // currently executing controller to its child writer (that would self-join).
            var final = AcquireActual(() => writer.CompleteAfterOriginalDrainAsync(actualDrain, CancellationToken.None));
            await JoinActualAsync(final);
            ThrowOriginalFailures();
        }
        catch (Exception error)
        {
            Retain(error);
            ThrowOriginalFailures();
        }
        finally
        {
            Volatile.Write(ref live.Active, false);
            _executing.Value = previous;
        }
    }

    private async Task<IStartupRecoveryFinalCleanWriter> DrainActualRequiredOriginalsAsync(Task start)
    {
        await start;
        IStartupRecoveryFinalCleanWriter? writer = null;
        try { await JoinActualAsync(AcquireActual(retireAndJoinActualAppProducers)); }
        catch (Exception error) { Retain(error); }
        if (!HasOriginalFailure())
        {
            try { await JoinActualAsync(AcquireActual(captureAndSaveOriginalSession)); }
            catch (Exception error) { Retain(error); }
        }
        if (!HasOriginalFailure())
        {
            try
            {
                var actual = AcquireActual(prepareOriginalFinalWriter);
                await JoinActualAsync(actual);
                writer = actual.GetAwaiter().GetResult() ?? throw new InvalidOperationException("No original prepared final writer was returned.");
            }
            catch (Exception error) { Retain(error); }
        }
        // An earlier failure must never drop the already known child stop/drain.
        // The owning callback independently acquires/joins ALL genuine child originals.
        try { await JoinActualAsync(AcquireActual(retireAndJoinActualBorrowers)); }
        catch (Exception error) { Retain(error); }
        // Unknown/failed borrowers keep the provider/UI alive: do not destroy them
        // and then manufacture a successful or timeout-based all-drained receipt.
        ThrowOriginalFailures();
        try { await JoinActualAsync(AcquireActual(disposeActualProvider)); }
        catch (Exception error) { Retain(error); }
        ThrowOriginalFailures();
        return writer ?? throw new InvalidOperationException("The original final writer preparation is unavailable.");
    }

    private T AcquireActual<T>(Func<T> source) where T : Task
    {
        (_synchronousSources ??= []).Add(this);
        try
        {
            var actual = source() ?? throw new InvalidOperationException("An actual native shutdown source returned no Task.");
            lock (_gate) _actualSources.Add(actual);
            return actual;
        }
        catch (Exception error)
        {
            Retain(error);
            if (error is OperationCanceledException) throw OwnedGroup("A direct shutdown callback returned no canceled original Task.", [error]);
            throw;
        }
        finally { _synchronousSources.RemoveAt(_synchronousSources.Count - 1); }
    }
    private async Task JoinActualAsync(Task actual)
    {
        try { await actual; }
        catch (Exception observed)
        {
            if (actual.Exception is { InnerExceptions.Count: > 0 } group)
                foreach (var cause in group.InnerExceptions) Retain(cause);
            else Retain(observed);
            ThrowOriginalFailures();
        }
    }
    private bool HasOriginalFailure() { lock (_gate) return _failures.Count != 0; }
    private void Retain(Exception error)
    {
        lock (_gate)
        {
            if (_ownedGroups.TryGetValue(error, out var originals))
            { foreach (var original in originals) Retain(original); }
            else if (!_failures.Any(original => ReferenceEquals(original, error))) _failures.Add(error);
        }
    }
    private AggregateException OwnedGroup(string message, Exception[] originals)
    {
        var group = new AggregateException(message, originals);
        lock (_gate) _ownedGroups.Add(group, originals);
        return group;
    }
    private void ThrowOriginalFailures()
    {
        Exception[] failures;
        lock (_gate) failures = _failures.ToArray();
        if (failures.Length == 1 && failures[0] is not OperationCanceledException)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Length > 0) throw OwnedGroup("Actual native shutdown originals remain unresolved; no clean acknowledgement was issued.", failures);
    }
}
