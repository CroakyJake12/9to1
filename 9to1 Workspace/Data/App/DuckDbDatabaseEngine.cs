namespace HavenOS.Apps.Data;

public sealed class DuckDbDatabaseEngine : IDataDatabaseEngine
{
    private const int MaximumPublishedRows = 10_000;
    private const int MaximumPublishedColumns = 256;
    private const int MaximumPublishedCells = 500_000;

    private readonly JsonLineWorkerClient _worker;

    public DuckDbDatabaseEngine(string workerScriptPath, string pythonExecutable = "python3")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerScriptPath);
        _worker = new JsonLineWorkerClient(
            pythonExecutable,
            [workerScriptPath],
            new Dictionary<string, string?>
            {
                ["PYTHONUNBUFFERED"] = "1"
            });
    }

    public async Task OpenAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _ = await _worker.CallAsync<WorkerAck>("open", new { databasePath }, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReplaceTableAsync(DataTableSnapshot table, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        ValidateTable(table);
        _ = await _worker.CallAsync<WorkerAck>("replaceTable", new { table }, cancellationToken).ConfigureAwait(false);
    }

    public Task<DataQueryResult> ExecuteReadOnlyAsync(string sql, int maxRows = 200, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        if (maxRows is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(maxRows));
        return _worker.CallAsync<DataQueryResult>("query", new { sql, maxRows }, cancellationToken);
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default) =>
        _ = await _worker.CallAsync<WorkerAck>("close", null, cancellationToken).ConfigureAwait(false);

    public ValueTask DisposeAsync() => _worker.DisposeAsync();

    private static void ValidateTable(DataTableSnapshot table)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table.Name);
        if (table.Name.Length > 128 || table.Name.Any(char.IsControl))
            throw new ArgumentException("Published table names must be 1-128 printable characters.", nameof(table));
        if (table.Columns.Count is < 1 or > MaximumPublishedColumns)
            throw new ArgumentOutOfRangeException(nameof(table), $"Published tables must contain 1-{MaximumPublishedColumns} columns.");
        if (table.Rows.Count > MaximumPublishedRows)
            throw new ArgumentOutOfRangeException(nameof(table), $"Published tables may contain at most {MaximumPublishedRows} rows.");
        if ((long)table.Columns.Count * table.Rows.Count > MaximumPublishedCells)
            throw new ArgumentOutOfRangeException(nameof(table), $"Published tables may contain at most {MaximumPublishedCells} cells.");

        var uniqueColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in table.Columns)
        {
            if (string.IsNullOrWhiteSpace(column) || column.Length > 128 || column.Any(char.IsControl))
                throw new ArgumentException("Published column names must be 1-128 printable characters.", nameof(table));
            if (!uniqueColumns.Add(column))
                throw new ArgumentException($"Published column name '{column}' is duplicated.", nameof(table));
        }

        if (table.Rows.Any(row => row.Count != table.Columns.Count))
            throw new ArgumentException("Every published row must contain exactly one value per column.", nameof(table));
    }

    private sealed record WorkerAck(bool Ok);
}
