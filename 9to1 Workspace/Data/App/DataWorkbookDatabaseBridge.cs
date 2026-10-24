using System.Security.Cryptography;
using System.Text.Json;

namespace HavenOS.Apps.Data;

/// <summary>
/// Immutable expected source identity captured from the exact range shown for an approved
/// publication. This is a freshness precondition, not an authorization capability.
/// </summary>
public sealed class DataApprovedRangeSnapshot
{
    private DataApprovedRangeSnapshot(string workbookId, string databasePath, DataRangeRequest sourceRange,
        string tableName, bool firstRowIsHeaders, string contentSHA256)
    {
        WorkbookId = workbookId;
        DatabasePath = databasePath;
        SourceRange = sourceRange;
        TableName = tableName;
        FirstRowIsHeaders = firstRowIsHeaders;
        ContentSHA256 = contentSHA256;
    }

    public string WorkbookId { get; }
    public string DatabasePath { get; }
    public DataRangeRequest SourceRange { get; }
    public string TableName { get; }
    public bool FirstRowIsHeaders { get; }
    public string ContentSHA256 { get; }

    public static DataApprovedRangeSnapshot Capture(string workbookId, string databasePath,
        DataRangeRequest sourceRange, string tableName, bool firstRowIsHeaders, DataRangeSnapshot expectedSnapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var normalizedTableName = DataWorkbookDatabaseBridge.ValidatePublicationRequest(workbookId,
            sourceRange, tableName, firstRowIsHeaders);
        var detached = DataWorkbookDatabaseBridge.CaptureValidatedSnapshot(sourceRange, expectedSnapshot);
        var fingerprint = DataWorkbookDatabaseBridge.ComputeApprovedContentIdentity(workbookId, databasePath,
            sourceRange, normalizedTableName, firstRowIsHeaders, detached);
        return new(workbookId, databasePath, sourceRange, normalizedTableName, firstRowIsHeaders, fingerprint);
    }
}

public sealed class DataStaleApprovedSourceException : InvalidOperationException
{
    public const string ErrorCode = "StaleApprovedSource";

    public DataStaleApprovedSourceException()
        : base("StaleApprovedSource: the actual workbook/database target or range no longer matches the approved publication snapshot.") { }

    public string Code => ErrorCode;
}

/// <summary>
/// Explicit snapshot bridge between the Calc-owned workbook and DuckDB-owned database.
/// Publishing copies displayed values; it never makes DuckDB authoritative for the sheet.
/// </summary>
public sealed class DataWorkbookDatabaseBridge
{
    private const int MaximumIdentifierLength = 128;
    private const int MaximumPublishedRows = 10_000;
    private const int MaximumPublishedColumns = 256;
    private const int MaximumPublishedCells = 500_000;

    private readonly IDataSpreadsheetEngine _spreadsheet;
    private readonly IDataDatabaseEngine _database;

    public DataWorkbookDatabaseBridge(IDataSpreadsheetEngine spreadsheet, IDataDatabaseEngine database)
    {
        _spreadsheet = spreadsheet ?? throw new ArgumentNullException(nameof(spreadsheet));
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public Task<DataPublishedTable> PublishRangeAsync(
        string workbookId,
        DataRangeRequest sourceRange,
        string tableName,
        bool firstRowIsHeaders = false,
        CancellationToken cancellationToken = default) =>
        PublishRangeCoreAsync(workbookId, sourceRange, tableName, firstRowIsHeaders, null, cancellationToken);

    /// <summary>Publishes only if the single actual detached read still matches approved source content.</summary>
    internal Task<DataPublishedTable> PublishApprovedRangeAsync(string workbookId,
        DataApprovedRangeSnapshot approvedSnapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approvedSnapshot);
        return PublishRangeCoreAsync(workbookId, approvedSnapshot.SourceRange, approvedSnapshot.TableName,
            approvedSnapshot.FirstRowIsHeaders, approvedSnapshot, cancellationToken);
    }

