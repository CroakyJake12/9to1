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

/// <summary>Opaque observation issued by the registered canonical owner. Caller implementations confer no authority.</summary>
public interface IOriginalCanonicalReadContext
{
    ResourceScope OriginalScope { get; }
    // Observational data only: read it only after the registered owner validates private issuer pairing.
    Guid? OriginalMaterializationFolderId => null;
}
/// <summary>Separate owner-issued write observation. It is not a claimed Home capability or final commit lease.</summary>
public interface IOriginalCanonicalWriteContext { ResourceScope OriginalScope { get; } }
public interface IOriginalCanonicalWriteResolver : ICanonicalResourceAccessResolver
{
    bool IsIssuedOriginalWrite(IOriginalCanonicalWriteContext context, AuthenticatedResourceActor actor,
        string actionId, ResourceScope scope);
    ValueTask<ResourceAccessDecision> EvaluateOriginalWriteAsync(IOriginalCanonicalWriteContext context,
        AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken cancellationToken);
}

public interface IOriginalCanonicalReadResolver : ICanonicalResourceAccessResolver
{
    bool IsIssuedOriginalRead(IOriginalCanonicalReadContext context, AuthenticatedResourceActor actor,
        string actionId, ResourceScope scope);
    bool IsIssuedOriginalReadOwnerBinding(IOriginalCanonicalReadContext context, object originalProvider, object originalDirectories, Guid expectedStoreId);
    ValueTask<ResourceAccessDecision> EvaluateOriginalReadAsync(IOriginalCanonicalReadContext context,
        AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken cancellationToken);
}

