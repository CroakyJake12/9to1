using Haven.Application;
namespace HavenOS.Home.Core;

public sealed partial class HomeResourceStoreOwnershipAuthority : IResourceStoreOriginalScopedOwnershipAuthority
{
    private HomeLocalProfileIdentity RequireOriginalScopedProfile() => actors is HomeLocalProfileIdentity profile &&
        ownership.HasOriginalProfile(profile) ? profile : throw new InvalidOperationException("The SAME actual scoped Home profile is required.");

    public async ValueTask<VerifiedResourceStoreOwnership?> GetVerifiedWithinOriginalSourceAsync(string resourceKind, string storeId,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        var profile = RequireOriginalScopedProfile(); var source = new HomeOwnershipOriginalSourceCallbacks(originalSynchronousScope, retainOriginalTask);
        var actor = await source.ReadAsync(() => profile.GetCurrentAsync(source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
        if (actor is null || actor.OrganisationId is not null) return null;
        var binding = await source.ReadAsync(() => ownership.GetVerifiedWithinOriginalSourceAsync(resourceKind, storeId, source.Run, source.Retain, token)).ConfigureAwait(false);
        var current = await source.ReadAsync(() => profile.GetCurrentAsync(source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
        if (binding is null || actor != current || binding.ProfileId != actor.ProfileId) return null;
        var receipt = await source.ReadAsync(() => ownership.CaptureReceiptWithinOriginalSourceAsync(binding, source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
        if (receipt is null || await source.ReadAsync(() => profile.GetCurrentAsync(source.Run, source.Retain, token).AsTask()).ConfigureAwait(false) != actor) return null;
        return new(binding.ResourceKind, binding.StoreId, binding.ProfileId, binding.ObservedStoreRevision) { Receipt = receipt };
    }
    public async ValueTask<bool> IsCurrentWithinOriginalSourceAsync(VerifiedResourceStoreOwnership captured, AuthenticatedResourceActor expectedActor,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        var profile = RequireOriginalScopedProfile(); var source = new HomeOwnershipOriginalSourceCallbacks(originalSynchronousScope, retainOriginalTask);
        if (captured.Receipt is null || expectedActor.AccountId is not null || expectedActor.OrganisationId is not null ||
            expectedActor.ProfileId != captured.ProfileId ||
            await source.ReadAsync(() => profile.GetCurrentAsync(source.Run, source.Retain, token).AsTask()).ConfigureAwait(false) != expectedActor) return false;
        return await source.ReadAsync(() => ownership.IsReceiptCurrentWithinOriginalSourceAsync(captured, source.Run, source.Retain, token).AsTask()).ConfigureAwait(false) &&
            await source.ReadAsync(() => profile.GetCurrentAsync(source.Run, source.Retain, token).AsTask()).ConfigureAwait(false) == expectedActor;
    }
}
