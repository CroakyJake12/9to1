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
    private CancellationTokenSource? _diagnosticSnapshotCts;

    private void TryRunDiagnosticSnapshot()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("HAVEN_DIAGNOSTIC_SNAPSHOT"), "1", StringComparison.Ordinal))
            return;
        var outputDir = Environment.GetEnvironmentVariable("HAVEN_DIAGNOSTIC_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputDir)) return;

        _diagnosticSnapshotCts = new CancellationTokenSource();
        var token = _diagnosticSnapshotCts.Token;

        // Poll for a trigger file so snapshots can be taken after interactions.
        _diagnosticPoller = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _diagnosticPoller.Tick += (_, _) =>
        {
            if (token.IsCancellationRequested) { _diagnosticPoller?.Stop(); return; }
            var triggerPath = Path.Combine(outputDir, "trigger.snapshot");
            if (!File.Exists(triggerPath)) return;
            try { File.Delete(triggerPath); } catch { }
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
        _diagnosticPoller.Start();

        // Initial snapshot after 5 seconds.
        _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            try { await WriteSnapshotAsync(outputDir, token); }
            catch (Exception ex)
            {
                Directory.CreateDirectory(outputDir);
                await File.WriteAllTextAsync(Path.Combine(outputDir, "diagnostic-error.txt"), ex.ToString());
            }
        });
    }

    private void StopDiagnosticSnapshot()
    {
        _diagnosticPoller?.Stop();
        _diagnosticPoller = null;
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
        if (Avalonia.Application.Current is Avalonia.Application app
            && app.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } window)
        {
            WalkControl(window, controls);
            foreach (var popup in window.GetVisualDescendants().OfType<Popup>())
            {
                if (popup.IsOpen && popup.Child is Control popupChild)
                    WalkControl(popupChild, controls);
            }
        }
        snapshot["controls"] = controls;
        var path = Path.Combine(outputDir, $"diagnostic-snapshot-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }), token);
    }

    private static void WalkControl(Control control, List<Dictionary<string, object?>> controls)
    {
        var entry = new Dictionary<string, object?>();
        entry["type"] = control.GetType().FullName;
        entry["name"] = control.Name;

        // Avalonia read-only diagnostics: GetDiagnostic (Value/Priority/Diagnostic/IsOverriddenCurrentValue)
        // and GetValueStoreDiagnostic (AppliedFrames with Source/Type/IsActive/Priority/Values)
        var diagProps = new Avalonia.AvaloniaProperty[]
        {
            Avalonia.Controls.Control.WidthProperty,
            Avalonia.Controls.Control.HeightProperty,
            Avalonia.Layout.Layoutable.MarginProperty,
        };
        // Try type-specific properties via reflection-safe casts.
        if (control is Avalonia.Controls.TextBlock tb)
        {
            diagProps = diagProps.Concat(new Avalonia.AvaloniaProperty[]
            {
                Avalonia.Controls.TextBlock.FontSizeProperty,
                Avalonia.Controls.TextBlock.FontFamilyProperty,
                Avalonia.Controls.TextBlock.ForegroundProperty,
            }).ToArray();
        }
        if (control is Avalonia.Controls.Panel || control is Avalonia.Controls.Border)
        {
            diagProps = diagProps.Concat(new Avalonia.AvaloniaProperty[]
            {
                Avalonia.Controls.Panel.BackgroundProperty,
            }).ToArray();
        }
        if (control is Avalonia.Controls.Primitives.TemplatedControl tc2)
        {
            diagProps = diagProps.Concat(new Avalonia.AvaloniaProperty[]
            {
                Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty,
            }).ToArray();
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
            }
            catch (Exception ex) { avaloniaDiags[prop.Name] = new { error = ex.Message }; }
        }
        // GetValueStoreDiagnostic frames (best-effort; API may differ by Avalonia version)
        try
        {
            var vsd = Avalonia.Diagnostics.AvaloniaObjectExtensions.GetValueStoreDiagnostic(control);
            if (vsd is not null)
            {
                avaloniaDiags["valueStoreDiagnostic"] = vsd.ToString();
            }
        }
        catch (Exception ex) { avaloniaDiags["valueStoreError"] = ex.Message; }
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
            try { cuiDiags["inspect"] = store.Inspect().Select(p => new { p.Name, value = p.Value?.ToString(), source = p.Source, level = p.Level.ToString() }).ToArray(); }
            catch (Exception ex) { cuiDiags["inspectError"] = ex.Message; }
            entry["cuiDiagnostics"] = cuiDiags;
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
