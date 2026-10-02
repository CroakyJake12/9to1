using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Read-only private graph-review associations. Separate from the broker-dependent owner
/// so resolving canonical resource owners cannot recursively construct the Home broker.</summary>
public sealed class HomeGraphPublicationResourceRegistry : ICanonicalResourceAccessResolver
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<HomeGraphPublicationOwner.Review, byte> _reviews = new();
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
        if (matches.Length != 1) return new(false, "GRAPH_PRIVATE_ORIGIN_UNAVAILABLE", actor.ActorId, scope.Revision, actor.OrganisationId);
        try
        {
            return new(await matches[0].VerifyResourceAsync(ct).ConfigureAwait(false), "GRAPH_PRIVATE_ORIGIN_CHECKED",
                actor.ActorId, scope.Revision, actor.OrganisationId);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or InvalidDataException)
        { return new(false, "GRAPH_PRIVATE_ORIGIN_REJECTED", actor.ActorId, scope.Revision, actor.OrganisationId); }
    }
}
