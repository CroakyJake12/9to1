using System.Text.Json;
using Haven.Application;
using Haven.Core.Forms;

namespace Haven.Core.Tests;

public sealed class FormDataRecordUpdateProjectionTests
{
    [Fact]
    public void Submitted_numeric_answer_prepares_exact_stable_record_without_mutating_target()
    {
        var (form, response, workbook, table) = Create();
        var original = JsonSerializer.Serialize(workbook);
        var plan = FormDataRecordUpdateProjection.Prepare(form, response, Guid.NewGuid(), workbook, table.Id, table.Records[0].RecordID);
        Assert.Equal(response.ResponseID, plan.ResponseID); Assert.Equal(response.FormVersionID, plan.FormVersionID);
        Assert.Equal(workbook.RevisionId, plan.Intent.RevisionID); Assert.Equal(table.Records[0].RecordID, plan.Intent.RecordID);
        var value = Assert.Single(plan.Intent.Values);
        Assert.Equal(table.Fields[0].FieldID, value.Key); Assert.Equal(DataCellKind.Number, value.Value.Kind);
        Assert.Equal(42, value.Value.Value.GetInt32()); Assert.Equal(original, JsonSerializer.Serialize(workbook));
    }

    [Fact]
    public void Different_pinned_revision_and_unknown_record_are_rejected_before_approval()
    {
        var (form, response, workbook, table) = Create();
        Assert.Throws<InvalidDataException>(() => FormDataRecordUpdateProjection.Prepare(form with { Revision = form.Revision + 1 },
            response, Guid.NewGuid(), workbook, table.Id, table.Records[0].RecordID));
        Assert.Throws<KeyNotFoundException>(() => FormDataRecordUpdateProjection.Prepare(form, response, Guid.NewGuid(), workbook, table.Id, Guid.NewGuid()));
    }

    [Fact]
    public void Stable_binding_follows_inserted_columns_and_does_not_overwrite_calculated_cells()
    {
        var (form, response, workbook, table) = Create();
        DataSpreadsheetOperations.InsertColumns(workbook.Sheets[0], 0, 1);
        var plan = FormDataRecordUpdateProjection.Prepare(form, response, Guid.NewGuid(), workbook, table.Id, table.Records[0].RecordID);
        Assert.Equal(table.Fields[0].FieldID, Assert.Single(plan.Intent.Values).Key);
        workbook.Sheets[0].SetCell(1, 1, "2", "=1+1", DataCellKind.Formula);
        Assert.Throws<InvalidOperationException>(() => FormDataRecordUpdateProjection.Prepare(form, response, Guid.NewGuid(), workbook, table.Id, table.Records[0].RecordID));
    }

    [Fact]
    public void Append_binding_cannot_be_silently_treated_as_an_existing_record_update()
    {
        var (form, response, workbook, table) = Create();
        form = form with { DataBindings = [form.DataBindings[0] with { Kind = FormDataBindingKind.AppendRecord }] };
        Assert.Throws<NotSupportedException>(() => FormDataRecordUpdateProjection.Prepare(form, response, Guid.NewGuid(), workbook, table.Id, table.Records[0].RecordID));
    }

    private static (FormProject, FormResponse, DataWorkbook, DataTableDefinition) Create()
    {
        var workbook = DataWorkbook.Create("Responses"); workbook.Version = 3; workbook.RevisionId = Guid.NewGuid();
        var sheet = workbook.Sheets[0]; sheet.SetCell(0, 0, "Amount"); sheet.SetCell(1, 0, "1");
        var table = new DataTableDefinition { SheetId = sheet.Id, HasHeaders = true, Range = new() { EndRow = 1, EndColumn = 0 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table);
        var now = DateTimeOffset.UtcNow; var form = FormProjectEditor.Create("Survey", FormModeKind.Form, now);
        var field = new FormField(Guid.NewGuid(), FormFieldKind.Number, "Amount", null, JsonSerializer.SerializeToElement(new { }), true, new());
        form = FormProjectEditor.AddField(form, form.Revision, form.Pages[0].PageID, field, now);
        form = FormProjectEditor.BindData(form, form.Revision, new(Guid.NewGuid(), field.FieldID, workbook.Id,
            table.Id, table.Fields[0].FieldID, FormDataBindingKind.UpdateRecord), now);
        var runtime = new FormResponseRuntime(form, Guid.NewGuid());
        var answered = runtime.Answer(1, field.FieldID, JsonSerializer.SerializeToElement(42));
        Assert.True(answered.Success); var submitted = runtime.Submit(answered.Response.Revision); Assert.True(submitted.Success);
        return (form, submitted.Response, workbook, table);
    }
}
