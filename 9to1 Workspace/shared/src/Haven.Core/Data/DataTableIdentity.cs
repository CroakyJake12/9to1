namespace Haven.Core;

/// <summary>Stable semantic identities over the table's existing sheet-backed values.
/// Coordinates locate current storage; callers retain FieldID/RecordID across edits.</summary>
public sealed record DataTableField(Guid FieldID, int SheetColumn);
public sealed record DataTableRecord(Guid RecordID, int SheetRow);

public static class DataTableIdentity
{
    private const int MaximumRecords = 1_000_000;
    private const int MaximumFields = 16_384;

    /// <summary>Explicit authoring operation. Reading or normalising a legacy range never invents identities.</summary>
    public static void Initialize(DataWorkbook workbook, DataTableDefinition table)
    {
        ArgumentNullException.ThrowIfNull(workbook); ArgumentNullException.ThrowIfNull(table);
        if (table.Fields is null || table.Records is null || table.Fields.Count != 0 || table.Records.Count != 0) throw new InvalidDataException("Unversioned table identities cannot be overwritten.");
        if (workbook.Tables.Any(other => !ReferenceEquals(other, table) && other.Id == table.Id)) throw new InvalidDataException("Duplicate table identity.");
        if (table.RecordIdentityVersion != 0) throw new InvalidOperationException("The table already has canonical record identities.");
        var sheet = workbook.Sheets.SingleOrDefault(sheet => sheet.Id == table.SheetId)
            ?? throw new InvalidDataException("Table sheet is missing.");
        Bounds(table);
        if (workbook.Tables.Any(other => !ReferenceEquals(other, table) && other.RecordIdentityVersion != 0
            && other.SheetId == table.SheetId && Overlaps(other.Range, table.Range)))
            throw new InvalidOperationException("Canonical record tables cannot overlap.");
        var fields = Enumerable.Range(table.Range.StartColumn, table.Range.ColumnCount)
            .Select(column => new DataTableField(Guid.NewGuid(), column)).ToList();
        var first = checked(table.Range.StartRow + (table.HasHeaders ? 1 : 0));
        var records = Enumerable.Range(first, checked(table.Range.EndRow - first + 1))
            .Select(row => new DataTableRecord(Guid.NewGuid(), row)).ToList();
        table.Fields = fields; table.Records = records; table.RecordIdentityVersion = 1;
        sheet.Workbook = workbook;
    }

    public static DataCell? ReadCell(DataWorkbook workbook, Guid tableID, Guid recordID, Guid fieldID)
    {
        var table = workbook.Tables.SingleOrDefault(item => item.Id == tableID) ?? throw new KeyNotFoundException("TableNotFound");
        if (table.RecordIdentityVersion != 1) throw new InvalidOperationException("The table has no canonical record identities.");
        ValidateTable(table);
        var row = table.Records.SingleOrDefault(item => item.RecordID == recordID) ?? throw new KeyNotFoundException("RecordNotFound");
        var field = table.Fields.SingleOrDefault(item => item.FieldID == fieldID) ?? throw new KeyNotFoundException("FieldNotFound");
        var sheet = workbook.Sheets.SingleOrDefault(item => item.Id == table.SheetId) ?? throw new InvalidDataException("Table sheet is missing.");
        var cell = sheet.GetCell(row.SheetRow, field.SheetColumn);
        return cell is null ? null : new DataCell { Row = cell.Row, Column = cell.Column, Kind = cell.Kind,
            Value = cell.Value, Formula = cell.Formula, Metadata = new(cell.Metadata, StringComparer.Ordinal) };
    }

    internal static void ValidateWorkbook(DataWorkbook workbook)
    {
        var ids = new HashSet<Guid>();
        foreach (var table in workbook.Tables)
            if (!ids.Add(table.Id)) throw new InvalidDataException("Duplicate Data table identity.");
        var structured = new List<DataTableDefinition>();
        foreach (var table in workbook.Tables)
        {
            if (table.RecordIdentityVersion == 0)
            {
                if (table.Fields.Count != 0 || table.Records.Count != 0) throw new InvalidDataException("Legacy table has unversioned record identities.");
                continue;
            }
            ValidateTable(table);
            if (!workbook.Sheets.Any(sheet => sheet.Id == table.SheetId))
                throw new InvalidDataException("Canonical table identity or sheet is invalid.");
            if (structured.Any(other => other.SheetId == table.SheetId && Overlaps(other.Range, table.Range)))
                throw new InvalidDataException("Canonical record tables cannot overlap.");
            structured.Add(table);
            foreach (var field in table.Fields) if (!ids.Add(field.FieldID)) throw new InvalidDataException("Duplicate Data field identity.");
            foreach (var record in table.Records) if (!ids.Add(record.RecordID)) throw new InvalidDataException("Duplicate Data record identity.");
        }
    }

