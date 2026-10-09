using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiRepeatedActionScopeTests
{
    [Fact]
    public async Task Same_key_repeat_refresh_keeps_control_identity_and_dispatches_current_scoped_row_after_pipeline_publication()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        var completed = await session.Dispatch<bool>(async () =>
        {
            var model = new CuiViewModel(); var first = Row("stable", "First row"); var second = Row("second", "Second row");
            model.Set("Rows", new[] { first, second }); var dispatched = new List<object?>();
            var dispatcher = new Dispatcher(dispatched); using var loader = new CuiControlLoader();
            loader.SetBindingContext(model); loader.SetActionDispatcher(dispatcher);
            try
            {
                var (tree, diagnostics) = loader.LoadMarkup("""
                    <Cui><Actions><Action name="Choose" command="rows.choose" parameter="{Binding row}" /></Actions>
                    <Page><Repeat Source="{Binding Rows}" As="row" Key="{Binding row.Id}">
                    <Button action="Choose" Text="{Binding row.Name}" /></Repeat></Page></Cui>
                    """);
                Assert.Empty(diagnostics); var root = Assert.IsAssignableFrom<Control>(tree); loader.WireBindings(root);
                var buttons = root.GetVisualDescendants().OfType<Button>().ToArray(); Assert.Equal(2, buttons.Length);
                Assert.Equal("First row", buttons[0].Content); Assert.Equal("Second row", buttons[1].Content);
                buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
                Assert.Same(second, Assert.Single(dispatched)); dispatched.Clear();
                var originalId = CuiRuntimeIdentity.GetStableId(buttons[0]);
                var current = Row("stable", "Current row"); model.Set("Rows", new[] { current, second });
                var refreshed = root.GetVisualDescendants().OfType<Button>().ToArray();
                Assert.Same(buttons[0], refreshed[0]); Assert.Equal(originalId, CuiRuntimeIdentity.GetStableId(refreshed[0]));
                Assert.Equal("Current row", refreshed[0].Content);
                refreshed[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
                Assert.Same(current, Assert.Single(dispatched)); Assert.DoesNotContain(first, dispatched);
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); }
            return true;
        }, lifetime.Token);
        Assert.True(completed);
    }

    private static Dictionary<string, object?> Row(string id, string name) => new() { ["Id"] = id, ["Name"] = name };
    private sealed class Dispatcher(List<object?> observations) : ICuiActionDispatcher
    {
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default)
        { Assert.Equal("rows.choose", command); observations.Add(parameter); return ValueTask.CompletedTask; }
    }
}
