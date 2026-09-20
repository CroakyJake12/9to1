using HavenOS.Apps.Data;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var address = new DataCellAddress("Sheet 1", 0, 1);
var cellSnapshot = new DataCellSnapshot(address, "42", "=SUM(A1:A2)");
Assert(cellSnapshot.Address == address && cellSnapshot.Formula == "=SUM(A1:A2)", "Cell contract round trip failed.");
Assert(new DataRangeRequest("Sheet 1", 0, 0, 10, 8) is { RowCount: 10, ColumnCount: 8 }, "P1 grid shape changed unexpectedly.");
Assert(new DataQueryResult(["total"], [["42"]], false) is { Columns.Count: 1, Rows.Count: 1 }, "Query result contract is invalid.");

var fake = new FakeSpreadsheetEngine();
await using var fakeDatabase = new FakeDatabaseEngine();
await fakeDatabase.OpenAsync("fixture.duckdb");
await using (var session = new DataGridSession(fake))
{
    var opened = await session.OpenAsync("fixture.ods");
    Assert(opened.Grid.Values.Count == DataGridSession.VisibleRows, "Grid session did not request 10 visible rows.");
    Assert(opened.Grid.Values.All(row => row.Count == DataGridSession.VisibleColumns), "Grid session did not request 8 visible columns.");
    Assert(opened.ActiveSheet.Name == "Sheet 1", "Grid session did not select the first discovered sheet.");

    var edited = await session.EditCellAsync(0, 0, "5");
    Assert(edited.Grid.Values[0][0] == "5" && fake.RecalculateCalls == 1, "Grid edit/recalculate/refresh failed.");
    var formula = await session.EditCellAsync(0, 1, string.Empty, "=A1*2");
    Assert(formula.Grid.Values[0][1] == "10" && fake.RecalculateCalls == 2, "Formula edit/recalculate/refresh failed.");

    var createdName = await session.CreateNamedRangeAsync("GridInput", 0, 0, 1, 2);
    Assert(createdName.Name == "GridInput" && createdName.Range is { Sheet: "Sheet 1", RowCount: 1, ColumnCount: 2 }, "Grid session created the wrong named range.");
    var names = await session.ListNamedRangesAsync();
    Assert(names.Count == 1 && names[0].Name == "GridInput", "Grid session did not list the created range-backed name.");
    var afterDeleteName = await session.DeleteNamedRangeAsync("gridinput");
    Assert(afterDeleteName.Count == 0, "Grid session did not delete a range-backed name case-insensitively.");

    var validation = await session.ApplyListValidationAsync(1, 2, 2, 1, ["Open", "Closed"], allowBlank: false);
    Assert(validation.Enabled && validation.Values.SequenceEqual(["Open", "Closed"]) && !validation.AllowBlank,
        "Grid session did not apply the requested literal list validation.");
    var validationRead = await session.GetListValidationAsync(1, 2, 2, 1);
    Assert(validationRead == validation, "Grid session did not read back the applied validation state.");
    var clearedValidation = await session.ClearValidationAsync(1, 2, 2, 1);
    Assert(!clearedValidation.Enabled && clearedValidation.Values.Count == 0, "Grid session did not clear list validation.");

    _ = await session.InsertRowsAsync(1);
    _ = await session.DeleteRowsAsync(1);
    _ = await session.InsertColumnsAsync(1);
    _ = await session.DeleteColumnsAsync(1);
    Assert(fake.InsertRowsCalls == 1 && fake.DeleteRowsCalls == 1, "Grid session did not delegate row structural edits exactly once.");
    Assert(fake.InsertColumnsCalls == 1 && fake.DeleteColumnsCalls == 1, "Grid session did not delegate column structural edits exactly once.");

    var bridge = new DataWorkbookDatabaseBridge(fake, fakeDatabase);
    var published = await bridge.PublishRangeAsync(opened.Workbook.Id, new DataRangeRequest("Sheet 1", 0, 0, 1, 2), "GridSnapshot");
    Assert(published.Columns.SequenceEqual(["A", "B"]) && published.RowCount == 1, "Database bridge published the wrong shape.");
    var publishedTable = fakeDatabase.LastTable ?? throw new InvalidOperationException("Database bridge did not call the database engine.");
    Assert(publishedTable.Rows[0].SequenceEqual(["5", "10"]), "Database bridge did not publish displayed spreadsheet values.");

    var secondSheet = await session.SelectSheetAsync(1);
    Assert(secondSheet.ActiveSheet.Name == "Summary", "Grid session sheet selection failed.");
    await session.SaveAsAsync("saved.ods");
    Assert(fake.LastSavePath == "saved.ods", "Grid session did not delegate save-as.");
    await session.CloseAsync();
    Assert(fake.CloseCalls == 1, "Grid session did not close the workbook exactly once.");
}
await fakeDatabase.CloseAsync();

