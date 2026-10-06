using Haven.Application;
namespace HavenOS.Home.Core;
/// <summary>Lazy exact owner composition breaks broker→resource owner→broker construction cycles.
/// No permission, account or caller metadata is synthesized by this adapter.</summary>
public sealed class HomeCloudflareResourceResolver(Func<HomeCloudflareServiceOwner> originalOwner) : ICanonicalResourceAccessResolver
{
    private readonly object _sync = new(); private HomeCloudflareServiceOwner? _actual;
    public string ResourceKind => "cloudflare.resource";
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken token)
    {
        var current = originalOwner();
        lock (_sync)
        { if (_actual is not null && !ReferenceEquals(_actual, current)) throw new UnauthorizedAccessException("Canonical Home Cloudflare owner changed."); _actual ??= current; }
        return current.EvaluateAsync(actor, action, scope, token);
    }
}
