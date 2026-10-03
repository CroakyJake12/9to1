using System.Security.Cryptography;
using Haven.Application;
using HavenOS.Files;
namespace HavenOS.Apps.Canvas;

public sealed partial class CanvasFilesArtifactBridge
{
    public async Task<CanvasFilesOpenResult> OpenOriginalDisplayAsync(ICanvasOriginalDisplaySelection originalSelection,
        CancellationToken cancellationToken = default)
    {
        if (originalSelection is not OriginalDisplaySelection selected || !ReferenceEquals(selected.Issuer, this))
            throw new UnauthorizedAccessException("Original Canvas private display unavailable.");
        using var retained = await selected.RetainAsync(cancellationToken).ConfigureAwait(false);
        OriginalProviderRead read;
        try
        {
            read = await OpenOriginalProviderAsync(selected.FileId, selected.StoreId, selected.Actor, selected.Provider,
                selected.OriginalReadContext ?? throw new NotSupportedException("Original registered Files read context unavailable."), () => selected.Available, cancellationToken, selected.OriginalBinding
                    ?? throw new NotSupportedException("Original Canvas private materialization binding unavailable.")).ConfigureAwait(false);
        }
        catch(OriginalCanvasMaterializationChangedException)
        {
            // We already retain the private gate; do not synchronously Dispose/reenter it.
            selected.RetireObservedMaterialization();throw;
        }
        var opened=read.Opened;
        if(read.Binding != selected.OriginalBinding)
            throw new UnauthorizedAccessException("Original Canvas materialization binding changed.");
        if (opened.CasRevisionId != selected.CasRevisionId || opened.Artifact.ArtifactId != selected.ArtifactId ||
            opened.Artifact.RevisionId != selected.ArtifactRevision ||
            Convert.ToHexString(SHA256.HashData(CanvasArtifactCodec.Serialize(opened.Artifact))) != selected.ArtifactHash)
            throw new InvalidOperationException("Original Canvas private display baseline changed.");
        return opened;
    }

    private Task<CanvasFilesOpenResult> OpenOriginalProviderAsync(HostedItemId fileId, Guid storeId,
        AuthenticatedResourceActor actor, DurableDriveProvider provider, Func<bool> available, CancellationToken ct)
        => Task.FromException<CanvasFilesOpenResult>(new NotSupportedException("Original Canvas read requires the registered owner-issued context."));

    private sealed class OriginalCanvasMaterializationChangedException(string message) : UnauthorizedAccessException(message) { }

    private sealed record OriginalProviderRead(CanvasFilesOpenResult Opened,FilesWorkspaceDirectoryBinding Binding);

