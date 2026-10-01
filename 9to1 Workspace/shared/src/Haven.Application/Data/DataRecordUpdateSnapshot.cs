using Haven.Core;

namespace Haven.Application;

/// <summary>Serializable exact reviewed operation state retained by the owning workflow. Restoring
/// checks its payload fingerprint; this state is descriptive and never reconstructs a Home capability.</summary>
public sealed record DataRecordUpdateSnapshot(int SchemaVersion, Guid OperationID, Guid StoreID, Guid WorkbookID,
    int Version, Guid RevisionID, Guid TableID, Guid RecordID, IReadOnlyDictionary<Guid, DataScalarRecordValue> Values,
    DataRecordMutationOrigin? Origin, string PayloadSHA256)
{
    public static DataRecordUpdateSnapshot Capture(DataRecordUpdateIntent intent) => new(1, intent.OperationID, intent.StoreID,
        intent.WorkbookID, intent.Version, intent.RevisionID, intent.TableID, intent.RecordID,
        intent.Values.ToDictionary(pair => pair.Key, pair => DataRecordEdits.Capture(pair.Value)), intent.Origin, intent.PayloadSHA256);

    public DataRecordUpdateIntent Restore()
    {
        if (SchemaVersion != 1) throw new InvalidDataException("Unsupported Data record operation state.");
        var intent = DataRecordUpdateIntent.Capture(StoreID, WorkbookID, Version, RevisionID, TableID, RecordID, Values, OperationID, Origin);
        if (intent.PayloadSHA256 != PayloadSHA256) throw new InvalidDataException("Data record operation state fingerprint differs.");
        return intent;
    }
}
