using HavenOS.Apps.Data;
using HavenOS.Apps.Data.Cui;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static async Task VerifyStaleQueryActionAsync(DataWorkbookAction action, string originalPath, string replacementPath)
{
    var surface = DataCuiSurfaceDefinition.LoadDefault();
    var spreadsheet = new HeldSpreadsheet();
    var database = new TrackingDatabase();
    var approval = new GateApproval();
    await using var controller = new DataCuiWorkspaceController(
        new(spreadsheet), new(spreadsheet, database), surface, approval);
    await controller.OpenAsync("fixture.ods");
    await controller.OpenQueryPanelAsync(originalPath);
    var approvedSourceFingerprint = controller.Current.Grid.ContentFingerprint();

    var pending = controller.ExecuteAiActionAsync(new("stale-query-action", null, null, action));
    await approval.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var approvedTarget = approval.Request?.Action switch
    {
        DataRunReadOnlyQueryAction run => run.DatabaseTarget,
        DataPublishVisibleRangeAction publish => publish.DatabaseTarget,
        DataCloseQueryPanelAction close => close.DatabaseTarget,
        _ => null,
    };
    Assert(string.Equals(approvedTarget, originalPath, StringComparison.Ordinal),
        "The authorizer did not receive the explicit original SQL target identity.");
    if (action is DataPublishVisibleRangeAction)
        Assert(approval.Request?.Action is DataPublishVisibleRangeAction
        {
            SourceWorkbookId: "workbook",
            SourceSheet: "Sheet 1",
            QuerySessionGeneration: 1,
            SourceContentFingerprint: var fingerprint,
            SourceRange: { Sheet: "Sheet 1", StartRow: 0, StartColumn: 0, RowCount: 10, ColumnCount: 8 },
        } && fingerprint == approvedSourceFingerprint,
        "Database publication approval did not include its exact workbook/sheet/range/content target.");
    if (action is DataRunReadOnlyQueryAction)
        Assert(approval.Request?.Action is DataRunReadOnlyQueryAction { QuerySessionGeneration: 1 },
            "SQL approval did not include its exact query-session generation.");
    if (action is DataCloseQueryPanelAction)
        Assert(approval.Request?.Action is DataCloseQueryPanelAction { QuerySessionGeneration: 1 },
            "Connection-close approval did not include its exact query-session generation.");
    await controller.CloseQueryPanelAsync();
    await controller.OpenQueryPanelAsync(replacementPath);
    var openCalls = database.OpenCalls;
    var closeCalls = database.CloseCalls;
    var queryCalls = database.QueryCalls;
    var replaceCalls = database.ReplaceCalls;
    var stateBeforeStaleResult = controller.Current;

    approval.Release.SetResult();
    var result = await pending;
    Assert(result.Outcome == DataWorkbookActionOutcome.Failed &&
        database.OpenCalls == openCalls && database.CloseCalls == closeCalls &&
        database.QueryCalls == queryCalls && database.ReplaceCalls == replaceCalls &&
        ReferenceEquals(controller.Current, stateBeforeStaleResult) &&
        controller.Current.Query is { IsOpen: true } &&
        controller.Current.Query.DatabaseTarget == replacementPath,
        $"Stale {action.ActionId} executed against a replaced query session ({originalPath} -> {replacementPath}).");
    if (action is DataRunReadOnlyQueryAction)
        Assert(result.ErrorCode == "StaleQuerySession" &&
            !result.Message.Contains(originalPath, StringComparison.Ordinal) &&
            !result.Message.Contains(replacementPath, StringComparison.Ordinal),
            "A stale SQL action exposed its old provider target in the user-facing error.");
}

static async Task VerifyStalePublicationSourceAsync(bool changeSheet)
{
    var surface = DataCuiSurfaceDefinition.LoadDefault();
    var spreadsheet = new HeldSpreadsheet();
    var database = new TrackingDatabase();
    var approval = new GateApproval();
    await using var controller = new DataCuiWorkspaceController(
        new(spreadsheet), new(spreadsheet, database), surface, approval);
    await controller.OpenAsync("fixture.ods");
    await controller.OpenQueryPanelAsync("analytics.duckdb");
    var approvedSourceFingerprint = controller.Current.Grid.ContentFingerprint();
    var pending = controller.ExecuteAiActionAsync(new(
        "stale-publication-source", null, null, new DataPublishVisibleRangeAction("PinnedRange")));
    await approval.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(approval.Request?.Action is DataPublishVisibleRangeAction
    {
        SourceWorkbookId: "workbook",
        SourceSheet: "Sheet 1",
        QuerySessionGeneration: 1,
        SourceContentFingerprint: var fingerprint,
        SourceRange: { Sheet: "Sheet 1", StartRow: 0, StartColumn: 0, RowCount: 10, ColumnCount: 8 },
    } && fingerprint == approvedSourceFingerprint,
        "Database publication approval did not pin its original source content.");
    if (changeSheet)
        await controller.SelectSheetAsync(1);
    else
        await controller.FilterVisibleAsync(0, "changed-range-visibility");
    var replaceCalls = database.ReplaceCalls;
    var stateBeforeStaleResult = controller.Current;
    approval.Release.SetResult();
    var result = await pending;
    Assert(result.Outcome == DataWorkbookActionOutcome.Failed && database.ReplaceCalls == replaceCalls &&
        ReferenceEquals(controller.Current, stateBeforeStaleResult) &&
        controller.Current.Query.DatabaseTarget == "analytics.duckdb",
        "An approved database publication used a changed sheet or visible-range snapshot.");
}

