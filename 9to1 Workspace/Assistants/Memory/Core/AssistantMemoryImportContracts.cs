using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Memory;

public enum AssistantMemoryImportState
{ Unavailable, RequiresReview, PendingApproval, Declined, Approved, Imported, AuditPending, OutcomeUnconfirmed }

/// <summary>Exact current presentation observation of the actual Home store review.
/// This observation grants no record access and cannot approve or complete a different request.</summary>
public sealed class AssistantMemoryImportPreview
{
    internal AssistantMemoryImportPreview(object issuer, object original, Guid? storeId,
        AssistantMemoryImportState state, string reason, string? requestId = null)
    { Issuer = issuer; Original = original; StoreId = storeId; State = state; Reason = reason; RequestId = requestId; }
    internal object Issuer { get; }
    internal object Original { get; }
    public Guid? StoreId { get; }
    public AssistantMemoryImportState State { get; }
    public string Reason { get; }
    public string? RequestId { get; }
    public bool CanRequest => State is AssistantMemoryImportState.RequiresReview or AssistantMemoryImportState.Declined;
    public bool CanComplete => State == AssistantMemoryImportState.Approved;
    public bool CanRetryAudit => State == AssistantMemoryImportState.AuditPending;
    public bool CanRead => State == AssistantMemoryImportState.Imported;
}

public interface IAssistantMemoryImportManagementController
{
    bool HasOriginalImportSession { get; }
    Task<AssistantMemoryImportPreview> InspectImportAsync(AssistantConversationBinding actualBinding, CancellationToken token = default);
    Task<AssistantMemoryImportPreview> RequestImportAsync(AssistantMemoryImportPreview samePreview, CancellationToken token = default);
    Task<AssistantMemoryImportPreview> RefreshImportAsync(AssistantMemoryImportPreview samePreview, CancellationToken token = default);
    Task<AssistantMemoryImportPreview> CompleteImportAsync(AssistantMemoryImportPreview samePreview, CancellationToken token = default);
    Task<AssistantMemoryImportPreview> RetryImportAuditAsync(AssistantMemoryImportPreview samePreview, CancellationToken token = default);
}
