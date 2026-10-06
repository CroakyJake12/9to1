using Haven.Core;

namespace Haven.Application;

internal sealed partial class TaskRunInvocationCustody
{
    private readonly List<TaskRunOriginalResponseOutcome> _inheritedResponseOutcomes = [];
    internal TaskRunOriginalResponseOutcome[] CaptureOriginalResponseOutcomes()
    {
        lock (_gate) return _inheritedResponseOutcomes.Concat(_originalResponses.Select(value => value.Outcome)
            .OfType<TaskRunOriginalResponseOutcome>()).Distinct<TaskRunOriginalResponseOutcome>(ReferenceEqualityComparer.Instance).ToArray();
    }
    internal void BorrowOriginalResponseOutcome(TaskRunToolCheckpointContinuationBinding binding,
        TaskRunOriginalResponseOutcome outcome)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(binding.Issuer, Issuer) || !ReferenceEquals(binding.Next, this)
                || !binding.Transferred || !ReferenceEquals(binding.Original.Issuer, Issuer)
                || !binding.Original.CaptureOriginalResponseOutcomes().Any(value => ReferenceEquals(value, outcome))
                || !Issuer.HasSuccessfulOriginalResponseOutcome(outcome.Operation))
                throw new InvalidOperationException("Copied or failed response outcome cannot transfer checkpoint custody.");
            if (_inheritedResponseOutcomes.Count >= 256)
                throw new InvalidOperationException("Finite inherited response outcomes require inspection.");
            if (!_inheritedResponseOutcomes.Any(value => ReferenceEquals(value, outcome))) _inheritedResponseOutcomes.Add(outcome);
        }
    }
}

public sealed partial class TaskExecutionCoordinator
{
    // Actual outer cleanup has completed before this is called. The notifications only
    // release the privately retained metadata drivers; their real reads/CAS decide ACKs.
    internal async Task FinishOriginalResponseOwnerCleanupAsync(TaskRunInvocationCustody custody)
    {
        RequireOriginalInvocation(custody);
        var responses = custody.CaptureOriginalResponses();
        foreach (var original in responses)
        {
            lock (original.Gate)
            {
                if (!original.TerminalReady.Task.IsCompleted)
                {
                    original.ActualFailure ??= custody.Causes.FirstOrDefault()
                        ?? new InvalidOperationException("The original response closed before its real model terminal.");
                    original.TerminalReady.TrySetResult();
                }
                original.CleanupReady.TrySetResult();
            }
        }
        // Every actual child metadata driver is joined even if a sibling failed.
        foreach (var original in responses)
        {
            var actual = original.OriginalTerminal;
            if (actual is null) continue; // Legacy unbound synthetic callers issued no response node.
            try
            {
                var outcome = await actual.ConfigureAwait(false);
                if (outcome is null || custody.CompletionBasis is not { } basis) continue;
                if (SameOriginalCheckpointRow(basis, outcome.Before)) custody.CompletionBasis = outcome.Acknowledged;
            }
            catch (Exception cause) { custody.Retain(cause, actual); }
        }
    }

    private bool HasSameSuccessfulResponseNode(TaskRunInvocationCustody original, TaskPlanNode node) =>
        original.CaptureOriginalResponseOutcomes().Any(outcome => outcome.OriginalNode.ActionId == node.ActionId
            && HasSuccessfulOriginalResponseOutcome(outcome.Operation)
            && SameOriginalToolCheckpointNode(outcome.OriginalNode, node));

    private bool HasSameFailedResponseBoundary(TaskRunInvocationCustody original,
        ChatOriginalToolCheckpointBoundary boundary, TaskPlanNode node)
    {
        // This is an exception only for the exact private failed response. Independently
        // owned tools/native effects and unrelated Plan nodes remain under old predicates.
        if (!_originalResponseRequests.TryGetValue(boundary.OriginalRequest, out var response)
            || !ReferenceEquals(response.Issuer, this) || !ReferenceEquals(response.Custody, original)
            || response.ActionId != node.ActionId || response.Outcome is not null
            || response.ActualToolCall is not { IsFaulted: true } raw
            || !ReferenceEquals(raw, boundary.ActualCall) || response.OriginalTerminal is not { IsCompletedSuccessfully: true }
            || response.ActualFailure is null || !HasSameRunningResponseNode(response, node)
            || response.Bindings.Count == 0 || response.Bindings.Any(value => !value.Registration.IsCompletedSuccessfully
                || value.Acknowledgment is null || value.Sources.Any(actual => !actual.IsCompletedSuccessfully))) return false;
        var sources = boundary.Chat.RequireOriginalToolCheckpointSources();
        var outward = boundary.ActualOutwardFailure;
        if (outward is null) return false;
        var stage = RequireOriginalProcessStage();
        return stage.Invoke(() =>
        {
            var failure = sources.Failure.TryGetOriginalFinalRequestFailure(boundary.OriginalRequest, outward);
            if (failure is null || !sources.Failure.IsIssuedOriginalFinalRequestFailure(failure, boundary.OriginalRequest, outward)) return false;
            var settled = sources.Failure.TryGetOriginalRequestFailure(boundary.OriginalRequest, failure.OriginalAdmission, outward);
            var dispatch = sources.Dispatch.TryGetOriginalToolResponseDispatchWitness(failure);
            return settled is not null && sources.Failure.IsIssuedOriginalRequestFailure(settled, boundary.OriginalRequest,
                failure.OriginalAdmission, outward) && dispatch is not null
                && sources.Dispatch.IsIssuedOriginalToolResponseDispatchWitness(dispatch, failure)
                && HasOriginalWholeCallNativeDispatchEvidence(dispatch, failure);
        }, owningCleanup: original.OriginalResolvedToolCheckpoint is not null);
    }
}