static async Task VerifyPublicationRejectsChangedBackendBytesAsync()
{
    var surface = DataCuiSurfaceDefinition.LoadDefault();
    var spreadsheet = new HeldSpreadsheet();
    var database = new TrackingDatabase();
    var approval = new GateApproval();
    await using var controller = new DataCuiWorkspaceController(
        new(spreadsheet), new(spreadsheet, database), surface, approval);
    await controller.OpenAsync("fixture.ods");
    await controller.OpenQueryPanelAsync("analytics.duckdb");
    var approvedFingerprint = controller.Current.Grid.ContentFingerprint();
    var pending = controller.ExecuteAiActionAsync(new(
        "publication-detached-bytes", null, null, new DataPublishVisibleRangeAction("ApprovedBytes")));
    await approval.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(approval.Request?.Action is DataPublishVisibleRangeAction { SourceContentFingerprint: var fingerprint } &&
        fingerprint == approvedFingerprint,
        "The authorizer did not receive the source-content fingerprint for the approved bytes.");

    // Change the producer after the controller cached the UI snapshot. The current
    // bridge rereads this backend and will publish the new value unless it accepts
    // and verifies the detached source captured at approval time.
    spreadsheet.ExternalFirstCellValue = "changed-after-approval";
    var backendSnapshot = await spreadsheet.ReadRangeAsync(
        "workbook", new DataRangeRequest("Sheet 1", 0, 0, DataGridSession.VisibleRows, DataGridSession.VisibleColumns));
    Assert(backendSnapshot.Values[0][0] == "changed-after-approval" &&
        controller.Current.Grid.Values[0][0] == "R0C0",
        "The held-backend fixture did not separate actual producer bytes from the cached UI snapshot.");
    var replaceCalls = database.ReplaceCalls;
    var stateBeforeRelease = controller.Current;
    approval.Release.SetResult();
    var result = await pending;
    Assert(result.Outcome == DataWorkbookActionOutcome.Failed && database.ReplaceCalls == replaceCalls &&
        ReferenceEquals(controller.Current, stateBeforeRelease),
        "Publication reread and committed backend bytes that differ from the approved detached range.");
}

var surface = DataCuiSurfaceDefinition.LoadDefault();
Assert(surface.Document.Actions["OpenQueryDatabase"].Command == DataOpenQueryPanelAction.Id &&
    surface.Document.Actions["RunReadOnlyQuery"].Command == DataRunReadOnlyQueryAction.Id &&
    surface.Document.Actions["PublishVisibleRange"].Command == DataPublishVisibleRangeAction.Id,
    "Data CUI query commands are not routed through their canonical typed action IDs.");
Assert(surface.Elements["sheet-tabs"].TryGetLiteralAttribute("action", out var sheetAction) &&
    sheetAction == DataSelectSheetAction.Id,
    "Data sheet tabs are not bound to their typed worksheet-selection action.");
Assert(surface.Elements["sort-column"].TryGetLiteralAttribute("items", out var sortOptions) && sortOptions == "columns" &&
    surface.Elements["filter-column"].TryGetLiteralAttribute("items", out var filterOptions) && filterOptions == "columns",
    "Sort/filter column controls are not populated from the shared workbook column options.");
Assert(new DataPublishVisibleRangeAction("target").Effect == DataWorkbookActionEffect.DatabaseMutation &&
    new DataPublishVisibleRangeAction("target").RequiresPerActionApproval,
    "Database publication is not typed as an explicitly approved database mutation.");
Assert(new DataWorkbookActionRequest("a\nb", "c", null, new DataEditCellAction(0, 0, "x")).ActionFingerprint() !=
    new DataWorkbookActionRequest("a", "b\nc", null, new DataEditCellAction(0, 0, "x")).ActionFingerprint(),
    "Exact-action approval fingerprint encoding is ambiguous across caller/task field boundaries.");
