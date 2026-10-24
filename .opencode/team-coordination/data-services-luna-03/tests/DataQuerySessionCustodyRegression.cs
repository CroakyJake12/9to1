// Report-owned deterministic regression source. It intentionally avoids SDK,
// DuckDB and external services; wire into the owning test target when the scope is approved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Haven.Core;
using HavenOS.Apps.Data;

namespace DataServicesLuna03.CustodyRegression;

internal static class DataQuerySessionCustodyRegression
{
    public static async Task RunAsync()
    {
        await FailedDisposeCloseRetainsRetryableIdentityAsync();
        await CancelledOpenCleansUpSideEffectAsync();
        await QueuedCloseWaitsForQueryAndClearsHistoryAsync();
        await QueryAttemptsAreBoundedAndSanitizedAsync();
        await ReopenedSessionRejectsStaleMaterializationAsync();
        RecordUpdateInputsAreDetachedAndBounded();
        await QueryResultBoundsPreserveHistoryAndDetachAsync();
        await OversizedWorkbookRangeIsRejectedBeforeReadAsync();
        await ApprovedRangePublicationRejectsStaleBackendDataAsync();
        await MalformedWorkbookSnapshotsAreRejectedBeforePublishAsync();
        await MalformedRangeIdentityIsRejectedBeforeReadAsync();
        await CancelledRangeReadDoesNotPublishAsync();
    }

    private static async Task FailedDisposeCloseRetainsRetryableIdentityAsync()
    {
        var database = new HeldDatabase { CloseFailuresRemaining = 1 };
        await using var session = new DataQuerySession(new FakeSpreadsheet(), database);
        await session.OpenAsync("retained.duckdb");
        await session.ExecuteAsync("SELECT 1");
        await ExpectAsync<IOException>(() => session.DisposeAsync().AsTask());
        Require(session.IsOpen && session.Snapshot().DatabasePath == "retained.duckdb"
            && session.Snapshot().RecentQueries.Count == 1, "Failed disposal discarded database custody/history.");
        await ExpectAsync<ObjectDisposedException>(() => session.ExecuteAsync("SELECT 2"));
        await session.DisposeAsync();
        Require(!session.IsOpen && database.ActivePath is null, "Dispose retry did not finish requested disposal.");
    }

    private static async Task CancelledOpenCleansUpSideEffectAsync()
    {
        var database = new HeldDatabase { CancelOpenAfterSideEffect = true };
        await using var session = new DataQuerySession(new FakeSpreadsheet(), database);
        await ExpectAsync<OperationCanceledException>(() => session.OpenAsync("cancelled.duckdb"));
        Require(database.CloseCalls == 1 && database.ActivePath is null && !session.IsOpen,
            "Cancelled open that had taken effect was not cleaned up.");

        var retryDatabase = new HeldDatabase { CancelOpenAfterSideEffect = true, CloseFailuresRemaining = 1 };
        await using var retrySession = new DataQuerySession(new FakeSpreadsheet(), retryDatabase);
        await ExpectAsync<AggregateException>(() => retrySession.OpenAsync("cancelled-retry.duckdb"));
        Require(retrySession.IsOpen && retrySession.Snapshot().DatabasePath == "cancelled-retry.duckdb"
            && retryDatabase.ActivePath == "cancelled-retry.duckdb", "Failed cancelled-open cleanup lost connection identity.");
        await ExpectAsync<ObjectDisposedException>(() => retrySession.ExecuteAsync("SELECT 1"));
        await retrySession.CloseAsync();
        Require(!retrySession.IsOpen && retryDatabase.ActivePath is null, "Close retry did not release cancelled-open custody.");
        await ExpectAsync<ObjectDisposedException>(() => retrySession.OpenAsync("must-not-reopen.duckdb"));
    }