/// <summary>Intersection of verified ambient actor and each canonical owner's current resource authority.</summary>
public sealed partial class ResourceAuthorizationService(IAuthenticatedResourceActorSource actors,
    IEnumerable<ICanonicalResourceAccessResolver> resolvers)
{
    /// <summary>Pure component identity only; this grants no actor or resource access.</summary>
    public bool IsBoundToActorSource(IAuthenticatedResourceActorSource expected) => ReferenceEquals(actors, expected);

    private readonly ICanonicalResourceAccessResolver[] _resolvers = resolvers.ToArray();

    public ValueTask<AuthenticatedResourceActor?> AuthorizeAsync(string actionId, IReadOnlyList<ResourceScope> scopes,
        CancellationToken cancellationToken = default)
        => AuthorizeCoreAsync(null, actionId, scopes, cancellationToken);

    /// <summary>Checks the actual current actor against the originating actor before any owning resolver is invoked.</summary>
    public ValueTask<AuthenticatedResourceActor?> AuthorizeForActorAsync(AuthenticatedResourceActor expectedActor,
        string actionId, IReadOnlyList<ResourceScope> scopes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        return AuthorizeCoreAsync(expectedActor, actionId, scopes, cancellationToken);
    }

    /// <summary>Pure deny-only pairing with the canonical owner's private provider/materialization composition. No actor or resource grant.</summary>
    public bool IsIssuedOriginalReadOwnerBinding(AuthenticatedResourceActor expectedActor,
        IOriginalCanonicalReadContext original, string actionId, ResourceScope scope,
        object originalProvider, object originalDirectories, Guid expectedStoreId)
    {
        if (expectedActor is null || original is null || scope is null || originalProvider is null || originalDirectories is null) return false;
        var owners = _resolvers.Where(resolver => resolver.ResourceKind == scope.Kind).ToArray();
        return owners.Length == 1 && owners[0] is IOriginalCanonicalReadResolver owner
            && owner.IsIssuedOriginalRead(original, expectedActor, actionId, scope)
            && owner.IsIssuedOriginalReadOwnerBinding(original, originalProvider, originalDirectories, expectedStoreId);
    }

    /// <summary>No ambient resolver fallback: each scope must belong to the same registered owner that privately issued this original observation.</summary>
    public async ValueTask<AuthenticatedResourceActor?> AuthorizeOriginalReadForActorAsync(
        AuthenticatedResourceActor expectedActor, IOriginalCanonicalReadContext original,
        string actionId, IReadOnlyList<ResourceScope> scopes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedActor); ArgumentNullException.ThrowIfNull(original);
        if (string.IsNullOrWhiteSpace(actionId) || scopes is null || scopes.Count == 0 || scopes.Count > 1000) return null;
        if (string.IsNullOrWhiteSpace(expectedActor.ActorId) || string.IsNullOrWhiteSpace(expectedActor.ProfileId)
            || string.IsNullOrWhiteSpace(expectedActor.AuthenticationRevision) || expectedActor.AccountId == Guid.Empty
            || expectedActor.OrganisationId == Guid.Empty || (expectedActor.OrganisationId is not null && expectedActor.AccountId is null)) return null;
        if (await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != expectedActor) return null;
        foreach (var scope in scopes)
        {
            if (scope is null || scope.Access != ResourceAccess.Read || string.IsNullOrWhiteSpace(scope.Kind)
                || string.IsNullOrWhiteSpace(scope.Id) || string.IsNullOrWhiteSpace(scope.Revision)) return null;
            var owners = _resolvers.Where(resolver => resolver.ResourceKind == scope.Kind).ToArray();
            if (owners.Length != 1 || owners[0] is not IOriginalCanonicalReadResolver owner
                || !owner.IsIssuedOriginalRead(original, expectedActor, actionId, scope)) return null;
            if (await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != expectedActor) return null;
            var decision = await owner.EvaluateOriginalReadAsync(original, expectedActor, actionId, scope, cancellationToken).ConfigureAwait(false);
            if (!decision.Allowed || decision.ActorId != expectedActor.ActorId || decision.ResourceRevision != scope.Revision
                || decision.OrganisationId != expectedActor.OrganisationId) return null;
        }
        return await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) == expectedActor ? expectedActor : null;
    }

    /// <summary>Pure private-issuer check before any approval/claim consumption or owner I/O.</summary>
    public bool IsOriginalWriteContextForActor(AuthenticatedResourceActor expectedActor,
        IOriginalCanonicalWriteContext original, string actionId, IReadOnlyList<ResourceScope> scopes)
    {
        if (expectedActor is null || original is null || scopes is null || scopes.Count != 1 || string.IsNullOrWhiteSpace(actionId)) return false;
        var scope = scopes[0];
        if (scope is null || scope.Access != ResourceAccess.Write || string.IsNullOrWhiteSpace(scope.Kind) ||
            string.IsNullOrWhiteSpace(scope.Id) || string.IsNullOrWhiteSpace(scope.Revision)) return false;
        var owners = _resolvers.Where(resolver => resolver.ResourceKind == scope.Kind).Take(2).ToArray();
        return owners.Length == 1 && owners[0] is IOriginalCanonicalWriteResolver owner &&
            owner.IsIssuedOriginalWrite(original, expectedActor, actionId, scope);
    }

    /// <summary>Observation through the same registered private write issuer, with no ambient owner fallback.</summary>
    public async ValueTask<AuthenticatedResourceActor?> AuthorizeOriginalWriteForActorAsync(
        AuthenticatedResourceActor expectedActor, IOriginalCanonicalWriteContext original,
        string actionId, IReadOnlyList<ResourceScope> scopes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedActor); ArgumentNullException.ThrowIfNull(original);
        if (!IsOriginalWriteContextForActor(expectedActor, original, actionId, scopes) ||
            string.IsNullOrWhiteSpace(expectedActor.ActorId) || string.IsNullOrWhiteSpace(expectedActor.ProfileId) ||
            string.IsNullOrWhiteSpace(expectedActor.AuthenticationRevision) || expectedActor.AccountId == Guid.Empty ||
            expectedActor.OrganisationId == Guid.Empty || (expectedActor.OrganisationId is not null && expectedActor.AccountId is null)) return null;
        if (await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != expectedActor) return null;
        var scope = scopes[0];
        var owner = (IOriginalCanonicalWriteResolver)_resolvers.Single(resolver => resolver.ResourceKind == scope.Kind);
        var decision = await owner.EvaluateOriginalWriteAsync(original, expectedActor, actionId, scope, cancellationToken).ConfigureAwait(false);
        if (!decision.Allowed || decision.ActorId != expectedActor.ActorId || decision.ResourceRevision != scope.Revision ||
            decision.OrganisationId != expectedActor.OrganisationId) return null;
        return await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) == expectedActor ? expectedActor : null;
    }

    private async ValueTask<AuthenticatedResourceActor?> AuthorizeCoreAsync(AuthenticatedResourceActor? expectedActor,
        string actionId, IReadOnlyList<ResourceScope> scopes, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actionId) || scopes is null || scopes.Count == 0 || scopes.Count > 1000) return null;
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null || string.IsNullOrWhiteSpace(actor.ActorId) || string.IsNullOrWhiteSpace(actor.ProfileId) ||
            string.IsNullOrWhiteSpace(actor.AuthenticationRevision) || actor.AccountId == Guid.Empty || actor.OrganisationId == Guid.Empty ||
            (actor.OrganisationId is not null && actor.AccountId is null)) return null;
        if (expectedActor is not null && actor != expectedActor) return null;
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
