using Xunit;

namespace HavenOS.Images.Tests;

public sealed class NativeGlycinSharedAnimationTests
{
    [Fact]
    public void Non_looping_shared_session_reports_actual_donor_end_without_pixel_hash_heuristics()
    {
        var bytes = Convert.FromBase64String("R0lGODlhAgABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACAAAACwAAAAAAgABAAAIBQABAAgIACH5BAAMAAAALAAAAAACAAEAgQAA/wAAAAAAAAAAAAgFAAEACAgAOw==");
        using var session = new PictureGlycinSharedRasterDecoder().OpenFrames(bytes, loopAnimation: false, TestContext.Current.CancellationToken);
        Assert.Equal("image/gif", session.MimeType);
        Assert.NotNull(session.TryNextFrame(TestContext.Current.CancellationToken));
        Assert.NotNull(session.TryNextFrame(TestContext.Current.CancellationToken));
        Assert.Null(session.TryNextFrame(TestContext.Current.CancellationToken));
        Assert.Null(session.TryNextFrame(TestContext.Current.CancellationToken));
        Assert.Throws<EndOfStreamException>(() => session.NextFrame(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Shared_session_returns_independent_real_frames_and_delays_without_retaining_previous_frames()
    {
        var bytes = Convert.FromBase64String("R0lGODlhAgABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACAAAACwAAAAAAgABAAAIBQABAAgIACH5BAAMAAAALAAAAAACAAEAgQAA/wAAAAAAAAAAAAgFAAEACAgAOw==");
        using var session = new PictureGlycinSharedRasterDecoder().OpenFrames(bytes, loopAnimation: true, TestContext.Current.CancellationToken);
        Array.Clear(bytes);
        var first = session.NextFrame(TestContext.Current.CancellationToken);
        Assert.Equal(80_000, first.DelayMicroseconds);
        Assert.Equal(new byte[] { 0, 0, 255, 255, 0, 0, 255, 255 }, first.Raster.CopyPixels());
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => session.NextFrame(canceled.Token));
        var second = session.NextFrame(TestContext.Current.CancellationToken);
        Assert.Equal(120_000, second.DelayMicroseconds);
        Assert.Equal(new byte[] { 255, 0, 0, 255, 255, 0, 0, 255 }, second.Raster.CopyPixels());
        Assert.Equal(255, first.Raster.CopyPixels()[2]);
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.NextFrame(TestContext.Current.CancellationToken));
    }
}
