using Haven.Core.Media;

namespace HavenOS.Apps.Wave;

public sealed partial class WaveFilesProjectService
{
    private readonly object _auditionGate = new();
    private readonly List<OriginalSourceAudition> _originalAuditions = [];
    internal OriginalSourceAudition CreateOriginalSourceAudition(WaveProject project, long expectedRevision, Guid clipId,
        IMediaEngine sameEngine, Action<Action> sourceScope, Action<Task> retain, CancellationToken token)
    {
        lock (_auditionGate)
        {
            _originalAuditions.RemoveAll(owner => owner.OriginalClose?.IsCompletedSuccessfully == true);
            if (_originalAuditions.Count >= 16) throw new InvalidOperationException("Wave retains original source previews. Retire this same owner before opening another.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var owner = new OriginalSourceAudition(this, project, expectedRevision, clipId, sameEngine, sourceScope, retain, start.Task, token);
            _originalAuditions.Add(owner); start.SetResult(); return owner;
        }
    }
    // A source preview owns one actual Files lease and the maintained shared
    // playback session. It previews original audio; it is not a rendered mix.
    internal sealed class OriginalSourceAudition
    {
        private readonly WaveFilesProjectService _owner;
        private readonly IMediaEngine _engine;
        private readonly Action<Action> _outerScope;
        private readonly Action<Task> _outerRetain;
        private readonly object _gate = new();
        private readonly List<Task> _raw = [];
        private readonly Dictionary<Task, Task<bool>> _rawObservations = new(ReferenceEqualityComparer.Instance);
        private MediaAssetReadLease? _source;
        private IMediaPlaybackSession? _nativeSession, _unknownSession;
        private bool _nativeIssued, _nativeOwned, _retiring;
        private Task<MediaEngineResult<MediaAssetReadLease>>? _originalSourceRead;
        private Task<MediaEngineResult<IMediaPlaybackSession>>? _originalNativeOpen;
        private Task? _originalClose, _originalNativeClose, _originalLeaseClose;
        public WaveProject OriginalProject { get; }
        public WaveClip OriginalClip { get; }
        public Task<MediaEngineResult<MediaPlaybackState>> OriginalOpen { get; }
        public Task? OriginalClose { get { lock (_gate) return _originalClose; } }
        public bool HasNativeSession => _nativeOwned && !_retiring;
        public Task? OriginalNativeClose => _originalNativeClose;
        public Task? OriginalLeaseClose => _originalLeaseClose;
        internal OriginalSourceAudition(WaveFilesProjectService owner, WaveProject project, long expectedRevision, Guid clipId,
            IMediaEngine engine, Action<Action> outerScope, Action<Task> retain, Task start, CancellationToken token)
        {
            _owner = owner; _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _outerScope = outerScope ?? throw new ArgumentNullException(nameof(outerScope)); _outerRetain = retain ?? throw new ArgumentNullException(nameof(retain));
            WaveProjectStore.Validate(project);
            if (project.Revision != expectedRevision) throw new InvalidOperationException("The original preview project revision changed.");
            OriginalProject = project with { Tracks = project.Tracks.Select(track => track with { Clips = track.Clips.ToList() }).ToList(),
                Markers = project.Markers.ToList(), Regions = project.Regions.ToList() };
            OriginalClip = OriginalProject.Tracks.SelectMany(track => track.Clips).Single(clip => clip.ClipId == clipId);
            OriginalOpen = OpenOriginalAsync(start, token); lock (_gate) RetainRaw(OriginalOpen);
        }
        private async Task<MediaEngineResult<MediaPlaybackState>> OpenOriginalAsync(Task start, CancellationToken token)
        {
            using var driver = EnterDriver(); await start.ConfigureAwait(false);
            var clip = OriginalClip;
            if (string.IsNullOrWhiteSpace(clip.SourceFileID) || string.IsNullOrWhiteSpace(clip.SourceRevisionID))
                return Failure<MediaPlaybackState>(MediaEngineErrorCode.SourceUnavailable);
            var resolved = await Acquire(() => _originalSourceRead = _owner._sources.ResolveAsync(clip.SourceFileID, new(clip.SourceReferenceId), clip.SourceRevisionID, token)).ConfigureAwait(false);
            if (!resolved.IsSuccess) return MediaEngineResult<MediaPlaybackState>.Failure(resolved.Error!);
            _source = resolved.Value ?? throw new InvalidOperationException("Files did not return its original read lease.");
            if (!ValidLease(_source, new(clip.SourceReferenceId), clip.SourceFileID, clip.SourceRevisionID) ||
                !string.Equals(await Acquire(() => HashAsync(_source.Source.SourceUri.LocalPath, token)).ConfigureAwait(false), clip.SourceSha256, StringComparison.OrdinalIgnoreCase))
                return Failure<MediaPlaybackState>(MediaEngineErrorCode.RevisionConflict);
            var capabilities = await Acquire(() => _engine.GetCapabilitiesAsync(token)).ConfigureAwait(false);
            if (!capabilities.AudioPlaybackAvailable) return MediaEngineResult<MediaPlaybackState>.Failure(new(
                MediaEngineErrorCode.BackendUnavailable, "Audio playback is unavailable: " + string.Join(", ", capabilities.MissingComponents),
                "Connect the configured audio playback engine, then preview the source again.", clip.ClipId.ToString("D"), true, true));
            _nativeIssued = true;
            var opened = await Acquire(() => _originalNativeOpen = _engine.OpenPlaybackAsync(_source.Source, token)).ConfigureAwait(false);
            _unknownSession = opened.Value; // Retain even invalid or unknown observations.
            if (!opened.IsSuccess)
            {
                // A failed result does not prove an original native callback
                // created no pipeline. Keep the SAME source until ownership
                // can actually retire; never infer a no-effect receipt.
                return MediaEngineResult<MediaPlaybackState>.Failure(opened.Error!);
            }
            _nativeSession = opened.Value ?? throw new InvalidOperationException("The original engine returned no playback session.");
            _nativeOwned = true;
            if (!string.Equals(await Acquire(() => HashAsync(_source.Source.SourceUri.LocalPath, token)).ConfigureAwait(false), clip.SourceSha256, StringComparison.OrdinalIgnoreCase))
                return Failure<MediaPlaybackState>(MediaEngineErrorCode.RevisionConflict);
            return MediaEngineResult<MediaPlaybackState>.Success(MediaPlaybackState.Stopped);
        }
        public Task<MediaEngineResult<MediaPlaybackState>> SetStateAsync(MediaPlaybackState state, CancellationToken token = default) =>
            Admit(async () =>
            {
                var opened = await OriginalOpen.ConfigureAwait(false);
                return opened.IsSuccess
                    ? await Acquire(() => _nativeSession!.SetStateAsync(state, token)).ConfigureAwait(false) : opened;
            });
        public Task<MediaEngineResult<MediaTime>> SeekAsync(long sourceFrame, CancellationToken token = default) => Admit(async () =>
        {
            var opened = await OriginalOpen.ConfigureAwait(false);
            if (!opened.IsSuccess) return MediaEngineResult<MediaTime>.Failure(opened.Error!);
            var position = MediaTimebase.SamplesPerSecond(OriginalProject.SampleRate).At(sourceFrame);
            return await Acquire(() => _nativeSession!.SeekAsync(position, token)).ConfigureAwait(false);
        });
        public Task<MediaEngineResult<MediaTime>> ReadPositionAsync(CancellationToken token = default) => Admit(async () =>
        {
            var opened = await OriginalOpen.ConfigureAwait(false);
            return opened.IsSuccess
                ? await Acquire(() => _nativeSession!.GetPositionAsync(token)).ConfigureAwait(false)
                : MediaEngineResult<MediaTime>.Failure(opened.Error!);
        });
        private Task<T> Admit<T>(Func<Task<T>> body)
        {
            lock (_gate)
            {
                if (_retiring) throw new InvalidOperationException("The original source preview is retiring.");
                CheckCapacity(); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var original = RunAdmittedAsync(start.Task, body); RetainRaw(original); start.SetResult(); return original;
            }
        }
        private async Task<T> RunAdmittedAsync<T>(Task start, Func<Task<T>> body)
        { using var driver = EnterDriver(); await start.ConfigureAwait(false); return await body().ConfigureAwait(false); }
        private void CheckCapacity()
        {
            var observed = _raw.Where(task => _rawObservations.TryGetValue(task, out var receipt) &&
                receipt.IsCompletedSuccessfully && receipt.Result).ToArray();
            foreach (var task in observed) { _raw.Remove(task); _rawObservations.Remove(task); }
            if (_raw.Count >= 128) throw new InvalidOperationException("Wave retains unresolved original playback sources. Recover this same source preview.");
        }
        private void RetainRaw(Task sameOriginal)
        {
            if (_rawObservations.ContainsKey(sameOriginal)) return;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var observation = ObserveRawAsync(start.Task, sameOriginal);
            _raw.Add(sameOriginal); _rawObservations.Add(sameOriginal, observation); start.SetResult();
        }
        private static async Task<bool> ObserveRawAsync(Task start, Task sameOriginal)
        {
            await start.ConfigureAwait(false);
            try { await sameOriginal.ConfigureAwait(false); return true; }
            catch (Exception) { return false; } // Original failure/cancel remains in the same raw ledger.
        }
        private Task<T> Acquire<T>(Func<Task<T>> callback)
        {
            lock (_gate) CheckCapacity(); Task<T>? original = null;
            WithinSource(() =>
            {
                original = callback() ?? throw new InvalidOperationException("The source owner did not issue its original Task.");
                lock (_gate) RetainRaw(original); _outerRetain(original);
            });
            return original!;
        }
        private Task Acquire(Func<Task> callback)
        {
            lock (_gate) CheckCapacity(); Task? original = null;
            WithinSource(() =>
            {
                original = callback() ?? throw new InvalidOperationException("The source owner did not issue its original Task.");
                lock (_gate) RetainRaw(original); _outerRetain(original);
            });
            return original!;
        }
        public Task CloseOriginalAsync()
        {
            DemandExternalJoin();
            lock (_gate)
            {
                if (_originalClose is not null) return _originalClose;
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _retiring = true; _originalClose = CloseOriginalCoreAsync(start.Task); start.SetResult(); return _originalClose;
            }
        }
        private async Task CloseOriginalCoreAsync(Task start)
        {
            using var driver = EnterDriver(); await start.ConfigureAwait(false); var failures = new List<Exception>();
            var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
            while (true)
            {
                Task[] actual; lock (_gate) actual = _raw.Append(OriginalOpen).Concat(_originalSourceRead is null ? [] : new Task[] { _originalSourceRead })
                    .Concat(_originalNativeOpen is null ? [] : new Task[] { _originalNativeOpen }).Where(task => !joined.Contains(task)).Distinct().ToArray();
                if (actual.Length == 0) break;
                foreach (var source in actual) { joined.Add(source); try { await source.ConfigureAwait(false); } catch (Exception failure) { failures.Add(source.Exception ?? failure); } }
            }
            // Scope/retainer refusal can happen AFTER the issuer returned its
            // actual pending receipt. Adopt a later SAME successful result only
            // into retirement custody, never as a successful product open.
            // OriginalOpen stays failed and _retiring already refuses new work.
            if (_source is null && _originalSourceRead?.IsCompletedSuccessfully == true)
            {
                var actualRead = _originalSourceRead.Result;
                if (actualRead.IsSuccess) _source = actualRead.Value;
            }
            if (_originalNativeOpen?.IsCompletedSuccessfully == true)
            {
                var actualOpen = _originalNativeOpen.Result;
                _unknownSession ??= actualOpen.Value;
                if (!_nativeOwned && actualOpen.IsSuccess && actualOpen.Value is { } actualSession)
                { _nativeSession = actualSession; _nativeOwned = true; }
            }
            Task<bool>[] observations; lock (_gate) observations = _rawObservations.Values.ToArray();
            foreach (var originalObservation in observations)
                try { await originalObservation.ConfigureAwait(false); } catch (Exception failure) { failures.Add(originalObservation.Exception ?? failure); }
            var nativeRetired = !_nativeIssued;
            if (_nativeOwned)
            {
                try { _ = Acquire(() => _originalNativeClose = _nativeSession!.DisposeAsync().AsTask()); }
                catch (Exception failure) { failures.Add(failure); }
                if (_originalNativeClose is not null)
                    try { await _originalNativeClose.ConfigureAwait(false); nativeRetired = true; }
                    catch (Exception failure) { failures.Add(_originalNativeClose.Exception ?? failure); }
            }
            // Keep the actual Files lease while an original native pipeline may
            // still read it. A failed/unknown native close cannot release it.
            if (nativeRetired && _source is not null)
            {
                try { _ = Acquire(() => _originalLeaseClose = _source.DisposeAsync().AsTask()); }
                catch (Exception failure) { failures.Add(failure); }
                if (_originalLeaseClose is not null)
                    try { await _originalLeaseClose.ConfigureAwait(false); }
                    catch (Exception failure) { failures.Add(_originalLeaseClose.Exception ?? failure); }
            }
            while (true)
            {
                Task[] actual; lock (_gate) actual = _raw.Where(task => !joined.Contains(task)).Distinct().ToArray();
                if (actual.Length == 0) break;
                foreach (var source in actual) { joined.Add(source); try { await source.ConfigureAwait(false); } catch (Exception failure) { failures.Add(source.Exception ?? failure); } }
            }
            lock (_gate) observations = _rawObservations.Values.ToArray();
            foreach (var originalObservation in observations)
                try { await originalObservation.ConfigureAwait(false); } catch (Exception failure) { failures.Add(originalObservation.Exception ?? failure); }
            if (!nativeRetired && failures.Count == 0) failures.Add(new InvalidOperationException("The original engine has unresolved source ownership; retain its Files lease."));
            if (failures.Count != 0) throw new AggregateException("Wave retains original preview source or retirement failures.", failures);
        }
        private sealed class Invocation(OriginalSourceAudition owner, Invocation? parent)
        { public OriginalSourceAudition Owner { get; } = owner; public Invocation? Parent { get; } = parent; public volatile bool Active = true; }
        private static readonly AsyncLocal<Invocation?> Logical = new();
        [ThreadStatic] private static Invocation? Physical;
        private sealed class Driver(Invocation current, Invocation? prior) : IDisposable
        { public void Dispose() { current.Active = false; Logical.Value = prior; } }
        private IDisposable EnterDriver()
        { var prior = Logical.Value; var current = new Invocation(this, prior); Logical.Value = current; return new Driver(current, prior); }
        private void WithinSource(Action body)
        {
            var prior = Physical; var invocation = new Invocation(this, prior ?? Logical.Value); Physical = invocation;
            var caller = Environment.CurrentManagedThreadId; var active = true; var entered = false;
            var bodyFailures = new List<Exception>(); Exception? scopeFailure = null;
            try
            {
                try
                {
                    _outerScope(() =>
                    {
                        try
                        {
                            if (!active || entered || Environment.CurrentManagedThreadId != caller)
                                throw new InvalidOperationException("The original source scope must run once synchronously on its caller thread.");
                            entered = true; body();
                        }
                        catch (Exception failure) { if (!bodyFailures.Any(known => ReferenceEquals(known, failure))) bodyFailures.Add(failure); throw; }
                    });
                }
                catch (Exception failure) { scopeFailure = failure; }
            }
            finally { active = false; invocation.Active = false; Physical = prior; }
            if (!entered && scopeFailure is null) scopeFailure = new InvalidOperationException("The original preview source scope was not entered.");
            if (scopeFailure is not null && !bodyFailures.Any(known => ReferenceEquals(known, scopeFailure))) bodyFailures.Add(scopeFailure);
            if (bodyFailures.Count > 1)
                throw new AggregateException("Wave retains every independent original preview callback and source scope failure.", bodyFailures);
            if (bodyFailures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(bodyFailures[0]).Throw();
        }
        public void DemandExternalJoin()
        {
            static bool Contains(Invocation? current, OriginalSourceAudition owner)
            { for (; current is not null; current = current.Parent) if (current.Active && ReferenceEquals(current.Owner, owner)) return true; return false; }
            if (Contains(Physical, this) || Contains(Logical.Value, this))
                throw new InvalidOperationException("An original preview callback cannot join its owning source retirement.");
        }
    }
}
