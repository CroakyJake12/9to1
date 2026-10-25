namespace Haven.Application;

public enum DeveloperProjectGitInspectionKind { Status, Diff, Branches }

/// <summary>Display facts from a privately issued registered project/repository snapshot.
/// This is neither a RunConfiguration nor Task identity. Only its configured issuer grants use.</summary>
public interface IDeveloperProjectOriginalManualGitBinding
{
    string CanonicalRoot { get; }
    AuthenticatedResourceActor OriginalActor { get; }
    DeveloperProjectGitInspectionKind Operation { get; }
    string RepositoryBindingId { get; }
    string CanonicalRepositoryId { get; }
    string DefinitionSha256 { get; }
    string Command { get; }
    int TimeoutSeconds { get; }
}

public interface IDeveloperProjectOriginalManualGitSource
{
    Task<IDeveloperProjectOriginalManualGitBinding> CaptureOriginalManualGitAsync(
        IDeveloperProjectOriginalReadSelection sameSelection, IDeveloperProjectOriginalReadAdmission sameRead,
        IDeveloperProjectOriginalExistingSourceCapture sameCapture, DeveloperProjectGitInspectionKind operation,
        string definitionSha256, CancellationToken token);
    bool IsIssuedOriginalManualGit(IDeveloperProjectOriginalManualGitBinding sameBinding);
    IReadOnlyList<ResourceScope> GetOriginalManualGitScopes(IDeveloperProjectOriginalManualGitBinding sameBinding);
    Task RevalidateOriginalManualGitAsync(IDeveloperProjectOriginalManualGitBinding sameBinding,
        AuthenticatedResourceActor sameActor, CancellationToken token);
    Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquireOriginalManualGitPinAsync(
        IDeveloperProjectOriginalManualGitBinding sameBinding, CancellationToken token);
    void DemandOriginalManualGitPin(IDeveloperProjectOriginalManualGitBinding sameBinding,
        IDeveloperWorkspaceOriginalExecutionCommitPin samePin);
    Task CloseOriginalManualGitPinAsync(IDeveloperProjectOriginalManualGitBinding sameBinding,
        IDeveloperWorkspaceOriginalExecutionCommitPin samePin);
}

/// <summary>Separate typed admission for Git inspection. Uses the maintained manual process
/// fence/executor, genuine Home review and claim; READ and public metadata never authorize Start.</summary>
public interface IDeveloperProjectOriginalManualGitAdmissionSource
{
    Task<IDeveloperProjectOriginalManualExecutionAdmission> AcquireOriginalManualGitAsync(
        IDeveloperProjectOriginalManualGitBinding sameBinding, CancellationToken token);
}
