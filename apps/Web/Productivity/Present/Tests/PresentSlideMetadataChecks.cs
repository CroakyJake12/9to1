using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
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
using NineToOne.Web.Productivity.Present;

internal static class PresentSlideMetadataChecks
{
    internal static async Task<int> Run(IAppPaths paths)
    {
        var count = 0;
        void Check(bool pass, string message)
        { if (!pass) throw new InvalidOperationException(message); count++; Console.WriteLine("ASSERT " + message); }
        var document = PresentDocument.Create("Independent slide names");
        var slide = document.Slides[0];
        slide.Title = "Review notes";
        var title = new PresentElement
        {
            Kind = PresentElementKind.Text, Role = PresentElementRoles.Title, Text = "Authored heading",
            X = .12, Y = .30, Width = .65, Height = .15,
            TextStyle = new PresentTextStyle { FontSizePoints = 24, Bold = true, Color = "#17344D" }
        };
        slide.Elements.Add(title);
        var editor = new PresentEditor(document);
        var slideId = slide.Id; var titleId = title.Id;
        PresentElement Title() => editor.SelectedSlide.Elements.Single(element => element.Id == titleId);
        Check(editor.SelectedSlide.Title == "Review notes" && Title().Text == "Authored heading",
            "normalization preserves an existing explicit slide name independently of authored title text");
        var titleBefore = JsonSerializer.Serialize(Title());
        Check(editor.SetSlideTitle(slideId, "Board review") && JsonSerializer.Serialize(Title()) == titleBefore,
            "rename metadata never changes authored title ID, content, geometry or formatting");
        Check(editor.Undo() && editor.SelectedSlide.Title == "Review notes" && JsonSerializer.Serialize(Title()) == titleBefore
            && editor.Redo() && editor.SelectedSlide.Title == "Board review", "rename has one reversible owner transaction");
        Check(editor.PreviewSlideTitle(slideId, "Temporary name") && Title().Text == "Authored heading"
            && editor.CancelLiveTextEdit() && editor.SelectedSlide.Title == "Board review", "name preview cancellation restores metadata without changing slide content");
        editor.PreviewElementText(slideId, titleId, "Edited heading");
        Check(editor.SelectedSlide.Title == "Board review" && editor.CommitLiveTextEdit() && editor.Undo()
            && Title().Text == "Authored heading" && editor.SelectedSlide.Title == "Board review",
            "authored title preview and undo never overwrite the explicit navigation name");
        var bodyId = editor.SelectedSlide.GetOrCreateBodyText().Id;
        editor.SetElementText(slideId, bodyId, "Ordinary body");
        Check(editor.SelectedSlide.Title == "Board review" && Title().Text == "Authored heading", "body editing leaves slide metadata and other authored content unchanged");
        editor.SelectElements([titleId]);
        Check(editor.MoveSelection(.04, .03) && Math.Abs(Title().X - .16) < .00001
            && editor.SelectedSlide.Title == "Board review", "real title object remains movable through the original transform owner");
        editor.Undo();
        var duplicate = editor.DuplicateSlide(slideId);
        Check(duplicate.Title == "Board review copy" && duplicate.GetTitleText() is { } copied
            && copied.Id != titleId && copied.Text == "Authored heading" && Math.Abs(copied.X - .12) < .00001,
            "duplicating a slide changes only the copied slide name and object identities, not authored title content");
        editor.Undo(); editor.SelectSlide(slideId); editor.SelectElements([titleId]);
        Check(editor.RemoveSelectedElements() && editor.SelectedSlide.GetTitleText() is null && editor.SelectedSlide.Title == "Board review",
            "deleting authored title content preserves the slide name without creating a replacement object");
        Check(editor.Undo() && Title().Id == titleId && Title().Text == "Authored heading" && editor.SelectedSlide.Title == "Board review",
            "undo title deletion restores the original object and independent metadata");
        editor.SelectElements([titleId, bodyId]); var group = editor.GroupSelection();
        Check(group is not null && editor.RemoveSelectedElements() && editor.SelectedSlide.GetTitleText() is null
            && editor.SelectedSlide.Title == "Board review", "deleting a group containing the title leaves the navigation name intact");
        editor.Undo(); editor.Undo();

        var repository = new PresentRepository(paths);
        var exporter = new PresentPptxExportService(); var importer = new PresentPptxImportService(paths);
        await repository.SaveAsync(editor.Document, "Independent name fixture", default);
        var saved = await repository.LoadAsync(document.Id, default) ?? throw new InvalidOperationException("Saved metadata fixture disappeared");
        Check(saved.Slides[0].Id == slideId && saved.Slides[0].Title == "Board review"
            && saved.Slides[0].GetTitleText()!.Id == titleId && saved.Slides[0].GetTitleText()!.Text == "Authored heading",
            "real repository reopen preserves name, authored title and both stable owner identities");
        var pptx = Path.Combine(paths.DataDirectory, "metadata-with-title.pptx");
        await exporter.ExportAsync(saved, pptx, default);
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main", a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        using (var archive = ZipFile.OpenRead(pptx))
        {
            using var input = archive.GetEntry("ppt/slides/slide1.xml")!.Open(); var xml = XDocument.Load(input);
            Check((string?)xml.Root!.Element(p + "cSld")!.Attribute("name") == "Board review"
                && xml.Descendants(a + "t").Count(text => text.Value == "Authored heading") == 1
                && !xml.Descendants(a + "t").Any(text => text.Value == "Board review"),
                "PPTX exports the navigation name as cSld metadata and authored title as content exactly once");
        }
        var imported = await importer.ImportAsync(pptx, default);
        Check(imported.Slides[0].Title == "Board review" && imported.Slides[0].GetTitleText()!.Text == "Authored heading",
            "external PPTX import honors explicit slide-name metadata independently from a real title placeholder");
        var emptyName = new PresentEditor(saved); emptyName.SetSlideTitle(slideId, string.Empty);
        var unnamedPath = Path.Combine(paths.DataDirectory, "metadata-empty-name.pptx");
        await exporter.ExportAsync(emptyName.Document, unnamedPath, default);
        var unnamed = await importer.ImportAsync(unnamedPath, default);
        Check(unnamed.Slides[0].Title == string.Empty && unnamed.Slides[0].GetTitleText()!.Text == "Authored heading",
            "an explicitly empty exported name is not overwritten by imported title text");

        var metadataOnly = PresentDocument.Create("Metadata only"); metadataOnly.Slides[0].Title = "Sidebar name";
        var metadataPath = Path.Combine(paths.DataDirectory, "metadata-only.pptx");
        await exporter.ExportAsync(metadataOnly, metadataPath, default);
        using (var archive = ZipFile.OpenRead(metadataPath))
        {
            using var input = archive.GetEntry("ppt/slides/slide1.xml")!.Open(); var xml = XDocument.Load(input);
            Check((string?)xml.Root!.Element(p + "cSld")!.Attribute("name") == "Sidebar name"
                && !xml.Descendants(p + "sp").Any() && !xml.Descendants(a + "t").Any(),
                "metadata-only slide exports zero invented content shapes or text");
        }
        var metadataImported = await importer.ImportAsync(metadataPath, default);
        Check(metadataImported.Slides[0].Title == "Sidebar name" && metadataImported.Slides[0].GetTitleText() is null,
            "metadata-only PPTX roundtrip preserves its name without inventing a title object");
        await repository.SaveAsync(metadataOnly, "Metadata-only native fixture", default);
        await RenderAndEdit(repository, metadataOnly.Id, document.Id, paths, Check);
        return count;
    }

