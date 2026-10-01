using Haven.Application;
using Haven.Application.Compatibility;

namespace HavenOS.Files.NativeHost;

/// <summary>Routes a retained canonical Files selection into package inspection; never installs or executes it.</summary>
public sealed class FilesCompatibilityPackageOpenCoordinator(ICompatibilityPackageContentSource files,
    IAuthenticatedResourceActorSource actors, ICompatibilityPackageOpenHandler host)
{
    private const long MaximumBytes = 2L * 1024 * 1024 * 1024;

    public async Task OpenAsync(CompatibilityPackageSource selection, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.FileId == Guid.Empty || selection.Length is < 1 or > MaximumBytes ||
            selection.Name is not { Length: > 0 } || selection.MetadataRevision is not { Length: > 0 } ||
            selection.Sha256 is not { Length: 64 } ||
            await actors.GetCurrentAsync(token).ConfigureAwait(false) != selection.ObservedActor)
            throw new UnauthorizedAccessException("Select a current package from the verified Files profile.");
        var extension = Path.GetExtension(selection.Name);
        if (!new[] { ".exe", ".msi", ".apk" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException("This file does not identify a supported package type.");
        await using var lease = await files.ReadAsync(selection.FileId, selection.ContentRevision,
            MaximumBytes, token).ConfigureAwait(false);
        if (lease.Source != selection)
            throw new InvalidOperationException("The selected package changed. Select its current Files revision again.");
        await lease.RevalidateAsync(token).ConfigureAwait(false);
        if (await actors.GetCurrentAsync(token).ConfigureAwait(false) != selection.ObservedActor)
            throw new UnauthorizedAccessException("The selected package profile changed.");
        // The OS host independently rereads this canonical source before it displays package metadata.
        await host.OpenAsync(selection, token).ConfigureAwait(false);
        await lease.RevalidateAsync(token).ConfigureAwait(false);
    }
}
