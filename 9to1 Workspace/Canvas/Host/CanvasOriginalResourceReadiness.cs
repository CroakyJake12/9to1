using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Canvas;

/// <summary>Borrowed genuine Home resource checks for the exact captured Canvas read scope.
/// The window separately revalidates its SAME Files owner/store/configuration and bytes.
/// This adapter supplies no principal, profile, scope grant, path resolver or Ready issuer.</summary>
internal sealed class CanvasOriginalResourceReadiness(HomeCoreRuntime sameRuntime,
    IAuthenticatedResourceActorSource sameActors, ResourceAuthorizationService sameResources,
    string actionId, Func<CancellationToken, ValueTask<IReadOnlyList<ResourceScope>>> capturedScopes)
    : ICuiSceneReadiness
{
    public async ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
    {
        var actor = await Observe(sameActors.GetCurrentAsync(token).AsTask()).ConfigureAwait(false);
        if (actor is null) return Denied("The original Home actor is unavailable.");
        var runtime = await Observe(sameRuntime.StartAsync(token)).ConfigureAwait(false);
        if (!new[] { "home.core", "home.state", "permissions.trust" }.All(id => runtime.Services.Any(row =>
            row.ServiceId == id && row.IsAvailable && row.State == HomeServiceLifecycleState.Ready &&
            row.ContractVersion.Major == HomeCoreServiceCatalog.CurrentContractVersion.Major &&
            row.ContractVersion.Minor >= HomeCoreServiceCatalog.CurrentContractVersion.Minor)))
            return Denied("The original Home services require recovery.");
        var scopes = await Observe(capturedScopes(token).AsTask()).ConfigureAwait(false);
        if (actionId != "canvas.file.open" || scopes.Count is < 1 or > 8 ||
            scopes.Any(scope => scope.Kind != "files.item" || scope.Access != ResourceAccess.Read ||
                string.IsNullOrWhiteSpace(scope.Id) || string.IsNullOrWhiteSpace(scope.Revision)))
            return Denied("Canvas has no exact captured canonical read scope.");
        var granted = await Observe(sameResources.AuthorizeAsync(actionId, scopes, token).AsTask()).ConfigureAwait(false);
        var finalActor = await Observe(sameActors.GetCurrentAsync(token).AsTask()).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return granted == actor && finalActor == actor
            ? new(CuiSceneAvailabilityState.Ready, "CanvasOriginalResourceAllowed", "The current Home actor and Files resource permit this view.")
            : Denied("The original Home actor or Files access changed.");
    }
    private static CuiSceneAvailability Denied(string message)
        => new(CuiSceneAvailabilityState.Unavailable, "CanvasOriginalResourceUnavailable", message);
    private static async Task<T> Observe<T>(Task<T> actual)
    { try { return await actual.ConfigureAwait(false); } catch when (actual.IsFaulted) { throw actual.Exception!; } }
}
