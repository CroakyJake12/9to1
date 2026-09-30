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
    private sealed class Ready : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken ct) => ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "ready", "Ready"));
    }
}
