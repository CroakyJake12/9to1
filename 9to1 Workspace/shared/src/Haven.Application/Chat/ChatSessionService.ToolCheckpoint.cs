using System.Collections.ObjectModel;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>The exact next unfinished finite Chat tool-response request. Captured transcript
/// and native dispatch facts concern this Chat producer only; they prove nothing about
/// independently owned provider/native effects or permission to resume.</summary>
internal sealed class ChatOriginalToolCheckpointBoundary(
    ChatSessionService chat, TaskRunInvocationCustody custody, OllamaToolRequest request,
    TaskRunContextInventory inventory, Guid assistantId, string assistantText,
    IReadOnlyList<ToolActivity> activities, int callsUsed, int toolLimit,
    OllamaToolCall? lastCall, WorkspaceToolResult? lastResult,
    IReadOnlyList<TaskRunOriginalToolOutcomeCustody> toolOutcomes)
{
    private readonly object _gate = new();
    internal readonly ChatSessionService Chat = chat;
    internal readonly TaskRunInvocationCustody Custody = custody;
    internal readonly OllamaToolRequest OriginalRequest = request;
    internal readonly TaskRunContextInventory OriginalInventory = inventory;
    internal readonly Guid AssistantId = assistantId;
    internal readonly string AssistantText = assistantText;
    internal readonly IReadOnlyList<ToolActivity> Activities = activities;
    internal readonly int CallsUsed = callsUsed;
    internal readonly int ToolLimit = toolLimit;
    internal readonly OllamaToolCall? LastCall = lastCall;
    internal readonly WorkspaceToolResult? LastResult = lastResult;
    internal readonly IReadOnlyList<TaskRunOriginalToolOutcomeCustody> ToolOutcomes = toolOutcomes;
    internal Task<OllamaToolResponse>? ActualCall;
    internal Exception? ActualOutwardFailure;
    internal bool ResponseDelivered;
    internal readonly TaskCompletionSource ActualCallCaptured = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void BindActualCall(Task<OllamaToolResponse> actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        lock (_gate)
        {
            if (ActualCall is not null) throw new InvalidOperationException("The original checkpoint request was already invoked.");
            ActualCall = actual;
        }
        ActualCallCaptured.TrySetResult(); // Phase notification only, never call/cleanup completion.
    }

    internal void RecordActualResponse(OllamaToolResponse actual)
    {
        lock (_gate)
        {
            if (ActualCall is not { IsCompletedSuccessfully: true } call || !ReferenceEquals(call.Result, actual))
                throw new InvalidOperationException("The actual returned tool response is unavailable.");
            ResponseDelivered = true; // Before any local Chat tool dispatch/content processing.
        }
    }

    internal void RecordActualFailure(Exception outward, Task<OllamaToolResponse>? actual)
    {
        lock (_gate)
        {
            if (ActualCall is null || !ReferenceEquals(ActualCall, actual) || ResponseDelivered
                || !ActualCall.IsFaulted || ActualCall.Exception is null) return;
            ActualOutwardFailure = outward;
        }
    }

    internal bool IsExactFaultedUndeliveredCall(Exception sameOutward)
    {
        lock (_gate) return ReferenceEquals(ActualOutwardFailure, sameOutward)
            && ActualCall is { IsFaulted: true } && !ResponseDelivered;
    }
}

internal sealed partial class TaskRunInvocationCustody
{
    private readonly List<TaskRunOriginalToolOutcomeCustody> _originalToolOutcomes = [];
    internal ChatOriginalToolCheckpointBoundary? OriginalToolCheckpoint;
    internal void RetainOriginalToolOutcome(TaskRunOriginalToolOutcomeCustody original)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(original.Issuer, Issuer))
                throw new InvalidOperationException("Another coordinator cannot replace original tool custody.");
            if (_originalToolOutcomes.Any(prior => ReferenceEquals(prior, original))) return;
            if (_originalToolOutcomes.Count >= 256)
                throw new InvalidOperationException("Finite original tool-result custody is full; accepted work requires inspection.");
            _originalToolOutcomes.Add(original);
        }
    }
    internal IReadOnlyList<TaskRunOriginalToolOutcomeCustody> CaptureOriginalToolOutcomes()
    {
        lock (_gate) return Array.AsReadOnly(_originalToolOutcomes.ToArray());
    }
    internal void BindOriginalToolCheckpoint(ChatOriginalToolCheckpointBoundary original)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(original.Custody, this) || !ReferenceEquals(original.Chat, OriginalChatOwner)
                || OriginalToolCheckpoint is { } prior && (prior.ActualCall is not { IsCompletedSuccessfully: true }
                    || !prior.ResponseDelivered))
                throw new InvalidOperationException("An unfinished or foreign tool-response original cannot be replaced.");
            OriginalToolCheckpoint = original;
        }
    }
}

