using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace HavenOS.Apps.Canvas.Tests;

/// <summary>Registered original Files owner read and final publication lifetime; the controlled final guard is not a full native Home admission fixture.</summary>
public sealed class CanvasFilesFinalAuthorityTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Actual_Files_publication_retains_final_authority_until_acknowledgement_or_denial(bool allow)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));var ct=timeout.Token;
        var root=Path.Combine(Path.GetTempPath(),"canvas-final-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var failures=new List<Exception>();
        Task? save=null;Task? retirement=null;ICanvasOriginalDisplaySelection? originalSelection=null,physicalSelection=null;
        TaskCompletionSource? release=null;Exception? assertedSaveDenial=null;
        void Add(Exception error){if(!failures.Any(existing=>ReferenceEquals(existing,error)))failures.Add(error);}
        try
        {
            var homeStore=new FileHomeCoreStateStore(Path.Combine(root,"home.json"));
            var profiles=new HomeLocalProfileIdentity(homeStore,new OperatingSystemPrincipalSource());
            var actor=await profiles.GetCurrentAsync(ct)??throw new InvalidOperationException("Actual OS profile unavailable.");
            var nativeFiles=new NativeFilesWorkspaceService(homeStore,profiles);
            var ownership=new HomeLocalStoreOwnership(homeStore,profiles,new HomeLocalStoreEvidenceRegistry([nativeFiles]),
                new HomePermissionTrustService(homeStore,(_,_)=>null));
            var ownershipAuthority=new HomeResourceStoreOwnershipAuthority(ownership,profiles);
            var authority=new NativeFilesWorkspaceAuthority(nativeFiles,profiles,ownershipAuthority);
            var chosen=Path.Combine(root,"chosen-empty");Directory.CreateDirectory(chosen);
            var configured=await nativeFiles.ConfigureNewAsync(chosen,ownership,ct);
            var workspace=await authority.GetCurrentAsync(configured.Configuration.StoreId,ct)
                ??throw new InvalidOperationException("Configured owning Files workspace unavailable.");
            Assert.Equal(configured.Configuration.StoreId,workspace.Configuration.StoreId);
            Assert.Equal(actor,workspace.Actor);
            var profile=Guid.Parse(actor.ProfileId);var path=Path.Combine(chosen,".9to1-files","drive.json");
            var provider=workspace.Provider;var directories=workspace.Directories;var folder=workspace.Configuration.AppFolders["canvas"];
            Assert.True((await provider.GetAsync(folder,ct)).IsSuccess);
            Assert.True((await directories.ResolveProfileAsync(profile,"canvas",ct)).IsSuccess);
            var originalReadOwner=new FilesArtifactResourceResolver(authority);
            var bridge=new CanvasFilesArtifactBridge(profiles,current=>current==actor?provider:null,directories,
                new ResourceAuthorizationService(profiles,[originalReadOwner]),()=>true);
            var originalLifetime=true;
            var created=await bridge.CreateAsync(CanvasArtifact.Create("Original"),ct);
            var initial=await bridge.OpenAsync(created.FileId,ct);
            var context=await originalReadOwner.CaptureOriginalCanvasReadAsync(workspace,created.FileId,()=>originalLifetime,ct);
            var opened=await bridge.OpenForDisplayAsync(created.FileId,initial.StoreId,actor,provider,context,ct);
            var selected=Assert.IsAssignableFrom<ICanvasOriginalDisplaySelection>(opened.OriginalSelection);originalSelection=selected;
            var session=new CanvasArtifactSession(opened.Artifact);
            Assert.True(session.RenameArtifact(new(opened.Artifact.RevisionId,Guid.NewGuid(),new(actor.ActorId,"Fixture")),"Candidate").IsSuccess);
            var before=await File.ReadAllBytesAsync(path,ct);var lease=new Lease();var checks=0;
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var originalRelease=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);release=originalRelease;
            async ValueTask<CanvasFilesFinalAuthority> Capture(AuthenticatedResourceActor original,DurableDriveProvider actual,CancellationToken token)
            {
                Assert.Equal(actor,original);Assert.Same(provider,actual);
                await Task.CompletedTask;
                return new(new FilesCommitAuthorityGuard(original.ActorId,async cancellation=>
                {
                    Assert.False(lease.Disposed);
                    if(Interlocked.Increment(ref checks)==1){entered.SetResult();await originalRelease.Task.WaitAsync(cancellation);}
                    return allow;
                }),lease);
            }
            var originalSave=bridge.SaveOriginalPreparedWithFinalAuthorityAsync(selected,created.FileId,session.GetArtifactSnapshot(),opened.CasRevisionId,
                opened.StoreId,actor,Capture,ct);
            save=originalSave;
            try
            {
                await entered.Task.WaitAsync(ct);Assert.False(originalSave.IsCompleted);Assert.False(lease.Disposed);
                var retirementEntered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                retirement=Task.Run(()=>{retirementEntered.SetResult();selected.Dispose();},ct);
                await retirementEntered.Task.WaitAsync(ct);Assert.False(retirement.IsCompleted);
            }
            finally{originalRelease.TrySetResult();}
            if(allow)
            {
                var committed=await originalSave;Assert.NotEqual(opened.CasRevisionId,committed.Id);
                Assert.Equal(committed.Id,(await bridge.OpenAsync(created.FileId,ct)).CasRevisionId);
            }
            else
            {
                assertedSaveDenial=await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>originalSave);
                Assert.Equal(before,await File.ReadAllBytesAsync(path,ct));
                Assert.Equal(opened.CasRevisionId,(await bridge.OpenAsync(created.FileId,ct)).CasRevisionId);
            }
            await retirement!.WaitAsync(ct);
            Assert.True(lease.Disposed);Assert.True(checks>=1);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>bridge.SaveOriginalPreparedWithFinalAuthorityAsync(selected,
                created.FileId,session.GetArtifactSnapshot(),opened.CasRevisionId,opened.StoreId,actor,Capture,ct));
            var candidate=session.GetArtifactSnapshot();
            Assert.True(session.RenameArtifact(new(candidate.RevisionId,Guid.NewGuid(),new(actor.ActorId,"Fixture")),"Second candidate").IsSuccess);
            candidate=session.GetArtifactSnapshot();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async()=>await bridge.SaveWithFinalAuthorityAsync(created.FileId,
                candidate,(allow?(await bridge.OpenAsync(created.FileId,ct)).CasRevisionId:opened.CasRevisionId),
                opened.StoreId,actor,(_,_,_)=>ValueTask.FromResult<CanvasFilesFinalAuthority>(null!),ct));
            var current=await bridge.OpenAsync(created.FileId,ct);
            var physicalContext=await originalReadOwner.CaptureOriginalCanvasReadAsync(workspace,created.FileId,()=>originalLifetime,ct);
            var physical=await bridge.OpenForDisplayAsync(created.FileId,current.StoreId,actor,provider,physicalContext,ct);
            var selectedPhysical=Assert.IsAssignableFrom<ICanvasOriginalDisplaySelection>(physical.OriginalSelection);physicalSelection=selectedPhysical;
            var unchanged=await File.ReadAllBytesAsync(path,ct);
            provider=new DurableDriveProvider(path,provider.Location.Id,actor.ActorId); // Same durable UUID and bytes, different owning provider.
            var captures=0;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>bridge.SaveOriginalPreparedWithFinalAuthorityAsync(selectedPhysical,
                created.FileId,candidate,physical.CasRevisionId,physical.StoreId,actor,(_,_,_)=>
                {Interlocked.Increment(ref captures);return ValueTask.FromResult<CanvasFilesFinalAuthority>(null!);},ct));
            Assert.Equal(0,captures);Assert.Equal(unchanged,await File.ReadAllBytesAsync(path,ct));selectedPhysical.Dispose();
            originalLifetime=false;
        }
        catch(Exception error){Add(error);}
        finally
        {
            release?.TrySetResult();
            if(save is not null)try{await save;}catch(Exception error){if(!ReferenceEquals(error,assertedSaveDenial))Add(error);}
            if(retirement is not null)try{await retirement;}catch(Exception error){Add(error);}
            if(originalSelection is not null)try{originalSelection.Dispose();}catch(Exception error){Add(error);}
            if(physicalSelection is not null)try{physicalSelection.Dispose();}catch(Exception error){Add(error);}
            try{Directory.Delete(root,true);}catch(Exception error){Add(error);}
        }
        if(failures.Count==1)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if(failures.Count>1)throw new AggregateException(failures);

    }
    private sealed class Lease:IAsyncDisposable
    {public bool Disposed;public ValueTask DisposeAsync(){Disposed=true;return ValueTask.CompletedTask;}}
}
