using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using CakeOS.Cui.Themes;
using SkiaSharp;

namespace CakeOS.Cui.Runtime;

/// <summary>One original-backdrop filter graph, with spatial weights for each authored colour.</summary>
internal sealed class CuiCustomMaterialOperation : ICustomDrawOperation
{
    private readonly Rect _rect;
    private readonly CornerRadius _radius;
    private readonly CuiCustomMaterialBrush _material;
    private readonly List<IDisposable> _owned = [];
    private readonly SKShader _tint;
    private readonly SKImageFilter? _backdrop;
    public Rect Bounds => _rect;

    public CuiCustomMaterialOperation(Rect rect, CornerRadius radius, CuiCustomMaterialBrush material)
    {
        _rect = rect; _radius = radius; _material = material;
        try
        {
            _tint = Own(CreateShader(Colour));
            var colours = ActiveColours().ToArray();
            if (colours.Any(colour => colour.BackgroundBlur > 0))
            {
                // Every null input below means the SAME original backdrop. We never
                // successively blur the output of another stop or copy a screenshot.
                foreach (var sigma in colours.Select(colour => colour.BackgroundBlur).Distinct())
                {
                    var mask = Own(CreateShader(colour => colour.BackgroundBlur == sigma ? SKColors.White : SKColors.Transparent));
                    var maskImage = Own(SKImageFilter.CreateShader(mask));
                    var blurred = sigma > 0 ? Own(SKImageFilter.CreateBlur((float)sigma, (float)sigma)) : null;
                    var weighted = Own(SKImageFilter.CreateBlendMode(SKBlendMode.DstIn, blurred, maskImage));
                    _backdrop = _backdrop is null ? weighted : Own(SKImageFilter.CreateBlendMode(SKBlendMode.Plus, _backdrop, weighted));
                }
            }
        }
        catch { Dispose(); throw; }
    }

    private T Own<T>(T value) where T : class, IDisposable
    { if (value is null) throw new InvalidOperationException("CUI_MATERIAL_UNSUPPORTED: the spatial material shader/filter could not be created."); _owned.Add(value); return value; }
    public bool HitTest(Point point) => false;
    public bool Equals(ICustomDrawOperation? other) => other is CuiCustomMaterialOperation operation
        && _rect == operation._rect && _radius == operation._radius && _material.Equals(operation._material);
    public void Dispose() { for (var index = _owned.Count - 1; index >= 0; index--) _owned[index].Dispose(); _owned.Clear(); }

    public void Render(ImmediateDrawingContext context)
    {
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>()
            ?? throw new InvalidOperationException("CUI_MATERIAL_UNSUPPORTED: the material requires the registered Skia drawing backend.");
        using var lease = feature.Lease(); var canvas = lease.SkCanvas;
        var rect = new SKRect(0, 0, (float)_rect.Width, (float)_rect.Height);
        using var rounded = new SKRoundRect();
        rounded.SetRectRadii(rect, [new((float)_radius.TopLeft, (float)_radius.TopLeft), new((float)_radius.TopRight, (float)_radius.TopRight),
            new((float)_radius.BottomRight, (float)_radius.BottomRight), new((float)_radius.BottomLeft, (float)_radius.BottomLeft)]);
        var save = canvas.Save();
        try
        {
            canvas.ClipRoundRect(rounded, SKClipOperation.Intersect, true);
            using var composite = new SKPaint { Color = new SKColor(255, 255, 255, (byte)Math.Round(255 * lease.CurrentOpacity)) };
            if (_backdrop is not null) canvas.SaveLayer(new SKCanvasSaveLayerRec { Bounds = rect, Backdrop = _backdrop, Paint = composite });
            else if (lease.CurrentOpacity < 1) canvas.SaveLayer(rect, composite);
            using var tint = new SKPaint { Shader = _tint, IsAntialias = true };
            canvas.DrawRect(rect, tint);
        }
        finally { canvas.RestoreToCount(save); }
    }

