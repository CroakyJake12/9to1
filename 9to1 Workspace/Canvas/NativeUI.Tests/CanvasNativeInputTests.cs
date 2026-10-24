using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
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
public sealed class CanvasNativeInputTests
{
    [Theory]
    [InlineData(PointerType.Touch, true, false, false, true)]
    [InlineData(PointerType.Touch, false, false, false, false)]
    [InlineData(PointerType.Pen, true, false, false, false)]
    [InlineData(PointerType.Mouse, false, false, true, true)]
    [InlineData(PointerType.Mouse, false, false, false, false)]
    public void Primary_pan_gesture_admits_touch_navigation_without_misclassifying_pen(
        PointerType type, bool panTool, bool primary, bool middle, bool expected)
        => Assert.Equal(expected, CanvasNativeViewport.ShouldBeginPan(type, panTool, primary, middle));

    [Fact]
    public void Pan_capture_loss_only_releases_the_pointer_that_owns_the_gesture()
    {
        var captured=new object();
        Assert.True(CanvasNativeViewport.IsCurrentPanPointer(captured,captured));
        Assert.False(CanvasNativeViewport.IsCurrentPanPointer(captured,new object()));
        Assert.False(CanvasNativeViewport.IsCurrentPanPointer(null,captured));
    }

    [Fact]
    public void Pen_release_adapter_claims_only_the_captured_pointer_once_and_latches_new_strokes()
    {
        var pointer=new object();
        Assert.True(CanvasNativeInkInput.CanQueueRelease(pointer,pointer,false,false,false));
        Assert.False(CanvasNativeInkInput.CanQueueRelease(null,pointer,false,false,false));
        Assert.False(CanvasNativeInkInput.CanQueueRelease(pointer,new object(),false,false,false));
        Assert.False(CanvasNativeInkInput.CanQueueRelease(pointer,pointer,true,false,false));
        Assert.False(CanvasNativeInkInput.CanQueueRelease(pointer,pointer,false,true,false));
        Assert.False(CanvasNativeInkInput.CanQueueRelease(pointer,pointer,false,false,true));
        Assert.True(CanvasNativeInkInput.IsRelevantCaptureLoss(pointer,pointer,false));
        Assert.False(CanvasNativeInkInput.IsRelevantCaptureLoss(pointer,new object(),false));
        Assert.False(CanvasNativeInkInput.IsRelevantCaptureLoss(null,pointer,false));
        Assert.False(CanvasNativeInkInput.IsRelevantCaptureLoss(pointer,pointer,true));
        Assert.False(CanvasNativeInkInput.CanBeginPointerGesture(true,false,true));
        Assert.False(CanvasNativeInkInput.CanBeginPointerGesture(false,false,false));
        Assert.False(CanvasNativeInkInput.CanBeginPointerGesture(true,true,false));
        Assert.True(CanvasNativeInkInput.CanBeginPointerGesture(true,false,false));
    }

    [Theory]
    [InlineData(PointerType.Touch,false,false,true)]
    [InlineData(PointerType.Touch,true,false,true)]
    [InlineData(PointerType.Touch,false,true,false)]
    [InlineData(PointerType.Pen,true,false,true)]
    [InlineData(PointerType.Pen,false,false,false)]
    [InlineData(PointerType.Pen,true,true,false)]
    [InlineData(PointerType.Mouse,true,false,true)]
    [InlineData(PointerType.Mouse,false,false,false)]
    public void Active_pen_tool_distinguishes_touch_ink_from_unpressed_mouse_and_eraser_end(
        PointerType type,bool primaryPressed,bool eraserEnd,bool expected)
        => Assert.Equal(expected,CanvasNativeInkInput.BeginsInkGesture(type,primaryPressed,eraserEnd));

