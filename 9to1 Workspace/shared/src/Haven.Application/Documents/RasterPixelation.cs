namespace Haven.Application;

/// <summary>One explicit pixel rectangle and block size in an owning raster
/// operation. It has no selection/file/permission identity of its own.</summary>
public sealed record RasterPixelation(int X, int Y, int Width, int Height, int BlockSize)
{
    public void ValidateGeometry()
    {
        if (X < 0 || Y < 0 || Width is < 1 or > 32768 || Height is < 1 or > 32768 ||
            X > 32768 - Width || Y > 32768 - Height || (long)Width * Height > 100_000_000 || BlockSize is < 2 or > 512)
            throw new ArgumentOutOfRangeException(nameof(Width), "Choose a bounded positive pixel rectangle and a whole-pixel block size from 2 to 512.");
    }
    public void Validate(int canvasWidth, int canvasHeight)
    {
        ValidateGeometry();
        if (canvasWidth is < 1 or > 32768 || canvasHeight is < 1 or > 32768 || (long)canvasWidth * canvasHeight > 100_000_000 ||
            Width > canvasWidth || Height > canvasHeight || X > canvasWidth - Width || Y > canvasHeight - Height)
            throw new ArgumentOutOfRangeException(nameof(Width), "The pixel rectangle must fit inside the current image.");
    }

}

/// <summary>Bounded block mean over RGBA8. Colour channels are weighted by
/// alpha so hidden transparent colours cannot bleed into visible pixels.
/// This primitive performs no decoder/profile conversion or source IO.</summary>
public struct RasterPixelationAccumulator
{
    private ulong _redAlpha, _greenAlpha, _blueAlpha, _alpha;
    private uint _count;
    public void AddRgba8(ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length % 4 != 0 || pixels.Length / 4 > 512 || _count > 512u * 512u - (uint)(pixels.Length / 4))
            throw new ArgumentOutOfRangeException(nameof(pixels), "Pixelation samples exceed one bounded 512 by 512 block.");
        for (var index = 0; index < pixels.Length; index += 4)
        {
            var alpha = pixels[index + 3];
            _redAlpha += (ulong)pixels[index] * alpha; _greenAlpha += (ulong)pixels[index + 1] * alpha;
            _blueAlpha += (ulong)pixels[index + 2] * alpha; _alpha += alpha; _count++;
        }
    }
    public readonly void WriteMeanRgba8(Span<byte> pixel)
    {
        if (pixel.Length != 4 || _count == 0) throw new ArgumentException("A pixelation block needs actual samples and one RGBA output pixel.", nameof(pixel));
        pixel[3] = (byte)((_alpha + _count / 2u) / _count);
        if (pixel[3] == 0) { pixel[0] = pixel[1] = pixel[2] = 0; return; }
        pixel[0] = (byte)((_redAlpha + _alpha / 2) / _alpha);
        pixel[1] = (byte)((_greenAlpha + _alpha / 2) / _alpha);
        pixel[2] = (byte)((_blueAlpha + _alpha / 2) / _alpha);
    }
}
