using System.IO.Compression;
using System.Text.Json;
using Haven.Application.Canvas;
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

// Genuine donor/Home/Files/headless cases, authored NOT RUN by this worker.
// No early-return success or stub drawing engine substitutes for native execution.
[Collection("Canvas native UI")]
public sealed class CanvasNativeSelectionControlTests
{
    [Fact]
    public async Task Accessible_selected_move_is_only_a_preview_until_real_Home_approval_and_preserves_other_stroke_identity()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        Assert.True(await native.Dispatch<bool>(async () =>
        {
            var ct = CancellationToken.None; await using var f = await Fixture.Create(ct);
            CanvasStrokeEditIntent? captured = null; HomePermissionAuthorization? pending = null;
            var context = SelectionContext(f, () => true, async (intent, token) =>
            {
                captured = intent;
                pending = await f.Broker.AuthorizeAsync("canvas", CanvasStrokeWriteIntent.ActionId, intent.Scopes,
                    intent.Arguments, "Move selected stroke", null, "native-selection-test", token);
            });
            using var surface = new CanvasNativeCuiSurface(f.OpenDocument, f.Readiness, eraser: context);
            using var approvals = new HomeApprovalCuiSurface(f.Runtime, f.Profiles, f.Permissions);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*") }; Grid.SetColumn(approvals, 1);
            grid.Children.Add(surface); grid.Children.Add(approvals);
            var window = new Window { Width = 1100, Height = 800, Content = grid }; window.Show();
            try
            {
                await approvals.InitializeAsync(ct); await surface.InitializeAsync(ct); window.UpdateLayout();
                await Click(surface, "Select"); await KeyboardClick(window, surface, "Next selection");
                await KeyboardClick(window, surface, "→");
                Assert.Null(captured);
                Assert.Equal(f.Opened.CasRevisionId, (await f.Bridge.OpenAsync(f.FileId, f.Opened.StoreId, ct)).CasRevisionId);
                await KeyboardClick(window, surface, "Review move"); await Until(() => pending is not null);
                Assert.NotNull(captured); var intent = captured!;
                var original = f.Opened.Artifact.Pages[0]; var id = original.StrokeOrder[0];
                Assert.Equal(id, intent.StrokeId); Assert.Equal(CanvasStrokeEditKind.Translate, intent.Kind);
                Assert.Equal(10, intent.DeltaX); Assert.Equal(0, intent.DeltaY); Assert.Equal(f.Opened.StoreId, intent.StoreId);
                Assert.Equal(f.Opened.Artifact.RevisionId, intent.ExpectedArtifactRevision);
                Assert.Equal(HomePermissionRequestState.PendingApproval, pending!.State);
                Assert.Equal(f.Opened.CasRevisionId, (await f.Bridge.OpenAsync(f.FileId, f.Opened.StoreId, ct)).CasRevisionId);
                await Approve(approvals, f, pending, ct);
                var capability = Assert.IsType<HomeResourceExecutionCapability>(await f.Broker.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments, ct));
                var committed = await new CanvasHomeStrokeEditOperation(f.Bridge, f.Broker, f.Profiles).ExecuteAsync(intent, capability, ct);
                var moved = committed.Artifact.Pages[0].Strokes.Single(value => value.StrokeId == id);
                Assert.Equal(original.Strokes.Single(value => value.StrokeId == id).Samples.Select(value => value with { X = value.X + 10 }), moved.Samples);
                var other = original.StrokeOrder[1];
                Assert.Equal(original.Strokes.Single(value => value.StrokeId == other).Samples,
                    committed.Artifact.Pages[0].Strokes.Single(value => value.StrokeId == other).Samples);
                Assert.Equal(original.StrokeOrder, committed.Artifact.Pages[0].StrokeOrder);
                Assert.True((await f.Broker.CompleteExecutionAsync(capability, new(HomePermissionRequestState.Succeeded,
                    "CANVAS_SELECTED_MOVE_COMMITTED", "Exact selected stroke moved.", [new("files.item", f.FileId.ToString())]), ct)).Succeeded);
                var reopened = await f.Bridge.OpenAsync(f.FileId, f.Opened.StoreId, ct);
                using var document = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(reopened.Artifact));
                Assert.Equal(id, document.Snapshot.Pages[0].StrokeOrder[0]);
                Assert.NotEmpty(document.Snapshot.SemanticHistory!.Undo);
                Assert.False(await surface.RefreshAsync(ct));
            }
            finally { window.Close(); await surface.CloseAndDrainAsync(); }
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Actual_pointer_drag_previews_without_writing_and_view_or_owner_change_cannot_submit_a_stale_move()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        Assert.True(await native.Dispatch<bool>(async () =>
        {
            var ct = CancellationToken.None; await using var f = await Fixture.Create(ct);
            var available = true; var requests = 0;
            var context = SelectionContext(f, () => available, (_, _) => { requests++; return Task.CompletedTask; });
            using var surface = new CanvasNativeCuiSurface(f.OpenDocument, f.Readiness, eraser: context);
            var window = new Window { Width = 1100, Height = 800, Content = surface }; window.Show();
            try
            {
                await surface.InitializeAsync(ct); window.UpdateLayout(); await Click(surface, "Select");
                var viewport = Assert.Single(surface.GetVisualDescendants().OfType<CanvasNativeViewport>());
                var local = LocalPointForDocument(viewport, new(205, 100));
                var start = viewport.TranslatePoint(local, window)!.Value;
                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(start + new Vector(20, 0), RawInputModifiers.LeftMouseButton);
                Assert.True(viewport.ZoomAt(1.2, local));
                window.MouseUp(start + new Vector(20, 0), MouseButton.Left);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.Equal(0, requests);
                var review = Assert.Single(surface.GetVisualDescendants().OfType<Button>(), value => Equals(value.Content, "Review move"));
                Assert.False(review.IsEnabled);
                var nextLocal = LocalPointForDocument(viewport, new(205, 100));
                var nextStart = viewport.TranslatePoint(nextLocal, window)!.Value;
                window.MouseDown(nextStart, MouseButton.Left);
                window.MouseMove(nextStart + new Vector(20, 0), RawInputModifiers.LeftMouseButton);
                window.MouseUp(nextStart + new Vector(20, 0), MouseButton.Left);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.True(review.IsEnabled); Assert.Equal(0, requests);
                available = false;
                Assert.Throws<UnauthorizedAccessException>(() => context.CaptureEdit(f.Opened.Artifact.Pages[0].StrokeOrder[0], CanvasStrokeEditKind.Translate, 10, 0));
                Assert.Equal(0, requests);
                Assert.Equal(f.Opened.CasRevisionId, (await f.Bridge.OpenAsync(f.FileId, f.Opened.StoreId, ct)).CasRevisionId);
            }
            finally { window.Close(); await surface.CloseAndDrainAsync(); }
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Read_only_selection_keeps_inspection_but_no_edit_port_and_locked_native_strokes_remain_readable()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        Assert.True(await native.Dispatch<bool>(async () =>
        {
            var ct = CancellationToken.None; await using var f = await Fixture.Create(ct);
            using var surface = new CanvasNativeCuiSurface(f.OpenDocument, f.Readiness);
            var window = new Window { Width = 1100, Height = 800, Content = surface }; window.Show();
            try
            {
                await surface.InitializeAsync(ct); window.UpdateLayout(); await Click(surface, "Select");
                await KeyboardClick(window, surface, "Next selection");
                Assert.False(Assert.Single(surface.GetVisualDescendants().OfType<Button>(), value => Equals(value.Content, "Review delete")).IsEnabled);
                Assert.False(Assert.Single(surface.GetVisualDescendants().OfType<Button>(), value => Equals(value.Content, "→")).IsEnabled);
                await KeyboardClick(window, surface, "Clear selection");
                Assert.Equal(f.Opened.CasRevisionId, (await f.Bridge.OpenAsync(f.FileId, f.Opened.StoreId, ct)).CasRevisionId);
                // Pure domain fixture only: no altered body is supplied as a real Files read.
                var locked = CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.Serialize(f.Opened.Artifact));
                locked.Pages[0].Layers[0] = locked.Pages[0].Layers[0] with { IsLocked = true };
                using var nativeLocked = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(locked));
                var metadata = nativeLocked.CaptureStrokeSelection(nativeLocked.Identity.RevisionId);
                Assert.All(metadata.Strokes, value => { Assert.True(value.IsLocked); Assert.False(value.CanDelete); Assert.False(value.CanTranslate); });
                Assert.NotNull(nativeLocked.PreviewSelectionTarget(205, 100, nativeLocked.Identity.RevisionId));
                Assert.Throws<InvalidOperationException>(() => nativeLocked.PreviewQuickErase(205, 100, nativeLocked.Identity.RevisionId));
            }
            finally { window.Close(); await surface.CloseAndDrainAsync(); }
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public void Feedback_captures_exact_committed_donor_curve_not_input_samples_or_page_bounds_and_never_changes_saved_state()
    {
        using var authored = CanvasRnoteDocument.Create("Retained curve feedback");
        var id = authored.DrawStroke(Enumerable.Range(0, 32).Select(index => new RnotePointerSample(
            100 + index * 7, 100 + Math.Sin(index / 3.0) * 25, .2 + index / 50.0)).ToArray(), authored.Identity.RevisionId);
        using var committed = CanvasRnoteDocument.Open(authored.Serialize());
        var before = committed.Serialize(); var revision = committed.Identity.RevisionId;
        var feedback = committed.CaptureSelectionPath(id, revision);
        Assert.Equal(id, feedback.StrokeId); Assert.Equal(committed.Identity.ArtifactId, feedback.ArtifactId);
        Assert.Equal(revision, feedback.RevisionId);
        var selected = committed.ExportCanonicalStrokeSelection([id], revision);
        using var zipped = new MemoryStream(selected, writable: false);
        using var gzip = new GZipStream(zipped, CompressionMode.Decompress);
        using var native = JsonDocument.Parse(gzip);
        var entity = Assert.Single(native.RootElement.GetProperty("data").GetProperty("engine_snapshot")
            .GetProperty("stroke_components").EnumerateArray().Select(slot => slot.GetProperty("value"))
            .Where(value => value.ValueKind != JsonValueKind.Null));
        var path = entity.GetProperty("brushstroke").GetProperty("path");
        AssertElement(path.GetProperty("start"), feedback.Geometry.Start);
        var segments = path.GetProperty("segments").EnumerateArray().ToArray();
        Assert.Equal(segments.Length, feedback.Geometry.Segments.Count);
        for (var index = 0; index < segments.Length; index++)
        {
            var raw = Assert.Single(segments[index].EnumerateObject()); var actual = feedback.Geometry.Segments[index];
            AssertElement(raw.Value.GetProperty("end"), actual.End);
            switch (raw.Name)
            {
                case "lineto": Assert.Equal(CanvasStrokePathSegmentKind.Line, actual.Kind); break;
                case "quadbezto":
                    Assert.Equal(CanvasStrokePathSegmentKind.Quadratic, actual.Kind); AssertPoint(raw.Value.GetProperty("cp"), actual.Control1!); break;
                case "cubbezto":
                    Assert.Equal(CanvasStrokePathSegmentKind.Cubic, actual.Kind); AssertPoint(raw.Value.GetProperty("cp1"), actual.Control1!);
                    AssertPoint(raw.Value.GetProperty("cp2"), actual.Control2!); break;
                default: Assert.Fail("The test requires the actual retained donor path variants."); break;
            }
        }
        Assert.Equal(before, committed.Serialize());
        Assert.Throws<InvalidOperationException>(() => committed.CaptureSelectionPath(id, Guid.NewGuid()));
        Assert.Equal(before, committed.Serialize());
    }

    [Fact]
    public async Task Genuine_contour_tracks_same_letterbox_pan_zoom_and_preview_delta_then_clears_on_close_without_mutation()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        Assert.True(await native.Dispatch<bool>(async () =>
        {
            var ct = CancellationToken.None; await using var f = await Fixture.Create(ct); var requests = 0;
            var context = SelectionContext(f, () => true, (_, _) => { requests++; return Task.CompletedTask; });
            using var surface = new CanvasNativeCuiSurface(f.OpenDocument, f.Readiness, eraser: context);
            var window = new Window { Width = 1100, Height = 800, Content = surface }; window.Show();
            CanvasNativeViewport? viewport = null;
            try
            {
                await surface.InitializeAsync(ct); window.UpdateLayout(); await Click(surface, "Select");
                await KeyboardClick(window, surface, "Next selection");
                viewport = Assert.Single(surface.GetVisualDescendants().OfType<CanvasNativeViewport>());
                Assert.True(viewport.TryGetSelectionFeedbackBounds(out var first, out var firstPreview));
                Assert.Equal(first, firstPreview); Assert.True(first.Width > 0);
                Assert.Contains(surface.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("selected donor path is outlined", StringComparison.Ordinal) == true);
                var anchor = new Point(viewport.Bounds.Width / 2, viewport.Bounds.Height / 2);
                Assert.True(viewport.ZoomAt(1.2, anchor));
                Assert.True(viewport.TryGetSelectionFeedbackBounds(out var zoomed, out _));
                Assert.Equal(anchor.X + (first.X - anchor.X) * 1.2, zoomed.X, precision: 5);
                Assert.Equal(anchor.Y + (first.Y - anchor.Y) * 1.2, zoomed.Y, precision: 5);
                Assert.Equal(first.Width * 1.2, zoomed.Width, precision: 5);
                Assert.True(viewport.PanBy(new Vector(15, -7)));
                Assert.True(viewport.TryGetSelectionFeedbackBounds(out var panned, out _));
                Assert.Equal(zoomed.X + 15, panned.X, precision: 5); Assert.Equal(zoomed.Y - 7, panned.Y, precision: 5);
                await KeyboardClick(window, surface, "→");
                Assert.True(viewport.TryGetSelectionFeedbackBounds(out var original, out var preview));
                var documentDelta = LocalPointForDocument(viewport, new(215, 100)) - LocalPointForDocument(viewport, new(205, 100));
                Assert.Equal(original.X + documentDelta.X, preview.X, precision: 5);
                Assert.Equal(original.Y + documentDelta.Y, preview.Y, precision: 5);
                Assert.Equal(original.Width, preview.Width, precision: 5); Assert.Equal(original.Height, preview.Height, precision: 5);
                Assert.Equal(0, requests);
                Assert.Equal(f.Opened.CasRevisionId, (await f.Bridge.OpenAsync(f.FileId, f.Opened.StoreId, ct)).CasRevisionId);
                await KeyboardClick(window, surface, "Clear selection");
                Assert.False(viewport.TryGetSelectionFeedbackBounds(out _, out _));
            }
            finally { window.Close(); await surface.CloseAndDrainAsync(); }
            Assert.NotNull(viewport); Assert.False(viewport!.TryGetSelectionFeedbackBounds(out _, out _));
            return true;
        }, CancellationToken.None));
    }

    private static void AssertElement(JsonElement expected, CanvasStrokePathElement actual)
    {
        var position = expected.GetProperty("pos").EnumerateArray().Select(value => value.GetDouble()).ToArray();
        Assert.Equal(position[0], actual.X); Assert.Equal(position[1], actual.Y); Assert.Equal(expected.GetProperty("pressure").GetDouble(), actual.Pressure);
    }
    private static void AssertPoint(JsonElement expected, CanvasStrokePathPoint actual)
    {
        var values = expected.EnumerateArray().Select(value => value.GetDouble()).ToArray();
        Assert.Equal(values[0], actual.X); Assert.Equal(values[1], actual.Y);
    }

    private static CanvasNativeEraserContext SelectionContext(Fixture f, Func<bool> available, Func<CanvasStrokeEditIntent, CancellationToken, Task> request)
        => new(f.FileId, f.Opened, available,
            (_, _) => throw new InvalidOperationException("Unexpected whole erase"),
            (_, _) => throw new InvalidOperationException("Unexpected partial erase"),
            (_, _) => throw new InvalidOperationException("Unexpected Quick erase"), request);
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
        private readonly string _root = Path.Combine(Path.GetTempPath(), "canvas-native-selection-" + Guid.NewGuid().ToString("N"));
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
            using var blank = CanvasRnoteDocument.Create("Native selection canvas");
            blank.DrawStroke(Enumerable.Range(0,36).Select(index=>new RnotePointerSample(100+index*6,100,.2+index*.01)).ToArray(),blank.Identity.RevisionId);
            blank.DrawStroke(Enumerable.Range(0, 36).Select(index => new RnotePointerSample(100 + index * 6, 200, .5)).ToArray(), blank.Identity.RevisionId);
            f.FileId = (await f.Bridge.CreateAsync(blank.Snapshot, ct)).FileId;
            f.Opened = await f.Bridge.OpenAsync(f.FileId,f.Workspace.Configuration.StoreId, ct);
            f.Owner = new(f.Bridge, f.Broker, f.Profiles); return f;
        }
        public async ValueTask DisposeAsync() { await Runtime.DisposeAsync(); Directory.Delete(_root, true); }
    }
}
