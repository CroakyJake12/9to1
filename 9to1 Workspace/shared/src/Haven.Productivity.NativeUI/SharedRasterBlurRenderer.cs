using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Haven.Application;

namespace Haven.Productivity.NativeUI;

/// <summary>Shared native Gaussian rectangle adapter over the original raster.
/// Native RGBA conversion is the SAME maintained Avalonia CopyPixels path.
/// Only selected output pixels change; neighbours are sampled from the original
/// canvas, with whole-canvas edge clamping rather than a hard rectangle seam.</summary>
public static class SharedRasterBlurRenderer
{
    public static WriteableBitmap Render(Bitmap sameSource, RasterBlur settings, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(sameSource); ArgumentNullException.ThrowIfNull(settings);
        var size = sameSource.PixelSize; settings.Validate(size.Width, size.Height); token.ThrowIfCancellationRequested();
        var weights = RasterBlur.CreateKernel(settings.Radius); var count = weights.Length;
        var rowLength = checked(size.Width * 4); var outputLength = checked(settings.Width * 4);
        // At the largest supported width/radius the row ring is <66 MiB,
        // plus two RGBA rows. No additional whole-image scratch is allocated.
        _ = checked((long)outputLength * count * sizeof(double));
        var result = new WriteableBitmap(size, sameSource.Dpi, PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        try
        {
            using var pixels = result.Lock();
            if (pixels.Format != PixelFormat.Rgba8888 || pixels.AlphaFormat != AlphaFormat.Unpremul)
                throw new NotSupportedException("The native framebuffer cannot supply the RGBA8 blur path.");
            sameSource.CopyPixels(pixels);
            var sourceRow = new byte[rowLength]; var outputRow = new byte[outputLength];
            var ring = new double[count][];
            for (var index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested(); ring[index] = new double[outputLength];
                ReadHorizontal(Math.Clamp(settings.Y - settings.Radius + index, 0, size.Height - 1), ring[index]);
            }
            var head = 0; var bottom = checked(settings.Y + settings.Height);
            for (var y = settings.Y; y < bottom; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < settings.Width; x++)
                {
                    var sum = new RasterBlurAccumulator();
                    for (var index = 0; index < count; index++) sum.AddPremultiplied(ring[(head + index) % count].AsSpan(x * 4, 4), weights[index]);
                    sum.WriteRgba8(outputRow.AsSpan(x * 4, 4));
                }
                Marshal.Copy(outputRow, 0, IntPtr.Add(pixels.Address, checked(y * pixels.RowBytes + settings.X * 4)), outputLength);
                if (y + 1 < bottom)
                {
                    // A future row is sampled before any write can change it.
                    // Already-written rows are kept in the ring, never reread.
                    ReadHorizontal(Math.Min(size.Height - 1, checked(y + settings.Radius + 1)), ring[head]);
                    head = (head + 1) % count;
                }
            }
            return result;

            void ReadHorizontal(int y, double[] row)
            {
                token.ThrowIfCancellationRequested();
                Marshal.Copy(IntPtr.Add(pixels.Address, checked(y * pixels.RowBytes)), sourceRow, 0, rowLength);
                for (var x = 0; x < settings.Width; x++)
                {
                    var sum = new RasterBlurAccumulator();
                    for (var index = 0; index < count; index++)
                    {
                        var sourceX = Math.Clamp(settings.X + x - settings.Radius + index, 0, size.Width - 1);
                        sum.AddRgba8(sourceRow.AsSpan(sourceX * 4, 4), weights[index]);
                    }
                    sum.WritePremultiplied(row.AsSpan(x * 4, 4));
                }
            }
        }
        catch { result.Dispose(); throw; }
    }
}
