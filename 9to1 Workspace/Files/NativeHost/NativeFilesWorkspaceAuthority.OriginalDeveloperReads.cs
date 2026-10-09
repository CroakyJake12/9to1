using Haven.Application;

namespace HavenOS.Files.NativeHost;

public sealed partial class NativeFilesWorkspaceAuthority
{
    internal async Task<NativeFilesWorkspace?> GetOriginalCurrentAsync(Guid? expectedStoreId, FilesOriginalReadSourceScope original, CancellationToken cancellationToken)
    {
        var actor = await original.Observe(() => profiles.GetCurrentAsync(cancellationToken).AsTask()).ConfigureAwait(false);
        var workspace = await original.Observe(() => workspaces.GetOriginalConfiguredAsync(expectedStoreId, original, cancellationToken)).ConfigureAwait(false);
        if (actor is null || workspace is null || workspace.Actor != actor) return null;
        var binding = await original.Observe(() => ownership.GetVerifiedAsync("files", workspace.Configuration.StoreId.ToString("D"), cancellationToken).AsTask()).ConfigureAwait(false);
        return binding?.ProfileId == actor.ProfileId && binding.ResourceKind == "files" &&
            binding.StoreId == workspace.Configuration.StoreId.ToString("D") &&
            await original.Observe(() => profiles.GetCurrentAsync(cancellationToken).AsTask()).ConfigureAwait(false) == actor ? workspace : null;
    }
    internal async ValueTask<Func<CancellationToken, ValueTask<bool>>> CaptureOriginalReadCheckAsync(
        NativeFilesWorkspace originalWorkspace, Func<bool> originalLifetime, FilesOriginalReadSourceScope original, CancellationToken cancellationToken)
    {
        bool Alive() { try { return original.Invoke(originalLifetime); } catch { return false; } }
        if (!workspaces.IsOriginalReadComposition(profiles, ownership))
            throw new UnauthorizedAccessException("Original Files Home composition is unavailable.");
        if (!Alive() || await original.Observe(() => profiles.GetCurrentAsync(cancellationToken).AsTask()).ConfigureAwait(false) != originalWorkspace.Actor
            || ownership is not IResourceStoreOwnershipReceiptAuthority receipts)
            throw new UnauthorizedAccessException("Original Files read unavailable.");
        var configuration = await original.Observe(() => workspaces.CaptureOriginalReadConfigurationCheckAsync(originalWorkspace, original, cancellationToken).AsTask()).ConfigureAwait(false);
        if (!Alive() || !await original.Observe(() => configuration(cancellationToken).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Original Files configuration changed.");
        var captured = await original.Observe(() => receipts.GetVerifiedAsync("files", originalWorkspace.Configuration.StoreId.ToString("D"), cancellationToken).AsTask()).ConfigureAwait(false);
        if (captured?.Receipt is null || captured.ProfileId != originalWorkspace.Actor.ProfileId || captured.ResourceKind != "files"
            || captured.StoreId != originalWorkspace.Configuration.StoreId.ToString("D")
            || !Alive() || !await original.Observe(() => configuration(cancellationToken).AsTask()).ConfigureAwait(false)
            || !await original.Observe(() => receipts.IsCurrentAsync(captured, originalWorkspace.Actor, cancellationToken).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Original Files ownership changed.");
        return async token => Alive() && await original.Observe(() => profiles.GetCurrentAsync(token).AsTask()).ConfigureAwait(false) == originalWorkspace.Actor
            && await original.Observe(() => configuration(token).AsTask()).ConfigureAwait(false)
            && await original.Observe(() => receipts.IsCurrentAsync(captured, originalWorkspace.Actor, token).AsTask()).ConfigureAwait(false)
            && await original.Observe(() => configuration(token).AsTask()).ConfigureAwait(false) && Alive()
            && await original.Observe(() => profiles.GetCurrentAsync(token).AsTask()).ConfigureAwait(false) == originalWorkspace.Actor;
    }

}
