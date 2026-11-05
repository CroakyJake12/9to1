using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// The text form of a menu item's icon slot. A colour emoji cannot be carried by an image, and a CUI app may
/// need exactly that mark on a menu row - Boards uses it for a Board whose owner chose an emoji - so the slot
/// has to be a first-class authored property rather than a monochrome stand-in. These tests pin that the glyph
/// shares the image slot's sizing and hit-target behaviour, that a blank value means no mark rather than an
/// error, that a glyph takes the slot when an image reference is also present, and that a text run which is not
/// a mark is refused at load instead of drawing a sentence in the icon box.
/// </summary>
public sealed class CuiMenuGlyphIconTests
{
    [Fact]
    public async Task Authored_menu_glyph_uses_the_image_slot_sizing_and_a_blank_value_is_no_mark()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var registry = new CuiControlRegistry();
            registry.RegisterImageSourceResolver((_, _) => new DrawingImage(new GeometryDrawing
                { Geometry = new RectangleGeometry(new Rect(0, 0, 100, 100)), Brush = Brushes.White }));
            using var loader = new CuiControlLoader(registry);
            var loaded = loader.LoadMarkup("<Container Type='Menu'>" +
                "<Button Type='Menu Item' Content='Marked' IconGlyph='📚' IconSize='18' Width='140'/>" +
                "<Button Type='Menu Item' Content='Blank' Width='120'/>" +
                "<Button Type='Menu Item' Content='Both' IconSource='icon:undo' IconGlyph='⚖️'/>" +
                "<Button Type='Menu Item' Content='GlyphOnly' IconGlyph='🔍' IconOnly='True' Width='32' Height='32'/>" +
                "</Container>");
            Assert.Empty(loaded.Diagnostics);
            Assert.NotNull(loaded.Root);

            var window = new Window { Content = loaded.Root, Width = 400, Height = 140 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var items = loaded.Root!.GetVisualDescendants().OfType<CuiMenuItem>().ToArray();

                var marked = items.Single(item => Equals(item.Header, "Marked"));
                Assert.Equal("📚", marked.IconGlyph);
                var glyph = marked.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "📚");
                Assert.True(glyph.IsVisible, "the glyph takes the icon slot");
                Assert.Equal(18, glyph.Bounds.Width, 2);
                Assert.Equal(18, glyph.Bounds.Height, 2);
                Assert.DoesNotContain(marked.GetVisualDescendants().OfType<Image>(), image => image.IsVisible);

                // A bound value that is empty is the normal state before an owner chooses a mark. The compiler
                // refuses an authored empty attribute, so this is exercised the way a binding reaches it.
                var blank = items.Single(item => Equals(item.Header, "Blank"));
                Assert.Null(blank.IconGlyph);
                blank.IconGlyph = string.Empty;
                window.UpdateLayout();
                Assert.False(blank.HasIconGlyph, "an empty bound value is no mark, not an error");
                Assert.DoesNotContain(blank.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.IsVisible && text.Text is { Length: > 0 } && text.Text != "Blank");
                blank.IconGlyph = "  ";
                window.UpdateLayout();
                Assert.False(blank.HasIconGlyph, "a whitespace-only mark is still no mark");

                // The mark sits beside the label. Hiding the label because a mark exists is the regression
                // this pins: the switcher's whole purpose is showing which Board is open.
                var markedHeader = marked.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.Text == "Marked");
                Assert.True(markedHeader.IsVisible, "a text mark must not hide the menu item's label");

                // A top-level mark and its label must not share one column: in a real window the Board's emoji
                // landed on top of the name, which is the failure this pins at the framework level.
                var markedGlyph = marked.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "📚");
                var markedLabel = marked.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Marked");
                // Bounds are self-relative, so the positions have to be translated into the item before they
                // can be compared - otherwise this reads as an overlap in a layout that is actually correct.
                var glyphAt = markedGlyph.TranslatePoint(new Point(0, 0), marked) ?? new Point(-1, -1);
                var labelAt = markedLabel.TranslatePoint(new Point(0, 0), marked) ?? new Point(-1, -1);
                var gap = labelAt.X - (glyphAt.X + markedGlyph.Bounds.Width);
                Assert.True(gap >= 4,
                    $"the mark needs a readable gap before the label, saw {gap}: mark at {glyphAt} width " +
                    $"{markedGlyph.Bounds.Width}, label at {labelAt}");

                var glyphOnly = items.Single(item => Equals(item.Header, "GlyphOnly"));
                Assert.True(glyphOnly.IconOnly);
                Assert.False(glyphOnly.GetVisualDescendants().OfType<TextBlock>()
                    .Any(text => text.Text == "GlyphOnly" && text.IsVisible),
                    "an icon-only item is exactly the case where the label is replaced by the mark");

                var both = items.Single(item => Equals(item.Header, "Both"));
                Assert.Equal("⚖️", both.IconGlyph);
                Assert.Contains(both.GetVisualDescendants().OfType<TextBlock>(), text => text.IsVisible && text.Text == "⚖️");
                Assert.DoesNotContain(both.GetVisualDescendants().OfType<Image>(), image => image.IsVisible);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task A_menu_glyph_follows_the_authored_size_and_a_text_run_that_is_not_a_mark_is_refused()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var registry = new CuiControlRegistry();
            using var loader = new CuiControlLoader(registry);
            var sized = loader.LoadMarkup("<Container Type='Menu'>" +
                "<Button Type='Menu Item' Content='Sized' IconGlyph='★' IconSize='30'/>" +
                "</Container>");
            Assert.Empty(sized.Diagnostics);
            var window = new Window { Content = sized.Root, Width = 300, Height = 100 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var item = sized.Root!.GetVisualDescendants().OfType<CuiMenuItem>()
                    .Single(menu => Equals(menu.Header, "Sized"));
                var star = item.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "★");
                Assert.Equal(30, star.Bounds.Width, 2);
            }
            finally
            {
                window.Close();
            }

            // The icon box holds a mark, not a phrase: an authored run longer than a mark is reported at load.
            var rejected = new CuiControlLoader(new CuiControlRegistry()).LoadMarkup(
                "<Container Type='Menu'><Button Type='Menu Item' Content='Sentence' " +
                "IconGlyph='🙂🙂🙂🙂🙂🙂🙂🙂🙂🙂🙂🙂🙂🙂🙂🙂'/></Container>");
            Assert.True(rejected.Diagnostics.Any(diagnostic => diagnostic.Code == "CUIR053"),
                "a sentence in the icon slot has to be reported, not drawn");
        }, CancellationToken.None);
    }
}
