using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using CakeOS.Cui.Runtime;

namespace Haven.Desktop;

/// <summary>
/// Opt-in, disabled-by-default diagnostic snapshot of runtime template/property origins.
/// Triggered ONLY when HAVEN_DIAGNOSTIC_SNAPSHOT=1 is set in the environment. Output goes
/// to HAVEN_DIAGNOSTIC_OUTPUT (must be set). Never runs for ordinary launches.
/// Uses only non-mutating diagnostics: CuiPropertyPresentation.TryGetExisting (no projection
/// creation) and Avalonia's read-only GetDiagnostic/GetValueStoreDiagnostic.
/// </summary>
public sealed partial class App
{
    private void TryRunDiagnosticSnapshot()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("HAVEN_DIAGNOSTIC_SNAPSHOT"), "1", StringComparison.Ordinal))
            return;
        var outputDir = Environment.GetEnvironmentVariable("HAVEN_DIAGNOSTIC_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputDir)) return;

        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5)); // let the app initialize
            try
            {
                var snapshot = new Dictionary<string, object?>();
                snapshot["timestamp"] = DateTimeOffset.UtcNow;
                snapshot["assembly"] = typeof(App).Assembly.FullName;
                var controls = new List<Dictionary<string, object?>>();
                CollectControls(Avalonia.Application.Current, controls);
                snapshot["controls"] = controls;
                var path = Path.Combine(outputDir, "diagnostic-snapshot.json");
                Directory.CreateDirectory(outputDir);
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                var path = Path.Combine(outputDir, "diagnostic-error.txt");
                Directory.CreateDirectory(outputDir);
                await File.WriteAllTextAsync(path, ex.ToString());
            }
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private static void CollectControls(Avalonia.Application? app, List<Dictionary<string, object?>> controls)
    {
        if (app?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } window)
        {
            WalkControl(window, controls);
        }
    }

    private static void WalkControl(Control control, List<Dictionary<string, object?>> controls)
    {
        var entry = new Dictionary<string, object?>();
        entry["type"] = control.GetType().FullName;
        entry["name"] = control.Name;
        if (CuiPropertyPresentation.TryGetExisting(control, out var store) && store is not null)
        {
            entry["cuiProjection"] = "attached";
            // Non-mutating: only read if projection exists.
        }
        else
        {
            entry["cuiProjection"] = "none";
        }
        controls.Add(entry);
        foreach (var child in Avalonia.VisualTree.VisualExtensions.GetVisualChildren(control).OfType<Control>())
            WalkControl(child, controls);
    }
}
