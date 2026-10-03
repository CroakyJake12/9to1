using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Reads committed operation evidence through current actual Data/Home ownership. Returning
/// a receipt proves the recorded mutation committed; it does not replay a write or complete Forms state.</summary>
public sealed class DataRecordMutationRecovery(IDataWorkbookRepository workbooks,
    IDataWorkbookCommitAuthority authority, IAuthenticatedResourceActorSource actors) : IDataRecordMutationReceiptSource
{
    internal bool IsBoundTo(IDataWorkbookRepository actualWorkbooks, IDataWorkbookCommitAuthority actualAuthority) =>
        ReferenceEquals(workbooks, actualWorkbooks) && ReferenceEquals(authority, actualAuthority);

    public async Task<DataRecordMutationReceipt?> ReadAsync(DataRecordUpdateIntent intent, CancellationToken cancellationToken = default) =>
        (await ReadCoreAsync(intent, null, cancellationToken).ConfigureAwait(false)).Receipt;

    public async Task<DataRecordMutationReceipt?> ReadAsync(DataRecordUpdateIntent intent, AuthenticatedResourceActor expectedActor,
        CancellationToken cancellationToken) => (await ObserveAsync(intent, expectedActor, cancellationToken).ConfigureAwait(false)).Receipt;

    public Task<DataRecordMutationObservation> ObserveAsync(DataRecordUpdateIntent intent, AuthenticatedResourceActor expectedActor,
        CancellationToken cancellationToken) => ReadCoreAsync(intent, expectedActor ?? throw new ArgumentNullException(nameof(expectedActor)), cancellationToken);

    private async Task<DataRecordMutationObservation> ReadCoreAsync(DataRecordUpdateIntent intent, AuthenticatedResourceActor? expectedActor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (workbooks is not IDataGuardedWorkbookRepository guarded) throw new UnauthorizedAccessException("Data guarded storage is unavailable.");
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No authenticated Data actor.");
        if (expectedActor is not null && actor != expectedActor) throw new UnauthorizedAccessException("Data receipt actor changed.");
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
        if (receipt is not null && intent.Origin is not null && receipt.SourceAdmissionVersion != 1)
            throw new InvalidDataException("DataSourceProvenanceUnverified");
        return new(receipt, new(identity.StoreId, workbook.Id, workbook.Version, workbook.RevisionId));
    }
}
