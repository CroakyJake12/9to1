using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Infrastructure;
using Haven.UI;
using NineToOne.Web.Productivity.Present;

internal static class PresentShapeTextChecks
{
    internal static async Task<int> Run(IAppPaths paths)
    {
        var count = 0;
        void Check(bool pass, string message)
        { if (!pass) throw new InvalidOperationException(message); count++; Console.WriteLine("ASSERT " + message); }
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "operations-review-24.pptx");
        Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture))).Equals("F60D83C00879198927F9B9C2C691D5F06A2AD2338E8A4D5E15DA7976A8F8E824", StringComparison.Ordinal),
            "the native regression uses the exact independently authored 24-slide browser fixture");
        var document = await new PresentPptxImportService(paths).ImportAsync(fixture, default);
        var repository = new PresentRepository(paths);
        await repository.SaveAsync(document, "Shape text rendering regression", default);
        var stored = (await repository.LoadAsync(document.Id, default))!;
        var cards = stored.Slides[0].Elements.Where(element => element.Kind == PresentElementKind.Shape && element.ShapeType == "roundRect").ToArray();
        Check(stored.Slides.Count == 24 && cards.Length == 4 && stored.Slides.All(slide => slide.Elements.Count(element => element.Kind == PresentElementKind.Shape && !string.IsNullOrEmpty(element.Text)) == 4),
            "the actual importer and repository retain all 96 authored shape-text objects across 24 slides");
        Check(cards[0].Text == "Progress\nTeam 1 is tracking 30% completion.\nReview the current plan and confirm the next delivery."
            && cards[0].TextStyle.FontSizePoints == 19 && cards[0].TextStyle.Bold
            && cards[0].TextStyle.HorizontalAlignment == PresentTextHorizontalAlignment.Center && cards[0].TextStyle.VerticalAlignment == PresentTextVerticalAlignment.Middle,
            "the persisted rectangle owns the missing text and the existing imported uniform text style before rendering");
        File.WriteAllText(Path.Combine(paths.DataDirectory, "shape-text-persisted-owner.json"), JsonSerializer.Serialize(stored));
        await using var session = new PresentBrowserSession(repository);
        Check((await session.OpenAsync(stored.Id)).Succeeded, "the persisted larger presentation opens through the original browser session owner");
        var parser = new CuiRichParser(); var markup = parser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Present.cui")));
        var surface = PresentBrowserSurface.Create(markup, session, () => true);
        using var loader = new CuiControlLoader(surface.ControlRegistry!);
        loader.SetBindingContext(session); loader.SetActionDispatcher(session);
        var (root, diagnostics) = loader.TryLoad(surface.Document);
        Check(root is not null && !diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error), "shape-text regression hosts the canonical CUI editor");
        loader.WireBindings(root!);
        var window = new Window { Width = 1280, Height = 900, Content = root }; window.Show();
        try
        {
            var control = root!.GetVisualDescendants().OfType<PresentRetainedSceneControl>().Single();
            var scene = control.GetVisualDescendants().OfType<HavenSceneControl>().Single();
            var canvas = scene.Root!;
            var editor = session.Editor!;
            window.AddHandler(InputElement.PointerPressedEvent, (_, args) => Console.WriteLine($"NATIVE_PRESS timestamp={args.Timestamp} count={args.ClickCount} editing={control.IsTextEditing} point={args.GetPosition(window)}"), RoutingStrategies.Tunnel, handledEventsToo: true);
            var nativeWindow = window.PlatformImpl!;
            var nativeInput = nativeWindow.GetType().GetInterfaces().Single(type => type.FullName == "Avalonia.Headless.IHeadlessWindow");
            void NativeClick(Point point)
            {
                // Feed the same native driver without HeadlessWindowExtensions'
                // forced multi-frame drain between the two physical clicks.
                nativeInput.GetMethod("MouseDown")!.Invoke(nativeWindow, [point, MouseButton.Left, RawInputModifiers.None]);
                nativeInput.GetMethod("MouseUp")!.Invoke(nativeWindow, [point, MouseButton.Left, RawInputModifiers.None]);
            }
            var textModeTransitions = new List<bool>();
            control.InteractionChanged += (_, _) => textModeTransitions.Add(control.IsTextEditing);
            var shapeId = cards[0].Id;
            PresentElement Item() => editor.SelectedSlide.Elements.Single(element => element.Id == shapeId);
            HavenRect Paper()
            {
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); using var frame = window.CaptureRenderedFrame();
                return (HavenRect)canvas.GetType().GetProperty("PaperBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas)!;
            }
            HavenDrawingContext Draw(string? frameName = null)
            {
                _ = Paper();
                if (frameName is not null) { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(paths.DataDirectory, frameName + ".png")); }
                var drawing = new HavenDrawingContext(); ((IHavenDrawCommandSource)canvas).Draw(drawing, 1); return drawing;
            }
            Point At(double x, double y)
            { var paper = Paper(); return scene.TranslatePoint(new Point(paper.X + paper.Width * x, paper.Y + paper.Height * y), window)!.Value; }
            Point Body() { var item = Item(); return At(item.X + item.Width / 2, item.Y + item.Height / 2); }
            void Tap(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
            { var physical = key == Key.Enter ? PhysicalKey.Enter : Enum.Parse<PhysicalKey>(key.ToString()); window.KeyPress(key, modifiers, physical, key.ToString()); window.KeyRelease(key, modifiers, physical, key.ToString()); }
            var drawing = Draw("shape-text-imported");
            foreach (var card in cards)
            {
                var text = drawing.Commands.OfType<HavenTextCommand>().SingleOrDefault(command => command.Layout.Text == card.Text);
                Check(text is not null, "the actual shared canvas paints persisted card text: " + card.Text.Split('\n')[0]);
                Check(Math.Abs(text!.Layout.FontSize - card.TextStyle.FontSizePoints * 96 / 72) < .001 && text.Layout.FontWeight == 700
                    && drawing.Commands.OfType<HavenPushClipCommand>().Any(command => command.Bounds == text.Bounds),
                    "shape text retains authored logical font size and the same object clipping bounds");
            }
            var initial = Item(); var x = initial.X; var y = initial.Y; var width = initial.Width; var height = initial.Height;
            var originalText = initial.Text;
            var body = Body(); window.MouseDown(body, MouseButton.Left); window.MouseUp(body, MouseButton.Left);
            Check(editor.Selection.ElementIds.SequenceEqual([shapeId]) && !control.IsTextEditing, "single click selects the real text-bearing rectangle without entering text edit");
            await Task.Delay(600); NativeClick(body); NativeClick(body); _ = Paper();
            Check(control.IsTextEditing && textModeTransitions.LastOrDefault(), "a native double click enters the rectangle's original owner text editor");
            Tap(Key.A, RawInputModifiers.Control); window.KeyTextInput("Progress updated\nApproved delivery plan");
            Check(Item().Text == "Progress updated\nApproved delivery plan" && Item().X == x && Item().Y == y && Item().Width == width && Item().Height == height,
                "actual typing previews shape text without replacing the object or changing geometry");
            Draw("shape-text-live-edit"); Tap(Key.Escape);
            Check(!control.IsTextEditing && textModeTransitions.Count > 0 && !textModeTransitions[^1] && editor.Undo() && Item().Text == originalText && Item().Id == shapeId,
                "Escape commits all shape typing as one undo step with stable shape identity");
            Check(editor.Redo() && Item().Text == "Progress updated\nApproved delivery plan", "redo restores the complete shape text transaction");
            control.SetEditor(editor); _ = Paper(); scene.FocusElement(canvas); Tap(Key.Enter);
            Check(control.IsTextEditing, "Enter independently activates shape text editing");
            Tap(Key.End); window.KeyTextInput("."); Tap(Key.Escape);
            Check(Item().Text.EndsWith("."), "keyboard shape editing writes through the same model Text property");
            await Task.Delay(600);
            body = Body(); window.MouseDown(body, MouseButton.Left); window.MouseMove(body + new Vector(24, 14)); window.MouseUp(body + new Vector(24, 14), MouseButton.Left);
            Check(!control.IsTextEditing && Item().X > x && Item().Y > y && Item().Text.EndsWith("."),
                "body drag still moves the authored rectangle and its text together after editing");
            editor.Undo(); control.SetEditor(editor); _ = Paper();
            var east = At(Item().X + Item().Width, Item().Y + Item().Height / 2);
            window.MouseDown(east, MouseButton.Left); window.MouseMove(east + new Vector(22, 0)); window.MouseUp(east + new Vector(22, 0), MouseButton.Left);
            Check(Item().Width > width && Item().Text.EndsWith("."), "the real resize handle still resizes text-bearing shapes without losing text");
            editor.Undo(); editor.RotateSelection(20); control.SetEditor(editor);
            drawing = Draw("shape-text-rotated");
            var textIndex = drawing.Commands.ToList().FindIndex(command => command is HavenTextCommand text && text.Layout.Text == Item().Text);
            Check(drawing.Commands.Take(textIndex).OfType<HavenPushTransformCommand>().Last().Transform.RotationDegrees == 20,
                "shape text is painted under the exact same rotation transform as its geometry");
            editor.Undo(); editor.SetSelectedOpacity(.5); drawing = Draw();
            Check(drawing.Commands.OfType<HavenTextCommand>().Single(command => command.Layout.Text == Item().Text).Opacity == .5,
                "shape text respects the shape's actual owner opacity");
            editor.Undo();
            var firstSlide = editor.SelectedSlide.Id;
            foreach (var slide in editor.Document.Slides.Skip(1).ToArray())
            {
                editor.SelectSlide(slide.Id); control.SetEditor(editor); drawing = Draw();
                Check(slide.Elements.Where(element => element.Kind == PresentElementKind.Shape && !string.IsNullOrWhiteSpace(element.Text))
                    .All(element => drawing.Commands.OfType<HavenTextCommand>().Any(command => command.Layout.Text == element.Text)),
                    "switching to slide " + (slide.Order + 1) + " retains its actual shape text");
            }
            editor.SelectSlide(firstSlide); editor.SelectElements([shapeId]); control.SetEditor(editor);
            session.SetCanvasZoom(1); window.Width = 900; window.Height = 700; drawing = Draw("shape-text-manual-small");
            Check(drawing.Commands.OfType<HavenTextCommand>().Any(command => command.Layout.Text == Item().Text),
                "manual zoom and a smaller real viewport preserve authored shape text");
            await session.DispatchAsync("ZoomFit", null); window.Width = 1280; window.Height = 900;
            var empty = editor.AddShape(editor.Selection.SlideId); control.SetEditor(editor); _ = Paper(); scene.FocusElement(canvas); Tap(Key.Enter);
            Check(control.IsTextEditing, "an empty shape can enter the existing text engine without a text placeholder");
            window.KeyTextInput("New shape label"); Tap(Key.Escape);
            Check(empty.Text == "New shape label", "typing authors the first real label on an empty shape");
            var vector = editor.AddCustomShape(editor.Selection.SlideId, DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Ellipse));
            editor.SetElementText(editor.Selection.SlideId, vector.Id, "Vector label"); control.SetEditor(editor); drawing = Draw("shape-text-vector");
            Check(drawing.Commands.OfType<HavenTextCommand>().Any(command => command.Layout.Text == "Vector label"),
                "vector shapes share the same actual text renderer instead of losing their labels");
            var saved = await session.SaveAsync(); var snapshot = JsonSerializer.Serialize(session.GetDocumentSnapshot());
            var id = session.DocumentId!.Value; await session.CloseAsync(); var reopened = await session.OpenAsync(id);
            Check(saved.Succeeded && reopened.Succeeded && snapshot == JsonSerializer.Serialize(session.GetDocumentSnapshot()),
                "shape text, style, geometry and identities survive actual repository save/close/reopen");
            using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(paths.DataDirectory, "shape-text-reopened.png"));
        }
        catch (Exception error) { Console.WriteLine("SHAPE_TEXT_FAILURE " + error); throw; }
        finally { window.Close(); if (session.IsDirty) await session.SaveAsync(); }
        return count;
    }
}