    private static async Task QueuedCloseWaitsForQueryAndClearsHistoryAsync()
    {
        var database = new HeldDatabase { HoldQuery = true };
        await using var session = new DataQuerySession(new FakeSpreadsheet(), database);
        await session.OpenAsync("queued.duckdb");
        var query = session.ExecuteAsync("SELECT 3");
        await database.QueryEntered.Task;
        using var queuedCancellation = new CancellationTokenSource();
        var queuedQuery = session.ExecuteAsync("SELECT queued-cancel", cancellationToken: queuedCancellation.Token);
        queuedCancellation.Cancel();
        await ExpectAsync<OperationCanceledException>(() => queuedQuery);
        var queuedAttempt = session.Snapshot().RecentAttempts[0];
        Require(queuedAttempt.Outcome == DataQueryAttemptOutcome.Cancelled
            && queuedAttempt.ErrorCode == "Cancelled"
            && queuedAttempt.QueryFingerprint.Length == 64,
            "Cancellation while queued behind another query was not observed in attempt history.");
        var close = session.CloseAsync();
        Require(!close.IsCompleted, "Close bypassed the active query operation gate.");
        database.ReleaseQuery.SetResult();
        await query;
        await close;
        Require(!session.IsOpen && database.CloseCalls == 1, "Queued close did not clear the completed query session.");
    }

    private static async Task QueryAttemptsAreBoundedAndSanitizedAsync()
    {
        var database = new HeldDatabase();
        await using var session = new DataQuerySession(new FakeSpreadsheet(), database);
        await session.OpenAsync("attempts.duckdb");
        var success = await session.ExecuteAsync("SELECT 1");
        var successfulAttempt = session.Snapshot().RecentAttempts[0];
        Require(successfulAttempt.Outcome == DataQueryAttemptOutcome.Succeeded
            && successfulAttempt.ExecutionID == success.ExecutionID
            && successfulAttempt.SuccessfulExecutionID == success.ExecutionID
            && successfulAttempt.SessionID == session.SessionID
            && successfulAttempt.TargetIdentity.Length == 64
            && !successfulAttempt.TargetIdentity.Contains("attempts.duckdb", StringComparison.Ordinal)
            && successfulAttempt.StartedAtUtc <= successfulAttempt.CompletedAtUtc
            && successfulAttempt.ErrorCode is null,
            "Successful query attempt lacks bounded identity/timing or exposes the raw target.");

        var providerFailure = new InvalidOperationException("secret-provider-detail-must-not-enter-history");
        database.NextQueryException = providerFailure;
        Exception? observed = null;
        try { _ = await session.ExecuteAsync("SELECT failing"); }
        catch (InvalidOperationException error) { observed = error; }
        Require(ReferenceEquals(observed, providerFailure), "Query failure did not preserve the original exception.");
        var failedAttempt = session.Snapshot().RecentAttempts[0];
        Require(failedAttempt.Outcome == DataQueryAttemptOutcome.Failed && failedAttempt.ErrorCode == "QueryFailed"
            && failedAttempt.SuccessfulExecutionID is null
            && !failedAttempt.ToString()!.Contains("secret-provider-detail-must-not-enter-history", StringComparison.Ordinal)
            && ReferenceEquals(session.Snapshot().RecentQueries[0], success),
            "Failed query did not retain sanitized attempt and prior successful result history.");

        using var cancellation = new CancellationTokenSource();
        database.CancelOnQuery = cancellation;
        await ExpectAsync<OperationCanceledException>(() => session.ExecuteAsync("SELECT cancelled", cancellationToken: cancellation.Token));
        var cancelledAttempt = session.Snapshot().RecentAttempts[0];
        Require(cancelledAttempt.Outcome == DataQueryAttemptOutcome.Cancelled && cancelledAttempt.ErrorCode == "Cancelled"
            && cancelledAttempt.SuccessfulExecutionID is null
            && ReferenceEquals(session.Snapshot().RecentQueries[0], success),
            "Cancelled query was not recorded truthfully or discarded the last successful result.");

        for (var index = 0; index < 105; index++)
            await session.ExecuteAsync($"SELECT {index}");
        Require(session.Snapshot().RecentAttempts.Count == 100, "Query attempt observation exceeded its in-memory bound.");
    }

