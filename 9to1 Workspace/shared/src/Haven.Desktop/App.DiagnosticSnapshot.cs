using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using System.Runtime.CompilerServices;
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
    private EventHandler? _diagnosticPollerTick;
    private CancellationTokenSource? _diagnosticSnapshotCts;

    private void TryRunDiagnosticSnapshot()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("HAVEN_DIAGNOSTIC_SNAPSHOT"), "1", StringComparison.Ordinal))
            return;
        var outputDir = Environment.GetEnvironmentVariable("HAVEN_DIAGNOSTIC_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputDir)) return;

        _diagnosticSnapshotCts = new CancellationTokenSource();
        var token = _diagnosticSnapshotCts.Token;

        _diagnosticPollerTick = (_, _) =>
        {
            if (token.IsCancellationRequested) { StopDiagnosticPoller(); return; }
            var triggerPath = Path.Combine(outputDir, "trigger.snapshot");
            if (!File.Exists(triggerPath)) return;
            try { File.Delete(triggerPath); }
            catch (Exception ex)
            {
                Directory.CreateDirectory(outputDir);
                File.AppendAllText(Path.Combine(outputDir, "diagnostic-trigger-error.txt"), ex.ToString() + "\n");
                return;
            }
            _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try { await WriteSnapshotAsync(outputDir, token); }
                catch (Exception ex)
                {
                    Directory.CreateDirectory(outputDir);
                    await File.WriteAllTextAsync(Path.Combine(outputDir, "diagnostic-error.txt"), ex.ToString());
                }
            });
        };
        _diagnosticPoller = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _diagnosticPoller.Tick += _diagnosticPollerTick;
        _diagnosticPoller.Start();

        // Initial snapshot after 5 seconds.
        _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            if (token.IsCancellationRequested) return;
            try { await WriteSnapshotAsync(outputDir, token); }
            catch (Exception ex)
            {
                Directory.CreateDirectory(outputDir);
                await File.WriteAllTextAsync(Path.Combine(outputDir, "diagnostic-error.txt"), ex.ToString());
            }
        });
    }

    private void StopDiagnosticPoller()
    {
        if (_diagnosticPoller is not null && _diagnosticPollerTick is not null)
            _diagnosticPoller.Tick -= _diagnosticPollerTick;
        _diagnosticPoller?.Stop();
        _diagnosticPoller = null;
        _diagnosticPollerTick = null;
    }

    private void StopDiagnosticSnapshot()
    {
        StopDiagnosticPoller();
        _diagnosticSnapshotCts?.Cancel();
        _diagnosticSnapshotCts?.Dispose();
        _diagnosticSnapshotCts = null;
    }

    private async Task WriteSnapshotAsync(string outputDir, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;
        Directory.CreateDirectory(outputDir);
        var snapshot = new Dictionary<string, object?>();
        snapshot["timestamp"] = DateTimeOffset.UtcNow;
        snapshot["captureId"] = Guid.NewGuid().ToString("N");
        snapshot["assembly"] = typeof(App).Assembly.FullName;
        snapshot["assemblyLocation"] = typeof(App).Assembly.Location;
        snapshot["executable"] = Environment.ProcessPath;

        // Loaded Spaces resource name + hash (actual owner: SpacesHomeCuiDocument)
        try
        {
            var asm = typeof(HavenOS.Apps.Spaces.SpacesHomeCuiDocument).Assembly;
            var resourceName = HavenOS.Apps.Spaces.SpacesHomeCuiDocument.ResourceName;
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream is not null)
            {
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, token);
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ms.ToArray()));
                snapshot["spacesResource"] = new { name = resourceName, sha256 = hash, size = ms.Length, assembly = asm.FullName };
            }
            else
            {
                snapshot["spacesResourceError"] = $"Resource '{resourceName}' not found in {asm.FullName}";
            }
        }
        catch (Exception ex) { snapshot["spacesResourceError"] = ex.ToString(); }

        var controls = new List<Dictionary<string, object?>>();
        var visited = new HashSet<int>(); // deduplicate by object identity
        if (Avalonia.Application.Current is Avalonia.Application app
            && app.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } window)
        {
            WalkControl(window, controls, visited);
            // Popup roots discovered via public APIs only
            foreach (var popup in window.GetVisualDescendants().OfType<Popup>())
            {
                if (popup.IsOpen && popup.Child is Control popupChild)
                    WalkControl(popupChild, controls, visited);
            }
        }
        snapshot["controls"] = controls;
        snapshot["controlCount"] = controls.Count;
        snapshot["partialCoverage"] = visited.Count < 1000 ? "complete" : "truncated";

        // Atomic write with unique capture ID
        var captureId = Guid.NewGuid().ToString("N");
        var path = Path.Combine(outputDir, $"diagnostic-snapshot-{captureId}.json");
        var tmpPath = path + ".tmp";
        await File.WriteAllTextAsync(tmpPath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }), token);
        File.Move(tmpPath, path, overwrite: false);
    }

    private static void WalkControl(Control control, List<Dictionary<string, object?>> controls, HashSet<int> visited)
    {
        var identity = RuntimeHelpers.GetHashCode(control);
        if (!visited.Add(identity)) return; // deduplicate by object identity

        var entry = new Dictionary<string, object?>();
        entry["type"] = control.GetType().FullName;
        entry["name"] = control.Name;

        // Avalonia read-only diagnostics
        var diagProps = new List<Avalonia.AvaloniaProperty>
        {
            Avalonia.Controls.Control.WidthProperty,
            Avalonia.Controls.Control.HeightProperty,
            Avalonia.Layout.Layoutable.MarginProperty,
        };
        if (control is Avalonia.Controls.TextBlock tb)
        {
            diagProps.Add(Avalonia.Controls.TextBlock.FontSizeProperty);
            diagProps.Add(Avalonia.Controls.TextBlock.FontFamilyProperty);
            diagProps.Add(Avalonia.Controls.TextBlock.ForegroundProperty);
        }
        if (control is Avalonia.Controls.Panel || control is Avalonia.Controls.Border)
        {
            diagProps.Add(Avalonia.Controls.Panel.BackgroundProperty);
        }
        if (control is Avalonia.Controls.Primitives.TemplatedControl tc2)
        {
            diagProps.Add(Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty);
        }

        var avaloniaDiags = new Dictionary<string, object?>();
        foreach (var prop in diagProps)
        {
            try
            {
                var d = Avalonia.Diagnostics.AvaloniaObjectExtensions.GetDiagnostic(control, prop);
                if (d is not null)
                {
                    avaloniaDiags[prop.Name] = new
                    {
                        value = d.Value?.ToString(),
                        priority = d.Priority.ToString(),
                        isOverridden = d.IsOverriddenCurrentValue,
                        diagnostic = d.Diagnostic?.ToString()
                    };
                }
                else
                {
                    avaloniaDiags[prop.Name] = "no-diagnostic";
                }
            }
            catch (Exception ex) { avaloniaDiags[prop.Name] = new { error = ex.Message }; }
        }
        entry["avaloniaDiagnostics"] = avaloniaDiags;

        // CUI non-mutating store lookup
        if (CuiPropertyPresentation.TryGetExisting(control, out var store) && store is not null)
        {
            entry["cuiProjection"] = "attached";
            var cuiDiags = new Dictionary<string, object?>();
            foreach (var propName in new[] { "Foreground", "Background", "FontSize", "FontFamily", "Padding", "Margin" })
            {
                try
                {
                    var resolved = store.Resolve(propName);
                    cuiDiags[propName] = new { value = resolved.Value?.ToString(), source = resolved.Source, level = resolved.Level.ToString(), order = resolved.Order };
                }
                catch (Exception ex) { cuiDiags[propName] = new { error = ex.Message }; }
            }
            entry["cuiDiagnostics"] = cuiDiags;
        }
        else
        {
            entry["cuiProjection"] = "none";
        }
        controls.Add(entry);
        foreach (var child in Avalonia.VisualTree.VisualExtensions.GetVisualChildren(control).OfType<Control>())
            WalkControl(child, controls, visited);
    }
}
