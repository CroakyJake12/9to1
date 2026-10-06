using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>One explicit Home review of a genuine saved project/root and exact canonical
/// command preparation. Setup/READ/CAKE identity and public IDs cannot issue this consent.
/// The additional held entry protects only finite Process.Start, not process success.</summary>
public sealed partial class HomeDeveloperWorkspaceExecutionConsentSource : IWorkspaceOriginalProcessStartConsentSource, IAsyncDisposable
{
    public const string ExecuteAction = "dev.workspace.execute";
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeResourceOperationBroker _broker;
    private readonly Func<IDeveloperWorkspaceOriginalExecutionBindingSource> _bindings;
    private readonly Func<ITaskRunToolActionOwner> _tools;
    private readonly object _gate = new();
    private readonly Dictionary<ITaskRunToolActionPreparation, Consent> _issued = new(ReferenceEqualityComparer.Instance);
    private readonly CloudflareOriginalTaskLedger _closing = new();
    private bool _retiring;
    private Task? _close;
    public HomeDeveloperWorkspaceExecutionConsentSource(FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceOperationBroker broker, HomePermissionTrustService permissions,
        Func<IDeveloperWorkspaceOriginalExecutionBindingSource> bindings, Func<ITaskRunToolActionOwner> tools)
    {
        ArgumentNullException.ThrowIfNull(bindings); ArgumentNullException.ThrowIfNull(tools);
        if (!profiles.IsBoundToStore(store) || !permissions.IsBoundToStore(store) || !broker.IsBoundToPermissions(permissions))
            throw new UnauthorizedAccessException("SAME actual Home store/profile/broker/policy is required.");
        _store = store; _profiles = profiles; _broker = broker; _bindings = bindings; _tools = tools;
    }
    public Task<IWorkspaceOriginalProcessStartConsent> AcquireOriginalAsync(IDeveloperWorkspaceOriginalExecutionBinding binding,
        IWorkspaceToolActionPreparation preparation, TaskExecutionSnapshot current, Action<Action> callback, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding); ArgumentNullException.ThrowIfNull(preparation); ArgumentNullException.ThrowIfNull(callback);
        DemandExternalOriginalProcessStartConsentJoin();
        Consent original;
        lock (_gate)
        {
            if (_retiring) throw new ObjectDisposedException(nameof(HomeDeveloperWorkspaceExecutionConsentSource));
            if (_issued.TryGetValue(preparation, out original!))
            {
                if (!ReferenceEquals(original.Binding, binding) || !original.Callback.Equals(callback))
                    throw new UnauthorizedAccessException("The original saved-root binding/caller cannot be replaced.");
                return original.Acquisition;
            }
            if (_issued.Count >= 128) throw new InvalidOperationException("Retained original execution consent custody is full.");
            original = new(this, binding, preparation, callback); _issued.Add(preparation, original);
            return original.Start(current, token);
        }
    }
    private static Action<Action> BindOriginalSynchronousCaller(Action<Action> originalCaller) => originalBody =>
    {
        int active = 1, invoked = 0; int originalThread = Environment.CurrentManagedThreadId;
        try
        {
            originalCaller(() =>
            {
                if (Volatile.Read(ref active) == 0)
                    throw new InvalidOperationException("The original execution caller callback phase has ended.");
                if (Environment.CurrentManagedThreadId != originalThread)
                    throw new InvalidOperationException("The original execution callback must run on its synchronous issuing thread.");
                if (Interlocked.CompareExchange(ref invoked, 1, 0) != 0)
                    throw new InvalidOperationException("The original execution caller callback can run only once.");
                originalBody();
            });
            if (Volatile.Read(ref invoked) == 0)
                throw new InvalidOperationException("The synchronous execution caller did not invoke the original callback.");
        }
        finally { Volatile.Write(ref active, 0); }
    };
    private Consent Require(IWorkspaceOriginalProcessStartConsent consent, ITaskRunToolActionPreparation preparation)
    {
        lock (_gate) return consent is Consent original && _issued.TryGetValue(preparation, out var retained)
            && ReferenceEquals(original, retained) ? original : throw new UnauthorizedAccessException("SAME private Home consent/preparation is required.");
    }
    public bool IsIssuedOriginalConsent(IWorkspaceOriginalProcessStartConsent consent, ITaskRunToolActionPreparation preparation)
    {
        Consent? actual;
        lock (_gate) actual = !_retiring && consent is Consent original && _issued.TryGetValue(preparation, out var retained)
            && ReferenceEquals(original, retained) ? original : null;
        return actual is { IsLive: true, Acquisition.IsCompletedSuccessfully: true };
    }
    public Task ValidateOriginalConsentAsync(IWorkspaceOriginalProcessStartConsent consent, ITaskRunToolActionPreparation preparation, CancellationToken token)
        => Require(consent, preparation).ValidateAsync(token);
    public Task<IWorkspaceOriginalProcessStartEntry> EnterOriginalProcessStartAsync(IWorkspaceOriginalProcessStartConsent consent,
        ITaskRunToolActionPreparation preparation, CancellationToken token) => Require(consent, preparation).EnterAsync(token);
    public bool IsIssuedOriginalEntry(IWorkspaceOriginalProcessStartConsent consent, ITaskRunToolActionPreparation preparation,
        IWorkspaceOriginalProcessStartEntry entry) => IsIssuedOriginalConsent(consent, preparation) && Require(consent, preparation).IsIssuedEntry(entry);
    public void RequestOriginalExecutionRetirement()
    {
        Consent[] originals; lock (_gate) { _retiring = true; originals = _issued.Values.ToArray(); }
        foreach (var original in originals) original.RequestRetirement();
    }
    public void DemandExternalOriginalProcessStartConsentJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        Consent[] originals; lock (_gate) originals = _issued.Values.ToArray();
        foreach (var original in originals) original.DemandExternalJoin();
    }
    public Task CloseAndDrainOriginalExecutionsAsync()
    {
        DemandExternalOriginalProcessStartConsentJoin(); Task actual; TaskCompletionSource begin; Consent[] originals;
        lock (_gate)
        {
            if (_close is not null) return _close;
            _retiring = true; originals = _issued.Values.ToArray(); begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _closing.BindOriginalOwner(this); actual = ClosePublishedAsync(begin.Task, originals); _close = actual;
        }
        begin.SetResult(); return actual;
    }
    private async Task ClosePublishedAsync(Task begin, Consent[] originals)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        foreach (var original in originals) original.RequestRetirement();
        var closes = new List<Task>();
        foreach (var original in originals)
            try { closes.Add(_closing.Invoke(() => original.DisposeAsync().AsTask())); } catch (Exception cause) { _closing.Retain(cause); }
        foreach (var actual in closes) try { await _closing.AwaitAsync(actual).ConfigureAwait(false); } catch (Exception cause) { _closing.Retain(cause); }
        await _closing.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (_closing.OriginalErrors.Count != 0) throw new AggregateException("Actual execution consent owners did not drain cleanly.", _closing.OriginalErrors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalExecutionsAsync());

    private sealed partial class Consent(HomeDeveloperWorkspaceExecutionConsentSource owner,
        IDeveloperWorkspaceOriginalExecutionBinding binding, IWorkspaceToolActionPreparation preparation, Action<Action> callback)
        : IWorkspaceOriginalProcessStartConsent
    {
        private HomeDeveloperWorkspaceExecutionConsentSource Owner => owner;
        internal IDeveloperWorkspaceOriginalExecutionBinding Binding => binding;
        internal Action<Action> Callback => callback;
        private readonly Action<Action> _guardedCaller = BindOriginalSynchronousCaller(callback);
        private readonly object _gate = new();
        private readonly CloudflareOriginalTaskLedger _sources = new();
        private readonly CloudflareOriginalTaskLedger _closing = new();
        private readonly List<(Task Driver, CloudflareOriginalTaskLedger Sources)> _validations = [];
        internal Task<IWorkspaceOriginalProcessStartConsent> Acquisition = null!;
        private IDeveloperWorkspaceOriginalExecutionBindingSource _bindingSource = null!;
        private IDeveloperWorkspaceOriginalExecutionScopedBindingSource _scopedBindingSource = null!;
        private IDeveloperWorkspaceOriginalExecutionCommitBindingSource _commitSource = null!;
        private IDeveloperWorkspaceOriginalExecutionPinCustodySource _pinCustody = null!;
        private ITaskRunToolActionOwner _toolSource = null!;
        private ResourceScope[] _scopes = [];
        private HomeResourcePreparedReview? _review;
        private HomeResourceExecutionCapability? _capability;
        private HomeClaimedResourceAttestation? _attestation;
        private CancellationTokenSource? _stop;
        private Task<IWorkspaceOriginalProcessStartEntry>? _entryAcquisition;
        private Entry? _entry;
        private bool _retiring;
        private Task? _close;
        internal bool IsLive { get { lock (_gate) return !_retiring; } }
        internal void RequestRetirement() { lock (_gate) _retiring = true; }
        private void DemandLive()
        { lock (owner._gate) lock (_gate) if (owner._retiring || _retiring) throw new ObjectDisposedException("Original execution consent"); }
        internal void DemandExternalJoin()
        {
            CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
            _bindingSource?.DemandExternalOriginalExecutionBindingJoin(); _entry?.DemandExternalOriginalProcessStartEntryJoin();
        }
        internal Task<IWorkspaceOriginalProcessStartConsent> Start(TaskExecutionSnapshot current, CancellationToken token)
        {
            _sources.BindOriginalOwner(this); _sources.BindOriginalCallerCallback(_guardedCaller);
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Acquisition = AcquirePublishedAsync(begin.Task, current, token); begin.SetResult(); return Acquisition;
        }
        private CloudflareOriginalTaskLedger NewSources(object originalOwner)
        { var sources = new CloudflareOriginalTaskLedger(); sources.BindOriginalOwner(originalOwner); sources.BindOriginalCallerCallback(_guardedCaller); return sources; }
        private static void RunOriginalScopedSource(CloudflareOriginalTaskLedger sources, Action callback)
        { _ = sources.Invoke(() => { callback(); return true; }); }
        private static void RetainOriginalScopedTask(CloudflareOriginalTaskLedger sources, Task actual)
        { _ = sources.Track(actual); }
        private async Task ValidateSourcesAsync(CloudflareOriginalTaskLedger sources, CancellationToken token)
        {
            DemandLive();
            if (!sources.Invoke(() => ReferenceEquals(owner._bindings(), _bindingSource) && ReferenceEquals(owner._tools(), _toolSource)
                && _bindingSource.IsIssuedOriginalBinding(binding)))
                throw new UnauthorizedAccessException("The genuine configured saved project/root issuer changed or is unavailable.");
            await sources.AwaitAsync(sources.Invoke(() => _scopedBindingSource.RevalidateOriginalWithinSourceAsync(binding, binding.OriginalActor,
                body => RunOriginalScopedSource(sources, body), actual => RetainOriginalScopedTask(sources, actual), token))).ConfigureAwait(false);
            var actor = await sources.AwaitAsync(sources.Invoke(() => owner._profiles.GetCurrentAsync(body => RunOriginalScopedSource(sources, body),
                actual => RetainOriginalScopedTask(sources, actual), token))).ConfigureAwait(false);
            if (actor is null || actor != binding.OriginalActor || !sources.Invoke(() => _bindingSource.IsIssuedOriginalBinding(binding)
                && _bindingSource.GetOriginalExecutionScopes(binding).SequenceEqual(_scopes)))
                throw new UnauthorizedAccessException("The actual saved-root binding or Home actor changed after its reads.");
            DemandLive();
        }
        private async Task<IWorkspaceOriginalProcessStartConsent> AcquirePublishedAsync(Task begin, TaskExecutionSnapshot current, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            return await _sources.RunToOriginalSettlementAsync<IWorkspaceOriginalProcessStartConsent>(async () =>
            {
                DemandLive();
                if (preparation.OriginalCall.Name is not ("run_command" or "run_tests") || binding.WorkspaceId == Guid.Empty || binding.ProjectId == Guid.Empty
                    || binding.RootId == Guid.Empty || binding.WorkspaceRevision < 0 || preparation.CanonicalWorkspaceRoot != binding.CanonicalRoot)
                    throw new UnauthorizedAccessException("A SAME genuine command/tests preparation for the exact saved root is required.");
                _bindingSource = _sources.Invoke(owner._bindings) ?? throw new InvalidOperationException("DEV_EXECUTION_SETUP_REQUIRED: genuine Files/saved workspace execution binding source is unavailable.");
                _toolSource = _sources.Invoke(owner._tools) ?? throw new InvalidOperationException("The genuine configured canonical tool owner is unavailable.");
                if (!_sources.Invoke(() => _bindingSource.IsIssuedOriginalBinding(binding)))
                    throw new UnauthorizedAccessException("Public workspace/project/root facts cannot mint execution trust.");
                if (_bindingSource is not IDeveloperWorkspaceOriginalExecutionScopedBindingSource scopedBindings ||
                    _bindingSource is not IDeveloperWorkspaceOriginalExecutionCommitBindingSource commitSource ||
                    _bindingSource is not IDeveloperWorkspaceOriginalExecutionPinCustodySource pinCustody)
                    throw new InvalidOperationException("DEV_EXECUTION_NATIVE_PIN_SETUP_REQUIRED: SAME scoped saved-root issuer and historical pin custody are required.");
                _scopedBindingSource = scopedBindings; _commitSource = commitSource; _pinCustody = pinCustody;
                await _sources.AwaitAsync(_sources.Invoke(() => _toolSource.ValidateOriginalPreparationAsync(preparation, current, token))).ConfigureAwait(false);
                _scopes = _sources.Invoke(() => _bindingSource.GetOriginalExecutionScopes(binding)).ToArray();
                if (_scopes.Length != 1 || _scopes[0].Kind != "dev.workspace.execute" || _scopes[0].Access != ResourceAccess.Execute
                    || string.IsNullOrWhiteSpace(_scopes[0].Id) || string.IsNullOrWhiteSpace(_scopes[0].Revision))
                    throw new UnauthorizedAccessException("One genuine privately issued saved-root Execute scope is required.");
                _stop = _sources.Invoke(() => CancellationTokenSource.CreateLinkedTokenSource(token));
                await ValidateSourcesAsync(_sources, _stop.Token).ConfigureAwait(false);
                var args = JsonSerializer.SerializeToElement(new { binding.WorkspaceId, binding.ProjectId, binding.RootId, binding.WorkspaceRevision,
                    binding.CanonicalRoot, TaskId = preparation.OriginalAttempt.Snapshot.TaskId, preparation.OriginalAttempt.Snapshot.ContextId,
                    preparation.OriginalAttempt.Snapshot.ExecutionId, preparation.OriginalAttempt.AttemptId, preparation.ActionId,
                    Tool = preparation.OriginalCall.Name, originalCallSha256 = WorkspaceToolOriginalDigest.Call(preparation.OriginalCall),
                    scope = "One finite process start only; setup/READ does not trust repository code; Task/model/tool/action permission remains separately required." });
                _review = _sources.Invoke(() => owner._broker.PrepareReviewForActor(binding.OriginalActor, "dev", ExecuteAction, _scopes, args,
                    "Execute only this reviewed command or project script at the current owned saved project root. Process exit remains a separate observation.",
                    null, "dev:execute:" + binding.OriginalActor.AuthenticationRevision));
                var observed = await _sources.AwaitAsync(_sources.Invoke(() => owner._broker.AuthorizePreparedReviewAsync(_review, _stop.Token))).ConfigureAwait(false);
                var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
                while (observed.Request?.State == HomePermissionRequestState.PendingApproval)
                {
                    if (DateTimeOffset.UtcNow >= deadline) throw new UnauthorizedAccessException("DEV_EXECUTION_APPROVAL_REQUIRED: explicitly accept this genuine project command review in Home.");
                    await _sources.AwaitAsync(Task.Delay(TimeSpan.FromSeconds(1), _stop.Token)).ConfigureAwait(false);
                    await ValidateSourcesAsync(_sources, _stop.Token).ConfigureAwait(false);
                    observed = await _sources.AwaitAsync(_sources.Invoke(() => owner._broker.ObservePreparedReviewAsync(_review, _stop.Token))).ConfigureAwait(false);
                }
                if (observed.Request?.State != HomePermissionRequestState.Approved) throw new UnauthorizedAccessException("Original project execution was not explicitly approved.");
                await ValidateSourcesAsync(_sources, _stop.Token).ConfigureAwait(false);
                _capability = await _sources.AwaitAsync(_sources.Invoke(() => owner._broker.BeginExecutionCapabilityAsync(_review.RequestId, args, _stop.Token))).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("The actual reviewed execution capability is unavailable.");
                var claimed = await _sources.AwaitAsync(_sources.Invoke(() => owner._broker.ClaimExecutionObservedAsync(_capability, "dev", ExecuteAction, _scopes, args, _stop.Token))).ConfigureAwait(false);
                if (claimed.Disposition != HomeResourceClaimDisposition.Claimed || claimed.Actor != binding.OriginalActor)
                    throw new UnauthorizedAccessException("The genuine original Home execution claim was refused.");
                _attestation = _sources.Invoke(() => owner._broker.CaptureClaimedAttestation(_capability))
                    ?? throw new UnauthorizedAccessException("The source-issued Home execution attestation is unavailable.");
                DemandLive(); return this;
            }).ConfigureAwait(false);
        }
        internal Task ValidateAsync(CancellationToken token)
        {
            DemandLive(); Task actual; CloudflareOriginalTaskLedger sources; TaskCompletionSource begin;
            lock (_gate)
            {
                if (_retiring || !Acquisition.IsCompletedSuccessfully || _capability is null || _attestation is null)
                    throw new UnauthorizedAccessException("The original execution approval must complete before validation.");
                if (_validations.Count >= 128) throw new InvalidOperationException("Original consent validation custody is full.");
                begin = new(TaskCreationOptions.RunContinuationsAsynchronously); sources = NewSources(this);
                actual = ValidatePublishedAsync(begin.Task, sources, token); _validations.Add((actual, sources));
            }
            begin.SetResult(); return actual;
        }
        private async Task ValidatePublishedAsync(Task begin, CloudflareOriginalTaskLedger sources, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            await sources.RunToOriginalSettlementAsync(async () =>
            {
                await ValidateSourcesAsync(sources, token).ConfigureAwait(false);
                if (!_capability!.IsUncompletedClaim(owner._broker)) throw new UnauthorizedAccessException("The original Home execution claim completed or retired.");
            }).ConfigureAwait(false);
        }
        public ValueTask DisposeAsync()
        {
            DemandExternalJoin(); Task actual; TaskCompletionSource begin;
            lock (_gate)
            {
                if (_close is not null) return new(_close); _retiring = true; begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _closing.BindOriginalOwner(this); _closing.BindOriginalCallerCallback(_guardedCaller); actual = ClosePublishedAsync(begin.Task); _close = actual;
            }
            begin.SetResult(); return new(actual);
        }
        private async Task ClosePublishedAsync(Task begin)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            try { _closing.Invoke(() => { _stop?.Cancel(); return true; }); } catch (Exception cause) { _closing.Retain(cause); }
            try { await _closing.AwaitAsync(Acquisition).ConfigureAwait(false); } catch (Exception cause) { _closing.Retain(cause); }
            Task<IWorkspaceOriginalProcessStartEntry>? acquisition; lock (_gate) acquisition = _entryAcquisition;
            if (acquisition is not null) try { await _closing.AwaitAsync(acquisition).ConfigureAwait(false); } catch (Exception cause) { _closing.Retain(cause); }
            // Release held Home BEFORE validators which may be waiting on its store gate.
            Task? entryClose = null;
            if (_entry is { } entry) try { _ = _closing.Invoke(() => { entryClose = entry.DisposeAsync().AsTask(); return entryClose; }); } catch (Exception cause) { _closing.Retain(cause); }
            (Task Driver, CloudflareOriginalTaskLedger Sources)[] validations; lock (_gate) validations = _validations.ToArray();
            foreach (var original in validations)
            {
                try { await _closing.AwaitAsync(original.Driver).ConfigureAwait(false); } catch (Exception cause) { _closing.Retain(cause); }
                await original.Sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
                foreach (var cause in original.Sources.OriginalErrors) _closing.Retain(cause);
            }
            if (entryClose is not null) try { await _closing.AwaitAsync(entryClose).ConfigureAwait(false); } catch (Exception cause) { _closing.Retain(cause); }
            await _sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false); foreach (var cause in _sources.OriginalErrors) _closing.Retain(cause);
            if (_capability is not null)
            {
                try
                {
                    var state = _entry is { OriginalClose.IsCompletedSuccessfully: true, NativeStartReturned: true } && _closing.OriginalErrors.Count == 0
                        ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.PartiallyCompleted;
                    var audit = await _closing.AwaitAsync(_closing.Invoke(() => owner._broker.CompleteExecutionAsync(_capability,
                        new(state, state == HomePermissionRequestState.Succeeded ? "DEV_ORIGINAL_PROCESS_START_ACKNOWLEDGED" : "DEV_ORIGINAL_PROCESS_START_INCOMPLETE",
                            "Retained the reviewed finite native start and exact cleanup. This does not certify process exit, build/test success or permission for another start.", []), CancellationToken.None))).ConfigureAwait(false);
                    if (!audit.Succeeded) throw new InvalidOperationException("The original execution audit is unfinished: " + audit.Code);
                }
                catch (Exception cause) { _closing.Retain(cause); }
            }
            try { _closing.Invoke(() => { _stop?.Dispose(); return true; }); } catch (Exception cause) { _closing.Retain(cause); }
            await _closing.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (_closing.OriginalErrors.Count != 0) throw new AggregateException("Original Home execution approval/start/cleanup/audit remains incomplete.", _closing.OriginalErrors);
        }
    }
}

public sealed class HomeDeveloperWorkspaceExecutionActionPolicySource : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) => appId == "dev" && actionId == HomeDeveloperWorkspaceExecutionConsentSource.ExecuteAction
        ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, RequiresPerActionApproval: true) : null;
}
