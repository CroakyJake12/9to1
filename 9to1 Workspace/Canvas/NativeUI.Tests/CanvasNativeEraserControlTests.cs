using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace HavenOS.Apps.Canvas.NativeUI.Tests;
[Collection("Canvas native UI")]
public sealed class CanvasNativeEraserControlTests
{
    [Theory]
    [InlineData(PointerType.Pen, false, true, true)]
    [InlineData(PointerType.Pen, true, false, true)]
    [InlineData(PointerType.Pen, false, false, false)]
    [InlineData(PointerType.Touch, false, false, true)]
    public void Native_eraser_accepts_pen_inversion_or_primary_press_but_not_non_pen_eraser_flag(
        PointerType type, bool primary, bool eraserEnd, bool expected)
        => Assert.Equal(expected, CanvasNativeEraserInput.BeginsEraserGesture(type, primary, eraserEnd));

    [Fact]
    public void Release_adapter_claims_only_the_matching_live_pointer_once()
    {
        var pointer=new object();
        Assert.True(CanvasNativeEraserInput.CanQueueRelease(pointer,pointer,false,false,false));
        Assert.False(CanvasNativeEraserInput.CanQueueRelease(null,pointer,false,false,false));
        Assert.False(CanvasNativeEraserInput.CanQueueRelease(pointer,new object(),false,false,false));
        Assert.False(CanvasNativeEraserInput.CanQueueRelease(pointer,pointer,true,false,false));
        Assert.False(CanvasNativeEraserInput.CanQueueRelease(pointer,pointer,false,true,false));
        Assert.False(CanvasNativeEraserInput.CanQueueRelease(pointer,pointer,false,false,true));
        Assert.True(CanvasNativeEraserInput.IsRelevantCaptureLoss(pointer,pointer,false));
        Assert.False(CanvasNativeEraserInput.IsRelevantCaptureLoss(pointer,new object(),false));
        Assert.False(CanvasNativeEraserInput.IsRelevantCaptureLoss(null,pointer,false));
        Assert.False(CanvasNativeEraserInput.IsRelevantCaptureLoss(pointer,pointer,true));
        Assert.False(CanvasNativeEraserInput.CanBeginPointerGesture(true,false,true,PointerType.Pen,false,true));
        Assert.False(CanvasNativeEraserInput.CanBeginPointerGesture(false,false,false,PointerType.Pen,false,true));
        Assert.False(CanvasNativeEraserInput.CanBeginPointerGesture(true,true,false,PointerType.Pen,false,true));
    }

    [Fact]
    public async Task Delayed_release_cannot_consume_same_pointer_repress_or_survive_close_invalidation()
    {
        var state=new CanvasNativeEraserInput.ReleaseGeneration();
        var pointer=new object();var oldIntent=new object();var newIntent=new object();
        var oldGeneration=state.Begin(pointer);
        var oldRelease=new CanvasNativeEraserInput.QueuedRelease(oldGeneration,pointer,
            (Guid.NewGuid(),Guid.NewGuid()),new(),oldIntent,_=>Task.CompletedTask);
        Assert.True(state.Queue(oldRelease));
        var blocked=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed=ObserveWhenReleasedAsync(blocked.Task,state,oldRelease);

        // Capture loss/cancel followed by a new gesture reuses the exact same
        // mouse pointer object, but receives a distinct generation and intent.
        state.Invalidate();
        var newGeneration=state.Begin(pointer);
        var newRelease=new CanvasNativeEraserInput.QueuedRelease(newGeneration,pointer,
            (Guid.NewGuid(),Guid.NewGuid()),new(),newIntent,_=>Task.CompletedTask);
        Assert.True(state.Queue(newRelease));
        Assert.False(state.Queue(new CanvasNativeEraserInput.QueuedRelease(newGeneration,pointer,
            newRelease.Identity,newRelease.Options,new object(),_=>Task.CompletedTask)));
        blocked.SetResult();
        Assert.False(await delayed);
        Assert.True(state.Owns(newRelease));
        Assert.Same(newIntent,newRelease.Intent);

        // CloseAndDrain invalidates an unsubmitted queued callback before its
        // original task is joined; it cannot act on the retired release.
        state.Invalidate();
        Assert.False(state.Owns(newRelease));
    }

    private static async Task<bool> ObserveWhenReleasedAsync(Task blocker,
        CanvasNativeEraserInput.ReleaseGeneration state,CanvasNativeEraserInput.QueuedRelease release)
    { await blocker; return state.Owns(release); }

