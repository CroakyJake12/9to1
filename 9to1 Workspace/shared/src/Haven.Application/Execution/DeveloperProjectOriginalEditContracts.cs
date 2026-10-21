namespace Haven.Application;

/// <summary>Separate WRITE authorization for a manual (no-AI) source edit. A READ admission
/// never authorizes a WRITE; the configured Files issuer and Home owner must recognize the SAME
/// selection/capture and the exact project-relative file/revision. Broker approval and private
/// issuer recognition are mandatory; no path, descriptor, stored ID or generic interface grants
/// an edit. The admission is bound to the exact reviewed AFRICE payload (text/hash/length) and an
/// operation identity, so a replacement that changes any byte/target/hash after review is refused.</summary>
public interface IDeveloperProjectOriginalEditAdmissionSource
{
    Task<IDeveloperProjectOriginalEditAdmission> AcquireOriginalEditAsync(
        IDeveloperProjectOriginalReadSelection sameSelection,
        IDeveloperProjectOriginalExistingSourceCapture sameCapture,
        string projectRelativePath, string expectedSha256, string reviewedAfterText,
        CancellationToken cancellationToken);
    Task ValidateOriginalEditAsync(IDeveloperProjectOriginalReadSelection sameSelection,
        IDeveloperProjectOriginalExistingSourceCapture sameCapture, string projectRelativePath, string expectedSha256,
        IDeveloperProjectOriginalEditAdmission sameAdmission, CancellationToken cancellationToken);
    /// <summary>Mints the genuine manual (no-AI) Home edit final fence for an apply. The
    /// configured Home issuer only vends a fence for its OWN private Edit (recognized as
    /// <paramref name="sameAdmission"/>) and binds the exact project-relative target and the
    /// reviewed BEFORE/AFTER digests. A caller-supplied or AlwaysAllow admission can never mint
    /// a trusted fence; the returned fence itself holds the real claimed Home authorization through
    /// the finite native write.</summary>
    IWorkspaceToolFinalFence MintOriginalEditFence(
        IDeveloperProjectOriginalEditAdmission sameAdmission,
        string projectRelativePath, string expectedSha256, string reviewedAfterText);
    void DemandExternalOriginalEditAdmissionJoin();
    Task CloseAndDrainOriginalEditsAsync();
}

/// <summary>A genuine WRITE admission over the SAME selected/captured source file. Its finite
/// edit starts run only after fresh validations of the actor, configured Files root/profile and
/// the exact file/revision, and only for the exact reviewed replacement bound to this admission.
/// The admission joins raw child tasks and closes/drains them.</summary>
public interface IDeveloperProjectOriginalEditAdmission : IAsyncDisposable
{
    /// <summary>Stable identity of this reviewed edit operation from review through authorized apply.</summary>
    Guid OperationId { get; }
    /// <summary>The exact reviewed replacement text (canonical bounded UTF-8).</summary>
    string ReviewedAfterText { get; }
    /// <summary>SHA-256 of the exact reviewed replacement bytes.</summary>
    string ReviewedAfterSha256 { get; }
    /// <summary>UTF-8 byte length of the exact reviewed replacement.</summary>
    long ReviewedAfterUtf8Length { get; }
    Task RevalidateOriginalEditAsync(CancellationToken cancellationToken);
    T RunOriginalEdit<T>(Func<T> finiteOriginalEditStart, CancellationToken cancellationToken);
}

/// <summary>The configured Files selection also issues a distinct WRITE scope for one exact
/// captured source file/revision and revalidates the SAME selection/capture/file on demand.</summary>
public interface IDeveloperProjectOriginalEditScopeSource
{
    IReadOnlyList<ResourceScope> GetOriginalEditScopes(IDeveloperProjectOriginalReadSelection sameSelection,
        IDeveloperProjectOriginalExistingSourceCapture sameCapture, string projectRelativePath, string expectedSha256);
    Task RevalidateOriginalEditScopeAsync(IDeveloperProjectOriginalReadSelection sameSelection,
        IDeveloperProjectOriginalExistingSourceCapture sameCapture, string projectRelativePath, string expectedSha256,
        AuthenticatedResourceActor sameActor, CancellationToken cancellationToken);
}
