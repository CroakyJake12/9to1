using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// The actual owning provider/context policy supplies a retained remote permit. The producer owns
/// credential/budget and private-context egress checks applicable to that provider, not client flags.
/// No implementation may substitute CAKE login for product/Home grants or invent cost/quota values.
/// </summary>
public interface ITaskRunCloudAdmissionSource
{
    ValueTask<ITaskRunCloudAdmissionLease?> AcquireOriginalAsync(TaskExecutionOwnerBinding owner,
        ProviderModelDescriptor actualModel, ProviderConfiguration actualConfiguration,
        TaskRunRouteCandidate exactCandidate, CancellationToken token);
}
public interface ITaskRunCloudAdmissionLease : IAsyncDisposable
{
    ValueTask RevalidateAsync(CancellationToken token);
}
/// <summary>The domain's original durable receipt authority, not a successful tool JSON field.</summary>
public interface ITaskRunActionReceiptAuthority
{
    Task ValidateOriginalAsync(TaskExecutionSnapshot snapshot, Guid attemptId, Guid actionId,
        string originalReceiptReference, CancellationToken token);
}
/// <summary>
/// Optional configured-route adapter reads the actual durable routing repository. It supplies
/// observations only. Absent configured route, the authority records a task-owned observation
/// revision, explicitly unrelated to upstream provider/config or Task PersistenceRevision.
/// </summary>
public interface ITaskRunRouteObservationSource
{
    ValueTask<TaskRunRouteCandidate?> ObserveOriginalAsync(TaskExecutionSnapshot snapshot,
        ProviderModelDescriptor actualModel, IReadOnlyList<string> requiredCapabilities, CancellationToken token);
    ValueTask DemandOriginalCurrentAsync(TaskExecutionSnapshot snapshot,
        TaskRunRouteCandidate originalObservation, CancellationToken token);
}

/// <summary>
/// Shared selected-route factory used by the actual model router. It captures current canonical
/// provider/configuration and central model permissions; returned route metadata alone is no grant.
/// Required restrictions come from the registered typed tools, not model-supplied action labels.
/// </summary>
public interface ITaskRunSelectedRouteCapture
{
    Task<TaskRunRouteCandidate> CaptureSelectedRouteAsync(TaskExecutionSnapshot snapshot,
        ProviderModelDescriptor actualSelection, IReadOnlyCollection<ToolCapability> requiredCapabilities,
        IReadOnlyCollection<RestrictedModelCapability> requiredRestrictedCapabilities,
        CancellationToken token = default);
    ValueTask<ProviderModelDescriptor?> GetRetainedSelectionAsync(TaskExecutionSnapshot snapshot,
        string requestedModelKey, IReadOnlyCollection<ToolCapability> requiredCapabilities,
        CancellationToken token = default);
}

/// <summary>
/// Canonical task authority over the EXISTING actor/model/config/privacy/governance sources.
/// This issues task/runtime lifetime only; every tool still passes the normal capability/permission
/// executor, and Home-scoped effects additionally pass Home's actor/ACL/review/final commit fence.
/// Selected-route capture is a trusted production factory operation after actual model selection,
/// not an API accepting a candidate's claimed eligibility or a client's permission scope list.
/// </summary>
public sealed partial class TaskRunPermissionAuthority : ITaskRunDelegationAuthority, ITaskRunSelectedRouteCapture
{
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly IModelProviderRegistry _providers;
    private readonly IProviderConfigurationStore _configurations;
    private readonly IPrivacyPreferenceStore _privacy;
    private readonly ModelPermissionEvaluator _modelPermissions;
    private readonly ITaskRunCloudAdmissionSource? _cloud;
    private readonly ITaskRunActionReceiptAuthority? _receipts;
    private readonly ITaskRunRouteObservationSource? _routes;
    private readonly Func<TaskExecutionCoordinator>? _originalTasks;
    private readonly ITaskRunVerifiedReauthenticationSource? _verifiedReauthentication;
    private readonly ITaskRunContextReauthenticationSource? _contextReauthentication;
    private readonly ITaskRunReauthenticationCustodySource? _reauthenticationCustody;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Owner> _owners = [];
    private bool _originalAdmissionSealed;

    // Request-only process retirement. This is neither original settlement nor permission.
    public bool IsOriginalAdmissionSealed { get { lock (_sync) return _originalAdmissionSealed; } }
    public void RequestOriginalAdmissionSeal()
    {
        lock (_sync)
        {
            _originalAdmissionSealed = true;
            foreach (var owner in _owners.Values) owner.Activation.Seal();
        }
    }
    private void DemandOriginalAdmissionOpen()
    {
        lock (_sync)
            if (_originalAdmissionSealed) throw new ObjectDisposedException("Task authority original admission");
    }
    private sealed class Owner(TaskExecutionOwnerBinding binding)
    {
        // Stable task-owned registry identity is distinct from each immutable authentication activation.
        public OwnerActivation Activation { get; private set; } = new(binding);
        public TaskExecutionOwnerBinding Binding => Activation.Binding;
        public void PublishActivation(OwnerActivation previous, OwnerActivation next)
        {
            if (!ReferenceEquals(Activation, previous) || !previous.IsSealed)
                throw new UnauthorizedAccessException("The original activation was not sealed for this renewal.");
            Activation = next;
        }
        public readonly Dictionary<string, Selection> Selections = new(StringComparer.Ordinal);
        public Selection? First;
        public readonly Dictionary<Guid, Lease> Attempts = [];
    }
    private sealed class OwnerActivation(TaskExecutionOwnerBinding binding)
    {
        public TaskExecutionOwnerBinding Binding { get; } = binding;
        private int _sealed;
        public bool IsSealed => Volatile.Read(ref _sealed) != 0;
        public void Seal() => Interlocked.Exchange(ref _sealed, 1);
    }
    private sealed record Selection(TaskRunRouteCandidate Candidate, ProviderModelDescriptor Model,
        ProviderConfiguration Configuration, string ModelFingerprint, string ConfigurationFingerprint,
        RestrictedModelCapability[] Restrictions, TaskExecutionSnapshot OriginalSnapshot,
        TaskRunRouteCandidate? ConfiguredObservation);

