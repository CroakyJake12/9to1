using System.Text.Json;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class DataTableDesignTests
{
    [Fact]
    public void Composite_primary_key_type_comparison_rejects_duplicates_without_changing_reviewed_workbook()
    {
        var (workbook, table) = Table("Products", ["1", "01"], ["North", "North"]);
        var before = JsonSerializer.Serialize(workbook);
        var key = new DataKeyDefinition(Guid.NewGuid(), "ProductRegion", DataKeyKind.Primary,
            table.Fields.Select(field => field.FieldID).ToArray());
        var edited = DataTableDesign.SetSchema(workbook, table.Id, workbook.Version, workbook.RevisionId, null,
            [new(table.Fields[0].FieldID, "Product", DataFieldType.Integer, false),
             new(table.Fields[1].FieldID, "Region", DataFieldType.Text, false)], [key]);
        Assert.False(edited.Success);
        Assert.Contains(edited.Issues, issue => issue.Code == "DuplicateKey" && issue.ConstraintID == key.KeyID);
        Assert.Equal(before, JsonSerializer.Serialize(workbook));
    }

    [Fact]
    public void Keys_and_relationships_use_canonical_fields_and_records_after_sort_and_json_reopen()
    {
        var (workbook, parent) = Table("Tutors", ["2", "1"], ["Grace", "Ada"]);
        var parentKey = new DataKeyDefinition(Guid.NewGuid(), "TutorID", DataKeyKind.Primary, [parent.Fields[0].FieldID]);
        workbook = DataTableDesign.SetSchema(workbook, parent.Id, workbook.Version, workbook.RevisionId, null,
            [new(parent.Fields[0].FieldID, "TutorID", DataFieldType.Integer, false),
             new(parent.Fields[1].FieldID, "TutorName", DataFieldType.Text, false)], [parentKey]).Workbook!;
        var child = AddTable(workbook, "Students", ["1", "2"], ["Student A", "Student B"]);
        workbook = DataTableDesign.SetSchema(workbook, child.Id, workbook.Version, workbook.RevisionId, null,
            [new(child.Fields[0].FieldID, "TutorID", DataFieldType.Integer, false),
             new(child.Fields[1].FieldID, "Name", DataFieldType.Text, false)], []).Workbook!;
        var relation = new DataRelationshipDefinition(Guid.NewGuid(), "Student tutor", child.Id, [child.Fields[0].FieldID],
            parent.Id, parentKey.KeyID, DataRelationshipCardinality.OneToMany, false, 0);
        workbook = DataTableDesign.SetRelationship(workbook, workbook.Version, workbook.RevisionId, null, relation).Workbook!;
        var tutor = workbook.Tables.Single(table => table.Id == parent.Id);
        var parentRecordID = tutor.Records[0].RecordID;
        DataSpreadsheetOperations.SortRange(workbook.Sheets.Single(sheet => sheet.Id == tutor.SheetId), tutor.Range, 0, hasHeader: true);
        Assert.Empty(DataRelationalSchema.Inspect(workbook));
        var reopened = JsonSerializer.Deserialize<DataWorkbook>(JsonSerializer.Serialize(workbook))!;
        reopened.Normalize();
        Assert.Equal("2", DataTableIdentity.ReadCell(reopened, parent.Id, parentRecordID, parent.Fields[0].FieldID)!.Value);
        Assert.Equal(parentKey.KeyID, Assert.Single(reopened.Relationships).TargetKeyID);
        Assert.Equal(relation.RelationshipID, Assert.Single(reopened.Relationships).RelationshipID);
    }

    [Fact]
    public void Referenced_key_removal_and_missing_parent_value_are_explicit_conflicts_and_leave_source_unchanged()
    {
        var (workbook, parent) = Table("Parent", ["1"], ["One"]);
        var key = new DataKeyDefinition(Guid.NewGuid(), "ParentID", DataKeyKind.Primary, [parent.Fields[0].FieldID]);
        workbook = DataTableDesign.SetSchema(workbook, parent.Id, workbook.Version, workbook.RevisionId, null,
            [new(parent.Fields[0].FieldID, "ID", DataFieldType.Integer, false),
             new(parent.Fields[1].FieldID, "Name", DataFieldType.Text, false)], [key]).Workbook!;
        var child = AddTable(workbook, "Child", ["9"], ["Unknown parent"]);
        workbook = DataTableDesign.SetSchema(workbook, child.Id, workbook.Version, workbook.RevisionId, null,
            [new(child.Fields[0].FieldID, "ParentID", DataFieldType.Integer, false),
             new(child.Fields[1].FieldID, "Name", DataFieldType.Text, false)], []).Workbook!;
        var relation = new DataRelationshipDefinition(Guid.NewGuid(), "Parent", child.Id, [child.Fields[0].FieldID],
            parent.Id, key.KeyID, DataRelationshipCardinality.OneToMany, false, 0);
        var before = JsonSerializer.Serialize(workbook);
        var invalid = DataTableDesign.SetRelationship(workbook, workbook.Version, workbook.RevisionId, null, relation);
        Assert.Contains(invalid.Issues, issue => issue.Code == "MissingReferencedKey" && issue.ConstraintID == relation.RelationshipID);
        Assert.Equal(before, JsonSerializer.Serialize(workbook));
        workbook.Sheets.Single(sheet => sheet.Id == child.SheetId).SetCell(1, 0, "1");
        workbook = DataTableDesign.SetRelationship(workbook, workbook.Version, workbook.RevisionId, null, relation).Workbook!;
        var schema = workbook.Tables.Single(table => table.Id == parent.Id).RelationalSchema!;
        before = JsonSerializer.Serialize(workbook);
        var removed = DataTableDesign.SetSchema(workbook, parent.Id, workbook.Version, workbook.RevisionId, schema.Revision, schema.Fields, []);
        Assert.Contains(removed.Issues, issue => issue.Code == "InvalidRelationship");
        Assert.Equal(before, JsonSerializer.Serialize(workbook));
    }

    [Fact]
    public void Typed_record_edit_and_required_row_insertion_fail_before_any_canonical_state_changes()
    {
        var (workbook, table) = Table("Items", ["1", "2"], ["A", "B"]);
        var fields = new DataFieldDefinition[] { new(table.Fields[0].FieldID, "ID", DataFieldType.Integer, false),
            new(table.Fields[1].FieldID, "Name", DataFieldType.Text, false) };
        var key = new DataKeyDefinition(Guid.NewGuid(), "ID", DataKeyKind.Primary, [table.Fields[0].FieldID]);
        workbook = DataTableDesign.SetSchema(workbook, table.Id, workbook.Version, workbook.RevisionId, null, fields, [key]).Workbook!;
        table = workbook.Tables.Single(item => item.Id == table.Id);
        var before = JsonSerializer.Serialize(workbook);
        var changes = new Dictionary<Guid, DataScalarRecordValue> {
            [table.Fields[0].FieldID] = new(DataCellKind.Number, JsonSerializer.SerializeToElement(2)),
            [table.Fields[1].FieldID] = new(DataCellKind.Text, JsonSerializer.SerializeToElement("Changed")) };
        Assert.Throws<InvalidOperationException>(() => DataRecordEdits.UpdateRecord(workbook, table.Id, table.Records[0].RecordID, changes));
        Assert.Equal(before, JsonSerializer.Serialize(workbook));
        Assert.Throws<InvalidDataException>(() => DataSpreadsheetOperations.InsertRows(workbook.Sheets.Single(sheet => sheet.Id == table.SheetId), 1));
        Assert.Equal(before, JsonSerializer.Serialize(workbook));
        changes[table.Fields[0].FieldID] = new(DataCellKind.Text, JsonSerializer.SerializeToElement("3"));
        Assert.Throws<InvalidOperationException>(() => DataRecordEdits.UpdateRecord(workbook, table.Id, table.Records[0].RecordID, changes));
        Assert.Equal(before, JsonSerializer.Serialize(workbook));
    }

    [Fact]
    public void Relational_metadata_cannot_normalize_away_missing_canonical_identity()
    {
        var (workbook, table) = Table("Items", ["1"], ["A"]);
        table.RelationalSchema = new(1, 1, [new(table.Fields[0].FieldID, "ID", DataFieldType.Integer),
            new(table.Fields[1].FieldID, "Name", DataFieldType.Text)], []);
        table.RecordIdentityVersion = 0;
        Assert.Throws<InvalidDataException>(() => workbook.Normalize());
    }

    private static (DataWorkbook Workbook, DataTableDefinition Table) Table(string name, string[] ids, string[] labels)
    {
        var workbook = DataWorkbook.Create("Relationships");
        return (workbook, AddTable(workbook, name, ids, labels));
    }
    private static DataTableDefinition AddTable(DataWorkbook workbook, string name, string[] ids, string[] labels)
    {
        var sheet = DataSheet.Create(workbook.Sheets.Count, name); workbook.Sheets.Add(sheet);
        sheet.SetCell(0, 0, "ID"); sheet.SetCell(0, 1, "Name");
        for (var row = 0; row < ids.Length; row++) { sheet.SetCell(row + 1, 0, ids[row]); sheet.SetCell(row + 1, 1, labels[row]); }
        var table = new DataTableDefinition { SheetId = sheet.Id, Name = name,
            Range = new() { EndRow = ids.Length, EndColumn = 1 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table); workbook.Normalize(); return table;
    }
}
