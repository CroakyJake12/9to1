using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Haven.Application;

namespace Haven.Productivity.NativeUI;

/// <summary>Portable native adapter over canonical pixelation geometry/mean.
/// SAME Avalonia CopyPixels supplies RGBA/alpha conversion; original image,
/// density and pixels outside the entered region remain unchanged.</summary>
public static class SharedRasterPixelationRenderer
{
    public static WriteableBitmap Render(Bitmap sameSource, RasterPixelation settings, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(sameSource); ArgumentNullException.ThrowIfNull(settings);
        var size = sameSource.PixelSize; settings.Validate(size.Width, size.Height); token.ThrowIfCancellationRequested();
        var result = new WriteableBitmap(size, sameSource.Dpi, PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        try
        {
            using var pixels = result.Lock();
            if (pixels.Format != PixelFormat.Rgba8888 || pixels.AlphaFormat != AlphaFormat.Unpremul)
                throw new NotSupportedException("The native framebuffer cannot supply the RGBA8 pixelation path.");
            sameSource.CopyPixels(pixels);
            var rowLength = checked(settings.Width * 4); var row = new byte[rowLength];
            var count = checked((settings.Width + settings.BlockSize - 1) / settings.BlockSize);
            var sums = new RasterPixelationAccumulator[count];
            var bottom = checked(settings.Y + settings.Height); Span<byte> mean = stackalloc byte[4];
            for (var top = settings.Y; top < bottom; top += settings.BlockSize)
            {
                Array.Clear(sums); var end = Math.Min(bottom, checked(top + settings.BlockSize));
                for (var y = top; y < end; y++)
                {
                    token.ThrowIfCancellationRequested();
                    var address = IntPtr.Add(pixels.Address, checked(y * pixels.RowBytes + settings.X * 4));
                    Marshal.Copy(address, row, 0, rowLength);
                    for (var block = 0; block < count; block++)
                    {
                        var offset = checked(block * settings.BlockSize);
                        sums[block].AddRgba8(row.AsSpan(offset * 4, Math.Min(settings.BlockSize, settings.Width - offset) * 4));
                    }
                }
                // Sampling of a band completes before any of its pixels are
                // changed. Mean buffers plus one region row are bounded; no
                // whole-image scratch or repeated recompression is introduced.
                for (var block = 0; block < count; block++)
                {
                    sums[block].WriteMeanRgba8(mean); var offset = checked(block * settings.BlockSize);
                    var width = Math.Min(settings.BlockSize, settings.Width - offset);
                    for (var x = offset; x < offset + width; x++) mean.CopyTo(row.AsSpan(x * 4, 4));
                }
                for (var y = top; y < end; y++)
                {
                    token.ThrowIfCancellationRequested();
                    var address = IntPtr.Add(pixels.Address, checked(y * pixels.RowBytes + settings.X * 4));
                    Marshal.Copy(row, 0, address, rowLength);
                }
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
