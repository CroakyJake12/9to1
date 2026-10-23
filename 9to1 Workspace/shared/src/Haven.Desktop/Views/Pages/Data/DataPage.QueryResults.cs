using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Data;

public sealed partial class DataPage
{
    private DataQueryResultsSnapshot? _lastSuccessfulQuerySnapshot;
    private Task? _originalQueryWithInspector;
    private Task<DataQueryResult>? _originalQueryProvider;
    private DataQueryResultsCuiWindow? _originalQueryInspector;
    internal Task<DataQueryResult>? OriginalQueryProvider => _originalQueryProvider;
    private readonly List<Task> _originalQueryPresentations = [];
    private readonly List<Task> _originalQueryRuns = [];
    private readonly List<Task<DataQueryResult>> _originalQueryProviders = [];
    private readonly List<Exception> _queryInspectorSetupFailures = [];
    private readonly List<Exception> _queryExecutionFailures = [];

    private Task RunOriginalQueryWithInspectorAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_originalQueryWithInspector is { IsCompleted: false } prior) return prior;
        if (_originalQueryProvider is { IsCompleted: false })
            throw new InvalidOperationException("The previous actual query is still running; inspect it before starting another.");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _originalQueryWithInspector = RunQueryAfterPublicationAsync(start.Task);
        _originalQueryRuns.RemoveAll(task => task.IsCompletedSuccessfully);
        _originalQueryRuns.Add(_originalQueryWithInspector);
        start.SetResult(); return _originalQueryWithInspector;
    }

    private async Task RunQueryAfterPublicationAsync(Task start)
    {
        await start;
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var workbook = Workbook; var query = CurrentQuery;
        if (workbook is null || query is null) return;
        var sql = query.Sql;
        var queryId = query.Id; var queryName = query.Name;
        var previous = _lastSuccessfulQuerySnapshot;
        var started = DateTimeOffset.UtcNow;
        var clock = Stopwatch.StartNew();
        DataQueryResult result;
        DataWorkbook input;
        string inputHash;
        bool unsaved;
        try
        {
            RecalculateWorkbook(); if (_formulaReport.ChangedCells > 0) MarkDirty();
            var safety = DataSqlSafety.Analyze(sql);
            _route.SetQuerySafety(sql); RenderFormulaState();
            if (!safety.IsReadOnly) throw new InvalidOperationException(safety.Message);
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
            var message = "Query did not complete: " + QueryMessage(error);
            _route.SetStatus(message + (previous is null ? " No successful result has been returned yet." : " The previous successful result is retained with its original source identity."));
            _bus.Fire("Data.Query.PreviewFailed");
            if (previous is not null)
                await PresentQuerySnapshotAsync(previous, message + " Showing the previous successful execution; these rows are not a new result.");
            return;
        }
        clock.Stop();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var snapshot = new DataQueryResultsSnapshot(input.Id, input.Title, input.Version, input.RevisionId,
            unsaved, queryId, queryName, sql, started, clock.Elapsed, result, inputHash);
        input = null!; // The inspector keeps returned rows, not a second full workbook.
        // Retain the SAME actual service return before any CUI/window callback can fail.
        _lastSuccessfulQuerySnapshot = snapshot;
        var selectedQuery = CurrentQuery;
        if (ReferenceEquals(Workbook, workbook) && selectedQuery is not null && selectedQuery.Id == queryId && selectedQuery.Sql == sql)
        {
            _lastQueryResult = result; _route.SetQueryResult(result);
        }
        _route.SetStatus($"Read-only local preview returned {result.Rows.Count} row(s){(result.Truncated ? " (truncated; total matching rows unknown)" : "")}. Results retain the executed query and source snapshot.");
        _bus.Fire("Data.Query.Previewed");
        await PresentQuerySnapshotAsync(snapshot, "Successful execution snapshot. Editing the workbook or SQL does not refresh these returned rows.");
    }

    private async Task PresentQuerySnapshotAsync(DataQueryResultsSnapshot snapshot, string notice)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            _route.SetStatus("The returned query snapshot is retained. Its full results inspector requires a desktop window; this host has only the inline preview.");
            return;
        }
        Task? actual = null;
        try
        {
            var inspector = _originalQueryInspector = new(snapshot, notice, () => !Volatile.Read(ref _disposed));
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
}
