using System.Security.Cryptography;
using System.Text;

namespace HavenOS.Apps.Data;

public sealed record DataQueryExecution(
    string Sql,
    int MaxRows,
    DataQueryResult Result,
    DateTimeOffset ExecutedAtUtc)
{
    public Guid ExecutionID { get; init; } = Guid.NewGuid();
}

public enum DataQueryAttemptOutcome { Succeeded, Failed, Cancelled, Stale }

/// <summary>Bounded in-memory attempt observation; it is not a persisted canonical SqlExecution record.</summary>
public sealed record DataQueryAttempt(Guid ExecutionID, Guid SessionID, Guid DatabaseGeneration, string TargetIdentity,
    string QueryFingerprint, DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc,
    DataQueryAttemptOutcome Outcome, string? ErrorCode, Guid? SuccessfulExecutionID);

public sealed class DataStaleQueryTargetException : InvalidOperationException
{
    public const string ErrorCode = "StaleTarget";
    public DataStaleQueryTargetException() : base("StaleTarget: the database session changed after this query was queued.") { }
    public string Code => ErrorCode;
}

public sealed record DataQuerySessionSnapshot(
    string DatabasePath,
    IReadOnlyList<DataPublishedTable> PublishedTables,
    IReadOnlyList<DataQueryExecution> RecentQueries)
{
    public IReadOnlyList<DataQueryAttempt> RecentAttempts { get; init; } = Array.Empty<DataQueryAttempt>();
}

/// <summary>
/// HUI/GenUI-facing query workflow. It exposes typed workbook publication, bounded
/// read-only SQL execution and literal result materialisation into a new Calc sheet;
/// schema mutation remains inaccessible through query text.
/// </summary>
public sealed class DataQuerySession : IAsyncDisposable
{
    private const int MaximumRecentQueries = 20;
    private const int MaximumRecentAttempts = 100;
    private const int MaximumMaterializedColumns = 256;
    private const int MaximumMaterializedDataRows = 999;

    private readonly IDataSpreadsheetEngine _spreadsheet;
    private readonly IDataDatabaseEngine _database;
    private readonly DataWorkbookDatabaseBridge _bridge;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly List<DataPublishedTable> _publishedTables = [];
    private readonly List<DataQueryExecution> _recentQueries = [];
    private readonly List<DataQueryAttempt> _recentAttempts = [];
    public Guid SessionID { get; } = Guid.NewGuid();
    private Guid _databaseGeneration = Guid.NewGuid();
    private string? _databasePath;
    private bool _disposed;
    private bool _disposeRequested;

    public DataQuerySession(IDataSpreadsheetEngine spreadsheet, IDataDatabaseEngine database)
    {
        _spreadsheet = spreadsheet ?? throw new ArgumentNullException(nameof(spreadsheet));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _bridge = new DataWorkbookDatabaseBridge(spreadsheet, database);
    }

    public bool IsOpen { get { lock (_stateLock) return _databasePath is not null; } }

    public async Task<DataQuerySessionSnapshot> OpenAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            lock (_stateLock)
                if (_databasePath is not null)
                    throw new InvalidOperationException("A database is already open in this Data query session.");

