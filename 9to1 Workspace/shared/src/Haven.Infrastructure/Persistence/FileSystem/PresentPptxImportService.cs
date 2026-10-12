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
        var titleParts = new Dictionary<string, XDocument>(StringComparer.Ordinal);
        for (var index = 0; index < slideIds.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relationshipId = (string?)slideIds[index].Attribute(OfficeRelationships + "id");
            if (relationshipId is null || !relationshipMap.TryGetValue(relationshipId, out var target)) continue;
            var partPath = ResolvePresentationPart(target);
            var slideXml = await ReadXmlAsync(archive, partPath, cancellationToken).ConfigureAwait(false);
            var titleSource = await ReadTitleSourceAsync(archive, partPath, titleParts, cancellationToken).ConfigureAwait(false);
            var slide = ParseSlide(slideXml, index, layoutId, document.SlideSize, titleSource);
            slide.SpeakerNotes = await ReadNotesAsync(archive, partPath, cancellationToken).ConfigureAwait(false);
            document.Slides.Add(slide);
        }

        if (document.Slides.Count == 0)
            throw new InvalidDataException("The PPTX contains no readable slides in Present's supported subset.");

        document.Metadata["pptx-import-support"] = Support.Description;
        document.Normalize();
        return document;
    }

    private static PresentSlide ParseSlide(XDocument xml, int order, Guid layoutId, PresentSlideSize slideSize, TitleSource titleSource)
    {
        var slide = new PresentSlide
        {
            Order = order,
            LayoutId = layoutId,
            // Navigation may display a numbered fallback, but it must never
            // become invented slide content when the source has no title.
            Title = string.Empty,
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
            var isTitle = placeholder is "title" or "ctrTitle";
            var properties = shape.Element(Presentation + "spPr");
            var preset = (string?)properties?.Element(Drawing + "prstGeom")?.Attribute("prst");
            var textBox = (string?)nonVisual?.Element(Presentation + "cNvSpPr")?.Attribute("txBox") is "1" or "true";
            var rectangle = !isTitle && !textBox && placeholder is null && preset is "rect" or "roundRect";
            if (!isTitle && !rectangle && string.IsNullOrEmpty(text)) continue;
            var element = new PresentElement
            {
                Kind = rectangle ? PresentElementKind.Shape : PresentElementKind.Text,
                ShapeType = rectangle ? preset! : string.Empty,
                Role = isTitle && slide.GetTitleText() is null ? PresentElementRoles.Title
                    : !isTitle && !rectangle && !bodyAssigned ? PresentElementRoles.Body : string.Empty,
                Text = text,
                Visible = (string?)nonVisual?.Element(Presentation + "cNvPr")?.Attribute("hidden") is not ("1" or "true"),
                Order = slide.Elements.Count,
                AlternativeText = (string?)nonVisual?.Element(Presentation + "cNvPr")?.Attribute("descr") ?? name
            };
            if (isTitle) ApplyTitleInheritance(element, shape, slideSize, titleSource);
            else
            {
                ApplyGeometry(element, shape, slideSize);
                ApplyShapeFontReference(element.TextStyle, shape, titleSource);
                ApplyTextStyle(element.TextStyle, ResolveThemeColors(textBody, titleSource));
                ResolveThemeFont(element.TextStyle, titleSource);
            }
            if (rectangle)
            {
                element.Style.FillColor = ReadColor(ResolveThemeColors(properties?.Element(Drawing + "solidFill"), titleSource)) ?? (properties?.Element(Drawing + "noFill") is not null ? "#00000000" : "");
                var line = properties?.Element(Drawing + "ln");
                var inheritedLine = ResolveStyleReference(shape, "lnRef", "lnStyleLst", titleSource);
                var noLine = line?.Element(Drawing + "noFill") is not null;
                element.Style.StrokeColor = noLine ? "#00000000" : ReadColor(ResolveThemeColors(line?.Element(Drawing + "solidFill"), titleSource))
                    ?? ReadColor(inheritedLine?.Element(Drawing + "solidFill")) ?? "";
                element.Style.StrokeWidth = noLine ? 0 : Math.Max(0, ParseLong(line?.Attribute("w") ?? inheritedLine?.Attribute("w")) / 9525d);
                ApplyShapeShadow(element.Style, shape, titleSource);
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
            if (element.Role == PresentElementRoles.Title) slide.Title = text;
            if (!isTitle && !rectangle) bodyAssigned = true;
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
        var paragraphProperties = paragraph?.Element(Drawing + "pPr");
        var listProperties = body?.Element(Drawing + "lstStyle")?.Element(Drawing + "lvl1pPr");
        var defaults = paragraphProperties?.Element(Drawing + "defRPr");
        var listDefaults = listProperties?.Element(Drawing + "defRPr");
        string? Value(string name) => (string?)run?.Attribute(name) ?? (string?)defaults?.Attribute(name) ?? (string?)listDefaults?.Attribute(name);
        if (double.TryParse(Value("sz"), NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && double.IsFinite(size) && size > 0)
            style.FontSizePoints = size / 100;
        if (Value("b") is { } bold) style.Bold = bold is "1" or "true";
        if (Value("i") is { } italic) style.Italic = italic is "1" or "true";
        if (Value("u") is { } underline) style.Underline = underline != "none";
        if (((string?)run?.Element(Drawing + "latin")?.Attribute("typeface") ?? (string?)defaults?.Element(Drawing + "latin")?.Attribute("typeface")
            ?? (string?)listDefaults?.Element(Drawing + "latin")?.Attribute("typeface")) is { } family)
            style.FontFamily = family;
        if (ReadColor(run?.Element(Drawing + "solidFill") ?? defaults?.Element(Drawing + "solidFill") ?? listDefaults?.Element(Drawing + "solidFill")) is { } color) style.Color = color;
        if (((string?)paragraphProperties?.Attribute("algn") ?? (string?)listProperties?.Attribute("algn")) is { } alignment)
            style.HorizontalAlignment = alignment switch
            { "ctr" => PresentTextHorizontalAlignment.Center, "r" => PresentTextHorizontalAlignment.Right, "just" or "dist" => PresentTextHorizontalAlignment.Justify, _ => PresentTextHorizontalAlignment.Left };
        if ((string?)body?.Element(Drawing + "bodyPr")?.Attribute("anchor") is { } anchor)
            style.VerticalAlignment = anchor switch
            { "ctr" => PresentTextVerticalAlignment.Middle, "b" => PresentTextVerticalAlignment.Bottom, _ => PresentTextVerticalAlignment.Top };
    }

    private sealed record TitleSource(XDocument? Layout, XDocument? Master, XDocument? Theme);

    private static XElement? ResolveThemeColors(XElement? value, TitleSource source, XElement? placeholder = null)
    {
        if (value is null) return null;
        var resolved = new XElement(value);
        var colors = source.Theme?.Root?.Element(Drawing + "themeElements")?.Element(Drawing + "clrScheme");
        var colorMap = source.Master?.Root?.Element(Presentation + "clrMap");
        foreach (var color in resolved.DescendantsAndSelf(Drawing + "schemeClr").ToArray())
        {
            var key = (string?)color.Attribute("val");
            if (key is null) continue;
            XElement? replacement;
            if (key == "phClr") replacement = placeholder;
            else
            {
                key = (string?)colorMap?.Attribute(key) ?? key;
                var declared = colors?.Element(Drawing + key)?.Elements().FirstOrDefault();
                var hex = (string?)declared?.Attribute(declared.Name == Drawing + "sysClr" ? "lastClr" : "val");
                replacement = hex is null ? null : new XElement(Drawing + "srgbClr", new XAttribute("val", hex), declared!.Elements());
            }
            if (replacement is null) continue;
            color.ReplaceWith(new XElement(replacement.Name, replacement.Attributes(), replacement.Elements(), color.Elements()));
        }
        return resolved;
    }

    private static void ApplyShapeFontReference(PresentTextStyle style, XElement shape, TitleSource source)
    {
        var reference = ResolveThemeColors(shape.Element(Presentation + "style")?.Element(Drawing + "fontRef"), source);
        if (ReadColor(reference) is { } color) style.Color = color;
        var kind = (string?)reference?.Attribute("idx");
        if (kind is "major" or "minor") style.FontFamily = kind == "major" ? "+mj-lt" : "+mn-lt";
    }

    private static void ResolveThemeFont(PresentTextStyle style, TitleSource source)
    {
        var kind = style.FontFamily switch { "+mj-lt" => "majorFont", "+mn-lt" => "minorFont", _ => null };
        if (kind is not null)
            style.FontFamily = (string?)source.Theme?.Root?.Element(Drawing + "themeElements")?.Element(Drawing + "fontScheme")
                ?.Element(Drawing + kind)?.Element(Drawing + "latin")?.Attribute("typeface") ?? style.FontFamily;
    }

    private static XElement? ResolveStyleReference(XElement shape, string referenceName, string listName, TitleSource source)
    {
        var reference = ResolveThemeColors(shape.Element(Presentation + "style")?.Element(Drawing + referenceName), source);
        var index = ParseLong(reference?.Attribute("idx"));
        var styles = source.Theme?.Root?.Element(Drawing + "themeElements")?.Element(Drawing + "fmtScheme")?.Element(Drawing + listName);
        if (index <= 0 || index > (styles?.Elements().Count() ?? 0)) return null;
        return ResolveThemeColors(styles!.Elements().ElementAt((int)index - 1), source, reference?.Element(Drawing + "srgbClr"));
    }

    private static void ApplyShapeShadow(PresentElementStyle style, XElement shape, TitleSource source)
    {
        var properties = shape.Element(Presentation + "spPr");
        // An authored empty effectLst clears theme effects; do not reintroduce them.
        var effects = ResolveThemeColors(properties?.Element(Drawing + "effectLst"), source)
            ?? ResolveStyleReference(shape, "effectRef", "effectStyleLst", source)?.Element(Drawing + "effectLst");
        var shadow = effects?.Element(Drawing + "outerShdw");
        if (shadow is null) return;
        var radians = ParseLong(shadow.Attribute("dir")) / 60000d * Math.PI / 180;
        var distance = Math.Max(0, ParseLong(shadow.Attribute("dist")) / 9525d);
        style.Shadow = true;
        style.ShadowStyle = new PresentShadowStyle
        {
            Color = ReadColor(shadow) ?? "#FF000000",
            Blur = Math.Max(0, ParseLong(shadow.Attribute("blurRad")) / 9525d),
            OffsetX = Math.Cos(radians) * distance,
            OffsetY = Math.Sin(radians) * distance,
            RotateWithShape = (string?)shadow.Attribute("rotWithShape") is not ("0" or "false")
        };
    }

    private static async Task<TitleSource> ReadTitleSourceAsync(ZipArchive archive, string slidePart, Dictionary<string, XDocument> parts, CancellationToken token)
    {
        var layout = await ReadRelatedPartAsync(archive, slidePart, "slideLayout", parts, token).ConfigureAwait(false);
        var master = layout is { } layoutPart
            ? await ReadRelatedPartAsync(archive, layoutPart.Path, "slideMaster", parts, token).ConfigureAwait(false) : null;
        var theme = master is { } masterPart
            ? await ReadRelatedPartAsync(archive, masterPart.Path, "theme", parts, token).ConfigureAwait(false) : null;
        return new(layout?.Xml, master?.Xml, theme?.Xml);
    }

    private static async Task<(string Path, XDocument Xml)?> ReadRelatedPartAsync(ZipArchive archive, string ownerPart, string kind,
        Dictionary<string, XDocument> parts, CancellationToken token)
    {
        // A deck commonly shares one master/theme across every slide. Parse each
        // bounded package part once for this import, never once per title.
        async Task<XDocument> ReadPart(string path)
        {
            if (parts.TryGetValue(path, out var cached)) return cached;
            var xml = await ReadXmlAsync(archive, path, token).ConfigureAwait(false);
            parts.Add(path, xml); return xml;
        }
        var slash = ownerPart.LastIndexOf('/');
        var relationshipsPath = ownerPart[..(slash + 1)] + "_rels/" + ownerPart[(slash + 1)..] + ".rels";
        if (archive.GetEntry(relationshipsPath) is null) return null;
        var relationships = await ReadPart(relationshipsPath).ConfigureAwait(false);
        var relationship = relationships.Root?.Elements(PackageRelationships + "Relationship").FirstOrDefault(item =>
            (string?)item.Attribute("Type") == OfficeRelationships.NamespaceName + "/" + kind);
        if (relationship is null) return null;
        if ((string?)relationship.Attribute("TargetMode") == "External" || relationship.Attribute("Target") is not { } target
            || !Uri.TryCreate(new Uri("https://package.invalid/" + ownerPart), target.Value.Replace('\\', '/'), out var resolved)
            || resolved.Scheme != "https" || resolved.Host != "package.invalid" || resolved.Query.Length != 0 || resolved.Fragment.Length != 0)
            throw new InvalidDataException($"The PPTX {kind} relationship must target an internal package part.");
        var path = Uri.UnescapeDataString(resolved.AbsolutePath.TrimStart('/'));
        return (path, await ReadPart(path).ConfigureAwait(false));
    }

    private static void ApplyTitleInheritance(PresentElement element, XElement shape, PresentSlideSize size, TitleSource source)
    {
        static XElement? Placeholder(XElement value) => value.Element(Presentation + "nvSpPr")?.Element(Presentation + "nvPr")?.Element(Presentation + "ph");
        static XElement? Find(XDocument? document, XElement reference)
        {
            var referencePlaceholder = Placeholder(reference);
            var type = (string?)referencePlaceholder?.Attribute("type") ?? "title";
            var index = (string?)referencePlaceholder?.Attribute("idx") ?? "0";
            return document?.Root?.Element(Presentation + "cSld")?.Element(Presentation + "spTree")?.Elements(Presentation + "sp")
                .Where(item => (string?)Placeholder(item)?.Attribute("type") is "title" or "ctrTitle")
                .OrderByDescending(item => ((string?)Placeholder(item)?.Attribute("type") == type ? 2 : 0)
                    + (((string?)Placeholder(item)?.Attribute("idx") ?? "0") == index ? 1 : 0)).FirstOrDefault();
        }
        var layoutShape = Find(source.Layout, shape);
        var masterShape = Find(source.Master, layoutShape ?? shape);
        var themeElements = source.Theme?.Root?.Element(Drawing + "themeElements");
        var colors = themeElements?.Element(Drawing + "clrScheme");
        var colorMap = source.Master?.Root?.Element(Presentation + "clrMap");
        void ApplyBody(XElement? body)
        {
            if (body is null) return;
            var resolved = new XElement(body);
            foreach (var color in resolved.Descendants(Drawing + "schemeClr").ToArray())
            {
                var key = (string?)color.Attribute("val");
                if (key is null) continue;
                key = (string?)colorMap?.Attribute(key) ?? key;
                var value = colors?.Element(Drawing + key)?.Elements().FirstOrDefault();
                var hex = (string?)value?.Attribute(value.Name == Drawing + "sysClr" ? "lastClr" : "val");
                if (hex is not null) color.ReplaceWith(new XElement(Drawing + "srgbClr", new XAttribute("val", hex), color.Elements()));
            }
            ApplyTextStyle(element.TextStyle, resolved);
        }
        var titleStyle = source.Master?.Root?.Element(Presentation + "txStyles")?.Element(Presentation + "titleStyle");
        if (titleStyle is not null)
            ApplyBody(new XElement(Presentation + "txBody", new XElement(Drawing + "lstStyle", titleStyle.Elements()), new XElement(Drawing + "p")));
        foreach (var inherited in new[] { masterShape, layoutShape, shape })
        {
            if (inherited is null) continue;
            ApplyGeometry(element, inherited, size);
            ApplyBody(inherited.Element(Presentation + "txBody"));
        }
        var fontKind = element.TextStyle.FontFamily switch { "+mj-lt" => "majorFont", "+mn-lt" => "minorFont", _ => null };
        if (fontKind is not null)
            element.TextStyle.FontFamily = (string?)themeElements?.Element(Drawing + "fontScheme")?.Element(Drawing + fontKind)?.Element(Drawing + "latin")?.Attribute("typeface") ?? string.Empty;
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
