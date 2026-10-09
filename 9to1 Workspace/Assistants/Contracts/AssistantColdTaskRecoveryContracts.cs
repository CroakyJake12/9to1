using Haven.Application;

namespace HavenOS.Apps.Assistants.Contracts;

public enum AssistantColdTaskRecoveryState { Unavailable, NoClosedInput, ReviewAvailable }

/// <summary>Source-issued observation only. Resume independently revalidates the
/// protected journal, current domain/actor, model policy and exact persisted Task.</summary>
public sealed class AssistantColdTaskRecoveryPreview
{
    internal AssistantColdTaskRecoveryPreview(Guid taskId, Guid runId, Guid conversationId,
        long revision, AssistantColdTaskRecoveryState state, string reason)
    { TaskId = taskId; RunId = runId; ConversationId = conversationId;
        Revision = revision; State = state; Reason = reason; }
    public Guid TaskId { get; }
    public Guid RunId { get; }
    public Guid ConversationId { get; }
    public long Revision { get; }
    public AssistantColdTaskRecoveryState State { get; }
    public string Reason { get; }
    public bool CanRequestResume => State == AssistantColdTaskRecoveryState.ReviewAvailable;
}

/// <summary>Optional SAME bridge recovery consumer; no caller-supplied capsule or authority.</summary>
public interface IAssistantOriginalColdTaskRecoveryOwner
{
    Task<AssistantColdTaskRecoveryPreview> ReadOriginalColdTaskRecoveryAsync(
        AssistantConversationBinding binding, CancellationToken token = default);
    Task<TaskRunOriginalResumeObservationLease> StartObservedOriginalColdTaskResumeAsync(
        AssistantConversationBinding binding, AssistantColdTaskRecoveryPreview samePreview,
        CancellationToken token = default);
}
