using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class DataRelationshipGraphProjectionTests
{
    [Fact]
    public void Legal_self_reference_projects_canonical_field_key_and_relation_ids_without_a_second_schema()
    {
        var workbook = DataWorkbook.Create("Employees"); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "ID"); sheet.SetCell(0, 1, "Manager");
        sheet.SetCell(1, 0, "1"); sheet.SetCell(1, 1, "2"); sheet.SetCell(2, 0, "2"); sheet.SetCell(2, 1, "1");
        var table = new DataTableDefinition { SheetId = sheet.Id, Name = "Employees", Range = new() { EndRow = 2, EndColumn = 1 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table); workbook.Normalize();
        var key = new DataKeyDefinition(Guid.NewGuid(), "Employee ID", DataKeyKind.Primary, [table.Fields[0].FieldID]);
        workbook = DataTableDesign.SetSchema(workbook, table.Id, 0, Guid.Empty, null,
            [new(table.Fields[0].FieldID, "ID", DataFieldType.Integer, false),
             new(table.Fields[1].FieldID, "Manager", DataFieldType.Integer, false)], [key]).Workbook!;
        var relation = new DataRelationshipDefinition(Guid.NewGuid(), "Manager", table.Id, [table.Fields[1].FieldID],
            table.Id, key.KeyID, DataRelationshipCardinality.OneToMany, false, 0);
        workbook = DataTableDesign.SetRelationship(workbook, 0, Guid.Empty, null, relation).Workbook!;
        var before = JsonSerializer.Serialize(workbook);
        var projected = DataRelationshipGraphProjection.Capture(workbook);
        Assert.Empty(projected.Schema.Validate(projected.Graph));
        var node = Assert.Single(projected.Graph.Nodes); Assert.Equal(table.Id, node.NodeId);
        Assert.Contains(node.Ports, port => port.PortId == table.Fields[1].FieldID);
        Assert.Contains(node.Ports, port => port.PortId == key.KeyID);
        var edge = Assert.Single(projected.Graph.Connections); Assert.Equal(relation.RelationshipID, edge.ConnectionId);
        Assert.Equal(table.Fields[1].FieldID, edge.OutputPortId); Assert.Equal(key.KeyID, edge.InputPortId);
        Assert.Equal(before, JsonSerializer.Serialize(workbook));
        var reopened = JsonSerializer.Deserialize<DataWorkbook>(before)!; reopened.Normalize();
        Assert.Equal(JsonSerializer.Serialize(projected.Graph), JsonSerializer.Serialize(DataRelationshipGraphProjection.Capture(reopened).Graph));
    }
}
