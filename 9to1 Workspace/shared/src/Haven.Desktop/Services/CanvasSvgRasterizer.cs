namespace Haven.Desktop.Services;

public sealed record CanvasRasterFrame(int Width, int Height, int Stride, byte[] PremultipliedBgra);

/// <summary>Desktop compatibility facade for the owning Canvas native renderer.</summary>
public static class CanvasSvgRasterizer
{
    public static CanvasRasterFrame Render(byte[] svg, int width, int height)
    {
        var frame = HavenOS.Apps.Canvas.CanvasSvgRasterizer.Render(svg, width, height);
        return new(frame.Width, frame.Height, frame.Stride, frame.PremultipliedBgra);
    }
}
