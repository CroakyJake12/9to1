using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Genuine manual (no-AI) Home source-EDIT final fence. It is vended only by the
/// SAME Home edit admission source for its own private Edit — never from a caller-supplied or
/// AlwaysAllow admission — and it binds the exact canonical root, stored project-relative target
/// and reviewed BEFORE/AFTER digests. The cheap retirement pin holds no Home leases and is acquired
/// before staging; the final <see cref="AcquireOriginalHomeCommitEntryAsync"/> freshly revalidates
/// the configured Files source/capture/scopes and the real Home claim, acquires the genuine Files-owned
/// retained manual currentness pin (also before any Home lease), then acquires the genuine claimed
/// Home completion + state entry only immediately before the native swap. The successful entry is
/// published BEFORE its Home/completion leases are transferred, and the finite native swap requires
/// that SAME live entry plus the SAME live currentness pin (probed synchronously under the held Home
/// gate). A changed target, changed AFTER payload, changed scope, closed entry, retired/mutated
/// captured source or real withdrawal is refused with zero effects.</summary>
public sealed class HomeSourceEditCommitFence : IWorkspaceManualEditFinalFence, IAsyncDisposable
{
    private readonly HomeDeveloperProjectEditAdmissionSource _owner;
    private readonly HomeDeveloperProjectEditAdmissionSource.Edit _edit;
    private readonly HomeResourceOperationBroker _broker;
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeClaimedResourceAttestation _attestation;
    private readonly HomeResourceExecutionCapability _capability;
    private readonly AuthenticatedResourceActor _actor;
    private readonly Guid _operationId;
    private readonly string _root;
    private readonly string _relativePath;
    private readonly string _beforeSha256;
    private readonly string _afterText;
    private readonly string _afterSha256;
    private readonly OllamaToolCall _call;
    private readonly IDeveloperProjectOriginalEditCurrentnessPinSource _scopeSource;
    private readonly object _sync = new();
    private readonly CloudflareOriginalTaskLedger _closeStages = new();
    private int _pins;
    private bool _closed;
    private bool _refused;
    private bool _committedEffect;
    private Entry? _entry;
    private IDeveloperProjectOriginalEditCurrentnessPin? _pin;
    private Task<IAsyncDisposable?>? _acquireDriver;
    private Task? _close;

