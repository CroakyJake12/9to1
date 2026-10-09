using System.Text.Json.Serialization;

namespace Haven.Application;

/// <summary>Deterministic adjustments of sRGB-encoded 8-bit pixels. This is a
/// shared working-space choice, not an ICC/HDR transform or a source/storage grant.</summary>
public sealed record RasterColorAdjustment(double ExposureEv = 0, double Brightness = 0,
    double Contrast = 0, double Saturation = 0, double Gamma = 1)
{
    [JsonIgnore]
    public bool IsIdentity => ExposureEv == 0 && Brightness == 0 && Contrast == 0 && Saturation == 0 && Gamma == 1;
    public void Validate()
    {
        if (!double.IsFinite(ExposureEv) || ExposureEv is < -10 or > 10 ||
            !double.IsFinite(Brightness) || Brightness is < -1 or > 1 ||
            !double.IsFinite(Contrast) || Contrast is < -1 or > 1 ||
            !double.IsFinite(Saturation) || Saturation is < -1 or > 1 ||
            !double.IsFinite(Gamma) || Gamma is < .1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(RasterColorAdjustment), "Use exposure from -10 to 10 EV, brightness/contrast/saturation from -1 to 1, and gamma from 0.1 to 10.");
    }
    public static RasterColorAdjustment FromNotes(NotesMediaTransformState sameSharedTransform)
    {
        ArgumentNullException.ThrowIfNull(sameSharedTransform);
        var settings = new RasterColorAdjustment(Brightness: sameSharedTransform.Brightness,
            Contrast: sameSharedTransform.Contrast, Saturation: sameSharedTransform.Saturation);
        settings.Validate(); return settings;
    }
}

/// <summary>App-neutral pixel primitive, not an image decoder or another document
/// model. Order: linear-light exposure, brightness, contrast about 18% grey,
/// Rec.709 luminance saturation, sRGB encoding and display gamma. Alpha remains exact.</summary>
public sealed class RasterColorAdjustmentProcessor
{
    private readonly RasterColorAdjustment _settings;
    private readonly double[] _linearTone = new double[256];
    private readonly byte[] _independentTone = new byte[256];
    public RasterColorAdjustmentProcessor(RasterColorAdjustment settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings)); settings.Validate();
        var exposure = Math.Pow(2, settings.ExposureEv); var contrast = Math.Pow(2, 2 * settings.Contrast);
        for (var value = 0; value < 256; value++)
        {
            var encoded = value / 255d;
            var linear = encoded <= .04045 ? encoded / 12.92 : Math.Pow((encoded + .055) / 1.055, 2.4);
            _linearTone[value] = (linear * exposure + settings.Brightness - .18) * contrast + .18;
            _independentTone[value] = Encode(_linearTone[value]);
        }
    }
    public void ApplySrgbRgba8(Span<byte> samePixels)
    {
        if (samePixels.Length % 4 != 0) throw new ArgumentException("Use complete RGBA8 pixels.", nameof(samePixels));
        if (_settings.IsIdentity) return;
        for (var offset = 0; offset < samePixels.Length; offset += 4)
        {
            // Do not manufacture colour in fully transparent pixels or mutate
            // source alpha. The original source is retained by its document owner.
            if (samePixels[offset + 3] == 0) continue;
            if (_settings.Saturation == 0)
            {
                samePixels[offset] = _independentTone[samePixels[offset]];
                samePixels[offset + 1] = _independentTone[samePixels[offset + 1]];
                samePixels[offset + 2] = _independentTone[samePixels[offset + 2]];
                continue;
            }
            var r = _linearTone[samePixels[offset]]; var g = _linearTone[samePixels[offset + 1]]; var b = _linearTone[samePixels[offset + 2]];
            var luminance = .2126 * r + .7152 * g + .0722 * b; var saturation = 1 + _settings.Saturation;
            samePixels[offset] = Encode(luminance + saturation * (r - luminance));
            samePixels[offset + 1] = Encode(luminance + saturation * (g - luminance));
            samePixels[offset + 2] = Encode(luminance + saturation * (b - luminance));
        }
    }
    private byte Encode(double linear)
    {
        linear = Math.Clamp(linear, 0, 1);
        var encoded = linear <= .0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - .055;
        if (_settings.Gamma != 1) encoded = Math.Pow(Math.Clamp(encoded, 0, 1), 1 / _settings.Gamma);
        return (byte)Math.Clamp((int)Math.Round(encoded * 255, MidpointRounding.AwayFromZero), 0, 255);
    }
}