var sheetGrid = new HeldSpreadsheet();
await using (var sheetController = new DataCuiWorkspaceController(
    new(sheetGrid), new(sheetGrid, new UnusedDatabase()), surface, new Approval()))
{
    await sheetController.OpenAsync("fixture.ods");
    try
    {
        await sheetController.ExecuteReadOnlyQueryAsync("SELECT 1");
        throw new InvalidOperationException("SQL without an active target unexpectedly executed.");
    }
    catch (DataCuiQueryException error)
    {
        Assert(error.Code == "ConnectionUnavailable" &&
            error.Message == "Open a DuckDB target before running SQL.",
            "A missing query target did not produce a stable actionable safe diagnostic.");
    }
    sheetController.SelectCell(4, 2);
    var changedSheet = await sheetController.ExecuteAiActionAsync(new(
        "select-sheet", null, null, new DataSelectSheetAction(1)));
    Assert(changedSheet.Outcome == DataWorkbookActionOutcome.Succeeded &&
        sheetController.Current.ActiveSheet is { Index: 1, Name: "Sheet 2" } &&
        sheetController.Current.Selection is { Row: 0, Column: 0, Address: "A1" } &&
        sheetController.Current.FormulaBarText == "R0C0" && !sheetController.Current.Filter.Enabled,
        "The typed sheet-tab action did not change the active workbook grid and reset sheet-local view state.");
    var stateBeforeInvalidSheet = sheetController.Current;
    var invalidSheet = await sheetController.ExecuteAiActionAsync(new(
        "invalid-sheet", null, null, new DataSelectSheetAction(99)));
    Assert(invalidSheet.Outcome == DataWorkbookActionOutcome.Failed &&
        ReferenceEquals(sheetController.Current, stateBeforeInvalidSheet),
        "An invalid typed worksheet selection mutated the current workbook state.");
}
var mismatchGrid = new HeldSpreadsheet();
var mismatchDatabase = new TrackingDatabase();
await using (var mismatchController = new DataCuiWorkspaceController(
    new(mismatchGrid), new(mismatchGrid, mismatchDatabase), surface, new MismatchedPublishApproval()))
{
    await mismatchController.OpenAsync("fixture.ods");
    await mismatchController.OpenQueryPanelAsync("target.duckdb");
    var mismatch = await mismatchController.ExecuteAiActionAsync(new(
        "mismatched-publish", null, null, new DataPublishVisibleRangeAction("RequestedTable")));
    Assert(mismatch.Outcome == DataWorkbookActionOutcome.Denied && mismatchDatabase.ReplaceCalls == 0 &&
        mismatchController.Current.Query.DatabaseTarget == "target.duckdb",
        "Database publication executed without approval for its exact table-name payload.");
}
var mismatchBytesGrid = new HeldSpreadsheet();
var mismatchBytesDatabase = new TrackingDatabase();
await using (var mismatchBytesController = new DataCuiWorkspaceController(
    new(mismatchBytesGrid), new(mismatchBytesGrid, mismatchBytesDatabase), surface, new MismatchedSourceFingerprintApproval()))
{
    await mismatchBytesController.OpenAsync("fixture.ods");
    await mismatchBytesController.OpenQueryPanelAsync("target.duckdb");
    var mismatch = await mismatchBytesController.ExecuteAiActionAsync(new(
        "mismatched-source-fingerprint", null, null, new DataPublishVisibleRangeAction("RequestedTable")));
    Assert(mismatch.Outcome == DataWorkbookActionOutcome.Denied && mismatchBytesDatabase.ReplaceCalls == 0,
        "Database publication executed without approval for its exact captured source bytes.");
}
foreach (var staleAction in new DataWorkbookAction[]
{
    new DataRunReadOnlyQueryAction("select 1"),
    new DataPublishVisibleRangeAction("StaleTable"),
    new DataCloseQueryPanelAction(),
})
{
    await VerifyStaleQueryActionAsync(staleAction, "analytics-a.duckdb", "analytics-b.duckdb");
    await VerifyStaleQueryActionAsync(staleAction, "analytics-a.duckdb", "analytics-a.duckdb");
}
await VerifyStalePublicationSourceAsync(changeSheet: true);
await VerifyStalePublicationSourceAsync(changeSheet: false);
await VerifyPublicationRejectsChangedBackendBytesAsync();
var deniedGrid = new HeldSpreadsheet();
var deniedDatabase = new TrackingDatabase();
await using (var deniedController = new DataCuiWorkspaceController(
    new(deniedGrid), new(deniedGrid, deniedDatabase), surface, new DenyApproval()))
{
    await deniedController.OpenAsync("fixture.ods");
    var denied = await deniedController.ExecuteAiActionAsync(new(
        "denied-open", null, null, new DataOpenQueryPanelAction("private.duckdb")));
    Assert(denied.Outcome == DataWorkbookActionOutcome.Denied && deniedDatabase.OpenedPath is null &&
        !deniedController.Current.Query.IsOpen,
        "A denied database-open action reached the provider or changed query state.");
}
var queryGrid = new HeldSpreadsheet();
var queryDatabase = new TrackingDatabase();
const string privateDatabasePath = "/users/alice/finance/analytics.duckdb";
await using (var queryController = new DataCuiWorkspaceController(
    new(queryGrid), new(queryGrid, queryDatabase), surface, new Approval()))
{
    await queryController.OpenAsync("fixture.ods");
    queryController.SelectCell(4, 2);
    Assert(queryController.Current.Sheets.Count == 2 &&
        queryController.Current.ActiveSheet.Name == queryController.Current.Workbook.ActiveSheet.Name &&
        ReferenceEquals(queryController.Current.Grid, queryController.Current.Workbook.Grid) &&
        queryController.Current.Columns.SequenceEqual(["A", "B", "C", "D", "E", "F", "G", "H"]),
        "CUI sheet/grid paths were not projections of the canonical workbook snapshot.");
    Assert(queryController.CreateAiContext().AvailableActions.Any(action => action.ActionId == DataOpenQueryPanelAction.Id),
        "The closed query target did not expose its typed open action.");
    var opened = await queryController.ExecuteAiActionAsync(new(
        "open-query", null, null, new DataOpenQueryPanelAction(privateDatabasePath)));
    Assert(opened.Outcome == DataWorkbookActionOutcome.Succeeded &&
        queryDatabase.OpenedPath == privateDatabasePath &&
        queryController.Current.Query is
        {
            IsOpen: true,
            DatabaseName: "analytics.duckdb",
            DatabaseTarget: "/users/alice/finance/analytics.duckdb",
            DatabasePathDraft: "/users/alice/finance/analytics.duckdb",
        },
        "The authorized CUI query-open action did not connect the requested database and publish its open state.");
    Assert(queryController.CreateAiContext().Query is { DatabaseName: "analytics.duckdb" },
        "The AI context exposed the private absolute database path instead of its display name.");
    var queryResult = await queryController.ExecuteAiActionAsync(new(
        "run-query", null, null, new DataRunReadOnlyQueryAction("select 1")));
    Assert(queryResult.Outcome == DataWorkbookActionOutcome.Succeeded &&
        queryController.Current.Query.ResultColumns.SequenceEqual(["answer"]) &&
        queryController.Current.Query.ResultRows.Count == 1 &&
        queryController.Current.Query.ResultRows[0][0] == "42" &&
        queryController.Current.Query.ResultTruncated &&
        queryController.Current.Query.ResultStatus.Contains("truncated", StringComparison.Ordinal),
        "Successful SQL output was not projected into the paths bound by the CUI result table.");
    var priorExecution = queryController.Current.Query.LastExecution;
    var successfulHistoryCount = queryController.Current.Query.RecentQueries?.Count ?? 0;
    queryDatabase.HoldNextQuery = true;
    var pendingQuery = queryController.ExecuteAiActionAsync(new(
        "cancelled-run", null, null, new DataRunReadOnlyQueryAction("select 2")));
    await queryDatabase.QueryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(queryController.Current.Query.IsExecuting && queryController.CreateAiContext().AvailableActions.Any(
        action => action.ActionId == DataCancelReadOnlyQueryAction.Id),
        "The active query did not expose its genuine cancel action/state.");
    var cancelResult = await queryController.ExecuteAiActionAsync(new(
        "cancel-query", null, null, new DataCancelReadOnlyQueryAction()));
    var cancelledQuery = await pendingQuery.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(cancelResult.Outcome == DataWorkbookActionOutcome.Succeeded &&
        cancelledQuery.Outcome == DataWorkbookActionOutcome.Cancelled && cancelledQuery.ErrorCode == "SqlCancelled" &&
        cancelledQuery.DiagnosticException is OperationCanceledException &&
        queryController.Current.Query.LastError is { Code: "SqlCancelled" } cancellationError &&
        !cancellationError.Message.Contains("provider", StringComparison.OrdinalIgnoreCase) &&
        ReferenceEquals(queryController.Current.Query.LastExecution, priorExecution) &&
        queryController.Current.Query.RecentQueries?.Count == successfulHistoryCount &&
        !queryController.Current.Query.IsExecuting && queryController.CreateAiContext().AvailableActions.All(
            action => action.ActionId != DataCancelReadOnlyQueryAction.Id),
        "Cancelling a held query fabricated a successful result, replaced prior output, duplicated history, or left cancel available after completion.");
    queryDatabase.FailQuery = true;
    const string manualSecret = "synthetic-secret-marker /private/manual.duckdb token=manual-secret";
    queryDatabase.FailQueryMessage = manualSecret;
    DataCuiQueryException? manualQueryFailure = null;
    try { await queryController.ExecuteReadOnlyQueryAsync("manual invalid sql"); }
    catch (DataCuiQueryException error) { manualQueryFailure = error; }
    Assert(manualQueryFailure is { Code: "SqlExecutionFailed" } &&
        !manualQueryFailure.Message.Contains("synthetic-secret-marker", StringComparison.Ordinal) &&
        ReferenceEquals(manualQueryFailure.OriginalException, queryDatabase.LastQueryException) &&
        queryController.Current.Query.LastError is { Code: "SqlExecutionFailed" } stateError &&
        !stateError.Message.Contains("synthetic-secret-marker", StringComparison.Ordinal) &&
        ReferenceEquals(queryController.Current.Query.LastExecution, priorExecution),
        "Manual SQL failure did not keep its exact task diagnostic private while exposing a safe error and retaining prior results.");

    const string typedSecret = "synthetic-secret-marker /private/typed.duckdb token=typed-secret";
    queryDatabase.FailQueryMessage = typedSecret;
    var failedQuery = await queryController.ExecuteAiActionAsync(new(
        "failed-query", null, null, new DataRunReadOnlyQueryAction("invalid sql")));
    var failedQueryWireResult = System.Text.Json.JsonSerializer.Serialize(failedQuery);
    Assert(failedQuery.Outcome == DataWorkbookActionOutcome.Failed &&
        failedQuery.ErrorCode == "SqlExecutionFailed" &&
        !failedQuery.Message.Contains("synthetic-secret-marker", StringComparison.Ordinal) &&
        !failedQueryWireResult.Contains("synthetic-secret-marker", StringComparison.Ordinal) &&
        !failedQueryWireResult.Contains("/private/typed.duckdb", StringComparison.Ordinal) &&
        ReferenceEquals(failedQuery.DiagnosticException, queryDatabase.LastQueryException) &&
        ReferenceEquals(queryController.Current.Query.LastExecution, priorExecution) &&
        queryController.Current.Query.ResultRows[0][0] == "42" &&
        queryController.Current.Query.RecentQueries is { Count: 1 } &&
        queryController.Current.Query.LastError is { Code: "SqlExecutionFailed" } &&
        !queryController.Current.Query.ErrorMessage!.Contains("synthetic-secret-marker", StringComparison.Ordinal) &&
        failedQuery.Context.Query is { ResultTruncated: true, ResultRowCount: 1 },
        "A typed SQL failure leaked provider details, lost its task diagnostic, or discarded the last result.");
    queryDatabase.FailQuery = false;
    var queryCallsBeforeInvalidInput = queryDatabase.QueryCalls;
    try { await queryController.ExecuteReadOnlyQueryAsync(" "); throw new InvalidOperationException("Blank SQL input was accepted."); }
    catch (DataCuiQueryException error)
    {
        Assert(error.Diagnostic.Code == "SqlInputInvalid" &&
            error.Message == "Enter a non-empty SQL statement." &&
            queryDatabase.QueryCalls == queryCallsBeforeInvalidInput,
            "A locally classifiable SQL input error did not retain a stable actionable diagnostic.");
    }
    await queryController.ExecuteAiActionAsync(new(
        "run-query-again", null, null, new DataRunReadOnlyQueryAction("select 2")));
    Assert(queryController.Current.Query.RecentQueries is { Count: 2 } recent &&
        recent[0].Sql == "select 2" && recent[1].Sql == "select 1" &&
        queryController.Current.Query.HistoryRows[0][1] == privateDatabasePath &&
        queryController.Current.Query.HistoryRows[0][2] == "select 2" &&
        queryController.Current.Query.HistoryRows[1][2] == "select 1" &&
        queryController.Current.Query.HistoryRows[0][3] == "1" &&
        queryController.Current.Query.HistoryRows[0][4] == "Yes" &&
        !string.IsNullOrWhiteSpace(queryController.Current.Query.HistoryRows[0][0]),
        "CUI query history is not a current-session projection of the canonical query session.");
    Assert(queryController.Current.Query.LastError is null,
        "A later successful SQL execution did not clear the prior query error state.");
    var published = await queryController.ExecuteAiActionAsync(new(
        "publish-range", null, null, new DataPublishVisibleRangeAction("VisibleCells")));
    Assert(published.Outcome == DataWorkbookActionOutcome.Succeeded &&
        queryDatabase.ReplacedTable?.Name == "VisibleCells" &&
        queryController.CreateAiContext().AvailableActions.Single(action => action.ActionId == DataPublishVisibleRangeAction.Id) is
        { Effect: DataWorkbookActionEffect.DatabaseMutation, RequiresPerActionApproval: true },
        "The visible-range CUI action did not publish through the shared workbook/database bridge.");
    var duplicate = await queryController.ExecuteAiActionAsync(new(
        "open-query-again", null, null, new DataOpenQueryPanelAction("other.duckdb")));
    Assert(duplicate.Outcome == DataWorkbookActionOutcome.Failed &&
        queryDatabase.OpenedPath == privateDatabasePath &&
        queryController.Current.Query is { IsOpen: true, DatabaseName: "analytics.duckdb", DatabaseTarget: "/users/alice/finance/analytics.duckdb" },
        "A failed second database open replaced the active query target or corrupted its open state.");
    queryDatabase.FailClose = true;
    var closeFailure = await queryController.ExecuteAiActionAsync(new(
        "close-query-failure", null, null, new DataCloseQueryPanelAction()));
    Assert(closeFailure.Outcome == DataWorkbookActionOutcome.Failed &&
        queryController.Current.Query is { IsOpen: true, DatabaseName: "analytics.duckdb", DatabaseTarget: "/users/alice/finance/analytics.duckdb" } &&
        queryController.Current.Selection is { Row: 4, Column: 2, Address: "C5" },
        "A failed database close discarded the still-open query target from CUI state.");
    queryDatabase.FailClose = false;
    var closed = await queryController.ExecuteAiActionAsync(new(
        "close-query", null, null, new DataCloseQueryPanelAction()));
    Assert(closed.Outcome == DataWorkbookActionOutcome.Succeeded && queryDatabase.CloseCalls == 2 &&
        queryController.Current.Query is { IsOpen: false, DatabaseName: "" } &&
        queryController.Current.Query.DatabaseTarget == "" &&
        queryController.Current.Query.DatabasePathDraft == privateDatabasePath &&
        queryController.Current.Query.ResultColumns.Count == 0 &&
        queryController.Current.Query.ResultRows.Count == 0 &&
        queryController.Current.Query.HistoryRows.Count == 0 &&
        queryController.Current.Selection is { Row: 4, Column: 2, Address: "C5" },
        "The typed close action did not close the database and clear query-only state.");
    Assert(queryController.CreateAiContext().AvailableActions.Any(action => action.ActionId == DataOpenQueryPanelAction.Id) &&
        queryController.CreateAiContext().AvailableActions.All(action => action.ActionId != DataCloseQueryPanelAction.Id),
        "The query action catalogue did not follow the connection lifecycle.");
    var reopened = await queryController.ExecuteAiActionAsync(new(
        "reopen-query", null, null, new DataOpenQueryPanelAction("replacement.duckdb")));
    Assert(reopened.Outcome == DataWorkbookActionOutcome.Succeeded &&
        queryDatabase.OpenedPath == "replacement.duckdb" &&
        queryController.Current.Query is { IsOpen: true, DatabaseName: "replacement.duckdb" },
        "Closing the current target did not permit an explicit connection switch.");
}

