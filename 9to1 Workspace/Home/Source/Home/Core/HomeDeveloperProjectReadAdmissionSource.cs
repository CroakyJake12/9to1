using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
namespace HavenOS.Home.Core;

/// <summary>Actual initial READ admission over a SAME privately issued Files selection. This
/// does not authorize the later manifest setup effects. Physical root/capture settlement remains
/// owned by Files/kernel; no held Home store lease is carried across those source validations.</summary>
public sealed class HomeDeveloperProjectReadAdmissionSource : IDeveloperProjectOriginalReadAdmissionSource,
    IDeveloperProjectOriginalReadRetirementSource, ICanonicalResourceAccessResolver, IAsyncDisposable
{
    public const string ReadAction = "dev.project.source.read";
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeResourceOperationBroker _broker;
    private readonly HomePermissionTrustService _permissions;
    private readonly Func<IDeveloperProjectOriginalPhysicalReadSelectionSource> _selections;
    private readonly object _sync = new();
    private readonly Dictionary<IDeveloperProjectOriginalReadSelection, Read> _reads = new(ReferenceEqualityComparer.Instance);
    private volatile bool _sealed; private Task? _close; private readonly CloudflareOriginalTaskLedger _closeStages = new();
    public HomeDeveloperProjectReadAdmissionSource(FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceOperationBroker broker, HomePermissionTrustService permissions,
        Func<IDeveloperProjectOriginalPhysicalReadSelectionSource> selections)
    {
        if (!profiles.IsBoundToStore(store) || !permissions.IsBoundToStore(store) || !broker.IsBoundToPermissions(permissions))
            throw new InvalidOperationException("SAME actual Home store/profile/broker/policy required.");
        _profiles = profiles; _broker = broker; _permissions = permissions; _selections = selections;
    }
    public string ResourceKind => "dev.project.source";
    public void DemandExternalOriginalReadAdmissionJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        Read[] originals; lock (_sync) originals = _reads.Values.ToArray();
        foreach (var original in originals) original.DemandExternalJoin();
    }
    public void RequestOriginalReadRetirement()
    {
        Read[] originals; lock (_sync) { _sealed = true; originals = _reads.Values.ToArray(); }
        foreach (var original in originals) original.RequestRetirement();
    }
    public Task CloseAndDrainOriginalReadsAsync()
    {
        DemandExternalOriginalReadAdmissionJoin();
        lock (_sync)
        {
            if (_close is not null) return _close; _sealed = true; var originals = _reads.Values.ToArray();
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _closeStages.BindOriginalOwner(this);
            _close = ClosePublishedAsync(begin.Task, originals); begin.SetResult(); return _close;
        }
    }
    private async Task ClosePublishedAsync(Task begin, Read[] originals)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var errors = new List<Exception>(); foreach (var original in originals) original.RequestRetirement();
        foreach (var original in originals)
            try { await _closeStages.AwaitAsync(_closeStages.Invoke(() => original.DisposeAsync())).ConfigureAwait(false); }
            catch (Exception cause) { errors.Add(cause); }
        if (errors.Count != 0) throw new AggregateException("Actual Home project READ originals failed to drain.", errors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalReadsAsync());
    public Task<IDeveloperProjectOriginalReadAdmission> AcquireOriginalAsync(IDeveloperProjectOriginalReadSelection selection, CancellationToken token)
    {
        DemandExternalOriginalReadAdmissionJoin();
        lock (_sync)
        {
            if (_sealed) throw new ObjectDisposedException(nameof(HomeDeveloperProjectReadAdmissionSource));
            if (_reads.TryGetValue(selection, out var original)) return original.OriginalAcquisition;
            if (_reads.Count >= 128) throw new InvalidOperationException("Unresolved original READ custody is full.");
            var read = new Read(this, selection); _reads.Add(selection, read); return read.Start(token);
        }
    }
    public Task ValidateOriginalAsync(IDeveloperProjectOriginalReadSelection selection, IDeveloperProjectOriginalReadAdmission admission, CancellationToken token)
    {
        Read original;
        lock (_sync)
            original = admission is Read actual && _reads.TryGetValue(selection, out var issued) && ReferenceEquals(actual, issued)
                ? actual : throw new UnauthorizedAccessException("SAME original Home READ issuer/selection required.");
        return original.RevalidateOriginalAsync(token);
    }
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken token)
    {
        lock (_sync)
        {
            var original = action == ReadAction && !_sealed
                ? _reads.Values.SingleOrDefault(x => x.HasOriginalScope(actor, scope)) : null;
            return original is null
                ? ValueTask.FromResult(new ResourceAccessDecision(false, "DEV_ORIGINAL_READ_SCOPE_REQUIRED", actor.ActorId, scope.Revision, actor.OrganisationId))
                : new(original.StartOriginalEvaluation(actor, scope, token));
        }
    }
    private sealed class Read(HomeDeveloperProjectReadAdmissionSource owner,
        IDeveloperProjectOriginalReadSelection selection)
        : IDeveloperProjectOriginalReadAdmission
    {
        private readonly object _gate = new(); private readonly CloudflareOriginalTaskLedger _stages = new();
        private readonly CloudflareOriginalTaskLedger _closingStages = new();
        private Task<IDeveloperProjectOriginalReadAdmission> _acquisition = null!;
        private readonly List<Task> _validations = []; private readonly List<Task> _evaluations = []; private CancellationTokenSource? _stop; private bool _sealed;
        private AuthenticatedResourceActor? _actor; private ResourceScope[] _scopes = [];
        private HomeResourcePreparedReview? _review; private HomeResourceExecutionCapability? _capability;
        private HomeClaimedResourceAttestation? _attestation; private Task? _close;
        private int _finiteCalls; private readonly List<Task> _finiteAdmissions = [];
        private IDeveloperProjectOriginalPhysicalReadSelectionSource _source = null!;
        internal Task<IDeveloperProjectOriginalReadAdmission> OriginalAcquisition => _acquisition;
        internal void DemandExternalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        internal void RequestRetirement() { lock (_gate) _sealed = true; }
        private void DemandLive()
        {
            lock (owner._sync) lock (_gate) if (owner._sealed || _sealed) throw new ObjectDisposedException("Original project READ admission");
        }
        internal bool HasOriginalScope(AuthenticatedResourceActor actor, ResourceScope scope)
        { lock (_gate) return !_sealed && _actor == actor && _scopes.Any(actual => actual == scope); }
        internal Task<ResourceAccessDecision> StartOriginalEvaluation(AuthenticatedResourceActor actor, ResourceScope scope, CancellationToken token)
        {
            // Owner gate is held by the public resolver during this callback-free publication.
            lock (_gate)
            {
                if (_sealed || owner._sealed) return Task.FromResult(new ResourceAccessDecision(false, "DEV_ORIGINAL_READ_RETIRED", actor.ActorId, scope.Revision, actor.OrganisationId));
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var original = EvaluatePublishedAsync(begin.Task, actor, scope, token); _evaluations.Add(original); begin.SetResult(); return original;
            }
        }
        private async Task<ResourceAccessDecision> EvaluatePublishedAsync(Task begin, AuthenticatedResourceActor actor, ResourceScope scope, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            await ValidateSourceAsync(actor, token).ConfigureAwait(false);
            var current = await ActorAsync(token).ConfigureAwait(false);
            return new(!owner._sealed && current == actor && HasOriginalScope(actor, scope), "DEV_ORIGINAL_SOURCE_READ", actor.ActorId, scope.Revision, actor.OrganisationId);
        }
        internal Task<IDeveloperProjectOriginalReadAdmission> Start(CancellationToken token)
        {
            _stages.BindOriginalOwner(this);
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _acquisition = AcquirePublishedAsync(begin.Task, token); begin.SetResult(); return _acquisition;
        }
        internal async Task<AuthenticatedResourceActor> ActorAsync(CancellationToken token) =>
            await _stages.AwaitAsync(_stages.Invoke(() => owner._profiles.GetCurrentAsync(token))).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Open the genuine installed DeviceLocal Home profile before reading project source.");
        internal async Task ValidateSourceAsync(AuthenticatedResourceActor actor, CancellationToken token)
        {
            DemandLive();
            if (!_stages.Invoke(() => ReferenceEquals(owner._selections(), _source) && _source.IsIssuedOriginal(selection))) throw new UnauthorizedAccessException("The original configured selection issuer retired.");
            await _stages.AwaitAsync(_stages.Invoke(() => _source.RevalidateOriginalAsync(selection, actor, token))).ConfigureAwait(false);
            if (!_stages.Invoke(() => _source.IsIssuedOriginal(selection))) throw new UnauthorizedAccessException("The actual selected root retired during source validation.");
            DemandLive();
        }
        private async Task<IDeveloperProjectOriginalReadAdmission> AcquirePublishedAsync(Task begin, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            return await _stages.RunToOriginalSettlementAsync(async () =>
            {
                DemandLive();
                _source = _stages.Invoke(() => owner._selections()) ?? throw new InvalidOperationException("Configured original Files selection issuer is unavailable.");
                if (!_stages.Invoke(() => _source.IsIssuedOriginal(selection))) throw new UnauthorizedAccessException("A path/descriptor cannot replace the SAME configured original Files selection.");
                var stop = _stages.Invoke(() => CancellationTokenSource.CreateLinkedTokenSource(token));
                lock (_gate) { _stop = stop; if (_sealed || owner._sealed) throw new ObjectDisposedException("Original project READ admission"); }
                _actor = await ActorAsync(_stop.Token).ConfigureAwait(false); await ValidateSourceAsync(_actor, _stop.Token).ConfigureAwait(false);
                var scopes = _stages.Invoke(() => _source.GetOriginalReadScopes(selection)).ToArray();
                if (scopes.Length != 1 || scopes[0].Kind != owner.ResourceKind || scopes[0].Access != ResourceAccess.Read ||
                    string.IsNullOrWhiteSpace(scopes[0].Id) || string.IsNullOrWhiteSpace(scopes[0].Revision)) throw new UnauthorizedAccessException("The configured selection must issue one distinct exact original source READ scope.");
                lock (_gate) _scopes = scopes;
                var arguments = JsonSerializer.SerializeToElement(new { sourceSelection = scopes[0].Id, sourceRevision = scopes[0].Revision,
                    limits = "64 files/64 folders; 4 MiB per file; 16 MiB total; no copy or mutation" });
                _review = _stages.Invoke(() => owner._broker.PrepareReviewForActor(_actor, "dev", ReadAction, scopes, arguments,
                    "Read the explicitly selected original project beneath your verified Files root. The later project setup needs its own review.", null,
                    "dev:source:" + _actor.AuthenticationRevision));
                var observed = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.AuthorizePreparedReviewAsync(_review, _stop.Token))).ConfigureAwait(false);
                var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
                while (observed.State == HomePreparedReviewState.RequestObserved && observed.Request?.State == HomePermissionRequestState.PendingApproval)
                {
                    if (DateTimeOffset.UtcNow >= deadline) throw new UnauthorizedAccessException("DEV_SOURCE_READ_APPROVAL_REQUIRED: Accept the exact selected project READ in Home; no manifest was read.");
                    await _stages.AwaitAsync(Task.Delay(TimeSpan.FromSeconds(1), _stop.Token)).ConfigureAwait(false);
                    await ValidateSourceAsync(_actor, _stop.Token).ConfigureAwait(false);
                    observed = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.ObservePreparedReviewAsync(_review, _stop.Token))).ConfigureAwait(false);
                }
                if (observed.Request?.State != HomePermissionRequestState.Approved) throw new UnauthorizedAccessException("The original selected-project READ was not accepted.");
                await ValidateSourceAsync(_actor, _stop.Token).ConfigureAwait(false);
                if (await ActorAsync(_stop.Token).ConfigureAwait(false) != _actor) throw new UnauthorizedAccessException("The original Home actor changed before READ admission.");
                _capability = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.BeginExecutionCapabilityAsync(_review.RequestId, arguments, _stop.Token))).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("Original Home READ capability unavailable.");
                var claim = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.ClaimExecutionObservedAsync(_capability, "dev", ReadAction, scopes, arguments, _stop.Token))).ConfigureAwait(false);
                if (claim.Disposition != HomeResourceClaimDisposition.Claimed || claim.Actor != _actor) throw new UnauthorizedAccessException("Original Home READ claim rejected.");
                _attestation = owner._broker.CaptureClaimedAttestation(_capability) ?? throw new UnauthorizedAccessException("Individual original Home READ Accept is required.");
                DemandLive(); return this;
            }).ConfigureAwait(false);
        }
        public Task RevalidateOriginalAsync(CancellationToken token)
        {
            DemandLive(); lock (_gate)
            {
                if (_sealed || owner._sealed || _acquisition.IsCompletedSuccessfully != true || _capability is null || _attestation is null) throw new UnauthorizedAccessException("The original READ claim is not admitted.");
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var task = RevalidatePublishedAsync(begin.Task, token);
                _validations.Add(task); begin.SetResult(); return task;
            }
        }
        private async Task RevalidatePublishedAsync(Task begin, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            await ValidateSourceAsync(_actor!, token).ConfigureAwait(false);
            if (await ActorAsync(token).ConfigureAwait(false) != _actor) throw new UnauthorizedAccessException("The actual project READ actor changed.");
            var current = await _stages.AwaitAsync(_stages.Invoke(() => owner._permissions.IsExecutionCurrentAsync(_capability!.RequestId, token))).ConfigureAwait(false);
            if (!current || await ActorAsync(token).ConfigureAwait(false) != _actor) throw new UnauthorizedAccessException("The original project READ was revoked or its actor changed.");
            DemandLive();
        }
        public T RunOriginalRead<T>(Func<T> body, CancellationToken token)
        {
            DemandLive(); TaskCompletionSource admission;
            lock (_gate)
            {
                token.ThrowIfCancellationRequested(); _stop!.Token.ThrowIfCancellationRequested();
                if (_sealed || owner._sealed || _acquisition.IsCompletedSuccessfully != true || _validations.Count == 0 || _validations.Any(x => !x.IsCompletedSuccessfully) ||
                    _capability is null || !_capability.IsUncompletedClaim(owner._broker)) throw new UnauthorizedAccessException("Fresh SAME original READ validation is required before the finite kernel source start.");
                if (++_finiteCalls > 20_000) throw new InvalidOperationException("The bounded original source READ call cohort is full.");
                admission = new(TaskCreationOptions.RunContinuationsAsynchronously); _finiteAdmissions.Add(admission.Task);
            }
            try { return _stages.Invoke(body); } // Callback outside locks; exact raw child enrolled before scope exit.
            finally { admission.TrySetResult(); } // Source close first joins every admitted finite callback, then its actual raw children.
        }
        public ValueTask DisposeAsync()
        {
            DemandExternalJoin(); lock (_gate)
            {
                if (_close is not null) return new(_close); _sealed = true;
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _closingStages.BindOriginalOwner(this);
                _close = ClosePublishedAsync(begin.Task); begin.SetResult(); return new(_close);
            }
        }
        private async Task ClosePublishedAsync(Task begin)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this); var errors = new List<Exception>();
            try { _closingStages.Invoke(() => { _stop?.Cancel(); return true; }); } catch (Exception cause) { errors.Add(cause); }
            Task[] drivers; lock (_gate) drivers = new Task[] { _acquisition }.Concat(_validations).Concat(_evaluations).Concat(_finiteAdmissions).ToArray();
            // The acquisition settles its own raw ledger. Enroll encompassing drivers in this
            // separate close ledger, so acquisition never observes/joins its own parent Task.
            foreach (var original in drivers) try { await _closingStages.AwaitAsync(original).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            await _stages.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            foreach (var cause in _stages.OriginalErrors) if (!errors.Any(x => ReferenceEquals(x, cause))) errors.Add(cause);
            if (_capability is not null)
            {
                try
                {
                    var audit = await _closingStages.AwaitAsync(_closingStages.Invoke(() => owner._broker.CompleteExecutionAsync(_capability,
                        new(errors.Count == 0 ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.Failed,
                            errors.Count == 0 ? "DEV_ORIGINAL_SOURCE_READ_CLOSED" : "DEV_ORIGINAL_SOURCE_READ_UNFINISHED",
                            "The actual admitted local READ task cohort was joined; project setup and physical capture remain separate originals.", []), CancellationToken.None))).ConfigureAwait(false);
                    if (!audit.Succeeded) throw new InvalidOperationException("Original project READ audit unfinished: " + audit.Code);
                }
                catch (Exception cause) { errors.Add(cause); }
            }
            try { _closingStages.Invoke(() => { _stop?.Dispose(); return true; }); } catch (Exception cause) { errors.Add(cause); }
            await _closingStages.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            foreach (var cause in _closingStages.OriginalErrors) if (!errors.Any(x => ReferenceEquals(x, cause))) errors.Add(cause);
            if (errors.Count != 0) throw new AggregateException("Original Home READ drivers/tasks/audit/cleanup failed.", errors);
        }
    }
}

public sealed class HomeDeveloperProjectReadActionPolicySource : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == "dev" && actionId == HomeDeveloperProjectReadAdmissionSource.ReadAction
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.Elevated, false, false, RequiresPerActionApproval: true) : null;
}

/// <summary>The actual Home composition receives this SAME lazy resolver before it builds
/// its resource service. Resolving the READ owner later avoids a Home/Files constructor cycle.</summary>
public sealed class HomeDeveloperProjectReadResourceResolver(Func<HomeDeveloperProjectReadAdmissionSource> source)
    : ICanonicalResourceAccessResolver
{
    public string ResourceKind => "dev.project.source";
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken token)
        => source().EvaluateAsync(actor, action, scope, token);
}
