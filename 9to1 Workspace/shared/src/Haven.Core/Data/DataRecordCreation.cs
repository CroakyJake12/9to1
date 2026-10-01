using System.Globalization;
using System.Text.Json;

namespace Haven.Core;

/// <summary>Creates one complete canonical record atomically, applying authored scalar defaults.
/// It extends into vacant storage only, moves no existing records, and grants no repository write.</summary>
public static class DataRecordCreation
{
    public static DataTableDesignResult Prepare(DataWorkbook workbook, Guid tableID, Guid recordID,
        int expectedVersion, Guid expectedRevision, IReadOnlyDictionary<Guid, DataScalarRecordValue> values)
    {
        ArgumentNullException.ThrowIfNull(workbook); ArgumentNullException.ThrowIfNull(values);
        if (workbook.Version != expectedVersion || workbook.RevisionId != expectedRevision)
            return Failure("RevisionConflict", tableID, recordID);
        var table = workbook.Tables.SingleOrDefault(table => table.Id == tableID);
        if (table is null) return Failure("TableNotFound", tableID, recordID);
        if (table.RecordIdentityVersion != 1) return Failure("RecordIdentityRequired", tableID, recordID);
        if (table.Fields.Count > 256) return Failure("RecordCreationFieldCapacity", tableID, recordID);
        Dictionary<Guid, DataScalarRecordValue> captured;
        try { captured = DataRecordCreationValues.Capture(values); }
        catch (DataRecordCreationValues.CapacityExceededException) { return Failure("RecordCreationFieldCapacity", tableID, recordID); }
        var existingIssues = DataRelationalSchema.Inspect(workbook);
        if (existingIssues.Count != 0) return new(null, existingIssues);
        var identities = workbook.Tables.SelectMany(item => item.Fields.Select(field => field.FieldID)
                .Concat(item.Records.Select(record => record.RecordID)).Concat(item.RelationalSchema?.Keys.Select(key => key.KeyID) ?? []).Append(item.Id))
            .Concat(workbook.Relationships.Select(relation => relation.RelationshipID)).Concat(workbook.Sheets.Select(sheet => sheet.Id)).Append(workbook.Id);
        if (recordID == Guid.Empty || identities.Contains(recordID)) return Failure("RecordIdentityConflict", tableID, recordID);
        if (table.Records.Count >= 1_000_000 || table.Range.EndRow >= int.MaxValue - 1)
            return Failure("RecordCreationCapacity", tableID, recordID);
        if (captured.Keys.Any(id => table.Fields.All(field => field.FieldID != id))) return Failure("FieldNotFound", tableID, recordID);
        var row = table.Range.EndRow + 1;
        var obstruction = workbook.Tables.FirstOrDefault(other => other.Id != tableID && other.SheetId == table.SheetId
            && other.Range.StartRow <= row && other.Range.EndRow >= row
            && other.Range.StartColumn <= table.Range.EndColumn && other.Range.EndColumn >= table.Range.StartColumn);
        if (obstruction is not null) return new(null, [new("RecordStorageConflict", tableID, RecordID: recordID, ConstraintID: obstruction.Id)]);
        var originalSheet = workbook.Sheets.Single(sheet => sheet.Id == table.SheetId);
        if (originalSheet.Cells.Any(cell => cell.Row == row && cell.Column >= table.Range.StartColumn && cell.Column <= table.Range.EndColumn
            && (cell.Value.Length != 0 || cell.Formula.Length != 0 || cell.Kind == DataCellKind.Formula)))
            return Failure("RecordStorageOccupied", tableID, recordID);
        foreach (var field in table.RelationalSchema?.Fields ?? [])
            if (!captured.ContainsKey(field.FieldID) && field.DefaultValue is { Length: > 0 } defaultValue)
                captured.Add(field.FieldID, Default(field, defaultValue));
        var candidate = JsonSerializer.Deserialize<DataWorkbook>(JsonSerializer.Serialize(workbook))!;
        var target = candidate.Tables.Single(item => item.Id == tableID);
        var sheet = candidate.Sheets.Single(item => item.Id == target.SheetId);
        target.Range.EndRow = row; target.Records.Add(new(recordID, row));
        foreach (var field in target.Fields)
        {
            captured.TryGetValue(field.FieldID, out var value);
            var text = value is null ? string.Empty : value.Value.ValueKind == JsonValueKind.String ? value.Value.GetString()! : value.Value.GetRawText();
            if (!DataSpreadsheetOperations.ValidateValue(candidate.Validations, sheet.Id, row, field.SheetColumn, text).IsValid)
                return new(null, [new("DataValidationFailed", tableID, field.FieldID, recordID)]);
            if (value is not null) sheet.SetCell(row, field.SheetColumn, text, kind: value.Kind);
        }
        var issues = DataRelationalSchema.Inspect(candidate);
        if (issues.Count != 0) return new(null, issues);
        candidate.Normalize(); return new(candidate, []);
    }
    private static DataScalarRecordValue Default(DataFieldDefinition field, string text)
    {
        var value = field.Type switch
        {
            DataFieldType.Integer => new DataScalarRecordValue(DataCellKind.Number,
                JsonSerializer.SerializeToElement(long.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture))),
            DataFieldType.Decimal or DataFieldType.Currency => new(DataCellKind.Number,
                JsonSerializer.SerializeToElement(decimal.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture))),
            DataFieldType.Boolean => new(DataCellKind.Boolean, JsonSerializer.SerializeToElement(bool.Parse(text))),
            DataFieldType.Date or DataFieldType.DateTime => new(DataCellKind.Date, JsonSerializer.SerializeToElement(text)),
            DataFieldType.Text or DataFieldType.Duration or DataFieldType.Uuid or DataFieldType.Category => new(DataCellKind.Text, JsonSerializer.SerializeToElement(text)),
            _ => throw new NotSupportedException("RecordDefaultCapabilityUnavailable")
        };
        return DataRecordEdits.Capture(value);
    }
    private static DataTableDesignResult Failure(string code, Guid tableID, Guid recordID) =>
        new(null, [new(code, tableID, RecordID: recordID)]);
}
