namespace Haven.Application;

public sealed partial class AgentTaskRuntimeService : ITaskRunProcessRetirementParticipant
{
    private readonly object _agentProcessGate = new();
    private readonly List<AgentProcessOriginal> _agentProcessOperations = [];
    private readonly List<AgentOriginalObservationCustody> _agentProcessPresentations = [];
    private volatile bool _agentProcessSealed;
    internal void DemandOriginalProcessSourceAdmission()
    {
        if (_agentProcessSealed) throw new InvalidOperationException("Agent source callback admission is sealed for process retirement.");
    }
    private AgentProcessOriginal[]? _sealedAgentOperations;
    private AgentOriginalObservationCustody[]? _sealedAgentPresentations;
    private Task? _agentProcessRequest;
    private Task? _agentProcessClose;
    private readonly List<Task> _agentProcessActualCloses = [];
    private readonly List<Exception> _agentProcessCauses = [];

    internal bool HasOriginalProcessObserverFaults(AgentCanonicalOriginal? sameOriginal) =>
        sameOriginal is not null && _observerFailures.Any(value => value.RunId == sameOriginal.Expected.Id);

    private Task<T> RunOriginalBusinessProcessOperation<T>(
        Func<AgentRuntimeOriginalCustody, CancellationToken, Task<T>> body, CancellationToken callerToken) =>
        StartOriginalAgentProcessOperation(body, callerToken);

