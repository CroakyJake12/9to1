using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using CakeOS.Cui.Themes;
using SkiaSharp;

namespace CakeOS.Cui.Runtime;

/// <summary>CUI's material surface adapter. Decoration never reparents its content or intercepts input.</summary>
public class CuiMaterialBorder : Border
{
    private MaterialLayer? _materialLayer;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != BackgroundProperty && change.Property != CornerRadiusProperty && change.Property != BorderThicknessProperty) return;
        if (Background is CuiMaterialBrush or CuiCustomMaterialBrush)
        {
            if (_materialLayer is null)
            {
                _materialLayer = new MaterialLayer { IsHitTestVisible = false, Focusable = false, ZIndex = int.MinValue };
                VisualChildren.Insert(0, _materialLayer);
            }
            _materialLayer.Material = Background; _materialLayer.Radius = CornerRadius;
            _materialLayer.IsVisible = true; _materialLayer.InvalidateVisual(); InvalidateArrange();
        }
        else if (_materialLayer is not null) _materialLayer.IsVisible = false;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        if (_materialLayer is not null)
        {
            var border = BorderThickness;
            var rect = new Rect(border.Left, border.Top, Math.Max(0, finalSize.Width - border.Left - border.Right), Math.Max(0, finalSize.Height - border.Top - border.Bottom));
            _materialLayer.Measure(rect.Size); _materialLayer.Arrange(rect);
        }
        return size;
    }

    private sealed class MaterialLayer : Control
    {
        public IBrush? Material;
        public CornerRadius Radius;
        public override void Render(DrawingContext context)
        {
            if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
            if (Material is CuiMaterialBrush material) context.Custom(new MaterialOperation(new Rect(Bounds.Size), Radius, material));
            else if (Material is CuiCustomMaterialBrush custom) context.Custom(new CuiCustomMaterialOperation(new Rect(Bounds.Size), Radius, custom));
        }
    }

    private sealed class MaterialOperation : ICustomDrawOperation
    {
        private static readonly Lazy<SKShader> Grain = new(CreateGrain);
        private readonly Rect _rect;
        private readonly CornerRadius _radius;
        private readonly CuiMaterialBrush _material;
        private readonly SKImageFilter? _blur;
        public Rect Bounds => _rect;
        public MaterialOperation(Rect rect, CornerRadius radius, CuiMaterialBrush material)
        {
            _rect = rect; _radius = radius; _material = material;
            if (material.BlurRadius > 0) _blur = SKImageFilter.CreateBlur((float)material.BlurRadius, (float)material.BlurRadius);
        }
        public bool HitTest(Point point) => false;
        public bool Equals(ICustomDrawOperation? other) => other is MaterialOperation operation
            && _rect == operation._rect && _radius == operation._radius && _material.Equals(operation._material);
        public void Dispose() => _blur?.Dispose();
        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null)
                throw new InvalidOperationException("CUI_MATERIAL_UNSUPPORTED: the material requires the registered Skia drawing backend.");
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            var rect = new SKRect(0, 0, (float)_rect.Width, (float)_rect.Height);
            using var rounded = new SKRoundRect();
            rounded.SetRectRadii(rect, [new((float)_radius.TopLeft, (float)_radius.TopLeft), new((float)_radius.TopRight, (float)_radius.TopRight),
                new((float)_radius.BottomRight, (float)_radius.BottomRight), new((float)_radius.BottomLeft, (float)_radius.BottomLeft)]);
            var save = canvas.Save();
            try
            {
                canvas.ClipRoundRect(rounded, SKClipOperation.Intersect, true);
                using var composite = new SKPaint { Color = new SKColor(255, 255, 255, (byte)Math.Round(255 * lease.CurrentOpacity)) };
                if (_blur is not null)
                    canvas.SaveLayer(new SKCanvasSaveLayerRec { Bounds = rect, Backdrop = _blur, Paint = composite });
                else if (lease.CurrentOpacity < 1) canvas.SaveLayer(rect, composite);
                var colour = _material.Tint;
                using var paint = new SKPaint { Color = new SKColor(colour.R, colour.G, colour.B, (byte)Math.Round(colour.A * _material.TintOpacity)), IsAntialias = true };
                canvas.DrawRect(rect, paint);
                if (_material.TonalBase)
                {
                    using var tone = SKShader.CreateLinearGradient(new(0, 0), new(rect.Width, rect.Height), [new SKColor(255, 255, 255, 6), new SKColor(0, 0, 0, 6)], [0f, 1f], SKShaderTileMode.Clamp);
                    paint.Shader = tone; paint.Color = SKColors.White; canvas.DrawRect(rect, paint);
                }
                if (_material.GrainOpacity > 0)
                {
                    paint.Shader = Grain.Value; paint.Color = new SKColor(255, 255, 255, (byte)Math.Round(255 * _material.GrainOpacity)); canvas.DrawRect(rect, paint);
                }
            }
            finally { canvas.RestoreToCount(save); }
        }
        private static SKShader CreateGrain()
        {
            using var bitmap = new SKBitmap(64, 64, SKColorType.Rgba8888, SKAlphaType.Opaque);
            var pixels = bitmap.GetPixelSpan(); var stride = bitmap.RowBytes; uint random = 0x9A1701;
            // SetPixel creates a native canvas/paint per pixel. Fill this one static
            // texture directly instead of allocating 4096 native drawing contexts.
            for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++)
            {
                random ^= random << 13; random ^= random >> 17; random ^= random << 5;
                var value = (byte)random; var offset = y * stride + x * 4;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = value; pixels[offset + 3] = 255;
            }
            bitmap.SetImmutable();
            return SKShader.CreateBitmap(bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
        }
    }
}
