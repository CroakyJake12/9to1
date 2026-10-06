using Haven.Core;
namespace Haven.Application;

public sealed partial class TaskRunPermissionAuthority : ITaskRunOriginalInferenceAdmissionSource
{
    public void DemandOriginalInferenceAdmission(TaskRunAttemptAdmission sameAdmission)
    {
        ArgumentNullException.ThrowIfNull(sameAdmission);
        lock (_sync) _ = RequireInferenceSelection(sameAdmission);
    }

    private (Owner Owner, OwnerActivation Activation, Selection Selection) RequireInferenceSelection(TaskRunAttemptAdmission admission)
    {
        var owner = RequireOwner(admission.Snapshot);
        if (admission.Lease is not Lease lease || lease.AttemptId != admission.AttemptId ||
            !ReferenceEquals(owner.Attempts.GetValueOrDefault(admission.AttemptId), lease) ||
            lease.Owner != admission.Snapshot.OwnerBinding || lease.Candidate.UsesCloud ||
            admission.Snapshot.ParentDelegation is not null)
            throw new UnauthorizedAccessException("Native model use requires the SAME issued direct local Task attempt; delegated/cloud use requires its own scoped sources.");
        lease.DemandOriginalToolAdmission();
        var selection = owner.Selections.GetValueOrDefault(CandidateKey(lease.Candidate))
            ?? throw new UnauthorizedAccessException("The original local model selection is absent.");
        if (!selection.Model.IsLocal || !ReferenceEquals(selection.Configuration, lease.OriginalCapturedRouteConfiguration))
            throw new UnauthorizedAccessException("The SAME issuer-captured local selection is required.");
        DemandSameActivation(owner, owner.Activation);
        return (owner, owner.Activation, selection);
    }

    public Task ValidateOriginalInferenceAdmissionAsync(TaskRunAttemptAdmission sameAdmission,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        DemandOriginalInferenceAdmission(sameAdmission);
        if (_actors is not ITaskRunOriginalTaskActorObservationSource originalActors)
            throw new InvalidOperationException("The SAME configured Task actor lacks actual original scoped observation support.");
        // Existing authority request seal/drain owns this gated actual driver before callbacks.
        return StartRenewalWork<object?>(null, token, async work =>
        {
            // Reuse the maintained callback adapter: every guard/body/caller fault is
            // retained before throw, including a refusal the caller swallows or replaces.
            // The SAME authority physical boundary and raw joins remain underneath it.
            var callbacks = new TaskRunOriginalResponseSourceCallbacks(originalSynchronousScope, retainOriginalTask);
            var source = new InferenceAdmissionCallbacks(this, work, callbacks.Run, retainOriginalTask);
            try
            {
                (Owner Owner, OwnerActivation Activation, Selection Selection) retained;
                lock (_sync) retained = RequireInferenceSelection(sameAdmission);
                var binding = retained.Activation.Binding;
                var expectedActor = new AuthenticatedResourceActor(binding.ActorId, binding.ProfileId,
                    binding.AccountId, binding.OrganisationId, binding.AuthenticationRevision);
                if (await source.ReadAsync(() => originalActors.GetOriginalCurrentWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false) != expectedActor)
                    throw new UnauthorizedAccessException("Actual native model actor changed.");
                var selected = retained.Selection;
                var provider = source.Invoke(() => _providers.Find(selected.Model.ProviderId))
                    ?? throw new InvalidOperationException("The actual local model provider is unavailable.");
                var configuration = await source.ReadAsync(() => _configurations.GetAsync(provider.Id, token)).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Actual local provider configuration is absent.");
                if (source.Invoke(() => !provider.IsLocal || !configuration.IsLocal || !configuration.IsEnabled || configuration.Id != provider.Id ||
                    RuntimeSafetyState.IsSafeMode && !provider.IsLocal || _privacy.Current.LocalOnlyMode && !provider.IsLocal))
                    throw new UnauthorizedAccessException("Current local provider/privacy policy refuses native model use.");
                // Direct SAME provider Task; no catalogue proxy silently discards raw failures.
                if (provider is not ITaskRunOriginalProviderCatalogueSource originalCatalogue)
                    throw new InvalidOperationException("The SAME local provider lacks actual original scoped catalogue support.");
                var catalogue = await source.ReadAsync(() => originalCatalogue.GetModelsWithinOriginalTaskSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
                var matches = source.Invoke(() => catalogue.Where(model => model.ProviderId == provider.Id &&
                    model.Name == selected.Model.Name && model.IsLocal).Take(2).ToArray());
                var required = selected.Candidate.RequiredCapabilities.Select(value => Enum.Parse<ToolCapability>(value, false)).ToArray();
                if (matches.Length != 1 || required.Any(capability => !matches[0].Supports(capability)) ||
                    ModelFingerprint(matches[0]) != selected.ModelFingerprint || ConfigurationFingerprint(configuration) != selected.ConfigurationFingerprint)
                    throw new UnauthorizedAccessException("The current actual model/configuration differs from the issued selection.");
                if (selected.Restrictions.Length != 0)
                {
                    var policy = await source.ReadAsync(() => _modelPermissions.GetOriginalPolicyAsync(token)).ConfigureAwait(false);
                    foreach (var restriction in selected.Restrictions)
                        if (!ModelPermissionEvaluator.Evaluate(policy, matches[0], restriction, acrossMesh: false).Allowed)
                            throw new UnauthorizedAccessException("Current central model policy denies native model use.");
                }
                if (selected.ConfiguredObservation is { } observation)
                {
                    if (_routes is not ITaskRunOriginalInferenceRouteSource routes)
                        throw new InvalidOperationException("Native model use requires original scoped configured-route currentness.");
                    await source.ReadAsync(() => routes.DemandOriginalRouteWithinSourceAsync(selected.OriginalSnapshot,
                        observation, source.Run, source.Retain, token)).ConfigureAwait(false);
                }
                // Actual actor LAST after all provider/model/policy/route reads.
                if (await source.ReadAsync(() => originalActors.GetOriginalCurrentWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false) != expectedActor)
                    throw new UnauthorizedAccessException("Actual native model actor changed after held reads.");
                token.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    var current = RequireInferenceSelection(sameAdmission);
                    if (!ReferenceEquals(current.Owner, retained.Owner) || !ReferenceEquals(current.Activation, retained.Activation) ||
                        !ReferenceEquals(current.Selection, selected)) throw new UnauthorizedAccessException("The original local activation/selection retired.");
                }
                return null;
            }
            finally
            {
                // Independent original joins before the encompassing driver settles, including
                // a returned raw Task captured before the caller scope subsequently faulted.
                foreach (var raw in source.Originals.ToArray()) await JoinRenewalTaskAsync(raw, work.Errors).ConfigureAwait(false);
            }
        });
    }

