using Haven.Core.Media;
using Haven.Infrastructure.Media;

namespace Haven.Core.Tests;

public sealed class GStreamerMediaRuntimeTests
{
    [Fact]
    public async Task Explicit_runtime_fixture_decodes_prerolls_seeks_and_stops_through_native_app_adapter()
    {
        // Native validation is opted in by a supplied fixture; ordinary cross-platform runs
        // still exercise fail-closed capability negotiation without requiring a local runtime.
        var fixture = Environment.GetEnvironmentVariable("ASTRA_GSTREAMER_FIXTURE");
        var engine = GStreamerMediaEngine.CreateHeadlessValidationEngine();
        var capabilities = await engine.GetCapabilitiesAsync();
        if (string.IsNullOrWhiteSpace(fixture))
        {
            Assert.Equal(capabilities.GStreamerAvailable, capabilities.GStreamerVersion is not null);
            return;
        }
        Assert.True(capabilities.GStreamerAvailable, string.Join(", ", capabilities.MissingComponents));
        Assert.True(capabilities.AudioPlaybackAvailable);
        var source = new MediaAssetSource(MediaAssetId.New(), Guid.NewGuid(), new Uri(Path.GetFullPath(fixture)));
        var opened = await engine.OpenPlaybackAsync(source);
        Assert.True(opened.IsSuccess, opened.Error?.Message);
        await using var session = opened.Value!;
        var paused = await session.SetStateAsync(MediaPlaybackState.Paused);
        Assert.True(paused.IsSuccess, paused.Error?.Message);
        var position = await session.GetPositionAsync();
        Assert.True(position.IsSuccess, position.Error?.Message);
        var seek = await session.SeekAsync(MediaTimebase.Nanoseconds.At(100_000_000));
        Assert.True(seek.IsSuccess, seek.Error?.Message);
        var playing = await session.SetStateAsync(MediaPlaybackState.Playing);
        Assert.True(playing.IsSuccess, playing.Error?.Message);
        var stopped = await session.SetStateAsync(MediaPlaybackState.Stopped);
        Assert.True(stopped.IsSuccess, stopped.Error?.Message);
        Assert.Equal(MediaPlaybackState.Stopped, session.State);
    }

    [Fact]
    public async Task Missing_source_cannot_be_reported_as_successfully_prerolled()
    {
        var engine = GStreamerMediaEngine.CreateHeadlessValidationEngine();
        var source = new MediaAssetSource(MediaAssetId.New(), Guid.NewGuid(), new Uri(Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid() + ".wav")));
        var opened = await engine.OpenPlaybackAsync(source);
        if (!opened.IsSuccess) { Assert.NotNull(opened.Error); return; }
        await using var session = opened.Value!;
        var paused = await session.SetStateAsync(MediaPlaybackState.Paused);
        Assert.False(paused.IsSuccess);
    }
}
