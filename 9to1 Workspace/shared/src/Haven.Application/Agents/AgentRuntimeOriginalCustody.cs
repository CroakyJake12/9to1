using System.Runtime.ExceptionServices;

namespace Haven.Application;

/// <summary>The exact returned Agent operation and its real source tasks. No durable Task, permission or retry authority.</summary>
internal sealed class AgentRuntimeOriginalCustody
{
    private readonly object _gate = new();
    private readonly List<Task> _sources = [];
    private readonly List<Exception> _causes = [];
    private readonly Dictionary<Exception, Exception[]> _ownEnvelopes = new(ReferenceEqualityComparer.Instance);
    private bool _knownFault;
    internal Task? OriginalDriver;
    internal Task? OriginalBody;
    internal object? OriginalProcessOwner;
    internal AgentCanonicalOriginal? OriginalCanonicalAgent;
    internal AgentProcessOriginal? OriginalProcessProducer;
    internal Func<Task>? OriginalProcessCleanup;
    internal bool OriginalAcknowledgedHistoryStage;
    internal bool OriginalObservationDrainStage;
    private void DemandOriginalCallbackAdmission(bool owningCleanup)
    {
        if (!owningCleanup && !OriginalAcknowledgedHistoryStage && !OriginalObservationDrainStage && OriginalProcessOwner is AgentTaskRuntimeService source)
            source.DemandOriginalProcessSourceAdmission();
    }
    internal void RetainSource(Task actual) { lock (_gate) _sources.Add(actual); }
    internal IReadOnlyList<Task> Sources { get { lock (_gate) return _sources.ToArray(); } }
    internal IReadOnlyList<Exception> Causes { get { lock (_gate) return _causes.ToArray(); } }
    internal bool Healthy => OriginalDriver is { IsCompletedSuccessfully: true }
        && OriginalBody is { IsCompletedSuccessfully: true }
        && Sources.All(source => source.IsCompletedSuccessfully) && Causes.Count == 0;

    internal Task<T> Start<T>(Func<AgentRuntimeOriginalCustody, Task<T>> body, Task? admissionStart = null)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actual = RunAsync(start.Task, admissionStart, body);
        lock (_gate)
        {
            if (OriginalDriver is not null) throw new InvalidOperationException("The original Agent operation is already published.");
            OriginalDriver = actual;
        }
        start.SetResult();
        return actual;
    }

    private async Task<T> RunAsync<T>(Task start, Task? admissionStart, Func<AgentRuntimeOriginalCustody, Task<T>> body)
    {
        await start.ConfigureAwait(false);
        if (admissionStart is not null) await admissionStart.ConfigureAwait(false);
        using var context = OriginalProcessOwner is { } owner ? TaskRunProcessProducerContext.EnterAsync(owner) : null;
        T result = default!;
        try
        {
            DemandOriginalCallbackAdmission(owningCleanup: false);
            var actualBody = TaskRunProcessProducerContext.Invoke(OriginalProcessOwner, () => body(this))
                ?? throw new InvalidOperationException("The Agent operation returned no original body Task.");
            OriginalBody = actualBody;
            result = await actualBody.ConfigureAwait(false);
        }
        catch (Exception actualFailure) { Capture(OriginalBody, actualFailure); }
        // Independently join actual process lifetime cleanup after the real body; preserve all originals.
        if (OriginalProcessCleanup is { } close)
        {
            Task? actualClose = null;
            try
            {
                actualClose = TaskRunProcessProducerContext.Invoke(OriginalProcessOwner, close)
                    ?? throw new InvalidOperationException("No original Agent process lifetime close was returned.");
                RetainSource(actualClose);
                await actualClose.ConfigureAwait(false);
            }
            catch (Exception closeFailure) { Capture(actualClose, closeFailure); }
        }
        ThrowRetained();
        return result;
    }

    internal T Invoke<T>(Func<T> source, bool owningCleanup = false)
    {
        try { DemandOriginalCallbackAdmission(owningCleanup); return TaskRunProcessProducerContext.Invoke(OriginalProcessOwner, source); }
        catch (Exception actualFailure)
        {
            Capture(null, actualFailure); // Actual synchronous fault, including OCE, is not owner cancellation.
            ThrowRetained(); throw;
        }
    }

    internal async Task<T> AwaitAsync<T>(Func<Task<T>> source, bool owningCleanup = false)
    {
        Task<T>? actual = null;
        try
        {
            DemandOriginalCallbackAdmission(owningCleanup);
            actual = TaskRunProcessProducerContext.Invoke(OriginalProcessOwner, source) ?? throw new InvalidOperationException("The actual Agent source returned no Task.");
            lock (_gate) _sources.Add(actual);
            return await actual.ConfigureAwait(false);
        }
        catch (Exception actualFailure)
        {
            Capture(actual, actualFailure);
            ThrowRetained();
            throw;
        }
    }

    internal async Task AwaitAsync(Func<Task> source, bool owningCleanup = false)
    {
        Task? actual = null;
        try
        {
            DemandOriginalCallbackAdmission(owningCleanup);
            actual = TaskRunProcessProducerContext.Invoke(OriginalProcessOwner, source) ?? throw new InvalidOperationException("The actual Agent source returned no Task.");
            lock (_gate) _sources.Add(actual);
            await actual.ConfigureAwait(false);
        }
        catch (Exception actualFailure)
        {
            Capture(actual, actualFailure);
            ThrowRetained();
            throw;
        }
    }

    private void Capture(Task? actual, Exception caught)
    {
        lock (_gate)
        {
            if (actual?.IsCanceled == true && !_knownFault && _causes.Count > 0) return;
            if (actual is null || actual.IsFaulted) _knownFault = true;
            var sources = actual?.Exception is { InnerExceptions.Count: > 0 } faults
                ? faults.InnerExceptions.ToArray() : new[] { caught };
            foreach (var source in sources)
            {
                var direct = _ownEnvelopes.TryGetValue(source, out var originals) ? originals : new[] { source };
                foreach (var cause in direct)
                    if (!_causes.Any(known => ReferenceEquals(known, cause))) _causes.Add(cause);
            }
        }
    }

    private void ThrowRetained()
    {
        Exception[] causes;
        bool fault;
        lock (_gate) { causes = _causes.ToArray(); fault = _knownFault; }
        if (causes.Length == 1 && (causes[0] is not OperationCanceledException || !fault))
            ExceptionDispatchInfo.Capture(causes[0]).Throw();
        if (causes.Length == 0) return;
        var envelope = new AggregateException("The actual Agent operation or its source tasks failed.", causes);
        lock (_gate) _ownEnvelopes[envelope] = causes;
        throw envelope; // Synchronous/faulted OCE is a fault payload, never manufactured cancellation.
    }
}