    private IEnumerable<CuiCustomColour> ActiveColours() => _material.Fill.Kind switch
    {
        CuiCustomFillKind.SolidColour => [_material.Fill.Colour],
        CuiCustomFillKind.Gradient => _material.Fill.Gradient!.Stops.Select(stop => stop.Colour),
        CuiCustomFillKind.Pattern => [_material.Fill.Pattern!.Foreground, _material.Fill.Pattern.Background],
        _ => throw new ArgumentOutOfRangeException(nameof(_material))
    };

    private SKShader CreateShader(Func<CuiCustomColour, SKColor> colour) => _material.Fill.Kind switch
    {
        CuiCustomFillKind.SolidColour => SKShader.CreateColor(colour(_material.Fill.Colour)),
        CuiCustomFillKind.Gradient => Gradient(_material.Fill.Gradient!, colour),
        CuiCustomFillKind.Pattern => Pattern(_material.Fill.Pattern!, colour),
        _ => throw new ArgumentOutOfRangeException(nameof(_material))
    };

    private SKShader Gradient(CuiCustomGradient value, Func<CuiCustomColour, SKColor> colour)
    {
        var colours = value.Stops.Select(stop => colour(stop.Colour)).ToArray();
        var positions = value.Stops.Select(stop => (float)stop.Offset).ToArray();
        var width = (float)_rect.Width; var height = (float)_rect.Height;
        var center = new SKPoint((float)value.CenterX * width, (float)value.CenterY * height);
        var angle = value.Angle * Math.PI / 180;
        return value.Kind switch
        {
            CuiCustomGradientKind.Linear => SKShader.CreateLinearGradient(
                new((float)(.5 - Math.Cos(angle) / 2) * width, (float)(.5 - Math.Sin(angle) / 2) * height),
                new((float)(.5 + Math.Cos(angle) / 2) * width, (float)(.5 + Math.Sin(angle) / 2) * height),
                colours, positions, SKShaderTileMode.Clamp),
            CuiCustomGradientKind.Radial => SKShader.CreateRadialGradient(center, (float)value.Radius * width,
                colours, positions, SKShaderTileMode.Clamp, SKMatrix.CreateScale(1, height / width, center.X, center.Y)),
            CuiCustomGradientKind.Conic => SKShader.CreateSweepGradient(center, colours, positions,
                SKMatrix.CreateRotationDegrees((float)value.Angle - 90, center.X, center.Y)),
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }

    private SKShader Pattern(CuiCustomPattern value, Func<CuiCustomColour, SKColor> colour)
    {
        var cell = (float)(value.Size + value.Spacing); var size = (float)value.Size;
        var extent = value.Kind == CuiCustomPatternKind.Checkerboard ? cell * 2 : cell;
        var tile = new SKRect(0, 0, extent, extent);
        using var recorder = new SKPictureRecorder(); var canvas = recorder.BeginRecording(tile);
        using var paint = new SKPaint { Color = colour(value.Background), BlendMode = SKBlendMode.Src, IsAntialias = true };
        canvas.DrawRect(tile, paint); paint.Color = colour(value.Foreground);
        switch (value.Kind)
        {
            case CuiCustomPatternKind.Stripes: canvas.DrawRect(0, 0, size, extent, paint); break;
            case CuiCustomPatternKind.Checkerboard:
                canvas.DrawRect(0, 0, size, size, paint); canvas.DrawRect(cell, cell, size, size, paint); break;
            case CuiCustomPatternKind.Dots: canvas.DrawCircle(cell / 2, cell / 2, size / 2, paint); break;
        }
        using var picture = recorder.EndRecording();
        return SKShader.CreatePicture(picture, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, SKFilterMode.Linear,
            SKMatrix.CreateRotationDegrees((float)value.Angle, (float)_rect.Width / 2, (float)_rect.Height / 2), tile);
    }

    private static SKColor Colour(CuiCustomColour value)
    {
        var colour = Color.Parse(value.Hex);
        return new SKColor(colour.R, colour.G, colour.B, (byte)Math.Round(255 * (1 - value.TransparencyPercent / 100)));
    }
}
