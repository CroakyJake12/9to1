using System.Text.Json;

namespace Haven.Core;

public sealed record DataTableDesignResult(DataWorkbook? Workbook, IReadOnlyList<DataSchemaIssue> Issues)
{
    public bool Success => Workbook is not null && Issues.Count == 0;
}

/// <summary>Local authored schema edits over the existing workbook aggregate. The returned candidate
/// still requires the owning repository's current permission, revision and atomic publication checks.</summary>
public static class DataTableDesign
{
    public static DataTableDesignResult SetSchema(DataWorkbook workbook, Guid tableID, int expectedWorkbookVersion,
        Guid expectedWorkbookRevision, long? expectedSchemaRevision, IReadOnlyList<DataFieldDefinition> fields,
        IReadOnlyList<DataKeyDefinition> keys)
    {
        ArgumentNullException.ThrowIfNull(workbook); ArgumentNullException.ThrowIfNull(fields); ArgumentNullException.ThrowIfNull(keys);
        if (workbook.Version != expectedWorkbookVersion || workbook.RevisionId != expectedWorkbookRevision)
            return new(null, [new("RevisionConflict", tableID)]);
        var current = workbook.Tables.SingleOrDefault(table => table.Id == tableID);
        if (current is null) return new(null, [new("TableNotFound", tableID)]);
        if (current.RelationalSchema?.Revision != expectedSchemaRevision)
            return new(null, [new("SchemaRevisionConflict", tableID)]);
        if (current.RecordIdentityVersion != 1)
            return new(null, [new("RecordIdentityRequired", tableID)]);
        if (expectedSchemaRevision == long.MaxValue) return new(null, [new("SchemaRevisionExhausted", tableID)]);
        var candidate = JsonSerializer.Deserialize<DataWorkbook>(JsonSerializer.Serialize(workbook))!;
        // Capture nested caller-owned lists as part of the immutable editing snapshot.
        var schema = new DataTableSchema(1, checked((expectedSchemaRevision ?? 0) + 1), fields, keys);
        candidate.Tables.Single(table => table.Id == tableID).RelationalSchema =
            JsonSerializer.Deserialize<DataTableSchema>(JsonSerializer.Serialize(schema))!;
        var issues = DataRelationalSchema.Inspect(candidate);
        if (issues.Count != 0) return new(null, issues);
        candidate.Normalize();
        return new(candidate, []);
    }

    public static DataTableDesignResult RemoveRelationship(DataWorkbook workbook, int expectedWorkbookVersion,
        Guid expectedWorkbookRevision, Guid relationshipID, long expectedRelationshipRevision)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        if (workbook.Version != expectedWorkbookVersion || workbook.RevisionId != expectedWorkbookRevision)
            return new(null, [new("RevisionConflict", Guid.Empty, ConstraintID: relationshipID)]);
        var current = workbook.Relationships.SingleOrDefault(item => item.RelationshipID == relationshipID);
        if (current is null) return new(null, [new("RelationshipNotFound", Guid.Empty, ConstraintID: relationshipID)]);
        if (current.Revision != expectedRelationshipRevision)
            return new(null, [new("RelationshipRevisionConflict", current.SourceTableID, ConstraintID: relationshipID)]);
        var candidate = JsonSerializer.Deserialize<DataWorkbook>(JsonSerializer.Serialize(workbook))!;
        candidate.Relationships = candidate.Relationships.Where(item => item.RelationshipID != relationshipID).ToList();
        var issues = DataRelationalSchema.Inspect(candidate);
        if (issues.Count != 0) return new(null, issues);
        candidate.Normalize(); return new(candidate, []);
    }

    public static DataTableDesignResult SetRelationship(DataWorkbook workbook, int expectedWorkbookVersion,
        Guid expectedWorkbookRevision, long? expectedRelationshipRevision, DataRelationshipDefinition relationship)
    {
        ArgumentNullException.ThrowIfNull(workbook); ArgumentNullException.ThrowIfNull(relationship);
        if (workbook.Version != expectedWorkbookVersion || workbook.RevisionId != expectedWorkbookRevision)
            return new(null, [new("RevisionConflict", relationship.SourceTableID)]);
        var current = workbook.Relationships.SingleOrDefault(item => item.RelationshipID == relationship.RelationshipID);
        if (current?.Revision != expectedRelationshipRevision)
            return new(null, [new("RelationshipRevisionConflict", relationship.SourceTableID, ConstraintID: relationship.RelationshipID)]);
        if (expectedRelationshipRevision == long.MaxValue)
            return new(null, [new("RelationshipRevisionExhausted", relationship.SourceTableID, ConstraintID: relationship.RelationshipID)]);
        var candidate = JsonSerializer.Deserialize<DataWorkbook>(JsonSerializer.Serialize(workbook))!;
        var edited = relationship with { Revision = checked((expectedRelationshipRevision ?? 0) + 1) };
        var captured = JsonSerializer.Deserialize<DataRelationshipDefinition>(JsonSerializer.Serialize(edited))!;
        candidate.Relationships = candidate.Relationships.Where(item => item.RelationshipID != relationship.RelationshipID)
            .Append(captured).ToList();
        var issues = DataRelationalSchema.Inspect(candidate);
        if (issues.Count != 0) return new(null, issues);
        candidate.Normalize();
        return new(candidate, []);
    }
}
