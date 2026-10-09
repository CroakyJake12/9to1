namespace Haven.Application;

public sealed partial class TaskExecutionCoordinator : ITaskRunProcessRetirementParticipant
{
    private readonly object _processProducerGate = new();
    private readonly List<CanonicalChatProcessProducer> _processChatProducers = [];
    private volatile bool _processProducerAdmissionSealed;
    internal bool HasSealedOriginalProcessProducerAdmission => _processProducerAdmissionSealed;
    private CanonicalChatProcessProducer[]? _processSealedChatCohort;
    private Task? _originalProcessRequest;
    private Task? _originalProcessClose;
    private readonly List<Task> _actualProcessChildCloses = [];
    private readonly List<Exception> _actualProcessCauses = [];

    internal IAsyncEnumerable<ChatStreamEvent> RegisterOriginalCanonicalChatProducer(
        TaskRunInvocationCustody custody, Func<CancellationToken, IAsyncEnumerable<ChatStreamEvent>> source,
        CancellationToken callerToken)
    {
        if (!ReferenceEquals(custody.Issuer, this) || !ReferenceEquals(custody.OriginalSelf, custody))
            throw new InvalidOperationException("No same privately issued original canonical invocation exists.");
        lock (_processProducerGate)
        {
            if (_processProducerAdmissionSealed)
                throw new InvalidOperationException("Canonical process producer admission is sealed.");
            _processChatProducers.RemoveAll(static prior => prior.HasHealthyClosedOriginal || prior.HasSuccessfullyResolvedOriginalToolCheckpoint);
            if (_processChatProducers.Count >= OriginalInvocationCapacity)
                throw new InvalidOperationException("Actual unresolved canonical producers require original-custody inspection.");
            if (custody.OriginalProcessProducer is not null)
                throw new InvalidOperationException("The original canonical invocation already has its actual outer producer.");
            var original = new CanonicalChatProcessProducer(this, custody, source, callerToken);
            _processChatProducers.Add(original);
            custody.OriginalProcessProducer = original;
            return original;
        }
    }

    public void DemandExternalOriginalProcessJoin()
    {
        CanonicalChatProcessProducer.DemandExternalOwnerJoin(this);
        TaskRunProcessProducerContext.DemandExternalJoin(this);
        DemandExternalOriginalActionJoin();
        CanonicalChatProcessProducer[] originals;
        lock (_processProducerGate) originals = _processChatProducers.ToArray();
        // Entire cohort is preflighted before any stop request or awaitable close is admitted.
        foreach (var original in originals) original.DemandExternalOriginalJoin();
    }

    public void RequestOriginalProcessRetirement()
    {
        TaskCompletionSource? start = null;
        lock (_processProducerGate)
        {
            if (_originalProcessRequest is not null) return;
            _processProducerAdmissionSealed = true;
            // Historical failed Tasks keep their exact fault/cause status. Only a SAME
            // privately completed successor receipt retires their already joined cohort.
            _processChatProducers.RemoveAll(static prior => prior.HasSuccessfullyResolvedOriginalToolCheckpoint);
            _processSealedChatCohort = _processChatProducers.ToArray();
            _sealedOriginalProcessStages = _originalProcessStages.ToArray();
            _sealedHostedOriginalRunResumes = _hostedOriginalRunResumes.ToArray();
            SealOriginalInitialTaskObservations();
            foreach (var stage in _sealedOriginalProcessStages) stage.Seal();
            // Pure metadata seals only: no callback, cancellation or task join under the registry gate.
            // Child admissions consult the SAME global seal without acquiring this registry lock.
            foreach (var original in _processSealedChatCohort) original.SealOriginalProcessAdmission();
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _originalProcessRequest = RequestPublishedOriginalProducersAsync(start.Task, _processSealedChatCohort);
        }
        start.SetResult();
    }

