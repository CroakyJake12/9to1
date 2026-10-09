using Haven.Core;

namespace Haven.Application;

public sealed partial class TaskExecutionCoordinator
{
    private readonly List<HostedInitialTaskSend> _hostedInitialTaskSends = [];
    private HostedInitialTaskSend[]? _sealedHostedInitialTaskSends;

    internal Task<TaskRunOriginalInitialChatObservationLease> StartOriginalInitialTaskObservation(
        HostedInitialTaskSend original, Func<CancellationToken, Task<TaskRunOriginalInitialChatObservationLease>> source)
    {
        lock (_processProducerGate)
        {
            if (!ReferenceEquals(original.Coordinator, this) || _processProducerAdmissionSealed)
                throw new InvalidOperationException("Initial Tasks source admission is sealed or belongs to another coordinator.");
            _hostedInitialTaskSends.RemoveAll(static prior => prior.Healthy);
            if (_hostedInitialTaskSends.Any(prior => prior.ConversationId == original.ConversationId))
                throw new InvalidOperationException("The SAME Tasks context already owns an initial source; copied input cannot create another.");
            if (_hostedInitialTaskSends.Count >= OriginalInvocationCapacity)
                throw new InvalidOperationException("Finite unresolved initial Task hosts require source inspection.");
            return StartOriginalProcessStage("start-observed-original-initial-task", CancellationToken.None, source,
                stage => { original.Stage = stage; _hostedInitialTaskSends.Add(original); });
        }
    }

    internal async Task ValidateOriginalInitialTaskContextAsync(HostedInitialTaskSend original, CancellationToken token)
    {
        var current = await RequireOriginalProcessStage().Await(() => repository.GetByContextAsync(original.ConversationId, token)).ConfigureAwait(false);
        if (current is not null) throw new InvalidOperationException("A canonical Task/run already exists for this context; initial Send cannot replace it.");
    }

    internal HostedInitialTaskSend PublishOriginalInitialTaskObservation(HostedInitialTaskSend original,
        Func<HostedInitialTaskSend, AgentRuntimeOriginalCustody, Task<TaskRunInitialChatObservationResult>> producer)
    {
        var start = HostedInitialTaskSend.NewSignal();
        lock (_processProducerGate)
        {
            if (_processProducerAdmissionSealed || !ReferenceEquals(original.Coordinator, this)
                || !_hostedInitialTaskSends.Any(prior => ReferenceEquals(prior, original))
                || original.Source is null || original.Invocation is null
                || !ReferenceEquals(original.Invocation.OriginalProcessProducer, original.Source)
                || !ReferenceEquals(original.Invocation.OriginalChatOwner, original.Chat))
                throw new InvalidOperationException("No same registered initial input/outer remains available for observer publication.");
            var sameSource = original.Source ?? throw new InvalidOperationException("No actual initial business source exists.");
            var sameStage = original.Stage ?? throw new InvalidOperationException("No actual initial acquisition stage exists.");
            original.ProducerCustody.OriginalProcessOwner = this;
            original.WaitCustody.OriginalProcessOwner = original;
            original.DetachCustody.OriginalProcessOwner = original;
            original.Lease = new(original.Chat, original);
            original.Producer = original.ProducerCustody.Start(async operation =>
            { await start.Task.ConfigureAwait(false); return await producer(original, operation).ConfigureAwait(false); });
            original.Wait = original.WaitCustody.Start(async operation =>
            {
                await start.Task.ConfigureAwait(false);
                var actual = original.Producer ?? throw new InvalidOperationException("No actual initial producer was published.");
                _ = await operation.AwaitAsync(() => Task.WhenAny(actual, original.Detached.Task)).ConfigureAwait(false);
                lock (original.Gate)
                    if (original.RetirementRequested)
                        return new TaskRunInitialChatObservationResult(TaskRunInitialChatObservationDisposition.ObservationDetached,
                            original.ReadAcknowledgedContext());
                return await operation.AwaitAsync(() => actual).ConfigureAwait(false);
            });
            original.Detach = original.DetachCustody.Start(async operation =>
            {
                await start.Task.ConfigureAwait(false);
                await original.DetachRequested.Task.ConfigureAwait(false);
                original.Detached.TrySetResult();
                InitialChatEventReader? reader;
                lock (original.Gate) reader = original.Reader;
                reader?.RequestDispose();
                try { _ = await operation.AwaitAsync(() => original.Wait!, owningCleanup: true).ConfigureAwait(false); }
                catch (Exception) { } // Same operation retains every actual failure; continue independent observer cleanup.
                if (reader is not null)
                    try { await operation.AwaitAsync(() => reader.ActualDispose, owningCleanup: true).ConfigureAwait(false); }
                    catch (Exception) { }
                InitialChatObserverWaitOriginal<TaskRunInitialChatObservationResult>[] admittedWaits;
                lock (original.Gate) admittedWaits = original.ObserverWaits.ToArray();
                foreach (var admitted in admittedWaits)
                    try { await operation.AwaitAsync(admitted.JoinOriginalAsync, owningCleanup: true).ConfigureAwait(false); }
                    catch (Exception) { }
                if (original.AcquisitionObservationWait is { } acquisitionWait)
                    try { await operation.AwaitAsync(acquisitionWait.JoinOriginalAsync, owningCleanup: true).ConfigureAwait(false); }
                    catch (Exception) { }
                original.ThrowObserverFailures();
                return true;
            });
            sameStage.RetainSource(original.Producer);
            sameStage.RetainSource(original.Wait);
            sameStage.RetainSource(original.Detach);
            original.Chat.RegisterOriginalColdInitialCapture(original, start.Task);
            sameStage.OriginalResultClosed = () => original.Healthy;
            sameStage.OriginalResultJoin = () => sameSource.ActualClose
                ?? sameSource.CloseAndSuspendOriginalProducerAsync();
        }
        start.SetResult();
        if (original.RetirementRequested) original.RequestObservationRetirement();
        return original;
    }