await using var querySpreadsheet = new FakeSpreadsheetEngine();
await using var queryDatabase = new FakeDatabaseEngine();
var queryWorkbook = await querySpreadsheet.OpenAsync("query-fixture.ods", readOnly: false);
await querySpreadsheet.SetCellAsync(queryWorkbook.Id, new DataCellAddress("Sheet 1", 0, 0), "Name");
await querySpreadsheet.SetCellAsync(queryWorkbook.Id, new DataCellAddress("Sheet 1", 0, 1), "Score");
await querySpreadsheet.SetCellAsync(queryWorkbook.Id, new DataCellAddress("Sheet 1", 1, 0), "Ada");
await querySpreadsheet.SetCellAsync(queryWorkbook.Id, new DataCellAddress("Sheet 1", 1, 1), "42");

await using (var querySession = new DataQuerySession(querySpreadsheet, queryDatabase))
{
    var openedQuery = await querySession.OpenAsync("query.duckdb");
    Assert(openedQuery.DatabasePath == "query.duckdb" && openedQuery.PublishedTables.Count == 0 && openedQuery.RecentQueries.Count == 0,
        "New query session state is invalid.");

    var published = await querySession.PublishRangeAsync(
        queryWorkbook.Id,
        new DataRangeRequest("Sheet 1", 0, 0, 2, 2),
        "Scores",
        firstRowIsHeaders: true);
    Assert(published.Columns.SequenceEqual(["Name", "Score"]) && published.RowCount == 1, "Query session publication failed.");

    for (var index = 0; index < 22; index++)
    {
        var execution = await querySession.ExecuteAsync($"SELECT {index} AS value", maxRows: 25);
        Assert(execution.MaxRows == 25 && execution.Result.Rows.Count == 1, "Query session execution contract failed.");
    }

    var querySnapshot = querySession.Snapshot();
    Assert(querySnapshot.PublishedTables.Count == 1 && querySnapshot.RecentQueries.Count == 20, "Query session did not retain bounded state.");
    Assert(querySnapshot.RecentQueries[0].Sql == "SELECT 21 AS value" && querySnapshot.RecentQueries[^1].Sql == "SELECT 2 AS value",
        "Query history is not newest-first or did not discard its oldest entries.");
    Assert(queryDatabase.QueryCalls == 22, "Query session did not delegate every query exactly once.");

    var materialized = await querySession.MaterializeAsync(queryWorkbook.Id, querySnapshot.RecentQueries[0], "Query Result");
    Assert(materialized.DataRowCount == 1 && materialized.Range is { RowCount: 2, ColumnCount: 1 }, "Query materialisation exposed the wrong dimensions.");
    var materializedValues = await querySpreadsheet.ReadRangeAsync(queryWorkbook.Id, materialized.Range);
    Assert(materializedValues.Values[0][0] == "sql" && materializedValues.Values[1][0] == "SELECT 21 AS value",
        "Query materialisation changed the result.");
    Assert(querySpreadsheet.MaterializedSheetCalls == 1, "Query materialisation did not use the typed spreadsheet operation exactly once.");

    var truncated = await querySession.ExecuteAsync("TRUNCATED PREVIEW", maxRows: 25);
    var refusedTruncated = false;
    try { _ = await querySession.MaterializeAsync(queryWorkbook.Id, truncated, "Incomplete"); }
    catch (InvalidOperationException) { refusedTruncated = true; }
    Assert(refusedTruncated, "Query session allowed a truncated preview to be materialized silently.");

    await querySession.CloseAsync();
    Assert(!querySession.IsOpen && queryDatabase.CloseCalls == 1, "Query session close semantics failed.");
}
await querySpreadsheet.CloseAsync(queryWorkbook.Id);

