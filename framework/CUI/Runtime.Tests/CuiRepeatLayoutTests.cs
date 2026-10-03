using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

[Collection("CuiNativeBackend")]
public sealed class CuiRepeatLayoutTests
{
    [Theory]
    [InlineData("Vertical")]
    [InlineData("Horizontal")]
    public async Task Repeated_items_have_distinct_layout_and_live_enabled_state(string orientation)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch(async () =>
        {
            var model = new CuiViewModel();
            model.Set("Items", new ObservableCollection<Dictionary<string, object?>>
            { new() { ["ID"] = "one" }, new() { ["ID"] = "two" } });
            model.Set("Enabled", false);
            using var host = new CuiSceneHost();
            await host.ShowAsync(new("layout", "Layout", "Home", new CuiRichParser().Parse(
                $"<Cui><Repeat Source=\"{{Binding Items}}\" As=\"item\" Key=\"{{Binding item.ID}}\" orientation=\"{orientation}\" spacing=\"8\"><Button width=\"80\" height=\"30\" IsEnabled=\"{{Binding Enabled}}\" content=\"Entry\" /></Repeat></Cui>"), model, model, new Ready()));
            var repeat = Assert.IsType<StackPanel>(host.Content);
            repeat.Measure(new Size(400, 400)); repeat.Arrange(new Rect(0, 0, 400, 400));
            Assert.Equal(Enum.Parse<Orientation>(orientation), repeat.Orientation);
            var first = repeat.Children[0]; var second = repeat.Children[1];
            if (orientation == "Vertical") Assert.True(second.Bounds.Top >= first.Bounds.Bottom + 8);
            else Assert.True(second.Bounds.Left >= first.Bounds.Right + 8);
            var button = Assert.IsType<Button>(Assert.Single(Assert.IsType<Panel>(first).Children));
            Assert.False(button.IsEnabled);
            model.Set("Enabled", true); Assert.True(button.IsEnabled);
            return true;
        }, default);
    }
    [Fact]
    public async Task Grid_repeat_places_stable_wrappers_and_updates_bound_coordinates()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch(async () =>
        {
            var model = new CuiViewModel();
            var items = new ObservableCollection<Dictionary<string, object?>>
            { new() { ["ID"] = "one", ["Row"] = 0, ["Column"] = 0 }, new() { ["ID"] = "two", ["Row"] = 1, ["Column"] = 1 } };
            model.Set("Items", items);
            using var host = new CuiSceneHost();
            await host.ShowAsync(new("grid", "Grid", "Home", new CuiRichParser().Parse(
                "<Cui><Repeat Panel=\"Grid\" rows=\"*,*\" columns=\"*,*\" Source=\"{Binding Items}\" As=\"item\" Key=\"{Binding item.ID}\" ItemRow=\"{Binding item.Row}\" ItemColumn=\"{Binding item.Column}\"><Button content=\"Entry\" /></Repeat></Cui>"), model, model, new Ready()));
            var grid = Assert.IsType<Grid>(host.Content);
            grid.Measure(new Size(400, 200)); grid.Arrange(new Rect(0, 0, 400, 200));
            var second = grid.Children[1];
            Assert.Equal(1, Grid.GetRow(second)); Assert.Equal(1, Grid.GetColumn(second));
            Assert.Equal(200, second.Bounds.Left); Assert.Equal(100, second.Bounds.Top);
            items[1] = new() { ["ID"] = "two", ["Row"] = 0, ["Column"] = 1 };
            Assert.Same(second, grid.Children[1]); Assert.Equal(0, Grid.GetRow(second));
            grid.Measure(new Size(400, 200)); grid.Arrange(new Rect(0, 0, 400, 200));
            Assert.Equal(0, second.Bounds.Top);
            return true;
        }, default);
    }
    [Fact]
    public async Task Nested_repeat_collection_changes_retain_captured_parent_scope()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch(async () =>
        {
            var children = new ObservableCollection<Dictionary<string, object?>> { new() { ["ID"] = "one" } };
            var model = new CuiViewModel();
            model.Set("Groups", new[] { new Dictionary<string, object?> { ["ID"] = "group", ["Label"] = "Owner", ["Children"] = children } });
            using var host = new CuiSceneHost();
            await host.ShowAsync(new("nested", "Nested", "Home", new CuiRichParser().Parse(
                "<Cui><Repeat Source=\"{Binding Groups}\" As=\"group\" Key=\"{Binding group.ID}\"><Repeat Source=\"{Binding group.Children}\" As=\"child\" Key=\"{Binding child.ID}\"><Button content=\"{Binding group.Label}\" /></Repeat></Repeat></Cui>"), model, model, new Ready()));
            var outer = Assert.IsType<StackPanel>(host.Content);
            var nested = Assert.IsType<StackPanel>(Assert.Single(Assert.IsType<Panel>(Assert.Single(outer.Children)).Children));
            var retained = nested.Children[0];
            children.Add(new() { ["ID"] = "two" });
            Assert.Equal(2, nested.Children.Count); Assert.Same(retained, nested.Children[0]);
            foreach (var wrapper in nested.Children)
                Assert.Equal("Owner", Assert.IsType<Button>(Assert.Single(Assert.IsType<Panel>(wrapper).Children)).Content);
            Assert.NotEqual(CuiRuntimeIdentity.GetStableId(nested.Children[0]), CuiRuntimeIdentity.GetStableId(nested.Children[1]));
            return true;
        }, default);
    }
    private sealed class Ready : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken ct) => ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "ready", "Ready"));
    }
}
