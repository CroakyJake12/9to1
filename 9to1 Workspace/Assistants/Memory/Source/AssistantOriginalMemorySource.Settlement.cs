using Haven.Application;

namespace HavenOS.Apps.Assistants.Memory;

public sealed partial class AssistantOriginalMemorySource :
    ICanonicalOriginalWriteSettlementPinOwner<ICanonicalAssistantMemoryWriteIntent, ICanonicalAssistantMemoryWriteAcknowledgment>
{
    public Task WaitOriginalSettlementDispatchWithinSourceAsync(ICanonicalAssistantMemoryWriteIntent sameIntent,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        var intent = DemandWriteIntent(sameIntent); TaskCompletionSource? start = null; Task actual;
        lock (_writeGate)
        {
            if (!_writeAttempts.TryGetValue(intent, out var attempt))
                throw new UnauthorizedAccessException("The actual memory write invocation is unavailable.");
            var invocation = attempt.Invocation;
            if (invocation.OriginalDispatchWait is null)
            {
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                invocation.OriginalDispatchWait = _originals.AdmitOriginalCleanup(attempt.Driver, () =>
                    WaitOriginalDispatchAsync(start.Task, invocation,
                        _originals.CreateCleanupScope(originalSynchronousScope, retainOriginalTask)));
            }
            actual = invocation.OriginalDispatchWait;
        }
        start?.SetResult(); return actual;
    }

    public bool IsOwnedOriginalSettlementDispatch(ICanonicalAssistantMemoryWriteIntent sameIntent,
        Task sameOriginalWait, Task<ICanonicalAssistantMemoryWriteAcknowledgment>? sameAtomic)
    {
        if (sameIntent is not WriteIntent intent || !ReferenceEquals(intent.Owner, this) || sameOriginalWait is null) return false;
        lock (_writeGate)
            return _writeAttempts.TryGetValue(intent, out var attempt) &&
                ReferenceEquals(attempt.Invocation.OriginalDispatchWait, sameOriginalWait) &&
                sameOriginalWait.IsCompletedSuccessfully && attempt.Invocation.OriginalDispatchWaitHealthy &&
                Volatile.Read(ref attempt.Invocation.OriginalDispatchSettled) == 1 &&
                ReferenceEquals(attempt.Invocation.Atomic, sameAtomic) &&
                (sameAtomic is null || sameAtomic.IsCompleted && IsOriginalAtomicWriteTask(intent, sameAtomic));
    }

    private static void MarkOriginalDispatchSettled(WriteInvocation invocation)
    {
        // Called only after productive raw acquisition/atomic dispatch has returned,
        // including capture of late pins. It precedes Home completion or claim close.
        Volatile.Write(ref invocation.OriginalDispatchSettled, 1);
        invocation.OriginalDispatchBarrier.TrySetResult();
    }

    private async Task WaitOriginalDispatchAsync(Task start, WriteInvocation invocation, AssistantMemoryOriginals.Scope source)
    {
        await start.ConfigureAwait(false); var failures = new List<Exception>();
        try
        {
            // Join only the private dispatch boundary, never the enclosing writer
            // driver which itself awaits Home settlement. Cancellation cannot skip it.
            await source.Read(() => invocation.OriginalDispatchBarrier.Task).ConfigureAwait(false);
            source.Run(() =>
            {
                if (Volatile.Read(ref invocation.OriginalDispatchSettled) != 1 ||
                    invocation.Atomic is { } raw && (!raw.IsCompleted || !IsOriginalAtomicWriteTask(invocation.Intent, raw)))
                    throw new UnauthorizedAccessException("The original memory acquisition/atomic cohort is not terminal.");
            });
        }
        catch (Exception failure) { failures.Add(failure); }
        try { await source.JoinAsync().ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
        AssistantMemoryOriginals.Throw(failures);
        lock (_writeGate) invocation.OriginalDispatchWaitHealthy = true;
    }

    public Task ReleaseOriginalSettlementPinsWithinSourceAsync(
        ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalAssistantMemoryWriteIntent, ICanonicalAssistantMemoryWriteAcknowledgment> sameHomePhase,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        var invocation = DemandOriginalSettlementInvocation(sameHomePhase);
        TaskCompletionSource? start = null; Task actual;
        lock (_writeGate)
        {
            if (invocation.SettlementPhase is not null && !ReferenceEquals(invocation.SettlementPhase, sameHomePhase))
                throw new UnauthorizedAccessException("The original Home settlement phase cannot be replaced.");
            if (invocation.SettlementRelease is null)
            {
                invocation.SettlementPhase = sameHomePhase;
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!_writeAttempts.TryGetValue(invocation.Intent, out var attempt))
                    throw new UnauthorizedAccessException("The SAME admitted memory write parent is unavailable.");
                invocation.SettlementRelease = _originals.AdmitOriginalCleanup(attempt.Driver, () =>
                    ReleaseOriginalSettlementPinsAsync(start.Task, invocation,
                        _originals.CreateCleanupScope(originalSynchronousScope, retainOriginalTask)));
            }
            actual = invocation.SettlementRelease;
        }
        // Cleanup remains available after presentation retirement or cancellation.
        // Cache custody before entering the actual close; a failed close is never replayed.
        start?.SetResult(); return actual;
    }

    public bool IsOwnedOriginalSettlementPinRelease(
        ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalAssistantMemoryWriteIntent, ICanonicalAssistantMemoryWriteAcknowledgment> sameHomePhase,
        Task sameOriginalReleaseTask)
    {
        if (sameHomePhase is null || sameOriginalReleaseTask is null ||
            _writes?.IsIssuedOriginalSettlementReleasePhase(sameHomePhase) != true ||
            sameHomePhase.OriginalIntent is not WriteIntent intent || !ReferenceEquals(intent.Owner, this)) return false;
        lock (_writeGate)
            return _writeAttempts.TryGetValue(intent, out var attempt) &&
                ReferenceEquals(attempt.Invocation.SettlementPhase, sameHomePhase) &&
                ReferenceEquals(attempt.Invocation.SettlementRelease, sameOriginalReleaseTask) &&
                sameOriginalReleaseTask.IsCompletedSuccessfully && attempt.Invocation.SettlementReleaseHealthy &&
                (attempt.Invocation.Den is null || attempt.Invocation.OriginalDenRelease?.IsCompletedSuccessfully == true);
    }

    private WriteInvocation DemandOriginalSettlementInvocation(
        ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalAssistantMemoryWriteIntent, ICanonicalAssistantMemoryWriteAcknowledgment> phase)
    {
        ArgumentNullException.ThrowIfNull(phase);
        if (_writes?.IsIssuedOriginalSettlementReleasePhase(phase) != true)
            throw new UnauthorizedAccessException("The SAME bound Home owner must issue the terminal settlement phase.");
        var intent = DemandWriteIntent(phase.OriginalIntent); WriteInvocation invocation;
        lock (_writeGate)
            invocation = _writeAttempts.TryGetValue(intent, out var attempt) ? attempt.Invocation
                : throw new UnauthorizedAccessException("No actual memory write invocation owns this phase.");
        if (Volatile.Read(ref invocation.OriginalDispatchSettled) != 1 ||
            !ReferenceEquals(invocation.Atomic, phase.OriginalAtomicSqlTask) ||
            invocation.Atomic is { } raw && (!raw.IsCompleted || !IsOriginalAtomicWriteTask(intent, raw)))
            throw new UnauthorizedAccessException("The exact original atomic child or owned no-SQL dispatch must settle before pin release.");
        return invocation;
    }

    private async Task ReleaseOriginalSettlementPinsAsync(Task start, WriteInvocation invocation, AssistantMemoryOriginals.Scope source)
    {
        await start.ConfigureAwait(false); var failures = new List<Exception>();
        try
        {
            source.Run(() =>
            {
                if (!ReferenceEquals(DemandOriginalSettlementInvocation(invocation.SettlementPhase!), invocation))
                    throw new UnauthorizedAccessException("The actual memory settlement invocation changed.");
            });
            if (invocation.Den is { } den)
                await source.Read(() => invocation.OriginalDenRelease = den.CloseAndDrainAsync()).ConfigureAwait(false);
        }
        catch (Exception failure) { failures.Add(failure); }
        // Join raw close even if its publication or a foreign callback failed.
        try { await source.JoinAsync().ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
        AssistantMemoryOriginals.Throw(failures);
        lock (_writeGate) invocation.SettlementReleaseHealthy = true;
    }
}
