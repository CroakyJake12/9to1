using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Domain;
using Xunit;

namespace HavenOS.Apps.Sites.Tests;

/// <summary>ED-SITES-002: routes have stable source identity plus URL mapping; renaming/reparenting can propose redirects and validate collisions.
/// Proves rename preserves source identity while proposing a redirect, collision with an existing route is rejected, cyclic redirect chains are
/// refused, and reparent proposes redirects for affected descendants.</summary>
public sealed class SiteRouteRedirectModelTests
{
    private static readonly Guid PageA = Guid.NewGuid();
    private static readonly Guid PageB = Guid.NewGuid();
    private static readonly Guid RouteA = Guid.NewGuid();
    private static readonly Guid RouteB = Guid.NewGuid();

    private static SiteRoute Route(Guid id, Guid pageId, string pattern) => new(id, pageId, pattern, SiteRouteKind.Static);
    private static SiteRedirect Redirect(string source, string destination) => new(Guid.NewGuid(), source, destination, 301, null);

    [Fact]
    public void RenamePreservesSourceIdentityAndProposesRedirect()
    {
        var route = Route(RouteA, PageA, "/old");
        var proposal = SiteRouteRedirectModel.ProposeRename(route, "/new");

        // Stable source identity preserved; only the URL mapping changes.
        Assert.Equal(RouteA, proposal.RouteId);
        Assert.Equal(PageA, proposal.PageId);
        Assert.Equal("/old", proposal.SourcePath);
        Assert.Equal("/new", proposal.DestinationPath);
        Assert.Equal(301, proposal.StatusCode);
    }

    [Fact]
    public void RenameToSamePatternIsRejected()
    {
        var route = Route(RouteA, PageA, "/same");
        Assert.Throws<InvalidOperationException>(() => SiteRouteRedirectModel.ProposeRename(route, "/same"));
    }

    [Fact]
    public void CollisionWithExistingRouteIsRejected()
    {
        var routes = new[] { Route(RouteA, PageA, "/a"), Route(RouteB, PageB, "/b") };
        Assert.Equal(SiteRouteValidation.DuplicateRoute, SiteRouteRedirectModel.ValidateCollision("/b", routes, []));
        // Renaming A to /b still collides with B.
        Assert.Equal(SiteRouteValidation.DuplicateRoute, SiteRouteRedirectModel.ValidateCollision("/b", routes, [], renamingRouteId: RouteA));
    }

    [Fact]
    public void ValidNewPatternPasses()
    {
        var routes = new[] { Route(RouteA, PageA, "/a") };
        Assert.Equal(SiteRouteValidation.Valid, SiteRouteRedirectModel.ValidateCollision("/c", routes, []));
    }

    [Fact]
    public void SelfRedirectIsRejected()
    {
        var redirects = new[] { Redirect("/x", "/x") };
        Assert.Equal(SiteRouteValidation.SelfRedirect, SiteRouteRedirectModel.ValidateCollision("/y", [], redirects));
    }

    [Fact]
    public void CyclicRedirectChainIsRefused()
    {
        // /a -> /b -> /a forms a cycle.
        var redirects = new[] { Redirect("/a", "/b"), Redirect("/b", "/a") };
        Assert.Equal(SiteRouteValidation.CyclicRedirect, SiteRouteRedirectModel.ValidateCollision("/c", [], redirects));
    }

    [Fact]
    public void AcyclicRedirectChainIsAllowed()
    {
        var redirects = new[] { Redirect("/a", "/b"), Redirect("/b", "/c") };
        Assert.Equal(SiteRouteValidation.Valid, SiteRouteRedirectModel.ValidateCollision("/d", [], redirects));
    }

    [Fact]
    public void ReparentProposesRedirectsForAffectedDescendants()
    {
        var moved = Route(RouteA, PageA, "/blog");
        var child = Route(RouteB, PageB, "/blog/post-1");
        var unrelated = Route(Guid.NewGuid(), Guid.NewGuid(), "/about");

        var proposals = SiteRouteRedirectModel.ProposeReparent(moved, "/journal", [child, unrelated]);

        // The moved route and its descendant get redirects; the unrelated route is untouched.
        Assert.Contains(proposals, p => p.RouteId == RouteA && p.DestinationPath == "/journal");
        Assert.Contains(proposals, p => p.RouteId == RouteB && p.SourcePath == "/blog/post-1" && p.DestinationPath == "/journal/post-1");
        Assert.DoesNotContain(proposals, p => p.RouteId == unrelated.RouteId);
    }

    [Fact]
    public void EmptyPatternIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => SiteRouteRedirectModel.ValidateCollision(" ", [], []));
    }
}
