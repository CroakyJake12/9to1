using Haven.Core;

namespace Haven.Application;

/// <summary>Private ownership of the actual existing producer and presentation originals.
/// None of these process-local references grants Task, model, tool or recovery authority.</summary>
internal sealed class AgentOriginalObservationCustody(object issuerMarker)
{
    internal readonly object IssuerMarker = issuerMarker;
    internal readonly object Gate = new();
    internal readonly TaskCompletionSource Detached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly AgentRuntimeOriginalCustody ProducerCustody = new();
    internal readonly AgentRuntimeOriginalCustody WaitCustody = new();
    internal readonly AgentRuntimeOriginalCustody DetachCustody = new();
    internal AgentRunOriginalObservationLease Lease = null!;
    internal Task<AgentRun>? ActualProducer;
    internal Task<AgentRun>? Producer;
    internal Task<AgentRunObservationResult>? Wait;
    internal Task? Detach;
    internal bool RetirementRequested;
    internal bool AttachedToCanonicalOriginal;

    internal bool CanRetireHealthyAdmission
    {
        get
        {
            lock (Gate) return ProducerCustody.Healthy && WaitCustody.Healthy
                && (Detach is null || DetachCustody.Healthy) && AttachedToCanonicalOriginal;
        }
    }
}

public sealed partial class AgentTaskRuntimeService : IAgentRunOriginalObservationSource
{
    private readonly object _originalObservationIssuer = new();
    private readonly object _originalObservationGate = new();
    private readonly List<AgentOriginalObservationCustody> _originalObservations = [];
    internal IReadOnlyList<AgentOriginalObservationCustody> ActualOriginalObservations
    { get { lock (_originalObservationGate) return _originalObservations.ToArray(); } }

    public AgentRunOriginalObservationLease StartObservedOriginalRun(Guid agentId, string task,
        CancellationToken cancellationToken, Guid? retryOfRunId = null, string? resourceReference = null) =>
        StartOriginalObservation(() => RunAsync(agentId, task, cancellationToken, retryOfRunId, resourceReference));

    public AgentRunOriginalObservationLease StartObservedOriginalRetry(Guid runId, CancellationToken cancellationToken) =>
        StartOriginalObservation(() => RetryAsync(runId, cancellationToken));

    public bool IsIssuedOriginalObservation(AgentRunOriginalObservationLease lease) =>
        lease is not null && ReferenceEquals(lease.Issuer, this)
        && lease.Original is AgentOriginalObservationCustody original
        && ReferenceEquals(original.IssuerMarker, _originalObservationIssuer)
        && ReferenceEquals(original.Lease, lease);

    private AgentRunOriginalObservationLease StartOriginalObservation(Func<Task<AgentRun>> source)
    {
        var original = new AgentOriginalObservationCustody(_originalObservationIssuer);
        original.Lease = new(this, original, () => original.Wait
            ?? throw new InvalidOperationException("No actual original observation wait exists."),
            () => RequestOriginalObservationRetirement(original),
            () => DetachOriginalObservation(original));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_originalObservationGate)
        {
            // Remove only acknowledged healthy admission slots. Exact custody remains on the
            // actual canonical invocation and issued lease; failed/unknown producers stay here.
            _originalObservations.RemoveAll(static prior => prior.CanRetireHealthyAdmission);
            if (_originalObservations.Count >= 128)
                throw new InvalidOperationException("Retained original Agent observations require owning-service inspection.");
            _originalObservations.Add(original);
            original.Producer = original.ProducerCustody.Start(async operation =>
            {
                await start.Task.ConfigureAwait(false);
                var run = await operation.AwaitAsync(() =>
                {
                    var actual = source() ?? throw new InvalidOperationException("No actual Agent producer Task was returned.");
                    lock (original.Gate) original.ActualProducer = actual;
                    return actual;
                }).ConfigureAwait(false);
                RetainOriginalPresentationCustody(original, run);
                return run;
            });
            original.Wait = original.WaitCustody.Start<AgentRunObservationResult>(async operation =>
            {
                await start.Task.ConfigureAwait(false);
                var producer = original.Producer
                    ?? throw new InvalidOperationException("No original Agent producer was published.");
                var selected = await Task.WhenAny(producer, original.Detached.Task).ConfigureAwait(false);
                lock (original.Gate)
                    if (original.RetirementRequested)
                        return new(AgentRunObservationDisposition.ObservationDetached, null);
                if (ReferenceEquals(selected, original.Detached.Task))
                    return new(AgentRunObservationDisposition.ObservationDetached, null);
                var returned = await operation.AwaitAsync(() => producer).ConfigureAwait(false);
                return new(AgentRunObservationDisposition.ProducerTerminal, returned);
            });
        }
        // Both actual drivers, the lease and admission are visible before any producer callback.
        start.SetResult();
        return original.Lease;
    }

    private void RetainOriginalPresentationCustody(AgentOriginalObservationCustody observation, AgentRun returned)
    {
        AgentCanonicalOriginal? original = null;
        if (_canonicalRuns.TryGetValue(returned.Id, out var active)) original = active;
        else if (_recordedObservations.TryGetValue(returned.Id, out var completed)) original = completed.Original;
        if (original is null) return; // Failed/unknown producer admission cannot be pruned.
        lock (original.Gate) original.ActualPresentationObservations.Add(observation);
        lock (observation.Gate) observation.AttachedToCanonicalOriginal = true;
    }

    private void RequestOriginalObservationRetirement(AgentOriginalObservationCustody original)
    {
        _ = DetachOriginalObservation(original); // SAME retained driver; request performs no join or producer cancellation.
    }

    private Task DetachOriginalObservation(AgentOriginalObservationCustody original)
    {
        TaskCompletionSource start;
        Task actual;
        lock (original.Gate)
        {
            if (original.Detach is { } prior) return prior;
            original.RetirementRequested = true;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            original.Detach = original.DetachCustody.Start(async operation =>
            {
                await start.Task.ConfigureAwait(false);
                // RCAA notification releases the SAME admitted wait. This close never joins,
                // cancels, disposes or reclassifies the globally owned canonical producer.
                original.Detached.TrySetResult();
                var wait = original.Wait ?? throw new InvalidOperationException("No actual admitted observation wait exists.");
                await operation.AwaitAsync(() => wait).ConfigureAwait(false);
                return true;
            });
            actual = original.Detach;
        }
        start.SetResult();
        return actual;
    }
}
