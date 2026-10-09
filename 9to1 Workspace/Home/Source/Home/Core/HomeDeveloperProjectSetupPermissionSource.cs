using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>One explicit Home review of a SAME journal-issued immutable setup and
/// privately bound Files destination. Source READ, a path, account identity or a public
/// checkpoint cannot issue this permission. Individual Files steps remain separate originals.</summary>
public sealed partial class HomeDeveloperProjectSetupPermissionSource : IDeveloperProjectOriginalSetupPermissionSource, IAsyncDisposable
{
    public const string SetupAction = "dev.project.setup.commit";
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeResourceOperationBroker _broker;
    private readonly Func<IDeveloperProjectOriginalSetupScopeSource> _scopes;
    private readonly Func<IDeveloperProjectOriginalCaptureAuthority> _captures;
    private readonly Func<IDeveloperProjectOriginalSetupStepOutcomeSource> _outcomes;
    private readonly Func<IDeveloperProjectOriginalSetupCompletionSource>? _completions;
    private readonly object _gate = new();
    private readonly Dictionary<DeveloperProjectSetupIntent, Permission> _originals = new(ReferenceEqualityComparer.Instance);
    private bool _retiring;
    private Task? _close;
    private readonly CloudflareOriginalTaskLedger _closing = new();

    public HomeDeveloperProjectSetupPermissionSource(FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceOperationBroker broker, HomePermissionTrustService permissions,
        Func<IDeveloperProjectOriginalSetupScopeSource> scopes, Func<IDeveloperProjectOriginalCaptureAuthority> captures,
        Func<IDeveloperProjectOriginalSetupStepOutcomeSource> outcomes)
        : this(store, profiles, broker, permissions, scopes, captures, outcomes, null) { }

