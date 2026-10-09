using Haven.Core;

namespace Haven.Application;

public enum TaskRunInitialChatObservationDisposition { ProducerTerminal = 0, ObservationDetached = 1 }

/// <summary>Observation outcome only. Producer terminal does not imply business success.</summary>
public sealed class TaskRunInitialChatObservationResult
{
    internal TaskRunInitialChatObservationResult(TaskRunInitialChatObservationDisposition disposition,
        ProviderExecutionContext? acknowledgedContext)
    { Disposition = disposition; AcknowledgedContext = acknowledgedContext; }
    public TaskRunInitialChatObservationDisposition Disposition { get; }
    public ProviderExecutionContext? AcknowledgedContext { get; }
}

/// <summary>Issued by the SAME initial Chat host. No business iterator, token, input or authority is exposed.</summary>
public sealed class TaskRunOriginalInitialChatObservationLease
{
    internal readonly ChatSessionService Issuer;
    internal readonly HostedInitialTaskSend Original;
    internal TaskRunOriginalInitialChatObservationLease(ChatSessionService issuer, HostedInitialTaskSend original)
    { Issuer = issuer; Original = original; }
    public Guid ConversationId => Original.ConversationId;
    public ProviderExecutionContext? CurrentAcknowledgedContext => Original.ReadAcknowledgedContext();
    public IAsyncEnumerable<ChatStreamEvent> ObserveOriginalEventsAsync(CancellationToken observationCancellationToken = default) =>
        Original.ObserveEvents(observationCancellationToken);
    public Task<TaskRunInitialChatObservationResult> WaitForOriginalObservationAsync(CancellationToken observationCancellationToken = default) =>
        Original.WaitForObservation(observationCancellationToken);
    public void DemandExternalOriginalObservationJoin() => Original.DemandExternalObservationJoin();
    public void RequestOriginalObservationRetirement() => Original.RequestObservationRetirement();
    public Task DetachAndDrainAsync() => Original.DetachAndDrain();
}