    private static async Task ReopenedSessionRejectsStaleMaterializationAsync()
    {
        var spreadsheet = new FakeSpreadsheet();
        var database = new HeldDatabase();
        await using var session = new DataQuerySession(spreadsheet, database);
        await session.OpenAsync("first.duckdb");
        var oldExecution = await session.ExecuteAsync("SELECT 4");
        await session.CloseAsync();
        await session.OpenAsync("second.duckdb");
        await ExpectAsync<InvalidOperationException>(() => session.MaterializeAsync("workbook", oldExecution, "Stale"));
        Require(spreadsheet.CreateSheetCalls == 0, "Stale query materialization reached the spreadsheet store.");
    }

    private static void RecordUpdateInputsAreDetachedAndBounded()
    {
        using var json = JsonDocument.Parse("\"before\"");
        var field = new Guid(900, 0, 0, new byte[8]);
        var original = new DataScalarRecordValue(DataCellKind.Text, json.RootElement);
        var supplied = new Dictionary<Guid, DataScalarRecordValue> { [field] = original };
        var captured = DataRecordEdits.CaptureValues(supplied);
        using var changed = JsonDocument.Parse("\"after\"");
        supplied[field] = new DataScalarRecordValue(DataCellKind.Text, changed.RootElement);
        Require(captured[field].Value.GetString() == "before", "Captured record values still alias the caller-owned dictionary.");

        var oversized = Enumerable.Range(1, 257).ToDictionary(
            index => new Guid(index, 0, 0, new byte[8]),
            _ => new DataScalarRecordValue(DataCellKind.Text, json.RootElement));
        Expect<ArgumentException>(() => DataRecordEdits.CaptureValues(new CountedDictionary(oversized, reportedCount: 1)));
    }

    private static async Task QueryResultBoundsPreserveHistoryAndDetachAsync()
    {
        var database = new HeldDatabase();
        await using var session = new DataQuerySession(new FakeSpreadsheet(), database);
        await session.OpenAsync("bounded-results.duckdb");

        var backendColumns = new List<string> { "original" };
        var backendRow = new List<string> { "before" };
        var backendRows = new List<IReadOnlyList<string>> { backendRow };
        database.EnqueueResult(new DataQueryResult(backendColumns, backendRows, false));
        var successful = await session.ExecuteAsync("SELECT prior", maxRows: 1);
        backendColumns[0] = "mutated";
        backendRow[0] = "mutated";
        Require(successful.Result.Columns[0] == "original" && successful.Result.Rows[0][0] == "before",
            "Captured query result retained mutable engine-owned collections.");
        Expect<NotSupportedException>(() => ((IList<string>)successful.Result.Columns)[0] = "consumer mutation");
        Expect<NotSupportedException>(() => ((IList<string>)successful.Result.Rows[0])[0] = "consumer mutation");

        async Task RefusalKeepsSuccessfulHistory(DataQueryResult invalid, string sql)
        {
            var prior = session.Snapshot().RecentQueries.ToArray();
            database.EnqueueResult(invalid);
            await ExpectAsync<InvalidDataException>(() => session.ExecuteAsync(sql, maxRows: 1));
            var after = session.Snapshot().RecentQueries;
            Require(after.Count == prior.Length && after.Select((item, index) => ReferenceEquals(item, prior[index])).All(equal => equal),
                "Rejected query result changed successful query history.");
        }

        await RefusalKeepsSuccessfulHistory(
            new DataQueryResult(new[] { "value" }, new DeferredRows(2), false),
            "SELECT oversized");
        await RefusalKeepsSuccessfulHistory(
            new DataQueryResult(new[] { "left", "right" }, new IReadOnlyList<string>[] { new[] { "only one" } }, false),
            "SELECT ragged");
        await RefusalKeepsSuccessfulHistory(
            new DataQueryResult(new[] { "value" }, new IReadOnlyList<string>[] { null! }, false),
            "SELECT null-row");

        database.EnqueueResult(new DataQueryResult(new[] { "value" }, Array.Empty<IReadOnlyList<string>>(), false));
        var empty = await session.ExecuteAsync("SELECT empty", maxRows: 1);
        Require(empty.Result.Columns.Count == 1 && empty.Result.Rows.Count == 0 && !empty.Result.Truncated,
            "A valid zero-row query result was rejected or altered.");
        Require(ReferenceEquals(session.Snapshot().RecentQueries[1], successful),
            "A valid empty result discarded the earlier successful query.");
    }

