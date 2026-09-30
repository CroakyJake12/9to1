using System.Security.Cryptography;
using Haven.Application;
using Haven.Core;
using HavenOS.Files;
using NineToOne.Cui.AI;

namespace Haven.Desktop.Services;

/// <summary>
/// Owning Write package operations over an explicitly bound canonical Files folder. Each save
/// writes a new immutable candidate then publishes its reference with Files CAS; a conflict leaves
/// the candidate recoverable and cannot overwrite the winning canonical content. This is not a
/// distributed transaction or a remote provider implementation.
/// </summary>
internal sealed class WriteFilesArtifactBridge(IAuthenticatedResourceActorSource actors,
    Func<AuthenticatedResourceActor, DurableDriveProvider?> providers, FilesWorkspaceDirectoryResolver directories,
    IWriteNativeDocumentPackageStore packages, ResourceAuthorizationService authorization,
    Func<AppAiAccessMode> currentHostAccessMode)
{
    public async Task<NotesDocument> OpenAsync(HostedItemId fileId, CancellationToken cancellationToken = default)
    {
        var (actor, provider, reference, metadata, scope) = await ResolveAsync(fileId, ResourceAccess.Read, null, cancellationToken).ConfigureAwait(false);
        var root = await RootAsync(actor, reference, cancellationToken).ConfigureAwait(false);
        var content = await provider.GetCurrentArtifactContentAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (!content.IsSuccess || string.IsNullOrWhiteSpace(content.Value!.ProviderContentReference))
            throw new InvalidDataException("The canonical artifact has no available owning-app package.");
        var path = SafePackagePath(root, content.Value.ProviderContentReference);
        await using (var contentStream = File.OpenRead(path))
        {
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(contentStream, cancellationToken).ConfigureAwait(false));
            if (contentStream.Length != content.Value.Revision.SizeBytes ||
                !string.Equals(actualHash, content.Value.Revision.ContentHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The package bytes differ from the canonical Files content revision.");
        }
        var opened = await packages.OpenAsync(path, cancellationToken).ConfigureAwait(false);
        if (!opened.IsSuccess) throw new InvalidDataException(opened.Error!.Message);
        if (opened.Value!.Id.ToString("N") != reference.ArtifactId)
            throw new InvalidDataException("The native package identity differs from the canonical Files artifact.");
        if (await authorization.AuthorizeAsync("write.file.open", [scope], cancellationToken).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("File authority or revision changed while opening the package.");
        return opened.Value;
    }

    public async Task<FilesRevision> SaveAsync(HostedItemId fileId, NotesDocument document, FilesRevisionId? expectedFileRevision,
        CancellationToken cancellationToken = default)
    {
        if (currentHostAccessMode() != AppAiAccessMode.Write) throw new UnauthorizedAccessException("Write mode is required to edit the artifact.");
        var (actor, provider, reference, metadata, scope) = await ResolveAsync(fileId, ResourceAccess.Write, expectedFileRevision, cancellationToken).ConfigureAwait(false);
        if (document.Id.ToString("N") != reference.ArtifactId) throw new InvalidDataException("Write cannot replace the canonical artifact with another document identity.");
        var root = await RootAsync(actor, reference, cancellationToken).ConfigureAwait(false);
        var revisionId = Guid.NewGuid().ToString("N");
        var relative = Path.Combine(".9to1-artifacts", fileId.ToString(), revisionId + ".9to1w");
        var destination = SafePackagePath(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        destination = SafePackagePath(root, relative);
        // Recheck after directory work and immediately before the owning app writes content.
        if (currentHostAccessMode() != AppAiAccessMode.Write ||
            await authorization.AuthorizeAsync("write.file.save", [scope], cancellationToken).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Write mode or current Files authority no longer permits this save.");
        var saved = await packages.SaveAsync(document, destination, cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess) throw new IOException(saved.Error!.Message);
        await using var stream = File.OpenRead(destination);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        if (currentHostAccessMode() != AppAiAccessMode.Write ||
            await authorization.AuthorizeAsync("write.file.save", [scope], cancellationToken).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Authority changed; the saved candidate remains recoverable but was not published.");
        var committed = await provider.CommitDurableRevisionAsync(new(fileId, "write", revisionId,
            metadata.OwnerPrincipalId, DateTimeOffset.UtcNow, stream.Length, hash, relative, expectedFileRevision), cancellationToken).ConfigureAwait(false);
        if (!committed.IsSuccess) throw new InvalidOperationException(committed.Error!.Message + " The candidate package remains recoverable.");
        return committed.Value!;
    }

    private async Task<(AuthenticatedResourceActor Actor, DurableDriveProvider Provider, FilesArtifactReference Reference, HostedItemMetadata Metadata, ResourceScope Scope)>
        ResolveAsync(HostedItemId fileId, ResourceAccess access, FilesRevisionId? expected, CancellationToken cancellationToken)
    {
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) ?? throw new UnauthorizedAccessException("No verified Home actor is active.");
        var provider = providers(actor) ?? throw new UnauthorizedAccessException("No authorised Files provider is bound to this actor.");
        var metadata = await provider.GetAsync(fileId, cancellationToken).ConfigureAwait(false);
        var reference = await provider.GetArtifactAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (!metadata.IsSuccess || !reference.IsSuccess) throw new InvalidOperationException("The canonical Write artifact is unavailable.");
        if (reference.Value!.OwnerAppId != "write" || reference.Value.ArtifactType != nameof(FilesArtifactType.WriteDocument) ||
            metadata.Value!.Kind != HostedItemKind.Artifact)
            throw new UnauthorizedAccessException("This artifact belongs to another application or document type.");
        if (access == ResourceAccess.Write && metadata.Value!.CurrentRevisionId != expected) throw new InvalidOperationException("The canonical Files revision changed; refresh before editing.");
        var scope = new ResourceScope("files.item", fileId.ToString(), metadata.Value!.CurrentRevisionId?.ToString() ?? "uncommitted", access);
        var action = access == ResourceAccess.Read ? "write.file.open" : "write.file.save";
        if (await authorization.AuthorizeAsync(action, [scope], cancellationToken).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Current Files ownership, mode or resource revision does not permit this operation.");
        return (actor, provider, reference.Value!, metadata.Value, scope);
    }

    private async Task<string> RootAsync(AuthenticatedResourceActor actor, FilesArtifactReference reference, CancellationToken cancellationToken)
    {
        var binding = actor.AccountId is { } account
            ? await directories.ResolveAsync(account, "write", cancellationToken).ConfigureAwait(false)
            : Guid.TryParse(actor.ProfileId, out var profile)
                ? await directories.ResolveProfileAsync(profile, "write", cancellationToken).ConfigureAwait(false)
                : throw new UnauthorizedAccessException("The local Home profile identity is invalid.");
        if (!binding.IsSuccess || binding.Value!.FolderId != reference.ParentFolderId)
            throw new UnauthorizedAccessException("Choose the current canonical Files folder; there is no private app-directory fallback.");
        return binding.Value.DirectoryPath;
    }

    private static string SafePackagePath(string root, string relative)
    {
        if (Path.IsPathFullyQualified(relative) || relative.Split(['/', '\\']).Any(part => part is "." or ".."))
            throw new InvalidDataException("The canonical package reference is not a safe Files-relative path.");
        var fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot) || (File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("The canonical Files folder is unavailable or redirects elsewhere.");
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!full.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The package reference escapes its canonical Files folder.");
        for (var path = full; path != fullRoot; path = Path.GetDirectoryName(path)!)
            if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("The package materialisation redirects outside its canonical Files authority.");
        return full;
    }
}
