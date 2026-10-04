using System.Runtime.ExceptionServices;
using HavenOS.Home.Core;
using HavenOS.Images;
using Xunit;

namespace HavenOS.AIStudio.Tests;

/// <summary>Real selected native codecs and encoded sources; no synthetic decoder or skipped platform case.</summary>
public sealed class StudioSharedRasterProviderTests
{
    private const string TwoFrames = "R0lGODlhAgABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACAAAACwAAAAAAgABAAAIBQABAAgIACH5BAAMAAAALAAAAAACAAEAgQAA/wAAAAAAAAAAAAgFAAEACAgAOw==";
    // Same wire shape as the maintained Picture transparency fixture.
    private const string TransparentPng = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAAEUlEQVR42mP4z8DQwMDw/z8ADX4DfpfxiD8AAAAASUVORK5CYII=";
    private const string SingleTransparentGif = "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAICRAEAOw==";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Actual_platform_provider_preserves_real_gif_frames_timing_loop_and_detached_copies(bool loop)
    {
        var source = Convert.FromBase64String(TwoFrames);
        HomeProductivityRasterFrame? retained = null;
        try
        {
            WithOriginalFrames(source, loop, session =>
            {
                Array.Clear(source);
                Assert.Equal("image/gif", session.MimeType);
                var first = Assert.IsType<PictureSharedAnimationFrame>(session.TryNextFrame(default));
                Assert.Equal(2, first.Raster.Width); Assert.Equal(1, first.Raster.Height); Assert.Equal(8, first.Raster.Stride);
                Assert.Equal(80_000, first.DelayMicroseconds);
                AssertPixels(first.Raster, [0, 0, 255, 255, 0, 0, 255, 255]);
                retained = first.Raster;
                var copy = first.Raster.CopyPixels(); Array.Clear(copy);
                AssertPixels(first.Raster, [0, 0, 255, 255, 0, 0, 255, 255]);
                var second = Assert.IsType<PictureSharedAnimationFrame>(session.TryNextFrame(default));
                Assert.Equal(120_000, second.DelayMicroseconds);
                AssertPixels(second.Raster, [255, 0, 0, 255, 255, 0, 0, 255]);
                if (loop)
                {
                    var repeated = Assert.IsType<PictureSharedAnimationFrame>(session.TryNextFrame(default));
                    Assert.Equal(80_000, repeated.DelayMicroseconds);
                    AssertPixels(repeated.Raster, [0, 0, 255, 255, 0, 0, 255, 255]);
                }
                else { Assert.Null(session.TryNextFrame(default)); Assert.Null(session.TryNextFrame(default)); }
            });
            // Closing the provider cannot clear a returned independent frame.
            Assert.NotNull(retained);
            AssertPixels(retained!, [0, 0, 255, 255, 0, 0, 255, 255]);
        }
        finally { Array.Clear(source); }
    }

    [Fact]
    public void Actual_single_frame_gif_preserves_transparency_and_selected_terminal_behavior()
    {
        var source = Convert.FromBase64String(SingleTransparentGif);
        try
        {
            WithOriginalFrames(source, OperatingSystem.IsWindows(), session =>
            {
                var first = Assert.IsType<PictureSharedAnimationFrame>(session.TryNextFrame(default));
                Assert.Equal(1, first.Raster.Width); Assert.Equal(1, first.Raster.Height);
                Assert.Equal(0, first.DelayMicroseconds); AssertPixels(first.Raster, [0, 0, 0, 0]);
                Assert.Null(session.TryNextFrame(default));
            });
        }
        finally { Array.Clear(source); }
    }

