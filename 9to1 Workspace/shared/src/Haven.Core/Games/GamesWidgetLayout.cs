namespace Haven.Core.Games;

/// <summary>Pure responsive 2D layout geometry for UMG-style widgets.</summary>
public readonly record struct GamesWidgetLayout(
    double AnchorX,
    double AnchorY,
    double OffsetX,
    double OffsetY,
    double Width,
    double Height,
    double MinWidth,
    double MinHeight,
    GamesWidgetAlignment Alignment,
    bool SnapToGrid)
{
    public static GamesWidgetLayout Centered(double width, double height, double minWidth = 0, double minHeight = 0, bool snapToGrid = false)
        => new(0.5, 0.5, 0, 0, width, height, minWidth, minHeight, GamesWidgetAlignment.Center, snapToGrid);

    public static GamesWidgetLayout EdgeStretched(double width, double height, double minWidth = 0, double minHeight = 0, bool snapToGrid = false)
        => new(0, 0, 0, 0, width, height, minWidth, minHeight, GamesWidgetAlignment.Stretch, snapToGrid);

    public static GamesWidgetLayout Validate(GamesWidgetLayout layout)
    {
        if (!double.IsFinite(layout.AnchorX) || !double.IsFinite(layout.AnchorY))
            throw new ArgumentException("Anchors must be finite.");
        if (layout.AnchorX < 0 || layout.AnchorX > 1 || layout.AnchorY < 0 || layout.AnchorY > 1)
            throw new ArgumentException("Anchors must be within [0, 1].");
        if (!double.IsFinite(layout.OffsetX) || !double.IsFinite(layout.OffsetY))
            throw new ArgumentException("Offsets must be finite.");
        if (!double.IsFinite(layout.Width) || !double.IsFinite(layout.Height) || layout.Width < 0 || layout.Height < 0)
            throw new ArgumentException("Width and height must be finite and non-negative.");
        if (!double.IsFinite(layout.MinWidth) || !double.IsFinite(layout.MinHeight) || layout.MinWidth < 0 || layout.MinHeight < 0)
            throw new ArgumentException("Minimum size must be finite and non-negative.");
        if (layout.MinWidth > layout.Width || layout.MinHeight > layout.Height)
            throw new ArgumentException("Minimum size cannot exceed requested size.");
        if (!Enum.IsDefined(layout.Alignment))
            throw new ArgumentException("Unknown widget alignment.");
        return layout;
    }

    public GamesWidgetBounds Resolve(double viewportWidth, double viewportHeight, double gridSize = 0)
    {
        Validate(this);
        if (!double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight) || viewportWidth <= 0 || viewportHeight <= 0)
            throw new ArgumentException("Viewport must be finite and positive.");
        if (!double.IsFinite(gridSize) || gridSize < 0)
            throw new ArgumentException("Grid size must be finite and non-negative.");

        // Clamp dimensions before alignment so a near-zero snap increment
        // cannot produce Infinity minus Infinity and a NaN coordinate.
        double width = Math.Min(viewportWidth, Math.Max(Width, MinWidth));
        double height = Math.Min(viewportHeight, Math.Max(Height, MinHeight));
        if (SnapToGrid && gridSize > 0)
        {
            width = Math.Min(viewportWidth, Math.Ceiling(width / gridSize) * gridSize);
            height = Math.Min(viewportHeight, Math.Ceiling(height / gridSize) * gridSize);
        }

        double x = AnchorX * viewportWidth + OffsetX;
        double y = AnchorY * viewportHeight + OffsetY;
        if (Alignment == GamesWidgetAlignment.Center)
        {
            x -= width / 2;
            y -= height / 2;
        }
        else if (Alignment == GamesWidgetAlignment.Stretch)
        {
            x = 0;
            y = 0;
            width = viewportWidth;
            height = viewportHeight;
        }

        x = Math.Clamp(x, 0, Math.Max(0, viewportWidth - width));
        y = Math.Clamp(y, 0, Math.Max(0, viewportHeight - height));
        width = Math.Min(width, viewportWidth);
        height = Math.Min(height, viewportHeight);
        return new GamesWidgetBounds(x, y, width, height);
    }
}

public enum GamesWidgetAlignment
{
    TopLeft,
    Center,
    Stretch
}

public readonly record struct GamesWidgetBounds(double X, double Y, double Width, double Height);
