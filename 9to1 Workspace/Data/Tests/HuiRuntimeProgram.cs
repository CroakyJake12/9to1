using System.Diagnostics;
using Haven.UI;
using Haven.UI.Components;
using HavenOS.Apps.Data;
using HavenOS.Apps.Data.Hui;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string FindDataDirectory()
{
    var current = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (current is not null)
    {
        foreach (var relativePath in new[] { Path.Combine("apps", "Data"), Path.Combine("9to1 Workspace", "Data") })
        {
            var dataDirectory = Path.Combine(current.FullName, relativePath);
            if (File.Exists(Path.Combine(dataDirectory, "workers", "calc_worker.py")))
                return dataDirectory;
        }
        current = current.Parent;
    }
    throw new DirectoryNotFoundException("Could not locate the Data app directory containing workers/calc_worker.py.");
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
        ?? throw new InvalidOperationException("Could not start LibreOffice to create the HUI fixture.");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    try
    {
        await process.WaitForExitAsync(timeout.Token);
    }
    catch (OperationCanceledException)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
        throw new TimeoutException("LibreOffice timed out while creating the HUI fixture.");
    }

    var stdout = await process.StandardOutput.ReadToEndAsync();
    var stderr = await process.StandardError.ReadToEndAsync();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"LibreOffice fixture conversion failed ({process.ExitCode}). stdout: {stdout} stderr: {stderr}");
}

static void LayoutAndRequireRender(DataHuiScene scene, HavenLayoutEngine layout, HavenSceneRenderer renderer, IHavenMeasureContext measure)
{
    scene.Root.ValidateUniqueNames();
    layout.Layout(scene.Root, new HavenSize(1280, 800), HavenPlatform.Linux, measure);
    var commands = renderer.Render(scene.Root);
    Assert(commands.Count > 0, "Data HUI scene produced no draw commands.");
    Assert(scene.Grid.Bounds.Width > 0 && scene.Grid.Bounds.Height > 0, "Data HUI grid did not receive layout bounds.");
}

static void PointerInvoke(HavenInputRouter input, HavenElement element)
{
    Assert(element.Bounds.Width > 0 && element.Bounds.Height > 0, $"HUI element '{element.Name}' has no pointer target bounds.");
    var center = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2d, element.Bounds.Y + element.Bounds.Height / 2d);
    input.PointerPressed(center);
    Assert(input.PointerReleased(center), $"HUI pointer invocation failed for '{element.Name}'.");
}

static DataHuiAction KeyboardInvokeAndDequeue(HavenInputRouter input, DataHuiScene scene, Haven.UI.Components.Button button)
{
    input.Focus(button);
    Assert(input.KeyDown(HavenKey.Enter), $"HUI did not accept Enter key-down for '{button.Name}'.");
    Assert(input.KeyUp(HavenKey.Enter), $"HUI did not accept Enter key-up for '{button.Name}'.");
    Assert(scene.TryDequeueAction(out var action), $"HUI action button '{button.Name}' did not enqueue a typed Data action.");
    return action;
}

var dataDirectory = FindDataDirectory();
var python = Environment.GetEnvironmentVariable("HAVEN_DATA_PYTHON") ?? "/usr/bin/python3";
var calcWorker = Path.Combine(dataDirectory, "workers", "calc_worker.py");
var temporaryRoot = Path.Combine(Path.GetTempPath(), $"haven-data-hui-runtime-{Guid.NewGuid():N}");
Directory.CreateDirectory(temporaryRoot);