    [Fact]
    public void Actual_original_caller_cancellation_and_repeated_resource_close_remain_distinct()
    {
        var source = Convert.FromBase64String(TwoFrames);
        var provider = SelectedProvider();
        IPictureSharedRasterFrameSession? original = null;
        var errors = new List<Exception>();
        try
        {
            using var caller = new CancellationTokenSource();
            caller.Cancel();
            var before = Assert.Throws<OperationCanceledException>(() => provider.OpenFrames(source, false, caller.Token));
            Assert.Equal(caller.Token, before.CancellationToken);
            original = provider.OpenFrames(source, false, default);
            Assert.NotNull(original.TryNextFrame(default));
            var error = Assert.Throws<OperationCanceledException>(() => original.TryNextFrame(caller.Token));
            Assert.Equal(caller.Token, error.CancellationToken);
            // Resource close joins the original native call; it does not replay
            // an already returned caller cancellation as a new cleanup failure.
            original.Dispose(); original.Dispose();
            Assert.Throws<ObjectDisposedException>(() => original.TryNextFrame(default));
        }
        catch (Exception error) { Add(errors, error); }
        finally
        {
            if (original is not null) Attempt(errors, original.Dispose);
            Attempt(errors, () => Array.Clear(source));
        }
        Throw(errors);
    }

    [Fact]
    public void Explicit_platform_selection_keeps_linux_png_and_refuses_unproved_windows_metadata()
    {
        var source = Convert.FromBase64String(TransparentPng);
        try
        {
            var provider = SelectedProvider();
            if (OperatingSystem.IsWindows())
            {
                var error = Assert.Throws<PictureSharedRasterUnavailableException>(() =>
                    provider.OpenFrames(source, false, default));
                Assert.Equal("image/png", error.DetectedMimeType);
                Assert.Equal(ImageMetadataAvailability.UnsupportedByReader, error.MetadataAvailability);
            }
            else
            {
                WithOriginalFrames(source, false, session =>
                {
                    Assert.Equal("image/png", session.MimeType);
                    var first = Assert.IsType<PictureSharedAnimationFrame>(session.TryNextFrame(default));
                    AssertPixels(first.Raster, [0, 0, 128, 128, 255, 0, 0, 255]);
                });
            }
        }
        finally { Array.Clear(source); }
    }

    [Fact]
    public void Actual_native_decoder_refuses_a_truncated_source_after_a_genuine_positive()
    {
        var source = Convert.FromBase64String(TwoFrames);
        try
        {
            WithOriginalFrames(source, false, session => Assert.NotNull(session.TryNextFrame(default)));
            var truncated = source.AsSpan(0, 6).ToArray();
            try
            {
                var error = Record.Exception(() =>
                    WithOriginalFrames(truncated, false, session => _ = session.TryNextFrame(default)));
                Assert.True(error is IOException or InvalidDataException);
            }
            finally { Array.Clear(truncated); }
        }
        finally { Array.Clear(source); }
    }

    private static IPictureSharedRasterDecoder SelectedProvider()
    {
        var provider = PictureSharedRasterProviders.ForCurrentPlatform();
        if (OperatingSystem.IsWindows()) Assert.IsType<PictureSkiaSharedRasterDecoder>(provider);
        else Assert.IsType<PictureGlycinSharedRasterProvider>(provider);
        return provider;
    }
    private static void WithOriginalFrames(byte[] source, bool loop, Action<IPictureSharedRasterFrameSession> body)
    {
        IPictureSharedRasterFrameSession? original = null;
        var errors = new List<Exception>();
        try { original = SelectedProvider().OpenFrames(source, loop, default); body(original); }
        catch (Exception error) { Add(errors, error); }
        finally { if (original is not null) Attempt(errors, original.Dispose); }
        Throw(errors);
    }
    private static void AssertPixels(HomeProductivityRasterFrame frame, byte[] expected)
    {
        var pixels = frame.CopyPixels();
        try { Assert.Equal(expected, pixels); }
        finally { Array.Clear(pixels); }
    }
    private static void Attempt(List<Exception> errors, Action action)
    {
        try { action(); } catch (Exception error) { Add(errors, error); }
    }
    private static void Add(List<Exception> errors, Exception error)
    {
        if (!errors.Any(existing => ReferenceEquals(existing, error))) errors.Add(error);
    }
    private static void Throw(List<Exception> errors)
    {
        if (errors.Count != 0)
            ExceptionDispatchInfo.Capture(errors.Count == 1 ? errors[0] : new AggregateException(errors)).Throw();
    }
}
