namespace Haven.Application;

/// <summary>Privately issued facts from the actual READ-authorized saved task/stage.
/// A saved task declaration is not an AI Task-run identity and grants no process permission.</summary>
public interface IDeveloperProjectOriginalManualStageBinding
{
    string CanonicalRoot { get; }
    AuthenticatedResourceActor OriginalActor { get; }
    string SavedTaskId { get; }
    long SavedTaskRevision { get; }
    string StageId { get; }
    string StageKind { get; }
    bool RequiresWorkspaceTrust { get; }
    string DefinitionSha256 { get; }
    string Command { get; }
    int TimeoutSeconds { get; }
}

public interface IDeveloperProjectOriginalManualStageSource
{
    Task<IDeveloperProjectOriginalManualStageBinding> CaptureOriginalManualStageAsync(
        IDeveloperProjectOriginalReadSelection sameSelection, IDeveloperProjectOriginalReadAdmission sameRead,
        IDeveloperProjectOriginalExistingSourceCapture sameCapture, string savedTaskId, long savedTaskRevision,
        string stageId, string definitionSha256, CancellationToken token);
    bool IsIssuedOriginalManualStage(IDeveloperProjectOriginalManualStageBinding sameBinding);
    IReadOnlyList<ResourceScope> GetOriginalManualStageScopes(IDeveloperProjectOriginalManualStageBinding sameBinding);
    Task RevalidateOriginalManualStageAsync(IDeveloperProjectOriginalManualStageBinding sameBinding,
        AuthenticatedResourceActor sameActor, CancellationToken token);
    Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquireOriginalManualStagePinAsync(
        IDeveloperProjectOriginalManualStageBinding sameBinding, CancellationToken token);
    void DemandOriginalManualStagePin(IDeveloperProjectOriginalManualStageBinding sameBinding,
        IDeveloperWorkspaceOriginalExecutionCommitPin samePin);
    Task CloseOriginalManualStagePinAsync(IDeveloperProjectOriginalManualStageBinding sameBinding,
        IDeveloperWorkspaceOriginalExecutionCommitPin samePin);
}

public interface IDeveloperProjectOriginalManualStageAdmissionSource
{
    Task<IDeveloperProjectOriginalManualExecutionAdmission> AcquireOriginalManualStageAsync(
        IDeveloperProjectOriginalManualStageBinding sameBinding, CancellationToken token);
}
