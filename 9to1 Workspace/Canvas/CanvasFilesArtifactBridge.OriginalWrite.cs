using System.Security.Cryptography;
using Haven.Application;
using HavenOS.Files;
namespace HavenOS.Apps.Canvas;

public sealed partial class CanvasFilesArtifactBridge
{
    /// <summary>Inactive explicit original-provider write route; the final authority must also retain the original binding.</summary>
    public async Task<FilesRevision> SaveOriginalPreparedWithOriginalWriteAuthorityAsync(
        ICanvasOriginalDisplaySelection originalSelection,IOriginalCanonicalWriteContext originalWrite,
        HostedItemId fileId,CanvasArtifact prepared,FilesRevisionId expectedFileRevision,Guid expectedStoreId,
        AuthenticatedResourceActor claimedActor,
        Func<AuthenticatedResourceActor,DurableDriveProvider,CancellationToken,ValueTask<CanvasFilesFinalAuthority>> captureFinalAuthority,
        CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(prepared);ArgumentNullException.ThrowIfNull(originalWrite);
        ArgumentNullException.ThrowIfNull(captureFinalAuthority);ArgumentNullException.ThrowIfNull(claimedActor);
        if(originalSelection is not OriginalDisplaySelection selected||!ReferenceEquals(selected.Issuer,this)||!selected.Available||
            selected.Actor!=claimedActor||selected.FileId!=fileId||selected.StoreId!=expectedStoreId||
            selected.CasRevisionId!=expectedFileRevision||selected.ArtifactId!=prepared.ArtifactId||
            selected.OriginalReadContext is null||selected.OriginalBinding is null)
            throw new UnauthorizedAccessException("Original Canvas private write provenance unavailable.");
        using var retained=await selected.RetainAsync(cancellationToken).ConfigureAwait(false);
        var provider=selected.Provider;var actor=selected.Actor;var binding=selected.OriginalBinding;
        var scope=new ResourceScope("files.item",fileId.ToString(),expectedFileRevision.ToString(),ResourceAccess.Write);
        async Task Current()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(!selected.Available||!hostAllowsWrites()||
                !authorization.IsIssuedOriginalReadOwnerBinding(actor,selected.OriginalReadContext,"canvas.file.open",
                    selected.OriginalReadContext.OriginalScope,provider,directories,expectedStoreId)||
                !authorization.IsOriginalWriteContextForActor(actor,originalWrite,"canvas.file.save",[scope]))
                throw new UnauthorizedAccessException("Exact original Canvas write issuer unavailable.");
            if(await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false)!=actor||
                !selected.Available||!ReferenceEquals(providers(actor),provider))
                throw new UnauthorizedAccessException("Original Canvas write actor/provider retired.");
            if(await authorization.AuthorizeOriginalWriteForActorAsync(actor,originalWrite,"canvas.file.save",[scope],cancellationToken).ConfigureAwait(false)!=actor||
                !selected.Available||!hostAllowsWrites())
                throw new UnauthorizedAccessException("Original Canvas write observation refused.");
        }
        async Task BindingCurrent()
        {
            await Current().ConfigureAwait(false);
            if(actor.AccountId is not null||!Guid.TryParse(actor.ProfileId,out var profile)||
                selected.OriginalReadContext.OriginalMaterializationFolderId is not Guid folder||folder==Guid.Empty)
                throw new UnauthorizedAccessException("Original private materialization unavailable.");
            var actual=await directories.ResolveProfileForOriginalStoreAsync(profile,OwnerAppId,new HostedItemId(folder),
                provider,expectedStoreId,cancellationToken).ConfigureAwait(false);
            await Current().ConfigureAwait(false);
            if(!actual.IsSuccess||actual.Value!=binding)
            {
                selected.RetireObservedMaterialization();
                throw new UnauthorizedAccessException("Original Canvas write materialization changed.");
            }
        }
        await BindingCurrent().ConfigureAwait(false);
        var baseline=await OpenOriginalProviderAsync(fileId,expectedStoreId,actor,provider,selected.OriginalReadContext,
            ()=>selected.Available,cancellationToken,binding).ConfigureAwait(false);
        await Current().ConfigureAwait(false);
        if(baseline.Opened.CasRevisionId!=selected.CasRevisionId||baseline.Opened.Artifact.ArtifactId!=selected.ArtifactId||
            baseline.Opened.Artifact.RevisionId!=selected.ArtifactRevision||
            Convert.ToHexString(SHA256.HashData(CanvasArtifactCodec.Serialize(baseline.Opened.Artifact)))!=selected.ArtifactHash)
            throw new InvalidOperationException("Original Canvas private write baseline changed.");
        var metadata=await provider.GetForOriginalStoreAsync(expectedStoreId,fileId,cancellationToken).ConfigureAwait(false);
        await Current().ConfigureAwait(false);
        if(!metadata.IsSuccess||metadata.Value!.CurrentRevisionId!=expectedFileRevision)
            throw new InvalidOperationException("Original Canvas write metadata baseline changed.");
        var bytes=CanvasArtifactCodec.Serialize(prepared);
        if(bytes.LongLength>MaximumArtifactBytes||prepared.RevisionId==selected.ArtifactRevision)
            throw new InvalidDataException("Original Canvas prepared effect unavailable.");
        var hash=Convert.ToHexString(SHA256.HashData(bytes));
        var relative=Path.Combine(".9to1-artifacts",fileId.ToString(),Guid.NewGuid().ToString("N")+".9to1c");
        await BindingCurrent().ConfigureAwait(false);
        var destination=SafePath(binding.DirectoryPath,relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await BindingCurrent().ConfigureAwait(false);
        destination=SafePath(binding.DirectoryPath,relative);
        await using(var stream=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,useAsync:true))
        {
            await stream.WriteAsync(bytes,cancellationToken).ConfigureAwait(false);
            await Current().ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk:true);
        }
        await BindingCurrent().ConfigureAwait(false);
        // No observational Home/resource/directory callbacks after final authority capture.
        // Its guard executes inside Files metadata ownership; actual binding commit lease is lazily retained inside Files ownership before Home.
        var homeAuthority=await captureFinalAuthority(actor,provider,cancellationToken).ConfigureAwait(false)
            ??throw new UnauthorizedAccessException("Original Canvas final authority unavailable.");
        await using var authority=RetainOriginalBindingFinalAuthority(selected,homeAuthority);
        var commit=new FilesOwningAppRevisionCommit(fileId,OwnerAppId,prepared.RevisionId.ToString("N"),
            metadata.Value!.OwnerPrincipalId,DateTimeOffset.UtcNow,bytes.LongLength,hash,relative,expectedFileRevision);
        var result=await provider.CommitDurableRevisionAsync(commit,Array.Empty<FilesItemRevisionPrecondition>(),expectedStoreId,
            authority.Guard,cancellationToken).ConfigureAwait(false);
        if(result.Error?.Code==FilesErrorCode.PermissionDenied)
            throw new UnauthorizedAccessException("Original Canvas final publication refused.");
        if(!result.IsSuccess)throw new InvalidOperationException(result.Error!.Message+" Candidate remains recoverable; observe the owning receipt before retrying.");
        return result.Value!;
    }
}
