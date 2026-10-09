namespace HavenOS.Apps.Assistants.Memory;

public enum AssistantMemoryWriteState
{
    Prepared,
    Preparing,
    HomeReview,
    CommitInProgress,
    Settling,
    Saved,
    Declined,
    OutcomeUnconfirmed,
    Unavailable
}

/// <summary>A current observation of the SAME source-issued preview and retained
/// operation. ApprovalRequestId identifies the actual Home review for navigation;
/// it is never authority to read, write, approve, retry or replace the operation.
/// Saved is observed only after the actual producer driver and cleanup succeed.
/// The actual CommitAsync result remains the saved-record acknowledgment.</summary>
public sealed record AssistantMemoryWriteObservation(Guid OperationId, AssistantMemoryWriteState State,
    string? HomeApprovalRequestId, string Reason);

/// <summary>Optional pure presentation observation. This performs no Home, Den,
/// Knowledge or model I/O and admits no new source work. Keep the original preview
/// and draft while awaiting its CommitAsync; observation never starts another write.</summary>
public interface IAssistantMemoryWriteObservationSource
{
    AssistantMemoryWriteObservation ObserveOriginalWrite(AssistantMemoryWritePreview samePreview);
}