    internal HomeSourceEditCommitFence(HomeDeveloperProjectEditAdmissionSource owner,
        HomeDeveloperProjectEditAdmissionSource.Edit edit,
        HomeResourceOperationBroker broker, FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeClaimedResourceAttestation attestation, HomeResourceExecutionCapability capability,
        AuthenticatedResourceActor actor, Guid operationId, string root, string relativePath,
        string beforeSha256, string afterText, string afterSha256,
        IDeveloperProjectOriginalEditCurrentnessPinSource scopeSource)
    {
        _owner = owner; _edit = edit; _broker = broker; _store = store; _profiles = profiles;
        _attestation = attestation; _capability = capability; _actor = actor;
        _operationId = operationId; _root = root; _relativePath = relativePath;
        _beforeSha256 = beforeSha256; _afterText = afterText; _afterSha256 = afterSha256;
        _scopeSource = scopeSource;
        _closeStages.BindOriginalOwner(this);
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["path"] = JsonSerializer.SerializeToElement(relativePath),
            ["content"] = JsonSerializer.SerializeToElement(afterText)
        };
        _call = new OllamaToolCall("write_file", arguments);
    }

    /// <summary>Genuine manual (no-AI): never a task-attempt admission.</summary>
    public TaskRunAttemptAdmission? OriginalAttempt => null;
    public Guid ActionId => _operationId;
    public string CanonicalWorkspaceRoot => _root;
    public OllamaToolCall OriginalCall => _call;
    public string ReviewedBeforeSha256 => _beforeSha256;
    public bool RequiresAbsentTarget => _edit.RequiresAbsentTarget;
    internal string RelativePath => _relativePath;
    internal string BeforeSha256 => _beforeSha256;
    internal string ReviewedAfterSha256 => _afterSha256;
    internal string ReviewedAfterText => _afterText;
    internal HomeDeveloperProjectEditAdmissionSource Owner => _owner;
    internal bool IsClosed { get { lock (_sync) return _closed; } }
    internal bool HasCommittedEffect { get { lock (_sync) return _committedEffect; } }

    internal void Seal() { lock (_sync) _closed = true; }
    private void DemandLive()
    { lock (_sync) if (_closed) throw new ObjectDisposedException("Home source edit final fence"); }

    /// <summary>Cheap retirement pin: no Home/completion/store leases are held here. It guards the
    /// finite effect pairing and confirms the claim is still uncompleted at staging start; it does
    /// not lease any Home state across asynchronous staging.</summary>
    public ValueTask<IAsyncDisposable?> AcquireOriginalCommitPinAsync(CancellationToken token)
    {
        lock (_sync) if (_closed || _refused) return ValueTask.FromResult<IAsyncDisposable?>(null);
        token.ThrowIfCancellationRequested();
        if (!_capability.IsUncompletedClaim(_broker)) return ValueTask.FromResult<IAsyncDisposable?>(null);
        lock (_sync)
        {
            if (_closed || _refused) return ValueTask.FromResult<IAsyncDisposable?>(null);
            _pins++;
        }
        return ValueTask.FromResult<IAsyncDisposable?>(new Pin(this));
    }

    private async Task FreshlyValidateAsync(CancellationToken token)
    {
        try { await _edit.RevalidateOriginalEditAsync(token).ConfigureAwait(false); }
        catch (UnauthorizedAccessException)
        {
            // Real withdrawal (a blocked caller) leaves the original claim uncompleted; this fresh
            // revalidation detects it and refuses every subsequent finite effect/entry.
            lock (_sync) _refused = true;
            throw;
        }
    }

    /// <summary>Acquires the genuine Home claimed-actor/scope/profile/completion entry immediately
    /// before the final native swap. The actual entry-acquisition driver is published up front and
    /// later joined by retirement (including its late products), so close never misses a late-published
    /// entry. Full async Files/source/preimage validation runs before any Home lease is held.</summary>
    public ValueTask<IAsyncDisposable?> AcquireOriginalHomeCommitEntryAsync(CancellationToken token)
    {
        lock (_sync)
        {
            if (_closed || _refused) return ValueTask.FromResult<IAsyncDisposable?>(null);
            if (_pins == 0 || !_capability.IsUncompletedClaim(_broker)) return ValueTask.FromResult<IAsyncDisposable?>(null);
            if (_acquireDriver is not null) return new(_acquireDriver);
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var driver = AcquireDriverAsync(begin.Task, token);
            _acquireDriver = driver; // publish the actual entry-acquisition driver before callbacks run
            begin.SetResult();
            return new(driver);
        }
    }

    private async Task<IAsyncDisposable?> AcquireDriverAsync(Task begin, CancellationToken token)
    {
        await begin.ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        // Full async Files/source/capture/scope and real Home-claim revalidation MUST run before any
        // Home/completion lease is acquired — never recursively beneath the held Home store gate
        // (that would self-deadlock). A blocked caller (real withdrawal) or changed reviewed
        // BEFORE/target/scope is refused here with zero effects.
        await FreshlyValidateAsync(token).ConfigureAwait(false);
        lock (_sync)
        {
            if (_closed || _refused) return null;
            if (_pins == 0 || _entry is not null || !_capability.IsUncompletedClaim(_broker)) return null;
        }
        // Genuine Files-owned retained manual currentness custody is acquired here, outside any held
        // Home lease. Full async SAME selection/capture/config/actor/file revalidation runs now; the
        // cheap returned probe is synchronous and safe beneath the held Home entry.
        IDeveloperProjectOriginalEditCurrentnessPin? pin = null;
        bool transferred = false;
        pin = await (RequiresAbsentTarget
            ? _scopeSource.AcquireOriginalCreateCurrentnessPinAsync(_edit.Selection, _edit.Capture, _relativePath, _afterSha256, token)
            : _scopeSource.AcquireOriginalEditCurrentnessPinAsync(_edit.Selection, _edit.Capture, _relativePath, _beforeSha256, _afterSha256, token)).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original manual edit currentness producer returned no genuine pin.");
        var sources = new CloudflareOriginalTaskLedger();
        sources.BindOriginalOwner(this);
        try
        {
            return await sources.RunToOriginalSettlementAsync<IAsyncDisposable>(async () =>
            {
                IAsyncDisposable? completion = null; IHomeLocalOperationLease? home = null;
                return await CloudflareOriginalPartialEntryCustody.RunOriginalAsync<IAsyncDisposable>(sources, async () =>
                {
                    completion = await sources.CaptureOriginalAcquisitionAsync(
                        () => _capability.AcquireCommitCompletionLeaseAsync(_broker, token),
                        actual => completion = actual).ConfigureAwait(false)
                        ?? throw new UnauthorizedAccessException("The original Home edit completion lease is unavailable.");
                    home = await sources.CaptureOriginalAcquisitionAsync(
                        () => _store.AcquireLocalOperationLeaseCoreAsync(_profiles, _actor,
                            new ClaimedActorGuard(this, sources),
                            body => RunOriginalScopedSource(sources, body),
                            actualTask => RetainOriginalScopedTask(sources, actualTask), token),
                        actual => home = actual).ConfigureAwait(false)
                        ?? throw new UnauthorizedAccessException("The actual held Home edit entry was rejected.");
                    if (home is not IHomeOriginalScopedLocalOperationLease scopedHome)
                        throw new InvalidOperationException("The actual held edit entry requires the original scoped Home callback producer.");
                    if (!await sources.AwaitAsync(sources.Invoke(() => scopedHome.IsCurrentAsync(
                        body => RunOriginalScopedSource(sources, body),
                        actualTask => RetainOriginalScopedTask(sources, actualTask), token))).ConfigureAwait(false))
                        throw new UnauthorizedAccessException("The held Home actor/claim changed before the native write admission.");
                    var entry = new Entry(this, home, completion);
                    lock (_sync)
                    {
                        if (_closed || _refused || _pins == 0 || _entry is not null)
                            throw new UnauthorizedAccessException("The original Home edit fence is closed or not staged.");
                        // Publish the actual entry and transfer the SAME successful live currentness pin
                        // BEFORE transferring Home/completion leases.
                        _entry = entry; _pin = pin; transferred = true;
                    }
                    home = null; completion = null; return entry;
                }, () =>
                {
                    var closes = new List<Func<ValueTask>>();
                    if (home is { } actualHome) closes.Add(actualHome.DisposeAsync);
                    if (completion is { } actualCompletion) closes.Add(actualCompletion.DisposeAsync);
                    return closes;
                }).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (Exception primary)
        {
            sources.Retain(primary);
            // Close the genuine currentness pin if it was never transferred to the published entry.
            if (!transferred && pin is { } actualPin)
                try { await sources.ObserveOriginalCloseAsync(actualPin.DisposeAsync).ConfigureAwait(false); }
                catch (Exception cause) { sources.Retain(cause); }
            // A scope may fault after returning a successful acquisition. The real entry remains
            // privately owned and closes before the failed acquisition settles.
            if (_entry is { } actualEntry)
                try { await sources.ObserveOriginalCloseAsync(actualEntry.DisposeAsync).ConfigureAwait(false); }
                catch (Exception cause) { sources.Retain(cause); }
            await sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            throw new AggregateException("Original held Home edit entry acquisition and independent late cleanup failed.", sources.OriginalErrors);
        }
        finally
        {
            foreach (var actual in sources.OriginalTasks) _ = _closeStages.Track(actual);
            foreach (var cause in sources.OriginalErrors) _closeStages.Retain(cause);
        }
    }

    public void DemandOriginalEffect(string actualRoot, WorkspaceToolEffectKind kind, string target, string sha)
    {
        lock (_sync) DemandOriginalEffectCore(actualRoot, kind, target, sha);
    }

    /// <summary>The finite committed swap guard. The SAME privately owned, successful, currently held
    /// Home entry AND the SAME successful live currentness pin are mandatory; a cheap staging pin alone
    /// can never back a commit.</summary>
    private void DemandOriginalEffectCore(string actualRoot, WorkspaceToolEffectKind kind, string target, string sha)
    {
        if (_entry is not { } entry || !ReferenceEquals(entry.Owner, this))
            throw new UnauthorizedAccessException("The original manual edit final swap requires the SAME privately held live Home entry.");
        if (entry.IsClosing)
            throw new UnauthorizedAccessException("The original Home edit entry closed before the finite native swap.");
        if (_pin is not { } pin)
            throw new UnauthorizedAccessException("The original manual edit final swap requires genuine Files currentness custody.");
        pin.DemandOriginalEditCurrentness();
        if (pin.RequiresAbsentTarget != RequiresAbsentTarget)
            throw new UnauthorizedAccessException("The SAME native pin must bind the reviewed CREATE or EDIT precondition.");
        if (!string.Equals(pin.ReviewedAfterSha256, _afterSha256, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The SAME manual edit currentness pin is bound to a different reviewed AFTER revision.");
        DemandOriginalParingCore(actualRoot, kind, target, sha);
    }

    public void DemandOriginalStage(string actualRoot, WorkspaceToolEffectKind kind, string target, string sha)
    {
        lock (_sync) DemandOriginalStageCore(actualRoot, kind, target, sha);
    }

    /// <summary>Private pre-commit staging guard: satisfied by the cheap pin alone and never grants a
    /// commit. It shares the exact root/target/kind/digest pairing checks with the final swap.</summary>
    private void DemandOriginalStageCore(string actualRoot, WorkspaceToolEffectKind kind, string target, string sha)
    {
        DemandOriginalParingCore(actualRoot, kind, target, sha);
    }

    private void DemandOriginalParingCore(string actualRoot, WorkspaceToolEffectKind kind, string target, string sha)
    {
        if (_closed || _pins == 0) throw new UnauthorizedAccessException("Original manual edit pin/effect pairing refused.");
        if (_refused)
            throw new UnauthorizedAccessException("The original Home edit authorization was revoked before the finite native effect.");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var effectiveRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(actualRoot));
        var expectedTarget = Path.GetFullPath(Path.Combine(effectiveRoot, _relativePath));
        var actualTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        if (kind != WorkspaceToolEffectKind.AtomicWrite || !Enum.IsDefined(kind) ||
            !string.Equals(effectiveRoot, _root, comparison) ||
            !string.Equals(actualTarget, expectedTarget, comparison))
            throw new UnauthorizedAccessException("Original manual edit root/target/kind is outside the reviewed file/revision.");
        if (OriginalCall.Name is not ("write_file" or "replace_in_file" or "apply_change_set"))
            throw new UnauthorizedAccessException("Original manual edit call does not own this effect.");
        if (!string.Equals(sha, _afterSha256, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The original manual edit effect digest does not match the reviewed AFTER payload.");
    }

    public T RunOriginalEffect<T>(string actualRoot, WorkspaceToolEffectKind kind, string target, string sha, Func<T> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        lock (_sync)
        {
            DemandOriginalEffectCore(actualRoot, kind, target, sha);
            // The held Home lease holds the store gate, so revocation cannot interleave while the
            // pin/entry is live; this synchronous claim check additionally refuses any in-memory
            // completion/rejection observed after pin acquisition and before the finite effect.
            if (!_capability.IsUncompletedClaim(_broker))
                throw new UnauthorizedAccessException("The original Home edit claim completed or was revoked before the finite effect.");
            var result = body();
            _committedEffect = true;
            return result;
        }
    }

    public T RunOriginalStage<T>(string actualRoot, WorkspaceToolEffectKind kind, string target, string sha, Func<T> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        lock (_sync)
        {
            DemandOriginalStageCore(actualRoot, kind, target, sha);
            if (!_capability.IsUncompletedClaim(_broker))
                throw new UnauthorizedAccessException("The original Home edit claim completed or was revoked before native staging.");
            return body();
        }
    }

    public ValueTask DisposeAsync()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        Task actual; TaskCompletionSource begin;
        lock (_sync)
        {
            if (_close is not null) return new(_close);
            _closed = true;
            begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = ClosePublishedAsync(begin.Task); _close = actual;
        }
        begin.SetResult(); return new(actual);
    }
    private async Task ClosePublishedAsync(Task begin)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var errors = new List<Exception>();
        Task<IAsyncDisposable?>? driver; Entry? entry;
        lock (_sync) { driver = _acquireDriver; entry = _entry; }
        // Retirement joins the actual entry-acquisition driver (including its late-returned products and
        // the genuine currentness pin) before settling, so a late-published product is never missed.
        // Every ORIGINAL acquisition failure is preserved even when it settled before retirement; each
        // cause is deduped by reference so it is reported once without re-wrapping a single aggregate.
        if (driver is not null)
        {
            Exception? driverFailure = null;
            try { await driver.ConfigureAwait(false); }
            catch (Exception cause) { driverFailure = cause; }
            if (driverFailure is not null)
            {
                var causes = (driverFailure as AggregateException)?.InnerExceptions ?? new[] { driverFailure }.AsEnumerable();
                foreach (var cause in causes)
                    if (!errors.Any(prior => ReferenceEquals(prior, cause))) errors.Add(cause);
            }
        }
        lock (_sync) entry = _entry;
        if (entry is not null)
            try { await _closeStages.AwaitAsync(_closeStages.Invoke(() => entry.DisposeAsync())).ConfigureAwait(false); }
            catch (Exception cause) { errors.Add(cause); }
        IDeveloperProjectOriginalEditCurrentnessPin? pin; lock (_sync) pin = _pin;
        if (pin is not null)
            try { await _closeStages.AwaitAsync(_closeStages.Invoke(() => pin.DisposeAsync())).ConfigureAwait(false); }
            catch (Exception cause) { errors.Add(cause); }
        await _closeStages.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        foreach (var cause in _closeStages.OriginalErrors)
            if (!errors.Any(prior => ReferenceEquals(prior, cause))) errors.Add(cause);
        if (errors.Count != 0) throw new AggregateException("Actual Home edit fence retirement failed.", errors);
    }

    private static void RunOriginalScopedSource(CloudflareOriginalTaskLedger sources, Action callback)
    { _ = sources.Invoke(() => { callback(); return true; }); }
    private static void RetainOriginalScopedTask(CloudflareOriginalTaskLedger sources, Task actual)
    { _ = sources.Track(actual); }

    private sealed class Pin(HomeSourceEditCommitFence owner) : IAsyncDisposable
    {
        private int _disposed;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) lock (owner._sync) owner._pins--;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Entry(HomeSourceEditCommitFence owner, IHomeLocalOperationLease home, IAsyncDisposable completion)
        : IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly CloudflareOriginalTaskLedger _closing = new();
        private Task? _close;
        internal HomeSourceEditCommitFence Owner => owner;
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
            // completion release before the finite swap settles.
            var closes = new List<Task>();
            foreach (var close in new Func<ValueTask>[] { home.DisposeAsync, completion.DisposeAsync })
                try { _ = _closing.Invoke(() => { var actual = close().AsTask(); closes.Add(actual); _ = _closing.Track(actual); return actual; }); }
                catch (Exception cause) { _closing.Retain(cause); }
            foreach (var actual in closes)
                try { await _closing.AwaitAsync(actual).ConfigureAwait(false); } catch (Exception cause) { _closing.Retain(cause); }
            await _closing.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (_closing.OriginalErrors.Count != 0) throw new AggregateException("Actual edit Home/completion entry cleanup failed.", _closing.OriginalErrors);
        }
    }

    private sealed class ClaimedActorGuard(HomeSourceEditCommitFence fence, CloudflareOriginalTaskLedger sources)
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
