using System.Diagnostics;
using HavenOS.Apps.Data;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string FindRepositoryRoot()
{
    var current = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "apps", "Data", "workers", "calc_worker.py")) ||
            File.Exists(Path.Combine(current.FullName, "workers", "calc_worker.py")) ||
            File.Exists(Path.Combine(current.FullName, "9to1 Workspace", "Data", "workers", "calc_worker.py")))
            return current.FullName;
        current = current.Parent;
    }
    throw new DirectoryNotFoundException("Could not locate the CakeOS repository root.");
}

static async Task VerifyCellAddressContractAsync()
{
    var engine = new MisaddressedCellEngine();
    await using var grid = new DataGridSession(engine);
    await grid.OpenAsync("fake.ods");
    try
    {
        _ = await grid.EditCellAsync(0, 0, "=1+1", "=1+1");
        throw new InvalidOperationException("DataGridSession accepted an engine response for the wrong cell.");
    }
    catch (InvalidDataException)
    {
        Assert(engine.RecalculateCount == 0, "DataGridSession recalculated after the engine reported editing the wrong cell.");
    }
}

if (args.Length == 1 && args[0] == "--cell-contract-only")
{
    await VerifyCellAddressContractAsync();
    Console.WriteLine("DataGridSession cell-address contract check passed.");
    return;
}

static async Task ConvertCsvToOdsAsync(string csvPath, string outputDirectory)
{
    var soffice = Environment.GetEnvironmentVariable("HAVEN_DATA_SOFFICE") ?? "soffice";
    var startInfo = new ProcessStartInfo
    {
        FileName = soffice,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    startInfo.ArgumentList.Add("--headless");
    startInfo.ArgumentList.Add("--convert-to");
    startInfo.ArgumentList.Add("ods");
    startInfo.ArgumentList.Add("--outdir");
    startInfo.ArgumentList.Add(outputDirectory);
    startInfo.ArgumentList.Add(csvPath);

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Could not start LibreOffice to create the runtime fixture.");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    try
    {
        await process.WaitForExitAsync(timeout.Token);
    }
    catch (OperationCanceledException)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
        throw new TimeoutException("LibreOffice timed out while creating the runtime fixture.");
    }

    var stdout = await process.StandardOutput.ReadToEndAsync();
    var stderr = await process.StandardError.ReadToEndAsync();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"LibreOffice fixture conversion failed ({process.ExitCode}). stdout: {stdout} stderr: {stderr}");
}

var root = FindRepositoryRoot();
var python = Environment.GetEnvironmentVariable("HAVEN_DATA_PYTHON") ?? "python3";
var workerDirectory = new[]
{
    Path.Combine(root, "apps", "Data", "workers"),
    Path.Combine(root, "workers"),
    Path.Combine(root, "9to1 Workspace", "Data", "workers"),
}.First(Directory.Exists);
var calcWorker = Path.Combine(workerDirectory, "calc_worker.py");
var duckDbWorker = Path.Combine(workerDirectory, "duckdb_worker.py");
var temporaryRoot = Path.Combine(Path.GetTempPath(), $"haven-data-dotnet-runtime-{Guid.NewGuid():N}");
Directory.CreateDirectory(temporaryRoot);

