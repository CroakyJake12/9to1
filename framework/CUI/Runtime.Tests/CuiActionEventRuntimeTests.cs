using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiActionEventRuntimeTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("Clicked")]
    [InlineData("OnceClick")]
    public async Task Owned_action_dispatches_current_parameters_again_on_later_clicks_and_detaches_on_disposal(string type)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            var model = new CuiViewModel(); var first = new object(); model.Set("Selected", first);
            var calls = new List<(string, object?)>(); using var loader = new CuiControlLoader();
            loader.SetBindingContext(model);
            loader.SetActionDispatcher(new Dispatcher((command, parameter, _) => { calls.Add((command, parameter)); return ValueTask.CompletedTask; }));
            var (root, diagnostics) = loader.LoadMarkup($"<Cui><Actions><Action name=\"Save\" command=\"canonical-save\" parameter=\"{{Binding Selected}}\"/></Actions><Page><Button Text=\"Save\"><ActionEvent Type=\"{type}\" Action=\"Save\"/></Button></Page></Cui>");
            Assert.DoesNotContain(diagnostics, d => d.Severity == CuiDiagnosticSeverity.Error);
            Assert.NotNull(root); loader.WireBindings(root);
            var button = Assert.Single(root.GetVisualDescendants().OfType<Button>());
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync().WaitAsync(Bound);
            Assert.Equal("canonical-save", Assert.Single(calls).Item1); Assert.Same(first, calls[0].Item2);
            var second = new object(); model.Set("Selected", second);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync().WaitAsync(Bound);
            Assert.Equal(2, calls.Count); Assert.Same(second, calls[1].Item2);
            loader.Dispose(); await loader.WhenActionsIdleAsync().WaitAsync(Bound);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(2, calls.Count);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Owned_action_ignores_child_click_bubbling_and_preserves_the_childs_direct_action()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            var calls = new List<string>(); using var loader = new CuiControlLoader();
            loader.SetActionDispatcher(new Dispatcher((command, _, _) => { calls.Add(command); return ValueTask.CompletedTask; }));
            var (root, diagnostics) = loader.LoadMarkup("<Page><Button ID=\"Parent\"><ActionEvent Type=\"Clicked\" Bubble=\"False\" Action=\"ParentAction\"/><Button ID=\"Child\" Text=\"Child\" Action=\"ChildAction\"/></Button></Page>");
            Assert.Empty(diagnostics); Assert.NotNull(root); loader.WireBindings(root);
            var buttons = root.GetVisualDescendants().OfType<Button>().ToDictionary(button => button.Name!);
            buttons["Child"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync().WaitAsync(Bound);
            Assert.Equal(new[] { "ChildAction" }, calls);
            buttons["Parent"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync().WaitAsync(Bound);
            Assert.Equal(new[] { "ChildAction", "ParentAction" }, calls);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Async_owned_action_retains_original_work_and_view_cancellation_without_blocking_input()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var token = default(CancellationToken); var calls = 0; var loader = new CuiControlLoader();
            loader.SetActionDispatcher(new Dispatcher((_, _, current) =>
            {
                Assert.False(loader.WhenActionsIdleAsync().IsCompleted);
                calls++; token = current; entered.SetResult(); return new ValueTask(release.Task);
            }));
            var (root, diagnostics) = loader.LoadMarkup("<Page><Button Text=\"Save\"><&Clicked Action:Save/></Button><Input Type=\"Text\"/></Page>");
            Assert.Empty(diagnostics); Assert.NotNull(root); loader.WireBindings(root);
            var button = Assert.Single(root.GetVisualDescendants().OfType<Button>());
            var input = Assert.Single(root.GetVisualDescendants().OfType<TextBox>());
            try
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await entered.Task.WaitAsync(Bound);
                var pending = loader.WhenActionsIdleAsync(); Assert.False(pending.IsCompleted);
                input.Text = "UI remains usable"; Assert.Equal("UI remains usable", input.Text);
                loader.Dispose(); Assert.True(token.IsCancellationRequested);
                var closing = loader.WhenActionsIdleAsync(); Assert.False(closing.IsCompleted);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(1, calls);
                release.SetResult(); await pending.WaitAsync(Bound); await closing.WaitAsync(Bound);
            }
            finally { release.TrySetResult(); loader.Dispose(); await loader.WhenActionsIdleAsync().WaitAsync(Bound); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Invalid_event_load_is_rejected_before_retiring_the_previous_valid_action_tree()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            var calls = 0; using var loader = new CuiControlLoader();
            loader.SetActionDispatcher(new Dispatcher((_, _, _) => { calls++; return ValueTask.CompletedTask; }));
            var (root, diagnostics) = loader.LoadMarkup("<Page><Button Text=\"Keep\" Action=\"Keep\"/></Page>");
            Assert.Empty(diagnostics); Assert.NotNull(root); loader.WireBindings(root);
            var original = Assert.Single(root.GetVisualDescendants().OfType<Button>());
            var invalid = loader.LoadMarkup("<Page><Button><ActionEvent Type=\"Typo\" Action=\"Never\"/></Button></Page>", "invalid-event.cui");
            Assert.Null(invalid.Root); Assert.Contains(invalid.Diagnostics, d => d.Code == "CUIE001" && d.Span.SourceName == "invalid-event.cui");
            original.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync().WaitAsync(Bound);
            Assert.Equal(1, calls);
        }, CancellationToken.None);
    }

    private sealed class Dispatcher(Func<string, object?, CancellationToken, ValueTask> callback) : ICuiActionDispatcher
    {
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) => callback(command, parameter, cancellationToken);
    }

    [Fact]
    public async Task Definition_instance_cannot_silently_replace_a_competing_direct_action()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var result = loader.LoadMarkup("<Cui><Button (Definition) ID=\"SaveButton\" Text=\"Save\"><ActionEvent Type=\"Clicked\" Action=\"Original\"/></Button><Page><SaveButton Action=\"Competing\"/></Page></Cui>");
            Assert.Null(result.Root);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIE004");
        }, CancellationToken.None);
    }
}
