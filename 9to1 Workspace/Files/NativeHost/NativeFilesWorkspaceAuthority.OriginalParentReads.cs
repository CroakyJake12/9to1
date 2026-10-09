using Haven.Application;

namespace HavenOS.Files.NativeHost;

public sealed partial class NativeFilesWorkspaceAuthority
{
    /// <summary>Optional original execution observation only. The exact same Home issuer
    /// must support nested actor/evidence/receipt scope custody; no legacy fallback.</summary>
    internal async Task<NativeFilesWorkspace?> GetOriginalCurrentWithinSourceAsync(Guid? expectedStoreId,
        FilesOriginalReadSourceScope original, CancellationToken cancellationToken)
    {
        if (ownership is not IResourceStoreOriginalScopedOwnershipAuthority sameOwnership)
            throw new NotSupportedException("The SAME Home ownership issuer lacks original nested callback custody.");
        var actor = await original.Observe(() => profiles.GetCurrentAsync(original.OriginalSynchronousScope,
            original.RetainOriginalTask, cancellationToken).AsTask()).ConfigureAwait(false);
        var workspace = await original.Observe(() => workspaces.GetOriginalConfiguredWithinSourceAsync(expectedStoreId,
            original, cancellationToken)).ConfigureAwait(false);
        if (actor is null || workspace is null || workspace.Actor != actor) return null;
        var binding = await original.Observe(() => sameOwnership.GetVerifiedWithinOriginalSourceAsync("files",
            workspace.Configuration.StoreId.ToString("D"), original.OriginalSynchronousScope,
            original.RetainOriginalTask, cancellationToken).AsTask()).ConfigureAwait(false);
        return binding?.ProfileId == actor.ProfileId && binding.ResourceKind == "files" &&
            binding.StoreId == workspace.Configuration.StoreId.ToString("D") &&
            await original.Observe(() => profiles.GetCurrentAsync(original.OriginalSynchronousScope,
                original.RetainOriginalTask, cancellationToken).AsTask()).ConfigureAwait(false) == actor ? workspace : null;
    }
}
