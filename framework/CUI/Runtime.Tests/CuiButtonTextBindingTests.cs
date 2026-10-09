using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CakeOS.Cui;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiButtonTextBindingTests
{
    [Fact]
    public async Task Button_text_literals_and_live_labels_use_native_content_without_changing_explicit_content_or_text_fields()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var model = new CuiViewModel();
            model.Set("Label", "First label"); model.Set("ContentLabel", "Explicit content");
            using var loader = new CuiControlLoader(); loader.SetBindingContext(model);
            var (tree, diagnostics) = loader.LoadMarkup("""
                <Cui><Page>
                  <Button Text="Literal label" />
                  <Button Text="{Binding Label}" />
                  <Button Content="{Binding ContentLabel}" />
                  <TextBlock Text="{Binding Label}" />
                  <TextBox Text="{Binding Label}" />
                </Page></Cui>
                """);
            Assert.Empty(diagnostics);
            var root = Assert.IsAssignableFrom<Panel>(tree);
            var literal = Assert.IsType<Button>(root.Children[0]);
            var live = Assert.IsType<Button>(root.Children[1]);
            var content = Assert.IsType<Button>(root.Children[2]);
            var text = Assert.IsType<TextBlock>(root.Children[3]);
            var input = Assert.IsType<TextBox>(root.Children[4]);
            var window = new Window { Width = 700, Height = 420, Content = root };
            try
            {
                window.Show(); window.UpdateLayout();
                Assert.Equal("Literal label", literal.Content);
                Assert.Equal("First label", live.Content);
                Assert.Equal("Explicit content", content.Content);
                Assert.Equal("First label", text.Text); Assert.Equal("First label", input.Text);
                model.Set("Label", "Current label"); model.Set("ContentLabel", "Current explicit content");
                window.UpdateLayout();
                Assert.Same(live, root.Children[1]); Assert.Same(content, root.Children[2]);
                Assert.Equal("Literal label", literal.Content);
                Assert.Equal("Current label", live.Content);
                Assert.Equal("Current explicit content", content.Content);
                Assert.Equal("Current label", text.Text); Assert.Equal("Current label", input.Text);
                Assert.True(live.Bounds.Width > 0 && live.Bounds.Height > 0);
            }
            finally { window.Close(); loader.Dispose(); await loader.WhenActionsIdleAsync(); }
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Same_key_repeat_refresh_updates_text_and_explicit_content_on_the_same_native_buttons()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var model = new CuiViewModel(); var first = Row("stable", "First label", "First explicit content");
            model.Set("Rows", new[] { first });
            using var loader = new CuiControlLoader(); loader.SetBindingContext(model);
            var (tree, diagnostics) = loader.LoadMarkup("""
                <Cui><Page><Repeat Source="{Binding Rows}" As="row" Key="{Binding row.Id}">
                  <Button Text="{Binding row.Label}" />
                  <Button Content="{Binding row.ContentLabel}" />
                </Repeat></Page></Cui>
                """);
            Assert.Empty(diagnostics); var root = Assert.IsAssignableFrom<Panel>(tree);
            var window = new Window { Width = 700, Height = 420, Content = root };
            try
            {
                window.Show(); window.UpdateLayout();
                var buttons = root.GetVisualDescendants().OfType<Button>().ToArray(); Assert.Equal(2, buttons.Length);
                Assert.Equal("First label", buttons[0].Content); Assert.Equal("First explicit content", buttons[1].Content);
                var identity = CuiRuntimeIdentity.GetStableId(buttons[0]);
                model.Set("Rows", new[] { Row("stable", "Current label", "Current explicit content") });
                window.UpdateLayout();
                var current = root.GetVisualDescendants().OfType<Button>().ToArray(); Assert.Equal(2, current.Length);
                Assert.Same(buttons[0], current[0]); Assert.Same(buttons[1], current[1]);
                Assert.Equal(identity, CuiRuntimeIdentity.GetStableId(current[0]));
                Assert.Equal("Current label", current[0].Content); Assert.Equal("Current explicit content", current[1].Content);
                Assert.True(current[0].Bounds.Width > 0 && current[0].Bounds.Height > 0);
            }
            finally { window.Close(); loader.Dispose(); await loader.WhenActionsIdleAsync(); }
            return true;
        }, CancellationToken.None));
    }

    private static Dictionary<string, object?> Row(string id, string label, string content) =>
        new() { ["Id"] = id, ["Label"] = label, ["ContentLabel"] = content };
}