    private static async Task OversizedWorkbookRangeIsRejectedBeforeReadAsync()
    {
        var spreadsheet = new FakeSpreadsheet { ThrowOnRead = true };
        var database = new HeldDatabase();
        var bridge = new DataWorkbookDatabaseBridge(spreadsheet, database);
        await ExpectAsync<ArgumentOutOfRangeException>(() => bridge.PublishRangeAsync(
            "workbook", new DataRangeRequest("Sheet", 0, 0, 10_001, 1), "too-many-rows"));
        await ExpectAsync<ArgumentOutOfRangeException>(() => bridge.PublishRangeAsync(
            "workbook", new DataRangeRequest("Sheet", 0, 0, 2_000, 256), "too-many-cells"));
        Require(spreadsheet.ReadRangeCalls == 0 && database.ReplaceTableCalls == 0,
            "An oversized workbook range reached a store before rejecting its bounds.");
        await ExpectAsync<NotSupportedException>(() => bridge.PublishRangeAsync(
            "workbook", new DataRangeRequest("Sheet", 0, 0, 10_001, 1), "header-plus-limit", firstRowIsHeaders: true));
        Require(spreadsheet.ReadRangeCalls == 1 && database.ReplaceTableCalls == 0,
            "A header row was incorrectly counted against the published-data row limit.");
    }

    private static async Task ApprovedRangePublicationRejectsStaleBackendDataAsync()
    {
        var range = new DataRangeRequest("Sheet", 0, 0, 2, 2);
        var approvedValues = new DataRangeSnapshot("Sheet", 0, 0,
            new IReadOnlyList<string>[] { new[] { "Name", "Score" }, new[] { "Ada", "42" } },
            new[] { true, true });
        var approved = DataApprovedRangeSnapshot.Capture("workbook", "target.duckdb", range,
            "People", firstRowIsHeaders: true, expectedSnapshot: approvedValues);

        await AssertApprovedMismatchAsync(approved, range, new DataRangeSnapshot("Sheet", 0, 0,
            new IReadOnlyList<string>[] { new[] { "Name", "Score" }, new[] { "Grace", "42" } }, new[] { true, true }));
        await AssertApprovedMismatchAsync(approved, range, new DataRangeSnapshot("Sheet", 0, 0,
            new IReadOnlyList<string>[] { new[] { "Person", "Score" }, new[] { "Ada", "42" } }, new[] { true, true }));
        await AssertApprovedMismatchAsync(approved, range, new DataRangeSnapshot("Sheet", 0, 0,
            new IReadOnlyList<string>[] { new[] { "Name", "Score" }, new[] { "Ada", "42" } }, new[] { true, false }));

        var successSpreadsheet = new FakeSpreadsheet { NextReadRange = approvedValues };
        var successDatabase = new HeldDatabase();
        await using (var session = new DataQuerySession(successSpreadsheet, successDatabase))
        {
            await session.OpenAsync("target.duckdb");
            var published = await session.PublishApprovedRangeAsync("workbook", approved);
            Require(successSpreadsheet.ReadRangeCalls == 1 && successDatabase.ReplaceTableCalls == 1
                && published.Name == "People" && successDatabase.LastPublishedTable is { } table && table.Rows.Single()[0] == "Ada",
                "Exact approved source bytes did not publish exactly once.");
        }

        var otherTargetSpreadsheet = new FakeSpreadsheet { NextReadRange = approvedValues };
        var otherTargetDatabase = new HeldDatabase();
        await using (var session = new DataQuerySession(otherTargetSpreadsheet, otherTargetDatabase))
        {
            await session.OpenAsync("other.duckdb");
            await ExpectAsync<DataStaleApprovedSourceException>(() => session.PublishApprovedRangeAsync("workbook", approved));
            Require(otherTargetSpreadsheet.ReadRangeCalls == 0 && otherTargetDatabase.ReplaceTableCalls == 0,
                "Approval for a different active database reached either backing store.");
        }

        var manualSpreadsheet = new FakeSpreadsheet
        {
            NextReadRange = new DataRangeSnapshot("Sheet", 0, 0,
                new IReadOnlyList<string>[] { new[] { "Name", "Score" }, new[] { "Grace", "42" } }, new[] { true, true })
        };
        var manualDatabase = new HeldDatabase();
        var manualBridge = new DataWorkbookDatabaseBridge(manualSpreadsheet, manualDatabase);
        await manualBridge.PublishRangeAsync("workbook", range, "Manual", firstRowIsHeaders: true);
        Require(manualDatabase.ReplaceTableCalls == 1 && manualDatabase.LastPublishedTable is { } manualTable
            && manualTable.Rows.Single()[0] == "Grace",
            "Manual explicit-snapshot publication was incorrectly changed to require an approval identity.");
    }