    public TaskRunPermissionAuthority(IAuthenticatedResourceActorSource actors, IModelProviderRegistry providers,
        IProviderConfigurationStore configurations, IPrivacyPreferenceStore privacy, ModelPermissionEvaluator modelPermissions,
        ITaskRunCloudAdmissionSource? cloud = null, ITaskRunActionReceiptAuthority? receipts = null,
        ITaskRunRouteObservationSource? routes = null, Func<TaskExecutionCoordinator>? originalTasks = null)
        : this(actors, providers, configurations, privacy, modelPermissions, cloud, receipts, routes, originalTasks,
            verifiedReauthentication: null, contextReauthentication: null, reauthenticationCustody: null) { }

    // All three specialized producers are required for renewal. Neither a configured identity
    // reader nor this constructor implies usable context, no-overlap custody or a binding CAS.
    public TaskRunPermissionAuthority(IAuthenticatedResourceActorSource actors, IModelProviderRegistry providers,
        IProviderConfigurationStore configurations, IPrivacyPreferenceStore privacy, ModelPermissionEvaluator modelPermissions,
        ITaskRunCloudAdmissionSource? cloud, ITaskRunActionReceiptAuthority? receipts,
        ITaskRunRouteObservationSource? routes, Func<TaskExecutionCoordinator>? originalTasks,
        ITaskRunVerifiedReauthenticationSource? verifiedReauthentication,
        ITaskRunContextReauthenticationSource? contextReauthentication,
        ITaskRunReauthenticationCustodySource? reauthenticationCustody)
    {
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _configurations = configurations ?? throw new ArgumentNullException(nameof(configurations));
        _privacy = privacy ?? throw new ArgumentNullException(nameof(privacy));
        _modelPermissions = modelPermissions ?? throw new ArgumentNullException(nameof(modelPermissions));
        _cloud = cloud; _receipts = receipts; _routes = routes; _originalTasks = originalTasks;
        if (verifiedReauthentication is not null && (verifiedReauthentication is not TaskRunVerifiedActorReauthenticationSource actual ||
            !ReferenceEquals(actual.OriginalVerifiedTaskActors, actors)))
            throw new ArgumentException("Renewal must borrow the SAME trusted Task actor source.", nameof(verifiedReauthentication));
        _verifiedReauthentication = verifiedReauthentication;
        _contextReauthentication = contextReauthentication;
        _reauthenticationCustody = reauthenticationCustody;
    }

    // Preserve the original eight-parameter CLR constructor. Optional legacy composition
    // conveys no canonical parent lookup or child authority.
    public TaskRunPermissionAuthority(IAuthenticatedResourceActorSource actors, IModelProviderRegistry providers,
        IProviderConfigurationStore configurations, IPrivacyPreferenceStore privacy, ModelPermissionEvaluator modelPermissions,
        ITaskRunCloudAdmissionSource? cloud, ITaskRunActionReceiptAuthority? receipts, ITaskRunRouteObservationSource? routes)
        : this(actors, providers, configurations, privacy, modelPermissions, cloud, receipts, routes, originalTasks: null) { }

