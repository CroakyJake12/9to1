using System.Security.Cryptography;
using Haven.Application;
using HavenOS.Files;

namespace HavenOS.Images;

/// <param name="Revision">Immutable content revision, retained across metadata-only changes.</param>
/// <param name="CasRevisionId">Current Files item revision for ACL checks and the next write CAS.</param>
public sealed record PictureFilesOpenResult(PictureArtifactEnvelope Artifact, FilesRevision Revision, FilesRevisionId CasRevisionId);

/// <summary>
/// Picture-owned codec over an explicitly bound canonical Files folder. Candidates
/// are immutable and published through Files revision CAS. Host authentication
/// and Files resource authorization are mandatory; no private directory fallback.
/// </summary>
public sealed class PictureFilesArtifactBridge(
    IAuthenticatedResourceActorSource actors,
    Func<AuthenticatedResourceActor, DurableDriveProvider?> providers,
    FilesWorkspaceDirectoryResolver directories,
    ResourceAuthorizationService authorization,
    Func<bool> hostAllowsWrites)
{
    private const string OwnerAppId = "picture";
    private const long MaximumArtifactBytes = PictureArtifactCodec.MaximumPayloadBytes;

    public async Task<PictureFilesOpenResult> CreateAsync(PictureDocument document, PictureSourceAssetReference? sourceAsset = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var fileId = HostedItemId.New();
        var artifact = PictureArtifactCodec.Deserialize(PictureArtifactCodec.Serialize(new PictureArtifactEnvelope
        {
            BackingFileId = fileId.Value, Document = document, SourceAsset = sourceAsset
        }));
        if (!hostAllowsWrites()) throw new UnauthorizedAccessException("The current Picture host is read-only.");
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) ?? throw new UnauthorizedAccessException("No verified Home actor is active.");
        var provider = providers(actor) ?? throw new UnauthorizedAccessException("No authorised canonical Files provider is available.");
        await ValidateSourceReferenceAsync(actor, provider, artifact.SourceAsset, cancellationToken).ConfigureAwait(false);
        var binding = await BindingAsync(actor, cancellationToken).ConfigureAwait(false);
        var folder = await provider.GetAsync(binding.FolderId, cancellationToken).ConfigureAwait(false);
        if (!folder.IsSuccess) throw new InvalidOperationException(folder.Error!.Message);
        var scope = new ResourceScope("files.item", binding.FolderId.ToString(), folder.Value!.CurrentRevisionId?.ToString() ?? "uncommitted", ResourceAccess.Write);
        await RecheckAsync(actor, scope, "picture.file.create", cancellationToken).ConfigureAwait(false);
        if (!hostAllowsWrites()) throw new UnauthorizedAccessException("Picture became read-only before creation.");
        var reference = new FilesArtifactReference(OwnerAppId, artifact.Document.DocumentId.ToString("N"), fileId,
            binding.FolderId, nameof(FilesArtifactType.Picture), artifact.Document.DisplayName + ".picture.json");
        var registered = await provider.RegisterArtifactAsync(reference, folder.Value.OwnerPrincipalId, cancellationToken).ConfigureAwait(false);
        if (!registered.IsSuccess) throw new InvalidOperationException(registered.Error!.Message);
        var revision = await SaveAsync(artifact, null, cancellationToken).ConfigureAwait(false);
        return new(artifact, revision, revision.Id);
    }

    public async Task<PictureFilesOpenResult> OpenAsync(HostedItemId fileId, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(fileId, ResourceAccess.Read, cancellationToken).ConfigureAwait(false);
        var binding = await BindingAsync(resolved.Actor, cancellationToken).ConfigureAwait(false);
        // Logical Files moves change the parent, never the registered payload anchor.
        var content = await resolved.Provider.GetCurrentArtifactContentAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (!content.IsSuccess || string.IsNullOrWhiteSpace(content.Value!.ProviderContentReference))
            throw new InvalidDataException("Picture has no committed canonical Files content revision.");
        var revision = content.Value.Revision;
        if (revision.SizeBytes is null or <= 0 or > MaximumArtifactBytes)
            throw new InvalidDataException("The Picture artifact is outside supported payload limits.");
        var path = SafePath(binding.DirectoryPath, content.Value.ProviderContentReference);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        if (stream.Length != revision.SizeBytes) throw new InvalidDataException("Picture bytes differ from the canonical Files size.");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), revision.ContentHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Picture bytes differ from the canonical Files content hash.");
        var artifact = PictureArtifactCodec.Deserialize(bytes);
        if (!Guid.TryParse(resolved.Reference.ArtifactId, out var registeredId) || artifact.Document.DocumentId != registeredId || artifact.BackingFileId != fileId.Value)
            throw new InvalidDataException("Picture artifact identity differs from its canonical Files identity.");
        if (revision.OwningAppId != OwnerAppId || revision.OwningAppRevisionId != OwningRevision(artifact))
            throw new InvalidDataException("Picture document revision differs from its owning canonical Files revision.");
        await RecheckAsync(resolved.Actor, resolved.Scope, "picture.file.open", cancellationToken).ConfigureAwait(false);
        return new(artifact, revision, resolved.Metadata.CurrentRevisionId
            ?? throw new InvalidDataException("Picture current Files item has no structural revision."));
    }

    public async Task<FilesRevision> SaveAsync(PictureArtifactEnvelope artifact, FilesRevisionId? expectedFileRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        artifact = PictureArtifactCodec.Deserialize(PictureArtifactCodec.Serialize(artifact));
        var fileId = new HostedItemId(artifact.BackingFileId);
        if (!hostAllowsWrites()) throw new UnauthorizedAccessException("The current Picture host is read-only.");
        var resolved = await ResolveAsync(fileId, ResourceAccess.Write, cancellationToken).ConfigureAwait(false);
        if (!Guid.TryParse(resolved.Reference.ArtifactId, out var registeredId) || artifact.Document.DocumentId != registeredId || artifact.BackingFileId != fileId.Value)
            throw new InvalidDataException("Picture cannot replace a canonical artifact with another identity.");
        await ValidateSourceReferenceAsync(resolved.Actor, resolved.Provider, artifact.SourceAsset, cancellationToken).ConfigureAwait(false);
        var binding = await BindingAsync(resolved.Actor, cancellationToken).ConfigureAwait(false);
        // Logical Files moves change the parent, never the registered payload anchor.
        var bytes = PictureArtifactCodec.Serialize(artifact);
        if (bytes.LongLength > MaximumArtifactBytes) throw new InvalidDataException("The Picture artifact exceeds supported payload limits.");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var current = await resolved.Provider.GetCurrentArtifactContentAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (current.IsSuccess && current.Value!.Revision.OwningAppRevisionId == OwningRevision(artifact))
        {
            var prior = current.Value.Revision;
            if (prior.ContentHash != hash || prior.SizeBytes != bytes.LongLength)
                throw new InvalidOperationException("Picture revision identity was reused with different content.");
            if (expectedFileRevision != prior.Id && expectedFileRevision != prior.ParentRevisionId && expectedFileRevision != resolved.Metadata.CurrentRevisionId)
                throw new InvalidOperationException("Picture Files revision conflict: reload before saving.");
            await RecheckWriteAsync(resolved.Actor, resolved.Scope, cancellationToken).ConfigureAwait(false);
            return prior;
        }
        if (resolved.Metadata.CurrentRevisionId != expectedFileRevision)
            throw new InvalidOperationException("Picture Files revision conflict: reload before saving.");
        var candidate = Guid.NewGuid().ToString("N");
        var relative = Path.Combine(".9to1-artifacts", fileId.ToString(), candidate + ".picture.json");
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
        await ValidateSourceReferenceAsync(resolved.Actor, resolved.Provider, artifact.SourceAsset, cancellationToken).ConfigureAwait(false);
        var committed = await resolved.Provider.CommitDurableRevisionAsync(new(fileId, OwnerAppId, OwningRevision(artifact),
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
        if (!metadata.IsSuccess || !reference.IsSuccess || reference.Value!.OwnerAppId != OwnerAppId || reference.Value.ArtifactType != nameof(FilesArtifactType.Picture))
            throw new InvalidDataException("The canonical Picture artifact is unavailable or belongs to another app.");
        var scope = new ResourceScope("files.item", fileId.ToString(), metadata.Value!.CurrentRevisionId?.ToString() ?? "uncommitted", access);
        await RecheckAsync(actor, scope, access == ResourceAccess.Read ? "picture.file.open" : "picture.file.save", cancellationToken).ConfigureAwait(false);
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
    private async Task RecheckAsync(AuthenticatedResourceActor actor, ResourceScope scope, string action, CancellationToken cancellationToken)
    {
        if (await authorization.AuthorizeAsync(action, [scope], cancellationToken).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Home identity, Files authority or resource revision changed.");
    }
    private Task RecheckWriteAsync(AuthenticatedResourceActor actor, ResourceScope scope, CancellationToken cancellationToken)
    {
        if (!hostAllowsWrites()) throw new UnauthorizedAccessException("Picture is read-only; the candidate cannot be published.");
        return RecheckAsync(actor, scope, "picture.file.save", cancellationToken);
    }
    private static string OwningRevision(PictureArtifactEnvelope artifact)
        => artifact.Document.DocumentId.ToString("N") + ":" + artifact.Document.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private async Task ValidateSourceReferenceAsync(AuthenticatedResourceActor actor, DurableDriveProvider provider,
        PictureSourceAssetReference? source, CancellationToken cancellationToken)
    {
        if (source is null) return;
        var fileId = new HostedItemId(source.FileId);
        var metadata = await provider.GetAsync(fileId, cancellationToken).ConfigureAwait(false);
        var retained = await provider.GetArtifactRevisionContentAsync(fileId, new(source.RevisionId), cancellationToken).ConfigureAwait(false);
        if (!metadata.IsSuccess || metadata.Value!.Kind != HostedItemKind.File || !retained.IsSuccess ||
            retained.Value!.Revision.SizeBytes != source.SizeBytes || !string.Equals(NormalizeHash(retained.Value.Revision.ContentHash), source.ContentHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Picture linked source does not match a retained canonical raw Files revision.");
        var scope = new ResourceScope("files.item", fileId.ToString(), metadata.Value.CurrentRevisionId?.ToString() ?? "uncommitted", ResourceAccess.Read);
        await RecheckAsync(actor, scope, "media.asset.read", cancellationToken).ConfigureAwait(false);
    }

    private static string? NormalizeHash(string? hash) => hash?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? hash[7..] : hash;

    private static string SafePath(string root, string relative)
    {
        if (Path.IsPathFullyQualified(relative) || relative.Split(['/', '\\']).Any(part => part is "." or ".."))
            throw new InvalidDataException("Picture content reference is not a safe Files-relative path.");
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(fullRoot) || (File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("The canonical Picture Files directory is unavailable or redirects outside its authority.");
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!full.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Picture content reference escapes its canonical Files folder.");
        for (var path = full; ; path = Path.GetDirectoryName(path)!)
        {
            if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Picture content redirects outside the canonical Files authority.");
            if (path == fullRoot) break;
        }
        return full;
    }
}
