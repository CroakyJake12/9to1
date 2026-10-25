using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiMenuIconSizeTests
{
    [Fact]
    public async Task Authored_menu_icon_size_changes_the_glyph_without_enlarging_its_hit_target_or_other_menus()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var registry = new CuiControlRegistry();
            registry.RegisterImageSourceResolver((_, _) => new DrawingImage(new GeometryDrawing
                { Geometry = new RectangleGeometry(new Rect(0, 0, 100, 100)), Brush = Brushes.White }));
            using var loader = new CuiControlLoader(registry);
            var loaded = loader.LoadMarkup("<Container Type='Menu'>" +
                "<Button Type='Menu Item' Content='Undo' IconSource='icon:undo' IconOnly='True' IconSize='18' " +
                "Width='32' MinWidth='32' Height='32' MinHeight='32' Padding='0'/>" +
                "<Button Type='Menu Item' Content='Gallery' IconSource='icon:gallery' IconOnly='True'/>" +
                "<Button Type='Menu Item' Content='Normal' IconSource='icon:normal'/>" +
                "</Container>");
            Assert.Empty(loaded.Diagnostics); Assert.NotNull(loaded.Root);
            var window = new Window { Content = loaded.Root, Width = 400, Height = 100 };
            try
            {
                window.Show(); window.UpdateLayout();
                var items = loaded.Root!.GetVisualDescendants().OfType<CuiMenuItem>().ToArray();
                var undo = items.Single(item => Equals(item.Header, "Undo"));
                var glyph = Assert.Single(undo.GetVisualDescendants().OfType<Image>());
                Assert.Equal(18, glyph.Bounds.Width, 2); Assert.Equal(18, glyph.Bounds.Height, 2);
                Assert.Equal(32, undo.Bounds.Width, 2); Assert.Equal(32, undo.Bounds.Height, 2);
                Assert.Equal(36, Assert.Single(items.Single(item => Equals(item.Header, "Gallery")).GetVisualDescendants().OfType<Image>()).Width);
                Assert.Equal(20, Assert.Single(items.Single(item => Equals(item.Header, "Normal")).GetVisualDescendants().OfType<Image>()).Width);
                CuiPropertyPresentation.Set(undo, "IconSize", 16d, CuiPropertyLevel.Instance, "test glyph size");
                window.UpdateLayout(); Assert.Equal(16, glyph.Bounds.Width, 2);
                Assert.Throws<ArgumentException>(() => CuiPropertyPresentation.Set(undo, "IconSize", double.NaN,
                    CuiPropertyLevel.Instance, "test glyph size"));
                Assert.Equal(16, glyph.Width);
                Assert.True(CuiPropertyPresentation.Remove(undo, "IconSize", CuiPropertyLevel.Instance, "test glyph size"));
                window.UpdateLayout(); Assert.Equal(18, glyph.Width); // restore the authored declaration first
                Assert.True(CuiPropertyPresentation.Remove(undo, "IconSize", CuiPropertyLevel.Instance, "CUI authored Instance"));
                window.UpdateLayout(); Assert.Null(undo.IconSize); Assert.Equal(36, glyph.Width);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(257)] [InlineData(double.PositiveInfinity)] [InlineData(double.NaN)]
    public void Menu_icon_size_rejects_nonpositive_or_unbounded_values(double size) =>
        Assert.Throws<ArgumentException>(() => new CuiMenuItem { IconSize = size });
}
