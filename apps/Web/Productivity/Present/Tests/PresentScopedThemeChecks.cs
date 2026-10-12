using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Infrastructure;
using Haven.UI;
using NineToOne.Web.Productivity.Present;

internal static class PresentScopedThemeChecks
{
    internal static async Task<int> Run(IAppPaths paths)
    {
        var count = 0; var failures = new List<string>();
        void Check(bool pass, string description)
        {
            Console.WriteLine((pass ? "ASSERT " : "FAIL ") + description);
            if (pass) count++; else failures.Add(description);
        }
        var document = PresentDocument.Create("Scoped CUI document colours");
        var owner = new PresentEditor(document);
        var shape = owner.AddShape(owner.Selection.SlideId);
        owner.SetSelectedElementStyle(new PresentElementStyle { FillColor = "#C93763", StrokeWidth = 0 });
        var repository = new PresentRepository(paths);
        await repository.SaveAsync(document, "Scoped theme preservation fixture", default);
        await using var session = new PresentBrowserSession(repository);
        Check((await session.OpenAsync(document.Id)).Succeeded, "scoped theme fixture opens through the original document repository");
        session.Editor!.SelectElements([shape.Id]);
        var before = JsonSerializer.Serialize(session.GetDocumentSnapshot());
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Present.cui"));
        source = Regex.Replace(source, "<Object\\b(?=[^>]*Type=\"Present(?:SlideCanvas|Thumbnails)\")[\\s\\S]*?</Object>",
            match => "<Theme Name=\"Retro\">" + match.Value + "</Theme>");
        var surface = PresentBrowserSurface.Create(new CuiRichParser().Parse(source), session, () => true);
        using var loader = new CuiControlLoader(surface.ControlRegistry!);
        loader.SetSurface("Present"); loader.SetAppearance(CuiAppearance.Bright);
        loader.SetBindingContext(session); loader.SetActionDispatcher(session);
        var (root, diagnostics) = loader.TryLoad(surface.Document);
        if (root is null || diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidOperationException("Canonical Present CUI with explicit Object themes failed to load");
        loader.WireBindings(root);
        var window = new Window { Width = 1280, Height = 900, Content = root }; window.Show();
        try
        {
            var canvasOwner = root.GetVisualDescendants().OfType<PresentRetainedSceneControl>().Single();
            var thumbnailOwner = root.GetVisualDescendants().OfType<PresentThumbnailControl>().Single();
            var canvas = canvasOwner.GetVisualDescendants().OfType<HavenSceneControl>().Single();
            var thumbnails = thumbnailOwner.GetVisualDescendants().OfType<HavenSceneControl>().Single();
            Color Resource(Control control, string key) => control.TryFindResource(key, out var value) && value is ISolidColorBrush solid
                ? solid.Color : throw new InvalidOperationException("Missing solid scoped resource: " + key);
            Point At(HavenSceneControl scene, HavenRect bounds, double yFromBottom = 0) => scene.TranslatePoint(
                new Point(bounds.X + bounds.Width / 2, yFromBottom == 0 ? bounds.Y + bounds.Height / 2 : bounds.Bottom - yFromBottom), window)!.Value;
            HavenDrawingContext Draw(HavenSceneControl scene)
            { var drawing = new HavenDrawingContext(); ((IHavenDrawCommandSource)scene.Root!).Draw(drawing, 1); return drawing; }
            bool Near(Color first, Color second) => Math.Abs(first.R - second.R) <= 3 && Math.Abs(first.G - second.G) <= 3 && Math.Abs(first.B - second.B) <= 3;
            Color Over(Color foreground, Color background)
            {
                var alpha = foreground.A / 255d;
                return Color.FromRgb((byte)Math.Round(foreground.R * alpha + background.R * (1 - alpha)),
                    (byte)Math.Round(foreground.G * alpha + background.G * (1 - alpha)),
                    (byte)Math.Round(foreground.B * alpha + background.B * (1 - alpha)));
            }
            (Color Handle, Color Highlight) Capture(string name)
            {
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Missing native frame");
                frame.Save(Path.Combine(paths.DataDirectory, name + ".png"));
                using var pixels = new WriteableBitmap(frame.PixelSize, frame.Dpi, PixelFormat.Rgba8888, AlphaFormat.Unpremul);
                using var buffer = pixels.Lock(); frame.CopyPixels(buffer);
                Color Pixel(Point point)
                {
                    var x = Math.Clamp((int)Math.Floor(point.X * frame.PixelSize.Width / window.ClientSize.Width), 0, frame.PixelSize.Width - 1);
                    var y = Math.Clamp((int)Math.Floor(point.Y * frame.PixelSize.Height / window.ClientSize.Height), 0, frame.PixelSize.Height - 1);
                    var address = buffer.Address + y * buffer.RowBytes + x * 4;
                    return Color.FromArgb(Marshal.ReadByte(address, 3), Marshal.ReadByte(address), Marshal.ReadByte(address, 1), Marshal.ReadByte(address, 2));
                }
                var canvasDrawing = Draw(canvas); var thumbnailDrawing = Draw(thumbnails);
                var handle = canvasDrawing.Commands.OfType<HavenEllipseCommand>().First(command => command.Brush is HavenTokenBrush { Token: "Surface" });
                var highlight = thumbnailDrawing.Commands.OfType<HavenFillRoundedRectCommand>().Single(command => command.Brush is HavenTokenBrush { Token: "AccentSubtle" });
                var handlePoint = At(canvas, handle.Rect);
                var handlePixel = Pixel(handlePoint + new Vector(-1, -1));
                var highlightPoint = At(thumbnails, highlight.Rect, 3);
                var backgroundPoint = thumbnails.TranslatePoint(new Point(thumbnails.Bounds.Width - 1, highlight.Rect.Bottom - 3), window)!.Value;
                var highlightPixel = Pixel(highlightPoint);
                // Sample inside the top-left handle, outside the shape/selection border,
                // where a translucent CUI surface composites over the authored white paper.
                var expectedHandle = Over(Resource(canvas, "HavenPanelBrush"), Colors.White);
                var expectedHighlight = Over(Resource(thumbnails, "HavenAccentSoftBrush"), Pixel(backgroundPoint));
                Console.WriteLine($"SCOPED_PIXELS {name} handle={handlePixel}/{expectedHandle} highlight={highlightPixel}/{expectedHighlight}");
                Check(Near(handlePixel, expectedHandle), name + ": actual selection handle pixels use the canvas Object's scoped CUI surface");
                Check(Near(highlightPixel, expectedHighlight), name + ": actual thumbnail highlight pixels use the thumbnail Object's scoped CUI accent");
                foreach (var scene in new[] { canvas, thumbnails })
                {
                    // Authored commands are in slide coordinates inside the existing
                    // transform. Sample the real rendered paper at the owner's geometry.
                    var paper = Draw(scene).Commands.OfType<HavenFillRoundedRectCommand>()
                        .First(command => command.Brush is HavenSolidBrush).Rect;
                    var authored = new HavenRect(paper.X + shape.X * paper.Width, paper.Y + shape.Y * paper.Height,
                        shape.Width * paper.Width, shape.Height * paper.Height);
                    Check(Near(Pixel(At(scene, authored)), Color.Parse("#C93763")), name + ": authored shape fill pixels remain exact in " + (ReferenceEquals(scene, canvas) ? "canvas" : "thumbnail"));
                }
                return (handlePixel, highlightPixel);
            }
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Check(canvas.TryFindResource("CuiEffectiveThemeName", out var canvasTheme) && Equals(canvasTheme, "Retro")
                && thumbnails.TryFindResource("CuiEffectiveThemeName", out var thumbnailTheme) && Equals(thumbnailTheme, "Retro"),
                "canonical Present objects receive explicit Retro scopes independently of the surrounding user preference");
            var globalSurface = (ISolidColorBrush)Application.Current!.FindResource("HavenPanelBrush")!;
            Check(Resource(canvas, "HavenPanelBrush") != globalSurface.Color,
                "the test's explicit scoped palette differs from application fallback and exposes an application-first resolver");
            var first = Capture("scoped-theme-retro-bright");
            CuiThemeScopeApplier.ApplyThemeToControl(canvasOwner, CuiTheme.Playful, "Present", appearanceOverride: CuiAppearance.SuperDark);
            var mixed = Capture("scoped-theme-independent-canvas");
            Check(!Near(first.Handle, mixed.Handle) && Near(first.Highlight, mixed.Highlight),
                "changing the canvas Object scope preserves the independent thumbnail theme");
            CuiThemeScopeApplier.ApplyThemeToControl(thumbnailOwner, CuiTheme.Playful, "Present", appearanceOverride: CuiAppearance.SuperDark);
            var second = Capture("scoped-theme-playful-superdark");
            Check(!Near(first.Handle, second.Handle) && !Near(first.Highlight, second.Highlight),
                "live scoped resource changes repaint both original retained scenes without an editor refresh");
            Check(ReferenceEquals(globalSurface, Application.Current!.FindResource("HavenPanelBrush")),
                "scoped theme updates leave the existing application-wide resources intact");
            Check(ReferenceEquals(canvas, canvasOwner.GetVisualDescendants().OfType<HavenSceneControl>().Single())
                && ReferenceEquals(thumbnails, thumbnailOwner.GetVisualDescendants().OfType<HavenSceneControl>().Single())
                && session.Editor.Selection.ElementIds.SequenceEqual([shape.Id]),
                "scoped retheming preserves both native scene instances and the original editable selection");
            Check(JsonSerializer.Serialize(session.GetDocumentSnapshot()) == before && !session.IsDirty
                && JsonSerializer.Serialize(await repository.LoadAsync(document.Id, default)) == before,
                "scoped chrome changes preserve exact authored content, styles, IDs, repository and dirty state");
        }
        finally { window.Close(); }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
        return count;
    }
}
