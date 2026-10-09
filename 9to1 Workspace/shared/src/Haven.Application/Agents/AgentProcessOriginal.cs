namespace Haven.Application;

/// <summary>The SAME public runtime operation, including pre-context discovery and actual lifetime
/// close. Its row/status and a detached presentation wait cannot replace this custody.</summary>
internal sealed class AgentProcessOriginal(AgentTaskRuntimeService owner, AgentRuntimeOriginalCustody original,
    CancellationToken? callerToken)
{
    private readonly object _gate = new();
    internal readonly AgentRuntimeOriginalCustody Original = original;
    internal readonly CancellationTokenSource? Lifetime = callerToken is { } token
        ? CancellationTokenSource.CreateLinkedTokenSource(token) : null;
    internal Task? ActualDriver;
    private Task? _stop;
    private Task? _lifetimeClose;
    private Task? _wholeClose;
    private bool _cancellationSealed;

    internal bool HealthyClosed => Original.Healthy && _lifetimeClose is { IsCompletedSuccessfully: true }
        && (_stop is null || _stop.IsCompletedSuccessfully) && HasHealthyCanonicalOutcome
        && !owner.HasOriginalProcessObserverFaults(Original.OriginalCanonicalAgent);

    private bool HasHealthyCanonicalOutcome
    {
        get
        {
            var canonical = Original.OriginalCanonicalAgent;
            if (canonical is null) return true; // This SAME operation never acquired a task context.
            return canonical.Active == 0 && canonical.LifetimeDisposed && canonical.Causes.Count == 0
                && (canonical.OriginalCancellation is null || canonical.OriginalCancellation.IsCompletedSuccessfully)
                && canonical.ActualMoves.All(actual => actual.IsCompletedSuccessfully)
                && canonical.ActualDisposals.All(actual => actual.IsCompletedSuccessfully)
                && canonical.ActualPersistence.All(actual => actual.IsCompletedSuccessfully)
                && canonical.ActualHistoryOperations.All(actual => actual.Healthy)
                && canonical.Chat is { } chat && chat.Original.OwnedCleanupTerminal
                && chat.Original.OriginalTerminalObservation is not null;
        }
    }

    internal void RequestOriginalProcessRetirement()
    {
        TaskCompletionSource? start = null;
        lock (_gate)
        {
            if (_cancellationSealed || Lifetime is null || _stop is not null) return;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _stop = StopPublishedOriginalAsync(start.Task);
            Original.RetainSource(_stop);
        }
        start.SetResult();
    }

    private async Task StopPublishedOriginalAsync(Task start)
    {
        await start.ConfigureAwait(false);
        try { TaskRunProcessProducerContext.Invoke(owner, () => { Lifetime!.Cancel(); return true; }); }
        catch (OperationCanceledException synchronousFault)
        { throw new AggregateException("The actual Agent process cancellation callback faulted.", synchronousFault); }
    }

    internal Task CloseOriginalLifetimeAsync()
    {
        TaskCompletionSource? start = null;
        Task actual;
        lock (_gate)
        {
            _cancellationSealed = true;
            if (_lifetimeClose is null)
            {
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _lifetimeClose = ClosePublishedLifetimeAsync(start.Task);
            }
            actual = _lifetimeClose;
        }
        start?.SetResult();
        return actual;
    }

    private async Task ClosePublishedLifetimeAsync(Task start)
    {
        await start.ConfigureAwait(false);
        var causes = new List<Exception>();
        Task? stop;
        lock (_gate) stop = _stop;
        if (stop is not null)
            try { await stop.ConfigureAwait(false); }
            catch (Exception cause)
            { causes.AddRange(stop.Exception is { } faults ? faults.InnerExceptions : new[] { cause }); }
        try { TaskRunProcessProducerContext.Invoke(owner, () => { Lifetime?.Dispose(); return true; }); }
        catch (Exception cause) { causes.Add(cause); }
        if (causes.Count > 0) throw new AggregateException("Actual Agent process cancellation/close failed.", causes);
    }

    internal Task CloseWholeOriginalAsync()
    {
        TaskRunProcessProducerContext.DemandExternalJoin(owner);
        TaskCompletionSource? start = null;
        Task actual;
        lock (_gate)
        {
            if (_wholeClose is null)
            {
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _wholeClose = ClosePublishedWholeOriginalAsync(start.Task);
            }
            actual = _wholeClose;
        }
        start?.SetResult();
        return actual;
    }

    private async Task ClosePublishedWholeOriginalAsync(Task start)
    {
        await start.ConfigureAwait(false);
        var causes = new List<Exception>();
        var actual = ActualDriver ?? throw new InvalidOperationException("No SAME original Agent driver was published.");
        try { await actual.ConfigureAwait(false); }
        catch (Exception cause) { causes.AddRange(actual.Exception is { } faults ? faults.InnerExceptions : new[] { cause }); }
        var close = CloseOriginalLifetimeAsync();
        try { await close.ConfigureAwait(false); }
        catch (Exception cause) { causes.AddRange(close.Exception is { } faults ? faults.InnerExceptions : new[] { cause }); }
        if (Original.OriginalCanonicalAgent is { } canonical)
            foreach (var originalCause in canonical.Causes)
                if (!causes.Any(prior => ReferenceEquals(prior, originalCause))) causes.Add(originalCause);
        if (causes.Count > 0) throw new AggregateException("The actual Agent source and business cleanup failed.", causes);
        if (!HealthyClosed)
            throw new InvalidOperationException("No successful original Agent producer, cleanup and terminal task acknowledgment exists.");
    }
}