    private Task<T> StartOriginalAgentProcessOperation<T>(
        Func<AgentRuntimeOriginalCustody, CancellationToken, Task<T>> body, CancellationToken? callerToken)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<T> driver;
        lock (_agentProcessGate)
        {
            if (_agentProcessSealed) throw new InvalidOperationException("Agent process source admission is sealed.");
            _agentProcessOperations.RemoveAll(static prior => prior.HealthyClosed);
            if (_agentProcessOperations.Count >= 128)
                throw new InvalidOperationException("Actual unresolved Agent process originals require owning inspection.");
            AgentRuntimeOriginalCustody original;
            lock (_originalOperationGate)
            {
                _originalOperations.RemoveAll(static prior => prior.Healthy);
                if (_originalOperations.Count >= 128)
                    throw new InvalidOperationException("Actual failed or unknown Agent source operations require owning-service inspection.");
                original = new() { OriginalProcessOwner = this };
                _originalOperations.Add(original);
            }
            var process = new AgentProcessOriginal(this, original, callerToken);
            original.OriginalProcessCleanup = process.CloseOriginalLifetimeAsync;
            original.OriginalProcessProducer = process;
            _agentProcessOperations.Add(process);
            driver = original.Start(operation => body(operation, process.Lifetime?.Token ?? default), start.Task);
            process.ActualDriver = driver;
        }
        // Real driver, custody, borrowed caller token and whole source admission are visible first.
        start.SetResult();
        return driver;
    }

    public void DemandExternalOriginalProcessJoin()
    {
        TaskRunProcessProducerContext.DemandExternalJoin(this);
        AgentProcessOriginal[] originals;
        lock (_agentProcessGate) originals = _agentProcessOperations.ToArray();
        foreach (var original in originals)
            original.Original.OriginalCanonicalAgent?.Chat?.Original.OriginalProcessProducer?.DemandExternalOriginalJoin();
    }

    public void RequestOriginalProcessRetirement()
    {
        TaskCompletionSource? start = null;
        lock (_agentProcessGate)
        {
            if (_agentProcessRequest is not null) return;
            _agentProcessSealed = true;
            _sealedAgentOperations = _agentProcessOperations.ToArray();
            _sealedAgentPresentations = _agentProcessPresentations.ToArray();
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _agentProcessRequest = RequestPublishedAgentOriginalsAsync(start.Task,
                _sealedAgentOperations, _sealedAgentPresentations);
        }
        start.SetResult();
    }

    private async Task RequestPublishedAgentOriginalsAsync(Task start, AgentProcessOriginal[] operations,
        AgentOriginalObservationCustody[] presentations)
    {
        await start.ConfigureAwait(false);
        foreach (var original in operations)
            try { original.RequestOriginalProcessRetirement(); }
            catch (Exception cause) { RetainAgentProcessFailure(cause); }
        foreach (var original in presentations)
            try { RequestOriginalObservationRetirement(original); }
            catch (Exception cause) { RetainAgentProcessFailure(cause); }
        ThrowAgentProcessFailures();
    }

    public Task CloseAndSuspendOriginalProducersAsync()
    {
        DemandExternalOriginalProcessJoin();
        RequestOriginalProcessRetirement();
        TaskCompletionSource? start = null;
        Task actual;
        lock (_agentProcessGate)
        {
            if (_agentProcessClose is null)
            {
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _agentProcessClose = ClosePublishedAgentOriginalsAsync(start.Task);
            }
            actual = _agentProcessClose;
        }
        start?.SetResult();
        return actual;
    }

    private async Task ClosePublishedAgentOriginalsAsync(Task start)
    {
        await start.ConfigureAwait(false);
        Task request; AgentProcessOriginal[] operations; AgentOriginalObservationCustody[] presentations;
        lock (_agentProcessGate)
        {
            request = _agentProcessRequest ?? throw new InvalidOperationException("No actual Agent request-all driver exists.");
            operations = _sealedAgentOperations ?? throw new InvalidOperationException("No sealed actual Agent sources exist.");
            presentations = _sealedAgentPresentations ?? throw new InvalidOperationException("No sealed actual encompassing presentation producers exist.");
        }
        try { await request.ConfigureAwait(false); } catch (Exception cause) { RetainAgentProcessFailure(cause, request); }
        var closes = new List<Task>();
        foreach (var original in operations)
            try { closes.Add(original.CloseWholeOriginalAsync()); } catch (Exception cause) { RetainAgentProcessFailure(cause); }
        foreach (var original in presentations)
            closes.Add(CloseOriginalProcessPresentationAsync(original));
        lock (_agentProcessGate) _agentProcessActualCloses.AddRange(closes);
        foreach (var actual in closes)
            try { await actual.ConfigureAwait(false); } catch (Exception cause) { RetainAgentProcessFailure(cause, actual); }
        // Durable history ACK and the public business result keep their existing semantics.
        // Exact source observer causes separately refuse a process-drain acknowledgment;
        // their matching originals cannot be retired as healthy admission slots.
        foreach (var (_, actualObserverCause) in _observerFailures) RetainAgentProcessFailure(actualObserverCause);
        ThrowAgentProcessFailures();
    }

    private async Task CloseOriginalProcessPresentationAsync(AgentOriginalObservationCustody original)
    {
        var causes = new List<Exception>();
        Task? producer; Task? wait; Task? detach;
        lock (original.Gate) { producer = original.Producer; wait = original.Wait; detach = original.Detach; }
        foreach (var actual in new[] { producer, wait, detach })
        {
            if (actual is null) { causes.Add(new InvalidOperationException("An actual encompassing observation original is missing.")); continue; }
            try { await actual.ConfigureAwait(false); }
            catch (Exception cause) { causes.AddRange(actual.Exception is { } faults ? faults.InnerExceptions : new[] { cause }); }
        }
        if (causes.Count > 0) throw new AggregateException("Actual encompassing Agent producer/presentation drain failed.", causes);
        if (!original.ProducerCustody.Healthy || !original.WaitCustody.Healthy || !original.DetachCustody.Healthy)
            throw new InvalidOperationException("Detached presentation metadata cannot certify the encompassing actual Agent producer drain.");
    }

    private void RetainAgentProcessFailure(Exception cause, Task? actual = null)
    {
        lock (_agentProcessGate)
        {
            IEnumerable<Exception> direct = actual?.Exception is { } faults ? faults.InnerExceptions : new[] { cause };
            foreach (var original in direct)
                if (!_agentProcessCauses.Any(prior => ReferenceEquals(prior, original))) _agentProcessCauses.Add(original);
        }
    }

    private void ThrowAgentProcessFailures()
    {
        Exception[] causes;
        lock (_agentProcessGate) causes = _agentProcessCauses.ToArray();
        if (causes.Length > 0) throw new AggregateException("Actual Agent process originals did not all settle successfully.", causes);
    }
}
