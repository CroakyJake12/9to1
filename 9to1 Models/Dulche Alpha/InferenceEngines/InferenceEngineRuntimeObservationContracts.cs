namespace Dulche.Runtime;

/// <summary>Optional engine-specific observation; the requested engine is a preference,
/// never an artifact/model grant or a second routing/execution authority.</summary>
public interface IInferenceEngineRuntimeObservationSource : IInferenceRuntimeObservationSource
{
    Task<InferenceRuntimeObservation> ObserveOriginalForEngineAsync(ModelIdentity sameModel,
        InferenceEngine requestedEngine, IInferenceEngineOriginalSourceScope originalScope,
        CancellationToken cancellationToken);
}
