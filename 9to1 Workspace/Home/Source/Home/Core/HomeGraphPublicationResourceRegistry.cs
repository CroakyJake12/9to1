using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Read-only private graph-review associations. Separate from the broker-dependent owner
/// so resolving canonical resource owners cannot recursively construct the Home broker.</summary>
public sealed class HomeGraphPublicationResourceRegistry : ICanonicalResourceAccessResolver
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<HomeGraphPublicationOwner.Review, byte> _reviews = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<HomeSpaceGraphOwner.Operation, byte> _spaceAssociations = new();
    internal void RegisterSpaceAssociation(HomeSpaceGraphOwner.Operation operation)
    {
        if (_spaceAssociations.Count >= 128 || !_spaceAssociations.TryAdd(operation, 0))
            throw new InvalidOperationException("Original Space graph association capacity or identity is unavailable.");
    }
    internal void RetireSpaceAssociation(HomeSpaceGraphOwner.Operation operation) => _spaceAssociations.TryRemove(operation, out _);
    public string ResourceKind => "nodegraph.definition";
    internal void Register(HomeGraphPublicationOwner.Review review)
    {
        if (_reviews.Count >= 1024 || !_reviews.TryAdd(review, 0))
            throw new InvalidOperationException("Private graph resource association unavailable.");
    }
    internal void Retire(HomeGraphPublicationOwner.Review review) => _reviews.TryRemove(review, out _);
    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken ct)
    {
        var matches = _reviews.Keys.Where(review => review.MatchesResource(actor, actionId, scope)).ToArray();
        var associations = _spaceAssociations.Keys.Where(operation => operation.MatchesGraphAssociation(actor, actionId, scope)).ToArray();
        if (matches.Length + associations.Length != 1) return new(false, "GRAPH_PRIVATE_ORIGIN_UNAVAILABLE", actor.ActorId, scope.Revision, actor.OrganisationId);
        try
        {
            var admitted = matches.Length == 1 ? await matches[0].VerifyResourceAsync(ct).ConfigureAwait(false) :
                await associations[0].VerifyGraphAssociationAsync(ct).ConfigureAwait(false);
            return new(admitted, "GRAPH_PRIVATE_ORIGIN_CHECKED",
                actor.ActorId, scope.Revision, actor.OrganisationId);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or InvalidDataException)
        { return new(false, "GRAPH_PRIVATE_ORIGIN_REJECTED", actor.ActorId, scope.Revision, actor.OrganisationId); }
    }
}