    private static async Task AssertApprovedMismatchAsync(DataApprovedRangeSnapshot approved,
        DataRangeRequest range, DataRangeSnapshot actual)
    {
        var spreadsheet = new FakeSpreadsheet { NextReadRange = actual };
        var database = new HeldDatabase();
        await using var session = new DataQuerySession(spreadsheet, database);
        await session.OpenAsync("target.duckdb");
        await ExpectAsync<DataStaleApprovedSourceException>(() => session.PublishApprovedRangeAsync("workbook", approved));
        Require(spreadsheet.ReadRangeCalls == 1 && database.ReplaceTableCalls == 0
            && session.Snapshot().PublishedTables.Count == 0,
            $"Approved-source mismatch for {range.Sheet} reached database publication or session history.");
    }

    private static async Task MalformedWorkbookSnapshotsAreRejectedBeforePublishAsync()
    {
        DataRangeSnapshot?[] malformed =
        [
            null,
            new DataRangeSnapshot("Sheet", 0, 0, null!),
            new DataRangeSnapshot("Sheet", 0, 0, new IReadOnlyList<string>[] { null! }),
            new DataRangeSnapshot("Sheet", 0, 0, new IReadOnlyList<string>[] { new string[] { null! } }),
            new DataRangeSnapshot("Sheet", 0, 0, new IReadOnlyList<string>[] { Array.Empty<string>() }),
        ];
        foreach (var response in malformed)
        {
            var spreadsheet = new FakeSpreadsheet { NextReadRange = response };
            var database = new HeldDatabase();
            var bridge = new DataWorkbookDatabaseBridge(spreadsheet, database);
            await ExpectAsync<InvalidDataException>(() => bridge.PublishRangeAsync(
                "workbook", new DataRangeRequest("Sheet", 0, 0, 1, 1), "malformed"));
            Require(spreadsheet.ReadRangeCalls == 1 && database.ReplaceTableCalls == 0,
                "Malformed spreadsheet data reached database publication.");
        }

        var changingSpreadsheet = new FakeSpreadsheet
        {
            NextReadRange = new DataRangeSnapshot("Sheet", 0, 0, new ChangingRows(
                new IReadOnlyList<string>[] { new[] { "Name", "Score" }, new[] { "Ada", "42" } },
                new IReadOnlyList<string>[] { new[] { "Name", "Score" }, new[] { "Mallory", "999" } }))
        };
        var changingDatabase = new HeldDatabase();
        var changingBridge = new DataWorkbookDatabaseBridge(changingSpreadsheet, changingDatabase);
        await changingBridge.PublishRangeAsync("workbook", new DataRangeRequest("Sheet", 0, 0, 2, 2), "snapshot", firstRowIsHeaders: true);
        Require(changingDatabase.LastPublishedTable is { } published && published.Rows.Count == 1 && published.Rows[0][0] == "Ada",
            "Bridge publication reread mutable store values after validating the original snapshot.");
    }

