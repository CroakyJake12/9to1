using Haven.Application;

namespace Haven.Core.Tests;

public sealed class RasterBlurTests
{
    [Fact]
    public void Gaussian_kernel_is_symmetric_normalized_positive_and_exactly_bounded()
    {
        foreach (var radius in new[] { 1, 2, 7, 32 })
        {
            var kernel = RasterBlur.CreateKernel(radius);
            Assert.Equal(radius * 2 + 1, kernel.Length); Assert.Equal(1, kernel.Sum(), 12);
            for (var index = 0; index < kernel.Length; index++)
            { Assert.True(double.IsFinite(kernel[index]) && kernel[index] > 0); Assert.Equal(Math.Truncate(kernel[index] * 65536), kernel[index] * 65536); Assert.Equal(kernel[index], kernel[^(index + 1)]); }
            Assert.True(kernel[radius] > kernel[0]);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => RasterBlur.CreateKernel(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RasterBlur.CreateKernel(33));
        RasterBlur[] invalid = [new(-1, 0, 1, 1, 1), new(0, -1, 1, 1, 1), new(0, 0, 0, 1, 1),
            new(0, 0, 1, 0, 1), new(0, 0, 1, 1, 0), new(0, 0, 1, 1, 33), new(int.MaxValue, 0, 1, 1, 2),
            new(0, 0, 32768, 32768, 2), new(0, 0, int.MaxValue, 1, 2)];
        foreach (var settings in invalid) Assert.Throws<ArgumentOutOfRangeException>(settings.ValidateGeometry);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RasterBlur(2, 1, 3, 2, 2).Validate(4, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RasterBlur(1, 2, 2, 3, 2).Validate(3, 4));
        new RasterBlur(0, 0, 1, 1, 32).Validate(1, 1);
        new RasterBlur(0, 0, 32768, 1, 32).Validate(32768, 1);
    }

    [Fact]
    public void Hidden_transparent_colour_does_not_bleed_and_intermediate_alpha_is_not_rounded()
    {
        var horizontal = new RasterBlurAccumulator();
        horizontal.AddRgba8([255, 0, 0, 255], .5); horizontal.AddRgba8([0, 255, 255, 0], .5);
        double[] intermediate = new double[4]; horizontal.WritePremultiplied(intermediate);
        Assert.Equal(new[] { 32512.5, 0, 0, 127.5 }, intermediate);
        var vertical = new RasterBlurAccumulator(); vertical.AddPremultiplied(intermediate, .5); vertical.AddPremultiplied([0, 0, 0, 0], .5);
        byte[] output = new byte[4]; vertical.WriteRgba8(output);
        Assert.Equal(new byte[] { 255, 0, 0, 64 }, output);
        var tiny = new RasterBlurAccumulator(); tiny.AddRgba8([255, 0, 255, 1], .25); tiny.AddRgba8([0, 0, 0, 0], .75); tiny.WriteRgba8(output);
        Assert.Equal(new byte[4], output);
        var transparent = new RasterBlurAccumulator(); transparent.AddRgba8([255, 0, 255, 0], 1); transparent.WriteRgba8(output);
        Assert.Equal(new byte[4], output);
    }

    [Fact]
    public void Invalid_or_incomplete_samples_refuse_without_publishing_a_partial_mean()
    {
        var sample = new RasterBlurAccumulator(); byte[] output = [11, 12, 13, 14];
        Assert.Throws<InvalidOperationException>(() => sample.WriteRgba8(output)); Assert.Equal(new byte[] { 11, 12, 13, 14 }, output);
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, -1d, 0d, 1.01 })
            Assert.Throws<ArgumentOutOfRangeException>(() => sample.AddRgba8([0, 0, 0, 0], invalid));
        Assert.Throws<ArgumentException>(() => sample.AddRgba8([0, 0, 0], .5));
        Assert.Throws<ArgumentException>(() => sample.AddPremultiplied([1, 0, 0, 0], .5));
        Assert.Throws<ArgumentException>(() => sample.AddPremultiplied([0, 0, 0, double.NaN], .5));
        sample.AddRgba8([7, 8, 9, 255], .5);
        Assert.Throws<InvalidOperationException>(() => sample.WriteRgba8(output));
        Assert.Throws<ArgumentOutOfRangeException>(() => sample.AddRgba8([0, 0, 0, 0], .75));
        sample.AddRgba8([7, 8, 9, 255], .5); sample.WriteRgba8(output); Assert.Equal(new byte[] { 7, 8, 9, 255 }, output);
        var bounded = new RasterBlurAccumulator();
        for (var index = 0; index < 65; index++) bounded.AddRgba8([1, 2, 3, 255], 1d / 65);
        Assert.Throws<ArgumentOutOfRangeException>(() => bounded.AddRgba8([1, 2, 3, 255], 1d / 65));
        bounded.WriteRgba8(output); Assert.Equal(new byte[] { 1, 2, 3, 255 }, output);
    }
}