    public static void ValidateSort(DataSheet sheet, DataCellRange range, bool hasHeader)
    {
        foreach (var table in Tables(sheet).Where(table => Overlaps(table.Range, range)))
        {
            var first = table.Range.StartRow + (table.HasHeaders ? 1 : 0);
            var whole = range.StartRow == table.Range.StartRow && hasHeader == table.HasHeaders;
            var body = range.StartRow == first && !hasHeader;
            if ((!whole && !body) || range.EndRow != table.Range.EndRow
                || range.StartColumn != table.Range.StartColumn || range.EndColumn != table.Range.EndColumn)
                throw new InvalidOperationException("Sort the whole canonical table to preserve record identity.");
        }
    }

    internal static void ApplySort(DataSheet sheet, DataCellRange range, IReadOnlyDictionary<int, int> sourceToTarget)
    {
        foreach (var table in Tables(sheet).Where(table => Overlaps(table.Range, range)))
            table.Records = table.Records.Select(record => sourceToTarget.TryGetValue(record.SheetRow, out var row)
                ? record with { SheetRow = row } : record).OrderBy(record => record.SheetRow).ToList();
    }

    // Compute all identity/range changes before changing any cell. The owning workbook is the
    // same aggregate; this is not another record store or a copy of cell values.
    internal static Action PrepareStructure(DataSheet sheet, int index, int count, bool rows, bool delete)
    {
        var end = checked(index + count);
        var plans = new List<(DataTableDefinition Table, DataCellRange Range, List<DataTableField> Fields, List<DataTableRecord> Records,
            List<DataTableFilter> Filters, int? SortColumn)>();
        foreach (var table in Tables(sheet))
        {
            ValidateTable(table);
            var range = table.Range.Clone(); var fields = table.Fields.ToList(); var records = table.Records.ToList();
            var filters = table.Filters.Select(filter => new DataTableFilter { Column = filter.Column, Operator = filter.Operator, Value = filter.Value }).ToList();
            var sortColumn = table.SortColumn;
            var start = rows ? range.StartRow : range.StartColumn;
            var last = rows ? range.EndRow : range.EndColumn;
            if (!delete)
            {
                if (index <= start)
                {
                    start = checked(start + count); last = checked(last + count);
                    if (rows) records = records.Select(record => record with { SheetRow = checked(record.SheetRow + count) }).ToList();
                    else fields = fields.Select(field => field with { SheetColumn = checked(field.SheetColumn + count) }).ToList();
                }
                else if (index <= last)
                {
                    if ((long)(rows ? records.Count : fields.Count) + count > (rows ? MaximumRecords : MaximumFields))
                        throw new InvalidOperationException("Canonical table identity capacity exceeded.");
                    last = checked(last + count);
                    if (rows)
                    {
                        records = records.Select(record => record.SheetRow >= index ? record with { SheetRow = checked(record.SheetRow + count) } : record).ToList();
                        records.AddRange(Enumerable.Range(index, count).Select(row => new DataTableRecord(Guid.NewGuid(), row)));
                    }
                    else
                    {
                        fields = fields.Select(field => field.SheetColumn >= index ? field with { SheetColumn = checked(field.SheetColumn + count) } : field).ToList();
                        fields.AddRange(Enumerable.Range(index, count).Select(column => new DataTableField(Guid.NewGuid(), column)));
                    }
                }
            }
            else
            {
                if (index <= start && end > start && rows && table.HasHeaders)
                    throw new InvalidOperationException("Remove the canonical table explicitly before deleting its header.");
                var removed = Math.Max(0L, Math.Min((long)last + 1, end) - Math.Max(start, index));
                var before = Math.Max(0, Math.Min(start, end) - index);
                start -= before; last = checked(last - before - (int)removed);
                if (last < start) throw new InvalidOperationException("A canonical table must retain its field range.");
                if (rows) records = records.Where(record => record.SheetRow < index || record.SheetRow >= end)
                    .Select(record => record.SheetRow >= end ? record with { SheetRow = record.SheetRow - count } : record).ToList();
                else fields = fields.Where(field => field.SheetColumn < index || field.SheetColumn >= end)
                    .Select(field => field.SheetColumn >= end ? field with { SheetColumn = field.SheetColumn - count } : field).ToList();
            }
            if (rows) { range.StartRow = start; range.EndRow = last; } else { range.StartColumn = start; range.EndColumn = last; }
            if (!rows)
            {
                if (delete) filters.RemoveAll(filter => filter.Column >= index && filter.Column < end);
                foreach (var filter in filters)
                    if (filter.Column >= (delete ? end : index)) filter.Column = checked(filter.Column + (delete ? -count : count));
                if (sortColumn is { } column)
                    sortColumn = delete && column >= index && column < end ? null
                        : column >= (delete ? end : index) ? checked(column + (delete ? -count : count)) : column;
            }
            var candidate = new DataTableDefinition { Id = table.Id, SheetId = table.SheetId, Range = range, HasHeaders = table.HasHeaders,
                RecordIdentityVersion = 1, Fields = fields, Records = records };
            ValidateTable(candidate);
            plans.Add((table, range, fields.OrderBy(field => field.SheetColumn).ToList(), records.OrderBy(record => record.SheetRow).ToList(), filters, sortColumn));
        }
        return () => { foreach (var plan in plans) { plan.Table.Range = plan.Range; plan.Table.Fields = plan.Fields; plan.Table.Records = plan.Records;
            plan.Table.Filters = plan.Filters; plan.Table.SortColumn = plan.SortColumn; } };
    }

