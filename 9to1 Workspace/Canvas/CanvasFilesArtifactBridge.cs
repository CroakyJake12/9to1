using System.Security.Cryptography;
using Haven.Application;
using HavenOS.Files;

namespace HavenOS.Apps.Canvas;

/// <param name="Revision">Immutable content revision, retained across metadata-only changes.</param>
/// <param name="CasRevisionId">Current Files item revision for ACL checks and the next write CAS.</param>
public sealed record CanvasFilesOpenResult(CanvasArtifact Artifact, FilesRevision Revision, FilesRevisionId CasRevisionId);

/// <summary>
/// Canvas-owned codec over an explicitly bound canonical Files folder. Candidates
/// are immutable and published through Files revision CAS. Host authentication
/// and Files resource authorization are mandatory; no private directory fallback.
/// </summary>
public sealed class CanvasFilesArtifactBridge(
    IAuthenticatedResourceActorSource actors,
    Func<AuthenticatedResourceActor, DurableDriveProvider?> providers,
    FilesWorkspaceDirectoryResolver directories,
    ResourceAuthorizationService authorization,
    Func<bool> hostAllowsWrites)
{
    private const string OwnerAppId = "canvas";
    private const long MaximumArtifactBytes = 512L * 1024 * 1024;

    public async Task<(HostedItemId FileId, FilesRevision Revision)> CreateAsync(CanvasArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        CanvasArtifactCodec.Serialize(artifact);
        if (!hostAllowsWrites()) throw new UnauthorizedAccessException("The current Canvas host is read-only.");
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) ?? throw new UnauthorizedAccessException("No verified Home actor is active.");
        var provider = providers(actor) ?? throw new UnauthorizedAccessException("No authorised canonical Files provider is available.");
        var binding = await BindingAsync(actor, cancellationToken).ConfigureAwait(false);
        var folder = await provider.GetAsync(binding.FolderId, cancellationToken).ConfigureAwait(false);
        if (!folder.IsSuccess) throw new InvalidOperationException(folder.Error!.Message);
        var scope = new ResourceScope("files.item", binding.FolderId.ToString(), folder.Value!.CurrentRevisionId?.ToString() ?? "uncommitted", ResourceAccess.Write);
        await RecheckAsync(actor, scope, "canvas.file.create", cancellationToken).ConfigureAwait(false);
        if (!hostAllowsWrites()) throw new UnauthorizedAccessException("Canvas became read-only before creation.");
        var fileId = HostedItemId.New();
        var reference = new FilesArtifactReference(OwnerAppId, artifact.ArtifactId.ToString("N"), fileId,
            binding.FolderId, nameof(FilesArtifactType.Canvas), artifact.DisplayName + CanvasArtifactFile.FileExtension);
        var registered = await provider.RegisterArtifactAsync(reference, folder.Value.OwnerPrincipalId, cancellationToken).ConfigureAwait(false);
        if (!registered.IsSuccess) throw new InvalidOperationException(registered.Error!.Message);
        // A failed initial write remains an explicit uncommitted Files artifact;
        // it is never presented as a successful durable create.
        var revision = await SaveAsync(fileId, artifact, null, cancellationToken).ConfigureAwait(false);
        return (fileId, revision);
    }

    public async Task<CanvasFilesOpenResult> OpenAsync(HostedItemId fileId, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(fileId, ResourceAccess.Read, cancellationToken).ConfigureAwait(false);
        var binding = await BindingAsync(resolved.Actor, cancellationToken).ConfigureAwait(false);
        RequireFolder(binding, resolved.Reference);
        var content = await resolved.Provider.GetCurrentArtifactContentAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (!content.IsSuccess || string.IsNullOrWhiteSpace(content.Value!.ProviderContentReference))
            throw new InvalidDataException("Canvas has no committed canonical Files content revision.");
        var revision = content.Value.Revision;
        if (revision.SizeBytes is null or <= 0 or > MaximumArtifactBytes)
            throw new InvalidDataException("The Canvas artifact is outside supported payload limits.");
        var path = SafePath(binding.DirectoryPath, content.Value.ProviderContentReference);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        if (stream.Length != revision.SizeBytes) throw new InvalidDataException("Canvas bytes differ from the canonical Files size.");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), revision.ContentHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Canvas bytes differ from the canonical Files content hash.");
        var artifact = CanvasArtifactCodec.Deserialize(bytes);
        if (!Guid.TryParse(resolved.Reference.ArtifactId, out var registeredId) || artifact.ArtifactId != registeredId)
            throw new InvalidDataException("Canvas artifact identity differs from its canonical Files identity.");
        await RecheckAsync(resolved.Actor, resolved.Scope, "canvas.file.open", cancellationToken).ConfigureAwait(false);
        return new(artifact, revision, resolved.Metadata.CurrentRevisionId
            ?? throw new InvalidDataException("Canvas current Files item has no structural revision."));
    }

    public async Task<FilesRevision> SaveAsync(HostedItemId fileId, CanvasArtifact artifact, FilesRevisionId? expectedFileRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (!hostAllowsWrites()) throw new UnauthorizedAccessException("The current Canvas host is read-only.");
        var resolved = await ResolveAsync(fileId, ResourceAccess.Write, cancellationToken).ConfigureAwait(false);
        if (!Guid.TryParse(resolved.Reference.ArtifactId, out var registeredId) || artifact.ArtifactId != registeredId)
            throw new InvalidDataException("Canvas cannot replace a canonical artifact with another identity.");
        var binding = await BindingAsync(resolved.Actor, cancellationToken).ConfigureAwait(false);
        RequireFolder(binding, resolved.Reference);
        var bytes = CanvasArtifactCodec.Serialize(artifact);
        if (bytes.LongLength > MaximumArtifactBytes) throw new InvalidDataException("The Canvas artifact exceeds supported payload limits.");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var current = await resolved.Provider.GetCurrentArtifactContentAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (current.IsSuccess && current.Value!.Revision.OwningAppRevisionId == artifact.RevisionId.ToString("N"))
        {
            var prior = current.Value.Revision;
            if (prior.ContentHash != hash || prior.SizeBytes != bytes.LongLength)
                throw new InvalidOperationException("Canvas revision identity was reused with different content.");
            if (expectedFileRevision != prior.Id && expectedFileRevision != prior.ParentRevisionId && expectedFileRevision != resolved.Metadata.CurrentRevisionId)
                throw new InvalidOperationException("Canvas Files revision conflict: reload before saving.");
            await RecheckWriteAsync(resolved.Actor, resolved.Scope, cancellationToken).ConfigureAwait(false);
            return prior;
        }
        if (resolved.Metadata.CurrentRevisionId != expectedFileRevision)
            throw new InvalidOperationException("Canvas Files revision conflict: reload before saving.");
        var candidate = Guid.NewGuid().ToString("N");
        var relative = Path.Combine(".9to1-artifacts", fileId.ToString(), candidate + ".9to1c");
        var destination = SafePath(binding.DirectoryPath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        destination = SafePath(binding.DirectoryPath, relative);
        await RecheckWriteAsync(resolved.Actor, resolved.Scope, cancellationToken).ConfigureAwait(false);
        await using (var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
        await RecheckWriteAsync(resolved.Actor, resolved.Scope, cancellationToken).ConfigureAwait(false);
        var committed = await resolved.Provider.CommitDurableRevisionAsync(new(fileId, OwnerAppId, artifact.RevisionId.ToString("N"),
            resolved.Metadata.OwnerPrincipalId, DateTimeOffset.UtcNow, bytes.LongLength, hash,
            relative, expectedFileRevision), cancellationToken).ConfigureAwait(false);
        if (!committed.IsSuccess)
            throw new InvalidOperationException(committed.Error!.Message + " The candidate remains recoverable and the prior canonical revision is unchanged.");
        return committed.Value!;
    }

    private async Task<(AuthenticatedResourceActor Actor, DurableDriveProvider Provider, FilesArtifactReference Reference, HostedItemMetadata Metadata, ResourceScope Scope)>
        ResolveAsync(HostedItemId fileId, ResourceAccess access, CancellationToken cancellationToken)
    {
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) ?? throw new UnauthorizedAccessException("No verified Home actor is active.");
        var provider = providers(actor) ?? throw new UnauthorizedAccessException("No canonical Files provider is bound to the Home actor.");
        var metadata = await provider.GetAsync(fileId, cancellationToken).ConfigureAwait(false);
        var reference = await provider.GetArtifactAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (!metadata.IsSuccess || !reference.IsSuccess || reference.Value!.OwnerAppId != OwnerAppId || reference.Value.ArtifactType != nameof(FilesArtifactType.Canvas))
            throw new InvalidDataException("The canonical Canvas artifact is unavailable or belongs to another app.");
        var scope = new ResourceScope("files.item", fileId.ToString(), metadata.Value!.CurrentRevisionId?.ToString() ?? "uncommitted", access);
        await RecheckAsync(actor, scope, access == ResourceAccess.Read ? "canvas.file.open" : "canvas.file.save", cancellationToken).ConfigureAwait(false);
        return (actor, provider, reference.Value, metadata.Value, scope);
    }

    private async Task<FilesWorkspaceDirectoryBinding> BindingAsync(AuthenticatedResourceActor actor, CancellationToken cancellationToken)
    {
        var result = actor.AccountId is { } account
            ? await directories.ResolveAsync(account, OwnerAppId, cancellationToken).ConfigureAwait(false)
            : Guid.TryParse(actor.ProfileId, out var profile)
                ? await directories.ResolveProfileAsync(profile, OwnerAppId, cancellationToken).ConfigureAwait(false)
                : throw new UnauthorizedAccessException("The Home profile identity is invalid.");
        if (!result.IsSuccess) throw new UnauthorizedAccessException(result.Error!.Message);
        return result.Value!;
    }
    private static void RequireFolder(FilesWorkspaceDirectoryBinding binding, FilesArtifactReference reference)
    {
        if (binding.FolderId != reference.ParentFolderId) throw new UnauthorizedAccessException("Choose the canonical Files folder containing this Canvas artifact.");
    }
    private async Task RecheckAsync(AuthenticatedResourceActor actor, ResourceScope scope, string action, CancellationToken cancellationToken)
    {
        if (await authorization.AuthorizeAsync(action, [scope], cancellationToken).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Home identity, Files authority or resource revision changed.");
    }
    private Task RecheckWriteAsync(AuthenticatedResourceActor actor, ResourceScope scope, CancellationToken cancellationToken)
    {
        if (!hostAllowsWrites()) throw new UnauthorizedAccessException("Canvas is read-only; the candidate cannot be published.");
        return RecheckAsync(actor, scope, "canvas.file.save", cancellationToken);
    }
    private static string SafePath(string root, string relative)
    {
        if (Path.IsPathFullyQualified(relative) || relative.Split(['/', '\\']).Any(part => part is "." or ".."))
            throw new InvalidDataException("Canvas content reference is not a safe Files-relative path.");
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(fullRoot) || (File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("The canonical Canvas Files directory is unavailable or redirects outside its authority.");
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!full.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Canvas content reference escapes its canonical Files folder.");
        for (var path = full; ; path = Path.GetDirectoryName(path)!)
        {
            if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Canvas content redirects outside the canonical Files authority.");
            if (path == fullRoot) break;
        }
        return full;
    }
}
