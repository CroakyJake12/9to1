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
        ?? throw new InvalidOperationException("Could not start LibreOffice to create the sort fixture.");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    try
    {
        await process.WaitForExitAsync(timeout.Token);
    }
    catch (OperationCanceledException)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
        throw new TimeoutException("LibreOffice timed out while creating the sort fixture.");
    }

    var stdout = await process.StandardOutput.ReadToEndAsync();
    var stderr = await process.StandardError.ReadToEndAsync();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"LibreOffice fixture conversion failed ({process.ExitCode}). stdout: {stdout} stderr: {stderr}");
}

var root = FindRepositoryRoot();
var python = Environment.GetEnvironmentVariable("HAVEN_DATA_PYTHON") ?? "python3";
var calcWorker = Path.Combine(root, "apps", "Data", "workers", "calc_worker.py");
var temporaryRoot = Path.Combine(Path.GetTempPath(), $"haven-data-sort-runtime-{Guid.NewGuid():N}");
Directory.CreateDirectory(temporaryRoot);

try
{
    var csvPath = Path.Combine(temporaryRoot, "fixture.csv");
    var sourceOds = Path.Combine(temporaryRoot, "fixture.ods");
    var savedOds = Path.Combine(temporaryRoot, "sorted.ods");
    await File.WriteAllTextAsync(csvPath, "seed\n");
    await ConvertCsvToOdsAsync(csvPath, temporaryRoot);
    Assert(File.Exists(sourceOds) && new FileInfo(sourceOds).Length > 0, "LibreOffice did not create the sort ODS fixture.");

    await using var spreadsheet = new CalcSpreadsheetEngine(calcWorker, python);
    await using var grid = new DataGridSession(spreadsheet);
    _ = await grid.OpenAsync(sourceOds);

    _ = await grid.EditCellAsync(0, 0, "Name");
    _ = await grid.EditCellAsync(0, 1, "Score");
    _ = await grid.EditCellAsync(1, 0, "Ada");
    _ = await grid.EditCellAsync(1, 1, "2");
    _ = await grid.EditCellAsync(2, 0, "Bob");
    _ = await grid.EditCellAsync(2, 1, "10");
    _ = await grid.EditCellAsync(3, 0, "Cara");
    _ = await grid.EditCellAsync(3, 1, "5");

    var ascending = await grid.SortRangeAsync(0, 0, 4, 2, 1, ascending: true, containsHeader: true);
    Assert(ascending.Grid.Values[0][0] == "Name" && ascending.Grid.Values[0][1] == "Score", "DataGridSession moved the header row.");
    Assert(ascending.Grid.Values[1][0] == "Ada" && ascending.Grid.Values[2][0] == "Cara" && ascending.Grid.Values[3][0] == "Bob",
        "DataGridSession did not expose ascending Calc sort order.");
    Assert(ascending.Grid.Values[1][1] == "2" && ascending.Grid.Values[2][1] == "5" && ascending.Grid.Values[3][1] == "10",
        "DataGridSession did not expose ascending numeric key order.");

    var descending = await grid.SortRangeAsync(0, 0, 4, 2, 1, ascending: false, containsHeader: true);
    Assert(descending.Grid.Values[1][0] == "Bob" && descending.Grid.Values[2][0] == "Cara" && descending.Grid.Values[3][0] == "Ada",
        "DataGridSession did not expose descending Calc sort order.");
    Assert(descending.Grid.Values[1][1] == "10" && descending.Grid.Values[2][1] == "5" && descending.Grid.Values[3][1] == "2",
        "DataGridSession did not expose descending numeric key order.");

    var invalidKeyBlocked = false;
    try { _ = await grid.SortRangeAsync(0, 0, 4, 2, 2); }
    catch (ArgumentOutOfRangeException) { invalidKeyBlocked = true; }
    Assert(invalidKeyBlocked, "DataGridSession accepted a sort key outside the visible sort range.");

    var headerOnlyBlocked = false;
    try { _ = await grid.SortRangeAsync(0, 0, 1, 2, 1, containsHeader: true); }
    catch (ArgumentException) { headerOnlyBlocked = true; }
    Assert(headerOnlyBlocked, "DataGridSession accepted a header-only sort range.");

    await grid.SaveAsAsync(savedOds);
    await grid.CloseAsync();

    _ = await grid.OpenAsync(savedOds, readOnly: true);
    var persisted = await grid.RefreshAsync();
    Assert(persisted.Grid.Values[1][0] == "Bob" && persisted.Grid.Values[3][0] == "Ada", "ODS save/reopen changed the sorted row order.");
    Assert(persisted.Grid.Values[1][1] == "10" && persisted.Grid.Values[3][1] == "2", "ODS save/reopen changed the sorted key order.");

    var readOnlyBlocked = false;
    try { _ = await grid.SortRangeAsync(0, 0, 4, 2, 1); }
    catch (InvalidOperationException) { readOnlyBlocked = true; }
    Assert(readOnlyBlocked, "DataGridSession did not block sort mutation on a read-only workbook.");
    await grid.CloseAsync();

    Console.WriteLine("Haven Data DataGridSession-to-Calc single-key sort runtime checks passed.");
}
finally
{
    try { Directory.Delete(temporaryRoot, recursive: true); } catch { }
}
