using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Pins CUI's labelled command group - the construct the office notebook bar is built from (the Stage A
/// mirrors measured 4,046 donor ribbon groups). The assertions are about measured behaviour: the authored
/// caption renders and is not counted as an item, the command row collapses exactly what does not fit, and
/// declaring GroupLabel where the construct cannot honour it is an explicit error rather than a silently
/// ignored attribute.
/// </summary>
public sealed class CuiRibbonGroupTests
{
    private const string WideMarkup = """
        <Cui>
          <Container id="group" Type="Ribbon Group" Width="320" GroupLabel="Clipboard" CellGap="4">
            <Button id="cut" Content="Cut" Width="50" Height="24" />
            <Button id="copy" Content="Copy" Width="50" Height="24" />
            <Button id="paste" Content="Paste" Width="50" Height="24" />
            <Button id="paint" Content="Paint" Width="50" Height="24" />
            <Container id="more" Type="Menu" OverflowHost="true" Width="30" Height="24" />
          </Container>
        </Cui>
        """;

    private const string NarrowMarkup = """
        <Cui>
          <Container id="group" Type="Ribbon Group" Width="140" GroupLabel="Clipboard" CellGap="4">
            <Button id="cut" Content="Cut" Width="50" Height="24" />
            <Button id="copy" Content="Copy" Width="50" Height="24" />
            <Button id="paste" Content="Paste" Width="50" Height="24" />
            <Button id="paint" Content="Paint" Width="50" Height="24" />
            <Container id="more" Type="Menu" OverflowHost="true" Width="30" Height="24" />
          </Container>
        </Cui>
        """;

    private static Control? Find(Control root, string id) =>
        root.GetSelfAndVisualDescendants().OfType<Control>().SingleOrDefault(c =>
            AutomationProperties.GetAutomationId(c) == id);

    private static (Control Root, CuiControlLoader Loader) Load(string markup, Window window)
    {
        var loader = new CuiControlLoader();
        var loaded = loader.LoadMarkup(markup);
        Assert.Empty(loaded.Diagnostics);
        var root = loaded.Root!;
        loader.WireBindings(root);
        window.Content = root;
        window.Show();
        window.UpdateLayout();
        return (root, loader);
    }

    [Fact]
    public async Task GroupLabel_renders_as_a_caption_and_is_not_counted_as_a_command_item()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var window = new Window { Width = 420, Height = 200 };
            var (root, loader) = Load(WideMarkup, window);
            using (loader)
            {
                var group = (CuiContainerControl)Find(root, "group")!;
                var panel = (CuiRibbonGroupPanel)group.Layout;
                Assert.Equal("Clipboard", panel.Label);
                Assert.Equal("Clipboard", panel.CaptionElement.Text);
                Assert.True(panel.CaptionElement.IsVisible);
                Assert.Equal(4, panel.VisibleItemCount);
                Assert.Equal(0, panel.HiddenItemCount);
                Assert.False(Find(root, "more")!.IsEffectivelyVisible, "no overflow when everything fits");

                // the caption sits below the command row, not inside it
                var cut = Find(root, "cut")!;
                Assert.True(panel.CaptionElement.Bounds.Y >= cut.Bounds.Y + cut.Bounds.Height - 0.5,
                            "the caption must sit under the command row rather than become another item");
                Assert.True(group.Bounds.Height > cut.Bounds.Height,
                            "the group is taller than its row because it carries a caption");
                Assert.NotNull(panel.RuleElement.Fill);
            }
        }, lifetime.Token);
    }

    [Fact]
    public async Task A_narrow_group_collapses_exactly_the_commands_that_do_not_fit_and_shows_its_overflow_host()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var window = new Window { Width = 200, Height = 200 };
            var (root, loader) = Load(NarrowMarkup, window);
            using (loader)
            {
                var group = (CuiContainerControl)Find(root, "group")!;
                var panel = (CuiRibbonGroupPanel)group.Layout;
                // 140 available; two 50-wide commands + 4 gap = 104, host 30 reserved: 104 + 4 + 30 = 138.
                Assert.Equal(2, panel.VisibleItemCount);
                Assert.Equal(2, panel.HiddenItemCount);
                Assert.True(Find(root, "cut")!.IsEffectivelyVisible);
                Assert.True(Find(root, "copy")!.IsEffectivelyVisible);
                Assert.False(Find(root, "paste")!.IsEffectivelyVisible);
                Assert.False(Find(root, "paint")!.IsEffectivelyVisible);
                Assert.True(Find(root, "more")!.IsEffectivelyVisible, "overflow appears once items collapse");
                Assert.True(group.Bounds.Width <= 140.5, "the group never exceeds the width it was given");
                Assert.True(panel.CaptionElement.IsVisible, "collapsing must not drop the group caption");
            }
        }, lifetime.Token);
    }

    [Fact]
    public void GroupLabel_on_a_container_that_cannot_honour_it_is_an_explicit_error()
    {
        var stack = new CuiContainerControl("Vertical Stack");
        var exception = Assert.Throws<InvalidOperationException>(() => stack.SetGroupLabel("Clipboard"));
        Assert.Contains("GroupLabel is not meaningful", exception.Message);
        Assert.Contains("Vertical Stack", exception.Message);
        stack.Dispose();
    }

    [Fact]
    public void Ribbon_group_is_a_registered_type_and_GroupLabel_is_a_registered_typed_property()
    {
        Assert.Contains("Ribbon Group", CuiControlRegistry.TypesFor("Container"));
        var registry = new CuiControlRegistry();
        Assert.True(registry.TryGetProperty("GroupLabel", out var property));
        Assert.Equal("text", property.LanguageType);
        Assert.Contains("Container", property.SupportedElementTypes);
    }
}
