using System.Globalization;

namespace Haven.Core;

public enum DataRecordViewKind { Table, Kanban, Timeline }
public enum DataRecordFilterOperator { Equals, Contains }
public sealed record DataRecordFilter(Guid FieldID, DataRecordFilterOperator Operator, string Value);
public sealed record DataRecordSort(Guid FieldID, bool Descending = false);
public sealed record DataRecordViewDefinition(Guid ViewID, Guid TableID, string Name, DataRecordViewKind Kind,
    IReadOnlyList<Guid> FieldIDs, IReadOnlyList<DataRecordFilter> Filters, IReadOnlyList<DataRecordSort> Sorts,
    Guid? GroupFieldID = null, Guid? StartFieldID = null, Guid? EndFieldID = null);
public sealed record DataRecordViewRow(Guid RecordID, IReadOnlyDictionary<Guid, DataCell?> Cells,
    string Group, DateTimeOffset? Start, DateTimeOffset? End, string? TimelineIssue);
public sealed record DataRecordViewSnapshot(Guid WorkbookID, Guid RevisionID, DataRecordViewDefinition View,
    IReadOnlyList<DataRecordViewRow> Rows, int TotalRows, bool HasMore);

/// <summary>Views project the canonical sheet-backed records. They never retain another row store.</summary>
public static class DataRecordViews
{
    public static void Validate(DataWorkbook workbook)
    {
        if (workbook.RecordViews.Count > 256 || workbook.RecordViews.Select(x => x.ViewID).Distinct().Count() != workbook.RecordViews.Count)
            throw new InvalidDataException("Invalid Data view identities or capacity.");
        foreach (var view in workbook.RecordViews)
        {
            var table = workbook.Tables.SingleOrDefault(t => t.Id == view.TableID);
            if (view.ViewID == Guid.Empty || table is null || table.RecordIdentityVersion != 1 ||
                string.IsNullOrWhiteSpace(view.Name) || view.Name.Length > 256 || !Enum.IsDefined(view.Kind) ||
                view.FieldIDs is null || view.Filters is null || view.Sorts is null || view.FieldIDs.Count > 256 ||
                view.Filters.Count > 32 || view.Sorts.Count > 8 || view.FieldIDs.Distinct().Count() != view.FieldIDs.Count)
                throw new InvalidDataException("A named view requires a canonical record table and bounded settings.");
            var fields = table.Fields.Select(f => f.FieldID).ToHashSet();
            var referenced = view.FieldIDs.Concat(view.Filters.Select(f => f.FieldID)).Concat(view.Sorts.Select(s => s.FieldID))
                .Concat(new[] { view.GroupFieldID, view.StartFieldID, view.EndFieldID }.Where(x => x.HasValue).Select(x => x!.Value));
            if (referenced.Any(id => !fields.Contains(id)) || view.Filters.Any(f => !Enum.IsDefined(f.Operator) || f.Value is null || f.Value.Length > 4096))
                throw new InvalidDataException("A view field is missing or its filter is invalid.");
            if (view.Kind == DataRecordViewKind.Kanban && view.GroupFieldID is null ||
                view.Kind == DataRecordViewKind.Timeline && view.StartFieldID is null)
                throw new InvalidDataException("Choose a grouping field for Board or a date field for Timeline.");
            if (view.Kind == DataRecordViewKind.Timeline)
                foreach (var id in new[] { view.StartFieldID, view.EndFieldID }.Where(x => x.HasValue))
                    if (table.RelationalSchema?.Fields.SingleOrDefault(f => f.FieldID == id)?.Type is not (DataFieldType.Date or DataFieldType.DateTime))
                        throw new InvalidDataException("Timeline fields require an explicit Date or DateTime schema.");
        }
    }

    public static DataRecordViewSnapshot Project(DataWorkbook workbook, Guid viewID, int offset = 0, int limit = 200)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        if (offset < 0 || limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        Validate(workbook);
        var view = workbook.RecordViews.SingleOrDefault(v => v.ViewID == viewID) ?? throw new KeyNotFoundException("ViewNotFound");
        var table = workbook.Tables.Single(t => t.Id == view.TableID);
        if (table.Records.Count > 10000) throw new InvalidOperationException("This record view currently supports at most 10,000 records.");
        DataTableIdentity.ValidateTable(table);
        var sheet = workbook.Sheets.Single(s => s.Id == table.SheetId);
        var fields = table.Fields.ToDictionary(f => f.FieldID);
        var required = view.FieldIDs.Concat(view.Filters.Select(f => f.FieldID)).Concat(view.Sorts.Select(f => f.FieldID))
            .Concat(new[] { view.GroupFieldID, view.StartFieldID, view.EndFieldID }.Where(id => id.HasValue).Select(id => id!.Value)).Distinct().ToArray();
        var rows = new List<DataRecordViewRow>();
        foreach (var record in table.Records)
        {
            // Validate identities once, then read the SAME sheet cells by their stable mapping.
            // Calling ReadCell per field would repeat the whole-table validation for each value.
            var cells = required.ToDictionary(id => id, id =>
            {
                var cell = sheet.GetCell(record.SheetRow, fields[id].SheetColumn);
                return cell is null ? null : new DataCell { Row = cell.Row, Column = cell.Column, Kind = cell.Kind,
                    Value = cell.Value, Formula = cell.Formula, Metadata = new(cell.Metadata, StringComparer.Ordinal) };
            });
            if (!view.Filters.All(f => f.Operator == DataRecordFilterOperator.Equals
                    ? string.Equals(cells[f.FieldID]?.Value ?? "", f.Value, StringComparison.OrdinalIgnoreCase)
                    : (cells[f.FieldID]?.Value ?? "").Contains(f.Value, StringComparison.OrdinalIgnoreCase))) continue;
            DateTimeOffset? Date(Guid? field) => field is { } id && DateTimeOffset.TryParse(cells[id]?.Value,
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date) ? date : null;
            var start = Date(view.StartFieldID); var end = Date(view.EndFieldID);
            var issue = view.Kind != DataRecordViewKind.Timeline ? null : start is null ? "Undated" :
                view.EndFieldID is not null && end is null ? "Invalid end date" : end < start ? "End precedes start" : null;
            rows.Add(new(record.RecordID, new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, DataCell?>(cells),
                view.GroupFieldID is { } group ? cells[group]?.Value ?? "" : "", start, end, issue));
        }
        rows.Sort((left, right) =>
        {
            foreach (var sort in view.Sorts)
            {
                var a = left.Cells[sort.FieldID]; var b = right.Cells[sort.FieldID];
                var result = Compare(a, b); if (result != 0) return sort.Descending ? -result : result;
            }
            return left.RecordID.CompareTo(right.RecordID);
        });
        return new(workbook.Id, workbook.RevisionId, view, rows.Skip(offset).Take(limit).ToArray(), rows.Count, offset + limit < rows.Count);
    }
    private static int Compare(DataCell? left, DataCell? right)
    {
        if (left is null || right is null) return left is null ? right is null ? 0 : -1 : 1;
        if (left.Kind == DataCellKind.Number && right.Kind == DataCellKind.Number &&
            decimal.TryParse(left.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) &&
            decimal.TryParse(right.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)) return a.CompareTo(b);
        return StringComparer.OrdinalIgnoreCase.Compare(left.Value, right.Value);
    }
}
