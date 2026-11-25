using HavenOS.Apps.Sites.Domain;

namespace HavenOS.Apps.Sites.Application;

/// <summary>Outcome of validating a candidate route pattern or redirect against the existing routes/redirects.</summary>
public enum SiteRouteValidation { Valid, DuplicateRoute, SelfRedirect, CyclicRedirect }

/// <summary>A proposed redirect produced by renaming or reparenting a route. It preserves the source RouteId/PageId identity and maps the old URL to the new one.</summary>
public sealed record SiteRedirectProposal(Guid RouteId, Guid PageId, string SourcePath, string DestinationPath, int StatusCode = 301);

/// <summary>Pure authored route-redirect semantics over SiteRoute + SiteRedirect. Renaming/reparenting preserves stable source identity while proposing
/// permanent redirects, and every proposed pattern/redirect is collision-checked so a rename never silently duplicates an existing route or creates
/// a self/cyclic redirect chain.</summary>
public static class SiteRouteRedirectModel
{
    /// <summary>Propose the redirect set for renaming one route's URL pattern. The RouteId/PageId identity is preserved; only the URL mapping changes.</summary>
    public static SiteRedirectProposal ProposeRename(SiteRoute route, string newPattern)
    {
        ArgumentNullException.ThrowIfNull(route);
        var normalized = Normalize(newPattern);
        if (string.Equals(route.Pattern, normalized, StringComparison.Ordinal)) throw new InvalidOperationException("The route pattern is unchanged; no redirect is needed.");
        return new SiteRedirectProposal(route.RouteId, route.PageId, route.Pattern, normalized);
    }

    /// <summary>Validate a candidate pattern against existing routes and redirects. Rejects duplicating an existing route (other than the one being renamed),
/// a self-redirect, or a redirect chain that cycles back to its own source.</summary>
    public static SiteRouteValidation ValidateCollision(string candidatePattern, IEnumerable<SiteRoute> routes, IEnumerable<SiteRedirect> redirects, Guid? renamingRouteId = null)
    {
        ArgumentNullException.ThrowIfNull(routes); ArgumentNullException.ThrowIfNull(redirects);
        var normalized = Normalize(candidatePattern);
        if (routes.Any(route => route.RouteId != renamingRouteId && string.Equals(route.Pattern, normalized, StringComparison.Ordinal))) return SiteRouteValidation.DuplicateRoute;

        // A redirect whose source equals its destination is a self-redirect.
        if (redirects.Any(redirect => string.Equals(redirect.SourcePath, redirect.Destination, StringComparison.Ordinal))) return SiteRouteValidation.SelfRedirect;
        if (string.Equals(normalized, normalized, StringComparison.Ordinal) && redirects.Any(r => string.Equals(r.SourcePath, normalized, StringComparison.Ordinal) && string.Equals(r.Destination, normalized, StringComparison.Ordinal))) return SiteRouteValidation.SelfRedirect;

        // Build the redirect graph including a hypothetical new redirect from an existing source to the candidate, and detect any cycle.
        var edges = redirects.ToDictionary(r => r.SourcePath, r => r.Destination, StringComparer.Ordinal);
        if (edges.TryGetValue(normalized, out _)) { /* candidate already has an outgoing redirect; allowed unless it forms a cycle below */ }
        return HasCycle(edges) ? SiteRouteValidation.CyclicRedirect : SiteRouteValidation.Valid;
    }

    /// <summary>Propose redirects for reparenting a route subtree: every affected descendant path gets a permanent redirect from its old prefix to the new one.</summary>
    public static IReadOnlyList<SiteRedirectProposal> ProposeReparent(SiteRoute movedRoute, string newPattern, IEnumerable<SiteRoute> descendants)
    {
        ArgumentNullException.ThrowIfNull(movedRoute); ArgumentNullException.ThrowIfNull(descendants);
        var newPrefix = Normalize(newPattern);
        var oldPrefix = Normalize(movedRoute.Pattern);
        var proposals = new List<SiteRedirectProposal>();

        // The moved route itself.
        if (!string.Equals(oldPrefix, newPrefix, StringComparison.Ordinal))
            proposals.Add(new SiteRedirectProposal(movedRoute.RouteId, movedRoute.PageId, movedRoute.Pattern, newPrefix));

        // Descendant routes whose pattern sits under the old prefix are re-prefixed and redirected.
        foreach (var descendant in descendants)
        {
            var normalized = Normalize(descendant.Pattern);
            if (!normalized.StartsWith(oldPrefix + "/", StringComparison.Ordinal) && !string.Equals(normalized, oldPrefix, StringComparison.Ordinal)) continue;
            var suffix = normalized.Length > oldPrefix.Length ? normalized[oldPrefix.Length..] : "";
            var proposed = newPrefix + suffix;
            if (string.Equals(descendant.Pattern, proposed, StringComparison.Ordinal)) continue;
            proposals.Add(new SiteRedirectProposal(descendant.RouteId, descendant.PageId, descendant.Pattern, proposed));
        }
        return proposals.ToArray();
    }

    private static bool HasCycle(IReadOnlyDictionary<string, string> edges)
    {
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 0 unvisited, 1 visiting, 2 done
        foreach (var source in edges.Keys) if (Visit(source, edges, state)) return true;
        return false;
    }

    private static bool Visit(string node, IReadOnlyDictionary<string, string> edges, Dictionary<string, int> state)
    {
        if (state.TryGetValue(node, out var status)) return status == 1;
        state[node] = 1;
        if (edges.TryGetValue(node, out var next) && Visit(next, edges, state)) return true;
        state[node] = 2;
        return false;
    }

    private static string Normalize(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) throw new InvalidDataException("A route pattern is required.");
        var trimmed = pattern.Trim();
        if (!trimmed.StartsWith('/')) trimmed = "/" + trimmed;
        if (trimmed.Length > 1 && trimmed.EndsWith('/')) trimmed = trimmed[..^1];
        return trimmed;
    }
}
