using System.Collections.ObjectModel;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Exact canonical target and typed values for one Home-reviewed record edit.</summary>
public sealed class DataRecordUpdateIntent
{
    public const string TargetAppId = "data";
    public const string ActionId = "data.records.update";
    public const string ResourceKind = "data.workbook";
    private readonly JsonElement _arguments;
    private DataRecordUpdateIntent(Guid storeID, Guid workbookID, int version, Guid revisionID, Guid tableID, Guid recordID,
        IReadOnlyDictionary<Guid, DataScalarRecordValue> values, Guid operationID, DataRecordMutationOrigin? origin)
    {
        if (operationID == Guid.Empty || origin is { } source && (source.FormID == Guid.Empty || source.FormVersionID == Guid.Empty
            || source.ResponseID == Guid.Empty || source.ResponseRevision < 1 || source.SourceStoreID == Guid.Empty)) throw new ArgumentException("A stable operation and valid source identity are required.");
        if (storeID == Guid.Empty || workbookID == Guid.Empty || revisionID == Guid.Empty || tableID == Guid.Empty
            || recordID == Guid.Empty || version < 1 || values is null || values.Count is < 1 or > 256)
            throw new ArgumentException("An exact canonical record target, persisted revision and scalar fields are required.");
        var captured = values.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key, pair => DataRecordEdits.Capture(pair.Value));
        if (captured.ContainsKey(Guid.Empty)) throw new ArgumentException("A canonical FieldID is required.");
        OperationID = operationID; Origin = origin;
        StoreID = storeID; WorkbookID = workbookID; Version = version; RevisionID = revisionID; TableID = tableID; RecordID = recordID;
        Values = new ReadOnlyDictionary<Guid, DataScalarRecordValue>(captured);
        Scopes = Array.AsReadOnly(new[] { new ResourceScope(ResourceKind, $"{storeID:D}/{workbookID:D}", revisionID.ToString("D"), ResourceAccess.Write) });
        _arguments = JsonSerializer.SerializeToElement(new { operation = "record.update", operationID, origin, storeID, workbookID, version, revisionID, tableID, recordID,
            values = captured.Select(pair => new { fieldID = pair.Key, kind = pair.Value.Kind.ToString(), value = pair.Value.Value }) });
        if (Encoding.UTF8.GetByteCount(_arguments.GetRawText()) > 65536) throw new ArgumentException("The reviewed record edit exceeds 64 KiB.");
    }
    public Guid OperationID { get; }
    public DataRecordMutationOrigin? Origin { get; }
    public string PayloadSHA256 => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_arguments.GetRawText())));
    public Guid StoreID { get; }
    public Guid WorkbookID { get; }
    public int Version { get; }
    public Guid RevisionID { get; }
    public Guid TableID { get; }
    public Guid RecordID { get; }
    public IReadOnlyDictionary<Guid, DataScalarRecordValue> Values { get; }
    public IReadOnlyList<ResourceScope> Scopes { get; }
    public JsonElement Arguments => _arguments.Clone();
    public static DataRecordUpdateIntent Capture(Guid storeID, Guid workbookID, int version, Guid revisionID, Guid tableID, Guid recordID,
        IReadOnlyDictionary<Guid, DataScalarRecordValue> values, Guid? operationID = null, DataRecordMutationOrigin? origin = null) =>
        new(storeID, workbookID, version, revisionID, tableID, recordID, values, operationID ?? Guid.NewGuid(), origin);
}
