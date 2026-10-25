namespace Haven.Application;

/// <summary>Display facts from a privately issued registered Files metadata observation. Public
/// implementations never grant execution; the configured source recognizes the exact object.</summary>
public interface IDeveloperProjectOriginalManualExecutionBinding
{
    string CanonicalRoot { get; }
    AuthenticatedResourceActor OriginalActor { get; }
    string ConfigurationId { get; }
    long ConfigurationRevision { get; }
    string DefinitionSha256 { get; }
    string Command { get; }
    int TimeoutSeconds { get; }
}

/// <summary>Genuine Task-free Files issuance, fresh source validation and retained native metadata/
/// registration/root custody. All asynchronous validation precedes the held Home entry.</summary>
public interface IDeveloperProjectOriginalManualExecutionSource
{
    Task<IDeveloperProjectOriginalManualExecutionBinding> CaptureOriginalManualExecutionAsync(
        IDeveloperProjectOriginalReadSelection sameSelection, IDeveloperProjectOriginalReadAdmission sameRead,
        IDeveloperProjectOriginalExistingSourceCapture sameCapture, string configurationId,
        long configurationRevision, string definitionSha256, CancellationToken token);
    bool IsIssuedOriginalManualExecution(IDeveloperProjectOriginalManualExecutionBinding sameBinding);
    IReadOnlyList<ResourceScope> GetOriginalManualExecutionScopes(IDeveloperProjectOriginalManualExecutionBinding sameBinding);
    Task RevalidateOriginalManualExecutionAsync(IDeveloperProjectOriginalManualExecutionBinding sameBinding,
        AuthenticatedResourceActor sameActor, CancellationToken token);
    Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquireOriginalManualExecutionPinAsync(
        IDeveloperProjectOriginalManualExecutionBinding sameBinding, CancellationToken token);
    void DemandOriginalManualExecutionPin(IDeveloperProjectOriginalManualExecutionBinding sameBinding,
        IDeveloperWorkspaceOriginalExecutionCommitPin samePin);
    Task CloseOriginalManualExecutionPinAsync(IDeveloperProjectOriginalManualExecutionBinding sameBinding,
        IDeveloperWorkspaceOriginalExecutionCommitPin samePin);
}

/// <summary>Separate genuine Home issuer for manual Run. A READ or arbitrary permission enum is
/// never process permission. The exact command, timeout and saved declaration enter Home review.</summary>
public interface IDeveloperProjectOriginalManualExecutionAdmissionSource
{
    Task<IDeveloperProjectOriginalManualExecutionAdmission> AcquireOriginalManualExecutionAsync(
        IDeveloperProjectOriginalManualExecutionBinding sameBinding, CancellationToken token);
    void DemandExternalOriginalManualExecutionJoin();
    Task CloseAndDrainOriginalManualExecutionsAsync();
}
public interface IDeveloperProjectOriginalManualExecutionAdmission : IAsyncDisposable
{
    Guid OperationId { get; }
    IWorkspaceToolFinalFence OriginalFence { get; }
    void ObserveOriginalProcessOutcome(IWorkspaceOriginalInvocation sameInvocation, ProcessResult? sameResult,
        WorkspaceToolPhysicalOutcome sameOutcome);
}

/// <summary>Configured Home private issuer recognition only. Deliberately distinct from source
/// WRITE and Task admission; no public registration or caller-provided allow Boolean.</summary>
public interface IWorkspaceManualProcessFinalFenceValidator
{
    bool IsIssuedOriginal(IWorkspaceToolFinalFence sameFence);
}