try
{
    await VerifyCellAddressContractAsync();

    var csvPath = Path.Combine(temporaryRoot, "fixture.csv");
    var sourceOds = Path.Combine(temporaryRoot, "fixture.ods");
    var savedOds = Path.Combine(temporaryRoot, "saved.ods");
    var databasePath = Path.Combine(temporaryRoot, "fixture.duckdb");
    await File.WriteAllTextAsync(csvPath, "Name,Score\nAda,10\nBob,20\n");
    await ConvertCsvToOdsAsync(csvPath, temporaryRoot);
    Assert(File.Exists(sourceOds) && new FileInfo(sourceOds).Length > 0, "LibreOffice did not create the ODS fixture.");

    await using var spreadsheet = new CalcSpreadsheetEngine(calcWorker, python);
    await using var database = new DuckDbDatabaseEngine(duckDbWorker, python);
    await using var grid = new DataGridSession(spreadsheet);
    await using var queries = new DataQuerySession(spreadsheet, database);

    var opened = await grid.OpenAsync(sourceOds);
    Assert(opened.ActiveSheet.Name.Length > 0, "Calc did not expose a worksheet to the .NET grid session.");
    Assert(opened.Grid.Values[1][0] == "Ada" && opened.Grid.Values[1][1] == "10", "The .NET Calc adapter read the wrong source values.");

    var editedScore = await grid.EditCellAsync(1, 1, "42");
    Assert(editedScore.Grid.Values[1][1] == "42", "The .NET Calc adapter did not expose the edited numeric cell.");
    _ = await grid.EditCellAsync(3, 0, "Total");
    var formula = await grid.EditCellAsync(3, 1, string.Empty, "=SUM(B2:B3)");
    Assert(formula.Grid.Values[3][1] == "62", $"Calc formula recalculation through .NET returned '{formula.Grid.Values[3][1]}' instead of 62.");

    var named = await grid.CreateNamedRangeAsync("ScoresRange", 1, 0, 2, 2);
    Assert(named.Range is { StartRow: 1, StartColumn: 0, RowCount: 2, ColumnCount: 2 }, "DataGridSession created the wrong range-backed name.");
    Assert((await grid.ListNamedRangesAsync()).Any(item => item.Name == "ScoresRange"), "DataGridSession did not list its created named range.");

    var rowInserted = await grid.InsertRowsAsync(0);
    Assert(rowInserted.Grid.Values[2][0] == "Ada" && rowInserted.Grid.Values[4][1] == "62",
        "DataGridSession row insertion did not shift workbook cells/formula through Calc.");
    var shiftedNamedRow = (await grid.ListNamedRangesAsync()).Single(item => item.Name == "ScoresRange");
    Assert(shiftedNamedRow.Range.StartRow == 2, "Calc did not shift the named range when a row was inserted before it.");
    var shiftedRowEdit = await grid.EditCellAsync(2, 1, "43");
    Assert(shiftedRowEdit.Grid.Values[4][1] == "63", "Shifted formula did not continue tracking its row dependency after insertion.");
    var rowDeleted = await grid.DeleteRowsAsync(0);
    Assert(rowDeleted.Grid.Values[1][0] == "Ada" && rowDeleted.Grid.Values[3][1] == "63",
        "DataGridSession row deletion did not restore the shifted data/formula positions.");
    Assert((await grid.ListNamedRangesAsync()).Single(item => item.Name == "ScoresRange").Range.StartRow == 1,
        "Calc did not restore the named range position after row deletion.");
    var restoredRowValue = await grid.EditCellAsync(1, 1, "42");
    Assert(restoredRowValue.Grid.Values[3][1] == "62", "Formula relationship was lost after row insert/delete round trip.");

    var columnInserted = await grid.InsertColumnsAsync(0);
    Assert(columnInserted.Grid.Values[1][2] == "42" && columnInserted.Grid.Values[3][2] == "62",
        "DataGridSession column insertion did not shift workbook cells/formula through Calc.");
    var shiftedNamedColumn = (await grid.ListNamedRangesAsync()).Single(item => item.Name == "ScoresRange");
    Assert(shiftedNamedColumn.Range.StartColumn == 1, "Calc did not shift the named range when a column was inserted before it.");
    var shiftedColumnEdit = await grid.EditCellAsync(1, 2, "44");
    Assert(shiftedColumnEdit.Grid.Values[3][2] == "64", "Shifted formula did not continue tracking its column dependency after insertion.");
    var columnDeleted = await grid.DeleteColumnsAsync(0);
    Assert(columnDeleted.Grid.Values[1][1] == "44" && columnDeleted.Grid.Values[3][1] == "64",
        "DataGridSession column deletion did not restore the shifted data/formula positions.");
    Assert((await grid.ListNamedRangesAsync()).Single(item => item.Name == "ScoresRange").Range.StartColumn == 0,
        "Calc did not restore the named range position after column deletion.");
    var restoredColumnValue = await grid.EditCellAsync(1, 1, "42");
    Assert(restoredColumnValue.Grid.Values[3][1] == "62", "Formula relationship was lost after column insert/delete round trip.");

    var querySnapshot = await queries.OpenAsync(databasePath);
    Assert(querySnapshot.DatabasePath == databasePath, "The .NET DuckDB adapter did not open the requested database.");
    var published = await queries.PublishRangeAsync(
        opened.Workbook.Id,
        new DataRangeRequest(opened.ActiveSheet.Name, 0, 0, 3, 2),
        "Scores",
        firstRowIsHeaders: true);
    Assert(published.Columns.SequenceEqual(["Name", "Score"]), "The .NET workbook-to-DuckDB bridge did not preserve headers.");
    Assert(published.RowCount == 2, "The .NET workbook-to-DuckDB bridge published the wrong number of data rows.");

    var aggregate = await queries.ExecuteAsync("SELECT SUM(CAST(\"Score\" AS INTEGER)) AS total FROM \"Scores\"", maxRows: 20);
    Assert(aggregate.Result.Columns.SequenceEqual(["total"]), "DuckDB aggregate column metadata was not returned through .NET.");
    Assert(aggregate.Result.Rows.Count == 1 && aggregate.Result.Rows[0][0] == "62", "DuckDB did not aggregate the Calc-published values through the .NET boundary.");

    var materializedAggregate = await queries.MaterializeAsync(opened.Workbook.Id, aggregate, "Query Result");
    Assert(materializedAggregate.DataRowCount == 1, "The .NET query materialiser reported the wrong data row count.");
    var aggregateSheet = await spreadsheet.ReadRangeAsync(opened.Workbook.Id, materializedAggregate.Range);
    Assert(aggregateSheet.Values.Count == 2 && aggregateSheet.Values[0][0] == "total" && aggregateSheet.Values[1][0] == "62",
        "DuckDB aggregate was not materialized into Calc through the .NET boundary.");

    var formulaLookingQuery = await queries.ExecuteAsync("SELECT '=1+1' AS literal", maxRows: 20);
    var literalMaterialization = await queries.MaterializeAsync(opened.Workbook.Id, formulaLookingQuery, "Literal Result");
    var literalSheet = await spreadsheet.ReadRangeAsync(opened.Workbook.Id, literalMaterialization.Range);
    Assert(literalSheet.Values[1][0] == "=1+1", "Formula-looking DuckDB output was executed instead of materialized as literal Calc text.");

    await grid.SaveAsAsync(savedOds);
    Assert(File.Exists(savedOds) && new FileInfo(savedOds).Length > 0, "Calc save-as through .NET did not produce an ODS file.");
    await queries.CloseAsync();
    await grid.CloseAsync();

    var reopened = await grid.OpenAsync(savedOds, readOnly: true);
    Assert(reopened.Grid.Values[3][0] == "Total" && reopened.Grid.Values[3][1] == "62", "Saved ODS did not preserve the .NET-edited formula after reopen.");
    var reopenedNamed = (await grid.ListNamedRangesAsync()).Single(item => item.Name == "ScoresRange");
    Assert(reopenedNamed.Range is { StartRow: 1, StartColumn: 0, RowCount: 2, ColumnCount: 2 }, "Saved ODS did not preserve the named range.");
    var reopenedSheets = await spreadsheet.ListSheetsAsync(reopened.Workbook.Id);
    Assert(reopenedSheets.Any(sheet => sheet.Name == "Query Result") && reopenedSheets.Any(sheet => sheet.Name == "Literal Result"),
        "Saved ODS did not preserve materialized query-result sheets.");
    var reopenedAggregate = await spreadsheet.ReadRangeAsync(
        reopened.Workbook.Id,
        new DataRangeRequest("Query Result", 0, 0, 2, 1));
    Assert(reopenedAggregate.Values[1][0] == "62", "Saved ODS changed the materialized aggregate result.");
    var reopenedLiteral = await spreadsheet.ReadRangeAsync(
        reopened.Workbook.Id,
        new DataRangeRequest("Literal Result", 0, 0, 2, 1));
    Assert(reopenedLiteral.Values[1][0] == "=1+1", "Saved ODS converted literal query output into a formula.");
    await grid.CloseAsync();

    Console.WriteLine("Haven Data .NET-to-worker named-range, structural and bidirectional runtime integration checks passed.");
}
finally
{
    try { Directory.Delete(temporaryRoot, recursive: true); } catch { }
}

