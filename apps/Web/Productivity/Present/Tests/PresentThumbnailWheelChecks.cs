using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Infrastructure;
using Haven.UI;
using NineToOne.Web;
using NineToOne.Web.Productivity.Present;

internal static class PresentThumbnailWheelChecks
{
    internal static async Task<int> Run(IAppPaths paths)
    {
        var count = 0; var failures = new List<string>();
        void Check(bool value, string description)
        {
            Console.WriteLine((value ? "ASSERT " : "FAIL ") + description);
            if (value) count++; else failures.Add(description);
        }
        var document = await new PresentPptxImportService(paths).ImportAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "operations-review-24.pptx"), default);
        var repository = new PresentRepository(paths);
        await repository.SaveAsync(document, "Native thumbnail wheel regression", default);
        await using var session = new PresentBrowserSession(repository);
        Check((await session.OpenAsync(document.Id)).Succeeded, "real 24-slide import opens through original repository/session");
        var surface = PresentBrowserSurface.Create(new CuiRichParser().Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Present.cui"))), session, () => true);
        using var loader = new CuiControlLoader(surface.ControlRegistry!);
        loader.SetBindingContext(session); loader.SetActionDispatcher(session);
        var (root, diagnostics) = loader.TryLoad(surface.Document);
        if (root is null || diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error)) throw new InvalidOperationException("Canonical Present CUI failed to load");
        loader.WireBindings(root);
        var window = new Window { Width = 1280, Height = 900, Content = root }; window.Show();
        try
        {
            var thumbnails = root.GetVisualDescendants().OfType<PresentThumbnailControl>().Single();
            var scene = thumbnails.GetVisualDescendants().OfType<HavenSceneControl>().Single();
            var navigator = scene.Root!;
            var accessibility = new BrowserAccessibilityBridge(); accessibility.Bind(root);
            var before = JsonSerializer.Serialize(session.GetDocumentSnapshot());
            var initialId = session.Editor!.Selection.SlideId;
            double Offset() => (double)navigator.GetType().GetProperty("ScrollOffset")!.GetValue(navigator)!;
            void Flush() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); using var frame = window.CaptureRenderedFrame(); }
            Point WheelPoint(bool gutter = false) => scene.TranslatePoint(new Point(gutter ? scene.Bounds.Width - 4 : scene.Bounds.Width / 2, Math.Min(350, scene.Bounds.Height / 2)), window)!.Value;
            string[] VisibleSlides()
            {
                using var snapshot = JsonDocument.Parse(accessibility.ReadSnapshot());
                return snapshot.RootElement.GetProperty("elements").EnumerateArray()
                    .Where(item => item.GetProperty("automationId").GetString()?.StartsWith("Present.Rail.", StringComparison.Ordinal) == true)
                    .Select(item => item.GetProperty("name").GetString()!).ToArray();
            }
            void Frame(string name)
            {
                Flush(); using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(paths.DataDirectory, name + ".png"));
                File.WriteAllText(Path.Combine(paths.DataDirectory, name + ".json"), accessibility.ReadSnapshot());
                var wrapper = scene.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
                Console.WriteLine($"THUMBNAIL {name} offset={Offset()} scene={scene.Bounds} window={window.ClientSize} wrapperViewport={wrapper?.Viewport} wrapperExtent={wrapper?.Extent}");
            }
            void Wheel(double y, bool gutter = false) { var point = WheelPoint(gutter); Console.WriteLine($"NATIVE_WHEEL delta={y} gutter={gutter} point={point} hit={window.InputHitTest(point)?.GetType().FullName}"); window.MouseWheel(point, new Vector(0, y)); Flush(); }
            void Tap(Key key) { window.KeyPress(key, RawInputModifiers.None, Enum.Parse<PhysicalKey>(key.ToString()), key.ToString()); window.KeyRelease(key, RawInputModifiers.None, Enum.Parse<PhysicalKey>(key.ToString()), key.ToString()); Flush(); }
            bool LastLabelInsideViewport()
            {
                var drawing = new HavenDrawingContext(); ((IHavenDrawCommandSource)navigator).Draw(drawing, 1);
                var label = drawing.Commands.OfType<HavenTextCommand>().FirstOrDefault(command => command.Layout.Text == "24" && command.Layout.FontSize == 11);
                if (label is null) return false;
                var bottom = scene.TranslatePoint(new Point(label.Bounds.Right, label.Bounds.Bottom), window)!.Value;
                var wrapper = scene.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
                var end = wrapper is null ? scene.TranslatePoint(new Point(scene.Bounds.Width, scene.Bounds.Height), window)!.Value
                    : wrapper.TranslatePoint(new Point(wrapper.Viewport.Width, wrapper.Viewport.Height), window)!.Value;
                return bottom.Y <= end.Y + .1 && bottom.Y <= window.ClientSize.Height - 40;
            }
            Frame("thumbnail-initial");
            Check(VisibleSlides().Any(name => name.StartsWith("Slide 1:", StringComparison.Ordinal)), "actual initial thumbnail accessibility describes slide one");
            Wheel(-1); var step = Offset();
            Check(step > 0 && step < scene.Bounds.Height / 2, "physical downward wheel scrolls thumbnail content down by a normal step, not reversed or multiplied twice");
            Wheel(1);
            Check(Math.Abs(Offset()) < .01, "physical upward wheel returns the same thumbnail viewport to its initial offset");
            Wheel(-16); Frame("thumbnail-middle");
            var browsedOffset = Offset(); var browsedSlides = VisibleSlides();
            Check(browsedOffset > 0 && browsedSlides.All(name => !name.StartsWith("Slide 1:", StringComparison.Ordinal)), "physical wheel exposes later actual slide thumbnails and updates visible automation children");
            session.RefreshSelection(); Flush();
            Check(browsedOffset > 0 && Math.Abs(Offset() - browsedOffset) < .01 && VisibleSlides().SequenceEqual(browsedSlides), "unchanged selection notification preserves the user's browsed thumbnail position");
            await session.DispatchAsync("ToggleNotes", null); Flush();
            Check(browsedOffset > 0 && Math.Abs(Offset() - browsedOffset) < .01, "notes panel notification does not snap the thumbnail rail back to the selected first slide");
            Check(session.Editor.Selection.SlideId == initialId && JsonSerializer.Serialize(session.GetDocumentSnapshot()) == before && !session.IsDirty,
                "wheel browsing changes neither selection nor saved owner IDs/content or dirty state");
            var gutterHit = window.InputHitTest(WheelPoint(gutter: true)) as Visual;
            Check(gutterHit is not null && (ReferenceEquals(gutterHit, scene) || gutterHit.GetVisualAncestors().Contains(scene)),
                "the unpainted rail gutter belongs to the original native thumbnail input host");
            scene.FocusElement(navigator); Tap(Key.Home);
            Check(Offset() < 10 && session.Editor.Selection.SlideId == initialId, "Home reveals the already selected first slide after independent wheel browsing");
            Wheel(-100, gutter: true); Frame("thumbnail-end");
            Check(VisibleSlides().Any(name => name.StartsWith("Slide 24:", StringComparison.Ordinal)) && LastLabelInsideViewport(), "physical wheel in the rail gutter reaches the last genuine thumbnail and full label");
            using (var snapshot = JsonDocument.Parse(accessibility.ReadSnapshot()))
            {
                var item = snapshot.RootElement.GetProperty("elements").EnumerateArray().Single(element => element.GetProperty("name").GetString()?.StartsWith("Slide 24:", StringComparison.Ordinal) == true);
                var bounds = item.GetProperty("bounds");
                var point = new Point(bounds.GetProperty("x").GetDouble() + bounds.GetProperty("width").GetDouble() / 2,
                    bounds.GetProperty("y").GetDouble() + bounds.GetProperty("height").GetDouble() / 2);
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Flush();
            }
            Check(session.Editor.Selection.SlideId == session.Editor.Document.Slides[^1].Id && !session.IsDirty,
                "physical click on the wheel-revealed last thumbnail selects its original slide ID without mutation");
            var maximum = Offset(); Wheel(-10, gutter: true);
            Check(Offset() == maximum, "wheel at the end clamps without moving the document or overflowing the viewport");
            Wheel(2); Check(Offset() < maximum, "upward wheel at the end immediately browses earlier slides");
            scene.FocusElement(navigator); Tap(Key.End);
            Check(session.Editor.Selection.SlideId == session.Editor.Document.Slides[^1].Id && LastLabelInsideViewport(), "physical End selects the actual last slide and scrolls its complete thumbnail into view");
            window.Width = 800; window.Height = 700; Frame("thumbnail-selected-end-small");
            Check(LastLabelInsideViewport(), "resizing a selected last slide keeps its whole thumbnail label within the shorter actual viewport");
            window.Width = 1280; window.Height = 900; Frame("thumbnail-selected-end-wide");
            Check(LastLabelInsideViewport(), "widening and growing the thumbnail viewport retains visible selected slide content");
            scene.FocusElement(navigator); Tap(Key.Home);
            Check(session.Editor.Selection.SlideId == initialId && Offset() < 10, "physical Home returns selection and visible rail to the initial stable slide ID");
            await session.CloseAsync(); await session.OpenAsync(document.Id, readOnly: true); Flush();
            Wheel(-4);
            Check(session.IsReadOnly && Offset() > 0 && !session.IsDirty, "read-only decks keep wheel browsing available without admitting document edits");
            Check(JsonSerializer.Serialize(session.GetDocumentSnapshot()) == before && JsonSerializer.Serialize(await repository.LoadAsync(document.Id, default)) == before,
                "all wheel, keyboard and resize workflows preserve the exact original persisted document");
        }
        finally { window.Close(); }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
        return count;
    }
}
