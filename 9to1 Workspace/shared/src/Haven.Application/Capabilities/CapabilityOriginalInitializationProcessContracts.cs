namespace Haven.Application;

public enum CapabilityOriginalInitializationCompletionKind { Initialized, DeclinedBeforeEffect, ObservationRetired }

/// <summary>Private observation-delivery result, never a claimed Home permission.
/// ObservationRetired describes only delivery; the actual process operation remains owned.</summary>
public interface ICapabilityOriginalInitializationCompletion
{
    ICapabilityOriginalInitializationObservation OriginalObservation { get; }
    CapabilityOriginalInitializationCompletionKind Kind { get; }
    ICapabilityOriginalInitializationAcknowledgment? Acknowledgment { get; }
}

/// <summary>Privately issued observation of the SAME process-owned setup. A view joins
/// its delivery/close only; it must not borrow or close the global SQL/Home business owners.</summary>
public interface ICapabilityOriginalInitializationObservation : IAsyncDisposable
{
    ICapabilityOriginalInitializationIntent OriginalIntent { get; }
    Task? OriginalClose { get; }
    Task<ICapabilityOriginalInitializationCompletion> WaitOriginalCompletionAsync(CancellationToken token);
    bool IsIssuedOriginalCompletion(ICapabilityOriginalInitializationCompletion sameActual);
    void RequestOriginalRetirement();
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}

public interface ICapabilityOriginalInitializationProcessSource : ICapabilityOriginalInitializationSource
{
    Task<ICapabilityOriginalInitializationObservation> StartOriginalInitializationProcessWithinSourceAsync(
        ICapabilityOriginalInitializationIntent sameIntent, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalInitializationObservation(ICapabilityOriginalInitializationObservation sameActual);
    // Actual process retirement requests only its original unresolved reviews. It
    // independently joins any admitted atomic SQL/completion, with no cancellation waiver.
    void RequestOriginalPendingReviewWithdrawals();
}

/// <summary>Effect-free original review withdrawal, using SAME prepared Home review
/// and independent preparation child barrier. READ/prompt IDs cannot issue it. A claimed
/// or retained SQL child cannot be waived: the owner joins it and audits normally.</summary>
public interface ICapabilityOriginalInitializationHomeReviewWithdrawalSource
{
    Task WithdrawOriginalPendingWriteWithinSourceAsync(ICapabilityOriginalInitializationHomeWriteClaim sameClaim,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
}
