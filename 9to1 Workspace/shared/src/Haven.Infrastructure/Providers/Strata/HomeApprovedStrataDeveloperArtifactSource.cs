using System.Runtime.ExceptionServices;
using System.Text.Json;
using SessionTrust = HavenOS.Home.PermissionsTrustNotifications.HomeTrustLevel;
using ReadRisk = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk;
using Dulche.Runtime;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure;

/// <summary>Opt-in actual local Home READ/use owner for explicitly selected developer artifacts.
/// It issues neither publisher trust nor system-installed/Ready status. Selection alone grants
/// nothing: the SAME current local Task/model admission and a full individual Home Accept are
/// required. Kernel-protected file identity/hash/inventory/CUDA checks remain in the original
/// StrataNativeArtifactSource. Session/cold restart never reconstructs an approval from metadata.</summary>
public sealed class HomeApprovedStrataDeveloperArtifactSource : IStrataVerifiedInstallationSource,
    ICanonicalResourceAccessResolver, IAsyncDisposable
{
    public const string ReadAction = "models.strata.developerArtifacts.read";
    public const string Kind = "strata.developer-artifacts";
    private readonly HomeLocalDomainComposition _home;
    private readonly TaskExecutionCoordinator _tasks;
    private readonly TaskRunPermissionAuthority _authority;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Work> _work = [];
    private readonly List<Task> _liveReadOriginals = [];
    private readonly Dictionary<TaskRunAttemptAdmission, Grant> _grants = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Lease> _leases = new(ReferenceEqualityComparer.Instance);
    private StrataDeveloperArtifactCapture? _selection; private long _revision; private bool _retiring; private Task? _close;
    private Exception? _readCustodyFailure;
    public HomeApprovedStrataDeveloperArtifactSource(HomeLocalDomainComposition sameHome,
        TaskExecutionCoordinator sameTasks, TaskRunPermissionAuthority sameAuthority)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The approved developer artifact owner requires Linux.");
        _home = sameHome ?? throw new ArgumentNullException(nameof(sameHome));
        _tasks = sameTasks ?? throw new ArgumentNullException(nameof(sameTasks));
        _authority = sameAuthority ?? throw new ArgumentNullException(nameof(sameAuthority));
    }
    public string ResourceKind => Kind;

    /// <summary>Configuration only. No artifact IO or approval occurs. Once an admitted read
    /// cohort exists, drain and restart the host to select another tuple; an altered tuple cannot take over any old approval/lease.</summary>
    public long SelectOriginalDeveloperArtifacts(string explicitConfigurationJson)
    {
        var capture = StrataDeveloperArtifactCapture.Capture(explicitConfigurationJson);
        _home.DemandOriginalStarted();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (_grants.Count != 0 || _work.Any(work => !work.Driver.IsCompleted))
                throw new InvalidOperationException("Drain and restart the original host before selecting another artifact tuple.");
            _selection = capture; return _revision = checked(_revision + 1);
        }
    }
    public Task<StrataVerifiedInstallationLease> AcquireOriginalAsync(ModelIdentity model,
        TaskRunAttemptAdmission admission, IInferenceEngineOriginalSourceScope scope, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(model); ArgumentNullException.ThrowIfNull(admission);
        Work work; var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (_work.Count >= 128) throw new InvalidOperationException("Retained original approved-artifact custody is full.");
            work = new(this, scope); _work.Add(work); work.Driver = RunAsync(begin.Task, work, model, admission, token);
        }
        try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { scope.RetainOriginalTask(work.Driver); return true; }); } catch (Exception cause) { work.Add(work.Driver, cause); }
        finally { begin.SetResult(); }
        return work.Driver;
    }
    private async Task<StrataVerifiedInstallationLease> RunAsync(Task begin, Work work, ModelIdentity model,
        TaskRunAttemptAdmission admission, CancellationToken caller)
    {
        await begin.ConfigureAwait(false); Lease? product = null;
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var token = CancellationTokenSource.CreateLinkedTokenSource(caller, _stop.Token);
        try
        {
            work.Throw();
            StrataDeveloperArtifactCapture selected; long revision;
            lock (_gate) { selected = _selection ?? throw Missing(model); revision = _revision; }
            _home.DemandOriginalStarted();
            if (selected.Selection.Model != model || admission.Lease.Candidate.UsesCloud ||
                admission.Lease.Candidate.ProviderId != model.ProviderId || admission.Lease.Candidate.ModelId != model.ModelId ||
                admission.Lease.Candidate.ArtifactIdentity != model.ArtifactRevision)
                throw new UnauthorizedAccessException("SAME selected full model identity/revision and direct local Task admission required.");
            var actor = await work.Read(() => _home.Profiles.GetCurrentAsync(work.Scope, work.Retain, token.Token).AsTask()).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The original local Home profile is unavailable.");
            var current = await work.Read(() => ((ITaskRunOriginalInferenceAttemptSource)_tasks)
                .GetIssuedAttemptWithinOriginalSourceAsync(admission, work.Scope, work.Retain, token.Token)).ConfigureAwait(false);
            if (!ReferenceEquals(current, admission)) throw new UnauthorizedAccessException("A copied/stale Task admission cannot obtain artifact READ.");
            await work.Read(() => ((ITaskRunOriginalInferenceAdmissionSource)_authority)
                .ValidateOriginalInferenceAdmissionAsync(admission, work.Scope, work.Retain, token.Token)).ConfigureAwait(false);
            Grant grant; TaskCompletionSource? start = null;
            lock (_gate)
            {
                DemandSelection(selected, revision);
                if (!_grants.TryGetValue(admission, out grant!))
                {
                    if (_grants.Count >= 128) throw new InvalidOperationException("Retained original Home artifact claims are full.");
                    grant = new(selected, revision, model, admission, actor); _grants.Add(admission, grant);
                    start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    grant.Driver = AcquireGrantAsync(start.Task, grant, work, token.Token);
                }
                if (grant.Actor != actor || !ReferenceEquals(grant.Selection, selected) || grant.Model != model)
                    throw new UnauthorizedAccessException("The original Home actor/artifact/model changed during admission.");
            }
            try { work.Retain(grant.Driver); } finally { start?.SetResult(); }
            await work.Observe(grant.Driver).ConfigureAwait(false);
            await work.Read(() => ((ITaskRunOriginalInferenceAdmissionSource)_authority)
                .ValidateOriginalInferenceAdmissionAsync(admission, work.Scope, work.Retain, token.Token)).ConfigureAwait(false);
            // Home review may take minutes; read the SAME actual canonical Task row again
            // after every approval/claim/authority await, before publishing a new borrow.
            current = await work.Read(() => ((ITaskRunOriginalInferenceAttemptSource)_tasks)
                .GetIssuedAttemptWithinOriginalSourceAsync(admission, work.Scope, work.Retain, token.Token)).ConfigureAwait(false);
            if (!ReferenceEquals(current, admission)) throw new UnauthorizedAccessException("The original Task/run/attempt retired while awaiting artifact READ approval.");
            work.Invoke(() => { DemandGrant(grant); return true; });
            product = new(this, grant);
            lock (_gate) { DemandSelection(selected, revision); _leases.Add(product); grant.Borrows++; }
        }
        catch (Exception cause) { work.Add(null, cause); }
        await work.Join().ConfigureAwait(false);
        if (work.Errors.Count != 0 && product is not null)
            try { await product.DisposeAsync().ConfigureAwait(false); } catch (Exception cause) { work.Add(null, cause); }
        work.Throw(); return product ?? throw new InvalidDataException("No original Home-approved artifact READ lease was issued.");
    }
    private async Task AcquireGrantAsync(Task begin, Grant grant, Work work, CancellationToken token)
    {
        await begin.ConfigureAwait(false);
        var broker = _home.Broker;
        grant.Review = work.Invoke(() => broker.PrepareReviewForActor(grant.Actor, "models", ReadAction, [grant.Scope],
            grant.Arguments,
            "Read/use this exact developer worker and Safetensors checkpoint for the SAME Task/Run. This is not publisher trust, installation status or native readiness. Kernel protection/CUDA/build observations remain required.\n\nComplete nonsecret artifact/Task/Run/attempt selection, independent inventory and developer PUBLIC key for this individual review:\n" + grant.Arguments.GetRawText(),
            null, "strata:developer:" + grant.Actor.AuthenticationRevision));
        var observation = await work.Read(() => broker.AuthorizePreparedReviewAsync(grant.Review, token)).ConfigureAwait(false);
        var until = DateTimeOffset.UtcNow.AddMinutes(5);
        while (observation.Request?.State == HomePermissionRequestState.PendingApproval)
        {
            if (DateTimeOffset.UtcNow >= until) throw new UnauthorizedAccessException("STRATA_DEVELOPER_ARTIFACT_READ_APPROVAL_REQUIRED: review the exact pending request in Home.");
            await work.Read(() => Task.Delay(TimeSpan.FromSeconds(1), token)).ConfigureAwait(false);
            if (await work.Read(() => _home.Profiles.GetCurrentAsync(work.Scope, work.Retain, token).AsTask()).ConfigureAwait(false) != grant.Actor)
                throw new UnauthorizedAccessException("The original Home actor changed while awaiting artifact approval.");
            observation = await work.Read(() => broker.ObservePreparedReviewAsync(grant.Review, token)).ConfigureAwait(false);
        }
        if (observation.Request?.State != HomePermissionRequestState.Approved || observation.Request.AppliedGrantId is not null ||
            observation.Request.AppliedTrustLevel != SessionTrust.Session || !observation.Request.Policy.RequiresPerActionApproval)
            throw new UnauthorizedAccessException("Individual original Home Accept is required; persistent/general trust is not installation READ authority.");
        await work.Read(() => broker.BeginExecutionCapabilityAsync(grant.Review.RequestId, grant.Arguments, token),
            value => grant.Capability = value).ConfigureAwait(false);
        var capability = grant.Capability ?? throw new UnauthorizedAccessException("The original Home artifact READ capability was not issued.");
        var claim = await work.Read(() => broker.ClaimExecutionObservedAsync(capability, "models", ReadAction,
            [grant.Scope], grant.Arguments, token)).ConfigureAwait(false);
        if (claim.Disposition != HomeResourceClaimDisposition.Claimed || claim.Actor != grant.Actor)
            throw new UnauthorizedAccessException("The original Home artifact READ claim was refused.");
        grant.Attestation = work.Invoke(() => broker.CaptureClaimedAttestation(capability))
            ?? throw new UnauthorizedAccessException("The original individual READ attestation is unavailable.");
        work.Invoke(() => { DemandGrant(grant); return true; });
    }
    private void DemandSelection(StrataDeveloperArtifactCapture selected, long revision)
    {
        if (_retiring || !ReferenceEquals(_selection, selected) || _revision != revision)
            throw new UnauthorizedAccessException("The original selected artifact tuple/revision/lifetime changed.");
    }
    private void RetainLiveOriginal(Task raw)
    {
        Exception? failure = null;
        lock (_gate)
        {
            // Only actual completed successes can leave custody. Always retain an already
            // created Task before refusing; pending/faulted children remain owned and joined.
            _liveReadOriginals.RemoveAll(original => original.IsCompletedSuccessfully);
            if (!_liveReadOriginals.Any(original => ReferenceEquals(original, raw))) _liveReadOriginals.Add(raw);
            if (_liveReadOriginals.Count > 128)
                failure = _readCustodyFailure ??= new InvalidOperationException("Original Strata principal/read custody is full; no more reads may start.");
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private void LiveScope(Action finite) => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { finite(); return true; });
    private void DemandGrant(Grant grant)
    {
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        {
            lock (_gate) { DemandSelection(grant.Selection, grant.Revision); if (_readCustodyFailure is { } retainedFailure) ExceptionDispatchInfo.Capture(retainedFailure).Throw(); if (grant.Closing || !_grants.TryGetValue(grant.Admission, out var actual) || !ReferenceEquals(actual, grant)) throw new UnauthorizedAccessException("Original artifact grant retired."); }
            _home.DemandOriginalStarted(); ((ITaskRunOriginalInferenceAdmissionSource)_authority).DemandOriginalInferenceAdmission(grant.Admission);
            if (grant.Capability is null || grant.Attestation is null || !ReferenceEquals(_home.Broker.CaptureClaimedAttestation(grant.Capability), grant.Attestation))
                throw new UnauthorizedAccessException("The SAME uncompleted claimed READ issuer is required.");
            _home.StateStore.DemandOriginalClaimedReadCurrent(_home.Broker, _home.Profiles, grant.Attestation, grant.Actor, LiveScope, RetainLiveOriginal);
            lock (_gate) DemandSelection(grant.Selection, grant.Revision);
            return true;
        });
    }
    public bool IsIssuedOriginalInstallation(StrataVerifiedInstallationLease sameLease, ModelIdentity model, TaskRunAttemptAdmission admission)
    {
        lock (_gate) return !_retiring && sameLease is Lease actual && ReferenceEquals(actual.Owner, this) && !actual.Closed &&
            _leases.Contains(actual) && ReferenceEquals(actual.Grant.Admission, admission) && actual.Grant.Model == model;
    }
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Grant? grant;
        lock (_gate) grant = !_retiring && action == ReadAction ? _grants.Values.SingleOrDefault(value =>
            !value.Closing && value.Actor == actor && value.Scope == scope && ReferenceEquals(value.Selection, _selection) && value.Revision == _revision) : null;
        if (grant is not null) ((ITaskRunOriginalInferenceAdmissionSource)_authority).DemandOriginalInferenceAdmission(grant.Admission);
        return ValueTask.FromResult(new ResourceAccessDecision(grant is not null, "STRATA_ORIGINAL_DEVELOPER_ARTIFACT_READ", actor.ActorId, scope.Revision, actor.OrganisationId));
    }
    private static InferenceEngineException Missing(ModelIdentity model) => new(new(DulcheErrorCode.ProviderUnavailable,
        "STRATA_APPROVED_DEVELOPER_ARTIFACT_SELECTION_REQUIRED", model.StableKey, false));
    private sealed class Grant(StrataDeveloperArtifactCapture selected,
        long revision, ModelIdentity model, TaskRunAttemptAdmission admission, AuthenticatedResourceActor actor)
    {
        public StrataDeveloperArtifactCapture Selection { get; } = selected; public long Revision { get; } = revision;
        public ModelIdentity Model { get; } = model; public TaskRunAttemptAdmission Admission { get; } = admission; public AuthenticatedResourceActor Actor { get; } = actor;
        public ResourceScope Scope { get; } = new(Kind, "strata:" + selected.Digest + ":" + admission.AttemptId.ToString("N"), revision.ToString(System.Globalization.CultureInfo.InvariantCulture), ResourceAccess.Read);
        public JsonElement Arguments { get; } = JsonSerializer.SerializeToElement(new
        { artifacts = selected.Arguments, selectionRevision = revision, taskID = admission.Snapshot.TaskId, runID = admission.Snapshot.ExecutionId, attemptID = admission.AttemptId });
        public Task Driver = null!; public HomeResourcePreparedReview? Review; public HomeResourceExecutionCapability? Capability;
        public HomeClaimedResourceAttestation? Attestation; public int Borrows; public bool Closing;
    }
    private sealed class Lease(HomeApprovedStrataDeveloperArtifactSource owner, Grant grant) : StrataVerifiedInstallationLease
    {
        public HomeApprovedStrataDeveloperArtifactSource Owner { get; } = owner; public Grant Grant { get; } = grant; public bool Closed;
        public override TaskRunAttemptAdmission OriginalAdmission => Grant.Admission; public override ModelIdentity OriginalModel => Grant.Model;
        public override string OriginalWorkerRoot => Grant.Selection.Selection.WorkerRoot;
        public override StrataInstalledFile OriginalWorkerFile => Grant.Selection.Selection.WorkerFile;
        public override string OriginalCheckpointRoot => Grant.Selection.Selection.CheckpointRoot;
        public override IReadOnlyList<StrataInstalledFile> OriginalCheckpointFiles => Grant.Selection.Selection.CheckpointFiles;
        public override InferenceModelRequirements OriginalRequirements => Grant.Selection.Requirements;
        public override InferenceEngineSupport? OriginalBuildSupport => Grant.Selection.BuildSupport;
        public override IReadOnlyList<int> OriginalCudaDeviceIndices => Grant.Selection.Selection.CudaDeviceIndices;
        public override void DemandCurrentOriginalInstallation()
        { lock (Owner._gate) if (Closed || !Owner._leases.Contains(this)) throw new ObjectDisposedException("Original developer-artifact READ lease"); Owner.DemandGrant(Grant); }
        public override ValueTask DisposeAsync()
        {
            lock (Owner._gate)
            {
                if (Closed) return ValueTask.CompletedTask; Closed = true;
                if (!Owner._leases.Remove(this) || --Grant.Borrows < 0) throw new InvalidOperationException("Original artifact READ borrow release was inconsistent.");
            }
            return ValueTask.CompletedTask;
        }
    }
    public void DemandExternalOriginalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
    public void RequestOriginalRetirement()
    { lock (_gate) { if (_retiring) return; _retiring = true; } CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { _stop.Cancel(); return true; }); }
    public Task CloseAndDrainOriginalAsync()
    {
        DemandExternalOriginalJoin(); TaskCompletionSource? begin = null; Task actual;
        lock (_gate) { _retiring = true; if (_close is null) { begin = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = CloseAsync(begin.Task); } actual = _close; }
        begin?.SetResult(); return actual;
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
    private async Task CloseAsync(Task begin)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        List<Exception> failures = []; try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { _stop.Cancel(); return true; }); } catch (Exception cause) { failures.Add(cause); }
        Work[] work; lock (_gate) { work = _work.ToArray(); if (_readCustodyFailure is { } retainedFailure) failures.Add(retainedFailure); }
        foreach (var original in work) { try { await original.Driver.ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); } }
        Lease[] leases; Grant[] grants;
        lock (_gate) { leases = _leases.ToArray(); grants = _grants.Values.ToArray(); }
        foreach (var lease in leases) try { await lease.DisposeAsync().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
        foreach (var grant in grants)
        {
            grant.Closing = true;
            try { await grant.Driver.ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            if (grant.Capability is not null)
            {
                try
                {
                    var outcome = grant.Driver.IsCompletedSuccessfully ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.Failed;
                    var originalAudit = _home.Broker.CompleteExecutionAsync(grant.Capability,
                        new(outcome, "STRATA_DEVELOPER_ARTIFACT_READ_CLOSED", "Original approved developer artifact read cohort retired; native outcome is separately observed.", []), CancellationToken.None);
                    RetainLiveOriginal(originalAudit); var audit = await originalAudit.ConfigureAwait(false);
                    if (!audit.Succeeded) throw new InvalidOperationException("Original artifact READ audit remains unfinished: " + audit.Code);
                }
                catch (Exception cause) { failures.Add(cause); }
            }
        }
        // Each top-level scoped profile/audit Task joins its own actual children. Repeat
        // the retained set after those Tasks settle so post-await children are also read.
        var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        while (true)
        {
            Task[] reads; lock (_gate) { if (_readCustodyFailure is { } retainedFailure && !failures.Any(cause => ReferenceEquals(cause, retainedFailure))) failures.Add(retainedFailure); reads = _liveReadOriginals.Where(raw => !joined.Contains(raw)).ToArray(); }
            if (reads.Length == 0) break;
            foreach (var raw in reads) { joined.Add(raw); try { await raw.ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); } }
        }
        _stop.Dispose();
        if (failures.Count != 0) throw new AggregateException("Original Home-approved Strata artifact reads did not drain cleanly.", failures);
    }
    private sealed class Work(HomeApprovedStrataDeveloperArtifactSource owner, IInferenceEngineOriginalSourceScope scope)
    {
        public Task<StrataVerifiedInstallationLease> Driver = null!; private readonly List<Task> _raw = []; private readonly List<Exception> _errors = [];
        public IReadOnlyList<Exception> Errors { get { lock (_raw) return _errors.ToArray(); } }
        public void Add(Task? raw, Exception cause)
        {
            lock (_raw) { if (!_errors.Any(value => ReferenceEquals(value, cause))) _errors.Add(cause); if (raw?.Exception is { } group) foreach (var error in group.InnerExceptions) if (!_errors.Any(value => ReferenceEquals(value, error))) _errors.Add(error); }
        }
        public T Invoke<T>(Func<T> factory)
        {
            var thread = Environment.CurrentManagedThreadId; var active = true; var used = false; T captured = default!;
            try
            {
                return CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () =>
                {
                    // Guard the supplied scope's pre/post code too: restored caller
                    // ExecutionContexts must not join the original source they enter.
                    _ = scope.InvokeOriginalFactory(() =>
                    {
                        if (!active || used || thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Original artifact callback is inactive, repeated or foreign-thread.");
                        used = true;
                        try { return captured = CloudflareOriginalExecutionGuard.InvokeOriginal(owner, factory); }
                        catch (Exception original) { Add(null, original); throw; }
                    });
                    if (!used) throw new InvalidOperationException("Original artifact callback was omitted."); Throw(); return captured;
                });
            }
            catch (Exception cause) { Add(null, cause); if (cause is OperationCanceledException) throw new AggregateException("Original synchronous artifact factory fault.", cause); throw; }
            finally { active = false; }
        }
        public void Scope(Action body) => Invoke(() => { body(); return true; });
        public void Retain(Task raw)
        { lock (_raw) if (!_raw.Any(value => ReferenceEquals(value, raw))) _raw.Add(raw); CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () => { scope.RetainOriginalTask(raw); return true; }); }
        public async Task<T> Read<T>(Func<Task<T>> factory, Action<T>? capture = null)
        {
            Task<T>? raw = null;
            try { _ = Invoke(() => { raw = factory(); Retain(raw); return raw; }); } catch (Exception cause) { Add(raw, cause); }
            T result = default!;
            if (raw is not null) try { result = await raw.ConfigureAwait(false); capture?.Invoke(result); } catch (Exception cause) { Add(raw, cause); }
            Throw(); return result;
        }
        public async Task Read(Func<Task> factory)
        {
            Task? raw = null; try { _ = Invoke(() => { raw = factory(); Retain(raw); return raw; }); } catch (Exception cause) { Add(raw, cause); }
            if (raw is not null) await Observe(raw).ConfigureAwait(false); Throw();
        }
        public async Task Observe(Task raw) { try { await raw.ConfigureAwait(false); } catch (Exception cause) { Add(raw, cause); } Throw(); }
        public async Task Join() { Task[] raw; lock (_raw) raw = _raw.ToArray(); foreach (var task in raw) try { await task.ConfigureAwait(false); } catch (Exception cause) { Add(task, cause); } }
        public void Throw()
        {
            var failures = Errors; if (failures.Count == 1 && failures[0] is not OperationCanceledException) ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count != 0) throw new AggregateException("Original approved artifact scope/source failed.", failures);
        }
    }
}

public sealed class HomeStrataDeveloperArtifactActionPolicySource : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) => appId == "models" &&
        actionId == HomeApprovedStrataDeveloperArtifactSource.ReadAction ? new(ReadRisk.High, false, false, true) : null;
}
/// <summary>Pure resolver indirection used while constructing the SAME Home graph; no grant or replacement Home.</summary>
public sealed class HomeStrataDeveloperArtifactResourceResolver(Func<HomeApprovedStrataDeveloperArtifactSource> source) : ICanonicalResourceAccessResolver
{
    public string ResourceKind => HomeApprovedStrataDeveloperArtifactSource.Kind;
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken token) =>
        source().EvaluateAsync(actor, action, scope, token);
}
