using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed partial class ResilientProviderRoutingModelClient
{
  private async Task BindOriginalResponseActionAsync(object sameRequest, RoutingState state, CancellationToken token)
  {
    state.ResponseAcknowledgment = null;
    if (taskCoordinator is null || state.Admission is not { } admission) return;
    var source = sameRequest switch {
      OllamaChatRequest chat => taskCoordinator.TryGetOriginalResponseActionSource(chat),
      OllamaToolRequest tools => taskCoordinator.TryGetOriginalResponseActionSource(tools),
      _ => null
    };
    if (source is null) return; // Existing ordinary clients keep their original raw route.
    var current = state.CurrentSnapshot ?? throw new InvalidOperationException("No actual prepared current attempt exists.");
    Task<TaskRunOriginalResponseActionAcknowledgment>? original = null;
    void Retain(Task actual) { if (!state.OriginalResponseTasks.Any(task => ReferenceEquals(task, actual))) state.OriginalResponseTasks.Add(actual); }
    try
    {
      original = sameRequest switch {
        OllamaChatRequest chat => source.BindOriginalResponseAsync(chat, admission, current, callback => callback(), Retain, token),
        OllamaToolRequest tools => source.BindOriginalResponseAsync(tools, admission, current, callback => callback(), Retain, token),
        _ => throw new InvalidOperationException("No original typed response request exists.")
      };
      if (original is null) throw new InvalidOperationException("No actual original response registration Task was returned.");
      Retain(original);
    }
    catch (OperationCanceledException cause) { throw new AggregateException("The response registration factory faulted synchronously.", cause); }
    var acknowledged = await AwaitExactTaskAsync(original).ConfigureAwait(false);
    var valid = sameRequest switch {
      OllamaChatRequest chat => source.IsIssuedOriginalResponseActionAcknowledgment(acknowledged, chat, admission),
      OllamaToolRequest tools => source.IsIssuedOriginalResponseActionAcknowledgment(acknowledged, tools, admission),
      _ => false
    };
    if (!valid || !ReferenceEquals(acknowledged.OriginalAdmission, admission)
      || !ReferenceEquals(acknowledged.OriginalRequest, sameRequest)
      || !acknowledged.OriginalRegistration.IsCompletedSuccessfully
      || acknowledged.OriginalSources.Any(task => !task.IsCompletedSuccessfully)
      || state.Context is not { } context || context.ActionId != acknowledged.ActualActionId)
      throw new UnauthorizedAccessException("No SAME privately reserved and actually acknowledged response exists.");
    Retain(acknowledged.OriginalRegistration);
    foreach (var actual in acknowledged.OriginalSources) Retain(actual);
    var row = acknowledged.AcknowledgedSnapshot;
    if (row.TaskId != current.TaskId || row.ContextId != current.ContextId || row.ExecutionId != current.ExecutionId
      || row.OwnerBinding != admission.Lease.Owner || row.Attempts.LastOrDefault()?.Id != admission.AttemptId)
      throw new UnauthorizedAccessException("The response acknowledgment belongs to another current Task/run.");
    state.CurrentSnapshot = row; state.Context = context with { PersistenceRevision = row.PersistenceRevision };
    state.ResponseAcknowledgment = acknowledged;
  }

  private bool UseOriginalSelectedModel(RoutingState state, SelectedProvider selected) =>
    originalModelRequests is not null && selected.Descriptor is { ProviderId: "llama-cpp", IsLocal: true }
    && state.Admission is { Lease.Candidate.UsesCloud: false };
  private TaskRunOriginalResponseActionAcknowledgment RequireOriginalResponse(RoutingState state) =>
    state.ResponseAcknowledgment ?? throw new UnauthorizedAccessException("A genuine current response action source is required for typed local inference.");
  private Task<string> RawOriginalSelectedComplete(RoutingState state, OllamaChatRequest original,
    SelectedProvider selected, OllamaChatRequest routed, CancellationToken token) => UseOriginalSelectedModel(state, selected)
    ? originalModelRequests!.CompleteOriginalSelectedModelAsync(original, routed, state.Admission!, RequireOriginalResponse(state), token)
    : RawCompleteAsync(selected, routed, token);
  private IAsyncEnumerable<string> RawOriginalSelectedStream(RoutingState state, OllamaChatRequest original,
    SelectedProvider selected, OllamaChatRequest routed, CancellationToken token) => UseOriginalSelectedModel(state, selected)
    ? originalModelRequests!.StreamOriginalSelectedModelAsync(original, routed, state.Admission!, RequireOriginalResponse(state), token)
    : RawStream(selected, routed, token);
  private Task<OllamaToolResponse> RawOriginalSelectedTools(RoutingState state, OllamaToolRequest original,
    SelectedProvider selected, OllamaToolRequest routed, CancellationToken token)
  {
    if (!UseOriginalSelectedModel(state, selected)) return RawToolsWithOriginalDispatchAsync(state, selected, routed, token);
    // Old concrete-provider inspection does not certify this nested selected-engine call.
    // Preserve unknown for checkpoint effect evidence until its actual raw lineage is supplied.
    if (state.OriginalRequestFailure is { } body) lock (_originalRequestFailureSync) body.ToolDispatchCohortUnknown = true;
    return originalModelRequests!.ToolsOriginalSelectedModelAsync(original, routed, state.Admission!, RequireOriginalResponse(state), token);
  }
}
