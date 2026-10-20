using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Haven.Desktop.HavenUI.Backend;

/// <summary>
/// A narrow, strict packaged-SVG path/rect asset decoder for the typed CUI image-source seam. It
/// accepts only the genuine vector artwork used by authored surfaces (currently the enlarged rounded
/// plus), parsing SVG path and rounded-rect geometry with the pinned backend
/// <see cref="StreamGeometry"/> / <see cref="RectangleGeometry"/> primitives. The decoder is
/// truthful: unsupported or unrendered SVG features (scripts, external entities/hrefs, gradients,
/// embedded images, transforms, stroke, opacity, fill-rule, unknown attributes, nested non-white
/// content, anything else) are rejected loudly rather than partially rendered. Foreground is
/// resolved from the scoped CUI text brush at each resolve so live theme changes retheme the icon.
/// </summary>
public sealed class HavenPackagedSvgResolver
{
    private const string SvgNamespace = "http://www.w3.org/2000/svg";
    private const string CurrentColor = "currentColor";
    private const NumberStyles NumberStyle = NumberStyles.Float;

    /// <summary>The single resolver delegate handed to the CUI registry.</summary>
    public static Func<string, Control, IImage?> Resolve { get; } = (source, target) => TryResolve(source, target, out var image) ? image : null;

    public static bool TryResolve(string source, Control target, out IImage? image)
    {
        image = null;
        if (string.IsNullOrWhiteSpace(source) || !source.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("avares" or "resm"))
            return false;
        using var stream = AssetLoader.Open(uri);
        image = Build(stream, target);
        return true;
    }

