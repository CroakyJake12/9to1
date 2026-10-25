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
