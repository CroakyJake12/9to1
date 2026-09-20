using System.Diagnostics;
using Haven.UI;
using Haven.UI.Components;
using HavenOS.Apps.Data;
using HavenOS.Apps.Data.Hui;

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

var root = FindRepositoryRoot();
var python = Environment.GetEnvironmentVariable("HAVEN_DATA_PYTHON") ?? "/usr/bin/python3";
var calcWorker = Path.Combine(root, "apps", "Data", "workers", "calc_worker.py");
var temporaryRoot = Path.Combine(Path.GetTempPath(), $"haven-data-hui-runtime-{Guid.NewGuid():N}");
Directory.CreateDirectory(temporaryRoot);

try
{
    var csvPath = Path.Combine(temporaryRoot, "fixture.csv");
    var sourceOds = Path.Combine(temporaryRoot, "fixture.ods");
    var savedOds = Path.Combine(temporaryRoot, "hui-saved.ods");
    await File.WriteAllTextAsync(
        csvPath,
        "Name,Score,Double\n" +
        "Ada,2,\n" +
        "Bob,10,\n" +
        "Cara,5,\n" +
        "Drew,8,\n" +
        "Eli,1,\n" +
        "Faye,7,\n" +
        "Gus,4,\n" +
        "Hope,9,\n" +
        "Iris,6,\n");
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
    LayoutAndRequireRender(scene, layout, renderer, measure);

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
    var saveAction = KeyboardInvokeAndDequeue(input, scene, scene.SaveReopenButton);
    Assert(saveAction == DataHuiAction.SaveAndReopen, "Save/reopen button emitted the wrong typed Data action.");
    var reopened = await controller.ExecuteAsync(saveAction, destinationPath: savedOds);
    Assert(File.Exists(savedOds) && new FileInfo(savedOds).Length > 0, "HUI save/reopen path did not produce an ODS workbook.");
    var reopenedNames = reopened.Grid.Values.Skip(1).Take(9).Select(row => row[0]).ToArray();
    Assert(reopenedNames.SequenceEqual(expectedNames), "HUI save/reopen path did not preserve sorted row order.");
    Assert(reopened.Grid.Values[2][1] == "3", "HUI save/reopen path did not preserve the edited score.");

    LayoutAndRequireRender(scene, layout, renderer, measure);
    Assert(scene.CellButton(2, 1).Content == "3", "HUI scene did not refresh from the reopened workbook snapshot.");
    Assert((scene.StatusText.Accessibility.AccessibleName ?? string.Empty).Contains("selected", StringComparison.OrdinalIgnoreCase), "HUI status text did not expose an accessible selected-cell summary.");

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
