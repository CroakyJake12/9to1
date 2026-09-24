namespace HavenOS.Images;

/// <summary>
/// Holds the zoom and pan applied to the Pictures image preview.
/// </summary>
public sealed class ImageViewportState
{
    public const double MinimumScale = 0.25;
    public const double MaximumScale = 8;

    public double Scale { get; private set; } = 1;
    public double OffsetX { get; private set; }
    public double OffsetY { get; private set; }

    public bool CanZoomOut => Scale > MinimumScale;
    public bool CanZoomIn => Scale < MaximumScale;
    public bool IsDefault => Scale == 1 && OffsetX == 0 && OffsetY == 0;

    public bool ZoomAt(double factor, double anchorX, double anchorY, double viewportWidth, double viewportHeight)
    {
        if (!double.IsFinite(factor) || factor <= 0
            || !double.IsFinite(anchorX) || !double.IsFinite(anchorY)
            || !double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight)
            || viewportWidth <= 0 || viewportHeight <= 0)
            return false;

        var nextScale = Math.Clamp(Scale * factor, MinimumScale, MaximumScale);
        if (nextScale == Scale)
            return false;

        var ratio = nextScale / Scale;
        var centerX = viewportWidth / 2;
        var centerY = viewportHeight / 2;
        var nextOffsetX = anchorX - centerX - (anchorX - centerX - OffsetX) * ratio;
        var nextOffsetY = anchorY - centerY - (anchorY - centerY - OffsetY) * ratio;
        if (!double.IsFinite(nextOffsetX) || !double.IsFinite(nextOffsetY))
            return false;

        OffsetX = nextOffsetX;
        OffsetY = nextOffsetY;
        Scale = nextScale;
        return true;
    }

    public bool PanBy(double deltaX, double deltaY)
    {
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY)
            || (deltaX == 0 && deltaY == 0))
            return false;

        var nextOffsetX = OffsetX + deltaX;
        var nextOffsetY = OffsetY + deltaY;
        if (!double.IsFinite(nextOffsetX) || !double.IsFinite(nextOffsetY))
            return false;

        OffsetX = nextOffsetX;
        OffsetY = nextOffsetY;
        return true;
    }

    public bool Reset()
    {
        if (IsDefault)
            return false;

        Scale = 1;
        OffsetX = 0;
        OffsetY = 0;
        return true;
    }
}
