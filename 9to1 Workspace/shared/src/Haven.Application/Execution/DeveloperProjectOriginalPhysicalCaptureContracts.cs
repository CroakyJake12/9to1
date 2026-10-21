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
    /// <summary>Synchronous, pure native-metadata probe on the retained captured file handle. It
    /// returns false when the SAME capture is no longer issued or its exact project-relative retained
    /// handle no longer matches the captured native identity (dev/inode/size/mtime/ctime/version).
    /// Safe to run beneath a held Home entry; it never reenters async or public Home/Files/store
    /// APIs. This is the genuine retained physical-state check a manual-edit currentness pin uses
    /// immediately before the finite swap.</summary>
    bool IsOriginalCapturedFileCurrentNative(IDeveloperProjectOriginalExistingSourceCapture sameCapture,
        string projectRelativePath);
    /// <summary>Genuine confined re-read of one captured source file's complete text. It requires the
    /// SAME returned admission validated at capture, and reads the retained captured handle under
    /// <see cref="IDeveloperProjectOriginalReadAdmission.RunOriginalRead"/> — never raw path IO. It
    /// returns null when the exact project-relative file is not part of this capture; a stale or
    /// mutated captured source is refused on its recorded size/hash, never truncated.</summary>
    Task<string?> ReadOriginalCapturedFileTextAsync(IDeveloperProjectOriginalExistingSourceCapture sameCapture,
        IDeveloperProjectOriginalReadAdmission sameReadAdmission, string projectRelativePath,
        CancellationToken cancellationToken);

    /// <summary>Genuine confined reviewed WRITE of one captured source file under a SAME WRITE
    /// admission (never the READ admission alone). It revalidates the SAME read + edit admissions
    /// and capture, refuses unless the current captured text hash equals <paramref name="expectedSha256"/>,
    /// applies the exact replacement through the shared atomic engine under the edit admission, then
    /// re-reads under the read admission and verifies the resulting hash equals the after hash. It
    /// returns the post-write SHA-256. No raw editor/layer filesystem writer is used.</summary>
    Task<string> ApplyOriginalCapturedFileEditAsync(IDeveloperProjectOriginalExistingSourceCapture sameCapture,
        IDeveloperProjectOriginalReadAdmission sameReadAdmission,
        IDeveloperProjectOriginalEditAdmission sameEditAdmission,
        string projectRelativePath, string expectedSha256, string newText, CancellationToken cancellationToken);
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