sealed class MisaddressedCellEngine : IDataSpreadsheetEngine
{
    public int RecalculateCount { get; private set; }

    public Task<DataWorkbookHandle> OpenAsync(string path, bool readOnly, CancellationToken cancellationToken = default) =>
        Task.FromResult(new DataWorkbookHandle("fake", path, readOnly));

    public Task<IReadOnlyList<DataSheetSummary>> ListSheetsAsync(string workbookId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DataSheetSummary>>([new DataSheetSummary("Sheet1", 0)]);

    public Task<DataRangeSnapshot> ReadRangeAsync(string workbookId, DataRangeRequest range, CancellationToken cancellationToken = default) =>
        Task.FromResult(new DataRangeSnapshot(range.Sheet, range.StartRow, range.StartColumn,
            Enumerable.Range(0, range.RowCount).Select(_ => (IReadOnlyList<string>)Enumerable.Repeat(string.Empty, range.ColumnCount).ToArray()).ToArray()));

    public Task<DataCellSnapshot> SetCellAsync(string workbookId, DataCellAddress address, string? value, string? formula = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new DataCellSnapshot(address with { Column = address.Column + 1 }, value ?? string.Empty, formula ?? string.Empty));

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
    public Task RecalculateAsync(string workbookId, CancellationToken cancellationToken = default)
    {
        RecalculateCount++;
        return Task.CompletedTask;
    }
    public Task SaveAsync(string workbookId, string destinationPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task CloseAsync(string workbookId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
