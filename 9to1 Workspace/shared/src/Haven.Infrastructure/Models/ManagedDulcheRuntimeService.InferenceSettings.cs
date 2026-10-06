using Dulche.Runtime;
using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>Borrowed settings for the SAME privately retained admitted endpoint. A preference
/// or diagnostic conveys no model permission, installed binary, residency or readiness grant.</summary>
public interface IManagedDulcheOriginalInferenceSettingsSource
{
    Task<OperationResult<InferenceEngineSettings>> GetOriginalInferenceEngineSettingsAsync(
        TaskRunAttemptAdmission sameAdmission, ModelIdentity sameModel, CancellationToken cancellationToken = default);
}

public sealed partial class ManagedDulcheRuntimeService : IManagedDulcheOriginalInferenceSettingsSource
{
    private InferenceEngine CaptureOriginalEnginePreference(ModelIdentity sameModel)
    {
        try
        {
            return InvokePhysical(() => {
                lock (_sync) { RequireOpen(); _ownerStop.Token.ThrowIfCancellationRequested(); }
                var requested = _inferencePreferences?.GetRequestedInferenceEngine(sameModel) ?? InferenceEngine.Automatic;
                lock (_sync) { RequireOpen(); _ownerStop.Token.ThrowIfCancellationRequested(); }
                if (!Enum.IsDefined(requested)) throw new InferenceEngineException(new(
                    DulcheErrorCode.InvalidArgument, "The original configured engine preference is invalid.", sameModel.StableKey, false));
                return requested;
            });
        }
        catch (OperationCanceledException cause)
        { throw new AggregateException("The original preference source faulted synchronously.", cause); }
    }

    private void ApplyOriginalEnginePreference(OriginalInferenceEngineLease lease, InferenceEngine requested, ModelIdentity sameModel)
    {
        InvokePhysical(() => {
            lock (_sync) { RequireOpen(); _ownerStop.Token.ThrowIfCancellationRequested(); }
            if (lease.Adapter is InferenceEngineDispatcher dispatcher)
            {
                var result = dispatcher.SetInferenceEngine(requested);
                if (!result.Succeeded) throw new InferenceEngineException(result.Error!);
            }
            else if (requested != InferenceEngine.Automatic)
                throw new InferenceEngineException(new(DulcheErrorCode.UnsupportedCapability,
                    "The actual composed adapter has no engine selection source.", sameModel.StableKey, false));
            // Default ordinary adapters retain their original startup behavior.
            return true;
        });
    }

    private Task AcquireOriginalSettingsValidation(Func<Task> factory)
    {
        try { return InvokePhysical(() => {
            var actual = factory() ?? throw new InvalidOperationException("No actual original settings validation Task was returned.");
            RetainOriginalModelSource(actual); return actual;
        }); }
        catch (OperationCanceledException cause)
        { throw new AggregateException("The original settings validation factory faulted synchronously.", cause); }
    }

    public Task<OperationResult<InferenceEngineSettings>> GetOriginalInferenceEngineSettingsAsync(
        TaskRunAttemptAdmission sameAdmission, ModelIdentity sameModel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sameAdmission); ArgumentNullException.ThrowIfNull(sameModel);
        cancellationToken.ThrowIfCancellationRequested();
        TaskCompletionSource start; Task<OperationResult<InferenceEngineSettings>> original;
        lock (_sync)
        {
            RequireIndependentClose(); RequireOpen();
            if (_originalWork.Count >= RetainedWorkCapacity) throw new InvalidOperationException("Original settings source custody is full.");
            start = Signal();
            original = RunOriginalAsync(start.Task, () => ObserveOriginalInferenceSettingsAsync(sameAdmission, sameModel));
            _originalWork.Add(original); // Publish the encompassing producer before any source callback.
        }
        start.SetResult();
        return cancellationToken.CanBeCanceled ? original.WaitAsync(cancellationToken) : original;
    }

    private async Task<OperationResult<InferenceEngineSettings>> ObserveOriginalInferenceSettingsAsync(
        TaskRunAttemptAdmission admission, ModelIdentity sameModel)
    {
        SelectedRequestEndpoint? selected; DulcheRuntime? runtime; DulcheEndpointStartupOriginal? startup;
        OriginalInferenceEngineLease? lease; bool explicitModel;
        lock (_sync)
        {
            RequireOpen();
            selected = _selectedRequestEndpoints.FirstOrDefault(item => ReferenceEquals(item.Admission, admission));
            explicitModel = selected is null && ReferenceEquals(_modelUseAdmission, admission) && _inferenceModel == sameModel;
            if (selected is not null && selected.Model != sameModel) selected = null;
            runtime = selected?.Runtime ?? (explicitModel ? _runtime : null);
            startup = selected?.Startup ?? (explicitModel ? _startup : null);
            lease = selected?.Lease ?? (explicitModel ? _inferenceLease : null);
        }
        if (runtime is null || startup is not { WasAcquired: true } || lease?.Adapter is not InferenceEngineDispatcher)
            return OperationResult<InferenceEngineSettings>.Failure(new(DulcheErrorCode.ProviderUnavailable,
                "No SAME privately retained model-bearing dispatcher endpoint exists.", sameModel.StableKey, false));
        // Failed/pending initialization is observable as a diagnostic, never promoted to Ready.
        if (selected is not null) await ValidateSelectedRequestAdmissionAsync(selected).ConfigureAwait(false);
        else await ValidateOriginalModelUseAsync(admission).ConfigureAwait(false);
        var permission = admission.Lease as ITaskRunOriginalInferenceLeaseSource
            ?? throw new InferenceEngineException(new(DulcheErrorCode.UnsupportedCapability,
                "No SAME scoped original lease validation source exists.", sameModel.StableKey, false));
        var validation = AcquireOriginalSettingsValidation(() => permission.RevalidateOriginalInferenceWithinSourceAsync(admission,
            callback => InvokePhysical(() => { callback(); return true; }), RetainOriginalModelSource, _ownerStop.Token));
        try { await validation.ConfigureAwait(false); }
        catch (Exception cause) { ThrowObservedModelTask(cause, validation); throw; }
        return InvokePhysical(() => {
            lock (_sync)
            {
                RequireOpen(); _ownerStop.Token.ThrowIfCancellationRequested();
                var current = selected is not null
                    ? _selectedRequestEndpoints.Any(item => ReferenceEquals(item, selected)) && selected.Model == sameModel
                        && ReferenceEquals(selected.Runtime, runtime) && ReferenceEquals(selected.Startup, startup) && ReferenceEquals(selected.Lease, lease)
                    : ReferenceEquals(_modelUseAdmission, admission) && _inferenceModel == sameModel
                        && ReferenceEquals(_runtime, runtime) && ReferenceEquals(_startup, startup) && ReferenceEquals(_inferenceLease, lease);
                if (!current) throw new UnauthorizedAccessException("The original settings endpoint was replaced.");
                var live = admission.Lease as ITaskRunOriginalInferenceLeaseCurrentnessSource
                    ?? throw new InferenceEngineException(new(DulcheErrorCode.UnsupportedCapability,
                        "No SAME original lease liveness source exists.", sameModel.StableKey, false));
                live.DemandOriginalInferenceWithinSource(admission);
                lease.DemandExternalOriginalJoin();
                runtime.RequireIndependentOriginalEndpointJoin(startup.OriginalAcquisition.EndpointId);
                return OperationResult<InferenceEngineSettings>.Success(new(runtime, startup.OriginalAcquisition.EndpointId));
            }
        });
    }
}