    private static async Task RenderAndEdit(PresentRepository repository, Guid metadataId, Guid authoredId, IAppPaths paths, Action<bool, string> check)
    {
        await using var session = new PresentBrowserSession(repository);
        check((await session.OpenAsync(metadataId)).Succeeded, "metadata-only document opens in the real Present session");
        var markup = new CuiRichParser().Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Present.cui")));
        var surface = PresentBrowserSurface.Create(markup, session, () => true);
        using var loader = new CuiControlLoader(surface.ControlRegistry!);
        loader.SetBindingContext(session); loader.SetActionDispatcher(session);
        var (root, diagnostics) = loader.TryLoad(surface.Document);
        check(root is not null && !diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error), "metadata test uses the canonical CUI and original retained editor");
        loader.WireBindings(root!);
        var window = new Window { Width = 1200, Height = 900, Content = root }; window.Show();
        try
        {
            void Flush() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); using var frame = window.CaptureRenderedFrame(); }
            PresentRetainedSceneControl Control() => root!.GetVisualDescendants().OfType<PresentRetainedSceneControl>().Single();
            HavenSceneControl Scene() => Control().GetVisualDescendants().OfType<HavenSceneControl>().Single();
            HavenTextCommand[] TextCommands()
            { var draw = new HavenDrawingContext(); ((IHavenDrawCommandSource)Scene().Root!).Draw(draw, 1); return draw.Commands.OfType<HavenTextCommand>().ToArray(); }
            HavenRect SlideRect() => (HavenRect)Scene().Root!.GetType().GetMethod("SlideRectLocal", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Scene().Root, null)!;
            Point PointAt(double x, double y) { var rect = SlideRect(); return Scene().TranslatePoint(new Point(rect.X + rect.Width * x, rect.Y + rect.Height * y), window)!.Value; }
            void Tap(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
            { var physical = key == Key.Enter ? PhysicalKey.Enter : Enum.Parse<PhysicalKey>(key.ToString()); window.KeyPress(key, modifiers, physical, key.ToString()); window.KeyRelease(key, modifiers, physical, key.ToString()); Flush(); }
            void Frame(string name) { Flush(); using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(paths.DataDirectory, name + ".png")); }
            Flush();
            check(!TextCommands().Any(), "actual metadata-only canvas renders no synthetic name text");
            var rail = root!.GetVisualDescendants().OfType<HavenSceneControl>().Single(scene => scene.Root?.GetType().Name == "PresentThumbnailNavigator");
            var railDraw = new HavenDrawingContext(); ((IHavenDrawCommandSource)rail.Root!).Draw(railDraw, 1);
            check(railDraw.Commands.OfType<HavenTextCommand>().Any(command => command.Layout.Text == "Sidebar name"), "slide name remains visible in the original thumbnail label");
            Frame("metadata-only-no-canvas-title");
            var before = JsonSerializer.Serialize(session.GetDocumentSnapshot());
            var blank = PointAt(.5, .1);
            window.MouseDown(blank, MouseButton.Left); window.MouseUp(blank, MouseButton.Left);
            window.MouseDown(blank, MouseButton.Left); window.MouseUp(blank, MouseButton.Left);
            Tap(Key.Enter); window.KeyTextInput("Must not rename"); Flush();
            check(!Control().IsTextEditing && !session.Editor!.IsLiveTextEditActive && JsonSerializer.Serialize(session.GetDocumentSnapshot()) == before,
                "physical double click, Enter and typing in the old synthetic title area cannot edit metadata or add content");
            check((await session.CloseAsync()).Succeeded && (await session.OpenAsync(authoredId)).Succeeded,
                "same original session switches from metadata-only to authored-title document");
            Flush();
            var title = session.Editor!.SelectedSlide.GetTitleText()!; var id = title.Id; var name = session.Editor.SelectedSlide.Title;
            check(TextCommands().Count(command => command.Layout.Text == "Authored heading") == 1
                && !TextCommands().Any(command => command.Layout.Text == name), "actual authored title draws once at its object bounds while navigation name is absent from the canvas");
            var point = PointAt(title.X + title.Width / 2, title.Y + title.Height / 2);
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Flush();
            check(session.Editor.Selection.ElementIds.Contains(id), "physical pointer selects the real authored title object");
            Tap(Key.Enter); Tap(Key.A, RawInputModifiers.Control);
            foreach (var character in "Physical authored heading") { window.KeyTextInput(character.ToString()); Flush(); }
            Tap(Key.Escape);
            check(session.Editor.SelectedSlide.GetTitleText()!.Text == "Physical authored heading" && session.Editor.SelectedSlide.Title == name
                && !session.Editor.IsLiveTextEditActive, "physical title typing commits through the original text editor without renaming the slide");
            check(session.Editor.Undo() && session.Editor.SelectedSlide.GetTitleText()!.Text == "Authored heading" && session.Editor.SelectedSlide.Title == name,
                "one owner undo restores the physically edited title and preserves independent metadata");
            session.Editor.Redo(); session.RefreshSelection(); Frame("metadata-and-authored-title-edited");
            check((await session.SaveAsync()).Succeeded && (await session.CloseAsync()).Succeeded && (await session.OpenAsync(authoredId)).Succeeded
                && session.Editor!.SelectedSlide.GetTitleText()!.Id == id && session.Editor.SelectedSlide.GetTitleText()!.Text == "Physical authored heading"
                && session.Editor.SelectedSlide.Title == name, "real session save and reopen preserve the edited authored object and independent navigation name");
            window.Width = 800; window.Height = 700;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                Flush();
                if (Scene().Bounds.Width <= 584 && SlideRect().Right <= Scene().Bounds.Width
                    && SlideRect().Bottom <= Scene().Bounds.Height) break;
                await Task.Delay(25);
            }
            var paper = SlideRect();
            Console.WriteLine($"METADATA_RESIZE window={window.ClientSize} scene={Scene().Bounds} control={Control().Bounds} canvas={Scene().Root!.Bounds} paper={paper} fit={session.IsFitToWindow} zoom={session.CanvasZoom} effective={Control().EffectiveZoom}");
            foreach (var ancestor in Scene().GetVisualAncestors().OfType<Control>())
                Console.WriteLine($"METADATA_ANCESTOR type={ancestor.GetType().Name} bounds={ancestor.Bounds} width={ancestor.Width} minWidth={ancestor.MinWidth} horizontal={ancestor.HorizontalAlignment}");
            Frame("metadata-and-authored-title-resize-observed");
            check(Scene().Bounds.Width <= 584 && paper.Right <= Scene().Bounds.Width && paper.Bottom <= Scene().Bounds.Height
                && Math.Abs(paper.Width / paper.Height - 16d / 9) < .0001,
                "reopened authored slide fits the settled compact native stage at its original aspect ratio");
            Frame("metadata-and-authored-title-reopened-800");
        }
        finally { window.Close(); }
    }
}
