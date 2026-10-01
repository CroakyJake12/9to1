using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Reads committed operation evidence through current actual Data/Home ownership. Returning
/// a receipt proves the recorded mutation committed; it does not replay a write or complete Forms state.</summary>
public sealed class DataRecordMutationRecovery(IDataWorkbookRepository workbooks,
    IDataWorkbookCommitAuthority authority, IAuthenticatedResourceActorSource actors)
{
    public async Task<DataRecordMutationReceipt?> ReadAsync(DataRecordUpdateIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (workbooks is not IDataGuardedWorkbookRepository guarded) throw new UnauthorizedAccessException("Data guarded storage is unavailable.");
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No authenticated Data actor.");
        var workbook = await workbooks.LoadAsync(intent.WorkbookID, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("WorkbookNotFound");
        var admission = await authority.CaptureAsync(intent.StoreID, intent.WorkbookID, workbook.Version, workbook.RevisionId,
            DataRecordUpdateIntent.ActionId, actor, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Data owner admission is unavailable.");
        var receipt = DataRecordMutationReceipts.Read(workbook).SingleOrDefault(item => item.OperationID == intent.OperationID);
        var identity = await guarded.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (!await admission.CheckAsync(new(identity.StoreId, workbook.Id, workbook.Version, workbook.RevisionId,
            DataWorkbookCommitPhase.Publication), cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Data ownership changed while reading mutation evidence.");
        if (receipt is not null && (receipt.PayloadSHA256 != intent.PayloadSHA256 || receipt.TableID != intent.TableID
            || receipt.RecordID != intent.RecordID || receipt.Origin != intent.Origin))
            throw new InvalidOperationException("DataMutationOperationConflict");
        return receipt;
    }
}
