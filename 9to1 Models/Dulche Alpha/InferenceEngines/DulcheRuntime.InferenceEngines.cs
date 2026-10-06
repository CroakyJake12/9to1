namespace Dulche.Runtime;

public sealed partial class DulcheRuntime
{
    /// <summary>Applicable to the configured local inference dispatcher; existing callers/APIs are unchanged.</summary>
    public OperationResult<Unit> setInferenceEngine(InferenceEngine engine)
    {
        var dispatchers = _adapters.Values.OfType<InferenceEngineDispatcher>().ToArray();
        if (dispatchers.Length != 1) return OperationResult<Unit>.Failure(new(DulcheErrorCode.InvalidState,
            "Exactly one configured inference runtime is required for an unscoped override.", "inferenceEngine", false));
        return dispatchers[0].SetInferenceEngine(engine);
    }
    public InferenceEngineDiagnostic getInferenceEngine(string? endpointId = null)
    {
        var dispatchers = _adapters.Values.OfType<InferenceEngineDispatcher>().ToArray();
        if (dispatchers.Length != 1) return new(InferenceEngine.Automatic, null,
            "No unique configured inference dispatcher exists.", null, null, [], []);
        return dispatchers[0].GetInferenceEngine(endpointId);
    }
    public OperationResult<Unit> SetInferenceEngine(InferenceEngine engine) => setInferenceEngine(engine);
    public InferenceEngineDiagnostic GetInferenceEngine(string? endpointId = null) => getInferenceEngine(endpointId);
}