var cancellationRaceGrid = new HeldSpreadsheet();
var cancellationRaceDatabase = new TrackingDatabase { BlockNextQueryUntilReleased = true };
var cancellationRaceApproval = new HoldCancelApproval();
await using (var cancellationRace = new DataCuiWorkspaceController(
    new(cancellationRaceGrid), new(cancellationRaceGrid, cancellationRaceDatabase), surface, cancellationRaceApproval))
{
    await cancellationRace.OpenAsync("fixture.ods");
    await cancellationRace.OpenQueryPanelAsync("race.duckdb");
    var runA = cancellationRace.ExecuteAiActionAsync(new(
        "run-a", null, null, new DataRunReadOnlyQueryAction("select 'A'")));
    await cancellationRaceDatabase.QueryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(cancellationRace.Current.Query.IsExecuting && cancellationRace.CreateAiContext().AvailableActions.Any(
        action => action.ActionId == DataCancelReadOnlyQueryAction.Id),
        "An active query did not expose its genuine CUI cancellation capability/state.");

    var pendingCancelA = cancellationRace.ExecuteAiActionAsync(new(
        "cancel-a", null, null, new DataCancelReadOnlyQueryAction()));
    await cancellationRaceApproval.CancelEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var cancelRequest = cancellationRaceApproval.CancelRequest;
    Assert(cancelRequest?.Action is DataCancelReadOnlyQueryAction
        { DatabaseTarget: "race.duckdb", QuerySessionGeneration: 1, ExecutionId: not null },
        "The approved cancel action did not bind the exact original query execution identity.");

    cancellationRaceDatabase.QueryRelease.SetResult();
    var completedA = await runA.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(completedA.Outcome == DataWorkbookActionOutcome.Succeeded &&
        !cancellationRace.Current.Query.IsExecuting &&
        cancellationRace.CreateAiContext().AvailableActions.All(action => action.ActionId != DataCancelReadOnlyQueryAction.Id),
        "The completed query remained advertised as cancellable.");

    cancellationRaceDatabase.QueryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    cancellationRaceDatabase.QueryRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    cancellationRaceDatabase.BlockNextQueryUntilReleased = true;
    var runB = cancellationRace.ExecuteAiActionAsync(new(
        "run-b", null, null, new DataRunReadOnlyQueryAction("select 'B'")));
    await cancellationRaceDatabase.QueryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    cancellationRaceApproval.ReleaseCancel.SetResult();
    var staleCancel = await pendingCancelA.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(staleCancel.Outcome == DataWorkbookActionOutcome.Failed && staleCancel.ErrorCode == "StaleQueryExecution" &&
        staleCancel.Message.Contains("no other query was cancelled", StringComparison.OrdinalIgnoreCase) &&
        cancellationRace.Current.Query.IsExecuting && !runB.IsCompleted,
        "A delayed approval for completed run A cancelled or replaced active run B.");

    cancellationRaceDatabase.QueryRelease.SetResult();
    var completedB = await runB.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(completedB.Outcome == DataWorkbookActionOutcome.Succeeded &&
        cancellationRace.Current.Query.LastError is null &&
        cancellationRace.Current.Query.RecentQueries is { Count: 2 } recent &&
        recent[0].Sql == "select 'B'" && recent[1].Sql == "select 'A'",
        "Rejecting stale cancellation fabricated a cancellation result or changed successful query history.");
}

