using System.Globalization;
using System.Text.Json;

namespace Haven.Core;

public sealed record DataScalarRecordValue(DataCellKind Kind, JsonElement Value);

/// <summary>Typed scalar edits of existing canonical records. Complex values and formula execution
/// require their owning capabilities; they are never silently converted into display strings.</summary>
public static class DataRecordEdits
{
    public static DataScalarRecordValue Capture(DataScalarRecordValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var json = value.Value.Clone();
        var valid = value.Kind switch
        {
            DataCellKind.Text => json.ValueKind == JsonValueKind.String && json.GetString()!.Length <= 65536,
            DataCellKind.Number => json.ValueKind == JsonValueKind.Number && json.TryGetDecimal(out _),
            DataCellKind.Boolean => json.ValueKind is JsonValueKind.True or JsonValueKind.False,
            DataCellKind.Date => json.ValueKind == JsonValueKind.String &&
                (DateOnly.TryParseExact(json.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                || DateTimeOffset.TryParseExact(json.GetString(), "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)),
            _ => false
        };
        if (!valid) throw new NotSupportedException("DataScalarValueUnavailable: the value does not match a supported canonical scalar kind.");
        return value with { Value = json };
    }

    public static void UpdateRecord(DataWorkbook workbook, Guid tableID, Guid recordID,
        IReadOnlyDictionary<Guid, DataScalarRecordValue> values)
    {
        ArgumentNullException.ThrowIfNull(workbook); ArgumentNullException.ThrowIfNull(values);
        if (values.Count is < 1 or > 256) throw new ArgumentException("Update one to 256 canonical fields.");
        var table = workbook.Tables.SingleOrDefault(table => table.Id == tableID) ?? throw new KeyNotFoundException("TableNotFound");
        DataTableIdentity.ValidateTable(table);
        var row = table.Records.SingleOrDefault(row => row.RecordID == recordID) ?? throw new KeyNotFoundException("RecordNotFound");
        var sheet = workbook.Sheets.SingleOrDefault(sheet => sheet.Id == table.SheetId) ?? throw new InvalidDataException("Table sheet is missing.");
        var fields = table.Fields.ToDictionary(field => field.FieldID);
        var prepared = new List<(int Column, DataCellKind Kind, string Text)>();
        foreach (var pair in values)
        {
            if (!fields.TryGetValue(pair.Key, out var field)) throw new KeyNotFoundException("FieldNotFound");
            var value = Capture(pair.Value);
            var existing = sheet.GetCell(row.SheetRow, field.SheetColumn);
            if (existing?.Kind == DataCellKind.Formula || !string.IsNullOrWhiteSpace(existing?.Formula))
                throw new InvalidOperationException("CalculatedFieldReadOnly");
            var text = value.Value.ValueKind == JsonValueKind.String ? value.Value.GetString()! : value.Value.GetRawText();
            if (!DataSpreadsheetOperations.ValidateValue(workbook.Validations, sheet.Id, row.SheetRow, field.SheetColumn, text).IsValid)
                throw new InvalidOperationException("DataValidationFailed");
            prepared.Add((field.SheetColumn, value.Kind, text));
        }
        // Validate the complete edit before changing any value; preserve existing cell metadata.
        foreach (var value in prepared)
        {
            var cell = sheet.GetOrCreateCell(row.SheetRow, value.Column);
            cell.Kind = value.Kind; cell.Value = value.Text; cell.Formula = string.Empty;
        }
    }
}
