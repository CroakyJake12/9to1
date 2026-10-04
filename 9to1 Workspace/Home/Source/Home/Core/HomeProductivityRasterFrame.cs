namespace HavenOS.Home.Core;

/// <summary>A detached CPU render result, never a native handle or an authority to read an asset.</summary>
public sealed class HomeProductivityRasterFrame
{
    private readonly byte[] _pixels;
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public HomeProductivityRasterFrame(int width, int height, int stride, ReadOnlySpan<byte> premultipliedBgra)
    {
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || stride < (long)width * 4 ||
            (long)stride * height > 64 * 1024 * 1024 || (long)stride * height != premultipliedBgra.Length)
            throw new ArgumentException("A productivity raster must contain a bounded complete BGRA frame.");
        Width = width; Height = height; Stride = stride; _pixels = premultipliedBgra.ToArray();
    }
    public byte[] CopyPixels() => (byte[])_pixels.Clone();
}

public sealed record HomeProductivityRasterBinding(string ControlId, Guid ObjectId, HomeProductivityRasterFrame Frame);
