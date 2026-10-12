using System.Collections.Frozen;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace CakeOS.Cui.Runtime;

/// <summary>Internal backend resource ownership for one resolved CUI palette.</summary>
internal sealed class CuiThemeResourceProvider(FrozenDictionary<object, object?> snapshot) : ResourceProvider
{
    private Dictionary<LinearGradientBrush, LinearGradientBrush>? _gradients;
    private Dictionary<DrawingBrush, DrawingBrush>? _patterns;

    private static Drawing CloneDrawing(Drawing? drawing) => drawing switch
    {
        DrawingGroup group => CloneGroup(group),
        GeometryDrawing geometry => new GeometryDrawing(geometry.Brush, geometry.Pen, geometry.Geometry is { } shape ? CloneGeometry(shape) : null),
        _ => throw new InvalidOperationException("A custom pattern must contain only declarative geometry.")
    };
    private static Geometry CloneGeometry(Geometry geometry)
    {
        if (geometry is not GeometryGroup group) return geometry.Clone();
        var copy = new GeometryGroup { FillRule = group.FillRule };
        foreach (var child in group.Children) copy.Children.Add(CloneGeometry(child));
        return copy;
    }
    private static DrawingGroup CloneGroup(DrawingGroup group)
    {
        var copy = new DrawingGroup { Opacity = group.Opacity };
        foreach (var child in group.Children) copy.Children.Add(CloneDrawing(child));
        return copy;
    }

    public override bool HasResources => snapshot.Count > 0;

    public override bool TryGetResource(object key, ThemeVariant? theme, out object? value)
    {
        if (!snapshot.TryGetValue(key, out value)) return false;
        if (value is DrawingBrush pattern)
        {
            _patterns ??= new(ReferenceEqualityComparer.Instance);
            if (!_patterns.TryGetValue(pattern, out var copy))
            {
                copy = new DrawingBrush(CloneDrawing(pattern.Drawing))
                {
                    TileMode = pattern.TileMode, Stretch = pattern.Stretch, AlignmentX = pattern.AlignmentX,
                    AlignmentY = pattern.AlignmentY, SourceRect = pattern.SourceRect, DestinationRect = pattern.DestinationRect,
                    Opacity = pattern.Opacity, TransformOrigin = pattern.TransformOrigin,
                    Transform = pattern.Transform is { } transform ? new MatrixTransform(transform.Value) : null
                };
                _patterns.Add(pattern, copy);
            }
            value = copy;
            return true;
        }
        if (value is not LinearGradientBrush template) return true;

        // A renderer can mutate a native gradient. Share immutable palette values,
        // but retain a separate mutable brush per control, including its aliases.
        _gradients ??= new(ReferenceEqualityComparer.Instance);
        if (!_gradients.TryGetValue(template, out var owned))
        {
            owned = new LinearGradientBrush
            {
                StartPoint = template.StartPoint, EndPoint = template.EndPoint,
                SpreadMethod = template.SpreadMethod, Opacity = template.Opacity,
                Transform = template.Transform, TransformOrigin = template.TransformOrigin
            };
            foreach (var stop in template.GradientStops)
                owned.GradientStops.Add(new GradientStop(stop.Color, stop.Offset));
            _gradients.Add(template, owned);
        }
        value = owned;
        return true;
    }
}