var filteredGrid = new HeldSpreadsheet();
await using (var filtered = new DataCuiWorkspaceController(new(filteredGrid), new(filteredGrid, new UnusedDatabase()), surface, new Approval()))
{
    await filtered.OpenAsync("fixture.ods");
    filtered.SelectCell(2, 1);
    var visible = await filtered.FilterVisibleAsync(0, "match");
    Assert(visible.Selection is { Row: 1, Column: 1, Address: "B2" } &&
        visible.FormulaBarText == "R1C1",
        "Filtering left the selected cell hidden or the formula bar detached from the visible selection.");
    var repeated = await filtered.FilterVisibleAsync(0, "match");
    Assert(repeated.Selection is { Row: 1, Column: 1, Address: "B2" } && repeated.FormulaBarText == "R1C1",
        "Repeating a filter moved an already-visible selection or detached its formula-bar value.");
    filteredGrid.FilterVisibility = Enumerable.Repeat(true, DataGridSession.VisibleRows).ToArray();
    var cleared = await filtered.ClearFilterAsync();
    Assert(cleared.Selection is { Row: 1, Column: 1, Address: "B2" } && cleared.FormulaBarText == "R1C1",
        "Clearing a filter did not preserve the selected cell and its formula-bar value.");
    filtered.SelectCell(3, 1);
    filteredGrid.FilterVisibility = [true, false, false, false, false, false, false, false, false, false];
    var headerOnly = await filtered.FilterVisibleAsync(0, "no-matching-data");
    Assert(headerOnly.Selection is { Row: 0, Column: 1, Address: "B1" } && headerOnly.FormulaBarText == "R0C1",
        "A valid header-only filter result did not move a hidden selection onto the visible header.");
    filteredGrid.FilterVisibility = Enumerable.Repeat(true, DataGridSession.VisibleRows).ToArray();
    await filtered.ClearFilterAsync();
    filtered.SelectCell(3, 1);
    filteredGrid.FilterVisibility = [true, true, false, true, false, true, true, true, true, true];
    var visibleSelection = await filtered.FilterVisibleAsync(0, "other");
    Assert(visibleSelection.Selection is { Row: 3, Column: 1, Address: "B4" } && visibleSelection.FormulaBarText == "R3C1",
        "Applying a filter moved a selection that remained visible.");
    var hiddenSelect = await filtered.ExecuteAiActionAsync(new("hidden-select", null, null, new DataSelectCellAction(2, 1)));
    Assert(hiddenSelect.Outcome == DataWorkbookActionOutcome.Failed &&
        filtered.Current.Selection is { Row: 3, Column: 1, Address: "B4" },
        "A typed selection action selected a row hidden by the active filter.");
}