    private async Task RequestPublishedOriginalProducersAsync(Task start, CanonicalChatProcessProducer[] originals)
    {
        await start.ConfigureAwait(false);
        // Requests are independent and complete before any child close is joined.
        // This loop never waits on a producer's Move, cancellation or Dispose.
        foreach (var original in originals)
            try { original.RequestOriginalProcessRetirement(); }
            catch (Exception actualRequestFailure) { RetainOriginalProcessFailure(actualRequestFailure); }
        TaskRunProcessStageCustody[] stages;
        lock (_processProducerGate) stages = _sealedOriginalProcessStages
            ?? throw new InvalidOperationException("No actual sealed coordinator source cohort exists.");
        HostedOriginalRunResume[] observations;
        lock (_processProducerGate) observations = _sealedHostedOriginalRunResumes
            ?? throw new InvalidOperationException("No actual sealed hosted observation cohort exists.");
        // These drivers were already issued and enrolled before the global seal. Request every
        // observer independently before any source-stage joins; UI retirement never stops business.
        foreach (var observation in observations)
            try { RequestHostedObservationRetirement(observation); }
            catch (Exception cause) { RetainOriginalProcessFailure(cause); }
        RequestOriginalInitialTaskObservations();
        foreach (var stage in stages)
            try { stage.RequestStop(); } catch (Exception cause) { RetainOriginalProcessFailure(cause); }
        ThrowOriginalProcessFailures();
    }

    public Task CloseAndSuspendOriginalProducersAsync()
    {
        DemandExternalOriginalProcessJoin();
        RequestOriginalProcessRetirement();
        TaskCompletionSource? start = null;
        Task actual;
        lock (_processProducerGate)
        {
            if (_originalProcessClose is null)
            {
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _originalProcessClose = ClosePublishedOriginalProducersAsync(start.Task);
            }
            actual = _originalProcessClose;
        }
        start?.SetResult();
        return actual;
    }

    private async Task ClosePublishedOriginalProducersAsync(Task start)
    {
        await start.ConfigureAwait(false);
        Task request; CanonicalChatProcessProducer[] originals;
        lock (_processProducerGate)
        {
            request = _originalProcessRequest ?? throw new InvalidOperationException("No actual request-all original exists.");
            originals = _processSealedChatCohort ?? throw new InvalidOperationException("No actual sealed producer cohort exists.");
        }
        try { await request.ConfigureAwait(false); }
        catch (Exception cause) { RetainOriginalProcessFailure(cause, request); }
        var closes = new List<Task>();
        foreach (var original in originals)
        {
            try
            {
                var actual = original.CloseAndSuspendOriginalProducerAsync();
                lock (_processProducerGate) _actualProcessChildCloses.Add(actual);
                closes.Add(actual);
            }
            catch (Exception acquisitionFailure) { RetainOriginalProcessFailure(acquisitionFailure); }
        }
        TaskRunProcessStageCustody[] stages;
        lock (_processProducerGate) stages = _sealedOriginalProcessStages
            ?? throw new InvalidOperationException("No actual coordinator source cohort exists.");
        foreach (var stage in stages)
            try
            {
                var actual = stage.CloseOriginalAsync();
                lock (_processProducerGate) _actualProcessChildCloses.Add(actual);
                closes.Add(actual);
            }
            catch (Exception cause) { RetainOriginalProcessFailure(cause); }
        foreach (var actual in closes)
            try { await actual.ConfigureAwait(false); }
            catch (Exception closeFailure) { RetainOriginalProcessFailure(closeFailure, actual); }
        foreach (var observation in _observationFailures)
            RetainOriginalProcessFailure(observation.OriginalException);
        ThrowOriginalProcessFailures();
    }

    private void RetainOriginalProcessFailure(Exception cause, Task? actual = null)
    {
        lock (_processProducerGate)
        {
            IEnumerable<Exception> originals = actual?.Exception is { } faults ? faults.InnerExceptions : new[] { cause };
            foreach (var original in originals)
                if (!_actualProcessCauses.Any(prior => ReferenceEquals(prior, original))) _actualProcessCauses.Add(original);
        }
    }

    private void ThrowOriginalProcessFailures()
    {
        Exception[] causes;
        lock (_processProducerGate) causes = _actualProcessCauses.ToArray();
        if (causes.Length > 0)
            throw new AggregateException("Actual canonical request-all and producer drains did not all succeed.", causes);
    }
}