public sealed partial class ChatSessionService
{
    private static Task<OllamaToolResponse> InvokeOriginalToolCheckpointCall(
        ChatOriginalToolCheckpointBoundary original, Func<Task<OllamaToolResponse>> source)
    {
        Task<OllamaToolResponse>? actual = null;
        try
        {
            var producer = original.Custody.OriginalProcessProducer
                ?? throw new InvalidOperationException("No actual original Chat producer owns this request.");
            producer.InvokeAdmittedOriginalCallback(() =>
            {
                original.Custody.OriginalProviderInvocationInvoked = true;
                actual = source() ?? throw new InvalidOperationException("No actual tool-response Task was returned.");
                original.Custody.RetainAdditionalOriginal("provider.tools", actual);
                original.BindActualCall(actual);
            });
            return actual ?? throw new InvalidOperationException("No actual original tool-response Task was acquired.");
        }
        catch (Exception cause)
        {
            if (actual is not null) original.Custody.RetainAdditionalOriginal("provider.tools.capture", actual);
            original.Custody.Retain(cause, actual);
            if (cause is OperationCanceledException && actual is null)
                throw new AggregateException("Actual synchronous tool-response acquisition fault.", cause);
            throw;
        }
    }

    private ChatOriginalToolCheckpointBoundary CaptureOriginalToolCheckpoint(
        TaskRunInvocationCustody custody, OllamaToolRequest proposed, TaskRunContextInventory inventory,
        Guid assistantId, string assistantText, IReadOnlyList<ToolActivity> activities,
        int callsUsed, int toolLimit, OllamaToolCall? lastCall, WorkspaceToolResult? lastResult)
    {
        ChatOriginalToolCheckpointBoundary? original = null;
        try
        {
            var producer = custody.OriginalProcessProducer
                ?? throw new InvalidOperationException("No actual canonical producer owns the tool transcript.");
            producer.InvokeOriginalCallback(() =>
            {
                if (!ReferenceEquals(custody.OriginalChatOwner, this) || custody.OriginalBinding is not { } binding
                    || proposed.ExecutionContext is not { } context || context.TaskId != binding.TaskId
                    || context.ContextId != binding.ContextId || context.ExecutionId != binding.ExecutionId
                    || assistantId == Guid.Empty || callsUsed < 0 || callsUsed > toolLimit || toolLimit is < 1 or > 100)
                    throw new InvalidOperationException("The exact original Chat/tool-response binding is unavailable.");
                var request = DetachOriginalToolRequest(proposed);
                original = new(this, custody, request, inventory, assistantId, assistantText,
                    Array.AsReadOnly(activities.Select(item => item with
                    {
                        InvocationEvidence = item.InvocationEvidence is null ? null
                            : Array.AsReadOnly(item.InvocationEvidence.ToArray())
                    }).ToArray()), callsUsed, toolLimit,
                    lastCall is null ? null : DetachOriginalToolCall(lastCall), lastResult,
                    custody.CaptureOriginalToolOutcomes());
                custody.BindOriginalToolCheckpoint(original);
            });
            return original ?? throw new InvalidOperationException("No actual original tool checkpoint was captured.");
        }
        catch (Exception cause)
        {
            custody.Retain(cause);
            if (cause is OperationCanceledException)
                throw new AggregateException("Actual synchronous tool checkpoint capture fault.", cause);
            throw;
        }
    }

    // Both the actual provider request and its retained transcript use the SAME detached
    // values. Caller/adapter mutation cannot later redefine what was originally sent.
    private static OllamaToolRequest DetachOriginalToolRequest(OllamaToolRequest proposed)
    {
        if (proposed.Messages.Count > 1024 || proposed.Tools.Count > 256)
            throw new InvalidOperationException("Finite original tool transcript/schema custody is full.");
        var messages = Array.AsReadOnly(proposed.Messages.Select(turn => turn with
        {
            ToolCalls = turn.ToolCalls is null ? null : Array.AsReadOnly(turn.ToolCalls.Select(DetachOriginalToolCall).ToArray()),
            Images = turn.Images is null ? null : Array.AsReadOnly(turn.Images.ToArray())
        }).ToArray());
        var tools = Array.AsReadOnly(proposed.Tools.Select(tool => tool with
        {
            Properties = new ReadOnlyDictionary<string, object>(tool.Properties.ToDictionary(pair => pair.Key,
                pair => pair.Value is JsonElement element ? (object)element.Clone()
                    : JsonSerializer.SerializeToElement(pair.Value).Clone(), StringComparer.Ordinal)),
            Required = Array.AsReadOnly(tool.Required.ToArray()), InputSchema = tool.InputSchema?.Clone()
        }).ToArray());
        var context = proposed.ExecutionContext;
        if (context is not null) context = context with
        {
            RequestedCandidate = DetachOriginalToolCandidate(context.RequestedCandidate),
            SelectedCandidate = DetachOriginalToolCandidate(context.SelectedCandidate)
        };
        return proposed with { Messages = messages, Tools = tools, ExecutionContext = context };
    }

    private static TaskRunRouteCandidate? DetachOriginalToolCandidate(TaskRunRouteCandidate? candidate) =>
        candidate is null ? null : candidate with
        { RequiredCapabilities = Array.AsReadOnly(candidate.RequiredCapabilities.ToArray()) };

    private static OllamaToolCall DetachOriginalToolCall(OllamaToolCall call) => call with
    {
        Arguments = new ReadOnlyDictionary<string, JsonElement>(call.Arguments.ToDictionary(pair => pair.Key,
            pair => pair.Value.Clone(), StringComparer.Ordinal))
    };
}
