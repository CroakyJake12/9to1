using Haven.Core;

namespace Haven.Application;

// PRIVATE PROPOSAL: display observations never issue project/content/effect authority.
// A foreign implementation cannot qualify: the actual configured source must recognize
// the exact observation, and native composition compares the actual known owner references.
// An opaque source-issued position narrows an already authorized metadata query.
// It is never a store, project, content or effect grant.
public interface ICanonicalProjectContextStoreContinuation { }

public interface ICanonicalProjectContextStoreObservation
{
    AuthenticatedResourceActor Actor { get; }
    ResourceStoreIdentity OriginalStoreIdentity { get; }
    VerifiedResourceStoreOwnership OriginalStoreOwnership { get; }
    IReadOnlyList<Conversation> Conversations { get; }
    IReadOnlyList<ContainerDefinition> Containers { get; }
    bool HasMore { get; }
    ICanonicalProjectContextStoreContinuation? NextContinuation => null;
}

public interface ICanonicalProjectContextStoreReadSource
{
    Task<ICanonicalProjectContextStoreObservation> ReadOriginalProjectContextsWithinSourceAsync(
        AuthenticatedResourceActor actualActor, int maximum,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        CancellationToken token, Guid? originalConversationId = null, bool includeStudioContexts = false);
    // Same protected source/actor/store READ is required on every page. Search is
    // literal title/container-name filtering only, never an ID or path grant.
    Task<ICanonicalProjectContextStoreObservation> ReadOriginalProjectContextPageWithinSourceAsync(
        AuthenticatedResourceActor actualActor, int maximum,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        CancellationToken token, ICanonicalProjectContextStoreContinuation? continuation = null,
        bool includeStudioContexts = false, string? searchText = null) =>
        Task.FromException<ICanonicalProjectContextStoreObservation>(new NotSupportedException(
            "This configured source does not support original project catalogue pages."));
    bool IsIssuedOriginalContinuation(ICanonicalProjectContextStoreContinuation sameContinuation) => false;
    bool IsAcknowledgedOriginalReadRefusal(Task sameOriginalRead);
    bool IsIssuedOriginalObservation(ICanonicalProjectContextStoreObservation actualObservation);
    Task RevalidateOriginalObservationWithinSourceAsync(ICanonicalProjectContextStoreObservation actualObservation,
        AuthenticatedResourceActor actualActor, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
}
