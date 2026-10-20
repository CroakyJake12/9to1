using Haven.Application;
using Haven.Core;
using System.Collections.Frozen;

namespace Dulche.Runtime;

/// <summary>Raw local transport only. Must be wrapped by the SAME existing ManagedProviderDulcheAdapter,
/// coordinator/frame/context authority before canonical dispatch. It never runs tools or selects fallback.
/// It owns the shared actual-worker surface (`IStrataNativeWorker`) so the same composition can drive the
/// V1 legacy worker or the guarded V2 candidate worker; the candidate path is not composition-selected
/// in this source-only checkpoint (no version>=1 acceptance).</summary>
public sealed class StrataRawModelProvider : IModelProvider, IAsyncDisposable, IOriginalInferenceEngineModelSource
{
    private readonly IStrataNativeWorker _worker;
    private readonly ProviderModelDescriptor _model;
    public StrataRawModelProvider(string sameConfiguredProviderId,IStrataNativeWorker actualLoadedWorker,
        ProviderModelDescriptor originalDescriptor)
    {
        _worker=actualLoadedWorker??throw new ArgumentNullException(nameof(actualLoadedWorker));
        if(string.IsNullOrWhiteSpace(sameConfiguredProviderId)||originalDescriptor.ProviderId!=sameConfiguredProviderId
            ||!originalDescriptor.IsLocal||originalDescriptor.Name!=_worker.Model.Model.ModelId
            ||originalDescriptor.Model.Capabilities.Any(value=>value is not (ToolCapability.Text or ToolCapability.Streaming or ToolCapability.Tools))
            ||originalDescriptor.Model.Supports(ToolCapability.Tools)&&!_worker.OriginalStructuredToolsAvailable)
            throw new ArgumentException("The current native raw provider requires its SAME local model and actual observed tool capability.",nameof(originalDescriptor));
        Id=sameConfiguredProviderId; _model=originalDescriptor with { Model=originalDescriptor.Model with {
            Capabilities=originalDescriptor.Model.Capabilities.ToFrozenSet() } };
    }
    public string Id { get; }
    public string DisplayName=>"Strata (bundled Dulche engine)";
    // Existing persisted provider kinds stay stable. This is protocol compatibility, not remote/local authority.
    public ModelProviderKind Kind=>ModelProviderKind.OpenAICompatible;
    public bool IsLocal=>true; // The transport is the actual child process, with no remote URL or proxy.
    public bool CanManageModels=>false;
    public Task<OperationResult<Unit>> ObserveOriginalInitializedModelAsync(ModelIdentity sameModel,CancellationToken cancellationToken)
        =>_worker.ObserveOriginalInitializedModelAsync(sameModel,cancellationToken);
    public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken cancellationToken)=>_worker.CheckOriginalHealthAsync(Id,cancellationToken);
    public async Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken)
    {
        if (!(await CheckHealthAsync(cancellationToken).ConfigureAwait(false)).IsHealthy) return [];
        if (_model.Supports(ToolCapability.Tools) && !_worker.OriginalStructuredToolsAvailable)
            return [_model with { Model = _model.Model with { Capabilities = _model.Model.Capabilities
                .Where(value => value != ToolCapability.Tools).ToFrozenSet() } }];
        return [_model];
    }
    public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request,CancellationToken cancellationToken)
        =>_worker.StreamOriginalAsync(request,cancellationToken);
    public Task<string> CompleteAsync(OllamaChatRequest request,CancellationToken cancellationToken)
        =>_worker.CompleteOriginalAsync(request,cancellationToken);
    public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request,CancellationToken cancellationToken)
        =>_worker.ToolsOriginalAsync(request,cancellationToken);
    public ValueTask DisposeAsync()=>_worker.DisposeAsync();
}
