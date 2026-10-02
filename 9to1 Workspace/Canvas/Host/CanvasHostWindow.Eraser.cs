using Haven.Application;
using System.Text.Json;
using CakeOS.Cui.Runtime;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
namespace HavenOS.Apps.Canvas;
public sealed partial class CanvasHostWindow
{
    private CanvasNativeEraserContext CreateEraserContext(HostedItemId fileId,CanvasFilesOpenResult opened,CanvasFilesArtifactBridge files,NativeFilesWorkspace original)
    {
        var storeId=opened.StoreId;
        var actor=original.Actor;
        var provider=original.Provider;
        var broker=Get<HomeResourceOperationBroker>();var actors=Get<IAuthenticatedResourceActorSource>();
        var whole=new CanvasHomeEraserOperation(files,broker,actors);
        var partial=new CanvasHomeSplitEraserOperation(files,broker,actors);
        var quick=new CanvasHomeQuickEraserOperation(files,broker,actors);
        var keyed=new CanvasHomeStrokeEditOperation(files,broker,actors);
        var history=new CanvasHomeHistoryOperation(files,broker,actors);
        bool Available()=>WriteAvailable()&&!_closed&&!_busy&&_pendingRequest is null&&_pendingOwnership is null&&_pendingAudit is null&&_pendingBeginAudit is null&&!_requestUncertain &&
            _workspace?.Actor==actor&&_workspace.Configuration.StoreId==storeId&&ReferenceEquals(_workspace.Provider,provider);
        async Task<OwnerCommit> Reopen(HostedItemId id,Guid artifactId,Guid revision,CancellationToken token)
        {
            await RequireOriginalOwnerAsync(original,storeId,token);
            var current=await files.OpenAsync(id,storeId,token);
            if(current.Artifact.ArtifactId!=artifactId||current.Artifact.RevisionId!=revision)
                throw new InvalidOperationException("The committed Canvas changed; reopen its current revision.");
            return new(current,id);
        }
        async Task Request(IReadOnlyList<ResourceScope> scopes,JsonElement args,string preview,
            Func<HomeResourceExecutionCapability,CancellationToken,Task<OwnerCommit>> execute,CancellationToken token)
        {
            if(!Available())throw new UnauthorizedAccessException("Finish the current Home request before erasing.");
            await RequireOriginalOwnerAsync(original,storeId,token);
            _busy=true;RefreshBindings();
            try{SetStatus(await RequestAsync(CanvasStrokeWriteIntent.ActionId,scopes,args,preview,execute,null,token));}
            finally{_busy=false;RefreshBindings();}
        }
        return new(fileId,opened,Available,
            (intent,token)=>Request(intent.Scopes,intent.Arguments,"Erase these exact colliding strokes",async(cap,cancel)=>
            {var commit=await whole.ExecuteAsync(intent,cap,cancel);return await Reopen(commit.FileId,commit.Artifact.ArtifactId,commit.Artifact.RevisionId,cancel);},token),
            (intent,token)=>Request(intent.Scopes,intent.Arguments,"Erase these exact stroke segments",async(cap,cancel)=>
            {var commit=await partial.ExecuteAsync(intent,cap,cancel);return await Reopen(commit.FileId,commit.Artifact.ArtifactId,commit.Artifact.RevisionId,cancel);},token),
            (intent,token)=>Request(intent.Scopes,intent.Arguments,"Erase this exact resolved stroke",async(cap,cancel)=>
            {var commit=await quick.ExecuteAsync(intent,cap,cancel);return await Reopen(commit.FileId,commit.Artifact.ArtifactId,commit.Artifact.RevisionId,cancel);},token),
            (intent,token)=>Request(intent.Scopes,intent.Arguments,"Erase this chosen stroke",async(cap,cancel)=>
            {var commit=await keyed.ExecuteAsync(intent,cap,cancel);return await Reopen(commit.FileId,commit.Artifact.ArtifactId,commit.Artifact.RevisionId,cancel);},token),
            (intent,token)=>Request(intent.Scopes,intent.Arguments,intent.Kind==CanvasHistoryKind.Undo ? "Undo this exact Canvas revision" : "Redo this exact Canvas revision",async(cap,cancel)=>
            {var commit=await history.ExecuteAsync(intent,cap,cancel);return await Reopen(commit.FileId,commit.Artifact.ArtifactId,commit.Artifact.RevisionId,cancel);},token));
    }
    private async Task RequireOriginalOwnerAsync(NativeFilesWorkspace original,Guid expectedStoreId,CancellationToken token)
    {
        if(await Get<IAuthenticatedResourceActorSource>().GetCurrentAsync(token)!=original.Actor)
            throw new UnauthorizedAccessException("The original Canvas actor changed.");
        var current=await Get<NativeFilesWorkspaceAuthority>().GetCurrentAsync(expectedStoreId,token);
        if(current is null||current.Actor!=original.Actor||!ReferenceEquals(current.Provider,original.Provider))
            throw new UnauthorizedAccessException("The original Canvas Files workspace changed.");
    }
    private sealed class OriginalCanvasReadiness(ICuiSceneReadiness inner,Func<CancellationToken,Task> validate) : ICuiSceneReadiness
    {
        public async ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken)
        {
            try{await validate(cancellationToken);return await inner.CheckAsync(cancellationToken);}
            catch(OperationCanceledException){throw;}
            catch(Exception error){return new(CuiSceneAvailabilityState.Unavailable,"CanvasOriginalOwnerUnavailable",error.Message);}
        }
    }
}
