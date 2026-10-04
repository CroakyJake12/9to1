using CakeOS.Hui.Renderer;
using Haven.UI;
using Haven.UI.Components;
using Xunit;
using Xunit.Abstractions;

namespace CakeOS.Canvas.App.Tests;

public sealed class CanvasRootLayoutTests : IDisposable
{
    private readonly StubCanvasSession _session = new();
    private readonly CanvasController _controller;
    private readonly CanvasRoot _root;
    private readonly ITestOutputHelper _output;

    public CanvasRootLayoutTests(ITestOutputHelper output)
    {
        _output = output;
        _controller = new CanvasController(() => _session);
        _root = new CanvasRoot(_controller, new NullDialogs(), Path.GetTempPath());
    }

    public void Dispose() => _root.Dispose();

    [Fact]
    public void AllChromeRowsReceiveBounds()
    {
        var layout = new HavenLayoutEngine();
        layout.Layout(_root.Page, new HavenSize(1100, 760), HavenPlatform.Windows, new NullMeasure());
        foreach (var name in new[]
        {
            "Canvas.Header", "Canvas.Toolbar", "Canvas.Options", "CanvasViewport",
            "Canvas.Status", "Canvas.Title", "Canvas.Width", "Canvas.ZoomSlider",
        })
        {
            var found = Find(_root.Page, name);
            Assert.True(found is not null, $"missing element {name}");
            _output.WriteLine($"{name}: x={found!.Bounds.X:0} y={found.Bounds.Y:0} w={found.Bounds.Width:0} h={found.Bounds.Height:0}");
        }
        var viewport = Find(_root.Page, "CanvasViewport")!;
        Assert.True(viewport.Bounds.Height > 200, $"viewport too short: {viewport.Bounds.Height}");
        var status = Find(_root.Page, "Canvas.Status")!;
        Assert.True(status.Bounds.Y > viewport.Bounds.Y, "status bar must sit below the viewport");

        var renderer = new HavenSceneRenderer();
        var commands = renderer.Render(_root.Page);
        var byType = commands.GroupBy(c => c.GetType().Name).ToDictionary(g => g.Key, g => g.Count());
        foreach (var kvp in byType.OrderBy(k => k.Key))
            _output.WriteLine($"cmd {kvp.Key}: {kvp.Value}");
        var iconKeys = commands.OfType<HavenIconCommand>().Select(c => c.Key).Distinct().ToList();
        _output.WriteLine("icons: " + string.Join(",", iconKeys));
        Assert.Contains(commands, c => c is HavenIconCommand icon && icon.Key == "undo");
        Assert.Contains(commands, c => c is HavenIconCommand icon && icon.Key == "zoom-out");
    }

    private static HavenElement? Find(HavenElement root, string name)
    {
        if (root.Name == name)
            return root;
        foreach (var child in root.Children)
        {
            var found = Find(child, name);
            if (found is not null)
                return found;
        }
        return null;
    }

    private sealed class NullDialogs : ICanvasFileDialogs
    {
        public Task<string?> PickOpenRnoteAsync(string directory, string title) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveRnoteAsync(string directory, string suggestedName, string title) => Task.FromResult<string?>(null);
    }

    private sealed class NullMeasure : IHavenMeasureContext
    {
        public HavenSize MeasureLeaf(HavenElement element, HavenSize available) =>
            new(Math.Min(available.Width, 100), Math.Min(available.Height, 30));
    }
}
