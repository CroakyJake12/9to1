using Haven.Application;

namespace Dulche.Runtime;

/// <summary>Optional raw transport of the SAME initialized selected lease. The existing caller
/// keeps its original routing frame, context, permissions and tool executor; no internal Dulche
/// tool dispatch or replacement request/TaskRun is implied by this port.</summary>
public interface IOriginalInferenceEngineRequestLease
{ IOriginalInferenceEngineRequestSource OriginalRequestSource { get; } }

/// <summary>Bound to an actual original endpoint and frozen selected lease. The trusted source
/// validates SAME coordinator-issued admission/acknowledged response action/current model and
/// wire context before each external finite factory. Every returned raw Task/once-converted
/// ValueTask is retained inside originalScope before exit and independently joined after faults.
/// Complete/Tools return the exact retained provider Tasks when possible; Stream retains actual
/// consumer Move/Dispose originals. Missing actual Text/Streaming/Tools proof refuses before
/// generation. The request includes its unchanged messages/options/images/tool history.</summary>
public interface IOriginalInferenceEngineRequestSource
{
    Task<string> CompleteOriginalAsync(OllamaChatRequest sameRequest,TaskRunAttemptAdmission sameAdmission,
        Guid acknowledgedResponseAction,IInferenceEngineOriginalSourceScope originalScope,CancellationToken cancellationToken);
    IAsyncEnumerable<string> StreamOriginalAsync(OllamaChatRequest sameRequest,TaskRunAttemptAdmission sameAdmission,
        Guid acknowledgedResponseAction,IInferenceEngineOriginalSourceScope originalScope,CancellationToken cancellationToken);
    Task<OllamaToolResponse> ToolsOriginalAsync(OllamaToolRequest sameRequest,TaskRunAttemptAdmission sameAdmission,
        Guid acknowledgedResponseAction,IInferenceEngineOriginalSourceScope originalScope,CancellationToken cancellationToken);
}
