using Haven.Application;
using HavenOS.Files;
using Xunit;
namespace HavenOS.Apps.Canvas.Tests;

/// <summary>Real Files publication/lifetime primitive; controlled actor/guard are not a genuine Home admission fixture.</summary>
public sealed class CanvasFilesFinalAuthorityTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Actual_Files_publication_retains_final_authority_until_acknowledgement_or_denial(bool allow)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));var ct=timeout.Token;
        var root=Path.Combine(Path.GetTempPath(),"canvas-final-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var profile=Guid.NewGuid();var actor=new Actor(new("local-profile:"+profile.ToString("D"),profile.ToString("D"),null,null,"fixture-final-authority"));
            var path=Path.Combine(root,"drive.json");var provider=new DurableDriveProvider(path,new FilesLocationId(Guid.NewGuid()),actor.Current.ActorId);
            var folder=HostedItemId.New();var now=DateTimeOffset.UtcNow;
            Assert.True((await provider.MutateAsync(new(new(Guid.NewGuid()),actor.Current.ActorId,folder,null,null,"CreateFolder",null,null,
                FilesOperationState.Pending,now,now,null,null),"Canvas",ct)).IsSuccess);
            var directories=new FilesWorkspaceDirectoryResolver(Path.Combine(root,"bindings.json"),_=>null,id=>id==profile?provider:null);
            Assert.True((await directories.RegisterProfileAsync(profile,folder,"canvas",root,ct)).IsSuccess);
            var bridge=new CanvasFilesArtifactBridge(actor,_=>provider,directories,new ResourceAuthorizationService(actor,[new Resolver(provider)]),()=>true);
            var created=await bridge.CreateAsync(CanvasArtifact.Create("Original"),ct);
            var initial=await bridge.OpenAsync(created.FileId,ct);
            var opened=await bridge.OpenForDisplayAsync(created.FileId,initial.StoreId,actor.Current,ct);
            var originalSelection=Assert.IsAssignableFrom<ICanvasOriginalDisplaySelection>(opened.OriginalSelection);
            var session=new CanvasArtifactSession(opened.Artifact);
            Assert.True(session.RenameArtifact(new(opened.Artifact.RevisionId,Guid.NewGuid(),new(actor.Current.ActorId,"Fixture")),"Candidate").IsSuccess);
            var before=await File.ReadAllBytesAsync(path,ct);var lease=new Lease();var checks=0;
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async ValueTask<CanvasFilesFinalAuthority> Capture(AuthenticatedResourceActor original,DurableDriveProvider actual,CancellationToken token)
            {
                Assert.Equal(actor.Current,original);Assert.Same(provider,actual);
                await Task.CompletedTask;
                return new(new FilesCommitAuthorityGuard(original.ActorId,async cancellation=>
                {
                    Assert.False(lease.Disposed);
                    if(Interlocked.Increment(ref checks)==1){entered.SetResult();await release.Task.WaitAsync(cancellation);}
                    return allow;
                }),lease);
            }
            var save=bridge.SaveOriginalPreparedWithFinalAuthorityAsync(originalSelection,created.FileId,session.GetArtifactSnapshot(),opened.CasRevisionId,
                opened.StoreId,actor.Current,Capture,ct);
            Task? retirement=null;
            try
            {
                await entered.Task.WaitAsync(ct);Assert.False(save.IsCompleted);Assert.False(lease.Disposed);
                var retirementEntered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                retirement=Task.Run(()=>{retirementEntered.SetResult();originalSelection.Dispose();},ct);
                await retirementEntered.Task.WaitAsync(ct);Assert.False(retirement.IsCompleted);
            }
            finally{release.TrySetResult();}
            if(allow)
            {
                var committed=await save;Assert.NotEqual(opened.CasRevisionId,committed.Id);
                Assert.Equal(committed.Id,(await bridge.OpenAsync(created.FileId,ct)).CasRevisionId);
            }
            else
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>save);
                Assert.Equal(before,await File.ReadAllBytesAsync(path,ct));
                Assert.Equal(opened.CasRevisionId,(await bridge.OpenAsync(created.FileId,ct)).CasRevisionId);
            }
            await retirement!.WaitAsync(ct);
            Assert.True(lease.Disposed);Assert.True(checks>=1);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>bridge.SaveOriginalPreparedWithFinalAuthorityAsync(originalSelection,
                created.FileId,session.GetArtifactSnapshot(),opened.CasRevisionId,opened.StoreId,actor.Current,Capture,ct));
            var candidate=session.GetArtifactSnapshot();
            Assert.True(session.RenameArtifact(new(candidate.RevisionId,Guid.NewGuid(),new(actor.Current.ActorId,"Fixture")),"Second candidate").IsSuccess);
            candidate=session.GetArtifactSnapshot();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>bridge.SaveWithFinalAuthorityAsync(created.FileId,
                candidate,(allow?(await bridge.OpenAsync(created.FileId,ct)).CasRevisionId:opened.CasRevisionId),
                opened.StoreId,actor.Current,(_,_,_)=>ValueTask.FromResult<CanvasFilesFinalAuthority>(null!),ct));
            var current=await bridge.OpenAsync(created.FileId,ct);
            var physical=await bridge.OpenForDisplayAsync(created.FileId,current.StoreId,actor.Current,ct);
            var physicalSelection=Assert.IsAssignableFrom<ICanvasOriginalDisplaySelection>(physical.OriginalSelection);
            var unchanged=await File.ReadAllBytesAsync(path,ct);
            provider=new DurableDriveProvider(path,provider.Location.Id,actor.Current.ActorId); // Same durable UUID and bytes, different owning provider.
            var captures=0;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>bridge.SaveOriginalPreparedWithFinalAuthorityAsync(physicalSelection,
                created.FileId,candidate,physical.CasRevisionId,physical.StoreId,actor.Current,(_,_,_)=>
                {Interlocked.Increment(ref captures);return ValueTask.FromResult<CanvasFilesFinalAuthority>(null!);},ct));
            Assert.Equal(0,captures);Assert.Equal(unchanged,await File.ReadAllBytesAsync(path,ct));physicalSelection.Dispose();
        }
        finally{Directory.Delete(root,true);}
    }
    private sealed class Lease:IAsyncDisposable
    {public bool Disposed;public ValueTask DisposeAsync(){Disposed=true;return ValueTask.CompletedTask;}}
    private sealed class Actor(AuthenticatedResourceActor actor):IAuthenticatedResourceActorSource
    {public AuthenticatedResourceActor Current=>actor;public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct)=>ValueTask.FromResult<AuthenticatedResourceActor?>(actor);}
    private sealed class Resolver(DurableDriveProvider provider):ICanonicalResourceAccessResolver
    {
        public string ResourceKind=>"files.item";
        public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor,string action,ResourceScope scope,CancellationToken ct)
        {
            var item=await provider.GetAsync(new(Guid.Parse(scope.Id)),ct);
            var allowed=item.IsSuccess&&item.Value!.OwnerPrincipalId==actor.ActorId&&(item.Value.CurrentRevisionId?.ToString()??"uncommitted")==scope.Revision
                &&(action,scope.Access) is ("canvas.file.open",ResourceAccess.Read) or ("canvas.file.save",ResourceAccess.Write) or ("canvas.file.create",ResourceAccess.Write);
            return new(allowed,allowed?"Allowed":"Denied",actor.ActorId,scope.Revision,actor.OrganisationId);
        }
    }
}
