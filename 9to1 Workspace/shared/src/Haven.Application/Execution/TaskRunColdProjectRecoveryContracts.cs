using Haven.Core;

namespace Haven.Application;

/// <summary>Source-observed project identity carried inside the authenticated capsule.
/// None of these serialized fields issues resource, actor, model or execution authority.</summary>
public sealed record TaskRunColdProjectIdentity(
    Guid WorkspaceId, Guid ProjectId, Guid RootId,
    long WorkspaceRevision, long ProjectRevision, string? RepositoryBindingId,
    string CanonicalRoot, string OriginalProjectContextJson,
    Guid ContainerId, string OriginalContainerSha256, string? OriginalContainerInstructions,
    string SavedWorkspaceDocumentSha256, string RegisteredRootFingerprint,
    AuthenticatedResourceActor OriginalHomeResourceActor);

/// <summary>Private Home-issued initial input after current saved-document, container,
/// profile and native-root observations and successful short borrower cleanup.</summary>
public interface ITaskRunColdOriginalProjectInput
{
    Conversation OriginalConversation { get; }
    ContainerDefinition OriginalContainer { get; }
    TaskRunColdProjectIdentity OriginalIdentity { get; }
    Task OriginalPreparation { get; }
}

/// <summary>NEW private Home READ reconciliation. Its genuine native commit pin remains
/// owned through the identity CAS; neither an old setup ACK nor an old pin is recreated.</summary>
public interface ITaskRunColdProjectRestorationLease
{
    ITaskRunColdOriginalProjectMaterial OriginalMaterial { get; }
    TaskRunColdProjectIdentity OriginalIdentity { get; }
    AuthenticatedResourceActor CurrentHomeResourceActor { get; }
    Task OriginalPreparation { get; }
    // Finite native checks use already-retained descriptors; no Home/Files lease or managed
    // callback is acquired. Supported statx/readlink probes are not a promise of zero native I/O.
    void DemandOriginalCommit();
    void RequestOriginalRetirement();
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}

/// <summary>SAME configured Home saved-project/resource producer. Exact private source
/// identity and genuine scoped reads remain mandatory; interface presence grants nothing.</summary>
public interface ITaskRunColdProjectResourceSource
{
    bool HasOriginalColdProjectComposition(ITaskRunColdRecoveryJournal sameJournal,
        IAuthenticatedResourceActorSource sameTaskActors);

    Task<ITaskRunColdOriginalProjectInput> PrepareOriginalProjectInputWithinSourceAsync(
        Conversation sameConversation, ContainerDefinition sameActualContainer,
        string exactProjectReferenceJson, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalProjectInput(ITaskRunColdOriginalProjectInput sameInput);
    bool IsOwnedOriginalProjectInput(ITaskRunColdOriginalProjectInput sameInput);
    Task ValidateOriginalProjectInputWithinSourceAsync(ITaskRunColdOriginalProjectInput sameInput,
        TaskRunColdChatInput exactInput, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    Task<TaskRunColdProjectIdentity> CaptureOriginalClosedProjectIdentityWithinSourceAsync(
        ITaskRunColdOriginalProjectInput sameInput, TaskExecutionSnapshot actualTerminal,
        TaskRunColdChatInput exactInput, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);

    Task<ITaskRunColdProjectRestorationLease> PrepareOriginalProjectRestorationWithinSourceAsync(
        ITaskRunColdOriginalProjectMaterial sameMaterial, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalProjectRestoration(ITaskRunColdProjectRestorationLease sameLease,
        ITaskRunColdOriginalProjectMaterial sameMaterial);
    // Historical cleanup recognition after seal is deliberately distinct from live issuance.
    bool IsOwnedOriginalProjectRestoration(ITaskRunColdProjectRestorationLease sameLease,
        ITaskRunColdOriginalProjectMaterial sameMaterial);
    Task ValidateOriginalProjectRestorationWithinSourceAsync(
        ITaskRunColdProjectRestorationLease sameLease, ITaskRunColdOriginalProjectMaterial sameMaterial,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    // Actual close must already be healthy. Fresh descriptor/root reads use current body custody;
    // this method never reopens the old lease or grants a Dev effect.
    Task ValidateOriginalClosedProjectRestorationWithinSourceAsync(
        ITaskRunColdProjectRestorationLease sameLease, ITaskRunColdOriginalProjectMaterial sameMaterial,
        ITaskRunColdJournalAcknowledgment sameAcknowledgment, TaskExecutionSnapshot actualCurrent,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
}
