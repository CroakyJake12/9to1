using Haven.Core.Media;

namespace HavenOS.Apps.Motion;

/// <summary>Media access stays in Files and shared media engines. No resolved URI is written into the project.</summary>
public sealed class MotionMediaService(IMediaAssetSourceResolver sources, IMediaEngine engine, IMediaTimelineRenderer? renderer = null)
{
    private readonly object _cleanupGate = new();
    private readonly List<(IAsyncDisposable Resource, Task ActualClose)> _failedCleanup = [];
    private void RetainFailedCleanup(IAsyncDisposable resource, Task actualClose)
    { lock (_cleanupGate) _failedCleanup.Add((resource, actualClose)); }
    public bool CanRender => renderer is not null;
    public Task<MediaEngineCapabilities> CapabilitiesAsync(CancellationToken cancellationToken = default) => engine.GetCapabilitiesAsync(cancellationToken);
    public Task<MotionSourcePlayback> OpenSourceAsync(MotionAssetReference asset, CancellationToken cancellationToken = default)
        => OpenSourceCoreAsync(asset, cancellationToken, null);
    internal Task<MotionSourcePlayback> OpenSourceOriginalAsync(MotionAssetReference asset, CancellationToken token, MotionOriginalSourceObserver originals)
        => OpenSourceCoreAsync(asset, token, originals);
    private async Task<MotionSourcePlayback> OpenSourceCoreAsync(MotionAssetReference asset, CancellationToken token, MotionOriginalSourceObserver? originals)
    {
        var lease = await ResolveAsync(asset, token, originals).ConfigureAwait(false);
        var failures = new List<Exception>();
        IMediaPlaybackSession? session = null;
        try
        {
            var opened = await SourceAsync(() => engine.OpenPlaybackAsync(lease.Source, token), originals).ConfigureAwait(false);
            if (!opened.IsSuccess) throw new MotionMediaException(opened.Error!);
            session = opened.Value!;
            return new(lease, session, originals);
        }
        catch (Exception cause) { MotionOriginalFailures.Add(failures, cause); }
        if (session is not null)
        {
            var close = SourceAsync(() => session.DisposeAsync().AsTask(), originals);
            try { await close.ConfigureAwait(false); }
            catch (Exception cause) { RetainFailedCleanup(session, close); MotionOriginalFailures.Add(failures, cause); }
        }
        var leaseClose = SourceAsync(() => lease.DisposeAsync().AsTask(), originals);
        try { await leaseClose.ConfigureAwait(false); }
        catch (Exception cause) { RetainFailedCleanup(lease, leaseClose); MotionOriginalFailures.Add(failures, cause); }
        MotionOriginalFailures.Throw(failures);
        throw new InvalidOperationException("Playback did not produce a session.");
    }
    public Task<MediaRenderOutput> RenderAsync(MotionProject project, Guid sequenceId, string filesResolvedOutputPath,
        IProgress<MediaRenderProgress>? progress, CancellationToken cancellationToken)
        => RenderCoreAsync(project, sequenceId, filesResolvedOutputPath, progress, cancellationToken, null);
    internal Task<MediaRenderOutput> RenderOriginalAsync(MotionProject project, Guid sequenceId, string output,
        IProgress<MediaRenderProgress>? progress, CancellationToken token, MotionOriginalSourceObserver originals)
        => RenderCoreAsync(project, sequenceId, output, progress, token, originals);
    private async Task<MediaRenderOutput> RenderCoreAsync(MotionProject project, Guid sequenceId, string filesResolvedOutputPath,
        IProgress<MediaRenderProgress>? progress, CancellationToken token, MotionOriginalSourceObserver? originals)
    {
        if (renderer is null) throw new NotSupportedException("The shared timeline renderer is not installed.");
        MotionProjectStore.Validate(project);
        // Freeze structural collections before awaiting source leases. The job stays pinned to this revision.
        var sequence = project.Sequences.Single(s => s.SequenceId == sequenceId);
        if (sequence.CaptionTracks?.Any(t => t.Cues.Count > 0) == true)
            throw new NotSupportedException("The connected renderer does not accept caption tracks yet. Export captions as SRT or WebVTT separately.");
        var elements = sequence.VideoTracks.Where(t => t.Visible).SelectMany((t, layer) => t.Elements.Select(e => (Element: e, Layer: layer))).ToArray();
        var assets = project.AssetReferences.ToDictionary(a => a.AssetId);
        var leases = new Dictionary<Guid, MediaAssetReadLease>();
        var failures = new List<Exception>();
        MediaRenderOutput? output = null;
        try
        {
            foreach (var assetId in elements.Select(e => e.Element.AssetId).Distinct())
                leases.Add(assetId, await ResolveAsync(assets[assetId], token, originals).ConfigureAwait(false));
            var clips = elements.Select(e => new MediaRenderClip(MotionProjectStore.ToShared(sequence, e.Element), MediaTrackKind.Video, e.Layer, leases[e.Element.AssetId].Source)).ToArray();
            var request = new MediaTimelineRenderRequest(Guid.NewGuid(), project.ProjectId, project.Revision, sequenceId,
                sequence.Width, sequence.Height, sequence.FrameRateNumerator, sequence.FrameRateDenominator, clips,
                MediaRenderFormat.WebmVp8Video, filesResolvedOutputPath);
            var result = await SourceAsync(() => renderer.RenderAsync(request, progress, token), originals).ConfigureAwait(false);
            if (!result.IsSuccess) throw new MotionMediaException(result.Error!);
            output = result.Value!;
        }
        catch (Exception cause) { MotionOriginalFailures.Add(failures, cause); }
        // All leases get their actual close even when render or another close fails.
        foreach (var lease in leases.Values)
        {
            var close = SourceAsync(() => lease.DisposeAsync().AsTask(), originals);
            try { await close.ConfigureAwait(false); }
            catch (Exception cause) { RetainFailedCleanup(lease, close); MotionOriginalFailures.Add(failures, cause); }
        }
        MotionOriginalFailures.Throw(failures);
        return output!;
    }
    private async Task<MediaAssetReadLease> ResolveAsync(MotionAssetReference asset, CancellationToken token, MotionOriginalSourceObserver? originals)
    {
        if (!Guid.TryParse(asset.FileId, out var fileId) || fileId == Guid.Empty || string.IsNullOrWhiteSpace(asset.SourceRevisionID))
            throw new InvalidOperationException("This source needs a canonical Files identity and retained revision before playback or render.");
        var result = await SourceAsync(() => sources is IMediaRetainedAssetSourceResolver retained
            ? retained.ResolveRetainedAsync(asset.FileId, new(asset.AssetId), asset.SourceRevisionID, token)
            : sources.ResolveAsync(asset.FileId, new(asset.AssetId), asset.SourceRevisionID, token), originals).ConfigureAwait(false);
        if (!result.IsSuccess) throw new MotionMediaException(result.Error!);
        var lease = result.Value!;
        if (lease.Source.AssetId.Value == asset.AssetId && lease.Source.HostedItemId == fileId && lease.Source.SourceRevisionId == asset.SourceRevisionID) return lease;
        var failures = new List<Exception> { new InvalidOperationException("The source revision changed. Relink the existing reference in Files.") };
        var leaseClose = SourceAsync(() => lease.DisposeAsync().AsTask(), originals);
        try { await leaseClose.ConfigureAwait(false); }
        catch (Exception cause) { RetainFailedCleanup(lease, leaseClose); MotionOriginalFailures.Add(failures, cause); }
        MotionOriginalFailures.Throw(failures);
        throw new InvalidOperationException("The source identity was not retained.");
    }
    private static Task<T> SourceAsync<T>(Func<Task<T>> factory, MotionOriginalSourceObserver? originals)
        => originals is not null ? originals.AwaitAsync(factory) : MotionOriginalSourceObserver.AwaitActualAsync(factory());
    private static Task SourceAsync(Func<Task> factory, MotionOriginalSourceObserver? originals)
        => originals is not null ? originals.AwaitAsync(factory) : MotionOriginalSourceObserver.AwaitActualAsync(factory());
}
public sealed class MotionMediaException(MediaEngineError error) : IOException(error.Message)
{ public MediaEngineError Error { get; } = error; }
public sealed class MotionSourcePlayback : IAsyncDisposable
{
    private readonly object _closeGate = new();
    private readonly MediaAssetReadLease _lease;
    private readonly MotionOriginalSourceObserver? _originals;
    private Task? _originalClose;
    private static readonly AsyncLocal<MotionSourcePlayback?> LogicalClose = new();
    [ThreadStatic] private static MotionSourcePlayback? _physicalClose;
    public IMediaPlaybackSession Session { get; }
    public Task? OriginalClose { get { lock (_closeGate) return _originalClose; } }
    public MotionSourcePlayback(MediaAssetReadLease lease, IMediaPlaybackSession session) : this(lease, session, null) { }
    internal MotionSourcePlayback(MediaAssetReadLease lease, IMediaPlaybackSession session, MotionOriginalSourceObserver? originals)
    { _lease = lease; Session = session; _originals = originals; }
    public Task CloseAndDrainAsync()
    {
        if (ReferenceEquals(LogicalClose.Value, this) || ReferenceEquals(_physicalClose, this))
            throw new InvalidOperationException("Playback cannot join its own cleanup.");
        lock (_closeGate)
        {
            if (_originalClose is not null) return _originalClose;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _originalClose = CloseAsync(start.Task);
            start.SetResult();
            return _originalClose;
        }
    }
    private async Task CloseAsync(Task start)
    {
        await start.ConfigureAwait(false);
        var prior = LogicalClose.Value; LogicalClose.Value = this;
        var failures = new List<Exception>();
        Task Acquire(Func<Task> factory)
        { var physical = _physicalClose; _physicalClose = this; try { return factory(); } finally { _physicalClose = physical; } }
        async Task CloseSource(Func<Task> factory)
        {
            try
            {
                if (_originals is not null) await _originals.AwaitAsync(() => Acquire(factory)).ConfigureAwait(false);
                else await MotionOriginalSourceObserver.AwaitActualAsync(Acquire(factory)).ConfigureAwait(false);
            }
            catch (Exception cause) { MotionOriginalFailures.Add(failures, cause); }
        }
        try
        {
            await CloseSource(() => Session.DisposeAsync().AsTask()).ConfigureAwait(false);
            await CloseSource(() => _lease.DisposeAsync().AsTask()).ConfigureAwait(false);
            MotionOriginalFailures.Throw(failures);
        }
        finally { LogicalClose.Value = prior; }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
