using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using VectorPath = Avalonia.Controls.Shapes.Path;

namespace Haven.Desktop.Tests;

public sealed class HomeVectorShapeRenderingTests
{
    [Fact]
    public async Task Canonical_vector_fill_renders_real_path_pixels_and_retains_owner_identities()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HomeProductivityCuiSurfaceTests.PixelAppBuilder));
        await session.Dispatch(() =>
        {
            var shape = DocumentVectorShapes.CreateEditableStarter("Canonical native shape");
            shape.Paths[0].Fill.Color = "#FFFF0000";
            shape.Paths[0].Stroke.Enabled = false;
            var original = JsonSerializer.Serialize(shape);
            var handler = new HomeVectorShapeObjectHandler();
            var engine = new HomeProductivityEngine();
            var value = handler.Project(shape);
            var green = handler.Transform(value, new("vector.fill", 1, "drawing.vector", [shape.Id],
                JsonSerializer.SerializeToElement(new { pathId = shape.Paths[0].Id, color = "#FF00FF00" }), 1));
            var result = engine.RenderObject(green);
            Assert.Equal(shape.Id, Assert.Single(result.VectorBindings).ObjectId);
            var retained = HomeVectorShapeObjectHandler.ReadCanonical(green.Content, shape.Id);
            Assert.Equal(shape.Paths[0].Id, retained.Paths[0].Id);
            Assert.Equal(shape.Paths[0].Subpaths[0].Nodes.Select(node => node.Id), retained.Paths[0].Subpaths[0].Nodes.Select(node => node.Id));
            using var surface = new HomeProductivityCuiSurface(result);
            var window = new Window { Content = surface, Width = 100, Height = 100 };
            window.Show();
            try
            {
            surface.Measure(new Size(100, 100));
            surface.Arrange(new Rect(0, 0, 100, 100));
            Assert.Equal(shape.Paths[0].Id.ToString("D"), AutomationProperties.GetAutomationId(
                Assert.Single(surface.GetVisualDescendants().OfType<VectorPath>())));
            using var bitmap = new RenderTargetBitmap(new PixelSize(100, 100), new Vector(96, 96));
            bitmap.Render(surface);
            if (Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR") is { Length: > 0 } captureDirectory)
            {
                Directory.CreateDirectory(captureDirectory);
                bitmap.Save(Path.Combine(captureDirectory, "home-vector-native.png"));
            }
            var pixels = new byte[100 * 100 * 4];
            var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try { bitmap.CopyPixels(new PixelRect(0, 0, 100, 100), pinned.AddrOfPinnedObject(), pixels.Length, 100 * 4); }
            finally { pinned.Free(); }
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, pixels.AsSpan((50 * 100 + 50) * 4, 4).ToArray());
            Assert.Equal(0, pixels[3]);
            Assert.Equal(original, JsonSerializer.Serialize(shape));
            retained.ClippingPathId = retained.Paths[0].Id;
            Assert.Throws<NotSupportedException>(() => engine.RenderObject(handler.Project(retained)));
            }
            finally { window.Close(); }
        }, TestContext.Current.CancellationToken);
    }
}
