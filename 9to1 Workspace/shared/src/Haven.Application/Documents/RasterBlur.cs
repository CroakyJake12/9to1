using System.Text.Json.Serialization;

namespace Haven.Application;

/// <summary>A local raster adjustment, with no file/selection/permission identity.
/// Radius is the finite Gaussian support in source pixels; sigma is radius/2.
/// The original encoded RGBA8 colour values are mixed without profile conversion.</summary>
public sealed record RasterBlur([property: JsonRequired] int X, [property: JsonRequired] int Y,
    [property: JsonRequired] int Width, [property: JsonRequired] int Height, [property: JsonRequired] int Radius)
{
    public void ValidateGeometry()
    {
        if (X < 0 || Y < 0 || Width is < 1 or > 32768 || Height is < 1 or > 32768 ||
            X > 32768 - Width || Y > 32768 - Height || (long)Width * Height > 100_000_000 || Radius is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(Width), "Choose a bounded positive pixel rectangle and a whole-pixel blur radius from 1 to 32.");
    }
    public void Validate(int canvasWidth, int canvasHeight)
    {
        ValidateGeometry();
        if (canvasWidth is < 1 or > 32768 || canvasHeight is < 1 or > 32768 || (long)canvasWidth * canvasHeight > 100_000_000 ||
            Width > canvasWidth || Height > canvasHeight || X > canvasWidth - Width || Y > canvasHeight - Height)
            throw new ArgumentOutOfRangeException(nameof(Width), "The blur rectangle must fit inside the current image.");
    }
    public static double[] CreateKernel(int radius)
    {
        if (radius is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(radius));
        var weights = new double[checked(radius * 2 + 1)]; var sigma = radius / 2d; double sum = 0;
        for (var index = 0; index < weights.Length; index++)
        { var offset = index - radius; weights[index] = Math.Exp(-.5 * offset * offset / (sigma * sigma)); sum += weights[index]; }
        // Exact dyadic weights make the two separable passes deterministic:
        // RGBA8 premultiplied sums fit exactly in double precision at 2^-32.
        // Symmetric rounding is followed by one centre correction to unit sum.
        const int scale = 65536; var total = 0;
        for (var index = 0; index < weights.Length; index++)
        { var quantized = (int)Math.Round(weights[index] / sum * scale, MidpointRounding.AwayFromZero); weights[index] = quantized; total += quantized; }
        weights[radius] += scale - total;
        for (var index = 0; index < weights.Length; index++) weights[index] /= scale;
        return weights;
    }
}

/// <summary>One bounded Gaussian pass. Intermediate values retain premultiplied
/// colour and alpha at double precision, so hidden transparent colours do not
/// bleed and rounding occurs only at the final RGBA8 publication.</summary>
public struct RasterBlurAccumulator
{
    private double _redAlpha, _greenAlpha, _blueAlpha, _alpha, _weight;
    private int _count;
    private void Admit(double weight)
    {
        if (!double.IsFinite(weight) || weight <= 0 || weight > 1 || _count >= 65 || _weight + weight > 1.000000001)
            throw new ArgumentOutOfRangeException(nameof(weight), "Gaussian samples must belong to one bounded, normalized pass.");
        _count++; _weight += weight;
    }
    public void AddRgba8(ReadOnlySpan<byte> pixel, double weight)
    {
        if (pixel.Length != 4) throw new ArgumentException("One RGBA8 sample is required.", nameof(pixel));
        Admit(weight); var alpha = pixel[3];
        _redAlpha += pixel[0] * alpha * weight; _greenAlpha += pixel[1] * alpha * weight;
        _blueAlpha += pixel[2] * alpha * weight; _alpha += alpha * weight;
    }
    public void AddPremultiplied(ReadOnlySpan<double> pixel, double weight)
    {
        if (pixel.Length != 4 || !double.IsFinite(pixel[3]) || pixel[3] < 0 || pixel[3] > 255.000000001)
            throw new ArgumentException("One finite premultiplied sample is required.", nameof(pixel));
        for (var channel = 0; channel < 3; channel++)
            if (!double.IsFinite(pixel[channel]) || pixel[channel] < 0 || pixel[channel] > pixel[3] * 255 + .000001)
                throw new ArgumentException("Premultiplied colour must fit its actual alpha.", nameof(pixel));
        Admit(weight);
        _redAlpha += pixel[0] * weight; _greenAlpha += pixel[1] * weight; _blueAlpha += pixel[2] * weight; _alpha += pixel[3] * weight;
    }
    private readonly void DemandComplete()
    {
        if (_count == 0 || Math.Abs(_weight - 1) > .000000001)
            throw new InvalidOperationException("A complete normalized Gaussian pass is required before publication.");
    }
    public readonly void WritePremultiplied(Span<double> pixel)
    {
        if (pixel.Length != 4) throw new ArgumentException("One intermediate sample is required.", nameof(pixel));
        DemandComplete(); pixel[0] = _redAlpha; pixel[1] = _greenAlpha; pixel[2] = _blueAlpha; pixel[3] = _alpha;
    }
    public readonly void WriteRgba8(Span<byte> pixel)
    {
        if (pixel.Length != 4) throw new ArgumentException("One RGBA8 output is required.", nameof(pixel));
        DemandComplete(); pixel[3] = Rounded(_alpha);
        if (pixel[3] == 0) { pixel[0] = pixel[1] = pixel[2] = 0; return; }
        pixel[0] = Rounded(_redAlpha / _alpha); pixel[1] = Rounded(_greenAlpha / _alpha); pixel[2] = Rounded(_blueAlpha / _alpha);
    }
    private static byte Rounded(double value) => (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
}
