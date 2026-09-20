namespace HavenOS.Apps.Data;

public sealed record DataQueryExecution(
    string Sql,
    int MaxRows,
    DataQueryResult Result,
    DateTimeOffset ExecutedAtUtc);

public sealed record DataQuerySessionSnapshot(
    string DatabasePath,
    IReadOnlyList<DataPublishedTable> PublishedTables,
    IReadOnlyList<DataQueryExecution> RecentQueries);

/// <summary>
/// HUI/GenUI-facing query workflow. It exposes typed workbook publication, bounded
/// read-only SQL execution and literal result materialisation into a new Calc sheet;
/// schema mutation remains inaccessible through query text.
/// </summary>
public sealed class DataQuerySession : IAsyncDisposable
{
    private const int MaximumRecentQueries = 20;
    private const int MaximumMaterializedColumns = 256;
    private const int MaximumMaterializedDataRows = 999;

    private readonly IDataSpreadsheetEngine _spreadsheet;
    private readonly IDataDatabaseEngine _database;
    private readonly DataWorkbookDatabaseBridge _bridge;
    private readonly List<DataPublishedTable> _publishedTables = [];
    private readonly List<DataQueryExecution> _recentQueries = [];
    private string? _databasePath;
    private bool _disposed;

    public DataQuerySession(IDataSpreadsheetEngine spreadsheet, IDataDatabaseEngine database)
    {
        _spreadsheet = spreadsheet ?? throw new ArgumentNullException(nameof(spreadsheet));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _bridge = new DataWorkbookDatabaseBridge(spreadsheet, database);
    }

    public bool IsOpen => _databasePath is not null;

    public async Task<DataQuerySessionSnapshot> OpenAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (_databasePath is not null)
            throw new InvalidOperationException("A database is already open in this Data query session.");

        await _database.OpenAsync(databasePath, cancellationToken).ConfigureAwait(false);
        _databasePath = databasePath;
        _publishedTables.Clear();
        _recentQueries.Clear();
        return Snapshot();
    }

    public async Task<DataPublishedTable> PublishRangeAsync(
        string workbookId,
        DataRangeRequest sourceRange,
        string tableName,
        bool firstRowIsHeaders = false,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureOpen();
        var published = await _bridge.PublishRangeAsync(
            workbookId,
            sourceRange,
            tableName,
            firstRowIsHeaders,
            cancellationToken).ConfigureAwait(false);

        var index = _publishedTables.FindIndex(table =>
            string.Equals(table.Name, published.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            _publishedTables[index] = published;
        else
            _publishedTables.Add(published);
        return published;
    }

    public async Task<DataQueryExecution> ExecuteAsync(
        string sql,
        int maxRows = 200,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        if (maxRows is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(maxRows));

        var result = await _database.ExecuteReadOnlyAsync(sql, maxRows, cancellationToken).ConfigureAwait(false);
        var execution = new DataQueryExecution(sql.Trim(), maxRows, result, DateTimeOffset.UtcNow);
        _recentQueries.Insert(0, execution);
        if (_recentQueries.Count > MaximumRecentQueries)
            _recentQueries.RemoveRange(MaximumRecentQueries, _recentQueries.Count - MaximumRecentQueries);
        return execution;
    }

    public async Task<DataMaterializedQueryResult> MaterializeAsync(
        string workbookId,
        DataQueryExecution execution,
        string sheetName,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);
        if (!_recentQueries.Contains(execution))
            throw new InvalidOperationException("Only a successful query from the current open Data query session can be materialized.");
        if (execution.Result.Truncated)
            throw new InvalidOperationException("A truncated query preview cannot be materialized because it would silently create an incomplete sheet.");
        if (execution.Result.Rows.Count > MaximumMaterializedDataRows)
            throw new InvalidOperationException($"Query materialization supports at most {MaximumMaterializedDataRows} data rows plus one header row in this slice.");
        if (execution.Result.Columns.Count is < 1 or > MaximumMaterializedColumns)
            throw new InvalidOperationException($"Query materialization requires 1-{MaximumMaterializedColumns} result columns.");
        if (execution.Result.Rows.Any(row => row.Count != execution.Result.Columns.Count))
            throw new InvalidDataException("Query result rows do not match the result column count.");

        var values = new List<IReadOnlyList<string>>(execution.Result.Rows.Count + 1)
        {
            execution.Result.Columns.ToArray()
        };
        values.AddRange(execution.Result.Rows.Select(row => (IReadOnlyList<string>)row.ToArray()));

        var created = await _spreadsheet.CreateSheetWithValuesAsync(
            workbookId,
            sheetName,
            values,
            cancellationToken).ConfigureAwait(false);
        if (created.StartRow != 0 || created.StartColumn != 0 ||
            created.Values.Count != values.Count ||
            created.Values.Any(row => row.Count != execution.Result.Columns.Count))
            throw new InvalidDataException("Spreadsheet engine returned unexpected dimensions for a materialized query result.");

        return new DataMaterializedQueryResult(
            created.Sheet,
            new DataRangeRequest(created.Sheet, 0, 0, values.Count, execution.Result.Columns.Count),
            execution.Result.Rows.Count);
    }

    public DataQuerySessionSnapshot Snapshot()
    {
        ThrowIfDisposed();
        var path = EnsureOpen();
        return new DataQuerySessionSnapshot(path, _publishedTables.ToArray(), _recentQueries.ToArray());
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_databasePath is null)
            return;

        // Keep the logical session open until the engine confirms close. If worker
        // shutdown fails, callers can still inspect state and retry rather than being
        // left with a closed facade over a potentially live database process.
        await _database.CloseAsync(cancellationToken).ConfigureAwait(false);
        _databasePath = null;
        _publishedTables.Clear();
        _recentQueries.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_databasePath is not null)
        {
            try { await _database.CloseAsync(CancellationToken.None).ConfigureAwait(false); }
            catch { }
            finally
            {
                _databasePath = null;
                _publishedTables.Clear();
                _recentQueries.Clear();
            }
        }
    }

    private string EnsureOpen() =>
        _databasePath ?? throw new InvalidOperationException("No database is open in this Data query session.");

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
