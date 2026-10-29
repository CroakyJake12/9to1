using Avalonia;

namespace HavenOS.Apps.Browse;

internal sealed record ChromiumViewport(int Width, int Height, double Scale)
{
    internal static readonly ChromiumViewport Default = new(1280, 800, 1);
    internal int CaptureWidth => Math.Max(1, (int)Math.Ceiling(Width * Scale));
    internal int CaptureHeight => Math.Max(1, (int)Math.Ceiling(Height * Scale));

    // A capture budget, not a viewport reduction. The CSS viewport stays the real size of the tab, so
    // the page still lays out correctly and hit testing still matches, while the streamed bitmap is
    // capped so the UI thread can afford to decode each frame. Measured owner window on 29 October:
    // 2850x1820, so an uncapped frame is roughly 5.2 megapixels at scale 1 and four times that at 2x -
    // and hidden tabs were streaming them too, which is what froze the desktop once a site loaded.
    private const double CaptureBudgetPixels = 2_000_000;

    internal double CaptureRatio
    {
        get
        {
            var devicePixels = (double)CaptureWidth * CaptureHeight;
            return devicePixels <= CaptureBudgetPixels || devicePixels <= 0
                ? 1 : Math.Sqrt(CaptureBudgetPixels / devicePixels);
        }
    }
    internal int ScaledCaptureWidth => Math.Max(1, (int)Math.Ceiling(CaptureWidth * CaptureRatio));
    internal int ScaledCaptureHeight => Math.Max(1, (int)Math.Ceiling(CaptureHeight * CaptureRatio));
    internal static ChromiumViewport From(Size bounds, double scaling)
    {
        if (!double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) || bounds.Width <= 0 || bounds.Height <= 0)
            return Default;
        var width = (int)Math.Clamp(Math.Ceiling(bounds.Width), 1, 8192);
        var height = (int)Math.Clamp(Math.Ceiling(bounds.Height), 1, 8192);
        var scale = double.IsFinite(scaling) && scaling > 0 ? Math.Min(scaling, 4) : 1;
        scale = Math.Min(scale, 8192d / Math.Max(width, height));
        scale = Math.Min(scale, Math.Sqrt(32000000d / ((double)width * height)));
        return new(width, height, scale);
    }
    internal static Rect Fit(Size image, Size bounds)
    {
        if (image.Width <= 0 || image.Height <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return default;
        var scale = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
        var width = image.Width * scale; var height = image.Height * scale;
        return new((bounds.Width - width) / 2, (bounds.Height - height) / 2, width, height);
    }
    internal Point MapPoint(Point point, Size bounds)
    {
        var frame = Fit(new Size(Width, Height), bounds);
        if (frame.Width <= 0 || frame.Height <= 0) return point;
        return new(Math.Clamp((point.X - frame.X) * Width / frame.Width, 0, Width),
            Math.Clamp((point.Y - frame.Y) * Height / frame.Height, 0, Height));
    }
    internal static Vector WheelPixels(Vector delta, bool shift)
    {
        // Same 50-DIP notch conversion as the owning Avalonia ScrollContentPresenter.
        // CDP consumes CSS pixels; DPI and screenshot scaling do not change scroll speed.
        if (shift && Math.Abs(delta.X) < 0.00001) delta = new(delta.Y, delta.X);
        return new(-delta.X * 50, -delta.Y * 50);
    }
}
