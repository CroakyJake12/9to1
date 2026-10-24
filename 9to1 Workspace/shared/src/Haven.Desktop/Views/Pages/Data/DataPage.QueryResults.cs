using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Data;

public sealed partial class DataPage
{
    private const string QueryAttemptHistoryMetadataKey = "sql.executionHistory.v1";
    private const int MaximumPersistedQueryAttempts = 100;

    private sealed record PersistedQueryAttemptHistory(int SchemaVersion, IReadOnlyList<PersistedQueryAttempt> Attempts);

    private sealed record QueryRunIdentity(
        long Generation,
        DataWorkbook Workbook,
        DataQuery Query,
        Guid WorkbookId,
        Guid QueryId,
        string Sql,
        string? SourceFingerprint);

    private sealed record DataQueryProviderAcknowledgement(Guid ExecutionId, DataQueryResultsSnapshot Snapshot);

    private sealed record PersistedQueryAttempt(
        Guid ExecutionId,
        Guid QueryId,
        Guid WorkbookId,
        Guid SourceRevisionId,
        int SourceVersion,
        string TargetId,
        string Dialect,
        string SqlSHA256,
        string Classification,
        bool IncludedUnsavedChanges,
        string? InputSnapshotSHA256,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset CompletedAtUtc,
        long DurationMilliseconds,
        string Outcome,
        string ErrorCode,
        int? ResultRowCount,
        int? ResultColumnCount,
        bool? ResultTruncated);

    private sealed class DataQueryRejectedException(string code, DataSqlRisk risk)
        : InvalidOperationException("The query was not executed because it is not classified as read-only.")
    {
        public string Code { get; } = code;
        public DataSqlRisk Risk { get; } = risk;
    }

    private DataQueryResultsSnapshot? _lastSuccessfulQuerySnapshot;
    private DataQueryProviderAcknowledgement? _lastQueryProviderAcknowledgement;
    private Task? _originalQueryWithInspector;
    private Task<DataQueryResult>? _originalQueryProvider;
    private DataQueryResultsCuiWindow? _originalQueryInspector;
    internal Task<DataQueryResult>? OriginalQueryProvider => _originalQueryProvider;
    private readonly List<Task> _originalQueryPresentations = [];
    private readonly List<Task> _originalQueryRuns = [];
    private readonly List<Task<DataQueryResult>> _originalQueryProviders = [];
    private readonly List<Exception> _queryInspectorSetupFailures = [];
    private readonly List<Exception> _queryExecutionFailures = [];
    private readonly List<Exception> _queryHistoryPersistenceFailures = [];
    private long _queryRunGeneration;
    internal DataQueryResultsSnapshot? LastQueryProviderAcknowledgement => _lastQueryProviderAcknowledgement?.Snapshot;

    private Task RunOriginalQueryWithInspectorAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_originalQueryWithInspector is { IsCompleted: false } prior) return prior;
        if (_originalQueryProvider is { IsCompleted: false })
            throw new InvalidOperationException("The previous actual query is still running; inspect it before starting another.");
        var generation = checked(++_queryRunGeneration);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _originalQueryWithInspector = RunQueryAfterPublicationAsync(start.Task, generation);
        _originalQueryRuns.RemoveAll(task => task.IsCompletedSuccessfully);
        _originalQueryRuns.Add(_originalQueryWithInspector);
        start.SetResult(); return _originalQueryWithInspector;
    }

    private async Task RunQueryAfterPublicationAsync(Task start, long generation)
    {
        await start;
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var workbook = Workbook; var query = CurrentQuery;
        if (workbook is null || query is null) return;
        var sql = query.Sql;
        var queryId = query.Id; var queryName = query.Name;
        var previous = _lastSuccessfulQuerySnapshot is { } prior &&
            prior.WorkbookId == workbook.Id && prior.QueryId == queryId ? prior : null;
        var runIdentity = new QueryRunIdentity(generation, workbook, query, workbook.Id, queryId, sql, null);
        var started = DateTimeOffset.UtcNow;
        var executionId = Guid.NewGuid();
        var sourceWorkbookId = workbook.Id;
        var sourceRevisionId = workbook.RevisionId;
        var sourceVersion = workbook.Version;
        var sqlFingerprint = SqlFingerprint(sql);
        var clock = Stopwatch.StartNew();
        DataQueryResult result;
        DataWorkbook input;
        string? inputHash = null;
        bool unsaved = _dirty;
        DataSqlSafetyResult? querySafety = null;
        try
        {
            RecalculateWorkbook(); if (_formulaReport.ChangedCells > 0) MarkDirty();
            runIdentity = runIdentity with { SourceFingerprint = QuerySourceFingerprint(workbook) };
            var safety = querySafety = DataSqlSafety.Analyze(sql);
            _route.SetQuerySafety(sql); RenderFormulaState();
            if (!safety.IsReadOnly)
                throw new DataQueryRejectedException($"SqlRiskRejected.{safety.Risk}", safety.Risk);
            // Query the existing canonical model through its SAME service. A private
            // in-memory input snapshot prevents edits/autosave during asynchronous
            // projection from changing the rows behind a claimed source revision.
            var bytes = JsonSerializer.SerializeToUtf8Bytes(workbook);
            input = JsonSerializer.Deserialize<DataWorkbook>(bytes)
                ?? throw new InvalidDataException("The original workbook snapshot is unavailable.");
            if (input.Id != workbook.Id || input.Version != workbook.Version || input.RevisionId != workbook.RevisionId)
                throw new InvalidDataException("The local query input did not preserve its canonical workbook identity.");
            input.Normalize();
            inputHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input)));
            unsaved = _dirty;
            var actual = _originalQueryProvider = _queries.ExecuteReadOnlyAsync(input, sql, 200, CancellationToken.None);
            _originalQueryProviders.RemoveAll(task => task.IsCompletedSuccessfully);
            _originalQueryProviders.Add(actual);
            try { result = await actual; } catch when (actual.IsFaulted) { throw actual.Exception!; }
            if (result.Rows.Count > 200 || result.Columns.Count > 2048 || result.Rows.Any(row => row.Count != result.Columns.Count))
                throw new InvalidDataException("The local query preview returned invalid or unsupported result dimensions.");
        }
        catch (Exception error)
        {
            _queryExecutionFailures.Add(error);
            ObjectDisposedException.ThrowIf(_disposed, this);
            clock.Stop();
            var failure = ClassifyQueryFailure(error, querySafety);
            var persisted = await TryPersistQueryAttemptAsync(workbook, query, runIdentity, new PersistedQueryAttempt(
                executionId, queryId, sourceWorkbookId, sourceRevisionId, sourceVersion,
                $"local-workbook:{sourceWorkbookId:D}", "SQLite", sqlFingerprint,
                (querySafety?.Risk ?? DataSqlRisk.Unknown).ToString(), unsaved, inputHash,
                started, DateTimeOffset.UtcNow, (long)clock.Elapsed.TotalMilliseconds,
                failure.Outcome, failure.ErrorCode, null, null, null));
            var message = failure.UserMessage + (persisted ? string.Empty : " The outcome could not be saved to this workbook.");
            if (!IsCurrentQueryRun(runIdentity)) return;
            var historyNotice = BuildQueryHistoryNotice(query, workbook.Id);
            var status = message + (previous is null ? " No successful result has been returned yet." : " The previous successful result is retained with its original source identity.") + historyNotice;
            if (!IsCurrentQueryRun(runIdentity)) return;
            _route.SetStatus(status);
            if (!IsCurrentQueryRun(runIdentity)) return;
            _bus.Fire("Data.Query.PreviewFailed");
            if (previous is not null && IsCurrentQueryRun(runIdentity))
                await PresentQuerySnapshotAsync(previous, status + " Showing the previous successful execution; these rows are not a new result.", runIdentity);
            return;
        }
        clock.Stop();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var snapshot = new DataQueryResultsSnapshot(input.Id, input.Title, input.Version, input.RevisionId,
            unsaved, queryId, queryName, sql, started, clock.Elapsed, result, inputHash!);
        input = null!; // The inspector keeps returned rows, not a second full workbook.
        // Preserve the genuine provider response independently of workbook-history save
        // and current-UI projection. A stale completion remains inspectable by its owner.
        _lastQueryProviderAcknowledgement = new DataQueryProviderAcknowledgement(executionId, snapshot);
        var successPersisted = await TryPersistQueryAttemptAsync(workbook, query, runIdentity, new PersistedQueryAttempt(
            executionId, queryId, sourceWorkbookId, sourceRevisionId, sourceVersion,
            $"local-workbook:{sourceWorkbookId:D}", "SQLite", sqlFingerprint,
            (querySafety?.Risk ?? DataSqlRisk.Unknown).ToString(), unsaved, inputHash,
            started, DateTimeOffset.UtcNow, (long)clock.Elapsed.TotalMilliseconds,
            "Succeeded", "None", result.Rows.Count, result.Columns.Count, result.Truncated));
        // The provider ACK and persisted attempt are retained independently above. Do not
        // project either into whichever workbook/query happens to be selected now.
        if (!IsCurrentQueryRun(runIdentity)) return;
        // Retain the SAME actual service return before any CUI/window callback can fail.
        _lastSuccessfulQuerySnapshot = snapshot;
        if (!IsCurrentQueryRun(runIdentity)) return;
        _lastQueryResult = result; _route.SetQueryResult(result);
        var historyNoticeAfterSave = BuildQueryHistoryNotice(query, workbook.Id);
        if (!IsCurrentQueryRun(runIdentity)) return;
        _route.SetStatus($"Read-only local preview returned {result.Rows.Count} row(s){(result.Truncated ? " (truncated; total matching rows unknown)" : "")}. Results retain the executed query and source snapshot." +
            (successPersisted ? string.Empty : " The execution outcome could not be saved to this workbook.") + historyNoticeAfterSave);
        if (!IsCurrentQueryRun(runIdentity)) return;
        _bus.Fire("Data.Query.Previewed");
        if (!IsCurrentQueryRun(runIdentity)) return;
        await PresentQuerySnapshotAsync(snapshot,
            "Successful execution snapshot. Editing the workbook or SQL does not refresh these returned rows." + historyNoticeAfterSave,
            runIdentity);
    }

    private async Task PresentQuerySnapshotAsync(DataQueryResultsSnapshot snapshot, string notice, QueryRunIdentity runIdentity)
    {
        if (!IsCurrentQueryRun(runIdentity)) return;
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            if (IsCurrentQueryRun(runIdentity))
                _route.SetStatus("The returned query snapshot is retained. Its full results inspector requires a desktop window; this host has only the inline preview.");
            return;
        }
        Task? actual = null;
        try
        {
            if (!IsCurrentQueryRun(runIdentity)) return;
            var inspector = new DataQueryResultsCuiWindow(snapshot, notice, () => IsCurrentQueryIdentity(runIdentity));
            if (!IsCurrentQueryRun(runIdentity)) return;
            _originalQueryInspector = inspector;
            actual = inspector.PresentOriginalAsync(owner);
            _originalQueryPresentations.RemoveAll(task => task.IsCompletedSuccessfully);
            _originalQueryPresentations.Add(actual);
            await actual;
        }
        catch (Exception error)
        {
            if (actual is null) _queryInspectorSetupFailures.Add(error);
            // The actual faulted presentation remains retained independently of the
            // successful provider ACK. A view failure never discards or re-executes it.
            if (IsCurrentQueryRun(runIdentity))
                _route.SetStatus("The query snapshot is retained, but its results inspector failed: " + QueryMessage(error));
        }
    }

    private void DemandQueryResultsSettledBeforeDispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_originalQueryRuns.Any(task => !task.IsCompleted) || _originalQueryProviders.Any(task => !task.IsCompleted)
            || _originalQueryProvider is { IsCompleted: false }
            || _originalQueryWithInspector is { IsCompleted: false }
            || _originalQueryInspector?.OriginalPresentation is { IsCompleted: false }
            || _originalQueryInspector?.OriginalClose is { IsCompleted: false })
            throw new InvalidOperationException("Close the original query-results dialog and let its query/view tasks settle before closing Data.");
    }
    private static string QueryMessage(Exception error)
    {
        var message = error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions.FirstOrDefault()?.Message ?? error.Message : error.Message;
        return message.Length <= 768 ? message : message[..768] + "…";
    }

    private static (string Outcome, string ErrorCode, string UserMessage) ClassifyQueryFailure(
        Exception error, DataSqlSafetyResult? safety)
    {
        if (error is OperationCanceledException)
            return ("Cancelled", "Cancelled", "The query was cancelled. Previous successful results are retained.");
        if (error is DataQueryRejectedException rejected)
            return ("Rejected", rejected.Code, $"The query was not executed because it is not read-only ({rejected.Risk}).");
        if (safety is { IsReadOnly: false })
            return ("Rejected", $"SqlRiskRejected.{safety.Risk}", $"The query was not executed because it is not read-only ({safety.Risk}).");
        if (error is InvalidDataException)
            return ("Failed", "SqlResultInvalid", "The query provider returned an invalid result. Previous successful results are retained.");
        return ("Failed", "SqlExecutionFailed", "The query could not be completed. Check the SQL, active target, and permissions; previous successful results are retained.");
    }

    private async Task<bool> TryPersistQueryAttemptAsync(
        DataWorkbook workbook,
        DataQuery query,
        QueryRunIdentity runIdentity,
        PersistedQueryAttempt attempt)
    {
        try
        {
            if (!ReferenceEquals(Workbook, workbook) || workbook.Id == Guid.Empty || query.Id != attempt.QueryId ||
                !workbook.Queries.Any(candidate => ReferenceEquals(candidate, query)))
            {
                _queryHistoryPersistenceFailures.Add(new InvalidOperationException(
                    "The SQL attempt could not be persisted because its original workbook or query is no longer available."));
                return false;
            }
            query.Metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
            var history = ReadQueryAttemptHistory(query, workbook.Id);
            var attempts = history.Attempts.ToList();
            var existing = attempts.FindIndex(value => value.ExecutionId == attempt.ExecutionId);
            if (existing >= 0) attempts[existing] = attempt;
            else attempts.Add(attempt);
            if (attempts.Count > MaximumPersistedQueryAttempts)
                attempts.RemoveRange(0, attempts.Count - MaximumPersistedQueryAttempts);
            query.Metadata[QueryAttemptHistoryMetadataKey] = JsonSerializer.Serialize(new PersistedQueryAttemptHistory(1, attempts));
            MarkDirty();
            // SaveAsync updates the owning Data route and workbook projection after its
            // repository await. Keep the caller's UI dispatcher context for the result
            // continuation as well; this method is entered from the query UI action.
            return await SaveAsync("Record SQL execution outcome", CancellationToken.None,
                () => IsCurrentQueryRun(runIdentity));
        }
        catch (Exception error)
        {
            _queryHistoryPersistenceFailures.Add(error);
            return false;
        }
    }

    private static PersistedQueryAttemptHistory ReadQueryAttemptHistory(DataQuery query, Guid workbookId)
    {
        if (!query.Metadata.TryGetValue(QueryAttemptHistoryMetadataKey, out var json))
            return new PersistedQueryAttemptHistory(1, Array.Empty<PersistedQueryAttempt>());
        var history = JsonSerializer.Deserialize<PersistedQueryAttemptHistory>(json)
            ?? throw new InvalidDataException("Stored SQL execution history is empty or invalid.");
        if (history.SchemaVersion != 1 || history.Attempts is null || history.Attempts.Count > MaximumPersistedQueryAttempts)
            throw new InvalidDataException("Stored SQL execution history uses an unsupported schema version.");
        var executionIds = new HashSet<Guid>();
        foreach (var attempt in history.Attempts)
        {
            if (attempt is null || attempt.ExecutionId == Guid.Empty || !executionIds.Add(attempt.ExecutionId) ||
                attempt.QueryId != query.Id || attempt.WorkbookId != workbookId || attempt.SourceVersion < 0 ||
                attempt.TargetId != $"local-workbook:{workbookId:D}" || attempt.Dialect != "SQLite" ||
                !Enum.TryParse<DataSqlRisk>(attempt.Classification, ignoreCase: false, out var risk) || !Enum.IsDefined(risk) ||
                !IsSha256(attempt.SqlSHA256) ||
                (attempt.InputSnapshotSHA256 is not null && !IsSha256(attempt.InputSnapshotSHA256)) ||
                attempt.StartedAtUtc.Offset != TimeSpan.Zero || attempt.CompletedAtUtc.Offset != TimeSpan.Zero ||
                attempt.CompletedAtUtc < attempt.StartedAtUtc || attempt.DurationMilliseconds < 0 ||
                !IsValidQueryAttemptOutcome(attempt, risk))
                throw new InvalidDataException("Stored SQL execution history contains an incompatible or invalid attempt.");
        }
        return history;
    }

    private static bool IsValidQueryAttemptOutcome(PersistedQueryAttempt attempt, DataSqlRisk risk)
    {
        var hasResultCounts = attempt.ResultRowCount is >= 0 && attempt.ResultColumnCount is >= 0;
        if ((attempt.ResultRowCount is null) != (attempt.ResultColumnCount is null) ||
            (attempt.ResultTruncated is not null && !hasResultCounts))
            return false;
        return attempt.Outcome switch
        {
            "Succeeded" => attempt.ErrorCode == "None" && hasResultCounts &&
                attempt.ResultRowCount <= 200 && attempt.ResultColumnCount <= 2048 && attempt.ResultTruncated is not null &&
                attempt.Classification == DataSqlRisk.ReadOnly.ToString(),
            "Cancelled" => attempt.ErrorCode == "Cancelled" && !hasResultCounts,
            "Rejected" => attempt.ErrorCode == $"SqlRiskRejected.{risk}" && risk != DataSqlRisk.ReadOnly && !hasResultCounts,
            "Failed" => (attempt.ErrorCode is "SqlResultInvalid" or "SqlExecutionFailed") && !hasResultCounts,
            _ => false,
        };
    }

    private static bool IsSha256(string value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static string BuildQueryHistoryNotice(DataQuery query, Guid workbookId)
    {
        try
        {
            var recent = ReadQueryAttemptHistory(query, workbookId).Attempts
                .OrderByDescending(attempt => attempt.CompletedAtUtc)
                .Take(5)
                .ToArray();
            if (recent.Length == 0) return string.Empty;
            var entries = string.Join(" · ", recent.Select(attempt =>
                $"{attempt.ExecutionId.ToString("N")[..8]} {attempt.Outcome}/{attempt.ErrorCode} {attempt.CompletedAtUtc.ToLocalTime():G}" +
                (attempt.ResultRowCount is { } rows ? $" {rows} rows" : string.Empty)));
            return $" Recent SQL attempts for query {query.Id:D}: {entries}.";
        }
        catch (Exception)
        {
            // Do not reinterpret or overwrite unsupported history metadata.
            return " Existing SQL execution history could not be interpreted and was left unchanged.";
        }
    }

    private static string SqlFingerprint(string sql) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(sql)));

    private bool IsCurrentQueryRun(QueryRunIdentity run)
    {
        if (!IsCurrentQueryIdentity(run))
            return false;
        return run.SourceFingerprint is null ||
            string.Equals(QuerySourceFingerprint(run.Workbook), run.SourceFingerprint, StringComparison.Ordinal);
    }

    private bool IsCurrentQueryIdentity(QueryRunIdentity run)
    {
        return !_disposed && run.Generation == _queryRunGeneration &&
            ReferenceEquals(Workbook, run.Workbook) && run.Workbook.Id == run.WorkbookId &&
            ReferenceEquals(CurrentQuery, run.Query) && run.Query.Id == run.QueryId &&
            string.Equals(run.Query.Sql, run.Sql, StringComparison.Ordinal) &&
            run.Workbook.Queries.Any(candidate => ReferenceEquals(candidate, run.Query));
    }

    private static string QuerySourceFingerprint(DataWorkbook workbook)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(workbook);
        var snapshot = JsonSerializer.Deserialize<DataWorkbook>(bytes)
            ?? throw new InvalidDataException("The workbook source identity could not be captured.");
        snapshot.Normalize();
        // These are persistence bookkeeping rather than query inputs. The attempt record
        // itself must not make the captured source appear stale after the owning save.
        snapshot.Version = 0;
        snapshot.UpdatedAt = default;
        snapshot.Metadata.Remove("lastSaveReason");
        snapshot.Recovery = new DataRecoveryState();
        foreach (var savedQuery in snapshot.Queries)
            savedQuery.Metadata.Remove(QueryAttemptHistoryMetadataKey);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot)));
    }
}
