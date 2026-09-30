using System.Globalization;
using System.Text.Json;

namespace Haven.Core.Forms;

public enum FormTableCellType { Text, Number, Boolean, Date, Choice, Reference }
public sealed record FormTableColumn(Guid ColumnID, string Label, FormTableCellType Type,
    bool Required = false, decimal? Minimum = null, decimal? Maximum = null,
    string[]? ChoiceIDs = null, Guid? ReferencedTableID = null);
public sealed record FormTableInputDefinition(Guid FieldID, IReadOnlyList<FormTableColumn> Columns,
    int MinimumRows = 0, int MaximumRows = 1000, bool AllowAddedRows = true,
    IReadOnlyList<Guid>? FixedRowIDs = null, IReadOnlyList<Guid>? UniqueColumnIDs = null);
public sealed record FormTableResponseRow(Guid RowID, IReadOnlyDictionary<Guid, JsonElement> Cells);
public sealed record FormTableInputResponse(Guid FieldID, IReadOnlyList<FormTableResponseRow> Rows);
public sealed record FormTableValidationIssue(string Code, Guid FieldID, Guid? RowID = null, Guid? ColumnID = null);

/// <summary>Typed row/cell validation shared by preview and submitted runtime, without text coercion.</summary>
public static class FormTableInput
{
    public static void ValidateDefinition(FormTableInputDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.FieldID == Guid.Empty || definition.MinimumRows < 0 || definition.MaximumRows < definition.MinimumRows
            || definition.MaximumRows > 10000 || definition.Columns is not { Count: > 0 and <= 256 })
            throw new ArgumentException("Invalid table input identity or bounds.");
        var columns = new HashSet<Guid>();
        foreach (var column in definition.Columns)
        {
            if (column.ColumnID == Guid.Empty || !columns.Add(column.ColumnID) || string.IsNullOrWhiteSpace(column.Label)
                || !Enum.IsDefined(column.Type) || column.Minimum > column.Maximum)
                throw new ArgumentException("Invalid table column identity, type or limits.");
            if (column.Type == FormTableCellType.Choice && (column.ChoiceIDs is not { Length: > 0 }
                || column.ChoiceIDs.Any(string.IsNullOrWhiteSpace) || column.ChoiceIDs.Distinct(StringComparer.Ordinal).Count() != column.ChoiceIDs.Length))
                throw new ArgumentException("Choice columns require unique stable option IDs.");
            if (column.Type == FormTableCellType.Reference && (column.ReferencedTableID is null || column.ReferencedTableID == Guid.Empty))
                throw new ArgumentException("Reference columns require a canonical Data table ID.");
        }
        var fixedRows = definition.FixedRowIDs ?? Array.Empty<Guid>();
        if (fixedRows.Any(id => id == Guid.Empty) || fixedRows.Distinct().Count() != fixedRows.Count || fixedRows.Count > definition.MaximumRows
            || !definition.AllowAddedRows && fixedRows.Count < definition.MinimumRows)
            throw new ArgumentException("Invalid fixed row identity.");
        var unique = definition.UniqueColumnIDs ?? Array.Empty<Guid>();
        if (unique.Any(id => !columns.Contains(id)) || unique.Distinct().Count() != unique.Count)
            throw new ArgumentException("Unique constraints must reference distinct canonical column IDs.");
    }

    public static IReadOnlyList<FormTableValidationIssue> Validate(FormTableInputDefinition definition, FormTableInputResponse response)
    {
        ValidateDefinition(definition);
        ArgumentNullException.ThrowIfNull(response);
        var issues = new List<FormTableValidationIssue>();
        void Issue(string code, Guid? row = null, Guid? column = null) => issues.Add(new(code, definition.FieldID, row, column));
        if (response.FieldID != definition.FieldID) { Issue("FieldIdentityMismatch"); return issues; }
        if (response.Rows.Count < definition.MinimumRows || response.Rows.Count > definition.MaximumRows) Issue("RowCountOutOfRange");
        if (response.Rows.Count > 10000) return issues; // No unbounded processing after a rejected oversized response.
        var rowIDs = new HashSet<Guid>();
        var columns = definition.Columns.ToDictionary(column => column.ColumnID);
        var fixedRows = new HashSet<Guid>(definition.FixedRowIDs ?? Array.Empty<Guid>());
        var uniqueColumns = definition.UniqueColumnIDs ?? Array.Empty<Guid>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in response.Rows)
        {
            if (row.RowID == Guid.Empty || !rowIDs.Add(row.RowID)) Issue("InvalidRowIdentity", row.RowID);
            if (!definition.AllowAddedRows && !fixedRows.Contains(row.RowID)) Issue("AddedRowsNotAllowed", row.RowID);
            foreach (var key in row.Cells.Keys)
                if (!columns.ContainsKey(key)) Issue("UnknownColumn", row.RowID, key);
            foreach (var column in definition.Columns)
            {
                if (!row.Cells.TryGetValue(column.ColumnID, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                { if (column.Required) Issue("RequiredCell", row.RowID, column.ColumnID); continue; }
                if (!ValidCell(column, value)) Issue("InvalidCellTypeOrValue", row.RowID, column.ColumnID);
            }
            if (uniqueColumns.Count > 0 && uniqueColumns.All(id => row.Cells.TryGetValue(id, out var value) && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined))
            {
                // JSON encoding of an array removes delimiter ambiguity; canonical numbers compare by decimal value.
                var key = JsonSerializer.Serialize(uniqueColumns.Select(id => Canonical(row.Cells[id])).ToArray());
                if (!seen.Add(key)) Issue("DuplicateRow", row.RowID);
            }
        }
        foreach (var id in fixedRows)
            if (!rowIDs.Contains(id)) Issue("MissingFixedRow", id);
        return issues;
    }

    private static string Canonical(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            return "number:" + number.ToString("G29", CultureInfo.InvariantCulture);
        if (value.ValueKind == JsonValueKind.String) return "string:" + value.GetString();
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("tableID", out var table)
            && table.ValueKind == JsonValueKind.String && table.TryGetGuid(out var id)
            && value.TryGetProperty("recordID", out var record) && record.ValueKind == JsonValueKind.String)
            return "reference:" + JsonSerializer.Serialize(new[] { id.ToString("N"), record.GetString() });
        return value.ValueKind + ":" + value.GetRawText();
    }
    private static bool ValidCell(FormTableColumn column, JsonElement value) => column.Type switch
    {
        FormTableCellType.Text => value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 65536 && (!column.Required || value.GetString()!.Length > 0),
        FormTableCellType.Number => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            && (column.Minimum is null || number >= column.Minimum) && (column.Maximum is null || number <= column.Maximum),
        FormTableCellType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        FormTableCellType.Date => value.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        FormTableCellType.Choice => value.ValueKind == JsonValueKind.String && column.ChoiceIDs!.Contains(value.GetString(), StringComparer.Ordinal),
        FormTableCellType.Reference => value.ValueKind == JsonValueKind.Object && value.TryGetProperty("tableID", out var table)
            && table.ValueKind == JsonValueKind.String && table.TryGetGuid(out var tableID) && tableID == column.ReferencedTableID
            && value.TryGetProperty("recordID", out var record) && record.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(record.GetString()),
        _ => false
    };
}
