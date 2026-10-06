namespace Haven.Application;

/// <summary>SAME configured kernel capture owner. This port retains an original metadata
/// producer and an actual directory descriptor; it issues no Home permission or Files
/// binding. Fresh profile/source reads precede a held Home entry. Final predicates beneath
/// the Files binding lease use the held descriptor and private entry only.</summary>
public interface IDeveloperProjectOriginalDirectoryRegistrationSource
{
    Task<IDeveloperProjectOriginalDirectoryRegistrationPreparation> PrepareOriginalDirectoryRegistrationAsync(
        DeveloperProjectSetupIntent sameIntent, IDeveloperProjectOriginalSourceCapture sameCapture,
        IDeveloperProjectOriginalSetupPermission samePermission, DeveloperProjectSetupStep sameStep,
        IDeveloperProjectOriginalDirectoryPreparation originalObservationPreparation,
        Task originalObservationTask, IDeveloperProjectOriginalDirectoryObservation originalObservation,
        CancellationToken cancellationToken);
    bool IsIssuedOriginalDirectoryRegistrationOutcome(
        IDeveloperProjectOriginalDirectoryRegistrationPreparation samePreparation,
        Task sameRegistrationTask, Task sameMetadataTask, object? sameResult);
    Task ValidateOriginalDirectoryRegistrationOutcomeAsync(
        IDeveloperProjectOriginalDirectoryRegistrationPreparation samePreparation,
        Task sameRegistrationTask, Task sameMetadataTask, object? sameResult, CancellationToken cancellationToken);
    void DemandExternalOriginalDirectoryRegistrationJoin();
}

public interface IDeveloperProjectOriginalDirectoryRegistrationPreparation : IAsyncDisposable
{
    string OriginalDirectoryPath { get; }
    // One original metadata producer, published before the factory can run. The source
    // independently joins the exact returned metadata Task even if acquisition faults.
    Task<T> RunOriginalDirectoryRegistrationAsync<T>(IDeveloperProjectOriginalSetupStepEntry sameEntry,
        Func<Task<T>> originalMetadataFactory, CancellationToken cancellationToken);
    // Finite native/private-reference checks only. No Home/profile/Files reacquisition.
    ValueTask<bool> CheckOriginalDirectoryCurrentAsync(IDeveloperProjectOriginalSetupStepEntry sameEntry,
        CancellationToken cancellationToken);
    // Propagated lifetime/cause custody only; no permission or metadata authority.
    void RunOriginalDirectorySourceScope(Action originalCallback);
    void RetainOriginalDirectoryTask(Task sameActualTask);
    Task CloseAndDrainAsync();
}
