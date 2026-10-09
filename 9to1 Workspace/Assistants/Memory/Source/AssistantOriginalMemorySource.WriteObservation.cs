using Haven.Application;

namespace HavenOS.Apps.Assistants.Memory;

public sealed partial class AssistantOriginalMemorySource
{
    public void DemandExternalOriginalRetirementJoin() => _originals.DemandExternalOriginalRetirementJoin();

    internal AssistantMemoryWriteObservation ObserveOriginalWriteIntent(ICanonicalAssistantMemoryWriteIntent sameIntent)
    {
        var intent = DemandWriteIntent(sameIntent);
        // Capture the private attempt under the producer gate, then release it before
        // observing Home's own synchronized request ID. Never invert their locks.
        WriteAttempt? attempt; ICanonicalAssistantMemoryHomeWriteSource? home;
        lock (_writeGate) { _writeAttempts.TryGetValue(intent, out attempt); home = _writes; }
        if (attempt is null)
            return home is null
                ? new(intent.OperationId, AssistantMemoryWriteState.Unavailable, null,
                    "The actual Home memory write owner is unavailable. Keep this preview and draft.")
                : new(intent.OperationId, AssistantMemoryWriteState.Prepared, null,
                    "This exact preview is ready to request its separate Home approval.");

        var actual = attempt.Driver;
        var claim = Volatile.Read(ref attempt.Invocation.Claim);
        var approvalId = claim?.OriginalApprovalRequestId;
        if (actual.IsCompleted)
        {
            if (actual.IsCompletedSuccessfully && actual.Result is ICanonicalAssistantMemoryWriteDecisionAcknowledgment { Applied: false } declined &&
                IsOwnedOriginalPublicWriteAcknowledgment(intent, declined, actual))
                return new(intent.OperationId, AssistantMemoryWriteState.Declined, approvalId, declined.Reason);
            if (actual.IsCompletedSuccessfully)
                return new(intent.OperationId, AssistantMemoryWriteState.Saved, approvalId,
                    "The original write and cleanup succeeded. Use its actual saved-record result.");
            if (IsAcknowledgedOriginalWriteRefusal(actual))
                return new(intent.OperationId, AssistantMemoryWriteState.Declined, approvalId,
                    "Home declined this exact operation before persistence. Keep its preview and draft; it has not been retried.");
            return new(intent.OperationId, AssistantMemoryWriteState.OutcomeUnconfirmed, approvalId,
                "The original operation did not settle successfully. Preserve its draft, operation and actual failure for inspection.");
        }

        var atomic = Volatile.Read(ref attempt.Invocation.Atomic);
        if (atomic is not null)
            return atomic.IsCompleted
                ? new(intent.OperationId, AssistantMemoryWriteState.Settling, approvalId,
                    "The actual commit attempt finished; Home audit and original cleanup are still settling.")
                : new(intent.OperationId, AssistantMemoryWriteState.CommitInProgress, approvalId,
                    "The original approved memory commit is in progress. Keep this same operation pending.");
        if (Volatile.Read(ref attempt.Invocation.OriginalDispatchSettled) == 1)
            return new(intent.OperationId, AssistantMemoryWriteState.Settling, approvalId,
                "Original preparation stopped; its Home review and captured sources are settling.");
        if (claim is not null && !claim.OriginalAcquisition.IsCompleted)
            return new(intent.OperationId, AssistantMemoryWriteState.HomeReview, approvalId,
                approvalId is null ? "Home is preparing the separate review for this exact memory record."
                    : "This exact Home review is in progress. Open it to inspect or decide; keep the original preview pending.");
        return new(intent.OperationId, AssistantMemoryWriteState.Preparing, approvalId,
            "The original source is checking the current Assistant and preparing its approved write.");
    }
}
