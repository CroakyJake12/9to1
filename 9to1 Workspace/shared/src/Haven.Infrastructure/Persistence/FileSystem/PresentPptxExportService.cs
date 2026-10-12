using System.IO.Compression;
using System.Globalization;
using System.Security;
using System.Text;
using System.Xml.Linq;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class PresentPptxExportService : IPresentReportExportService
{
    private const double EmuPerInch = 914_400d;
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static bool NativeShape(PresentElement element) => element.Kind == PresentElementKind.Shape &&
        element.VectorShape is null && element.ShapeType is "rect" or "roundRect";

    public IReadOnlyList<string> ExportExtensions { get; } = [".9to1p", ".pptx"];

    public async Task<string> ExportAsync(
        PresentDocument document,
        string destinationPath,
        CancellationToken cancellationToken) =>
        (await ExportWithReportAsync(document, destinationPath, cancellationToken).ConfigureAwait(false)).Path;

    public PresentCompatibilityReport PreviewExport(PresentDocument document, string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var extension = Path.GetExtension(destinationPath);
        if (extension.Equals(".9to1p", StringComparison.OrdinalIgnoreCase))
            return new PresentCompatibilityReport(".9to1p", PresentCompatibilityDirection.Export, []);
        if (!extension.Equals(".pptx", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Present exports .9to1p and .pptx presentations.");

        return CreatePptxCompatibilityReport(document);
    }

    public async Task<PresentExportResult> ExportWithReportAsync(
        PresentDocument document,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        cancellationToken.ThrowIfCancellationRequested();
        var extension = Path.GetExtension(destinationPath);
        var report = PreviewExport(document, destinationPath);

        if (extension.Equals(".9to1p", StringComparison.OrdinalIgnoreCase))
        {
            await PresentPackageCodec.ExportAsync(document, destinationPath, cancellationToken).ConfigureAwait(false);
            return new PresentExportResult(Path.GetFullPath(destinationPath), report);
        }

        document.Normalize();
        var fullDestination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullDestination);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        var temporary = fullDestination + $".{Guid.NewGuid():N}.tmp";

        try
        {
            await WritePackageAsync(document, temporary, cancellationToken).ConfigureAwait(false);
            ValidatePackage(temporary, document.Slides.Count);
            File.Move(temporary, fullDestination, overwrite: true);
            return new PresentExportResult(fullDestination, report);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static PresentCompatibilityReport CreatePptxCompatibilityReport(PresentDocument document)
    {
        var issues = new List<PresentCompatibilityIssue>();
        foreach (var slide in document.Slides)
        {
            if (slide.Transition.Kind != PresentTransitionKind.None)
                Add(PresentCompatibilitySeverity.Warning, "transition", slide.Id,
                    "The transition is not compiled into a destination-native transition.");
            if (slide.Animations.Count > 0)
                Add(PresentCompatibilitySeverity.Warning, "animation", slide.Id,
                    "Slide animation cues are not compiled into the PPTX output.");
            if (slide.Background.Kind == PresentBackgroundKind.Image)
                Add(PresentCompatibilitySeverity.Warning, "slide-background", slide.Id,
                    "The slide background is replaced by the destination theme background.");
            if (slide.LayoutId is not null)
                Add(PresentCompatibilitySeverity.Warning, "slide-layout", slide.Id,
                    "The source slide layout relationship is replaced by a blank PPTX layout.");

            foreach (var element in slide.Elements.Where(item => item.Visible))
            {
                if (element.Kind != PresentElementKind.Text && !NativeShape(element))
                {
                    Add(PresentCompatibilitySeverity.Warning, "object-type", element.Id,
                        $"The {element.Kind} object is represented by explanatory text; its interactive or visual behavior is not preserved.");
                }

            }
        }

        if (document.Layouts.Count > 1 || document.Sections.Count > 0)
            Add(PresentCompatibilitySeverity.Warning, "presentation-structure", document.Id,
                "Source themes, masters, layouts, and sections are not preserved by the PPTX writer.");

        return new PresentCompatibilityReport(".pptx", PresentCompatibilityDirection.Export, issues);

        void Add(PresentCompatibilitySeverity severity, string feature, Guid? target, string description) =>
            issues.Add(new PresentCompatibilityIssue(
                Guid.NewGuid(), severity, PresentCompatibilityDirection.Export, feature, target,
                description, PresentCompatibilityResolution.Degraded));
    }

    private static async Task WritePackageAsync(
        PresentDocument document,
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        var slideWidth = Math.Max(1, (long)Math.Round(document.SlideSize.WidthInches * EmuPerInch));
        var slideHeight = Math.Max(1, (long)Math.Round(document.SlideSize.HeightInches * EmuPerInch));

        await WriteEntryAsync(archive, "[Content_Types].xml", BuildContentTypes(document.Slides.Count), cancellationToken);
        await WriteEntryAsync(archive, "_rels/.rels", RootRelationships(), cancellationToken);
        await WriteEntryAsync(archive, "ppt/presentation.xml", BuildPresentation(document.Slides.Count, slideWidth, slideHeight), cancellationToken);
        await WriteEntryAsync(archive, "ppt/_rels/presentation.xml.rels", BuildPresentationRelationships(document.Slides.Count), cancellationToken);
        await WriteEntryAsync(archive, "ppt/slideMasters/slideMaster1.xml", SlideMaster(), cancellationToken);
        await WriteEntryAsync(archive, "ppt/slideMasters/_rels/slideMaster1.xml.rels", SlideMasterRelationships(), cancellationToken);
        await WriteEntryAsync(archive, "ppt/slideLayouts/slideLayout1.xml", SlideLayout(), cancellationToken);
        await WriteEntryAsync(archive, "ppt/slideLayouts/_rels/slideLayout1.xml.rels", SlideLayoutRelationships(), cancellationToken);
        await WriteEntryAsync(archive, "ppt/theme/theme1.xml", Theme(), cancellationToken);
        await WriteEntryAsync(archive, "ppt/notesMasters/notesMaster1.xml", NotesMaster(), cancellationToken);
        await WriteEntryAsync(archive, "ppt/notesMasters/_rels/notesMaster1.xml.rels", NotesMasterRelationships(), cancellationToken);

        for (var index = 0; index < document.Slides.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var number = index + 1;
            await WriteEntryAsync(archive, $"ppt/slides/slide{number}.xml", BuildSlide(document, document.Slides[index], slideWidth, slideHeight), cancellationToken);
            await WriteEntryAsync(archive, $"ppt/slides/_rels/slide{number}.xml.rels", SlideRelationships(number), cancellationToken);
            await WriteEntryAsync(archive, $"ppt/notesSlides/notesSlide{number}.xml", BuildNotes(document.Slides[index]), cancellationToken);
            await WriteEntryAsync(archive, $"ppt/notesSlides/_rels/notesSlide{number}.xml.rels", NotesRelationships(number), cancellationToken);
        }

        archive.Dispose();
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string BuildContentTypes(int slideCount)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        builder.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        builder.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
        builder.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        builder.Append("<Override PartName=\"/ppt/presentation.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml\"/>");
        builder.Append("<Override PartName=\"/ppt/slideMasters/slideMaster1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml\"/>");
        builder.Append("<Override PartName=\"/ppt/slideLayouts/slideLayout1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml\"/>");
        builder.Append("<Override PartName=\"/ppt/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/>");
        builder.Append("<Override PartName=\"/ppt/notesMasters/notesMaster1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.notesMaster+xml\"/>");
        for (var index = 1; index <= slideCount; index++)
        {
            builder.Append($"<Override PartName=\"/ppt/slides/slide{index}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/>");
            builder.Append($"<Override PartName=\"/ppt/notesSlides/notesSlide{index}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.notesSlide+xml\"/>");
        }
        builder.Append("</Types>");
        return builder.ToString();
    }

    private static string RootRelationships() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"ppt/presentation.xml\"/>" +
        "</Relationships>";

    private static string BuildPresentation(int slideCount, long slideWidth, long slideHeight)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        builder.Append("<p:presentation xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\">");
        builder.Append("<p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst>");
        builder.Append($"<p:notesMasterIdLst><p:notesMasterId r:id=\"rId{slideCount + 2}\"/></p:notesMasterIdLst><p:sldIdLst>");
        for (var index = 0; index < slideCount; index++)
            builder.Append($"<p:sldId id=\"{256 + index}\" r:id=\"rId{index + 2}\"/>");
        builder.Append("</p:sldIdLst>");
        builder.Append($"<p:sldSz cx=\"{slideWidth}\" cy=\"{slideHeight}\" type=\"custom\"/>");
        builder.Append("<p:notesSz cx=\"6858000\" cy=\"9144000\"/><p:defaultTextStyle><a:defPPr/></p:defaultTextStyle></p:presentation>");
        return builder.ToString();
    }

    private static string BuildPresentationRelationships(int slideCount)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        builder.Append("<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster\" Target=\"slideMasters/slideMaster1.xml\"/>");
        for (var index = 0; index < slideCount; index++)
            builder.Append($"<Relationship Id=\"rId{index + 2}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide\" Target=\"slides/slide{index + 1}.xml\"/>");
        builder.Append($"<Relationship Id=\"rId{slideCount + 2}\" Type=\"{R}/notesMaster\" Target=\"notesMasters/notesMaster1.xml\"/>");
        builder.Append("</Relationships>");
        return builder.ToString();
    }

    private static string SlideMaster() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:sldMaster xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"><p:cSld name=""><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr></p:spTree></p:cSld><p:clrMap accent1="accent1" accent2="accent2" accent3="accent3" accent4="accent4" accent5="accent5" accent6="accent6" bg1="lt1" bg2="lt2" folHlink="folHlink" hlink="hlink" tx1="dk1" tx2="dk2"/><p:sldLayoutIdLst><p:sldLayoutId id="1" r:id="rId1"/></p:sldLayoutIdLst><p:txStyles><p:titleStyle/><p:bodyStyle/><p:otherStyle/></p:txStyles></p:sldMaster>
""";

    private static string SlideMasterRelationships() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout" Target="../slideLayouts/slideLayout1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme" Target="../theme/theme1.xml"/></Relationships>
""";

    private static string SlideLayout() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:sldLayout xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" type="blank" preserve="1"><p:cSld name="Blank"><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr></p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>
""";

    private static string SlideLayoutRelationships() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster" Target="../slideMasters/slideMaster1.xml"/></Relationships>
""";

    private static string Theme() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Haven"><a:themeElements><a:clrScheme name="Haven"><a:dk1><a:srgbClr val="1F1F1F"/></a:dk1><a:lt1><a:srgbClr val="FFFFFF"/></a:lt1><a:dk2><a:srgbClr val="343434"/></a:dk2><a:lt2><a:srgbClr val="F4F4F4"/></a:lt2><a:accent1><a:srgbClr val="E65F42"/></a:accent1><a:accent2><a:srgbClr val="4F7CAC"/></a:accent2><a:accent3><a:srgbClr val="6A9A6B"/></a:accent3><a:accent4><a:srgbClr val="8C6FB3"/></a:accent4><a:accent5><a:srgbClr val="C28A3D"/></a:accent5><a:accent6><a:srgbClr val="4F9A96"/></a:accent6><a:hlink><a:srgbClr val="0563C1"/></a:hlink><a:folHlink><a:srgbClr val="954F72"/></a:folHlink></a:clrScheme><a:fontScheme name="Haven"><a:majorFont><a:latin typeface="Aptos Display"/><a:ea typeface=""/><a:cs typeface=""/></a:majorFont><a:minorFont><a:latin typeface="Aptos"/><a:ea typeface=""/><a:cs typeface=""/></a:minorFont></a:fontScheme><a:fmtScheme name="Haven"><a:fillStyleLst><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:fillStyleLst><a:lnStyleLst><a:ln w="6350"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="solid"/></a:ln><a:ln w="12700"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="solid"/></a:ln><a:ln w="19050"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="solid"/></a:ln></a:lnStyleLst><a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst><a:bgFillStyleLst><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:bgFillStyleLst></a:fmtScheme></a:themeElements></a:theme>
""";

    private static XElement Root(XName name, params object[] content) => new(name,
        new XAttribute(XNamespace.Xmlns + "p", P), new XAttribute(XNamespace.Xmlns + "a", A),
        new XAttribute(XNamespace.Xmlns + "r", R), content);

    private static XElement GroupProperties() => new(P + "grpSpPr", new XElement(A + "xfrm",
        new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
        new XElement(A + "ext", new XAttribute("cx", 0), new XAttribute("cy", 0)),
        new XElement(A + "chOff", new XAttribute("x", 0), new XAttribute("y", 0)),
        new XElement(A + "chExt", new XAttribute("cx", 0), new XAttribute("cy", 0))));

    private static XElement ShapeTree() => new(P + "spTree",
        new XElement(P + "nvGrpSpPr", new XElement(P + "cNvPr", new XAttribute("id", 1), new XAttribute("name", "")),
            new XElement(P + "cNvGrpSpPr"), new XElement(P + "nvPr")), GroupProperties());

    private static string BuildSlide(PresentDocument document, PresentSlide slide, long slideWidth, long slideHeight)
    {
        var tree = ShapeTree();
        var id = 3;
        foreach (var element in slide.Elements.Where(item => item.Kind != PresentElementKind.Group
            && (item.Visible || item.Kind == PresentElementKind.Text && string.Equals(item.Role, PresentElementRoles.Title, StringComparison.OrdinalIgnoreCase))).OrderBy(item => item.Order))
        {
            if (element.Kind == PresentElementKind.Text && string.IsNullOrEmpty(element.Text)
                && !string.Equals(element.Role, PresentElementRoles.Title, StringComparison.OrdinalIgnoreCase)) continue;
            tree.Add(ElementShape(id++, $"Element {element.Id:N}", element, document.Theme, slideWidth, slideHeight));
        }
        var background = slide.Background.Kind == PresentBackgroundKind.Solid ? slide.Background.Color :
            document.Theme.Background.Kind == PresentBackgroundKind.Solid ? document.Theme.Background.Color : document.Theme.Colors.Background;
        return Root(P + "sld", new XAttribute("show", slide.Hidden ? "0" : "1"),
            new XElement(P + "cSld", new XAttribute("name", slide.Title), new XElement(P + "bg", new XElement(P + "bgPr",
                SolidFill(background, document.Theme.Colors.Background), new XElement(A + "effectLst"))), tree),
            new XElement(P + "clrMapOvr", new XElement(A + "masterClrMapping"))).ToString(SaveOptions.DisableFormatting);
    }

    private static XElement ElementShape(int id, string name, PresentElement element, PresentTheme theme, long slideWidth, long slideHeight)
    {
        var nativeShape = NativeShape(element);
        var style = element.TextStyle;
        var rounded = nativeShape && (element.ShapeType == "roundRect" || element.Style.CornerRadius > 0);
        var adjustment = (int)Math.Round(Math.Clamp(element.Style.CornerRadius * element.Width * slideWidth /
            Math.Max(1, Math.Min(element.Width * slideWidth, element.Height * slideHeight)) * 100000, 0, 50000));
        var properties = new XElement(P + "spPr", new XElement(A + "xfrm",
            new XAttribute("rot", (long)Math.Round(element.RotationDegrees * 60000)),
            new XElement(A + "off", new XAttribute("x", ToEmu(element.X, slideWidth)), new XAttribute("y", ToEmu(element.Y, slideHeight))),
            new XElement(A + "ext", new XAttribute("cx", Math.Max(1, ToEmu(element.Width, slideWidth))), new XAttribute("cy", Math.Max(1, ToEmu(element.Height, slideHeight))))),
            new XElement(A + "prstGeom", new XAttribute("prst", rounded ? "roundRect" : "rect"), new XElement(A + "avLst",
                rounded ? new XElement(A + "gd", new XAttribute("name", "adj"), new XAttribute("fmla", "val " + adjustment.ToString(CultureInfo.InvariantCulture))) : null)));
        properties.Add(nativeShape ? SolidFill(element.Style.FillColor, theme.Colors.Accent1, element.Opacity) : new XElement(A + "noFill"));
        properties.Add(new XElement(A + "ln", new XAttribute("w", (long)Math.Round(Math.Max(0, element.Style.StrokeWidth) * 9525)),
            nativeShape && element.Style.StrokeWidth > 0 ? SolidFill(element.Style.StrokeColor, theme.Colors.Accent1, element.Opacity) : new XElement(A + "noFill")));
        if (nativeShape && element.Style.Shadow && element.Style.ShadowStyle is { } shadow)
        {
            var direction = Math.Atan2(shadow.OffsetY, shadow.OffsetX) * 180 / Math.PI;
            if (direction < 0) direction += 360;
            properties.Add(new XElement(A + "effectLst", new XElement(A + "outerShdw",
                new XAttribute("blurRad", (long)Math.Round(shadow.Blur * 9525)),
                new XAttribute("dist", (long)Math.Round(Math.Sqrt(shadow.OffsetX * shadow.OffsetX + shadow.OffsetY * shadow.OffsetY) * 9525)),
                new XAttribute("dir", (long)Math.Round(direction * 60000)),
                new XAttribute("rotWithShape", shadow.RotateWithShape ? "1" : "0"),
                SolidFill(shadow.Color, "#59000000", element.Opacity).Elements())));
        }
        var result = new XElement(P + "sp", new XElement(P + "nvSpPr",
            new XElement(P + "cNvPr", new XAttribute("id", id), new XAttribute("name", name), new XAttribute("descr", element.AlternativeText ?? ""),
                !element.Visible ? new XAttribute("hidden", "1") : null),
            new XElement(P + "cNvSpPr", new XAttribute("txBox", nativeShape ? "0" : "1")), new XElement(P + "nvPr",
                element.Kind == PresentElementKind.Text && string.Equals(element.Role, PresentElementRoles.Title, StringComparison.OrdinalIgnoreCase)
                    ? new XElement(P + "ph", new XAttribute("type", "title")) : null)), properties);
        var text = element.Kind == PresentElementKind.Text || nativeShape ? element.Text : ExportText(element);
        if (!nativeShape || !string.IsNullOrEmpty(text))
            result.Add(TextBody(text, style, theme, element.Opacity));
        return result;
    }

    private static XElement TextBody(string text, PresentTextStyle style, PresentTheme theme, double opacity = 1)
    {
        var alignment = style.HorizontalAlignment switch
        { PresentTextHorizontalAlignment.Center => "ctr", PresentTextHorizontalAlignment.Right => "r", PresentTextHorizontalAlignment.Justify => "just", _ => "l" };
        var anchor = style.VerticalAlignment switch
        { PresentTextVerticalAlignment.Middle => "ctr", PresentTextVerticalAlignment.Bottom => "b", _ => "t" };
        var size = (int)Math.Round(Math.Clamp(style.FontSizePoints, 1, 400) * 100);
        XElement RunProperties(XName name) => new(name, new XAttribute("lang", "en-GB"), new XAttribute("sz", size),
            new XAttribute("b", style.Bold ? "1" : "0"), new XAttribute("i", style.Italic ? "1" : "0"),
            new XAttribute("u", style.Underline ? "sng" : "none"), SolidFill(style.Color, theme.Colors.Foreground, opacity),
            new XElement(A + "latin", new XAttribute("typeface", string.IsNullOrWhiteSpace(style.FontFamily) ? theme.BodyFontFamily : style.FontFamily)));
        return new XElement(P + "txBody",
            new XElement(A + "bodyPr", new XAttribute("wrap", "square"), new XAttribute("anchor", anchor),
                new XAttribute("lIns", 0), new XAttribute("rIns", 0), new XAttribute("tIns", 0), new XAttribute("bIns", 0)),
            new XElement(A + "lstStyle"),
            (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').Select(line =>
                new XElement(A + "p", new XElement(A + "pPr", new XAttribute("algn", alignment)),
                    new XElement(A + "r", RunProperties(A + "rPr"), new XElement(A + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), line)),
                    RunProperties(A + "endParaRPr"))));
    }

    private static XElement SolidFill(string? value, string fallback, double opacity = 1)
    {
        static System.Drawing.Color? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim();
            if (!text.StartsWith('#')) { var named = System.Drawing.Color.FromName(text); return named.IsKnownColor ? named : null; }
            var hex = text[1..];
            if (hex.Length is 3 or 4) hex = string.Concat(hex.Select(character => new string(character, 2)));
            if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed)) return null;
            return System.Drawing.Color.FromArgb(hex.Length == 8 ? (int)(packed >> 24) : 255, (int)(packed >> 16) & 255, (int)(packed >> 8) & 255, (int)packed & 255);
        }
        var color = Parse(value) ?? Parse(fallback) ?? System.Drawing.Color.Black;
        var rgb = new XElement(A + "srgbClr", new XAttribute("val", $"{color.R:X2}{color.G:X2}{color.B:X2}"));
        var alpha = (int)Math.Round(color.A / 255d * Math.Clamp(opacity, 0, 1) * 100000);
        if (alpha < 100000) rgb.Add(new XElement(A + "alpha", new XAttribute("val", alpha)));
        return new XElement(A + "solidFill", rgb);
    }

    private static string BuildNotes(PresentSlide slide)
    {
        var tree = ShapeTree();
        tree.Add(new XElement(P + "sp", new XElement(P + "nvSpPr",
            new XElement(P + "cNvPr", new XAttribute("id", 2), new XAttribute("name", "Speaker notes")),
            new XElement(P + "cNvSpPr"), new XElement(P + "nvPr", new XElement(P + "ph", new XAttribute("type", "body"), new XAttribute("idx", 1)))),
            new XElement(P + "spPr"), TextBody(slide.SpeakerNotes, new PresentTextStyle { FontSizePoints = 12 }, new PresentTheme())));
        return Root(P + "notes", new XElement(P + "cSld", tree), new XElement(P + "clrMapOvr", new XElement(A + "masterClrMapping"))).ToString(SaveOptions.DisableFormatting);
    }

    private static string NotesMaster() => Root(P + "notesMaster", new XElement(P + "cSld", ShapeTree()),
        new XElement(P + "clrMap", new[] { "accent1", "accent2", "accent3", "accent4", "accent5", "accent6", "hlink", "folHlink" }.Select(name => new XAttribute(name, name)),
            new XAttribute("bg1", "lt1"), new XAttribute("bg2", "lt2"), new XAttribute("tx1", "dk1"), new XAttribute("tx2", "dk2")),
        new XElement(P + "notesStyle")).ToString(SaveOptions.DisableFormatting);

    private static string NotesMasterRelationships() => $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"{R}/theme\" Target=\"../theme/theme1.xml\"/></Relationships>";

    private static string NotesRelationships(int number) => $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"{R}/slide\" Target=\"../slides/slide{number}.xml\"/><Relationship Id=\"rId2\" Type=\"{R}/notesMaster\" Target=\"../notesMasters/notesMaster1.xml\"/></Relationships>";

    private static string ExportText(PresentElement element) => element.Kind switch
    {
        PresentElementKind.Text => element.Text,
        PresentElementKind.Image => "[Image preserved in 9to1] " + FirstNonEmpty(element.AlternativeText, element.Text, element.AssetId),
        PresentElementKind.Shape => "[Shape preserved in 9to1] " + FirstNonEmpty(element.Text, element.ShapeType),
        PresentElementKind.GenUi => "[Interactive 9to1 content — open in 9to1 to use] " + FirstNonEmpty(element.AlternativeText, element.Text),
        PresentElementKind.Media => "[Media preserved in 9to1] " + FirstNonEmpty(element.AlternativeText, element.Text, element.AssetId),
        PresentElementKind.Group => "[9to1 object group]",
        _ => "[9to1 content preserved in native presentation]"
    };

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static long ToEmu(double normalized, long total) =>
        (long)Math.Round(Math.Clamp(double.IsFinite(normalized) ? normalized : 0, 0, 1) * total);

    private static string SlideRelationships(int number) => $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"{R}/slideLayout\" Target=\"../slideLayouts/slideLayout1.xml\"/><Relationship Id=\"rId2\" Type=\"{R}/notesSlide\" Target=\"../notesSlides/notesSlide{number}.xml\"/></Relationships>";

    private static async Task WriteEntryAsync(
        ZipArchive archive, string path, string content, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: false);
        await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ValidatePackage(string path, int slideCount)
    {
        using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var required = new[]
        {
            "[Content_Types].xml",
            "_rels/.rels",
            "ppt/presentation.xml",
            "ppt/_rels/presentation.xml.rels",
            "ppt/slideMasters/slideMaster1.xml",
            "ppt/slideLayouts/slideLayout1.xml",
            "ppt/theme/theme1.xml",
            "ppt/notesMasters/notesMaster1.xml"
        };
        foreach (var part in required)
            ValidateXmlPart(archive, part);
        for (var index = 1; index <= slideCount; index++)
        {
            ValidateXmlPart(archive, $"ppt/slides/slide{index}.xml");
            ValidateXmlPart(archive, $"ppt/slides/_rels/slide{index}.xml.rels");
            ValidateXmlPart(archive, $"ppt/notesSlides/notesSlide{index}.xml");
            ValidateXmlPart(archive, $"ppt/notesSlides/_rels/notesSlide{index}.xml.rels");
        }
    }

    private static void ValidateXmlPart(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidDataException($"The PPTX package is missing {path}.");
        using var stream = entry.Open();
        _ = XDocument.Load(stream, LoadOptions.None);
    }

    private static string Xml(string? value) => SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
