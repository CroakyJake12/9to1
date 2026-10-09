namespace Haven.Application;

/// <summary>Private coupling of the configured Files selection issuer and kernel owner. These
/// public interfaces and paths grant nothing; the configured sources recognize SAME objects.</summary>
public interface IDeveloperProjectOriginalPhysicalReadSelectionSource : IDeveloperProjectOriginalReadSelectionSource
{
    void DemandExternalOriginalReadSelectionJoin();
    bool IsIssuedOriginalPhysicalBinding(IDeveloperProjectOriginalReadSelection sameSelection,
        IDeveloperProjectOriginalPhysicalSelection samePhysicalSelection);
}
public interface IDeveloperProjectOriginalPhysicalSelection : IAsyncDisposable
{
    string OriginalProjectRoot { get; }
    Task CloseAndDrainAsync();
}
public interface IDeveloperProjectOriginalPhysicalCaptureSource
{
    Task<IDeveloperProjectOriginalPhysicalSelection> OpenOriginalSelectionAsync(
        string sameConfiguredFilesRoot, string explicitlySelectedExistingProjectRoot, CancellationToken cancellationToken);
    bool IsIssuedOriginalSelection(IDeveloperProjectOriginalPhysicalSelection sameSelection);
    Task RevalidateOriginalSelectionAsync(IDeveloperProjectOriginalPhysicalSelection sameSelection, CancellationToken cancellationToken);
    Task<IDeveloperProjectOriginalExistingSourceCapture> CaptureOriginalAsync(
        IDeveloperProjectOriginalPhysicalSelection samePhysicalSelection,
        IDeveloperProjectOriginalReadSelection sameSelection,
        IDeveloperProjectOriginalReadAdmission sameReadAdmission, CancellationToken cancellationToken);
    bool IsIssuedOriginalCapture(IDeveloperProjectOriginalSourceCapture sameCapture,
        IDeveloperProjectOriginalPhysicalSelection samePhysicalSelection, IDeveloperProjectOriginalReadSelection sameSelection);
    Task RevalidateOriginalCaptureAsync(IDeveloperProjectOriginalSourceCapture sameCapture, CancellationToken cancellationToken);
    Task CloseOriginalCaptureAsync(IDeveloperProjectOriginalSourceCapture sameCapture);
    void RequestOriginalCaptureRetirement();
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalCapturesAsync();
}

/// <summary>Pure actual Home read-owner dependency guard; no effect/read approval. Strict
/// capture cannot return its close from a source callback joining the same admitted read.</summary>
public interface IDeveloperProjectOriginalReadAdmissionJoinGuard
{
    void DemandExternalOriginalReadAdmissionJoin();
}
