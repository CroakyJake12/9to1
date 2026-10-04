using System.Security.Cryptography;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Sealed exact association-table creation proposal. Each generated identity is captured once,
/// previewed against actual canonical schemas, and reused by the owning commit; capture grants no write.</summary>
public sealed class DataJunctionTableUpdateIntent
{
    public const string TargetAppID = "data";
    public const string ActionID = "data.junctions.create";
    private readonly byte[] _definition;
    private readonly JsonElement _arguments;
    private DataJunctionTableUpdateIntent(Guid storeID, DataWorkbook workbook, DataJunctionTableDefinition definition, Guid operationID)
    {
        if (storeID == Guid.Empty || workbook.Id == Guid.Empty || workbook.Version < 1 || workbook.RevisionId == Guid.Empty || operationID == Guid.Empty)
            throw new ArgumentException("An exact persisted junction target is required.");
        _definition = JsonSerializer.SerializeToUtf8Bytes(definition);
        var captured = JsonSerializer.Deserialize<DataJunctionTableDefinition>(_definition)!;
        var prepared = DataJunctionTableDesign.Prepare(workbook, workbook.Version, workbook.RevisionId, captured);
        if (!prepared.Success) throw new InvalidDataException("JunctionPreviewFailed: " + prepared.Issues[0].Code);
        var candidate = prepared.Workbook!;
        var table = candidate.Tables.Single(table => table.Id == captured.TableID);
        var sheet = candidate.Sheets.Single(sheet => sheet.Id == captured.SheetID);
        var left = workbook.Tables.Single(table => table.Id == captured.Left.TableID);
        var right = workbook.Tables.Single(table => table.Id == captured.Right.TableID);
        StoreID = storeID; WorkbookID = workbook.Id; Version = workbook.Version; RevisionID = workbook.RevisionId;
        OperationID = operationID;
        _arguments = JsonSerializer.SerializeToElement(new
        {
            operation = "junction.create", operationID, storeID, workbookID = workbook.Id,
            version = workbook.Version, revisionID = workbook.RevisionId,
            definition = captured,
            left = new { left.Id, left.Name, schema = left.RelationalSchema, recordsValidated = left.Records.Count },
            right = new { right.Id, right.Name, schema = right.RelationalSchema, recordsValidated = right.Records.Count },
            createdTable = table, createdSheet = sheet,
            createdRelationships = candidate.Relationships.Where(item => item.RelationshipID == captured.LeftRelationshipID || item.RelationshipID == captured.RightRelationshipID).ToArray(),
            tablesCreated = 1, sheetsCreated = 1, relationshipsCreated = 2, recordsCreated = 0, recordsChanged = 0,
            sourceRecordsCopied = 0, referentialAction = "restrict"
        });
        if (System.Text.Encoding.UTF8.GetByteCount(_arguments.GetRawText()) > 1024 * 1024)
            throw new ArgumentException("The junction preview exceeds 1 MiB.");
        Scopes = Array.AsReadOnly(new[] { new ResourceScope(DataTableSchemaUpdateIntent.ResourceKind,
            $"{storeID:D}/{workbook.Id:D}", workbook.RevisionId.ToString("D"), ResourceAccess.Write) });
    }
    public Guid StoreID { get; }
    public Guid WorkbookID { get; }
    public int Version { get; }
    public Guid RevisionID { get; }
    public Guid OperationID { get; }
    public DataJunctionTableDefinition Definition => JsonSerializer.Deserialize<DataJunctionTableDefinition>(_definition)!;
    public JsonElement Arguments => _arguments.Clone();
    public IReadOnlyList<ResourceScope> Scopes { get; }
    public string PayloadSHA256 => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_arguments.GetRawText())));
    public static DataJunctionTableUpdateIntent Capture(Guid storeID, DataWorkbook workbook,
        DataJunctionTableDefinition definition, Guid? operationID = null) => new(storeID, workbook, definition, operationID ?? Guid.NewGuid());
}
