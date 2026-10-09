namespace Haven.Application;

/// <summary>Configured kernel source observations only. Neither public metadata nor an
/// implementation of these interfaces issues Home permission, a directory mutation or a
/// historical content snapshot. The SAME capture source retains the original tasks/handles.</summary>
public interface IDeveloperProjectOriginalDirectoryObservationSource
{
    // Must run before acquiring the held Home step entry. Fresh configured issuer/profile
    // checks may perform I/O. The preparation opens only a captured existing directory.
    Task<IDeveloperProjectOriginalDirectoryPreparation> PrepareOriginalDirectoryAsync(
        DeveloperProjectSetupIntent sameIntent, IDeveloperProjectOriginalSourceCapture sameCapture,
        IDeveloperProjectOriginalSetupPermission samePermission, DeveloperProjectSetupStep sameStep,
        CancellationToken cancellationToken);
    bool IsIssuedOriginalDirectoryOutcome(IDeveloperProjectOriginalDirectoryPreparation samePreparation,
        Task sameActualObservationTask, IDeveloperProjectOriginalDirectoryObservation sameObservation);
    // Runs after held Home entry closure; no Home/Files lock is held by this method's caller.
    Task ValidateOriginalDirectoryOutcomeAsync(IDeveloperProjectOriginalDirectoryPreparation samePreparation,
        Task sameActualObservationTask, IDeveloperProjectOriginalDirectoryObservation sameObservation,
        CancellationToken cancellationToken);
    void DemandExternalOriginalDirectoryJoin();
}

public interface IDeveloperProjectOriginalDirectoryPreparation : IAsyncDisposable
{
    // The caller consumes the SAME actual Home entry around acquisition of this Task.
    // Native root/descriptor checks have no Home/Files lease reacquisition beneath that entry.
    Task<IDeveloperProjectOriginalDirectoryObservation> ObserveOriginalDirectoryAsync(
        IDeveloperProjectOriginalSetupStepEntry sameEntry, CancellationToken cancellationToken);
    Task CloseAndDrainAsync();
}

public interface IDeveloperProjectOriginalDirectoryObservation
{
    string OriginalDirectoryPath { get; }
}
