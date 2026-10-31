using System.Threading;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Pins the two CUI constructs the Stage A donor mirrors could only approximate: an authored Toolbar with
/// REAL measured overflow (7,401 donor instances had nowhere to go) and a first-class Separator (1,626).
/// These assert measured layout - which item is visible, where survivors sit, that the overflow host
/// appears only when something actually collapsed - not parsing or styling.
/// </summary>
public sealed class CuiToolbarOverflowAndSeparatorTests
{
    private const string ToolbarMarkup = """
        <Cui>
          <Container id="toolbar" Type="Toolbar" Width="220" CellGap="4">
            <Button id="one" Content="1" Width="50" Height="24" />
            <Button id="two" Content="2" Width="50" Height="24" />
            <Button id="three" Content="3" Width="50" Height="24" />
            <Button id="four" Content="4" Width="50" Height="24" />
            <Container id="more" Type="Menu" OverflowHost="true" Width="30" Height="24">
              <Button id="hidden-item" Content="H" Width="50" Height="24" />
            </Container>
          </Container>
        </Cui>
        """;

    private const string SeparatorMarkup = """
        <Cui>
          <Container id="column" Type="Vertical Stack" Width="200">
            <Button id="above" Content="A" Width="120" Height="24" />
            <Container id="rule" Type="Separator" Width="200" />
            <Button id="below" Content="B" Width="120" Height="24" />
          </Container>
        </Cui>
        """;

    private static Control? Find(Control root, string id) =>
        root.GetSelfAndVisualDescendants().OfType<Control>().SingleOrDefault(c =>
            AutomationProperties.GetAutomationId(c) == id);

