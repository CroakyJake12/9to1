namespace Haven.Application;

/// <summary>Configured owning Files/kernel producer only. Matching metadata, a successful
/// public Task, or a public result/receipt does not establish an original physical outcome.
/// The source recognizes the SAME exact guarded operation Task and privately issued result,
/// including every required physical observation and cleanup. Unknown/partial work refuses.
/// Home checks this after releasing its genuine original entry; the journal records it later.</summary>
public interface IDeveloperProjectOriginalSetupStepOutcomeSource
{
    bool IsIssuedOriginalStepOutcome(DeveloperProjectSetupIntent sameIntent,
        IDeveloperProjectOriginalSourceCapture sameCapture, DeveloperProjectSetupStep sameStep,
        Task sameActualStepTask, object? sameActualResult);
    Task ValidateOriginalStepOutcomeAsync(DeveloperProjectSetupIntent sameIntent,
        IDeveloperProjectOriginalSourceCapture sameCapture, DeveloperProjectSetupStep sameStep,
        Task sameActualStepTask, object? sameActualResult, CancellationToken cancellationToken);
    DeveloperProjectOriginalStepOutcomeObservation GetOriginalStepOutcomeObservation(
        DeveloperProjectSetupIntent sameIntent, IDeveloperProjectOriginalSourceCapture sameCapture,
        DeveloperProjectSetupStep sameStep, Task sameActualStepTask, object? sameActualResult);
    void DemandExternalOriginalSetupStepOutcomeJoin();
}

/// <summary>Receipt reference and outcome digest for durable recovery display only. This
/// record never authorizes an effect, recovery replay, or an acknowledged journal transition;
/// those require the configured source to validate its SAME private original Task/result.</summary>
public sealed record DeveloperProjectOriginalStepOutcomeObservation(
    string OriginalReceiptReference, string OriginalOutcomeDigest);
