using Avalonia;
using Avalonia.Media;

namespace CakeOS.Cui.Themes;

/// <summary>A validated spatial backdrop fill for canonical CUI surface backgrounds.</summary>
public sealed class CuiCustomMaterialBrush : IImmutableSolidColorBrush, IEquatable<CuiCustomMaterialBrush>
{
    public Color Color => Colors.Transparent;
    public double Opacity => 1;
    public ITransform? Transform => null;
    public RelativePoint TransformOrigin => default;
    public CuiCustomFill Fill { get; }

    public CuiCustomMaterialBrush(CuiCustomFill fill) => Fill = CuiCustomThemeContract.ValidateFill(fill);

    public bool Equals(CuiCustomMaterialBrush? other)
    {
        if (other is null || Fill.Kind != other.Fill.Kind) return false;
        return Fill.Kind switch
        {
            CuiCustomFillKind.SolidColour => Fill.Colour == other.Fill.Colour,
            CuiCustomFillKind.Pattern => Fill.Pattern == other.Fill.Pattern,
            CuiCustomFillKind.Gradient => Fill.Gradient is { } a && other.Fill.Gradient is { } b
                && a.Kind == b.Kind && a.Angle == b.Angle && a.CenterX == b.CenterX && a.CenterY == b.CenterY
                && a.Radius == b.Radius && a.Stops.SequenceEqual(b.Stops),
            _ => false
        };
    }
    public override bool Equals(object? other) => other is CuiCustomMaterialBrush material && Equals(material);
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(Fill.Kind);
        switch (Fill.Kind)
        {
            case CuiCustomFillKind.SolidColour: hash.Add(Fill.Colour); break;
            case CuiCustomFillKind.Pattern: hash.Add(Fill.Pattern); break;
            case CuiCustomFillKind.Gradient:
                var gradient = Fill.Gradient!; hash.Add(gradient.Kind); hash.Add(gradient.Angle);
                hash.Add(gradient.CenterX); hash.Add(gradient.CenterY); hash.Add(gradient.Radius);
                foreach (var stop in gradient.Stops) hash.Add(stop);
                break;
        }
        return hash.ToHashCode();
    }
}
