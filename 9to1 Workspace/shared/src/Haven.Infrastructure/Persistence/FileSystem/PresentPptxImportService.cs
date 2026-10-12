using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using System.Xml;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class PresentPptxImportService : IPresentImportService
{
    private const double EmuPerInch = 914_400d;
    private static readonly XNamespace Presentation = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private readonly IAppPaths _paths;

    public PresentPptxImportService(IAppPaths paths) =>
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public IReadOnlyList<string> ImportExtensions { get; } = [".9to1p", ".pptx"];

    public PresentImportSupport Support { get; } = new(
        ".pptx",
        "Editable PPTX subset: slide order and size, titles, styled text boxes, rectangles and speaker notes are reconstructed. Unsupported PowerPoint features are not presented as lossless.",
        ["slide order", "slide dimensions", "slide titles", "plain text boxes", "text-box geometry", "uniform text formatting", "rectangles", "solid fills and strokes", "speaker notes", "hidden slides"],
        ["native images and media", "custom shape paths", "mixed formatting within a text box", "charts and SmartArt", "animations and transitions", "theme/layout fidelity"]);

    public async Task<PresentDocument> ImportAsync(string sourcePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(sourcePath);
        var extension = Path.GetExtension(fullPath);
        if (extension.Equals(".9to1p", StringComparison.OrdinalIgnoreCase))
            return await PresentPackageCodec.ImportAsync(fullPath, _paths.DataDirectory, cancellationToken).ConfigureAwait(false);
        if (!extension.Equals(".pptx", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Present imports native .9to1p packages and a documented editable subset of .pptx presentations.");
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The PowerPoint presentation was not found.", fullPath);

        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var presentation = await ReadXmlAsync(archive, "ppt/presentation.xml", cancellationToken).ConfigureAwait(false);
        var relationships = await ReadXmlAsync(archive, "ppt/_rels/presentation.xml.rels", cancellationToken).ConfigureAwait(false);

        var document = PresentDocument.Create(Path.GetFileNameWithoutExtension(fullPath));
        document.Slides.Clear();
        ApplySlideSize(document, presentation);
        var layoutId = document.Layouts[0].Id;
        var relationshipMap = relationships
            .Root?
            .Elements(PackageRelationships + "Relationship")
            .Where(element => element.Attribute("Id") is not null && element.Attribute("Target") is not null)
            .ToDictionary(
                element => (string)element.Attribute("Id")!,
                element => (string)element.Attribute("Target")!,
                StringComparer.Ordinal) ?? new Dictionary<string, string>(StringComparer.Ordinal);

        var slideIds = presentation.Descendants(Presentation + "sldId").ToArray();
        for (var index = 0; index < slideIds.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relationshipId = (string?)slideIds[index].Attribute(OfficeRelationships + "id");
            if (relationshipId is null || !relationshipMap.TryGetValue(relationshipId, out var target)) continue;
            var partPath = ResolvePresentationPart(target);
            var slideXml = await ReadXmlAsync(archive, partPath, cancellationToken).ConfigureAwait(false);
            var slide = ParseSlide(slideXml, index, layoutId, document.SlideSize);
            slide.SpeakerNotes = await ReadNotesAsync(archive, partPath, cancellationToken).ConfigureAwait(false);
            document.Slides.Add(slide);
        }

        if (document.Slides.Count == 0)
            throw new InvalidDataException("The PPTX contains no readable slides in Present's supported subset.");

        document.Metadata["pptx-import-support"] = Support.Description;
        document.Normalize();
        return document;
    }

    private static PresentSlide ParseSlide(XDocument xml, int order, Guid layoutId, PresentSlideSize slideSize)
    {
        var slide = new PresentSlide
        {
            Order = order,
            LayoutId = layoutId,
            Title = $"Slide {order + 1}",
            Elements = [],
            Hidden = (string?)xml.Root?.Attribute("show") is "0" or "false"
        };
        var background = ReadColor(xml.Root?.Element(Presentation + "cSld")?.Element(Presentation + "bg")?.Element(Presentation + "bgPr")?.Element(Drawing + "solidFill"));
        if (background is not null) { slide.Background.Kind = PresentBackgroundKind.Solid; slide.Background.Color = background; }
        var bodyAssigned = false;
        foreach (var shape in xml.Root?.Element(Presentation + "cSld")?.Element(Presentation + "spTree")?.Elements(Presentation + "sp") ?? [])
        {
            var textBody = shape.Element(Presentation + "txBody");
            var text = ReadText(textBody);
            var nonVisual = shape.Element(Presentation + "nvSpPr");
            var name = (string?)nonVisual?.Element(Presentation + "cNvPr")?.Attribute("name") ?? string.Empty;
            var placeholder = (string?)nonVisual?.Element(Presentation + "nvPr")?.Element(Presentation + "ph")?.Attribute("type");
            if (placeholder is "title" or "ctrTitle" || name.Equals("Title", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Title ", StringComparison.OrdinalIgnoreCase))
            {
                slide.Title = text;
                continue;
            }
            var properties = shape.Element(Presentation + "spPr");
            var preset = (string?)properties?.Element(Drawing + "prstGeom")?.Attribute("prst");
            var textBox = (string?)nonVisual?.Element(Presentation + "cNvSpPr")?.Attribute("txBox") is "1" or "true";
            var rectangle = !textBox && placeholder is null && preset is "rect" or "roundRect";
            if (!rectangle && string.IsNullOrEmpty(text)) continue;
            var element = new PresentElement
            {
                Kind = rectangle ? PresentElementKind.Shape : PresentElementKind.Text,
                ShapeType = rectangle ? preset! : string.Empty,
                Role = !rectangle && !bodyAssigned ? PresentElementRoles.Body : string.Empty,
                Text = text,
                Order = slide.Elements.Count,
                AlternativeText = (string?)nonVisual?.Element(Presentation + "cNvPr")?.Attribute("descr") ?? name
            };
            ApplyGeometry(element, shape, slideSize);
            ApplyTextStyle(element.TextStyle, textBody);
            if (rectangle)
            {
                element.Style.FillColor = ReadColor(properties?.Element(Drawing + "solidFill")) ?? (properties?.Element(Drawing + "noFill") is not null ? "#00000000" : "");
                var line = properties?.Element(Drawing + "ln");
                element.Style.StrokeColor = ReadColor(line?.Element(Drawing + "solidFill")) ?? (line?.Element(Drawing + "noFill") is not null ? "#00000000" : "");
                element.Style.StrokeWidth = Math.Max(0, ParseLong(line?.Attribute("w")) / 9525d);
                if (preset == "roundRect")
                {
                    var formula = (string?)properties?.Element(Drawing + "prstGeom")?.Element(Drawing + "avLst")?.Elements(Drawing + "gd")
                        .FirstOrDefault(gd => (string?)gd.Attribute("name") == "adj")?.Attribute("fmla");
                    var adjustment = formula?.StartsWith("val ", StringComparison.Ordinal) == true &&
                        double.TryParse(formula[4..], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed) ? Math.Clamp(parsed, 0, 50000) : 16667;
                    var width = element.Width * slideSize.WidthInches;
                    var height = element.Height * slideSize.HeightInches;
                    element.Style.CornerRadius = Math.Min(width, height) / Math.Max(.000001, width) * adjustment / 100000;
                }
            }
            slide.Elements.Add(element);
            if (!rectangle) bodyAssigned = true;
        }
        return slide;
    }

    private static void ApplyGeometry(PresentElement element, XElement shape, PresentSlideSize slideSize)
    {
        var transform = shape.Element(Presentation + "spPr")?.Element(Drawing + "xfrm");
        var offset = transform?.Element(Drawing + "off");
        var extent = transform?.Element(Drawing + "ext");
        if (offset is null || extent is null) return;
        var slideWidth = Math.Max(1, slideSize.WidthInches * EmuPerInch);
        var slideHeight = Math.Max(1, slideSize.HeightInches * EmuPerInch);
        element.X = ParseLong(offset.Attribute("x")) / slideWidth;
        element.Y = ParseLong(offset.Attribute("y")) / slideHeight;
        element.Width = ParseLong(extent.Attribute("cx")) / slideWidth;
        element.Height = ParseLong(extent.Attribute("cy")) / slideHeight;
        element.RotationDegrees = ParseLong(transform?.Attribute("rot")) / 60000d;
    }

    private static string ReadText(XElement? body) => body is null ? string.Empty :
        string.Join("\n", body.Elements(Drawing + "p").Select(paragraph => string.Concat(paragraph.Elements().Select(node =>
            node.Name == Drawing + "br" ? "\n" : node.Name == Drawing + "r" || node.Name == Drawing + "fld" ? (string?)node.Element(Drawing + "t") ?? "" : ""))));

    private static void ApplyTextStyle(PresentTextStyle style, XElement? body)
    {
        var paragraph = body?.Element(Drawing + "p");
        var run = paragraph?.Element(Drawing + "r")?.Element(Drawing + "rPr") ?? paragraph?.Element(Drawing + "endParaRPr");
        var defaults = paragraph?.Element(Drawing + "pPr")?.Element(Drawing + "defRPr");
        string? Value(string name) => (string?)run?.Attribute(name) ?? (string?)defaults?.Attribute(name);
        if (double.TryParse(Value("sz"), NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && double.IsFinite(size) && size > 0)
            style.FontSizePoints = size / 100;
        style.Bold = Value("b") is "1" or "true";
        style.Italic = Value("i") is "1" or "true";
        style.Underline = Value("u") is { } underline && underline != "none";
        style.FontFamily = (string?)run?.Element(Drawing + "latin")?.Attribute("typeface") ?? (string?)defaults?.Element(Drawing + "latin")?.Attribute("typeface") ?? string.Empty;
        style.Color = ReadColor(run?.Element(Drawing + "solidFill") ?? defaults?.Element(Drawing + "solidFill")) ?? string.Empty;
        style.HorizontalAlignment = (string?)paragraph?.Element(Drawing + "pPr")?.Attribute("algn") switch
        { "ctr" => PresentTextHorizontalAlignment.Center, "r" => PresentTextHorizontalAlignment.Right, "just" or "dist" => PresentTextHorizontalAlignment.Justify, _ => PresentTextHorizontalAlignment.Left };
        style.VerticalAlignment = (string?)body?.Element(Drawing + "bodyPr")?.Attribute("anchor") switch
        { "ctr" => PresentTextVerticalAlignment.Middle, "b" => PresentTextVerticalAlignment.Bottom, _ => PresentTextVerticalAlignment.Top };
    }

    private static string? ReadColor(XElement? fill)
    {
        var rgb = fill?.Element(Drawing + "srgbClr");
        var hex = (string?)rgb?.Attribute("val");
        if (hex?.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) return null;
        if (rgb?.Element(Drawing + "alpha") is { } alpha)
        {
            var value = (byte)Math.Clamp(Math.Round(ParseLong(alpha.Attribute("val")) / 100000d * 255), 0, 255);
            if (value < 255) return $"#{value:X2}{hex.ToUpperInvariant()}";
        }
        return "#" + hex.ToUpperInvariant();
    }

    private static async Task<string> ReadNotesAsync(ZipArchive archive, string slidePart, CancellationToken token)
    {
        var slash = slidePart.LastIndexOf('/');
        var relationshipsPath = slidePart[..(slash + 1)] + "_rels/" + slidePart[(slash + 1)..] + ".rels";
        if (archive.GetEntry(relationshipsPath) is null) return string.Empty;
        var relationships = await ReadXmlAsync(archive, relationshipsPath, token).ConfigureAwait(false);
        var notes = relationships.Root?.Elements(PackageRelationships + "Relationship").FirstOrDefault(relationship =>
            (string?)relationship.Attribute("Type") == OfficeRelationships.NamespaceName + "/notesSlide");
        if (notes is null) return string.Empty;
        if ((string?)notes.Attribute("TargetMode") == "External" || notes.Attribute("Target") is not { } target)
            throw new InvalidDataException("The PPTX notes relationship must target an internal package part.");
        var baseUri = new Uri("https://package.invalid/" + slidePart);
        if (!Uri.TryCreate(baseUri, target.Value.Replace('\\', '/'), out var resolved) || resolved.Scheme != "https" || resolved.Host != "package.invalid")
            throw new InvalidDataException("The PPTX notes relationship must target an internal package part.");
        var xml = await ReadXmlAsync(archive, Uri.UnescapeDataString(resolved.AbsolutePath.TrimStart('/')), token).ConfigureAwait(false);
        return string.Join("\n", xml.Descendants(Presentation + "sp").Where(shape =>
            (string?)shape.Element(Presentation + "nvSpPr")?.Element(Presentation + "nvPr")?.Element(Presentation + "ph")?.Attribute("type") == "body")
            .Select(shape => ReadText(shape.Element(Presentation + "txBody"))));
    }

    private static void ApplySlideSize(PresentDocument document, XDocument presentation)
    {
        var size = presentation.Descendants(Presentation + "sldSz").FirstOrDefault();
        var cx = ParseLong(size?.Attribute("cx"));
        var cy = ParseLong(size?.Attribute("cy"));
        if (cx <= 0 || cy <= 0) return;
        var width = cx / EmuPerInch;
        var height = cy / EmuPerInch;
        document.SlideSize = new PresentSlideSize
        {
            Preset = ResolvePreset(width, height),
            WidthInches = width,
            HeightInches = height
        };
        document.SlideSize.Normalize();
    }

    private static PresentSlideSizePreset ResolvePreset(double width, double height)
    {
        if (Nearly(width, 13.333333) && Nearly(height, 7.5)) return PresentSlideSizePreset.Widescreen16By9;
        if (Nearly(width, 10) && Nearly(height, 7.5)) return PresentSlideSizePreset.Standard4By3;
        if (Nearly(width, 7.5) && Nearly(height, 13.333333)) return PresentSlideSizePreset.Portrait9By16;
        return PresentSlideSizePreset.Custom;
    }

    private static bool Nearly(double left, double right) => Math.Abs(left - right) < 0.02;

    private static long ParseLong(XAttribute? attribute) =>
        long.TryParse(attribute?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static string ResolvePresentationPart(string target)
    {
        var normalized = target.Replace('\\', '/').TrimStart('/');
        while (normalized.StartsWith("../", StringComparison.Ordinal)) normalized = normalized[3..];
        return normalized.StartsWith("ppt/", StringComparison.OrdinalIgnoreCase) ? normalized : "ppt/" + normalized;
    }

    private static async Task<XDocument> ReadXmlAsync(ZipArchive archive, string path, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidDataException($"The PPTX is missing required part '{path}'.");
        if (entry.Length > 16 * 1024 * 1024) throw new InvalidDataException($"The PPTX XML part '{path}' is too large.");
        await using var entryStream = entry.Open();
        using var reader = XmlReader.Create(entryStream, new XmlReaderSettings
        { Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
        return await XDocument.LoadAsync(reader, LoadOptions.PreserveWhitespace, cancellationToken).ConfigureAwait(false);
    }
}
