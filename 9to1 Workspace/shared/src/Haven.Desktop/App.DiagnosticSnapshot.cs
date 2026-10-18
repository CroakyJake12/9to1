using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;

namespace Haven.Desktop;

/// <summary>
/// Opt-in, disabled-by-default diagnostic snapshot of runtime template/property origins.
/// Triggered ONLY when HAVEN_DIAGNOSTIC_SNAPSHOT=1. Output goes to HAVEN_DIAGNOSTIC_OUTPUT.
/// Uses non-mutating diagnostics: CuiPropertyPresentation.TryGetExisting (no projection
/// creation) and Avalonia's read-only GetDiagnostic/GetValueStoreDiagnostic.
/// </summary>
public sealed partial class App
{
    private Avalonia.Threading.DispatcherTimer? _diagnosticPoller;

    private void TryRunDiagnosticSnapshot()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("HAVEN_DIAGNOSTIC_SNAPSHOT"), "1", StringComparison.Ordinal))
            return;
        var outputDir = Environment.GetEnvironmentVariable("HAVEN_DIAGNOSTIC_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputDir)) return;

        // Poll for a trigger file so snapshots can be taken after interactions (e.g. opening a dropdown).
        _diagnosticPoller = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _diagnosticPoller.Tick += (_, _) =>
        {
            var triggerPath = Path.Combine(outputDir, "trigger.snapshot");
            if (!File.Exists(triggerPath)) return;
            try { File.Delete(triggerPath); } catch { }
            _ = Task.Run(async () =>
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try { await WriteSnapshotAsync(outputDir); }
                    catch (Exception ex)
                    {
                        Directory.CreateDirectory(outputDir);
                        await File.WriteAllTextAsync(Path.Combine(outputDir, "diagnostic-error.txt"), ex.ToString());
                    }
                });
            });
        };
        _diagnosticPoller.Start();

        // Also take an initial snapshot after 5 seconds.
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try { await WriteSnapshotAsync(outputDir); }
                catch (Exception ex)
                {
                    Directory.CreateDirectory(outputDir);
                    await File.WriteAllTextAsync(Path.Combine(outputDir, "diagnostic-error.txt"), ex.ToString());
                }
            });
        });
    }

    private async Task WriteSnapshotAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var snapshot = new Dictionary<string, object?>();
        snapshot["timestamp"] = DateTimeOffset.UtcNow;
        snapshot["assembly"] = typeof(App).Assembly.FullName;
        snapshot["assemblyLocation"] = typeof(App).Assembly.Location;
        // Loaded Spaces resource name + hash
        try
        {
            var asm = typeof(Haven.Desktop.Views.Pages.Spaces.SpacesHomePage).Assembly;
            var resourceName = "HavenOS.Apps.Spaces.UI.Home.SpacesHome.cui";
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream is not null)
            {
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ms.ToArray()));
                snapshot["spacesResource"] = new { name = resourceName, sha256 = hash, size = ms.Length };
            }
        }
        catch (Exception ex) { snapshot["spacesResourceError"] = ex.Message; }

        var controls = new List<Dictionary<string, object?>>();
        if (Avalonia.Application.Current is Avalonia.Application app
            && app.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } window)
        {
            WalkControl(window, controls);
            // Popup roots
            foreach (var popup in window.GetVisualDescendants().OfType<Popup>())
            {
                if (popup.Child is Control popupChild)
                    WalkControl(popupChild, controls);
            }
        }
        snapshot["controls"] = controls;
        var path = Path.Combine(outputDir, $"diagnostic-snapshot-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WalkControl(Control control, List<Dictionary<string, object?>> controls)
    {
        var entry = new Dictionary<string, object?>();
        entry["type"] = control.GetType().FullName;
        entry["name"] = control.Name;
        // Avalonia read-only diagnostics
        try
        {
            var diag = control is Avalonia.Controls.Primitives.TemplatedControl tc ? Avalonia.Diagnostics.AvaloniaObjectExtensions.GetDiagnostic(tc, Avalonia.Controls.Primitives.TemplatedControl.TemplateProperty) : null;
            entry["templateDiagnostic"] = new { value = diag?.Value?.ToString(), priority = diag?.Priority.ToString(), isOverridden = diag?.IsOverriddenCurrentValue };
        }
        catch { }
        // CUI non-mutating store lookup
        if (CuiPropertyPresentation.TryGetExisting(control, out var store) && store is not null)
        {
            entry["cuiProjection"] = "attached";
            try
            {
                var resolved = store.Resolve("Foreground");
                entry["cuiForeground"] = new { value = resolved.Value?.ToString(), source = resolved.Source, level = resolved.Level.ToString() };
            }
            catch { entry["cuiForeground"] = "unresolved"; }
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