foreach (var malformedVisibility in new IReadOnlyList<bool>[]
{
    [true, false],
    Enumerable.Repeat(false, DataGridSession.VisibleRows).ToArray(),
})
{
    var malformedFilterGrid = new HeldSpreadsheet { FilterVisibility = malformedVisibility };
    await using (var malformedFilter = new DataCuiWorkspaceController(
        new(malformedFilterGrid), new(malformedFilterGrid, new UnusedDatabase()), surface))
    {
        await malformedFilter.OpenAsync("fixture.ods");
        try
        {
            await malformedFilter.FilterVisibleAsync(0, "malformed-visibility");
            throw new InvalidOperationException("Malformed header-aware filter visibility was accepted.");
        }
        catch (InvalidDataException) { }
    }
}

var saveGrid = new HeldSpreadsheet();
await using (var saveController = new DataCuiWorkspaceController(
    new(saveGrid), new(saveGrid, new UnusedDatabase()), surface))
{
    await saveController.OpenAsync("fixture.ods");
    saveGrid.Release.SetResult();
    await saveController.CommitFormulaBarAsync("durable value");
    saveGrid.FailSave = true;
    try { await saveController.SaveAsAsync("copy.ods"); throw new InvalidOperationException("The failed-save fixture unexpectedly succeeded."); }
    catch (IOException) { }
    Assert(saveController.Current.IsDirty && saveGrid.SavedPath is null &&
        saveController.Current.FormulaBarText == "durable value",
        "A failed Save As cleared dirty state or discarded the pending cell input.");
    saveGrid.FailSave = false;
    var saved = await saveController.SaveAsAsync("copy.ods");
    Assert(!saved.IsDirty && saveGrid.SavedPath == "copy.ods" &&
        saved.Selection is { Address: "A1" } && saved.FormulaBarText == "durable value",
        "A successful Save As did not persist through the spreadsheet engine or preserve editor state.");
}

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
    Console.WriteLine("Data CUI filter/selection, SQL target/history/error and stale-approval, Save As, original-cell/sheet and close-drain controls passed (managed seams; donor/runtime/graphical acceptance unrun).");

