using System.Text.Json;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class DataTableIdentityTests
{
    private static (DataWorkbook Workbook, DataSheet Sheet, DataTableDefinition Table) Create()
    {
        var workbook = DataWorkbook.Create("Records"); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "Name"); sheet.SetCell(0, 1, "Score");
        sheet.SetCell(1, 0, "Ada"); sheet.SetCell(1, 1, "2");
        sheet.SetCell(2, 0, "Grace"); sheet.SetCell(2, 1, "1");
        sheet.SetCell(3, 0, "Linus"); sheet.SetCell(3, 1, "2");
        var table = new DataTableDefinition { SheetId = sheet.Id, Name = "People", HasHeaders = true,
            Range = new() { EndRow = 3, EndColumn = 1 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table); workbook.Normalize();
        return (workbook, sheet, table);
    }

    [Fact]
    public void Sort_and_roundtrip_preserve_record_values_and_stable_ties()
    {
        var (workbook, sheet, table) = Create(); var ada = table.Records[0].RecordID; var name = table.Fields[0].FieldID;
        var original = table.Records.Select(record => record.RecordID).ToArray();
        DataSpreadsheetOperations.SortRange(sheet, table.Range, 1, descending: true, hasHeader: true);
        Assert.Equal(new[] { original[0], original[2], original[1] }, table.Records.Select(record => record.RecordID));
        DataSpreadsheetOperations.SortRange(sheet, table.Range, 1, descending: false, hasHeader: true);
        Assert.Equal("Ada", DataTableIdentity.ReadCell(workbook, table.Id, ada, name)!.Value);
        var loaded = JsonSerializer.Deserialize<DataWorkbook>(JsonSerializer.Serialize(workbook))!; loaded.Normalize();
        Assert.Equal("Ada", DataTableIdentity.ReadCell(loaded, table.Id, ada, name)!.Value);
        var projection = DataTableIdentity.ReadCell(loaded, table.Id, ada, name)!; projection.Value = "detached";
        Assert.Equal("Ada", DataTableIdentity.ReadCell(loaded, table.Id, ada, name)!.Value);
    }

    [Fact]
    public void Side_by_side_table_and_filter_keep_their_independent_record_bindings()
    {
        var (workbook, sheet, table) = Create();
        var other = new DataTableDefinition { SheetId = sheet.Id, Name = "Other", HasHeaders = true,
            Range = new() { StartColumn = 3, EndColumn = 3, EndRow = 3 } };
        sheet.SetCell(1, 3, "Other first"); DataTableIdentity.Initialize(workbook, other); workbook.Tables.Add(other);
        var otherRecords = other.Records.ToArray(); var records = table.Records.ToArray();
        var visible = DataSpreadsheetOperations.FilterRows(sheet, table.Range,
            [new() { Column = 0, Operator = DataFilterOperator.Equals, Value = "Ada" }]);
        Assert.Single(visible); Assert.Equal(records, table.Records);
        DataSpreadsheetOperations.SortRange(sheet, table.Range, 1, hasHeader: true);
        Assert.Equal(otherRecords, other.Records); Assert.Equal("Other first", sheet.GetCell(1, 3)!.Value);
    }

    [Fact]
    public void Structural_edits_retain_surviving_ids_and_remove_only_deleted_bindings()
    {
        var (workbook, sheet, table) = Create(); var ada = table.Records[0].RecordID; var grace = table.Records[1].RecordID;
        var name = table.Fields[0].FieldID; var score = table.Fields[1].FieldID;
        DataSpreadsheetOperations.InsertRows(sheet, 2, 2); DataSpreadsheetOperations.InsertColumns(sheet, 1, 1);
        Assert.Equal(5, table.Records.Count); Assert.Equal(3, table.Fields.Count);
        Assert.Equal("Ada", DataTableIdentity.ReadCell(workbook, table.Id, ada, name)!.Value);
        Assert.Equal("1", DataTableIdentity.ReadCell(workbook, table.Id, grace, score)!.Value);
        DataSpreadsheetOperations.DeleteRows(sheet, 4, 1); DataSpreadsheetOperations.DeleteColumns(sheet, 2, 1);
        Assert.Throws<KeyNotFoundException>(() => DataTableIdentity.ReadCell(workbook, table.Id, grace, name));
        Assert.Throws<KeyNotFoundException>(() => DataTableIdentity.ReadCell(workbook, table.Id, ada, score));
        workbook.Normalize(); Assert.Equal("Ada", DataTableIdentity.ReadCell(workbook, table.Id, ada, name)!.Value);
    }

    [Fact]
    public void Partial_sort_and_header_deletion_reject_without_mutating_cells_or_ids()
    {
        var (workbook, sheet, _) = Create(); var before = JsonSerializer.Serialize(workbook);
        Assert.Throws<InvalidOperationException>(() => DataSpreadsheetOperations.SortRange(sheet, new() { StartRow = 1, EndRow = 3 }, 0));
        Assert.Equal(before, JsonSerializer.Serialize(workbook));
        Assert.Throws<InvalidOperationException>(() => DataSpreadsheetOperations.DeleteRows(sheet, 0, 1));
        Assert.Equal(before, JsonSerializer.Serialize(workbook));
    }

    [Fact]
    public void Schema_four_legacy_read_does_not_allocate_record_ids()
    {
        var workbook = DataWorkbook.Create("Legacy"); workbook.SchemaVersion = 4;
        workbook.Tables.Add(new() { SheetId = workbook.Sheets[0].Id, Range = new() { EndRow = 3, EndColumn = 1 } });
        var tableID = workbook.Tables[0].Id; var loaded = JsonSerializer.Deserialize<DataWorkbook>(JsonSerializer.Serialize(workbook))!;
        loaded.Normalize(); Assert.Equal(5, loaded.SchemaVersion); Assert.Equal(tableID, loaded.Tables[0].Id);
        Assert.Equal(0, loaded.Tables[0].RecordIdentityVersion); Assert.Empty(loaded.Tables[0].Records); Assert.Empty(loaded.Tables[0].Fields);
    }

    [Fact]
    public void Malformed_native_identity_is_rejected_instead_of_normalized_or_dropped()
    {
        var (workbook, _, table) = Create(); table.Range.StartColumn = -1;
        Assert.Throws<InvalidDataException>(workbook.Normalize);
        Assert.Equal(-1, table.Range.StartColumn);
        table.Range.StartColumn = 0;
        workbook.Tables.Add(new() { Name = table.Name, SheetId = table.SheetId });
        Assert.Throws<InvalidDataException>(workbook.Normalize); Assert.Equal(2, workbook.Tables.Count);
    }
}
