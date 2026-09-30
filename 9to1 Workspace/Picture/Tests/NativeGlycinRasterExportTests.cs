using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class NativeGlycinRasterExportTests
{
    [Theory]
    [InlineData(PictureRasterExportFormat.Png, false)]
    [InlineData(PictureRasterExportFormat.Png, true)]
    [InlineData(PictureRasterExportFormat.WebP, false)]
    [InlineData(PictureRasterExportFormat.WebP, true)]
    [InlineData(PictureRasterExportFormat.Tiff, false)]
    [InlineData(PictureRasterExportFormat.Tiff, true)]
    [InlineData(PictureRasterExportFormat.Bmp, false)]
    [InlineData(PictureRasterExportFormat.Bmp, true)]
    [InlineData(PictureRasterExportFormat.Tga, false)]
    [InlineData(PictureRasterExportFormat.Tga, true)]
    [InlineData(PictureRasterExportFormat.Ico, false)]
    [InlineData(PictureRasterExportFormat.Ico, true)]
    public void Genuine_donor_creator_roundtrips_each_lossless_raster_format(PictureRasterExportFormat format, bool transparent)
    {
        byte[] pixels = transparent ? [0, 0, 128, 128, 0, 0, 0, 0] : [0, 0, 255, 255, 255, 0, 0, 255];
        var source = new HomeProductivityRasterFrame(2, 1, 8, pixels);
        var encoded = new PictureGlycinRasterEncoder().EncodeFlattenedFrame(source, format, cancellationToken: TestContext.Current.CancellationToken);
        var restored = new PictureGlycinDecoder().DecodeFirstFrame(encoded);
        Assert.Equal(2u, restored.Width);
        Assert.Equal(1u, restored.Height);
        Assert.Equal(pixels, restored.BgraPremultipliedPixels);
        Assert.Equal(pixels, source.CopyPixels());
    }

    [Fact]
    public void JPEG_requires_opaque_input_and_explicit_quality_while_preserving_dimensions()
    {
        var encoder = new PictureGlycinRasterEncoder();
        var alpha = new HomeProductivityRasterFrame(1, 1, 4, new byte[] { 0, 0, 128, 128 });
        Assert.Throws<NotSupportedException>(() => encoder.EncodeFlattenedFrame(alpha, PictureRasterExportFormat.Jpeg,
            cancellationToken: TestContext.Current.CancellationToken));
        var pixels = Enumerable.Repeat(new byte[] { 0, 0, 255, 255 }, 64).SelectMany(value => value).ToArray();
        var encoded = encoder.EncodeFlattenedFrame(new(8, 8, 32, pixels), PictureRasterExportFormat.Jpeg, quality: 95,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 255, 216, 255 }, encoded[..3]);
        var restored = new PictureGlycinDecoder().DecodeFirstFrame(encoded);
        Assert.Equal(8u, restored.Width);
        Assert.Equal(8u, restored.Height);
        Assert.InRange(restored.BgraPremultipliedPixels[2], (byte)245, (byte)255);
        Assert.Equal(255, restored.BgraPremultipliedPixels[3]);
    }
}
