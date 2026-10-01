using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public sealed record DataRecordCreationProjectionResult(DataWorkbook? Workbook, DataFormulaRecalculationReport? Calculation,
    IReadOnlyList<DataSchemaIssue> Issues)
{
    public bool Success => Workbook is not null && Issues.Count == 0;
}

/// <summary>Includes affected local calculation in the exact record-creation proposal. Unsupported
/// dependency semantics require an actual owning backend, rather than preserving a misleading cache.</summary>
public static class DataRecordCreationProjection
{
    public static DataRecordCreationProjectionResult Prepare(DataWorkbook workbook, Guid tableID, Guid recordID,
        int expectedVersion, Guid expectedRevision, IReadOnlyDictionary<Guid, DataScalarRecordValue> values,
        DateTimeOffset calculationAt)
    {
        var result = DataRecordCreation.Prepare(workbook, tableID, recordID, expectedVersion, expectedRevision, values);
        if (!result.Success) return new(null, null, result.Issues);
        if (!HasCompleteLocalDependencies(workbook, calculationAt))
            return new(null, null, [new("RecordCalculationCapabilityUnavailable", tableID, RecordID: recordID)]);
        var candidate = result.Workbook!; var table = candidate.Tables.Single(table => table.Id == tableID);
        var record = table.Records.Single(record => record.RecordID == recordID);
        var sheet = candidate.Sheets.Single(sheet => sheet.Id == table.SheetId);
        var addresses = table.Fields.Select(field => DataFormulaEngine.Address(sheet, record.SheetRow, field.SheetColumn)).ToArray();
        var calculation = new DataFormulaEngine(new SnapshotTime(calculationAt)).Recalculate(candidate, addresses);
        var issues = DataRelationalSchema.Inspect(candidate);
        return issues.Count == 0 ? new(candidate, calculation, []) : new(null, calculation, issues);
    }
    private static bool HasCompleteLocalDependencies(DataWorkbook workbook, DateTimeOffset calculationAt)
    {
        foreach (var sheet in workbook.Sheets)
        foreach (var cell in sheet.Cells.Where(cell => !string.IsNullOrWhiteSpace(cell.Formula)))
        {
            try
            {
                if (!Inspect(DataFormulaParser.Parse(cell.Formula), sheet, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0)) return false;
            }
            catch (DataFormulaParseException) { return false; }
        }
        return true;
        bool Inspect(DataFormulaExpression expression, DataSheet current, HashSet<string> names, int depth)
        {
            if (depth > 128) return false;
            switch (expression)
            {
                case DataFormulaLiteralExpression or DataFormulaErrorExpression: return true;
                case DataFormulaReferenceExpression reference: return Resolve(reference.Reference.SheetName, current) is not null;
                case DataFormulaRangeExpression range:
                    var start = Resolve(range.Range.Start.SheetName, current);
                    var end = Resolve(range.Range.End.SheetName ?? range.Range.Start.SheetName, current);
                    var rows = Math.Abs((long)range.Range.Start.Row - range.Range.End.Row) + 1;
                    var columns = Math.Abs((long)range.Range.Start.Column - range.Range.End.Column) + 1;
                    return start is not null && end?.Id == start.Id && rows * columns <= 100_000;
                case DataFormulaUnaryExpression unary: return Inspect(unary.Operand, current, names, depth + 1);
                case DataFormulaBinaryExpression binary: return Inspect(binary.Left, current, names, depth + 1) && Inspect(binary.Right, current, names, depth + 1);
                case DataFormulaFunctionExpression function:
                    // Probe the actual local evaluator with no inputs: known functions report an argument
                    // error or value, while unavailable functions report Name. No second catalogue is maintained.
                    var available = function.Name.ToUpperInvariant() is "IF" or "IFERROR" or "IFNA"
                        || DataFormulaFunctions.Evaluate(function.Name, [], calculationAt).ErrorCode != DataFormulaErrorCode.Name;
                    return available && function.Arguments.All(argument => Inspect(argument, current, names, depth + 1));
                case DataFormulaNameExpression name:
                    if (!names.Add(name.Name)) return true; // Known name cycles remain typed calculation errors.
                    var matching = workbook.NamedRanges.Where(range => range.Name.Equals(name.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (matching.Length != 1 || string.IsNullOrWhiteSpace(matching[0].RefersTo)) return false;
                    try { return Inspect(DataFormulaParser.Parse(matching[0].RefersTo), current, names, depth + 1); }
                    finally { names.Remove(name.Name); }
                default: return false;
            }
        }
        DataSheet? Resolve(string? name, DataSheet current)
        {
            if (string.IsNullOrWhiteSpace(name)) return current;
            var matches = workbook.Sheets.Where(sheet => sheet.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
    }
    private sealed class SnapshotTime(DateTimeOffset at) : TimeProvider
    {
        private readonly TimeZoneInfo _zone = TimeZoneInfo.CreateCustomTimeZone("DataReviewSnapshot", at.Offset, "Data review snapshot", "Data review snapshot");
        public override DateTimeOffset GetUtcNow() => at.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => _zone;
    }
}
