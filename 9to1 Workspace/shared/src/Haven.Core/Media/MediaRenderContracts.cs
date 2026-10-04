namespace Haven.Core.Media;

public enum MediaRenderFormat { WebmVp8Video, WavePcmAudio }
public enum MediaRenderJobState { Running, Completed, Cancelled, Failed }
public sealed record MediaRenderClip(MediaTimelineClip Clip, MediaTrackKind Kind, int Layer, MediaAssetSource Source);
public sealed record MediaTimelineRenderRequest(Guid JobID, Guid ProjectID, long ProjectRevision, Guid SequenceID,
    int Width, int Height, int FrameRateNumerator, int FrameRateDenominator,
    IReadOnlyList<MediaRenderClip> Clips, MediaRenderFormat Format, string OutputPath, int AudioSampleRate = 48000, int AudioChannels = 2);
public sealed record MediaRenderProgress(Guid JobID, MediaRenderJobState State, double? Fraction);
public sealed record MediaRenderSourcePin(MediaAssetId AssetID, string Revision, string ObservedSha256);
public sealed record MediaRenderOutput(Guid JobID, Guid ProjectID, long ProjectRevision, Guid SequenceID,
    string OutputPath, long Bytes, IReadOnlyList<MediaRenderSourcePin> Sources);

/// <summary>Shared revision-pinned render boundary; source URIs are operation-scoped Files resolutions.</summary>
public interface IMediaTimelineRenderer
{
    Task<MediaEngineResult<MediaRenderOutput>> RenderAsync(MediaTimelineRenderRequest request,
        IProgress<MediaRenderProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>Files owns immutable revision materialisation and releases its operation lease.</summary>
public sealed class MediaAssetReadLease : IAsyncDisposable
{
    private Func<ValueTask>? _release;
    public MediaAssetSource Source { get; }
    public MediaAssetReadLease(MediaAssetSource source, Func<ValueTask> release)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Source.Validate();
        _release = release ?? throw new ArgumentNullException(nameof(release));
    }
    public ValueTask DisposeAsync() => Interlocked.Exchange(ref _release, null)?.Invoke() ?? ValueTask.CompletedTask;
}
public interface IMediaAssetSourceResolver
{
    Task<MediaEngineResult<MediaAssetReadLease>> ResolveAsync(string fileID, MediaAssetId assetID,
        string? expectedRevision, CancellationToken cancellationToken = default);
}
