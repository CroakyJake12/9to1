using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace HavenOS.Images;

public sealed partial class PictureCropService
{
    private static void ValidateOriginalPixelCrop(Bitmap source, CropOperation crop)
    {
        var size = source.PixelSize;
        if (crop.X < 0 || crop.Y < 0 || crop.Width <= 0 || crop.Height <= 0 ||
            (long)crop.X + crop.Width > size.Width || (long)crop.Y + crop.Height > size.Height)
            throw new InvalidDataException("The image operation is unsupported or outside the current raster.");
        if (!double.IsFinite(source.Dpi.X) || !double.IsFinite(source.Dpi.Y) || source.Dpi.X <= 0 || source.Dpi.Y <= 0)
            throw new InvalidDataException("The source has no valid pixel-to-canvas DPI mapping.");
    }

    private static WriteableBitmap RenderOriginalPixelCrop(Bitmap source, CropOperation crop,
        PixelFormat sameFormat, AlphaFormat sameAlpha)
    {
        // Bound native row/buffer arithmetic before allocating. Only the
        // cropped output is allocated; no whole-source scratch or decoder.
        var bytesPerPixel = sameFormat.BitsPerPixel / 8;
        var minimumStride = checked(crop.Width * bytesPerPixel);
        _ = checked(minimumStride * crop.Height);
        var result = new WriteableBitmap(new PixelSize(crop.Width, crop.Height), source.Dpi, sameFormat, sameAlpha);
        try
        {
            using var target = result.Lock();
            if (target.Format != sameFormat || target.AlphaFormat != sameAlpha ||
                target.Size != result.PixelSize || target.RowBytes < minimumStride)
                throw new NotSupportedException("The native crop framebuffer cannot preserve the source pixel format.");
            var bufferSize = checked(target.RowBytes * crop.Height);
            source.CopyPixels(new PixelRect(crop.X, crop.Y, crop.Width, crop.Height), target.Address, bufferSize, target.RowBytes);
            return result;
        }
        catch (Exception originalFailure)
        {
            try { result.Dispose(); }
            catch (Exception retirementFailure) { throw new AggregateException(originalFailure, retirementFailure); }
            throw;
        }
    }
}
