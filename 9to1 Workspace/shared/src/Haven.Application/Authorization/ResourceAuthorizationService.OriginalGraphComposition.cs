namespace Haven.Application;

public sealed partial class ResourceAuthorizationService
{
    // Maintained Home composition validation only, never a permission decision.
    internal bool HasSingleOriginalGraphResolver(ICanonicalResourceAccessResolver original) =>
        _resolvers.Count(resolver => resolver.ResourceKind == original.ResourceKind) == 1 &&
        _resolvers.Any(resolver => ReferenceEquals(resolver, original));
}