    public async Task<TaskExecutionOwnerBinding> AuthorizeStartAsync(TaskExecutionSnapshot proposed, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        DemandOriginalAdmissionOpen();
        if (proposed.TaskId == Guid.Empty || proposed.ContextId == Guid.Empty || proposed.ExecutionId == Guid.Empty ||
            proposed.OwnerBinding is not null || proposed.Attempts.Count != 0 || proposed.ParentDelegation is not null)
            throw new UnauthorizedAccessException("A fresh canonical task/run intent is required.");
        var actor = await _actors.GetCurrentAsync(token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Current product actor is unavailable.");
        if (!ValidActor(actor)) throw new UnauthorizedAccessException("Current product actor identity is incomplete.");
        token.ThrowIfCancellationRequested();
        if (await _actors.GetCurrentAsync(token).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Product actor changed during task admission.");
        var binding = new TaskExecutionOwnerBinding(proposed.TaskId, proposed.ContextId, proposed.ExecutionId,
            actor.ActorId, actor.ProfileId, actor.AccountId, actor.OrganisationId, actor.AuthenticationRevision,
            "task-owner:" + Guid.NewGuid().ToString("N"));
        lock (_sync)
        {
            DemandOriginalAdmissionOpen();
            if (_owners.ContainsKey(proposed.TaskId)) throw new UnauthorizedAccessException("A task owner cannot be replaced.");
            if (_owners.Count == 1024) throw new InvalidOperationException("Retained task authority capacity exhausted.");
            _owners.Add(proposed.TaskId, new(binding));
        }
        return binding; // Requested/ApprovedPermissionScopes remain recorded intent, never an issued grant.
    }

    /// <summary>
    /// Called by the actual selected-model factory, with restrictions derived from registered typed
    /// tools via ModelToolPermissionMap. Re-reads the canonical provider catalogue/config before
    /// issuing a detached observation. Initial local selection works without a cloud/Home permit.
    /// Fallback retains the original route identity and checks the original selected config too.
    /// </summary>
    public async Task<TaskRunRouteCandidate> CaptureSelectedRouteAsync(TaskExecutionSnapshot snapshot,
        ProviderModelDescriptor actualSelection, IReadOnlyCollection<ToolCapability> requiredCapabilities,
        IReadOnlyCollection<RestrictedModelCapability> requiredRestrictedCapabilities,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(actualSelection); ArgumentNullException.ThrowIfNull(requiredCapabilities);
        ArgumentNullException.ThrowIfNull(requiredRestrictedCapabilities);
        var owner = RequireOwner(snapshot);
        await DemandDelegatedParentAsync(owner, token).ConfigureAwait(false);
        await RequireActorAsync(owner.Binding, token).ConfigureAwait(false);
        var required = requiredCapabilities.Distinct().Order().ToArray();
        var restrictions = requiredRestrictedCapabilities.Distinct().Order().ToArray();
        if (required.Length > 128 || restrictions.Length > 128 || required.Any(value => !Enum.IsDefined(value)) ||
            restrictions.Any(value => !Enum.IsDefined(value))) throw new ArgumentException("Registered capability requirements are invalid.");
        var (model, configuration) = await ReadSelectedAsync(actualSelection, required, restrictions, token).ConfigureAwait(false);
        var names = required.Select(value => value.ToString()).ToArray();
        var observed = _routes is null ? null : await _routes.ObserveOriginalAsync(snapshot, model, names, token).ConfigureAwait(false);
        Selection? first;
        lock (_sync) first = owner.First;
        var routeId = first?.Candidate.RouteId ?? observed?.RouteId ?? "task-selected:" + snapshot.TaskId.ToString("D");
        var routeRevision = first?.Candidate.RouteRevision ?? observed?.RouteRevision ?? 1;
        if (string.IsNullOrWhiteSpace(routeId) || routeId.Length > 256 || routeRevision <= 0 ||
            observed is not null && (observed.ProviderId != model.ProviderId || observed.ModelId != model.Name ||
                observed.ArtifactIdentity is not null || observed.UsesCloud != !model.IsLocal || !observed.RequiredCapabilities.SequenceEqual(names) ||
                first is not null && (observed.RouteId != routeId || observed.RouteRevision != routeRevision)))
            throw new UnauthorizedAccessException("Actual configured route observation changed the selected candidate.");
        var candidate = new TaskRunRouteCandidate(routeId, routeRevision, model.ProviderId, model.Name,
            null, !model.IsLocal, Array.AsReadOnly(names)); // Existing descriptor exposes no artifact revision; do not invent one.
        var selection = new Selection(candidate, DetachModel(model), DetachConfiguration(configuration),
            ModelFingerprint(model), ConfigurationFingerprint(configuration), restrictions, snapshot,
            observed is null ? null : observed with { RequiredCapabilities = Array.AsReadOnly(observed.RequiredCapabilities.ToArray()) });
        await RequireActorAsync(owner.Binding, token).ConfigureAwait(false);
        await DemandDelegatedParentAsync(owner, token).ConfigureAwait(false);
        lock (_sync)
        {
            if (!ReferenceEquals(RequireOwner(snapshot), owner)) throw new UnauthorizedAccessException("Task authority activation retired.");
            if (owner.First is { } published && (published.Candidate.RouteId != candidate.RouteId ||
                published.Candidate.RouteRevision != candidate.RouteRevision))
                throw new UnauthorizedAccessException("Concurrent route capture changed the original task route identity.");
            var key = CandidateKey(candidate);
            if (owner.Selections.TryGetValue(key, out var old))
            {
                if (old.ModelFingerprint != selection.ModelFingerprint || old.ConfigurationFingerprint != selection.ConfigurationFingerprint ||
                    !old.Restrictions.SequenceEqual(selection.Restrictions))
                    throw new UnauthorizedAccessException("A retained route observation cannot be rewritten.");
                return old.Candidate;
            }
            if (owner.Selections.Count == 128) throw new InvalidOperationException("Original route observation capacity exhausted.");
            owner.Selections.Add(key, selection); owner.First ??= selection;
        }
        return candidate;
    }

    public async ValueTask<ProviderModelDescriptor?> GetRetainedSelectionAsync(TaskExecutionSnapshot snapshot,
        string requestedModelKey, IReadOnlyCollection<ToolCapability> requiredCapabilities,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(requiredCapabilities);
        var owner = RequireOwner(snapshot);
        await DemandDelegatedParentAsync(owner, token).ConfigureAwait(false);
        await RequireActorAsync(owner.Binding, token).ConfigureAwait(false);
        Selection? selected;
        lock (_sync)
        {
            _ = RequireOwner(snapshot); // Recheck the exact activation after all awaited owner reads.
            var candidates = owner.Selections.Values.Where(value => value.Model.Key == requestedModelKey &&
                requiredCapabilities.All(capability => value.Model.Supports(capability))).Take(2).ToArray();
            selected = candidates.Length == 1 ? candidates[0] : null;
        }
        // Original captured observation only. Every later dispatch still requires actual fresh lease
        // admission/current policy; a discovery outage never turns retained metadata into a grant.
        return selected is null ? null : DetachModel(selected.Model);
    }

    public async Task<ITaskRunAdmissionLease> AuthorizeAttemptAsync(TaskExecutionSnapshot snapshot, Guid proposedAttemptId,
        TaskRunRouteCandidate candidate, Guid? expectedPreviousAttemptId, CancellationToken token)
    {
        if (proposedAttemptId == Guid.Empty) throw new UnauthorizedAccessException("Original attempt identity required.");
        var owner = RequireOwner(snapshot);
        await DemandDelegatedParentAsync(owner, token).ConfigureAwait(false);
        Selection selection;
        lock (_sync)
            selection = owner.Selections.GetValueOrDefault(CandidateKey(candidate))
                ?? throw new UnauthorizedAccessException("Candidate was not captured from actual selected provider/configuration.");
        if (expectedPreviousAttemptId is null ? snapshot.Attempts.Count != 0 :
            snapshot.Attempts.LastOrDefault() is not { } old || old.Id != expectedPreviousAttemptId ||
                old.State is not (TaskRunAttemptState.Failed or TaskRunAttemptState.Suspended))
            throw new UnauthorizedAccessException("Fallback must name the actual original failed/suspended attempt.");
        lock (_sync)
            if (expectedPreviousAttemptId is null && owner.First?.Candidate != selection.Candidate)
                throw new UnauthorizedAccessException("Initial attempt must use the original selected task route.");
        await RevalidateSelectionAsync(owner, selection, token).ConfigureAwait(false);
        ITaskRunCloudAdmissionLease? cloud = null;
        try
        {
            if (candidate.UsesCloud)
            {
                var source = _cloud ?? throw new InvalidOperationException("Actual cloud context/credential/budget admission is unavailable.");
                cloud = await source.AcquireOriginalAsync(owner.Binding, selection.Model, selection.Configuration, selection.Candidate, token).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("Actual cloud admission refused; no context was dispatched.");
                await cloud.RevalidateAsync(token).ConfigureAwait(false);
            }
            await RevalidateSelectionAsync(owner, selection, token).ConfigureAwait(false);
            await DemandDelegatedParentAsync(owner, token).ConfigureAwait(false);
            Lease lease;
            lock (_sync)
            {
                _ = RequireOwner(snapshot); // Capture one immutable activation under its publication gate.
                lease = new Lease(this, owner, selection, proposedAttemptId, cloud);
                if (owner.Attempts.Count == 128 || !owner.Attempts.TryAdd(proposedAttemptId, lease))
                    throw new InvalidOperationException("Retained attempt authority capacity/identity conflict.");
            }
            cloud = null; // Explicit ownership transfer after retained issuance.
            return lease;
        }
        catch (Exception original)
        {
            if (cloud is not null)
            {
                Task? closeTask = null;
                try { closeTask = cloud.DisposeAsync().AsTask(); await closeTask.ConfigureAwait(false); }
                catch (Exception close)
                {
                    var actual = (Exception?)closeTask?.Exception ?? close;
                    if (!ReferenceEquals(original, actual)) throw new AggregateException(original, actual);
                }
            }
            throw;
        }
    }

    public async Task ValidateAcceptedActionAsync(TaskExecutionSnapshot snapshot, Guid attemptId, Guid actionId,
        string ownerReceiptReference, CancellationToken token)
    {
        var owner = RequireOwner(snapshot);
        if (actionId == Guid.Empty || string.IsNullOrWhiteSpace(ownerReceiptReference) || ownerReceiptReference.Length > 1024)
            throw new UnauthorizedAccessException("Original domain receipt required.");
        lock (_sync)
            if (!owner.Attempts.ContainsKey(attemptId)) throw new UnauthorizedAccessException("Original attempt issuer required.");
        // Accept the real owner receipt even after an effect's actor policy retires. The receipt owner
        // validates actual commit identity/current terminal result; never reconstruct or repeat effect.
        var receipts = _receipts ?? throw new InvalidOperationException("Actual domain receipt authority is unavailable.");
        await receipts.ValidateOriginalAsync(snapshot, attemptId, actionId, ownerReceiptReference, token).ConfigureAwait(false);
    }

    /// <summary>Fresh denial-only typed tool policy for the SAME private issued lease and selected model.
    /// The tool name comes from the registered owner, not a candidate or persisted scope grant.</summary>
    public Task ValidateOriginalToolAsync(TaskRunAttemptAdmission original, string exactToolName, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(original); ArgumentException.ThrowIfNullOrWhiteSpace(exactToolName);
        if (exactToolName.Length > 200) throw new ArgumentException("Registered typed tool name exceeded its bound.");
        return ValidateOriginalToolBodyAsync(original, exactToolName, token);
    }
    private async Task ValidateOriginalToolBodyAsync(TaskRunAttemptAdmission original, string exactToolName, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var owner = RequireOwner(original.Snapshot);
        Lease lease; Selection selected;
        lock (_sync)
        {
            lease = owner.Attempts.GetValueOrDefault(original.AttemptId)
                ?? throw new UnauthorizedAccessException("Actual original attempt issuer required.");
            if (!ReferenceEquals(original.Lease, lease) || original.AttemptId != lease.AttemptId)
                throw new UnauthorizedAccessException("SAME privately issued attempt lease required for typed tool admission.");
            selected = owner.Selections.GetValueOrDefault(CandidateKey(lease.Candidate))
                ?? throw new UnauthorizedAccessException("Original selected model is unavailable.");
            lease.DemandOriginalToolAdmission();
        }
        await ObserveOriginalPermissionReadAsync(RunOriginalToolPolicyRead(() => lease.RevalidateAsync(token).AsTask(), token)).ConfigureAwait(false);
        var required = ModelToolPermissionMap.Map(exactToolName);
        if (required is { } restricted)
        {
            var policy = await ObserveOriginalPermissionReadAsync(RunOriginalToolPolicyRead(() => _modelPermissions.GetOriginalPolicyAsync(token), token)).ConfigureAwait(false);
            if (!ModelPermissionEvaluator.Evaluate(policy, selected.Model, restricted).Allowed)
                throw new UnauthorizedAccessException("Current model policy denies the actual typed tool.");
            // Held actor/model/config reads occur before the final fresh policy observation.
            await ObserveOriginalPermissionReadAsync(RunOriginalToolPolicyRead(() => lease.RevalidateAsync(token).AsTask(), token)).ConfigureAwait(false);
            policy = await ObserveOriginalPermissionReadAsync(RunOriginalToolPolicyRead(() => _modelPermissions.GetOriginalPolicyAsync(token), token)).ConfigureAwait(false);
            if (!ModelPermissionEvaluator.Evaluate(policy, selected.Model, restricted).Allowed)
                throw new UnauthorizedAccessException("Model tool permission retired during admission.");
        }
        token.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!ReferenceEquals(RequireOwner(original.Snapshot), owner) || !ReferenceEquals(owner.Attempts.GetValueOrDefault(original.AttemptId), lease) ||
                !ReferenceEquals(owner.Selections.GetValueOrDefault(CandidateKey(lease.Candidate)), selected))
                throw new UnauthorizedAccessException("Original task/model activation retired during typed tool validation.");
            lease.DemandOriginalToolAdmission();
        }
        // Fresh policy observations precede the raw lifetime/native effect gate. This does not
        // claim an atomic model-policy writer lock, monetary reservation, Home permission or network exclusion.
    }
    private static Task<T> RunOriginalToolPolicyRead<T>(Func<Task<T>> finite, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try { return finite(); }
        catch (OperationCanceledException original) { throw new AggregateException("Synchronous original tool-policy callback fault.", original); }
    }
    private static Task RunOriginalToolPolicyRead(Func<Task> finite, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try { return finite(); }
        catch (OperationCanceledException original) { throw new AggregateException("Synchronous original tool-policy callback fault.", original); }
    }

