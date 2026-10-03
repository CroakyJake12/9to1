using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Application;
using Xunit;

namespace HavenOS.Apps.Canvas.NativeUI.Tests;

[Collection("Canvas native UI")]
public sealed class CanvasNativeLayerSelectorTests
{
    [Fact]
    public async Task Mounted_duplicate_names_select_exact_original_layer_without_canonical_write_then_real_ink_uses_its_native_rank()
    {
        await using var native=HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        Assert.True(await native.Dispatch<bool>(async()=>
        {
            using var document=CanvasRnoteDocument.Create("Layer selector");var page=document.Snapshot.Pages[0];var original=page.Layers[0].LayerId;
            var second=Guid.NewGuid();document.CreateNativeUserLayer(page.PageId,second,page.Layers[0].Name,0,Request(document));
            var accesses=0;
            using var selector=new CanvasNativeLayerSelector(document,()=>true,_=>{++accesses;return ValueTask.FromResult(true);});
            var window=new Window{Width=420,Height=240,Content=selector};window.Show();
            try
            {
                var bytes=document.Serialize();var nativeBytes=document.ExportRnote();
                var buttons=selector.GetVisualDescendants().OfType<Button>().ToArray();Assert.Equal(2,buttons.Length);
                Assert.Equal(new[]{second,original},buttons.Select(button=>(Guid)button.Tag!));
                buttons.Single(button=>(Guid)button.Tag! == original).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await selector.WhenSelectionIdleAsync();Assert.Equal(original,document.ActiveNativeUserLayerId);
                Assert.Equal(1,accesses);selector.Refresh(); // same canonical revision, new native row generation.
                buttons.Single(button=>(Guid)button.Tag! == second).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await selector.WhenSelectionIdleAsync();Assert.Equal(1,accesses);Assert.Equal(original,document.ActiveNativeUserLayerId);
                Assert.Equal(bytes,document.Serialize());Assert.Equal(nativeBytes,document.ExportRnote());
                var target=document.ActiveNativeUserLayerId;
                var stroke=document.DrawStrokeIntoNativeUserLayer([new(20,20,.8),new(80,60,.8)],Request(document),target);
                Assert.Equal(original,Assert.Single(document.Snapshot.Pages[0].Strokes).LayerId);
                using var reopened=CanvasRnoteDocument.Open(document.Serialize());
                Assert.Equal(stroke,Assert.Single(reopened.Snapshot.Pages[0].Strokes).StrokeId);
                Assert.NotEmpty(reopened.Render().Svg);return true;
            }
            finally{window.Close();}
        },CancellationToken.None));
    }

    [Fact]
    public async Task Held_original_access_result_cannot_adopt_after_host_retirement_or_resurrect_old_native_layer_view()
    {
        await using var native=HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        Assert.True(await native.Dispatch<bool>(async()=>
        {
            using var document=CanvasRnoteDocument.Create("Retired layer selector");var page=document.Snapshot.Pages[0];var original=page.Layers[0].LayerId;
            var second=Guid.NewGuid();document.CreateNativeUserLayer(page.PageId,second,"Second",0,Request(document));
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var current=true;
            async ValueTask<bool> Validate(CancellationToken cancellationToken){entered.TrySetResult();await release.Task.WaitAsync(cancellationToken);return true;}
            using var selector=new CanvasNativeLayerSelector(document,()=>current,Validate);
            var window=new Window{Width=420,Height=240,Content=selector};window.Show();Task? action=null;
            try
            {
                var bytes=document.Serialize();var nativeBytes=document.ExportRnote();
                selector.GetVisualDescendants().OfType<Button>().Single(button=>(Guid)button.Tag! == original).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                action=selector.WhenSelectionIdleAsync();await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                current=false;release.TrySetResult();await action.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(second,document.ActiveNativeUserLayerId);Assert.Empty(selector.GetVisualDescendants().OfType<Button>());
                current=true;selector.Refresh();Assert.Empty(selector.GetVisualDescendants().OfType<Button>());
                Assert.Equal(bytes,document.Serialize());Assert.Equal(nativeBytes,document.ExportRnote());return true;
            }
            finally
            {
                release.TrySetResult();
                // Observe the genuine already-dispatched pipeline before teardown.
                // Its normal-path failure is awaited above; cleanup cannot replace
                // the original assertion/timeout exception.
                if(action is not null){try{await action;}catch{}}
                window.Close();
            }
        },CancellationToken.None));
    }

    [Fact]
    public async Task Actual_unbound_native_import_keeps_its_render_and_reports_layer_navigation_unavailable_without_fake_rows()
    {
        await using var native=HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        Assert.True(await native.Dispatch<bool>(()=>
        {
            using var source=CanvasRnoteDocument.Create();source.DrawStroke([new(20,20,.8),new(80,60,.8)],Request(source));
            using var imported=CanvasRnoteDocument.Import(source.ExportRnote(),"rnote","Actual imported native ink");
            var bytes=imported.Serialize();var nativeBytes=imported.ExportRnote();var frame=imported.Render().Svg;
            Assert.NotNull(imported.NativeUserLayerUnavailableReason);
            using var selector=new CanvasNativeLayerSelector(imported,()=>true,_=>ValueTask.FromResult(true));
            var window=new Window{Width=420,Height=240,Content=selector};window.Show();
            try
            {
                Assert.Empty(selector.GetVisualDescendants().OfType<Button>());Assert.Equal(imported.NativeUserLayerUnavailableReason,selector.Status);
                Assert.Equal(bytes,imported.Serialize());Assert.Equal(nativeBytes,imported.ExportRnote());Assert.Equal(frame,imported.Render().Svg);return true;
            }
            finally{window.Close();}
        },CancellationToken.None));
    }
    [Fact]
    public async Task Genuine_donor_highlighter_role_keeps_ink_and_reports_user_layer_navigation_unavailable()
    {
        await using var native=HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        Assert.True(await native.Dispatch<bool>(()=>
        {
            using var document=CanvasRnoteDocument.Create("Reserved native role");
            document.DrawStroke([new(20,20,.8),new(80,60,.8)],Request(document),new(CanvasRnoteInkKind.Marker,"#FFFF0000",20));
            var bytes=document.Serialize();var nativeBytes=document.ExportRnote();var frame=document.Render().Svg;
            Assert.NotNull(document.NativeUserLayerUnavailableReason);
            using var selector=new CanvasNativeLayerSelector(document,()=>true,_=>ValueTask.FromResult(true));
            var window=new Window{Width=420,Height=240,Content=selector};window.Show();
            try
            {
                Assert.Empty(selector.GetVisualDescendants().OfType<Button>());Assert.Equal(document.NativeUserLayerUnavailableReason,selector.Status);
                Assert.Equal(bytes,document.Serialize());Assert.Equal(nativeBytes,document.ExportRnote());Assert.Equal(frame,document.Render().Svg);return true;
            }
            finally{window.Close();}
        },CancellationToken.None));
    }
    private static CanvasMutationRequest Request(CanvasRnoteDocument document)=>new(document.Snapshot.RevisionId,Guid.NewGuid(),new("native-layer-selector-fixture","Layer selector fixture"));
}
