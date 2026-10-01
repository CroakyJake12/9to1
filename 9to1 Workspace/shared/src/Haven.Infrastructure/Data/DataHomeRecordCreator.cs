using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure;

/// <summary>Prepares from actual persisted Data, requests individual Home review and consumes its exact approval.
/// This explicit owner port must be mounted by the real Data host; it supplies no default DI authorization.</summary>
public sealed class DataHomeRecordCreator(IDataWorkbookRepository workbooks, IDataWorkbookCommitAuthority authority,
    IAuthenticatedResourceActorSource actors, HomeResourceOperationBroker home) : IDataRecordCreator
{
    private sealed record Selection(DataHomeRecordCreator Issuer, Guid StoreID, Guid WorkbookID, AuthenticatedResourceActor Actor) : IDataRecordDisplaySelection;

    public async Task<DataRecordDisplaySnapshot> LoadForDisplayAsync(Guid workbookID, CancellationToken cancellationToken = default)
    {
        if (workbooks is not IDataGuardedWorkbookRepository guarded) throw new UnauthorizedAccessException("Data guarded display storage is unavailable.");
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No authenticated Data actor.");
        var root = await guarded.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (root.SchemaVersion != 1 || root.StoreId == Guid.Empty) throw new UnauthorizedAccessException("Data display identity is unavailable.");
        var selection = new Selection(this, root.StoreId, workbookID, actor);
        var workbook = await workbooks.LoadAsync(workbookID, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("WorkbookNotFound");
        if (workbook.Id != workbookID) throw new UnauthorizedAccessException("The owning display loaded a different workbook.");
        await RequireSelectionAsync(selection, workbookID, cancellationToken).ConfigureAwait(false);
        var admission = await authority.CaptureAsync(root.StoreId, workbookID, workbook.Version, workbook.RevisionId,
            DataRecordCreateIntent.ActionID, actor, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Data display ownership is unavailable.");
        if (!await admission.CheckAsync(new(root.StoreId, workbookID, workbook.Version, workbook.RevisionId,
            DataWorkbookCommitPhase.Publication), cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Data display ownership changed.");
        await RequireSelectionAsync(selection, workbookID, cancellationToken).ConfigureAwait(false);
        return new(workbook, selection);
    }

    private async Task<Selection> RequireSelectionAsync(IDataRecordDisplaySelection? display, Guid workbookID, CancellationToken token)
    {
        if (display is not Selection selection || !ReferenceEquals(selection.Issuer, this) || selection.WorkbookID != workbookID
            || workbooks is not IDataGuardedWorkbookRepository guarded)
            throw new UnauthorizedAccessException("The displayed workbook has no owning Data origin.");
        var actor = await actors.GetCurrentAsync(token).ConfigureAwait(false);
        var root = await guarded.GetStoreIdentityAsync(token).ConfigureAwait(false);
        if (actor != selection.Actor || root.SchemaVersion != 1 || root.StoreId != selection.StoreID
            || await actors.GetCurrentAsync(token).ConfigureAwait(false) != selection.Actor)
            throw new UnauthorizedAccessException("The original displayed Data actor or store changed.");
        return selection;
    }

    public Task<DataRecordCreateReview> ReviewAsync(Guid workbookID, Guid tableID, Guid recordID, int expectedVersion,
        Guid expectedRevision, IReadOnlyDictionary<Guid, DataScalarRecordValue> values, CancellationToken cancellationToken = default)
        => Task.FromException<DataRecordCreateReview>(new UnauthorizedAccessException("Load the actual owning Data display before reviewing a record."));

    public async Task<DataRecordCreateReview> ReviewAsync(IDataRecordDisplaySelection display, Guid workbookID, Guid tableID, Guid recordID, int expectedVersion,
        Guid expectedRevision, IReadOnlyDictionary<Guid, DataScalarRecordValue> values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        var capturedValues = DataRecordCreationValues.Capture(values);
        if (workbooks is not IDataGuardedWorkbookRepository guarded) throw new NotSupportedException("Data guarded storage is unavailable.");
        var selection = await RequireSelectionAsync(display, workbookID, cancellationToken).ConfigureAwait(false);
        var actor = selection.Actor;
        var workbook = await workbooks.LoadAsync(workbookID, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("WorkbookNotFound");
        if (workbook.Version != expectedVersion || workbook.RevisionId != expectedRevision)
            throw new DataWorkbookRevisionConflictException(workbookID, expectedVersion, workbook.Version);
        await RequireSelectionAsync(display, workbookID, cancellationToken).ConfigureAwait(false);
        var identity = await guarded.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (identity.SchemaVersion != 1 || identity.StoreId != selection.StoreID) throw new UnauthorizedAccessException("The original displayed Data store changed.");
        var admission = await authority.CaptureAsync(identity.StoreId, workbookID, expectedVersion, expectedRevision,
            DataRecordCreateIntent.ActionID, actor, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Data owner admission is unavailable.");
        var intent = DataRecordCreateIntent.Capture(identity.StoreId, workbook, tableID, recordID, capturedValues);
        if (!await admission.CheckAsync(new(identity.StoreId, workbookID, expectedVersion, expectedRevision,
            DataWorkbookCommitPhase.Publication), cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Data owner changed during record creation preparation.");
        await RequireSelectionAsync(display, workbookID, cancellationToken).ConfigureAwait(false);
        var request = await home.AuthorizeForActorAsync(actor, DataRecordCreateIntent.TargetAppID, DataRecordCreateIntent.ActionID,
            intent.Scopes, intent.Arguments, "Create canonical record", null, "data-record-creator", cancellationToken).ConfigureAwait(false);
        if (request.State != HomePermissionRequestState.PendingApproval)
            throw new UnauthorizedAccessException("Home did not create an individual record creation review.");
        await RequireSelectionAsync(display, workbookID, cancellationToken).ConfigureAwait(false);
        return new(request.RequestId, intent, display);
    }

    public async Task<DataRecordCreatorCommit> CommitAsync(DataRecordCreateReview review, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        var selected = await RequireSelectionAsync(review.DisplaySelection, review.Intent.WorkbookID, cancellationToken).ConfigureAwait(false);
        if (selected.StoreID != review.Intent.StoreID) throw new UnauthorizedAccessException("The reviewed store differs from the original display.");
        var capability = await home.BeginExecutionCapabilityAsync(review.RequestID, review.Intent.Arguments, cancellationToken).ConfigureAwait(false);
        if (capability is null)
        {
            await RequireSelectionAsync(review.DisplaySelection, review.Intent.WorkbookID, cancellationToken).ConfigureAwait(false);
            var receipt = await new DataRecordCreateRecovery(workbooks, authority, actors)
                .ReadAsync(review.Intent, cancellationToken).ConfigureAwait(false);
            if (receipt is null) return new(false, "ApprovalRequired", null, false);
            var retained = await LoadCommittedAsync(review.Intent, review.DisplaySelection!, cancellationToken).ConfigureAwait(false);
            return new(true, "DataRecordAlreadyCreated", retained, false, retained.Version == review.Intent.Version + 1 ? review.Intent.Calculation : null);
        }
        // The claimed owning operation carries the exact reviewed original store and Home actor
        // into every guarded lease. Let it record a rejected claim instead of abandoning a begun capability.
        var result = await new DataHomeRecordCreateOperation(workbooks, authority, home)
            .ExecuteAsync(review.Intent, capability, cancellationToken).ConfigureAwait(false);
        if (!result.Committed) return new(false, result.Code, null, result.AuditRecorded);
        // Observe the same durable receipt through current ownership; never replay after acknowledgement failure.
        await new DataRecordCreateRecovery(workbooks, authority, actors).ReadAsync(review.Intent, cancellationToken).ConfigureAwait(false);
        var workbook = await LoadCommittedAsync(review.Intent, review.DisplaySelection!, cancellationToken).ConfigureAwait(false);
        return new(true, result.Code, workbook, result.AuditRecorded, workbook.Version == review.Intent.Version + 1 ? review.Intent.Calculation : null);
    }
    private async Task<DataWorkbook> LoadCommittedAsync(DataRecordCreateIntent intent, IDataRecordDisplaySelection display, CancellationToken token)
    {
        var selection = await RequireSelectionAsync(display, intent.WorkbookID, token).ConfigureAwait(false);
        if (selection.StoreID != intent.StoreID) throw new UnauthorizedAccessException("The reviewed store differs from the original display.");
        var actor = selection.Actor;
        if (workbooks is not IDataGuardedWorkbookRepository guarded
            || (await guarded.GetStoreIdentityAsync(token).ConfigureAwait(false)).StoreId != intent.StoreID)
            throw new UnauthorizedAccessException("The actual Data store changed while observing the commit.");
        var workbook = await workbooks.LoadAsync(intent.WorkbookID, token).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("WorkbookNotFound");
        var admission = await authority.CaptureAsync(intent.StoreID, workbook.Id, workbook.Version, workbook.RevisionId,
            DataRecordCreateIntent.ActionID, actor, token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Data owner admission is unavailable.");
        if (!await admission.CheckAsync(new(intent.StoreID, workbook.Id, workbook.Version, workbook.RevisionId,
            DataWorkbookCommitPhase.Publication), token).ConfigureAwait(false)
            || await actors.GetCurrentAsync(token).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Data ownership changed while loading committed records.");
        if (DataHomeRecordCreateOperation.ReadReceipt(workbook, intent) is null)
            throw new InvalidDataException("The committed record creation evidence is unavailable.");
        await RequireSelectionAsync(display, intent.WorkbookID, token).ConfigureAwait(false);
        return workbook;
    }
}
