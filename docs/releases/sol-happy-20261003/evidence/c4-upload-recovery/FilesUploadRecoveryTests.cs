using System.Security.Cryptography;
using HavenOS.Files;
using Xunit;

namespace HostedFiles.Recovery.Tests;

public sealed class FilesUploadRecoveryTests
{
    private sealed class Store : IDisposable
    {
        public string Directory {get;}=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"c4-upload-"+Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Directory,"files.json");
        public FilesLocationId Location {get;}=new(Guid.NewGuid());
        public DurableDriveProvider Open()=>new(Path,Location,"controlled-owner");
        public void Dispose(){if(System.IO.Directory.Exists(Directory))System.IO.Directory.Delete(Directory,true);}
    }
    private static FilesUploadedContent Content()=>new(new(Guid.NewGuid()),null,"file.bin","application/octet-stream",
        new(Guid.NewGuid()),null,"controlled-owner",DateTimeOffset.UtcNow,3,
        Convert.ToHexString(SHA256.HashData(new byte[]{1,2,3})),"immutable/file.bin");
    private static FilesCommitAuthorityGuard Current()=>new("controlled-owner",_=>ValueTask.FromResult(true));

    [Fact]
    public async Task ReopenAfterUnobservedAcknowledgmentReplaysSameRevisionWithoutDuplicateEvents()
    {
        using var store=new Store();var first=store.Open();var original=await first.GetStoreEvidenceAsync();var content=Content();
        var committed=await first.CommitUploadedContentAsync(content,[],original.StoreId,Current(),default);
        Assert.True(committed.IsSuccess);Assert.Equal(content.RevisionId,committed.Value!.Id);
        var reopened=store.Open();var recovered=await reopened.CommitUploadedContentAsync(content,[],original.StoreId,Current(),default);
        Assert.True(recovered.IsSuccess);Assert.Equal(committed.Value,recovered.Value);
        var events=await reopened.GetChangesAsync(null,100,default);Assert.Single(events.Items);
        Assert.Equal(content.RevisionId,events.Items[0].ResultRevisionId);
        var retained=await reopened.GetArtifactRevisionContentAsync(content.FileId,content.RevisionId);
        Assert.True(retained.IsSuccess);Assert.Equal(content.ProviderContentReference,retained.Value!.ProviderContentReference);
    }

    [Fact]
    public async Task ChangedReplayIsDeniedAndOriginalBytesRemainPersisted()
    {
        using var store=new Store();var provider=store.Open();var original=await provider.GetStoreEvidenceAsync();var content=Content();
        Assert.True((await provider.CommitUploadedContentAsync(content,[],original.StoreId,Current(),default)).IsSuccess);
        var before=await File.ReadAllBytesAsync(store.Path);
        var changed=await store.Open().CommitUploadedContentAsync(content with{SizeBytes=4},[],original.StoreId,Current(),default);
        Assert.False(changed.IsSuccess);Assert.Equal(FilesErrorCode.InvalidState,changed.Error!.Code);
        Assert.Equal(before,await File.ReadAllBytesAsync(store.Path));
        Assert.Single((await provider.GetChangesAsync(null,100,default)).Items);
    }

    [Fact]
    public async Task RetirementAtFinalPublicationRefusesEffectAndRetryAfterReopenCommitsOnce()
    {
        using var store=new Store();var provider=store.Open();var original=await provider.GetStoreEvidenceAsync();var content=Content();
        Assert.True((await provider.CommitUploadedContentAsync(content,[],original.StoreId,Current(),default)).IsSuccess);
        var priorRevision=content.RevisionId;
        content=content with {RevisionId=new(Guid.NewGuid()),ExpectedRevision=priorRevision};
        var before=await File.ReadAllBytesAsync(store.Path);var checks=0;
        var retiring=new FilesCommitAuthorityGuard("controlled-owner",_=>ValueTask.FromResult(++checks==1));
        var refused=await provider.CommitUploadedContentAsync(content,[],original.StoreId,retiring,default);
        Assert.Equal(2,checks);Assert.False(refused.IsSuccess);Assert.Equal(FilesErrorCode.PermissionDenied,refused.Error!.Code);
        Assert.Equal(before,await File.ReadAllBytesAsync(store.Path));
        Assert.Empty(System.IO.Directory.GetFiles(store.Directory,"*.tmp"));
        Assert.Single((await store.Open().GetChangesAsync(null,100,default)).Items);
        Assert.Equal(priorRevision,(await store.Open().GetAsync(content.FileId,default)).Value!.CurrentRevisionId);
        var retry=await store.Open().CommitUploadedContentAsync(content,[],original.StoreId,Current(),default);
        Assert.True(retry.IsSuccess);Assert.Equal(content.RevisionId,(await store.Open().GetAsync(content.FileId,default)).Value!.CurrentRevisionId);
        Assert.Equal(2,(await store.Open().GetChangesAsync(null,100,default)).Items.Count);
    }

    [Fact]
    public async Task ChangedStoreFenceRefusesEvenPreviouslyCommittedReplay()
    {
        using var store=new Store();var provider=store.Open();var original=await provider.GetStoreEvidenceAsync();var content=Content();
        Assert.True((await provider.CommitUploadedContentAsync(content,[],original.StoreId,Current(),default)).IsSuccess);
        var before=await File.ReadAllBytesAsync(store.Path);
        var result=await store.Open().CommitUploadedContentAsync(content,[],Guid.NewGuid(),Current(),default);
        Assert.False(result.IsSuccess);Assert.Equal(FilesErrorCode.RevisionConflict,result.Error!.Code);
        Assert.Equal(before,await File.ReadAllBytesAsync(store.Path));
    }
}
