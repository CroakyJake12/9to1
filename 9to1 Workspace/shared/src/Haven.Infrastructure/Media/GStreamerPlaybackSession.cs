using Haven.Core.Media;

namespace Haven.Infrastructure.Media;

internal delegate int GStreamerGetState(nint element, out int state, out int pending, ulong timeout);
internal delegate int GStreamerQueryPosition(nint element, int format, out long position);

// The existing GStreamer engine supplies its SAME pipeline and native delegates.
// This leaf retains each original native call driver and once-only retirement;
// it does not decode audio, resolve Files, or create another playback engine.
internal sealed class GStreamerPlaybackSession : IMediaPlaybackSession
{
    private readonly Func<nint, int, int> _setState;
    private readonly GStreamerGetState _getState;
    private readonly GStreamerQueryPosition _queryPosition;
    private readonly Func<nint, int, int, long, int> _seek;
    private readonly Action<nint> _unref;
    private readonly object _sourceGate = new();
    private readonly SemaphoreSlim _nativeGate = new(1, 1);
    private readonly List<Task> _originalOperations = [];
    private readonly Dictionary<Task, Task<bool>> _originalOperationObservations = new(ReferenceEqualityComparer.Instance);
    private nint _pipeline;
    private bool _retiring;
    private volatile MediaPlaybackState _state = MediaPlaybackState.Stopped;
    private Task? _originalClose, _originalStop, _originalUnref;
    internal nint OriginalPipeline { get; }
    internal nint RetainedPipeline { get { lock (_sourceGate) return _pipeline; } }
    internal Task? OriginalClose { get { lock (_sourceGate) return _originalClose; } }
    internal Task? OriginalStop => _originalStop;
    internal Task? OriginalUnref => _originalUnref;
    internal IReadOnlyList<Task> OriginalOperations { get { lock (_sourceGate) return _originalOperations.ToArray(); } }
    internal IReadOnlyList<Task<bool>> OriginalOperationObservations { get { lock (_sourceGate) return _originalOperationObservations.Values.ToArray(); } }
    public MediaPlaybackState State => _state;
    internal GStreamerPlaybackSession(nint samePipeline, Func<nint, int, int> setState, GStreamerGetState getState,
        GStreamerQueryPosition queryPosition, Func<nint, int, int, long, int> seek, Action<nint> unref)
    {
        if (samePipeline == 0) throw new ArgumentException("Use the actual nonempty GStreamer pipeline.", nameof(samePipeline));
        OriginalPipeline = _pipeline = samePipeline;
        _setState = setState ?? throw new ArgumentNullException(nameof(setState));
        _getState = getState ?? throw new ArgumentNullException(nameof(getState));
        _queryPosition = queryPosition ?? throw new ArgumentNullException(nameof(queryPosition));
        _seek = seek ?? throw new ArgumentNullException(nameof(seek)); _unref = unref ?? throw new ArgumentNullException(nameof(unref));
    }
    public Task<MediaEngineResult<MediaPlaybackState>> SetStateAsync(MediaPlaybackState state, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (state is not (MediaPlaybackState.Playing or MediaPlaybackState.Paused or MediaPlaybackState.Stopped))
            return Task.FromResult(Fail<MediaPlaybackState>("Requested playback state cannot be set directly."));
        return AdmitOriginalCall(pipeline =>
        {
            var nativeState = state switch { MediaPlaybackState.Playing => 4, MediaPlaybackState.Paused => 3, _ => 1 };
            if (_setState(pipeline, nativeState) == 0) return Fail<MediaPlaybackState>("GStreamer rejected the requested playback state.");
            _state = state; return MediaEngineResult<MediaPlaybackState>.Success(state);
        }, token);
    }
    public Task<MediaEngineResult<MediaTime>> GetPositionAsync(CancellationToken token = default) => AdmitOriginalCall(pipeline =>
    {
        if (_queryPosition(pipeline, 3, out var position) == 0 || position < 0)
            return Fail<MediaTime>("GStreamer could not report the current position.");
        return MediaEngineResult<MediaTime>.Success(MediaTimebase.Nanoseconds.At(position));
    }, token);
    public Task<MediaEngineResult<MediaTime>> SeekAsync(MediaTime position, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!position.IsValid || position.Ticks < 0) return Task.FromResult(Fail<MediaTime>("Seek position must be non-negative and valid."));
        long nanoseconds;
        try { nanoseconds = position.ConvertTo(MediaTimebase.Nanoseconds).Ticks; }
        catch (OverflowException) { return Task.FromResult(Fail<MediaTime>("Seek position exceeds GStreamer limits.")); }
        return AdmitOriginalCall(pipeline => _seek(pipeline, 3, 1, nanoseconds) == 0
            ? Fail<MediaTime>("GStreamer rejected the seek request.")
            : MediaEngineResult<MediaTime>.Success(MediaTimebase.Nanoseconds.At(nanoseconds)), token);
    }
    private Task<MediaEngineResult<T>> AdmitOriginalCall<T>(Func<nint, MediaEngineResult<T>> actualCall, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_sourceGate)
        {
            if (_retiring || _pipeline == 0) return Task.FromResult(Fail<T>("Playback session is disposed."));
            // A successful terminal flag is not an independent source join.
            // Only this owner's prepublished observation of the SAME raw call
            // can authorize healthy pruning; faults/cancellation remain held.
            var observedHealthy = _originalOperations.Where(source =>
                _originalOperationObservations.TryGetValue(source, out var observation) &&
                observation.IsCompletedSuccessfully && observation.Result).ToArray();
            foreach (var source in observedHealthy)
            { _originalOperations.Remove(source); _originalOperationObservations.Remove(source); }
            if (_originalOperations.Count >= 128) throw new InvalidOperationException("GStreamer retains unresolved original native calls. Retire this SAME session before continuing.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var original = RunOriginalCallAsync(start.Task, actualCall, token);
            var observation = ObserveOriginalCallAsync(start.Task, original);
            _originalOperations.Add(original);
            _originalOperationObservations.Add(original, observation);
            start.SetResult(); return original;
        }
    }
    private static async Task<bool> ObserveOriginalCallAsync(Task start, Task sameOriginal)
    {
        await start.ConfigureAwait(false);
        try { await sameOriginal.ConfigureAwait(false); return true; }
        catch (Exception) { return false; } // SAME raw failure stays in _originalOperations.
    }
    private async Task<T> RunOriginalCallAsync<T>(Task start, Func<nint, T> actualCall, CancellationToken token)
    {
        using var driver = EnterOriginalDriver();
        await start.ConfigureAwait(false);
        await _nativeGate.WaitAsync(token).ConfigureAwait(false);
        try { return WithinOriginalNative(() => actualCall(_pipeline)); }
        finally { _nativeGate.Release(); }
    }
    public ValueTask DisposeAsync()
    {
        DemandExternalOriginalJoin();
        lock (_sourceGate)
        {
            if (_originalClose is not null) return new(_originalClose);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _retiring = true; _originalClose = CloseOriginalCoreAsync(start.Task); start.SetResult(); return new(_originalClose);
        }
    }
    private async Task CloseOriginalCoreAsync(Task start)
    {
        using var driver = EnterOriginalDriver();
        await start.ConfigureAwait(false); var failures = new List<Exception>();
        var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        while (true)
        {
            Task[] admitted;
            lock (_sourceGate) admitted = _originalOperations.Where(source => !joined.Contains(source)).Distinct().ToArray();
            if (admitted.Length == 0) break;
            foreach (var original in admitted)
            { joined.Add(original); try { await original.ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); } }
        }
        Task<bool>[] observations;
        lock (_sourceGate) observations = _originalOperationObservations.Values.ToArray();
        foreach (var sameObservation in observations)
            try { await sameObservation.ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
        var actualPipeline = _pipeline;
        var stopStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _originalStop = StopOriginalCoreAsync(stopStart.Task, actualPipeline); stopStart.SetResult();
        try { await _originalStop.ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
        if (_originalStop.IsCompletedSuccessfully)
        {
            var unrefStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _originalUnref = UnrefOriginalCoreAsync(unrefStart.Task, actualPipeline); unrefStart.SetResult();
            try { await _originalUnref.ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count != 0) throw new AggregateException("GStreamer retained original native call or physical retirement failures.", failures);
    }
    private async Task StopOriginalCoreAsync(Task start, nint actualPipeline)
    {
        using var driver = EnterOriginalDriver(); await start.ConfigureAwait(false);
        WithinOriginalNative(() =>
        {
            if (actualPipeline == 0 || _setState(actualPipeline, 1) == 0)
                throw new InvalidOperationException("GStreamer did not acknowledge the original pipeline stop.");
            // NULL/READY transitions are synchronous in GStreamer. Observe the
            // actual state with zero native wait; an unresolved transition is a
            // failed retirement, never permission to unref an active pipeline.
            var result = _getState(actualPipeline, out var current, out var pending, 0);
            if (result is not (1 or 3) || current != 1 || pending is not (0 or 1))
                throw new InvalidOperationException("The SAME GStreamer pipeline has not reached its actual NULL retirement state.");
            _state = MediaPlaybackState.Stopped; return true;
        });
    }
    private async Task UnrefOriginalCoreAsync(Task start, nint actualPipeline)
    {
        using var driver = EnterOriginalDriver(); await start.ConfigureAwait(false);
        WithinOriginalNative(() => { _unref(actualPipeline); return true; });
        // Preserve the pointer after any stop/unref failure or unknown effect.
        // Only this successful physical release permits retiring its ownership.
        lock (_sourceGate) _pipeline = 0;
    }
    private sealed class Invocation(GStreamerPlaybackSession owner, Invocation? parent)
    { public GStreamerPlaybackSession Owner { get; } = owner; public Invocation? Parent { get; } = parent; public volatile bool Active = true; }
    private static readonly AsyncLocal<Invocation?> LogicalNative = new();
    [ThreadStatic] private static Invocation? PhysicalNative;
    private sealed class DriverScope(Invocation original, Invocation? prior) : IDisposable
    { public void Dispose() { original.Active = false; LogicalNative.Value = prior; } }
    private IDisposable EnterOriginalDriver()
    { var prior = LogicalNative.Value; var actual = new Invocation(this, prior); LogicalNative.Value = actual; return new DriverScope(actual, prior); }
    private T WithinOriginalNative<T>(Func<T> callback)
    {
        var prior = PhysicalNative; var actual = new Invocation(this, prior ?? LogicalNative.Value); PhysicalNative = actual;
        try { return callback(); } finally { actual.Active = false; PhysicalNative = prior; }
    }
    private void DemandExternalOriginalJoin()
    {
        static bool Contains(Invocation? current, GStreamerPlaybackSession owner)
        { for (; current is not null; current = current.Parent) if (current.Active && ReferenceEquals(current.Owner, owner)) return true; return false; }
        if (Contains(PhysicalNative, this) || Contains(LogicalNative.Value, this))
            throw new InvalidOperationException("An original GStreamer callback cannot join its SAME owning session retirement.");
    }
    private static MediaEngineResult<T> Fail<T>(string message) => MediaEngineResult<T>.Failure(new(
        MediaEngineErrorCode.PipelineFailed, message, "Reopen the media item and check GStreamer diagnostics.", null, true, true));
}
