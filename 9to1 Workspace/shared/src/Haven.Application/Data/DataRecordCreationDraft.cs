using System.Globalization;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public sealed record DataRecordDraftField(Guid FieldID, bool Supplied, string Text);
public sealed record DataRecordDraftCapture(IReadOnlyDictionary<Guid, DataScalarRecordValue>? Values, IReadOnlyList<DataSchemaIssue> Issues)
{
    public bool Success => Values is not null && Issues.Count == 0;
}

/// <summary>Parses an explicit scalar-entry draft. Omitted fields are left to owning creation defaults;
/// an explicitly supplied empty string remains supplied and is never replaced by a default.</summary>
public static class DataRecordCreationDraft
{
    public static DataRecordDraftCapture Capture(DataTableDefinition table, IReadOnlyList<DataRecordDraftField> draft)
    {
        ArgumentNullException.ThrowIfNull(table); ArgumentNullException.ThrowIfNull(draft);
        if (draft.Count > 256 || draft.Select(field => field.FieldID).Distinct().Count() != draft.Count)
            return new(null, [new("RecordDraftShapeInvalid", table.Id)]);
        var values = new Dictionary<Guid, DataScalarRecordValue>(); var issues = new List<DataSchemaIssue>();
        foreach (var field in draft)
        {
            if (!table.Fields.Any(item => item.FieldID == field.FieldID))
            { issues.Add(new("FieldNotFound", table.Id, field.FieldID)); continue; }
            if (!field.Supplied) continue;
            if (field.Text is null || field.Text.Length > 65_536)
            { issues.Add(new("RecordDraftValueCapacity", table.Id, field.FieldID)); continue; }
            var authored = table.RelationalSchema?.Fields.SingleOrDefault(item => item.FieldID == field.FieldID);
            try
            {
                var scalar = field.Text.Length == 0
                    ? new DataScalarRecordValue(DataCellKind.Text, JsonSerializer.SerializeToElement(""))
                    : authored?.Type switch
                {
                    DataFieldType.Integer => new DataScalarRecordValue(DataCellKind.Number,
                        JsonSerializer.SerializeToElement(long.Parse(field.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture))),
                    DataFieldType.Decimal or DataFieldType.Currency => new(DataCellKind.Number,
                        JsonSerializer.SerializeToElement(decimal.Parse(field.Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture))),
                    DataFieldType.Boolean => new(DataCellKind.Boolean, JsonSerializer.SerializeToElement(bool.Parse(field.Text))),
                    DataFieldType.Date or DataFieldType.DateTime => new(DataCellKind.Date, JsonSerializer.SerializeToElement(field.Text)),
                    DataFieldType.Text or DataFieldType.Duration or DataFieldType.Uuid or DataFieldType.Category => new(DataCellKind.Text, JsonSerializer.SerializeToElement(field.Text)),
                    null => new(DataCellKind.Text, JsonSerializer.SerializeToElement(field.Text)),
                    _ => throw new NotSupportedException("RecordDraftScalarCapabilityUnavailable")
                };
                values.Add(field.FieldID, DataRecordEdits.Capture(scalar));
            }
            catch (Exception error) when (error is FormatException or OverflowException or ArgumentException or NotSupportedException)
            { issues.Add(new("RecordDraftScalarInvalid", table.Id, field.FieldID)); }
        }
        return issues.Count == 0 ? new(new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, DataScalarRecordValue>(values), []) : new(null, issues);
    }
}
