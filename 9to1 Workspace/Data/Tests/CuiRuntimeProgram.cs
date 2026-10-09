using HavenOS.Apps.Data;
using HavenOS.Apps.Data.Cui;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var surface = DataCuiSurfaceDefinition.LoadDefault();
var spreadsheet = new HeldSpreadsheet();
var query = new UnusedDatabase();
await using (var controller = new DataCuiWorkspaceController(new(spreadsheet), new(spreadsheet, query), surface, new Approval()))
{
    await controller.OpenAsync("fixture.ods");
    var manual = controller.CommitFormulaBarAsync("first");
    await spreadsheet.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var typed = controller.ExecuteAiActionAsync(new("typed", null, null, new DataEditCellAction(2, 3, "intended")));
    controller.SelectCell(4, 5);
    var queuedManual = controller.CommitFormulaBarAsync("manual target");
    controller.SelectCell(6, 7);
    spreadsheet.Release.SetResult();
    await manual;
    var result = await typed;
    Assert(result.Outcome == DataWorkbookActionOutcome.Succeeded, "The authorised typed edit failed.");
    await queuedManual;
    Assert(spreadsheet.Edits.Count == 3 && spreadsheet.Edits[1] is { Row: 2, Column: 3 },
        "A queued typed edit followed a changed UI selection instead of its original cell.");
    Assert(spreadsheet.Edits[2] is { Row: 4, Column: 5 } &&
        controller.Current.Selection is { Row: 4, Column: 5, Address: "F5" },
        "A queued formula-bar edit followed a changed selection instead of its original cell.");
}

var staleSpreadsheet = new HeldSpreadsheet();
var pendingApproval = new HeldApproval();
await using (var stale = new DataCuiWorkspaceController(new(staleSpreadsheet), new(staleSpreadsheet, new UnusedDatabase()), surface, pendingApproval))
{
    await stale.OpenAsync("fixture.ods");
    var waiting = stale.ExecuteAiActionAsync(new("old-sheet", null, null, new DataEditCellAction(1, 1, "old context")));
    await pendingApproval.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await stale.SelectSheetAsync(1);
    pendingApproval.Release.SetResult();
    var refused = await waiting;
    Assert(refused.Outcome == DataWorkbookActionOutcome.Failed && staleSpreadsheet.Edits.Count == 0,
        "An edit approved for the original sheet mutated a different active sheet.");
}

var held = new HeldSpreadsheet();
var database = new UnusedDatabase();
var closing = new DataCuiWorkspaceController(new(held), new(held, database), surface);
await closing.OpenAsync("fixture.ods");
var active = closing.CommitFormulaBarAsync("drain before close");
await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
var originalClose = closing.DisposeAsync().AsTask();
Assert(ReferenceEquals(originalClose, closing.DisposeAsync().AsTask()), "Repeated close replaced the original drain task.");
Assert(!originalClose.IsCompleted && held.CloseCalls == 0, "Close retired the workbook while its original edit was active.");
var late = closing.CommitFormulaBarAsync("late");
held.Release.SetResult();
await active;
await originalClose;
try { await late; throw new InvalidOperationException("A late edit entered a closed workbook."); }
catch (ObjectDisposedException) { }
Assert(held.CloseCalls == 1 && held.Edits.Count == 1, "Close did not join the original operation exactly once.");
Console.WriteLine("Data CUI original-cell, original-sheet and close-drain controls passed (managed seams; donor/runtime/graphical acceptance unrun).");

sealed class Approval : IDataWorkbookActionAuthorizer
{
    public ValueTask<DataWorkbookActionAuthorization> AuthorizeAsync(DataWorkbookActionRequest request, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(DataWorkbookActionAuthorization.Allow());
}

sealed class HeldApproval : IDataWorkbookActionAuthorizer
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async ValueTask<DataWorkbookActionAuthorization> AuthorizeAsync(DataWorkbookActionRequest request, CancellationToken cancellationToken = default)
    { Entered.SetResult(); await Release.Task.WaitAsync(cancellationToken); return DataWorkbookActionAuthorization.Allow(); }
}

sealed class HeldSpreadsheet : IDataSpreadsheetEngine
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<DataCellAddress> Edits { get; } = [];
    public int CloseCalls { get; private set; }
    public Task<DataWorkbookHandle> OpenAsync(string path, bool readOnly, CancellationToken cancellationToken = default)
        => Task.FromResult(new DataWorkbookHandle("workbook", path, readOnly));
    public Task<IReadOnlyList<DataSheetSummary>> ListSheetsAsync(string workbookId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DataSheetSummary>>([new("Sheet 1", 0), new("Sheet 2", 1)]);
    public Task<DataRangeSnapshot> ReadRangeAsync(string workbookId, DataRangeRequest range, CancellationToken cancellationToken = default)
        => Task.FromResult(new DataRangeSnapshot(range.Sheet, range.StartRow, range.StartColumn,
            Enumerable.Range(0, range.RowCount).Select(_ => (IReadOnlyList<string>)Enumerable.Repeat("", range.ColumnCount).ToArray()).ToArray()));
    public async Task<DataCellSnapshot> SetCellAsync(string workbookId, DataCellAddress address, string? value, string? formula = null, CancellationToken cancellationToken = default)
    {
        Edits.Add(address);
        if (Edits.Count == 1) { Entered.SetResult(); await Release.Task.WaitAsync(cancellationToken); }
        return new(address, value ?? "", formula ?? "");
    }
    public Task RecalculateAsync(string workbookId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CloseAsync(string workbookId, CancellationToken cancellationToken = default) { CloseCalls++; return Task.CompletedTask; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public Task SaveAsync(string workbookId, string destinationPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<DataRangeSnapshot> CreateSheetWithValuesAsync(string workbookId, string sheetName, IReadOnlyList<IReadOnlyList<string>> values, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<DataNamedRangeSummary>> ListNamedRangesAsync(string workbookId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<DataNamedRangeSummary> CreateNamedRangeAsync(string workbookId, string name, DataRangeRequest range, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteNamedRangeAsync(string workbookId, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<DataListValidationState> GetListValidationAsync(string workbookId, DataRangeRequest range, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<DataListValidationState> ApplyListValidationAsync(string workbookId, DataRangeRequest range, IReadOnlyList<string> values, bool allowBlank = true, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<DataListValidationState> ClearValidationAsync(string workbookId, DataRangeRequest range, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task InsertRowsAsync(string workbookId, string sheet, int index, int count, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteRowsAsync(string workbookId, string sheet, int index, int count, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task InsertColumnsAsync(string workbookId, string sheet, int index, int count, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteColumnsAsync(string workbookId, string sheet, int index, int count, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

sealed class UnusedDatabase : IDataDatabaseEngine
{
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public Task OpenAsync(string databasePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task ReplaceTableAsync(DataTableSnapshot table, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<DataQueryResult> ExecuteReadOnlyAsync(string sql, int maxRows = 200, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task CloseAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
