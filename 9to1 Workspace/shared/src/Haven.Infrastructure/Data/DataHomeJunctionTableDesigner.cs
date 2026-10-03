using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure;

/// <summary>Prepares from actual persisted Data, requests individual Home review and consumes its exact approval.
/// This explicit owner port must be mounted by the real Data host; it supplies no default DI authorization.</summary>
public sealed class DataHomeJunctionTableDesigner(IDataWorkbookRepository workbooks, IDataWorkbookCommitAuthority authority,
    IAuthenticatedResourceActorSource actors, HomeResourceOperationBroker home) : IDataJunctionTableDesigner
{
    public async Task<DataJunctionTableReview> ReviewAsync(Guid workbookID, int expectedVersion,
        Guid expectedRevision, DataJunctionTableDefinition definition, CancellationToken cancellationToken = default)
    {
        if (workbooks is not IDataGuardedWorkbookRepository guarded) throw new NotSupportedException("Data guarded storage is unavailable.");
        var workbook = await workbooks.LoadAsync(workbookID, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("WorkbookNotFound");
        if (workbook.Version != expectedVersion || workbook.RevisionId != expectedRevision)
            throw new DataWorkbookRevisionConflictException(workbookID, expectedVersion, workbook.Version);
        var identity = await guarded.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No authenticated Data actor.");
        var admission = await authority.CaptureAsync(identity.StoreId, workbookID, expectedVersion, expectedRevision,
            DataJunctionTableUpdateIntent.ActionID, actor, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Data owner admission is unavailable.");
        var intent = DataJunctionTableUpdateIntent.Capture(identity.StoreId, workbook, definition);
        if (!await admission.CheckAsync(new(identity.StoreId, workbookID, expectedVersion, expectedRevision,
            DataWorkbookCommitPhase.Publication), cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Data owner changed during junction preparation.");
        var request = await home.AuthorizeAsync(DataJunctionTableUpdateIntent.TargetAppID, DataJunctionTableUpdateIntent.ActionID,
            intent.Scopes, intent.Arguments, "Create association table", null, "data-junction-table-designer", cancellationToken).ConfigureAwait(false);
        if (request.State != HomePermissionRequestState.PendingApproval)
            throw new UnauthorizedAccessException("Home did not create an individual junction review.");
        return new(request.RequestId, intent);
    }

    public async Task<DataJunctionTableDesignerCommit> CommitAsync(DataJunctionTableReview review, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        var capability = await home.BeginExecutionCapabilityAsync(review.RequestID, review.Intent.Arguments, cancellationToken).ConfigureAwait(false);
        if (capability is null)
        {
            var receipt = await new DataJunctionTableMutationRecovery(workbooks, authority, actors)
                .ReadAsync(review.Intent, cancellationToken).ConfigureAwait(false);
            if (receipt is null) return new(false, "ApprovalRequired", null, false);
            return new(true, "DataJunctionTableAlreadyCommitted", await workbooks.LoadAsync(review.Intent.WorkbookID, cancellationToken).ConfigureAwait(false), false);
        }
        var result = await new DataHomeJunctionTableUpdateOperation(workbooks, authority, home)
            .ExecuteAsync(review.Intent, capability, cancellationToken).ConfigureAwait(false);
        if (!result.Committed) return new(false, result.Code, null, result.AuditRecorded);
        // Observe the same durable receipt through current ownership; never replay after acknowledgement failure.
        await new DataJunctionTableMutationRecovery(workbooks, authority, actors).ReadAsync(review.Intent, cancellationToken).ConfigureAwait(false);
        var workbook = await workbooks.LoadAsync(review.Intent.WorkbookID, cancellationToken).ConfigureAwait(false);
        return new(true, result.Code, workbook, result.AuditRecorded);
    }
}
