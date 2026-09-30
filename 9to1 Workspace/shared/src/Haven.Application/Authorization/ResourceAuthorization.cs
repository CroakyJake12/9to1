namespace Haven.Application;

/// <summary>Derived by the trusted host from its authenticated session/profile; never deserialised from operation arguments.</summary>
public sealed record AuthenticatedResourceActor(string ActorId, string ProfileId, Guid? AccountId,
    Guid? OrganisationId, string AuthenticationRevision);
public interface IAuthenticatedResourceActorSource
{
    ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken);
}

public enum ResourceAccess { Read, Write, Execute }
public sealed record ResourceScope(string Kind, string Id, string Revision, ResourceAccess Access);
public sealed record ResourceAccessDecision(bool Allowed, string Code, string ActorId,
    string ResourceRevision, Guid? OrganisationId);

/// <summary>Owning service resolves actual records and current ACLs, including owner/calendar/read-only/organisation restrictions.</summary>
public interface ICanonicalResourceAccessResolver
{
    string ResourceKind { get; }
    ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken cancellationToken);
}

/// <summary>Intersection of verified ambient actor and each canonical owner's current resource authority.</summary>
public sealed class ResourceAuthorizationService(IAuthenticatedResourceActorSource actors,
    IEnumerable<ICanonicalResourceAccessResolver> resolvers)
{
    private readonly ICanonicalResourceAccessResolver[] _resolvers = resolvers.ToArray();

    public async ValueTask<AuthenticatedResourceActor?> AuthorizeAsync(string actionId, IReadOnlyList<ResourceScope> scopes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actionId) || scopes is null || scopes.Count == 0 || scopes.Count > 1000) return null;
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null || string.IsNullOrWhiteSpace(actor.ActorId) || string.IsNullOrWhiteSpace(actor.ProfileId) ||
            string.IsNullOrWhiteSpace(actor.AuthenticationRevision) || actor.AccountId == Guid.Empty || actor.OrganisationId == Guid.Empty ||
            (actor.OrganisationId is not null && actor.AccountId is null)) return null;
        foreach (var scope in scopes)
        {
            if (scope is null || string.IsNullOrWhiteSpace(scope.Kind) || string.IsNullOrWhiteSpace(scope.Id) ||
                string.IsNullOrWhiteSpace(scope.Revision) || !Enum.IsDefined(scope.Access)) return null;
            var owners = _resolvers.Where(resolver => resolver.ResourceKind == scope.Kind).ToArray();
            if (owners.Length != 1) return null;
            var decision = await owners[0].EvaluateAsync(actor, actionId, scope, cancellationToken).ConfigureAwait(false);
            if (!decision.Allowed || decision.ActorId != actor.ActorId || decision.ResourceRevision != scope.Revision ||
                decision.OrganisationId != actor.OrganisationId) return null;
        }
        return await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) == actor ? actor : null;
    }
}
