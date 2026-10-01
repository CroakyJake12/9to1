using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure;

/// <summary>Canonical Data workbook scope for the exact existing-record mutation. No Files path alias.</summary>
public sealed class DataWorkbookMutationAccessResolver(IDataWorkbookRepository workbooks,
    IDataWorkbookCommitAuthority authority) : ICanonicalResourceAccessResolver
{
    public string ResourceKind => DataRecordUpdateIntent.ResourceKind;
    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken cancellationToken)
    {
        ResourceAccessDecision Deny() => new(false, "PermissionDenied", actor.ActorId, scope.Revision, actor.OrganisationId);
        if (workbooks is not IDataGuardedWorkbookRepository || actionId != DataRecordUpdateIntent.ActionId && actionId != DataTableSchemaUpdateIntent.ActionID && actionId != DataRelationshipUpdateIntent.ActionID
            || scope.Kind != ResourceKind || scope.Access != ResourceAccess.Write) return Deny();
        var parts = scope.Id.Split('/');
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var storeID) || !Guid.TryParse(parts[1], out var workbookID)
            || !Guid.TryParse(scope.Revision, out var revisionID)) return Deny();
        try
        {
            var workbook = await workbooks.LoadAsync(workbookID, cancellationToken).ConfigureAwait(false);
            if (workbook is null || workbook.RevisionId != revisionID) return Deny();
            var admission = await authority.CaptureAsync(storeID, workbookID, workbook.Version, revisionID, actionId, actor, cancellationToken).ConfigureAwait(false);
            return admission is null ? Deny() : new(true, "Allowed", actor.ActorId, scope.Revision, actor.OrganisationId);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { return Deny(); }
    }
}

public sealed record DataRecordMutationResult(bool Committed, string Code, DataSaveResult? Saved, bool AuditRecorded);

/// <summary>Claims the exact one-use Home approval, updates existing canonical cells and carries the
/// claimed actor/root/receipt into durable Data admission. UI or Forms adapters cannot substitute a path.</summary>
public sealed class DataHomeRecordUpdateOperation(IDataWorkbookRepository workbooks,
    IDataWorkbookCommitAuthority authority, HomeResourceOperationBroker home, IDataRecordMutationOriginAuthority? origins = null)
{
    public async Task<DataRecordMutationResult> ExecuteAsync(DataRecordUpdateIntent intent,
        HomeResourceExecutionCapability capability, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capability);
        var actor = await home.ClaimExecutionAsync(capability, DataRecordUpdateIntent.TargetAppId, DataRecordUpdateIntent.ActionId,
            intent.Scopes, intent.Arguments, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Home did not authorize this exact Data record edit.");
        DataSaveResult saved;
        try
        {
            if (workbooks is not IDataGuardedWorkbookRepository guarded) throw new NotSupportedException("Data guarded commit is unavailable.");
            var workbook = await workbooks.LoadAsync(intent.WorkbookID, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("WorkbookNotFound");
            if (workbook.Version != intent.Version || workbook.RevisionId != intent.RevisionID)
                throw new DataWorkbookRevisionConflictException(intent.WorkbookID, intent.Version, workbook.Version);
            var admission = await authority.CaptureAsync(intent.StoreID, intent.WorkbookID, intent.Version, intent.RevisionID,
                DataRecordUpdateIntent.ActionId, actor, cancellationToken).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Data owner admission was revoked.");
            if (intent.Origin is not null)
            {
                if (origins is null) throw new NotSupportedException("DataOriginAuthorityUnavailable");
                var source = await origins.CaptureAsync(intent, actor, cancellationToken).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("The retained Forms source is unavailable or differs from the reviewed edit.");
                admission = new CombinedAdmission(admission, source);
            }
            if (DataRecordMutationReceipts.Read(workbook).Any(receipt => receipt.OperationID == intent.OperationID))
                throw new InvalidOperationException("DataMutationOperationAlreadyRecorded");
            DataRecordEdits.UpdateRecord(workbook, intent.TableID, intent.RecordID, intent.Values);
            DataRecordMutationReceipts.Add(workbook, new(intent.OperationID, intent.PayloadSHA256, intent.TableID, intent.RecordID,
                intent.Version, intent.RevisionID, DateTimeOffset.UtcNow, intent.Origin, intent.Origin is null ? 0 : 1));
            saved = await guarded.SaveAsync(workbook, "Home-approved canonical record update", admission, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or NotSupportedException or KeyNotFoundException or OperationCanceledException)
        {
            var code = error switch
            {
                NotSupportedException { Message: "DataOriginAuthorityUnavailable" } => "SourceAuthorityUnavailable",
                OperationCanceledException => "Cancelled", UnauthorizedAccessException => "PermissionDenied",
                DataWorkbookRevisionConflictException => "RevisionConflict",
                InvalidOperationException { Message: "DataMutationOperationAlreadyRecorded" } => "DataMutationOperationAlreadyRecorded",
                InvalidOperationException { Message: "DataMutationReceiptCapacityReached" } => "DataMutationReceiptCapacityReached",
                InvalidOperationException { Message: "DataValidationFailed" } => "DataValidationFailed",
                InvalidOperationException { Message: "CalculatedFieldReadOnly" } => "CalculatedFieldReadOnly",
                KeyNotFoundException { Message: "FieldNotFound" } => "FieldNotFound",
                KeyNotFoundException { Message: "RecordNotFound" } => "RecordNotFound",
                _ => "DataUpdateFailed"
            };
            var recorded = await CompleteAsync(capability, new(error is OperationCanceledException ? HomePermissionRequestState.Cancelled
                : HomePermissionRequestState.Failed, code, "The Data record update did not commit.", [])).ConfigureAwait(false);
            return new(false, code, null, recorded);
        }
        var audit = await CompleteAsync(capability, new(HomePermissionRequestState.Succeeded, "DataRecordUpdated",
            $"Committed workbook revision {saved.Version}.", [new("data.record", intent.RecordID.ToString("D"))])).ConfigureAwait(false);
        return new(true, audit ? "DataRecordUpdated" : "DataCommittedAuditPending", saved, audit);
    }

    private sealed class CombinedAdmission(IDataWorkbookCommitAdmission target, IDataWorkbookCommitAdmission source) : IDataWorkbookCommitAdmission
    {
        public async ValueTask<bool> CheckAsync(DataWorkbookCommitContext context, CancellationToken cancellationToken) =>
            await target.CheckAsync(context, cancellationToken).ConfigureAwait(false)
            && await source.CheckAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> CompleteAsync(HomeResourceExecutionCapability capability, HomeExecutionOutcome outcome)
    {
        try { return (await home.CompleteExecutionAsync(capability, outcome, CancellationToken.None).ConfigureAwait(false)).Succeeded; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { return false; } // A committed workbook remains committed even if its Home acknowledgement needs repair.
    }
}

public sealed class DataMutationActionPolicies : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == DataRecordUpdateIntent.TargetAppId && (actionId == DataRecordUpdateIntent.ActionId || actionId == DataTableSchemaUpdateIntent.ActionID || actionId == DataRelationshipUpdateIntent.ActionID)
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, true, false, true) : null;
}