try
{
    var csvPath = Path.Combine(temporaryRoot, "fixture.csv");
    var sourceOds = Path.Combine(temporaryRoot, "fixture.ods");
    var savedOds = Path.Combine(temporaryRoot, "hui-saved.ods");
    await File.WriteAllTextAsync(
        csvPath,
        "Name,Score,Double,,,,,,Tag\n" +
        "Ada,2,,,,,,,Tag-A\n" +
        "Bob,10,\n" +
        "Cara,5,\n" +
        "Drew,8,\n" +
        "Eli,1,\n" +
        "Faye,7,\n" +
        "Gus,4,\n" +
        "Hope,9,\n" +
        "Iris,6,\n" +
        "Jade,3,,,,,,,Tag-Jade\n" +
        "Kiki,11,\n" +
        "Liam,12,\n");
    await ConvertCsvToOdsAsync(csvPath, temporaryRoot);
    Assert(File.Exists(sourceOds) && new FileInfo(sourceOds).Length > 0, "LibreOffice did not create the Data HUI ODS fixture.");

    await using var spreadsheet = new CalcSpreadsheetEngine(calcWorker, python);
    await using var session = new DataGridSession(spreadsheet);
    var controller = new DataHuiController(session);
    var scene = controller.Scene;
    var layout = new HavenLayoutEngine();
    var renderer = new HavenSceneRenderer();
    var measure = new DataHuiMeasureContext();
    var input = new HavenInputRouter(scene.Root);

    var opened = await controller.OpenAsync(sourceOds);
    Assert(opened.Grid.Values[1][0] == "Ada" && opened.Grid.Values[1][1] == "2", "Data HUI did not receive the opened Calc viewport.");
    Assert(opened.Grid.Values.Count == 10 && opened.Grid.Values.All(row => row.Count == 8), "Data HUI did not preserve its fixed 10 x 8 viewport.");
    Assert(scene.PreviousRowsButton.State.HasFlag(HavenElementState.Disabled), "Previous rows was enabled on the first row page.");
    Assert(scene.PreviousColumnsButton.State.HasFlag(HavenElementState.Disabled), "Previous columns was enabled on the first column page.");
    LayoutAndRequireRender(scene, layout, renderer, measure);

    var nextRowsAction = KeyboardInvokeAndDequeue(input, scene, scene.NextRowsButton);
    Assert(nextRowsAction == DataHuiAction.NextRowPage, "Next rows button emitted the wrong typed Data action.");
    var nextRowPage = await controller.ExecuteAsync(nextRowsAction);
    Assert(nextRowPage.Grid.StartRow == 10, "Next rows button did not advance the real DataGridSession by ten rows.");
    Assert(nextRowPage.Grid.Values.Count == 10 && nextRowPage.Grid.Values[0].Count == 8, "Next row page changed the fixed 10 x 8 viewport.");
    Assert(nextRowPage.Grid.Values[0][0] == "Jade", "Next row page did not load data from the next Calc rows.");
    Assert(scene.CellButton(0, 0).Accessibility.AccessibleName == "A11: Jade", "Paged cell accessibility did not report its absolute spreadsheet address.");
    Assert((scene.StatusText.Content ?? string.Empty).Contains("rows 11–20", StringComparison.Ordinal), "Paged status did not announce the visible absolute row range.");
    Assert(scene.CellButton(scene.SelectedRow, scene.SelectedColumn).Accessibility.AccessibleName == "A12: Kiki",
        "Paged selected-cell accessibility did not report its absolute spreadsheet address.");
    Assert((scene.StatusText.Content ?? string.Empty).Contains("selected A12", StringComparison.Ordinal) &&
        scene.StatusText.Accessibility.AccessibleName == scene.StatusText.Content,
        "Paged status text and accessibility name did not report the same absolute selected cell.");
    Assert(!scene.PreviousRowsButton.State.HasFlag(HavenElementState.Disabled), "Previous rows stayed disabled after advancing.");
    LayoutAndRequireRender(scene, layout, renderer, measure);

    var nextColumnsAction = KeyboardInvokeAndDequeue(input, scene, scene.NextColumnsButton);
    Assert(nextColumnsAction == DataHuiAction.NextColumnPage, "Next columns button emitted the wrong typed Data action.");
    var nextColumnPage = await controller.ExecuteAsync(nextColumnsAction);
    Assert(nextColumnPage.Grid.StartColumn == 8, "Next columns button did not advance the real DataGridSession by eight columns.");
    Assert(nextColumnPage.Grid.Values.Count == 10 && nextColumnPage.Grid.Values.All(row => row.Count == 8), "Next column page changed the fixed 10 x 8 viewport.");
    Assert(nextColumnPage.Grid.Values[0][0] == "Tag-Jade", "Next column page did not load the Calc value from absolute column I.");
    Assert(scene.CellButton(0, 0).Accessibility.AccessibleName == "I11: Tag-Jade", "Paged column accessibility did not report its absolute spreadsheet address.");
    Assert((scene.StatusText.Content ?? string.Empty).Contains("columns I–P", StringComparison.Ordinal), "Paged status did not announce the visible absolute column range.");
    Assert(scene.CellButton(scene.SelectedRow, scene.SelectedColumn).Accessibility.AccessibleName == "I12: blank" &&
        (scene.StatusText.Content ?? string.Empty).Contains("selected I12", StringComparison.Ordinal) &&
        scene.StatusText.Accessibility.AccessibleName == scene.StatusText.Content,
        "Paged column selection, status, and accessibility addresses diverged.");
    Assert(!scene.PreviousColumnsButton.State.HasFlag(HavenElementState.Disabled), "Previous columns stayed disabled after advancing.");

    var previousColumnsAction = KeyboardInvokeAndDequeue(input, scene, scene.PreviousColumnsButton);
    Assert(previousColumnsAction == DataHuiAction.PreviousColumnPage, "Previous columns button emitted the wrong typed Data action.");
    var firstColumnPageAgain = await controller.ExecuteAsync(previousColumnsAction);
    Assert(firstColumnPageAgain.Grid.StartColumn == 0 && firstColumnPageAgain.Grid.Values[0][0] == "Jade", "Previous columns button did not return to the first column page.");

    var previousRowsAction = KeyboardInvokeAndDequeue(input, scene, scene.PreviousRowsButton);
    Assert(previousRowsAction == DataHuiAction.PreviousRowPage, "Previous rows button emitted the wrong typed Data action.");
    var firstRowPageAgain = await controller.ExecuteAsync(previousRowsAction);
    Assert(firstRowPageAgain.Grid.StartRow == 0 && firstRowPageAgain.Grid.Values[1][0] == "Ada", "Previous rows button did not return to the first Calc page.");

    // Seed one dependent formula through the typed session so the subsequent HUI edit
    // must traverse edit -> Calc recalculation -> refresh before the scene can pass.
    _ = await session.EditCellAsync(1, 2, string.Empty, "=B2*2");
    var withFormula = await controller.RefreshAsync();
    Assert(withFormula.Grid.Values[1][2] == "4", "Calc did not establish the dependent formula before the HUI edit path.");
    LayoutAndRequireRender(scene, layout, renderer, measure);

    PointerInvoke(input, scene.CellButton(1, 1));
    Assert(scene.SelectedRow == 1 && scene.SelectedColumn == 1, "HUI pointer selection did not select B2.");
    Assert(scene.CellButton(1, 1).Accessibility.Selected, "HUI selected-cell accessibility state was not updated.");

    var editAction = KeyboardInvokeAndDequeue(input, scene, scene.EditButton);
    Assert(editAction == DataHuiAction.EditSelected, "Edit button emitted the wrong typed Data action.");
    var edited = await controller.ExecuteAsync(editAction, editValue: "3");
    Assert(edited.Grid.Values[1][1] == "3", "HUI edit action did not update the selected Calc cell.");
    Assert(edited.Grid.Values[1][2] == "6", "HUI edit action did not expose Calc recalculation of the dependent formula.");

    LayoutAndRequireRender(scene, layout, renderer, measure);
    var sortAction = KeyboardInvokeAndDequeue(input, scene, scene.SortAscendingButton);
    Assert(sortAction == DataHuiAction.SortAscending, "Sort button emitted the wrong typed Data action.");
    var sorted = await controller.ExecuteAsync(sortAction);
    var expectedNames = new[] { "Eli", "Ada", "Gus", "Cara", "Iris", "Faye", "Drew", "Hope", "Bob" };
    var actualNames = sorted.Grid.Values.Skip(1).Take(9).Select(row => row[0]).ToArray();
    Assert(actualNames.SequenceEqual(expectedNames), $"HUI sort action returned wrong row order: {string.Join(", ", actualNames)}");
    Assert(sorted.Grid.Values[2][1] == "3", "Edited score did not survive the HUI-driven sort.");

    LayoutAndRequireRender(scene, layout, renderer, measure);
    var laterRowsAction = KeyboardInvokeAndDequeue(input, scene, scene.NextRowsButton);
    _ = await controller.ExecuteAsync(laterRowsAction);
    var laterColumnsAction = KeyboardInvokeAndDequeue(input, scene, scene.NextColumnsButton);
    var laterPage = await controller.ExecuteAsync(laterColumnsAction);
    Assert(laterPage.Grid.StartRow == DataGridSession.VisibleRows &&
        laterPage.Grid.StartColumn == DataGridSession.VisibleColumns,
        "Save/reopen reset was not exercised from a noninitial workbook page.");

    var saveAction = KeyboardInvokeAndDequeue(input, scene, scene.SaveReopenButton);
    Assert(saveAction == DataHuiAction.SaveAndReopen, "Save/reopen button emitted the wrong typed Data action.");
    var reopened = await controller.ExecuteAsync(saveAction, destinationPath: savedOds);
    Assert(File.Exists(savedOds) && new FileInfo(savedOds).Length > 0, "HUI save/reopen path did not produce an ODS workbook.");
    Assert(reopened.Grid.StartRow == 0 && reopened.Grid.StartColumn == 0 &&
        scene.PreviousRowsButton.State.HasFlag(HavenElementState.Disabled) &&
        scene.PreviousColumnsButton.State.HasFlag(HavenElementState.Disabled),
        "HUI save/reopen did not reset the page and disable previous-page controls.");
    var reopenedNames = reopened.Grid.Values.Skip(1).Take(9).Select(row => row[0]).ToArray();
    Assert(reopenedNames.SequenceEqual(expectedNames), "HUI save/reopen path did not preserve sorted row order.");
    Assert(reopened.Grid.Values[2][1] == "3", "HUI save/reopen path did not preserve the edited score.");

    LayoutAndRequireRender(scene, layout, renderer, measure);
    Assert(scene.CellButton(2, 1).Content == "3", "HUI scene did not refresh from the reopened workbook snapshot.");
    Assert((scene.StatusText.Accessibility.AccessibleName ?? string.Empty).Contains("selected", StringComparison.OrdinalIgnoreCase), "HUI status text did not expose an accessible selected-cell summary.");
    Assert((scene.StatusText.Content ?? string.Empty).Contains("rows 1–10", StringComparison.Ordinal) &&
        (scene.StatusText.Content ?? string.Empty).Contains("columns A–H", StringComparison.Ordinal) &&
        (scene.StatusText.Content ?? string.Empty).Contains("selected B2", StringComparison.Ordinal) &&
        scene.StatusText.Accessibility.AccessibleName == scene.StatusText.Content,
        "Reopened workbook status and accessibility text did not agree on the reset page and selected address.");

    await session.CloseAsync();
    Console.WriteLine("Haven Data HUI contract edit/recalc/sort/save-reopen runtime checks passed.");
}
finally
{
    try { Directory.Delete(temporaryRoot, recursive: true); } catch { }
}

file sealed class DataHuiMeasureContext : IHavenMeasureContext
{
    public HavenSize MeasureLeaf(HavenElement element, HavenSize available)
    {
        return element switch
        {
            Haven.UI.Components.Text text => FitText(text.Content, text.GetValue(HavenProperties.FontSize), available),
            Haven.UI.Components.Button button => new HavenSize(
                Math.Min(available.Width, Math.Max(84, button.Content.Length * 8 + 24)),
                Math.Min(available.Height, 42)),
            _ => new HavenSize(Math.Min(available.Width, 48), Math.Min(available.Height, 48)),
        };
    }

    private static HavenSize FitText(string text, double fontSize, HavenSize available)
    {
        var size = fontSize <= 0 ? 14 : fontSize;
        var width = Math.Max(24, text.Length * size * .58);
        return new HavenSize(Math.Min(available.Width, width), Math.Min(available.Height, size * 1.4));
    }
}
