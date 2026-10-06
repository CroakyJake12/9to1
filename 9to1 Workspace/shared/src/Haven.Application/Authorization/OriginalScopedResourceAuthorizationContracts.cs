namespace Haven.Application;

/// <summary>Actual configured resource actor producer with finite callback/raw-task custody.
/// Implementing the interface or observing an actor value creates no resource authority.</summary>
public interface IOriginalScopedResourceActorSource : IAuthenticatedResourceActorSource
{
    Task<AuthenticatedResourceActor?> GetCurrentWithinOriginalSourceAsync(
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
}

/// <summary>SAME registered canonical resolver; no fallback to unscoped callbacks.
/// The existing actor/scope/ACL decision predicates remain mandatory.</summary>
public interface IOriginalScopedCanonicalResourceAccessResolver : ICanonicalResourceAccessResolver
{
    Task<ResourceAccessDecision> EvaluateWithinOriginalSourceAsync(AuthenticatedResourceActor sameActor,
        string actionId, ResourceScope sameScope, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
}