    private sealed class InferenceAdmissionCallbacks(TaskRunPermissionAuthority owner, RenewalWork work,
        Action<Action> caller, Action<Task> retain)
    {
        public readonly List<Task> Originals = [];
        public void Retain(Task raw)
        {
            bool newlyCaptured;
            lock (owner._sync)
            {
                newlyCaptured = !Originals.Any(item => ReferenceEquals(item, raw));
                if (newlyCaptured) Originals.Add(raw);
                if (!work.Raw.Any(item => ReferenceEquals(item, raw))) work.Raw.Add(raw);
            }
            if (newlyCaptured) retain(raw);
        }
        public void Run(Action body) => Invoke(() => { body(); return 0; });
        public T Invoke<T>(Func<T> body) => owner.InvokeRenewalPhysical(() => InvokeScoped(body));
        private T InvokeScoped<T>(Func<T> body)
        {
            var thread = Environment.CurrentManagedThreadId; var active = 1; var used = 0; T result = default!;
            try
            {
                caller(() =>
                {
                    if (Volatile.Read(ref active) != 1 || Environment.CurrentManagedThreadId != thread || Interlocked.Exchange(ref used, 1) != 0)
                        throw new InvalidOperationException("The original native authority callback is inactive, foreign-thread or already consumed.");
                    result = body();
                });
                if (Volatile.Read(ref used) == 0) throw new InvalidOperationException("The original native authority callback was not invoked.");
                return result;
            }
            catch (OperationCanceledException error) { throw new AggregateException("Actual synchronous native authority factory fault.", error); }
            finally { Interlocked.Exchange(ref active, 0); }
        }
        public async Task<T> ReadAsync<T>(Func<Task<T>> factory)
        {
            Task<T>? actual = null; Exception? invocation = null;
            try { _ = Invoke(() => { actual = factory(); Retain(actual); return actual; }); }
            catch (Exception cause) { invocation = cause; AddRenewalCause(work.Errors, cause); }
            T result = default!;
            if (actual is not null) result = await work.AwaitAsync(actual).ConfigureAwait(false);
            if (invocation is not null) ThrowRenewalCauses(work.Errors);
            return actual is null ? throw new InvalidOperationException("No actual native authority Task was acquired.") : result;
        }
        public async Task ReadAsync(Func<Task> factory)
        {
            Task? actual = null; Exception? invocation = null;
            try { _ = Invoke(() => { actual = factory(); Retain(actual); return actual; }); }
            catch (Exception cause) { invocation = cause; AddRenewalCause(work.Errors, cause); }
            if (actual is not null) await work.AwaitAsync(actual).ConfigureAwait(false);
            if (invocation is not null) ThrowRenewalCauses(work.Errors);
            if (actual is null) throw new InvalidOperationException("No actual native authority Task was acquired.");
        }
    }
}
