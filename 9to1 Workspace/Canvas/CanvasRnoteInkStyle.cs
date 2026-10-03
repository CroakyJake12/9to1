using System.Globalization;
using System.Text.Json;
using Haven.Application;

namespace HavenOS.Apps.Canvas;

public enum CanvasRnoteInkKind { Solid, Marker }

/// <summary>Resolved properties supported by the actual native donor bridge; preset editing cannot alter earlier strokes.</summary>
public sealed record CanvasRnoteInkStyle(CanvasRnoteInkKind Kind = CanvasRnoteInkKind.Solid,
    string Color = "#FF000000", double BaseWidth = 2.5, double Opacity = 1)
{
    public static CanvasRnoteInkStyle Default { get; } = new();
    internal (uint Tool, double Red, double Green, double Blue, double Alpha) ValidateAndResolve()
    {
        if (!Enum.IsDefined(Kind) || !double.IsFinite(BaseWidth) || BaseWidth is < 0.5 or > 200 ||
            !double.IsFinite(Opacity) || Opacity is < 0 or > 1 || Color is null || Color.Length != 9 || Color[0] != '#' ||
            !uint.TryParse(Color.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var argb))
            throw new ArgumentException("Rnote ink requires supported solid/marker kind, #AARRGGBB colour, width 0.5..200 and opacity 0..1.");
        return (Kind == CanvasRnoteInkKind.Solid ? 0u : 1u, ((argb >> 16) & 255) / 255d,
            ((argb >> 8) & 255) / 255d, (argb & 255) / 255d, (argb >> 24) / 255d * Opacity);
    }

    internal CanvasBrushProperties BrushSnapshot()
    {
        ValidateAndResolve();
        return new()
        {
            EngineId = "rnote", Color = Color.ToUpperInvariant(), BaseWidth = BaseWidth, Opacity = Opacity,
            EngineParameters = new() { ["brushStyle"] = JsonSerializer.SerializeToElement(Kind == CanvasRnoteInkKind.Solid ? "solid" : "marker") }
        };
    }
}
