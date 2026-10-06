using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public sealed partial class TaskExecutionCoordinator
{
    internal async Task<TaskRunColdToolCheckpoint> CaptureOriginalClosedColdToolBoundaryAsync(
        ChatSessionService sameChat, TaskRunInvocationCustody original,
        TaskExecutionSnapshot sameTerminal, TaskRunColdOriginalSourceScope sources, CancellationToken token)
    {
        if (!ReferenceEquals(original.Issuer, this) || !ReferenceEquals(original.OriginalChatOwner, sameChat)
            || original.OriginalToolCheckpoint is not { } boundary || !ReferenceEquals(boundary.Chat, sameChat)
            || !ReferenceEquals(original.OriginalTerminalObservation, sameTerminal))
            throw new UnauthorizedAccessException("No SAME actual closed Chat/tool producer owns this durable capture.");
        if (!_originalResponseRequests.TryGetValue(boundary.OriginalRequest, out var originalResponse))
            throw new InvalidOperationException("The actual failed response request has no private issuer.");
        sources.Invoke(() => { DemandOriginalResponseRequest(originalResponse); return true; });
        var providers = sources.Invoke(sameChat.RequireOriginalToolCheckpointSources);
        var outward = boundary.ActualOutwardFailure
            ?? throw new InvalidOperationException("The unfinished original has no exact outward failure.");
        var failure = sources.Invoke(() => providers.Failure.TryGetOriginalFinalRequestFailure(boundary.OriginalRequest, outward));
        if (failure is null || !sources.Invoke(() => providers.Failure.IsIssuedOriginalFinalRequestFailure(
                failure, boundary.OriginalRequest, outward)))
            throw new InvalidOperationException("The actual provider did not issue this final failure binding.");
        var settled = sources.Invoke(() => providers.Failure.TryGetOriginalRequestFailure(
            boundary.OriginalRequest, failure.OriginalAdmission, outward));
        var dispatch = sources.Invoke(() => providers.Dispatch.TryGetOriginalToolResponseDispatchWitness(failure));
        if (settled is null || !sources.Invoke(() => providers.Failure.IsIssuedOriginalRequestFailure(settled,
                boundary.OriginalRequest, failure.OriginalAdmission, outward))
            || dispatch is null || !sources.Invoke(() => providers.Dispatch.IsIssuedOriginalToolResponseDispatchWitness(dispatch, failure))
            || !HasOriginalWholeCallNativeDispatchEvidence(dispatch, failure))
            throw new InvalidOperationException("Every actual provider invocation must have settled inspected-method evidence before a durable boundary.");
        // This reuses the live owning checks. It does not rebuild original receipts from
        // row fields. The optional predicate differs only in finite source ancestry.
        var current = await TaskRunColdSourceIo.Read(sources, () => repository.GetAsync(sameTerminal.TaskId, token)).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The actual closed Task disappeared.");
        if (!SameOriginalCheckpointRow(current, sameTerminal))
            throw new InvalidOperationException("The actual Task changed before durable closed-boundary capture.");
        sources.Invoke(() =>
        {
            RequireOriginalToolCheckpointBoundary(original, boundary, current,
                node => HasSameFailedResponseBoundaryWithinColdSource(original, boundary, node, sources));
            return true;
        });
        if (original.OriginalInputCurrentness is not { } input)
            throw new InvalidOperationException("The actual accepted input source is unavailable.");
        await TaskRunColdSourceIo.Read(sources, () => input(token)).ConfigureAwait(false);
        var final = await TaskRunColdSourceIo.Read(sources, () => repository.GetAsync(current.TaskId, token)).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The actual Task disappeared during durable input validation.");
        if (!SameOriginalCheckpointRow(current, final))
            throw new InvalidOperationException("The actual accepted-work row changed during durable input validation.");
        sources.Invoke(() =>
        {
            RequireOriginalToolCheckpointBoundary(original, boundary, final,
                node => HasSameFailedResponseBoundaryWithinColdSource(original, boundary, node, sources));
            return true;
        });
        var closed = new List<TaskRunColdClosedAction>();
        foreach (var node in final.Plan.Where(value => value.State == TaskPlanNodeState.Completed))
        {
            if (sources.Invoke(() => HasSameSuccessfulResponseNode(original, node)))
                closed.Add(new(TaskRunColdClosedActionKind.SuccessfulModelResponse, node));
            else
            {
                var outcome = boundary.ToolOutcomes.SingleOrDefault(value => value.OriginalNode.ActionId == node.ActionId);
                if (outcome is null || !sources.Invoke(() => HasSuccessfulOriginalToolCheckpointOutcome(outcome))
                    || !SameOriginalToolCheckpointNode(outcome.OriginalNode, node))
                    throw new InvalidOperationException("An accepted operation has no successful original owner retirement.");
                closed.Add(new(TaskRunColdClosedActionKind.OriginalOwnedTool, node));
            }
        }
        var response = boundary.OriginalRequest.ExecutionContext?.ActionId
            ?? throw new InvalidOperationException("The genuine failed response has no reserved action.");
        var invocations = dispatch.OriginalInvocations.Select(value => new TaskRunColdFailedProviderInvocation(
            value.OriginalFailedAttempt.OriginalAdmission.AttemptId,
            value.OriginalIssuedRouteConfiguration.ProviderId, value.InspectedMethod,
            value.OriginalFailedAttempt.OriginalObservation.AttemptId,
            Array.AsReadOnly(value.OriginalProviderTask.Exception!.InnerExceptions
                .Select(cause => cause.GetType().FullName ?? cause.GetType().Name).ToArray()))).ToArray();
        // The authenticated payload is detached from all live caller mutation. No raw
        // Tasks/leases or receipt references are serialized/recreated on restart.
        var material = new TaskRunColdToolCheckpoint(boundary.OriginalRequest, boundary.OriginalInventory,
            boundary.AssistantId, boundary.AssistantText, boundary.Activities, boundary.CallsUsed,
            boundary.ToolLimit, boundary.LastCall, boundary.LastResult, boundary.RuntimeByName,
            boundary.OriginalControlFingerprint, response, final.RecoveryObservation!, closed, invocations);
        return JsonSerializer.Deserialize<TaskRunColdToolCheckpoint>(JsonSerializer.Serialize(material))
            ?? throw new InvalidDataException("The full durable tool boundary could not be detached.");
    }

    private bool HasSameFailedResponseBoundaryWithinColdSource(TaskRunInvocationCustody original,
        ChatOriginalToolCheckpointBoundary boundary, TaskPlanNode node, TaskRunColdOriginalSourceScope sources)
    {
        if (!_originalResponseRequests.TryGetValue(boundary.OriginalRequest, out var response)
            || !ReferenceEquals(response.Issuer, this) || !ReferenceEquals(response.Custody, original)
            || response.ActionId != node.ActionId || response.Outcome is not null
            || response.ActualToolCall is not { IsFaulted: true } raw || !ReferenceEquals(raw, boundary.ActualCall)
            || response.OriginalTerminal is not { IsCompletedSuccessfully: true } || response.ActualFailure is null
            || !HasSameRunningResponseNode(response, node) || response.Bindings.Count == 0
            || response.Bindings.Any(value => !value.Registration.IsCompletedSuccessfully || value.Acknowledgment is null
                || value.Sources.Any(actual => !actual.IsCompletedSuccessfully))) return false;
        return sources.Invoke(() =>
        {
            var providers = boundary.Chat.RequireOriginalToolCheckpointSources();
            var outward = boundary.ActualOutwardFailure;
            if (outward is null) return false;
            var failure = providers.Failure.TryGetOriginalFinalRequestFailure(boundary.OriginalRequest, outward);
            if (failure is null || !providers.Failure.IsIssuedOriginalFinalRequestFailure(failure, boundary.OriginalRequest, outward)) return false;
            var settled = providers.Failure.TryGetOriginalRequestFailure(boundary.OriginalRequest, failure.OriginalAdmission, outward);
            var dispatch = providers.Dispatch.TryGetOriginalToolResponseDispatchWitness(failure);
            return settled is not null && providers.Failure.IsIssuedOriginalRequestFailure(settled, boundary.OriginalRequest,
                failure.OriginalAdmission, outward) && dispatch is not null
                && providers.Dispatch.IsIssuedOriginalToolResponseDispatchWitness(dispatch, failure)
                && HasOriginalWholeCallNativeDispatchEvidence(dispatch, failure);
        });
    }
}
