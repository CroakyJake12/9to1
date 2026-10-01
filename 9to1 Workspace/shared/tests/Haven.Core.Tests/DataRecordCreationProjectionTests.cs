using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class DataRecordCreationProjectionTests
{
    [Fact]
    public void Reviewed_creation_includes_actual_dependent_formula_effect_and_reuses_snapshot_clock()
    {
        var (workbook, table) = Fixture(); var sheet = workbook.Sheets.Single();
        sheet.SetCell(0, 2, "old cache", "=A2+NOW()", DataCellKind.Formula);
        var original = JsonSerializer.Serialize(workbook);
        var intent = DataRecordCreateIntent.Capture(Guid.NewGuid(), workbook, table.Id, Guid.NewGuid(),
            new Dictionary<Guid, DataScalarRecordValue>
            { [table.Fields.Single().FieldID] = new(DataCellKind.Number, JsonSerializer.SerializeToElement(7)) });
        Assert.Equal(original, JsonSerializer.Serialize(workbook));
        Assert.Equal(intent.PayloadSHA256, intent.Revalidate(workbook).PayloadSHA256);
        var effect = Assert.Single(intent.Arguments.GetProperty("formulaEffects").EnumerateArray());
        Assert.Equal("old cache", effect.GetProperty("before").GetProperty("Value").GetString());
        Assert.NotEqual("old cache", effect.GetProperty("after").GetProperty("Value").GetString());
        var projected = DataRecordCreationProjection.Prepare(workbook, table.Id, intent.RecordID,
            intent.Version, intent.RevisionID, intent.Values, intent.CalculationAt);
        Assert.True(projected.Success); Assert.Equal(1, projected.Calculation!.EvaluatedCells);
        Assert.Equal(effect.GetProperty("after").GetProperty("Value").GetString(), projected.Workbook!.Sheets.Single().GetCell(0, 2)!.Value);
    }
    [Fact]
    public void Unsupported_formula_dependency_requires_owning_calculation_capability_without_mutation()
    {
        var (workbook, table) = Fixture(); workbook.Sheets.Single().SetCell(0, 2, "cached", "=INDIRECT(\"A2\")", DataCellKind.Formula);
        var original = JsonSerializer.Serialize(workbook);
        var result = DataRecordCreationProjection.Prepare(workbook, table.Id, Guid.NewGuid(), workbook.Version,
            workbook.RevisionId, new Dictionary<Guid, DataScalarRecordValue>
            { [table.Fields.Single().FieldID] = new(DataCellKind.Number, JsonSerializer.SerializeToElement(7)) }, DateTimeOffset.UtcNow);
        Assert.False(result.Success); Assert.Contains(result.Issues, issue => issue.Code == "RecordCalculationCapabilityUnavailable");
        Assert.Equal(original, JsonSerializer.Serialize(workbook));
    }
    [Fact]
    public void Draft_distinguishes_omitted_default_from_explicit_blank_and_rejects_invalid_scalar()
    {
        var (_, table) = Fixture(); var id = table.Fields.Single().FieldID;
        table.RelationalSchema = new(1, 1, [new(id, "Code", DataFieldType.Text, DefaultValue: "007")], []);
        var omitted = DataRecordCreationDraft.Capture(table, [new(id, false, "")]);
        Assert.True(omitted.Success); Assert.Empty(omitted.Values!);
        var explicitBlank = DataRecordCreationDraft.Capture(table, [new(id, true, "")]);
        Assert.True(explicitBlank.Success); Assert.Equal("", explicitBlank.Values![id].Value.GetString());
        var literal = DataRecordCreationDraft.Capture(table, [new(id, true, "007")]);
        Assert.Equal(DataCellKind.Text, literal.Values![id].Kind); Assert.Equal("007", literal.Values[id].Value.GetString());
        table.RelationalSchema = new(1, 1, [new(id, "ID", DataFieldType.Integer)], []);
        var numericBlank = DataRecordCreationDraft.Capture(table, [new(id, true, "")]);
        Assert.True(numericBlank.Success); Assert.Equal("", numericBlank.Values![id].Value.GetString());
        var invalid = DataRecordCreationDraft.Capture(table, [new(id, true, "one")]);
        Assert.False(invalid.Success); Assert.Null(invalid.Values);
        Assert.Contains(invalid.Issues, issue => issue.FieldID == id && issue.Code == "RecordDraftScalarInvalid");
    }
    private static (DataWorkbook Workbook, DataTableDefinition Table) Fixture()
    {
        var workbook = DataWorkbook.Create("Create"); var sheet = workbook.Sheets.Single(); sheet.SetCell(0, 0, "ID");
        var table = new DataTableDefinition { Name = "Records", SheetId = sheet.Id, Range = new() { EndRow = 0 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table); workbook.Normalize();
        workbook.Version = 1; workbook.RevisionId = Guid.NewGuid(); return (workbook, table);
    }
}
