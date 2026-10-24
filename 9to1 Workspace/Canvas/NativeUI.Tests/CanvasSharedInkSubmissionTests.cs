using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Apps.Canvas.NativeUI.Tests;

[Collection("Canvas native UI")]
public sealed class CanvasSharedInkSubmissionTests
{
    [Theory]
    [InlineData(PointerType.Touch, false, false, false, true)]
    [InlineData(PointerType.Touch, false, true, false, false)]
    [InlineData(PointerType.Touch, false, true, true, true)]
    [InlineData(PointerType.Pen, false, false, true, false)]
    [InlineData(PointerType.Pen, true, false, false, true)]
    [InlineData(PointerType.Mouse, false, true, false, false)]
    [InlineData(PointerType.Mouse, false, true, true, true)]
    public void Shared_ink_admits_touch_without_mouse_button_but_keeps_eraser_end_capability_guard(
        PointerType type, bool primaryPressed, bool eraserEnd, bool eraserEnabled, bool expected)
        => Assert.Equal(expected, CanvasSharedInkInput.CanBeginGesture(type, primaryPressed, eraserEnd, eraserEnabled));

    [Fact]
    public void Capture_loss_predicate_ignores_foreign_pointer_and_self_release_but_accepts_active_pointer()
    {
        var active=new object();
        Assert.False(CanvasSharedInkInput.IsRelevantCaptureLoss(active,new object(),false));
        Assert.True(CanvasSharedInkInput.IsRelevantCaptureLoss(active,active,false));
        Assert.False(CanvasSharedInkInput.IsRelevantCaptureLoss(active,active,true));
        Assert.False(CanvasSharedInkInput.IsRelevantCaptureLoss(null,active,false));
    }

