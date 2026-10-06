using Haven.Core;

namespace Haven.Application;

/// <summary>Opaque current Files personal-store/project selection. It is independent of
/// old setup products. Public inputs and resource metadata alone issue no READ or execution.</summary>
public interface IDeveloperOriginalCurrentProjectSelection : IDeveloperProjectOriginalReadSelection { }

/// <summary>Metadata from the SAME source's current registered project observation. The
/// native owner must recognize the exact selection/descriptor/store, then require READ.</summary>
public interface IDeveloperOriginalCurrentProjectDescriptor
{
    IDeveloperProjectOriginalWorkspaceMetadataStore OriginalStore { get; }
    Guid WorkspaceId { get; }
    Guid ProjectId { get; }
    Guid RootId { get; }
    long WorkspaceRevision { get; }
    long ProjectRevision { get; }
    string? RepositoryBindingId { get; }
    string ExactProjectReferenceJson { get; }
    Conversation OriginalConversation { get; }
    ContainerDefinition OriginalContainer { get; }
    AuthenticatedResourceActor OriginalActor { get; }
    string ConfiguredFilesRoot { get; }
    string RegisteredProjectRoot { get; }
    string OriginalRegistrationStatePath { get; }
    string OriginalRegistrationStateJson { get; }
    string OriginalSelectedRegistrationJson { get; }
    string? ExpectedWorkspaceDocumentSha256 { get; }
    string? ExpectedRegisteredRootFingerprint { get; }
}

public interface IDeveloperOriginalCurrentProjectSelectionSource : IDeveloperProjectOriginalReadSelectionSource
{
    Task<IDeveloperOriginalCurrentProjectSelection> SelectOriginalWithinSourceAsync(
        Conversation sameConversation, ContainerDefinition sameActualContainer, string exactProjectReferenceJson,
        string? expectedWorkspaceDocumentSha256, string? expectedRegisteredRootFingerprint,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    IDeveloperOriginalCurrentProjectDescriptor GetOriginalDescriptor(IDeveloperOriginalCurrentProjectSelection sameSelection);
    bool IsIssuedOriginalDescriptor(IDeveloperOriginalCurrentProjectSelection sameSelection,
        IDeveloperOriginalCurrentProjectDescriptor sameDescriptor);
    Task RevalidateOriginalWithinSourceAsync(IDeveloperOriginalCurrentProjectSelection sameSelection,
        AuthenticatedResourceActor sameActor, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    void DemandExternalOriginalCurrentProjectJoin();
}

/// <summary>Configured private Home issuer for the SAME current selection READ.
/// Native capture must require this exact configured issuer in addition to the admission
/// callback. An arbitrary IDeveloperProjectOriginalReadAdmission is never permission.</summary>
public interface IDeveloperOriginalCurrentProjectReadAdmissionSource
{
    bool IsIssuedOriginalCurrentProjectRead(IDeveloperOriginalCurrentProjectSelection sameSelection,
        IDeveloperProjectOriginalReadAdmission sameReadAdmission);
    Task ValidateOriginalCurrentProjectReadWithinSourceAsync(
        IDeveloperOriginalCurrentProjectSelection sameSelection,
        IDeveloperProjectOriginalReadAdmission sameReadAdmission,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
}

/// <summary>NEW existing-document/registration/root descriptor custody after genuine READ.
/// This is not an old Save Task/ACK or a Dev effect grant. Current native Demand probes already
/// retained handles; it invokes no managed permission/profile/Files callback or lease factory.</summary>
public interface IDeveloperOriginalCurrentProjectNativeRead : IDeveloperWorkspaceOriginalExecutionCommitPin
{
    string OriginalWorkspaceDocument { get; }
    string OriginalWorkspaceDocumentSha256 { get; }
    string OriginalRegisteredRootFingerprint { get; }
}
public interface IDeveloperOriginalCurrentProjectNativeSource
{
    Task<IDeveloperOriginalCurrentProjectNativeRead> CaptureOriginalWithinSourceAsync(
        IDeveloperOriginalCurrentProjectSelection sameSelection, IDeveloperProjectOriginalReadAdmission sameReadAdmission,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalRead(IDeveloperOriginalCurrentProjectSelection sameSelection,
        Task sameActualCaptureTask, IDeveloperOriginalCurrentProjectNativeRead sameRead);
    // Historical private ownership only, for independent cleanup and exact closed proof.
    bool IsOwnedOriginalRead(IDeveloperOriginalCurrentProjectSelection sameSelection,
        Task sameActualCaptureTask, IDeveloperOriginalCurrentProjectNativeRead sameRead);
    Task ValidateOriginalReadWithinSourceAsync(IDeveloperOriginalCurrentProjectSelection sameSelection,
        Task sameActualCaptureTask, IDeveloperOriginalCurrentProjectNativeRead sameRead,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    bool IsClosedOriginalRead(IDeveloperOriginalCurrentProjectSelection sameSelection,
        Task sameActualCaptureTask, IDeveloperOriginalCurrentProjectNativeRead sameRead, Task sameActualCloseTask);
    void RequestOriginalRetirement();
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}
