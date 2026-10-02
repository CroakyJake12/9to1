using System.Security.Cryptography;
using Haven.Application;
using HavenOS.Files;

namespace HavenOS.Images;

/// <param name="Revision">Immutable content revision, retained across metadata-only changes.</param>
/// <param name="CasRevisionId">Current Files item revision for ACL checks and the next write CAS.</param>
public sealed record PictureFilesOpenResult(PictureArtifactEnvelope Artifact, FilesRevision Revision, FilesRevisionId CasRevisionId)
{
    public Guid StoreId { get; init; }
}

/// <summary>Detached fixed creation/source guards; no authority or live capability is serialized.</summary>
public sealed record PictureCreateCapture(HostedItemId FileId, FilesWorkspaceDirectoryBinding Binding,
    FilesRevisionId? FolderRevision, FilesItemRevisionPrecondition? SourceAsset,
    FilesItemRevisionPrecondition? SourceArtifact);

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
    Func<bool> hostAllowsWrites,
    Func<AuthenticatedResourceActor, DurableDriveProvider, CancellationToken, ValueTask<FilesCommitAuthorityGuard>>? captureCommitAuthority = null)
{
    private const string OwnerAppId = "picture";
    private const long MaximumArtifactBytes = PictureArtifactCodec.MaximumPayloadBytes;

    public Task<PictureFilesOpenResult> CreateAsync(PictureDocument document, PictureSourceAssetReference? sourceAsset = null,
        CancellationToken cancellationToken = default)
        => CreateCoreAsync(document, sourceAsset, null, null, null, cancellationToken);

    /// <summary>Original caller-captured destination identity and actor are data, not authority.</summary>
    public Task<PictureFilesOpenResult> CreateAsync(PictureDocument document, PictureSourceAssetReference? sourceAsset,
        Guid expectedStoreId, AuthenticatedResourceActor claimedActor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claimedActor);
        if (expectedStoreId == Guid.Empty) throw new ArgumentException("The original Files store identity is required.", nameof(expectedStoreId));
        return CreateCoreAsync(document, sourceAsset, expectedStoreId, claimedActor, null, cancellationToken);
    }

    public Task<PictureFilesOpenResult> CreateAsync(PictureDocument document, PictureSourceAssetReference? sourceAsset,
        Guid expectedStoreId, AuthenticatedResourceActor claimedActor, PictureCreateCapture capture,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claimedActor); ArgumentNullException.ThrowIfNull(capture);
        if (expectedStoreId == Guid.Empty || capture.FileId.Value == Guid.Empty || capture.FolderRevision is null ||
            capture.Binding.OwningAppId != OwnerAppId || capture.SourceArtifact?.ItemId == capture.FileId ||
            (sourceAsset is not null && (capture.SourceAsset is null || capture.SourceAsset.ItemId.Value != sourceAsset.FileId)))
            throw new ArgumentException("The exact original Picture creation/source identities are required.", nameof(capture));
        return CreateCoreAsync(document, sourceAsset, expectedStoreId, claimedActor, capture, cancellationToken);
    }

    private async Task<PictureFilesOpenResult> CreateCoreAsync(PictureDocument document, PictureSourceAssetReference? sourceAsset,
        Guid? expectedStoreId, AuthenticatedResourceActor? claimedActor, PictureCreateCapture? capture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var fileId = capture?.FileId ?? HostedItemId.New();
        var artifact = PictureArtifactCodec.Deserialize(PictureArtifactCodec.Serialize(new PictureArtifactEnvelope
        {
            BackingFileId = fileId.Value, Document = document, SourceAsset = sourceAsset
        }));
        if (!hostAllowsWrites()) throw new UnauthorizedAccessException("The current Picture host is read-only.");
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) ?? throw new UnauthorizedAccessException("No verified Home actor is active.");
        if (claimedActor is not null && claimedActor != actor)
            throw new UnauthorizedAccessException("The Picture creation actor differs from its captured original actor.");
        var provider = providers(actor) ?? throw new UnauthorizedAccessException("No authorised canonical Files provider is available.");
        if (expectedStoreId is { } originalStore)
            await provider.GetStoreEvidenceAsync(originalStore, cancellationToken).ConfigureAwait(false);
        var initialSourceGuard = await ValidateSourceReferenceAsync(actor, provider, artifact.SourceAsset, cancellationToken).ConfigureAwait(false);
        if (capture is not null && initialSourceGuard != capture.SourceAsset)
            throw new InvalidOperationException("The captured retained-source metadata changed before creation.");
        var binding = await BindingAsync(actor, cancellationToken).ConfigureAwait(false);
        if (capture is not null && binding != capture.Binding)
            throw new UnauthorizedAccessException("The original Picture destination changed before creation.");
        var folder = await provider.GetAsync(binding.FolderId, cancellationToken).ConfigureAwait(false);
        if (!folder.IsSuccess) throw new InvalidOperationException(folder.Error!.Message);
        if (capture is not null && folder.Value!.CurrentRevisionId != capture.FolderRevision)
            throw new InvalidOperationException("The captured Picture destination revision changed before creation.");
        await RecheckCreationSourceAsync(actor, provider, capture?.SourceArtifact, cancellationToken).ConfigureAwait(false);
        var scope = new ResourceScope("files.item", binding.FolderId.ToString(), folder.Value!.CurrentRevisionId?.ToString() ?? "uncommitted", ResourceAccess.Write);
        await RecheckAsync(actor, scope, "picture.file.create", cancellationToken).ConfigureAwait(false);
        if (!hostAllowsWrites()) throw new UnauthorizedAccessException("Picture became read-only before creation.");
        var reference = new FilesArtifactReference(OwnerAppId, artifact.Document.DocumentId.ToString("N"), fileId,
            binding.FolderId, nameof(FilesArtifactType.Picture), artifact.Document.DisplayName + ".picture.json");
        var guards = new List<FilesItemRevisionPrecondition> { new(binding.FolderId, folder.Value.CurrentRevisionId) };
        if (capture?.SourceArtifact is { } sourceArtifactGuard) guards.Add(sourceArtifactGuard);
        if (artifact.SourceAsset is { } rawSource)
        {
            var raw = await provider.GetAsync(new(rawSource.FileId), cancellationToken).ConfigureAwait(false);
            if (!raw.IsSuccess) throw new UnauthorizedAccessException("The canonical source is unavailable.");
            var rawGuard = new FilesItemRevisionPrecondition(new(rawSource.FileId), raw.Value!.CurrentRevisionId);
            if (capture is not null && rawGuard != capture.SourceAsset)
                throw new InvalidOperationException("The captured retained source changed before creation.");
            guards.Add(rawGuard);
        }
        var bytes = PictureArtifactCodec.Serialize(artifact);
        if (bytes.LongLength > MaximumArtifactBytes) throw new InvalidDataException("The Picture artifact exceeds supported payload limits.");
        var relative = Path.Combine(".9to1-artifacts", fileId.ToString(), Guid.NewGuid().ToString("N") + ".picture.json");
        var destination = SafePath(binding.DirectoryPath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        destination = SafePath(binding.DirectoryPath, relative);
        await using (var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
        await RecheckAsync(actor, scope, "picture.file.create", cancellationToken).ConfigureAwait(false);
        var finalSourceGuard = await ValidateSourceReferenceAsync(actor, provider, artifact.SourceAsset, cancellationToken).ConfigureAwait(false);
        if (capture is not null && finalSourceGuard != capture.SourceAsset)
            throw new InvalidOperationException("The captured retained source changed before publication.");
        await RecheckCreationSourceAsync(actor, provider, capture?.SourceArtifact, cancellationToken).ConfigureAwait(false);
        if (await BindingAsync(actor, cancellationToken).ConfigureAwait(false) != binding || !ReferenceEquals(providers(actor), provider))
            throw new UnauthorizedAccessException("The canonical Picture destination changed before creation.");
        SafePath(binding.DirectoryPath, relative);
        var commitAuthority = captureCommitAuthority is null
            ? new FilesCommitAuthorityGuard(actor.ActorId, async token =>
                await actors.GetCurrentAsync(token).ConfigureAwait(false) == actor && hostAllowsWrites())
            : await captureCommitAuthority(actor, provider, cancellationToken).ConfigureAwait(false);
        var commit = new FilesOwningAppRevisionCommit(fileId, OwnerAppId, OwningRevision(artifact), actor.ActorId, DateTimeOffset.UtcNow,
            bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)), relative, null);
        var committed = expectedStoreId is { } pinnedStore
            ? await provider.CommitCreatedArtifactAsync(reference, commit, guards, pinnedStore, commitAuthority, cancellationToken).ConfigureAwait(false)
            : await provider.CommitCreatedArtifactAsync(reference, commit, guards, commitAuthority, cancellationToken).ConfigureAwait(false);
        if (!committed.IsSuccess)
            throw new InvalidOperationException(committed.Error!.Message + " The unpublished candidate remains recoverable; no artifact was registered.");
        return new(artifact, committed.Value!, committed.Value!.Id) { StoreId = expectedStoreId ?? Guid.Empty };
    }

    public Task<PictureFilesOpenResult> OpenAsync(HostedItemId fileId, CancellationToken cancellationToken = default)
        => OpenCoreAsync(fileId, null, cancellationToken);

    public Task<PictureFilesOpenResult> OpenAsync(HostedItemId fileId, Guid expectedStoreId, CancellationToken cancellationToken = default)
    {
        if (expectedStoreId == Guid.Empty) throw new ArgumentException("The original Files store identity is required.", nameof(expectedStoreId));
        return OpenCoreAsync(fileId, expectedStoreId, cancellationToken);
    }

    private async Task<PictureFilesOpenResult> OpenCoreAsync(HostedItemId fileId, Guid? expectedStoreId, CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(fileId, ResourceAccess.Read, cancellationToken, expectedStoreId).ConfigureAwait(false);
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
        if ((await resolved.Provider.GetStoreEvidenceAsync(resolved.StoreId, cancellationToken).ConfigureAwait(false)).StoreId != resolved.StoreId)
            throw new InvalidOperationException("Files store changed during Picture materialization.");
        return new(artifact, revision, resolved.Metadata.CurrentRevisionId
            ?? throw new InvalidDataException("Picture current Files item has no structural revision.")) { StoreId = resolved.StoreId };
    }

    public Task<FilesRevision> SaveAsync(PictureArtifactEnvelope artifact, FilesRevisionId? expectedFileRevision,
        CancellationToken cancellationToken = default) => SaveCoreAsync(artifact, expectedFileRevision, null, null, cancellationToken);

    /// <summary>Retains the exact actor already claimed by an owning Home operation; this overload grants no authority.</summary>
    public Task<FilesRevision> SaveAsync(PictureArtifactEnvelope artifact, FilesRevisionId? expectedFileRevision,
        AuthenticatedResourceActor claimedActor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claimedActor);
        return SaveCoreAsync(artifact, expectedFileRevision, claimedActor, null, cancellationToken);
    }

    public Task<FilesRevision> SaveAsync(PictureArtifactEnvelope artifact, FilesRevisionId? expectedFileRevision,
        Guid expectedStoreId, AuthenticatedResourceActor claimedActor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claimedActor);
        if (expectedStoreId == Guid.Empty) throw new ArgumentException("An original Files store identity is required.", nameof(expectedStoreId));
        return SaveCoreAsync(artifact, expectedFileRevision, claimedActor, expectedStoreId, cancellationToken);
    }

    private async Task<FilesRevision> SaveCoreAsync(PictureArtifactEnvelope artifact, FilesRevisionId? expectedFileRevision,
        AuthenticatedResourceActor? claimedActor, Guid? expectedStoreId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        artifact = PictureArtifactCodec.Deserialize(PictureArtifactCodec.Serialize(artifact));
        var fileId = new HostedItemId(artifact.BackingFileId);
        if (!hostAllowsWrites()) throw new UnauthorizedAccessException("The current Picture host is read-only.");
        var resolved = await ResolveAsync(fileId, ResourceAccess.Write, cancellationToken, expectedStoreId).ConfigureAwait(false);
        if (expectedStoreId is { } originalStore && resolved.StoreId != originalStore)
            throw new InvalidOperationException("The original Files store has been replaced.");
        if (claimedActor is not null && resolved.Actor != claimedActor)
            throw new UnauthorizedAccessException("The Picture write actor differs from the claimed Home execution actor.");
        if (!Guid.TryParse(resolved.Reference.ArtifactId, out var registeredId) || artifact.Document.DocumentId != registeredId || artifact.BackingFileId != fileId.Value)
            throw new InvalidDataException("Picture cannot replace a canonical artifact with another identity.");
        var sourcePrecondition = await ValidateSourceReferenceAsync(resolved.Actor, resolved.Provider, artifact.SourceAsset, cancellationToken).ConfigureAwait(false);
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
        var finalSourcePrecondition = await ValidateSourceReferenceAsync(resolved.Actor, resolved.Provider, artifact.SourceAsset, cancellationToken).ConfigureAwait(false);
        if (sourcePrecondition != finalSourcePrecondition)
            throw new InvalidOperationException("The linked source changed before Picture publication.");
        if (!ReferenceEquals(providers(resolved.Actor), resolved.Provider))
            throw new UnauthorizedAccessException("The canonical Files provider changed before Picture publication.");
        var commitAuthority = captureCommitAuthority is null
            ? new FilesCommitAuthorityGuard(resolved.Actor.ActorId, async token =>
                await actors.GetCurrentAsync(token).ConfigureAwait(false) == resolved.Actor && hostAllowsWrites())
            : await captureCommitAuthority(resolved.Actor, resolved.Provider, cancellationToken).ConfigureAwait(false);
        var commit = new FilesOwningAppRevisionCommit(fileId, OwnerAppId, OwningRevision(artifact),
            resolved.Metadata.OwnerPrincipalId, DateTimeOffset.UtcNow, bytes.LongLength, hash, relative, expectedFileRevision);
        var committed = expectedStoreId is { } pinnedStore
            ? await resolved.Provider.CommitDurableRevisionAsync(commit, sourcePrecondition is { } sourceGuard ? [sourceGuard] : Array.Empty<FilesItemRevisionPrecondition>(), pinnedStore, commitAuthority, cancellationToken).ConfigureAwait(false)
            : sourcePrecondition is { } rawSourceGuard
            ? await resolved.Provider.CommitDurableRevisionAsync(commit, [rawSourceGuard], commitAuthority, cancellationToken).ConfigureAwait(false)
            : await resolved.Provider.CommitDurableRevisionAsync(commit, commitAuthority, cancellationToken).ConfigureAwait(false);
        if (!committed.IsSuccess)
            throw new InvalidOperationException(committed.Error!.Message + " The candidate remains recoverable and the prior canonical revision is unchanged.");
        return committed.Value!;
    }

    private async Task<(AuthenticatedResourceActor Actor, DurableDriveProvider Provider, FilesArtifactReference Reference, HostedItemMetadata Metadata, ResourceScope Scope, Guid StoreId)>
        ResolveAsync(HostedItemId fileId, ResourceAccess access, CancellationToken cancellationToken, Guid? expectedStoreId = null)
    {
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) ?? throw new UnauthorizedAccessException("No verified Home actor is active.");
        var provider = providers(actor) ?? throw new UnauthorizedAccessException("No canonical Files provider is bound to the Home actor.");
        var storeId = expectedStoreId is { } originalStore
            ? (await provider.GetStoreEvidenceAsync(originalStore, cancellationToken).ConfigureAwait(false)).StoreId
            : (await provider.GetStoreEvidenceAsync(cancellationToken).ConfigureAwait(false)).StoreId;
        if (storeId == Guid.Empty) throw new InvalidDataException("Files store identity is unavailable.");
        var metadata = await provider.GetAsync(fileId, cancellationToken).ConfigureAwait(false);
        var reference = await provider.GetArtifactAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (!metadata.IsSuccess || !reference.IsSuccess || reference.Value!.OwnerAppId != OwnerAppId || reference.Value.ArtifactType != nameof(FilesArtifactType.Picture))
            throw new InvalidDataException("The canonical Picture artifact is unavailable or belongs to another app.");
        var scope = new ResourceScope("files.item", fileId.ToString(), metadata.Value!.CurrentRevisionId?.ToString() ?? "uncommitted", access);
        await RecheckAsync(actor, scope, access == ResourceAccess.Read ? "picture.file.open" : "picture.file.save", cancellationToken).ConfigureAwait(false);
        if ((await provider.GetStoreEvidenceAsync(storeId, cancellationToken).ConfigureAwait(false)).StoreId != storeId)
            throw new InvalidOperationException("Files store changed during resolution.");
        return (actor, provider, reference.Value, metadata.Value, scope, storeId);
    }

    private async Task RecheckCreationSourceAsync(AuthenticatedResourceActor actor, DurableDriveProvider provider,
        FilesItemRevisionPrecondition? source, CancellationToken ct)
    {
        if (source is null) return;
        if (source.ExpectedRevision is not { } revision)
            throw new InvalidDataException("The original editable-copy source revision is required.");
        var current = await provider.GetAsync(source.ItemId, ct).ConfigureAwait(false);
        if (!current.IsSuccess || current.Value!.CurrentRevisionId != revision || current.Value.Kind != HostedItemKind.Artifact)
            throw new InvalidOperationException("The original editable-copy source changed before creation.");
        await RecheckAsync(actor, new("files.item", source.ItemId.ToString(), revision.ToString(), ResourceAccess.Read),
            "picture.file.open", ct).ConfigureAwait(false);
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

    private async Task<FilesItemRevisionPrecondition?> ValidateSourceReferenceAsync(AuthenticatedResourceActor actor, DurableDriveProvider provider,
        PictureSourceAssetReference? source, CancellationToken cancellationToken)
    {
        if (source is null) return null;
        var fileId = new HostedItemId(source.FileId);
        var metadata = await provider.GetAsync(fileId, cancellationToken).ConfigureAwait(false);
        var retained = await provider.GetArtifactRevisionContentAsync(fileId, new(source.RevisionId), cancellationToken).ConfigureAwait(false);
        if (!metadata.IsSuccess || metadata.Value!.Kind != HostedItemKind.File || !retained.IsSuccess ||
            retained.Value!.Revision.SizeBytes != source.SizeBytes || !string.Equals(NormalizeHash(retained.Value.Revision.ContentHash), source.ContentHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Picture linked source does not match a retained canonical raw Files revision.");
        var scope = new ResourceScope("files.item", fileId.ToString(), metadata.Value.CurrentRevisionId?.ToString() ?? "uncommitted", ResourceAccess.Read);
        await RecheckAsync(actor, scope, "media.asset.read", cancellationToken).ConfigureAwait(false);
        if (metadata.Value.CurrentRevisionId is not { } sourceRevision)
            throw new InvalidDataException("The retained Picture source has no current canonical Files revision.");
        return new(fileId, sourceRevision);
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
