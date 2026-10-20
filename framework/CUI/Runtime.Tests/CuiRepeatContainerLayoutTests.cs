using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Pins the canonical generic Repeat composition repair: a Repeat directly under a Container is a
/// keyed generated CHILD RANGE of that Container, so each emitted item participates in the
/// Container's declared Wrap/Horizontal Stack/Vertical Stack/Grid layout instead of a hidden
/// vertical sub-panel. These assert real arranged layout (multi-row wrap, distinct X on a row,
/// bounded hit targets) and stable keyed identity across add/remove/reorder, plus the explicit
/// legacy Panel= compatibility escape.
/// </summary>
public sealed class CuiRepeatContainerLayoutTests
{
    private const string WrapMarkup = """
        <Cui>
          <Container id="wrap" Type="Wrap" Width="200" CellGap="6">
            <Button id="before" Content="B" Width="50" Height="24" />
            <Repeat Source="{Binding Items}" As="it" Key="{Binding it.Id}">
              <Button Content="{Binding it.Label}" Width="50" Height="24" />
            </Repeat>
            <Button id="after" Content="A" Width="50" Height="24" />
          </Container>
        </Cui>
        """;

    private static Dictionary<string, object?> Item(string id, string label) =>
        new() { ["Id"] = id, ["Label"] = label };

    private static Point Pos(Control control, Control ancestor) =>
        control.TranslatePoint(new Point(0, 0), ancestor) ?? default;

    private static Control? FindContainer(Control root, string id) =>
        root.GetVisualDescendants().OfType<Control>().SingleOrDefault(c =>
            AutomationProperties.GetAutomationId(c) == id);

    private static Button[] ItemButtons(Control root)
    {
        var all = root.GetVisualDescendants().OfType<Button>().ToArray();
        return all.Where(b => b.Content is string && b.Content is not ("B" or "A")).ToArray();
    }

