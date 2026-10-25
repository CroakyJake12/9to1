using Haven.Core;

namespace Haven.Application;

public sealed partial class ChatSessionService
{
    /// <summary>Detached availability over this SAME privately issued observation. It is
    /// not a command grant; the actual coordinator freshly authorizes every submission.</summary>
    public bool CanSubmitObservedOriginalTaskFollowUp(TaskRunOriginalInitialChatObservationLease sameObservation)
    {
        if (!IsIssuedOriginalTaskObservation(sameObservation) || taskCoordinator?.HasCommandAuthority != true ||
            !ReferenceEquals(sameObservation.Original.Coordinator, taskCoordinator)) return false;
        lock (sameObservation.Original.Gate)
            return !sameObservation.Original.RetirementRequested && !sameObservation.Original.ObserverAdmissionSealed &&
                !sameObservation.Original.ProducerTerminal && sameObservation.CurrentAcknowledgedContext is not null;
    }

    public async Task<FollowUpDecision> SubmitObservedOriginalTaskFollowUpAsync(
        TaskRunOriginalInitialChatObservationLease sameObservation, string instruction, TaskFollowUpMode mode,
        CancellationToken commandCancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!CanSubmitObservedOriginalTaskFollowUp(sameObservation))
            throw new InvalidOperationException("This original task observation cannot receive a follow-up. Open the existing task instead.");
        var context = sameObservation.CurrentAcknowledgedContext
            ?? throw new InvalidOperationException("The original producer has not acknowledged its Task/Run.");
        var coordinator = sameObservation.Original.Coordinator;
        await safety.EnsureMayActAsync(context.ContextId, "chat.task-follow-up", commandCancellationToken).ConfigureAwait(false);
        var current = await coordinator.GetAsync(context.TaskId, commandCancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The original task is unavailable.");
        if (current.ContextId != sameObservation.ConversationId || current.ExecutionId != context.ExecutionId ||
            current.State is TaskExecutionLifecycle.Completed or TaskExecutionLifecycle.Cancelled or TaskExecutionLifecycle.Failed ||
            !CanSubmitObservedOriginalTaskFollowUp(sameObservation))
            throw new InvalidOperationException("The original task is no longer eligible for this live follow-up.");
        var reply = await coordinator.SubmitFollowUpAsync(context.TaskId, instruction, mode,
            null, null, commandCancellationToken).ConfigureAwait(false);
        if (reply.Snapshot.TaskId != context.TaskId || reply.Snapshot.ExecutionId != context.ExecutionId ||
            reply.Snapshot.ContextId != sameObservation.ConversationId)
            throw new InvalidDataException("The actual follow-up reply belongs to a different Task/Run/context.");
        return reply; // Actual durable ACK, not a statement that a steer was applied.
    }
    private bool TryGetLiveResumedTaskComposerRun(TaskRunOriginalInitialChatObservationLease initial,
        TaskRunOriginalResumeObservationLease resume, out TaskRunInvocationCustody? actual)
    {
        actual = null;
        if (!IsIssuedObservedTaskComposerResume(initial, resume) || taskCoordinator?.HasCommandAuthority != true ||
            resume.Original is not HostedOriginalRunResume hosted) return false;
        lock (hosted.Gate)
        {
            if (hosted.RetirementRequested || hosted.Producer is null || hosted.Producer.IsCompleted) return false;
            if (hosted.ToolCheckpoint is { Claimed: true, Bound: true } checkpoint &&
                ReferenceEquals(checkpoint.Issuer, taskCoordinator) && ReferenceEquals(checkpoint.OriginalHost, hosted) && ReferenceEquals(checkpoint.Next.OriginalProcessProducer, hosted.Source))
                actual = checkpoint.Next;
            else if (hosted.Source.OriginalContinuationCustody?.OriginalColdPreparation is { BodyBound: true } cold &&
                ReferenceEquals(cold.Owner, taskCoordinator) && ReferenceEquals(cold.Invocation.OriginalProcessProducer, hosted.Source.OriginalContinuationCustody.OriginalChild))
                actual = cold.Invocation;
            else if (hosted.Source.OriginalContinuationCustody?.OriginalPreparation is { Claimed: true, Bound: true } same &&
                ReferenceEquals(same.Issuer, taskCoordinator) && ReferenceEquals(same.Next.OriginalProcessProducer, hosted.Source.OriginalContinuationCustody.OriginalChild))
                actual = same.Next;
            if (actual is null || !ReferenceEquals(actual.Issuer, taskCoordinator) || !ReferenceEquals(actual.OriginalSelf, actual) ||
                actual.OriginalBinding is not { } binding || binding.TaskId != resume.CanonicalTaskContext.TaskId ||
                binding.ExecutionId != resume.CanonicalTaskContext.ExecutionId || binding.ContextId != initial.ConversationId ||
                actual.OwnedCleanupTerminal || actual.Causes.Count != 0 || actual.OriginalProcessProducer is not { } producer ||
                producer.ActualStop is not null || producer.ActualDispose is not null ||
                hosted.ActualRun is not null && !ReferenceEquals(hosted.ActualRun, actual))
            { actual = null; return false; }
            return true;
        }
    }
    public bool CanSubmitObservedResumedTaskFollowUp(TaskRunOriginalInitialChatObservationLease initial,
        TaskRunOriginalResumeObservationLease resume) => TryGetLiveResumedTaskComposerRun(initial, resume, out _);