    private static async Task MalformedRangeIdentityIsRejectedBeforeReadAsync()
    {
        var spreadsheet = new FakeSpreadsheet();
        var database = new HeldDatabase();
        var bridge = new DataWorkbookDatabaseBridge(spreadsheet, database);
        await ExpectAsync<ArgumentException>(() => bridge.PublishRangeAsync(
            "workbook", new DataRangeRequest(null!, 0, 0, 1, 1), "bad-sheet"));
        foreach (var range in new[]
        {
            new DataRangeRequest("Sheet", -1, 0, 1, 1),
            new DataRangeRequest("Sheet", 0, -1, 1, 1),
            new DataRangeRequest("Sheet", int.MaxValue, 0, 1, 1),
            new DataRangeRequest("Sheet", 0, int.MaxValue, 1, 1),
        })
            await ExpectAsync<ArgumentOutOfRangeException>(() => bridge.PublishRangeAsync("workbook", range, "bad-range"));
        Require(spreadsheet.ReadRangeCalls == 0 && database.ReplaceTableCalls == 0,
            "Malformed range identity reached a backing store.");
    }

    private static async Task CancelledRangeReadDoesNotPublishAsync()
    {
        using var cancellation = new CancellationTokenSource();
        var spreadsheet = new FakeSpreadsheet
        {
            NextReadRange = new DataRangeSnapshot("Sheet", 0, 0, new IReadOnlyList<string>[] { new[] { "value" } }),
            CancelOnRead = cancellation,
        };
        var database = new HeldDatabase();
        var bridge = new DataWorkbookDatabaseBridge(spreadsheet, database);
        await ExpectAsync<OperationCanceledException>(() => bridge.PublishRangeAsync(
            "workbook", new DataRangeRequest("Sheet", 0, 0, 1, 1), "cancelled", cancellationToken: cancellation.Token));
        Require(spreadsheet.ReadRangeCalls == 1 && database.ReplaceTableCalls == 0,
            "A range cancelled after the store returned was still published to the database.");
    }

