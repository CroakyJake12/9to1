using System.Runtime.ExceptionServices;

namespace Haven.Application;

/// <summary>Owns the actual public canonical enumerator. The selected inner iterator continues
/// owning Begin, provider frames, tracker/resources and the acknowledged terminal task CAS.</summary>
internal sealed class CanonicalChatProcessProducer : IAsyncEnumerable<ChatStreamEvent>, IAsyncEnumerator<ChatStreamEvent>
{
    private sealed class Context(CanonicalChatProcessProducer owner, Context? parent)
    {
        internal readonly CanonicalChatProcessProducer Owner = owner;
        internal readonly Context? Parent = parent;
        internal volatile bool Active = true;
    }
    private static readonly AsyncLocal<Context?> ActiveOriginal = new();
    [ThreadStatic] private static Context? SynchronousOriginal;
    private readonly object _gate = new();
    private readonly TaskExecutionCoordinator _owner;
    private readonly ICanonicalProcessEnumerableCustody _custody;
    private readonly TaskRunInvocationCustody? _originalInvocation;
    private readonly Func<CancellationToken, IAsyncEnumerable<ChatStreamEvent>> _source;
    private readonly CancellationTokenSource _lifetime;
    private readonly List<Task> _moves = [];
    private readonly List<Task> _raw = [];
    private readonly List<Exception> _causes = [];
    private readonly Dictionary<Exception, Exception[]> _ownEnvelopes = new(ReferenceEqualityComparer.Instance);
    private bool _knownFault;
    internal const int OriginalMoveCapacity = 4096;
    private Exception? _capacityFailure;
    private bool _enumerationClaimed;
    private bool _admissionSealed;
    private bool _lifetimeDisposed;
    private bool _sourceInvoked;
    private CancellationToken _enumerationToken;
    private IAsyncEnumerator<ChatStreamEvent>? _iterator;
    private ChatStreamEvent? _current;
    private Task<bool>? _lastMove;
    private Task? _stop;
    private Task? _dispose;
    private Task? _close;

    internal CanonicalChatProcessProducer(TaskExecutionCoordinator owner, TaskRunInvocationCustody custody,
        Func<CancellationToken, IAsyncEnumerable<ChatStreamEvent>> source, CancellationToken callerToken)
        : this(owner, new TaskRunInvocationProcessCustody(custody), source, callerToken) { _originalInvocation = custody; }

    internal CanonicalChatProcessProducer(TaskExecutionCoordinator owner, ICanonicalProcessEnumerableCustody custody,
        Func<CancellationToken, IAsyncEnumerable<ChatStreamEvent>> source, CancellationToken callerToken)
    {
        _owner = owner; _custody = custody; _source = source;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
    }

    internal CanonicalContinuationProcessCustody? OriginalContinuationCustody => _custody as CanonicalContinuationProcessCustody;

    internal IReadOnlyList<Task> ActualMoves { get { lock (_gate) return _moves.ToArray(); } }
    internal Task? ActualStop { get { lock (_gate) return _stop; } }
    internal Task? ActualClose { get { lock (_gate) return _close; } }
    internal Task? ActualDispose { get { lock (_gate) return _dispose; } }
    internal bool HasHealthyClosedOriginal
    {
        get
        {
            lock (_gate) return _dispose is { IsCompletedSuccessfully: true } && _lifetimeDisposed
                && (_stop is null || _stop.IsCompletedSuccessfully)
                && _moves.All(actual => actual.IsCompletedSuccessfully)
                && _raw.All(actual => actual.IsCompletedSuccessfully) && _causes.Count == 0
                && (!_sourceInvoked || _custody.HasOwnedTerminalObservation);
        }
    }

