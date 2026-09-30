using System.Text.Json;
using Haven.Core.Forms;
using Xunit;

namespace Haven.Core.Tests;

public sealed class FormTableInputTests
{
    [Fact]
    public void RealTypedTablePreservesRowIdentityAndRejectsTextCoercedNumbers()
    {
        var field = Guid.NewGuid(); var column = Guid.NewGuid(); var row = Guid.NewGuid();
        var definition = new FormTableInputDefinition(field, [new(column, "Amount", FormTableCellType.Number, true, 0)], 1, 10);
        var valid = new FormTableInputResponse(field, [new(row, new Dictionary<Guid, JsonElement> { [column] = JsonSerializer.SerializeToElement(10m) })]);
        Assert.Empty(FormTableInput.Validate(definition, valid));
        var invalid = valid with { Rows = [new(row, new Dictionary<Guid, JsonElement> { [column] = JsonSerializer.SerializeToElement("10") })] };
        Assert.Equal("InvalidCellTypeOrValue", Assert.Single(FormTableInput.Validate(definition, invalid)).Code);
    }

    [Fact]
    public void DuplicateConstraintUsesNumericValueAndCanonicalColumnIdentity()
    {
        var field = Guid.NewGuid(); var column = Guid.NewGuid();
        var definition = new FormTableInputDefinition(field, [new(column, "Amount", FormTableCellType.Number)], UniqueColumnIDs: [column]);
        var response = new FormTableInputResponse(field,
        [new(Guid.NewGuid(), new Dictionary<Guid, JsonElement> { [column] = JsonSerializer.SerializeToElement(10) }),
         new(Guid.NewGuid(), new Dictionary<Guid, JsonElement> { [column] = JsonSerializer.SerializeToElement(10.00m) })]);
        Assert.Equal("DuplicateRow", Assert.Single(FormTableInput.Validate(definition, response)).Code);
    }

    [Fact]
    public void FixedRowsAndRequiredColumnsCannotBeSilentlyOmitted()
    {
        var field = Guid.NewGuid(); var column = Guid.NewGuid(); var fixedRow = Guid.NewGuid();
        var definition = new FormTableInputDefinition(field, [new(column, "Name", FormTableCellType.Text, true)],
            AllowAddedRows: false, FixedRowIDs: [fixedRow]);
        var response = new FormTableInputResponse(field, [new(Guid.NewGuid(), new Dictionary<Guid, JsonElement>())]);
        var issues = FormTableInput.Validate(definition, response);
        Assert.Contains(issues, issue => issue.Code == "AddedRowsNotAllowed");
        Assert.Contains(issues, issue => issue.Code == "RequiredCell");
        Assert.Contains(issues, issue => issue.Code == "MissingFixedRow");
    }
}
