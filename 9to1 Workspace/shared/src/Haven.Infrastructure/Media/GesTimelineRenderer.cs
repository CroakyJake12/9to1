using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Haven.Core.Media;

namespace Haven.Infrastructure.Media;

/// <summary>
/// Production GES command-line timeline adapter, shared by media surfaces. Only typed placements/formats
/// become backend arguments; arbitrary pipelines and page/model-authored shell text are never accepted.
/// </summary>
public sealed class GesTimelineRenderer(string executablePath = "ges-launch-1.0", MediaRenderLimits? limits = null) : IMediaTimelineRenderer
{
    public MediaRenderLimits Limits { get; } = limits ?? new();
    public async Task<MediaEngineResult<MediaRenderOutput>> RenderAsync(MediaTimelineRenderRequest request,
        IProgress<MediaRenderProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sourcePins = new Dictionary<MediaAssetId, MediaRenderSourcePin>();
        string? temporaryPath = null;
        Process? process = null;
        try
        {
            Limits.Validate();
            request = request with { Clips = request.Clips?.Take(checked(Limits.MaximumClipCount + 1)).ToArray()! };
            Validate(request);
            cancellationToken.ThrowIfCancellationRequested();
            var outputPath = Path.GetFullPath(request.OutputPath);
            if (File.Exists(outputPath) || Directory.Exists(outputPath))
                return Failure(MediaEngineErrorCode.ExportFailed, "The output already exists; choose a new Files export target.");
            var directory = Path.GetDirectoryName(outputPath)!;
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(directory, ".render-" + request.JobID.ToString("N") + "-" + Guid.NewGuid().ToString("N") +
                (request.Format == MediaRenderFormat.WebmVp8Video ? ".webm" : ".wav"));
            foreach (var source in request.Clips.Select(clip => clip.Source).DistinctBy(source => source.AssetId))
            {
                if (Path.GetFullPath(source.SourceUri.LocalPath) == outputPath) return Failure(MediaEngineErrorCode.ExportFailed, "An export cannot replace its source.");
                sourcePins.Add(source.AssetId, new(source.AssetId, source.SourceRevisionId!,
                    await HashAsync(source.SourceUri.LocalPath, cancellationToken).ConfigureAwait(false)));
            }
            var start = new ProcessStartInfo(executablePath)
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            void Argument(string value) => start.ArgumentList.Add(value);
            Argument("--no-interactive");
            // Rendering must not probe/use user microphone/speaker/display preview devices.
            Argument("--audiosink=fakesink"); Argument("--videosink=fakesink");
            Argument("--track-types=" + (request.Format == MediaRenderFormat.WebmVp8Video ? "video" : "audio"));
            Argument("--format=" + (request.Format == MediaRenderFormat.WebmVp8Video ? "video/webm:video/x-vp8" : "audio/x-wav:audio/x-raw"));
            Argument("--outputuri=" + new Uri(temporaryPath).AbsoluteUri);
            if (request.Format == MediaRenderFormat.WebmVp8Video)
                Argument(FormattableString.Invariant($"--video-caps=video/x-raw,width={request.Width},height={request.Height},framerate={request.FrameRateNumerator}/{request.FrameRateDenominator}"));
            if (request.Format == MediaRenderFormat.WavePcmAudio)
                Argument(FormattableString.Invariant($"--audio-caps=audio/x-raw,format=S16LE,rate={request.AudioSampleRate},channels={request.AudioChannels}"));
            foreach (var clip in request.Clips.OrderBy(clip => clip.Layer).ThenBy(clip => clip.Clip.TimelineStart).ThenBy(clip => clip.Clip.ClipId))
            {
                Argument("+clip"); Argument(clip.Source.SourceUri.AbsoluteUri);
                Argument("name=" + clip.Clip.ClipId.ToString("N"));
                Argument("layer=" + clip.Layer.ToString(CultureInfo.InvariantCulture));
                Argument("start=" + Seconds(clip.Clip.TimelineStart));
                Argument("duration=" + Seconds(clip.Clip.Duration));
                Argument("inpoint=" + Seconds(clip.Clip.SourceStart));
            }
            cancellationToken.ThrowIfCancellationRequested();
            process = Process.Start(start) ?? throw new IOException("The GES renderer could not start.");
            Report(new(request.JobID, MediaRenderJobState.Running, 0));
            // Drain both streams concurrently with bounded memory. Backend diagnostics are not returned as
            // tool results because they may contain resolved private source paths/content.
            var stdout = DrainAsync(process.StandardOutput);
            var stderr = DrainAsync(process.StandardError);
            try { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                Report(new(request.JobID, MediaRenderJobState.Cancelled, null));
                return Failure(MediaEngineErrorCode.OperationCancelled, "Rendering was cancelled; no complete export was published.");
            }
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            if (process.ExitCode != 0 || !File.Exists(temporaryPath) || new FileInfo(temporaryPath).Length == 0)
            {
                Report(new(request.JobID, MediaRenderJobState.Failed, null));
                return Failure(MediaEngineErrorCode.ExportFailed, "GES did not complete a valid output; inspect installed codecs and source availability.");
            }
            foreach (var source in request.Clips.Select(clip => clip.Source).DistinctBy(source => source.AssetId))
                if (await HashAsync(source.SourceUri.LocalPath, cancellationToken).ConfigureAwait(false) != sourcePins[source.AssetId].ObservedSha256)
                    return Failure(MediaEngineErrorCode.RevisionConflict, "A source changed during rendering; the derived output was discarded.");
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = new FileInfo(temporaryPath).Length;
            File.Move(temporaryPath, outputPath); // Atomic publish, refuses concurrent replacement.
            Report(new(request.JobID, MediaRenderJobState.Completed, 1));
            return MediaEngineResult<MediaRenderOutput>.Success(new(request.JobID, request.ProjectID, request.ProjectRevision,
                request.SequenceID, outputPath, bytes, sourcePins.Values.ToArray()));
        }
        catch (OperationCanceledException)
        { return Failure(MediaEngineErrorCode.OperationCancelled, "Rendering was cancelled; no complete export was published."); }
        catch (FileNotFoundException)
        { return Failure(MediaEngineErrorCode.SourceUnavailable, "A Files-resolved source is offline."); }
        catch (UnauthorizedAccessException)
        { return Failure(MediaEngineErrorCode.PermissionDenied, "Storage permissions prevented this render."); }
        catch (Win32Exception)
        { return Failure(MediaEngineErrorCode.BackendUnavailable, "The configured GES runtime is unavailable."); }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or OverflowException or NotSupportedException)
        { return Failure(MediaEngineErrorCode.UnsupportedSource, "The render configuration is invalid or unsupported."); }
        catch (IOException)
        { return Failure(MediaEngineErrorCode.ExportFailed, "Storage failed while rendering; no complete export was published."); }
        finally
        {
            process?.Dispose();
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { /* Storage failure must not hide the observed render outcome. */ }
                catch (UnauthorizedAccessException) { }
            }
        }

        void Report(MediaRenderProgress value)
        {
            // Observers cannot interrupt process ownership or turn a published export into a failure.
            try { progress?.Report(value); }
            catch (Exception) { }
        }

        MediaEngineResult<MediaRenderOutput> Failure(MediaEngineErrorCode code, string message) =>
            MediaEngineResult<MediaRenderOutput>.Failure(new(code, message, "9to1.Media.Render", request.JobID.ToString("N"), true,
                code is not (MediaEngineErrorCode.PermissionDenied or MediaEngineErrorCode.RevisionConflict)));
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer).ConfigureAwait(false) > 0) { }
    }
    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }
    private static string Seconds(MediaTime time) =>
        (time.ConvertTo(MediaTimebase.Nanoseconds).Ticks / 1_000_000_000m).ToString("0.000000000", CultureInfo.InvariantCulture);

    private void Validate(MediaTimelineRenderRequest request)
    {
        if (request.JobID == Guid.Empty || request.ProjectID == Guid.Empty || request.SequenceID == Guid.Empty
            || request.ProjectRevision < 0 || !Enum.IsDefined(request.Format) || string.IsNullOrWhiteSpace(request.OutputPath)
            || request.Width > Limits.MaximumWidth || request.Height > Limits.MaximumHeight
            || request.Clips?.Count > Limits.MaximumClipCount
            || request.FrameRateNumerator > (long)Limits.MaximumFrameRate * request.FrameRateDenominator
            || request.FrameRateNumerator <= 0 || request.FrameRateDenominator <= 0 || request.Width <= 0 || request.Height <= 0
            || request.AudioSampleRate is < 8000 or > 384000 || request.AudioChannels is < 1 or > 32
            || request.Clips is not { Count: > 0 } || request.Clips.Any(clip => clip is null || clip.Clip is null || clip.Source is null)
            || request.Clips.Select(clip => clip.Clip.ClipId).Distinct().Count() != request.Clips.Count)
            throw new InvalidDataException("Invalid render identity, format, dimensions, timing or clips.");
        foreach (var layer in request.Clips.GroupBy(clip => clip.Layer))
        {
            var ordered = layer.OrderBy(clip => clip.Clip.TimelineStart).ToArray();
            if (ordered.Zip(ordered.Skip(1), (left, right) => left.Clip.TimelineRange.End > right.Clip.TimelineStart).Any(overlap => overlap))
                throw new InvalidDataException("Unspecified overlap/transition on one render layer.");
        }
        var sources = new Dictionary<MediaAssetId, MediaAssetSource>();
        foreach (var clip in request.Clips)
        {
            clip.Clip.Validate(); clip.Source.Validate();
            if (clip.Layer < 0 || clip.Layer >= Limits.MaximumLayerCount || clip.Source.SourceUri.Scheme != "file"
                || string.IsNullOrWhiteSpace(clip.Source.SourceRevisionId) || clip.Source.AssetId != clip.Clip.AssetId
                || clip.Kind != (request.Format == MediaRenderFormat.WebmVp8Video ? MediaTrackKind.Video : MediaTrackKind.Audio))
                throw new InvalidDataException("Unsupported or unpinned source/timeline clip.");
            if (sources.TryGetValue(clip.Source.AssetId, out var previous) && previous != clip.Source)
                throw new InvalidDataException("An asset has inconsistent source resolution or revision.");
            sources[clip.Source.AssetId] = clip.Source;
            if (clip.Clip.TimelineRange.End > MediaTimebase.Nanoseconds.At(checked(Limits.MaximumTimelineSeconds * 1_000_000_000L)))
                throw new InvalidDataException("Timeline exceeds configured host resource policy.");
            _ = Seconds(clip.Clip.TimelineStart); _ = Seconds(clip.Clip.SourceStart); _ = Seconds(clip.Clip.Duration);
        }
    }
}
