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
