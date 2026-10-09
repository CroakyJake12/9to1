using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CakeOS.Cui;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiObjectTextBindingTests
{
    [Fact]
    public async Task Registered_object_explicit_text_port_receives_initial_and_live_text_without_a_reflective_setter()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var model = new CuiViewModel(); model.Set("Message", "Original message");
            var registry = new CuiControlRegistry();
            registry.RegisterObjectRenderer("TypedMessage", _ => new MessageView());
            registry.RegisterObjectRenderer("UnqualifiedMessage", _ => new UnqualifiedView());
            using var loader = new CuiControlLoader(registry); loader.SetBindingContext(model);
            var (tree, diagnostics) = loader.LoadMarkup("""
                <Cui><Page>
                  <Object Type="TypedMessage" Text="{Binding Message}" />
                  <Object Type="TypedMessage" Text="Literal message" />
                  <Object Type="UnqualifiedMessage" Text="{Binding Message}" />
                </Page></Cui>
                """);
            Assert.Empty(diagnostics); var root = Assert.IsAssignableFrom<Panel>(tree); loader.WireBindings(root);
            var typed = Assert.IsType<MessageView>(root.Children[0]);
            var literal = Assert.IsType<MessageView>(root.Children[1]);
            var unqualified = Assert.IsType<UnqualifiedView>(root.Children[2]);
            var window = new Window { Width = 600, Height = 400, Content = root };
            try
            {
                window.Show(); window.UpdateLayout();
                Assert.Equal("Original message", typed.Text); Assert.Equal(typed.Text, typed.Label.Text);
                Assert.Equal("Literal message", literal.Text); Assert.Equal("Untouched", unqualified.Text);
                model.Set("Message", "Current message"); window.UpdateLayout();
                Assert.Same(typed, root.Children[0]); Assert.Equal("Current message", typed.Text);
                Assert.Equal(typed.Text, typed.Label.Text); Assert.Equal("Literal message", literal.Text);
                Assert.Equal("Untouched", unqualified.Text);
                Assert.True(typed.Label.Bounds.Width > 0 && typed.Label.Bounds.Height > 0);
                loader.Dispose(); await loader.WhenActionsIdleAsync();
                model.Set("Message", "After close"); Assert.Equal("Current message", typed.Text);
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); window.Close(); }
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Repeated_registered_object_keeps_its_native_identity_and_updates_text_from_the_current_row()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var model = new CuiViewModel();
            model.Set("Rows", new[] { Row("first", "First message"), Row("second", "Second message") });
            var registry = new CuiControlRegistry(); registry.RegisterObjectRenderer("TypedMessage", _ => new MessageView());
            using var loader = new CuiControlLoader(registry); loader.SetBindingContext(model);
            var (tree, diagnostics) = loader.LoadMarkup("""
                <Cui><Page><Repeat Source="{Binding Rows}" As="message" Key="{Binding message.Id}">
                  <Object Type="TypedMessage" Text="{Binding message.Content}" />
                </Repeat></Page></Cui>
                """);
            Assert.Empty(diagnostics); var root = Assert.IsAssignableFrom<Panel>(tree); loader.WireBindings(root);
            var window = new Window { Width = 600, Height = 400, Content = root };
            try
            {
                window.Show(); window.UpdateLayout();
                var views = root.GetVisualDescendants().OfType<MessageView>().ToArray(); Assert.Equal(2, views.Length);
                Assert.Equal("First message", views[0].Text); Assert.Equal("Second message", views[1].Text);
                var firstIdentity = CuiRuntimeIdentity.GetStableId(views[0]);
                var secondIdentity = CuiRuntimeIdentity.GetStableId(views[1]); Assert.NotEqual(firstIdentity, secondIdentity);
                var current = Row("second", "Changed current message");
                model.Set("Rows", new[] { Row("first", "First message"), current }); window.UpdateLayout();
                var refreshed = root.GetVisualDescendants().OfType<MessageView>().ToArray(); Assert.Equal(2, refreshed.Length);
                Assert.Same(views[0], refreshed[0]); Assert.Same(views[1], refreshed[1]);
                Assert.Equal(firstIdentity, CuiRuntimeIdentity.GetStableId(refreshed[0]));
                Assert.Equal(secondIdentity, CuiRuntimeIdentity.GetStableId(refreshed[1]));
                Assert.Equal("Changed current message", refreshed[1].Text);
                Assert.Equal(refreshed[1].Text, refreshed[1].Label.Text);
                var scope = Assert.IsAssignableFrom<ICuiBindingContext>(refreshed[1].DataContext);
                Assert.True(scope.TryGetValue("message", out var row)); Assert.Same(current, row);
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); window.Close(); }
            return true;
        }, CancellationToken.None));
    }

    private static Dictionary<string, object?> Row(string id, string content) => new() { ["Id"] = id, ["Content"] = content };
    private sealed class MessageView : UserControl, ICuiTextBindingTarget
    {
        internal TextBlock Label { get; } = new();
        internal MessageView() => Content = Label;
        public string Text { get => Label.Text ?? ""; set => Label.Text = value; }
    }
    private sealed class UnqualifiedView : UserControl
    {
        public string Text { get; set; } = "Untouched";
    }
}
