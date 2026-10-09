namespace HavenOS.Images;

/// <summary>Pixel geometry for the existing CropOperation; no source or permission identity.</summary>
public static class PictureCropRatio
{
    public static PictureCropRatioFit FitInside(CropOperation bounds, int canvasWidth, int canvasHeight,
        int ratioWidth, int ratioHeight)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (canvasWidth is < 1 or > 32768 || canvasHeight is < 1 or > 32768 ||
            (long)canvasWidth * canvasHeight > 100_000_000 || bounds.X < 0 || bounds.Y < 0 ||
            bounds.Width < 1 || bounds.Height < 1 || (long)bounds.X + bounds.Width > canvasWidth ||
            (long)bounds.Y + bounds.Height > canvasHeight)
            throw new ArgumentOutOfRangeException(nameof(bounds), "Enter a positive crop rectangle inside this edit's input canvas.");
        if (ratioWidth is < 1 or > 32768 || ratioHeight is < 1 or > 32768)
            throw new ArgumentOutOfRangeException(nameof(ratioWidth), "Choose a positive whole-number width and height ratio from 1 to 32768.");

        // Retain the limiting side in whole pixels, then round only the other
        // side to its nearest pixel (half up). Exact integer multiples would
        // shrink coprime original-image ratios unnecessarily or refuse them.
        int width, height;
        // A rectangle already within nearest-pixel rounding is a fixed point.
        // Fitting its draft and then applying it cannot shave another pixel
        // because rounding changed which side appears mathematically limiting.
        if (((long)bounds.Width * ratioHeight + ratioWidth / 2) / ratioWidth == bounds.Height ||
            ((long)bounds.Height * ratioWidth + ratioHeight / 2) / ratioHeight == bounds.Width)
        { width = bounds.Width; height = bounds.Height; }
        else if ((long)bounds.Width * ratioHeight <= (long)bounds.Height * ratioWidth)
        {
            width = bounds.Width;
            height = checked((int)(((long)width * ratioHeight + ratioWidth / 2) / ratioWidth));
        }
        else
        {
            height = bounds.Height;
            width = checked((int)(((long)height * ratioWidth + ratioHeight / 2) / ratioHeight));
        }
        if (width < 1 || height < 1 || width > bounds.Width || height > bounds.Height)
            throw new ArgumentOutOfRangeException(nameof(bounds), "This ratio needs a larger crop rectangle to retain at least one pixel on each side.");
        var crop = new CropOperation(checked(bounds.X + (bounds.Width - width) / 2),
            checked(bounds.Y + (bounds.Height - height) / 2), width, height);
        return new(crop, (long)width * ratioHeight != (long)height * ratioWidth);
    }
}

public sealed record PictureCropRatioFit(CropOperation Crop, bool IsPixelRounded);
