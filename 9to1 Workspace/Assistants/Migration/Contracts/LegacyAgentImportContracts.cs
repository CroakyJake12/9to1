namespace HavenOS.Apps.Assistants.Migration;

public enum LegacyAgentImportState
{
    Unavailable, RequiresReview, PendingApproval, Declined, Approved, Imported, AuditPending, OutcomeUnconfirmed
}

/// <summary>Live preview issued by this migration presentation over its configured
/// original source and actual Home import owner. Observed IDs never grant access.</summary>
public sealed class LegacyAgentImportPreview
{
    internal LegacyAgentImportPreview(object issuer, object original, Guid? storeId, LegacyAgentImportState state,
        string reason, string? requestId = null)
    { Issuer = issuer; Original = original; StoreId = storeId; State = state; Reason = reason; RequestId = requestId; }
    internal object Issuer { get; }
    internal object Original { get; }
    public Guid? StoreId { get; }
    public string SourceLabel => "Configured saved agents on this device";
    public LegacyAgentImportState State { get; }
    public string Reason { get; }
    public string? RequestId { get; }
    public bool CanRequest => State is LegacyAgentImportState.RequiresReview or LegacyAgentImportState.Declined;
    public bool CanComplete => State == LegacyAgentImportState.Approved;
    public bool CanRetryAudit => State == LegacyAgentImportState.AuditPending;
    public bool CanBrowse => State == LegacyAgentImportState.Imported;
}

/// <summary>Optional first-use workflow on the SAME migration controller. It never
/// approves a Home request. Confirmation consumes the exact owner-issued preview.</summary>
public interface ILegacyAgentMigrationImportController
{
    Task<LegacyAgentImportPreview> InspectImportAsync(CancellationToken token = default);
    Task<LegacyAgentImportPreview> RequestImportAsync(LegacyAgentImportPreview samePreview, CancellationToken token = default);
    Task<LegacyAgentImportPreview> RefreshImportAsync(LegacyAgentImportPreview samePreview, CancellationToken token = default);
    Task<LegacyAgentImportPreview> CompleteImportAsync(LegacyAgentImportPreview samePreview, CancellationToken token = default);
    Task<LegacyAgentImportPreview> RetryImportAuditAsync(LegacyAgentImportPreview samePreview, CancellationToken token = default);
}