    private static DrawingImage Build(Stream stream, Control target)
    {
        XDocument document;
        try
        {
            // Explicit DTD prohibition and a null resolver: external entities/DTDs are never read.
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(stream, settings);
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (Exception error) when (error is XmlException or InvalidOperationException)
        {
            throw new InvalidOperationException("A packaged CUI SVG asset is not well-formed XML.", error);
        }

        var root = document.Root
            ?? throw new InvalidOperationException("A packaged CUI SVG asset has no root element.");
        // Exact SVG root identity: canonical namespace and exact local name.
        if (root.Name != XName.Get("svg", SvgNamespace))
            throw new InvalidOperationException($"A packaged CUI SVG asset has unexpected root '{root.Name}'.");

        ValidateAttributes(root);
        var viewBox = ParseViewBox(root);

        var geometries = new List<Geometry>();
        foreach (var element in root.Elements())
        {
            if (element.Name.NamespaceName != SvgNamespace)
                throw new InvalidOperationException($"An SVG child element '{element.Name}' is not in the SVG namespace.");
            if (element.Elements().Any())
                throw new InvalidOperationException($"An SVG '{element.Name.LocalName}' element in a packaged CUI asset must not contain nested elements.");
            switch (element.Name.LocalName)
            {
                case "path":
                    geometries.Add(ParsePath(element));
                    break;
                case "rect":
                    geometries.Add(ParseRect(element));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported SVG element '{element.Name.LocalName}' in packaged CUI asset; only path and rect are accepted.");
            }
        }

        // Reject any non-whitespace text/comment-bearing content anywhere in the SVG.
        foreach (var text in root.DescendantNodes().OfType<XText>())
        {
            if (!string.IsNullOrWhiteSpace(text.Value))
                throw new InvalidOperationException("A packaged CUI SVG asset contains unsupported text content.");
        }

        // Scoped theme foreground, resolved live at each resolve so theme changes retheme the icon.
        if (!target.TryFindResource("CuiTextBrush", out var resource) || resource is not IBrush brush)
            throw new InvalidOperationException("The packaged SVG's scoped CUI text brush is unavailable.");

        // Per-element painting: every path/rect becomes its own GeometryDrawing carrying its own
        // geometry, so overlapping shapes paint independently (as a renderer would) and cannot
        // cancel one another via cross-element opposite-winding interaction.
        var group = new DrawingGroup();
        foreach (var geometry in geometries)
            group.Children.Add(new GeometryDrawing { Geometry = geometry, Brush = brush });

        return new DrawingImage(group)
        {
            Viewbox = viewBox
        };
    }

    /// <summary>
    /// Strict per-element attribute allowlist. Root permits only namespace declarations, viewBox and
    /// fill="currentColor"; path/rect permit only their geometry attributes plus fill="currentColor".
    /// Anything else (transform, stroke, opacity, fill-rule, style, class, href, event handlers,
    /// unknown attributes) is rejected because the decoder does not render those features.
    /// </summary>
    private static void ValidateAttributes(XElement element)
    {
        foreach (var attr in element.Attributes())
        {
            if (attr.IsNamespaceDeclaration) continue;
            // Reject foreign namespaced attributes (for example f:x / f:viewBox) by their
            // namespace rather than accepting the local name and looking it up plainly.
            if (attr.Name.Namespace != XNamespace.None)
                throw new InvalidOperationException($"Unsupported namespaced SVG attribute '{attr.Name}' on element '{element.Name.LocalName}' in packaged CUI asset.");
            var local = attr.Name.LocalName;
            var name = element.Name.LocalName;
            var allowed = name switch
            {
                "svg" => local.Equals("viewBox", StringComparison.Ordinal) || local.Equals("viewbox", StringComparison.Ordinal),
                "rect" => local is "x" or "y" or "width" or "height" or "rx" or "ry",
                "path" => local.Equals("d", StringComparison.Ordinal),
                _ => false
            };
            if (local.Equals("fill", StringComparison.Ordinal))
            {
                if (!attr.Value.Trim().Equals(CurrentColor, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A packaged CUI SVG 'fill' must be 'currentColor'; other colors/gradients are rejected.");
                continue;
            }
            if (!allowed)
                throw new InvalidOperationException($"Unsupported SVG attribute '{attr.Name.LocalName}' on element '{name}' in packaged CUI asset.");
        }
    }

    private static Rect ParseViewBox(XElement root)
    {
        var attr = root.Attribute("viewBox") ?? root.Attribute("viewbox");
        if (attr is null) return new Rect(0, 0, 32, 32);
        if (string.IsNullOrWhiteSpace(attr.Value)) return new Rect(0, 0, 32, 32);
        var parts = attr.Value.Split([' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4
            || !TryParseFinite(parts[0], out var x) || !TryParseFinite(parts[1], out var y)
            || !TryParseFinite(parts[2], out var width) || !TryParseFinite(parts[3], out var height)
            || width <= 0 || height <= 0)
            throw new InvalidOperationException("A packaged CUI SVG viewBox must contain four finite numbers with positive width/height.");
        return new Rect(x, y, width, height);
    }

    private static Geometry ParsePath(XElement element)
    {
        ValidateAttributes(element);
        var d = element.Attribute("d")?.Value
            ?? throw new InvalidOperationException("An SVG path in a packaged CUI asset requires a d attribute.");
        if (string.IsNullOrWhiteSpace(d)) throw new InvalidOperationException("An SVG path d in a packaged CUI asset is empty.");
        try
        {
            return StreamGeometry.Parse(d);
        }
        catch (Exception error) when (error is FormatException or NotSupportedException)
        {
            throw new InvalidOperationException("An SVG path d in a packaged CUI asset is invalid.", error);
        }
    }

    private static Geometry ParseRect(XElement element)
    {
        ValidateAttributes(element);
        static double Attr(XElement e, string name, double fallback, bool required = false)
        {
            var attr = e.Attribute(name);
            if (attr is null)
            {
                if (required) throw new InvalidOperationException($"An SVG rect in a packaged CUI asset requires a '{name}' attribute.");
                return fallback;
            }
            if (!TryParseFinite(attr.Value, out var parsed))
                throw new InvalidOperationException($"An SVG rect '{name}' in a packaged CUI asset must be finite.");
            return parsed;
        }
        var x = Attr(element, "x", 0);
        var y = Attr(element, "y", 0);
        var width = Attr(element, "width", 0, required: true);
        var height = Attr(element, "height", 0, required: true);
        if (width <= 0 || height <= 0) throw new InvalidOperationException("An SVG rect in a packaged CUI asset must have positive width/height.");
        // Per SVG, a missing rx/ry inherits the other; negative radii are an error; radii are
        // clamped to half the box so the rounded end never exceeds the rect's extent.
        var rxAttr = element.Attribute("rx");
        var ryAttr = element.Attribute("ry");
        var rx = rxAttr is null ? 0d : Attr(element, "rx", 0);
        var ry = ryAttr is null ? 0d : Attr(element, "ry", 0);
        if (rxAttr is null && ryAttr is not null) rx = ry;
        else if (ryAttr is null && rxAttr is not null) ry = rx;
        if (rx < 0 || ry < 0)
            throw new InvalidOperationException("An SVG rect in a packaged CUI asset must not have a negative rounding radius.");
        rx = Math.Min(rx, width / 2);
        ry = Math.Min(ry, height / 2);
        return new RectangleGeometry(new Rect(x, y, width, height), rx, ry);
    }

    private static bool TryParseFinite(string text, out double value)
    {
        value = 0;
        return double.TryParse(text, NumberStyle, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }
}
