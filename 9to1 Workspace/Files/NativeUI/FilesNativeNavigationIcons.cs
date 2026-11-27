using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace HavenOS.Files.NativeUI;

/// <summary>Uses maintained packaged Browse and shared line artwork, with the actual
/// Files control foreground. No filesystem, network or user-provided SVG is accepted.</summary>
internal static class FilesNativeNavigationIcons
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly ConditionalWeakTable<Control, NavigationInk> Inks = new();

    internal static IImage? Resolve(string source, Control target)
    {
        var asset = source switch
        {
            "files:back" or "files:up" => "nav-back",
            "files:forward" => "nav-forward",
            "files:refresh" => "nav-reload",
            "files:folder-outline" => "folder-outline",
            "files:close" => "close",
            _ => null
        };
        if (asset is null) return null;
        var outlined = source is "files:folder-outline" or "files:close";
        using var stream = typeof(FilesNativeNavigationIcons).Assembly.GetManifestResourceStream(
            "HavenOS.Files.NativeUI.Assets." + asset + ".svg")
            ?? throw new InvalidDataException("The maintained Files navigation artwork is unavailable.");
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8192
        });
        var root = XDocument.Load(reader).Root;
        if (root is null || root.Name != Svg + "svg" || (string?)root.Attribute("viewBox") != "0 0 24 24" ||
            (string?)root.Attribute("fill") != (outlined ? "none" : "currentColor") ||
            outlined && ((string?)root.Attribute("stroke") != "currentColor" ||
                (string?)root.Attribute("stroke-width") != "1.75" ||
                (string?)root.Attribute("stroke-linecap") != "round" || (string?)root.Attribute("stroke-linejoin") != "round") ||
            root.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration &&
                attribute.Name != "viewBox" && attribute.Name != "fill" &&
                !(outlined && (attribute.Name == "stroke" || attribute.Name == "stroke-width" ||
                    attribute.Name == "stroke-linecap" || attribute.Name == "stroke-linejoin"))))
            throw new InvalidDataException("The packaged navigation artwork has an unsupported shape.");
        var paths = root.Elements().ToArray();
        if (paths.Length != 1 || paths[0].Name != Svg + "path" || paths[0].HasElements ||
            paths[0].Attributes().Any(attribute => attribute.Name != "d") ||
            (string?)paths[0].Attribute("d") is not { Length: > 0 } data ||
            root.DescendantNodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
            throw new InvalidDataException("The packaged Files artwork requires its original single path.");
        var geometry = StreamGeometry.Parse(data);
        if (source == "files:up") geometry.Transform = new RotateTransform(90, 12, 12);
        if (!target.TryFindResource("CuiTextBrush", out var resource) || resource is not IBrush brush)
            throw new InvalidOperationException("The Files navigation foreground is unavailable.");
        var drawing = new GeometryDrawing { Geometry = geometry, Brush = outlined ? null : brush,
            Pen = outlined ? new Pen(brush, 1.75, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round) : null };
        Inks.GetValue(target, static control => new NavigationInk(control)).Set(drawing, brush);
        return new DrawingImage(drawing) { Viewbox = new Rect(0, 0, 24, 24) };
    }

    // Observe only this image, never retain its parent button or the Files page.
    // The inherited foreground notification also covers selected/contrast theme changes.
    private sealed class NavigationInk
    {
        private readonly Control _target;
        private GeometryDrawing? _drawing;
        private IBrush? _fallback;
        internal NavigationInk(Control target)
        {
            _target = target;
            target.PropertyChanged += Changed;
            target.AttachedToVisualTree += (_, _) => Apply();
            target.DetachedFromVisualTree += (_, _) => Apply();
        }
        internal void Set(GeometryDrawing drawing, IBrush fallback)
        { _drawing = drawing; _fallback = fallback; Apply(); }
        private void Changed(object? sender, AvaloniaPropertyChangedEventArgs change)
        { if (change.Property == TextElement.ForegroundProperty) Apply(); }
        private void Apply()
        {
            if (_drawing is null) return;
            var owner = _target.GetVisualAncestors().OfType<Avalonia.Controls.Primitives.TemplatedControl>()
                .FirstOrDefault(control => control is Button or ListBoxItem);
            var fallback = _target.TryFindResource("CuiTextBrush", out var value) && value is IBrush scoped ? scoped : _fallback;
            var brush = owner?.Foreground ?? fallback;
            if (_drawing.Pen is Pen pen) pen.Brush = brush;
            else _drawing.Brush = brush;
        }
    }
}
