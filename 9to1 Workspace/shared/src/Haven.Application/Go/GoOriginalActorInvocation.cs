namespace Haven.Application.Go;

/// <summary>
/// Optional original-session owner port. ExpectedActor is retained trusted-host metadata, never a permission.
/// Implementations must freshly resolve canonical identity/revision and current authority, compare the full
/// original actor before and after asynchronous admission, and enforce it at the actual owner commit.
/// Providers lacking this port cannot fall back to current-actor invocation on the original-session path.
/// </summary>
public interface IGoOriginalActorInvocation : IGoProvider
{
    Task InvokeForActorAsync(GoCanonicalReference reference, string actionId,
        AuthenticatedResourceActor expectedActor, CancellationToken cancellationToken);
}
