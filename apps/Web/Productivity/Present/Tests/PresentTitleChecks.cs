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

internal static class PresentTitleChecks
{
    internal static async Task<int> Run(IAppPaths paths)
    {
        var count = 0;
        void Check(bool pass, string message)
        { if (!pass) throw new InvalidOperationException(message); count++; Console.WriteLine("ASSERT " + message); }
        static bool Near(double a, double b) => Math.Abs(a - b) < .00001;
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main", a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "external-styled-notes.pptx");
        var importer = new PresentPptxImportService(paths);
        var exporter = new PresentPptxExportService();
        var document = await importer.ImportAsync(fixture, default);
        var editor = new PresentEditor(document);
        var slideId = document.Slides[0].Id;
        var titleId = document.Slides[0].GetTitleText()!.Id;
        PresentElement Title() => editor.Document.Slides[0].GetTitleText()!;
        var importedTitle = Title();
        Check(importedTitle.Id != Guid.Empty && importedTitle.Kind == PresentElementKind.Text && importedTitle.Text == "Quarterly roadmap",
            "independent PPTX title is an original editable text object with a stable owner ID");
        Check(Near(importedTitle.X * document.SlideSize.WidthInches * 914400, 457200)
            && Near(importedTitle.Y * document.SlideSize.HeightInches * 914400, 274638)
            && Near(importedTitle.Width * document.SlideSize.WidthInches * 914400, 8229600)
            && Near(importedTitle.Height * document.SlideSize.HeightInches * 914400, 1143000),
            "title geometry inherits from its actual slide master through the layout");
        Check(importedTitle.TextStyle.FontSizePoints == 44 && importedTitle.TextStyle.FontFamily == "Calibri"
            && importedTitle.TextStyle.Color == "#000000" && importedTitle.TextStyle.HorizontalAlignment == PresentTextHorizontalAlignment.Center
            && importedTitle.TextStyle.VerticalAlignment == PresentTextVerticalAlignment.Middle,
            "title inherits master font size, theme font/colour and both alignments");
        Check(document.Slides[0].GetOrCreateBodyText().Id != titleId && document.Slides[0].GetOrCreateBodyText().Text.Contains("Plan together."),
            "imported title never consumes the original body role");
        Title().Locked = true;
        var lockedTitleState = JsonSerializer.Serialize(editor.Document);
        Check(!editor.SetSlideTitle(slideId, "Locked metadata edit") && !editor.PreviewSlideTitle(slideId, "Locked metadata preview")
            && !editor.SetElementText(slideId, titleId, "Locked direct edit") && !editor.PreviewElementText(slideId, titleId, "Locked direct preview")
            && JsonSerializer.Serialize(editor.Document) == lockedTitleState && !editor.IsLiveTextEditActive,
            "locked imported title rejects metadata and direct text edits without a history or preview mutation");
        Title().Locked = false;
        Check(editor.SetSlideTitle(slideId, "Metadata edit") && Title().Text == "Metadata edit" && Title().Id == titleId,
            "slide-title owner updates the same editable title object");
        Check(editor.Undo() && Title().Text == "Quarterly roadmap" && editor.Document.Slides[0].Title == "Quarterly roadmap",
            "one undo restores both title text and metadata");
        editor.PreviewElementText(slideId, titleId, "Preview title");
        Check(editor.Document.Slides[0].Title == "Preview title" && editor.CancelLiveTextEdit() && Title().Text == "Quarterly roadmap",
            "live title-object preview synchronizes metadata and cancellation restores original content");
        editor.PreviewElementText(slideId, titleId, "One transaction"); editor.PreviewElementText(slideId, titleId, "One transaction complete");
        Check(editor.CommitLiveTextEdit() && editor.Undo() && Title().Text == "Quarterly roadmap", "several live title edits commit as one original undo transaction");
        Check(editor.Redo() && editor.Document.Slides[0].Title == "One transaction complete" && Title().Id == titleId, "redo retains title identity and synchronized metadata");
        editor.PreviewSlideTitle(slideId, "Metadata live preview");
        Check(Title().Text == "Metadata live preview" && editor.CancelLiveTextEdit(), "legacy title preview updates and cancels the real title object");
        editor.SelectElements([titleId]);
        var clipboard = editor.CopySelection(); var pasted = editor.Paste(clipboard);
        Check(pasted.Count == 1 && editor.SelectedElements.Single().Role != PresentElementRoles.Title
            && editor.SelectedSlide.GetTitleText()!.Id == titleId, "copying a title duplicates its text and geometry without stealing slide metadata ownership");
        editor.Undo();
        editor.SelectElements([titleId]); editor.RemoveSelectedElements();
        Check(editor.SelectedSlide.GetTitleText() is null && editor.SelectedSlide.Title == string.Empty,
            "deleting the title object does not resurrect its text as a synthetic title");
        Check(editor.Undo() && Title().Id == titleId && editor.SelectedSlide.Title == Title().Text, "undo title deletion restores the original title ID");
        var duplicate = editor.DuplicateSlide(slideId);
        Check(duplicate.GetTitleText() is { } copied && copied.Id != titleId && copied.Text == duplicate.Title && Near(copied.X, Title().X),
            "duplicating a slide remaps the title ID while preserving geometry and synchronized copy title");
        editor.Undo(); editor.SelectSlide(slideId);
        editor.SetElementText(slideId, titleId, "Saved title object");

        var repository = new PresentRepository(paths);
        await repository.SaveAsync(editor.Document, "Imported title retention", default);
        var saved = await repository.LoadAsync(document.Id, default) ?? throw new InvalidOperationException("The saved title document disappeared.");
        Check(saved.Slides[0].GetTitleText()!.Id == titleId && saved.Slides[0].Title == "Saved title object"
            && Near(saved.Slides[0].GetTitleText()!.Width, importedTitle.Width), "real repository save and reopen preserve title ID, content and imported geometry");
        var nativePackage = Path.Combine(paths.DataDirectory, "title-native.9to1p");
        await exporter.ExportAsync(saved, nativePackage, default);
        var nativeImported = await importer.ImportAsync(nativePackage, default);
        Check(nativeImported.Id != saved.Id && nativeImported.Slides[0].GetTitleText()!.Id == titleId
            && nativeImported.Slides[0].Title == "Saved title object" && nativeImported.Slides[0].GetTitleText()!.TextStyle.FontSizePoints == 44,
            "native package import creates an independent document while preserving authored title object identity and style");
        var destination = Path.Combine(paths.DataDirectory, "title-roundtrip.pptx");
        await exporter.ExportAsync(saved, destination, default);
        using (var archive = ZipFile.OpenRead(destination))
        {
            using var input = archive.GetEntry("ppt/slides/slide1.xml")!.Open();
            var xml = XDocument.Load(input);
            Check(xml.Descendants(a + "t").Count(text => text.Value == "Saved title object") == 1
                && xml.Descendants(p + "ph").Count(ph => (string?)ph.Attribute("type") == "title") == 1,
                "native PPTX export emits exactly one title text object and title placeholder");
        }
        var reimported = await importer.ImportAsync(destination, default);
        var roundTripTitle = reimported.Slides[0].GetTitleText()!;
        Check(roundTripTitle.Text == saved.Slides[0].Title && Near(roundTripTitle.X, Title().X) && Near(roundTripTitle.Height, Title().Height)
            && roundTripTitle.TextStyle.FontSizePoints == 44 && roundTripTitle.TextStyle.FontFamily == "Calibri",
            "PPTX export and reimport retain editable title geometry and explicit inherited formatting");
        var emptyOwner = new PresentEditor(reimported);
        emptyOwner.SetElementText(reimported.Slides[0].Id, roundTripTitle.Id, string.Empty);
        var emptyPath = Path.Combine(paths.DataDirectory, "empty-title.pptx");
        await exporter.ExportAsync(emptyOwner.Document, emptyPath, default);
        var emptyImported = await importer.ImportAsync(emptyPath, default);
        Check(emptyImported.Slides[0].GetTitleText() is { Text.Length: 0 } emptyTitle
            && emptyImported.Slides[0].Title.Length == 0 && Near(emptyTitle.Height, importedTitle.Height),
            "an intentionally empty title retains its authored box through export instead of becoming an invented default title");
        emptyOwner.SetElementText(reimported.Slides[0].Id, roundTripTitle.Id, "Hidden title retained");
        emptyOwner.SelectedSlide.GetTitleText()!.Visible = false;
        var hiddenPath = Path.Combine(paths.DataDirectory, "hidden-title.pptx");
        await exporter.ExportAsync(emptyOwner.Document, hiddenPath, default);
        using (var archive = ZipFile.OpenRead(hiddenPath))
        {
            using var input = archive.GetEntry("ppt/slides/slide1.xml")!.Open(); var xml = XDocument.Load(input);
            var shape = xml.Descendants(p + "sp").Single(item => item.Descendants(p + "ph").Any(ph => (string?)ph.Attribute("type") == "title"));
            Check((string?)shape.Element(p + "nvSpPr")!.Element(p + "cNvPr")!.Attribute("hidden") == "1",
                "a hidden title exports as its real OOXML hidden object, not missing content");
        }
        var hiddenImported = await importer.ImportAsync(hiddenPath, default);
        Check(hiddenImported.Slides[0].GetTitleText() is { Visible: false, Text: "Hidden title retained" }
            && hiddenImported.Slides[0].Title == "Hidden title retained", "hidden-title roundtrip preserves its role and visibility without inventing a visible replacement");

        var noTitlePath = Path.Combine(paths.DataDirectory, "no-title-placeholder.pptx");
        File.Copy(fixture, noTitlePath);
        using (var archive = ZipFile.Open(noTitlePath, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("ppt/slides/slide1.xml")!; XDocument xml;
            using (var input = entry.Open()) xml = XDocument.Load(input);
            // Even a normal textbox named "Title 1" must remain ordinary text.
            var ordinary = xml.Descendants(p + "sp").First();
            ordinary.Element(p + "nvSpPr")!.Element(p + "nvPr")!.Element(p + "ph")!.Remove();
            ordinary.Element(p + "spPr")!.Add(new XElement(a + "xfrm",
                new XElement(a + "off", new XAttribute("x", 457200), new XAttribute("y", 274638)),
                new XElement(a + "ext", new XAttribute("cx", 8229600), new XAttribute("cy", 1143000))));
            entry.Delete(); using var output = archive.CreateEntry("ppt/slides/slide1.xml").Open(); xml.Save(output);
        }
        var noTitleImported = await importer.ImportAsync(noTitlePath, default);
        Check(noTitleImported.Slides[0].GetTitleText() is null && noTitleImported.Slides[0].Title.Length == 0
            && noTitleImported.Slides[0].Elements.Count(item => item.Text == "Quarterly roadmap") == 1,
            "a slide without an actual title placeholder preserves its ordinary textbox and does not invent Slide 1 content");
        var noTitleExport = Path.Combine(paths.DataDirectory, "no-title-roundtrip.pptx");
        await exporter.ExportAsync(noTitleImported, noTitleExport, default);
        var noTitleRoundTrip = await importer.ImportAsync(noTitleExport, default);
        Check(noTitleRoundTrip.Slides[0].GetTitleText() is null && noTitleRoundTrip.Slides[0].Title.Length == 0
            && noTitleRoundTrip.Slides[0].Elements.Count(item => item.Text == "Quarterly roadmap") == 1,
            "no-title PPTX roundtrip preserves actual text without creating any title placeholder");

        // Direct slide properties must override inherited placeholder properties.
        var overridePath = Path.Combine(paths.DataDirectory, "title-override.pptx");
        File.Copy(fixture, overridePath);
        using (var archive = ZipFile.Open(overridePath, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("ppt/slides/slide1.xml")!; XDocument xml;
            using (var input = entry.Open()) xml = XDocument.Load(input);
            var shape = xml.Descendants(p + "sp").First();
            shape.Element(p + "spPr")!.Add(new XElement(a + "xfrm", new XAttribute("rot", 900000),
                new XElement(a + "off", new XAttribute("x", 1828800), new XAttribute("y", 914400)),
                new XElement(a + "ext", new XAttribute("cx", 3657600), new XAttribute("cy", 914400))));
            shape.Element(p + "txBody")!.Element(a + "p")!.Element(a + "r")!.AddFirst(new XElement(a + "rPr",
                new XAttribute("sz", 3000), new XAttribute("b", 1), new XElement(a + "solidFill", new XElement(a + "srgbClr", new XAttribute("val", "2468AC"))),
                new XElement(a + "latin", new XAttribute("typeface", "Montserrat"))));
            entry.Delete(); using var output = archive.CreateEntry("ppt/slides/slide1.xml").Open(); xml.Save(output);
        }
        var overridden = (await importer.ImportAsync(overridePath, default)).Slides[0].GetTitleText()!;
        Check(Near(overridden.Y, 1d / 7.5) && overridden.RotationDegrees == 15 && overridden.TextStyle.FontSizePoints == 30
            && overridden.TextStyle.Bold && overridden.TextStyle.Color == "#2468AC" && overridden.TextStyle.FontFamily == "Montserrat"
            && overridden.TextStyle.HorizontalAlignment == PresentTextHorizontalAlignment.Center,
            "direct slide-title geometry and formatting override inherited values while unspecified alignment still inherits");
        var partialStylePath = Path.Combine(paths.DataDirectory, "title-partial-style.pptx");
        File.Copy(fixture, partialStylePath);
        using (var archive = ZipFile.Open(partialStylePath, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("ppt/slides/slide1.xml")!; XDocument xml;
            using (var input = entry.Open()) xml = XDocument.Load(input);
            var body = xml.Descendants(p + "sp").First().Element(p + "txBody")!;
            body.Element(a + "lstStyle")!.Add(new XElement(a + "lvl1pPr", new XElement(a + "defRPr", new XAttribute("sz", 2800), new XAttribute("b", 1),
                new XElement(a + "latin", new XAttribute("typeface", "Montserrat")),
                new XElement(a + "solidFill", new XElement(a + "srgbClr", new XAttribute("val", "2468AC"))))));
            body.Element(a + "p")!.AddFirst(new XElement(a + "pPr", new XElement(a + "defRPr", new XAttribute("i", 1))));
            body.Element(a + "p")!.Element(a + "r")!.AddFirst(new XElement(a + "rPr", new XAttribute("u", "sng")));
            entry.Delete(); using var output = archive.CreateEntry("ppt/slides/slide1.xml").Open(); xml.Save(output);
        }
        var partialStyle = (await importer.ImportAsync(partialStylePath, default)).Slides[0].GetTitleText()!.TextStyle;
        Check(partialStyle.FontSizePoints == 28 && partialStyle.Bold && partialStyle.Italic && partialStyle.Underline
            && partialStyle.FontFamily == "Montserrat" && partialStyle.Color == "#2468AC",
            "partial run and paragraph overrides retain each unspecified list-default title style property");

        var externalThemePath = Path.Combine(paths.DataDirectory, "external-title-theme.pptx");
        File.Copy(fixture, externalThemePath);
        using (var archive = ZipFile.Open(externalThemePath, ZipArchiveMode.Update))
        {
            const string part = "ppt/slideMasters/_rels/slideMaster1.xml.rels";
            var entry = archive.GetEntry(part)!; XDocument xml;
            using (var input = entry.Open()) xml = XDocument.Load(input);
            var relationship = xml.Root!.Elements().Single(item => item.Attribute("Type")!.Value.EndsWith("/theme", StringComparison.Ordinal));
            relationship.SetAttributeValue("TargetMode", "External"); relationship.SetAttributeValue("Target", "https://example.invalid/theme.xml");
            entry.Delete(); using var output = archive.CreateEntry(part).Open(); xml.Save(output);
        }
        var rejected = false;
        try { await importer.ImportAsync(externalThemePath, default); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "title inheritance rejects external theme relationships without fetching network content");

        var legacy = PresentDocument.Create("Legacy title"); legacy.Slides[0].Title = "Legacy metadata title";
        var legacyOwner = new PresentEditor(legacy);
        Check(legacy.Slides[0].GetTitleText() is null && legacyOwner.SetSlideTitle(legacy.Slides[0].Id, "Legacy edited")
            && legacyOwner.Undo() && legacyOwner.SelectedSlide.Title == "Legacy metadata title", "existing metadata-only titles retain their original editable owner behavior without schema migration");

        await repository.SaveAsync(noTitleImported, "No-title import fixture", default);
        await repository.SaveAsync(hiddenImported, "Hidden-title import fixture", default);
        await RenderAndEdit(saved, noTitleImported, hiddenImported, repository, paths, Check);
        return count;
    }

    private static async Task RenderAndEdit(PresentDocument document, PresentDocument noTitleDocument, PresentDocument hiddenDocument,
        PresentRepository repository, IAppPaths paths, Action<bool, string> check)
    {
        await using var session = new PresentBrowserSession(repository);
        check((await session.OpenAsync(document.Id)).Succeeded, "saved title document opens through the real Present session");
        var parser = new CuiRichParser(); var markup = parser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Present.cui")));
        var surface = PresentBrowserSurface.Create(markup, session, () => true);
        using var loader = new CuiControlLoader(surface.ControlRegistry!);
        loader.SetBindingContext(session); loader.SetActionDispatcher(session);
        var (root, diagnostics) = loader.TryLoad(surface.Document);
        check(root is not null && !diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error), "real title editor lowers through canonical CUI");
        loader.WireBindings(root!);
        var window = new Window { Width = 1200, Height = 900, Content = root }; window.Show();
        try
        {
            void Flush() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); using var frame = window.CaptureRenderedFrame(); }
            HavenSceneControl Scene() => root!.GetVisualDescendants().OfType<PresentRetainedSceneControl>().Single()
                .GetVisualDescendants().OfType<HavenSceneControl>().Single();
            Flush();
            var scene = Scene();
            var canvas = scene.Root!;
            var ownerTitle = session.Editor!.SelectedSlide.GetTitleText()!;
            var drawing = new HavenDrawingContext(); ((IHavenDrawCommandSource)canvas).Draw(drawing, 1);
            var painted = drawing.Commands.OfType<HavenTextCommand>().Where(item => item.Layout.Text == ownerTitle.Text).ToArray();
            check(painted.Length == 1 && painted[0].Layout.FontFamily == "Calibri" && Near(painted[0].Layout.FontSize, 44 * 96d / 72),
                "actual retained canvas draws one styled title object without the synthetic fixed title");
            using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(paths.DataDirectory, "present-imported-title.png"));
            var rect = (HavenRect)canvas.GetType().GetMethod("SlideRectLocal", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, null)!;
            var point = scene.TranslatePoint(new Point(rect.X + (ownerTitle.X + ownerTitle.Width * .5) * rect.Width,
                rect.Y + (ownerTitle.Y + ownerTitle.Height * .5) * rect.Height), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            check(session.Editor.Selection.ElementIds.Contains(ownerTitle.Id), "physical pointer selects the imported title using its authored bounds");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter"); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
            window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a"); window.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
            foreach (var character in "Physical title edit") { window.KeyTextInput(character.ToString()); Flush(); }
            check(session.Editor.SelectedSlide.Title == "Physical title edit" && session.Editor.SelectedSlide.GetTitleText()!.Text == "Physical title edit",
                "real canvas typing previews the title object and slide metadata together");
            window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, "Enter"); window.KeyRelease(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, "Enter");
            check(!session.Editor.IsLiveTextEditActive && session.Editor.Undo() && session.Editor.SelectedSlide.Title == "Saved title object",
                "physical title typing commits to one owner undo step");
            session.Editor.Redo(); session.RefreshSelection();
            check((await session.SaveAsync()).Succeeded && (await session.CloseAsync()).Succeeded && (await session.OpenAsync(document.Id)).Succeeded
                && session.Editor!.SelectedSlide.GetTitleText()!.Id == ownerTitle.Id && session.Editor.SelectedSlide.Title == "Physical title edit",
                "physical title edit keeps the same object after session save, close and reopen");
            Flush(); using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(paths.DataDirectory, "present-title-edited-reopened.png"));
            check((await session.CloseAsync()).Succeeded && (await session.OpenAsync(noTitleDocument.Id)).Succeeded,
                "no-title deck opens through the same retained session after repeated navigation");
            Flush();
            scene = Scene(); canvas = scene.Root!;
            drawing = new HavenDrawingContext(); ((IHavenDrawCommandSource)canvas).Draw(drawing, 1);
            check(drawing.Commands.OfType<HavenTextCommand>().Count(item => item.Layout.Text == "Quarterly roadmap") == 1
                && !drawing.Commands.OfType<HavenTextCommand>().Any(item => item.Layout.Text == "Slide 1"),
                "actual no-title canvas paints its ordinary textbox once and no invented numbered title");
            var ordinary = session.Editor!.SelectedSlide.Elements.Single(item => item.Text == "Quarterly roadmap");
            rect = (HavenRect)canvas.GetType().GetMethod("SlideRectLocal", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, null)!;
            point = scene.TranslatePoint(new Point(rect.X + (ordinary.X + ordinary.Width * .5) * rect.Width,
                rect.Y + (ordinary.Y + ordinary.Height * .5) * rect.Height), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            check(session.Editor.Selection.ElementIds.Contains(ordinary.Id) && !session.Editor.IsLiveTextEditActive,
                "an empty synthetic title region cannot hijack selection of the actual normal textbox");
            using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(paths.DataDirectory, "present-no-title.png"));
            await session.CloseAsync(); await session.OpenAsync(hiddenDocument.Id); Flush();
            canvas = Scene().Root!;
            drawing = new HavenDrawingContext(); ((IHavenDrawCommandSource)canvas).Draw(drawing, 1);
            check(!drawing.Commands.OfType<HavenTextCommand>().Any(item => item.Layout.Text is "Hidden title retained" or "Slide 1"),
                "actual retained canvas draws neither a hidden title nor a replacement synthetic title");
            using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(paths.DataDirectory, "present-hidden-title.png"));
        }
        finally { window.Close(); }
        static bool Near(double a, double b) => Math.Abs(a - b) < .0001;
    }
}