    private static IEnumerable<DataTableDefinition> Tables(DataSheet sheet) =>
        sheet.Workbook?.Tables.Where(table => table.SheetId == sheet.Id && table.RecordIdentityVersion != 0) ?? [];
    private static bool Overlaps(DataCellRange a, DataCellRange b) => a.StartRow <= b.EndRow && a.EndRow >= b.StartRow
        && a.StartColumn <= b.EndColumn && a.EndColumn >= b.StartColumn;
    private static void Bounds(DataTableDefinition table)
    {
        var range = table.Range;
        if (range is null || range.StartRow < 0 || range.StartColumn < 0 || range.EndRow < range.StartRow || range.EndColumn < range.StartColumn
            || range.EndRow == int.MaxValue || range.EndColumn == int.MaxValue
            || (long)range.EndRow - range.StartRow + (table.HasHeaders ? 0 : 1) > MaximumRecords
            || (long)range.EndColumn - range.StartColumn + 1 > MaximumFields || table.Id == Guid.Empty || table.SheetId == Guid.Empty)
            throw new InvalidDataException("Canonical table bounds or identity are invalid.");
    }
    internal static void ValidateTable(DataTableDefinition table)
    {
        Bounds(table);
        if (table.RecordIdentityVersion != 1 || table.Fields is null || table.Records is null
            || table.Fields.Count != (long)table.Range.EndColumn - table.Range.StartColumn + 1
            || table.Records.Count != (long)table.Range.EndRow - table.Range.StartRow + (table.HasHeaders ? 0 : 1))
            throw new InvalidDataException("Canonical table identity schema is invalid.");
        var ids = new HashSet<Guid> { table.Id }; var columns = new HashSet<int>(); var rows = new HashSet<int>();
        foreach (var field in table.Fields)
            if (field is null || field.FieldID == Guid.Empty || !ids.Add(field.FieldID) || !columns.Add(field.SheetColumn)
                || field.SheetColumn < table.Range.StartColumn || field.SheetColumn > table.Range.EndColumn)
                throw new InvalidDataException("Invalid canonical field identity or storage binding.");
        foreach (var record in table.Records)
            if (record is null || record.RecordID == Guid.Empty || !ids.Add(record.RecordID) || !rows.Add(record.SheetRow)
                || record.SheetRow < (long)table.Range.StartRow + (table.HasHeaders ? 1 : 0) || record.SheetRow > table.Range.EndRow)
                throw new InvalidDataException("Invalid canonical record identity or storage binding.");
    }
}
