using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using NineToOne.Web;

// Geometry UNIT only: actual production Render, owner CUI/parser/templates/fonts and
// glyph layout; scripted public binding projection, no media/storage/action success.
// 195 logical units supports geometry; it is NOT evidence of browser 200% zoom.
if (args.Length != 6) throw new ArgumentException("Wave.cui fresh.json width height default|wave-opt-in native-font-factor(1|2)");
var markup = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (File.Exists(output)) throw new IOException("Fresh output required.");
var width = double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
var height = double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(height) || height <= 0) throw new ArgumentException("Positive finite native viewport required.");
var fontFactor = double.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture);
if (fontFactor is not (1d or 2d)) throw new ArgumentException("Explicit native font factor1 or2 required; this is not browser zoom.");
var optIn = args[4] == "wave-opt-in";
if (!optIn && args[4] != "default") throw new ArgumentException("Unknown host policy.");
await using var session = HeadlessUnitTestSession.StartNew(typeof(WaveLayoutApplication));
var failures = await session.Dispatch(() =>
{
    var parser = new CuiRichParser();
    var document = parser.Parse(File.ReadAllText(markup), markup);
    if (parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error)) throw new InvalidDataException("Actual Wave markup must parse without errors.");
    var projection = new GeometryProjection();
    var app = new BrowserApplication();
    var view = (ContentControl)typeof(BrowserApplication).GetField("_view", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
    var render = typeof(BrowserApplication).GetMethod("Render", BindingFlags.NonPublic | BindingFlags.Instance)!;
    // Public ctor binding allows the identical checks to compile against the frozen
    // original record that predates the optional flag. No layout property is mutated.
    var constructor = typeof(BrowserCuiSurface).GetConstructors().Single();
    var parameters = constructor.GetParameters();
    var arguments = parameters.Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
    arguments[0] = document; arguments[1] = projection; arguments[2] = projection;
    var policyIndex = Array.FindIndex(parameters, p => p.Name == "ConstrainHorizontalLayout");
    if (policyIndex >= 0) arguments[policyIndex] = optIn;
    var surface = (BrowserCuiSurface)constructor.Invoke(arguments);
    var window = new Window { Content = view, Width = width, Height = height };
    var checks = new List<object>(); var failed = 0;
    void Check(string name, bool passed) { checks.Add(new { name, passed }); Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}"); if (!passed) failed++; }
    window.Show();
    try
    {
        if (render.Invoke(app, [surface, "#/app.wave"]) is not true) throw new InvalidOperationException("Actual production Render failed.");
        using var frame = window.CaptureRenderedFrame();
        if (frame is null) throw new InvalidOperationException("Actual native frame required before geometry inspection.");
        var scroll = (ScrollViewer)view.Content!; var root = (Control)scroll.Content!;
        var controls = root.GetVisualDescendants().OfType<Control>().Prepend(root).ToArray();
        // Snapshot every actual effective font BEFORE setting any property. Set
        // local values on each existing owner control, avoiding inherited x2 twice.
        // This is a native font-enlargement UNIT, not a shipped preference or zoom API.
        var fonts = new List<(Control Control, double Before, Func<double> Read, Action<double> Set)>();
        foreach (var text in controls.OfType<TextBlock>())
            fonts.Add((text, text.FontSize, () => text.FontSize, value => text.SetCurrentValue(TextBlock.FontSizeProperty, value)));
        foreach (var owner in controls.Where(c => c is Button or TextBox).Cast<TemplatedControl>())
            fonts.Add((owner, owner.FontSize, () => owner.FontSize, value => owner.SetCurrentValue(TemplatedControl.FontSizeProperty, value)));
        if (fonts.Count == 0 || fonts.Any(f => !double.IsFinite(f.Before) || f.Before <= 0)) throw new InvalidDataException("Actual effective native fonts missing.");
        foreach (var font in fonts) font.Set(font.Before * fontFactor);
        using var enlargedFrame = window.CaptureRenderedFrame();
        if (enlargedFrame is null) throw new InvalidOperationException("Actual post-font native frame required.");
        var currentControls = root.GetVisualDescendants().OfType<Control>().Prepend(root).ToArray();
        Check("Actual native fonts equal requested baseline factor", fonts.All(f => Math.Abs(f.Read() - f.Before * fontFactor) < 0.000001)
            && currentControls.Count(c => c is TextBlock or Button or TextBox) == fonts.Count
            && fonts.All(f => currentControls.Contains(f.Control)));
        Rect InRoot(Control c) { var point = c.TranslatePoint(default, root) ?? throw new InvalidOperationException("Detached control."); return new Rect(point, c.Bounds.Size); }
        bool Fit(Control c) { var r = InRoot(c); return c.IsMeasureValid && c.IsArrangeValid && r.Width > 0 && double.IsFinite(r.Width) && r.X >= -0.1 && r.Right <= root.Bounds.Width + 0.1; }
        // Actual loader-generated action Tag and stable CUI identity separate authored
        // Wave command controls from Fluent ScrollBar RepeatButton template parts.
        var buttons = controls.OfType<Button>().Where(c => c.IsEffectivelyVisible
            && c.Tag is string && CuiRuntimeIdentity.GetStableId(c) is not null).ToArray();
        var inputs = controls.OfType<TextBox>().Where(c => c.IsEffectivelyVisible).ToArray();
        var texts = controls.OfType<TextBlock>().Where(c => c.IsEffectivelyVisible && !string.IsNullOrEmpty(c.Text)).Select(c => new {
            text = c.Text!, bounds = InRoot(c), width = c.Bounds.Width, height = c.Bounds.Height,
            valid = c.IsMeasureValid && c.IsArrangeValid, complete = c.TextLayout.TextLines.Sum(l => l.Length),
            // Diagnostic only: full-range hit rectangles can include invisible trailing
            // whitespace advances. Preserve ALL existing checks/tolerance unchanged.
            lines = c.TextLayout.TextLines.Select(l => new {
                l.FirstTextSourceIndex, l.Length, l.NewLineLength, l.TrailingWhitespaceLength,
                l.Start, l.Width, l.WidthIncludingTrailingWhitespace, l.Height, l.Extent,
                l.Baseline, l.HasOverflowed, l.HasCollapsed,
                l.OverhangLeading, l.OverhangTrailing, l.OverhangAfter,
                sourceSpanValid = l.FirstTextSourceIndex >= 0 && l.Length >= 0
                    && (long)l.FirstTextSourceIndex + l.Length <= c.Text!.Length,
                sourceSpan = l.FirstTextSourceIndex >= 0 && l.Length >= 0
                    && (long)l.FirstTextSourceIndex + l.Length <= c.Text!.Length
                    ? c.Text!.Substring(l.FirstTextSourceIndex, l.Length) : null
            }).ToArray(),
            glyphs = c.TextLayout.HitTestTextRange(0, c.Text!.Length).ToArray(), padding = c.Padding }).ToArray();
        Check("Actual native client is requested width", Math.Abs(window.ClientSize.Width - width) < 0.1);
        Check("Outer policy is opt-in only", scroll.HorizontalScrollBarVisibility == (optIn && policyIndex >= 0 ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto));
        if (optIn)
        {
            Check("Wave native outer extent fits viewport", scroll.Extent.Width <= scroll.Viewport.Width + 0.1 && root.Bounds.Width <= scroll.Viewport.Width + 0.1);
            Check("Every actual Wave button fits horizontally", buttons.Length == 32 && buttons.All(b => b.Tag is string action && GeometryProjection.Commands.Contains(action)) && buttons.All(Fit));
            Check("Every actual Wave input fits horizontally", inputs.Length >= 13 && inputs.All(Fit));
            Check("Every full native Wave glyph range is retained", texts.Length > 25 && texts.All(t => t.valid && t.glyphs.Length > 0 && t.complete >= t.text.Length));
            Check("Every full native Wave glyph fits assigned bounds", texts.All(t => t.glyphs.All(g => g.X >= -0.1 && g.Y >= -0.1 && g.Right + t.padding.Left <= t.width + 0.1 && g.Bottom + t.padding.Top <= t.height + 0.1)));
            Check("Every full native Wave text stays inside root", texts.All(t => t.glyphs.All(g => t.bounds.X + t.padding.Left + g.X >= -0.1 && t.bounds.X + t.padding.Left + g.Right <= root.Bounds.Width + 0.1)));
            var wrapPanels = controls.OfType<WrapPanel>().Where(c => c.IsEffectivelyVisible).ToArray();
            Check("Actual wrapped siblings never overlap", wrapPanels.Length >= 8 && wrapPanels.All(panel => {
                var items = panel.Children.Where(c => c.IsEffectivelyVisible).ToArray();
                return items.SelectMany((a, i) => items.Skip(i + 1).Select(b => (a, b))).All(pair => {
                    var a = InRoot(pair.a); var b = InRoot(pair.b);
                    return Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X) <= 0.1 || Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y) <= 0.1;
                });
            }));
            Check("Canonical label projections are complete", new[] { GeometryProjection.ProjectLabel, GeometryProjection.TrackLabel, GeometryProjection.ClipLabel, GeometryProjection.SelectionLabel }.All(expected => texts.Any(t => t.text == expected && t.complete >= expected.Length)));
            Check("Native controls keep usable focus and names", buttons.Length == 32 && buttons.All(b => b.IsEnabled && b.Focusable && !string.IsNullOrWhiteSpace(Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(b).GetName())));
            var localScrolls = controls.OfType<ScrollViewer>().Where(c => c.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto && c.Content is Control content && (content.Name == "wave-waveform-scroll" || content.GetVisualDescendants().OfType<Control>().Any(x => x.Name == "wave-waveform-scroll"))).ToArray();
            Check("Deliberate waveform keeps local horizontal scrolling", localScrolls.Length == 1 && Fit(localScrolls[0]) && localScrolls[0].Extent.Width > localScrolls[0].Viewport.Width);
        }
        else Check("Default owner host behavior is unchanged", scroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { scope = "Native geometry/font enlargement UNIT; scripted public projection only; no Wave action/storage/audio/browser/zoom acceptance", width, height, fontFactor, nativeFonts = fonts.Select(f => new { f.Control.Name, before = f.Before, actual = f.Read() }), optIn, policyPresent = policyIndex >= 0, checks, failures = failed,
            nativeClient = window.ClientSize, extent = scroll.Extent, viewport = scroll.Viewport, root = root.Bounds,
            texts, buttons = buttons.Select(b => new { b.Name, bounds = InRoot(b), b.IsEnabled }), inputs = inputs.Select(c => new { c.Name, bounds = InRoot(c) }) }, new JsonSerializerOptions { WriteIndented = true }));
        return failed;
    }
    finally { app.ResetPrivateContext(); window.Content = null; window.Close(); }
}, CancellationToken.None);
return failures > 0 ? 1 : 0; // Natural native outcome; structured failed checks stay durable. Setup exceptions are not expected RED.

