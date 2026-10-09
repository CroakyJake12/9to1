using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Haven.UI;

namespace HavenOS.Apps.Assistants.NativeUI;

/// <summary>Native rasterisation of the maintained canonical icon catalogue.
/// Text is the CUI-owned icon-key binding; it is never parsed as a path or URI.
/// This control opens no asset, store, network source or alternate UI scene.</summary>
public sealed class AssistantAvatarIcon : TextBlock
{
    public static readonly StyledProperty<IBrush?> BadgeBrushProperty =
        AvaloniaProperty.Register<AssistantAvatarIcon, IBrush?>(nameof(BadgeBrush));
    public IBrush? BadgeBrush { get => GetValue(BadgeBrushProperty); set => SetValue(BadgeBrushProperty, value); }
    static AssistantAvatarIcon() => AffectsRender<AssistantAvatarIcon>(BadgeBrushProperty);
    public AssistantAvatarIcon()
    {
        Bind(BadgeBrushProperty, this.GetResourceObservable("HavenAccentBrush"));
        Bind(ForegroundProperty, this.GetResourceObservable("HavenAccentInkBrush"));
        IsHitTestVisible = false;
    }
    public string IconKey => AssistantAvatarCatalogue.DisplayKey(Text);
    protected override Size MeasureOverride(Size availableSize) => new(48, 48);
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty) InvalidateVisual();
    }
    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0 || !double.IsFinite(size)) return;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        context.DrawEllipse(BadgeBrush, null, center, size / 2, size / 2);
        var source = HavenIconCatalog.Resolve(IconKey);
        var box = source.ViewBox ?? new HavenRect(0, 0, 24, 24);
        var scale = size * .60 / Math.Max(box.Width, box.Height);
        Point Map(HavenPoint point) => new(center.X + (point.X - box.X - box.Width / 2) * scale,
            center.Y + (point.Y - box.Y - box.Height / 2) * scale);
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.SetFillRule(source.Path.FillRule == HavenFillRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd);
            foreach (var figure in source.Path.Figures)
            {
                path.BeginFigure(Map(figure.Start), false);
                foreach (var segment in figure.Segments)
                {
                    switch (segment)
                    {
                        case HavenLineSegment line: path.LineTo(Map(line.End), true); break;
                        case HavenQuadraticBezierSegment quadratic:
                            path.QuadraticBezierTo(Map(quadratic.Control), Map(quadratic.End), true); break;
                        case HavenCubicBezierSegment cubic:
                            path.CubicBezierTo(Map(cubic.Control1), Map(cubic.Control2), Map(cubic.End), true); break;
                        case HavenArcSegment arc:
                            path.ArcTo(Map(arc.End), new Size(arc.Radius.Width * scale, arc.Radius.Height * scale),
                                arc.RotationDegrees, arc.IsLargeArc,
                                arc.SweepDirection == HavenSweepDirection.Clockwise ? SweepDirection.Clockwise : SweepDirection.CounterClockwise, true); break;
                        default: throw new InvalidOperationException("The canonical icon contains an unsupported path segment.");
                    }
                }
                path.EndFigure(figure.Closed);
            }
        }
        context.DrawGeometry(null, new Pen(Foreground, Math.Max(1.5, size / 24)), geometry);
    }
}
