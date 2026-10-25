using System.Text.Json;
using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Private manual console process fence. Fresh Files custody precedes Home acquisition;
/// only a finite native Start borrows the held Home entry. The maintained executor releases that
/// entry in its Start finally before any pipe/exit wait. No Task identity or source WRITE grant.</summary>
public sealed class HomeManualProjectProcessFence : IWorkspaceToolFinalFence,
    IWorkspaceOriginalProcessStartEntryReleaseFence, IAsyncDisposable
{
    private readonly HomeDeveloperManualProjectExecutionAdmissionSource _owner;
    private readonly HomeDeveloperManualProjectExecutionAdmissionSource.Run _run;
    private readonly HomeResourceOperationBroker _broker;
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeClaimedResourceAttestation _attestation;
    private readonly HomeResourceExecutionCapability _capability;
    private readonly AuthenticatedResourceActor _actor;
    private readonly CancellationToken _businessToken;
    private readonly object _sync = new();
    private readonly CloudflareOriginalTaskLedger _closeStages = new();
    private int _pins;
    private bool _closed, _refused, _startAttempted, _started;
    private Entry? _entry;
    private Task<IAsyncDisposable?>? _acquireDriver;
    private Task? _entryClose, _close;
    internal HomeManualProjectProcessFence(HomeDeveloperManualProjectExecutionAdmissionSource owner,
        HomeDeveloperManualProjectExecutionAdmissionSource.Run run, HomeResourceOperationBroker broker,
        FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles, HomeClaimedResourceAttestation attestation,
        HomeResourceExecutionCapability capability, AuthenticatedResourceActor actor, CancellationToken businessToken)
    {
        _owner = owner; _run = run; _broker = broker; _store = store; _profiles = profiles;
        _attestation = attestation; _capability = capability; _actor = actor; _businessToken = businessToken;
        _closeStages.BindOriginalOwner(this);
        OriginalCall = new("run_command", new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["command"] = JsonSerializer.SerializeToElement(run.Binding.Command),
            ["timeout_seconds"] = JsonSerializer.SerializeToElement(run.Binding.TimeoutSeconds)
        });
        RequestSha256 = WorkspaceToolOriginalDigest.Process(WorkspaceToolProcessRequestFactory.CreateOriginal(
            run.Binding.CanonicalRoot, run.Binding.Command, run.Binding.TimeoutSeconds));
    }
    public TaskRunAttemptAdmission? OriginalAttempt => null;
    public Guid ActionId => _run.OperationId;
    public string CanonicalWorkspaceRoot => _run.Binding.CanonicalRoot;
    public OllamaToolCall OriginalCall { get; }
    internal string RequestSha256 { get; }
    internal HomeDeveloperManualProjectExecutionAdmissionSource Owner => _owner;
    internal bool IsClosed { get { lock (_sync) return _closed; } }
    internal bool HasStarted { get { lock (_sync) return _started; } }
    public ValueTask<IAsyncDisposable?> AcquireOriginalCommitPinAsync(CancellationToken token)
    {
        lock (_sync)
        {
            if (_closed || _refused || _acquireDriver is not null) return ValueTask.FromResult<IAsyncDisposable?>(null);
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _acquireDriver = AcquirePublishedAsync(begin.Task, token); begin.SetResult(); return new(_acquireDriver);
        }
    }
    private async Task<IAsyncDisposable?> AcquirePublishedAsync(Task begin, CancellationToken token)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var sources = new CloudflareOriginalTaskLedger(); sources.BindOriginalOwner(this);
        try
        {
            return await sources.RunToOriginalSettlementAsync<IAsyncDisposable?>(async () =>
            {
                token.ThrowIfCancellationRequested(); _businessToken.ThrowIfCancellationRequested();
                await sources.AwaitAsync(sources.Invoke(() => _run.RevalidateAsync(token))).ConfigureAwait(false);
                IDeveloperWorkspaceOriginalExecutionCommitPin? native = null;
                IAsyncDisposable? completion = null; IHomeLocalOperationLease? home = null;
                return await CloudflareOriginalPartialEntryCustody.RunOriginalAsync<IAsyncDisposable?>(sources, async () =>
                {
                    native = await sources.CaptureOriginalAcquisitionAsync(() => _run.Files.AcquireOriginalManualExecutionPinAsync(_run.Binding, token),
                        actual => native = actual).ConfigureAwait(false);
                    sources.Invoke(() => { _run.Files.DemandOriginalManualExecutionPin(_run.Binding, native); return true; });
                    completion = await sources.CaptureOriginalAcquisitionAsync(() => _capability.AcquireCommitCompletionLeaseAsync(_broker, token),
                        actual => completion = actual).ConfigureAwait(false)
                        ?? throw new UnauthorizedAccessException("The original manual Run completion lease is unavailable.");
                    home = await sources.CaptureOriginalAcquisitionAsync(() => _store.AcquireLocalOperationLeaseCoreAsync(_profiles, _actor,
                        new ClaimedActorGuard(this, sources), body => RunOriginalScopedSource(sources, body),
                        actual => RetainOriginalScopedTask(sources, actual), token), actual => home = actual).ConfigureAwait(false)
                        ?? throw new UnauthorizedAccessException("The actual held manual Run Home entry was rejected.");
                    if (home is not IHomeOriginalScopedLocalOperationLease scopedHome ||
                        !await sources.AwaitAsync(sources.Invoke(() => scopedHome.IsCurrentAsync(body => RunOriginalScopedSource(sources, body),
                            actual => RetainOriginalScopedTask(sources, actual), token))).ConfigureAwait(false))
                        throw new UnauthorizedAccessException("The held Home actor/claim changed before native Start.");
                    sources.Invoke(() => { _run.Files.DemandOriginalManualExecutionPin(_run.Binding, native); return true; });
                    _run.DemandLive();
                    lock (_sync)
                    {
                        if (_closed || _refused || _entry is not null || _entryClose is not null)
                            throw new UnauthorizedAccessException("The original manual process entry retired before publication.");
                        _entry = new Entry(this, home, completion, native); _pins++;
                    }
                    home = null; completion = null; native = null; return new Pin(this);
                }, () =>
                {
                    var closes = new List<Func<ValueTask>>();
                    if (home is { } actualHome) closes.Add(actualHome.DisposeAsync);
                    if (completion is { } actualCompletion) closes.Add(actualCompletion.DisposeAsync);
                    if (native is { } actualNative) closes.Add(() => new ValueTask(_run.Files.CloseOriginalManualExecutionPinAsync(_run.Binding, actualNative)));
                    return closes;
                }).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            lock (_sync) _refused = true;
            sources.Retain(error);
            if (_entry is { } entry)
                try { await sources.ObserveOriginalCloseAsync(entry.DisposeAsync).ConfigureAwait(false); } catch (Exception close) { sources.Retain(close); }
            await sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            throw new AggregateException("Manual process held-entry acquisition or late cleanup failed.", sources.OriginalErrors);
        }
        finally
        {
            foreach (var actual in sources.OriginalTasks) _ = _closeStages.Track(actual);
            foreach (var error in sources.OriginalErrors) _closeStages.Retain(error);
        }
    }
    private void DemandPair(string root, WorkspaceToolEffectKind kind, string target, string sha)
    {
        _businessToken.ThrowIfCancellationRequested(); _run.DemandLive();
        if (_closed || _refused || _pins != 1 || _entry is not { IsClosing: false } || _entryClose is not null ||
            _acquireDriver?.IsCompletedSuccessfully != true || kind != WorkspaceToolEffectKind.ProcessStart ||
            root != CanonicalWorkspaceRoot || target != CanonicalWorkspaceRoot || sha != RequestSha256 ||
            !_capability.IsUncompletedClaim(_broker))
            throw new UnauthorizedAccessException("The exact manual Run command requires its SAME live Home entry and native root pin.");
        _run.Files.DemandOriginalManualExecutionPin(_run.Binding, _entry.NativePin);
    }
    public void DemandOriginalEffect(string root, WorkspaceToolEffectKind kind, string target, string sha)
    { lock (_sync) DemandPair(root, kind, target, sha); }
    public T RunOriginalEffect<T>(string root, WorkspaceToolEffectKind kind, string target, string sha, Func<T> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        lock (_sync)
        {
            DemandPair(root, kind, target, sha);
            if (_startAttempted) throw new UnauthorizedAccessException("The original manual native Start cannot be replayed.");
            _startAttempted = true;
            var result = CloudflareOriginalExecutionGuard.InvokeOriginal(this, body);
            _started = result is not bool started || started; return result;
        }
    }
    public void DemandExternalOriginalProcessStartEntryJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
    public Task ReleaseOriginalProcessStartEntryAsync(string root, string target, string sha)
    {
        DemandExternalOriginalProcessStartEntryJoin();
        if (root != CanonicalWorkspaceRoot || target != CanonicalWorkspaceRoot || sha != RequestSha256)
            throw new UnauthorizedAccessException("Manual process release does not match its exact native Start request.");
        return CloseEntryAsync();
    }
    private Task CloseEntryAsync()
    {
        lock (_sync)
        {
            if (_entryClose is not null) return _entryClose;
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _entryClose = ReleasePublishedAsync(begin.Task); begin.SetResult(); return _entryClose;
        }
    }
    private async Task ReleasePublishedAsync(Task begin)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        // The acquisition may have refused after publishing a late entry. Always join it first.
        Task<IAsyncDisposable?>? acquire; lock (_sync) acquire = _acquireDriver;
        if (acquire is not null)
            try { await _closeStages.AwaitAsync(acquire).ConfigureAwait(false); } catch (Exception error) { _closeStages.Retain(error); }
        Entry? entry; lock (_sync) entry = _entry;
        if (entry is not null)
            try { await _closeStages.ObserveOriginalCloseAsync(entry.DisposeAsync).ConfigureAwait(false); } catch (Exception error) { _closeStages.Retain(error); }
        if (_closeStages.OriginalErrors.Count != 0) throw new AggregateException("Manual process start entry release failed.", _closeStages.OriginalErrors);
    }
    public ValueTask DisposeAsync()
    {
        DemandExternalOriginalProcessStartEntryJoin();
        lock (_sync)
        {
            if (_close is not null) return new(_close); _closed = true;
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = ClosePublishedAsync(begin.Task); begin.SetResult(); return new(_close);
        }
    }
    private async Task ClosePublishedAsync(Task begin)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        try { await _closeStages.AwaitAsync(CloseEntryAsync()).ConfigureAwait(false); }
        catch (Exception error) { _closeStages.Retain(error); }
        await _closeStages.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (_closeStages.OriginalErrors.Count != 0) throw new AggregateException("Original manual process fence did not retire cleanly.", _closeStages.OriginalErrors);
    }

    private static void RunOriginalScopedSource(CloudflareOriginalTaskLedger sources, Action callback)
    { _ = sources.Invoke(() => { callback(); return true; }); }
    private static void RetainOriginalScopedTask(CloudflareOriginalTaskLedger sources, Task actual)
    { _ = sources.Track(actual); }

    private sealed class Pin(HomeManualProjectProcessFence owner) : IAsyncDisposable
    {
        private int _disposed;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) lock (owner._sync) owner._pins--;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Entry(HomeManualProjectProcessFence owner, IHomeLocalOperationLease home, IAsyncDisposable completion,
        IDeveloperWorkspaceOriginalExecutionCommitPin nativePin)
        : IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly CloudflareOriginalTaskLedger _closing = new();
        private Task? _close;
        internal HomeManualProjectProcessFence Owner => owner;
        internal IDeveloperWorkspaceOriginalExecutionCommitPin NativePin => nativePin;
        internal bool IsClosing { get { lock (_gate) return _close is not null; } }
        public ValueTask DisposeAsync()
        {
            CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
            Task actual; TaskCompletionSource begin;
            lock (_gate)
            {
                if (_close is not null) return new(_close);
                begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _closing.BindOriginalOwner(this);
                actual = ClosePublishedAsync(begin.Task); _close = actual;
            }
            begin.SetResult(); return new(actual);
        }
        private async Task ClosePublishedAsync(Task begin)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            // Start every actual close independently, Home first. No sibling failure/hold can skip
            // completion/native-pin release before the process wait.
            var closes = new List<Task>();
            foreach (var close in new Func<ValueTask>[] { home.DisposeAsync, completion.DisposeAsync,
                () => new ValueTask(owner._run.Files.CloseOriginalManualExecutionPinAsync(owner._run.Binding, nativePin)) })
                try { _ = _closing.Invoke(() => { var actual = close().AsTask(); closes.Add(actual); _ = _closing.Track(actual); return actual; }); }
                catch (Exception cause) { _closing.Retain(cause); }
            foreach (var actual in closes)
                try { await _closing.AwaitAsync(actual).ConfigureAwait(false); } catch (Exception cause) { _closing.Retain(cause); }
            await _closing.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (_closing.OriginalErrors.Count != 0) throw new AggregateException("Actual process Home/completion entry cleanup failed.", _closing.OriginalErrors);
        }
    }

    private sealed class ClaimedActorGuard(HomeManualProjectProcessFence fence, CloudflareOriginalTaskLedger sources)
        : IHomeOriginalScopedStateCommitActorGuard
    {
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected,
            HomeStateCommitPhase phase, CancellationToken token)
            => CheckAsync(state, expected, phase, body => RunOriginalScopedSource(sources, body),
                actualTask => RetainOriginalScopedTask(sources, actualTask), token);
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected,
            HomeStateCommitPhase phase, Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (expected != fence._actor || fence.IsClosed || fence._refused ||
                !fence._broker.IsClaimedAttestationCurrentInState(fence._attestation, expected, state))
                return ValueTask.FromResult(false);
            // Already-held Home readUnlocked/current profile only; no Files/source/completion/store acquisition.
            return new(sources.AwaitAsync(sources.Invoke(() => fence._profiles.CheckAsync(state, expected, phase,
                originalSynchronousScope, retainOriginalTask, token))));
        }
    }
}