    private static Point Pos(Control control, Control ancestor) =>
        control.TranslatePoint(new Point(0, 0), ancestor) ?? default;

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
    public async Task Toolbar_with_room_for_everything_shows_every_item_and_presents_no_overflow_host()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var window = new Window { Width = 600, Height = 200 };
            var (root, loader) = Load(ToolbarMarkup, window);
            using (loader)
            {
                var toolbar = (CuiContainerControl)Find(root, "toolbar")!;
                var panel = (CuiToolbarPanel)toolbar.Layout;
                Assert.Equal(4, panel.VisibleItemCount);
                Assert.Equal(0, panel.HiddenItemCount);
                // Visibility is asserted EFFECTIVELY: the toolbar collapses the layout cell, and the
                // authored control keeps its own declared state (pinned below).
                Assert.False(Find(root, "more")!.IsEffectivelyVisible, "no overflow control when nothing collapsed");
                foreach (var id in new[] { "one", "two", "three", "four" })
                    Assert.True(Find(root, id)!.IsEffectivelyVisible,
                                $"{id} must be visible when the toolbar has room");
            }
        }, lifetime.Token);
    }

    [Fact]
    public async Task Narrow_toolbar_collapses_exactly_the_items_that_do_not_fit_and_shows_the_authored_host()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            // Same document at a width that cannot hold four 50-wide items: 140 available, host 30 wide.
            var window = new Window { Width = 160, Height = 200 };
            var (root, loader) = Load(ToolbarMarkup.Replace("Width=\"220\"", "Width=\"140\""), window);
            using (loader)
            {
                var toolbar = (CuiContainerControl)Find(root, "toolbar")!;
                var panel = (CuiToolbarPanel)toolbar.Layout;

                // Two items fit (50 + 4 + 50 = 104) and the 30-wide host is reserved first: 104 + 4 + 30 = 138
                // <= 140, so exactly two items survive and two collapse.
                Assert.Equal(2, panel.VisibleItemCount);
                Assert.Equal(2, panel.HiddenItemCount);
                Assert.True(Find(root, "one")!.IsEffectivelyVisible);
                Assert.True(Find(root, "two")!.IsEffectivelyVisible);
                Assert.False(Find(root, "three")!.IsEffectivelyVisible, "the third item must collapse");
                Assert.False(Find(root, "four")!.IsEffectivelyVisible, "the fourth item must collapse");
                Assert.True(Find(root, "more")!.IsEffectivelyVisible,
                            "the overflow host must appear once items collapse");

                var x1 = Pos(Find(root, "one")!, toolbar).X;
                var x2 = Pos(Find(root, "two")!, toolbar).X;
                var more = Find(root, "more")!;
                var hostX = Pos(more, toolbar).X;
                Assert.True(x2 > x1, "visible items stay in document order");
                Assert.True(hostX > x2, "the overflow control sits after the surviving items");
                Assert.True(hostX + more.Bounds.Width <= toolbar.Bounds.Width + 0.5,
                            "the overflow control must fit inside the measured toolbar width");
                Assert.True(panel.HiddenItems.Count == 2, "the panel reports which cells it collapsed");
            }
        }, lifetime.Token);
    }

    [Fact]
    public async Task Collapsing_a_toolbar_hides_the_cell_without_rewriting_authored_visibility()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var window = new Window { Width = 160, Height = 200 };
            var (root, loader) = Load(ToolbarMarkup.Replace("Width=\"220\"", "Width=\"140\""), window);
            using (loader)
            {
                var hidden = Find(root, "three")!;
                // The contract this construct is built on: overflow hides the layout CELL and never
                // rewrites the authored control, so moving that same control into the overflow menu
                // later needs no authored-state repair.
                Assert.False(hidden.IsEffectivelyVisible, "the item must not render in a narrow toolbar");
                Assert.True(hidden.IsVisible, "overflow must not rewrite the authored control's own IsVisible");
                var store = CuiPropertyPresentation.For(hidden);
                Assert.True(store.Resolve("IsVisible").Value is not false,
                            "the authored property store must stay clean too");
            }
        }, lifetime.Token);
    }

    [Fact]
    public async Task Separator_takes_the_span_it_is_offered_and_only_its_thickness_across()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var window = new Window { Width = 400, Height = 300 };
            var (root, loader) = Load(SeparatorMarkup, window);
            using (loader)
            {
                var rule = (CuiContainerControl)Find(root, "rule")!;
                Assert.Equal("Separator", rule.LayoutType);
                Assert.IsType<CuiSeparatorPanel>(rule.Layout);
                Assert.True(rule.Bounds.Width >= 100, "the rule spans the width it is given");
                Assert.True(rule.Bounds.Height is >= 1 and <= 8,
                            "across the span the separator contributes only its thickness");

                var above = Find(root, "above")!;
                var below = Find(root, "below")!;
                var aboveY = Pos(above, root).Y;
                var belowY = Pos(below, root).Y;
                var ruleY = Pos(rule, root).Y;
                Assert.True(aboveY < ruleY && ruleY < belowY, "the separator sits between what it separates");

                // The rule element is painted from the theme line brush, never a hard-coded colour.
                var panel = (CuiSeparatorPanel)rule.Layout;
                Assert.NotNull(panel.Rule.Fill);
            }
        }, lifetime.Token);
    }

    [Fact]
    public async Task A_container_type_that_is_not_registered_is_still_rejected()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var loader = new CuiControlLoader();
            var loaded = loader.LoadMarkup("""<Cui><Container id="x" Type="Donut" /></Cui>""");
            // The loader reports an unregistered Container Type as CUIR001 at load time; the compiler
// reports the same class of problem as CUIC017 at compile time. Both must stay refused.
Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Code == "CUIR001");
            loader.Dispose();
        }, lifetime.Token);
    }

    [Fact]
    public void Toolbar_and_separator_are_registered_types_and_overflow_host_is_a_typed_property()
    {
        var types = CuiControlRegistry.TypesFor("Container");
        Assert.Contains("Toolbar", types);
        Assert.Contains("Separator", types);
        var registry = new CuiControlRegistry();
        Assert.True(registry.TryGetProperty("OverflowHost", out var property));
        Assert.Equal("boolean", property.LanguageType);
        Assert.Contains("Container", property.SupportedElementTypes);
    }
}
