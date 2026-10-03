using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Exact typed schema proposal for individual Home review. Capturing is not authorization.</summary>
public sealed class DataTableSchemaUpdateIntent
{
    public const string TargetAppID = "data";
    public const string ActionID = "data.table.schema.update";
    public const string ResourceKind = "data.workbook";
    private readonly JsonElement _arguments;
    private readonly byte[] _schema;
    private DataTableSchemaUpdateIntent(Guid storeID, DataWorkbook workbook, Guid tableID,
        long? expectedSchemaRevision, IReadOnlyList<DataFieldDefinition> fields, IReadOnlyList<DataKeyDefinition> keys,
        Guid operationID)
    {
        if (storeID == Guid.Empty || workbook.Id == Guid.Empty || workbook.Version < 1 || workbook.RevisionId == Guid.Empty
            || tableID == Guid.Empty || operationID == Guid.Empty) throw new ArgumentException("An exact persisted schema target is required.");
        var edited = DataTableDesign.SetSchema(workbook, tableID, workbook.Version, workbook.RevisionId,
            expectedSchemaRevision, fields, keys);
        if (!edited.Success) throw new InvalidDataException("SchemaPreviewFailed: " + edited.Issues[0].Code);
        var current = workbook.Tables.Single(table => table.Id == tableID);
        var schema = edited.Workbook!.Tables.Single(table => table.Id == tableID).RelationalSchema!;
        _schema = JsonSerializer.SerializeToUtf8Bytes(schema);
        if (_schema.Length > 1024 * 1024) throw new ArgumentException("The schema proposal exceeds 1 MiB.");
        StoreID = storeID; WorkbookID = workbook.Id; TableID = tableID; Version = workbook.Version;
        RevisionID = workbook.RevisionId; ExpectedSchemaRevision = expectedSchemaRevision; OperationID = operationID;
        _arguments = JsonSerializer.SerializeToElement(new { operation = "table.schema.update", operationID, storeID,
            workbookID = workbook.Id, version = workbook.Version, revisionID = workbook.RevisionId, tableID,
            expectedSchemaRevision, before = current.RelationalSchema, after = schema,
            recordsValidated = current.Records.Count,
            // No cell values or records are modified by this declaration-only operation.
            changedFields = schema.Fields.Where(field => current.RelationalSchema?.Fields
                .SingleOrDefault(existing => existing.FieldID == field.FieldID) is not { } prior
                || JsonSerializer.Serialize(prior) != JsonSerializer.Serialize(field)).Select(field => field.FieldID).ToArray() });
        Scopes = Array.AsReadOnly(new[] { new ResourceScope(ResourceKind, $"{storeID:D}/{workbook.Id:D}",
            workbook.RevisionId.ToString("D"), ResourceAccess.Write) });
    }
    public Guid StoreID { get; }
    public Guid WorkbookID { get; }
    public Guid TableID { get; }
    public int Version { get; }
    public Guid RevisionID { get; }
    public long? ExpectedSchemaRevision { get; }
    public Guid OperationID { get; }
    public IReadOnlyList<ResourceScope> Scopes { get; }
    public JsonElement Arguments => _arguments.Clone();
    public string PayloadSHA256 => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_arguments.GetRawText())));
    public DataTableSchema Schema => JsonSerializer.Deserialize<DataTableSchema>(_schema)!;
    public static DataTableSchemaUpdateIntent Capture(Guid storeID, DataWorkbook workbook, Guid tableID,
        long? expectedSchemaRevision, IReadOnlyList<DataFieldDefinition> fields, IReadOnlyList<DataKeyDefinition> keys,
        Guid? operationID = null) => new(storeID, workbook, tableID, expectedSchemaRevision, fields, keys, operationID ?? Guid.NewGuid());
}
