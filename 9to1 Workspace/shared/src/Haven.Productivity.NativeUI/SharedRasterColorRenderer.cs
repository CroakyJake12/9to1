using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Haven.Application;

namespace Haven.Productivity.NativeUI;

/// <summary>Native presentation adapter over the shared sRGB pixel primitive.
/// The SAME maintained Avalonia framebuffer transcoder performs pixel/alpha
/// conversion; this leaf neither decodes source files nor owns their identity.</summary>
public static class SharedRasterColorRenderer
{
    public static WriteableBitmap Render(Bitmap sameSource, RasterColorAdjustment settings, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(sameSource);
        var processor = new RasterColorAdjustmentProcessor(settings); token.ThrowIfCancellationRequested();
        var size = sameSource.PixelSize;
        if (size.Width is < 1 or > 32768 || size.Height is < 1 or > 32768 || (long)size.Width * size.Height > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(sameSource), "The full-quality raster exceeds the declared native adjustment limit.");
        var result = new WriteableBitmap(size, sameSource.Dpi, PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        try
        {
            using var pixels = result.Lock();
            if (pixels.Format != PixelFormat.Rgba8888 || pixels.AlphaFormat != AlphaFormat.Unpremul)
                throw new NotSupportedException("The native framebuffer cannot supply the declared sRGB RGBA8 adjustment path.");
            sameSource.CopyPixels(pixels);
            var rowLength = checked(size.Width * 4); var row = new byte[rowLength];
            for (var y = 0; y < size.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                var address = IntPtr.Add(pixels.Address, checked(y * pixels.RowBytes));
                Marshal.Copy(address, row, 0, rowLength);
                processor.ApplySrgbRgba8(row);
                Marshal.Copy(row, 0, address, rowLength);
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
