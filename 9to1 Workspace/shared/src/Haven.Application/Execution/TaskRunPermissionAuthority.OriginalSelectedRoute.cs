using Haven.Core;
namespace Haven.Application;

public sealed partial class TaskRunPermissionAuthority : ITaskRunOriginalSelectedRouteCaptureSource
{
    private readonly HashSet<RenewalWork> _originalScopedRouteCaptures = [];
    public Task<TaskRunRouteCandidate> CaptureSelectedRouteWithinOriginalSourceAsync(TaskExecutionSnapshot snapshot,
        ProviderModelDescriptor actualSelection, IReadOnlyCollection<ToolCapability> requiredCapabilities,
        IReadOnlyCollection<RestrictedModelCapability> requiredRestrictedCapabilities,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(actualSelection); ArgumentNullException.ThrowIfNull(requiredCapabilities);
        ArgumentNullException.ThrowIfNull(requiredRestrictedCapabilities); ArgumentNullException.ThrowIfNull(originalSynchronousScope);
        ArgumentNullException.ThrowIfNull(retainOriginalTask);
        if (snapshot.ParentDelegation is not null || !actualSelection.IsLocal)
            throw new UnauthorizedAccessException("Original scoped fresh route capture supports direct local selection only.");
        if (_actors is not ITaskRunOriginalTaskActorObservationSource actualActors)
            throw new InvalidOperationException("The SAME configured Task actor requires its actual scoped observation leaf.");
        if (_routes is not null && _routes is not ITaskRunOriginalSelectedRouteObservationSource)
            throw new InvalidOperationException("The configured route owner lacks actual scoped original observation support.");
        var owner = RequireOwner(snapshot);
        lock (_sync)
        {
            foreach (var healthy in _originalScopedRouteCaptures.Where(work => work.Driver.IsCompletedSuccessfully &&
                work.Errors.Count == 0 && !work.CleanupIncomplete && work.Raw.All(raw => raw.IsCompletedSuccessfully)).ToArray())
            { _renewalWork.Remove(healthy); _originalScopedRouteCaptures.Remove(healthy); }
        }
        return StartRenewalWork<TaskRunRouteCandidate>(null, token, async work =>
        {
            lock (_sync) _originalScopedRouteCaptures.Add(work);
            var callback = new TaskRunOriginalResponseSourceCallbacks(originalSynchronousScope, retainOriginalTask);
            void DemandSameOwner()
            { lock (_sync) { DemandRenewalOpen(); if (!ReferenceEquals(RequireOwner(snapshot), owner)) throw new UnauthorizedAccessException("The original selection owner activation retired."); } }
            void Caller(Action body) => InvokeRenewalPhysical(() =>
            {
                callback.Run(() => { DemandSameOwner(); body(); DemandSameOwner(); });
                DemandSameOwner(); return true;
            });
            var source = new InferenceAdmissionCallbacks(this, work, Caller, retainOriginalTask);
            try
            {
                var expectedActor = new AuthenticatedResourceActor(owner.Binding.ActorId, owner.Binding.ProfileId,
                    owner.Binding.AccountId, owner.Binding.OrganisationId, owner.Binding.AuthenticationRevision);
                if (await source.ReadAsync(() => actualActors.GetOriginalCurrentWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false) != expectedActor)
                    throw new UnauthorizedAccessException("The current actual Task actor differs from the privately issued owner.");
                var required = source.Invoke(() => requiredCapabilities.Distinct().Order().ToArray());
                var restrictions = source.Invoke(() => requiredRestrictedCapabilities.Distinct().Order().ToArray());
                if (required.Length > 128 || restrictions.Length > 128 || required.Any(value => !Enum.IsDefined(value)) ||
                    restrictions.Any(value => !Enum.IsDefined(value))) throw new ArgumentException("Registered capability requirements are invalid.");
                var provider = source.Invoke(() => _providers.Find(actualSelection.ProviderId))
                    ?? throw new InvalidOperationException("Actual provider is unavailable.");
                var configuration = await source.ReadAsync(() => _configurations.GetAsync(actualSelection.ProviderId, token)).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Actual provider configuration is unavailable.");
                if (source.Invoke(() => !configuration.IsEnabled || configuration.Id != provider.Id ||
                    configuration.IsLocal != provider.IsLocal || actualSelection.IsLocal != provider.IsLocal ||
                    !provider.IsLocal || (RuntimeSafetyState.IsSafeMode || _privacy.Current.LocalOnlyMode) && !provider.IsLocal))
                    throw new UnauthorizedAccessException("Provider locality/configuration/privacy admission denied.");
                // SAME selected provider raw Task, not the tolerant registry catalogue proxy.
                if (provider is not ITaskRunOriginalProviderCatalogueSource originalCatalogue)
                    throw new InvalidOperationException("The SAME selected provider lacks its actual scoped catalogue producer.");
                var models = await source.ReadAsync(() => originalCatalogue.GetModelsWithinOriginalTaskSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
                var matches = source.Invoke(() => models.Where(model => model.ProviderId == actualSelection.ProviderId &&
                    model.Name == actualSelection.Name).Take(2).ToArray());
                if (matches.Length != 1 || !matches[0].IsLocal || required.Any(capability => !matches[0].Supports(capability)))
                    throw new UnauthorizedAccessException("Selected model/capabilities are not current in the actual provider catalogue.");
                if (restrictions.Length != 0)
                {
                    var policy = await source.ReadAsync(() => _modelPermissions.GetOriginalPolicyAsync(token)).ConfigureAwait(false);
                    foreach (var capability in restrictions)
                        if (!ModelPermissionEvaluator.Evaluate(policy, matches[0], capability, acrossMesh: false).Allowed)
                            throw new UnauthorizedAccessException("Current central model policy denies a required typed capability.");
                }
                var model = matches[0]; var names = required.Select(value => value.ToString()).ToArray();
                TaskRunRouteCandidate? observed = null;
                if (_routes is ITaskRunOriginalSelectedRouteObservationSource actualRoutes)
                    observed = await source.ReadAsync(() => actualRoutes.ObserveOriginalWithinSourceAsync(snapshot, model,
                        Array.AsReadOnly(names), source.Run, source.Retain, token)).ConfigureAwait(false);
                Selection? first; lock (_sync) first = owner.First;
                var routeId = first?.Candidate.RouteId ?? observed?.RouteId ?? "task-selected:" + snapshot.TaskId.ToString("D");
                var routeRevision = first?.Candidate.RouteRevision ?? observed?.RouteRevision ?? 1;
                if (string.IsNullOrWhiteSpace(routeId) || routeId.Length > 256 || routeRevision <= 0 ||
                    observed is not null && (observed.ProviderId != model.ProviderId || observed.ModelId != model.Name ||
                        observed.ArtifactIdentity is not null || observed.UsesCloud || !observed.RequiredCapabilities.SequenceEqual(names) ||
                        first is not null && (observed.RouteId != routeId || observed.RouteRevision != routeRevision)))
                    throw new UnauthorizedAccessException("Actual configured route observation changed the selected candidate.");
                var candidate = new TaskRunRouteCandidate(routeId, routeRevision, model.ProviderId, model.Name,
                    null, false, Array.AsReadOnly(names));
                var selection = source.Invoke(() => new Selection(candidate, DetachModel(model), DetachConfiguration(configuration),
                    ModelFingerprint(model), ConfigurationFingerprint(configuration), restrictions, snapshot, observed));
                // Fresh actual actor LAST, after every configuration/catalogue/policy/route await.
                if (await source.ReadAsync(() => actualActors.GetOriginalCurrentWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false) != expectedActor)
                    throw new UnauthorizedAccessException("The actual Task actor changed after held route reads.");
                token.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    DemandSameOwner();
                    if (owner.First is { } published && (published.Candidate.RouteId != candidate.RouteId || published.Candidate.RouteRevision != candidate.RouteRevision))
                        throw new UnauthorizedAccessException("Concurrent route capture changed the original task route identity.");
                    var key = CandidateKey(candidate);
                    if (owner.Selections.TryGetValue(key, out var old))
                    {
                        if (old.ModelFingerprint != selection.ModelFingerprint || old.ConfigurationFingerprint != selection.ConfigurationFingerprint || !old.Restrictions.SequenceEqual(restrictions))
                            throw new UnauthorizedAccessException("A retained route observation cannot be rewritten.");
                        return old.Candidate;
                    }
                    if (owner.Selections.Count == 128) throw new InvalidOperationException("Original route observation capacity exhausted.");
                    owner.Selections.Add(key, selection); owner.First ??= selection;
                }
                return candidate;
            }
            finally { foreach (var raw in source.Originals.ToArray()) await JoinRenewalTaskAsync(raw, work.Errors).ConfigureAwait(false); }
        });
    }
}