    // This observes the actual outer tasks after the private caller has obtained its
    // genuine failed-frame settlement. It does not waive a canceled task, stop failure,
    // unrelated cause or inner cleanup fault, and issues no replay permission.
    internal bool HasClosedOriginalWithExpectedProviderFailure(Task sameCall, Exception sameOutward)
    {
        if (!sameCall.IsFaulted || sameCall.Exception is not { } originalFault) return false;
        var known = new HashSet<Exception>(ReferenceEqualityComparer.Instance) { sameOutward };
        void Keep(Exception cause)
        {
            known.Add(cause);
            if (cause is AggregateException group)
                foreach (var direct in group.InnerExceptions) Keep(direct);
        }
        foreach (var direct in originalFault.InnerExceptions) Keep(direct);
        bool Known(Exception cause) => known.Contains(cause)
            || cause is AggregateException { InnerExceptions.Count: > 0 } group && group.InnerExceptions.All(Known);
        bool Terminal(Task actual) => actual.IsCompletedSuccessfully
            || actual.IsFaulted && actual.Exception is { InnerExceptions.Count: > 0 } group && group.InnerExceptions.All(Known);
        lock (_gate) return _dispose is not null && Terminal(_dispose) && _lifetimeDisposed
            && _capacityFailure is null && (_stop is null || _stop.IsCompletedSuccessfully)
            && _moves.All(Terminal) && _raw.All(Terminal) && _causes.Count > 0 && _causes.All(Known)
            && (!_sourceInvoked || _custody.HasOwnedTerminalObservation);
    }

    internal bool HasSuccessfullyResolvedOriginalToolCheckpoint
    {
        get
        {
            var receipt = _originalInvocation?.OriginalResolvedToolCheckpoint;
            if (receipt is null) return false;
            var binding = receipt.Binding;
            return ReferenceEquals(binding.Issuer, _owner) && ReferenceEquals(binding.Original, _originalInvocation)
                && ReferenceEquals(binding.Original.OriginalProcessProducer, this)
                && ReferenceEquals(binding.Resolution, receipt) && binding.Claimed && binding.Bound
                && receipt.Completion.IsCompletedSuccessfully
                && receipt.ActualBusinessDriver.IsCompletedSuccessfully && receipt.ActualResolutionValidation.IsCompletedSuccessfully
                && binding.OriginalResolutionDriver is { IsCompletedSuccessfully: true }
                && binding.Next.OriginalProcessProducer is { HasHealthyClosedOriginal: true }
                && binding.Boundary.ActualCall is { } actual && binding.Boundary.ActualOutwardFailure is { } outward
                && HasClosedOriginalWithExpectedProviderFailure(actual, outward);
        }
    }

    public ChatStreamEvent Current
    {
        get { lock (_gate) return _current ?? throw new InvalidOperationException("The original canonical Move has no current item."); }
    }

