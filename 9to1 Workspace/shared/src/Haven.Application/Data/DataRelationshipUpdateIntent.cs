using System.Security.Cryptography;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public enum DataRelationshipMutationKind { Upsert, Remove }

/// <summary>Exact relational constraint change for individual Home review. Capturing grants no mutation.</summary>
public sealed class DataRelationshipUpdateIntent
{
    public const string TargetAppID = "data";
    public const string ActionID = "data.relationships.edit";
    private readonly byte[] _relationship;
    private readonly JsonElement _arguments;
    private DataRelationshipUpdateIntent(Guid storeID, DataWorkbook workbook, DataRelationshipMutationKind kind,
        DataRelationshipDefinition relationship, long? expectedRelationshipRevision, Guid operationID)
    {
        if (storeID == Guid.Empty || workbook.Id == Guid.Empty || workbook.Version < 1 || workbook.RevisionId == Guid.Empty
            || operationID == Guid.Empty || relationship.RelationshipID == Guid.Empty || !Enum.IsDefined(kind))
            throw new ArgumentException("An exact persisted relationship target is required.");
        var before = workbook.Relationships.SingleOrDefault(item => item.RelationshipID == relationship.RelationshipID);
        var edited = kind == DataRelationshipMutationKind.Upsert
            ? DataTableDesign.SetRelationship(workbook, workbook.Version, workbook.RevisionId, expectedRelationshipRevision, relationship)
            : expectedRelationshipRevision is { } revision
                ? DataTableDesign.RemoveRelationship(workbook, workbook.Version, workbook.RevisionId, relationship.RelationshipID, revision)
                : new DataTableDesignResult(null, [new("RelationshipRevisionRequired", relationship.SourceTableID)]);
        if (!edited.Success) throw new InvalidDataException("RelationshipPreviewFailed: " + edited.Issues[0].Code);
        var after = edited.Workbook!.Relationships.SingleOrDefault(item => item.RelationshipID == relationship.RelationshipID);
        var exact = after ?? before!;
        _relationship = JsonSerializer.SerializeToUtf8Bytes(exact);
        var source = workbook.Tables.Single(table => table.Id == exact.SourceTableID);
        var target = workbook.Tables.Single(table => table.Id == exact.TargetTableID);
        StoreID = storeID; WorkbookID = workbook.Id; Version = workbook.Version; RevisionID = workbook.RevisionId;
        Kind = kind; RelationshipID = exact.RelationshipID; ExpectedRelationshipRevision = expectedRelationshipRevision; OperationID = operationID;
        _arguments = JsonSerializer.SerializeToElement(new
        {
            operation = kind == DataRelationshipMutationKind.Upsert ? "relationship.upsert" : "relationship.remove",
            operationID, storeID, workbookID = workbook.Id, version = workbook.Version, revisionID = workbook.RevisionId,
            expectedRelationshipRevision, before, after,
            source = new { source.Id, source.Name, schema = source.RelationalSchema, recordsValidated = source.Records.Count },
            target = new { target.Id, target.Name, schema = target.RelationalSchema, recordsValidated = target.Records.Count },
            recordsChanged = 0, referentialAction = "restrict"
        });
        if (System.Text.Encoding.UTF8.GetByteCount(_arguments.GetRawText()) > 1024 * 1024) throw new ArgumentException("The relationship preview exceeds 1 MiB.");
        Scopes = Array.AsReadOnly(new[] { new ResourceScope(DataTableSchemaUpdateIntent.ResourceKind,
            $"{storeID:D}/{workbook.Id:D}", workbook.RevisionId.ToString("D"), ResourceAccess.Write) });
    }
    public Guid StoreID { get; }
    public Guid WorkbookID { get; }
    public int Version { get; }
    public Guid RevisionID { get; }
    public Guid OperationID { get; }
    public Guid RelationshipID { get; }
    public long? ExpectedRelationshipRevision { get; }
    public DataRelationshipMutationKind Kind { get; }
    public DataRelationshipDefinition Relationship => JsonSerializer.Deserialize<DataRelationshipDefinition>(_relationship)!;
    public JsonElement Arguments => _arguments.Clone();
    public IReadOnlyList<ResourceScope> Scopes { get; }
    public string PayloadSHA256 => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_arguments.GetRawText())));
    public static DataRelationshipUpdateIntent Capture(Guid storeID, DataWorkbook workbook, DataRelationshipMutationKind kind,
        DataRelationshipDefinition relationship, long? expectedRelationshipRevision, Guid? operationID = null) =>
        new(storeID, workbook, kind, relationship, expectedRelationshipRevision, operationID ?? Guid.NewGuid());
}
