using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Haven.Application;
using HavenOS.Apps.Canvas;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;

namespace Haven.Desktop.Tests;

public sealed class HomeProductivityCuiSurfaceTests
{
    [Fact]
    public async Task Bound_frame_pixels_are_detached_and_rendered_by_the_actual_Home_leaf()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(PixelAppBuilder));
        await session.Dispatch(() =>
        {
        byte[] pixels = [0, 0, 255, 255];
        var frame = new HomeProductivityRasterFrame(1, 1, 4, pixels);
        Array.Clear(pixels);
        var result = new HomeProductivityObjectRenderResult("<Cui><Object Type=\"spe.raster\" id=\"ink\" /></Cui>", [])
        { RasterBindings = [new("ink", Guid.NewGuid(), frame)] };
        using var surface = new HomeProductivityCuiSurface(result);
        var window = new Window { Content = surface, Width = 80, Height = 80 };
        window.Show();
        try
        {
            var bitmap = Assert.IsType<WriteableBitmap>(Assert.Single(surface.GetVisualDescendants().OfType<Image>()).Source);
            using (var buffer = bitmap.Lock())
            {
                var actual = new byte[4];
                Marshal.Copy(buffer.Address, actual, 0, actual.Length);
                Assert.Equal(new byte[] { 0, 0, 255, 255 }, actual);
            }
            surface.Dispose();
            Assert.Null(surface.Content);
        }
        finally { window.Close(); }
        Assert.Throws<InvalidDataException>(() => new HomeProductivityCuiSurface(result with { RasterBindings = [] }));
        Assert.Throws<InvalidDataException>(() => new HomeProductivityCuiSurface(result with
            { RasterBindings = [result.RasterBindings[0], result.RasterBindings[0]] }));
        Assert.Throws<InvalidDataException>(() => new HomeProductivityCuiSurface(result with
            { CuiSource = "<Cui><StackPanel><Object Type=\"spe.raster\" id=\"ink\" /><Object Type=\"spe.raster\" id=\"ink\" /></StackPanel></Cui>" }));
        Assert.Throws<InvalidDataException>(() => new HomeProductivityCuiSurface(result with { CuiSource = "<Cui><TextBlock text=\"No frame\" /></Cui>" }));
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Canonical_Rnote_stroke_renders_through_shared_engine_and_same_native_Home_leaf()
    {
        if (!OperatingSystem.IsLinux()) return;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(PixelAppBuilder));
        await session.Dispatch(() =>
        {
        using var document = CanvasRnoteDocument.Create("Shared Canvas ink");
        var operation = new CanvasMutationRequest(document.Snapshot.RevisionId, Guid.NewGuid(), new("test-native-actor", "Native renderer test"));
        var id = document.DrawStroke([new(10, 10, .2), new(35, 40, .7), new(80, 65, .5)], operation);
        var stroke = Assert.Single(document.Snapshot.Pages[0].Strokes);
        var shared = new HomeProductivityEngine(handlers: [new CanvasInkObjectHandler()]);
        var value = shared.CreateObject("drawing.ink", id, JsonSerializer.SerializeToElement(stroke));
        var rendered = shared.RenderObject(value);
        Assert.Equal(id, Assert.Single(rendered.RasterBindings).ObjectId);
        using var surface = new HomeProductivityCuiSurface(rendered);
        var window = new Window { Content = surface, Width = 512, Height = 512 };
        window.Show();
        try
        {
            var bitmap = Assert.IsType<WriteableBitmap>(Assert.Single(surface.GetVisualDescendants().OfType<Image>()).Source);
            using var buffer = bitmap.Lock();
            var row = new byte[512 * 4];
            var containsInk = false;
            for (var y = 0; y < 512; y++)
            {
                Marshal.Copy(IntPtr.Add(buffer.Address, y * buffer.RowBytes), row, 0, row.Length);
                containsInk |= row.Any(channel => channel != 0);
            }
            Assert.True(containsInk);
            Assert.Equal(JsonSerializer.Serialize(stroke), JsonSerializer.Serialize(Assert.Single(document.Snapshot.Pages[0].Strokes)));
        }
        finally { window.Close(); }
        }, TestContext.Current.CancellationToken);
    }

    public static class PixelAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Avalonia.Application>()
            .UseSkia().WithInterFont().With(new Avalonia.Media.FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" }).UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
