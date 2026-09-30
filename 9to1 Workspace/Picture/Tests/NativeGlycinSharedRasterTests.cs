using Xunit;

namespace HavenOS.Images.Tests;

public sealed class NativeGlycinSharedRasterTests
{
    [Fact]
    public void Authenticated_owning_bytes_adapter_returns_independent_srgb_shared_frame_without_a_path()
    {
        var bytes = Convert.FromBase64String("R0lGODlhAgABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACAAAACwAAAAAAgABAAAIBQABAAgIACH5BAAMAAAALAAAAAACAAEAgQAA/wAAAAAAAAAAAAgFAAEACAgAOw==");
        var decoder = new PictureGlycinSharedRasterDecoder();
        var shared = decoder.DecodeFirstFrame(bytes, TestContext.Current.CancellationToken);
        Array.Clear(bytes);
        Assert.Equal(2, shared.Width); Assert.Equal(1, shared.Height); Assert.Equal(8, shared.Stride);
        Assert.Equal(new byte[] { 0, 0, 255, 255, 0, 0, 255, 255 }, shared.CopyPixels());
        var returned = shared.CopyPixels(); Array.Clear(returned);
        Assert.Equal(255, shared.CopyPixels()[2]);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => decoder.DecodeFirstFrame(bytes, cancelled.Token));
    }
}