    [Fact]
    public async Task Owning_pointer_adapter_admits_inverted_pen_only_for_available_explicit_Eraser()
    {
        await using var native=HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        await native.Dispatch(async()=>
        {
            await using var fixture=await Fixture.Create(CancellationToken.None);
            var writable=true;var eraserAvailable=true;var requests=0;
            var context=new CanvasNativeEraserContext(fixture.FileId,fixture.Opened,()=>writable,
                (_,_)=>{requests++;return Task.CompletedTask;},(_,_)=>{requests++;return Task.CompletedTask;},
                (_,_)=>{requests++;return Task.CompletedTask;},(_,_)=>{requests++;return Task.CompletedTask;},
                (_,_)=>{requests++;return Task.CompletedTask;});
            using var document=await fixture.OpenDocument(CancellationToken.None);
            using var viewport=new CanvasNativeViewport(document,fixture.Readiness);
            using var tools=new CanvasToolState(tool=>tool==CanvasPrimaryTool.Eraser && !eraserAvailable
                ? CanvasToolCapability.Unavailable("Synthetic unavailable capability") : new(true,"Synthetic test capability"),_=>{});
            var input=new CanvasNativeEraserInput(viewport,document,tools,context);
            var window=new Window{Width=900,Height=700,Content=viewport};window.Show();
            try
            {
                Assert.True(await viewport.RefreshAsync(CancellationToken.None));window.UpdateLayout();
                tools.Select(CanvasPrimaryTool.Eraser);
                Assert.True(input.CanHistory(CanvasHistoryKind.Undo));
                Assert.True(input.CanBeginPointerGesture(PointerType.Pen,false,true));
                Assert.True(input.CanBeginPointerGesture(PointerType.Mouse,true,false));
                Assert.True(input.CanBeginPointerGesture(PointerType.Touch,false,false));
                Assert.Equal(CanvasPrimaryTool.Eraser,tools.Selected); // Classification never switches tools or creates ink.

                tools.Select(CanvasPrimaryTool.Pen);
                Assert.False(input.CanBeginPointerGesture(PointerType.Pen,false,true));
                tools.Select(CanvasPrimaryTool.Pan);
                Assert.False(input.CanBeginPointerGesture(PointerType.Pen,false,true));
                tools.Select(CanvasPrimaryTool.Eraser);

                var center=new Point(viewport.Bounds.Width/2,viewport.Bounds.Height/2);
                var point=viewport.TranslatePoint(center,window)!.Value;
                window.MouseDown(point,MouseButton.Left);
                Assert.False(input.CanHistory(CanvasHistoryKind.Undo));
                Assert.True(viewport.ZoomAt(1.1,center)); // View changes cancel the incomplete gesture.
                window.MouseUp(point,MouseButton.Left);
                await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);
                Assert.True(input.CanHistory(CanvasHistoryKind.Undo));
                Assert.Equal(0,requests);

                eraserAvailable=false;
                Assert.False(input.CanBeginPointerGesture(PointerType.Pen,false,true));
                eraserAvailable=true;writable=false;
                Assert.False(input.CanBeginPointerGesture(PointerType.Pen,false,true));
                writable=true;tools.Select(CanvasPrimaryTool.Eraser);
                window.MouseDown(point,MouseButton.Left);window.MouseUp(point,MouseButton.Left);
                var close=input.CloseAndDrainAsync(); // Retire before the published callback resumes.
                await close;
                await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);
                Assert.Equal(0,requests);
            }
            finally { window.Close(); await input.CloseAndDrainAsync(); await viewport.CloseAndDrainAsync(); }
        },CancellationToken.None);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("tool")]
    [InlineData("access")]
    public async Task Reentrant_review_notification_revalidates_before_issuing_the_Home_eraser_request(string change)
    {
        await using var native=HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        await native.Dispatch(async()=>
        {
            var ct=CancellationToken.None;await using var fixture=await Fixture.Create(ct);
            var writable=true;var requests=0;var notified=false;Task? close=null;
            var context=new CanvasNativeEraserContext(fixture.FileId,fixture.Opened,()=>writable,
                (_,_)=>{requests++;return Task.CompletedTask;},(_,_)=>{requests++;return Task.CompletedTask;},
                (_,_)=>{requests++;return Task.CompletedTask;},(_,_)=>{requests++;return Task.CompletedTask;});
            using var document=await fixture.OpenDocument(ct);
            using var viewport=new CanvasNativeViewport(document,fixture.Readiness);
            using var tools=new CanvasToolState(_=>new(true,"Synthetic tool capability"),_=>{},
                eraserCapability:options=>context.Capability(document,options));
            var input=new CanvasNativeEraserInput(viewport,document,tools,context);
            var window=new Window{Width=900,Height=700,Content=viewport};window.Show();
            input.Changed+=(_,_)=>
            {
                if(notified||input.Status!="Review this exact eraser change in Home.")return;
                notified=true;
                if(change=="close")close=input.CloseAndDrainAsync();
                else if(change=="tool")tools.Select(CanvasPrimaryTool.Pan);
                else writable=false;
            };
            try
            {
                Assert.True(await viewport.RefreshAsync(ct));window.UpdateLayout();tools.Select(CanvasPrimaryTool.Eraser);
                var localStart=LocalPointForDocument(viewport,new(190,100));
                var localEnd=LocalPointForDocument(viewport,new(220,100));
                var start=viewport.TranslatePoint(localStart,window)!.Value;
                var end=viewport.TranslatePoint(localEnd,window)!.Value;
                window.MouseDown(start,MouseButton.Left);window.MouseMove(end,RawInputModifiers.LeftMouseButton);window.MouseUp(end,MouseButton.Left);
                await Until(()=>notified);
                if(close is not null)await close;else await input.CloseAndDrainAsync();
                Assert.Equal(0,requests);
                Assert.Contains("retired before Home submission",input.Status);
            }
            finally{window.Close();await input.CloseAndDrainAsync();await viewport.CloseAndDrainAsync();}
        },CancellationToken.None);
    }

    [Fact]
    public async Task Accessible_chosen_stroke_requires_actual_Home_approval_and_commits_one_canonical_delete()
    {
        await using var native=HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        Assert.True(await native.Dispatch<bool>(async()=>
        {
            var ct=CancellationToken.None;await using var f=await Fixture.Create(ct);
            CanvasStrokeEditIntent? chosen=null;HomePermissionAuthorization? pending=null;
            var context=new CanvasNativeEraserContext(f.FileId,f.Opened,()=>true,
                (_,_)=>throw new InvalidOperationException("Wrong whole-stroke route"),
                (_,_)=>throw new InvalidOperationException("Wrong partial route"),
                (_,_)=>throw new InvalidOperationException("Wrong pointer Quick route"),
                async(intent,token)=>{chosen=intent;pending=await f.Broker.AuthorizeAsync("canvas",CanvasStrokeWriteIntent.ActionId,intent.Scopes,intent.Arguments,"Erase chosen stroke",null,"native-eraser-test",token);});
            using var surface=new CanvasNativeCuiSurface(f.OpenDocument,f.Readiness,eraser:context);
            using var approvals=new HomeApprovalCuiSurface(f.Runtime,f.Profiles,f.Permissions);
            var grid=new Grid{ColumnDefinitions=new ColumnDefinitions("2*,*")};Grid.SetColumn(approvals,1);grid.Children.Add(surface);grid.Children.Add(approvals);
            var window=new Window{Width=1100,Height=800,Content=grid};window.Show();
            try
            {
                await approvals.InitializeAsync(ct);await surface.InitializeAsync(ct);window.UpdateLayout();
                await Click(surface,"Eraser");await Click(surface,"Quick");await KeyboardClick(window,surface,"Next stroke");await KeyboardClick(window,surface,"Erase chosen stroke");
                await Until(()=>pending is not null);
                Assert.NotNull(chosen);Assert.Equal(f.Opened.StoreId,chosen!.StoreId);
                Assert.Equal(Assert.Single(f.Opened.Artifact.Pages[0].Strokes).StrokeId,chosen.StrokeId);
                Assert.Equal(HomePermissionRequestState.PendingApproval,pending!.State);
                Assert.Equal(f.Opened.CasRevisionId,(await f.Bridge.OpenAsync(f.FileId,f.Opened.StoreId,ct)).CasRevisionId);
                await Approve(approvals,f,pending,ct);
                var cap=Assert.IsType<HomeResourceExecutionCapability>(await f.Broker.BeginExecutionCapabilityAsync(pending.RequestId,chosen.Arguments,ct));
                var owner=new CanvasHomeStrokeEditOperation(f.Bridge,f.Broker,f.Profiles);
                var commit=await owner.ExecuteAsync(chosen,cap,ct);
                Assert.Empty(commit.Artifact.Pages[0].Strokes);
                Assert.True((await f.Broker.CompleteExecutionAsync(cap,new(HomePermissionRequestState.Succeeded,"CANVAS_NATIVE_CHOSEN_ERASE_COMMITTED","Exact chosen stroke committed.",[new("files.item",f.FileId.ToString())]),ct)).Succeeded);
                var reopened=await f.Bridge.OpenAsync(f.FileId,f.Opened.StoreId,ct);
                using var document=CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(reopened.Artifact));
                Assert.Empty(document.Snapshot.Pages[0].Strokes);
                Assert.NotEmpty(document.Snapshot.SemanticHistory!.Undo);
                Assert.False(await surface.RefreshAsync(ct));
            }
            finally{window.Close();}
            return true;
        },CancellationToken.None));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_natural_pointer_gesture_routes_exact_whole_or_partial_owner_and_has_no_write_before_approval(bool partial)
    {
        await using var native=HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        Assert.True(await native.Dispatch<bool>(async()=>
        {
            var ct=CancellationToken.None;await using var f=await Fixture.Create(ct);
            CanvasEraserIntent? whole=null;CanvasSplitEraseIntent? split=null;HomePermissionAuthorization? pending=null;
            var context=new CanvasNativeEraserContext(f.FileId,f.Opened,()=>true,
                async(intent,token)=>{whole=intent;pending=await f.Broker.AuthorizeAsync("canvas",CanvasStrokeWriteIntent.ActionId,intent.Scopes,intent.Arguments,"Erase whole strokes",null,"native-eraser-test",token);},
                async(intent,token)=>{split=intent;pending=await f.Broker.AuthorizeAsync("canvas",CanvasStrokeWriteIntent.ActionId,intent.Scopes,intent.Arguments,"Erase exact stroke segments",null,"native-eraser-test",token);},
                (_,_)=>throw new InvalidOperationException("Wrong Quick route"),(_,_)=>throw new InvalidOperationException("Wrong named-target route"));
            using var surface=new CanvasNativeCuiSurface(f.OpenDocument,f.Readiness,eraser:context);
            using var approvals=new HomeApprovalCuiSurface(f.Runtime,f.Profiles,f.Permissions);
            var grid=new Grid{ColumnDefinitions=new ColumnDefinitions("2*,*")};Grid.SetColumn(approvals,1);grid.Children.Add(surface);grid.Children.Add(approvals);
            var window=new Window{Width=1100,Height=800,Content=grid};window.Show();
            try
            {
                await approvals.InitializeAsync(ct);await surface.InitializeAsync(ct);window.UpdateLayout();
                await Click(surface,"Eraser");await Click(surface,partial ? "Partial stroke" : "Whole stroke");
                var viewport=Assert.Single(surface.GetVisualDescendants().OfType<CanvasNativeViewport>());
                var local=LocalPointForDocument(viewport,new(205,100));
                Assert.InRange(local.X,0,viewport.Bounds.Width);Assert.InRange(local.Y,0,viewport.Bounds.Height);
                var point=viewport.TranslatePoint(local,window)!.Value;
                window.MouseDown(point,MouseButton.Left);window.MouseUp(point,MouseButton.Left);
                window.MouseUp(point,MouseButton.Left); // Duplicate release event for the same pointer capture.
                await Until(()=>pending is not null);
                Assert.Equal(f.Opened.CasRevisionId,(await f.Bridge.OpenAsync(f.FileId,f.Opened.StoreId,ct)).CasRevisionId);
                Assert.Equal(partial,split is not null);Assert.Equal(!partial,whole is not null);
                await Approve(approvals,f,pending!,ct);
                var args=partial ? split!.Arguments : whole!.Arguments;
                var cap=Assert.IsType<HomeResourceExecutionCapability>(await f.Broker.BeginExecutionCapabilityAsync(pending!.RequestId,args,ct));
                if(partial)
                {
                    Assert.Equal(f.Opened.StoreId,split!.StoreId);
                    var commit=await new CanvasHomeSplitEraserOperation(f.Bridge,f.Broker,f.Profiles).ExecuteAsync(split,cap,ct);
                    Assert.Equal(2,commit.Artifact.SchemaVersion);Assert.Equal(2,commit.Artifact.Pages[0].Strokes.Count);
                    Assert.All(commit.Artifact.Pages[0].Strokes,stroke=>Assert.NotNull(stroke.PathGeometry));
                }
                else
                {
                    Assert.Equal(f.Opened.StoreId,whole!.ExpectedStoreId);
                    var commit=await new CanvasHomeEraserOperation(f.Bridge,f.Broker,f.Profiles).ExecuteAsync(whole,cap,ct);
                    Assert.Empty(commit.Artifact.Pages[0].Strokes);
                }
                Assert.True((await f.Broker.CompleteExecutionAsync(cap,new(HomePermissionRequestState.Succeeded,"CANVAS_NATIVE_NATURAL_ERASE_COMMITTED","Exact natural eraser revision committed.",[new("files.item",f.FileId.ToString())]),ct)).Succeeded);
                var reopened=await f.Bridge.OpenAsync(f.FileId,f.Opened.StoreId,ct);
                using var document=CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(reopened.Artifact));
                Assert.NotEmpty(document.Snapshot.SemanticHistory!.Undo);
            }
            finally{window.Close();}
            return true;
        },CancellationToken.None));
    }
    [Fact]
    public async Task Real_Quick_pointer_capture_commits_only_after_actual_Home_review_and_retires_old_surface()
    {
        await using var native=HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        Assert.True(await native.Dispatch<bool>(async()=>
        {
            var ct=CancellationToken.None;await using var f=await Fixture.Create(ct);
            CanvasQuickEraseIntent? captured=null;HomePermissionAuthorization? pending=null;var requestCount=0;
            var context=new CanvasNativeEraserContext(f.FileId,f.Opened,()=>true,
                (_,_)=>throw new InvalidOperationException("Wrong whole route"),
                (_,_)=>throw new InvalidOperationException("Wrong partial route"),
                async(intent,token)=>{requestCount++;captured=intent;pending=await f.Broker.AuthorizeAsync("canvas",CanvasStrokeWriteIntent.ActionId,intent.Scopes,intent.Arguments,"Erase actual Quick target",null,"native-quick-pointer-test",token);},
                (_,_)=>throw new InvalidOperationException("Wrong chosen-target route"));
            using var surface=new CanvasNativeCuiSurface(f.OpenDocument,f.Readiness,eraser:context);
            using var approvals=new HomeApprovalCuiSurface(f.Runtime,f.Profiles,f.Permissions);
            var grid=new Grid{ColumnDefinitions=new ColumnDefinitions("2*,*")};Grid.SetColumn(approvals,1);grid.Children.Add(surface);grid.Children.Add(approvals);
            var window=new Window{Width=1100,Height=800,Content=grid};window.Show();
            try
            {
                await approvals.InitializeAsync(ct);await surface.InitializeAsync(ct);window.UpdateLayout();
                await Click(surface,"Eraser");await Click(surface,"Quick");
                var viewport=Assert.Single(surface.GetVisualDescendants().OfType<CanvasNativeViewport>());
                var local=LocalPointForDocument(viewport,new(205,100));
                Assert.InRange(local.X,0,viewport.Bounds.Width);Assert.InRange(local.Y,0,viewport.Bounds.Height);
                var point=viewport.TranslatePoint(local,window)!.Value;
                window.MouseDown(point,MouseButton.Left);window.MouseUp(point,MouseButton.Left);
                window.MouseUp(point,MouseButton.Left); // Actual duplicate release, not a second gesture.
                await Until(()=>pending is not null);
                // A distinct later gesture is gated after submission; the
                // separate MouseUp above exercises duplicate release delivery.
                window.MouseDown(point,MouseButton.Left);window.MouseUp(point,MouseButton.Left);
                await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);
                Assert.Equal(1,requestCount);
                Assert.NotNull(captured);Assert.Equal(f.Opened.StoreId,captured!.StoreId);
                Assert.Equal(Assert.Single(f.Opened.Artifact.Pages[0].Strokes).StrokeId,captured.StrokeId);
                Assert.Equal(HomePermissionRequestState.PendingApproval,pending!.State);
                var untouched=await f.Bridge.OpenAsync(f.FileId,f.Opened.StoreId,ct);
                Assert.Equal(f.Opened.CasRevisionId,untouched.CasRevisionId);
                Assert.Equal(CanvasArtifactCodec.Serialize(f.Opened.Artifact),CanvasArtifactCodec.Serialize(untouched.Artifact));
                await Approve(approvals,f,pending,ct);
                var cap=Assert.IsType<HomeResourceExecutionCapability>(await f.Broker.BeginExecutionCapabilityAsync(pending.RequestId,captured.Arguments,ct));
                var commit=await new CanvasHomeQuickEraserOperation(f.Bridge,f.Broker,f.Profiles).ExecuteAsync(captured,cap,ct);
                Assert.Empty(commit.Artifact.Pages[0].Strokes);
                Assert.True((await f.Broker.CompleteExecutionAsync(cap,new(HomePermissionRequestState.Succeeded,"CANVAS_NATIVE_QUICK_COMMITTED","Actual Quick target committed.",[new("files.item",f.FileId.ToString())]),ct)).Succeeded);
                var reopened=await f.Bridge.OpenAsync(f.FileId,f.Opened.StoreId,ct);
                using var document=CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(reopened.Artifact));
                Assert.Empty(document.Snapshot.Pages[0].Strokes);
                Assert.NotEmpty(document.Snapshot.SemanticHistory!.Undo);
                Assert.False(await surface.RefreshAsync(ct));
            }
            finally{window.Close();}
            return true;
        },CancellationToken.None));
    }
    [Fact]
    public async Task View_change_cancels_real_pointer_capture_and_stale_Files_revision_retires_surface_without_request()
    {
        await using var native=HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        Assert.True(await native.Dispatch<bool>(async()=>
        {
            var ct=CancellationToken.None;await using var f=await Fixture.Create(ct);var requests=0;
            var context=new CanvasNativeEraserContext(f.FileId,f.Opened,()=>true,
                (_,_)=>{requests++;return Task.CompletedTask;},(_,_)=>{requests++;return Task.CompletedTask;},
                (_,_)=>{requests++;return Task.CompletedTask;},(_,_)=>{requests++;return Task.CompletedTask;});
            using var surface=new CanvasNativeCuiSurface(f.OpenDocument,f.Readiness,eraser:context);
            var window=new Window{Width=1100,Height=800,Content=surface};window.Show();
            try
            {
                await surface.InitializeAsync(ct);window.UpdateLayout();await Click(surface,"Eraser");
                var viewport=Assert.Single(surface.GetVisualDescendants().OfType<CanvasNativeViewport>());
                var local=new Point(viewport.Bounds.Width/2,viewport.Bounds.Height/2);var point=viewport.TranslatePoint(local,window)!.Value;
                window.MouseDown(point,MouseButton.Left);Assert.True(viewport.ZoomAt(1.2,local));window.MouseUp(point,MouseButton.Left);
                await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);Assert.Equal(0,requests);
                IPointer? capturedPointer=null;
                viewport.PointerPressed+=(_,args)=>capturedPointer=args.Pointer;
                window.MouseDown(point,MouseButton.Left);
                Assert.NotNull(capturedPointer);
                capturedPointer!.Capture(null); // Deliver an actual synthetic capture-lost transition.
                window.MouseUp(point,MouseButton.Left);
                await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);
                Assert.Equal(0,requests);
                Assert.Equal(f.Opened.CasRevisionId,(await f.Bridge.OpenAsync(f.FileId,f.Opened.StoreId,ct)).CasRevisionId);
                var now=DateTimeOffset.UtcNow;
                Assert.True((await f.Workspace.Provider.MutateAsync(new(new(Guid.NewGuid()),f.Workspace.Actor.ActorId,
                    f.FileId,f.FolderId,null,"Rename",f.Opened.CasRevisionId,null,FilesOperationState.Pending,now,now,null,null),"Stale eraser.9to1c",ct)).IsSuccess);
                Assert.False(await surface.RefreshAsync(ct));Assert.Null(surface.Content);Assert.Equal(0,requests);
            }
            finally{window.Close();}
            return true;
        },CancellationToken.None));
    }
    [Fact]
    public async Task History_controls_request_fresh_exact_Home_operations_and_reopen_actual_donor_Undo_Redo()
    {
        await using var native=HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        Assert.True(await native.Dispatch<bool>(async()=>
        {
            var ct=CancellationToken.None;await using var f=await Fixture.Create(ct);
            using var approvals=new HomeApprovalCuiSurface(f.Runtime,f.Profiles,f.Permissions);
            var grid=new Grid{ColumnDefinitions=new ColumnDefinitions("2*,*")};Grid.SetColumn(approvals,1);grid.Children.Add(approvals);
            var window=new Window{Width=1100,Height=800,Content=grid};window.Show();
            try
            {
                await approvals.InitializeAsync(ct);
                foreach(var kind in new[]{CanvasHistoryKind.Undo,CanvasHistoryKind.Redo})
                {
                    var original=await f.Bridge.OpenAsync(f.FileId,f.Workspace.Configuration.StoreId,ct);
                    CanvasHistoryIntent? captured=null;HomePermissionAuthorization? pending=null;
                    var context=new CanvasNativeEraserContext(f.FileId,original,()=>true,
                        (_,_)=>throw new InvalidOperationException("Unexpected eraser request"),(_,_)=>throw new InvalidOperationException("Unexpected eraser request"),
                        (_,_)=>throw new InvalidOperationException("Unexpected eraser request"),(_,_)=>throw new InvalidOperationException("Unexpected deletion request"),
                        async(intent,token)=>{captured=intent;pending=await f.Broker.AuthorizeAsync("canvas",CanvasStrokeWriteIntent.ActionId,intent.Scopes,intent.Arguments,"Restore exact Canvas history",null,"native-eraser-history-test",token);});
                    using var surface=new CanvasNativeCuiSurface(async token=>
                    {
                        var current=await f.Bridge.OpenAsync(f.FileId,original.StoreId,token);
                        if(current.CasRevisionId!=original.CasRevisionId)throw new InvalidOperationException("The displayed history revision changed.");
                        return CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(current.Artifact));
                    },f.ReadinessFor(original.CasRevisionId),eraser:context);
                    grid.Children.Add(surface);await surface.InitializeAsync(ct);window.UpdateLayout();
                    try
                    {
                        await Click(surface,kind.ToString());await Until(()=>pending is not null);
                        Assert.Equal(kind,captured!.Kind);Assert.Equal(original.StoreId,captured.ExpectedStoreId);
                        Assert.Equal(original.CasRevisionId,(await f.Bridge.OpenAsync(f.FileId,original.StoreId,ct)).CasRevisionId);
                        await Approve(approvals,f,pending!,ct);
                        var cap=Assert.IsType<HomeResourceExecutionCapability>(await f.Broker.BeginExecutionCapabilityAsync(pending!.RequestId,captured.Arguments,ct));
                        var commit=await new CanvasHomeHistoryOperation(f.Bridge,f.Broker,f.Profiles).ExecuteAsync(captured,cap,ct);
                        Assert.Equal(kind==CanvasHistoryKind.Undo ? 0 : 1,commit.Artifact.Pages[0].Strokes.Count);
                        Assert.True((await f.Broker.CompleteExecutionAsync(cap,new(HomePermissionRequestState.Succeeded,"CANVAS_NATIVE_HISTORY_COMMITTED","Exact history revision committed.",[new("files.item",f.FileId.ToString())]),ct)).Succeeded);
                        var reopened=await f.Bridge.OpenAsync(f.FileId,original.StoreId,ct);
                        using var document=CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(reopened.Artifact));
                        Assert.Equal(kind==CanvasHistoryKind.Undo ? 0 : 1,document.Snapshot.Pages[0].Strokes.Count);
                        Assert.False(await surface.RefreshAsync(ct));
                    }
                    finally{grid.Children.Remove(surface);}
                }
            }
            finally{window.Close();}
            return true;
        },CancellationToken.None));
    }
    [Fact]
    public async Task Original_store_draw_successor_denies_actual_foreign_store_substitution_before_adoption_or_write()
    {
        await using var native=HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        Assert.True(await native.Dispatch<bool>(async()=>
        {
            var ct=CancellationToken.None;await using var f=await Fixture.Create(ct);
            var opened=f.Opened;
            Assert.Throws<ArgumentException>(()=>CanvasStrokeWriteIntent.Capture(f.FileId,opened.CasRevisionId,opened.Artifact.ArtifactId,
                opened.Artifact.RevisionId,Guid.NewGuid(),Guid.Empty,[new(10,10,.5),new(20,20,.5)]));
            var intent=CanvasStrokeWriteIntent.Capture(f.FileId,opened.CasRevisionId,opened.Artifact.ArtifactId,
                opened.Artifact.RevisionId,Guid.NewGuid(),opened.StoreId,[new(10,10,.5),new(20,20,.5)]);
            var pending=await f.Broker.AuthorizeAsync("canvas",CanvasStrokeWriteIntent.ActionId,intent.Scopes,intent.Arguments,"Draw exact stroke",null,"native-original-store-test",ct);
            using var approvals=new HomeApprovalCuiSurface(f.Runtime,f.Profiles,f.Permissions);
            var window=new Window{Width=600,Height=700,Content=approvals};window.Show();
            try
            {
                await approvals.InitializeAsync(ct);await Approve(approvals,f,pending,ct);
                var cap=Assert.IsType<HomeResourceExecutionCapability>(await f.Broker.BeginExecutionCapabilityAsync(pending.RequestId,intent.Arguments,ct));
                var statePath=Path.Combine(f.Workspace.Configuration.RootDirectory,".9to1-files","drive.json");
                var envelope=System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllBytesAsync(statePath,ct))!.AsObject();
                envelope["state"]!.AsObject()["storeId"]=Guid.NewGuid().ToString("D");
                envelope["state"]!.AsObject()["foreignOpaqueMarker"]="preserve exact substituted bytes";
                var foreign=System.Text.Encoding.UTF8.GetBytes(envelope.ToJsonString());await File.WriteAllBytesAsync(statePath,foreign,ct);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Owner.ExecuteAsync(intent,cap,ct));
                Assert.Equal(foreign,await File.ReadAllBytesAsync(statePath,ct));
                Assert.True((await f.Broker.AbortUnclaimedExecutionAsync(cap,ct)).Succeeded);
                Assert.Equal(foreign,await File.ReadAllBytesAsync(statePath,ct));
            }
            finally{window.Close();}
            return true;
        },CancellationToken.None));
    }
    private static async Task KeyboardClick(Window window,Control surface,string text)
    {
        surface.UpdateLayout();var button=Assert.Single(surface.GetVisualDescendants().OfType<Button>(),value=>Equals(value.Content,text));
        Assert.True(button.IsEnabled);Assert.True(button.Focus());
        window.KeyPress(Key.Space,RawInputModifiers.None,PhysicalKey.Space," ");
        window.KeyRelease(Key.Space,RawInputModifiers.None,PhysicalKey.Space," ");
        await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);
    }
    private static async Task Click(Control surface,string text)
    {
        surface.UpdateLayout();var button=Assert.Single(surface.GetVisualDescendants().OfType<Button>(),value=>Equals(value.Content,text)||value.Content?.ToString()?.EndsWith(" "+text,StringComparison.Ordinal)==true);
        Assert.True(button.IsEnabled);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);
    }
    private static async Task Until(Func<bool> condition){for(var i=0;i<300&&!condition();i++)await Task.Delay(10);Assert.True(condition());}
    private static async Task Approve(HomeApprovalCuiSurface approvals,Fixture f,HomePermissionAuthorization pending,CancellationToken ct)
    {
        await approvals.FocusRequestAsync(pending.RequestId,ct);
        await Until(()=>approvals.GetVisualDescendants().OfType<Button>().Any(button=>Equals(button.Content,"Accept once")&&button.IsEnabled));
        await Click(approvals,"Accept once");
        for(var i=0;i<300;i++){if((await f.Permissions.GetSnapshotAsync(cancellationToken:ct)).PendingRequests.Count==0)return;await Task.Delay(10);}
        Assert.Empty((await f.Permissions.GetSnapshotAsync(cancellationToken:ct)).PendingRequests);
    }
    private static Point LocalPointForDocument(CanvasNativeViewport viewport,Point documentPoint)
    {
        // The real viewport maps the fitted raster, letterbox, zoom and pan to donor document coordinates.
        // ViewZoom alone is not the full document-to-local scale.
        var center=new Point(viewport.Bounds.Width/2,viewport.Bounds.Height/2);
        var origin=viewport.ToDocumentPoint(center)!.Value;
        var dx=viewport.ToDocumentPoint(center+new Vector(1,0))!.Value.X-origin.X;
        var dy=viewport.ToDocumentPoint(center+new Vector(0,1))!.Value.Y-origin.Y;
        Assert.True(double.IsFinite(dx)&&dx>0);Assert.True(double.IsFinite(dy)&&dy>0);
        var local=center+new Vector((documentPoint.X-origin.X)/dx,(documentPoint.Y-origin.Y)/dy);
        var mapped=viewport.ToDocumentPoint(local);Assert.NotNull(mapped);
        Assert.Equal(documentPoint.X,mapped.Value.X,precision:6);Assert.Equal(documentPoint.Y,mapped.Value.Y,precision:6);
        return local;
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "canvas-native-eraser-" + Guid.NewGuid().ToString("N"));
        public HomeLocalProfileIdentity Profiles { get; private set; } = null!;
        public HomePermissionTrustService Permissions { get; private set; } = null!;
        public HomeCoreRuntime Runtime { get; private set; } = null!;
        public HomeResourceOperationBroker Broker { get; private set; } = null!;
        public NativeFilesWorkspace Workspace { get; private set; } = null!;
        public CanvasFilesArtifactBridge Bridge { get; private set; } = null!;
        public CanvasHomeStrokeOperation Owner { get; private set; } = null!;
        public CanvasFilesOpenResult Opened { get; private set; } = null!;
        public HostedItemId FileId { get; private set; }
        public HostedItemId FolderId { get; private set; }
        private ResourceAuthorizationService _resources = null!;
        public ICuiSceneReadiness Readiness => ReadinessFor(Opened.CasRevisionId);
        public ICuiSceneReadiness ReadinessFor(FilesRevisionId revision) => new CanvasOriginalResourceReadiness(Runtime, Profiles, _resources,
            "canvas.file.open", _ => ValueTask.FromResult<IReadOnlyList<ResourceScope>>([new("files.item", FileId.ToString(), revision.ToString(), ResourceAccess.Read)]));
        public async Task<CanvasRnoteDocument> OpenDocument(CancellationToken ct)
        {
            var current = await Bridge.OpenAsync(FileId,Workspace.Configuration.StoreId, ct);
            if (current.CasRevisionId != Opened.CasRevisionId) throw new InvalidOperationException("Captured Canvas changed.");
            return CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(current.Artifact));
        }
        public static async Task<Fixture> Create(CancellationToken ct)
        {
            var f = new Fixture(); Directory.CreateDirectory(f._root);
            var home = new FileHomeCoreStateStore(Path.Combine(f._root, "home.json"));
            f.Profiles = new(home, new OperatingSystemPrincipalSource());
            var files = new NativeFilesWorkspaceService(home, f.Profiles);
            f.Permissions = new(home, new CanvasNativeActionPolicies().TryGet);
            var ownership = new HomeLocalStoreOwnership(home, f.Profiles, new HomeLocalStoreEvidenceRegistry([files]), f.Permissions);
            var authority = new NativeFilesWorkspaceAuthority(files, f.Profiles, new HomeResourceStoreOwnershipAuthority(ownership, f.Profiles));
            var directory = Path.Combine(f._root, "selected-empty-workspace"); Directory.CreateDirectory(directory);
            await files.ConfigureNewAsync(directory, ownership, ct);
            f.Workspace = (await authority.GetCurrentAsync(ct))!;
            f.FolderId = f.Workspace.Configuration.AppFolders["canvas"];
            f._resources = new(f.Profiles, [new FilesArtifactResourceResolver(async (actor, token) =>
            { var current = await authority.GetCurrentAsync(token); return current?.Actor == actor ? current.Provider : null; },
            async (actor, app, token) =>
            { var current = await authority.GetCurrentAsync(token); return current?.Actor == actor && current.Configuration.AppFolders.TryGetValue(app, out var folder) ? folder : null; })]);
            f.Broker = new(f._resources, f.Permissions);
            f.Runtime = new([new HomeCoreStateService(home), new HomePermissionsCoreService(f.Permissions, f.Profiles)]);
            await f.Runtime.StartAsync(ct);
            f.Bridge = new(f.Profiles, actor => actor == f.Workspace.Actor ? f.Workspace.Provider : null,
                f.Workspace.Directories, f._resources, () => true,
                (actor, provider, token) => authority.CaptureCommitAuthorityAsync(actor, provider, () => true, token));
            using var blank = CanvasRnoteDocument.Create("Native eraser canvas");
            blank.DrawStroke(Enumerable.Range(0,36).Select(index=>new RnotePointerSample(100+index*6,100,.2+index*.01)).ToArray(),blank.Identity.RevisionId);
            f.FileId = (await f.Bridge.CreateAsync(blank.Snapshot, ct)).FileId;
            f.Opened = await f.Bridge.OpenAsync(f.FileId,f.Workspace.Configuration.StoreId, ct);
            f.Owner = new(f.Bridge, f.Broker, f.Profiles); return f;
        }
        public async ValueTask DisposeAsync() { await Runtime.DisposeAsync(); Directory.Delete(_root, true); }
    }
}