    internal void DemandOriginalInitialTaskBodyAdmission(HostedInitialTaskSend original)
    {
        if (_processProducerAdmissionSealed || !ReferenceEquals(original.Coordinator, this))
            throw new InvalidOperationException("Initial business source callback admission is sealed.");
    }

    internal void AttachOriginalInitialTaskProjection(HostedInitialTaskSend original)
    {
        DemandOriginalInitialTaskBodyAdmission(original);
        if (original.Projection is { } projection) SnapshotChanged += projection;
    }
    internal void DetachOriginalInitialTaskProjection(HostedInitialTaskSend original)
    {
        if (original.Projection is { } projection) SnapshotChanged -= projection;
    }

    internal void ReleaseOriginalUnpublishedInitialInput(HostedInitialTaskSend original)
    {
        var custody = original.Invocation;
        if (custody is null || !ReferenceEquals(custody.Issuer, this) || !ReferenceEquals(custody.OriginalSelf, custody)
            || original.Source is not null || custody.OriginalProcessProducer is not null || custody.OriginalBegin is not null
            || custody.OriginalBinding is not null || custody.OriginalTracker is not null
            || custody.OriginalProviderInvocationInvoked || custody.AttemptAdmissionInvoked) return;
        // Only the exact private source factory that never registered an outer can release this slot.
        // The actual failed acquisition driver and all causes remain on the process source stage.
        ReleaseHealthyOrUnstartedInvocation(custody);
    }

    private void SealOriginalInitialTaskObservations()
    {
        _sealedHostedInitialTaskSends = _hostedInitialTaskSends.ToArray();
        foreach (var original in _sealedHostedInitialTaskSends) original.SealObservationAdmission();
    }
    private void RequestOriginalInitialTaskObservations()
    {
        HostedInitialTaskSend[] originals;
        lock (_processProducerGate) originals = _sealedHostedInitialTaskSends
            ?? throw new InvalidOperationException("No actual sealed initial observer cohort exists.");
        foreach (var original in originals)
            try { original.RequestObservationRetirement(); }
            catch (Exception cause) { RetainOriginalProcessFailure(cause); }
    }
}
