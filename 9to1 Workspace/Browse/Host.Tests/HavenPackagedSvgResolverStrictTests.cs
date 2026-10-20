using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Haven.Desktop.HavenUI.Backend;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Xml.Linq;
using Xunit;

namespace HavenOS.Browse.Host.Tests;

/// <summary>
/// Focused strictness coverage for <see cref="HavenPackagedSvgResolver"/>: per-element painting with
/// the centre filled, rx/ry inheritance, negative/foreign/empty/unsupported rejection and
/// invariant finite numeric parsing under a comma-decimal culture. These tests exercise the real
/// decoder/ (including the XmlReader DTD-prohibit/null-resolver settings) via the private parsing
/// surface so malformed packaged assets can be fed directly without embedding resource assets.
/// </summary>
public sealed class HavenPackagedSvgResolverStrictTests
{
    private const string SvgNs = "http://www.w3.org/2000/svg";
    private static readonly Type ResolverType = typeof(HavenPackagedSvgResolver);

    private static object InvokeStatic(string name, params object[] args)
    {
        try
        {
            return ResolverType.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, args)!;
        }
        catch (TargetInvocationException tie)
        {
            ExceptionDispatchInfo.Capture(tie.InnerException!).Throw();
            throw;
        }
    }

    private static XElement Svg(params object[] content)
    {
        var ns = XNamespace.Get(SvgNs);
        return new XElement(ns + "svg",
            new XAttribute("xmlns", SvgNs),
            content);
    }

    private static XElement SvgRect(params object[] attributes)
    {
        var ns = XNamespace.Get(SvgNs);
        return new XElement(ns + "rect", attributes);
    }

    private static XElement ParseRectElement(string rectXml) => XElement.Parse(rectXml);

    private static DrawingImage Build(string xml) => BuildWithForeground(xml, Brushes.Red);

    private static DrawingImage BuildWithForeground(string xml, IBrush brush)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var target = new Button();
        target.Resources["CuiTextBrush"] = brush;
        return (DrawingImage)InvokeStatic("Build", stream, target);
    }

    [AvaloniaFact]
    public void Plus_paints_per_element_and_centre_is_filled()
    {
        var drawing = Build("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32" fill="currentColor">
              <rect x="12.5" y="3" width="7" height="26" rx="3.5"/>
              <rect x="3" y="12.5" width="26" height="7" rx="3.5"/>
            </svg>
            """);

        Assert.Equal(new Rect(0, 0, 32, 32), drawing.Viewbox);
        var group = Assert.IsType<DrawingGroup>(drawing.Drawing);
        Assert.Equal(2, group.Children.Count);

        // Each child is its own GeometryDrawing (per-element painting) with the scoped brush.
        // Both rounded bars cover the centre, so even with distinct windings FillContains(16,16)
        // must be true for every element (no cross-element cancellation).
        foreach (var child in group.Children)
        {
            var geometryDrawing = Assert.IsType<GeometryDrawing>(child);
            Assert.NotNull(geometryDrawing.Brush);
            Assert.NotNull(geometryDrawing.Geometry);
            Assert.True(geometryDrawing.Geometry.FillContains(new Point(16, 16)));
        }

        // The overall painted bounds (union of per-element geometry bounds) span the centre
        // without clipping the plus.
        var first = true;
        var union = default(Rect);
        foreach (var child in group.Children)
        {
            var bounds = Assert.IsType<GeometryDrawing>(child).Geometry!.Bounds;
            union = first ? bounds : union.Union(bounds);
            first = false;
        }
        Assert.True(union.Contains(new Point(16, 16)));
    }

    [Fact]
    public void Rect_missing_ry_inherits_rx_and_vice_versa()
    {
        object ParseRect(string rectXml) => InvokeStatic("ParseRect", ParseRectElement(rectXml));

        var rxOnly = Assert.IsType<RectangleGeometry>(ParseRect("<rect x='0' y='0' width='32' height='32' rx='4'/>"));
        Assert.Equal(4, rxOnly.RadiusX);
        Assert.Equal(4, rxOnly.RadiusY); // ry inherits rx

        var ryOnly = Assert.IsType<RectangleGeometry>(ParseRect("<rect x='0' y='0' width='32' height='32' ry='5'/>"));
        Assert.Equal(5, ryOnly.RadiusX); // rx inherits ry
        Assert.Equal(5, ryOnly.RadiusY);
    }

    [Fact]
    public void Rect_radii_are_clamped_to_half_the_box()
    {
        var rect = Assert.IsType<RectangleGeometry>(InvokeStatic("ParseRect",
            ParseRectElement("<rect x='0' y='0' width='10' height='20' rx='9'/>")));
        Assert.Equal(5, rect.RadiusX); // clamp to width/2 (10/2)
        Assert.Equal(9, rect.RadiusY); // ry inherits rx=9, clamped to height/2 (20/2)
    }

    [Fact]
    public void Rect_negative_radius_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => InvokeStatic("ParseRect",
            ParseRectElement("<rect x='0' y='0' width='32' height='32' rx='-2'/>")));
    }

    [Fact]
    public void Foreign_namespaced_attribute_is_rejected()
    {
        var element = Svg(
            new XElement(XNamespace.Get(SvgNs) + "rect",
                new XAttribute(XNamespace.Get("urn:foreign") + "x", "1"),
                new XAttribute("y", "1"), new XAttribute("width", "10"), new XAttribute("height", "10")));
        Assert.Throws<InvalidOperationException>(() => InvokeStatic("ParseRect", element.Elements().Single()));
    }

    [Fact]
    public void Nested_path_rect_child_is_rejected()
    {
        var xml = """
            <svg xmlns="http://www.w3.org/2000/svg">
              <path d="M0 0h32v32h-32z"><rect width="1" height="1" /></path>
            </svg>
            """;
        Assert.Throws<InvalidOperationException>(() => Build(xml));
    }

    [Fact]
    public void Unsupported_element_is_rejected()
    {
        var xml = """
            <svg xmlns="http://www.w3.org/2000/svg"><circle cx="16" cy="16" r="8" /></svg>
            """;
        Assert.Throws<InvalidOperationException>(() => Build(xml));
    }

    [Fact]
    public void Unsupported_attribute_is_rejected()
    {
        var element = Svg(
            new XElement(XNamespace.Get(SvgNs) + "rect",
                new XAttribute("x", "0"), new XAttribute("y", "0"),
                new XAttribute("width", "10"), new XAttribute("height", "10"),
                new XAttribute("transform", "rotate(45)")));
        Assert.Throws<InvalidOperationException>(() => InvokeStatic("ParseRect", element.Elements().Single()));
    }

    [Fact]
    public void Non_currentcolor_fill_is_rejected()
    {
        var element = Svg(
            new XElement(XNamespace.Get(SvgNs) + "rect",
                new XAttribute("x", "0"), new XAttribute("y", "0"),
                new XAttribute("width", "10"), new XAttribute("height", "10"),
                new XAttribute("fill", "#ff0000")));
        Assert.Throws<InvalidOperationException>(() => InvokeStatic("ParseRect", element.Elements().Single()));
    }

    [Fact]
    public void Invalid_viewbox_is_rejected()
    {
        var xml = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="a b c d"><rect width="1" height="1" /></svg>
            """;
        Assert.Throws<InvalidOperationException>(() => Build(xml));
    }

    [Fact]
    public void Decimal_plus_parses_under_comma_decimal_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // Ensure a comma-decimal culture such that any culture-sensitive parse would break
            // on the authored "12.5" / "3.5" numbers; the decoder must use invariant parsing.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var drawing = Build("""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32" fill="currentColor">
                  <rect x="12.5" y="3" width="7" height="26" rx="3.5"/>
                  <rect x="3" y="12.5" width="26" height="7" rx="3.5"/>
                </svg>
                """);
            var group = Assert.IsType<DrawingGroup>(drawing.Drawing);
            foreach (var child in group.Children)
                Assert.NotNull(Assert.IsType<GeometryDrawing>(child).Geometry);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [AvaloniaFact]
    public void Same_direction_nested_subpaths_fill_center_per_svg_nonzero_default()
    {
        // SVG's default fill-rule is nonzero: nested same-direction subpaths fill their centre.
        // Avalonia's StreamGeometry mini-language defaults to evenodd, so the decoder must carry the
        // SVG nonzero default through. Under evenodd the inner square would cancel and leave the
        // centre empty; nonzero fills it.
        var drawing = Build("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32" fill="currentColor">
              <path d="M0 0H32V32H0Z M8 8H24V24H8Z"/>
            </svg>
            """);
        var group = Assert.IsType<DrawingGroup>(drawing.Drawing);
        var pathDraw = Assert.Single(group.Children.OfType<GeometryDrawing>());
        Assert.NotNull(pathDraw.Geometry);
        var geometry = pathDraw.Geometry;
        // Centre inside the inner square is filled (nonzero), not a hole.
        Assert.True(geometry.FillContains(new Point(16, 16)));
        // The ring between the squares is also filled; a point fully outside is not.
        Assert.True(geometry.FillContains(new Point(4, 16)));
        Assert.False(geometry.FillContains(new Point(-1, -1)));
    }

    [Fact]
    public void Path_non_svg_fill_rule_prefix_is_rejected()
    {
        var f1 = """<svg xmlns="http://www.w3.org/2000/svg"><path d="F1 M0 0H32V32H0Z"/></svg>""";
        Assert.Throws<InvalidOperationException>(() => Build(f1));
        var f0 = """<svg xmlns="http://www.w3.org/2000/svg"><path d="F0 M0 0H32V32H0Z"/></svg>""";
        Assert.Throws<InvalidOperationException>(() => Build(f0));
    }

    [Fact]
    public void Path_exponent_overflow_number_is_rejected()
    {
        var xml = """<svg xmlns="http://www.w3.org/2000/svg"><path d="M0 0L1e999 0H32V32H0Z"/></svg>""";
        Assert.Throws<InvalidOperationException>(() => Build(xml));
    }

    [Fact]
    public void Path_nonfinite_number_is_rejected()
    {
        var nan = """<svg xmlns="http://www.w3.org/2000/svg"><path d="M0 0NaN32V32H0Z"/></svg>""";
        Assert.Throws<InvalidOperationException>(() => Build(nan));
        var inf = """<svg xmlns="http://www.w3.org/2000/svg"><path d="M0 0LInfinity 0H32V32H0Z"/></svg>""";
        Assert.Throws<InvalidOperationException>(() => Build(inf));
    }

    [AvaloniaFact]
    public void Scoped_theme_foreground_is_resolved_live_per_build()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32" fill="currentColor">
              <path d="M0 0H32V32H0Z"/>
            </svg>
            """;
        var red = Brushes.Red;
        var blue = Brushes.Blue;
        var redImage = BuildWithForeground(svg, red);
        var blueImage = BuildWithForeground(svg, blue);
        IBrush SingleBrush(DrawingImage image) =>
            Assert.IsType<GeometryDrawing>(Assert.Single(Assert.IsType<DrawingGroup>(image.Drawing).Children)).Brush!;
        Assert.Same(red, SingleBrush(redImage));
        Assert.Same(blue, SingleBrush(blueImage));
        Assert.NotSame(red, blue);
    }
}
