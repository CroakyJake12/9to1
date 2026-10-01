using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>Observes an actual committed junction receipt through current Home ownership without replaying a write.</summary>
public sealed class DataJunctionTableMutationRecovery(IDataWorkbookRepository workbooks,
    IDataWorkbookCommitAuthority authority, IAuthenticatedResourceActorSource actors)
{
    public async Task<DataJunctionTableMutationReceipt?> ReadAsync(DataJunctionTableUpdateIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (workbooks is not IDataGuardedWorkbookRepository guarded)
            throw new UnauthorizedAccessException("Data guarded storage is unavailable.");
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No authenticated Data actor.");
        var workbook = await workbooks.LoadAsync(intent.WorkbookID, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("WorkbookNotFound");
        var admission = await authority.CaptureAsync(intent.StoreID, intent.WorkbookID, workbook.Version,
            workbook.RevisionId, DataJunctionTableUpdateIntent.ActionID, actor, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Data owner admission is unavailable.");
        var identity = await guarded.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (!await admission.CheckAsync(new(identity.StoreId, workbook.Id, workbook.Version, workbook.RevisionId,
            DataWorkbookCommitPhase.Publication), cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Data ownership changed while reading junction evidence.");
        return DataHomeJunctionTableUpdateOperation.ReadReceipt(workbook, intent);
    }
}
