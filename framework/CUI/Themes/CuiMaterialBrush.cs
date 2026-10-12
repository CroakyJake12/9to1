using Avalonia.Media;
using Avalonia;

namespace CakeOS.Cui.Themes;

/// <summary>An immutable CUI background material consumed by the CUI surface adapter.</summary>
public sealed class CuiMaterialBrush : IImmutableSolidColorBrush, IEquatable<CuiMaterialBrush>
{
    public Color Color => Colors.Transparent;
    public double Opacity => 1;
    public ITransform? Transform => null;
    public RelativePoint TransformOrigin => default;
    public Color Tint { get; }
    public double TintOpacity { get; }
    public double BlurRadius { get; }
    public double GrainOpacity { get; }
    public bool TonalBase { get; }

    public CuiMaterialBrush(Color tint, double tintOpacity, double blurRadius, double grainOpacity = 0, bool tonalBase = false)
    {
        if (!double.IsFinite(tintOpacity) || tintOpacity is < 0 or > 1
            || !double.IsFinite(blurRadius) || blurRadius is < 0 or > 32
            || !double.IsFinite(grainOpacity) || grainOpacity is < 0 or > .05)
            throw new ArgumentOutOfRangeException(nameof(tintOpacity), "Material values must be finite and inside their supported ranges.");
        Tint = tint; TintOpacity = tintOpacity; BlurRadius = blurRadius; GrainOpacity = grainOpacity; TonalBase = tonalBase;
    }

    public override bool Equals(object? other) => other is CuiMaterialBrush material && Equals(material);
    public bool Equals(CuiMaterialBrush? material) => material is not null && Tint == material.Tint
        && TintOpacity == material.TintOpacity && BlurRadius == material.BlurRadius
        && GrainOpacity == material.GrainOpacity && TonalBase == material.TonalBase;
    public override int GetHashCode() => HashCode.Combine(Tint, TintOpacity, BlurRadius, GrainOpacity, TonalBase);
}