    /// <summary>Pure private issuer identity; no actor/model/resource permission is granted by this check.</summary>
    public bool IsIssuedOriginal(ITaskRunAdmissionLease original)
    {
        lock (_sync)
            return original is Lease actual && _owners.Values.Any(owner =>
                owner.Attempts.Values.Any(lease => ReferenceEquals(lease, actual)));
    }

    public async Task ValidateTaskCommandAsync(TaskExecutionSnapshot snapshot, string command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        DemandOriginalAdmissionOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        if (command.Length > 128 || snapshot.OwnerBinding is not { } owner ||
            snapshot.TaskId != owner.TaskId || snapshot.ContextId != owner.ContextId || snapshot.ExecutionId != owner.ExecutionId)
            throw new UnauthorizedAccessException("Current task owner provenance is unavailable.");
        // Persisted ownership supplies the expected identity only. A fresh actual host/session
        // observation supplies current authentication; this creates no route/tool/Home grant.
        if (snapshot.ParentDelegation is not null)
            await DemandDelegatedParentAsync(RequireOwner(snapshot), token).ConfigureAwait(false);
        else
        {
            Owner? live;
            lock (_sync) live = _owners.GetValueOrDefault(snapshot.TaskId);
            if (live is not null) await DemandDelegatedParentAsync(live, token).ConfigureAwait(false);
        }
        await RequireActorAsync(owner, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        await RequireActorAsync(owner, token).ConfigureAwait(false);
        lock (_sync)
        {
            DemandOriginalAdmissionOpen();
            if (_owners.TryGetValue(snapshot.TaskId, out var original) &&
                (original.Activation.IsSealed || original.Binding != owner))
                throw new UnauthorizedAccessException("The original Task activation is sealed or changed; specialized renewal is required.");
        }
    }

    internal IAuthenticatedResourceActorSource OriginalTaskActors => _actors;

    /// <summary>Denial-only verification for the private explicit cloud-permission handler.
    /// It supplies no lease, cloud/context permit or effect and never accepts persisted scope text.</summary>
    internal async Task ValidateOriginalCloudPermissionAsync(TaskExecutionSnapshot snapshot,
        TaskExecutionOwnerBinding exactOwner, TaskRunRouteCandidate exactCandidate, CancellationToken token)
    {
        var owner = RequireOwner(snapshot);
        if (owner.Binding != exactOwner || !exactCandidate.UsesCloud ||
            snapshot.State is TaskExecutionLifecycle.Completed or TaskExecutionLifecycle.Cancelled or TaskExecutionLifecycle.Failed)
            throw new UnauthorizedAccessException("Current original task/run/owner is unavailable for permission resolution.");
        Selection selected;
        lock (_sync)
            selected = owner.Selections.GetValueOrDefault(CandidateKey(exactCandidate))
                ?? throw new UnauthorizedAccessException("The original selected model was not captured by this Task issuer.");
        await RevalidateSelectionAsync(owner, selected, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        lock (_sync)
            if (!ReferenceEquals(_owners.GetValueOrDefault(snapshot.TaskId), owner) ||
                !ReferenceEquals(owner.Selections.GetValueOrDefault(CandidateKey(exactCandidate)), selected))
                throw new UnauthorizedAccessException("Original task/selection retired during permission validation.");
    }

    private Owner RequireOwner(TaskExecutionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_sync)
        {
            DemandOriginalAdmissionOpen();
            if (!_owners.TryGetValue(snapshot.TaskId, out var owner) || owner.Activation.IsSealed || snapshot.OwnerBinding != owner.Binding ||
                snapshot.ContextId != owner.Binding.ContextId || snapshot.ExecutionId != owner.Binding.ExecutionId)
                throw new UnauthorizedAccessException("Actual original task owner issuance required; recorded receipt text is insufficient.");
            return owner;
        }
    }
    private async ValueTask RequireActorAsync(TaskExecutionOwnerBinding binding, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var expected = new AuthenticatedResourceActor(binding.ActorId, binding.ProfileId, binding.AccountId, binding.OrganisationId, binding.AuthenticationRevision);
        var originalActor = RunDelegationCallback(() => _actors.GetCurrentAsync(token).AsTask(), token); // SAME ValueTask consumed once.
        if (await ObserveOriginalPermissionReadAsync(originalActor).ConfigureAwait(false) != expected)
            throw new UnauthorizedAccessException("Original product actor/revision is no longer current.");
    }
    private async Task<(ProviderModelDescriptor Model, ProviderConfiguration Configuration)> ReadSelectedAsync(
        ProviderModelDescriptor expected, IReadOnlyCollection<ToolCapability> requirements,
        IReadOnlyCollection<RestrictedModelCapability> restrictions, CancellationToken token)
    {
        var provider = RunDelegationCallback(() => _providers.Find(expected.ProviderId)) ?? throw new InvalidOperationException("Actual provider is unavailable.");
        var originalConfiguration = RunDelegationCallback(() => _configurations.GetAsync(expected.ProviderId, token), token);
        var configuration = await ObserveOriginalPermissionReadAsync(originalConfiguration).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Actual provider configuration is unavailable.");
        if (!configuration.IsEnabled || configuration.Id != provider.Id || configuration.IsLocal != provider.IsLocal ||
            expected.IsLocal != provider.IsLocal || RuntimeSafetyState.IsSafeMode && !provider.IsLocal ||
            _privacy.Current.LocalOnlyMode && !provider.IsLocal)
            throw new UnauthorizedAccessException("Provider locality/configuration/privacy admission denied.");
        var originalCatalogue = RunDelegationCallback(() => _providers.GetModelsAsync(new ModelCataloguePolicy(AllowLocal: expected.IsLocal,
            AllowRemote: !expected.IsLocal, AllowedProviderIds: new[] { expected.ProviderId }.ToFrozenSet(StringComparer.Ordinal)), token), token);
        var models = await ObserveOriginalPermissionReadAsync(originalCatalogue).ConfigureAwait(false);
        var matches = models.Where(model => model.ProviderId == expected.ProviderId && model.Name == expected.Name).Take(2).ToArray();
        if (matches.Length != 1 || matches[0].IsLocal != expected.IsLocal || requirements.Any(capability => !matches[0].Supports(capability)))
            throw new UnauthorizedAccessException("Selected model/capabilities are not current in the actual provider catalogue.");
        foreach (var capability in restrictions)
            if (!(await ObserveOriginalPermissionReadAsync(RunDelegationCallback(() => _modelPermissions.EvaluateAsync(matches[0], capability, acrossMesh: false, cancellationToken: token), token)).ConfigureAwait(false)).Allowed)
                throw new UnauthorizedAccessException("Current central model permission policy denies a required typed capability.");
        token.ThrowIfCancellationRequested();
        return (matches[0], configuration);
    }
    private async Task RevalidateSelectionAsync(Owner owner, Selection selection, CancellationToken token)
    {
        OwnerActivation activation;
        lock (_sync)
        {
            activation = owner.Activation;
            DemandSameActivation(owner, activation);
        }
        await RequireActorAsync(activation.Binding, token).ConfigureAwait(false);
        var required = selection.Candidate.RequiredCapabilities.Select(value => Enum.Parse<ToolCapability>(value, ignoreCase: false)).ToArray();
        var actual = await ReadSelectedAsync(selection.Model, required, selection.Restrictions, token).ConfigureAwait(false);
        if (ModelFingerprint(actual.Model) != selection.ModelFingerprint || ConfigurationFingerprint(actual.Configuration) != selection.ConfigurationFingerprint)
            throw new UnauthorizedAccessException("Actual provider/model/configuration changed after route capture.");
        if (selection.ConfiguredObservation is { } configured)
            await ObserveOriginalPermissionReadAsync(RunDelegationCallback(() => _routes!.DemandOriginalCurrentAsync(selection.OriginalSnapshot, configured, token).AsTask(), token)).ConfigureAwait(false);
        var first = owner.First ?? throw new UnauthorizedAccessException("Original selected route missing.");
        if (first.Model.IsLocal && !selection.Model.IsLocal)
        {
            var originalConfigurationTask = RunDelegationCallback(() => _configurations.GetAsync(first.Model.ProviderId, token), token);
            var originalConfiguration = await ObserveOriginalPermissionReadAsync(originalConfigurationTask).ConfigureAwait(false);
            if (originalConfiguration is null || !originalConfiguration.IsEnabled || !originalConfiguration.AllowCloudFallback ||
                _privacy.Current.LocalOnlyMode) throw new UnauthorizedAccessException("Original local selection does not allow cloud fallback.");
        }
        await RequireActorAsync(activation.Binding, token).ConfigureAwait(false);
        lock (_sync) DemandSameActivation(owner, activation);
    }

    private void DemandSameActivation(Owner owner, OwnerActivation activation)
    {
        if (activation.IsSealed || !ReferenceEquals(owner.Activation, activation) ||
            !ReferenceEquals(_owners.GetValueOrDefault(activation.Binding.TaskId), owner))
            throw new UnauthorizedAccessException("The original Task owner activation is no longer current.");
    }

    // Capture the actual returned child task once. Faulted OCE remains a fault payload;
    // genuinely canceled tasks retain their ordinary cancellation semantics. No Flatten and
    // no claim to reconstruct causes an upstream producer discarded before returning its task.
    private static async Task<T> ObserveOriginalPermissionReadAsync<T>(Task<T> original)
    {
        ArgumentNullException.ThrowIfNull(original);
        try { return await original.ConfigureAwait(false); }
        catch (Exception) when (original.IsFaulted)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(original.Exception!).Throw(); throw; }
    }
    private static async Task ObserveOriginalPermissionReadAsync(Task original)
    {
        ArgumentNullException.ThrowIfNull(original);
        try { await original.ConfigureAwait(false); }
        catch (Exception) when (original.IsFaulted)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(original.Exception!).Throw(); throw; }
    }

    private sealed class Lease(TaskRunPermissionAuthority issuer, Owner owner, Selection selection, Guid attemptId,
        ITaskRunCloudAdmissionLease? cloud) : ITaskRunAdmissionCommitLease
    {
        private readonly object _sync = new();
        private readonly SemaphoreSlim _commit = new(1, 1);
        private bool _closing;
        private Task? _close;
        private readonly Owner _originalOwner = owner;
        private readonly OwnerActivation _activation = owner.Activation;
        private readonly TaskExecutionOwnerBinding _originalBinding = owner.Binding;
        public TaskExecutionOwnerBinding Owner => _originalBinding; // Never rewrites old attempt/audit identity.
        private void DemandCurrentActivation()
        { lock (issuer._sync) issuer.DemandSameActivation(_originalOwner, _activation); }
        public Guid AttemptId { get; } = attemptId;
        public TaskRunRouteCandidate Candidate => selection.Candidate;
        internal ProviderConfiguration OriginalCapturedRouteConfiguration => selection.Configuration;
        internal void DemandOriginalToolAdmission()
        {
            lock (_sync) if (_closing) throw new UnauthorizedAccessException("Original typed tool lease retired.");
        }
        public string ReceiptReference { get; } = "task-attempt:" + Guid.NewGuid().ToString("N");
        public async ValueTask RevalidateAsync(CancellationToken token)
        {
            DemandCurrentActivation();
            lock (_sync) if (_closing) throw new UnauthorizedAccessException("Original attempt retired.");
            await issuer.DemandDelegatedParentAsync(_originalOwner, token).ConfigureAwait(false);
            await issuer.RevalidateSelectionAsync(_originalOwner, selection, token).ConfigureAwait(false);
            if (cloud is not null)
                await ObserveOriginalPermissionReadAsync(issuer.RunDelegationCallback(() => cloud.RevalidateAsync(token).AsTask(), token)).ConfigureAwait(false);
            await issuer.DemandDelegatedParentAsync(_originalOwner, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            DemandCurrentActivation();
            lock (_sync) if (_closing) throw new UnauthorizedAccessException("Original attempt retired during policy read.");
        }
        public ValueTask<IAsyncDisposable?> AcquireOriginalCommitPinAsync(CancellationToken token)
        {
            DemandExternalClose(); // A cloud-close callback cannot wait for its own held retirement gate.
            return AcquireOriginalCommitPinBodyAsync(token);
        }
        private async ValueTask<IAsyncDisposable?> AcquireOriginalCommitPinBodyAsync(CancellationToken token)
        {
            // Raw lifetime only: NO provider/Home/Files/SQLite/Context read under owner transaction.
            await _commit.WaitAsync(token).ConfigureAwait(false);
            lock (issuer._sync)
            lock (_sync)
            {
                if (_closing || _activation.IsSealed || !ReferenceEquals(_originalOwner.Activation, _activation) ||
                    !ReferenceEquals(issuer._owners.GetValueOrDefault(_originalBinding.TaskId), _originalOwner))
                { _commit.Release(); return null; }
                return new Pin(_commit);
            }
        }
        private static readonly AsyncLocal<LeaseClosePhase?> CloseExecuting = new();
        private readonly List<Task> _originalCloseStages = [];
        [ThreadStatic] private static List<LeaseClosePhase>? _closePhysical;
        public ValueTask DisposeAsync()
        {
            DemandExternalClose(); TaskCompletionSource? start = null; Task actual;
            var parent = _closePhysical?.LastOrDefault() ?? CloseExecuting.Value; // Capture actual live ancestry before the gate/callbacks.
            lock (_sync)
            {
                if (_close is null)
                {
                    _closing = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _close = ClosePublishedAsync(start.Task, parent); // SAME actual whole driver, before callbacks.
                }
                actual = _close;
            }
            start?.SetResult(); return new(actual);
        }
        private async Task ClosePublishedAsync(Task start, LeaseClosePhase? parent)
        {
            await start.ConfigureAwait(false);
            var previous = CloseExecuting.Value; var phase = new LeaseClosePhase(this, _close!, parent); CloseExecuting.Value = phase;
            var errors = new List<Exception>(); var acquired = false; Task? originalWait = null, originalClose = null;
            try
            {
                try
                {
                    originalWait = _commit.WaitAsync(); _originalCloseStages.Add(originalWait);
                    await JoinCloseStageAsync(originalWait, errors).ConfigureAwait(false);
                    acquired = originalWait.IsCompletedSuccessfully;
                }
                catch (Exception error) { AddCloseCause(errors, error); }
                if (acquired && cloud is not null)
                {
                    try
                    {
                        var calls = _closePhysical ??= []; calls.Add(phase);
                        try { originalClose = cloud.DisposeAsync().AsTask(); _originalCloseStages.Add(originalClose); }
                        finally { calls.RemoveAt(calls.Count - 1); }
                    }
                    catch (Exception error) { AddCloseCause(errors, error); }
                    if (originalClose is not null) await JoinCloseStageAsync(originalClose, errors).ConfigureAwait(false);
                }
            }
            finally
            {
                if (acquired) try { _commit.Release(); } catch (Exception error) { AddCloseCause(errors, error); }
                phase.Retire(); CloseExecuting.Value = previous;
            }
            foreach (var actualOriginal in _originalCloseStages)
                await JoinCloseStageAsync(actualOriginal, errors).ConfigureAwait(false);
            if (errors.Count == 1 && errors[0] is not OperationCanceledException)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            if (errors.Count != 0) throw new AggregateException("Original attempt lease close faults/cancellation/cleanup.", errors);
        }
        private void DemandExternalClose()
        {
            for (var phase = CloseExecuting.Value; phase is not null; phase = phase.Parent)
                if (phase.Live && ReferenceEquals(phase.Owner, this)) throw new InvalidOperationException("An actual attempt lease close original cannot join itself.");
            if (_closePhysical is { } physical)
                foreach (var call in physical)
                    for (var phase = call; phase is not null; phase = phase.Parent)
                        if (phase.Live && ReferenceEquals(phase.Owner, this))
                            throw new InvalidOperationException("An attempt lease close callback cannot join its containing original/ancestor.");
        }
        private sealed class LeaseClosePhase(Lease owner, Task original, LeaseClosePhase? parent)
        {
            public Lease Owner { get; } = owner;
            public Task Original { get; } = original;
            public LeaseClosePhase? Parent { get; } = parent;
            private int _active = 1;
            public bool Live => Volatile.Read(ref _active) != 0 || !Original.IsCompleted;
            public void Retire() => Interlocked.Exchange(ref _active, 0);
        }
        private static void AddCloseCause(List<Exception> errors, Exception actual)
        { if (!errors.Any(error => ReferenceEquals(error, actual))) errors.Add(actual); }
        private static async Task JoinCloseStageAsync(Task actual, List<Exception> errors)
        {
            try { await actual.ConfigureAwait(false); }
            catch (Exception observed)
            {
                AddCloseCause(errors, observed);
                if (actual.Exception is { } group)
                { AddCloseCause(errors, group); foreach (var cause in group.InnerExceptions) AddCloseCause(errors, cause); }
            }
        }
        private sealed class Pin(SemaphoreSlim gate) : IAsyncDisposable
        {
            private int _released;
            public ValueTask DisposeAsync() { if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release(); return ValueTask.CompletedTask; }
        }
    }
    private static bool ValidActor(AuthenticatedResourceActor actor) => !string.IsNullOrWhiteSpace(actor.ActorId) &&
        !string.IsNullOrWhiteSpace(actor.ProfileId) && !string.IsNullOrWhiteSpace(actor.AuthenticationRevision) &&
        actor.AccountId != Guid.Empty && actor.OrganisationId != Guid.Empty && (actor.OrganisationId is null || actor.AccountId is not null);
    private static ProviderModelDescriptor DetachModel(ProviderModelDescriptor model) => model with
    { Model = model.Model with { Capabilities = model.Capabilities.ToFrozenSet() } };
    private static ProviderConfiguration DetachConfiguration(ProviderConfiguration configuration) => configuration with
    { Metadata = configuration.Metadata.ToFrozenDictionary(StringComparer.Ordinal) };
    private static string CandidateKey(TaskRunRouteCandidate candidate) => JsonSerializer.Serialize(candidate);
    private static string ModelFingerprint(ProviderModelDescriptor model) => Digest(JsonSerializer.SerializeToElement(new
    { model.ProviderId, model.IsLocal, model.Name, model.Model.SizeBytes, model.Model.Family, model.Model.ParameterSize,
      model.Model.Quantization, model.ContextWindow, model.DisplayName,
      Capabilities = model.Capabilities.Order().ToArray() }));
    private static string ConfigurationFingerprint(ProviderConfiguration configuration) => Digest(JsonSerializer.SerializeToElement(new
    { configuration.Id, configuration.Kind, configuration.Endpoint, configuration.IsEnabled, configuration.IsLocal,
      configuration.AllowCloudFallback, configuration.UpdatedAt, Metadata = configuration.Metadata.OrderBy(value => value.Key, StringComparer.Ordinal).ToArray() }));
    private static string Digest(JsonElement json) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(json)));
}