Console.WriteLine("Haven Data contract, grid-session, named-range, list-validation, structural-edit, database-bridge, query-session and materialisation smoke checks passed.");

internal sealed class FakeSpreadsheetEngine : IDataSpreadsheetEngine
{
    private readonly Dictionary<(string Sheet, int Row, int Column), string> _values = new();
    private readonly List<string> _sheets = ["Sheet 1", "Summary"];
    private readonly Dictionary<string, DataNamedRangeSummary> _namedRanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<DataRangeRequest, DataListValidationState> _validations = new();
    private DataWorkbookHandle? _open;

    public int RecalculateCalls { get; private set; }
    public int CloseCalls { get; private set; }
    public int MaterializedSheetCalls { get; private set; }
    public int InsertRowsCalls { get; private set; }
    public int DeleteRowsCalls { get; private set; }
    public int InsertColumnsCalls { get; private set; }
    public int DeleteColumnsCalls { get; private set; }
    public string? LastSavePath { get; private set; }

    public Task<DataWorkbookHandle> OpenAsync(string path, bool readOnly, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _open = new DataWorkbookHandle("fake-workbook", path, readOnly);
        return Task.FromResult(_open);
    }

    public Task<IReadOnlyList<DataSheetSummary>> ListSheetsAsync(string workbookId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen(workbookId);
        IReadOnlyList<DataSheetSummary> sheets = _sheets.Select((name, index) => new DataSheetSummary(name, index)).ToArray();
        return Task.FromResult(sheets);
    }

    public Task<DataRangeSnapshot> ReadRangeAsync(string workbookId, DataRangeRequest range, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen(workbookId);
        var rows = new List<IReadOnlyList<string>>(range.RowCount);
        for (var row = 0; row < range.RowCount; row++)
        {
            var values = new List<string>(range.ColumnCount);
            for (var column = 0; column < range.ColumnCount; column++)
                values.Add(_values.GetValueOrDefault((range.Sheet, range.StartRow + row, range.StartColumn + column), string.Empty));
            rows.Add(values);
        }
        return Task.FromResult(new DataRangeSnapshot(range.Sheet, range.StartRow, range.StartColumn, rows));
    }

    public Task<DataCellSnapshot> SetCellAsync(string workbookId, DataCellAddress address, string? value, string? formula = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen(workbookId);
        var stored = string.IsNullOrWhiteSpace(formula) ? value ?? string.Empty : formula == "=A1*2" ? "10" : value ?? string.Empty;
        _values[(address.Sheet, address.Row, address.Column)] = stored;
        return Task.FromResult(new DataCellSnapshot(address, stored, formula ?? string.Empty));
    }

