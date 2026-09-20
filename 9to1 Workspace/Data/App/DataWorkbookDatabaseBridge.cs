namespace HavenOS.Apps.Data;

/// <summary>
/// Explicit snapshot bridge between the Calc-owned workbook and DuckDB-owned database.
/// Publishing copies displayed values; it never makes DuckDB authoritative for the sheet.
/// </summary>
public sealed class DataWorkbookDatabaseBridge
{
    private const int MaximumIdentifierLength = 128;

    private readonly IDataSpreadsheetEngine _spreadsheet;
    private readonly IDataDatabaseEngine _database;

    public DataWorkbookDatabaseBridge(IDataSpreadsheetEngine spreadsheet, IDataDatabaseEngine database)
    {
        _spreadsheet = spreadsheet ?? throw new ArgumentNullException(nameof(spreadsheet));
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public async Task<DataPublishedTable> PublishRangeAsync(
        string workbookId,
        DataRangeRequest sourceRange,
        string tableName,
        bool firstRowIsHeaders = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        ArgumentNullException.ThrowIfNull(sourceRange);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        if (sourceRange.RowCount < 1 || sourceRange.ColumnCount < 1)
            throw new ArgumentOutOfRangeException(nameof(sourceRange), "Published ranges must contain at least one row and one column.");

        var snapshot = await _spreadsheet.ReadRangeAsync(workbookId, sourceRange, cancellationToken).ConfigureAwait(false);
        ValidateSnapshot(sourceRange, snapshot);

        var header = firstRowIsHeaders ? snapshot.Values[0] : null;
        var columns = BuildColumnNames(sourceRange.ColumnCount, header);
        var rows = snapshot.Values
            .Skip(firstRowIsHeaders ? 1 : 0)
            .Select(row => (IReadOnlyList<string>)row.ToArray())
            .ToArray();

        var table = new DataTableSnapshot(tableName.Trim(), columns, rows);
        await _database.ReplaceTableAsync(table, cancellationToken).ConfigureAwait(false);
        return new DataPublishedTable(table.Name, table.Columns, table.Rows.Count, sourceRange);
    }

    private static void ValidateSnapshot(DataRangeRequest request, DataRangeSnapshot snapshot)
    {
        if (!string.Equals(request.Sheet, snapshot.Sheet, StringComparison.Ordinal))
            throw new InvalidDataException("Spreadsheet engine returned a range from the wrong worksheet.");
        if (request.StartRow != snapshot.StartRow || request.StartColumn != snapshot.StartColumn)
            throw new InvalidDataException("Spreadsheet engine returned a range with different coordinates.");
        if (snapshot.Values.Count != request.RowCount || snapshot.Values.Any(row => row.Count != request.ColumnCount))
            throw new InvalidDataException("Spreadsheet engine returned a range with unexpected dimensions.");
    }

    private static IReadOnlyList<string> BuildColumnNames(int count, IReadOnlyList<string>? header)
    {
        var names = new string[count];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < count; index++)
        {
            var raw = header is null ? string.Empty : header[index];
            var basis = SanitizeHeader(raw);
            if (basis.Length == 0)
                basis = ColumnName(index);
            basis = Limit(basis, MaximumIdentifierLength);

            var candidate = basis;
            var suffix = 2;
            while (!used.Add(candidate))
            {
                var suffixText = $"_{suffix++}";
                candidate = Limit(basis, MaximumIdentifierLength - suffixText.Length) + suffixText;
            }
            names[index] = candidate;
        }
        return names;
    }

    private static string SanitizeHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return new string(value.Trim().Where(character => !char.IsControl(character)).ToArray());
    }

    private static string Limit(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    private static string ColumnName(int column)
    {
        if (column < 0)
            throw new ArgumentOutOfRangeException(nameof(column));
        var value = column + 1;
        var result = string.Empty;
        while (value > 0)
        {
            value--;
            result = (char)('A' + value % 26) + result;
            value /= 26;
        }
        return result;
    }
}