    private async Task<DataPublishedTable> PublishRangeCoreAsync(string workbookId,
        DataRangeRequest sourceRange, string tableName, bool firstRowIsHeaders,
        DataApprovedRangeSnapshot? approvedSnapshot, CancellationToken cancellationToken)
    {
        var normalizedTableName = ValidatePublicationRequest(workbookId, sourceRange, tableName, firstRowIsHeaders);
        var snapshot = await _spreadsheet.ReadRangeAsync(workbookId, sourceRange, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        snapshot = CaptureValidatedSnapshot(sourceRange, snapshot);
        if (approvedSnapshot is not null)
        {
            var actual = ComputeApprovedContentIdentity(workbookId, approvedSnapshot.DatabasePath, sourceRange, normalizedTableName,
                firstRowIsHeaders, snapshot);
            if (!string.Equals(actual, approvedSnapshot.ContentSHA256, StringComparison.Ordinal))
                throw new DataStaleApprovedSourceException();
        }

        var header = firstRowIsHeaders ? snapshot.Values[0] : null;
        var columns = BuildColumnNames(sourceRange.ColumnCount, header);
        var rows = snapshot.Values
            .Skip(firstRowIsHeaders ? 1 : 0)
            .Select(row => (IReadOnlyList<string>)row.ToArray())
            .ToArray();

        var table = new DataTableSnapshot(normalizedTableName, columns, rows);
        await _database.ReplaceTableAsync(table, cancellationToken).ConfigureAwait(false);
        return new DataPublishedTable(table.Name, table.Columns, table.Rows.Count, sourceRange);
    }

    internal static string ValidatePublicationRequest(string workbookId, DataRangeRequest sourceRange,
        string tableName, bool firstRowIsHeaders)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        ArgumentNullException.ThrowIfNull(sourceRange);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRange.Sheet);
        if (sourceRange.StartRow < 0 || sourceRange.StartColumn < 0
            || (long)sourceRange.StartRow + sourceRange.RowCount > int.MaxValue
            || (long)sourceRange.StartColumn + sourceRange.ColumnCount > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(sourceRange), "Published range coordinates must be non-negative and representable.");
        if (sourceRange.RowCount < 1 || sourceRange.ColumnCount < 1)
            throw new ArgumentOutOfRangeException(nameof(sourceRange), "Published ranges must contain at least one row and one column.");
        var publishedRows = sourceRange.RowCount - (firstRowIsHeaders ? 1 : 0);
        var snapshotCells = (long)sourceRange.RowCount * sourceRange.ColumnCount;
        if (publishedRows > MaximumPublishedRows || sourceRange.ColumnCount > MaximumPublishedColumns
            || (long)publishedRows * sourceRange.ColumnCount > MaximumPublishedCells
            || snapshotCells > (long)MaximumPublishedCells + MaximumPublishedColumns)
            throw new ArgumentOutOfRangeException(nameof(sourceRange), "Published ranges exceed the database row, column or cell limits.");
        var normalizedTableName = tableName.Trim();
        if (normalizedTableName.Length > MaximumIdentifierLength || normalizedTableName.Any(char.IsControl))
            throw new ArgumentException($"Published table names must contain 1-{MaximumIdentifierLength} printable characters.", nameof(tableName));
        return normalizedTableName;
    }

    internal static DataRangeSnapshot CaptureValidatedSnapshot(DataRangeRequest request, DataRangeSnapshot snapshot)
    {
        if (snapshot is null || snapshot.Values is null)
            throw new InvalidDataException("Spreadsheet engine returned an incomplete range snapshot.");
        if (!string.Equals(request.Sheet, snapshot.Sheet, StringComparison.Ordinal))
            throw new InvalidDataException("Spreadsheet engine returned a range from the wrong worksheet.");
        if (request.StartRow != snapshot.StartRow || request.StartColumn != snapshot.StartColumn)
            throw new InvalidDataException("Spreadsheet engine returned a range with different coordinates.");
        if (snapshot.Values.Count != request.RowCount)
            throw new InvalidDataException("Spreadsheet engine returned a range with unexpected dimensions.");
        if (snapshot.RowVisibility is not null && snapshot.RowVisibility.Count != request.RowCount)
            throw new InvalidDataException("Spreadsheet engine returned invalid row-visibility metadata.");

        var values = new IReadOnlyList<string>[request.RowCount];
        for (var rowIndex = 0; rowIndex < request.RowCount; rowIndex++)
        {
            var sourceRow = snapshot.Values[rowIndex];
            if (sourceRow is null || sourceRow.Count != request.ColumnCount)
                throw new InvalidDataException("Spreadsheet engine returned a range with unexpected dimensions.");
            var row = new string[request.ColumnCount];
            for (var columnIndex = 0; columnIndex < request.ColumnCount; columnIndex++)
                row[columnIndex] = sourceRow[columnIndex]
                    ?? throw new InvalidDataException("Spreadsheet engine returned a null cell value.");
            values[rowIndex] = Array.AsReadOnly(row);
        }
        return new DataRangeSnapshot(snapshot.Sheet, snapshot.StartRow, snapshot.StartColumn, Array.AsReadOnly(values),
            snapshot.RowVisibility is null ? null : Array.AsReadOnly(snapshot.RowVisibility.ToArray()));
    }

    internal static string ComputeApprovedContentIdentity(string workbookId, string databasePath,
        DataRangeRequest sourceRange, string tableName, bool firstRowIsHeaders, DataRangeSnapshot snapshot)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new ApprovedRangeFingerprint(workbookId, databasePath,
            sourceRange, tableName, firstRowIsHeaders, snapshot.Values, snapshot.RowVisibility));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private sealed record ApprovedRangeFingerprint(string WorkbookId, string DatabasePath, DataRangeRequest SourceRange,
        string TableName, bool FirstRowIsHeaders, IReadOnlyList<IReadOnlyList<string>> Values,
        IReadOnlyList<bool>? RowVisibility);

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