            try
            {
                await _database.OpenAsync(databasePath, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException cancelled)
            {
                await CleanupCancelledOpenAsync(databasePath, cancelled).ConfigureAwait(false);
                throw;
            }
            if (cancellationToken.IsCancellationRequested)
            {
                var cancelled = new OperationCanceledException(cancellationToken);
                await CleanupCancelledOpenAsync(databasePath, cancelled).ConfigureAwait(false);
                throw cancelled;
            }
            lock (_stateLock)
            {
                _databasePath = databasePath;
                _databaseGeneration = Guid.NewGuid();
                _publishedTables.Clear();
                _recentQueries.Clear();
                _recentAttempts.Clear();
            }
            return Snapshot();
        }
        finally { _operationGate.Release(); }
    }

    public async Task<DataPublishedTable> PublishRangeAsync(
        string workbookId,
        DataRangeRequest sourceRange,
        string tableName,
        bool firstRowIsHeaders = false,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            EnsureOpen();
            var published = await _bridge.PublishRangeAsync(
                workbookId,
                sourceRange,
                tableName,
                firstRowIsHeaders,
                cancellationToken).ConfigureAwait(false);
            TrackPublishedTable(published);
            return published;
        }
        finally { _operationGate.Release(); }
    }

    /// <summary>Protected publication path: verifies the active workbook and database against the exact approved source identity.</summary>
    public async Task<DataPublishedTable> PublishApprovedRangeAsync(string workbookId,
        DataApprovedRangeSnapshot approvedSnapshot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        ArgumentNullException.ThrowIfNull(approvedSnapshot);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            var databasePath = EnsureOpen();
            if (!string.Equals(workbookId, approvedSnapshot.WorkbookId, StringComparison.Ordinal)
                || !string.Equals(databasePath, approvedSnapshot.DatabasePath, StringComparison.Ordinal))
                throw new DataStaleApprovedSourceException();

            var published = await _bridge.PublishApprovedRangeAsync(workbookId, approvedSnapshot, cancellationToken)
                .ConfigureAwait(false);
            TrackPublishedTable(published);
            return published;
        }
        finally { _operationGate.Release(); }
    }

    public async Task<DataQueryExecution> ExecuteAsync(
        string sql,
        int maxRows = 200,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        if (maxRows is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(maxRows));
        var queuedAtUtc = DateTimeOffset.UtcNow;
        var queuedExecutionID = Guid.NewGuid();
        string? queuedDatabasePath;
        Guid queuedDatabaseGeneration;
        lock (_stateLock)
        {
            queuedDatabasePath = _disposed || _disposeRequested ? null : _databasePath;
            queuedDatabaseGeneration = _databaseGeneration;
        }
        var queuedTargetIdentity = queuedDatabasePath is null
            ? Fingerprint("database-target", "<unbound>")
            : Fingerprint("database-target", queuedDatabasePath);
        var queryFingerprint = Fingerprint("query", sql.Trim());
        try
        {
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (queuedDatabasePath is not null)
                RecordAttempt(new DataQueryAttempt(queuedExecutionID, SessionID,
                    queuedDatabaseGeneration, queuedTargetIdentity, queryFingerprint,
                    queuedAtUtc, DateTimeOffset.UtcNow, DataQueryAttemptOutcome.Cancelled, "Cancelled", null));
            throw;
        }
        try
        {
            ThrowIfUnavailable();
            string? databasePath;
            Guid databaseGeneration;
            lock (_stateLock)
            {
                databasePath = _databasePath;
                databaseGeneration = _databaseGeneration;
            }
            if (!string.Equals(queuedDatabasePath, databasePath, StringComparison.Ordinal)
                || queuedDatabaseGeneration != databaseGeneration)
            {
                RecordAttempt(new DataQueryAttempt(queuedExecutionID, SessionID, queuedDatabaseGeneration,
                    queuedTargetIdentity, queryFingerprint, queuedAtUtc, DateTimeOffset.UtcNow,
                    DataQueryAttemptOutcome.Stale, DataStaleQueryTargetException.ErrorCode, null));
                throw new DataStaleQueryTargetException();
            }
            if (databasePath is null)
                throw new InvalidOperationException("No database is open in this Data query session.");

            var executionID = queuedExecutionID;
            var startedAtUtc = queuedAtUtc;
            var targetIdentity = queuedTargetIdentity;
            try
            {
                var result = await _database.ExecuteReadOnlyAsync(sql, maxRows, cancellationToken).ConfigureAwait(false);
                // Do not publish a query into successful history when cancellation won
                // the race with an engine that completed without observing its token.
                cancellationToken.ThrowIfCancellationRequested();
                result = CaptureResult(result, maxRows);
                var execution = new DataQueryExecution(sql.Trim(), maxRows, result, DateTimeOffset.UtcNow)
                { ExecutionID = executionID };
                lock (_stateLock)
                {
                    _recentQueries.Insert(0, execution);
                    if (_recentQueries.Count > MaximumRecentQueries)
                        _recentQueries.RemoveRange(MaximumRecentQueries, _recentQueries.Count - MaximumRecentQueries);
                    RecordAttemptLocked(new DataQueryAttempt(executionID, SessionID, databaseGeneration,
                        targetIdentity, queryFingerprint,
                        startedAtUtc, DateTimeOffset.UtcNow, DataQueryAttemptOutcome.Succeeded, null, execution.ExecutionID));
                }
                return execution;
            }
            catch (OperationCanceledException)
            {
                RecordAttempt(new DataQueryAttempt(executionID, SessionID, databaseGeneration, targetIdentity, queryFingerprint,
                    startedAtUtc, DateTimeOffset.UtcNow, DataQueryAttemptOutcome.Cancelled, "Cancelled", null));
                throw;
            }
            catch (InvalidDataException)
            {
                RecordAttempt(new DataQueryAttempt(executionID, SessionID, databaseGeneration, targetIdentity, queryFingerprint,
                    startedAtUtc, DateTimeOffset.UtcNow, DataQueryAttemptOutcome.Failed, "InvalidResult", null));
                throw;
            }
            catch
            {
                // Keep raw provider text and exception details out of the observable history.
                RecordAttempt(new DataQueryAttempt(executionID, SessionID, databaseGeneration, targetIdentity, queryFingerprint,
                    startedAtUtc, DateTimeOffset.UtcNow, DataQueryAttemptOutcome.Failed, "QueryFailed", null));
                throw;
            }
        }
        finally { _operationGate.Release(); }
    }

    public async Task<DataMaterializedQueryResult> MaterializeAsync(
        string workbookId,
        DataQueryExecution execution,
        string sheetName,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            EnsureOpen();
            ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
            ArgumentNullException.ThrowIfNull(execution);
            ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);
            lock (_stateLock)
                if (!_recentQueries.Any(candidate => ReferenceEquals(candidate, execution)))
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
        finally { _operationGate.Release(); }
    }

    public DataQuerySessionSnapshot Snapshot()
    {
        ThrowIfDisposed();
        lock (_stateLock)
        {
            var path = _databasePath ?? throw new InvalidOperationException("No database is open in this Data query session.");
            return new DataQuerySessionSnapshot(path, _publishedTables.ToArray(), _recentQueries.ToArray())
            {
                RecentAttempts = _recentAttempts.ToArray()
            };
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? path;
            lock (_stateLock)
            {
                if (_disposed) return;
                path = _databasePath;
                if (path is null)
                {
                    if (_disposeRequested) _disposed = true;
                    return;
                }
            }

            // Keep the logical session open until the engine confirms close. If worker
            // shutdown fails, callers can still inspect state and retry.
            await _database.CloseAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                _databasePath = null;
                _publishedTables.Clear();
                _recentQueries.Clear();
                _recentAttempts.Clear();
                if (_disposeRequested) _disposed = true;
            }
        }
        finally { _operationGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            string? path;
            lock (_stateLock)
            {
                if (_disposed) return;
                _disposeRequested = true;
                path = _databasePath;
                if (path is null)
                {
                    _disposed = true;
                    return;
                }
            }
            // A failed close is observable and leaves path/history intact. A later
            // DisposeAsync or CloseAsync retries the same connection; it is never
            // silently forgotten or reopened under a new identity.
            await _database.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            lock (_stateLock)
            {
                _databasePath = null;
                _publishedTables.Clear();
                _recentQueries.Clear();
                _recentAttempts.Clear();
                _disposed = true;
            }
        }
        finally { _operationGate.Release(); }
    }

    private string EnsureOpen()
    {
        lock (_stateLock)
            return _databasePath ?? throw new InvalidOperationException("No database is open in this Data query session.");
    }

    private void TrackPublishedTable(DataPublishedTable published)
    {
        lock (_stateLock)
        {
            var index = _publishedTables.FindIndex(table =>
                string.Equals(table.Name, published.Name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                _publishedTables[index] = published;
            else
                _publishedTables.Add(published);
        }
    }

    private void RecordAttempt(DataQueryAttempt attempt)
    {
        lock (_stateLock) RecordAttemptLocked(attempt);
    }

    private void RecordAttemptLocked(DataQueryAttempt attempt)
    {
        _recentAttempts.Insert(0, attempt);
        if (_recentAttempts.Count > MaximumRecentAttempts)
            _recentAttempts.RemoveRange(MaximumRecentAttempts, _recentAttempts.Count - MaximumRecentAttempts);
    }

    private static string Fingerprint(string purpose, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(purpose + "\0" + value);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static DataQueryResult CaptureResult(DataQueryResult result, int maxRows)
    {
        if (result is null || result.Columns is null || result.Rows is null)
            throw new InvalidDataException("Database engine returned an incomplete query result.");
        if (result.Rows.Count > maxRows)
            throw new InvalidDataException("Database engine exceeded the requested query row limit.");
        var columns = result.Columns.ToArray();
        if (columns.Any(column => column is null))
            throw new InvalidDataException("Database engine returned a null query column name.");
        var rows = result.Rows.Select(row =>
        {
            if (row is null)
                throw new InvalidDataException("Database engine returned a null query row.");
            if (row.Count != columns.Length)
                throw new InvalidDataException("Query result rows do not match the result column count.");
            if (row.Any(value => value is null))
                throw new InvalidDataException("Database engine returned a null query cell.");
            return (IReadOnlyList<string>)Array.AsReadOnly(row.ToArray());
        }).ToArray();
        return new DataQueryResult(Array.AsReadOnly(columns), Array.AsReadOnly(rows), result.Truncated);
    }

    private void ThrowIfDisposed()
    {
        lock (_stateLock) ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void ThrowIfUnavailable()
    {
        lock (_stateLock) ObjectDisposedException.ThrowIf(_disposed || _disposeRequested, this);
    }

    private async Task CleanupCancelledOpenAsync(string databasePath, OperationCanceledException cancelled)
    {
        try { await _database.CloseAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception closeError)
        {
            // The open may have taken effect before cancellation was observed.
            // Preserve exact path custody and surface both failures for close retry.
            lock (_stateLock)
            {
                _databasePath = databasePath;
                _databaseGeneration = Guid.NewGuid();
                _disposeRequested = true;
            }
            throw new AggregateException("Database open was cancelled and cleanup failed; the connection remains tracked.", cancelled, closeError);
        }
    }
}
