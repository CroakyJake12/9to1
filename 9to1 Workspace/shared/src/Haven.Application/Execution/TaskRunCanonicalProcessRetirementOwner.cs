namespace Haven.Application;

/// <summary>The configured canonical business cohort. Request-only sealing and complete
/// original custody are separate from UI observation retirement and final-clean authority.</summary>
public sealed class TaskRunCanonicalProcessRetirementOwner : ITaskRunProcessRetirementParticipant
{
    private readonly object _gate = new();
    private readonly TaskExecutionCoordinator _coordinator;
    private readonly AgentTaskRuntimeService _agents;
    private readonly TaskRunPermissionAuthority _authority;
    private readonly TaskRunOriginalFrameOwner _frames;
    private readonly List<Task> _originalRequests = [];
    private readonly List<Task> _originalCloses = [];
    private readonly List<Exception> _causes = [];
    private Task? _request;
    private Task? _close;

    public TaskRunCanonicalProcessRetirementOwner(TaskExecutionCoordinator coordinator,
        AgentTaskRuntimeService agents, TaskRunPermissionAuthority authority,
        TaskRunOriginalFrameOwner frames)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(frames);
        coordinator.DemandOriginalCanonicalProcessComposition(authority, frames);
        agents.DemandOriginalCanonicalProcessCoordinator(coordinator);
        _coordinator = coordinator;
        _agents = agents;
        _authority = authority;
        _frames = frames;
    }

    /// <summary>Pure composition identity only. This never grants permission or certifies settlement.</summary>
    public bool HasOriginalComposition(TaskExecutionCoordinator coordinator,
        AgentTaskRuntimeService agents, TaskRunPermissionAuthority authority,
        TaskRunOriginalFrameOwner frames) =>
        ReferenceEquals(_coordinator, coordinator) && ReferenceEquals(_agents, agents)
        && ReferenceEquals(_authority, authority) && ReferenceEquals(_frames, frames);

    public void DemandExternalOriginalProcessJoin()
    {
        TaskRunProcessProducerContext.DemandExternalJoin(this);
        _coordinator.DemandExternalOriginalProcessJoin();
        _agents.DemandExternalOriginalProcessJoin();
        _frames.DemandExternalOriginalProcessJoin();
    }

    public void RequestOriginalProcessRetirement()
    {
        TaskCompletionSource? start = null;
        lock (_gate)
        {
            if (_request is not null) return;
            // Pure metadata seals only. All three gates deny admission before any request
            // callback runs; no policy/repository read, cancellation or child join occurs here.
            _authority.RequestOriginalAdmissionSeal();
            _coordinator.SealOriginalCanonicalProcessAdmission();
            _agents.SealOriginalCanonicalProcessAdmission();
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _request = RequestAllOriginalsAsync(start.Task);
        }
        start.SetResult();
    }

    private async Task RequestAllOriginalsAsync(Task start)
    {
        await start.ConfigureAwait(false);
        using var context = TaskRunProcessProducerContext.EnterAsync(this);
        // Capture both actual request drivers before any join. Their completion proves only
        // that every retained business original has been requested, never business settlement.
        var requests = new List<Task>();
        CaptureRequest(_coordinator.RequestOriginalProcessRetirement,
            _coordinator.ReadOriginalCanonicalProcessRequest, requests);
        CaptureRequest(_agents.RequestOriginalProcessRetirement,
            _agents.ReadOriginalCanonicalProcessRequest, requests);
        foreach (var actual in requests)
            try { await actual.ConfigureAwait(false); }
            catch (Exception cause) { Retain(cause, actual); }
        ThrowRetained();
    }

    private void CaptureRequest(Action request, Func<Task> original, List<Task> requests)
    {
        try { TaskRunProcessProducerContext.Invoke(this, () => { request(); return true; }); }
        catch (Exception cause) { Retain(cause); }
        // If a request faulted after publication, its same real driver must still be retained.
        try
        {
            var actual = TaskRunProcessProducerContext.Invoke(this, original);
            lock (_gate) _originalRequests.Add(actual);
            requests.Add(actual);
        }
        catch (Exception cause) { Retain(cause); }
    }

    public Task CloseAndSuspendOriginalProducersAsync()
    {
        DemandExternalOriginalProcessJoin(); // Guard before an existing Task can be returned.
        RequestOriginalProcessRetirement();
        TaskCompletionSource? start = null;
        Task actual;
        lock (_gate)
        {
            if (_close is null)
            {
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _close = CloseAllOriginalsAsync(start.Task);
            }
            actual = _close;
        }
        start?.SetResult();
        return actual;
    }

    private async Task CloseAllOriginalsAsync(Task start)
    {
        await start.ConfigureAwait(false);
        using var context = TaskRunProcessProducerContext.EnterAsync(this);
        Task request;
        lock (_gate) request = _request
            ?? throw new InvalidOperationException("No same original canonical request driver exists.");
        try { await request.ConfigureAwait(false); }
        catch (Exception cause) { Retain(cause, request); }
        // Acquire every actual close before awaiting any of them. Frame close starts only
        // after both complete business request loops, independently of their earlier failures.
        var closes = new List<Task>();
        CaptureClose(_coordinator.CloseAndSuspendOriginalProducersAsync, closes);
        CaptureClose(_agents.CloseAndSuspendOriginalProducersAsync, closes);
        CaptureClose(_frames.CloseAndDrainAsync, closes);
        foreach (var actual in closes)
            try { await actual.ConfigureAwait(false); }
            catch (Exception cause) { Retain(cause, actual); }
        ThrowRetained();
    }

    private void CaptureClose(Func<Task> source, List<Task> closes)
    {
        try
        {
            var actual = TaskRunProcessProducerContext.Invoke(this, source)
                ?? throw new InvalidOperationException("A canonical business owner returned no actual close Task.");
            lock (_gate) _originalCloses.Add(actual);
            closes.Add(actual);
        }
        catch (Exception cause) { Retain(cause); }
    }

    private void Retain(Exception cause, Task? actual = null)
    {
        lock (_gate)
        {
            IEnumerable<Exception> originals = actual?.Exception is { InnerExceptions.Count: > 0 } group
                ? group.InnerExceptions : new[] { cause };
            foreach (var original in originals)
                if (!_causes.Any(prior => ReferenceEquals(prior, original))) _causes.Add(original);
        }
    }

    private void ThrowRetained()
    {
        Exception[] causes;
        lock (_gate) causes = _causes.ToArray();
        if (causes.Length > 0)
            throw new AggregateException("Canonical business request and original drains did not all succeed.", causes);
    }
}
