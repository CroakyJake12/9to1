using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using NineToOne.Web.Productivity.Present;

internal static class PresentInheritedStyleChecks
{
    internal static async Task<int> Run(IAppPaths paths)
    {
        var count = 0;
        void Check(bool pass, string message)
        { if (!pass) throw new InvalidOperationException(message); count++; Console.WriteLine("ASSERT " + message); }
        static bool Near(double a, double b) => Math.Abs(a - b) < .00001;
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "operations-review-24.pptx");
        var importer = new PresentPptxImportService(paths);
        var exporter = new PresentPptxExportService();
        var document = await importer.ImportAsync(fixture, default);
        static PresentElement Card(PresentDocument deck) => deck.Slides[0].Elements.First(element => element.ShapeType == "roundRect");
        void CheckStyle(PresentDocument deck, string phase)
        {
            var cards = deck.Slides.SelectMany(slide => slide.Elements).Where(element => element.ShapeType == "roundRect").ToArray();
            Check(cards.Length == 96 && cards.All(card => card.TextStyle.Color == "#FFFFFF" && card.TextStyle.FontFamily == "Calibri"),
                phase + ": all96 real card objects retain inherited white fontRef and authored theme font");
            Check(cards.All(card => card.Style.StrokeColor == "#869EB4" && Near(card.Style.StrokeWidth, 1)),
                phase + ": explicit outline colour combines with actual one-pixel theme default width");
            Check(cards.All(card => card.Style.Shadow && card.Style.ShadowStyle is { Color: "#59000000", RotateWithShape: false } shadow
                && Near(shadow.Blur, 40000d / 9525) && Near(shadow.OffsetX, 0) && Near(shadow.OffsetY, 23000d / 9525)),
                phase + ": actual effectRef2 preserves shadow colour, opacity, blur, distance and direction");
        }
        CheckStyle(document, "independent fixture import");
        var repository = new PresentRepository(paths);
        await repository.SaveAsync(document, "Imported inherited styles", default);
        var reopened = (await repository.LoadAsync(document.Id, default))!;
        CheckStyle(reopened, "native owner save/reopen");
        Check(Card(reopened).Id == Card(document).Id, "native save retains authored object identity alongside additive shadow detail");
        await using (var session = new PresentBrowserSession(repository))
        {
            Check((await session.OpenAsync(reopened.Id)).Succeeded, "style fixture opens through the actual Present session owner");
            var markup = new CuiRichParser().Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Present.cui")));
            var surface = PresentBrowserSurface.Create(markup, session, () => true);
            using var loader = new CuiControlLoader(surface.ControlRegistry!);
            loader.SetBindingContext(session); loader.SetActionDispatcher(session);
            var (root, diagnostics) = loader.TryLoad(surface.Document);
            Check(root is not null && !diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error), "inherited styles use the actual retained CUI host");
            loader.WireBindings(root!);
            var window = new Window { Width = 1280, Height = 900, Content = root }; window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(paths.DataDirectory, "inherited-styles-native.png"));
                var editingStage = root!.GetVisualDescendants().OfType<PresentRetainedSceneControl>().Single();
                var scene = editingStage.GetVisualDescendants().OfType<HavenSceneControl>().Single();
                var drawing = new HavenDrawingContext(); ((IHavenDrawCommandSource)scene.Root!).Draw(drawing, 1);
                Check(drawing.Commands.OfType<HavenShadowCommand>().Count(command => command.Shadow.Brush is HavenSolidBrush { A: 89, R: 0, G: 0, B: 0 }
                    && Near(command.Shadow.Blur, 40000d / 9525) && Near(command.Shadow.OffsetY, 23000d / 9525)) == 8,
                    "actual canvas paints the four cards and four bars with their authored effects");
            }
            finally { window.Close(); }
        }
        var owner = new PresentEditor(reopened);
        var cardId = Card(reopened).Id;
        owner.SelectElements([cardId]);
        var supplied = JsonSerializer.Deserialize<PresentElementStyle>(JsonSerializer.Serialize(Card(owner.Document).Style))!;
        supplied.FillColor = "#102030";
        Check(owner.SetSelectedElementStyle(supplied), "original style owner accepts an imported card appearance edit");
        supplied.ShadowStyle!.Color = "#FF0000";
        Check(Card(owner.Document).Style.ShadowStyle!.Color == "#59000000", "owner copies shadow detail without sharing caller-owned mutable state");
        Check(owner.Undo() && Card(owner.Document).Style.FillColor == "#E2EBF2" && Card(owner.Document).Id == cardId,
            "one undo restores full appearance and the same object identity");
        CheckStyle(owner.Document, "original owner undo");
        var exported = Path.Combine(paths.DataDirectory, "inherited-styles-roundtrip.pptx");
        await exporter.ExportAsync(owner.Document, exported, default);
        CheckStyle(await importer.ImportAsync(exported, default), "actual PPTX export/reimport");
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main", a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        using (var archive = ZipFile.OpenRead(exported))
        {
            using var input = archive.GetEntry("ppt/slides/slide1.xml")!.Open();
            var xml = XDocument.Load(input);
            var shadow = xml.Descendants(a + "outerShdw").First();
            Check((string?)shadow.Attribute("blurRad") == "40000" && (string?)shadow.Attribute("dist") == "23000"
                && (string?)shadow.Attribute("dir") == "5400000" && (string?)shadow.Attribute("rotWithShape") == "0",
                "export writes genuine DrawingML shadow geometry, independently of its importer");
        }
        var overrides = Path.Combine(paths.DataDirectory, "inherited-styles-overrides.pptx");
        File.Copy(fixture, overrides);
        using (var archive = ZipFile.Open(overrides, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("ppt/slides/slide1.xml")!;
            XDocument xml; using (var input = entry.Open()) xml = XDocument.Load(input);
            var shape = xml.Descendants(p + "sp").First(item => item.Element(p + "spPr")?.Element(a + "prstGeom")?.Attribute("prst")?.Value == "roundRect");
            var properties = shape.Element(p + "spPr")!;
            properties.Element(a + "ln")!.SetAttributeValue("w", 0);
            properties.Add(new XElement(a + "effectLst"));
            shape.Element(p + "txBody")!.Element(a + "p")!.Element(a + "pPr")!.Element(a + "defRPr")!
                .Add(new XElement(a + "solidFill", new XElement(a + "srgbClr", new XAttribute("val", "123456"))));
            entry.Delete(); using var output = archive.CreateEntry("ppt/slides/slide1.xml").Open(); xml.Save(output);
        }
        var overridden = Card(await importer.ImportAsync(overrides, default));
        Check(overridden.TextStyle.Color == "#123456", "explicit paragraph colour wins over shape fontRef colour");
        Check(overridden.Style.StrokeWidth == 0, "an explicit zero outline width is never replaced by a theme width");
        Check(!overridden.Style.Shadow && overridden.Style.ShadowStyle is null, "an explicit empty effect list clears theme effects");
        Console.WriteLine("INHERITED_STYLE_ARTIFACT " + exported);
        return count;
    }
}
