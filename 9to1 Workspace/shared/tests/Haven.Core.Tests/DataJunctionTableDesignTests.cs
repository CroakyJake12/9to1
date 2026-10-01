using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class DataJunctionTableDesignTests
{
    [Fact]
    public void Composite_junction_preserves_sources_and_enforces_both_foreign_keys_and_unique_associations()
    {
        var (workbook, left, right) = Source(); var before = JsonSerializer.Serialize(workbook);
        var definition = DataJunctionTableDesign.CreateDefinition(workbook, "Assignments", left, right);
        var prepared = DataJunctionTableDesign.Prepare(workbook, 0, Guid.Empty, definition);
        Assert.True(prepared.Success); var candidate = prepared.Workbook!;
        Assert.Equal(before, JsonSerializer.Serialize(workbook));
        Assert.Equal(3, candidate.Tables.Count); Assert.Equal(2, candidate.Relationships.Count);
        var junction = candidate.Tables.Single(table => table.Id == definition.TableID);
        Assert.Empty(junction.Records); Assert.Equal(3, junction.Fields.Count);
        Assert.Equal(definition.FieldIDs, junction.RelationalSchema!.Keys.Single().FieldIDs);
        Assert.All(candidate.Tables.Where(table => table.Id != junction.Id), table =>
            Assert.Equal(JsonSerializer.Serialize(workbook.Tables.Single(original => original.Id == table.Id)), JsonSerializer.Serialize(table)));
        Assert.Equal(JsonSerializer.Serialize(workbook.Sheets[0]), JsonSerializer.Serialize(candidate.Sheets[0]));
        var graph = DataRelationshipGraphProjection.Capture(candidate); Assert.Equal(3, graph.Graph.Nodes.Count); Assert.Equal(2, graph.Graph.Connections.Count);
        var reopened = JsonSerializer.Deserialize<DataWorkbook>(JsonSerializer.Serialize(candidate))!; reopened.Normalize();
        Assert.Equal(JsonSerializer.Serialize(graph.Graph), JsonSerializer.Serialize(DataRelationshipGraphProjection.Capture(reopened).Graph));
        var sheet = candidate.Sheets.Single(sheet => sheet.Id == definition.SheetID);
        var recordID = Guid.NewGuid(); junction.Range.EndRow = 1; junction.Records.Add(new(recordID, 1));
        sheet.SetCell(1, 0, "1", kind: DataCellKind.Number); sheet.SetCell(1, 1, "10", kind: DataCellKind.Number);
        sheet.SetCell(1, 2, "007", kind: DataCellKind.Text);
        Assert.Empty(DataRelationalSchema.Inspect(candidate));
        sheet.SetCell(1, 2, "999", kind: DataCellKind.Text);
        Assert.Contains(DataRelationalSchema.Inspect(candidate), issue => issue.Code == "MissingReferencedKey"
            && issue.RecordID == recordID && issue.ConstraintID == definition.RightRelationshipID);
        sheet.SetCell(1, 2, "007", kind: DataCellKind.Text);
        junction.Range.EndRow = 2; junction.Records.Add(new(Guid.NewGuid(), 2));
        sheet.SetCell(2, 0, "1", kind: DataCellKind.Number); sheet.SetCell(2, 1, "10", kind: DataCellKind.Number); sheet.SetCell(2, 2, "007", kind: DataCellKind.Text);
        Assert.Contains(DataRelationalSchema.Inspect(candidate), issue => issue.Code == "DuplicateKey" && issue.ConstraintID == definition.PrimaryKeyID);
    }
    [Fact]
    public void Junction_identity_or_name_collision_and_stale_preview_do_not_mutate_source()
    {
        var (workbook, left, right) = Source(); var before = JsonSerializer.Serialize(workbook);
        var definition = DataJunctionTableDesign.CreateDefinition(workbook, "Assignments", left, right);
        Assert.False(DataJunctionTableDesign.Prepare(workbook, 1, Guid.Empty, definition).Success);
        Assert.False(DataJunctionTableDesign.Prepare(workbook, 0, Guid.Empty, definition with { TableID = left.TableID }).Success);
        Assert.False(DataJunctionTableDesign.Prepare(workbook, 0, Guid.Empty, definition with { Name = workbook.Tables[0].Name }).Success);
        Assert.False(DataJunctionTableDesign.Prepare(workbook, 0, Guid.Empty, definition with { FieldIDs = definition.FieldIDs.Select(_ => Guid.Empty).ToArray() }).Success);
        Assert.Equal(before, JsonSerializer.Serialize(workbook));
    }
    private static (DataWorkbook Workbook, DataJunctionEndpoint Left, DataJunctionEndpoint Right) Source()
    {
        var workbook = DataWorkbook.Create("Junction"); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "Tenant"); sheet.SetCell(0, 1, "Student"); sheet.SetCell(0, 3, "Course");
        sheet.SetCell(1, 0, "1"); sheet.SetCell(1, 1, "10"); sheet.SetCell(2, 0, "1"); sheet.SetCell(2, 1, "20");
        sheet.SetCell(1, 3, "007", kind: DataCellKind.Text); sheet.SetCell(2, 3, "008", kind: DataCellKind.Text);
        var left = new DataTableDefinition { SheetId = sheet.Id, Name = "Students", Range = new() { EndRow = 2, EndColumn = 1 } };
        var right = new DataTableDefinition { SheetId = sheet.Id, Name = "Courses", Range = new() { StartColumn = 3, EndColumn = 3, EndRow = 2 } };
        DataTableIdentity.Initialize(workbook, left); workbook.Tables.Add(left); DataTableIdentity.Initialize(workbook, right); workbook.Tables.Add(right); workbook.Normalize();
        var leftKey = new DataKeyDefinition(Guid.NewGuid(), "Student key", DataKeyKind.Primary, left.Fields.Select(field => field.FieldID).ToArray());
        var rightKey = new DataKeyDefinition(Guid.NewGuid(), "Course key", DataKeyKind.Primary, right.Fields.Select(field => field.FieldID).ToArray());
        workbook = DataTableDesign.SetSchema(workbook, left.Id, 0, Guid.Empty, null,
            [new(left.Fields[0].FieldID, "Tenant", DataFieldType.Integer, false), new(left.Fields[1].FieldID, "Student", DataFieldType.Integer, false)], [leftKey]).Workbook!;
        workbook = DataTableDesign.SetSchema(workbook, right.Id, 0, Guid.Empty, null,
            [new(right.Fields[0].FieldID, "Course", DataFieldType.Text, false)], [rightKey]).Workbook!;
        return (workbook, new(left.Id, leftKey.KeyID), new(right.Id, rightKey.KeyID));
    }
}
