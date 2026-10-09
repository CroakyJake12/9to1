using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiActionAttributeCaseTests
{
    [Theory]
    [InlineData("action")]
    [InlineData("Action")]
    [InlineData("ACTION")]
    [InlineData("aCtIoN")]
    public async Task Recognized_action_attribute_wires_the_actual_button_and_dispatches_once(string attribute)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        var completed = await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            var dispatcher = new RecordingDispatcher();
            loader.SetActionDispatcher(dispatcher);
            var (root, diagnostics) = loader.LoadMarkup($"<Cui><Page><Button {attribute}=\"picture.rotate\">Rotate</Button></Page></Cui>");
            Assert.Empty(diagnostics);
            var button = Assert.IsType<Button>(Assert.Single(Assert.IsAssignableFrom<Panel>(root).Children));
            loader.WireBindings(root!);
            var observed = Assert.IsType<CuiControlDiagnostics>(loader.Inspect(button));
            Assert.True(observed.DispatcherConnected);
            Assert.True(observed.ActionsWired);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var original = loader.WhenActionsIdleAsync();
            await original;
            Assert.True(original.IsCompletedSuccessfully);
            var call = Assert.Single(dispatcher.Calls);
            Assert.Equal("picture.rotate", call.Command);
            Assert.Null(call.Parameter);
            loader.Dispose();
            await loader.WhenActionsIdleAsync();
            return true;
        }, CancellationToken.None);
        Assert.True(completed);
    }

    [Fact]
    public async Task Named_action_with_recognized_attribute_case_preserves_its_exact_typed_target()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        var completed = await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            var dispatcher = new RecordingDispatcher();
            var target = new object();
            var bindings = new CuiViewModel();
            bindings.Set("Target", target);
            loader.SetBindingContext(bindings);
            loader.SetActionDispatcher(dispatcher);
            var (root, diagnostics) = loader.LoadMarkup("""
                <Cui><Actions><Action Name="Select" Command="picture.select" Parameter="{Binding Target}" /></Actions>
                  <Page><Button Action="Select">Select</Button></Page>
                </Cui>
                """);
            Assert.Empty(diagnostics);
            var button = Assert.IsType<Button>(Assert.Single(Assert.IsAssignableFrom<Panel>(root).Children));
            loader.WireBindings(root!);
            Assert.True(loader.Inspect(button)!.ActionsWired);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await loader.WhenActionsIdleAsync();
            var call = Assert.Single(dispatcher.Calls);
            Assert.Equal("picture.select", call.Command);
            Assert.Same(target, call.Parameter);
            loader.Dispose();
            await loader.WhenActionsIdleAsync();
            return true;
        }, CancellationToken.None);
        Assert.True(completed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Conflicting_action_case_aliases_refuse_before_a_button_can_dispatch(bool onButton)
    {
        using var loader = new CuiControlLoader();
        var dispatcher = new RecordingDispatcher();
        loader.SetActionDispatcher(dispatcher);
        var markup = onButton
            ? "<Cui><Page><Button action=\"picture.rotate\" Action=\"picture.erase\">Run</Button></Page></Cui>"
            : "<Cui><Actions><Action name=\"Run\" command=\"picture.rotate\" Command=\"picture.erase\" /></Actions><Page><Button action=\"Run\">Run</Button></Page></Cui>";
        var (root, diagnostics) = loader.LoadMarkup(markup);
        Assert.Null(root);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error &&
            diagnostic.Message.Contains("repeated", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(dispatcher.Calls);
    }

    private sealed class RecordingDispatcher : ICuiActionDispatcher
    {
        internal List<(string Command, object? Parameter)> Calls { get; } = [];
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            Calls.Add((command, parameter));
            return ValueTask.CompletedTask;
        }
    }
}
