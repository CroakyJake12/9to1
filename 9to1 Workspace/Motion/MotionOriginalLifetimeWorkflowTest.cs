using Haven.Core.Media;

namespace HavenOS.Apps.Motion;

// Local controls for actual Motion presentation custody; these do not qualify Home/Files/media readiness.
internal static class MotionOriginalLifetimeWorkflowTest
{
    internal static async Task RunAsync()
    {
        await HeldPickerAsync();
        await ReentrantPublicationAsync();
        await NestedRestoredContextSourceAsync();
        await HeldSeekAndPlaybackCloseAsync();
        await ForeignSourceFaultsAsync();
        await FaultedRawCancellationCauseAsync();
        await IndependentRenderCleanupAsync();
        await PlaybackCleanupFaultsAsync();
    }
    private static MotionEditSession Session(bool clip = false)
    {
        var directory = Directory.CreateTempSubdirectory("motion-original-evidence-");
        var path = Path.Combine(directory.FullName, "original.motion.json");
        var store = new MotionProjectStore();
        var project = store.Create(Guid.NewGuid().ToString(), 640, 480, 30, 1);
        project = project with { AssetReferences = [project.AssetReferences[0] with { SourceRevisionID = "retained-fixture-revision" }] };
        store.Save(path, project, -1);
        var session = new MotionEditSession(store, path);
        if (clip) session.Apply(p => store.Insert(p, p.Revision, p.Sequences[0].SequenceId, p.Sequences[0].VideoTracks[0].TrackId, p.AssetReferences[0].AssetId, 0, 0, 30));
        return session;
    }
    private static async Task HeldPickerAsync()
    {
        var started = NewGate(); var picked = new TaskCompletionSource<MotionAssetReference?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workspace = new MotionCuiWorkspace(Session(), _ => true, pickAsset: _ => { started.SetResult(); return picked.Task; });
        var command = workspace.DispatchAsync("9to1.Motion.ImportAsset", null).AsTask();
        Task? close = null;
        try
        {
            await started.Task;
            close = workspace.CloseAndDrainAsync();
            Require(ReferenceEquals(close, workspace.CloseAndDrainAsync()) && !close.IsCompleted, "Close joins SAME accepted held picker");
            Require(!workspace.TryGetValue("CaptionText", out _) && workspace.IsActionAvailable("9to1.Motion.AddTrack") == false, "Retirement immediately revokes bindings and commands");
        }
        finally { picked.TrySetResult(null); await command; await (close ?? workspace.CloseAndDrainAsync()); }
    }
    private static async Task ReentrantPublicationAsync()
    {
        var effects = 0; var deniedSelfJoin = false;
        var workspace = new MotionCuiWorkspace(Session(), _ => true, pickAsset: _ => { effects++; return Task.FromResult<MotionAssetReference?>(null); });
        workspace.PropertyChanged += (_, _) =>
        {
            try { _ = workspace.CloseAndDrainAsync(); }
            catch (InvalidOperationException) { deniedSelfJoin = true; }
            workspace.RequestRetirement();
        };
        await workspace.DispatchAsync("9to1.Motion.ImportAsset", null);
        await workspace.CloseAndDrainAsync();
        Require(deniedSelfJoin && effects == 0, "Actual physical Changed callback cannot self-join or dispatch after retirement");
    }
    private static async Task NestedRestoredContextSourceAsync()
    {
        var baseline = ExecutionContext.Capture() ?? throw new InvalidOperationException("Fixture baseline context missing.");
        MotionCuiWorkspace? parent = null;
        var denied = false;
        var child = new MotionCuiWorkspace(Session(), _ => true, pickAsset: ignoredToken =>
        {
            ExecutionContext.Run(baseline.CreateCopy(), ignoredState =>
            {
                try { _ = parent!.CloseAndDrainAsync(); }
                catch (InvalidOperationException) { denied = true; }
            }, null);
            return Task.FromResult<MotionAssetReference?>(null);
        });
        parent = new MotionCuiWorkspace(Session(), _ => true, pickAsset: _ => InvokeChild());
        async Task<MotionAssetReference?> InvokeChild()
        { await child.DispatchAsync("9to1.Motion.ImportAsset", null); return null; }
        await parent.DispatchAsync("9to1.Motion.ImportAsset", null);
        await parent.CloseAndDrainAsync(); await child.CloseAndDrainAsync();
        Require(denied, "Restored-context nested raw source cannot join its awaiting actual parent original");
    }
    private static async Task HeldSeekAndPlaybackCloseAsync()
    {
        var seekStarted = NewGate(); var seek = new TaskCompletionSource<MediaEngineResult<MediaTime>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closeStarted = NewGate(); var nativeClose = NewGate(); var leaseCloses = 0; var closeAcquisitions = 0;
        var native = new TestPlayback
        {
            Seek = position => { seekStarted.SetResult(); return seek.Task; },
            Close = () => { closeAcquisitions++; closeStarted.SetResult(); return new(nativeClose.Task); }
        };
        var media = new MotionMediaService(new Resolver((id, asset, revision) => Lease(id, asset, revision, () => { leaseCloses++; return ValueTask.CompletedTask; })), new Engine(native));
        var workspace = new MotionCuiWorkspace(Session(true), _ => true, media);
        Require(workspace.TrySetValue("ClipIndex", 0), "Playback clip selection");
        var command = workspace.DispatchAsync("9to1.Motion.PlaySource", null).AsTask();
        Task? close = null;
        try
        {
            await seekStarted.Task;
            close = workspace.CloseAndDrainAsync();
            Require(!close.IsCompleted && closeAcquisitions == 0, "Playback cannot close before actual Seek settles");
            seek.SetResult(MediaEngineResult<MediaTime>.Success(MediaTimebase.FramesPerSecond(30).At(0)));
            await command; await closeStarted.Task;
            Require(!close.IsCompleted && leaseCloses == 0 && closeAcquisitions == 1, "Actual native close precedes lease release and remains retained");
        }
        finally
        {
            seek.TrySetResult(MediaEngineResult<MediaTime>.Success(MediaTimebase.FramesPerSecond(30).At(0)));
            nativeClose.TrySetResult(); await command; await (close ?? workspace.CloseAndDrainAsync());
        }
        Require(leaseCloses == 1 && closeAcquisitions == 1, "Native playback and source close each acquired once");
    }
    private static async Task ForeignSourceFaultsAsync()
    {
        var foreignEmpty = new AggregateException("foreign empty"); var io = new IOException("actual sibling");
        var raw = new TaskCompletionSource<MotionAssetReference?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = NewGate();
        var workspace = new MotionCuiWorkspace(Session(), _ => true, pickAsset: _ => { started.SetResult(); return raw.Task; });
        var command = workspace.DispatchAsync("9to1.Motion.ImportAsset", null).AsTask();
        await started.Task; raw.SetException([foreignEmpty, io]);
        var body = await Failure(command); var close = workspace.CloseAndDrainAsync(); var cleanup = await Failure(close);
        Require(ContainsReference(body, foreignEmpty) && ContainsReference(body, io) && ContainsReference(cleanup, foreignEmpty) && ContainsReference(cleanup, io), "Actual mixed source siblings and foreign empty group survive body and close");
        Require(ReferenceEquals(close, workspace.CloseAndDrainAsync()) && close.IsFaulted, "Unresolved original failure keeps SAME close failed");
    }
    private static async Task FaultedRawCancellationCauseAsync()
    {
        var actualCause = new OperationCanceledException("Faulted raw source, with no cancellation request.");
        var actualRawSource = Task.FromException<MotionAssetReference?>(actualCause);
        var workspace = new MotionCuiWorkspace(Session(), _ => true, pickAsset: _ => actualRawSource);
        var command = workspace.DispatchAsync("9to1.Motion.ImportAsset", null).AsTask();
        var bodyFailure = await Failure(command);
        var close = workspace.CloseAndDrainAsync(); var closeFailure = await Failure(close);
        Require(actualRawSource.IsFaulted && !actualRawSource.IsCanceled && ReferenceEquals(actualRawSource.Exception!.InnerExceptions.Single(), actualCause), "Actual source is faulted OCE, not an intentionally canceled Task");
        Require(ReferenceEquals(bodyFailure, actualCause) && close.IsFaulted && !close.IsCanceled && ContainsReference(closeFailure, actualCause), "Same raw cause remains faulted close custody without token/type waiver");
        Require(ReferenceEquals(close, workspace.CloseAndDrainAsync()), "Failed cancellation-cause close remains SAME original");
    }
    private static async Task IndependentRenderCleanupAsync()
    {
        var store = new MotionProjectStore(); var project = Session(true).Project;
        var second = new MotionAssetReference(Guid.NewGuid(), Guid.NewGuid().ToString(), "second-revision");
        project = store.AddAsset(project, project.Revision, second);
        project = store.Insert(project, project.Revision, project.Sequences[0].SequenceId, project.Sequences[0].VideoTracks[0].TrackId, second.AssetId, 30, 0, 30);
        var renderFailure = new IOException("render source"); var firstReleaseFailure = new IOException("first release"); var releases = 0;
        var firstAsset = project.AssetReferences[0].AssetId;
        var resolver = new Resolver((id, asset, revision) => Lease(id, asset, revision, () =>
        { releases++; return asset.Value == firstAsset ? new ValueTask(Task.FromException(firstReleaseFailure)) : ValueTask.CompletedTask; }));
        var media = new MotionMediaService(resolver, new Engine(new TestPlayback()), new Renderer(renderFailure));
        var failure = await Failure(media.RenderAsync(project, project.Sequences[0].SequenceId, "/tmp/motion-fixture-unused-output.webm", null, CancellationToken.None));
        Require(releases == 2 && ContainsReference(failure, renderFailure) && ContainsReference(failure, firstReleaseFailure), "Render and each actual lease cleanup preserve all causes");
    }
    private static async Task PlaybackCleanupFaultsAsync()
    {
        var nativeFailure = new IOException("native close"); var leaseFailure = new AggregateException("foreign lease group", new IOException("nested cause")); var releases = 0; var nativeCloses = 0;
        var session = new TestPlayback { Close = () => { nativeCloses++; return new(Task.FromException(nativeFailure)); } };
        var lease = Lease(Guid.NewGuid().ToString(), MediaAssetId.New(), "revision", () => { releases++; return new(Task.FromException(leaseFailure)); });
        var playback = new MotionSourcePlayback(lease, session); var close = playback.CloseAndDrainAsync();
        var failure = await Failure(close); var sameClose = playback.DisposeAsync().AsTask();
        Require(ReferenceEquals(close, sameClose) && ContainsReference(failure, nativeFailure) && ContainsReference(failure, leaseFailure) && nativeCloses == 1 && releases == 1, "Cached playback close keeps native and opaque lease failures without repeating cleanup");
    }
    private static MediaAssetReadLease Lease(string id, MediaAssetId asset, string? revision, Func<ValueTask> release)
        => new(new(asset, Guid.Parse(id), new Uri("file:///tmp/motion-local-fixture-not-opened"), revision), release);
    private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task<Exception> Failure(Task task)
    { try { await task; } catch (Exception cause) { return cause; } throw new InvalidDataException("Expected actual source failure."); }
    private static bool ContainsReference(Exception observed, Exception actual)
        => ReferenceEquals(observed, actual) || observed is AggregateException group && group.InnerExceptions.Any(child => ContainsReference(child, actual));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private sealed class Resolver(Func<string, MediaAssetId, string?, MediaAssetReadLease> resolve) : IMediaAssetSourceResolver
    {
        public Task<MediaEngineResult<MediaAssetReadLease>> ResolveAsync(string fileID, MediaAssetId assetID, string? expectedRevision, CancellationToken cancellationToken = default)
            => Task.FromResult(MediaEngineResult<MediaAssetReadLease>.Success(resolve(fileID, assetID, expectedRevision)));
    }
    private sealed class Engine(IMediaPlaybackSession session) : IMediaEngine
    {
        public Task<MediaEngineCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new MediaEngineCapabilities(false, false, null, false, false, false, ["local fixture"]));
        public Task<MediaEngineResult<IMediaPlaybackSession>> OpenPlaybackAsync(MediaAssetSource source, CancellationToken cancellationToken = default)
            => Task.FromResult(MediaEngineResult<IMediaPlaybackSession>.Success(session));
    }
    private sealed class Renderer(Exception failure) : IMediaTimelineRenderer
    {
        public Task<MediaEngineResult<MediaRenderOutput>> RenderAsync(MediaTimelineRenderRequest request, IProgress<MediaRenderProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromException<MediaEngineResult<MediaRenderOutput>>(failure);
    }
    private sealed class TestPlayback : IMediaPlaybackSession
    {
        internal Func<MediaTime, Task<MediaEngineResult<MediaTime>>>? Seek;
        internal Func<ValueTask>? Close;
        public MediaPlaybackState State { get; private set; }
        public Task<MediaEngineResult<MediaPlaybackState>> SetStateAsync(MediaPlaybackState state, CancellationToken cancellationToken = default)
        { State = state; return Task.FromResult(MediaEngineResult<MediaPlaybackState>.Success(state)); }
        public Task<MediaEngineResult<MediaTime>> GetPositionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(MediaEngineResult<MediaTime>.Success(MediaTimebase.FramesPerSecond(30).At(0)));
        public Task<MediaEngineResult<MediaTime>> SeekAsync(MediaTime position, CancellationToken cancellationToken = default)
            => Seek?.Invoke(position) ?? Task.FromResult(MediaEngineResult<MediaTime>.Success(position));
        public ValueTask DisposeAsync() => Close?.Invoke() ?? ValueTask.CompletedTask;
    }
}