    [Fact]
    public async Task Delayed_ink_release_cannot_consume_same_pointer_repress_or_survive_retirement()
    {
        var pointer=new object();
        CanvasStrokeWriteIntent Intent()=>CanvasStrokeWriteIntent.Capture(HostedItemId.New(),new(Guid.NewGuid()),
            Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),[new(1,1,.5),new(2,2,.5)]);
        var oldIntent=Intent();var newIntent=Intent();
        long generation=4;
        var oldRelease=new CanvasNativeInkInput.QueuedRelease(generation,pointer,oldIntent);
        CanvasNativeInkInput.QueuedRelease? active=oldRelease;
        var blocked=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed=ObserveWhenReleasedAsync(blocked.Task,()=>CanvasNativeInkInput.OwnsRelease(generation,active,oldRelease.Generation,oldRelease));

        generation++;
        var newRelease=new CanvasNativeInkInput.QueuedRelease(generation,pointer,newIntent);
        active=newRelease; // Same pointer object, distinct captured intent.
        blocked.SetResult();
        Assert.False(await delayed);
        Assert.Same(newIntent,newRelease.Intent);
        Assert.True(CanvasNativeInkInput.OwnsRelease(generation,active,newRelease.Generation,newRelease));

        generation++;active=null; // Retirement invalidates before original-work drain.
        Assert.False(CanvasNativeInkInput.OwnsRelease(generation,active,newRelease.Generation,newRelease));
    }

    private static async Task<bool> ObserveWhenReleasedAsync(Task blocker,Func<bool> ownsRelease)
    { await blocker; return ownsRelease(); }

    [Fact]
    public async Task Closing_drains_a_queued_ink_release_without_invoking_the_Home_request()
    {
        await using var native=HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        await native.Dispatch<bool>(async()=>
        {
            var ct=CancellationToken.None;await using var fixture=await Fixture.Create(ct);var requests=0;
            using var surface=new CanvasNativeCuiSurface(token=>fixture.OpenDocument(token),fixture.Readiness,
                new(fixture.FileId,fixture.Opened.CasRevisionId,fixture.Opened.Artifact.ArtifactId,fixture.Opened.Artifact.RevisionId,
                    ()=>true,(_,_)=>{requests++;return Task.CompletedTask;},fixture.Opened.StoreId));
            var window=new Window{Width=1000,Height=800,Content=surface};window.Show();
            try
            {
                await surface.InitializeAsync(ct);window.UpdateLayout();
                var viewport=Assert.Single(surface.GetVisualDescendants().OfType<CanvasNativeViewport>());
                var start=viewport.TranslatePoint(new Point(viewport.Bounds.Width/2,viewport.Bounds.Height/2),window)!.Value;
                window.MouseDown(start,MouseButton.Left);
                window.MouseMove(start+new Vector(15,15),RawInputModifiers.LeftMouseButton);
                window.MouseUp(start+new Vector(15,15),MouseButton.Left);
                await surface.CloseAndDrainAsync();
                await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);
                Assert.Equal(0,requests);
            }
            finally{window.Close();}
            return true;
        },CancellationToken.None);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("tool")]
    [InlineData("access")]
    public async Task Review_notification_revalidates_before_issuing_a_Home_ink_request(string change)
    {
        await using var native=HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        await native.Dispatch<bool>(async()=>
        {
            var ct=CancellationToken.None;await using var fixture=await Fixture.Create(ct);
            var writable=true;var requests=0;var reviewNotified=false;
            using var document=await fixture.OpenDocument(ct);
            using var viewport=new CanvasNativeViewport(document,fixture.Readiness);
            using var tools=new CanvasToolState(_=>new(true,"Synthetic available tool"),_=>{});
            var input=new CanvasNativeInkInput(viewport,document,tools,fixture.FileId,fixture.Opened.CasRevisionId,
                ()=>writable,(_,_)=>{requests++;return Task.CompletedTask;},fixture.Opened.StoreId);
            var window=new Window{Width=1000,Height=800,Content=viewport};window.Show();
            Task? close=null;
            input.Changed+=(_,_)=>
            {
                if(reviewNotified || input.Status!="Review this captured stroke in Home.")return;
                reviewNotified=true;
                if(change=="close")close=input.CloseAndDrainAsync();
                else if(change=="tool")tools.Select(CanvasPrimaryTool.Pan);
                else writable=false;
            };
            try
            {
                Assert.True(await viewport.RefreshAsync(ct));window.UpdateLayout();tools.Select(CanvasPrimaryTool.Pen);
                var start=viewport.TranslatePoint(new Point(viewport.Bounds.Width/2,viewport.Bounds.Height/2),window)!.Value;
                window.MouseDown(start,MouseButton.Left);
                window.MouseMove(start+new Vector(15,15),RawInputModifiers.LeftMouseButton);
                window.MouseUp(start+new Vector(15,15),MouseButton.Left);
                await Until(()=>reviewNotified);
                if(close is not null)await close;
                else
                {
                    await Until(()=>input.Status=="The captured stroke was retired before Home submission.");
                    await input.CloseAndDrainAsync();
                }
                Assert.Equal(0,requests);
            }
            finally{window.Close();await input.CloseAndDrainAsync();await viewport.CloseAndDrainAsync();}
            return true;
        },CancellationToken.None);
    }

    [Fact]
    public async Task Close_drains_the_exact_ink_request_task_after_it_was_invoked()
    {
        await using var native=HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        await native.Dispatch<bool>(async()=>
        {
            var ct=CancellationToken.None;await using var fixture=await Fixture.Create(ct);
            var requestStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finishRequest=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task issued=Task.CompletedTask;var requests=0;
            using var document=await fixture.OpenDocument(ct);
            using var viewport=new CanvasNativeViewport(document,fixture.Readiness);
            using var tools=new CanvasToolState(_=>new(true,"Synthetic available tool"),_=>{});
            var input=new CanvasNativeInkInput(viewport,document,tools,fixture.FileId,fixture.Opened.CasRevisionId,
                ()=>true,(_,_)=>{requests++;issued=finishRequest.Task;requestStarted.SetResult();return issued;},fixture.Opened.StoreId);
            var window=new Window{Width=1000,Height=800,Content=viewport};window.Show();
            try
            {
                Assert.True(await viewport.RefreshAsync(ct));window.UpdateLayout();tools.Select(CanvasPrimaryTool.Pen);
                var start=viewport.TranslatePoint(new Point(viewport.Bounds.Width/2,viewport.Bounds.Height/2),window)!.Value;
                window.MouseDown(start,MouseButton.Left);
                window.MouseMove(start+new Vector(15,15),RawInputModifiers.LeftMouseButton);
                window.MouseUp(start+new Vector(15,15),MouseButton.Left);
                await requestStarted.Task;
                var exact=issued;
                var close=input.CloseAndDrainAsync();
                Assert.False(close.IsCompleted);
                finishRequest.SetResult();
                await close;
                Assert.Same(finishRequest.Task,exact);Assert.Equal(1,requests);
            }
            finally{window.Close();await input.CloseAndDrainAsync();await viewport.CloseAndDrainAsync();}
            return true;
        },CancellationToken.None);
    }

    [Fact]
    public async Task Native_view_navigation_preserves_document_coordinates_and_cancels_unsubmitted_ink()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        Assert.True(await native.Dispatch<bool>(async () =>
        {
            var ct = CancellationToken.None;
            await using var fixture = await Fixture.Create(ct);
            var submissions = 0;
            using var surface = new CanvasNativeCuiSurface(token => fixture.OpenDocument(token), fixture.Readiness,
                new(fixture.FileId, fixture.Opened.CasRevisionId, fixture.Opened.Artifact.ArtifactId, fixture.Opened.Artifact.RevisionId,
                    () => true, (_, _) => { submissions++; return Task.CompletedTask; },fixture.Opened.StoreId));
            var window = new Window { Width = 1000, Height = 800, Content = surface }; window.Show();
            try
            {
                await surface.InitializeAsync(ct); window.UpdateLayout();
                var viewport = Assert.Single(surface.GetVisualDescendants().OfType<CanvasNativeViewport>());
                var center = new Point(viewport.Bounds.Width / 2, viewport.Bounds.Height / 2);
                var original = viewport.ToDocumentPoint(center)!.Value;
                var nativeCenter = viewport.TranslatePoint(center, window)!.Value;
                window.MouseWheel(nativeCenter, new Vector(0, 1));
                Assert.Equal(1.2, viewport.ViewZoom, 8);
                var anchored = viewport.ToDocumentPoint(center)!.Value;
                Assert.Equal(original.X, anchored.X, 8); Assert.Equal(original.Y, anchored.Y, 8);
                window.MouseDown(nativeCenter, MouseButton.Middle);
                window.MouseMove(nativeCenter + new Vector(20, 10), RawInputModifiers.MiddleMouseButton);
                window.MouseUp(nativeCenter + new Vector(20, 10), MouseButton.Middle);
                var moved = viewport.ToDocumentPoint(center + new Vector(20, 10))!.Value;
                Assert.Equal(original.X, moved.X, 8); Assert.Equal(original.Y, moved.Y, 8);
                Assert.Null(viewport.ToDocumentPoint(new Point(-1, 10)));
                Assert.False(viewport.ZoomAt(double.NaN, center));
                Assert.False(viewport.PanBy(new Vector(double.PositiveInfinity, 0)));
                var nativeStart = viewport.TranslatePoint(center, window)!.Value;
                window.MouseDown(nativeStart, MouseButton.Left);
                window.MouseMove(nativeStart + new Vector(10, 5), RawInputModifiers.LeftMouseButton);
                viewport.ResetView(); // Inverse coordinates changed mid-gesture: never submit a distorted stroke.
                window.MouseUp(nativeStart + new Vector(10, 5), MouseButton.Left);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.Equal(0, submissions);
                var panTool = Assert.Single(surface.GetVisualDescendants().OfType<Button>(),
                    value => value.Content?.ToString()?.EndsWith(" Pan", StringComparison.Ordinal) == true);
                Assert.True(panTool.IsEnabled);
                panTool.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.True(viewport.PanWithPrimaryButton);
                window.MouseDown(nativeStart, MouseButton.Left);
                window.MouseMove(nativeStart + new Vector(12, 8), RawInputModifiers.LeftMouseButton);
                window.MouseUp(nativeStart + new Vector(12, 8), MouseButton.Left);
                Assert.Equal(new Vector(12, 8), viewport.ViewPan);
                Assert.Equal(0, submissions);
                var editorState = Assert.IsType<CanvasNativeEditorState>(surface.CaptureEditorState());
                Assert.Equal(CanvasPrimaryTool.Pan, editorState.Tool);
                Assert.Equal(new Vector(12, 8), editorState.Pan);
                Button(surface, "Fit").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.Equal(1, viewport.ViewZoom);
                Assert.Equal(original, viewport.ToDocumentPoint(center));
                Assert.Equal(fixture.Opened.CasRevisionId, (await fixture.Bridge.OpenAsync(fixture.FileId, ct)).CasRevisionId);
                Assert.Empty((await fixture.Bridge.OpenAsync(fixture.FileId, ct)).Artifact.Pages[0].Strokes);

                await surface.CloseAndDrainAsync();
                var reopened = new CanvasNativeCuiSurface(token => fixture.OpenDocument(token), fixture.Readiness,
                    new(fixture.FileId, fixture.Opened.CasRevisionId, fixture.Opened.Artifact.ArtifactId, fixture.Opened.Artifact.RevisionId,
                        () => true, (_, _) => Task.CompletedTask, fixture.Opened.StoreId), restoreState: editorState);
                window.Content = reopened;
                try
                {
                    await reopened.InitializeAsync(ct); window.UpdateLayout();
                    var restoredViewport = Assert.Single(reopened.GetVisualDescendants().OfType<CanvasNativeViewport>());
                    Assert.Equal(CanvasPrimaryTool.Pan, reopened.CaptureEditorState()!.Tool);
                    Assert.Equal(1, restoredViewport.ViewZoom, 8);
                    Assert.Equal(new Vector(12, 8), restoredViewport.ViewPan);
                }
                finally { await reopened.CloseAndDrainAsync(); }
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Routed_native_mouse_stroke_requires_real_Home_approval_and_exact_Files_revision(bool changeRevision)
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        Assert.True(await native.Dispatch<bool>(async () =>
        {
            var ct = CancellationToken.None;
            await using var fixture = await Fixture.Create(ct);
            CanvasStrokeWriteIntent? captured = null;
            HomePermissionAuthorization? pending = null;
            var requestCount = 0;
            using var surface = new CanvasNativeCuiSurface(token => fixture.OpenDocument(token), fixture.Readiness,
                new(fixture.FileId, fixture.Opened.CasRevisionId, fixture.Opened.Artifact.ArtifactId, fixture.Opened.Artifact.RevisionId,
                    () => true, async (intent, token) =>
                    {
                        requestCount++;
                        captured = intent;
                        pending = await fixture.Broker.AuthorizeAsync(CanvasStrokeWriteIntent.TargetAppId,
                            CanvasStrokeWriteIntent.ActionId, intent.Scopes, intent.Arguments,
                            "Draw the exact captured red stroke in this Canvas revision", null, "native-canvas-test-session", token);
                    },fixture.Opened.StoreId));
            using var approvals = new HomeApprovalCuiSurface(fixture.Runtime, fixture.Profiles, fixture.Permissions);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*") };
            Grid.SetColumn(approvals, 1); grid.Children.Add(surface); grid.Children.Add(approvals);
            var window = new Window { Width = 1100, Height = 800, Content = grid };
            window.Show();
            try
            {
                await approvals.InitializeAsync(ct); await surface.InitializeAsync(ct); window.UpdateLayout();
                Button(surface, "Red").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                var viewport = Assert.Single(surface.GetVisualDescendants().OfType<CanvasNativeViewport>());
                var start = viewport.TranslatePoint(new Point(viewport.Bounds.Width / 2, viewport.Bounds.Height / 2), window)!.Value;
                var end = start + new Vector(15, 15);
                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(end, Avalonia.Input.RawInputModifiers.LeftMouseButton);
                window.MouseUp(end, MouseButton.Left);
                window.MouseUp(end, MouseButton.Left); // Duplicate release on the same captured stroke.
                await Until(() => pending is not null);
                window.MouseDown(start, MouseButton.Left); window.MouseUp(start, MouseButton.Left);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.Equal(1, requestCount);
                Assert.NotNull(captured);
                Assert.Equal(HomePermissionRequestState.PendingApproval, pending!.State);
                Assert.Equal("#FFFF0000", captured!.Style.Color);
                Assert.Equal(fixture.Opened.StoreId,captured.ExpectedStoreId);
                Assert.Equal(fixture.Opened.CasRevisionId, (await fixture.Bridge.OpenAsync(fixture.FileId, ct)).CasRevisionId);
                Assert.Empty((await fixture.Bridge.OpenAsync(fixture.FileId, ct)).Artifact.Pages[0].Strokes);
                await approvals.FocusRequestAsync(pending.RequestId, ct);
                await Until(() => Buttons(approvals).Any(button => Equals(button.Content, "Accept once") && button.IsEnabled));
                Button(approvals, "Accept once").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await UntilAsync(async () => (await fixture.Permissions.GetSnapshotAsync(cancellationToken: ct)).PendingRequests.Count == 0);
                var capability = Assert.IsType<HomeResourceExecutionCapability>(await fixture.Broker.BeginExecutionCapabilityAsync(pending.RequestId, captured.Arguments, ct));
                if (changeRevision)
                {
                    var now = DateTimeOffset.UtcNow;
                    Assert.True((await fixture.Workspace.Provider.MutateAsync(new(new(Guid.NewGuid()), fixture.Workspace.Actor.ActorId,
                        fixture.FileId, fixture.FolderId, null, "Rename", fixture.Opened.CasRevisionId, null,
                        FilesOperationState.Pending, now, now, null, null), "Changed while approval was pending.9to1c", ct)).IsSuccess);
                    await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Owner.ExecuteAsync(captured, capability, ct));
                    var aborted = await fixture.Broker.AbortUnclaimedExecutionAsync(capability, ct);
                    Assert.True(aborted.Succeeded);
                    Assert.Equal(HomePermissionRequestState.Failed, (await fixture.Permissions.GetAuthorizationAsync(pending.RequestId, ct)).State);
                    Assert.Null(await fixture.Broker.ClaimExecutionAsync(capability, CanvasStrokeWriteIntent.TargetAppId,
                        CanvasStrokeWriteIntent.ActionId, captured.Scopes, captured.Arguments, ct));
                    Assert.Empty((await fixture.Bridge.OpenAsync(fixture.FileId, ct)).Artifact.Pages[0].Strokes);
                    Assert.False(await surface.RefreshAsync(ct));
                }
                else
                {
                    var committed = await fixture.Owner.ExecuteAsync(captured, capability, ct);
                    var stroke = Assert.Single(committed.Artifact.Pages[0].Strokes);
                    Assert.Equal(captured.OperationId, stroke.StrokeId);
                    Assert.All(stroke.Samples, sample => Assert.Equal(0.5, sample.Pressure));
                    var reopened = await fixture.Bridge.OpenAsync(fixture.FileId, ct);
                    Assert.Equal(committed.FilesRevision.Id, reopened.CasRevisionId);
                    using var document = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(reopened.Artifact));
                    using var rendered = new CanvasNativeViewport(document, fixture.ReadinessFor(reopened.CasRevisionId));
                    grid.Children.Remove(surface); grid.Children.Add(rendered); window.UpdateLayout();
                    Assert.True(await rendered.RefreshAsync(ct));
                    var bitmap = Assert.IsAssignableFrom<Bitmap>(Assert.Single(rendered.GetVisualDescendants().OfType<Image>()).Source);
                    Assert.True(HasRedPixel(bitmap), "The committed donor stroke must produce actual red raster pixels after reopening.");
                }
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None));
    }

    private static IEnumerable<Button> Buttons(Control value) { value.UpdateLayout(); return value.GetVisualDescendants().OfType<Button>(); }
    private static Button Button(Control value, string text) => Assert.Single(Buttons(value), button => Equals(button.Content, text));
    private static async Task Until(Func<bool> predicate)
    { for (var i = 0; i < 300 && !predicate(); i++) await Task.Delay(10); Assert.True(predicate()); }
    private static async Task UntilAsync(Func<Task<bool>> predicate)
    { for (var i = 0; i < 300; i++) { if (await predicate()) return; await Task.Delay(10); } Assert.True(await predicate()); }
    private static bool HasRedPixel(Bitmap bitmap)
    {
        var stride = checked(bitmap.PixelSize.Width * 4); var count = checked(stride * bitmap.PixelSize.Height);
        var data = Marshal.AllocHGlobal(count);
        try
        {
            bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), data, count, stride);
            var pixels = new byte[count]; Marshal.Copy(data, pixels, 0, count);
            for (var i = 0; i < pixels.Length; i += 4)
                if (pixels[i + 2] > 128 && pixels[i] < 40 && pixels[i + 1] < 40 && pixels[i + 3] > 128) return true;
            return false;
        }
        finally { Marshal.FreeHGlobal(data); }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "canvas-native-input-" + Guid.NewGuid().ToString("N"));
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
            var current = await Bridge.OpenAsync(FileId, ct);
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
            using var blank = CanvasRnoteDocument.Create("Native input canvas");
            f.FileId = (await f.Bridge.CreateAsync(blank.Snapshot, ct)).FileId;
            f.Opened = await f.Bridge.OpenAsync(f.FileId, ct);
            f.Owner = new(f.Bridge, f.Broker, f.Profiles); return f;
        }
        public async ValueTask DisposeAsync() { await Runtime.DisposeAsync(); Directory.Delete(_root, true); }
    }
}

public sealed class CanvasInputTestApplication : Avalonia.Application
{
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this);
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<CanvasInputTestApplication>().UseSkia())
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
