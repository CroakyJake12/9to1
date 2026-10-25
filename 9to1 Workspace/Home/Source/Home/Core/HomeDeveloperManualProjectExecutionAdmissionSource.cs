using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>Task-free manual console execution. Genuine registered Files issuance, explicit Home
/// high-risk per-action review and claim, then a separately held finite native-start entry.</summary>
public sealed partial class HomeDeveloperManualProjectExecutionAdmissionSource :
    IDeveloperProjectOriginalManualExecutionAdmissionSource, IWorkspaceManualProcessFinalFenceValidator,
    ICanonicalResourceAccessResolver, IAsyncDisposable
{
    public const string RunAction = "dev.project.console.run";
    public string ResourceKind => "dev.project.console";
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeResourceOperationBroker _broker;
    private readonly HomePermissionTrustService _permissions;
    private readonly Func<IDeveloperProjectOriginalManualExecutionSource> _files;
    private readonly Func<IWorkspaceOriginalInvocationSource> _physical;
    private readonly object _sync = new();
    private readonly List<Run> _runs = [];
    private readonly List<Task> _acquisitions = [];
    private readonly CloudflareOriginalTaskLedger _closing = new();
    private volatile bool _sealed; private Task? _close;
    public HomeDeveloperManualProjectExecutionAdmissionSource(FileHomeCoreStateStore store,
        HomeLocalProfileIdentity profiles, HomeResourceOperationBroker broker, HomePermissionTrustService permissions,
        Func<IDeveloperProjectOriginalManualExecutionSource> files, Func<IWorkspaceOriginalInvocationSource> physical)
    {
        if (!profiles.IsBoundToStore(store) || !permissions.IsBoundToStore(store) || !broker.IsBoundToPermissions(permissions))
            throw new InvalidOperationException("SAME actual Home store/profile/broker/policy required.");
        _store = store; _profiles = profiles; _broker = broker; _permissions = permissions; _files = files; _physical = physical;
    }
    public void DemandExternalOriginalManualExecutionJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        Run[] runs; lock (_sync) runs = _runs.ToArray();
        foreach (var run in runs) run.DemandExternalJoin();
    }
    public Task<IDeveloperProjectOriginalManualExecutionAdmission> AcquireOriginalManualExecutionAsync(
        IDeveloperProjectOriginalManualExecutionBinding binding, CancellationToken token) => AcquireManualProcessAsync(new ConsoleBinding(binding), token);
    private Task<IDeveloperProjectOriginalManualExecutionAdmission> AcquireManualProcessAsync(ProcessBinding binding, CancellationToken token)
    {
        DemandExternalOriginalManualExecutionJoin();
        lock (_sync)
        {
            if (_sealed) throw new ObjectDisposedException(nameof(HomeDeveloperManualProjectExecutionAdmissionSource));
            if (_runs.Any(x => ReferenceEquals(x.Binding.Original, binding.Original)))
                throw new UnauthorizedAccessException("A manual Run binding is single-use; inspect its original outcome before requesting a new reviewed operation.");
            if (_runs.Count >= 128) throw new InvalidOperationException("Manual process admission custody is full.");
            var run = new Run(this, binding); _runs.Add(run);
            var acquisition = run.Start(token);
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var observed = AcquireAndCloseFailureAsync(begin.Task, run, acquisition);
            _acquisitions.Add(observed); begin.SetResult(); return observed;
        }
    }
    private static async Task<IDeveloperProjectOriginalManualExecutionAdmission> AcquireAndCloseFailureAsync(Task begin, Run run,
        Task<IDeveloperProjectOriginalManualExecutionAdmission> acquisition)
    {
        await begin.ConfigureAwait(false);
        try { return await acquisition.ConfigureAwait(false); }
        catch (Exception primary)
        {
            // The original acquisition driver has settled. Its private late products and claim
            // can now close without joining this encompassing observer or waiting until host exit.
            try { await run.DisposeAsync().ConfigureAwait(false); }
            catch (Exception close) { throw new AggregateException("Manual Run admission and independent failed-acquisition cleanup failed.", primary, close); }
            throw;
        }
    }
    public bool IsIssuedOriginal(IWorkspaceToolFinalFence fence) => !_sealed && fence is HomeManualProjectProcessFence original &&
        ReferenceEquals(original.Owner, this) && !original.IsClosed;
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken token)
    {
        lock (_sync)
        {
            var run = !_sealed && action == RunAction ? _runs.SingleOrDefault(x => x.HasScope(actor, scope)) : null;
            return run is null ? ValueTask.FromResult(new ResourceAccessDecision(false, "DEV_MANUAL_RUN_SCOPE_REQUIRED", actor.ActorId, scope.Revision, actor.OrganisationId))
                : new(run.EvaluateAsync(actor, scope, token));
        }
    }
    public Task CloseAndDrainOriginalManualExecutionsAsync()
    {
        DemandExternalOriginalManualExecutionJoin();
        lock (_sync)
        {
            if (_close is not null) return _close; _sealed = true;
            _closing.BindOriginalOwner(this);
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseAsync(begin.Task, _runs.ToArray()); begin.SetResult(); return _close;
        }
    }
    private async Task CloseAsync(Task begin, Run[] runs)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        foreach (var run in runs) run.RequestRetirement();
        foreach (var run in runs)
            try { await _closing.ObserveOriginalCloseAsync(run.DisposeAsync).ConfigureAwait(false); }
            catch (Exception error) { _closing.Retain(error); }
        Task[] acquisitions; lock (_sync) acquisitions = _acquisitions.ToArray();
        foreach (var acquisition in acquisitions)
            try { await _closing.AwaitAsync(acquisition).ConfigureAwait(false); } catch (Exception error) { _closing.Retain(error); }
        await _closing.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (_closing.OriginalErrors.Count != 0) throw new AggregateException("Manual Home process admissions did not drain.", _closing.OriginalErrors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalManualExecutionsAsync());

    internal sealed class Run(HomeDeveloperManualProjectExecutionAdmissionSource owner,
        ProcessBinding binding) : IDeveloperProjectOriginalManualExecutionAdmission
    {
        private readonly object _gate = new();
        private readonly CloudflareOriginalTaskLedger _sources = new();
        private readonly CloudflareOriginalTaskLedger _closing = new();
        private readonly List<Task> _drivers = [];
        private Task<IDeveloperProjectOriginalManualExecutionAdmission> _acquisition = null!;
        private IDeveloperProjectOriginalManualExecutionSource _files = null!;
        private AuthenticatedResourceActor? _actor;
        private ResourceScope[] _scopes = [];
        private HomeResourceExecutionCapability? _capability;
        private HomeClaimedResourceAttestation? _attestation;
        private HomeManualProjectProcessFence? _fence;
        private CancellationTokenSource? _stop;
        private bool _sealed; private Task? _close;
        private ProcessResult? _result; private WorkspaceToolPhysicalOutcome? _outcome;
        internal ProcessBinding Binding => binding;
        internal IDeveloperProjectOriginalManualExecutionSource Files => _files;
        public Guid OperationId { get; } = Guid.NewGuid();
        public IWorkspaceToolFinalFence OriginalFence => _fence ?? throw new UnauthorizedAccessException("Manual Home Run is not admitted.");
        internal void DemandExternalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        internal void RequestRetirement() { lock (_gate) _sealed = true; }
        internal void DemandLive()
        { lock (_gate) if (_sealed || owner._sealed) throw new ObjectDisposedException("Original manual project process admission"); }
        internal bool HasScope(AuthenticatedResourceActor actor, ResourceScope scope)
        { lock (_gate) return !_sealed && _actor == actor && _scopes.Length == 1 && _scopes[0] == scope; }
        internal Task<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, ResourceScope scope, CancellationToken token)
        {
            lock (_gate)
            {
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var task = EvaluatePublishedAsync(begin.Task, actor, scope, token); _drivers.Add(task); begin.SetResult(); return task;
            }
        }
        private async Task<ResourceAccessDecision> EvaluatePublishedAsync(Task begin, AuthenticatedResourceActor actor, ResourceScope scope, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            await ValidateSourceAsync(actor, token).ConfigureAwait(false);
            return new(await ActorAsync(token).ConfigureAwait(false) == actor && HasScope(actor, scope),
                "DEV_MANUAL_RUN", actor.ActorId, scope.Revision, actor.OrganisationId);
        }
        internal Task<IDeveloperProjectOriginalManualExecutionAdmission> Start(CancellationToken token)
        {
            _sources.BindOriginalOwner(this);
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _acquisition = AcquireAsync(begin.Task, token); begin.SetResult(); return _acquisition;
        }
        private async Task<AuthenticatedResourceActor> ActorAsync(CancellationToken token) =>
            await _sources.AwaitAsync(_sources.Invoke(() => owner._profiles.GetCurrentAsync(token))).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Open the genuine DeviceLocal Home profile before running this project.");
        private async Task ValidateSourceAsync(AuthenticatedResourceActor actor, CancellationToken token)
        {
            DemandLive();
            if (!_sources.Invoke(() => ReferenceEquals(owner._files(), _files) && binding.IsIssued(_files)))
                throw new UnauthorizedAccessException("The SAME configured Files manual execution issuer retired.");
            await _sources.AwaitAsync(_sources.Invoke(() => binding.RevalidateAsync(_files, actor, token))).ConfigureAwait(false);
            DemandLive();
        }
        private async Task<IDeveloperProjectOriginalManualExecutionAdmission> AcquireAsync(Task begin, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            return await _sources.RunToOriginalSettlementAsync(async () =>
            {
                DemandLive(); _files = _sources.Invoke(owner._files);
                _stop = _sources.Invoke(() => CancellationTokenSource.CreateLinkedTokenSource(token));
                _actor = await ActorAsync(_stop.Token).ConfigureAwait(false);
                await ValidateSourceAsync(_actor, _stop.Token).ConfigureAwait(false);
                var scopes = _sources.Invoke(() => binding.Scopes(_files)).ToArray();
                if (scopes.Length != 1 || scopes[0].Kind != "dev.project.console" || scopes[0].Access != ResourceAccess.Execute ||
                    string.IsNullOrWhiteSpace(scopes[0].Id) || string.IsNullOrWhiteSpace(scopes[0].Revision))
                    throw new UnauthorizedAccessException("Files must issue one exact registered-project console execution scope.");
                lock (_gate) _scopes = scopes;
                var arguments = _sources.Invoke(() => binding.ReviewArguments(OperationId, scopes[0]));
                var review = _sources.Invoke(() => owner._broker.PrepareReviewForActor(_actor, "dev", RunAction, scopes, arguments,
                    binding.ReviewText, null, binding.ReceiptPrefix + _actor.AuthenticationRevision));
                var observed = await _sources.AwaitAsync(_sources.Invoke(() => owner._broker.AuthorizePreparedReviewAsync(review, _stop.Token))).ConfigureAwait(false);
                var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
                while (observed.State == HomePreparedReviewState.RequestObserved && observed.Request?.State == HomePermissionRequestState.PendingApproval)
                {
                    if (DateTimeOffset.UtcNow >= deadline) throw new UnauthorizedAccessException("Accept the exact manual console Run in Home. No process was started.");
                    await _sources.AwaitAsync(Task.Delay(TimeSpan.FromSeconds(1), _stop.Token)).ConfigureAwait(false);
                    await ValidateSourceAsync(_actor, _stop.Token).ConfigureAwait(false);
                    observed = await _sources.AwaitAsync(_sources.Invoke(() => owner._broker.ObservePreparedReviewAsync(review, _stop.Token))).ConfigureAwait(false);
                }
                if (observed.Request?.State != HomePermissionRequestState.Approved) throw new UnauthorizedAccessException("Manual console Run was not accepted.");
                await ValidateSourceAsync(_actor, _stop.Token).ConfigureAwait(false);
                if (await ActorAsync(_stop.Token).ConfigureAwait(false) != _actor) throw new UnauthorizedAccessException("The Home actor changed before manual Run admission.");
                _capability = await _sources.AwaitAsync(_sources.Invoke(() => owner._broker.BeginExecutionCapabilityAsync(review.RequestId, arguments, _stop.Token))).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("Manual Home execution capability unavailable.");
                var claim = await _sources.AwaitAsync(_sources.Invoke(() => owner._broker.ClaimExecutionObservedAsync(_capability, "dev", RunAction, scopes, arguments, _stop.Token))).ConfigureAwait(false);
                if (claim.Disposition != HomeResourceClaimDisposition.Claimed || claim.Actor != _actor) throw new UnauthorizedAccessException("Manual Home Run claim was rejected.");
                _attestation = owner._broker.CaptureClaimedAttestation(_capability) ?? throw new UnauthorizedAccessException("Individual manual Run Accept is required.");
                DemandLive();
                _fence = new(owner, this, owner._broker, owner._store, owner._profiles, _attestation, _capability, _actor, _stop.Token);
                return (IDeveloperProjectOriginalManualExecutionAdmission)this;
            }).ConfigureAwait(false);
        }
        internal Task RevalidateAsync(CancellationToken token)
        {
            DemandLive(); lock (_gate)
            {
                if (!_acquisition.IsCompletedSuccessfully || _capability is null) throw new UnauthorizedAccessException("The manual Run claim is not admitted.");
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var task = RevalidatePublishedAsync(begin.Task, token); _drivers.Add(task); begin.SetResult(); return task;
            }
        }
        private async Task RevalidatePublishedAsync(Task begin, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            await ValidateSourceAsync(_actor!, token).ConfigureAwait(false);
            if (await ActorAsync(token).ConfigureAwait(false) != _actor ||
                !await _sources.AwaitAsync(_sources.Invoke(() => owner._permissions.IsExecutionCurrentAsync(_capability!.RequestId, token))).ConfigureAwait(false) ||
                await ActorAsync(token).ConfigureAwait(false) != _actor)
                throw new UnauthorizedAccessException("The actual manual Run actor or Home claim changed.");
            DemandLive();
        }
        public void ObserveOriginalProcessOutcome(IWorkspaceOriginalInvocation invocation, ProcessResult? result, WorkspaceToolPhysicalOutcome outcome)
        {
            var physical = owner._physical();
            if (_fence is null || !physical.IsIssuedOriginalForFence(invocation, _fence) || !physical.ValidateOriginalOutcome(invocation, outcome) ||
                outcome.Effects.Any(effect => effect.Kind != WorkspaceToolEffectKind.ProcessStart || effect.CanonicalTarget != binding.CanonicalRoot ||
                    effect.ContentOrRequestSha256 != _fence.RequestSha256) || outcome.Effects.Count > 1)
                throw new UnauthorizedAccessException("No SAME original manual process outcome was produced.");
            lock (_gate)
            {
                if (_close is not null || _outcome is not null) throw new InvalidOperationException("The original manual process outcome is already retained or closing.");
                _result = result; _outcome = outcome;
            }
        }
        public ValueTask DisposeAsync()
        {
            DemandExternalJoin(); lock (_gate)
            {
                if (_close is not null) return new(_close); _sealed = true; _closing.BindOriginalOwner(this);
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _close = CloseAsync(begin.Task); begin.SetResult(); return new(_close);
            }
        }
        private async Task CloseAsync(Task begin)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            try { _closing.Invoke(() => { _stop?.Cancel(); return true; }); } catch (Exception error) { _closing.Retain(error); }
            Task[] drivers; lock (_gate) drivers = new Task[] { _acquisition }.Concat(_drivers).ToArray();
            foreach (var task in drivers)
                try { await _closing.AwaitAsync(task).ConfigureAwait(false); } catch (Exception error) { _closing.Retain(error); }
            await _sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            foreach (var error in _sources.OriginalErrors) _closing.Retain(error);
            if (_fence is { } fence)
                try { await _closing.ObserveOriginalCloseAsync(fence.DisposeAsync).ConfigureAwait(false); } catch (Exception error) { _closing.Retain(error); }
            if (_capability is { } capability)
            {
                try
                {
                    var known = _outcome is { OutcomeUnknown: false, OriginalErrors.Count: 0 } actual && actual.Effects.Count == 1 &&
                        actual.Effects[0] is { Admitted: true, EffectKnown: true, TerminalOutcomeKnown: true } && _fence?.HasStarted == true;
                    var succeeded = known && _result is { TimedOut: false, ExitCode: 0 } &&
                        _outcome!.Effects[0].ExitCode == _result.ExitCode && _closing.OriginalErrors.Count == 0;
                    var state = succeeded ? HomePermissionRequestState.Succeeded : _outcome?.KnownNoEffect == true && _fence?.HasStarted != true
                        ? HomePermissionRequestState.Cancelled : HomePermissionRequestState.Failed;
                    var audit = await _closing.AwaitAsync(_closing.Invoke(() => owner._broker.CompleteExecutionAsync(capability,
                        new(state, succeeded ? binding.SuccessCode : binding.FailureCode,
                            known ? $"Original project process exited {_result?.ExitCode}; timed out: {_result?.TimedOut}. Output remains with its manual Run owner."
                                : "No settled original process outcome proves success. Inspect this operation; it is never automatically replayed.", []), CancellationToken.None))).ConfigureAwait(false);
                    if (!audit.Succeeded) throw new InvalidOperationException("Manual Run Home completion audit failed: " + audit.Code);
                }
                catch (Exception error) { _closing.Retain(error); }
            }
            try { _closing.Invoke(() => { _stop?.Dispose(); return true; }); } catch (Exception error) { _closing.Retain(error); }
            await _closing.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (_closing.OriginalErrors.Count != 0) throw new AggregateException("Manual Run admission, entry or audit cleanup failed.", _closing.OriginalErrors);
        }
    }
}

public sealed class HomeDeveloperManualProjectExecutionActionPolicySource : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) => appId == "dev" && actionId == HomeDeveloperManualProjectExecutionAdmissionSource.RunAction
        ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, RequiresPerActionApproval: true) : null;
}
public sealed class HomeDeveloperManualProjectExecutionResourceResolver(Func<HomeDeveloperManualProjectExecutionAdmissionSource> source)
    : ICanonicalResourceAccessResolver
{
    public string ResourceKind => "dev.project.console";
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken token)
        => source().EvaluateAsync(actor, action, scope, token);
}
