using Haven.Core;
namespace Haven.Application;

public sealed partial class TaskRunPermissionAuthority : ITaskRunOriginalResponseAdmissionSource
{
    private (Owner Owner, OwnerActivation Activation, Selection Selection, Lease Lease) RequireResponseSelection(TaskRunAttemptAdmission admission)
    {
        var owner = RequireOwner(admission.Snapshot);
        if (admission.Lease is not Lease lease || lease.AttemptId != admission.AttemptId ||
            !ReferenceEquals(owner.Attempts.GetValueOrDefault(admission.AttemptId), lease) || lease.Owner != admission.Snapshot.OwnerBinding)
            throw new UnauthorizedAccessException("SAME privately issued response attempt is required.");
        if (admission.Snapshot.ParentDelegation is not null)
            throw new NotSupportedException("Response validation needs the original scoped delegated-parent producer.");
        lease.DemandOriginalToolAdmission(); DemandSameActivation(owner, owner.Activation);
        var selection = owner.Selections.GetValueOrDefault(CandidateKey(lease.Candidate))
            ?? throw new UnauthorizedAccessException("The original response route selection is absent.");
        if (!ReferenceEquals(selection.Configuration, lease.OriginalCapturedRouteConfiguration))
            throw new UnauthorizedAccessException("The original captured route selection differs.");
        return (owner, owner.Activation, selection, lease);
    }
    public void DemandOriginalResponseAdmission(TaskRunAttemptAdmission sameAdmission)
    { ArgumentNullException.ThrowIfNull(sameAdmission); lock (_sync) _ = RequireResponseSelection(sameAdmission); }

    public Task ValidateOriginalResponseAdmissionAsync(TaskRunAttemptAdmission sameAdmission,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        DemandOriginalResponseAdmission(sameAdmission);
        return StartRenewalWork<object?>(null, token, async work =>
        {
            // The SAME authority owns this whole driver before any external finite callback.
            // Response-only adapter conserves a factory/phase refusal even when the caller
            // swallows or replaces it. The inherited physical guard covers this WHOLE caller.
            var callbacks = new TaskRunOriginalResponseSourceCallbacks(originalSynchronousScope, retainOriginalTask);
            var source = new InferenceAdmissionCallbacks(this, work, callbacks.Run, retainOriginalTask);
            try
            {
                (Owner Owner, OwnerActivation Activation, Selection Selection, Lease Lease) retained;
                lock (_sync) retained = RequireResponseSelection(sameAdmission);
                var binding = retained.Activation.Binding;
                var actor = new AuthenticatedResourceActor(binding.ActorId, binding.ProfileId,
                    binding.AccountId, binding.OrganisationId, binding.AuthenticationRevision);
                if (await source.ReadAsync(() => _actors.GetCurrentAsync(token).AsTask()).ConfigureAwait(false) != actor)
                    throw new UnauthorizedAccessException("The actual response actor changed.");
                var selected = retained.Selection;
                var provider = source.Invoke(() => _providers.Find(selected.Model.ProviderId))
                    ?? throw new InvalidOperationException("The selected response provider is unavailable.");
                var configuration = await source.ReadAsync(() => _configurations.GetAsync(provider.Id, token)).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The selected response configuration is absent.");
                if (source.Invoke(() => !configuration.IsEnabled || configuration.Id != provider.Id || configuration.Kind != provider.Kind ||
                    provider.IsLocal != selected.Model.IsLocal || configuration.IsLocal != provider.IsLocal ||
                    (RuntimeSafetyState.IsSafeMode || _privacy.Current.LocalOnlyMode) && !provider.IsLocal))
                    throw new UnauthorizedAccessException("Current response provider/privacy policy refuses use.");
                var catalogue = await source.ReadAsync(() => provider.GetModelsAsync(token)).ConfigureAwait(false);
                var matches = source.Invoke(() => catalogue.Where(value => value.ProviderId == provider.Id &&
                    value.Name == selected.Model.Name && value.IsLocal == selected.Model.IsLocal).Take(2).ToArray());
                var required = selected.Candidate.RequiredCapabilities.Select(value => Enum.Parse<ToolCapability>(value, false)).ToArray();
                if (matches.Length != 1 || required.Any(value => !matches[0].Supports(value)) ||
                    ModelFingerprint(matches[0]) != selected.ModelFingerprint || ConfigurationFingerprint(configuration) != selected.ConfigurationFingerprint)
                    throw new UnauthorizedAccessException("The actual response model/configuration differs from original selection.");
                if (selected.Restrictions.Length != 0)
                {
                    var policy = await source.ReadAsync(() => _modelPermissions.GetOriginalPolicyAsync(token)).ConfigureAwait(false);
                    foreach (var restriction in selected.Restrictions)
                        if (!ModelPermissionEvaluator.Evaluate(policy, matches[0], restriction, acrossMesh: false).Allowed)
                            throw new UnauthorizedAccessException("Current central model policy denies the original response.");
                }
                if (selected.ConfiguredObservation is { } observation)
                {
                    if (_routes is not ITaskRunOriginalInferenceRouteSource routes)
                        throw new InvalidOperationException("The configured route needs its original scoped currentness producer.");
                    await source.ReadAsync(() => routes.DemandOriginalRouteWithinSourceAsync(selected.OriginalSnapshot,
                        observation, source.Run, source.Retain, token)).ConfigureAwait(false);
                }
                var first = retained.Owner.First ?? throw new UnauthorizedAccessException("The original route selection is absent.");
                if (first.Model.IsLocal && !selected.Model.IsLocal)
                {
                    var original = await source.ReadAsync(() => _configurations.GetAsync(first.Model.ProviderId, token)).ConfigureAwait(false);
                    if (original is null || !original.IsEnabled || !original.AllowCloudFallback || source.Invoke(() => _privacy.Current.LocalOnlyMode))
                        throw new UnauthorizedAccessException("The original local selection does not allow current cloud fallback.");
                }
                if (selected.Candidate.UsesCloud)
                {
                    if (retained.Lease.OriginalCloudAdmission is not ITaskRunOriginalScopedCloudAdmissionLease cloud)
                        throw new NotSupportedException("The SAME configured cloud lease needs its original scoped validation producer.");
                    await source.ReadAsync(() => cloud.RevalidateWithinOriginalSourceAsync(source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
                }
                else if (retained.Lease.OriginalCloudAdmission is not null)
                    throw new UnauthorizedAccessException("A local response may not borrow a cloud lease.");
                // Fresh actor LAST after all actual policy/provider/cloud reads.
                if (await source.ReadAsync(() => _actors.GetCurrentAsync(token).AsTask()).ConfigureAwait(false) != actor)
                    throw new UnauthorizedAccessException("The actual response actor changed after reads.");
                token.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    var current = RequireResponseSelection(sameAdmission);
                    if (!ReferenceEquals(current.Owner, retained.Owner) || !ReferenceEquals(current.Activation, retained.Activation) ||
                        !ReferenceEquals(current.Selection, retained.Selection) || !ReferenceEquals(current.Lease, retained.Lease))
                        throw new UnauthorizedAccessException("The privately issued response activation retired.");
                }
                return null;
            }
            finally { foreach (var raw in source.Originals.ToArray()) await JoinRenewalTaskAsync(raw, work.Errors).ConfigureAwait(false); }
        });
    }
}