sealed class Approval : IDataWorkbookActionAuthorizer
{
    public ValueTask<DataWorkbookActionAuthorization> AuthorizeAsync(DataWorkbookActionRequest request, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(DataWorkbookActionAuthorization.AllowFor(request));
}

sealed class HoldCancelApproval : IDataWorkbookActionAuthorizer
{
    public TaskCompletionSource CancelEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseCancel { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public DataWorkbookActionRequest? CancelRequest { get; private set; }

    public async ValueTask<DataWorkbookActionAuthorization> AuthorizeAsync(
        DataWorkbookActionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Action is DataCancelReadOnlyQueryAction)
        {
            CancelRequest = request;
            CancelEntered.SetResult();
            await ReleaseCancel.Task.WaitAsync(cancellationToken);
        }
        return DataWorkbookActionAuthorization.AllowFor(request);
    }
}

sealed class DenyApproval : IDataWorkbookActionAuthorizer
{
    public ValueTask<DataWorkbookActionAuthorization> AuthorizeAsync(DataWorkbookActionRequest request, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(DataWorkbookActionAuthorization.Deny("Denied by regression fixture."));
}

sealed class MismatchedPublishApproval : IDataWorkbookActionAuthorizer
{
    public ValueTask<DataWorkbookActionAuthorization> AuthorizeAsync(DataWorkbookActionRequest request, CancellationToken cancellationToken = default)
    {
        var differentRequest = request with { Action = new DataPublishVisibleRangeAction("DifferentTable") };
        return ValueTask.FromResult(DataWorkbookActionAuthorization.AllowFor(differentRequest));
    }
}

sealed class MismatchedSourceFingerprintApproval : IDataWorkbookActionAuthorizer
{
    public ValueTask<DataWorkbookActionAuthorization> AuthorizeAsync(DataWorkbookActionRequest request, CancellationToken cancellationToken = default)
    {
        var differentRequest = request with
        {
            Action = ((DataPublishVisibleRangeAction)request.Action) with { SourceContentFingerprint = "different-source-bytes" },
        };
        return ValueTask.FromResult(DataWorkbookActionAuthorization.AllowFor(differentRequest));
    }
}

sealed class HeldApproval : IDataWorkbookActionAuthorizer
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async ValueTask<DataWorkbookActionAuthorization> AuthorizeAsync(DataWorkbookActionRequest request, CancellationToken cancellationToken = default)
    { Entered.SetResult(); await Release.Task.WaitAsync(cancellationToken); return DataWorkbookActionAuthorization.AllowFor(request); }
}

sealed class GateApproval : IDataWorkbookActionAuthorizer
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public DataWorkbookActionRequest? Request { get; private set; }
    public async ValueTask<DataWorkbookActionAuthorization> AuthorizeAsync(DataWorkbookActionRequest request, CancellationToken cancellationToken = default)
    {
        Request = request;
        Entered.SetResult();
        await Release.Task.WaitAsync(cancellationToken);
        return DataWorkbookActionAuthorization.AllowFor(request);
    }
}

