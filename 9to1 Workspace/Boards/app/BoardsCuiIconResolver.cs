using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CakeOS.Apps.Boards.App;

/// <summary>Resolves the Boards-owned folder artwork used by authored CUI.</summary>
public static class BoardsCuiIconResolver
{
    public static IImage? Resolve(string source, Control target)
    {
        if (!string.Equals(source, "icon:folder", StringComparison.Ordinal)) return null;
        if (!target.TryFindResource("CuiTextBrush", out var resource) || resource is not IBrush brush)
            throw new InvalidOperationException("The Boards folder icon requires the scoped CUI text brush.");

        var folder = StreamGeometry.Parse("M 2 5 L 9 5 L 11 7 L 22 7 L 22 19 L 2 19 Z");
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing { Geometry = folder, Brush = brush });
        return new DrawingImage(group) { Viewbox = new Rect(0, 0, 24, 24) };
    }
}
