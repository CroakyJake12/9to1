namespace Haven.Application;

/// <summary>A tagged CREATE precondition, deliberately distinct from every SHA-256, including
/// the SHA-256 of zero bytes. This describes scope; only the original owner grants authority.</summary>
public static class DeveloperProjectSourceWriteRevision
{
    public const string Absent = "absent";
}

/// <summary>Separate WRITE authorization for a manual (no-AI) source edit. A READ admission
/// never authorizes a WRITE; the configured Files issuer and Home owner must recognize the SAME
/// selection/capture and the exact project-relative file/revision. Broker approval and private
/// issuer recognition are mandatory; no path, descriptor, stored ID or generic interface grants
/// an edit. The admission is bound to the exact reviewed AFRICE payload (text/hash/length) and an
/// operation identity, so a replacement that changes any byte/target/hash after review is refused.</summary>
public interface IDeveloperProjectOriginalEditAdmissionSource
{
    /// <summary>Separately reviewed CREATE of one genuinely absent target beneath an existing
    /// captured parent. Absence is a distinct precondition, never the hash of an empty file.</summary>
    Task<IDeveloperProjectOriginalEditAdmission> AcquireOriginalCreateAsync(
        IDeveloperProjectOriginalReadSelection sameSelection,
        IDeveloperProjectOriginalExistingSourceCapture sameCapture,
        string projectRelativePath, string reviewedAfterText, CancellationToken cancellationToken)
        => throw new NotSupportedException("This original issuer does not admit absent-target CREATE.");
    Task ValidateOriginalCreateAsync(IDeveloperProjectOriginalReadSelection sameSelection,
        IDeveloperProjectOriginalExistingSourceCapture sameCapture, string projectRelativePath,
        IDeveloperProjectOriginalEditAdmission sameAdmission, CancellationToken cancellationToken)
        => throw new NotSupportedException("This original issuer does not validate absent-target CREATE.");
    IWorkspaceToolFinalFence MintOriginalCreateFence(IDeveloperProjectOriginalEditAdmission sameAdmission,
        string projectRelativePath, string reviewedAfterText)
        => throw new NotSupportedException("This original issuer does not mint absent-target CREATE fences.");
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
    /// <summary>Privately bound by the configured issuer. A public implementation of this
    /// property is not authority; the kernel still requires that issuer's admission and fence.</summary>
    bool RequiresAbsentTarget => false;
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
    IReadOnlyList<ResourceScope> GetOriginalCreateScopes(IDeveloperProjectOriginalReadSelection sameSelection,
        IDeveloperProjectOriginalExistingSourceCapture sameCapture, string projectRelativePath)
        => throw new NotSupportedException("The configured Files source does not issue CREATE scopes.");
    Task RevalidateOriginalCreateScopeAsync(IDeveloperProjectOriginalReadSelection sameSelection,
        IDeveloperProjectOriginalExistingSourceCapture sameCapture, string projectRelativePath,
        AuthenticatedResourceActor sameActor, CancellationToken cancellationToken)
        => throw new NotSupportedException("The configured Files source does not validate CREATE scopes.");
    IReadOnlyList<ResourceScope> GetOriginalEditScopes(IDeveloperProjectOriginalReadSelection sameSelection,
        IDeveloperProjectOriginalExistingSourceCapture sameCapture, string projectRelativePath, string expectedSha256);
    Task RevalidateOriginalEditScopeAsync(IDeveloperProjectOriginalReadSelection sameSelection,
        IDeveloperProjectOriginalExistingSourceCapture sameCapture, string projectRelativePath, string expectedSha256,
        AuthenticatedResourceActor sameActor, CancellationToken cancellationToken);
}
