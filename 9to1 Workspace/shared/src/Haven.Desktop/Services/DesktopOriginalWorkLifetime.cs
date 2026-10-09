using System.Runtime.ExceptionServices;

namespace Haven.Desktop.Services;

/// <summary>Page-owned originals only. This is not a provider, permission, frame settlement,
/// or generated-widget authority. An admitted action must return before an external owner
/// joins this encompassing close. Borrowed services remain owned by their existing hosts.</summary>
internal class DesktopOriginalWorkLifetime : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Original> _originals = [];
    private readonly List<Exception> _admissionFailures = [];
    private InvalidOperationException? _capacityRefusal;
    private readonly Func<Task> _stop;
    private readonly Func<Task> _cleanup;
    private bool _retiring;
    private Task? _close;
    private readonly AsyncLocal<Original?> _executing = new();
    private readonly AsyncLocal<Task?> _executingClose = new();
    [ThreadStatic] private static List<DesktopOriginalWorkLifetime>? _synchronousCloseOwners;
    private readonly Dictionary<Exception, Exception[]> _ownedGroups = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Task, NotesReadAloudController.OriginalSpeechSource?> _originalSpeechSources = [];

    internal DesktopOriginalWorkLifetime(Func<Task> stop, Func<Task> cleanup)
    { _stop = stop ?? throw new ArgumentNullException(nameof(stop)); _cleanup = cleanup ?? throw new ArgumentNullException(nameof(cleanup)); }

    internal bool IsRetiring { get { lock (_gate) return _retiring; } }
    internal Original? Executing => FindLiveExecutingOriginal();
    private Original? FindLiveExecutingOriginal()
    {
        // A nested callback may be terminal while its inherited encompassing
        // original still awaits that callback's child. Follow only exact private
        // references issued by this owner; status/IDs are not substitute custody.
        for (var original = _executing.Value; original is not null; original = original.Parent)
        {
            if (!original.IsOwnedBy(this)) return null;
            if (original.HasLiveWork) return original;
        }
        return null;
    }
    internal Task? OriginalClose { get { lock (_gate) return _close; } }

    internal Task RunAsync(Func<Original, Task> actualBody, Action<Task>? publishOriginal = null)
    {
        ArgumentNullException.ThrowIfNull(actualBody);
        var start = new TaskCompletionSource();
        Original original;
        lock (_gate)
        {
            if (_retiring) throw new ObjectDisposedException("NewChatOriginalWorkLifetime");
            if (_capacityRefusal is { } recordedRefusal) throw recordedRefusal;
            _originals.RemoveAll(static item => item.CanPruneSuccessful);
            if (_originals.Count >= 128)
            {
                var refusal = _capacityRefusal ??= new InvalidOperationException("Original chat work capacity requires external page retirement.");
                Add(_admissionFailures, refusal);
                throw refusal;
            }
            original = new Original(this, _lifetime.Token, FindLiveExecutingOriginal());
            original.Task = RunCoreAsync(start.Task, original, actualBody);
            _originals.Add(original); // SAME returned Task exists before bus/provider/native callbacks.
            try { publishOriginal?.Invoke(original.Task); } // Owner-only assignment, before the body gate opens.
            catch (Exception error) { original.Retain(error); }
        }
        start.SetResult();
        return original.Task;
    }

    internal Task<T> RunAsync<T>(Func<Original, Task<T>> actualBody)
    {
        ArgumentNullException.ThrowIfNull(actualBody);
        var start = new TaskCompletionSource();
        Task<T> returned;
        lock (_gate)
        {
            if (_retiring) throw new ObjectDisposedException("NewChatOriginalWorkLifetime");
            if (_capacityRefusal is { } recordedRefusal) throw recordedRefusal;
            _originals.RemoveAll(static item => item.CanPruneSuccessful);
            if (_originals.Count >= 128)
            {
                var refusal = _capacityRefusal ??= new InvalidOperationException("Original chat work capacity requires external page retirement.");
                Add(_admissionFailures, refusal);
                throw refusal;
            }
            var original = new Original(this, _lifetime.Token, FindLiveExecutingOriginal());
            returned = RunCoreAsync(start.Task, original, actualBody);
            original.Task = returned;
            _originals.Add(original);
        }
        start.SetResult();
        return returned;
    }

    internal void RunSynchronous(Action<Original> actualCallback)
    {
        ArgumentNullException.ThrowIfNull(actualCallback);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Original original;
        lock (_gate)
        {
            if (_retiring) throw new ObjectDisposedException("NewChatOriginalWorkLifetime");
            if (_capacityRefusal is { } recordedRefusal) throw recordedRefusal;
            _originals.RemoveAll(static item => item.CanPruneSuccessful);
            if (_originals.Count >= 128)
            {
                var refusal = _capacityRefusal ??= new InvalidOperationException("Original chat work capacity requires external page retirement.");
                Add(_admissionFailures, refusal);
                throw refusal;
            }
            original = new Original(this, _lifetime.Token, FindLiveExecutingOriginal()) { Task = completion.Task };
            // This is the actual synchronous callback's completion, not an unjoined
            // asynchronous body wrapper. It exists before any callback can reenter.
            _originals.Add(original);
        }
        var previous = _executing.Value;
        _executing.Value = original;
        try
        {
            original.Token.ThrowIfCancellationRequested();
            actualCallback(original);
            completion.SetResult();
        }
        catch (Exception directCause)
        {
            original.Retain(directCause);
            try { original.ThrowRetained(); }
            catch (Exception terminalCause) { completion.SetException(terminalCause); }
            ExceptionDispatchInfo.Capture(directCause).Throw();
        }
        finally { _executing.Value = previous; }
    }

    private async Task RunCoreAsync(Task start, Original original, Func<Original, Task> body)
    {
        await start;
        var previous = _executing.Value;
        _executing.Value = original;
        var failedBody = false;
        try
        {
            original.ThrowRetained();
            original.Token.ThrowIfCancellationRequested();
            original.Body = body(original);
            await original.Body;
        }
        catch (Exception error) { failedBody = true; original.Capture(original.Body, error); }
        finally { _executing.Value = previous; }
        // Preserve existing handled-policy return behavior. Its retained causes still
        // prevent successful pruning and remain part of the actual external close.
        if (failedBody) original.ThrowRetained();
    }

    private async Task<T> RunCoreAsync<T>(Task start, Original original, Func<Original, Task<T>> body)
    {
        await start;
        var previous = _executing.Value;
        _executing.Value = original;
        T result = default!;
        var failedBody = false;
        try
        {
            original.ThrowRetained();
            original.Token.ThrowIfCancellationRequested();
            var actual = body(original);
            original.Body = actual;
            result = await actual;
        }
        catch (Exception error) { failedBody = true; original.Capture(original.Body, error); }
        finally { _executing.Value = previous; }
        if (failedBody) original.ThrowRetained();
        return result;
    }

    internal void DemandAdmission()
    { if (IsRetiring) throw new ObjectDisposedException("NewChatOriginalWorkLifetime"); }
    internal bool IsOriginalAdmissionRefusal(Exception actualCause)
    { lock (_gate) return _admissionFailures.Any(item => ReferenceEquals(item, actualCause)); }
    internal void RequestRetirement() => _ = AcquireClose();
    internal void DemandExternalClose()
    {
        if (Executing is not null || _executingClose.Value is { IsCompleted: false } ||
            _synchronousCloseOwners?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("An admitted page original or active stop/cleanup callback must request retirement, return, and be joined by an external owner.");
    }
    internal Task CloseAndDrainAsync()
    {
        DemandExternalClose();
        return AcquireClose();
    }
    internal void RunCloseCallback(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Task actualClose;
        lock (_gate) actualClose = _close ?? throw new InvalidOperationException("No original close was published before this cleanup callback.");
        var previous = _executingClose.Value;
        _executingClose.Value = actualClose;
        try { RunPhysicalCloseCallback(callback); }
        finally { _executingClose.Value = previous; }
    }

    private void RunPhysicalCloseCallback(Action callback)
    {
        // CancellationToken registrations restore their captured ExecutionContext.
        // This physical synchronous stack survives that restoration and preserves
        // each exact owner even when another owner's close is nested here.
        var owners = _synchronousCloseOwners ??= [];
        owners.Add(this);
        try { callback(); }
        finally { owners.RemoveAt(owners.Count - 1); }
    }

    private Task AcquireClose()
    {
        var start = new TaskCompletionSource();
        Task close;
        lock (_gate)
        {
            if (_close is not null) return _close;
            _retiring = true;
            close = CloseCoreAsync(start.Task, _originals.ToArray());
            _close = close; // Publish/seal before cancellation or stop callbacks can reenter.
        }
        start.SetResult();
        return close;
    }

    private async Task CloseCoreAsync(Task start, Original[] originals)
    {
        await start;
        var previousClose = _executingClose.Value;
        _executingClose.Value = _close ?? throw new InvalidOperationException("The actual close task was not published.");
        try
        {
        var failures = new List<Exception>();
        try { RunPhysicalCloseCallback(_lifetime.Cancel); } catch (Exception error) { Add(failures, error); }
        Task? stop = null;
        try { RunPhysicalCloseCallback(() => stop = _stop()); } catch (Exception error) { Add(failures, error); }
        var terminalObservations = new List<(Original Original, Exception Cause)>();
        var stageObservations = new List<(Original Original, Task Stage, Exception Cause)>();
        // Stop starts before a provider/iterator that may be waiting on scoped cancellation.
        foreach (var original in originals)
        {
            try { await original.Task; }
            catch (Exception error) { terminalObservations.Add((original, error)); }
            foreach (var stage in original.UnjoinedStages)
                try { await stage; } catch (Exception error) { stageObservations.Add((original, stage, error)); }
        }
        if (stop is not null) await Join(stop, failures);
        // Expected local playback interruption is known only after the controller's
        // SAME stop original and raw native acknowledgement are both terminal.
        foreach (var observation in terminalObservations)
            observation.Original.CollectTerminal(failures, observation.Cause);
        foreach (var observation in stageObservations)
            observation.Original.CollectStageTerminal(failures, observation.Stage, observation.Cause);
        foreach (var original in originals)
            original.CollectRetained(failures); // A handled/success-returning body is not no-fault proof.
        lock (_gate) foreach (var error in _admissionFailures) Add(failures, error);
        // Every original is terminal before any owned-control destruction. A missing
        // generated-manager close must refuse in cleanup rather than destroy unknown work.
        Task? cleanup = null;
        try { RunPhysicalCloseCallback(() => cleanup = _cleanup()); } catch (Exception error) { Add(failures, error); }
        if (cleanup is not null) await Join(cleanup, failures);
        try { _lifetime.Dispose(); } catch (Exception error) { Add(failures, error); }
        Throw(failures, "Original chat tasks or independent stop/cleanup failed.");
        }
        finally { _executingClose.Value = previousClose; }
    }

    private static async Task Join(Task actual, List<Exception> failures)
    { try { await actual; } catch (Exception error) { Capture(failures, actual, error); } }
    private static void Capture(List<Exception> failures, Task? actual, Exception caught)
    {
        if (actual?.Exception is { InnerExceptions.Count: > 0 } group)
            foreach (var error in group.InnerExceptions) Add(failures, error);
        else Add(failures, caught); // Canceled originals retain the actual caught observation.
    }
    private static void Add(List<Exception> failures, Exception error)
    { if (!failures.Any(item => ReferenceEquals(item, error))) failures.Add(error); }
    private static void Throw(List<Exception> failures, string message)
    {
        if (failures.Count == 0) return;
        if (failures.Count > 1 || failures[0] is OperationCanceledException)
            throw new AggregateException(message, failures); // Unknown OCE/empty aggregate is not waived.
        ExceptionDispatchInfo.Capture(failures[0]).Throw();
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private bool IsAcknowledgedOriginalSpeechSource(Task actual)
    {
        lock (_gate) return actual.IsCanceled && _originalSpeechSources.TryGetValue(actual, out var issued)
            && issued?.IsAcknowledgedFor(actual) == true;
    }

    internal sealed class Original(DesktopOriginalWorkLifetime owner, CancellationToken token, Original? parent)
    {
        private readonly object _gate = new();
        private readonly HashSet<Task> _stages = [];
        private readonly List<Exception> _failures = [];
        private readonly List<(Task Source, Exception Cause)> _originalSpeechCancellations = [];
        private readonly HashSet<Task> _joinedStages = [];
        private Exception? _terminalGroup;
        private Func<bool>? _publicationGuard;
        private volatile bool _hasRetainedFailure;
        private int _pendingStageCount;
        internal Original? Parent { get; } = parent;
        internal bool IsOwnedBy(DesktopOriginalWorkLifetime actualOwner) => ReferenceEquals(owner, actualOwner);
        internal Task Task { get; set; } = null!;
        internal Task? Body { get; set; }
        internal CancellationToken Token { get; } = token;
        internal bool AllowsSourceContextTransition { get; set; } = true;
        internal bool HasLiveWork => !Task.IsCompleted || Volatile.Read(ref _pendingStageCount) != 0;
        internal bool CanPruneSuccessful => Task.IsCompletedSuccessfully && !_hasRetainedFailure && Volatile.Read(ref _pendingStageCount) == 0;
        internal bool IsPublicationCurrent
        {
            get
            {
                if (owner.IsRetiring || Token.IsCancellationRequested) return false;
                var current = _publicationGuard?.Invoke() ?? true;
                return current && !owner.IsRetiring && !Token.IsCancellationRequested;
            }
        }
        internal Task[] UnjoinedStages { get { lock (_gate) return _stages.Where(task => !_joinedStages.Contains(task)).ToArray(); } }
        internal void BindPublicationGuard(Func<bool> originalGuard) => _publicationGuard = originalGuard;

        /// <summary>Run a queued publication under this SAME live admitted original, without admitting new work.</summary>
        internal void RunAcceptedPublicationCallback(Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            _ = RunAcceptedPublicationScope(callback);
        }
        internal bool IsAcceptedPublicationCurrent => RunAcceptedPublicationScope(null);
        private bool RunAcceptedPublicationScope(Action? callback)
        {
            var current = false;
            var preceding = owner._executing.Value;
            owner._executing.Value = this;
            try
            {
                // Use the maintained physical stack before any source guard can restore its ExecutionContext.
                owner.RunPhysicalCloseCallback(() =>
                {
                    bool live;
                    lock (owner._gate)
                        live = owner._originals.Any(actual => ReferenceEquals(actual, this)) && HasLiveWork;
                    if (!live)
                    {
                        var refusal = new InvalidOperationException("A terminal or foreign original cannot publish an accepted callback.");
                        Retain(refusal);
                        lock (owner._gate) Add(owner._admissionFailures, refusal);
                        owner.RequestRetirement(); // Seal admission and retain the actual protocol fault in cached close.
                        throw refusal;
                    }
                    try
                    {
                        if (!IsPublicationCurrent) return; // Normal stop/replacement settles this accepted callback with no effect.
                        callback?.Invoke();
                        current = IsPublicationCurrent; // Recheck source getters; normal self-retirement is not a new failure.
                    }
                    catch (Exception cause) { Retain(cause); throw; }
                });
            }
            finally { owner._executing.Value = preceding; }
            return current;
        }

        internal void DemandPublication()
        {
            Token.ThrowIfCancellationRequested();
            if (owner.IsRetiring) throw new ObjectDisposedException("NewChatOriginalWorkLifetime");
            if (!(_publicationGuard?.Invoke() ?? true)) throw new InvalidOperationException("The original conversation presentation was replaced.");
            Token.ThrowIfCancellationRequested();
            if (owner.IsRetiring) throw new ObjectDisposedException("NewChatOriginalWorkLifetime");
        }
        internal void Retain(Exception error) { lock (_gate) { _hasRetainedFailure = true; RetainCause(_failures, error); } }
        private void RetainCause(List<Exception> destination, Exception error)
        {
            Exception[]? owned;
            lock (owner._gate) owner._ownedGroups.TryGetValue(error, out owned);
            if (owned is not null) foreach (var direct in owned) Add(destination, direct);
            else Add(destination, error); // No foreign aggregate flattening or type-based waiver.
        }
        internal void Capture(Task? actual, Exception caught)
        {
            lock (_gate)
            {
                _hasRetainedFailure = true;
                // Only a source privately issued by the actual speech controller
                // can associate this exact caught observation with expected stop.
                if (actual?.IsCanceled == true)
                    lock (owner._gate)
                        if (owner._originalSpeechSources.TryGetValue(actual, out var issued) && issued?.IsIssuedFor(actual) == true)
                        {
                            if (!_originalSpeechCancellations.Any(observation => ReferenceEquals(observation.Source, actual)
                                && ReferenceEquals(observation.Cause, caught)))
                                _originalSpeechCancellations.Add((actual, caught));
                            return;
                        }
                // Every other occurrence remains a failure, including a faulted
                // raw task reusing the very same canceled-playback exception object.
                if (actual?.Exception is { InnerExceptions.Count: > 0 } group)
                    foreach (var error in group.InnerExceptions) RetainCause(_failures, error);
                else RetainCause(_failures, caught);
            }
        }
        internal void QualifyOriginalSpeechSource(Task actual, NotesReadAloudController.OriginalSpeechSource issued)
        {
            ArgumentNullException.ThrowIfNull(actual);
            ArgumentNullException.ThrowIfNull(issued);
            if (!issued.IsIssuedFor(actual)) throw new InvalidOperationException("The actual local speech source was not issued by its controller.");
            lock (owner._gate)
            {
                foreach (var terminal in owner._originalSpeechSources.Keys.Where(task => task.IsCompleted && !task.IsCanceled).ToArray())
                    owner._originalSpeechSources.Remove(terminal);
                if (owner._originalSpeechSources.TryGetValue(actual, out var existing))
                {
                    if (!ReferenceEquals(existing, issued)) owner._originalSpeechSources[actual] = null;
                    // A raw task reused by distinct playback acquisitions has
                    // ambiguous provenance and cannot be qualified by either stop.
                }
                else owner._originalSpeechSources.Add(actual, issued);
            }
        }
        private void Enrol(Task actual)
        {
            ArgumentNullException.ThrowIfNull(actual);
            lock (_gate)
            {
                foreach (var successful in _stages.Where(task => task.IsCompletedSuccessfully && _joinedStages.Contains(task)).ToArray())
                { _stages.Remove(successful); _joinedStages.Remove(successful); }
                if (_stages.Add(actual)) Interlocked.Increment(ref _pendingStageCount);
                // Never refuse AFTER a raw original has already been acquired.
            }
        }
        internal async Task AwaitAsync(Task actual)
        {
            Enrol(actual);
            try { await actual.ConfigureAwait(false); }
            catch (Exception error) { Capture(actual, error); throw; }
            finally { lock (_gate) if (_joinedStages.Add(actual)) Interlocked.Decrement(ref _pendingStageCount); }
        }
        internal async Task<T> AwaitAsync<T>(Task<T> actual)
        {
            Enrol(actual);
            try { return await actual.ConfigureAwait(false); }
            catch (Exception error) { Capture(actual, error); throw; }
            finally { lock (_gate) if (_joinedStages.Add(actual)) Interlocked.Decrement(ref _pendingStageCount); }
        }
        internal async Task ReadStreamAsync<T>(IAsyncEnumerable<T> source, Func<T, Task> publish)
        {
            IAsyncEnumerator<T>? iterator = null;
            Exception? primary = null;
            try
            {
                DemandPublication();
                iterator = source.GetAsyncEnumerator(Token);
                while (await AwaitAsync(iterator.MoveNextAsync().AsTask()).ConfigureAwait(false)) // Each SAME ValueTask once.
                {
                    DemandPublication();
                    await AwaitAsync(publish(iterator.Current)).ConfigureAwait(false);
                }
            }
            catch (Exception error) { primary = error; Retain(error); }
            finally
            {
                if (iterator is not null)
                    try { await AwaitAsync(iterator.DisposeAsync().AsTask()).ConfigureAwait(false); }
                    catch (Exception error) { primary ??= error; Retain(error); }
            }
            // Preserve existing body-policy dispatch on the actual primary object; the
            // original record independently retains every phase/cleanup sibling.
            if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
        }
        internal void ThrowRetained()
        {
            lock (_gate)
            {
                if (_failures.Count == 0) return;
                if (_failures.Count > 1 || _failures[0] is OperationCanceledException)
                {
                    var group = new AggregateException("Original chat operation causes remain retained.", _failures);
                    _terminalGroup = group; // Only this privately produced wrapper can be reduced at close.
                    lock (owner._gate) owner._ownedGroups.Add(group, _failures.ToArray());
                    throw group;
                }
                ExceptionDispatchInfo.Capture(_failures[0]).Throw();
            }
        }
        internal void CollectStageTerminal(List<Exception> destination, Task actual, Exception caught)
        {
            lock (_gate)
            {
                if (owner.IsAcknowledgedOriginalSpeechSource(actual)) return;
                if (actual.Exception is { InnerExceptions.Count: > 0 } group)
                    foreach (var error in group.InnerExceptions) RetainCause(destination, error);
                else RetainCause(destination, caught);
            }
        }
        internal void CollectTerminal(List<Exception> destination, Exception caught)
        {
            lock (_gate)
            {
                foreach (var error in _failures) RetainCause(destination, error);
                if (!ReferenceEquals(caught, _terminalGroup)) CollectStageTerminal(destination, Task, caught);
            }
        }
        internal void CollectRetained(List<Exception> destination)
        {
            lock (_gate)
            {
                foreach (var error in _failures) RetainCause(destination, error);
                foreach (var observation in _originalSpeechCancellations)
                    if (!owner.IsAcknowledgedOriginalSpeechSource(observation.Source)) Add(destination, observation.Cause);
            }
        }
    }
}
