using System.Runtime.ExceptionServices;

namespace Haven.Application;

/// <summary>The actual finite coordinator source driver and its raw stages; no Task/run or replay authority.</summary>
internal sealed class TaskRunProcessStageCustody(TaskExecutionCoordinator owner, string name, CancellationToken callerToken)
{
    private sealed class Live(TaskRunProcessStageCustody original, Live? parent)
    {
        internal readonly TaskRunProcessStageCustody Original = original;
        internal readonly Live? Parent = parent;
        internal volatile bool Active = true;
    }
    private static readonly AsyncLocal<Live?> Current = new();
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
    private readonly List<Task> _sources = [];
    private readonly List<Exception> _causes = [];
    private readonly Dictionary<Exception, Exception[]> _ownEnvelopes = new(ReferenceEqualityComparer.Instance);
    private bool _fault;
    private bool _lifetimeClosed;
    private bool _sealed;
    private int _callbackAdmissions;
    private Task? _stop;
    private Task? _close;
    internal Task? ActualDriver;
    internal Task? ActualBody;
    internal Func<bool>? OriginalResultClosed;
    internal Func<Task>? OriginalResultJoin;
    internal Haven.Core.TaskExecutionSnapshot? AcknowledgedBegin;
    internal volatile bool BeginOwnershipTransferred;
    internal readonly string Name = name;
    internal TaskExecutionCoordinator Owner => owner;
    internal IReadOnlyList<Task> ActualSources { get { lock (_gate) return _sources.ToArray(); } }
    internal bool HealthyClosed
    {
        get
        {
            bool wholeSourcesClosed;
            Func<bool>? resultClosed;
            lock (_gate)
            {
                wholeSourcesClosed = ActualDriver is { IsCompletedSuccessfully: true } && _lifetimeClosed
                    && (_stop is null || _stop.IsCompletedSuccessfully) && _sources.All(actual => actual.IsCompletedSuccessfully)
                    && _causes.Count == 0;
                resultClosed = OriginalResultClosed;
            }
            return wholeSourcesClosed && (resultClosed is null || resultClosed());
        }
    }
    internal static TaskRunProcessStageCustody? CurrentFor(TaskExecutionCoordinator owner)
    {
        for (var current = Current.Value; current is not null; current = current.Parent)
            if (current.Active && ReferenceEquals(current.Original.Owner, owner)) return current.Original;
        return null;
    }
    internal Task<T> Publish<T>(Task start, Func<CancellationToken, Task<T>> body)
    {
        lock (_gate)
        {
            if (ActualDriver is not null) throw new InvalidOperationException("The actual coordinator stage is already published.");
            var actual = RunOriginalAsync(start, body);
            ActualDriver = actual;
            return actual;
        }
    }
    private async Task<T> RunOriginalAsync<T>(Task start, Func<CancellationToken, Task<T>> body)
    {
        await start.ConfigureAwait(false);
        var previous = Current.Value;
        var current = new Live(this, previous);
        Current.Value = current;
        using var context = TaskRunProcessProducerContext.EnterAsync(owner);
        T result = default!;
        var acquisitionInvoked = false;
        try
        {
            _lifetime.Token.ThrowIfCancellationRequested(); // Genuine withdrawal before any source callback.
            acquisitionInvoked = true;
            var actualBody = Invoke(() => body(_lifetime.Token));
            ActualBody = actualBody;
            RetainSource(actualBody);
            result = await actualBody.ConfigureAwait(false);
        }
        catch (OperationCanceledException withdrawal) when (!acquisitionInvoked)
        {
            lock (_gate) _causes.Add(withdrawal); // No synchronous callback or faulted original existed.
        }
        catch (Exception failure) { Retain(failure, ActualBody); }
        finally
        {
            Task? stop;
            lock (_gate) { _lifetimeClosed = true; stop = _stop; }
            if (stop is not null)
                try { await stop.ConfigureAwait(false); } catch (Exception cause) { Retain(cause, stop); }
            try { Invoke(() => { _lifetime.Dispose(); return true; }, owningCleanup: true); }
            catch (Exception cause) { Retain(cause); }
            current.Active = false;
            Current.Value = previous;
        }
        ThrowRetained();
        return result;
    }
    internal T Invoke<T>(Func<T> callback, bool owningCleanup = false)
    {
        try
        {
            lock (_gate)
            {
                if (!owningCleanup && (_sealed || owner.HasSealedOriginalProcessProducerAdmission))
                    throw new InvalidOperationException("The actual coordinator source stage admission is sealed.");
                if (!owningCleanup && _callbackAdmissions >= 512)
                    throw new InvalidOperationException("Finite original coordinator callback admission is exhausted.");
                if (!owningCleanup) _callbackAdmissions++;
            }
            return TaskRunProcessProducerContext.Invoke(owner, callback);
        }
        catch (Exception failure) { Retain(failure); ThrowRetained(); throw; }
    }
    internal void RetainSource(Task actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        lock (_gate)
        {
            if (_sources.Any(prior => ReferenceEquals(prior, actual))) return;
            _sources.Add(actual);
        }
    }
    internal async Task<T> Await<T>(Func<Task<T>> source, bool owningCleanup = false)
    {
        if (!owningCleanup) _lifetime.Token.ThrowIfCancellationRequested();
        Task<T>? actual = null;
        try
        {
            actual = Invoke(source, owningCleanup) ?? throw new InvalidOperationException("No actual coordinator source Task was returned.");
            RetainSource(actual);
            return await actual.ConfigureAwait(false);
        }
        catch (Exception cause) { Retain(cause, actual); ThrowRetained(); throw; }
    }
    internal async Task Await(Func<Task> source, bool owningCleanup = false)
    {
        if (!owningCleanup) _lifetime.Token.ThrowIfCancellationRequested();
        Task? actual = null;
        try
        {
            actual = Invoke(source, owningCleanup) ?? throw new InvalidOperationException("No actual coordinator source Task was returned.");
            RetainSource(actual);
            await actual.ConfigureAwait(false);
        }
        catch (Exception cause) { Retain(cause, actual); ThrowRetained(); throw; }
    }
    internal void Seal() { lock (_gate) _sealed = true; }
    internal void RequestStop()
    {
        TaskCompletionSource? start = null;
        lock (_gate)
        {
            _sealed = true;
            if (!_lifetimeClosed && _stop is null)
            {
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _stop = StopOriginalAsync(start.Task);
            }
        }
        start?.SetResult();
    }
    private async Task StopOriginalAsync(Task start)
    {
        await start.ConfigureAwait(false);
        try { Invoke(() => { _lifetime.Cancel(); return true; }, owningCleanup: true); }
        catch (Exception cause) { Retain(cause); ThrowRetained(); throw; }
    }
    internal Task CloseOriginalAsync()
    {
        TaskRunProcessProducerContext.DemandExternalJoin(owner);
        RequestStop();
        TaskCompletionSource? start = null;
        Task actual;
        lock (_gate)
        {
            if (_close is null)
            {
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _close = JoinOriginalAsync(start.Task);
            }
            actual = _close;
        }
        start?.SetResult();
        return actual;
    }
    private async Task JoinOriginalAsync(Task start)
    {
        await start.ConfigureAwait(false);
        var previous = Current.Value;
        var current = new Live(this, previous);
        Current.Value = current;
        using var context = TaskRunProcessProducerContext.EnterAsync(owner);
        try
        {
        var driver = ActualDriver ?? throw new InvalidOperationException("The original coordinator driver was not published.");
        try { await driver.ConfigureAwait(false); } catch (Exception cause) { Retain(cause, driver); }
        Task[] sources; Task? stop;
        lock (_gate) { sources = _sources.ToArray(); stop = _stop; }
        // Every admitted original is joined independently; a body fault cannot mask a late source.
        foreach (var actual in sources)
            try { await actual.ConfigureAwait(false); } catch (Exception cause) { Retain(cause, actual); }
        if (stop is not null)
            try { await stop.ConfigureAwait(false); } catch (Exception cause) { Retain(cause, stop); }
        if (OriginalResultJoin is { } joinResult)
        {
            Task? actualJoin = null;
            try
            {
                actualJoin = Invoke(joinResult, owningCleanup: true);
                RetainSource(actualJoin);
                await actualJoin.ConfigureAwait(false);
            }
            catch (Exception cause) { Retain(cause, actualJoin); }
        }
        ThrowRetained();
        if (!HealthyClosed) throw new InvalidOperationException("The actual coordinator preparation has no fully closed original result.");
        }
        finally
        {
            current.Active = false;
            Current.Value = previous;
        }
    }
    internal void RetainOriginalFailure(Exception cause, Task? actual = null) => Retain(cause, actual);

    private void Retain(Exception cause, Task? actual = null)
    {
        lock (_gate)
        {
            if (actual is null || actual.IsFaulted) _fault = true;
            IEnumerable<Exception> errors = actual?.Exception is { } group ? group.InnerExceptions : new[] { cause };
            foreach (var error in errors)
                foreach (var original in _ownEnvelopes.TryGetValue(error, out var originalCauses) ? originalCauses : new[] { error })
                    if (!_causes.Any(prior => ReferenceEquals(prior, original))) _causes.Add(original);
        }
    }
    private void ThrowRetained()
    {
        Exception[] causes; bool fault;
        lock (_gate) { causes = _causes.ToArray(); fault = _fault; }
        if (causes.Length == 0) return;
        if (causes.Length == 1 && (causes[0] is not OperationCanceledException || !fault))
            ExceptionDispatchInfo.Capture(causes[0]).Throw();
        var envelope = new AggregateException("Actual coordinator source stages failed; all original causes remain retained.", causes);
        lock (_gate) _ownEnvelopes[envelope] = causes;
        throw envelope;
    }
}
