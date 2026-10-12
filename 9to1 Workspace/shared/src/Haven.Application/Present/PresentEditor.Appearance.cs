using Haven.Core;

namespace Haven.Application;

public sealed partial class PresentEditor
{
    /// <summary>Changes only the requested paint properties, preserving each object's
    /// other formatting and committing a mixed plain/vector selection as one edit.</summary>
    public bool SetSelectedShapeAppearance(string? fill = null, string? stroke = null, double? strokeWidth = null)
    {
        if (strokeWidth is { } width && (!double.IsFinite(width) || width < 0 || width > 10000)) return false;
        var selected = EditableSelection().Where(element => element.Kind == PresentElementKind.Shape).ToArray();
        if (selected.Length == 0 || fill is null && stroke is null && strokeWidth is null) return false;
        Mutate(() =>
        {
            foreach (var element in selected)
            {
                if (element.VectorShape is { } vector)
                {
                    foreach (var path in vector.Paths)
                    {
                        if (fill is not null) { path.Fill.Kind = fill.Length == 0 ? DocumentVectorFillKind.None : DocumentVectorFillKind.Solid; if (fill.Length > 0) path.Fill.Color = fill; }
                        if (stroke is not null)
                        {
                            path.Stroke.Enabled = stroke.Length > 0;
                            if (stroke.Length > 0) { path.Stroke.Color = stroke; if (path.Stroke.Width <= 0) path.Stroke.Width = 1; }
                        }
                        if (strokeWidth is { } vectorWidth) { path.Stroke.Width = vectorWidth; path.Stroke.Enabled = vectorWidth > 0; }
                    }
                }
                else
                {
                    if (fill is not null) element.Style.FillColor = fill.Length == 0 ? "#00000000" : fill;
                    if (stroke is not null)
                    {
                        element.Style.StrokeColor = stroke.Length == 0 ? "#00000000" : stroke;
                        element.Style.StrokeWidth = stroke.Length == 0 ? 0 : Math.Max(1, element.Style.StrokeWidth);
                    }
                    if (strokeWidth is { } plainWidth) element.Style.StrokeWidth = plainWidth;
                }
            }
        });
        return true;
    }
}
