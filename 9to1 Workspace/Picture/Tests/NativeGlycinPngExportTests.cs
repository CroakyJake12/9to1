using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class NativeGlycinPngExportTests
{
    [Fact]
    public void Genuine_sandboxed_PNG_creator_roundtrips_transparency_premultiplication_and_ignores_stride_padding()
    {
        byte[] source = [0, 0, 0, 0, 0, 0, 255, 255, 128, 0, 0, 128, 99, 99, 99, 99];
        var frame = new HomeProductivityRasterFrame(3, 1, 16, source);
        var encoder = new PictureGlycinPngEncoder();
        foreach (byte compression in new byte[] { 0, 100 })
        {
            var png = encoder.EncodeFlattenedFrame(frame, compression, TestContext.Current.CancellationToken);
            var restored = new PictureGlycinDecoder().DecodeFirstFrame(png);
            Assert.Equal(3u, restored.Width);
            Assert.Equal(1u, restored.Height);
            Assert.Equal(source[..12], restored.BgraPremultipliedPixels);
            Assert.Equal(source, frame.CopyPixels());
        }
    }

    [Fact]
    public void PNG_export_rejects_invalid_premultiplication_compression_and_precancelled_operations()
    {
        var encoder = new PictureGlycinPngEncoder();
        var invalid = new HomeProductivityRasterFrame(1, 1, 4, new byte[] { 255, 0, 0, 1 });
        Assert.Throws<InvalidDataException>(() => encoder.EncodeFlattenedFrame(invalid, cancellationToken: TestContext.Current.CancellationToken));
        var valid = new HomeProductivityRasterFrame(1, 1, 4, new byte[] { 0, 0, 255, 255 });
        Assert.Throws<ArgumentOutOfRangeException>(() => encoder.EncodeFlattenedFrame(valid, 101, TestContext.Current.CancellationToken));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => encoder.EncodeFlattenedFrame(valid, cancellationToken: cancelled.Token));
    }
}