    [Fact]
    public async Task Actual_shared_ink_input_cancels_its_captured_pointer_but_self_release_keeps_the_original_request()
    {
        await using var native=HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        await native.Dispatch<bool>(async()=>
        {
            var engine=new HomeProductivityEngine(handlers:[new CanvasInkObjectHandler()]);
            var layerId=Guid.NewGuid();
            var context=new HomeProductivityContext("canvas",Guid.NewGuid().ToString("D"),0,[],
                new HashSet<string>(StringComparer.Ordinal){"drawing.ink"});
            CanvasSharedInkOwnerSnapshot owner=new(context,layerId,"capture-loss-owner",[]);
            var callbackCount=0;
            using var input=new CanvasSharedInkInput(engine,()=>owner,(_,_)=>
            {
                callbackCount++;
                return ValueTask.FromResult(new CanvasSharedInkCommitResult(false,"TEST_OWNER_NOT_ATTACHED",
                    "Input-only capture test: no owning commit is attached."));
            });
            var window=new Window{Width=900,Height=700,Content=input};window.Show();
            IPointer? observed=null;
            input.PointerPressed+=(_,args)=>observed=args.Pointer;
            try
            {
                Assert.True(input.Refresh());window.UpdateLayout();
                var start=new Point(input.Bounds.Width/2,input.Bounds.Height/2);
                window.MouseDown(start,MouseButton.Left);Assert.NotNull(observed);
                var active=observed!;
                active.Capture(null); // Real headless capture-lost event for the owning pointer.
                window.MouseUp(start,MouseButton.Left);
                await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);
                Assert.Equal(0,callbackCount);

                window.MouseDown(start,MouseButton.Left);
                window.MouseMove(start+new Vector(24,12),RawInputModifiers.LeftMouseButton);
                window.MouseUp(start+new Vector(24,12),MouseButton.Left);
                window.MouseUp(start+new Vector(24,12),MouseButton.Left); // Duplicate release cannot queue another owner task.
                window.MouseDown(start,MouseButton.Left);window.MouseUp(start,MouseButton.Left); // Same-pointer re-press stays latched.
                var original=Assert.IsAssignableFrom<Task>(input.PendingSubmission);
                await original;
                Assert.Equal(1,callbackCount); // The input self-release was ignored; no synthetic success is returned.
                Assert.Contains("TEST_OWNER_NOT_ATTACHED",input.LastError!.Message);
            }
            finally{window.Close();await input.CloseAndDrainAsync();}
            return true;
        },CancellationToken.None);
    }

    [Fact]
    public void Translated_shared_ink_erase_maps_visible_hits_back_to_canonical_samples_and_rejects_stale_placement()
    {
        var engine = new HomeProductivityEngine(handlers: [new CanvasInkObjectHandler()]);
        var strokeId = Guid.NewGuid(); var layerId = Guid.NewGuid();
        var stroke = new CanvasInkStroke
        {
            StrokeId = strokeId, LayerId = layerId, RevisionId = Guid.NewGuid(),
            Samples = [new(10, 10, .2, .1, -.1), new(30, 10, .8, -.2, .2)],
            Transform = new() { TranslateX = 100, TranslateY = 40 }
        };
        var context = new HomeProductivityContext("canvas", Guid.NewGuid().ToString("D"), 0,
            [strokeId], new HashSet<string>(StringComparer.Ordinal) { "drawing.ink" });
        var owner = Owner(engine, context, layerId, stroke);
        var captured = CanvasSharedInkStrokeFactory.Snapshot(engine, owner);
        var scene = CanvasSharedInkStrokeFactory.RenderScene(engine, context, layerId, captured.Bundles);
        Assert.True(scene.X > 90); Assert.True(scene.Y > 30); // The displayed scene is translated, not the retained source samples.
        var visibleGesture = new[] { new RnotePointerSample(20 + 100, 10 + 40, .5), new RnotePointerSample(22 + 100, 10 + 40, .5) };

        var hit = CanvasSharedInkStrokeFactory.CaptureWholeErase(engine, captured, visibleGesture, 12);
        Assert.Empty(hit.ReplacementBundles!);
        Assert.Equal(new[] { strokeId }, hit.Erasure!.ErasedObjectIds);
        var placement = Assert.Single(hit.Erasure.Placements);
        Assert.Equal(new RnotePointerSample(20, 10, .5), placement.ToStrokeCoordinates(visibleGesture[0]));
        Assert.True(CanvasSharedInkStrokeFactory.IsCurrentEraseRequest(engine, hit, captured));

        var unplacedGesture = new[] { new RnotePointerSample(20, 10, .5), new RnotePointerSample(22, 10, .5) };
        var miss = CanvasSharedInkStrokeFactory.CaptureWholeErase(engine, captured, unplacedGesture, 12);
        Assert.True(JsonElement.DeepEquals(Assert.Single(captured.Bundles), Assert.Single(miss.ReplacementBundles!)));
        var retained = engine.ParseBundle(Assert.Single(captured.Bundles).GetRawText()).Objects.Single().Content.Deserialize<CanvasInkStroke>()!;
        Assert.Equal(stroke.Samples, retained.Samples); Assert.Equal(stroke.LayerId, retained.LayerId);
        Assert.Equal(stroke.Transform, retained.Transform); Assert.Equal(stroke.RevisionId, retained.RevisionId);

        var movedAgain = Owner(engine, context, layerId, stroke with { Transform = stroke.Transform with { TranslateX = 101 } });
        Assert.False(CanvasSharedInkStrokeFactory.IsCurrentEraseRequest(engine, hit, movedAgain));
        Assert.Throws<NotSupportedException>(() => CanvasSharedInkStrokeFactory.Snapshot(engine,
            Owner(engine, context, layerId, stroke with { Transform = stroke.Transform with { ScaleX = 2 } })));
        Assert.Throws<InvalidDataException>(() => CanvasSharedInkStrokeFactory.Snapshot(engine,
            Owner(engine, context, layerId, stroke with { LayerId = Guid.NewGuid() })));
    }

    [Fact]
    public async Task Drain_waits_for_a_later_success_and_retains_the_exact_earlier_failure()
    {
        var ledger = new CanvasSharedInkSubmissionLedger();
        var originalCause = new IOException("The first accepted stroke submission failed.");
        var firstOriginal = Task.FromException(originalCause);
        var laterCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var laterOriginal = laterCompletion.Task;
        ledger.Observe(firstOriginal);
        ledger.Observe(laterOriginal);

        Assert.Collection(ledger.Originals,
            original => Assert.Same(firstOriginal, original),
            original => Assert.Same(laterOriginal, original));
        var drain = ledger.DrainAsync();
        try
        {
            Assert.False(drain.IsCompleted);
            Assert.False(laterOriginal.IsCompleted);
        }
        finally { laterCompletion.TrySetResult(); }

        var failure = await Assert.ThrowsAsync<AggregateException>(() => drain);
        Assert.True(laterOriginal.IsCompletedSuccessfully);
        Assert.Same(originalCause, Assert.Single(failure.InnerExceptions));
        Assert.Collection(ledger.Originals,
            original => Assert.Same(firstOriginal, original),
            original => Assert.Same(laterOriginal, original));
    }

    [Fact]
    public async Task Drain_awaits_both_failed_originals_and_preserves_each_actual_cause()
    {
        var ledger = new CanvasSharedInkSubmissionLedger();
        var firstCause = new IOException("The first original submission failed.");
        var secondCause = new InvalidOperationException("The second original submission also failed.");
        var firstOriginal = Task.FromException(firstCause);
        var secondCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondOriginal = secondCompletion.Task;
        ledger.Observe(firstOriginal);
        ledger.Observe(secondOriginal);

        var drain = ledger.DrainAsync();
        try
        {
            Assert.False(drain.IsCompleted);
            Assert.False(secondOriginal.IsCompleted);
        }
        finally { secondCompletion.TrySetException(secondCause); }

        var failure = await Assert.ThrowsAsync<AggregateException>(() => drain);
        Assert.True(firstOriginal.IsFaulted);
        Assert.True(secondOriginal.IsFaulted);
        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.Contains(failure.InnerExceptions, cause => ReferenceEquals(cause, firstCause));
        Assert.Contains(failure.InnerExceptions, cause => ReferenceEquals(cause, secondCause));
        Assert.Collection(ledger.Originals,
            original => Assert.Same(firstOriginal, original),
            original => Assert.Same(secondOriginal, original));
    }

    private static CanvasSharedInkOwnerSnapshot Owner(HomeProductivityEngine engine, HomeProductivityContext context,
        Guid layerId, CanvasInkStroke stroke)
    {
        var value = engine.CreateObject("drawing.ink", stroke.StrokeId, JsonSerializer.SerializeToElement(stroke));
        var bundle = engine.SerializeSelection(context with { SelectedObjectIds = [stroke.StrokeId] }, [value]);
        using var document = JsonDocument.Parse(engine.SerializeBundle(bundle));
        return new(context, layerId, "source-revision-1", [document.RootElement.Clone()]);
    }
}