    private async Task<OriginalProviderRead> OpenOriginalProviderAsync(HostedItemId fileId, Guid storeId,
        AuthenticatedResourceActor actor, DurableDriveProvider provider, IOriginalCanonicalReadContext originalRead, Func<bool> available, CancellationToken ct, FilesWorkspaceDirectoryBinding? expectedOriginalBinding = null)
    {
        async Task Current()
        {
            ct.ThrowIfCancellationRequested();
            if (!authorization.IsIssuedOriginalReadOwnerBinding(actor, originalRead, "canvas.file.open",
                originalRead.OriginalScope, provider, directories, storeId))
                throw new UnauthorizedAccessException("Original Canvas provider or materializer is not privately issued by the registered owner.");
            if (!available() || await actors.GetCurrentAsync(ct).ConfigureAwait(false) != actor ||
                !available() || !ReferenceEquals(providers(actor), provider))
                throw new UnauthorizedAccessException("Original Canvas read actor or provider changed.");
            if (await authorization.AuthorizeOriginalReadForActorAsync(actor, originalRead, "canvas.file.open",
                [originalRead.OriginalScope], ct).ConfigureAwait(false) != actor || !available())
                throw new UnauthorizedAccessException("Original Canvas owning read retired.");
        }
        await Current().ConfigureAwait(false);
        var evidence = await provider.GetStoreEvidenceAsync(storeId, ct).ConfigureAwait(false);
        await Current().ConfigureAwait(false);
        if (evidence.StoreId != storeId) throw new UnauthorizedAccessException("Original Canvas store changed.");
        var metadata = await provider.GetForOriginalStoreAsync(storeId, fileId, ct).ConfigureAwait(false);
        await Current().ConfigureAwait(false);
        if (!metadata.IsSuccess || metadata.Value!.CurrentRevisionId is null ||
            originalRead.OriginalScope != new ResourceScope("files.item", fileId.ToString(), metadata.Value.CurrentRevisionId.Value.ToString(), ResourceAccess.Read))
            throw new UnauthorizedAccessException("Original Canvas exact owning scope changed.");
        var reference = await provider.GetArtifactForOriginalStoreAsync(storeId, fileId, metadata.Value.CurrentRevisionId, ct).ConfigureAwait(false);
        await Current().ConfigureAwait(false);
        if (!metadata.IsSuccess || !reference.IsSuccess || reference.Value!.OwnerAppId != OwnerAppId ||
            reference.Value.ArtifactType != nameof(FilesArtifactType.Canvas))
            throw new InvalidDataException("Original canonical Canvas artifact unavailable.");
        var scope = new ResourceScope("files.item", fileId.ToString(), metadata.Value!.CurrentRevisionId?.ToString() ?? "uncommitted", ResourceAccess.Read);
        if (await authorization.AuthorizeOriginalReadForActorAsync(actor, originalRead, "canvas.file.open", [scope], ct).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Original Canvas read authority unavailable.");
        await Current().ConfigureAwait(false);
        async Task<FilesWorkspaceDirectoryBinding> OriginalBinding()
        {
            await Current().ConfigureAwait(false);
            // Getter data is read only after SAME private issuer/provider/directories/store admission.
            if(actor.AccountId is not null || !Guid.TryParse(actor.ProfileId,out var profile) ||
                originalRead.OriginalMaterializationFolderId is not Guid folder || folder==Guid.Empty)
                throw new UnauthorizedAccessException("Original private Canvas materialization root unavailable.");
            var result=await directories.ResolveProfileForOriginalStoreAsync(profile,OwnerAppId,new HostedItemId(folder),provider,storeId,ct).ConfigureAwait(false);
            await Current().ConfigureAwait(false);
            if(!result.IsSuccess)throw new OriginalCanvasMaterializationChangedException("Original Canvas materialization unavailable.");
            return result.Value!;
        }
        var binding=await OriginalBinding().ConfigureAwait(false);
        if(expectedOriginalBinding is not null && binding!=expectedOriginalBinding)
            throw new OriginalCanvasMaterializationChangedException("Original Canvas private materialization was replaced before payload admission.");
        var content = await provider.GetCurrentArtifactContentForOriginalStoreAsync(storeId, fileId, metadata.Value.CurrentRevisionId, ct).ConfigureAwait(false);
        await Current().ConfigureAwait(false);
        if (!content.IsSuccess || string.IsNullOrWhiteSpace(content.Value!.ProviderContentReference))
            throw new InvalidDataException("Original Canvas content unavailable.");
        var revision = content.Value.Revision;
        if (revision.SizeBytes is null or <= 0 or > MaximumArtifactBytes)
            throw new InvalidDataException("Original Canvas payload outside supported limits.");
        var path = SafePath(binding.DirectoryPath, content.Value.ProviderContentReference);
        await Current().ConfigureAwait(false);
        if(await OriginalBinding().ConfigureAwait(false)!=binding)
            throw new OriginalCanvasMaterializationChangedException("Original Canvas materialization changed before payload read.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        if (stream.Length != revision.SizeBytes) throw new InvalidDataException("Original Canvas size changed.");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
        await Current().ConfigureAwait(false);
        if(await OriginalBinding().ConfigureAwait(false)!=binding)
            throw new OriginalCanvasMaterializationChangedException("Original Canvas materialization changed during payload read.");
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), revision.ContentHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original Canvas content hash changed.");
        var artifact = CanvasArtifactCodec.Deserialize(bytes);
        if (!Guid.TryParse(reference.Value.ArtifactId, out var id) || artifact.ArtifactId != id ||
            revision.OwningAppId != OwnerAppId || revision.OwningAppRevisionId != artifact.RevisionId.ToString("N"))
            throw new InvalidDataException("Original Canvas artifact identity changed.");
        if (await authorization.AuthorizeOriginalReadForActorAsync(actor, originalRead, "canvas.file.open", [scope], ct).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Original Canvas read authority unavailable.");
        await Current().ConfigureAwait(false);
        return new(new CanvasFilesOpenResult(artifact, revision, metadata.Value.CurrentRevisionId
            ?? throw new InvalidDataException("Original Canvas structural revision unavailable.")) { StoreId = storeId },binding);
    }
}
