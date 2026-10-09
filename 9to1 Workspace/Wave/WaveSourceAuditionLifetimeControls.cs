using Haven.Core.Media;

namespace HavenOS.Apps.Wave;

internal static partial class WaveNativeWorkflowTest
{
    private static void CheckOriginalSourceAuditionLifetime(WaveProject project, Guid hostedId, string source)
    {
        CheckHeldOriginalSeekBeforeRetirement(project, hostedId, source);
        CheckOriginalSeekMultipleDirectCauses(project, hostedId, source);
        CheckOriginalNativeCloseFailureKeepsFilesLease(project, hostedId, source);
        CheckOriginalEngineOpenFailureKeepsUnknownSource(project, hostedId, source);
        CheckOriginalPreviewCallbackSelfJoin(project, hostedId, source);
        CheckLateSourceLeaseAfterScopeRefusal(project, hostedId, source);
        CheckLateNativeSessionAfterScopeRefusal(project, hostedId, source);
        CheckIndependentRepeatedScopeCauses(project, hostedId, source);
    }
    private static void CheckHeldOriginalSeekBeforeRetirement(WaveProject project, Guid hostedId, string source)
    {
        var resolver = new WaveformFixtureResolver(hostedId, source); var engine = new SourcePreviewFixtureEngine();
        var held = new TaskCompletionSource<MediaEngineResult<MediaTime>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.Session.SeekSource = _ => { entered.TrySetResult(); return held.Task; };
        var owner = new WaveFilesProjectService(resolver).CreateOriginalSourceAudition(project, project.Revision,
            project.Tracks[0].Clips[0].ClipId, engine, body => body(), _ => { }, CancellationToken.None);
        Pump(owner.OriginalOpen, owner, "source preview original open");
        var seek = owner.SeekAsync(71); Pump(entered.Task, owner, "source preview original seek entry");
        var close = owner.CloseOriginalAsync();
        Require(!close.IsCompleted && engine.Session.CloseCalls == 0 && resolver.Released == 0,
            "Original native/Files ownership retired before the SAME admitted raw seek settled.");
        held.SetResult(MediaEngineResult<MediaTime>.Success(MediaTimebase.SamplesPerSecond(project.SampleRate).At(71)));
        Pump(seek, owner, "source preview held original seek"); Pump(close, owner, "source preview after actual seek terminal");
        Require(engine.Session.CloseCalls == 1 && resolver.Released == 1 && ReferenceEquals(close, owner.CloseOriginalAsync()),
            "Original source preview did not retain one actual native close and lease release.");
    }
    private static void CheckOriginalSeekMultipleDirectCauses(WaveProject project, Guid hostedId, string source)
    {
        var resolver = new WaveformFixtureResolver(hostedId, source); var engine = new SourcePreviewFixtureEngine();
        var first = new IOException("First actual raw seek cause");
        var foreign = new AggregateException("Opaque foreign seek cause", new InvalidOperationException("Foreign nested detail"));
        var held = new TaskCompletionSource<MediaEngineResult<MediaTime>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.Session.SeekSource = _ => { entered.TrySetResult(); return held.Task; };
        var owner = new WaveFilesProjectService(resolver).CreateOriginalSourceAudition(project, project.Revision,
            project.Tracks[0].Clips[0].ClipId, engine, body => body(), _ => { }, CancellationToken.None);
        Pump(owner.OriginalOpen, owner, "multi-cause actual source open");
        var seek = owner.SeekAsync(31); Pump(entered.Task, owner, "multi-cause same raw seek entered");
        var close = owner.CloseOriginalAsync();
        Require(!close.IsCompleted && engine.Session.CloseCalls == 0 && resolver.Released == 0,
            "Actual native/Files custody was released while the SAME raw seek remained held.");
        held.SetException(new Exception[] { first, foreign });
        try { Pump(seek, owner, "expected first awaited seek cause"); throw new InvalidDataException("Faulted original seek became success."); }
        catch (IOException cause) when (ReferenceEquals(cause, first)) { }
        try { Pump(close, owner, "all actual raw seek causes retained"); throw new InvalidDataException("Faulted original seek became healthy retirement."); }
        catch (AggregateException causes) when (RetainsOriginalTaskEnvelope(causes, held.Task, first, foreign)) { }
        Require(held.Task.Exception is { InnerExceptions.Count: 2 } raw && ReferenceEquals(raw.InnerExceptions[0], first) &&
            ReferenceEquals(raw.InnerExceptions[1], foreign), "Actual direct source siblings were replaced or flattened.");
        Require(ReferenceEquals(close, owner.CloseOriginalAsync()) && close.IsFaulted &&
            owner.OriginalNativeClose?.IsCompletedSuccessfully == true && owner.OriginalLeaseClose?.IsCompletedSuccessfully == true &&
            engine.Session.CloseCalls == 1 && resolver.Released == 1,
            "Known actual native and Files resources were not independently retired once after the raw seek settled.");
        RetainedFailures.Add((owner, close, foreign));
    }
    private static bool RetainsOriginalTaskEnvelope(AggregateException ownerFailure, Task sameOriginal, params Exception[] directCauses)
    {
        if (sameOriginal.Exception is not { } raw || raw.InnerExceptions.Count != directCauses.Length ||
            raw.InnerExceptions.Where((cause, index) => !ReferenceEquals(cause, directCauses[index])).Any()) return false;
        // Only the known Task's single aggregate envelope is observed. A foreign
        // aggregate inside that envelope remains one opaque original object.
        return ownerFailure.InnerExceptions.Any(cause => cause is AggregateException envelope &&
            envelope.InnerExceptions.Count == directCauses.Length &&
            !envelope.InnerExceptions.Where((inner, index) => !ReferenceEquals(inner, directCauses[index])).Any());
    }
    private static void CheckOriginalNativeCloseFailureKeepsFilesLease(WaveProject project, Guid hostedId, string source)
    {
        var resolver = new WaveformFixtureResolver(hostedId, source); var engine = new SourcePreviewFixtureEngine();
        var fault = new IOException("fixture original native pipeline release failed");
        var rawClose = Task.FromException(fault); engine.Session.CloseSource = () => new ValueTask(rawClose);
        var owner = new WaveFilesProjectService(resolver).CreateOriginalSourceAudition(project, project.Revision,
            project.Tracks[0].Clips[0].ClipId, engine, body => body(), _ => { }, CancellationToken.None);
        Pump(owner.OriginalOpen, owner, "native-close fault original source open");
        var close = owner.CloseOriginalAsync();
        try { Pump(close, owner, "expected original native source release failure"); throw new InvalidDataException("Native close failure became source retirement success."); }
        catch (AggregateException failure) when (RetainsOriginalTaskEnvelope(failure, rawClose, fault)) { }
        Require(ReferenceEquals(owner.OriginalNativeClose, rawClose) && owner.OriginalLeaseClose is null && resolver.Released == 0,
            "The SAME failed native source was lost or its Files lease released while its pipeline may still read it.");
        Require(ReferenceEquals(close, owner.CloseOriginalAsync()) && engine.Session.CloseCalls == 1, "Failed physical close was replayed or replaced.");
        RetainedFailures.Add((owner, close, fault));
    }
    private static void CheckOriginalEngineOpenFailureKeepsUnknownSource(WaveProject project, Guid hostedId, string source)
    {
        var resolver = new WaveformFixtureResolver(hostedId, source); var engine = new SourcePreviewFixtureEngine();
        var fault = new ArgumentException("fixture foreign engine open callback failure"); var rawOpen = Task.FromException<MediaEngineResult<IMediaPlaybackSession>>(fault);
        engine.OpenSource = _ => rawOpen;
        var owner = new WaveFilesProjectService(resolver).CreateOriginalSourceAudition(project, project.Revision,
            project.Tracks[0].Clips[0].ClipId, engine, body => body(), _ => { }, CancellationToken.None);
        try { Pump(owner.OriginalOpen, owner, "expected original engine-open failure"); throw new InvalidDataException("Foreign engine fault became an unavailable success."); }
        catch (ArgumentException failure) when (ReferenceEquals(failure, fault)) { }
        var close = owner.CloseOriginalAsync();
        try { Pump(close, owner, "expected unknown engine original close failure"); throw new InvalidDataException("Unknown engine source effect became a clean close."); }
        catch (AggregateException failure) when (RetainsOriginalTaskEnvelope(failure, rawOpen, fault)) { }
        Require(resolver.Released == 0 && owner.OriginalLeaseClose is null && engine.Session.CloseCalls == 0 && ReferenceEquals(close, owner.CloseOriginalAsync()),
            "Unknown original engine ownership released its source or replayed a fabricated cleanup.");
        RetainedFailures.Add((owner, close, fault));
    }
    private static void CheckOriginalPreviewCallbackSelfJoin(WaveProject project, Guid hostedId, string source)
    {
        var resolver = new WaveformFixtureResolver(hostedId, source); var engine = new SourcePreviewFixtureEngine();
        var owner = new WaveFilesProjectService(resolver).CreateOriginalSourceAudition(project, project.Revision,
            project.Tracks[0].Clips[0].ClipId, engine, body => body(), _ => { }, CancellationToken.None);
        Pump(owner.OriginalOpen, owner, "self-join original source open"); var neutral = ExecutionContext.Capture()!; var observed = false;
        engine.Session.PositionSource = () =>
        {
            ExecutionContext.Run(neutral, _ =>
            {
                try { owner.CloseOriginalAsync(); throw new InvalidDataException("Original native callback admitted its own close under restored context."); }
                catch (InvalidOperationException) { observed = true; }
                Require(owner.OriginalClose is null, "Self-join published the source's original close before rejecting.");
            }, null);
            return Task.FromResult(MediaEngineResult<MediaTime>.Success(MediaTimebase.SamplesPerSecond(project.SampleRate).At(17)));
        };
        Pump(owner.ReadPositionAsync(), owner, "actual source callback self-join control");
        Require(observed, "Actual native callback self-join control did not execute.");
        Pump(owner.CloseOriginalAsync(), owner, "outside original source callback close");
        Require(resolver.Released == 1, "A completed outside source close did not release the canonical lease.");
    }
    private static void CheckLateSourceLeaseAfterScopeRefusal(WaveProject project, Guid hostedId, string source)
    {
        var originalResolver = new WaveformFixtureResolver(hostedId, source); var resolver = new HeldAuditionResolver(originalResolver);
        var engine = new SourcePreviewFixtureEngine(); var fault = new IOException("Actual scope failed after pending Files receipt publication"); var first = true;
        var owner = new WaveFilesProjectService(resolver).CreateOriginalSourceAudition(project, project.Revision,
            project.Tracks[0].Clips[0].ClipId, engine, body => { body(); if (first) { first = false; throw fault; } }, _ => { }, CancellationToken.None);
        try { Pump(owner.OriginalOpen, owner, "late Files original scope refusal"); throw new InvalidDataException("Scope refusal became healthy source open."); }
        catch (IOException original) when (ReferenceEquals(original, fault)) { }
        var close = owner.CloseOriginalAsync();
        Require(!close.IsCompleted && originalResolver.Released == 0 && engine.OpenCount == 0,
            "Pending original Files receipt was abandoned or used for native publication after scope refusal.");
        Require(resolver.OriginalRead is not null && resolver.OriginalRead.IsCompletedSuccessfully, "Actual original resolver did not supply its held lease.");
        resolver.Held.SetResult(resolver.OriginalRead!.Result);
        try { Pump(close, owner, "late Files receipt independently retired"); throw new InvalidDataException("Failed scope became clean source retirement."); }
        catch (AggregateException original) when (RetainsOriginalTaskEnvelope(original, owner.OriginalOpen, fault)) { }
        Require(originalResolver.Released == 1 && owner.OriginalLeaseClose?.IsCompletedSuccessfully == true && engine.OpenCount == 0,
            "SAME late successful Files receipt was not independently retired without admitting native work.");
        Require(ReferenceEquals(close, owner.CloseOriginalAsync()) && owner.OriginalOpen.IsFaulted, "Failed original open/close was replaced.");
        RetainedFailures.Add((owner, close, fault));
    }
    private static void CheckLateNativeSessionAfterScopeRefusal(WaveProject project, Guid hostedId, string source)
    {
        var resolver = new WaveformFixtureResolver(hostedId, source); var engine = new SourcePreviewFixtureEngine();
        var held = new TaskCompletionSource<MediaEngineResult<IMediaPlaybackSession>>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.OpenSource = _ => held.Task;
        var fault = new IOException("Actual scope failed after pending native receipt publication"); var refused = false;
        var owner = new WaveFilesProjectService(resolver).CreateOriginalSourceAudition(project, project.Revision,
            project.Tracks[0].Clips[0].ClipId, engine, body =>
            { body(); if (!refused && engine.OpenCount == 1) { refused = true; throw fault; } }, _ => { }, CancellationToken.None);
        try { Pump(owner.OriginalOpen, owner, "late native original scope refusal"); throw new InvalidDataException("Native scope refusal became healthy source open."); }
        catch (IOException original) when (ReferenceEquals(original, fault)) { }
        var close = owner.CloseOriginalAsync();
        Require(!close.IsCompleted && resolver.Released == 0 && engine.Session.CloseCalls == 0,
            "Pending actual native session lost its source lease before its SAME result settled.");
        held.SetResult(MediaEngineResult<IMediaPlaybackSession>.Success(engine.Session));
        try { Pump(close, owner, "late native result independently retired"); throw new InvalidDataException("Original scope failure became clean close."); }
        catch (AggregateException original) when (RetainsOriginalTaskEnvelope(original, owner.OriginalOpen, fault)) { }
        Require(engine.Session.CloseCalls == 1 && resolver.Released == 1 && owner.OriginalNativeClose?.IsCompletedSuccessfully == true,
            "Known SAME successful native result was not retired before its actual Files lease.");
        Require(ReferenceEquals(close, owner.CloseOriginalAsync()) && !owner.HasNativeSession && owner.OriginalOpen.IsFaulted,
            "Late native cleanup custody admitted product work or replaced the failed driver.");
        RetainedFailures.Add((owner, close, fault));
    }
    private static void CheckIndependentRepeatedScopeCauses(WaveProject project, Guid hostedId, string source)
    {
        var resolver = new WaveformFixtureResolver(hostedId, source); var engine = new SourcePreviewFixtureEngine();
        var first = new IOException("Actual capability callback failure"); engine.CapabilityFailure = first;
        var owner = new WaveFilesProjectService(resolver).CreateOriginalSourceAudition(project, project.Revision,
            project.Tracks[0].Clips[0].ClipId, engine, body =>
            {
                try { body(); }
                catch (IOException cause) when (ReferenceEquals(cause, first))
                { try { body(); } catch (InvalidOperationException) { } }
            }, _ => { }, CancellationToken.None);
        AggregateException originalFailure;
        try { Pump(owner.OriginalOpen, owner, "independent actual callback and second protocol refusal"); throw new InvalidDataException("Repeated source scope swallowed original causes."); }
        catch (AggregateException failure)
        {
            originalFailure = failure;
            Require(failure.InnerExceptions.Any(cause => ReferenceEquals(cause, first)) &&
                failure.InnerExceptions.Any(cause => cause is InvalidOperationException && cause.Message.Contains("once synchronously", StringComparison.Ordinal)),
                "First callback cause or independent second-invocation protocol cause was dropped.");
        }
        var close = owner.CloseOriginalAsync();
        try { Pump(close, owner, "failed repeated scope original close"); throw new InvalidDataException("Failed callback group became successful close."); }
        catch (AggregateException failure) when (RetainsOriginalTaskEnvelope(failure, owner.OriginalOpen, originalFailure)) { }
        Require(resolver.Released == 1 && engine.OpenCount == 0 && ReferenceEquals(close, owner.CloseOriginalAsync()),
            "Original Files lease did not independently retire after refused native admission.");
        RetainedFailures.Add((owner, close, originalFailure));
    }
    private sealed class HeldAuditionResolver(IMediaAssetSourceResolver original) : IMediaAssetSourceResolver
    {
        public Task<MediaEngineResult<MediaAssetReadLease>>? OriginalRead;
        public TaskCompletionSource<MediaEngineResult<MediaAssetReadLease>> Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<MediaEngineResult<MediaAssetReadLease>> ResolveAsync(string fileID, MediaAssetId assetID, string? revisionID = null, CancellationToken token = default)
        { OriginalRead = original.ResolveAsync(fileID, assetID, revisionID, token); return Held.Task; }
    }
    // Explicit original native-port fixture. Production consumes the maintained
    // GStreamer IMediaEngine supplied by actual Home composition, never this class.
    private sealed class SourcePreviewFixtureEngine : IMediaEngine
    {
        public SourcePreviewFixtureSession Session { get; } = new();
        public MediaAssetSource? OpenedSource { get; private set; }
        public int OpenCount { get; private set; }
        public Func<MediaAssetSource, Task<MediaEngineResult<IMediaPlaybackSession>>>? OpenSource;
        public Exception? CapabilityFailure;
        public Task<MediaEngineCapabilities> GetCapabilitiesAsync(CancellationToken token = default)
        {
            if (CapabilityFailure is not null) throw CapabilityFailure;
            return Task.FromResult(new MediaEngineCapabilities(true, false, "explicit-native-port-fixture", true, false, false, []));
        }
        public Task<MediaEngineResult<IMediaPlaybackSession>> OpenPlaybackAsync(MediaAssetSource source, CancellationToken token = default)
        { OpenedSource = source; OpenCount++; return OpenSource?.Invoke(source) ?? Task.FromResult(MediaEngineResult<IMediaPlaybackSession>.Success(Session)); }
    }
    private sealed class SourcePreviewFixtureSession : IMediaPlaybackSession
    {
        public MediaPlaybackState State { get; private set; } = MediaPlaybackState.Stopped;
        public MediaTime LastSeek { get; private set; } = MediaTimebase.Nanoseconds.At(0);
        public int CloseCalls { get; private set; }
        public Func<MediaTime, Task<MediaEngineResult<MediaTime>>>? SeekSource;
        public Func<Task<MediaEngineResult<MediaTime>>>? PositionSource;
        public Func<ValueTask>? CloseSource;
        public Task<MediaEngineResult<MediaPlaybackState>> SetStateAsync(MediaPlaybackState state, CancellationToken token = default)
        { State = state; return Task.FromResult(MediaEngineResult<MediaPlaybackState>.Success(state)); }
        public Task<MediaEngineResult<MediaTime>> SeekAsync(MediaTime position, CancellationToken token = default)
        { LastSeek = position; return SeekSource?.Invoke(position) ?? Task.FromResult(MediaEngineResult<MediaTime>.Success(position)); }
        public Task<MediaEngineResult<MediaTime>> GetPositionAsync(CancellationToken token = default) =>
            PositionSource?.Invoke() ?? Task.FromResult(MediaEngineResult<MediaTime>.Success(LastSeek));
        public ValueTask DisposeAsync() { CloseCalls++; State = MediaPlaybackState.Stopped; return CloseSource?.Invoke() ?? ValueTask.CompletedTask; }
    }
}