sealed class HeldSpreadsheet : IDataSpreadsheetEngine
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<DataCellAddress> Edits { get; } = [];
    public IReadOnlyList<bool> FilterVisibility { get; set; } = [true, true, false, true, true, true, true, true, true, true];
    private IReadOnlyList<bool>? ActiveRowVisibility { get; set; }
    public string? SavedPath { get; private set; }
    public bool FailSave { get; set; }
    public string? ExternalFirstCellValue { get; set; }
    public int CloseCalls { get; private set; }
    public Task<DataWorkbookHandle> OpenAsync(string path, bool readOnly, CancellationToken cancellationToken = default)
        => Task.FromResult(new DataWorkbookHandle("workbook", path, readOnly));
    public Task<IReadOnlyList<DataSheetSummary>> ListSheetsAsync(string workbookId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DataSheetSummary>>([new("Sheet 1", 0), new("Sheet 2", 1)]);
    public Task<DataRangeSnapshot> ReadRangeAsync(string workbookId, DataRangeRequest range, CancellationToken cancellationToken = default)
        => Task.FromResult(new DataRangeSnapshot(range.Sheet, range.StartRow, range.StartColumn,
            Enumerable.Range(0, range.RowCount)
                .Select(row => (IReadOnlyList<string>)Enumerable.Range(0, range.ColumnCount)
                    .Select(column => row == 0 && column == 0 && ExternalFirstCellValue is { } changed
                        ? changed
                        : $"R{range.StartRow + row}C{range.StartColumn + column}").ToArray()).ToArray(),
            ActiveRowVisibility));
    public async Task<DataCellSnapshot> SetCellAsync(string workbookId, DataCellAddress address, string? value, string? formula = null, CancellationToken cancellationToken = default)
    {
        Edits.Add(address);
        if (Edits.Count == 1) { Entered.SetResult(); await Release.Task.WaitAsync(cancellationToken); }
        return new(address, value ?? "", formula ?? "");
    }
    public Task<DataRangeSnapshot> FilterEqualsAsync(string workbookId, DataRangeRequest range, int keyColumnOffset, string value, bool containsHeader = true, CancellationToken cancellationToken = default)
    {
        ActiveRowVisibility = FilterVisibility;
        return Task.FromResult(new DataRangeSnapshot(range.Sheet, range.StartRow, range.StartColumn,
            Enumerable.Range(0, range.RowCount)
                .Select(row => (IReadOnlyList<string>)Enumerable.Range(0, range.ColumnCount)
                    .Select(column => row == 0 && column == 0 && ExternalFirstCellValue is { } changed
                        ? changed
                        : $"R{range.StartRow + row}C{range.StartColumn + column}").ToArray()).ToArray(),
            ActiveRowVisibility));
    }
    public Task<DataRangeSnapshot> ClearFilterAsync(string workbookId, DataRangeRequest range, bool containsHeader = true, CancellationToken cancellationToken = default)
    {
        ActiveRowVisibility = Enumerable.Repeat(true, range.RowCount).ToArray();
        return Task.FromResult(new DataRangeSnapshot(range.Sheet, range.StartRow, range.StartColumn,
            Enumerable.Range(0, range.RowCount)
                .Select(row => (IReadOnlyList<string>)Enumerable.Range(0, range.ColumnCount)
                    .Select(column => $"R{range.StartRow + row}C{range.StartColumn + column}").ToArray()).ToArray(),
            ActiveRowVisibility));
    }
    public Task RecalculateAsync(string workbookId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CloseAsync(string workbookId, CancellationToken cancellationToken = default) { CloseCalls++; return Task.CompletedTask; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public Task SaveAsync(string workbookId, string destinationPath, CancellationToken cancellationToken = default)
    {
        if (FailSave) throw new IOException("Simulated Save As failure.");
        SavedPath = destinationPath;
        return Task.CompletedTask;
    }
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

sealed class TrackingDatabase : IDataDatabaseEngine
{
    public string? OpenedPath { get; private set; }
    public DataTableSnapshot? ReplacedTable { get; private set; }
    public int CloseCalls { get; private set; }
    public int OpenCalls { get; private set; }
    public int QueryCalls { get; private set; }
    public int ReplaceCalls { get; private set; }
    public bool FailClose { get; set; }
    public bool FailQuery { get; set; }
    public bool HoldNextQuery { get; set; }
    public bool BlockNextQueryUntilReleased { get; set; }
    public TaskCompletionSource QueryEntered { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource QueryRelease { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string FailQueryMessage { get; set; } = "Simulated SQL execution failure.";
    public IOException? LastQueryException { get; private set; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public Task OpenAsync(string databasePath, CancellationToken cancellationToken = default)
    { OpenCalls++; OpenedPath = databasePath; return Task.CompletedTask; }
    public Task ReplaceTableAsync(DataTableSnapshot table, CancellationToken cancellationToken = default)
    { ReplaceCalls++; ReplacedTable = table; return Task.CompletedTask; }
    public async Task<DataQueryResult> ExecuteReadOnlyAsync(string sql, int maxRows = 200, CancellationToken cancellationToken = default)
    {
        QueryCalls++;
        if (HoldNextQuery)
        {
            HoldNextQuery = false;
            QueryEntered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        if (BlockNextQueryUntilReleased)
        {
            BlockNextQueryUntilReleased = false;
            var entered = QueryEntered;
            var release = QueryRelease;
            entered.SetResult();
            await release.Task.WaitAsync(cancellationToken);
        }
        if (FailQuery)
        {
            LastQueryException = new IOException(FailQueryMessage);
            throw LastQueryException;
        }
        return new DataQueryResult(
            new[] { "answer" },
            new IReadOnlyList<string>[] { new[] { "42" } },
            true);
    }
    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        CloseCalls++;
        if (FailClose) throw new IOException("Simulated database close failure.");
        OpenedPath = null;
        return Task.CompletedTask;
    }
}
