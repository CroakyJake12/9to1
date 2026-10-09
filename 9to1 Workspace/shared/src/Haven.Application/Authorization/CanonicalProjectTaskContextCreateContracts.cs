using Haven.Core;

namespace Haven.Application;

// Creation is a separate, manually approved owning WRITE, never a READ grant.
public interface ICanonicalProjectTaskContextCreationIntent
{
    AuthenticatedResourceActor Actor { get; }
    ICanonicalProjectContextStoreObservation OriginalStoreObservation { get; }
    IDeveloperOriginalProjectCommandRead OriginalProjectRead { get; }
    Conversation OriginalStudioConversation { get; }
    ContainerDefinition OriginalStudioContainer { get; }
    Conversation TaskConversation { get; }
    ContainerDefinition TaskContainer { get; }
    Guid OperationId { get; }
}
public interface ICanonicalProjectTaskContextCreation
{
    ICanonicalProjectTaskContextCreationIntent OriginalIntent { get; }
    AuthenticatedResourceActor Actor { get; }
    Conversation TaskConversation { get; }
    ContainerDefinition TaskContainer { get; }
    Guid OperationId { get; }
}
public interface ICanonicalProjectTaskContextCreateSource
{
    Task<ICanonicalProjectTaskContextCreationIntent> PrepareOriginalCreationIntentWithinSourceAsync(
        ICanonicalProjectContextStoreObservation sameStudioObservation,
        IDeveloperOriginalProjectCommandRead sameLiveStudioProjectRead,
        Guid newConversationId, string title, Guid operationId,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalCreationIntent(ICanonicalProjectTaskContextCreationIntent sameIntent);
    string GetOriginalCreationIntentDigest(ICanonicalProjectTaskContextCreationIntent sameIntent);
    Task ValidateOriginalCreationIntentWithinSourceAsync(ICanonicalProjectTaskContextCreationIntent sameIntent,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    // Valid only inside this producer's retained atomic commit driver, over its actual
    // native pins. No Home/profile/repository read or permission reacquisition.
    void DemandOriginalCreationCommit(ICanonicalProjectTaskContextCreationIntent sameIntent);
    bool IsOriginalCreationCommitTask(ICanonicalProjectTaskContextCreationIntent sameIntent,
        Task<ICanonicalProjectTaskContextCreation> sameOriginalAtomicSqlTask);
    bool IsOwnedOriginalCreationAcknowledgment(ICanonicalProjectTaskContextCreationIntent sameIntent,
        ICanonicalProjectTaskContextCreation sameAcknowledgment,
        Task<ICanonicalProjectTaskContextCreation> sameOriginalAtomicSqlTask);
    Task<ICanonicalProjectTaskContextCreation> CommitOriginalCreationWithinSourceAsync(
        ICanonicalProjectTaskContextCreationIntent sameIntent,
        IDeveloperOriginalProjectCommandRead sameLiveStudioProjectRead,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalCreation(ICanonicalProjectTaskContextCreation sameCreation);
    bool IsAcknowledgedOriginalCreationRefusal(Task sameOriginalCommit);
    // SAME accepted raw cohort only, after healthy independent source joins and
    // actual no-SQL proof. Exception aliases and enclosing Den effects are not waived.
    bool IsAcknowledgedOriginalCreationSourceRefusal(Task sameOriginalSource) => false;
    Task RevalidateOriginalCreationWithinSourceAsync(ICanonicalProjectTaskContextCreation sameCreation,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
}

// Optional source capability. A cast alone grants nothing: native composition must
// pair the SAME concrete creation owner with its actual process Den selection source.
public interface ICanonicalProjectTaskContextResumeCreateSource
{
    Task<ICanonicalProjectTaskContextCreationIntent> PrepareOriginalResumedCreationIntentWithinSourceAsync(
        ICanonicalProjectTaskContextResumeSelection sameSelection,
        ICanonicalProjectContextStoreObservation freshStudioObservation,
        IDeveloperOriginalProjectCommandRead freshLiveStudioProjectRead,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
}
