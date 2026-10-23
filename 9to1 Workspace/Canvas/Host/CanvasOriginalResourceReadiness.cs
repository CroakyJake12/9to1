using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Canvas;

/// <summary>Borrowed genuine Home resource checks for the exact captured Canvas read scope.
/// The content separately revalidates its SAME Files owner/store/configuration and bytes.
/// This adapter supplies no principal, profile, scope grant, path resolver or Ready issuer.</summary>
internal sealed class CanvasOriginalResourceReadiness(HomeCoreRuntime sameRuntime,
    IAuthenticatedResourceActorSource sameActors, ResourceAuthorizationService sameResources,
    string actionId, Func<CancellationToken, ValueTask<IReadOnlyList<ResourceScope>>> capturedScopes,
    Action<Action>? originalHostSource = null, Action<Task>? retainOriginalSource = null)
    : ICuiSceneReadiness
{
    public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
    {
        var originals = new CloudflareOriginalTaskLedger();
        originals.BindOriginalOwner(this);
        originals.BindOriginalCallerCallback(originalHostSource);
        void Source(Action body) => originals.Invoke(() => { body(); return true; });
        void Retain(Task actual)
        {
            _ = originals.Track(actual); // Custody precedes the parent's callback boundary.
            if (retainOriginalSource is not null) Source(() => retainOriginalSource(actual));
        }
        return new(originals.RunToOriginalSettlementAsync(async () =>
        {
            if (sameActors is not IOriginalScopedResourceActorSource scopedActors)
                throw new NotSupportedException("The SAME Canvas Home actor lacks its original scoped producer.");
            Task<AuthenticatedResourceActor?> ReadActor() => originals.Invoke(() =>
                scopedActors.GetCurrentWithinOriginalSourceAsync(Source, Retain, token));
            var actor = await originals.AwaitAsync(ReadActor()).ConfigureAwait(false);
            if (actor is null) return Denied("The original Home actor is unavailable.");
            var runtime = await originals.AwaitAsync(originals.Invoke(() => sameRuntime.StartAsync(token))).ConfigureAwait(false);
            if (!new[] { "home.core", "home.state", "permissions.trust" }.All(id => runtime.Services.Any(row =>
                row.ServiceId == id && row.IsAvailable && row.State == HomeServiceLifecycleState.Ready &&
                row.ContractVersion.Major == HomeCoreServiceCatalog.CurrentContractVersion.Major &&
                row.ContractVersion.Minor >= HomeCoreServiceCatalog.CurrentContractVersion.Minor)))
                return Denied("The original Home services require recovery.");
            var scopes = await originals.AwaitAsync(originals.Invoke(() => capturedScopes(token).AsTask())).ConfigureAwait(false);
            if (actionId != "canvas.file.open" || scopes.Count is < 1 or > 8 ||
                scopes.Any(scope => scope.Kind != "files.item" || scope.Access != ResourceAccess.Read ||
                    string.IsNullOrWhiteSpace(scope.Id) || string.IsNullOrWhiteSpace(scope.Revision)))
                return Denied("Canvas has no exact captured canonical read scope.");
            var granted = await originals.AwaitAsync(originals.Invoke(() => sameResources.AuthorizeForActorWithinOriginalSourceAsync(
                actor, actionId, scopes, Source, Retain, token))).ConfigureAwait(false);
            var finalActor = await originals.AwaitAsync(ReadActor()).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return granted == actor && finalActor == actor
                ? new(CuiSceneAvailabilityState.Ready, "CanvasOriginalResourceAllowed", "The current Home actor and Files resource permit this view.")
                : Denied("The original Home actor or Files access changed.");
        }));
    }
    private static CuiSceneAvailability Denied(string message)
        => new(CuiSceneAvailabilityState.Unavailable, "CanvasOriginalResourceUnavailable", message);
}
