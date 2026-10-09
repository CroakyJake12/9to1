using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiContextMenuActionTests
{
    [Fact]
    public async Task Attached_native_menu_keeps_visual_content_and_updates_header_then_dispatches_exact_current_row()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var model = new CuiViewModel(); var first = Row("same", "First"); model.Set("Rows", new[] { first });
            var calls = new List<object?>(); using var loader = new CuiControlLoader();
            loader.SetBindingContext(model); loader.SetActionDispatcher(new RecordingDispatcher(calls));
            var (tree, diagnostics) = loader.LoadMarkup("""
                <Cui><Actions><Action name="Choose" command="rows.choose" parameter="{Binding row}" /></Actions>
                <Page><Repeat Source="{Binding Rows}" As="row" Key="{Binding row.Id}">
                  <Button Text="{Binding row.Name}"><ContextMenu>
                    <MenuItem Header="{Binding row.Name}" action="Choose" />
                  </ContextMenu></Button>
                </Repeat></Page></Cui>
                """);
            Assert.Empty(diagnostics); var root = Assert.IsAssignableFrom<Control>(tree); loader.WireBindings(root);
            var window = new Window { Width = 700, Height = 420, Content = root };
            try
            {
                window.Show(); window.UpdateLayout();
                var owner = Assert.Single(root.GetVisualDescendants().OfType<Button>());
                var menu = Assert.IsType<ContextMenu>(owner.ContextMenu); var item = Assert.IsType<MenuItem>(Assert.Single(menu.Items));
                Assert.Equal("First", owner.Content); Assert.Equal("First", item.Header);
                Assert.DoesNotContain(menu, root.GetVisualDescendants());
                var current = Row("same", "Current"); model.Set("Rows", new[] { current }); window.UpdateLayout();
                Assert.Same(owner, Assert.Single(root.GetVisualDescendants().OfType<Button>())); Assert.Same(menu, owner.ContextMenu);
                Assert.Equal("Current", owner.Content); Assert.Equal("Current", item.Header);
                menu.Open(owner); window.UpdateLayout(); Assert.True(menu.IsOpen);
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await loader.WhenActionsIdleAsync();
                Assert.Same(current, Assert.Single(calls)); Assert.DoesNotContain(first, calls);
                item.IsEnabled = false; item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await loader.WhenActionsIdleAsync();
                Assert.Single(calls);
                item.IsEnabled = true; owner.IsEnabled = false;
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await loader.WhenActionsIdleAsync(); Assert.Single(calls);
                owner.IsEnabled = true;
                menu.Close(); loader.Dispose(); await loader.WhenActionsIdleAsync();
                Assert.Null(owner.ContextMenu); item.IsEnabled = true;
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await loader.WhenActionsIdleAsync(); Assert.Single(calls);
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); window.Close(); }
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Removed_repeat_unwires_owned_off_tree_items_and_submenu_bubbling_dispatches_only_clicked_item()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var model = new CuiViewModel(); var row = Row("one", "One"); model.Set("Rows", new[] { row });
            var calls = new List<object?>(); using var loader = new CuiControlLoader();
            loader.SetBindingContext(model); loader.SetActionDispatcher(new RecordingDispatcher(calls));
            var (tree, diagnostics) = loader.LoadMarkup("""
                <Cui><Actions><Action name="Choose" command="rows.choose" parameter="{Binding row}" /></Actions>
                <Page><Repeat Source="{Binding Rows}" As="row" Key="{Binding row.Id}">
                  <Border><ContextMenu><MenuItem Header="Parent" action="Choose">
                    <MenuItem Header="Child" action="Choose" />
                  </MenuItem></ContextMenu><Text Text="{Binding row.Name}" /></Border>
                </Repeat></Page></Cui>
                """);
            Assert.Empty(diagnostics); var root = Assert.IsAssignableFrom<Control>(tree); loader.WireBindings(root);
            var owner = Assert.Single(root.GetVisualDescendants().OfType<Border>());
            var menu = Assert.IsType<ContextMenu>(owner.ContextMenu); var parent = Assert.IsType<MenuItem>(Assert.Single(menu.Items));
            var child = Assert.IsType<MenuItem>(Assert.Single(parent.Items));
            child.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await loader.WhenActionsIdleAsync();
            Assert.Same(row, Assert.Single(calls));
            model.Set("Rows", Array.Empty<Dictionary<string, object?>>());
            Assert.Null(owner.ContextMenu); parent.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); child.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await loader.WhenActionsIdleAsync(); Assert.Single(calls);
            loader.Dispose(); await loader.WhenActionsIdleAsync();
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Held_menu_dispatch_is_published_before_callback_and_close_joins_all_exact_raw_and_observer_faults()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var raw = new TaskCompletionSource(); var entered = new TaskCompletionSource();
            var opaque = new AggregateException("Original opaque", Array.Empty<Exception>()); var io = new IOException("Actual second raw cause");
            var observer = new OperationCanceledException("Independent observer fault");
            var loader = new CuiControlLoader();
            loader.SetActionDispatcher(new CallbackDispatcher(() =>
            {
                Assert.False(loader.WhenActionsIdleAsync().IsCompleted); entered.SetResult(); return new ValueTask(raw.Task);
            }));
            loader.ActionFailed += (_, _) => throw observer;
            var (root, diagnostics) = loader.LoadMarkup("<Cui><Page><Button Text=\"Owner\"><ContextMenu><MenuItem Header=\"Run\" action=\"run\" /></ContextMenu></Button></Page></Cui>");
            Assert.Empty(diagnostics); var tree = Assert.IsAssignableFrom<Control>(root); loader.WireBindings(tree);
            var owner = Assert.Single(tree.GetVisualDescendants().OfType<Button>()); var item = Assert.IsType<MenuItem>(Assert.Single(owner.ContextMenu!.Items));
            try
            {
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); var original = loader.WhenActionsIdleAsync();
                var first = await Task.WhenAny(entered.Task, original); if (ReferenceEquals(first, original)) await original;
                Assert.True(entered.Task.IsCompletedSuccessfully); Assert.False(original.IsCompleted);
                raw.SetException(new Exception[] { opaque, io });
                var failure = await Assert.ThrowsAnyAsync<Exception>(() => original); AssertOnlyKnown(failure, opaque, io, observer);
                Assert.Contains(opaque, Leaves(failure)); Assert.Contains(io, Leaves(failure)); Assert.Contains(observer, Leaves(failure));
                loader.Dispose(); var close = loader.WhenActionsIdleAsync();
                var closeFailure = await Assert.ThrowsAnyAsync<Exception>(() => close); AssertOnlyKnown(closeFailure, opaque, io, observer);
                Assert.Null(owner.ContextMenu); item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Assert.True(raw.Task.IsFaulted);
            }
            finally
            {
                raw.TrySetException(new Exception[] { opaque, io });
                try { await raw.Task; } catch (Exception error) { AssertOnlyKnown(error, opaque, io); }
                loader.Dispose(); try { await loader.WhenActionsIdleAsync(); } catch (Exception error) { AssertOnlyKnown(error, opaque, io, observer); }
            }
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Actual_native_menu_close_callback_failure_is_retained_and_never_replayed()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var loader = new CuiControlLoader(); var cause = new IOException("Actual native Closed callback"); var attempts = 0;
            var (tree, diagnostics) = loader.LoadMarkup("<Cui><Page><Button Text=\"Owner\"><ContextMenu><MenuItem Header=\"Item\" /></ContextMenu></Button></Page></Cui>");
            Assert.Empty(diagnostics); var root = Assert.IsAssignableFrom<Control>(tree); var owner = Assert.Single(root.GetVisualDescendants().OfType<Button>());
            var menu = Assert.IsType<ContextMenu>(owner.ContextMenu); var window = new Window { Width = 600, Height = 400, Content = root };
            EventHandler<RoutedEventArgs> fail = (_, _) => { attempts++; throw cause; };
            try
            {
                window.Show(); window.UpdateLayout(); menu.Open(owner); window.UpdateLayout(); Assert.True(menu.IsOpen); menu.Closed += fail;
                Assert.Same(cause, Assert.Throws<IOException>(() => loader.Dispose()));
                var failure = await Assert.ThrowsAnyAsync<Exception>(() => loader.WhenActionsIdleAsync()); AssertOnlyKnown(failure, cause);
                Assert.Same(menu, owner.ContextMenu); loader.Dispose(); Assert.Equal(1, attempts);
                var repeated = await Assert.ThrowsAnyAsync<Exception>(() => loader.WhenActionsIdleAsync()); AssertOnlyKnown(repeated, cause);
            }
            finally
            {
                menu.Closed -= fail; loader.Dispose();
                try { await loader.WhenActionsIdleAsync(); } catch (Exception error) { AssertOnlyKnown(error, cause); }
                window.Close();
            }
            return true;
        }, CancellationToken.None));
    }

    private static Dictionary<string, object?> Row(string id, string name) => new() { ["Id"] = id, ["Name"] = name };
    private static IEnumerable<Exception> Leaves(Exception error)
    {
        if (error is AggregateException { InnerExceptions.Count: > 0 } group)
        { foreach (var inner in group.InnerExceptions) foreach (var leaf in Leaves(inner)) yield return leaf; }
        else yield return error;
    }
    private static void AssertOnlyKnown(Exception error, params Exception[] expected)
    {
        if (expected.Any(value => ReferenceEquals(value, error))) return;
        if (error is AggregateException { InnerExceptions.Count: > 0 } group)
        { foreach (var inner in group.InnerExceptions) AssertOnlyKnown(inner, expected); return; }
        Assert.Fail("Unexpected cleanup cause: " + error);
    }
    private sealed class RecordingDispatcher(List<object?> calls) : ICuiActionDispatcher
    { public ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default) { Assert.Equal("rows.choose", command); calls.Add(parameter); return ValueTask.CompletedTask; } }
    private sealed class CallbackDispatcher(Func<ValueTask> callback) : ICuiActionDispatcher
    { public ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default) => callback(); }
}