    [Fact]
    public async Task Repeat_items_flow_into_the_owning_wrap_as_individual_cells_with_static_siblings()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var model = new CuiViewModel();
            // Eight fixed-width (50px) cells at 200px wrap width + 6px gaps -> 3 per row, so rows
            // 0-2, 3-5, 6-7 wrap deterministically without depending on font metrics.
            model.Set("Items", new[]
            {
                Item("1", "1"), Item("2", "2"), Item("3", "3"),
                Item("4", "4"), Item("5", "5"), Item("6", "6"),
                Item("7", "7"), Item("8", "8")
            });
            using var loader = new CuiControlLoader(); loader.SetBindingContext(model);
            var loaded = loader.LoadMarkup(WrapMarkup);
            Assert.Empty(loaded.Diagnostics); var root = loaded.Root!; loader.WireBindings(root);
            var window = new Window { Width = 400, Height = 300, Content = root };
            window.Show(); window.UpdateLayout();
            try
            {
                var wrap = FindContainer(root, "wrap");
                Assert.NotNull(wrap);
                var before = Assert.Single(root.GetVisualDescendants().OfType<Button>(), b => b.Content is "B");
                var after = Assert.Single(root.GetVisualDescendants().OfType<Button>(), b => b.Content is "A");
                var items = ItemButtons(root);
                Assert.Equal(8, items.Length);

                // Static siblings participate in the same owning layout and keep their positions.
                var beforePos = Pos(before, wrap!);
                var firstPos = Pos(items[0], wrap!);
                Assert.True(beforePos.X < firstPos.X, "static 'before' must precede the first repeat cell");
                var afterPos = Pos(after, wrap!);
                var lastPos = Pos(items[7], wrap!);
                Assert.True(afterPos.X > lastPos.X || afterPos.Y > lastPos.Y, "static 'after' must follow the repeat range");

                // First row: distinct X, same Y.
                var p0 = Pos(items[0], wrap!); var p1 = Pos(items[1], wrap!); var p2 = Pos(items[2], wrap!);
                Assert.True(p1.X > p0.X, "second cell must sit right of the first on the same row");
                Assert.True(p2.X > p1.X, "third cell must sit right of the second on the same row");
                Assert.True(Math.Abs(p0.Y - p1.Y) < 1 && Math.Abs(p1.Y - p2.Y) < 1, "first three cells share one row");

                // Wrapping: row 2 begins below row 1 and restarts toward the left edge.
                var p3 = Pos(items[3], wrap!);
                Assert.True(p3.Y > p2.Y, "later item must wrap to a new row (multi-column grid)");
                Assert.True(p3.X < p0.X, "wrapped row should restart toward the left edge");

                // Real hit targets: each button's arranged bounds contain its centre and are hit-testable.
                foreach (var item in items)
                {
                    var rect = new Rect(item.TranslatePoint(new Point(0, 0), wrap!) ?? default,
                        item.Bounds.Size);
                    Assert.True(rect.Contains(rect.Center), $"button '{item.Content}' bounds must contain its centre");
                    Assert.True(item.IsHitTestVisible);
                }
            }
            finally { window.Close(); loader.Dispose(); }
        }, lifetime.Token);
    }

    [Fact]
    public async Task Keyed_add_remove_reorder_preserves_item_identity_and_wrap_positions()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var model = new CuiViewModel();
            model.Set("Items", new[] { Item("1", "1"), Item("2", "2"), Item("3", "3") });
            using var loader = new CuiControlLoader(); loader.SetBindingContext(model);
            var loaded = loader.LoadMarkup(WrapMarkup);
            Assert.Empty(loaded.Diagnostics); var root = loaded.Root!; loader.WireBindings(root);
            var window = new Window { Width = 400, Height = 300, Content = root };
            window.Show(); window.UpdateLayout();
            try
            {
                var before = ItemButtons(root); Assert.Equal(3, before.Length);

                // Reorder: same keyed controls must be reused (same instance), not rebuilt.
                model.Set("Items", new[] { Item("3", "3"), Item("1", "1"), Item("2", "2") });
                window.UpdateLayout();
                var reordered = ItemButtons(root);
                Assert.Equal(3, reordered.Length);
                Assert.Equal("3", Assert.IsType<string>(reordered[0].Content));
                Assert.Equal("1", Assert.IsType<string>(reordered[1].Content));
                Assert.Equal("2", Assert.IsType<string>(reordered[2].Content));
                Assert.Contains(before[2], reordered);
                Assert.Contains(before[0], reordered);
                Assert.Contains(before[1], reordered);

                // Remove one key and add a fresh key.
                model.Set("Items", new[] { Item("3", "3"), Item("4", "4"), Item("1", "1") });
                window.UpdateLayout();
                var edited = ItemButtons(root);
                Assert.Equal(3, edited.Length);
                Assert.Equal("4", Assert.IsType<string>(edited[1].Content));
                Assert.Contains(before[2], edited); // "3" survives
                Assert.Contains(before[0], edited); // "1" survives
                Assert.DoesNotContain(before[1], edited); // "2" removed

                // Positions still reflect the owning wrap after reorder.
                var wrap = FindContainer(root, "wrap"); Assert.NotNull(wrap);
                var p0 = Pos(edited[0], wrap!); var p1 = Pos(edited[1], wrap!);
                Assert.True(p1.X > p0.X);
            }
            finally { window.Close(); loader.Dispose(); }
        }, lifetime.Token);
    }

    [Fact]
    public async Task Two_repeat_ranges_with_static_siblings_all_emit_into_the_owning_container()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            const string markup = """
                <Cui>
                  <Container id="host" Type="Vertical Stack" Width="200" CellGap="4">
                    <Button id="s1" Content="S1" Width="50" Height="20" />
                    <Repeat Source="{Binding First}" As="a" Key="{Binding a.Id}"><Button Content="{Binding a.Label}" Width="50" Height="20" /></Repeat>
                    <Button id="middle" Content="MID" Width="50" Height="20" />
                    <Repeat Source="{Binding Second}" As="b" Key="{Binding b.Id}"><Button Content="{Binding b.Label}" Width="50" Height="20" /></Repeat>
                    <Button id="s2" Content="S2" Width="50" Height="20" />
                  </Container>
                </Cui>
                """;
            var model = new CuiViewModel();
            model.Set("First", new[] { Item("a1", "a1"), Item("a2", "a2") });
            model.Set("Second", new[] { Item("b1", "b1") });
            using var loader = new CuiControlLoader(); loader.SetBindingContext(model);
            var loaded = loader.LoadMarkup(markup);
            Assert.Empty(loaded.Diagnostics); var root = loaded.Root!; loader.WireBindings(root);
            var window = new Window { Width = 400, Height = 300, Content = root };
            window.Show(); window.UpdateLayout();
            try
            {
                var host = FindContainer(root, "host"); Assert.NotNull(host);
                var visible = root.GetVisualDescendants().OfType<Button>().Select(b => b.Content?.ToString()).ToArray();
                Assert.Equal(new[] { "S1", "a1", "a2", "MID", "b1", "S2" }, visible);

                // Static siblings and both ranges share one authored order in the owning container.
                var s1 = Assert.Single(root.GetVisualDescendants().OfType<Button>(), b => b.Content is "S1");
                var mid = Assert.Single(root.GetVisualDescendants().OfType<Button>(), b => b.Content is "MID");
                var s2 = Assert.Single(root.GetVisualDescendants().OfType<Button>(), b => b.Content is "S2");
                Assert.True(Pos(s1, host!).Y < Pos(mid, host!).Y);
                Assert.True(Pos(mid, host!).Y < Pos(s2, host!).Y);
            }
            finally { window.Close(); loader.Dispose(); }
        }, lifetime.Token);
    }

    [Fact]
    public async Task Explicit_legacy_panel_attribute_keeps_the_nested_host_panel_compatibility_escape()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            const string markup = """
                <Cui>
                  <Container id="host" Type="Vertical Stack" Width="200" CellGap="4">
                    <Repeat Source="{Binding Items}" As="it" Key="{Binding it.Id}" Panel="Horizontal Stack">
                      <Button Content="{Binding it.Label}" Width="40" Height="20" />
                    </Repeat>
                  </Container>
                </Cui>
                """;
            var model = new CuiViewModel();
            model.Set("Items", new[] { Item("1", "1"), Item("2", "2"), Item("3", "3") });
            using var loader = new CuiControlLoader(); loader.SetBindingContext(model);
            var loaded = loader.LoadMarkup(markup);
            Assert.Empty(loaded.Diagnostics); var root = loaded.Root!; loader.WireBindings(root);
            var window = new Window { Width = 400, Height = 300, Content = root };
            window.Show(); window.UpdateLayout();
            try
            {
                var host = FindContainer(root, "host"); Assert.NotNull(host);
                var items = ItemButtons(root); Assert.Equal(3, items.Length);
                // Explicit Panel= preserves the historical "one host panel" grouping: all items lay
                // out on a single horizontal row regardless of the owning vertical-stack container.
                var p0 = Pos(items[0], host!); var p1 = Pos(items[1], host!); var p2 = Pos(items[2], host!);
                Assert.True(Math.Abs(p0.Y - p1.Y) < 1 && Math.Abs(p1.Y - p2.Y) < 1,
                    "legacy Panel=Horizontal Stack must keep one horizontal row (not the container layout)");
                Assert.True(p1.X > p0.X && p2.X > p1.X);
            }
            finally { window.Close(); loader.Dispose(); }
        }, lifetime.Token);
    }
}
