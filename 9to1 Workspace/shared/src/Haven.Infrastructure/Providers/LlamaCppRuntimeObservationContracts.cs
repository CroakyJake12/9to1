using Dulche.Runtime;
using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>Detached facts from the SAME provider's actual endpoint, opened GGUF and host.
/// Construction/copies never establish private issuance, residency or model-use permission.</summary>
public sealed record LlamaCppOriginalRuntimeObservation(LocalModelEndpointObservation Endpoint,
    string GgufArchitecture, uint GgufFileType, int ContextTokens, InferenceHardware Hardware);

public interface ILlamaCppOriginalRuntimeObservationSource
{
    Task<LlamaCppOriginalRuntimeObservation> ObserveOriginalRuntimeWithinSourceAsync(ModelIdentity sameModel,
        IInferenceEngineOriginalSourceScope originalScope, CancellationToken cancellationToken);
    bool IsIssuedOriginalRuntimeObservation(LlamaCppOriginalRuntimeObservation sameObservation);
}

/// <summary>Pure composition of an immutable original request. No lookup, I/O, preparation,
/// permission or endpoint is created by binding; the actual observation validates all owners.</summary>
public interface IManagedOriginalRequestRuntimeObservationSource
{
    IInferenceRuntimeObservationSource BindOriginalRequest(ModelIdentity sameModel,
        TaskRunAttemptAdmission sameAdmission, IModelProvider sameObservedProvider);
}
