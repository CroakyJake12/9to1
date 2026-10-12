using System.IO.Compression;
using System.Xml.Linq;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

internal static class PresentInterchangeChecks
{
    internal static async Task<int> Run(IAppPaths paths)
    {
        var count = 0;
        void Check(bool pass, string message)
        { if (!pass) throw new InvalidOperationException(message); count++; Console.WriteLine("ASSERT " + message); }
        var importer = new PresentPptxImportService(paths);
        var exporter = new PresentPptxExportService();
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "external-styled-notes.pptx");
        var document = await importer.ImportAsync(fixture, default);
        void CheckContent(PresentDocument deck, string phase)
        {
            Check(deck.Slides.Count == 2 && deck.Slides[0].Title == "Quarterly roadmap" && deck.Slides[1].Title == "Appendix", phase + ": independent authored slide order and titles survive");
            Check(deck.Slides[1].Hidden, phase + ": actual hidden-slide flag survives");
            Check(Math.Abs(deck.SlideSize.WidthInches - 13.333333) < .00001 && deck.SlideSize.HeightInches == 7.5, phase + ": real slide dimensions survive");
            var slide = deck.Slides[0];
            var shape = slide.Elements.Single(e => e.Kind == PresentElementKind.Shape);
            var text = slide.Elements.Single(e => e.Kind == PresentElementKind.Text && e.Text.Contains("Plan together."));
            Check(shape.ShapeType == "rect" && shape.Style.FillColor == "#4472C4" && shape.Style.StrokeColor == "#204080", phase + ": rectangle remains a real coloured editable shape");
            Check(Math.Abs(shape.Style.StrokeWidth - 8d / 3) < .0001, phase + ": two-point stroke keeps physical width");
            Check(Math.Abs(shape.X * deck.SlideSize.WidthInches - 2.4) < .00001 && Math.Abs(shape.Width * deck.SlideSize.WidthInches - 6.4) < .00001 && shape.Y == .3, phase + ": shape position and size survive native EMU conversion");
            Check(text.Text == "Plan together.\nBuild clearly.", phase + ": paragraph boundaries remain editable newlines");
            Check(text.TextStyle.FontFamily == "Montserrat" && text.TextStyle.FontSizePoints == 36 && text.TextStyle.Bold && text.TextStyle.Italic && text.TextStyle.Underline && text.TextStyle.Color == "#FFFFFF", phase + ": font size, family, bold, italic, underline and white colour survive");
            Check(text.TextStyle.HorizontalAlignment == PresentTextHorizontalAlignment.Center && text.TextStyle.VerticalAlignment == PresentTextVerticalAlignment.Middle, phase + ": horizontal and vertical text alignment survive");
            Check(slide.SpeakerNotes == "Present the priorities.\nPause for questions.\nUnicode: café 👩🏽‍💻", phase + ": actual native notes part preserves paragraph and Unicode content");
            Check(slide.Elements.All(e => !e.Text.Contains("preserved in 9to1")), phase + ": supported visual content has no explanatory-text substitute");
        }
        CheckContent(document, "external import");
        var destination = Path.Combine(paths.DataDirectory, "external-roundtrip.pptx");
        var result = await exporter.ExportWithReportAsync(document, destination, default);
        Check(!result.CompatibilityReport.Issues.Any(issue => issue.FeatureType is "speaker-notes" or "text-formatting" or "hidden-slide" or "object-type"), "compatibility report does not mislabel supported native content as degraded");
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main", a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        using (var archive = ZipFile.OpenRead(destination))
        {
            XDocument Read(string name) { using var source = archive.GetEntry(name)!.Open(); return XDocument.Load(source); }
            var slide = Read("ppt/slides/slide1.xml");
            Check(slide.Descendants(p + "sp").Any(shape => shape.Element(p + "spPr")?.Element(a + "solidFill")?.Element(a + "srgbClr")?.Attribute("val")?.Value == "4472C4"), "export writes a native DrawingML shape fill, independently of reimport");
            Check(Read("ppt/notesSlides/notesSlide1.xml").Descendants(a + "t").Any(t => t.Value == "Pause for questions."), "export writes actual editable speaker-note text in its own notes part");
            Check(archive.GetEntry("ppt/notesMasters/notesMaster1.xml") is not null && Read("ppt/notesSlides/_rels/notesSlide1.xml.rels").Root!.Elements().Count() == 2, "notes part has real slide and notes-master relationships");
            Check(Read("ppt/slides/slide2.xml").Root!.Attribute("show")!.Value == "0", "export uses the native PowerPoint hidden-slide flag");
            foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".xml") || e.FullName.EndsWith(".rels")))
            { using var input = entry.Open(); _ = XDocument.Load(input); }
            Check(true, "every generated XML and relationship part is well formed");
        }
        var roundTrip = await importer.ImportAsync(destination, default);
        CheckContent(roundTrip, "export/reimport");
        Check(document.Id != roundTrip.Id, "import creates a new canonical document instead of overwriting the original identity");
        var shortHex = roundTrip.Slides[0].Elements.Single(e => e.Kind == PresentElementKind.Text && e.Text.Contains("Plan together."));
        shortHex.TextStyle.Color = "#fff";
        roundTrip.Slides[0].Elements.Single(e => e.Kind == PresentElementKind.Shape).Style.CornerRadius = .07;
        await exporter.ExportAsync(roundTrip, Path.Combine(paths.DataDirectory, "short-hex.pptx"), default);
        var shortRoundTrip = await importer.ImportAsync(Path.Combine(paths.DataDirectory, "short-hex.pptx"), default);
        Check(shortRoundTrip.Slides[0].Elements.Single(e => e.Text.Contains("Plan together.")).TextStyle.Color == "#FFFFFF", "short hex accepted by the editor exports as a real standard RGB colour");
        var rounded = shortRoundTrip.Slides[0].Elements.Single(e => e.Kind == PresentElementKind.Shape);
        Check(rounded.ShapeType == "roundRect" && Math.Abs(rounded.Style.CornerRadius - .07) < .00001,
            "rounded rectangle adjustment preserves the authored physical corner radius");
        var original = await File.ReadAllBytesAsync(fixture);
        var broken = Path.Combine(paths.DataDirectory, "broken-notes.pptx");
        await File.WriteAllBytesAsync(broken, original);
        using (var archive = ZipFile.Open(broken, ZipArchiveMode.Update)) archive.GetEntry("ppt/notesSlides/notesSlide1.xml")!.Delete();
        var rejected = false;
        try { await importer.ImportAsync(broken, default); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && File.ReadAllBytes(fixture).SequenceEqual(original), "a missing referenced notes part fails without silently discarding notes or changing the original file");
        var externalNotes = Path.Combine(paths.DataDirectory, "external-notes.pptx");
        await File.WriteAllBytesAsync(externalNotes, original);
        using (var archive = ZipFile.Open(externalNotes, ZipArchiveMode.Update))
        {
            const string rels = "ppt/slides/_rels/slide1.xml.rels";
            var entry = archive.GetEntry(rels)!;
            XDocument xml; using (var input = entry.Open()) xml = XDocument.Load(input);
            var notes = xml.Root!.Elements().Single(e => e.Attribute("Type")!.Value.EndsWith("/notesSlide", StringComparison.Ordinal));
            notes.SetAttributeValue("TargetMode", "External"); notes.SetAttributeValue("Target", "https://example.invalid/notes.xml");
            entry.Delete(); using var output = archive.CreateEntry(rels).Open(); xml.Save(output);
        }
        rejected = false;
        try { await importer.ImportAsync(externalNotes, default); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "external notes relationships are rejected instead of fetching a URL or silently losing notes");
        Console.WriteLine("INTERCHANGE_ARTIFACT " + destination);
        return count;
    }
}
