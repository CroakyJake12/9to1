namespace Haven.Application;

/// <summary>Original existing-source metadata/materialization custody on the SAME configured
/// capture owner. No byte copy, source overwrite, resource permission or immutable history.
/// Preparation validates the captured file/profile before any held Home entry.</summary>
public interface IDeveloperProjectOriginalFileRegistrationSource
{
    Task<IDeveloperProjectOriginalFileRegistrationPreparation> PrepareOriginalFileRegistrationAsync(
        DeveloperProjectSetupIntent sameIntent, IDeveloperProjectOriginalSourceCapture sameCapture,
        IDeveloperProjectOriginalSetupPermission samePermission, DeveloperProjectSetupStep sameStep,
        CancellationToken cancellationToken);
    bool IsIssuedOriginalFileRegistrationOutcome(IDeveloperProjectOriginalFileRegistrationPreparation samePreparation,
        Task sameRegistrationTask, Task sameMetadataTask, object? sameResult);
    Task ValidateOriginalFileRegistrationOutcomeAsync(IDeveloperProjectOriginalFileRegistrationPreparation samePreparation,
        Task sameRegistrationTask, Task sameMetadataTask, object? sameResult, CancellationToken cancellationToken);
    void DemandExternalOriginalFileRegistrationJoin();
}

public interface IDeveloperProjectOriginalFileRegistrationPreparation : IAsyncDisposable
{
    string OriginalFilePath { get; }
    Task<T> RunOriginalFileRegistrationAsync<T>(IDeveloperProjectOriginalSetupStepEntry sameEntry,
        Func<FileStream, Task<T>> originalMetadataFactory, CancellationToken cancellationToken);
    // Native descriptor/path/version and privately held entry only; never reacquire Home/Files.
    ValueTask<bool> CheckOriginalFileCurrentAsync(IDeveloperProjectOriginalSetupStepEntry sameEntry,
        CancellationToken cancellationToken);
    void RunOriginalFileSourceScope(Action originalCallback);
    void RetainOriginalFileTask(Task sameActualTask);
    Task CloseAndDrainAsync();
}
