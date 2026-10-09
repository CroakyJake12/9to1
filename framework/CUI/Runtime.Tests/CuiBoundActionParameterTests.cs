using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiBoundActionParameterTests
{
    [Fact]
    public async Task Same_native_button_dispatches_current_selected_object_after_initial_null_and_keeps_literal_parameters()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var model = new CuiViewModel(); model.Set("Selected", null);
            var calls = new List<object?>(); using var loader = new CuiControlLoader();
            loader.SetBindingContext(model); loader.SetActionDispatcher(new RecordingDispatcher(calls));
            var (tree, diagnostics) = loader.LoadMarkup("""
                <Cui><Actions><Action name="Choose" command="choose" parameter="{Binding Selected}" />
                <Action name="Literal" command="choose" parameter="unchanged literal" /></Actions>
                <Page><Button id="Current" action="Choose" Text="Choose" />
                <Button id="Literal" action="Literal" Text="Literal" /></Page></Cui>
                """);
            Assert.Empty(diagnostics); var root = Assert.IsAssignableFrom<Control>(tree); loader.WireBindings(root);
            var button = root.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "Current");
            var literal = root.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "Literal");
            var window = new Window { Width = 500, Height = 320, Content = root };
            try
            {
                window.Show(); window.UpdateLayout();
                var first = new object(); model.Set("Selected", first);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
                Assert.Same(first, Assert.Single(calls));
                var second = new object(); model.Set("Selected", second);
                Assert.Same(button, root.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "Current"));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
                Assert.Equal(2, calls.Count); Assert.Same(second, calls[1]);
                literal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
                Assert.Equal("unchanged literal", Assert.IsType<string>(calls[2]));
                loader.Dispose(); await loader.WhenActionsIdleAsync();
                model.Set("Selected", new object()); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync(); Assert.Equal(3, calls.Count);
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); window.Close(); }
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Bound_getter_runs_after_actual_pipeline_publication_and_its_fault_and_observer_remain_on_cached_close()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var cause = new IOException("Actual live parameter getter");
            var observer = new OperationCanceledException("Actual independent action observer");
            var calls = new List<object?>(); var loader = new CuiControlLoader(); var reads = 0;
            var context = new GetterContext(() =>
            {
                reads++; Assert.False(loader.WhenActionsIdleAsync().IsCompleted); throw cause;
            });
            loader.SetBindingContext(context); loader.SetActionDispatcher(new RecordingDispatcher(calls));
            loader.ActionFailed += (_, _) => throw observer;
            var (tree, diagnostics) = loader.LoadMarkup("""
                <Cui><Actions><Action name="Choose" command="choose" parameter="{Binding Selected}" /></Actions>
                <Page><Button action="Choose" Text="Choose" /></Page></Cui>
                """);
            Assert.Empty(diagnostics); var root = Assert.IsAssignableFrom<Control>(tree); loader.WireBindings(root);
            var button = Assert.Single(root.GetVisualDescendants().OfType<Button>());
            try
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var original = loader.WhenActionsIdleAsync();
                var failure = await Assert.ThrowsAnyAsync<Exception>(() => original);
                AssertOnlyKnown(failure, cause, observer); Assert.Contains(cause, Leaves(failure)); Assert.Contains(observer, Leaves(failure));
                Assert.Equal(1, reads); Assert.Empty(calls);
                loader.Dispose(); var close = loader.WhenActionsIdleAsync();
                var closeFailure = await Assert.ThrowsAnyAsync<Exception>(() => close); AssertOnlyKnown(closeFailure, cause, observer);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(1, reads); Assert.Empty(calls);
            }
            finally
            {
                // Keep every actual owner/failure rooted before asserting any cleanup cause.
                lock (RetainedFailures) RetainedFailures.Add(new object[] { loader, root, context, cause, observer });
                loader.Dispose();
                try { await loader.WhenActionsIdleAsync(); } catch (Exception error) { AssertOnlyKnown(error, cause, observer); }
            }
            return true;
        }, CancellationToken.None));
    }

    private static readonly List<object[]> RetainedFailures = new();
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
        Assert.Fail("Unexpected original parameter cleanup cause: " + error);
    }
    private sealed class GetterContext(Func<object?> get) : ICuiBindingContext
    {
        public bool TryGetValue(string path, out object? value)
        { value = path == "Selected" ? get() : null; return path == "Selected"; }
    }
    private sealed class RecordingDispatcher(List<object?> calls) : ICuiActionDispatcher
    {
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default)
        { Assert.Equal("choose", command); calls.Add(parameter); return ValueTask.CompletedTask; }
    }
}
