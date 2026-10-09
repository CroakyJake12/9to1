using Haven.Core;

namespace Haven.Application;

public sealed partial class ChatSessionService
{
    private TaskRunOriginalResponseOperation ReserveOriginalChatResponse(TaskRunInvocationCustody original,
        Guid? parent, TaskRunToolCheckpointContinuationBinding? continuation = null) =>
        (taskCoordinator ?? throw new InvalidOperationException("The actual Task coordinator is unavailable."))
        .ReserveOriginalResponse(original, this, parent, continuation);

    private static void InvokeOriginalResponseCallback(TaskRunOriginalResponseOperation original, Action source,
        bool cleanup = false)
    {
        try
        {
            var producer = original.Custody.OriginalProcessProducer
                ?? throw new InvalidOperationException("The response has no actual owning Chat producer.");
            if (cleanup) producer.InvokeOriginalCallback(source); else producer.InvokeAdmittedOriginalCallback(source);
        }
        catch (Exception cause)
        {
            original.Custody.Retain(cause);
            original.Issuer.RecordOriginalResponseTerminal(original, failure: cause);
            if (cause is OperationCanceledException)
                throw new AggregateException("The original response factory faulted synchronously.", cause);
            throw;
        }
    }

    private async Task<TaskExecutionSnapshot?> ObserveOriginalChatResponseTerminalAsync(
        TaskRunOriginalResponseOperation? original, Task<OllamaToolResponse>? raw = null, Exception? failure = null)
    {
        if (original is null) return null;
        var actual = original.Issuer.RecordOriginalResponseTerminal(original, raw, failure);
        if (actual is null || original.DeferredToWholeContinuation) return null;
        try
        {
            var outcome = await actual.ConfigureAwait(false);
            if (outcome is null) return null;
            CurrentCanonicalTask = outcome.Acknowledged;
            return outcome.Acknowledged;
        }
        catch (Exception cause)
        {
            original.Custody.Retain(cause, actual);
            if (actual.IsFaulted && actual.Exception is { } faults) throw faults;
            throw;
        }
    }
}
