using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;

namespace HavenOS.Apps.Motion;

internal static class MotionNativeWorkflowTest
{
    public static int Run()
    {
        CuiNativeHost.ConfigureFonts(AppBuilder.Configure<MotionTestApplication>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })).SetupWithoutStarting();
        var folder = Directory.CreateTempSubdirectory("motion-native-evidence-");
        var path = Path.Combine(folder.FullName, "native.motion.json");
        var store = new MotionProjectStore(); var project = store.Create("file:example-source", 1920, 1080, 30, 1);
        store.Save(path, project, -1); var session = new MotionEditSession(store, path);
        var sequence = project.Sequences[0]; var track = sequence.VideoTracks[0];
        session.Apply(p => store.Insert(p, p.Revision, sequence.SequenceId, track.TrackId, p.AssetReferences[0].AssetId, 0, 0, 90));
        session.Apply(p => store.AddTrack(p, p.Revision, sequence.SequenceId, "B-roll"));
        var second = session.Project.Sequences[0].VideoTracks[1];
        session.Apply(p => store.Insert(p, p.Revision, sequence.SequenceId, second.TrackId, p.AssetReferences[0].AssetId, 30, 90, 120));
        var workspace = new MotionCuiWorkspace(session, _ => true);
        workspace.TrySetValue("ClipIndex", 0);
        var scene = MotionNativeSurface.CreateScene(workspace, new TestReadiness());
        using var loader = new CuiControlLoader(scene.ControlRegistry!);
        loader.SetBindingContext(workspace); loader.SetActionDispatcher(workspace);
        var (root, diagnostics) = loader.TryLoad(scene.Document);
        if (root is null || diagnostics.Any(d => d.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error))
            throw new InvalidDataException(string.Join(Environment.NewLine, diagnostics));
        loader.WireBindings(root); // SAME canonical post-load step used by CuiSceneHost.
        var window = new Window { Width = 1200, Height = 900, Content = root };
        var actualVisualResources = CuiSceneVisualResources.Create("Imagine", CuiAppearance.Dark);
        window.Resources.MergedDictionaries.Add(actualVisualResources);
        if (actualVisualResources["CuiBackgroundBrush"] is not SolidColorBrush actualBackground)
            throw new InvalidDataException("The actual Imagine Dark background resource is unavailable.");
        window.Background = actualBackground;
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        if (window.Bounds.Width <= 0 || window.Bounds.Height <= 0 || root.Bounds.Width <= 0 || root.Bounds.Height <= 0)
            throw new InvalidDataException("The SAME actual native Motion window and scene did not finish layout.");
        MotionMarkerWorkflowTest.RunNative(loader, root, workspace, store, path);
        VerifyReachableNativeControls(window, loader, root, workspace, store, path, folder.FullName);
        window.UpdateLayout();
        using var bitmap = window.CaptureRenderedFrame()
            ?? throw new InvalidDataException("The actual native Motion window produced no frame.");
        DemandOpaqueNativeBackground(bitmap, actualBackground.Color);
        var image = Path.Combine(folder.FullName, "motion-native-window-opaque.png"); bitmap.Save(image);
        window.Close(); workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Console.WriteLine($"Motion CUI rendered with {diagnostics.Count} diagnostics. Retained image: {image}");
        return 0;
    }
    private static readonly List<(object Owner, Task? Source, Exception Failure)> RetainedViewportFailures = [];
    private static void VerifyReachableNativeControls(Window window, CuiControlLoader loader, Control root,
        MotionCuiWorkspace workspace, MotionProjectStore store, string path, string evidence)
    {
        try
        {
            T Find<T>(string id) where T : Control => root.GetVisualDescendants().OfType<T>().Single(control => control.Name == id);
            Rect WindowRect(Control control)
            {
                var origin = control.TranslatePoint(default, window) ?? throw new InvalidDataException("Actual Motion control has no window transform.");
                return new(origin, control.Bounds.Size);
            }
            bool Contains(Rect outer, Rect inner) => inner.Width > 0 && inner.Height > 0 && inner.X >= outer.X - .01 &&
                inner.Y >= outer.Y - .01 && inner.Right <= outer.Right + .01 && inner.Bottom <= outer.Bottom + .01;
            void DemandInViewport(Control control)
            {
                var actual = WindowRect(control);
                if (!control.IsEffectivelyVisible || !Contains(new Rect(window.Bounds.Size), actual))
                    throw new InvalidDataException("Actual Motion control is clipped by the native window: " + control.Name);
                foreach (var viewport in control.GetVisualAncestors().OfType<ScrollViewer>())
                    {
                        var presenter = viewport.GetVisualDescendants().OfType<ScrollContentPresenter>().Single(part =>
                            ReferenceEquals(part.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault(), viewport));
                        if (!Contains(WindowRect(presenter), actual)) throw new InvalidDataException("Actual Motion control is clipped by its original content viewport: " + control.Name);
                    }
            }
            void Reach(Control control)
            {
                control.BringIntoView(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                DemandInViewport(control);
                // The vendored hit tester uses compositor readback. A headless
                // layout pass alone does not publish a newly scrolled transform;
                // capture the SAME actual rendered scene before its real input.
                using var reachedFrame = window.CaptureRenderedFrame()
                    ?? throw new InvalidDataException("Reached native Motion control has no actual rendered scene: " + control.Name);
                DemandInViewport(control);
                var actual = WindowRect(control); var center = actual.Center;
                var hit = window.InputHitTest(center) as Visual;
                if (hit is null || !(ReferenceEquals(hit, control) || hit.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, control))))
                {
                    var label = $"{window.Bounds.Width:0}x{window.Bounds.Height:0}";
                    reachedFrame.Save(Path.Combine(evidence, "motion-reach-failure-" + label + "-" + control.Name + ".png"));
                    var hitControl = hit as Control;
                    var hitDescription = hitControl is null ? hit?.GetType().Name ?? "null"
                        : hitControl.GetType().Name + ":" + hitControl.Name + " bounds=" + WindowRect(hitControl);
                    var viewports = string.Join("; ", control.GetVisualAncestors().OfType<ScrollViewer>()
                        .Select(viewport => viewport.Name + " offset=" + viewport.Offset + " viewport=" + viewport.Viewport));
                    throw new InvalidDataException("Reached native Motion control does not own its real hit-tested point: " + control.Name +
                        " bounds=" + actual + " point=" + center + " enabled=" + control.IsEffectivelyEnabled +
                        " hit=" + hitDescription + " scroll=" + viewports);
                }
            }
            void Observe(Task task, object owner, string stage)
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!task.IsCompleted && DateTime.UtcNow < deadline) Dispatcher.UIThread.RunJobs();
                if (!task.IsCompleted)
                {
                    var failure = new TimeoutException("Actual reached-control source remains unfinished: " + stage);
                    RetainedViewportFailures.Add((owner, task, failure)); throw failure;
                }
                task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            }
            void Click(Button control)
            {
                Reach(control);
                if (!control.IsEnabled || loader.Inspect(control) is not { ActionsWired: true, DispatcherConnected: true })
                    throw new InvalidDataException("Reached Motion action is not enabled/wired: " + control.Name);
                var point = WindowRect(control).Center;
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
                Observe(loader.WhenActionsIdleAsync(), loader, control.Name ?? "native button");
            }
            void Capture(string name)
            {
                window.UpdateLayout();
                using var actual = window.CaptureRenderedFrame() ?? throw new InvalidDataException("Actual reached-control Motion viewport produced no frame.");
                actual.Save(Path.Combine(evidence, name));
            }
            foreach (var size in new[] { new Size(1200, 900), new Size(800, 600) })
            {
                window.Width = size.Width; window.Height = size.Height;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var label = $"{size.Width:0}x{size.Height:0}";
                foreach (var viewport in new[] { Find<ScrollViewer>("MotionMediaViewport"), Find<ScrollViewer>("MotionInspectorViewport"), Find<ScrollViewer>("MotionCaptionViewport") })
                {
                    if (!Contains(new Rect(window.Bounds.Size), WindowRect(viewport)) || viewport.Viewport.Height <= 0 || viewport.Viewport.Width <= 0)
                        throw new InvalidDataException("Actual Motion panel has no positive bounded viewport at " + label);
                    viewport.Offset = default;
                }
                foreach (var control in Find<ComboBox>("MotionSequenceSelector").GetVisualParent()!.GetVisualChildren().OfType<Control>())
                    DemandInViewport(control);
                Capture("motion-fit-" + label + "-panels.png");
                Click(Find<Button>("MotionSelectMarkerRow"));
                var before = store.Load(path); var marker = before.Sequences[0].Markers!.Single();
                var markerName = Find<TextBox>("MotionMarkerName"); Reach(markerName); markerName.Text = "Reachable " + label;
                var save = Find<Button>("MotionUpdateMarker"); Reach(save);
                Capture("motion-fit-" + label + "-marker-save.png"); Click(save);
                var edited = store.Load(path).Sequences[0].Markers!.Single();
                if (edited.MarkerId != marker.MarkerId || edited.Name != "Reachable " + label || edited.Frame != marker.Frame)
                    throw new InvalidDataException("Real reached Save did not persist the SAME marker fields.");
                var delete = Find<Button>("MotionDeleteMarker"); Reach(delete); Click(delete);
                if (store.Load(path).Sequences[0].Markers!.Count != 0) throw new InvalidDataException("Real reached Delete did not persist marker removal.");
                Click(Find<Button>("MotionUndoButton"));
                if (store.Load(path).Sequences[0].Markers!.Single().MarkerId != marker.MarkerId)
                    throw new InvalidDataException("Real header Undo did not restore the same marker.");
                Find<ComboBox>("MotionClipSelector").SelectedIndex = 0;
                var originalElement = workspace.Sequence.VideoTracks[0].Elements[0];
                var destination = Find<ComboBox>("MotionDestinationTrack"); destination.SelectedIndex = 0; Reach(destination);
                Capture("motion-fit-" + label + "-destination.png");
                var point = WindowRect(destination).Center;
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
                if (!destination.IsDropDownOpen) throw new InvalidDataException("Actual reached destination selector did not open its native popup.");
                window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                if (destination.SelectedIndex != 1 || destination.IsDropDownOpen)
                    throw new InvalidDataException("Actual keyboard destination selection did not commit the second original track.");
                Click(Find<Button>("MotionMoveToTrackButton"));
                var moved = store.Load(path).Sequences[0].VideoTracks[1].Elements.Single(element => element.ElementId == originalElement.ElementId);
                if (moved != (originalElement with { TrackId = workspace.Sequence.VideoTracks[1].TrackId }))
                    throw new InvalidDataException("Actual reached destination action changed canonical element source/timing instead of only its track.");
                Click(Find<Button>("MotionUndoButton"));
                var restored = store.Load(path).Sequences[0].VideoTracks[0].Elements.Single(element => element.ElementId == originalElement.ElementId);
                if (restored != originalElement || !store.Load(path).AssetReferences.SequenceEqual(before.AssetReferences))
                    throw new InvalidDataException("Actual reached track Undo replaced element or canonical source identity.");
                Console.WriteLine("Actual native viewport, scrolling, hit-tested mouse marker Save/Delete and keyboard destination +track move/Undo passed at " + label + ".");
            }
            window.Width = 1200; window.Height = 900;
            foreach (var viewport in new[] { Find<ScrollViewer>("MotionMediaViewport"), Find<ScrollViewer>("MotionInspectorViewport"), Find<ScrollViewer>("MotionCaptionViewport") }) viewport.Offset = default;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        }
        catch (Exception failure)
        {
            RetainedViewportFailures.Add((window, workspace.OriginalClose, failure));
            RetainedViewportFailures.Add((loader, null, failure)); throw;
        }
    }
    private static void DemandOpaqueNativeBackground(Bitmap bitmap, Color expected)
    {
        if (bitmap.PixelSize.Width <= 1 || bitmap.PixelSize.Height <= 1 || expected.A != 255)
            throw new InvalidDataException("The actual native frame or semantic background is not opaque and positive.");
        var bgra = bitmap.Format == PixelFormat.Bgra8888;
        if (!bgra && bitmap.Format != PixelFormat.Rgba8888)
            throw new InvalidDataException($"The actual native capture has unsupported pixel format {bitmap.Format}.");
        var stride = checked(bitmap.PixelSize.Width * 4);
        var pixels = new byte[checked(stride * bitmap.PixelSize.Height)];
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pinned.AddrOfPinnedObject(), pixels.Length, stride); }
        finally { pinned.Free(); }
        for (var index = 3; index < pixels.Length; index += 4)
            if (pixels[index] != 255) throw new InvalidDataException("The actual native window capture contains transparent pixels.");
        // The authored root has a 12-DIP margin. This unoccupied pixel must come
        // from the SAME actual Window background, not a fabricated comparison fill.
        var sample = stride + 4;
        var red = pixels[sample + (bgra ? 2 : 0)]; var green = pixels[sample + 1]; var blue = pixels[sample + (bgra ? 0 : 2)];
        if (red != expected.R || green != expected.G || blue != expected.B)
            throw new InvalidDataException($"Actual native background RGB({red},{green},{blue}) differs from the Imagine Dark semantic resource {expected}.");
        Console.WriteLine($"Opaque actual native frame {bitmap.PixelSize}; unoccupied background matches Imagine Dark {expected}.");
    }
    private sealed class MotionTestApplication : Application
    { public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this, "Imagine", CuiAppearance.Dark); }
    private sealed class TestReadiness : ICuiSceneReadiness
    { public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "fixture", "Fixture only")); }
}