    public HomeDeveloperProjectSetupPermissionSource(FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceOperationBroker broker, HomePermissionTrustService permissions,
        Func<IDeveloperProjectOriginalSetupScopeSource> scopes, Func<IDeveloperProjectOriginalCaptureAuthority> captures,
        Func<IDeveloperProjectOriginalSetupStepOutcomeSource> outcomes,
        Func<IDeveloperProjectOriginalSetupCompletionSource>? completions)
    {
        ArgumentNullException.ThrowIfNull(scopes); ArgumentNullException.ThrowIfNull(captures); ArgumentNullException.ThrowIfNull(outcomes);
        if (!profiles.IsBoundToStore(store) || !permissions.IsBoundToStore(store) || !broker.IsBoundToPermissions(permissions))
            throw new UnauthorizedAccessException("SAME configured Home store/profile/resource broker/policy is required.");
        _store = store; _profiles = profiles; _broker = broker; _scopes = scopes; _captures = captures; _outcomes = outcomes; _completions = completions;
    }
    public Task<IDeveloperProjectOriginalSetupPermission> AcquireOriginalAsync(DeveloperProjectSetupIntent intent,
        IDeveloperProjectOriginalSourceCapture capture, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capture);
        DemandExternalOriginalSetupJoin();
        lock (_gate)
        {
            if (_retiring) throw new ObjectDisposedException(nameof(HomeDeveloperProjectSetupPermissionSource));
            if (_originals.TryGetValue(intent, out var original))
            {
                if (!ReferenceEquals(original.Capture, capture)) throw new UnauthorizedAccessException("Original setup capture cannot be replaced.");
                return original.Acquisition;
            }
            if (_originals.Count >= 128) throw new InvalidOperationException("Retained original setup custody is full.");
            var permission = new Permission(this, intent, capture); _originals.Add(intent, permission);
            return permission.Start(token);
        }
    }
    public Task ValidateOriginalAsync(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSetupPermission permission, CancellationToken token)
    {
        Permission actual;
        lock (_gate) actual = permission is Permission value && _originals.TryGetValue(intent, out var owned)
            && ReferenceEquals(value, owned) ? value : throw new UnauthorizedAccessException("SAME private setup permission/intent issuer is required.");
        return actual.ValidateCurrentAsync(token);
    }
    public void RequestOriginalSetupRetirement()
    {
        Permission[] originals; lock (_gate) { _retiring = true; originals = _originals.Values.ToArray(); }
        foreach (var original in originals) original.RequestRetirement();
    }
    public void DemandExternalOriginalSetupJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        Permission[] originals; lock (_gate) originals = _originals.Values.ToArray();
        foreach (var original in originals) original.DemandExternalJoin();
    }
    public Task CloseAndDrainOriginalSetupsAsync()
    {
        DemandExternalOriginalSetupJoin();
        Task actual; TaskCompletionSource begin; Permission[] originals;
        lock (_gate)
        {
            if (_close is not null) return _close;
            _retiring = true; originals = _originals.Values.ToArray(); begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _closing.BindOriginalOwner(this); actual = ClosePublishedAsync(begin.Task, originals); _close = actual;
        }
        begin.SetResult(); return actual;
    }
    private async Task ClosePublishedAsync(Task begin, Permission[] originals)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        foreach (var original in originals) original.RequestRetirement();
        foreach (var original in originals)
            try { await _closing.ObserveOriginalCloseAsync(original.DisposeAsync).ConfigureAwait(false); }
            catch (Exception cause) { _closing.Retain(cause); }
        await _closing.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (_closing.OriginalErrors.Count != 0) throw new AggregateException("Original setup owners did not drain cleanly.", _closing.OriginalErrors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalSetupsAsync());

    private sealed partial class Permission(HomeDeveloperProjectSetupPermissionSource owner,
        DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture) : IDeveloperProjectOriginalSetupPermission
    {
        private HomeDeveloperProjectSetupPermissionSource Owner => owner;
        private DeveloperProjectSetupIntent OriginalIntent => intent;
        internal IDeveloperProjectOriginalSourceCapture Capture => capture;
        internal Task<IDeveloperProjectOriginalSetupPermission> Acquisition = null!;
        private readonly object _gate = new();
        private readonly CloudflareOriginalTaskLedger _sources = new();
        private readonly CloudflareOriginalTaskLedger _closing = new();
        private readonly List<Task> _validations = [];
        private readonly List<Task> _stepCloseDrivers = [];
        private readonly Dictionary<DeveloperProjectSetupStep, Step> _steps = new(ReferenceEqualityComparer.Instance);
        private bool _retiring; private Task? _close;
        private CancellationTokenSource? _stop;
        private IDeveloperProjectOriginalSetupScopeSource _scopeSource = null!;
        private IDeveloperProjectOriginalCaptureAuthority _captureSource = null!;
        private IDeveloperProjectOriginalSetupStepOutcomeSource _outcomeSource = null!;
        private ResourceScope[] _scopes = [];
        private HomeResourcePreparedReview? _review;
        private HomeResourceExecutionCapability? _capability;
        private HomeClaimedResourceAttestation? _attestation;

        internal void RequestRetirement() { lock (_gate) _retiring = true; }
        internal void DemandExternalJoin()
        {
            CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
            Step[] steps; lock (_gate) steps = _steps.Values.ToArray();
            foreach (var step in steps) step.DemandExternalJoin();
            _completionSource?.DemandExternalOriginalSetupCompletionJoin();
        }
        private void DemandLive()
        {
            lock (owner._gate) lock (_gate)
                if (owner._retiring || _retiring) throw new ObjectDisposedException("Original high-risk setup permission");
        }
        private void DemandDeclaredStep(DeveloperProjectSetupStep sameStep)
        {
            if (!intent.Steps.Any(original => ReferenceEquals(original, sameStep)))
                throw new UnauthorizedAccessException("The SAME original declared step object is required; copied step IDs cannot issue an entry.");
        }
        internal Task<IDeveloperProjectOriginalSetupPermission> Start(CancellationToken token)
        {
            _sources.BindOriginalOwner(this); var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Acquisition = AcquirePublishedAsync(begin.Task, token); begin.SetResult(); return Acquisition;
        }
        private Task<AuthenticatedResourceActor> ActorAsync(CancellationToken token) => ActorPublishedAsync(token);
        private async Task<AuthenticatedResourceActor> ActorPublishedAsync(CancellationToken token) =>
            await _sources.AwaitAsync(_sources.Invoke(() => owner._profiles.GetCurrentAsync(token))).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("A genuine installed DeviceLocal Home actor is required for project setup.");
        private async Task ValidateSourcesAsync(CancellationToken token)
        {
            DemandLive();
            if (!_sources.Invoke(() => ReferenceEquals(owner._scopes(), _scopeSource) && ReferenceEquals(owner._captures(), _captureSource)
                && ReferenceEquals(owner._outcomes(), _outcomeSource) && _scopeSource.IsIssuedOriginalSetupBinding(intent, capture)
                && _captureSource.IsIssuedOriginal(capture)))
                throw new UnauthorizedAccessException("The actual configured destination/capture/outcome issuers or their private binding changed.");
            await _sources.AwaitAsync(_sources.Invoke(() => _captureSource.RevalidateOriginalAsync(capture, intent.OriginalActor, token))).ConfigureAwait(false);
            await _sources.AwaitAsync(_sources.Invoke(() => _scopeSource.RevalidateOriginalSetupAsync(intent, capture, intent.OriginalActor, token))).ConfigureAwait(false);
            if (await ActorAsync(token).ConfigureAwait(false) != intent.OriginalActor)
                throw new UnauthorizedAccessException("The real Home actor changed after original source/destination reads.");
            if (!_sources.Invoke(() => _scopeSource.IsIssuedOriginalSetupBinding(intent, capture)
                && _captureSource.IsIssuedOriginal(capture) && _scopeSource.GetOriginalSetupScopes(intent, capture).SequenceEqual(_scopes)))
                throw new UnauthorizedAccessException("The original private destination scope changed during validation.");
            DemandLive();
        }
        private async Task<IDeveloperProjectOriginalSetupPermission> AcquirePublishedAsync(Task begin, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            return await _sources.RunToOriginalSettlementAsync(async () =>
            {
                DemandLive(); if (intent.Validate() is { } invalid) throw new ArgumentException(invalid);
                _scopeSource = _sources.Invoke(owner._scopes) ?? throw new InvalidOperationException("The actual journal-bound Files destination source is unavailable.");
                _captureSource = _sources.Invoke(owner._captures) ?? throw new InvalidOperationException("The actual physical capture authority is unavailable.");
                _outcomeSource = _sources.Invoke(owner._outcomes) ?? throw new InvalidOperationException("The actual Files step outcome issuer is unavailable.");
                _completionSource = owner._completions is null ? null : _sources.Invoke(owner._completions);
                if (!_sources.Invoke(() => _scopeSource.IsIssuedOriginalSetupBinding(intent, capture) && _captureSource.IsIssuedOriginal(capture)))
                    throw new UnauthorizedAccessException("No genuine SAME journal/destination/source setup binding exists.");
                _stop = _sources.Invoke(() => CancellationTokenSource.CreateLinkedTokenSource(token));
                _scopes = _sources.Invoke(() => _scopeSource.GetOriginalSetupScopes(intent, capture)).ToArray();
                if (_scopes.Length != 1 || _scopes[0].Kind != "dev.project.destination" || _scopes[0].Access != ResourceAccess.Write
                    || string.IsNullOrWhiteSpace(_scopes[0].Id) || string.IsNullOrWhiteSpace(_scopes[0].Revision))
                    throw new UnauthorizedAccessException("One exact privately issued destination Write scope is required.");
                await ValidateSourcesAsync(_stop.Token).ConfigureAwait(false);
                var args = JsonSerializer.SerializeToElement(new { intent, digest = intent.Digest(),
                    originalEffectLimits = "512 distinct once-issued steps; no command trust, model permission, copy replay or source READ grant" });
                _review = _sources.Invoke(() => owner._broker.PrepareReviewForActor(intent.OriginalActor, "dev", SetupAction, _scopes, args,
                    "Commit only the reviewed original project setup steps at this owned Files destination. Partial or unknown work retains the same IDs.",
                    null, "dev:setup:" + intent.OriginalActor.AuthenticationRevision));
                var observed = await _sources.AwaitAsync(_sources.Invoke(() => owner._broker.AuthorizePreparedReviewAsync(_review, _stop.Token))).ConfigureAwait(false);
                var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
                while (observed.Request?.State == HomePermissionRequestState.PendingApproval)
                {
                    if (DateTimeOffset.UtcNow >= deadline) throw new UnauthorizedAccessException("DEV_SETUP_APPROVAL_REQUIRED: accept the original high-risk intent in Home before any step.");
                    await _sources.AwaitAsync(Task.Delay(TimeSpan.FromSeconds(1), _stop.Token)).ConfigureAwait(false);
                    await ValidateSourcesAsync(_stop.Token).ConfigureAwait(false);
                    observed = await _sources.AwaitAsync(_sources.Invoke(() => owner._broker.ObservePreparedReviewAsync(_review, _stop.Token))).ConfigureAwait(false);
                }
                if (observed.Request?.State != HomePermissionRequestState.Approved) throw new UnauthorizedAccessException("The original setup intent was not explicitly accepted.");
                await ValidateSourcesAsync(_stop.Token).ConfigureAwait(false);
                _capability = await _sources.AwaitAsync(_sources.Invoke(() => owner._broker.BeginExecutionCapabilityAsync(_review.RequestId, args, _stop.Token))).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("The original accepted setup capability is unavailable.");
                var claimed = await _sources.AwaitAsync(_sources.Invoke(() => owner._broker.ClaimExecutionObservedAsync(_capability, "dev", SetupAction, _scopes, args, _stop.Token))).ConfigureAwait(false);
                if (claimed.Disposition != HomeResourceClaimDisposition.Claimed || claimed.Actor != intent.OriginalActor)
                    throw new UnauthorizedAccessException("The original Home setup claim was rejected.");
                _attestation = _sources.Invoke(() => owner._broker.CaptureClaimedAttestation(_capability))
                    ?? throw new UnauthorizedAccessException("The genuine per-action Home setup review attestation is required.");
                DemandLive(); return this;
            }).ConfigureAwait(false);
        }
        internal Task ValidateCurrentAsync(CancellationToken token)
        {
            DemandLive(); Task actual; TaskCompletionSource begin;
            lock (_gate)
            {
                if (_retiring || !Acquisition.IsCompletedSuccessfully || _capability is null || _attestation is null)
                    throw new UnauthorizedAccessException("The SAME original setup acquisition must succeed before validation.");
                begin = new(TaskCreationOptions.RunContinuationsAsynchronously); actual = ValidatePublishedAsync(begin.Task, token); _validations.Add(actual);
            }
            begin.SetResult(); return actual;
        }
        private async Task ValidatePublishedAsync(Task begin, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            await ValidateSourcesAsync(token).ConfigureAwait(false);
            if (!_capability!.IsUncompletedClaim(owner._broker)) throw new UnauthorizedAccessException("The original setup claim is completed or closing.");
        }
        public Task<IDeveloperProjectOriginalSetupStepEntry> EnterOriginalStepAsync(DeveloperProjectSetupStep sameStep, CancellationToken token)
        {
            DemandLive(); DemandDeclaredStep(sameStep); Step original;
            lock (_gate)
            {
                if (_retiring || !Acquisition.IsCompletedSuccessfully || _capability is null || _attestation is null)
                    throw new UnauthorizedAccessException("The genuine complete-intent setup review must be admitted first.");
                if (_steps.TryGetValue(sameStep, out original!)) return original.Acquisition;
                original = new Step(this, sameStep); _steps.Add(sameStep, original);
                return original.Start(token);
            }
        }
        public bool IsIssuedOriginalStepEntry(DeveloperProjectSetupStep sameStep, IDeveloperProjectOriginalSetupStepEntry sameEntry)
        {
            lock (_gate) return !_retiring && _steps.TryGetValue(sameStep, out var step) && step.Acquisition.IsCompletedSuccessfully
                && ReferenceEquals(step.Entry, sameEntry) && step.Entry is { IsClosing: false };
        }
        public Task ValidateOriginalStepResultAsync(DeveloperProjectSetupStep sameStep, IDeveloperProjectOriginalSetupStepEntry sameEntry,
            Task sameActualStepTask, object? sameActualResult, CancellationToken token)
        {
            DemandLive(); Step step;
            lock (_gate) step = _steps.TryGetValue(sameStep, out var actual) && ReferenceEquals(actual.Entry, sameEntry)
                ? actual : throw new UnauthorizedAccessException("SAME privately issued step/entry is required.");
            return step.ValidateResultAsync(sameActualStepTask, sameActualResult, token);
        }
        public ValueTask DisposeAsync()
        {
            DemandExternalJoin(); Task actual; TaskCompletionSource begin;
            lock (_gate)
            {
                if (_close is not null) return new(_close); _retiring = true;
                begin = new(TaskCreationOptions.RunContinuationsAsynchronously); _closing.BindOriginalOwner(this);
                actual = ClosePublishedAsync(begin.Task); _close = actual;
            }
            begin.SetResult(); return new(actual);
        }
        private async Task ClosePublishedAsync(Task begin)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            try { _closing.Invoke(() => { _stop?.Cancel(); return true; }); } catch (Exception cause) { _closing.Retain(cause); }
            Task[] validations; Step[] steps; lock (_gate) { validations = _validations.ToArray(); steps = _steps.Values.ToArray(); }
            // A public validation may already be waiting on the store gate held by
            // another admitted step entry. Start ALL independent step/entry closes
            // before joining validation; retirement seals their fresh admissions.
            foreach (var step in steps) step.RequestRetirement();
            foreach (var step in steps)
            {
                try
                {
                    var actualClose = _closing.ObserveOriginalCloseAsync(step.CloseAsync);
                    lock (_gate) _stepCloseDrivers.Add(actualClose);
                }
                catch (Exception cause) { _closing.Retain(cause); }
            }
            foreach (var original in new Task[] { Acquisition }.Concat(validations))
                try { await _closing.AwaitAsync(original).ConfigureAwait(false); } catch (Exception cause) { _closing.Retain(cause); }
            Task[] stepCloses; lock (_gate) stepCloses = _stepCloseDrivers.ToArray();
            foreach (var actualClose in stepCloses)
                try { await _closing.AwaitAsync(actualClose).ConfigureAwait(false); } catch (Exception cause) { _closing.Retain(cause); }
            await _sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            foreach (var cause in _sources.OriginalErrors) _closing.Retain(cause);
            if (_capability is not null)
            {
                bool acknowledged = false;
                if (_closing.OriginalErrors.Count == 0)
                    try { acknowledged = await _closing.AwaitAsync(StartOriginalFinalCompletion()).ConfigureAwait(false); }
                    catch (Exception cause) { _closing.Retain(cause); }
                foreach (var actual in _completionStages.OriginalTasks) _ = _closing.Track(actual);
                foreach (var cause in _completionStages.OriginalErrors) _closing.Retain(cause);
                if (!acknowledged)
                    _closing.Retain(new InvalidOperationException("DEV_SETUP_FINAL_JOURNAL_ACK_REQUIRED: retained step outcomes do not certify complete setup; no replay or new IDs."));
                try
                {
                    var audit = await _closing.AwaitAsync(_closing.Invoke(() => owner._broker.CompleteExecutionAsync(_capability,
                        new(acknowledged ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.PartiallyCompleted,
                            acknowledged ? "DEV_SETUP_ORIGINAL_ACKNOWLEDGED" : "DEV_SETUP_ORIGINAL_INCOMPLETE",
                            acknowledged ? "The same original declared setup steps, cleanup and private final journal acknowledgment completed. Command and business permissions remain separate."
                                : "Retained the exact original step/cleanup outcomes. Complete journal acknowledgment and remaining declared steps are separately required.", []), CancellationToken.None))).ConfigureAwait(false);
                    if (!audit.Succeeded) throw new InvalidOperationException("Known setup outcomes retained; Home terminal audit unfinished: " + audit.Code);
                }
                catch (Exception cause) { _closing.Retain(cause); }
            }
            try { _closing.Invoke(() => { _stop?.Dispose(); return true; }); } catch (Exception cause) { _closing.Retain(cause); }
            await _closing.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (_closing.OriginalErrors.Count != 0) throw new AggregateException("Original setup acquisition/steps/cleanup/audit remain incomplete.", _closing.OriginalErrors);
        }
    }
}

public sealed class HomeDeveloperProjectSetupActionPolicySource : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == "dev" && actionId == HomeDeveloperProjectSetupPermissionSource.SetupAction
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, RequiresPerActionApproval: true) : null;
}
