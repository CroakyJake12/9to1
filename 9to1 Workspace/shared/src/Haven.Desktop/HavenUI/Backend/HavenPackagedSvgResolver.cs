using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using System.Xml.Linq;

namespace Haven.Desktop.HavenUI.Backend;

/// <summary>
/// A narrow, strict packaged-SVG path/rect asset decoder for the typed CUI image-source seam. It
/// accepts only the genuine vector artwork used by authored surfaces (currently the enlarged rounded
/// plus), parsing SVG path and rounded-rect geometry with the pinned backend
/// <see cref="StreamGeometry"/> / <see cref="RectangleGeometry"/> primitives. Unsupported SVG
/// features (scripts, external entities/hrefs, gradients, embedded images, anything else) are
/// rejected loudly rather than partially rendered. Foreground is resolved from the scoped CUI text
/// brush at each resolve so live theme changes retheme the icon.
/// </summary>
public sealed class HavenPackagedSvgResolver
{
    private const string CurrentColor = "currentColor";

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
            document = XDocument.Load(stream, LoadOptions.None);
        }
        catch (Exception error) when (error is System.Xml.XmlException or InvalidOperationException)
        {
            throw new InvalidOperationException("A packaged CUI SVG asset is not well-formed XML.", error);
        }

        var root = document.Root
            ?? throw new InvalidOperationException("A packaged CUI SVG asset has no root element.");
        if (!root.Name.LocalName.Equals("svg", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"A packaged CUI SVG asset has unexpected root '{root.Name.LocalName}'.");

        var viewBox = ParseViewBox(root);
        var group = new GeometryGroup();
        foreach (var element in root.Elements())
        {
            switch (element.Name.LocalName.ToLowerInvariant())
            {
                case "path":
                    group.Children.Add(ParsePath(element));
                    break;
                case "rect":
                    group.Children.Add(ParseRect(element));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported SVG element '{element.Name.LocalName}' in packaged CUI asset; only path and rect are accepted.");
            }
        }

        // Scoped theme foreground, resolved live at each resolve so theme changes retheme the icon.
        if (!target.TryFindResource("CuiTextBrush", out var resource) || resource is not IBrush brush)
            throw new InvalidOperationException("The packaged SVG's scoped CUI text brush is unavailable.");

        return new DrawingImage(new GeometryDrawing { Geometry = group, Brush = brush })
        {
            Viewbox = viewBox
        };
    }

    private static Rect ParseViewBox(XElement root)
    {
        var value = root.Attribute("viewBox")?.Value ?? root.Attribute("viewbox")?.Value;
        if (string.IsNullOrWhiteSpace(value)) return new Rect(0, 0, 32, 32);
        var parts = value.Split([' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || !double.TryParse(parts[0], out var x) || !double.TryParse(parts[1], out var y)
            || !double.TryParse(parts[2], out var width) || !double.TryParse(parts[3], out var height)
            || width <= 0 || height <= 0 || !double.IsFinite(x) || !double.IsFinite(y))
            throw new InvalidOperationException("A packaged CUI SVG viewBox must contain four finite numbers with positive width/height.");
        return new Rect(x, y, width, height);
    }

    private static Geometry ParsePath(XElement element)
    {
        EnsureCurrentColorOnly(element);
        RejectForbiddenAttributes(element);
        var d = element.Attribute("d")?.Value
            ?? throw new InvalidOperationException("An SVG path in a packaged CUI asset requires a d attribute.");
        if (string.IsNullOrWhiteSpace(d)) throw new InvalidOperationException("An SVG path d in a packaged CUI asset is empty.");
        return StreamGeometry.Parse(d);
    }

    private static Geometry ParseRect(XElement element)
    {
        EnsureCurrentColorOnly(element);
        RejectForbiddenAttributes(element);
        static double Attr(XElement e, string name, double fallback, bool required = false)
        {
            var attr = e.Attribute(name);
            if (attr is null)
            {
                if (required) throw new InvalidOperationException($"An SVG rect in a packaged CUI asset requires a '{name}' attribute.");
                return fallback;
            }
            if (!double.TryParse(attr.Value, out var parsed) || !double.IsFinite(parsed))
                throw new InvalidOperationException($"An SVG rect '{name}' in a packaged CUI asset must be finite.");
            return parsed;
        }
        var x = Attr(element, "x", 0);
        var y = Attr(element, "y", 0);
        var width = Attr(element, "width", 0, required: true);
        var height = Attr(element, "height", 0, required: true);
        if (width <= 0 || height <= 0) throw new InvalidOperationException("An SVG rect in a packaged CUI asset must have positive width/height.");
        var rx = Attr(element, "rx", 0);
        var ry = Attr(element, "ry", 0);
        return new RectangleGeometry(new Rect(x, y, width, height), Math.Max(0, rx), Math.Max(0, ry));
    }

    private static void EnsureCurrentColorOnly(XElement element)
    {
        foreach (var name in new[] { "fill", "stroke" })
        {
            var attr = element.Attribute(name);
            if (attr is not null && !attr.Value.Trim().Equals(CurrentColor, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"A packaged CUI SVG '{name}' must be 'currentColor'; other colors/gradients are rejected.");
        }
    }

    private static void RejectForbiddenAttributes(XElement element)
    {
        foreach (var attr in element.Attributes())
        {
            var name = attr.Name.LocalName.ToLowerInvariant();
            if (name is "onload" or "onclick" or "style" or "href" or "xlink:href" or "src" or "class")
                throw new InvalidOperationException($"Unsupported SVG attribute '{attr.Name.LocalName}' in packaged CUI asset.");
        }
    }
}
