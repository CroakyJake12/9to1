using System.Text.Json;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class DataRecordEditsTests
{
    [Fact]
    public void Complete_record_edit_is_validated_before_any_cell_changes()
    {
        var (workbook, table) = Create(); var sheet = workbook.Sheets[0];
        sheet.SetCell(1, 1, "2", "=1+1", DataCellKind.Formula);
        var before = JsonSerializer.Serialize(workbook);
        Assert.Throws<InvalidOperationException>(() => DataRecordEdits.UpdateRecord(workbook, table.Id, table.Records[0].RecordID,
            new Dictionary<Guid, DataScalarRecordValue>
            {
                [table.Fields[0].FieldID] = new(DataCellKind.Text, JsonSerializer.SerializeToElement("Would change")),
                [table.Fields[1].FieldID] = new(DataCellKind.Number, JsonSerializer.SerializeToElement(3))
            }));
        Assert.Equal(before, JsonSerializer.Serialize(workbook));
    }

    [Fact]
    public void Typed_values_preserve_field_record_identity_and_existing_cell_metadata()
    {
        var (workbook, table) = Create(); var sheet = workbook.Sheets[0]; var rowID = table.Records[0].RecordID;
        var fieldID = table.Fields[0].FieldID; sheet.GetCell(1, 0)!.Metadata["note"] = "retain";
        DataRecordEdits.UpdateRecord(workbook, table.Id, rowID, new Dictionary<Guid, DataScalarRecordValue>
        { [fieldID] = new(DataCellKind.Boolean, JsonSerializer.SerializeToElement(true)) });
        var cell = DataTableIdentity.ReadCell(workbook, table.Id, rowID, fieldID)!;
        Assert.Equal(DataCellKind.Boolean, cell.Kind); Assert.Equal("true", cell.Value); Assert.Equal("retain", cell.Metadata["note"]);
    }

    [Theory]
    [InlineData("{\"nested\":1}")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    [InlineData("\"42\"")]
    public void Unsupported_or_mismatched_values_are_not_stringified_into_numeric_fields(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Throws<NotSupportedException>(() => DataRecordEdits.Capture(new(DataCellKind.Number, document.RootElement)));
    }

    private static (DataWorkbook, DataTableDefinition) Create()
    {
        var workbook = DataWorkbook.Create("Typed records"); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "Name"); sheet.SetCell(0, 1, "Value"); sheet.SetCell(1, 0, "Original"); sheet.SetCell(1, 1, "1");
        var table = new DataTableDefinition { SheetId = sheet.Id, HasHeaders = true, Range = new() { EndRow = 1, EndColumn = 1 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table); return (workbook, table);
    }
}
