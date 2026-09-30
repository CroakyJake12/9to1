using System.Collections.Frozen;
using Dulche.Runtime;
using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Current Home-owned native personal routing state. This reader never claims legacy/global, foreign,
/// app, Agent or organisation records and never invents a route when a profile has only an unsaved draft.</summary>
public sealed class HomePersonalModelRoutes(HomeLocalProfileIdentity profiles, IVersionedModelRouteRepository routes,
    ResourceAuthorizationService resources)
{
    public async Task<ConfiguredModelRoute?> GetAsync(ModelCapabilityCategory category, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(category)) return null;
        var actor = await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null || actor.AccountId is not null || actor.OrganisationId is not null) return null;
        var scope = new ResourceScope("home.profile-model-routes", actor.ProfileId, actor.AuthenticationRevision, ResourceAccess.Read);
        if (await resources.AuthorizeAsync("models.routes.read", [scope], cancellationToken).ConfigureAwait(false) != actor) return null;
        var id = HomeModelPickerFeatureProvider.RouteId(actor.ProfileId, category);
        var route = await routes.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (route is null || route.RouteId != id || route.Scope != ModelRouteScope.User || route.ScopeId != actor.ProfileId ||
            route.Category != category || route.Revision <= 0 || route.Candidates is null || route.Policy is null) return null;
        if (await resources.AuthorizeAsync("models.routes.read", [scope], cancellationToken).ConfigureAwait(false) != actor) return null;
        return route with { Candidates = Array.AsReadOnly(route.Candidates.ToArray()), Policy = route.Policy with
        { AllowedProviders = route.Policy.AllowedProviders?.ToFrozenSet(StringComparer.Ordinal),
            RequiredCapabilities = route.Policy.RequiredCapabilities?.ToFrozenSet(StringComparer.Ordinal) } };
    }
}
