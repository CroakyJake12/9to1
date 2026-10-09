using Haven.Core;

namespace Haven.Application;

// Marker only. A serialized tuple, alternate issuer or caller implementation cannot
// qualify: the root-configured SAME source must recognize the actual checkpoint.
public interface ICanonicalProjectTaskContextResumeCheckpoint { }

public interface ICanonicalProjectTaskContextResumeSelection
{
    ICanonicalProjectTaskContextResumeCheckpoint OriginalCheckpoint { get; }
    AuthenticatedResourceActor Actor { get; }
    // Actual source-issued Home receipt, revalidated before pin acquisition and
    // by the SAME held Home state guard. This observation grants no WRITE.
    VerifiedResourceStoreOwnership OriginalDenOwnership { get; }
    ResourceStoreIdentity OriginalStoreIdentity { get; }
    Conversation OriginalStudioConversation { get; }
    ContainerDefinition OriginalStudioContainer { get; }
    Conversation TaskConversation { get; }
    ContainerDefinition TaskContainer { get; }
    Guid OriginalCreationOperationId { get; }
    string DenId { get; }
    string NamespaceId { get; }
    string DefinitionId { get; }
    long DefinitionRevision { get; }
    string SessionId { get; }
    long MembershipRevision { get; }
}

/// <summary>Actual Den-owned revision exclusion. It grants no Home/SQL WRITE and
/// borrows no old approval. SAME Den writer lease stays held until every accepted
/// atomic child independently joins; currentness under commit is purely source-owned.</summary>
public interface ICanonicalProjectTaskContextResumeRevisionPin : IAsyncDisposable
{
    ICanonicalProjectTaskContextResumeSelection OriginalSelection { get; }
    Task? OriginalClose { get; }
    Task CloseAndDrainAsync();
}

public interface ICanonicalProjectTaskContextResumeSelectionSource
{
    Task<ICanonicalProjectTaskContextResumeSelection> PrepareOriginalResumeSelectionWithinSourceAsync(
        ICanonicalProjectTaskContextResumeCheckpoint sameCheckpoint, AuthenticatedResourceActor actualActor,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalResumeSelection(ICanonicalProjectTaskContextResumeSelection sameSelection);
    Task RevalidateOriginalResumeSelectionWithinSourceAsync(
        ICanonicalProjectTaskContextResumeSelection sameSelection, AuthenticatedResourceActor actualActor,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    // Obtain after the separate current manual Home WRITE approval, before held Home
    // commit entry/SQL BEGIN. Capture actual pin INSIDE productive callback, before
    // scope postchecks can fail. Never Home.Open/ObserveOwnership under Den pin.
    Task<ICanonicalProjectTaskContextResumeRevisionPin> AcquireOriginalResumeRevisionPinWithinSourceAsync(
        ICanonicalProjectTaskContextResumeSelection sameSelection, AuthenticatedResourceActor actualActor,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        Action<ICanonicalProjectTaskContextResumeRevisionPin> captureOriginalPin, CancellationToken token);
    bool IsIssuedOriginalResumeRevisionPin(ICanonicalProjectTaskContextResumeSelection sameSelection,
        ICanonicalProjectTaskContextResumeRevisionPin samePin);
    // Pure held-lease guard over SAME actual definition/session/store/selection;
    // no Den/Home/permission/SQL reacquisition or serialized-boolean grant.
    void DemandOriginalPinnedResumeSelection(ICanonicalProjectTaskContextResumeSelection sameSelection,
        ICanonicalProjectTaskContextResumeRevisionPin samePin);
}
