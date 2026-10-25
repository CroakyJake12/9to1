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
}
