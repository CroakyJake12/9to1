using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
using Haven.Application;
using Xunit;
namespace HavenOS.Apps.Canvas.NativeUI.Tests;
[Collection("Canvas native UI")]
public sealed class CanvasNativeLayerKeyboardTests
{
    private static CanvasMutationRequest Request(CanvasRnoteDocument document)=>new(document.Identity.RevisionId,Guid.NewGuid(),new("native-layer-keyboard","Layer keyboard"));
    private static void Key(Window window,Key key,RawInputModifiers modifiers=RawInputModifiers.None)
    {
        var physical=key==Avalonia.Input.Key.Up?PhysicalKey.ArrowUp:PhysicalKey.ArrowDown;
        window.KeyPress(key,modifiers,physical,key.ToString());window.KeyRelease(key,modifiers,physical,key.ToString());
    }
    [Fact]
    public async Task Genuine_mounted_native_keyboard_navigates_stable_duplicate_layer_IDs_preserves_focus_and_refuses_modified_or_external_input()
    {
        await using var native=HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        Assert.True(await native.Dispatch<bool>(async()=>
        {
            using var document=CanvasRnoteDocument.Create("Layer keyboard");var page=document.Snapshot.Pages.Single();var last=page.LayerOrder[0];var first=Guid.NewGuid();
            document.CreateNativeUserLayer(page.PageId,first,page.Layers.Single().Name,0,Request(document));document.SelectNativeUserLayer(page.PageId,first);
            var accesses=0;var current=true;
            using var selector=new CanvasNativeLayerSelector(document,()=>current,_=>{++accesses;return ValueTask.FromResult(true);});
            var outside=new TextBox();var window=new Window{Width=420,Height=300,Content=new StackPanel{Children={selector,outside}}};window.Show();
            try
            {
                var bytes=document.Serialize();var ink=document.ExportRnote();var frame=document.Render().Svg;
                selector.GetVisualDescendants().OfType<Button>().Single(button=>button.Tag is Guid id&&id==first).Focus();
                Key(window,Avalonia.Input.Key.Down);await selector.WhenSelectionIdleAsync();Assert.Equal(last,document.ActiveNativeUserLayerId);Assert.Equal(1,accesses);
                Assert.True(selector.GetVisualDescendants().OfType<Button>().Single(button=>button.Tag is Guid id&&id==last).IsFocused);
                Key(window,Avalonia.Input.Key.Up);await selector.WhenSelectionIdleAsync();Assert.Equal(first,document.ActiveNativeUserLayerId);Assert.Equal(2,accesses);
                Key(window,Avalonia.Input.Key.Up);Key(window,Avalonia.Input.Key.Down,RawInputModifiers.Shift);await selector.WhenSelectionIdleAsync();
                Assert.Equal(first,document.ActiveNativeUserLayerId);Assert.Equal(2,accesses);
                outside.Focus();Key(window,Avalonia.Input.Key.Down);await selector.WhenSelectionIdleAsync();Assert.Equal(first,document.ActiveNativeUserLayerId);Assert.Equal(2,accesses);
                selector.GetVisualDescendants().OfType<Button>().Single(button=>button.Tag is Guid id&&id==first).Focus();current=false;
                Key(window,Avalonia.Input.Key.Down);current=true;Key(window,Avalonia.Input.Key.Down);await selector.WhenSelectionIdleAsync();
                Assert.Empty(selector.GetVisualDescendants().OfType<Button>());Assert.Equal(first,document.ActiveNativeUserLayerId);Assert.Equal(2,accesses);
                Assert.Equal(bytes,document.Serialize());Assert.Equal(ink,document.ExportRnote());Assert.Equal(frame,document.Render().Svg);return true;
            }
            finally{window.Close();}
        },CancellationToken.None));
    }
    [Fact]
    public async Task Held_original_keyboard_access_cannot_adopt_after_same_revision_row_refresh()
    {
        await using var native=HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        Assert.True(await native.Dispatch<bool>(async()=>
        {
            using var document=CanvasRnoteDocument.Create("Stale keyboard");var page=document.Snapshot.Pages.Single();var last=page.LayerOrder[0];var first=Guid.NewGuid();
            document.CreateNativeUserLayer(page.PageId,first,"First",0,Request(document));document.SelectNativeUserLayer(page.PageId,first);
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async ValueTask<bool> Access(CancellationToken ct){entered.TrySetResult();await release.Task.WaitAsync(ct);return true;}
            using var selector=new CanvasNativeLayerSelector(document,()=>true,Access);var window=new Window{Width=420,Height=240,Content=selector};window.Show();Task? action=null;Exception? primary=null;
            try
            {
                var bytes=document.Serialize();var ink=document.ExportRnote();
                selector.GetVisualDescendants().OfType<Button>().Single(button=>button.Tag is Guid id&&id==first).Focus();
                Key(window,Avalonia.Input.Key.Down);action=selector.WhenSelectionIdleAsync();await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                selector.Refresh();release.TrySetResult();await action.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(first,document.ActiveNativeUserLayerId);Assert.NotEqual(last,document.ActiveNativeUserLayerId);
                Assert.Equal(bytes,document.Serialize());Assert.Equal(ink,document.ExportRnote());return true;
            }
            catch(Exception error){primary=error;throw;}
            finally
            {
                release.TrySetResult();
                try{if(action is not null)try{await action;}catch when(primary is not null){}}
                finally{window.Close();}
            }
        },CancellationToken.None));
    }
}
