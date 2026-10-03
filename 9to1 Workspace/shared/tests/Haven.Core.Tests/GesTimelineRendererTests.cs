using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Core.Media;
using Haven.Infrastructure.Media;
using Xunit;

namespace Haven.Core.Tests;

public sealed class GesTimelineRendererTests
{
    [GesRuntimeFact]
    public async Task Actual_GES_trim_render_retains_source_and_canonical_revision_and_has_25_observed_frames()
    {
        using var paths = new Paths();
        var request = Request(paths.Output);
        var sourcePath = request.Clips[0].Source.SourceUri.LocalPath;
        var before = await Hash(sourcePath);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var result = await new GesTimelineRenderer(Runtime()).RenderAsync(request, cancellationToken: deadline.Token);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(request.ProjectID, result.Value!.ProjectID);
        Assert.Equal(request.ProjectRevision, result.Value.ProjectRevision);
        Assert.Equal(request.SequenceID, result.Value.SequenceID);
        Assert.Equal(request.Clips[0].Clip.AssetId, Assert.Single(result.Value.Sources).AssetID);
        Assert.Equal(before, await Hash(sourcePath));
        Assert.Equal(before, result.Value.Sources[0].ObservedSha256);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("ASTRA_FFPROBE") ?? "ffprobe")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "-v", "error", "-count_frames", "-select_streams", "v:0", "-show_entries", "stream=width,height,r_frame_rate,nb_read_frames:format=duration", "-of", "json", paths.Output })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var json = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
        using var observed = JsonDocument.Parse(json);
        var stream = observed.RootElement.GetProperty("streams")[0];
        Assert.Equal(160, stream.GetProperty("width").GetInt32());
        Assert.Equal(120, stream.GetProperty("height").GetInt32());
        Assert.Equal("25/1", stream.GetProperty("r_frame_rate").GetString());
        Assert.Equal("25", stream.GetProperty("nb_read_frames").GetString());
        Assert.Empty(Directory.GetFiles(paths.Root, ".render-*"));
    }

    [GesRuntimeFact]
    public async Task Cancellation_of_started_native_render_kills_job_and_does_not_publish_partial_export()
    {
        using var paths = new Paths();
        using var cancellation = new CancellationTokenSource();
        var request = Request(paths.Output);
        var result = await new GesTimelineRenderer(Runtime()).RenderAsync(request,
            new CallbackProgress(value => { if (value.State == MediaRenderJobState.Running) cancellation.Cancel(); }), cancellation.Token);
        Assert.False(result.IsSuccess);
        Assert.Equal(MediaEngineErrorCode.OperationCancelled, result.Error!.Code);
        Assert.False(File.Exists(paths.Output));
        Assert.Empty(Directory.GetFiles(paths.Root, ".render-*"));
    }

    [GesAudioRuntimeFact]
    public async Task Actual_GES_audio_render_resamples_into_requested_PCM_rate_and_channels()
    {
        using var paths = new Paths();
        var timebase = MediaTimebase.SamplesPerSecond(48000);
        var asset = MediaAssetId.New();
        var source = new MediaAssetSource(asset, Guid.NewGuid(), new Uri(Path.GetFullPath(Environment.GetEnvironmentVariable("ASTRA_GSTREAMER_FIXTURE")!)), "fixture-audio-revision-1");
        var output = Path.Combine(paths.Root, "audio.wav");
        var request = new MediaTimelineRenderRequest(Guid.NewGuid(), Guid.NewGuid(), 3, Guid.NewGuid(), 1, 1, 25, 1,
            [new(new(Guid.NewGuid(), Guid.NewGuid(), asset, timebase.At(0), timebase.At(4800), timebase.At(0)), MediaTrackKind.Audio, 0, source)],
            MediaRenderFormat.WavePcmAudio, output, 48000, 2);
        var before = await Hash(source.SourceUri.LocalPath);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var result = await new GesTimelineRenderer(Runtime()).RenderAsync(request, cancellationToken: deadline.Token);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(before, await Hash(source.SourceUri.LocalPath));
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("ASTRA_FFPROBE") ?? "ffprobe")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "-v", "error", "-show_entries", "stream=sample_rate,channels,codec_name:format=duration", "-of", "json", output })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var json = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
        using var observed = JsonDocument.Parse(json);
        var stream = observed.RootElement.GetProperty("streams")[0];
        Assert.Equal("48000", stream.GetProperty("sample_rate").GetString());
        Assert.Equal(2, stream.GetProperty("channels").GetInt32());
        Assert.Equal("pcm_s16le", stream.GetProperty("codec_name").GetString());
        var duration = double.Parse(observed.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        // GES audio resampling may round the boundary by two output samples. This adapter
        // validates real PCM output; it does not claim Wave sample-exact mastering acceptance.
        Assert.InRange(Math.Abs(duration - 0.1), 0, 2.1 / 48000);
    }

    [GesRuntimeFact]
    public async Task Mutable_caller_collection_and_failing_progress_observer_cannot_retarget_started_job()
    {
        using var paths = new Paths();
        var request = Request(paths.Output);
        var original = request.Clips.Single();
        var callerClips = request.Clips.ToList();
        request = request with { Clips = callerClips };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var result = await new GesTimelineRenderer(Runtime()).RenderAsync(request, new CallbackProgress(progress =>
        {
            if (progress.State == MediaRenderJobState.Running) callerClips.Clear();
            throw new InvalidOperationException("UI observer failure");
        }), deadline.Token);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Empty(callerClips);
        Assert.Equal(original.Source.AssetId, Assert.Single(result.Value!.Sources).AssetID);
        Assert.True(File.Exists(paths.Output));
    }

    [Fact]
    public async Task Configured_resource_policy_rejects_oversized_job_before_native_launch()
    {
        using var paths = new Paths();
        var source = Path.Combine(paths.Root, "source.bin");
        await File.WriteAllBytesAsync(source, [1]);
        var renderer = new GesTimelineRenderer("must-not-start", new(MaximumWidth: 128));
        var result = await renderer.RenderAsync(Request(paths.Output, source));
        Assert.Equal(MediaEngineErrorCode.UnsupportedSource, result.Error!.Code);
        Assert.False(File.Exists(paths.Output));
    }

    [Fact]
    public async Task Unknown_runtime_cannot_report_a_completed_export()
    {
        using var paths = new Paths();
        var source = Path.Combine(paths.Root, "source.bin");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        var request = Request(paths.Output, source);
        var result = await new GesTimelineRenderer(Path.Combine(paths.Root, "missing-runtime")).RenderAsync(request);
        Assert.Equal(MediaEngineErrorCode.BackendUnavailable, result.Error!.Code);
        Assert.False(File.Exists(paths.Output));
    }

    [Fact]
    public async Task Inconsistent_asset_revision_and_existing_target_are_rejected_without_source_or_output_mutation()
    {
        using var paths = new Paths();
        var source = Path.Combine(paths.Root, "source.bin");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        await File.WriteAllTextAsync(paths.Output, "previous export");
        var request = Request(paths.Output, source);
        var result = await new GesTimelineRenderer("unused").RenderAsync(request);
        Assert.Equal(MediaEngineErrorCode.ExportFailed, result.Error!.Code);
        Assert.Equal("previous export", await File.ReadAllTextAsync(paths.Output));
        var original = request.Clips[0];
        result = await new GesTimelineRenderer("unused").RenderAsync(request with
        { Clips = [original, original with { Clip = original.Clip with { ClipId = Guid.NewGuid(), TimelineStart = MediaTimebase.FramesPerSecond(25).At(25) },
            Source = original.Source with { SourceRevisionId = "different-revision" } }] });
        Assert.Equal(MediaEngineErrorCode.UnsupportedSource, result.Error!.Code);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(source));
    }

    private static string Runtime() => Environment.GetEnvironmentVariable("ASTRA_GES_EXECUTABLE")!;
    private static MediaTimelineRenderRequest Request(string output, string? sourcePath = null)
    {
        var asset = MediaAssetId.New();
        var timebase = MediaTimebase.FramesPerSecond(25);
        sourcePath ??= Environment.GetEnvironmentVariable("ASTRA_MOTION_FIXTURE")!;
        var source = new MediaAssetSource(asset, Guid.NewGuid(), new Uri(Path.GetFullPath(sourcePath)), "test-source-revision-1");
        return new(Guid.NewGuid(), Guid.NewGuid(), 7, Guid.NewGuid(), 160, 120, 25, 1,
            [new(new(Guid.NewGuid(), Guid.NewGuid(), asset, timebase.At(12), timebase.At(25), timebase.At(0)), MediaTrackKind.Video, 0, source)],
            MediaRenderFormat.WebmVp8Video, output);
    }
    private static async Task<string> Hash(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }
    private sealed class CallbackProgress(Action<MediaRenderProgress> callback) : IProgress<MediaRenderProgress>
    { public void Report(MediaRenderProgress value) => callback(value); }
    private sealed class GesRuntimeFactAttribute : FactAttribute
    {
        public GesRuntimeFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASTRA_GES_EXECUTABLE")) ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASTRA_MOTION_FIXTURE")))
                Skip = "Requires configured actual GES runtime and motion source fixture.";
        }
    }
    private sealed class GesAudioRuntimeFactAttribute : FactAttribute
    {
        public GesAudioRuntimeFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASTRA_GES_EXECUTABLE")) ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASTRA_GSTREAMER_FIXTURE")))
                Skip = "Requires configured actual GES runtime and audio source fixture.";
        }
    }
    private sealed class Paths : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "astra-ges-" + Guid.NewGuid().ToString("N"));
        public string Output => Path.Combine(Root, "export.webm");
        public Paths() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, true);
    }
}
