using Haven.Core;

namespace Haven.Application;

/// <summary>The trusted Data owner reads committed evidence using the same captured actor. A caller
/// cannot supply a receipt or declare a write successful through the Forms journal API.</summary>
public sealed record DataRecordMutationTargetObservation(Guid StoreID, Guid WorkbookID, int Version, Guid RevisionID);
public sealed record DataRecordMutationObservation(DataRecordMutationReceipt? Receipt, DataRecordMutationTargetObservation Target);

public interface IDataRecordMutationReceiptSource
{
    Task<DataRecordMutationObservation> ObserveAsync(DataRecordUpdateIntent intent, AuthenticatedResourceActor expectedActor,
        CancellationToken cancellationToken);

    Task<DataRecordMutationReceipt?> ReadAsync(DataRecordUpdateIntent intent, AuthenticatedResourceActor expectedActor,
        CancellationToken cancellationToken);
}
