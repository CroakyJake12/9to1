using System.Text.Json;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class DataRecordCreationTests
{
    [Fact]
    public void Complete_record_applies_scalar_defaults_without_coercing_text_and_retains_canonical_id_on_reopen()
    {
        var (workbook, table) = EmptyTable(); var before = JsonSerializer.Serialize(workbook); var recordID = Guid.NewGuid();
        var values = new Dictionary<Guid, DataScalarRecordValue>
        { [table.Fields[0].FieldID] = new(DataCellKind.Number, JsonSerializer.SerializeToElement(1)) };
        var result = DataRecordCreation.Prepare(workbook, table.Id, recordID, 0, Guid.Empty, values);
        Assert.True(result.Success); Assert.Equal(before, JsonSerializer.Serialize(workbook));
        var created = result.Workbook!; var current = created.Tables.Single();
        Assert.Equal(recordID, Assert.Single(current.Records).RecordID);
        Assert.Equal("007", DataTableIdentity.ReadCell(created, table.Id, recordID, table.Fields[1].FieldID)!.Value);
        Assert.Equal(DataCellKind.Text, DataTableIdentity.ReadCell(created, table.Id, recordID, table.Fields[1].FieldID)!.Kind);
        Assert.Equal("true", DataTableIdentity.ReadCell(created, table.Id, recordID, table.Fields[2].FieldID)!.Value);
        Assert.Equal(DataCellKind.Boolean, DataTableIdentity.ReadCell(created, table.Id, recordID, table.Fields[2].FieldID)!.Kind);
        Assert.Equal("01:02:03", DataTableIdentity.ReadCell(created, table.Id, recordID, table.Fields[3].FieldID)!.Value);
        Assert.Null(DataTableIdentity.ReadCell(created, table.Id, recordID, table.Fields[4].FieldID));
        var reopened = JsonSerializer.Deserialize<DataWorkbook>(JsonSerializer.Serialize(created))!; reopened.Normalize();
        Assert.Equal(recordID, Assert.Single(reopened.Tables.Single().Records).RecordID);
        Assert.Empty(DataRelationalSchema.Inspect(reopened));
        var second = DataRecordCreation.Prepare(created, table.Id, Guid.NewGuid(), 0, Guid.Empty, values);
        Assert.False(second.Success); Assert.Contains(second.Issues, issue => issue.Code == "DuplicateKey");
    }
    [Fact]
    public void Missing_required_value_or_occupied_storage_keeps_original_cells_and_record_identities()
    {
        var (workbook, table) = EmptyTable(); var before = JsonSerializer.Serialize(workbook);
        Assert.False(DataRecordCreation.Prepare(workbook, table.Id, Guid.NewGuid(), 0, Guid.Empty,
            new Dictionary<Guid, DataScalarRecordValue>()).Success);
        Assert.Equal(before, JsonSerializer.Serialize(workbook));
        workbook.Sheets[0].SetCell(1, 0, "Outside-table note", kind: DataCellKind.Text);
        var occupied = JsonSerializer.Serialize(workbook);
        var values = new Dictionary<Guid, DataScalarRecordValue>
        { [table.Fields[0].FieldID] = new(DataCellKind.Number, JsonSerializer.SerializeToElement(1)) };
        var result = DataRecordCreation.Prepare(workbook, table.Id, Guid.NewGuid(), 0, Guid.Empty, values);
        Assert.False(result.Success); Assert.Contains(result.Issues, issue => issue.Code == "RecordStorageOccupied");
        Assert.Equal(occupied, JsonSerializer.Serialize(workbook)); Assert.Empty(workbook.Tables.Single().Records);
    }
    [Fact]
    public void Atomic_association_values_validate_both_refs_without_an_intermediate_blank_record()
    {
        var source = DataWorkbook.Create("Associations"); var sheet = source.Sheets[0]; sheet.SetCell(0, 0, "ID"); sheet.SetCell(1, 0, "1");
        var parent = new DataTableDefinition { Name = "People", SheetId = sheet.Id, Range = new() { EndRow = 1 } };
        DataTableIdentity.Initialize(source, parent); source.Tables.Add(parent); source.Normalize();
        var key = new DataKeyDefinition(Guid.NewGuid(), "Person ID", DataKeyKind.Primary, [parent.Fields[0].FieldID]);
        source = DataTableDesign.SetSchema(source, parent.Id, 0, Guid.Empty, null,
            [new(parent.Fields[0].FieldID, "ID", DataFieldType.Integer, false)], [key]).Workbook!;
        var endpoint = new DataJunctionEndpoint(parent.Id, key.KeyID);
        var definition = DataJunctionTableDesign.CreateDefinition(source, "Connections", endpoint, endpoint);
        source = DataJunctionTableDesign.Prepare(source, 0, Guid.Empty, definition).Workbook!;
        var before = JsonSerializer.Serialize(source); var recordID = Guid.NewGuid();
        var values = definition.FieldIDs.ToDictionary(fieldID => fieldID,
            _ => new DataScalarRecordValue(DataCellKind.Number, JsonSerializer.SerializeToElement(1)));
        var created = DataRecordCreation.Prepare(source, definition.TableID, recordID, 0, Guid.Empty, values);
        Assert.True(created.Success); Assert.Empty(DataRelationalSchema.Inspect(created.Workbook!));
        Assert.Equal(before, JsonSerializer.Serialize(source));
        values[definition.FieldIDs[1]] = new(DataCellKind.Number, JsonSerializer.SerializeToElement(999));
        var rejected = DataRecordCreation.Prepare(source, definition.TableID, recordID, 0, Guid.Empty, values);
        Assert.False(rejected.Success); Assert.Contains(rejected.Issues, issue => issue.Code == "MissingReferencedKey" && issue.ConstraintID == definition.RightRelationshipID);
        Assert.Equal(before, JsonSerializer.Serialize(source));
    }
    private static (DataWorkbook Workbook, DataTableDefinition Table) EmptyTable()
    {
        var workbook = DataWorkbook.Create("Defaults"); var sheet = workbook.Sheets[0];
        for (var column = 0; column < 5; column++) sheet.SetCell(0, column, $"Field {column + 1}");
        var table = new DataTableDefinition { SheetId = sheet.Id, Range = new() { EndColumn = 4 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table); workbook.Normalize();
        var key = new DataKeyDefinition(Guid.NewGuid(), "ID", DataKeyKind.Primary, [table.Fields[0].FieldID]);
        workbook = DataTableDesign.SetSchema(workbook, table.Id, 0, Guid.Empty, null,
            [new(table.Fields[0].FieldID, "ID", DataFieldType.Integer, false),
             new(table.Fields[1].FieldID, "Code", DataFieldType.Text, false, "007"),
             new(table.Fields[2].FieldID, "Enabled", DataFieldType.Boolean, false, "TRUE"),
             new(table.Fields[3].FieldID, "Delay", DataFieldType.Duration, false, "01:02:03"),
             new(table.Fields[4].FieldID, "Date", DataFieldType.Date, true, "")], [key]).Workbook!;
        return (workbook, workbook.Tables.Single());
    }
}
