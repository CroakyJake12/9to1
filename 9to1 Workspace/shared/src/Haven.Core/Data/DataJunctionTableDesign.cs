using System.Text.Json;

namespace Haven.Core;

public sealed record DataJunctionEndpoint(Guid TableID, Guid KeyID);
public sealed record DataJunctionTableDefinition(Guid TableID, Guid SheetID, string Name, Guid PrimaryKeyID,
    Guid LeftRelationshipID, Guid RightRelationshipID, DataJunctionEndpoint Left, DataJunctionEndpoint Right,
    IReadOnlyList<Guid> FieldIDs);

/// <summary>Creates an empty canonical association table with two real foreign-key constraints.
/// Source records remain in their existing table/sheet storage; this local proposal grants no persistence.</summary>
public static class DataJunctionTableDesign
{
    public static DataJunctionTableDefinition CreateDefinition(DataWorkbook workbook, string name,
        DataJunctionEndpoint left, DataJunctionEndpoint right)
    {
        ArgumentNullException.ThrowIfNull(workbook); ArgumentNullException.ThrowIfNull(left); ArgumentNullException.ThrowIfNull(right);
        var leftKey = Key(workbook, left); var rightKey = Key(workbook, right);
        return new(Guid.NewGuid(), Guid.NewGuid(), name, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), left, right,
            Enumerable.Range(0, checked(leftKey.FieldIDs.Count + rightKey.FieldIDs.Count)).Select(_ => Guid.NewGuid()).ToArray());
    }
    public static DataTableDesignResult Prepare(DataWorkbook workbook, int expectedVersion, Guid expectedRevision,
        DataJunctionTableDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(workbook); ArgumentNullException.ThrowIfNull(definition);
        if (workbook.Version != expectedVersion || workbook.RevisionId != expectedRevision)
            return Failure("RevisionConflict", definition.TableID);
        if (definition.Left is null || definition.Right is null || definition.FieldIDs is null
            || string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Length > 256 || definition.Name != definition.Name.Trim())
            return Failure("InvalidJunctionDefinition", definition.TableID);
        if (workbook.Tables.Any(table => string.Equals(table.Name, definition.Name, StringComparison.OrdinalIgnoreCase))
            || workbook.Sheets.Any(sheet => string.Equals(sheet.Name, definition.Name, StringComparison.OrdinalIgnoreCase)))
            return Failure("JunctionNameConflict", definition.TableID);
        var beforeIssues = DataRelationalSchema.Inspect(workbook);
        if (beforeIssues.Count != 0) return new(null, beforeIssues);
        var leftTable = workbook.Tables.SingleOrDefault(table => table.Id == definition.Left.TableID);
        var rightTable = workbook.Tables.SingleOrDefault(table => table.Id == definition.Right.TableID);
        var leftKey = leftTable?.RelationalSchema?.Keys.SingleOrDefault(key => key.KeyID == definition.Left.KeyID);
        var rightKey = rightTable?.RelationalSchema?.Keys.SingleOrDefault(key => key.KeyID == definition.Right.KeyID);
        if (leftKey is null || rightKey is null) return Failure("JunctionKeyNotFound", definition.TableID);
        if (definition.FieldIDs.Count != leftKey.FieldIDs.Count + rightKey.FieldIDs.Count || definition.FieldIDs.Count > 16384)
            return Failure("JunctionFieldShapeMismatch", definition.TableID);
        var generated = definition.FieldIDs.Concat(new[] { definition.TableID, definition.SheetID, definition.PrimaryKeyID,
            definition.LeftRelationshipID, definition.RightRelationshipID }).ToArray();
        var used = workbook.Tables.SelectMany(table => table.Fields.Select(field => field.FieldID)
                .Concat(table.Records.Select(record => record.RecordID)).Concat(table.RelationalSchema?.Keys.Select(key => key.KeyID) ?? []).Append(table.Id))
            .Concat(workbook.Sheets.Select(sheet => sheet.Id)).Concat(workbook.Queries.Select(query => query.Id))
            .Concat(workbook.Relationships.Select(relationship => relationship.RelationshipID)).Append(workbook.Id).ToHashSet();
        if (generated.Any(id => id == Guid.Empty || used.Contains(id)) || generated.Distinct().Count() != generated.Length)
            return Failure("JunctionIdentityConflict", definition.TableID);
        var candidate = JsonSerializer.Deserialize<DataWorkbook>(JsonSerializer.Serialize(workbook))!;
        var fields = new List<DataFieldDefinition>();
        AppendFields(leftTable!.RelationalSchema!, leftKey, "Left");
        AppendFields(rightTable!.RelationalSchema!, rightKey, "Right");
        var sheet = new DataSheet { Id = definition.SheetID, Order = candidate.Sheets.Count, Name = definition.Name };
        for (var column = 0; column < fields.Count; column++) sheet.SetCell(0, column, fields[column].Name, kind: DataCellKind.Text);
        var table = new DataTableDefinition
        {
            Id = definition.TableID, SheetId = definition.SheetID, Name = definition.Name,
            Range = new() { EndRow = 0, EndColumn = fields.Count - 1 }, HasHeaders = true, RecordIdentityVersion = 1,
            Fields = fields.Select((field, column) => new DataTableField(field.FieldID, column)).ToList(), Records = [],
            RelationalSchema = new(1, 1, fields, [new(definition.PrimaryKeyID, "Association key", DataKeyKind.Primary, definition.FieldIDs.ToArray())]),
            Metadata = new(StringComparer.Ordinal)
            {
                ["data.junction.schemaVersion"] = "1", ["data.junction.leftRelationship"] = definition.LeftRelationshipID.ToString("D"),
                ["data.junction.rightRelationship"] = definition.RightRelationshipID.ToString("D")
            }
        };
        candidate.Sheets.Add(sheet); candidate.Tables.Add(table);
        candidate.Relationships.Add(new(definition.LeftRelationshipID, "Left reference", table.Id,
            definition.FieldIDs.Take(leftKey.FieldIDs.Count).ToArray(), leftTable.Id, leftKey.KeyID, DataRelationshipCardinality.OneToMany, false, 1));
        candidate.Relationships.Add(new(definition.RightRelationshipID, "Right reference", table.Id,
            definition.FieldIDs.Skip(leftKey.FieldIDs.Count).ToArray(), rightTable.Id, rightKey.KeyID, DataRelationshipCardinality.OneToMany, false, 1));
        var issues = DataRelationalSchema.Inspect(candidate);
        if (issues.Count != 0) return new(null, issues);
        candidate.Normalize(); return new(candidate, []);
        void AppendFields(DataTableSchema schema, DataKeyDefinition key, string side)
        {
            for (var index = 0; index < key.FieldIDs.Count; index++)
            {
                var original = schema.Fields.Single(field => field.FieldID == key.FieldIDs[index]);
                fields.Add(new(definition.FieldIDs[fields.Count], $"{side}{index + 1}", original.Type, false,
                    null, original.Categories?.ToArray()));
            }
        }
    }
    private static DataKeyDefinition Key(DataWorkbook workbook, DataJunctionEndpoint endpoint) =>
        workbook.Tables.SingleOrDefault(table => table.Id == endpoint.TableID)?.RelationalSchema?.Keys.SingleOrDefault(key => key.KeyID == endpoint.KeyID)
        ?? throw new KeyNotFoundException("JunctionKeyNotFound");
    private static DataTableDesignResult Failure(string code, Guid tableID) => new(null, [new(code, tableID)]);
}
