namespace HavenOS.Apps.Wave;

public sealed record WaveAsset(string Id, string Path, string Name, TimeSpan Duration, WaveMediaInfo MediaInfo);
public sealed record WaveMediaInfo(int Width, int Height, double FrameRate, string VideoCodec, string AudioCodec, int AudioChannels, int SampleRate);
public sealed record WaveTimeline(
    string Id,
    string Name,
    TimeSpan Duration,
    IReadOnlyList<WaveTrack> Tracks,
    IReadOnlyList<WaveClip> Clips);
public sealed record WaveTrack(string Id, string Name, WaveTrackType Type, int Priority, bool Muted, bool Locked);
public sealed record WaveClip(
    string Id,
    string TrackId,
    string AssetId,
    TimeSpan Start,
    TimeSpan Duration,
    TimeSpan InPoint,
    TimeSpan OutPoint,
    double Speed,
    double Volume);
public sealed record WaveTransition(string Id, string ClipAId, string ClipBId, TimeSpan Duration, string Type);
public sealed record WaveEffect(string Id, string ClipId, string Type, IReadOnlyDictionary<string, object> Parameters);

public enum WaveTrackType
{
    Video = 0,
    Audio = 1,
    Subtitle = 2,
}

public sealed record WaveExportSettings(string OutputPath, string Container, string VideoCodec, string AudioCodec, int Width, int Height, double FrameRate, int Bitrate, string Preset);

public interface IWaveEngine : IAsyncDisposable
{
    Task<WaveAsset> ImportAssetAsync(string path, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WaveAsset>> GetAssetsAsync(CancellationToken cancellationToken = default);
    Task RemoveAssetAsync(string assetId, CancellationToken cancellationToken = default);
    Task<WaveTimeline> CreateTimelineAsync(string name, TimeSpan duration, CancellationToken cancellationToken = default);
    Task<WaveTimeline> GetTimelineAsync(string timelineId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WaveTimeline>> GetTimelinesAsync(CancellationToken cancellationToken = default);
    Task<WaveTimeline> UpdateTimelineAsync(WaveTimeline timeline, CancellationToken cancellationToken = default);
    Task DeleteTimelineAsync(string timelineId, CancellationToken cancellationToken = default);
    Task<WaveTrack> AddTrackAsync(string timelineId, WaveTrack track, CancellationToken cancellationToken = default);
    Task<WaveClip> AddClipAsync(string timelineId, WaveClip clip, CancellationToken cancellationToken = default);
    Task<WaveClip> UpdateClipAsync(string clipId, WaveClip clip, CancellationToken cancellationToken = default);
    Task RemoveClipAsync(string clipId, CancellationToken cancellationToken = default);
    Task<WaveTransition> AddTransitionAsync(string timelineId, WaveTransition transition, CancellationToken cancellationToken = default);
    Task<WaveEffect> AddEffectAsync(string clipId, WaveEffect effect, CancellationToken cancellationToken = default);
    Task<WaveEffect> UpdateEffectAsync(string effectId, WaveEffect effect, CancellationToken cancellationToken = default);
    Task RemoveEffectAsync(string effectId, CancellationToken cancellationToken = default);
    Task<WaveExportJob> ExportAsync(string timelineId, WaveExportSettings settings, CancellationToken cancellationToken = default);
    Task<WaveExportJob> GetExportJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task CancelExportAsync(string jobId, CancellationToken cancellationToken = default);
    void SetEventCallback(Action<WaveEngineEvent> callback);
}

public enum WaveEngineEventType
{
    AssetImported = 0,
    AssetRemoved = 1,
    TimelineCreated = 2,
    TimelineUpdated = 3,
    TimelineDeleted = 4,
    ClipAdded = 5,
    ClipUpdated = 6,
    ClipRemoved = 7,
    TransitionAdded = 8,
    EffectAdded = 9,
    EffectUpdated = 10,
    EffectRemoved = 11,
    ExportStarted = 12,
    ExportProgress = 13,
    ExportCompleted = 14,
    ExportFailed = 15,
}

public sealed record WaveEngineEvent(WaveEngineEventType Type, string Payload);

public sealed record WaveExportJob(
    string Id,
    string TimelineId,
    WaveExportSettings Settings,
    WaveExportStatus Status,
    double Progress,
    string? OutputPath,
    string? Error);

public enum WaveExportStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
}

public sealed class WaveAppService(IWaveEngine engine)
{
    public IWaveEngine Engine { get; } = engine ?? throw new ArgumentNullException(nameof(engine));
}