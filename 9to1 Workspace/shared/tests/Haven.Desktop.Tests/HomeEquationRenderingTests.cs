using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;

namespace Haven.Desktop.Tests;

public sealed class HomeEquationRenderingTests
{
    [Fact]
    public async Task Actual_fraction_layout_places_numerator_and_denominator_on_opposite_sides_of_native_bar()
    {
        var token = TestContext.Current.CancellationToken;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HomeProductivityCuiSurfaceTests.PixelAppBuilder));
        await session.Dispatch(() =>
        {
            var block = new NotesBlock { Kind = NotesBlockKind.Equation, Equation = new()
                { Source = @"\frac{1}{2}", ViewMode = NotesEquationViewMode.Visual, AccessibleAlternative = "One half", RenderedText = "not mathematical layout" } };
            var content = JsonSerializer.SerializeToNode(block)!.AsObject();
            content["RetainedFutureField"] = new JsonObject { ["value"] = 42 };
            var canonical = JsonSerializer.SerializeToElement(content);
            var handler = new HomeEquationObjectHandler();
            var value = handler.Create(block.Id, canonical);
            var result = handler.Render(value);
            Assert.Equal(block.Id, Assert.Single(result.EquationBindings).ObjectId);
            using var surface = new HomeProductivityCuiSurface(result);
            var window = new Window { Content = surface, Width = 320, Height = 220 };
            window.Show(); window.UpdateLayout();
            try
            {
                var image = Assert.Single(surface.GetVisualDescendants().OfType<Image>());
                Assert.Equal(block.Id.ToString("D"), AutomationProperties.GetAutomationId(image));
                Assert.True(image.Bounds.Width > 0); Assert.True(image.Bounds.Height > 0);
                var native = Assert.IsAssignableFrom<Bitmap>(image.Source);
                var width = native.PixelSize.Width; var height = native.PixelSize.Height;
                var pixels = Pixels(native);
                var longest = 0; var barY = -1;
                for (var y = 0; y < height; y++)
                {
                    var run = 0;
                    for (var x = 0; x < width; x++)
                    {
                        run = pixels[(y * width + x) * 4 + 3] > 128 ? run + 1 : 0;
                        if (run > longest) { longest = run; barY = y; }
                    }
                }
                Assert.True(longest >= width / 3, "The native fraction needs a real horizontal rule.");
                Assert.InRange(barY, height / 4, height * 3 / 4);
                Assert.True(InkRows(pixels, width, 0, barY - 2) >= 4, "Numerator glyph pixels must appear above the fraction bar.");
                Assert.True(InkRows(pixels, width, barY + 3, height) >= 4, "Denominator glyph pixels must appear below the fraction bar.");
                using var mounted = new RenderTargetBitmap(new PixelSize(320, 220), new Vector(96, 96));
                mounted.Render(surface);
                var displayed = Pixels(mounted);
                Assert.True(Enumerable.Range(0, displayed.Length / 4).Count(index => displayed[index * 4 + 3] > 128 &&
                    displayed[index * 4] < 128 && displayed[index * 4 + 1] < 128 && displayed[index * 4 + 2] < 128) > 20);
                if (Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR") is { Length: > 0 } capture)
                { Directory.CreateDirectory(capture); mounted.Save(Path.Combine(capture, "home-equation-fraction.png")); }
                Assert.Equal(canonical.GetRawText(), value.Content.GetRawText());
                Assert.Equal(@"\frac{1}{2}", block.Equation!.Source);
                Assert.Equal(42, value.Content.GetProperty("RetainedFutureField").GetProperty("value").GetInt32());
            }
            finally { window.Close(); }
        }, token);
    }

    [Theory]
    [InlineData("macro")]
    [InlineData("malformed")]
    [InlineData("unknown")]
    public async Task Unsupported_or_malformed_source_shows_explicit_error_and_retains_canonical_source(string failure)
    {
        var token = TestContext.Current.CancellationToken;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HomeProductivityCuiSurfaceTests.PixelAppBuilder));
        await session.Dispatch(() =>
        {
            var equation = new NotesEquationData { ViewMode = NotesEquationViewMode.Visual,
                Source = failure == "malformed" ? @"\frac{1}{" : failure == "unknown" ? @"\thisCommandDoesNotExist{1}" : @"\custom{1}",
                RenderedText = "This fallback must not become an equation image." };
            if (failure == "macro") equation.Macros.Add("custom", "#1");
            var block = new NotesBlock { Kind = NotesBlockKind.Equation, Equation = equation };
            var original = JsonSerializer.Serialize(block);
            var handler = new HomeEquationObjectHandler();
            var value = handler.Create(block.Id, JsonSerializer.SerializeToElement(block));
            using var surface = new HomeProductivityCuiSurface(handler.Render(value));
            var window = new Window { Content = surface, Width = 600, Height = 260 };
            window.Show(); window.UpdateLayout();
            try
            {
                Assert.Empty(surface.GetVisualDescendants().OfType<Image>());
                var texts = surface.GetVisualDescendants().OfType<TextBlock>().ToArray();
                Assert.False(string.IsNullOrWhiteSpace(Assert.Single(texts,
                    item => AutomationProperties.GetAutomationId(item) == block.Id.ToString("D") + "-error").Text));
                Assert.Equal(equation.Source, Assert.Single(texts,
                    item => AutomationProperties.GetAutomationId(item) == block.Id.ToString("D") + "-source").Text);
                Assert.Equal(original, value.Content.GetRawText());
                Assert.Equal(original, JsonSerializer.Serialize(block));
            }
            finally { window.Close(); }
        }, token);
    }
    private static int InkRows(byte[] pixels, int width, int first, int end) =>
        Enumerable.Range(first, Math.Max(0, end - first)).Count(y =>
            Enumerable.Range(0, width).Any(x => pixels[(y * width + x) * 4 + 3] > 128));
    private static byte[] Pixels(Bitmap bitmap)
    {
        var bytes = new byte[checked(bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4)];
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), pinned.AddrOfPinnedObject(), bytes.Length, bitmap.PixelSize.Width * 4); }
        finally { pinned.Free(); }
        return bytes;
    }
}
