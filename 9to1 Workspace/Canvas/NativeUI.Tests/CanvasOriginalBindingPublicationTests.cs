using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using HavenOS.Files;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace HavenOS.Apps.Canvas.Tests;

public sealed partial class CanvasHomeNativeInsertionOperationTests
{
    private static object ActualPrivateField(object original,string name)=>
        original.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(original)
            ??throw new InvalidOperationException("Required actual owner field unavailable: "+name);
    private static async Task UntilActualOwner(Func<bool> observed,CancellationToken ct)
    {
        var elapsed=Stopwatch.StartNew();
        while(!observed())
        {
            ct.ThrowIfCancellationRequested();
            if(elapsed.Elapsed>TimeSpan.FromSeconds(30))throw new TimeoutException("Actual owning queue was not observed.");
            await Task.Delay(10,ct);
        }
    }

    [Fact]
    public async Task Actual_binding_owner_update_waits_through_Files_publication_and_original_Home_fence()
    {
        await using var fixture=await Fixture.Create();using var display=await fixture.CaptureDisplay();
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(90));var ct=deadline.Token;
        var page=display.Opened.Artifact.Pages.Single();var store=fixture.LayerStore();
        var intent=store.Capture(display,new(CanvasNativeLayerEditKind.SetLocked,page.PageId,page.LayerOrder[0],Value:true));
        var capability=await fixture.ApproveLayer(intent);
        var claim=await fixture.Home.ClaimExecutionWithOriginalWriteObservedAsync(capability,CanvasStrokeWriteIntent.TargetAppId,
            CanvasStrokeWriteIntent.ActionId,intent.Scopes,intent.Arguments,display.OriginalWrite,ct);
        Assert.Equal(HomeResourceClaimDisposition.Claimed,claim.Disposition);Assert.Equal(intent.OriginalActor,claim.Actor);
        var workspace=fixture.OriginalWorkspace;var profile=Guid.Parse(workspace.Actor.ProfileId);
        var binding=(await workspace.Directories.ResolveProfileForOriginalStoreAsync(profile,"canvas",
            workspace.Configuration.AppFolders["canvas"],workspace.Provider,intent.StoreId,ct)).Value!;
        var owner=ActualPrivateField(workspace.Directories,"_store");
        var bindingGate=Assert.IsType<SemaphoreSlim>(ActualPrivateField(owner,"_gate"));
        var completionGate=Assert.IsType<SemaphoreSlim>(ActualPrivateField(capability,"_completionGate"));
        var queue=typeof(SemaphoreSlim).GetField("m_asyncHead",BindingFlags.Instance|BindingFlags.NonPublic)
            ??throw new InvalidOperationException("Required actual net10 semaphore queue unavailable.");
        var update=owner.GetType().GetMethod("UpdateAsync",[typeof(Func<FilesWorkspaceDirectoryResolver.BindingState,FilesWorkspaceDirectoryResolver.BindingState>),typeof(CancellationToken)])
            ??throw new InvalidOperationException("Required actual owner UpdateAsync unavailable.");
        var replacement=Path.Combine(fixture.RootDirectory,"real-binding-owner-replacement");Directory.CreateDirectory(replacement);
        Task<FilesRevision>? publication=null;Task<FilesWorkspaceDirectoryResolver.BindingState>? mutation=null;
        Exception? primary=null;var retained=false;
        try
        {
            await completionGate.WaitAsync(ct);retained=true;
            publication=store.SaveAsync(intent,capability,claim.Actor!,ct);
            await UntilActualOwner(()=>queue.GetValue(completionGate) is not null,ct);
            Assert.Equal(0,bindingGate.CurrentCount);Assert.False(publication.IsCompleted);
            // Invoke the genuine persisted binding owner primitive. This is controlled owner concurrency,
            // not a trusted Register call (Register reads Files first and cannot isolate this lease).
            Func<FilesWorkspaceDirectoryResolver.BindingState,FilesWorkspaceDirectoryResolver.BindingState> replace=state=>
                new(state.Bindings.Select(item=>item==binding?item with {DirectoryPath=replacement}:item).ToArray());
            mutation=Assert.IsAssignableFrom<Task<FilesWorkspaceDirectoryResolver.BindingState>>(update.Invoke(owner,[replace,ct]));
            await UntilActualOwner(()=>queue.GetValue(bindingGate) is not null,ct);
            Assert.False(mutation.IsCompleted);Assert.False(publication.IsCompleted);
            completionGate.Release();retained=false;
            var acknowledged=await publication.WaitAsync(ct);var changed=await mutation.WaitAsync(ct);
            Assert.Equal(intent.FilesRevision,acknowledged.ParentRevisionId);Assert.NotEqual(intent.FilesRevision,acknowledged.Id);
            Assert.Equal(replacement,Assert.Single(changed.Bindings,item=>item==(binding with {DirectoryPath=replacement})).DirectoryPath);
            var content=await workspace.Provider.GetCurrentArtifactContentForOriginalStoreAsync(intent.StoreId,fixture.FileId,acknowledged.Id,ct);
            Assert.True(content.IsSuccess);
            var bytes=await File.ReadAllBytesAsync(Path.Combine(binding.DirectoryPath,content.Value!.ProviderContentReference!),ct);
            Assert.Equal(intent.PreparedHash,Convert.ToHexString(SHA256.HashData(bytes)));
            using var native=CanvasRnoteDocument.Open(bytes);Assert.True(native.Snapshot.Pages.Single().Layers.Single().IsLocked);
            var drive=await File.ReadAllBytesAsync(fixture.StatePath,ct);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>fixture.Files.OpenOriginalDisplayAsync(display.Opened.OriginalSelection!,ct));
            Assert.Equal(drive,await File.ReadAllBytesAsync(fixture.StatePath,ct));
            Assert.True((await fixture.Home.CompleteExecutionAsync(capability,new(HomePermissionRequestState.Succeeded,
                "CANVAS_BINDING_RETAINED_ACK","Actual original Files publication acknowledged before owner binding replacement.",[]),ct)).Succeeded);
        }
        catch(Exception error){primary=error;throw;}
        finally
        {
            if(retained)completionGate.Release();
            // Release and observe BOTH genuine owner tasks even when a bounded observation failed.
            // Never delete fixture files while an actual owner still operates on them.
            Exception? cleanup=null;
            if(publication is not null)try{await publication;}catch(Exception error){cleanup=error;}
            if(mutation is not null)try{await mutation;}catch(Exception error){cleanup??=error;}
            if(primary is null&&cleanup is not null)ExceptionDispatchInfo.Capture(cleanup).Throw();
        }
    }
}
