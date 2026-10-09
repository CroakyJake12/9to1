namespace Haven.Application.Go;

/// <summary>Optional original-session discovery. Expected actor metadata is not authority; the owner checks its actual actor before and after owner reads.</summary>
public interface IGoOriginalActorQuery : IGoProvider
{
    IAsyncEnumerable<GoResult> QueryForActorAsync(GoQuery query, AuthenticatedResourceActor expectedActor,
        CancellationToken cancellationToken);
}

/// <summary>Optional fresh canonical resolution bound to the original session; no legacy resolution fallback.</summary>
public interface IGoOriginalActorCanonicalResolver : IGoProvider
{
    Task<GoResult?> ResolveForActorAsync(GoCanonicalLocator locator, AuthenticatedResourceActor expectedActor,
        CancellationToken cancellationToken);
}
