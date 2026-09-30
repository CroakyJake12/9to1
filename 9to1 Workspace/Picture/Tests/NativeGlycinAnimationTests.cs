using Xunit;

namespace HavenOS.Images.Tests;

public sealed class NativeGlycinAnimationTests
{
    // Two lossless GIF frames: opaque red then blue, two pixels, delays 80/120 ms.
    private const string TwoFrames = "R0lGODlhAgABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACAAAACwAAAAAAgABAAAIBQABAAgIACH5BAAMAAAALAAAAAACAAEAgQAA/wAAAAAAAAAAAAgFAAEACAgAOw==";

    [Fact]
    public void Genuine_sandboxed_frame_session_steps_and_loops_exact_pixels_delays_and_owns_captured_source()
    {
        var source = Convert.FromBase64String(TwoFrames);
        using var session = new PictureGlycinDecoder().OpenFrames(source);
        Array.Clear(source);
        var first = session.NextFrame(TestContext.Current.CancellationToken);
        Assert.Equal(2u, first.Width);
        Assert.Equal(1u, first.Height);
        Assert.Equal(80_000, first.DelayMicroseconds);
        Assert.Equal(new byte[] { 0, 0, 255, 255, 0, 0, 255, 255 }, first.BgraPremultipliedPixels);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => session.NextFrame(cancelled.Token));
        var second = session.NextFrame(TestContext.Current.CancellationToken);
        Assert.Equal(120_000, second.DelayMicroseconds);
        Assert.Equal(new byte[] { 255, 0, 0, 255, 255, 0, 0, 255 }, second.BgraPremultipliedPixels);
        var repeated = session.NextFrame(TestContext.Current.CancellationToken);
        Assert.Equal(first.DelayMicroseconds, repeated.DelayMicroseconds);
        Assert.Equal(first.BgraPremultipliedPixels, repeated.BgraPremultipliedPixels);
        Array.Clear(first.BgraPremultipliedPixels);
        Assert.Contains(repeated.BgraPremultipliedPixels, pixel => pixel != 0);
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.NextFrame(TestContext.Current.CancellationToken));
    }
}