    public Task<DataRangeSnapshot> CreateSheetWithValuesAsync(
        string workbookId,
        string sheetName,
        IReadOnlyList<IReadOnlyList<string>> values,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen(workbookId);
        if (_sheets.Any(existing => string.Equals(existing, sheetName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Fake workbook already contains that sheet.");
        if (values.Count == 0 || values[0].Count == 0 || values.Any(row => row.Count != values[0].Count))
            throw new InvalidOperationException("Fake materialized values are not rectangular.");
        _sheets.Add(sheetName);
        MaterializedSheetCalls++;
        for (var row = 0; row < values.Count; row++)
            for (var column = 0; column < values[row].Count; column++)
                _values[(sheetName, row, column)] = values[row][column];
        return Task.FromResult(new DataRangeSnapshot(sheetName, 0, 0, values.Select(row => (IReadOnlyList<string>)row.ToArray()).ToArray()));
    }

    public Task<IReadOnlyList<DataNamedRangeSummary>> ListNamedRangesAsync(string workbookId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen(workbookId);
        IReadOnlyList<DataNamedRangeSummary> result = _namedRanges.Values.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        return Task.FromResult(result);
    }

    public Task<DataNamedRangeSummary> CreateNamedRangeAsync(string workbookId, string name, DataRangeRequest range, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen(workbookId);
        if (_namedRanges.ContainsKey(name)) throw new InvalidOperationException("Fake named range already exists.");
        var summary = new DataNamedRangeSummary(name, range);
        _namedRanges.Add(name, summary);
        return Task.FromResult(summary);
    }

    public Task DeleteNamedRangeAsync(string workbookId, string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen(workbookId);
        if (!_namedRanges.Remove(name)) throw new InvalidOperationException("Fake named range does not exist.");
        return Task.CompletedTask;
    }

    public Task<DataListValidationState> GetListValidationAsync(string workbookId, DataRangeRequest range, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen(workbookId);
        return Task.FromResult(_validations.GetValueOrDefault(range, new DataListValidationState(range, false, [], true)));
    }

    public Task<DataListValidationState> ApplyListValidationAsync(
        string workbookId,
        DataRangeRequest range,
        IReadOnlyList<string> values,
        bool allowBlank = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen(workbookId);
        var state = new DataListValidationState(range, true, values.ToArray(), allowBlank);
        _validations[range] = state;
        return Task.FromResult(state);
    }

    public Task<DataListValidationState> ClearValidationAsync(string workbookId, DataRangeRequest range, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen(workbookId);
        _validations.Remove(range);
        return Task.FromResult(new DataListValidationState(range, false, [], true));
    }

    public Task InsertRowsAsync(string workbookId, string sheet, int index, int count, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); EnsureOpen(workbookId); InsertRowsCalls++; return Task.CompletedTask;
    }

    public Task DeleteRowsAsync(string workbookId, string sheet, int index, int count, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); EnsureOpen(workbookId); DeleteRowsCalls++; return Task.CompletedTask;
    }

    public Task InsertColumnsAsync(string workbookId, string sheet, int index, int count, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); EnsureOpen(workbookId); InsertColumnsCalls++; return Task.CompletedTask;
    }

    public Task DeleteColumnsAsync(string workbookId, string sheet, int index, int count, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); EnsureOpen(workbookId); DeleteColumnsCalls++; return Task.CompletedTask;
    }

    public Task RecalculateAsync(string workbookId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); EnsureOpen(workbookId); RecalculateCalls++; return Task.CompletedTask;
    }

    public Task SaveAsync(string workbookId, string destinationPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); EnsureOpen(workbookId); LastSavePath = destinationPath; return Task.CompletedTask;
    }

    public Task CloseAsync(string workbookId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); EnsureOpen(workbookId); CloseCalls++; _open = null; return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() { _open = null; return ValueTask.CompletedTask; }

    private void EnsureOpen(string workbookId)
    {
        if (_open is null || !string.Equals(_open.Id, workbookId, StringComparison.Ordinal))
            throw new InvalidOperationException("Fake workbook is not open.");
    }
}

internal sealed class FakeDatabaseEngine : IDataDatabaseEngine
{
    private bool _open;
    public DataTableSnapshot? LastTable { get; private set; }
    public int QueryCalls { get; private set; }
    public int CloseCalls { get; private set; }

    public Task OpenAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); ArgumentException.ThrowIfNullOrWhiteSpace(databasePath); _open = true; return Task.CompletedTask;
    }

    public Task ReplaceTableAsync(DataTableSnapshot table, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); EnsureOpen(); LastTable = table; return Task.CompletedTask;
    }

    public Task<DataQueryResult> ExecuteReadOnlyAsync(string sql, int maxRows = 200, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); EnsureOpen(); QueryCalls++;
        if (string.Equals(sql, "TRUNCATED PREVIEW", StringComparison.Ordinal))
            return Task.FromResult(new DataQueryResult(["value"], [["partial"]], true));
        return Task.FromResult(new DataQueryResult(["sql"], [[sql.Trim()]], false));
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); EnsureOpen(); CloseCalls++; _open = false; return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() { _open = false; return ValueTask.CompletedTask; }

    private void EnsureOpen()
    {
        if (!_open) throw new InvalidOperationException("Fake database is not open.");
    }
}
