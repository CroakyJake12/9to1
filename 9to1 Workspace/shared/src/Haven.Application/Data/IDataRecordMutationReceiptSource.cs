using Haven.Core;

namespace Haven.Application;

/// <summary>The trusted Data owner reads committed evidence using the same captured actor. A caller
/// cannot supply a receipt or declare a write successful through the Forms journal API.</summary>
public interface IDataRecordMutationReceiptSource
{
    Task<DataRecordMutationReceipt?> ReadAsync(DataRecordUpdateIntent intent, AuthenticatedResourceActor expectedActor,
        CancellationToken cancellationToken);
}