    public async Task<FollowUpDecision> SubmitObservedResumedTaskFollowUpAsync(
        TaskRunOriginalInitialChatObservationLease initial, TaskRunOriginalResumeObservationLease resume,
        string instruction, TaskFollowUpMode mode, CancellationToken commandToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!TryGetLiveResumedTaskComposerRun(initial, resume, out var original))
            throw new InvalidOperationException("The same issued continuation cannot receive a live follow-up.");
        var context = resume.CanonicalTaskContext;
        var coordinator = resume.Issuer;
        await safety.EnsureMayActAsync(context.ContextId, "chat.task-follow-up", commandToken).ConfigureAwait(false);
        var current = await coordinator.GetAsync(context.TaskId, commandToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The original continued task is unavailable.");
        if (current.ContextId != initial.ConversationId || current.ExecutionId != context.ExecutionId ||
            current.State is TaskExecutionLifecycle.Completed or TaskExecutionLifecycle.Cancelled or TaskExecutionLifecycle.Failed ||
            !TryGetLiveResumedTaskComposerRun(initial, resume, out var latest) || !ReferenceEquals(original, latest))
            throw new InvalidOperationException("The original continuation changed or is no longer live.");
        var reply = await coordinator.SubmitFollowUpAsync(context.TaskId, instruction, mode, null, null, commandToken).ConfigureAwait(false);
        if (reply.Snapshot.TaskId != context.TaskId || reply.Snapshot.ExecutionId != context.ExecutionId || reply.Snapshot.ContextId != initial.ConversationId)
            throw new InvalidDataException("The continued follow-up acknowledgement belongs to another Task/Run/context.");
        return reply;
    }
    public async Task<TaskRunOriginalRunControlResult> PauseObservedResumedTaskComposerAsync(
        TaskRunOriginalInitialChatObservationLease initial, TaskRunOriginalResumeObservationLease resume, CancellationToken commandToken)
    {
        if (!TryGetLiveResumedTaskComposerRun(initial, resume, out var original))
            throw new InvalidOperationException("The same issued continuation is no longer live for Pause.");
        var context = resume.CanonicalTaskContext;
        await safety.EnsureMayActAsync(context.ContextId, "chat.pause", commandToken).ConfigureAwait(false);
        var current = await resume.Issuer.GetOriginalRunControlAvailabilityAsync(context.TaskId, context.ExecutionId, commandToken).ConfigureAwait(false);
        if (!current.CanPause || !TryGetLiveResumedTaskComposerRun(initial, resume, out var latest) || !ReferenceEquals(original, latest))
            throw new InvalidOperationException("The original continuation changed before Pause admission.");
        var result = await resume.Issuer.PauseOriginalRunAsync(context.TaskId, context.ExecutionId, commandToken).ConfigureAwait(false);
        if (result.Kind != TaskRunOriginalRunControlKind.Pause || result.CanonicalTaskContext.TaskId != context.TaskId ||
            result.CanonicalTaskContext.ExecutionId != context.ExecutionId || result.CanonicalTaskContext.ContextId != context.ContextId)
            throw new InvalidDataException("The continued Pause acknowledgement belongs to another Task/Run/context.");
        return result;
    }
    /// <summary>Same-owner invalidation only; signals carry no snapshot/permission grant. The
    /// borrowing view owns this actual event registration and joins its own posted projections.</summary>
    public IDisposable WatchObservedResumedTaskComposer(TaskRunOriginalInitialChatObservationLease initial,
        TaskRunOriginalResumeObservationLease resume, Action invalidated)
    {
        ArgumentNullException.ThrowIfNull(invalidated);
        if (!IsIssuedObservedTaskComposerResume(initial, resume))
            throw new UnauthorizedAccessException("The same Chat owner did not issue this continuation observation.");
        return new ComposerTaskInvalidation(resume.Issuer, resume.CanonicalTaskContext, invalidated);
    }
    private sealed class ComposerTaskInvalidation : IDisposable
    {
        private readonly TaskExecutionCoordinator _owner;
        private readonly EventHandler<TaskExecutionSnapshot> _handler;
        private int _disposed;
        internal ComposerTaskInvalidation(TaskExecutionCoordinator owner, ProviderExecutionContext same, Action invalidated)
        {
            _owner = owner;
            _handler = (_, snapshot) =>
            {
                if (Volatile.Read(ref _disposed) == 0 && snapshot.TaskId == same.TaskId &&
                    snapshot.ExecutionId == same.ExecutionId && snapshot.ContextId == same.ContextId) invalidated();
            };
            owner.SnapshotChanged += _handler;
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) _owner.SnapshotChanged -= _handler;
        }
    }
    private (TaskExecutionCoordinator Owner, ProviderExecutionContext Context) RequireObservedTaskComposerIdentity(
        TaskRunOriginalInitialChatObservationLease sameObservation)
    {
        if (!IsIssuedOriginalTaskObservation(sameObservation) || !ReferenceEquals(sameObservation.Original.Coordinator, taskCoordinator))
            throw new UnauthorizedAccessException("The same Chat owner did not issue this task identity.");
        var context = sameObservation.CurrentAcknowledgedContext
            ?? throw new InvalidOperationException("No canonical task identity was acknowledged by this original input.");
        if (context.ContextId != sameObservation.ConversationId)
            throw new InvalidDataException("The original task context changed.");
        return (sameObservation.Original.Coordinator, context);
    }
    public Task<TaskRunOriginalRunControlAvailability?> GetObservedTaskComposerControlsAsync(
        TaskRunOriginalInitialChatObservationLease sameObservation, CancellationToken token)
    {
        var same = RequireObservedTaskComposerIdentity(sameObservation);
        return ReadAsync();
        async Task<TaskRunOriginalRunControlAvailability?> ReadAsync() => !same.Owner.HasCommandAuthority ? null :
            await same.Owner.GetOriginalRunControlAvailabilityAsync(same.Context.TaskId, same.Context.ExecutionId, token).ConfigureAwait(false);
    }
    /// <summary>Request the real original producer's pause. Only its eventual result is an
    /// acknowledgement; requesting this never advertises a resumable boundary.</summary>
    public async Task<TaskRunOriginalRunControlResult> PauseObservedTaskComposerAsync(
        TaskRunOriginalInitialChatObservationLease sameObservation, CancellationToken commandToken)
    {
        var same = RequireObservedTaskComposerIdentity(sameObservation);
        await safety.EnsureMayActAsync(same.Context.ContextId, "chat.pause", commandToken).ConfigureAwait(false);
        var controls = await same.Owner.GetOriginalRunControlAvailabilityAsync(
            same.Context.TaskId, same.Context.ExecutionId, commandToken).ConfigureAwait(false);
        if (controls.CanonicalTaskContext.ContextId != sameObservation.ConversationId || !controls.CanPause)
            throw new InvalidOperationException("The original task has no supported pause operation.");
        var result = await same.Owner.PauseOriginalRunAsync(same.Context.TaskId, same.Context.ExecutionId, commandToken).ConfigureAwait(false);
        if (result.CanonicalTaskContext.TaskId != same.Context.TaskId ||
            result.CanonicalTaskContext.ExecutionId != same.Context.ExecutionId ||
            result.CanonicalTaskContext.ContextId != sameObservation.ConversationId || result.Kind != TaskRunOriginalRunControlKind.Pause)
            throw new InvalidDataException("The original pause acknowledgement belongs to another task operation.");
        return result;
    }
    public async Task<TaskRunOriginalRunControlResult> EndObservedTaskComposerPassAsync(
        TaskRunOriginalInitialChatObservationLease sameObservation, TaskRunOriginalRunControlAvailability observed,
        CancellationToken commandToken)
    {
        ArgumentNullException.ThrowIfNull(observed);
        var same = RequireObservedTaskComposerIdentity(sameObservation);
        await safety.EnsureMayActAsync(same.Context.ContextId, "chat.end-pass", commandToken).ConfigureAwait(false);
        var current = await same.Owner.GetOriginalRunControlAvailabilityAsync(same.Context.TaskId, same.Context.ExecutionId, commandToken).ConfigureAwait(false);
        if (current.CanonicalTaskContext != observed.CanonicalTaskContext || !current.CanEndPass ||
            current.CanonicalTaskContext.ContextId != sameObservation.ConversationId)
            throw new InvalidOperationException("The original paused pass changed or cannot yet be ended safely.");
        return await same.Owner.EndPausedOriginalRunAsync(observed.CanonicalTaskContext, commandToken).ConfigureAwait(false);
    }
    public async Task<TaskRunOriginalResumeObservationLease> StartObservedTaskComposerResumeAsync(
        TaskRunOriginalInitialChatObservationLease sameObservation, TaskRunOriginalRunControlAvailability observed,
        CancellationToken observationToken)
    {
        ArgumentNullException.ThrowIfNull(observed);
        var same = RequireObservedTaskComposerIdentity(sameObservation);
        await safety.EnsureMayActAsync(same.Context.ContextId, "chat.resume", observationToken).ConfigureAwait(false);
        var current = await same.Owner.GetOriginalRunControlAvailabilityAsync(same.Context.TaskId, same.Context.ExecutionId, observationToken).ConfigureAwait(false);
        if (current.CanonicalTaskContext != observed.CanonicalTaskContext ||
            current.CanonicalTaskContext.ContextId != sameObservation.ConversationId ||
            !(current.CanResumeUnstartedOriginal || current.CanResumeOriginalToolCheckpoint || current.CanResumeColdOriginal))
            throw new InvalidOperationException("The original task continuation changed or is unavailable. Refresh its controls.");
        return await same.Owner.StartObservedOriginalRunResumeAsync(same.Context.TaskId, same.Context.ExecutionId, observationToken).ConfigureAwait(false);
    }
    public bool IsIssuedObservedTaskComposerResume(TaskRunOriginalInitialChatObservationLease sameObservation,
        TaskRunOriginalResumeObservationLease resume)
    {
        if (!IsIssuedOriginalTaskObservation(sameObservation) || !ReferenceEquals(sameObservation.Original.Coordinator, taskCoordinator)) return false;
        var context = sameObservation.CurrentAcknowledgedContext;
        return context is not null && sameObservation.Original.Coordinator.IsIssuedOriginalRunResumeObservation(resume) &&
            resume.CanonicalTaskContext.TaskId == context.TaskId && resume.CanonicalTaskContext.ExecutionId == context.ExecutionId &&
            resume.CanonicalTaskContext.ContextId == sameObservation.ConversationId;
    }
    public void DemandExternalObservedTaskComposerJoin(TaskRunOriginalInitialChatObservationLease sameObservation) =>
        RequireObservedTaskComposerIdentity(sameObservation).Owner.DemandExternalOriginalProcessJoin();

}
