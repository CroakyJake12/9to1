namespace Haven.Application.Automations;

public enum CanonicalAutomationOriginalChangeCompletionKind { Changed, DeclinedBeforeEffect, ObservationRetired }

/// <summary>A privately issued delivery result. ObservationRetired detaches only
/// the view; it grants no assertion about the process-owned change or SQL outcome.</summary>
public interface ICanonicalAutomationDefinitionOriginalChangeCompletion
{
    ICanonicalAutomationDefinitionOriginalChangeObservation OriginalObservation { get; }
    CanonicalAutomationOriginalChangeCompletionKind Kind { get; }
    ICanonicalAutomationDefinitionOriginalChangeAcknowledgment? Acknowledgment { get; }
}

/// <summary>Observation of the SAME process-owned approved change. A view closes
/// its own delivery before joining commands, and never owns or cancels business SQL.</summary>
public interface ICanonicalAutomationDefinitionOriginalChangeObservation : IAsyncDisposable
{
    ICanonicalAutomationDefinitionOriginalChangeIntent OriginalIntent { get; }
    Task? OriginalClose { get; }
    Task<ICanonicalAutomationDefinitionOriginalChangeCompletion> WaitOriginalCompletionAsync(CancellationToken token);
    bool IsIssuedOriginalCompletion(ICanonicalAutomationDefinitionOriginalChangeCompletion sameActual);
    void RequestOriginalRetirement();
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}

/// <summary>A source-issued finite selection result. A declined preparation has no
/// intent or delivery, grants no approval, and does not describe a SQL outcome.</summary>
public interface ICanonicalAutomationDefinitionOriginalProcessPreparation
{
    bool IsDeclinedBeforeEffect { get; }
    ICanonicalAutomationDefinitionOriginalChangeObservation? Observation { get; }
}

/// <summary>Finite delivery acquisition over the maintained protected writer. The
/// actual driver uses process scopes and remains strongly owned through Home review,
/// atomic settlement and unknown failures, independently of presentation retirement.</summary>
public interface ICanonicalAutomationDefinitionOriginalProcessSource : ICanonicalAutomationDefinitionOriginalWriteSource
{
    Task<ICanonicalAutomationDefinitionOriginalChangeObservation> StartOriginalChangeProcessWithinSourceAsync(
        ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent, Action<Action> scope,
        Action<Task> retain, CancellationToken token);
    Task<ICanonicalAutomationDefinitionOriginalProcessPreparation> PrepareOriginalChangeProcessWithinSourceAsync(
        ICanonicalAutomationLibraryOriginalObservation sameObservation,
        AutomationOwnerRead<Haven.Core.AutomationDefinition> sameRow, CanonicalAutomationOriginalChangeKind kind,
        Guid operationId, Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsIssuedOriginalProcessPreparation(ICanonicalAutomationDefinitionOriginalProcessPreparation sameActual);
    bool IsIssuedOriginalChangeObservation(ICanonicalAutomationDefinitionOriginalChangeObservation sameActual);
    // Global retirement requests only this writer's actual unresolved Home reviews.
    // Accepted SQL and unknown results still require independent terminal settlement.
    void RequestOriginalPendingReviewWithdrawals();
}
