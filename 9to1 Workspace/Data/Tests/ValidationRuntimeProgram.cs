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
        ?? throw new InvalidOperationException("Could not start LibreOffice to create the validation fixture.");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    try
    {
        await process.WaitForExitAsync(timeout.Token);
    }
    catch (OperationCanceledException)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
        throw new TimeoutException("LibreOffice timed out while creating the validation fixture.");
    }

    var stdout = await process.StandardOutput.ReadToEndAsync();
    var stderr = await process.StandardError.ReadToEndAsync();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"LibreOffice validation fixture conversion failed ({process.ExitCode}). stdout: {stdout} stderr: {stderr}");
}

var root = FindRepositoryRoot();
var python = Environment.GetEnvironmentVariable("HAVEN_DATA_PYTHON") ?? "python3";
var calcWorker = Path.Combine(root, "apps", "Data", "workers", "calc_worker.py");
var temporaryRoot = Path.Combine(Path.GetTempPath(), $"haven-data-validation-runtime-{Guid.NewGuid():N}");
Directory.CreateDirectory(temporaryRoot);

try
{
    var csvPath = Path.Combine(temporaryRoot, "fixture.csv");
    var sourceOds = Path.Combine(temporaryRoot, "fixture.ods");
    var savedOds = Path.Combine(temporaryRoot, "validated.ods");
    await File.WriteAllTextAsync(csvPath, "Name,Status\nAda,Open\nBob,Closed\n");
    await ConvertCsvToOdsAsync(csvPath, temporaryRoot);
    Assert(File.Exists(sourceOds) && new FileInfo(sourceOds).Length > 0, "LibreOffice did not create the validation ODS fixture.");

    await using var spreadsheet = new CalcSpreadsheetEngine(calcWorker, python);
    await using var grid = new DataGridSession(spreadsheet);
    _ = await grid.OpenAsync(sourceOds);

    var applied = await grid.ApplyListValidationAsync(1, 1, 2, 1, ["Open", "Closed", "Pending"], allowBlank: false);
    Assert(applied.Enabled && applied.Values.SequenceEqual(["Open", "Closed", "Pending"]) && !applied.AllowBlank,
        "DataGridSession did not apply the requested validation through Calc.");
    var readBack = await grid.GetListValidationAsync(1, 1, 2, 1);
    Assert(
        readBack.Range == applied.Range &&
        readBack.Enabled == applied.Enabled &&
        readBack.Values.SequenceEqual(applied.Values) &&
        readBack.AllowBlank == applied.AllowBlank,
        "DataGridSession did not read back the Calc validation rule semantically.");

    _ = await grid.InsertRowsAsync(0);
    var shiftedRow = await grid.GetListValidationAsync(2, 1, 2, 1);
    Assert(shiftedRow.Enabled && shiftedRow.Values.SequenceEqual(applied.Values),
        "Validation did not shift with row insertion through DataGridSession.");
    _ = await grid.DeleteRowsAsync(0);
    Assert((await grid.GetListValidationAsync(1, 1, 2, 1)).Enabled,
        "Validation did not shift back after row deletion through DataGridSession.");

    _ = await grid.InsertColumnsAsync(0);
    var shiftedColumn = await grid.GetListValidationAsync(1, 2, 2, 1);
    Assert(shiftedColumn.Enabled && shiftedColumn.Values.SequenceEqual(applied.Values),
        "Validation did not shift with column insertion through DataGridSession.");
    _ = await grid.DeleteColumnsAsync(0);
    Assert((await grid.GetListValidationAsync(1, 1, 2, 1)).Enabled,
        "Validation did not shift back after column deletion through DataGridSession.");

    await grid.SaveAsAsync(savedOds);
    await grid.CloseAsync();

    _ = await grid.OpenAsync(savedOds, readOnly: true);
    var persisted = await grid.GetListValidationAsync(1, 1, 2, 1);
    Assert(persisted.Enabled && persisted.Values.SequenceEqual(applied.Values) && !persisted.AllowBlank,
        "ODS save/reopen did not preserve list validation through the .NET boundary.");
    var readOnlyBlocked = false;
    try { _ = await grid.ClearValidationAsync(1, 1, 2, 1); }
    catch (InvalidOperationException) { readOnlyBlocked = true; }
    Assert(readOnlyBlocked, "DataGridSession did not block validation mutation on a read-only workbook.");
    await grid.CloseAsync();

    _ = await grid.OpenAsync(savedOds);
    var cleared = await grid.ClearValidationAsync(1, 1, 2, 1);
    Assert(!cleared.Enabled && cleared.Values.Count == 0, "DataGridSession did not clear persisted list validation.");
    await grid.CloseAsync();

    Console.WriteLine("Haven Data .NET-to-Calc list validation runtime integration checks passed.");
}
finally
{
    try { Directory.Delete(temporaryRoot, recursive: true); } catch { }
}