    public IAsyncEnumerator<ChatStreamEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_enumerationClaimed || _admissionSealed || _owner.HasSealedOriginalProcessProducerAdmission)
                throw new InvalidOperationException("The actual canonical producer is single-use or sealed.");
            _enumerationClaimed = true;
            _enumerationToken = cancellationToken;
            return this;
        }
    }

    public ValueTask<bool> MoveNextAsync()
    {
        TaskCompletionSource start;
        Task<bool> actual;
        lock (_gate)
        {
            if (_capacityFailure is { } capacityCause) ExceptionDispatchInfo.Capture(capacityCause).Throw();
            if (!_enumerationClaimed || _admissionSealed || _dispose is not null || _owner.HasSealedOriginalProcessProducerAdmission)
                throw new InvalidOperationException("Canonical Move admission is unavailable.");
            if (_lastMove is { IsCompleted: false })
                throw new InvalidOperationException("The actual canonical Move is already admitted.");
            if (_moves.Count >= OriginalMoveCapacity)
            {
                _capacityFailure ??= new InvalidOperationException("The finite original canonical Move custody is exhausted; partial output requires inspection.");
                _admissionSealed = true;
                Retain(_capacityFailure);
                _custody.RetainFailure(_capacityFailure);
                ExceptionDispatchInfo.Capture(_capacityFailure).Throw();
            }
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = _lastMove = MoveOriginalAsync(start.Task);
            _moves.Add(actual);
            _custody.RetainSource("process.outer-move", actual);
        }
        start.SetResult();
        return new(actual);
    }

    private async Task<bool> MoveOriginalAsync(Task start)
    {
        await start.ConfigureAwait(false);
        var previous = ActiveOriginal.Value;
        var context = new Context(this, previous);
        ActiveOriginal.Value = context;
        Task<bool>? raw = null;
        try
        {
            if (_owner.HasSealedOriginalProcessProducerAdmission)
                throw new InvalidOperationException("Process admission sealed before the original canonical callback began.");
            if (_iterator is null)
            {
                var body = InvokeAdmittedOriginal(() =>
                {
                    _sourceInvoked = true;
                    return _source(_lifetime.Token)
                        ?? throw new InvalidOperationException("No original canonical source was returned.");
                });
                _iterator = InvokeAdmittedOriginal(() => body.GetAsyncEnumerator(_enumerationToken))
                    ?? throw new InvalidOperationException("No actual canonical iterator was returned.");
            }
            var iterator = _iterator ?? throw new InvalidOperationException("The actual canonical iterator is missing.");
            raw = InvokeAdmittedOriginal(() => iterator.MoveNextAsync().AsTask());
            RetainRaw("process.outer-raw-move", raw);
            var hasItem = await raw.ConfigureAwait(false);
            if (hasItem)
            {
                var current = InvokeOriginal(() => iterator.Current);
                lock (_gate) _current = current; // Stable even if external disposal starts before the reader continuation.
            }
            return hasItem;
        }
        catch (Exception cause)
        {
            lock (_gate) _admissionSealed = true;
            Retain(cause, raw); ThrowRetained(); throw;
        }
        finally { context.Active = false; ActiveOriginal.Value = previous; }
    }

    internal static void DemandExternalOwnerJoin(TaskExecutionCoordinator owner)
    {
        void Check(Context? context)
        {
            for (; context is not null; context = context.Parent)
                if (context.Active && ReferenceEquals(context.Owner._owner, owner))
                    throw new InvalidOperationException("An original canonical callback cannot join its owning process drain.");
        }
        Check(ActiveOriginal.Value); Check(SynchronousOriginal);
    }

    internal void DemandExternalOriginalJoin()
    {
        void Check(Context? context)
        {
            for (; context is not null; context = context.Parent)
                if (context.Active && ReferenceEquals(context.Owner, this))
                    throw new InvalidOperationException("An original canonical callback cannot join its own Move/Dispose.");
        }
        Check(ActiveOriginal.Value); Check(SynchronousOriginal);
        _custody.DemandExternalJoin();
    }

    internal void SealOriginalProcessAdmission()
    {
        lock (_gate) { _admissionSealed = true; _custody.MarkRetirementRequested(); }
    }

    internal void InvokeOriginalCallback(Action body) => InvokeOriginal(() => { body(); return true; });

    internal void InvokeAdmittedOriginalCallback(Action body) => InvokeAdmittedOriginal(() => { body(); return true; });

    private T InvokeAdmittedOriginal<T>(Func<T> body)
    {
        // This is a finite callback admission, distinct from the already published Move driver.
        // No callback runs under either metadata lock; an acquisition admitted before seal may
        // return late and is still joined by its original Move/Dispose owner.
        lock (_gate)
            if (_admissionSealed || _owner.HasSealedOriginalProcessProducerAdmission)
                throw new InvalidOperationException("Canonical source callback admission sealed before this stage began.");
        return InvokeOriginal(body);
    }

    private T InvokeOriginal<T>(Func<T> body)
    {
        var previous = SynchronousOriginal;
        var context = new Context(this, previous);
        SynchronousOriginal = context;
        try { return body(); }
        finally { context.Active = false; SynchronousOriginal = previous; }
    }

    internal void RequestOriginalProcessRetirement() => RequestOriginalWithdrawal(processRetirement: true);
    internal void RequestOriginalRunWithdrawal() => RequestOriginalWithdrawal(processRetirement: false);

    private void RequestOriginalWithdrawal(bool processRetirement)
    {
        TaskCompletionSource? stopStart = null;
        TaskCompletionSource? disposeStart = null;
        lock (_gate)
        {
            _admissionSealed = true;
            if (processRetirement) _custody.MarkRetirementRequested();
            if (!_lifetimeDisposed && _stop is null)
            {
                stopStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _stop = StopOriginalAsync(stopStart.Task);
                _custody.RetainSource("process.outer-stop", _stop);
            }
            if (_dispose is null)
            {
                disposeStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _dispose = DisposeOriginalAsync(disposeStart.Task);
                _custody.RetainSource("process.outer-dispose", _dispose);
            }
        }
        stopStart?.SetResult(); disposeStart?.SetResult();
    }

    private async Task StopOriginalAsync(Task start)
    {
        await start.ConfigureAwait(false);
        try { InvokeOriginal(() => { _lifetime.Cancel(); return true; }); }
        catch (Exception cause) { Retain(cause); ThrowRetained(); throw; }
    }

    public ValueTask DisposeAsync()
    {
        DemandExternalOriginalJoin();
        TaskCompletionSource? start = null;
        Task actual;
        lock (_gate)
        {
            _admissionSealed = true;
            if (_dispose is null)
            {
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _dispose = DisposeOriginalAsync(start.Task);
                _custody.RetainSource("process.outer-dispose", _dispose);
            }
            actual = _dispose;
        }
        start?.SetResult();
        return new(actual);
    }

    private async Task DisposeOriginalAsync(Task start)
    {
        await start.ConfigureAwait(false);
        var previous = ActiveOriginal.Value;
        var context = new Context(this, previous);
        ActiveOriginal.Value = context;
        try
        {
            Task[] moves;
            lock (_gate) moves = _moves.ToArray();
            foreach (var move in moves)
                try { await move.ConfigureAwait(false); } catch (Exception cause) { Retain(cause, move); }
            if (_iterator is { } iterator)
            {
                Task? actual = null;
                try
                {
                    actual = InvokeOriginal(() => iterator.DisposeAsync().AsTask());
                    RetainRaw("process.outer-raw-dispose", actual);
                    await actual.ConfigureAwait(false);
                }
                catch (Exception cause) { Retain(cause, actual); }
            }
            Task? stop;
            // Close further cancellation admission atomically with CTS disposal. Already published
            // cancellation is independently joined even if Move/inner Dispose failed.
            lock (_gate) { _lifetimeDisposed = true; stop = _stop; }
            if (stop is not null)
                try { await stop.ConfigureAwait(false); } catch (Exception cause) { Retain(cause, stop); }
            try { InvokeOriginal(() => { _lifetime.Dispose(); return true; }); }
            catch (Exception cause) { Retain(cause); }
            ThrowRetained();
        }
        finally { context.Active = false; ActiveOriginal.Value = previous; }
    }

    internal Task CloseAndSuspendOriginalProducerAsync() => CloseOriginalWithdrawal(processRetirement: true);
    internal Task CloseAndSuspendOriginalRunProducerAsync() => CloseOriginalWithdrawal(processRetirement: false);

    private Task CloseOriginalWithdrawal(bool processRetirement)
    {
        DemandExternalOriginalJoin();
        RequestOriginalWithdrawal(processRetirement);
        TaskCompletionSource? start = null;
        Task actual;
        lock (_gate)
        {
            if (_close is null)
            {
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _close = CloseOriginalAsync(start.Task);
                _custody.RetainSource("process.outer-close", _close);
            }
            actual = _close;
        }
        start?.SetResult();
        return actual;
    }

    private async Task CloseOriginalAsync(Task start)
    {
        await start.ConfigureAwait(false);
        Task? stop; Task dispose;
        lock (_gate) { stop = _stop; dispose = _dispose ?? throw new InvalidOperationException("No actual original Dispose was published."); }
        if (stop is not null)
            try { await stop.ConfigureAwait(false); } catch (Exception cause) { Retain(cause, stop); }
        try { await dispose.ConfigureAwait(false); } catch (Exception cause) { Retain(cause, dispose); }
        ThrowRetained();
        if (!HasHealthyClosedOriginal)
            throw new InvalidOperationException("The actual canonical producer has no healthy whole cleanup and terminal acknowledgment.");
    }

    private void RetainRaw(string stage, Task actual)
    {
        lock (_gate) _raw.Add(actual);
        _custody.RetainSource(stage, actual);
    }

    private void Retain(Exception cause, Task? actual = null)
    {
        lock (_gate)
        {
            if (actual is null || actual.IsFaulted) _knownFault = true;
            IEnumerable<Exception> direct = actual?.Exception is { } faults ? faults.InnerExceptions : new[] { cause };
            foreach (var error in direct)
                foreach (var original in _ownEnvelopes.TryGetValue(error, out var prior) ? prior : new[] { error })
                    if (!_causes.Any(existing => ReferenceEquals(existing, original))) _causes.Add(original);
        }
    }

    private void ThrowRetained()
    {
        Exception[] causes; bool fault;
        lock (_gate) { causes = _causes.ToArray(); fault = _knownFault; }
        if (causes.Length == 0) return;
        if (causes.Length == 1 && (causes[0] is not OperationCanceledException || !fault))
            ExceptionDispatchInfo.Capture(causes[0]).Throw();
        var envelope = new AggregateException("Actual canonical Move, stop or cleanup failed.", causes);
        lock (_gate) _ownEnvelopes[envelope] = causes;
        throw envelope;
    }
}
