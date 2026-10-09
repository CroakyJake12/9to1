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
using Haven.Desktop.Tests;
using ScottPlot.Avalonia;

namespace NineToOne.Web.Mathematics.OwnerBoundaryTests;

// Anonymous owner-control fixture, not a new application, backend or authority.
public sealed class MathOwnerInputGeometryTests
{
    [Fact]
    public async Task Actual_keyboard_equation_error_retains_last_valid_pixels_identity_and_source_until_restore()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HomeProductivityCuiSurfaceTests.PixelAppBuilder));
        await session.Dispatch(async () =>
        {
            var initial = new MathExpression(Guid.NewGuid(), 4, @"\frac{1}{2}", "One half");
            using var editor = new SharedMathEditorControl(initial);
            var window = new Window { Content = editor, Width = 640, Height = 680 };
            window.Show(); Pump(window);
            try
            {
                var before = MathObjectCodec.Encode(editor.Snapshot.LastValid);
                var image = Assert.Single(editor.GetVisualDescendants().OfType<Image>());
                var bitmap = Assert.IsAssignableFrom<Bitmap>(image.Source);
                var pixels = Pixels(bitmap);
                await Click(editor, "math-mode"); Pump(window);
                var source = Text(editor, "math-source");
                Type(window, source, @"\frac{1}{");
                var apply = Button(editor, "math-apply-source");
                Assert.True(apply.Focus());
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
                await editor.WhenActionsIdleAsync(); Pump(window);
                Assert.Equal(@"\frac{1}{", editor.Snapshot.DraftLaTeX);
                Assert.NotNull(editor.Snapshot.Diagnostic);
                Assert.Equal(before, MathObjectCodec.Encode(editor.Snapshot.LastValid));
                var displayedImage = Assert.Single(editor.GetVisualDescendants().OfType<Image>());
                Assert.Same(image, displayedImage);
                var displayedBitmap = Assert.IsAssignableFrom<Bitmap>(displayedImage.Source);
                Assert.Same(bitmap, displayedBitmap);
                Assert.Equal(pixels, Pixels(displayedBitmap));
                Assert.False(editor.IsActionAvailable("Fraction"));
                await Click(editor, "math-restore");
                Assert.Null(editor.Snapshot.Diagnostic);
                Assert.Equal(editor.Snapshot.LastValid.LaTeX, source.Text);
                Assert.Equal(before, MathObjectCodec.Encode(editor.Snapshot.LastValid));
                Type(window, source, "y"); await Click(editor, "math-apply-source");
                Assert.Equal(initial.ExpressionID, editor.Snapshot.LastValid.ExpressionID);
                Assert.Equal(5, editor.Snapshot.LastValid.Revision);
                Assert.Equal("y", editor.Snapshot.LastValid.LaTeX.Trim());
                Assert.Equal(before, MathObjectCodec.Encode(initial));
            }
            finally { window.Close(); }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Native_keyboard_coordinates_emit_same_typed_response_and_rejected_decimal_retains_previous_response()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HomeProductivityCuiSurfaceTests.PixelAppBuilder));
        await session.Dispatch(async () =>
        {
            var graph = Graph(); using var editor = new SharedGraphEditorControl(graph);
            var window = new Window { Content = editor, Width = 720, Height = 580 };
            window.Show(); Pump(window);
            try
            {
                var original = MathObjectCodec.Encode(graph);
                Type(window, Text(editor, "math-graph-x"), "-1.25");
                Type(window, Text(editor, "math-graph-y"), "3.5");
                var place = Button(editor, "math-place-point"); Assert.True(place.Focus());
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
                await editor.WhenActionsIdleAsync(); Pump(window);
                var response = Assert.IsType<GraphResponse>(editor.LastResponse);
                Assert.Equal(graph.GraphID, response.GraphID); Assert.Equal(7, response.GraphRevision);
                Assert.Equal(1, response.Revision); Assert.NotEqual(Guid.Empty, response.ResponseID);
                var action = Assert.Single(response.Actions); Assert.Equal(GraphResponseTool.PlacePoint, action.Tool);
                var point = Assert.IsType<GraphPoint>(action.Primitive);
                Assert.Equal(new GraphCoordinate(-1.25m, 3.5m), point.Position);
                Assert.Equal(graph.GraphID, editor.Snapshot.GraphID); Assert.Equal(8, editor.Snapshot.Revision);
                Assert.Contains(editor.Snapshot.Primitives, x => x.PrimitiveID == point.PrimitiveID);
                var committed = MathObjectCodec.Encode(editor.Snapshot); var answer = MathObjectCodec.Encode(response);
                Type(window, Text(editor, "math-graph-x"), "1.00000000000000000000000000001");
                place.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); await editor.WhenActionsIdleAsync();
                Assert.Equal(committed, MathObjectCodec.Encode(editor.Snapshot));
                Assert.Equal(answer, MathObjectCodec.Encode(editor.LastResponse!));
                Assert.Equal(original, MathObjectCodec.Encode(graph));
                var captured = MathObjectCodec.Decode<GraphDefinition>(committed);
                Assert.Equal(committed, MathObjectCodec.Encode(captured));
                Assert.Equal(point.PrimitiveID, Assert.IsType<GraphPoint>(Assert.Single(captured.Primitives)).PrimitiveID);
            }
            finally { window.Close(); }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Outside_plot_and_right_pointer_release_preserve_graph_before_real_left_click_advances_once()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HomeProductivityCuiSurfaceTests.PixelAppBuilder));
        await session.Dispatch(() =>
        {
            var graph = Graph(); using var editor = new SharedGraphEditorControl(graph);
            var window = new Window { Content = editor, Width = 720, Height = 580 };
            window.Show(); Pump(window);
            try
            {
                using var rendered = Assert.IsAssignableFrom<Bitmap>(window.CaptureRenderedFrame());
                Assert.True(rendered.PixelSize.Width > 0 && rendered.PixelSize.Height > 0);
                var native = Assert.Single(editor.GetVisualDescendants().OfType<AvaPlot>());
                Assert.True(native.Plot.LastRender.DataRect.HasArea);
                var original = MathObjectCodec.Encode(editor.Snapshot);
                Assert.False(native.Plot.LastRender.DataRect.Contains(new ScottPlot.Pixel(1, 1)));
                var outside = native.TranslatePoint(new Point(1, 1), window) ?? throw new InvalidOperationException("ActualPlotNotAttached");
                window.MouseDown(outside, MouseButton.Left); window.MouseUp(outside, MouseButton.Left); Pump(window);
                Assert.Equal(original, MathObjectCodec.Encode(editor.Snapshot)); Assert.Null(editor.LastResponse);
                var pixel = native.Plot.GetPixel(new ScottPlot.Coordinates(1.5, 2));
                var target = native.TranslatePoint(new Point(pixel.X, pixel.Y), window) ?? throw new InvalidOperationException("ActualPlotNotAttached");
                window.MouseDown(target, MouseButton.Right); window.MouseUp(target, MouseButton.Right); Pump(window);
                Assert.Equal(original, MathObjectCodec.Encode(editor.Snapshot)); Assert.Null(editor.LastResponse);
                window.MouseDown(target, MouseButton.Left); window.MouseUp(target, MouseButton.Left); Pump(window);
                var response = Assert.IsType<GraphResponse>(editor.LastResponse);
                Assert.Equal(graph.GraphID, response.GraphID); Assert.Equal(7, response.GraphRevision);
                var placed = Assert.IsType<GraphPoint>(Assert.Single(response.Actions).Primitive);
                Assert.InRange(placed.Position.X, 1.45m, 1.55m); Assert.InRange(placed.Position.Y, 1.95m, 2.05m);
                Assert.Equal(8, editor.Snapshot.Revision); Assert.Single(editor.Snapshot.Primitives);
                Assert.Equal(original, MathObjectCodec.Encode(graph));
            }
            finally { window.Close(); }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Failed_native_precision_projection_preserves_existing_pixels_and_exact_canonical_source()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HomeProductivityCuiSurfaceTests.PixelAppBuilder));
        await session.Dispatch(() =>
        {
            var graph = Graph() with { Primitives = [new GraphLine(Guid.NewGuid(), new(-2, -1), new(2, 1))] };
            using var editor = new SharedGraphEditorControl(graph);
            var window = new Window { Content = editor, Width = 720, Height = 580 };
            window.Show(); Pump(window);
            try
            {
                var native = Assert.Single(editor.GetVisualDescendants().OfType<AvaPlot>());
                var encoded = MathObjectCodec.Encode(graph);
                using var frame = Assert.IsAssignableFrom<Bitmap>(window.CaptureRenderedFrame());
                var pixels = Pixels(frame);
                var dense = graph with { Axes = graph.Axes with { XGridSpacing = 0.0001m } };
                var denseBytes = MathObjectCodec.Encode(dense);
                var grid = Assert.Throws<NotSupportedException>(() => ScottPlotGraphAdapter.Render(native, dense));
                Assert.Equal("GraphGridBudgetExceeded", grid.Message);
                var collapsed = graph with { Primitives = [new GraphLine(Guid.NewGuid(),
                    new(100000000000000000000m, 0), new(100000000000000000001m, 0))] };
                var collapsedBytes = MathObjectCodec.Encode(collapsed);
                var precision = Assert.Throws<NotSupportedException>(() => ScottPlotGraphAdapter.Render(native, collapsed));
                Assert.Equal("GraphLineBelowDisplayPrecision", precision.Message);
                Pump(window); using var after = Assert.IsAssignableFrom<Bitmap>(window.CaptureRenderedFrame());
                Assert.Equal(frame.PixelSize, after.PixelSize); Assert.Equal(pixels, Pixels(after));
                Assert.Equal(encoded, MathObjectCodec.Encode(editor.Snapshot));
                Assert.Equal(denseBytes, MathObjectCodec.Encode(dense));
                Assert.Equal(collapsedBytes, MathObjectCodec.Encode(collapsed));
                Assert.Equal(encoded, MathObjectCodec.Encode(graph));
            }
            finally { window.Close(); }
        }, TestContext.Current.CancellationToken);
    }

    private static GraphDefinition Graph() => new(Guid.NewGuid(), 7, new(-5, 5, -5, 5), [], [], [GraphResponseTool.PlacePoint]);
    private static void Pump(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private static TextBox Text(Control editor, string name) => Assert.Single(editor.GetVisualDescendants().OfType<TextBox>(), x => x.Name == name);
    private static Button Button(Control editor, string name) => Assert.Single(editor.GetVisualDescendants().OfType<Button>(), x => x.Name == name);
    private static async Task Click(SharedMathEditorControl editor, string name)
    { Button(editor, name).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); await editor.WhenActionsIdleAsync(); Dispatcher.UIThread.RunJobs(); }
    private static void Type(Window window, TextBox text, string value)
    {
        Assert.True(text.Focus());
        window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
        window.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
        window.KeyTextInput(value); Dispatcher.UIThread.RunJobs();
        Assert.Equal(value, text.Text);
    }
    private static byte[] Pixels(Bitmap bitmap)
    {
        var bytes = new byte[checked(bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4)];
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), pin.AddrOfPinnedObject(), bytes.Length, bitmap.PixelSize.Width * 4); }
        finally { pin.Free(); }
        return bytes;
    }
}
