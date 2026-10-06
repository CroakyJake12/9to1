namespace Haven.Application;

/// <summary>The configured Files source issues the SAME selection before any manifest read.
/// Its public metadata is not a grant. The actual issuer retains the selected configured root,
/// store/profile revision, kernel handles and current read authorization; a path cannot substitute.</summary>
public interface IDeveloperProjectOriginalReadSelection { }
public interface IDeveloperProjectOriginalReadSelectionSource
{
    bool IsIssuedOriginal(IDeveloperProjectOriginalReadSelection sameSelection);
    Task RevalidateOriginalAsync(IDeveloperProjectOriginalReadSelection sameSelection,
        AuthenticatedResourceActor sameActor, CancellationToken cancellationToken);
    IReadOnlyList<ResourceScope> GetOriginalReadScopes(IDeveloperProjectOriginalReadSelection sameSelection);
}

/// <summary>Separate initial READ authorization. No absent later manifest/review authorizes
/// this earlier read. A configured owning producer must validate the SAME returned admission.</summary>
public interface IDeveloperProjectOriginalReadAdmissionSource
{
    Task<IDeveloperProjectOriginalReadAdmission> AcquireOriginalAsync(
        IDeveloperProjectOriginalReadSelection sameSelection, CancellationToken cancellationToken);
    Task ValidateOriginalAsync(IDeveloperProjectOriginalReadSelection sameSelection,
        IDeveloperProjectOriginalReadAdmission sameAdmission, CancellationToken cancellationToken);
}
public interface IDeveloperProjectOriginalReadAdmission : IAsyncDisposable
{
    Task RevalidateOriginalAsync(CancellationToken cancellationToken);
    T RunOriginalRead<T>(Func<T> finiteOriginalReadStart, CancellationToken cancellationToken);
}

/// <summary>The actual Home setup owner validates SAME configured capture and immutable intent,
/// obtains an explicit high-risk review/claim, and retains every actual step and cleanup task.
/// Journal/checkpoint IDs and public interface implementations never issue this admission.</summary>
public interface IDeveloperProjectOriginalSetupPermissionSource
{
    Task<IDeveloperProjectOriginalSetupPermission> AcquireOriginalAsync(
        DeveloperProjectSetupIntent sameIntent, IDeveloperProjectOriginalSourceCapture sameCapture,
        CancellationToken cancellationToken);
    Task ValidateOriginalAsync(DeveloperProjectSetupIntent sameIntent,
        IDeveloperProjectOriginalSetupPermission samePermission, CancellationToken cancellationToken);
    void RequestOriginalSetupRetirement();
    void DemandExternalOriginalSetupJoin();
    Task CloseAndDrainOriginalSetupsAsync();
}
public interface IDeveloperProjectOriginalSetupPermission : IAsyncDisposable
{
    // Exact private returned-entry pairing and held lifetime; metadata/interfaces cannot issue it.
    bool IsIssuedOriginalStepEntry(DeveloperProjectSetupStep sameStep, IDeveloperProjectOriginalSetupStepEntry sameEntry);
    Task<IDeveloperProjectOriginalSetupStepEntry> EnterOriginalStepAsync(
        DeveloperProjectSetupStep sameStep, CancellationToken cancellationToken);
    Task ValidateOriginalStepResultAsync(DeveloperProjectSetupStep sameStep,
        IDeveloperProjectOriginalSetupStepEntry sameEntry, Task sameActualStepTask,
        object? sameActualResult, CancellationToken cancellationToken);
}
public interface IDeveloperProjectOriginalSetupStepEntry : IAsyncDisposable
{
    // Pure private issuer/reference/held-admission observation; no I/O or lease acquisition.
    void DemandOriginalStepEntry(DeveloperProjectSetupStep sameStep);
    // Actual already-held Home/profile/readUnlocked currentness, retaining raw read tasks.
    // This may run under Files metadata publication lease: no completion/Home/resource/Files
    // lease reacquisition or Files callbacks. Lock order remains completion -> held Home -> Files.
    ValueTask<bool> CheckOriginalStepCommitAsync(DeveloperProjectSetupStep sameStep,
        CancellationToken cancellationToken);
    T RunOriginalStep<T>(DeveloperProjectSetupStep sameStep, Func<T> finiteOriginalStepStart,
        CancellationToken cancellationToken);
}
