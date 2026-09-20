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
        if (File.Exists(Path.Combine(current.FullName, "apps", "Data", "workers", "calc_worker.py")))
            return current.FullName;
        current = current.Parent;
    }
    throw new DirectoryNotFoundException("Could not locate the CakeOS repository root.");
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
        ?? throw new InvalidOperationException("Could not start LibreOffice to create the filter fixture.");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    try
    {
        await process.WaitForExitAsync(timeout.Token);
    }
    catch (OperationCanceledException)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
        throw new TimeoutException("LibreOffice timed out while creating the filter fixture.");
    }

    var stdout = await process.StandardOutput.ReadToEndAsync();
    var stderr = await process.StandardError.ReadToEndAsync();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"LibreOffice fixture conversion failed ({process.ExitCode}). stdout: {stdout} stderr: {stderr}");
}

static void AssertVisibility(DataGridSessionSnapshot snapshot, IReadOnlyList<bool> expected, string message)
{
    var actual = snapshot.Grid.RowVisibility
        ?? throw new InvalidOperationException("Calc did not provide row-visibility metadata to DataGridSession.");
    Assert(actual.SequenceEqual(expected), $"{message} Expected [{string.Join(",", expected)}], got [{string.Join(",", actual)}].");
}

var root = FindRepositoryRoot();
var python = Environment.GetEnvironmentVariable("HAVEN_DATA_PYTHON") ?? "python3";
var calcWorker = Path.Combine(root, "apps", "Data", "workers", "calc_worker.py");
var temporaryRoot = Path.Combine(Path.GetTempPath(), $"haven-data-filter-runtime-{Guid.NewGuid():N}");
Directory.CreateDirectory(temporaryRoot);

try
{
    var csvPath = Path.Combine(temporaryRoot, "fixture.csv");
    var sourceOds = Path.Combine(temporaryRoot, "fixture.ods");
    var savedOds = Path.Combine(temporaryRoot, "filtered.ods");
    await File.WriteAllTextAsync(csvPath, "seed\n");
    await ConvertCsvToOdsAsync(csvPath, temporaryRoot);
    Assert(File.Exists(sourceOds) && new FileInfo(sourceOds).Length > 0, "LibreOffice did not create the filter ODS fixture.");

    await using var spreadsheet = new CalcSpreadsheetEngine(calcWorker, python);
    await using var grid = new DataGridSession(spreadsheet);
    _ = await grid.OpenAsync(sourceOds);

    _ = await grid.EditCellAsync(0, 0, "Name");
    _ = await grid.EditCellAsync(0, 1, "Category");
    _ = await grid.EditCellAsync(1, 0, "Ada");
    _ = await grid.EditCellAsync(1, 1, "Red");
    _ = await grid.EditCellAsync(2, 0, "Bob");
    _ = await grid.EditCellAsync(2, 1, "Blue");
    _ = await grid.EditCellAsync(3, 0, "Cara");
    _ = await grid.EditCellAsync(3, 1, "Red");
    _ = await grid.EditCellAsync(4, 0, "Drew");
    _ = await grid.EditCellAsync(4, 1, "Green");

    var baseline = await grid.RefreshAsync();
    AssertVisibility(baseline, Enumerable.Repeat(true, DataGridSession.VisibleRows).ToArray(), "Unexpected baseline visibility.");

    var filtered = await grid.FilterEqualsAsync(0, 0, 5, 2, 1, "red", containsHeader: true);
    Assert(filtered.Grid.Values[1][0] == "Ada" && filtered.Grid.Values[2][0] == "Bob" && filtered.Grid.Values[3][0] == "Cara",
        "DataGridSession filtering changed workbook row values instead of visibility.");
    AssertVisibility(
        filtered,
        [true, true, false, true, false, true, true, true, true, true],
        "DataGridSession did not expose the expected case-insensitive Calc filter visibility.");

    var invalidKeyBlocked = false;
    try { _ = await grid.FilterEqualsAsync(0, 0, 5, 2, 2, "Red"); }
    catch (ArgumentOutOfRangeException) { invalidKeyBlocked = true; }
    Assert(invalidKeyBlocked, "DataGridSession accepted a filter key outside the visible filter range.");

    var emptyValueBlocked = false;
    try { _ = await grid.FilterEqualsAsync(0, 0, 5, 2, 1, ""); }
    catch (ArgumentException) { emptyValueBlocked = true; }
    Assert(emptyValueBlocked, "DataGridSession accepted an empty first-slice filter value.");

    var headerOnlyBlocked = false;
    try { _ = await grid.FilterEqualsAsync(0, 0, 1, 2, 1, "Red", containsHeader: true); }
    catch (ArgumentException) { headerOnlyBlocked = true; }
    Assert(headerOnlyBlocked, "DataGridSession accepted a header-only filter range.");

    await grid.SaveAsAsync(savedOds);
    await grid.CloseAsync();

    _ = await grid.OpenAsync(savedOds, readOnly: true);
    var persisted = await grid.RefreshAsync();
    AssertVisibility(
        persisted,
        [true, true, false, true, false, true, true, true, true, true],
        "ODS save/reopen changed the filtered row visibility.");

    var readOnlyFilterBlocked = false;
    try { _ = await grid.FilterEqualsAsync(0, 0, 5, 2, 1, "Blue"); }
    catch (InvalidOperationException) { readOnlyFilterBlocked = true; }
    Assert(readOnlyFilterBlocked, "DataGridSession did not block filter mutation on a read-only workbook.");

    var readOnlyClearBlocked = false;
    try { _ = await grid.ClearFilterAsync(0, 0, 5, 2); }
    catch (InvalidOperationException) { readOnlyClearBlocked = true; }
    Assert(readOnlyClearBlocked, "DataGridSession did not block filter clearing on a read-only workbook.");
    await grid.CloseAsync();

    _ = await grid.OpenAsync(savedOds);
    var cleared = await grid.ClearFilterAsync(0, 0, 5, 2, containsHeader: true);
    AssertVisibility(cleared, Enumerable.Repeat(true, DataGridSession.VisibleRows).ToArray(), "DataGridSession clear-filter did not restore visible rows.");
    await grid.CloseAsync();

    Console.WriteLine("Haven Data DataGridSession-to-Calc literal text-equality filter runtime checks passed.");
}
finally
{
    try { Directory.Delete(temporaryRoot, recursive: true); } catch { }
}
