namespace Haven.Application;

// A presentation owns this finite delivery only. The configured writer owns the
// actual Home approval/SQL driver until independently settled at process close.
public enum CanonicalGeneratedUiOriginalCompletionKind { Saved, DeclinedBeforeEffect, ObservationRetired }
public interface ICanonicalGeneratedUiOriginalSaveCompletion
{
    ICanonicalGeneratedUiOriginalSaveObservation OriginalObservation { get; }
    CanonicalGeneratedUiOriginalCompletionKind Kind { get; }
    ICanonicalGeneratedUiOriginalSaveAcknowledgment? Acknowledgment { get; }
}
public interface ICanonicalGeneratedUiOriginalSaveObservation : IAsyncDisposable
{
    ICanonicalGeneratedUiOriginalSaveIntent OriginalIntent { get; }
    Task? OriginalClose { get; }
    Task<ICanonicalGeneratedUiOriginalSaveCompletion> WaitOriginalCompletionAsync(CancellationToken token);
    bool IsIssuedOriginalCompletion(ICanonicalGeneratedUiOriginalSaveCompletion sameCompletion);
    void RequestOriginalRetirement();
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}
public interface ICanonicalGeneratedUiInteractionOriginalProcessSource
{
    Task<ICanonicalGeneratedUiOriginalSaveObservation> StartOriginalSaveProcessWithinSourceAsync(
        ICanonicalGeneratedUiOriginalSaveIntent sameIntent, Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsIssuedOriginalSaveObservation(ICanonicalGeneratedUiOriginalSaveObservation sameObservation);
}
