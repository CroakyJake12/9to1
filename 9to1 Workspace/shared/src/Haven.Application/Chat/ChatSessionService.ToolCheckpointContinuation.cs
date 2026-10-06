using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public sealed partial class ChatSessionService
{
    internal (ITaskRunOriginalRequestFailureSource Failure,
        ITaskRunOriginalToolCheckpointSelectionSource Selection,
        ITaskRunOriginalToolResponseDispatchWitnessSource Dispatch) RequireOriginalToolCheckpointSources() =>
        ollama is ITaskRunOriginalRequestFailureSource failure
            && ollama is ITaskRunOriginalToolCheckpointSelectionSource selection
            && ollama is ITaskRunOriginalToolResponseDispatchWitnessSource dispatch
        ? (failure, selection, dispatch)
        : throw new InvalidOperationException("The actual configured provider caller has no original failure/settlement/local-selection/native-dispatch sources.");

    // The factory is retained only by the actual initial input producer. Its arguments
    // come from a privately issued continuation binding, never from public skip flags.
    private IAsyncEnumerable<ChatStreamEvent> CreateOriginalToolCheckpointContinuation(
        TaskRunToolCheckpointContinuationBinding original, Conversation conversation, string agentName,
        string? workspaceRoot, PermissionMode filePermission, PermissionMode commandPermission,
        PermissionMode browserPermission, CancellationToken token)
    {
        var coordinator = taskCoordinator ?? throw new InvalidOperationException("The original Task owner is unavailable.");
        coordinator.DemandOriginalToolCheckpointFactory(original, this);
        IAsyncEnumerable<ChatStreamEvent> CreateBody(CancellationToken bodyToken) => ObserveOriginalSendAsync(
            SendOriginalToolCheckpointAsync(original, conversation, agentName, workspaceRoot,
                filePermission, commandPermission, browserPermission, bodyToken), original.Next, bodyToken);
        return coordinator.RegisterOriginalCanonicalChatProducer(original.Next, CreateBody, token);
    }

    private static T InvokeOriginalCheckpointStage<T>(TaskRunInvocationCustody original, Func<T> source)
    {
        var producer = original.OriginalProcessProducer
            ?? throw new InvalidOperationException("The actual checkpoint body has no owning process producer.");
        T value = default!;
        try
        {
            producer.InvokeAdmittedOriginalCallback(() => value = source());
            return value;
        }
        catch (Exception cause)
        {
            original.Retain(cause);
            if (cause is OperationCanceledException)
                throw new AggregateException("Actual synchronous checkpoint callback fault.", cause);
            throw;
        }
    }

    private static async Task<T> AwaitOriginalCheckpointStage<T>(TaskRunInvocationCustody original,
        string stage, Func<Task<T>> source)
    {
        Task<T>? actual = null;
        try
        {
            InvokeOriginalCheckpointStage(original, () =>
            {
                actual = source() ?? throw new InvalidOperationException("The actual checkpoint source returned no Task.");
                original.RetainAdditionalOriginal(stage, actual);
                return true;
            });
            return await actual!.ConfigureAwait(false);
        }
        catch (Exception cause)
        {
            original.Retain(cause, actual);
            if (actual is { IsFaulted: true, Exception: { } fault }) throw fault;
            throw;
        }
    }

    private static async Task AwaitOriginalCheckpointStage(TaskRunInvocationCustody original,
        string stage, Func<Task> source)
    {
        Task? actual = null;
        try
        {
            InvokeOriginalCheckpointStage(original, () =>
            {
                actual = source() ?? throw new InvalidOperationException("The actual checkpoint source returned no Task.");
                original.RetainAdditionalOriginal(stage, actual);
                return true;
            });
            await actual!.ConfigureAwait(false);
        }
        catch (Exception cause)
        {
            original.Retain(cause, actual);
            if (actual is { IsFaulted: true, Exception: { } fault }) throw fault;
            throw;
        }
    }

    private async IAsyncEnumerable<ChatStreamEvent> SendOriginalToolCheckpointAsync(
        TaskRunToolCheckpointContinuationBinding binding, Conversation conversation, string agentName,
        string? workspaceRoot, PermissionMode filePermission, PermissionMode commandPermission,
        PermissionMode browserPermission,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        var coordinator = taskCoordinator ?? throw new InvalidOperationException("The canonical Task owner is unavailable.");
        var owner = taskToolOwner ?? throw new InvalidOperationException("The original typed tool owner is unavailable.");
        var capture = taskProviderContextCapture ?? throw new InvalidOperationException("The actual request-context owner is unavailable.");
        var original = binding.Next;
        var boundary = binding.Boundary;
        var selected = binding.Selection ?? throw new InvalidOperationException("No original local selection was issued.");
        var issued = binding.NewAdmission ?? throw new InvalidOperationException("No actual new same-run attempt was acknowledged.");
        var current = await AwaitOriginalCheckpointStage(original, "checkpoint.body-bind", () =>
            coordinator.BindOriginalToolCheckpointBodyAsync(binding, this, conversation.Id, token)).ConfigureAwait(false);
        CurrentCanonicalTask = current;
        var tracker = new ChatExecutionTracker(ChatExecutionStage.Recovering, null, current.ExecutionId);
        original.OriginalTracker = tracker;
        void Publish(ChatExecutionSnapshot value)
        {
            if (value.Stage == ChatExecutionStage.Completed && original.OriginalCompletion is not { IsCompletedSuccessfully: true })
            { original.DeferredCompletionPublication = () => Publish(value); return; }
            try
            {
                var producer = original.OriginalProcessProducer
                    ?? throw new InvalidOperationException("No actual checkpoint producer owns tracker publication.");
                producer.InvokeOriginalCallback(() => { CurrentExecution = value; ExecutionChanged?.Invoke(value); });
            }
            catch (OperationCanceledException cause)
            { throw new AggregateException("Actual synchronous checkpoint tracker publication fault.", cause); }
        }
        tracker.Changed += Publish;
        original.DeferredCompletionPublication = null;
        var assistantId = boundary.AssistantId;
        var buffer = new StringBuilder(boundary.AssistantText);
        var activities = boundary.Activities.ToList();
        var turns = boundary.OriginalRequest.Messages.ToList();
        var callsUsed = boundary.CallsUsed;
        var lastCall = boundary.LastCall;
        var lastResult = boundary.LastResult;
        var descriptor = selected.ActualSelectedModel;
        var requestModel = descriptor.ProviderId.Equals("ollama", StringComparison.OrdinalIgnoreCase)
            ? descriptor.Name : descriptor.Key;
        var tools = boundary.OriginalRequest.Tools;
        var previousIds = boundary.OriginalRequest.Messages.SelectMany(turn => turn.ToolCalls ?? [])
            .Where(call => !string.IsNullOrWhiteSpace(call.Id)).Select(call => call.Id!).ToHashSet(StringComparer.Ordinal);
        var runtimeByName = boundary.RuntimeByName;
        var priorCalls = boundary.OriginalRequest.Messages.SelectMany(turn => turn.ToolCalls ?? []).ToList();

        async Task<TaskExecutionSnapshot> ReadCurrent(string phase)
        {
            var row = await AwaitOriginalCheckpointStage(original, phase, () => coordinator.GetAsync(current.TaskId, token)).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The actual same-run task disappeared.");
            if (row.TaskId != binding.Acknowledged!.TaskId || row.ContextId != conversation.Id
                || row.ExecutionId != issued.Snapshot.ExecutionId || row.OwnerBinding != issued.Snapshot.OwnerBinding
                || row.Attempts.LastOrDefault()?.Id != issued.AttemptId || row.RecoveryObservation is not null
                || OriginalToolCheckpointControlFingerprint(row) != boundary.OriginalControlFingerprint
                || row.State is TaskExecutionLifecycle.Completed or TaskExecutionLifecycle.Cancelled)
                throw new InvalidOperationException("The checkpoint body cannot change its actual task, run, owner or attempt.");
            current = row; CurrentCanonicalTask = row; return row;
        }

        async Task<WorkspaceToolResult> ExecuteTool(OllamaToolCall proposed)
        {
            var call = DetachOriginalToolCall(proposed);
            if (string.IsNullOrWhiteSpace(call.Id) || !previousIds.Add(call.Id)
                || !runtimeByName.TryGetValue(call.Name, out var runtime)
                || !tools.Any(tool => tool.Name.Equals(call.Name, StringComparison.Ordinal))
                || runtime is not (ToolRuntimeKind.Workspace or ToolRuntimeKind.Mcp)
                || runtime == ToolRuntimeKind.Mcp && owner is not CanonicalWorkspaceCloudflareToolActionOwner
                || !owner.SupportsCanonicalInvocation(runtime, call.Name))
                throw new InvalidOperationException("The continued model returned an existing tool ID or a tool without its original fixed typed owner.");
            await AwaitOriginalCheckpointStage(original, "checkpoint.tool-safety", () =>
                safety.EnsureMayActAsync(conversation.Id, $"chat.tool.{call.Name}", token)).ConfigureAwait(false);
            if (ModelToolPermissionMap.Map(call.Name) is { } restricted)
            {
                if (modelPermissions is null)
                    throw new InvalidOperationException("The actual current model-tool permission source is unavailable.");
                var decision = await AwaitOriginalCheckpointStage(original, "checkpoint.model-tool-permission", () =>
                    modelPermissions.EvaluateAsync(descriptor, restricted, acrossMesh: false, token)).ConfigureAwait(false);
                if (!decision.Allowed) throw new UnauthorizedAccessException("The actual current model permission refused this tool.");
            }
            var row = await ReadCurrent("checkpoint.tool-current").ConfigureAwait(false);
            var actualAdmission = await AwaitOriginalCheckpointStage(original, "checkpoint.tool-issued-attempt", () =>
                coordinator.GetIssuedAttemptAsync(row.TaskId, row.ExecutionId, issued.AttemptId, token)).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The actual resumed attempt issuer is unavailable.");
            if (!ReferenceEquals(actualAdmission, issued))
                throw new InvalidOperationException("Another attempt cannot replace the actual checkpoint admission.");
            var actionId = Guid.NewGuid();
            var permission = ModelToolPermissionMap.Map(call.Name) switch
            {
                RestrictedModelCapability.EditFiles => filePermission,
                RestrictedModelCapability.RunCommands => commandPermission,
                _ => browserPermission
            };
            var producer = original.OriginalProcessProducer!;
            var preparation = await AwaitOriginalCheckpointStage(original, "checkpoint.tool-prepare", () =>
                owner is CanonicalWorkspaceCloudflareToolActionOwner composite
                    ? composite.PrepareCallerScopedOriginalAsync(actualAdmission, row, actionId, call, runtime,
                        permission, workspaceRoot, producer.InvokeAdmittedOriginalCallback, token)
                    : owner.PrepareOriginalAsync(actualAdmission, row, actionId, call, runtime, permission, workspaceRoot, token)).ConfigureAwait(false);
            if (!ReferenceEquals(preparation.OriginalAttempt, actualAdmission) || preparation.ActionId != actionId)
                throw new InvalidOperationException("The tool preparation did not retain the actual resumed admission/action.");
            if (original.CaptureOriginalToolOutcomes().Any(prior => prior.OriginalNode.Acceptance is not null
                    && prior.OriginalNode.OriginalToolIntent is { } intent
                    && intent.RuntimeKey == preparation.OriginalToolIntent.RuntimeKey
                    && intent.ToolName == preparation.OriginalToolIntent.ToolName
                    && intent.CanonicalWorkspaceRoot == preparation.OriginalToolIntent.CanonicalWorkspaceRoot
                    && priorCalls.Any(previous => previous.Name == call.Name
                        && SameOriginalCheckpointArguments(previous, call))))
                throw new InvalidOperationException("The continued model attempted to replay an already acknowledged original tool operation.");
            current = await AwaitOriginalCheckpointStage(original, "checkpoint.tool-intent", () =>
                coordinator.RegisterOriginalToolActionAsync(preparation, current.LastCheckpointActionId,
                    StatusForTool(call.Name), token)).ConfigureAwait(false);
            var result = await AwaitOriginalCheckpointStage(original, "checkpoint.tool-execute", () =>
                owner.ExecuteOriginalAsync(preparation, async bodyToken =>
                {
                    if (runtime == ToolRuntimeKind.Workspace)
                    {
                        if (workspaceRoot is null) throw new InvalidOperationException("The original workspace root is unavailable.");
                        if (checkpoints is not null && ModelToolPermissionMap.Map(call.Name) == RestrictedModelCapability.EditFiles)
                        {
                            var checkpoint = await AwaitOriginalCheckpointStage(original, "checkpoint.before-mutation", () =>
                                checkpoints.EnsureBeforeMutationAsync(current.ExecutionId, conversation.Id, conversation.ContainerId,
                                    workspaceRoot, checkpoints.Mode, bodyToken)).ConfigureAwait(false);
                            if (checkpoint is not null)
                                current = await AwaitOriginalCheckpointStage(original, "checkpoint.before-mutation-ack", () =>
                                    coordinator.RecordCheckpointAsync(current.TaskId, current.ExecutionId, checkpoint.Id, bodyToken)).ConfigureAwait(false);
                        }
                        return await AwaitOriginalCheckpointStage(original, "checkpoint.workspace-runtime", () =>
                            workspaceTools.ExecuteOriginalAsync(workspaceRoot, call, preparation, bodyToken,
                                conversation.Id, conversation.ContainerId)).ConfigureAwait(false);
                    }
                    var cloudflare = (CanonicalWorkspaceCloudflareToolActionOwner)owner;
                    return await AwaitOriginalCheckpointStage(original, "checkpoint.cloudflare-runtime", () =>
                        cloudflare.ExecuteOriginalCloudflareRuntimeAsync(call, preparation,
                            producer.InvokeAdmittedOriginalCallback, bodyToken)).ConfigureAwait(false);
                }, token)).ConfigureAwait(false);
            await AwaitOriginalCheckpointStage(original, "checkpoint.tool-result-validation", () =>
                owner.ValidateOriginalResultAsync(preparation, result, token).AsTask()).ConfigureAwait(false);
            current = await AwaitOriginalCheckpointStage(original, "checkpoint.tool-outcome", () =>
                coordinator.RecordOriginalToolActionOutcomeAsync(preparation, result, token)).ConfigureAwait(false);
            CurrentCanonicalTask = current;
            await AwaitOriginalCheckpointStage(original, "checkpoint.tool-retirement", () =>
                coordinator.RetireAcknowledgedToolOriginalAsync(preparation, current).AsTask()).ConfigureAwait(false);
            original.RetainOriginalToolOutcome(coordinator.CaptureOriginalToolOutcomeCustody(preparation, result, current));
            priorCalls.Add(call);
            return result.OriginalResult;
        }

        var firstOriginalResponse = true;
        while (callsUsed < boundary.ToolLimit)
        {
            token.ThrowIfCancellationRequested();
            tracker.Update(ChatExecutionStage.Thinking, "Continuing the original tool checkpoint");
            await AwaitOriginalCheckpointStage(original, "checkpoint.model-turn-safety", () =>
                safety.EnsureMayActAsync(conversation.Id, "chat.model-tool-turn", token)).ConfigureAwait(false);
            await ReadCurrent("checkpoint.provider-current").ConfigureAwait(false);
            var responseOriginal = ReserveOriginalChatResponse(original, boundary.OriginalRequest.ExecutionContext?.ActionId,
                firstOriginalResponse ? binding : null);
            firstOriginalResponse = false;
            var request = boundary.OriginalRequest with
            {
                Model = requestModel, Messages = Array.AsReadOnly(turns.ToArray()),
                ExecutionContext = new(current.TaskId, current.ContextId, current.ExecutionId, issued.AttemptId,
                    current.PersistenceRevision, responseOriginal.ActionId)
                { RequestedCandidate = selected.ActualSelectedCandidate, SelectedCandidate = selected.ActualSelectedCandidate }
            };
            var nextBoundary = CaptureOriginalToolCheckpoint(original, request, boundary.OriginalInventory,
                assistantId, buffer.ToString(), activities, callsUsed, boundary.ToolLimit, lastCall, lastResult,
                runtimeByName, current);
            request = nextBoundary.OriginalRequest;
            coordinator.CaptureOriginalResponseRequest(responseOriginal, request);
            await AwaitOriginalCheckpointStage(original, "checkpoint.provider-context-capture", () =>
                capture.CaptureOriginalAsync(current, request, boundary.OriginalInventory, token).AsTask()).ConfigureAwait(false);
            // Context capture can await arbitrary source reads. Re-observe the genuine
            // accepted Conversation/User immediately before acquiring a new raw call.
            await AwaitOriginalCheckpointStage(original, "checkpoint.provider-input-currentness", () =>
                coordinator.ValidateOriginalToolCheckpointInputAsync(original, current, token)).ConfigureAwait(false);
            Task<OllamaToolResponse>? raw = null;
            OllamaToolResponse response;
            try
            {
                raw = InvokeOriginalToolCheckpointCall(nextBoundary, () => ollama.ChatWithToolsAsync(request, token));
                response = await raw.ConfigureAwait(false);
                if (response is null) throw new InvalidOperationException("The actual provider returned no tool response.");
                nextBoundary.RecordActualResponse(response);
                var responseAck = await ObserveOriginalChatResponseTerminalAsync(responseOriginal, raw).ConfigureAwait(false);
                if (responseAck is not null) current = responseAck;
            }
            catch (Exception cause)
            {
                nextBoundary.RecordActualFailure(cause, raw); original.Retain(cause, raw);
                await ObserveOriginalChatResponseTerminalAsync(responseOriginal, raw, cause).ConfigureAwait(false);
                throw;
            }
            if (response.EffectiveModel is not { } actualModel || actualModel.Key != descriptor.Key
                || !actualModel.Supports(ToolCapability.Text) || !actualModel.Supports(ToolCapability.Tools))
                throw new InvalidOperationException("The effective response model is not the actual selected capable local model.");
            if (response.ToolCalls.Count == 0)
            {
                var content = string.IsNullOrWhiteSpace(response.Content)
                    ? "The tool pass completed without a final model response. Review the activity above." : response.Content;
                buffer.Append(content);
                yield return ChatStreamEvent.AssistantDelta(assistantId, content);
                break;
            }
            var returnedCalls = Array.AsReadOnly(response.ToolCalls.Select(DetachOriginalToolCall).ToArray());
            turns.Add(new("assistant", response.Content, returnedCalls));
            if (!string.IsNullOrWhiteSpace(response.Content))
            { buffer.Append(response.Content); yield return ChatStreamEvent.AssistantDelta(assistantId, response.Content); }
            foreach (var call in returnedCalls)
            {
                if (callsUsed >= boundary.ToolLimit) break;
                callsUsed++;
                lastCall = call;
                tracker.Update(ChatExecutionStage.RunningTool, StatusForTool(call.Name));
                lastResult = await ExecuteTool(call).ConfigureAwait(false);
                activities.Add(lastResult.Activity);
                yield return ChatStreamEvent.Activity(assistantId, lastResult.Activity);
                turns.Add(new("tool", lastResult.Output, ToolName: call.Name));
            }
        }
        if (callsUsed >= boundary.ToolLimit)
            throw new InvalidOperationException("The retained original tool-call limit was reached; no new input or replay was created.");
        await AwaitOriginalCheckpointStage(original, "checkpoint.complete-safety", () =>
            safety.EnsureMayActAsync(conversation.Id, "chat.complete", token)).ConfigureAwait(false);
        var assistant = new ChatMessage(assistantId, conversation.Id, MessageRole.Assistant, buffer.ToString(),
            agentName, requestModel, activities.Count == 0 ? null : JsonSerializer.Serialize(new { toolActivities = activities }), DateTimeOffset.UtcNow);
        if (!conversation.IsTemporary)
            await AwaitOriginalCheckpointStage(original, "checkpoint.assistant-write", () =>
                conversations.AddMessageAsync(assistant, token)).ConfigureAwait(false);
        original.CompletionBasis = await ReadCurrent("checkpoint.completion-basis").ConfigureAwait(false);
        tracker.Complete();
        tracker.Changed -= Publish;
        yield return ChatStreamEvent.AssistantCompleted(assistant);
    }

    private static bool SameOriginalCheckpointArguments(OllamaToolCall original, OllamaToolCall current) =>
        original.Arguments.Count == current.Arguments.Count && original.Arguments.All(pair =>
            current.Arguments.TryGetValue(pair.Key, out var value)
            && JsonElement.DeepEquals(pair.Value, value));
}