public sealed class GeometryProjection : ICuiWritableBindingContext, ICuiActionDispatcher, ICuiActionAvailability
{
    // Scripted geometry availability only: these actions STILL ALWAYS THROW.
    public static readonly HashSet<string> Commands = new(StringComparer.Ordinal) { "AddTrack", "Close", "Create", "Delete", "Duplicate", "Export", "Import", "Join", "Marker", "Mixer", "Move", "Mute", "OpenProject", "OpenProjectRow", "Pause", "Play", "Processing", "Redo", "Refresh", "Reopen", "Resume", "RippleDelete", "Save", "Seek", "SelectClip", "SelectClipRow", "SelectTrack", "SelectTrackRow", "Solo", "Split", "Stop", "ToggleLoop", "Trim", "Undo" };
    public bool? IsActionAvailable(string command) => Commands.Contains(command);
    public const string Id = "f69fd693-4d20-4b3c-b35b-b7e902728bf2";
    public const string ProjectLabel = "Open Narrow field recording · revision 12 · " + Id;
    public const string TrackLabel = "Selected: Field recording stereo · " + Id;
    public const string ClipLabel = "Selected clip: " + Id + " · 96000 frames";
    public const string SelectionLabel = "Clip " + Id + " · source " + Id + " · source frames 0–96000 · timeline frame 48000 · gain 0.75 · fades 1200/2400 frames";
    private static Dictionary<string, object?> Row(string id, string label) => new() { ["ID"] = id, ["Label"] = label };
    public bool TryGetValue(string path, out object? value)
    {
        // These are scripted UI lookup values, not persisted WaveProject/authority.
        value = path switch {
            "Projects" => new[] { Row(Id, ProjectLabel), Row("d9b49a84-72cb-45eb-80f6-08fa05e77ec7", "Open Second recording · revision 3 · d9b49a84-72cb-45eb-80f6-08fa05e77ec7") },
            "Tracks" => new[] { Row(Id, TrackLabel) }, "Clips" => new[] { Row(Id, ClipLabel) },
            "Timeline" => new[] { new Dictionary<string, object?> { ["ID"] = Id, ["Text"] = "Field recording stereo · 1.000s → 3.000s · " + Id } },
            "Waveform" => Enumerable.Range(0, 600).Select(i => new Dictionary<string, object?> { ["ID"] = i, ["Height"] = 75d, ["Description"] = $"Bucket {i + 1}: peak 0.750" }).ToArray(),
            "Title" => "Narrow field recording", "SampleRate" => "48000", "Channels" => "2", "Gain" => "0.75", "FadeIn" => "0.025", "FadeOut" => "0.05", "Pan" => "0", "Seek" => "1", "MarkerName" => "Marker", "JoinClipId" => Id,
            "Start" => "1", "TrimStart" => "0.1", "TrimEnd" => "0.2",
            "Status" => "Saved local project · revision 12. This is scripted geometry data, not a save receipt.",
            "ProjectIdentity" => "Project " + Id + " · 48000 Hz · 2 channels · revision 12",
            "Selection" => SelectionLabel, "Transport" => "Paused/stopped · 1.000s · Loop off", "LoopLabel" => "Loop playback: off",
            "PreviewStatus" => "Mixed preview: 600 buckets · 48000 Hz · 2 channels", "Mixer" => "Track " + Id + " · gain 0.75 · pan 0 · mute False · solo False", "Annotations" => "Marker at frame 48000 (" + Id + ")",
            "NotBusy" or "CanEdit" or "CanUndo" or "CanRedo" or "HasClip" => true, _ => null };
        return value is not null;
    }
    // WireInputWriteBack requires the public host interface. This geometry-only
    // fixture never edits: every attempted write is rejected, not successful state.
    public bool TrySetValue(string path, object? value) => false;
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Geometry fixture cannot claim Wave action success.");
}
public sealed class WaveLayoutApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<WaveLayoutApplication>().UseSkia()).UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this, "Wave");
}
