using Avalonia;
using Avalonia.Media;

namespace CakeOS.Cui.Themes;

/// <summary>The Atlas Glow field, resolved against the actual CUI surface bounds.</summary>
public sealed class CuiGlowFieldBrush(Color fieldTop, Color fieldBottom, Color light)
    : IImmutableSolidColorBrush, IEquatable<CuiGlowFieldBrush>
{
    // Ordinary native brushes still receive a quiet opaque fallback. The CUI
    // material adapter replaces it with the complete spatial field.
    public Color Color => FieldTop;
    public double Opacity => 1;
    public ITransform? Transform => null;
    public RelativePoint TransformOrigin => default;
    public Color FieldTop { get; } = fieldTop;
    public Color FieldBottom { get; } = fieldBottom;
    public Color Light { get; } = light;

    public DrawingBrush CreateBrush(Size size)
    {
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));

        // CSS linear-gradient(160deg): the gradient line reaches the projections
        // of the two far corners. Relative square endpoints would rotate the
        // field when the same shell is resized to a different aspect ratio.
        var angle = 160d * Math.PI / 180;
        var x = Math.Sin(angle);
        var y = -Math.Cos(angle);
        var half = (Math.Abs(size.Width * x) + Math.Abs(size.Height * y)) / 2;
        var field = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(size.Width / 2 - x * half, size.Height / 2 - y * half, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(size.Width / 2 + x * half, size.Height / 2 + y * half, RelativeUnit.Absolute),
            GradientStops = new GradientStops { new(FieldTop, 0), new(FieldBottom, 1) }
        };
        // CSS radial-gradient(ellipse at 17% 10%, color-mix(in srgb,
        // light 30%, transparent), transparent 64%).
        // The default farthest-corner ellipse keeps the farthest-side ratio.
        var haze = new RadialGradientBrush
        {
            Center = new RelativePoint(.17, .10, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(.17, .10, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(.83 * Math.Sqrt(2), RelativeUnit.Relative),
            RadiusY = new RelativeScalar(.90 * Math.Sqrt(2), RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new(Avalonia.Media.Color.FromArgb(77, Light.R, Light.G, Light.B), 0),
                new(Avalonia.Media.Color.FromArgb(0, Light.R, Light.G, Light.B), .64)
            }
        };
        var geometry = new RectangleGeometry(new Rect(size));
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing { Brush = field, Geometry = geometry });
        group.Children.Add(new GeometryDrawing { Brush = haze, Geometry = geometry });
        return new DrawingBrush(group) { Stretch = Stretch.Fill };
    }

    public bool Equals(CuiGlowFieldBrush? other) => other is not null &&
        FieldTop == other.FieldTop && FieldBottom == other.FieldBottom && Light == other.Light;
    public override bool Equals(object? other) => other is CuiGlowFieldBrush field && Equals(field);
    public override int GetHashCode() => HashCode.Combine(FieldTop, FieldBottom, Light);
}
