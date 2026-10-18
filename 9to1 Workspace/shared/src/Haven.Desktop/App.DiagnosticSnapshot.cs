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
    private EventHandler? _diagnosticPollerTick;
    private CancellationTokenSource? _diagnosticSnapshotCts;
    private Task? _diagnosticCaptureTask;

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
            _diagnosticCaptureTask = _originalAppWork.RunAsync(async original =>
            {
                await original.AwaitAsync(Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try { await WriteSnapshotAsync(outputDir, token); }
                    catch (Exception ex)
                    {
                        Directory.CreateDirectory(outputDir);
                        await File.WriteAllTextAsync(Path.Combine(outputDir, "diagnostic-error.txt"), ex.ToString());
                    }
                })).ConfigureAwait(false);
            });
        };
        _diagnosticPoller = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _diagnosticPoller.Tick += _diagnosticPollerTick;
        _diagnosticPoller.Start();
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
        var captureId = Guid.NewGuid().ToString("N");
        var snapshot = new Dictionary<string, object?>();
        snapshot["captureId"] = captureId;
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
        var visited = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        if (Avalonia.Application.Current is Avalonia.Application app
            && app.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } window)
        {
            WalkControl(window, controls, visited);
            foreach (var popup in window.GetVisualDescendants().OfType<Popup>())
            {
                if (popup.IsOpen && popup.Child is Control popupChild)
                    WalkControl(popupChild, controls, visited);
            }
        }
        snapshot["controls"] = controls;
        snapshot["controlCount"] = controls.Count;

        var path = Path.Combine(outputDir, $"diagnostic-snapshot-{captureId}.json");
        var tmpPath = path + ".tmp";
        await File.WriteAllTextAsync(tmpPath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }), token);
        File.Move(tmpPath, path, overwrite: false);
    }

    private static void WalkControl(Control control, List<Dictionary<string, object?>> controls, HashSet<Control> visited)
    {
        if (!visited.Add(control)) return;

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
        // GetValueStoreDiagnostic: use reflection since API differs between Avalonia versions.
        try
        {
            var vsd = Avalonia.Diagnostics.AvaloniaObjectExtensions.GetValueStoreDiagnostic(control);
            if (vsd is not null)
            {
                var vsdType = vsd.GetType();
                var framesProp = vsdType.GetProperty("AppliedFrames") ?? vsdType.GetProperty("Frames");
                if (framesProp is not null)
                {
                    var framesVal = framesProp.GetValue(vsd);
                    if (framesVal is System.Collections.IEnumerable enumerable)
                    {
                        var frames = new List<Dictionary<string, object?>>();
                        foreach (var frame in enumerable)
                        {
                            var frameType = frame.GetType();
                            var frameEntry = new Dictionary<string, object?>();
                            foreach (var propName in new[] { "Source", "Type", "IsActive", "Priority" })
                            {
                                var prop = frameType.GetProperty(propName);
                                if (prop is not null)
                                    frameEntry[propName.ToLowerInvariant()] = prop.GetValue(frame)?.ToString();
                            }
                            // Values
                            var valuesProp = frameType.GetProperty("Values");
                            if (valuesProp is not null)
                            {
                                var vals = valuesProp.GetValue(frame);
                                if (vals is System.Collections.IEnumerable valEnum)
                                {
                                    var values = new Dictionary<string, string?>();
                                    foreach (var v in valEnum)
                                    {
                                        var vType = v.GetType();
                                        var propProp = vType.GetProperty("Property");
                                        var valProp = vType.GetProperty("Value");
                                        var propName = propProp?.GetValue(v)?.ToString() ?? "?";
                                        values[propName] = valProp?.GetValue(v)?.ToString();
                                    }
                                    frameEntry["values"] = values;
                                }
                            }
                            frames.Add(frameEntry);
                        }
                        avaloniaDiags["valueStoreFrames"] = frames;
                    }
                    else
                    {
                        avaloniaDiags["valueStoreFrames"] = "AppliedFrames property exists but is not enumerable";
                    }
                }
                else
                {
                    // Record all public properties for diagnosis
                    var propNames = vsdType.GetProperties().Select(p => p.Name).ToArray();
                    avaloniaDiags["valueStoreProperties"] = propNames;
                }
            }
        }
        catch (Exception ex) { avaloniaDiags["valueStoreError"] = ex.ToString(); }
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
            WalkControl(child, controls, visited);
    }
}