    private static async Task ExpectAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class HeldDatabase : IDataDatabaseEngine
    {
        private readonly Queue<DataQueryResult> _results = new();
        public int CloseFailuresRemaining { get; set; }
        public bool CancelOpenAfterSideEffect { get; set; }
        public bool HoldQuery { get; set; }
        public int CloseCalls { get; private set; }
        public int ReplaceTableCalls { get; private set; }
        public DataTableSnapshot? LastPublishedTable { get; private set; }
        public Exception? NextQueryException { get; set; }
        public CancellationTokenSource? CancelOnQuery { get; set; }
        public string? ActivePath { get; private set; }
        public TaskCompletionSource QueryEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseQuery { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void EnqueueResult(DataQueryResult result) => _results.Enqueue(result);

        public Task OpenAsync(string databasePath, CancellationToken cancellationToken = default)
        {
            ActivePath = databasePath;
            if (CancelOpenAfterSideEffect) throw new OperationCanceledException(cancellationToken);
            return Task.CompletedTask;
        }

        public Task ReplaceTableAsync(DataTableSnapshot table, CancellationToken cancellationToken = default)
        {
            ReplaceTableCalls++;
            LastPublishedTable = table;
            return Task.CompletedTask;
        }

        public async Task<DataQueryResult> ExecuteReadOnlyAsync(string sql, int maxRows = 200, CancellationToken cancellationToken = default)
        {
            if (NextQueryException is { } error)
            {
                NextQueryException = null;
                throw error;
            }
            if (HoldQuery)
            {
                QueryEntered.SetResult();
                await ReleaseQuery.Task;
            }
            if (CancelOnQuery is { } cancel)
            {
                CancelOnQuery = null;
                cancel.Cancel();
            }
            return _results.Count > 0
                ? _results.Dequeue()
                : new DataQueryResult(new[] { "value" }, new IReadOnlyList<string>[] { new[] { sql } }, false);
        }

        public Task CloseAsync(CancellationToken cancellationToken = default)
        {
            CloseCalls++;
            if (CloseFailuresRemaining-- > 0) throw new IOException("Held close failure.");
            ActivePath = null;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeSpreadsheet : IDataSpreadsheetEngine
    {
        public int CreateSheetCalls { get; private set; }
        public int ReadRangeCalls { get; private set; }
        public DataRangeSnapshot? NextReadRange { get; set; }
        public CancellationTokenSource? CancelOnRead { get; set; }
        public bool ThrowOnRead { get; set; }
        public Task<DataWorkbookHandle> OpenAsync(string path, bool readOnly, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<IReadOnlyList<DataSheetSummary>> ListSheetsAsync(string workbookId, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<DataRangeSnapshot> ReadRangeAsync(string workbookId, DataRangeRequest range, CancellationToken cancellationToken = default)
        {
            ReadRangeCalls++;
            CancelOnRead?.Cancel();
            if (ThrowOnRead) throw Unsupported();
            return Task.FromResult(NextReadRange!);
        }
        public Task<DataCellSnapshot> SetCellAsync(string workbookId, DataCellAddress address, string? value, string? formula = null, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<DataRangeSnapshot> CreateSheetWithValuesAsync(string workbookId, string sheetName, IReadOnlyList<IReadOnlyList<string>> values, CancellationToken cancellationToken = default)
        {
            CreateSheetCalls++;
            return Task.FromResult(new DataRangeSnapshot(sheetName, 0, 0, values));
        }
        public Task<IReadOnlyList<DataNamedRangeSummary>> ListNamedRangesAsync(string workbookId, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<DataNamedRangeSummary> CreateNamedRangeAsync(string workbookId, string name, DataRangeRequest range, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task DeleteNamedRangeAsync(string workbookId, string name, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<DataListValidationState> GetListValidationAsync(string workbookId, DataRangeRequest range, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<DataListValidationState> ApplyListValidationAsync(string workbookId, DataRangeRequest range, IReadOnlyList<string> values, bool allowBlank = true, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task<DataListValidationState> ClearValidationAsync(string workbookId, DataRangeRequest range, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task InsertRowsAsync(string workbookId, string sheet, int index, int count, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task DeleteRowsAsync(string workbookId, string sheet, int index, int count, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task InsertColumnsAsync(string workbookId, string sheet, int index, int count, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task DeleteColumnsAsync(string workbookId, string sheet, int index, int count, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task RecalculateAsync(string workbookId, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task SaveAsync(string workbookId, string destinationPath, CancellationToken cancellationToken = default) => throw Unsupported();
        public Task CloseAsync(string workbookId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        private static NotSupportedException Unsupported() => new("Not used by this focused regression.");
    }

    private sealed class CountedDictionary(IReadOnlyDictionary<Guid, DataScalarRecordValue> values, int reportedCount)
        : IReadOnlyDictionary<Guid, DataScalarRecordValue>
    {
        public int Count => reportedCount;
        public IEnumerable<Guid> Keys => values.Keys;
        public IEnumerable<DataScalarRecordValue> Values => values.Values;
        public DataScalarRecordValue this[Guid key] => values[key];
        public bool ContainsKey(Guid key) => values.ContainsKey(key);
        public bool TryGetValue(Guid key, out DataScalarRecordValue value) => values.TryGetValue(key, out value!);
        public IEnumerator<KeyValuePair<Guid, DataScalarRecordValue>> GetEnumerator() => values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class DeferredRows(int count) : IReadOnlyList<IReadOnlyList<string>>
    {
        public int Count => count;
        public IReadOnlyList<string> this[int index] => throw new InvalidOperationException("Oversized rows must be rejected before copying.");
        public IEnumerator<IReadOnlyList<string>> GetEnumerator() => throw new InvalidOperationException("Oversized rows must be rejected before enumeration.");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ChangingRows(
        IReadOnlyList<string>[] original,
        IReadOnlyList<string>[] changed) : IReadOnlyList<IReadOnlyList<string>>
    {
        private int _enumerations;
        public int Count => original.Length;
        public IReadOnlyList<string> this[int index] => original[index];
        public IEnumerator<IReadOnlyList<string>> GetEnumerator()
        {
            var current = Interlocked.Increment(ref _enumerations) == 1 ? original : changed;
            foreach (var row in current) yield return row;
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
