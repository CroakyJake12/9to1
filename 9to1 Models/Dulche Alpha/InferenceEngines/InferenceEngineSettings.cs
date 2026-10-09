namespace Dulche.Runtime;

/// <summary>Typed runtime/settings view. This borrows the existing runtime and never owns a
/// TaskRun, model permission, engine session, native process or independent fallback loop.</summary>
public sealed class InferenceEngineSettings(DulcheRuntime sameRuntime,string originalEndpointId)
{
    public InferenceEngineDiagnostic Read()=>sameRuntime.getInferenceEngine(originalEndpointId);
    public Task<OperationResult<Unit>> ApplyAsync(InferenceEngine requested,CancellationToken cancellationToken=default)
        =>sameRuntime.setInferenceEngine(requested,originalEndpointId,cancellationToken);
    public Task<OperationResult<Unit>> AutomaticAsync(CancellationToken cancellationToken=default)
        =>ApplyAsync(InferenceEngine.Automatic,cancellationToken);
    public OperationResult<Unit> PermitFallback(DulcheError sameObservedFailure)
        =>sameRuntime.PermitInferenceEngineInitializationFallback(originalEndpointId,sameObservedFailure);
}

/// <summary>Requested product configuration only. This is no model, installation, capability,
/// TaskRun or fallback permission. The existing owner validates the same admitted model and
/// captures this preference in its finite physical callback before actual startup.</summary>
public interface IInferenceEnginePreferenceSource
{
    InferenceEngine GetRequestedInferenceEngine(ModelIdentity sameModel);
}

/// <summary>Bounded configured preferences keyed by the entire model identity, including its
/// artifact revision. Missing entries request Automatic. This has no IO or inference work;
/// persisted product configuration remains the host's existing configured settings owner.</summary>
public sealed class ConfiguredInferenceEnginePreferences : IInferenceEnginePreferenceSource
{
    private readonly object _gate=new();
    private readonly Dictionary<ModelIdentity,InferenceEngine> _requested=[];
    public InferenceEngine GetRequestedInferenceEngine(ModelIdentity sameModel)
    {
        DemandIdentity(sameModel);
        lock(_gate) return _requested.GetValueOrDefault(sameModel,InferenceEngine.Automatic);
    }
    public void SetRequestedInferenceEngine(ModelIdentity sameModel,InferenceEngine requested)
    {
        DemandIdentity(sameModel);
        if(requested is not (InferenceEngine.Automatic or InferenceEngine.Strata or InferenceEngine.LlamaCpp))
            throw new ArgumentOutOfRangeException(nameof(requested));
        lock(_gate) {
            if(requested==InferenceEngine.Automatic) { _requested.Remove(sameModel);return; }
            if(_requested.Count>=256&&!_requested.ContainsKey(sameModel))
                throw new InvalidOperationException("Configured model engine preference capacity is full.");
            _requested[sameModel]=requested;
        }
    }
    private static void DemandIdentity(ModelIdentity sameModel)
    {
        ArgumentNullException.ThrowIfNull(sameModel);
        if(string.IsNullOrWhiteSpace(sameModel.ProviderId)||string.IsNullOrWhiteSpace(sameModel.ModelId))
            throw new ArgumentException("A complete configured model identity is required.",nameof(sameModel));
    }
}
