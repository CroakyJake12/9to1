using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>Owning native routes supply their current canonical read scopes, never a constant readiness grant.</summary>
public sealed class HomeResourceCuiReadiness(HomeCoreRuntime home, IAuthenticatedResourceActorSource actors,
    ResourceAuthorizationService resources, string actionId,
    Func<CancellationToken, ValueTask<IReadOnlyList<ResourceScope>>> currentScopes) : ICuiSceneReadiness
{
    public async ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (actor is null) return Unavailable("HomeProfileUnavailable", "Open Home to recover this profile.");
            var snapshot = await home.StartAsync(cancellationToken).ConfigureAwait(false);
            foreach (var id in new[] { "home.core", "home.state", "permissions.trust" })
                if (!snapshot.Services.Any(service => service.ServiceId == id && service.IsAvailable &&
                    service.State == HomeServiceLifecycleState.Ready &&
                    service.ContractVersion.Major == HomeCoreServiceCatalog.CurrentContractVersion.Major &&
                    service.ContractVersion.Minor >= HomeCoreServiceCatalog.CurrentContractVersion.Minor))
                    return Unavailable("HomeServicesUnavailable", "Home services need repair before opening this content.");
            var scopes = await currentScopes(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(actionId) || scopes.Count == 0 || scopes.Any(scope => scope.Access != ResourceAccess.Read))
                return Unavailable("ResourceScopeUnavailable", "The owning app has no current canonical read scope.");
            if (await resources.AuthorizeAsync(actionId, scopes, cancellationToken).ConfigureAwait(false) != actor ||
                actor != await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false))
                return Unavailable("ResourceAccessChanged", "Current Home and resource access do not permit this content.");
            return new(CuiSceneAvailabilityState.Ready, "HomeResourceReady", "Home and the owning resource permit this view.");
        }
        catch (UnauthorizedAccessException)
        {
            return Unavailable("ResourceAccessDenied", "Current Home and resource access do not permit this content.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            return Unavailable("HomeRecoveryRequired", "Home or the owning store needs recovery. Existing data was preserved.");
        }
    }

    private static CuiSceneAvailability Unavailable(string code, string message) => new(CuiSceneAvailabilityState.Unavailable, code, message);
}
