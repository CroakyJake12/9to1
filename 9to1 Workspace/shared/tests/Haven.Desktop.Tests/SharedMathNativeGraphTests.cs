using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Core.Mathematics;
using Haven.Desktop.Mathematics;
using ScottPlot.Avalonia;

namespace Haven.Desktop.Tests;

public sealed class SharedMathNativeGraphTests
{
    [Fact]
    public async Task Maintained_native_graph_renders_structured_ids_and_actual_pointer_edits_exact_graph_version()
    {
        var token = TestContext.Current.CancellationToken;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HomeProductivityCuiSurfaceTests.PixelAppBuilder));
        await session.Dispatch(async () =>
        {
            var point = new GraphPoint(Guid.NewGuid(), new(0, 0));
            var line = new GraphLine(Guid.NewGuid(), new(-4, -3), new(4, 3));
            var table = new GraphCoordinateTable(Guid.NewGuid(), [new(-2, 3), new(2, -3)]);
            var graph = new GraphDefinition(Guid.NewGuid(), 7, new(-5, 5, -5, 5), [],
                [point, line, table], [GraphResponseTool.PlacePoint], "Coordinates and straight line");
            var encoded = MathObjectCodec.Encode(graph);
            using var editor = new SharedGraphEditorControl(graph);
            var window = new Window { Content = editor, Width = 720, Height = 580 };
            window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            try
            {
                using var before = Assert.IsAssignableFrom<Bitmap>(window.CaptureRenderedFrame());
                var pixels = Pixels(before);
                Assert.True(pixels.Where((_, index) => index % 4 != 3).Distinct().Count() >= 16);
                Assert.Equal(new[] { point.PrimitiveID, line.PrimitiveID, table.PrimitiveID }, editor.Projection.RenderedPrimitiveIDs);
                Assert.Empty(editor.Projection.UnavailableSymbolicPrimitiveIDs);
                Assert.Equal(ScottPlotGraphAdapter.Implementation, editor.Projection.Implementation);
                Assert.Equal(encoded, MathObjectCodec.Encode(editor.Snapshot));
                var native = Assert.Single(editor.GetVisualDescendants().OfType<AvaPlot>());
                Assert.True(native.Plot.LastRender.DataRect.HasArea);
                // Use actual rendered axes to place a native click, then check the captured
                // coordinate against the intended point with a display/pointer tolerance.
                var click = native.Plot.GetPixel(new ScottPlot.Coordinates(1.5, 2));
                var local = new Point(click.X, click.Y);
                var screen = native.TranslatePoint(local, window) ?? throw new InvalidOperationException("NativeGraphNotAttached");
                window.MouseDown(screen, MouseButton.Left); window.MouseUp(screen, MouseButton.Left);
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var response = Assert.IsType<GraphResponse>(editor.LastResponse);
                Assert.Equal(graph.GraphID, response.GraphID); Assert.Equal(7, response.GraphRevision);
                var action = Assert.Single(response.Actions); Assert.Equal(GraphResponseTool.PlacePoint, action.Tool);
                var placed = Assert.IsType<GraphPoint>(action.Primitive);
                Assert.InRange(placed.Position.X, 1.45m, 1.55m); Assert.InRange(placed.Position.Y, 1.95m, 2.05m);
                Assert.Equal(graph.GraphID, editor.Snapshot.GraphID); Assert.Equal(8, editor.Snapshot.Revision);
                Assert.Contains(editor.Snapshot.Primitives, x => x.PrimitiveID == placed.PrimitiveID);
                Assert.Equal(encoded, MathObjectCodec.Encode(graph));
                var x = Assert.Single(editor.GetVisualDescendants().OfType<TextBox>(), control => control.Name == "math-graph-x");
                var y = Assert.Single(editor.GetVisualDescendants().OfType<TextBox>(), control => control.Name == "math-graph-y");
                x.Text = "-1.25"; y.Text = "3.5"; Dispatcher.UIThread.RunJobs();
                var button = Assert.Single(editor.GetVisualDescendants().OfType<Button>(), control => control.Name == "math-place-point");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await editor.WhenActionsIdleAsync();
                Assert.Equal(new GraphCoordinate(-1.25m, 3.5m), Assert.IsType<GraphPoint>(Assert.Single(editor.LastResponse!.Actions).Primitive).Position);
                Assert.Equal(8, editor.LastResponse.GraphRevision); Assert.Equal(9, editor.Snapshot.Revision);
                var durable = MathObjectCodec.Encode(editor.Snapshot);
                var responseBeforeUnsupportedPrecision = MathObjectCodec.Encode(editor.LastResponse!);
                x.Text = "1.00000000000000000000000000001"; y.Text = "2";
                Dispatcher.UIThread.RunJobs(); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await editor.WhenActionsIdleAsync();
                Assert.Equal(durable, MathObjectCodec.Encode(editor.Snapshot));
                Assert.Equal(responseBeforeUnsupportedPrecision, MathObjectCodec.Encode(editor.LastResponse!));
                Assert.Equal(9, editor.Snapshot.Revision);
                var restored = MathObjectCodec.Decode<GraphDefinition>(durable);
                using var reopened = new SharedGraphEditorControl(restored);
                Assert.Equal(durable, MathObjectCodec.Encode(reopened.Snapshot));
                Assert.Equal(editor.Projection.RenderedPrimitiveIDs, reopened.Projection.RenderedPrimitiveIDs);
                editor.Dispose();
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await editor.WhenActionsIdleAsync();
                window.MouseDown(screen, MouseButton.Left); window.MouseUp(screen, MouseButton.Left); Dispatcher.UIThread.RunJobs();
                Assert.False(editor.TrySetValue("X", "1"));
                Assert.Throws<ObjectDisposedException>(() => { _ = editor.DispatchAsync("PlacePoint", null, TestContext.Current.CancellationToken); });
                Assert.Equal(durable, MathObjectCodec.Encode(editor.Snapshot));
            }
            finally { window.Close(); }
        }, token);
    }

    [Fact]
    public async Task Undeclared_tools_invalid_coordinates_and_unavailable_symbolic_plotting_preserve_canonical_graph()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HomeProductivityCuiSurfaceTests.PixelAppBuilder));
        await session.Dispatch(() =>
        {
            var expression = new MathExpression(Guid.NewGuid(), 1, "x^{2}");
            var function = new GraphFunction(Guid.NewGuid(), new(expression.ExpressionID, 1));
            var graph = new GraphDefinition(Guid.NewGuid(), 1, new(-5, 5, -5, 5), [expression], [function], []);
            var encoded = MathObjectCodec.Encode(graph);
            using var editor = new SharedGraphEditorControl(graph);
            Assert.Equal(new[] { function.PrimitiveID }, editor.Projection.UnavailableSymbolicPrimitiveIDs);
            Assert.Empty(editor.Projection.RenderedPrimitiveIDs); Assert.False(editor.IsActionAvailable("PlacePoint"));
            Assert.Throws<InvalidOperationException>(() => { _ = editor.DispatchAsync("PlacePoint", null, TestContext.Current.CancellationToken); });
            Assert.Equal(encoded, MathObjectCodec.Encode(editor.Snapshot));
            using var editable = new SharedGraphEditorControl(graph with { ResponseTools = [GraphResponseTool.PlacePoint] });
            Assert.True(editable.TrySetValue("X", "NaN")); Assert.True(editable.TrySetValue("Y", "2"));
            _ = editable.DispatchAsync("PlacePoint", null);
            Assert.Equal(1, editable.Snapshot.Revision); Assert.Null(editable.LastResponse);
            Assert.True(editable.TrySetValue("X", "6")); _ = editable.DispatchAsync("PlacePoint", null);
            Assert.Equal(1, editable.Snapshot.Revision); Assert.Null(editable.LastResponse);
            var dense = graph with { Axes = graph.Axes with { XGridSpacing = 0.0001m } };
            Assert.Throws<NotSupportedException>(() => new SharedGraphEditorControl(dense));
            var collapsed = graph with { Axes = graph.Axes with { XMinimum = 100000000000000000000m, XMaximum = 100000000000000000001m } };
            Assert.Throws<NotSupportedException>(() => new SharedGraphEditorControl(collapsed));
        }, TestContext.Current.CancellationToken);
    }
    private static byte[] Pixels(Bitmap bitmap)
    {
        var bytes = new byte[checked(bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4)];
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), pinned.AddrOfPinnedObject(), bytes.Length, bitmap.PixelSize.Width * 4); }
        finally { pinned.Free(); }
        return bytes;
    }
}
