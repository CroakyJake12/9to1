namespace Haven.Application;

/// <summary>Genuine finite-currentness custody for a manual (no-AI) source-EDIT commit, issued
/// by the configured Files selection producer for its SAME selection/capture/target and reviewed
/// BEFORE/AFTER pair. This is the real shape the execution bridge uses (a pure private issuance /
/// native metadata probe), but owned by the Files capture producer and bound to the reviewed edit
/// — never a caller boolean, dummy Task, or AlwaysAllow admission. Its cheap probe never reenters
/// public Home/Files/store methods and is safe beneath a held Home lease.</summary>
public interface IDeveloperProjectOriginalEditCurrentnessPin : IAsyncDisposable
{
    bool RequiresAbsentTarget => false;
    /// <summary>Synchronous private probe run immediately before the finite native swap (beneath
    /// the held Home entry). Throws when the SAME configured Files selection/capture/target is no
    /// longer the producer's live, privately owned product or the exact captured file is missing.
    /// A completed prevalidation alone never freezes later source/capture changes.</summary>
    void DemandOriginalEditCurrentness();

    /// <summary>The exact reviewed AFTER SHA-256 this pin was acquired for. The finite swap must
    /// write exactly this revision; a pin bound to any other reviewed AFTER identity does not back
    /// the fence's commit.</summary>
    string ReviewedAfterSha256 { get; }
}

/// <summary>The configured Files selection issuer (the <see cref="IDeveloperProjectOriginalEditScopeSource"/>)
/// additionally supplies genuine finite-currentness custody for one exact reviewed edit. Acquisition
/// runs the full async selection/capture/config/actor revalidation outside any held Home lease; the
/// returned pin's probe is pure, synchronous and cheap.</summary>
public interface IDeveloperProjectOriginalEditCurrentnessPinSource
{
    Task<IDeveloperProjectOriginalEditCurrentnessPin> AcquireOriginalCreateCurrentnessPinAsync(
        IDeveloperProjectOriginalReadSelection sameSelection,
        IDeveloperProjectOriginalExistingSourceCapture sameCapture,
        string projectRelativePath, string reviewedAfterSha256, CancellationToken cancellationToken)
        => throw new NotSupportedException("The configured Files source has no genuine CREATE currentness custody.");
    Task<IDeveloperProjectOriginalEditCurrentnessPin> AcquireOriginalEditCurrentnessPinAsync(
        IDeveloperProjectOriginalReadSelection sameSelection,
        IDeveloperProjectOriginalExistingSourceCapture sameCapture,
        string projectRelativePath, string expectedSha256, string reviewedAfterSha256,
        CancellationToken cancellationToken);
}
