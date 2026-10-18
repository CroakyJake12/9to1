using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiInvalidFontWeightRetentionTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("<Page>\n<Text fontweight=\"Banana\"/></Page>")]
    [InlineData("<Page>\n<Text fOnTwEiGhT=\"Banana\"/></Page>")]
    [InlineData("<Page>\n<Text Font-Weight=\"Banana\"/></Page>")]
    [InlineData("<Page>\n<Text font_weight=\"Banana\"/></Page>")]
    [InlineData("<Page>\n<Text font.weight=\"Banana\"/></Page>")]
    [InlineData("<Cui>\n<Text (Definition) ID=\"Bad\" font-weight=\"Banana\"/><Page><Bad/></Page></Cui>")]
    [InlineData("<Cui>\n<Prefab (Definition) ID=\"Bad\" Type=\"static\"><Text FONT-WEIGHT=\"Banana\"/></Prefab><Page><Bad/></Page></Cui>")]
    [InlineData("<Cui><Page>\n<Theme Name=\"Professional\"><Text fOnT-wEiGhT=\"Banana\"/></Theme></Page></Cui>")]
    public async Task Invalid_effective_property_keeps_pointer_actions_binding_and_draft_then_allows_a_valid_replacement(string invalidMarkup)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await session.Dispatch(async () =>
        {
            var calls = new List<string>();
            var model = new CuiViewModel();
            model.Set("Label", "Before");
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(model);
            loader.SetActionDispatcher(new Actions(calls));
            var first = loader.LoadMarkup("<Page><Container Type=\"Vertical Stack\"><Button ID=\"keep\" Text=\"{Binding Label}\" Action=\"Keep\"/><Input Type=\"Text\"/></Container></Page>");
            Assert.Empty(first.Diagnostics);
            var root = Assert.IsAssignableFrom<Control>(first.Root);
            loader.WireBindings(root);
            var button = Assert.Single(root.GetVisualDescendants().OfType<Button>());
            var input = Assert.Single(root.GetVisualDescendants().OfType<TextBox>());
            var window = new Window { Content = root, Width = 500, Height = 300 };
            try
            {
                window.Show();
                window.UpdateLayout();
                input.Focus();
                window.KeyTextInput("Retain my draft");
                Click(window, button);
                await loader.WhenActionsIdleAsync().WaitAsync(Bound);
                Assert.Equal(new[] { "Keep" }, calls);

                var invalid = loader.LoadMarkup(invalidMarkup, "invalid-weight.cui");
                Assert.Null(invalid.Root);
                Assert.Contains(invalid.Diagnostics, diagnostic => diagnostic.Code == "CUIR053"
                    && diagnostic.Span.SourceName == "invalid-weight.cui" && diagnostic.Span.Start.Line == 2);
                Assert.Same(root, window.Content);
                Click(window, button);
                await loader.WhenActionsIdleAsync().WaitAsync(Bound);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync().WaitAsync(Bound);
                model.Set("Label", "Still live");
                Assert.Equal(new[] { "Keep", "Keep", "Keep" }, calls);
                Assert.Equal("Still live", button.Content);
                Assert.Equal("Retain my draft", input.Text);

                var next = loader.LoadMarkup("<Page><Button ID=\"next\" Text=\"Next\" Font-Weight=\"Bold\" Action=\"Next\"/></Page>");
                Assert.Empty(next.Diagnostics);
                var nextRoot = Assert.IsAssignableFrom<Control>(next.Root);
                loader.WireBindings(nextRoot);
                window.Content = nextRoot;
                var nextButton = Assert.Single(nextRoot.GetVisualDescendants().OfType<Button>());
                Assert.Equal(FontWeight.Bold, nextButton.FontWeight);
                Click(window, nextButton);
                await loader.WhenActionsIdleAsync().WaitAsync(Bound);
                Assert.Equal(new[] { "Keep", "Keep", "Keep", "Next" }, calls);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync().WaitAsync(Bound);
                Assert.Equal(4, calls.Count);
            }
            finally
            {
                loader.Dispose();
                await loader.WhenActionsIdleAsync().WaitAsync(Bound);
                window.Close();
            }
        }, timeout.Token);
    }

    private static void Click(Window window, Button button)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
        Assert.NotNull(point);
        Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0);
        var hit = window.InputHitTest(point.Value) as Visual;
        Assert.True(ReferenceEquals(hit, button) || hit?.GetVisualAncestors().Contains(button) == true);
        window.MouseMove(point.Value);
        window.MouseDown(point.Value, MouseButton.Left);
        window.MouseUp(point.Value, MouseButton.Left);
    }

    private sealed class Actions(List<string> calls) : ICuiActionDispatcher
    {
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            calls.Add(command);
            return ValueTask.CompletedTask;
        }
    }
}
